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

    // NINA host version (NOT the plugin version). Surfaced via
    // /api/companion/info so the wizard can show which NINA build it's
    // pairing against. Default-implemented as empty so non-NINA hosts
    // (dev harness, companion binary, test stubs) don't have to override.
    string NinaVersion => "";

    // Observer coordinates from NINA's active profile AstrometrySettings.
    // Surfaced in the Tonight's Preview response so the dashboard's altitude
    // chart can compute per-target altitude curves. Both default to 0 (the
    // "unset" sentinel the JS already handles by hiding the curves) so
    // non-NINA hosts and pre-profile-load conditions don't need to override.
    // Companion mirrors get these via the synced tonight-preview-cache.json,
    // not from this property directly — companion mode never hits the live
    // path that reads them.
    double ObserverLatitude  => 0;
    double ObserverLongitude => 0;
}
