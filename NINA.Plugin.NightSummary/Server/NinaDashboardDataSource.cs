using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Server;

// Plugin-side data source. Reads via the classlib SqliteSessionReader so prod
// and dev share one source of truth for SELECT logic. TS reads still go through
// the plugin's TargetSchedulerDatabase since that lives outside the dashboard
// SQLite (separate file managed by the TS plugin).
internal sealed class NinaDashboardDataSource : IDashboardDataSource {
    private readonly string dbPath;
    private readonly string connectionString;

    public NinaDashboardDataSource(string dbPath) {
        this.dbPath           = dbPath;
        this.connectionString = $"Data Source={dbPath};Version=3;";
    }

    private SqliteSessionReader Reader() => new SqliteSessionReader(connectionString, new NinaDashboardLogger());
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

    public Task<TsImageAugment?> GetTsImageAugmentAsync(string targetName, string filterName, System.DateTime timestamp, int windowSeconds, double exposureDurationSeconds, CancellationToken ct = default) {
        if (!TargetSchedulerDatabase.IsPluginInstalled) return Task.FromResult<TsImageAugment?>(null);
        var tsDb = new TargetSchedulerDatabase();
        if (!tsDb.IsAvailable) return Task.FromResult<TsImageAugment?>(null);
        return Task.FromResult<TsImageAugment?>(tsDb.GetImageAugment(targetName, filterName, timestamp, windowSeconds, exposureDurationSeconds));
    }

    public Task<int> ResyncTsGradingAsync(string sessionId, CancellationToken ct = default) {
        if (!HasDb() || !TargetSchedulerDatabase.IsPluginInstalled || string.IsNullOrEmpty(sessionId))
            return Task.FromResult(0);
        var tsDb = new TargetSchedulerDatabase();
        if (!tsDb.IsAvailable) return Task.FromResult(0);

        var reader = Reader();
        var session = reader.GetSession(sessionId);
        if (session == null) return Task.FromResult(0);
        var images = reader.GetImagesForSession(sessionId).ToList();
        // Cheap pre-check — skip the TS query entirely when nothing is Pending.
        if (!images.Any(i => i.GradingStatus == 0)) return Task.FromResult(0);

        var nsDb = new SessionDatabase(dbPath);
        int changed = TsGradingResync.Sync(nsDb, tsDb, sessionId, session.SessionStart, session.SessionEnd, images);
        return Task.FromResult(changed);
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
