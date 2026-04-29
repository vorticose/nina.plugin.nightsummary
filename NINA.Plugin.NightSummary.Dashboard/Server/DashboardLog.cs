using System;
using System.Diagnostics;
using System.IO;

namespace NINA.Plugin.NightSummary.Server {

    public enum LogLevel { DEBUG, INFO, WARN, ERROR }

    /// <summary>
    /// Dedicated file logger for the dashboard server. Writes to its own log file
    /// so we don't flood NINA's main log with per-request debug output.
    /// Thread-safe, size-capped with single-file rotation.
    /// </summary>
    public sealed class DashboardLog : IDisposable {

        private static DashboardLog instance;
        public static DashboardLog Instance => instance;

        private StreamWriter writer;
        private readonly object writeLock = new object();
        private readonly string logPath;
        private readonly long maxBytes;
        private long currentBytes;

        public LogLevel MinLevel { get; set; } = LogLevel.DEBUG;

        public DashboardLog(string logPath, long maxBytes = 5 * 1024 * 1024) {
            this.logPath = logPath;
            this.maxBytes = maxBytes;
        }

        public void Open() {
            lock (writeLock) {
                if (writer != null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                var fi = new FileInfo(logPath);
                currentBytes = fi.Exists ? fi.Length : 0;
                writer = new StreamWriter(logPath, append: true) { AutoFlush = true };
                Write(LogLevel.INFO, "Dashboard log opened");
            }
        }

        public void Close() {
            lock (writeLock) {
                if (writer == null) return;
                Write(LogLevel.INFO, "Dashboard log closed");
                writer.Flush();
                writer.Dispose();
                writer = null;
            }
        }

        public void Dispose() => Close();

        // ── Static convenience (use after Init) ─────────────────────────

        public static DashboardLog Init(string logPath) {
            instance?.Close();
            instance = new DashboardLog(logPath);
            instance.Open();
            return instance;
        }

        public static void Shutdown() {
            instance?.Close();
            instance = null;
        }

        /// <summary>
        /// Deletes dashboard log files in <paramref name="logsDir"/> older than
        /// <paramref name="keepDays"/> days, based on last-write time.
        /// </summary>
        public static void PurgeOldLogs(string logsDir, int keepDays = 14) {
            if (!Directory.Exists(logsDir)) return;
            var cutoff = DateTime.Now.AddDays(-keepDays);
            foreach (var file in Directory.GetFiles(logsDir, "dashboard-*.log*")) {
                try {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                } catch { }
            }
        }

        // ── Logging methods ─────────────────────────────────────────────

        public void Debug(string message) => Write(LogLevel.DEBUG, message);
        public void Info(string message)  => Write(LogLevel.INFO, message);
        public void Warn(string message)  => Write(LogLevel.WARN, message);
        public void Error(string message) => Write(LogLevel.ERROR, message);
        public void Error(string message, Exception ex) =>
            Write(LogLevel.ERROR, $"{message} {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

        /// <summary>
        /// Starts a stopwatch and returns an action that, when called with status code
        /// and optional detail, logs the completed request.
        /// Usage: var done = log.BeginRequest("GET", "/api/sessions");
        ///        ... handle request ...
        ///        done(200, "42 sessions");
        /// </summary>
        public Action<int, string> BeginRequest(string method, string path) {
            var sw = Stopwatch.StartNew();
            return (status, detail) => {
                sw.Stop();
                var msg = $"{method} {path} -> {status} ({sw.ElapsedMilliseconds}ms";
                if (!string.IsNullOrEmpty(detail)) msg += $", {detail}";
                msg += ")";
                Write(status >= 400 ? LogLevel.WARN : LogLevel.DEBUG, msg);
            };
        }

        // ── Internal ────────────────────────────────────────────────────

        private void Write(LogLevel level, string message) {
            if (level < MinLevel) return;
            lock (writeLock) {
                if (writer == null) return;
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                writer.WriteLine(line);
                currentBytes += line.Length + Environment.NewLine.Length;
                if (currentBytes >= maxBytes) Rotate();
            }
        }

        private void Rotate() {
            writer.Flush();
            writer.Dispose();
            writer = null;

            var backup = logPath + ".1";
            try {
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(logPath, backup);
            } catch {
                // If rotation fails, just truncate
                try { File.Delete(logPath); } catch { }
            }

            currentBytes = 0;
            writer = new StreamWriter(logPath, append: false) { AutoFlush = true };
        }
    }
}
