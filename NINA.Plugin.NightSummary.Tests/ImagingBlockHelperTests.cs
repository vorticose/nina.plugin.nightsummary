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
            IEnumerable<ImageRecord> images, double gapMinutes = 15, IEnumerable<ImageRecord>? sessionImages = null) {

            var t = typeof(ReportGenerator).Assembly.GetType("NINA.Plugin.NightSummary.Reporting.ImagingBlockHelper");
            Assert.NotNull(t);
            var m = t.GetMethod("DetectWindows", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(m);
            var result = m.Invoke(null, new object?[] { images, gapMinutes, sessionImages });
            return (IReadOnlyList<(DateTime, DateTime)>)result!;
        }

        // Timestamp is the exposure start (NINA ExposureStart).
        private static ImageRecord Img(DateTime ts, double exposureSec = 300, string target = "T") =>
            new ImageRecord {
                SessionId        = "S",
                Timestamp        = ts,
                ExposureDuration = exposureSec,
                TargetName       = target,
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
            // Timestamp is the exposure start; the window runs to start + exposure.
            Assert.Equal(t,                 result[0].Start);
            Assert.Equal(t.AddSeconds(300), result[0].End);
        }

        [Fact]
        public void ContiguousFrames_OneWindow() {
            // Five 300s frames every 5 minutes — gap = 0 between exposures, all merged.
            var t0     = new DateTime(2025, 1, 15, 22, 0, 0);
            var images = Enumerable.Range(0, 5).Select(i => Img(t0.AddMinutes(i * 5))).ToList();
            var result = DetectWindows(images);
            Assert.Single(result);
            Assert.Equal(t0,                   result[0].Start);
            Assert.Equal(t0.AddMinutes(5 * 5), result[0].End);
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
                Img(t0.AddMinutes(70)),  // gap = 70 - 10 = 60 min > 15
                Img(t0.AddMinutes(75)),
            };
            var result = DetectWindows(images);
            Assert.Equal(2, result.Count);

            // Window 1 spans first two frames
            Assert.Equal(t0,                   result[0].Start);
            Assert.Equal(t0.AddMinutes(10),    result[0].End);

            // Window 2 spans last two frames
            Assert.Equal(t0.AddMinutes(70), result[1].Start);
            Assert.Equal(t0.AddMinutes(80), result[1].End);
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
            // start(next) - prevEnd = 15 min exactly. Existing EventTimeline code
            // uses `gap <= 15` → merge. Preserve that.
            var t0   = new DateTime(2025, 1, 15, 22, 0, 0);
            // First frame: start t0, exposure 300s → end = t0 + 5min.
            // Second frame starts at t0 + 20min → gap = 15 exactly.
            var images = new List<ImageRecord> {
                Img(t0,                exposureSec: 300),
                Img(t0.AddMinutes(20), exposureSec: 300),
            };
            var result = DetectWindows(images);
            Assert.Single(result);
        }

        [Fact]
        public void GapJustOver15Minutes_DoesNotMerge() {
            // Gap of 15.5 min between start(next) and prev end → splits.
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

        // ── Exposure-start convention and cross-target cap ──────────────────

        [Fact]
        public void NextTargetAfterShortFrames_DoesNotReachBack() {
            // 2026-07-23: Sadr Region 5s frames until 22:25:21, then WR 134_C 600s frames
            // starting 22:28:49. Treating the timestamp as the save time drew WR 134_C from
            // 22:18:49, inside Sadr's window.
            var sadrLast = new DateTime(2026, 7, 23, 22, 25, 21);
            var wrFirst  = new DateTime(2026, 7, 23, 22, 28, 49);
            var sadr = new List<ImageRecord> {
                Img(sadrLast.AddMinutes(-10), 5, "Sadr"),
                Img(sadrLast,                 5, "Sadr"),
            };
            var wr = new List<ImageRecord> {
                Img(wrFirst,                600, "WR"),
                Img(wrFirst.AddSeconds(628), 600, "WR"),
            };
            var session = sadr.Concat(wr).ToList();

            var sadrWin = DetectWindows(sadr, sessionImages: session);
            var wrWin   = DetectWindows(wr,   sessionImages: session);

            Assert.Single(sadrWin);
            Assert.Single(wrWin);
            Assert.Equal(sadrLast.AddSeconds(5), sadrWin[0].End);
            Assert.Equal(wrFirst,                wrWin[0].Start);
            Assert.True(sadrWin[0].End <= wrWin[0].Start);
        }

        [Fact]
        public void LegacySaveTimeStamps_EndCappedAtNextFrame() {
            // Pre-v3.1.0 rows stamped the save time. Read as a start, A's last frame would
            // run 300s past B's first frame. The cap stops A's window at B's first frame.
            var t0 = new DateTime(2025, 1, 15, 22, 0, 0);
            var a = new List<ImageRecord> { Img(t0, 300, "A"), Img(t0.AddMinutes(5), 300, "A") };
            var b = new List<ImageRecord> { Img(t0.AddMinutes(6), 300, "B") };
            var session = a.Concat(b).ToList();

            var aWin = DetectWindows(a, sessionImages: session);
            var bWin = DetectWindows(b, sessionImages: session);

            Assert.Equal(t0.AddMinutes(6), aWin[0].End);
            Assert.Equal(t0.AddMinutes(6), bWin[0].Start);
        }

        [Fact]
        public void WithoutSessionImages_CapsAgainstOwnFramesOnly() {
            var t0 = new DateTime(2025, 1, 15, 22, 0, 0);
            var images = new List<ImageRecord> { Img(t0, 600), Img(t0.AddMinutes(5), 600) };
            var result = DetectWindows(images);
            Assert.Single(result);
            Assert.Equal(t0, result[0].Start);
            Assert.Equal(t0.AddMinutes(15), result[0].End);
        }

        [Fact]
        public void MissingExposure_UsesSixtySecondFallback() {
            var t0 = new DateTime(2025, 1, 15, 22, 0, 0);
            var result = DetectWindows(new List<ImageRecord> { Img(t0, 0) });
            Assert.Equal(t0,                result[0].Start);
            Assert.Equal(t0.AddSeconds(60), result[0].End);
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
