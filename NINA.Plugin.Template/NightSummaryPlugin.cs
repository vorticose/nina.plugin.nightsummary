using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Plugin.NightSummary.MyPluginProperties;
using NINA.Plugin.NightSummary.Session;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary {

    [Export(typeof(IPluginManifest))]
    public class NightSummaryPlugin : PluginBase, INotifyPropertyChanged {

        [ImportingConstructor]
        public NightSummaryPlugin(
            IProfileService profileService,
            IOptionsVM options,
            IImageSaveMediator imageSaveMediator) {

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

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}