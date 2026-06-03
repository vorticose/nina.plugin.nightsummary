# Builds the Windows companion distribution and zips it.
#
# Produces: build/companion-win/NightSummaryCompanion-win-<arch>.zip
#
# Layout inside the zip (folder the user unzips + keeps):
#   NightSummaryCompanion/
#     NightSummaryCompanion-bin.exe   <- the real self-contained .NET binary
#     e_sqlite3.dll  libSkiaSharp.dll <- native deps (PublishSingleFile keeps them external)
#     NightSummaryCompanion.cmd       <- watchdog launcher (respawn on exit 88, quit on 0)
#     Start NightSummaryCompanion.vbs <- double-click target; runs the .cmd hidden (no console window)
#     README.txt                      <- SmartScreen + run instructions
#
# Mirrors build-companion-mac.ps1: single-file self-contained publish, native
# libs copied alongside, a watchdog that mirrors the macOS bash launcher
# (exit 88 = Dashboard "Restart", exit 0 = Dashboard "Quit"). Windows zips do
# not carry Unix exec bits, so a plain Compress-Archive is fine here (unlike the
# Linux/mac tar.gz path).
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

# Real binary under a -bin suffix so the .cmd can carry the friendly name.
Copy-Item "$publishDir/NightSummaryCompanion.exe" "$appDir/NightSummaryCompanion-bin.exe"

# Copy ALL native dlls the publish emitted (e_sqlite3.dll + libSkiaSharp.dll).
# Globbing future-proofs new native deps; a missing one is a runtime
# DllNotFoundException, never a build error, so assert at least one exists.
$dlls = Get-ChildItem "$publishDir/*.dll"
if (-not $dlls) { throw "no native dlls found in $publishDir -- expected e_sqlite3.dll + libSkiaSharp.dll" }
Copy-Item $dlls.FullName $appDir/
Write-Host ("  bundled natives: " + (($dlls.Name) -join ', '))

# 3. Watchdog launcher (.cmd). Mirrors the macOS bash watchdog:
#      exit 88 -> respawn (Dashboard "Restart")
#      exit 0  -> stop    (Dashboard "Quit" / clean shutdown)
#      other   -> propagate the code and stop (don't spin on a crash loop)
$cmd = @'
@echo off
setlocal
cd /d "%~dp0"
:loop
"%~dp0NightSummaryCompanion-bin.exe" %*
if "%errorlevel%"=="88" (
    rem Restart requested via dashboard. Brief pause so the OS frees the port.
    timeout /t 1 /nobreak >nul
    goto loop
)
rem exit 0 (clean quit) or any other code: propagate and stop.
exit /b %errorlevel%
'@
[System.IO.File]::WriteAllText((Join-Path $appDir 'NightSummaryCompanion.cmd'),
    ($cmd -replace "`r`n", "`r`n"),  # keep CRLF for a .cmd
    (New-Object System.Text.UTF8Encoding $false))

# 4. Hidden-launch shim (.vbs). Double-clicking this runs the .cmd with a hidden
#    window (style 0) so the companion runs in the background with no stuck
#    console window. The .cmd remains available for a visible/diagnostic run.
$vbs = @'
' Launches NightSummaryCompanion in the background (no console window).
Set sh = CreateObject("WScript.Shell")
dir = Left(WScript.ScriptFullName, InStrRev(WScript.ScriptFullName, "\"))
sh.CurrentDirectory = dir
sh.Run """" & dir & "NightSummaryCompanion.cmd""", 0, False
'@
[System.IO.File]::WriteAllText((Join-Path $appDir 'Start NightSummaryCompanion.vbs'),
    ($vbs -replace "`r`n", "`r`n"),
    (New-Object System.Text.UTF8Encoding $false))

# 5. README with the one-time SmartScreen click-through (no codesigning per
#    project policy) and run instructions.
$readme = @"
Night Summary Companion (Windows x64) - v$version

WHAT THIS IS
  A local web dashboard that mirrors your Night Summary imaging history from the
  primary (NINA) machine. Runs a small web server on http://localhost:8182/.

INSTALL
  1. Unzip this folder anywhere (e.g. C:\Tools\NightSummaryCompanion).
  2. Double-click "Start NightSummaryCompanion.vbs" to run it in the background
     (no window). Or run NightSummaryCompanion.cmd to see a console.
  3. First run: Windows SmartScreen may warn ("Windows protected your PC").
     Click "More info" -> "Run anyway". This is expected for unsigned apps;
     the companion is open source and unsigned by design.
  4. A browser tab opens to the setup wizard. Pair it with your primary machine.

STOP / RESTART
  - Stop: close the console window, or end NightSummaryCompanion-bin.exe in Task
    Manager (the .vbs/.cmd run it hidden).
  - The dashboard's Restart button respawns the process automatically (the .cmd
    watchdog handles it).

AUTOSTART AT LOGIN (optional)
  Press Win+R, type shell:startup, and drop a shortcut to
  "Start NightSummaryCompanion.vbs" into that folder.
"@
Set-Content -Path (Join-Path $appDir 'README.txt') -Value $readme -Encoding UTF8

# 6. Zip it (Windows zips don't need Unix mode bits).
$zipName = "NightSummaryCompanion-win-$Arch.zip"
$zipPath = Join-Path $buildDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath }
Compress-Archive -Path $appDir -DestinationPath $zipPath -CompressionLevel Optimal

$zipMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "  -> $zipPath ($zipMb MB)" -ForegroundColor Green
Write-Host ""
Write-Host "Done. Artifact in $buildDir" -ForegroundColor Cyan
