using NINA.Core.Utility;
using NINA.Plugin.NightSummary.MyPluginProperties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

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

        // Secret string fields encrypted at rest via DPAPI. The in-memory settings
        // object always holds plaintext (senders use it directly); only the JSON on
        // disk is protected. To add a secret, add its name + accessors here.
        private static readonly (string Name, Func<NightSummarySettings, string> Get, Action<NightSummarySettings, string> Set)[] SecretFields = {
            ("SmtpPassword",      s => s.SmtpPassword,      (s, v) => s.SmtpPassword = v),
            ("PushoverAppToken",  s => s.PushoverAppToken,  (s, v) => s.PushoverAppToken = v),
            ("PushoverUserKey",   s => s.PushoverUserKey,   (s, v) => s.PushoverUserKey = v),
            ("DiscordWebhookUrl", s => s.DiscordWebhookUrl, (s, v) => s.DiscordWebhookUrl = v),
            ("DashboardApiKey",   s => s.DashboardApiKey,   (s, v) => s.DashboardApiKey = v),
        };

        // Protected blobs that failed to decrypt on load (settings.json moved to a
        // different Windows account/PC). We blank the in-memory value so it's treated
        // as unset, but keep the original blob here so Save() writes it back untouched
        // instead of destroying a credential that is still valid on its home machine.
        private readonly Dictionary<string, string> _undecryptable = new();

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
                        var hadLegacyPlaintext = DecryptSecrets(loaded);
                        _settings = loaded;
                        // Legacy plaintext secrets (pre-encryption settings.json) are now
                        // in memory as plaintext; persist them back in encrypted form.
                        if (hadLegacyPlaintext) Save();
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
                // Serialize to a mutable node, then replace secret fields with their
                // encrypted form — this keeps the in-memory _settings plaintext (so
                // senders keep working) while the file gets only ciphertext.
                var node = JsonSerializer.SerializeToNode(_settings, options) as JsonObject;
                if (node != null) {
                    foreach (var f in SecretFields) {
                        var plain = f.Get(_settings) ?? "";
                        string toWrite;
                        if (plain.Length > 0) {
                            toWrite = SecretProtector.Protect(plain);
                            _undecryptable.Remove(f.Name); // new/decrypted value supersedes any preserved blob
                        } else if (_undecryptable.TryGetValue(f.Name, out var preserved)) {
                            toWrite = preserved;           // couldn't decrypt on load — don't clobber it
                        } else {
                            toWrite = "";
                        }
                        node[f.Name] = toWrite;
                    }
                }
                File.WriteAllText(_path, (node?.ToJsonString(options)) ?? JsonSerializer.Serialize(_settings, options));
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to save settings. {ex.Message}");
            }
        }

        /// <summary>
        /// Decrypts secret fields in place after load. Returns true if any secret was
        /// found as legacy plaintext (no encryption marker), signalling the caller to
        /// re-save so those values get encrypted on disk.
        /// </summary>
        private bool DecryptSecrets(NightSummarySettings settings) {
            _undecryptable.Clear();
            var hadLegacyPlaintext = false;
            foreach (var f in SecretFields) {
                var stored = f.Get(settings) ?? "";
                if (stored.Length == 0) continue;
                if (SecretProtector.IsProtected(stored)) {
                    var plain = SecretProtector.Unprotect(stored);
                    if (plain == null) {
                        _undecryptable[f.Name] = stored; // preserve blob, treat as unset
                        f.Set(settings, "");
                    } else {
                        f.Set(settings, plain);
                    }
                } else {
                    hadLegacyPlaintext = true; // plaintext already in the property; encrypt on next Save
                }
            }
            return hadLegacyPlaintext;
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
