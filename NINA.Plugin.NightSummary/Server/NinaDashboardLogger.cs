using System;
using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Server;

// Routes classlib-side dashboard logging into NINA's Logger so messages land
// in the standard NINA log alongside the rest of the plugin's output.
internal sealed class NinaDashboardLogger : IDashboardLogger {
    public void Info(string message)  => Logger.Info($"NightSummary: {message}");
    public void Warn(string message)  => Logger.Warning($"NightSummary: {message}");
    public void Debug(string message) => Logger.Debug($"NightSummary: {message}");
    public void Error(string message, Exception? ex = null) {
        if (ex == null) Logger.Error($"NightSummary: {message}");
        else            Logger.Error($"NightSummary: {message}", ex);
    }
}
