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
        /// Estimated start-of-exposure for an image record. The recorded
        /// <see cref="ImageRecord.Timestamp"/> is the end-of-exposure save time, so subtract
        /// the exposure duration to approximate when the shutter actually opened.
        /// Falls back to 60s when duration is missing or non-positive.
        /// </summary>
        public static DateTime EstimatedStart(ImageRecord r) =>
            r.Timestamp.AddSeconds(-(r.ExposureDuration > 0 ? r.ExposureDuration : 60));

        /// <summary>
        /// Returns one <c>(Start, End)</c> tuple per contiguous imaging window detected in the
        /// supplied image records, sorted ascending by start time. Frames are merged into a
        /// single window whenever the gap between an estimated-start and the previous frame's
        /// end-timestamp is <paramref name="gapMinutes"/> minutes or less (inclusive — a gap
        /// of exactly the threshold still merges). Default threshold is
        /// <see cref="DefaultGapMinutes"/> (15 minutes), matching the timeline/preview chart.
        /// Returns an empty list for null or empty input.
        /// </summary>
        public static IReadOnlyList<(DateTime Start, DateTime End)> DetectWindows(
            IEnumerable<ImageRecord> images,
            double gapMinutes = DefaultGapMinutes) {

            if (images == null) return Array.Empty<(DateTime, DateTime)>();
            var sorted = images.OrderBy(i => i.Timestamp).ToList();
            if (sorted.Count == 0) return Array.Empty<(DateTime, DateTime)>();

            var windows = new List<(DateTime Start, DateTime End)>();
            var blockStart = EstimatedStart(sorted[0]);
            var blockEnd   = sorted[0].Timestamp;

            for (int i = 1; i <= sorted.Count; i++) {
                if (i < sorted.Count) {
                    var gap = (EstimatedStart(sorted[i]) - blockEnd).TotalMinutes;
                    if (gap <= gapMinutes) {
                        blockEnd = sorted[i].Timestamp;
                        continue;
                    }
                }
                windows.Add((blockStart, blockEnd));
                if (i < sorted.Count) {
                    blockStart = EstimatedStart(sorted[i]);
                    blockEnd   = sorted[i].Timestamp;
                }
            }
            return windows;
        }
    }
}
