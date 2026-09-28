using System;
using System.IO;
using System.Linq;
using System.Net.Http;
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
  Serve dashboard.html / .css / .js / report-icon.png from this directory
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
                "sync"  => await RunSyncAsync(configPath, ResolveArg(args, "--rig")),
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

    // One-shot CLI sync. With no --rig, syncs every enabled+complete rig
    // sequentially; --rig <id> targets a single rig. Exit 0 iff all attempted
    // syncs succeeded.
    private static async Task<int> RunSyncAsync(string configPath, string? rigFilter) {
        var (config, _, log) = Bootstrap(configPath);
        if (!config.IsComplete(out var reason)) {
            Console.Error.WriteLine($"Config incomplete: {reason}");
            Console.Error.WriteLine($"Run '{typeof(Program).Assembly.GetName().Name} serve' and finish setup in the dashboard, or edit {configPath} directly.");
            return 4;
        }
        var rigs = config.Rigs
            .Where(r => r.Enabled && r.IsComplete())
            .Where(r => string.IsNullOrEmpty(rigFilter) || string.Equals(r.Id, rigFilter, StringComparison.Ordinal))
            .ToList();
        if (rigs.Count == 0) {
            Console.Error.WriteLine(string.IsNullOrEmpty(rigFilter)
                ? "No enabled, fully-configured rig to sync."
                : $"No enabled, fully-configured rig with id '{rigFilter}'.");
            return 4;
        }
        bool allOk = true;
        foreach (var rig in rigs) {
            var paths = new CompanionPaths(config.RigDataDir(rig.Id));
            paths.EnsureExists();
            var engine = new SyncEngine(rig, config.Port, paths, log);
            log.Info($"CLI sync: rig '{rig.Name}' ({rig.Id})");
            var result = await engine.SyncAsync(CancellationToken.None);
            allOk &= result.Success;
        }
        return allOk ? 0 : 3;
    }

    private static async Task<int> RunServeAsync(string configPath, bool noSync, bool noBrowser, string? webDir, string? roPortOverride) {
        var (config, rootDataDir, log) = Bootstrap(configPath);
        // Don't Validate() here — serve must come up even when config is fresh
        // so the user can complete setup from the dashboard. Loops below skip
        // their work while !IsComplete and pick up automatically once saved.

        // "Show me the dashboard" launch. If a companion is already serving on
        // this port (the autostart agent is up and the user just double-clicked
        // the app, or launched a 2nd copy), don't stand up a second server —
        // open the running dashboard and exit cleanly. The HTTP probe makes this
        // snappy; without it we'd wait out the bind-retry before discovering the
        // conflict. A clean first launch sees connection-refused here (instant)
        // and proceeds normally. Skipped under --no-browser (autostart/service).
        if (!noBrowser && await AnotherInstanceServingAsync(config.Port)) {
            log.Info($"A companion is already serving on port {config.Port}; opening it instead of starting a second instance.");
            TryOpenBrowser($"http://localhost:{config.Port}/", log);
            return 0;
        }

        var complete = config.IsComplete(out var setupReason);
        if (!complete) {
            log.Warn($"Companion config incomplete ({setupReason}). Open the dashboard to finish setup.");
        }

        var settings = new CompanionPluginSettings();

        // One backend (data source + paths + regen + controller) per configured
        // rig, plus a scheduler + ping loop each. The dashboard resolves ?rig= to
        // a backend per request; add/remove/enable mutate this live.
        var registry = new CompanionRigRegistry(config, configPath, settings, log);

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
            rigs:        registry,
            settings:    settings,
            webAssets:   webAssets,
            externalLog: log);

        // Wrap StartAsync so a bind failure (port in use, permission denied) lands
        // in companion-YYYY-MM-DD.log instead of an unhandled throw → stderr →
        // exit 2 → watchdog dies on `exit $code`. Without this, a save-then-restart
        // that picks a busy port leaves the user with a dead companion and zero
        // log evidence of WHY. Bonus: tell them exactly how to recover.
        try {
            await server.StartAsync(config.Port);
            log.Info($"Dashboard serving on http://localhost:{config.Port} (companion mode)");
        } catch (System.Net.Sockets.SocketException ex) {
            // Backstop for the pre-probe above: if the port was free at probe
            // time but got claimed before our bind (race with the autostart
            // agent), treat AddressAlreadyInUse the same way — open the running
            // dashboard rather than dying silently.
            if (ex.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse) {
                log.Info($"Port {config.Port} already in use — a companion instance is already running.");
                if (!noBrowser) {
                    log.Info("Opening the running dashboard instead of starting a second instance.");
                    TryOpenBrowser($"http://localhost:{config.Port}/", log);
                }
                return 0;  // clean exit; the already-running instance keeps serving
            }
            log.Error($"Cannot bind dashboard on port {config.Port}: {ex.Message}");
            log.Error($"Another process is using port {config.Port}. To recover:");
            log.Error($"  1. Edit {configPath} and set \"port\" to a free value (default 8182)");
            log.Error($"  2. Or stop the process holding the port (try: lsof -iTCP:{config.Port} -nP)");
            log.Error("Companion is exiting cleanly so the watchdog stops; relaunch after fixing.");
            Console.Error.WriteLine($"error: cannot bind dashboard on port {config.Port}: {ex.Message}");
            Console.Error.WriteLine($"See {Path.Combine(rootDataDir, "logs", "companion-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log")} for recovery steps.");
            return 0;  // clean exit so the bash watchdog stops instead of looping/dying noisily
        }

        // Optional second instance with readOnly=true for safe public exposure.
        // --readonly-port CLI flag implies enable=true even when companion.json
        // says otherwise, so testers don't need to edit the file.
        DashboardServer? roServer = null;
        bool roEnabled = config.EnableReadOnlyMirror || !string.IsNullOrWhiteSpace(roPortOverride);
        int roPort     = ParseRoPort(roPortOverride) ?? config.ReadOnlyMirrorPort;
        if (roEnabled) {
            roServer = await StartReadOnlyMirrorAsync(roPort, config.Port, registry, settings, webAssets, log);
        }

        log.Info("Press Ctrl+C to stop.");

        // Launch convenience: pop the dashboard in the user's default browser so
        // double-clicking the app icon actually shows something (the companion is
        // a headless background agent — LSUIElement / no console window — so the
        // browser tab IS its UI). Incomplete config opens /setup (the wizard);
        // a complete config opens the dashboard itself. Gated only on:
        //   - --no-browser NOT passed (autostart/service entries set this flag so
        //     login/reboot stays silent — no surprise tab every boot)
        //
        // We deliberately don't check Console.IsOutputRedirected here — Finder
        // launches of a .app bundle redirect stdout to Console.app, which would
        // suppress the auto-open in exactly the case we want it to fire.
        if (!noBrowser) {
            var landing = config.IsComplete() ? "" : "setup";
            TryOpenBrowser($"http://localhost:{config.Port}/{landing}", log);
        }

        // Park forever — Ctrl+C kills the process; in service mode the host
        // signals SIGTERM and the runtime shuts everything down too.
        var stop = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.TrySetResult();

        // Per-rig scheduler + ping loops (back off when offline, coast on the
        // happy path) — one pair per enabled rig, owned by the registry.
        var schedulerCts = new CancellationTokenSource();
        registry.StartAll();

        // Boot sync, in the background now that the server is up and the tab is
        // open. The dashboard renders the last-synced data on disk immediately and
        // live-refreshes when this lands. Each enabled+complete rig with OnBoot
        // syncs; coalesces inside its controller, cancels cleanly on shutdown.
        if (!noSync) registry.KickBootSyncs(schedulerCts.Token);

        await stop.Task;
        log.Info("Stopping server…");
        schedulerCts.Cancel();
        await registry.DisposeAsync();
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
            CompanionRigRegistry registry,
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
            // Mirror reads the same rigs via the same registry — only difference is
            // the readOnly flag. tokenStore=null on purpose: the public surface
            // should never reach pairing-management endpoints, and the 403
            // chokepoint catches the rest. ?rig= still works through the registry.
            var mirror = new DashboardServer(
                rigs:        registry,
                settings:    settings,
                webAssets:   webAssets,
                externalLog: log,
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

    // ── Bootstrap helpers ────────────────────────────────────────────────

    // Loads config (v1→v2 shape migration happens inside Load), relocates a flat
    // v1 data dir into rigs/{id}/ once, and stands up a root-level logger. The
    // root data dir is shared (logs/, dashboard cache); each rig nests under
    // rigs/{id}/.
    private static (CompanionConfig config, string rootDataDir, CompanionLogger log) Bootstrap(string configPath) {
        var freshConfig = !File.Exists(configPath);
        var config = CompanionConfig.Load(configPath);
        if (freshConfig) {
            Console.WriteLine($"Wrote default config to: {Path.GetFullPath(configPath)}");
            Console.WriteLine("Open the dashboard once it starts and use the Settings tab to finish setup.");
        }
        var rootDataDir = config.ResolvedDataDir();
        var logsDir = Path.Combine(rootDataDir, "logs");
        Directory.CreateDirectory(logsDir);
        var log = new CompanionLogger(logsDir);

        // Persist a v1->v2 shape migration BEFORE touching data, so the rig id
        // minted during Load is stable across restarts (otherwise the next boot
        // would mint a new id and orphan the relocated rigs/<id>/ dir).
        if (config.JustMigratedV1) {
            log.Info("Companion: migrated config to v2 (rigs[]); saving so the rig id is stable.");
            try { CompanionConfig.Save(config, configPath); } catch (Exception ex) { log.Warn($"Companion: could not persist v2 config: {ex.Message}"); }
        }

        // One-time: move a legacy flat data dir under rigs/{defaultRigId}/. No-op
        // on fresh installs and on already-migrated trees (marker file).
        CompanionMigration.RelocateDataDirIfNeeded(rootDataDir, config.DefaultRig()?.Id, log);

        log.Info($"Companion config: {configPath}");
        log.Info($"Root data dir: {rootDataDir} ({config.Rigs.Count} rig(s))");
        log.Info(config.IsComplete()
            ? $"Default rig primary: {config.DefaultRig()?.ResolvedNinaUrl()}"
            : "Primary: <not configured> — finish setup in the dashboard.");
        return (config, rootDataDir, log);
    }

    // Quick check: is a companion already serving on this port? Hits the
    // companion status endpoint with a short timeout. true => another instance
    // is live (open it, don't start a second). false => nothing there / not a
    // companion / unreachable => safe to start our own server. Swallows every
    // failure (connection refused on a clean first launch is the common case).
    private static async Task<bool> AnotherInstanceServingAsync(int port) {
        try {
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1200) };
            using var resp = await http.GetAsync($"http://localhost:{port}/api/companion/status");
            return resp.IsSuccessStatusCode;
        } catch {
            return false;
        }
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
        // Canonical config home: the per-user app-data dir, alongside the synced
        // data. This is OUTSIDE the install artifact, so it survives every update
        // on all three platforms — macOS .app replace, Windows exe replace, Linux
        // .deb/AppImage/tarball replace. The user pairs once, not once per update.
        //   macOS   -> ~/Library/Application Support/NightSummaryCompanion/
        //   Windows -> %LOCALAPPDATA%\NightSummaryCompanion\
        //   Linux   -> ~/.local/share/NightSummaryCompanion/
        // (An explicit --config <path> overrides this entirely; handled upstream.)
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var canonical = Path.Combine(appData, "NightSummaryCompanion", "companion.json");

        // One-time migration. Older builds stored companion.json NEXT TO the
        // binary (inside the macOS .app bundle / beside the Windows exe / in the
        // Linux install dir) — exactly the spot an update wipes. If a legacy file
        // is still there and we haven't created the canonical one yet, copy the
        // host + pairing token across so the user doesn't have to re-pair. After
        // this the canonical copy is authoritative; the legacy one is ignored.
        // (On macOS the bundle is replaced wholesale on update, deleting the
        // legacy file before the new build ever runs, so only that one in-bundle
        // generation can't be auto-rescued — every later update is safe.)
        if (!File.Exists(canonical)) {
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrEmpty(exeDir)) {
                var legacy = Path.Combine(exeDir, "companion.json");
                if (File.Exists(legacy)) {
                    try {
                        Directory.CreateDirectory(Path.GetDirectoryName(canonical)!);
                        File.Copy(legacy, canonical, overwrite: false);
                    } catch { /* best-effort; a fresh canonical file is created on first save */ }
                }
            }
        }
        return canonical;
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
