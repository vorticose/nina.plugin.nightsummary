using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    public class ReportGeneratorTests {

        private readonly ReportGenerator _generator;

        public ReportGeneratorTests() {
            SettingsManager.Instance.Current.ReportLightMode = false;
            _generator = TestDeps.NewReportGenerator();
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

            SettingsManager.Instance.Current.ReportLightMode = false;
            var darkReport  = await _generator.GenerateHtmlReport(data);

            SettingsManager.Instance.Current.ReportLightMode = true;
            var lightReport = await _generator.GenerateHtmlReport(data);

            SettingsManager.Instance.Current.ReportLightMode = false; // reset

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

            // The aborted span uses var(--skip-color) inline — should not appear when nothing was skipped
            // Note: --skip-color is defined in the CSS block regardless; check for its usage in content
            Assert.DoesNotContain("var(--skip-color)", report);
        }

        // ── Warnings ────────────────────────────────────────────────────────

        [Fact]
        public async Task Warnings_ContainNoUnexpectedEntries_ForCleanData() {
            var data = TestDataFactory.MakeReportData(imageCount: 10);
            await _generator.GenerateHtmlReport(data);

            // TS API warning is expected when Target Scheduler is not running — filter it out
            var unexpected = _generator.Warnings
                .Where(w => !w.Contains("Tonight's Preview") && !w.Contains("Target Scheduler"))
                .ToList();
            Assert.Empty(unexpected);
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
