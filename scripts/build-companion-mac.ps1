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

    Copy-Item "$publishDir/NightSummaryCompanion"    $macOs/
    Copy-Item "$publishDir/libe_sqlite3.dylib"       $macOs/

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
    <key>CFBundlePackageType</key>           <string>APPL</string>
    <key>CFBundleInfoDictionaryVersion</key> <string>6.0</string>
    <key>LSUIElement</key>                   <true/>
    <key>LSMinimumSystemVersion</key>        <string>11.0</string>
    <key>NSHighResolutionCapable</key>       <true/>
</dict>
</plist>
"@
    Set-Content -Path (Join-Path $contents 'Info.plist') -Value $plist -Encoding UTF8 -NoNewline

    # 4. Zip the .app. PowerShell's Compress-Archive preserves file structure
    # but does not preserve the Unix executable bit -- the receiving Mac sees
    # the binary as read-only. README install steps remind the user to do
    # `chmod +x NightSummaryCompanion.app/Contents/MacOS/NightSummaryCompanion`
    # on first install, and gatekeeper's right-click->Open is required anyway.
    # CI on macos-latest runners will produce a .dmg later, which preserves
    # exec bits natively.
    $zipName = "NightSummaryCompanion-mac-$archLabel.zip"
    $zipPath = Join-Path $buildDir $zipName
    if (Test-Path $zipPath) { Remove-Item $zipPath }
    Compress-Archive -Path $appRoot -DestinationPath $zipPath
    $zipMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Host "  -> $zipPath ($zipMb MB)" -ForegroundColor Green
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
Write-Host ""
Write-Host "To install on a fresh Mac:" -ForegroundColor Yellow
Write-Host "  1. Transfer the .zip to the Mac (AirDrop / scp / download)."
Write-Host "  2. Unzip. Finder shows NightSummaryCompanion.app."
Write-Host "  3. Drag it into /Applications."
Write-Host "  4. Right-click the .app -> Open. Gatekeeper warns once; click Open."
Write-Host "  5. The setup wizard opens in your default browser. Done."
Write-Host ""
Write-Host "If the binary refuses to run with a permission error:" -ForegroundColor Yellow
Write-Host "  Right-click .app -> Show Package Contents -> Contents/MacOS/."
Write-Host "  Run: chmod +x NightSummaryCompanion"
