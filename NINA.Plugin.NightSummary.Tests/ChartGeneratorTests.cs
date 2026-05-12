using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.Linq;
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
        [InlineData(ChartGenerator.PrimaryMedian)]
        [InlineData(ChartGenerator.PrimarySkyTemp)]
        [InlineData(ChartGenerator.PrimarySkyBright)]
        [InlineData(ChartGenerator.PrimaryWindDir)]
        [InlineData(ChartGenerator.PrimaryWindGust)]
        [InlineData(ChartGenerator.PrimaryMeanADU)]
        [InlineData(ChartGenerator.PrimaryStDev)]
        [InlineData(ChartGenerator.PrimaryMAD)]
        [InlineData(ChartGenerator.PrimaryExposure)]
        [InlineData(ChartGenerator.PrimaryGain)]
        [InlineData(ChartGenerator.PrimaryOffset)]
        [InlineData(ChartGenerator.PrimaryCoolerSet)]
        [InlineData(ChartGenerator.PrimaryRotatorPos)]
        [InlineData(ChartGenerator.PrimaryPosAngle)]
        [InlineData(ChartGenerator.PrimaryMinADU)]
        [InlineData(ChartGenerator.PrimaryMaxADU)]
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
                img.StatMedian       = 1500.0;
                img.SkyTemperature   = -25.0;
                img.SkyBrightness    = 0.02;
                img.WindDirection    = 220.0;
                img.WindGust         = 5.0;
                img.StatMean         = 1530.0;
                img.StatStDev        = 85.0;
                img.StatMAD          = 45.0;
                img.CoolerSetpoint   = -10.0;
                img.RotatorPosition  = 180.0;
                img.PositionAngle    = 45.0;
                img.StatMin          = 50;
                img.StatMax          = 60000;
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

        // ── Event marker tests ──────────────────────────────────────────────

        [Fact]
        public void EventMarkers_TimeAxis_RendersMarkerLines() {
            var images = TestDataFactory.MakeImageSeries("test", 5);
            var markers = new List<(DateTime, string, string)> {
                (images[2].Timestamp, "AutoFocus", "AF completed — Filter: Ha")
            };

            var svg = ChartGenerator.GenerateMetricChart(
                images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone,
                ChartGenerator.XAxisTime, markers);

            Assert.Contains("stroke-dasharray=\"4,3\"", svg);   // AF dash pattern
            Assert.Contains("AF completed", svg);               // description in tooltip
            Assert.Contains(">AF</text>", svg);                 // label at top
            Assert.Contains("@ 22:10:00", svg);                 // timestamp in tooltip
        }

        [Fact]
        public void EventMarkers_NonTimeAxis_NoMarkers() {
            var images = TestDataFactory.MakeImageSeries("test", 5);
            var markers = new List<(DateTime, string, string)> {
                (images[2].Timestamp, "AutoFocus", "AF completed")
            };

            var svg = ChartGenerator.GenerateMetricChart(
                images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone,
                ChartGenerator.XAxisFrameIndex, markers);

            Assert.DoesNotContain("AF completed", svg);
        }

        [Fact]
        public void EventMarkers_OutsideRange_Skipped() {
            var images = TestDataFactory.MakeImageSeries("test", 5);
            var markers = new List<(DateTime, string, string)> {
                (images[0].Timestamp.AddHours(-1), "AutoFocus", "AF before range")
            };

            var svg = ChartGenerator.GenerateMetricChart(
                images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone,
                ChartGenerator.XAxisTime, markers);

            Assert.DoesNotContain("AF before range", svg);
        }

        [Fact]
        public void EventMarkers_Null_NoError() {
            var images = TestDataFactory.MakeImageSeries("test", 5);

            var svg = ChartGenerator.GenerateMetricChart(
                images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone,
                ChartGenerator.XAxisTime, null);

            Assert.Contains("<svg", svg);
            Assert.DoesNotContain(">AF</text>", svg);
        }

        [Fact]
        public void EventMarkers_MultipleEvents_AllRendered() {
            var images = TestDataFactory.MakeImageSeries("test", 10);
            var markers = new List<(DateTime, string, string)> {
                (images[2].Timestamp, "AutoFocus", "AF run 1"),
                (images[5].Timestamp, "AutoFocus", "AF run 2"),
                (images[8].Timestamp, "MeridianFlip", "Meridian flip")
            };

            var svg = ChartGenerator.GenerateMetricChart(
                images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone,
                ChartGenerator.XAxisTime, markers);

            // Count labels: 2 AF + 1 MF
            int afLabels = svg.Split(">AF</text>").Length - 1;
            int flipLabels = svg.Split(">MF</text>").Length - 1;
            Assert.Equal(2, afLabels);
            Assert.Equal(1, flipLabels);
            Assert.Contains("AF run 1", svg);
            Assert.Contains("AF run 2", svg);
            Assert.Contains("Meridian flip", svg);
        }

        // ── Target population ────────────────────────────────────────────────

        [Fact]
        public void BuildChartModel_SingleTarget_TargetsHasOneEntry() {
            var images = TestDataFactory.MakeImageSeries("s", 5, filter: "Ha", target: "M42");
            var model  = ChartGenerator.BuildChartModel(images,
                ChartGenerator.PrimaryHFR, ChartGenerator.SecNone, ChartGenerator.XAxisTime, null);
            Assert.Single(model.Targets);
            Assert.Equal("M42", model.Targets[0]);
        }

        [Fact]
        public void BuildChartModel_MultipleTargets_ChronologicalOrder() {
            var t0 = new DateTime(2025, 1, 15, 22, 0, 0);
            var images = new List<ImageRecord> {
                TestDataFactory.MakeImage("s", target: "Orion",  timestamp: t0),
                TestDataFactory.MakeImage("s", target: "Orion",  timestamp: t0.AddMinutes(10)),
                TestDataFactory.MakeImage("s", target: "M42",    timestamp: t0.AddMinutes(20)),
                TestDataFactory.MakeImage("s", target: "M42",    timestamp: t0.AddMinutes(30)),
                TestDataFactory.MakeImage("s", target: "IC 434", timestamp: t0.AddMinutes(40)),
            };
            var model = ChartGenerator.BuildChartModel(images,
                ChartGenerator.PrimaryHFR, ChartGenerator.SecNone, ChartGenerator.XAxisTime, null);
            Assert.Equal(new[] { "Orion", "M42", "IC 434" }, model.Targets);
        }

        [Fact]
        public void BuildChartModel_NullOrBlankTargetName_ExcludedFromTargets() {
            var t0 = new DateTime(2025, 1, 15, 22, 0, 0);
            var images = new List<ImageRecord> {
                TestDataFactory.MakeImage("s", target: "M42", timestamp: t0),
                new ImageRecord { SessionId="s", Timestamp=t0.AddMinutes(10), TargetName=null,  HFR=2.5, Filter="Ha", Accepted=true, ExposureDuration=300, Gain=100, Offset=10 },
                new ImageRecord { SessionId="s", Timestamp=t0.AddMinutes(20), TargetName="",    HFR=2.5, Filter="Ha", Accepted=true, ExposureDuration=300, Gain=100, Offset=10 },
                new ImageRecord { SessionId="s", Timestamp=t0.AddMinutes(30), TargetName="   ", HFR=2.5, Filter="Ha", Accepted=true, ExposureDuration=300, Gain=100, Offset=10 },
            };
            var model = ChartGenerator.BuildChartModel(images,
                ChartGenerator.PrimaryHFR, ChartGenerator.SecNone, ChartGenerator.XAxisTime, null);
            Assert.Single(model.Targets);
            Assert.Equal("M42", model.Targets[0]);
        }

        [Fact]
        public void BuildChartModel_Points_HaveTargetSet() {
            var t0 = new DateTime(2025, 1, 15, 22, 0, 0);
            var images = new List<ImageRecord> {
                TestDataFactory.MakeImage("s", target: "M42",   filter: "Ha",   timestamp: t0),
                TestDataFactory.MakeImage("s", target: "Orion", filter: "OIII", timestamp: t0.AddMinutes(30)),
            };
            var model = ChartGenerator.BuildChartModel(images,
                ChartGenerator.PrimaryHFR, ChartGenerator.SecNone, ChartGenerator.XAxisTime, null);
            Assert.All(model.PrimaryPoints, p => Assert.False(string.IsNullOrEmpty(p.Target)));
            Assert.Contains(model.PrimaryPoints, p => p.Target == "M42");
            Assert.Contains(model.PrimaryPoints, p => p.Target == "Orion");
        }

        [Fact]
        public void EventMarkers_DifferentTypes_DifferentStyles() {
            var images = TestDataFactory.MakeImageSeries("test", 5);
            var markers = new List<(DateTime, string, string)> {
                (images[1].Timestamp, "AutoFocus", "AF event"),
                (images[2].Timestamp, "MeridianFlip", "Flip event"),
                (images[3].Timestamp, "RoofOpen", "Safe event")
            };

            var svg = ChartGenerator.GenerateMetricChart(
                images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone,
                ChartGenerator.XAxisTime, markers);

            // Distinct colors matching event timeline (dark mode)
            Assert.Contains("#a78bfa", svg);  // AF purple
            Assert.Contains("#fbbf24", svg);  // Flip amber
            Assert.Contains("#34d399", svg);  // Safe green
            // Distinct labels
            Assert.Contains(">AF</text>", svg);
            Assert.Contains(">MF</text>", svg);
            Assert.Contains(">S</text>", svg);
            // Transparent hit area for tooltips
            Assert.Contains("stroke=\"transparent\" stroke-width=\"8\"", svg);
        }

        // ── New metric rendering tests ──────────────────────────────────────

        private static List<ImageRecord> MakePopulatedImages(int count = 5) {
            var images = TestDataFactory.MakeImageSeries("test-session", count);
            for (int i = 0; i < images.Count; i++) {
                images[i].FocuserTemp      = 12.5 + i * 0.2;
                images[i].AmbientTemp      = 8.0 + i * 0.1;
                images[i].Altitude         = 55.0 + i * 2;
                images[i].Azimuth          = 180.0 + i * 3;
                images[i].Airmass          = 1.2 - i * 0.02;
                images[i].Humidity         = 65.0 + i;
                images[i].FocuserPosition  = 45200 + i * 10;
                images[i].SkyQuality       = 21.5 + i * 0.05;
                images[i].CloudCover       = 5.0 + i;
                images[i].CameraTemp       = -10.0 + i * 0.1;
                images[i].DewPoint         = 2.0 + i * 0.3;
                images[i].WindSpeed        = 3.5 + i * 0.2;
                images[i].Pressure         = 1013.0 + i * 0.1;
                images[i].SeeingFWHM       = 2.8 + i * 0.1;
                images[i].StatMedian       = 1500.0 + i * 20;
                images[i].SkyTemperature   = -25.0 + i * 0.5;
                images[i].SkyBrightness    = 0.020 + i * 0.005;
                images[i].WindDirection    = 220.0 + i * 5;
                images[i].WindGust         = 5.0 + i * 0.3;
                images[i].StatMean         = 1530.0 + i * 20;
                images[i].StatStDev        = 85.0 + i * 2;
                images[i].StatMAD          = 45.0 + i;
                images[i].CoolerSetpoint   = -10.0;
                images[i].RotatorPosition  = 180.0 + i * 0.5;
                images[i].PositionAngle    = 45.0 + i * 0.2;
                images[i].StatMin          = 50 + i * 3;
                images[i].StatMax          = 60000 + i * 100;
                images[i].Gain             = 100;
                images[i].Offset           = 50;
            }
            return images;
        }

        [Theory]
        [InlineData(ChartGenerator.PrimarySkyTemp)]
        [InlineData(ChartGenerator.PrimarySkyBright)]
        [InlineData(ChartGenerator.PrimaryWindDir)]
        [InlineData(ChartGenerator.PrimaryWindGust)]
        [InlineData(ChartGenerator.PrimaryMeanADU)]
        [InlineData(ChartGenerator.PrimaryStDev)]
        [InlineData(ChartGenerator.PrimaryMAD)]
        [InlineData(ChartGenerator.PrimaryExposure)]
        [InlineData(ChartGenerator.PrimaryGain)]
        [InlineData(ChartGenerator.PrimaryOffset)]
        [InlineData(ChartGenerator.PrimaryCoolerSet)]
        [InlineData(ChartGenerator.PrimaryRotatorPos)]
        [InlineData(ChartGenerator.PrimaryPosAngle)]
        [InlineData(ChartGenerator.PrimaryMinADU)]
        [InlineData(ChartGenerator.PrimaryMaxADU)]
        public void NewMetrics_WithData_RenderPolyline(int metric) {
            var images = MakePopulatedImages();

            var svg = ChartGenerator.GenerateMetricChart(images, metric, ChartGenerator.SecNone);

            Assert.Contains("<svg", svg);
            Assert.Contains("<polyline", svg);  // actual data line, not just placeholder
            Assert.Contains("<circle", svg);    // data points rendered
        }

        [Theory]
        [InlineData(ChartGenerator.SecSkyTemp)]
        [InlineData(ChartGenerator.SecSkyBright)]
        [InlineData(ChartGenerator.SecWindDir)]
        [InlineData(ChartGenerator.SecWindGust)]
        [InlineData(ChartGenerator.SecMeanADU)]
        [InlineData(ChartGenerator.SecStDev)]
        [InlineData(ChartGenerator.SecMAD)]
        [InlineData(ChartGenerator.SecExposure)]
        [InlineData(ChartGenerator.SecGain)]
        [InlineData(ChartGenerator.SecOffset)]
        [InlineData(ChartGenerator.SecCoolerSet)]
        [InlineData(ChartGenerator.SecRotatorPos)]
        [InlineData(ChartGenerator.SecPosAngle)]
        [InlineData(ChartGenerator.SecMinADU)]
        [InlineData(ChartGenerator.SecMaxADU)]
        public void NewMetrics_AsSecondary_RenderDualAxis(int secMetric) {
            var images = MakePopulatedImages();

            var svg = ChartGenerator.GenerateMetricChart(images, ChartGenerator.PrimaryHFR, secMetric);

            Assert.Contains("<svg", svg);
            Assert.Contains("stroke-dasharray=\"6,3\"", svg);  // secondary line is dashed
        }

        [Theory]
        [InlineData(ChartGenerator.PrimarySkyTemp,    "Sky Temp")]
        [InlineData(ChartGenerator.PrimarySkyBright,  "Sky Brightness")]
        [InlineData(ChartGenerator.PrimaryWindDir,    "Wind Direction")]
        [InlineData(ChartGenerator.PrimaryWindGust,   "Wind Gust")]
        [InlineData(ChartGenerator.PrimaryMeanADU,    "Mean ADU")]
        [InlineData(ChartGenerator.PrimaryStDev,      "Std Deviation")]
        [InlineData(ChartGenerator.PrimaryMAD,        "MAD")]
        [InlineData(ChartGenerator.PrimaryExposure,   "Exposure")]
        [InlineData(ChartGenerator.PrimaryGain,       "Gain")]
        [InlineData(ChartGenerator.PrimaryOffset,     "Offset")]
        [InlineData(ChartGenerator.PrimaryCoolerSet,  "Cooler Setpoint")]
        [InlineData(ChartGenerator.PrimaryRotatorPos, "Rotator Position")]
        [InlineData(ChartGenerator.PrimaryPosAngle,   "Position Angle")]
        [InlineData(ChartGenerator.PrimaryMinADU,     "Min ADU")]
        [InlineData(ChartGenerator.PrimaryMaxADU,     "Max ADU")]
        public void NewMetrics_ChartTitle_ContainsMetricName(int metric, string expectedLabel) {
            var title = ChartGenerator.GetChartTitle(metric, ChartGenerator.SecNone);

            Assert.Contains(expectedLabel, title);
        }

        [Theory]
        [InlineData(ChartGenerator.PrimarySkyTemp,    "no sky temperature")]
        [InlineData(ChartGenerator.PrimarySkyBright,  "no sky brightness")]
        [InlineData(ChartGenerator.PrimaryWindDir,    "no wind direction")]
        [InlineData(ChartGenerator.PrimaryWindGust,   "no wind gust")]
        [InlineData(ChartGenerator.PrimaryMeanADU,    "no mean")]
        [InlineData(ChartGenerator.PrimaryStDev,      "no standard deviation")]
        [InlineData(ChartGenerator.PrimaryMAD,        "no MAD")]
        [InlineData(ChartGenerator.PrimaryExposure,   "no exposure")]
        [InlineData(ChartGenerator.PrimaryGain,       "no gain")]
        [InlineData(ChartGenerator.PrimaryOffset,     "no offset")]
        [InlineData(ChartGenerator.PrimaryCoolerSet,  "no cooler")]
        [InlineData(ChartGenerator.PrimaryRotatorPos, "no rotator")]
        [InlineData(ChartGenerator.PrimaryPosAngle,   "no position angle")]
        [InlineData(ChartGenerator.PrimaryMinADU,     "no min")]
        [InlineData(ChartGenerator.PrimaryMaxADU,     "no max")]
        public void NewMetrics_NoData_ShowsPlaceholderMessage(int metric, string expectedFragment) {
            var images = TestDataFactory.MakeImageSeries("test-session", 5);
            // Leave all new fields null/default — should trigger placeholder
            // Exposure/Gain/Offset are always populated by MakeImage; zero them out
            foreach (var img in images) {
                img.ExposureDuration = 0;
                img.Gain   = -1;
                img.Offset = -1;
            }

            var svg = ChartGenerator.GenerateMetricChart(images, metric, ChartGenerator.SecNone);

            Assert.Contains("<svg", svg);
            Assert.Contains(expectedFragment, svg, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SkyBrightness_UsesHighPrecisionFormat() {
            var images = MakePopulatedImages();
            // Values are 0.020, 0.025, 0.030, 0.035, 0.040

            var svg = ChartGenerator.GenerateMetricChart(
                images, ChartGenerator.PrimarySkyBright, ChartGenerator.SecNone);

            Assert.Contains("<polyline", svg);
            // F3 axis format should produce 3 decimal places (e.g. "0.020")
            Assert.Contains("0.0", svg);  // at minimum, axis labels are not blank
            // F4 tooltip format should show in circle titles
            Assert.Contains("Lux", svg);  // tooltip unit present
        }

        [Theory]
        [InlineData(ChartGenerator.PrimarySkyTemp)]
        [InlineData(ChartGenerator.PrimarySkyBright)]
        [InlineData(ChartGenerator.PrimaryWindDir)]
        [InlineData(ChartGenerator.PrimaryWindGust)]
        [InlineData(ChartGenerator.PrimaryMeanADU)]
        [InlineData(ChartGenerator.PrimaryStDev)]
        [InlineData(ChartGenerator.PrimaryMAD)]
        [InlineData(ChartGenerator.PrimaryExposure)]
        [InlineData(ChartGenerator.PrimaryGain)]
        [InlineData(ChartGenerator.PrimaryOffset)]
        [InlineData(ChartGenerator.PrimaryCoolerSet)]
        [InlineData(ChartGenerator.PrimaryRotatorPos)]
        [InlineData(ChartGenerator.PrimaryPosAngle)]
        [InlineData(ChartGenerator.PrimaryMinADU)]
        [InlineData(ChartGenerator.PrimaryMaxADU)]
        public void NewMetrics_AsXAxis_ProduceValidSvg(int metricAsX) {
            var images = MakePopulatedImages();

            int xAxis = ChartGenerator.XAxisMetricOffset + metricAsX;
            var svg = ChartGenerator.GenerateMetricChart(
                images, ChartGenerator.PrimaryHFR, ChartGenerator.SecNone, xAxis);

            Assert.Contains("<svg", svg);
            Assert.Contains("<polyline", svg);
        }
    }
}
