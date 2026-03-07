using NINA.Plugin.NightSummary.Data;
using System.Collections.Generic;

namespace NINA.Plugin.NightSummary.Reporting {
    /// <summary>
    /// All data needed to generate a Night Summary HTML report.
    /// Passed as a single parameter to ReportGenerator to avoid growing the method signature
    /// as more data sources are added in future versions.
    /// </summary>
    public class ReportData {
        public SessionRecord      Session { get; init; }
        public List<ImageRecord>  Images  { get; init; }
        public List<SessionEvent> Events  { get; init; }
        /// <summary>
        /// Per-target exposure progress from Target Scheduler. Empty if TS is not installed.
        /// </summary>
        public List<TsTargetData> TsData  { get; init; }
        /// <summary>
        /// Total accepted exposure seconds per target across all sessions except the current one.
        /// </summary>
        public Dictionary<string, double> CumulativeIntegrationSeconds { get; init; }
        /// <summary>
        /// Imaging camera FOV width in degrees, computed from profile (pixel size + focal length + sensor width).
        /// </summary>
        public double CameraFovWidthDeg  { get; init; }
        /// <summary>
        /// Imaging camera FOV height in degrees.
        /// </summary>
        public double CameraFovHeightDeg { get; init; }
    }
}
