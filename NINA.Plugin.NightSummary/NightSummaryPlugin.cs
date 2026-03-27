using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Plugin.NightSummary.MyPluginProperties;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Session;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
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
            SessionService sessionService) {

            this.sessionService = sessionService;
            this.profileService = profileService;

            TestEmailCommand = new RelayCommand(async () => {
                EmailTestStatus.Text = "";
                var senderAddr = Settings.Default.SenderAddress;
                var password   = Settings.Default.SmtpPassword;
                var recipient  = Settings.Default.RecipientAddress;
                var smtpHost   = Settings.Default.SmtpHost;
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
                bool useGmail = Settings.Default.UseGmailSmtp;
                var sender = new EmailSender(
                    useGmail ? "smtp.gmail.com" : smtpHost,
                    useGmail ? 587 : Settings.Default.SmtpPort,
                    useGmail ? true : Settings.Default.SmtpSsl,
                    senderAddr, password, recipient);
                bool ok = await sender.SendTestAsync();
                EmailTestStatus.Text = ok ? "✓ Sent" : "✗ Failed — check NINA log";
            });

            TestDiscordCommand = new RelayCommand(async () => {
                DiscordTestStatus.Text = "";
                var url = Settings.Default.DiscordWebhookUrl;
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
                var appToken = Settings.Default.PushoverAppToken;
                var userKey  = Settings.Default.PushoverUserKey;
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
            Settings.Default.Save();
            Logger.Info("NightSummary: Plugin torn down");
            await base.Teardown();
        }

        // Settings properties bound to the Options UI
        public bool UseGmailSmtp {
            get => Settings.Default.UseGmailSmtp;
            set {
                Settings.Default.UseGmailSmtp = value;
                Settings.Default.Save();
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(UseCustomSmtp));
            }
        }

        public bool UseCustomSmtp {
            get => !Settings.Default.UseGmailSmtp;
            set {
                Settings.Default.UseGmailSmtp = !value;
                Settings.Default.Save();
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(UseGmailSmtp));
            }
        }

        public string SenderAddress {
            get => Settings.Default.SenderAddress;
            set {
                Settings.Default.SenderAddress = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public string SmtpPassword {
            get => Settings.Default.SmtpPassword;
            set {
                Settings.Default.SmtpPassword = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public string SmtpHost {
            get => Settings.Default.SmtpHost;
            set {
                Settings.Default.SmtpHost = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public int SmtpPort {
            get => Settings.Default.SmtpPort;
            set {
                Settings.Default.SmtpPort = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public bool SmtpSsl {
            get => Settings.Default.SmtpSsl;
            set {
                Settings.Default.SmtpSsl = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public string RecipientAddress {
            get => Settings.Default.RecipientAddress;
            set {
                Settings.Default.RecipientAddress = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public bool SaveReportLocally {
            get => Settings.Default.SaveReportLocally;
            set {
                Settings.Default.SaveReportLocally = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public string SaveReportPath {
            get => Settings.Default.SaveReportPath;
            set {
                Settings.Default.SaveReportPath = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public bool EmailEnabled {
            get => Settings.Default.EmailEnabled;
            set {
                Settings.Default.EmailEnabled = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public bool PushoverEnabled {
            get => Settings.Default.PushoverEnabled;
            set {
                Settings.Default.PushoverEnabled = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public string PushoverAppToken {
            get => Settings.Default.PushoverAppToken;
            set {
                Settings.Default.PushoverAppToken = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public string PushoverUserKey {
            get => Settings.Default.PushoverUserKey;
            set {
                Settings.Default.PushoverUserKey = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public bool DiscordEnabled {
            get => Settings.Default.DiscordEnabled;
            set {
                Settings.Default.DiscordEnabled = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public string DiscordWebhookUrl {
            get => Settings.Default.DiscordWebhookUrl;
            set {
                Settings.Default.DiscordWebhookUrl = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public int ReportDetailLevel {
            get => Settings.Default.ReportDetailLevel;
            set {
                Settings.Default.ReportDetailLevel = value;
                Settings.Default.ShowSkyThumbnails  = true;
                Settings.Default.ShowAltitudeChart  = true;
                Settings.Default.ShowMoonCurve      = true;
                Settings.Default.ShowMinAltitude    = true;
                Settings.Default.ShowTSProgressBars = true;
                Settings.Default.ShowSessionHistory = true;
                Settings.Default.ShowStarCountCV    = true;
                Settings.Default.ShowHFRGraph       = true;
                Settings.Default.ShowPerTargetIQ       = true;
                Settings.Default.ShowNextNightPreview  = true;
                Settings.Default.Save();
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ShowSkyThumbnails));
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
            get => Settings.Default.ShowSkyThumbnails;
            set {
                Settings.Default.ShowSkyThumbnails = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public bool ShowMoonCurve {
            get => Settings.Default.ShowMoonCurve;
            set {
                Settings.Default.ShowMoonCurve = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public bool ShowMinAltitude {
            get => Settings.Default.ShowMinAltitude;
            set {
                Settings.Default.ShowMinAltitude = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public bool ShowSessionHistory {
            get => Settings.Default.ShowSessionHistory;
            set {
                Settings.Default.ShowSessionHistory = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public bool ShowAltitudeChart {
            get => Settings.Default.ShowAltitudeChart;
            set {
                Settings.Default.ShowAltitudeChart = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public bool ShowTSProgressBars {
            get => Settings.Default.ShowTSProgressBars;
            set {
                Settings.Default.ShowTSProgressBars = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public bool ShowStarCountCV {
            get => Settings.Default.ShowStarCountCV;
            set {
                Settings.Default.ShowStarCountCV = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public bool ShowHFRGraph {
            get => Settings.Default.ShowHFRGraph;
            set {
                Settings.Default.ShowHFRGraph = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public bool ShowPerTargetIQ {
            get => Settings.Default.ShowPerTargetIQ;
            set {
                Settings.Default.ShowPerTargetIQ = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public bool ReportLightMode {
            get => Settings.Default.ReportLightMode;
            set {
                Settings.Default.ReportLightMode = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public bool ExpandSectionsDefault {
            get => Settings.Default.ExpandSectionsDefault;
            set {
                Settings.Default.ExpandSectionsDefault = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public int ChartPrimaryMetric {
            get => Settings.Default.ChartPrimaryMetric;
            set {
                Settings.Default.ChartPrimaryMetric = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public int ChartSecondaryMetric {
            get => Settings.Default.ChartSecondaryMetric;
            set {
                Settings.Default.ChartSecondaryMetric = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public const int MaxAdditionalCharts = 4;

        private static readonly List<string> _primaryMetricNames = new List<string> {
            "HFR", "FWHM", "Guiding RMS", "Focuser Temp (°C)", "Ambient Temp (°C)",
            "Eccentricity", "Altitude (°)", "Airmass", "Humidity (%)", "Focuser Position (steps)",
            "Sky Quality (mag/arcsec²)", "Cloud Cover (%)", "Camera Temp (°C)", "Dew Point (°C)",
            "Wind Speed (m/s)", "Pressure (hPa)", "Star Count", "Azimuth (°)"
        };

        private static readonly List<string> _secondaryMetricNames = new List<string> {
            "None", "HFR", "FWHM", "Guiding RMS", "Focuser Temp (°C)", "Ambient Temp (°C)",
            "Eccentricity", "Altitude (°)", "Airmass", "Humidity (%)", "Focuser Position (steps)",
            "Sky Quality (mag/arcsec²)", "Cloud Cover (%)", "Camera Temp (°C)", "Dew Point (°C)",
            "Wind Speed (m/s)", "Pressure (hPa)", "Star Count", "Azimuth (°)"
        };

        public IReadOnlyList<string> PrimaryMetricNames  => _primaryMetricNames;
        public IReadOnlyList<string> SecondaryMetricNames => _secondaryMetricNames;

        private ObservableCollection<ChartConfig> _additionalCharts;
        public ObservableCollection<ChartConfig> AdditionalCharts {
            get {
                if (_additionalCharts == null) {
                    _additionalCharts = DeserializeChartConfigs(Settings.Default.AdditionalChartConfigs);
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
            Settings.Default.AdditionalChartConfigs =
                string.Join("|", AdditionalCharts.Select(c => $"{c.Primary}:{c.Secondary}"));
            Settings.Default.Save();
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
            get => Settings.Default.ShowNextNightPreview;
            set {
                Settings.Default.ShowNextNightPreview = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public bool IsTsInstalled => new TargetSchedulerDatabase().IsAvailable;

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

                var saved = ParseFilterClassifications(Settings.Default.FilterClassifications);

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
            Settings.Default.FilterClassifications = string.Join(",", parts);
            Settings.Default.Save();
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
