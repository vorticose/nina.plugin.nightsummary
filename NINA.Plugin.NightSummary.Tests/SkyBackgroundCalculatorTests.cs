using NINA.Plugin.NightSummary.Reporting;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Calc = NINA.Plugin.NightSummary.Reporting.SkyBackgroundCalculator;

namespace NINA.Plugin.NightSummary.Tests {
    public class SkyBackgroundCalculatorTests {

        // Known lunar phases (2024). New moon and full moon are ephemeris extremes, so the
        // illumination assertions below stay far from any threshold and tolerate the
        // low-precision moon model.
        private static readonly DateTime NewMoonUtc  = new DateTime(2024, 1, 11, 12, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime FullMoonUtc = new DateTime(2024, 1, 25, 18, 0, 0, DateTimeKind.Utc);

        // At 18:00 UTC the full moon is near transit at lon +90 (local midnight) and near
        // nadir at lon -90 (local noon) — sign of the altitude is unambiguous either way.
        private const double MoonUpLat = 0.0, MoonUpLon = 90.0;
        private const double MoonDownLat = 0.0, MoonDownLon = -90.0;

        private static Calc.FrameRow Frame(
            DateTime tsUtc, double median, string filter = "Ha", int exposure = 600,
            int gain = 100, int offset = 50, int binning = 1, int bitDepth = 16) =>
            new Calc.FrameRow(tsUtc, filter, gain, offset, binning, exposure, bitDepth, median, 5.0, 20.0);

        private static List<Calc.FrameRow> Repeat(int n, DateTime ts, double median, string filter = "Ha") =>
            Enumerable.Range(0, n).Select(_ => Frame(ts, median, filter)).ToList();

        // ── Percentile ──────────────────────────────────────────────────────────

        [Fact]
        public void Percentile_Empty_IsNaN() =>
            Assert.True(double.IsNaN(Calc.Percentile(Array.Empty<double>(), 0.10)));

        [Fact]
        public void Percentile_Single_ReturnsThatValue() =>
            Assert.Equal(42.0, Calc.Percentile(new[] { 42.0 }, 0.10));

        [Fact]
        public void Percentile_UnsortedInput_IsHandled() {
            // 0..100 by 10; P10 (type-7) of 11 points = value at rank 0.1*10 = 1.0 -> 10.
            var vals = new[] { 100.0, 0, 50, 10, 90, 20, 80, 30, 70, 40, 60 };
            Assert.Equal(10.0, Calc.Percentile(vals, 0.10), 6);
        }

        [Fact]
        public void Percentile_Interpolates() {
            var vals = new[] { 0.0, 10.0 };                 // rank 0.1*1 = 0.1 -> 0 + 0.1*(10-0) = 1
            Assert.Equal(1.0, Calc.Percentile(vals, 0.10), 6);
        }

        // ── Median ──────────────────────────────────────────────────────────────

        [Fact]
        public void Median_Odd_MiddleValue() =>
            Assert.Equal(20.0, Calc.Median(new[] { 30.0, 10.0, 20.0 }));

        [Fact]
        public void Median_Even_AveragesMiddlePair() =>
            Assert.Equal(25.0, Calc.Median(new[] { 10.0, 20.0, 30.0, 40.0 }));

        // ── Cohort key ────────────────────────────────────────────────────────────

        [Fact]
        public void KeyOf_RoundsExposureToNearestSecond() {
            Assert.Equal(Calc.KeyOf(Frame(NewMoonUtc, 100, exposure: 600)),
                         Calc.KeyOf(new Calc.FrameRow(NewMoonUtc, "Ha", 100, 50, 1, 600.4, 16, 100, 5, 20)));
        }

        [Fact]
        public void KeyOf_DifferentExposure_DifferentCohort() =>
            Assert.NotEqual(Calc.KeyOf(Frame(NewMoonUtc, 100, exposure: 600)),
                            Calc.KeyOf(Frame(NewMoonUtc, 100, exposure: 300)));

        // ── Moon geometry ─────────────────────────────────────────────────────────

        [Theory]
        [InlineData(2024, 1, 11)]
        [InlineData(2024, 1, 25)]
        public void MoonIllumFraction_InUnitRange(int y, int mo, int d) {
            double f = Calc.MoonIllumFraction(new DateTime(y, mo, d, 12, 0, 0, DateTimeKind.Utc));
            Assert.InRange(f, 0.0, 1.0);
        }

        [Fact]
        public void MoonIllumFraction_NewMoonDark_FullMoonBright() {
            Assert.True(Calc.MoonIllumFraction(NewMoonUtc) < 0.20);
            Assert.True(Calc.MoonIllumFraction(FullMoonUtc) > 0.80);
        }

        [Fact]
        public void IsMoonlessCondition_NewMoon_IsTrueAnywhere() =>
            Assert.True(Calc.IsMoonlessCondition(Frame(NewMoonUtc, 100), 40.0, -75.0));

        [Fact]
        public void IsMoonlessCondition_FullMoonBelowHorizon_IsTrue() =>
            Assert.True(Calc.IsMoonlessCondition(Frame(FullMoonUtc, 100), MoonDownLat, MoonDownLon));

        [Fact]
        public void IsMoonlessCondition_FullMoonUp_IsFalse() =>
            Assert.False(Calc.IsMoonlessCondition(Frame(FullMoonUtc, 100), MoonUpLat, MoonUpLon));

        // ── ComputeFromFrames: aggregation ──────────────────────────────────────────

        [Fact]
        public void EmptySession_ReturnsEmpty() {
            var result = Calc.ComputeFromFrames(
                Array.Empty<Calc.FrameRow>(), Repeat(30, NewMoonUtc, 100), 40, -75);
            Assert.Empty(result.Points);
            Assert.Empty(result.Filters);
        }

        [Fact]
        public void ColdStart_UnderThreshold_RatioIsNull() {
            var history = Repeat(10, NewMoonUtc, 100);          // < MinCohortSamples
            var session = new[] { Frame(NewMoonUtc, 300) };
            var result  = Calc.ComputeFromFrames(session, history.Concat(session).ToList(), 40, -75);

            Assert.Null(result.Points.Single().TimesDarkest);
            Assert.False(result.Filters.Single().BaselineReady);
            Assert.Null(result.Filters.Single().TimesDarkest);
        }

        [Fact]
        public void ReadyCohort_ComputesTimesDarkest() {
            var history = Repeat(20, NewMoonUtc, 100);          // floor P10 = 100
            var session = new[] { Frame(NewMoonUtc, 300) };     // 300 / 100 = 3x
            var result  = Calc.ComputeFromFrames(session, history.Concat(session).ToList(), 40, -75);

            var f = result.Filters.Single();
            Assert.True(f.BaselineReady);
            Assert.Equal(100.0, f.FloorAdu.Value, 3);
            Assert.Equal(3.0, f.TimesDarkest.Value, 3);
            Assert.True(f.FloorIsMoonless);                     // 20 moonless frames >= threshold
            Assert.Equal(3.0, result.Points.Single().TimesDarkest.Value, 3);
        }

        [Fact]
        public void PrefersMoonlessFloor_OverBrightFrames() {
            // 25 dark frames at 100 define the floor; 5 bright moon-up frames at 1000 are excluded.
            var history = Repeat(25, NewMoonUtc, 100)
                .Concat(Repeat(5, FullMoonUtc, 1000)).ToList();
            var session = new[] { Frame(NewMoonUtc, 300) };
            var result  = Calc.ComputeFromFrames(session, history.Concat(session).ToList(), MoonUpLat, MoonUpLon);

            var f = result.Filters.Single();
            Assert.True(f.FloorIsMoonless);
            Assert.Equal(100.0, f.FloorAdu.Value, 3);           // moonless pool, not polluted by the 1000s
            Assert.Equal(3.0, f.TimesDarkest.Value, 3);
        }

        [Fact]
        public void FallsBackToFullCohort_WhenTooFewMoonless() {
            // Only 10 moonless (< threshold) but 25 total -> baseline ready, floor from full pool, flag off.
            var history = Repeat(10, NewMoonUtc, 100)
                .Concat(Repeat(15, FullMoonUtc, 500)).ToList();
            var session = new[] { Frame(NewMoonUtc, 300) };
            var result  = Calc.ComputeFromFrames(session, history.Concat(session).ToList(), MoonUpLat, MoonUpLon);

            var f = result.Filters.Single();
            Assert.True(f.BaselineReady);
            Assert.False(f.FloorIsMoonless);
            Assert.Equal(100.0, f.FloorAdu.Value, 3);           // lowest 10 of the pool are 100 -> P10 = 100
        }

        [Fact]
        public void SeparatesCohorts_ByExposure() {
            // History is all 300s; the 600s session frame has no matching cohort -> null.
            var history = Repeat(20, NewMoonUtc, 100).Select(r => r with { ExposureDuration = 300 }).ToList();
            var session = new[] { Frame(NewMoonUtc, 300, exposure: 600) };
            var result  = Calc.ComputeFromFrames(session, history.Concat(session).ToList(), 40, -75);

            Assert.Null(result.Points.Single().TimesDarkest);
            Assert.False(result.Filters.Single().BaselineReady);
        }
    }
}
