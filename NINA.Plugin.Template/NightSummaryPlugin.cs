using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Session;
using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary {
    [Export(typeof(IPluginManifest))]
    public class NightSummaryPlugin : PluginBase, INotifyPropertyChanged {

        private readonly IProfileService profileService;
        private readonly IImageSaveMediator imageSaveMediator;
        private readonly SessionDatabase database;
        private readonly SessionCollector collector;

        [ImportingConstructor]
        public NightSummaryPlugin(
            IProfileService profileService,
            IOptionsVM options,
            IImageSaveMediator imageSaveMediator) {

            this.profileService = profileService;
            this.imageSaveMediator = imageSaveMediator;

            // Initialize the database - creates SQLite file if it doesn't exist
            this.database = new SessionDatabase();

            // Initialize the collector with the mediator and database
            this.collector = new SessionCollector(imageSaveMediator, database);

            Logger.Info("NightSummary: Plugin initialized successfully");
        }

        public override Task Teardown() {
            // Make sure we cleanly end any active session when NINA shuts down
            collector.EndSession();
            Logger.Info("NightSummary: Plugin torn down");
            return base.Teardown();
        }

        /// <summary>
        /// Starts a new imaging session. This will be called from the
        /// NightSummaryInstruction when added to a sequence.
        /// </summary>
        public void StartSession() {
            var profileName = profileService?.ActiveProfile?.Name ?? "Unknown";
            collector.StartSession(profileName);
        }

        /// <summary>
        /// Ends the current session. This will be called from the
        /// NightSummaryInstruction at sequence end.
        /// </summary>
        public void EndSession() {
            collector.EndSession();
        }

        /// <summary>
        /// Returns the current session ID for use by the report generator.
        /// </summary>
        public string GetCurrentSessionId() {
            return collector.GetCurrentSessionId();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}