using System;
using System.Collections.Generic;

namespace NINA.Plugin.SessionCapture.Models {

    /// <summary>
    /// Root container for a captured session recording.
    /// This format is consumed by the Night Summary replay test harness.
    /// </summary>
    public class CaptureRecording {
        public int FormatVersion { get; set; } = 1;
        public string NinaVersion { get; set; } = "";
        public string SessionCaptureVersion { get; set; } = "1.0.0";
        public DateTime RecordedAt { get; set; }
        public CaptureInitialState InitialState { get; set; } = new();
        public List<CaptureEvent> Events { get; set; } = new();
    }

    public class CaptureInitialState {
        public string ProfileName { get; set; } = "";
        public string ProfileId { get; set; } = "";
        public double FocalLength { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double PixelSize { get; set; }
        public int CameraXSize { get; set; }
        public int CameraYSize { get; set; }
        public string CameraName { get; set; } = "";
        public List<string> Filters { get; set; } = new();
    }

    public class CaptureEvent {
        public DateTime Timestamp { get; set; }
        public string Type { get; set; } = "";
        public object Data { get; set; }
    }

    // ── Event data classes ───────────────────────────────────────────────────

    public class ImageSavedEventData {
        public string ImageType { get; set; } = "";
        public string TargetName { get; set; } = "";
        public string Filter { get; set; } = "";
        public double ExposureTime { get; set; }
        public double HFR { get; set; }
        public double FWHM { get; set; }
        public double Eccentricity { get; set; }
        public int DetectedStars { get; set; }
        public double GuidingRmsTotal { get; set; }
        public double GuidingScale { get; set; }
        public double RaHours { get; set; }
        public double DecDegrees { get; set; }
        public int Gain { get; set; }
        public int Offset { get; set; }
        public int BinX { get; set; }
        public double? FocuserTemp { get; set; }
        public int? FocuserPosition { get; set; }
        public double? AmbientTemp { get; set; }
        public double? CameraTemp { get; set; }
        public double? CoolerSetpoint { get; set; }
        public double? RotatorPosition { get; set; }
        public double? Humidity { get; set; }
        public double? DewPoint { get; set; }
        public double? WindSpeed { get; set; }
        public double? Pressure { get; set; }
        public double? Altitude { get; set; }
        public double? Azimuth { get; set; }
        public double? Airmass { get; set; }
        public string SideOfPier { get; set; }
        public string ReadoutMode { get; set; }
        public double? SkyQuality { get; set; }
        public double? CloudCover { get; set; }
        public double? SeeingFWHM { get; set; }
        public double? PositionAngle { get; set; }
    }

    public class AutoFocusEventData {
        public string Filter { get; set; } = "";
        public double Temperature { get; set; }
        public double Position { get; set; }
    }

    public class SafetyStateEventData {
        public bool IsSafe { get; set; }
    }

    public class MeridianFlipEventData {
        public bool Success { get; set; }
        public double RaHours { get; set; }
        public double DecDegrees { get; set; }
    }
}
