using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests.Replay {

    /// <summary>
    /// Integration tests using a real-world Session Capture recording
    /// from the overnight imaging session of 2026-03-30/31.
    /// </summary>
    public class RealWorldReplayTests {

        private static string RecordingPath =>
            Path.Combine(AppContext.BaseDirectory, "TestData", "capture_2026-03-31.json");

        [Fact]
        public void RealSession_StoresAllLightFrames() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            var images = result.GetImages();
            Assert.Equal(53, images.Count);
            Assert.All(images, img => Assert.Equal("LIGHT", img.ImageType));
        }

        [Fact]
        public void RealSession_CapturesAllThreeTargets() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            var targets = result.GetImages()
                .Select(i => i.TargetName).Distinct().OrderBy(t => t).ToList();
            Assert.Equal(3, targets.Count);
            Assert.Contains("Seagull Nebula", targets);
            Assert.Contains("Lagoon Nebula", targets);
            Assert.Contains("M 101", targets);
        }

        [Fact]
        public void RealSession_ProfileNameCaptured() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            var session = result.GetSession();
            Assert.Equal("CAT91", session.ProfileName);
        }

        [Fact]
        public void RealSession_TimestampsSpanFullSession() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            var images = result.GetImages();
            var span = images.Last().Timestamp - images.First().Timestamp;
            // 9-hour session should have images spanning several hours
            Assert.True(span.TotalHours > 5, $"Expected >5h span, got {span.TotalHours:F1}h");
        }

        [Fact]
        public void RealSession_AutoFocusEventsRecorded() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            var afEvents = result.GetEvents().Where(e => e.EventType == "AutoFocus").ToList();
            Assert.Equal(5, afEvents.Count);
        }

        [Fact]
        public void RealSession_MeridianFlipRecorded() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            var flips = result.GetEvents().Where(e => e.EventType == "MeridianFlip").ToList();
            Assert.Single(flips);
        }

        [Fact]
        public void RealSession_ImageMetadataPopulated() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            // Check a representative image has realistic values
            var img = result.GetImages().First();
            Assert.True(img.HFR > 0 && img.HFR < 10, $"HFR should be realistic, got {img.HFR}");
            Assert.True(img.StarCount > 0, "StarCount should be populated");
            Assert.True(img.ExposureDuration > 0, "ExposureDuration should be populated");
            Assert.True(img.GuidingRMSTotal > 0, "GuidingRMS should be populated");
            Assert.Equal(100, img.Gain);
        }

        [Fact]
        public void RealSession_WeatherDataPopulated() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            var img = result.GetImages().First();
            Assert.NotNull(img.AmbientTemp);
            Assert.NotNull(img.Humidity);
            Assert.NotNull(img.FocuserTemp);
        }

        [Fact]
        public void RealSession_CoordinatesPopulated() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            var img = result.GetImages().First();
            Assert.True(img.RaHours > 0, "RA should be populated");
            Assert.True(Math.Abs(img.DecDegrees) > 0, "Dec should be populated");
        }

        [Fact]
        public void RealSession_RotatorPositionPopulated() {
            using var runner = new SessionReplayRunner(RecordingPath);
            var result = runner.Run();

            var img = result.GetImages().First();
            Assert.NotNull(img.RotatorPosition);
        }

        [Fact]
        public async Task RealSession_GeneratesHtmlReport() {
            using var runner = new SessionReplayRunner(RecordingPath);
            // ASI2600MM Pro: 6248x4176 @ 3.76µm, CAT91 focal length 448mm
            runner.OverrideCameraInfo(6248, 4176, 3.76);
            var result = runner.Run();

            // Build ReportData from replayed database
            var db       = result.Database;
            var session  = result.GetSession();
            var images   = result.GetImages();
            var events   = result.GetEvents();
            var history  = new Dictionary<string, List<TargetSessionHistory>>();
            foreach (var target in images.Select(i => i.TargetName).Distinct())
                history[target] = db.GetSessionHistoryForTarget(target, result.SessionId);

            // Compute FOV from camera specs: plateScale = 206.265 * pixelSize / focalLength
            double pixelSize = 3.76, focalLength = 448;
            double plateScale = 206.265 * pixelSize / focalLength;
            double fovW = plateScale * 6248 / 3600.0;
            double fovH = plateScale * 4176 / 3600.0;

            var reportData = new ReportData {
                Session                      = session,
                Images                       = images,
                Events                       = events,
                TsData                       = new List<TsTargetData>(),
                CumulativeIntegrationSeconds = db.GetCumulativeIntegrationByTarget(result.SessionId),
                SessionHistory               = history,
                CameraFovWidthDeg            = fovW,
                CameraFovHeightDeg           = fovH,
                ObserverLatitude             = 31.547333,
                ObserverLongitude            = -99.382751,
                ActiveProfileId              = "f0ef1e0d-52cf-4973-bc15-4b51652e72b4",
                SkippedExposures             = session.SkippedExposures
            };

            // Generate the HTML report
            var generator = new ReportGenerator();
            var html = await generator.GenerateHtmlReport(reportData);

            Assert.NotNull(html);
            Assert.Contains("<html", html);
            Assert.Contains("Seagull Nebula", html);
            Assert.Contains("Lagoon Nebula", html);
            Assert.Contains("M 101", html);

            // Save to disk for visual comparison
            var outputDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "N.I.N.A.", "Night Summary", "Saved Test Reports");
            Directory.CreateDirectory(outputDir);
            var outputPath = Path.Combine(outputDir, $"replay_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.html");
            await File.WriteAllTextAsync(outputPath, html);
        }
    }
}
