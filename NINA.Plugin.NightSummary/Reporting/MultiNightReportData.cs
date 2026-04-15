using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.NightSummary.Reporting {
    /// <summary>
    /// All data needed to generate a multi-night summary report.
    /// Assembled from date-range queries against the session database.
    /// </summary>
    public class MultiNightReportData {
        /// <summary>Date range start (inclusive).</summary>
        public DateTime From { get; init; }
        /// <summary>Date range end (inclusive).</summary>
        public DateTime To { get; init; }
        /// <summary>NINA profile name for display.</summary>
        public string ProfileName { get; init; }
        /// <summary>Sessions within the date range, newest-first.</summary>
        public List<SessionRecord> Sessions { get; init; }
        /// <summary>All images across all sessions in the range, ordered by timestamp.</summary>
        public List<ImageRecord> AllImages { get; init; }
        /// <summary>Observer latitude in decimal degrees from NINA profile.</summary>
        public double ObserverLatitude { get; init; }
        /// <summary>Observer longitude in decimal degrees (positive East).</summary>
        public double ObserverLongitude { get; init; }
        /// <summary>Camera FOV width in degrees (from most recent session in range).</summary>
        public double CameraFovWidthDeg { get; init; }
        /// <summary>Camera FOV height in degrees.</summary>
        public double CameraFovHeightDeg { get; init; }
    }
}
