using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using System;
using System.Collections.Generic;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    public class RoofClosedHelperTests {

        private static readonly DateTime T0 = new DateTime(2025, 1, 15, 22, 0, 0);

        private static SessionEvent Evt(string type, DateTime ts) =>
            new SessionEvent { SessionId = "s1", EventType = type, Timestamp = ts };

        // ── GetIntervals ────────────────────────────────────────────────────

        [Fact]
        public void NoEvents_ReturnsEmpty() {
            var intervals = RoofClosedHelper.GetIntervals(null, T0, T0.AddHours(1));
            Assert.Empty(intervals);
        }

        [Fact]
        public void MatchedClosedOpen_ReturnsInterval() {
            var events = new List<SessionEvent> {
                Evt("RoofClosed", T0.AddMinutes(10)),
                Evt("RoofOpen",   T0.AddMinutes(20))
            };
            var intervals = RoofClosedHelper.GetIntervals(events, T0, T0.AddHours(1));
            Assert.Single(intervals);
            Assert.Equal(600, (intervals[0].end - intervals[0].start).TotalSeconds); // 10 min
        }

        [Fact]
        public void OrphanedClosed_ExtendsToWindowEnd() {
            var windowEnd = T0.AddHours(1);
            var events = new List<SessionEvent> {
                Evt("RoofClosed", T0.AddMinutes(30))
            };
            var intervals = RoofClosedHelper.GetIntervals(events, T0, windowEnd);
            Assert.Single(intervals);
            Assert.Equal(T0.AddMinutes(30), intervals[0].start);
            Assert.Equal(windowEnd, intervals[0].end);
        }

        [Fact]
        public void ClosedBeforeWindow_ClampedToWindowStart() {
            var events = new List<SessionEvent> {
                Evt("RoofClosed", T0.AddMinutes(-10)),
                Evt("RoofOpen",   T0.AddMinutes(5))
            };
            var intervals = RoofClosedHelper.GetIntervals(events, T0, T0.AddHours(1));
            Assert.Single(intervals);
            Assert.Equal(T0, intervals[0].start); // Clamped to window start
            Assert.Equal(T0.AddMinutes(5), intervals[0].end);
        }

        [Fact]
        public void OpenAfterWindow_ClampedToWindowEnd() {
            var windowEnd = T0.AddMinutes(30);
            var events = new List<SessionEvent> {
                Evt("RoofClosed", T0.AddMinutes(20)),
                Evt("RoofOpen",   T0.AddMinutes(40))
            };
            var intervals = RoofClosedHelper.GetIntervals(events, T0, windowEnd);
            Assert.Single(intervals);
            Assert.Equal(windowEnd, intervals[0].end); // Clamped to window end
        }

        [Fact]
        public void MultipleClosedOpenPairs() {
            var events = new List<SessionEvent> {
                Evt("RoofClosed", T0.AddMinutes(5)),
                Evt("RoofOpen",   T0.AddMinutes(10)),
                Evt("RoofClosed", T0.AddMinutes(25)),
                Evt("RoofOpen",   T0.AddMinutes(35))
            };
            var intervals = RoofClosedHelper.GetIntervals(events, T0, T0.AddHours(1));
            Assert.Equal(2, intervals.Count);
            Assert.Equal(900, RoofClosedHelper.TotalSeconds(intervals)); // 5min + 10min = 15min = 900s
        }

        // ── TotalSeconds ────────────────────────────────────────────────────

        [Fact]
        public void TotalSeconds_SumsAllIntervals() {
            var intervals = new List<(DateTime start, DateTime end)> {
                (T0, T0.AddMinutes(5)),
                (T0.AddMinutes(20), T0.AddMinutes(30))
            };
            Assert.Equal(900, RoofClosedHelper.TotalSeconds(intervals)); // 5 + 10 = 15 min
        }

        // ── IsEntirelyWithinClosed ──────────────────────────────────────────

        [Fact]
        public void EventInsideClosed_ReturnsTrue() {
            var closedIntervals = new List<(DateTime start, DateTime end)> {
                (T0, T0.AddMinutes(30))
            };
            Assert.True(RoofClosedHelper.IsEntirelyWithinClosed(
                T0.AddMinutes(5), T0.AddMinutes(10), closedIntervals));
        }

        [Fact]
        public void EventOutsideClosed_ReturnsFalse() {
            var closedIntervals = new List<(DateTime start, DateTime end)> {
                (T0, T0.AddMinutes(10))
            };
            Assert.False(RoofClosedHelper.IsEntirelyWithinClosed(
                T0.AddMinutes(15), T0.AddMinutes(20), closedIntervals));
        }

        [Fact]
        public void EventPartiallyOverlapping_ReturnsFalse() {
            var closedIntervals = new List<(DateTime start, DateTime end)> {
                (T0, T0.AddMinutes(10))
            };
            // Event spans 5-15, closed is 0-10 — not entirely within
            Assert.False(RoofClosedHelper.IsEntirelyWithinClosed(
                T0.AddMinutes(5), T0.AddMinutes(15), closedIntervals));
        }

        [Fact]
        public void NoClosed_ReturnsFalse() {
            var closedIntervals = new List<(DateTime start, DateTime end)>();
            Assert.False(RoofClosedHelper.IsEntirelyWithinClosed(
                T0, T0.AddMinutes(5), closedIntervals));
        }

        // ── ExtendForAbortedExposures ───────────────────────────────────────

        [Fact]
        public void AbortedExposure_ExtendsRoofClosedBackwards() {
            var roofIntervals = new List<(DateTime start, DateTime end)> {
                (T0.AddMinutes(10), T0.AddMinutes(30))  // RoofClosed at +10
            };
            var timingEvents = new List<TimingEvent> {
                new TimingEvent {
                    EventType = "AbortedExposure",
                    StartTime = T0.AddMinutes(5),   // Exposure started at +5
                    EndTime = T0.AddMinutes(10),     // Aborted when roof closed at +10
                    DurationSeconds = 300
                }
            };

            var extended = RoofClosedHelper.ExtendForAbortedExposures(roofIntervals, timingEvents);
            Assert.Single(extended);
            Assert.Equal(T0.AddMinutes(5), extended[0].start);  // Extended back to exposure start
            Assert.Equal(T0.AddMinutes(30), extended[0].end);   // End unchanged
        }

        [Fact]
        public void NoAbortedExposure_IntervalsUnchanged() {
            var roofIntervals = new List<(DateTime start, DateTime end)> {
                (T0.AddMinutes(10), T0.AddMinutes(30))
            };
            var timingEvents = new List<TimingEvent> {
                new TimingEvent {
                    EventType = "Exposure",
                    StartTime = T0,
                    EndTime = T0.AddMinutes(5),
                    DurationSeconds = 300
                }
            };

            var extended = RoofClosedHelper.ExtendForAbortedExposures(roofIntervals, timingEvents);
            Assert.Single(extended);
            Assert.Equal(T0.AddMinutes(10), extended[0].start);  // Unchanged
        }

        [Fact]
        public void MultipleAbortedExposures_ExtendsToMostRecentNotEarliest() {
            // Two aborted exposures within the 10-min causal window. The most recent one
            // is the one physically interrupted by the closing roof; extending back to
            // the earlier one would over-attribute idle time to weather rather than
            // imaging window.
            var roofIntervals = new List<(DateTime start, DateTime end)> {
                (T0.AddMinutes(15), T0.AddMinutes(30))
            };
            var timingEvents = new List<TimingEvent> {
                new TimingEvent {
                    EventType = "AbortedExposure",
                    StartTime = T0.AddMinutes(8),    // 7 min before closure — earlier abort
                    EndTime   = T0.AddMinutes(13),
                    DurationSeconds = 300
                },
                new TimingEvent {
                    EventType = "AbortedExposure",
                    StartTime = T0.AddMinutes(13),   // 2 min before closure — the actual cut
                    EndTime   = T0.AddMinutes(15),
                    DurationSeconds = 120
                }
            };

            var extended = RoofClosedHelper.ExtendForAbortedExposures(roofIntervals, timingEvents);
            Assert.Single(extended);
            Assert.Equal(T0.AddMinutes(13), extended[0].start);
            Assert.Equal(T0.AddMinutes(30), extended[0].end);
        }

        [Fact]
        public void AbortedExposure_FarFromRoofClosed_NotExtended() {
            var roofIntervals = new List<(DateTime start, DateTime end)> {
                (T0.AddMinutes(30), T0.AddMinutes(50))
            };
            var timingEvents = new List<TimingEvent> {
                new TimingEvent {
                    EventType = "AbortedExposure",
                    StartTime = T0,                  // Exposure 30 min before roof closed
                    EndTime = T0.AddMinutes(10),
                    DurationSeconds = 600
                }
            };

            var extended = RoofClosedHelper.ExtendForAbortedExposures(roofIntervals, timingEvents);
            Assert.Single(extended);
            Assert.Equal(T0.AddMinutes(30), extended[0].start);  // Not extended — too far away
        }
    }
}
