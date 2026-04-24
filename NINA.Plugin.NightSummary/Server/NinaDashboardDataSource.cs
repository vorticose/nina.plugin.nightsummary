using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Server;

// Plugin-side data source. Wraps SessionDatabase and TargetSchedulerDatabase.
// All methods Task-wrap their underlying sync calls -- the existing DB code
// is sync (System.Data.SQLite) and the dashboard handlers are low-throughput,
// so there's no benefit to making the underlying code async.
internal sealed class NinaDashboardDataSource : IDashboardDataSource {
    private readonly string dbPath;

    public NinaDashboardDataSource(string dbPath) {
        this.dbPath = dbPath;
    }

    private SessionDatabase Db() => new SessionDatabase(dbPath);
    private bool HasDb() => File.Exists(dbPath);

    public Task<IReadOnlyList<SessionRecord>> GetAllSessionsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SessionRecord>>(HasDb() ? Db().GetAllSessions() : new List<SessionRecord>());

    public Task<SessionRecord?> GetSessionAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(HasDb() ? Db().GetSession(sessionId) : null);

    public Task<IReadOnlyList<ImageRecord>> GetImagesAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ImageRecord>>(HasDb() ? Db().GetImagesForSession(sessionId) : new List<ImageRecord>());

    public Task<IReadOnlyList<SessionEvent>> GetEventsAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SessionEvent>>(HasDb() ? Db().GetEventsForSession(sessionId) : new List<SessionEvent>());

    public Task<IReadOnlyList<TimingEvent>> GetTimingEventsAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TimingEvent>>(HasDb() ? Db().GetTimingEventsForSession(sessionId) : new List<TimingEvent>());

    public Task<IReadOnlyList<TargetDetail>> GetTargetDetailsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TargetDetail>>(HasDb() ? Db().GetTargetDetails() : new List<TargetDetail>());

    public Task<IReadOnlyList<TargetSessionDetail>> GetSessionsForTargetAsync(string targetName, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TargetSessionDetail>>(HasDb() ? Db().GetSessionsForTarget(targetName) : new List<TargetSessionDetail>());

    public Task<IReadOnlyDictionary<string, double>> GetLatestPositionAnglesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, double>>(new Dictionary<string, double>());

    public Task<bool> IsTargetSchedulerAvailableAsync(CancellationToken ct = default) {
        if (!TargetSchedulerDatabase.IsPluginInstalled) return Task.FromResult(false);
        var tsDb = new TargetSchedulerDatabase();
        return Task.FromResult(tsDb.IsAvailable);
    }

    public Task<IReadOnlyList<TsProjectInfo>> GetTSProjectsAsync(CancellationToken ct = default) {
        if (!TargetSchedulerDatabase.IsPluginInstalled) return Task.FromResult<IReadOnlyList<TsProjectInfo>>(new List<TsProjectInfo>());
        var tsDb = new TargetSchedulerDatabase();
        if (!tsDb.IsAvailable) return Task.FromResult<IReadOnlyList<TsProjectInfo>>(new List<TsProjectInfo>());
        return Task.FromResult<IReadOnlyList<TsProjectInfo>>(tsDb.GetAllProjects());
    }

    public Task<TsApiSettings?> GetTSApiSettingsAsync(CancellationToken ct = default) {
        if (!TargetSchedulerDatabase.IsPluginInstalled) return Task.FromResult<TsApiSettings?>(null);
        var tsDb = new TargetSchedulerDatabase();
        if (!tsDb.IsAvailable) return Task.FromResult<TsApiSettings?>(null);
        var (enabled, port) = tsDb.GetApiSettings();
        return Task.FromResult<TsApiSettings?>(new TsApiSettings(enabled, port));
    }

    public Task<string?> LoadReportHtmlAsync(string sessionId, CancellationToken ct = default) {
        // The server reads disk-backed report HTML directly via reportsDir; this
        // method exists on the interface for future cloud backends. Plugin returns
        // null and the server's existing File.ReadAllText path handles loading.
        return Task.FromResult<string?>(null);
    }

    public Task<byte[]?> LoadLivestackImageAsync(string sessionId, string filename, CancellationToken ct = default)
        => Task.FromResult<byte[]?>(null);

    public Task<string?> LoadLivestackManifestAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
