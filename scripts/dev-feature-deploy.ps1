# Night Summary - Feature Branch Deploy Script (run directly on the Windows machine)
# Usage: .\scripts\dev-feature-deploy.ps1 -Branch feature/timelapse-embed
#
# What this does:
#   1. Saves current branch
#   2. Checks out the specified feature branch and pulls latest
#   3. Builds the plugin in Release
#   4. Copies the DLL to the local NINA plugins folder
#   5. Returns to the previous branch

param(
    [Parameter(Mandatory=$true)]
    [string]$Branch
)

$ErrorActionPreference = "Stop"
$repoRoot      = Split-Path -Parent $PSScriptRoot
$projectDir    = Join-Path $repoRoot "NINA.Plugin.NightSummary"
$buildDir      = Join-Path $projectDir "bin\Release\net8.0-windows"
$ninaPluginDir = Join-Path $env:LOCALAPPDATA "NINA\Plugins\3.0.0\Night Summary"

# --- Check if NINA has the DLL locked ---
$targetDll = Join-Path $ninaPluginDir "NINA.Plugin.NightSummary.dll"
if (Test-Path $targetDll) {
    try {
        $stream = [System.IO.File]::Open($targetDll, 'Open', 'Read', 'None')
        $stream.Close()
    } catch {
        Write-Host "NINA is running (DLL is locked). Close it before deploying." -ForegroundColor Red
        exit 1
    }
}

# --- Save current branch ---
$prevBranch = git -C $repoRoot rev-parse --abbrev-ref HEAD
if ($LASTEXITCODE -ne 0) { Write-Error "Failed to detect current branch."; exit 1 }

# --- Checkout feature branch and pull ---
Write-Host "Switching to $Branch..." -ForegroundColor Cyan
git -C $repoRoot fetch origin
if ($LASTEXITCODE -ne 0) { Write-Error "git fetch failed."; exit 1 }

git -C $repoRoot checkout $Branch
if ($LASTEXITCODE -ne 0) { Write-Error "Failed to checkout $Branch."; exit 1 }

Write-Host "Pulling latest from origin..." -ForegroundColor Cyan
git -C $repoRoot pull origin $Branch
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
if ($prevBranch -ne $Branch) {
    Write-Host "Returning to $prevBranch..." -ForegroundColor Cyan
    git -C $repoRoot checkout $prevBranch
}

Write-Host ""
Write-Host "Done. Restart NINA to pick up the $Branch build." -ForegroundColor Green
