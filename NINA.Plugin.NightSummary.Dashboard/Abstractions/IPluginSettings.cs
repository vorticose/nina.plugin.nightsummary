using System;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Dashboard.Dtos;

namespace NINA.Plugin.NightSummary.Dashboard.Abstractions;

public interface IPluginSettings {
    Task<DashboardSettingsDto> GetAsync(CancellationToken ct = default);
    Task UpdateAsync(DashboardSettingsDto settings, CancellationToken ct = default);

    // Scoped override: current plugin settings are snapshotted, overrides applied,
    // then restored when the IDisposable is disposed. Used by report regeneration.
    IDisposable ApplyOverrides(SettingsOverridesDto overrides);
}
