using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Dashboard.Abstractions;

// Owns the report-build pipeline. Plugin implementation wires SessionService.
// Dev harness returns IsAvailable=false (regenerate UI hides itself).
//
// The caller is responsible for snapshotting/applying/restoring plugin settings
// around the call -- this interface assumes _settings.Current already reflects
// the desired effective settings for the regen.
public interface IReportRegenerator {
    bool IsAvailable { get; }

    // Builds report data for the session, generates HTML, writes it to disk under
    // <reports>/<sessionId>.html, and persists any livestack masters surfaced by
    // the report data. Returns null on success or a failure message on error.
    Task<string?> RegenerateAsync(string sessionId, CancellationToken ct = default);
}
