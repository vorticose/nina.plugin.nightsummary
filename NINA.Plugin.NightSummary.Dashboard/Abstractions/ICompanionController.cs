using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Dashboard.Abstractions;

// Surface the companion app's sync engine through the dashboard so the UI can
// trigger syncs and read status without dropping to the CLI. Wired via an
// optional ctor arg on DashboardServer — null in primary mode, populated by
// the companion process. Kept tiny on purpose: only what the UI needs.
public interface ICompanionController {

    // Returns the latest snapshot of sync state (loaded from last_synced.json
    // on the companion). Cheap to call; safe to poll.
    CompanionSyncStatus GetStatus();

    // Triggers a one-shot sync. Implementations must coalesce concurrent calls
    // (in-flight sync + new request → reuse the running task) so a hammered
    // button doesn't spawn parallel SyncEngine runs.
    Task<CompanionSyncStatus> TriggerSyncAsync(CancellationToken ct = default);

    // True iff a sync is currently running (for UI spinner gating).
    bool IsSyncing { get; }

    // Cheap /api/health probe. Updates internal reachability state; returned
    // in subsequent GetStatus() calls. Safe to call on a fast cadence.
    Task PingPrimaryAsync(CancellationToken ct = default);

    // Snapshot of editable companion.json values for the Settings UI. The api
    // key is masked — the dashboard never sees the real secret.
    CompanionConfigSnapshot GetConfig();

    // Persist edits to companion.json and hot-reload the SyncEngine. Pass null
    // for ApiKey to keep the existing one unchanged. Returns the validation
    // outcome plus the post-save snapshot. If newly complete, the
    // implementation may kick off an initial sync in the background.
    Task<CompanionConfigSaveResult> SaveConfigAsync(CompanionConfigEdit edit, CancellationToken ct = default);

    // Probes an arbitrary host/port/apiKey triple against /api/health. Does
    // not mutate any state — purely informational for the Settings form.
    Task<CompanionConfigTestResult> TestConnectionAsync(string host, int port, string apiKey, CancellationToken ct = default);

    // Unauthenticated probe of the primary's /api/companion/info endpoint.
    // Used by the setup wizard's "Test Connection" step BEFORE any token is
    // available — distinguishes "wrong host" / "not NS" / "version mismatch"
    // so the wizard can show a specific message. Does not mutate state.
    Task<CompanionProbeResult> ProbePrimaryAsync(string host, int port, CancellationToken ct = default);

    // Forwards a pairing claim to the primary's /api/companion/pair endpoint.
    // On 200, persists the token + host/port to companion.json and reloads
    // the SyncEngine so the first sync uses the freshly issued bearer.
    Task<CompanionClaimResult> ClaimPairingAsync(string host, int port, string token, string companionName, CancellationToken ct = default);
}

// Result of an unauthenticated /api/companion/info probe. Ok=true means the
// server responded as a Night Summary primary; the wizard inspects the
// fields to decide whether pairing can proceed.
public sealed record CompanionProbeResult(
    bool Ok,
    string? NsVersion,
    string? NinaVersion,
    bool HasNs,
    int PairedCount,
    string? MinCompanionVersion,
    string? Error);

// Result of forwarding a pair request to the primary. Ok=true means the
// primary returned 200 and the companion has persisted the new token.
// ErrorCode mirrors the primary's wire codes ("unknown_token", "revoked",
// "already_paired") so the wizard can pick the right user-facing message.
public sealed record CompanionClaimResult(
    bool Ok,
    string? CompanionId,
    string? NinaVersion,
    string? NsVersion,
    string? ErrorCode,
    string? Error,
    string? AlreadyPairedCompanionName);

// Editable surface of companion.json. ApiKey == null means "leave unchanged"
// so the dashboard can re-save other fields without round-tripping the secret.
// DashboardPort == null means "leave unchanged" too (most config saves
// don't touch it); when set, takes effect on the next companion restart
// since the TCP listener is bound at startup.
public sealed record CompanionConfigEdit(
    string Host,
    int Port,
    string? ApiKey,
    bool OnBoot,
    int PollingIntervalHoursOnSuccess,
    int PollingIntervalMinutesOnFailure,
    int? DashboardPort = null);

public sealed record CompanionConfigSnapshot(
    string Host,
    int Port,
    string ApiKeyMasked,
    bool ApiKeySet,
    string DataDir,
    bool OnBoot,
    int PollingIntervalHoursOnSuccess,
    int PollingIntervalMinutesOnFailure,
    int DashboardPort,
    bool IsComplete,
    string? IncompleteReason,
    // True iff a per-companion pairing token is currently configured. The
    // wizard checks this to decide whether to show "you're paired" UX or the
    // full setup flow.
    bool PairingTokenSet = false);

public sealed record CompanionConfigSaveResult(
    bool Ok,
    string? Error,
    CompanionConfigSnapshot Snapshot);

public sealed record CompanionConfigTestResult(
    bool Ok,
    string? Version,
    int? Schema,
    string? Error);

// Snapshot of companion sync state. Times are UTC; UI converts to local.
public sealed record CompanionSyncStatus(
    DateTime? LastAttemptUtc,
    DateTime? LastSuccessUtc,
    string? LastError,
    string? PrimaryVersion,
    int? PrimarySchema,
    long DbBytes,
    long TsDbBytes,
    int FilesAdded,
    int FilesUpdated,
    int FilesDeleted,
    int ThumbsAdded,
    int ThumbsUpdated,
    int ThumbsDeleted,
    bool IsRunning,
    // Cheap reachability ping result, refreshed on a faster cadence than full sync.
    // Lets the banner flip to "online" within ~minute of the primary coming back,
    // independent of whether a sync is currently due.
    bool? PrimaryReachable,
    DateTime? PrimaryLastCheckedUtc);
