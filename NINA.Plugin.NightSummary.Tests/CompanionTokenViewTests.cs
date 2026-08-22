using NINA.Plugin.NightSummary;
using NINA.Plugin.NightSummary.Data;
using System;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Tests for the pure projection layer behind the Options panel's pairing
    /// lists. WPF binding/layout is not unit-tested; the timestamp + display
    /// name rules are.
    /// </summary>
    public class CompanionTokenViewTests {

        [Fact]
        public void Paired_UsesCompanionNameAndPairedTimestamp() {
            var e = new CompanionTokenEntry {
                Id            = "abc123",
                CompanionName = "Mac mini",
                CreatedAt     = DateTime.UtcNow.AddDays(-10),
                PairedAt      = DateTime.UtcNow.AddHours(-3),
                LastUsedAt    = DateTime.UtcNow.AddMinutes(-5),
            };
            var v = new CompanionTokenView(e);

            Assert.Equal("Mac mini", v.DisplayName);
            Assert.True(v.IsPaired);
            Assert.StartsWith("paired ", v.TimestampText);
            Assert.Contains("3h ago", v.TimestampText);
        }

        [Fact]
        public void Unpaired_UsesNameOrPlaceholder_AndCreatedTimestamp() {
            var e = new CompanionTokenEntry {
                Id        = "abc123",
                Name      = null,
                CreatedAt = DateTime.UtcNow.AddDays(-2),
            };
            var v = new CompanionTokenView(e);

            Assert.Equal("(unnamed)", v.DisplayName);
            Assert.False(v.IsPaired);
            Assert.StartsWith("created ", v.TimestampText);
            Assert.Contains("not yet claimed", v.TimestampText);
            Assert.Contains("2d ago", v.TimestampText);
        }

        [Fact]
        public void Unpaired_WithPresetName_PrefersThatOverPlaceholder() {
            var e = new CompanionTokenEntry {
                Id        = "abc123",
                Name      = "Reserved for office",
                CreatedAt = DateTime.UtcNow.AddMinutes(-30),
            };
            var v = new CompanionTokenView(e);
            Assert.Equal("Reserved for office", v.DisplayName);
            Assert.Contains("30m ago", v.TimestampText);
        }

        [Theory]
        [InlineData(10,           "just now")]   // 10 seconds → just now
        [InlineData(60 * 5,       "5m ago")]
        [InlineData(60 * 60 * 4,  "4h ago")]
        [InlineData(86400 * 3,    "3d ago")]
        [InlineData(86400L * 90,  "3mo ago")]
        public void RelativeTimes_HumanizeAtExpectedThresholds(long secondsAgo, string expectedFragment) {
            var e = new CompanionTokenEntry {
                Id        = "x",
                CreatedAt = DateTime.UtcNow.AddSeconds(-secondsAgo),
            };
            var v = new CompanionTokenView(e);
            Assert.Contains(expectedFragment, v.TimestampText);
        }
    }
}
