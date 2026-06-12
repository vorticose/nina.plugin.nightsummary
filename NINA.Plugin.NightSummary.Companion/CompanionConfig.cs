using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NINA.Plugin.NightSummary.Companion;

// User-editable config for the companion. Lives in the per-user app-data dir by
// default (see Program.DefaultConfigPath — outside the install artifact so it
// survives updates), or wherever the user points the --config flag.
//
// v2 (multi-rig): the companion can sync N primary rigs into one dashboard. Each
// rig keeps its own complete data dir under {dataDir}/rigs/{rigId}/ and is
// described by a RigConfig entry in the `rigs` array. The legacy v1 shape
// (top-level `nina` + `sync` blocks, no `configVersion`) is migrated into
// `rigs[0]` silently on first load (see MigrateFromV1). The legacy Nina/Sync
// proxies below keep single-rig call-sites and tests compiling unchanged — they
// read/write the FIRST rig.
public sealed class CompanionConfig {

    // Bumped to 2 when the rigs array became canonical. Absent (null) in a v1
    // file; we treat null/absent + a populated legacy `nina` block as "needs
    // migration." Save always stamps 2.
    [JsonPropertyName("configVersion")]
    public int? ConfigVersion { get; set; }

    [JsonPropertyName("port")]
    public int Port { get; set; } = 8182;

    [JsonPropertyName("dataDir")]
    public string DataDir { get; set; } = "";

    // Canonical v2 store: one entry per paired primary rig. Single-rig installs
    // have exactly one element; the dashboard hides the rig switcher when count
    // is 1 so the single-rig UX is unchanged.
    [JsonPropertyName("rigs")]
    public List<RigConfig> Rigs { get; set; } = new();

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

    // ── Legacy v1 fields ────────────────────────────────────────────────────
    // Deserialized straight from an old companion.json's top-level "nina"/"sync"
    // blocks so MigrateFromV1 can fold them into rigs[0]. Never written back
    // (Save emits v2 only) — kept non-null only transiently between Load and the
    // migration step. Renamed off "Nina"/"Sync" so the proxies below can own
    // those names for single-rig callers.
    [JsonPropertyName("nina")]
    public RigConfig.NinaConfig? RawNina { get; set; }

    [JsonPropertyName("sync")]
    public RigConfig.SyncConfig? RawSync { get; set; }

    public sealed class NinaConfig : RigConfig.NinaConfig { }   // kept for source compat (some tests reference the nested name)

    // ── Single-rig convenience proxies ──────────────────────────────────────
    // Getter-only so `new CompanionConfig { Nina = { Host = "x" } }` (nested
    // object-initializer) and `_config.Nina.Host = ...` both work: they mutate
    // the first rig, creating it on demand. JsonIgnore so they don't double-emit
    // alongside the rigs array.

    [JsonIgnore]
    public RigConfig.NinaConfig Nina {
        get => EnsureFirstRig().Nina;
        set => EnsureFirstRig().Nina = value;
    }

    [JsonIgnore]
    public RigConfig.SyncConfig Sync {
        get => EnsureFirstRig().Sync;
        set => EnsureFirstRig().Sync = value;
    }

    // The first rig, creating an empty one if the list is empty. Used by the
    // single-rig proxies and any code that predates multi-rig.
    public RigConfig EnsureFirstRig() {
        if (Rigs.Count == 0) Rigs.Add(new RigConfig());
        return Rigs[0];
    }

    // ── Path resolution ─────────────────────────────────────────────────────

    // Root data dir — rigs nest under {root}/rigs/{rigId}/. Defaults to a
    // platform-appropriate app-data dir when not specified (handy for first run).
    public string ResolvedDataDir() {
        if (!string.IsNullOrWhiteSpace(DataDir)) return DataDir;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(appData, "NightSummaryCompanion");
    }

    // Per-rig data dir. Each rig owns a complete NightSummary tree here so there
    // is no schema change, no session-id collision risk, and no merge logic —
    // the sync engine runs unchanged per rig.
    public string RigDataDir(string rigId) =>
        Path.Combine(ResolvedDataDir(), "rigs", rigId);

    // Legacy single-rig URL helper — proxies the first rig.
    public string ResolvedNinaUrl() => EnsureFirstRig().ResolvedNinaUrl();

    // ── Load / Save ──────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new() {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    // Serialize/save options that DROP the legacy raw blocks + nulls so a saved
    // v2 file is clean (no stale top-level "nina"/"sync"). WhenWritingNull elides
    // RawNina/RawSync once migration has cleared them.
    private static readonly JsonSerializerOptions SaveOpts = new() {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static CompanionConfig Load(string path) {
        // 1. Primary file, if present and usable.
        var primary = TryReadConfig(path);
        if (primary != null) { primary.MigrateFromV1(); return primary; }

        // 2. Recover from the last-good backup. A torn write or a truncated
        //    primary must not cost the user their host + pairing token, so fall
        //    back to companion.json.bak and restore it in place (leaving the
        //    .bak intact as the safety copy).
        var bak = path + ".bak";
        var backup = TryReadConfig(bak);
        if (backup != null) {
            try { File.Copy(bak, path, overwrite: true); } catch { /* best-effort restore */ }
            backup.MigrateFromV1();
            return backup;
        }

        // 3. Nothing usable → materialize a fresh default so the user has
        //    something concrete to edit / the setup wizard to fill in.
        var def = new CompanionConfig { ConfigVersion = 2 };
        Save(def, path);
        return def;
    }

    // Fold a v1 file (top-level nina/sync, no rigs) into rigs[0]. Idempotent:
    // a v2 file with a populated rigs array is left untouched. NOTE: this is only
    // the CONFIG-SHAPE migration; relocating the flat on-disk data dir into
    // rigs/{id}/ is a separate, heavier step (CompanionMigration) the caller runs
    // once it knows the resolved data dir.
    public void MigrateFromV1() {
        if (Rigs.Count == 0 && RawNina != null && !string.IsNullOrWhiteSpace(RawNina.Host)) {
            var rig = new RigConfig {
                Id      = NewRigId(),
                Name    = RawNina.Host,           // default display name = host; user can rename
                Enabled = true,
                Nina    = RawNina,
                Sync    = RawSync ?? new RigConfig.SyncConfig(),
            };
            Rigs.Add(rig);
        }
        // Backfill ids/names on any rig that somehow lacks them (hand-edited file).
        foreach (var r in Rigs) {
            if (string.IsNullOrWhiteSpace(r.Id))   r.Id   = NewRigId();
            if (string.IsNullOrWhiteSpace(r.Name)) r.Name = string.IsNullOrWhiteSpace(r.Nina.Host) ? r.Id : r.Nina.Host;
        }
        // Migration consumed the legacy blocks — drop them so Save doesn't re-emit.
        RawNina = null;
        RawSync = null;
        ConfigVersion = 2;
    }

    // 8-char lowercase base32 (Crockford-ish, no padding) from a fresh GUID.
    // Short enough for a URL query param + a folder name, collision-safe for the
    // handful of rigs a user pairs.
    public static string NewRigId() {
        Span<byte> buf = stackalloc byte[8];
        RandomNumberGenerator.Fill(buf);
        const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
        Span<char> outc = stackalloc char[8];
        for (int i = 0; i < 8; i++) outc[i] = alphabet[buf[i] & 31];
        return new string(outc);
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
        cfg.ConfigVersion = 2;
        cfg.RawNina = null;   // never re-emit legacy blocks
        cfg.RawSync = null;
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var json = JsonSerializer.Serialize(cfg, SaveOpts);
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

    // ── Completeness ────────────────────────────────────────────────────────

    // Companion-level validity: at least one enabled rig that is itself complete.
    // Drives the "setup needed" UX in the dashboard so serve can start before the
    // user has paired any rig.
    public bool IsComplete(out string? reason) {
        if (Port <= 0 || Port > 65535) { reason = $"port {Port} out of range"; return false; }
        var enabled = Rigs.Where(r => r.Enabled).ToList();
        if (enabled.Count == 0) { reason = "no rig configured — run the setup wizard"; return false; }
        // Complete if ANY enabled rig is usable — a single broken rig among
        // several shouldn't black out the whole dashboard.
        if (enabled.Any(r => r.IsComplete())) { reason = null; return true; }
        enabled[0].IsComplete(out reason);
        return false;
    }

    public bool IsComplete() => IsComplete(out _);

    // First enabled+complete rig, else first enabled, else first overall, else null.
    // This is the "default rig" the dashboard shows when no ?rig= is supplied.
    public RigConfig? DefaultRig() =>
        Rigs.FirstOrDefault(r => r.Enabled && r.IsComplete())
        ?? Rigs.FirstOrDefault(r => r.Enabled)
        ?? Rigs.FirstOrDefault();
}

// One paired primary rig. Owns its own NINA endpoint + token + sync schedule.
// Its synced data lives under CompanionConfig.RigDataDir(Id).
public sealed class RigConfig {

    [JsonPropertyName("id")]      public string Id      { get; set; } = "";
    [JsonPropertyName("name")]    public string Name    { get; set; } = "";
    [JsonPropertyName("enabled")] public bool   Enabled { get; set; } = true;

    [JsonPropertyName("nina")] public NinaConfig Nina { get; set; } = new();
    [JsonPropertyName("sync")] public SyncConfig Sync { get; set; } = new();

    public class NinaConfig {
        [JsonPropertyName("host")] public string Host { get; set; } = "";
        [JsonPropertyName("port")] public int    Port { get; set; } = 8181;

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

        // Accept session-end push notifications from the primary (NINA). When
        // true (default) the companion's /api/companion/sync endpoint triggers a
        // sync whenever the primary POSTs to it after a session ends.
        [JsonPropertyName("acceptPush")]
        public bool AcceptPush { get; set; } = true;
    }

    public string ResolvedNinaUrl() {
        var host = string.IsNullOrWhiteSpace(Nina.Host) ? "localhost" : Nina.Host;
        return $"http://{host}:{Nina.Port}";
    }

    public bool IsComplete(out string? reason) {
        if (string.IsNullOrWhiteSpace(Nina.Host)) { reason = "nina.host is empty"; return false; }
        if (string.IsNullOrWhiteSpace(Nina.PairingToken)) {
            reason = "no pairing token — run the setup wizard"; return false;
        }
        if (Nina.Port <= 0 || Nina.Port > 65535) { reason = $"nina.port {Nina.Port} out of range"; return false; }
        reason = null; return true;
    }

    public bool IsComplete() => IsComplete(out _);
}
