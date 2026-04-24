namespace NINA.Plugin.NightSummary.Dashboard.Abstractions;

public interface IDashboardPaths {
    // Base directories
    string ReportsDir { get; }
    string LogsDir { get; }
    string HipsCacheDir { get; }

    // Per-session paths
    string ReportHtmlPath(string sessionId);
    string ReportSettingsPath(string sessionId);
    string LivestackDir(string sessionId);
    string LivestackManifestPath(string sessionId);
    string LivestackImagePath(string sessionId, string filename);
}
