using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.NightSummary.Tests.Fixtures {
    /// <summary>
    /// Builds realistic test data objects with sensible defaults.
    /// All builder methods accept optional overrides for specific test scenarios.
    /// </summary>
    internal static class TestDataFactory {

        public static SessionRecord MakeSession(
            string? sessionId = null,
            DateTime? start = null,
            int skippedExposures = 0) {
            return new SessionRecord {
                SessionId        = sessionId ?? Guid.NewGuid().ToString(),
                SessionStart     = start ?? new DateTime(2025, 1, 15, 21, 0, 0),
                SessionEnd       = (start ?? new DateTime(2025, 1, 15, 21, 0, 0)).AddHours(6),
                ProfileName      = "Test Profile",
                SkippedExposures = skippedExposures,
                CamXSize         = 4656,
                CamYSize         = 3520,
                PixelSizeMicrons = 3.76,
                FocalLengthMm    = 714
            };
        }

        public static ImageRecord MakeImage(
            string sessionId,
            string target      = "M31",
            string filter      = "Ha",
            double hfr         = 2.5,
            double fwhm        = 3.2,
            bool accepted      = true,
            double raHours     = 0.0,
            double decDeg      = 0.0,
            DateTime? timestamp = null) {
            return new ImageRecord {
                SessionId         = sessionId,
                Timestamp         = timestamp ?? new DateTime(2025, 1, 15, 22, 0, 0),
                TargetName        = target,
                Filter            = filter,
                ExposureDuration  = 300,
                HFR               = hfr,
                FWHM              = fwhm,
                Eccentricity      = 0.45,
                StarCount         = 312,
                GuidingRMSTotal   = 0.65,
                GuidingScale      = 1.0,
                Accepted          = accepted,
                RaHours           = raHours,
                DecDegrees        = decDeg,
                FocuserTemp       = 12.5,
                AmbientTemp       = 8.0,
                Gain              = 100,
                Offset            = 10,
                Binning           = 1,
                ImageType         = "LIGHT"
            };
        }

        public static SessionEvent MakeEvent(
            string sessionId,
            string eventType    = "AutoFocus",
            bool afSucceeded    = true,
            DateTime? timestamp = null) {
            return new SessionEvent {
                SessionId   = sessionId,
                Timestamp   = timestamp ?? new DateTime(2025, 1, 15, 22, 30, 0),
                EventType   = eventType,
                Description = $"Test {eventType} event",
                AfSucceeded = afSucceeded,
                AfHfr       = 2.4
            };
        }

        /// <summary>
        /// Builds a complete ReportData for testing.
        /// Targets have RA=0/Dec=0 by default to avoid live HTTP thumbnail calls.
        /// </summary>
        public static ReportData MakeReportData(
            int imageCount    = 10,
            int targetCount   = 1,
            int skippedExp    = 0,
            string[]? targets = null) {

            var sessionId = Guid.NewGuid().ToString();
            var session   = MakeSession(sessionId, skippedExposures: skippedExp);

            var targetNames = targets ?? BuildTargetNames(targetCount);
            var images      = new List<ImageRecord>();
            var imagesPerTarget = Math.Max(1, imageCount / targetNames.Length);

            foreach (var target in targetNames) {
                for (int i = 0; i < imagesPerTarget; i++) {
                    images.Add(MakeImage(sessionId, target: target,
                        hfr: 2.0 + (i * 0.1),
                        raHours: 0.0,    // no coordinates = no thumbnail HTTP calls
                        decDeg:  0.0));
                }
            }

            var events = new List<SessionEvent> { MakeEvent(sessionId) };

            return new ReportData {
                Session                      = session,
                Images                       = images,
                Events                       = events,
                TsData                       = new List<TsTargetData>(),
                CumulativeIntegrationSeconds = new Dictionary<string, double>(),
                SessionHistory               = new Dictionary<string, List<TargetSessionHistory>>(),
                CameraFovWidthDeg            = 2.5,
                CameraFovHeightDeg           = 1.8,
                ObserverLatitude             = 40.7128,
                ObserverLongitude            = -74.0060,
                ActiveProfileId              = "test-profile-id",
                SkippedExposures             = skippedExp
            };
        }

        public static List<ImageRecord> MakeImageSeries(
            string sessionId,
            int count,
            string target = "M31",
            string filter = "Ha") {
            var images = new List<ImageRecord>();
            for (int i = 0; i < count; i++) {
                var img = MakeImage(sessionId, target, filter, hfr: 2.0 + (i * 0.05));
                img.Timestamp = new DateTime(2025, 1, 15, 22, 0, 0).AddMinutes(i * 5);
                images.Add(img);
            }
            return images;
        }

        private static string[] BuildTargetNames(int count) {
            var names = new[] { "M31", "M42", "NGC 7000", "IC 1805", "M81", "M51" };
            var result = new string[Math.Min(count, names.Length)];
            Array.Copy(names, result, result.Length);
            return result;
        }
    }
}
