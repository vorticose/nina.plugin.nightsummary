using NINA.Plugin.NightSummary.Reporting;
using System;
using System.Collections.Generic;
using Xunit;
using Calc = NINA.Plugin.NightSummary.Reporting.SkyBackgroundCalculator;

namespace NINA.Plugin.NightSummary.Tests {
    public class SkyBackgroundChartTests {

        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 21, 0, 0, DateTimeKind.Utc);

        private static Calc.SkyBackgroundResult Result(params Calc.SkyFramePoint[] pts) =>
            new Calc.SkyBackgroundResult(pts, Array.Empty<Calc.SkyFilterSummary>());

        [Fact]
        public void NoGradeablePoints_ReturnsPlaceholder() {
            var result = Result(
                new Calc.SkyFramePoint(T0, "OIII", 500, null),
                new Calc.SkyFramePoint(T0.AddMinutes(10), "OIII", 600, null));
            var svg = ChartGenerator.BuildSkyBackgroundChart(result);
            Assert.Contains("baseline still building", svg);
            Assert.DoesNotContain("<polyline", svg);
        }

        [Fact]
        public void TwoFilters_RendersLinesFloorAndAxes() {
            var result = Result(
                new Calc.SkyFramePoint(T0,               "OIII", 500, 1.2),
                new Calc.SkyFramePoint(T0.AddMinutes(60), "OIII", 900, 2.8),
                new Calc.SkyFramePoint(T0,               "Ha",   300, 1.0),
                new Calc.SkyFramePoint(T0.AddMinutes(60), "Ha",   320, 1.1));
            var svg = ChartGenerator.BuildSkyBackgroundChart(result);

            Assert.StartsWith("<svg", svg);
            Assert.Contains("your darkest", svg);   // floor line label
            Assert.Contains("×", svg);               // multiplier axis
            Assert.Contains("OIII", svg);            // legend
            Assert.Contains("Ha", svg);
            Assert.Contains("<circle", svg);         // per-frame dots
            // one polyline per filter (each has >= 2 points)
            Assert.Equal(2, CountOccurrences(svg, "<polyline"));
        }

        [Fact]
        public void SingleFramePerFilter_DrawsDotWithoutPolyline() {
            var result = Result(new Calc.SkyFramePoint(T0, "OIII", 500, 1.5));
            var svg = ChartGenerator.BuildSkyBackgroundChart(result);
            Assert.Contains("<circle", svg);
            Assert.DoesNotContain("<polyline", svg);
        }

        private static int CountOccurrences(string haystack, string needle) {
            int n = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }
    }
}
