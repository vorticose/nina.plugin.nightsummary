using NINA.Plugin.NightSummary.Reporting;
using System;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    public class AltitudeCalculatorTests {

        // Mid-latitude observer (Philadelphia-ish) used across most tests
        private const double Lat  =  40.0;
        private const double Lon  = -75.0;

        // A known winter night for reference
        private static readonly DateTime WinterNight = new DateTime(2025, 1, 15, 22, 0, 0);

        // ── AngularSeparation ───────────────────────────────────────────────

        [Fact]
        public void AngularSeparation_SamePoint_IsZero() {
            var sep = AltitudeCalculator.AngularSeparation(5.0, 30.0, 5.0, 30.0);
            Assert.InRange(sep, 0.0, 0.001);
        }

        [Fact]
        public void AngularSeparation_NorthToSouthPole_Is180() {
            var sep = AltitudeCalculator.AngularSeparation(0.0, 90.0, 0.0, -90.0);
            Assert.InRange(sep, 179.99, 180.01);
        }

        [Fact]
        public void AngularSeparation_IsSymmetric() {
            var sep1 = AltitudeCalculator.AngularSeparation(2.0, 30.0, 8.0, -15.0);
            var sep2 = AltitudeCalculator.AngularSeparation(8.0, -15.0, 2.0,  30.0);
            Assert.InRange(Math.Abs(sep1 - sep2), 0.0, 0.001);
        }

        [Fact]
        public void AngularSeparation_ResultIsNonNegative() {
            var sep = AltitudeCalculator.AngularSeparation(0.0, 0.0, 12.0, 0.0);
            Assert.True(sep >= 0);
        }

        // ── GetAltitude ─────────────────────────────────────────────────────

        [Fact]
        public void GetAltitude_ResultInValidRange() {
            var alt = AltitudeCalculator.GetAltitude(5.0, 30.0, Lat, Lon, WinterNight);
            Assert.InRange(alt, -90.0, 90.0);
        }

        [Fact]
        public void GetAltitude_CircumpolarStar_AltitudeApproxLat() {
            // Polaris (RA≈2.53h, Dec≈+89.26°) at lat=45° is always ≈45° altitude
            // Min/max: lat ± (90 - dec) = 44.26°..45.74° → assert within ±3°
            double alt = AltitudeCalculator.GetAltitude(2.53, 89.26, 45.0, 0.0, WinterNight);
            Assert.InRange(alt, 43.0, 47.0);
        }

        [Fact]
        public void GetAltitude_DeepSouthernObject_BelowHorizon() {
            // Object at dec=-80° from lat=+45° cannot rise above horizon
            double alt = AltitudeCalculator.GetAltitude(0.0, -80.0, 45.0, 0.0, WinterNight);
            Assert.True(alt < 0);
        }

        // ── GetAltitudeCurve ────────────────────────────────────────────────

        [Fact]
        public void GetAltitudeCurve_AlwaysIncludesEndpoint() {
            var start = new DateTime(2025, 1, 15, 22, 0, 0);
            var end   = start.AddMinutes(65); // 65 min doesn't land on 10-min step
            var curve = AltitudeCalculator.GetAltitudeCurve(5.0, 30.0, Lat, Lon, start, end, stepMinutes: 10);
            Assert.Equal(end, curve[curve.Count - 1].Time);
        }

        [Fact]
        public void GetAltitudeCurve_AllAltitudesInValidRange() {
            var start = new DateTime(2025, 1, 15, 22, 0, 0);
            var end   = start.AddHours(3);
            var curve = AltitudeCalculator.GetAltitudeCurve(5.0, 30.0, Lat, Lon, start, end);
            foreach (var (_, alt) in curve)
                Assert.InRange(alt, -90.0, 90.0);
        }

        [Fact]
        public void GetAltitudeCurve_SameStartAndEnd_ReturnsAtLeastOnePoint() {
            var t     = new DateTime(2025, 1, 15, 22, 0, 0);
            var curve = AltitudeCalculator.GetAltitudeCurve(5.0, 30.0, Lat, Lon, t, t);
            Assert.True(curve.Count >= 1);
        }

        [Fact]
        public void GetAltitudeCurve_TimestampsAreMonotonicallyIncreasing() {
            var start = new DateTime(2025, 1, 15, 22, 0, 0);
            var end   = start.AddHours(2);
            var curve = AltitudeCalculator.GetAltitudeCurve(5.0, 30.0, Lat, Lon, start, end);
            for (int i = 1; i < curve.Count; i++)
                Assert.True(curve[i].Time >= curve[i - 1].Time);
        }

        [Fact]
        public void GetAltitudeCurve_FirstPointMatchesStart() {
            var start = new DateTime(2025, 1, 15, 22, 0, 0);
            var end   = start.AddHours(2);
            var curve = AltitudeCalculator.GetAltitudeCurve(5.0, 30.0, Lat, Lon, start, end);
            Assert.Equal(start, curve[0].Time);
        }

        // ── GetMoonAltitudeCurve ────────────────────────────────────────────

        [Fact]
        public void GetMoonAltitudeCurve_IsNonEmpty() {
            var start = new DateTime(2025, 1, 15, 22, 0, 0);
            var curve = AltitudeCalculator.GetMoonAltitudeCurve(Lat, Lon, start, start.AddHours(3));
            Assert.True(curve.Count > 0);
        }

        [Fact]
        public void GetMoonAltitudeCurve_AllAltitudesInValidRange() {
            var start = new DateTime(2025, 1, 15, 22, 0, 0);
            var curve = AltitudeCalculator.GetMoonAltitudeCurve(Lat, Lon, start, start.AddHours(3));
            foreach (var (_, alt) in curve)
                Assert.InRange(alt, -90.0, 90.0);
        }

        [Fact]
        public void GetMoonAltitudeCurve_AlwaysIncludesEndpoint() {
            var start = new DateTime(2025, 1, 15, 22, 0, 0);
            var end   = start.AddMinutes(65);
            var curve = AltitudeCalculator.GetMoonAltitudeCurve(Lat, Lon, start, end, stepMinutes: 10);
            Assert.Equal(end, curve[curve.Count - 1].Time);
        }

        // ── GetSunPosition ──────────────────────────────────────────────────

        [Fact]
        public void GetSunPosition_RaInValidRange() {
            var (ra, _) = AltitudeCalculator.GetSunPosition(new DateTime(2025, 6, 21, 12, 0, 0, DateTimeKind.Utc));
            Assert.InRange(ra, 0.0, 24.0);
        }

        [Fact]
        public void GetSunPosition_DecInValidRange() {
            var (_, dec) = AltitudeCalculator.GetSunPosition(new DateTime(2025, 6, 21, 12, 0, 0, DateTimeKind.Utc));
            Assert.InRange(dec, -24.0, 24.0);
        }

        [Fact]
        public void GetSunPosition_SummerSolstice_DecIsPositive() {
            // Jun 21 — sun Dec ≈ +23.4°
            var (_, dec) = AltitudeCalculator.GetSunPosition(new DateTime(2025, 6, 21, 12, 0, 0, DateTimeKind.Utc));
            Assert.True(dec > 20.0);
        }

        [Fact]
        public void GetSunPosition_WinterSolstice_DecIsNegative() {
            // Dec 21 — sun Dec ≈ -23.4°
            var (_, dec) = AltitudeCalculator.GetSunPosition(new DateTime(2025, 12, 21, 12, 0, 0, DateTimeKind.Utc));
            Assert.True(dec < -20.0);
        }

        // ── GetSunAltitude ──────────────────────────────────────────────────

        [Fact]
        public void GetSunAltitude_ResultInValidRange() {
            var alt = AltitudeCalculator.GetSunAltitude(Lat, Lon, WinterNight);
            Assert.InRange(alt, -90.0, 90.0);
        }

        [Fact]
        public void GetSunAltitude_AtNight_IsBelowHorizon() {
            // 22:00 local in January at mid-latitude — sun well below horizon
            var alt = AltitudeCalculator.GetSunAltitude(Lat, 0.0, new DateTime(2025, 1, 15, 22, 0, 0));
            Assert.True(alt < -10.0);
        }

        // ── FindNightWindow ─────────────────────────────────────────────────

        [Fact]
        public void FindNightWindow_SunsetBeforeSunrise() {
            var (sunset, sunrise) = AltitudeCalculator.FindNightWindow(Lat, Lon, new DateTime(2025, 1, 15, 21, 0, 0));
            Assert.True(sunset < sunrise);
        }

        [Fact]
        public void FindNightWindow_WindowLengthIsReasonable() {
            // Mid-latitude winter night: 8-16 hours between sunset and sunrise
            var (sunset, sunrise) = AltitudeCalculator.FindNightWindow(Lat, Lon, new DateTime(2025, 1, 15, 21, 0, 0));
            var hours = (sunrise - sunset).TotalHours;
            Assert.InRange(hours, 8.0, 16.0);
        }

        [Fact]
        public void FindNightWindow_NoCrossing_ReturnsFallback() {
            // North Pole in summer — sun never sets; fallback values used
            var sessionStart  = new DateTime(2025, 6, 21, 21, 0, 0);
            var (sunset, sunrise) = AltitudeCalculator.FindNightWindow(90.0, 0.0, sessionStart);
            Assert.Equal(sessionStart.AddHours(-1),  sunset);
            Assert.Equal(sessionStart.AddHours(14), sunrise);
        }

        // ── GetMoonPosition ─────────────────────────────────────────────────

        [Fact]
        public void GetMoonPosition_RaInValidRange() {
            var (ra, _) = AltitudeCalculator.GetMoonPosition(new DateTime(2025, 1, 15, 22, 0, 0, DateTimeKind.Utc));
            Assert.InRange(ra, 0.0, 24.0);
        }

        [Fact]
        public void GetMoonPosition_DecInValidRange() {
            // Moon's max declination ≈ ±28.5° (due to orbital inclination + ecliptic tilt)
            var (_, dec) = AltitudeCalculator.GetMoonPosition(new DateTime(2025, 1, 15, 22, 0, 0, DateTimeKind.Utc));
            Assert.InRange(dec, -30.0, 30.0);
        }

        // ── GetPeakAltitude ────────────────────────────────────────────────

        [Fact]
        public void GetPeakAltitude_ReturnsReasonableValue() {
            // Vega (RA ~18.6h, Dec ~38.8°) from Philadelphia should transit high
            var start = new DateTime(2025, 7, 15, 21, 0, 0);
            var end = start.AddHours(6);
            var peak = AltitudeCalculator.GetPeakAltitude(18.6, 38.8, Lat, Lon, start, end);
            Assert.InRange(peak, 50.0, 90.0);
        }

        [Fact]
        public void GetPeakAltitude_GreaterOrEqualToEndpoints() {
            var start = new DateTime(2025, 1, 15, 21, 0, 0);
            var end = start.AddHours(6);
            var peak = AltitudeCalculator.GetPeakAltitude(5.5, 22.0, Lat, Lon, start, end);
            var startAlt = AltitudeCalculator.GetAltitude(5.5, 22.0, Lat, Lon, start);
            var endAlt = AltitudeCalculator.GetAltitude(5.5, 22.0, Lat, Lon, end);
            Assert.True(peak >= startAlt - 0.1);
            Assert.True(peak >= endAlt - 0.1);
        }

        [Fact]
        public void GetPeakAltitude_SouthernTarget_StaysLow() {
            // A target at Dec -70° from latitude +40° should never get very high
            var start = new DateTime(2025, 1, 15, 21, 0, 0);
            var end = start.AddHours(6);
            var peak = AltitudeCalculator.GetPeakAltitude(3.0, -70.0, Lat, Lon, start, end);
            Assert.True(peak < 0); // Below horizon
        }

        // ── GetMeridianTransitTime ─────────────────────────────────────────

        [Fact]
        public void GetMeridianTransitTime_ReturnsTimeOnSessionNight() {
            // Vega (RA ~18.6h) in mid-July should transit in the evening
            var sessionStart = new DateTime(2025, 7, 15, 21, 0, 0);
            var transit = AltitudeCalculator.GetMeridianTransitTime(18.6, Lon, sessionStart);
            Assert.NotNull(transit);
            // Transit should be somewhere in the evening/night (between 18:00 and 06:00 next day)
            var hour = transit.Value.Hour;
            Assert.True(hour >= 18 || hour < 6, $"Transit at {transit.Value:HH:mm} not in expected evening/night range");
        }

        [Fact]
        public void GetMeridianTransitTime_DriftsEarlierEachNight() {
            // Same target on consecutive nights should transit ~4min earlier
            var night1 = new DateTime(2025, 7, 15, 21, 0, 0);
            var night2 = new DateTime(2025, 7, 16, 21, 0, 0);
            var t1 = AltitudeCalculator.GetMeridianTransitTime(18.6, Lon, night1);
            var t2 = AltitudeCalculator.GetMeridianTransitTime(18.6, Lon, night2);
            Assert.NotNull(t1);
            Assert.NotNull(t2);
            // t2 should be ~3-5 min earlier in clock time (sidereal drift)
            // Compare time-of-day only (both are on different calendar days)
            var tod1 = t1.Value.TimeOfDay;
            var tod2 = t2.Value.TimeOfDay;
            // Handle midnight crossing: normalize to evening hours
            if (tod1.TotalHours < 12) tod1 = tod1.Add(TimeSpan.FromHours(24));
            if (tod2.TotalHours < 12) tod2 = tod2.Add(TimeSpan.FromHours(24));
            var driftMinutes = (tod1 - tod2).TotalMinutes;
            Assert.InRange(driftMinutes, 2.0, 6.0); // ~3.94 min/night expected
        }

        [Fact]
        public void GetMeridianTransitTime_ReturnsNonNull_ForTypicalTarget() {
            // Orion (RA ~5.5h) in January should have a valid transit
            var sessionStart = new DateTime(2025, 1, 15, 21, 0, 0);
            var transit = AltitudeCalculator.GetMeridianTransitTime(5.5, Lon, sessionStart);
            Assert.NotNull(transit);
        }
    }
}
