using NINA.Plugin.NightSummary.Data;

namespace NINA.Plugin.NightSummary.Dashboard.Abstractions;

// Thin wrapper around the plugin's settings instance. Plugin side returns
// SettingsManager.Instance.Current so changes flow back to disk; dev harness
// returns an in-memory NightSummarySettings populated from defaults/fixtures.
public interface IPluginSettings {
    NightSummarySettings Current { get; }
    void Save();
    string PluginVersion { get; }
}
