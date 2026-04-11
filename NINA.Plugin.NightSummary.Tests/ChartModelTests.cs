using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    /// <summary>
    /// Tests for <see cref="ChartGenerator.BuildChartModel"/> — the JSON-ready
    /// data model that feeds the client-side JS renderer. Legacy SVG path is
    /// covered by <see cref="ChartGeneratorTests"/>.
    /// </summary>
    public class ChartModelTests {

        public ChartModelTests() {
            SettingsManager.Instance.Current.ReportLightMode = false;
        }

        // ── Metric info population ──────────────────────────────────────────

        [Fact]
        public void BuildChartModel_Primary_PopulatesMetricInfo() {
            var images = TestDataFactory.MakeImageSeries("t", 5);

            var model = ChartGenerator.BuildChartModel(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone);

            Assert.NotNull(model.Primary);
            Assert.Equal(ChartGenerator.PrimaryHFR, model.Primary.Index);
            Assert.Equal("HFR", model.Primary.Label);
            Assert.Equal("HFR (px)", model.Primary.AxisLabel);
            Assert.Equal(" px", model.Primary.Unit);
            Assert.Equal("F1", model.Primary.Format);
            Assert.Null(model.Primary.NoDataMessage);   // 5 points is enough
        }

        [Fact]
        public void BuildChartModel_Secondary_PopulatesWhenRequested() {
            var images = TestDataFactory.MakeImageSeries("t", 5);

            var model = ChartGenerator.BuildChartModel(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecFocuserTemp);

            Assert.NotNull(model.Secondary);
            Assert.Equal(ChartGenerator.SecFocuserTemp, model.Secondary!.Index);
            Assert.Equal("Focuser Temp", model.Secondary.Label);
        }

        [Fact]
        public void BuildChartModel_Secondary_NullWhenSecNone() {
            var images = TestDataFactory.MakeImageSeries("t", 5);

            var model = ChartGenerator.BuildChartModel(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone);

            Assert.Null(model.Secondary);
            Assert.Empty(model.SecondaryPoints);
        }

        [Fact]
        public void BuildChartModel_IntegerMetric_UsesF0Format() {
            var images = TestDataFactory.MakeImageSeries("t", 5);
            foreach (var img in images) img.StarCount = 300;

            var model = ChartGenerator.BuildChartModel(images, ChartGenerator.PrimaryStarCount, ChartGenerator.SecNone);

            Assert.Equal("F0", model.Primary.Format);
        }

        // ── Empty / insufficient data ───────────────────────────────────────

        [Fact]
        public void BuildChartModel_EmptyImages_ReturnsModelWithEmptyPoints() {
            var model = ChartGenerator.BuildChartModel(new List<ImageRecord>(), ChartGenerator.PrimaryHFR, ChartGenerator.SecNone);

            Assert.NotNull(model);
            Assert.Empty(model.PrimaryPoints);
            Assert.Empty(model.SecondaryPoints);
            Assert.Empty(model.Filters);
            Assert.NotNull(model.Primary.NoDataMessage);
        }

        [Fact]
        public void BuildChartModel_InsufficientData_SetsNoDataMessage() {
            var images = TestDataFactory.MakeImageSeries("t", 5);
            // Zero out HFR so ExtractPrimary returns 0 points
            foreach (var img in images) img.HFR = 0;

            var model = ChartGenerator.BuildChartModel(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone);

            Assert.NotNull(model.Primary.NoDataMessage);
            Assert.Contains("HFR", model.Primary.NoDataMessage);
        }

        [Fact]
        public void BuildChartModel_FWHMMissing_NoDataHintMentionsHocusFocus() {
            var images = TestDataFactory.MakeImageSeries("t", 5);
            foreach (var img in images) img.FWHM = 0;

            var model = ChartGenerator.BuildChartModel(images, ChartGenerator.PrimaryFWHM, ChartGenerator.SecNone);

            Assert.NotNull(model.Primary.NoDataHint);
            Assert.Contains("Hocus Focus", model.Primary.NoDataHint);
        }

        // ── Filter list ──────────────────────────────────────────────────────

        [Fact]
        public void BuildChartModel_MultipleFilters_ListedInSortOrder() {
            var images = BuildLrgbImages();

            var model = ChartGenerator.BuildChartModel(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone);

            // FilterHelper.SortKey uses L, R, G, B, Ha, Sii, Oiii order — so LRGB comes out in that order
            Assert.Equal(new[] { "L", "R", "G", "B" }, model.Filters);
        }

        [Fact]
        public void BuildChartModel_SingleFilter_ListedOnlyOnce() {
            var images = TestDataFactory.MakeImageSeries("t", 5, filter: "L");

            var model = ChartGenerator.BuildChartModel(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone);

            Assert.Single(model.Filters);
            Assert.Equal("L", model.Filters[0]);
        }

        [Fact]
        public void BuildChartModel_Points_CarryFilterNames() {
            var images = BuildLrgbImages();

            var model = ChartGenerator.BuildChartModel(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone);

            // Every point should have its source filter set
            Assert.All(model.PrimaryPoints, p => Assert.False(string.IsNullOrEmpty(p.Filter)));
            // And the set of point filters should match the distinct filter list
            var pointFilters = model.PrimaryPoints.Select(p => p.Filter).Distinct().OrderBy(f => f).ToList();
            Assert.Equal(new[] { "B", "G", "L", "R" }, pointFilters);
        }

        // ── Event markers ────────────────────────────────────────────────────

        [Fact]
        public void BuildChartModel_EventMarkers_PrecomputedXValue() {
            var images = TestDataFactory.MakeImageSeries("t", 5);
            var minTime = images[0].Timestamp;
            var afTime  = images[2].Timestamp;
            var markers = new List<(DateTime, string, string)> {
                (afTime, "AutoFocus", "AF at Ha")
            };

            var model = ChartGenerator.BuildChartModel(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone, ChartGenerator.XAxisTime, markers);

            Assert.Single(model.EventMarkers);
            var evt = model.EventMarkers[0];
            Assert.Equal("AutoFocus", evt.Type);
            Assert.Equal("AF",         evt.Label);
            Assert.Equal("AF at Ha",   evt.Description);
            // xValue should be seconds from the first image timestamp
            Assert.Equal((afTime - minTime).TotalSeconds, evt.XValue, precision: 3);
        }

        [Theory]
        [InlineData("AutoFocus",    "AF")]
        [InlineData("MeridianFlip", "MF")]
        [InlineData("RoofOpen",     "S")]
        [InlineData("RoofClosed",   "US")]
        public void BuildChartModel_EventMarker_LabelMatchesType(string type, string expectedLabel) {
            var images = TestDataFactory.MakeImageSeries("t", 5);
            var markers = new List<(DateTime, string, string)> {
                (images[2].Timestamp, type, "evt")
            };

            var model = ChartGenerator.BuildChartModel(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone, ChartGenerator.XAxisTime, markers);

            Assert.Equal(expectedLabel, model.EventMarkers[0].Label);
        }

        // ── Chart config ─────────────────────────────────────────────────────

        [Fact]
        public void BuildChartModel_Title_NotEmpty() {
            var images = TestDataFactory.MakeImageSeries("t", 5);

            var model = ChartGenerator.BuildChartModel(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone);

            Assert.Equal("HFR Vs. Time", model.Title);
        }

        [Fact]
        public void BuildChartModel_LightMode_SetsFlag() {
            SettingsManager.Instance.Current.ReportLightMode = true;
            var images = TestDataFactory.MakeImageSeries("t", 5);

            var model = ChartGenerator.BuildChartModel(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone);

            SettingsManager.Instance.Current.ReportLightMode = false;
            Assert.True(model.LightMode);
        }

        [Fact]
        public void BuildChartModel_XAxisTime_SetsModeZero() {
            var images = TestDataFactory.MakeImageSeries("t", 5);

            var model = ChartGenerator.BuildChartModel(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone, ChartGenerator.XAxisTime);

            Assert.Equal(0, model.XAxis.Mode);
            Assert.Equal("Time", model.XAxis.Label);
            Assert.Empty(model.XAxis.AxisLabel); // Time axis has no bottom title
        }

        [Fact]
        public void BuildChartModel_XAxisFrameIndex_SetsAxisLabel() {
            var images = TestDataFactory.MakeImageSeries("t", 5);

            var model = ChartGenerator.BuildChartModel(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone, ChartGenerator.XAxisFrameIndex);

            Assert.Equal(1, model.XAxis.Mode);
            Assert.Equal("Frame #", model.XAxis.AxisLabel);
        }

        [Fact]
        public void BuildChartModel_XAxisMetric_SetsFormatAndUnit() {
            var images = TestDataFactory.MakeImageSeries("t", 5);
            foreach (var img in images) img.Altitude = 55.0;

            int xAxis = ChartGenerator.XAxisMetricOffset + ChartGenerator.PrimaryAltitude;
            var model = ChartGenerator.BuildChartModel(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone, xAxis);

            Assert.Equal(xAxis, model.XAxis.Mode);
            Assert.Contains("Altitude", model.XAxis.AxisLabel);
            Assert.Equal("F0", model.XAxis.Format);      // Altitude is an integer-format metric
            Assert.Equal("°",  model.XAxis.Unit);
        }

        [Fact]
        public void BuildChartModel_Dimensions_MatchLegacyConstants() {
            var images = TestDataFactory.MakeImageSeries("t", 5);

            var model = ChartGenerator.BuildChartModel(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone);

            Assert.Equal(800, model.Width);
            Assert.Equal(300, model.Height);
        }

        // ── Point count sanity ───────────────────────────────────────────────

        [Fact]
        public void BuildChartModel_AllValidImages_ProducesPointPerImage() {
            var images = TestDataFactory.MakeImageSeries("t", 7);

            var model = ChartGenerator.BuildChartModel(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone);

            Assert.Equal(7, model.PrimaryPoints.Count);
        }

        [Fact]
        public void BuildChartModel_PointsSortedByX() {
            var images = TestDataFactory.MakeImageSeries("t", 10);
            // Shuffle to confirm BuildPointList sorts
            var shuffled = images.OrderBy(_ => Guid.NewGuid()).ToList();

            var model = ChartGenerator.BuildChartModel(shuffled, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone);

            var xs = model.PrimaryPoints.Select(p => p.X).ToList();
            var sorted = xs.OrderBy(x => x).ToList();
            Assert.Equal(sorted, xs);
        }

        // ── JSON round-trip ──────────────────────────────────────────────────

        [Fact]
        public void BuildChartModel_SerializesAndDeserializesRoundTrip() {
            var images = BuildLrgbImages();
            var markers = new List<(DateTime, string, string)> {
                (images[4].Timestamp, "AutoFocus", "AF")
            };

            var model = ChartGenerator.BuildChartModel(
                images,
                ChartGenerator.PrimaryHFR,
                ChartGenerator.SecFocuserTemp,
                ChartGenerator.XAxisTime,
                markers);

            var json = JsonSerializer.Serialize(model);
            var back = JsonSerializer.Deserialize<ChartModel>(json);

            Assert.NotNull(back);
            Assert.Equal(model.Title, back!.Title);
            Assert.Equal(model.PrimaryPoints.Count, back.PrimaryPoints.Count);
            Assert.Equal(model.Filters, back.Filters);
            Assert.Equal(model.EventMarkers.Count, back.EventMarkers.Count);
            Assert.Equal(model.Primary.Label, back.Primary.Label);
        }

        [Fact]
        public void BuildChartModel_Json_UsesCamelCaseProperties() {
            var images = TestDataFactory.MakeImageSeries("t", 5);
            var model = ChartGenerator.BuildChartModel(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone);

            var json = JsonSerializer.Serialize(model);

            // Assert the camelCase property names the JS renderer reads are present
            Assert.Contains("\"primaryPoints\"", json);
            Assert.Contains("\"xAxis\"", json);
            Assert.Contains("\"lightMode\"", json);
            Assert.Contains("\"filters\"", json);
        }

        // ── Duplicate-timestamp regression ──────────────────────────────────

        [Fact]
        public void BuildChartModel_DuplicateTimestamps_FilterAttributionPreserved() {
            // Two images at the exact same timestamp, different filters.
            // The timestamp-keyed lookup approach would misattribute one of them.
            var t = new DateTime(2025, 1, 15, 22, 30, 0);
            var lImg   = TestDataFactory.MakeImage("t", filter: "L",  hfr: 1.8);
            var siiImg = TestDataFactory.MakeImage("t", filter: "Sii", hfr: 2.6);
            lImg.Timestamp   = t;
            siiImg.Timestamp = t;
            // Add a second L point so the chart has enough data to render
            var lImg2 = TestDataFactory.MakeImage("t", filter: "L", hfr: 1.9);
            lImg2.Timestamp = t.AddMinutes(5);
            var siiImg2 = TestDataFactory.MakeImage("t", filter: "Sii", hfr: 2.7);
            siiImg2.Timestamp = t.AddMinutes(10);

            var images = new List<ImageRecord> { lImg, siiImg, lImg2, siiImg2 };

            var model = ChartGenerator.BuildChartModel(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone);

            // Each input image should appear as exactly one point with its own filter
            Assert.Equal(4, model.PrimaryPoints.Count);
            var lPoints   = model.PrimaryPoints.Where(p => p.Filter == "L").ToList();
            var siiPoints = model.PrimaryPoints.Where(p => p.Filter == "Sii").ToList();
            Assert.Equal(2, lPoints.Count);
            Assert.Equal(2, siiPoints.Count);
            // Y-values must remain aligned with the right filter
            Assert.Contains(lPoints,   p => p.Y == 1.8);
            Assert.Contains(lPoints,   p => p.Y == 1.9);
            Assert.Contains(siiPoints, p => p.Y == 2.6);
            Assert.Contains(siiPoints, p => p.Y == 2.7);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a 12-image LRGB series cycling L, R, G, B three times,
        /// one minute apart. Mimics the rotation pattern described in the
        /// feature request.
        /// </summary>
        private static List<ImageRecord> BuildLrgbImages() {
            var images = new List<ImageRecord>();
            var filters = new[] { "L", "R", "G", "B" };
            var start = new DateTime(2025, 1, 15, 22, 0, 0);
            for (int i = 0; i < 12; i++) {
                var img = TestDataFactory.MakeImage("t", filter: filters[i % 4], hfr: 2.0 + (i * 0.05));
                img.Timestamp = start.AddMinutes(i);
                images.Add(img);
            }
            return images;
        }
    }
}
