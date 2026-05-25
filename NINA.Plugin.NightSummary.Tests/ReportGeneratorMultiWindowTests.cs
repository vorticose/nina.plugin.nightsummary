using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    /// <summary>
    /// Verifies the per-target multi-window behavior in BuildTargetSection:
    /// — H3 subtitle lists each window when count > 1
    /// — BuildAltitudeChart emits one highlight rect + one start/end line pair per window
    /// — Filter table splits per window plus a Grand Total table
    /// — Single-window targets render exactly as before (regression guard)
    /// </summary>
    public class ReportGeneratorMultiWindowTests {

        private readonly ReportGenerator _gen;

        public ReportGeneratorMultiWindowTests() {
            _gen = TestDeps.NewReportGenerator();
            SettingsManager.Instance.Current.ReportLightMode        = false;
            SettingsManager.Instance.Current.ReportDetailLevel      = 1;
            SettingsManager.Instance.Current.ShowHFRGraph           = false;
            SettingsManager.Instance.Current.ShowStarCountCV        = false;
            SettingsManager.Instance.Current.ShowPerTargetIQ        = false;
            SettingsManager.Instance.Current.ShowSessionHistory     = false;
            SettingsManager.Instance.Current.ShowSkyThumbnails      = false;
            SettingsManager.Instance.Current.ShowAltitudeChart      = true;
            SettingsManager.Instance.Current.ShowMinAltitude        = false;
            SettingsManager.Instance.Current.ShowMoonCurve          = false;
            SettingsManager.Instance.Current.ShowNextNightPreview   = false;
            SettingsManager.Instance.Current.ShowTSProgressBars     = false;
            SettingsManager.Instance.Current.AdditionalChartConfigs = "";
            SettingsManager.Instance.Current.ExpandSectionsDefault  = false;
        }

        /// <summary>
        /// Builds a ReportData where the single target was imaged in <paramref name="windowCount"/>
        /// non-continuous windows separated by 60-minute gaps (well above the 15-min merge
        /// threshold). Each window contains 3 frames of 300s exposure.
        /// </summary>
        private static ReportData MultiWindowData(int windowCount, double ra = 5.5833, double dec = -5.3911) {
            var data = TestDataFactory.MakeReportData(imageCount: 3, observerLat: 40.7128, observerLon: -74.0060);
            data.Images.Clear();

            var sessionId = data.Session.SessionId;
            var t0        = new DateTime(2025, 1, 15, 22, 0, 0);

            for (int w = 0; w < windowCount; w++) {
                // 60 min gap between window starts → 60 - 5 (exposure) = 55 min gap > 15 min threshold
                var winStart = t0.AddMinutes(w * 60);
                for (int i = 0; i < 3; i++) {
                    var img = TestDataFactory.MakeImage(sessionId,
                        timestamp: winStart.AddMinutes(i * 5),
                        raHours:   ra,
                        decDeg:    dec);
                    data.Images.Add(img);
                }
            }
            return data;
        }

        // ── H3 subtitle ──────────────────────────────────────────────────────

        [Fact]
        public async Task SingleWindow_SubtitleSaysStartEnd() {
            var data = MultiWindowData(windowCount: 1);
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("Start:", html);
            Assert.DoesNotContain("windows:", html);
        }

        [Fact]
        public async Task TwoWindows_SubtitleListsBothWindows() {
            var data = MultiWindowData(windowCount: 2);
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("2 windows:", html);
        }

        [Fact]
        public async Task ThreeWindows_SubtitleSaysThreeWindows() {
            var data = MultiWindowData(windowCount: 3);
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("3 windows:", html);
        }

        // ── Altitude chart highlights ────────────────────────────────────────

        // Highlight rect is the only one in BuildAltitudeChart that uses opacity='0.07'.
        // Counting those occurrences gives us a stable indicator of per-window rects.
        private static int CountAltitudeWindowRects(string html) {
            return Regex.Matches(html, @"opacity='0\.07'").Count;
        }

        [Fact]
        public async Task SingleWindow_OneAltitudeHighlight() {
            var data = MultiWindowData(windowCount: 1);
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Equal(1, CountAltitudeWindowRects(html));
        }

        [Fact]
        public async Task TwoWindows_TwoAltitudeHighlights() {
            var data = MultiWindowData(windowCount: 2);
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Equal(2, CountAltitudeWindowRects(html));
        }

        [Fact]
        public async Task ThreeWindows_ThreeAltitudeHighlights() {
            var data = MultiWindowData(windowCount: 3);
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Equal(3, CountAltitudeWindowRects(html));
        }

        // ── Filter table split ───────────────────────────────────────────────

        [Fact]
        public async Task SingleWindow_NoWindowCaptions() {
            var data = MultiWindowData(windowCount: 1);
            var html = await _gen.GenerateHtmlReport(data);
            Assert.DoesNotContain("<strong>Window 1</strong>", html);
            Assert.DoesNotContain("Grand Total", html);
        }

        [Fact]
        public async Task TwoWindows_FilterTableHasWindowExpandersAndGrandTotal() {
            var data = MultiWindowData(windowCount: 2);
            var html = await _gen.GenerateHtmlReport(data);
            Assert.Contains("<strong>Window 1</strong>", html);
            Assert.Contains("<strong>Window 2</strong>", html);
            Assert.Contains("Grand Total", html);
        }

        [Fact]
        public async Task TwoWindows_PerWindowSectionsAreCollapsibleAndClosedByDefault() {
            var data = MultiWindowData(windowCount: 2);
            var html = await _gen.GenerateHtmlReport(data);
            // Each window emits a <details class='window-section'> block — closed by default
            // (no `open` attribute). Count matches the window count.
            var openings = Regex.Matches(html, @"<details class='window-section'>").Count;
            Assert.Equal(2, openings);
            // None of the window expanders should be pre-opened.
            Assert.DoesNotContain("<details class='window-section' open", html);
        }

        [Fact]
        public async Task TwoWindows_FilterTableHasPerWindowTotalLabel() {
            var data = MultiWindowData(windowCount: 2);
            var html = await _gen.GenerateHtmlReport(data);
            // Each sub-table closes with a "Window Total" row.
            var matches = Regex.Matches(html, "Window Total").Count;
            Assert.Equal(2, matches);
        }

        [Fact]
        public async Task TwoWindows_GrandTotalAppearsBeforeFirstWindowExpander() {
            var data = MultiWindowData(windowCount: 2);
            var html = await _gen.GenerateHtmlReport(data);
            var grandTotalIdx = html.IndexOf("Grand Total", StringComparison.Ordinal);
            var firstWindowIdx = html.IndexOf("<details class='window-section'>", StringComparison.Ordinal);
            Assert.True(grandTotalIdx > 0,  "Grand Total row missing");
            Assert.True(firstWindowIdx > 0, "Window expander missing");
            Assert.True(grandTotalIdx < firstWindowIdx,
                "Grand Total row should render before the first window expander");
        }

        // ── Single-window regression guard ──────────────────────────────────

        [Fact]
        public async Task SingleWindow_StartLabelStillPresent() {
            var data = MultiWindowData(windowCount: 1);
            var html = await _gen.GenerateHtmlReport(data);
            // Legacy single-window mode keeps text "Start" / "End" labels above the lines.
            Assert.Contains(">Start<", html);
            Assert.Contains(">End<", html);
        }

        [Fact]
        public async Task MultiWindow_DropsTextLabelsToAvoidOverlap() {
            var data = MultiWindowData(windowCount: 2);
            var html = await _gen.GenerateHtmlReport(data);
            // Multi-window mode hides the visible "Start" / "End" labels (tooltips remain).
            // The subtitle "Start:" prefix still appears in single-window; in multi-window
            // the subtitle becomes "N windows:" — assert text labels above lines are gone.
            // <text ...>Start</text> would only appear from the legacy per-window labels.
            Assert.DoesNotContain(">Start</text>", html);
            Assert.DoesNotContain(">End</text>",   html);
        }

        // ── Image Quality section split ─────────────────────────────────────

        [Fact]
        public async Task TwoWindows_ImageQualitySplitMatchesFilterTablePattern() {
            // IQ section requires multiTarget=true to render. Build two targets, each with
            // 2 windows, and enable ShowPerTargetIQ.
            SettingsManager.Instance.Current.ShowPerTargetIQ = true;

            var data = TestDataFactory.MakeReportData(imageCount: 3, observerLat: 40.7128, observerLon: -74.0060);
            data.Images.Clear();
            var sessionId = data.Session.SessionId;
            var t0 = new DateTime(2025, 1, 15, 22, 0, 0);
            // Target A: 2 windows 60min apart
            // Target B: 2 windows starting later
            foreach (var (name, baseT) in new[] { ("M31", t0), ("M42", t0.AddMinutes(120)) }) {
                for (int w = 0; w < 2; w++) {
                    var winStart = baseT.AddMinutes(w * 60);
                    for (int i = 0; i < 3; i++) {
                        var img = TestDataFactory.MakeImage(sessionId,
                            target: name,
                            timestamp: winStart.AddMinutes(i * 5),
                            raHours: 5.5833, decDeg: -5.3911);
                        data.Images.Add(img);
                    }
                }
            }
            var html = await _gen.GenerateHtmlReport(data);

            // IQ section emitted (per-target IQ requires multi-target)
            Assert.Contains("<details class='iq-section'", html);
            Assert.Contains("Image Quality", html);

            // Each target has 2 IQ windows = 4 .window-section expanders total across the
            // two IQ blocks. PLUS 4 more from the filter-table splits (one per window per
            // target). Total expanders = 8.
            var totalExpanders = Regex.Matches(html, @"<details class='window-section'>").Count;
            Assert.Equal(8, totalExpanders);
        }

        // ── Window count == 0 guard ─────────────────────────────────────────

        [Fact]
        public async Task NoImages_RendersNoTargetSection() {
            var data = MultiWindowData(windowCount: 0);
            data.Images.Clear();
            var html = await _gen.GenerateHtmlReport(data);
            // No images = nothing to render under Targets Imaged
            Assert.DoesNotContain("class='target-section'", html);
        }
    }
}
