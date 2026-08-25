using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;
using NINA.Plugin.NightSummary.Dashboard.WebAssets;
using NINA.Plugin.NightSummary.Server;
using NINA.Plugin.NightSummary.Tests.Fixtures;
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
    /// GET /api/sessions must compute moon from SessionStart. A huge report
    /// HTML whose moon box says 99% must not win.
    /// </summary>
    public class SessionListMoonHttpTests {

        private sealed class ListStubDataSource : IDashboardDataSource {
            public IReadOnlyList<SessionRecord> Sessions { get; set; } = Array.Empty<SessionRecord>();
            public IReadOnlyList<ImageRecord> Images { get; set; } = Array.Empty<ImageRecord>();

            public Task<IReadOnlyList<SessionRecord>> GetAllSessionsAsync(CancellationToken ct = default)
                => Task.FromResult(Sessions);
            public Task<SessionRecord?> GetSessionAsync(string sessionId, CancellationToken ct = default)
                => Task.FromResult<SessionRecord?>(null);
            public Task<IReadOnlyList<ImageRecord>> GetImagesAsync(string sessionId, CancellationToken ct = default)
                => Task.FromResult(Images);
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
            public StubPaths(string tmp) { DataDir = tmp; }
            public string DataDir       { get; }
            public string ReportsDir    => Path.Combine(DataDir, "reports");
            public string LogsDir       => Path.Combine(DataDir, "logs");
            public string HipsCacheDir  => Path.Combine(DataDir, "hips");
            public string DatabasePath  => Path.Combine(DataDir, "test.sqlite");
            public string ThumbsRoot    => Path.Combine(DataDir, "thumbs");
            public string ReportHtmlPath(string s)     => Path.Combine(ReportsDir, s + ".html");
            public string ReportSettingsPath(string s) => Path.Combine(ReportsDir, s + ".settings.json");
            public string LivestackDir(string s)       => Path.Combine(ReportsDir, "livestack", s);
            public string LivestackManifestPath(string s) => Path.Combine(LivestackDir(s), "manifest.json");
            public string LivestackImagePath(string s, string f) => Path.Combine(LivestackDir(s), f);
        }

        private static int GetFreePort() {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        [Fact]
        public async Task GetSessions_JunkHtmlMoonBox_DoesNotWin() {
            var tmp = Path.Combine(Path.GetTempPath(), "ns-moon-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            Directory.CreateDirectory(Path.Combine(tmp, "reports"));
            // HandleGetSessions bails to [] if DatabasePath is missing.
            File.WriteAllBytes(Path.Combine(tmp, "test.sqlite"), Array.Empty<byte>());

            var newMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
            var session = TestDataFactory.MakeSession("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", newMoon);
            Assert.True(session.SessionEnd > session.SessionStart);

            var junk = new string('x', 2 * 1024 * 1024)
                       + "<div class='stat-value'>99% \u2191</div><div class='stat-label'>Moon</div>";
            File.WriteAllText(Path.Combine(tmp, "reports", session.SessionId + ".html"), junk);

            var data = new ListStubDataSource {
                Sessions = new[] { session },
                Images   = Array.Empty<ImageRecord>()
            };
            var paths = new StubPaths(tmp);
            int port = GetFreePort();
            var srv = new DashboardServer(
                data:        data,
                settings:    new StubSettings(),
                webAssets:   new StubWebAssets(),
                externalLog: new StubLogger(),
                paths:       paths,
                regen:       null,
                readOnly:    false);
            await srv.StartAsync(port, "localhost");
            try {
                using var client = new HttpClient {
                    BaseAddress = new Uri($"http://localhost:{port}/"),
                    Timeout     = TimeSpan.FromSeconds(10)
                };
                var resp = await client.GetAsync("/api/sessions");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                var body = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
                Assert.Equal(1, doc.RootElement.GetArrayLength());
                var row = doc.RootElement[0];
                Assert.Equal("0% \u2191", row.GetProperty("moonPhase").GetString());
                Assert.True(row.TryGetProperty("targets", out var targets));
                Assert.Equal(JsonValueKind.Array, targets.ValueKind);
                Assert.Equal(0, targets.GetArrayLength());
                Assert.DoesNotContain("99%", body);
            } finally {
                await srv.StopAsync();
            }
        }
    }
}
