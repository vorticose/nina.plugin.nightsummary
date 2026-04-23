using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Session;
using NINA.Plugin.NightSummary.Tests.Mocks;
using System;
using System.IO;
using System.Threading;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Tests for the SequenceFinished debounce logic that distinguishes transient
    /// cancel-and-restart patterns (e.g. WhenUnsafe) from true manual stops.
    /// Uses a short debounce timeout (200ms) so expiry tests complete quickly.
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

            _service = new SessionService(
                _imageSaveMediator,
                _profileService,
                _safetyMonitorMediator,
                _focuserMediator,
                _telescopeMediator,
                _cameraMediator,
                _sequenceMediator,
                null, null, null, null, null, null, null, null,
                _dbPath,
                restartDebounceMs: 200);
        }

        public void Dispose() {
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
        public void SequenceFinished_WithActiveSession_ArmsDebounce_SessionStillActive() {
            _service.StartSession("Test");
            Assert.NotNull(_service.GetCurrentSessionId());

            FireSequenceFinished();

            // Session should still be alive immediately after SequenceFinished
            Assert.NotNull(_service.GetCurrentSessionId());
        }

        [Fact]
        public void SequenceStarting_WithinDebounceWindow_KeepsSessionAlive() {
            _service.StartSession("Test");
            var sessionId = _service.GetCurrentSessionId();

            FireSequenceFinished();
            FireSequenceStarting();  // simulates WhenUnsafe restart

            // Session must survive the restart
            Assert.Equal(sessionId, _service.GetCurrentSessionId());
        }

        [Fact]
        public void SequenceStarting_WithinDebounceWindow_SubsequentTimerExpiry_DoesNotCleanUp() {
            _service.StartSession("Test");
            var sessionId = _service.GetCurrentSessionId();

            FireSequenceFinished();
            FireSequenceStarting();

            // Wait well past the debounce window — session should still be alive
            Thread.Sleep(400);

            Assert.Equal(sessionId, _service.GetCurrentSessionId());
        }

        [Fact]
        public void DebounceExpiry_WithoutRestart_EndsSession() {
            _service.StartSession("Test");
            Assert.NotNull(_service.GetCurrentSessionId());

            FireSequenceFinished();

            // Wait for debounce to expire (200ms timeout + buffer)
            Thread.Sleep(500);

            Assert.Null(_service.GetCurrentSessionId());
        }

        [Fact]
        public void SequenceFinished_ThenStarting_ThenFinishedAgain_HandlesCleanly() {
            _service.StartSession("Test");
            var sessionId = _service.GetCurrentSessionId();

            // First transient interrupt — session survives
            FireSequenceFinished();
            FireSequenceStarting();
            Assert.Equal(sessionId, _service.GetCurrentSessionId());

            // Second interrupt without restart — debounce expires, session ends
            FireSequenceFinished();
            Thread.Sleep(500);
            Assert.Null(_service.GetCurrentSessionId());
        }

        [Fact]
        public void EndSession_DuringDebounceWindow_CancelsDebounce() {
            _service.StartSession("Test");

            FireSequenceFinished();

            // End Session instruction runs while debounce is pending
            _service.EndSession();

            // Wait past the debounce window — no double-cleanup crash
            Thread.Sleep(500);

            // Session ended by the instruction, not the debounce
            Assert.Null(_service.GetCurrentSessionId());
        }
    }
}
