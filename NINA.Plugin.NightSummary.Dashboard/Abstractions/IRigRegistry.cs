using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.NightSummary.Dashboard.Abstractions;

// One servable rig's complete backend: the data source + on-disk paths the
// dashboard reads, plus the optional regenerator and companion controller bound
// to it. In primary mode there is exactly one of these (the live NINA rig); in
// companion mode there is one per paired primary, each rooted at its own data
// dir under {root}/rigs/{id}/.
public sealed class RigBackend {
    public string Id      { get; }
    public string Name    { get; }
    public bool   Enabled { get; }
    public IDashboardDataSource Data    { get; }
    public IDashboardPaths      Paths   { get; }
    public IReportRegenerator?  Regen   { get; }   // null when regeneration disabled
    public ICompanionController? Companion { get; } // null in primary mode

    public RigBackend(string id, string name, bool enabled,
                      IDashboardDataSource data, IDashboardPaths paths,
                      IReportRegenerator? regen, ICompanionController? companion) {
        Id        = id;
        Name      = name;
        Enabled   = enabled;
        Data      = data  ?? throw new ArgumentNullException(nameof(data));
        Paths     = paths ?? throw new ArgumentNullException(nameof(paths));
        Regen     = regen;
        Companion = companion;
    }
}

// Resolves an incoming request's ?rig=<id> to the backend that serves it. The
// dashboard server holds one of these instead of a single data source/paths
// triple, so the same server process can switch which rig's data it reads on a
// per-request basis. Primary mode passes a SingleRigRegistry so the existing
// single-rig code path is untouched.
public interface IRigRegistry {
    // Companion-global root data dir — shared logs + dashboard cache live here,
    // NOT under any one rig. Equals the sole rig's data dir in single-rig mode.
    string RootDataDir { get; }

    // The rig served when no (or an unknown) ?rig= is supplied. Never null.
    RigBackend Default { get; }

    // All configured rigs, including disabled ones (the settings UI lists them).
    IReadOnlyList<RigBackend> All { get; }

    // Resolve a rig id to its backend. Null/empty/unknown id → Default (preserves
    // back-compat for bookmarks, the read-only mirror, and primary-mode JS that
    // never sends the param).
    RigBackend Resolve(string? rigId);

    // ── Multi-rig management (companion only) ─────────────────────────────────
    // False on the primary / single-rig / read-only registries; the dashboard
    // hides the add/remove UI and the management endpoints 400 when unsupported.
    bool SupportsManagement { get; }

    // Create a new (initially unpaired, enabled) rig and stand up its backend +
    // sync loops. Returns the generated rig id; the caller then pairs it via
    // /api/setup/claim?rig=<id>. Throws NotSupportedException when management is
    // unsupported.
    System.Threading.Tasks.Task<string> AddRigAsync(string name);

    // Tear down a rig: stop its loops, dispose its data source, drop it from the
    // config. When deleteData is true, also delete its on-disk data dir. Returns
    // false if the id is unknown. Refuses to remove the last remaining rig.
    bool RemoveRig(string rigId, bool deleteData);

    // Enable/disable a rig without removing it — starts/stops its sync loops and
    // persists the flag. Returns false if the id is unknown.
    bool SetRigEnabled(string rigId, bool enabled);
}

// Trivial single-entry registry. Used by primary mode, the read-only mirror, the
// dev harness, and tests — everywhere there is exactly one rig. Resolve ignores
// the id and always returns the one backend.
public sealed class SingleRigRegistry : IRigRegistry {
    private readonly RigBackend _only;
    public SingleRigRegistry(RigBackend only) { _only = only ?? throw new ArgumentNullException(nameof(only)); }

    public string RootDataDir => _only.Paths.DataDir;
    public RigBackend Default => _only;
    public IReadOnlyList<RigBackend> All => new[] { _only };
    public RigBackend Resolve(string? rigId) => _only;

    public bool SupportsManagement => false;
    public System.Threading.Tasks.Task<string> AddRigAsync(string name) =>
        throw new NotSupportedException("single-rig registry does not support adding rigs");
    public bool RemoveRig(string rigId, bool deleteData) =>
        throw new NotSupportedException("single-rig registry does not support removing rigs");
    public bool SetRigEnabled(string rigId, bool enabled) =>
        throw new NotSupportedException("single-rig registry does not support enabling/disabling rigs");
}
