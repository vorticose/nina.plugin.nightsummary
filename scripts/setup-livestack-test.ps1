# setup-livestack-test.ps1
# Creates fake live stack assets alongside the most recent saved report
# so the Preview/Resend paths can load them for testing.
#
# Usage: Run on the Windows rig from the repo root.
#   .\scripts\setup-livestack-test.ps1 -Targets "Seagull Nebula","M 101" -Filters "H","O","S"
#   .\scripts\setup-livestack-test.ps1  (will prompt interactively)

param(
    [string[]]$Targets,
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
if (-not $Targets -or $Targets.Count -eq 0) {
    $raw = Read-Host "Enter target names comma-separated (e.g. 'Seagull Nebula, M 101, Lagoon Nebula')"
    $Targets = $raw -split "," | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" }
}
if (-not $Filters -or $Filters.Count -eq 0) {
    $raw = Read-Host "Enter filter names comma-separated (e.g. 'H,O,S' or 'L,R,G,B,H,O,S')"
    $Filters = $raw -split "," | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" }
}

if ($Targets.Count -eq 0 -or $Filters.Count -eq 0) {
    Write-Error "At least one target and one filter are required."
    exit 1
}

Write-Host "  Targets: $($Targets -join ', ')" -ForegroundColor Gray
Write-Host "  Filters: $($Filters -join ', ')" -ForegroundColor Gray

# --- Map test JPEGs to target/filter combinations ---
$testAssetsDir = Join-Path $PSScriptRoot "test-assets"
if (-not (Test-Path $testAssetsDir)) {
    Write-Error "Test assets not found: $testAssetsDir"
    exit 1
}

# Available test JPEGs (mono narrowband-style images) - cycled across targets/filters
$testJpegs = @(
    "H_stack_1.jpg", "H_stack_3.jpg", "H_stack_6.jpg",
    "S_stack_0.jpg", "S_stack_7.jpg",
    "O_stack_8.jpg",
    "R_stack_5.jpg", "G_stack_9.jpg", "B_stack_4.jpg"
)

$assetsDir = Join-Path $reportDir "assets"
New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null

$manifest = @()
$jpegIndex = 0

foreach ($target in $Targets) {
    Write-Host ""
    Write-Host "Creating live stack assets for target '$target'..." -ForegroundColor Yellow

    foreach ($filter in $Filters) {
        $srcFile = Join-Path $testAssetsDir $testJpegs[$jpegIndex % $testJpegs.Count]
        $destName = "${target}_${filter}.jpg"
        $destPath = Join-Path $assetsDir $destName

        Copy-Item -Path $srcFile -Destination $destPath -Force
        Write-Host "  Copied $($testJpegs[$jpegIndex % $testJpegs.Count]) -> $destName" -ForegroundColor Green

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
}

# Write manifest
$manifestPath = Join-Path $assetsDir "livestack.json"
$manifest | ConvertTo-Json -Depth 3 | Set-Content -Path $manifestPath -Encoding UTF8

Write-Host ""
Write-Host "Done! Created:" -ForegroundColor Green
Write-Host "  $assetsDir" -ForegroundColor Gray
Write-Host "  $manifestPath" -ForegroundColor Gray
Write-Host "  $($manifest.Count) images across $($Targets.Count) target(s)" -ForegroundColor Gray
Write-Host ""
Write-Host "Now open NINA -> Night Summary -> Preview Report and select the latest session." -ForegroundColor Cyan
Write-Host "The Live Stack section should appear with the test images." -ForegroundColor Cyan
