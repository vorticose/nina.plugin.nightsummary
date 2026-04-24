using System;
using System.Collections.Generic;

namespace NINA.Plugin.NightSummary.Data {

    /// <summary>
    /// Target Scheduler project hierarchy for the Stats Targets tab (Phase 3a).
    /// Populated from the TS SQLite database via TargetSchedulerDatabase.GetAllProjects.
    /// </summary>
    public class TsProjectInfo {
        public int      Id              { get; set; }  // TS rowid (not stable across DBs; use Guid for keys)
        public string   Guid            { get; set; }  // Stable unique identifier — use for override keys
        public string   ProfileId       { get; set; }
        public string   Name            { get; set; }
        public string   Description     { get; set; }
        public int      StateValue      { get; set; }  // 0=Draft, 1=Active, 2=Inactive, 3=Closed
        public string   State           { get; set; }  // "Draft" | "Active" | "Inactive" | "Closed"
        public int      PriorityValue   { get; set; }  // 0=Low, 1=Normal, 2=High
        public string   Priority        { get; set; }  // "Low" | "Normal" | "High"
        public bool     IsMosaic        { get; set; }
        public DateTime? CreateDate     { get; set; }
        public DateTime? ActiveDate     { get; set; }
        public DateTime? InactiveDate   { get; set; }
        public double   MinimumAltitude { get; set; }
        public double   MaximumAltitude { get; set; }
        public List<TsProjectTarget> Targets { get; set; } = new List<TsProjectTarget>();
    }

    /// <summary>
    /// A target belonging to a Target Scheduler project.
    /// Distinct from TsTargetData (which is shaped for per-session Tonight's Summary use).
    /// </summary>
    public class TsProjectTarget {
        public int      Id              { get; set; }
        public string   Guid            { get; set; }
        public int      ProjectId       { get; set; }
        public string   Name            { get; set; }
        public bool     Active          { get; set; }
        public double   RA              { get; set; }  // decimal hours
        public double   Dec             { get; set; }  // decimal degrees
        public double   Rotation        { get; set; }
        public List<TsProjectExposurePlan> ExposurePlans { get; set; } = new List<TsProjectExposurePlan>();
    }

    /// <summary>
    /// A single exposure plan (per-filter goal + progress) within a target.
    /// </summary>
    public class TsProjectExposurePlan {
        public string Filter        { get; set; }
        public string TemplateName  { get; set; }
        public double ExposureSec   { get; set; }
        public int    Desired       { get; set; }
        public int    Acquired      { get; set; }
        public int    Accepted      { get; set; }
    }
}
