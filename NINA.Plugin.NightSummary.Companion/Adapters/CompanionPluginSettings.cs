using System.Reflection;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Companion.Adapters;

// Settings stub for the companion. The companion does not own a settings.json
// of its own (its config is companion.json, separate); it just supplies the
// dashboard server with a NightSummarySettings instance carrying defaults.
// Mode = "companion" so the dashboard JS / future sync UI can detect it via
// /api/mode and surface the staleness banner / sync controls.
internal sealed class CompanionPluginSettings : IPluginSettings {
    public NightSummarySettings Current { get; } = new NightSummarySettings();
    public void Save() { /* no-op — companion settings live in companion.json */ }
    public string PluginVersion =>
        typeof(CompanionPluginSettings).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?.Split('+')[0] ?? "";
    public string Mode => "companion";
}
