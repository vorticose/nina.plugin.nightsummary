using NINA.Plugin.NightSummary.Data;
using System;
using System.IO;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Tests for the companion API key persisted via SettingsManager. The key
    /// authorizes /api/export/* endpoints; once generated it must remain stable
    /// across reads and survive a save/load round-trip.
    /// </summary>
    public class CompanionApiKeyTests : IDisposable {

        private readonly string _path;

        public CompanionApiKeyTests() {
            _path = Path.Combine(Path.GetTempPath(), $"ns_companionkey_test_{Guid.NewGuid():N}.json");
        }

        public void Dispose() {
            if (File.Exists(_path)) File.Delete(_path);
        }

        private SettingsManager Make() => new SettingsManager(_path, attemptMigration: false);

        [Fact]
        public void EnsureCompanionApiKey_GeneratesNonEmptyKeyOnFirstCall() {
            var mgr = Make();
            var key = mgr.EnsureCompanionApiKey();

            Assert.False(string.IsNullOrEmpty(key));
            Assert.True(key.Length >= 32, $"key should have meaningful entropy, got length {key.Length}");
            Assert.Equal(key, mgr.Current.CompanionApiKey);
        }

        [Fact]
        public void EnsureCompanionApiKey_IsIdempotent_DoesNotRotateExistingKey() {
            var mgr     = Make();
            var first   = mgr.EnsureCompanionApiKey();
            var second  = mgr.EnsureCompanionApiKey();
            var third   = mgr.EnsureCompanionApiKey();

            Assert.Equal(first, second);
            Assert.Equal(first, third);
        }

        [Fact]
        public void EnsureCompanionApiKey_PersistsAcrossInstances() {
            var first  = Make().EnsureCompanionApiKey();
            var second = Make().EnsureCompanionApiKey();

            Assert.Equal(first, second);
        }

        [Fact]
        public void EnsureCompanionApiKey_PreservesUserSetKeyVerbatim() {
            // If the user pastes their own key into settings.json (e.g. for cross-machine
            // re-pairing) Ensure must not clobber it.
            var mgr = Make();
            mgr.Load();
            mgr.Current.CompanionApiKey = "user-supplied-key-12345";
            mgr.Save();

            var key = Make().EnsureCompanionApiKey();
            Assert.Equal("user-supplied-key-12345", key);
        }

        [Fact]
        public void EnsureCompanionApiKey_KeysAreUniquePerInstall() {
            // Two fresh installs should generate distinct keys (i.e. RNG, not constant).
            var pathA = Path.Combine(Path.GetTempPath(), $"ns_keytest_a_{Guid.NewGuid():N}.json");
            var pathB = Path.Combine(Path.GetTempPath(), $"ns_keytest_b_{Guid.NewGuid():N}.json");
            try {
                var keyA = new SettingsManager(pathA, attemptMigration: false).EnsureCompanionApiKey();
                var keyB = new SettingsManager(pathB, attemptMigration: false).EnsureCompanionApiKey();
                Assert.NotEqual(keyA, keyB);
            } finally {
                if (File.Exists(pathA)) File.Delete(pathA);
                if (File.Exists(pathB)) File.Delete(pathB);
            }
        }
    }
}
