using System;
namespace NINA.Plugin.NightSummary.Data {
    /// <summary>
    /// Represents a single captured image and all associated metadata
    /// recorded during a Night Summary session.
    /// </summary>
    public class ImageRecord {
        // Primary key for SQLite
        public int Id { get; set; }

        // Session this image belongs to
        public string SessionId { get; set; }

        // When the image was saved
        public DateTime Timestamp { get; set; }

        // Target and filter info
        public string TargetName { get; set; }
        public string Filter { get; set; }
        public double ExposureDuration { get; set; }

        // Image quality metrics
        public double HFR { get; set; }
        public double FWHM { get; set; }
        public double Eccentricity { get; set; }
        public int StarCount { get; set; }

        // Guiding - stored in arcseconds using NINA's scale factor
        public double GuidingRMSTotal { get; set; }
        public double GuidingScale { get; set; }

        // Whether this image was accepted or rejected by image grader
        public bool Accepted { get; set; }
    }
}