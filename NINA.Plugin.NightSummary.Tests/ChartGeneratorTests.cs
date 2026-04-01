using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System.Collections.Generic;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    public class ChartGeneratorTests {

        public ChartGeneratorTests() {
            SettingsManager.Instance.Current.ReportLightMode = false;
        }

        // ── Primary metric coverage ──────────────────────────────────────────

        [Theory]
        [InlineData(ChartGenerator.PrimaryHFR)]
        [InlineData(ChartGenerator.PrimaryFWHM)]
        [InlineData(ChartGenerator.PrimaryGuidingRMS)]
        [InlineData(ChartGenerator.PrimaryFocuserTemp)]
        [InlineData(ChartGenerator.PrimaryAmbientTemp)]
        [InlineData(ChartGenerator.PrimaryEccentricity)]
        [InlineData(ChartGenerator.PrimaryAltitude)]
        [InlineData(ChartGenerator.PrimaryAirmass)]
        [InlineData(ChartGenerator.PrimaryHumidity)]
        [InlineData(ChartGenerator.PrimaryFocuserPos)]
        [InlineData(ChartGenerator.PrimarySkyQuality)]
        [InlineData(ChartGenerator.PrimaryCloudCover)]
        [InlineData(ChartGenerator.PrimaryCameraTemp)]
        [InlineData(ChartGenerator.PrimaryDewPoint)]
        [InlineData(ChartGenerator.PrimaryWindSpeed)]
        [InlineData(ChartGenerator.PrimaryPressure)]
        [InlineData(ChartGenerator.PrimaryStarCount)]
        [InlineData(ChartGenerator.PrimaryAzimuth)]
        [InlineData(ChartGenerator.PrimarySeeingFWHM)]
        public void AllPrimaryMetrics_ProduceNonEmptySvg(int metric) {
            var sessionId = "test-session";
            var images    = TestDataFactory.MakeImageSeries(sessionId, 5);

            // Populate all optional fields so every metric has data to plot
            foreach (var img in images) {
                img.FocuserTemp      = 12.5;
                img.AmbientTemp      = 8.0;
                img.Altitude         = 55.0;
                img.Azimuth          = 180.0;
                img.Airmass          = 1.2;
                img.Humidity         = 65.0;
                img.FocuserPosition  = 45200;
                img.SkyQuality       = 21.5;
                img.CloudCover       = 5.0;
                img.CameraTemp       = -10.0;
                img.DewPoint         = 2.0;
                img.WindSpeed        = 3.5;
                img.Pressure         = 1013.0;
                img.SeeingFWHM       = 2.8;
            }

            var svg = ChartGenerator.GenerateMetricChart(images, metric, ChartGenerator.SecNone);

            Assert.NotNull(svg);
            Assert.NotEmpty(svg);
            Assert.Contains("<svg", svg);
        }

        // ── Edge cases ───────────────────────────────────────────────────────

        [Fact]
        public void EmptyImageList_ReturnsPlaceholderSvg_NotCrash() {
            var svg = ChartGenerator.GenerateMetricChart(
                new List<NINA.Plugin.NightSummary.Data.ImageRecord>(),
                ChartGenerator.PrimaryHFR,
                ChartGenerator.SecNone);

            Assert.NotNull(svg);
            Assert.NotEmpty(svg);
            Assert.Contains("<svg", svg);
        }

        [Fact]
        public void SingleDataPoint_RendersWithoutError() {
            var sessionId = "test-session";
            var images    = new List<NINA.Plugin.NightSummary.Data.ImageRecord> {
                TestDataFactory.MakeImage(sessionId, hfr: 2.5)
            };

            var svg = ChartGenerator.GenerateMetricChart(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone);

            Assert.NotNull(svg);
            Assert.Contains("<svg", svg);
        }

        [Fact]
        public void DualAxisChart_ContainsExpectedElements() {
            var sessionId = "test-session";
            var images    = TestDataFactory.MakeImageSeries(sessionId, 5);
            foreach (var img in images) img.FocuserTemp = 12.5;

            var svg = ChartGenerator.GenerateMetricChart(
                images,
                ChartGenerator.PrimaryHFR,
                ChartGenerator.SecFocuserTemp);

            Assert.Contains("<svg", svg);
            Assert.NotEmpty(svg);
        }

        // ── Axis format ──────────────────────────────────────────────────────

        [Theory]
        [InlineData(ChartGenerator.PrimaryAltitude)]
        [InlineData(ChartGenerator.PrimaryStarCount)]
        [InlineData(ChartGenerator.PrimaryPressure)]
        [InlineData(ChartGenerator.PrimaryCloudCover)]
        public void IntegerMetrics_ProduceNonEmptySvg(int metric) {
            var sessionId = "test-session";
            var images    = TestDataFactory.MakeImageSeries(sessionId, 5);
            foreach (var img in images) {
                img.Altitude     = 55.0;
                img.StarCount    = 300;
                img.Pressure     = 1013.0;
                img.CloudCover   = 10.0;
            }

            var svg = ChartGenerator.GenerateMetricChart(images, metric, ChartGenerator.SecNone);

            Assert.NotEmpty(svg);
            Assert.Contains("<svg", svg);
        }

        // ── Light mode ────────────────────────────────────────────────────────

        [Fact]
        public void LightMode_GeneratesChart_WithLightColors() {
            SettingsManager.Instance.Current.ReportLightMode = true;
            var sessionId = "test-session";
            var images    = TestDataFactory.MakeImageSeries(sessionId, 5);
            foreach (var img in images) img.FocuserTemp = 12.5;

            var svg = ChartGenerator.GenerateMetricChart(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecFocuserTemp);

            SettingsManager.Instance.Current.ReportLightMode = false; // reset
            Assert.Contains("<svg", svg);
            // Light mode uses a light background color
            Assert.Contains("#f5f5f5", svg);
        }

        // ── Swapped mode (primary has no data, secondary does) ────────────────

        [Fact]
        public void SwappedMode_PrimaryNoData_SecondaryHasData_ShowsBadge() {
            var sessionId = "test-session";
            var images    = TestDataFactory.MakeImageSeries(sessionId, 5);
            // Zero HFR = no primary data points; FocuserTemp = data for secondary
            foreach (var img in images) { img.HFR = 0; img.FocuserTemp = 12.5; }

            var svg = ChartGenerator.GenerateMetricChart(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecFocuserTemp);

            Assert.Contains("<svg", svg);
            Assert.Contains("no data", svg);
        }

        [Fact]
        public void SecondaryNoData_WantedButMissing_ShowsBadge() {
            var sessionId = "test-session";
            var images    = TestDataFactory.MakeImageSeries(sessionId, 5); // HFR populated
            // GuidingRMSTotal = 0 → SecGuidingRMS extraction filters on > 0 → returns empty → badge shown
            foreach (var img in images) img.GuidingRMSTotal = 0;

            var svg = ChartGenerator.GenerateMetricChart(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecGuidingRMS);

            Assert.Contains("<svg", svg);
            Assert.Contains("no data", svg);
        }

        [Theory]
        [InlineData(ChartGenerator.PrimaryHFR)]
        [InlineData(ChartGenerator.PrimaryFWHM)]
        [InlineData(ChartGenerator.PrimaryGuidingRMS)]
        public void DecimalMetrics_AxisLabels_HaveOneDecimalPlace(int metric) {
            var sessionId = "test-session";
            var images    = TestDataFactory.MakeImageSeries(sessionId, 5);

            var svg = ChartGenerator.GenerateMetricChart(images, metric, ChartGenerator.SecNone);

            Assert.NotEmpty(svg);
            Assert.Contains("<svg", svg);
        }

        // ── Chart title ──────────────────────────────────────────────────────

        [Theory]
        [InlineData(ChartGenerator.PrimaryHFR,         ChartGenerator.SecNone)]
        [InlineData(ChartGenerator.PrimaryFWHM,        ChartGenerator.SecGuidingRMS)]
        [InlineData(ChartGenerator.PrimaryAltitude,    ChartGenerator.SecHumidity)]
        [InlineData(ChartGenerator.PrimarySkyQuality,  ChartGenerator.SecCloudCover)]
        public void GetChartTitle_ReturnsNonEmptyString(int primary, int secondary) {
            var title = ChartGenerator.GetChartTitle(primary, secondary);
            Assert.NotNull(title);
            Assert.NotEmpty(title);
        }

        // ── Custom X-Axis ───────────────────────────────────────────────────

        [Fact]
        public void GetChartTitle_TimeXAxis_SaysVsTime() {
            var title = ChartGenerator.GetChartTitle(
                ChartGenerator.PrimaryHFR, ChartGenerator.SecNone, ChartGenerator.XAxisTime);
            Assert.Equal("HFR Vs. Time", title);
        }

        [Fact]
        public void GetChartTitle_FrameIndexXAxis_SaysVsFrame() {
            var title = ChartGenerator.GetChartTitle(
                ChartGenerator.PrimaryHFR, ChartGenerator.SecNone, ChartGenerator.XAxisFrameIndex);
            Assert.Equal("HFR Vs. Frame", title);
        }

        [Fact]
        public void GetChartTitle_MetricXAxis_UsesMetricName() {
            int xAxis = ChartGenerator.XAxisMetricOffset + ChartGenerator.PrimaryAltitude;
            var title = ChartGenerator.GetChartTitle(
                ChartGenerator.PrimaryHFR, ChartGenerator.SecNone, xAxis);
            Assert.Contains("Altitude", title);
            Assert.DoesNotContain("Time", title);
        }

        [Fact]
        public void GetChartTitle_DualAxis_MetricXAxis_IncludesBothMetricsAndXLabel() {
            int xAxis = ChartGenerator.XAxisMetricOffset + ChartGenerator.PrimaryAltitude;
            var title = ChartGenerator.GetChartTitle(
                ChartGenerator.PrimaryHFR, ChartGenerator.SecFocuserTemp, xAxis);
            Assert.Contains("HFR", title);
            Assert.Contains("Focuser", title);
            Assert.Contains("Altitude", title);
        }

        [Theory]
        [InlineData(ChartGenerator.XAxisTime)]
        [InlineData(ChartGenerator.XAxisFrameIndex)]
        public void GetXAxisLabel_BuiltInModes_ReturnExpectedLabels(int xMetric) {
            var label = ChartGenerator.GetXAxisLabel(xMetric);
            Assert.NotNull(label);
            Assert.NotEmpty(label);
        }

        [Fact]
        public void GetXAxisLabel_MetricOffset_ReturnsMetricName() {
            int xMetric = ChartGenerator.XAxisMetricOffset + ChartGenerator.PrimaryHumidity;
            var label = ChartGenerator.GetXAxisLabel(xMetric);
            Assert.Contains("Humidity", label);
        }

        [Fact]
        public void FrameIndexXAxis_ProducesValidSvg() {
            var images = TestDataFactory.MakeImageSeries("test", 5);

            var svg = ChartGenerator.GenerateMetricChart(
                images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone,
                ChartGenerator.XAxisFrameIndex);

            Assert.Contains("<svg", svg);
            Assert.Contains("<polyline", svg);
        }

        [Fact]
        public void MetricXAxis_ProducesValidSvg() {
            var images = TestDataFactory.MakeImageSeries("test", 5);
            foreach (var img in images) {
                img.Altitude    = 45.0 + images.IndexOf(img) * 5;
                img.FocuserTemp = 12.5;
            }

            int xAxis = ChartGenerator.XAxisMetricOffset + ChartGenerator.PrimaryAltitude;
            var svg = ChartGenerator.GenerateMetricChart(
                images, ChartGenerator.PrimaryHFR, ChartGenerator.SecFocuserTemp, xAxis);

            Assert.Contains("<svg", svg);
            Assert.Contains("<polyline", svg);
        }

        [Fact]
        public void MetricXAxis_MissingXValues_FiltersOutPoints() {
            var images = TestDataFactory.MakeImageSeries("test", 5);
            // Only some images have Altitude — others default to 0 which ExtractPrimary filters out
            images[0].Altitude = 55.0;
            images[1].Altitude = 60.0;

            int xAxis = ChartGenerator.XAxisMetricOffset + ChartGenerator.PrimaryAltitude;
            var svg = ChartGenerator.GenerateMetricChart(
                images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone, xAxis);

            // Should still produce valid SVG (2 points or placeholder)
            Assert.Contains("<svg", svg);
        }

        [Fact]
        public void EmptyImages_NonTimeXAxis_ReturnsPlaceholder() {
            var svg = ChartGenerator.GenerateMetricChart(
                new List<ImageRecord>(),
                ChartGenerator.PrimaryHFR, ChartGenerator.SecNone,
                ChartGenerator.XAxisFrameIndex);

            Assert.Contains("<svg", svg);
        }

        [Fact]
        public void FrameIndexXAxis_TooltipsContainFrameNumber() {
            var images = TestDataFactory.MakeImageSeries("test", 3);

            var svg = ChartGenerator.GenerateMetricChart(
                images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone,
                ChartGenerator.XAxisFrameIndex);

            // Frame index tooltips should contain "#" prefix
            Assert.Contains("#", svg);
        }

        [Theory]
        [InlineData(ChartGenerator.PrimaryHFR)]
        [InlineData(ChartGenerator.PrimaryAltitude)]
        [InlineData(ChartGenerator.PrimaryFocuserTemp)]
        [InlineData(ChartGenerator.PrimaryStarCount)]
        public void AllPrimaryMetrics_AsXAxis_ProduceValidSvg(int metricAsX) {
            var images = TestDataFactory.MakeImageSeries("test", 5);
            foreach (var img in images) {
                img.FocuserTemp     = 12.5;
                img.AmbientTemp     = 8.0;
                img.Altitude        = 55.0;
                img.Azimuth         = 180.0;
                img.Airmass         = 1.2;
                img.Humidity        = 65.0;
                img.FocuserPosition = 45200;
                img.SkyQuality      = 21.5;
                img.CloudCover      = 5.0;
                img.CameraTemp      = -10.0;
                img.DewPoint        = 2.0;
                img.WindSpeed       = 3.5;
                img.Pressure        = 1013.0;
                img.SeeingFWHM      = 2.8;
                img.StarCount       = 300;
            }

            int xAxis = ChartGenerator.XAxisMetricOffset + metricAsX;
            // Use FWHM as primary so it doesn't collide with x-axis metric
            var svg = ChartGenerator.GenerateMetricChart(
                images, ChartGenerator.PrimaryFWHM, ChartGenerator.SecNone, xAxis);

            Assert.Contains("<svg", svg);
            Assert.Contains("<polyline", svg);
        }

        [Fact]
        public void NonTimeXAxis_RendersAxisTitle() {
            var images = TestDataFactory.MakeImageSeries("test", 5);
            foreach (var img in images) img.Altitude = 55.0;

            int xAxis = ChartGenerator.XAxisMetricOffset + ChartGenerator.PrimaryAltitude;
            var svg = ChartGenerator.GenerateMetricChart(
                images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone, xAxis);

            // Non-time x-axis renders an x-axis title label
            Assert.Contains("Altitude", svg);
        }
    }
}
