using System;

namespace NINA.Plugin.NightSummary.Data {
    public class TargetSessionHistory {
        public DateTime SessionStart       { get; set; }
        public double   IntegrationSeconds { get; set; }
        public double   AvgHFR            { get; set; }
        public double   AvgFWHM           { get; set; }
        public double   AvgGuidingRMS     { get; set; }
    }
}
