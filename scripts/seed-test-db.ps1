# Night Summary - Seed Test Database
# Creates a fully self-contained demo session with realistic synthetic data.
# Run this to populate the test database used by "Send Test Report" in NINA.
#
# Targets:
#   M31 - Andromeda Galaxy        (broadband: L R G B)
#   IC 1805 - Heart Nebula        (narrowband: Ha OIII)
#   Rosette Nebula - NGC 2244     (narrowband: Ha OIII SII)
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

# Observer location: Sacramento Mountains, NM (matches SessionService fallback)
$obsLat = 32.9
$obsLon = -105.5

# Altitude/azimuth calculation for a given RA/Dec at a given local time
function Get-AltAz($raHours, $decDeg, $localDateTime) {
    $pi  = [math]::PI
    $rad = $pi / 180.0
    # Assume MDT (UTC-6) for October in NM
    $utc = $localDateTime.AddHours(6)
    $jd  = 2451545.0 + ($utc - [DateTime]::new(2000, 1, 1, 12, 0, 0)).TotalDays
    $T   = ($jd - 2451545.0) / 36525.0
    $gmst = 280.46061837 + 360.98564736629 * ($jd - 2451545.0) + 0.000387933 * $T * $T
    $gmst = (($gmst % 360) + 360) % 360
    $lst  = ($gmst / 15.0 + $obsLon / 15.0) % 24
    if ($lst -lt 0) { $lst += 24 }
    $ha   = ($lst - $raHours) * 15.0 * $rad
    $latR = $obsLat * $rad
    $decR = $decDeg * $rad
    $sinAlt = [math]::Sin($latR) * [math]::Sin($decR) + [math]::Cos($latR) * [math]::Cos($decR) * [math]::Cos($ha)
    $alt = [math]::Asin([math]::Max(-1.0, [math]::Min(1.0, $sinAlt))) / $rad
    $cosAlt = [math]::Cos($alt * $rad)
    $cosAz = if ($cosAlt -gt 0.001) { ([math]::Sin($decR) - [math]::Sin($latR) * $sinAlt) / ([math]::Cos($latR) * $cosAlt) } else { 0.0 }
    $cosAz = [math]::Max(-1.0, [math]::Min(1.0, $cosAz))
    $az = [math]::Acos($cosAz) / $rad
    if ([math]::Sin($ha) -gt 0) { $az = 360 - $az }
    $pier = if ($ha -gt 0) { "West" } else { "East" }
    return @{
        Alt  = [math]::Round($alt, 2)
        Az   = [math]::Round($az, 2)
        Pier = $pier
    }
}

# ── Schema ───────────────────────────────────────────────────────────────────

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
    PositionAngle REAL,
    Humidity REAL,
    DewPoint REAL,
    WindSpeed REAL,
    Pressure REAL,
    SkyBrightness REAL,
    SkyTemperature REAL,
    WindDirection REAL,
    WindGust REAL,
    GradingStatus INTEGER DEFAULT -1,
    RejectReason TEXT,
    ImageType TEXT,
    Altitude REAL,
    Azimuth REAL,
    Airmass REAL,
    SideOfPier TEXT,
    ReadoutMode TEXT,
    SkyQuality REAL,
    CloudCover REAL,
    SeeingFWHM REAL,
    StatMedian REAL,
    StatMean REAL,
    StatStDev REAL,
    StatMAD REAL,
    StatMin INTEGER,
    StatMax INTEGER,
    StatBitDepth INTEGER
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

Exec @"
CREATE TABLE IF NOT EXISTS SessionTimingEvents (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId TEXT NOT NULL,
    EventType TEXT NOT NULL,
    StartTime TEXT,
    EndTime TEXT,
    DurationSeconds REAL,
    Details TEXT
)
"@

# Migrate older DBs that predate newer columns
foreach ($col in @(
    @{ Table = "Sessions"; Def = "CamXSize INTEGER DEFAULT 0" }
    @{ Table = "Sessions"; Def = "CamYSize INTEGER DEFAULT 0" }
    @{ Table = "Sessions"; Def = "PixelSizeMicrons REAL DEFAULT 0" }
    @{ Table = "Sessions"; Def = "FocalLengthMm REAL DEFAULT 0" }
    @{ Table = "Sessions"; Def = "SkippedExposures INTEGER DEFAULT 0" }
    @{ Table = "Sessions"; Def = "CameraName TEXT" }
    @{ Table = "Sessions"; Def = "TelescopeName TEXT" }
    @{ Table = "Sessions"; Def = "MountName TEXT" }
    @{ Table = "Sessions"; Def = "FilterWheelName TEXT" }
    @{ Table = "Sessions"; Def = "FocuserName TEXT" }
    @{ Table = "Sessions"; Def = "RotatorName TEXT" }
    @{ Table = "Sessions"; Def = "GuiderName TEXT" }
    @{ Table = "Sessions"; Def = "DomeName TEXT" }
    @{ Table = "Sessions"; Def = "FlatDeviceName TEXT" }
    @{ Table = "Sessions"; Def = "SafetyMonitorName TEXT" }
    @{ Table = "Sessions"; Def = "WeatherName TEXT" }
    @{ Table = "Sessions"; Def = "SwitchName TEXT" }
    @{ Table = "Images";   Def = "FWHM REAL DEFAULT 0" }
    @{ Table = "Images";   Def = "Eccentricity REAL DEFAULT 0" }
    @{ Table = "Images";   Def = "RaHours REAL DEFAULT 0" }
    @{ Table = "Images";   Def = "DecDegrees REAL DEFAULT 0" }
    @{ Table = "Images";   Def = "FocuserTemp REAL" }
    @{ Table = "Images";   Def = "AmbientTemp REAL" }
    @{ Table = "Images";   Def = "Gain INTEGER DEFAULT -1" }
    @{ Table = "Images";   Def = "Offset INTEGER DEFAULT -1" }
    @{ Table = "Images";   Def = "Binning INTEGER DEFAULT 0" }
    @{ Table = "Images";   Def = "CameraTemp REAL" }
    @{ Table = "Images";   Def = "CoolerSetpoint REAL" }
    @{ Table = "Images";   Def = "FocuserPosition INTEGER" }
    @{ Table = "Images";   Def = "RotatorPosition REAL" }
    @{ Table = "Images";   Def = "PositionAngle REAL" }
    @{ Table = "Images";   Def = "Humidity REAL" }
    @{ Table = "Images";   Def = "DewPoint REAL" }
    @{ Table = "Images";   Def = "WindSpeed REAL" }
    @{ Table = "Images";   Def = "Pressure REAL" }
    @{ Table = "Images";   Def = "GradingStatus INTEGER DEFAULT -1" }
    @{ Table = "Images";   Def = "RejectReason TEXT" }
    @{ Table = "Images";   Def = "ImageType TEXT" }
    @{ Table = "Images";   Def = "Altitude REAL" }
    @{ Table = "Images";   Def = "Azimuth REAL" }
    @{ Table = "Images";   Def = "Airmass REAL" }
    @{ Table = "Images";   Def = "SideOfPier TEXT" }
    @{ Table = "Images";   Def = "ReadoutMode TEXT" }
    @{ Table = "Images";   Def = "SkyQuality REAL" }
    @{ Table = "Images";   Def = "CloudCover REAL" }
    @{ Table = "Images";   Def = "SeeingFWHM REAL" }
    @{ Table = "Images";   Def = "StatMedian REAL" }
    @{ Table = "Images";   Def = "StatMean REAL" }
    @{ Table = "Images";   Def = "StatStDev REAL" }
    @{ Table = "Images";   Def = "StatMAD REAL" }
    @{ Table = "Images";   Def = "StatMin INTEGER" }
    @{ Table = "Images";   Def = "StatMax INTEGER" }
    @{ Table = "Images";   Def = "StatBitDepth INTEGER" }
    @{ Table = "SessionEvents"; Def = "AfSucceeded INTEGER" }
    @{ Table = "SessionEvents"; Def = "AfHfr REAL" }
)) {
    try { Exec "ALTER TABLE $($col.Table) ADD COLUMN $($col.Def)" } catch { }
}

# Wipe previous demo data ────────────────────────────────────────────────────

$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT SessionId FROM Sessions WHERE ProfileName IN ('Night Summary Demo', 'Demo-History')"
$reader = $cmd.ExecuteReader()
$oldIds = @()
while ($reader.Read()) { $oldIds += $reader["SessionId"].ToString() }
$reader.Close()

foreach ($id in $oldIds) {
    Exec "DELETE FROM Images              WHERE SessionId = @s" @{ "@s" = $id }
    Exec "DELETE FROM SessionEvents       WHERE SessionId = @s" @{ "@s" = $id }
    Exec "DELETE FROM SessionTimingEvents WHERE SessionId = @s" @{ "@s" = $id }
    Exec "DELETE FROM Sessions            WHERE SessionId = @s" @{ "@s" = $id }
}
if ($oldIds.Count -gt 0) { Write-Host "Cleared $($oldIds.Count) previous demo session(s)." -ForegroundColor DarkGray }

# ── Session definition ─────────────────────────────────────────────────────

$sessionId    = [System.Guid]::NewGuid().ToString()
$sessionStart = [DateTime]::new(2025, 10, 15, 21, 0, 0)   # Oct 15 2025, 9:00 PM
$sessionEnd   = [DateTime]::new(2025, 10, 16,  5, 30, 0)  # Oct 16 2025, 5:30 AM (8.5h session)

# Camera / scope profile (realistic mid-range mono rig)
# ASI2600MM: 6248x4176, 3.76um pixel, 700mm focal length
$camX     = 6248
$camY     = 4176
$pixelSz  = 3.76
$focalLen = 700.0

Exec "INSERT INTO Sessions (SessionId, SessionStart, SessionEnd, ProfileName, Notes, ReportSent,
      CamXSize, CamYSize, PixelSizeMicrons, FocalLengthMm, SkippedExposures,
      CameraName, TelescopeName, MountName, FilterWheelName, FocuserName, RotatorName, GuiderName,
      SafetyMonitorName, WeatherName)
      VALUES (@sid, @start, @end, @prof, @notes, 0,
      @cx, @cy, @px, @fl, @skipped,
      @cam, @scope, @mount, @fw, @foc, @rot, @guide,
      @safety, @weather)" @{
    "@sid"     = $sessionId
    "@start"   = $sessionStart.ToString("o")
    "@end"     = $sessionEnd.ToString("o")
    "@prof"    = "Night Summary Demo"
    "@notes"   = "Demo session - generated by seed-test-db.ps1"
    "@cx"      = $camX
    "@cy"      = $camY
    "@px"      = $pixelSz
    "@fl"      = $focalLen
    "@skipped" = 3
    "@cam"     = "ZWO ASI2600MM Pro"
    "@scope"   = "Sky-Watcher Esprit 100ED"
    "@mount"   = "Sky-Watcher EQ6-R Pro"
    "@fw"      = "ZWO EFW 7x36mm"
    "@foc"     = "ZWO EAF"
    "@rot"     = $null
    "@guide"   = "PHD2"
    "@safety"  = "Spike-a-Roof"
    "@weather" = "OpenWeatherMap"
}

Write-Host "Session: $sessionId" -ForegroundColor Cyan
Write-Host "  $($sessionStart.ToString('yyyy-MM-dd HH:mm')) -> $($sessionEnd.ToString('HH:mm'))" -ForegroundColor Cyan

# ── Target definitions ─────────────────────────────────────────────────────

$targets = @(
    @{
        Name          = "M31 - Andromeda Galaxy"
        RaHours       = 0.7123    # 00h 42m 44s
        DecDegrees    = 41.269    # +41 16'
        PositionAngle = 35.0
        Filters       = @(
            @{ Name = "L";  ExpSec = 120; Count = 30; BaseHFR = 1.75; BaseFWHM = 2.00; BaseEcc = 0.37; BaseStars = 420 }
            @{ Name = "R";  ExpSec = 120; Count = 20; BaseHFR = 1.82; BaseFWHM = 2.08; BaseEcc = 0.39; BaseStars = 395 }
            @{ Name = "G";  ExpSec = 120; Count = 20; BaseHFR = 1.79; BaseFWHM = 2.05; BaseEcc = 0.38; BaseStars = 405 }
            @{ Name = "B";  ExpSec = 120; Count = 20; BaseHFR = 1.88; BaseFWHM = 2.15; BaseEcc = 0.41; BaseStars = 380 }
        )
        StartOffset   = 10        # minutes from session start (9:10 PM)
        Gain          = 100
        BaseMedian    = 950       # narrowband is higher, broadband lower
    }
    @{
        Name          = "IC 1805 - Heart Nebula"
        RaHours       = 2.5467    # 02h 32m 48s
        DecDegrees    = 61.45     # +61 27'
        PositionAngle = 270.0
        Filters       = @(
            @{ Name = "Ha";   ExpSec = 300; Count = 12; BaseHFR = 2.00; BaseFWHM = 2.25; BaseEcc = 0.42; BaseStars = 310 }
            @{ Name = "OIII"; ExpSec = 300; Count = 8;  BaseHFR = 2.08; BaseFWHM = 2.35; BaseEcc = 0.44; BaseStars = 285 }
        )
        StartOffset   = 225       # 12:45 AM (after M31 + slew + AF)
        Gain          = 100
        BaseMedian    = 1200
    }
    @{
        Name          = "Rosette Nebula - NGC 2244"
        RaHours       = 6.5625    # 06h 33m 45s
        DecDegrees    = 4.998     # +04 59'
        PositionAngle = 0.0
        Filters       = @(
            @{ Name = "Ha";   ExpSec = 300; Count = 10; BaseHFR = 2.05; BaseFWHM = 2.30; BaseEcc = 0.43; BaseStars = 290 }
            @{ Name = "OIII"; ExpSec = 300; Count = 6;  BaseHFR = 2.12; BaseFWHM = 2.40; BaseEcc = 0.45; BaseStars = 270 }
            @{ Name = "SII";  ExpSec = 300; Count = 4;  BaseHFR = 2.18; BaseFWHM = 2.48; BaseEcc = 0.46; BaseStars = 255 }
        )
        StartOffset   = 370       # 3:10 AM (after Heart + slew + AF)
        Gain          = 100
        BaseMedian    = 1100
    }
)

# Safety monitor closure windows: [startOffset, endOffset] in minutes
$closedWindows = @( @(295, 315) )  # roof closes ~1:55 AM to 2:15 AM during Heart Nebula (wind gusts)

function IsUnsafe($offsetMin) {
    foreach ($w in $closedWindows) { if ($offsetMin -ge $w[0] -and $offsetMin -lt $w[1]) { return $true } }
    return $false
}

# ── Seed images ──────────────────────────────────────────────────────────────

# Tracking for timing events - we'll accumulate them as we generate images
$timingEvents = [System.Collections.ArrayList]::new()

$totalImages = 0
$elapsed     = 0.0   # running clock in minutes from session start
$frameInSession = 0  # global frame counter for weather drift

# Slowly-drifting ambient conditions (simulate a cooling night)
$baseSkyQuality = 21.0    # mag/arcsec^2 - good dark site
$baseCloudCover = 5.0     # percent
$basePressure   = 1015.0  # hPa
$baseHumidity   = 42.0    # percent

foreach ($target in $targets) {
    $elapsed = $target.StartOffset
    Write-Host "  Target: $($target.Name)" -ForegroundColor White

    foreach ($f in $target.Filters) {
        $rejected = 0

        for ($i = 0; $i -lt $f.Count; $i++) {
            # Skip frames during roof-closed windows; advance clock through closure
            while (IsUnsafe $elapsed) { $elapsed += 0.5 }

            $captureTime = $sessionStart.AddMinutes($elapsed)
            $ts = $captureTime.ToString("o")

            # Calculate altitude/azimuth at capture time
            $altaz = Get-AltAz $target.RaHours $target.DecDegrees $captureTime
            $altitude = $altaz.Alt
            $azimuth  = $altaz.Az
            $pier     = $altaz.Pier
            $airmass  = if ($altitude -gt 5) { [math]::Round(1.0 / [math]::Sin($altitude * [math]::PI / 180.0), 3) } else { $null }

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

            # Slowly drifting environmental data
            $hourFrac   = $elapsed / 60.0
            $skyQ       = [math]::Round($baseSkyQuality + $hourFrac * 0.05 + (Rnd -0.1 0.1), 2)
            $cloud      = [math]::Round([math]::Max(0, $baseCloudCover + (Rnd -3.0 3.0)), 1)
            $seeingFwhm = [math]::Round($f.BaseFWHM * 0.95 + (Rnd -0.2 0.2), 2)
            $humidity   = [math]::Round($baseHumidity + $hourFrac * 1.5 + (Rnd -2.0 2.0), 1)
            $dewPt      = [math]::Round($ambTemp - 12.0 + $hourFrac * 0.3 + (Rnd -0.5 0.5), 1)
            $windSpd    = [math]::Round(3.0 + (Rnd -1.5 1.5), 1)
            $pressure   = [math]::Round($basePressure + (Rnd -0.5 0.5), 1)
            $skyBright  = [math]::Round(0.02 + $hourFrac * 0.005 + (Rnd -0.005 0.005), 4)  # Lux, dark site
            $skyTemp    = [math]::Round(-25.0 + $hourFrac * 0.3 + (Rnd -1.0 1.0), 1)       # IR sky temp C
            $windDir    = [math]::Round(220 + $hourFrac * 2.0 + (Rnd -10 10), 0)            # degrees
            $windGust   = [math]::Round($windSpd * 1.5 + (Rnd -0.5 1.0), 1)                # m/s

            # Image statistics (16-bit mono)
            $statMedian   = [math]::Round($target.BaseMedian + (Rnd -80 80), 1)
            $statMean     = [math]::Round($statMedian * 1.02 + (Rnd -20 20), 1)
            $statStDev    = [math]::Round(85 + (Rnd -15 15), 1)
            $statMAD      = [math]::Round(45 + (Rnd -10 10), 1)
            $statMin      = RndInt 0 120
            $statMax      = RndInt 55000 65535
            $statBitDepth = 16

            # Reject ~10% of frames — mix of TS grading (~5%) and manual thumbs-down (~5%)
            # so the report reliably exercises both RejectReason paths and the tooltip
            # layout. Higher than a real session's rate, but the demo DB is small so bumping
            # the rate guarantees non-zero counts in every bucket for visual QA.
            $accepted      = 1
            $gradingStatus = 1
            $rejectReason  = $null
            $roll = RndInt 1 100
            if ($roll -le 5) {
                # TS-graded rejection
                $accepted      = 0
                $gradingStatus = 2
                $rejectReason  = @("HFR too high", "Star count below threshold", "Guiding RMS exceeded limit") | Get-Random
                # Make the bad frame look bad so quality metrics justify the reject
                $hfr   = [math]::Round($hfr   * 1.6 + (Rnd 0.1 0.4), 2)
                $fwhm  = [math]::Round($fwhm  * 1.5 + (Rnd 0.1 0.3), 2)
                $stars = RndInt 60 120
                $rejected++
            } elseif ($roll -le 10) {
                # Manual rejection (user thumbs-down in NINA's image history panel).
                # Quality metrics are unchanged — subjective user call, not auto-detected.
                $accepted      = 0
                $gradingStatus = -1
                $rejectReason  = "Manual"
                $rejected++
            }

            Exec @"
INSERT INTO Images (
    SessionId, Timestamp, TargetName, Filter, ExposureDuration,
    HFR, FWHM, Eccentricity, StarCount, GuidingRMSTotal, GuidingScale, Accepted,
    RaHours, DecDegrees, FocuserTemp, AmbientTemp,
    Gain, Offset, Binning, CameraTemp, CoolerSetpoint,
    FocuserPosition, RotatorPosition, PositionAngle,
    Humidity, DewPoint, WindSpeed, Pressure,
    SkyBrightness, SkyTemperature, WindDirection, WindGust,
    GradingStatus, RejectReason,
    ImageType, Altitude, Azimuth, Airmass, SideOfPier, ReadoutMode,
    SkyQuality, CloudCover, SeeingFWHM,
    StatMedian, StatMean, StatStDev, StatMAD, StatMin, StatMax, StatBitDepth
) VALUES (
    @sid, @ts, @target, @filter, @exp,
    @hfr, @fwhm, @ecc, @stars, @rms, 1.32, @accepted,
    @ra, @dec, @focTemp, @ambTemp,
    @gain, 50, 1, -10.0, -10.0,
    @focPos, NULL, @pa,
    @humidity, @dewPt, @wind, @pressure,
    @skyBright, @skyTemp, @windDir, @windGust,
    @gradingStatus, @rejectReason,
    'LIGHT', @alt, @az, @airmass, @pier, 'Mode 0 (High Gain)',
    @skyQ, @cloud, @seeingFwhm,
    @statMedian, @statMean, @statStDev, @statMAD, @statMin, @statMax, @statBitDepth
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
                "@pa"            = $target.PositionAngle
                "@focTemp"       = $focTemp
                "@ambTemp"       = $ambTemp
                "@focPos"        = $focPos
                "@gain"          = $target.Gain
                "@humidity"      = $humidity
                "@dewPt"         = $dewPt
                "@wind"          = $windSpd
                "@pressure"      = $pressure
                "@skyBright"     = $skyBright
                "@skyTemp"       = $skyTemp
                "@windDir"       = $windDir
                "@windGust"      = $windGust
                "@gradingStatus" = $gradingStatus
                "@rejectReason"  = if ($rejectReason) { $rejectReason } else { [DBNull]::Value }
                "@alt"           = $altitude
                "@az"            = $azimuth
                "@airmass"       = if ($airmass) { $airmass } else { [DBNull]::Value }
                "@pier"          = $pier
                "@skyQ"          = $skyQ
                "@cloud"         = $cloud
                "@seeingFwhm"    = $seeingFwhm
                "@statMedian"    = $statMedian
                "@statMean"      = $statMean
                "@statStDev"     = $statStDev
                "@statMAD"       = $statMAD
                "@statMin"       = $statMin
                "@statMax"       = $statMax
                "@statBitDepth"  = $statBitDepth
            }

            # ── Generate timing events for this exposure ──
            $expStart = $captureTime
            $expEnd   = $expStart.AddSeconds($f.ExpSec)

            # Exposure event (excluded from overhead analysis but needed for completeness)
            $timingEvents.Add(@{ Type = "Exposure"; Start = $expStart; End = $expEnd; Dur = $f.ExpSec; Details = "$($f.Name) $($f.ExpSec)s" }) | Out-Null

            # Camera download (~2-4s after exposure)
            $dlDur   = Rnd 1.8 4.2
            $dlStart = $expEnd
            $dlEnd   = $dlStart.AddSeconds($dlDur)
            $timingEvents.Add(@{ Type = "CameraDownload"; Start = $dlStart; End = $dlEnd; Dur = $dlDur; Details = $null }) | Out-Null

            # Image save (~0.8-2.5s, overlaps with next exposure start in real life)
            $saveDur   = Rnd 0.8 2.5
            $saveStart = $dlEnd
            $saveEnd   = $saveStart.AddSeconds($saveDur)
            $timingEvents.Add(@{ Type = "ImageSave"; Start = $saveStart; End = $saveEnd; Dur = $saveDur; Details = $null }) | Out-Null

            # Dither every 3 frames (~3-8s)
            if ($i -gt 0 -and $i % 3 -eq 0) {
                $dithDur   = Rnd 3.0 8.0
                $dithStart = $saveEnd
                $dithEnd   = $dithStart.AddSeconds($dithDur)
                $timingEvents.Add(@{ Type = "Dither"; Start = $dithStart; End = $dithEnd; Dur = $dithDur; Details = $null }) | Out-Null
            }

            $elapsed += $f.ExpSec / 60.0 + 0.25   # exposure + ~15s overhead
            $totalImages++
            $frameInSession++
        }

        $accepted = $f.Count - $rejected
        Write-Host "    $($f.Name): $($f.Count) frames ($accepted accepted, $rejected rejected)" -ForegroundColor Gray
    }
}

Write-Host "  Total images: $totalImages" -ForegroundColor Cyan

# ── Seed timeline events ───────────────────────────────────────────────────

Exec "DELETE FROM SessionEvents WHERE SessionId = @s" @{ "@s" = $sessionId }

function EventAt($offsetMin, $type, $desc, $afSucceeded = $null, $afHfr = $null) {
    $ts = $sessionStart.AddMinutes($offsetMin).ToString("o")
    $params = @{
        "@s" = $sessionId; "@ts" = $ts; "@type" = $type; "@desc" = $desc
        "@afOk" = if ($null -ne $afSucceeded) { $afSucceeded } else { [DBNull]::Value }
        "@afHfr" = if ($null -ne $afHfr) { $afHfr } else { [DBNull]::Value }
    }
    Exec "INSERT INTO SessionEvents (SessionId, Timestamp, EventType, Description, AfSucceeded, AfHfr)
          VALUES (@s, @ts, @type, @desc, @afOk, @afHfr)" $params
    Write-Host "  +$([int]$offsetMin)m  $type" -ForegroundColor DarkGray
}

Write-Host "Seeding timeline events..." -ForegroundColor White
EventAt   3  "RoofOpen"     "Safety monitor: Safe - roof opened"
EventAt   8  "AutoFocus"    "AutoFocus completed - Filter: L, Temp: 8.3C, Position: 38310"  1  1.72    # start of M31
EventAt 100  "MeridianFlip" "Meridian flip completed successfully"                                      # M31 crosses meridian
EventAt 103  "AutoFocus"    "AutoFocus completed - Filter: L, Temp: 7.1C, Position: 38355"  1  1.68    # post-flip refocus
EventAt 222  "AutoFocus"    "AutoFocus completed - Filter: Ha, Temp: 5.8C, Position: 38400" 1  1.95    # start of Heart Nebula
EventAt 295  "RoofClosed"   "Safety monitor: Unsafe - roof closed (wind gusts)"                         # during Heart Nebula
EventAt 315  "RoofOpen"     "Safety monitor: Safe - roof reopened"
EventAt 318  "AutoFocus"    "AutoFocus completed - Filter: Ha, Temp: 4.5C, Position: 38430" 1  2.02    # post-roof refocus
EventAt 368  "AutoFocus"    "AutoFocus completed - Filter: Ha, Temp: 3.9C, Position: 38445" 1  2.08    # start of Rosette
EventAt 430  "AutoFocus"    "AutoFocus completed - Filter: OIII, Temp: 3.2C, Position: 38460" 0 3.85   # failed AF during Rosette (recovered)
EventAt 432  "AutoFocus"    "AutoFocus completed - Filter: OIII, Temp: 3.2C, Position: 38458" 1 2.15   # retry succeeded

# ── Seed overhead timing events ────────────────────────────────────────────

Write-Host "Seeding timing events for overhead analysis..." -ForegroundColor White
Exec "DELETE FROM SessionTimingEvents WHERE SessionId = @s" @{ "@s" = $sessionId }

# Add target slew + centering events between targets
function AddSlew($offsetMin, $dur, $details) {
    $s = $sessionStart.AddMinutes($offsetMin)
    $e = $s.AddSeconds($dur)
    $timingEvents.Add(@{ Type = "Slew"; Start = $s; End = $e; Dur = $dur; Details = $details }) | Out-Null
}

function AddCentering($offsetMin, $dur) {
    $s = $sessionStart.AddMinutes($offsetMin)
    $e = $s.AddSeconds($dur)
    $timingEvents.Add(@{ Type = "Centering"; Start = $s; End = $e; Dur = $dur; Details = "Plate solve + center" }) | Out-Null
}

function AddPlateSolve($offsetMin, $dur) {
    $s = $sessionStart.AddMinutes($offsetMin)
    $e = $s.AddSeconds($dur)
    $timingEvents.Add(@{ Type = "PlateSolve"; Start = $s; End = $e; Dur = $dur; Details = $null }) | Out-Null
}

function AddAutofocus($offsetMin, $dur) {
    $s = $sessionStart.AddMinutes($offsetMin)
    $e = $s.AddSeconds($dur)
    $timingEvents.Add(@{ Type = "Autofocus"; Start = $s; End = $e; Dur = $dur; Details = $null }) | Out-Null
}

function AddMeridianFlip($offsetMin, $dur) {
    $s = $sessionStart.AddMinutes($offsetMin)
    $e = $s.AddSeconds($dur)
    $timingEvents.Add(@{ Type = "MeridianFlip"; Start = $s; End = $e; Dur = $dur; Details = $null }) | Out-Null
}

function AddFilterChange($offsetMin, $dur) {
    $s = $sessionStart.AddMinutes($offsetMin)
    $e = $s.AddSeconds($dur)
    $timingEvents.Add(@{ Type = "FilterChange"; Start = $s; End = $e; Dur = $dur; Details = $null }) | Out-Null
}

# Session start: slew to M31
AddSlew        5   18  "Slew to M31 - Andromeda Galaxy"
AddCentering   5.5 25
AddPlateSolve  5.3  4
AddAutofocus   8   45

# Filter changes within M31 (L->R, R->G, G->B)
# L ends ~9:10 + 30*2.25 = ~76.5 min offset, so ~86m from start
AddFilterChange  86   6
AddFilterChange 131   6    # R->G at ~131m
AddFilterChange 176   6    # G->B at ~176m

# Meridian flip
AddMeridianFlip 100  65   # total flip time including slew + settle
AddCentering    101  30
AddPlateSolve   101   5
AddAutofocus    103  50

# Slew to Heart Nebula
AddSlew        220  22  "Slew to IC 1805 - Heart Nebula"
AddCentering   220.5 28
AddPlateSolve  220.3  5
AddAutofocus   222  48
AddFilterChange 222   6   # L->Ha filter change

# Filter change within Heart (Ha->OIII)
AddFilterChange 290   6

# Post-roof recovery
AddAutofocus   318  52
AddCentering   316  30
AddPlateSolve  316   5

# Slew to Rosette
AddSlew        365  20  "Slew to Rosette Nebula - NGC 2244"
AddCentering   365.5 26
AddPlateSolve  365.3  4
AddAutofocus   368  50

# Filter changes within Rosette (Ha->OIII, OIII->SII)
AddFilterChange 420   6   # Ha->OIII
AddFilterChange 455   6   # OIII->SII

# Failed AF + retry
AddAutofocus   430  38   # failed - timed out
AddAutofocus   432  45   # retry succeeded

# Write all timing events to DB
foreach ($te in $timingEvents) {
    Exec @"
INSERT INTO SessionTimingEvents (SessionId, EventType, StartTime, EndTime, DurationSeconds, Details)
VALUES (@sid, @type, @start, @end, @dur, @details)
"@ @{
        "@sid"     = $sessionId
        "@type"    = $te.Type
        "@start"   = $te.Start.ToString("o")
        "@end"     = $te.End.ToString("o")
        "@dur"     = $te.Dur
        "@details" = if ($te.Details) { $te.Details } else { [DBNull]::Value }
    }
}
Write-Host "  $($timingEvents.Count) timing events seeded" -ForegroundColor DarkGray

# ── Seed historical sessions ─────────────────────────────────────────────

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

    Exec "INSERT INTO Sessions (SessionId, SessionStart, SessionEnd, ProfileName, Notes, ReportSent,
          CamXSize, CamYSize, PixelSizeMicrons, FocalLengthMm,
          CameraName, TelescopeName, MountName, FilterWheelName, FocuserName, GuiderName)
          VALUES (@sid, @start, @end, @prof, @notes, 1,
          @cx, @cy, @px, @fl,
          @cam, @scope, @mount, @fw, @foc, @guide)" @{
        "@sid"   = $hSid
        "@start" = $hStart.ToString("o")
        "@end"   = $hEnd.ToString("o")
        "@prof"  = "Night Summary Demo"
        "@notes" = $h.Label
        "@cx"    = $camX; "@cy" = $camY; "@px" = $pixelSz; "@fl" = $focalLen
        "@cam"   = "ZWO ASI2600MM Pro"; "@scope" = "Sky-Watcher Esprit 100ED"
        "@mount" = "Sky-Watcher EQ6-R Pro"; "@fw" = "ZWO EFW 7x36mm"
        "@foc"   = "ZWO EAF"; "@guide" = "PHD2"
    }

    $totalCombos = $targets | ForEach-Object { $_.Filters.Count } | Measure-Object -Sum | Select-Object -ExpandProperty Sum
    $perCombo    = [math]::Max(3, [math]::Floor($h.ImgCount / $totalCombos))
    $hElapsed    = 0.0

    foreach ($target in $targets) {
        foreach ($f in $target.Filters) {
            for ($i = 0; $i -lt $perCombo; $i++) {
                $hCaptureTime = $hStart.AddMinutes($hElapsed)
                $hts   = $hCaptureTime.ToString("o")
                $hfr   = [math]::Round($f.BaseHFR  * $h.HfrMult  + (Rnd -0.10 0.10), 2)
                $fwhm  = [math]::Round($f.BaseFWHM * $h.FwhmMult + (Rnd -0.12 0.12), 2)
                $ecc   = [math]::Round($f.BaseEcc                + (Rnd -0.03 0.03), 3)
                $stars = RndInt ($f.BaseStars - 50) ($f.BaseStars + 50)
                $rms   = [math]::Round(0.48 * $h.RmsMult         + (Rnd -0.06 0.06), 3)

                # Compute altitude for history images too
                $hAltaz = Get-AltAz $target.RaHours $target.DecDegrees $hCaptureTime
                $hAirmass = if ($hAltaz.Alt -gt 5) { [math]::Round(1.0 / [math]::Sin($hAltaz.Alt * [math]::PI / 180.0), 3) } else { $null }

                Exec @"
INSERT INTO Images (
    SessionId, Timestamp, TargetName, Filter, ExposureDuration,
    HFR, FWHM, Eccentricity, StarCount, GuidingRMSTotal, GuidingScale, Accepted,
    RaHours, DecDegrees, Gain, Offset, Binning, CameraTemp, CoolerSetpoint,
    Humidity, DewPoint, WindSpeed, Pressure,
    SkyBrightness, SkyTemperature, WindDirection, WindGust,
    GradingStatus, ImageType, Altitude, Azimuth, Airmass, SideOfPier, ReadoutMode,
    StatMedian, StatMean, StatStDev, StatMAD, StatMin, StatMax, StatBitDepth
) VALUES (
    @sid, @ts, @target, @filter, @exp,
    @hfr, @fwhm, @ecc, @stars, @rms, 1.32, 1,
    @ra, @dec, 100, 50, 1, -10.0, -10.0,
    @humidity, @dewPt, @wind, @pressure,
    @skyBright, @skyTemp, @windDir, @windGust,
    1, 'LIGHT', @alt, @az, @airmass, @pier, 'Mode 0 (High Gain)',
    @statMedian, @statMean, @statStDev, @statMAD, @statMin, @statMax, 16
)
"@ @{
                    "@sid"    = $hSid; "@ts" = $hts; "@target" = $target.Name
                    "@filter" = $f.Name; "@exp" = $f.ExpSec
                    "@hfr"    = $hfr; "@fwhm" = $fwhm; "@ecc" = $ecc
                    "@stars"  = $stars; "@rms" = $rms
                    "@ra"     = $target.RaHours; "@dec" = $target.DecDegrees
                    "@alt"    = $hAltaz.Alt; "@az" = $hAltaz.Az
                    "@airmass" = if ($hAirmass) { $hAirmass } else { [DBNull]::Value }
                    "@pier"   = $hAltaz.Pier
                    "@humidity" = [math]::Round(55 + (Rnd -5 5), 1)
                    "@dewPt"    = [math]::Round(3.0 + (Rnd -1 1), 1)
                    "@wind"     = [math]::Round(3.0 + (Rnd -1.5 1.5), 1)
                    "@pressure" = [math]::Round(1013 + (Rnd -2 2), 1)
                    "@skyBright"  = [math]::Round(0.02 + (Rnd -0.005 0.005), 4)
                    "@skyTemp"    = [math]::Round(-25.0 + (Rnd -2 2), 1)
                    "@windDir"    = [math]::Round(220 + (Rnd -15 15), 0)
                    "@windGust"   = [math]::Round(4.5 + (Rnd -1 1), 1)
                    "@statMedian" = [math]::Round($target.BaseMedian + (Rnd -80 80), 1)
                    "@statMean"   = [math]::Round($target.BaseMedian * 1.02 + (Rnd -20 20), 1)
                    "@statStDev"  = [math]::Round(85 + (Rnd -15 15), 1)
                    "@statMAD"    = [math]::Round(45 + (Rnd -10 10), 1)
                    "@statMin"    = RndInt 0 120
                    "@statMax"    = RndInt 55000 65535
                }
                $hElapsed += $f.ExpSec / 60.0 + 0.25
            }
        }
    }

    $dateStr = $hStart.ToString("yyyy-MM-dd")
    Write-Host "  $dateStr ($($h.WeeksAgo)w ago): $($perCombo * $totalCombos) images - $($h.Label)" -ForegroundColor DarkCyan
}

$conn.Close()

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Main session : $totalImages images across 3 targets (M31 + Heart + Rosette)" -ForegroundColor Green
Write-Host "  Historical   : $($historySessions.Count) past sessions seeded" -ForegroundColor Green
Write-Host "  Timing events: $($timingEvents.Count) events for overhead analysis" -ForegroundColor Green
Write-Host "  DSS thumbnails will render using real coordinates (no TS required)" -ForegroundColor Green
Write-Host ""
Write-Host "Click 'Send Test Report' in the Night Summary plugin options to preview." -ForegroundColor White
