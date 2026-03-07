using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace NINA.Plugin.NightSummary.Data {
    /// <summary>
    /// Handles all SQLite database operations for Night Summary.
    /// Creates and manages the database file, and provides methods
    /// for reading and writing SessionRecords and ImageRecords.
    /// </summary>
    public class SessionDatabase {

        private readonly string dbPath;
        private readonly string connectionString;

        public SessionDatabase() {
            string pluginDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA", "Plugins", CoreUtil.Version, "NightSummary");
            Directory.CreateDirectory(pluginDataPath);
            dbPath = Path.Combine(pluginDataPath, "nightsummary.sqlite");
            connectionString = $"Data Source={dbPath};Version=3;";
            InitializeDatabase();
        }

        public SessionDatabase(string customDbPath) {
            dbPath = customDbPath;
            connectionString = $"Data Source={dbPath};Version=3;";
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath));
            InitializeDatabase();
        }

        /// <summary>
        /// Creates the database tables if they don't already exist.
        /// Safe to call every time - uses CREATE TABLE IF NOT EXISTS.
        /// </summary>
        private void InitializeDatabase() {
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();

                string createSessions = @"
                    CREATE TABLE IF NOT EXISTS Sessions (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SessionId TEXT NOT NULL,
                        SessionStart TEXT NOT NULL,
                        SessionEnd TEXT,
                        ProfileName TEXT,
                        Notes TEXT,
                        ReportSent INTEGER DEFAULT 0
                    )";

                string createImages = @"
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
                        DecDegrees REAL DEFAULT 0
                    )";

                using (var cmd = new SQLiteCommand(createSessions, conn))
                    cmd.ExecuteNonQuery();

                using (var cmd = new SQLiteCommand(createImages, conn))
                    cmd.ExecuteNonQuery();

                string createEvents = @"
                    CREATE TABLE IF NOT EXISTS SessionEvents (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SessionId TEXT NOT NULL,
                        Timestamp TEXT NOT NULL,
                        EventType TEXT NOT NULL,
                        Description TEXT
                    )";

                using (var cmd = new SQLiteCommand(createEvents, conn))
                    cmd.ExecuteNonQuery();

                // Migrate existing databases that predate added columns
                MigrateAddColumn(conn, "Images", "FWHM",       "REAL DEFAULT 0");
                MigrateAddColumn(conn, "Images", "Eccentricity","REAL DEFAULT 0");
                MigrateAddColumn(conn, "Images", "RaHours",    "REAL DEFAULT 0");
                MigrateAddColumn(conn, "Images", "DecDegrees", "REAL DEFAULT 0");
            }
        }

        /// <summary>
        /// Adds a column to an existing table if it doesn't already exist.
        /// SQLite does not support ALTER TABLE ADD COLUMN IF NOT EXISTS,
        /// so we attempt the ALTER and swallow the error if the column is already there.
        /// </summary>
        private void MigrateAddColumn(SQLiteConnection conn, string table, string column, string definition) {
            try {
                using (var cmd = new SQLiteCommand($"ALTER TABLE {table} ADD COLUMN {column} {definition}", conn))
                    cmd.ExecuteNonQuery();
            } catch {
                // Column already exists — nothing to do
            }
        }

        /// <summary>
        /// Saves a new session record and returns it with its Id populated.
        /// Call this when the sequence starts.
        /// </summary>
        public SessionRecord CreateSession(SessionRecord session) {
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                string sql = @"
                    INSERT INTO Sessions (SessionId, SessionStart, ProfileName, Notes, ReportSent)
                    VALUES (@SessionId, @SessionStart, @ProfileName, @Notes, @ReportSent);
                    SELECT last_insert_rowid();";

                using (var cmd = new SQLiteCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@SessionId", session.SessionId);
                    cmd.Parameters.AddWithValue("@SessionStart", session.SessionStart.ToString("o"));
                    cmd.Parameters.AddWithValue("@ProfileName", session.ProfileName ?? "");
                    cmd.Parameters.AddWithValue("@Notes", session.Notes ?? "");
                    cmd.Parameters.AddWithValue("@ReportSent", session.ReportSent ? 1 : 0);
                    session.Id = Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
            return session;
        }

        /// <summary>
        /// Updates the session end time and report sent status.
        /// Call this when the sequence ends.
        /// </summary>
        public void FinalizeSession(string sessionId, DateTime endTime, bool reportSent) {
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                string sql = @"
                    UPDATE Sessions 
                    SET SessionEnd = @SessionEnd, ReportSent = @ReportSent
                    WHERE SessionId = @SessionId";

                using (var cmd = new SQLiteCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@SessionEnd", endTime.ToString("o"));
                    cmd.Parameters.AddWithValue("@ReportSent", reportSent ? 1 : 0);
                    cmd.Parameters.AddWithValue("@SessionId", sessionId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Saves a single image record to the database.
        /// Call this each time an image is captured during the session.
        /// GuidingRMSTotal is stored in arcseconds (pixels * GuidingScale).
        /// </summary>
        public void SaveImageRecord(ImageRecord image) {
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                string sql = @"
                    INSERT INTO Images (
                        SessionId, Timestamp, TargetName, Filter, ExposureDuration,
                        HFR, FWHM, Eccentricity, StarCount, GuidingRMSTotal, GuidingScale, Accepted,
                        RaHours, DecDegrees)
                    VALUES (
                        @SessionId, @Timestamp, @TargetName, @Filter, @ExposureDuration,
                        @HFR, @FWHM, @Eccentricity, @StarCount, @GuidingRMSTotal, @GuidingScale, @Accepted,
                        @RaHours, @DecDegrees)";

                using (var cmd = new SQLiteCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@SessionId", image.SessionId);
                    cmd.Parameters.AddWithValue("@Timestamp", image.Timestamp.ToString("o"));
                    cmd.Parameters.AddWithValue("@TargetName", image.TargetName ?? "");
                    cmd.Parameters.AddWithValue("@Filter", image.Filter ?? "");
                    cmd.Parameters.AddWithValue("@ExposureDuration", image.ExposureDuration);
                    cmd.Parameters.AddWithValue("@HFR", image.HFR);
                    cmd.Parameters.AddWithValue("@FWHM", image.FWHM);
                    cmd.Parameters.AddWithValue("@Eccentricity", image.Eccentricity);
                    cmd.Parameters.AddWithValue("@StarCount", image.StarCount);
                    cmd.Parameters.AddWithValue("@GuidingRMSTotal", image.GuidingRMSTotal);
                    cmd.Parameters.AddWithValue("@GuidingScale", image.GuidingScale);
                    cmd.Parameters.AddWithValue("@Accepted", image.Accepted ? 1 : 0);
                    cmd.Parameters.AddWithValue("@RaHours",    image.RaHours);
                    cmd.Parameters.AddWithValue("@DecDegrees", image.DecDegrees);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Retrieves all image records for a given session.
        /// </summary>
        public List<ImageRecord> GetImagesForSession(string sessionId) {
            var images = new List<ImageRecord>();
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                string sql = "SELECT * FROM Images WHERE SessionId = @SessionId ORDER BY Timestamp";
                using (var cmd = new SQLiteCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@SessionId", sessionId);
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            images.Add(new ImageRecord {
                                Id = Convert.ToInt32(reader["Id"]),
                                SessionId = reader["SessionId"] == DBNull.Value ? "" : reader["SessionId"].ToString(),
                                Timestamp = reader["Timestamp"] == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["Timestamp"].ToString()),
                                TargetName = reader["TargetName"] == DBNull.Value ? "" : reader["TargetName"].ToString(),
                                Filter = reader["Filter"] == DBNull.Value ? "" : reader["Filter"].ToString(),
                                ExposureDuration = reader["ExposureDuration"] == DBNull.Value ? 0 : Convert.ToDouble(reader["ExposureDuration"]),
                                HFR = reader["HFR"] == DBNull.Value ? 0 : Convert.ToDouble(reader["HFR"]),
                                FWHM = reader["FWHM"] == DBNull.Value ? 0 : Convert.ToDouble(reader["FWHM"]),
                                Eccentricity = reader["Eccentricity"] == DBNull.Value ? 0 : Convert.ToDouble(reader["Eccentricity"]),
                                StarCount = reader["StarCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["StarCount"]),
                                GuidingRMSTotal = reader["GuidingRMSTotal"] == DBNull.Value ? 0 : Convert.ToDouble(reader["GuidingRMSTotal"]),
                                GuidingScale = reader["GuidingScale"] == DBNull.Value ? 1 : Convert.ToDouble(reader["GuidingScale"]),
                                Accepted = reader["Accepted"] == DBNull.Value ? false : Convert.ToInt32(reader["Accepted"]) == 1,
                                RaHours    = reader["RaHours"]    == DBNull.Value ? 0 : Convert.ToDouble(reader["RaHours"]),
                                DecDegrees = reader["DecDegrees"] == DBNull.Value ? 0 : Convert.ToDouble(reader["DecDegrees"])
                            });
                        }
                    }
                }
            }
            return images;
        }

        /// <summary>
        /// Retrieves the session record for a given sessionId.
        /// </summary>
        public SessionRecord GetSession(string sessionId) {
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                string sql = "SELECT * FROM Sessions WHERE SessionId = @SessionId";
                using (var cmd = new SQLiteCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@SessionId", sessionId);
                    using (var reader = cmd.ExecuteReader()) {
                        if (reader.Read()) {
                            try {
                                return new SessionRecord {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    SessionId = reader["SessionId"] == DBNull.Value ? "" : reader["SessionId"].ToString(),
                                    SessionStart = reader["SessionStart"] == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["SessionStart"].ToString()),
                                    SessionEnd = reader["SessionEnd"] == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["SessionEnd"].ToString()),
                                    ProfileName = reader["ProfileName"] == DBNull.Value ? "" : reader["ProfileName"].ToString(),
                                    Notes = reader["Notes"] == DBNull.Value ? "" : reader["Notes"].ToString(),
                                    ReportSent = reader["ReportSent"] == DBNull.Value ? false : Convert.ToInt32(reader["ReportSent"]) == 1
                                };
                            } catch (Exception ex) {
                                Logger.Error($"NightSummary: Error reading session record field: {ex.Message}");
                                throw;
                            }
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Saves a session event (autofocus run, safety monitor change, meridian flip, etc.).
        /// </summary>
        public void SaveEvent(SessionEvent evt) {
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                string sql = @"
                    INSERT INTO SessionEvents (SessionId, Timestamp, EventType, Description)
                    VALUES (@SessionId, @Timestamp, @EventType, @Description)";

                using (var cmd = new SQLiteCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@SessionId", evt.SessionId);
                    cmd.Parameters.AddWithValue("@Timestamp", evt.Timestamp.ToString("o"));
                    cmd.Parameters.AddWithValue("@EventType", evt.EventType ?? "");
                    cmd.Parameters.AddWithValue("@Description", evt.Description ?? "");
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Retrieves all session events for a given session, ordered by timestamp.
        /// </summary>
        public List<SessionEvent> GetEventsForSession(string sessionId) {
            var events = new List<SessionEvent>();
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                string sql = "SELECT * FROM SessionEvents WHERE SessionId = @SessionId ORDER BY Timestamp";
                using (var cmd = new SQLiteCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@SessionId", sessionId);
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            events.Add(new SessionEvent {
                                Id = Convert.ToInt32(reader["Id"]),
                                SessionId = reader["SessionId"] == DBNull.Value ? "" : reader["SessionId"].ToString(),
                                Timestamp = reader["Timestamp"] == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["Timestamp"].ToString()),
                                EventType = reader["EventType"] == DBNull.Value ? "" : reader["EventType"].ToString(),
                                Description = reader["Description"] == DBNull.Value ? "" : reader["Description"].ToString()
                            });
                        }
                    }
                }
            }
            return events;
        }

        /// <summary>
        /// Returns total accepted exposure seconds per target name across all sessions
        /// except the one identified by excludeSessionId.
        /// </summary>
        public Dictionary<string, double> GetCumulativeIntegrationByTarget(string excludeSessionId) {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                string sql = @"
                    SELECT TargetName, SUM(ExposureDuration) AS TotalSeconds
                    FROM Images
                    WHERE Accepted = 1 AND SessionId != @SessionId
                    GROUP BY TargetName";

                using (var cmd = new SQLiteCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@SessionId", excludeSessionId ?? "");
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            var name  = reader["TargetName"] == DBNull.Value ? "" : reader["TargetName"].ToString();
                            var total = reader["TotalSeconds"] == DBNull.Value ? 0 : Convert.ToDouble(reader["TotalSeconds"]);
                            if (!string.IsNullOrEmpty(name))
                                result[name] = total;
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Returns per-session aggregate stats for a target across all sessions except the current one.
        /// Ordered most-recent-first, limited to <paramref name="limit"/> rows.
        /// </summary>
        public List<TargetSessionHistory> GetSessionHistoryForTarget(string targetName, string excludeSessionId, int limit = 5) {
            var result = new List<TargetSessionHistory>();
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                string sql = @"
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
                    ORDER BY s.SessionStart DESC
                    LIMIT @Limit";

                using (var cmd = new SQLiteCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@TargetName",       targetName       ?? "");
                    cmd.Parameters.AddWithValue("@ExcludeSessionId", excludeSessionId ?? "");
                    cmd.Parameters.AddWithValue("@Limit",            limit);
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            result.Add(new TargetSessionHistory {
                                SessionStart       = reader["SessionStart"]       == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["SessionStart"].ToString()),
                                IntegrationSeconds = reader["IntegrationSeconds"] == DBNull.Value ? 0 : Convert.ToDouble(reader["IntegrationSeconds"]),
                                AvgHFR             = reader["AvgHFR"]             == DBNull.Value ? 0 : Convert.ToDouble(reader["AvgHFR"]),
                                AvgFWHM            = reader["AvgFWHM"]            == DBNull.Value ? 0 : Convert.ToDouble(reader["AvgFWHM"]),
                                AvgGuidingRMS      = reader["AvgGuidingRMS"]      == DBNull.Value ? 0 : Convert.ToDouble(reader["AvgGuidingRMS"])
                            });
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Returns the most recent session by SessionStart, or null if no sessions exist.
        /// </summary>
        public SessionRecord GetLatestSession() {
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                string sql = "SELECT * FROM Sessions ORDER BY SessionStart DESC LIMIT 1";
                using (var cmd = new SQLiteCommand(sql, conn)) {
                    using (var reader = cmd.ExecuteReader()) {
                        if (reader.Read()) {
                            try {
                                return new SessionRecord {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    SessionId = reader["SessionId"] == DBNull.Value ? "" : reader["SessionId"].ToString(),
                                    SessionStart = reader["SessionStart"] == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["SessionStart"].ToString()),
                                    SessionEnd = reader["SessionEnd"] == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["SessionEnd"].ToString()),
                                    ProfileName = reader["ProfileName"] == DBNull.Value ? "" : reader["ProfileName"].ToString(),
                                    Notes = reader["Notes"] == DBNull.Value ? "" : reader["Notes"].ToString(),
                                    ReportSent = reader["ReportSent"] == DBNull.Value ? false : Convert.ToInt32(reader["ReportSent"]) == 1
                                };
                            } catch (Exception ex) {
                                Logger.Error($"NightSummary: Error reading latest session record: {ex.Message}");
                                throw;
                            }
                        }
                    }
                }
            }
            return null;
        }
    }
}