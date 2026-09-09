using NINA.Plugin.NightSummary.Server;
using System.Text.RegularExpressions;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    /// <summary>
    /// Dashboard session-card altitude charts used to scrape only the first
    /// imaging-window rect and the first polyline from each per-target chart.
    /// A roof close that split a target into two windows (gap &gt; 15 min) then
    /// dropped the post-reopen band on the Sessions list while the session
    /// page timeline still showed it. The card look (sunset-sunrise axis, no
    /// hatch, no event markers) must stay the same.
    /// </summary>
    public class AltitudeChartComposerTests {

        private static int CountOpacity15Rects(string svg) =>
            Regex.Matches(svg, @"opacity='0\.15'").Count;

        private static string PerTargetHtml(string innerSvg, string targetName = "LBN 437") =>
            "<html><body>" +
            "<div class='target-section'>" +
            $"<h3>{targetName} <span>Start: 21:00</span></h3>" +
            innerSvg +
            "</div>" +
            "</body></html>";

        private static string TwoWindowChart(string extraPolylines = "") =>
            "<svg class='altitude-chart' viewBox='0 0 500 248'>" +
            "<rect x='38' y='20' width='452' height='200' fill='#0d1117' rx='4'/>" +
            "<rect x='50' y='20' width='80' height='200' fill='#7eb8f7' opacity='0.07'/>" +
            "<rect x='200' y='20' width='90' height='200' fill='#7eb8f7' opacity='0.07'/>" +
            "<polyline points='38,100 490,80' fill='none' stroke='#7eb8f7' stroke-width='2'/>" +
            extraPolylines +
            "<text x='38' y='238' fill='#888'>19:55</text>" +
            "<text x='490' y='238' fill='#888'>06:00</text>" +
            "</svg>";

        [Fact]
        public void EmptyHtml_ReturnsEmptySvg() {
            var result = AltitudeChartComposer.FromReportHtml("", "sess-1");
            Assert.Equal("", result.Svg);
            Assert.Empty(result.Legend);
        }

        [Fact]
        public void TwoWindows_KeepsBothRects() {
            var result = AltitudeChartComposer.FromReportHtml(PerTargetHtml(TwoWindowChart()), "sess-roof");

            Assert.Equal(2, CountOpacity15Rects(result.Svg));
            Assert.Contains("<title>LBN 437</title>", result.Svg);
            Assert.Single(result.Legend);
            Assert.Equal("LBN 437", result.Legend[0].Name);
        }

        [Fact]
        public void TwoWindows_KeepsCardLook_NoHatchOrEventMarkers() {
            var html =
                "<div class=\"ns-chart-svg\" id=\"nsc0-svg-altitude\">" +
                "<svg viewBox='0 0 760 248'>" +
                "<defs><pattern id='ns-idle-alt'></pattern></defs>" +
                "<rect x='38' y='20' width='50' height='200' fill='#4e79a7' opacity='0.15'/>" +
                "<text x='100' y='16' fill='#a78bfa'>AF</text>" +
                "</svg></div>" +
                PerTargetHtml(TwoWindowChart());

            var result = AltitudeChartComposer.FromReportHtml(html, "sess-look");

            Assert.DoesNotContain("ns-idle-alt", result.Svg);
            Assert.DoesNotContain(">AF<", result.Svg);
            Assert.Contains("preserveAspectRatio='none'", result.Svg);
            Assert.Equal(2, CountOpacity15Rects(result.Svg));
        }

        [Fact]
        public void SegmentedPolyline_KeepsEverySegment() {
            var extra = "<polyline points='200,80 490,70' fill='none' stroke='#7eb8f7' stroke-width='2'/>";
            var inner =
                "<svg class='altitude-chart' viewBox='0 0 500 248'>" +
                "<rect x='38' y='20' width='452' height='200' fill='#0d1117' rx='4'/>" +
                "<rect x='50' y='20' width='80' height='200' fill='#7eb8f7' opacity='0.07'/>" +
                "<polyline points='38,100 100,90' fill='none' stroke='#7eb8f7' stroke-width='2'/>" +
                extra +
                "</svg>";

            var result = AltitudeChartComposer.FromReportHtml(PerTargetHtml(inner), "sess-seg");

            var colored = Regex.Matches(result.Svg, @"stroke='#4e79a7' stroke-width='2'");
            Assert.Equal(2, colored.Count);
        }

        [Fact]
        public void SingleWindow_Unchanged() {
            var inner =
                "<svg class='altitude-chart' viewBox='0 0 500 248'>" +
                "<rect x='38' y='20' width='452' height='200' fill='#0d1117' rx='4'/>" +
                "<rect x='80' y='20' width='120' height='200' fill='#7eb8f7' opacity='0.07'/>" +
                "<polyline points='38,100 490,80' fill='none' stroke='#7eb8f7' stroke-width='2'/>" +
                "</svg>";

            var result = AltitudeChartComposer.FromReportHtml(PerTargetHtml(inner, "M31"), "sess-one");

            Assert.Equal(1, CountOpacity15Rects(result.Svg));
            Assert.Contains("<title>M31</title>", result.Svg);
            Assert.Single(result.Legend);
            Assert.Equal("M31", result.Legend[0].Name);
        }

        [Fact]
        public void TwoTargets_EachKeepsItsWindows() {
            var html =
                "<div class='target-section'><h3>First</h3>" +
                "<svg class='altitude-chart' viewBox='0 0 500 248'>" +
                "<rect x='38' y='20' width='452' height='200' fill='#0d1117' rx='4'/>" +
                "<rect x='40' y='20' width='60' height='200' fill='#7eb8f7' opacity='0.07'/>" +
                "<polyline points='38,100 200,90' fill='none' stroke='#7eb8f7' stroke-width='2'/>" +
                "</svg></div>" +
                "<div class='target-section'><h3>LBN 437</h3>" +
                "<svg class='altitude-chart' viewBox='0 0 500 248'>" +
                "<rect x='38' y='20' width='452' height='200' fill='#0d1117' rx='4'/>" +
                "<rect x='120' y='20' width='50' height='200' fill='#7eb8f7' opacity='0.07'/>" +
                "<rect x='250' y='20' width='70' height='200' fill='#7eb8f7' opacity='0.07'/>" +
                "<polyline points='38,80 490,70' fill='none' stroke='#7eb8f7' stroke-width='2'/>" +
                "</svg></div>";

            var result = AltitudeChartComposer.FromReportHtml(html, "sess-multi");

            Assert.Equal(3, CountOpacity15Rects(result.Svg));
            Assert.Equal(2, result.Legend.Count);
            Assert.Equal("First", result.Legend[0].Name);
            Assert.Equal("LBN 437", result.Legend[1].Name);
            Assert.Equal("#f28e2b", result.Legend[1].Color);
        }

        [Fact]
        public void CacheVersion_RejectsLegacyJson() {
            Assert.False(AltitudeChartComposer.IsCurrentCache("{\"svg\":\"<svg/>\",\"legend\":[]}"));
            Assert.False(AltitudeChartComposer.IsCurrentCache("{\"svg\":\"<svg/>\",\"legend\":[],\"v\":1}"));
            Assert.False(AltitudeChartComposer.IsCurrentCache("{\"svg\":\"<svg/>\",\"legend\":[],\"v\":2}"));
            Assert.True(AltitudeChartComposer.IsCurrentCache("{\"svg\":\"<svg/>\",\"legend\":[],\"v\":3}"));
            Assert.False(AltitudeChartComposer.IsCurrentCache(""));
            Assert.False(AltitudeChartComposer.IsCurrentCache("not json"));
        }
    }
}
