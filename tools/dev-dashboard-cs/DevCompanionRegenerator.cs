using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;
using NINA.Plugin.NightSummary.Dashboard.Adapters;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;

namespace NINA.Plugin.NightSummary.DevHost;

// Mirror of CompanionReportRegenerator (which lives in the Companion exe and
// isn't reachable from the dev harness). Wires the same Dashboard building
// blocks — CompanionReportDataBuilder + ReportGenerator — against the dev
// snapshot DB so a regenerate-from-companion-UI request actually produces
// HTML in --companion-mode. Iterating on regen-emitted markup no longer
// requires building + scp'ing the real companion binary to a Mac.
internal sealed class DevCompanionRegenerator : IReportRegenerator {

    private readonly string _connectionString;
    private readonly IPluginSettings _settings;
    private readonly IDashboardLogger _log;
    private readonly IDashboardPaths _paths;
    private readonly bool _hasDb;

    public DevCompanionRegenerator(string dbPath, IPluginSettings settings, IDashboardLogger log, IDashboardPaths paths) {
        _hasDb            = File.Exists(dbPath);
        _connectionString = $"Data Source={dbPath};Mode=ReadOnly";
        _settings         = settings;
        _log              = log;
        _paths            = paths;
    }

    public bool IsAvailable => _hasDb;

    public async Task<string?> RegenerateAsync(string sessionId, CancellationToken ct = default) {
        try {
            ct.ThrowIfCancellationRequested();
            if (!_hasDb) return "dev-companion: snapshot database not present";

            var reader   = new SqliteSessionReader(_connectionString, _log);
            var builder  = new CompanionReportDataBuilder(reader, _settings, _log, _paths);
            var data     = builder.Build(sessionId);
            if (data == null) return "session not found in snapshot DB";

            ct.ThrowIfCancellationRequested();
            var generator = new ReportGenerator(_settings, _log, new NullTargetSchedulerDatabase());
            var html      = await generator.GenerateHtmlReport(data);

            var reportPath = _paths.ReportHtmlPath(sessionId);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            await File.WriteAllTextAsync(reportPath, html, ct);
            _log.Info($"DevCompanionRegenerator: wrote → {reportPath}");
            return null;
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            _log.Error($"DevCompanionRegenerator: regenerate failed for {sessionId}", ex);
            return ex.Message;
        }
    }
}
