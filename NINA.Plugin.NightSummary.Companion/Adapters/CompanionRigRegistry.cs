using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Companion.Sync;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Companion.Adapters;

// Companion-mode IRigRegistry: one backend (data source + paths + regenerator +
// controller) per configured rig, each rooted at {root}/rigs/{id}/, plus a
// per-rig scheduler + reachability ping loop. The dashboard server resolves
// ?rig=<id> through here; add/remove/enable mutate the shared CompanionConfig
// and start/stop the matching loops live (no process restart).
internal sealed class CompanionRigRegistry : IRigRegistry, IAsyncDisposable, IDisposable {

    private readonly CompanionConfig _config;
    private readonly string _configPath;
    private readonly CompanionPluginSettings _settings;
    private readonly IDashboardLogger _log;

    private readonly object _gate = new();
    private readonly Dictionary<string, RigRunner> _runners = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();   // preserves config order for the switcher

    public CompanionRigRegistry(CompanionConfig config, string configPath, CompanionPluginSettings settings, IDashboardLogger log) {
        _config     = config;
        _configPath = configPath;
        _settings   = settings;
        _log        = log;
        foreach (var rig in _config.Rigs) AddRunnerLocked(rig);
    }

    // ── IRigRegistry ──────────────────────────────────────────────────────────

    public string RootDataDir => _config.ResolvedDataDir();

    public RigBackend Default {
        get {
            lock (_gate) {
                var d = _config.DefaultRig();
                if (d != null && _runners.TryGetValue(d.Id, out var r)) return r.Backend;
                // No usable rig yet (fresh install before first pair): synthesize a
                // backend over the first runner, or — if there are none at all —
                // make a placeholder rig so the dashboard/setup wizard has a root.
                if (_order.Count > 0) return _runners[_order[0]].Backend;
                var placeholder = _config.EnsureFirstRig();
                if (string.IsNullOrWhiteSpace(placeholder.Id)) placeholder.Id = CompanionConfig.NewRigId();
                AddRunnerLocked(placeholder);
                return _runners[placeholder.Id].Backend;
            }
        }
    }

    public IReadOnlyList<RigBackend> All {
        get { lock (_gate) return _order.Select(id => _runners[id].Backend).ToList(); }
    }

    public RigBackend Resolve(string? rigId) {
        lock (_gate) {
            if (!string.IsNullOrEmpty(rigId) && _runners.TryGetValue(rigId, out var r)) return r.Backend;
        }
        return Default;
    }

    public bool SupportsManagement => true;

    public Task<string> AddRigAsync(string name) {
        lock (_gate) {
            var rig = new RigConfig {
                Id      = CompanionConfig.NewRigId(),
                Name    = string.IsNullOrWhiteSpace(name) ? $"Rig {_config.Rigs.Count + 1}" : name.Trim(),
                Enabled = true,
            };
            _config.Rigs.Add(rig);
            CompanionConfig.Save(_config, _configPath);
            AddRunnerLocked(rig);
            _log.Info($"Companion: added rig '{rig.Name}' ({rig.Id}); pair it to start syncing.");
            return Task.FromResult(rig.Id);
        }
    }

    public bool RemoveRig(string rigId, bool deleteData) {
        RigRunner? runner;
        string? dataDir = null;
        lock (_gate) {
            if (!_runners.TryGetValue(rigId, out runner)) return false;
            if (_config.Rigs.Count <= 1) {
                _log.Warn($"Companion: refusing to remove the last rig ({rigId}).");
                return false;
            }
            _runners.Remove(rigId);
            _order.Remove(rigId);
            _config.Rigs.RemoveAll(r => r.Id == rigId);
            CompanionConfig.Save(_config, _configPath);
            dataDir = _config.RigDataDir(rigId);
        }
        // Stop loops outside the lock; dispose the data source so SQLite handles
        // are released before we try to delete the dir.
        runner.StopAsync().GetAwaiter().GetResult();
        if (deleteData && dataDir != null) {
            try {
                if (Directory.Exists(dataDir)) Directory.Delete(dataDir, recursive: true);
                _log.Info($"Companion: deleted synced data for rig {rigId} at {dataDir}");
            } catch (Exception ex) {
                _log.Warn($"Companion: could not delete rig {rigId} data dir: {ex.Message}");
            }
        }
        _log.Info($"Companion: removed rig {rigId} (deleteData={deleteData}).");
        return true;
    }

    public bool SetRigEnabled(string rigId, bool enabled) {
        RigRunner? runner;
        lock (_gate) {
            if (!_runners.TryGetValue(rigId, out runner)) return false;
            var rig = _config.Rigs.FirstOrDefault(r => r.Id == rigId);
            if (rig == null) return false;
            rig.Enabled = enabled;
            CompanionConfig.Save(_config, _configPath);
        }
        if (enabled) runner.Start();
        else         runner.StopAsync().GetAwaiter().GetResult();
        _log.Info($"Companion: rig {rigId} {(enabled ? "enabled" : "disabled")}.");
        return true;
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────

    // Start the scheduler + ping loops for every enabled rig. Called once after
    // the dashboard server is up.
    public void StartAll() {
        lock (_gate) {
            foreach (var id in _order) {
                var runner = _runners[id];
                if (runner.Rig.Enabled) runner.Start();
            }
        }
    }

    // Boot-sync each enabled, complete rig whose OnBoot is set. Fire-and-forget;
    // coalesces inside each controller. Mirrors the single-rig boot-sync that
    // used to live in Program.RunServeAsync.
    public void KickBootSyncs(CancellationToken ct) {
        List<RigRunner> snapshot;
        lock (_gate) snapshot = _order.Select(id => _runners[id]).ToList();
        foreach (var runner in snapshot) {
            var rig = runner.Rig;
            if (!rig.Enabled || !rig.Sync.OnBoot || !rig.IsComplete()) continue;
            _ = Task.Run(async () => {
                try {
                    _log.Info($"Boot sync starting for rig '{rig.Name}' (background)…");
                    var result = await runner.Controller.TriggerSyncAsync(ct);
                    if (!string.IsNullOrEmpty(result.LastError))
                        _log.Warn($"Boot sync for '{rig.Name}' did not complete cleanly: {result.LastError}");
                } catch (OperationCanceledException) {
                } catch (Exception ex) {
                    _log.Warn($"Boot sync for '{rig.Name}' failed: {ex.Message}");
                }
            }, ct);
        }
    }

    public async ValueTask DisposeAsync() {
        List<RigRunner> snapshot;
        lock (_gate) snapshot = _runners.Values.ToList();
        foreach (var r in snapshot) {
            try { await r.StopAsync(); } catch { /* best-effort */ }
        }
    }

    // Sync disposal for `using` call-sites (tests, simple teardown). Blocks on the
    // async path — loop cancellation is fast.
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    // ── Internal ────────────────────────────────────────────────────────────

    private void AddRunnerLocked(RigConfig rig) {
        if (string.IsNullOrWhiteSpace(rig.Id)) rig.Id = CompanionConfig.NewRigId();
        if (_runners.ContainsKey(rig.Id)) return;
        var paths = new CompanionPaths(_config.RigDataDir(rig.Id));
        paths.EnsureExists();
        var data   = new CompanionDataSource(paths.DatabasePath, paths.TsDatabasePath, _log);
        var regen  = new CompanionReportRegenerator(paths.DatabasePath, paths.TsDatabasePath, _settings, _log, paths);
        var engine = new SyncEngine(rig, _config.Port, paths, _log);
        var controller = new CompanionController(engine, _config, rig, paths, _configPath, _log);
        var runner  = new RigRunner(rig, controller, data, paths, regen, _log);
        _runners[rig.Id] = runner;
        _order.Add(rig.Id);
    }

    // Owns one rig's two background loops (scheduler + reachability ping) and the
    // CTS that stops them. Start()/StopAsync() are idempotent so enable/disable
    // toggling is safe.
    private sealed class RigRunner {
        public RigConfig Rig { get; }
        public CompanionController Controller { get; }
        private readonly IDashboardDataSource _data;
        private readonly IDashboardPaths _paths;
        private readonly IReportRegenerator _regen;
        private readonly IDashboardLogger _log;
        private CancellationTokenSource? _cts;
        private Task? _scheduler;
        private Task? _pinger;
        private readonly object _runGate = new();

        private const int PingIntervalSeconds = 10;

        // Built fresh each access so Name/Enabled track live edits (rename on
        // pairing, enable/disable toggle) without rebuilding the runner.
        public RigBackend Backend =>
            new RigBackend(Rig.Id, Rig.Name, Rig.Enabled, _data, _paths, _regen, Controller);

        public RigRunner(RigConfig rig, CompanionController controller,
                         IDashboardDataSource data, IDashboardPaths paths, IReportRegenerator regen,
                         IDashboardLogger log) {
            Rig = rig; Controller = controller;
            _data = data; _paths = paths; _regen = regen; _log = log;
        }

        public void Start() {
            lock (_runGate) {
                if (_cts != null) return;  // already running
                _cts = new CancellationTokenSource();
                var ct = _cts.Token;
                _scheduler = Task.Run(() => SchedulerLoop(ct));
                _pinger    = Task.Run(() => PingLoop(ct));
            }
        }

        public async Task StopAsync() {
            CancellationTokenSource? cts;
            Task? sched, ping;
            lock (_runGate) {
                cts = _cts; sched = _scheduler; ping = _pinger;
                _cts = null; _scheduler = null; _pinger = null;
            }
            if (cts == null) return;
            cts.Cancel();
            try { await Task.WhenAll(sched ?? Task.CompletedTask, ping ?? Task.CompletedTask); }
            catch (OperationCanceledException) { }
            cts.Dispose();
        }

        private async Task PingLoop(CancellationToken ct) {
            bool? lastReported = null;
            if (Rig.IsComplete()) {
                try {
                    await Controller.PingPrimaryAsync(ct);
                    lastReported = Controller.GetStatus().PrimaryReachable;
                } catch { }
            }
            while (!ct.IsCancellationRequested) {
                try {
                    await Task.Delay(TimeSpan.FromSeconds(PingIntervalSeconds), ct);
                    if (!Rig.IsComplete()) { lastReported = null; continue; }
                    await Controller.PingPrimaryAsync(ct);
                    var now = Controller.GetStatus().PrimaryReachable;
                    if (now != lastReported) {
                        if (now == true && lastReported == false) {
                            _log.Info($"Rig '{Rig.Name}': primary recovered — auto-triggering sync.");
                            _ = Task.Run(async () => {
                                try { await Controller.TriggerSyncAsync(ct); }
                                catch (Exception ex) { _log.Warn($"Rig '{Rig.Name}' recovery sync failed: {ex.Message}"); }
                            }, ct);
                        }
                        lastReported = now;
                    }
                } catch (OperationCanceledException) { return; }
                  catch (Exception ex) { _log.Debug($"Rig '{Rig.Name}' ping loop: {ex.Message}"); }
            }
        }

        private async Task SchedulerLoop(CancellationToken ct) {
            while (!ct.IsCancellationRequested) {
                var status = Controller.GetStatus();
                var lastOk = status.LastError == null && status.LastSuccessUtc != null;
                var delay  = lastOk
                    ? TimeSpan.FromHours(Math.Max(1, Rig.Sync.PollingIntervalHoursOnSuccess))
                    : TimeSpan.FromMinutes(Math.Max(1, Rig.Sync.PollingIntervalMinutesOnFailure));
                try { await Task.Delay(delay, ct); }
                catch (OperationCanceledException) { return; }
                if (!Rig.IsComplete()) continue;
                try {
                    _log.Info($"Rig '{Rig.Name}': scheduled sync (last={(lastOk ? "ok" : "failed")})…");
                    var result = await Controller.TriggerSyncAsync(ct);
                    if (!string.IsNullOrEmpty(result.LastError)) _log.Warn($"Rig '{Rig.Name}' scheduled sync error: {result.LastError}");
                } catch (OperationCanceledException) { return; }
                  catch (Exception ex) { _log.Error($"Rig '{Rig.Name}' scheduled sync threw", ex); }
            }
        }
    }
}
