using System;
using System.IO;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Server;

// Plugin-side path provider. All paths anchor under
// %LOCALAPPDATA%\NINA\NightSummary\, the version-independent data root.
internal sealed class NinaDashboardPaths : IDashboardPaths {
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NINA", "NightSummary");

    public string DataDir      => Root;
    public string ReportsDir   => Path.Combine(Root, "reports");
    public string LogsDir      => Path.Combine(Root, "logs");
    public string HipsCacheDir => Path.Combine(Root, "hips-cache");
    public string DatabasePath => Path.Combine(Root, "nightsummary.sqlite");

    public string ReportHtmlPath(string sessionId)        => Path.Combine(ReportsDir, $"{sessionId}.html");
    public string ReportSettingsPath(string sessionId)    => Path.Combine(ReportsDir, $"{sessionId}.settings.json");
    public string LivestackDir(string sessionId)          => Path.Combine(ReportsDir, sessionId, "livestack");
    public string LivestackManifestPath(string sessionId) => Path.Combine(LivestackDir(sessionId), "livestack.json");
    public string LivestackImagePath(string sessionId, string filename)
                                                          => Path.Combine(LivestackDir(sessionId), filename);
}
