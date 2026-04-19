using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;

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
                "NINA", "NightSummary");
            Directory.CreateDirectory(pluginDataPath);
            dbPath = Path.Combine(pluginDataPath, "nightsummary.sqlite");

            // Migrate from legacy version-specific location if needed
            if (!File.Exists(dbPath)) {
                MigrateLegacyDatabase(pluginDataPath);
            }

            Logger.Info($"NightSummary: Database path: {dbPath}");
            Logger.Info($"NightSummary: Database exists: {File.Exists(dbPath)}");

            connectionString = $"Data Source={dbPath};Version=3;";
            SeedTestDatabaseIfMissing(pluginDataPath);
            InitializeDatabase();
        }

        /// <summary>
        /// Scans legacy version-specific plugin folders for existing databases,
        /// copies the most recent verified one as the base using an atomic temp-then-rename
        /// strategy so dbPath is never left in a partial state, then merges any unique sessions
        /// from older databases. Never deletes or moves source files — all originals are preserved.
        /// </summary>
        private void MigrateLegacyDatabase(string newDataPath) {
            var tempPath = dbPath + ".migration_tmp";
            try {
                Logger.Info("NightSummary: No database at new location, scanning for legacy databases...");

                var pluginsRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NINA", "Plugins");

                if (!Directory.Exists(pluginsRoot)) {
                    Logger.Info($"NightSummary: Plugins directory not found at {pluginsRoot}, skipping migration");
                    return;
                }

                var candidates = new List<(string path, DateTime modified)>();

                foreach (var versionDir in Directory.GetDirectories(pluginsRoot)) {
                    var legacyDb = Path.Combine(versionDir, "NightSummary", "nightsummary.sqlite");
                    Logger.Info($"NightSummary: Checking legacy path: {legacyDb}");
                    if (File.Exists(legacyDb)) {
                        var modified = File.GetLastWriteTimeUtc(legacyDb);
                        Logger.Info($"NightSummary: Found legacy database at {legacyDb} (modified: {modified:yyyy-MM-dd HH:mm:ss} UTC)");
                        candidates.Add((legacyDb, modified));
                    }
                }

                if (candidates.Count == 0) {
                    Logger.Info("NightSummary: No legacy databases found, starting fresh");
                    return;
                }

                // Filter out corrupt candidates before selecting the base
                var sorted = candidates
                    .OrderByDescending(c => c.modified)
                    .Where(c => VerifySQLiteIntegrity(c.path))
                    .ToList();

                if (sorted.Count == 0) {
                    Logger.Error("NightSummary: All legacy databases failed integrity check. Skipping migration — starting fresh.");
                    return;
                }

                var best = sorted.First();
                Logger.Info($"NightSummary: Selected primary legacy database: {best.path} (modified {best.modified:yyyy-MM-dd HH:mm:ss} UTC)");

                // Atomic copy: write to a temp file, verify it, then rename into place.
                // This ensures dbPath is never partially written — it either doesn't exist, or is complete and verified.
                Logger.Info($"NightSummary: Copying to temp file {tempPath}");
                if (File.Exists(tempPath)) File.Delete(tempPath);
                File.Copy(best.path, tempPath);

                if (!VerifySQLiteIntegrity(tempPath)) {
                    Logger.Error("NightSummary: Copied database failed integrity check. Aborting migration.");
                    try { File.Delete(tempPath); } catch { }
                    return;
                }

                File.Move(tempPath, dbPath);
                Logger.Info($"NightSummary: Primary database installed at {dbPath}. Original preserved at {best.path}");

                // Merge sessions from any additional valid legacy databases
                if (sorted.Count > 1) {
                    var toMerge = sorted.Skip(1).Select(c => c.path).ToList();
                    Logger.Info($"NightSummary: Found {toMerge.Count} additional legacy database(s) to merge");
                    MergeOlderDatabases(toMerge);
                }

                // Migrate test database — scan all version folders, not just the primary's sibling
                var newTestDb = Path.Combine(newDataPath, "test", "nightsummary.sqlite");
                if (!File.Exists(newTestDb)) {
                    foreach (var (candidatePath, _) in sorted) {
                        var legacyTestDb = Path.Combine(Path.GetDirectoryName(candidatePath), "test", "nightsummary.sqlite");
                        if (File.Exists(legacyTestDb)) {
                            Directory.CreateDirectory(Path.GetDirectoryName(newTestDb));
                            File.Copy(legacyTestDb, newTestDb);
                            Logger.Info($"NightSummary: Test database migrated from {legacyTestDb} to {newTestDb}");
                            break;
                        }
                    }
                }
            } catch (Exception ex) {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                Logger.Error($"NightSummary: Legacy database migration failed: {ex.Message}. No data was lost — original files are untouched.");
            }
        }

        /// <summary>
        /// Merges unique sessions from older legacy databases into the new database.
        /// Skips sessions whose SessionId already exists. Never modifies source databases.
        /// Writes a state file after each successful DB merge so interrupted runs can resume.
        /// Creates a pre-merge backup that is kept until the next version cycle.
        /// </summary>
        private void MergeOlderDatabases(List<string> olderDbPaths) {
            // Create a pre-merge backup before touching the destination
            var backupPath = dbPath + ".pre_merge_backup";
            try {
                File.Copy(dbPath, backupPath, overwrite: true);
                Logger.Info($"NightSummary: Pre-merge backup created at {backupPath}");
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: Could not create pre-merge backup: {ex.Message}. Proceeding without backup.");
            }

            // Load merge state file so interrupted runs can skip already-completed databases
            var mergeLogPath = dbPath + ".merge_state";
            var alreadyMerged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(mergeLogPath)) {
                try {
                    foreach (var line in File.ReadAllLines(mergeLogPath))
                        if (!string.IsNullOrWhiteSpace(line)) alreadyMerged.Add(line.Trim());
                    Logger.Info($"NightSummary: Resuming partial migration — {alreadyMerged.Count} database(s) already merged");
                } catch { /* non-fatal — will just re-attempt previously merged databases */ }
            }

            // Collect existing SessionIds once upfront to avoid redundant queries
            var existingSessionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var newConnStr = $"Data Source={dbPath};Version=3;";
            using (var conn = new SQLiteConnection(newConnStr)) {
                conn.Open();
                using (var cmd = new SQLiteCommand("SELECT SessionId FROM Sessions", conn))
                using (var reader = cmd.ExecuteReader()) {
                    while (reader.Read())
                        existingSessionIds.Add(reader.GetString(0));
                }
            }
            Logger.Info($"NightSummary: Destination database has {existingSessionIds.Count} existing session(s)");

            int totalMergedSessions = 0, totalMergedImages = 0, totalMergedEvents = 0;

            foreach (var olderDbPath in olderDbPaths) {
                if (alreadyMerged.Contains(olderDbPath)) {
                    Logger.Info($"NightSummary: Skipping {olderDbPath} — already merged in a previous run");
                    continue;
                }

                try {
                    Logger.Info($"NightSummary: Merging from {olderDbPath}...");

                    if (!VerifySQLiteIntegrity(olderDbPath)) {
                        Logger.Warning($"NightSummary: Skipping {olderDbPath} — failed integrity check");
                        continue;
                    }

                    var olderConnStr = $"Data Source={olderDbPath};Version=3;Read Only=True;";
                    int mergedSessions = 0, mergedImages = 0, mergedEvents = 0, skippedSessions = 0;

                    using (var src = new SQLiteConnection(olderConnStr))
                    using (var dst = new SQLiteConnection(newConnStr)) {
                        src.Open();
                        dst.Open();

                        if (!TableExists(src, "Sessions")) {
                            Logger.Warning($"NightSummary: Source has no Sessions table: {olderDbPath}. Skipping.");
                            continue;
                        }

                        // Compute column intersections once per source DB, not once per session
                        var sessionColumns = GetCommonColumns(src, dst, "Sessions");
                        var imageColumns   = TableExists(src, "Images")        ? GetCommonColumns(src, dst, "Images")        : null;
                        var eventColumns   = TableExists(src, "SessionEvents") ? GetCommonColumns(src, dst, "SessionEvents") : null;

                        if (sessionColumns.Count == 0) {
                            Logger.Warning($"NightSummary: No common Sessions columns with {olderDbPath}. Skipping.");
                            continue;
                        }

                        // Read all sessions using only columns present in both schemas.
                        // Fixes latent bug: old hardcoded SELECT missed CamXSize, CamYSize,
                        // PixelSizeMicrons, FocalLengthMm, and SkippedExposures.
                        var sessions = new List<Dictionary<string, object>>();
                        var colList = string.Join(", ", sessionColumns);
                        using (var cmd = new SQLiteCommand($"SELECT {colList} FROM Sessions", src))
                        using (var reader = cmd.ExecuteReader()) {
                            while (reader.Read()) {
                                var row = new Dictionary<string, object>();
                                foreach (var col in sessionColumns)
                                    row[col] = reader[col] == DBNull.Value ? null : reader[col];
                                sessions.Add(row);
                            }
                        }

                        foreach (var session in sessions) {
                            if (!session.TryGetValue("SessionId", out var sidObj) || sidObj == null ||
                                string.IsNullOrWhiteSpace(sidObj.ToString())) {
                                Logger.Warning($"NightSummary: Skipping session with null/empty SessionId in {olderDbPath}");
                                skippedSessions++;
                                continue;
                            }

                            var sessionId = sidObj.ToString();
                            if (existingSessionIds.Contains(sessionId)) {
                                skippedSessions++;
                                continue;
                            }

                            using (var tx = dst.BeginTransaction()) {
                                try {
                                    var cols  = string.Join(", ", sessionColumns);
                                    var parms = string.Join(", ", sessionColumns.Select(c => $"@{c}"));
                                    using (var cmd = new SQLiteCommand($"INSERT INTO Sessions ({cols}) VALUES ({parms})", dst)) {
                                        foreach (var col in sessionColumns)
                                            cmd.Parameters.AddWithValue($"@{col}", (object)session[col] ?? DBNull.Value);
                                        cmd.ExecuteNonQuery();
                                    }

                                    int imageCount = imageColumns != null ? CopyTableRows(src, dst, "Images",        imageColumns, sessionId) : 0;
                                    int eventCount = eventColumns != null ? CopyTableRows(src, dst, "SessionEvents", eventColumns, sessionId) : 0;

                                    tx.Commit();
                                    mergedSessions++;
                                    mergedImages += imageCount;
                                    mergedEvents += eventCount;
                                    existingSessionIds.Add(sessionId);
                                } catch (Exception ex) {
                                    Logger.Warning($"NightSummary: Rolling back session {session.GetValueOrDefault("SessionId")} from {olderDbPath}: {ex.Message}");
                                    try { tx.Rollback(); } catch { }
                                }
                            }
                        }
                    }

                    Logger.Info($"NightSummary: Merged {olderDbPath} — {mergedSessions} session(s) ({mergedImages} images, {mergedEvents} events), {skippedSessions} skipped");
                    totalMergedSessions += mergedSessions;
                    totalMergedImages   += mergedImages;
                    totalMergedEvents   += mergedEvents;

                    // Record this database as done so it's skipped if the process is interrupted later
                    try { File.AppendAllText(mergeLogPath, olderDbPath + Environment.NewLine); } catch { }

                } catch (Exception ex) {
                    Logger.Error($"NightSummary: Failed to merge {olderDbPath}: {ex.Message}. Skipping — no data was lost.");
                }
            }

            Logger.Info($"NightSummary: All merges complete — {totalMergedSessions} total session(s) merged ({totalMergedImages} images, {totalMergedEvents} events)");

            // Post-merge integrity check — if it fails, the pre-merge backup is the recovery path
            if (!VerifySQLiteIntegrity(dbPath)) {
                Logger.Error($"NightSummary: Post-merge integrity check FAILED. Restore from backup at: {backupPath}");
            } else {
                Logger.Info("NightSummary: Post-merge integrity check passed. Migration complete.");
                // Clean up state file on full success; keep backup for one version cycle as a safety net
                try { if (File.Exists(mergeLogPath)) File.Delete(mergeLogPath); } catch { }
            }
        }

        /// <summary>
        /// Runs SQLite's PRAGMA integrity_check on the given file.
        /// Returns true only if the database is fully healthy.
        /// </summary>
        private static bool VerifySQLiteIntegrity(string filePath) {
            try {
                var cs = $"Data Source={filePath};Version=3;Read Only=True;";
                using (var conn = new SQLiteConnection(cs)) {
                    conn.Open();
                    var results = new List<string>();
                    using (var cmd = new SQLiteCommand("PRAGMA integrity_check", conn))
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read())
                            results.Add(reader.GetString(0));
                    }
                    bool ok = results.Count == 1 && results[0] == "ok";
                    if (!ok)
                        Logger.Warning($"NightSummary: Integrity check failed for {filePath}: {string.Join("; ", results)}");
                    else
                        Logger.Info($"NightSummary: Integrity check passed for {filePath}");
                    return ok;
                }
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: Could not run integrity check on {filePath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Returns true if the named table exists in the given connection.
        /// </summary>
        private static bool TableExists(SQLiteConnection conn, string tableName) {
            using (var cmd = new SQLiteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name", conn)) {
                cmd.Parameters.AddWithValue("@name", tableName);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        /// <summary>
        /// Returns column names present in both src and dst for the given table, excluding Id.
        /// </summary>
        private static List<string> GetCommonColumns(SQLiteConnection src, SQLiteConnection dst, string tableName) {
            return GetColumnNames(src, tableName)
                .Intersect(GetColumnNames(dst, tableName))
                .Where(c => c != "Id")
                .ToList();
        }

        private static List<string> GetColumnNames(SQLiteConnection conn, string tableName) {
            var columns = new List<string>();
            using (var cmd = new SQLiteCommand($"PRAGMA table_info({tableName})", conn))
            using (var reader = cmd.ExecuteReader()) {
                while (reader.Read())
                    columns.Add(reader.GetString(1));
            }
            return columns;
        }

        /// <summary>
        /// Copies all rows for the given sessionId from src to dst for the specified table,
        /// inserting only the pre-computed common columns. Returns the number of rows copied.
        /// </summary>
        private static int CopyTableRows(SQLiteConnection src, SQLiteConnection dst,
            string tableName, List<string> columns, string sessionId) {
            if (columns.Count == 0) return 0;
            int count = 0;
            var columnList = string.Join(", ", columns);
            var paramList  = string.Join(", ", columns.Select(c => $"@{c}"));

            using (var readCmd = new SQLiteCommand($"SELECT {columnList} FROM {tableName} WHERE SessionId = @sid", src)) {
                readCmd.Parameters.AddWithValue("@sid", sessionId);
                using (var reader = readCmd.ExecuteReader()) {
                    while (reader.Read()) {
                        using (var insertCmd = new SQLiteCommand($"INSERT INTO {tableName} ({columnList}) VALUES ({paramList})", dst)) {
                            foreach (var col in columns)
                                insertCmd.Parameters.AddWithValue($"@{col}", reader[col] ?? DBNull.Value);
                            insertCmd.ExecuteNonQuery();
                            count++;
                        }
                    }
                }
            }
            return count;
        }
        public SessionDatabase(string customDbPath) {
            dbPath = customDbPath;
            connectionString = $"Data Source={dbPath};Version=3;";
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath));
            InitializeDatabase();
        }

        /// <summary>
        /// Copies the bundled demo database to the test DB location if no test DB exists yet.
        /// Runs once on first install so users have demo data ready for Send Test Report.
        /// </summary>
        private static void SeedTestDatabaseIfMissing(string pluginDataPath) {
            try {
                var testDir    = Path.Combine(pluginDataPath, "test");
                var testDbPath = Path.Combine(testDir, "nightsummary.sqlite");
                var versionFile = Path.Combine(testDir, "demo.version");
                var pluginDir  = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var bundled    = Path.Combine(pluginDir, "Assets", "demo-nightsummary.sqlite");
                if (!File.Exists(bundled)) return;

                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
                var existingVersion = File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : "";

                if (File.Exists(testDbPath) && currentVersion == existingVersion) return;

                Directory.CreateDirectory(testDir);
                File.Copy(bundled, testDbPath, overwrite: true);
                File.WriteAllText(versionFile, currentVersion);
                Logger.Info($"NightSummary: Demo database updated to version {currentVersion}");
            } catch { /* non-fatal — user can always run the seed script manually */ }
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
                        Description TEXT,
                        AfSucceeded INTEGER,
                        AfHfr REAL
                    )";

                using (var cmd = new SQLiteCommand(createEvents, conn))
                    cmd.ExecuteNonQuery();

                // Migrate existing databases that predate added columns
                MigrateAddColumn(conn, "Images",        "FWHM",             "REAL DEFAULT 0");
                MigrateAddColumn(conn, "Images",        "Eccentricity",     "REAL DEFAULT 0");
                MigrateAddColumn(conn, "Images",        "RaHours",          "REAL DEFAULT 0");
                MigrateAddColumn(conn, "Images",        "DecDegrees",       "REAL DEFAULT 0");
                MigrateAddColumn(conn, "Images",        "FocuserTemp",      "REAL");
                MigrateAddColumn(conn, "Images",        "AmbientTemp",      "REAL");
                MigrateAddColumn(conn, "Sessions",      "CamXSize",         "INTEGER DEFAULT 0");
                MigrateAddColumn(conn, "Sessions",      "CamYSize",         "INTEGER DEFAULT 0");
                MigrateAddColumn(conn, "Sessions",      "PixelSizeMicrons", "REAL DEFAULT 0");
                MigrateAddColumn(conn, "Sessions",      "FocalLengthMm",    "REAL DEFAULT 0");
                MigrateAddColumn(conn, "Images",        "Gain",             "INTEGER DEFAULT -1");
                MigrateAddColumn(conn, "Images",        "Offset",           "INTEGER DEFAULT -1");
                MigrateAddColumn(conn, "Images",        "Binning",          "INTEGER DEFAULT 0");
                MigrateAddColumn(conn, "Images",        "CameraTemp",       "REAL");
                MigrateAddColumn(conn, "Images",        "CoolerSetpoint",   "REAL");
                MigrateAddColumn(conn, "Images",        "FocuserPosition",  "INTEGER");
                MigrateAddColumn(conn, "Images",        "RotatorPosition",  "REAL");
                MigrateAddColumn(conn, "Images",        "PositionAngle",    "REAL");
                MigrateAddColumn(conn, "Images",        "Humidity",         "REAL");
                MigrateAddColumn(conn, "Images",        "DewPoint",         "REAL");
                MigrateAddColumn(conn, "Images",        "WindSpeed",        "REAL");
                MigrateAddColumn(conn, "Images",        "Pressure",         "REAL");
                MigrateAddColumn(conn, "Images",        "GradingStatus",    "INTEGER DEFAULT -1");
                MigrateAddColumn(conn, "Images",        "RejectReason",     "TEXT");
                MigrateAddColumn(conn, "Images",        "ImageType",        "TEXT");
                MigrateAddColumn(conn, "Images",        "Altitude",         "REAL");
                MigrateAddColumn(conn, "Images",        "Azimuth",          "REAL");
                MigrateAddColumn(conn, "Images",        "Airmass",          "REAL");
                MigrateAddColumn(conn, "Images",        "SideOfPier",       "TEXT");
                MigrateAddColumn(conn, "Images",        "ReadoutMode",      "TEXT");
                MigrateAddColumn(conn, "Images",        "SkyQuality",       "REAL");
                MigrateAddColumn(conn, "Images",        "CloudCover",       "REAL");
                MigrateAddColumn(conn, "Images",        "SeeingFWHM",       "REAL");
                MigrateAddColumn(conn, "SessionEvents", "AfSucceeded",      "INTEGER");
                MigrateAddColumn(conn, "SessionEvents", "AfHfr",            "REAL");
                MigrateAddColumn(conn, "Sessions",      "SkippedExposures", "INTEGER DEFAULT 0");
                MigrateAddColumn(conn, "Sessions",      "CameraName",       "TEXT");
                MigrateAddColumn(conn, "Sessions",      "TelescopeName",    "TEXT");
                MigrateAddColumn(conn, "Sessions",      "MountName",        "TEXT");
                MigrateAddColumn(conn, "Sessions",      "FilterWheelName",  "TEXT");
                MigrateAddColumn(conn, "Sessions",      "FocuserName",      "TEXT");
                MigrateAddColumn(conn, "Sessions",      "RotatorName",      "TEXT");
                MigrateAddColumn(conn, "Sessions",      "GuiderName",       "TEXT");
                MigrateAddColumn(conn, "Sessions",      "DomeName",         "TEXT");
                MigrateAddColumn(conn, "Sessions",      "FlatDeviceName",   "TEXT");
                MigrateAddColumn(conn, "Sessions",      "SafetyMonitorName","TEXT");
                MigrateAddColumn(conn, "Sessions",      "WeatherName",      "TEXT");
                MigrateAddColumn(conn, "Sessions",      "SwitchName",       "TEXT");
                MigrateAddColumn(conn, "Images",        "StatMedian",       "REAL");
                MigrateAddColumn(conn, "Images",        "StatMean",         "REAL");
                MigrateAddColumn(conn, "Images",        "StatStDev",        "REAL");
                MigrateAddColumn(conn, "Images",        "StatMAD",          "REAL");
                MigrateAddColumn(conn, "Images",        "StatMin",          "INTEGER");
                MigrateAddColumn(conn, "Images",        "StatMax",          "INTEGER");
                MigrateAddColumn(conn, "Images",        "StatBitDepth",     "INTEGER");
                MigrateAddColumn(conn, "Images",        "SkyBrightness",    "REAL");
                MigrateAddColumn(conn, "Images",        "SkyTemperature",   "REAL");
                MigrateAddColumn(conn, "Images",        "WindDirection",    "REAL");
                MigrateAddColumn(conn, "Images",        "WindGust",         "REAL");

                // Index to keep session-list enrichment queries fast even on DBs with
                // hundreds of sessions and 100k+ images (subqueries per-session).
                using (var cmd = new SQLiteCommand("CREATE INDEX IF NOT EXISTS idx_images_sessionid ON Images(SessionId)", conn)) {
                    cmd.ExecuteNonQuery();
                }

                string createTimingEvents = @"
                    CREATE TABLE IF NOT EXISTS SessionTimingEvents (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SessionId TEXT NOT NULL,
                        EventType TEXT NOT NULL,
                        StartTime TEXT,
                        EndTime TEXT,
                        DurationSeconds REAL,
                        Details TEXT
                    )";

                using (var cmd = new SQLiteCommand(createTimingEvents, conn))
                    cmd.ExecuteNonQuery();
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
        /// Stores camera hardware info captured from the first image of the session.
        /// Safe to call multiple times — only updates if values are still zero.
        /// </summary>
        public void UpdateSessionCameraInfo(string sessionId, int camXSize, int camYSize, double pixelSizeMicrons, double focalLengthMm) {
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                string sql = @"
                    UPDATE Sessions
                    SET CamXSize = @CamXSize, CamYSize = @CamYSize,
                        PixelSizeMicrons = @PixelSizeMicrons, FocalLengthMm = @FocalLengthMm
                    WHERE SessionId = @SessionId AND CamXSize = 0";

                using (var cmd = new SQLiteCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@SessionId",        sessionId);
                    cmd.Parameters.AddWithValue("@CamXSize",         camXSize);
                    cmd.Parameters.AddWithValue("@CamYSize",         camYSize);
                    cmd.Parameters.AddWithValue("@PixelSizeMicrons", pixelSizeMicrons);
                    cmd.Parameters.AddWithValue("@FocalLengthMm",    focalLengthMm);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Updates equipment names for a session. Only overwrites fields that are currently empty,
        /// so calling at both session start and end fills in late-connecting equipment without
        /// overwriting values captured earlier.
        /// </summary>
        public void UpdateSessionEquipment(string sessionId, string camera, string telescope, string mount,
            string filterWheel, string focuser, string rotator, string guider,
            string dome = null, string flatDevice = null, string safetyMonitor = null,
            string weather = null, string switchHub = null) {
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                string sql = @"
                    UPDATE Sessions SET
                        CameraName        = CASE WHEN CameraName        IS NULL OR CameraName        = '' THEN @Camera        ELSE CameraName        END,
                        TelescopeName     = CASE WHEN TelescopeName     IS NULL OR TelescopeName     = '' THEN @Telescope     ELSE TelescopeName     END,
                        MountName         = CASE WHEN MountName         IS NULL OR MountName         = '' THEN @Mount         ELSE MountName         END,
                        FilterWheelName   = CASE WHEN FilterWheelName   IS NULL OR FilterWheelName   = '' THEN @FilterWheel   ELSE FilterWheelName   END,
                        FocuserName       = CASE WHEN FocuserName       IS NULL OR FocuserName       = '' THEN @Focuser       ELSE FocuserName       END,
                        RotatorName       = CASE WHEN RotatorName       IS NULL OR RotatorName       = '' THEN @Rotator       ELSE RotatorName       END,
                        GuiderName        = CASE WHEN GuiderName        IS NULL OR GuiderName        = '' THEN @Guider        ELSE GuiderName        END,
                        DomeName          = CASE WHEN DomeName          IS NULL OR DomeName          = '' THEN @Dome          ELSE DomeName          END,
                        FlatDeviceName    = CASE WHEN FlatDeviceName    IS NULL OR FlatDeviceName    = '' THEN @FlatDevice    ELSE FlatDeviceName    END,
                        SafetyMonitorName = CASE WHEN SafetyMonitorName IS NULL OR SafetyMonitorName = '' THEN @SafetyMonitor ELSE SafetyMonitorName END,
                        WeatherName       = CASE WHEN WeatherName       IS NULL OR WeatherName       = '' THEN @Weather       ELSE WeatherName       END,
                        SwitchName        = CASE WHEN SwitchName        IS NULL OR SwitchName        = '' THEN @Switch        ELSE SwitchName        END
                    WHERE SessionId = @SessionId";

                using (var cmd = new SQLiteCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@SessionId",      sessionId);
                    cmd.Parameters.AddWithValue("@Camera",         (object)camera         ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Telescope",      (object)telescope      ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Mount",          (object)mount          ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@FilterWheel",    (object)filterWheel    ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Focuser",        (object)focuser        ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Rotator",        (object)rotator        ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Guider",         (object)guider         ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Dome",           (object)dome           ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@FlatDevice",     (object)flatDevice     ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@SafetyMonitor",  (object)safetyMonitor  ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Weather",        (object)weather        ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Switch",         (object)switchHub      ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Updates the session end time and report sent status.
        /// Call this when the sequence ends.
        /// </summary>
        public void FinalizeSession(string sessionId, DateTime endTime, bool reportSent, int skippedExposures = 0) {
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                string sql = @"
                    UPDATE Sessions
                    SET SessionEnd = @SessionEnd, ReportSent = @ReportSent, SkippedExposures = @SkippedExposures
                    WHERE SessionId = @SessionId";

                using (var cmd = new SQLiteCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@SessionEnd", endTime.ToString("o"));
                    cmd.Parameters.AddWithValue("@ReportSent", reportSent ? 1 : 0);
                    cmd.Parameters.AddWithValue("@SkippedExposures", skippedExposures);
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
                        RaHours, DecDegrees, FocuserTemp, AmbientTemp,
                        Gain, Offset, Binning, CameraTemp, CoolerSetpoint,
                        FocuserPosition, RotatorPosition, PositionAngle,
                        Humidity, DewPoint, WindSpeed, Pressure,
                        SkyBrightness, SkyTemperature, WindDirection, WindGust,
                        GradingStatus, RejectReason,
                        ImageType, Altitude, Azimuth, Airmass, SideOfPier, ReadoutMode, SkyQuality, CloudCover, SeeingFWHM,
                        StatMedian, StatMean, StatStDev, StatMAD, StatMin, StatMax, StatBitDepth)
                    VALUES (
                        @SessionId, @Timestamp, @TargetName, @Filter, @ExposureDuration,
                        @HFR, @FWHM, @Eccentricity, @StarCount, @GuidingRMSTotal, @GuidingScale, @Accepted,
                        @RaHours, @DecDegrees, @FocuserTemp, @AmbientTemp,
                        @Gain, @Offset, @Binning, @CameraTemp, @CoolerSetpoint,
                        @FocuserPosition, @RotatorPosition, @PositionAngle,
                        @Humidity, @DewPoint, @WindSpeed, @Pressure,
                        @SkyBrightness, @SkyTemperature, @WindDirection, @WindGust,
                        @GradingStatus, @RejectReason,
                        @ImageType, @Altitude, @Azimuth, @Airmass, @SideOfPier, @ReadoutMode, @SkyQuality, @CloudCover, @SeeingFWHM,
                        @StatMedian, @StatMean, @StatStDev, @StatMAD, @StatMin, @StatMax, @StatBitDepth)";

                using (var cmd = new SQLiteCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@SessionId",       image.SessionId);
                    cmd.Parameters.AddWithValue("@Timestamp",       image.Timestamp.ToString("o"));
                    cmd.Parameters.AddWithValue("@TargetName",      image.TargetName ?? "");
                    cmd.Parameters.AddWithValue("@Filter",          image.Filter ?? "");
                    cmd.Parameters.AddWithValue("@ExposureDuration",image.ExposureDuration);
                    cmd.Parameters.AddWithValue("@HFR",             image.HFR);
                    cmd.Parameters.AddWithValue("@FWHM",            image.FWHM);
                    cmd.Parameters.AddWithValue("@Eccentricity",    image.Eccentricity);
                    cmd.Parameters.AddWithValue("@StarCount",       image.StarCount);
                    cmd.Parameters.AddWithValue("@GuidingRMSTotal", image.GuidingRMSTotal);
                    cmd.Parameters.AddWithValue("@GuidingScale",    image.GuidingScale);
                    cmd.Parameters.AddWithValue("@Accepted",        image.Accepted ? 1 : 0);
                    cmd.Parameters.AddWithValue("@RaHours",         image.RaHours);
                    cmd.Parameters.AddWithValue("@DecDegrees",      image.DecDegrees);
                    cmd.Parameters.AddWithValue("@FocuserTemp",     image.FocuserTemp.HasValue     ? (object)image.FocuserTemp.Value     : DBNull.Value);
                    cmd.Parameters.AddWithValue("@AmbientTemp",     image.AmbientTemp.HasValue     ? (object)image.AmbientTemp.Value     : DBNull.Value);
                    cmd.Parameters.AddWithValue("@Gain",            image.Gain);
                    cmd.Parameters.AddWithValue("@Offset",          image.Offset);
                    cmd.Parameters.AddWithValue("@Binning",         image.Binning);
                    cmd.Parameters.AddWithValue("@CameraTemp",      image.CameraTemp.HasValue      ? (object)image.CameraTemp.Value      : DBNull.Value);
                    cmd.Parameters.AddWithValue("@CoolerSetpoint",  image.CoolerSetpoint.HasValue  ? (object)image.CoolerSetpoint.Value  : DBNull.Value);
                    cmd.Parameters.AddWithValue("@FocuserPosition", image.FocuserPosition.HasValue ? (object)image.FocuserPosition.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@RotatorPosition", image.RotatorPosition.HasValue ? (object)image.RotatorPosition.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@PositionAngle",   image.PositionAngle.HasValue   ? (object)image.PositionAngle.Value   : DBNull.Value);
                    cmd.Parameters.AddWithValue("@Humidity",        image.Humidity.HasValue        ? (object)image.Humidity.Value        : DBNull.Value);
                    cmd.Parameters.AddWithValue("@DewPoint",        image.DewPoint.HasValue        ? (object)image.DewPoint.Value        : DBNull.Value);
                    cmd.Parameters.AddWithValue("@WindSpeed",       image.WindSpeed.HasValue       ? (object)image.WindSpeed.Value       : DBNull.Value);
                    cmd.Parameters.AddWithValue("@Pressure",        image.Pressure.HasValue        ? (object)image.Pressure.Value        : DBNull.Value);
                    cmd.Parameters.AddWithValue("@SkyBrightness",  image.SkyBrightness.HasValue  ? (object)image.SkyBrightness.Value  : DBNull.Value);
                    cmd.Parameters.AddWithValue("@SkyTemperature", image.SkyTemperature.HasValue ? (object)image.SkyTemperature.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@WindDirection",  image.WindDirection.HasValue   ? (object)image.WindDirection.Value  : DBNull.Value);
                    cmd.Parameters.AddWithValue("@WindGust",       image.WindGust.HasValue        ? (object)image.WindGust.Value       : DBNull.Value);
                    cmd.Parameters.AddWithValue("@GradingStatus",   image.GradingStatus);
                    cmd.Parameters.AddWithValue("@RejectReason",    image.RejectReason != null     ? (object)image.RejectReason          : DBNull.Value);
                    cmd.Parameters.AddWithValue("@ImageType",       image.ImageType    != null     ? (object)image.ImageType             : DBNull.Value);
                    cmd.Parameters.AddWithValue("@Altitude",        image.Altitude.HasValue        ? (object)image.Altitude.Value        : DBNull.Value);
                    cmd.Parameters.AddWithValue("@Azimuth",         image.Azimuth.HasValue         ? (object)image.Azimuth.Value         : DBNull.Value);
                    cmd.Parameters.AddWithValue("@Airmass",         image.Airmass.HasValue         ? (object)image.Airmass.Value         : DBNull.Value);
                    cmd.Parameters.AddWithValue("@SideOfPier",      image.SideOfPier   != null     ? (object)image.SideOfPier            : DBNull.Value);
                    cmd.Parameters.AddWithValue("@ReadoutMode",     image.ReadoutMode  != null     ? (object)image.ReadoutMode           : DBNull.Value);
                    cmd.Parameters.AddWithValue("@SkyQuality",      image.SkyQuality.HasValue      ? (object)image.SkyQuality.Value      : DBNull.Value);
                    cmd.Parameters.AddWithValue("@CloudCover",      image.CloudCover.HasValue      ? (object)image.CloudCover.Value      : DBNull.Value);
                    cmd.Parameters.AddWithValue("@SeeingFWHM",      image.SeeingFWHM.HasValue      ? (object)image.SeeingFWHM.Value      : DBNull.Value);
                    cmd.Parameters.AddWithValue("@StatMedian",      image.StatMedian.HasValue      ? (object)image.StatMedian.Value      : DBNull.Value);
                    cmd.Parameters.AddWithValue("@StatMean",        image.StatMean.HasValue        ? (object)image.StatMean.Value        : DBNull.Value);
                    cmd.Parameters.AddWithValue("@StatStDev",       image.StatStDev.HasValue       ? (object)image.StatStDev.Value       : DBNull.Value);
                    cmd.Parameters.AddWithValue("@StatMAD",         image.StatMAD.HasValue         ? (object)image.StatMAD.Value         : DBNull.Value);
                    cmd.Parameters.AddWithValue("@StatMin",         image.StatMin.HasValue         ? (object)image.StatMin.Value         : DBNull.Value);
                    cmd.Parameters.AddWithValue("@StatMax",         image.StatMax.HasValue         ? (object)image.StatMax.Value         : DBNull.Value);
                    cmd.Parameters.AddWithValue("@StatBitDepth",    image.StatBitDepth.HasValue    ? (object)image.StatBitDepth.Value    : DBNull.Value);
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
                                DecDegrees = reader["DecDegrees"] == DBNull.Value ? 0 : Convert.ToDouble(reader["DecDegrees"]),
                                FocuserTemp     = reader["FocuserTemp"]     == DBNull.Value ? (double?)null : Convert.ToDouble(reader["FocuserTemp"]),
                                AmbientTemp     = reader["AmbientTemp"]     == DBNull.Value ? (double?)null : Convert.ToDouble(reader["AmbientTemp"]),
                                Gain            = reader["Gain"]            == DBNull.Value ? -1 : Convert.ToInt32(reader["Gain"]),
                                Offset          = reader["Offset"]          == DBNull.Value ? -1 : Convert.ToInt32(reader["Offset"]),
                                Binning         = reader["Binning"]         == DBNull.Value ? 0  : Convert.ToInt32(reader["Binning"]),
                                CameraTemp      = reader["CameraTemp"]      == DBNull.Value ? (double?)null : Convert.ToDouble(reader["CameraTemp"]),
                                CoolerSetpoint  = reader["CoolerSetpoint"]  == DBNull.Value ? (double?)null : Convert.ToDouble(reader["CoolerSetpoint"]),
                                FocuserPosition = reader["FocuserPosition"] == DBNull.Value ? (int?)null    : Convert.ToInt32(reader["FocuserPosition"]),
                                RotatorPosition = reader["RotatorPosition"] == DBNull.Value ? (double?)null : Convert.ToDouble(reader["RotatorPosition"]),
                                PositionAngle   = reader["PositionAngle"]   == DBNull.Value ? (double?)null : Convert.ToDouble(reader["PositionAngle"]),
                                Humidity        = reader["Humidity"]        == DBNull.Value ? (double?)null : Convert.ToDouble(reader["Humidity"]),
                                DewPoint        = reader["DewPoint"]        == DBNull.Value ? (double?)null : Convert.ToDouble(reader["DewPoint"]),
                                WindSpeed       = reader["WindSpeed"]       == DBNull.Value ? (double?)null : Convert.ToDouble(reader["WindSpeed"]),
                                Pressure        = reader["Pressure"]        == DBNull.Value ? (double?)null : Convert.ToDouble(reader["Pressure"]),
                                SkyBrightness   = reader["SkyBrightness"]   == DBNull.Value ? (double?)null : Convert.ToDouble(reader["SkyBrightness"]),
                                SkyTemperature  = reader["SkyTemperature"]  == DBNull.Value ? (double?)null : Convert.ToDouble(reader["SkyTemperature"]),
                                WindDirection   = reader["WindDirection"]   == DBNull.Value ? (double?)null : Convert.ToDouble(reader["WindDirection"]),
                                WindGust        = reader["WindGust"]        == DBNull.Value ? (double?)null : Convert.ToDouble(reader["WindGust"]),
                                GradingStatus   = reader["GradingStatus"]   == DBNull.Value ? -1 : Convert.ToInt32(reader["GradingStatus"]),
                                RejectReason    = reader["RejectReason"]    == DBNull.Value ? null : reader["RejectReason"].ToString(),
                                ImageType       = reader["ImageType"]       == DBNull.Value ? null : reader["ImageType"].ToString(),
                                Altitude        = reader["Altitude"]        == DBNull.Value ? (double?)null : Convert.ToDouble(reader["Altitude"]),
                                Azimuth         = reader["Azimuth"]         == DBNull.Value ? (double?)null : Convert.ToDouble(reader["Azimuth"]),
                                Airmass         = reader["Airmass"]         == DBNull.Value ? (double?)null : Convert.ToDouble(reader["Airmass"]),
                                SideOfPier      = reader["SideOfPier"]      == DBNull.Value ? null : reader["SideOfPier"].ToString(),
                                ReadoutMode     = reader["ReadoutMode"]     == DBNull.Value ? null : reader["ReadoutMode"].ToString(),
                                SkyQuality      = reader["SkyQuality"]      == DBNull.Value ? (double?)null : Convert.ToDouble(reader["SkyQuality"]),
                                CloudCover      = reader["CloudCover"]      == DBNull.Value ? (double?)null : Convert.ToDouble(reader["CloudCover"]),
                                SeeingFWHM      = reader["SeeingFWHM"]      == DBNull.Value ? (double?)null : Convert.ToDouble(reader["SeeingFWHM"]),
                                StatMedian      = reader["StatMedian"]      == DBNull.Value ? (double?)null : Convert.ToDouble(reader["StatMedian"]),
                                StatMean        = reader["StatMean"]        == DBNull.Value ? (double?)null : Convert.ToDouble(reader["StatMean"]),
                                StatStDev       = reader["StatStDev"]       == DBNull.Value ? (double?)null : Convert.ToDouble(reader["StatStDev"]),
                                StatMAD         = reader["StatMAD"]         == DBNull.Value ? (double?)null : Convert.ToDouble(reader["StatMAD"]),
                                StatMin         = reader["StatMin"]         == DBNull.Value ? (int?)null    : Convert.ToInt32(reader["StatMin"]),
                                StatMax         = reader["StatMax"]         == DBNull.Value ? (int?)null    : Convert.ToInt32(reader["StatMax"]),
                                StatBitDepth    = reader["StatBitDepth"]    == DBNull.Value ? (int?)null    : Convert.ToInt32(reader["StatBitDepth"])
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
                                return ReadSessionRecord(reader);
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
                    INSERT INTO SessionEvents (SessionId, Timestamp, EventType, Description, AfSucceeded, AfHfr)
                    VALUES (@SessionId, @Timestamp, @EventType, @Description, @AfSucceeded, @AfHfr)";

                using (var cmd = new SQLiteCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@SessionId",   evt.SessionId);
                    cmd.Parameters.AddWithValue("@Timestamp",   evt.Timestamp.ToString("o"));
                    cmd.Parameters.AddWithValue("@EventType",   evt.EventType ?? "");
                    cmd.Parameters.AddWithValue("@Description", evt.Description ?? "");
                    cmd.Parameters.AddWithValue("@AfSucceeded", evt.AfSucceeded.HasValue ? (object)(evt.AfSucceeded.Value ? 1 : 0) : DBNull.Value);
                    cmd.Parameters.AddWithValue("@AfHfr",       evt.AfHfr.HasValue       ? (object)evt.AfHfr.Value                : DBNull.Value);
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
                                Id          = Convert.ToInt32(reader["Id"]),
                                SessionId   = reader["SessionId"]   == DBNull.Value ? "" : reader["SessionId"].ToString(),
                                Timestamp   = reader["Timestamp"]   == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["Timestamp"].ToString()),
                                EventType   = reader["EventType"]   == DBNull.Value ? "" : reader["EventType"].ToString(),
                                Description = reader["Description"] == DBNull.Value ? "" : reader["Description"].ToString(),
                                AfSucceeded = reader["AfSucceeded"] == DBNull.Value ? (bool?)null : Convert.ToInt32(reader["AfSucceeded"]) == 1,
                                AfHfr       = reader["AfHfr"]       == DBNull.Value ? (double?)null : Convert.ToDouble(reader["AfHfr"])
                            });
                        }
                    }
                }
            }
            return events;
        }

        public void SaveTimingEvents(string sessionId, List<TimingEvent> events) {
            if (events == null || events.Count == 0) return;
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                using (var transaction = conn.BeginTransaction()) {
                    string sql = @"
                        INSERT INTO SessionTimingEvents (SessionId, EventType, StartTime, EndTime, DurationSeconds, Details)
                        VALUES (@SessionId, @EventType, @StartTime, @EndTime, @DurationSeconds, @Details)";

                    foreach (var evt in events) {
                        using (var cmd = new SQLiteCommand(sql, conn)) {
                            cmd.Parameters.AddWithValue("@SessionId",       sessionId);
                            cmd.Parameters.AddWithValue("@EventType",       evt.EventType ?? "");
                            cmd.Parameters.AddWithValue("@StartTime",       evt.StartTime == DateTime.MinValue ? (object)DBNull.Value : evt.StartTime.ToString("o"));
                            cmd.Parameters.AddWithValue("@EndTime",         evt.EndTime   == DateTime.MinValue ? (object)DBNull.Value : evt.EndTime.ToString("o"));
                            cmd.Parameters.AddWithValue("@DurationSeconds", evt.DurationSeconds);
                            cmd.Parameters.AddWithValue("@Details",         evt.Details != null ? (object)evt.Details : DBNull.Value);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    transaction.Commit();
                }
            }
        }

        public void ClearTimingEvents(string sessionId) {
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                using (var cmd = new SQLiteCommand("DELETE FROM SessionTimingEvents WHERE SessionId = @SessionId", conn)) {
                    cmd.Parameters.AddWithValue("@SessionId", sessionId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Atomically deletes a session and all of its related rows (images, events, timing events).
        /// Returns the number of rows deleted from the Sessions table (0 if the session was not found, 1 on success).
        /// </summary>
        public int DeleteSession(string sessionId) {
            if (string.IsNullOrWhiteSpace(sessionId)) return 0;

            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                using (var tx = conn.BeginTransaction()) {
                    try {
                        int affectedParent;

                        using (var cmd = new SQLiteCommand("DELETE FROM Images WHERE SessionId = @sid", conn, tx)) {
                            cmd.Parameters.AddWithValue("@sid", sessionId);
                            cmd.ExecuteNonQuery();
                        }
                        using (var cmd = new SQLiteCommand("DELETE FROM SessionEvents WHERE SessionId = @sid", conn, tx)) {
                            cmd.Parameters.AddWithValue("@sid", sessionId);
                            cmd.ExecuteNonQuery();
                        }
                        using (var cmd = new SQLiteCommand("DELETE FROM SessionTimingEvents WHERE SessionId = @sid", conn, tx)) {
                            cmd.Parameters.AddWithValue("@sid", sessionId);
                            cmd.ExecuteNonQuery();
                        }
                        using (var cmd = new SQLiteCommand("DELETE FROM Sessions WHERE SessionId = @sid", conn, tx)) {
                            cmd.Parameters.AddWithValue("@sid", sessionId);
                            affectedParent = cmd.ExecuteNonQuery();
                        }

                        tx.Commit();
                        Logger.Info($"NightSummary: Deleted session {sessionId} ({affectedParent} session row)");
                        return affectedParent;
                    } catch (Exception ex) {
                        try { tx.Rollback(); } catch { }
                        Logger.Error($"NightSummary: Failed to delete session {sessionId}: {ex.Message}");
                        throw;
                    }
                }
            }
        }

        public List<TimingEvent> GetTimingEventsForSession(string sessionId) {
            var events = new List<TimingEvent>();
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                string sql = "SELECT * FROM SessionTimingEvents WHERE SessionId = @SessionId ORDER BY StartTime";
                using (var cmd = new SQLiteCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@SessionId", sessionId);
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            events.Add(new TimingEvent {
                                EventType       = reader["EventType"]       == DBNull.Value ? "" : reader["EventType"].ToString(),
                                StartTime       = reader["StartTime"]       == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["StartTime"].ToString()),
                                EndTime         = reader["EndTime"]         == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["EndTime"].ToString()),
                                DurationSeconds = reader["DurationSeconds"] == DBNull.Value ? 0 : Convert.ToDouble(reader["DurationSeconds"]),
                                Details         = reader["Details"]         == DBNull.Value ? null : reader["Details"].ToString()
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
        public List<TargetSessionHistory> GetSessionHistoryForTarget(string targetName, string excludeSessionId) {
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
                    ORDER BY s.SessionStart DESC";

                using (var cmd = new SQLiteCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@TargetName",       targetName       ?? "");
                    cmd.Parameters.AddWithValue("@ExcludeSessionId", excludeSessionId ?? "");
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
        /// Returns the most recent <paramref name="limit"/> sessions, newest-first.
        /// </summary>
        public List<SessionRecord> GetRecentSessions(int limit) {
            var result = new List<SessionRecord>();
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                string sql = SessionListWithCountsSql + " ORDER BY s.SessionStart DESC LIMIT @Limit";
                using (var cmd = new SQLiteCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@Limit", limit);
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            try { result.Add(ReadEnrichedSessionRecord(reader)); }
                            catch (Exception ex) { Logger.Error($"NightSummary: Error reading session record: {ex.Message}"); }
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Returns all sessions whose start date falls within [from, to], newest-first.
        /// </summary>
        public List<SessionRecord> GetSessionsByDateRange(DateTime from, DateTime to) {
            var result = new List<SessionRecord>();
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                string sql = SessionListWithCountsSql +
                    " WHERE s.SessionStart >= @From AND s.SessionStart <= @To ORDER BY s.SessionStart DESC";
                using (var cmd = new SQLiteCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@From", from.ToString("o"));
                    cmd.Parameters.AddWithValue("@To",   to.Date.AddDays(1).AddSeconds(-1).ToString("o"));
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            try { result.Add(ReadEnrichedSessionRecord(reader)); }
                            catch (Exception ex) { Logger.Error($"NightSummary: Error reading session record: {ex.Message}"); }
                        }
                    }
                }
            }
            return result;
        }

        // Shared SELECT for session-list methods that need image/target/integration counts
        // for display in the dropdown. Counts use Accepted = 1 to match what the report shows
        // as the "X images" number. Uses correlated subqueries (no GROUP BY ambiguity with s.*)
        // and the idx_images_sessionid index to keep this fast on large DBs.
        private const string SessionListWithCountsSql = @"
            SELECT s.*,
                (SELECT COUNT(*) FROM Images WHERE SessionId = s.SessionId AND Accepted = 1) AS ImageCount,
                (SELECT COUNT(DISTINCT TargetName) FROM Images WHERE SessionId = s.SessionId AND Accepted = 1 AND TargetName IS NOT NULL AND TargetName <> '') AS TargetCount,
                (SELECT COALESCE(SUM(ExposureDuration), 0) FROM Images WHERE SessionId = s.SessionId AND Accepted = 1) AS IntegrationSeconds
            FROM Sessions s";

        private SessionRecord ReadEnrichedSessionRecord(SQLiteDataReader reader) {
            var record = ReadSessionRecord(reader);
            record.ImageCount         = reader["ImageCount"]         == DBNull.Value ? 0 : Convert.ToInt32(reader["ImageCount"]);
            record.TargetCount        = reader["TargetCount"]        == DBNull.Value ? 0 : Convert.ToInt32(reader["TargetCount"]);
            record.IntegrationSeconds = reader["IntegrationSeconds"] == DBNull.Value ? 0 : Convert.ToDouble(reader["IntegrationSeconds"]);
            return record;
        }

        /// <summary>
        /// Returns all sessions ordered newest-first.
        /// </summary>
        public List<SessionRecord> GetAllSessions() {
            var result = new List<SessionRecord>();
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                string sql = "SELECT * FROM Sessions ORDER BY SessionStart DESC";
                using (var cmd = new SQLiteCommand(sql, conn))
                using (var reader = cmd.ExecuteReader()) {
                    while (reader.Read()) {
                        try {
                            result.Add(ReadSessionRecord(reader));
                        } catch (Exception ex) {
                            Logger.Error($"NightSummary: Error reading session record: {ex.Message}");
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
                                return ReadSessionRecord(reader);
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

        /// <summary>
        /// Batch-updates GradingStatus, RejectReason, and Accepted for images in a session
        /// based on records retrieved from the Target Scheduler database.
        /// Images not matched to a TS row are left unchanged.
        /// </summary>
        public void UpdateImageGradingFromTs(string sessionId, List<(int imageId, int gradingStatus, string rejectReason)> updates) {
            if (updates == null || updates.Count == 0) return;
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                using (var tx = conn.BeginTransaction()) {
                    string sql = @"
                        UPDATE Images
                        SET GradingStatus = @GradingStatus,
                            RejectReason  = @RejectReason,
                            Accepted      = @Accepted
                        WHERE Id = @Id AND SessionId = @SessionId";

                    foreach (var (imageId, gradingStatus, rejectReason) in updates) {
                        using (var cmd = new SQLiteCommand(sql, conn, tx)) {
                            // GradingStatus enum (Target Scheduler plugin, ImageGrader.cs): 0=Pending, 1=Accepted, 2=Rejected
                            bool accepted = gradingStatus == 1;
                            cmd.Parameters.AddWithValue("@Id",            imageId);
                            cmd.Parameters.AddWithValue("@SessionId",     sessionId);
                            cmd.Parameters.AddWithValue("@GradingStatus", gradingStatus);
                            cmd.Parameters.AddWithValue("@RejectReason",  rejectReason != null ? (object)rejectReason : DBNull.Value);
                            cmd.Parameters.AddWithValue("@Accepted",      accepted ? 1 : 0);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    tx.Commit();
                }
            }
        }

        /// <summary>
        /// Updates the Accepted flag for a single image matched by session and capture timestamp.
        /// Uses a ±5-second tolerance to accommodate slight clock differences between
        /// NINA's image-saved event and the thumbnail view model.
        /// Called when the user manually grades (or un-grades) a frame in NINA's thumbnail panel.
        /// </summary>
        public int UpdateImageAccepted(string sessionId, DateTime timestamp, bool accepted, string rejectReason = null) {
            using (var conn = new SQLiteConnection(connectionString)) {
                conn.Open();
                // rejectReason null = leave existing reason unchanged (preserves TS-set reasons).
                // Empty string = clear reason (used when un-rejecting).
                string sql = rejectReason != null
                    ? @"UPDATE Images SET Accepted = @Accepted, RejectReason = @RejectReason
                        WHERE SessionId = @SessionId
                          AND ABS(JULIANDAY(Timestamp) - JULIANDAY(@Timestamp)) * 86400.0 <= 5.0"
                    : @"UPDATE Images SET Accepted = @Accepted
                        WHERE SessionId = @SessionId
                          AND ABS(JULIANDAY(Timestamp) - JULIANDAY(@Timestamp)) * 86400.0 <= 5.0";
                using (var cmd = new SQLiteCommand(sql, conn)) {
                    cmd.Parameters.AddWithValue("@SessionId", sessionId);
                    cmd.Parameters.AddWithValue("@Timestamp", timestamp.ToString("o"));
                    cmd.Parameters.AddWithValue("@Accepted",  accepted ? 1 : 0);
                    if (rejectReason != null)
                        cmd.Parameters.AddWithValue("@RejectReason", rejectReason.Length > 0 ? (object)rejectReason : DBNull.Value);
                    return cmd.ExecuteNonQuery();
                }
            }
        }

        private static SessionRecord ReadSessionRecord(SQLiteDataReader reader) {
            return new SessionRecord {
                Id               = Convert.ToInt32(reader["Id"]),
                SessionId        = reader["SessionId"]        == DBNull.Value ? "" : reader["SessionId"].ToString(),
                SessionStart     = reader["SessionStart"]     == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["SessionStart"].ToString()),
                SessionEnd       = reader["SessionEnd"]       == DBNull.Value ? DateTime.MinValue : DateTime.Parse(reader["SessionEnd"].ToString()),
                ProfileName      = reader["ProfileName"]      == DBNull.Value ? "" : reader["ProfileName"].ToString(),
                Notes            = reader["Notes"]            == DBNull.Value ? "" : reader["Notes"].ToString(),
                ReportSent       = reader["ReportSent"]       == DBNull.Value ? false : Convert.ToInt32(reader["ReportSent"]) == 1,
                CamXSize         = reader["CamXSize"]         == DBNull.Value ? 0 : Convert.ToInt32(reader["CamXSize"]),
                CamYSize         = reader["CamYSize"]         == DBNull.Value ? 0 : Convert.ToInt32(reader["CamYSize"]),
                PixelSizeMicrons = reader["PixelSizeMicrons"] == DBNull.Value ? 0 : Convert.ToDouble(reader["PixelSizeMicrons"]),
                FocalLengthMm    = reader["FocalLengthMm"]    == DBNull.Value ? 0 : Convert.ToDouble(reader["FocalLengthMm"]),
                SkippedExposures = reader["SkippedExposures"] == DBNull.Value ? 0 : Convert.ToInt32(reader["SkippedExposures"]),
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
}