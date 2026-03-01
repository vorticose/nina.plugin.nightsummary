using System;

namespace NINA.Plugin.NightSummary.Data {
    /// <summary>
    /// Represents a single imaging session (one night).
    /// Groups all ImageRecords taken during that session.
    /// </summary>
    public class SessionRecord {
        // Primary key for SQLite
        public int Id { get; set; }

        // Unique identifier shared with ImageRecord.SessionId
        // so we can query all images belonging to this session
        public string SessionId { get; set; }

        // When the sequence started and ended
        public DateTime SessionStart { get; set; }
        public DateTime SessionEnd { get; set; }

        // NINA profile active during this session
        public string ProfileName { get; set; }

        // Overall session notes - we can populate this
        // with a summary string once the session ends
        public string Notes { get; set; }

        // Whether the end of session report was successfully sent
        public bool ReportSent { get; set; }
    }
}