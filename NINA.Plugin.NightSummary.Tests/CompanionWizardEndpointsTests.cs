using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;
using NINA.Plugin.NightSummary.Server;
using System;
using System.Collections.Generic;
using System.IO;
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
    /// Integration tests for the setup wizard's server-side routes
    /// (/setup, /setup.{js,css}, /api/setup/{probe,claim}) and the / ↔ /setup
    /// redirect that depends on companion config completeness.
    /// </summary>
    public class CompanionWizardEndpointsTests : IAsyncLifetime {

        private string _tempRoot = null!;
        private StubCompanion _companion = null!;
        private DashboardServer _server = null!;
        private HttpClient _http        = null!;
        private int _port;

        public async Task InitializeAsync() {
            _tempRoot  = Path.Combine(Path.GetTempPath(), $"ns_wizard_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempRoot);
            _companion = new StubCompanion();

            _server = NewServer(_companion);
            _port = GetFreePort();
            await _server.StartAsync(_port);
            // The HttpClient's default redirect-follow muddies status-code checks
            // for redirect tests; disable so 302 surfaces as 302.
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            _http = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{_port}") };
            await Task.Delay(50);
        }

        public async Task DisposeAsync() {
            try { await _server.StopAsync(); } catch { }
            _http?.Dispose();
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
        }

        private DashboardServer NewServer(StubCompanion? companion) {
            return new DashboardServer(
                data:        new StubDataSource(),
                settings:    new StubSettings(),
                webAssets:   new StubWebAssets(),
                externalLog: new StubLogger(),
                paths:       new StubPaths(_tempRoot),
                regen:       null,
                companion:   companion,
                tokenStore:  null);
        }

        // ── /setup and /setup.{js,css} ───────────────────────────────────────

        [Fact]
        public async Task SetupHtml_ServedInCompanionModeWhenIncomplete() {
            _companion.IsComplete = false;
            var resp = await _http.GetAsync("/setup");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Contains("text/html", resp.Content.Headers.ContentType?.MediaType ?? "");
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("Set up Night Summary Companion", body);
        }

        [Fact]
        public async Task SetupHtml_RedirectsToRootWhenAlreadyComplete() {
            _companion.IsComplete = true;
            var resp = await _http.GetAsync("/setup");
            Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
            Assert.Equal("/", resp.Headers.Location?.ToString());
        }

        [Fact]
        public async Task SetupJs_ServedWithCorrectContentType() {
            _companion.IsComplete = false;
            var resp = await _http.GetAsync("/setup.js");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Contains("javascript", resp.Content.Headers.ContentType?.MediaType ?? "");
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("showStep", body);
        }

        [Fact]
        public async Task SetupCss_ServedWithCorrectContentType() {
            _companion.IsComplete = false;
            var resp = await _http.GetAsync("/setup.css");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Contains("text/css", resp.Content.Headers.ContentType?.MediaType ?? "");
        }

        // ── / ↔ /setup redirect ──────────────────────────────────────────────

        [Fact]
        public async Task Root_RedirectsToSetupWhenIncomplete() {
            _companion.IsComplete = false;
            var resp = await _http.GetAsync("/");
            Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
            Assert.Equal("/setup", resp.Headers.Location?.ToString());
        }

        [Fact]
        public async Task Root_ServesDashboardWhenComplete() {
            _companion.IsComplete = true;
            var resp = await _http.GetAsync("/");
            // No dashboard asset stubbed → falls through to the asset error path,
            // but it's emphatically NOT a redirect. Either 200 with HTML or 5xx
            // — both prove the redirect-to-/setup branch did not fire.
            Assert.NotEqual(HttpStatusCode.Found, resp.StatusCode);
        }

        // ── /api/setup/probe ─────────────────────────────────────────────────

        [Fact]
        public async Task Probe_PassesQueryThroughToController() {
            _companion.ProbeResult = new CompanionProbeResult(
                Ok: true, NsVersion: "3.1.1", NinaVersion: "3.2.0.9001",
                HasNs: true, PairedCount: 2, MinCompanionVersion: "0.0.0", Error: null);

            var resp = await _http.GetAsync("/api/setup/probe?host=rig.local&port=8181");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var r = doc.RootElement;
            Assert.True(r.GetProperty("ok").GetBoolean());
            Assert.Equal("3.1.1",       r.GetProperty("nsVersion").GetString());
            Assert.Equal("3.2.0.9001",  r.GetProperty("ninaVersion").GetString());
            Assert.Equal(2,             r.GetProperty("pairedCount").GetInt32());

            Assert.Equal("rig.local", _companion.LastProbeHost);
            Assert.Equal(8181,        _companion.LastProbePort);
        }

        [Fact]
        public async Task Probe_PropagatesError_FromController() {
            _companion.ProbeResult = new CompanionProbeResult(false, null, null, false, 0, null, "timed out");
            var resp = await _http.GetAsync("/api/setup/probe?host=nowhere&port=1234");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("timed out", doc.RootElement.GetProperty("error").GetString());
        }

        // ── /api/setup/claim ─────────────────────────────────────────────────

        [Fact]
        public async Task Claim_HappyPath_ReturnsCompanionId() {
            _companion.ClaimResult = new CompanionClaimResult(
                Ok: true, CompanionId: "1ab8c2", NinaVersion: "3.2", NsVersion: "3.1.1",
                ErrorCode: null, Error: null, AlreadyPairedCompanionName: null);

            var resp = await PostJson("/api/setup/claim", new {
                host = "rig.local", port = 8181, token = "ABCD1234EFGH5678", companionName = "Mac mini",
            });
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var r = doc.RootElement;
            Assert.True(r.GetProperty("ok").GetBoolean());
            Assert.Equal("1ab8c2", r.GetProperty("companionId").GetString());

            // Controller received the form values verbatim.
            Assert.Equal("rig.local",        _companion.LastClaimHost);
            Assert.Equal(8181,               _companion.LastClaimPort);
            Assert.Equal("ABCD1234EFGH5678", _companion.LastClaimToken);
            Assert.Equal("Mac mini",         _companion.LastClaimName);
        }

        [Fact]
        public async Task Claim_AlreadyPaired_PropagatesErrorCodeAndOtherName() {
            _companion.ClaimResult = new CompanionClaimResult(
                Ok: false, CompanionId: null, NinaVersion: null, NsVersion: null,
                ErrorCode: "already_paired", Error: "already_paired",
                AlreadyPairedCompanionName: "Office laptop");

            var resp = await PostJson("/api/setup/claim", new {
                host = "rig.local", port = 8181, token = "X", companionName = "Mac mini",
            });
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var r = doc.RootElement;
            Assert.False(r.GetProperty("ok").GetBoolean());
            Assert.Equal("already_paired", r.GetProperty("errorCode").GetString());
            Assert.Equal("Office laptop",  r.GetProperty("alreadyPairedCompanionName").GetString());
        }

        [Fact]
        public async Task Claim_InvalidJson_Returns400() {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/setup/claim") {
                Content = new StringContent("{ broken", Encoding.UTF8, "application/json"),
            };
            var resp = await _http.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        // ── Primary mode (no _companion) ─────────────────────────────────────

        [Fact]
        public async Task AllSetupRoutes_404InPrimaryMode() {
            // Spin up a second server with companion=null.
            var altPort = GetFreePort();
            var alt = NewServer(companion: null);
            try {
                await alt.StartAsync(altPort);
                using var altHttp = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{altPort}") };
                await Task.Delay(50);

                Assert.Equal(HttpStatusCode.NotFound, (await altHttp.GetAsync("/setup")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await altHttp.GetAsync("/setup.js")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await altHttp.GetAsync("/setup.css")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await altHttp.GetAsync("/api/setup/probe?host=x&port=1")).StatusCode);
                using var req = new HttpRequestMessage(HttpMethod.Post, "/api/setup/claim") {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
                Assert.Equal(HttpStatusCode.NotFound, (await altHttp.SendAsync(req)).StatusCode);
            } finally {
                await alt.StopAsync();
            }
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private async Task<HttpResponseMessage> PostJson(string path, object body) {
            using var req = new HttpRequestMessage(HttpMethod.Post, path) {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            return await _http.SendAsync(req);
        }

        private static int GetFreePort() {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        // ── Stubs ────────────────────────────────────────────────────────────

        // Records the args of the most recent Probe/Claim call so tests can
        // assert the wizard's wire shape gets passed through unchanged.
        private sealed class StubCompanion : ICompanionController {
            public bool IsComplete { get; set; } = false;

            public CompanionProbeResult ProbeResult { get; set; } =
                new(true, "3.1.1", "3.2.0.9001", true, 0, "0.0.0", null);
            public CompanionClaimResult ClaimResult { get; set; } =
                new(true, "abc123", "3.2.0.9001", "3.1.1", null, null, null);

            public string? LastProbeHost { get; private set; }
            public int     LastProbePort { get; private set; }
            public string? LastClaimHost { get; private set; }
            public int     LastClaimPort { get; private set; }
            public string? LastClaimToken { get; private set; }
            public string? LastClaimName  { get; private set; }

            public CompanionSyncStatus GetStatus() => new(
                LastAttemptUtc: null, LastSuccessUtc: null, LastError: null,
                PrimaryVersion: null, PrimarySchema: null, DbBytes: 0, TsDbBytes: 0,
                FilesAdded: 0, FilesUpdated: 0, FilesDeleted: 0,
                ThumbsAdded: 0, ThumbsUpdated: 0, ThumbsDeleted: 0,
                IsRunning: false, PrimaryReachable: null, PrimaryLastCheckedUtc: null);

            public Task<CompanionSyncStatus> TriggerSyncAsync(CancellationToken ct = default) =>
                Task.FromResult(GetStatus());

            public bool IsSyncing => false;
            public Task PingPrimaryAsync(CancellationToken ct = default) => Task.CompletedTask;

            public CompanionConfigSnapshot GetConfig() => new(
                Host: "", Port: 0,
                DataDir: "", OnBoot: true,
                PollingIntervalHoursOnSuccess: 4, PollingIntervalMinutesOnFailure: 30,
                DashboardPort: 8182, IsComplete: IsComplete, IncompleteReason: null);

            public Task<CompanionConfigSaveResult> SaveConfigAsync(CompanionConfigEdit edit, CancellationToken ct = default)
                => Task.FromResult(new CompanionConfigSaveResult(true, null, GetConfig()));

            public Task<CompanionConfigTestResult> TestConnectionAsync(string host, int port, string apiKey, CancellationToken ct = default)
                => Task.FromResult(new CompanionConfigTestResult(true, "test", 1, null));

            public Task<CompanionProbeResult> ProbePrimaryAsync(string host, int port, CancellationToken ct = default) {
                LastProbeHost = host;
                LastProbePort = port;
                return Task.FromResult(ProbeResult);
            }

            public Task<CompanionClaimResult> ClaimPairingAsync(string host, int port, string token, string companionName, CancellationToken ct = default) {
                LastClaimHost  = host;
                LastClaimPort  = port;
                LastClaimToken = token;
                LastClaimName  = companionName;
                return Task.FromResult(ClaimResult);
            }

        }

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
            public void Save() { }
            public string PluginVersion => "test";
            public string Mode          => "companion";
        }

        private sealed class StubLogger : IDashboardLogger {
            public void Info(string m) { }
            public void Warn(string m) { }
            public void Error(string m, Exception? ex = null) { }
            public void Debug(string m) { }
        }

        // Serves the real embedded wizard assets so the setup HTML/JS/CSS
        // tests exercise the actual files, not a placeholder.
        private sealed class StubWebAssets : IWebAssets {
            public async Task<byte[]?> ReadAsync(string logicalName, CancellationToken ct = default) {
                var asm = typeof(DashboardServer).Assembly;
                using var s = asm.GetManifestResourceStream(logicalName);
                if (s == null) return null;
                using var ms = new MemoryStream();
                await s.CopyToAsync(ms, ct);
                return ms.ToArray();
            }
        }

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
            public Task<int> ResyncTsGradingAsync(string sessionId, CancellationToken ct = default)
                => Task.FromResult(0);
            public Task<string?> LoadReportHtmlAsync(string sessionId, CancellationToken ct = default)
                => Task.FromResult<string?>(null);
            public Task<byte[]?> LoadLivestackImageAsync(string sessionId, string filename, CancellationToken ct = default)
                => Task.FromResult<byte[]?>(null);
            public Task<string?> LoadLivestackManifestAsync(string sessionId, CancellationToken ct = default)
                => Task.FromResult<string?>(null);
        }
    }
}
