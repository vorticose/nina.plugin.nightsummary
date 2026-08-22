using NINA.Plugin.NightSummary.Data;

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
            SettingsManager.Instance.Current.ReportLightMode         = false;
            SettingsManager.Instance.Current.ReportDetailLevel       = 2;   // Full
            SettingsManager.Instance.Current.ShowHFRGraph            = true;
            SettingsManager.Instance.Current.ChartPrimaryMetric      = 0;   // HFR
            SettingsManager.Instance.Current.ChartSecondaryMetric    = 0;   // SecNone
            SettingsManager.Instance.Current.AdditionalChartConfigs  = "";
            SettingsManager.Instance.Current.ChartXAxisMetric        = 0;   // Time
            SettingsManager.Instance.Current.ShowStarCountCV         = false;
            SettingsManager.Instance.Current.ShowPerTargetIQ         = false;
            SettingsManager.Instance.Current.ShowSessionHistory      = false;
            SettingsManager.Instance.Current.ShowNextNightPreview    = false;
            SettingsManager.Instance.Current.ExpandSectionsDefault   = false;
            _generator = TestDeps.NewReportGenerator();
        }

        // ── Detail level: Overview stat boxes ──────────────────────────────

        [Fact]
        public async Task DetailLevel0_NoAvgHFR_InOverview() {
            SettingsManager.Instance.Current.ReportDetailLevel = 0;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.DoesNotContain("Avg HFR", report);
        }

        [Fact]
        public async Task DetailLevel0_NoYieldBox() {
            SettingsManager.Instance.Current.ReportDetailLevel = 0;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.DoesNotContain(">Yield", report);
        }

        [Fact]
        public async Task DetailLevel0_NoMoonBox() {
            SettingsManager.Instance.Current.ReportDetailLevel = 0;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.DoesNotContain(">Moon<", report);
        }

        [Fact]
        public async Task DetailLevel1_HasAvgHFR_InOverview() {
            SettingsManager.Instance.Current.ReportDetailLevel = 1;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("Avg HFR", report);
        }

        [Fact]
        public async Task DetailLevel1_HasAvgGuidingRMS_InOverview() {
            SettingsManager.Instance.Current.ReportDetailLevel = 1;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("Avg Guiding RMS", report);
        }

        [Fact]
        public async Task DetailLevel2_HasYieldBox() {
            SettingsManager.Instance.Current.ReportDetailLevel = 2;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains(">Yield", report);
        }

        [Fact]
        public async Task DetailLevel2_HasMoonBox() {
            SettingsManager.Instance.Current.ReportDetailLevel = 2;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains(">Moon<", report);
        }

        // ── Detail level: IQ section ────────────────────────────────────────

        [Fact]
        public async Task DetailLevel0_NoIQSection() {
            SettingsManager.Instance.Current.ReportDetailLevel = 0;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.DoesNotContain("Session Image Quality", report);
        }

        [Fact]
        public async Task DetailLevel1_HasIQSection() {
            SettingsManager.Instance.Current.ReportDetailLevel = 1;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("Session Image Quality", report);
        }

        // ── Detail level: event timeline ────────────────────────────────────

        [Fact]
        public async Task DetailLevel0_NoEventTimeline() {
            SettingsManager.Instance.Current.ReportDetailLevel = 0;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.DoesNotContain("Session Timeline", report);
        }

        [Fact]
        public async Task DetailLevel1_HasEventTimeline_WhenEventsPresent() {
            SettingsManager.Instance.Current.ReportDetailLevel = 1;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            // MakeReportData includes one AutoFocus event by default
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("Session Timeline", report);
        }

        // ── Chart section ───────────────────────────────────────────────────

        [Fact]
        public async Task ShowHFRGraph_True_DetailLevel2_ChartAppears() {
            SettingsManager.Instance.Current.ShowHFRGraph      = true;
            SettingsManager.Instance.Current.ReportDetailLevel = 2;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("HFR Vs. Time", report);
        }

        [Fact]
        public async Task ShowHFRGraph_False_NoChart() {
            SettingsManager.Instance.Current.ShowHFRGraph      = false;
            SettingsManager.Instance.Current.ReportDetailLevel = 2;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.DoesNotContain("HFR Vs. Time", report);
        }

        [Fact]
        public async Task ShowHFRGraph_True_DetailLevel1_NoChart() {
            // Chart requires detailLevel >= 2
            SettingsManager.Instance.Current.ShowHFRGraph      = true;
            SettingsManager.Instance.Current.ReportDetailLevel = 1;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.DoesNotContain("HFR Vs. Time", report);
        }

        [Fact]
        public async Task AdditionalChartConfig_AddsSecondChart() {
            SettingsManager.Instance.Current.ShowHFRGraph           = true;
            SettingsManager.Instance.Current.ReportDetailLevel      = 2;
            SettingsManager.Instance.Current.ChartPrimaryMetric     = 0; // HFR
            SettingsManager.Instance.Current.AdditionalChartConfigs = "1:0"; // FWHM:SecNone
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("HFR Vs. Time",  report);
            Assert.Contains("FWHM Vs. Time", report);
        }

        [Fact]
        public async Task AdditionalChartConfig_ThreeTokenFormat_SetsXAxis() {
            SettingsManager.Instance.Current.ShowHFRGraph           = true;
            SettingsManager.Instance.Current.ReportDetailLevel      = 2;
            SettingsManager.Instance.Current.ChartPrimaryMetric     = 0; // HFR
            // 3-token format: FWHM:SecNone:FrameIndex
            SettingsManager.Instance.Current.AdditionalChartConfigs = $"1:0:{ChartGenerator.XAxisFrameIndex}";
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("HFR Vs. Time",  report);  // default chart still uses Time
            Assert.Contains("FWHM Vs. Frame", report);  // additional chart uses Frame x-axis
        }

        [Fact]
        public async Task AdditionalChartConfig_TwoTokenFormat_DefaultsToTimeXAxis() {
            SettingsManager.Instance.Current.ShowHFRGraph           = true;
            SettingsManager.Instance.Current.ReportDetailLevel      = 2;
            SettingsManager.Instance.Current.ChartPrimaryMetric     = 0; // HFR
            SettingsManager.Instance.Current.AdditionalChartConfigs = "1:0";
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("FWHM Vs. Time", report);
        }

        [Fact]
        public async Task DefaultXAxisSetting_AppliesToMainChart() {
            SettingsManager.Instance.Current.ShowHFRGraph           = true;
            SettingsManager.Instance.Current.ReportDetailLevel      = 2;
            SettingsManager.Instance.Current.ChartPrimaryMetric     = 0; // HFR
            SettingsManager.Instance.Current.ChartXAxisMetric       = ChartGenerator.XAxisFrameIndex;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("HFR Vs. Frame", report);
            Assert.DoesNotContain("HFR Vs. Time", report);
        }

        [Fact]
        public async Task EmptyAdditionalChartConfig_NoExtraChart() {
            SettingsManager.Instance.Current.ShowHFRGraph           = true;
            SettingsManager.Instance.Current.ReportDetailLevel      = 2;
            SettingsManager.Instance.Current.AdditionalChartConfigs = "";
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            // Only one chart section. Match the <h2> header exactly to avoid
            // false positives from SVG text content or CSS class names.
            var count = CountOccurrences(report, "<h2>HFR Vs. Time</h2>");
            Assert.Equal(1, count);
        }

        // ── Star count CV section ───────────────────────────────────────────

        [Fact]
        public async Task ShowStarCountCV_True_DetailLevel1_SectionPresent() {
            SettingsManager.Instance.Current.ShowStarCountCV   = true;
            SettingsManager.Instance.Current.ReportDetailLevel = 1;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("Star Count Consistency", report);
        }

        [Fact]
        public async Task ShowStarCountCV_False_SectionAbsent() {
            SettingsManager.Instance.Current.ShowStarCountCV = false;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.DoesNotContain("Star Count Consistency", report);
        }

        // ── Per-target IQ section ───────────────────────────────────────────

        [Fact]
        public async Task ShowPerTargetIQ_True_MultiTarget_SectionPresent() {
            SettingsManager.Instance.Current.ShowPerTargetIQ   = true;
            SettingsManager.Instance.Current.ReportDetailLevel = 1;
            // Multi-target needed to trigger per-target IQ
            var data   = TestDataFactory.MakeReportData(imageCount: 20, targetCount: 2);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("Image Quality", report);
            Assert.Contains("iq-section", report);
        }

        // ── Session history section ─────────────────────────────────────────

        [Fact]
        public async Task ShowSessionHistory_True_WithData_HistoryTablePresent() {
            SettingsManager.Instance.Current.ShowSessionHistory = true;
            SettingsManager.Instance.Current.ReportDetailLevel  = 2;
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
            SettingsManager.Instance.Current.ShowSessionHistory = true;
            SettingsManager.Instance.Current.ReportDetailLevel  = 2;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            // SessionHistory is empty by default in MakeReportData
            var report = await _generator.GenerateHtmlReport(data);
            Assert.DoesNotContain("Session History", report);
        }

        // ── Safety monitor footnote ─────────────────────────────────────────

        [Fact]
        public async Task DetailLevel2_NoSafetyMonitor_FootnotePresent() {
            SettingsManager.Instance.Current.ReportDetailLevel = 2;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            // No roof events → hasSafetyMonitor = false → footnote "*" appears
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("without cloud exclusion", report);
        }

        // ── ShowNextNightPreview: no crash when TS not running ──────────────

        [Fact]
        public async Task ShowNextNightPreview_True_NoExceptionWhenTsNotRunning() {
            SettingsManager.Instance.Current.ShowNextNightPreview = true;
            SettingsManager.Instance.Current.ReportDetailLevel    = 2;
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
