using NINA.Plugin.NightSummary.Data;

using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    /// <summary>
    /// Tests for content correctness in ReportGenerator output:
    /// filter breakdown stat boxes, accepted/rejected image counts,
    /// cumulative integration, FormatDuration, and multi-filter sessions.
    /// </summary>
    public class ReportGeneratorContentTests {

        private readonly ReportGenerator _generator;

        public ReportGeneratorContentTests() {
            SettingsManager.Instance.Current.ReportLightMode        = false;
            SettingsManager.Instance.Current.ReportDetailLevel      = 2;
            SettingsManager.Instance.Current.ShowHFRGraph           = false;
            SettingsManager.Instance.Current.ShowStarCountCV        = false;
            SettingsManager.Instance.Current.ShowPerTargetIQ        = false;
            SettingsManager.Instance.Current.ShowSessionHistory     = false;
            SettingsManager.Instance.Current.ShowNextNightPreview   = false;
            SettingsManager.Instance.Current.AdditionalChartConfigs = "";
            SettingsManager.Instance.Current.ExpandSectionsDefault  = false;
            _generator = new ReportGenerator();
        }

        // ── Filter breakdown stat boxes ────────────────────────────────────────

        [Fact]
        public async Task FilterBreakdown_StatBox_ContainsDetailsElement() {
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("stat-breakdown", report);
        }

        [Fact]
        public async Task FilterBreakdown_StatBox_ContainsBreakdownBody() {
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("stat-breakdown-body", report);
        }

        [Fact]
        public async Task FilterBreakdown_MultipleFilters_AllFiltersAppearInBreakdown() {
            var sessionId = Guid.NewGuid().ToString();
            var session   = TestDataFactory.MakeSession(sessionId);
            var images    = new List<ImageRecord>();
            var filters   = new[] { "Ha", "OIII", "SII" };
            foreach (var f in filters) {
                for (int i = 0; i < 5; i++)
                    images.Add(TestDataFactory.MakeImage(sessionId, filter: f));
            }
            var data = new ReportData {
                Session = session, Images = images, Events = new List<SessionEvent>(),
                TsData  = new List<TsTargetData>(),
                CumulativeIntegrationSeconds = new Dictionary<string, double>(),
                SessionHistory = new Dictionary<string, List<TargetSessionHistory>>()
            };
            var report = await _generator.GenerateHtmlReport(data);
            foreach (var f in filters)
                Assert.Contains(f, report);
        }

        [Fact]
        public async Task FilterBreakdown_ImageCount_AppearsInBreakdownRow() {
            var data   = TestDataFactory.MakeReportData(imageCount: 10, targets: new[] { "M31" });
            var report = await _generator.GenerateHtmlReport(data);
            // 10 images total — breakdown should list the filter count
            Assert.Contains("stat-breakdown-row", report);
        }

        // ── Total Images stat box ─────────────────────────────────────────────

        [Fact]
        public async Task OverviewStats_TotalImages_ShowsCorrectCount() {
            var data   = TestDataFactory.MakeReportData(imageCount: 12);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains(">12<", report);
        }

        [Fact]
        public async Task OverviewStats_TotalImages_StatLabelPresent() {
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("Total Images", report);
        }

        [Fact]
        public async Task OverviewStats_TotalExposure_StatLabelPresent() {
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("Total Exposure", report);
        }

        // ── FormatDuration ────────────────────────────────────────────────────

        [Fact]
        public async Task FormatDuration_UnderOneHour_ShowsMinutes() {
            // 10 images × 300s = 3000s = 50m
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("50m", report);
        }

        [Fact]
        public async Task FormatDuration_OverOneHour_ShowsHoursAndMinutes() {
            // 24 images × 300s = 7200s = 2h 0m → "2h"
            var data   = TestDataFactory.MakeReportData(imageCount: 24);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("2h", report);
        }

        // ── Accepted / rejected images ─────────────────────────────────────────

        [Fact]
        public async Task Report_AcceptedImages_CountedInTotal() {
            var sessionId = Guid.NewGuid().ToString();
            var session   = TestDataFactory.MakeSession(sessionId);
            var images    = new List<ImageRecord>();
            for (int i = 0; i < 8; i++)
                images.Add(TestDataFactory.MakeImage(sessionId, accepted: true));
            for (int i = 0; i < 2; i++)
                images.Add(TestDataFactory.MakeImage(sessionId, accepted: false));
            var data = new ReportData {
                Session = session, Images = images, Events = new List<SessionEvent>(),
                TsData  = new List<TsTargetData>(),
                CumulativeIntegrationSeconds = new Dictionary<string, double>(),
                SessionHistory = new Dictionary<string, List<TargetSessionHistory>>()
            };
            var report = await _generator.GenerateHtmlReport(data);
            // Total image count includes both accepted and unaccepted
            Assert.Contains(">10<", report);
        }

        // ── Cumulative integration ────────────────────────────────────────────

        [Fact]
        public async Task NoTSData_TsCumulativeSection_NotRendered() {
            // CumulativeIntegrationSeconds is not used by ReportGenerator directly —
            // the ts-cumulative paragraph only renders when TS progress bar data is present.
            // Without TS data this section should be absent.
            var data   = TestDataFactory.MakeReportData(imageCount: 10, targets: new[] { "M31" });
            var report = await _generator.GenerateHtmlReport(data);
            // ".ts-cumulative" is always present in the stylesheet — check for the rendered tag instead
            Assert.DoesNotContain("<p class='ts-cumulative'", report);
        }

        // ── Multi-target report ────────────────────────────────────────────────

        [Fact]
        public async Task MultiTarget_EachTargetHasOwnSection() {
            var targets = new[] { "M31", "M42", "NGC 7000" };
            var data    = TestDataFactory.MakeReportData(imageCount: 30, targets: targets);
            var report  = await _generator.GenerateHtmlReport(data);
            foreach (var t in targets)
                Assert.Contains(t, report);
        }

        [Fact]
        public async Task MultiTarget_FilterTable_ContainsHeaderRow() {
            var data   = TestDataFactory.MakeReportData(imageCount: 20, targetCount: 2);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("Filter", report);
            Assert.Contains("Images", report);
        }

        // ── Session date/time formatting ───────────────────────────────────────

        [Fact]
        public async Task Report_ContainsFormattedSessionDate() {
            var start = new DateTime(2025, 3, 15, 21, 0, 0);
            var data  = TestDataFactory.MakeReportData(imageCount: 10);
            data.Session.SessionStart = start;
            var report = await _generator.GenerateHtmlReport(data);
            // Date should appear in some human-readable form
            Assert.Contains("2025", report);
        }

        // ── ExpandSectionsDefault ──────────────────────────────────────────────

        [Fact]
        public async Task ExpandSectionsDefault_True_SectionsHaveOpenAttribute() {
            SettingsManager.Instance.Current.ExpandSectionsDefault = true;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            SettingsManager.Instance.Current.ExpandSectionsDefault = false; // reset
            // detailsOpen = " open" → rendered as <details class='...' open>
            Assert.Contains("' open>", report);
        }

        [Fact]
        public async Task ExpandSectionsDefault_False_SectionsHaveNoOpenAttribute() {
            SettingsManager.Instance.Current.ExpandSectionsDefault = false;
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.DoesNotContain("' open>", report);
        }

        // ── Footer ────────────────────────────────────────────────────────────

        [Fact]
        public async Task Report_ContainsFooter() {
            var data   = TestDataFactory.MakeReportData(imageCount: 10);
            var report = await _generator.GenerateHtmlReport(data);
            Assert.Contains("Night Summary", report);
            Assert.Contains("N.I.N.A", report);
        }

        // ── Overhead breakdown ────────────────────────────────────────────────

        private static ReportData MakeOverheadReportData(List<TimingEvent> timingEvents) {
            var baseData = TestDataFactory.MakeReportData(imageCount: 8);
            // Replace session images with 8×600s exposures starting at a known time
            var sessionId = baseData.Session.SessionId;
            var t0 = new DateTime(2026, 4, 22, 22, 0, 0);
            var images = new List<ImageRecord>();
            for (int i = 0; i < 8; i++)
                images.Add(new ImageRecord {
                    SessionId = sessionId, TargetName = "Cat 91", Filter = "H",
                    ExposureDuration = 600,
                    Timestamp = t0.AddSeconds(i * 605 + 5)
                });
            return new ReportData {
                Session = baseData.Session,
                Images = images,
                Events = baseData.Events,
                TsData = baseData.TsData,
                CumulativeIntegrationSeconds = baseData.CumulativeIntegrationSeconds,
                SessionHistory = baseData.SessionHistory,
                TimingEvents = timingEvents
            };
        }

        [Fact]
        public async Task OverheadCoverage_AbortedExposurePastWindowEnd_DoesNotReportHundredPercent() {
            // Reproduce the bug: AbortedExposure whose end exceeds windowEnd was
            // included in MergeOverheadIntervals, inflating mergedOverheadSec above
            // impliedOverheadSec and causing coverage to be capped at 100%.
            //
            // Session: 8×600s = 4800s integration; window ~5436s → implied ~636s.
            // AbortedExposure of 52s is included in the overhead table but must NOT
            // contribute to the coverage percentage.
            SettingsManager.Instance.Current.ShowOverheadBreakdown = true;

            var t0 = new DateTime(2026, 4, 22, 21, 58, 43);
            var timingEvents = new List<TimingEvent> {
                new() { EventType = "TempCompFocus", StartTime = t0,               EndTime = t0.AddSeconds(7),   DurationSeconds = 7 },
                new() { EventType = "Dither",        StartTime = t0.AddSeconds(870),  EndTime = t0.AddSeconds(898), DurationSeconds = 28 },
                new() { EventType = "Dither",        StartTime = t0.AddSeconds(2139), EndTime = t0.AddSeconds(2166), DurationSeconds = 27 },
                new() { EventType = "StartGuiding",  StartTime = t0.AddSeconds(2200), EndTime = t0.AddSeconds(2309), DurationSeconds = 109 },
                // Exposure events (8 complete) — excluded from overhead merge
                new() { EventType = "Exposure",      StartTime = t0.AddSeconds(10),  EndTime = t0.AddSeconds(615), DurationSeconds = 605 },
                new() { EventType = "Exposure",      StartTime = t0.AddSeconds(620), EndTime = t0.AddSeconds(1225), DurationSeconds = 605 },
                // AbortedExposure: starts inside window, ends 52s past windowEnd (2309s)
                // windowEnd = t0+2309, AbortedExposure end = t0+2309+52 = t0+2361
                new() { EventType = "AbortedExposure", StartTime = t0.AddSeconds(2260), EndTime = t0.AddSeconds(2361), DurationSeconds = 52.3 },
            };

            var data = MakeOverheadReportData(timingEvents);
            var report = await _generator.GenerateHtmlReport(data);

            Assert.Contains("Overhead Accounted", report);
            Assert.DoesNotContain(">100.0%<", report);
        }

        [Fact]
        public async Task OverheadCoverage_AbortedExposurePastWindowEnd_IsShownInBreakdownTable() {
            // AbortedExposure (Skipped Exposure) must still appear in the overhead
            // breakdown table even though it is excluded from the coverage calculation.
            SettingsManager.Instance.Current.ShowOverheadBreakdown = true;

            var t0 = new DateTime(2026, 4, 22, 21, 58, 43);
            var timingEvents = new List<TimingEvent> {
                new() { EventType = "TempCompFocus",   StartTime = t0,                EndTime = t0.AddSeconds(7),   DurationSeconds = 7 },
                new() { EventType = "Dither",          StartTime = t0.AddSeconds(870), EndTime = t0.AddSeconds(898), DurationSeconds = 28 },
                new() { EventType = "Exposure",        StartTime = t0.AddSeconds(10),  EndTime = t0.AddSeconds(615), DurationSeconds = 605 },
                new() { EventType = "AbortedExposure", StartTime = t0.AddSeconds(900), EndTime = t0.AddSeconds(960), DurationSeconds = 52.3 },
            };

            var data = MakeOverheadReportData(timingEvents);
            var report = await _generator.GenerateHtmlReport(data);

            Assert.Contains("Skipped Exposure", report);
        }
    }
}
