using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Session;

namespace NINA.Plugin.NightSummary.Server;

// Plugin-side ISessionMaintenance: resend goes through the live SessionService
// (same path as the WPF "Send Report" button), delete goes through the plugin's
// SessionDatabase write path plus on-disk artifact cleanup. Injected into
// DashboardServer for the /api/nightsummary/* (Touch 'N' Stars) endpoints;
// mirrors the NinaReportRegenerator seam.
internal sealed class NinaSessionMaintenance : ISessionMaintenance {
    private readonly SessionService sessionService;
    private readonly string dbPath;
    private readonly NinaDashboardPaths paths;

    public NinaSessionMaintenance(SessionService sessionService, string dbPath, NinaDashboardPaths paths) {
        this.sessionService = sessionService;
        this.dbPath         = dbPath;
        this.paths          = paths;
    }

    public async Task ResendAsync(string sessionId, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        // Rebuilds ReportData from persisted rows and fires every configured
        // sender (email / Discord / Pushover / dashboard) — identical to the
        // WPF Send Report command.
        await sessionService.SendFromDatabaseAsync(dbPath, sessionId);
    }

    public Task<bool> DeleteAsync(string sessionId, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        var removed = new SessionDatabase(dbPath).DeleteSession(sessionId);
        if (removed <= 0) return Task.FromResult(false);

        // The WPF delete path historically left these orphaned; clean them up
        // here so a TNS/dashboard delete removes the session completely.
        // Best-effort: a locked or missing file must not fail the delete —
        // the DB rows (the authoritative record) are already gone.
        TryDelete(() => File.Delete(paths.ReportHtmlPath(sessionId)),     "report html");
        TryDelete(() => File.Delete(paths.ReportSettingsPath(sessionId)), "report settings");
        TryDelete(() => Directory.Delete(paths.LivestackDir(sessionId), recursive: true), "livestack dir");
        TryDelete(() => {
            var thumbDir = Path.Combine(paths.ThumbsRoot, sessionId);
            if (Directory.Exists(thumbDir)) Directory.Delete(thumbDir, recursive: true);
        }, "thumbnail dir");
        return Task.FromResult(true);

        void TryDelete(Action action, string what) {
            try { action(); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (Exception ex) {
                Logger.Warning($"NightSummary: Session delete could not remove {what} for {sessionId}: {ex.Message}");
            }
        }
    }
}
