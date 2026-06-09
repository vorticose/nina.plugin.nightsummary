# Builds the Linux companion distribution and tars it.
#
# Produces: build/companion-linux/NightSummaryCompanion-linux-<arch>.tar.gz
#
# Layout inside the tar.gz:
#   NightSummaryCompanion/
#     NightSummaryCompanion-bin        <- self-contained .NET binary (natives baked in)
#     NightSummaryCompanion            <- bash watchdog (respawn on exit 88, quit on 0)
#     companion.png                    <- app-menu / launcher icon (256px)
#     nightsummary-companion.desktop   <- desktop-entry template (@DIR@ placeholder)
#     install.sh                       <- prereqs + registers .desktop + systemd setup
#     nightsummary-companion.service   <- systemd --user unit template (@DIR@ placeholder)
#     README.txt
#
# Mirrors build-companion-mac.ps1: single-file self-contained publish, native
# libs alongside, the same bash watchdog (exit 88 = Dashboard "Restart", exit 0
# = "Quit"). Uses a Pax TarWriter so Unix exec bits survive (PowerShell
# Compress-Archive would drop them).
#
# Runs on any pwsh 7+ box (cross-publishes to linux-x64 from Windows). The
# resulting binary RUNS on Linux only -- smoke-test under WSL/Docker/a real box.
#
# Usage:
#   .\scripts\build-companion-linux.ps1          # x64 (default)
#
# Linux runtime prereqs (SkiaSharp): libfontconfig1 + libfreetype6. install.sh
# detects + prints the apt command.

[CmdletBinding()]
param(
    [ValidateSet('x64')]
    [string]$Arch = 'x64'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$buildDir = Join-Path $repoRoot 'build/companion-linux'
$projPath = Join-Path $repoRoot 'NINA.Plugin.NightSummary.Companion/NINA.Plugin.NightSummary.Companion.csproj'

if (-not (Test-Path $buildDir)) { New-Item -ItemType Directory -Path $buildDir -Force | Out-Null }

function Get-CompanionVersion {
    $pluginCsproj = Join-Path $repoRoot 'NINA.Plugin.NightSummary/NINA.Plugin.NightSummary.csproj'
    if (Test-Path $pluginCsproj) {
        $xml = [xml](Get-Content $pluginCsproj -Raw)
        $vp = $xml.Project.PropertyGroup.VersionPrefix | Where-Object { $_ } | Select-Object -First 1
        if ($vp) { return $vp }
    }
    return '0.0.0'
}

$rid     = "linux-$Arch"
$version = Get-CompanionVersion

Write-Host ""
Write-Host "=== Building NightSummaryCompanion ($rid, v$version) ===" -ForegroundColor Cyan

# 1. Publish self-contained single-file binary; natives stay external.
#    Clean the RID output first -- an incremental single-file publish is flaky
#    about re-externalizing native libs (a stale "up to date" publish can drop
#    libSkiaSharp.so / libe_sqlite3.so from the publish root), so always publish
#    from scratch.
$ridOut = Join-Path $repoRoot "NINA.Plugin.NightSummary.Companion/bin/Release/net8.0/$rid"
if (Test-Path $ridOut) { Remove-Item -Recurse -Force $ridOut }
$publishDir = Join-Path $ridOut 'publish'
& dotnet publish $projPath `
    -c Release `
    -r $rid `
    --self-contained true `
    -p:PublishSingleFile=true `
    --nologo `
    -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid (exit $LASTEXITCODE)" }
if (-not (Test-Path "$publishDir/NightSummaryCompanion")) {
    throw "publish output missing: $publishDir/NightSummaryCompanion"
}

# 2. Stage the distribution folder.
$staging = Join-Path $buildDir "staging-$Arch"
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
$appDir = Join-Path $staging 'NightSummaryCompanion'
New-Item -ItemType Directory -Path $appDir -Force | Out-Null

Copy-Item "$publishDir/NightSummaryCompanion" "$appDir/NightSummaryCompanion-bin"
# Natives (libe_sqlite3.so + libSkiaSharp.so) are baked INTO the binary via
# IncludeNativeLibrariesForSelfExtract, so there are no sibling .so files to copy
# -- the -bin is fully self-contained (system libfontconfig1/libfreetype6 are
# still required at runtime; install.sh checks for them).

# App icon (PNG). install.sh writes a .desktop entry that points Icon= at this
# file by absolute path, so it shows in the app menu / launcher with no icon-
# theme install needed.
$pngSrc = Join-Path $repoRoot 'assets/companion-icon/companion-256.png'
if (Test-Path $pngSrc) {
    Copy-Item $pngSrc (Join-Path $appDir 'companion.png')
} else {
    Write-Host "  WARNING: $pngSrc missing -- .desktop will have no icon" -ForegroundColor Yellow
}

# 3. Bash watchdog launcher -- identical contract to the macOS bundle launcher.
$watchdog = @'
#!/bin/bash
# NightSummaryCompanion launcher + watchdog.
# Respawns the binary on exit code 88 (dashboard Restart), exits on 0 (Quit).
# readlink -f so a symlink to this script (e.g. the .deb's /usr/bin entry ->
# /opt/.../NightSummaryCompanion) still resolves the binary next to the REAL
# script, not next to the symlink.
DIR="$(cd "$(dirname "$(readlink -f "$0")")" && pwd)"
BIN="$DIR/NightSummaryCompanion-bin"
while :; do
    "$BIN" "$@"
    code=$?
    case $code in
        88) sleep 1 ;;          # Dashboard "Restart": brief pause, respawn.
        0)  exit 0 ;;           # Dashboard "Quit" / clean shutdown.
        *)  exit $code ;;       # Crash: propagate so systemd / user sees it.
    esac
done
'@
[System.IO.File]::WriteAllText((Join-Path $appDir 'NightSummaryCompanion'),
    ($watchdog -replace "`r`n", "`n"),
    (New-Object System.Text.UTF8Encoding $false))

# 4. systemd --user unit template. @DIR@ is substituted by install.sh at install
#    time (the install dir isn't known until the user unpacks it).
$unit = @'
[Unit]
Description=Night Summary Companion dashboard
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
WorkingDirectory=@DIR@
ExecStart=@DIR@/NightSummaryCompanion serve
Restart=on-failure
RestartSec=5

[Install]
WantedBy=default.target
'@
[System.IO.File]::WriteAllText((Join-Path $appDir 'nightsummary-companion.service'),
    ($unit -replace "`r`n", "`n"),
    (New-Object System.Text.UTF8Encoding $false))

# 4b. Desktop entry template. @DIR@ -> install dir (install.sh substitutes).
#     Exec runs the bash watchdog so the dashboard Restart/Quit contract holds.
$desktop = @'
[Desktop Entry]
Type=Application
Name=Night Summary Companion
Comment=Local dashboard mirroring your Night Summary imaging history
Exec=@DIR@/NightSummaryCompanion serve
Icon=@DIR@/companion.png
Terminal=false
Categories=Utility;Network;
StartupNotify=false
'@
[System.IO.File]::WriteAllText((Join-Path $appDir 'nightsummary-companion.desktop'),
    ($desktop -replace "`r`n", "`n"),
    (New-Object System.Text.UTF8Encoding $false))

# 5. install.sh -- prereq check + run/autostart instructions.
$install = @'
#!/bin/bash
# Night Summary Companion - Linux setup helper. Safe to run multiple times.
set -e
DIR="$(cd "$(dirname "$0")" && pwd)"

echo "Night Summary Companion installer"
echo

# SkiaSharp needs fontconfig + freetype at runtime. Desktops usually have them;
# headless servers usually do not.
MISSING=""
ldconfig -p 2>/dev/null | grep -q libfontconfig || MISSING="$MISSING libfontconfig1"
ldconfig -p 2>/dev/null | grep -q libfreetype   || MISSING="$MISSING libfreetype6"
if [ -n "$MISSING" ]; then
    echo "!! Missing runtime libraries:$MISSING"
    echo "   Install with:  sudo apt-get update && sudo apt-get install -y$MISSING"
    echo "   (companion will fail to render report thumbnails without them)"
    echo
fi

chmod +x "$DIR/NightSummaryCompanion" "$DIR/NightSummaryCompanion-bin"

# Register the desktop entry so the companion shows up in the app menu /
# launcher with its icon. User-scoped (~/.local), no root needed. Re-runnable.
if [ -f "$DIR/nightsummary-companion.desktop" ]; then
    APPS_DIR="$HOME/.local/share/applications"
    mkdir -p "$APPS_DIR"
    sed "s|@DIR@|$DIR|g" "$DIR/nightsummary-companion.desktop" > "$APPS_DIR/nightsummary-companion.desktop"
    chmod +x "$APPS_DIR/nightsummary-companion.desktop" 2>/dev/null || true
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$APPS_DIR" 2>/dev/null || true
    echo "Installed app-menu entry: $APPS_DIR/nightsummary-companion.desktop"
    echo
fi

echo "Run now:"
echo "  $DIR/NightSummaryCompanion serve"
echo
echo "Autostart at login (systemd --user):"
echo "  mkdir -p ~/.config/systemd/user"
echo "  sed \"s|@DIR@|$DIR|g\" \"$DIR/nightsummary-companion.service\" > ~/.config/systemd/user/nightsummary-companion.service"
echo "  systemctl --user daemon-reload"
echo "  systemctl --user enable --now nightsummary-companion"
echo "  loginctl enable-linger \"$USER\"   # keep running when logged out"
'@
[System.IO.File]::WriteAllText((Join-Path $appDir 'install.sh'),
    ($install -replace "`r`n", "`n"),
    (New-Object System.Text.UTF8Encoding $false))

# 6. README.
$readme = @"
Night Summary Companion (Linux $Arch) - v$version

A local web dashboard mirroring your Night Summary imaging history. Runs a small
web server on http://localhost:8182/.

QUICK START
  tar -xzf NightSummaryCompanion-linux-$Arch.tar.gz
  cd NightSummaryCompanion
  ./install.sh                 # checks prereqs, prints autostart steps
  ./NightSummaryCompanion serve

PREREQS (SkiaSharp): libfontconfig1 libfreetype6
  sudo apt-get update && sudo apt-get install -y libfontconfig1 libfreetype6

The first run opens the setup wizard in your browser (xdg-open). Pair it with
your primary (NINA) machine there.

CONFIG
  Config + synced data live in ~/.local/share/NightSummaryCompanion (NOT next to
  the binary), so replacing the binary on update never loses your settings/history.

AUTOSTART AT LOGIN
  Easiest: turn on 'Start at login' in the dashboard (Settings -> Start at login).
  Headless / manual: install.sh prints the systemd --user steps.

STOP / RESTART
  Use the dashboard Quit / Restart buttons (Settings -> Companion process), or
  manage the systemd --user service if you enabled it.
"@
[System.IO.File]::WriteAllText((Join-Path $appDir 'README.txt'),
    ($readme -replace "`r`n", "`n"),
    (New-Object System.Text.UTF8Encoding $false))

# 7. tar.gz with Unix mode bits preserved (Pax TarWriter). Exec for the binary,
#    the watchdog, and the .sh; 644 for libs/unit/readme.
$tarName = "NightSummaryCompanion-linux-$Arch.tar.gz"
$tarPath = Join-Path $buildDir $tarName
if (Test-Path $tarPath) { Remove-Item $tarPath }

$execMode = [int]([Convert]::ToInt32('755', 8))
$fileMode = [int]([Convert]::ToInt32('644', 8))
$dirMode  = [int]([Convert]::ToInt32('755', 8))

$stagingFull = (Resolve-Path $staging).Path
$gz = [System.IO.File]::Open($tarPath, [System.IO.FileMode]::CreateNew)
try {
    $gzStream = New-Object System.IO.Compression.GZipStream(
        $gz, [System.IO.Compression.CompressionLevel]::Optimal)
    try {
        $writer = New-Object System.Formats.Tar.TarWriter(
            $gzStream, [System.Formats.Tar.TarEntryFormat]::Pax, $true)
        try {
            $entries = Get-ChildItem -Path $staging -Recurse -Force | Sort-Object FullName
            foreach ($item in $entries) {
                $relative = $item.FullName.Substring($stagingFull.Length + 1).Replace('\', '/')
                if ($item.PSIsContainer) {
                    $entry = New-Object System.Formats.Tar.PaxTarEntry(
                        [System.Formats.Tar.TarEntryType]::Directory, "$relative/")
                    $entry.Mode = [System.IO.UnixFileMode]$dirMode
                    $writer.WriteEntry($entry)
                } else {
                    # Exec for: the binary, the watchdog (no extension), and *.sh.
                    $name = $item.Name
                    $mode = $fileMode
                    if ($name -eq 'NightSummaryCompanion-bin' -or
                        $name -eq 'NightSummaryCompanion' -or
                        $name -like '*.sh') {
                        $mode = $execMode
                    }
                    $entry = New-Object System.Formats.Tar.PaxTarEntry(
                        [System.Formats.Tar.TarEntryType]::RegularFile, $relative)
                    $entry.Mode = [System.IO.UnixFileMode]$mode
                    $srcStream = [System.IO.File]::OpenRead($item.FullName)
                    try { $entry.DataStream = $srcStream; $writer.WriteEntry($entry) }
                    finally { $srcStream.Dispose() }
                }
            }
        } finally { $writer.Dispose() }
    } finally { $gzStream.Dispose() }
} finally { $gz.Dispose() }

$tarMb = [math]::Round((Get-Item $tarPath).Length / 1MB, 1)
Write-Host "  -> $tarPath ($tarMb MB, exec bits preserved)" -ForegroundColor Green
Write-Host ""
Write-Host "Done. Artifact in $buildDir" -ForegroundColor Cyan
Write-Host "Smoke-test on Linux (WSL/Docker/real box):" -ForegroundColor Yellow
Write-Host "  tar -xzf $tarName && cd NightSummaryCompanion && ./install.sh && ./NightSummaryCompanion serve"
