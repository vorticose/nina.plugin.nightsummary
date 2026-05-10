namespace NINA.Plugin.NightSummary.Data {

    /// <summary>
    /// Per-image augmentation pulled from the Target Scheduler database for the
    /// dashboard's lightbox metrics panel. Populated by TargetSchedulerDatabase
    /// (NS-project side) and consumed by DashboardServer via IDashboardDataSource.
    /// All fields default to null when TS isn't installed or no row matches —
    /// the JS hides any chip whose value is null.
    /// </summary>
    public class TsImageAugment {
        public string ProjectName { get; set; }            // null if not joinable
        public string ExposureTemplateName { get; set; }
        public int? GradingStatus { get; set; }
        public string RejectReason { get; set; }

        // Parsed from TS metadata JSON — null if missing/unparseable. NS captures
        // overall HFR + total guiding RMS but not these per-axis details.
        public double? HFRStDev { get; set; }
        public double? GuidingRMSRA { get; set; }
        public double? GuidingRMSRAArcSec { get; set; }
        public double? GuidingRMSDEC { get; set; }
        public double? GuidingRMSDECArcSec { get; set; }
    }
}
