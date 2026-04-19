using System;
using System.Collections.Generic;
using System.Text.Json;

namespace NINA.Plugin.NightSummary.Tests.Replay.Models {

    /// <summary>
    /// Root container for a recorded NINA session.
    /// Serialized to/from JSON for storage as test fixtures.
    /// </summary>
    internal class SessionRecording {
        public int FormatVersion { get; set; } = 1;
        public string NinaVersion { get; set; } = "";
        public string NightSummaryVersion { get; set; } = "";
        public DateTime RecordedAt { get; set; }
        public RecordingInitialState InitialState { get; set; } = new();
        public List<RecordingEvent> Events { get; set; } = new();
    }

    /// <summary>
    /// Equipment and profile snapshot captured at session start.
    /// </summary>
    internal class RecordingInitialState {
        public string ProfileName { get; set; } = "Test Profile";
        public string ProfileId { get; set; } = Guid.NewGuid().ToString();
        public double FocalLength { get; set; } = 714;
        public double Latitude { get; set; } = 40.7128;
        public double Longitude { get; set; } = -74.0060;
        public double PixelSize { get; set; } = 3.76;
        public int CameraXSize { get; set; } = 4656;
        public int CameraYSize { get; set; } = 3520;
        public string CameraName { get; set; } = "Test Camera";
        public List<string> Filters { get; set; } = new() { "L", "Ha", "OIII", "SII" };
    }

    /// <summary>
    /// A single timestamped event in the recording.
    /// The Data property is a raw JsonElement to allow type-specific deserialization.
    /// </summary>
    internal class RecordingEvent {
        public DateTime Timestamp { get; set; }
        public string Type { get; set; } = "";
        public JsonElement Data { get; set; }
    }

    // ── Event-specific data classes ──────────────────────────────────────────

    internal class ImageSavedData {
        public string ImageType { get; set; } = "LIGHT";
        public string TargetName { get; set; } = "Unknown";
        public string Filter { get; set; } = "None";
        public double ExposureTime { get; set; } = 300;
        public double HFR { get; set; } = 0;
        public double FWHM { get; set; } = 0;
        public double Eccentricity { get; set; } = 0;
        public int DetectedStars { get; set; } = 0;
        public double GuidingRmsTotal { get; set; } = 0;
        public double GuidingScale { get; set; } = 1.0;
        public double RaHours { get; set; } = 0;
        public double DecDegrees { get; set; } = 0;
        public int Gain { get; set; } = -1;
        public int Offset { get; set; } = -1;
        public int BinX { get; set; } = 1;
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

    internal class AutoFocusData {
        public string Filter { get; set; } = "L";
        public double Temperature { get; set; } = 15.0;
        public double Position { get; set; } = 25000;
    }

    internal class SafetyStateData {
        public bool IsSafe { get; set; } = true;
    }

    internal class MeridianFlipData {
        public bool Success { get; set; } = true;
        public double RaHours { get; set; } = 0;
        public double DecDegrees { get; set; } = 0;
    }
}
