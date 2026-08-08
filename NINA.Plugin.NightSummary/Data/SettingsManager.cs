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

        private const string BackupSuffix = ".bak";

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
                var fromPrimary = TryReadSettings(_path);
                if (fromPrimary != null) {
                    _settings = fromPrimary;
                    return _settings;
                }

                // Primary is corrupt or unreadable (e.g. a torn write from a crash or
                // force-kill). Recover from the last-good backup instead of silently
                // wiping the user's toggles/webhook back to defaults.
                var backupPath  = _path + BackupSuffix;
                var fromBackup  = TryReadSettings(backupPath);
                if (fromBackup != null) {
                    Logger.Warning("NightSummary: settings.json was corrupt — recovered from settings.json.bak");
                    try { File.Copy(backupPath, _path, overwrite: true); } catch { /* best-effort restore */ }
                    _settings = fromBackup;
                    return _settings;
                }

                Logger.Warning("NightSummary: settings.json and settings.json.bak were both unreadable — using defaults");
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

        // Reads + parses a settings file, returning null if it's absent, empty,
        // or unparseable so the caller can fall through to a backup / defaults.
        private static NightSummarySettings TryReadSettings(string path) {
            if (!File.Exists(path)) return null;
            try {
                var json   = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<NightSummarySettings>(json);
                if (loaded == null) return null;

                // Apply defaults for new settings not present in the saved JSON.
                // System.Text.Json leaves missing bool properties as false, not the
                // class default. Check for fields added after initial release.
                ApplyNewFieldDefaults(loaded, json);
                return loaded;
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: Could not read {Path.GetFileName(path)} ({ex.Message})");
                return null;
            }
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
                var tmp     = _path + ".tmp";
                File.WriteAllText(tmp, json);

                // Atomic swap that preserves the previous good copy as settings.json.bak.
                // File.Replace is atomic on NTFS/APFS/ext4 (a reader sees the old or the
                // new file, never a torn one) and rotates the existing file into the .bak
                // slot in the same operation — so a crash mid-save can't cost the user
                // their toggles/webhook.
                if (File.Exists(_path)) {
                    try {
                        File.Replace(tmp, _path, _path + BackupSuffix);
                        return;
                    } catch (IOException)                { /* rename blocked — fall through */ }
                      catch (UnauthorizedAccessException) { /* fall through */ }
                      catch (PlatformNotSupportedException) { /* fall through */ }
                }
                // First write, or File.Replace unavailable on this FS — plain overwrite.
                File.Move(tmp, _path, overwrite: true);
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
