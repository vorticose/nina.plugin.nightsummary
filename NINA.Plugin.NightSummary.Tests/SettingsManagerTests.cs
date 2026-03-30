using NINA.Plugin.NightSummary.Data;
using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Tests for SettingsManager — load, save, round-trip, and error handling.
    /// All tests use a temp file path so they never touch production settings.
    /// Migration from legacy ApplicationSettingsBase is not tested here because it
    /// requires the Windows Application Settings infrastructure.
    /// </summary>
    public class SettingsManagerTests : IDisposable {

        private readonly string _path;

        public SettingsManagerTests() {
            _path = Path.Combine(Path.GetTempPath(), $"ns_settings_test_{Guid.NewGuid():N}.json");
        }

        public void Dispose() {
            if (File.Exists(_path)) File.Delete(_path);
        }

        private SettingsManager Make() => new SettingsManager(_path, attemptMigration: false);

        // ── Load ──────────────────────────────────────────────────────────────

        [Fact]
        public void Load_ReturnsDefaults_WhenFileDoesNotExist() {
            var mgr      = Make();
            var settings = mgr.Load();

            Assert.NotNull(settings);
            // Spot-check a few defaults from each section
            Assert.True(settings.UseGmailSmtp);
            Assert.Equal("smtp.gmail.com", settings.SmtpHost);
            Assert.Equal(587, settings.SmtpPort);
            Assert.False(settings.EmailEnabled);
            Assert.False(settings.DiscordEnabled);
            Assert.Equal(2, settings.ReportDetailLevel);
            Assert.True(settings.ShowMoonCurve);
            Assert.True(settings.ShowSkyThumbnails);
            Assert.Equal("", settings.FilterClassifications);
            Assert.Equal("", settings.AdditionalChartConfigs);
        }

        [Fact]
        public void Load_ReturnsDefaults_WhenFileIsEmpty() {
            File.WriteAllText(_path, "");
            var settings = Make().Load();

            Assert.NotNull(settings);
            Assert.Equal(2, settings.ReportDetailLevel);
        }

        [Fact]
        public void Load_ReturnsDefaults_WhenFileContainsInvalidJson() {
            File.WriteAllText(_path, "{ this is not valid json }}}");
            var settings = Make().Load();

            Assert.NotNull(settings);
            Assert.Equal(2, settings.ReportDetailLevel);
            Assert.True(settings.UseGmailSmtp);
        }

        [Fact]
        public void Load_ReturnsDefaults_WhenFileContainsWrongType() {
            File.WriteAllText(_path, "42");
            var settings = Make().Load();

            Assert.NotNull(settings);
            Assert.Equal(2, settings.ReportDetailLevel);
        }

        // ── Save ──────────────────────────────────────────────────────────────

        [Fact]
        public void Save_CreatesFile() {
            var mgr = Make();
            mgr.Load();
            mgr.Save();

            Assert.True(File.Exists(_path));
        }

        [Fact]
        public void Save_CreatesDirectory_WhenItDoesNotExist() {
            var nestedPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "sub", "settings.json");
            try {
                var mgr = new SettingsManager(nestedPath, attemptMigration: false);
                mgr.Load();
                mgr.Save();
                Assert.True(File.Exists(nestedPath));
            } finally {
                var dir = Path.GetDirectoryName(Path.GetDirectoryName(nestedPath));
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Save_WritesValidJson() {
            var mgr = Make();
            mgr.Load();
            mgr.Save();

            var json = File.ReadAllText(_path);
            var obj  = JsonSerializer.Deserialize<NightSummarySettings>(json);
            Assert.NotNull(obj);
        }

        // ── Round-trip ────────────────────────────────────────────────────────

        [Fact]
        public void SaveThenLoad_RoundTrips_BoolSettings() {
            var mgr = Make();
            mgr.Load();
            mgr.Current.EmailEnabled      = true;
            mgr.Current.DiscordEnabled    = true;
            mgr.Current.PushoverEnabled   = true;
            mgr.Current.ReportLightMode   = true;
            mgr.Current.ShowMoonCurve     = false;
            mgr.Current.ExpandSectionsDefault = true;
            mgr.Save();

            var mgr2     = Make();
            var settings = mgr2.Load();

            Assert.True(settings.EmailEnabled);
            Assert.True(settings.DiscordEnabled);
            Assert.True(settings.PushoverEnabled);
            Assert.True(settings.ReportLightMode);
            Assert.False(settings.ShowMoonCurve);
            Assert.True(settings.ExpandSectionsDefault);
        }

        [Fact]
        public void SaveThenLoad_RoundTrips_StringSettings() {
            var mgr = Make();
            mgr.Load();
            mgr.Current.SenderAddress     = "user@example.com";
            mgr.Current.RecipientAddress  = "recipient@example.com";
            mgr.Current.SmtpPassword      = "hunter2";
            mgr.Current.DiscordWebhookUrl = "https://discord.com/api/webhooks/123/abc";
            mgr.Current.FilterClassifications = "Luminance=B,Ha=N,OIII=N,SII=N";
            mgr.Current.AdditionalChartConfigs = "1:0|2:3";
            mgr.Save();

            var settings = Make().Load();

            Assert.Equal("user@example.com",      settings.SenderAddress);
            Assert.Equal("recipient@example.com", settings.RecipientAddress);
            Assert.Equal("hunter2",               settings.SmtpPassword);
            Assert.Equal("https://discord.com/api/webhooks/123/abc", settings.DiscordWebhookUrl);
            Assert.Equal("Luminance=B,Ha=N,OIII=N,SII=N", settings.FilterClassifications);
            Assert.Equal("1:0|2:3",               settings.AdditionalChartConfigs);
        }

        [Fact]
        public void SaveThenLoad_RoundTrips_IntSettings() {
            var mgr = Make();
            mgr.Load();
            mgr.Current.SmtpPort             = 465;
            mgr.Current.ReportDetailLevel    = 1;
            mgr.Current.ChartPrimaryMetric   = 5;
            mgr.Current.ChartSecondaryMetric = 3;
            mgr.Save();

            var settings = Make().Load();

            Assert.Equal(465, settings.SmtpPort);
            Assert.Equal(1,   settings.ReportDetailLevel);
            Assert.Equal(5,   settings.ChartPrimaryMetric);
            Assert.Equal(3,   settings.ChartSecondaryMetric);
        }

        [Fact]
        public void SaveThenLoad_RoundTrips_AllDisplayToggles() {
            var mgr = Make();
            mgr.Load();
            mgr.Current.ShowMoonCurve         = false;
            mgr.Current.ShowSkyThumbnails     = false;
            mgr.Current.ShowSessionHistory    = false;
            mgr.Current.ShowAltitudeChart     = false;
            mgr.Current.ShowMinAltitude       = false;
            mgr.Current.ShowTSProgressBars    = false;
            mgr.Current.ShowStarCountCV       = false;
            mgr.Current.ShowHFRGraph          = false;
            mgr.Current.ShowPerTargetIQ       = false;
            mgr.Current.ShowNextNightPreview  = false;
            mgr.Save();

            var settings = Make().Load();

            Assert.False(settings.ShowMoonCurve);
            Assert.False(settings.ShowSkyThumbnails);
            Assert.False(settings.ShowSessionHistory);
            Assert.False(settings.ShowAltitudeChart);
            Assert.False(settings.ShowMinAltitude);
            Assert.False(settings.ShowTSProgressBars);
            Assert.False(settings.ShowStarCountCV);
            Assert.False(settings.ShowHFRGraph);
            Assert.False(settings.ShowPerTargetIQ);
            Assert.False(settings.ShowNextNightPreview);
        }

        // ── Forward compatibility ─────────────────────────────────────────────

        [Fact]
        public void Load_UsesPropertyDefaults_ForFieldsMissingFromJson() {
            // Simulate a settings.json from an older version that didn't have all fields
            var partialJson = """
                {
                    "EmailEnabled": true,
                    "SenderAddress": "old@example.com"
                }
                """;
            File.WriteAllText(_path, partialJson);

            var settings = Make().Load();

            // Fields present in JSON are loaded
            Assert.True(settings.EmailEnabled);
            Assert.Equal("old@example.com", settings.SenderAddress);

            // Fields absent from JSON use POCO initializer defaults
            Assert.Equal(2, settings.ReportDetailLevel);
            Assert.True(settings.UseGmailSmtp);
            Assert.Equal("smtp.gmail.com", settings.SmtpHost);
            Assert.Equal(587, settings.SmtpPort);
            Assert.True(settings.ShowMoonCurve);
        }

        // ── Current property ──────────────────────────────────────────────────

        [Fact]
        public void Current_ReturnsSameInstance_OnMultipleAccesses() {
            var mgr = Make();
            var a   = mgr.Current;
            var b   = mgr.Current;
            Assert.Same(a, b);
        }

        [Fact]
        public void Current_ReturnsLoadedSettings_AfterLoad() {
            var mgr = Make();
            mgr.Load();
            mgr.Current.EmailEnabled = true;
            mgr.Save();

            var mgr2 = Make();
            Assert.True(mgr2.Current.EmailEnabled);
        }
    }
}
