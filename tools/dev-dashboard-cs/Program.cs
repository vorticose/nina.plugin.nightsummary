using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Dashboard.WebAssets;
using NINA.Plugin.NightSummary.Server;

namespace NINA.Plugin.NightSummary.DevHost;

internal static class Program {
    private const int    DefaultPort = 8182;
    private const string Usage =
        "Usage: nightsummary-dev-dashboard [--port N] [--db PATH] [--web PATH] [--data PATH] [--reports PATH]\n" +
        "  --port    Port to bind (default 8182)\n" +
        "  --db      Path to nightsummary.sqlite (default %LOCALAPPDATA%/NINA/NightSummary/nightsummary.sqlite)\n" +
        "  --web     Source dir for HTML/CSS/JS (default <repo>/NINA.Plugin.NightSummary.Dashboard/Web)\n" +
        "  --data    Cache + logs root (default ./data under exe)\n" +
        "  --reports Reports dir (default %LOCALAPPDATA%/NINA/NightSummary/reports)";

    public static async Task<int> Main(string[] args) {
        var opts = ParseArgs(args);
        if (opts == null) {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        Directory.CreateDirectory(opts.DataDir);
        Directory.CreateDirectory(opts.ReportsDir);

        var log      = new DevDashboardLogger();
        var paths    = new DevDashboardPaths(opts.DataDir, opts.ReportsDir, opts.DbPath);
        var data     = new DevDashboardDataSource(opts.DbPath, log);
        var settings = new DevPluginSettings();
        var assets   = new DiskWebAssets(opts.WebDir);
        var regen    = new DevReportRegenerator();

        var server = new DashboardServer(data, settings, assets, log, paths, regen);

        log.Info($"DB:      {opts.DbPath} (exists: {File.Exists(opts.DbPath)})");
        log.Info($"Web:     {opts.WebDir} (exists: {Directory.Exists(opts.WebDir)})");
        log.Info($"Data:    {opts.DataDir}");
        log.Info($"Reports: {opts.ReportsDir}");

        var stop = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };

        try {
            await server.StartAsync(opts.Port, "localhost");
            log.Info($"Listening on http://localhost:{opts.Port}/  (Ctrl+C to stop)");
        } catch (Exception ex) {
            log.Error("Failed to start dev dashboard", ex);
            return 2;
        }

        stop.Wait();
        log.Info("Shutting down…");
        await server.StopAsync();
        return 0;
    }

    private sealed class Options {
        public int    Port       { get; set; } = DefaultPort;
        public string DbPath     { get; set; } = "";
        public string WebDir     { get; set; } = "";
        public string DataDir    { get; set; } = "";
        public string ReportsDir { get; set; } = "";
    }

    private static Options? ParseArgs(string[] args) {
        var opts = new Options();
        for (int i = 0; i < args.Length; i++) {
            string a = args[i];
            string? next() => i + 1 < args.Length ? args[++i] : null;
            switch (a) {
                case "-h":
                case "--help":
                    return null;
                case "-p":
                case "--port":
                    if (!int.TryParse(next(), out var p)) return null;
                    opts.Port = p;
                    break;
                case "--db":      opts.DbPath     = next() ?? ""; break;
                case "--web":     opts.WebDir     = next() ?? ""; break;
                case "--data":    opts.DataDir    = next() ?? ""; break;
                case "--reports": opts.ReportsDir = next() ?? ""; break;
                default:
                    Console.Error.WriteLine($"Unknown arg: {a}");
                    return null;
            }
        }

        var local      = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var prodRoot   = Path.Combine(local, "NINA", "NightSummary");
        var exeDir     = AppContext.BaseDirectory;
        var repoRoot   = FindRepoRoot(exeDir);

        if (string.IsNullOrEmpty(opts.DbPath))     opts.DbPath     = Path.Combine(prodRoot, "nightsummary.sqlite");
        if (string.IsNullOrEmpty(opts.ReportsDir)) opts.ReportsDir = Path.Combine(prodRoot, "reports");
        if (string.IsNullOrEmpty(opts.DataDir))    opts.DataDir    = Path.Combine(exeDir, "data");
        if (string.IsNullOrEmpty(opts.WebDir)) {
            opts.WebDir = repoRoot != null
                ? Path.Combine(repoRoot, "NINA.Plugin.NightSummary.Dashboard", "Web")
                : Path.Combine(exeDir, "Web");
        }
        return opts;
    }

    // Walks up from the exe to find the repo root (marker: NINA.Plugin.NightSummary.sln).
    // Lets --web default to live source for hot reload regardless of bin/Debug nesting.
    private static string? FindRepoRoot(string start) {
        var dir = new DirectoryInfo(start);
        while (dir != null) {
            if (File.Exists(Path.Combine(dir.FullName, "NINA.Plugin.NightSummary.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
