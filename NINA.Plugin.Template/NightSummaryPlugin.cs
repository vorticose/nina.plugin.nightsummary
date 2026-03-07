using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Plugin.NightSummary.MyPluginProperties;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Session;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
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

        [ImportingConstructor]
        public NightSummaryPlugin(
            IProfileService profileService,
            IOptionsVM options,
            IImageSaveMediator imageSaveMediator,
            SessionService sessionService) {

            this.sessionService = sessionService;

            TestDiscordCommand = new RelayCommand(async () => {
                var url = Settings.Default.DiscordWebhookUrl;
                if (string.IsNullOrWhiteSpace(url)) {
                    Logger.Warning("NightSummary: Discord test skipped — webhook URL is empty");
                    return;
                }
                var sender = new DiscordSender(url);
                await sender.SendTestAsync();
            });

            TestPushoverCommand = new RelayCommand(async () => {
                var appToken = Settings.Default.PushoverAppToken;
                var userKey  = Settings.Default.PushoverUserKey;
                if (string.IsNullOrWhiteSpace(appToken) || string.IsNullOrWhiteSpace(userKey)) {
                    Logger.Warning("NightSummary: Pushover test skipped — app token or user key is empty");
                    return;
                }
                var sender = new PushoverSender(appToken, userKey);
                await sender.SendAsync("Night Summary", "Pushover is configured correctly!");
            });

            SendTestReportCommand = new RelayCommand(async () => {
                var testDbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NINA", "Plugins", CoreUtil.Version, "NightSummary", "test", "nightsummary.sqlite");

                if (!File.Exists(testDbPath)) {
                    Logger.Warning($"NightSummary: Test database not found at {testDbPath}");
                    return;
                }

                await this.sessionService.SendFromDatabaseAsync(testDbPath);
            });

            ResendLastSessionCommand = new RelayCommand(async () => {
                var liveDbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NINA", "Plugins", CoreUtil.Version, "NightSummary", "nightsummary.sqlite");

                if (!File.Exists(liveDbPath)) {
                    Logger.Warning($"NightSummary: Live database not found at {liveDbPath}");
                    return;
                }

                await this.sessionService.SendFromDatabaseAsync(liveDbPath);
            });

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

        public ICommand TestDiscordCommand { get; }
        public ICommand TestPushoverCommand { get; }
        public ICommand SendTestReportCommand { get; }
        public ICommand ResendLastSessionCommand { get; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
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
