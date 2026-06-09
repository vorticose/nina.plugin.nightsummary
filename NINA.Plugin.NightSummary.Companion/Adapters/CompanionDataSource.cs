using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Companion.Adapters;

// IDashboardDataSource for companion mode. Wraps the same SqliteSessionReader
// the plugin uses, pointed at the synced nightsummary.sqlite. Target Scheduler
// reads come from the synced schedulerdb.sqlite via CompanionTsReader (the
// plugin's TargetSchedulerDatabase is net8.0-windows + NINA.Core, so the SQL
// is mirrored in CompanionTsReader). Disk-backed loaders return null so the
// dashboard server falls back to its existing reports/ filesystem reads.
internal sealed class CompanionDataSource : IDashboardDataSource {

    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly IDashboardLogger _log;
    private readonly CompanionTsReader _ts;

    public CompanionDataSource(string dbPath, string tsDbPath, IDashboardLogger log) {
        _dbPath = dbPath;
        _connectionString = $"Data Source={dbPath};Mode=ReadOnly";
        _log = log;
        _ts = new CompanionTsReader(tsDbPath, log);
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

    // TS reads served from the synced schedulerdb.sqlite. TS API settings
    // returned but the Host stays "localhost" — live TS API hits the NINA box
    // and is out of scope here (see noon-boundary cache work in COMPANION_PLAN).
    public Task<bool> IsTargetSchedulerAvailableAsync(CancellationToken ct = default)
        => Task.FromResult(_ts.IsAvailable);
    public Task<IReadOnlyList<TsProjectInfo>> GetTSProjectsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TsProjectInfo>>(_ts.GetAllProjects());
    public Task<TsApiSettings?> GetTSApiSettingsAsync(CancellationToken ct = default) {
        if (!_ts.IsAvailable) return Task.FromResult<TsApiSettings?>(null);
        var (enabled, port) = _ts.GetApiSettings();
        return Task.FromResult<TsApiSettings?>(new TsApiSettings(enabled, port));
    }

    public Task<TsImageAugment?> GetTsImageAugmentAsync(string targetName, string filterName, System.DateTime timestamp, int windowSeconds, double exposureDurationSeconds, CancellationToken ct = default) {
        if (!_ts.IsAvailable) return Task.FromResult<TsImageAugment?>(null);
        return Task.FromResult<TsImageAugment?>(_ts.GetImageAugment(targetName, filterName, timestamp, windowSeconds, exposureDurationSeconds));
    }

    // Companion DB is opened read-only (Mode=ReadOnly above) and writes belong to
    // the primary rig — the synced schedulerdb is a snapshot, not a live source.
    // Resync is a no-op here; primary-side resync runs when the user opens the
    // session on the NINA box and any updates propagate via the next companion sync.
    public Task<int> ResyncTsGradingAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(0);

    public Task<string?> LoadReportHtmlAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
    public Task<byte[]?> LoadLivestackImageAsync(string sessionId, string filename, CancellationToken ct = default)
        => Task.FromResult<byte[]?>(null);
    public Task<string?> LoadLivestackManifestAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
