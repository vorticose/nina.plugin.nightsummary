using System;
using System.IO;
using NINA.Plugin.NightSummary.Companion;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests;

// Durability of the companion config across torn writes / truncation. The whole
// point is that a crash mid-save or a corrupt file never costs the user their
// host + pairing token (which would otherwise force a manual re-pair). Save is
// atomic (temp + rename) and rotates the previous good copy into companion.json.bak;
// Load falls back to that .bak when the primary is missing, empty, or unparseable.
public class CompanionConfigDurabilityTests {

    private static string Good(string host, string token) =>
        "{\"port\":8182,\"dataDir\":\"\",\"nina\":{\"host\":\"" + host +
        "\",\"port\":8181,\"pairingToken\":\"" + token +
        "\"},\"sync\":{\"onBoot\":true,\"acceptPush\":true}," +
        "\"enableReadOnlyMirror\":false,\"readOnlyMirrorPort\":8282}";

    private static void Cleanup(string path) {
        foreach (var p in new[] { path, path + ".bak", path + ".tmp" })
            if (File.Exists(p)) File.Delete(p);
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"ns-durability-{Guid.NewGuid():N}.json");

    [Fact]
    public void Save_FirstWrite_CreatesNoBak() {
        var path = TempPath();
        try {
            CompanionConfig.Save(new CompanionConfig { Nina = { Host = "a", PairingToken = "t1" } }, path);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".bak"));   // nothing to rotate yet
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Save_SecondWrite_RotatesPreviousIntoBak() {
        var path = TempPath();
        try {
            CompanionConfig.Save(new CompanionConfig { Nina = { Host = "first",  PairingToken = "t1" } }, path);
            CompanionConfig.Save(new CompanionConfig { Nina = { Host = "second", PairingToken = "t2" } }, path);

            Assert.True(File.Exists(path + ".bak"));
            Assert.Equal("second", CompanionConfig.Load(path).Nina.Host);            // primary = newest
            Assert.Equal("first",  CompanionConfig.Load(path + ".bak").Nina.Host);   // .bak = previous good
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Load_RecoversFromBak_WhenPrimaryCorrupt() {
        var path = TempPath();
        try {
            File.WriteAllText(path + ".bak", Good("100.86.208.29", "REAL-TOKEN"));
            File.WriteAllText(path, "{ this is not valid json");

            var loaded = CompanionConfig.Load(path);

            Assert.Equal("100.86.208.29", loaded.Nina.Host);
            Assert.Equal("REAL-TOKEN",    loaded.Nina.PairingToken);
            Assert.True(File.Exists(path + ".bak"));                                 // backup left intact
            Assert.Equal("100.86.208.29", CompanionConfig.Load(path).Nina.Host);    // primary restored in place
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Load_RecoversFromBak_WhenPrimaryEmpty() {
        var path = TempPath();
        try {
            File.WriteAllText(path + ".bak", Good("host-from-bak", "tok"));
            File.WriteAllText(path, "");   // truncated / zero-byte primary

            var loaded = CompanionConfig.Load(path);

            Assert.Equal("host-from-bak", loaded.Nina.Host);
            Assert.Equal("tok",           loaded.Nina.PairingToken);
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Load_PrefersPrimary_OverBak_WhenBothValid() {
        var path = TempPath();
        try {
            File.WriteAllText(path,          Good("primary-host", "p"));
            File.WriteAllText(path + ".bak", Good("stale-bak",    "b"));

            Assert.Equal("primary-host", CompanionConfig.Load(path).Nina.Host);
        } finally { Cleanup(path); }
    }

    [Fact]
    public void Load_MaterializesDefault_WhenNothingPresent() {
        var path = TempPath();
        try {
            Assert.False(File.Exists(path));
            var loaded = CompanionConfig.Load(path);

            Assert.Equal("", loaded.Nina.Host);            // fresh default
            Assert.False(loaded.IsComplete());
            Assert.True(File.Exists(path));                // materialized for editing
        } finally { Cleanup(path); }
    }
}
