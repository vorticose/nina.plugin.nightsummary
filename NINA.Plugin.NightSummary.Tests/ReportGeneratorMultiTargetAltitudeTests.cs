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
            SettingsManager.Instance.Current.TimelineShowMinAltitude = true;
            SettingsManager.Instance.Current.PreviewShowMinAltitude  = true;
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
            SettingsManager.Instance.Current.TimelineShowMinAltitude = false;
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

        [Fact]
        public async Task SessionAltChart_MinAltLine_HasDistinguishableStylingFromShading() {
            // Shading keeps the target-color tint (fill-opacity='0.10') so each block is
            // still visually attributable to its target. The min-alt line is rendered in
            // the single-target chart's red (#cc4444) for consistent meaning across charts.
            var tsData = new List<TsTargetData> {
                new TsTargetData { TargetName = "M31", RA = 5.5833, Dec = -5.3911, MinimumAltitude = 30 }
            };
            var data = MakeAltitudeChartData(tsData);
            var html = await _gen.GenerateHtmlReport(data);
            // Shading tint — faint fill-opacity in a target-colored band
            Assert.Contains("fill-opacity='0.10'", html);
            // Min-alt line — red, full-opacity dashed stroke, tagged with min-alt-line class
            Assert.Matches(
                @"<line[^>]*stroke='#cc4444'[^>]*stroke-dasharray='5,4'[^>]*opacity='1'[^>]*class='min-alt-line'",
                html);
        }

        // ── Shared min-alt label dedupe ────────────────────────────────────────

        [Fact]
        public async Task SessionAltChart_AllTargetsShareMinAlt_RendersSingleLabel() {
            // Two targets with the same min-altitude → only one "Min N°" label.
            var tsData = new List<TsTargetData> {
                new TsTargetData { TargetName = "M31", RA = 5.5833, Dec = -5.3911, MinimumAltitude = 30 },
                new TsTargetData { TargetName = "M42", RA = 5.5833, Dec = -5.3911, MinimumAltitude = 30 }
            };
            var data = TestDataFactory.MakeReportData(
                imageCount: 10, targets: new[] { "M31", "M42" },
                observerLat: 40.7128, observerLon: -74.0060);
            var baseTime = new DateTime(2025, 1, 15, 22, 0, 0);
            for (int i = 0; i < data.Images.Count; i++) {
                data.Images[i].RaHours    = 5.5833;
                data.Images[i].DecDegrees = -5.3911;
                data.Images[i].Timestamp  = baseTime.AddMinutes(i * 10);
            }
            data.Session.SessionStart = baseTime;
            data.Session.SessionEnd   = baseTime.AddMinutes(10 * 10 + 10);
            data.TsData.Clear();
            data.TsData.AddRange(tsData);

            var html = await _gen.GenerateHtmlReport(data);
            int labelCount = System.Text.RegularExpressions.Regex
                .Matches(html, "class='min-alt-label'").Count;
            Assert.Equal(1, labelCount);
            // And the single label is the shared value
            Assert.Contains("Min 30°", html);
        }

        [Fact]
        public async Task SessionAltChart_TargetsDifferMinAlt_RendersMultipleLabels() {
            // Two targets with different min-altitudes → one label per block.
            var tsData = new List<TsTargetData> {
                new TsTargetData { TargetName = "M31", RA = 5.5833, Dec = -5.3911, MinimumAltitude = 30 },
                new TsTargetData { TargetName = "M42", RA = 5.5833, Dec = -5.3911, MinimumAltitude = 45 }
            };
            var data = TestDataFactory.MakeReportData(
                imageCount: 10, targets: new[] { "M31", "M42" },
                observerLat: 40.7128, observerLon: -74.0060);
            var baseTime = new DateTime(2025, 1, 15, 22, 0, 0);
            for (int i = 0; i < data.Images.Count; i++) {
                data.Images[i].RaHours    = 5.5833;
                data.Images[i].DecDegrees = -5.3911;
                data.Images[i].Timestamp  = baseTime.AddMinutes(i * 10);
            }
            data.Session.SessionStart = baseTime;
            data.Session.SessionEnd   = baseTime.AddMinutes(10 * 10 + 10);
            data.TsData.Clear();
            data.TsData.AddRange(tsData);

            var html = await _gen.GenerateHtmlReport(data);
            int labelCount = System.Text.RegularExpressions.Regex
                .Matches(html, "class='min-alt-label'").Count;
            Assert.True(labelCount >= 2,
                $"Expected ≥2 min-alt labels when targets have different min alts, got {labelCount}");
        }

        // ── Preview altitude chart (reflection-invoked because method is private) ─────

        [Fact]
        public void PreviewAltChart_AllTargetsShareMinAlt_RendersSingleLabel() {
            var html = InvokePreviewAltitudeChart(
                new[] {
                    ("M31", 30.0),
                    ("M42", 30.0)
                });
            int labelCount = System.Text.RegularExpressions.Regex
                .Matches(html, "class='min-alt-label'").Count;
            Assert.Equal(1, labelCount);
            Assert.Contains("Min 30°", html);
            // Red color used, matching the single-target chart
            Assert.Contains("stroke='#cc4444'", html);
        }

        [Fact]
        public void PreviewAltChart_TargetsDifferMinAlt_RendersMultipleLabels() {
            var html = InvokePreviewAltitudeChart(
                new[] {
                    ("M31", 30.0),
                    ("M42", 45.0)
                });
            int labelCount = System.Text.RegularExpressions.Regex
                .Matches(html, "class='min-alt-label'").Count;
            Assert.True(labelCount >= 2,
                $"Expected ≥2 min-alt labels when targets have different min alts, got {labelCount}");
        }

        // ── Label position flips to avoid the altitude curve ───────────────────

        // ── Label position flips to avoid the altitude curve ───────────────────

        [Fact]
        public void PreviewAltChart_CurveAboveLineWithRoomBelow_LabelRendersBelow() {
            // Typical imaging scenario. Polaris-like target (dec ≈ lat) stays
            // near the observer's latitude altitude (~41°) all night, so the
            // curve sits above the min-alt=25° line at the right edge. The
            // open sky below the line gives more clearance than the gap
            // between the line and the curve, so the label should render
            // BELOW the line.
            var html = InvokePreviewAltitudeChart(
                new[] { ("PolarisLike", 25.0) },
                raHours: 2.53, decDegrees: 89.26);
            var labelY = ExtractLabelY(html);
            var lineY  = ExtractLineY(html);
            Assert.True(labelY > lineY,
                $"Expected label below min-alt line (labelY={labelY}, lineY={lineY})");
        }

        [Fact]
        public void PreviewAltChart_SharedLabelIgnoresOtherTargetCurves_LabelStaysBelow() {
            // Regression: the shared-label placement must only consider the label's
            // own target (the rightmost block) — not every visible target curve. A
            // low-altitude neighbor passing through the label zone must NOT flip the
            // label away from its optimal side.
            //
            // Setup:
            //  - Target A (rightmost): Polaris-like (dec≈lat), alt≈41° all night.
            //    Min-alt=25° → line well below curve, "below the line" is open sky
            //    → correct placement is BELOW the line.
            //  - Target B (earlier block, same min-alt=25° so shared-label path fires):
            //    southern target (dec=-60°) stays very low from lat 40.7. Its curve
            //    at the right edge would otherwise intrude on the label's below-line
            //    clearance. With the new behavior this curve is ignored.
            var nightStart = new DateTime(2025, 1, 15, 22, 0, 0);
            var nightEnd   = nightStart.AddHours(3);

            var imagingBlocks = new List<TsPreviewEntry> {
                // Block B runs first (earlier EndTime) — NOT the label's owner
                new TsPreviewEntry {
                    Name      = "SouthernB",
                    StartTime = nightStart,
                    EndTime   = nightStart.AddHours(2)
                },
                // Block A is rightmost (latest EndTime) — owns the shared label
                new TsPreviewEntry {
                    Name      = "PolarisA",
                    StartTime = nightStart.AddHours(1),
                    EndTime   = nightStart.AddHours(3)
                }
            };
            var colorMap = new Dictionary<string, string> {
                { "SouthernB", "#92b4f4" },
                { "PolarisA",  "#ffd78b" }
            };
            var coordLookup = new Dictionary<string, (double Ra, double Dec)> {
                { "SouthernB", (5.5833, -60.0) },   // low from lat 40.7
                { "PolarisA",  (2.53,   89.26) }    // near-zenith pole, alt≈lat
            };
            var minAltLookup = new Dictionary<string, double> {
                { "SouthernB", 25.0 },
                { "PolarisA",  25.0 }
            };

            var method = typeof(ReportGenerator).GetMethod(
                "BuildPreviewAltitudeChart",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method);
            var html = (string)method.Invoke(_gen, new object[] {
                imagingBlocks, colorMap, coordLookup, minAltLookup,
                40.7128, -74.0060, nightStart, nightEnd
            });

            // Exactly one label (shared path), and it must sit BELOW the line.
            int labelCount = System.Text.RegularExpressions.Regex
                .Matches(html, "class='min-alt-label'").Count;
            Assert.Equal(1, labelCount);
            var labelY = ExtractLabelY(html);
            var lineY  = ExtractLineY(html);
            Assert.True(labelY > lineY,
                $"Shared label should stay below line when rightmost target's curve is above it, " +
                $"regardless of other targets' curve positions (labelY={labelY}, lineY={lineY})");
        }

        [Fact]
        public void PreviewAltChart_MinAltLabelRendersAfterTargetCurves() {
            // Regression: SVG paints in document order. If min-alt labels emit before
            // the per-target altitude polylines, unrelated curves visually paint over
            // the label text. Assert the label <text> appears AFTER the last target
            // curve polyline in the output.
            var html = InvokePreviewAltitudeChart(
                new[] { ("PolarisLike", 25.0) },
                raHours: 2.53, decDegrees: 89.26);
            int labelIdx = html.IndexOf("class='min-alt-label'",
                StringComparison.Ordinal);
            int lastCurveIdx = html.LastIndexOf("stroke-width='2'",
                StringComparison.Ordinal);
            Assert.True(labelIdx > 0, "min-alt label not present");
            Assert.True(lastCurveIdx > 0, "target curve polyline not present");
            Assert.True(labelIdx > lastCurveIdx,
                $"Expected min-alt label to emit after target curves " +
                $"(labelIdx={labelIdx}, lastCurveIdx={lastCurveIdx})");
        }

        [Fact]
        public void PreviewAltChart_CurveBelowLineNearTop_LabelRendersAbove() {
            // Inverse scenario: Polaris-like target stays near lat altitude
            // (~41°), and min-alt=50° puts the dashed line well above the
            // curve. The curve now blocks most of the space BELOW the line,
            // while there's plenty of empty sky between the line and the top
            // of the plot. Expected: label flips ABOVE the line.
            var html = InvokePreviewAltitudeChart(
                new[] { ("PolarisLike", 50.0) },
                raHours: 2.53, decDegrees: 89.26);
            var labelY = ExtractLabelY(html);
            var lineY  = ExtractLineY(html);
            Assert.True(labelY < lineY,
                $"Expected label above min-alt line (labelY={labelY}, lineY={lineY})");
        }

        private static double ExtractLabelY(string html) {
            // Match only a single <text ...> element (no '<' between y and class)
            var m = System.Text.RegularExpressions.Regex.Match(
                html, @"<text\s+x='[^']+'\s+y='(?<y>[\d.]+)'[^<>]*class='min-alt-label'");
            Assert.True(m.Success, "min-alt-label <text> element not found");
            return double.Parse(m.Groups["y"].Value,
                System.Globalization.CultureInfo.InvariantCulture);
        }

        private static double ExtractLineY(string html) {
            var m = System.Text.RegularExpressions.Regex.Match(
                html, @"<line\s+x1='[^']+'\s+y1='(?<y>[\d.]+)'[^<>]*class='min-alt-line'");
            Assert.True(m.Success, "min-alt-line element not found");
            return double.Parse(m.Groups["y"].Value,
                System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Invokes the private BuildPreviewAltitudeChart with synthesized TS preview entries.
        /// Each target gets one 60-minute block; all targets share the same RA/Dec so the
        /// altitude calculator produces a clean curve for chart dimensions.
        /// </summary>
        private string InvokePreviewAltitudeChart(IEnumerable<(string Name, double MinAlt)> targets) {
            return InvokePreviewAltitudeChart(targets, raHours: 5.5833, decDegrees: -5.3911);
        }

        private string InvokePreviewAltitudeChart(
            IEnumerable<(string Name, double MinAlt)> targets,
            double raHours, double decDegrees) {
            var targetList = targets.ToList();
            var nightStart = new DateTime(2025, 1, 15, 22, 0, 0);
            var nightEnd   = nightStart.AddHours(targetList.Count + 1);

            var imagingBlocks = new List<TsPreviewEntry>();
            var colorMap      = new Dictionary<string, string>();
            var coordLookup   = new Dictionary<string, (double Ra, double Dec)>();
            var minAltLookup  = new Dictionary<string, double>();
            string[] palette  = { "#ffd78b", "#92b4f4", "#c3f584", "#ff9b99", "#f0a0ff" };

            for (int i = 0; i < targetList.Count; i++) {
                var (name, minAlt) = targetList[i];
                imagingBlocks.Add(new TsPreviewEntry {
                    Name      = name,
                    StartTime = nightStart.AddHours(i),
                    EndTime   = nightStart.AddHours(i + 1)
                });
                colorMap[name]     = palette[i % palette.Length];
                coordLookup[name]  = (raHours, decDegrees);
                minAltLookup[name] = minAlt;
            }

            var method = typeof(ReportGenerator).GetMethod(
                "BuildPreviewAltitudeChart",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method);

            var result = method.Invoke(_gen, new object[] {
                imagingBlocks,
                colorMap,
                coordLookup,
                minAltLookup,
                40.7128, -74.0060,
                nightStart, nightEnd
            });
            return (string)result;
        }
    }
}
