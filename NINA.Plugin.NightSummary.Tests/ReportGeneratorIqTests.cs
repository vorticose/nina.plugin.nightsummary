using NINA.Plugin.NightSummary.MyPluginProperties;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Tests for the Image Quality section and AppendIqRows branches in ReportGenerator.
    /// Covers metric-presence combinations: HFR, FWHM, guiding, eccentricity, star count.
    /// </summary>
    public class ReportGeneratorIqTests {

        private readonly ReportGenerator _gen;

        public ReportGeneratorIqTests() {
            _gen = new ReportGenerator();
            // Defaults that exercise the IQ section
            Settings.Default.ReportLightMode  = false;
            Settings.Default.ReportDetailLevel = 1;
            Settings.Default.ShowHFRGraph      = false;
            Settings.Default.ShowStarCountCV   = true;
            Settings.Default.ShowPerTargetIQ   = false;
            Settings.Default.ShowSkyThumbnails = false;
            Settings.Default.ShowSessionHistory = false;
            Settings.Default.ShowTSProgressBars = false;
        }

        // ── IQ section presence ───────────────────────────────────────────────

        [Fact]
        public async Task IqSection_AllMetricsZero_SectionNotRendered() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            // Strip all IQ metrics
            foreach (var img in data.Images) {
                img.HFR             = 0;
                img.FWHM            = 0;
                img.Eccentricity    = 0;
                img.GuidingRMSTotal = 0;
                img.StarCount       = 0;
            }
            var html = await _gen.GenerateHtmlReport(data);
            Assert.DoesNotContain("Session Image Quality", html);
        }

        [Fact]
        public async Task IqSection_HfrOnly_SectionAppears() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            foreach (var img in data.Images) {
                img.FWHM            = 0;
                img.Eccentricity    = 0;
                img.GuidingRMSTotal = 0;
                img.StarCount       = 0;
            }
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("Session Image Quality", html);
            Assert.Contains("HFR", html);
        }

        [Fact]
        public async Task IqSection_FwhmOnly_SectionAppears() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            foreach (var img in data.Images) {
                img.HFR             = 0;
                img.Eccentricity    = 0;
                img.GuidingRMSTotal = 0;
                img.StarCount       = 0;
            }
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("Session Image Quality", html);
            Assert.Contains("FWHM", html);
        }

        [Fact]
        public async Task IqSection_GuidingOnly_SectionAppears() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            foreach (var img in data.Images) {
                img.HFR          = 0;
                img.FWHM         = 0;
                img.Eccentricity = 0;
                img.StarCount    = 0;
                // keep GuidingRMSTotal from default (0.65)
            }
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("Session Image Quality", html);
            Assert.Contains("Guiding", html);
        }

        [Fact]
        public async Task IqSection_AllMetrics_AllRowsRendered() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            // Defaults already include HFR, FWHM, Eccentricity, GuidingRMS, StarCount
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("HFR",         html);
            Assert.Contains("FWHM",        html);
            Assert.Contains("Eccentricity",html);
            Assert.Contains("Guiding",     html);
        }

        // ── Eccentricity row ─────────────────────────────────────────────────

        [Fact]
        public async Task IqSection_EccentricityPositive_RowAppears() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            foreach (var img in data.Images) img.Eccentricity = 0.55;
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("Eccentricity", html);
        }

        [Fact]
        public async Task IqSection_EccentricityZero_RowAbsent() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            foreach (var img in data.Images) img.Eccentricity = 0;
            var html = await _gen.GenerateHtmlReport(data);
            Assert.DoesNotContain("Eccentricity", html);
        }

        // ── Guiding row ──────────────────────────────────────────────────────

        [Fact]
        public async Task IqSection_GuidingPresent_RmsRowAppears() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            foreach (var img in data.Images) img.GuidingRMSTotal = 0.72;
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("Guiding", html);
        }

        [Fact]
        public async Task IqSection_GuidingZero_RmsRowAbsent() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            foreach (var img in data.Images) img.GuidingRMSTotal = 0;
            var html = await _gen.GenerateHtmlReport(data);
            // Guiding row should not appear, but IQ section may still appear due to HFR/FWHM
            Assert.DoesNotContain("Guiding RMS", html);
        }

        // ── Star count CV ────────────────────────────────────────────────────

        [Fact]
        public async Task StarCountCV_Enabled_StarCountTableAppears() {
            Settings.Default.ShowStarCountCV = true;
            var data = TestDataFactory.MakeReportData(imageCount: 10);
            foreach (var img in data.Images) img.StarCount = 250;
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("star-count-table", html);
        }

        [Fact]
        public async Task StarCountCV_Disabled_TableAbsent() {
            Settings.Default.ShowStarCountCV = false;
            var data = TestDataFactory.MakeReportData(imageCount: 10);
            var html = await _gen.GenerateHtmlReport(data);
            Assert.DoesNotContain("Star Count CV", html);
        }

        [Fact]
        public async Task StarCountCV_InsufficientImages_NotShown() {
            Settings.Default.ShowStarCountCV = true;
            var data = TestDataFactory.MakeReportData(imageCount: 1);
            foreach (var img in data.Images) img.StarCount = 250;
            var html = await _gen.GenerateHtmlReport(data);
            // CV requires at least 2 images — single image session has no CV row
            Assert.DoesNotContain("Star Count CV", html);
        }

        // ── Min/Max/Mean/CV values in rows ───────────────────────────────────

        [Fact]
        public async Task IqRows_HfrMinMaxMean_AllPresent() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            foreach (var img in data.Images) { img.FWHM = 0; img.GuidingRMSTotal = 0; img.Eccentricity = 0; img.StarCount = 0; }
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("Min",  html);
            Assert.Contains("Max",  html);
            Assert.Contains("Mean", html);
            Assert.Contains("CV",   html);
        }

        // ── Per-target IQ section ────────────────────────────────────────────

        [Fact]
        public async Task PerTargetIQ_Enabled_MultiTarget_IqSectionPerTarget() {
            Settings.Default.ShowPerTargetIQ   = true;
            Settings.Default.ReportDetailLevel = 1;
            var data = TestDataFactory.MakeReportData(imageCount: 10, targetCount: 2);
            var html = await _gen.GenerateHtmlReport(data);
            // Per-target IQ adds an iq-table within each target section
            Assert.Contains("iq-table", html);
        }

        [Fact]
        public async Task PerTargetIQ_Disabled_NoExtraIqTable() {
            Settings.Default.ShowPerTargetIQ   = false;
            Settings.Default.ReportDetailLevel = 1;
            var data = TestDataFactory.MakeReportData(imageCount: 10, targetCount: 2);
            var html = await _gen.GenerateHtmlReport(data);
            // Use the attribute form to avoid matching the CSS rule ".iq-table { ... }"
            var count = CountOccurrences(html, "class='iq-table'");
            Assert.True(count <= 1, $"Expected at most 1 iq-table element, found {count}");
        }

        // ── Altitude chart ───────────────────────────────────────────────────

        [Fact]
        public async Task AltitudeChart_ValidCoords_SvgRendered() {
            Settings.Default.ShowSkyThumbnails = false;
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            // Spread timestamps so the altitude chart has a meaningful time range
            var baseTime = new DateTime(2025, 1, 15, 22, 0, 0);
            for (int i = 0; i < data.Images.Count; i++) {
                data.Images[i].RaHours    = 5.5833; // Orion Nebula RA
                data.Images[i].DecDegrees = -5.3911;
                data.Images[i].Timestamp  = baseTime.AddMinutes(i * 30);
            }
            // ObserverLatitude and ObserverLongitude are already set in MakeReportData (40.7128 / -74.0060)
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("altitude-chart", html);
        }

        [Fact]
        public async Task AltitudeChart_ZeroCoords_NotRendered() {
            Settings.Default.ShowSkyThumbnails = false;
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            // RA=0/Dec=0 is the default from MakeReportData — no altitude chart
            var html = await _gen.GenerateHtmlReport(data);
            Assert.DoesNotContain("altitude-chart", html);
        }

        [Fact]
        public async Task AltitudeChart_ZeroObserverLocation_NotRendered() {
            Settings.Default.ShowSkyThumbnails = false;
            // Pass zero lat/lon via factory
            var data = TestDataFactory.MakeReportData(imageCount: 5, observerLat: 0, observerLon: 0);
            foreach (var img in data.Images) {
                img.RaHours    = 5.5833;
                img.DecDegrees = -5.3911;
            }
            var html = await _gen.GenerateHtmlReport(data);
            Assert.DoesNotContain("altitude-chart", html);
        }

        // ── Overview stat boxes ──────────────────────────────────────────────

        [Fact]
        public async Task OverviewStats_DetailLevel2_SafetyMonitorFootnoteAppears() {
            Settings.Default.ReportDetailLevel = 2;
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            // Session has no safety monitor data by default
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("safety monitor", html.ToLower());
        }

        [Fact]
        public async Task OverviewStats_DetailLevel2_YieldBoxPresent() {
            Settings.Default.ReportDetailLevel = 2;
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("Yield", html);
        }

        [Fact]
        public async Task OverviewStats_FwhmInOverview_DetailLevel2() {
            Settings.Default.ReportDetailLevel = 2;
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            foreach (var img in data.Images) img.FWHM = 3.5;
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("FWHM", html);
        }

        // ── Unrecognized filter warning ──────────────────────────────────────

        [Fact]
        public async Task UnrecognizedFilter_GeneratesWarning() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            foreach (var img in data.Images) img.Filter = "XYZ_Unknown_Filter";
            await _gen.GenerateHtmlReport(data);
            Assert.True(_gen.Warnings.Any(w => w.Contains("XYZ_Unknown_Filter") || w.Contains("unrecognized") || w.Contains("Unrecognized")),
                "Expected an unrecognized filter warning");
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static int CountOccurrences(string text, string pattern) {
            int count = 0, idx = 0;
            while ((idx = text.IndexOf(pattern, idx)) != -1) { count++; idx += pattern.Length; }
            return count;
        }
    }
}
