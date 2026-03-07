using System.Collections.Generic;

namespace NINA.Plugin.NightSummary.Data {

    /// <summary>
    /// Per-filter exposure progress pulled from the Target Scheduler database.
    /// </summary>
    public class TsFilterProgress {
        public string Filter   { get; set; }
        public int    Desired  { get; set; }
        public int    Acquired { get; set; }
        public int    Accepted { get; set; }
    }

    /// <summary>
    /// Target Scheduler data for a single target: coordinates and per-filter progress.
    /// </summary>
    public class TsTargetData {
        public string                  TargetName { get; set; }
        public double                  RA         { get; set; }  // decimal hours
        public double                  Dec        { get; set; }  // decimal degrees
        public double                  Rotation   { get; set; }  // position angle degrees East of North
        public List<TsFilterProgress>  Filters    { get; set; } = new List<TsFilterProgress>();
    }
}
