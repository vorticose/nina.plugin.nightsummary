using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;

namespace NINA.Plugin.NightSummary.Data {
    /// <summary>
    /// Read-only access to the Target Scheduler SQLite database.
    /// Returns per-target, per-filter exposure progress (desired/acquired/accepted)
    /// and target coordinates (RA/Dec) for targets that were imaged in the current session.
    /// Gracefully returns empty results if the TS database is not found.
    /// </summary>
    public class TargetSchedulerDatabase {

        private static readonly string DefaultDbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "SchedulerPlugin", "schedulerdb.sqlite");

        private readonly string dbPath;

        public TargetSchedulerDatabase(string customPath = null) {
            dbPath = customPath ?? DefaultDbPath;
        }

        /// <summary>
        /// True if the Target Scheduler plugin DLL is present in the NINA plugins folder.
        /// Checks the plugin installation directory rather than the database, so that
        /// a leftover DB from a previous install does not produce a false positive.
        /// </summary>
        public static bool IsPluginInstalled {
            get {
                var pluginsRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NINA", "Plugins");
                if (!Directory.Exists(pluginsRoot)) return false;
                return Directory.EnumerateFiles(pluginsRoot, "*TargetScheduler*.dll", SearchOption.AllDirectories).Any();
            }
        }

        public bool IsAvailable => File.Exists(dbPath);

        // Exposed so high-volume callers (importer) can open one connection and
        // reuse it across many queries instead of paying open/close per row.
        public string GetDbPath() => dbPath;

        /// <summary>
        /// Returns TS progress data for the given set of target names.
        /// Only targets whose names match (case-insensitive) are returned.
        /// Returns an empty list if the database is not found or any error occurs.
        /// </summary>
        public List<TsTargetData> GetProgressForTargets(IEnumerable<string> sessionTargetNames, string profileId = null) {
            if (!IsAvailable) {
                Logger.Info("NightSummary: Target Scheduler database not found — skipping TS progress");
                return new List<TsTargetData>();
            }

            var nameSet = new HashSet<string>(
                sessionTargetNames.Select(n => n.Trim()),
                StringComparer.OrdinalIgnoreCase);

            try {
                Logger.Info($"NightSummary: Querying TS progress for profile {profileId ?? "(all)"}");
                var connectionString = $"Data Source={dbPath};Version=3;Read Only=True;";
                using (var conn = new SQLiteConnection(connectionString)) {
                    conn.Open();
                    return QueryProgress(conn, nameSet, profileId);
                }
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to read Target Scheduler database. {ex.Message}");
                return new List<TsTargetData>();
            }
        }

        /// <summary>
        /// Returns raw acquired-image rows from the TS database whose acquireddate falls within
        /// [start, end] (inclusive). Returns an empty list if TS is unavailable or any error occurs.
        /// </summary>
        public List<TsAcquiredImage> GetAcquiredImagesForDateRange(DateTime start, DateTime end) {
            if (!IsAvailable) return new List<TsAcquiredImage>();

            try {
                var connectionString = $"Data Source={dbPath};Version=3;Read Only=True;";
                using (var conn = new SQLiteConnection(connectionString)) {
                    conn.Open();
                    long startUnix = new DateTimeOffset(start.ToUniversalTime()).ToUnixTimeSeconds();
                    long endUnix   = new DateTimeOffset(end.ToUniversalTime()).ToUnixTimeSeconds();

                    const string sql = @"
                        SELECT id, acquireddate, filtername, gradingstatus, rejectreason
                        FROM acquiredimage
                        WHERE acquireddate >= @Start AND acquireddate <= @End";

                    var result = new List<TsAcquiredImage>();
                    using (var cmd = new SQLiteCommand(sql, conn)) {
                        cmd.Parameters.AddWithValue("@Start", startUnix);
                        cmd.Parameters.AddWithValue("@End",   endUnix);
                        using (var reader = cmd.ExecuteReader()) {
                            while (reader.Read()) {
                                long unixTs = Convert.ToInt64(reader["acquireddate"]);
                                result.Add(new TsAcquiredImage {
                                    AcquiredAt     = DateTimeOffset.FromUnixTimeSeconds(unixTs).LocalDateTime,
                                    FilterName     = reader["filtername"]     == DBNull.Value ? "" : reader["filtername"].ToString(),
                                    GradingStatus  = reader["gradingstatus"]  == DBNull.Value ? -1 : Convert.ToInt32(reader["gradingstatus"]),
                                    RejectReason   = reader["rejectreason"]   == DBNull.Value ? null : reader["rejectreason"].ToString()
                                });
                            }
                        }
                    }
                    return result;
                }
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to read TS acquiredimage table. {ex.Message}");
                return new List<TsAcquiredImage>();
            }
        }

        /// <summary>
        /// Returns the JPEG thumbnail blob TS stored for the LIGHT frame matching
        /// (<paramref name="targetName"/>, <paramref name="filterName"/>) at
        /// <paramref name="ts"/> ± <paramref name="windowSeconds"/>. Returns null
        /// if no match, no thumb, or any error. Used by ThumbnailImporter for the
        /// one-shot historical backfill — see RAW_THUMBNAILS_DESIGN.md.
        /// </summary>
        public byte[] GetThumbnailBlob(string targetName, string filterName, DateTime ts, int windowSeconds) {
            if (!IsAvailable) return null;
            if (string.IsNullOrEmpty(targetName)) return null;

            try {
                var connectionString = $"Data Source={dbPath};Version=3;Read Only=True;";
                using (var conn = new SQLiteConnection(connectionString)) {
                    conn.Open();
                    long center = new DateTimeOffset(ts.ToUniversalTime()).ToUnixTimeSeconds();
                    long lo = center - windowSeconds;
                    long hi = center + windowSeconds;

                    // Pick the closest match in case multiple TS rows fall in the window.
                    const string sql = @"
                        SELECT i.imagedata
                        FROM acquiredimage a
                        JOIN target       t ON a.targetId = t.id
                        JOIN imagedata    i ON i.AcquiredImageId = a.Id
                        WHERE a.acquireddate BETWEEN @Lo AND @Hi
                          AND t.name      = @Target COLLATE NOCASE
                          AND a.filtername = @Filter COLLATE NOCASE
                        ORDER BY ABS(a.acquireddate - @Center)
                        LIMIT 1";

                    using (var cmd = new SQLiteCommand(sql, conn)) {
                        cmd.Parameters.AddWithValue("@Lo",     lo);
                        cmd.Parameters.AddWithValue("@Hi",     hi);
                        cmd.Parameters.AddWithValue("@Center", center);
                        cmd.Parameters.AddWithValue("@Target", targetName);
                        cmd.Parameters.AddWithValue("@Filter", filterName ?? "");
                        var blob = cmd.ExecuteScalar();
                        return blob == DBNull.Value || blob == null ? null : (byte[])blob;
                    }
                }
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: GetThumbnailBlob failed (target={targetName}, filter={filterName}): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Looks up the TS row matching (<paramref name="targetName"/>,
        /// <paramref name="filterName"/>) for the NS image timestamped <paramref name="ts"/>.
        ///
        /// Tries two centers in order:
        ///   1. <paramref name="ts"/> directly — for new NS rows that capture
        ///      <c>ExposureStart</c> in their Timestamp column (post-fix convention).
        ///   2. <paramref name="ts"/> minus <paramref name="exposureDurationSeconds"/> —
        ///      for legacy NS rows that captured the ImageSaved time, which lags
        ///      ExposureStart by ~exposure duration plus a few seconds of overhead.
        ///
        /// Each attempt searches within ±<paramref name="windowSeconds"/> seconds.
        /// Returns null on no match, TS unavailable, or any error.
        /// </summary>
        public TsImageAugment GetImageAugment(string targetName, string filterName, DateTime ts, int windowSeconds, double exposureDurationSeconds = 0) {
            if (!IsAvailable) return null;
            if (string.IsNullOrEmpty(targetName)) return null;

            try {
                var connectionString = $"Data Source={dbPath};Version=3;Read Only=True;";
                using (var conn = new SQLiteConnection(connectionString)) {
                    conn.Open();
                    long centerDirect = new DateTimeOffset(ts.ToUniversalTime()).ToUnixTimeSeconds();

                    // Direct attempt — new convention.
                    var aug = QueryAugment(conn, targetName, filterName, centerDirect, windowSeconds);
                    if (aug != null) return aug;

                    // Fallback — legacy NS rows, shift back by exposure duration.
                    if (exposureDurationSeconds > 0) {
                        long centerShifted = centerDirect - (long)exposureDurationSeconds;
                        return QueryAugment(conn, targetName, filterName, centerShifted, windowSeconds);
                    }
                    return null;
                }
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: GetImageAugment failed (target={targetName}, filter={filterName}): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads the TS API settings (enableAPI, apiPort) from the profilepreference table.
        /// Returns (false, 0) if TS is unavailable or any error occurs.
        /// </summary>
        public (bool Enabled, int Port) GetApiSettings(string profileId = null) {
            if (!IsAvailable) return (false, 0);

            try {
                var connectionString = $"Data Source={dbPath};Version=3;Read Only=True;";
                using (var conn = new SQLiteConnection(connectionString)) {
                    conn.Open();
                    var sql = profileId != null
                        ? "SELECT enableAPI, apiPort FROM profilepreference WHERE ProfileId = @ProfileId"
                        : "SELECT enableAPI, apiPort FROM profilepreference LIMIT 1";
                    using (var cmd = new SQLiteCommand(sql, conn)) {
                        if (profileId != null) cmd.Parameters.AddWithValue("@ProfileId", profileId);
                        using (var reader = cmd.ExecuteReader()) {
                            if (reader.Read()) {
                                bool enabled = Convert.ToInt32(reader["enableAPI"]) == 1;
                                int port     = Convert.ToInt32(reader["apiPort"]);
                                Logger.Info($"NightSummary: TS API settings for profile '{profileId ?? "(any)"}' — enableAPI={enabled}, apiPort={port}");
                                return (enabled, port);
                            }
                            Logger.Warning($"NightSummary: No TS profilepreference row found for profile '{profileId ?? "(any)"}'. TS API check will return not-enabled.");
                        }
                    }
                }
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to read TS API settings. {ex.Message}");
            }
            return (false, 0);
        }

        /// <summary>
        /// Returns the full Target Scheduler project tree (projects → targets → exposure plans)
        /// for the Stats Targets tab (Phase 3a). Reads directly from the TS SQLite database.
        /// Returns an empty list if TS is unavailable or any error occurs.
        /// If <paramref name="profileId"/> is null, projects across all profiles are returned.
        /// </summary>
        public List<TsProjectInfo> GetAllProjects(string profileId = null) {
            if (!IsAvailable) return new List<TsProjectInfo>();

            try {
                var connectionString = $"Data Source={dbPath};Version=3;Read Only=True;";
                using (var conn = new SQLiteConnection(connectionString)) {
                    conn.Open();

                    // One query to rule them all: project + target + exposureplan + exposuretemplate,
                    // ordered so we can stream-group them into a nested structure.
                    // LEFT JOINs so projects without targets, or targets without exposure plans,
                    // still appear (we want to show them in the stats tab too).
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
                        LEFT JOIN exposuretemplate et ON et.Id = ep.exposureTemplateId" +
                        (profileId != null ? " WHERE p.ProfileId = @ProfileId" : "") +
                        " ORDER BY p.Id, t.Id, et.filtername, et.name";

                    var projects = new Dictionary<int, TsProjectInfo>();
                    var targets  = new Dictionary<int, TsProjectTarget>();

                    using (var cmd = new SQLiteCommand(sql, conn)) {
                        if (profileId != null) cmd.Parameters.AddWithValue("@ProfileId", profileId);
                        using (var reader = cmd.ExecuteReader()) {
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
                        }
                    }

                    Logger.Info($"NightSummary: TS GetAllProjects returned {projects.Count} projects / {targets.Count} targets for profile '{profileId ?? "(all)"}'");
                    return projects.Values.ToList();
                }
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to read TS project tree. {ex.Message}");
                return new List<TsProjectInfo>();
            }
        }

        private static string ProjectStateName(int v) {
            switch (v) {
                case 0: return "Draft";
                case 1: return "Active";
                case 2: return "Inactive";
                case 3: return "Closed";
                default: return "Unknown";
            }
        }

        private static string ProjectPriorityName(int v) {
            switch (v) {
                case 0: return "Low";
                case 1: return "Normal";
                case 2: return "High";
                default: return "Unknown";
            }
        }

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

        private List<TsTargetData> QueryProgress(SQLiteConnection conn, HashSet<string> nameSet, string profileId = null) {
            var sql = @"
                SELECT
                    t.name        AS TargetName,
                    p.name        AS ProjectName,
                    t.ra          AS RA,
                    t.dec         AS Dec,
                    t.rotation    AS Rotation,
                    p.MinimumAltitude AS MinimumAltitude,
                    et.name       AS TemplateName,
                    et.filtername AS Filter,
                    CASE WHEN ep.exposure > 0 THEN ep.exposure ELSE et.defaultexposure END AS ExposureSec,
                    ep.desired    AS Desired,
                    ep.acquired   AS Acquired,
                    ep.accepted   AS Accepted
                FROM exposureplan ep
                JOIN target t           ON t.Id  = ep.targetid
                JOIN project p          ON p.Id  = t.ProjectId
                JOIN exposuretemplate et ON et.Id = ep.exposureTemplateId
                WHERE ep.desired > 0" +
                (profileId != null ? " AND p.ProfileId = @ProfileId" : "") +
                " ORDER BY p.name, t.name, et.filtername, et.name";

            var rows = new List<(string Name, string ProjectName, double RA, double Dec, double Rotation, double MinimumAltitude, string TemplateName, string Filter, double ExposureSec, int Desired, int Acquired, int Accepted)>();

            using (var cmd = new SQLiteCommand(sql, conn)) {
                if (profileId != null) cmd.Parameters.AddWithValue("@ProfileId", profileId);
                using (var reader = cmd.ExecuteReader()) {
                    while (reader.Read()) {
                        var name = reader["TargetName"].ToString();
                        if (!nameSet.Contains(name)) continue;

                        rows.Add((
                            Name:            name,
                            ProjectName:     reader["ProjectName"].ToString() ?? "",
                            RA:              Convert.ToDouble(reader["RA"]),
                            Dec:             Convert.ToDouble(reader["Dec"]),
                            Rotation:        reader["Rotation"]        == DBNull.Value ? 0 : Convert.ToDouble(reader["Rotation"]),
                            MinimumAltitude: reader["MinimumAltitude"] == DBNull.Value ? 0 : Convert.ToDouble(reader["MinimumAltitude"]),
                            TemplateName:    reader["TemplateName"].ToString() ?? "",
                            Filter:          reader["Filter"].ToString() ?? "",
                            ExposureSec:     reader["ExposureSec"] == DBNull.Value ? 0 : Convert.ToDouble(reader["ExposureSec"]),
                            Desired:         Convert.ToInt32(reader["Desired"]),
                            Acquired:        Convert.ToInt32(reader["Acquired"]),
                            Accepted:        Convert.ToInt32(reader["Accepted"])
                        ));
                    }
                }
            }

            // Group by (project, target) — same target name in different projects = separate progress sections
            return rows
                .GroupBy(r => (r.ProjectName, r.Name))
                .Select(g => new TsTargetData {
                    TargetName      = g.Key.Name,
                    ProjectName     = g.Key.ProjectName,
                    RA              = g.First().RA,
                    Dec             = g.First().Dec,
                    Rotation        = g.First().Rotation,
                    MinimumAltitude = g.First().MinimumAltitude,
                    Filters    = g.Select(r => new TsFilterProgress {
                                       TemplateName = r.TemplateName,
                                       Filter       = r.Filter,
                                       ExposureSec  = r.ExposureSec,
                                       Desired      = r.Desired,
                                       Acquired     = r.Acquired,
                                       Accepted     = r.Accepted
                                   }).ToList()
                })
                .ToList();
        }

        // Single SQL query against TS for one (target, filter, ±window-around-center).
        // Centralized so GetImageAugment can call it twice (direct + legacy-shifted)
        // without duplicating the SELECT/JOIN block.
        private static TsImageAugment QueryAugment(SQLiteConnection conn, string targetName, string filterName, long center, int windowSeconds) {
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

            // TS stores per-image metadata as serialized JSON (HFRStDev, per-axis guiding,
            // ADU stats, etc). NS doesn't capture HFRStDev or RA/Dec guiding split natively
            // so we lift just those fields out — everything else NS already has on the row.
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
                } catch { /* malformed JSON — fields stay null */ }
            }
            return aug;
        }

        // Pull a numeric field from the TS metadata JSON object. Returns null when the
        // property is missing, null, or not a number — TS sometimes serializes 0.0 as "0".
        private static double? TryDouble(System.Text.Json.JsonElement root, string name) {
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (!root.TryGetProperty(name, out var prop)) return null;
            if (prop.ValueKind == System.Text.Json.JsonValueKind.Number && prop.TryGetDouble(out var d)) return d;
            if (prop.ValueKind == System.Text.Json.JsonValueKind.String
                && double.TryParse(prop.GetString(), System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out var ds)) return ds;
            return null;
        }
    }
}
