using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Equipment.MySafetyMonitor;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.SessionCapture.Models;
using NINA.Plugin.SessionCapture.Serialization;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NINA.Plugin.SessionCapture {

    [Export(typeof(CaptureService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class CaptureService : IFocuserConsumer, ISafetyMonitorConsumer {

        private readonly IImageSaveMediator imageSaveMediator;
        private readonly IProfileService profileService;
        private readonly ISafetyMonitorMediator safetyMonitorMediator;
        private readonly IFocuserMediator focuserMediator;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly ICameraMediator cameraMediator;

        private CaptureRecording currentRecording;
        private bool isCapturing;
        private bool? lastIsSafe;

        private static readonly string OutputDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "SessionCapture");

        private static readonly JsonSerializerOptions JsonOptions = CaptureJson.Options;

        [ImportingConstructor]
        public CaptureService(
            IImageSaveMediator imageSaveMediator,
            IProfileService profileService,
            ISafetyMonitorMediator safetyMonitorMediator,
            IFocuserMediator focuserMediator,
            ITelescopeMediator telescopeMediator,
            ICameraMediator cameraMediator,
            ISequenceMediator sequenceMediator) {

            this.imageSaveMediator = imageSaveMediator;
            this.profileService = profileService;
            this.safetyMonitorMediator = safetyMonitorMediator;
            this.focuserMediator = focuserMediator;
            this.telescopeMediator = telescopeMediator;
            this.cameraMediator = cameraMediator;
        }

        public bool IsCapturing => isCapturing;

        public void StartCapture() {
            if (isCapturing) {
                Logger.Warning("SessionCapture: Already capturing — ignoring duplicate start");
                return;
            }

            currentRecording = new CaptureRecording {
                RecordedAt = DateTime.Now,
                NinaVersion = CoreUtil.Version,
                InitialState = CaptureInitialState()
            };
            lastIsSafe = null;

            // Subscribe to all mediators
            imageSaveMediator.ImageSaved += OnImageSaved;
            telescopeMediator.AfterMeridianFlip += OnAfterMeridianFlip;
            safetyMonitorMediator?.RegisterConsumer(this);
            focuserMediator?.RegisterConsumer(this);

            isCapturing = true;
            var state = currentRecording.InitialState;
            Logger.Info($"SessionCapture: Recording started — profile={state.ProfileName}, camera={state.CameraName} ({state.CameraXSize}x{state.CameraYSize}), focal={state.FocalLength}mm");
            Logger.Info($"SessionCapture: Subscribed to ImageSaved, AfterMeridianFlip, SafetyMonitor, Focuser mediators");
            Logger.Info($"SessionCapture: Output will be saved to {OutputDir}");
        }

        public void StopCapture() {
            if (!isCapturing) return;
            var eventCount = currentRecording?.Events.Count ?? 0;
            Logger.Info($"SessionCapture: Stopping capture — {eventCount} events recorded");

            // Unsubscribe
            imageSaveMediator.ImageSaved -= OnImageSaved;
            telescopeMediator.AfterMeridianFlip -= OnAfterMeridianFlip;
            safetyMonitorMediator?.RemoveConsumer(this);
            focuserMediator?.RemoveConsumer(this);

            isCapturing = false;

            // Write recording to disk
            SaveRecording();
            currentRecording = null;
        }

        // ── Event handlers ───────────────────────────────────────────────────

        private void OnImageSaved(object sender, ImageSavedEventArgs e) {
            if (!isCapturing) return;
            try {
                // Serialize the full event — all metadata, star detection, image stats, path.
                // Custom JsonConverters handle Coordinates, RMS, IStarDetectionAnalysis,
                // IImageStatistics, and skip BitmapSource (pixel data).
                var data = new ImageSavedSnapshot {
                    MetaData             = e.MetaData,
                    StarDetectionAnalysis = e.StarDetectionAnalysis,
                    Statistics           = e.Statistics,
                    PathToImage          = e.PathToImage?.ToString(),
                    FileType             = e.FileType.ToString(),
                    IsBayered            = e.IsBayered,
                    Duration             = e.Duration,
                    Filter               = e.Filter
                };

                AddEvent("ImageSaved", data);
                var target = e.MetaData?.Target?.Name ?? "Unknown";
                var filter = e.MetaData?.FilterWheel?.Filter ?? "?";
                var hfr    = e.StarDetectionAnalysis?.HFR ?? 0;
                var stars  = e.StarDetectionAnalysis?.DetectedStars ?? 0;
                Logger.Info($"SessionCapture: Recorded ImageSaved — {target}/{filter}, HFR={hfr:F2}, Stars={stars}");
            } catch (Exception ex) {
                Logger.Error($"SessionCapture: Failed to record ImageSaved event. {ex.Message}");
            }
        }

        private Task OnAfterMeridianFlip(object sender, AfterMeridianFlipEventArgs e) {
            if (!isCapturing) return Task.CompletedTask;
            try {
                var data = new MeridianFlipEventData {
                    Success = e.Success,
                    RaHours = e.Target?.RA ?? 0,
                    DecDegrees = e.Target?.Dec ?? 0
                };
                AddEvent("MeridianFlip", data);
                Logger.Info($"SessionCapture: Recorded MeridianFlip — success={e.Success}");
            } catch (Exception ex) {
                Logger.Error($"SessionCapture: Failed to record MeridianFlip event. {ex.Message}");
            }
            return Task.CompletedTask;
        }

        // ── ISafetyMonitorConsumer ────────────────────────────────────────────

        public void UpdateDeviceInfo(SafetyMonitorInfo deviceInfo) {
            if (!isCapturing || deviceInfo == null) return;
            bool isSafe = deviceInfo.IsSafe;
            if (lastIsSafe.HasValue && lastIsSafe.Value == isSafe) return;
            lastIsSafe = isSafe;

            var data = new SafetyStateEventData { IsSafe = isSafe };
            AddEvent("SafetyStateChanged", data);
            Logger.Info($"SessionCapture: Recorded SafetyStateChanged — isSafe={isSafe}");
        }

        // ── IFocuserConsumer ─────────────────────────────────────────────────

        public void UpdateEndAutoFocusRun(AutoFocusInfo info) {
            if (!isCapturing) return;
            var data = new AutoFocusEventData {
                Filter = info?.Filter ?? "N/A",
                Temperature = info?.Temperature ?? 0,
                Position = info?.Position ?? 0
            };
            AddEvent("AutoFocusComplete", data);
            Logger.Info($"SessionCapture: Recorded AutoFocusComplete — {data.Filter}");
        }

        public void UpdateDeviceInfo(FocuserInfo deviceInfo) { }
        public void UpdateUserFocused(FocuserInfo info) { }
        public void AutoFocusRunStarting() { }
        public void NewAutoFocusPoint(OxyPlot.DataPoint dataPoint) { }
        public void Dispose() { if (isCapturing) StopCapture(); }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void AddEvent(string type, object data) {
            currentRecording?.Events.Add(new CaptureEvent {
                Timestamp = DateTime.Now,
                Type = type,
                Data = data
            });
        }

        private CaptureInitialState CaptureInitialState() {
            var camInfo = cameraMediator?.GetInfo();
            var profile = profileService?.ActiveProfile;

            var filters = profile?.FilterWheelSettings?.FilterWheelFilters?
                .Select(f => f.Name).ToList() ?? new System.Collections.Generic.List<string>();

            // Camera info from live mediator; fall back to profile settings if camera
            // isn't connected yet (e.g. capture starts before camera connect in sequence)
            var pixelSize = camInfo?.PixelSize ?? 0;
            var camX      = camInfo?.XSize ?? 0;
            var camY      = camInfo?.YSize ?? 0;
            var camName   = camInfo?.Name ?? "";

            if (pixelSize <= 0)
                pixelSize = profile?.CameraSettings?.PixelSize ?? 0;
            if (camX <= 0)
                camX = profile?.FramingAssistantSettings?.CameraWidth ?? 0;
            if (camY <= 0)
                camY = profile?.FramingAssistantSettings?.CameraHeight ?? 0;

            return new CaptureInitialState {
                ProfileName = profile?.Name ?? "Unknown",
                ProfileId = profile?.Id.ToString() ?? "",
                FocalLength = profile?.TelescopeSettings?.FocalLength ?? 0,
                Latitude = profile?.AstrometrySettings?.Latitude ?? 0,
                Longitude = profile?.AstrometrySettings?.Longitude ?? 0,
                PixelSize = pixelSize,
                CameraXSize = camX,
                CameraYSize = camY,
                CameraName = string.IsNullOrEmpty(camName) ? "Unknown" : camName,
                Filters = filters
            };
        }

        private void SaveRecording() {
            try {
                Directory.CreateDirectory(OutputDir);
                var filename = $"capture_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json";
                var path = Path.Combine(OutputDir, filename);
                var json = JsonSerializer.Serialize(currentRecording, JsonOptions);
                File.WriteAllText(path, json);
                Logger.Info($"SessionCapture: Recording saved to {path} ({currentRecording.Events.Count} events)");
            } catch (Exception ex) {
                Logger.Error($"SessionCapture: Failed to save recording. {ex.Message}");
            }
        }

    }
}
