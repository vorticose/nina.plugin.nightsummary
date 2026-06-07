# Night Summary - Remote Deploy Script (scp over Tailscale)
# Usage: .\scripts\deploy-remote.ps1
#
# Copies the already-built Release artifacts to the remote telescope machine via
# scp. (The SMB share is unreliable from some dev boxes - 'net use' can return
# System error 67 - so scp is the dependable path.) Run deploy.ps1 or a Release
# build first.
#
# Requirements:
#   - OpenSSH client (scp) on PATH, with key auth already set up to the rig.
#   - PowerShell 7+ (needed to pass the space-containing remote path to scp correctly).
#
# Set once in your shell profile (kept out of the repo on purpose):
#   $env:NS_OBSERVATORY_HOST = "<tailscale-ip-or-hostname>"
#   $env:NS_OBSERVATORY_USER = "<observatory-windows-username>"
#
# NOTE: close NINA on the rig first - a running NINA locks the plugin DLLs.

$ErrorActionPreference = "Stop"
$repoRoot  = Split-Path -Parent $PSScriptRoot
$buildDir  = Join-Path $repoRoot "NINA.Plugin.NightSummary\bin\Release\net8.0-windows"
$dll       = Join-Path $buildDir "NINA.Plugin.NightSummary.dll"

$remoteHost = $env:NS_OBSERVATORY_HOST
$remoteUser = $env:NS_OBSERVATORY_USER
if (-not $remoteHost -or -not $remoteUser) {
    Write-Error "Set the NS_OBSERVATORY_HOST and NS_OBSERVATORY_USER environment variables before deploying."
    exit 1
}
if (-not (Get-Command scp -ErrorAction SilentlyContinue)) {
    Write-Error "scp (OpenSSH client) not found on PATH."
    exit 1
}

# --- Verify build exists ---
if (-not (Test-Path $dll)) {
    Write-Error "DLL not found at $dll - run deploy.ps1 first to build."
    exit 1
}

$version = [System.Reflection.AssemblyName]::GetAssemblyName($dll).Version
$versionStr = "$($version.Major).$($version.Minor).$($version.Build)"
Write-Host "Version: $versionStr" -ForegroundColor Cyan

# --- Files to deploy: main DLL + Dashboard.dll (ReportGenerator) + deps.json ---
# Dashboard.dll holds ReportGenerator and deps.json drives assembly resolution;
# both must ship alongside the main DLL or report generation throws at runtime.
$deployFiles = @(
    $dll,
    (Join-Path $buildDir "NINA.Plugin.NightSummary.Dashboard.dll"),
    (Join-Path $buildDir "NINA.Plugin.NightSummary.deps.json")
)

# Remote plugin folder. The path contains a space, so the remote target is wrapped
# in double quotes for the remote shell (backslash-escaping does not work with scp).
$remoteBase = "C:/Users/$remoteUser/AppData/Local/NINA/Plugins/3.0.0/Night Summary"

Write-Host "Deploying to ${remoteUser}@${remoteHost} via scp ..." -ForegroundColor Cyan
foreach ($f in $deployFiles) {
    if (-not (Test-Path $f)) { Write-Error "Missing build artifact: $f"; exit 1 }
    $name   = Split-Path $f -Leaf
    $target = '{0}@{1}:"{2}/{3}"' -f $remoteUser, $remoteHost, $remoteBase, $name
    scp -O $f $target
    if ($LASTEXITCODE -ne 0) {
        Write-Error "scp failed for $name. Is the rig on Tailscale and NINA closed?"
        exit 1
    }
    Write-Host "  copied $name" -ForegroundColor Green
}
Write-Host "Done. Restart NINA on the telescope machine to load v$versionStr." -ForegroundColor Green
