using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.MyPluginProperties;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Tests for BuildTargetSection branches: coordinate subtitle, filter table layout,
    /// side-by-side altitude chart, empty filter names, and the overview filter breakdown.
    /// </summary>
    public class ReportGeneratorTargetTests {

        private readonly ReportGenerator _gen;

        public ReportGeneratorTargetTests() {
            _gen = new ReportGenerator();
            Settings.Default.ReportLightMode        = false;
            Settings.Default.ReportDetailLevel      = 2;
            Settings.Default.ShowHFRGraph           = false;
            Settings.Default.ShowStarCountCV        = false;
            Settings.Default.ShowPerTargetIQ        = false;
            Settings.Default.ShowSessionHistory     = false;
            Settings.Default.ShowSkyThumbnails      = false;
            Settings.Default.ShowAltitudeChart      = false;
            Settings.Default.ShowNextNightPreview   = false;
            Settings.Default.ShowTSProgressBars     = false;
            Settings.Default.AdditionalChartConfigs = "";
            Settings.Default.ExpandSectionsDefault  = false;
        }

        // ── Coordinate subtitle ───────────────────────────────────────────────

        [Fact]
        public async Task TargetSubtitle_WithCoords_ContainsFormattedRA() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            foreach (var img in data.Images) {
                img.RaHours    = 5.5833;   // Orion Nebula
                img.DecDegrees = -5.3911;
            }
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("R.A.", html);
        }

        [Fact]
        public async Task TargetSubtitle_WithCoords_ContainsDec() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            foreach (var img in data.Images) {
                img.RaHours    = 5.5833;
                img.DecDegrees = -5.3911;
            }
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("Dec.", html);
        }

        [Fact]
        public async Task TargetSubtitle_WithCoords_ContainsMoonSeparation() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            foreach (var img in data.Images) {
                img.RaHours    = 5.5833;
                img.DecDegrees = -5.3911;
            }
            var html = await _gen.GenerateHtmlReport(data);
            // Moon separation emoji + degrees sign always appears when coords present
            Assert.Contains("&#127769;", html);
        }

        [Fact]
        public async Task TargetSubtitle_WithoutCoords_NoRAOrDec() {
            // Default MakeReportData uses RA=0, Dec=0 — no coordinate subtitle
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            var html = await _gen.GenerateHtmlReport(data);
            Assert.DoesNotContain("R.A.", html);
            Assert.DoesNotContain("Dec.", html);
        }

        // ── Altitude chart in target section ──────────────────────────────────

        [Fact]
        public async Task AltitudeChartInTarget_WithCoords_ShowsChartWrapper() {
            Settings.Default.ShowAltitudeChart = true;
            Settings.Default.ReportDetailLevel = 1;

            var data     = TestDataFactory.MakeReportData(imageCount: 5, observerLat: 40.7128, observerLon: -74.0060);
            var baseTime = new DateTime(2025, 1, 15, 22, 0, 0);
            for (int i = 0; i < data.Images.Count; i++) {
                data.Images[i].RaHours    = 5.5833;
                data.Images[i].DecDegrees = -5.3911;
                data.Images[i].Timestamp  = baseTime.AddMinutes(i * 30);
            }
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("class='ts-target-header'", html);
        }

        [Fact]
        public async Task AltitudeChartInTarget_ZeroObserverLocation_NoChartContent() {
            Settings.Default.ShowAltitudeChart = true;
            Settings.Default.ReportDetailLevel = 1;

            // observerLat=0, observerLon=0 → BuildAltitudeChart returns empty string →
            // the ts-target-header wrapper is still rendered (showSideBySideChart=true),
            // but the altitude-chart SVG itself is absent.
            var data     = TestDataFactory.MakeReportData(imageCount: 5, observerLat: 0, observerLon: 0);
            var baseTime = new DateTime(2025, 1, 15, 22, 0, 0);
            for (int i = 0; i < data.Images.Count; i++) {
                data.Images[i].RaHours    = 5.5833;
                data.Images[i].DecDegrees = -5.3911;
                data.Images[i].Timestamp  = baseTime.AddMinutes(i * 30);
            }
            var html = await _gen.GenerateHtmlReport(data);
            Assert.DoesNotContain("altitude-chart", html);
        }

        [Fact]
        public async Task AltitudeChartInTarget_Disabled_NoWrapper() {
            Settings.Default.ShowAltitudeChart = false;

            var data     = TestDataFactory.MakeReportData(imageCount: 5, observerLat: 40.7128, observerLon: -74.0060);
            var baseTime = new DateTime(2025, 1, 15, 22, 0, 0);
            for (int i = 0; i < data.Images.Count; i++) {
                data.Images[i].RaHours    = 5.5833;
                data.Images[i].DecDegrees = -5.3911;
                data.Images[i].Timestamp  = baseTime.AddMinutes(i * 30);
            }
            var html = await _gen.GenerateHtmlReport(data);
            Assert.DoesNotContain("class='ts-target-header'", html);
        }

        // ── Filter table layout ───────────────────────────────────────────────

        [Fact]
        public async Task FilterTable_SingleFilter_ShowsFilterName() {
            var data = TestDataFactory.MakeReportData(imageCount: 6, targets: new[] { "M31" });
            foreach (var img in data.Images) img.Filter = "Ha";
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("Ha", html);
        }

        [Fact]
        public async Task FilterTable_MultipleFilters_EachFilterPresent() {
            var sessionId = System.Guid.NewGuid().ToString();
            var session   = TestDataFactory.MakeSession(sessionId);
            var images    = new List<ImageRecord>();
            foreach (var f in new[] { "Ha", "OIII", "SII" })
                for (int i = 0; i < 4; i++)
                    images.Add(TestDataFactory.MakeImage(sessionId, filter: f));
            var data = new ReportData {
                Session = session, Images = images, Events = new List<SessionEvent>(),
                TsData  = new List<TsTargetData>(),
                CumulativeIntegrationSeconds = new Dictionary<string, double>(),
                SessionHistory = new Dictionary<string, List<TargetSessionHistory>>()
            };
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("Ha",   html);
            Assert.Contains("OIII", html);
            Assert.Contains("SII",  html);
        }

        [Fact]
        public async Task FilterTable_SameFilterDifferentExposures_ShowsMultipleRows() {
            var sessionId = System.Guid.NewGuid().ToString();
            var session   = TestDataFactory.MakeSession(sessionId);
            var images    = new List<ImageRecord>();
            // 4 × Ha at 300s
            for (int i = 0; i < 4; i++) {
                var img = TestDataFactory.MakeImage(sessionId, filter: "Ha");
                img.ExposureDuration = 300;
                images.Add(img);
            }
            // 4 × Ha at 600s (different exposure → second row in table)
            for (int i = 0; i < 4; i++) {
                var img = TestDataFactory.MakeImage(sessionId, filter: "Ha");
                img.ExposureDuration = 600;
                images.Add(img);
            }
            var data = new ReportData {
                Session = session, Images = images, Events = new List<SessionEvent>(),
                TsData  = new List<TsTargetData>(),
                CumulativeIntegrationSeconds = new Dictionary<string, double>(),
                SessionHistory = new Dictionary<string, List<TargetSessionHistory>>()
            };
            var html = await _gen.GenerateHtmlReport(data);
            // Both exposure durations should appear in the table
            Assert.Contains("300s", html);
            Assert.Contains("600s", html);
        }

        [Fact]
        public async Task FilterTable_EmptyFilterName_ReportRendersWithoutError() {
            var sessionId = System.Guid.NewGuid().ToString();
            var session   = TestDataFactory.MakeSession(sessionId);
            var images    = new List<ImageRecord>();
            for (int i = 0; i < 4; i++) {
                var img = TestDataFactory.MakeImage(sessionId, filter: "");
                images.Add(img);
            }
            var data = new ReportData {
                Session = session, Images = images, Events = new List<SessionEvent>(),
                TsData  = new List<TsTargetData>(),
                CumulativeIntegrationSeconds = new Dictionary<string, double>(),
                SessionHistory = new Dictionary<string, List<TargetSessionHistory>>()
            };
            var html = await _gen.GenerateHtmlReport(data);
            // Empty filter → overview breakdown maps it to "—"; report should render cleanly
            Assert.Contains("stat-breakdown-row", html);
            Assert.Contains("—", html);
        }

        // ── Filter table total row ────────────────────────────────────────────

        [Fact]
        public async Task FilterTable_ContainsTotalRow() {
            var data = TestDataFactory.MakeReportData(imageCount: 6, targets: new[] { "M31" });
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("<strong>Total</strong>", html);
        }

        // ── Session overview filter breakdown ────────────────────────────────

        [Fact]
        public async Task OverviewBreakdown_EmptyFilterName_ShowsDashInBreakdown() {
            var sessionId = System.Guid.NewGuid().ToString();
            var session   = TestDataFactory.MakeSession(sessionId);
            var images    = new List<ImageRecord>();
            for (int i = 0; i < 5; i++)
                images.Add(TestDataFactory.MakeImage(sessionId, filter: ""));
            var data = new ReportData {
                Session = session, Images = images, Events = new List<SessionEvent>(),
                TsData  = new List<TsTargetData>(),
                CumulativeIntegrationSeconds = new Dictionary<string, double>(),
                SessionHistory = new Dictionary<string, List<TargetSessionHistory>>()
            };
            var html = await _gen.GenerateHtmlReport(data);
            // Overview filter breakdown maps empty → "—"
            Assert.Contains("stat-breakdown-row", html);
        }

        // ── Target section structure ──────────────────────────────────────────

        [Fact]
        public async Task TargetSection_ContainsTargetSectionClass() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("target-section", html);
        }

        [Fact]
        public async Task TargetSection_H3ContainsTargetName() {
            var data = TestDataFactory.MakeReportData(imageCount: 5, targets: new[] { "NGC 7000" });
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("NGC 7000", html);
        }

        [Fact]
        public async Task TargetSection_MultipleTargets_EachHasOwnSection() {
            var targets = new[] { "M31", "M42" };
            var data    = TestDataFactory.MakeReportData(imageCount: 12, targets: targets);
            var html    = await _gen.GenerateHtmlReport(data);
            // Each target should appear in an h3
            Assert.Contains("<h3>M31", html);
            Assert.Contains("<h3>M42", html);
        }

        // ── Star count CV — broadband vs narrowband split ────────────────────

        [Fact]
        public async Task StarCountCV_BroadbandAndNarrowband_BothCVsShown() {
            Settings.Default.ShowStarCountCV   = true;
            Settings.Default.ReportDetailLevel = 1;

            var sessionId = System.Guid.NewGuid().ToString();
            var session   = TestDataFactory.MakeSession(sessionId);
            var images    = new List<ImageRecord>();
            // 5 broadband (Lum)
            for (int i = 0; i < 5; i++) {
                var img = TestDataFactory.MakeImage(sessionId, filter: "Lum");
                img.StarCount = 300 + i * 10;
                images.Add(img);
            }
            // 5 narrowband (Ha)
            for (int i = 0; i < 5; i++) {
                var img = TestDataFactory.MakeImage(sessionId, filter: "Ha");
                img.StarCount = 80 + i * 5;
                images.Add(img);
            }
            var data = new ReportData {
                Session = session, Images = images, Events = new List<SessionEvent>(),
                TsData  = new List<TsTargetData>(),
                CumulativeIntegrationSeconds = new Dictionary<string, double>(),
                SessionHistory = new Dictionary<string, List<TargetSessionHistory>>()
            };
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("star-count-table", html);
            Assert.Contains("Broadband CV", html);
            Assert.Contains("Narrowband CV", html);
        }

        [Fact]
        public async Task StarCountCV_BroadbandOnlyOneImage_ShowsDashForBroadbandCV() {
            Settings.Default.ShowStarCountCV   = true;
            Settings.Default.ReportDetailLevel = 1;

            var sessionId = System.Guid.NewGuid().ToString();
            var session   = TestDataFactory.MakeSession(sessionId);
            var images    = new List<ImageRecord>();
            // Only 1 broadband image — CV requires >= 2
            var img1 = TestDataFactory.MakeImage(sessionId, filter: "Lum");
            img1.StarCount = 300;
            images.Add(img1);
            // 3 narrowband (Ha)
            for (int i = 0; i < 3; i++) {
                var img = TestDataFactory.MakeImage(sessionId, filter: "Ha");
                img.StarCount = 80 + i * 5;
                images.Add(img);
            }
            var data = new ReportData {
                Session = session, Images = images, Events = new List<SessionEvent>(),
                TsData  = new List<TsTargetData>(),
                CumulativeIntegrationSeconds = new Dictionary<string, double>(),
                SessionHistory = new Dictionary<string, List<TargetSessionHistory>>()
            };
            var html = await _gen.GenerateHtmlReport(data);
            // Broadband CV should be "—" (only 1 image), narrowband CV should be a percentage
            Assert.Contains("star-count-table", html);
        }

        // ── Session history date formatting in table ──────────────────────────

        [Fact]
        public async Task SessionHistory_ZeroAvgHFR_ShowsDashForHFR() {
            Settings.Default.ShowSessionHistory = true;
            Settings.Default.ReportDetailLevel  = 2;

            var history = new Dictionary<string, List<TargetSessionHistory>> {
                ["M31"] = new List<TargetSessionHistory> {
                    new TargetSessionHistory {
                        SessionStart       = new DateTime(2025, 2, 10, 21, 0, 0),
                        IntegrationSeconds = 3600,
                        AvgHFR             = 0,   // no HFR data → should show "—"
                        AvgFWHM            = 0,
                        AvgGuidingRMS      = 0
                    }
                }
            };
            var data = TestDataFactory.MakeReportData(
                imageCount: 6, targets: new[] { "M31" }, sessionHistory: history);
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("Session History", html);
            // All three zero-value metrics should render as "—"
            Assert.Contains("Feb 10, 2025", html);
        }

        [Fact]
        public async Task SessionHistory_NonZeroMetrics_ShowsFormattedValues() {
            Settings.Default.ShowSessionHistory = true;
            Settings.Default.ReportDetailLevel  = 2;

            var history = new Dictionary<string, List<TargetSessionHistory>> {
                ["M31"] = new List<TargetSessionHistory> {
                    new TargetSessionHistory {
                        SessionStart       = new DateTime(2025, 1, 20, 21, 0, 0),
                        IntegrationSeconds = 7200,
                        AvgHFR             = 2.8,
                        AvgFWHM            = 3.5,
                        AvgGuidingRMS      = 0.55
                    }
                }
            };
            var data = TestDataFactory.MakeReportData(
                imageCount: 6, targets: new[] { "M31" }, sessionHistory: history);
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("2.80px", html);
            Assert.Contains("3.50", html);
            Assert.Contains("0.55", html);
        }
    }
}
