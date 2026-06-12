using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Companion.Adapters;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Companion.Sync;

// Pulls a fresh copy of nightsummary.sqlite + the schedulerdb (when present) +
// the reports/ tree from the NINA machine into the companion's data dir.
//
// Order of operations (matches COMPANION_PLAN.md):
//   1. /api/health                           reachable + schema-compat check
//   2. /api/export/manifest                  full file list (used for orphan reconcile)
//   3. /api/export/reports?since=lastMtime   incremental zip → extract over reports/
//   4. /api/export/database                  VACUUM INTO snapshot → atomic replace
//   5. /api/export/ts-database               same; 404 = skip
//   6. orphan delete                         files in local manifest but not remote
//   7. write last_synced.json
public sealed class SyncEngine {

    private readonly RigConfig _rig;
    private readonly int _dashboardPort;
    private readonly CompanionPaths _paths;
    private readonly IDashboardLogger _log;
    private readonly object _httpGate = new();
    private HttpClient _http;
    private readonly bool _externalHttp;

    // Server emits camelCase JSON; one shared reader instead of allocating fresh
    // options at every manifest deserialize.
    private static readonly JsonSerializerOptions ManifestJson =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // Canonical multi-rig ctor: one engine per paired rig. dashboardPort is the
    // companion's own listener port, advertised so the primary learns the push
    // URL — it's a companion-global value, not per-rig.
    public SyncEngine(RigConfig rig, int dashboardPort, CompanionPaths paths, IDashboardLogger log, HttpClient? http = null) {
        _rig           = rig;
        _dashboardPort = dashboardPort;
        _paths         = paths;
        _log           = log;
        _externalHttp  = http != null;
        _http          = http ?? BuildHttp(rig, dashboardPort);
    }

    // Single-rig convenience: drives the first rig. Used by the `sync` CLI path
    // and the existing test suite. Multi-rig serve constructs one engine per rig
    // via the ctor above.
    public SyncEngine(CompanionConfig config, CompanionPaths paths, IDashboardLogger log, HttpClient? http = null)
        : this(config.EnsureFirstRig(), config.Port, paths, log, http) { }

    private static HttpClient BuildHttp(RigConfig rig, int dashboardPort) {
        var c = new HttpClient { BaseAddress = new Uri(rig.ResolvedNinaUrl()) };
        c.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", rig.Nina.PairingToken ?? "");
        // Advertise our own dashboard port on every request so the primary
        // can auto-detect the reachable push URL (no manual entry needed in
        // NS Options). Pairs with TcpHttpRequest.CompanionDashboardPort +
        // RequireCompanionAuth.UpdatePushUrlFromRequest on the primary.
        c.DefaultRequestHeaders.TryAddWithoutValidation(
            "X-Companion-Dashboard-Port", dashboardPort.ToString());
        c.Timeout = TimeSpan.FromMinutes(30);
        return c;
    }

    // Hot-reload after the user edits config in the dashboard. Swap the live
    // HttpClient — in-flight calls keep using the old one until they finish;
    // next call uses the new one. We deliberately don't dispose the old client
    // because in-flight requests still hold the reference; let the GC collect
    // it when nothing's using it. No-op when the engine was constructed with
    // an externally owned HttpClient — that owner controls its lifecycle.
    public void Reconfigure() {
        if (_externalHttp) return;
        var fresh = BuildHttp(_rig, _dashboardPort);
        lock (_httpGate) { _http = fresh; }
    }

    // Live progress callback, set by the controller. Invoked on phase changes
    // and periodically during the big streamed downloads so the setup wizard can
    // show a moving indicator. Null = nobody's listening (e.g. CLI sync).
    public Action<CompanionSyncProgress>? OnProgress { get; set; }

    // User-facing phases the wizard counts through. Kept coarse on purpose — the
    // engine has ~11 internal steps, but the user only cares about the big ones.
    private const int TotalPhases = 5;

    private void Report(int step, string phase, long bytes = 0, string? detail = null) {
        try { OnProgress?.Invoke(new CompanionSyncProgress(phase, step, TotalPhases, bytes, detail)); }
        catch { /* progress is best-effort — never let it break a sync */ }
    }

    // CopyToAsync that emits byte progress for the current phase every ~512 KB.
    // Used for the three large transfers (reports zip, DB, thumbs zip) so a
    // multi-minute pull doesn't look frozen on a single phase label.
    private async Task CopyWithProgressAsync(System.IO.Stream src, System.IO.Stream dst,
                                             int step, string phase, CancellationToken ct) {
        var buffer = new byte[81920];
        long total = 0, lastReport = 0;
        int read;
        while ((read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0) {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            total += read;
            if (total - lastReport >= 524288) { Report(step, phase, total); lastReport = total; }
        }
        Report(step, phase, total);
    }

    public sealed record SyncResult(
        bool Reachable,
        bool Success,
        string? Error,
        long DbBytes,
        long TsDbBytes,
        int FilesAdded,
        int FilesUpdated,
        int FilesDeleted,
        int ThumbsAdded,
        int ThumbsUpdated,
        int ThumbsDeleted,
        string? PrimaryVersion);

    public async Task<SyncResult> SyncAsync(CancellationToken ct = default) {
        _paths.EnsureExists();
        var statePath = Path.Combine(_paths.DataDir, "last_synced.json");
        var state = SyncState.Load(statePath);
        state.LastAttemptUtc = DateTime.UtcNow;

        try {
            // 1 — Reachability + schema check
            Report(1, "Connecting to your imaging rig");
            var (reachable, primaryVersion, primarySchema) = await CheckHealthAsync(ct);
            state.PrimaryVersion = primaryVersion;
            state.PrimarySchema  = primarySchema;
            if (!reachable) {
                state.LastError = "primary unreachable";
                state.Save(statePath);
                _log.Warn($"Sync: primary unreachable at {_rig.ResolvedNinaUrl()}");
                return new SyncResult(false, false, "primary unreachable", 0, 0, 0, 0, 0, 0, 0, 0, null);
            }

            // 2 — Full remote manifest (used for orphan reconcile)
            var manifest = await FetchManifestAsync(since: null, ct);
            _log.Info($"Sync: remote manifest reports {manifest.Files.Length} file(s)");

            // 3 — Incremental reports zip
            Report(2, "Downloading reports");
            var (added, updated) = await PullReportsZipAsync(state.LastReportMtimeUtc, ct, 2, "Downloading reports");

            // 4 — Main DB
            Report(3, "Downloading database");
            var dbBytes = await PullSqliteAsync("/api/export/database", _paths.DatabasePath, ct, 3, "Downloading database");

            // 5 — TS DB (optional)
            var tsBytes = await TryPullSqliteAsync("/api/export/ts-database", _paths.TsDatabasePath, ct, 3, "Downloading database");

            // 6a — Reports orphan reconcile (only when manifest is non-empty — never nuke on bad response)
            int deleted = manifest.Files.Length > 0
                ? DeleteOrphans(manifest)
                : 0;

            // 5b — Tonight's Preview cache. Primary serves a snapshot of its
            // tonight-preview-cache.json so the companion can render the Stats
            // → Tonight tab without trying to hit the primary's TS API (which
            // listens on the primary's loopback in NINA's process and is
            // unreachable from the companion's network). 404 = primary has no
            // cache yet (no human loaded Tonight + no proactive refresh has
            // run). Best-effort — sync continues on failure.
            try {
                await TryPullTonightCacheAsync(ct);
            } catch (Exception ex) {
                _log.Warn($"Sync: tonight cache pull skipped — {ex.Message}");
            }

            // 6b — Thumbs: separate manifest + zip + orphan pass. Primary may
            // not have raw thumbnails enabled (older sessions, feature off) — the
            // server returns an empty manifest/zip in that case and we no-op.
            Report(4, "Downloading thumbnails");
            var thumbsManifest = await FetchThumbsManifestAsync(since: null, ct);
            _log.Info($"Sync: remote thumbs manifest reports {thumbsManifest.Files.Length} file(s)");
            var (tAdded, tUpdated) = await PullThumbsZipAsync(state.LastThumbMtimeUtc, ct, 4, "Downloading thumbnails");
            int tDeleted = thumbsManifest.Files.Length > 0
                ? DeleteThumbOrphans(thumbsManifest)
                : 0;

            // 7 — Persist
            Report(5, "Finishing up");
            if (MaxMtimeUtc(manifest) is { } maxMtime) state.LastReportMtimeUtc = maxMtime;
            if (MaxMtimeUtc(thumbsManifest) is { } maxThumbMtime) state.LastThumbMtimeUtc = maxThumbMtime;
            state.LastSuccessUtc = DateTime.UtcNow;
            state.LastError      = null;
            state.Save(statePath);

            _log.Info($"Sync: ok — db={dbBytes}B ts={tsBytes}B reports added={added} updated={updated} deleted={deleted}" +
                      $" thumbs added={tAdded} updated={tUpdated} deleted={tDeleted}");
            return new SyncResult(true, true, null, dbBytes, tsBytes, added, updated, deleted,
                                  tAdded, tUpdated, tDeleted, primaryVersion);

        } catch (Exception ex) {
            state.LastError = ex.Message;
            state.Save(statePath);
            _log.Error("Sync failed", ex);
            return new SyncResult(true, false, ex.Message, 0, 0, 0, 0, 0, 0, 0, 0, null);
        }
    }

    // ── Step 1: health ──────────────────────────────────────────────────────

    public async Task<(bool reachable, string? version, int? schema)> CheckHealthAsync(CancellationToken ct) {
        try {
            using var resp = await _http.GetAsync("/api/health", ct);
            if (!resp.IsSuccessStatusCode) return (false, null, null);
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var version = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
            int? schema = doc.RootElement.TryGetProperty("schemaVersion", out var s) && s.ValueKind == JsonValueKind.Number
                ? s.GetInt32() : null;
            return (true, version, schema);
        } catch (Exception ex) {
            _log.Debug($"health check failed: {ex.Message}");
            return (false, null, null);
        }
    }

    // ── Step 2: manifest ────────────────────────────────────────────────────

    private Task<ManifestResponse> FetchManifestAsync(DateTime? since, CancellationToken ct) =>
        FetchManifestFromAsync("/api/export/manifest", since, allow404: false, ct);

    // Shared manifest fetch for both the reports and thumbs endpoints. allow404
    // lets the thumbs caller treat a primary that predates the endpoint as "empty"
    // instead of throwing.
    private async Task<ManifestResponse> FetchManifestFromAsync(string endpoint, DateTime? since,
                                                                bool allow404, CancellationToken ct) {
        var url = endpoint;
        if (since.HasValue) url += "?since=" + Uri.EscapeDataString(since.Value.ToString("o"));
        using var resp = await _http.GetAsync(url, ct);
        if (allow404 && resp.StatusCode == System.Net.HttpStatusCode.NotFound) {
            _log.Info($"Sync: {endpoint} → 404 (primary predates this endpoint)");
            return new ManifestResponse();
        }
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<ManifestResponse>(json, ManifestJson) ?? new ManifestResponse();
    }

    // ── Step 3: reports zip ─────────────────────────────────────────────────

    private Task<(int added, int updated)> PullReportsZipAsync(DateTime? since, CancellationToken ct,
                                                              int step, string phase) =>
        PullZipAsync("/api/export/reports", _paths.ReportsDir, "ns-companion",
                     since, skipOn404: false, ct, step, phase);

    // Shared zip pull for the reports and thumbs trees: stream the (optionally
    // incremental) zip to a temp file, then extract each entry into rootDir with
    // path-traversal sanitisation, preserving the server's last-write time.
    // skipOn404 lets the thumbs caller no-op against a primary that predates the
    // endpoint; reports treat 404 as a hard error.
    private async Task<(int added, int updated)> PullZipAsync(string endpoint, string root, string tempPrefix,
                                                              DateTime? since, bool skipOn404,
                                                              CancellationToken ct, int step, string phase) {
        if (string.IsNullOrEmpty(root)) return (0, 0);

        var url = endpoint;
        if (since.HasValue) url += "?since=" + Uri.EscapeDataString(since.Value.ToString("o"));

        var tempZip = Path.Combine(Path.GetTempPath(), $"{tempPrefix}-{Guid.NewGuid():N}.zip");
        try {
            using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)) {
                if (skipOn404 && resp.StatusCode == System.Net.HttpStatusCode.NotFound) return (0, 0);
                resp.EnsureSuccessStatusCode();
                using var src = await resp.Content.ReadAsStreamAsync(ct);
                using var dst = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None,
                                               81920, useAsync: true);
                await CopyWithProgressAsync(src, dst, step, phase, ct);
            }

            Directory.CreateDirectory(root);
            int added = 0, updated = 0;
            using var fs = new FileStream(tempZip, FileMode.Open, FileAccess.Read);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries) {
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                var safeRel = SanitizeRelativePath(entry.FullName);
                if (safeRel == null) {
                    _log.Warn($"Sync: skipping suspicious zip entry '{entry.FullName}'");
                    continue;
                }
                var dest = Path.Combine(root, safeRel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                bool existed = File.Exists(dest);
                using (var es = entry.Open())
                using (var ds = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None)) {
                    await es.CopyToAsync(ds, ct);
                }
                // Counterpart to entry.LastWriteTime = info.LastWriteTime on the server
                // — DOS-time stored as local wall-clock, so write back as local.
                File.SetLastWriteTime(dest, entry.LastWriteTime.DateTime);
                if (existed) updated++; else added++;
            }
            return (added, updated);
        } finally {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
        }
    }

    // Refuses zip entries that try to escape the reports dir via "..", absolute
    // paths, or drive-rooted names. Returns the rel path unchanged on success.
    private static string? SanitizeRelativePath(string entryName) {
        var normalized = entryName.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal)) return null;
        if (Path.IsPathRooted(normalized)) return null;
        foreach (var part in normalized.Split('/')) {
            if (part == ".." || part == ".") return null;
        }
        return normalized;
    }

    // ── Step 4 & 5: SQLite snapshots ────────────────────────────────────────

    private async Task<long> PullSqliteAsync(string url, string destPath, CancellationToken ct,
                                             int step, string phase) {
        var temp = destPath + ".incoming";
        try {
            using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)) {
                resp.EnsureSuccessStatusCode();
                using var src = await resp.Content.ReadAsStreamAsync(ct);
                using var dst = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
                                               81920, useAsync: true);
                await CopyWithProgressAsync(src, dst, step, phase, ct);
            }
            // Atomic replace — never leave a half-written DB at the canonical path
            if (File.Exists(destPath)) File.Replace(temp, destPath, destPath + ".bak", ignoreMetadataErrors: true);
            else                       File.Move(temp, destPath);
            return new FileInfo(destPath).Length;
        } catch {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
    }

    private async Task<long> TryPullSqliteAsync(string url, string destPath, CancellationToken ct,
                                                int step, string phase) {
        try {
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) {
                _log.Info($"Sync: {url} → 404 (skipping; primary has no TS DB)");
                return 0;
            }
            resp.EnsureSuccessStatusCode();
            // Stream into temp then atomic replace, same as PullSqliteAsync but with the
            // already-fetched response so we don't double-request.
            var temp = destPath + ".incoming";
            try {
                using (var src = await resp.Content.ReadAsStreamAsync(ct))
                using (var dst = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
                                                81920, useAsync: true)) {
                    await CopyWithProgressAsync(src, dst, step, phase, ct);
                }
                if (File.Exists(destPath)) File.Replace(temp, destPath, destPath + ".bak", ignoreMetadataErrors: true);
                else                       File.Move(temp, destPath);
                return new FileInfo(destPath).Length;
            } catch {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                throw;
            }
        } catch (HttpRequestException ex) {
            _log.Warn($"Sync: {url} failed ({ex.Message}); continuing without TS DB");
            return 0;
        }
    }

    // Pulls the primary's tonight-preview-cache.json into the companion's data
    // dir. Atomic write via temp + replace. 404 = no cache yet, returns 0 (not
    // an error). The dashboard's HandleGetTonightPreview short-circuits to this
    // file in companion mode so Tonight tab renders without the unreachable
    // live TS API call. Side effect on the primary: stale cache triggers a
    // background TS refresh server-side, so the NEXT companion sync gets fresh
    // data even if no human ever loads Tonight on the primary.
    private async Task<long> TryPullTonightCacheAsync(CancellationToken ct) {
        var url = "/api/export/tonight-cache";
        try {
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) {
                _log.Info($"Sync: {url} → 404 (primary has no cache yet — will populate on next sync)");
                return 0;
            }
            resp.EnsureSuccessStatusCode();
            var destPath = Path.Combine(_paths.DataDir, "tonight-preview-cache.json");
            var temp = destPath + ".incoming";
            try {
                using (var src = await resp.Content.ReadAsStreamAsync(ct))
                using (var dst = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
                                                81920, useAsync: true)) {
                    await src.CopyToAsync(dst, ct);
                }
                if (File.Exists(destPath)) File.Replace(temp, destPath, null, ignoreMetadataErrors: true);
                else                       File.Move(temp, destPath);
                var bytes = new FileInfo(destPath).Length;
                _log.Info($"Sync: tonight cache pulled ({bytes} bytes)");
                return bytes;
            } catch {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                throw;
            }
        } catch (HttpRequestException ex) {
            _log.Warn($"Sync: {url} failed ({ex.Message}); continuing without tonight cache");
            return 0;
        }
    }

    // ── Step 6: orphan reconcile ────────────────────────────────────────────

    private int DeleteOrphans(ManifestResponse remote) =>
        DeleteOrphansIn(_paths.ReportsDir, remote, "orphan");

    // Deletes local files under root that the remote manifest no longer lists, then
    // prunes the empty dirs left behind. Shared by the reports and thumbs trees;
    // label only varies the warning text. No-op on a missing/empty root.
    private int DeleteOrphansIn(string? root, ManifestResponse remote, string label) {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return 0;

        var remotePaths = new HashSet<string>(
            remote.Files.Select(f => Normalize(f.Path)),
            StringComparer.OrdinalIgnoreCase);

        int deleted = 0;
        foreach (var local in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) {
            var rel = Path.GetRelativePath(root, local).Replace('\\', '/');
            if (!remotePaths.Contains(rel)) {
                try { File.Delete(local); deleted++; }
                catch (Exception ex) { _log.Warn($"Sync: could not delete {label} '{rel}': {ex.Message}"); }
            }
        }
        // Best-effort prune empty subdirs created by deletes
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                                     .OrderByDescending(d => d.Length)) {
            try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
            catch { /* ignore */ }
        }
        return deleted;
    }

    private static string Normalize(string p) => p.Replace('\\', '/').TrimStart('/');

    // Newest file mtime in a manifest as UTC, or null when the manifest is empty —
    // the high-water mark stored in sync state to drive the next incremental pull.
    private static DateTime? MaxMtimeUtc(ManifestResponse manifest) =>
        manifest.Files.Length > 0 ? manifest.Files.Max(f => f.Mtime).ToUniversalTime() : null;

    // ── Step 6b: thumbnail tree (manifest + zip + orphan) ───────────────────
    //
    // Thumbnails ride the same manifest/zip/orphan helpers as reports, against
    // the separate thumbs endpoints and rooted at ThumbsRoot so the two orphan
    // passes can never touch each other's tree. Thumbs tolerate a 404 (primary
    // predates the endpoint, or raw thumbnails disabled) — reports don't.

    private Task<ManifestResponse> FetchThumbsManifestAsync(DateTime? since, CancellationToken ct) =>
        FetchManifestFromAsync("/api/export/thumbs-manifest", since, allow404: true, ct);

    private Task<(int added, int updated)> PullThumbsZipAsync(DateTime? since, CancellationToken ct,
                                                             int step, string phase) =>
        PullZipAsync("/api/export/thumbs", _paths.ThumbsRoot, "ns-companion-thumbs",
                     since, skipOn404: true, ct, step, phase);

    private int DeleteThumbOrphans(ManifestResponse remote) =>
        DeleteOrphansIn(_paths.ThumbsRoot, remote, "orphan thumb");
}
