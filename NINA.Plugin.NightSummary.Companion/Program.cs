using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Companion.Adapters;
using NINA.Plugin.NightSummary.Companion.Sync;
using NINA.Plugin.NightSummary.Dashboard.WebAssets;
using NINA.Plugin.NightSummary.Server;

namespace NINA.Plugin.NightSummary.Companion;

internal static class Program {

    private const string Usage =
@"NightSummaryCompanion — pull a synced copy of your Night Summary data and serve the dashboard.

Usage:
  NightSummaryCompanion sync     [--config <path>]              one-shot sync, then exit
  NightSummaryCompanion serve    [--config <path>] [--no-sync]  sync (unless --no-sync) then run dashboard server forever
  NightSummaryCompanion version
  NightSummaryCompanion help

Default config path:
  ./companion.json (next to the executable)

On first run a default companion.json is written and the program exits so you can fill it in.
";

    public static async Task<int> Main(string[] args) {
        if (args.Length == 0 || args[0] is "help" or "-h" or "--help") {
            Console.WriteLine(Usage);
            return 0;
        }
        if (args[0] is "version" or "-v" or "--version") {
            var ver = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "unknown";
            Console.WriteLine($"NightSummaryCompanion {ver}");
            return 0;
        }

        var configPath = ResolveArg(args, "--config") ?? DefaultConfigPath();
        var cmd = args[0];
        try {
            return cmd switch {
                "sync"  => await RunSyncAsync(configPath),
                "serve" => await RunServeAsync(configPath, noSync: HasFlag(args, "--no-sync")),
                _ => UnknownCommand(cmd),
            };
        } catch (Exception ex) {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static int UnknownCommand(string cmd) {
        Console.Error.WriteLine($"Unknown command: {cmd}");
        Console.Error.WriteLine(Usage);
        return 1;
    }

    // ── Commands ─────────────────────────────────────────────────────────

    private static async Task<int> RunSyncAsync(string configPath) {
        var (config, paths, log) = Bootstrap(configPath);
        config.Validate();
        var engine = new SyncEngine(config, paths, log);
        var result = await engine.SyncAsync(CancellationToken.None);
        return result.Success ? 0 : 3;
    }

    private static async Task<int> RunServeAsync(string configPath, bool noSync) {
        var (config, paths, log) = Bootstrap(configPath);
        config.Validate();

        if (!noSync && config.Sync.OnBoot) {
            log.Info("Boot sync starting…");
            var engine = new SyncEngine(config, paths, log);
            var result = await engine.SyncAsync(CancellationToken.None);
            if (!result.Success) log.Warn($"Boot sync did not complete cleanly: {result.Error}");
        }

        var settings = new CompanionPluginSettings();
        var server = new DashboardServer(
            data:        new CompanionDataSource(paths.DatabasePath, log),
            settings:    settings,
            webAssets:   new EmbeddedWebAssets(),
            externalLog: log,
            paths:       paths,
            regen:       null);

        await server.StartAsync(config.Port);
        log.Info($"Dashboard serving on http://localhost:{config.Port} (companion mode)");
        log.Info("Press Ctrl+C to stop.");

        // Park forever — Ctrl+C kills the process; in service mode the host
        // signals SIGTERM and the runtime shuts everything down too.
        var stop = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.TrySetResult();
        await stop.Task;

        log.Info("Stopping server…");
        await server.StopAsync();
        return 0;
    }

    // ── Bootstrap helpers ────────────────────────────────────────────────

    private static (CompanionConfig config, CompanionPaths paths, CompanionLogger log) Bootstrap(string configPath) {
        var freshConfig = !File.Exists(configPath);
        var config = CompanionConfig.Load(configPath);
        if (freshConfig) {
            Console.WriteLine($"Wrote default config to: {Path.GetFullPath(configPath)}");
            Console.WriteLine("Edit it to fill in nina.host, nina.port, and nina.apiKey, then re-run.");
            Environment.Exit(0);
        }
        var paths = new CompanionPaths(config.ResolvedDataDir());
        paths.EnsureExists();
        var log = new CompanionLogger(paths.LogsDir);
        log.Info($"Companion config: {configPath}");
        log.Info($"Data dir: {paths.DataDir}");
        log.Info($"Primary: {config.ResolvedNinaUrl()}");
        return (config, paths, log);
    }

    private static string DefaultConfigPath() {
        // AppContext.BaseDirectory is the right call under PublishSingleFile
        // (Assembly.Location returns empty there).
        return Path.Combine(AppContext.BaseDirectory, "companion.json");
    }

    private static string? ResolveArg(string[] args, string name) {
        for (int i = 0; i < args.Length - 1; i++) {
            if (args[i] == name) return args[i + 1];
        }
        return null;
    }

    private static bool HasFlag(string[] args, string name) {
        foreach (var a in args) if (a == name) return true;
        return false;
    }
}
