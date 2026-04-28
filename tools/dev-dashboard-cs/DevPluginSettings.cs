using System.Reflection;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.DevHost;

// Settings live in memory only. Save() is a no-op; restarting the harness
// resets to defaults. Good enough for UI iteration; not a place to persist
// credentials.
internal sealed class DevPluginSettings : IPluginSettings {
    public NightSummarySettings Current { get; } = new NightSummarySettings();
    public void Save() { }
    public string PluginVersion => "3.0.0";
}
