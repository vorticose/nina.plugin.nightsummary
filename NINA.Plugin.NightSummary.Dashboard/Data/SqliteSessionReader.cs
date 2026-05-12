using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Data;

// Wraps SQLiteDataReader so `reader["MissingCol"]` returns DBNull instead of
// throwing IndexOutOfRangeException. Lets the reader run against any schema
// version the plugin has shipped — the dev harness opens older DBs without
// running migrations, and a backup restored on a newer plugin must not crash.
internal sealed class SchemaSafeReader : IDisposable {
    private readonly SQLiteDataReader r;
    private readonly HashSet<string> cols;
    public SchemaSafeReader(SQLiteDataReader r) {
        this.r = r;
        cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < r.FieldCount; i++) cols.Add(r.GetName(i));
    }
    public object this[string name] => cols.Contains(name) ? r[name] : DBNull.Value;
    public bool Read() => r.Read();
    public void Dispose() => r.Dispose();
}

// Single source of truth for dashboard SELECT queries. Both prod (plugin
// SessionDatabase) and dev (harness) call here. Schema/migration/writes stay
// in plugin SessionDatabase. Read errors route through IDashboardLogger so
// classlib has no dependency on NINA.Core.Utility.Logger.
public sealed class SqliteSessionReader {
    private readonly string connectionString;
    private readonly IDashboardLogger? log;

    public SqliteSessionReader(string connectionString, IDashboardLogger? log = null) {
        this.connectionString = connectionString;
        this.log              = log;
    }

    public List<ImageRecord> GetImagesForSession(string sessionId) {
        var images = new List<ImageRecord>();
        using var conn = new SQLiteConnection(connectionString);
        conn.Open();
        const string sql = "SELECT * FROM Images WHERE SessionId = @SessionId ORDER BY Timestamp";
        using var cmd = new SQLiteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        using var reader = new SchemaSafeReader(cmd.ExecuteReader());
        while (reader.Read()) {
            images.Add(new ImageRecord {
                Id               = Convert.ToInt32(reader["Id"]),
                SessionId        = reader["SessionId"]        == DBNull.Value ? "" : reader["SessionId"].ToString(),
                Timestamp        = reader["Timestamp"]        == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["Timestamp"].ToString()),
                TargetName       = reader["TargetName"]       == DBNull.Value ? "" : reader["TargetName"].ToString(),
                Filter           = reader["Filter"]           == DBNull.Value ? "" : reader["Filter"].ToString(),
                ExposureDuration = reader["ExposureDuration"] == DBNull.Value ? 0 : Convert.ToDouble(reader["ExposureDuration"]),
                HFR              = reader["HFR"]              == DBNull.Value ? 0 : Convert.ToDouble(reader["HFR"]),
                FWHM             = reader["FWHM"]             == DBNull.Value ? 0 : Convert.ToDouble(reader["FWHM"]),
                Eccentricity     = reader["Eccentricity"]     == DBNull.Value ? 0 : Convert.ToDouble(reader["Eccentricity"]),
                StarCount        = reader["StarCount"]        == DBNull.Value ? 0 : Convert.ToInt32(reader["StarCount"]),
                GuidingRMSTotal  = reader["GuidingRMSTotal"]  == DBNull.Value ? 0 : Convert.ToDouble(reader["GuidingRMSTotal"]),
                GuidingScale     = reader["GuidingScale"]     == DBNull.Value ? 1 : Convert.ToDouble(reader["GuidingScale"]),
                Accepted         = reader["Accepted"]         == DBNull.Value ? false : Convert.ToInt32(reader["Accepted"]) == 1,
                RaHours          = reader["RaHours"]          == DBNull.Value ? 0 : Convert.ToDouble(reader["RaHours"]),
                DecDegrees       = reader["DecDegrees"]       == DBNull.Value ? 0 : Convert.ToDouble(reader["DecDegrees"]),
                FocuserTemp      = reader["FocuserTemp"]      == DBNull.Value ? (double?)null : Convert.ToDouble(reader["FocuserTemp"]),
                AmbientTemp      = reader["AmbientTemp"]      == DBNull.Value ? (double?)null : Convert.ToDouble(reader["AmbientTemp"]),
                Gain             = reader["Gain"]             == DBNull.Value ? -1 : Convert.ToInt32(reader["Gain"]),
                Offset           = reader["Offset"]           == DBNull.Value ? -1 : Convert.ToInt32(reader["Offset"]),
                Binning          = reader["Binning"]          == DBNull.Value ?  0 : Convert.ToInt32(reader["Binning"]),
                CameraTemp       = reader["CameraTemp"]       == DBNull.Value ? (double?)null : Convert.ToDouble(reader["CameraTemp"]),
                CoolerSetpoint   = reader["CoolerSetpoint"]   == DBNull.Value ? (double?)null : Convert.ToDouble(reader["CoolerSetpoint"]),
                FocuserPosition  = reader["FocuserPosition"]  == DBNull.Value ? (int?)null    : Convert.ToInt32(reader["FocuserPosition"]),
                RotatorPosition  = reader["RotatorPosition"]  == DBNull.Value ? (double?)null : Convert.ToDouble(reader["RotatorPosition"]),
                PositionAngle    = reader["PositionAngle"]    == DBNull.Value ? (double?)null : Convert.ToDouble(reader["PositionAngle"]),
                Humidity         = reader["Humidity"]         == DBNull.Value ? (double?)null : Convert.ToDouble(reader["Humidity"]),
                DewPoint         = reader["DewPoint"]         == DBNull.Value ? (double?)null : Convert.ToDouble(reader["DewPoint"]),
                WindSpeed        = reader["WindSpeed"]        == DBNull.Value ? (double?)null : Convert.ToDouble(reader["WindSpeed"]),
                Pressure         = reader["Pressure"]         == DBNull.Value ? (double?)null : Convert.ToDouble(reader["Pressure"]),
                SkyBrightness    = reader["SkyBrightness"]    == DBNull.Value ? (double?)null : Convert.ToDouble(reader["SkyBrightness"]),
                SkyTemperature   = reader["SkyTemperature"]   == DBNull.Value ? (double?)null : Convert.ToDouble(reader["SkyTemperature"]),
                WindDirection    = reader["WindDirection"]    == DBNull.Value ? (double?)null : Convert.ToDouble(reader["WindDirection"]),
                WindGust         = reader["WindGust"]         == DBNull.Value ? (double?)null : Convert.ToDouble(reader["WindGust"]),
                GradingStatus    = reader["GradingStatus"]    == DBNull.Value ? -1 : Convert.ToInt32(reader["GradingStatus"]),
                RejectReason     = reader["RejectReason"]     == DBNull.Value ? null : reader["RejectReason"].ToString(),
                ImageType        = reader["ImageType"]        == DBNull.Value ? null : reader["ImageType"].ToString(),
                Altitude         = reader["Altitude"]         == DBNull.Value ? (double?)null : Convert.ToDouble(reader["Altitude"]),
                Azimuth          = reader["Azimuth"]          == DBNull.Value ? (double?)null : Convert.ToDouble(reader["Azimuth"]),
                Airmass          = reader["Airmass"]          == DBNull.Value ? (double?)null : Convert.ToDouble(reader["Airmass"]),
                SideOfPier       = reader["SideOfPier"]       == DBNull.Value ? null : reader["SideOfPier"].ToString(),
                ReadoutMode      = reader["ReadoutMode"]      == DBNull.Value ? null : reader["ReadoutMode"].ToString(),
                SkyQuality       = reader["SkyQuality"]       == DBNull.Value ? (double?)null : Convert.ToDouble(reader["SkyQuality"]),
                CloudCover       = reader["CloudCover"]       == DBNull.Value ? (double?)null : Convert.ToDouble(reader["CloudCover"]),
                SeeingFWHM       = reader["SeeingFWHM"]       == DBNull.Value ? (double?)null : Convert.ToDouble(reader["SeeingFWHM"]),
                StatMedian       = reader["StatMedian"]       == DBNull.Value ? (double?)null : Convert.ToDouble(reader["StatMedian"]),
                StatMean         = reader["StatMean"]         == DBNull.Value ? (double?)null : Convert.ToDouble(reader["StatMean"]),
                StatStDev        = reader["StatStDev"]        == DBNull.Value ? (double?)null : Convert.ToDouble(reader["StatStDev"]),
                StatMAD          = reader["StatMAD"]          == DBNull.Value ? (double?)null : Convert.ToDouble(reader["StatMAD"]),
                StatMin          = reader["StatMin"]          == DBNull.Value ? (int?)null    : Convert.ToInt32(reader["StatMin"]),
                StatMax          = reader["StatMax"]          == DBNull.Value ? (int?)null    : Convert.ToInt32(reader["StatMax"]),
                StatBitDepth     = reader["StatBitDepth"]     == DBNull.Value ? (int?)null    : Convert.ToInt32(reader["StatBitDepth"]),
                ThumbnailVersion = reader["ThumbnailVersion"] == DBNull.Value ? (int?)null    : Convert.ToInt32(reader["ThumbnailVersion"]),
                FilePath         = reader["FilePath"]         == DBNull.Value ? null          : reader["FilePath"].ToString()
            });
        }
        return images;
    }

    public SessionRecord? GetSession(string sessionId) {
        using var conn = new SQLiteConnection(connectionString);
        conn.Open();
        const string sql = "SELECT * FROM Sessions WHERE SessionId = @SessionId";
        using var cmd = new SQLiteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        using var reader = new SchemaSafeReader(cmd.ExecuteReader());
        if (reader.Read()) {
            try {
                return ReadSessionRecord(reader);
            } catch (Exception ex) {
                log?.Error($"Error reading session record field: {ex.Message}");
                throw;
            }
        }
        return null;
    }

    public List<SessionEvent> GetEventsForSession(string sessionId) {
        var events = new List<SessionEvent>();
        using var conn = new SQLiteConnection(connectionString);
        conn.Open();
        const string sql = "SELECT * FROM SessionEvents WHERE SessionId = @SessionId ORDER BY Timestamp";
        using var cmd = new SQLiteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        using var reader = new SchemaSafeReader(cmd.ExecuteReader());
        while (reader.Read()) {
            events.Add(new SessionEvent {
                Id          = Convert.ToInt32(reader["Id"]),
                SessionId   = reader["SessionId"]   == DBNull.Value ? "" : reader["SessionId"].ToString(),
                Timestamp   = reader["Timestamp"]   == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["Timestamp"].ToString()),
                EventType   = reader["EventType"]   == DBNull.Value ? "" : reader["EventType"].ToString(),
                Description = reader["Description"] == DBNull.Value ? "" : reader["Description"].ToString(),
                AfSucceeded = reader["AfSucceeded"] == DBNull.Value ? (bool?)null : Convert.ToInt32(reader["AfSucceeded"]) == 1,
                AfHfr       = reader["AfHfr"]       == DBNull.Value ? (double?)null : Convert.ToDouble(reader["AfHfr"])
            });
        }
        return events;
    }

    public List<TimingEvent> GetTimingEventsForSession(string sessionId) {
        var events = new List<TimingEvent>();
        using var conn = new SQLiteConnection(connectionString);
        conn.Open();
        const string sql = "SELECT * FROM SessionTimingEvents WHERE SessionId = @SessionId ORDER BY StartTime";
        using var cmd = new SQLiteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        using var reader = new SchemaSafeReader(cmd.ExecuteReader());
        while (reader.Read()) {
            events.Add(new TimingEvent {
                EventType       = reader["EventType"]       == DBNull.Value ? "" : reader["EventType"].ToString(),
                StartTime       = reader["StartTime"]       == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["StartTime"].ToString()),
                EndTime         = reader["EndTime"]         == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["EndTime"].ToString()),
                DurationSeconds = reader["DurationSeconds"] == DBNull.Value ? 0 : Convert.ToDouble(reader["DurationSeconds"]),
                Details         = reader["Details"]         == DBNull.Value ? null : reader["Details"].ToString()
            });
        }
        return events;
    }

    public Dictionary<string, double> GetCumulativeIntegrationByTarget(string excludeSessionId) {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        using var conn = new SQLiteConnection(connectionString);
        conn.Open();
        const string sql = @"
            SELECT TargetName, SUM(ExposureDuration) AS TotalSeconds
            FROM Images
            WHERE Accepted = 1 AND SessionId != @SessionId
            GROUP BY TargetName";
        using var cmd = new SQLiteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@SessionId", excludeSessionId ?? "");
        using var reader = new SchemaSafeReader(cmd.ExecuteReader());
        while (reader.Read()) {
            var name  = reader["TargetName"] == DBNull.Value ? "" : reader["TargetName"].ToString();
            var total = reader["TotalSeconds"] == DBNull.Value ? 0 : Convert.ToDouble(reader["TotalSeconds"]);
            if (!string.IsNullOrEmpty(name))
                result[name] = total;
        }
        return result;
    }

    public List<TargetDetail> GetTargetDetails() {
        var targets = new Dictionary<string, TargetDetail>(StringComparer.OrdinalIgnoreCase);

        using var conn = new SQLiteConnection(connectionString);
        conn.Open();

        const string sqlAgg = @"
            SELECT
                i.TargetName,
                SUM(CASE WHEN i.Accepted = 1 THEN i.ExposureDuration ELSE 0 END) AS TotalSeconds,
                COUNT(DISTINCT i.SessionId) AS SessionCount,
                MAX(s.SessionStart) AS LastSessionStart,
                COUNT(*) AS TotalFrames,
                SUM(CASE WHEN i.Accepted = 1 THEN 1 ELSE 0 END) AS AcceptedFrames,
                AVG(CASE WHEN i.Accepted = 1 AND i.HFR > 0 THEN i.HFR END) AS AvgHFR,
                AVG(CASE WHEN i.Accepted = 1 AND i.FWHM > 0 THEN i.FWHM END) AS AvgFWHM,
                AVG(CASE WHEN i.Accepted = 1 AND i.GuidingRMSTotal > 0 THEN i.GuidingRMSTotal END) AS AvgGuidingRMS
            FROM Images i
            JOIN Sessions s ON s.SessionId = i.SessionId
            WHERE i.TargetName IS NOT NULL AND i.TargetName != ''
              AND (i.ImageType IS NULL OR i.ImageType = '' OR i.ImageType = 'LIGHT')
            GROUP BY i.TargetName
            ORDER BY TotalSeconds DESC";

        using (var cmd = new SQLiteCommand(sqlAgg, conn))
        using (var reader = new SchemaSafeReader(cmd.ExecuteReader())) {
            while (reader.Read()) {
                var name = reader["TargetName"].ToString();
                if (string.IsNullOrEmpty(name)) continue;
                targets[name] = new TargetDetail {
                    TargetName              = name,
                    TotalIntegrationSeconds = reader["TotalSeconds"]     == DBNull.Value ? 0 : Convert.ToDouble(reader["TotalSeconds"]),
                    SessionCount            = reader["SessionCount"]     == DBNull.Value ? 0 : Convert.ToInt32(reader["SessionCount"]),
                    LastSessionStart        = reader["LastSessionStart"] == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["LastSessionStart"].ToString()),
                    TotalFrames             = reader["TotalFrames"]      == DBNull.Value ? 0 : Convert.ToInt32(reader["TotalFrames"]),
                    AcceptedFrames          = reader["AcceptedFrames"]   == DBNull.Value ? 0 : Convert.ToInt32(reader["AcceptedFrames"]),
                    AvgHFR                  = reader["AvgHFR"]           == DBNull.Value ? 0 : Math.Round(Convert.ToDouble(reader["AvgHFR"]), 2),
                    AvgFWHM                 = reader["AvgFWHM"]          == DBNull.Value ? 0 : Math.Round(Convert.ToDouble(reader["AvgFWHM"]), 2),
                    AvgGuidingRMS           = reader["AvgGuidingRMS"]    == DBNull.Value ? 0 : Math.Round(Convert.ToDouble(reader["AvgGuidingRMS"]), 2),
                };
            }
        }

        const string sqlFilters = @"
            SELECT
                i.TargetName, i.Filter,
                SUM(CASE WHEN i.Accepted = 1 THEN i.ExposureDuration ELSE 0 END) AS TotalSeconds,
                COUNT(*) AS FrameCount,
                SUM(CASE WHEN i.Accepted = 1 THEN 1 ELSE 0 END) AS AcceptedCount
            FROM Images i
            WHERE i.TargetName IS NOT NULL AND i.TargetName != ''
              AND (i.ImageType IS NULL OR i.ImageType = '' OR i.ImageType = 'LIGHT')
            GROUP BY i.TargetName, i.Filter
            ORDER BY i.TargetName, TotalSeconds DESC";

        using (var cmd = new SQLiteCommand(sqlFilters, conn))
        using (var reader = new SchemaSafeReader(cmd.ExecuteReader())) {
            while (reader.Read()) {
                var name   = reader["TargetName"].ToString();
                var filter = reader["Filter"] == DBNull.Value ? "" : reader["Filter"].ToString();
                if (string.IsNullOrEmpty(name) || !targets.ContainsKey(name)) continue;
                targets[name].Filters.Add(new FilterBreakdown {
                    Filter        = string.IsNullOrEmpty(filter) ? "Unknown" : filter,
                    TotalSeconds  = reader["TotalSeconds"]  == DBNull.Value ? 0 : Convert.ToDouble(reader["TotalSeconds"]),
                    FrameCount    = reader["FrameCount"]    == DBNull.Value ? 0 : Convert.ToInt32(reader["FrameCount"]),
                    AcceptedCount = reader["AcceptedCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["AcceptedCount"]),
                });
            }
        }

        const string sqlCoords = @"
            SELECT i.TargetName, i.SessionId, i.RaHours, i.DecDegrees, s.SessionStart
            FROM Images i
            JOIN Sessions s ON s.SessionId = i.SessionId
            WHERE i.TargetName IS NOT NULL AND i.TargetName != ''
              AND (i.ImageType IS NULL OR i.ImageType = '' OR i.ImageType = 'LIGHT')
            ORDER BY s.SessionStart DESC";

        var coordsDone  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sessionDone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (var cmd = new SQLiteCommand(sqlCoords, conn))
        using (var reader = new SchemaSafeReader(cmd.ExecuteReader())) {
            while (reader.Read()) {
                var name = reader["TargetName"].ToString();
                if (string.IsNullOrEmpty(name) || !targets.ContainsKey(name)) continue;

                if (!sessionDone.Contains(name)) {
                    targets[name].LatestSessionId = reader["SessionId"].ToString();
                    sessionDone.Add(name);
                }

                if (!coordsDone.Contains(name)) {
                    var ra  = reader["RaHours"]    == DBNull.Value ? 0 : Convert.ToDouble(reader["RaHours"]);
                    var dec = reader["DecDegrees"] == DBNull.Value ? 0 : Convert.ToDouble(reader["DecDegrees"]);
                    if (ra != 0 || dec != 0) {
                        targets[name].RaHours    = Math.Round(ra,  4);
                        targets[name].DecDegrees = Math.Round(dec, 4);
                        coordsDone.Add(name);
                    }
                }

                if (coordsDone.Contains(name) && sessionDone.Contains(name) &&
                    coordsDone.Count == targets.Count && sessionDone.Count == targets.Count)
                    break;
            }
        }

        return targets.Values.OrderByDescending(t => t.TotalIntegrationSeconds).ToList();
    }

    public List<TargetSessionHistory> GetSessionHistoryForTarget(string targetName, string excludeSessionId) {
        var result = new List<TargetSessionHistory>();
        using var conn = new SQLiteConnection(connectionString);
        conn.Open();
        const string sql = @"
            SELECT
                s.SessionStart,
                SUM(CASE WHEN i.Accepted = 1 THEN i.ExposureDuration ELSE 0 END) AS IntegrationSeconds,
                AVG(CASE WHEN i.HFR > 0 THEN i.HFR END)               AS AvgHFR,
                AVG(CASE WHEN i.FWHM > 0 THEN i.FWHM END)             AS AvgFWHM,
                AVG(CASE WHEN i.GuidingRMSTotal > 0 THEN i.GuidingRMSTotal END) AS AvgGuidingRMS
            FROM Images i
            JOIN Sessions s ON s.SessionId = i.SessionId
            WHERE i.TargetName = @TargetName
              AND i.SessionId != @ExcludeSessionId
            GROUP BY i.SessionId
            ORDER BY s.SessionStart DESC";

        using var cmd = new SQLiteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@TargetName",       targetName       ?? "");
        cmd.Parameters.AddWithValue("@ExcludeSessionId", excludeSessionId ?? "");
        using var reader = new SchemaSafeReader(cmd.ExecuteReader());
        while (reader.Read()) {
            result.Add(new TargetSessionHistory {
                SessionStart       = reader["SessionStart"]       == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["SessionStart"].ToString()),
                IntegrationSeconds = reader["IntegrationSeconds"] == DBNull.Value ? 0 : Convert.ToDouble(reader["IntegrationSeconds"]),
                AvgHFR             = reader["AvgHFR"]             == DBNull.Value ? 0 : Convert.ToDouble(reader["AvgHFR"]),
                AvgFWHM            = reader["AvgFWHM"]            == DBNull.Value ? 0 : Convert.ToDouble(reader["AvgFWHM"]),
                AvgGuidingRMS      = reader["AvgGuidingRMS"]      == DBNull.Value ? 0 : Convert.ToDouble(reader["AvgGuidingRMS"])
            });
        }
        return result;
    }

    public List<TargetSessionDetail> GetSessionsForTarget(string targetName) {
        var sessions = new Dictionary<string, TargetSessionDetail>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(targetName)) return new List<TargetSessionDetail>();

        using var conn = new SQLiteConnection(connectionString);
        conn.Open();

        const string sqlAgg = @"
            SELECT
                s.SessionId, s.SessionStart, s.SessionEnd,
                SUM(CASE WHEN i.Accepted = 1 THEN i.ExposureDuration ELSE 0 END) AS IntegrationSeconds,
                COUNT(*)                                                         AS FrameCount,
                SUM(CASE WHEN i.Accepted = 1 THEN 1 ELSE 0 END)                 AS AcceptedFrames,
                AVG(CASE WHEN i.Accepted = 1 AND i.HFR > 0 THEN i.HFR END)       AS AvgHFR,
                AVG(CASE WHEN i.Accepted = 1 AND i.GuidingRMSTotal > 0 THEN i.GuidingRMSTotal END) AS AvgGuidingRMS
            FROM Images i
            JOIN Sessions s ON s.SessionId = i.SessionId
            WHERE i.TargetName = @TargetName COLLATE NOCASE
              AND (i.ImageType IS NULL OR i.ImageType = '' OR i.ImageType = 'LIGHT')
            GROUP BY s.SessionId, s.SessionStart, s.SessionEnd
            ORDER BY s.SessionStart DESC";

        using (var cmd = new SQLiteCommand(sqlAgg, conn)) {
            cmd.Parameters.AddWithValue("@TargetName", targetName);
            using var reader = new SchemaSafeReader(cmd.ExecuteReader());
            while (reader.Read()) {
                var sid = reader["SessionId"].ToString();
                if (string.IsNullOrEmpty(sid)) continue;
                sessions[sid] = new TargetSessionDetail {
                    SessionId          = sid,
                    SessionStart       = reader["SessionStart"]       == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["SessionStart"].ToString()),
                    SessionEnd         = reader["SessionEnd"]         == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["SessionEnd"].ToString()),
                    IntegrationSeconds = reader["IntegrationSeconds"] == DBNull.Value ? 0 : Convert.ToDouble(reader["IntegrationSeconds"]),
                    FrameCount         = reader["FrameCount"]         == DBNull.Value ? 0 : Convert.ToInt32(reader["FrameCount"]),
                    AcceptedFrames     = reader["AcceptedFrames"]     == DBNull.Value ? 0 : Convert.ToInt32(reader["AcceptedFrames"]),
                    AvgHFR             = reader["AvgHFR"]             == DBNull.Value ? 0 : Math.Round(Convert.ToDouble(reader["AvgHFR"]), 2),
                    AvgGuidingRMS      = reader["AvgGuidingRMS"]      == DBNull.Value ? 0 : Math.Round(Convert.ToDouble(reader["AvgGuidingRMS"]), 2),
                };
            }
        }

        if (sessions.Count == 0) return new List<TargetSessionDetail>();

        const string sqlFilters = @"
            SELECT
                i.SessionId, i.Filter,
                SUM(CASE WHEN i.Accepted = 1 THEN i.ExposureDuration ELSE 0 END) AS IntegrationSeconds,
                COUNT(*)                                                         AS FrameCount,
                SUM(CASE WHEN i.Accepted = 1 THEN 1 ELSE 0 END)                 AS AcceptedFrames,
                AVG(CASE WHEN i.Accepted = 1 AND i.HFR > 0 THEN i.HFR END)       AS AvgHFR,
                AVG(CASE WHEN i.Accepted = 1 AND i.GuidingRMSTotal > 0 THEN i.GuidingRMSTotal END) AS AvgGuidingRMS
            FROM Images i
            WHERE i.TargetName = @TargetName COLLATE NOCASE
              AND (i.ImageType IS NULL OR i.ImageType = '' OR i.ImageType = 'LIGHT')
            GROUP BY i.SessionId, i.Filter
            ORDER BY i.SessionId, IntegrationSeconds DESC";

        using (var cmd = new SQLiteCommand(sqlFilters, conn)) {
            cmd.Parameters.AddWithValue("@TargetName", targetName);
            using var reader = new SchemaSafeReader(cmd.ExecuteReader());
            while (reader.Read()) {
                var sid = reader["SessionId"].ToString();
                if (!sessions.ContainsKey(sid)) continue;
                var filter = reader["Filter"] == DBNull.Value ? "" : reader["Filter"].ToString();
                sessions[sid].Filters.Add(new TargetSessionFilterDetail {
                    Filter             = string.IsNullOrEmpty(filter) ? "Unknown" : filter,
                    IntegrationSeconds = reader["IntegrationSeconds"] == DBNull.Value ? 0 : Convert.ToDouble(reader["IntegrationSeconds"]),
                    FrameCount         = reader["FrameCount"]         == DBNull.Value ? 0 : Convert.ToInt32(reader["FrameCount"]),
                    AcceptedFrames     = reader["AcceptedFrames"]     == DBNull.Value ? 0 : Convert.ToInt32(reader["AcceptedFrames"]),
                    AvgHFR             = reader["AvgHFR"]             == DBNull.Value ? 0 : Math.Round(Convert.ToDouble(reader["AvgHFR"]), 2),
                    AvgGuidingRMS      = reader["AvgGuidingRMS"]      == DBNull.Value ? 0 : Math.Round(Convert.ToDouble(reader["AvgGuidingRMS"]), 2),
                });
            }
        }

        return sessions.Values.OrderByDescending(s => s.SessionStart).ToList();
    }

    public List<SessionRecord> GetRecentSessions(int limit) {
        var result = new List<SessionRecord>();
        using var conn = new SQLiteConnection(connectionString);
        conn.Open();
        var sql = SessionListWithCountsSql + " ORDER BY s.SessionStart DESC LIMIT @Limit";
        using var cmd = new SQLiteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Limit", limit);
        using var reader = new SchemaSafeReader(cmd.ExecuteReader());
        while (reader.Read()) {
            try { result.Add(ReadEnrichedSessionRecord(reader)); }
            catch (Exception ex) { log?.Error($"Error reading session record: {ex.Message}"); }
        }
        return result;
    }

    public List<SessionRecord> GetSessionsByDateRange(DateTime from, DateTime to) {
        var result = new List<SessionRecord>();
        using var conn = new SQLiteConnection(connectionString);
        conn.Open();
        var sql = SessionListWithCountsSql +
            " WHERE s.SessionStart >= @From AND s.SessionStart <= @To ORDER BY s.SessionStart DESC";
        using var cmd = new SQLiteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@From", from.ToString("o"));
        cmd.Parameters.AddWithValue("@To",   to.Date.AddDays(1).AddSeconds(-1).ToString("o"));
        using var reader = new SchemaSafeReader(cmd.ExecuteReader());
        while (reader.Read()) {
            try { result.Add(ReadEnrichedSessionRecord(reader)); }
            catch (Exception ex) { log?.Error($"Error reading session record: {ex.Message}"); }
        }
        return result;
    }

    public List<SessionRecord> GetAllSessions() {
        var result = new List<SessionRecord>();
        using var conn = new SQLiteConnection(connectionString);
        conn.Open();
        const string sql = "SELECT * FROM Sessions ORDER BY SessionStart DESC";
        using var cmd = new SQLiteCommand(sql, conn);
        using var reader = new SchemaSafeReader(cmd.ExecuteReader());
        while (reader.Read()) {
            try { result.Add(ReadSessionRecord(reader)); }
            catch (Exception ex) { log?.Error($"Error reading session record: {ex.Message}"); }
        }
        return result;
    }

    public SessionRecord? GetLatestSession() {
        using var conn = new SQLiteConnection(connectionString);
        conn.Open();
        const string sql = "SELECT * FROM Sessions ORDER BY SessionStart DESC LIMIT 1";
        using var cmd = new SQLiteCommand(sql, conn);
        using var reader = new SchemaSafeReader(cmd.ExecuteReader());
        if (reader.Read()) {
            try {
                return ReadSessionRecord(reader);
            } catch (Exception ex) {
                log?.Error($"Error reading latest session record: {ex.Message}");
                throw;
            }
        }
        return null;
    }

    // Shared SELECT for session-list methods that need image/target/integration counts
    // for display in the dropdown. Counts use Accepted = 1 to match what the report shows.
    private const string SessionListWithCountsSql = @"
        SELECT s.*,
            (SELECT COUNT(*) FROM Images WHERE SessionId = s.SessionId AND Accepted = 1) AS ImageCount,
            (SELECT COUNT(DISTINCT TargetName) FROM Images WHERE SessionId = s.SessionId AND Accepted = 1 AND TargetName IS NOT NULL AND TargetName <> '') AS TargetCount,
            (SELECT COALESCE(SUM(ExposureDuration), 0) FROM Images WHERE SessionId = s.SessionId AND Accepted = 1) AS IntegrationSeconds
        FROM Sessions s";

    private static SessionRecord ReadEnrichedSessionRecord(SchemaSafeReader reader) {
        var record = ReadSessionRecord(reader);
        record.ImageCount         = reader["ImageCount"]         == DBNull.Value ? 0 : Convert.ToInt32(reader["ImageCount"]);
        record.TargetCount        = reader["TargetCount"]        == DBNull.Value ? 0 : Convert.ToInt32(reader["TargetCount"]);
        record.IntegrationSeconds = reader["IntegrationSeconds"] == DBNull.Value ? 0 : Convert.ToDouble(reader["IntegrationSeconds"]);
        return record;
    }

    private static SessionRecord ReadSessionRecord(SchemaSafeReader reader) {
        return new SessionRecord {
            Id                = Convert.ToInt32(reader["Id"]),
            SessionId         = reader["SessionId"]         == DBNull.Value ? "" : reader["SessionId"].ToString(),
            SessionStart      = reader["SessionStart"]      == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["SessionStart"].ToString()),
            SessionEnd        = reader["SessionEnd"]        == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["SessionEnd"].ToString()),
            ProfileName       = reader["ProfileName"]       == DBNull.Value ? "" : reader["ProfileName"].ToString(),
            Notes             = reader["Notes"]             == DBNull.Value ? "" : reader["Notes"].ToString(),
            ReportSent        = reader["ReportSent"]        == DBNull.Value ? false : Convert.ToInt32(reader["ReportSent"]) == 1,
            CamXSize          = reader["CamXSize"]          == DBNull.Value ? 0 : Convert.ToInt32(reader["CamXSize"]),
            CamYSize          = reader["CamYSize"]          == DBNull.Value ? 0 : Convert.ToInt32(reader["CamYSize"]),
            PixelSizeMicrons  = reader["PixelSizeMicrons"]  == DBNull.Value ? 0 : Convert.ToDouble(reader["PixelSizeMicrons"]),
            FocalLengthMm     = reader["FocalLengthMm"]     == DBNull.Value ? 0 : Convert.ToDouble(reader["FocalLengthMm"]),
            SkippedExposures  = reader["SkippedExposures"]  == DBNull.Value ? 0 : Convert.ToInt32(reader["SkippedExposures"]),
            CameraName        = reader["CameraName"]        == DBNull.Value ? null : reader["CameraName"].ToString(),
            TelescopeName     = reader["TelescopeName"]     == DBNull.Value ? null : reader["TelescopeName"].ToString(),
            MountName         = reader["MountName"]         == DBNull.Value ? null : reader["MountName"].ToString(),
            FilterWheelName   = reader["FilterWheelName"]   == DBNull.Value ? null : reader["FilterWheelName"].ToString(),
            FocuserName       = reader["FocuserName"]       == DBNull.Value ? null : reader["FocuserName"].ToString(),
            RotatorName       = reader["RotatorName"]       == DBNull.Value ? null : reader["RotatorName"].ToString(),
            GuiderName        = reader["GuiderName"]        == DBNull.Value ? null : reader["GuiderName"].ToString(),
            DomeName          = reader["DomeName"]          == DBNull.Value ? null : reader["DomeName"].ToString(),
            FlatDeviceName    = reader["FlatDeviceName"]    == DBNull.Value ? null : reader["FlatDeviceName"].ToString(),
            SafetyMonitorName = reader["SafetyMonitorName"] == DBNull.Value ? null : reader["SafetyMonitorName"].ToString(),
            WeatherName       = reader["WeatherName"]       == DBNull.Value ? null : reader["WeatherName"].ToString(),
            SwitchName        = reader["SwitchName"]        == DBNull.Value ? null : reader["SwitchName"].ToString()
        };
    }
}
