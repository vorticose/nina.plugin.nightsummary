using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Data;
using System;
using System.IO;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Verifies the assembly-level safety floor from TestSendGuard is actually in place.
    /// If someone deletes the [ModuleInitializer], these fail rather than the suite
    /// quietly going back to sending real email/Discord from whatever credentials the
    /// developer happens to have configured.
    /// </summary>
    public class TestSendGuardTests {

        [Fact]
        public void Floor_IsInstalled_BeforeAnyTestRuns() {
            Assert.NotNull(TestSendGuard.Installed);
        }

        [Fact]
        public void EveryDeliveryChannel_IsDisabledByDefault() {
            var s = SettingsManager.Instance.Current;
            Assert.False(s.EmailEnabled,      "EmailEnabled must be off for the whole test run");
            Assert.False(s.DiscordEnabled,    "DiscordEnabled must be off for the whole test run");
            Assert.False(s.PushoverEnabled,   "PushoverEnabled must be off for the whole test run");
            Assert.False(s.SaveReportLocally, "SaveReportLocally must be off for the whole test run");
        }

        [Fact]
        public void Instance_IsNotTheProductionSettingsFile() {
            // The whole point: SettingsManager.Instance must not be the manager backed by
            // %LOCALAPPDATA%\NINA\NightSummary\settings.json on a developer machine.
            Assert.Same(TestSendGuard.Installed, SettingsManager.Instance);
        }

        [Fact]
        public void NestedOverride_RestoresToTheFloor_NotToProduction() {
            // This is the property that makes the floor robust to a class forgetting to
            // dispose in the right order, or a drain timing out and releasing early.
            var tempPath = Path.Combine(Path.GetTempPath(), "ns_guard_nested_probe.json");
            try {
                var inner = new SettingsManager(tempPath, attemptMigration: false);
                inner.Load();
                inner.Current.DiscordEnabled = true;   // a class doing the wrong thing
                using (SettingsManager.UseInstanceForTesting(inner)) {
                    Assert.Same(inner, SettingsManager.Instance);
                }
                // Popping that override must land back on the floor, still disabled.
                Assert.Same(TestSendGuard.Installed, SettingsManager.Instance);
                Assert.False(SettingsManager.Instance.Current.DiscordEnabled);
            } finally {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void LoggerHome_IsNotProductionLocalAppData() {
            Assert.False(string.IsNullOrEmpty(TestSendGuard.IsolatedNinaHome));
            Assert.Equal(TestSendGuard.IsolatedNinaHome, CoreUtil.APPLICATIONTEMPPATH);
            var productionNina = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA");
            Assert.False(
                string.Equals(CoreUtil.APPLICATIONTEMPPATH, productionNina, StringComparison.OrdinalIgnoreCase),
                "Logger home must not be the host's real %LOCALAPPDATA%\\NINA");
        }

        [Fact]
        public void Logger_WritesUnderIsolatedHome_NotProductionLogs() {
            Logger.Info("NightSummary test log redirect probe");
            var isolatedLogs = Path.Combine(TestSendGuard.IsolatedNinaHome, "Logs");
            Assert.True(Directory.Exists(isolatedLogs));
            Assert.NotEmpty(Directory.GetFiles(isolatedLogs, "*.log"));
        }
    }
}
