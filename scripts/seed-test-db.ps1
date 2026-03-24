# Night Summary - Seed Test Database
# Creates a fully self-contained demo session with realistic synthetic data.
# Run this to populate the test database used by "Send Test Report" in NINA.
#
# Targets:
#   M31 - Andromeda Galaxy      (broadband: L R G B)
#   Rosette Nebula - NGC 2244   (narrowband: Ha OIII SII)
#
# Usage: .\scripts\seed-test-db.ps1

$dbPath    = "$env:LOCALAPPDATA\NINA\NightSummary\test\nightsummary.sqlite"
$sqliteDir = "$env:USERPROFILE\.nuget\packages\stub.system.data.sqlite.core.netstandard\1.0.119"
$managedDll = "$sqliteDir\lib\netstandard2.0\System.Data.SQLite.dll"
$nativeDll  = "$sqliteDir\runtimes\win-x64\native\SQLite.Interop.dll"

# Ensure DB directory exists
$dbDir = Split-Path $dbPath
if (-not (Test-Path $dbDir)) { New-Item -ItemType Directory -Force -Path $dbDir | Out-Null }

# Load SQLite
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

function Rnd([double]$min, [double]$max) { [math]::Round([double]$min + [double](Get-Random -Minimum 0 -Maximum 1000) / 1000.0 * ([double]$max - [double]$min), 3) }
function RndInt($min, $max) { Get-Random -Minimum $min -Maximum ($max + 1) }

# â”€â”€ Schema â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

Exec @"
CREATE TABLE IF NOT EXISTS Sessions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId TEXT NOT NULL,
    SessionStart TEXT NOT NULL,
    SessionEnd TEXT,
    ProfileName TEXT,
    Notes TEXT,
    ReportSent INTEGER DEFAULT 0,
    CamXSize INTEGER DEFAULT 0,
    CamYSize INTEGER DEFAULT 0,
    PixelSizeMicrons REAL DEFAULT 0,
    FocalLengthMm REAL DEFAULT 0
)
"@

Exec @"
CREATE TABLE IF NOT EXISTS Images (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId TEXT NOT NULL,
    Timestamp TEXT NOT NULL,
    TargetName TEXT,
    Filter TEXT,
    ExposureDuration REAL,
    HFR REAL,
    FWHM REAL DEFAULT 0,
    Eccentricity REAL DEFAULT 0,
    StarCount INTEGER,
    GuidingRMSTotal REAL,
    GuidingScale REAL,
    Accepted INTEGER DEFAULT 1,
    RaHours REAL DEFAULT 0,
    DecDegrees REAL DEFAULT 0,
    FocuserTemp REAL,
    AmbientTemp REAL,
    Gain INTEGER DEFAULT -1,
    Offset INTEGER DEFAULT -1,
    Binning INTEGER DEFAULT 0,
    CameraTemp REAL,
    CoolerSetpoint REAL,
    FocuserPosition INTEGER,
    RotatorPosition REAL,
    Humidity REAL,
    DewPoint REAL,
    WindSpeed REAL,
    Pressure REAL,
    GradingStatus INTEGER DEFAULT -1,
    RejectReason TEXT
)
"@

Exec @"
CREATE TABLE IF NOT EXISTS SessionEvents (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId TEXT NOT NULL,
    Timestamp TEXT NOT NULL,
    EventType TEXT NOT NULL,
    Description TEXT,
    AfSucceeded INTEGER,
    AfHfr REAL
)
"@

# Migrate older DBs that predate newer columns
foreach ($col in @(
    @{ Table = “Sessions”; Def = “CamXSize INTEGER DEFAULT 0” }
    @{ Table = “Sessions”; Def = “CamYSize INTEGER DEFAULT 0” }
    @{ Table = “Sessions”; Def = “PixelSizeMicrons REAL DEFAULT 0” }
    @{ Table = “Sessions”; Def = “FocalLengthMm REAL DEFAULT 0” }
    @{ Table = “Images”;   Def = “FWHM REAL DEFAULT 0” }
    @{ Table = “Images”;   Def = “Eccentricity REAL DEFAULT 0” }
    @{ Table = “Images”;   Def = “RaHours REAL DEFAULT 0” }
    @{ Table = “Images”;   Def = “DecDegrees REAL DEFAULT 0” }
    @{ Table = “Images”;   Def = “FocuserTemp REAL” }
    @{ Table = “Images”;   Def = “AmbientTemp REAL” }
    @{ Table = “Images”;   Def = “Gain INTEGER DEFAULT -1” }
    @{ Table = “Images”;   Def = “Offset INTEGER DEFAULT -1” }
    @{ Table = “Images”;   Def = “Binning INTEGER DEFAULT 0” }
    @{ Table = “Images”;   Def = “CameraTemp REAL” }
    @{ Table = “Images”;   Def = “CoolerSetpoint REAL” }
    @{ Table = “Images”;   Def = “FocuserPosition INTEGER” }
    @{ Table = “Images”;   Def = “RotatorPosition REAL” }
    @{ Table = “Images”;   Def = “Humidity REAL” }
    @{ Table = “Images”;   Def = “DewPoint REAL” }
    @{ Table = “Images”;   Def = “WindSpeed REAL” }
    @{ Table = “Images”;   Def = “Pressure REAL” }
    @{ Table = “Images”;   Def = “GradingStatus INTEGER DEFAULT -1” }
    @{ Table = “Images”;   Def = “RejectReason TEXT” }
    @{ Table = “SessionEvents”; Def = “AfSucceeded INTEGER” }
    @{ Table = “SessionEvents”; Def = “AfHfr REAL” }
    @{ Table = “Sessions”;      Def = “SkippedExposures INTEGER DEFAULT 0” }
)) {
    try { Exec “ALTER TABLE $($col.Table) ADD COLUMN $($col.Def)” } catch { }
}

# Wipe previous demo data â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT SessionId FROM Sessions WHERE ProfileName IN ('Night Summary Demo', 'Demo-History')"
$reader = $cmd.ExecuteReader()
$oldIds = @()
while ($reader.Read()) { $oldIds += $reader["SessionId"].ToString() }
$reader.Close()

foreach ($id in $oldIds) {
    Exec "DELETE FROM Images        WHERE SessionId = @s" @{ "@s" = $id }
    Exec "DELETE FROM SessionEvents WHERE SessionId = @s" @{ "@s" = $id }
    Exec "DELETE FROM Sessions      WHERE SessionId = @s" @{ "@s" = $id }
}
if ($oldIds.Count -gt 0) { Write-Host "Cleared $($oldIds.Count) previous demo session(s)." -ForegroundColor DarkGray }

# â”€â”€ Session definition â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

$sessionId    = [System.Guid]::NewGuid().ToString()
$sessionStart = [DateTime]::new(2025, 10, 15, 21, 0, 0)   # Oct 15 2025, 9:00 PM
$sessionEnd   = [DateTime]::new(2025, 10, 16,  6,  0, 0)  # Oct 16 2025, 6:00 AM (9h session)

# Camera / scope profile (realistic mid-range rig)
# ASI2600MC-equivalent: 6248x4176, 3.76Âµm pixel, 700mm focal length
# â†’ ~1.1"/px, FOV ~1.9Â°x1.3Â° â€” reasonable for both targets
$camX     = 6248
$camY     = 4176
$pixelSz  = 3.76
$focalLen = 700.0

Exec "INSERT INTO Sessions (SessionId, SessionStart, SessionEnd, ProfileName, Notes, ReportSent, CamXSize, CamYSize, PixelSizeMicrons, FocalLengthMm, SkippedExposures)
      VALUES (@sid, @start, @end, @prof, @notes, 0, @cx, @cy, @px, @fl, @skipped)" @{
    "@sid"     = $sessionId
    "@start"   = $sessionStart.ToString("o")
    "@end"     = $sessionEnd.ToString("o")
    "@prof"    = "Night Summary Demo"
    "@notes"   = "Demo session - generated by seed-test-db.ps1"
    "@cx"      = $camX
    "@cy"      = $camY
    "@px"      = $pixelSz
    "@fl"      = $focalLen
    "@skipped" = 5
}

Write-Host "Session: $sessionId" -ForegroundColor Cyan
Write-Host "  $($sessionStart.ToString('yyyy-MM-dd HH:mm')) â†’ $($sessionEnd.ToString('HH:mm'))" -ForegroundColor Cyan

# â”€â”€ Target definitions â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

$targets = @(
    @{
        Name       = "M31 - Andromeda Galaxy"
        RaHours    = 0.7123    # 00h 42m 44s
        DecDegrees = 41.269    # +41Â° 16'
        Filters    = @(
            @{ Name = "L";  ExpSec = 120; Count = 30; BaseHFR = 1.75; BaseFWHM = 2.00; BaseEcc = 0.37; BaseStars = 420 }
            @{ Name = "R";  ExpSec = 120; Count = 20; BaseHFR = 1.82; BaseFWHM = 2.08; BaseEcc = 0.39; BaseStars = 395 }
            @{ Name = "G";  ExpSec = 120; Count = 20; BaseHFR = 1.79; BaseFWHM = 2.05; BaseEcc = 0.38; BaseStars = 405 }
            @{ Name = "B";  ExpSec = 120; Count = 20; BaseHFR = 1.88; BaseFWHM = 2.15; BaseEcc = 0.41; BaseStars = 380 }
        )
        StartOffset = 10       # minutes from session start (9:10 PM)
    }
    @{
        Name       = "Rosette Nebula - NGC 2244"
        RaHours    = 6.5625    # 06h 33m 45s
        DecDegrees = 4.998     # +04 59'
        Filters    = @(
            @{ Name = "Ha";   ExpSec = 300; Count = 10; BaseHFR = 2.05; BaseFWHM = 2.30; BaseEcc = 0.43; BaseStars = 290 }
            @{ Name = "OIII"; ExpSec = 300; Count = 8;  BaseHFR = 2.12; BaseFWHM = 2.40; BaseEcc = 0.45; BaseStars = 270 }
            @{ Name = "SII";  ExpSec = 300; Count = 4;  BaseHFR = 2.18; BaseFWHM = 2.48; BaseEcc = 0.46; BaseStars = 255 }
        )
        StartOffset = 420      # minutes from session start (4:00 AM -- Rosette above 20 deg elevation)
    }
)

# â”€â”€ Seed images â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

# Safety monitor closure windows: [startOffset, endOffset] in minutes
$closedWindows = @( @(320, 340) )  # roof closes during the gap between targets (~1:20 AM)

function IsUnsafe($offsetMin) {
    foreach ($w in $closedWindows) { if ($offsetMin -ge $w[0] -and $offsetMin -lt $w[1]) { return $true } }
    return $false
}

$totalImages = 0
$elapsed     = 0.0   # running clock in minutes from session start

foreach ($target in $targets) {
    $elapsed = $target.StartOffset
    Write-Host "  Target: $($target.Name)" -ForegroundColor White

    foreach ($f in $target.Filters) {
        $rejected = 0

        for ($i = 0; $i -lt $f.Count; $i++) {
            # Skip frames during roof-closed windows; advance clock through closure
            while (IsUnsafe $elapsed) { $elapsed += 0.5 }

            $ts = $sessionStart.AddMinutes($elapsed).ToString("o")

            # Simulate slight HFR drift over time (focuser temp drop)
            $drift    = $i * 0.003
            $hfr      = [math]::Round($f.BaseHFR  + $drift + (Rnd -0.12 0.12), 2)
            $fwhm     = [math]::Round($f.BaseFWHM + $drift + (Rnd -0.15 0.15), 2)
            $ecc      = [math]::Round($f.BaseEcc         + (Rnd -0.04 0.04), 3)
            $stars    = RndInt ($f.BaseStars - 40) ($f.BaseStars + 40)
            $rms      = [math]::Round(0.48 + (Rnd -0.08 0.08), 3)
            $focPos   = RndInt 38200 38600
            $focTemp  = [math]::Round(8.5  - ($elapsed / 60.0) * 0.4 + (Rnd -0.2 0.2), 1)
            $ambTemp  = [math]::Round(6.0  - ($elapsed / 60.0) * 0.3 + (Rnd -0.3 0.3), 1)

            # Reject ~5% of frames with plausible reasons
            $accepted      = 1
            $gradingStatus = 1
            $rejectReason  = $null
            $roll = RndInt 1 100
            if ($roll -le 5) {
                $accepted      = 0
                $gradingStatus = 2
                $rejectReason  = @("HFR too high", "Star count below threshold", "Guiding RMS exceeded limit") | Get-Random
                # Make the bad frame look bad
                $hfr   = [math]::Round($hfr   * 1.6 + (Rnd 0.1 0.4), 2)
                $fwhm  = [math]::Round($fwhm  * 1.5 + (Rnd 0.1 0.3), 2)
                $stars = RndInt 60 120
                $rejected++
            }

            Exec @"
INSERT INTO Images (
    SessionId, Timestamp, TargetName, Filter, ExposureDuration,
    HFR, FWHM, Eccentricity, StarCount, GuidingRMSTotal, GuidingScale, Accepted,
    RaHours, DecDegrees, FocuserTemp, AmbientTemp,
    Gain, Offset, Binning, CameraTemp, CoolerSetpoint,
    FocuserPosition, RotatorPosition, Humidity, DewPoint, WindSpeed, Pressure,
    GradingStatus, RejectReason
) VALUES (
    @sid, @ts, @target, @filter, @exp,
    @hfr, @fwhm, @ecc, @stars, @rms, 1.32, @accepted,
    @ra, @dec, @focTemp, @ambTemp,
    100, 50, 1, -10.0, -10.0,
    @focPos, NULL, 45.0, 5.2, 8.3, 1015.0,
    @gradingStatus, @rejectReason
)
"@ @{
                "@sid"           = $sessionId
                "@ts"            = $ts
                "@target"        = $target.Name
                "@filter"        = $f.Name
                "@exp"           = $f.ExpSec
                "@hfr"           = $hfr
                "@fwhm"          = $fwhm
                "@ecc"           = $ecc
                "@stars"         = $stars
                "@rms"           = $rms
                "@accepted"      = $accepted
                "@ra"            = $target.RaHours
                "@dec"           = $target.DecDegrees
                "@focTemp"       = $focTemp
                "@ambTemp"       = $ambTemp
                "@focPos"        = $focPos
                "@gradingStatus" = $gradingStatus
                "@rejectReason"  = if ($rejectReason) { $rejectReason } else { [DBNull]::Value }
            }

            $elapsed += $f.ExpSec / 60.0 + 0.25   # exposure + ~15s overhead
            $totalImages++
        }

        $accepted = $f.Count - $rejected
        Write-Host "    $($f.Name): $($f.Count) frames ($accepted accepted, $rejected rejected)" -ForegroundColor Gray
    }
}

Write-Host "  Total images: $totalImages" -ForegroundColor Cyan

# â”€â”€ Seed timeline events â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

Exec "DELETE FROM SessionEvents WHERE SessionId = @s" @{ "@s" = $sessionId }

$totalMin = ($sessionEnd - $sessionStart).TotalMinutes

function EventAt($offsetMin, $type, $desc) {
    $ts = $sessionStart.AddMinutes($offsetMin).ToString("o")
    Exec "INSERT INTO SessionEvents (SessionId, Timestamp, EventType, Description) VALUES (@s, @ts, @type, @desc)" @{
        "@s" = $sessionId; "@ts" = $ts; "@type" = $type; "@desc" = $desc
    }
    Write-Host "  +$([int]$offsetMin)m  $type" -ForegroundColor DarkGray
}

Write-Host "Seeding timeline events..." -ForegroundColor White
EventAt   3  “RoofOpen”     “Safety monitor: Safe - roof opened”
EventAt   8  “AutoFocus”    “AutoFocus completed - Filter: L, Temp: 8.3C, Position: 38310”   # start of M31
EventAt 100  “MeridianFlip” “Meridian flip completed successfully”                            # M31 crosses meridian
EventAt 103  “AutoFocus”    “AutoFocus completed - Filter: L, Temp: 7.1C, Position: 38355”   # post-flip refocus
EventAt 320  “RoofClosed”   “Safety monitor: Unsafe - roof closed (wind gusts)”              # gap between targets
EventAt 340  “RoofOpen”     “Safety monitor: Safe - roof reopened”                           # gap between targets
EventAt 418  “AutoFocus”    “AutoFocus completed - Filter: Ha, Temp: 4.8C, Position: 38420”  # start of Rosette
EventAt 480  “AutoFocus”    “AutoFocus completed - Filter: OIII, Temp: 3.9C, Position: 38448” # Rosette filter switch

# â”€â”€ Seed historical sessions â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

Write-Host "Seeding historical sessions..." -ForegroundColor White

$historySessions = @(
    @{ WeeksAgo = 10; HfrMult = 1.18; FwhmMult = 1.22; RmsMult = 1.12; ImgCount = 55;  Label = "Poorer seeing" }
    @{ WeeksAgo =  6; HfrMult = 0.95; FwhmMult = 0.92; RmsMult = 0.98; ImgCount = 72;  Label = "Good night"    }
    @{ WeeksAgo =  4; HfrMult = 1.05; FwhmMult = 1.08; RmsMult = 1.03; ImgCount = 68;  Label = "Average night" }
    @{ WeeksAgo =  2; HfrMult = 0.90; FwhmMult = 0.88; RmsMult = 0.94; ImgCount = 85;  Label = "Best night"    }
)

foreach ($h in $historySessions) {
    $hSid   = [System.Guid]::NewGuid().ToString()
    $hStart = $sessionStart.AddDays(-($h.WeeksAgo * 7))
    $hEnd   = $hStart.AddHours(6)

    Exec "INSERT INTO Sessions (SessionId, SessionStart, SessionEnd, ProfileName, Notes, ReportSent, CamXSize, CamYSize, PixelSizeMicrons, FocalLengthMm)
          VALUES (@sid, @start, @end, @prof, @notes, 1, @cx, @cy, @px, @fl)" @{
        "@sid"   = $hSid
        "@start" = $hStart.ToString("o")
        "@end"   = $hEnd.ToString("o")
        "@prof"  = "Demo-History"
        "@notes" = $h.Label
        "@cx"    = $camX; "@cy" = $camY; "@px" = $pixelSz; "@fl" = $focalLen
    }

    $totalCombos = $targets | ForEach-Object { $_.Filters.Count } | Measure-Object -Sum | Select-Object -ExpandProperty Sum
    $perCombo    = [math]::Max(3, [math]::Floor($h.ImgCount / $totalCombos))
    $hElapsed    = 0.0

    foreach ($target in $targets) {
        foreach ($f in $target.Filters) {
            for ($i = 0; $i -lt $perCombo; $i++) {
                $hts   = $hStart.AddMinutes($hElapsed).ToString("o")
                $hfr   = [math]::Round($f.BaseHFR  * $h.HfrMult  + (Rnd -0.10 0.10), 2)
                $fwhm  = [math]::Round($f.BaseFWHM * $h.FwhmMult + (Rnd -0.12 0.12), 2)
                $ecc   = [math]::Round($f.BaseEcc                + (Rnd -0.03 0.03), 3)
                $stars = RndInt ($f.BaseStars - 50) ($f.BaseStars + 50)
                $rms   = [math]::Round(0.48 * $h.RmsMult         + (Rnd -0.06 0.06), 3)

                Exec @"
INSERT INTO Images (
    SessionId, Timestamp, TargetName, Filter, ExposureDuration,
    HFR, FWHM, Eccentricity, StarCount, GuidingRMSTotal, GuidingScale, Accepted,
    RaHours, DecDegrees, Gain, Offset, Binning, CameraTemp, CoolerSetpoint,
    GradingStatus
) VALUES (
    @sid, @ts, @target, @filter, @exp,
    @hfr, @fwhm, @ecc, @stars, @rms, 1.32, 1,
    @ra, @dec, 100, 50, 1, -10.0, -10.0,
    1
)
"@ @{
                    "@sid"    = $hSid; "@ts" = $hts; "@target" = $target.Name
                    "@filter" = $f.Name; "@exp" = $f.ExpSec
                    "@hfr"    = $hfr; "@fwhm" = $fwhm; "@ecc" = $ecc
                    "@stars"  = $stars; "@rms" = $rms
                    "@ra"     = $target.RaHours; "@dec" = $target.DecDegrees
                }
                $hElapsed += $f.ExpSec / 60.0 + 0.25
            }
        }
    }

    $dateStr = $hStart.ToString("yyyy-MM-dd")
    Write-Host “  $dateStr ($($h.WeeksAgo)w ago): $($perCombo * $totalCombos) images - $($h.Label)” -ForegroundColor DarkCyan
}

$conn.Close()

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Main session : $totalImages images across 2 targets (M31 + Rosette Nebula)" -ForegroundColor Green
Write-Host "  Historical   : $($historySessions.Count) past sessions seeded" -ForegroundColor Green
Write-Host "  DSS thumbnails will render using real coordinates (no TS required)" -ForegroundColor Green
Write-Host ""
Write-Host "Click 'Send Test Report' in the Night Summary plugin options to preview." -ForegroundColor White
