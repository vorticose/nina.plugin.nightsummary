using System;
using System.IO;
using System.Linq;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests.Replay {

    /// <summary>
    /// End-to-end integration tests that replay a recorded NINA session through
    /// Night Summary's real pipeline and assert on the database contents.
    /// </summary>
    public class ReplayIntegrationTests {

        private static string RecordingPath =>
            Path.Combine(AppContext.BaseDirectory, "TestData", "basic-session.json");

        // ── Database correctness ─────────────────────────────────────────────

        [Fact]
        public void Replay_StoresCorrectImageCount() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            // basic-session.json has 8 LIGHT images + 1 DARK
            // Only LIGHT frames should be recorded
            var images = result.GetImages();
            Assert.Equal(8, images.Count);
        }

        [Fact]
        public void Replay_FiltersOutDarkFrames() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            var images = result.GetImages();
            Assert.All(images, img => Assert.Equal("LIGHT", img.ImageType));
        }

        [Fact]
        public void Replay_StoresCorrectTargetNames() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            var targets = result.GetImages().Select(i => i.TargetName).Distinct().OrderBy(t => t).ToList();
            Assert.Equal(2, targets.Count);
            Assert.Contains("M31", targets);
            Assert.Contains("M42", targets);
        }

        [Fact]
        public void Replay_StoresCorrectFilterNames() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            var filters = result.GetImages().Select(i => i.Filter).Distinct().OrderBy(f => f).ToList();
            Assert.Contains("Ha", filters);
            Assert.Contains("OIII", filters);
            Assert.Contains("L", filters);
        }

        [Fact]
        public void Replay_StoresImageMetadataFields() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            // Check the first image has all expected fields populated
            var first = result.GetImages().First();
            Assert.Equal("M31", first.TargetName);
            Assert.Equal("Ha", first.Filter);
            Assert.Equal(300, first.ExposureDuration);
            Assert.True(first.HFR > 0, "HFR should be populated");
            Assert.True(first.StarCount > 0, "StarCount should be populated");
            Assert.True(first.GuidingRMSTotal > 0, "GuidingRMSTotal should be populated");
            Assert.Equal(100, first.Gain);
            Assert.Equal(10, first.Offset);
            Assert.Equal(1, first.Binning);
        }

        [Fact]
        public void Replay_StoresCoordinates() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            var m31Image = result.GetImages().First(i => i.TargetName == "M31");
            Assert.True(Math.Abs(m31Image.RaHours - 0.7122) < 0.001, "RA should match recording");
            Assert.True(Math.Abs(m31Image.DecDegrees - 41.269) < 0.001, "Dec should match recording");

            var m42Image = result.GetImages().First(i => i.TargetName == "M42");
            Assert.True(Math.Abs(m42Image.RaHours - 5.5883) < 0.001);
            Assert.True(Math.Abs(m42Image.DecDegrees - (-5.391)) < 0.001);
        }

        [Fact]
        public void Replay_StoresWeatherData() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            // First image in the recording has weather data
            var first = result.GetImages().First();
            Assert.NotNull(first.AmbientTemp);
            Assert.True(first.AmbientTemp > 0);
        }

        [Fact]
        public void Replay_StoresTelescopePointing() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            var first = result.GetImages().First();
            Assert.NotNull(first.Altitude);
            Assert.True(first.Altitude > 0, "Altitude should be populated");
            Assert.NotNull(first.Azimuth);
            Assert.True(first.Azimuth > 0, "Azimuth should be populated");
        }

        // ── Timestamps use Clock, not wall time ──────────────────────────────

        [Fact]
        public void Replay_TimestampsMatchRecordedTimes() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            var images = result.GetImages();
            var session = result.GetSession();

            // Session start should be approximately the first event time
            Assert.True(session.SessionStart.Year == 2026, "Session start should use Clock time, not wall time");
            Assert.True(session.SessionStart.Month == 3, "Session should be in March from recording");

            // Image timestamps should span hours, not milliseconds
            var timeSpan = images.Last().Timestamp - images.First().Timestamp;
            Assert.True(timeSpan.TotalMinutes > 60, $"Image timestamps should span hours, got {timeSpan.TotalMinutes:F1} minutes");
        }

        // ── Session record ───────────────────────────────────────────────────

        [Fact]
        public void Replay_CreatesSessionWithProfileName() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            var session = result.GetSession();
            Assert.NotNull(session);
            Assert.Equal("Deep Sky Rig", session.ProfileName);
        }

        [Fact]
        public void Replay_StoresCameraInfo() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            var session = result.GetSession();
            Assert.Equal(4656, session.CamXSize);
            Assert.Equal(3520, session.CamYSize);
            Assert.True(Math.Abs(session.PixelSizeMicrons - 3.76) < 0.01);
            Assert.Equal(714, session.FocalLengthMm);
        }

        // ── Event processing ─────────────────────────────────────────────────

        [Fact]
        public void Replay_RecordsAutoFocusEvents() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            var events = result.GetEvents();
            var afEvents = events.Where(e => e.EventType == "AutoFocus").ToList();
            Assert.Equal(2, afEvents.Count);
            Assert.Contains("Ha", afEvents[0].Description);
        }

        [Fact]
        public void Replay_RecordsSafetyStateChanges() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            var events = result.GetEvents();
            // Safety monitor: first push is initial state (not logged),
            // second push (unsafe) is logged as RoofClosed,
            // third push (safe again) is logged as RoofOpen
            var safetyEvents = events.Where(e =>
                e.EventType == "RoofClosed" || e.EventType == "RoofOpen").ToList();
            Assert.True(safetyEvents.Count >= 1, "Should have at least one safety state change event");
        }

        [Fact]
        public void Replay_RecordsMeridianFlip() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            var events = result.GetEvents();
            var flipEvents = events.Where(e => e.EventType == "MeridianFlip").ToList();
            Assert.Single(flipEvents);
            Assert.Contains("successfully", flipEvents[0].Description);
        }

        // ── Cumulative integration ───────────────────────────────────────────

        [Fact]
        public void Replay_CumulativeIntegrationExcludesCurrentSession() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            // GetCumulativeIntegrationByTarget excludes the current session,
            // so with only one session in the database it returns empty.
            // This validates the query runs without error on a fresh database.
            var cumulative = result.GetCumulativeIntegration();
            Assert.NotNull(cumulative);
            Assert.Empty(cumulative);
        }
    }
}
