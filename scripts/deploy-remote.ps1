# Night Summary - Remote Deploy Script
# Usage: .\scripts\deploy-remote.ps1
#
# Copies the already-built Release DLL to the remote telescope machine
# over the Tailscale network share. Run deploy-local.ps1 first to build.

$ErrorActionPreference = "Stop"
$repoRoot  = Split-Path -Parent $PSScriptRoot
$buildDir  = Join-Path $repoRoot "NINA.Plugin.Template\bin\Release\net8.0-windows"
$dll       = Join-Path $buildDir "NINA.Plugin.NightSummary.dll"
$remoteDir = "\\100.86.208.29\NightSummaryPlugin"

# --- Verify build exists ---
if (-not (Test-Path $dll)) {
    Write-Error "DLL not found at $dll — run deploy-local.ps1 first to build."
    exit 1
}

$version = [System.Reflection.AssemblyName]::GetAssemblyName($dll).Version
$versionStr = "$($version.Major).$($version.Minor).$($version.Build)"
Write-Host "Version: $versionStr" -ForegroundColor Cyan

# --- Verify remote share is reachable ---
if (-not (Test-Path $remoteDir)) {
    Write-Error "Remote share $remoteDir is not reachable. Is the telescope machine on Tailscale?"
    exit 1
}

# --- Copy DLL ---
Write-Host "Deploying to $remoteDir ..." -ForegroundColor Cyan
Copy-Item $dll $remoteDir -Force
Write-Host "Done. Restart NINA on the telescope machine to load v$versionStr." -ForegroundColor Green
