using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Companion;
using NINA.Plugin.NightSummary.Companion.Adapters;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests;

// Server-side rig routing: the companion registry resolves ?rig= to a backend,
// lists rigs for /api/mode + status/all, and hot-reloads on add/remove/enable.
public class CompanionRigRegistryTests : IDisposable {

    private sealed class NullLog : IDashboardLogger {
        public void Info(string m) { }
        public void Warn(string m) { }
        public void Error(string m, Exception? ex = null) { }
        public void Debug(string m) { }
    }

    private readonly string _root;
    private readonly string _configPath;

    public CompanionRigRegistryTests() {
        _root = Path.Combine(Path.GetTempPath(), $"ns-rig-reg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _configPath = Path.Combine(_root, "companion.json");
    }

    public void Dispose() {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private CompanionConfig TwoRigConfig() => new() {
        Port    = 8182,
        DataDir = _root,
        Rigs = {
            new RigConfig { Id = "alpha000", Name = "Alpha", Enabled = true,  Nina = { Host = "10.0.0.1", PairingToken = "t1" } },
            new RigConfig { Id = "beta0000", Name = "Beta",  Enabled = true,  Nina = { Host = "10.0.0.2", PairingToken = "t2" } },
        },
    };

    private CompanionRigRegistry NewRegistry(CompanionConfig cfg) {
        CompanionConfig.Save(cfg, _configPath);
        return new CompanionRigRegistry(cfg, _configPath, new CompanionPluginSettings(), new NullLog());
    }

    [Fact]
    public void Resolve_KnownId_ReturnsThatRig() {
        using var reg = NewRegistry(TwoRigConfig());
        Assert.Equal("beta0000", reg.Resolve("beta0000").Id);
        Assert.Equal("alpha000", reg.Resolve("alpha000").Id);
    }

    [Fact]
    public void Resolve_UnknownOrNull_ReturnsDefault() {
        using var reg = NewRegistry(TwoRigConfig());
        var def = reg.Default.Id;
        Assert.Equal(def, reg.Resolve("does-not-exist").Id);
        Assert.Equal(def, reg.Resolve(null).Id);
        Assert.Equal(def, reg.Resolve("").Id);
    }

    [Fact]
    public void All_ListsRigsInConfigOrder() {
        using var reg = NewRegistry(TwoRigConfig());
        Assert.Equal(new[] { "alpha000", "beta0000" }, reg.All.Select(r => r.Id).ToArray());
    }

    [Fact]
    public void Default_IsFirstEnabledComplete() {
        var cfg = TwoRigConfig();
        cfg.Rigs[0].Enabled = false;   // Alpha disabled → Beta is default
        using var reg = NewRegistry(cfg);
        Assert.Equal("beta0000", reg.Default.Id);
    }

    [Fact]
    public void RootDataDir_IsCompanionRoot() {
        using var reg = NewRegistry(TwoRigConfig());
        Assert.Equal(_root, reg.RootDataDir);
    }

    [Fact]
    public async Task AddRig_CreatesBackend_AndPersists() {
        using var reg = NewRegistry(TwoRigConfig());
        var id = await reg.AddRigAsync("Garage");

        Assert.NotNull(reg.Resolve(id));
        Assert.Equal(id, reg.Resolve(id).Id);
        Assert.Equal(3, reg.All.Count);
        // Persisted to disk.
        var reloaded = CompanionConfig.Load(_configPath);
        Assert.Contains(reloaded.Rigs, r => r.Id == id && r.Name == "Garage");
        // New rig dir exists.
        Assert.True(Directory.Exists(Path.Combine(_root, "rigs", id)));
    }

    [Fact]
    public void RemoveRig_DropsBackend_AndPersists() {
        using var reg = NewRegistry(TwoRigConfig());
        Assert.True(reg.RemoveRig("beta0000", deleteData: false));
        Assert.Single(reg.All);
        Assert.DoesNotContain(reg.All, r => r.Id == "beta0000");
        var reloaded = CompanionConfig.Load(_configPath);
        Assert.DoesNotContain(reloaded.Rigs, r => r.Id == "beta0000");
    }

    [Fact]
    public void RemoveRig_RefusesLastRig() {
        var cfg = TwoRigConfig();
        cfg.Rigs.RemoveAt(1);   // single rig
        using var reg = NewRegistry(cfg);
        Assert.False(reg.RemoveRig("alpha000", deleteData: false));
        Assert.Single(reg.All);
    }

    [Fact]
    public void RemoveRig_UnknownId_ReturnsFalse() {
        using var reg = NewRegistry(TwoRigConfig());
        Assert.False(reg.RemoveRig("nope", deleteData: false));
        Assert.Equal(2, reg.All.Count);
    }

    [Fact]
    public void SetRigEnabled_PersistsFlag() {
        using var reg = NewRegistry(TwoRigConfig());
        Assert.True(reg.SetRigEnabled("beta0000", false));
        var reloaded = CompanionConfig.Load(_configPath);
        Assert.False(reloaded.Rigs.First(r => r.Id == "beta0000").Enabled);
        Assert.False(reg.All.First(r => r.Id == "beta0000").Enabled);
    }

    [Fact]
    public void SupportsManagement_True() {
        using var reg = NewRegistry(TwoRigConfig());
        Assert.True(reg.SupportsManagement);
    }

    // The plain single-rig registry (primary / read-only mirror) reports no
    // management + always resolves to its one backend.
    [Fact]
    public void SingleRigRegistry_NoManagement() {
        var paths = new CompanionPaths(Path.Combine(_root, "rigs", "solo0000"));
        paths.EnsureExists();
        var backend = new RigBackend("solo0000", "Solo", true,
            new CompanionDataSource(paths.DatabasePath, paths.TsDatabasePath, new NullLog()),
            paths, null, null);
        var reg = new SingleRigRegistry(backend);
        Assert.False(reg.SupportsManagement);
        Assert.Equal("solo0000", reg.Resolve("anything").Id);
        Assert.Single(reg.All);
        Assert.Throws<NotSupportedException>(() => reg.RemoveRig("x", false));
    }
}
