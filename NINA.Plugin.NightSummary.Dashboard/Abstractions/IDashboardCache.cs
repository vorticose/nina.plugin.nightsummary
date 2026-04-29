using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Dashboard.Abstractions;

public interface IDashboardCache {
    Task<IReadOnlyDictionary<string, string>> LoadAllAltitudeChartsAsync(CancellationToken ct = default);
    Task<string?> GetAltitudeChartAsync(string sessionId, CancellationToken ct = default);
    Task SetAltitudeChartAsync(string sessionId, string chartJson, CancellationToken ct = default);
    Task InvalidateAltitudeChartAsync(string sessionId, CancellationToken ct = default);

    Task<string?> GetMetadataAsync(string key, CancellationToken ct = default);
    Task SetMetadataAsync(string key, string value, CancellationToken ct = default);
}
