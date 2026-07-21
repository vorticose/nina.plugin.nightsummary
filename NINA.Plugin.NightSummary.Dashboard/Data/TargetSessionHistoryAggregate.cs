using System.Collections.Generic;

namespace NINA.Plugin.NightSummary.Data {

    // Roll-up across ALL prior sessions for a target (the current session excluded),
    // used by the report's Session History "totals band". Complements
    // TargetSessionHistory (which is per-session); this is the summed/averaged view
    // plus a per-filter integration breakdown, derived from raw Images rows so the
    // averages are real frame-level means rather than a (statistically wrong)
    // average of the per-session averages.
    public class TargetSessionHistoryAggregate {
        public double TotalIntegrationSeconds { get; set; }
        public double AvgHFR { get; set; }
        public double AvgFWHM { get; set; }
        public double AvgGuidingRMS { get; set; }
        // Per-filter integration, raw user filter names, sorted desc by integration.
        // Sums to TotalIntegrationSeconds (same "not rejected" predicate).
        public List<FilterIntegration> Filters { get; set; } = new();
    }

    public class FilterIntegration {
        public string Filter { get; set; } = "";
        public double IntegrationSeconds { get; set; }
    }
}
