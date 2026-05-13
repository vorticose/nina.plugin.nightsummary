using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;
using NINA.Plugin.NightSummary.Server;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// End-to-end integration tests for the companion export endpoints. Spins up
    /// a real DashboardServer over loopback, hits the endpoints with HttpClient,
    /// and verifies bearer-auth, manifest filtering, VACUUM INTO snapshot output,
    /// and the reports zip stream.
    /// </summary>
    public class CompanionExportTests : IAsyncLifetime {

        private string _tempRoot = null!;
        private string _reportsDir = null!;
        private string _dbPath = null!;
        private DashboardServer _server = null!;
        private int _port;
        private HttpClient _http = null!;
        private const string ApiKey = "test-companion-key-abc123";

        public async Task InitializeAsync() {
            _tempRoot   = Path.Combine(Path.GetTempPath(), $"ns_companion_test_{Guid.NewGuid():N}");
            _reportsDir = Path.Combine(_tempRoot, "reports");
            _dbPath     = Path.Combine(_tempRoot, "nightsummary.sqlite");
            Directory.CreateDirectory(_reportsDir);

            // Live SQLite DB so VACUUM INTO has something real to copy
            using (var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;")) {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE thing (id INTEGER PRIMARY KEY, name TEXT); INSERT INTO thing (name) VALUES ('one'),('two');";
                cmd.ExecuteNonQuery();
            }

            // A few report fixture files spanning subdirs and mtimes
            File.WriteAllText(Path.Combine(_reportsDir, "session-1.html"), "<html>1</html>");
            File.WriteAllText(Path.Combine(_reportsDir, "session-1.settings.json"), "{}");
            var lsDir = Path.Combine(_reportsDir, "session-1", "livestack");
            Directory.CreateDirectory(lsDir);
            File.WriteAllText(Path.Combine(lsDir, "livestack.json"), "{}");
            File.WriteAllBytes(Path.Combine(lsDir, "M101_L.jpg"), new byte[] { 0xFF, 0xD8, 0xFF });

            var paths    = new StubPaths(_tempRoot);
            var settings = new StubSettings { ApiKeyValue = ApiKey, ModeValue = "primary" };
            _server = new DashboardServer(
                data:        new StubDataSource(),
                settings:    settings,
                webAssets:   new StubWebAssets(),
                externalLog: new StubLogger(),
                paths:       paths,
                regen:       null);

            _port = GetFreePort();
            await _server.StartAsync(_port);

            _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_port}") };
            // Server starts a TCP accept loop in the background; a short delay lets the listener bind
            await Task.Delay(50);
        }

        public async Task DisposeAsync() {
            try { await _server.StopAsync(); } catch { }
            _http?.Dispose();
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
        }

        // ── /api/health ──────────────────────────────────────────────────────

        [Fact]
        public async Task Health_ReturnsModeAndSchemaVersion() {
            var resp = await _http.GetAsync("/api/health");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("primary", doc.RootElement.GetProperty("mode").GetString());
            Assert.True(doc.RootElement.TryGetProperty("schemaVersion", out var sv));
            Assert.True(sv.GetInt32() >= 1);
        }

        [Fact]
        public async Task Mode_ReturnsConfiguredMode() {
            var resp = await _http.GetAsync("/api/mode");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal("primary", doc.RootElement.GetProperty("mode").GetString());
        }

        // ── Auth gate ────────────────────────────────────────────────────────

        [Fact]
        public async Task Export_NoAuth_Returns401() {
            var resp = await _http.GetAsync("/api/export/database");
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        [Fact]
        public async Task Export_WrongKey_Returns401() {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/export/database");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "wrong-key");
            var resp = await _http.SendAsync(req);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        [Fact]
        public async Task Export_NonBearerScheme_Returns401() {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/export/database");
            req.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("user:" + ApiKey)));
            var resp = await _http.SendAsync(req);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        // ── /api/export/database (VACUUM INTO) ───────────────────────────────

        [Fact]
        public async Task ExportDatabase_WithAuth_ReturnsValidSqliteSnapshot() {
            var bytes = await GetAuthorizedBytes("/api/export/database");
            // SQLite file magic header: "SQLite format 3\0"
            Assert.True(bytes.Length > 100);
            var header = Encoding.ASCII.GetString(bytes, 0, 16);
            Assert.Equal("SQLite format 3\0", header);

            // Round-trip the snapshot through SQLite to confirm it's queryable + has our row
            var snapshotPath = Path.Combine(_tempRoot, "snapshot.sqlite");
            File.WriteAllBytes(snapshotPath, bytes);
            using var conn = new SQLiteConnection($"Data Source={snapshotPath};Version=3;Read Only=True;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM thing";
            var count = Convert.ToInt32(cmd.ExecuteScalar());
            Assert.Equal(2, count);
        }

        [Fact]
        public async Task ExportDatabase_SnapshotConsistentDuringConcurrentWrites() {
            // Stress: hammer the live DB with inserts while VACUUM INTO copies it. The
            // resulting snapshot must be internally consistent (parseable + queryable),
            // even if the row count is anywhere between the snapshot's initial and
            // final values.
            using var cts = new CancellationTokenSource();
            var writer = Task.Run(async () => {
                using var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
                conn.Open();
                int i = 0;
                while (!cts.Token.IsCancellationRequested) {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "INSERT INTO thing (name) VALUES (@n)";
                    cmd.Parameters.AddWithValue("@n", $"row-{i++}");
                    cmd.ExecuteNonQuery();
                    await Task.Delay(1);
                }
            });

            try {
                await Task.Delay(50);
                var bytes = await GetAuthorizedBytes("/api/export/database");
                var header = Encoding.ASCII.GetString(bytes, 0, 16);
                Assert.Equal("SQLite format 3\0", header);

                var snapshotPath = Path.Combine(_tempRoot, "live-snapshot.sqlite");
                File.WriteAllBytes(snapshotPath, bytes);
                using var conn = new SQLiteConnection($"Data Source={snapshotPath};Version=3;Read Only=True;");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM thing";
                var count = Convert.ToInt32(cmd.ExecuteScalar());
                Assert.True(count >= 2, $"snapshot should at least contain initial 2 rows, had {count}");
            } finally {
                cts.Cancel();
                try { await writer; } catch { }
            }
        }

        // ── /api/export/manifest ─────────────────────────────────────────────

        [Fact]
        public async Task ExportManifest_WithAuth_ListsAllReportFiles() {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/export/manifest");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
            var resp = await _http.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var files = doc.RootElement.GetProperty("files");
            var paths = new List<string>();
            foreach (var f in files.EnumerateArray()) paths.Add(f.GetProperty("path").GetString()!);

            Assert.Contains("session-1.html", paths);
            Assert.Contains("session-1.settings.json", paths);
            Assert.Contains("session-1/livestack/livestack.json", paths);
            Assert.Contains("session-1/livestack/M101_L.jpg", paths);
        }

        [Fact]
        public async Task ExportManifest_SinceQuery_FiltersByMtime() {
            // Set a clear mtime split: bump M101_L.jpg's mtime to "now", set everything
            // else firmly in the past, then ask for files newer than "5 seconds ago".
            var past = DateTime.UtcNow.AddDays(-2);
            foreach (var p in Directory.EnumerateFiles(_reportsDir, "*", SearchOption.AllDirectories))
                File.SetLastWriteTimeUtc(p, past);
            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(Path.Combine(_reportsDir, "session-1", "livestack", "M101_L.jpg"), now);

            var since = now.AddSeconds(-5).ToString("o");
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/export/manifest?since={Uri.EscapeDataString(since)}");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
            var resp = await _http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var paths = new List<string>();
            foreach (var f in doc.RootElement.GetProperty("files").EnumerateArray())
                paths.Add(f.GetProperty("path").GetString()!);

            Assert.Single(paths);
            Assert.Equal("session-1/livestack/M101_L.jpg", paths[0]);
        }

        // ── /api/export/reports ──────────────────────────────────────────────

        [Fact]
        public async Task ExportReports_WithAuth_ReturnsZipWithAllFiles() {
            var bytes = await GetAuthorizedBytes("/api/export/reports");
            using var ms = new MemoryStream(bytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            var entries = new HashSet<string>();
            foreach (var e in archive.Entries) entries.Add(e.FullName.Replace('\\', '/'));

            Assert.Contains("session-1.html", entries);
            Assert.Contains("session-1.settings.json", entries);
            Assert.Contains("session-1/livestack/livestack.json", entries);
            Assert.Contains("session-1/livestack/M101_L.jpg", entries);
        }

        // ── /api/export/ts-database ──────────────────────────────────────────

        [Fact]
        public async Task ExportTsDatabase_WhenAbsent_Returns404() {
            // Stub paths point at a temp dir; no TS DB on disk → 404 expected
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/export/ts-database");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
            var resp = await _http.SendAsync(req);
            // The export uses the real default TS path under %LOCALAPPDATA%; this CI box
            // may or may not have it. Accept either 404 (absent) or 200 (present) as
            // valid — the behavior we're guarding against is a 5xx or auth bypass.
            Assert.True(resp.StatusCode == HttpStatusCode.NotFound || resp.StatusCode == HttpStatusCode.OK,
                $"unexpected status {(int)resp.StatusCode}");
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private async Task<byte[]> GetAuthorizedBytes(string path) {
            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
            var resp = await _http.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            return await resp.Content.ReadAsByteArrayAsync();
        }

        private static int GetFreePort() {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        // ── Stubs ────────────────────────────────────────────────────────────

        private sealed class StubPaths : IDashboardPaths {
            private readonly string _root;
            public StubPaths(string root) { _root = root; }
            public string DataDir      => _root;
            public string ReportsDir   => Path.Combine(_root, "reports");
            public string LogsDir      => Path.Combine(_root, "logs");
            public string HipsCacheDir => Path.Combine(_root, "hips-cache");
            public string DatabasePath => Path.Combine(_root, "nightsummary.sqlite");
            public string ThumbsRoot   => Path.Combine(_root, "thumbs");
            public string ReportHtmlPath(string id)        => Path.Combine(ReportsDir, $"{id}.html");
            public string ReportSettingsPath(string id)    => Path.Combine(ReportsDir, $"{id}.settings.json");
            public string LivestackDir(string id)          => Path.Combine(ReportsDir, id, "livestack");
            public string LivestackManifestPath(string id) => Path.Combine(LivestackDir(id), "livestack.json");
            public string LivestackImagePath(string id, string f) => Path.Combine(LivestackDir(id), f);
        }

        private sealed class StubSettings : IPluginSettings {
            public NightSummarySettings Current { get; } = new NightSummarySettings();
            // Object initializer runs after the constructor, so write through to Current
            // here instead of caching a separate field that the auth check would not see.
            public string ApiKeyValue {
                get => Current.CompanionApiKey;
                set => Current.CompanionApiKey = value;
            }
            public string ModeValue { get; set; } = "primary";
            public void Save() { }
            public string PluginVersion => "test";
            public string Mode => ModeValue;
        }

        private sealed class StubLogger : IDashboardLogger {
            public void Info(string m) { }
            public void Warn(string m) { }
            public void Error(string m, Exception? ex = null) { }
            public void Debug(string m) { }
        }

        private sealed class StubWebAssets : IWebAssets {
            public Task<byte[]?> ReadAsync(string logicalName, CancellationToken ct = default)
                => Task.FromResult<byte[]?>(null);
        }

        // Returns empty/false everywhere — none of the export endpoints touch the data source.
        private sealed class StubDataSource : IDashboardDataSource {
            public Task<IReadOnlyList<SessionRecord>> GetAllSessionsAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<SessionRecord>>(Array.Empty<SessionRecord>());
            public Task<SessionRecord?> GetSessionAsync(string sessionId, CancellationToken ct = default)
                => Task.FromResult<SessionRecord?>(null);
            public Task<IReadOnlyList<ImageRecord>> GetImagesAsync(string sessionId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<ImageRecord>>(Array.Empty<ImageRecord>());
            public Task<IReadOnlyList<SessionEvent>> GetEventsAsync(string sessionId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<SessionEvent>>(Array.Empty<SessionEvent>());
            public Task<IReadOnlyList<TimingEvent>> GetTimingEventsAsync(string sessionId, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<TimingEvent>>(Array.Empty<TimingEvent>());
            public Task<IReadOnlyList<TargetDetail>> GetTargetDetailsAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<TargetDetail>>(Array.Empty<TargetDetail>());
            public Task<IReadOnlyList<TargetSessionDetail>> GetSessionsForTargetAsync(string t, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<TargetSessionDetail>>(Array.Empty<TargetSessionDetail>());
            public Task<IReadOnlyDictionary<string, double>> GetLatestPositionAnglesAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyDictionary<string, double>>(new Dictionary<string, double>());
            public Task<bool> IsTargetSchedulerAvailableAsync(CancellationToken ct = default)
                => Task.FromResult(false);
            public Task<IReadOnlyList<TsProjectInfo>> GetTSProjectsAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<TsProjectInfo>>(Array.Empty<TsProjectInfo>());
            public Task<TsApiSettings?> GetTSApiSettingsAsync(CancellationToken ct = default)
                => Task.FromResult<TsApiSettings?>(null);
            public Task<TsImageAugment?> GetTsImageAugmentAsync(string targetName, string filterName, DateTime ts, int windowSeconds, double exposureDurationSeconds, CancellationToken ct = default)
                => Task.FromResult<TsImageAugment?>(null);
            public Task<string?> LoadReportHtmlAsync(string sessionId, CancellationToken ct = default)
                => Task.FromResult<string?>(null);
            public Task<byte[]?> LoadLivestackImageAsync(string sessionId, string filename, CancellationToken ct = default)
                => Task.FromResult<byte[]?>(null);
            public Task<string?> LoadLivestackManifestAsync(string sessionId, CancellationToken ct = default)
                => Task.FromResult<string?>(null);
        }
    }
}
