using System;

namespace NINA.Plugin.NightSummary.Dashboard.Abstractions;

public interface IDashboardLogger {
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
    void Debug(string message);
}
