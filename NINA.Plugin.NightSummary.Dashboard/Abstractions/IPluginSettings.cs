using NINA.Plugin.NightSummary.Data;

namespace NINA.Plugin.NightSummary.Dashboard.Abstractions;

// Thin wrapper around the plugin's settings instance. Plugin side returns
// SettingsManager.Instance.Current so changes flow back to disk; dev harness
// returns an in-memory NightSummarySettings populated from defaults/fixtures.
public interface IPluginSettings {
    NightSummarySettings Current { get; }
    void Save();
    string PluginVersion { get; }

    // "primary" when the dashboard server runs inside the NINA plugin (live data).
    // "companion" when it runs in the standalone companion binary (synced data copy).
    // Surfaced via /api/mode so the dashboard JS can show sync UI / staleness banner
    // without changing any business logic on the server.
    string Mode { get; }
}
