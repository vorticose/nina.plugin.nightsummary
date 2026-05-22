using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;
using NINA.Plugin.NightSummary.Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// End-to-end integration tests for the companion pairing endpoints
    /// (<c>/api/companion/info</c>, <c>/api/companion/pair</c>,
    /// <c>/api/companion/revoke</c>). Spins up a real DashboardServer over
    /// loopback wired to a tempdir-backed CompanionTokenStore, then exercises
    /// every documented failure mode from the design doc plus the happy paths.
    /// </summary>
    public class CompanionPairingEndpointsTests : IAsyncLifetime {

        private string _tempRoot       = null!;
        private string _tokenStorePath = null!;
        private CompanionTokenStore _store = null!;
        private DashboardServer _server = null!;
        private HttpClient _http        = null!;
        private int _port;

        public async Task InitializeAsync() {
            _tempRoot       = Path.Combine(Path.GetTempPath(), $"ns_pair_test_{Guid.NewGuid():N}");
            _tokenStorePath = Path.Combine(_tempRoot, "companion_tokens.json");
            Directory.CreateDirectory(_tempRoot);
            _store = new CompanionTokenStore(_tokenStorePath);

            var paths    = new StubPaths(_tempRoot);
            var settings = new StubSettings();
            _server = new DashboardServer(
                data:        new StubDataSource(),
                settings:    settings,
                webAssets:   new StubWebAssets(),
                externalLog: new StubLogger(),
                paths:       paths,
                regen:       null,
                companion:   null,
                tokenStore:  _store);

            _port = GetFreePort();
            await _server.StartAsync(_port);
            _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_port}") };
            await Task.Delay(50); // let TCP listener bind
        }

        public async Task DisposeAsync() {
            try { await _server.StopAsync(); } catch { }
            _http?.Dispose();
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
        }

        // ── /api/companion/info ──────────────────────────────────────────────

        [Fact]
        public async Task Info_NoAuth_Returns200WithVersions() {
            var resp = await _http.GetAsync("/api/companion/info");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var r = doc.RootElement;

            Assert.True(r.GetProperty("hasNs").GetBoolean());
            Assert.Equal("test-ns-version", r.GetProperty("nsVersion").GetString());
            Assert.Equal("3.2.0.9001",      r.GetProperty("ninaVersion").GetString());
            Assert.Equal(0,                 r.GetProperty("pairedCount").GetInt32());
            Assert.True(r.TryGetProperty("minCompanionVersion", out _));
        }

        [Fact]
        public async Task Info_ReflectsPairedCount() {
            // Two tokens: one pairs, one stays unpaired. pairedCount must be 1.
            var paired   = FreshToken();
            var unpaired = FreshToken();
            _store.Add(paired);
            _store.Add(unpaired);

            await PostJson("/api/companion/pair", new { token = paired, companionName = "Mac mini" });

            using var doc = JsonDocument.Parse(await (await _http.GetAsync("/api/companion/info")).Content.ReadAsStringAsync());
            Assert.Equal(1, doc.RootElement.GetProperty("pairedCount").GetInt32());
        }

        [Fact]
        public async Task Info_RevokedTokenDoesNotCountAsPaired() {
            var token = FreshToken();
            _store.Add(token);
            await PostJson("/api/companion/pair", new { token, companionName = "Mac mini" });

            // Revoke the entry directly via the store (simulates the Options-panel path).
            var entry = _store.FindByToken(token)!;
            _store.Revoke(entry.Id);

            using var doc = JsonDocument.Parse(await (await _http.GetAsync("/api/companion/info")).Content.ReadAsStringAsync());
            Assert.Equal(0, doc.RootElement.GetProperty("pairedCount").GetInt32());
        }

        // ── /api/companion/pair — failure modes ──────────────────────────────

        [Fact]
        public async Task Pair_MissingToken_Returns400() {
            var resp = await PostJson("/api/companion/pair", new { companionName = "Mac mini" });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task Pair_MissingCompanionName_Returns400() {
            var resp = await PostJson("/api/companion/pair", new { token = FreshToken() });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task Pair_InvalidJson_Returns400() {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/companion/pair") {
                Content = new StringContent("{not json", Encoding.UTF8, "application/json"),
            };
            var resp = await _http.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task Pair_UnknownToken_Returns401UnknownToken() {
            var resp = await PostJson("/api/companion/pair",
                new { token = "ZZZZZZZZZZZZZZZZ", companionName = "Mac mini" });
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("unknown_token", doc.RootElement.GetProperty("error").GetString());
        }

        [Fact]
        public async Task Pair_RevokedToken_Returns401Revoked() {
            var token = FreshToken();
            var entry = _store.Add(token);
            _store.Revoke(entry.Id);

            var resp = await PostJson("/api/companion/pair", new { token, companionName = "Mac mini" });
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("revoked", doc.RootElement.GetProperty("error").GetString());
        }

        [Fact]
        public async Task Pair_AlreadyPairedWithDifferentCompanion_Returns409() {
            var token = FreshToken();
            _store.Add(token);

            var first = await PostJson("/api/companion/pair", new { token, companionName = "Mac mini" });
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            var second = await PostJson("/api/companion/pair", new { token, companionName = "Office laptop" });
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

            using var doc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
            Assert.Equal("already_paired",  doc.RootElement.GetProperty("error").GetString());
            Assert.Equal("Mac mini",        doc.RootElement.GetProperty("companionName").GetString());
        }

        [Fact]
        public async Task Pair_SameCompanionRebinds_Returns200() {
            // Same companion name on a second call (e.g. wizard re-run) — no conflict.
            var token = FreshToken();
            _store.Add(token);

            await PostJson("/api/companion/pair", new { token, companionName = "Mac mini" });
            var second = await PostJson("/api/companion/pair", new { token, companionName = "Mac mini" });

            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        }

        [Fact]
        public async Task Pair_StalePairing_SilentlyRebinds() {
            // The "I rebuilt the Mac mini" case: a stale pairing (>7 days since
            // lastUsedAt) is silently overwritten by a new companion. We can't
            // wait 7 days in a test, so backdate the on-disk file and have a
            // fresh store reload it.
            var token = FreshToken();
            var entry = _store.Add(token);
            _store.MarkPaired(entry.Id, "Old Mac");

            BackdatePairing(entry.Id, daysAgo: 8);

            // Recreate the server with a store that re-reads the backdated file.
            await _server.StopAsync();
            _store = new CompanionTokenStore(_tokenStorePath);
            _server = new DashboardServer(
                data: new StubDataSource(), settings: new StubSettings(),
                webAssets: new StubWebAssets(), externalLog: new StubLogger(),
                paths: new StubPaths(_tempRoot), regen: null,
                companion: null, tokenStore: _store);
            _port = GetFreePort();
            await _server.StartAsync(_port);
            _http.Dispose();
            _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_port}") };
            await Task.Delay(50);

            var resp = await PostJson("/api/companion/pair", new { token, companionName = "Mac mini v2" });
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            Assert.Equal("Mac mini v2", _store.FindById(entry.Id)!.CompanionName);
        }

        // ── /api/companion/pair — happy path ─────────────────────────────────

        [Fact]
        public async Task Pair_HappyPath_Returns200WithIds() {
            var token = FreshToken();
            var entry = _store.Add(token);

            var resp = await PostJson("/api/companion/pair", new { token, companionName = "Mac mini" });
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var r = doc.RootElement;
            Assert.Equal(entry.Id,          r.GetProperty("companionId").GetString());
            Assert.Equal("test-ns-version", r.GetProperty("nsVersion").GetString());
            Assert.Equal("3.2.0.9001",      r.GetProperty("ninaVersion").GetString());

            var reloaded = _store.FindById(entry.Id)!;
            Assert.True(reloaded.IsPaired);
            Assert.Equal("Mac mini", reloaded.CompanionName);
        }

        [Fact]
        public async Task Pair_AcceptsHyphenatedToken() {
            // Token store normalizes; the endpoint must accept whatever the user types.
            _store.Add("K4M29N3X7QR58VH2");

            var resp = await PostJson("/api/companion/pair",
                new { token = "K4M2-9N3X-7QR5-8VH2", companionName = "Mac mini" });
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        // ── /api/companion/revoke ────────────────────────────────────────────

        [Fact]
        public async Task Revoke_NoAuth_Returns401() {
            var resp = await PostJson("/api/companion/revoke", new { id = "deadbe" });
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        [Fact]
        public async Task Revoke_NonBearerScheme_Returns401() {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/companion/revoke") {
                Content = JsonContent(new { id = "deadbe" }),
            };
            req.Headers.Add("Authorization", "Basic dXNlcjpwYXNz");
            var resp = await _http.SendAsync(req);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        [Fact]
        public async Task Revoke_UnknownBearer_Returns401() {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/companion/revoke") {
                Content = JsonContent(new { id = "deadbe" }),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "NOTAVALIDTOKEN12");
            var resp = await _http.SendAsync(req);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        [Fact]
        public async Task Revoke_RevokedBearer_Returns401() {
            var token = FreshToken();
            var entry = _store.Add(token);
            await PostJson("/api/companion/pair", new { token, companionName = "Mac mini" });
            _store.Revoke(entry.Id); // revoked the calling token itself

            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/companion/revoke") {
                Content = JsonContent(new { id = entry.Id }),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var resp = await _http.SendAsync(req);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        [Fact]
        public async Task Revoke_MissingId_Returns400() {
            var token = FreshToken();
            _store.Add(token);
            await PostJson("/api/companion/pair", new { token, companionName = "Mac mini" });

            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/companion/revoke") {
                Content = JsonContent(new { }),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var resp = await _http.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task Revoke_UnknownId_Returns404() {
            var token = FreshToken();
            _store.Add(token);
            await PostJson("/api/companion/pair", new { token, companionName = "Mac mini" });

            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/companion/revoke") {
                Content = JsonContent(new { id = "deadbe" }),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var resp = await _http.SendAsync(req);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        [Fact]
        public async Task Revoke_HappyPath_Returns204_AndEntryIsRevoked() {
            // Two paired tokens: token A revokes token B via the endpoint.
            var tokenA = FreshToken();
            var tokenB = FreshToken();
            var entryB = _store.Add(tokenB);
            _store.Add(tokenA);
            await PostJson("/api/companion/pair", new { token = tokenA, companionName = "Caller" });
            await PostJson("/api/companion/pair", new { token = tokenB, companionName = "Target" });

            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/companion/revoke") {
                Content = JsonContent(new { id = entryB.Id }),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
            var resp = await _http.SendAsync(req);
            Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

            Assert.True(_store.FindById(entryB.Id)!.IsRevoked);
        }

        [Fact]
        public async Task Revoke_SelfRevocation_Returns204() {
            // Companion revokes its own token — caller's own id is valid input.
            var token = FreshToken();
            var entry = _store.Add(token);
            await PostJson("/api/companion/pair", new { token, companionName = "Mac mini" });

            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/companion/revoke") {
                Content = JsonContent(new { id = entry.Id }),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var resp = await _http.SendAsync(req);
            Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        }

        // ── Wrong HTTP methods ───────────────────────────────────────────────

        [Fact]
        public async Task Info_Post_Returns404() {
            // /api/companion/info is GET-only — POST falls through to the 404 branch.
            var resp = await _http.PostAsync("/api/companion/info",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        [Fact]
        public async Task Pair_Get_Returns404() {
            var resp = await _http.GetAsync("/api/companion/pair");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        // ── No-token-store mode (companion-side server) ──────────────────────

        [Fact]
        public async Task Endpoints_404WhenTokenStoreIsNull() {
            // Stand up a second server with no token store (mimics companion mode);
            // all three pairing routes must 404 with a clear message.
            var altPort = GetFreePort();
            var altServer = new DashboardServer(
                data: new StubDataSource(), settings: new StubSettings(),
                webAssets: new StubWebAssets(), externalLog: new StubLogger(),
                paths: new StubPaths(_tempRoot), regen: null,
                companion: null, tokenStore: null);
            try {
                await altServer.StartAsync(altPort);
                using var altHttp = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{altPort}") };
                await Task.Delay(50);

                Assert.Equal(HttpStatusCode.NotFound, (await altHttp.GetAsync("/api/companion/info")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound,
                    (await altHttp.PostAsync("/api/companion/pair",
                        new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound,
                    (await altHttp.PostAsync("/api/companion/revoke",
                        new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);
            } finally {
                await altServer.StopAsync();
            }
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private async Task<HttpResponseMessage> PostJson(string path, object body) {
            using var req = new HttpRequestMessage(HttpMethod.Post, path) {
                Content = JsonContent(body),
            };
            return await _http.SendAsync(req);
        }

        private static StringContent JsonContent(object body) =>
            new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static int GetFreePort() {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static string FreshToken() {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var bytes = new byte[16];
            RandomNumberGenerator.Fill(bytes);
            var sb = new StringBuilder(16);
            foreach (var b in bytes) sb.Append(alphabet[b % alphabet.Length]);
            return sb.ToString();
        }

        // Rewrites the sidecar JSON to push the entry's lastUsedAt/pairedAt
        // into the past, so the >7d "rebuild" branch can be exercised without
        // waiting a week.
        private void BackdatePairing(string id, int daysAgo) {
            var json = File.ReadAllText(_tokenStorePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var newTokens = new List<object>();
            foreach (var t in root.GetProperty("tokens").EnumerateArray()) {
                if (t.GetProperty("id").GetString() == id) {
                    var past = DateTime.UtcNow.AddDays(-daysAgo).ToString("o");
                    newTokens.Add(new {
                        id            = t.GetProperty("id").GetString(),
                        name          = (string?)null,
                        hash          = t.GetProperty("hash").GetString(),
                        createdAt     = past,
                        pairedAt      = past,
                        lastUsedAt    = past,
                        companionName = t.GetProperty("companionName").GetString(),
                    });
                } else {
                    newTokens.Add(JsonSerializer.Deserialize<object>(t.GetRawText())!);
                }
            }
            var rewritten = JsonSerializer.Serialize(new {
                version = root.GetProperty("version").GetInt32(),
                tokens  = newTokens,
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_tokenStorePath, rewritten);
        }

        // ── Stubs (parallel to CompanionExportTests) ─────────────────────────

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
            public string PluginVersion => "test-ns-version";
            public string Mode          => "primary";
            public string NinaVersion   => "3.2.0.9001";
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
