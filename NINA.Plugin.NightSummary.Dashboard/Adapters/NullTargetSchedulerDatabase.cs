using System.Collections.Generic;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;
using NINA.Plugin.NightSummary.Data;

namespace NINA.Plugin.NightSummary.Dashboard.Adapters;

// No-op TS database for hosts that don't have access to the TS SQLite file
// (companion app, dev harness without TS data). Always reports the plugin
// as absent so the report skips TS progress sections gracefully.
public sealed class NullTargetSchedulerDatabase : ITargetSchedulerDatabase {
    public bool IsPluginInstalled => false;
    public bool IsAvailable => false;
    public (bool Enabled, int Port) GetApiSettings(string? profileId = null) => (false, 0);
    public List<TsTargetData> GetProgressForTargets(IEnumerable<string> sessionTargetNames, string? profileId = null)
        => new List<TsTargetData>();
}
