# Night Summary - v3-dev Deploy Script (run directly on the Windows machine)
# Usage: .\scripts\dev-v3-deploy.ps1
#
# What this does:
#   1. Checks out v3-dev and pulls latest
#   2. Builds the plugin in Release
#   3. Copies the DLL to the local NINA plugins folder
#   4. Returns to the previous branch

$ErrorActionPreference = "Stop"
$repoRoot      = Split-Path -Parent $PSScriptRoot
$projectDir    = Join-Path $repoRoot "NINA.Plugin.NightSummary"
$buildDir      = Join-Path $projectDir "bin\Release\net8.0-windows"
$ninaPluginDir = Join-Path $env:LOCALAPPDATA "NINA\Plugins\3.0.0\Night Summary"

# --- Save current branch ---
$prevBranch = git -C $repoRoot rev-parse --abbrev-ref HEAD
if ($LASTEXITCODE -ne 0) { Write-Error "Failed to detect current branch."; exit 1 }

# --- Checkout v3-dev and pull ---
Write-Host "Switching to v3-dev..." -ForegroundColor Cyan
git -C $repoRoot checkout v3-dev
if ($LASTEXITCODE -ne 0) { Write-Error "Failed to checkout v3-dev."; exit 1 }

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

# --- Return to previous branch ---
if ($prevBranch -ne "v3-dev") {
    Write-Host "Returning to $prevBranch..." -ForegroundColor Cyan
    git -C $repoRoot checkout $prevBranch
}

Write-Host ""
Write-Host "Done. Restart NINA to pick up the v3-dev build." -ForegroundColor White
