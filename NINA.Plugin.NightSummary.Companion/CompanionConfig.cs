using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NINA.Plugin.NightSummary.Companion;

// User-editable config for the companion. Lives next to the binary by default,
// or wherever the user points the --config flag. Reflects the schema documented
// in COMPANION_PLAN.md.
public sealed class CompanionConfig {

    [JsonPropertyName("port")]
    public int Port { get; set; } = 8182;

    [JsonPropertyName("dataDir")]
    public string DataDir { get; set; } = "";

    [JsonPropertyName("nina")]
    public NinaConfig Nina { get; set; } = new();

    [JsonPropertyName("sync")]
    public SyncConfig Sync { get; set; } = new();

    // Optional second DashboardServer instance bound to a separate port with
    // readOnly: true. Designed for parallel-port public exposure behind a
    // reverse proxy / Tailscale Funnel — the public port refuses every non-GET
    // request server-side and hides destructive UI. Mirrors the primary-side
    // EnableReadOnlyMirror + ReadOnlyMirrorPort settings (default 8281 there,
    // 8282 here so the two don't collide on a box running both).
    [JsonPropertyName("enableReadOnlyMirror")]
    public bool EnableReadOnlyMirror { get; set; } = false;

    [JsonPropertyName("readOnlyMirrorPort")]
    public int ReadOnlyMirrorPort { get; set; } = 8282;

    public sealed class NinaConfig {
        [JsonPropertyName("host")]    public string Host    { get; set; } = "";
        [JsonPropertyName("port")]    public int    Port    { get; set; } = 8181;

        // Per-companion pairing token issued by the primary's pairing wizard.
        // Sent as Authorization: Bearer to authenticate every request to the
        // primary's /api/export/* endpoints. See COMPANION_PAIRING_DESIGN.md.
        [JsonPropertyName("pairingToken")] public string PairingToken { get; set; } = "";
    }

    public sealed class SyncConfig {
        [JsonPropertyName("onBoot")]
        public bool OnBoot { get; set; } = true;

        [JsonPropertyName("pollingIntervalMinutesOnFailure")]
        public int PollingIntervalMinutesOnFailure { get; set; } = 30;

        [JsonPropertyName("pollingIntervalHoursOnSuccess")]
        public int PollingIntervalHoursOnSuccess { get; set; } = 4;

        // Accept session-end push notifications from the primary (NINA).
        // When true (default) the companion's /api/companion/sync endpoint
        // triggers a sync whenever the primary POSTs to it after a session
        // ends. When false, push-triggered requests no-op (the user-clicked
        // Sync button still works — push is identified by an X-Sync-Trigger
        // header). Toggling this off pushes the companion onto the polling
        // schedule only.
        [JsonPropertyName("acceptPush")]
        public bool AcceptPush { get; set; } = true;
    }

    // Where the synced data lives on disk. Defaults to a platform-appropriate
    // app-data dir when not specified in the file (handy for first run).
    public string ResolvedDataDir() {
        if (!string.IsNullOrWhiteSpace(DataDir)) return DataDir;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(appData, "NightSummaryCompanion");
    }

    public string ResolvedNinaUrl() {
        var host = string.IsNullOrWhiteSpace(Nina.Host) ? "localhost" : Nina.Host;
        return $"http://{host}:{Nina.Port}";
    }

    // ── Load / Save ──────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new() {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static CompanionConfig Load(string path) {
        if (!File.Exists(path)) {
            // Materialize a default file so the user has something concrete to edit
            var def = new CompanionConfig();
            Save(def, path);
            return def;
        }
        try {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<CompanionConfig>(json, JsonOpts);
            return loaded ?? new CompanionConfig();
        } catch (Exception ex) {
            throw new InvalidOperationException($"Failed to read {path}: {ex.Message}", ex);
        }
    }

    public static void Save(CompanionConfig cfg, string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(cfg, JsonOpts));
    }

    public void Validate() {
        if (string.IsNullOrWhiteSpace(Nina.Host))
            throw new InvalidOperationException("companion.json: nina.host is required");
        if (string.IsNullOrWhiteSpace(Nina.PairingToken))
            throw new InvalidOperationException("companion.json: nina.pairingToken is required (run the setup wizard)");
        if (Nina.Port <= 0 || Nina.Port > 65535)
            throw new InvalidOperationException($"companion.json: nina.port {Nina.Port} out of range");
        if (Port <= 0 || Port > 65535)
            throw new InvalidOperationException($"companion.json: port {Port} out of range");
    }

    // Non-throwing variant — drives "setup needed" UX in the dashboard so
    // serve can start before the user has paired.
    public bool IsComplete(out string? reason) {
        if (string.IsNullOrWhiteSpace(Nina.Host)) { reason = "nina.host is empty"; return false; }
        if (string.IsNullOrWhiteSpace(Nina.PairingToken)) {
            reason = "no pairing token — run the setup wizard"; return false;
        }
        if (Nina.Port <= 0 || Nina.Port > 65535) { reason = $"nina.port {Nina.Port} out of range"; return false; }
        if (Port <= 0 || Port > 65535) { reason = $"port {Port} out of range"; return false; }
        reason = null; return true;
    }

    public bool IsComplete() => IsComplete(out _);
}
