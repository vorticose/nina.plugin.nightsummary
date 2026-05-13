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

    private readonly CompanionConfig _config;
    private readonly CompanionPaths _paths;
    private readonly IDashboardLogger _log;
    private readonly object _httpGate = new();
    private HttpClient _http;
    private readonly bool _externalHttp;

    public SyncEngine(CompanionConfig config, CompanionPaths paths, IDashboardLogger log, HttpClient? http = null) {
        _config = config;
        _paths  = paths;
        _log    = log;
        _externalHttp = http != null;
        _http   = http ?? BuildHttp(config);
    }

    private static HttpClient BuildHttp(CompanionConfig config) {
        var c = new HttpClient { BaseAddress = new Uri(config.ResolvedNinaUrl()) };
        c.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.Nina.ApiKey ?? "");
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
        var fresh = BuildHttp(_config);
        lock (_httpGate) { _http = fresh; }
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
        string? PrimaryVersion);

    public async Task<SyncResult> SyncAsync(CancellationToken ct = default) {
        _paths.EnsureExists();
        var statePath = Path.Combine(_paths.DataDir, "last_synced.json");
        var state = SyncState.Load(statePath);
        state.LastAttemptUtc = DateTime.UtcNow;

        try {
            // 1 — Reachability + schema check
            var (reachable, primaryVersion, primarySchema) = await CheckHealthAsync(ct);
            state.PrimaryVersion = primaryVersion;
            state.PrimarySchema  = primarySchema;
            if (!reachable) {
                state.LastError = "primary unreachable";
                state.Save(statePath);
                _log.Warn($"Sync: primary unreachable at {_config.ResolvedNinaUrl()}");
                return new SyncResult(false, false, "primary unreachable", 0, 0, 0, 0, 0, null);
            }

            // 2 — Full remote manifest (used for orphan reconcile)
            var manifest = await FetchManifestAsync(since: null, ct);
            _log.Info($"Sync: remote manifest reports {manifest.Files.Length} file(s)");

            // 3 — Incremental reports zip
            var (added, updated) = await PullReportsZipAsync(state.LastReportMtimeUtc, ct);

            // 4 — Main DB
            var dbBytes = await PullSqliteAsync("/api/export/database", _paths.DatabasePath, ct);

            // 5 — TS DB (optional)
            var tsBytes = await TryPullSqliteAsync("/api/export/ts-database", _paths.TsDatabasePath, ct);

            // 6 — Orphan reconcile (only when manifest is non-empty — never nuke on bad response)
            int deleted = manifest.Files.Length > 0
                ? DeleteOrphans(manifest)
                : 0;

            // 7 — Persist
            var maxMtime = manifest.Files.Length > 0
                ? manifest.Files.Max(f => f.Mtime).ToUniversalTime()
                : (DateTime?)null;
            if (maxMtime.HasValue) state.LastReportMtimeUtc = maxMtime;
            state.LastSuccessUtc = DateTime.UtcNow;
            state.LastError      = null;
            state.Save(statePath);

            _log.Info($"Sync: ok — db={dbBytes}B ts={tsBytes}B reports added={added} updated={updated} deleted={deleted}");
            return new SyncResult(true, true, null, dbBytes, tsBytes, added, updated, deleted, primaryVersion);

        } catch (Exception ex) {
            state.LastError = ex.Message;
            state.Save(statePath);
            _log.Error("Sync failed", ex);
            return new SyncResult(true, false, ex.Message, 0, 0, 0, 0, 0, null);
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

    private async Task<ManifestResponse> FetchManifestAsync(DateTime? since, CancellationToken ct) {
        var url = "/api/export/manifest";
        if (since.HasValue) url += "?since=" + Uri.EscapeDataString(since.Value.ToString("o"));
        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<ManifestResponse>(json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            ?? new ManifestResponse();
    }

    // ── Step 3: reports zip ─────────────────────────────────────────────────

    private async Task<(int added, int updated)> PullReportsZipAsync(DateTime? since, CancellationToken ct) {
        var url = "/api/export/reports";
        if (since.HasValue) url += "?since=" + Uri.EscapeDataString(since.Value.ToString("o"));

        var tempZip = Path.Combine(Path.GetTempPath(), $"ns-companion-{Guid.NewGuid():N}.zip");
        try {
            using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)) {
                resp.EnsureSuccessStatusCode();
                using var src = await resp.Content.ReadAsStreamAsync(ct);
                using var dst = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None,
                                               81920, useAsync: true);
                await src.CopyToAsync(dst, ct);
            }

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
                var dest = Path.Combine(_paths.ReportsDir, safeRel);
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

    private async Task<long> PullSqliteAsync(string url, string destPath, CancellationToken ct) {
        var temp = destPath + ".incoming";
        try {
            using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)) {
                resp.EnsureSuccessStatusCode();
                using var src = await resp.Content.ReadAsStreamAsync(ct);
                using var dst = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
                                               81920, useAsync: true);
                await src.CopyToAsync(dst, ct);
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

    private async Task<long> TryPullSqliteAsync(string url, string destPath, CancellationToken ct) {
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
                    await src.CopyToAsync(dst, ct);
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

    // ── Step 6: orphan reconcile ────────────────────────────────────────────

    private int DeleteOrphans(ManifestResponse remote) {
        var remotePaths = new HashSet<string>(
            remote.Files.Select(f => Normalize(f.Path)),
            StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_paths.ReportsDir)) return 0;

        int deleted = 0;
        foreach (var local in Directory.EnumerateFiles(_paths.ReportsDir, "*", SearchOption.AllDirectories)) {
            var rel = Path.GetRelativePath(_paths.ReportsDir, local).Replace('\\', '/');
            if (!remotePaths.Contains(rel)) {
                try { File.Delete(local); deleted++; }
                catch (Exception ex) { _log.Warn($"Sync: could not delete orphan '{rel}': {ex.Message}"); }
            }
        }
        // Best-effort prune empty subdirs created by deletes
        foreach (var dir in Directory.EnumerateDirectories(_paths.ReportsDir, "*", SearchOption.AllDirectories)
                                     .OrderByDescending(d => d.Length)) {
            try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
            catch { /* ignore */ }
        }
        return deleted;
    }

    private static string Normalize(string p) => p.Replace('\\', '/').TrimStart('/');
}
