using NINA.Plugin.NightSummary.Server;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// The mosaic survey thumbnail revalidates with an ETag so the browser never
    /// keeps an image from an earlier panel layout. These pin the If-None-Match
    /// comparison that decides between a 304 and a fresh image.
    /// </summary>
    public class HttpCachingTests {

        private const string Etag = "\"822d539bb917b66d4a9a328340ee8615\"";

        [Theory]
        [InlineData("\"822d539bb917b66d4a9a328340ee8615\"")]
        [InlineData("W/\"822d539bb917b66d4a9a328340ee8615\"")]
        [InlineData("  \"822d539bb917b66d4a9a328340ee8615\"  ")]
        [InlineData("\"other\", \"822d539bb917b66d4a9a328340ee8615\"")]
        [InlineData("*")]
        public void IfNoneMatch_SameLayout_IsNotModified(string header) {
            Assert.True(HttpCaching.IfNoneMatchMatches(header, Etag));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\"3285e1eca8541499b63b45e68e9d041f\"")]
        [InlineData("822d539bb917b66d4a9a328340ee8615")]
        [InlineData("\"822D539BB917B66D4A9A328340EE8615\"")]
        public void IfNoneMatch_DifferentOrMissing_SendsImage(string? header) {
            Assert.False(HttpCaching.IfNoneMatchMatches(header!, Etag));
        }
    }
}
