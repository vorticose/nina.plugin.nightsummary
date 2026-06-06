# Builds the Windows companion distribution and zips it.
#
# Produces: build/companion-win/NightSummaryCompanion-win-<arch>.zip
#
# Layout inside the zip (folder the user unzips + keeps):
#   NightSummaryCompanion/
#     NightSummaryCompanion.exe       <- single double-click target: a WinExe (no
#                                          console window) with the brand icon
#                                          embedded; self-respawns on Restart
#     e_sqlite3.dll  libSkiaSharp.dll <- native deps (PublishSingleFile keeps them external)
#     README.txt                      <- SmartScreen + run instructions
#
# Unlike build-companion-{mac,linux}.ps1 (which keep a bash watchdog for the
# exit-88 Restart sentinel), Windows needs no external watchdog: the exe is a
# WinExe so double-clicking shows no console, and the dashboard Restart is handled
# in-process by a self-respawn (see DashboardServer.Companion.cs RespawnSelfWindows
# + the StartAsync bind-retry). Windows zips don't carry Unix exec bits, so a plain
# Compress-Archive is fine here (unlike the Linux/mac tar.gz path).
#
# Usage:
#   .\scripts\build-companion-win.ps1            # x64 (default)
#
# Requires: pwsh 7+ (Compress-Archive + dotnet publish).

[CmdletBinding()]
param(
    [ValidateSet('x64')]
    [string]$Arch = 'x64'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$buildDir = Join-Path $repoRoot 'build/companion-win'
$projPath = Join-Path $repoRoot 'NINA.Plugin.NightSummary.Companion/NINA.Plugin.NightSummary.Companion.csproj'

if (-not (Test-Path $buildDir)) { New-Item -ItemType Directory -Path $buildDir -Force | Out-Null }

# Version comes from the plugin csproj VersionPrefix (companion lockstep-versions
# with the plugin), same source build-companion-mac.ps1 uses.
function Get-CompanionVersion {
    $pluginCsproj = Join-Path $repoRoot 'NINA.Plugin.NightSummary/NINA.Plugin.NightSummary.csproj'
    if (Test-Path $pluginCsproj) {
        $xml = [xml](Get-Content $pluginCsproj -Raw)
        $vp = $xml.Project.PropertyGroup.VersionPrefix | Where-Object { $_ } | Select-Object -First 1
        if ($vp) { return $vp }
    }
    return '0.0.0'
}

$rid     = "win-$Arch"
$version = Get-CompanionVersion

Write-Host ""
Write-Host "=== Building NightSummaryCompanion ($rid, v$version) ===" -ForegroundColor Cyan

# 1. Publish self-contained single-file binary. Natives (e_sqlite3, libSkiaSharp)
#    stay external because IncludeNativeLibrariesForSelfExtract is left off.
#    Clean the RID output first -- an incremental single-file publish can drop
#    the externalized native dlls from the publish root, so publish from scratch.
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
if (-not (Test-Path "$publishDir/NightSummaryCompanion.exe")) {
    throw "publish output missing: $publishDir/NightSummaryCompanion.exe"
}

# 2. Stage the distribution folder.
$staging = Join-Path $buildDir "staging-$Arch"
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
$appDir = Join-Path $staging 'NightSummaryCompanion'
New-Item -ItemType Directory -Path $appDir -Force | Out-Null

# The single double-click target: a WinExe (no console window) with the brand
# icon embedded (ApplicationIcon in the csproj). It self-respawns on a dashboard
# Restart, so no external .cmd watchdog or hidden-launch .vbs is needed anymore.
Copy-Item "$publishDir/NightSummaryCompanion.exe" "$appDir/NightSummaryCompanion.exe"

# Copy ALL native dlls the publish emitted (e_sqlite3.dll + libSkiaSharp.dll).
# Globbing future-proofs new native deps; a missing one is a runtime
# DllNotFoundException, never a build error, so assert at least one exists.
$dlls = Get-ChildItem "$publishDir/*.dll"
if (-not $dlls) { throw "no native dlls found in $publishDir -- expected e_sqlite3.dll + libSkiaSharp.dll" }
Copy-Item $dlls.FullName $appDir/
Write-Host ("  bundled natives: " + (($dlls.Name) -join ', '))

# 3. README with the one-time SmartScreen click-through (no codesigning per
#    project policy) and run instructions.
$readme = @"
Night Summary Companion (Windows x64) - v$version

WHAT THIS IS
  A local web dashboard that mirrors your Night Summary imaging history from the
  primary (NINA) machine. Runs a small web server on http://localhost:8182/.

INSTALL
  1. Unzip this folder anywhere (e.g. C:\Tools\NightSummaryCompanion).
  2. Double-click "NightSummaryCompanion.exe". It runs in the background with no
     console window.
  3. First run: Windows SmartScreen may warn ("Windows protected your PC").
     Click "More info" -> "Run anyway". This is expected for unsigned apps;
     the companion is open source and unsigned by design.
  4. A browser tab opens to the setup wizard. Pair it with your primary machine.

STOP / RESTART
  - Stop: use the dashboard's Quit button (Settings -> Companion process), or end
    NightSummaryCompanion.exe in Task Manager.
  - The dashboard's Restart button relaunches the app automatically.

AUTOSTART AT LOGIN
  Turn on "Start at login" in the dashboard (Settings -> Start at login). It drops
  a shortcut to NightSummaryCompanion.exe in your Startup folder. To do it by hand
  instead: Win+R, type shell:startup, and drop a shortcut to
  NightSummaryCompanion.exe into that folder.
"@
Set-Content -Path (Join-Path $appDir 'README.txt') -Value $readme -Encoding UTF8

# 4. Zip it (Windows zips don't need Unix mode bits).
$zipName = "NightSummaryCompanion-win-$Arch.zip"
$zipPath = Join-Path $buildDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath }
Compress-Archive -Path $appDir -DestinationPath $zipPath -CompressionLevel Optimal

$zipMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "  -> $zipPath ($zipMb MB)" -ForegroundColor Green
Write-Host ""
Write-Host "Done. Artifact in $buildDir" -ForegroundColor Cyan
