using System;
using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Server {

    /// <summary>
    /// Minimal HTTP request parsed from a raw TCP stream.
    /// Exposes exactly the members that DashboardServer handlers use,
    /// with the same types as HttpListenerRequest so no handler code changes.
    /// </summary>
    public sealed class TcpHttpRequest {
        public string HttpMethod { get; internal set; } = "GET";
        public Uri Url { get; internal set; }
        public NameValueCollection QueryString { get; internal set; } = new NameValueCollection();
        public long ContentLength64 { get; internal set; }
        public Encoding ContentEncoding => Encoding.UTF8;
        public Stream InputStream { get; internal set; } = Stream.Null;
        public string UserAgent { get; internal set; }
        public string Authorization { get; internal set; }
        // IP address of the TCP peer. Captured at accept time so the auth
        // middleware can derive the companion's reachable push URL without
        // needing the companion to advertise its own IP. Null on synthetic
        // requests (tests / dev harness).
        public System.Net.IPAddress RemoteIp { get; internal set; }
        // Companion-advertised dashboard port for the originating companion.
        // Combined with RemoteIp to build "http://<ip>:<port>/" — the URL
        // the primary uses to push session-end sync triggers.
        public int? CompanionDashboardPort { get; internal set; }
        // Arbitrary headers not surfaced by a typed property. Used for low-
        // traffic custom headers like X-Sync-Trigger so we don't have to add
        // a new property for every signal. Keys are case-insensitive.
        public System.Collections.Generic.Dictionary<string, string> Headers { get; internal set; }
            = new(System.StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Conditional-GET helpers for endpoints that revalidate with an ETag.
    /// </summary>
    public static class HttpCaching {
        /// <summary>
        /// True when an If-None-Match header value matches <paramref name="etag"/>.
        /// Handles "*", comma-separated lists and weak (W/) validators, which
        /// compare equal to their strong form for GET revalidation.
        /// </summary>
        public static bool IfNoneMatchMatches(string ifNoneMatch, string etag) {
            if (string.IsNullOrWhiteSpace(ifNoneMatch) || string.IsNullOrEmpty(etag)) return false;
            static string Strip(string t) {
                t = t.Trim();
                return t.StartsWith("W/", StringComparison.Ordinal) ? t.Substring(2) : t;
            }
            var want = Strip(etag);
            foreach (var part in ifNoneMatch.Split(',')) {
                var tag = part.Trim();
                if (tag == "*") return true;
                if (string.Equals(Strip(tag), want, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Response compression policy. The dashboard is mostly reached over
    /// Tailscale from phones and tablets, where bytes cost real time: gzip cuts
    /// the inlined dashboard page from ~800 KB to ~200 KB and API JSON by ~80%.
    /// </summary>
    public static class HttpCompression {
        // Below this, gzip framing overhead and CPU outweigh the saving.
        public const int MinBytes = 1024;

        public static bool AcceptsGzip(string acceptEncoding) {
            if (string.IsNullOrEmpty(acceptEncoding)) return false;
            foreach (var part in acceptEncoding.Split(',')) {
                var token = part.Trim();
                var semi = token.IndexOf(';');
                var name = semi >= 0 ? token.Substring(0, semi).Trim() : token;
                if (!string.Equals(name, "gzip", StringComparison.OrdinalIgnoreCase)) continue;
                if (semi < 0) return true;
                // "gzip;q=0" is an explicit refusal; any positive q accepts.
                var param = token.Substring(semi + 1).Trim();
                if (!param.StartsWith("q=", StringComparison.OrdinalIgnoreCase)) return true;
                return double.TryParse(param.Substring(2), System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out var q) && q > 0;
            }
            return false;
        }

        public static bool IsCompressible(string contentType) {
            if (string.IsNullOrEmpty(contentType)) return false;
            return contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                || contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
                || contentType.StartsWith("application/javascript", StringComparison.OrdinalIgnoreCase)
                || contentType.StartsWith("image/svg+xml", StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldCompress(int statusCode, string contentType, int length)
            => statusCode == 200 && length >= MinBytes && IsCompressible(contentType);

        public static byte[] Gzip(byte[] body) {
            using var ms = new MemoryStream(body.Length / 3 + 64);
            using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                gz.Write(body, 0, body.Length);
            return ms.ToArray();
        }
    }

    /// <summary>
    /// Minimal HTTP response writer for a raw TCP stream.
    /// Body is buffered in memory; all headers + body are written atomically on Close().
    /// Close() is idempotent — callers that invoke res.OutputStream.Close() before res.Close()
    /// are safe because AppendOnlyStream ignores Dispose.
    /// </summary>
    public sealed class TcpHttpResponse {
        private readonly Stream _tcp;
        private readonly AppendOnlyStream _body = new AppendOnlyStream();
        private bool _closed;

        public int StatusCode { get; set; } = 200;
        public string ContentType { get; set; }
        public long ContentLength64 { get; set; }
        public WebHeaderCollection Headers { get; } = new WebHeaderCollection();
        public Stream OutputStream => _body;
        // Set from the request's Accept-Encoding. When true, Close() gzips
        // compressible bodies (see HttpCompression). StreamAsync never does:
        // exports are already-compressed zips or SQLite files, and the companion
        // sync client does not send Accept-Encoding anyway.
        public bool AcceptsGzip { get; set; }

        public TcpHttpResponse(Stream tcpStream) => _tcp = tcpStream;

        /// <summary>
        /// Streams the contents of <paramref name="source"/> directly to the underlying TCP
        /// stream after writing the HTTP status + headers. Bypasses the in-memory body buffer
        /// used by Close(), so multi-GB exports do not OOM. After this returns, the response
        /// is considered complete; subsequent Close() calls are no-ops.
        /// </summary>
        public async Task StreamAsync(string contentType, Stream source, long contentLength,
                                      System.Collections.Generic.IDictionary<string, string> extraHeaders = null) {
            if (_closed) return;
            _closed = true;
            try {
                var sb = new StringBuilder();
                sb.Append($"HTTP/1.1 {StatusCode} {GetStatusText(StatusCode)}\r\n");
                if (contentType != null)
                    sb.Append($"Content-Type: {contentType}\r\n");
                sb.Append($"Content-Length: {contentLength}\r\n");
                if (extraHeaders != null) {
                    foreach (var kv in extraHeaders) {
                        var lower = kv.Key.ToLowerInvariant();
                        if (lower == "content-type" || lower == "content-length") continue;
                        sb.Append($"{kv.Key}: {kv.Value}\r\n");
                    }
                }
                foreach (string key in Headers.AllKeys) {
                    var lower = key.ToLowerInvariant();
                    if (lower == "content-type" || lower == "content-length") continue;
                    sb.Append($"{key}: {Headers[key]}\r\n");
                }
                sb.Append("Connection: close\r\n\r\n");
                var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
                await _tcp.WriteAsync(headerBytes, 0, headerBytes.Length);
                await source.CopyToAsync(_tcp);
                await _tcp.FlushAsync();
            } catch { /* client disconnected — ignore */ }
        }

        public void Close() {
            if (_closed) return;
            _closed = true;
            try {
                var bodyBytes = _body.ToArray();
                bool gzip = AcceptsGzip
                    && Headers["Content-Encoding"] == null
                    && HttpCompression.ShouldCompress(StatusCode, ContentType, bodyBytes.Length);
                if (gzip) bodyBytes = HttpCompression.Gzip(bodyBytes);
                var sb = new StringBuilder();
                // HTTP/1.1, not 1.0: Chromium never revalidates an HTTP/1.0 response
                // (no If-None-Match is sent), so every ETag here would be ignored.
                // Connection: close + Content-Length stays valid 1.1 framing.
                sb.Append($"HTTP/1.1 {StatusCode} {GetStatusText(StatusCode)}\r\n");
                if (ContentType != null)
                    sb.Append($"Content-Type: {ContentType}\r\n");
                sb.Append($"Content-Length: {bodyBytes.Length}\r\n");
                if (gzip) sb.Append("Content-Encoding: gzip\r\n");
                if (HttpCompression.IsCompressible(ContentType)) sb.Append("Vary: Accept-Encoding\r\n");
                foreach (string key in Headers.AllKeys) {
                    var lower = key.ToLowerInvariant();
                    if (lower == "content-type" || lower == "content-length") continue;
                    sb.Append($"{key}: {Headers[key]}\r\n");
                }
                sb.Append("Connection: close\r\n\r\n");
                var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
                _tcp.Write(headerBytes, 0, headerBytes.Length);
                if (bodyBytes.Length > 0)
                    _tcp.Write(bodyBytes, 0, bodyBytes.Length);
                _tcp.Flush();
            } catch { /* client disconnected — ignore */ }
        }

        private static string GetStatusText(int code) => code switch {
            200 => "OK",
            202 => "Accepted",
            204 => "No Content",
            304 => "Not Modified",
            400 => "Bad Request",
            403 => "Forbidden",
            404 => "Not Found",
            405 => "Method Not Allowed",
            409 => "Conflict",
            413 => "Request Entity Too Large",
            500 => "Internal Server Error",
            _   => "Unknown",
        };

        /// <summary>
        /// MemoryStream wrapper that silently ignores Close/Dispose so handlers
        /// that call res.OutputStream.Close() don't prevent the buffer being read.
        /// </summary>
        private sealed class AppendOnlyStream : Stream {
            private readonly MemoryStream _ms = new MemoryStream();
            public byte[] ToArray() => _ms.ToArray();
            public override bool CanRead  => false;
            public override bool CanSeek  => false;
            public override bool CanWrite => true;
            public override long Length   => _ms.Length;
            public override long Position { get => _ms.Position; set { } }
            public override void Flush() { }
            public override int  Read(byte[] buf, int off, int cnt) => 0;
            public override long Seek(long off, SeekOrigin orig)    => 0;
            public override void SetLength(long val) { }
            public override void Write(byte[] buf, int off, int cnt) => _ms.Write(buf, off, cnt);
            public override Task WriteAsync(byte[] buf, int off, int cnt, CancellationToken ct)
                => _ms.WriteAsync(buf, off, cnt, ct);
            protected override void Dispose(bool disposing) { /* ignore — keep buffer alive */ }
        }
    }
}
