using NINA.Plugin.NightSummary.Dashboard.WebAssets;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System;
using System.Buffers.Binary;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    /// <summary>
    /// Guards the size of the icon inlined into the HTML report header.
    ///
    /// Reports are delivered as base64 MIME email bodies and Discord attachments,
    /// so the icon's byte size is paid on every send and inflates by 4/3 in base64.
    /// Pointing the report back at the 776x776 / ~600 KB brand master (as it did
    /// before) made the icon ~90% of an entire report and left the MIME body far
    /// more exposed to gateway rewriting and truncation. These tests fail loudly
    /// if that regresses, rather than letting reports silently balloon again.
    /// </summary>
    public class ReportIconTests {

        // Generous ceiling: the committed asset is ~9 KB. This is a tripwire for
        // "someone pointed this at the brand master again", not a golden-file check.
        private const int MaxIconBytes = 32 * 1024;

        // The report renders the icon at 48px; 144px covers 3x HiDPI.
        private const int ExpectedIconPixels = 144;

        public ReportIconTests() {
            // Same deterministic baseline the other ReportGenerator suites use.
            // ShowSkyThumbnails especially: it would otherwise inline fetched JPEGs
            // (and hit the network), which is exactly what the total-size assertion
            // below is trying to hold still.
            SettingsManager.Instance.Current.ShowSkyThumbnails      = false;
            SettingsManager.Instance.Current.ReportLightMode        = false;
            SettingsManager.Instance.Current.ReportDetailLevel      = 2;
            SettingsManager.Instance.Current.ShowHFRGraph           = false;
            SettingsManager.Instance.Current.ShowStarCountCV        = false;
            SettingsManager.Instance.Current.ShowPerTargetIQ        = false;
            SettingsManager.Instance.Current.ShowSessionHistory     = false;
            SettingsManager.Instance.Current.ShowNextNightPreview   = false;
            SettingsManager.Instance.Current.AdditionalChartConfigs = "";
            SettingsManager.Instance.Current.ExpandSectionsDefault  = false;
        }

        private static byte[] ReadReportIcon() {
            using var stream = typeof(ReportGenerator).Assembly
                                   .GetManifestResourceStream(AssetNames.HeaderIcon);
            Assert.NotNull(stream);
            using var ms = new System.IO.MemoryStream();
            stream!.CopyTo(ms);
            return ms.ToArray();
        }

        [Fact]
        public void ReportIcon_IsEmbeddedInTheDashboardAssembly() {
            var names = typeof(ReportGenerator).Assembly.GetManifestResourceNames();
            Assert.Contains(ReportGenerator.ReportIconResource, names);
        }

        [Fact]
        public void ReportIcon_IsNotTheFullSizeBrandMaster() {
            // Both inlining consumers must resolve to the small copy. The brand master
            // is ~600 KB and gets base64-expanded by 4/3 on every use.
            Assert.NotEqual(AssetNames.BrandMaster, AssetNames.HeaderIcon);
            Assert.NotEqual(AssetNames.BrandMaster, ReportGenerator.ReportIconResource);
        }

        [Fact]
        public void BrandMaster_IsNotEmbeddedInEitherShippedAssembly() {
            // The 776x776 master is ~600 KB and has no code consumer: no HTTP icon
            // endpoint, no favicon link, and NINA's plugin loader reads no embedded
            // resources. Embedding it just inflated the plugin DLL and the dashboard
            // classlib that ships with all three companion builds. It still lives at
            // assets/plugin-icon.png on disk for the store listing, the docs site and
            // gen-companion-icons.py; it must not come back as a resource.
            var dashboard = typeof(ReportGenerator).Assembly.GetManifestResourceNames();
            Assert.DoesNotContain(AssetNames.BrandMaster, dashboard);

            var plugin = typeof(SettingsManager).Assembly.GetManifestResourceNames();
            Assert.DoesNotContain(AssetNames.BrandMaster, plugin);
        }

        [Fact]
        public void ReportAndDashboardHeaders_ShareTheSameIcon() {
            // The report header and the dashboard header render the same mark at the
            // same 48px. If they ever drift apart, one of them is carrying bytes the
            // other proved unnecessary.
            Assert.Equal(AssetNames.HeaderIcon, ReportGenerator.ReportIconResource);
        }

        [Fact]
        public void ReportIcon_StaysSmallEnoughToInline() {
            var bytes = ReadReportIcon();
            Assert.InRange(bytes.Length, 1, MaxIconBytes);
        }

        [Fact]
        public void ReportIcon_IsAValidPngAtTheExpectedSize() {
            var bytes = ReadReportIcon();

            // PNG signature, then the IHDR chunk: 4-byte length, "IHDR", width, height.
            var signature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            Assert.Equal(signature, bytes.Take(8).ToArray());
            Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(bytes, 12, 4));

            var width  = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
            var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
            Assert.Equal(ExpectedIconPixels, width);
            Assert.Equal(ExpectedIconPixels, height);
        }

        [Fact]
        public async Task GeneratedReport_HeaderIconDataUri_IsSmall() {
            var data   = TestDataFactory.MakeReportData(imageCount: 5);
            var report = await TestDeps.NewReportGenerator().GenerateHtmlReport(data);

            var match = Regex.Match(report, "data:image/png;base64,([A-Za-z0-9+/=]+)");
            Assert.True(match.Success, "report header should still embed a PNG data URI");

            // 4/3 of MaxIconBytes, rounded up past base64 padding.
            var maxBase64Chars = (MaxIconBytes + 2) / 3 * 4;
            Assert.InRange(match.Groups[1].Value.Length, 1, maxBase64Chars);
        }

        [Fact]
        public async Task GeneratedReport_TotalSize_StaysWellUnderMailGatewayLimits() {
            var data   = TestDataFactory.MakeReportData(imageCount: 5);
            var report = await TestDeps.NewReportGenerator().GenerateHtmlReport(data);

            // A minimal report was ~840 KB when the brand master was inlined. Anything
            // near that means the icon (or another asset) is being inlined at full size.
            Assert.InRange(report.Length, 1, 400 * 1024);
        }
    }
}
