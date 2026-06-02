using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;
using NINA.Plugin.NightSummary.Dashboard.Adapters;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;

namespace NINA.Plugin.NightSummary.Companion.Adapters;

// IReportRegenerator implementation for the companion app. Builds the report
// entirely from synced data — no primary contact needed. Replaces the older
// path where the companion proxied to the primary's /api/sessions/{id}/regenerate
// (CompanionController.RegenerateOnPrimaryAsync) which required the imaging
// rig to be online.
internal sealed class CompanionReportRegenerator : IReportRegenerator {

    private readonly string _connectionString;
    private readonly string _tsDbPath;
    private readonly IPluginSettings _settings;
    private readonly IDashboardLogger _log;
    private readonly IDashboardPaths _paths;
    private readonly bool _hasDb;

    public CompanionReportRegenerator(string dbPath, string tsDbPath, IPluginSettings settings, IDashboardLogger log, IDashboardPaths paths) {
        _hasDb            = File.Exists(dbPath);
        _connectionString = $"Data Source={dbPath};Mode=ReadOnly";
        _tsDbPath         = tsDbPath;
        _settings         = settings;
        _log              = log;
        _paths            = paths;
    }

    public bool IsAvailable => _hasDb;

    public async Task<string?> RegenerateAsync(string sessionId, CancellationToken ct = default) {
        try {
            ct.ThrowIfCancellationRequested();
            if (!_hasDb) return "companion: synced database not present";

            // CompanionTsReader points at the synced schedulerdb.sqlite. When
            // the file is absent (user doesn't run TS, or TS DB hasn't synced
            // yet) IsAvailable returns false and ReportGenerator skips all TS
            // sections — same fallback the primary uses when TS isn't installed.
            ITargetSchedulerDatabase tsDb = new CompanionTsReader(_tsDbPath, _log);

            var reader   = new SqliteSessionReader(_connectionString, _log);
            var builder  = new CompanionReportDataBuilder(reader, _settings, _log, _paths, tsDb);
            var data     = builder.Build(sessionId);
            if (data == null) return "session not found in companion sync";

            ct.ThrowIfCancellationRequested();
            var generator = new ReportGenerator(_settings, _log, tsDb, _paths);
            var html      = await generator.GenerateHtmlReport(data);

            var reportPath = _paths.ReportHtmlPath(sessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            await File.WriteAllTextAsync(reportPath, html, ct);
            _log.Info($"Companion: regenerated report → {reportPath}");
            return null;
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            _log.Error($"Companion: regenerate failed for {sessionId}", ex);
            return ex.Message;
        }
    }
}
