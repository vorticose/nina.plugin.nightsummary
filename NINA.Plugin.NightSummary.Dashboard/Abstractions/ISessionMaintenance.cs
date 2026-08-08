using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Dashboard.Abstractions;

// Plugin-side session actions the dashboard server can invoke but not implement
// itself: re-sending a historical session's report through the configured
// delivery channels requires the live SessionService (senders + settings), and
// deleting a session must go through the plugin's SessionDatabase write path.
// Mirrors the IReportRegenerator seam: interface here (no NINA/WPF deps),
// implementation in the plugin project, injected at DashboardServer
// construction. Null in companion mode and on the read-only mirror.
public interface ISessionMaintenance {
    // Re-fires the configured delivery channels (email / Discord / Pushover /
    // dashboard) for the given session, rebuilding the report from the DB.
    // Throws on failure; the caller maps exceptions to an error response.
    Task ResendAsync(string sessionId, CancellationToken ct = default);

    // Deletes the session's DB rows and its on-disk artifacts (report HTML,
    // settings sidecar, livestack directory). Returns false when the session
    // id does not exist.
    Task<bool> DeleteAsync(string sessionId, CancellationToken ct = default);
}
