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
            // Store the database in the same location NINA uses for plugin data
            string pluginDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA", "Plugins", "NightSummary");

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
                        FWHM REAL,
                        Eccentricity REAL,
                        StarCount INTEGER,
                        GuidingRMSTotal REAL,
                        GuidingRMSRA REAL,
                        GuidingRMSDec REAL,
                        FocuserPosition INTEGER,
                        CameraTemperature REAL,
                        Accepted INTEGER DEFAULT 1
                    )";

                using (var cmd = new SQLiteCommand(createSessions, conn))
                    cmd.ExecuteNonQuery();

                using (var cmd = new SQLiteCommand(createImages, conn))
                    cmd.ExecuteNonQuery();
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
        /// </summary>
        public void SaveImageRecord(ImageRecord image) {
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                string sql = @"
                    INSERT INTO Images (
                        SessionId, Timestamp, TargetName, Filter, ExposureDuration,
                        HFR, FWHM, Eccentricity, StarCount,
                        GuidingRMSTotal, GuidingRMSRA, GuidingRMSDec,
                        FocuserPosition, CameraTemperature, Accepted)
                    VALUES (
                        @SessionId, @Timestamp, @TargetName, @Filter, @ExposureDuration,
                        @HFR, @FWHM, @Eccentricity, @StarCount,
                        @GuidingRMSTotal, @GuidingRMSRA, @GuidingRMSDec,
                        @FocuserPosition, @CameraTemperature, @Accepted)";

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
                    cmd.Parameters.AddWithValue("@GuidingRMSRA", image.GuidingRMSRA);
                    cmd.Parameters.AddWithValue("@GuidingRMSDec", image.GuidingRMSDec);
                    cmd.Parameters.AddWithValue("@FocuserPosition", image.FocuserPosition);
                    cmd.Parameters.AddWithValue("@CameraTemperature", image.CameraTemperature);
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
                                SessionId = reader["SessionId"].ToString(),
                                Timestamp = DateTime.Parse(reader["Timestamp"].ToString()),
                                TargetName = reader["TargetName"].ToString(),
                                Filter = reader["Filter"].ToString(),
                                ExposureDuration = Convert.ToDouble(reader["ExposureDuration"]),
                                HFR = Convert.ToDouble(reader["HFR"]),
                                FWHM = Convert.ToDouble(reader["FWHM"]),
                                Eccentricity = Convert.ToDouble(reader["Eccentricity"]),
                                StarCount = Convert.ToInt32(reader["StarCount"]),
                                GuidingRMSTotal = Convert.ToDouble(reader["GuidingRMSTotal"]),
                                GuidingRMSRA = Convert.ToDouble(reader["GuidingRMSRA"]),
                                GuidingRMSDec = Convert.ToDouble(reader["GuidingRMSDec"]),
                                FocuserPosition = Convert.ToInt32(reader["FocuserPosition"]),
                                CameraTemperature = Convert.ToDouble(reader["CameraTemperature"]),
                                Accepted = Convert.ToInt32(reader["Accepted"]) == 1
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
                            return new SessionRecord {
                                Id = Convert.ToInt32(reader["Id"]),
                                SessionId = reader["SessionId"].ToString(),
                                SessionStart = DateTime.Parse(reader["SessionStart"].ToString()),
                                SessionEnd = reader["SessionEnd"] == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["SessionEnd"].ToString()),
                                ProfileName = reader["ProfileName"].ToString(),
                                Notes = reader["Notes"].ToString(),
                                ReportSent = Convert.ToInt32(reader["ReportSent"]) == 1
                            };
                        }
                    }
                }
            }
            return null;
        }
    }
}