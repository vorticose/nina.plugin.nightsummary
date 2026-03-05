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
                        Accepted INTEGER DEFAULT 1
                    )";

                using (var cmd = new SQLiteCommand(createSessions, conn))
                    cmd.ExecuteNonQuery();

                using (var cmd = new SQLiteCommand(createImages, conn))
                    cmd.ExecuteNonQuery();

                // Migrate existing databases that predate FWHM/Eccentricity columns
                MigrateAddColumn(conn, "Images", "FWHM", "REAL DEFAULT 0");
                MigrateAddColumn(conn, "Images", "Eccentricity", "REAL DEFAULT 0");
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
                        HFR, FWHM, Eccentricity, StarCount, GuidingRMSTotal, GuidingScale, Accepted)
                    VALUES (
                        @SessionId, @Timestamp, @TargetName, @Filter, @ExposureDuration,
                        @HFR, @FWHM, @Eccentricity, @StarCount, @GuidingRMSTotal, @GuidingScale, @Accepted)";

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
                                Accepted = reader["Accepted"] == DBNull.Value ? false : Convert.ToInt32(reader["Accepted"]) == 1
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
    }
}