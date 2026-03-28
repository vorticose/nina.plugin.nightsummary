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
    /// Tests for Settings-controlled branches in ReportGenerator: detail levels,
    /// optional sections, chart configuration, and data-conditional output.
    /// </summary>
    public class ReportGeneratorSettingsTests {

        private readonly ReportGenerator _generator;

        public ReportGeneratorSettingsTests() {
            // Establish a known baseline for all settings-controlled branches
            Settings.Default.ReportLightMode         = false;
            Settings.Default.ReportDetailLevel       = 2;   // Full
            Settings.Default.ShowHFRGraph            = true;
            Settings.Default.ChartPrimaryMetric      = 0;   // HFR
            Settings.Default.ChartSecondaryMetric    = 0;   // SecNone
            Settings.Default.AdditionalChartConfigs  = "";
            Settings.Default.ShowStarCountCV         = false;
            Settings.Default.ShowPerTargetIQ         = false;
            Settings.Default.ShowSessionHistory      = false;
            Settings.Default.ShowNextNightPreview    = false;
            Settings.Default.ExpandSectionsDefault   = false;
            _generator = new ReportGenerator();
        }

        // ── Detail level: Overview stat boxes ──────────────────────────────

        [Fact]
        public async Task DetailLevel0_NoAvgHFR_InOverview() {
            Settings.Default.ReportDetailLevel = 0;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.DoesNotContain("Avg HFR", report);
        }

        [Fact]
        public async Task DetailLevel0_NoYieldBox() {
            Settings.Default.ReportDetailLevel = 0;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.DoesNotContain(">Yield", report);
        }

        [Fact]
        public async Task DetailLevel0_NoMoonBox() {
            Settings.Default.ReportDetailLevel = 0;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.DoesNotContain(">Moon<", report);
        }

        [Fact]
        public async Task DetailLevel1_HasAvgHFR_InOverview() {
            Settings.Default.ReportDetailLevel = 1;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("Avg HFR", report);
        }

        [Fact]
        public async Task DetailLevel1_HasAvgGuidingRMS_InOverview() {
            Settings.Default.ReportDetailLevel = 1;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("Avg Guiding RMS", report);
        }

        [Fact]
        public async Task DetailLevel2_HasYieldBox() {
            Settings.Default.ReportDetailLevel = 2;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains(">Yield", report);
        }

        [Fact]
        public async Task DetailLevel2_HasMoonBox() {
            Settings.Default.ReportDetailLevel = 2;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains(">Moon<", report);
        }

        // ── Detail level: IQ section ────────────────────────────────────────

        [Fact]
        public async Task DetailLevel0_NoIQSection() {
            Settings.Default.ReportDetailLevel = 0;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.DoesNotContain("Session Image Quality", report);
        }

        [Fact]
        public async Task DetailLevel1_HasIQSection() {
            Settings.Default.ReportDetailLevel = 1;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("Session Image Quality", report);
        }

        // ── Detail level: event timeline ────────────────────────────────────

        [Fact]
        public async Task DetailLevel0_NoEventTimeline() {
            Settings.Default.ReportDetailLevel = 0;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.DoesNotContain("Session Timeline", report);
        }

        [Fact]
        public async Task DetailLevel1_HasEventTimeline_WhenEventsPresent() {
            Settings.Default.ReportDetailLevel = 1;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            // MakeReportData includes one AutoFocus event by default
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("Session Timeline", report);
        }

        // ── Chart section ───────────────────────────────────────────────────

        [Fact]
        public async Task ShowHFRGraph_True_DetailLevel2_ChartAppears() {
            Settings.Default.ShowHFRGraph      = true;
            Settings.Default.ReportDetailLevel = 2;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("HFR Vs. Time", report);
        }

        [Fact]
        public async Task ShowHFRGraph_False_NoChart() {
            Settings.Default.ShowHFRGraph      = false;
            Settings.Default.ReportDetailLevel = 2;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.DoesNotContain("HFR Vs. Time", report);
        }

        [Fact]
        public async Task ShowHFRGraph_True_DetailLevel1_NoChart() {
            // Chart requires detailLevel >= 2
            Settings.Default.ShowHFRGraph      = true;
            Settings.Default.ReportDetailLevel = 1;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.DoesNotContain("HFR Vs. Time", report);
        }

        [Fact]
        public async Task AdditionalChartConfig_AddsSecondChart() {
            Settings.Default.ShowHFRGraph           = true;
            Settings.Default.ReportDetailLevel      = 2;
            Settings.Default.ChartPrimaryMetric     = 0; // HFR
            Settings.Default.AdditionalChartConfigs = "1:0"; // FWHM:SecNone
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("HFR Vs. Time",  report);
            Assert.Contains("FWHM Vs. Time", report);
        }

        [Fact]
        public async Task EmptyAdditionalChartConfig_NoExtraChart() {
            Settings.Default.ShowHFRGraph           = true;
            Settings.Default.ReportDetailLevel      = 2;
            Settings.Default.AdditionalChartConfigs = "";
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            // Only one chart — "HFR Vs. Time" should appear exactly once as an h2
            var count = CountOccurrences(report, "HFR Vs. Time");
            Assert.Equal(1, count);
        }

        // ── Star count CV section ───────────────────────────────────────────

        [Fact]
        public async Task ShowStarCountCV_True_DetailLevel1_SectionPresent() {
            Settings.Default.ShowStarCountCV   = true;
            Settings.Default.ReportDetailLevel = 1;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("Star Count Consistency", report);
        }

        [Fact]
        public async Task ShowStarCountCV_False_SectionAbsent() {
            Settings.Default.ShowStarCountCV = false;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.DoesNotContain("Star Count Consistency", report);
        }

        // ── Per-target IQ section ───────────────────────────────────────────

        [Fact]
        public async Task ShowPerTargetIQ_True_MultiTarget_SectionPresent() {
            Settings.Default.ShowPerTargetIQ   = true;
            Settings.Default.ReportDetailLevel = 1;
            // Multi-target needed to trigger per-target IQ
            var data   = TestDataFactory.MakeReportData(imageCount: 20, targetCount: 2);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("Image Quality", report);
            Assert.Contains("iq-section", report);
        }

        // ── Session history section ─────────────────────────────────────────

        [Fact]
        public async Task ShowSessionHistory_True_WithData_HistoryTablePresent() {
            Settings.Default.ShowSessionHistory = true;
            Settings.Default.ReportDetailLevel  = 2;
            var history = new Dictionary<string, List<TargetSessionHistory>> {
                ["M31"] = new List<TargetSessionHistory> {
                    new TargetSessionHistory {
                        SessionStart       = new DateTime(2025, 1, 10, 21, 0, 0),
                        IntegrationSeconds = 7200,
                        AvgHFR             = 2.4,
                        AvgFWHM            = 3.1,
                        AvgGuidingRMS      = 0.60
                    }
                }
            };
            var data = TestDataFactory.MakeReportData(imageCount: 10, targets: new[] { "M31" }, sessionHistory: history);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("Session History", report);
            Assert.Contains("Jan 10, 2025",    report);
        }

        [Fact]
        public async Task ShowSessionHistory_True_NoData_HistoryAbsent() {
            Settings.Default.ShowSessionHistory = true;
            Settings.Default.ReportDetailLevel  = 2;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            // SessionHistory is empty by default in MakeReportData
            var report = await _generator.GenerateHtmlReport(data);
            Assert.DoesNotContain("Session History", report);
        }

        // ── Safety monitor footnote ─────────────────────────────────────────

        [Fact]
        public async Task DetailLevel2_NoSafetyMonitor_FootnotePresent() {
            Settings.Default.ReportDetailLevel = 2;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            // No roof events → hasSafetyMonitor = false → footnote "*" appears
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("without cloud exclusion", report);
        }

        // ── ShowNextNightPreview: no crash when TS not running ──────────────

        [Fact]
        public async Task ShowNextNightPreview_True_NoExceptionWhenTsNotRunning() {
            Settings.Default.ShowNextNightPreview = true;
            Settings.Default.ReportDetailLevel    = 2;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            // Should not throw — TS not running returns empty section gracefully
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("<html", report);
        }

        // ── Filter table FormatDuration output ─────────────────────────────

        [Fact]
        public async Task FilterTable_ContainsFormattedDuration() {
            // 10 images × 300s each = 3000s = 50m → should format as "50m"
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("50m", report);
        }

        // ── Helper ─────────────────────────────────────────────────────────

        private static int CountOccurrences(string haystack, string needle) {
            int count = 0, idx = 0;
            while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0) {
                count++;
                idx += needle.Length;
            }
            return count;
        }
    }
}
