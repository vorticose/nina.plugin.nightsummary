$profilePath = "C:\Users\Evan\AppData\Local\NINA\Profiles\537757f5-9608-4e17-941c-24ebef632414.profile"

[xml]$xml = Get-Content $profilePath

$fa = $xml.Profile.FramingAssistantSettings
$fa.CameraWidth  = "6248"
$fa.CameraHeight = "4176"

$xml.Save($profilePath)

Write-Host "Updated FramingAssistantSettings:" -ForegroundColor Cyan
Write-Host "  CameraWidth  = $($fa.CameraWidth)"
Write-Host "  CameraHeight = $($fa.CameraHeight)"
Write-Host "Done." -ForegroundColor Green
