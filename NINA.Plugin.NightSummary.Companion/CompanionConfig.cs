using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NINA.Plugin.NightSummary.Companion;

// User-editable config for the companion. Lives in the per-user app-data dir by
// default (see Program.DefaultConfigPath — outside the install artifact so it
// survives updates), or wherever the user points the --config flag. Reflects the
// schema documented in COMPANION_PLAN.md.
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
        // 1. Primary file, if present and usable.
        var primary = TryReadConfig(path);
        if (primary != null) return primary;

        // 2. Recover from the last-good backup. A torn write or a truncated
        //    primary must not cost the user their host + pairing token, so fall
        //    back to companion.json.bak and restore it in place (leaving the
        //    .bak intact as the safety copy).
        var bak = path + ".bak";
        var backup = TryReadConfig(bak);
        if (backup != null) {
            try { File.Copy(bak, path, overwrite: true); } catch { /* best-effort restore */ }
            return backup;
        }

        // 3. Nothing usable → materialize a fresh default so the user has
        //    something concrete to edit / the setup wizard to fill in.
        var def = new CompanionConfig();
        Save(def, path);
        return def;
    }

    // Reads + parses a config file, returning null if it's absent, empty, or
    // unparseable (so the caller can fall through to a backup / a fresh default).
    // A corrupt config must never brick the companion — it's a re-syncable mirror.
    private static CompanionConfig? TryReadConfig(string path) {
        if (!File.Exists(path)) return null;
        try {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<CompanionConfig>(json, JsonOpts);
        } catch {
            return null;
        }
    }

    public static void Save(CompanionConfig cfg, string path) {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var json = JsonSerializer.Serialize(cfg, JsonOpts);
        var tmp  = full + ".tmp";
        File.WriteAllText(tmp, json);

        // Atomic swap that preserves the previous good copy as companion.json.bak.
        // File.Replace is atomic on NTFS/APFS/ext4 (a reader sees the old or the
        // new file, never a torn one) and rotates the existing file into the .bak
        // slot in the same operation — so a crash mid-save can't lose the pairing.
        if (File.Exists(full)) {
            try {
                File.Replace(tmp, full, full + ".bak");
                return;
            } catch (IOException)                 { /* rename blocked — fall through */ }
            catch (UnauthorizedAccessException)    { /* fall through */ }
            catch (PlatformNotSupportedException)  { /* fall through */ }
        }
        // First write, or File.Replace unavailable on this FS — plain overwrite.
        File.Move(tmp, full, overwrite: true);
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
