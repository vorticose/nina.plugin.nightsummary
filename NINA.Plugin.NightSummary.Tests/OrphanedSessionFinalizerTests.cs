using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using System;
using System.Collections.Generic;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    public class OrphanedSessionFinalizerTests {

        private static readonly DateTime SessionStart = new DateTime(2025, 1, 15, 21, 0, 0);

        private static SessionRecord Session(string sid, DateTime start, DateTime end) =>
            new SessionRecord { SessionId = sid, SessionStart = start, SessionEnd = end };

        private static ImageRecord Img(string sid, DateTime ts) =>
            new ImageRecord { SessionId = sid, Timestamp = ts };

        private static SessionEvent Evt(string sid, DateTime ts) =>
            new SessionEvent { SessionId = sid, EventType = "AutoFocus", Timestamp = ts, Description = "" };

        // ── Already finalized ───────────────────────────────────────────────

        [Fact]
        public void AlreadyFinalized_ReturnsNull() {
            var sid     = Guid.NewGuid().ToString();
            var session = Session(sid, SessionStart, SessionStart.AddHours(6));

            var result = OrphanedSessionFinalizer.ResolveEndTime(session, new List<ImageRecord>(), new List<SessionEvent>(), null);

            Assert.Null(result);
        }

        [Fact]
        public void SessionEndEqualsSessionStart_TreatedAsOrphaned() {
            var sid     = Guid.NewGuid().ToString();
            var session = Session(sid, SessionStart, SessionStart);
            var images  = new List<ImageRecord> { Img(sid, SessionStart.AddHours(1)) };

            var result = OrphanedSessionFinalizer.ResolveEndTime(session, images, new List<SessionEvent>(), null);

            Assert.Equal(SessionStart.AddHours(1), result);
        }

        // ── Currently live session ──────────────────────────────────────────

        [Fact]
        public void CurrentlyLiveSession_ReturnsNull() {
            var sid     = Guid.NewGuid().ToString();
            var session = Session(sid, SessionStart, DateTime.MinValue);
            var images  = new List<ImageRecord> { Img(sid, SessionStart.AddHours(1)) };

            var result = OrphanedSessionFinalizer.ResolveEndTime(session, images, new List<SessionEvent>(), sid);

            Assert.Null(result);
        }

        [Fact]
        public void OtherSessionLive_StillFinalizesThisOne() {
            var sid     = Guid.NewGuid().ToString();
            var session = Session(sid, SessionStart, DateTime.MinValue);
            var images  = new List<ImageRecord> { Img(sid, SessionStart.AddHours(1)) };

            var result = OrphanedSessionFinalizer.ResolveEndTime(session, images, new List<SessionEvent>(), "some-other-session-id");

            Assert.Equal(SessionStart.AddHours(1), result);
        }

        [Fact]
        public void NullCurrentLiveSessionId_DoesNotThrow() {
            var sid     = Guid.NewGuid().ToString();
            var session = Session(sid, SessionStart, DateTime.MinValue);
            var images  = new List<ImageRecord> { Img(sid, SessionStart.AddHours(1)) };

            var result = OrphanedSessionFinalizer.ResolveEndTime(session, images, new List<SessionEvent>(), null);

            Assert.Equal(SessionStart.AddHours(1), result);
        }

        // ── Deriving the end time ───────────────────────────────────────────

        [Fact]
        public void OrphanedWithImagesOnly_UsesLastImageTimestamp() {
            var sid     = Guid.NewGuid().ToString();
            var session = Session(sid, SessionStart, DateTime.MinValue);
            var images  = new List<ImageRecord> {
                Img(sid, SessionStart.AddMinutes(30)),
                Img(sid, SessionStart.AddHours(2)),
                Img(sid, SessionStart.AddHours(1))
            };

            var result = OrphanedSessionFinalizer.ResolveEndTime(session, images, new List<SessionEvent>(), null);

            Assert.Equal(SessionStart.AddHours(2), result);
        }

        [Fact]
        public void OrphanedWithEventsOnly_UsesLastEventTimestamp() {
            var sid     = Guid.NewGuid().ToString();
            var session = Session(sid, SessionStart, DateTime.MinValue);
            var events  = new List<SessionEvent> {
                Evt(sid, SessionStart.AddMinutes(45)),
                Evt(sid, SessionStart.AddHours(3))
            };

            var result = OrphanedSessionFinalizer.ResolveEndTime(session, new List<ImageRecord>(), events, null);

            Assert.Equal(SessionStart.AddHours(3), result);
        }

        [Fact]
        public void OrphanedWithImagesAndEvents_UsesTheLaterOfTheTwo() {
            var sid     = Guid.NewGuid().ToString();
            var session = Session(sid, SessionStart, DateTime.MinValue);
            var images  = new List<ImageRecord> { Img(sid, SessionStart.AddHours(1)) };
            var events  = new List<SessionEvent> { Evt(sid, SessionStart.AddHours(4)) };

            var result = OrphanedSessionFinalizer.ResolveEndTime(session, images, events, null);

            Assert.Equal(SessionStart.AddHours(4), result);
        }

        [Fact]
        public void OrphanedWithNoActivity_FallsBackToSessionStart() {
            var sid     = Guid.NewGuid().ToString();
            var session = Session(sid, SessionStart, DateTime.MinValue);

            var result = OrphanedSessionFinalizer.ResolveEndTime(session, new List<ImageRecord>(), new List<SessionEvent>(), null);

            Assert.Equal(SessionStart, result);
        }
    }
}
