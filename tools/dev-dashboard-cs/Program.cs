using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;
using NINA.Plugin.NightSummary.Dashboard.WebAssets;
using NINA.Plugin.NightSummary.Server;

namespace NINA.Plugin.NightSummary.DevHost;

internal static class Program {
    private const int    DefaultPort = 8182;
    private const string Usage =
        "Usage: nightsummary-dev-dashboard [--port N] [--host H] [--db PATH] [--ts-db PATH] [--ts-api-host H] [--no-ts] [--empty-projects] [--web PATH] [--data PATH] [--reports PATH]\n" +
        "  --port         Port to bind (default 8182)\n" +
        "  --host         Bind host: 'localhost' (default, loopback only), '+' / '*' (all interfaces, needs urlacl/admin),\n" +
        "                 or a specific IP (e.g. tailnet IP). Use '+' for LAN/tailnet access.\n" +
        "  --db           Path to nightsummary.sqlite (default %LOCALAPPDATA%/NINA/NightSummary/nightsummary.sqlite)\n" +
        "  --ts-db        Path to Target Scheduler schedulerdb.sqlite (default %LOCALAPPDATA%/NINA/SchedulerPlugin/schedulerdb.sqlite)\n" +
        "  --ts-api-host  Hostname/IP for TS API calls (default 'localhost'). Use rig's tailnet IP when TS runs on a remote box.\n" +
        "  --no-ts        Hide Target Scheduler from the dashboard (simulates a non-TS user). Overrides --ts-db / --ts-api-host.\n" +
        "  --empty-projects  TS available but 0 projects (simulates TS installed, never configured).\n" +
        "  --companion-mode  Wire a stub ICompanionController so the dashboard renders its companion-mode\n" +
        "                    UI (sync banner, pairing wizard, settings tab variants). Hot-reload of JS/CSS\n" +
        "                    still works via --web. Useful for iterating on mobile UI bugs without\n" +
        "                    rebuilding + redeploying the actual companion binary.\n" +
        "  --web          Source dir for HTML/CSS/JS (default <repo>/NINA.Plugin.NightSummary.Dashboard/Web)\n" +
        "  --data         Cache + logs root (default ./data under exe)\n" +
        "  --reports      Reports dir (default %LOCALAPPDATA%/NINA/NightSummary/reports)";

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
        var data     = new DevDashboardDataSource(opts.DbPath, log, opts.TsDbPath, opts.TsApiHost, opts.NoTs, opts.EmptyProjects);
        var settings = new DevPluginSettings();
        if (opts.CompanionMode) settings.Mode = "companion";
        var assets   = new DiskWebAssets(opts.WebDir, opts.AssetsDir);
        // In companion mode the regenerator wires the same building blocks the
        // real companion uses (CompanionReportDataBuilder + ReportGenerator)
        // against the snapshot DB so devs can exercise the regen path without
        // a real companion build. Primary mode has no SessionService here, so
        // regen reports "not available" and the UI hides the button.
        IReportRegenerator regen = opts.CompanionMode
            ? new DevCompanionRegenerator(opts.DbPath, settings, log, paths)
            : new DevReportRegenerator();

        // --companion-mode flips DashboardServer to its companion-mode wiring by
        // passing a non-null ICompanionController. Stub returns plausible static
        // values so the UI renders banners + sync status + pairing wizard pages
        // without a real primary or sync engine. Keeps the hot-reload --web
        // path intact for fast mobile UI iteration.
        var companion = opts.CompanionMode ? new DevStubCompanionController(log) : null;

        var server = new DashboardServer(
            data:        data,
            settings:    settings,
            webAssets:   assets,
            externalLog: log,
            paths:       paths,
            regen:       regen,
            companion:   companion);

        log.Info($"DB:      {opts.DbPath} (exists: {File.Exists(opts.DbPath)})");
        if (opts.NoTs) {
            log.Info("TS:      DISABLED via --no-ts (simulates non-TS user)");
        } else if (opts.EmptyProjects) {
            log.Info($"TS DB:   {opts.TsDbPath} (exists: {File.Exists(opts.TsDbPath)})");
            log.Info("TS:      AVAILABLE but 0 projects via --empty-projects");
        } else {
            log.Info($"TS DB:   {opts.TsDbPath} (exists: {File.Exists(opts.TsDbPath)})");
            log.Info($"TS API:  host={opts.TsApiHost}");
        }
        log.Info($"Web:     {opts.WebDir} (exists: {Directory.Exists(opts.WebDir)})");
        log.Info($"Assets:  {opts.AssetsDir} (exists: {Directory.Exists(opts.AssetsDir)})");
        log.Info($"Data:    {opts.DataDir}");
        log.Info($"Reports: {opts.ReportsDir}");

        var stop = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };

        try {
            await server.StartAsync(opts.Port, opts.Host);
            var displayHost = opts.Host is "+" or "*" ? "<all-interfaces>" : opts.Host;
            log.Info($"Listening on http://{displayHost}:{opts.Port}/  (Ctrl+C to stop)");
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
        public string Host       { get; set; } = "localhost";
        public string DbPath     { get; set; } = "";
        public string TsDbPath   { get; set; } = "";
        public string TsApiHost  { get; set; } = "localhost";
        public bool   NoTs           { get; set; } = false;
        public bool   EmptyProjects  { get; set; } = false;
        public bool   CompanionMode  { get; set; } = false;
        public string WebDir     { get; set; } = "";
        public string AssetsDir  { get; set; } = "";
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
                case "--host":    opts.Host       = next() ?? "localhost"; break;
                case "--db":      opts.DbPath     = next() ?? ""; break;
                case "--ts-db":   opts.TsDbPath   = next() ?? ""; break;
                case "--ts-api-host": opts.TsApiHost = next() ?? "localhost"; break;
                case "--no-ts":          opts.NoTs          = true; break;
                case "--empty-projects": opts.EmptyProjects  = true; break;
                case "--companion-mode": opts.CompanionMode  = true; break;
                case "--web":     opts.WebDir     = next() ?? ""; break;
                case "--assets":  opts.AssetsDir  = next() ?? ""; break;
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
        if (string.IsNullOrEmpty(opts.TsDbPath))   opts.TsDbPath   = Path.Combine(local, "NINA", "SchedulerPlugin", "schedulerdb.sqlite");
        if (string.IsNullOrEmpty(opts.ReportsDir)) opts.ReportsDir = Path.Combine(prodRoot, "reports");
        if (string.IsNullOrEmpty(opts.DataDir))    opts.DataDir    = Path.Combine(exeDir, "data");
        if (string.IsNullOrEmpty(opts.WebDir)) {
            opts.WebDir = repoRoot != null
                ? Path.Combine(repoRoot, "NINA.Plugin.NightSummary.Dashboard", "Web")
                : Path.Combine(exeDir, "Web");
        }
        if (string.IsNullOrEmpty(opts.AssetsDir)) {
            opts.AssetsDir = repoRoot != null
                ? Path.Combine(repoRoot, "assets")
                : Path.Combine(exeDir, "assets");
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
