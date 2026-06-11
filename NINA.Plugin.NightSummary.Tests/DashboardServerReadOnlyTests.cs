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
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    /// <summary>
    /// Integration tests for read-only mode on DashboardServer. The server is
    /// constructed with minimal stub dependencies and started on a random free
    /// port so multiple tests can run concurrently without binding conflicts.
    ///
    /// Verifies the three observable read-only signals:
    ///   1. Every non-GET/HEAD method returns 403 (single chokepoint at top of
    ///      HandleRequest, before the POST route table is consulted).
    ///   2. The X-Read-Only response header is set on every response.
    ///   3. The served dashboard HTML carries data-readonly="true" on the <html>
    ///      element so CSS hides destructive UI.
    /// </summary>
    public class DashboardServerReadOnlyTests {

        // ── Stubs ───────────────────────────────────────────────────────────

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
            public Task<IReadOnlyList<TargetSessionDetail>> GetSessionsForTargetAsync(string targetName, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<TargetSessionDetail>>(Array.Empty<TargetSessionDetail>());
            public Task<IReadOnlyDictionary<string, double>> GetLatestPositionAnglesAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyDictionary<string, double>>(new Dictionary<string, double>());
            public Task<bool> IsTargetSchedulerAvailableAsync(CancellationToken ct = default) => Task.FromResult(false);
            public Task<IReadOnlyList<TsProjectInfo>> GetTSProjectsAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<TsProjectInfo>>(Array.Empty<TsProjectInfo>());
            public Task<TsApiSettings?> GetTSApiSettingsAsync(CancellationToken ct = default)
                => Task.FromResult<TsApiSettings?>(null);
            public Task<TsImageAugment?> GetTsImageAugmentAsync(string targetName, string filterName, DateTime timestamp, int windowSeconds, double exposureDurationSeconds, CancellationToken ct = default)
                => Task.FromResult<TsImageAugment?>(null);
            public Task<string?> LoadReportHtmlAsync(string sessionId, CancellationToken ct = default)
                => Task.FromResult<string?>(null);
            public Task<byte[]?> LoadLivestackImageAsync(string sessionId, string filename, CancellationToken ct = default)
                => Task.FromResult<byte[]?>(null);
            public Task<string?> LoadLivestackManifestAsync(string sessionId, CancellationToken ct = default)
                => Task.FromResult<string?>(null);
            public Task<int> ResyncTsGradingAsync(string sessionId, CancellationToken ct = default)
                => Task.FromResult(0);
        }

        private sealed class StubSettings : IPluginSettings {
            private readonly NightSummarySettings _s = new NightSummarySettings();
            private readonly string _version;
            public StubSettings(string version = "test") { _version = version; }
            public NightSummarySettings Current => _s;
            public void Save() { }
            public string PluginVersion => _version;
            public string Mode => "primary";
        }

        // Serves minimal HTML/CSS/JS/icon assets so the dashboard HTML can be built.
        private sealed class StubWebAssets : IWebAssets {
            public Task<byte[]?> ReadAsync(string name, CancellationToken ct = default) {
                byte[]? bytes = name switch {
                    "dashboard.html" => Encoding.UTF8.GetBytes(
                        "<!DOCTYPE html><html lang=\"en\"{{READONLY_ATTR}}><head><style>{{STYLES}}</style></head>" +
                        "<body><img src=\"{{ICON}}\"><span>v{{VERSION}}</span><script>{{SCRIPTS}}</script></body></html>"),
                    "dashboard.css"     => Encoding.UTF8.GetBytes("body { }"),
                    "dashboard.js"      => Encoding.UTF8.GetBytes("// stub"),
                    "flatpickr.min.css" => Encoding.UTF8.GetBytes(""),
                    "flatpickr.min.js"  => Encoding.UTF8.GetBytes(""),
                    "plugin-icon.png"   => Array.Empty<byte>(),
                    _                   => null
                };
                return Task.FromResult(bytes);
            }
        }

        private sealed class StubLogger : IDashboardLogger {
            public void Info(string m) { }
            public void Warn(string m) { }
            public void Error(string m, Exception? ex = null) { }
            public void Debug(string m) { }
        }

        private sealed class StubPaths : IDashboardPaths {
            private readonly string _tmp;
            public StubPaths() {
                _tmp = Path.Combine(Path.GetTempPath(), "ns-test-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_tmp);
            }
            public string DataDir       => _tmp;
            public string ReportsDir    => Path.Combine(_tmp, "reports");
            public string LogsDir       => Path.Combine(_tmp, "logs");
            public string HipsCacheDir  => Path.Combine(_tmp, "hips");
            public string DatabasePath  => Path.Combine(_tmp, "test.sqlite");
            public string ThumbsRoot    => Path.Combine(_tmp, "thumbs");
            public string ReportHtmlPath(string s)     => Path.Combine(ReportsDir, s + ".html");
            public string ReportSettingsPath(string s) => Path.Combine(ReportsDir, s + ".settings.json");
            public string LivestackDir(string s)       => Path.Combine(ReportsDir, "livestack", s);
            public string LivestackManifestPath(string s) => Path.Combine(LivestackDir(s), "manifest.json");
            public string LivestackImagePath(string s, string f) => Path.Combine(LivestackDir(s), f);
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        // Bind a TcpListener to port 0, grab the assigned port, release it. Brief
        // race with other listeners on the box, but the OS rarely re-issues a
        // freshly-released port within microseconds.
        private static int GetFreePort() {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private static async Task<DashboardServer> StartServerAsync(int port, bool readOnly, IPluginSettings settings = null) {
            var srv = new DashboardServer(
                data:        new StubDataSource(),
                settings:    settings ?? new StubSettings(),
                webAssets:   new StubWebAssets(),
                externalLog: new StubLogger(),
                paths:       new StubPaths(),
                regen:       null,
                readOnly:    readOnly);
            await srv.StartAsync(port, "localhost");
            return srv;
        }

        private static HttpClient NewClient(int port) =>
            new HttpClient { BaseAddress = new Uri($"http://localhost:{port}/"), Timeout = TimeSpan.FromSeconds(5) };

        // ── Tests ───────────────────────────────────────────────────────────

        [Fact]
        public async Task ReadOnly_PostRegenerateAll_Returns403() {
            int port = GetFreePort();
            var srv = await StartServerAsync(port, readOnly: true);
            try {
                using var client = NewClient(port);
                var resp = await client.PostAsync("/api/regenerate-all", new StringContent(""));
                Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task ReadOnly_PostTsOverride_Returns403() {
            int port = GetFreePort();
            var srv = await StartServerAsync(port, readOnly: true);
            try {
                using var client = NewClient(port);
                var resp = await client.PostAsync("/api/stats/ts/override", new StringContent("{}"));
                Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task ReadOnly_PostProjectReset_Returns403() {
            int port = GetFreePort();
            var srv = await StartServerAsync(port, readOnly: true);
            try {
                using var client = NewClient(port);
                var resp = await client.PostAsync("/api/stats/projects/abc-def/reset", new StringContent(""));
                Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task ReadOnly_PutMethod_Returns403() {
            int port = GetFreePort();
            var srv = await StartServerAsync(port, readOnly: true);
            try {
                using var client = NewClient(port);
                var resp = await client.PutAsync("/api/sessions/foo/regenerate", new StringContent(""));
                Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task ReadOnly_GetHealth_Returns200() {
            int port = GetFreePort();
            var srv = await StartServerAsync(port, readOnly: true);
            try {
                using var client = NewClient(port);
                var resp = await client.GetAsync("/api/health");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task ReadOnly_GetSessions_Returns200() {
            int port = GetFreePort();
            var srv = await StartServerAsync(port, readOnly: true);
            try {
                using var client = NewClient(port);
                var resp = await client.GetAsync("/api/sessions");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task ReadOnly_XReadOnlyHeader_PresentOnEveryResponse() {
            int port = GetFreePort();
            var srv = await StartServerAsync(port, readOnly: true);
            try {
                using var client = NewClient(port);
                var getResp = await client.GetAsync("/api/health");
                Assert.True(getResp.Headers.Contains("X-Read-Only"));

                var postResp = await client.PostAsync("/api/regenerate-all", new StringContent(""));
                Assert.True(postResp.Headers.Contains("X-Read-Only"),
                    "X-Read-Only header should be present even on 403 responses");
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task ReadOnly_DashboardHtml_HasDataReadonlyAttribute() {
            int port = GetFreePort();
            var srv = await StartServerAsync(port, readOnly: true);
            try {
                using var client = NewClient(port);
                var html = await client.GetStringAsync("/");
                Assert.Contains("data-readonly=\"true\"", html);
            } finally { await srv.StopAsync(); }
        }

        // ── Non-readonly regression guards ─────────────────────────────────

        [Fact]
        public async Task Normal_NoXReadOnlyHeader() {
            int port = GetFreePort();
            var srv = await StartServerAsync(port, readOnly: false);
            try {
                using var client = NewClient(port);
                var resp = await client.GetAsync("/api/health");
                Assert.False(resp.Headers.Contains("X-Read-Only"));
            } finally { await srv.StopAsync(); }
        }

        // Regression: a release build has the informational-version attribute
        // stripped, so IPluginSettings.PluginVersion can be "" (not null). /api/health
        // must still report a non-empty version (assembly fallback) or the companion's
        // sync bar shows "primary v?". (Bug found smoke-testing v3.2.0.)
        [Fact]
        public async Task Health_VersionNonEmpty_WhenSettingsVersionBlank() {
            int port = GetFreePort();
            var srv = await StartServerAsync(port, readOnly: false, settings: new StubSettings(""));
            try {
                using var client = NewClient(port);
                var json = await client.GetStringAsync("/api/health");
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var version = doc.RootElement.GetProperty("version").GetString();
                Assert.False(string.IsNullOrEmpty(version),
                    $"/api/health version must fall back when settings version is blank, got '{version}'");
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task Normal_DashboardHtml_NoDataReadonlyAttribute() {
            int port = GetFreePort();
            var srv = await StartServerAsync(port, readOnly: false);
            try {
                using var client = NewClient(port);
                var html = await client.GetStringAsync("/");
                Assert.DoesNotContain("data-readonly", html);
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task Normal_PostRegenerateAll_NotForbidden() {
            // In normal mode the route still exists; we just verify the short-circuit
            // isn't tripped. The actual handler may 400/500 due to stub data, but it
            // must not be 403 from the readOnly chokepoint.
            int port = GetFreePort();
            var srv = await StartServerAsync(port, readOnly: false);
            try {
                using var client = NewClient(port);
                var resp = await client.PostAsync("/api/regenerate-all", new StringContent("{}", Encoding.UTF8, "application/json"));
                Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
            } finally { await srv.StopAsync(); }
        }
    }
}
