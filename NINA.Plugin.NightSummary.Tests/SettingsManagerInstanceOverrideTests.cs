using NINA.Plugin.NightSummary.Data;
using System;
using System.IO;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Regression tests for <see cref="SettingsManager.UseInstanceForTesting"/>.
    ///
    /// Background: prior to this hook, replay/integration tests created an isolated
    /// SettingsManager pointing at a temp settings.json (with all delivery channels
    /// disabled), but <see cref="SessionService"/> read from <c>SettingsManager.Instance</c>
    /// — the production singleton — and therefore actually fired real email + Discord
    /// sends using whatever credentials were on the test host. UseInstanceForTesting
    /// redirects the singleton so callers reading <c>Instance.Current</c> see the
    /// isolated test settings instead.
    /// </summary>
    public class SettingsManagerInstanceOverrideTests {

        [Fact]
        public void Instance_WithoutOverride_ReturnsProductionSingleton() {
            // Sanity: a fresh process with no override returns whatever Instance
            // normally returns. We can't assert path == ProductionPath because
            // another test on the same process may have constructed it lazily
            // — but we CAN assert it's non-null and stable across calls.
            var a = SettingsManager.Instance;
            var b = SettingsManager.Instance;
            Assert.NotNull(a);
            Assert.Same(a, b);
        }

        [Fact]
        public void UseInstanceForTesting_RedirectsInstance_AndRestoresOnDispose() {
            var tempPath = Path.Combine(Path.GetTempPath(), $"ns_override_{Guid.NewGuid():N}.json");
            try {
                var testMgr = new SettingsManager(tempPath, attemptMigration: false);
                testMgr.Load();
                testMgr.Current.EmailEnabled    = false;
                testMgr.Current.DiscordEnabled  = false;
                testMgr.Current.PushoverEnabled = false;
                testMgr.Save();

                var beforeOverride = SettingsManager.Instance;

                using (SettingsManager.UseInstanceForTesting(testMgr)) {
                    Assert.Same(testMgr, SettingsManager.Instance);
                    Assert.False(SettingsManager.Instance.Current.EmailEnabled);
                    Assert.False(SettingsManager.Instance.Current.DiscordEnabled);
                    Assert.False(SettingsManager.Instance.Current.PushoverEnabled);
                }

                // After Dispose, Instance returns whatever it returned before.
                Assert.Same(beforeOverride, SettingsManager.Instance);
            } finally {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void UseInstanceForTesting_NestedOverrides_RestoreInLifoOrder() {
            var path1 = Path.Combine(Path.GetTempPath(), $"ns_override_outer_{Guid.NewGuid():N}.json");
            var path2 = Path.Combine(Path.GetTempPath(), $"ns_override_inner_{Guid.NewGuid():N}.json");
            try {
                var outer = new SettingsManager(path1, attemptMigration: false);
                outer.Load(); outer.Save();
                var inner = new SettingsManager(path2, attemptMigration: false);
                inner.Load(); inner.Save();

                var beforeAny = SettingsManager.Instance;

                using (SettingsManager.UseInstanceForTesting(outer)) {
                    Assert.Same(outer, SettingsManager.Instance);

                    using (SettingsManager.UseInstanceForTesting(inner)) {
                        Assert.Same(inner, SettingsManager.Instance);
                    }

                    // After inner is disposed, outer is restored.
                    Assert.Same(outer, SettingsManager.Instance);
                }

                // After outer is disposed, original is restored.
                Assert.Same(beforeAny, SettingsManager.Instance);
            } finally {
                if (File.Exists(path1)) File.Delete(path1);
                if (File.Exists(path2)) File.Delete(path2);
            }
        }

        [Fact]
        public void UseInstanceForTesting_NullArgument_Throws() {
            Assert.Throws<ArgumentNullException>(() => SettingsManager.UseInstanceForTesting(null));
        }

        [Fact]
        public void UseInstanceForTesting_DoubleDispose_IsSafe() {
            var tempPath = Path.Combine(Path.GetTempPath(), $"ns_override_dd_{Guid.NewGuid():N}.json");
            try {
                var testMgr = new SettingsManager(tempPath, attemptMigration: false);
                testMgr.Load(); testMgr.Save();

                var scope = SettingsManager.UseInstanceForTesting(testMgr);
                Assert.Same(testMgr, SettingsManager.Instance);
                scope.Dispose();
                scope.Dispose(); // must not throw or unwind a second restoration
            } finally {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
    }
}
