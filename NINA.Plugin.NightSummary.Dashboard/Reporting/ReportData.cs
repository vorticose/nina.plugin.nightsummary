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
        /// Per-target session history for historical comparison (all previous sessions), keyed by target name.
        /// </summary>
        public Dictionary<string, List<TargetSessionHistory>> SessionHistory { get; init; }
        /// <summary>
        /// Per-target roll-up across all previous sessions (totals + per-filter breakdown)
        /// for the Session History totals band, keyed by target name. Optional — null
        /// (or a missing key) renders the section without the band.
        /// </summary>
        public Dictionary<string, TargetSessionHistoryAggregate> SessionHistoryAggregate { get; init; }
        /// <summary>
        /// Imaging camera FOV width in degrees, computed from profile (pixel size + focal length + sensor width).
        /// </summary>
        public double CameraFovWidthDeg  { get; init; }
        /// <summary>
        /// Imaging camera FOV height in degrees.
        /// </summary>
        public double CameraFovHeightDeg { get; init; }
        /// <summary>
        /// Observer latitude in decimal degrees from NINA profile. 0 if not configured.
        /// </summary>
        public double ObserverLatitude  { get; init; }
        /// <summary>
        /// Observer longitude in decimal degrees (positive East) from NINA profile. 0 if not configured.
        /// </summary>
        public double ObserverLongitude { get; init; }
        /// <summary>
        /// Active NINA profile GUID, used to filter TS queries to the correct profile.
        /// </summary>
        public string ActiveProfileId { get; init; }
        /// <summary>
        /// Number of exposures that were skipped/aborted during the session (e.g. by RMS triggers, safety events).
        /// </summary>
        public int SkippedExposures { get; init; }
        /// <summary>
        /// Per-event timing data parsed from NINA logs for overhead breakdown analysis.
        /// Empty if log parsing was unavailable or produced no results.
        /// </summary>
        public List<TimingEvent> TimingEvents { get; init; }

        /// <summary>
        /// Equipment names for the session, keyed by role (Camera, Telescope, Mount, etc.).
        /// Values are display names (user override if set, otherwise NINA-detected name).
        /// Empty entries are omitted.
        /// </summary>
        public Dictionary<string, string> Equipment { get; init; } = new();
        /// <summary>
        /// Live Stack images captured during the session. Empty if Live Stack plugin is not running.
        /// </summary>
        public List<Session.LiveStackImage> LiveStackImages { get; set; } = new();
    }
}
