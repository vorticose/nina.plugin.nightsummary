using NINA.Plugin.NightSummary.Data;
using System.Collections.Generic;

namespace NINA.Plugin.NightSummary.Tests.Replay {

    /// <summary>
    /// Contains the results of a replayed session for test assertions.
    /// Provides convenient access to the database contents after replay.
    /// </summary>
    internal class ReplayResult {
        public string SessionId { get; init; }
        public SessionDatabase Database { get; init; }

        public SessionRecord GetSession() => Database.GetSession(SessionId);
        public List<ImageRecord> GetImages() => Database.GetImagesForSession(SessionId);
        public List<SessionEvent> GetEvents() => Database.GetEventsForSession(SessionId);
        public Dictionary<string, double> GetCumulativeIntegration()
            => Database.GetCumulativeIntegrationByTarget(SessionId);
    }
}
