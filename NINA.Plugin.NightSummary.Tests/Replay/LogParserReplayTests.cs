using NINA.Plugin.NightSummary.Data;
using System;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace NINA.Plugin.NightSummary.Tests.Replay {

    /// <summary>
    /// Replay tests for <see cref="NinaLogParser"/> against real NINA log files
    /// stored at K:\Remote Astro\Logs\audit. Skipped gracefully when the folder
    /// isn't present (CI, non-dev machines). On the dev machine these assert the
    /// key audit-validated behaviors so regressions get caught.
    /// </summary>
    public class LogParserReplayTests {
        private const string AuditFolder = @"K:\Remote Astro\Logs\audit";

        private readonly ITestOutputHelper _out;
        public LogParserReplayTests(ITestOutputHelper output) { _out = output; }

        [Fact]
        [Trait("Category", "Manual")]
        public void MeridianFlipLog_CapturesFullFlipWindow_And_SuppressesNoOps() {
            var log = FindLog("merdian flip and AF");
            if (log == null) return;

            var (start, end) = DeriveWindow(log);
            var events = NinaLogParser.ParseFile(log, start, end);
            DumpSummary(Path.GetFileName(log), events);

            // Finding D: trigger-based flip full window (slew + center + re-guide + settle)
            var mflip = events.Where(e => e.EventType == "MeridianFlip").ToList();
            Assert.Single(mflip);
            Assert.True(mflip[0].DurationSeconds > 100,
                $"MeridianFlip should capture full window (>100s), got {mflip[0].DurationSeconds:F1}s");

            // Wait scope: WaitUntilSafe removed
            Assert.Empty(events.Where(e => e.EventType == "SafetyWait"));

            // Finding 2: no inner plate solves inside Centering — outer solves only
            // (one per LIGHT exposure roughly)
            var solves = events.Where(e => e.EventType == "PlateSolve").ToList();
            var exposures = events.Where(e => e.EventType == "Exposure").ToList();
            Assert.True(solves.Count <= exposures.Count + 5,
                $"PlateSolves ({solves.Count}) should be close to exposure count ({exposures.Count}), not inflated by inner centering solves");
        }

        [Fact]
        [Trait("Category", "Manual")]
        public void ShortSession_WithValidationErrors_EmitsEventsForFailedItems() {
            var log = FindLog("short session with unsafe");
            if (log == null) return;

            var (start, end) = DeriveWindow(log);
            var events = NinaLogParser.ParseFile(log, start, end);
            DumpSummary(Path.GetFileName(log), events);

            // Finding 4: ERROR "Failed validation" lines should terminate pendingStarts
            // so events get emitted (even as 0s) — baseline parser lost them entirely.
            Assert.NotEmpty(events);

            // Wait scope: WaitForTimeSpan captured as Wait
            var waits = events.Where(e => e.EventType == "Wait").ToList();
            Assert.NotEmpty(waits);
        }

        [Fact]
        [Trait("Category", "Manual")]
        public void OverheadAnalysisLog_RemovesBogusWaitUntilSafe_And_SuppressesGuidingNoOps() {
            var log = FindLog("overhead analysis");
            if (log == null) return;

            var (start, end) = DeriveWindow(log);
            var events = NinaLogParser.ParseFile(log, start, end);
            DumpSummary(Path.GetFileName(log), events);

            // Wait scope removal: baseline counted an 8400s WaitUntilSafe as overhead;
            // current parser drops it entirely.
            Assert.Empty(events.Where(e => e.EventType == "SafetyWait"));

            // Finding 1: StartGuiding no-ops suppressed — Guiding event count should be
            // low (roughly one per real re-guide after dither), not dozens.
            var guiding = events.Where(e => e.EventType == "Guiding").ToList();
            Assert.True(guiding.Count < 15,
                $"Guiding events should be ≪ exposure count after no-op suppression, got {guiding.Count}");
        }

        private static string FindLog(string substring) {
            if (!Directory.Exists(AuditFolder)) return null;
            return Directory.GetFiles(AuditFolder, "*.log")
                .FirstOrDefault(f => f.Contains(substring, StringComparison.OrdinalIgnoreCase));
        }

        private static (DateTime start, DateTime end) DeriveWindow(string logPath) {
            DateTime? first = null, last = null;
            foreach (var line in File.ReadLines(logPath)) {
                var parts = line.Split('|');
                if (parts.Length < 6) continue;
                if (!DateTime.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var t)) continue;
                first ??= t;
                last = t;
            }
            return (first ?? DateTime.MinValue, last?.AddMinutes(1) ?? DateTime.MaxValue);
        }

        private void DumpSummary(string logName, System.Collections.Generic.List<TimingEvent> events) {
            _out.WriteLine($"=== {logName} ===");
            _out.WriteLine($"{events.Count} total events");
            var groups = events.GroupBy(e => e.EventType)
                .OrderByDescending(g => g.Sum(e => e.DurationSeconds));
            foreach (var g in groups)
                _out.WriteLine($"  {g.Key,-18} {g.Count(),4} × total {g.Sum(e => e.DurationSeconds),10:F1}s");
        }
    }
}
