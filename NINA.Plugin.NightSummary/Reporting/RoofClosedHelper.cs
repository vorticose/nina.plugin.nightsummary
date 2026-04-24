using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.NightSummary.Reporting {

    /// <summary>
    /// Computes roof-closed (unsafe) intervals from SessionEvents, clamped to a time window.
    /// Shared by YieldCalculator and the overhead breakdown in ReportGenerator.
    /// </summary>
    internal static class RoofClosedHelper {

        /// <summary>
        /// Returns roof-closed intervals clamped to [windowStart, windowEnd].
        /// An orphaned RoofClosed with no matching RoofOpen is closed at windowEnd.
        /// </summary>
        public static List<(DateTime start, DateTime end)> GetIntervals(
            List<SessionEvent> events, DateTime windowStart, DateTime windowEnd) {

            var roofEvents = (events ?? new List<SessionEvent>())
                .Where(e => e.EventType == "RoofClosed" || e.EventType == "RoofOpen")
                .OrderBy(e => e.Timestamp)
                .ToList();

            var intervals = new List<(DateTime start, DateTime end)>();
            DateTime? closedAt = null;

            foreach (var ev in roofEvents) {
                if (ev.EventType == "RoofClosed") {
                    closedAt = ev.Timestamp;
                } else if (ev.EventType == "RoofOpen" && closedAt.HasValue) {
                    var overlapStart = closedAt.Value < windowStart ? windowStart : closedAt.Value;
                    var overlapEnd   = ev.Timestamp   > windowEnd  ? windowEnd  : ev.Timestamp;
                    if (overlapEnd > overlapStart)
                        intervals.Add((overlapStart, overlapEnd));
                    closedAt = null;
                }
            }

            // Orphaned RoofClosed — no matching RoofOpen before window end
            if (closedAt.HasValue && closedAt.Value < windowEnd) {
                var overlapStart = closedAt.Value < windowStart ? windowStart : closedAt.Value;
                intervals.Add((overlapStart, windowEnd));
            }

            return intervals;
        }

        /// <summary>
        /// Extends roof-closed intervals backwards to cover any aborted exposures that
        /// immediately precede them. When an exposure is aborted by an unsafe trigger,
        /// the partial exposure time is weather-lost — not overhead, not integration.
        /// </summary>
        public static List<(DateTime start, DateTime end)> ExtendForAbortedExposures(
            List<(DateTime start, DateTime end)> roofIntervals, List<TimingEvent> timingEvents) {

            if (roofIntervals.Count == 0 || timingEvents == null)
                return roofIntervals;

            var aborted = timingEvents
                .Where(e => e.EventType == "AbortedExposure")
                .OrderBy(e => e.StartTime)
                .ToList();

            if (aborted.Count == 0)
                return roofIntervals;

            var extended = new List<(DateTime start, DateTime end)>();
            foreach (var interval in roofIntervals) {
                var newStart = interval.start;
                // Look for an aborted exposure that started before this roof-closed interval
                // and was still running (or recently aborted) when the interval began.
                // The exposure start must be within 10 minutes before the roof closure
                // to establish a causal link (unsafe trigger aborted the exposure).
                var match = aborted.FirstOrDefault(a =>
                    a.StartTime < interval.start &&
                    a.StartTime >= interval.start.AddMinutes(-10));
                if (match != null)
                    newStart = match.StartTime;
                extended.Add((newStart, interval.end));
            }
            return extended;
        }

        /// <summary>
        /// Total seconds of roof-closed time within the window.
        /// </summary>
        public static double TotalSeconds(List<(DateTime start, DateTime end)> intervals) {
            return intervals.Sum(i => (i.end - i.start).TotalSeconds);
        }

        /// <summary>
        /// Returns true if the given time range falls entirely within any roof-closed interval.
        /// </summary>
        public static bool IsEntirelyWithinClosed(
            DateTime start, DateTime end, List<(DateTime start, DateTime end)> closedIntervals) {
            return closedIntervals.Any(c => start >= c.start && end <= c.end);
        }
    }
}
