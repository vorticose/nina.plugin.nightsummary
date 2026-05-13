using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Companion.Adapters;
using NINA.Plugin.NightSummary.Companion.Sync;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;
using NINA.Plugin.NightSummary.Dashboard.WebAssets;
using NINA.Plugin.NightSummary.Server;

namespace NINA.Plugin.NightSummary.Companion;

internal static class Program {

    private const string Usage =
@"NightSummaryCompanion — pull a synced copy of your Night Summary data and serve the dashboard.

Usage:
  NightSummaryCompanion sync     [--config <path>]                              one-shot sync, then exit
  NightSummaryCompanion serve    [--config <path>] [--no-sync] [--web <dir>]    sync (unless --no-sync) then run dashboard server forever
  NightSummaryCompanion version
  NightSummaryCompanion help

Default config path:
  ./companion.json (next to the executable)

On first run a default companion.json is written and the program exits so you can fill it in.

--web <dir>
  Serve dashboard.html / .css / .js / plugin-icon.png from this directory
  instead of the embedded resources baked into the binary. Each request hits
  the disk fresh, so editing a CSS file + refreshing the browser is enough
  to iterate on UI without rebuilding. Intended for development; production
  installs should omit it. Falls back to embedded assets when omitted.
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
        var webDir     = ResolveArg(args, "--web");
        var cmd = args[0];
        try {
            return cmd switch {
                "sync"  => await RunSyncAsync(configPath),
                "serve" => await RunServeAsync(configPath, noSync: HasFlag(args, "--no-sync"), webDir: webDir),
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
        if (!config.IsComplete(out var reason)) {
            Console.Error.WriteLine($"Config incomplete: {reason}");
            Console.Error.WriteLine($"Run '{typeof(Program).Assembly.GetName().Name} serve' and finish setup in the dashboard, or edit {configPath} directly.");
            return 4;
        }
        var engine = new SyncEngine(config, paths, log);
        var result = await engine.SyncAsync(CancellationToken.None);
        return result.Success ? 0 : 3;
    }

    private static async Task<int> RunServeAsync(string configPath, bool noSync, string? webDir) {
        var (config, paths, log) = Bootstrap(configPath);
        // Don't Validate() here — serve must come up even when config is fresh
        // so the user can complete setup from the dashboard. Loops below skip
        // their work while !IsComplete and pick up automatically once saved.

        // Single SyncEngine + controller so the scheduler and the UI button
        // share coalescing — concurrent runs collapse to one in-flight sync.
        var engine     = new SyncEngine(config, paths, log);
        var controller = new CompanionController(engine, config, paths, configPath, log);

        if (!config.IsComplete(out var setupReason)) {
            log.Warn($"Companion config incomplete ({setupReason}). Open the dashboard to finish setup.");
        } else if (!noSync && config.Sync.OnBoot) {
            log.Info("Boot sync starting…");
            var result = await controller.TriggerSyncAsync(CancellationToken.None);
            if (!string.IsNullOrEmpty(result.LastError)) log.Warn($"Boot sync did not complete cleanly: {result.LastError}");
        }

        var settings = new CompanionPluginSettings();

        // --web <dir> hot-reloads HTML/CSS/JS straight from disk so UI iteration
        // doesn't need a rebuild + re-publish + scp cycle. Each request hits the
        // filesystem fresh; the server skips its assembled-HTML cache when the
        // asset source advertises HotReload=true. Otherwise fall back to the
        // embedded resources baked into the binary (the production path).
        IWebAssets webAssets;
        if (!string.IsNullOrWhiteSpace(webDir)) {
            var resolved = Path.GetFullPath(webDir);
            if (!Directory.Exists(resolved)) {
                Console.Error.WriteLine($"error: --web '{resolved}' is not a directory");
                return 5;
            }
            log.Info($"Web assets: disk (hot-reload) → {resolved}");
            webAssets = new DiskWebAssets(resolved);
        } else {
            webAssets = new EmbeddedWebAssets();
        }

        var server = new DashboardServer(
            data:        new CompanionDataSource(paths.DatabasePath, paths.TsDatabasePath, log),
            settings:    settings,
            webAssets:   webAssets,
            externalLog: log,
            paths:       paths,
            regen:       null,
            companion:   controller);

        await server.StartAsync(config.Port);
        log.Info($"Dashboard serving on http://localhost:{config.Port} (companion mode)");
        log.Info("Press Ctrl+C to stop.");

        // Park forever — Ctrl+C kills the process; in service mode the host
        // signals SIGTERM and the runtime shuts everything down too.
        var stop = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.TrySetResult();

        // Background scheduler. Sleep is success-vs-failure dependent so we
        // back off when offline and don't hammer the primary on the happy path.
        var schedulerCts = new CancellationTokenSource();
        var scheduler = Task.Run(() => RunSchedulerLoop(controller, config, log, schedulerCts.Token));
        var pinger    = Task.Run(() => RunPingLoop(controller, config, log, schedulerCts.Token));

        await stop.Task;
        log.Info("Stopping server…");
        schedulerCts.Cancel();
        try { await Task.WhenAll(scheduler, pinger); } catch (OperationCanceledException) { }
        await server.StopAsync();
        return 0;
    }

    // Cheap reachability poll. Hits /api/health on a fast cadence so the banner
    // flips to "online" within ~minute of the primary coming back, independent
    // of the slow sync schedule. No data transferred — pure status update.
    private const int PingIntervalSeconds = 10;
    private static async Task RunPingLoop(CompanionController controller, CompanionConfig config, CompanionLogger log, CancellationToken ct) {
        bool? lastReported = null;
        // Prime immediately so the dashboard has a value within seconds of boot.
        // Skip when config is incomplete — the http client has no usable host yet.
        if (config.IsComplete()) {
            try {
                await controller.PingPrimaryAsync(ct);
                var s0 = controller.GetStatus();
                log.Info($"Reachability initial probe → primary {(s0.PrimaryReachable == true ? "online" : "offline")}");
                lastReported = s0.PrimaryReachable;
            } catch { }
        }
        while (!ct.IsCancellationRequested) {
            try {
                await Task.Delay(TimeSpan.FromSeconds(PingIntervalSeconds), ct);
                if (!config.IsComplete()) { lastReported = null; continue; }
                await controller.PingPrimaryAsync(ct);
                // Only log on transitions so the file doesn't fill up with no-op pings.
                var now = controller.GetStatus().PrimaryReachable;
                if (now != lastReported) {
                    log.Info($"Reachability change → primary {(now == true ? "online" : "offline")}");
                    // Primary just came back — kick a sync so the dashboard clears its
                    // stale "Last sync failed" message and pulls anything new without
                    // waiting for the next scheduled cycle. Coalesces inside the
                    // controller, so a manual click in the same window is a no-op.
                    if (now == true && lastReported == false) {
                        log.Info("Primary recovered — auto-triggering sync.");
                        _ = Task.Run(async () => {
                            try { await controller.TriggerSyncAsync(ct); }
                            catch (Exception ex) { log.Warn($"Recovery sync failed: {ex.Message}"); }
                        }, ct);
                    }
                    lastReported = now;
                }
            } catch (OperationCanceledException) { return; }
              catch (Exception ex) { log.Debug($"Ping loop: {ex.Message}"); }
        }
    }

    // Periodic auto-sync loop. Picks the next interval based on whether the
    // last attempt succeeded — failure mode polls more aggressively (default
    // 30 min) so the dashboard recovers quickly when the primary comes back;
    // success mode coasts at hours so we don't beat up an idle rig.
    private static async Task RunSchedulerLoop(CompanionController controller, CompanionConfig config, CompanionLogger log, CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            var status = controller.GetStatus();
            var lastOk = status.LastError == null && status.LastSuccessUtc != null;
            var delay  = lastOk
                ? TimeSpan.FromHours(Math.Max(1, config.Sync.PollingIntervalHoursOnSuccess))
                : TimeSpan.FromMinutes(Math.Max(1, config.Sync.PollingIntervalMinutesOnFailure));

            try {
                await Task.Delay(delay, ct);
            } catch (OperationCanceledException) { return; }

            // Re-check after the delay — config may have been wiped via the
            // dashboard while we slept; don't try to sync without creds.
            if (!config.IsComplete()) continue;

            try {
                log.Info($"Scheduled sync starting (last={(lastOk ? "ok" : "failed")}, next interval was {delay.TotalMinutes:F0}m)…");
                var result = await controller.TriggerSyncAsync(ct);
                if (!string.IsNullOrEmpty(result.LastError)) log.Warn($"Scheduled sync error: {result.LastError}");
            } catch (OperationCanceledException) { return; }
              catch (Exception ex) { log.Error("Scheduled sync threw", ex); }
        }
    }

    // ── Bootstrap helpers ────────────────────────────────────────────────

    private static (CompanionConfig config, CompanionPaths paths, CompanionLogger log) Bootstrap(string configPath) {
        var freshConfig = !File.Exists(configPath);
        var config = CompanionConfig.Load(configPath);
        if (freshConfig) {
            Console.WriteLine($"Wrote default config to: {Path.GetFullPath(configPath)}");
            Console.WriteLine("Open the dashboard once it starts and use the Settings tab to finish setup.");
        }
        var paths = new CompanionPaths(config.ResolvedDataDir());
        paths.EnsureExists();
        var log = new CompanionLogger(paths.LogsDir);
        log.Info($"Companion config: {configPath}");
        log.Info($"Data dir: {paths.DataDir}");
        log.Info(config.IsComplete()
            ? $"Primary: {config.ResolvedNinaUrl()}"
            : "Primary: <not configured> — finish setup in the dashboard.");
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
