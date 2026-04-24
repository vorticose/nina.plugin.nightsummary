using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Data;

namespace NINA.Plugin.NightSummary.Dashboard.Abstractions;

public interface IDashboardDataSource {
    // --- Sessions ---
    Task<IReadOnlyList<SessionRecord>> GetAllSessionsAsync(CancellationToken ct = default);
    Task<SessionRecord?> GetSessionAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<ImageRecord>> GetImagesAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<SessionEvent>> GetEventsAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<TimingEvent>> GetTimingEventsAsync(string sessionId, CancellationToken ct = default);

    // --- Targets ---
    Task<IReadOnlyList<TargetDetail>> GetTargetDetailsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TargetSessionHistory>> GetSessionsForTargetAsync(string targetName, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, double>> GetLatestPositionAnglesAsync(CancellationToken ct = default);

    // --- Target Scheduler ---
    Task<bool> IsTargetSchedulerAvailableAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TsProjectInfo>> GetTSProjectsAsync(CancellationToken ct = default);
    Task<TsApiSettings?> GetTSApiSettingsAsync(CancellationToken ct = default);

    // --- Report artifacts (disk-backed) ---
    Task<string?> LoadReportHtmlAsync(string sessionId, CancellationToken ct = default);
    Task<byte[]?> LoadLivestackImageAsync(string sessionId, string filename, CancellationToken ct = default);
    Task<string?> LoadLivestackManifestAsync(string sessionId, CancellationToken ct = default);

    // --- Report regeneration (optional capability) ---
    bool SupportsReportRegeneration { get; }
    Task<ReportRegenerationResult> RegenerateReportAsync(
        string sessionId,
        IReadOnlyDictionary<string, string?>? overrides,
        CancellationToken ct = default);
}

// Carries TS dashboard API server settings (port + enabled flag). Lives in classlib
// since the dashboard reads it but does not own the underlying TS DB connection.
public record TsApiSettings(bool Enabled, int Port);

public record ReportRegenerationResult(bool Succeeded, string? Message, string? ReportPath);
