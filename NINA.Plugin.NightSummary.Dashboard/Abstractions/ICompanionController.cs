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
}

// Editable surface of companion.json. ApiKey == null means "leave unchanged"
// so the dashboard can re-save other fields without round-tripping the secret.
public sealed record CompanionConfigEdit(
    string Host,
    int Port,
    string? ApiKey,
    bool OnBoot,
    int PollingIntervalHoursOnSuccess,
    int PollingIntervalMinutesOnFailure);

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
    string? IncompleteReason);

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
    bool IsRunning,
    // Cheap reachability ping result, refreshed on a faster cadence than full sync.
    // Lets the banner flip to "online" within ~minute of the primary coming back,
    // independent of whether a sync is currently due.
    bool? PrimaryReachable,
    DateTime? PrimaryLastCheckedUtc);
