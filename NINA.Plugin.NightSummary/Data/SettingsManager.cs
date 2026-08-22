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

        private const string BackupSuffix = ".bak";

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
                var fromPrimary = TryReadSettings(_path);
                if (fromPrimary != null) {
                    var hadLegacyPlaintext = DecryptSecrets(fromPrimary);
                    _settings = fromPrimary;
                    // Legacy plaintext secrets (pre-encryption settings.json) are now
                    // in memory as plaintext; persist them back in encrypted form.
                    if (hadLegacyPlaintext) Save();
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
                    var hadLegacyPlaintext = DecryptSecrets(fromBackup);
                    _settings = fromBackup;
                    if (hadLegacyPlaintext) Save();
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
                var json = node?.ToJsonString(options) ?? JsonSerializer.Serialize(_settings, options);
                var tmp  = _path + ".tmp";
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
