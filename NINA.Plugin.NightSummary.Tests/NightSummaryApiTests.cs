using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Integration;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    // Unit tests for the pure logic of the TNS-facing NightSummaryApi facade:
    // settings masking, write-only patch semantics, and id validation. The DB-backed
    // methods (Sessions/Session/Resend/Delete) need live NINA wiring and are excluded,
    // matching the suite's Session/* integration exclusion.
    public class NightSummaryApiTests {

        private static readonly string[] Secrets =
            { "SmtpPassword", "DiscordWebhookUrl", "PushoverAppToken", "PushoverUserKey", "DashboardApiKey" };

        [Fact]
        public void MaskSettings_RemovesAllSecrets_AddsSetFlags() {
            var s = new NightSummarySettings {
                SmtpPassword      = "SECRETsmtp111",
                DiscordWebhookUrl = "https://discord/webhook/SECRETtok222",
                PushoverAppToken  = "SECRETatok333",
                PushoverUserKey   = "SECRETukey444",
                DashboardApiKey   = "SECRETakey555",
                SenderAddress     = "me@example.com"
            };

            var node = NightSummaryApi.MaskSettings(s, new[] { "Ha", "OIII" });
            var json = node.ToJsonString();

            foreach (var secret in Secrets) {
                Assert.False(node.ContainsKey(secret), $"{secret} must be removed");
                Assert.True(node.ContainsKey(secret + "Set"), $"{secret}Set flag must be present");
            }
            // No secret VALUES leak anywhere in the payload.
            Assert.DoesNotContain("SECRET", json);
            // Set flags reflect presence.
            Assert.True(node["SmtpPasswordSet"]!.GetValue<bool>());
            // Non-secret fields pass through.
            Assert.Equal("me@example.com", node["SenderAddress"]!.GetValue<string>());
            // Filter names included.
            Assert.Equal(2, node["_filterNames"]!.AsArray().Count);
        }

        [Fact]
        public void MaskSettings_EmptySecret_SetFlagFalse() {
            var s = new NightSummarySettings { SmtpPassword = "" };
            var node = NightSummaryApi.MaskSettings(s, null);
            Assert.False(node["SmtpPasswordSet"]!.GetValue<bool>());
            Assert.Empty(node["_filterNames"]!.AsArray());
        }

        [Fact]
        public void ApplyPatch_BlankSecret_KeepsCurrent() {
            var s = new NightSummarySettings { SmtpPassword = "existing-pw" };
            NightSummaryApi.ApplyPatch(s, "{\"SmtpPassword\":\"\"}");
            Assert.Equal("existing-pw", s.SmtpPassword); // blank secret ignored
        }

        [Fact]
        public void ApplyPatch_AbsentSecret_KeepsCurrent() {
            var s = new NightSummarySettings { SmtpPassword = "existing-pw" };
            NightSummaryApi.ApplyPatch(s, "{\"EmailEnabled\":true}");
            Assert.Equal("existing-pw", s.SmtpPassword);
            Assert.True(s.EmailEnabled);
        }

        [Fact]
        public void ApplyPatch_NonBlankSecret_Changes() {
            var s = new NightSummarySettings { SmtpPassword = "old" };
            NightSummaryApi.ApplyPatch(s, "{\"SmtpPassword\":\"new-pw\"}");
            Assert.Equal("new-pw", s.SmtpPassword);
        }

        [Fact]
        public void ApplyPatch_NonSecretFields_UpdateAcrossTypes() {
            var s = new NightSummarySettings { ReportDetailLevel = 1, ReportLightMode = false };
            NightSummaryApi.ApplyPatch(s, "{\"ReportDetailLevel\":3,\"ReportLightMode\":true,\"SaveReportPath\":\"C:/x\"}");
            Assert.Equal(3, s.ReportDetailLevel);
            Assert.True(s.ReportLightMode);
            Assert.Equal("C:/x", s.SaveReportPath);
        }

        [Fact]
        public void ApiVersion_IsStable() {
            Assert.Equal("1.0", NightSummaryApi.ApiVersion());
        }

        [Fact]
        public void Status_WhenNotWired_ReportsNotInstalled() {
            // Unwired state (no plugin running) must not throw and must envelope cleanly.
            NightSummaryApi.Unwire();
            var doc = JsonDocument.Parse(NightSummaryApi.Status());
            Assert.True(doc.RootElement.GetProperty("Success").GetBoolean());
            Assert.False(doc.RootElement.GetProperty("Response").GetProperty("Installed").GetBoolean());
        }

        [Fact]
        public void Sessions_WhenNotWired_ReturnsNotLoadedEnvelope() {
            NightSummaryApi.Unwire();
            var doc = JsonDocument.Parse(NightSummaryApi.Sessions(10));
            Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
            Assert.Contains("not loaded", doc.RootElement.GetProperty("Error").GetString());
        }

        [Theory]
        [InlineData("c04877f4-3bd3-4014-b948-d186bc0f2bf4", true)]
        [InlineData("../etc/passwd", false)]
        [InlineData("a/b", false)]
        [InlineData("", false)]
        public void SessionIdValidation(string id, bool expected) {
            Assert.Equal(expected, NightSummaryApi.IsSafeSessionIdForTest(id));
        }
    }
}
