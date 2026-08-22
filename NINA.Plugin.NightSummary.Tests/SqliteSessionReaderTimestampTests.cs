using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System;
using System.IO;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    // Regression guard for fix #2 (feature/locale-invariant-report).
    //
    // The dashboard reader (SqliteSessionReader, reached here via
    // SessionDatabase.GetImagesForSession) must parse stored ISO-8601 timestamps
    // with DateTimeStyles.RoundtripKind. The writer serializes via
    // DateTime.ToString("o"), so a UTC-stamped time is persisted as "...Z".
    //
    // Without RoundtripKind, DateTime.Parse silently converts that "Z" instant to
    // the machine's local clock and stamps Kind=Local — which skews altitude charts
    // and the noon-to-noon session-date boundary on positive UTC offsets (the
    // "GMT+" bug class). The Kind assertion below is the timezone-independent
    // discriminator: it fails on a regression regardless of the test machine's zone.
    public class SqliteSessionReaderTimestampTests : IDisposable {

        private readonly string _dbPath;
        private readonly SessionDatabase _db;

        public SqliteSessionReaderTimestampTests() {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ns_ts_test_{Guid.NewGuid():N}.sqlite");
            _db = new SessionDatabase(_dbPath);
        }

        public void Dispose() {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public void GetImagesForSession_PreservesUtcKind_OnRoundTrip() {
            var session = TestDataFactory.MakeSession();
            _db.CreateSession(session);

            // UTC-stamped capture time → persisted as "2026-01-15T22:00:00.0000000Z".
            var utcStamp = new DateTime(2026, 1, 15, 22, 0, 0, DateTimeKind.Utc);
            _db.SaveImageRecord(TestDataFactory.MakeImage(session.SessionId, timestamp: utcStamp));

            var img = Assert.Single(_db.GetImagesForSession(session.SessionId));

            // Primary guard (timezone-independent): without RoundtripKind the reader
            // returns Kind=Local on every machine.
            Assert.Equal(DateTimeKind.Utc, img.Timestamp.Kind);
            // Value must not be shifted off the stored instant (catches the skew on
            // machines with a non-zero UTC offset).
            Assert.Equal(utcStamp, img.Timestamp);
            Assert.Equal(22, img.Timestamp.Hour);
        }
    }
}
