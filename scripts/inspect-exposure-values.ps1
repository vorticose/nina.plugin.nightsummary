$dbPath    = "C:\Users\Evan\AppData\Local\NINA\SchedulerPlugin\schedulerdb.sqlite"
$sqliteDir = "$env:USERPROFILE\.nuget\packages\stub.system.data.sqlite.core.netstandard\1.0.119"
$managedDll = "$sqliteDir\lib\netstandard2.0\System.Data.SQLite.dll"
$nativeDll  = "$sqliteDir\runtimes\win-x64\native\SQLite.Interop.dll"

$tempDir = "$env:TEMP\sqliteinspect"
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null
Copy-Item $nativeDll $tempDir -Force
[System.IO.Directory]::SetCurrentDirectory($tempDir)
Add-Type -Path $managedDll

$conn = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$dbPath;Version=3;Read Only=True;")
$conn.Open()

$cmd = $conn.CreateCommand()
$cmd.CommandText = @"
SELECT et.name, et.filtername, ep.exposure, et.defaultexposure, ep.desired, ep.acquired, ep.accepted
FROM exposureplan ep
JOIN exposuretemplate et ON et.Id = ep.exposureTemplateId
WHERE ep.desired > 0
LIMIT 20
"@
$reader = $cmd.ExecuteReader()
Write-Host "=== Exposure plan values ==="
while ($reader.Read()) {
    Write-Host "  Template=$($reader['name'])  Filter=$($reader['filtername'])  ep.exposure=$($reader['exposure'])  et.defaultexposure=$($reader['defaultexposure'])  desired=$($reader['desired'])  acquired=$($reader['acquired'])  accepted=$($reader['accepted'])"
}
$reader.Close()
$conn.Close()
