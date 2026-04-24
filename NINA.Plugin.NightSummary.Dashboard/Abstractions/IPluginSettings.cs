using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Dashboard.Abstractions;

public interface IPluginSettings {
    // Returns the dashboard-relevant subset of plugin settings as a dictionary keyed by
    // setting name (e.g. "ReportDetailLevel", "ShowMoonCurve"). Values are stringified
    // (parsed by the server when needed) so the interface stays decoupled from the
    // plugin's NightSummarySettings class.
    Task<IReadOnlyDictionary<string, string?>> GetAsync(CancellationToken ct = default);

    Task UpdateAsync(IReadOnlyDictionary<string, string?> settings, CancellationToken ct = default);

    // Scoped override: the current settings are snapshotted on construction, overrides
    // applied, then restored when the IDisposable is disposed. Used by report regeneration.
    IDisposable ApplyOverrides(IReadOnlyDictionary<string, string?> overrides);
}
