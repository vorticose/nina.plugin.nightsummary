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
2026-03-30T21:38:46.5000|INFO|FilterWheelVM.cs|ChangeFilter|112|Moving to Filter S at Position 4
2026-03-30T21:38:50.0272|INFO|SequenceItem.cs|Run|254|Finishing Category: Scheduler, Item: SwitchFilter, Filter: S
2026-03-30T21:39:06.8545|INFO|SequenceItem.cs|Run|208|Starting Category: Scheduler, Item: TakeExposure, ExposureTime 600, Gain 100, Offset 19, ImageType LIGHT, Binning 1x1
2026-03-30T21:49:11.8742|INFO|SequenceItem.cs|Run|254|Finishing Category: Scheduler, Item: TakeExposure, ExposureTime 600, Gain 100, Offset 19, ImageType LIGHT, Binning 1x1
2026-03-30T21:49:11.9040|INFO|ImageSolver.cs|Solve|41|Platesolving with parameters: FocalLength: 448 PixelSize: 3.76 SearchRadius: 30 BlindFailoverEnabled: True Regions: 5000 DownSampleFactor: 0 MaxObjects: 500
2026-03-30T21:49:14.0657|INFO|ImageSolver.cs|Solve|54|Platesolve successful: Coordinates: RA: 07:05:49; Dec: -11 04' 20""; Epoch: J2000 - Position Angle: 114.18
2026-03-30T21:49:17.1189|INFO|HocusFocusStarDetection.cs|Detect|413|Average HFR: 1.604065807017856, HFR MAD: 0.064623168208225, Detected Stars 1394, Region: 0
2026-03-30T21:49:22.6443|INFO|ImageSaveController.cs|DoWork|97|Successfully saved file at D:\\Seagull Nebula\S\600.00s\test.fits. Duration Total: 00:00:10.7636414; BeforeSave: 00:00:00.0199465; BeforeFinalizeImageSaved: 00:00:05.5429538; FinalizeSaveTime: 00:00:05.2007394
2026-03-30T21:49:25.8666|INFO|SequenceItem.cs|Run|208|Starting Category: Scheduler, Item: SwitchFilter, Filter: H
2026-03-30T21:49:26.5000|INFO|FilterWheelVM.cs|ChangeFilter|112|Moving to Filter H at Position 5
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
        public void ParsesPlateSolvesFromImageSolver() {
            var events = NinaLogParser.ParseFile(_logPath, SessionStart, SessionEnd);
            var solves = events.Where(e => e.EventType == "PlateSolve").ToList();

            Assert.Equal(2, solves.Count);
            Assert.Equal("Success", solves[0].Details);
            Assert.InRange(solves[0].DurationSeconds, 1, 5);
        }

        [Fact]
        public void DoesNotParseStarDetectionEvents() {
            // Star detection events from HocusFocusStarDetection.cs are sub-operations
            // and are no longer parsed (they were zero-duration anyway)
            var events = NinaLogParser.ParseFile(_logPath, SessionStart, SessionEnd);
            var stars = events.Where(e => e.EventType == "StarDetection").ToList();
            Assert.Empty(stars);
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
        public void DoesNotParseCenteringSolverDirectly() {
            // Centering is now tracked via SequenceItem Center/CenterAndRotate, not CenteringSolver.cs
            // The test log doesn't have a SequenceItem Center, so no centering events expected
            var events = NinaLogParser.ParseFile(_logPath, SessionStart, SessionEnd);
            var centering = events.Where(e => e.EventType == "Centering").ToList();
            Assert.Empty(centering);
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
        public void UnmatchedExposure_EmitsAbortedExposureEvent() {
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
                // Should not produce a normal Exposure event
                Assert.DoesNotContain(events, e => e.EventType == "Exposure");
                // Should produce an AbortedExposure event
                var aborted = events.Where(e => e.EventType == "AbortedExposure").ToList();
                Assert.Single(aborted);
                Assert.Equal(new DateTime(2026, 3, 30, 21, 39, 6, 854).AddTicks(5000), aborted[0].StartTime);
                // Duration should be capped at requested exposure (600s) + 30s grace, not
                // extended to sessionEnd (which would be ~51 minutes here).
                Assert.Equal(630, aborted[0].DurationSeconds);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void UnmatchedExposure_WithoutRequestedDuration_CapsAt600s() {
            // No "ExposureTime N" in the Starting message — parser can't extract requested time,
            // so it falls back to the 600s (10 min) conservative cap.
            var unmatchedLog = @"----------------------------------------------------------------------
--------------N.I.N.A. - Nighttime Imaging 'N' Astronomy--------------
--------------------------Version 3.2.0.9001--------------------------
-------------------------2026-03-30T21:21:13--------------------------
----------------------------------------------------------------------
2026-03-30T21:39:06.8545|INFO|SequenceItem.cs|Run|208|Starting Category: Scheduler, Item: TakeExposure
";
            var path = Path.Combine(Path.GetTempPath(), $"unmatched_nodur_{Guid.NewGuid():N}.log");
            File.WriteAllText(path, unmatchedLog);
            try {
                var events = NinaLogParser.ParseFile(path, SessionStart, SessionEnd);
                var aborted = events.Where(e => e.EventType == "AbortedExposure").ToList();
                Assert.Single(aborted);
                Assert.Equal(600, aborted[0].DurationSeconds);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void RespectsExactSessionBoundaries() {
            // Narrow the session window so the early MoveFocuserByTemperature (21:34) falls outside
            var narrowStart = new DateTime(2026, 3, 30, 21, 38, 0);
            var narrowEnd   = new DateTime(2026, 3, 30, 21, 50, 0);
            var events = NinaLogParser.ParseFile(_logPath, narrowStart, narrowEnd);

            // MoveFocuserByTemperature at 21:34 should be excluded (before narrowStart)
            var tempComp = events.Where(e => e.EventType == "TempCompFocus").ToList();
            Assert.Empty(tempComp);

            // Autofocus starts at 21:36:26, before narrowStart — should also be excluded
            var af = events.Where(e => e.EventType == "Autofocus").ToList();
            Assert.Empty(af);

            // Exposure at 21:39 and filter change at 21:38:45 should still be included
            var exposures = events.Where(e => e.EventType == "Exposure").ToList();
            Assert.Single(exposures); // Only the first exposure (21:39-21:49), second starts at 21:49
        }

        [Fact]
        public void SwitchFilter_NoFilterMoveInWindow_IsNotCounted() {
            var log = @"----------------------------------------------------------------------
--------------N.I.N.A. - Nighttime Imaging 'N' Astronomy--------------
--------------------------Version 3.2.0.9001--------------------------
-------------------------2026-03-30T21:21:13--------------------------
----------------------------------------------------------------------
2026-03-30T21:38:45.8894|INFO|SequenceItem.cs|Run|208|Starting Category: Scheduler, Item: SwitchFilter, Filter: S
2026-03-30T21:38:45.9100|INFO|SequenceItem.cs|Run|254|Finishing Category: Scheduler, Item: SwitchFilter, Filter: S
";
            var path = Path.Combine(Path.GetTempPath(), $"noop_{Guid.NewGuid():N}.log");
            File.WriteAllText(path, log);
            try {
                var events = NinaLogParser.ParseFile(path, SessionStart, SessionEnd);
                Assert.Empty(events.Where(e => e.EventType == "FilterChange"));
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void SwitchFilter_FilterMoveBeforeWindow_IsNotCounted() {
            // A "Moving to Filter" from before the SwitchFilter start (e.g. autofocus)
            // should not satisfy the check for the subsequent SwitchFilter window.
            var log = @"----------------------------------------------------------------------
--------------N.I.N.A. - Nighttime Imaging 'N' Astronomy--------------
--------------------------Version 3.2.0.9001--------------------------
-------------------------2026-03-30T21:21:13--------------------------
----------------------------------------------------------------------
2026-03-30T21:38:00.0000|INFO|FilterWheelVM.cs|ChangeFilter|112|Moving to Filter L at Position 0
2026-03-30T21:38:45.8894|INFO|SequenceItem.cs|Run|208|Starting Category: Scheduler, Item: SwitchFilter, Filter: S
2026-03-30T21:38:45.9100|INFO|SequenceItem.cs|Run|254|Finishing Category: Scheduler, Item: SwitchFilter, Filter: S
";
            var path = Path.Combine(Path.GetTempPath(), $"before_{Guid.NewGuid():N}.log");
            File.WriteAllText(path, log);
            try {
                var events = NinaLogParser.ParseFile(path, SessionStart, SessionEnd);
                Assert.Empty(events.Where(e => e.EventType == "FilterChange"));
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void SwitchFilter_FilterMoveInWindow_IsCounted() {
            var log = @"----------------------------------------------------------------------
--------------N.I.N.A. - Nighttime Imaging 'N' Astronomy--------------
--------------------------Version 3.2.0.9001--------------------------
-------------------------2026-03-30T21:21:13--------------------------
----------------------------------------------------------------------
2026-03-30T21:38:45.8894|INFO|SequenceItem.cs|Run|208|Starting Category: Scheduler, Item: SwitchFilter, Filter: S
2026-03-30T21:38:46.5000|INFO|FilterWheelVM.cs|ChangeFilter|112|Moving to Filter S at Position 4
2026-03-30T21:38:50.0272|INFO|SequenceItem.cs|Run|254|Finishing Category: Scheduler, Item: SwitchFilter, Filter: S
";
            var path = Path.Combine(Path.GetTempPath(), $"real_{Guid.NewGuid():N}.log");
            File.WriteAllText(path, log);
            try {
                var events = NinaLogParser.ParseFile(path, SessionStart, SessionEnd);
                var filters = events.Where(e => e.EventType == "FilterChange").ToList();
                Assert.Single(filters);
                Assert.Equal("S", filters[0].Details);
                Assert.InRange(filters[0].DurationSeconds, 3, 6);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void RunAutofocus_RestoresFilter_DoesNotBleedToNextSwitchFilter() {
            // Finding 5 regression: AF internally logs "Moving to Filter" when restoring the
            // working filter. Without the lastFilterMoveTimestamp reset on RunAutofocus finish,
            // the subsequent SwitchFilter (a no-op) would incorrectly be counted.
            var log = @"----------------------------------------------------------------------
--------------N.I.N.A. - Nighttime Imaging 'N' Astronomy--------------
--------------------------Version 3.2.0.9001--------------------------
-------------------------2026-03-30T21:21:13--------------------------
----------------------------------------------------------------------
2026-03-30T21:40:00.0000|INFO|SequenceItem.cs|Run|208|Starting Category: Focuser, Item: RunAutofocus
2026-03-30T21:42:50.0000|INFO|FilterWheelVM.cs|ChangeFilter|112|Moving to Filter H at Position 5
2026-03-30T21:43:00.0000|INFO|SequenceItem.cs|Run|254|Finishing Category: Focuser, Item: RunAutofocus
2026-03-30T21:43:06.0000|INFO|SequenceItem.cs|Run|208|Starting Category: Scheduler, Item: SwitchFilter, Filter: H
2026-03-30T21:43:06.5000|INFO|SequenceItem.cs|Run|254|Finishing Category: Scheduler, Item: SwitchFilter, Filter: H
";
            var path = Path.Combine(Path.GetTempPath(), $"afbleed_{Guid.NewGuid():N}.log");
            File.WriteAllText(path, log);
            try {
                var events = NinaLogParser.ParseFile(path, SessionStart, SessionEnd);
                Assert.Single(events.Where(e => e.EventType == "Autofocus"));
                Assert.Empty(events.Where(e => e.EventType == "FilterChange"));
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void ErrorLevel_SequenceItem_ClearsPendingStart() {
            // Finding 4: a validation failure ("ERROR|SequenceItem.cs|Run|...|Failed validation:")
            // must terminate the item's pendingStart and emit the event — otherwise pendingStarts
            // leak and the warning log fills with "unmatched X start" entries.
            var log = @"----------------------------------------------------------------------
--------------N.I.N.A. - Nighttime Imaging 'N' Astronomy--------------
--------------------------Version 3.2.0.9001--------------------------
-------------------------2026-03-30T21:21:13--------------------------
----------------------------------------------------------------------
2026-03-30T21:40:00.0000|INFO|SequenceItem.cs|Run|208|Starting Category: Focuser, Item: RunAutofocus
2026-03-30T21:40:00.0050|ERROR|SequenceItem.cs|Run|281|Failed validation: Category: Focuser, Item: RunAutofocus - no focuser connected
";
            var path = Path.Combine(Path.GetTempPath(), $"fail_{Guid.NewGuid():N}.log");
            File.WriteAllText(path, log);
            try {
                var events = NinaLogParser.ParseFile(path, SessionStart, SessionEnd);
                var af = events.Where(e => e.EventType == "Autofocus").ToList();
                Assert.Single(af);
                Assert.True(af[0].DurationSeconds < 1);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void StartGuiding_NoRealRequest_IsSuppressed() {
            // Finding 1: if PHD2 is already guiding, StartGuiding SequenceItem executes as a no-op.
            // Real starts log "Phd2 - Requesting to start guiding" via TryStartGuideCommand;
            // no-ops do not. Suppress when no real request in window.
            var log = @"----------------------------------------------------------------------
--------------N.I.N.A. - Nighttime Imaging 'N' Astronomy--------------
--------------------------Version 3.2.0.9001--------------------------
-------------------------2026-03-30T21:21:13--------------------------
----------------------------------------------------------------------
2026-03-30T21:40:00.0000|INFO|SequenceItem.cs|Run|208|Starting Category: Guider, Item: StartGuiding
2026-03-30T21:40:00.0100|INFO|PHD2Guider.cs|StartGuidingPrivate|195|Phd2 - App is already guiding. Skipping start guiding
2026-03-30T21:40:00.0200|INFO|SequenceItem.cs|Run|254|Finishing Category: Guider, Item: StartGuiding
";
            var path = Path.Combine(Path.GetTempPath(), $"guide_{Guid.NewGuid():N}.log");
            File.WriteAllText(path, log);
            try {
                var events = NinaLogParser.ParseFile(path, SessionStart, SessionEnd);
                Assert.Empty(events.Where(e => e.EventType == "Guiding"));
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void StartGuiding_WithRealRequest_IsCounted() {
            var log = @"----------------------------------------------------------------------
--------------N.I.N.A. - Nighttime Imaging 'N' Astronomy--------------
--------------------------Version 3.2.0.9001--------------------------
-------------------------2026-03-30T21:21:13--------------------------
----------------------------------------------------------------------
2026-03-30T21:40:00.0000|INFO|SequenceItem.cs|Run|208|Starting Category: Guider, Item: StartGuiding
2026-03-30T21:40:00.5000|INFO|PHD2Guider.cs|TryStartGuideCommand|150|Phd2 - Requesting to start guiding
2026-03-30T21:40:30.0000|INFO|SequenceItem.cs|Run|254|Finishing Category: Guider, Item: StartGuiding
";
            var path = Path.Combine(Path.GetTempPath(), $"guidereal_{Guid.NewGuid():N}.log");
            File.WriteAllText(path, log);
            try {
                var events = NinaLogParser.ParseFile(path, SessionStart, SessionEnd);
                Assert.Single(events.Where(e => e.EventType == "Guiding"));
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void InnerPlateSolveDuringCentering_IsSuppressed() {
            // Finding 2: Center/CenterAndRotate run inner plate solves as part of execution.
            // Those must not emit separate PlateSolve events — would double-count with the
            // Centering event itself.
            var log = @"----------------------------------------------------------------------
--------------N.I.N.A. - Nighttime Imaging 'N' Astronomy--------------
--------------------------Version 3.2.0.9001--------------------------
-------------------------2026-03-30T21:21:13--------------------------
----------------------------------------------------------------------
2026-03-30T21:40:00.0000|INFO|SequenceItem.cs|Run|208|Starting Category: Telescope, Item: CenterAndRotate
2026-03-30T21:40:05.0000|INFO|ImageSolver.cs|Solve|41|Platesolving with parameters: FocalLength: 448
2026-03-30T21:40:08.0000|INFO|ImageSolver.cs|Solve|54|Platesolve successful: Coordinates: RA: 07:05:49
2026-03-30T21:40:10.0000|INFO|SequenceItem.cs|Run|254|Finishing Category: Telescope, Item: CenterAndRotate
2026-03-30T21:50:00.0000|INFO|ImageSolver.cs|Solve|41|Platesolving with parameters: FocalLength: 448
2026-03-30T21:50:03.0000|INFO|ImageSolver.cs|Solve|54|Platesolve successful: Coordinates: RA: 07:05:49
";
            var path = Path.Combine(Path.GetTempPath(), $"innersolve_{Guid.NewGuid():N}.log");
            File.WriteAllText(path, log);
            try {
                var events = NinaLogParser.ParseFile(path, SessionStart, SessionEnd);
                Assert.Single(events.Where(e => e.EventType == "Centering"));
                // Only the outer (post-exposure) plate solve should be emitted, not the inner one
                var solves = events.Where(e => e.EventType == "PlateSolve").ToList();
                Assert.Single(solves);
                Assert.Equal(new DateTime(2026, 3, 30, 21, 50, 0), solves[0].StartTime);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void MeridianFlipTrigger_FullWindow_IsTracked() {
            // Finding D: trigger-based flip spans from SequenceTrigger start to MeridianFlipVM
            // "Exiting meridian flip" — includes slew, center, re-guide, settle. Slew-only was
            // a significant undercount (32s vs 110s real).
            var log = @"----------------------------------------------------------------------
--------------N.I.N.A. - Nighttime Imaging 'N' Astronomy--------------
--------------------------Version 3.2.0.9001--------------------------
-------------------------2026-03-30T21:21:13--------------------------
----------------------------------------------------------------------
2026-03-30T22:00:00.0000|INFO|SequenceTrigger.cs|Run|45|Starting Trigger: MeridianFlipTrigger
2026-03-30T22:01:50.9000|INFO|MeridianFlipVM.cs|DoMeridianFlip|310|Meridian Flip - Exiting meridian flip
";
            var path = Path.Combine(Path.GetTempPath(), $"mflip_{Guid.NewGuid():N}.log");
            File.WriteAllText(path, log);
            try {
                var events = NinaLogParser.ParseFile(path, SessionStart, SessionEnd);
                var flips = events.Where(e => e.EventType == "MeridianFlip").ToList();
                Assert.Single(flips);
                Assert.InRange(flips[0].DurationSeconds, 110, 112);
                Assert.Contains("full window", flips[0].Details);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void WaitForTimeSpan_IsTrackedAsWait() {
            // User's safety stabilization wait pattern: a deliberate buffer after unsafe→safe
            // transition. Sequencer-caused wait — counts as overhead.
            var log = @"----------------------------------------------------------------------
--------------N.I.N.A. - Nighttime Imaging 'N' Astronomy--------------
--------------------------Version 3.2.0.9001--------------------------
-------------------------2026-03-30T21:21:13--------------------------
----------------------------------------------------------------------
2026-03-30T22:00:00.0000|INFO|SequenceItem.cs|Run|208|Starting Category: Utility, Item: WaitForTimeSpan, Wait: 120s
2026-03-30T22:02:00.0000|INFO|SequenceItem.cs|Run|254|Finishing Category: Utility, Item: WaitForTimeSpan, Wait: 120s
";
            var path = Path.Combine(Path.GetTempPath(), $"wait_{Guid.NewGuid():N}.log");
            File.WriteAllText(path, log);
            try {
                var events = NinaLogParser.ParseFile(path, SessionStart, SessionEnd);
                var waits = events.Where(e => e.EventType == "Wait").ToList();
                Assert.Single(waits);
                Assert.InRange(waits[0].DurationSeconds, 119, 121);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void WaitUntilSafe_IsNotTracked() {
            // Condition-gated wait: weather unsafe means rig physically can't image.
            // Not overhead — skipped by design.
            var log = @"----------------------------------------------------------------------
--------------N.I.N.A. - Nighttime Imaging 'N' Astronomy--------------
--------------------------Version 3.2.0.9001--------------------------
-------------------------2026-03-30T21:21:13--------------------------
----------------------------------------------------------------------
2026-03-30T22:00:00.0000|INFO|SequenceItem.cs|Run|208|Starting Category: Safety, Item: WaitUntilSafe
2026-03-30T22:30:00.0000|INFO|SequenceItem.cs|Run|254|Finishing Category: Safety, Item: WaitUntilSafe
";
            var path = Path.Combine(Path.GetTempPath(), $"safewait_{Guid.NewGuid():N}.log");
            // Widen session window to cover the 30-min wait
            var wideEnd = new DateTime(2026, 3, 30, 23, 0, 0);
            File.WriteAllText(path, log);
            try {
                var events = NinaLogParser.ParseFile(path, SessionStart, wideEnd);
                Assert.Empty(events.Where(e => e.EventType == "SafetyWait" || e.EventType == "Wait" || e.EventType == "WaitUntilSafe"));
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void ParsesSchedulerWaitFromSymbolMessages() {
            // TargetScheduler-WaitStart → TargetScheduler-NewTargetStart bracket an idle wait.
            var waitLog = @"----------------------------------------------------------------------
--------------N.I.N.A. - Nighttime Imaging 'N' Astronomy--------------
--------------------------Version 3.2.0.9001--------------------------
-------------------------2026-03-30T21:21:13--------------------------
----------------------------------------------------------------------
2026-03-30T21:40:00.0000|INFO|Symbol.cs|OnMessageReceived|627|Received message from Target Scheduler re: TargetScheduler-WaitStart
2026-03-30T21:42:44.0000|INFO|Symbol.cs|OnMessageReceived|627|Received message from Target Scheduler re: TargetScheduler-NewTargetStart
2026-03-30T21:42:44.0100|INFO|Symbol.cs|OnMessageReceived|627|Received message from Target Scheduler re: TargetScheduler-TargetStart
";
            var path = Path.Combine(Path.GetTempPath(), $"wait_{Guid.NewGuid():N}.log");
            File.WriteAllText(path, waitLog);
            try {
                var events = NinaLogParser.ParseFile(path, SessionStart, SessionEnd);
                var waits = events.Where(e => e.EventType == "SchedulerWait").ToList();
                Assert.Single(waits);
                Assert.InRange(waits[0].DurationSeconds, 160, 170); // 2m44s
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void SchedulerWait_WithoutNewTargetStart_DoesNotEmit() {
            var orphanLog = @"----------------------------------------------------------------------
--------------N.I.N.A. - Nighttime Imaging 'N' Astronomy--------------
--------------------------Version 3.2.0.9001--------------------------
-------------------------2026-03-30T21:21:13--------------------------
----------------------------------------------------------------------
2026-03-30T21:40:00.0000|INFO|Symbol.cs|OnMessageReceived|627|Received message from Target Scheduler re: TargetScheduler-WaitStart
";
            var path = Path.Combine(Path.GetTempPath(), $"orphanwait_{Guid.NewGuid():N}.log");
            File.WriteAllText(path, orphanLog);
            try {
                var events = NinaLogParser.ParseFile(path, SessionStart, SessionEnd);
                Assert.DoesNotContain(events, e => e.EventType == "SchedulerWait");
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
            Assert.Contains("ImageSave", types);
            Assert.Contains("TempCompFocus", types);
            Assert.Contains("PlateSolve", types);
            // StarDetection is not parsed (zero-duration, single timestamp)
            Assert.DoesNotContain("StarDetection", types);
        }

        // ── Fix: failed SequenceItem (ERROR line, no Finishing) ──────────────
        // Real-world trigger: PHD2 StartGuiding fails after 3 retries (~110s). Parser
        // used to orphan the pendingStart because ERROR lines were filtered out.

        [Fact]
        public void FailedSequenceItem_EmitsEventFromErrorLine() {
            var log = @"----------------------------------------------------------------------
--------------N.I.N.A. - Nighttime Imaging 'N' Astronomy--------------
--------------------------Version 3.2.0.9001--------------------------
-------------------------2026-03-30T21:21:13--------------------------
----------------------------------------------------------------------
2026-03-30T21:35:00.0000|INFO|SequenceItem.cs|Run|208|Starting Category: Guider, Item: StartGuiding
2026-03-30T21:36:49.0000|ERROR|SequenceItem.cs|Run|263|Category: Guider, Item: StartGuiding -
2026-03-30T21:36:49.0100|ERROR|SequenceItem.cs|RunErrorBehavior|195|Instruction Start Guiding failed after 1 attempt. Error behavior is set to ContinueOnError. Continuing.
";
            var path = Path.Combine(Path.GetTempPath(), $"failed_{Guid.NewGuid():N}.log");
            File.WriteAllText(path, log);
            try {
                var events = NinaLogParser.ParseFile(path, SessionStart, SessionEnd);
                var guiding = events.Where(e => e.EventType == "Guiding").ToList();
                Assert.Single(guiding);
                Assert.InRange(guiding[0].DurationSeconds, 108, 111);
                Assert.Equal("Failed", guiding[0].Details);
            } finally {
                File.Delete(path);
            }
        }

        // ── Fix: sequence cancelled mid-item (WhenUnsafe/roof close) ─────────
        // When a SequenceItem is pending and the sequence is cancelled by WhenUnsafe
        // (roof closes mid-guiding-retry), no Finishing or ERROR line is emitted.
        // Parser flushes pendingStarts at the cancel timestamp.

        [Fact]
        public void CancelledSequence_FlushesPendingStartsAtCancelTime() {
            var log = @"----------------------------------------------------------------------
--------------N.I.N.A. - Nighttime Imaging 'N' Astronomy--------------
--------------------------Version 3.2.0.9001--------------------------
-------------------------2026-03-30T21:21:13--------------------------
----------------------------------------------------------------------
2026-03-30T21:35:00.0000|INFO|SequenceItem.cs|Run|208|Starting Category: Guider, Item: StartGuiding
2026-03-30T21:36:43.0000|INFO|WhenCommon.cs|InterruptWhen|332|Canceling sequence...
";
            var path = Path.Combine(Path.GetTempPath(), $"cancelled_{Guid.NewGuid():N}.log");
            File.WriteAllText(path, log);
            try {
                var events = NinaLogParser.ParseFile(path, SessionStart, SessionEnd);
                var guiding = events.Where(e => e.EventType == "Guiding").ToList();
                Assert.Single(guiding);
                Assert.InRange(guiding[0].DurationSeconds, 102, 104);
                Assert.Equal("Cancelled", guiding[0].Details);
            } finally {
                File.Delete(path);
            }
        }

        // ── Fix: meridian flip trigger full duration ─────────────────────────
        // MeridianFlipTrigger runs as a SequenceTrigger, not a SequenceItem, so the
        // full flip (stop guide → slew → settle → recenter → reguide → settle) must
        // be tracked from SequenceTrigger start to MeridianFlipVM's "Exiting" marker.

        [Fact]
        public void MeridianFlipTrigger_EmitsEventSpanningFullFlipDuration() {
            var log = @"----------------------------------------------------------------------
--------------N.I.N.A. - Nighttime Imaging 'N' Astronomy--------------
--------------------------Version 3.2.0.9001--------------------------
-------------------------2026-03-30T21:21:13--------------------------
----------------------------------------------------------------------
2026-03-30T21:39:42.8354|INFO|SequenceTrigger.cs|Run|114|Starting Trigger: MeridianFlipTrigger
2026-03-30T21:39:42.8445|INFO|MeridianFlipVM.cs|DoMeridianFlip|160|Meridian Flip - Initializing Meridian Flip.
2026-03-30T21:41:33.7062|INFO|MeridianFlipVM.cs|DoMeridianFlip|221|Meridian Flip - Exiting meridian flip
";
            var path = Path.Combine(Path.GetTempPath(), $"mf_{Guid.NewGuid():N}.log");
            File.WriteAllText(path, log);
            try {
                var events = NinaLogParser.ParseFile(path, SessionStart, SessionEnd);
                var mf = events.Where(e => e.EventType == "MeridianFlip").ToList();
                Assert.Single(mf);
                // 21:39:42.835 → 21:41:33.706 ≈ 110.9s (full flip, not just slew)
                Assert.InRange(mf[0].DurationSeconds, 110, 112);
                Assert.Contains("recenter", mf[0].Details);
            } finally {
                File.Delete(path);
            }
        }

        // ── Fix: WaitForTimeSpan tracked (OnceSafe recovery waits, etc.) ─────

        // ── End-to-end smoke test against a real-world log ───────────────────
        // Skipped on machines that don't have the reference log. Verifies the parser
        // doesn't crash on a real multi-target session and that each of the fixes
        // above leaves a visible fingerprint (failed guiding, cancelled item, MF).
        // Overfitting is mitigated by asserting presence, not exact counts.

        private const string ReferenceLogPath =
            @"K:\Remote Astro\Logs\20260323-205422-3.2.0.9001.10716-202603.log";

        [Fact]
        public void ReferenceLog_20260323_ParsesWithoutCrash_AndShowsAllFixFingerprints() {
            if (!File.Exists(ReferenceLogPath)) return; // skip on machines without the log

            // Session bounds generous — just need to cover the full 2026-03-23 night
            var start = new DateTime(2026, 3, 23, 20, 0, 0);
            var end   = new DateTime(2026, 3, 24, 6, 0, 0);

            var events = NinaLogParser.ParseFile(ReferenceLogPath, start, end);

            Assert.NotEmpty(events);
            Assert.Contains(events, e => e.EventType == "Guiding" && e.Details == "Failed");
            Assert.Contains(events, e => e.EventType == "Guiding" && e.Details == "Cancelled");
            Assert.Contains(events, e => e.EventType == "MeridianFlip" && e.DurationSeconds > 100);
        }

        [Fact]
        public void WaitForTimeSpan_EmitsWaitEvent() {
            var log = @"----------------------------------------------------------------------
--------------N.I.N.A. - Nighttime Imaging 'N' Astronomy--------------
--------------------------Version 3.2.0.9001--------------------------
-------------------------2026-03-30T21:21:13--------------------------
----------------------------------------------------------------------
2026-03-30T21:38:16.2930|INFO|SequenceItem.cs|Run|208|Starting Category: Utility, Item: WaitForTimeSpan, Time: 60s
2026-03-30T21:39:16.3826|INFO|SequenceItem.cs|Run|254|Finishing Category: Utility, Item: WaitForTimeSpan, Time: 60s
";
            var path = Path.Combine(Path.GetTempPath(), $"wait_{Guid.NewGuid():N}.log");
            File.WriteAllText(path, log);
            try {
                var events = NinaLogParser.ParseFile(path, SessionStart, SessionEnd);
                var waits = events.Where(e => e.EventType == "Wait").ToList();
                Assert.Single(waits);
                Assert.InRange(waits[0].DurationSeconds, 59, 61);
            } finally {
                File.Delete(path);
            }
        }
    }
}
