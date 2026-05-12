using System.IO;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.DevHost;

// Dev-side path provider. All roots are explicit so the harness never touches
// production data unless the operator points it there.
internal sealed class DevDashboardPaths : IDashboardPaths {
    public string DataDir      { get; }
    public string ReportsDir   { get; }
    public string LogsDir      { get; }
    public string HipsCacheDir { get; }
    public string DatabasePath { get; }
    public string ThumbsRoot   { get; }

    public DevDashboardPaths(string dataDir, string reportsDir, string databasePath, string thumbsRoot = null) {
        DataDir      = dataDir;
        ReportsDir   = reportsDir;
        LogsDir      = Path.Combine(dataDir, "logs");
        HipsCacheDir = Path.Combine(dataDir, "hips-cache");
        DatabasePath = databasePath;
        // Dev harness anchors thumbs under {dataDir}/thumbs unless --thumbs-root overrides.
        ThumbsRoot   = !string.IsNullOrEmpty(thumbsRoot) ? thumbsRoot : Path.Combine(dataDir, "thumbs");
    }

    public string ReportHtmlPath(string sessionId)        => Path.Combine(ReportsDir, $"{sessionId}.html");
    public string ReportSettingsPath(string sessionId)    => Path.Combine(ReportsDir, $"{sessionId}.settings.json");
    public string LivestackDir(string sessionId)          => Path.Combine(ReportsDir, sessionId, "livestack");
    public string LivestackManifestPath(string sessionId) => Path.Combine(LivestackDir(sessionId), "livestack.json");
    public string LivestackImagePath(string sessionId, string filename)
                                                          => Path.Combine(LivestackDir(sessionId), filename);
}
