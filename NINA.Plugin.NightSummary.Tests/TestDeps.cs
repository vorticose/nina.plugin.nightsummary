using System;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;
using NINA.Plugin.NightSummary.Dashboard.Adapters;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;

namespace NINA.Plugin.NightSummary.Tests;

// Test plumbing for the post-companion-independence world. Pre-port the
// tests just did `new ReportGenerator()` and mutated SettingsManager.Instance
// directly. Now ReportGenerator takes IPluginSettings/IDashboardLogger/
// ITargetSchedulerDatabase — TestDeps.NewReportGenerator() wires a settings
// proxy that still reads/writes the plugin singleton so existing
// `SettingsManager.Instance.Current.X = ...` test patterns keep working.
internal static class TestDeps {
    public static ReportGenerator NewReportGenerator() => new ReportGenerator(
        new SingletonProxySettings(),
        new SilentLogger(),
        new NullTargetSchedulerDatabase());

    internal sealed class SingletonProxySettings : IPluginSettings {
        public NightSummarySettings Current => SettingsManager.Instance.Current;
        public void Save() => SettingsManager.Instance.Save();
        public string PluginVersion => "test";
        public string Mode => "test";
        public string NinaVersion => "test";
    }

    internal sealed class SilentLogger : IDashboardLogger {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? ex = null) { }
        public void Debug(string message) { }
    }
}
