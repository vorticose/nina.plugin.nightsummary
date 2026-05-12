using System;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.DevHost;

internal sealed class DevDashboardLogger : IDashboardLogger {
    public void Info(string message)  => Console.WriteLine($"[INFO ] {message}");
    public void Warn(string message)  => Console.WriteLine($"[WARN ] {message}");
    public void Debug(string message) => Console.WriteLine($"[DEBUG] {message}");
    public void Error(string message, Exception? ex = null) {
        if (ex == null) Console.Error.WriteLine($"[ERROR] {message}");
        else            Console.Error.WriteLine($"[ERROR] {message} :: {ex}");
    }
}
