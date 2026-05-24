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
    // Mutable so --companion-mode in Program.cs can flip it to "companion" at
    // startup. /api/mode reads this; the JS initCompanionBanner gates the
    // entire companion UI (unhides Settings nav link, shows sync banner, sets
    // COMPANION_MODE=true) on the response. Without this flip the stub
    // ICompanionController gets wired but no companion-mode UI ever renders —
    // which defeats the whole point of --companion-mode.
    public string Mode { get; set; } = "primary";
}
