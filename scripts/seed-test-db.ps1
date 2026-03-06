# Seed test database with dummy session data including timeline events
# Run this once to populate the test database used by "Send Test Report"

$dbPath    = "$env:LOCALAPPDATA\NINA\Plugins\3.2.0.9001\NightSummary\test\nightsummary.sqlite"
$sqliteDir = "$env:USERPROFILE\.nuget\packages\stub.system.data.sqlite.core.netstandard\1.0.119"
$managedDll = "$sqliteDir\lib\netstandard2.0\System.Data.SQLite.dll"
$nativeDll  = "$sqliteDir\runtimes\win-x64\native\SQLite.Interop.dll"

# Copy native interop to same dir as managed DLL so it can be found at runtime
$tempDir = "$env:TEMP\sqlite-ps"
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null
Copy-Item $managedDll $tempDir -Force
Copy-Item $nativeDll  $tempDir -Force
[System.Reflection.Assembly]::LoadFrom("$tempDir\System.Data.SQLite.dll") | Out-Null

$conn = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$dbPath;Version=3;")
$conn.Open()

function Exec($sql, $params = @{}) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    foreach ($k in $params.Keys) { $cmd.Parameters.AddWithValue($k, $params[$k]) | Out-Null }
    $cmd.ExecuteNonQuery() | Out-Null
}

# Ensure SessionEvents table exists
Exec @"
CREATE TABLE IF NOT EXISTS SessionEvents (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId TEXT NOT NULL,
    Timestamp TEXT NOT NULL,
    EventType TEXT NOT NULL,
    Description TEXT
)
"@

# Get the most recent session
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT SessionId, SessionStart, SessionEnd FROM Sessions ORDER BY SessionStart DESC LIMIT 1"
$reader = $cmd.ExecuteReader()
if (-not $reader.Read()) {
    $reader.Close()
    Write-Host "No sessions found in test database." -ForegroundColor Red
    $conn.Close()
    exit 1
}
$sessionId    = $reader["SessionId"].ToString()
$sessionStart = [DateTime]::Parse($reader["SessionStart"].ToString())
$sessionEnd   = [DateTime]::Parse($reader["SessionEnd"].ToString())
$reader.Close()
Write-Host "Using session: $sessionId ($($sessionStart.ToString('yyyy-MM-dd HH:mm')) - $($sessionEnd.ToString('HH:mm')))" -ForegroundColor Cyan

# Clear existing events for this session
Exec "DELETE FROM SessionEvents WHERE SessionId = @sid" @{ "@sid" = $sessionId }

$totalMinutes = ($sessionEnd - $sessionStart).TotalMinutes

function OffsetTime($minutes) {
    return $sessionStart.AddMinutes($minutes).ToString("o")
}

# Events distributed across the session
$t1  = $totalMinutes * 0.04
$t2  = $totalMinutes * 0.15
$t3  = $totalMinutes * 0.38
$t4  = $totalMinutes * 0.55
$t5  = $totalMinutes * 0.67
$t6  = $totalMinutes * 0.88
$t7  = $totalMinutes * 0.91
$t8  = $totalMinutes * 0.97

$events = @(
    @{ Time = $t1;  Type = "RoofOpen";     Desc = "Safety monitor: Safe - roof opened" }
    @{ Time = $t2;  Type = "AutoFocus";    Desc = "AutoFocus completed - Filter: L, Temp: 2.4C, Position: 38420" }
    @{ Time = $t3;  Type = "AutoFocus";    Desc = "AutoFocus completed - Filter: Ha, Temp: 1.9C, Position: 38490" }
    @{ Time = $t4;  Type = "MeridianFlip"; Desc = "Meridian flip completed successfully" }
    @{ Time = $t5;  Type = "AutoFocus";    Desc = "AutoFocus completed - Filter: Ha, Temp: 1.6C, Position: 38505" }
    @{ Time = $t6;  Type = "RoofClosed";   Desc = "Safety monitor: Unsafe - roof closed" }
    @{ Time = $t7;  Type = "RoofOpen";     Desc = "Safety monitor: Safe - roof opened" }
    @{ Time = $t8;  Type = "AutoFocus";    Desc = "AutoFocus completed - Filter: OIII, Temp: 0.8C, Position: 38512" }
)

foreach ($e in $events) {
    Exec "INSERT INTO SessionEvents (SessionId, Timestamp, EventType, Description) VALUES (@sid, @ts, @type, @desc)" @{
        "@sid"  = $sessionId
        "@ts"   = (OffsetTime $e.Time)
        "@type" = $e.Type
        "@desc" = $e.Desc
    }
    Write-Host "  Added $($e.Type) at +$([int]$e.Time) min" -ForegroundColor Gray
}

$conn.Close()
Write-Host ""
Write-Host "Done. $($events.Count) events seeded into test database." -ForegroundColor Green
Write-Host "Click 'Send Test Report' in NINA settings to see the timeline." -ForegroundColor Green
