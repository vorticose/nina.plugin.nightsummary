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
        public List<TsTargetData> GetProgressForTargets(IEnumerable<string> sessionTargetNames) {
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
                    return QueryProgress(conn, nameSet);
                }
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to read Target Scheduler database. {ex.Message}");
                return new List<TsTargetData>();
            }
        }

        private List<TsTargetData> QueryProgress(SQLiteConnection conn, HashSet<string> nameSet) {
            const string sql = @"
                SELECT
                    t.name        AS TargetName,
                    t.ra          AS RA,
                    t.dec         AS Dec,
                    t.rotation    AS Rotation,
                    et.filtername AS Filter,
                    ep.desired    AS Desired,
                    ep.acquired   AS Acquired,
                    ep.accepted   AS Accepted
                FROM exposureplan ep
                JOIN target t           ON t.Id  = ep.targetid
                JOIN exposuretemplate et ON et.Id = ep.exposureTemplateId
                WHERE ep.desired > 0
                ORDER BY t.name, et.filtername";

            var rows = new List<(string Name, double RA, double Dec, double Rotation, string Filter, int Desired, int Acquired, int Accepted)>();

            using (var cmd = new SQLiteCommand(sql, conn))
            using (var reader = cmd.ExecuteReader()) {
                while (reader.Read()) {
                    var name = reader["TargetName"].ToString();
                    if (!nameSet.Contains(name)) continue;

                    rows.Add((
                        Name:     name,
                        RA:       Convert.ToDouble(reader["RA"]),
                        Dec:      Convert.ToDouble(reader["Dec"]),
                        Rotation: reader["Rotation"] == DBNull.Value ? 0 : Convert.ToDouble(reader["Rotation"]),
                        Filter:   reader["Filter"].ToString() ?? "",
                        Desired:  Convert.ToInt32(reader["Desired"]),
                        Acquired: Convert.ToInt32(reader["Acquired"]),
                        Accepted: Convert.ToInt32(reader["Accepted"])
                    ));
                }
            }

            // Group into TsTargetData objects
            return rows
                .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new TsTargetData {
                    TargetName = g.Key,
                    RA         = g.First().RA,
                    Dec        = g.First().Dec,
                    Rotation   = g.First().Rotation,
                    Filters    = g.GroupBy(r => r.Filter, StringComparer.OrdinalIgnoreCase)
                                   .Select(fg => new TsFilterProgress {
                                       Filter   = fg.Key,
                                       Desired  = fg.Sum(r => r.Desired),
                                       Acquired = fg.Sum(r => r.Acquired),
                                       Accepted = fg.Sum(r => r.Accepted)
                                   }).ToList()
                })
                .ToList();
        }
    }
}
