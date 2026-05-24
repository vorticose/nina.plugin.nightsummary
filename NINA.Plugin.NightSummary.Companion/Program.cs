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
  NightSummaryCompanion serve    [--config <path>] [--no-sync] [--no-browser] [--web <dir>]    sync (unless --no-sync) then run dashboard server forever
  NightSummaryCompanion version
  NightSummaryCompanion help

Default config path:
  ./companion.json (next to the executable)

On first run a default companion.json is written and the program exits so you can fill it in.

--no-browser
  Suppress the auto-opening of http://localhost:<port>/setup on first run.
  Default behavior opens the wizard in the user's default browser when the
  companion starts up and companion.json is not yet complete. Pass this flag
  when running under launchd / Task Scheduler / systemd so the service start
  doesn't pop a browser window on every reboot. install-service sets it
  automatically per platform.

--web <dir>
  Serve dashboard.html / .css / .js / plugin-icon.png from this directory
  instead of the embedded resources baked into the binary. Each request hits
  the disk fresh, so editing a CSS file + refreshing the browser is enough
  to iterate on UI without rebuilding. Intended for development; production
  installs should omit it. Falls back to embedded assets when omitted.

--readonly-port <int>
  Override companion.json's readOnlyMirrorPort for this invocation. When
  combined with enableReadOnlyMirror=true in the config (or just present at
  all — sets enable to true implicitly), the companion spins a second
  DashboardServer on this port with readOnly=true. Used for ad-hoc testing
  without editing the config.
";

    public static async Task<int> Main(string[] args) {
        if (args.Length > 0 && args[0] is "help" or "-h" or "--help") {
            Console.WriteLine(Usage);
            return 0;
        }
        if (args.Length > 0 && args[0] is "version" or "-v" or "--version") {
            var ver = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "unknown";
            Console.WriteLine($"NightSummaryCompanion {ver}");
            return 0;
        }

        var configPath = ResolveArg(args, "--config") ?? DefaultConfigPath();
        var webDir     = ResolveArg(args, "--web");
        var roPortOverride = ResolveArg(args, "--readonly-port");
        // No-arg invocation defaults to `serve` so double-clicking the .app
        // bundle from Finder (which passes zero args) does the obvious thing
        // instead of printing usage to a hidden stdout and exiting silently.
        // Explicit `help` still works for users at a terminal.
        var cmd = args.Length > 0 ? args[0] : "serve";
        try {
            return cmd switch {
                "sync"  => await RunSyncAsync(configPath),
                "serve" => await RunServeAsync(configPath,
                                               noSync:      HasFlag(args, "--no-sync"),
                                               noBrowser:   HasFlag(args, "--no-browser"),
                                               webDir:      webDir,
                                               roPortOverride: roPortOverride),
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

    private static async Task<int> RunServeAsync(string configPath, bool noSync, bool noBrowser, string? webDir, string? roPortOverride) {
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

        // Wrap StartAsync so a bind failure (port in use, permission denied) lands
        // in companion-YYYY-MM-DD.log instead of an unhandled throw → stderr →
        // exit 2 → watchdog dies on `exit $code`. Without this, a save-then-restart
        // that picks a busy port leaves the user with a dead companion and zero
        // log evidence of WHY. Bonus: tell them exactly how to recover.
        try {
            await server.StartAsync(config.Port);
            log.Info($"Dashboard serving on http://localhost:{config.Port} (companion mode)");
        } catch (System.Net.Sockets.SocketException ex) {
            log.Error($"Cannot bind dashboard on port {config.Port}: {ex.Message}");
            log.Error($"Another process is using port {config.Port}. To recover:");
            log.Error($"  1. Edit {configPath} and set \"port\" to a free value (default 8182)");
            log.Error($"  2. Or stop the process holding the port (try: lsof -iTCP:{config.Port} -nP)");
            log.Error("Companion is exiting cleanly so the watchdog stops; relaunch after fixing.");
            Console.Error.WriteLine($"error: cannot bind dashboard on port {config.Port}: {ex.Message}");
            Console.Error.WriteLine($"See {Path.Combine(paths.LogsDir, "companion-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log")} for recovery steps.");
            return 0;  // clean exit so the bash watchdog stops instead of looping/dying noisily
        }

        // Optional second instance with readOnly=true for safe public exposure.
        // --readonly-port CLI flag implies enable=true even when companion.json
        // says otherwise, so testers don't need to edit the file.
        DashboardServer? roServer = null;
        bool roEnabled = config.EnableReadOnlyMirror || !string.IsNullOrWhiteSpace(roPortOverride);
        int roPort     = ParseRoPort(roPortOverride) ?? config.ReadOnlyMirrorPort;
        if (roEnabled) {
            roServer = await StartReadOnlyMirrorAsync(roPort, config.Port, paths, settings, webAssets, log);
        }

        log.Info("Press Ctrl+C to stop.");

        // First-run convenience: pop the wizard in the user's default browser
        // when the companion is freshly installed. Gated on:
        //   - --no-browser NOT passed (service installs opt out via the flag;
        //     install-service will set this automatically per platform)
        //   - config is incomplete (returning user with a working install
        //     gets no surprise tab; complete config = silent boot)
        //
        // We deliberately don't check Console.IsOutputRedirected here — Finder
        // launches of a .app bundle redirect stdout to Console.app, which would
        // suppress the auto-open in exactly the case we want it to fire.
        if (!noBrowser && !config.IsComplete()) {
            TryOpenBrowser($"http://localhost:{config.Port}/setup", log);
        }

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
        if (roServer != null) {
            try { await roServer.StopAsync(); } catch (Exception ex) { log.Warn($"Read-only mirror stop: {ex.Message}"); }
        }
        await server.StopAsync();
        return 0;
    }

    // Same shape as the primary's StartReadOnlyMirrorAsync in NightSummaryPlugin.cs —
    // separate DashboardServer instance bound to its own port, reads the same data
    // dir, refuses every non-GET request with 403 via the readOnly ctor flag.
    // Validation lives here (not in the setter) so the user can change the port
    // and toggle in one Settings save without going through an invalid mid-state.
    // Failures log + return null; the main server keeps running.
    private static async Task<DashboardServer?> StartReadOnlyMirrorAsync(
            int port,
            int mainPort,
            CompanionPaths paths,
            CompanionPluginSettings settings,
            IWebAssets webAssets,
            CompanionLogger log) {
        if (port < 1024 || port > 65535) {
            log.Warn($"Read-only mirror port {port} out of range (1024-65535); mirror not started");
            return null;
        }
        if (port == mainPort) {
            log.Warn($"Read-only mirror port {port} matches main dashboard port; mirror not started");
            return null;
        }
        try {
            // Mirror reads the same DB + same web assets — only difference is the
            // readOnly flag. Pass companion=null/regen=null on purpose: the public
            // surface should never reach pairing-management or regenerate endpoints,
            // and the 403 chokepoint catches the rest.
            var mirror = new DashboardServer(
                data:        new CompanionDataSource(paths.DatabasePath, paths.TsDatabasePath, log),
                settings:    settings,
                webAssets:   webAssets,
                externalLog: log,
                paths:       paths,
                regen:       null,
                companion:   null,
                tokenStore:  null,
                readOnly:    true);
            await mirror.StartAsync(port);
            log.Info($"Read-only mirror serving on http://localhost:{port}");
            return mirror;
        } catch (Exception ex) {
            log.Warn($"Failed to start read-only mirror on port {port}: {ex.Message}");
            return null;
        }
    }

    private static int? ParseRoPort(string? s) {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return int.TryParse(s, out var n) ? n : null;
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

    // Cross-platform "open URL in default browser." UseShellExecute=true lets
    // .NET resolve the protocol handler. Best-effort: log + swallow on failure
    // (headless box, missing $BROWSER, sandboxed shell, etc.) — the URL is
    // also printed to the log above so the user can copy/paste as a fallback.
    private static void TryOpenBrowser(string url, IDashboardLogger log) {
        try {
            if (OperatingSystem.IsMacOS()) {
                System.Diagnostics.Process.Start("open", url);
            } else if (OperatingSystem.IsLinux()) {
                System.Diagnostics.Process.Start("xdg-open", url);
            } else if (OperatingSystem.IsWindows()) {
                // Windows ShellExecute via /c start handles default-browser
                // resolution correctly under all .NET single-file edge cases.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                    FileName        = "cmd",
                    Arguments       = $"/c start \"\" \"{url}\"",
                    CreateNoWindow  = true,
                    UseShellExecute = false,
                });
            } else {
                log.Info($"Auto-open browser: unsupported platform, please open {url} manually.");
                return;
            }
            log.Info($"Opened setup wizard in default browser: {url}");
        } catch (Exception ex) {
            log.Warn($"Could not auto-open browser ({ex.Message}). Open {url} manually.");
        }
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
