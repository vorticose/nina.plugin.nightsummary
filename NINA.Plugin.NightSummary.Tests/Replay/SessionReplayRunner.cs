using NINA.Equipment.Equipment.MyCamera;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Session;
using NINA.Plugin.NightSummary.Tests.Mocks;
using NINA.Plugin.NightSummary.Tests.Replay.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NINA.Plugin.NightSummary.Tests.Replay {

    /// <summary>
    /// Orchestrates the replay of a recorded NINA session through Night Summary's
    /// real pipeline. Loads a recording file, configures mock mediators, advances
    /// the clock for each event, and fires events through the mocks.
    ///
    /// Usage:
    ///   using var runner = new SessionReplayRunner("path/to/recording.json");
    ///   runner.ConfigureSettings(s => { s.SaveReportLocally = false; });
    ///   var result = runner.Run();
    ///   Assert.Equal(10, result.GetImages().Count);
    /// </summary>
    internal class SessionReplayRunner : IDisposable {

        private readonly SessionRecording _recording;
        private readonly string _dbPath;
        private readonly string _settingsPath;

        private readonly MockImageSaveMediator _imageSaveMediator = new();
        private readonly MockProfileService _profileService = new();
        private readonly MockSafetyMonitorMediator _safetyMonitorMediator = new();
        private readonly MockFocuserMediator _focuserMediator = new();
        private readonly MockTelescopeMediator _telescopeMediator = new();
        private readonly MockCameraMediator _cameraMediator = new();
        private readonly MockSequenceMediator _sequenceMediator = new();

        private SessionService _service;
        // Redirects SettingsManager.Instance.Current to the isolated test settings
        // (with all delivery channels disabled) for the lifetime of this runner.
        // Without this, SessionService reads the production settings.json on the host
        // and will actually fire emails + Discord with real credentials when EndSession
        // triggers GenerateAndSendAsync.
        private readonly IDisposable _settingsOverride;

        private static readonly JsonSerializerOptions _jsonOptions = new() {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public SessionReplayRunner(string recordingPath) {
            var json = File.ReadAllText(recordingPath);
            _recording = JsonSerializer.Deserialize<SessionRecording>(json, _jsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize recording");

            _dbPath = Path.Combine(Path.GetTempPath(), $"ns_replay_{Guid.NewGuid():N}.sqlite");
            _settingsPath = Path.Combine(Path.GetTempPath(), $"ns_replay_settings_{Guid.NewGuid():N}.json");

            // Configure mocks from initial state
            ConfigureMocksFromInitialState(_recording.InitialState);

            // Set up clock for replay
            Clock.DisableSkipPolling = true;

            // Initialize settings with defaults
            var settingsMgr = new SettingsManager(_settingsPath, attemptMigration: false);
            settingsMgr.Load();
            // Disable all delivery channels by default — tests opt in as needed.
            // CRITICAL: SessionService reads from SettingsManager.Instance.Current
            // (the static singleton), not from this isolated manager. Redirect the
            // singleton below via UseInstanceForTesting so these disabled flags
            // are actually what SessionService sees when EndSession fires
            // GenerateAndSendAsync — otherwise the test host's real settings.json
            // is read and real emails/Discord messages are sent.
            settingsMgr.Current.SaveReportLocally = false;
            settingsMgr.Current.EmailEnabled = false;
            settingsMgr.Current.DiscordEnabled = false;
            settingsMgr.Current.PushoverEnabled = false;
            settingsMgr.Save();
            _settingsOverride = SettingsManager.UseInstanceForTesting(settingsMgr);
        }

        /// <summary>
        /// Overrides camera info for recordings where the camera wasn't connected
        /// at capture start. Sets both the mock camera mediator and profile settings
        /// so SessionService.StartSession captures correct hardware info.
        /// </summary>
        public void OverrideCameraInfo(int xSize, int ySize, double pixelSize) {
            _cameraMediator.ConfiguredInfo = new CameraInfo {
                XSize = xSize,
                YSize = ySize,
                PixelSize = pixelSize
            };
            var camSettings = _profileService.Profile.CameraSettings as MockCameraSettings;
            if (camSettings != null) camSettings.PixelSize = pixelSize;
            var framingSettings = _profileService.Profile.FramingAssistantSettings as MockFramingAssistantSettings;
            if (framingSettings != null) {
                framingSettings.CameraWidth = xSize;
                framingSettings.CameraHeight = ySize;
            }
        }

        /// <summary>
        /// Allows tests to configure settings before replay.
        /// </summary>
        public void ConfigureSettings(Action<NightSummarySettings> configure) {
            var settingsMgr = new SettingsManager(_settingsPath, attemptMigration: false);
            settingsMgr.Load();
            configure(settingsMgr.Current);
            settingsMgr.Save();
        }

        /// <summary>
        /// Replays the recorded session through Night Summary's real pipeline.
        /// Returns a ReplayResult for assertions.
        /// </summary>
        public ReplayResult Run() {
            // Construct real SessionService with mock mediators and isolated database
            _service = new SessionService(
                _imageSaveMediator,
                _profileService,
                _safetyMonitorMediator,
                _focuserMediator,
                _telescopeMediator,
                _cameraMediator,
                _sequenceMediator,
                null, // filterWheelMediator — not needed for replay
                null, // rotatorMediator
                null, // guiderMediator
                null, // domeMediator
                null, // flatDeviceMediator
                null, // weatherDataMediator
                null, // switchMediator
                null, // messageBroker — not needed for replay
                _dbPath);

            // Set clock to the first event timestamp (or recordedAt) for session start
            var sessionStartTime = _recording.Events.Count > 0
                ? _recording.Events[0].Timestamp.AddSeconds(-5)
                : _recording.RecordedAt;
            Clock.Now = () => sessionStartTime;

            // Start session
            _service.StartSession(_recording.InitialState.ProfileName);
            var sessionId = _service.GetCurrentSessionId();

            // Replay events in order
            foreach (var evt in _recording.Events) {
                // Advance clock to this event's timestamp
                var eventTime = evt.Timestamp;
                Clock.Now = () => eventTime;

                switch (evt.Type) {
                    case "ImageSaved":
                        ReplayImageSaved(evt);
                        break;
                    case "AutoFocusComplete":
                        ReplayAutoFocus(evt);
                        break;
                    case "SafetyStateChanged":
                        ReplaySafetyState(evt);
                        break;
                    case "MeridianFlip":
                        ReplayMeridianFlip(evt);
                        break;
                }
            }

            // Set clock to after the last event for session end
            var sessionEndTime = _recording.Events.Count > 0
                ? _recording.Events[^1].Timestamp.AddSeconds(5)
                : sessionStartTime.AddHours(1);
            Clock.Now = () => sessionEndTime;

            // End session (triggers report generation on Task.Run — we don't await it here)
            _service.EndSession();

            return new ReplayResult {
                SessionId = sessionId,
                Database = _service.Database
            };
        }

        private void ReplayImageSaved(RecordingEvent evt) {
            var data = JsonSerializer.Deserialize<ImageSavedData>(evt.Data.GetRawText(), _jsonOptions);
            if (data == null) return;
            var args = EventArgsBuilder.BuildImageSavedEventArgs(data);
            _imageSaveMediator.FireImageSaved(args);
        }

        private void ReplayAutoFocus(RecordingEvent evt) {
            var data = JsonSerializer.Deserialize<AutoFocusData>(evt.Data.GetRawText(), _jsonOptions);
            if (data == null) return;
            var info = EventArgsBuilder.BuildAutoFocusInfo(data, evt.Timestamp);
            _focuserMediator.FireAutoFocusComplete(info);
        }

        private void ReplaySafetyState(RecordingEvent evt) {
            var data = JsonSerializer.Deserialize<SafetyStateData>(evt.Data.GetRawText(), _jsonOptions);
            if (data == null) return;
            _safetyMonitorMediator.PushSafetyState(data.IsSafe);
        }

        private void ReplayMeridianFlip(RecordingEvent evt) {
            var data = JsonSerializer.Deserialize<MeridianFlipData>(evt.Data.GetRawText(), _jsonOptions);
            if (data == null) return;
            _telescopeMediator.FireMeridianFlip(data.Success, data.RaHours, data.DecDegrees).Wait();
        }

        private void ConfigureMocksFromInitialState(RecordingInitialState state) {
            // Camera info
            _cameraMediator.ConfiguredInfo = new CameraInfo {
                XSize = state.CameraXSize,
                YSize = state.CameraYSize,
                PixelSize = state.PixelSize
            };

            // Profile settings
            _profileService.Profile.Name = state.ProfileName;
            if (Guid.TryParse(state.ProfileId, out var guid))
                _profileService.Profile.Id = guid;

            var telSettings = _profileService.Profile.TelescopeSettings as MockTelescopeSettings;
            if (telSettings != null) telSettings.FocalLength = state.FocalLength;

            var astroSettings = _profileService.Profile.AstrometrySettings as MockAstrometrySettings;
            if (astroSettings != null) {
                astroSettings.Latitude = state.Latitude;
                astroSettings.Longitude = state.Longitude;
            }

            var camSettings = _profileService.Profile.CameraSettings as MockCameraSettings;
            if (camSettings != null) camSettings.PixelSize = state.PixelSize;

            var framingSettings = _profileService.Profile.FramingAssistantSettings as MockFramingAssistantSettings;
            if (framingSettings != null) {
                framingSettings.CameraWidth = state.CameraXSize;
                framingSettings.CameraHeight = state.CameraYSize;
            }
        }

        public void Dispose() {
            // Drain any in-flight report-generation tasks BEFORE releasing the settings
            // override. EndSession kicks off report generation on Task.Run; if Dispose
            // runs before that task reads SettingsManager.Instance.Current.EmailEnabled
            // (which is the gate that short-circuits real sends), the override will be
            // gone and the task will see the production singleton → real email/Discord
            // send. 10s is generous: the only work in-flight is local HTML generation
            // since senders are disabled by config.
            try {
                _service?.WaitForPendingReportsAsync(TimeSpan.FromSeconds(10))
                         .GetAwaiter().GetResult();
            } catch { /* individual tasks log their own errors */ }

            _settingsOverride?.Dispose();
            Clock.Reset();
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
            try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch { }
        }
    }
}
