using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Tests for rejected frame tracking:
    /// - SessionDatabase.UpdateImageAccepted (timestamp-based match, tolerance, un-reject)
    /// - ReportGenerator filter table display (Rejected column appears / hidden, TS reason table)
    /// </summary>
    public class RejectedFramesTests : IDisposable {

        private readonly string _dbPath;
        private readonly SessionDatabase _db;
        private readonly ReportGenerator _gen;

        public RejectedFramesTests() {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ns_rejected_{Guid.NewGuid():N}.sqlite");
            _db     = new SessionDatabase(_dbPath);
            _gen    = TestDeps.NewReportGenerator();
            // Minimal settings for report tests
            SettingsManager.Instance.Current.ReportLightMode        = false;
            SettingsManager.Instance.Current.ReportDetailLevel      = 2;
            SettingsManager.Instance.Current.ShowHFRGraph           = false;
            SettingsManager.Instance.Current.ShowStarCountCV        = false;
            SettingsManager.Instance.Current.ShowPerTargetIQ        = false;
            SettingsManager.Instance.Current.ShowSessionHistory     = false;
            SettingsManager.Instance.Current.ShowSkyThumbnails      = false;
            SettingsManager.Instance.Current.ShowAltitudeChart      = false;
            SettingsManager.Instance.Current.ShowNextNightPreview   = false;
            SettingsManager.Instance.Current.ShowTSProgressBars     = false;
            SettingsManager.Instance.Current.AdditionalChartConfigs = "";
            SettingsManager.Instance.Current.ExpandSectionsDefault  = false;
        }

        public void Dispose() {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        // ── DB: UpdateImageAccepted ───────────────────────────────────────────

        [Fact]
        public void UpdateImageAccepted_ExactTimestampMatch_SetsRejected() {
            var session   = TestDataFactory.MakeSession();
            _db.CreateSession(session);
            var ts  = new DateTime(2025, 1, 15, 22, 0, 0);
            var img = TestDataFactory.MakeImage(session.SessionId, timestamp: ts);
            _db.SaveImageRecord(img);

            int rows = _db.UpdateImageAccepted(session.SessionId, ts, accepted: false);

            Assert.Equal(1, rows);
            var loaded = _db.GetImagesForSession(session.SessionId);
            Assert.Single(loaded);
            Assert.False(loaded[0].Accepted);
        }

        [Fact]
        public void UpdateImageAccepted_WithinTolerance_Matches() {
            var session   = TestDataFactory.MakeSession();
            _db.CreateSession(session);
            var stored  = new DateTime(2025, 1, 15, 22, 0, 0);
            var queried = stored.AddSeconds(3); // within 5s tolerance
            var img = TestDataFactory.MakeImage(session.SessionId, timestamp: stored);
            _db.SaveImageRecord(img);

            int rows = _db.UpdateImageAccepted(session.SessionId, queried, accepted: false);

            Assert.Equal(1, rows);
            Assert.False(_db.GetImagesForSession(session.SessionId)[0].Accepted);
        }

        [Fact]
        public void UpdateImageAccepted_OutsideTolerance_NoMatch() {
            var session = TestDataFactory.MakeSession();
            _db.CreateSession(session);
            var stored  = new DateTime(2025, 1, 15, 22, 0, 0);
            var queried = stored.AddSeconds(10); // outside 5s tolerance
            var img = TestDataFactory.MakeImage(session.SessionId, timestamp: stored);
            _db.SaveImageRecord(img);

            int rows = _db.UpdateImageAccepted(session.SessionId, queried, accepted: false);

            Assert.Equal(0, rows);
            Assert.True(_db.GetImagesForSession(session.SessionId)[0].Accepted); // unchanged
        }

        [Fact]
        public void UpdateImageAccepted_UnReject_RestoresAccepted() {
            var session = TestDataFactory.MakeSession();
            _db.CreateSession(session);
            var ts  = new DateTime(2025, 1, 15, 22, 0, 0);
            var img = TestDataFactory.MakeImage(session.SessionId, accepted: false, timestamp: ts);
            _db.SaveImageRecord(img);

            _db.UpdateImageAccepted(session.SessionId, ts, accepted: true);

            Assert.True(_db.GetImagesForSession(session.SessionId)[0].Accepted);
        }

        [Fact]
        public void UpdateImageAccepted_WrongSession_NoMatch() {
            var session = TestDataFactory.MakeSession();
            _db.CreateSession(session);
            var ts  = new DateTime(2025, 1, 15, 22, 0, 0);
            var img = TestDataFactory.MakeImage(session.SessionId, timestamp: ts);
            _db.SaveImageRecord(img);

            int rows = _db.UpdateImageAccepted("different-session-id", ts, accepted: false);

            Assert.Equal(0, rows);
            Assert.True(_db.GetImagesForSession(session.SessionId)[0].Accepted); // unchanged
        }

        [Fact]
        public void UpdateImageAccepted_MultipleImages_OnlyMatchingTimestampUpdated() {
            var session = TestDataFactory.MakeSession();
            _db.CreateSession(session);
            var t1 = new DateTime(2025, 1, 15, 22, 0, 0);
            var t2 = new DateTime(2025, 1, 15, 22, 5, 0);
            _db.SaveImageRecord(TestDataFactory.MakeImage(session.SessionId, timestamp: t1));
            _db.SaveImageRecord(TestDataFactory.MakeImage(session.SessionId, timestamp: t2));

            _db.UpdateImageAccepted(session.SessionId, t1, accepted: false);

            var images = _db.GetImagesForSession(session.SessionId);
            var img1   = images.Find(i => Math.Abs((i.Timestamp - t1).TotalSeconds) < 5);
            var img2   = images.Find(i => Math.Abs((i.Timestamp - t2).TotalSeconds) < 5);
            Assert.NotNull(img1);
            Assert.NotNull(img2);
            Assert.False(img1.Accepted);
            Assert.True(img2.Accepted);
        }

        // ── Report: overview stats ───────────────────────────────────────────

        [Fact]
        public async Task Overview_NoRejections_NoRejectedNote() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);

            var html = await _gen.GenerateHtmlReport(data);

            Assert.DoesNotContain("rejected", html);
        }

        [Fact]
        public async Task Overview_WithRejections_ShowsRejectedNote() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            data.Images[0].Accepted = false;
            data.Images[1].Accepted = false;

            var html = await _gen.GenerateHtmlReport(data);

            Assert.Contains("2 rejected", html);
        }

        [Fact]
        public async Task Overview_WithAbortedAndRejected_ShowsBoth() {
            var data = TestDataFactory.MakeReportData(imageCount: 5, skippedExp: 3);
            data.Images[0].Accepted = false;

            var html = await _gen.GenerateHtmlReport(data);

            Assert.Contains("3 aborted", html);
            Assert.Contains("1 rejected", html);
        }

        // ── Report: filter table ──────────────────────────────────────────────

        [Fact]
        public async Task FilterTable_NoRejections_NoRejectedColumn() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            // All images accepted by default

            var html = await _gen.GenerateHtmlReport(data);

            Assert.DoesNotContain("Rejected", html);
        }

        [Fact]
        public async Task FilterTable_WithRejections_ShowsRejectedColumn() {
            var data   = TestDataFactory.MakeReportData(imageCount: 5);
            data.Images[0].Accepted = false;

            var html = await _gen.GenerateHtmlReport(data);

            Assert.Contains("<th>Rejected</th>", html);
        }

        [Fact]
        public async Task FilterTable_WithRejections_TotalRowShowsCount() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            data.Images[0].Accepted = false;
            data.Images[1].Accepted = false;

            var html = await _gen.GenerateHtmlReport(data);

            // Total row should have bold 2 in the rejected column
            Assert.Contains("<strong>2</strong>", html);
        }

        [Fact]
        public async Task FilterTable_RowWithNoRejections_ShowsDash() {
            // Two filters: Ha has a rejection, OIII does not — OIII row should show "—"
            var data = TestDataFactory.MakeReportData(imageCount: 4);
            data.Images[0].Filter   = "Ha";
            data.Images[0].Accepted = false;
            data.Images[1].Filter   = "Ha";
            data.Images[2].Filter   = "OIII";
            data.Images[3].Filter   = "OIII";

            var html = await _gen.GenerateHtmlReport(data);

            Assert.Contains("—", html);
        }

        // ── Report: TS rejection reason tooltips ─────────────────────────────

        [Fact]
        public async Task FilterTable_NoTsReasons_NoTooltip() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            data.Images[0].Accepted = false;
            // RejectReason is null — manual rejection, no TS reason

            var html = await _gen.GenerateHtmlReport(data);

            // Rejected column appears but no tooltip since no TS reason. `cursor:help` is
            // used legitimately elsewhere in the report (yield stat, CV box, info icons) so
            // assert on the specific rejected-cell pattern: a <td> with a title attribute
            // wrapping a numeric count.
            Assert.Contains("<th>Rejected</th>", html);
            Assert.DoesNotMatch(
                new System.Text.RegularExpressions.Regex(@"<td title='[^']+' style='cursor:help;'>\d+</td>"),
                html);
        }

        [Fact]
        public async Task FilterTable_TsRejections_ShowsTooltipWithReasons() {
            var data = TestDataFactory.MakeReportData(imageCount: 5);
            data.Images[0].Accepted     = false;
            data.Images[0].RejectReason = "HFR too high";
            data.Images[1].Accepted     = false;
            data.Images[1].RejectReason = "HFR too high";
            data.Images[2].Accepted     = false;
            data.Images[2].RejectReason = "Guiding RMS";

            var html = await _gen.GenerateHtmlReport(data);

            Assert.Contains("cursor:help", html);
            Assert.Contains("HFR too high", html);
            Assert.Contains("Guiding RMS", html);
        }

        [Fact]
        public async Task FilterTable_TsReason_HtmlEncodedInTooltip() {
            var data = TestDataFactory.MakeReportData(imageCount: 3);
            data.Images[0].Accepted     = false;
            data.Images[0].RejectReason = "Star count < threshold";

            var html = await _gen.GenerateHtmlReport(data);

            // Special chars must be encoded in the title attribute
            Assert.Contains("Star count &lt; threshold", html);
        }
    }
}
