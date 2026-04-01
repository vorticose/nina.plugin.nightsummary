using System;

namespace NINA.Plugin.NightSummary.Data {
    /// <summary>
    /// Represents a single imaging session (one night).
    /// Groups all ImageRecords taken during that session.
    /// </summary>
    public class SessionRecord {
        // Primary key for SQLite
        public int Id { get; set; }

        // Unique identifier shared with ImageRecord.SessionId
        // so we can query all images belonging to this session
        public string SessionId { get; set; }

        // When the sequence started and ended
        public DateTime SessionStart { get; set; }
        public DateTime SessionEnd { get; set; }

        // NINA profile active during this session
        public string ProfileName { get; set; }

        // Overall session notes - we can populate this
        // with a summary string once the session ends
        public string Notes { get; set; }

        // Whether the end of session report was successfully sent
        public bool ReportSent { get; set; }

        // Camera hardware info captured from the first image of the session.
        // Used to compute FOV for the sky survey thumbnail.
        public int    CamXSize         { get; set; }
        public int    CamYSize         { get; set; }
        public double PixelSizeMicrons { get; set; }
        public double FocalLengthMm    { get; set; }

        // Number of exposures skipped/aborted during the session
        public int SkippedExposures { get; set; }

        // Equipment names captured at session start/end
        public string CameraName      { get; set; }
        public string TelescopeName   { get; set; }
        public string MountName       { get; set; }
        public string FilterWheelName { get; set; }
        public string FocuserName     { get; set; }
        public string RotatorName     { get; set; }
        public string GuiderName      { get; set; }

        // Display string for session picker dropdown
        public string DisplayLabel => $"{SessionStart:yyyy-MM-dd  HH:mm}  —  {ProfileName}";
    }
}