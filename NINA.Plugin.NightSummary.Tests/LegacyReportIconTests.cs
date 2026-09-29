using System;
using NINA.Plugin.NightSummary.Server;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Older reports inline the ~800 KB brand PNG as the 48px header icon; the
    /// dashboard swaps in the small icon when serving them. Only that exact header
    /// tag with a legacy-sized payload may change.
    /// </summary>
    public class LegacyReportIconTests {

        private const string Small = "data:image/png;base64,c21hbGw=";
        private static readonly string BigB64 = new string('A', LegacyReportIcon.LegacyMinBase64Chars + 10);

        private static string Header(string b64) =>
            "<div class='header'>\n  <img src='data:image/png;base64," + b64 +
            "' alt='Night Summary' style='width:48px; height:48px; border-radius:6px; flex-shrink:0;' />\n</div>";

        [Fact]
        public void LegacyIcon_IsReplacedWithSmallIcon() {
            var html = "<html><body>" + Header(BigB64) + "<p>rest</p></body></html>";
            var result = LegacyReportIcon.Shrink(html, Small);
            Assert.Equal("<html><body>" + "<div class='header'>\n  <img src='" + Small +
                         "' alt='Night Summary' style='width:48px; height:48px; border-radius:6px; flex-shrink:0;' />\n</div>" +
                         "<p>rest</p></body></html>", result);
        }

        [Fact]
        public void SmallIcon_IsLeftAlone() {
            var html = Header(new string('B', 12_000));
            Assert.Same(html, LegacyReportIcon.Shrink(html, Small));
        }

        [Fact]
        public void NoHeaderIcon_IsLeftAlone() {
            var html = "<img src='data:image/png;base64," + BigB64 + "' alt='M31' />";
            Assert.Same(html, LegacyReportIcon.Shrink(html, Small));
        }

        [Fact]
        public void MarkersFromDifferentTags_AreLeftAlone() {
            // A big image earlier in the page, then an unrelated element carrying the alt text.
            var html = "<img src='data:image/png;base64," + BigB64 + "' alt='x' /><img src='y.png' alt='Night Summary' />";
            Assert.Same(html, LegacyReportIcon.Shrink(html, Small));
        }

        [Fact]
        public void MissingSmallIcon_IsLeftAlone() {
            var html = Header(BigB64);
            Assert.Same(html, LegacyReportIcon.Shrink(html, null!));
        }
    }
}
