using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.DevHost;

// Regen requires SessionService which only exists with a live NINA instance.
// Dev harness reports unavailable so the regenerate UI hides itself.
internal sealed class DevReportRegenerator : IReportRegenerator {
    public bool IsAvailable => false;
    public Task<string?> RegenerateAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<string?>("Regeneration not supported in dev harness");
}
