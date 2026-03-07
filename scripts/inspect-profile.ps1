$profilePath = "C:\Users\Evan\AppData\Local\NINA\Profiles\537757f5-9608-4e17-941c-24ebef632414.profile"
[xml]$xml = Get-Content $profilePath

# Camera settings
Write-Host "=== CameraSettings ==="
$cam = $xml.Profile.CameraSettings
$cam | Get-Member -MemberType Property | Where-Object { $_.Name -match "Pixel|Sensor|Width|Height|Size|Focal" } | ForEach-Object {
    $name = $_.Name
    Write-Host "  $name = $($cam.$name)"
}

Write-Host ""
Write-Host "PixelSize = $($cam.PixelSize)"

# Telescope settings
Write-Host ""
Write-Host "=== TelescopeSettings ==="
$tel = $xml.Profile.TelescopeSettings
$tel | Get-Member -MemberType Property | Where-Object { $_.Name -match "Focal|Aperture|Length" } | ForEach-Object {
    $name = $_.Name
    Write-Host "  $name = $($tel.$name)"
}

# Framing assistant settings
Write-Host ""
Write-Host "=== FramingAssistantSettings ==="
$fa = $xml.Profile.FramingAssistantSettings
if ($fa) {
    $fa | Get-Member -MemberType Property | ForEach-Object {
        $name = $_.Name
        Write-Host "  $name = $($fa.$name)"
    }
}
