# Session Capture - Build & Deploy Script
# Usage: .\scripts\deploy-capture.ps1
#
# What this does:
#   1. Builds NINA.Plugin.SessionCapture in Release
#   2. Copies the DLL to local NINA plugins folder (Session Capture)
#   3. Copies the DLL to the remote telescope machine over Tailscale

$ErrorActionPreference = "Stop"
$repoRoot   = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $repoRoot "NINA.Plugin.SessionCapture"
$buildDir   = Join-Path $projectDir "bin\Release\net8.0-windows"
$dll        = Join-Path $buildDir "NINA.Plugin.SessionCapture.dll"
$localDir   = Join-Path $env:LOCALAPPDATA "NINA\Plugins\3.0.0\Session Capture"
$remoteDir  = "\\100.86.208.29\Users\RBFocus\AppData\Local\NINA\Plugins\3.0.0\Session Capture"

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

# --- Deploy to remote ---
if (Test-Path $remoteDir) {
    Copy-Item $dll $remoteDir -Force
    Write-Host "Deployed to remote telescope machine." -ForegroundColor Green
} else {
    Write-Host "Remote machine not reachable - skipping remote deploy." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Done. Restart NINA to load Session Capture v$versionStr." -ForegroundColor Green
Write-Host ""
Write-Host "Usage in NINA:" -ForegroundColor White
Write-Host "  Add 'Session Capture Start' before your imaging instructions" -ForegroundColor White
Write-Host "  Add 'Session Capture Stop' after your imaging instructions" -ForegroundColor White
Write-Host "  Recordings saved to: %LOCALAPPDATA%\NINA\SessionCapture\" -ForegroundColor White
