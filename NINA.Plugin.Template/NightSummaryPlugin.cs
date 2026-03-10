using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Plugin.NightSummary.MyPluginProperties;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Session;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NINA.Plugin.NightSummary {

    [Export(typeof(IPluginManifest))]
    public class NightSummaryPlugin : PluginBase, INotifyPropertyChanged {

        private readonly SessionService sessionService;
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

            TestEmailCommand = new RelayCommand(async () => {
                EmailTestStatus.Text = "";
                var gmail    = Settings.Default.GmailAddress;
                var password = Settings.Default.GmailAppPassword;
                var recipient= Settings.Default.RecipientAddress;
                if (string.IsNullOrWhiteSpace(gmail) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(recipient)) {
                    EmailTestStatus.Text = "✗ Fill in all email fields first";
                    return;
                }
                var sender = new EmailSender(gmail, password, recipient);
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
                var sender = new PushoverSender(appToken, userKey);
                bool ok = await sender.SendAsync("Night Summary", "Pushover is configured correctly!");
                PushoverTestStatus.Text = ok ? "✓ Sent" : "✗ Failed — check NINA log";
            });

            SendTestReportCommand = new RelayCommand(async () => {
                TestReportStatus.Text = "";
                var testDbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NINA", "Plugins", CoreUtil.Version, "NightSummary", "test", "nightsummary.sqlite");

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
                "NINA", "Plugins", CoreUtil.Version, "NightSummary", "nightsummary.sqlite");

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

            LoadSessions();
            Logger.Info("NightSummary: Plugin initialized successfully");
        }

        public override Task Teardown() {
            Settings.Default.Save();
            Logger.Info("NightSummary: Plugin torn down");
            return base.Teardown();
        }

        // Settings properties bound to the Options UI
        public string GmailAddress {
            get => Settings.Default.GmailAddress;
            set {
                Settings.Default.GmailAddress = value;
                Settings.Default.Save();
                RaisePropertyChanged();
            }
        }

        public string GmailAppPassword {
            get => Settings.Default.GmailAppPassword;
            set {
                Settings.Default.GmailAppPassword = value;
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
}
