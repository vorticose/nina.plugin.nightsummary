using NINA.Core.Utility;
using NINA.Plugin.NightSummary.MyPluginProperties;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace NINA.Plugin.NightSummary.Data {

    /// <summary>
    /// Manages plugin settings persistence using a JSON file in the stable NightSummary
    /// data folder (%LOCALAPPDATA%\NINA\NightSummary\settings.json). This path is
    /// version-independent and survives both plugin updates and NINA version changes,
    /// unlike ApplicationSettingsBase which uses a versioned path that resets on update.
    /// </summary>
    public class SettingsManager {

        public static readonly string ProductionPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "NightSummary", "settings.json");

        private static readonly Lazy<SettingsManager> _instance =
            new Lazy<SettingsManager>(() => new SettingsManager(ProductionPath, attemptMigration: true));

        // Test override slot — see UseInstanceForTesting. volatile so updates are
        // visible across threads even though tests must run sequentially.
        private static volatile SettingsManager _testOverride;

        /// <summary>Production singleton — uses the stable NightSummary data folder.
        /// In tests, may be redirected via <see cref="UseInstanceForTesting"/> so that
        /// callers reading <c>SettingsManager.Instance.Current</c> see isolated test
        /// settings rather than the real production settings.json on the host.</summary>
        public static SettingsManager Instance => _testOverride ?? _instance.Value;

        /// <summary>
        /// Test-only: redirects <see cref="Instance"/> to <paramref name="testManager"/>
        /// until the returned scope is disposed. Used by replay/integration tests so they
        /// read from an isolated settings file (with all delivery channels disabled)
        /// rather than the real production settings on the test host — which would
        /// otherwise cause email/Discord/Pushover sends to fire with real credentials
        /// whenever SessionService.EndSession is exercised by a test.
        ///
        /// Supports nested overrides (LIFO restoration). Not thread-safe; tests using
        /// this must run sequentially per xUnit collection.
        /// </summary>
        internal static IDisposable UseInstanceForTesting(SettingsManager testManager) {
            if (testManager == null) throw new ArgumentNullException(nameof(testManager));
            var previous = _testOverride;
            _testOverride = testManager;
            return new TestOverrideScope(() => _testOverride = previous);
        }

        private sealed class TestOverrideScope : IDisposable {
            private Action _onDispose;
            internal TestOverrideScope(Action onDispose) { _onDispose = onDispose; }
            public void Dispose() {
                var d = _onDispose;
                _onDispose = null;
                d?.Invoke();
            }
        }

        private readonly string _path;
        private readonly bool _attemptMigration;
        private NightSummarySettings _settings;

        /// <param name="path">Path to the settings JSON file.</param>
        /// <param name="attemptMigration">
        /// When true and no JSON file exists, tries to copy values from the legacy
        /// ApplicationSettingsBase store. Pass false in tests to get clean defaults.
        /// </param>
        public SettingsManager(string path, bool attemptMigration = false) {
            _path             = path;
            _attemptMigration = attemptMigration;
        }

        public NightSummarySettings Current {
            get {
                if (_settings == null) Load();
                return _settings;
            }
        }

        public NightSummarySettings Load() {
            if (File.Exists(_path)) {
                try {
                    var json   = File.ReadAllText(_path);
                    var loaded = JsonSerializer.Deserialize<NightSummarySettings>(json);
                    if (loaded != null) {
                        // Apply defaults for new settings not present in the saved JSON.
                        // System.Text.Json leaves missing bool properties as false, not the
                        // class default. Check for fields added after initial release.
                        ApplyNewFieldDefaults(loaded, json);
                        _settings = loaded;
                        return _settings;
                    }
                } catch (Exception ex) {
                    Logger.Warning($"NightSummary: Could not read settings.json ({ex.Message}) — using defaults");
                }
            } else if (_attemptMigration) {
                var migrated = TryMigrateFromLegacy();
                if (migrated != null) {
                    _settings = migrated;
                    Save();
                    return _settings;
                }
            }

            _settings = new NightSummarySettings();
            return _settings;
        }

        /// <summary>
        /// Applies default values for settings fields that were added after the initial release.
        /// When System.Text.Json deserializes a JSON file that's missing a bool property, it
        /// defaults to false — not the class's field initializer. This method checks if the JSON
        /// is missing each new field and applies the intended default.
        /// </summary>
        private static void ApplyNewFieldDefaults(NightSummarySettings settings, string json) {
            var defaults = new NightSummarySettings();

            // v2.11.0 additions
            if (!json.Contains("ShowChartTargetChips"))
                settings.ShowChartTargetChips = defaults.ShowChartTargetChips;
            if (!json.Contains("ShowChartFilterChips"))
                settings.ShowChartFilterChips = defaults.ShowChartFilterChips;

            // v2.10.0 additions
            if (!json.Contains("ShowOverheadBreakdown"))
                settings.ShowOverheadBreakdown = defaults.ShowOverheadBreakdown;
            if (!json.Contains("ShowLiveStackImages"))
                settings.ShowLiveStackImages = defaults.ShowLiveStackImages;
            if (!json.Contains("ShowEquipmentProfile"))
                settings.ShowEquipmentProfile = defaults.ShowEquipmentProfile;
            if (!json.Contains("EquipmentVisibleFields"))
                settings.EquipmentVisibleFields = defaults.EquipmentVisibleFields;
            if (!json.Contains("SaveReportFilePattern"))
                settings.SaveReportFilePattern = defaults.SaveReportFilePattern;
        }

        /// <summary>
        /// Generates and persists a CompanionApiKey if one is not already set. Called
        /// at dashboard server startup so /api/export/* endpoints have a valid bearer
        /// token to compare against. Returns the (possibly newly generated) key.
        /// </summary>
        public string EnsureCompanionApiKey() {
            var current = Current;
            if (!string.IsNullOrEmpty(current.CompanionApiKey)) return current.CompanionApiKey;
            current.CompanionApiKey = GenerateApiKey();
            Save();
            Logger.Info("NightSummary: Generated new CompanionApiKey for export endpoints");
            return current.CompanionApiKey;
        }

        private static string GenerateApiKey() {
            // 32 bytes → 43-char URL-safe base64 (no padding). Plenty of entropy for a LAN bearer token.
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-").Replace("/", "_").TrimEnd('=');
        }

        public void Save() {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json    = JsonSerializer.Serialize(_settings, options);
                File.WriteAllText(_path, json);
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to save settings. {ex.Message}");
            }
        }

        private NightSummarySettings TryMigrateFromLegacy() {
            try {
                var l = Settings.Default;
                var s = new NightSummarySettings {
                    UseGmailSmtp           = l.UseGmailSmtp,
                    SenderAddress          = l.SenderAddress          ?? "",
                    SmtpPassword           = l.SmtpPassword           ?? "",
                    SmtpHost               = l.SmtpHost               ?? "smtp.gmail.com",
                    SmtpPort               = l.SmtpPort,
                    SmtpSsl                = l.SmtpSsl,
                    RecipientAddress       = l.RecipientAddress       ?? "",
                    EmailEnabled           = l.EmailEnabled,
                    SaveReportLocally      = l.SaveReportLocally,
                    SaveReportPath         = l.SaveReportPath         ?? "",
                    PushoverEnabled        = l.PushoverEnabled,
                    PushoverAppToken       = l.PushoverAppToken       ?? "",
                    PushoverUserKey        = l.PushoverUserKey        ?? "",
                    DiscordEnabled         = l.DiscordEnabled,
                    DiscordWebhookUrl      = l.DiscordWebhookUrl      ?? "",
                    DashboardEnabled       = l.DashboardEnabled,
                    DashboardUrl           = l.DashboardUrl           ?? "",
                    DashboardApiKey        = l.DashboardApiKey        ?? "",
                    ReportDetailLevel      = l.ReportDetailLevel,
                    ReportLightMode        = l.ReportLightMode,
                    ExpandSectionsDefault  = l.ExpandSectionsDefault,
                    ShowMoonCurve          = l.ShowMoonCurve,
                    ShowSkyThumbnails      = l.ShowSkyThumbnails,
                    ShowSessionHistory     = l.ShowSessionHistory,
                    ShowAltitudeChart      = l.ShowAltitudeChart,
                    ShowMinAltitude        = l.ShowMinAltitude,
                    ShowTSProgressBars     = l.ShowTSProgressBars,
                    ShowStarCountCV        = l.ShowStarCountCV,
                    ShowHFRGraph           = l.ShowHFRGraph,
                    ShowPerTargetIQ        = l.ShowPerTargetIQ,
                    ShowNextNightPreview   = l.ShowNextNightPreview,
                    ChartPrimaryMetric     = l.ChartPrimaryMetric,
                    ChartSecondaryMetric   = l.ChartSecondaryMetric,
                    AdditionalChartConfigs = l.AdditionalChartConfigs ?? "",
                    FilterClassifications  = l.FilterClassifications  ?? "",
                };
                Logger.Info("NightSummary: Migrated settings from legacy ApplicationSettings to settings.json");
                return s;
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: Could not migrate legacy settings (non-fatal). {ex.Message}");
                return null;
            }
        }
    }
}
