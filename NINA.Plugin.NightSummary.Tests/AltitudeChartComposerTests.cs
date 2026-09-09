using NINA.Plugin.NightSummary.Server;
using System.Text.RegularExpressions;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    /// <summary>
    /// Dashboard session-card altitude charts used to scrape only the first
    /// imaging-window rect and the first polyline from each per-target chart.
    /// A roof close that split a target into two windows (gap &gt; 15 min) then
    /// dropped the post-reopen band on the Sessions list while the session
    /// page timeline still showed it.
    /// </summary>
    public class AltitudeChartComposerTests {

        private static int CountOpacity15Rects(string svg) =>
            Regex.Matches(svg, @"opacity='0\.15'").Count;

        private static string SessionTimelineHtml(string innerSvg, string extra = "") =>
            "<html><body>" +
            "<div class=\"ns-chart-svg\" id=\"nsc0-svg-altitude\">" +
            innerSvg +
            "</div>" +
            extra +
            "</body></html>";

        private static string PerTargetHtml(string innerSvg, string targetName = "LBN 437") =>
            "<html><body>" +
            "<div class='target-section'>" +
            $"<h3>{targetName} <span>Start: 21:00</span></h3>" +
            innerSvg +
            "</div>" +
            "</body></html>";

        [Fact]
        public void EmptyHtml_ReturnsEmptySvg() {
            var result = AltitudeChartComposer.FromReportHtml("", "sess-1");
            Assert.Equal("", result.Svg);
            Assert.Empty(result.Legend);
        }

        [Fact]
        public void SessionTimeline_TwoWindows_KeepsBothRects() {
            var svg =
                "<svg viewBox='0 0 760 248' xmlns='http://www.w3.org/2000/svg' style='width:100%;font-family:Arial,sans-serif;font-size:10px;'>" +
                "<defs><pattern id='ns-idle-alt' patternUnits='userSpaceOnUse' width='8' height='8'>" +
                "<rect width='8' height='8' fill='#0f0f23'/>" +
                "</pattern></defs>" +
                "<rect x='38' y='20' width='120' height='200' fill='#4e79a7' opacity='0.15'/>" +
                "<rect x='220' y='20' width='90' height='200' fill='#4e79a7' opacity='0.15'/>" +
                "<g><title>LBN 437</title>" +
                "<polyline points='38,100 400,80' fill='none' stroke='transparent' stroke-width='10'/>" +
                "<polyline points='38,100 400,80' fill='none' stroke='#4e79a7' stroke-width='2'/>" +
                "</g>" +
                "<g><title>Moon</title>" +
                "<polyline points='38,180 400,190' fill='none' stroke='#c0c0c0' stroke-width='1.5'/>" +
                "</g>" +
                "</svg>";

            var result = AltitudeChartComposer.FromReportHtml(SessionTimelineHtml(svg), "sess-roof");

            Assert.Equal(2, CountOpacity15Rects(result.Svg));
            Assert.Contains("x='38'", result.Svg);
            Assert.Contains("x='220'", result.Svg);
            Assert.Contains("<title>LBN 437</title>", result.Svg);
            Assert.Contains("<title>Moon Position</title>", result.Svg);
            Assert.DoesNotContain("<title>Moon</title>", result.Svg);
            Assert.Contains("preserveAspectRatio='none'", result.Svg);
            Assert.Contains("id='ns-idle-alt-sess-roof'", result.Svg);
            Assert.Single(result.Legend);
            Assert.Equal("LBN 437", result.Legend[0].Name);
            Assert.Equal("#4e79a7", result.Legend[0].Color);
        }

        [Fact]
        public void SessionTimeline_PreferredOverPerTargetCharts() {
            var sessionSvg =
                "<svg viewBox='0 0 760 248' xmlns='http://www.w3.org/2000/svg'>" +
                "<rect x='38' y='20' width='50' height='200' fill='#4e79a7' opacity='0.15'/>" +
                "<rect x='300' y='20' width='80' height='200' fill='#4e79a7' opacity='0.15'/>" +
                "<g><title>LBN 437</title>" +
                "<polyline points='10,10 20,20' fill='none' stroke='#4e79a7' stroke-width='2'/>" +
                "</g></svg>";
            var perTarget =
                "<div class='target-section'><h3>LBN 437</h3>" +
                "<svg class='altitude-chart' viewBox='0 0 500 248'>" +
                "<rect x='50' y='20' width='80' height='200' fill='#7eb8f7' opacity='0.07'/>" +
                "<polyline points='38,100 490,80' fill='none' stroke='#7eb8f7' stroke-width='2'/>" +
                "</svg></div>";

            var result = AltitudeChartComposer.FromReportHtml(
                SessionTimelineHtml(sessionSvg, perTarget), "sess-1");

            Assert.Equal(2, CountOpacity15Rects(result.Svg));
            Assert.Contains("x='300'", result.Svg);
            Assert.DoesNotContain("opacity='0.07'", result.Svg);
        }

        [Fact]
        public void Fallback_TwoWindows_KeepsBothRects() {
            var inner =
                "<svg class='altitude-chart' viewBox='0 0 500 248'>" +
                "<rect x='38' y='20' width='452' height='200' fill='#0d1117' rx='4'/>" +
                "<rect x='50' y='20' width='80' height='200' fill='#7eb8f7' opacity='0.07'/>" +
                "<rect x='200' y='20' width='90' height='200' fill='#7eb8f7' opacity='0.07'/>" +
                "<polyline points='38,100 490,80' fill='none' stroke='#7eb8f7' stroke-width='2'/>" +
                "<text x='38' y='238' fill='#888'>19:55</text>" +
                "<text x='490' y='238' fill='#888'>06:00</text>" +
                "</svg>";

            var result = AltitudeChartComposer.FromReportHtml(PerTargetHtml(inner), "sess-fallback");

            Assert.Equal(2, CountOpacity15Rects(result.Svg));
            Assert.Contains("<title>LBN 437</title>", result.Svg);
            Assert.DoesNotContain("ns-idle-alt", result.Svg);
        }

        [Fact]
        public void Fallback_SegmentedPolyline_KeepsEverySegment() {
            var inner =
                "<svg class='altitude-chart' viewBox='0 0 500 248'>" +
                "<rect x='38' y='20' width='452' height='200' fill='#0d1117' rx='4'/>" +
                "<rect x='50' y='20' width='80' height='200' fill='#7eb8f7' opacity='0.07'/>" +
                "<polyline points='38,100 100,90' fill='none' stroke='#7eb8f7' stroke-width='2'/>" +
                "<polyline points='200,80 490,70' fill='none' stroke='#7eb8f7' stroke-width='2'/>" +
                "</svg>";

            var result = AltitudeChartComposer.FromReportHtml(PerTargetHtml(inner), "sess-seg");

            // Two source segments become two transparent+colored pairs inside the target group.
            var colored = Regex.Matches(result.Svg, @"stroke='#4e79a7' stroke-width='2'");
            Assert.Equal(2, colored.Count);
        }

        [Fact]
        public void Fallback_SingleWindow_Unchanged() {
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
        public void PatternId_IsUniquePerSession() {
            var svg =
                "<svg viewBox='0 0 760 248' xmlns='http://www.w3.org/2000/svg'>" +
                "<defs><pattern id='ns-idle-alt'><rect width='8' height='8' fill='#0f0f23'/></pattern></defs>" +
                "<rect x='10' y='20' width='20' height='200' fill='url(#ns-idle-alt)' opacity='0.4'/>" +
                "<g><title>A</title><polyline points='1,1 2,2' fill='none' stroke='#4e79a7' stroke-width='2'/></g>" +
                "</svg>";
            var html = SessionTimelineHtml(svg);

            var a = AltitudeChartComposer.FromReportHtml(html, "sess/A");
            var b = AltitudeChartComposer.FromReportHtml(html, "sess/B");

            Assert.Contains("ns-idle-alt-sess_A", a.Svg);
            Assert.Contains("url(#ns-idle-alt-sess_A)", a.Svg);
            Assert.Contains("ns-idle-alt-sess_B", b.Svg);
            Assert.DoesNotContain("ns-idle-alt-sess_B", a.Svg);
        }

        [Fact]
        public void CacheVersion_RejectsLegacyJson() {
            Assert.False(AltitudeChartComposer.IsCurrentCache("{\"svg\":\"<svg/>\",\"legend\":[]}"));
            Assert.False(AltitudeChartComposer.IsCurrentCache("{\"svg\":\"<svg/>\",\"legend\":[],\"v\":1}"));
            Assert.True(AltitudeChartComposer.IsCurrentCache("{\"svg\":\"<svg/>\",\"legend\":[],\"v\":2}"));
            Assert.False(AltitudeChartComposer.IsCurrentCache(""));
            Assert.False(AltitudeChartComposer.IsCurrentCache("not json"));
        }
    }
}
