using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    /// <summary>
    /// Tests for SessionDatabase query methods not covered in SessionDatabaseTests:
    /// GetRecentSessions, GetAllSessions, GetSessionsByDateRange, GetLatestSession,
    /// GetSessionHistoryForTarget, and UpdateImageGradingFromTs.
    /// </summary>
    public class SessionDatabaseQueryTests : IDisposable {

        private readonly string _dbPath;
        private readonly SessionDatabase _db;

        public SessionDatabaseQueryTests() {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ns_query_{Guid.NewGuid():N}.sqlite");
            _db     = new SessionDatabase(_dbPath);
        }

        public void Dispose() {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private SessionRecord CreateSession(DateTime start, string? id = null) {
            var session = TestDataFactory.MakeSession(id, start);
            _db.CreateSession(session);
            return session;
        }

        // ── GetLatestSession ──────────────────────────────────────────────────

        [Fact]
        public void GetLatestSession_EmptyDatabase_ReturnsNull() {
            var result = _db.GetLatestSession();
            Assert.Null(result);
        }

        [Fact]
        public void GetLatestSession_SingleSession_ReturnsThatSession() {
            var session = CreateSession(new DateTime(2025, 3, 1, 21, 0, 0));
            var result  = _db.GetLatestSession();
            Assert.NotNull(result);
            Assert.Equal(session.SessionId, result.SessionId);
        }

        [Fact]
        public void GetLatestSession_MultipleSessions_ReturnsMostRecent() {
            CreateSession(new DateTime(2025, 1, 1, 21, 0, 0));
            CreateSession(new DateTime(2025, 3, 1, 21, 0, 0)); // newest
            CreateSession(new DateTime(2025, 2, 1, 21, 0, 0));
            var result = _db.GetLatestSession();
            Assert.NotNull(result);
            Assert.Equal(new DateTime(2025, 3, 1, 21, 0, 0), result.SessionStart);
        }

        // ── GetAllSessions ────────────────────────────────────────────────────

        [Fact]
        public void GetAllSessions_EmptyDatabase_ReturnsEmptyList() {
            var result = _db.GetAllSessions();
            Assert.Empty(result);
        }

        [Fact]
        public void GetAllSessions_ThreeSessions_ReturnsAllThree() {
            CreateSession(new DateTime(2025, 1, 1, 21, 0, 0));
            CreateSession(new DateTime(2025, 2, 1, 21, 0, 0));
            CreateSession(new DateTime(2025, 3, 1, 21, 0, 0));
            var result = _db.GetAllSessions();
            Assert.Equal(3, result.Count);
        }

        [Fact]
        public void GetAllSessions_ReturnsNewestFirst() {
            CreateSession(new DateTime(2025, 1, 1, 21, 0, 0));
            CreateSession(new DateTime(2025, 3, 1, 21, 0, 0));
            CreateSession(new DateTime(2025, 2, 1, 21, 0, 0));
            var result = _db.GetAllSessions();
            Assert.True(result[0].SessionStart > result[1].SessionStart);
            Assert.True(result[1].SessionStart > result[2].SessionStart);
        }

        // ── GetRecentSessions ─────────────────────────────────────────────────

        [Fact]
        public void GetRecentSessions_EmptyDatabase_ReturnsEmptyList() {
            var result = _db.GetRecentSessions(5);
            Assert.Empty(result);
        }

        [Fact]
        public void GetRecentSessions_LimitRespected() {
            for (int i = 1; i <= 6; i++)
                CreateSession(new DateTime(2025, i, 1, 21, 0, 0));
            var result = _db.GetRecentSessions(3);
            Assert.Equal(3, result.Count);
        }

        [Fact]
        public void GetRecentSessions_ReturnsNewestFirst() {
            CreateSession(new DateTime(2025, 1, 1, 21, 0, 0));
            CreateSession(new DateTime(2025, 3, 1, 21, 0, 0));
            CreateSession(new DateTime(2025, 2, 1, 21, 0, 0));
            var result = _db.GetRecentSessions(3);
            Assert.Equal(new DateTime(2025, 3, 1, 21, 0, 0), result[0].SessionStart);
        }

        [Fact]
        public void GetRecentSessions_LimitGreaterThanCount_ReturnsAll() {
            CreateSession(new DateTime(2025, 1, 1, 21, 0, 0));
            CreateSession(new DateTime(2025, 2, 1, 21, 0, 0));
            var result = _db.GetRecentSessions(10);
            Assert.Equal(2, result.Count);
        }

        // ── GetSessionsByDateRange ────────────────────────────────────────────

        [Fact]
        public void GetSessionsByDateRange_NoSessionsInRange_ReturnsEmpty() {
            CreateSession(new DateTime(2025, 1, 1, 21, 0, 0));
            var result = _db.GetSessionsByDateRange(
                new DateTime(2025, 6, 1), new DateTime(2025, 6, 30));
            Assert.Empty(result);
        }

        [Fact]
        public void GetSessionsByDateRange_SessionsInRange_ReturnsMatches() {
            CreateSession(new DateTime(2025, 1, 15, 21, 0, 0)); // in range
            CreateSession(new DateTime(2025, 2, 15, 21, 0, 0)); // in range
            CreateSession(new DateTime(2025, 6, 15, 21, 0, 0)); // out of range
            var result = _db.GetSessionsByDateRange(
                new DateTime(2025, 1, 1), new DateTime(2025, 3, 1));
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void GetSessionsByDateRange_ExactBoundary_IncludesBoundaryDate() {
            var start = new DateTime(2025, 3, 15, 21, 0, 0);
            CreateSession(start);
            var result = _db.GetSessionsByDateRange(
                new DateTime(2025, 3, 15), new DateTime(2025, 3, 15));
            Assert.Single(result);
        }

        // ── GetSessionHistoryForTarget ────────────────────────────────────────

        [Fact]
        public void GetSessionHistoryForTarget_NoHistoricSessions_ReturnsEmpty() {
            var session = CreateSession(new DateTime(2025, 3, 1, 21, 0, 0));
            _db.SaveImageRecord(TestDataFactory.MakeImage(session.SessionId, target: "M31"));
            // Exclude current session — no history
            var result = _db.GetSessionHistoryForTarget("M31", session.SessionId);
            Assert.Empty(result);
        }

        [Fact]
        public void GetSessionHistoryForTarget_TwoPriorSessions_ReturnsBothNewestFirst() {
            var old1    = CreateSession(new DateTime(2025, 1, 1, 21, 0, 0));
            var old2    = CreateSession(new DateTime(2025, 2, 1, 21, 0, 0));
            var current = CreateSession(new DateTime(2025, 3, 1, 21, 0, 0));

            _db.SaveImageRecord(TestDataFactory.MakeImage(old1.SessionId,    target: "M31"));
            _db.SaveImageRecord(TestDataFactory.MakeImage(old2.SessionId,    target: "M31"));
            _db.SaveImageRecord(TestDataFactory.MakeImage(current.SessionId, target: "M31"));

            var result = _db.GetSessionHistoryForTarget("M31", current.SessionId);
            Assert.Equal(2, result.Count);
            // newest-first: old2 before old1
            Assert.True(result[0].SessionStart > result[1].SessionStart);
        }

        [Fact]
        public void GetSessionHistoryForTarget_ReturnsAllPriorSessions() {
            var sessions = Enumerable.Range(1, 6)
                .Select(i => CreateSession(new DateTime(2025, i, 1, 21, 0, 0)))
                .ToList();
            foreach (var s in sessions)
                _db.SaveImageRecord(TestDataFactory.MakeImage(s.SessionId, target: "M42"));
            var current = sessions.Last();
            var result  = _db.GetSessionHistoryForTarget("M42", current.SessionId);
            Assert.Equal(5, result.Count);
        }

        [Fact]
        public void GetSessionHistoryForTarget_DifferentTarget_NotIncluded() {
            var old     = CreateSession(new DateTime(2025, 1, 1, 21, 0, 0));
            var current = CreateSession(new DateTime(2025, 3, 1, 21, 0, 0));
            _db.SaveImageRecord(TestDataFactory.MakeImage(old.SessionId,     target: "M31"));
            _db.SaveImageRecord(TestDataFactory.MakeImage(current.SessionId, target: "M31"));
            var result = _db.GetSessionHistoryForTarget("M42", current.SessionId);
            Assert.Empty(result);
        }

        [Fact]
        public void GetSessionHistoryForTarget_PopulatesAvgHFR() {
            var old     = CreateSession(new DateTime(2025, 1, 1, 21, 0, 0));
            var current = CreateSession(new DateTime(2025, 3, 1, 21, 0, 0));
            _db.SaveImageRecord(TestDataFactory.MakeImage(old.SessionId, target: "M31", hfr: 2.5));
            _db.SaveImageRecord(TestDataFactory.MakeImage(current.SessionId, target: "M31"));
            var result = _db.GetSessionHistoryForTarget("M31", current.SessionId);
            Assert.Single(result);
            Assert.Equal(2.5, result[0].AvgHFR, precision: 2);
        }

        // ── UpdateImageGradingFromTs ──────────────────────────────────────────

        [Fact]
        public void UpdateImageGradingFromTs_EmptyList_NoException() {
            var session = CreateSession(new DateTime(2025, 3, 1, 21, 0, 0));
            // Should not throw
            _db.UpdateImageGradingFromTs(session.SessionId, new List<(int, int, string)>());
        }

        [Fact]
        public void UpdateImageGradingFromTs_NullList_NoException() {
            var session = CreateSession(new DateTime(2025, 3, 1, 21, 0, 0));
            _db.UpdateImageGradingFromTs(session.SessionId, null);
        }

        [Fact]
        public void UpdateImageGradingFromTs_ValidUpdate_UpdatesAcceptedFlag() {
            var session = CreateSession(new DateTime(2025, 3, 1, 21, 0, 0));
            var image   = TestDataFactory.MakeImage(session.SessionId, accepted: true);
            _db.SaveImageRecord(image);
            var saved   = _db.GetImagesForSession(session.SessionId).First();

            // GradingStatus 2 = rejected in TS convention
            _db.UpdateImageGradingFromTs(session.SessionId,
                new List<(int, int, string)> { (saved.Id, 2, "star_trail") });

            var updated = _db.GetImagesForSession(session.SessionId).First();
            Assert.False(updated.Accepted);
            Assert.Equal("star_trail", updated.RejectReason);
        }

        // ── UpdateSessionCameraInfo ───────────────────────────────────────────

        [Fact]
        public void UpdateSessionCameraInfo_Persists_AllFields() {
            var session = CreateSession(new DateTime(2025, 3, 1, 21, 0, 0));
            _db.UpdateSessionCameraInfo(session.SessionId, 4656, 3520, 3.76, 540.0);

            var updated = _db.GetSession(session.SessionId);
            Assert.Equal(4656,  updated.CamXSize);
            Assert.Equal(3520,  updated.CamYSize);
            Assert.Equal(3.76,  updated.PixelSizeMicrons, precision: 2);
            Assert.Equal(540.0, updated.FocalLengthMm,    precision: 1);
        }

        [Fact]
        public void UpdateSessionCameraInfo_OnlyUpdatesWhenCamXSizeIsZero() {
            // First update should apply (CamXSize starts at 0)
            var session = CreateSession(new DateTime(2025, 3, 1, 21, 0, 0));
            _db.UpdateSessionCameraInfo(session.SessionId, 4656, 3520, 3.76, 540.0);

            // Second update should be ignored (CamXSize is now 4656, not 0)
            _db.UpdateSessionCameraInfo(session.SessionId, 1234, 1000, 1.0, 100.0);

            var result = _db.GetSession(session.SessionId);
            Assert.Equal(4656, result.CamXSize);
        }

        [Fact]
        public void UpdateSessionCameraInfo_UnknownSessionId_NoException() {
            var ex = Record.Exception(() =>
                _db.UpdateSessionCameraInfo("nonexistent-id", 4656, 3520, 3.76, 540.0));
            Assert.Null(ex);
        }
    }
}
