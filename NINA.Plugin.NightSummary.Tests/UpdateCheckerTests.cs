using System;
using System.Runtime.InteropServices;
using NINA.Plugin.NightSummary.Server;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Pure-logic tests for <see cref="UpdateChecker"/> — version comparison,
    /// version normalization, per-platform asset resolution, and the
    /// release-JSON → <see cref="UpdateInfo"/> mapping (incl. the self-update
    /// gating). The HTTP/cache path is not exercised here (needs network); these
    /// cover every branch the dashboard banner depends on.
    /// </summary>
    public class UpdateCheckerTests {

        // ── NormalizeVersion ────────────────────────────────────────────────
        [Theory]
        [InlineData("v3.2.1", "3.2.1")]
        [InlineData("3.2.1", "3.2.1")]
        [InlineData("V3.2.1", "3.2.1")]
        [InlineData("3.2.1-beta", "3.2.1")]
        [InlineData("3.2.1+abc123", "3.2.1")]
        [InlineData("v3.2.1-beta+abc", "3.2.1")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        public void NormalizeVersion_strips_prefix_and_suffix(string raw, string expected) {
            Assert.Equal(expected, UpdateChecker.NormalizeVersion(raw));
        }

        // ── IsNewer ─────────────────────────────────────────────────────────
        [Theory]
        [InlineData("3.2.0", "v3.2.1", true)]
        [InlineData("3.2.1", "v3.2.1", false)]   // same version
        [InlineData("3.2.1", "v3.2.0", false)]   // latest is older (downgrade)
        [InlineData("3.2.1", "v3.3.0", true)]
        [InlineData("3.2.1", "v4.0.0", true)]
        [InlineData("2.11.1", "v3.0.0", true)]
        [InlineData("3.2.1", "3.2.1", false)]    // no leading v
        public void IsNewer_compares_semver(string current, string latest, bool expected) {
            Assert.Equal(expected, UpdateChecker.IsNewer(current, latest));
        }

        [Theory]
        [InlineData("", "v3.2.1")]               // blank current (dev build) — never nag
        [InlineData("3.2.1", "")]                // blank tag
        [InlineData("not-a-version", "v3.2.1")]  // unparseable current
        [InlineData("3.2.1", "garbage")]         // unparseable tag
        public void IsNewer_is_false_on_unparseable_input(string current, string latest) {
            Assert.False(UpdateChecker.IsNewer(current, latest));
        }

        // A pre-release tag must not register as newer than the same stable.
        [Fact]
        public void IsNewer_treats_prerelease_core_as_the_stable_number() {
            // 3.2.1-beta normalizes to 3.2.1 — not newer than 3.2.1.
            Assert.False(UpdateChecker.IsNewer("3.2.1", "v3.2.1-beta"));
            // ...but 3.3.0-beta core (3.3.0) IS newer than 3.2.1.
            Assert.True(UpdateChecker.IsNewer("3.2.1", "v3.3.0-beta"));
        }

        // ── ResolveAssetName ────────────────────────────────────────────────
        [Fact]
        public void ResolveAssetName_windows_x64() {
            Assert.Equal("NightSummaryCompanion-win-x64.zip",
                UpdateChecker.ResolveAssetName(true, false, false, Architecture.X64));
        }

        [Fact]
        public void ResolveAssetName_mac_by_arch() {
            Assert.Equal("NightSummaryCompanion-mac-arm64.dmg",
                UpdateChecker.ResolveAssetName(false, false, true, Architecture.Arm64));
            Assert.Equal("NightSummaryCompanion-mac-x64.dmg",
                UpdateChecker.ResolveAssetName(false, false, true, Architecture.X64));
        }

        [Fact]
        public void ResolveAssetName_linux_x64_tarball() {
            Assert.Equal("NightSummaryCompanion-linux-x64.tar.gz",
                UpdateChecker.ResolveAssetName(false, true, false, Architecture.X64));
        }

        [Fact]
        public void ResolveAssetName_unsupported_combos_are_empty() {
            Assert.Equal("", UpdateChecker.ResolveAssetName(true, false, false, Architecture.Arm64));  // win arm64
            Assert.Equal("", UpdateChecker.ResolveAssetName(false, true, false, Architecture.Arm64));  // linux arm64
        }

        // ── BuildFromReleaseJson ─────────────────────────────────────────────
        private const string ReleaseJson = @"{
            ""tag_name"": ""v3.3.0"",
            ""html_url"": ""https://github.com/vorticose/nina.plugin.nightsummary/releases/tag/v3.3.0"",
            ""body"": ""Release notes here"",
            ""assets"": [
                { ""name"": ""NightSummaryCompanion-win-x64.zip"", ""browser_download_url"": ""https://example.com/win.zip"" },
                { ""name"": ""NightSummaryCompanion-mac-arm64.dmg"", ""browser_download_url"": ""https://example.com/mac.dmg"" },
                { ""name"": ""NightSummaryCompanion-linux-x64.tar.gz"", ""browser_download_url"": ""https://example.com/linux.tgz"" }
            ]
        }";

        [Fact]
        public void Build_resolves_windows_asset_and_allows_self_update() {
            var info = UpdateChecker.BuildFromReleaseJson(
                ReleaseJson, "3.2.1",
                isWindows: true, isLinux: false, isMac: false,
                Architecture.X64, UpdateStrategy.WindowsZipSwap);

            Assert.Equal("3.2.1", info.Current);
            Assert.Equal("3.3.0", info.Latest);
            Assert.True(info.UpdateAvailable);
            Assert.Equal("NightSummaryCompanion-win-x64.zip", info.AssetName);
            Assert.Equal("https://example.com/win.zip", info.AssetUrl);
            Assert.True(info.CanSelfUpdate);
            Assert.Equal("Release notes here", info.Notes);
            Assert.Contains("releases/tag/v3.3.0", info.ReleaseUrl);
        }

        [Fact]
        public void Build_NotifyOnly_strategy_blocks_self_update_even_when_available() {
            // AppImage / .deb / non-writable install: update is available and the
            // asset resolves, but the strategy says we can't self-replace.
            var info = UpdateChecker.BuildFromReleaseJson(
                ReleaseJson, "3.2.1",
                isWindows: false, isLinux: true, isMac: false,
                Architecture.X64, UpdateStrategy.NotifyOnly);

            Assert.True(info.UpdateAvailable);
            Assert.False(info.CanSelfUpdate);
            // Release URL still surfaced so the UI can offer a manual download.
            Assert.Contains("releases/tag/v3.3.0", info.ReleaseUrl);
        }

        [Fact]
        public void Build_no_update_when_current_is_latest() {
            var info = UpdateChecker.BuildFromReleaseJson(
                ReleaseJson, "3.3.0",
                isWindows: true, isLinux: false, isMac: false,
                Architecture.X64, UpdateStrategy.WindowsZipSwap);

            Assert.False(info.UpdateAvailable);
            Assert.False(info.CanSelfUpdate);
        }

        [Fact]
        public void Build_no_self_update_when_asset_missing_for_platform() {
            // Linux arm64 has no asset in the release → can't self-update even
            // though a newer version exists and the strategy would allow it.
            var info = UpdateChecker.BuildFromReleaseJson(
                ReleaseJson, "3.2.1",
                isWindows: false, isLinux: true, isMac: false,
                Architecture.Arm64, UpdateStrategy.LinuxTarballInPlace);

            Assert.True(info.UpdateAvailable);
            Assert.Equal("", info.AssetName);
            Assert.Equal("", info.AssetUrl);
            Assert.False(info.CanSelfUpdate);
        }

        // ── DecideStrategy (pure overload, all platforms) ────────────────────
        [Fact]
        public void DecideStrategy_windows_is_zip_swap() {
            Assert.Equal(UpdateStrategy.WindowsZipSwap,
                UpdateChecker.DecideStrategy(true, false, false, null, @"C:\App", _ => true));
        }

        [Fact]
        public void DecideStrategy_mac_is_app_replace() {
            Assert.Equal(UpdateStrategy.MacAppReplace,
                UpdateChecker.DecideStrategy(false, true, false, null, "/Applications/x.app", _ => true));
        }

        [Fact]
        public void DecideStrategy_linux_writable_tarball_is_in_place() {
            Assert.Equal(UpdateStrategy.LinuxTarballInPlace,
                UpdateChecker.DecideStrategy(false, false, true, null, "/home/u/.local/share/nightsummary-companion", _ => true));
        }

        [Fact]
        public void DecideStrategy_linux_appimage_is_notify_only() {
            // $APPIMAGE set → read-only mount, can't swap in place.
            Assert.Equal(UpdateStrategy.NotifyOnly,
                UpdateChecker.DecideStrategy(false, false, true, "/home/u/App.AppImage", "/tmp/.mount_abc", _ => true));
        }

        [Fact]
        public void DecideStrategy_linux_nonwritable_is_notify_only() {
            // .deb under /usr — root-owned, not writable by the companion's user.
            Assert.Equal(UpdateStrategy.NotifyOnly,
                UpdateChecker.DecideStrategy(false, false, true, null, "/usr/lib/nightsummary-companion", _ => false));
        }

        [Fact]
        public void DecideStrategy_linux_null_dir_is_notify_only() {
            Assert.Equal(UpdateStrategy.NotifyOnly,
                UpdateChecker.DecideStrategy(false, false, true, null, null, _ => true));
        }

        [Fact]
        public void DecideStrategy_unknown_os_is_notify_only() {
            Assert.Equal(UpdateStrategy.NotifyOnly,
                UpdateChecker.DecideStrategy(false, false, false, null, "/x", _ => true));
        }

        // ── URL building + NS_UPDATE_BASE_URL test seam ──────────────────────
        [Fact]
        public void Urls_default_to_github_when_no_override() {
            var prior = Environment.GetEnvironmentVariable("NS_UPDATE_BASE_URL");
            Environment.SetEnvironmentVariable("NS_UPDATE_BASE_URL", null);
            try {
                Assert.Equal("https://api.github.com/repos/vorticose/nina.plugin.nightsummary/releases/latest",
                    UpdateChecker.ReleaseApiUrl());
                Assert.Equal("https://github.com/vorticose/nina.plugin.nightsummary/releases/latest/download/checksums.txt",
                    UpdateChecker.DownloadUrl("checksums.txt"));
            } finally {
                Environment.SetEnvironmentVariable("NS_UPDATE_BASE_URL", prior);
            }
        }

        [Fact]
        public void Urls_use_override_base_and_trim_trailing_slash() {
            var prior = Environment.GetEnvironmentVariable("NS_UPDATE_BASE_URL");
            Environment.SetEnvironmentVariable("NS_UPDATE_BASE_URL", "http://localhost:9999/");
            try {
                Assert.Equal("http://localhost:9999/releases/latest", UpdateChecker.ReleaseApiUrl());
                Assert.Equal("http://localhost:9999/releases/latest/download/install-companion-mac.sh",
                    UpdateChecker.DownloadUrl("install-companion-mac.sh"));
            } finally {
                Environment.SetEnvironmentVariable("NS_UPDATE_BASE_URL", prior);
            }
        }
    }
}
