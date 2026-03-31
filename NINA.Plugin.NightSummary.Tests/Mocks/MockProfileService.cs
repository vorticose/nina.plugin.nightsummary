using NINA.Core.Utility;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;
using System.ComponentModel;
using System.Globalization;

namespace NINA.Plugin.NightSummary.Tests.Mocks {

    /// <summary>
    /// Mock IProfileService for replay testing.
    /// Night Summary reads ActiveProfile properties: Name, Id, TelescopeSettings.FocalLength,
    /// AstrometrySettings.Latitude/Longitude, CameraSettings.PixelSize,
    /// FramingAssistantSettings.CameraWidth/CameraHeight, FilterWheelSettings.FilterWheelFilters.
    /// All other profile properties return null (Night Summary uses ?. everywhere).
    /// </summary>
    internal class MockProfileService : IProfileService {

        public MockProfile Profile { get; } = new MockProfile();

        // ── Used by Night Summary ────────────────────────────────────────────
        public IProfile ActiveProfile => Profile;

        // ── Not used by Night Summary ────────────────────────────────────────
        public bool ProfileWasSpecifiedFromCommandLineArgs => false;
        public AsyncObservableCollection<ProfileMeta> Profiles => new AsyncObservableCollection<ProfileMeta>();
        public bool Clone(ProfileMeta profileInfos) => throw new NotImplementedException();
        public void Add() => throw new NotImplementedException();
        public bool SelectProfile(ProfileMeta profileInfo) => throw new NotImplementedException();
        public bool RemoveProfile(ProfileMeta profileInfo) => throw new NotImplementedException();
        public void ChangeLocale(CultureInfo language) { }
        public void ChangeLatitude(double latitude) { }
        public void ChangeLongitude(double longitude) { }
        public void ChangeElevation(double elevation) { }
        public void ChangeHorizon(string horizonFilePath) { }
        public void Release() { }
        public event EventHandler LocaleChanged;
        public event EventHandler LocationChanged;
        public event EventHandler BeforeProfileChanging;
        public event EventHandler ProfileChanged;
        public event EventHandler HorizonChanged;
    }

    /// <summary>
    /// Minimal IProfile implementation. Only the properties Night Summary reads
    /// have real backing values. Everything else returns null.
    /// </summary>
    internal class MockProfile : IProfile {

        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Test Profile";
        public string Description => "";
        public string Location => "";
        public DateTime LastUsed => DateTime.MinValue;

        // ── Settings Night Summary reads ─────────────────────────────────────
        public ITelescopeSettings TelescopeSettings { get; set; } = new MockTelescopeSettings();
        public IAstrometrySettings AstrometrySettings { get; set; } = new MockAstrometrySettings();
        public ICameraSettings CameraSettings { get; set; } = new MockCameraSettings();
        public IFramingAssistantSettings FramingAssistantSettings { get; set; } = new MockFramingAssistantSettings();
        public IFilterWheelSettings FilterWheelSettings { get; set; } = null; // null is fine, NS uses ?.

        // ── Settings Night Summary doesn't read ──────────────────────────────
        public IApplicationSettings ApplicationSettings { get; set; } = null;
        public IColorSchemaSettings ColorSchemaSettings { get; set; } = null;
        public IDomeSettings DomeSettings { get; set; } = null;
        public IFlatWizardSettings FlatWizardSettings { get; set; } = null;
        public IFocuserSettings FocuserSettings { get; set; } = null;
        public IGuiderSettings GuiderSettings { get; set; } = null;
        public IImageFileSettings ImageFileSettings { get; set; } = null;
        public IImageSettings ImageSettings { get; set; } = null;
        public IMeridianFlipSettings MeridianFlipSettings { get; set; } = null;
        public IPlanetariumSettings PlanetariumSettings { get; set; } = null;
        public IPlateSolveSettings PlateSolveSettings { get; set; } = null;
        public IRotatorSettings RotatorSettings { get; set; } = null;
        public IFlatDeviceSettings FlatDeviceSettings { get; set; } = null;
        public ISequenceSettings SequenceSettings { get; set; } = null;
        public ISwitchSettings SwitchSettings { get; set; } = null;
        public IWeatherDataSettings WeatherDataSettings { get; set; } = null;
        public ISnapShotControlSettings SnapShotControlSettings { get; set; } = null;
        public ISafetyMonitorSettings SafetyMonitorSettings { get; set; } = null;
        public IPluginSettings PluginSettings { get; set; } = null;
        public IGnssSettings GnssSettings { get; set; } = null;
        public IAlpacaSettings AlpacaSettings { get; set; } = null;
        public IImageHistorySettings ImageHistorySettings { get; set; } = null;
        public IDockPanelSettings DockPanelSettings { get; set; } = null;

        public void Save() { }
        public void Dispose() { }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    // ── Minimal settings stubs ───────────────────────────────────────────────
    // Only the properties Night Summary reads have meaningful defaults.
    // All settings interfaces inherit ISettings : INotifyPropertyChanged.

    internal class MockTelescopeSettings : ITelescopeSettings {
        public double FocalLength { get; set; } = 714;
        public string Name { get; set; } = "Test Telescope";
        public string MountName { get; set; } = "";
        public double FocalRatio { get; set; } = 0;
        public string Id { get; set; } = "";
        public string LastDeviceName { get; set; } = "";
        public int SettleTime { get; set; } = 0;
        public string SnapPortStart { get; set; } = "";
        public string SnapPortStop { get; set; } = "";
        public bool NoSync { get; set; } = false;
        public bool TimeSync { get; set; } = false;
        public bool PrimaryReversed { get; set; } = false;
        public bool SecondaryReversed { get; set; } = false;
        public NINA.Core.Enum.TelescopeLocationSyncDirection TelescopeLocationSyncDirection { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    internal class MockAstrometrySettings : IAstrometrySettings {
        public double Latitude { get; set; } = 40.7128;
        public double Longitude { get; set; } = -74.0060;
        public double Elevation { get; set; } = 0;
        public string HorizonFilePath { get; set; } = "";
        public NINA.Core.Model.CustomHorizon Horizon { get; set; } = null;
        public string Observer { get; set; } = "";
        public string Observatory { get; set; } = "";
        public string Site { get; set; } = "";
        public event PropertyChangedEventHandler PropertyChanged;
    }

    internal class MockCameraSettings : ICameraSettings {
        public double PixelSize { get; set; } = 3.76;

        // ── Everything below: required by interface, not used by Night Summary ──
        public double BitDepth { get; set; } = 16;
        public NINA.Core.Enum.CameraBulbModeEnum BulbMode { get; set; }
        public string Id { get; set; } = "";
        public string LastDeviceName { get; set; } = "";
        public NINA.Core.Enum.RawConverterEnum RawConverter { get; set; }
        public string SerialPort { get; set; } = "";
        public double MinFlatExposureTime { get; set; } = 0;
        public double MaxFlatExposureTime { get; set; } = 0;
        public string FileCameraFolder { get; set; } = "";
        public bool FileCameraUseBulbMode { get; set; } = false;
        public bool FileCameraIsBayered { get; set; } = false;
        public string FileCameraExtension { get; set; } = "";
        public bool FileCameraAlwaysListen { get; set; } = false;
        public int FileCameraDownloadDelay { get; set; } = 0;
        public NINA.Core.Enum.BayerPatternEnum BayerPattern { get; set; }
        public bool FLIEnableFloodFlush { get; set; } = false;
        public bool FLIEnableSnapshotFloodFlush { get; set; } = false;
        public double FLIFloodDuration { get; set; } = 0;
        public uint FLIFlushCount { get; set; } = 0;
        public NINA.Core.Model.Equipment.BinningMode FLIFloodBin { get; set; }
        public bool BitScaling { get; set; } = false;
        public double CoolingDuration { get; set; } = 0;
        public double WarmingDuration { get; set; } = 0;
        public double? Temperature { get; set; } = null;
        public short? BinningX { get; set; } = null;
        public short? BinningY { get; set; } = null;
        public int? Gain { get; set; } = null;
        public int? Offset { get; set; } = null;
        public int? USBLimit { get; set; } = null;
        public short? ReadoutMode { get; set; } = null;
        public short? ReadoutModeForSnapImages { get; set; } = null;
        public short? ReadoutModeForNormalImages { get; set; } = null;
        public bool QhyIncludeOverscan { get; set; } = false;
        public int Timeout { get; set; } = 0;
        public bool? DewHeaterOn { get; set; } = null;
        public bool ASCOMAllowUnevenPixelDimension { get; set; } = false;
        public double MirrorLockupDelay { get; set; } = 0;
        public bool? BinAverageEnabled { get; set; } = null;
        public bool? TrackingCameraASCOMServerEnabled { get; set; } = null;
        public string TrackingCameraASCOMServerPipeName { get; set; } = "";
        public bool? TrackingCameraASCOMServerLoggingEnabled { get; set; } = null;
        public bool SBIGUseExternalCcdTracker { get; set; } = false;
        public ushort? AtikGainPreset { get; set; } = null;
        public ushort? AtikExposureSpeed { get; set; } = null;
        public int? AtikWindowHeaterPowerLevel { get; set; } = null;
        public bool TouptekAlikeUltraMode { get; set; } = false;
        public bool TouptekAlikeHighFullwell { get; set; } = false;
        public bool TouptekAlikeLEDLights { get; set; } = false;
        public int TouptekAlikeDewHeaterStrength { get; set; } = 0;
        public int GenericCameraDewHeaterStrength { get; set; } = 0;
        public int GenericCameraFanSpeed { get; set; } = 0;
        public bool? ZwoAsiMonoBinMode { get; set; } = null;
        public bool ASCOMCreate32BitData { get; set; } = false;
        public bool BadPixelCorrection { get; set; } = false;
        public int BadPixelCorrectionThreshold { get; set; } = 0;
        public event PropertyChangedEventHandler PropertyChanged;
    }

    internal class MockFramingAssistantSettings : IFramingAssistantSettings {
        public int CameraWidth { get; set; } = 4656;
        public int CameraHeight { get; set; } = 3520;

        // ── Required by interface, not used by Night Summary ─────────────────
        public double FieldOfView { get; set; } = 0;
        public double Opacity { get; set; } = 0;
        public NINA.Core.Enum.SkySurveySource LastSelectedImageSource { get; set; }
        public double LastRotationAngle { get; set; } = 0;
        public bool SaveImageInOfflineCache { get; set; } = false;
        public bool AnnotateConstellationBoundaries { get; set; } = false;
        public bool AnnotateConstellations { get; set; } = false;
        public bool AnnotateDSO { get; set; } = false;
        public bool AnnotateGrid { get; set; } = false;
        public System.Collections.Generic.List<string> DisabledCatalogues { get; set; } = new();
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
