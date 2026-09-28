using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Session;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using NINA.Plugin.NightSummary.Tests.Mocks;
using System;
using System.IO;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Tests for FinalizeOrphanedSessions — the startup pass that recovers sessions
    /// left over from a previous NINA run (most commonly a crash before End Session
    /// could run) so they stop rendering as "in progress" and stop being hidden from
    /// the dashboard session list.
    /// </summary>
    public class SessionServiceOrphanRecoveryTests : IDisposable {

        private readonly MockSequenceMediator _sequenceMediator = new();
        private readonly MockImageSaveMediator _imageSaveMediator = new();
        private readonly MockProfileService _profileService = new();
        private readonly MockSafetyMonitorMediator _safetyMonitorMediator = new();
        private readonly MockFocuserMediator _focuserMediator = new();
        private readonly MockTelescopeMediator _telescopeMediator = new();
        private readonly MockCameraMediator _cameraMediator = new();
        private readonly string _dbPath;
        private readonly string _settingsPath;
        // Keeps dashboard report HTML out of the developer's real
        // %LOCALAPPDATA%\NINA\NightSummary\reports. SaveReportForDashboardAsync runs
        // unconditionally and consults no setting, so without this seam every test run
        // litters the live folder.
        private readonly string _reportsDir;
        // See SessionReplayRunner — SessionService reads from SettingsManager.Instance,
        // not from this test's isolated SettingsManager. Without redirecting the
        // singleton the disabled delivery flags below are decorative: FinalizeOrphanedSessions
        // generates a report per recovered session and sends it using whatever real
        // credentials are on the test host. This class did exactly that and posted three
        // live Discord messages when the suite was run on the observatory PC (2026-08-23).
        private readonly IDisposable _settingsOverride;
        private readonly SessionService _service;
        private readonly SessionDatabase _db;

        public SessionServiceOrphanRecoveryTests() {
            _dbPath       = Path.Combine(Path.GetTempPath(), $"ns_orphan_{Guid.NewGuid():N}.sqlite");
            _settingsPath = Path.Combine(Path.GetTempPath(), $"ns_orphan_settings_{Guid.NewGuid():N}.json");
            _reportsDir   = Path.Combine(Path.GetTempPath(), $"ns_orphan_reports_{Guid.NewGuid():N}");

            var settingsMgr = new SettingsManager(_settingsPath, attemptMigration: false);
            settingsMgr.Load();
            settingsMgr.Current.SaveReportLocally = false;
            settingsMgr.Current.EmailEnabled      = false;
            settingsMgr.Current.DiscordEnabled    = false;
            settingsMgr.Current.PushoverEnabled   = false;
            settingsMgr.Save();
            _settingsOverride = SettingsManager.UseInstanceForTesting(settingsMgr);

            _service = new SessionService(
                _imageSaveMediator,
                _profileService,
                _safetyMonitorMediator,
                _focuserMediator,
                _telescopeMediator,
                _cameraMediator,
                _sequenceMediator,
                null, null, null, null, null, null, null, null,
                databasePath: _dbPath,
                reportsDirectory: _reportsDir);

            _db = new SessionDatabase(_dbPath);
        }

        public void Dispose() {
            // Drain pending report tasks before releasing the settings override — see
            // SessionReplayRunner.Dispose for the race this prevents. These tests DO
            // finalize sessions with content, so this is load-bearing here: releasing
            // the override first would let an in-flight report send against the host's
            // real settings.
            try {
                _service?.WaitForPendingReportsAsync(TimeSpan.FromSeconds(10))
                         .GetAwaiter().GetResult();
            } catch { }
            _settingsOverride?.Dispose();
            if (File.Exists(_dbPath))       File.Delete(_dbPath);
            if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
            try { if (Directory.Exists(_reportsDir)) Directory.Delete(_reportsDir, true); } catch { }
        }

        // ── No database ─────────────────────────────────────────────────────

        [Fact]
        public void NoDatabase_ReturnsZeroAndDoesNotThrow() {
            var missingPath = Path.Combine(Path.GetTempPath(), $"ns_missing_{Guid.NewGuid():N}.sqlite");
            var recovered = _service.FinalizeOrphanedSessions(missingPath);
            Assert.Equal(0, recovered);
        }

        // ── Orphaned session recovery ───────────────────────────────────────

        [Fact]
        public void OrphanedSessionWithImages_IsFinalizedFromLastImageTimestamp() {
            var sessionId = Guid.NewGuid().ToString();
            var start      = new DateTime(2025, 1, 15, 21, 0, 0);
            var lastImage  = start.AddHours(3);
            _db.CreateSession(new SessionRecord { SessionId = sessionId, SessionStart = start, ProfileName = "Test" });
            _db.SaveImageRecord(TestDataFactory.MakeImage(sessionId, timestamp: start.AddHours(1)));
            _db.SaveImageRecord(TestDataFactory.MakeImage(sessionId, timestamp: lastImage));

            var recovered = _service.FinalizeOrphanedSessions(_dbPath);

            Assert.Equal(1, recovered);
            var session = _db.GetSession(sessionId);
            Assert.Equal(lastImage, session!.SessionEnd);
            Assert.True(session.AutoFinalized);
        }

        [Fact]
        public void AlreadyFinalizedSession_IsNotTouchedOrCounted() {
            var sessionId = Guid.NewGuid().ToString();
            var start = new DateTime(2025, 1, 15, 21, 0, 0);
            var end   = start.AddHours(6);
            _db.CreateSession(new SessionRecord { SessionId = sessionId, SessionStart = start, ProfileName = "Test" });
            _db.FinalizeSession(sessionId, end, reportSent: true);

            var recovered = _service.FinalizeOrphanedSessions(_dbPath);

            Assert.Equal(0, recovered);
            var session = _db.GetSession(sessionId);
            Assert.Equal(end, session!.SessionEnd);
            Assert.False(session.AutoFinalized);
        }

        [Fact]
        public void MultipleOrphanedSessions_AllAreRecovered() {
            var sid1 = Guid.NewGuid().ToString();
            var sid2 = Guid.NewGuid().ToString();
            var start = new DateTime(2025, 1, 15, 21, 0, 0);
            _db.CreateSession(new SessionRecord { SessionId = sid1, SessionStart = start, ProfileName = "Test" });
            _db.SaveImageRecord(TestDataFactory.MakeImage(sid1, timestamp: start.AddMinutes(30)));
            _db.CreateSession(new SessionRecord { SessionId = sid2, SessionStart = start.AddHours(5), ProfileName = "Test" });
            _db.SaveImageRecord(TestDataFactory.MakeImage(sid2, timestamp: start.AddHours(5).AddMinutes(45)));

            var recovered = _service.FinalizeOrphanedSessions(_dbPath);

            Assert.Equal(2, recovered);
            Assert.True(_db.GetSession(sid1)!.AutoFinalized);
            Assert.True(_db.GetSession(sid2)!.AutoFinalized);
        }

        // ── The currently running session must never be touched ────────────

        [Fact]
        public void CurrentlyLiveSession_IsNeverFinalized() {
            _service.StartSession("Test");
            var liveSessionId = _service.GetCurrentSessionId();
            Assert.NotNull(liveSessionId);

            var recovered = _service.FinalizeOrphanedSessions(_dbPath);

            Assert.Equal(0, recovered);
            var session = _db.GetSession(liveSessionId!);
            Assert.False(session!.AutoFinalized);
            Assert.NotNull(_service.GetCurrentSessionId());
        }

        [Fact]
        public void LiveSessionPlusUnrelatedOrphan_OnlyOrphanIsFinalized() {
            _service.StartSession("Test");
            var liveSessionId = _service.GetCurrentSessionId();

            var orphanId = Guid.NewGuid().ToString();
            var start    = new DateTime(2025, 1, 10, 21, 0, 0);
            _db.CreateSession(new SessionRecord { SessionId = orphanId, SessionStart = start, ProfileName = "Test" });
            _db.SaveImageRecord(TestDataFactory.MakeImage(orphanId, timestamp: start.AddHours(2)));

            var recovered = _service.FinalizeOrphanedSessions(_dbPath);

            Assert.Equal(1, recovered);
            Assert.True(_db.GetSession(orphanId)!.AutoFinalized);
            Assert.False(_db.GetSession(liveSessionId!)!.AutoFinalized);
        }
    }
}
