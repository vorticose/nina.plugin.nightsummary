using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    /// <summary>
    /// ImagingBlockHelper is internal — exercise it through reflection to keep the helper
    /// internal-only while still verifying its behavior.
    /// </summary>
    public class ImagingBlockHelperTests {
        private static IReadOnlyList<(DateTime Start, DateTime End)> DetectWindows(
            IEnumerable<ImageRecord> images, double gapMinutes = 15) {

            var t = typeof(ReportGenerator).Assembly.GetType("NINA.Plugin.NightSummary.Reporting.ImagingBlockHelper");
            Assert.NotNull(t);
            var m = t.GetMethod("DetectWindows", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(m);
            var result = m.Invoke(null, new object[] { images, gapMinutes });
            return (IReadOnlyList<(DateTime, DateTime)>)result!;
        }

        private static ImageRecord Img(DateTime ts, double exposureSec = 300) =>
            new ImageRecord {
                SessionId        = "S",
                Timestamp        = ts,
                ExposureDuration = exposureSec,
                TargetName       = "T",
                Filter           = "L"
            };

        // ── Guard branches ──────────────────────────────────────────────────

        [Fact]
        public void NullImages_ReturnsEmpty() {
            var result = DetectWindows(null!);
            Assert.Empty(result);
        }

        [Fact]
        public void EmptyImages_ReturnsEmpty() {
            var result = DetectWindows(new List<ImageRecord>());
            Assert.Empty(result);
        }

        // ── Single window cases ─────────────────────────────────────────────

        [Fact]
        public void SingleImage_ReturnsOneWindow() {
            var t      = new DateTime(2025, 1, 15, 22, 0, 0);
            var images = new List<ImageRecord> { Img(t, exposureSec: 300) };
            var result = DetectWindows(images);
            Assert.Single(result);
            // start ≈ timestamp - exposure
            Assert.Equal(t.AddSeconds(-300), result[0].Start);
            Assert.Equal(t,                  result[0].End);
        }

        [Fact]
        public void ContiguousFrames_OneWindow() {
            // Five 300s frames every 5 minutes — gap = 0 between exposures, all merged.
            var t0     = new DateTime(2025, 1, 15, 22, 0, 0);
            var images = Enumerable.Range(0, 5).Select(i => Img(t0.AddMinutes(i * 5))).ToList();
            var result = DetectWindows(images);
            Assert.Single(result);
            Assert.Equal(t0.AddSeconds(-300),         result[0].Start);
            Assert.Equal(t0.AddMinutes(4 * 5),        result[0].End);
        }

        // ── Multi-window splits ─────────────────────────────────────────────

        [Fact]
        public void LongGap_ProducesTwoWindows() {
            // First window: 22:00 + 22:05 (300s frames, 5min cadence).
            // Gap of 60 minutes (well above 15) → new window.
            // Second window: 23:10 + 23:15.
            var t0     = new DateTime(2025, 1, 15, 22, 0, 0);
            var images = new List<ImageRecord> {
                Img(t0),
                Img(t0.AddMinutes(5)),
                Img(t0.AddMinutes(70)),  // gap = 70 - 5 = 65 min > 15
                Img(t0.AddMinutes(75)),
            };
            var result = DetectWindows(images);
            Assert.Equal(2, result.Count);

            // Window 1 spans first two frames
            Assert.Equal(t0.AddSeconds(-300),  result[0].Start);
            Assert.Equal(t0.AddMinutes(5),     result[0].End);

            // Window 2 spans last two frames
            Assert.Equal(t0.AddMinutes(70).AddSeconds(-300), result[1].Start);
            Assert.Equal(t0.AddMinutes(75),                 result[1].End);
        }

        [Fact]
        public void ThreeWindows_RetainOrder() {
            var t0     = new DateTime(2025, 1, 15, 21, 0, 0);
            var images = new List<ImageRecord> {
                Img(t0),
                Img(t0.AddMinutes(60)),                   // gap 60 min - 5 exposure = 55 min
                Img(t0.AddMinutes(60).AddMinutes(60)),    // gap 60 min - 5 = 55 min
            };
            var result = DetectWindows(images);
            Assert.Equal(3, result.Count);
            // Each is a single-frame window of length ≈ exposure
            Assert.True(result[0].Start < result[1].Start);
            Assert.True(result[1].Start < result[2].Start);
        }

        // ── Boundary behavior (preserve legacy <= 15 inclusive) ─────────────

        [Fact]
        public void GapExactly15Minutes_StillMerges() {
            // EstimatedStart(next) - prevEnd = 15 min exactly. Existing EventTimeline code
            // uses `gap <= 15` → merge. Preserve that.
            var t0   = new DateTime(2025, 1, 15, 22, 0, 0);
            // First frame: timestamp t0, exposure 300s → estStart = t0 - 5min, end = t0.
            // Second frame: estStart = t0 + 15min → gap = 15 exactly.
            // For estStart(2) = t0+15min, exposure 300s, timestamp = t0+15min+5min = t0+20min.
            var images = new List<ImageRecord> {
                Img(t0,                exposureSec: 300),
                Img(t0.AddMinutes(20), exposureSec: 300),
            };
            var result = DetectWindows(images);
            Assert.Single(result);
        }

        [Fact]
        public void GapJustOver15Minutes_DoesNotMerge() {
            // Gap of 15.5 min between estStart(next) and prev end → splits.
            var t0     = new DateTime(2025, 1, 15, 22, 0, 0);
            var images = new List<ImageRecord> {
                Img(t0,                exposureSec: 300),
                Img(t0.AddMinutes(20).AddSeconds(30), exposureSec: 300),
            };
            var result = DetectWindows(images);
            Assert.Equal(2, result.Count);
        }

        // ── Configurable threshold ──────────────────────────────────────────

        [Fact]
        public void CustomGapMinutes_HonorsThreshold() {
            var t0     = new DateTime(2025, 1, 15, 22, 0, 0);
            var images = new List<ImageRecord> {
                Img(t0,                exposureSec: 300),
                Img(t0.AddMinutes(60), exposureSec: 300),  // 55 min gap
            };

            // gapMinutes = 60 → merges since 55 ≤ 60
            var merged = DetectWindows(images, gapMinutes: 60);
            Assert.Single(merged);

            // gapMinutes = 10 → splits since 55 > 10
            var split = DetectWindows(images, gapMinutes: 10);
            Assert.Equal(2, split.Count);
        }

        // ── Sort independence ───────────────────────────────────────────────

        [Fact]
        public void UnsortedInput_StillProducesSortedWindows() {
            var t0     = new DateTime(2025, 1, 15, 22, 0, 0);
            var images = new List<ImageRecord> {
                Img(t0.AddMinutes(70)),    // window 2 frame
                Img(t0),                   // window 1 frame
                Img(t0.AddMinutes(75)),    // window 2 frame
                Img(t0.AddMinutes(5)),     // window 1 frame
            };
            var result = DetectWindows(images);
            Assert.Equal(2, result.Count);
            Assert.True(result[0].Start < result[1].Start);
        }
    }
}
