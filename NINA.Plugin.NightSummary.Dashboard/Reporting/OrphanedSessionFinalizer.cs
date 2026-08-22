using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.NightSummary.Reporting {

    /// <summary>
    /// Sessions where NINA closes or crashes before the End Session instruction runs are
    /// left with SessionEnd unset (DateTime.MinValue) by design — nothing else ever
    /// finalizes them afterward. That means every report build renders them as
    /// perpetually "in progress" with a duration measured against DateTime.Now (growing
    /// longer each time the report is regenerated), and the dashboard's session list
    /// (which only shows sessions where SessionEnd &gt; SessionStart) hides them forever,
    /// even once a report exists on disk for them.
    /// </summary>
    public static class OrphanedSessionFinalizer {

        /// <summary>
        /// Returns the end time an orphaned session should be finalized with, derived from
        /// its own last recorded activity — or null if the session doesn't need finalizing
        /// (it already has a real end time, or it's the session currently running).
        /// </summary>
        public static DateTime? ResolveEndTime(
            SessionRecord session,
            List<ImageRecord> images,
            List<SessionEvent> events,
            string currentLiveSessionId) {

            if (session.SessionEnd > session.SessionStart) return null;
            if (session.SessionId == currentLiveSessionId) return null;

            var candidates = new List<DateTime>();
            if (images.Count > 0) candidates.Add(images.Max(i => i.Timestamp));
            if (events.Count > 0) candidates.Add(events.Max(e => e.Timestamp));

            return candidates.Count > 0 ? candidates.Max() : session.SessionStart;
        }
    }
}
