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
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Tests for the dual-auth shim on the existing /api/export/* endpoints.
    /// Step 3 of COMPANION_PAIRING_DESIGN: those routes must accept either a
    /// pairing token from the store (preferred) or the legacy CompanionApiKey
    /// (transition window), and emit a one-shot deprecation warning when the
    /// legacy path fires.
    /// </summary>
    public class CompanionAuthShimTests : IAsyncLifetime {

        private const string LegacyApiKey = "legacy-api-key-abc123-XYZ";

        private string _tempRoot       = null!;
        private string _tokenStorePath = null!;
        private CompanionTokenStore _store = null!;
        private CapturingLogger _log = null!;
        private DashboardServer _server = null!;
        private HttpClient _http        = null!;
        private int _port;

        public async Task InitializeAsync() {
            _tempRoot       = Path.Combine(Path.GetTempPath(), $"ns_authshim_test_{Guid.NewGuid():N}");
            _tokenStorePath = Path.Combine(_tempRoot, "companion_tokens.json");
            Directory.CreateDirectory(Path.Combine(_tempRoot, "reports"));
            _store = new CompanionTokenStore(_tokenStorePath);
            _log   = new CapturingLogger();

            // A fixture file so /api/export/manifest returns a non-empty list
            // (exercises the full handler past the auth gate).
            File.WriteAllText(Path.Combine(_tempRoot, "reports", "session-1.html"), "<html>1</html>");

            _server = NewServer(_store);
            _port   = GetFreePort();
            await _server.StartAsync(_port);
            _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_port}") };
            await Task.Delay(50);
        }

        public async Task DisposeAsync() {
            try { await _server.StopAsync(); } catch { }
            _http?.Dispose();
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
        }

        private DashboardServer NewServer(ICompanionTokenStore? tokenStore) {
            return new DashboardServer(
                data:        new StubDataSource(),
                settings:    new StubSettings { Current = { CompanionApiKey = LegacyApiKey } },
                webAssets:   new StubWebAssets(),
                externalLog: _log,
                paths:       new StubPaths(_tempRoot),
                regen:       null,
                companion:   null,
                tokenStore:  tokenStore);
        }

        // ── Pairing-token path ────────────────────────────────────────────────

        [Fact]
        public async Task ValidPairingToken_GrantsAccess_And_BumpsLastUsedAt() {
            var token = FreshToken();
            var entry = _store.Add(token);
            _store.MarkPaired(entry.Id, "Mac mini");

            // Backdate lastUsedAt so the touch we're testing produces a
            // measurable delta. Without this the timestamps are too close to
            // distinguish reliably across the round-trip.
            BackdateLastUsedAt(entry.Id, secondsAgo: 60);
            var before = ReloadEntry(entry.Id)!.LastUsedAt!.Value;

            var resp = await BearerGet("/api/export/manifest", token);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var after = ReloadEntry(entry.Id)!.LastUsedAt!.Value;
            Assert.True(after > before,
                $"expected lastUsedAt to advance past {before:o}, got {after:o}");
        }

        [Fact]
        public async Task ValidPairingToken_NormalizesHyphens() {
            // Companion HTTP client should send the bare token, but a user
            // pasting from the wizard into a curl command might keep hyphens —
            // normalization lives in the store, the auth shim should inherit it.
            _store.Add("K4M29N3X7QR58VH2");
            var resp = await BearerGet("/api/export/manifest", "K4M2-9N3X-7QR5-8VH2");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        [Fact]
        public async Task RevokedPairingToken_FallsThroughToLegacy_Not_AutoFails() {
            // A revoked store entry must not short-circuit auth — if the user
            // still has the old apiKey configured we want them to keep working
            // until they re-pair. Use a token that ALSO happens not to equal
            // the legacy key (different value) → the legacy compare misses and
            // we land on 401, which is the right answer for "this exact
            // bearer is no longer valid for anything."
            var token = FreshToken();
            var entry = _store.Add(token);
            _store.Revoke(entry.Id);

            var resp = await BearerGet("/api/export/manifest", token);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
            Assert.Empty(_log.Warnings); // didn't trip the legacy path
        }

        [Fact]
        public async Task UnknownBearer_Returns401() {
            var resp = await BearerGet("/api/export/manifest", "NEVERREGISTERED1");
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        // ── Legacy CompanionApiKey path ──────────────────────────────────────

        [Fact]
        public async Task LegacyApiKey_StillWorks_Returns200() {
            // No pairing tokens in the store — only the legacy apiKey is configured.
            var resp = await BearerGet("/api/export/manifest", LegacyApiKey);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        [Fact]
        public async Task LegacyApiKey_EmitsOneShotDeprecationWarning() {
            await BearerGet("/api/export/manifest", LegacyApiKey);

            Assert.Single(_log.Warnings);
            Assert.Contains("legacy CompanionApiKey", _log.Warnings[0], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("re-pair",               _log.Warnings[0], StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task LegacyApiKey_WarningFiresOnlyOncePerSession() {
            for (int i = 0; i < 5; i++) {
                await BearerGet("/api/export/manifest", LegacyApiKey);
            }
            Assert.Single(_log.Warnings);
        }

        [Fact]
        public async Task PairingTokenTakesPrecedence_NoWarning() {
            // When the bearer matches a valid pairing token, the legacy compare
            // never runs and no deprecation warning fires.
            var token = FreshToken();
            _store.Add(token);

            await BearerGet("/api/export/manifest", token);
            Assert.Empty(_log.Warnings);
        }

        // ── Companion-mode server (no token store) ───────────────────────────

        [Fact]
        public async Task NoTokenStore_LegacyApiKeyStillAuthorizes() {
            // Companion-side DashboardServer instances run with tokenStore=null
            // (their own export endpoints stay legacy-key-gated until the
            // companion itself starts issuing pairing tokens, out of scope here).
            var altPort = GetFreePort();
            var alt     = NewServer(tokenStore: null);
            try {
                await alt.StartAsync(altPort);
                using var altHttp = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{altPort}") };
                await Task.Delay(50);

                using var req = new HttpRequestMessage(HttpMethod.Get, "/api/export/manifest");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", LegacyApiKey);
                var resp = await altHttp.SendAsync(req);
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            } finally {
                await alt.StopAsync();
            }
        }

        // ── Shape ────────────────────────────────────────────────────────────

        [Fact]
        public async Task MissingAuthorizationHeader_Returns401() {
            var resp = await _http.GetAsync("/api/export/manifest");
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        [Fact]
        public async Task NonBearerScheme_Returns401() {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/export/manifest");
            req.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("u:" + LegacyApiKey)));
            var resp = await _http.SendAsync(req);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private async Task<HttpResponseMessage> BearerGet(string path, string bearer) {
            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            return await _http.SendAsync(req);
        }

        private CompanionTokenEntry? ReloadEntry(string id) =>
            new CompanionTokenStore(_tokenStorePath).FindById(id);

        // Rewrites a single entry's lastUsedAt N seconds into the past so a
        // subsequent TouchLastUsed produces a measurable delta. Keeps the
        // sidecar's other fields intact via raw JSON manipulation.
        private void BackdateLastUsedAt(string id, int secondsAgo) {
            var json = File.ReadAllText(_tokenStorePath);
            var marker = $"\"id\": \"{id}\"";
            var idx = json.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(idx > 0, $"entry {id} not found in store JSON");
            // Find the next "lastUsedAt" property after this id.
            var luIdx = json.IndexOf("\"lastUsedAt\":", idx, StringComparison.Ordinal);
            Assert.True(luIdx > 0);
            var valueStart = json.IndexOf('"', luIdx + "\"lastUsedAt\":".Length) + 1;
            var valueEnd   = json.IndexOf('"', valueStart);
            var newStamp   = DateTime.UtcNow.AddSeconds(-secondsAgo).ToString("o");
            json = json.Substring(0, valueStart) + newStamp + json.Substring(valueEnd);
            File.WriteAllText(_tokenStorePath, json);

            // The in-memory _store cached the old value; force a reload by
            // swapping in a fresh instance for subsequent reads. The server's
            // own _tokenStore reference is the one we constructed in
            // InitializeAsync, so we also recreate the server to pick up the
            // backdated state cleanly.
            _server.StopAsync().GetAwaiter().GetResult();
            _store = new CompanionTokenStore(_tokenStorePath);
            _server = NewServer(_store);
            _port = GetFreePort();
            _server.StartAsync(_port).GetAwaiter().GetResult();
            _http.Dispose();
            _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_port}") };
            Thread.Sleep(50);
        }

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

        // ── Stubs ────────────────────────────────────────────────────────────

        private sealed class CapturingLogger : IDashboardLogger {
            public readonly List<string> Warnings = new();
            public void Info(string m) { }
            public void Warn(string m) { Warnings.Add(m); }
            public void Error(string m, Exception? ex = null) { }
            public void Debug(string m) { }
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
            public string Mode          => "primary";
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
