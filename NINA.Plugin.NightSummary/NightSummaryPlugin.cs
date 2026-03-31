using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Session;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NINA.Plugin.NightSummary {

    [Export(typeof(IPluginManifest))]
    public class NightSummaryPlugin : PluginBase, INotifyPropertyChanged {

        private readonly SessionService sessionService;
        private readonly IProfileService profileService;
        private readonly string liveDbPath;
        private ObservableCollection<SessionRecord> _availableSessions = new ObservableCollection<SessionRecord>();
        public ObservableCollection<SessionRecord> AvailableSessions {
            get => _availableSessions;
            private set { _availableSessions = value; RaisePropertyChanged(); }
        }

        private SessionRecord _selectedSession;
        public SessionRecord SelectedSession {
            get => _selectedSession;
            set { _selectedSession = value; RaisePropertyChanged(); }
        }

        private DateTime _searchFrom = DateTime.Today.AddMonths(-1);
        public DateTime SearchFrom {
            get => _searchFrom;
            set { _searchFrom = value; RaisePropertyChanged(); }
        }

        private DateTime _searchTo = DateTime.Today;
        public DateTime SearchTo {
            get => _searchTo;
            set { _searchTo = value; RaisePropertyChanged(); }
        }

        private string _searchResultText = "";
        public string SearchResultText {
            get => _searchResultText;
            set { _searchResultText = value; RaisePropertyChanged(); }
        }

        public ButtonStatus EmailTestStatus   { get; } = new ButtonStatus();
        public ButtonStatus DiscordTestStatus { get; } = new ButtonStatus();
        public ButtonStatus PushoverTestStatus{ get; } = new ButtonStatus();
        public ButtonStatus ResendStatus      { get; } = new ButtonStatus();
        public ButtonStatus TestReportStatus  { get; } = new ButtonStatus();

        [ImportingConstructor]
        public NightSummaryPlugin(
            IProfileService profileService,
            IOptionsVM options,
            IImageSaveMediator imageSaveMediator,
            IMessageBroker messageBroker,
            SessionService sessionService) {

            this.sessionService = sessionService;
            this.profileService = profileService;
            sessionService.SetMessageBroker(messageBroker);

            TestEmailCommand = new RelayCommand(async () => {
                EmailTestStatus.Text = "";
                var senderAddr = S.SenderAddress;
                var password   = S.SmtpPassword;
                var recipient  = S.RecipientAddress;
                var smtpHost   = S.SmtpHost;
                if (string.IsNullOrWhiteSpace(senderAddr) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(recipient)) {
                    EmailTestStatus.Text = "✗ Fill in all email fields first";
                    return;
                }
                if (!senderAddr.Contains("@")) {
                    EmailTestStatus.Text = "✗ Sender doesn't look like an email address";
                    return;
                }
                if (!recipient.Contains("@")) {
                    EmailTestStatus.Text = "✗ Recipient doesn't look like an email address";
                    return;
                }
                if (string.IsNullOrWhiteSpace(smtpHost)) {
                    EmailTestStatus.Text = "✗ SMTP server is required";
                    return;
                }
                bool useGmail = S.UseGmailSmtp;
                var sender = new EmailSender(
                    useGmail ? "smtp.gmail.com" : smtpHost,
                    useGmail ? 587 : S.SmtpPort,
                    useGmail ? true : S.SmtpSsl,
                    senderAddr, password, recipient);
                bool ok = await sender.SendTestAsync();
                EmailTestStatus.Text = ok ? "✓ Sent" : "✗ Failed — check NINA log";
            });

            TestDiscordCommand = new RelayCommand(async () => {
                DiscordTestStatus.Text = "";
                var url = S.DiscordWebhookUrl;
                if (string.IsNullOrWhiteSpace(url)) {
                    DiscordTestStatus.Text = "✗ Webhook URL is empty";
                    return;
                }

                var sender = new DiscordSender(url);
                bool ok = await sender.SendTestAsync();
                DiscordTestStatus.Text = ok ? "✓ Sent" : "✗ Failed — check NINA log";
            });

            TestPushoverCommand = new RelayCommand(async () => {
                PushoverTestStatus.Text = "";
                var appToken = S.PushoverAppToken;
                var userKey  = S.PushoverUserKey;
                if (string.IsNullOrWhiteSpace(appToken) || string.IsNullOrWhiteSpace(userKey)) {
                    PushoverTestStatus.Text = "✗ App token or user key is empty";
                    return;
                }
                if (appToken.Length < 20 || appToken.Contains(" ")) {
                    PushoverTestStatus.Text = "✗ App token looks wrong — double-check you copied it correctly";
                    return;
                }
                if (userKey.Length < 20 || userKey.Contains(" ")) {
                    PushoverTestStatus.Text = "✗ User key looks wrong — double-check you copied it correctly";
                    return;
                }
                var sender = new PushoverSender(appToken, userKey);
                bool ok = await sender.SendAsync("Night Summary", "Pushover is configured correctly!");
                PushoverTestStatus.Text = ok ? "✓ Sent" : "✗ Failed — check NINA log";
            });

            SendTestReportCommand = new RelayCommand(async () => {
                TestReportStatus.Text = "";
                var testDbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NINA", "NightSummary", "test", "nightsummary.sqlite");

                if (!File.Exists(testDbPath)) {
                    Logger.Warning($"NightSummary: Test database not found at {testDbPath}");
                    TestReportStatus.Text = "✗ Test database not found";
                    return;
                }

                await this.sessionService.SendFromDatabaseAsync(testDbPath);
                TestReportStatus.Text = "✓ Sent";
            });

            liveDbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA", "NightSummary", "nightsummary.sqlite");

            RefreshSessionsCommand = new RelayCommand(async () => {
                SearchResultText = "";
                await Task.Run(() => LoadSessions());
            });

            SearchSessionsCommand = new RelayCommand(async () => {
                if (!File.Exists(liveDbPath)) return;
                await Task.Run(() => {
                    try {
                        var db       = new SessionDatabase(liveDbPath);
                        var sessions = db.GetSessionsByDateRange(SearchFrom, SearchTo);
                        System.Windows.Application.Current.Dispatcher.Invoke(() => {
                            AvailableSessions.Clear();
                            foreach (var s in sessions)
                                AvailableSessions.Add(s);
                            SelectedSession = AvailableSessions.Count > 0 ? AvailableSessions[0] : null;
                            SearchResultText = sessions.Count == 0
                                ? "No sessions found in that range"
                                : $"{sessions.Count} session{(sessions.Count == 1 ? "" : "s")} found";
                        });
                    } catch (Exception ex) {
                        Logger.Error($"NightSummary: Failed to search sessions. {ex.Message}");
                    }
                });
            });

            ClearSearchCommand = new RelayCommand(async () => {
                SearchResultText = "";
                await Task.Run(() => LoadSessions());
            });

            ResendSessionCommand = new RelayCommand(async () => {
                ResendStatus.Text = "";
                if (!File.Exists(liveDbPath)) {
                    Logger.Warning($"NightSummary: Live database not found at {liveDbPath}");
                    ResendStatus.Text = "✗ No session database found";
                    return;
                }
                if (SelectedSession == null) {
                    ResendStatus.Text = "✗ No session selected";
                    return;
                }
                await this.sessionService.SendFromDatabaseAsync(liveDbPath, SelectedSession.SessionId);
                ResendStatus.Text = "✓ Sent";
            });

            // Keep old name pointing to the same command for backwards compat
            ResendLastSessionCommand = ResendSessionCommand;

            RefreshFiltersCommand = new RelayCommand(async () => {
                await Task.Run(() => LoadFilterClassifications());
            });

            PreviewReportCommand = new RelayCommand(async () => {
                var window = new PreviewWindow(sessionService);
                window.Show();
            });

            LoadSessions();
            LoadFilterClassifications();

            Logger.Info("NightSummary: Plugin initialized successfully");
        }

        public override async Task Teardown() {
            SettingsManager.Instance.Save();
            Logger.Info("NightSummary: Plugin torn down");
            await base.Teardown();
        }

        // Settings properties bound to the Options UI
        private NightSummarySettings S => SettingsManager.Instance.Current;
        private void SaveSettings() => SettingsManager.Instance.Save();

        public bool UseGmailSmtp {
            get => S.UseGmailSmtp;
            set { S.UseGmailSmtp = value; SaveSettings(); RaisePropertyChanged(); RaisePropertyChanged(nameof(UseCustomSmtp)); }
        }

        public bool UseCustomSmtp {
            get => !S.UseGmailSmtp;
            set { S.UseGmailSmtp = !value; SaveSettings(); RaisePropertyChanged(); RaisePropertyChanged(nameof(UseGmailSmtp)); }
        }

        public string SenderAddress {
            get => S.SenderAddress;
            set { S.SenderAddress = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public string SmtpPassword {
            get => S.SmtpPassword;
            set { S.SmtpPassword = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public string SmtpHost {
            get => S.SmtpHost;
            set { S.SmtpHost = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public int SmtpPort {
            get => S.SmtpPort;
            set { S.SmtpPort = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool SmtpSsl {
            get => S.SmtpSsl;
            set { S.SmtpSsl = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public string RecipientAddress {
            get => S.RecipientAddress;
            set { S.RecipientAddress = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool SaveReportLocally {
            get => S.SaveReportLocally;
            set { S.SaveReportLocally = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public string SaveReportPath {
            get => S.SaveReportPath;
            set { S.SaveReportPath = value; SaveSettings(); RaisePropertyChanged(); RaisePropertyChanged(nameof(SaveReportPatternPreview)); }
        }

        public string SaveReportFilePattern {
            get => S.SaveReportFilePattern;
            set { S.SaveReportFilePattern = value; SaveSettings(); RaisePropertyChanged(); RaisePropertyChanged(nameof(SaveReportPatternPreview)); }
        }

        public string SaveReportPatternPreview {
            get {
                var pattern = S.SaveReportFilePattern;
                if (string.IsNullOrWhiteSpace(pattern)) return "NightSummary_<timestamp>.html";
                var preview = new Dictionary<string, string> {
                    ["$$CAMERA$$"] = "ZWO ASI2600MM",
                    ["$$TELESCOPE$$"] = "My Telescope",
                    ["$$SEQUENCETITLE$$"] = "MySequence"
                };
                var resolved = Session.SessionService.ResolveFilePattern(pattern, preview) + ".html";
                return resolved.Replace("\\", " \u203A ");
            }
        }

        public bool EmailEnabled {
            get => S.EmailEnabled;
            set { S.EmailEnabled = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool PushoverEnabled {
            get => S.PushoverEnabled;
            set { S.PushoverEnabled = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public string PushoverAppToken {
            get => S.PushoverAppToken;
            set { S.PushoverAppToken = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public string PushoverUserKey {
            get => S.PushoverUserKey;
            set { S.PushoverUserKey = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool DiscordEnabled {
            get => S.DiscordEnabled;
            set { S.DiscordEnabled = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public string DiscordWebhookUrl {
            get => S.DiscordWebhookUrl;
            set { S.DiscordWebhookUrl = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public int ReportDetailLevel {
            get => S.ReportDetailLevel;
            set {
                S.ReportDetailLevel     = value;
                S.ShowSkyThumbnails     = true;
                S.ShowLiveStackImages   = true;
                S.ShowAltitudeChart     = true;
                S.ShowMoonCurve         = true;
                S.ShowMinAltitude       = true;
                S.ShowTSProgressBars    = true;
                S.ShowSessionHistory    = true;
                S.ShowStarCountCV       = true;
                S.ShowHFRGraph          = true;
                S.ShowPerTargetIQ       = true;
                S.ShowNextNightPreview  = true;
                SaveSettings();
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ShowSkyThumbnails));
                RaisePropertyChanged(nameof(ShowLiveStackImages));
                RaisePropertyChanged(nameof(ShowAltitudeChart));
                RaisePropertyChanged(nameof(ShowMoonCurve));
                RaisePropertyChanged(nameof(ShowMinAltitude));
                RaisePropertyChanged(nameof(ShowTSProgressBars));
                RaisePropertyChanged(nameof(ShowSessionHistory));
                RaisePropertyChanged(nameof(ShowStarCountCV));
                RaisePropertyChanged(nameof(ShowHFRGraph));
                RaisePropertyChanged(nameof(ShowPerTargetIQ));
                RaisePropertyChanged(nameof(ShowNextNightPreview));
            }
        }

        public bool ShowSkyThumbnails {
            get => S.ShowSkyThumbnails;
            set { S.ShowSkyThumbnails = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowLiveStackImages {
            get => S.ShowLiveStackImages;
            set { S.ShowLiveStackImages = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowMoonCurve {
            get => S.ShowMoonCurve;
            set { S.ShowMoonCurve = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowMinAltitude {
            get => S.ShowMinAltitude;
            set { S.ShowMinAltitude = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowSessionHistory {
            get => S.ShowSessionHistory;
            set { S.ShowSessionHistory = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowAltitudeChart {
            get => S.ShowAltitudeChart;
            set { S.ShowAltitudeChart = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowTSProgressBars {
            get => S.ShowTSProgressBars && IsTsInstalled;
            set { S.ShowTSProgressBars = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowStarCountCV {
            get => S.ShowStarCountCV;
            set { S.ShowStarCountCV = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowHFRGraph {
            get => S.ShowHFRGraph;
            set { S.ShowHFRGraph = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowPerTargetIQ {
            get => S.ShowPerTargetIQ;
            set { S.ShowPerTargetIQ = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ReportLightMode {
            get => S.ReportLightMode;
            set { S.ReportLightMode = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ExpandSectionsDefault {
            get => S.ExpandSectionsDefault;
            set { S.ExpandSectionsDefault = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public int ChartPrimaryMetric {
            get => S.ChartPrimaryMetric;
            set { S.ChartPrimaryMetric = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public int ChartSecondaryMetric {
            get => S.ChartSecondaryMetric;
            set { S.ChartSecondaryMetric = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public const int MaxAdditionalCharts = 4;

        private static readonly List<string> _primaryMetricNames = new List<string> {
            "HFR", "FWHM", "Guiding RMS", "Focuser Temp (°C)", "Ambient Temp (°C)",
            "Eccentricity", "Altitude (°)", "Airmass", "Humidity (%)", "Focuser Position (steps)",
            "Sky Quality (mag/arcsec²)", "Cloud Cover (%)", "Camera Temp (°C)", "Dew Point (°C)",
            "Wind Speed (m/s)", "Pressure (hPa)", "Star Count", "Azimuth (°)", "Seeing FWHM (arcsec)"
        };

        private static readonly List<string> _secondaryMetricNames = new List<string> {
            "None", "HFR", "FWHM", "Guiding RMS", "Focuser Temp (°C)", "Ambient Temp (°C)",
            "Eccentricity", "Altitude (°)", "Airmass", "Humidity (%)", "Focuser Position (steps)",
            "Sky Quality (mag/arcsec²)", "Cloud Cover (%)", "Camera Temp (°C)", "Dew Point (°C)",
            "Wind Speed (m/s)", "Pressure (hPa)", "Star Count", "Azimuth (°)", "Seeing FWHM (arcsec)"
        };

        public IReadOnlyList<string> PrimaryMetricNames  => _primaryMetricNames;
        public IReadOnlyList<string> SecondaryMetricNames => _secondaryMetricNames;

        private ObservableCollection<ChartConfig> _additionalCharts;
        public ObservableCollection<ChartConfig> AdditionalCharts {
            get {
                if (_additionalCharts == null) {
                    _additionalCharts = DeserializeChartConfigs(S.AdditionalChartConfigs);
                    _additionalCharts.CollectionChanged += (_, __) => {
                        SerializeChartConfigs();
                        RaisePropertyChanged(nameof(CanAddChart));
                    };
                }
                return _additionalCharts;
            }
        }

        public bool CanAddChart => AdditionalCharts.Count < MaxAdditionalCharts;

        public void AddAdditionalChart() {
            if (AdditionalCharts.Count >= MaxAdditionalCharts) return;
            AdditionalCharts.Add(new ChartConfig(0, 0, SerializeChartConfigs));
        }

        public void RemoveAdditionalChart(ChartConfig config) {
            AdditionalCharts.Remove(config);
        }

        private void SerializeChartConfigs() {
            S.AdditionalChartConfigs =
                string.Join("|", AdditionalCharts.Select(c => $"{c.Primary}:{c.Secondary}"));
            SaveSettings();
        }

        private ObservableCollection<ChartConfig> DeserializeChartConfigs(string raw) {
            var col = new ObservableCollection<ChartConfig>();
            if (string.IsNullOrWhiteSpace(raw)) return col;
            foreach (var part in raw.Split('|')) {
                var tokens = part.Split(':');
                if (tokens.Length == 2
                    && int.TryParse(tokens[0], out int p) && p >= 0 && p < _primaryMetricNames.Count
                    && int.TryParse(tokens[1], out int s) && s >= 0 && s <= _secondaryMetricNames.Count) {
                    col.Add(new ChartConfig(p, s, SerializeChartConfigs));
                }
            }
            return col;
        }

        public bool ShowNextNightPreview {
            get => S.ShowNextNightPreview && IsTsInstalled && IsTsApiEnabled;
            set { S.ShowNextNightPreview = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool IsTsInstalled => TargetSchedulerDatabase.IsPluginInstalled;

        public bool IsTsApiEnabled {
            get {
                if (!IsTsInstalled) return false;
                var tsDb = new TargetSchedulerDatabase();
                if (!tsDb.IsAvailable) return false;
                var profileId = profileService?.ActiveProfile?.Id.ToString();
                var (enabled, _) = tsDb.GetApiSettings(profileId);
                return enabled;
            }
        }

        // ── Filter classification ──

        private ObservableCollection<FilterClassificationItem> _filterItems = new ObservableCollection<FilterClassificationItem>();
        public ObservableCollection<FilterClassificationItem> FilterItems {
            get => _filterItems;
            private set { _filterItems = value; RaisePropertyChanged(); }
        }

        public ICommand RefreshFiltersCommand { get; private set; }

        private void LoadFilterClassifications() {
            try {
                var filters = profileService?.ActiveProfile?.FilterWheelSettings?.FilterWheelFilters;
                if (filters == null || filters.Count == 0) return;

                var saved = ParseFilterClassifications(S.FilterClassifications);

                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    FilterItems.Clear();
                    foreach (var f in filters) {
                        if (string.IsNullOrWhiteSpace(f.Name)) continue;
                        var item = new FilterClassificationItem(f.Name, this);
                        if (saved.TryGetValue(f.Name, out var cls))
                            item.Classification = cls;
                        FilterItems.Add(item);
                    }
                });
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to load filter classifications. {ex.Message}");
            }
        }

        internal void SaveFilterClassifications() {
            var parts = FilterItems
                .Where(f => f.Classification != "A")
                .Select(f => $"{f.Name}={f.Classification}");
            S.FilterClassifications = string.Join(",", parts);
            SaveSettings();
        }

        internal static Dictionary<string, string> ParseFilterClassifications(string raw) =>
            FilterHelper.ParseClassifications(raw);

        private void LoadSessions() {
            try {
                if (!File.Exists(liveDbPath)) return;
                var db       = new SessionDatabase(liveDbPath);
                var sessions = db.GetRecentSessions(30);
                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    AvailableSessions.Clear();
                    foreach (var s in sessions)
                        AvailableSessions.Add(s);
                    if (SelectedSession == null && AvailableSessions.Count > 0)
                        SelectedSession = AvailableSessions[0];
                });
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to load session list. {ex.Message}");
            }
        }

        public ICommand TestEmailCommand { get; }
        public ICommand TestDiscordCommand { get; }
        public ICommand TestPushoverCommand { get; }
        public ICommand SendTestReportCommand { get; }
        public ICommand ResendLastSessionCommand { get; }
        public ICommand ResendSessionCommand { get; }
        public ICommand RefreshSessionsCommand { get; }
        public ICommand SearchSessionsCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand PreviewReportCommand { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Bindable status indicator for action buttons in the Options UI.
    /// Text starting with "✓" is shown in green; anything else in red.
    /// </summary>
    public class ButtonStatus : INotifyPropertyChanged {
        private string _text = "";
        public string Text {
            get => _text;
            set {
                _text = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Foreground)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Visibility)));
            }
        }

        public System.Windows.Media.Brush Foreground =>
            _text.StartsWith("✓")
                ? System.Windows.Media.Brushes.LightGreen
                : System.Windows.Media.Brushes.Salmon;

        public System.Windows.Visibility Visibility =>
            string.IsNullOrEmpty(_text)
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// Minimal async-capable relay command for the Options UI.
    /// </summary>
    internal class RelayCommand : ICommand {
        private readonly Func<Task> execute;
        private bool isExecuting;

        public RelayCommand(Func<Task> execute) {
            this.execute = execute;
        }

        public bool CanExecute(object parameter) => !isExecuting;

        public async void Execute(object parameter) {
            isExecuting = true;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            try {
                await execute();
            } finally {
                isExecuting = false;
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler CanExecuteChanged;
    }

    /// <summary>
    /// Represents a single filter with its user-assigned classification.
    /// Classification values: A=Auto (first-letter matching), B=Broadband, N=Narrowband, X=Exclude.
    /// </summary>
    public class FilterClassificationItem : INotifyPropertyChanged {
        private readonly NightSummaryPlugin plugin;
        private string _classification = "A";

        public FilterClassificationItem(string name, NightSummaryPlugin plugin) {
            Name = name;
            this.plugin = plugin;
        }

        public string Name { get; }

        public string Classification {
            get => _classification;
            set {
                if (_classification == value) return;
                _classification = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Classification)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ClassificationIndex)));
                plugin?.SaveFilterClassifications();
            }
        }

        /// <summary>
        /// ComboBox binding: 0=Auto, 1=Broadband, 2=Narrowband, 3=Exclude
        /// </summary>
        public int ClassificationIndex {
            get {
                switch (_classification) {
                    case "B": return 1;
                    case "N": return 2;
                    case "X": return 3;
                    default:  return 0;
                }
            }
            set {
                switch (value) {
                    case 1:  Classification = "B"; break;
                    case 2:  Classification = "N"; break;
                    case 3:  Classification = "X"; break;
                    default: Classification = "A"; break;
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class ChartConfig : INotifyPropertyChanged {
        private readonly Action _onChanged;
        private int _primary;
        private int _secondary;

        public ChartConfig(int primary, int secondary, Action onChanged) {
            _primary   = primary;
            _secondary = secondary;
            _onChanged = onChanged;
        }

        public int Primary {
            get => _primary;
            set { _primary = value; OnPropertyChanged(); _onChanged(); }
        }

        public int Secondary {
            get => _secondary;
            set { _secondary = value; OnPropertyChanged(); _onChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
