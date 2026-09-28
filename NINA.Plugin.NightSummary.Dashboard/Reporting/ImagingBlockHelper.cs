using System;
using System.Collections.Generic;
using System.Linq;
using NINA.Plugin.NightSummary.Data;

namespace NINA.Plugin.NightSummary.Reporting {
    /// <summary>
    /// Detects contiguous imaging windows from a sequence of <see cref="ImageRecord"/>s by
    /// merging frames whose gap is within <c>gapMinutes</c>. Used by the post-session report
    /// and the live timeline to render the imaging history correctly when a target is
    /// captured in two or more non-continuous windows during a single session (for example,
    /// when the target sets before the meridian and rises again after a long idle gap, or
    /// when the Target Scheduler swaps a target out and back in mid-night).
    /// </summary>
    internal static class ImagingBlockHelper {
        /// <summary>
        /// Default gap threshold in minutes used when callers don't specify one.
        /// Matches the legacy value historically duplicated in
        /// <see cref="EventTimelineGenerator"/> and the per-session preview chart.
        /// </summary>
        public const double DefaultGapMinutes = 15;

        /// <summary>
        /// Assumed exposure length when a record has no positive duration.
        /// </summary>
        private const double FallbackExposureSeconds = 60;

        /// <summary>
        /// Returns one <c>(Start, End)</c> tuple per contiguous imaging window detected in the
        /// supplied image records, sorted ascending by start time.
        ///
        /// <see cref="ImageRecord.Timestamp"/> is the exposure start (NINA ExposureStart, since
        /// v3.1.0), so a frame covers <c>[Timestamp, Timestamp + ExposureDuration]</c>. Rows
        /// written before v3.1.0 stamped the save time instead, which would push a frame's end
        /// past the next exposure. The camera takes one exposure at a time, so each frame's end
        /// is capped at the next frame's start in <paramref name="sessionImages"/> (any target).
        /// That keeps windows of different targets from overlapping under either convention.
        /// Pass the whole session's images there; when omitted, only <paramref name="images"/>
        /// are used for the cap.
        ///
        /// Frames merge into one window when the gap between a frame's start and the previous
        /// frame's end is <paramref name="gapMinutes"/> or less (inclusive). Returns an empty
        /// list for null or empty input.
        /// </summary>
        public static IReadOnlyList<(DateTime Start, DateTime End)> DetectWindows(
            IEnumerable<ImageRecord> images,
            double gapMinutes = DefaultGapMinutes,
            IEnumerable<ImageRecord>? sessionImages = null) {

            if (images == null) return Array.Empty<(DateTime, DateTime)>();
            var sorted = images.OrderBy(i => i.Timestamp).ToList();
            if (sorted.Count == 0) return Array.Empty<(DateTime, DateTime)>();

            var starts = (sessionImages ?? sorted)
                .Select(i => i.Timestamp)
                .Distinct()
                .OrderBy(t => t)
                .ToArray();

            DateTime FrameEnd(ImageRecord r) {
                var end = r.Timestamp.AddSeconds(r.ExposureDuration > 0 ? r.ExposureDuration : FallbackExposureSeconds);
                var next = NextStartAfter(starts, r.Timestamp);
                return next.HasValue && next.Value < end ? next.Value : end;
            }

            var windows = new List<(DateTime Start, DateTime End)>();
            var blockStart = sorted[0].Timestamp;
            var blockEnd   = FrameEnd(sorted[0]);

            for (int i = 1; i <= sorted.Count; i++) {
                if (i < sorted.Count) {
                    var gap = (sorted[i].Timestamp - blockEnd).TotalMinutes;
                    if (gap <= gapMinutes) {
                        var end = FrameEnd(sorted[i]);
                        if (end > blockEnd) blockEnd = end;
                        continue;
                    }
                }
                windows.Add((blockStart, blockEnd));
                if (i < sorted.Count) {
                    blockStart = sorted[i].Timestamp;
                    blockEnd   = FrameEnd(sorted[i]);
                }
            }
            return windows;
        }

        // First entry in the ascending array strictly after t, or null.
        private static DateTime? NextStartAfter(DateTime[] ascending, DateTime t) {
            int lo = 0, hi = ascending.Length;
            while (lo < hi) {
                int mid = (lo + hi) / 2;
                if (ascending[mid] <= t) lo = mid + 1; else hi = mid;
            }
            return lo < ascending.Length ? ascending[lo] : (DateTime?)null;
        }
    }
}
