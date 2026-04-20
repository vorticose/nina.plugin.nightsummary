using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Tests for the per-target minimum-altitude line on the multi-target altitude chart
    /// used by the Session Timeline section (BuildSessionAltitudeChart in ReportGenerator).
    /// Tonight's Preview goes through a live TS API path that can't be exercised from tests,
    /// so coverage there is limited to the shared gating logic (ShowMinAltitude flag).
    /// </summary>
    public class ReportGeneratorMultiTargetAltitudeTests {

        private readonly ReportGenerator _gen;

        public ReportGeneratorMultiTargetAltitudeTests() {
            _gen = new ReportGenerator();
            // Baseline: enable the Session Timeline altitude chart path and disable
            // unrelated heavy sections to keep test output tight.
            SettingsManager.Instance.Current.ReportLightMode        = false;
            SettingsManager.Instance.Current.ReportDetailLevel      = 2;
            SettingsManager.Instance.Current.ShowHFRGraph           = false;
            SettingsManager.Instance.Current.ShowStarCountCV        = false;
            SettingsManager.Instance.Current.ShowPerTargetIQ        = false;
            SettingsManager.Instance.Current.ShowSessionHistory     = false;
            SettingsManager.Instance.Current.ShowSkyThumbnails      = false;
            SettingsManager.Instance.Current.ShowAltitudeChart      = false;
            SettingsManager.Instance.Current.ShowNextNightPreview   = false;
            SettingsManager.Instance.Current.ShowTSProgressBars     = false;
            SettingsManager.Instance.Current.TimelineAltitudeDefault = true;
            SettingsManager.Instance.Current.ShowMinAltitude        = true;
            SettingsManager.Instance.Current.AdditionalChartConfigs = "";
            SettingsManager.Instance.Current.ExpandSectionsDefault  = false;
        }

        /// <summary>
        /// Builds a ReportData with valid observer coords and images spanning a usable
        /// time window, so BuildSessionAltitudeChart has enough data to render segments.
        /// </summary>
        private static ReportData MakeAltitudeChartData(List<TsTargetData> tsData) {
            var data = TestDataFactory.MakeReportData(
                imageCount: 10, observerLat: 40.7128, observerLon: -74.0060);
            var baseTime = new DateTime(2025, 1, 15, 22, 0, 0);
            for (int i = 0; i < data.Images.Count; i++) {
                data.Images[i].RaHours    = 5.5833;   // Orion Nebula
                data.Images[i].DecDegrees = -5.3911;
                // 10-minute intervals keep all frames in one merged block (15-min gap-merge threshold)
                data.Images[i].Timestamp  = baseTime.AddMinutes(i * 10);
            }
            // Override session window to match image spread
            data.Session.SessionStart = baseTime;
            data.Session.SessionEnd   = baseTime.AddMinutes(10 * 10 + 10);
            // TsData is init-only on ReportData — mutate the existing list
            data.TsData.Clear();
            data.TsData.AddRange(tsData);
            return data;
        }

        // ── Min altitude line rendering ────────────────────────────────────────

        [Fact]
        public async Task SessionAltChart_WithTsMinAlt_RendersMinAltLine() {
            var tsData = new List<TsTargetData> {
                new TsTargetData { TargetName = "M31", RA = 5.5833, Dec = -5.3911, MinimumAltitude = 30 }
            };
            var data = MakeAltitudeChartData(tsData);
            var html = await _gen.GenerateHtmlReport(data);
            // The multi-target chart tags the line with class="min-alt-line"
            Assert.Contains("class='min-alt-line'", html);
        }

        [Fact]
        public async Task SessionAltChart_WithTsMinAlt_RendersMinAltLabel() {
            var tsData = new List<TsTargetData> {
                new TsTargetData { TargetName = "M31", RA = 5.5833, Dec = -5.3911, MinimumAltitude = 30 }
            };
            var data = MakeAltitudeChartData(tsData);
            var html = await _gen.GenerateHtmlReport(data);
            // Label text is "Min <deg>°" (see BuildSessionAltitudeChart)
            Assert.Contains("Min 30°", html);
        }

        [Fact]
        public async Task SessionAltChart_NoTsData_NoMinAltLine() {
            // Empty TsData = no TS project info = no min alt known
            var data = MakeAltitudeChartData(new List<TsTargetData>());
            var html = await _gen.GenerateHtmlReport(data);
            Assert.DoesNotContain("min-alt-line", html);
            Assert.DoesNotContain("Min 30°", html);
        }

        [Fact]
        public async Task SessionAltChart_TsMinAltZero_NoMinAltLine() {
            // MinimumAltitude = 0 means "not set" in TS — should be skipped
            var tsData = new List<TsTargetData> {
                new TsTargetData { TargetName = "M31", RA = 5.5833, Dec = -5.3911, MinimumAltitude = 0 }
            };
            var data = MakeAltitudeChartData(tsData);
            var html = await _gen.GenerateHtmlReport(data);
            Assert.DoesNotContain("min-alt-line", html);
        }

        [Fact]
        public async Task SessionAltChart_ShowMinAltitudeDisabled_NoMinAltLine() {
            SettingsManager.Instance.Current.ShowMinAltitude = false;
            var tsData = new List<TsTargetData> {
                new TsTargetData { TargetName = "M31", RA = 5.5833, Dec = -5.3911, MinimumAltitude = 30 }
            };
            var data = MakeAltitudeChartData(tsData);
            var html = await _gen.GenerateHtmlReport(data);
            Assert.DoesNotContain("min-alt-line", html);
            Assert.DoesNotContain("Min 30°", html);
        }

        [Fact]
        public async Task SessionAltChart_MultiTargetWithDifferentMinAlt_RendersBothLabels() {
            // Two targets with different min altitudes — both lines + labels should render
            var tsData = new List<TsTargetData> {
                new TsTargetData { TargetName = "M31", RA = 5.5833, Dec = -5.3911, MinimumAltitude = 30 },
                new TsTargetData { TargetName = "M42", RA = 5.5833, Dec = -5.3911, MinimumAltitude = 45 }
            };
            var data = TestDataFactory.MakeReportData(
                imageCount: 10, targets: new[] { "M31", "M42" },
                observerLat: 40.7128, observerLon: -74.0060);
            var baseTime = new DateTime(2025, 1, 15, 22, 0, 0);
            // First 5 images M31, last 5 M42 — factory already alternates per target group
            for (int i = 0; i < data.Images.Count; i++) {
                data.Images[i].RaHours    = 5.5833;
                data.Images[i].DecDegrees = -5.3911;
                // 10-minute intervals keep all frames in one merged block (15-min gap-merge threshold)
                data.Images[i].Timestamp  = baseTime.AddMinutes(i * 10);
            }
            data.Session.SessionStart = baseTime;
            data.Session.SessionEnd   = baseTime.AddMinutes(10 * 10 + 10);
            data.TsData.Clear();
            data.TsData.AddRange(tsData);

            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("Min 30°", html);
            Assert.Contains("Min 45°", html);
        }

        [Fact]
        public async Task SessionAltChart_TsMinAlt90OrAbove_NoMinAltLine() {
            // Sanity check: min-alt lines must fall within the 0-90 plot range
            var tsData = new List<TsTargetData> {
                new TsTargetData { TargetName = "M31", RA = 5.5833, Dec = -5.3911, MinimumAltitude = 95 }
            };
            var data = MakeAltitudeChartData(tsData);
            var html = await _gen.GenerateHtmlReport(data);
            Assert.DoesNotContain("min-alt-line", html);
        }
    }
}
