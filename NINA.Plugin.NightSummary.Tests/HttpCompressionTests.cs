using System.IO;
using System.IO.Compression;
using System.Text;
using NINA.Plugin.NightSummary.Server;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Dashboard responses are gzipped for clients that ask for it (phones and
    /// tablets over Tailscale), and left plain for clients that don't (the
    /// companion's sync client). These pin the negotiation, the policy, and the
    /// bytes Close() actually puts on the wire.
    /// </summary>
    public class HttpCompressionTests {

        [Theory]
        [InlineData("gzip")]
        [InlineData("gzip, deflate, br")]
        [InlineData("br, GZIP")]
        [InlineData("deflate, gzip;q=0.5")]
        [InlineData("gzip; q=1.0")]
        public void AcceptsGzip_WhenOffered(string header) {
            Assert.True(HttpCompression.AcceptsGzip(header));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("identity")]
        [InlineData("br, deflate")]
        [InlineData("gzip;q=0")]
        [InlineData("gzip; q=0.0")]
        [InlineData("x-gzip-ish")]
        public void AcceptsGzip_WhenNotOfferedOrRefused(string? header) {
            Assert.False(HttpCompression.AcceptsGzip(header!));
        }

        [Theory]
        [InlineData("text/html; charset=utf-8", true)]
        [InlineData("text/css; charset=utf-8", true)]
        [InlineData("application/json; charset=utf-8", true)]
        [InlineData("application/javascript; charset=utf-8", true)]
        [InlineData("image/svg+xml", true)]
        [InlineData("image/jpeg", false)]
        [InlineData("application/zip", false)]
        [InlineData("application/octet-stream", false)]
        [InlineData(null, false)]
        public void IsCompressible_TextYes_BinaryNo(string? contentType, bool expected) {
            Assert.Equal(expected, HttpCompression.IsCompressible(contentType!));
        }

        [Fact]
        public void ShouldCompress_SkipsSmallBodiesAndNon200() {
            const string json = "application/json; charset=utf-8";
            Assert.True(HttpCompression.ShouldCompress(200, json, HttpCompression.MinBytes));
            Assert.False(HttpCompression.ShouldCompress(200, json, HttpCompression.MinBytes - 1));
            Assert.False(HttpCompression.ShouldCompress(304, json, 50_000));
            Assert.False(HttpCompression.ShouldCompress(404, json, 50_000));
        }

        [Fact]
        public void Gzip_RoundTripsExactly() {
            var body = Encoding.UTF8.GetBytes(new string('x', 5000) + "é↑ end");
            Assert.Equal(body, Gunzip(HttpCompression.Gzip(body)));
        }

        [Fact]
        public void Close_WithAcceptsGzip_WritesGzipBodyAndHeaders() {
            var json = "{\"items\":[" + string.Join(",", System.Linq.Enumerable.Repeat("\"session\"", 500)) + "]}";
            var (headers, body) = Send(json, "application/json; charset=utf-8", acceptsGzip: true);

            Assert.Contains("Content-Encoding: gzip\r\n", headers);
            Assert.Contains("Vary: Accept-Encoding\r\n", headers);
            Assert.Contains($"Content-Length: {body.Length}\r\n", headers);
            Assert.True(body.Length < Encoding.UTF8.GetByteCount(json));
            Assert.Equal(json, Encoding.UTF8.GetString(Gunzip(body)));
        }

        [Fact]
        public void Close_WithoutAcceptsGzip_WritesPlainBody() {
            var json = "{\"items\":[" + string.Join(",", System.Linq.Enumerable.Repeat("\"session\"", 500)) + "]}";
            var (headers, body) = Send(json, "application/json; charset=utf-8", acceptsGzip: false);

            Assert.DoesNotContain("Content-Encoding", headers);
            Assert.Contains("Vary: Accept-Encoding\r\n", headers);
            Assert.Equal(json, Encoding.UTF8.GetString(body));
        }

        [Fact]
        public void Close_BinaryBody_NeverGzipped() {
            var (headers, _) = Send(new string('j', 5000), "image/jpeg", acceptsGzip: true);
            Assert.DoesNotContain("Content-Encoding", headers);
            Assert.DoesNotContain("Vary", headers);
        }

        // Chromium ignores ETags on HTTP/1.0 responses (never sends If-None-Match),
        // which silently disabled every conditional GET the dashboard relies on.
        [Fact]
        public void Close_WritesHttp11StatusLine() {
            var (headers, _) = Send("{}", "application/json; charset=utf-8", acceptsGzip: false);
            Assert.StartsWith("HTTP/1.1 200 OK\r\n", headers);
        }

        private static (string headers, byte[] body) Send(string content, string contentType, bool acceptsGzip) {
            var wire = new MemoryStream();
            var res = new TcpHttpResponse(wire) { ContentType = contentType, AcceptsGzip = acceptsGzip };
            var bytes = Encoding.UTF8.GetBytes(content);
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.Close();

            var all = wire.ToArray();
            var raw = Encoding.ASCII.GetString(all);
            int split = raw.IndexOf("\r\n\r\n", System.StringComparison.Ordinal) + 4;
            var body = new byte[all.Length - split];
            System.Array.Copy(all, split, body, 0, body.Length);
            return (raw.Substring(0, split), body);
        }

        private static byte[] Gunzip(byte[] gz) {
            using var input = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress);
            using var output = new MemoryStream();
            input.CopyTo(output);
            return output.ToArray();
        }
    }
}
