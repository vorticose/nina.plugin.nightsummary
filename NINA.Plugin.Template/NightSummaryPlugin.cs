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
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NINA.Plugin.NightSummary {

    [Export(typeof(IPluginManifest))]
    public class NightSummaryPlugin : PluginBase, INotifyPropertyChanged {

        [ImportingConstructor]
        public NightSummaryPlugin(
            IProfileService profileService,
            IOptionsVM options,
            IImageSaveMediator imageSaveMediator) {

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

        public bool SendReportOnSessionEnd {
            get => Settings.Default.SendReportOnSessionEnd;
            set {
                Settings.Default.SendReportOnSessionEnd = value;
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

        public ICommand TestPushoverCommand { get; }

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
