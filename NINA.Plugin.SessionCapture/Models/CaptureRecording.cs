using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.SessionCapture.Models {

    /// <summary>
    /// Root container for a captured session recording.
    /// This format is consumed by the Night Summary replay test harness.
    /// </summary>
    public class CaptureRecording {
        public int FormatVersion { get; set; } = 2;
        public string NinaVersion { get; set; } = "";
        public string SessionCaptureVersion { get; set; } = "1.1.0";
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

    /// <summary>
    /// Full snapshot of an ImageSaved event. Serializes the entire ImageMetaData tree,
    /// star detection analysis, and image statistics — capturing everything NINA exposes
    /// so future Night Summary features can use data from existing recordings.
    /// </summary>
    public class ImageSavedSnapshot {
        public ImageMetaData MetaData { get; set; }
        public IStarDetectionAnalysis StarDetectionAnalysis { get; set; }
        public IImageStatistics Statistics { get; set; }
        public string PathToImage { get; set; }
        public string FileType { get; set; }
        public bool IsBayered { get; set; }
        public double Duration { get; set; }
        public string Filter { get; set; }
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
