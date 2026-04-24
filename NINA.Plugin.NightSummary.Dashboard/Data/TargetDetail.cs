using System;
using System.Collections.Generic;

namespace NINA.Plugin.NightSummary.Data {
    public class TargetDetail {
        public string   TargetName             { get; set; }
        public double   TotalIntegrationSeconds { get; set; }
        public int      SessionCount           { get; set; }
        public DateTime LastSessionStart       { get; set; }
        public string   LatestSessionId        { get; set; }
        public double   AvgHFR                 { get; set; }
        public double   AvgFWHM                { get; set; }
        public double   AvgGuidingRMS          { get; set; }
        public double   RaHours                { get; set; }
        public double   DecDegrees             { get; set; }
        public int      TotalFrames            { get; set; }
        public int      AcceptedFrames         { get; set; }
        public List<FilterBreakdown> Filters   { get; set; } = new List<FilterBreakdown>();
    }

    public class FilterBreakdown {
        public string Filter        { get; set; }
        public double TotalSeconds  { get; set; }
        public int    FrameCount    { get; set; }
        public int    AcceptedCount { get; set; }
    }
}
