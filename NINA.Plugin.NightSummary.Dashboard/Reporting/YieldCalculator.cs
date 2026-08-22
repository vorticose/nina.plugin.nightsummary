using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.NightSummary.Reporting {

    /// <summary>
    /// Calculates imaging yield percentage, accounting for roof-closed time exclusion.
    /// </summary>
    public static class YieldCalculator {

        public class YieldResult {
            public double YieldPct { get; set; }
            public bool HasSafetyMonitor { get; set; }
        }

        /// <summary>
        /// Computes yield as total exposure time ÷ effective imaging window.
        /// The effective window is (first image → last image) minus any roof-closed periods.
        /// </summary>
        public static YieldResult Calculate(
            List<ImageRecord> images,
            List<SessionEvent> events,
            DateTime sessionStart,
            DateTime sessionEnd) {

            var firstImage = images.Any() ? images.Min(i => i.Timestamp) : sessionStart;
            var lastImage  = images.Any() ? images.Max(i => i.Timestamp) : sessionEnd;
            var windowSec  = (lastImage - firstImage).TotalSeconds;

            var roofIntervals = RoofClosedHelper.GetIntervals(events, firstImage, lastImage);
            // Dedup overlapping intervals — duplicate RoofClosed/RoofOpen pairs (e.g.
            // double-subscribed mediator events) would otherwise double-count and shrink
            // the effective window, inflating yield. Mirrors the same fix in
            // ReportGenerator.BuildOverheadBreakdownSection.
            roofIntervals = MergeIntervalsSimple(roofIntervals);
            var roofClosedSec = RoofClosedHelper.TotalSeconds(roofIntervals);

            var effectiveWindowSec = windowSec - roofClosedSec;
            var totalExposureSec   = images.Sum(i => i.ExposureDuration);
            double yieldPct = effectiveWindowSec > 0
                ? Math.Min(totalExposureSec / effectiveWindowSec * 100.0, 100.0)
                : 0;

            return new YieldResult {
                YieldPct = yieldPct,
                HasSafetyMonitor = events?.Any(e => e.EventType == "RoofClosed" || e.EventType == "RoofOpen") ?? false
            };
        }

        // Local copy of the merge so YieldCalculator stays free of a dependency
        // on ReportGenerator's helper. Same algorithm.
        private static List<(DateTime start, DateTime end)> MergeIntervalsSimple(List<(DateTime start, DateTime end)> intervals) {
            var result = new List<(DateTime start, DateTime end)>();
            var sorted = intervals.Where(i => i.end > i.start).OrderBy(i => i.start).ToList();
            if (sorted.Count == 0) return result;
            var curStart = sorted[0].start;
            var curEnd   = sorted[0].end;
            for (int i = 1; i < sorted.Count; i++) {
                if (sorted[i].start <= curEnd) {
                    if (sorted[i].end > curEnd) curEnd = sorted[i].end;
                } else {
                    result.Add((curStart, curEnd));
                    curStart = sorted[i].start;
                    curEnd   = sorted[i].end;
                }
            }
            result.Add((curStart, curEnd));
            return result;
        }
    }
}
