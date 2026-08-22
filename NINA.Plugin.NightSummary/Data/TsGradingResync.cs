using System;
using System.Collections.Generic;
using System.Linq;
using NINA.Core.Utility;

namespace NINA.Plugin.NightSummary.Data {

    /// <summary>
    /// Shared TS grading sync logic. Used both at session end (SessionService.FinalizeSession)
    /// and on-demand from the dashboard when a user opens a session that still has Pending
    /// frames — TS may have reached a verdict in the time since the session ended.
    /// </summary>
    public static class TsGradingResync {

        /// <summary>
        /// Queries the Target Scheduler database for grading results overlapping the session
        /// window and batch-updates the Images rows. Matches on filter name + timestamp within
        /// ±60s. Returns the number of image rows whose grading actually changed.
        /// Caller is responsible for catching exceptions — this method does not swallow them
        /// (SessionService wraps in try/catch for non-fatal session-end behavior; the dashboard
        /// endpoint surfaces failures as 500s).
        /// </summary>
        public static int Sync(SessionDatabase nsDb, TargetSchedulerDatabase tsDb,
                                string sessionId, DateTime sessionStart, DateTime sessionEnd,
                                List<ImageRecord> images) {
            if (nsDb == null || tsDb == null || string.IsNullOrEmpty(sessionId) || images == null)
                return 0;
            if (!tsDb.IsAvailable) return 0;

            var tsRows = tsDb.GetAcquiredImagesForDateRange(sessionStart, sessionEnd);
            if (tsRows.Count == 0) return 0;

            // Only include updates that actually change something — avoids burning a transaction
            // when nothing has moved since the last sync (common on repeat dashboard opens).
            var updates = new List<(int imageId, int gradingStatus, string rejectReason)>();
            foreach (var img in images) {
                var match = tsRows.FirstOrDefault(r =>
                    string.Equals(r.FilterName, img.Filter, StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs((r.AcquiredAt - img.Timestamp).TotalSeconds) <= 60);
                if (match == null) continue;
                if (match.GradingStatus == img.GradingStatus &&
                    string.Equals(match.RejectReason ?? "", img.RejectReason ?? "", StringComparison.Ordinal))
                    continue;
                updates.Add((img.Id, match.GradingStatus, match.RejectReason));
            }

            if (updates.Count == 0) return 0;
            nsDb.UpdateImageGradingFromTs(sessionId, updates);
            return updates.Count;
        }
    }
}
