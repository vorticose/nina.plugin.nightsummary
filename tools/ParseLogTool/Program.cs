using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NINA.Plugin.NightSummary.Data;

// Stub Logger matching the signatures used by NinaLogParser. Satisfies the
// `using NINA.Core.Utility;` import in the compile-included parser without
// pulling in the NINA runtime. Must live in NINA.Core.Utility namespace so
// `Logger.Info(...)` in NinaLogParser.cs resolves to this stub.
// Output goes to stderr so stdout stays clean for diff-friendly event listings.
namespace NINA.Core.Utility {
    internal static class Logger {
        public static void Info(string msg)    => Console.Error.WriteLine($"[INFO]  {msg}");
        public static void Warning(string msg) => Console.Error.WriteLine($"[WARN]  {msg}");
        public static void Error(string msg)   => Console.Error.WriteLine($"[ERROR] {msg}");
        public static void Debug(string msg)   => Console.Error.WriteLine($"[DEBUG] {msg}");
    }
}

namespace ParseLogTool {
    internal class Program {
        private static int Main(string[] args) {
            if (args.Length < 1 || args[0] == "--help" || args[0] == "-h") {
                PrintUsage();
                return args.Length == 0 ? 2 : 0;
            }

            var logPath = args[0];
            if (!File.Exists(logPath)) {
                Console.Error.WriteLine($"Log file not found: {logPath}");
                return 1;
            }

            // Session window defaults to full day containing the log's first timestamp.
            var (sessionStart, sessionEnd) = DeriveSessionWindow(logPath, args);

            Console.WriteLine($"=== {Path.GetFileName(logPath)} ===");
            Console.WriteLine($"Session window: {sessionStart:yyyy-MM-dd HH:mm:ss} → {sessionEnd:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine();

            var events = NinaLogParser.ParseFile(logPath, sessionStart, sessionEnd);

            PrintEventList(events);
            Console.WriteLine();
            PrintCategorySummary(events);
            return 0;
        }

        private static (DateTime start, DateTime end) DeriveSessionWindow(string logPath, string[] args) {
            // args[1]/args[2] override if provided
            if (args.Length >= 3 &&
                DateTime.TryParse(args[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var s) &&
                DateTime.TryParse(args[2], CultureInfo.InvariantCulture, DateTimeStyles.None, out var e)) {
                return (s, e);
            }

            // Derive from log: first INFO line timestamp − 1 min, last timestamp + 1 min
            DateTime? first = null, last = null;
            foreach (var line in File.ReadLines(logPath)) {
                var parts = line.Split('|');
                if (parts.Length < 6) continue;
                if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) continue;
                first ??= t;
                last = t;
            }
            if (first == null || last == null) {
                // Fallback: last 24h
                var now = DateTime.Now;
                return (now.AddDays(-1), now);
            }
            return (first.Value.AddMinutes(-1), last.Value.AddMinutes(1));
        }

        private static void PrintEventList(List<TimingEvent> events) {
            Console.WriteLine("=== events (chronological) ===");
            foreach (var e in events.OrderBy(x => x.StartTime)) {
                var detail = string.IsNullOrEmpty(e.Details) ? "" : $"  {e.Details}";
                Console.WriteLine($"{e.StartTime:HH:mm:ss.fff}  {e.EventType,-16} {e.DurationSeconds,8:F2}s{detail}");
            }
        }

        private static void PrintCategorySummary(List<TimingEvent> events) {
            Console.WriteLine("=== category summary ===");

            // Match the plugin's ReportGenerator.BuildOverheadBreakdownSection logic so
            // the tool's output reflects the same metrics shown in the HTML report.
            // NOTE: the tool has no access to SessionEvents, so roof-closed time cannot
            // be subtracted from the window. Sessions with weather aborts will show a
            // larger implied overhead / lower coverage here than in the real report.
            var overheadEvents = events
                .Where(e => e.EventType != "Exposure" && e.DurationSeconds > 0)
                .ToList();
            var nonAborted = events
                .Where(e => e.DurationSeconds > 0 && e.EventType != "AbortedExposure")
                .ToList();
            if (nonAborted.Count == 0) {
                Console.WriteLine("(no events to summarize)");
                return;
            }

            var groups = overheadEvents
                .GroupBy(e => e.EventType)
                .Select(g => new {
                    Type = g.Key,
                    Count = g.Count(),
                    Total = g.Sum(e => e.DurationSeconds),
                    Avg = g.Average(e => e.DurationSeconds)
                })
                .Where(g => g.Total >= 1.0)
                .OrderByDescending(g => g.Total)
                .ToList();

            foreach (var g in groups)
                Console.WriteLine($"{g.Type,-18} {g.Count,4} × total {g.Total,10:F1}s  avg {g.Avg,7:F2}s");

            var windowStart = nonAborted.Min(e => e.StartTime);
            var windowEnd   = nonAborted.Max(e => e.EndTime);
            var windowSec   = (windowEnd - windowStart).TotalSeconds;
            var integration = events.Where(e => e.EventType == "Exposure").Sum(e => e.DurationSeconds);
            var impliedOverhead = Math.Max(0, windowSec - integration);
            var mergedOverhead  = MergeOverheadIntervals(overheadEvents);
            var coverage        = impliedOverhead > 0
                ? Math.Min(100.0, mergedOverhead / impliedOverhead * 100.0) : 0;
            var unaccounted     = Math.Max(0, impliedOverhead - mergedOverhead);
            var efficiency      = windowSec > 0 ? integration / windowSec * 100.0 : 0;

            Console.WriteLine();
            Console.WriteLine("=== overhead analysis (matches plugin report) ===");
            Console.WriteLine($"window:            {windowSec,10:F1}s  ({FormatHMS(windowSec)})");
            Console.WriteLine($"integration:       {integration,10:F1}s  ({FormatHMS(integration)})");
            Console.WriteLine($"implied overhead:  {impliedOverhead,10:F1}s  ({FormatHMS(impliedOverhead)})    = window - integration");
            Console.WriteLine($"merged overhead:   {mergedOverhead,10:F1}s  ({FormatHMS(mergedOverhead)})    wall-clock union of overhead events");
            Console.WriteLine($"unaccounted:       {unaccounted,10:F1}s  ({FormatHMS(unaccounted)})");
            Console.WriteLine();
            Console.WriteLine($"imaging efficiency:  {efficiency,5:F1}%    integration / window");
            Console.WriteLine($"overhead coverage:   {coverage,5:F1}%    merged overhead / implied overhead");
            Console.WriteLine();
            Console.WriteLine("NOTE: roof-closed time not subtracted (tool has no session-event data).");
            Console.WriteLine("      Reports for sessions with weather aborts will show different numbers.");
        }

        /// <summary>
        /// Mirror of ReportGenerator.MergeOverheadIntervals — unions overlapping event
        /// intervals so overlapping ops (ImageSave during next exposure) count once.
        /// </summary>
        private static double MergeOverheadIntervals(List<TimingEvent> events) {
            var intervals = events
                .Where(e => e.StartTime != DateTime.MinValue && e.EndTime > e.StartTime)
                .OrderBy(e => e.StartTime)
                .ToList();
            if (intervals.Count == 0) return 0;

            double total = 0;
            var curStart = intervals[0].StartTime;
            var curEnd   = intervals[0].EndTime;
            for (int i = 1; i < intervals.Count; i++) {
                if (intervals[i].StartTime <= curEnd) {
                    if (intervals[i].EndTime > curEnd) curEnd = intervals[i].EndTime;
                } else {
                    total += (curEnd - curStart).TotalSeconds;
                    curStart = intervals[i].StartTime;
                    curEnd   = intervals[i].EndTime;
                }
            }
            total += (curEnd - curStart).TotalSeconds;
            return total;
        }

        private static string FormatHMS(double seconds) {
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h{t.Minutes:D2}m{t.Seconds:D2}s";
            if (t.TotalMinutes >= 1) return $"{t.Minutes}m{t.Seconds:D2}s";
            return $"{t.TotalSeconds:F1}s";
        }

        private static void PrintUsage() {
            Console.Error.WriteLine("usage: ParseLogTool <logPath> [<sessionStart> <sessionEnd>]");
            Console.Error.WriteLine("  logPath       NINA log file");
            Console.Error.WriteLine("  sessionStart  ISO datetime (optional; defaults to log's first timestamp − 1min)");
            Console.Error.WriteLine("  sessionEnd    ISO datetime (optional; defaults to log's last timestamp + 1min)");
        }
    }
}
