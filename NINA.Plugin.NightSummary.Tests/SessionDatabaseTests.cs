using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System;
using System.IO;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    public class SessionDatabaseTests : IDisposable {

        private readonly string _dbPath;
        private readonly SessionDatabase _db;

        public SessionDatabaseTests() {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ns_test_{Guid.NewGuid():N}.sqlite");
            _db = new SessionDatabase(_dbPath);
        }

        public void Dispose() {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private SessionRecord CreateTestSession(string? sessionId = null, DateTime? start = null) {
            var session = TestDataFactory.MakeSession(sessionId, start);
            _db.CreateSession(session);
            return session;
        }

        // ── Schema ───────────────────────────────────────────────────────────

        [Fact]
        public void FreshDatabase_CreatesFile() {
            Assert.True(File.Exists(_dbPath));
        }

        // ── Session round-trip ───────────────────────────────────────────────

        [Fact]
        public void SaveAndRetrieve_Session_RoundTripsCorrectly() {
            var sessionId = Guid.NewGuid().ToString();
            var start     = new DateTime(2025, 1, 15, 21, 0, 0);
            var session   = TestDataFactory.MakeSession(sessionId, start);
            _db.CreateSession(session);
            _db.UpdateSessionCameraInfo(sessionId, 4656, 3520, 3.76, 714.0);

            var retrieved = _db.GetSession(sessionId);

            Assert.NotNull(retrieved);
            Assert.Equal(sessionId,     retrieved.SessionId);
            Assert.Equal("Test Profile", retrieved.ProfileName);
            Assert.Equal(4656,           retrieved.CamXSize);
            Assert.Equal(3520,           retrieved.CamYSize);
            Assert.Equal(3.76,           retrieved.PixelSizeMicrons, precision: 2);
            Assert.Equal(714.0,          retrieved.FocalLengthMm,    precision: 1);
        }

        [Fact]
        public void FinalizeSession_UpdatesEndTimeAndSkippedExposures() {
            var sessionId = Guid.NewGuid().ToString();
            var start     = new DateTime(2025, 1, 15, 21, 0, 0);
            var end       = start.AddHours(6);

            CreateTestSession(sessionId, start);
            _db.FinalizeSession(sessionId, end, reportSent: true, skippedExposures: 5);

            var session = _db.GetSession(sessionId);

            Assert.NotNull(session);
            Assert.True(session.ReportSent);
            Assert.Equal(5, session.SkippedExposures);
        }

        // ── SkippedExposures persistence ──────────────────────────────────────

        [Fact]
        public void SkippedExposures_IsPersistedAndRetrieved() {
            var sessionId = Guid.NewGuid().ToString();
            CreateTestSession(sessionId);
            _db.FinalizeSession(sessionId, DateTime.Now.AddHours(1), false, skippedExposures: 3);

            var session = _db.GetSession(sessionId);

            Assert.Equal(3, session!.SkippedExposures);
        }

        [Fact]
        public void SkippedExposures_Zero_IsStoredCorrectly() {
            var sessionId = Guid.NewGuid().ToString();
            CreateTestSession(sessionId);
            _db.FinalizeSession(sessionId, DateTime.Now.AddHours(1), false, skippedExposures: 0);

            var session = _db.GetSession(sessionId);

            Assert.Equal(0, session!.SkippedExposures);
        }

        // ── Image round-trip ─────────────────────────────────────────────────

        [Fact]
        public void SaveAndRetrieve_ImageRecord_RoundTripsKeyFields() {
            var sessionId = Guid.NewGuid().ToString();
            CreateTestSession(sessionId);

            var image = TestDataFactory.MakeImage(sessionId, target: "M31", filter: "Ha", hfr: 2.75);
            _db.SaveImageRecord(image);

            var images = _db.GetImagesForSession(sessionId);

            Assert.Single(images);
            var retrieved = images[0];
            Assert.Equal("M31",  retrieved.TargetName);
            Assert.Equal("Ha",   retrieved.Filter);
            Assert.Equal(2.75,   retrieved.HFR, precision: 2);
            Assert.Equal(300.0,  retrieved.ExposureDuration, precision: 1);
            Assert.True(retrieved.Accepted);
        }

        [Fact]
        public void GetImagesForSession_ReturnsOnlyImagesForThatSession() {
            var session1 = Guid.NewGuid().ToString();
            var session2 = Guid.NewGuid().ToString();

            CreateTestSession(session1);
            CreateTestSession(session2);

            _db.SaveImageRecord(TestDataFactory.MakeImage(session1, target: "M31"));
            _db.SaveImageRecord(TestDataFactory.MakeImage(session1, target: "M31"));
            _db.SaveImageRecord(TestDataFactory.MakeImage(session2, target: "M42"));

            var imagesForSession1 = _db.GetImagesForSession(session1);
            var imagesForSession2 = _db.GetImagesForSession(session2);

            Assert.Equal(2, imagesForSession1.Count);
            Assert.Single(imagesForSession2);
            Assert.All(imagesForSession1, img => Assert.Equal(session1, img.SessionId));
        }

        // ── Events round-trip ─────────────────────────────────────────────────

        [Fact]
        public void SaveAndRetrieve_SessionEvent_RoundTripsCorrectly() {
            var sessionId = Guid.NewGuid().ToString();
            CreateTestSession(sessionId);

            var evt = TestDataFactory.MakeEvent(sessionId, "AutoFocus");
            _db.SaveEvent(evt);

            var events = _db.GetEventsForSession(sessionId);

            Assert.Single(events);
            Assert.Equal("AutoFocus", events[0].EventType);
        }

        // ── SeeingFWHM round-trip ─────────────────────────────────────────────

        [Fact]
        public void SeeingFWHM_IsPersistedAndRetrieved() {
            var sessionId = Guid.NewGuid().ToString();
            CreateTestSession(sessionId);

            var image = TestDataFactory.MakeImage(sessionId, seeingFwhm: 3.14);
            _db.SaveImageRecord(image);

            var images = _db.GetImagesForSession(sessionId);

            Assert.Single(images);
            Assert.NotNull(images[0].SeeingFWHM);
            Assert.Equal(3.14, images[0].SeeingFWHM!.Value, precision: 2);
        }

        [Fact]
        public void SeeingFWHM_Null_IsStoredAsNull() {
            var sessionId = Guid.NewGuid().ToString();
            CreateTestSession(sessionId);

            var image = TestDataFactory.MakeImage(sessionId, seeingFwhm: null);
            _db.SaveImageRecord(image);

            var images = _db.GetImagesForSession(sessionId);

            Assert.Single(images);
            Assert.Null(images[0].SeeingFWHM);
        }

        // ── Image stats round-trip ────────────────────────────────────────────

        [Fact]
        public void ImageStats_ArePersistedAndRetrieved() {
            var sessionId = Guid.NewGuid().ToString();
            CreateTestSession(sessionId);

            var image = TestDataFactory.MakeImage(sessionId);
            image.StatMedian   = 1523.0;
            image.StatMean     = 1580.5;
            image.StatStDev    = 245.3;
            image.StatMAD      = 112.7;
            image.StatMin      = 100;
            image.StatMax      = 65535;
            image.StatBitDepth = 16;
            _db.SaveImageRecord(image);

            var images = _db.GetImagesForSession(sessionId);

            Assert.Single(images);
            var r = images[0];
            Assert.Equal(1523.0, r.StatMedian!.Value, precision: 1);
            Assert.Equal(1580.5, r.StatMean!.Value,   precision: 1);
            Assert.Equal(245.3,  r.StatStDev!.Value,  precision: 1);
            Assert.Equal(112.7,  r.StatMAD!.Value,    precision: 1);
            Assert.Equal(100,    r.StatMin!.Value);
            Assert.Equal(65535,  r.StatMax!.Value);
            Assert.Equal(16,     r.StatBitDepth!.Value);
        }

        [Fact]
        public void ImageStats_Null_AreStoredAsNull() {
            var sessionId = Guid.NewGuid().ToString();
            CreateTestSession(sessionId);

            var image = TestDataFactory.MakeImage(sessionId);
            _db.SaveImageRecord(image);

            var images = _db.GetImagesForSession(sessionId);

            Assert.Single(images);
            var r = images[0];
            Assert.Null(r.StatMedian);
            Assert.Null(r.StatMean);
            Assert.Null(r.StatStDev);
            Assert.Null(r.StatMAD);
            Assert.Null(r.StatMin);
            Assert.Null(r.StatMax);
            Assert.Null(r.StatBitDepth);
        }

        // ── Cumulative integration ────────────────────────────────────────────

        [Fact]
        public void GetCumulativeIntegration_ExcludesCurrentSession() {
            var historicId = Guid.NewGuid().ToString();
            var currentId  = Guid.NewGuid().ToString();

            CreateTestSession(historicId, DateTime.Now.AddDays(-1));
            CreateTestSession(currentId,  DateTime.Now);

            // 10 x 300s = 3000s for historic session
            for (int i = 0; i < 10; i++)
                _db.SaveImageRecord(TestDataFactory.MakeImage(historicId, target: "M31", filter: "Ha"));

            // 5 x 300s for current session — should be excluded from cumulative total
            for (int i = 0; i < 5; i++)
                _db.SaveImageRecord(TestDataFactory.MakeImage(currentId, target: "M31", filter: "Ha"));

            var cumulative = _db.GetCumulativeIntegrationByTarget(excludeSessionId: currentId);

            Assert.True(cumulative.ContainsKey("M31"));
            Assert.Equal(3000.0, cumulative["M31"], precision: 0);
        }
    }
}
