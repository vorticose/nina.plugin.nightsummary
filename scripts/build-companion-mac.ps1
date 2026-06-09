# Builds the macOS companion app bundle and zips it for distribution.
#
# Produces: build/companion-mac/NightSummaryCompanion-mac-<arch>.dmg (on macOS via
#           hdiutil), or a .tar.gz fallback for cross-builds where hdiutil is absent.
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
# Requires: pwsh 7+ (works on Windows + macOS + Linux). The .dmg is built only
# where hdiutil exists (a Mac / the CI macos runner); a cross-build on Windows or
# Linux falls back to the .tar.gz. The CI macos-latest runner is what produces the
# signed .app + .dmg the release ships (it also cross-builds osx-x64 for Intel).

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

    # TWO scripts, deliberately split:
    #
    #   NightSummaryCompanion           = the bundle's CFBundleExecutable. A thin
    #                                     LAUNCHER that detaches the watchdog and
    #                                     EXITS IMMEDIATELY.
    #   NightSummaryCompanion-watchdog  = the detached background loop that runs
    #                                     the real binary and respawns it.
    #
    # Why split: macOS LaunchServices treats a Finder click on an already-
    # "running" .app as a "reopen" AppleEvent. A headless agent has no run loop
    # to answer it, so the reopen times out (-1712) and the click does nothing —
    # e.g. the user closes the dashboard tab, clicks the app to get it back, and
    # nothing happens. Because the launcher exits at once, LaunchServices never
    # sees this .app as "running", so EVERY click launches it fresh; the binary's
    # own probe ("is a companion already serving? then just open the dashboard")
    # then runs every time and the tab reliably reappears. Windows always spawns
    # a fresh process per double-click and Linux launches fresh too, so this
    # launcher trick is macOS-only.
    $launcher = @'
#!/bin/bash
# NightSummaryCompanion launcher (the .app's CFBundleExecutable). Detaches the
# real server as a background watchdog, then exits immediately so macOS never
# considers this .app "running" — that way every Finder click launches us fresh
# and reliably opens the dashboard instead of sending a dead-end reopen event.
DIR="$(cd "$(dirname "$0")" && pwd)"
nohup "$DIR/NightSummaryCompanion-watchdog" "$@" >/dev/null 2>&1 &
exit 0
'@
    $watchdog = @'
#!/bin/bash
# NightSummaryCompanion watchdog (detached background loop).
# Respawns the binary on exit code 88 (dashboard Restart), exits on 0 (Quit).
DIR="$(cd "$(dirname "$0")" && pwd)"
BIN="$DIR/NightSummaryCompanion-bin"
while :; do
    "$BIN" "$@"
    code=$?
    case $code in
        88) sleep 1 ;;     # Restart: let the OS free the TCP port, then respawn
        0)  exit 0 ;;      # Clean Quit: stop the loop
        *)  exit $code ;;  # Crash: don't spin; surface the code (Console.app / open -W)
    esac
done
'@
    # PowerShell on Windows writes CRLF by default which bash chokes on.
    # Use [System.IO.File]::WriteAllText with UTF8NoBOM + explicit LF.
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText((Join-Path $macOs 'NightSummaryCompanion'),
        ($launcher -replace "`r`n", "`n"), $utf8NoBom)
    [System.IO.File]::WriteAllText((Join-Path $macOs 'NightSummaryCompanion-watchdog'),
        ($watchdog -replace "`r`n", "`n"), $utf8NoBom)

    # WriteAllText creates these scripts 0644. The .tar.gz path sets exec bits in
    # the tar entries, but the .dmg path uses ditto, which copies on-disk perms
    # verbatim -- so the launcher (CFBundleExecutable) and watchdog MUST be made
    # executable ON DISK here, or the installed .app fails to open ("can't be
    # opened"). chmod exists wherever the .dmg is built (a Mac); on a Windows
    # cross-build it's absent and the tar entries carry the modes instead.
    if (Get-Command chmod -ErrorAction SilentlyContinue) {
        & chmod +x (Join-Path $macOs 'NightSummaryCompanion') `
                   (Join-Path $macOs 'NightSummaryCompanion-watchdog') `
                   (Join-Path $macOs 'NightSummaryCompanion-bin')
    }

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

    # 5. README.txt -- install + Gatekeeper (right-click Open / Sequoia fallback),
    #    config location, autostart, stop/restart. Lands in the .tar.gz and .dmg.
    $macReadme = @"
Night Summary Companion (macOS $archLabel) - v$version

A local web dashboard that mirrors your Night Summary imaging history from the
primary (NINA) machine. Runs a small web server on a configurable localhost port.

INSTALL
  1. Drag NightSummaryCompanion.app into the Applications folder.
  2. First launch on a downloaded copy: right-click the app -> Open, then click
     Open in the dialog. The app is ad-hoc signed (not notarized -- no paid Apple
     account, by design), so macOS says 'unidentified developer', not 'damaged'.
     macOS 15 (Sequoia): if right-click -> Open does not offer Open, go to
     System Settings -> Privacy & Security -> scroll down -> Open Anyway.
  3. A browser tab opens to the setup wizard. Pair it with your primary machine.

  Config + synced data live in
  ~/Library/Application Support/NightSummaryCompanion (NOT inside the .app), so
  replacing the app on update never loses your settings or history.

AUTOSTART AT LOGIN
  Turn on 'Start at login' in the dashboard (Settings -> Start at login).

STOP / RESTART
  - Stop: the dashboard Quit button, or quit NightSummaryCompanion in Activity Monitor.
  - The dashboard Restart button relaunches it automatically.
  - It runs as a background agent (no Dock icon). Click the app again to reopen
    the dashboard in your browser.
"@
    [System.IO.File]::WriteAllText((Join-Path $staging 'README.txt'),
        ($macReadme -replace "`r`n", "`n"),
        (New-Object System.Text.UTF8Encoding $false))

    # 6. Package. On macOS (hdiutil present) the deliverable is a .dmg; a cross-
    #    build (Windows/Linux, no hdiutil) ships a .tar.gz fallback. Build ONE, not
    #    both: the macos runner has limited disk and the x64 cross-build pulls an
    #    extra runtime pack, so duplicating ~100 MB tips hdiutil into "No space
    #    left on device". Free the whole RID build dir (publish + intermediates)
    #    first -- the -bin is already in the bundle, so none of it is needed now.
    $ridDir = Split-Path -Parent $publishDir   # .../bin/Release/net8.0/osx-<arch>
    if (Test-Path $ridDir) { Remove-Item -Recurse -Force $ridDir }

    if (Get-Command hdiutil -ErrorAction SilentlyContinue) {
        # .dmg imaged straight from the staging dir, which already holds the
        # signed .app + README; just add an /Applications symlink for drag-to-
        # install. Imaging $staging in place avoids a second ~90 MB copy of the
        # .app -- the macos runner is disk-tight (the x64 cross-build pulls an
        # extra runtime pack and was hitting "No space left on device"). hdiutil
        # preserves the .app's code signature + exec bits when it images the folder.
        $dmgName  = "NightSummaryCompanion-mac-$archLabel.dmg"
        $dmgPath  = Join-Path $buildDir $dmgName
        if (Test-Path $dmgPath) { Remove-Item $dmgPath }
        $appsLink = Join-Path $staging 'Applications'
        if (-not (Test-Path $appsLink)) { & ln -s /Applications $appsLink }

        & hdiutil create -volname "Night Summary Companion" -srcfolder "$staging" -ov -format UDZO "$dmgPath" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "hdiutil create failed (exit $LASTEXITCODE)" }
        $dmgMb = [math]::Round((Get-Item $dmgPath).Length / 1MB, 1)
        Write-Host "  -> $dmgPath ($dmgMb MB)" -ForegroundColor Green
    } else {
        # .tar.gz fallback (cross-build). Pax tar preserves Unix mode bits
        # (Compress-Archive drops the exec bit); macOS Finder double-clicks it.
        $tarName = "NightSummaryCompanion-mac-$archLabel.tar.gz"
        $tarPath = Join-Path $buildDir $tarName
        if (Test-Path $tarPath) { Remove-Item $tarPath }

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
                    $entries = Get-ChildItem -Path $staging -Recurse -Force | Sort-Object FullName
                    foreach ($item in $entries) {
                        $relative = $item.FullName.Substring($stagingFull.Length + 1).Replace('\', '/')
                        if ($item.PSIsContainer) {
                            $entry = New-Object System.Formats.Tar.PaxTarEntry(
                                [System.Formats.Tar.TarEntryType]::Directory, "$relative/")
                            $entry.Mode = [System.IO.UnixFileMode]$dirMode
                            $writer.WriteEntry($entry)
                        } else {
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
$macDmg    = [bool](Get-Command hdiutil  -ErrorAction SilentlyContinue)
Write-Host ""
Write-Host "To install on a fresh Mac:" -ForegroundColor Yellow
if ($macDmg) {
    Write-Host "  1. Transfer the .dmg to the Mac (AirDrop / scp / download)."
    Write-Host "  2. Open the .dmg and drag NightSummaryCompanion.app onto Applications."
} else {
    Write-Host "  1. Transfer the .tar.gz to the Mac (this cross-build has no .dmg)."
    Write-Host "  2. Double-click the .tar.gz; Archive Utility extracts a folder. Drag"
    Write-Host "     NightSummaryCompanion.app into /Applications."
}
if ($macSigned) {
    Write-Host "  3. Open it. An AirDropped/scp'd copy opens straight away; a browser-"
    Write-Host "     DOWNLOADED copy is quarantined -> right-click -> Open once"
    Write-Host "     ('unidentified developer', not 'damaged', because the bundle is signed)."
    Write-Host "     macOS 15 (Sequoia): if Open isn't offered, System Settings ->"
    Write-Host "     Privacy & Security -> Open Anyway."
    Write-Host "  4. Setup wizard opens in your default browser. Done."
    Write-Host ""
    Write-Host "Bundle is ad-hoc signed (no Apple account). Notarization would remove the" -ForegroundColor DarkGray
    Write-Host "right-click step but needs a paid dev account -- out of scope by policy."  -ForegroundColor DarkGray
} else {
    Write-Host "  3. Double-click 'Fix Permissions.command' (one-time ad-hoc codesign)."
    Write-Host "  4. Right-click NightSummaryCompanion.app -> Open. Gatekeeper warns once; click Open."
    Write-Host "  5. Setup wizard opens in your default browser. Done."
    Write-Host ""
    Write-Host "This is an UNSIGNED cross-build (codesign is mac-only). Build on a Mac /"  -ForegroundColor DarkGray
    Write-Host "the CI macos runner to sign the bundle + produce the .dmg."                -ForegroundColor DarkGray
}
