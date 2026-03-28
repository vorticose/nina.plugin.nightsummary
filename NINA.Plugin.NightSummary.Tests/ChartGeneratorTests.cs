using NINA.Plugin.NightSummary.MyPluginProperties;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System.Collections.Generic;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    public class ChartGeneratorTests {

        public ChartGeneratorTests() {
            Settings.Default.ReportLightMode = false;
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
            Settings.Default.ReportLightMode = true;
            var sessionId = "test-session";
            var images    = TestDataFactory.MakeImageSeries(sessionId, 5);
            foreach (var img in images) img.FocuserTemp = 12.5;

            var svg = ChartGenerator.GenerateMetricChart(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecFocuserTemp);

            Settings.Default.ReportLightMode = false; // reset
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
            // FocuserTemp = 0 → secondary has no data points
            foreach (var img in images) img.FocuserTemp = 0;

            var svg = ChartGenerator.GenerateMetricChart(images, ChartGenerator.PrimaryHFR, ChartGenerator.SecFocuserTemp);

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
    }
}
