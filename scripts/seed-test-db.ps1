# Seed test database with dummy session data including timeline events and historical sessions
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

# Rename image targets to match whatever names exist in the local TS database
# so the Target Progress section renders correctly on any machine.
$tsDbPath = "$env:LOCALAPPDATA\NINA\SchedulerPlugin\schedulerdb.sqlite"
if (Test-Path $tsDbPath) {
    $tsConn = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$tsDbPath;Version=3;Read Only=True;")
    $tsConn.Open()
    $tsCmd = $tsConn.CreateCommand()
    $tsCmd.CommandText = "SELECT name FROM target ORDER BY name"
    $tsReader = $tsCmd.ExecuteReader()
    $tsNames = @()
    while ($tsReader.Read()) { $tsNames += $tsReader["name"].ToString() }
    $tsReader.Close()
    $tsConn.Close()

    # Get distinct target names currently in the test session
    $nsCmd = $conn.CreateCommand()
    $nsCmd.CommandText = "SELECT DISTINCT TargetName FROM Images WHERE SessionId = @sid"
    $nsCmd.Parameters.AddWithValue("@sid", $sessionId) | Out-Null
    $nsReader = $nsCmd.ExecuteReader()
    $nsNames = @()
    while ($nsReader.Read()) { $nsNames += $nsReader["TargetName"].ToString() }
    $nsReader.Close()

    # For each NS target, find the best matching TS target name (case-insensitive, ignoring " (N)" suffixes)
    foreach ($nsName in $nsNames) {
        $baseName = $nsName -replace '\s*\(\d+\)$', ''
        $match = $tsNames | Where-Object { ($_ -replace '\s*\(\d+\)$', '') -eq $baseName } | Select-Object -First 1
        if ($match -and $match -ne $nsName) {
            Exec "UPDATE Images SET TargetName = @newName WHERE SessionId = @sid AND TargetName = @oldName" @{
                "@newName" = $match; "@sid" = $sessionId; "@oldName" = $nsName
            }
            Write-Host "  Renamed '$nsName' -> '$match' to match TS database." -ForegroundColor Cyan
        }
    }
    Write-Host "Target names synced with TS database." -ForegroundColor Cyan
} else {
    Write-Host "TS database not found - skipping target name sync." -ForegroundColor Yellow
}

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

# Historical sessions for Session History section
# Read distinct target+filter combos from the current session
$fCmd = $conn.CreateCommand()
$fCmd.CommandText = "SELECT TargetName, Filter, ExposureDuration FROM Images WHERE SessionId = @sid GROUP BY TargetName, Filter"
$fCmd.Parameters.AddWithValue("@sid", $sessionId) | Out-Null
$fReader = $fCmd.ExecuteReader()
$filterRows = @()
while ($fReader.Read()) {
    $filterRows += @{
        Target = $fReader["TargetName"].ToString()
        Filter = $fReader["Filter"].ToString()
        ExpDur = [double]$fReader["ExposureDuration"]
    }
}
$fReader.Close()
Write-Host "Found $($filterRows.Count) target/filter combos for history seeding." -ForegroundColor Gray

# Remove previously seeded historical sessions
$hCmd = $conn.CreateCommand()
$hCmd.CommandText = "SELECT SessionId FROM Sessions WHERE ProfileName = 'Test-History'"
$hReader = $hCmd.ExecuteReader()
$oldIds = @()
while ($hReader.Read()) { $oldIds += $hReader["SessionId"].ToString() }
$hReader.Close()
foreach ($oldId in $oldIds) {
    Exec "DELETE FROM Images WHERE SessionId = @sid"        @{ "@sid" = $oldId }
    Exec "DELETE FROM SessionEvents WHERE SessionId = @sid" @{ "@sid" = $oldId }
    Exec "DELETE FROM Sessions WHERE SessionId = @sid"      @{ "@sid" = $oldId }
    Write-Host "  Removed old history session: $oldId" -ForegroundColor DarkGray
}

# Seed 2 historical nights with slightly different quality metrics
$totalHistSessions = 0

foreach ($weeksAgo in @(4, 2)) {
    $hfr_mult  = if ($weeksAgo -eq 4) { 1.15 } else { 0.92 }
    $fwhm_mult = if ($weeksAgo -eq 4) { 1.20 } else { 0.88 }
    $rms_mult  = if ($weeksAgo -eq 4) { 1.10 } else { 0.95 }
    $imgCount  = if ($weeksAgo -eq 4) { 40   } else { 65   }

    $hSid   = [System.Guid]::NewGuid().ToString()
    $hStart = $sessionStart.AddDays(-($weeksAgo * 7)).Date.AddHours(21)
    $hEnd   = $hStart.AddHours(6)

    Exec "INSERT INTO Sessions (SessionId, SessionStart, SessionEnd, ProfileName, Notes, ReportSent) VALUES (@sid, @start, @end, @prof, '', 1)" @{
        "@sid"   = $hSid
        "@start" = $hStart.ToString("o")
        "@end"   = $hEnd.ToString("o")
        "@prof"  = "Test-History"
    }

    $perCombo = [math]::Max(3, [math]::Floor($imgCount / [math]::Max(1, $filterRows.Count)))
    $step     = ($hEnd - $hStart).TotalMinutes / [math]::Max(1, $filterRows.Count * $perCombo)
    $elapsed  = 0.0

    foreach ($row in $filterRows) {
        for ($i = 0; $i -lt $perCombo; $i++) {
            $ts   = $hStart.AddMinutes($elapsed).ToString("o")
            $hfr  = [math]::Round(1.85 * $hfr_mult  + (Get-Random -Minimum -15 -Maximum 15) / 100.0, 2)
            $fwhm = [math]::Round(2.10 * $fwhm_mult + (Get-Random -Minimum -20 -Maximum 20) / 100.0, 2)
            $rms  = [math]::Round(0.52 * $rms_mult  + (Get-Random -Minimum -8  -Maximum 8 ) / 100.0, 3)
            $stars = [math]::Max(50, 380 + (Get-Random -Minimum -60 -Maximum 60))

            Exec "INSERT INTO Images (SessionId, Timestamp, TargetName, Filter, ExposureDuration, HFR, FWHM, Eccentricity, StarCount, GuidingRMSTotal, GuidingScale, Accepted) VALUES (@sid, @ts, @target, @filter, @exp, @hfr, @fwhm, 0.42, @stars, @rms, 1.32, 1)" @{
                "@sid"    = $hSid
                "@ts"     = $ts
                "@target" = $row.Target
                "@filter" = $row.Filter
                "@exp"    = $row.ExpDur
                "@hfr"    = $hfr
                "@fwhm"   = $fwhm
                "@stars"  = $stars
                "@rms"    = $rms
            }
            $elapsed += $step
        }
    }

    $dateLabel = $hStart.ToString("yyyy-MM-dd")
    $imgTotal  = $filterRows.Count * $perCombo
    Write-Host "  Seeded $weeksAgo weeks ago: $dateLabel ($imgTotal images)" -ForegroundColor DarkCyan
    $totalHistSessions++
}

$conn.Close()
Write-Host ""
Write-Host "Done. $($events.Count) timeline events + $totalHistSessions historical sessions seeded." -ForegroundColor Green
Write-Host "Click 'Send Test Report' in NINA settings to see the session history." -ForegroundColor Green
