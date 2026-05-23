using NINA.Core.Utility;
using NINA.Plugin.NightSummary.MyPluginProperties;
using System;
using System.IO;
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

        /// <summary>Production singleton — uses the stable NightSummary data folder.</summary>
        public static SettingsManager Instance => _instance.Value;

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
