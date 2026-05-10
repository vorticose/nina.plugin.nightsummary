namespace NINA.Plugin.NightSummary.Dashboard.Abstractions;

public interface IDashboardPaths {
    // Base directories
    string DataDir { get; }
    string ReportsDir { get; }
    string LogsDir { get; }
    string HipsCacheDir { get; }
    string DatabasePath { get; }
    // Resolved thumbnails root — defaults to DataDir/thumbs but honors the
    // user's ThumbnailStorageDir override. Per-call resolution lets a settings
    // change take effect without restarting the server.
    string ThumbsRoot { get; }

    // Per-session paths
    string ReportHtmlPath(string sessionId);
    string ReportSettingsPath(string sessionId);
    string LivestackDir(string sessionId);
    string LivestackManifestPath(string sessionId);
    string LivestackImagePath(string sessionId, string filename);
}
