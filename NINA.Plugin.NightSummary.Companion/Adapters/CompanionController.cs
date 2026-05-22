using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Companion.Sync;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Companion.Adapters;

// Bridges the dashboard's /api/companion/* endpoints to the SyncEngine. Holds
// a single coalescing Task so concurrent button-mashes share one sync run
// rather than spawning parallel pulls.
public sealed class CompanionController : ICompanionController {

    private readonly SyncEngine _engine;
    private readonly CompanionConfig _config;
    private readonly CompanionPaths _paths;
    private readonly string _configPath;
    private readonly IDashboardLogger _log;
    private readonly string _statePath;
    private readonly object _gate = new();
    private Task<SyncEngine.SyncResult>? _inFlight;

    // Reachability is kept in-memory (not persisted) — it's "right now" state.
    // volatile because the ping loop writes from a background task and the
    // request thread reads on every GetStatus().
    private volatile bool _hasReachability;
    private bool _primaryReachable;
    private DateTime _primaryLastCheckedUtc;

    public CompanionController(SyncEngine engine, CompanionConfig config, CompanionPaths paths, string configPath, IDashboardLogger log) {
        _engine     = engine;
        _config     = config;
        _paths      = paths;
        _configPath = configPath;
        _log        = log;
        _statePath  = Path.Combine(paths.DataDir, "last_synced.json");
    }

    public bool IsSyncing {
        get { lock (_gate) return _inFlight != null && !_inFlight.IsCompleted; }
    }

    public async Task PingPrimaryAsync(CancellationToken ct = default) {
        // Cap the probe at 5 s — the shared HttpClient has a 30 min timeout
        // for DB pulls, so without this an unreachable primary at boot blocks
        // the entire ping loop for half an hour.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
        bool reachable;
        try {
            (reachable, _, _) = await _engine.CheckHealthAsync(timeoutCts.Token);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            reachable = false;
        }
        SetReachability(reachable);
    }

    // Lets sync runs and other paths poke reachability without going through
    // the dedicated probe — a successful sync proves the primary is up, and
    // the dashboard banner shouldn't have to wait for the next ping cycle.
    public void SetReachability(bool reachable) {
        lock (_gate) {
            _primaryReachable      = reachable;
            _primaryLastCheckedUtc = DateTime.UtcNow;
            _hasReachability       = true;
        }
    }

    public CompanionSyncStatus GetStatus() {
        var s = SyncState.Load(_statePath);
        bool? reachable; DateTime? checkedAt;
        lock (_gate) {
            reachable = _hasReachability ? _primaryReachable : (bool?)null;
            checkedAt = _hasReachability ? _primaryLastCheckedUtc : (DateTime?)null;
        }
        return new CompanionSyncStatus(
            LastAttemptUtc:        s.LastAttemptUtc,
            LastSuccessUtc:        s.LastSuccessUtc,
            LastError:             s.LastError,
            PrimaryVersion:        s.PrimaryVersion,
            PrimarySchema:         s.PrimarySchema,
            DbBytes:               0,
            TsDbBytes:             0,
            FilesAdded:            0,
            FilesUpdated:          0,
            FilesDeleted:          0,
            ThumbsAdded:           0,
            ThumbsUpdated:         0,
            ThumbsDeleted:         0,
            IsRunning:             IsSyncing,
            PrimaryReachable:      reachable,
            PrimaryLastCheckedUtc: checkedAt);
    }

    public async Task<CompanionSyncStatus> TriggerSyncAsync(CancellationToken ct = default) {
        Task<SyncEngine.SyncResult> task;
        lock (_gate) {
            if (_inFlight != null && !_inFlight.IsCompleted) {
                task = _inFlight;
            } else {
                _inFlight = Task.Run(() => _engine.SyncAsync(ct));
                task = _inFlight;
            }
        }
        var result = await task;
        // The SyncEngine already pinged /api/health as step 1 — propagate the
        // outcome so the banner reflects "online" the moment a sync succeeds,
        // even if the dedicated ping loop hasn't fired since the primary came back.
        SetReachability(result.Reachable);
        // Status reflects what got persisted by SyncEngine; layer in this run's counts.
        var s = GetStatus();
        return s with {
            DbBytes        = result.DbBytes,
            TsDbBytes      = result.TsDbBytes,
            FilesAdded     = result.FilesAdded,
            FilesUpdated   = result.FilesUpdated,
            FilesDeleted   = result.FilesDeleted,
            ThumbsAdded    = result.ThumbsAdded,
            ThumbsUpdated  = result.ThumbsUpdated,
            ThumbsDeleted  = result.ThumbsDeleted,
        };
    }

    // ── Config surface ───────────────────────────────────────────────────

    public CompanionConfigSnapshot GetConfig() {
        lock (_gate) return BuildSnapshot();
    }

    private CompanionConfigSnapshot BuildSnapshot() {
        var complete = _config.IsComplete(out var reason);
        return new CompanionConfigSnapshot(
            Host:                            _config.Nina.Host ?? "",
            Port:                            _config.Nina.Port,
            ApiKeyMasked:                    _config.MaskedApiKey(),
            ApiKeySet:                       !string.IsNullOrEmpty(_config.Nina.ApiKey),
            DataDir:                         _config.ResolvedDataDir(),
            OnBoot:                          _config.Sync.OnBoot,
            PollingIntervalHoursOnSuccess:   _config.Sync.PollingIntervalHoursOnSuccess,
            PollingIntervalMinutesOnFailure: _config.Sync.PollingIntervalMinutesOnFailure,
            DashboardPort:                   _config.Port,
            IsComplete:                      complete,
            IncompleteReason:                reason,
            PairingTokenSet:                 !string.IsNullOrEmpty(_config.Nina.PairingToken));
    }

    public async Task<CompanionConfigSaveResult> SaveConfigAsync(CompanionConfigEdit edit, CancellationToken ct = default) {
        bool wasComplete;
        lock (_gate) {
            wasComplete = _config.IsComplete();
            // Validate before mutating so a bad edit doesn't half-update the file
            if (string.IsNullOrWhiteSpace(edit.Host))
                return Fail("host is required");
            if (edit.Port <= 0 || edit.Port > 65535)
                return Fail($"port {edit.Port} out of range");
            if (edit.PollingIntervalHoursOnSuccess < 1)
                return Fail("success interval must be at least 1 hour");
            if (edit.PollingIntervalMinutesOnFailure < 1)
                return Fail("failure interval must be at least 1 minute");
            // ApiKey == null means "leave unchanged". Disallow blank-replacements
            // because a working key going to empty bricks the sync.
            if (edit.ApiKey != null && string.IsNullOrWhiteSpace(edit.ApiKey))
                return Fail("apiKey cannot be empty (omit the field to keep the existing value)");

            _config.Nina.Host = edit.Host.Trim();
            _config.Nina.Port = edit.Port;
            if (edit.ApiKey != null) _config.Nina.ApiKey = edit.ApiKey.Trim();
            _config.Sync.OnBoot = edit.OnBoot;
            _config.Sync.PollingIntervalHoursOnSuccess   = edit.PollingIntervalHoursOnSuccess;
            _config.Sync.PollingIntervalMinutesOnFailure = edit.PollingIntervalMinutesOnFailure;

            try {
                CompanionConfig.Save(_config, _configPath);
            } catch (Exception ex) {
                _log.Error("Companion: failed to write companion.json", ex);
                return Fail($"could not write config: {ex.Message}");
            }
            _engine.Reconfigure();
            _log.Info($"Companion: config saved (host={_config.Nina.Host}, port={_config.Nina.Port}, " +
                      $"keyChanged={edit.ApiKey != null}, success={_config.Sync.PollingIntervalHoursOnSuccess}h, " +
                      $"failure={_config.Sync.PollingIntervalMinutesOnFailure}m)");
        }

        // If we just crossed the "incomplete → complete" line, kick a sync so
        // the dashboard immediately has data without waiting for the scheduler.
        // Probe reachability synchronously first so the banner reflects truth
        // before the sync UI shows.
        if (!wasComplete && _config.IsComplete()) {
            try { await PingPrimaryAsync(ct); } catch { }
            _ = Task.Run(async () => {
                try {
                    _log.Info("Companion: config newly complete — triggering initial sync.");
                    await TriggerSyncAsync(ct);
                } catch (Exception ex) {
                    _log.Warn($"Initial post-setup sync failed: {ex.Message}");
                }
            }, ct);
        } else {
            // Even on a re-save, the host/key may have changed; re-probe so the
            // banner doesn't lag the change.
            try { await PingPrimaryAsync(ct); } catch { }
        }

        return new CompanionConfigSaveResult(true, null, GetConfig());

        CompanionConfigSaveResult Fail(string msg) =>
            new(false, msg, BuildSnapshot());
    }

    public async Task<CompanionConfigTestResult> TestConnectionAsync(string host, int port, string apiKey, CancellationToken ct = default) {
        // Empty apiKey from the form means "use the saved one" — same convention
        // as SaveConfigAsync, so the user can test without re-typing the key.
        var effectiveKey = string.IsNullOrEmpty(apiKey) ? _config.Nina.ApiKey : apiKey;
        var r = await ConnectionTester.TestAsync(host, port, effectiveKey ?? "", ct);
        return new CompanionConfigTestResult(r.Ok, r.Version, r.Schema, r.Error);
    }

    // ── Setup wizard — primary-side calls (no token required yet) ────────

    public async Task<CompanionProbeResult> ProbePrimaryAsync(string host, int port, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(host))     return new CompanionProbeResult(false, null, null, false, 0, null, "host is empty");
        if (port <= 0 || port > 65535)            return new CompanionProbeResult(false, null, null, false, 0, null, $"port {port} out of range");

        using var http = new System.Net.Http.HttpClient { BaseAddress = new Uri($"http://{host}:{port}") };
        http.Timeout = TimeSpan.FromSeconds(5);
        try {
            using var resp = await http.GetAsync("/api/companion/info", ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) {
                // The server is reachable but doesn't expose pair endpoints —
                // probably an older Night Summary (pre-wizard release) or some
                // other server that happens to be on that host:port.
                return new CompanionProbeResult(false, null, null, false, 0, null,
                    "server responded but does not support pairing — upgrade Night Summary on the NINA machine");
            }
            if (!resp.IsSuccessStatusCode) {
                return new CompanionProbeResult(false, null, null, false, 0, null,
                    $"primary returned {(int)resp.StatusCode}");
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var r = doc.RootElement;
            return new CompanionProbeResult(
                Ok:                  true,
                NsVersion:           r.TryGetProperty("nsVersion",           out var v1) ? v1.GetString() : null,
                NinaVersion:         r.TryGetProperty("ninaVersion",         out var v2) ? v2.GetString() : null,
                HasNs:               r.TryGetProperty("hasNs",               out var v3) && v3.GetBoolean(),
                PairedCount:         r.TryGetProperty("pairedCount",         out var v4) && v4.ValueKind == System.Text.Json.JsonValueKind.Number ? v4.GetInt32() : 0,
                MinCompanionVersion: r.TryGetProperty("minCompanionVersion", out var v5) ? v5.GetString() : null,
                Error:               null);
        } catch (TaskCanceledException) {
            return new CompanionProbeResult(false, null, null, false, 0, null, "timed out (5s) — primary not reachable");
        } catch (Exception ex) {
            return new CompanionProbeResult(false, null, null, false, 0, null, ex.Message);
        }
    }

    public async Task<CompanionClaimResult> ClaimPairingAsync(string host, int port, string token, string companionName, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(host))          return new CompanionClaimResult(false, null, null, null, "bad_host",          "host is empty", null);
        if (port <= 0 || port > 65535)                 return new CompanionClaimResult(false, null, null, null, "bad_port",          $"port {port} out of range", null);
        if (string.IsNullOrWhiteSpace(token))         return new CompanionClaimResult(false, null, null, null, "missing_token",     "token is empty", null);
        if (string.IsNullOrWhiteSpace(companionName)) return new CompanionClaimResult(false, null, null, null, "missing_name",      "companion name is empty", null);

        using var http = new System.Net.Http.HttpClient { BaseAddress = new Uri($"http://{host}:{port}") };
        http.Timeout = TimeSpan.FromSeconds(10);
        var body = System.Text.Json.JsonSerializer.Serialize(new { token = token.Trim(), companionName = companionName.Trim() });
        var content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json");

        try {
            using var resp = await http.PostAsync("/api/companion/pair", content, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrEmpty(json) ? "{}" : json);
            var r = doc.RootElement;

            if (resp.IsSuccessStatusCode) {
                var companionId = r.TryGetProperty("companionId", out var ci) ? ci.GetString() : null;
                // Persist the token + host/port so subsequent /api/export/*
                // calls authenticate as the new pairing. The api key (if any)
                // is left untouched — the dual-auth shim accepts either.
                lock (_gate) {
                    _config.Nina.Host         = host.Trim();
                    _config.Nina.Port         = port;
                    _config.Nina.PairingToken = token.Trim();
                    try {
                        CompanionConfig.Save(_config, _configPath);
                    } catch (Exception ex) {
                        _log.Error("Companion: failed to write companion.json after pair", ex);
                        return new CompanionClaimResult(false, null, null, null, "save_failed",
                            $"paired with primary but could not write companion.json: {ex.Message}", null);
                    }
                    _engine.Reconfigure();
                }
                _log.Info($"Companion: paired with primary as '{companionName}' (companionId={companionId})");
                // Probe reachability immediately so the banner doesn't lag.
                try { await PingPrimaryAsync(ct); } catch { }
                return new CompanionClaimResult(
                    Ok:           true,
                    CompanionId:  companionId,
                    NinaVersion:  r.TryGetProperty("ninaVersion", out var nv) ? nv.GetString() : null,
                    NsVersion:    r.TryGetProperty("nsVersion",   out var sv) ? sv.GetString() : null,
                    ErrorCode:    null,
                    Error:        null,
                    AlreadyPairedCompanionName: null);
            }

            // Surface the primary's wire-format error code so the wizard can pick
            // the right message — "unknown_token" vs "revoked" vs "already_paired"
            // all need distinct user-facing copy.
            var errorCode = r.TryGetProperty("error", out var ec) ? ec.GetString() : null;
            var otherName = r.TryGetProperty("companionName", out var on) ? on.GetString() : null;
            return new CompanionClaimResult(false, null, null, null,
                errorCode, errorCode ?? $"primary returned {(int)resp.StatusCode}", otherName);
        } catch (TaskCanceledException) {
            return new CompanionClaimResult(false, null, null, null, "timeout", "timed out (10s) — primary not reachable", null);
        } catch (Exception ex) {
            return new CompanionClaimResult(false, null, null, null, "network_error", ex.Message, null);
        }
    }
}
