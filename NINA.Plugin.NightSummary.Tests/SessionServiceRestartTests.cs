using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Session;
using NINA.Plugin.NightSummary.Tests.Mocks;
using System;
using System.IO;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Tests for SequenceFinished behavior: the event must NOT end an active session.
    /// Only the End Session instruction (EndSession()) terminates a session.
    /// This design survives WhenUnsafe cancel-and-restart patterns without losing data.
    /// </summary>
    public class SessionServiceRestartTests : IDisposable {

        private readonly MockSequenceMediator _sequenceMediator = new();
        private readonly MockImageSaveMediator _imageSaveMediator = new();
        private readonly MockProfileService _profileService = new();
        private readonly MockSafetyMonitorMediator _safetyMonitorMediator = new();
        private readonly MockFocuserMediator _focuserMediator = new();
        private readonly MockTelescopeMediator _telescopeMediator = new();
        private readonly MockCameraMediator _cameraMediator = new();
        private readonly string _dbPath;
        private readonly string _settingsPath;
        // See SessionReplayRunner — SessionService reads from SettingsManager.Instance,
        // not from this test's isolated SettingsManager. Redirect the singleton so the
        // disabled delivery flags below are actually observed.
        private readonly IDisposable _settingsOverride;
        private readonly SessionService _service;

        public SessionServiceRestartTests() {
            _dbPath       = Path.Combine(Path.GetTempPath(), $"ns_restart_{Guid.NewGuid():N}.sqlite");
            _settingsPath = Path.Combine(Path.GetTempPath(), $"ns_restart_settings_{Guid.NewGuid():N}.json");

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
                databasePath: _dbPath);
        }

        public void Dispose() {
            // Drain pending report tasks before releasing the settings override — see
            // SessionReplayRunner.Dispose for the race this prevents. These tests don't
            // end sessions with content so this is normally a no-op, but it's a cheap
            // guarantee against future tests in this class growing into the same hazard.
            try {
                _service?.WaitForPendingReportsAsync(TimeSpan.FromSeconds(10))
                         .GetAwaiter().GetResult();
            } catch { }
            _settingsOverride?.Dispose();
            if (File.Exists(_dbPath))       File.Delete(_dbPath);
            if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void FireSequenceFinished() => _sequenceMediator.FireSequenceFinished();
        private void FireSequenceStarting() => _sequenceMediator.FireSequenceStarting();

        // ── Tests ─────────────────────────────────────────────────────────────

        [Fact]
        public void SequenceFinished_WhenNoActiveSession_DoesNothing() {
            // No StartSession called — should be a no-op
            FireSequenceFinished();
            Assert.Null(_service.GetCurrentSessionId());
        }

        [Fact]
        public void SequenceFinished_WithActiveSession_DoesNotEndSession() {
            _service.StartSession("Test");
            var sessionId = _service.GetCurrentSessionId();
            Assert.NotNull(sessionId);

            FireSequenceFinished();

            // SequenceFinished must not end the session — only End Session instruction does
            Assert.Equal(sessionId, _service.GetCurrentSessionId());
        }

        [Fact]
        public void MultipleSequenceFinished_WithActiveSession_SessionSurvivesAll() {
            _service.StartSession("Test");
            var sessionId = _service.GetCurrentSessionId();

            // Simulates multiple WhenUnsafe cancel-restart cycles
            FireSequenceFinished();
            FireSequenceStarting();
            FireSequenceFinished();
            FireSequenceStarting();
            FireSequenceFinished();

            Assert.Equal(sessionId, _service.GetCurrentSessionId());
        }

        [Fact]
        public void SequenceFinished_ThenStarting_SessionIdUnchanged() {
            _service.StartSession("Test");
            var sessionId = _service.GetCurrentSessionId();

            FireSequenceFinished();
            FireSequenceStarting();

            Assert.Equal(sessionId, _service.GetCurrentSessionId());
        }

        [Fact]
        public void EndSession_WithActiveSession_EndsSession() {
            _service.StartSession("Test");
            Assert.NotNull(_service.GetCurrentSessionId());

            _service.EndSession();

            Assert.Null(_service.GetCurrentSessionId());
        }

        [Fact]
        public void EndSession_AfterSequenceFinished_EndsSession() {
            _service.StartSession("Test");

            // SequenceFinished fires but session stays alive
            FireSequenceFinished();
            Assert.NotNull(_service.GetCurrentSessionId());

            // End Session instruction is the sole authority
            _service.EndSession();
            Assert.Null(_service.GetCurrentSessionId());
        }
    }
}
