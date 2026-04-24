using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.DevHost;

// Dev data source. Hits the same SqliteSessionReader the plugin uses so SELECT
// logic stays in lockstep. TS-related calls return empty/false because the dev
// harness does not have access to the TS plugin's separate SQLite file.
internal sealed class DevDashboardDataSource : IDashboardDataSource {
    private readonly string dbPath;
    private readonly string connectionString;
    private readonly IDashboardLogger log;

    public DevDashboardDataSource(string dbPath, IDashboardLogger log) {
        this.dbPath           = dbPath;
        this.connectionString = $"Data Source={dbPath};Version=3;";
        this.log              = log;
    }

    private SqliteSessionReader Reader() => new SqliteSessionReader(connectionString, log);
    private bool HasDb() => File.Exists(dbPath);

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
