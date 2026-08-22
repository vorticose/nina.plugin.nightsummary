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
    // Same fallback as the primary's NinaPluginSettings: release builds have the
    // informational-version attribute stripped, so fall back to the AssemblyVersion
    // (Major.Minor.Build) rather than returning "" — keeps the companion from
    // reporting its own version as blank. Never empty for a real build.
    public string PluginVersion {
        get {
            var info = typeof(CompanionPluginSettings).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion?.Split('+')[0];
            if (!string.IsNullOrWhiteSpace(info)) return info;
            var v = typeof(CompanionPluginSettings).Assembly.GetName().Version;
            return v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "";
        }
    }
    public string Mode => "companion";
}
