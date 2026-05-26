using NINA.Plugin.NightSummary.Companion;
using NINA.Plugin.NightSummary.Companion.Adapters;
using NINA.Plugin.NightSummary.Companion.Sync;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;
using NINA.Plugin.NightSummary.Server;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// End-to-end SyncEngine tests. Each test spins up a real DashboardServer
    /// (acting as the primary), drives the SyncEngine (acting as the companion)
    /// against it, and inspects the resulting data dir on disk.
    ///
    /// What we are guarding:
    ///   - DB pulled via VACUUM INTO + atomic replace
    ///   - Reports zip extracted faithfully (paths, mtimes preserved)
    ///   - Subsequent syncs detect orphans deleted on primary and remove them locally
    ///   - last_synced.json round-trips
    ///   - Auth wired correctly (without bearer key, sync fails)
    ///   - TS DB absent on primary → 404 → sync still succeeds
    /// </summary>
    public class CompanionSyncEngineTests : IAsyncLifetime {

        private string _primaryRoot   = null!;
        private string _companionRoot = null!;
        private string _primaryDb     = null!;
        private string _tokenStorePath = null!;
        private CompanionTokenStore _tokenStore = null!;
        private DashboardServer _primary = null!;
        private int _primaryPort;
        private const string Token = "SYNCTEST16CHARS!";

        public async Task InitializeAsync() {
            var stamp = Guid.NewGuid().ToString("N");
            _primaryRoot   = Path.Combine(Path.GetTempPath(), $"ns_sync_primary_{stamp}");
            _companionRoot = Path.Combine(Path.GetTempPath(), $"ns_sync_companion_{stamp}");
            Directory.CreateDirectory(Path.Combine(_primaryRoot, "reports"));
            Directory.CreateDirectory(_companionRoot);
            _primaryDb = Path.Combine(_primaryRoot, "nightsummary.sqlite");

            using (var conn = new SQLiteConnection($"Data Source={_primaryDb};Version=3;")) {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE thing (id INTEGER PRIMARY KEY, name TEXT); INSERT INTO thing (name) VALUES ('alpha'),('beta');";
                cmd.ExecuteNonQuery();
            }

            // Seed three report files including one in a livestack subdir
            var reports = Path.Combine(_primaryRoot, "reports");
            File.WriteAllText(Path.Combine(reports, "session-1.html"), "<html>1</html>");
            File.WriteAllText(Path.Combine(reports, "session-1.settings.json"), "{}");
            var ls = Path.Combine(reports, "session-1", "livestack");
            Directory.CreateDirectory(ls);
            File.WriteAllText(Path.Combine(ls, "livestack.json"), "{ \"v\": 1 }");

            _tokenStorePath = Path.Combine(_primaryRoot, "companion_tokens.json");
            _tokenStore = new CompanionTokenStore(_tokenStorePath);
            var paired = _tokenStore.Add(Token);
            _tokenStore.MarkPaired(paired.Id, "sync-test-companion");

            _primary = new DashboardServer(
                data:        new EmptyDataSource(),
                settings:    new PrimarySettings(),
                webAssets:   new EmptyWebAssets(),
                externalLog: new SilentLogger(),
                paths:       new RootPaths(_primaryRoot),
                regen:       null,
                companion:   null,
                tokenStore:  _tokenStore);
            _primaryPort = GetFreePort();
            await _primary.StartAsync(_primaryPort);
            await Task.Delay(50);
        }

        public async Task DisposeAsync() {
            try { await _primary.StopAsync(); } catch { }
            try { if (Directory.Exists(_primaryRoot))   Directory.Delete(_primaryRoot, true);   } catch { }
            try { if (Directory.Exists(_companionRoot)) Directory.Delete(_companionRoot, true); } catch { }
        }

        // ── Tests ────────────────────────────────────────────────────────────

        [Fact]
        public async Task Sync_FromScratch_PullsDbAndAllReports() {
            var (config, paths, log) = MakeCompanion(Token);
            using var http = MakeHttp(Token);
            var engine = new SyncEngine(config, paths, log, http);

            var result = await engine.SyncAsync();

            Assert.True(result.Success, result.Error);
            Assert.True(result.Reachable);
            Assert.True(File.Exists(paths.DatabasePath), "synced DB missing");
            Assert.True(File.Exists(Path.Combine(paths.ReportsDir, "session-1.html")));
            Assert.True(File.Exists(Path.Combine(paths.ReportsDir, "session-1.settings.json")));
            Assert.True(File.Exists(Path.Combine(paths.ReportsDir, "session-1", "livestack", "livestack.json")));
            Assert.Equal(3, result.FilesAdded);

            // last_synced.json should record success + a high-water mtime
            var state = SyncState.Load(Path.Combine(paths.DataDir, "last_synced.json"));
            Assert.NotNull(state.LastSuccessUtc);
            Assert.NotNull(state.LastReportMtimeUtc);
        }

        [Fact]
        public async Task Sync_DbContentMatchesPrimary() {
            var (config, paths, log) = MakeCompanion(Token);
            using var http = MakeHttp(Token);
            var engine = new SyncEngine(config, paths, log, http);

            await engine.SyncAsync();

            using var conn = new SQLiteConnection($"Data Source={paths.DatabasePath};Version=3;Read Only=True;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM thing ORDER BY id";
            using var reader = cmd.ExecuteReader();
            var names = new List<string>();
            while (reader.Read()) names.Add(reader.GetString(0));
            Assert.Equal(new[] { "alpha", "beta" }, names);
        }

        [Fact]
        public async Task Sync_OrphansLocallyAfterPrimaryDeletion() {
            var (config, paths, log) = MakeCompanion(Token);
            using var http = MakeHttp(Token);
            var engine = new SyncEngine(config, paths, log, http);
            await engine.SyncAsync();
            Assert.True(File.Exists(Path.Combine(paths.ReportsDir, "session-1.html")));

            // Delete a file on the primary — companion's next sync should mirror that
            File.Delete(Path.Combine(_primaryRoot, "reports", "session-1.html"));

            var second = await engine.SyncAsync();
            Assert.True(second.Success, second.Error);
            Assert.False(File.Exists(Path.Combine(paths.ReportsDir, "session-1.html")),
                "deleted-on-primary file should be removed locally");
            Assert.True(second.FilesDeleted >= 1);
        }

        [Fact]
        public async Task Sync_WrongToken_Fails() {
            var (config, paths, log) = MakeCompanion("WRONGTOKEN16CHRS");
            using var http = MakeHttp("WRONGTOKEN16CHRS");
            var engine = new SyncEngine(config, paths, log, http);

            var result = await engine.SyncAsync();
            Assert.False(result.Success);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public async Task Sync_ReachabilityCheck_FailsCleanly_WhenPrimaryDown() {
            var (config, paths, log) = MakeCompanion(Token, hostOverride: "127.0.0.1", portOverride: GetFreePort());
            using var http = new HttpClient { BaseAddress = new Uri(config.ResolvedNinaUrl()), Timeout = TimeSpan.FromSeconds(2) };
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
            var engine = new SyncEngine(config, paths, log, http);

            var result = await engine.SyncAsync();
            Assert.False(result.Reachable);
            Assert.False(result.Success);
        }

        [Fact]
        public async Task Sync_PreservesEntryMtimesFromZip() {
            var (config, paths, log) = MakeCompanion(Token);
            using var http = MakeHttp(Token);
            var engine = new SyncEngine(config, paths, log, http);

            // Stamp a known mtime on the primary. DOS zip format only carries the
            // local wall-clock with 2-second precision, so compare as local + tolerate
            // the rounding window.
            var stamped = DateTime.Now.AddDays(-3);
            var primaryHtml = Path.Combine(_primaryRoot, "reports", "session-1.html");
            File.SetLastWriteTime(primaryHtml, stamped);

            await engine.SyncAsync();

            var localHtml = Path.Combine(paths.ReportsDir, "session-1.html");
            var localMtime = File.GetLastWriteTime(localHtml);
            Assert.InRange((localMtime - stamped).Duration().TotalSeconds, 0, 3);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private (CompanionConfig, CompanionPaths, IDashboardLogger) MakeCompanion(
            string token, string? hostOverride = null, int? portOverride = null) {
            var config = new CompanionConfig {
                Port    = GetFreePort(),
                DataDir = _companionRoot,
                Nina = new CompanionConfig.NinaConfig {
                    Host   = hostOverride ?? "127.0.0.1",
                    Port   = portOverride ?? _primaryPort,
                    PairingToken = token,
                },
            };
            var paths = new CompanionPaths(_companionRoot);
            paths.EnsureExists();
            var log = new SilentLogger();
            return (config, paths, log);
        }

        private HttpClient MakeHttp(string key) {
            var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_primaryPort}"), Timeout = TimeSpan.FromMinutes(1) };
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            return http;
        }

        private static int GetFreePort() {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        // ── Inline stubs for the primary-side server ─────────────────────────

        private sealed class RootPaths : IDashboardPaths {
            private readonly string _r;
            public RootPaths(string r) { _r = r; }
            public string DataDir      => _r;
            public string ReportsDir   => Path.Combine(_r, "reports");
            public string LogsDir      => Path.Combine(_r, "logs");
            public string HipsCacheDir => Path.Combine(_r, "hips-cache");
            public string DatabasePath => Path.Combine(_r, "nightsummary.sqlite");
            public string ThumbsRoot   => Path.Combine(_r, "thumbs");
            public string ReportHtmlPath(string id)        => Path.Combine(ReportsDir, $"{id}.html");
            public string ReportSettingsPath(string id)    => Path.Combine(ReportsDir, $"{id}.settings.json");
            public string LivestackDir(string id)          => Path.Combine(ReportsDir, "livestack", id);
            public string LivestackManifestPath(string id) => Path.Combine(LivestackDir(id), "livestack.json");
            public string LivestackImagePath(string id, string f) => Path.Combine(LivestackDir(id), f);
        }

        private sealed class PrimarySettings : IPluginSettings {
            public NightSummarySettings Current { get; } = new NightSummarySettings();
            public void Save() { }
            public string PluginVersion => "test";
            public string Mode => "primary";
        }

        private sealed class SilentLogger : IDashboardLogger {
            public void Info(string m) { }
            public void Warn(string m) { }
            public void Error(string m, Exception? ex = null) { }
            public void Debug(string m) { }
        }

        private sealed class EmptyWebAssets : IWebAssets {
            public Task<byte[]?> ReadAsync(string n, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);
        }

        private sealed class EmptyDataSource : IDashboardDataSource {
            public Task<IReadOnlyList<SessionRecord>> GetAllSessionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SessionRecord>>(Array.Empty<SessionRecord>());
            public Task<SessionRecord?> GetSessionAsync(string id, CancellationToken ct = default) => Task.FromResult<SessionRecord?>(null);
            public Task<IReadOnlyList<ImageRecord>> GetImagesAsync(string id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ImageRecord>>(Array.Empty<ImageRecord>());
            public Task<IReadOnlyList<SessionEvent>> GetEventsAsync(string id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SessionEvent>>(Array.Empty<SessionEvent>());
            public Task<IReadOnlyList<TimingEvent>> GetTimingEventsAsync(string id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TimingEvent>>(Array.Empty<TimingEvent>());
            public Task<IReadOnlyList<TargetDetail>> GetTargetDetailsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TargetDetail>>(Array.Empty<TargetDetail>());
            public Task<IReadOnlyList<TargetSessionDetail>> GetSessionsForTargetAsync(string n, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TargetSessionDetail>>(Array.Empty<TargetSessionDetail>());
            public Task<IReadOnlyDictionary<string, double>> GetLatestPositionAnglesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<string, double>>(new Dictionary<string, double>());
            public Task<bool> IsTargetSchedulerAvailableAsync(CancellationToken ct = default) => Task.FromResult(false);
            public Task<IReadOnlyList<TsProjectInfo>> GetTSProjectsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TsProjectInfo>>(Array.Empty<TsProjectInfo>());
            public Task<TsApiSettings?> GetTSApiSettingsAsync(CancellationToken ct = default) => Task.FromResult<TsApiSettings?>(null);
            public Task<TsImageAugment?> GetTsImageAugmentAsync(string targetName, string filterName, DateTime ts, int windowSeconds, double exposureDurationSeconds, CancellationToken ct = default) => Task.FromResult<TsImageAugment?>(null);
            public Task<int> ResyncTsGradingAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(0);
            public Task<string?> LoadReportHtmlAsync(string id, CancellationToken ct = default) => Task.FromResult<string?>(null);
            public Task<byte[]?> LoadLivestackImageAsync(string id, string f, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);
            public Task<string?> LoadLivestackManifestAsync(string id, CancellationToken ct = default) => Task.FromResult<string?>(null);
        }
    }
}
