using NINA.Plugin.NightSummary.Server;
using System;
using System.IO;
using System.Threading;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    public class DashboardLogTests : IDisposable {

        private readonly string _tmpDir;

        public DashboardLogTests() {
            _tmpDir = Path.Combine(Path.GetTempPath(), $"ns_log_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tmpDir);
        }

        public void Dispose() {
            try { Directory.Delete(_tmpDir, recursive: true); } catch { }
        }

        // ── PurgeOldLogs ─────────────────────────────────────────────────────

        [Fact]
        public void PurgeOldLogs_DeletesFilesOlderThanKeepDays() {
            var oldFile = Path.Combine(_tmpDir, "dashboard-2020-01-01.log");
            File.WriteAllText(oldFile, "old");
            File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-20));

            DashboardLog.PurgeOldLogs(_tmpDir, keepDays: 14);

            Assert.False(File.Exists(oldFile));
        }

        [Fact]
        public void PurgeOldLogs_PreservesFilesWithinKeepDays() {
            var recentFile = Path.Combine(_tmpDir, "dashboard-2026-04-05.log");
            File.WriteAllText(recentFile, "recent");
            File.SetLastWriteTime(recentFile, DateTime.Now.AddDays(-3));

            DashboardLog.PurgeOldLogs(_tmpDir, keepDays: 14);

            Assert.True(File.Exists(recentFile));
        }

        [Fact]
        public void PurgeOldLogs_DeletesRotatedBackupsOlderThanKeepDays() {
            var oldBackup = Path.Combine(_tmpDir, "dashboard-2020-01-01.log.1");
            File.WriteAllText(oldBackup, "old backup");
            File.SetLastWriteTime(oldBackup, DateTime.Now.AddDays(-30));

            DashboardLog.PurgeOldLogs(_tmpDir, keepDays: 14);

            Assert.False(File.Exists(oldBackup));
        }

        [Fact]
        public void PurgeOldLogs_IgnoresNonDashboardFiles() {
            var otherFile = Path.Combine(_tmpDir, "nightsummary.sqlite");
            File.WriteAllText(otherFile, "db");
            File.SetLastWriteTime(otherFile, DateTime.Now.AddDays(-30));

            DashboardLog.PurgeOldLogs(_tmpDir, keepDays: 14);

            Assert.True(File.Exists(otherFile));
        }

        [Fact]
        public void PurgeOldLogs_DoesNotThrowWhenDirectoryMissing() {
            var missing = Path.Combine(_tmpDir, "nonexistent");
            // Should not throw
            DashboardLog.PurgeOldLogs(missing, keepDays: 14);
        }

        [Fact]
        public void PurgeOldLogs_DeletesOldKeepsNew_Mixed() {
            var oldFile = Path.Combine(_tmpDir, "dashboard-2020-01-01.log");
            var newFile = Path.Combine(_tmpDir, "dashboard-2026-04-05.log");
            File.WriteAllText(oldFile, "old");
            File.WriteAllText(newFile, "new");
            File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-20));
            File.SetLastWriteTime(newFile, DateTime.Now.AddDays(-1));

            DashboardLog.PurgeOldLogs(_tmpDir, keepDays: 14);

            Assert.False(File.Exists(oldFile));
            Assert.True(File.Exists(newFile));
        }

        // ── BeginRequest ─────────────────────────────────────────────────────

        [Fact]
        public void BeginRequest_DoneCallbackWritesWithoutThrowing() {
            var logPath = Path.Combine(_tmpDir, "dashboard-test.log");
            using var log = new DashboardLog(logPath);
            log.Open();
            var done = log.BeginRequest("GET", "/api/sessions");
            Thread.Sleep(5); // ensure elapsed > 0
            done(200, "42 sessions"); // should not throw
            Assert.True(File.Exists(logPath));
        }

        [Fact]
        public void BeginRequest_DoneWithErrorStatusWritesToFile() {
            var logPath = Path.Combine(_tmpDir, "dashboard-test2.log");
            using var log = new DashboardLog(logPath);
            log.Open();
            var done = log.BeginRequest("GET", "/api/missing");
            done(404, null);
            log.Close();
            Assert.True(new FileInfo(logPath).Length > 0);
        }

        // ── Open / Close ─────────────────────────────────────────────────────

        [Fact]
        public void Open_CreatesLogFile() {
            var logPath = Path.Combine(_tmpDir, "dashboard-open.log");
            Assert.False(File.Exists(logPath));
            using var log = new DashboardLog(logPath);
            log.Open();
            Assert.True(File.Exists(logPath));
        }

        [Fact]
        public void Shutdown_ClosesStaticInstance() {
            var logPath = Path.Combine(_tmpDir, "dashboard-shutdown.log");
            DashboardLog.Init(logPath);
            DashboardLog.Shutdown();
            Assert.Null(DashboardLog.Instance);
        }
    }
}
