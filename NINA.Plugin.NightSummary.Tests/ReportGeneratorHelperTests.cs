using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using System;
using System.Collections.Generic;
using System.Linq;
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

        // ── MergeOverheadIntervals ───────────────────────────────────────────

        private static TimingEvent MakeEvent(int startMin, int endMin, string type = "Test") {
            var baseTime = new DateTime(2026, 3, 31, 0, 0, 0);
            return new TimingEvent {
                EventType = type,
                StartTime = baseTime.AddMinutes(startMin),
                EndTime = baseTime.AddMinutes(endMin),
                DurationSeconds = (endMin - startMin) * 60
            };
        }

        [Fact]
        public void MergeOverheadIntervals_EmptyList_ReturnsZero() {
            Assert.Equal(0, ReportGenerator.MergeOverheadIntervals(new List<TimingEvent>()));
        }

        [Fact]
        public void MergeOverheadIntervals_NoOverlap_ReturnsSumOfDurations() {
            var events = new List<TimingEvent> {
                MakeEvent(0, 5),   // 5 min
                MakeEvent(10, 15)  // 5 min, no overlap
            };
            Assert.Equal(600, ReportGenerator.MergeOverheadIntervals(events)); // 10 min
        }

        [Fact]
        public void MergeOverheadIntervals_FullOverlap_ReturnsSingleSpan() {
            var events = new List<TimingEvent> {
                MakeEvent(0, 10),  // 10 min
                MakeEvent(2, 8)    // fully contained within first
            };
            Assert.Equal(600, ReportGenerator.MergeOverheadIntervals(events)); // 10 min, not 18
        }

        [Fact]
        public void MergeOverheadIntervals_PartialOverlap_MergesCorrectly() {
            var events = new List<TimingEvent> {
                MakeEvent(0, 10),   // 10 min
                MakeEvent(5, 15)    // overlaps by 5 min
            };
            Assert.Equal(900, ReportGenerator.MergeOverheadIntervals(events)); // 15 min, not 20
        }

        [Fact]
        public void MergeOverheadIntervals_MultipleOverlappingGroups() {
            var events = new List<TimingEvent> {
                MakeEvent(0, 5),    // group 1: merged to 0-8
                MakeEvent(3, 8),
                MakeEvent(20, 25),  // group 2: standalone
                MakeEvent(30, 40),  // group 3: merged to 30-42
                MakeEvent(35, 42)
            };
            // group 1: 8 min, group 2: 5 min, group 3: 12 min = 25 min
            Assert.Equal(1500, ReportGenerator.MergeOverheadIntervals(events));
        }

        [Fact]
        public void MergeOverheadIntervals_ConcurrentEvents_CountOnce() {
            // Simulates ImageSave running during CameraDownload — exact same time window
            var events = new List<TimingEvent> {
                MakeEvent(0, 3, "CameraDownload"),
                MakeEvent(0, 3, "ImageSave")
            };
            Assert.Equal(180, ReportGenerator.MergeOverheadIntervals(events)); // 3 min, not 6
        }

        // ── SubtractIntervals ───────────────────────────────────────────────
        // Used to remove exposure overlap from merged overhead so the Overhead
        // Accounted % numerator and denominator are commensurable (both exclude
        // exposure time).

        private static (DateTime start, DateTime end) Iv(int startMin, int endMin) {
            var baseTime = new DateTime(2026, 3, 31, 0, 0, 0);
            return (baseTime.AddMinutes(startMin), baseTime.AddMinutes(endMin));
        }

        [Fact]
        public void SubtractIntervals_EmptyMinus_ReturnsFromUnchanged() {
            var from = new List<(DateTime, DateTime)> { Iv(0, 10) };
            var result = ReportGenerator.SubtractIntervals(from, new List<(DateTime, DateTime)>());
            Assert.Single(result);
            Assert.Equal(10 * 60, (result[0].end - result[0].start).TotalSeconds);
        }

        [Fact]
        public void SubtractIntervals_NoOverlap_ReturnsFromUnchanged() {
            var from  = new List<(DateTime, DateTime)> { Iv(0, 10) };
            var minus = new List<(DateTime, DateTime)> { Iv(20, 30) };
            var result = ReportGenerator.SubtractIntervals(from, minus);
            Assert.Single(result);
        }

        [Fact]
        public void SubtractIntervals_FullyContained_RemovesEntireFromSegment() {
            // 5-min overhead entirely inside a 10-min exposure → contributes 0s
            var from  = new List<(DateTime, DateTime)> { Iv(2, 7) };
            var minus = new List<(DateTime, DateTime)> { Iv(0, 10) };
            var result = ReportGenerator.SubtractIntervals(from, minus);
            Assert.Empty(result);
        }

        [Fact]
        public void SubtractIntervals_PartialOverlap_TrimsTail() {
            // Overhead 0–10 with exposure 5–15 → keeps 0–5
            var from  = new List<(DateTime, DateTime)> { Iv(0, 10) };
            var minus = new List<(DateTime, DateTime)> { Iv(5, 15) };
            var result = ReportGenerator.SubtractIntervals(from, minus);
            Assert.Single(result);
            Assert.Equal(5 * 60, (result[0].end - result[0].start).TotalSeconds);
        }

        [Fact]
        public void SubtractIntervals_MinusInsideFrom_SplitsIntoTwo() {
            // Overhead 0–10 with a 4–6 exposure carved out → 0–4 + 6–10 = 8 min
            var from  = new List<(DateTime, DateTime)> { Iv(0, 10) };
            var minus = new List<(DateTime, DateTime)> { Iv(4, 6) };
            var result = ReportGenerator.SubtractIntervals(from, minus);
            Assert.Equal(2, result.Count);
            var totalMin = result.Sum(r => (r.end - r.start).TotalMinutes);
            Assert.Equal(8, totalMin);
        }

        [Fact]
        public void SubtractIntervals_MultipleMinusIntervals_AllCarvedOut() {
            // Overhead 0–20 with two exposures 2–5 and 12–15 → 4 segments summing to 14 min
            var from  = new List<(DateTime, DateTime)> { Iv(0, 20) };
            var minus = new List<(DateTime, DateTime)> { Iv(2, 5), Iv(12, 15) };
            var result = ReportGenerator.SubtractIntervals(from, minus);
            var totalMin = result.Sum(r => (r.end - r.start).TotalMinutes);
            Assert.Equal(14, totalMin);
        }

        [Fact]
        public void SubtractIntervals_RegressionImageSaveDuringExposure_DoesNotDoubleCount() {
            // Image save 100–105 runs during exposure 90–150 (i.e. concurrent overhead).
            // Without subtraction it would inflate coverage > 100%.
            // After subtraction it contributes 0s — the time is already inside exposure.
            var from  = new List<(DateTime, DateTime)> { Iv(100, 105) };
            var minus = new List<(DateTime, DateTime)> { Iv(90, 150) };
            var result = ReportGenerator.SubtractIntervals(from, minus);
            Assert.Empty(result);
        }
    }
}
