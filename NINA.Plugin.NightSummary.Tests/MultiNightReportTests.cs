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
    /// Tests for multi-night report generation: header, aggregate stats,
    /// per-target sections, per-session cards, and edge cases.
    /// </summary>
    public class MultiNightReportTests {

        private readonly ReportGenerator _generator;

        public MultiNightReportTests() {
            SettingsManager.Instance.Current.ReportLightMode       = false;
            SettingsManager.Instance.Current.ReportDetailLevel     = 2;
            SettingsManager.Instance.Current.ShowSkyThumbnails     = false;
            SettingsManager.Instance.Current.ShowAltitudeChart     = false;
            SettingsManager.Instance.Current.ExpandSectionsDefault = false;
            _generator = new ReportGenerator();
        }

        // ── Header ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Header_ContainsMultiNightTitle() {
            var data = TestDataFactory.MakeMultiNightData();
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            Assert.Contains("Multi-Night Summary", html);
        }

        [Fact]
        public async Task Header_ContainsDateRange() {
            var data = TestDataFactory.MakeMultiNightData();
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            Assert.Contains("Jan 10, 2025", html);
            Assert.Contains("Jan 12, 2025", html);
        }

        [Fact]
        public async Task Header_ContainsSessionCount() {
            var data = TestDataFactory.MakeMultiNightData(sessionCount: 4);
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            Assert.Contains("4 nights", html);
        }

        [Fact]
        public async Task Header_ContainsProfileName() {
            var data = TestDataFactory.MakeMultiNightData();
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            Assert.Contains("Test Profile", html);
        }

        // ── Aggregate Overview ──────────────────────────────────────────────

        [Fact]
        public async Task Overview_TotalImages_MatchesAllImages() {
            var data = TestDataFactory.MakeMultiNightData(sessionCount: 3, imagesPerSession: 5);
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            // 3 sessions * 5 images/session * 2 targets = 30
            Assert.Contains(">30</div>", html);
        }

        [Fact]
        public async Task Overview_ContainsTotalExposure() {
            var data = TestDataFactory.MakeMultiNightData();
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            Assert.Contains("Total Exposure", html);
        }

        [Fact]
        public async Task Overview_ContainsTargetCount() {
            var data = TestDataFactory.MakeMultiNightData(targets: new[] { "M31", "NGC 7000", "IC 1805" });
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            Assert.Contains(">3</div>", html); // 3 targets
        }

        [Fact]
        public async Task Overview_ContainsSessionCount() {
            var data = TestDataFactory.MakeMultiNightData(sessionCount: 5);
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            Assert.Contains(">5</div>", html); // sessions stat box
        }

        [Fact]
        public async Task Overview_ContainsAvgHFR() {
            var data = TestDataFactory.MakeMultiNightData();
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            Assert.Contains("Avg HFR", html);
        }

        [Fact]
        public async Task Overview_ContainsAvgGuidingRMS() {
            var data = TestDataFactory.MakeMultiNightData();
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            Assert.Contains("Avg Guiding RMS", html);
        }

        // ── Per-Target Sections ─────────────────────────────────────────────

        [Fact]
        public async Task TargetSection_ContainsAllTargetNames() {
            var data = TestDataFactory.MakeMultiNightData(targets: new[] { "M31", "NGC 7000" });
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            Assert.Contains("M31", html);
            Assert.Contains("NGC 7000", html);
        }

        [Fact]
        public async Task TargetSection_ContainsFilterTable() {
            var data = TestDataFactory.MakeMultiNightData();
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            Assert.Contains("<th>Filter</th>", html);
            Assert.Contains("<th>Sessions</th>", html);
        }

        [Fact]
        public async Task TargetSection_ContainsSessionsColumn() {
            var data = TestDataFactory.MakeMultiNightData(sessionCount: 3);
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            // Each target should show 3 sessions in the total row
            Assert.Contains("<strong>3</strong>", html);
        }

        [Fact]
        public async Task TargetSection_PerSessionBreakdown_WhenMultipleSessions() {
            var data = TestDataFactory.MakeMultiNightData(sessionCount: 3);
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            Assert.Contains("Per-Session Breakdown", html);
        }

        [Fact]
        public async Task TargetSection_ContainsIntegrationInfo() {
            var data = TestDataFactory.MakeMultiNightData();
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            Assert.Contains("integration", html);
        }

        // ── Per-Session Cards ───────────────────────────────────────────────

        [Fact]
        public async Task SessionCards_ContainsAllSessionDates() {
            var data = TestDataFactory.MakeMultiNightData(sessionCount: 3);
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            Assert.Contains("Jan 10", html);
            Assert.Contains("Jan 11", html);
            Assert.Contains("Jan 12", html);
        }

        [Fact]
        public async Task SessionCards_ContainsTargetNames() {
            var data = TestDataFactory.MakeMultiNightData(targets: new[] { "M31" });
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            // Target name should appear in the session card body
            Assert.Contains("M31", html);
        }

        [Fact]
        public async Task SessionCards_ContainsMiniStats() {
            var data = TestDataFactory.MakeMultiNightData();
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            Assert.Contains("session-mini-stat", html);
            Assert.Contains("session-mini-value", html);
        }

        [Fact]
        public async Task SessionCards_ContainsFilterDetails() {
            var data = TestDataFactory.MakeMultiNightData();
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            Assert.Contains("Filter Details", html);
        }

        // ── Edge Cases ──────────────────────────────────────────────────────

        [Fact]
        public async Task EmptyRange_ShowsNoImagesMessage() {
            var data = new MultiNightReportData {
                From = new DateTime(2025, 1, 1),
                To = new DateTime(2025, 1, 7),
                ProfileName = "Test",
                Sessions = new List<SessionRecord>(),
                AllImages = new List<ImageRecord>(),
                ObserverLatitude = 40.0,
                ObserverLongitude = -74.0
            };
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            Assert.Contains("No images were recorded during this period", html);
        }

        [Fact]
        public async Task SingleSession_NoPerSessionBreakdownInTarget() {
            var data = TestDataFactory.MakeMultiNightData(sessionCount: 1);
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            // With only 1 session, per-session breakdown is not shown
            Assert.DoesNotContain("Per-Session Breakdown", html);
        }

        [Fact]
        public async Task RejectedImages_ShowsRejectedStatBox() {
            var data = TestDataFactory.MakeMultiNightData(sessionCount: 2, imagesPerSession: 5);
            // Mark some images as rejected
            for (int i = 0; i < 4; i++)
                data.AllImages[i].Accepted = false;
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            Assert.Contains("Rejected", html);
        }

        [Fact]
        public async Task LightMode_UsesLightThemeColors() {
            SettingsManager.Instance.Current.ReportLightMode = true;
            try {
                var data = TestDataFactory.MakeMultiNightData();
                var html = await _generator.GenerateMultiNightHtmlReport(data);
                Assert.Contains("--bg: #f5f5f5", html);
            } finally {
                SettingsManager.Instance.Current.ReportLightMode = false;
            }
        }

        [Fact]
        public async Task Report_ContainsFooter() {
            var data = TestDataFactory.MakeMultiNightData();
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            Assert.Contains("Generated by Night Summary", html);
        }

        // ── Filter Breakdown in Overview ────────────────────────────────────

        [Fact]
        public async Task Overview_FilterBreakdown_ExpandableStatBox() {
            var data = TestDataFactory.MakeMultiNightData();
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            Assert.Contains("stat-breakdown", html);
            Assert.Contains("stat-breakdown-body", html);
        }

        [Fact]
        public async Task Overview_FilterBreakdown_ContainsFilterName() {
            var data = TestDataFactory.MakeMultiNightData();
            var html = await _generator.GenerateMultiNightHtmlReport(data);
            // All test images use "Ha" filter
            Assert.Contains("Ha", html);
        }
    }
}
