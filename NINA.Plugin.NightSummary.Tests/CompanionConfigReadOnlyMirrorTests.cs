using System.IO;
using NINA.Plugin.NightSummary.Companion;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests;

// Round-trip CompanionConfig through Load/Save with the new read-only mirror
// fields populated. Catches a class of bug where a renamed JSON property or
// missing [JsonPropertyName] silently defaults the field on next boot — the
// kind of thing that wouldn't crash but would silently disable the feature.
public class CompanionConfigReadOnlyMirrorTests {

    [Fact]
    public void Defaults_DisableMirror_OnPort8282() {
        var c = new CompanionConfig();
        Assert.False(c.EnableReadOnlyMirror);
        Assert.Equal(8282, c.ReadOnlyMirrorPort);
    }

    [Fact]
    public void RoundTrip_PreservesReadOnlyMirrorFields() {
        var path = Path.Combine(Path.GetTempPath(), $"ns-rom-{System.Guid.NewGuid():N}.json");
        try {
            var c = new CompanionConfig {
                EnableReadOnlyMirror = true,
                ReadOnlyMirrorPort   = 9282,
            };
            CompanionConfig.Save(c, path);

            var loaded = CompanionConfig.Load(path);
            Assert.True(loaded.EnableReadOnlyMirror);
            Assert.Equal(9282, loaded.ReadOnlyMirrorPort);
        } finally {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Load_OldConfigWithoutMirrorFields_GetsDefaults() {
        // Simulates upgrading from a pre-mirror companion.json. The new fields
        // should fall through to their defaults rather than throw.
        var path = Path.Combine(Path.GetTempPath(), $"ns-rom-legacy-{System.Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path, "{\"port\":8182,\"nina\":{\"host\":\"x\",\"port\":8181,\"pairingToken\":\"abc\"}}");
            var loaded = CompanionConfig.Load(path);
            Assert.False(loaded.EnableReadOnlyMirror);
            Assert.Equal(8282, loaded.ReadOnlyMirrorPort);
        } finally {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
