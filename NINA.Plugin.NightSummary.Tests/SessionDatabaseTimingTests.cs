using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    public class SessionDatabaseTimingTests : IDisposable {

        private readonly string _dbPath;
        private readonly SessionDatabase _db;

        public SessionDatabaseTimingTests() {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ns_timing_test_{Guid.NewGuid():N}.sqlite");
            _db = new SessionDatabase(_dbPath);
        }

        public void Dispose() {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public void SaveAndRetrieve_TimingEvents_RoundTripsCorrectly() {
            var sessionId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;

            var events = new List<TimingEvent> {
                new TimingEvent {
                    EventType = "Exposure",
                    StartTime = now,
                    EndTime = now.AddSeconds(605),
                    DurationSeconds = 605.0,
                    Details = "Exposure 600s, Gain 100"
                },
                new TimingEvent {
                    EventType = "FilterChange",
                    StartTime = now.AddSeconds(610),
                    EndTime = now.AddSeconds(615),
                    DurationSeconds = 5.0,
                    Details = "H"
                },
                new TimingEvent {
                    EventType = "StarDetection",
                    StartTime = now.AddSeconds(606),
                    EndTime = now.AddSeconds(606),
                    DurationSeconds = 0,
                    Details = null
                }
            };

            _db.SaveTimingEvents(sessionId, events);
            var retrieved = _db.GetTimingEventsForSession(sessionId);

            Assert.Equal(3, retrieved.Count);

            var exposure = retrieved.First(e => e.EventType == "Exposure");
            Assert.Equal(605.0, exposure.DurationSeconds);
            Assert.Equal("Exposure 600s, Gain 100", exposure.Details);

            var filter = retrieved.First(e => e.EventType == "FilterChange");
            Assert.Equal("H", filter.Details);
            Assert.Equal(5.0, filter.DurationSeconds);

            var star = retrieved.First(e => e.EventType == "StarDetection");
            Assert.Null(star.Details);
            Assert.Equal(0, star.DurationSeconds);
        }

        [Fact]
        public void SaveTimingEvents_EmptyList_DoesNotThrow() {
            _db.SaveTimingEvents("test-session", new List<TimingEvent>());
            var retrieved = _db.GetTimingEventsForSession("test-session");
            Assert.Empty(retrieved);
        }

        [Fact]
        public void SaveTimingEvents_NullList_DoesNotThrow() {
            _db.SaveTimingEvents("test-session", null);
            var retrieved = _db.GetTimingEventsForSession("test-session");
            Assert.Empty(retrieved);
        }

        [Fact]
        public void GetTimingEvents_NonexistentSession_ReturnsEmpty() {
            var retrieved = _db.GetTimingEventsForSession("nonexistent");
            Assert.Empty(retrieved);
        }

        [Fact]
        public void GetTimingEvents_OrderedByStartTime() {
            var sessionId = "order-test";
            var baseTime = new DateTime(2026, 3, 30, 21, 0, 0);

            var events = new List<TimingEvent> {
                new TimingEvent { EventType = "Dither", StartTime = baseTime.AddSeconds(30), EndTime = baseTime.AddSeconds(45), DurationSeconds = 15 },
                new TimingEvent { EventType = "Exposure", StartTime = baseTime, EndTime = baseTime.AddSeconds(600), DurationSeconds = 600 },
                new TimingEvent { EventType = "FilterChange", StartTime = baseTime.AddSeconds(15), EndTime = baseTime.AddSeconds(20), DurationSeconds = 5 }
            };

            _db.SaveTimingEvents(sessionId, events);
            var retrieved = _db.GetTimingEventsForSession(sessionId);

            Assert.Equal("Exposure", retrieved[0].EventType);
            Assert.Equal("FilterChange", retrieved[1].EventType);
            Assert.Equal("Dither", retrieved[2].EventType);
        }
    }
}
