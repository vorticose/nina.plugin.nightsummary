using System.IO;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Companion.Adapters;

// IDashboardPaths rooted at the companion's data dir (synced copy of the NINA
// machine's NightSummary tree). Mirrors NinaDashboardPaths layout exactly so
// the dashboard server reads the synced data the same way it would the live
// data — companion mode is just a different root.
public sealed class CompanionPaths : IDashboardPaths {
    private readonly string _root;
    public CompanionPaths(string root) { _root = root; }

    public string DataDir      => _root;
    public string ReportsDir   => Path.Combine(_root, "reports");
    public string LogsDir      => Path.Combine(_root, "logs");
    public string HipsCacheDir => Path.Combine(_root, "hips-cache");
    public string DatabasePath => Path.Combine(_root, "nightsummary.sqlite");
    // Companion mirrors the NINA-side layout: thumbnails live under {dataDir}/thumbs.
    // Sync engine writes synced JPEGs here; dashboard reads them via /api/frames/{id}/thumb.
    public string ThumbsRoot   => Path.Combine(_root, "thumbs");

    public string ReportHtmlPath(string sessionId)        => Path.Combine(ReportsDir, $"{sessionId}.html");
    public string ReportSettingsPath(string sessionId)    => Path.Combine(ReportsDir, $"{sessionId}.settings.json");
    // Matches the on-disk layout the export zip from the primary carries —
    // reports/livestack/{sessionId}/. Prior value was inverted and pointed at
    // an empty directory.
    public string LivestackDir(string sessionId)          => Path.Combine(ReportsDir, "livestack", sessionId);
    public string LivestackManifestPath(string sessionId) => Path.Combine(LivestackDir(sessionId), "livestack.json");
    public string LivestackImagePath(string sessionId, string filename)
                                                          => Path.Combine(LivestackDir(sessionId), filename);

    public string TsDatabasePath => Path.Combine(_root, "schedulerdb.sqlite");

    public void EnsureExists() {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(ReportsDir);
        Directory.CreateDirectory(LogsDir);
    }
}
