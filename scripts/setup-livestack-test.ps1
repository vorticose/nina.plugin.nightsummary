# setup-livestack-test.ps1
# Creates fake live stack assets alongside the most recent saved report
# so the Preview/Resend paths can load them for testing.
#
# Usage: Run on the Windows rig from the repo root.
#   .\scripts\setup-livestack-test.ps1 -Target "Cat 91" -Filters "H","O","S"
#   .\scripts\setup-livestack-test.ps1 -Target "Cat 91" -Filters "L","R","G","B","H","O","S"
#
# If -Target or -Filters are omitted, you will be prompted.

param(
    [string]$Target,
    [string[]]$Filters
)

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

# --- Get target and filter names ---
if (-not $Target) {
    $Target = Read-Host "Enter target name (e.g. 'Cat 91', 'M42')"
}
if (-not $Filters -or $Filters.Count -eq 0) {
    $input = Read-Host "Enter filter names comma-separated (e.g. 'H,O,S' or 'L,R,G,B,H,O,S')"
    $Filters = $input -split "," | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" }
}

if (-not $Target -or $Filters.Count -eq 0) {
    Write-Error "Target and at least one filter are required."
    exit 1
}

Write-Host "  Target: $Target" -ForegroundColor Gray
Write-Host "  Filters: $($Filters -join ', ')" -ForegroundColor Gray

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

Write-Host ""
Write-Host "Creating live stack assets for target '$Target'..." -ForegroundColor Yellow

$assetsDir = Join-Path $reportDir "assets"
New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null

$manifest = @()
$jpegIndex = 0

foreach ($filter in $Filters) {
    if ($jpegIndex -ge $testJpegs.Count) { break }

    $srcFile = Join-Path $testAssetsDir $testJpegs[$jpegIndex]
    $destName = "${Target}_${filter}.jpg"
    $destPath = Join-Path $assetsDir $destName

    Copy-Item -Path $srcFile -Destination $destPath -Force
    Write-Host "  Copied $($testJpegs[$jpegIndex]) -> $destName" -ForegroundColor Green

    $manifest += @{
        file = $destName
        target = $Target
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
Write-Host "  $($manifest.Count) images for target '$Target'" -ForegroundColor Gray
Write-Host ""
Write-Host "Now open NINA -> Night Summary -> Preview Report and select the latest session." -ForegroundColor Cyan
Write-Host "The Live Stack section should appear with the test images." -ForegroundColor Cyan
