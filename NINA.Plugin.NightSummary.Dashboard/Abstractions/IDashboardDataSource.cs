using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Dashboard.Dtos;

namespace NINA.Plugin.NightSummary.Dashboard.Abstractions;

public interface IDashboardDataSource {
    Task<IReadOnlyList<SessionDto>> GetAllSessionsAsync(CancellationToken ct = default);
    Task<SessionDto?> GetSessionAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<ImageDto>> GetImagesAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<EventDto>> GetEventsAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<TimingEventDto>> GetTimingEventsAsync(string sessionId, CancellationToken ct = default);

    Task<IReadOnlyList<TargetDetailDto>> GetTargetDetailsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SessionDto>> GetSessionsForTargetAsync(string targetName, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, double>> GetLatestPositionAnglesAsync(CancellationToken ct = default);

    Task<bool> IsTargetSchedulerAvailableAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TSProjectDto>> GetTSProjectsAsync(CancellationToken ct = default);
    Task<TSApiSettingsDto?> GetTSApiSettingsAsync(CancellationToken ct = default);

    Task<string?> LoadReportHtmlAsync(string sessionId, CancellationToken ct = default);
    Task<byte[]?> LoadLivestackImageAsync(string sessionId, string filename, CancellationToken ct = default);
    Task<string?> LoadLivestackManifestAsync(string sessionId, CancellationToken ct = default);

    bool SupportsReportRegeneration { get; }
    Task<ReportRegenerationResultDto> RegenerateReportAsync(
        string sessionId,
        SettingsOverridesDto? overrides,
        CancellationToken ct = default);
}
