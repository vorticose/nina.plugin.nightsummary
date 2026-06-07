# Night Summary - Remote Deploy Script
# Usage: .\scripts\deploy-remote.ps1
#
# Copies the already-built Release DLL to the remote telescope machine
# over the Tailscale network share. Run deploy.ps1 first to build.

$ErrorActionPreference = "Stop"
$repoRoot  = Split-Path -Parent $PSScriptRoot
$buildDir  = Join-Path $repoRoot "NINA.Plugin.NightSummary\bin\Release\net8.0-windows"
$dll       = Join-Path $buildDir "NINA.Plugin.NightSummary.dll"

# Observatory host (Tailscale IP or MagicDNS name) comes from an env var so no
# machine-specific address is committed. Set it once in your shell profile:
#   $env:NS_OBSERVATORY_HOST = "<your-tailscale-ip-or-hostname>"
$remoteHost = $env:NS_OBSERVATORY_HOST
if (-not $remoteHost) {
    Write-Error "Set the NS_OBSERVATORY_HOST environment variable to the observatory host (Tailscale IP or hostname) before deploying."
    exit 1
}
$remoteDir = "\\$remoteHost\Night Summary"

# --- Verify build exists ---
if (-not (Test-Path $dll)) {
    Write-Error "DLL not found at $dll - run deploy.ps1 first to build."
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
