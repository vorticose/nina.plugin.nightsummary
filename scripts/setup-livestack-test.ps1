# setup-livestack-test.ps1
# Creates fake live stack assets alongside the most recent saved report
# so the Preview/Resend paths can load them for testing.
#
# Usage: Run on the Windows rig from the repo root.
#   .\scripts\setup-livestack-test.ps1
#
# The script will:
# 1. Find the most recent saved report folder
# 2. Query the database for target names in that session
# 3. Copy test JPEGs into assets/ with a livestack.json manifest

$ErrorActionPreference = "Stop"

# --- Locate saved reports ---
$defaultSaveRoot = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "N.I.N.A.\Night Summary\Saved Reports"
if (-not (Test-Path $defaultSaveRoot)) {
    Write-Error "Saved reports directory not found: $defaultSaveRoot"
    exit 1
}

$reportDirs = Get-ChildItem -Path $defaultSaveRoot -Directory | Sort-Object LastWriteTime -Descending
if ($reportDirs.Count -eq 0) {
    Write-Error "No saved report folders found in $defaultSaveRoot"
    exit 1
}

$reportDir = $reportDirs[0].FullName
$htmlFiles = Get-ChildItem -Path $reportDir -Filter "*.html"
if ($htmlFiles.Count -eq 0) {
    Write-Error "No HTML files found in $reportDir"
    exit 1
}

Write-Host "Using report folder: $reportDir" -ForegroundColor Cyan
Write-Host "  HTML file: $($htmlFiles[0].Name)" -ForegroundColor Gray

# --- Query database for target names ---
$dbPath = Join-Path $env:LOCALAPPDATA "NINA\NightSummary\nightsummary.sqlite"
if (-not (Test-Path $dbPath)) {
    Write-Error "Database not found: $dbPath"
    exit 1
}

# Extract session date from folder name (e.g. NightSummary_2026-03-30 -> 2026-03-30)
$folderName = (Get-Item $reportDir).Name
Write-Host "  Folder name: $folderName" -ForegroundColor Gray

# Get the most recent session's targets from the database
Add-Type -Path (Join-Path $PSScriptRoot "..\NINA.Plugin.NightSummary\bin\Release\net8.0-windows\System.Data.SQLite.dll")
$conn = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$dbPath;Version=3;Read Only=True;")
$conn.Open()

# Find the latest session
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT SessionId FROM Sessions ORDER BY SessionStart DESC LIMIT 1"
$sessionId = $cmd.ExecuteScalar()
if (-not $sessionId) {
    $conn.Close()
    Write-Error "No sessions found in database"
    exit 1
}
Write-Host "  Session ID: $sessionId" -ForegroundColor Gray

# Get distinct targets for this session
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT DISTINCT TargetName FROM Images WHERE SessionId = @sid AND TargetName IS NOT NULL AND TargetName != ''"
$cmd2.Parameters.AddWithValue("@sid", $sessionId) | Out-Null
$reader = $cmd2.ExecuteReader()
$targets = @()
while ($reader.Read()) {
    $targets += $reader["TargetName"].ToString()
}
$reader.Close()

# Get distinct filters
$cmd3 = $conn.CreateCommand()
$cmd3.CommandText = "SELECT DISTINCT FilterName FROM Images WHERE SessionId = @sid AND FilterName IS NOT NULL AND FilterName != ''"
$cmd3.Parameters.AddWithValue("@sid", $sessionId) | Out-Null
$reader3 = $cmd3.ExecuteReader()
$filters = @()
while ($reader3.Read()) {
    $filters += $reader3["FilterName"].ToString()
}
$reader3.Close()
$conn.Close()

Write-Host "  Targets: $($targets -join ', ')" -ForegroundColor Gray
Write-Host "  Filters: $($filters -join ', ')" -ForegroundColor Gray

if ($targets.Count -eq 0) {
    Write-Error "No targets found for session $sessionId"
    exit 1
}

# --- Map test JPEGs to target/filter combinations ---
$testAssetsDir = Join-Path $PSScriptRoot "..\.claude\worktrees\livestack-thumbnails\test-assets"
if (-not (Test-Path $testAssetsDir)) {
    Write-Error "Test assets not found: $testAssetsDir"
    exit 1
}

# Available test JPEGs (mono narrowband-style images)
$testJpegs = @(
    "H_stack_1.jpg", "H_stack_3.jpg", "H_stack_6.jpg",
    "S_stack_0.jpg", "S_stack_7.jpg",
    "O_stack_8.jpg",
    "R_stack_5.jpg", "G_stack_9.jpg", "B_stack_4.jpg"
)

# Use the first target and assign test images to each real filter
$target = $targets[0]
Write-Host ""
Write-Host "Creating live stack assets for target '$target'..." -ForegroundColor Yellow

$assetsDir = Join-Path $reportDir "assets"
New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null

$manifest = @()
$jpegIndex = 0

foreach ($filter in $filters) {
    if ($jpegIndex -ge $testJpegs.Count) { break }

    $srcFile = Join-Path $testAssetsDir $testJpegs[$jpegIndex]
    $destName = "${target}_${filter}.jpg"
    $destPath = Join-Path $assetsDir $destName

    Copy-Item -Path $srcFile -Destination $destPath -Force
    Write-Host "  Copied $($testJpegs[$jpegIndex]) -> $destName" -ForegroundColor Green

    $manifest += @{
        file = $destName
        target = $target
        filter = $filter
        isMonochrome = $true
        stackCount = (Get-Random -Minimum 3 -Maximum 20)
        redStackCount = $null
        greenStackCount = $null
        blueStackCount = $null
    }
    $jpegIndex++
}

# Write manifest
$manifestPath = Join-Path $assetsDir "livestack.json"
$manifest | ConvertTo-Json -Depth 3 | Set-Content -Path $manifestPath -Encoding UTF8

Write-Host ""
Write-Host "Done! Created:" -ForegroundColor Green
Write-Host "  $assetsDir" -ForegroundColor Gray
Write-Host "  $manifestPath" -ForegroundColor Gray
Write-Host "  $($manifest.Count) images for target '$target'" -ForegroundColor Gray
Write-Host ""
Write-Host "Now open NINA -> Night Summary -> Preview Report and select the latest session." -ForegroundColor Cyan
Write-Host "The Live Stack section should appear with the test images." -ForegroundColor Cyan
