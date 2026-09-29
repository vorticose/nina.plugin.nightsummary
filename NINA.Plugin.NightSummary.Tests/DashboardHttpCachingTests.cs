using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;
using NINA.Plugin.NightSummary.Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    /// <summary>
    /// End-to-end checks of the dashboard's transport caching over a real
    /// DashboardServer on loopback: the dashboard page and session reports
    /// revalidate with an ETag (an unchanged page costs a 304, not ~800 KB), and
    /// responses are gzipped only for clients that send Accept-Encoding: gzip.
    /// HttpClient sends no Accept-Encoding by default, which is exactly how the
    /// companion's sync client talks to a primary, so plain responses must stay
    /// plain for it.
    /// </summary>
    public class DashboardHttpCachingTests {

        private const string SessionA = "aaaaaaaa-1111-4b2c-8d3e-111111111111";

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
            public NightSummarySettings Current => _s;
            public void Save() { }
            public string PluginVersion => "test";
            public string Mode => "primary";
        }

        // dashboard.js is padded past HttpCompression.MinBytes so the page is
        // big enough to be gzipped.
        private sealed class StubWebAssets : IWebAssets {
            public Task<byte[]?> ReadAsync(string name, CancellationToken ct = default) {
                byte[]? bytes = name switch {
                    "dashboard.html" => Encoding.UTF8.GetBytes(
                        "<!DOCTYPE html><html lang=\"en\"{{READONLY_ATTR}}><head><style>{{STYLES}}</style></head>" +
                        "<body><img src=\"{{ICON}}\"><span>v{{VERSION}}</span><script>{{SCRIPTS}}</script></body></html>"),
                    "dashboard.css"     => Encoding.UTF8.GetBytes("body { }"),
                    "dashboard.js"      => Encoding.UTF8.GetBytes("// stub\n" + string.Concat(Enumerable.Repeat("function f() { return 1; }\n", 200))),
                    "flatpickr.min.css" => Encoding.UTF8.GetBytes(""),
                    "flatpickr.min.js"  => Encoding.UTF8.GetBytes(""),
                    "report-icon.png"   => Array.Empty<byte>(),
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
                _tmp = Path.Combine(Path.GetTempPath(), "ns-cache-test-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_tmp);
                Directory.CreateDirectory(ReportsDir);
                File.WriteAllText(DatabasePath, "stub-db");
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

        private static int GetFreePort() {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private static async Task<(DashboardServer srv, HttpClient client)> StartAsync(StubPaths paths) {
            int port = GetFreePort();
            var srv = new DashboardServer(
                data:        new StubDataSource(),
                settings:    new StubSettings(),
                webAssets:   new StubWebAssets(),
                externalLog: new StubLogger(),
                paths:       paths,
                regen:       null);
            await srv.StartAsync(port, "localhost");
            var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}/"), Timeout = TimeSpan.FromSeconds(5) };
            return (srv, client);
        }

        private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string url,
                                                                string? ifNoneMatch = null, bool gzip = false) {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (ifNoneMatch != null) req.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
            if (gzip) req.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
            return await client.SendAsync(req);
        }

        private static string? Etag(HttpResponseMessage resp)
            => resp.Headers.TryGetValues("ETag", out var v) ? v.FirstOrDefault() : null;

        private static string? CacheControl(HttpResponseMessage resp)
            => resp.Headers.TryGetValues("Cache-Control", out var v) ? string.Join(",", v) : null;

        // ── Dashboard page ──────────────────────────────────────────────────

        [Fact]
        public async Task DashboardPage_RevalidatesWithEtag() {
            var (srv, client) = await StartAsync(new StubPaths());
            try {
                var first = await GetAsync(client, "/");
                Assert.Equal(HttpStatusCode.OK, first.StatusCode);
                var etag = Etag(first);
                Assert.False(string.IsNullOrEmpty(etag));
                Assert.Equal("no-cache", CacheControl(first));

                var again = await GetAsync(client, "/", ifNoneMatch: etag);
                Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
                Assert.Empty(await again.Content.ReadAsByteArrayAsync());

                var stale = await GetAsync(client, "/", ifNoneMatch: "\"something-else\"");
                Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
            } finally {
                await srv.StopAsync();
            }
        }

        [Fact]
        public async Task DashboardPage_GzippedOnlyWhenAsked() {
            var (srv, client) = await StartAsync(new StubPaths());
            try {
                var plain = await GetAsync(client, "/");
                Assert.Empty(plain.Content.Headers.ContentEncoding);
                var plainBody = await plain.Content.ReadAsByteArrayAsync();

                var zipped = await GetAsync(client, "/", gzip: true);
                Assert.Contains("gzip", zipped.Content.Headers.ContentEncoding);
                var zippedBody = await zipped.Content.ReadAsByteArrayAsync();
                Assert.True(zippedBody.Length < plainBody.Length);
                Assert.Equal(plainBody, Gunzip(zippedBody));
            } finally {
                await srv.StopAsync();
            }
        }

        // ── Session report ──────────────────────────────────────────────────

        [Fact]
        public async Task Report_RevalidatesUntilRegenerated() {
            var paths = new StubPaths();
            var reportPath = Path.Combine(paths.ReportsDir, SessionA + ".html");
            File.WriteAllText(reportPath, "<html><head></head><body>first</body></html>");
            File.SetLastWriteTimeUtc(reportPath, new DateTime(2026, 9, 1, 3, 0, 0, DateTimeKind.Utc));
            var (srv, client) = await StartAsync(paths);
            try {
                var url = $"/api/sessions/{SessionA}/report?theme=dark";
                var first = await GetAsync(client, url);
                Assert.Equal(HttpStatusCode.OK, first.StatusCode);
                var etag = Etag(first);
                Assert.False(string.IsNullOrEmpty(etag));

                var again = await GetAsync(client, url, ifNoneMatch: etag);
                Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);

                // The theme changes the injected overrides, so another theme is a different body.
                var otherTheme = await GetAsync(client, $"/api/sessions/{SessionA}/report?theme=light", ifNoneMatch: etag);
                Assert.Equal(HttpStatusCode.OK, otherTheme.StatusCode);

                // Regenerating the report (new mtime) must bust the cached copy.
                File.WriteAllText(reportPath, "<html><head></head><body>second</body></html>");
                File.SetLastWriteTimeUtc(reportPath, new DateTime(2026, 9, 2, 3, 0, 0, DateTimeKind.Utc));
                var regenerated = await GetAsync(client, url, ifNoneMatch: etag);
                Assert.Equal(HttpStatusCode.OK, regenerated.StatusCode);
                Assert.Contains("second", await regenerated.Content.ReadAsStringAsync());
                Assert.NotEqual(etag, Etag(regenerated));
            } finally {
                await srv.StopAsync();
            }
        }

        [Fact]
        public async Task Report_Missing_Is404NotCached() {
            var (srv, client) = await StartAsync(new StubPaths());
            try {
                var resp = await GetAsync(client, $"/api/sessions/{SessionA}/report");
                Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
                Assert.Null(Etag(resp));
            } finally {
                await srv.StopAsync();
            }
        }

        // ── Target thumbnails ───────────────────────────────────────────────

        private static readonly byte[] FakeJpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 0xFF, 0xD9 };

        private static string ReportWithThumb(string target, byte[] jpeg) =>
            "<div class='target-section'><h3>" + target + "</h3>" +
            "<div class='ts-thumb-wrap'><img src='data:image/jpeg;base64," + Convert.ToBase64String(jpeg) + "' alt='" + target + "' /></div>" +
            "</div>";

        [Fact]
        public async Task Thumbnails_ServedAsVersionedImageUrl_NotInlineBase64() {
            var paths = new StubPaths();
            var reportPath = Path.Combine(paths.ReportsDir, SessionA + ".html");
            File.WriteAllText(reportPath, ReportWithThumb("M31", FakeJpeg));
            var (srv, client) = await StartAsync(paths);
            try {
                var json = await client.GetStringAsync($"/api/sessions/{SessionA}/thumbnails");
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var src = doc.RootElement[0].GetProperty("dataUri").GetString()!;
                Assert.StartsWith($"/api/sessions/{SessionA}/target-thumb/0?v=", src);
                Assert.DoesNotContain("base64", json);

                var img = await client.GetAsync(src);
                Assert.Equal(HttpStatusCode.OK, img.StatusCode);
                Assert.Equal("image/jpeg", img.Content.Headers.ContentType?.MediaType);
                Assert.Contains("immutable", CacheControl(img));
                Assert.Equal(FakeJpeg, await img.Content.ReadAsByteArrayAsync());
            } finally {
                await srv.StopAsync();
            }
        }

        [Fact]
        public async Task Thumbnails_UrlCarriesRequestRig_ForImgTags() {
            var paths = new StubPaths();
            File.WriteAllText(Path.Combine(paths.ReportsDir, SessionA + ".html"), ReportWithThumb("M31", FakeJpeg));
            var (srv, client) = await StartAsync(paths);
            try {
                // Twice: the second answer comes from the (rig-neutral) cache.
                for (int i = 0; i < 2; i++) {
                    var json = await client.GetStringAsync($"/api/sessions/{SessionA}/thumbnails?rig=rig-b");
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    Assert.EndsWith("&rig=rig-b", doc.RootElement[0].GetProperty("dataUri").GetString());
                }
                var plain = await client.GetStringAsync($"/api/sessions/{SessionA}/thumbnails");
                Assert.DoesNotContain("rig=", plain);
            } finally {
                await srv.StopAsync();
            }
        }

        [Fact]
        public async Task Thumbnails_RegeneratedReport_GetsNewUrlVersion() {
            var paths = new StubPaths();
            var reportPath = Path.Combine(paths.ReportsDir, SessionA + ".html");
            File.WriteAllText(reportPath, ReportWithThumb("M31", FakeJpeg));
            File.SetLastWriteTimeUtc(reportPath, new DateTime(2026, 9, 1, 3, 0, 0, DateTimeKind.Utc));
            var (srv, client) = await StartAsync(paths);
            try {
                static async Task<string> Src(HttpClient c) {
                    using var d = System.Text.Json.JsonDocument.Parse(await c.GetStringAsync($"/api/sessions/{SessionA}/thumbnails"));
                    return d.RootElement[0].GetProperty("dataUri").GetString()!;
                }
                var before = await Src(client);
                File.WriteAllText(reportPath, ReportWithThumb("M31", new byte[] { 0xFF, 0xD8, 9, 9, 0xFF, 0xD9 }));
                File.SetLastWriteTimeUtc(reportPath, new DateTime(2026, 9, 2, 3, 0, 0, DateTimeKind.Utc));
                srv.InvalidateSessionCaches(SessionA); // what regenerate/resend do
                var after = await Src(client);
                Assert.NotEqual(before, after);
                Assert.Equal(new byte[] { 0xFF, 0xD8, 9, 9, 0xFF, 0xD9 }, await client.GetByteArrayAsync(after));
            } finally {
                await srv.StopAsync();
            }
        }

        [Theory]
        [InlineData("5")]
        [InlineData("-1")]
        [InlineData("abc")]
        public async Task TargetThumb_BadIndex_Is404(string index) {
            var paths = new StubPaths();
            File.WriteAllText(Path.Combine(paths.ReportsDir, SessionA + ".html"), ReportWithThumb("M31", FakeJpeg));
            var (srv, client) = await StartAsync(paths);
            try {
                var resp = await client.GetAsync($"/api/sessions/{SessionA}/target-thumb/{index}");
                Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            } finally {
                await srv.StopAsync();
            }
        }

        private static byte[] Gunzip(byte[] gz) {
            using var input = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress);
            using var output = new MemoryStream();
            input.CopyTo(output);
            return output.ToArray();
        }
    }
}
