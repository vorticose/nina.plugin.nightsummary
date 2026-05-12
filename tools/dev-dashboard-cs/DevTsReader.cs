using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using NINA.Plugin.NightSummary.Data;

namespace NINA.Plugin.NightSummary.DevHost;

// Slim read-only Target Scheduler SQLite reader for the dev harness.
// Mirrors the project-tree query in NINA.Plugin.NightSummary.Data.TargetSchedulerDatabase
// but lives outside the main plugin so it has no NINA.Core.Utility (Logger) dependency.
internal sealed class DevTsReader {
    private readonly string dbPath;

    public DevTsReader(string dbPath) {
        this.dbPath = dbPath;
    }

    public bool IsAvailable => !string.IsNullOrEmpty(dbPath) && File.Exists(dbPath);

    public List<TsProjectInfo> GetAllProjects() {
        if (!IsAvailable) return new List<TsProjectInfo>();

        try {
            var connectionString = $"Data Source={dbPath};Version=3;Read Only=True;";
            using var conn = new SQLiteConnection(connectionString);
            conn.Open();

            var sql = @"
                SELECT
                    p.Id               AS PId,
                    p.guid             AS PGuid,
                    p.ProfileId        AS PProfile,
                    p.name             AS PName,
                    p.description      AS PDesc,
                    p.state            AS PState,
                    p.priority         AS PPriority,
                    p.isMosaic         AS PMosaic,
                    p.createDate       AS PCreate,
                    p.activeDate       AS PActive,
                    p.inactiveDate     AS PInactive,
                    p.minimumAltitude  AS PMinAlt,
                    p.maximumAltitude  AS PMaxAlt,
                    t.Id               AS TId,
                    t.guid             AS TGuid,
                    t.name             AS TName,
                    t.active           AS TActive,
                    t.ra               AS TRa,
                    t.dec              AS TDec,
                    t.rotation         AS TRotation,
                    ep.exposure        AS EpExposure,
                    ep.desired         AS EpDesired,
                    ep.acquired        AS EpAcquired,
                    ep.accepted        AS EpAccepted,
                    et.name            AS EtName,
                    et.filtername      AS EtFilter,
                    et.defaultexposure AS EtDefault
                FROM project p
                LEFT JOIN target t            ON t.ProjectId = p.Id
                LEFT JOIN exposureplan ep     ON ep.targetid = t.Id
                LEFT JOIN exposuretemplate et ON et.Id = ep.exposureTemplateId
                ORDER BY p.Id, t.Id, et.filtername, et.name";

            var projects = new Dictionary<int, TsProjectInfo>();
            var targets  = new Dictionary<int, TsProjectTarget>();

            using var cmd = new SQLiteCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) {
                int pid = Convert.ToInt32(reader["PId"]);
                if (!projects.TryGetValue(pid, out var proj)) {
                    int stateVal = reader["PState"] == DBNull.Value ? 0 : Convert.ToInt32(reader["PState"]);
                    int priVal   = reader["PPriority"] == DBNull.Value ? 1 : Convert.ToInt32(reader["PPriority"]);
                    proj = new TsProjectInfo {
                        Id              = pid,
                        Guid            = reader["PGuid"]    == DBNull.Value ? null : reader["PGuid"].ToString(),
                        ProfileId       = reader["PProfile"] == DBNull.Value ? null : reader["PProfile"].ToString(),
                        Name            = reader["PName"]    == DBNull.Value ? null : reader["PName"].ToString(),
                        Description     = reader["PDesc"]    == DBNull.Value ? null : reader["PDesc"].ToString(),
                        StateValue      = stateVal,
                        State           = ProjectStateName(stateVal),
                        PriorityValue   = priVal,
                        Priority        = ProjectPriorityName(priVal),
                        IsMosaic        = reader["PMosaic"]  != DBNull.Value && Convert.ToInt32(reader["PMosaic"]) == 1,
                        CreateDate      = UnixSecondsToNullableDate(reader["PCreate"]),
                        ActiveDate      = UnixSecondsToNullableDate(reader["PActive"]),
                        InactiveDate    = UnixSecondsToNullableDate(reader["PInactive"]),
                        MinimumAltitude = reader["PMinAlt"]  == DBNull.Value ? 0 : Convert.ToDouble(reader["PMinAlt"]),
                        MaximumAltitude = reader["PMaxAlt"]  == DBNull.Value ? 0 : Convert.ToDouble(reader["PMaxAlt"]),
                    };
                    projects[pid] = proj;
                }

                if (reader["TId"] == DBNull.Value) continue;
                int tid = Convert.ToInt32(reader["TId"]);
                if (!targets.TryGetValue(tid, out var tgt)) {
                    tgt = new TsProjectTarget {
                        Id        = tid,
                        Guid      = reader["TGuid"] == DBNull.Value ? null : reader["TGuid"].ToString(),
                        ProjectId = pid,
                        Name      = reader["TName"] == DBNull.Value ? null : reader["TName"].ToString(),
                        Active    = reader["TActive"] != DBNull.Value && Convert.ToInt32(reader["TActive"]) == 1,
                        RA        = reader["TRa"]  == DBNull.Value ? 0 : Convert.ToDouble(reader["TRa"]),
                        Dec       = reader["TDec"] == DBNull.Value ? 0 : Convert.ToDouble(reader["TDec"]),
                        Rotation  = reader["TRotation"] == DBNull.Value ? 0 : Convert.ToDouble(reader["TRotation"]),
                    };
                    targets[tid] = tgt;
                    proj.Targets.Add(tgt);
                }

                if (reader["EpDesired"] == DBNull.Value) continue;
                var epExposure = reader["EpExposure"] == DBNull.Value ? 0 : Convert.ToDouble(reader["EpExposure"]);
                var etDefault  = reader["EtDefault"]  == DBNull.Value ? 0 : Convert.ToDouble(reader["EtDefault"]);
                tgt.ExposurePlans.Add(new TsProjectExposurePlan {
                    Filter       = reader["EtFilter"] == DBNull.Value ? "" : reader["EtFilter"].ToString(),
                    TemplateName = reader["EtName"]   == DBNull.Value ? "" : reader["EtName"].ToString(),
                    ExposureSec  = epExposure > 0 ? epExposure : etDefault,
                    Desired      = Convert.ToInt32(reader["EpDesired"]),
                    Acquired     = reader["EpAcquired"] == DBNull.Value ? 0 : Convert.ToInt32(reader["EpAcquired"]),
                    Accepted     = reader["EpAccepted"] == DBNull.Value ? 0 : Convert.ToInt32(reader["EpAccepted"]),
                });
            }

            return projects.Values.ToList();
        } catch (Exception ex) {
            Console.Error.WriteLine($"[DevTsReader] GetAllProjects failed: {ex.Message}");
            return new List<TsProjectInfo>();
        }
    }

    // Mirror of TargetSchedulerDatabase.GetImageAugment for the dev harness.
    // Tries direct match (new NS rows) then legacy-shifted match (NS pre-fix rows
    // where Timestamp = ImageSaved). Returns null on no match or TS unavailable.
    public TsImageAugment? GetImageAugment(string targetName, string filterName, DateTime ts, int windowSeconds, double exposureDurationSeconds = 0) {
        if (!IsAvailable) return null;
        if (string.IsNullOrEmpty(targetName)) return null;

        try {
            var connectionString = $"Data Source={dbPath};Version=3;Read Only=True;";
            using var conn = new SQLiteConnection(connectionString);
            conn.Open();
            long centerDirect = new DateTimeOffset(ts.ToUniversalTime()).ToUnixTimeSeconds();

            var aug = QueryAugment(conn, targetName, filterName, centerDirect, windowSeconds);
            if (aug != null) return aug;

            if (exposureDurationSeconds > 0) {
                long centerShifted = centerDirect - (long)exposureDurationSeconds;
                return QueryAugment(conn, targetName, filterName, centerShifted, windowSeconds);
            }
            return null;
        } catch (Exception ex) {
            Console.Error.WriteLine($"[DevTsReader] GetImageAugment failed: {ex.Message}");
            return null;
        }
    }

    private static TsImageAugment? QueryAugment(SQLiteConnection conn, string targetName, string filterName, long center, int windowSeconds) {
        const string sql = @"
            SELECT a.metadata, a.gradingStatus, a.rejectreason,
                   p.name AS projectName, et.name AS templateName
            FROM acquiredimage a
            JOIN target           t  ON a.targetId   = t.id
            LEFT JOIN project     p  ON a.projectId  = p.id
            LEFT JOIN exposureplan ep ON a.exposureId = ep.id
            LEFT JOIN exposuretemplate et ON et.id = ep.exposureTemplateId
            WHERE a.acquireddate BETWEEN @Lo AND @Hi
              AND t.name       = @Target COLLATE NOCASE
              AND a.filtername = @Filter COLLATE NOCASE
            ORDER BY ABS(a.acquireddate - @Center)
            LIMIT 1";

        using var cmd = new SQLiteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Lo",     center - windowSeconds);
        cmd.Parameters.AddWithValue("@Hi",     center + windowSeconds);
        cmd.Parameters.AddWithValue("@Center", center);
        cmd.Parameters.AddWithValue("@Target", targetName);
        cmd.Parameters.AddWithValue("@Filter", filterName ?? "");
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var aug = new TsImageAugment {
            ProjectName          = reader["projectName"] as string,
            ExposureTemplateName = reader["templateName"] as string,
            GradingStatus        = reader["gradingStatus"] == DBNull.Value
                ? (int?)null
                : Convert.ToInt32(reader["gradingStatus"]),
            RejectReason         = reader["rejectreason"] as string
        };

        var json = reader["metadata"] as string;
        if (!string.IsNullOrEmpty(json)) {
            try {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                aug.HFRStDev            = TryDouble(root, "HFRStDev");
                aug.GuidingRMSRA        = TryDouble(root, "GuidingRMSRA");
                aug.GuidingRMSRAArcSec  = TryDouble(root, "GuidingRMSRAArcSec");
                aug.GuidingRMSDEC       = TryDouble(root, "GuidingRMSDEC");
                aug.GuidingRMSDECArcSec = TryDouble(root, "GuidingRMSDECArcSec");
            } catch { /* malformed JSON — leave fields null */ }
        }
        return aug;
    }

    private static double? TryDouble(System.Text.Json.JsonElement root, string name) {
        if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
        if (!root.TryGetProperty(name, out var prop)) return null;
        if (prop.ValueKind == System.Text.Json.JsonValueKind.Number && prop.TryGetDouble(out var d)) return d;
        if (prop.ValueKind == System.Text.Json.JsonValueKind.String
            && double.TryParse(prop.GetString(), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out var ds)) return ds;
        return null;
    }

    public (bool Enabled, int Port) GetApiSettings() {
        if (!IsAvailable) return (false, 0);
        try {
            var connectionString = $"Data Source={dbPath};Version=3;Read Only=True;";
            using var conn = new SQLiteConnection(connectionString);
            conn.Open();
            using var cmd = new SQLiteCommand(
                "SELECT enableAPI, apiPort FROM profilepreference LIMIT 1", conn);
            using var reader = cmd.ExecuteReader();
            if (reader.Read()) {
                bool enabled = Convert.ToInt32(reader["enableAPI"]) == 1;
                int port     = Convert.ToInt32(reader["apiPort"]);
                return (enabled, port);
            }
        } catch (Exception ex) {
            Console.Error.WriteLine($"[DevTsReader] GetApiSettings failed: {ex.Message}");
        }
        return (false, 0);
    }

    private static string ProjectStateName(int v) => v switch {
        0 => "Draft", 1 => "Active", 2 => "Inactive", 3 => "Closed", _ => "Unknown"
    };

    private static string ProjectPriorityName(int v) => v switch {
        0 => "Low", 1 => "Normal", 2 => "High", _ => "Unknown"
    };

    private static DateTime? UnixSecondsToNullableDate(object raw) {
        if (raw == null || raw == DBNull.Value) return null;
        try {
            long seconds = Convert.ToInt64(raw);
            if (seconds <= 0) return null;
            return DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
        } catch {
            return null;
        }
    }
}
