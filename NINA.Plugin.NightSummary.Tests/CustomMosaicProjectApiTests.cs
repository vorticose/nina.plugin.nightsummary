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
    /// Custom mosaic projects must work when Target Scheduler is not installed.
    /// </summary>
    public class CustomMosaicProjectApiTests {

        private sealed class StubDataSource : IDashboardDataSource {
            public IReadOnlyList<TargetDetail> Targets { get; set; } = Array.Empty<TargetDetail>();
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
                => Task.FromResult(Targets);
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
            private readonly string _tmp;
            public StubPaths() {
                _tmp = Path.Combine(Path.GetTempPath(), "ns-mosaic-test-" + Guid.NewGuid().ToString("N"));
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

        private static int GetFreePort() {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private static async Task<(DashboardServer srv, int port)> StartServerAsync(params string[] targetNames) {
            int port = GetFreePort();
            var data = new StubDataSource();
            var paths = new StubPaths();
            if (targetNames.Length > 0) {
                data.Targets = targetNames.Select(n => new TargetDetail { TargetName = n }).ToList();
                // /api/stats/targets returns nothing unless the NS database file exists.
                File.WriteAllBytes(paths.DatabasePath, Array.Empty<byte>());
            }
            var srv = new DashboardServer(
                data:        data,
                settings:    new StubSettings(),
                webAssets:   new StubWebAssets(),
                externalLog: new StubLogger(),
                paths:       paths,
                regen:       null,
                readOnly:    false);
            await srv.StartAsync(port, "localhost");
            return (srv, port);
        }

        private static HttpClient NewClient(int port) =>
            new HttpClient { BaseAddress = new Uri($"http://localhost:{port}/"), Timeout = TimeSpan.FromSeconds(5) };

        [Fact]
        public async Task Create_custom_mosaic_without_TS_returns_guid_and_suggested_name() {
            var (srv, port) = await StartServerAsync();
            try {
                using var client = NewClient(port);
                var body = JsonSerializer.Serialize(new {
                    action = "create",
                    isMosaic = true,
                    targets = new[] { "North America Panel 1", "North America Panel 2" }
                });
                var resp = await client.PostAsync("/api/stats/projects/custom",
                    new StringContent(body, Encoding.UTF8, "application/json"));
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                Assert.StartsWith("custom-", doc.RootElement.GetProperty("guid").GetString());
                Assert.Equal("North America", doc.RootElement.GetProperty("name").GetString());
                Assert.True(doc.RootElement.GetProperty("isMosaic").GetBoolean());
            } finally { await srv.StopAsync(); }
        }

        [Fact]
        public async Task Project_stats_for_custom_mosaic_does_not_require_TS() {
            var (srv, port) = await StartServerAsync();
            try {
                using var client = NewClient(port);
                var body = JsonSerializer.Serialize(new {
                    action = "create",
                    name = "Heart",
                    isMosaic = true,
                    targets = new[] { "Heart_1", "Heart_2" }
                });
                var create = await client.PostAsync("/api/stats/projects/custom",
                    new StringContent(body, Encoding.UTF8, "application/json"));
                create.EnsureSuccessStatusCode();
                var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
                var guid = created.RootElement.GetProperty("guid").GetString();

                var resp = await client.GetAsync("/api/stats/projects/" + guid);
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                Assert.True(doc.RootElement.GetProperty("project").GetProperty("isMosaic").GetBoolean());
                Assert.True(doc.RootElement.GetProperty("project").GetProperty("isCustom").GetBoolean());
            } finally { await srv.StopAsync(); }
        }

        // Issue #31: changing a custom project's status saved the override, but the
        // target card's project came back hardcoded to Active, so the badge never changed.
        [Fact]
        public async Task Status_override_applies_to_custom_project_on_target_stats() {
            var (srv, port) = await StartServerAsync("Heart_1");
            try {
                using var client = NewClient(port);
                var create = await client.PostAsync("/api/stats/projects/custom",
                    new StringContent(JsonSerializer.Serialize(new {
                        action = "create", name = "Heart", isMosaic = false, targets = new[] { "Heart_1" }
                    }), Encoding.UTF8, "application/json"));
                create.EnsureSuccessStatusCode();
                var guid = JsonDocument.Parse(await create.Content.ReadAsStringAsync())
                    .RootElement.GetProperty("guid").GetString();

                async Task<(string? state, string? source)> GetProjectState() {
                    var json = await client.GetStringAsync("/api/stats/targets");
                    using var doc = JsonDocument.Parse(json);
                    var proj = doc.RootElement.GetProperty("targets")[0].GetProperty("ts").GetProperty("project");
                    Assert.Equal(guid, proj.GetProperty("guid").GetString());
                    return (proj.GetProperty("state").GetString(), proj.GetProperty("stateSource").GetString());
                }

                async Task SetOverride(string status) {
                    var resp = await client.PostAsync("/api/stats/ts/override",
                        new StringContent(JsonSerializer.Serialize(new { projectGuid = guid, status }),
                            Encoding.UTF8, "application/json"));
                    resp.EnsureSuccessStatusCode();
                }

                Assert.Equal(("Active", "raw"), await GetProjectState());

                await SetOverride("Draft");
                Assert.Equal(("Draft", "override"), await GetProjectState());

                await SetOverride("Completed");
                Assert.Equal(("Completed", "override"), await GetProjectState());

                // Empty status clears the override ("Reset" in the dropdown).
                await SetOverride("");
                Assert.Equal(("Active", "raw"), await GetProjectState());
            } finally { await srv.StopAsync(); }
        }
    }
}
