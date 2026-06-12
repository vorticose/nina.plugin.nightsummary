using System;
using System.IO;
using System.Linq;
using NINA.Plugin.NightSummary.Companion;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests;

// v2 multi-rig config: v1→v2 config-shape migration, data-dir relocation,
// default-rig resolution, per-rig completeness. Companion-global vs per-rig
// field placement.
public class CompanionMultiRigConfigTests {

    private sealed class NullLog : IDashboardLogger {
        public void Info(string m) { }
        public void Warn(string m) { }
        public void Error(string m, Exception? ex = null) { }
        public void Debug(string m) { }
    }


    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"ns-multirig-{Guid.NewGuid():N}.json");

    private static void Cleanup(string path) {
        foreach (var p in new[] { path, path + ".bak", path + ".tmp" })
            if (File.Exists(p)) File.Delete(p);
    }

    // Minimal v1-shaped JSON (no configVersion, top-level nina/sync).
    private static string V1Json(string host, string token) =>
        "{\"port\":8182,\"dataDir\":\"\",\"nina\":{\"host\":\"" + host +
        "\",\"port\":8181,\"pairingToken\":\"" + token +
        "\"},\"sync\":{\"onBoot\":true,\"acceptPush\":true}}";

    [Fact]
    public void Load_V1File_MigratesIntoRigsZero() {
        var path = TempPath();
        try {
            File.WriteAllText(path, V1Json("10.0.0.5", "TOKEN-A"));

            var cfg = CompanionConfig.Load(path);

            Assert.Single(cfg.Rigs);
            var rig = cfg.Rigs[0];
            Assert.Equal("10.0.0.5", rig.Nina.Host);
            Assert.Equal("TOKEN-A", rig.Nina.PairingToken);
            Assert.False(string.IsNullOrWhiteSpace(rig.Id));        // generated id
            Assert.Equal("10.0.0.5", rig.Name);                     // default name = host
            Assert.True(rig.Enabled);
            Assert.Equal(2, cfg.ConfigVersion);
            // Legacy proxy still reads the first rig.
            Assert.Equal("10.0.0.5", cfg.Nina.Host);
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Save_AlwaysWritesV2_DropsLegacyBlocks() {
        var path = TempPath();
        try {
            File.WriteAllText(path, V1Json("h1", "t1"));
            var cfg = CompanionConfig.Load(path);   // migrates in memory
            CompanionConfig.Save(cfg, path);

            var raw = File.ReadAllText(path);
            Assert.Contains("\"configVersion\": 2", raw);
            Assert.Contains("\"rigs\"", raw);
            // No stale top-level nina/sync blocks (they live inside rigs now).
            // The substring `"nina"` still appears nested under the rig, so assert
            // the legacy TOP-LEVEL shape is gone by checking it round-trips clean.
            var reloaded = CompanionConfig.Load(path);
            Assert.Single(reloaded.Rigs);
            Assert.Equal("h1", reloaded.Rigs[0].Nina.Host);
            Assert.Equal("t1", reloaded.Rigs[0].Nina.PairingToken);
        } finally { Cleanup(path); }
    }

    [Fact]
    public void RoundTrip_TwoRigs_Preserved() {
        var path = TempPath();
        try {
            var cfg = new CompanionConfig {
                Port = 8190,
                Rigs = {
                    new RigConfig { Id = "rig1aaaa", Name = "Backyard", Nina = { Host = "10.0.0.1", PairingToken = "tk1" } },
                    new RigConfig { Id = "rig2bbbb", Name = "Remote",   Enabled = false, Nina = { Host = "10.0.0.2", PairingToken = "tk2" } },
                },
            };
            CompanionConfig.Save(cfg, path);
            var loaded = CompanionConfig.Load(path);

            Assert.Equal(2, loaded.Rigs.Count);
            Assert.Equal("Backyard", loaded.Rigs[0].Name);
            Assert.Equal("rig2bbbb", loaded.Rigs[1].Id);
            Assert.False(loaded.Rigs[1].Enabled);
            Assert.Equal(8190, loaded.Port);
        } finally { Cleanup(path); }
    }

    [Fact]
    public void DefaultRig_PrefersEnabledComplete() {
        var cfg = new CompanionConfig {
            Rigs = {
                new RigConfig { Id = "a", Name = "A", Enabled = false, Nina = { Host = "h", PairingToken = "t" } },
                new RigConfig { Id = "b", Name = "B", Enabled = true,  Nina = { Host = "",  PairingToken = "" } },  // incomplete
                new RigConfig { Id = "c", Name = "C", Enabled = true,  Nina = { Host = "h", PairingToken = "t" } }, // enabled + complete
            },
        };
        Assert.Equal("c", cfg.DefaultRig()?.Id);
    }

    [Fact]
    public void DefaultRig_FallsBackToFirstEnabled_WhenNoneComplete() {
        var cfg = new CompanionConfig {
            Rigs = {
                new RigConfig { Id = "a", Enabled = false },
                new RigConfig { Id = "b", Enabled = true },   // incomplete but enabled
            },
        };
        Assert.Equal("b", cfg.DefaultRig()?.Id);
    }

    [Fact]
    public void IsComplete_TrueWhenAnyEnabledRigComplete() {
        var cfg = new CompanionConfig {
            Rigs = {
                new RigConfig { Enabled = true,  Nina = { Host = "", PairingToken = "" } },
                new RigConfig { Enabled = true,  Nina = { Host = "h", PairingToken = "t" } },
            },
        };
        Assert.True(cfg.IsComplete());
    }

    [Fact]
    public void IsComplete_FalseWhenNoRigs() {
        var cfg = new CompanionConfig();
        Assert.False(cfg.IsComplete(out var reason));
        Assert.Contains("no rig", reason);
    }

    [Fact]
    public void RigDataDir_NestsUnderRigsFolder() {
        var cfg = new CompanionConfig { DataDir = Path.Combine("C:", "comp") };
        var dir = cfg.RigDataDir("abc12345");
        Assert.Equal(Path.Combine("C:", "comp", "rigs", "abc12345"), dir);
    }

    [Fact]
    public void NewRigId_Is8CharsBase32() {
        var id = CompanionConfig.NewRigId();
        Assert.Equal(8, id.Length);
        Assert.All(id, c => Assert.Contains(c, "abcdefghijklmnopqrstuvwxyz234567"));
    }

    // ── Data-dir relocation ──────────────────────────────────────────────────

    [Fact]
    public void Relocate_MovesFlatTreeIntoRigDir_AndWritesMarker() {
        var root = Path.Combine(Path.GetTempPath(), $"ns-reloc-{Guid.NewGuid():N}");
        try {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "reports"));
            File.WriteAllText(Path.Combine(root, "nightsummary.sqlite"), "DB");
            File.WriteAllText(Path.Combine(root, "reports", "s1.html"), "<html>");
            File.WriteAllText(Path.Combine(root, "last_synced.json"), "{}");
            File.WriteAllText(Path.Combine(root, "tonight-preview-cache.json"), "{}");
            // Shared roots that must NOT move.
            Directory.CreateDirectory(Path.Combine(root, "logs"));
            File.WriteAllText(Path.Combine(root, "logs", "x.log"), "log");

            CompanionMigration.RelocateDataDirIfNeeded(root, "rig0000a", new NullLog());

            var rigRoot = Path.Combine(root, "rigs", "rig0000a");
            Assert.True(File.Exists(Path.Combine(rigRoot, "nightsummary.sqlite")));
            Assert.True(File.Exists(Path.Combine(rigRoot, "reports", "s1.html")));
            Assert.True(File.Exists(Path.Combine(rigRoot, "last_synced.json")));
            Assert.True(File.Exists(Path.Combine(rigRoot, "tonight-preview-cache.json")));
            Assert.True(File.Exists(Path.Combine(root, "migration.done")));
            // Originals moved out of root.
            Assert.False(File.Exists(Path.Combine(root, "nightsummary.sqlite")));
            Assert.False(Directory.Exists(Path.Combine(root, "reports")));
            // Shared logs untouched.
            Assert.True(File.Exists(Path.Combine(root, "logs", "x.log")));
        } finally {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Relocate_NoOp_WhenMarkerPresent() {
        var root = Path.Combine(Path.GetTempPath(), $"ns-reloc-{Guid.NewGuid():N}");
        try {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "migration.done"), "done");
            File.WriteAllText(Path.Combine(root, "nightsummary.sqlite"), "DB");

            CompanionMigration.RelocateDataDirIfNeeded(root, "rig0000a", new NullLog());

            // DB left in place (marker short-circuits before any move).
            Assert.True(File.Exists(Path.Combine(root, "nightsummary.sqlite")));
            Assert.False(Directory.Exists(Path.Combine(root, "rigs")));
        } finally {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Relocate_FreshInstall_WritesMarker_NoRigDir() {
        var root = Path.Combine(Path.GetTempPath(), $"ns-reloc-{Guid.NewGuid():N}");
        try {
            Directory.CreateDirectory(root);   // empty — no flat data

            CompanionMigration.RelocateDataDirIfNeeded(root, "rig0000a", new NullLog());

            Assert.True(File.Exists(Path.Combine(root, "migration.done")));
            Assert.False(Directory.Exists(Path.Combine(root, "rigs", "rig0000a")));
        } finally {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
