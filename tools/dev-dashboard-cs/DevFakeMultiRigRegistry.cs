using System;
using System.Collections.Generic;
using System.Linq;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.DevHost;

// Wraps N RigBackends that all point at the SAME underlying data/paths — the
// dev-only way to exercise the real multi-rig switcher and an "all rigs"
// merged view against realistic session data without a second physical rig.
// --fake-rigs N duplicates the one configured snapshot N times under
// different display names. Never used by the plugin or the real companion.
internal sealed class DevFakeMultiRigRegistry : IRigRegistry {
    private readonly List<RigBackend> _rigs;

    public DevFakeMultiRigRegistry(List<RigBackend> rigs) {
        if (rigs == null || rigs.Count == 0) throw new ArgumentException("at least one rig required", nameof(rigs));
        _rigs = rigs;
    }

    public string RootDataDir => _rigs[0].Paths.DataDir;
    public RigBackend Default => _rigs[0];
    public IReadOnlyList<RigBackend> All => _rigs;
    public RigBackend Resolve(string? rigId) => _rigs.FirstOrDefault(r => r.Id == rigId) ?? Default;

    public bool SupportsManagement => false;
    public System.Threading.Tasks.Task<string> AddRigAsync(string name) =>
        throw new NotSupportedException("fake multi-rig registry does not support adding rigs");
    public bool RemoveRig(string rigId, bool deleteData) =>
        throw new NotSupportedException("fake multi-rig registry does not support removing rigs");
    public bool SetRigEnabled(string rigId, bool enabled) =>
        throw new NotSupportedException("fake multi-rig registry does not support enabling/disabling rigs");
    public bool SetRigName(string rigId, string name) =>
        throw new NotSupportedException("fake multi-rig registry does not support renaming rigs");
}
