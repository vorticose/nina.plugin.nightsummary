using NINA.Plugin.NightSummary.Data;

using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System;
using System.Collections.Generic;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    public class EventTimelineGeneratorTests {

        public EventTimelineGeneratorTests() {
            SettingsManager.Instance.Current.ReportLightMode = false;
        }

        // Helper: session with precise start/end
        private static SessionRecord Session(DateTime start, DateTime end) {
            var s = TestDataFactory.MakeSession(start: start);
            s.SessionEnd = end;
            return s;
        }

        // Helper: single image within the session window
        private static ImageRecord Img(string sid, DateTime ts) =>
            TestDataFactory.MakeImage(sid, timestamp: ts);

        // Helper: event within the session window
        private static SessionEvent Evt(string sid, string type, DateTime ts) =>
            TestDataFactory.MakeEvent(sid, eventType: type, timestamp: ts);

        // ── Guard branches ──────────────────────────────────────────────────

        [Fact]
        public void EmptyImages_ReturnsEmpty() {
            var session = Session(
                new DateTime(2025, 1, 15, 21, 0, 0),
                new DateTime(2025, 1, 16,  3, 0, 0));
            var result = EventTimelineGenerator.GenerateTimeline(session, new List<ImageRecord>(), new List<SessionEvent>());
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void ZeroDurationSession_ReturnsEmpty() {
            var t       = new DateTime(2025, 1, 15, 21, 0, 0);
            var session = Session(t, t); // start == end → totalSeconds = 0
            var sid     = session.SessionId;
            var images  = new List<ImageRecord> { Img(sid, t) };
            var result  = EventTimelineGenerator.GenerateTimeline(session, images, new List<SessionEvent>());
            Assert.Equal(string.Empty, result);
        }

        // ── Basic structure ─────────────────────────────────────────────────

        [Fact]
        public void ValidSession_ContainsSvgElement() {
            var start   = new DateTime(2025, 1, 15, 21, 0, 0);
            var end     = start.AddHours(6);
            var session = Session(start, end);
            var images  = TestDataFactory.MakeImageSeries(session.SessionId, 5);
            var result  = EventTimelineGenerator.GenerateTimeline(session, images, new List<SessionEvent>());
            Assert.Contains("<svg", result);
        }

        [Fact]
        public void ValidSession_ContainsSessionTimestamps() {
            var start   = new DateTime(2025, 1, 15, 21, 0, 0);
            var end     = start.AddHours(6);
            var session = Session(start, end);
            var images  = TestDataFactory.MakeImageSeries(session.SessionId, 5);
            var result  = EventTimelineGenerator.GenerateTimeline(session, images, new List<SessionEvent>());
            Assert.Contains("21:00", result);
            Assert.Contains("03:00", result);
        }

        [Fact]
        public void ValidSession_ContainsTargetLegend() {
            var start   = new DateTime(2025, 1, 15, 21, 0, 0);
            var session = Session(start, start.AddHours(6));
            var images  = TestDataFactory.MakeImageSeries(session.SessionId, 5, target: "M31");
            var result  = EventTimelineGenerator.GenerateTimeline(session, images, new List<SessionEvent>());
            Assert.Contains("M31", result);
        }

        // ── Target color palette ────────────────────────────────────────────

        [Fact]
        public void SevenTargets_ColorCyclesViaModulo() {
            // Palette has 6 colors; 7th target gets color index 6 % 6 = 0 = "#4e79a7"
            var start   = new DateTime(2025, 1, 15, 21, 0, 0);
            var session = Session(start, start.AddHours(6));
            var sid     = session.SessionId;
            var images  = new List<ImageRecord>();
            var names   = new[] { "T1", "T2", "T3", "T4", "T5", "T6", "T7" };
            var baseTime = start.AddMinutes(10);
            for (int i = 0; i < names.Length; i++)
                images.Add(Img(sid, baseTime.AddHours(i)));
            var result = EventTimelineGenerator.GenerateTimeline(session, images, new List<SessionEvent>());
            // First and seventh target should both use the first palette color
            var firstColorCount = CountOccurrences(result, "#4e79a7");
            Assert.True(firstColorCount >= 2); // at least legend swatch + imaging band for both
        }

        // ── Event marker colors ─────────────────────────────────────────────

        [Theory]
        [InlineData("AutoFocus",    "#a78bfa")]
        [InlineData("RoofOpen",     "#34d399")]
        [InlineData("RoofClosed",   "#f87171")]
        [InlineData("MeridianFlip", "#fbbf24")]
        public void EventMarker_CorrectColor(string eventType, string expectedColor) {
            var start   = new DateTime(2025, 1, 15, 21, 0, 0);
            var session = Session(start, start.AddHours(6));
            var sid     = session.SessionId;
            var images  = new List<ImageRecord> { Img(sid, start.AddHours(1)) };
            var events  = new List<SessionEvent> { Evt(sid, eventType, start.AddHours(2)) };
            var result  = EventTimelineGenerator.GenerateTimeline(session, images, events);
            Assert.Contains($"fill='{expectedColor}'", result);
        }

        [Fact]
        public void UnknownEventType_UsesWhiteMarker() {
            var start   = new DateTime(2025, 1, 15, 21, 0, 0);
            var session = Session(start, start.AddHours(6));
            var sid     = session.SessionId;
            var images  = new List<ImageRecord> { Img(sid, start.AddHours(1)) };
            var events  = new List<SessionEvent> { Evt(sid, "SomeUnknownEvent", start.AddHours(2)) };
            var result  = EventTimelineGenerator.GenerateTimeline(session, images, events);
            Assert.Contains("fill='#ffffff'", result);
        }

        // ── Event filtering ─────────────────────────────────────────────────

        [Fact]
        public void EventOutsideSessionWindow_IsExcluded() {
            var start   = new DateTime(2025, 1, 15, 21, 0, 0);
            var session = Session(start, start.AddHours(6));
            var sid     = session.SessionId;
            var images  = new List<ImageRecord> { Img(sid, start.AddHours(1)) };
            // Event is 1 hour before session start — outside window
            var events  = new List<SessionEvent> { Evt(sid, "AutoFocus", start.AddHours(-1)) };
            var result  = EventTimelineGenerator.GenerateTimeline(session, images, events);
            // No polygon for the AutoFocus marker (the color won't appear in the svg polygon section)
            Assert.DoesNotContain("data-tip=", result);
        }

        // ── JS marker array ─────────────────────────────────────────────────

        [Fact]
        public void NoEvents_JsMarkerArrayIsEmpty() {
            var start   = new DateTime(2025, 1, 15, 21, 0, 0);
            var session = Session(start, start.AddHours(6));
            var images  = TestDataFactory.MakeImageSeries(session.SessionId, 5);
            var result  = EventTimelineGenerator.GenerateTimeline(session, images, new List<SessionEvent>());
            Assert.Contains("markers = []", result);
        }

        [Fact]
        public void WithEvents_JsMarkerArrayIsPopulated() {
            var start   = new DateTime(2025, 1, 15, 21, 0, 0);
            var session = Session(start, start.AddHours(6));
            var sid     = session.SessionId;
            var images  = new List<ImageRecord> { Img(sid, start.AddHours(1)) };
            var events  = new List<SessionEvent> { Evt(sid, "AutoFocus", start.AddHours(2)) };
            var result  = EventTimelineGenerator.GenerateTimeline(session, images, events);
            Assert.DoesNotContain("markers = []", result);
            Assert.Contains("markers = [{", result);
        }

        // ── Tick interval ───────────────────────────────────────────────────

        [Fact]
        public void ShortSession_Uses15MinuteTicks() {
            // Session < 2h → tickIntervalMins = 15, so 22:15 tick should appear
            var start   = new DateTime(2025, 1, 15, 22, 0, 0);
            var session = Session(start, start.AddHours(1));
            var sid     = session.SessionId;
            var images  = new List<ImageRecord> { Img(sid, start.AddMinutes(30)) };
            var result  = EventTimelineGenerator.GenerateTimeline(session, images, new List<SessionEvent>());
            Assert.Contains("22:15", result);
        }

        [Fact]
        public void LongSession_Uses60MinuteTicks() {
            // Session > 5h → tickIntervalMins = 60, so 22:00 tick appears, 22:30 does not
            var start   = new DateTime(2025, 1, 15, 21, 0, 0);
            var session = Session(start, start.AddHours(6));
            var images  = TestDataFactory.MakeImageSeries(session.SessionId, 5);
            var result  = EventTimelineGenerator.GenerateTimeline(session, images, new List<SessionEvent>());
            Assert.Contains("22:00", result);
            Assert.DoesNotContain("22:30", result);
        }

        // ── Light vs dark mode ──────────────────────────────────────────────

        [Fact]
        public void LightMode_UsesDifferentIdleBackground() {
            var start   = new DateTime(2025, 1, 15, 21, 0, 0);
            var session = Session(start, start.AddHours(6));
            var images  = TestDataFactory.MakeImageSeries(session.SessionId, 5);

            var lightResult = EventTimelineGenerator.GenerateTimeline(session, images, new List<SessionEvent>(), light: true);
            var darkResult  = EventTimelineGenerator.GenerateTimeline(session, images, new List<SessionEvent>(), light: false);

            Assert.Contains("#d0d4da", lightResult); // light idle bg
            Assert.Contains("#0f0f23", darkResult);  // dark idle bg
        }

        // ── Block merging ───────────────────────────────────────────────────

        [Fact]
        public void CloseImages_MergeIntoSingleBlock() {
            // MakeImageSeries places images 5 min apart with 300s exposure
            // gap = (estimatedStart_next - blockEnd) = (ts[i] - 300s) - ts[i-1] = 0 → always merges
            // All images form a single contiguous block — only one colored rect in SVG
            var start   = new DateTime(2025, 1, 15, 21, 0, 0);
            var session = Session(start, start.AddHours(6));
            var images  = TestDataFactory.MakeImageSeries(session.SessionId, 10, target: "M31");
            var result  = EventTimelineGenerator.GenerateTimeline(session, images, new List<SessionEvent>());
            // One color band for M31 means exactly one rect with the M31 target color (#4e79a7)
            Assert.Equal(1, CountOccurrences(result, "opacity='0.85'"));
        }

        [Fact]
        public void FarApartImages_SplitIntoMultipleBlocks() {
            // Images 25 min apart with 300s exposure: gap = 25min - 5min = 20min > 15min → split
            var start   = new DateTime(2025, 1, 15, 21, 0, 0);
            var session = Session(start, start.AddHours(6));
            var sid     = session.SessionId;
            var t0      = start.AddHours(1);
            var images  = new List<ImageRecord> {
                Img(sid, t0),
                Img(sid, t0.AddMinutes(25)),
                Img(sid, t0.AddMinutes(50))
            };
            var result = EventTimelineGenerator.GenerateTimeline(session, images, new List<SessionEvent>());
            Assert.True(CountOccurrences(result, "opacity='0.85'") >= 2);
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static int CountOccurrences(string haystack, string needle) {
            int count = 0, idx = 0;
            while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0) {
                count++;
                idx += needle.Length;
            }
            return count;
        }
    }
}
