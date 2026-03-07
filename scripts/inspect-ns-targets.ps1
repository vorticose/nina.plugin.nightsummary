$sqliteDir  = "$env:USERPROFILE\.nuget\packages\stub.system.data.sqlite.core.netstandard\1.0.119"
$managedDll = "$sqliteDir\lib\netstandard2.0\System.Data.SQLite.dll"
$nativeDll  = "$sqliteDir\runtimes\win-x64\native\SQLite.Interop.dll"

$tempDir = "$env:TEMP\sqliteinspect"
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null
Copy-Item $nativeDll $tempDir -Force
[System.IO.Directory]::SetCurrentDirectory($tempDir)
Add-Type -Path $managedDll

$dbPath = "C:\Users\Evan\AppData\Local\NINA\Plugins\3.2.0.9001\NightSummary\test\nightsummary.sqlite"
$conn = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$dbPath;Version=3;Read Only=True;")
$conn.Open()

$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT DISTINCT TargetName FROM Images ORDER BY TargetName"
$reader = $cmd.ExecuteReader()
Write-Host "=== Target names in Night Summary test DB ==="
while ($reader.Read()) { Write-Host "  '$($reader['TargetName'])'" }
$reader.Close()
$conn.Close()
