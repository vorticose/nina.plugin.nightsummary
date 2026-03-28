using NINA.Plugin.NightSummary.Reporting;
using System;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    /// <summary>
    /// Tests for the pure static helper methods on ReportGenerator
    /// (formatting and astronomical calculations).
    /// </summary>
    public class ReportGeneratorHelperTests {

        // ── FormatRA ──────────────────────────────────────────────────────────

        [Fact]
        public void FormatRA_Zero_ReturnsZeroHMS() {
            Assert.Equal("00h 00m 0s", ReportGenerator.FormatRA(0.0));
        }

        [Fact]
        public void FormatRA_TypicalValue_FormatsCorrectly() {
            // 5h 34m 32s → 5.575556h
            double ra = 5 + 34.0 / 60 + 32.0 / 3600;
            var result = ReportGenerator.FormatRA(ra);
            Assert.StartsWith("05h 34m", result);
        }

        [Fact]
        public void FormatRA_MaxRA_23h59m() {
            double ra = 23 + 59.0 / 60;
            var result = ReportGenerator.FormatRA(ra);
            Assert.StartsWith("23h 59m", result);
        }

        [Theory]
        [InlineData(1.5,  "01h 30m")]
        [InlineData(12.0, "12h 00m")]
        public void FormatRA_WholeAndHalfHours_FormatCorrectly(double ra, string expectedPrefix) {
            Assert.StartsWith(expectedPrefix, ReportGenerator.FormatRA(ra));
        }

        // ── FormatDec ─────────────────────────────────────────────────────────

        [Fact]
        public void FormatDec_Zero_ReturnsZero() {
            Assert.Equal("+00° 00′ 0″", ReportGenerator.FormatDec(0.0));
        }

        [Fact]
        public void FormatDec_PositiveDec_HasPlusSign() {
            var result = ReportGenerator.FormatDec(45.5);
            Assert.StartsWith("+", result);
        }

        [Fact]
        public void FormatDec_NegativeDec_HasMinusSign() {
            var result = ReportGenerator.FormatDec(-30.25);
            Assert.StartsWith("-", result);
        }

        [Fact]
        public void FormatDec_NinetyCrabs_FormatsCorrectly() {
            var result = ReportGenerator.FormatDec(90.0);
            Assert.StartsWith("+90°", result);
        }

        [Fact]
        public void FormatDec_KnownValue_FormatsCorrectly() {
            // +45° 30′ 0″
            var result = ReportGenerator.FormatDec(45.5);
            Assert.Contains("45°", result);
            Assert.Contains("30′", result);
        }

        // ── FormatDuration ────────────────────────────────────────────────────

        [Fact]
        public void FormatDuration_LessThan60s_ShowsSeconds() {
            Assert.Equal("45s", ReportGenerator.FormatDuration(45));
        }

        [Fact]
        public void FormatDuration_ExactlyOneMinute_ShowsMinutesOnly() {
            Assert.Equal("1m", ReportGenerator.FormatDuration(60));
        }

        [Fact]
        public void FormatDuration_MinutesWithSeconds_ShowsBoth() {
            Assert.Equal("2m 30s", ReportGenerator.FormatDuration(150));
        }

        [Fact]
        public void FormatDuration_ExactMinutes_NoSeconds() {
            Assert.Equal("5m", ReportGenerator.FormatDuration(300));
        }

        [Fact]
        public void FormatDuration_ExactlyOneHour_ShowsHoursOnly() {
            Assert.Equal("1h", ReportGenerator.FormatDuration(3600));
        }

        [Fact]
        public void FormatDuration_HoursAndMinutes_ShowsBoth() {
            Assert.Equal("1h 30m", ReportGenerator.FormatDuration(5400));
        }

        [Fact]
        public void FormatDuration_ExactHours_NoMinutes() {
            Assert.Equal("2h", ReportGenerator.FormatDuration(7200));
        }

        // ── FormatIntegration ─────────────────────────────────────────────────

        [Fact]
        public void FormatIntegration_LessThan1Hour_ShowsMinutes() {
            Assert.Equal("30m", ReportGenerator.FormatIntegration(1800));
        }

        [Fact]
        public void FormatIntegration_MoreThan1Hour_ShowsHours() {
            var result = ReportGenerator.FormatIntegration(5400); // 1.5h
            Assert.Equal("1.5h", result);
        }

        [Fact]
        public void FormatIntegration_Exactly1Hour_ShowsHours() {
            Assert.Equal("1.0h", ReportGenerator.FormatIntegration(3600));
        }

        // ── MoonIllumination ──────────────────────────────────────────────────

        [Fact]
        public void MoonIllumination_AtReferenceNewMoon_IsNearZero() {
            // Reference new moon: 2000-01-06 18:14 UTC
            var newMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
            var illum = ReportGenerator.MoonIllumination(newMoon, out bool waxing);
            Assert.True(illum < 5.0, $"Expected near 0% at new moon, got {illum:F1}%");
            Assert.True(waxing, "Should be waxing just after new moon");
        }

        [Fact]
        public void MoonIllumination_AtApproxFullMoon_IsNear100() {
            // ~14.77 days after reference new moon = full moon
            var fullMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc)
                               .AddDays(14.77);
            var illum = ReportGenerator.MoonIllumination(fullMoon, out bool waxing);
            Assert.True(illum > 95.0, $"Expected near 100% at full moon, got {illum:F1}%");
            Assert.False(waxing, "Should be waning just after full moon");
        }

        [Fact]
        public void MoonIllumination_ReturnsValueBetween0And100() {
            var now = DateTime.UtcNow;
            var illum = ReportGenerator.MoonIllumination(now, out _);
            Assert.InRange(illum, 0.0, 100.0);
        }

        [Fact]
        public void MoonIllumination_LocalTimeHandledSameAsUtc() {
            // Local and UTC times representing the same instant should give same result
            var utcTime   = new DateTime(2024, 3, 15, 0, 0, 0, DateTimeKind.Utc);
            var localTime = utcTime.ToLocalTime();
            var illumUtc   = ReportGenerator.MoonIllumination(utcTime, out _);
            var illumLocal = ReportGenerator.MoonIllumination(localTime, out _);
            Assert.Equal(illumUtc, illumLocal, precision: 1);
        }
    }
}
