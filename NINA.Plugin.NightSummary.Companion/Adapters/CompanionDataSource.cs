using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Companion.Adapters;

// IDashboardDataSource for companion mode. Wraps the same SqliteSessionReader
// the plugin uses, pointed at the synced nightsummary.sqlite. Target Scheduler
// lookups intentionally return empty — the live TS DB lives on the NINA box;
// the companion will gain a synced TS read path in a later phase. Disk-backed
// loaders return null so the dashboard server falls back to its existing
// reports/ filesystem reads.
internal sealed class CompanionDataSource : IDashboardDataSource {

    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly IDashboardLogger _log;

    public CompanionDataSource(string dbPath, IDashboardLogger log) {
        _dbPath = dbPath;
        _connectionString = $"Data Source={dbPath};Version=3;Read Only=True;";
        _log = log;
    }

    private SqliteSessionReader Reader() => new(_connectionString, _log);
    private bool HasDb() => File.Exists(_dbPath);

    public Task<IReadOnlyList<SessionRecord>> GetAllSessionsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SessionRecord>>(HasDb() ? Reader().GetAllSessions() : new List<SessionRecord>());

    public Task<SessionRecord?> GetSessionAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(HasDb() ? Reader().GetSession(sessionId) : null);

    public Task<IReadOnlyList<ImageRecord>> GetImagesAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ImageRecord>>(HasDb() ? Reader().GetImagesForSession(sessionId) : new List<ImageRecord>());

    public Task<IReadOnlyList<SessionEvent>> GetEventsAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SessionEvent>>(HasDb() ? Reader().GetEventsForSession(sessionId) : new List<SessionEvent>());

    public Task<IReadOnlyList<TimingEvent>> GetTimingEventsAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TimingEvent>>(HasDb() ? Reader().GetTimingEventsForSession(sessionId) : new List<TimingEvent>());

    public Task<IReadOnlyList<TargetDetail>> GetTargetDetailsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TargetDetail>>(HasDb() ? Reader().GetTargetDetails() : new List<TargetDetail>());

    public Task<IReadOnlyList<TargetSessionDetail>> GetSessionsForTargetAsync(string targetName, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TargetSessionDetail>>(HasDb() ? Reader().GetSessionsForTarget(targetName) : new List<TargetSessionDetail>());

    public Task<IReadOnlyDictionary<string, double>> GetLatestPositionAnglesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, double>>(new Dictionary<string, double>());

    // TS not available in companion mode (yet). The dashboard already handles this
    // gracefully — Tonight's Preview hides, TS progress bars hide, etc.
    public Task<bool> IsTargetSchedulerAvailableAsync(CancellationToken ct = default)
        => Task.FromResult(false);
    public Task<IReadOnlyList<TsProjectInfo>> GetTSProjectsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TsProjectInfo>>(new List<TsProjectInfo>());
    public Task<TsApiSettings?> GetTSApiSettingsAsync(CancellationToken ct = default)
        => Task.FromResult<TsApiSettings?>(null);

    public Task<string?> LoadReportHtmlAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
    public Task<byte[]?> LoadLivestackImageAsync(string sessionId, string filename, CancellationToken ct = default)
        => Task.FromResult<byte[]?>(null);
    public Task<string?> LoadLivestackManifestAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
