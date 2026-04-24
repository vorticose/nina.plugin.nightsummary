using System;
using System.Collections.Generic;

namespace NINA.Plugin.NightSummary.Data {

    /// <summary>
    /// Per-session aggregate for a target's session history (Phase 2 stats detail panel).
    /// Richer than TargetSessionHistory — includes frame counts and per-filter breakdown.
    /// </summary>
    public class TargetSessionDetail {
        public string   SessionId           { get; set; }
        public DateTime SessionStart        { get; set; }
        public DateTime SessionEnd          { get; set; }
        public double   IntegrationSeconds  { get; set; }
        public int      FrameCount          { get; set; }  // all light frames (accepted + rejected)
        public int      AcceptedFrames      { get; set; }
        public double   AvgHFR              { get; set; }
        public double   AvgGuidingRMS       { get; set; }
        public List<TargetSessionFilterDetail> Filters { get; set; } = new List<TargetSessionFilterDetail>();
    }

    /// <summary>
    /// Per-filter breakdown within a single session for a target.
    /// </summary>
    public class TargetSessionFilterDetail {
        public string Filter             { get; set; }
        public double IntegrationSeconds { get; set; }
        public int    FrameCount         { get; set; }
        public int    AcceptedFrames     { get; set; }
        public double AvgHFR             { get; set; }
        public double AvgGuidingRMS      { get; set; }
    }
}
