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

# List all tables
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"
$reader = $cmd.ExecuteReader()
Write-Host "=== Tables in TS DB ==="
while ($reader.Read()) { Write-Host "  $($reader['name'])" }
$reader.Close()

# Show schema for each table
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"
$tableReader = $cmd2.ExecuteReader()
$tables = @()
while ($tableReader.Read()) { $tables += $tableReader['name'] }
$tableReader.Close()

foreach ($table in $tables) {
    Write-Host "`n=== Schema: $table ==="
    $schCmd = $conn.CreateCommand()
    $schCmd.CommandText = "PRAGMA table_info('$table')"
    $schReader = $schCmd.ExecuteReader()
    while ($schReader.Read()) {
        Write-Host "  $($schReader['name'])  [$($schReader['type'])]"
    }
    $schReader.Close()

    # Show row count
    $cntCmd = $conn.CreateCommand()
    $cntCmd.CommandText = "SELECT COUNT(*) FROM [$table]"
    $count = $cntCmd.ExecuteScalar()
    Write-Host "  --> $count rows"
}

$conn.Close()
