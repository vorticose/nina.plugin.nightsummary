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

        public bool IsAvailable => File.Exists(dbPath);

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
                                return (enabled, port);
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to read TS API settings. {ex.Message}");
            }
            return (false, 0);
        }

        private List<TsTargetData> QueryProgress(SQLiteConnection conn, HashSet<string> nameSet, string profileId = null) {
            var sql = @"
                SELECT
                    t.name        AS TargetName,
                    t.ra          AS RA,
                    t.dec         AS Dec,
                    t.rotation    AS Rotation,
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
                " ORDER BY t.name, et.filtername, et.name";

            var rows = new List<(string Name, double RA, double Dec, double Rotation, string TemplateName, string Filter, double ExposureSec, int Desired, int Acquired, int Accepted)>();

            using (var cmd = new SQLiteCommand(sql, conn)) {
                if (profileId != null) cmd.Parameters.AddWithValue("@ProfileId", profileId);
                using (var reader = cmd.ExecuteReader()) {
                    while (reader.Read()) {
                        var name = reader["TargetName"].ToString();
                        if (!nameSet.Contains(name)) continue;

                        rows.Add((
                            Name:         name,
                            RA:           Convert.ToDouble(reader["RA"]),
                            Dec:          Convert.ToDouble(reader["Dec"]),
                            Rotation:     reader["Rotation"]   == DBNull.Value ? 0 : Convert.ToDouble(reader["Rotation"]),
                            TemplateName: reader["TemplateName"].ToString() ?? "",
                            Filter:       reader["Filter"].ToString() ?? "",
                            ExposureSec:  reader["ExposureSec"] == DBNull.Value ? 0 : Convert.ToDouble(reader["ExposureSec"]),
                            Desired:      Convert.ToInt32(reader["Desired"]),
                            Acquired:     Convert.ToInt32(reader["Acquired"]),
                            Accepted:     Convert.ToInt32(reader["Accepted"])
                        ));
                    }
                }
            }

            // Group by target only — each exposure plan row is its own bar (one per template+filter)
            return rows
                .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new TsTargetData {
                    TargetName = g.Key,
                    RA         = g.First().RA,
                    Dec        = g.First().Dec,
                    Rotation   = g.First().Rotation,
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
    }
}
