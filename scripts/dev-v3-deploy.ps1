# Night Summary - Dev Deploy Script (run directly on the Windows machine)
# Usage: .\scripts\dev-v3-deploy.ps1 [branch]
#
# What this does:
#   1. Checks out the target branch (default: v3-dev) and pulls latest
#   2. Builds the plugin in Release
#   3. Closes NINA if running (so the DLL is unlocked)
#   4. Copies the DLL to the local NINA plugins folder
#   5. Relaunches NINA if it was closed
#   6. Returns to the previous branch
#
# Examples:
#   .\scripts\dev-v3-deploy.ps1                        # builds v3-dev
#   .\scripts\dev-v3-deploy.ps1 feature/dashboard-polish  # builds a feature branch

param(
    [string]$Branch
)

$ErrorActionPreference = "Stop"
$repoRoot      = Split-Path -Parent $PSScriptRoot
$projectDir    = Join-Path $repoRoot "NINA.Plugin.NightSummary"
$buildDir      = Join-Path $projectDir "bin\Release\net8.0-windows"
$ninaPluginDir = Join-Path $env:LOCALAPPDATA "NINA\Plugins\3.0.0\Night Summary"
$ninaExe       = Join-Path ${env:ProgramFiles} "N.I.N.A. - Nighttime Imaging 'N' Astronomy\NINA.exe"

# --- Fetch latest from remote ---
Write-Host "Fetching from origin..." -ForegroundColor Cyan
$savedPref = $ErrorActionPreference
$ErrorActionPreference = "Continue"
git -C $repoRoot fetch origin --prune 2>&1 | Out-Null
$ErrorActionPreference = $savedPref

# --- Pick branch ---
if (-not $Branch) {
    # List all branches (local + remote-tracking)
    $branches = git -C $repoRoot branch -a --format='%(refname:short)' | ForEach-Object { $_ -replace '^origin/', '' } | Where-Object { $_ -match '\S' -and $_ -ne 'HEAD' } | Sort-Object -Unique
    Write-Host "Available branches:" -ForegroundColor Cyan
    for ($i = 0; $i -lt $branches.Count; $i++) {
        $marker = if ($branches[$i] -eq "v3-dev") { " (default)" } else { "" }
        Write-Host "  [$($i + 1)] $($branches[$i])$marker"
    }
    Write-Host ""
    $choice = Read-Host "Branch number or name (Enter = v3-dev)"
    if ([string]::IsNullOrWhiteSpace($choice)) {
        $Branch = "v3-dev"
    } elseif ($choice -match '^\d+$' -and [int]$choice -ge 1 -and [int]$choice -le $branches.Count) {
        $Branch = $branches[[int]$choice - 1]
    } else {
        $Branch = $choice
    }
}

Write-Host "Target branch: $Branch" -ForegroundColor Cyan

# --- Save current branch ---
$prevBranch = git -C $repoRoot rev-parse --abbrev-ref HEAD
if ($LASTEXITCODE -ne 0) { Write-Error "Failed to detect current branch."; exit 1 }

# --- Checkout target branch and pull ---
Write-Host "Switching to $Branch..." -ForegroundColor Cyan
git -C $repoRoot checkout $Branch
if ($LASTEXITCODE -ne 0) { Write-Error "Failed to checkout $Branch."; exit 1 }

Write-Host "Pulling latest from origin..." -ForegroundColor Cyan
git -C $repoRoot pull
if ($LASTEXITCODE -ne 0) { Write-Error "git pull failed."; exit 1 }
Write-Host "Pull complete." -ForegroundColor Green

# --- Clean (ensures embedded resources like JS/CSS are re-embedded) ---
Write-Host "Cleaning previous build..." -ForegroundColor Cyan
dotnet clean "$projectDir\NINA.Plugin.NightSummary.csproj" -c Release 2>&1 | Out-Null

# --- Restore (fast when packages are cached) ---
Write-Host "Restoring packages..." -ForegroundColor Cyan
dotnet restore "$projectDir\NINA.Plugin.NightSummary.csproj" | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error "Restore failed."; exit 1 }

Write-Host "Building..." -ForegroundColor Cyan
dotnet build "$projectDir\NINA.Plugin.NightSummary.csproj" -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed."; exit 1 }
Write-Host "Build succeeded." -ForegroundColor Green

# --- Close NINA if it has the DLL locked ---
$ninaWasRunning = $false
$targetDll = Join-Path $ninaPluginDir "NINA.Plugin.NightSummary.dll"

function Close-NINA {
    $ninaProc = Get-Process -Name "NINA" -ErrorAction SilentlyContinue
    if (-not $ninaProc) { return $false }

    Write-Host "NINA is running. Closing NINA..." -ForegroundColor Yellow
    $ninaProc | ForEach-Object { $_.CloseMainWindow() | Out-Null }
    $timeout = 15
    $waited = 0
    while ((Get-Process -Name "NINA" -ErrorAction SilentlyContinue) -and $waited -lt $timeout) {
        Start-Sleep -Seconds 1
        $waited++
    }
    if (Get-Process -Name "NINA" -ErrorAction SilentlyContinue) {
        Write-Host "NINA did not close gracefully after ${timeout}s. Force killing..." -ForegroundColor Red
        Stop-Process -Name "NINA" -Force
        Start-Sleep -Seconds 2
    }
    Write-Host "NINA closed." -ForegroundColor Green
    return $true
}

$ninaWasRunning = Close-NINA

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

# --- Relaunch NINA if it was closed ---
if ($ninaWasRunning) {
    if (Test-Path $ninaExe) {
        Write-Host "Relaunching NINA..." -ForegroundColor Cyan
        Start-Process $ninaExe
        Write-Host "NINA started." -ForegroundColor Green
    } else {
        Write-Host "Could not find NINA at: $ninaExe" -ForegroundColor Yellow
        Write-Host "Start NINA manually." -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Done. Built and deployed from $Branch." -ForegroundColor White
