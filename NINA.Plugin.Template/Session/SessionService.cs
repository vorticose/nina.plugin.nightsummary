using NINA.Plugin.NightSummary.Data;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using System.ComponentModel.Composition;

namespace NINA.Plugin.NightSummary.Session {

    /// <summary>
    /// Singleton service that manages the active imaging session.
    /// Exported via MEF so both the plugin and sequencer instructions
    /// can access the same instance.
    /// </summary>
    [Export(typeof(SessionService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class SessionService {

        private readonly SessionCollector collector;

        [ImportingConstructor]
        public SessionService(
            IImageSaveMediator imageSaveMediator,
            IProfileService profileService) {

            var database = new SessionDatabase();
            this.collector = new SessionCollector(imageSaveMediator, database);
        }

        public void StartSession(string profileName) {
            collector.StartSession(profileName);
        }

        public void EndSession() {
            collector.EndSession();
        }

        public string GetCurrentSessionId() {
            return collector.GetCurrentSessionId();
        }

        public SessionDatabase Database => collector.Database;
    }
}