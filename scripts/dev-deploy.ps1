# Night Summary - Dev Deploy Script (run directly on the Windows machine)
# Usage: .\scripts\dev-deploy.ps1
#
# What this does:
#   1. git pull (latest from current branch)
#   2. Builds the plugin in Release
#   3. Copies the DLL to the local NINA plugins folder

$ErrorActionPreference = "Stop"
$repoRoot      = Split-Path -Parent $PSScriptRoot
$projectDir    = Join-Path $repoRoot "NINA.Plugin.NightSummary"
$buildDir      = Join-Path $projectDir "bin\Release\net8.0-windows"
$ninaPluginDir = Join-Path $env:LOCALAPPDATA "NINA\Plugins\3.0.0\Night Summary"

# --- Pull ---
Write-Host "Pulling latest from origin..." -ForegroundColor Cyan
git -C $repoRoot pull
if ($LASTEXITCODE -ne 0) { Write-Error "git pull failed."; exit 1 }
Write-Host "Pull complete." -ForegroundColor Green

# --- Restore (fast when packages are cached) ---
Write-Host "Restoring packages..." -ForegroundColor Cyan
dotnet restore "$projectDir\NINA.Plugin.NightSummary.csproj" | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error "Restore failed."; exit 1 }

# --- Build ---
Write-Host "Building..." -ForegroundColor Cyan
dotnet build "$projectDir\NINA.Plugin.NightSummary.csproj" -c Release --no-restore | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed."; exit 1 }
Write-Host "Build succeeded." -ForegroundColor Green

# --- Deploy ---
$dll = Join-Path $buildDir "NINA.Plugin.NightSummary.dll"
if (Test-Path $ninaPluginDir) {
    Copy-Item $dll $ninaPluginDir -Force
    Write-Host "Deployed to NINA plugins folder." -ForegroundColor Green
} else {
    Write-Host "NINA plugin folder not found at: $ninaPluginDir" -ForegroundColor Red
    Write-Host "Copy manually from: $dll" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "Done. Restart NINA to pick up the new build." -ForegroundColor White
