using System;
using System.IO;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Companion.Adapters;

// Console + file logger for the companion. Writes to stdout (so the user sees
// progress when run interactively) and appends to a daily file under logs/ so
// background runs leave a trace.
internal sealed class CompanionLogger : IDashboardLogger {
    private readonly string _logsDir;
    private readonly object _lock = new();

    public CompanionLogger(string logsDir) {
        _logsDir = logsDir;
        Directory.CreateDirectory(_logsDir);
    }

    public void Info(string m)  => Write("INFO ", m);
    public void Warn(string m)  => Write("WARN ", m);
    public void Debug(string m) => Write("DEBUG", m);
    public void Error(string m, Exception? ex = null) => Write("ERROR", ex == null ? m : $"{m} :: {ex}");

    private void Write(string level, string message) {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var line  = $"[{stamp}] {level} {message}";
        lock (_lock) {
            Console.WriteLine(line);
            try {
                var file = Path.Combine(_logsDir, $"companion-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(file, line + Environment.NewLine);
            } catch { /* don't let logging crash the run */ }
        }
    }
}
