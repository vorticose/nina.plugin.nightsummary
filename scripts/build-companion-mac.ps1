# Builds the macOS companion app bundle and zips it for distribution.
#
# Produces: build/companion-mac/NightSummaryCompanion-mac-<arch>.zip
#
# The .app bundle uses LSUIElement=true so it runs as a background agent (no
# dock icon, no menu bar). Combined with the first-run auto-open-browser logic
# in Program.cs, the user experience is: drag .app to Applications, double-click,
# browser tab opens with the setup wizard. To stop the companion before
# install-service ships, use Activity Monitor.
#
# Usage:
#   .\scripts\build-companion-mac.ps1               # builds arm64 (default)
#   .\scripts\build-companion-mac.ps1 -Arch x64     # Intel Mac build
#   .\scripts\build-companion-mac.ps1 -Arch both    # both arches
#
# Requires: pwsh 7+ (works on Windows + macOS + Linux). Cannot produce a .dmg
# from a non-macOS dev box because hdiutil is mac-only -- the zipped .app
# bundle gives the same drag-to-Applications experience for now. CI on
# macos-latest runners will add the .dmg later.

[CmdletBinding()]
param(
    [ValidateSet('arm64', 'x64', 'both')]
    [string]$Arch = 'arm64'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$buildDir = Join-Path $repoRoot 'build/companion-mac'
$projPath = Join-Path $repoRoot 'NINA.Plugin.NightSummary.Companion/NINA.Plugin.NightSummary.Companion.csproj'

if (-not (Test-Path $buildDir)) { New-Item -ItemType Directory -Path $buildDir -Force | Out-Null }

# Derive the version string from the companion csproj so the Info.plist matches
# what `NightSummaryCompanion --version` reports. Falls back to 0.0.0 when the
# build hasn't computed it yet (first run before any dotnet build invocation).
function Get-CompanionVersion {
    # The dashboard csproj reads VersionPrefix via the parent plugin csproj
    # SetGitBuildNumber target. Read the plugin's <VersionPrefix> directly --
    # companion lockstep-versions with the plugin per project memory.
    $pluginCsproj = Join-Path $repoRoot 'NINA.Plugin.NightSummary/NINA.Plugin.NightSummary.csproj'
    if (Test-Path $pluginCsproj) {
        $xml = [xml](Get-Content $pluginCsproj -Raw)
        $vp = $xml.Project.PropertyGroup.VersionPrefix | Where-Object { $_ } | Select-Object -First 1
        if ($vp) { return $vp }
    }
    return '0.0.0'
}

function Build-Arch {
    param([string]$Rid)

    $archLabel = if ($Rid -eq 'osx-arm64') { 'arm64' } else { 'x64' }
    $version   = Get-CompanionVersion

    Write-Host ""
    Write-Host "=== Building NightSummaryCompanion.app ($Rid, v$version) ===" -ForegroundColor Cyan

    # 1. Publish self-contained single-file binary
    $publishDir = Join-Path $repoRoot "NINA.Plugin.NightSummary.Companion/bin/Release/net8.0/$Rid/publish"
    & dotnet publish $projPath `
        -c Release `
        -r $Rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        --nologo `
        -v quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $Rid (exit $LASTEXITCODE)" }
    if (-not (Test-Path "$publishDir/NightSummaryCompanion")) {
        throw "publish output missing: $publishDir/NightSummaryCompanion"
    }

    # 2. Assemble .app bundle structure under build/companion-mac/staging-<arch>/
    $staging = Join-Path $buildDir "staging-$archLabel"
    if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
    $appRoot  = Join-Path $staging 'NightSummaryCompanion.app'
    $contents = Join-Path $appRoot 'Contents'
    $macOs    = Join-Path $contents 'MacOS'
    New-Item -ItemType Directory -Path $macOs -Force | Out-Null

    # Copy the real binary under a -bin suffix so the launcher script can
    # carry the canonical CFBundleExecutable name (NightSummaryCompanion).
    # macOS doesn't care that the actual executable is a shell script; the
    # script runs the real binary in a loop and respawns it on exit code 88
    # (Dashboard "Restart" button) or stops on exit code 0 (Dashboard "Quit").
    Copy-Item "$publishDir/NightSummaryCompanion"    "$macOs/NightSummaryCompanion-bin"
    # Natives (libe_sqlite3, libSkiaSharp) are baked INTO the binary via
    # IncludeNativeLibrariesForSelfExtract, so there are no sibling dylibs to copy
    # into the bundle -- the -bin is fully self-contained.

    # Watchdog launcher script. macOS treats this as the bundle's executable
    # (CFBundleExecutable=NightSummaryCompanion). The script:
    #   - resolves its own directory so relative paths work regardless of cwd
    #     (LaunchServices launches with cwd=/)
    #   - loops, running the real binary
    #   - exit 88 from binary  -> respawn (Dashboard "Restart" hit)
    #   - exit 0  from binary  -> break and stop (Dashboard "Quit" or clean shutdown)
    #   - any other exit       -> log and stop (don't spin on a crash loop)
    $watchdog = @'
#!/bin/bash
# NightSummaryCompanion launcher + watchdog.
# Respawns the binary on exit code 88 (dashboard Restart), exits on 0 (Quit).
DIR="$(cd "$(dirname "$0")" && pwd)"
BIN="$DIR/NightSummaryCompanion-bin"
while :; do
    "$BIN" "$@"
    code=$?
    case $code in
        88)
            # Restart requested via dashboard. Small breather so the OS
            # releases the TCP port before the next bind attempt.
            sleep 1
            ;;
        0)
            # Clean quit. Stop the loop and exit the .app process group.
            exit 0
            ;;
        *)
            # Crash / unexpected exit. Don't spin -- propagate the code so
            # the user can see "it died" in Console.app or via `open -W`.
            exit $code
            ;;
    esac
done
'@
    $watchdogPath = Join-Path $macOs 'NightSummaryCompanion'
    # PowerShell on Windows writes CRLF by default which bash chokes on.
    # Use [System.IO.File]::WriteAllText with UTF8NoBOM + explicit LF.
    [System.IO.File]::WriteAllText($watchdogPath,
        ($watchdog -replace "`r`n", "`n"),
        (New-Object System.Text.UTF8Encoding $false))

    # 2b. App icon. Drop the committed .icns into Contents/Resources and point
    # CFBundleIconFile at it (below). LSUIElement=true means no Dock icon, but
    # the .icns is what Finder shows for the .app and what appears in System
    # Settings -> General -> Login Items / "Allow in the Background".
    $resources = Join-Path $contents 'Resources'
    New-Item -ItemType Directory -Path $resources -Force | Out-Null
    $icnsSrc = Join-Path $repoRoot 'assets/companion-icon/companion.icns'
    if (Test-Path $icnsSrc) {
        Copy-Item $icnsSrc (Join-Path $resources 'companion.icns')
    } else {
        Write-Host "  WARNING: $icnsSrc missing -- bundle will use the generic app icon" -ForegroundColor Yellow
    }

    # 3. Info.plist
    #
    # LSUIElement=true makes the app a UI Agent -- no dock icon, no menu bar.
    # That matches what the companion is: a headless local server. Combined
    # with the first-run auto-open-browser logic, the user double-clicks the
    # app and the browser shows the setup wizard with no visible app chrome.
    #
    # CFBundleIdentifier follows the reverse-DNS convention NINA itself uses.
    # CFBundleVersion stays in lockstep with the plugin version so update
    # checks comparing against GitHub Releases tags work without translation.
    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>                  <string>Night Summary Companion</string>
    <key>CFBundleDisplayName</key>           <string>Night Summary Companion</string>
    <key>CFBundleIdentifier</key>            <string>com.vorticose.nightsummary.companion</string>
    <key>CFBundleVersion</key>               <string>$version</string>
    <key>CFBundleShortVersionString</key>    <string>$version</string>
    <key>CFBundleExecutable</key>            <string>NightSummaryCompanion</string>
    <key>CFBundleIconFile</key>              <string>companion</string>
    <key>CFBundlePackageType</key>           <string>APPL</string>
    <key>CFBundleInfoDictionaryVersion</key> <string>6.0</string>
    <key>LSUIElement</key>                   <true/>
    <key>LSMinimumSystemVersion</key>        <string>11.0</string>
    <key>NSHighResolutionCapable</key>       <true/>
</dict>
</plist>
"@
    Set-Content -Path (Join-Path $contents 'Info.plist') -Value $plist -Encoding UTF8 -NoNewline

    # 4. Code-sign the bundle ad-hoc when codesign is available (macOS only — a
    # CI macos runner or the Mac mini). A signed bundle means the user just
    # right-click->Opens once on a downloaded copy; no Fix Permissions step.
    # Cross-building on Windows we cannot sign (codesign is mac-only), so we fall
    # back to shipping Fix Permissions.command for the user to ad-hoc sign there.
    # Ad-hoc only — no Developer ID / notarization, so no Apple account needed.
    $signed = $false
    if (Get-Command codesign -ErrorAction SilentlyContinue) {
        Write-Host "  codesigning bundle (ad-hoc)..."
        & codesign --force --deep --sign - $appRoot
        if ($LASTEXITCODE -eq 0) {
            & codesign --verify --deep --strict $appRoot 2>$null
            $signed = ($LASTEXITCODE -eq 0)
        }
        Write-Host ("  bundle signed: " + $signed)
    } else {
        Write-Host "  codesign unavailable (cross-build) — shipping Fix Permissions.command"
    }

    # Fix Permissions.command is only needed for UNSIGNED (Windows-cross-built)
    # bundles. Signed bundles skip it entirely.
    if (-not $signed) {
        $fixCmd = @"
#!/bin/bash
# Run once after installing NightSummaryCompanion.app. Ad-hoc signs the
# binary so the arm64 kernel will let it run. Safe to run multiple times.
set -e
APP="/Applications/NightSummaryCompanion.app"
if [ ! -d "`$APP" ]; then
    echo "NightSummaryCompanion.app not found in /Applications."
    echo "Drag it there first, then run this script again."
    read -p "Press Enter to close..."
    exit 1
fi
echo "Fixing permissions on `$APP ..."
chmod +x "`$APP/Contents/MacOS/"*
codesign --force --deep --sign - "`$APP"
echo ""
echo "Done. You can close this window."
echo "Double-click NightSummaryCompanion.app to launch."
read -p "Press Enter to close..."
"@
        $fixPath = Join-Path $staging 'Fix Permissions.command'
        Set-Content -Path $fixPath -Value $fixCmd -Encoding UTF8 -NoNewline
    }

    # 5. Build a .tar.gz instead of .zip. Tar preserves Unix mode bits by spec
    # (POSIX-1.2001 / pax) so macOS Archive Utility unpacks the binary with
    # 0755 intact. PowerShell's Compress-Archive emits Windows-style zips
    # that drop the exec bit -- tested, doesn't work on Mac without a follow-
    # up chmod step. macOS' Finder happily double-clicks .tar.gz and extracts
    # in place via Archive Utility.
    $tarName = "NightSummaryCompanion-mac-$archLabel.tar.gz"
    $tarPath = Join-Path $buildDir $tarName
    if (Test-Path $tarPath) { Remove-Item $tarPath }

    # Unix mode bits per POSIX tar:
    #   0o755 = rwxr-xr-x
    #   0o644 = rw-r--r--
    #   0o755 = rwxr-xr-x  (dirs)
    $execMode = [int]([Convert]::ToInt32('755', 8))
    $fileMode = [int]([Convert]::ToInt32('644', 8))
    $dirMode  = [int]([Convert]::ToInt32('755', 8))
    $cmdMode  = [int]([Convert]::ToInt32('755', 8))  # the .command file

    $stagingFull = (Resolve-Path $staging).Path
    $gz = [System.IO.File]::Open($tarPath, [System.IO.FileMode]::CreateNew)
    try {
        $gzStream = New-Object System.IO.Compression.GZipStream(
            $gz, [System.IO.Compression.CompressionLevel]::Optimal)
        try {
            $writer = New-Object System.Formats.Tar.TarWriter(
                $gzStream, [System.Formats.Tar.TarEntryFormat]::Pax, $true)
            try {
                # Stable enumeration: directories first via Get-ChildItem -Recurse.
                $entries = Get-ChildItem -Path $staging -Recurse -Force | Sort-Object FullName
                foreach ($item in $entries) {
                    $relative = $item.FullName.Substring($stagingFull.Length + 1).Replace('\', '/')

                    if ($item.PSIsContainer) {
                        $entry = New-Object System.Formats.Tar.PaxTarEntry(
                            [System.Formats.Tar.TarEntryType]::Directory, "$relative/")
                        $entry.Mode = [System.IO.UnixFileMode]$dirMode
                        $writer.WriteEntry($entry)
                    } else {
                        # Mode picker:
                        #   .app binary + dylib -> exec
                        #   .command helper      -> exec
                        #   everything else      -> 644
                        $mode = $fileMode
                        if ($relative -like 'NightSummaryCompanion.app/Contents/MacOS/*') {
                            $mode = $execMode
                        } elseif ($relative -like '*.command') {
                            $mode = $cmdMode
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
}

switch ($Arch) {
    'arm64' { Build-Arch 'osx-arm64' }
    'x64'   { Build-Arch 'osx-x64' }
    'both'  {
        Build-Arch 'osx-arm64'
        Build-Arch 'osx-x64'
    }
}

Write-Host ""
Write-Host "Done. Artifacts in $buildDir" -ForegroundColor Cyan
$macSigned = [bool](Get-Command codesign -ErrorAction SilentlyContinue)
Write-Host ""
Write-Host "To install on a fresh Mac:" -ForegroundColor Yellow
Write-Host "  1. Transfer the .tar.gz to the Mac (AirDrop / scp / download)."
Write-Host "  2. Double-click the .tar.gz in Finder. Archive Utility extracts a folder."
Write-Host "  3. Drag NightSummaryCompanion.app into /Applications."
if ($macSigned) {
    Write-Host "  4. Open it. A scp'd/AirDropped copy double-clicks straight away; a"
    Write-Host "     browser-DOWNLOADED copy is quarantined -> right-click -> Open once"
    Write-Host "     ('unidentified developer', not 'damaged', because the bundle is signed)."
    Write-Host "  5. Setup wizard opens in your default browser. Done."
    Write-Host ""
    Write-Host "Bundle is ad-hoc signed (no Apple account). Notarization would remove the" -ForegroundColor DarkGray
    Write-Host "right-click step but needs a paid dev account -- out of scope by policy."  -ForegroundColor DarkGray
} else {
    Write-Host "  4. Double-click 'Fix Permissions.command' (one-time ad-hoc codesign)."
    Write-Host "  5. Right-click NightSummaryCompanion.app -> Open. Gatekeeper warns once; click Open."
    Write-Host "  6. Setup wizard opens in your default browser. Done."
    Write-Host ""
    Write-Host "This is an UNSIGNED cross-build (codesign is mac-only). Build on a Mac /"  -ForegroundColor DarkGray
    Write-Host "the CI macos runner to sign the bundle and drop steps 4-5 to one click."   -ForegroundColor DarkGray
}
