using System;
using System.Collections.Generic;

namespace NINA.Plugin.NightSummary.Data {

    /// <summary>
    /// Per-filter exposure progress pulled from the Target Scheduler database.
    /// </summary>
    public class TsFilterProgress {
        public string Filter       { get; set; }
        public string TemplateName { get; set; }
        public double ExposureSec  { get; set; }
        public int    Desired      { get; set; }
        public int    Acquired     { get; set; }
        public int    Accepted     { get; set; }
    }

    /// <summary>
    /// A single row from the Target Scheduler acquiredimage table.
    /// Used to match images back to grading results at session end.
    /// </summary>
    public class TsAcquiredImage {
        public DateTime AcquiredAt    { get; set; }
        public string   FilterName    { get; set; }
        public int      GradingStatus { get; set; }  // raw TS enum; -1 = unknown
        public string   RejectReason  { get; set; }
    }

    /// <summary>
    /// Target Scheduler data for a single target: coordinates and per-filter progress.
    /// </summary>
    public class TsTargetData {
        public string                  TargetName      { get; set; }
        public double                  RA              { get; set; }  // decimal hours
        public double                  Dec             { get; set; }  // decimal degrees
        public double                  Rotation        { get; set; }  // position angle degrees East of North
        public double                  MinimumAltitude { get; set; }  // degrees; 0 = not set
        public List<TsFilterProgress>  Filters         { get; set; } = new List<TsFilterProgress>();
    }

    // ── TS API response models (for /profiles and /profiles/{id}/preview) ──

    /// <summary>
    /// A NINA profile returned by the TS API /profiles endpoint.
    /// </summary>
    public class TsProfileInfo {
        public string Id     { get; set; }
        public string Name   { get; set; }
        public bool   Active { get; set; }
    }

    /// <summary>
    /// A single entry from the TS API /profiles/{id}/preview endpoint.
    /// Represents either a target imaging block or a wait period.
    /// </summary>
    public class TsPreviewEntry {
        public string                   Id           { get; set; }
        public string                   Name         { get; set; }
        public bool                     WaitPeriod   { get; set; }
        public DateTime                 StartTime    { get; set; }
        public DateTime                 EndTime      { get; set; }
        public List<TsPreviewExposure>  ExposurePlan { get; set; } = new List<TsPreviewExposure>();
    }

    /// <summary>
    /// A filter/exposure entry within a TS preview target block.
    /// </summary>
    public class TsPreviewExposure {
        public string FilterName { get; set; }
        public double Exposure   { get; set; }  // seconds
        public int    Count      { get; set; }
    }
}
