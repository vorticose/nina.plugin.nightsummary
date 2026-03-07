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
$cmd.CommandText = "SELECT name, ra, dec, rotation FROM target ORDER BY name"
$reader = $cmd.ExecuteReader()
Write-Host "=== Target names in TS DB ==="
while ($reader.Read()) {
    Write-Host "  '$($reader['name'])'  ra=$($reader['ra'])  dec=$($reader['dec'])  rotation=$($reader['rotation'])"
}
$reader.Close()
$conn.Close()
