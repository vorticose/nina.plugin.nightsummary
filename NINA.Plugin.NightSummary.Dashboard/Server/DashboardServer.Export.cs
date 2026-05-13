using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Server {

    // Companion R&D — export endpoints used by the standalone companion app to
    // pull a copy of the live data over HTTP. All routes here require a bearer
    // token (Authorization: Bearer <CompanionApiKey>); the dashboard itself stays
    // unauthenticated. See COMPANION_PLAN.md.
    public partial class DashboardServer {

        // ── Auth ──────────────────────────────────────────────────────────────

        // Returns true when the request carries the configured CompanionApiKey
        // as a bearer token. On failure, writes a 401 response and returns false
        // so callers can early-return without further work.
        private async Task<bool> RequireCompanionAuth(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            var configured = _settings.Current.CompanionApiKey;
            var authHeader = req.Authorization;
            if (string.IsNullOrEmpty(configured) ||
                string.IsNullOrEmpty(authHeader) ||
                !authHeader.StartsWith("Bearer ", StringComparison.Ordinal) ||
                !ConstantTimeEquals(authHeader.Substring("Bearer ".Length), configured)) {
                await WriteJson(res, 401, new { error = "unauthorized" });
                done?.Invoke(401, null);
                return false;
            }
            return true;
        }

        // Length-aware constant-time compare so a remote attacker cannot use
        // response-time differences to leak the configured key character by character.
        private static bool ConstantTimeEquals(string a, string b) {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        // ── /api/mode ─────────────────────────────────────────────────────────

        private async Task HandleGetMode(TcpHttpResponse res, Action<int, string> done) {
            await WriteJson(res, 200, new { mode = _settings.Mode ?? "primary" });
            done?.Invoke(200, null);
        }

        // ── /api/export/manifest ──────────────────────────────────────────────

        // Emits a JSON list of report files (path relative to reports/, mtime,
        // size). The companion uses this to compute the diff for incremental sync
        // and to detect orphans for deletion.
        private async Task HandleExportManifest(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            if (!await RequireCompanionAuth(req, res, done)) return;

            DateTimeOffset? since = ParseIsoQuery(req.QueryString["since"]);
            var files = new List<object>();
            if (Directory.Exists(reportsDir)) {
                foreach (var path in Directory.EnumerateFiles(reportsDir, "*", SearchOption.AllDirectories)) {
                    var info = new FileInfo(path);
                    if (since.HasValue && info.LastWriteTimeUtc <= since.Value.UtcDateTime) continue;
                    var relative = Path.GetRelativePath(reportsDir, path).Replace('\\', '/');
                    files.Add(new {
                        path  = relative,
                        size  = info.Length,
                        mtime = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero).ToString("o"),
                    });
                }
            }
            await WriteJson(res, 200, new { files });
            done?.Invoke(200, $"manifest: {files.Count} file(s)");
        }

        // ── /api/export/database ──────────────────────────────────────────────

        private async Task HandleExportDatabase(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            if (!await RequireCompanionAuth(req, res, done)) return;
            await StreamSqliteSnapshot(dbPath, res, done, "nightsummary.sqlite");
        }

        // ── /api/export/ts-database ───────────────────────────────────────────

        private async Task HandleExportTsDatabase(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            if (!await RequireCompanionAuth(req, res, done)) return;

            var tsPath = TargetSchedulerDbPath();
            if (!File.Exists(tsPath)) {
                await WriteJson(res, 404, new { error = "ts-database not found" });
                done?.Invoke(404, "ts-db absent");
                return;
            }
            // imagedata holds JPEG thumbnails (~95% of TS DB volume) and the
            // companion never reads it. Slim the snapshot before streaming so
            // sync time stays in seconds, not minutes.
            await StreamSqliteSnapshot(tsPath, res, done, "schedulerdb.sqlite",
                postSnapshotSql: "DELETE FROM imagedata; VACUUM;");
        }

        // Default TS DB location matches TargetSchedulerDatabase.DefaultDbPath
        // (kept private there). Inlined to avoid widening that public surface.
        private static string TargetSchedulerDbPath() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "SchedulerPlugin", "schedulerdb.sqlite");

        // VACUUM INTO produces a consistent snapshot of a live SQLite DB without
        // racing the writer. The destination temp file is streamed to the client
        // and deleted afterward. Falls back to a 500 if VACUUM INTO fails.
        // postSnapshotSql, when non-null, runs against the temp snapshot before
        // streaming — used by ts-database to drop the imagedata thumbnail blobs.
        private async Task StreamSqliteSnapshot(string sourceDb, TcpHttpResponse res, Action<int, string> done, string filename, string postSnapshotSql = null) {
            if (!File.Exists(sourceDb)) {
                await WriteJson(res, 404, new { error = "database not found" });
                done?.Invoke(404, $"db absent: {filename}");
                return;
            }

            // Temp file in a process-private dir so concurrent exports don't collide.
            var tempDir = Path.Combine(Path.GetTempPath(), "nightsummary-export");
            Directory.CreateDirectory(tempDir);
            var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.sqlite");

            try {
                // VACUUM INTO does not accept parameter binding for the path — must inline as a SQL string literal.
                // Doubling embedded single-quotes neutralizes any quoting in the temp path.
                var cs = $"Data Source={sourceDb};Version=3;Read Only=True;";
                using (var conn = new SQLiteConnection(cs)) {
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"VACUUM INTO '{tempPath.Replace("'", "''")}'";
                    cmd.ExecuteNonQuery();
                }

                if (!string.IsNullOrEmpty(postSnapshotSql)) {
                    var rwCs = $"Data Source={tempPath};Version=3;";
                    using var conn = new SQLiteConnection(rwCs);
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = postSnapshotSql;
                    cmd.ExecuteNonQuery();
                }

                var info = new FileInfo(tempPath);
                using var src = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                                               bufferSize: 81920, useAsync: true);
                res.StatusCode = 200;
                var headers = new Dictionary<string, string> {
                    { "Content-Disposition", $"attachment; filename=\"{filename}\"" },
                    { "Access-Control-Allow-Origin", "*" },
                };
                await res.StreamAsync("application/octet-stream", src, info.Length, headers);
                done?.Invoke(200, $"db export: {info.Length} bytes ({filename})");
            } catch (Exception ex) {
                log?.Error($"DB export failed for {filename}", ex);
                try { await WriteJson(res, 500, new { error = "export failed" }); } catch { }
                done?.Invoke(500, ex.Message);
            } finally {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best-effort */ }
            }
        }

        // ── /api/export/reports ───────────────────────────────────────────────

        // Streams the reports/ tree as a zip. With ?since=ISO8601 only files whose
        // mtime is strictly newer are included (matches manifest semantics so the
        // companion's diff and the zip stay in sync).
        private async Task HandleExportReports(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            if (!await RequireCompanionAuth(req, res, done)) return;

            DateTimeOffset? since = ParseIsoQuery(req.QueryString["since"]);

            // Build the zip in a temp file (not memory) — multi-GB libraries will OOM otherwise.
            var tempDir = Path.Combine(Path.GetTempPath(), "nightsummary-export");
            Directory.CreateDirectory(tempDir);
            var tempZip = Path.Combine(tempDir, $"{Guid.NewGuid():N}.zip");
            int included = 0;

            try {
                using (var fs = new FileStream(tempZip, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                               bufferSize: 81920, useAsync: true))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false)) {
                    if (Directory.Exists(reportsDir)) {
                        foreach (var path in Directory.EnumerateFiles(reportsDir, "*", SearchOption.AllDirectories)) {
                            var info = new FileInfo(path);
                            if (since.HasValue && info.LastWriteTimeUtc <= since.Value.UtcDateTime) continue;
                            var entryName = Path.GetRelativePath(reportsDir, path).Replace('\\', '/');
                            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                            // Zip DOS-time format has no TZ; storing local wall-clock so
                            // SetLastWriteTime on the companion side reproduces the same
                            // visible mtime when both machines share a TZ.
                            entry.LastWriteTime = info.LastWriteTime;
                            using var entryStream = entry.Open();
                            using var fileStream  = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                            await fileStream.CopyToAsync(entryStream);
                            included++;
                        }
                    }
                }

                var zipInfo = new FileInfo(tempZip);
                using var zipStream = new FileStream(tempZip, FileMode.Open, FileAccess.Read, FileShare.Read,
                                                    bufferSize: 81920, useAsync: true);
                res.StatusCode = 200;
                var headers = new Dictionary<string, string> {
                    { "Content-Disposition", "attachment; filename=\"reports.zip\"" },
                    { "Access-Control-Allow-Origin", "*" },
                    { "X-Reports-File-Count", included.ToString() },
                };
                await res.StreamAsync("application/zip", zipStream, zipInfo.Length, headers);
                done?.Invoke(200, $"reports zip: {included} file(s), {zipInfo.Length} bytes");
            } catch (Exception ex) {
                log?.Error("Reports export failed", ex);
                try { await WriteJson(res, 500, new { error = "export failed" }); } catch { }
                done?.Invoke(500, ex.Message);
            } finally {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { /* best-effort */ }
            }
        }

        // ── /api/export/thumbs-manifest ───────────────────────────────────────

        // Separate from the reports manifest so orphan-reconcile in each tree
        // operates independently (deleting a stale report should never nuke
        // thumbnails; deleting an old session's thumbs should never touch the
        // reports tree). Paths are relative to ThumbsRoot, mtime-filterable.
        private async Task HandleExportThumbsManifest(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            if (!await RequireCompanionAuth(req, res, done)) return;

            DateTimeOffset? since = ParseIsoQuery(req.QueryString["since"]);
            var root = _paths.ThumbsRoot;
            var files = new List<object>();
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root)) {
                foreach (var path in Directory.EnumerateFiles(root, "*.jpg", SearchOption.AllDirectories)) {
                    var info = new FileInfo(path);
                    if (since.HasValue && info.LastWriteTimeUtc <= since.Value.UtcDateTime) continue;
                    var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                    files.Add(new {
                        path  = relative,
                        size  = info.Length,
                        mtime = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero).ToString("o"),
                    });
                }
            }
            await WriteJson(res, 200, new { files });
            done?.Invoke(200, $"thumbs manifest: {files.Count} file(s)");
        }

        // ── /api/export/thumbs ────────────────────────────────────────────────

        // Streams the thumbs/ tree as a zip. Per-file mtime filter matches the
        // manifest. Returns an empty zip (not 404) when the dir is missing so
        // the companion's sync path doesn't need a special case for "thumbnails
        // never captured on this primary."
        private async Task HandleExportThumbs(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            if (!await RequireCompanionAuth(req, res, done)) return;

            DateTimeOffset? since = ParseIsoQuery(req.QueryString["since"]);
            var root = _paths.ThumbsRoot;

            var tempDir = Path.Combine(Path.GetTempPath(), "nightsummary-export");
            Directory.CreateDirectory(tempDir);
            var tempZip = Path.Combine(tempDir, $"{Guid.NewGuid():N}.zip");
            int included = 0;

            try {
                using (var fs = new FileStream(tempZip, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                               bufferSize: 81920, useAsync: true))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false)) {
                    if (!string.IsNullOrEmpty(root) && Directory.Exists(root)) {
                        foreach (var path in Directory.EnumerateFiles(root, "*.jpg", SearchOption.AllDirectories)) {
                            var info = new FileInfo(path);
                            if (since.HasValue && info.LastWriteTimeUtc <= since.Value.UtcDateTime) continue;
                            var entryName = Path.GetRelativePath(root, path).Replace('\\', '/');
                            // JPEGs do not compress meaningfully — NoCompression cuts
                            // CPU + temp-file size with no real size penalty.
                            var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
                            entry.LastWriteTime = info.LastWriteTime;
                            using var entryStream = entry.Open();
                            using var fileStream  = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                            await fileStream.CopyToAsync(entryStream);
                            included++;
                        }
                    }
                }

                var zipInfo = new FileInfo(tempZip);
                using var zipStream = new FileStream(tempZip, FileMode.Open, FileAccess.Read, FileShare.Read,
                                                    bufferSize: 81920, useAsync: true);
                res.StatusCode = 200;
                var headers = new Dictionary<string, string> {
                    { "Content-Disposition", "attachment; filename=\"thumbs.zip\"" },
                    { "Access-Control-Allow-Origin", "*" },
                    { "X-Thumbs-File-Count", included.ToString() },
                };
                await res.StreamAsync("application/zip", zipStream, zipInfo.Length, headers);
                done?.Invoke(200, $"thumbs zip: {included} file(s), {zipInfo.Length} bytes");
            } catch (Exception ex) {
                log?.Error("Thumbs export failed", ex);
                try { await WriteJson(res, 500, new { error = "export failed" }); } catch { }
                done?.Invoke(500, ex.Message);
            } finally {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { /* best-effort */ }
            }
        }

        // ── Shared helpers ────────────────────────────────────────────────────

        private static DateTimeOffset? ParseIsoQuery(string raw) {
            if (string.IsNullOrEmpty(raw)) return null;
            return DateTimeOffset.TryParse(raw, null,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var dt) ? dt : (DateTimeOffset?)null;
        }

        // Plugin assembly version — surfaced via /api/health so the companion can
        // refuse to sync against an incompatible primary.
        internal static string GetServerAssemblyVersion() =>
            typeof(DashboardServer).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion?.Split('+')[0] ?? "";

        // Schema version of the data the companion will receive. Bump when the
        // SQLite layout, sidecar JSON, or livestack manifest format changes in a
        // breaking way.
        internal const int CompanionSchemaVersion = 1;
    }
}
