# Night Summary - Start Dev Dashboard Server
#
# Usage:
#   .\scripts\start-dev.ps1                       # launch (kills stale, uses existing binary, primary mode)
#   .\scripts\start-dev.ps1 -Rebuild              # rebuild C# server first, then launch
#   .\scripts\start-dev.ps1 -CompanionMode        # wire a stub ICompanionController so the dashboard
#                                                 # renders its companion-mode UI (banner, sync panel,
#                                                 # pairing wizard). For iterating on mobile UI bugs.
#   .\scripts\start-dev.ps1 -Rebuild -CompanionMode
#
# Hot reload: JS/CSS changes in Web/ are served live - no rebuild needed.
# Rebuild only when tools/dev-dashboard-cs C# code changes.
#
# Data: always uses the dev snapshot DB at ~/Documents/ns-snapshot/
# URL:  http://<this-host-tailscale-ip>:8183/  (Tailscale - iPad/tablet access)

param(
    [switch]$Rebuild,
    [switch]$CompanionMode
)

$ErrorActionPreference = "Stop"

$Root       = Split-Path -Parent $PSScriptRoot
$ProjDir    = Join-Path $Root "tools\dev-dashboard-cs"
$ExeDir     = Join-Path $ProjDir "bin\Release\net8.0"
$Exe        = Join-Path $ExeDir "nightsummary-dev-dashboard.exe"
$WebDir     = Join-Path $Root "NINA.Plugin.NightSummary.Dashboard\Web"
$AssetsDir  = Join-Path $Root "assets"
$SnapshotRoot = Join-Path $env:USERPROFILE "Documents\ns-snapshot"
$SnapshotDb   = Join-Path $SnapshotRoot "nightsummary.sqlite"
$SnapshotTs   = Join-Path $SnapshotRoot "schedulerdb.sqlite"
$SnapshotRp   = Join-Path $SnapshotRoot "reports"
$BindHost   = "+"
$Port       = 8183

# Best-effort: show this host's Tailscale IP (CGNAT range) in the URL hint so the
# printed link is tap-ready on a tablet, without committing a machine-specific IP.
$DisplayHost = (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -match '^100\.(6[4-9]|[7-9]\d|1[01]\d|12[0-7])\.' } |
    Select-Object -First 1 -ExpandProperty IPAddress)
if (-not $DisplayHost) { $DisplayHost = 'localhost' }

# --- Kill any stale instance ---
$existing = Get-Process -Name "nightsummary-dev-dashboard" -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping existing dev server (PID $($existing.Id))..." -ForegroundColor Yellow
    $existing | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

# --- Build if requested or binary is missing ---
if ($Rebuild -or -not (Test-Path $Exe)) {
    if (-not (Test-Path $Exe)) {
        Write-Host "Binary not found - building..." -ForegroundColor Yellow
    } else {
        Write-Host "Rebuilding dev dashboard C# server..." -ForegroundColor Cyan
    }
    dotnet build $ProjDir -c Release -v quiet
    if ($LASTEXITCODE -ne 0) { Write-Error "Build failed."; exit 1 }
    Write-Host "Build succeeded." -ForegroundColor Green
}

# --- Pre-flight checks ---
if (-not (Test-Path $Exe))        { Write-Error "Binary missing: $Exe`nRun: .\scripts\start-dev.ps1 -Rebuild"; exit 1 }
if (-not (Test-Path $SnapshotDb)) { Write-Error "Snapshot DB missing: $SnapshotDb"; exit 1 }
if (-not (Test-Path $WebDir))     { Write-Error "Web dir missing: $WebDir`n(Wrong worktree?)"; exit 1 }
$TsArgs = @()
if (Test-Path $SnapshotTs) { $TsArgs = @('--ts-db', $SnapshotTs) }

$ModeArgs = @()
if ($CompanionMode) { $ModeArgs = @('--companion-mode') }

# --- Launch ---
Write-Host ""
Write-Host "Night Summary dev server" -ForegroundColor Cyan
Write-Host "  Worktree : $Root" -ForegroundColor Gray
Write-Host "  Web      : $WebDir" -ForegroundColor Gray
Write-Host "  DB       : $SnapshotDb" -ForegroundColor Gray
if (Test-Path $SnapshotTs) {
    Write-Host "  TS DB    : $SnapshotTs" -ForegroundColor Gray
} else {
    Write-Host "  TS DB    : (not found - TS augment disabled)" -ForegroundColor DarkGray
}
Write-Host "  URL      : http://${DisplayHost}:$Port/" -ForegroundColor White
if ($CompanionMode) {
    Write-Host "  Mode     : COMPANION (stub controller - sync/pair/regen are no-ops)" -ForegroundColor Magenta
} else {
    Write-Host "  Mode     : primary" -ForegroundColor Gray
}
Write-Host ""

& $Exe --host $BindHost --port $Port `
       --db      $SnapshotDb `
       --data    $SnapshotRoot `
       --reports $SnapshotRp `
       --web     $WebDir `
       --assets  $AssetsDir `
       @TsArgs `
       @ModeArgs
