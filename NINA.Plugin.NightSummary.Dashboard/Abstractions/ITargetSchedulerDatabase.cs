using System.Collections.Generic;
using NINA.Plugin.NightSummary.Data;

namespace NINA.Plugin.NightSummary.Dashboard.Abstractions;

// Subset of TargetSchedulerDatabase needed by ReportGenerator. Plugin
// (Windows) returns the real System.Data.SQLite-backed impl; companion
// returns a no-op since the TS DB is not synced to the companion box.
public interface ITargetSchedulerDatabase {
    bool IsPluginInstalled { get; }
    bool IsAvailable { get; }
    (bool Enabled, int Port) GetApiSettings(string? profileId = null);
    List<TsTargetData> GetProgressForTargets(IEnumerable<string> sessionTargetNames, string? profileId = null);
}
