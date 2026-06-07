# Night Summary - Dev Deploy Script (run directly on the Windows machine)
# Usage: .\scripts\dev-deploy.ps1
#
# What this does:
#   1. Saves current branch
#   2. Checks out dev and pulls latest
#   3. Builds the plugin in Release
#   4. Copies the DLL to the local NINA plugins folder
#   5. Returns to the previous branch

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

# --- Checkout dev and pull ---
Write-Host "Switching to dev..." -ForegroundColor Cyan
git -C $repoRoot checkout dev
if ($LASTEXITCODE -ne 0) { Write-Error "Failed to checkout dev."; exit 1 }

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
# Copy the plugin DLL plus its runtime companions. ReportGenerator now lives in
# Dashboard.dll, and deps.json drives assembly resolution -- omitting either ships
# a plugin that throws on report generation.
$dll = Join-Path $buildDir "NINA.Plugin.NightSummary.dll"
$deployFiles = @(
    $dll,
    (Join-Path $buildDir "NINA.Plugin.NightSummary.Dashboard.dll"),
    (Join-Path $buildDir "NINA.Plugin.NightSummary.deps.json")
)
if (Test-Path $ninaPluginDir) {
    foreach ($f in $deployFiles) { Copy-Item $f $ninaPluginDir -Force }
    Write-Host "Deployed to NINA plugins folder." -ForegroundColor Green
} else {
    Write-Host "NINA plugin folder not found at: $ninaPluginDir" -ForegroundColor Red
    Write-Host "Copy manually from: $dll" -ForegroundColor Yellow
    exit 1
}

# --- Return to previous branch ---
if ($prevBranch -ne "dev") {
    Write-Host "Returning to $prevBranch..." -ForegroundColor Cyan
    git -C $repoRoot checkout $prevBranch
}

Write-Host ""
Write-Host "Done. Restart NINA to pick up the dev build." -ForegroundColor White
