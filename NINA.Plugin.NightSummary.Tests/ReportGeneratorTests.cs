using NINA.Plugin.NightSummary.MyPluginProperties;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    public class ReportGeneratorTests {

        private readonly ReportGenerator _generator;

        public ReportGeneratorTests() {
            Settings.Default.ReportLightMode = false;
            _generator = new ReportGenerator();
        }

        // ── Basic rendering ───────────────────────────────────────────────────

        [Fact]
        public async Task GenerateReport_ProducesNonEmptyHtml() {
            var data   = TestDataFactory.MakeReportData(imageCount: 10, targetCount: 1);
            var report = await _generator.GenerateHtmlReport(data);

            Assert.NotNull(report);
            Assert.NotEmpty(report);
            Assert.Contains("<html", report);
            Assert.Contains("</html>", report);
        }

        [Fact]
        public async Task GenerateReport_ContainsTargetName() {
            var data   = TestDataFactory.MakeReportData(imageCount: 10, targetCount: 1, targets: new[] { "M31" });
            var report = await _generator.GenerateHtmlReport(data);

            Assert.Contains("M31", report);
        }

        [Fact]
        public async Task GenerateReport_WithZeroImages_DoesNotCrash() {
            var data   = TestDataFactory.MakeReportData(imageCount: 0);
            var report = await _generator.GenerateHtmlReport(data);

            Assert.NotNull(report);
            Assert.Contains("<html", report);
        }

        [Fact]
        public async Task GenerateReport_WithMultipleTargets_ContainsAllTargetNames() {
            var targets = new[] { "M31", "M42", "NGC 7000" };
            var data    = TestDataFactory.MakeReportData(imageCount: 30, targetCount: 3, targets: targets);
            var report  = await _generator.GenerateHtmlReport(data);

            foreach (var target in targets) {
                Assert.Contains(target, report);
            }
        }

        // ── Theme ────────────────────────────────────────────────────────────

        [Fact]
        public async Task LightModeReport_ContainsDifferentCssThanDarkMode() {
            var data = TestDataFactory.MakeReportData(imageCount: 10);

            Settings.Default.ReportLightMode = false;
            var darkReport  = await _generator.GenerateHtmlReport(data);

            Settings.Default.ReportLightMode = true;
            var lightReport = await _generator.GenerateHtmlReport(data);

            Settings.Default.ReportLightMode = false; // reset

            Assert.NotEqual(darkReport, lightReport);
        }

        // ── Skipped exposures ─────────────────────────────────────────────────

        [Fact]
        public async Task Report_ShowsSkipIndicator_WhenSkippedExposuresGreaterThanZero() {
            var data   = TestDataFactory.MakeReportData(imageCount: 10, skippedExp: 3);
            var report = await _generator.GenerateHtmlReport(data);

            // Report should contain the skip count somewhere in the output
            Assert.Contains("3", report);
            Assert.Contains("abort", report.ToLower());
        }

        [Fact]
        public async Task Report_NoSkipIndicator_WhenSkippedExposuresIsZero() {
            var data   = TestDataFactory.MakeReportData(imageCount: 10, skippedExp: 0);
            var report = await _generator.GenerateHtmlReport(data);

            // The skip-color span should not appear when nothing was skipped
            Assert.DoesNotContain("skip-color", report);
        }

        // ── Warnings ────────────────────────────────────────────────────────

        [Fact]
        public async Task Warnings_AreEmpty_ForCleanData() {
            var data = TestDataFactory.MakeReportData(imageCount: 10);
            await _generator.GenerateHtmlReport(data);

            Assert.Empty(_generator.Warnings);
        }

        // ── Size sanity check ────────────────────────────────────────────────

        [Fact]
        public async Task Report_IsUnder5MB() {
            var data   = TestDataFactory.MakeReportData(imageCount: 50, targetCount: 6);
            var report = await _generator.GenerateHtmlReport(data);

            var bytes = System.Text.Encoding.UTF8.GetByteCount(report);
            Assert.True(bytes < 5 * 1024 * 1024, $"Report is {bytes / 1024}KB — exceeds 5MB limit");
        }
    }
}
