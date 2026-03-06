using NINA.Plugin.NightSummary.Data;
using System.Collections.Generic;

namespace NINA.Plugin.NightSummary.Reporting {
    /// <summary>
    /// All data needed to generate a Night Summary HTML report.
    /// Passed as a single parameter to ReportGenerator to avoid growing the method signature
    /// as more data sources are added in future versions.
    /// </summary>
    public class ReportData {
        public SessionRecord Session { get; init; }
        public List<ImageRecord> Images { get; init; }
        public List<SessionEvent> Events { get; init; }
    }
}
