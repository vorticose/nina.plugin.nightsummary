using System;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.DevHost;

/// <summary>
/// Stub <see cref="ICompanionController"/> for the dev harness. Returns plausible
/// static values so the dashboard renders its companion-mode UI (banner, sync
/// status, settings tab variants) without spinning up the actual companion
/// binary or pointing at a real primary. Used by --companion-mode to iterate
/// on mobile UI bugs with hot-reload of JS/CSS.
///
/// Side effects are limited to a fake "isSyncing flicker" on TriggerSyncAsync
/// so the spinner gating can be exercised. Save/Test/Pair/Regen calls return
/// success and log the payload — no network, no disk writes.
/// </summary>
internal sealed class DevStubCompanionController : ICompanionController {

    private readonly DevDashboardLogger _log;
    private readonly object _gate = new();
    private bool _syncing;
    private CompanionSyncProgress? _progress;
    private DateTime _lastAttemptUtc = DateTime.UtcNow.AddMinutes(-15);
    private DateTime _lastSuccessUtc = DateTime.UtcNow.AddMinutes(-15);
    private DateTime _lastPingUtc    = DateTime.UtcNow;
    private bool _primaryReachable   = true;

    public DevStubCompanionController(DevDashboardLogger log) {
        _log = log;
    }

    public bool IsSyncing {
        get { lock (_gate) return _syncing; }
    }

    public CompanionSyncStatus GetStatus() {
        lock (_gate) {
            return new CompanionSyncStatus(
                LastAttemptUtc:        _lastAttemptUtc,
                LastSuccessUtc:        _lastSuccessUtc,
                LastError:             null,
                PrimaryVersion:        "3.1.0",
                PrimarySchema:         12,
                DbBytes:               2_572_288,
                TsDbBytes:             11_653_120,
                FilesAdded:            0,
                FilesUpdated:          0,
                FilesDeleted:          0,
                ThumbsAdded:           0,
                ThumbsUpdated:         0,
                ThumbsDeleted:         0,
                IsRunning:             _syncing,
                PrimaryReachable:      _primaryReachable,
                PrimaryLastCheckedUtc: _lastPingUtc,
                Progress:              _syncing ? _progress : null);
        }
    }

    public async Task<CompanionSyncStatus> TriggerSyncAsync(CancellationToken ct = default) {
        lock (_gate) {
            if (_syncing) return GetStatus();
            _syncing = true;
            _lastAttemptUtc = DateTime.UtcNow;
        }
        _log.Info("[stub-companion] TriggerSyncAsync — simulating a phased sync");
        // Walk the same phases the real SyncEngine reports so the wizard's
        // progress UI can be exercised end-to-end in the dev harness.
        var phases = new (string phase, long bytes)[] {
            ("Connecting to your imaging rig", 0),
            ("Downloading reports",            3_100_000),
            ("Downloading database",           2_500_000),
            ("Downloading thumbnails",         12_400_000),
            ("Finishing up",                   0),
        };
        try {
            for (int i = 0; i < phases.Length; i++) {
                lock (_gate) _progress = new CompanionSyncProgress(phases[i].phase, i + 1, phases.Length, phases[i].bytes, null);
                await Task.Delay(800, ct);
            }
        } catch { }
        lock (_gate) {
            _syncing = false;
            _progress = null;
            _lastSuccessUtc = DateTime.UtcNow;
        }
        return GetStatus();
    }

    public Task PingPrimaryAsync(CancellationToken ct = default) {
        lock (_gate) {
            _lastPingUtc       = DateTime.UtcNow;
            _primaryReachable  = true;  // stub always "online" — flip via a future flag if we need offline UI testing
        }
        return Task.CompletedTask;
    }

    public CompanionConfigSnapshot GetConfig() {
        return new CompanionConfigSnapshot(
            Host:                            "100.86.208.29",
            Port:                            8181,
            DataDir:                         "(dev stub)",
            OnBoot:                          true,
            PollingIntervalHoursOnSuccess:   4,
            PollingIntervalMinutesOnFailure: 30,
            DashboardPort:                   8183,
            IsComplete:                      true,
            IncompleteReason:                null,
            PairingTokenSet:                 true,
            AcceptPush:                      true,
            EnableReadOnlyMirror:            false,
            ReadOnlyMirrorPort:              8282);
    }

    public Task<CompanionConfigSaveResult> SaveConfigAsync(CompanionConfigEdit edit, CancellationToken ct = default) {
        _log.Info($"[stub-companion] SaveConfigAsync (host={edit.Host}, port={edit.Port}, dashboardPort={edit.DashboardPort}, " +
                  $"enableReadOnlyMirror={edit.EnableReadOnlyMirror}, readOnlyMirrorPort={edit.ReadOnlyMirrorPort}) — pretending to persist");
        return Task.FromResult(new CompanionConfigSaveResult(true, null, GetConfig()));
    }

    public Task<CompanionConfigTestResult> TestConnectionAsync(string host, int port, string apiKey, CancellationToken ct = default) {
        _log.Info($"[stub-companion] TestConnectionAsync (host={host}, port={port}) — pretending OK");
        return Task.FromResult(new CompanionConfigTestResult(true, "3.1.0", 12, null));
    }

    public Task<CompanionProbeResult> ProbePrimaryAsync(string host, int port, CancellationToken ct = default) {
        _log.Info($"[stub-companion] ProbePrimaryAsync (host={host}, port={port}) — pretending OK");
        return Task.FromResult(new CompanionProbeResult(
            Ok: true, NsVersion: "3.1.0", NinaVersion: "3.2.0.9001",
            HasNs: true, PairedCount: 1, MinCompanionVersion: "3.0.0", Error: null));
    }

    public Task<CompanionClaimResult> ClaimPairingAsync(string host, int port, string token, string companionName, CancellationToken ct = default) {
        _log.Info($"[stub-companion] ClaimPairingAsync (host={host}, port={port}, name={companionName}) — pretending OK");
        return Task.FromResult(new CompanionClaimResult(
            Ok: true, CompanionId: "dev-stub", NinaVersion: "3.2.0.9001", NsVersion: "3.1.0",
            ErrorCode: null, Error: null, AlreadyPairedCompanionName: null));
    }

}
