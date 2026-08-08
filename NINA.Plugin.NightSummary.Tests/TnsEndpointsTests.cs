using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;
using NINA.Plugin.NightSummary.Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// Integration tests for the /api/nightsummary/* (Touch 'N' Stars compat)
    /// endpoint namespace implemented in DashboardServer.Tns.cs. Mirrors the
    /// structure of DashboardServerReadOnlyTests: minimal stub dependencies,
    /// a real DashboardServer bound to a random free port, and an HttpClient
    /// talking to it over loopback. Every test owns its own StubPaths temp
    /// dir so tests can run concurrently without colliding on report files.
    ///
    /// TNS envelope shape (PascalCase, see DashboardServer.Tns.cs TnsEnvelope):
    ///   { Success, Response, Error, StatusCode, Type }
    /// Response is omitted from the JSON entirely on error responses
    /// (JsonIgnoreCondition.WhenWritingNull), so error-path tests must not
    /// probe for a "Response" property.
    /// </summary>
    public class TnsEndpointsTests {

        // ── Canned session ids ─────────────────────────────────────────────
        private const string SessionA = "aaaaaaaa-1111-4b2c-8d3e-111111111111"; // completed, 3 LIGHT images
        private const string SessionB = "bbbbbbbb-2222-4b2c-8d3e-222222222222"; // completed, 2 LIGHT images
        private const string SessionC = "cccccccc-3333-4b2c-8d3e-333333333333"; // in-progress (SessionEnd == MinValue)

        // ── Stubs ───────────────────────────────────────────────────────────

        private sealed class StubDataSource : IDashboardDataSource {
            public readonly List<SessionRecord> Sessions = new List<SessionRecord>();
            public readonly Dictionary<string, List<ImageRecord>> ImagesBySession = new Dictionary<string, List<ImageRecord>>();

            public Task<IReadOnlyList<SessionRecord>> GetAllSessionsAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<SessionRecord>>(Sessions);
            public Task<SessionRecord?> GetSessionAsync(string sessionId, CancellationToken ct = default)
                => Task.FromResult<SessionRecord?>(Sessions.FirstOrDefault(s => s.SessionId == sessionId));
            public Task<IReadOnlyList<ImageRecord>> GetImagesAsync(string sessionId, CancellationToken ct = default) {
                var list = ImagesBySession.TryGetValue(sessionId, out var imgs)
                    ? (IReadOnlyList<ImageRecord>)imgs
                    : Array.Empty<ImageRecord>();
                return Task.FromResult(list);
            }
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

        private static StubDataSource NewStubData() {
            var now = DateTime.UtcNow;
            var data = new StubDataSource();

            data.Sessions.Add(new SessionRecord {
                SessionId    = SessionA,
                SessionStart = now.AddHours(-3),
                SessionEnd   = now.AddHours(-1),
                ProfileName  = "TestProfile"
            });
            data.Sessions.Add(new SessionRecord {
                SessionId    = SessionB,
                SessionStart = now.AddHours(-6),
                SessionEnd   = now.AddHours(-4),
                ProfileName  = "TestProfile"
            });
            data.Sessions.Add(new SessionRecord {
                SessionId    = SessionC,
                SessionStart = now.AddMinutes(-30),
                SessionEnd   = DateTime.MinValue, // in progress — SessionEnd <= SessionStart
                ProfileName  = "TestProfile"
            });

            data.ImagesBySession[SessionA] = new List<ImageRecord> {
                NewLight(SessionA, "M31", 300, accepted: true),
                NewLight(SessionA, "M31", 300, accepted: true),
                NewLight(SessionA, "M31", 300, accepted: true),
            };
            data.ImagesBySession[SessionB] = new List<ImageRecord> {
                NewLight(SessionB, "M42", 180, accepted: true),
                NewLight(SessionB, "M42", 180, accepted: false, gradingStatus: -1), // rejected — not CountsAsAccepted
            };
            return data;
        }

        private static ImageRecord NewLight(string sessionId, string target, double exposureSeconds, bool accepted, int gradingStatus = -1) {
            return new ImageRecord {
                SessionId        = sessionId,
                Timestamp        = DateTime.UtcNow,
                TargetName       = target,
                Filter           = "L",
                ExposureDuration = exposureSeconds,
                Accepted         = accepted,
                GradingStatus    = gradingStatus,
                ImageType        = "LIGHT"
            };
        }

        private sealed class StubMaintenance : ISessionMaintenance {
            public readonly List<string> ResendCalls = new List<string>();
            public readonly List<string> DeleteCalls = new List<string>();
            public bool DeleteReturns = true;
            public Exception ResendException;

            public Task ResendAsync(string sessionId, CancellationToken ct = default) {
                ResendCalls.Add(sessionId);
                if (ResendException != null) throw ResendException;
                return Task.CompletedTask;
            }

            public Task<bool> DeleteAsync(string sessionId, CancellationToken ct = default) {
                DeleteCalls.Add(sessionId);
                return Task.FromResult(DeleteReturns);
            }
        }

        private sealed class StubSettings : IPluginSettings {
            private readonly NightSummarySettings _s = new NightSummarySettings();
            public NightSummarySettings Current => _s;
            public void Save() { }
            public string PluginVersion => "test";
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

        // Own private stub — unique temp dir per instance so tests never collide
        // on report files. DatabasePath must point at a real file: several TNS
        // handlers gate on File.Exists(dbPath) before consulting the (stub) data
        // source, so an absent file would make every session-list/status test
        // observe "no db" short-circuits instead of exercising the real logic.
        // ReportsDir is also created eagerly so tests may write a report file
        // before the server (which normally creates it) has been constructed.
        private sealed class StubPaths : IDashboardPaths {
            private readonly string _tmp;
            public StubPaths() {
                _tmp = Path.Combine(Path.GetTempPath(), "ns-tns-test-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_tmp);
                Directory.CreateDirectory(ReportsDir);
                File.WriteAllText(DatabasePath, "stub-db"); // never read by these endpoints; data comes from StubDataSource
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

        private static async Task<DashboardServer> StartServerAsync(
            int port, StubDataSource data, StubPaths paths,
            ISessionMaintenance maintenance = null, bool readOnly = false) {
            var srv = new DashboardServer(
                data:        data,
                settings:    new StubSettings(),
                webAssets:   new StubWebAssets(),
                externalLog: new StubLogger(),
                paths:       paths,
                regen:       null,
                maintenance: maintenance,
                readOnly:    readOnly);
            await srv.StartAsync(port, "localhost");
            return srv;
        }

        private static HttpClient NewClient(int port) =>
            new HttpClient { BaseAddress = new Uri($"http://localhost:{port}/"), Timeout = TimeSpan.FromSeconds(5) };

        private static async Task<JsonDocument> ParseAsync(HttpResponseMessage resp) {
            var body = await resp.Content.ReadAsStringAsync();
            return JsonDocument.Parse(body);
        }

        // ── Tests ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Status_ReturnsEnvelopeWithInstalled() {
            int port = GetFreePort();
            var srv = await StartServerAsync(port, NewStubData(), new StubPaths());
            try {
                using var client = NewClient(port);
                var resp = await client.GetAsync("/api/nightsummary/status");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                using var doc = await ParseAsync(resp);
                Assert.True(doc.RootElement.GetProperty("Success").GetBoolean());
                var response = doc.RootElement.GetProperty("Response");
                Assert.True(response.GetProperty("Installed").GetBoolean());
                // Only SessionA and SessionB have SessionEnd > SessionStart; SessionC is in-progress.
                Assert.Equal(2, response.GetProperty("SessionCount").GetInt32());
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task Sessions_ReturnsCompletedOnly_PascalCase() {
            int port = GetFreePort();
            var srv = await StartServerAsync(port, NewStubData(), new StubPaths());
            try {
                using var client = NewClient(port);
                var resp = await client.GetAsync("/api/nightsummary/sessions");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                using var doc = await ParseAsync(resp);
                var arr = doc.RootElement.GetProperty("Response");
                var items = arr.EnumerateArray().ToList();
                Assert.Equal(2, items.Count); // completed only — SessionC excluded

                var ids = items.Select(i => i.GetProperty("SessionId").GetString()).ToList();
                Assert.Contains(SessionA, ids);
                Assert.Contains(SessionB, ids);
                Assert.DoesNotContain(SessionC, ids);

                var a = items.Single(i => i.GetProperty("SessionId").GetString() == SessionA);
                Assert.False(string.IsNullOrEmpty(a.GetProperty("DisplayLabel").GetString()));
                Assert.False(a.GetProperty("HasReport").GetBoolean());
                Assert.Equal(3, a.GetProperty("ImageCount").GetInt32());

                var b = items.Single(i => i.GetProperty("SessionId").GetString() == SessionB);
                Assert.Equal(2, b.GetProperty("ImageCount").GetInt32());
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task Sessions_HasReportTrue_WhenFileExists() {
            int port = GetFreePort();
            var paths = new StubPaths();
            File.WriteAllText(Path.Combine(paths.ReportsDir, $"{SessionA}.html"), "<html>report</html>");
            var srv = await StartServerAsync(port, NewStubData(), paths);
            try {
                using var client = NewClient(port);
                var resp = await client.GetAsync("/api/nightsummary/sessions");
                using var doc = await ParseAsync(resp);
                var items = doc.RootElement.GetProperty("Response").EnumerateArray().ToList();

                var a = items.Single(i => i.GetProperty("SessionId").GetString() == SessionA);
                Assert.True(a.GetProperty("HasReport").GetBoolean());

                var b = items.Single(i => i.GetProperty("SessionId").GetString() == SessionB);
                Assert.False(b.GetProperty("HasReport").GetBoolean());
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task Report_404_WhenMissing() {
            int port = GetFreePort();
            var srv = await StartServerAsync(port, NewStubData(), new StubPaths());
            try {
                using var client = NewClient(port);
                var resp = await client.GetAsync($"/api/nightsummary/report/{SessionA}");
                Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
                using var doc = await ParseAsync(resp);
                Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task Report_200_ServesHtml() {
            int port = GetFreePort();
            var paths = new StubPaths();
            const string marker = "TNS-REPORT-MARKER-12345";
            File.WriteAllText(Path.Combine(paths.ReportsDir, $"{SessionA}.html"), $"<html><body>{marker}</body></html>");
            var srv = await StartServerAsync(port, NewStubData(), paths);
            try {
                using var client = NewClient(port);
                var resp = await client.GetAsync($"/api/nightsummary/report/{SessionA}");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                Assert.Equal("text/html", resp.Content.Headers.ContentType?.MediaType);
                var body = await resp.Content.ReadAsStringAsync();
                Assert.Contains(marker, body);
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task Report_InvalidId_400() {
            int port = GetFreePort();
            var srv = await StartServerAsync(port, NewStubData(), new StubPaths());
            try {
                using var client = NewClient(port);
                var resp = await client.GetAsync("/api/nightsummary/report/..%2fetc");
                Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task Resend_501_WhenNoMaintenance() {
            int port = GetFreePort();
            var srv = await StartServerAsync(port, NewStubData(), new StubPaths(), maintenance: null);
            try {
                using var client = NewClient(port);
                var resp = await client.PostAsync($"/api/nightsummary/sessions/{SessionA}/resend", new StringContent(""));
                Assert.Equal((HttpStatusCode)501, resp.StatusCode);
                using var doc = await ParseAsync(resp);
                Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task Resend_200_CallsMaintenance() {
            int port = GetFreePort();
            var maintenance = new StubMaintenance();
            var srv = await StartServerAsync(port, NewStubData(), new StubPaths(), maintenance: maintenance);
            try {
                using var client = NewClient(port);
                var resp = await client.PostAsync($"/api/nightsummary/sessions/{SessionA}/resend", new StringContent(""));
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                using var doc = await ParseAsync(resp);
                Assert.True(doc.RootElement.GetProperty("Success").GetBoolean());
                Assert.Contains(SessionA, maintenance.ResendCalls);
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task Resend_500_WhenMaintenanceThrows() {
            int port = GetFreePort();
            var maintenance = new StubMaintenance { ResendException = new InvalidOperationException("boom") };
            var srv = await StartServerAsync(port, NewStubData(), new StubPaths(), maintenance: maintenance);
            try {
                using var client = NewClient(port);
                var resp = await client.PostAsync($"/api/nightsummary/sessions/{SessionA}/resend", new StringContent(""));
                Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
                using var doc = await ParseAsync(resp);
                Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
                Assert.Contains("boom", doc.RootElement.GetProperty("Error").GetString());
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task Delete_200_CallsMaintenance() {
            int port = GetFreePort();
            var maintenance = new StubMaintenance { DeleteReturns = true };
            var srv = await StartServerAsync(port, NewStubData(), new StubPaths(), maintenance: maintenance);
            try {
                using var client = NewClient(port);
                var resp = await client.DeleteAsync($"/api/nightsummary/sessions/{SessionA}");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                using var doc = await ParseAsync(resp);
                Assert.True(doc.RootElement.GetProperty("Success").GetBoolean());
                Assert.Contains(SessionA, maintenance.DeleteCalls);
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task Delete_404_WhenMaintenanceReturnsFalse() {
            int port = GetFreePort();
            var maintenance = new StubMaintenance { DeleteReturns = false };
            var srv = await StartServerAsync(port, NewStubData(), new StubPaths(), maintenance: maintenance);
            try {
                using var client = NewClient(port);
                var resp = await client.DeleteAsync($"/api/nightsummary/sessions/{SessionA}");
                Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
                using var doc = await ParseAsync(resp);
                Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
                Assert.Contains(SessionA, maintenance.DeleteCalls);
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task ReadOnly_Resend_And_Delete_403() {
            int port = GetFreePort();
            var maintenance = new StubMaintenance();
            var srv = await StartServerAsync(port, NewStubData(), new StubPaths(), maintenance: maintenance, readOnly: true);
            try {
                using var client = NewClient(port);

                var resendResp = await client.PostAsync($"/api/nightsummary/sessions/{SessionA}/resend", new StringContent(""));
                Assert.Equal(HttpStatusCode.Forbidden, resendResp.StatusCode);

                var deleteResp = await client.DeleteAsync($"/api/nightsummary/sessions/{SessionA}");
                Assert.Equal(HttpStatusCode.Forbidden, deleteResp.StatusCode);

                Assert.Empty(maintenance.ResendCalls);
                Assert.Empty(maintenance.DeleteCalls);
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task UnknownTnsPath_404Envelope() {
            int port = GetFreePort();
            var srv = await StartServerAsync(port, NewStubData(), new StubPaths());
            try {
                using var client = NewClient(port);
                var resp = await client.GetAsync("/api/nightsummary/bogus");
                Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
                using var doc = await ParseAsync(resp);
                Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
            } finally { await srv.StopAsync(); }
        }
    }
}
