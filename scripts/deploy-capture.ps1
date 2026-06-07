# Session Capture - Build & Deploy Script
# Usage: .\scripts\deploy-capture.ps1
#
# What this does:
#   1. Builds NINA.Plugin.SessionCapture in Release
#   2. Copies the DLL to the local NINA plugins folder (Session Capture)
#   3. Copies the DLL to the remote telescope machine via scp over Tailscale
#
# Requirements for the remote step:
#   - OpenSSH client (scp) on PATH, with key auth already set up to the rig.
#   - PowerShell 7+ (needed to pass the space-containing remote path to scp correctly).
#
# Set once in your shell profile (kept out of the repo on purpose):
#   $env:NS_OBSERVATORY_HOST = "<tailscale-ip-or-hostname>"
#   $env:NS_OBSERVATORY_USER = "<observatory-windows-username>"
#
# NOTE: close NINA on the rig first - a running NINA locks the plugin DLL.

$ErrorActionPreference = "Stop"
$repoRoot   = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $repoRoot "NINA.Plugin.SessionCapture"
$buildDir   = Join-Path $projectDir "bin\Release\net8.0-windows"
$dll        = Join-Path $buildDir "NINA.Plugin.SessionCapture.dll"
$localDir   = Join-Path $env:LOCALAPPDATA "NINA\Plugins\3.0.0\Session Capture"

$remoteHost = $env:NS_OBSERVATORY_HOST
$remoteUser = $env:NS_OBSERVATORY_USER

# --- Build ---
Write-Host "Building Session Capture..." -ForegroundColor Cyan
dotnet build "$projectDir\NINA.Plugin.SessionCapture.csproj" -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed."; exit 1 }
Write-Host "Build succeeded." -ForegroundColor Green

# --- Read version from DLL ---
$version = [System.Reflection.AssemblyName]::GetAssemblyName($dll).Version
$versionStr = "$($version.Major).$($version.Minor).$($version.Build)"
Write-Host "Version: $versionStr" -ForegroundColor Cyan

# --- Deploy locally ---
if (-not (Test-Path $localDir)) {
    New-Item -ItemType Directory -Path $localDir -Force | Out-Null
    Write-Host "Created local plugin folder: $localDir" -ForegroundColor Yellow
}
Copy-Item $dll $localDir -Force
Write-Host "Deployed to local NINA plugins folder." -ForegroundColor Green

# --- Deploy to remote (scp over Tailscale) ---
if ($remoteHost -and $remoteUser -and (Get-Command scp -ErrorAction SilentlyContinue)) {
    # Remote path has a space, so wrap the target in double quotes for the remote shell.
    $remoteBase = "C:/Users/$remoteUser/AppData/Local/NINA/Plugins/3.0.0/Session Capture"
    $target = '{0}@{1}:"{2}/{3}"' -f $remoteUser, $remoteHost, $remoteBase, (Split-Path $dll -Leaf)
    Write-Host "Deploying to ${remoteUser}@${remoteHost} via scp ..." -ForegroundColor Cyan
    scp -O $dll $target
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Remote deploy failed (is the rig on Tailscale and NINA closed?) - skipping." -ForegroundColor Yellow
    } else {
        Write-Host "Deployed to remote telescope machine." -ForegroundColor Green
    }
} else {
    Write-Host "Remote deploy skipped - set NS_OBSERVATORY_HOST/_USER and install the OpenSSH client to enable." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Done. Restart NINA to load Session Capture v$versionStr." -ForegroundColor Green
Write-Host ""
Write-Host "Usage in NINA:" -ForegroundColor White
Write-Host "  Add 'Session Capture Start' before your imaging instructions" -ForegroundColor White
Write-Host "  Add 'Session Capture Stop' after your imaging instructions" -ForegroundColor White
Write-Host "  Recordings saved to: %LOCALAPPDATA%\NINA\SessionCapture\" -ForegroundColor White
