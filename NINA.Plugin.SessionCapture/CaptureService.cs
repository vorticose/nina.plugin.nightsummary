using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Equipment.MySafetyMonitor;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.SessionCapture.Models;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
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

        private static readonly JsonSerializerOptions JsonOptions = new() {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

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
                var meta = e.MetaData;
                double guidingScale = meta?.Image?.RecordedRMS?.Scale ?? 1;
                double guidingTotal = (meta?.Image?.RecordedRMS?.Total ?? 0) * guidingScale;

                // Read FWHM/Eccentricity via reflection (Hocus Focus)
                double fwhm = 0, eccentricity = 0;
                var analysis = e.StarDetectionAnalysis;
                if (analysis != null) {
                    var type = analysis.GetType();
                    fwhm = ReadDouble(type.GetProperty("FWHM"), analysis);
                    eccentricity = ReadDouble(type.GetProperty("Eccentricity"), analysis);
                }

                var data = new ImageSavedEventData {
                    ImageType = meta?.Image?.ImageType ?? "",
                    TargetName = meta?.Target?.Name ?? "Unknown",
                    Filter = meta?.FilterWheel?.Filter ?? "None",
                    ExposureTime = meta?.Image?.ExposureTime ?? 0,
                    HFR = e.StarDetectionAnalysis?.HFR ?? 0,
                    FWHM = fwhm,
                    Eccentricity = eccentricity,
                    DetectedStars = e.StarDetectionAnalysis?.DetectedStars ?? 0,
                    GuidingRmsTotal = guidingTotal,
                    GuidingScale = guidingScale,
                    RaHours = meta?.Target?.Coordinates?.RA ?? 0,
                    DecDegrees = meta?.Target?.Coordinates?.Dec ?? 0,
                    Gain = meta?.Camera?.Gain ?? -1,
                    Offset = meta?.Camera?.Offset ?? -1,
                    BinX = meta?.Camera?.BinX ?? 0,
                    FocuserTemp = NullIfNaN(meta?.Focuser?.Temperature),
                    FocuserPosition = meta?.Focuser?.Position,
                    AmbientTemp = NullIfNaN(meta?.WeatherData?.Temperature),
                    CameraTemp = NullIfNaN(meta?.Camera?.Temperature),
                    CoolerSetpoint = NullIfNaN(meta?.Camera?.SetPoint),
                    RotatorPosition = NullIfNaN(meta?.Rotator?.Position),
                    Humidity = NullIfNaN(meta?.WeatherData?.Humidity),
                    DewPoint = NullIfNaN(meta?.WeatherData?.DewPoint),
                    WindSpeed = NullIfNaN(meta?.WeatherData?.WindSpeed),
                    Pressure = NullIfNaN(meta?.WeatherData?.Pressure),
                    Altitude = NullIfNaN(meta?.Telescope?.Altitude),
                    Azimuth = NullIfNaN(meta?.Telescope?.Azimuth),
                    Airmass = NullIfNaN(meta?.Telescope?.Airmass),
                    SideOfPier = meta?.Telescope?.SideOfPier.ToString(),
                    ReadoutMode = string.IsNullOrEmpty(meta?.Camera?.ReadoutModeName) ? null : meta.Camera.ReadoutModeName,
                    SkyQuality = NullIfNaN(meta?.WeatherData?.SkyQuality),
                    CloudCover = NullIfNaN(meta?.WeatherData?.CloudCover),
                    SeeingFWHM = NullIfNaN(meta?.WeatherData?.StarFWHM)
                };

                AddEvent("ImageSaved", data);
                Logger.Info($"SessionCapture: Recorded ImageSaved — {data.TargetName}/{data.Filter}, HFR={data.HFR:F2}, Stars={data.DetectedStars}");
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

            return new CaptureInitialState {
                ProfileName = profile?.Name ?? "Unknown",
                ProfileId = profile?.Id.ToString() ?? "",
                FocalLength = profile?.TelescopeSettings?.FocalLength ?? 0,
                Latitude = profile?.AstrometrySettings?.Latitude ?? 0,
                Longitude = profile?.AstrometrySettings?.Longitude ?? 0,
                PixelSize = camInfo?.PixelSize ?? 0,
                CameraXSize = camInfo?.XSize ?? 0,
                CameraYSize = camInfo?.YSize ?? 0,
                CameraName = camInfo?.Name ?? "Unknown",
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

        private static double ReadDouble(PropertyInfo prop, object obj) {
            if (prop == null) return 0;
            try { return Convert.ToDouble(prop.GetValue(obj)); } catch { return 0; }
        }

        private static double? NullIfNaN(double? value) =>
            value.HasValue && !double.IsNaN(value.Value) ? value : null;
    }
}
