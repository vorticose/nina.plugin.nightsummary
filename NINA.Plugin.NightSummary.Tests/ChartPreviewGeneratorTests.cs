using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace NINA.Plugin.NightSummary.Tests {
    /// <summary>
    /// Generates a sample HTML report with multi-filter LRGB data and writes it
    /// to <c>.preview/sample-report.html</c> in the worktree root. Run on demand
    /// via <c>dotnet test --filter FullyQualifiedName~ChartPreviewGeneratorTests</c>
    /// during feature development to visually verify the JS chart renderer.
    ///
    /// The emitted file is NOT committed and is safe to regenerate at any time.
    /// </summary>
    public class ChartPreviewGeneratorTests {
        private readonly ITestOutputHelper _output;

        public ChartPreviewGeneratorTests(ITestOutputHelper output) {
            _output = output;
        }

        [Fact]
        public async Task GenerateLrgbPreview_WritesSampleHtmlReport() {
            SettingsManager.Instance.Current.ReportLightMode  = false;
            SettingsManager.Instance.Current.ShowHFRGraph     = true;
            SettingsManager.Instance.Current.ReportDetailLevel = 2;
            SettingsManager.Instance.Current.ChartPrimaryMetric   = ChartGenerator.PrimaryHFR;
            SettingsManager.Instance.Current.ChartSecondaryMetric = ChartGenerator.SecFWHM;
            SettingsManager.Instance.Current.ChartXAxisMetric     = ChartGenerator.XAxisTime;
            SettingsManager.Instance.Current.ShowChartAfMarkers   = true;
            SettingsManager.Instance.Current.ShowChartFlipMarkers = true;
            SettingsManager.Instance.Current.ShowChartRoofMarkers = true;
            SettingsManager.Instance.Current.AdditionalChartConfigs = "";

            var data = BuildLrgbReportData();

            var generator = new ReportGenerator();
            var html      = await generator.GenerateHtmlReport(data);

            // Write to .preview at the worktree root. The test binary lives under
            // NINA.Plugin.NightSummary.Tests/bin/Debug/..., so walk up to the root.
            var rootDir = FindWorktreeRoot(AppContext.BaseDirectory);
            var outDir  = Path.Combine(rootDir, ".preview");
            Directory.CreateDirectory(outDir);
            var outPath = Path.Combine(outDir, "sample-report.html");
            File.WriteAllText(outPath, html);

            _output.WriteLine($"Preview report written to: {outPath}");
            _output.WriteLine($"HTML length: {html.Length} chars");

            Assert.True(File.Exists(outPath));
            Assert.Contains("ns-chart-filter-bar", html); // CSS chip selector bar present
            Assert.Contains("ns-chart-filter-btn", html); // chip labels present
            // JS renderer is dead code — data-chart attribute and NSMetricChart no longer emitted
        }

        [Fact]
        public async Task GenerateImbalancedFilterPreview_WritesSampleHtmlReport() {
            SettingsManager.Instance.Current.ReportLightMode  = false;
            SettingsManager.Instance.Current.ShowHFRGraph     = true;
            SettingsManager.Instance.Current.ReportDetailLevel = 2;
            SettingsManager.Instance.Current.ChartPrimaryMetric   = ChartGenerator.PrimaryHFR;
            SettingsManager.Instance.Current.ChartSecondaryMetric = ChartGenerator.SecFWHM;
            SettingsManager.Instance.Current.ChartXAxisMetric     = ChartGenerator.XAxisTime;
            SettingsManager.Instance.Current.AdditionalChartConfigs = "";

            var data = BuildImbalancedReportData();

            var generator = new ReportGenerator();
            var html      = await generator.GenerateHtmlReport(data);

            var rootDir = FindWorktreeRoot(AppContext.BaseDirectory);
            var outDir  = Path.Combine(rootDir, ".preview");
            Directory.CreateDirectory(outDir);
            var outPath = Path.Combine(outDir, "sample-report-imbalanced.html");
            File.WriteAllText(outPath, html);

            _output.WriteLine($"Imbalanced preview written to: {outPath}");

            Assert.True(File.Exists(outPath));
        }

        /// <summary>
        /// Pathological distribution to stress-test the filter selector:
        ///   L  — 30 frames across the whole 3-hour session (well-sampled)
        ///   Ha —  5 frames concentrated in a 12-minute burst at hour 2
        ///   Sii —  2 frames (minimum renderable — tests the >=2 threshold)
        ///   Oiii — 1 frame (should trigger placeholder / no-data badge)
        /// </summary>
        private static ReportData BuildImbalancedReportData() {
            var sessionId = Guid.NewGuid().ToString();
            var session   = TestDataFactory.MakeSession(sessionId);
            var rand      = new Random(7);
            var images    = new List<ImageRecord>();
            var start     = new DateTime(2025, 1, 15, 22, 0, 0);

            // L: 30 frames spread over 180 minutes
            for (int i = 0; i < 30; i++) {
                var img = TestDataFactory.MakeImage(sessionId,
                    target: "NGC 7000", filter: "L",
                    hfr:  1.8 + (rand.NextDouble() - 0.5) * 0.25,
                    fwhm: 2.7 + (rand.NextDouble() - 0.5) * 0.20,
                    raHours: 0.0, decDeg: 0.0);
                img.Timestamp = start.AddMinutes(i * 6);
                img.GuidingRMSTotal = 0.6 + (rand.NextDouble() - 0.5) * 0.2;
                images.Add(img);
            }

            // Ha: 5 frames within 12 minutes, 2 hours in
            var haStart = start.AddHours(2);
            for (int i = 0; i < 5; i++) {
                var img = TestDataFactory.MakeImage(sessionId,
                    target: "NGC 7000", filter: "Ha",
                    hfr:  2.4 + (rand.NextDouble() - 0.5) * 0.15,
                    fwhm: 3.1 + (rand.NextDouble() - 0.5) * 0.15,
                    raHours: 0.0, decDeg: 0.0);
                img.Timestamp = haStart.AddMinutes(i * 3);
                img.GuidingRMSTotal = 0.6;
                images.Add(img);
            }

            // Sii: exactly 2 frames (edge of the >= 2 threshold)
            for (int i = 0; i < 2; i++) {
                var img = TestDataFactory.MakeImage(sessionId,
                    target: "NGC 7000", filter: "Sii",
                    hfr:  2.6, fwhm: 3.3,
                    raHours: 0.0, decDeg: 0.0);
                img.Timestamp = start.AddMinutes(150 + i * 4);
                img.GuidingRMSTotal = 0.65;
                images.Add(img);
            }

            // Oiii: a single frame (below the render threshold — should show placeholder when filtered)
            {
                var img = TestDataFactory.MakeImage(sessionId,
                    target: "NGC 7000", filter: "Oiii",
                    hfr: 2.5, fwhm: 3.2,
                    raHours: 0.0, decDeg: 0.0);
                img.Timestamp = start.AddMinutes(170);
                img.GuidingRMSTotal = 0.6;
                images.Add(img);
            }

            var events = new List<SessionEvent> {
                new SessionEvent { SessionId = sessionId, Timestamp = start.AddMinutes(30), EventType = "AutoFocus", Description = "AF @ L", AfSucceeded = true, AfHfr = 1.75 }
            };

            return new ReportData {
                Session                      = session,
                Images                       = images,
                Events                       = events,
                TsData                       = new List<TsTargetData>(),
                CumulativeIntegrationSeconds = new Dictionary<string, double>(),
                SessionHistory               = new Dictionary<string, List<TargetSessionHistory>>(),
                CameraFovWidthDeg            = 2.5,
                CameraFovHeightDeg           = 1.8,
                ObserverLatitude             = 40.7128,
                ObserverLongitude            = -74.0060,
                ActiveProfileId              = "test-profile-id",
                SkippedExposures             = 0
            };
        }

        [Fact]
        public async Task GenerateLrgbPreviewLightMode_WritesSampleHtmlReport() {
            SettingsManager.Instance.Current.ReportLightMode  = true;
            SettingsManager.Instance.Current.ShowHFRGraph     = true;
            SettingsManager.Instance.Current.ReportDetailLevel = 2;
            SettingsManager.Instance.Current.ChartPrimaryMetric   = ChartGenerator.PrimaryHFR;
            SettingsManager.Instance.Current.ChartSecondaryMetric = ChartGenerator.SecFWHM;
            SettingsManager.Instance.Current.ChartXAxisMetric     = ChartGenerator.XAxisTime;

            var data = BuildLrgbReportData();

            var generator = new ReportGenerator();
            var html      = await generator.GenerateHtmlReport(data);

            SettingsManager.Instance.Current.ReportLightMode = false; // reset

            var rootDir = FindWorktreeRoot(AppContext.BaseDirectory);
            var outDir  = Path.Combine(rootDir, ".preview");
            Directory.CreateDirectory(outDir);
            var outPath = Path.Combine(outDir, "sample-report-light.html");
            File.WriteAllText(outPath, html);

            _output.WriteLine($"Light mode preview written to: {outPath}");

            Assert.True(File.Exists(outPath));
        }

        /// <summary>
        /// Builds a realistic LRGB rotating series: 40 frames, 10 per filter,
        /// cycling L-R-G-B with a drift in HFR/FWHM to make trends visible. Each
        /// filter has slightly different mean HFR so the filter-by-filter trend
        /// lines are meaningfully different (exactly the scenario the user
        /// described when asking for the feature).
        /// </summary>
        private static ReportData BuildLrgbReportData() {
            var sessionId = Guid.NewGuid().ToString();
            var session   = TestDataFactory.MakeSession(sessionId);
            var filters   = new[] { "L", "R", "G", "B" };
            var filterMeanHfr = new Dictionary<string, double> {
                ["L"] = 1.8, ["R"] = 2.0, ["G"] = 2.1, ["B"] = 2.3
            };
            var filterMeanFwhm = new Dictionary<string, double> {
                ["L"] = 2.7, ["R"] = 3.0, ["G"] = 3.1, ["B"] = 3.3
            };
            var rand = new Random(42);

            var images = new List<ImageRecord>();
            var start  = new DateTime(2025, 1, 15, 22, 0, 0);
            for (int i = 0; i < 40; i++) {
                var filter = filters[i % 4];
                var cycle  = i / 4;
                // Gradual upward drift in HFR as seeing degrades + per-filter noise
                var hfr  = filterMeanHfr[filter]  + cycle * 0.03 + (rand.NextDouble() - 0.5) * 0.15;
                var fwhm = filterMeanFwhm[filter] + cycle * 0.02 + (rand.NextDouble() - 0.5) * 0.10;

                var img = TestDataFactory.MakeImage(sessionId,
                    target: "NGC 7000",
                    filter: filter,
                    hfr:    hfr,
                    fwhm:   fwhm,
                    raHours: 0.0,
                    decDeg:  0.0);
                img.Timestamp = start.AddMinutes(i * 3);
                img.GuidingRMSTotal = 0.6 + (rand.NextDouble() - 0.5) * 0.2;
                images.Add(img);
            }

            // A few events to exercise the event marker rendering
            var events = new List<SessionEvent> {
                new SessionEvent { SessionId = sessionId, Timestamp = start.AddMinutes(12), EventType = "AutoFocus",    Description = "AF @ L",            AfSucceeded = true, AfHfr = 1.75 },
                new SessionEvent { SessionId = sessionId, Timestamp = start.AddMinutes(60), EventType = "MeridianFlip", Description = "Meridian flip" },
                new SessionEvent { SessionId = sessionId, Timestamp = start.AddMinutes(90), EventType = "AutoFocus",    Description = "AF @ R",            AfSucceeded = true, AfHfr = 1.80 }
            };

            return new ReportData {
                Session                      = session,
                Images                       = images,
                Events                       = events,
                TsData                       = new List<TsTargetData>(),
                CumulativeIntegrationSeconds = new Dictionary<string, double>(),
                SessionHistory               = new Dictionary<string, List<TargetSessionHistory>>(),
                CameraFovWidthDeg            = 2.5,
                CameraFovHeightDeg           = 1.8,
                ObserverLatitude             = 40.7128,
                ObserverLongitude            = -74.0060,
                ActiveProfileId              = "test-profile-id",
                SkippedExposures             = 0
            };
        }

        // ── Per-target chip bar presence/absence ─────────────────────────────

        [Fact]
        public async Task MultiTargetSession_ShowsTargetChipBar() {
            SetupChartSettings();
            var data = TestDataFactory.MakeReportData(imageCount: 10, targets: new[] { "M42", "Orion Nebula" });
            var html = await new ReportGenerator().GenerateHtmlReport(data);
            Assert.Contains("ns-chart-target-btn", html);
            Assert.Contains("All Targets", html);
            Assert.Contains("M42", html);
            Assert.Contains("Orion Nebula", html);
        }

        [Fact]
        public async Task SingleTargetSession_NoTargetChipBar() {
            SetupChartSettings();
            var data = TestDataFactory.MakeReportData(imageCount: 10, targets: new[] { "M42" });
            var html = await new ReportGenerator().GenerateHtmlReport(data);
            Assert.DoesNotContain("ns-chart-target-btn", html);
            Assert.DoesNotContain("All Targets", html);
        }

        [Fact]
        public async Task MultiTargetMultiFilter_ShowsBothChipBars() {
            SetupChartSettings();
            // Build images across two targets and two filters manually
            var sessionId = Guid.NewGuid().ToString();
            var t0 = new DateTime(2025, 1, 15, 22, 0, 0);
            var images = new List<ImageRecord>();
            foreach (var (tgt, flt, offset) in new[] {
                ("M42", "Ha",   0), ("M42",   "Ha",  5), ("M42",   "OIII", 10),
                ("Orion", "Ha", 15), ("Orion", "OIII", 20) }) {
                var img = TestDataFactory.MakeImage(sessionId, target: tgt, filter: flt);
                img.Timestamp = t0.AddMinutes(offset);
                images.Add(img);
            }
            var data = new ReportData {
                Session = TestDataFactory.MakeSession(sessionId),
                Images = images,
                Events = new List<SessionEvent>(),
                TsData = new List<TsTargetData>(),
                CumulativeIntegrationSeconds = new Dictionary<string, double>(),
                SessionHistory = new Dictionary<string, List<TargetSessionHistory>>(),
            };
            var html = await new ReportGenerator().GenerateHtmlReport(data);
            Assert.Contains("ns-chart-target-btn", html);  // target bar
            Assert.Contains("ns-chart-filter-bar",  html);  // filter bar
            Assert.Contains("All Targets", html);
            Assert.Contains("All", html);
        }

        private static void SetupChartSettings() {
            SettingsManager.Instance.Current.ShowHFRGraph         = true;
            SettingsManager.Instance.Current.ReportDetailLevel    = 2;
            SettingsManager.Instance.Current.ChartPrimaryMetric   = ChartGenerator.PrimaryHFR;
            SettingsManager.Instance.Current.ChartSecondaryMetric = ChartGenerator.SecNone;
            SettingsManager.Instance.Current.ChartXAxisMetric     = ChartGenerator.XAxisTime;
            SettingsManager.Instance.Current.AdditionalChartConfigs = "";
        }

        /// <summary>
        /// Walks up from the test bin directory until it finds the worktree root
        /// (identified by the presence of <c>NINA.Plugin.NightSummary.sln</c>).
        /// </summary>
        private static string FindWorktreeRoot(string start) {
            var dir = new DirectoryInfo(start);
            while (dir != null) {
                if (File.Exists(Path.Combine(dir.FullName, "NINA.Plugin.NightSummary.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return start;
        }
    }
}
