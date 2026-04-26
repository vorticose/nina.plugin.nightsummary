using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System;
using System.Data.SQLite;
using System.IO;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Migration scenarios that need scaffolded plugin folders. Uses the internal
    /// constructor that takes pluginsRoot so we don't touch real LocalAppData.
    /// </summary>
    public class SessionDatabaseMigrationTests : IDisposable {

        private readonly string _root;
        private readonly string _newDataPath;
        private readonly string _pluginsRoot;
        private readonly string _newDbPath;

        public SessionDatabaseMigrationTests() {
            _root = Path.Combine(Path.GetTempPath(), $"ns_mig_{Guid.NewGuid():N}");
            _newDataPath = Path.Combine(_root, "newdata");
            _pluginsRoot = Path.Combine(_root, "plugins");
            _newDbPath   = Path.Combine(_newDataPath, "nightsummary.sqlite");
            Directory.CreateDirectory(_newDataPath);
            Directory.CreateDirectory(_pluginsRoot);
        }

        public void Dispose() {
            try {
                SQLiteConnection.ClearAllPools();
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            } catch { /* test-only cleanup */ }
        }

        // Build a legacy DB at <pluginsRoot>/<versionFolder>/NightSummary/nightsummary.sqlite
        // with one session matching the given id. Returns the path.
        private string MakeLegacyDb(string versionFolder, string sessionId, DateTime sessionStart) {
            var legacyDir = Path.Combine(_pluginsRoot, versionFolder, "NightSummary");
            Directory.CreateDirectory(legacyDir);
            var legacyPath = Path.Combine(legacyDir, "nightsummary.sqlite");
            var seed = new SessionDatabase(legacyPath);
            seed.CreateSession(TestDataFactory.MakeSession(sessionId, sessionStart));
            return legacyPath;
        }

        [Fact]
        public void Resume_AfterInterruptedMerge_PicksUpUnmergedLegacyDatabases() {
            // Scaffold two legacy DBs with distinct sessions.
            var primaryId   = Guid.NewGuid().ToString();
            var unmergedId  = Guid.NewGuid().ToString();
            var primaryDb   = MakeLegacyDb("3.1.0", primaryId,  new DateTime(2025, 5, 1, 22, 0, 0));
            var olderDb     = MakeLegacyDb("3.0.0", unmergedId, new DateTime(2025, 4, 1, 22, 0, 0));

            // Make primary actually be the most-recently-modified file so the scan picks it.
            File.SetLastWriteTimeUtc(primaryDb, DateTime.UtcNow);
            File.SetLastWriteTimeUtc(olderDb,   DateTime.UtcNow.AddDays(-1));

            // Simulate the "interrupted-mid-merge" state:
            //   - dbPath exists (primary was already copied in the previous boot)
            //   - .merge_state file exists but is empty (no legacy DB finished merging yet)
            //   - the older legacy DB still has un-merged sessions
            File.Copy(primaryDb, _newDbPath);
            File.WriteAllText(_newDbPath + ".merge_state", "");

            // Construct via the test-only ctor — this triggers the resume path.
            var db = new SessionDatabase(_newDbPath, _newDataPath, _pluginsRoot);
            Assert.NotNull(db.GetSession(primaryId));
            Assert.NotNull(db.GetSession(unmergedId)); // the bug: this would be missing

            // .merge_state should be cleared after a successful resume.
            Assert.False(File.Exists(_newDbPath + ".merge_state"),
                "merge_state file should be deleted after resume completes");
        }

        [Fact]
        public void Resume_WithNoStateFile_SkipsResumeEntirely() {
            // Sanity: when dbPath exists and merge_state does NOT, migration is a no-op.
            var existingId = Guid.NewGuid().ToString();
            var seed = new SessionDatabase(_newDbPath);
            seed.CreateSession(TestDataFactory.MakeSession(existingId));

            // A legacy DB is sitting in pluginsRoot — but without merge_state we should NOT touch it.
            var orphanLegacyId = Guid.NewGuid().ToString();
            MakeLegacyDb("3.0.0", orphanLegacyId, new DateTime(2025, 4, 1, 22, 0, 0));

            var db = new SessionDatabase(_newDbPath, _newDataPath, _pluginsRoot);
            Assert.NotNull(db.GetSession(existingId));
            Assert.Null(db.GetSession(orphanLegacyId));
        }

        [Fact]
        public void Resume_WithStateFileButNoLegacyDbs_CleansUpOrphanedStateFile() {
            // Edge: state file lingers but legacy DBs are gone (user manually cleaned up).
            // Resume should detect nothing-to-do and remove the file rather than re-trigger every boot.
            var existingId = Guid.NewGuid().ToString();
            var seed = new SessionDatabase(_newDbPath);
            seed.CreateSession(TestDataFactory.MakeSession(existingId));
            File.WriteAllText(_newDbPath + ".merge_state", "");

            var db = new SessionDatabase(_newDbPath, _newDataPath, _pluginsRoot);
            Assert.NotNull(db.GetSession(existingId));

            Assert.False(File.Exists(_newDbPath + ".merge_state"));
        }
    }
}
