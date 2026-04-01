using NINA.Plugin.NightSummary.Data;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    public class NinaLogParserTests : IDisposable {

        private readonly string _logPath;

        public NinaLogParserTests() {
            _logPath = Path.Combine(Path.GetTempPath(), $"nina_test_{Guid.NewGuid():N}.log");
            File.WriteAllText(_logPath, TestLogContent);
        }

        public void Dispose() {
            if (File.Exists(_logPath)) File.Delete(_logPath);
        }

        private static readonly DateTime SessionStart = new DateTime(2026, 3, 30, 21, 30, 0);
        private static readonly DateTime SessionEnd   = new DateTime(2026, 3, 30, 22, 30, 0);

        // Representative log excerpt from a real NINA session — covers all event types
        private const string TestLogContent = @"----------------------------------------------------------------------
--------------N.I.N.A. - Nighttime Imaging 'N' Astronomy--------------
--------------------------Version 3.2.0.9001--------------------------
-------------------------2026-03-30T21:21:13--------------------------
----------------------------------------------------------------------
2026-03-30T21:34:20.9148|INFO|SequenceItem.cs|Run|208|Starting Category: Focuser, Item: MoveFocuserByTemperature, Slope: -8.5545, Intercept 31749.69
2026-03-30T21:34:27.0761|INFO|SequenceItem.cs|Run|254|Finishing Category: Focuser, Item: MoveFocuserByTemperature, Slope: -8.5545, Intercept 31749.69
2026-03-30T21:35:41.9665|INFO|CenteringSolver.cs|Center|99|Centering Solver - Scope Position: RA: 07:07:02; Dec: -11 06' 51""; Separation RA: -00:04:41; Dec: 00 23' 47""; Distance: 01 12' 49""; Threshold: 1
2026-03-30T21:36:04.2870|INFO|CenteringSolver.cs|Center|99|Centering Solver - Scope Position: RA: 07:07:02; Dec: -11 06' 52""; Separation RA: 00:00:00; Dec: 00 01' 19""; Distance: 00 01' 19""; Threshold: 1
2026-03-30T21:36:26.1879|INFO|CenteringSolver.cs|Center|99|Centering Solver - Scope Position: RA: 07:07:02; Dec: -11 06' 53""; Separation RA: 00:00:00; Dec: 00 00' 03""; Distance: 00 00' 03""; Threshold: 1
2026-03-30T21:36:26.1887|INFO|CenteringSolver.cs|Center|165|Restoring filter to L after centering
2026-03-30T21:36:26.4693|INFO|SequenceItem.cs|Run|208|Starting Category: Focuser, Item: RunAutofocus
2026-03-30T21:38:45.8202|INFO|SequenceItem.cs|Run|254|Finishing Category: Focuser, Item: RunAutofocus
2026-03-30T21:38:45.8577|INFO|SequenceItem.cs|Run|208|Starting Category: , Item: Dither
2026-03-30T21:38:45.8759|INFO|SequenceItem.cs|Run|254|Finishing Category: , Item: Dither
2026-03-30T21:38:45.8894|INFO|SequenceItem.cs|Run|208|Starting Category: Scheduler, Item: SwitchFilter, Filter: S
2026-03-30T21:38:50.0272|INFO|SequenceItem.cs|Run|254|Finishing Category: Scheduler, Item: SwitchFilter, Filter: S
2026-03-30T21:39:06.8545|INFO|SequenceItem.cs|Run|208|Starting Category: Scheduler, Item: TakeExposure, ExposureTime 600, Gain 100, Offset 19, ImageType LIGHT, Binning 1x1
2026-03-30T21:49:11.8742|INFO|SequenceItem.cs|Run|254|Finishing Category: Scheduler, Item: TakeExposure, ExposureTime 600, Gain 100, Offset 19, ImageType LIGHT, Binning 1x1
2026-03-30T21:49:11.9040|INFO|ImageSolver.cs|Solve|41|Platesolving with parameters: FocalLength: 448 PixelSize: 3.76 SearchRadius: 30 BlindFailoverEnabled: True Regions: 5000 DownSampleFactor: 0 MaxObjects: 500
2026-03-30T21:49:14.0657|INFO|ImageSolver.cs|Solve|54|Platesolve successful: Coordinates: RA: 07:05:49; Dec: -11 04' 20""; Epoch: J2000 - Position Angle: 114.18
2026-03-30T21:49:17.1189|INFO|HocusFocusStarDetection.cs|Detect|413|Average HFR: 1.604065807017856, HFR MAD: 0.064623168208225, Detected Stars 1394, Region: 0
2026-03-30T21:49:22.6443|INFO|ImageSaveController.cs|DoWork|97|Successfully saved file at D:\\Seagull Nebula\S\600.00s\test.fits. Duration Total: 00:00:10.7636414; BeforeSave: 00:00:00.0199465; BeforeFinalizeImageSaved: 00:00:05.5429538; FinalizeSaveTime: 00:00:05.2007394
2026-03-30T21:49:25.8666|INFO|SequenceItem.cs|Run|208|Starting Category: Scheduler, Item: SwitchFilter, Filter: H
2026-03-30T21:49:35.0816|INFO|SequenceItem.cs|Run|254|Finishing Category: Scheduler, Item: SwitchFilter, Filter: H
2026-03-30T21:49:35.1164|INFO|SequenceItem.cs|Run|208|Starting Category: Scheduler, Item: TakeExposure, ExposureTime 600, Gain 100, Offset 19, ImageType LIGHT, Binning 1x1
2026-03-30T21:59:40.2080|INFO|SequenceItem.cs|Run|254|Finishing Category: Scheduler, Item: TakeExposure, ExposureTime 600, Gain 100, Offset 19, ImageType LIGHT, Binning 1x1
2026-03-30T21:59:40.2240|INFO|ImageSolver.cs|Solve|41|Platesolving with parameters: FocalLength: 448 PixelSize: 3.76 SearchRadius: 30
2026-03-30T21:59:43.4680|INFO|ImageSolver.cs|Solve|54|Platesolve successful: Coordinates: RA: 07:05:49; Dec: -11 04' 20""; Epoch: J2000
2026-03-30T21:59:43.1092|INFO|HocusFocusStarDetection.cs|Detect|413|Average HFR: 1.581034271008424, HFR MAD: 0.05368535241124173, Detected Stars 181, Region: 0
2026-03-30T21:59:49.1674|INFO|ImageSaveController.cs|DoWork|97|Successfully saved file at D:\\Seagull Nebula\H\600.00s\test2.fits. Duration Total: 00:00:08.9588223; BeforeSave: 00:00:00.0153293; BeforeFinalizeImageSaved: 00:00:03.2073901; FinalizeSaveTime: 00:00:05.7361014
2026-03-30T21:59:52.1861|INFO|SequenceItem.cs|Run|208|Starting Category: , Item: Dither
2026-03-30T22:00:10.1635|INFO|SequenceItem.cs|Run|254|Finishing Category: , Item: Dither
";

        [Fact]
        public void ParsesExposureStartingFinishingPairs() {
            var events = NinaLogParser.ParseFile(_logPath, SessionStart, SessionEnd);
            var exposures = events.Where(e => e.EventType == "Exposure").ToList();

            Assert.Equal(2, exposures.Count);
            // First exposure: 21:39:06 to 21:49:11 = ~605s (600s exposure + ~5s download)
            Assert.InRange(exposures[0].DurationSeconds, 600, 610);
            Assert.Contains("Exposure 600s", exposures[0].Details);
        }

        [Fact]
        public void DerivesCameraDownloadTime() {
            var events = NinaLogParser.ParseFile(_logPath, SessionStart, SessionEnd);
            var downloads = events.Where(e => e.EventType == "CameraDownload").ToList();

            Assert.Equal(2, downloads.Count);
            // Download time = total TakeExposure duration - 600s requested
            Assert.True(downloads[0].DurationSeconds > 0, "Download time should be positive");
            Assert.True(downloads[0].DurationSeconds < 15, "Download time should be reasonable");
            Assert.Contains("Derived from 600s exposure", downloads[0].Details);
        }

        [Fact]
        public void ParsesSwitchFilterWithFilterName() {
            var events = NinaLogParser.ParseFile(_logPath, SessionStart, SessionEnd);
            var filters = events.Where(e => e.EventType == "FilterChange").ToList();

            Assert.Equal(2, filters.Count);
            Assert.Equal("S", filters[0].Details);
            Assert.Equal("H", filters[1].Details);
            // First filter: 21:38:45 to 21:38:50 = ~4.1s
            Assert.InRange(filters[0].DurationSeconds, 3, 6);
        }

        [Fact]
        public void ParsesDitherWithVariableDurations() {
            var events = NinaLogParser.ParseFile(_logPath, SessionStart, SessionEnd);
            var dithers = events.Where(e => e.EventType == "Dither").ToList();

            Assert.Equal(2, dithers.Count);
            // First dither is nearly instant (~0.02s), second is ~18s with settle
            Assert.True(dithers[0].DurationSeconds < 1, "Instant dither should be < 1s");
            Assert.True(dithers[1].DurationSeconds > 10, "Settle dither should be > 10s");
        }

        [Fact]
        public void ParsesAutofocusStartEnd() {
            var events = NinaLogParser.ParseFile(_logPath, SessionStart, SessionEnd);
            var af = events.Where(e => e.EventType == "Autofocus").ToList();

            Assert.Single(af);
            // 21:36:26 to 21:38:45 = ~139s
            Assert.InRange(af[0].DurationSeconds, 130, 145);
        }

        [Fact]
        public void ParsesPlateSolveStartEnd() {
            var events = NinaLogParser.ParseFile(_logPath, SessionStart, SessionEnd);
            var solves = events.Where(e => e.EventType == "PlateSolve").ToList();

            Assert.Equal(2, solves.Count);
            Assert.Equal("Success", solves[0].Details);
            // First solve: 21:49:11 to 21:49:14 = ~2.1s
            Assert.InRange(solves[0].DurationSeconds, 1, 5);
        }

        [Fact]
        public void ParsesStarDetectionSingleTimestamp() {
            var events = NinaLogParser.ParseFile(_logPath, SessionStart, SessionEnd);
            var stars = events.Where(e => e.EventType == "StarDetection").ToList();

            Assert.Equal(2, stars.Count);
            Assert.Equal(0, stars[0].DurationSeconds);
            Assert.Contains("HFR 1.604", stars[0].Details);
            Assert.Contains("Stars 1394", stars[0].Details);
        }

        [Fact]
        public void ParsesImageSaveWithEmbeddedDuration() {
            var events = NinaLogParser.ParseFile(_logPath, SessionStart, SessionEnd);
            var saves = events.Where(e => e.EventType == "ImageSave").ToList();

            Assert.Equal(2, saves.Count);
            // First save: Duration Total 10.76s
            Assert.InRange(saves[0].DurationSeconds, 10, 11);
            Assert.Contains("BeforeSave:", saves[0].Details);
            Assert.Contains("Finalize:", saves[0].Details);
        }

        [Fact]
        public void ParsesCenteringSolverSequence() {
            var events = NinaLogParser.ParseFile(_logPath, SessionStart, SessionEnd);
            var centering = events.Where(e => e.EventType == "Centering").ToList();

            Assert.Single(centering);
            // 21:35:41 to 21:36:26 = ~44s
            Assert.InRange(centering[0].DurationSeconds, 40, 50);
        }

        [Fact]
        public void ParsesMoveFocuserByTemperature() {
            var events = NinaLogParser.ParseFile(_logPath, SessionStart, SessionEnd);
            var tempComp = events.Where(e => e.EventType == "TempCompFocus").ToList();

            Assert.Single(tempComp);
            // 21:34:20 to 21:34:27 = ~6.1s
            Assert.InRange(tempComp[0].DurationSeconds, 5, 8);
            Assert.Contains("Slope -8.5545", tempComp[0].Details);
        }

        [Fact]
        public void HandlesEmptyLogGracefully() {
            var emptyPath = Path.Combine(Path.GetTempPath(), $"empty_{Guid.NewGuid():N}.log");
            File.WriteAllText(emptyPath, "");
            try {
                var events = NinaLogParser.ParseFile(emptyPath, SessionStart, SessionEnd);
                Assert.Empty(events);
            } finally {
                File.Delete(emptyPath);
            }
        }

        [Fact]
        public void ExtractsNinaVersionFromHeader() {
            var lines = TestLogContent.Split('\n');
            var version = NinaLogParser.ExtractNinaVersion(lines);
            Assert.Equal("3.2.0.9001", version);
        }

        [Fact]
        public void ExtractsLogFileTimestamp() {
            var ts = NinaLogParser.ExtractLogFileTimestamp("20260330-212110-3.2.0.9001.13884-202603");
            Assert.NotNull(ts);
            Assert.Equal(new DateTime(2026, 3, 30, 21, 21, 10), ts.Value);
        }

        [Fact]
        public void ExtractLogFileTimestamp_InvalidFormat_ReturnsNull() {
            Assert.Null(NinaLogParser.ExtractLogFileTimestamp("invalid"));
            Assert.Null(NinaLogParser.ExtractLogFileTimestamp("abc"));
        }

        [Fact]
        public void CrossChecksExposureCount_MatchesExpected() {
            // Should parse 2 exposures from the test log
            var events = NinaLogParser.ParseFile(_logPath, SessionStart, SessionEnd, expectedImageCount: 2);
            var exposures = events.Where(e => e.EventType == "Exposure").ToList();
            Assert.Equal(2, exposures.Count);
        }

        [Fact]
        public void HandlesUnmatchedStartGracefully() {
            // Create a log with a Starting but no Finishing for TakeExposure
            var unmatchedLog = @"----------------------------------------------------------------------
--------------N.I.N.A. - Nighttime Imaging 'N' Astronomy--------------
--------------------------Version 3.2.0.9001--------------------------
-------------------------2026-03-30T21:21:13--------------------------
----------------------------------------------------------------------
2026-03-30T21:39:06.8545|INFO|SequenceItem.cs|Run|208|Starting Category: Scheduler, Item: TakeExposure, ExposureTime 600, Gain 100, Offset 19, ImageType LIGHT, Binning 1x1
";
            var path = Path.Combine(Path.GetTempPath(), $"unmatched_{Guid.NewGuid():N}.log");
            File.WriteAllText(path, unmatchedLog);
            try {
                var events = NinaLogParser.ParseFile(path, SessionStart, SessionEnd);
                // Should not crash, and should not produce an Exposure event
                Assert.DoesNotContain(events, e => e.EventType == "Exposure");
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void ParsesAllEventTypesFromTestFixture() {
            var events = NinaLogParser.ParseFile(_logPath, SessionStart, SessionEnd);

            var types = events.Select(e => e.EventType).Distinct().OrderBy(t => t).ToList();
            Assert.Contains("Exposure", types);
            Assert.Contains("CameraDownload", types);
            Assert.Contains("FilterChange", types);
            Assert.Contains("Dither", types);
            Assert.Contains("Autofocus", types);
            Assert.Contains("PlateSolve", types);
            Assert.Contains("StarDetection", types);
            Assert.Contains("ImageSave", types);
            Assert.Contains("Centering", types);
            Assert.Contains("TempCompFocus", types);
        }
    }
}
