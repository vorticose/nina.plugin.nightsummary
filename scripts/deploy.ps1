# Night Summary - Deploy Script
# Usage: .\scripts\deploy.ps1
#
# What this does:
#   1. Builds the plugin in Release
#   2. Creates NINA.Plugin.NightSummary.zip (excluding .pdb)
#   3. Calculates SHA256 checksum
#   4. Updates manifest.json and repository.json with new version, download URL, and checksum
#   5. Copies the DLL to local NINA plugins folder
#
# After running this script:
#   1. Create a GitHub Release tagged v{version} and upload the zip from scripts/
#   2. git add manifest.json && git commit -m "Release v{version}" && git push

$ErrorActionPreference = "Stop"
$repoRoot   = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $repoRoot "NINA.Plugin.Template"
$buildDir   = Join-Path $projectDir "bin\Release\net8.0-windows"
$zipPath    = Join-Path $PSScriptRoot "NINA.Plugin.NightSummary.zip"
$manifestPath = Join-Path $repoRoot "manifest.json"
$ninaPluginDir = Join-Path $env:LOCALAPPDATA "NINA\Plugins\3.0.0\NightSummary"

# --- Build ---
Write-Host "Building..." -ForegroundColor Cyan
dotnet build "$projectDir\NINA.Plugin.NightSummary.csproj" -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed."; exit 1 }
Write-Host "Build succeeded." -ForegroundColor Green

# --- Read version from DLL ---
$dll = Join-Path $buildDir "NINA.Plugin.NightSummary.dll"
$version = [System.Reflection.AssemblyName]::GetAssemblyName($dll).Version
$versionStr = "$($version.Major).$($version.Minor).$($version.Build)"
Write-Host "Version: $versionStr" -ForegroundColor Cyan

# --- Create zip ---
if (Test-Path $zipPath) { Remove-Item $zipPath }
Add-Type -Assembly "System.IO.Compression.FileSystem"
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, "Create")
Get-ChildItem -Path $buildDir -Recurse -File | Where-Object { $_.Extension -ne ".pdb" } | ForEach-Object {
    $entryName = $_.FullName.Substring($buildDir.Length + 1)
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entryName) | Out-Null
}
$zip.Dispose()
Write-Host "Zip created: $zipPath" -ForegroundColor Green

# --- Checksum ---
$checksum = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash
Write-Host "SHA256: $checksum" -ForegroundColor Cyan

# --- Update manifest.json ---
$downloadUrl = "https://github.com/vorticose/nina.plugin.nightsummary/releases/download/v$versionStr/NINA.Plugin.NightSummary.zip"
$manifest = Get-Content $manifestPath | ConvertFrom-Json
$manifest[0].Version.Major = $version.Major
$manifest[0].Version.Minor = $version.Minor
$manifest[0].Version.Patch = $version.Build
$manifest[0].Version.Build = $version.Revision
$manifest[0].Installer.URL = $downloadUrl
$manifest[0].Installer.Checksum = $checksum
$manifest | ConvertTo-Json -Depth 10 | Set-Content $manifestPath
Write-Host "manifest.json updated." -ForegroundColor Green

# --- Update repository.json ---
$repoJsonPath = Join-Path $repoRoot "repository.json"
$repoJson = @(Get-Content $repoJsonPath | ConvertFrom-Json)
$repoJson[0].Version.Major = $version.Major
$repoJson[0].Version.Minor = $version.Minor
$repoJson[0].Version.Patch = $version.Build
$repoJson[0].Version.Build = $version.Revision
$repoJson[0].Installer.URL = $downloadUrl
$repoJson[0].Installer.Checksum = $checksum
$repoJson | ConvertTo-Json -Depth 10 | Set-Content $repoJsonPath
Write-Host "repository.json updated." -ForegroundColor Green

# --- Deploy locally ---
if (Test-Path $ninaPluginDir) {
    Copy-Item $dll $ninaPluginDir -Force
    Write-Host "Deployed to local NINA plugins folder." -ForegroundColor Green
} else {
    Write-Host "Local NINA plugin folder not found - skipping local deploy." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor White
Write-Host "  1. Go to github.com/vorticose/nina.plugin.nightsummary/releases/new" -ForegroundColor White
Write-Host "  2. Tag: v$versionStr  |  Title: Night Summary v$versionStr" -ForegroundColor White
Write-Host "  3. Upload: $zipPath" -ForegroundColor White
$step4 = "  4. git add manifest.json repository.json; git commit -m Release-v$versionStr; git push"
Write-Host $step4 -ForegroundColor White
