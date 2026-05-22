using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Server {

    public class DashboardServer {

        private TcpListener _tcpListener;
        private CancellationTokenSource cts;
        private readonly string dbPath;
        private readonly string cachePath;
        private readonly string reportsDir;
        private readonly string dataDir;
        private readonly IDashboardPaths _paths;     // kept so ThumbsRoot stays settings-aware
        private readonly IDashboardDataSource _data;
        private readonly IPluginSettings _settings;
        private readonly IWebAssets _webAssets;
        private readonly IDashboardLogger _external;
        private readonly IReportRegenerator _regen;
        private string cachedDashboardHtml;
        private DashboardLog log;
        private readonly HashSet<string> _loggedUserAgents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Sync helpers wrap the async data source so server code can call DbX() inline
        //    inside Linq/Select expressions without rewriting every block to async/Task.WhenAll.
        //    Server traffic is low (single-user LAN), so the brief block here is acceptable. ──
        private IReadOnlyList<SessionRecord>     DbSessions()                        => _data.GetAllSessionsAsync().GetAwaiter().GetResult();
        private SessionRecord                    DbSession(string sessionId)         => _data.GetSessionAsync(sessionId).GetAwaiter().GetResult();
        private IReadOnlyList<ImageRecord>       DbImages(string sessionId)          => _data.GetImagesAsync(sessionId).GetAwaiter().GetResult();
        private IReadOnlyList<SessionEvent>      DbEvents(string sessionId)          => _data.GetEventsAsync(sessionId).GetAwaiter().GetResult();
        private IReadOnlyList<TimingEvent>       DbTimingEvents(string sessionId)    => _data.GetTimingEventsAsync(sessionId).GetAwaiter().GetResult();
        private IReadOnlyList<TargetDetail>      DbTargetDetails()                   => _data.GetTargetDetailsAsync().GetAwaiter().GetResult();
        private List<TargetSessionDetail>        DbSessionsForTarget(string name)    => _data.GetSessionsForTargetAsync(name).GetAwaiter().GetResult().ToList();
        private bool                             TsAvailable()                       => _data.IsTargetSchedulerAvailableAsync().GetAwaiter().GetResult();
        private List<TsProjectInfo>              TsProjects()                        => _data.GetTSProjectsAsync().GetAwaiter().GetResult().ToList();
        private (bool enabled, int port, string host) TsApi() {
            var s = _data.GetTSApiSettingsAsync().GetAwaiter().GetResult();
            return s != null ? (s.Enabled, s.Port, s.Host) : (false, 0, "localhost");
        }

        // Regenerate-all progress tracking. regenAllRunning is an int so we can use
        // Interlocked.CompareExchange for an atomic check-and-set gate — concurrent
        // POSTs would otherwise both pass the bool check before either flipped it.
        private int regenAllRunning;
        private volatile int regenAllCurrent;
        private volatile int regenAllTotal;
        private volatile int regenAllGenerated;
        private volatile int regenAllFailed;
        private volatile string regenAllStatus; // "running", "done", "error"
        private volatile string regenAllError;

        // Thumbnail cache: sessionId -> list of (target, dataUri)
        // ConcurrentDictionary because multiple HTTP threads can hit Remove/TryGetValue
        // simultaneously; the previous Dictionary made check-then-remove non-atomic and
        // produced sporadic cache corruption under load.
        private readonly ConcurrentDictionary<string, List<ThumbnailEntry>> thumbnailCache = new();

        private class ThumbnailEntry {
            public string target { get; set; }
            public string dataUri { get; set; }
            public string fovSvg { get; set; }  // SVG overlay with FOV rectangle (from report)
        }

        // Altitude chart cache: sessionId -> { svg, legend }
        private readonly ConcurrentDictionary<string, object> altitudeChartCache = new();

        // Live stack cache: sessionId -> { target -> list of entries }
        private readonly ConcurrentDictionary<string, Dictionary<string, List<LiveStackEntry>>> livestackCache = new();

        private class LiveStackEntry {
            public string target { get; set; }
            public string filter { get; set; }
            public string url { get; set; }
            public string label { get; set; }
            public bool isComposite { get; set; }
        }

        // Altitude chart coordinate scaling: widen from 500 to 825 for better aspect ratio
        private const double AltPadL = 38.0;          // left padding (y-axis labels)
        private const double AltOrigRight = 490.0;    // original right edge of plot (500 - 10)
        private const double AltNewSvgW = 950.0;      // new viewBox width (plot area only)
        private const double AltNewRight = 940.0;     // new right edge (950 - 10)
        // Legend is rendered as HTML overlay — no SVG legend constants needed
        private static readonly double AltScaleX = (AltNewRight - AltPadL) / (AltOrigRight - AltPadL); // ~1.719

        /// <summary>Map an x-coordinate from the original 500-wide plot space to the wider 750-wide space.</summary>
        private static double MapX(double x) => AltPadL + (x - AltPadL) * AltScaleX;

        /// <summary>Scale all x-coordinates in a polyline points string ("x1,y1 x2,y2 ...").</summary>
        private static string ScalePolylineX(string points) {
            var parts = points.Split(' ');
            var sb = new StringBuilder(points.Length * 2);
            foreach (var part in parts) {
                if (sb.Length > 0) sb.Append(' ');
                var comma = part.IndexOf(',');
                if (comma < 0) { sb.Append(part); continue; }
                if (double.TryParse(part.Substring(0, comma), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double x)) {
                    sb.Append(MapX(x).ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                    sb.Append(part.Substring(comma)); // ",y" unchanged
                } else {
                    sb.Append(part);
                }
            }
            return sb.ToString();
        }

        /// <summary>Remap the x='...' attribute in an SVG element string.</summary>
        private static string RemapSvgX(string element) {
            return Regex.Replace(element, @"x='([\d.]+)'", m => {
                if (double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double x) && x >= AltPadL) {
                    return $"x='{MapX(x).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}'";
                }
                return m.Value; // keep axis labels (x < padL) unchanged
            });
        }

        // Target color palette (matches ReportGenerator.PreviewColors)
        private static readonly string[] TargetColors = {
            "#4e79a7", "#f28e2b", "#e15759", "#76b7b2", "#59a14f", "#edc948"
        };

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        public bool IsRunning { get; private set; }
        public string Url { get; private set; }
        public string TailscaleUrl { get; private set; }
        public string ZeroTierUrl { get; private set; }

        public DashboardServer(
            IDashboardDataSource data,
            IPluginSettings settings,
            IWebAssets webAssets,
            IDashboardLogger externalLog,
            IDashboardPaths paths,
            IReportRegenerator regen) {
            _data       = data       ?? throw new ArgumentNullException(nameof(data));
            _settings   = settings   ?? throw new ArgumentNullException(nameof(settings));
            _webAssets  = webAssets  ?? throw new ArgumentNullException(nameof(webAssets));
            _external   = externalLog ?? throw new ArgumentNullException(nameof(externalLog));
            _regen      = regen;     // optional — null in dev when regeneration is disabled

            // Path roots come from IDashboardPaths; the legacy fields stay so the rest
            // of the file's File.Exists/Path.Combine calls keep working unchanged.
            this._paths     = paths;
            this.dataDir    = paths.DataDir;
            this.cachePath  = Path.Combine(dataDir, "nightsummary-dashboard-cache.sqlite");
            this.reportsDir = paths.ReportsDir;
            this.dbPath     = paths.DatabasePath;
            Directory.CreateDirectory(reportsDir);
        }

        /// <summary>
        /// Starts the HTTP server bound to all interfaces (0.0.0.0).
        /// TcpListener is used instead of HttpListener so no urlacl registration or
        /// admin rights are required — HttpListener routes through HTTP.sys which
        /// enforces namespace reservations for any binding other than localhost.
        /// </summary>
        public Task StartAsync(int port) => StartAsync(port, IPAddress.Any);

        // Used by the dev harness; maps a host string to an IPAddress.
        public Task StartAsync(int port, string host) {
            var addr = host switch {
                "+" or "*" or "0.0.0.0" => IPAddress.Any,
                "localhost"              => IPAddress.Loopback,
                _                       => IPAddress.TryParse(host, out var ip) ? ip : IPAddress.Any,
            };
            return StartAsync(port, addr);
        }

        private Task StartAsync(int port, IPAddress bindAddress) {
            if (IsRunning) return Task.CompletedTask;

            try {
                var logsDir = Path.Combine(dataDir, "logs");
                Directory.CreateDirectory(logsDir);
                DashboardLog.PurgeOldLogs(logsDir);
                log = DashboardLog.Init(Path.Combine(logsDir, $"dashboard-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log"));

                cts = new CancellationTokenSource();
                _tcpListener = new TcpListener(bindAddress, port);
                _tcpListener.Start();

                var hostname = Dns.GetHostName();
                Url = $"http://{hostname}:{port}";
                TailscaleUrl = GetTailscaleUrl(port);
                ZeroTierUrl  = GetZeroTierUrl(port);
                IsRunning = true;

                // Fire-and-forget the request loop
                _ = AcceptLoop(cts.Token);

                // Initialize the persistent dashboard cache DB
                InitCacheDb();

                // Pre-warm altitude chart cache in background so first page load is instant
                _ = Task.Run(() => WarmAltitudeChartCache(cts.Token));

                log.Info($"NightSummary {_settings.PluginVersion ?? "unknown"} starting");
                log.Info($"Server started on port {port} — local: {Url}" +
                    (TailscaleUrl != null ? $", tailnet: {TailscaleUrl}" : "") +
                    (ZeroTierUrl  != null ? $", zerotier: {ZeroTierUrl}" : ""));
                var dbExists = File.Exists(dbPath);
                var dbSize   = dbExists ? $"{new FileInfo(dbPath).Length / 1024.0 / 1024.0:F1} MB" : "not found";
                log.Info($"DB: {dbPath} ({dbSize})");
                log.Info($"Reports: {reportsDir}");
                _ = Task.Run(async () => {
                    try {
                        var sessions = await _data.GetAllSessionsAsync();
                        log?.Info($"DB: {sessions.Count} session(s) on record");
                    } catch (Exception ex) {
                        log?.Warn($"Could not read session count at startup: {ex.Message}");
                    }
                });

                _external.Info($"NightSummary: Local dashboard started at {Url}");
                if (TailscaleUrl != null)
                    _external.Info($"NightSummary: Tailnet URL: {TailscaleUrl}");
                if (ZeroTierUrl != null)
                    _external.Info($"NightSummary: ZeroTier URL: {ZeroTierUrl}");
            } catch (Exception ex) {
                _external.Error($"NightSummary: Failed to start local dashboard. {ex.Message}");
                log?.Error("Server failed to start", ex);
                IsRunning = false;
                throw;
            }

            return Task.CompletedTask;
        }

        public Task StopAsync() {
            if (!IsRunning) return Task.CompletedTask;

            try {
                cts?.Cancel();
                _tcpListener?.Stop();
                _tcpListener = null;
                IsRunning = false;
                Url = null;
                TailscaleUrl = null;
                ZeroTierUrl  = null;
                log?.Info("Server stopped");
                _external.Info("NightSummary: Local dashboard stopped");
                DashboardLog.Shutdown();
                log = null;
            } catch (Exception ex) {
                _external.Error($"NightSummary: Error stopping local dashboard. {ex.Message}");
            }

            return Task.CompletedTask;
        }

        public string GetReportsDirectory() => reportsDir;

        private async Task AcceptLoop(CancellationToken ct) {
            while (!ct.IsCancellationRequested) {
                try {
                    var client = await _tcpListener.AcceptTcpClientAsync(ct);
                    _ = Task.Run(() => HandleTcpClient(client, ct), ct);
                } catch (OperationCanceledException) {
                    break;
                } catch (ObjectDisposedException) {
                    break;
                } catch (SocketException) {
                    break;
                } catch (Exception ex) {
                    log?.Error("Accept loop error", ex);
                    _external.Error($"NightSummary: Dashboard accept error. {ex.Message}");
                }
            }
        }

        private async Task HandleTcpClient(TcpClient client, CancellationToken ct) {
            try {
                using (client) {
                    client.ReceiveTimeout = 10_000;
                    client.SendTimeout    = 30_000;
                    var stream = client.GetStream();
                    var req = await ParseHttpRequestAsync(stream, ct);
                    if (req == null) return;
                    var res = new TcpHttpResponse(stream);
                    try {
                        await HandleRequest(req, res);
                    } finally {
                        res.Close();
                    }
                }
            } catch (Exception ex) {
                log?.Error($"Client handler error: {ex.Message}");
            }
        }

        private static async Task<TcpHttpRequest> ParseHttpRequestAsync(Stream stream, CancellationToken ct) {
            // Read one byte at a time until \r\n\r\n (end of HTTP headers)
            var headerBytes = new List<byte>(1024);
            var one = new byte[1];
            while (true) {
                int n = await stream.ReadAsync(one, 0, 1, ct);
                if (n == 0) return null;
                headerBytes.Add(one[0]);
                int c = headerBytes.Count;
                if (c >= 4
                    && headerBytes[c - 4] == '\r' && headerBytes[c - 3] == '\n'
                    && headerBytes[c - 2] == '\r' && headerBytes[c - 1] == '\n')
                    break;
                if (c > 16_384) return null; // header too large
            }

            var headerText  = Encoding.ASCII.GetString(headerBytes.ToArray());
            var lines       = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0) return null;
            var requestParts = lines[0].Split(' ');
            if (requestParts.Length < 2) return null;

            var method  = requestParts[0].ToUpperInvariant();
            var rawPath = requestParts[1];

            long contentLength = 0;
            string userAgent = null;
            for (int i = 1; i < lines.Length; i++) {
                var colon = lines[i].IndexOf(':');
                if (colon <= 0) continue;
                var name = lines[i].Substring(0, colon).Trim();
                var val  = lines[i].Substring(colon + 1).Trim();
                if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    long.TryParse(val, out contentLength);
                else if (string.Equals(name, "User-Agent", StringComparison.OrdinalIgnoreCase))
                    userAgent = val;
            }

            if (!rawPath.StartsWith("/")) rawPath = "/" + rawPath;
            Uri uri;
            try   { uri = new Uri("http://localhost" + rawPath); }
            catch { return null; }

            var queryString = ParseTcpQueryString(uri.Query);

            Stream bodyStream = Stream.Null;
            if (contentLength > 0 && contentLength <= 64 * 1024 * 1024) {
                var bodyBytes = new byte[contentLength];
                int offset = 0;
                while (offset < (int)contentLength) {
                    int read = await stream.ReadAsync(bodyBytes, offset, (int)contentLength - offset, ct);
                    if (read == 0) break;
                    offset += read;
                }
                bodyStream = new MemoryStream(bodyBytes);
            }

            return new TcpHttpRequest {
                HttpMethod      = method,
                Url             = uri,
                QueryString     = queryString,
                ContentLength64 = contentLength,
                InputStream     = bodyStream,
                UserAgent       = userAgent,
            };
        }

        private static System.Collections.Specialized.NameValueCollection ParseTcpQueryString(string query) {
            var result = new System.Collections.Specialized.NameValueCollection();
            if (string.IsNullOrEmpty(query)) return result;
            foreach (var pair in query.TrimStart('?').Split('&')) {
                if (string.IsNullOrEmpty(pair)) continue;
                var eq = pair.IndexOf('=');
                if (eq < 0)
                    result[TcpUnescape(pair)] = "";
                else
                    result[TcpUnescape(pair.Substring(0, eq))] = TcpUnescape(pair.Substring(eq + 1));
            }
            return result;
        }

        private static string TcpUnescape(string s) {
            try { return Uri.UnescapeDataString(s.Replace("+", " ")); }
            catch { return s; }
        }

        private async Task HandleRequest(TcpHttpRequest req, TcpHttpResponse res) {
            var path = req.Url.AbsolutePath.TrimEnd('/');
            if (string.IsNullOrEmpty(path)) path = "/";
            var done = log?.BeginRequest(req.HttpMethod, path);
            var ua = req.UserAgent;
            if (!string.IsNullOrEmpty(ua)) {
                lock (_loggedUserAgents) {
                    if (_loggedUserAgents.Add(ua))
                        log?.Info($"Client: {ua}");
                }
            }

            try {
                if (req.HttpMethod == "GET") {
                    if (path == "/api/health") {
                        await WriteJson(res, 200, new { status = "ok" });
                        done?.Invoke(200, null);
                    } else if (path == "/api/sessions") {
                        await HandleGetSessions(res, done);
                    } else if (path.StartsWith("/api/sessions/") && path.EndsWith("/images")) {
                        var sessionId = ExtractSessionId(path, "/images");
                        await HandleGetSessionImages(res, sessionId, done);
                    } else if (path.StartsWith("/api/sessions/") && path.EndsWith("/events")) {
                        var sessionId = ExtractSessionId(path, "/events");
                        await HandleGetSessionEvents(res, sessionId, done);
                    } else if (path.StartsWith("/api/sessions/") && path.EndsWith("/timing")) {
                        var sessionId = ExtractSessionId(path, "/timing");
                        await HandleGetSessionTiming(res, sessionId, done);
                    } else if (path.StartsWith("/api/sessions/") && path.EndsWith("/report")) {
                        var sessionId = ExtractSessionId(path, "/report");
                        await HandleGetSessionReport(res, sessionId, req.QueryString["theme"], done);
                    } else if (path.StartsWith("/api/sessions/") && path.EndsWith("/thumbnails")) {
                        var sessionId = ExtractSessionId(path, "/thumbnails");
                        await HandleGetSessionThumbnails(res, sessionId, done);
                    } else if (path.StartsWith("/api/sessions/") && path.Contains("/livestack/")) {
                        // Serve individual live stack image file: /api/sessions/{id}/livestack/{file}.jpg
                        // Target names can contain spaces (e.g. "M 101_R.jpg") — decode them.
                        var afterSessions = path.Substring("/api/sessions/".Length);
                        var slashIdx = afterSessions.IndexOf('/');
                        var sessionId = Uri.UnescapeDataString(afterSessions.Substring(0, slashIdx));
                        var filename = Uri.UnescapeDataString(afterSessions.Substring(slashIdx + "/livestack/".Length));
                        await HandleGetLiveStackImage(res, sessionId, filename, done);
                    } else if (path.StartsWith("/api/sessions/") && path.EndsWith("/livestack")) {
                        var sessionId = ExtractSessionId(path, "/livestack");
                        await HandleGetSessionLiveStack(res, sessionId, done);
                    } else if (path.StartsWith("/api/sessions/") && path.EndsWith("/altitude-chart")) {
                        var sessionId = ExtractSessionId(path, "/altitude-chart");
                        await HandleGetAltitudeChart(res, sessionId, done);
                    } else if (path.StartsWith("/api/sessions/") && path.EndsWith("/settings")) {
                        var sessionId = ExtractSessionId(path, "/settings");
                        await HandleGetSessionSettings(res, sessionId, done);
                    } else if (path.StartsWith("/api/sessions/") && !path.Substring("/api/sessions/".Length).Contains("/")) {
                        var sessionId = path.Substring("/api/sessions/".Length);
                        await HandleGetSession(res, sessionId, done);
                    } else if (path == "/api/stats/targets") {
                        await HandleGetTargetStats(res, done);
                    } else if (path.StartsWith("/api/stats/targets/") && path.EndsWith("/sessions")) {
                        var prefix = "/api/stats/targets/";
                        var suffix = "/sessions";
                        var encoded = path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length);
                        var targetName = Uri.UnescapeDataString(encoded);
                        await HandleGetTargetSessions(res, targetName, done);
                    } else if (path.StartsWith("/api/stats/projects/") && path.EndsWith("/sessions")) {
                        var pPrefix = "/api/stats/projects/";
                        var pSuffix = "/sessions";
                        var pEncoded = path.Substring(pPrefix.Length, path.Length - pPrefix.Length - pSuffix.Length);
                        var pGuid = Uri.UnescapeDataString(pEncoded);
                        await HandleGetProjectSessions(res, pGuid, done);
                    } else if (path.StartsWith("/api/stats/projects/") && path.EndsWith("/mosaic-thumb")) {
                        var mPrefix = "/api/stats/projects/";
                        var mSuffix = "/mosaic-thumb";
                        var mEncoded = path.Substring(mPrefix.Length, path.Length - mPrefix.Length - mSuffix.Length);
                        var mGuid = Uri.UnescapeDataString(mEncoded);
                        await HandleGetProjectMosaicThumb(res, mGuid, done);
                    } else if (path.StartsWith("/api/stats/projects/") && !path.Substring("/api/stats/projects/".Length).Contains("/")) {
                        var projectGuid = Uri.UnescapeDataString(path.Substring("/api/stats/projects/".Length));
                        await HandleGetProjectStats(res, projectGuid, done);
                    } else if (path == "/api/stats/summary") {
                        await HandleGetStatsSummary(res, done);
                    } else if (path == "/api/tonight/preview") {
                        await HandleGetTonightPreview(res, done);
                    } else if (path == "/api/ts/projects") {
                        await HandleGetTsProjects(res, done);
                    } else if (path == "/api/filters") {
                        await HandleGetFilters(res, done);
                    } else if (path.StartsWith("/api/frames/") && path.EndsWith("/thumb")) {
                        // /api/frames/{imageId}/thumb?size=sm|md
                        var fPrefix = "/api/frames/";
                        var fSuffix = "/thumb";
                        var fIdStr  = path.Substring(fPrefix.Length, path.Length - fPrefix.Length - fSuffix.Length);
                        if (!long.TryParse(fIdStr, out var fId)) {
                            await WriteJson(res, 400, new { error = "Invalid frame id" });
                            done?.Invoke(400, "bad id");
                        } else {
                            var size = req.QueryString["size"];
                            await HandleGetFrameThumb(res, fId, size, done);
                        }
                    } else if (path.StartsWith("/api/frames/") && path.EndsWith("/metrics")) {
                        // /api/frames/{imageId}/metrics — NS row + TS augmentation for the lightbox panel
                        var mPrefix = "/api/frames/";
                        var mSuffix = "/metrics";
                        var mIdStr  = path.Substring(mPrefix.Length, path.Length - mPrefix.Length - mSuffix.Length);
                        if (!long.TryParse(mIdStr, out var mId)) {
                            await WriteJson(res, 400, new { error = "Invalid frame id" });
                            done?.Invoke(400, "bad id");
                        } else {
                            await HandleGetFrameMetrics(res, mId, done);
                        }
                    } else if (path.StartsWith("/api/targets/") && path.EndsWith("/frames")) {
                        var tPrefix  = "/api/targets/";
                        var tSuffix  = "/frames";
                        var tEncoded = path.Substring(tPrefix.Length, path.Length - tPrefix.Length - tSuffix.Length);
                        var tName    = Uri.UnescapeDataString(tEncoded);
                        await HandleGetTargetFrames(res, tName, done);
                    } else if (path.StartsWith("/api/projects/") && path.EndsWith("/frames")) {
                        var pjPrefix  = "/api/projects/";
                        var pjSuffix  = "/frames";
                        var pjEncoded = path.Substring(pjPrefix.Length, path.Length - pjPrefix.Length - pjSuffix.Length);
                        var pjGuid    = Uri.UnescapeDataString(pjEncoded);
                        await HandleGetProjectFrames(res, pjGuid, done);
                    } else if (path == "/api/regenerate-all/status") {
                        await HandleRegenAllStatus(res);
                        done?.Invoke(200, null);
                    } else if (path == "/api/settings") {
                        await HandleGetSettings(res);
                        done?.Invoke(200, null);
                    } else if (path == "/") {
                        await WriteHtml(res, 200, GetDashboardHtml());
                        done?.Invoke(200, "dashboard html");
                    } else {
                        await WriteJson(res, 404, new { error = "Not found" });
                        done?.Invoke(404, null);
                    }
                } else if (req.HttpMethod == "POST") {
                    if (path == "/api/regenerate-all") {
                        await HandleRegenerateAll(req, res, done);
                    } else if (path.StartsWith("/api/sessions/") && path.EndsWith("/regenerate")) {
                        var sessionId = ExtractSessionId(path, "/regenerate");
                        await HandleRegenerateReport(req, res, sessionId, done);
                    } else if (path.StartsWith("/api/sessions/") && path.EndsWith("/resync-ts-grading")) {
                        var sessionId = ExtractSessionId(path, "/resync-ts-grading");
                        await HandleResyncTsGrading(res, sessionId, done);
                    } else if (path == "/api/stats/ts/override") {
                        await HandleTsStatusOverride(req, res, done);
                    } else if (path == "/api/stats/ts/link") {
                        await HandleTsTargetLink(req, res, done);
                    } else if (path == "/api/stats/ts/assign") {
                        await HandleTsAssign(req, res, done);
                    } else if (path == "/api/stats/ts/exclude") {
                        await HandleTsExclude(req, res, done);
                    } else if (path == "/api/stats/projects/custom") {
                        await HandleCustomProjects(req, res, done);
                    } else if (path == "/api/clientlog") {
                        await HandleClientLog(req, res, done);
                    } else if (path == "/api/stats/projects/reset") {
                        await HandleProjectsReset(req, res, done);
                    } else if (path.StartsWith("/api/stats/projects/") && path.EndsWith("/reset")) {
                        var pguid = Uri.UnescapeDataString(path.Substring("/api/stats/projects/".Length,
                            path.Length - "/api/stats/projects/".Length - "/reset".Length));
                        await HandleProjectReset(req, res, pguid, done);
                    } else {
                        await WriteJson(res, 404, new { error = "Not found" });
                        done?.Invoke(404, null);
                    }
                } else {
                    res.StatusCode = 405;
                    res.Close();
                    done?.Invoke(405, null);
                }
            } catch (Exception ex) {
                log?.Error($"Request error: {req.HttpMethod} {path}", ex);
                _external.Error($"NightSummary: Dashboard request error for {req.Url}. {ex.Message}");
                done?.Invoke(500, ex.Message);
                try { await WriteJson(res, 500, new { error = "Internal server error" }); } catch { res.Close(); }
            }
        }

        private string ExtractSessionId(string path, string suffix) {
            var start = "/api/sessions/".Length;
            var end = path.Length - suffix.Length;
            return path.Substring(start, end - start);
        }

        // ── API Handlers ──────────────────────────────────────────────────────

        private async Task HandleGetSessions(TcpHttpResponse res, Action<int, string> done) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, Array.Empty<object>());
                done?.Invoke(200, "0 sessions (no db)");
                return;
            }

            var sessions = await _data.GetAllSessionsAsync();
            // Hide in-progress sessions (SessionEnd unset or before SessionStart) — they have
            // no finalized data, no report, and no thumbnails. Live status is shown in the
            // Tonight tab via /api/tonight/preview.
            var completed = sessions.Where(s => s.SessionEnd > s.SessionStart).ToList();
            var result = completed.Select(s => {
                var images = DbImages(s.SessionId);
                var lightImages = images.Where(i => string.IsNullOrEmpty(i.ImageType) || i.ImageType == "LIGHT").ToList();
                // Extract moon phase from report if available
                string moonPhase = null;
                var reportPath = Path.Combine(reportsDir, $"{s.SessionId}.html");
                bool hasReport = File.Exists(reportPath);
                if (hasReport) {
                    try {
                        var html = File.ReadAllText(reportPath);
                        // Match: <div class='stat-value'>42% ↑</div><div class='stat-label'>Moon</div>
                        var moonMatch = Regex.Match(html, @"<div class='stat-value'>(\d+%\s*[^\<]*)</div>\s*<div class='stat-label'>Moon</div>");
                        if (moonMatch.Success) moonPhase = System.Text.RegularExpressions.Regex.Replace(System.Net.WebUtility.HtmlDecode(moonMatch.Groups[1].Value), @"\s+", " ").Trim();
                    } catch { }
                }
                return new {
                    sessionId = s.SessionId,
                    sessionStart = s.SessionStart.ToString("o"),
                    sessionEnd = s.SessionEnd.ToString("o"),
                    profileName = s.ProfileName,
                    imageCount = lightImages.Count,
                    targets = lightImages
                        .Where(i => !string.IsNullOrEmpty(i.TargetName))
                        .Select(i => i.TargetName).Distinct().ToList(),
                    totalIntegrationSeconds = lightImages.Where(i => i.CountsAsAccepted).Sum(i => i.ExposureDuration),
                    avgHfr = lightImages.Where(i => i.HFR > 0).Select(i => i.HFR).DefaultIfEmpty(0).Average(),
                    avgFwhm = lightImages.Where(i => i.FWHM > 0).Select(i => i.FWHM).DefaultIfEmpty(0).Average(),
                    avgGuiding = lightImages.Where(i => i.GuidingRMSTotal > 0).Select(i => i.GuidingRMSTotal).DefaultIfEmpty(0).Average(),
                    hasReport,
                    moonPhase
                };
            }).ToList();

            if (result.Count == 0) log?.Warn("Sessions query returned 0 completed sessions — DB exists but may have no finalized data");
            await WriteJson(res, 200, result);
            done?.Invoke(200, $"{result.Count} sessions");
        }

        private async Task HandleGetSession(TcpHttpResponse res, string sessionId, Action<int, string> done) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 404, new { error = "Database not found" });
                done?.Invoke(404, "no db");
                return;
            }

            var session = DbSession(sessionId);
            if (session == null) {
                await WriteJson(res, 404, new { error = "Session not found" });
                done?.Invoke(404, sessionId);
                return;
            }

            var images = DbImages(sessionId);
            var lightImages = images.Where(i => string.IsNullOrEmpty(i.ImageType) || i.ImageType == "LIGHT").ToList();
            var events = DbEvents(sessionId);

            var targetBreakdown = lightImages
                .Where(i => !string.IsNullOrEmpty(i.TargetName))
                .GroupBy(i => i.TargetName)
                .Select(g => new {
                    target = g.Key,
                    imageCount = g.Count(),
                    accepted = g.Count(i => i.CountsAsAccepted),
                    rejected = g.Count(i => !i.CountsAsAccepted),
                    integrationSeconds = g.Where(i => i.CountsAsAccepted).Sum(i => i.ExposureDuration),
                    avgHfr = g.Where(i => i.HFR > 0).Select(i => i.HFR).DefaultIfEmpty(0).Average(),
                    avgFwhm = g.Where(i => i.FWHM > 0).Select(i => i.FWHM).DefaultIfEmpty(0).Average(),
                    avgGuiding = g.Where(i => i.GuidingRMSTotal > 0).Select(i => i.GuidingRMSTotal).DefaultIfEmpty(0).Average(),
                    avgStarCount = g.Where(i => i.StarCount > 0).Select(i => (double)i.StarCount).DefaultIfEmpty(0).Average(),
                    filters = g.GroupBy(i => i.Filter ?? "Unknown").Select(fg => new {
                        filter = fg.Key,
                        count = fg.Count(),
                        accepted = fg.Count(i => i.CountsAsAccepted),
                        integrationSeconds = fg.Where(i => i.CountsAsAccepted).Sum(i => i.ExposureDuration)
                    }).ToList()
                }).ToList();

            var result = new {
                sessionId = session.SessionId,
                sessionStart = session.SessionStart.ToString("o"),
                sessionEnd = session.SessionEnd.ToString("o"),
                profileName = session.ProfileName,
                notes = session.Notes,
                skippedExposures = session.SkippedExposures,
                equipment = new {
                    camera = session.CameraName,
                    telescope = session.TelescopeName,
                    mount = session.MountName,
                    filterWheel = session.FilterWheelName,
                    focuser = session.FocuserName,
                    rotator = session.RotatorName,
                    guider = session.GuiderName,
                    dome = session.DomeName,
                    flatDevice = session.FlatDeviceName,
                    safetyMonitor = session.SafetyMonitorName,
                    weather = session.WeatherName,
                    switchHub = session.SwitchName
                },
                cameraInfo = new {
                    xSize = session.CamXSize,
                    ySize = session.CamYSize,
                    pixelSizeMicrons = session.PixelSizeMicrons,
                    focalLengthMm = session.FocalLengthMm
                },
                summary = new {
                    totalImages = lightImages.Count,
                    accepted = lightImages.Count(i => i.CountsAsAccepted),
                    rejected = lightImages.Count(i => !i.CountsAsAccepted),
                    totalIntegrationSeconds = lightImages.Where(i => i.CountsAsAccepted).Sum(i => i.ExposureDuration),
                    avgHfr = lightImages.Where(i => i.HFR > 0).Select(i => i.HFR).DefaultIfEmpty(0).Average(),
                    avgFwhm = lightImages.Where(i => i.FWHM > 0).Select(i => i.FWHM).DefaultIfEmpty(0).Average(),
                    avgGuiding = lightImages.Where(i => i.GuidingRMSTotal > 0).Select(i => i.GuidingRMSTotal).DefaultIfEmpty(0).Average(),
                    avgStarCount = lightImages.Where(i => i.StarCount > 0).Select(i => (double)i.StarCount).DefaultIfEmpty(0).Average(),
                    autoFocusRuns = events.Count(e => e.EventType == "AutoFocus"),
                    meridianFlips = events.Count(e => e.EventType == "MeridianFlip")
                },
                targets = targetBreakdown,
                hasReport = File.Exists(Path.Combine(reportsDir, $"{sessionId}.html"))
            };

            await WriteJson(res, 200, result);
            done?.Invoke(200, $"{sessionId} — {targetBreakdown.Count} targets, {lightImages.Count} images");
        }

        // Fire-and-forget from the dashboard on session-detail load. Queries TS for any
        // late grading verdicts on Pending images and updates the NS DB. Cheap pre-check
        // in the data source skips the TS query entirely when nothing is Pending.
        private async Task HandleResyncTsGrading(TcpHttpResponse res, string sessionId, Action<int, string> done) {
            if (string.IsNullOrEmpty(sessionId)) {
                await WriteJson(res, 400, new { error = "Missing session id" });
                done?.Invoke(400, null);
                return;
            }
            try {
                int updated = await _data.ResyncTsGradingAsync(sessionId);
                await WriteJson(res, 200, new { updated });
                done?.Invoke(200, $"{sessionId} — {updated} grading row(s) refreshed");
            } catch (Exception ex) {
                log?.Warn($"Resync TS grading failed for {sessionId}: {ex.Message}");
                await WriteJson(res, 500, new { error = ex.Message });
                done?.Invoke(500, ex.Message);
            }
        }

        private async Task HandleGetSessionImages(TcpHttpResponse res, string sessionId, Action<int, string> done) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, Array.Empty<object>());
                done?.Invoke(200, "0 images (no db)");
                return;
            }

            var images = DbImages(sessionId);
            // gradingStatus / rejectReason are TS-import residue; suppress them
            // when TS is unavailable so frame tiles don't render as rejected for
            // a non-TS user (see HandleGetFrameMetrics for the same gating).
            // Also reset accepted=true when TS sync had previously written accepted=false
            // for a Pending row (legacy DBs from before the UpdateImageGradingFromTs
            // fix), or when TS isn't installed at all (stale TS-import residue).
            bool tsAvailable = await _data.IsTargetSchedulerAvailableAsync();
            var result = images.Select(i => new {
                id = i.Id,
                timestamp = i.Timestamp.ToString("o"),
                targetName = i.TargetName,
                filter = i.Filter,
                exposureDuration = i.ExposureDuration,
                imageType = i.ImageType,
                // Pending (gradingStatus=0) is "TS hasn't graded yet" — render as accepted.
                accepted = (!tsAvailable && i.GradingStatus >= 0) ? true : i.CountsAsAccepted,
                hfr = i.HFR,
                fwhm = i.FWHM,
                eccentricity = i.Eccentricity,
                starCount = i.StarCount,
                guidingRmsTotal = i.GuidingRMSTotal,
                focuserTemp = i.FocuserTemp,
                ambientTemp = i.AmbientTemp,
                cameraTemp = i.CameraTemp,
                humidity = i.Humidity,
                dewPoint = i.DewPoint,
                windSpeed = i.WindSpeed,
                pressure = i.Pressure,
                altitude = i.Altitude,
                azimuth = i.Azimuth,
                airmass = i.Airmass,
                gain = i.Gain,
                offset = i.Offset,
                binning = i.Binning,
                focuserPosition = i.FocuserPosition,
                skyQuality = i.SkyQuality,
                cloudCover = i.CloudCover,
                seeingFwhm = i.SeeingFWHM,
                gradingStatus = tsAvailable ? i.GradingStatus : -1,
                rejectReason = tsAvailable ? i.RejectReason : null,
                // Raw image thumbs — null/0 = none, 1 = sm, 2 = md, 3 = both.
                thumbnailVersion = i.ThumbnailVersion,
                filePath = i.FilePath
            }).ToList();

            await WriteJson(res, 200, result);
            done?.Invoke(200, $"{result.Count} images for {sessionId}");
        }

        private async Task HandleGetSessionEvents(TcpHttpResponse res, string sessionId, Action<int, string> done) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, Array.Empty<object>());
                done?.Invoke(200, "0 events (no db)");
                return;
            }

            var events = DbEvents(sessionId);
            var result = events.Select(e => new {
                id = e.Id,
                timestamp = e.Timestamp.ToString("o"),
                eventType = e.EventType,
                description = e.Description,
                afSucceeded = e.AfSucceeded,
                afHfr = e.AfHfr
            }).ToList();

            await WriteJson(res, 200, result);
            done?.Invoke(200, $"{result.Count} events for {sessionId}");
        }

        private async Task HandleGetSessionTiming(TcpHttpResponse res, string sessionId, Action<int, string> done) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, Array.Empty<object>());
                done?.Invoke(200, "0 timing events (no db)");
                return;
            }

            var events = DbTimingEvents(sessionId);
            var result = events.Select(t => new {
                eventType = t.EventType,
                startTime = t.StartTime.ToString("o"),
                endTime = t.EndTime.ToString("o"),
                durationSeconds = t.DurationSeconds,
                details = t.Details
            }).ToList();

            await WriteJson(res, 200, result);
            done?.Invoke(200, $"{result.Count} timing events for {sessionId}");
        }

        private async Task HandleGetSessionReport(TcpHttpResponse res, string sessionId, string theme, Action<int, string> done) {
            var reportPath = Path.Combine(reportsDir, $"{sessionId}.html");
            if (!File.Exists(reportPath)) {
                await WriteJson(res, 404, new { error = "Report not found" });
                done?.Invoke(404, sessionId);
                return;
            }
            var html = File.ReadAllText(reportPath);
            if (theme == "light" || theme == "dark") {
                // Inject data-theme attribute and CSS variable override so initial render matches dashboard theme
                html = System.Text.RegularExpressions.Regex.Replace(html,
                    @"<html(\s[^>]*)?>",
                    m => {
                        var attrs = System.Text.RegularExpressions.Regex.Replace(m.Value, @"\s*data-theme=['""][^'""]*['""]", "");
                        return attrs.Replace("<html", $"<html data-theme='{theme}'");
                    });
                const string lightSvgOverrides =
                    "svg rect[fill='#0d1117'] { fill: #e8eef5; }" +
                    "svg [stroke='#2d2d5e'] { stroke: #c0c8d4; }" +
                    "svg [fill='#2d2d5e'] { fill: #c0c8d4; }" +
                    "svg text[fill='#888'] { fill: #666; }" +
                    "svg [stroke='#c0c0c0'] { stroke: #7a8a9e; }" +
                    "svg [stroke='#7eb8f7'] { stroke: #2563b8; }" +
                    "svg rect[fill='#1a1a2e'] { fill: #f5f5f5; }" +
                    "svg [stroke='#2a2a4a'] { stroke: #c8cdd4; }" +
                    "svg [stroke='#555577'] { stroke: #666688; }" +
                    "svg text[fill='#aaaacc'] { fill: #555577; }" +
                    "svg circle[fill='#a8d4ff'] { fill: #1a4f9e; }" +
                    "svg circle[fill='#ffd4a8'] { fill: #b85c10; }" +
                    "svg rect[fill='#3a1e00'] { fill: #fff3cd; }" +
                    "svg text[fill='#e0e0e0'] { fill: #1a1a2e; }";
                const string darkSvgOverrides =
                    "svg rect[fill='#e8eef5'] { fill: #0d1117; }" +
                    "svg rect[fill='#f5f5f5'] { fill: #1a1a2e; }" +
                    "svg text[fill='#1a1a2e'] { fill: #e0e0e0; }";
                var (bg, lightCss, darkCss) = theme == "light"
                    ? ("#f5f5f5",
                       ":root { --bg: #f5f5f5; --text: #1a1a2e; --accent: #2563b8; --accent-light: #3b7dd8; --accent-lighter: #5a9ae6; --surface: #e8ecf1; --border: #c0c8d4; --muted: #666; --dim: #888; --chart-bg: #e0e4ea; --chart-dark: #d0d4da; --bar-acquired: #8bb0d4; --warn-bg: #fff3cd; --warn-border: #d4a850; --warn-text: #856404; --warn-item: #6d5200; --skip-color: #cc3333; }" + lightSvgOverrides,
                       "")
                    : ("#1a1a2e",
                       "",
                       ":root { --bg: #1a1a2e; --text: #e0e0e0; --accent: #7eb8f7; --accent-light: #a0c4ff; --accent-lighter: #c0d8ff; --surface: #16213e; --border: #2d2d5e; --muted: #888; --dim: #555; --chart-bg: #0d1117; --chart-dark: #0f0f23; --bar-acquired: #3a5a7a; --warn-bg: #3a2a00; --warn-border: #b8860b; --warn-text: #f0c040; --warn-item: #d4a850; --skip-color: #cc6666; }" + darkSvgOverrides);
                // Prevent iOS bounce on iframe content (not baked into report HTML — only injected when served via dashboard)
                const string iframeOnlyCss = "html { overscroll-behavior: none; }";
                var overrideStyle = $"<style id='ns-theme-override'>{lightCss}{darkCss}{iframeOnlyCss}</style>";
                html = html.Replace("</head>", overrideStyle + "</head>");
                // Also patch any hardcoded inline html background
                html = System.Text.RegularExpressions.Regex.Replace(html,
                    @"(<html[^>]*style=['""][^'""]*background-color:)[^;'""]+",
                    "$1" + bg);
            }
            await WriteHtml(res, 200, html);
            done?.Invoke(200, $"{sessionId} ({html.Length / 1024}KB)");
        }

        private async Task HandleGetSessionThumbnails(TcpHttpResponse res, string sessionId, Action<int, string> done) {
            if (thumbnailCache.TryGetValue(sessionId, out var cached)) {
                await WriteJson(res, 200, cached);
                done?.Invoke(200, $"{sessionId} — {cached.Count} thumbs (cached)");
                return;
            }

            var reportPath = Path.Combine(reportsDir, $"{sessionId}.html");
            if (!File.Exists(reportPath)) {
                var empty = new List<ThumbnailEntry>();
                thumbnailCache[sessionId] = empty;
                await WriteJson(res, 200, empty);
                done?.Invoke(200, $"{sessionId} — no report");
                return;
            }

            var html = File.ReadAllText(reportPath);
            var entries = new List<ThumbnailEntry>();

            // Split HTML on target-section boundaries and extract h3 + thumbnail + FOV overlay from each
            var sections = html.Split(new[] { "<div class='target-section'>" }, StringSplitOptions.None);
            var h3Pattern = new Regex(@"<h3>([^<]+)");
            var imgPattern = new Regex(@"<div\s+class='ts-thumb-wrap'>\s*<img\s+src='(data:image/[^']+)'");
            var svgPattern = new Regex(@"<div\s+class='ts-thumb-wrap'>[^<]*<img[^>]*/>\s*(<svg[^>]*>.*?</svg>)", RegexOptions.Singleline);

            for (int i = 1; i < sections.Length; i++) { // skip first (before any target-section)
                var block = sections[i];
                var h3Match = h3Pattern.Match(block);
                var imgMatch = imgPattern.Match(block);
                if (h3Match.Success && imgMatch.Success) {
                    var svgMatch = svgPattern.Match(block);
                    entries.Add(new ThumbnailEntry {
                        target = h3Match.Groups[1].Value.Trim(),
                        dataUri = imgMatch.Groups[1].Value,
                        fovSvg = svgMatch.Success ? svgMatch.Groups[1].Value : null
                    });
                }
            }

            thumbnailCache[sessionId] = entries;
            await WriteJson(res, 200, entries);
            done?.Invoke(200, $"{sessionId} — {entries.Count} thumbs");
        }

        private async Task HandleGetSessionLiveStack(TcpHttpResponse res, string sessionId, Action<int, string> done) {
            if (livestackCache.TryGetValue(sessionId, out var cached)) {
                await WriteJson(res, 200, cached);
                var total = cached.Values.Sum(l => l.Count);
                done?.Invoke(200, $"{sessionId} — {total} livestack images (cached)");
                return;
            }

            // Try master files first, fall back to extracting from report HTML
            var result = ExtractLiveStackFromMasters(sessionId);
            if (result.Count == 0) {
                result = ExtractLiveStackFromReport(sessionId);
            }

            livestackCache[sessionId] = result;
            var totalImages = result.Values.Sum(l => l.Count);
            await WriteJson(res, 200, result);
            done?.Invoke(200, $"{sessionId} — {totalImages} livestack images across {result.Count} targets");
        }

        // ── Raw image thumbnails (RAW_THUMBNAILS_DESIGN.md) ──────────────────
        // Per-call so the user's ThumbnailStorageDir override is picked up immediately
        // without restarting the server. Defaults to %LOCALAPPDATA%\NINA\NightSummary\thumbs
        // when no override is set.
        private string ThumbsRoot => _paths.ThumbsRoot;

        // Walks the NS Image rows for {sessionId} to find the row with matching
        // Id, returns its sessionId. Used so the binary endpoint doesn't have to
        // know about sessionId — clients pass just the frame id.
        private (string sessionId, int? versionMask) ResolveFrame(long imageId) {
            // We don't have an indexed-by-id query exposed via IDashboardDataSource,
            // so do a small scan via DbSessions(). Sessions list is small (<1000)
            // and we early-exit on first match. Could be optimized later with a
            // direct GetImageByIdAsync if needed.
            foreach (var s in DbSessions()) {
                foreach (var img in DbImages(s.SessionId)) {
                    if (img.Id == imageId) return (s.SessionId, img.ThumbnailVersion);
                }
            }
            return (null, null);
        }

        // Per-frame metrics for the lightbox panel. Returns the NS Image row plus
        // optional TS augmentation (Project, ExposureTemplate, per-axis guiding RMS).
        // Fields default to null when TS is unavailable or no row matches — the JS
        // hides any chip whose value is null/empty.
        private async Task HandleGetFrameMetrics(TcpHttpResponse res, long imageId, Action<int, string> done) {
            // Locate the NS row + session via the same scan ResolveFrame uses.
            ImageRecord img = null;
            SessionRecord sess = null;
            foreach (var s in DbSessions()) {
                foreach (var i in DbImages(s.SessionId)) {
                    if (i.Id == imageId) { img = i; sess = s; break; }
                }
                if (img != null) break;
            }
            if (img == null) {
                await WriteJson(res, 404, new { error = "Frame not found" });
                done?.Invoke(404, $"frame {imageId} unknown");
                return;
            }

            // TS augment via the data-source abstraction — null when TS is not
            // installed or no row matches; never throws to caller.
            string projectName = null, templateName = null;
            double? guidingRA = null, guidingRAArcsec = null;
            double? guidingDec = null, guidingDecArcsec = null;
            double? hfrStDev = null;
            int? augGrading = null;
            string augReject = null;
            try {
                // ±30s window centered on either NS Timestamp (new convention) or
                // NS Timestamp - ExposureDuration (legacy ImageSaved convention) — see
                // TargetSchedulerDatabase.GetImageAugment for the two-pass strategy.
                // Matches the importer's ±30s so identical frames augment consistently.
                var aug = await _data.GetTsImageAugmentAsync(img.TargetName, img.Filter, img.Timestamp, 30, img.ExposureDuration);
                if (aug != null) {
                    projectName      = aug.ProjectName;
                    templateName     = aug.ExposureTemplateName;
                    hfrStDev         = aug.HFRStDev;
                    guidingRA        = aug.GuidingRMSRA;
                    guidingRAArcsec  = aug.GuidingRMSRAArcSec;
                    guidingDec       = aug.GuidingRMSDEC;
                    guidingDecArcsec = aug.GuidingRMSDECArcSec;
                    augGrading       = aug.GradingStatus;
                    augReject        = aug.RejectReason;
                }
            } catch (Exception ex) {
                _external.Warn($"NightSummary: HandleGetFrameMetrics TS augment failed: {ex.Message}");
            }

            // Prefer NS's grading when set (>= 0); fall back to whatever TS recorded for
            // legacy rows that pre-date NS capturing the field. -1 stays as the
            // "genuinely unknown" sentinel and the JS renders a "Not graded" chip.
            //
            // When TS is unavailable (uninstalled, --no-ts dev sim, etc.), the NS
            // gradingStatus / rejectReason columns are stale TS-import residue —
            // a true non-TS user would never have them populated. Suppress so the
            // lightbox doesn't claim "TS Rejected" on a non-TS session. Also reset
            // accepted=true when the false came from TS sync (gradingStatus >= 0
            // means UpdateImageGradingFromTs set accepted = gradingStatus==1, so
            // accepted=false there is TS rejection, not NINA-side manual).
            bool tsAvailable = await _data.IsTargetSchedulerAvailableAsync();
            int finalGrading;
            string finalReject;
            // Pending (GradingStatus=0) counts as accepted — see ImageRecord.CountsAsAccepted.
            // Heals legacy DB rows where UpdateImageGradingFromTs wrote Accepted=false for
            // Pending images before the fix.
            bool effectivelyAccepted = img.CountsAsAccepted;
            if (tsAvailable) {
                finalGrading = img.GradingStatus >= 0
                    ? img.GradingStatus
                    : (augGrading.HasValue ? augGrading.Value : -1);
                finalReject = !string.IsNullOrEmpty(img.RejectReason) ? img.RejectReason : augReject;
            } else {
                finalGrading = -1;
                finalReject = null;
                if (img.GradingStatus >= 0) effectivelyAccepted = true;
            }

            // img.GuidingRMSTotal is stored in arcseconds (px × scale at capture
            // time — see SessionDatabase.SaveImageRecord comment). Use it directly
            // for the arcsec representation; derive px by dividing by scale.
            // (Earlier this multiplied by scale again, producing arcsec², making
            // the "RMS arcsec" row in the lightbox 1.5–2.5× too high vs the
            // per-axis values from TS.)
            double? guidingArcsec = img.GuidingRMSTotal > 0
                ? (double?)img.GuidingRMSTotal : null;
            double? guidingRmsPx = (img.GuidingRMSTotal > 0 && img.GuidingScale > 0)
                ? (double?)(img.GuidingRMSTotal / img.GuidingScale) : null;

            await WriteJson(res, 200, new {
                // Identity
                id = img.Id,
                sessionId = sess.SessionId,
                profileName = sess.ProfileName,
                timestamp = img.Timestamp.ToString("o"),
                // Lets the lightbox suppress the "Not graded" pill for non-TS users —
                // an ungraded label only makes sense when grading is even a concept.
                tsAvailable = tsAvailable,

                // Capture
                targetName = img.TargetName,
                filter = img.Filter,
                exposureDuration = img.ExposureDuration,
                gain = img.Gain,
                offset = img.Offset,
                binning = img.Binning,
                readoutMode = img.ReadoutMode,
                filePath = img.FilePath,

                // Quality
                hfr = img.HFR,
                hfrStDev = hfrStDev,
                fwhm = img.FWHM,
                eccentricity = img.Eccentricity,
                starCount = img.StarCount,

                // ADU (from NS v2.10+ StatX columns)
                aduMin = img.StatMin,
                aduMax = img.StatMax,
                aduMean = img.StatMean,
                aduMedian = img.StatMedian,
                aduStDev = img.StatStDev,

                // Guiding — total from NS, RA/Dec from TS when present
                guidingRmsTotal = guidingRmsPx,
                guidingArcsec = guidingArcsec,
                guidingRmsRa = guidingRA,
                guidingRmsRaArcsec = guidingRAArcsec,
                guidingRmsDec = guidingDec,
                guidingRmsDecArcsec = guidingDecArcsec,

                // Environment
                airmass = img.Airmass,
                altitude = img.Altitude,
                azimuth = img.Azimuth,
                cameraTemp = img.CameraTemp,
                focuserTemp = img.FocuserTemp,
                focuserPosition = img.FocuserPosition,
                ambientTemp = img.AmbientTemp,
                humidity = img.Humidity,
                pressure = img.Pressure,

                // Status
                accepted = effectivelyAccepted,
                gradingStatus = finalGrading,
                rejectReason = finalReject,

                // TS-only context
                project = projectName,
                exposureTemplate = templateName
            });
            done?.Invoke(200, $"frame {imageId} metrics");
        }

        private async Task HandleGetFrameThumb(TcpHttpResponse res, long imageId, string size, Action<int, string> done) {
            var sizeFlag = string.Equals(size, "md", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
            var (sessionId, _) = ResolveFrame(imageId);
            if (string.IsNullOrEmpty(sessionId)) {
                await WriteJson(res, 404, new { error = "Frame not found" });
                done?.Invoke(404, $"frame {imageId} unknown");
                return;
            }

            var path = Path.Combine(ThumbsRoot, sessionId, imageId + (sizeFlag == 2 ? "_md.jpg" : "_sm.jpg"));
            if (!File.Exists(path)) {
                // Fallback: requested medium but only small exists → serve small.
                if (sizeFlag == 2) {
                    var fallback = Path.Combine(ThumbsRoot, sessionId, imageId + "_sm.jpg");
                    if (File.Exists(fallback)) path = fallback;
                }
                if (!File.Exists(path)) {
                    await WriteJson(res, 404, new { error = "Thumbnail not found" });
                    done?.Invoke(404, $"thumb {imageId} missing");
                    return;
                }
            }

            try {
                var bytes = File.ReadAllBytes(path);
                res.ContentType = "image/jpeg";
                res.ContentLength64 = bytes.Length;
                // Content-addressed by frame id + size — never mutates after capture.
                res.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
                res.StatusCode = 200;
                await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                done?.Invoke(200, $"frame {imageId} {size ?? "sm"} ({bytes.Length / 1024}KB)");
            } catch (Exception ex) {
                _external.Warn($"NightSummary: Failed to serve thumbnail {path}: {ex.Message}");
                await WriteJson(res, 500, new { error = "Failed to read thumbnail" });
                done?.Invoke(500, ex.Message);
            }
        }

        // Cross-session: every LIGHT frame for this target name (case-insensitive),
        // newest first. Each entry carries enough context for the gallery (sessionId,
        // timestamp, filter, thumb version).
        private async Task HandleGetTargetFrames(TcpHttpResponse res, string targetName, Action<int, string> done) {
            if (string.IsNullOrEmpty(targetName)) {
                await WriteJson(res, 400, new { error = "Missing target name" });
                done?.Invoke(400, null);
                return;
            }

            // Suppress TS-derived gradingStatus when TS is unavailable — same
            // reason as HandleGetSessionImages. Also reset accepted=true when
            // TS sync had set it to false.
            bool tsAvailable = await _data.IsTargetSchedulerAvailableAsync();
            var rows = new List<object>();
            foreach (var s in DbSessions()) {
                foreach (var img in DbImages(s.SessionId)) {
                    if (!string.Equals(img.TargetName, targetName, StringComparison.OrdinalIgnoreCase)) continue;
                    if ((img.ThumbnailVersion ?? 0) == 0) continue;
                    rows.Add(new {
                        id = img.Id,
                        sessionId = s.SessionId,
                        timestamp = img.Timestamp.ToString("o"),
                        filter = img.Filter,
                        exposureDuration = img.ExposureDuration,
                        // Pending (gradingStatus=0) is "TS hasn't graded yet" — render as accepted.
                        accepted = (!tsAvailable && img.GradingStatus >= 0) ? true : img.CountsAsAccepted,
                        gradingStatus = tsAvailable ? img.GradingStatus : -1,
                        thumbnailVersion = img.ThumbnailVersion
                    });
                }
            }
            // Newest first
            rows.Reverse();
            await WriteJson(res, 200, rows);
            done?.Invoke(200, $"{rows.Count} frames for target {targetName}");
        }

        // TS-mediated: project guid → list of TS target names → aggregate frames.
        // Returns 404 when TS is not installed; the dashboard hides the project tab in that case.
        private async Task HandleGetProjectFrames(TcpHttpResponse res, string projectGuid, Action<int, string> done) {
            if (string.IsNullOrEmpty(projectGuid)) {
                await WriteJson(res, 400, new { error = "Missing project guid" });
                done?.Invoke(400, null);
                return;
            }
            if (!TsAvailable()) {
                await WriteJson(res, 404, new { error = "Target Scheduler not installed" });
                done?.Invoke(404, "no TS");
                return;
            }

            var targetNames = TsProjects()
                .Where(p => string.Equals(p.Guid, projectGuid, StringComparison.OrdinalIgnoreCase))
                .SelectMany(p => p.Targets ?? new List<TsProjectTarget>())
                .Select(t => t.Name)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (targetNames.Count == 0) {
                await WriteJson(res, 200, Array.Empty<object>());
                done?.Invoke(200, $"project {projectGuid} has no targets");
                return;
            }

            var rows = new List<object>();
            foreach (var s in DbSessions()) {
                foreach (var img in DbImages(s.SessionId)) {
                    if (!targetNames.Contains(img.TargetName ?? "")) continue;
                    if ((img.ThumbnailVersion ?? 0) == 0) continue;
                    rows.Add(new {
                        id = img.Id,
                        sessionId = s.SessionId,
                        timestamp = img.Timestamp.ToString("o"),
                        targetName = img.TargetName,
                        filter = img.Filter,
                        exposureDuration = img.ExposureDuration,
                        // Pending (gradingStatus=0) is "TS hasn't graded yet" — render as accepted.
                        accepted = img.CountsAsAccepted,
                        gradingStatus = img.GradingStatus,
                        thumbnailVersion = img.ThumbnailVersion
                    });
                }
            }
            rows.Reverse();
            await WriteJson(res, 200, rows);
            done?.Invoke(200, $"{rows.Count} frames for project {projectGuid}");
        }

        private async Task HandleGetLiveStackImage(TcpHttpResponse res, string sessionId, string filename, Action<int, string> done) {
            // Belt-and-suspenders against path traversal: strip any directory components,
            // then verify the resolved path is still under the livestack root. The earlier
            // Contains("..") check missed URL-encoded variants and unusual separators.
            var safeName = Path.GetFileName(filename ?? "");
            if (string.IsNullOrEmpty(safeName) || safeName != filename) {
                await WriteJson(res, 400, new { error = "Invalid filename" });
                done?.Invoke(400, "invalid filename");
                return;
            }

            var liveRoot = Path.GetFullPath(Path.Combine(reportsDir, "livestack", sessionId));
            var filePath = Path.GetFullPath(Path.Combine(liveRoot, safeName));
            if (!filePath.StartsWith(liveRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !filePath.Equals(liveRoot, StringComparison.OrdinalIgnoreCase)) {
                await WriteJson(res, 400, new { error = "Invalid filename" });
                done?.Invoke(400, "path escape");
                return;
            }
            if (!File.Exists(filePath)) {
                await WriteJson(res, 404, new { error = "Image not found" });
                done?.Invoke(404, filename);
                return;
            }

            try {
                var bytes = File.ReadAllBytes(filePath);
                res.ContentType = "image/jpeg";
                res.ContentLength64 = bytes.Length;
                // Cache for 1 hour — images don't change unless report is regenerated
                res.Headers["Cache-Control"] = "public, max-age=3600";
                res.StatusCode = 200;
                await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                done?.Invoke(200, $"{sessionId}/{filename} ({bytes.Length / 1024}KB)");
            } catch (Exception ex) {
                _external.Warn($"NightSummary: Failed to serve livestack image {filePath}: {ex.Message}");
                await WriteJson(res, 500, new { error = "Failed to read image" });
                done?.Invoke(500, ex.Message);
            }
        }

        private Dictionary<string, List<LiveStackEntry>> ExtractLiveStackFromMasters(string sessionId) {
            var result = new Dictionary<string, List<LiveStackEntry>>();
            var assetsDir = Path.Combine(reportsDir, "livestack", sessionId);
            var manifestPath = Path.Combine(assetsDir, "livestack.json");
            if (!File.Exists(manifestPath)) return result;

            try {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json);
                foreach (var entry in manifest) {
                    var target = entry["target"].GetString();
                    var filter = entry["filter"].GetString();
                    var file = entry["file"].GetString();
                    var isMono = entry["isMonochrome"].GetBoolean();
                    var stackCount = entry["stackCount"].GetInt32();
                    var isComposite = !isMono || filter.Equals("RGB", StringComparison.OrdinalIgnoreCase);

                    string label;
                    if (isComposite && entry.ContainsKey("redStackCount") && entry["redStackCount"].ValueKind == JsonValueKind.Number) {
                        label = $"Composite \u00b7 R:{entry["redStackCount"].GetInt32()} G:{entry["greenStackCount"].GetInt32()} B:{entry["blueStackCount"].GetInt32()}";
                    } else {
                        label = $"{filter} \u00b7 {stackCount} frames";
                    }

                    if (!File.Exists(Path.Combine(assetsDir, file))) continue;

                    if (!result.ContainsKey(target))
                        result[target] = new List<LiveStackEntry>();

                    result[target].Add(new LiveStackEntry {
                        target = target, filter = filter,
                        url = $"/api/sessions/{sessionId}/livestack/{file}",
                        label = label, isComposite = isComposite
                    });
                }
            } catch (Exception ex) {
                _external.Warn($"NightSummary: Failed to read livestack manifest for {sessionId}: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// Fallback: extract live stack images from the report HTML as base64 data URIs.
        /// Used when master JPEG files haven't been saved to the dashboard directory yet.
        /// </summary>
        private Dictionary<string, List<LiveStackEntry>> ExtractLiveStackFromReport(string sessionId) {
            var result = new Dictionary<string, List<LiveStackEntry>>();
            var reportPath = Path.Combine(reportsDir, $"{sessionId}.html");
            if (!File.Exists(reportPath)) return result;

            try {
                var html = File.ReadAllText(reportPath);
                var sections = html.Split(new[] { "<div class='target-section'>" }, StringSplitOptions.None);
                var h3Pattern = new Regex(@"<h3>([^<]+)");
                // Mono images: <img class='ts-livestack-img' src='data:...' alt='Ha stack' />
                var monoImgPattern = new Regex(@"<img\s+class='ts-livestack-img'\s+src='(data:image/[^']+)'\s+alt='([^']*)'", RegexOptions.Singleline);
                var monoLabelPattern = new Regex(@"<div class='ts-livestack-label'>([^<]+)</div>");
                // Composite images: <div class='ts-livestack-composite'><img src='data:...' ...
                var compImgPattern = new Regex(@"<div class='ts-livestack-composite'>\s*<img\s+src='(data:image/[^']+)'", RegexOptions.Singleline);
                var compLabelPattern = new Regex(@"<div class='ts-livestack-composite'>.*?<div class='ts-livestack-label'>([^<]+)</div>", RegexOptions.Singleline);

                for (int i = 1; i < sections.Length; i++) {
                    var block = sections[i];
                    var h3Match = h3Pattern.Match(block);
                    if (!h3Match.Success || !block.Contains("livestack-section")) continue;

                    var targetName = h3Match.Groups[1].Value.Trim();
                    var entries = new List<LiveStackEntry>();

                    // Mono images
                    var monoMatches = monoImgPattern.Matches(block);
                    var monoLabels = monoLabelPattern.Matches(block);
                    for (int j = 0; j < monoMatches.Count; j++) {
                        var filter = monoMatches[j].Groups[2].Value.Replace(" stack", "");
                        var rawLabel = j < monoLabels.Count ? System.Net.WebUtility.HtmlDecode(monoLabels[j].Groups[1].Value.Trim()) : filter;
                        // Strip "Live Stack · " prefix for shelf context
                        rawLabel = Regex.Replace(rawLabel, @"^Live Stack\s*\S\s*", "");
                        entries.Add(new LiveStackEntry {
                            target = targetName, filter = filter,
                            url = monoMatches[j].Groups[1].Value, // data URI as url
                            label = rawLabel,
                            isComposite = false
                        });
                    }

                    // Composites
                    var compMatches = compImgPattern.Matches(block);
                    var compLabels = compLabelPattern.Matches(block);
                    for (int j = 0; j < compMatches.Count; j++) {
                        var rawLabel = j < compLabels.Count ? System.Net.WebUtility.HtmlDecode(compLabels[j].Groups[1].Value.Trim()) : "Composite";
                        // Shorten "Live Stack Composite · R:5 G:3 B:5 · 1h 40m" → "RGB · R:5 G:3 B:5 · 1h 40m"
                        rawLabel = Regex.Replace(rawLabel, @"^Live Stack Composite\s*\S\s*", "RGB \u00b7 ");
                        rawLabel = Regex.Replace(rawLabel, @"^Live Stack\s*\S\s*", "");
                        entries.Add(new LiveStackEntry {
                            target = targetName, filter = "RGB",
                            url = compMatches[j].Groups[1].Value, // data URI as url
                            label = rawLabel,
                            isComposite = true
                        });
                    }

                    if (entries.Count > 0) result[targetName] = entries;
                }
                if (result.Count > 0)
                    log?.Debug($"Extracted {result.Values.Sum(l => l.Count)} livestack images from report HTML for {sessionId}");
            } catch (Exception ex) {
                _external.Warn($"NightSummary: Failed to extract livestack from report for {sessionId}: {ex.Message}");
            }
            return result;
        }

        private async Task WarmAltitudeChartCache(CancellationToken ct) {
            try {
                // Bulk-load all cached charts from DB into memory — fast path, no HTML parsing
                int dbLoaded = 0;
                try {
                    using (var conn = new SQLiteConnection($"Data Source={cachePath};Version=3;")) {
                        conn.Open();
                        using (var cmd = new SQLiteCommand("SELECT SessionId, ChartJson FROM AltitudeCharts", conn))
                        using (var reader = cmd.ExecuteReader()) {
                            while (reader.Read()) {
                                if (ct.IsCancellationRequested) break;
                                var sid = reader.GetString(0);
                                var json = reader.GetString(1);
                                try {
                                    altitudeChartCache[sid] = JsonSerializer.Deserialize<JsonElement>(json);
                                    dbLoaded++;
                                } catch { }
                            }
                        }
                    }
                } catch (Exception ex) {
                    log?.Warn($"Could not load altitude charts from DB cache: {ex.Message}");
                }
                log?.Info($"Loaded {dbLoaded} altitude charts from persistent DB cache.");

                // Parse HTML for any sessions not yet in cache (new or invalidated)
                if (!File.Exists(dbPath)) return;
                    var sessions = DbSessions();
                var toGenerate = sessions.Where(s =>
                    !altitudeChartCache.ContainsKey(s.SessionId) &&
                    File.Exists(Path.Combine(reportsDir, $"{s.SessionId}.html"))
                ).ToList();
                if (toGenerate.Count > 0) {
                    log?.Info($"Generating altitude charts for {toGenerate.Count} uncached sessions...");
                    foreach (var s in toGenerate) {
                        if (ct.IsCancellationRequested) break;
                        BuildAltitudeChartResult(s.SessionId);
                    }
                }
                log?.Info("Altitude chart cache warm-up complete.");
            } catch (Exception ex) {
                log?.Error("Altitude chart cache warm-up failed", ex);
            }
        }

        // ── Persistent altitude chart cache (nightsummary-dashboard-cache.sqlite) ──

        private void InitCacheDb() {
            try {
                using (var conn = new SQLiteConnection($"Data Source={cachePath};Version=3;")) {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(
                        "CREATE TABLE IF NOT EXISTS AltitudeCharts (SessionId TEXT PRIMARY KEY, ChartJson TEXT NOT NULL, GeneratedAt TEXT NOT NULL)",
                        conn))
                        cmd.ExecuteNonQuery();
                    // Generic key/value store for dashboard-side metadata (TS status overrides,
                    // manual target→TS links, etc.) Avoids touching SettingsManager for features
                    // that don't need XAML bindings.
                    using (var cmd = new SQLiteCommand(
                        "CREATE TABLE IF NOT EXISTS DashboardMetadata (Key TEXT PRIMARY KEY, Value TEXT NOT NULL, UpdatedAt TEXT NOT NULL)",
                        conn))
                        cmd.ExecuteNonQuery();
                }
            } catch (Exception ex) {
                log?.Warn($"Could not initialize dashboard cache DB: {ex.Message}");
            }
        }

        private string GetDashboardMeta(string key) {
            try {
                using (var conn = new SQLiteConnection($"Data Source={cachePath};Version=3;")) {
                    conn.Open();
                    using (var cmd = new SQLiteCommand("SELECT Value FROM DashboardMetadata WHERE Key = @k", conn)) {
                        cmd.Parameters.AddWithValue("@k", key);
                        return cmd.ExecuteScalar() as string;
                    }
                }
            } catch { return null; }
        }

        private void SetDashboardMeta(string key, string value) {
            try {
                using (var conn = new SQLiteConnection($"Data Source={cachePath};Version=3;")) {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(
                        "INSERT OR REPLACE INTO DashboardMetadata (Key, Value, UpdatedAt) VALUES (@k, @v, @ts)",
                        conn)) {
                        cmd.Parameters.AddWithValue("@k", key);
                        cmd.Parameters.AddWithValue("@v", value ?? "");
                        cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("o"));
                        cmd.ExecuteNonQuery();
                    }
                }
            } catch (Exception ex) {
                log?.Warn($"Could not write DashboardMetadata key '{key}': {ex.Message}");
            }
        }

        // Typed wrappers for the TS overrides + manual links JSON blobs.
        private const string TsStatusOverridesKey    = "ts.statusOverrides";
        private const string TsTargetLinksKey        = "ts.targetLinks";
        private const string TsProjectAssignmentsKey = "ts.projectAssignments";
        private const string TsTargetExclusionsKey   = "ts.targetExclusions";
        private const string TsCustomProjectsKey     = "ts.customProjects";

        private record CustomProjectRecord(string Guid, string Name);

        private List<CustomProjectRecord> GetCustomProjects() {
            var raw = GetDashboardMeta(TsCustomProjectsKey);
            if (string.IsNullOrEmpty(raw)) return new List<CustomProjectRecord>();
            try { return JsonSerializer.Deserialize<List<CustomProjectRecord>>(raw) ?? new List<CustomProjectRecord>(); }
            catch { return new List<CustomProjectRecord>(); }
        }

        private void SaveCustomProjects(List<CustomProjectRecord> projects) {
            SetDashboardMeta(TsCustomProjectsKey, JsonSerializer.Serialize(projects));
        }

        private Dictionary<string, string> GetTsStatusOverrides() {
            var raw = GetDashboardMeta(TsStatusOverridesKey);
            if (string.IsNullOrEmpty(raw)) return new Dictionary<string, string>();
            try {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(raw) ?? new Dictionary<string, string>();
            } catch { return new Dictionary<string, string>(); }
        }

        private void SetTsStatusOverride(string projectGuid, string statusOrNull) {
            if (string.IsNullOrEmpty(projectGuid)) return;
            var map = GetTsStatusOverrides();
            if (string.IsNullOrEmpty(statusOrNull)) map.Remove(projectGuid);
            else map[projectGuid] = statusOrNull;
            SetDashboardMeta(TsStatusOverridesKey, JsonSerializer.Serialize(map));
        }

        private Dictionary<string, string> GetTsTargetLinks() {
            var raw = GetDashboardMeta(TsTargetLinksKey);
            if (string.IsNullOrEmpty(raw)) return new Dictionary<string, string>();
            try {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(raw) ?? new Dictionary<string, string>();
            } catch { return new Dictionary<string, string>(); }
        }

        private void SetTsTargetLink(string sessionTargetNameLower, string tsTargetGuidOrNull) {
            if (string.IsNullOrEmpty(sessionTargetNameLower)) return;
            var map = GetTsTargetLinks();
            if (string.IsNullOrEmpty(tsTargetGuidOrNull)) map.Remove(sessionTargetNameLower);
            else map[sessionTargetNameLower] = tsTargetGuidOrNull;
            SetDashboardMeta(TsTargetLinksKey, JsonSerializer.Serialize(map));
        }

        private Dictionary<string, List<string>> GetProjectAssignments() {
            var raw = GetDashboardMeta(TsProjectAssignmentsKey);
            if (string.IsNullOrEmpty(raw)) return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try {
                // Try new array format first
                var result = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(raw);
                if (result != null) return new Dictionary<string, List<string>>(result, StringComparer.OrdinalIgnoreCase);
            } catch {
                // Fall back to old string format and normalize
                try {
                    var old = JsonSerializer.Deserialize<Dictionary<string, string>>(raw);
                    if (old != null) {
                        var normalized = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kv in old) {
                            if (!string.IsNullOrEmpty(kv.Value))
                                normalized[kv.Key] = new List<string> { kv.Value };
                        }
                        return normalized;
                    }
                } catch { }
            }
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }

        private void SetProjectAssignment(string targetNameLower, string projectGuidOrEmpty) {
            if (string.IsNullOrEmpty(targetNameLower)) return;
            var map = GetProjectAssignments();
            if (string.IsNullOrEmpty(projectGuidOrEmpty)) {
                // Clear all assignments
                map.Remove(targetNameLower);
            } else if (map.TryGetValue(targetNameLower, out var existing) && existing.Contains(projectGuidOrEmpty)) {
                // Toggle off: remove this GUID
                existing.Remove(projectGuidOrEmpty);
                if (existing.Count == 0) map.Remove(targetNameLower);
            } else {
                // Toggle on: add this GUID
                if (!map.ContainsKey(targetNameLower)) map[targetNameLower] = new List<string>();
                map[targetNameLower].Add(projectGuidOrEmpty);
            }
            SetDashboardMeta(TsProjectAssignmentsKey, JsonSerializer.Serialize(map));
        }

        private Dictionary<string, List<string>> GetTargetExclusions() {
            var raw = GetDashboardMeta(TsTargetExclusionsKey);
            if (string.IsNullOrEmpty(raw)) return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try {
                return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(raw)
                    ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            } catch { return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase); }
        }

        private void SetTargetExclusion(string projectGuid, string targetNameLower, bool exclude) {
            if (string.IsNullOrEmpty(projectGuid) || string.IsNullOrEmpty(targetNameLower)) return;
            var map = GetTargetExclusions();
            if (!map.ContainsKey(projectGuid)) map[projectGuid] = new List<string>();
            if (exclude) {
                if (!map[projectGuid].Contains(targetNameLower, StringComparer.OrdinalIgnoreCase))
                    map[projectGuid].Add(targetNameLower);
            } else {
                map[projectGuid].RemoveAll(x => string.Equals(x, targetNameLower, StringComparison.OrdinalIgnoreCase));
                if (map[projectGuid].Count == 0) map.Remove(projectGuid);
            }
            SetDashboardMeta(TsTargetExclusionsKey, JsonSerializer.Serialize(map));
        }

        private string GetCachedChartJson(string sessionId) {
            try {
                using (var conn = new SQLiteConnection($"Data Source={cachePath};Version=3;")) {
                    conn.Open();
                    using (var cmd = new SQLiteCommand("SELECT ChartJson FROM AltitudeCharts WHERE SessionId = @id", conn)) {
                        cmd.Parameters.AddWithValue("@id", sessionId);
                        return cmd.ExecuteScalar() as string;
                    }
                }
            } catch { return null; }
        }

        private void SetCachedChartJson(string sessionId, string json) {
            try {
                using (var conn = new SQLiteConnection($"Data Source={cachePath};Version=3;")) {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(
                        "INSERT OR REPLACE INTO AltitudeCharts (SessionId, ChartJson, GeneratedAt) VALUES (@id, @json, @ts)",
                        conn)) {
                        cmd.Parameters.AddWithValue("@id", sessionId);
                        cmd.Parameters.AddWithValue("@json", json);
                        cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("o"));
                        cmd.ExecuteNonQuery();
                    }
                }
            } catch { }
        }

        private void DeleteCachedChartJson(string sessionId) {
            try {
                using (var conn = new SQLiteConnection($"Data Source={cachePath};Version=3;")) {
                    conn.Open();
                    using (var cmd = new SQLiteCommand("DELETE FROM AltitudeCharts WHERE SessionId = @id", conn)) {
                        cmd.Parameters.AddWithValue("@id", sessionId);
                        cmd.ExecuteNonQuery();
                    }
                }
            } catch { }
        }

        private async Task HandleGetAltitudeChart(TcpHttpResponse res, string sessionId, Action<int, string> done) {
            if (altitudeChartCache.TryGetValue(sessionId, out var cached)) {
                await WriteJson(res, 200, cached);
                done?.Invoke(200, $"{sessionId} — altitude chart (cached)");
                return;
            }

            var result = BuildAltitudeChartResult(sessionId);
            await WriteJson(res, 200, result);
            done?.Invoke(200, $"{sessionId} — altitude chart built");
        }

        private object BuildAltitudeChartResult(string sessionId) {
            // Check persistent DB cache before doing any HTML parsing
            var cachedJson = GetCachedChartJson(sessionId);
            if (cachedJson != null) {
                try {
                    var cached = JsonSerializer.Deserialize<JsonElement>(cachedJson);
                    altitudeChartCache[sessionId] = cached;
                    return cached;
                } catch { }
            }

            var reportPath = Path.Combine(reportsDir, $"{sessionId}.html");
            if (!File.Exists(reportPath)) {
                var empty = new { svg = "", legend = Array.Empty<object>() };
                altitudeChartCache[sessionId] = empty;
                // Don't persist empty results — regenerate when report appears
                return empty;
            }

            var html = File.ReadAllText(reportPath);

            // Find all altitude chart SVGs and their associated target names
            var sections = html.Split(new[] { "<div class='target-section'>" }, StringSplitOptions.None);
            var h3Pattern = new Regex(@"<h3>([^<]+)");
            var svgPattern = new Regex(@"<svg class='altitude-chart'.*?</svg>", RegexOptions.Singleline);
            var polylinePattern = new Regex(@"<polyline points='([^']+)' fill='none' stroke='#7eb8f7' stroke-width='2'/>"); // target curve (dark mode)
            var polylinePatternLight = new Regex(@"<polyline points='([^']+)' fill='none' stroke='#2563b8' stroke-width='2'/>"); // light mode
            var sessionRectPattern = new Regex(@"<rect x='([\d.]+)' y='\d+' width='([\d.]+)' height='\d+' fill='#7eb8f7' opacity='0\.07'/>");
            var sessionRectPatternLight = new Regex(@"<rect x='([\d.]+)' y='\d+' width='([\d.]+)' height='\d+' fill='#2563b8' opacity='0\.07'/>");

            // Extract per-target data
            var targetData = new List<(string Name, string Points, double SessX, double SessW)>();
            string scaffoldSvg = null; // first chart's full SVG for structural elements

            for (int i = 1; i < sections.Length; i++) {
                var block = sections[i];
                var h3Match = h3Pattern.Match(block);
                var svgMatch = svgPattern.Match(block);
                if (!h3Match.Success || !svgMatch.Success) continue;

                var targetName = h3Match.Groups[1].Value.Trim();
                var svgContent = svgMatch.Value;

                if (scaffoldSvg == null) scaffoldSvg = svgContent;

                // Extract the target altitude polyline
                var polyMatch = polylinePattern.Match(svgContent);
                if (!polyMatch.Success) polyMatch = polylinePatternLight.Match(svgContent);
                if (!polyMatch.Success) continue;

                // Extract session window rect position
                double sessX = 0, sessW = 0;
                var rectMatch = sessionRectPattern.Match(svgContent);
                if (!rectMatch.Success) rectMatch = sessionRectPatternLight.Match(svgContent);
                if (rectMatch.Success) {
                    sessX = double.Parse(rectMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    sessW = double.Parse(rectMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                }

                targetData.Add((targetName, polyMatch.Groups[1].Value, sessX, sessW));
            }

            if (targetData.Count == 0 || scaffoldSvg == null) {
                var noCharts = new { svg = "", legend = Array.Empty<object>() };
                altitudeChartCache[sessionId] = noCharts;
                // Persist so we don't re-parse this report on every restart
                try { SetCachedChartJson(sessionId, JsonSerializer.Serialize(noCharts, JsonOpts)); } catch { }
                return noCharts;
            }

            // Normalize light-mode colors to dark-mode for consistent dashboard rendering
            scaffoldSvg = scaffoldSvg
                .Replace("#e8eef5", "#0d1117")  // chart background
                .Replace("#c0c8d4", "#2d2d5e")  // border/grid
                .Replace("fill='#666'", "fill='#888'")  // muted text
                .Replace("stroke='#2563b8'", "stroke='#7eb8f7'")  // accent (for moon/other)
                .Replace("fill='#2563b8'", "fill='#7eb8f7'")      // accent fills
                .Replace("#7a8a9e", "#c0c0c0")  // moon stroke
                .Replace("#c07a00", "#f59e0b")  // sunrise
                .Replace("opacity='0.75'", "opacity='0.45'");  // moon opacity

            // Extract shared structural elements from the first chart
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            // Trim vertical padding: original is 0-248, content lives at ~10-242
            const int vbTopTrim = 14;  // trim from top (room for 90° label)
            const int vbBotTrim = 2;   // trim from bottom (tight to time labels)
            var viewBoxMatch = Regex.Match(scaffoldSvg, @"viewBox='[\d.]+ [\d.]+ [\d.]+ ([\d.]+)'");
            int origH = viewBoxMatch.Success ? (int)double.Parse(viewBoxMatch.Groups[1].Value, inv) : 248;
            var viewBoxY = vbTopTrim.ToString();
            var viewBoxH = (origH - vbTopTrim - vbBotTrim).ToString();

            var moonPattern = new Regex(@"<g><title>Moon Position</title>.*?</g>", RegexOptions.Singleline);
            var timeLabelPattern = new Regex(@"<text[^>]*fill='#888'[^>]*>\d{2}:\d{2}</text>");

            // Build SVG — no legend (rendered as HTML overlay), no sunset/sunrise text
            var sb = new StringBuilder();
            sb.AppendLine($"<svg viewBox='0 {viewBoxY} {AltNewSvgW.ToString("F0", inv)} {viewBoxH}' xmlns='http://www.w3.org/2000/svg' preserveAspectRatio='none'>");

            // Background + border rects (scale x and width to fill wider plot area)
            var bgRects = Regex.Matches(scaffoldSvg, @"<rect x='38'[^/]*/>");
            foreach (Match r in bgRects) {
                var rect = Regex.Replace(r.Value, @"width='([\d.]+)'", m => {
                    if (double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, inv, out double w))
                        return $"width='{(w * AltScaleX).ToString("F1", inv)}'";
                    return m.Value;
                });
                sb.AppendLine(rect);
            }

            // Per-target imaging window shading with border lines (scaled coordinates)
            for (int t = 0; t < targetData.Count; t++) {
                var td = targetData[t];
                if (td.SessW > 0) {
                    var color = TargetColors[t % TargetColors.Length];
                    var sx = MapX(td.SessX).ToString("F1", inv);
                    var sw = (td.SessW * AltScaleX).ToString("F1", inv);
                    sb.AppendLine($"<rect x='{sx}' y='20' width='{sw}' height='200' fill='{color}' opacity='0.15'/>");
                    var endX = MapX(td.SessX + td.SessW).ToString("F1", inv);
                    sb.AppendLine($"<line x1='{sx}' y1='20' x2='{sx}' y2='220' stroke='{color}' stroke-width='1' opacity='0.6'/>");
                    sb.AppendLine($"<line x1='{endX}' y1='20' x2='{endX}' y2='220' stroke='{color}' stroke-width='1' opacity='0.6'/>");
                }
            }

            // Grid lines at 30 and 60 degrees (scale x2 endpoint, exclude min altitude lines)
            var gridLines = Regex.Matches(scaffoldSvg, @"<line x1='38'[^/]*/>");
            foreach (Match g in gridLines) {
                if (g.Value.Contains("#cc4444")) continue;
                var line = Regex.Replace(g.Value, @"x2='([\d.]+)'", m => {
                    if (double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, inv, out double x))
                        return $"x2='{MapX(x).ToString("F1", inv)}'";
                    return m.Value;
                });
                sb.AppendLine(line);
            }

            // Altitude axis labels (90, 60, 30, 0) — keep at original x positions
            var axisLabels = Regex.Matches(scaffoldSvg, @"<text x='34'[^>]*>[^<]*</text>");
            foreach (Match a in axisLabels) sb.AppendLine(a.Value);

            // Per-target altitude curves with distinct colors (scale polyline x-coordinates)
            for (int t = 0; t < targetData.Count; t++) {
                var td = targetData[t];
                var color = TargetColors[t % TargetColors.Length];
                var scaledPoints = ScalePolylineX(td.Points);
                sb.AppendLine($"<g><title>{td.Name}</title>");
                sb.AppendLine($"<polyline points='{scaledPoints}' fill='none' stroke='transparent' stroke-width='10'/>");
                sb.AppendLine($"<polyline points='{scaledPoints}' fill='none' stroke='{color}' stroke-width='2'/>");
                sb.AppendLine("</g>");
            }

            // Moon curve (scale polyline x-coordinates within the group)
            var moonMatch = moonPattern.Match(scaffoldSvg);
            if (moonMatch.Success) {
                var moonSvg = Regex.Replace(moonMatch.Value, @"points='([^']+)'", m => {
                    return $"points='{ScalePolylineX(m.Groups[1].Value)}'";
                });
                sb.AppendLine(moonSvg);
            }

            // Sunset/sunrise labels — omitted from dashboard chart (dropped to allow preserveAspectRatio=none)

            // Time axis labels (scale x positions)
            foreach (Match t in timeLabelPattern.Matches(scaffoldSvg)) sb.AppendLine(RemapSvgX(t.Value));

            sb.AppendLine("</svg>");

            // Legend data for HTML overlay (rendered client-side)
            var legend = targetData.Select((td, i) => new {
                name = td.Name,
                color = TargetColors[i % TargetColors.Length]
            }).ToList();

            var svgResult = sb.ToString();
            var result = new { svg = svgResult, legend };
            altitudeChartCache[sessionId] = result;
            // Persist to DB so subsequent server restarts skip HTML parsing
            try { SetCachedChartJson(sessionId, JsonSerializer.Serialize(result, JsonOpts)); } catch { }
            return result;
        }

        private async Task HandleGetTargetStats(TcpHttpResponse res, Action<int, string> done) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, new { targets = Array.Empty<object>(), tsStatus = "not_installed" });
                done?.Invoke(200, "0 targets (no db)");
                return;
            }

            var details = DbTargetDetails();

            // ── Phase 3a: Target Scheduler enrichment ──
            // Load TS projects via direct SQLite. If TS isn't installed or the read fails,
            // we return the target data unchanged with tsStatus = "not_installed" | "error".
            string tsStatus = "available";
            string tsError  = null;
            List<TsProjectInfo> tsProjects = null;
            if (!TsAvailable()) {
                tsStatus = "not_installed";
                log?.Info("TS not available — target stats returned without TS enrichment");
            } else {
                try {
                    tsProjects = TsProjects();
                    log?.Debug($"TS loaded {tsProjects?.Count ?? 0} project(s)");
                } catch (Exception ex) {
                    tsStatus = "error";
                    tsError  = ex.Message;
                    _external.Error($"NightSummary: TS GetAllProjects threw. {ex.Message}");
                    log?.Error($"TS GetAllProjects failed: {ex.Message}");
                }
            }

            // Merge in NS custom projects (not from TS DB)
            var customProjectsList = GetCustomProjects();
            var customGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (tsStatus == "available") {
                foreach (var cp in customProjectsList) {
                    if (string.IsNullOrEmpty(cp.Guid)) continue;
                    if (tsProjects == null) tsProjects = new List<TsProjectInfo>();
                    tsProjects.Add(new TsProjectInfo { Guid = cp.Guid, Name = cp.Name, State = "Active", IsMosaic = false });
                    customGuids.Add(cp.Guid);
                }
            }

            // Build a case-insensitive lookup from target name → (project, target)
            // Also index targets by Guid for manual-link resolution.
            var tsTargetByNameLower = new Dictionary<string, (TsProjectInfo project, TsProjectTarget target)>(StringComparer.OrdinalIgnoreCase);
            var tsTargetByGuid      = new Dictionary<string, (TsProjectInfo project, TsProjectTarget target)>(StringComparer.OrdinalIgnoreCase);
            if (tsProjects != null) {
                foreach (var proj in tsProjects) {
                    foreach (var tgt in proj.Targets) {
                        // On name collisions (same target in multiple TS projects), prefer the
                        // entry with more exposure plans so we don't silently pick a project
                        // that has the target defined but no plans yet (which would produce
                        // goals=[] in the TDP). Manual link can always override.
                        if (!string.IsNullOrEmpty(tgt.Name)) {
                            if (!tsTargetByNameLower.ContainsKey(tgt.Name) ||
                                tgt.ExposurePlans.Count > tsTargetByNameLower[tgt.Name].target.ExposurePlans.Count) {
                                tsTargetByNameLower[tgt.Name] = (proj, tgt);
                            }
                        }
                        if (!string.IsNullOrEmpty(tgt.Guid)) {
                            tsTargetByGuid[tgt.Guid] = (proj, tgt);
                        }
                    }
                }
            }

            // Load overrides + manual links from the dashboard cache DB
            var statusOverrides = GetTsStatusOverrides();
            var manualLinks     = GetTsTargetLinks();

            // Cross-project assignments (target name → list of assigned project guids)
            var projectAssignmentsMap = GetProjectAssignments();

            // Index TS projects by guid for assignment lookups
            var tsProjectByGuid = new Dictionary<string, TsProjectInfo>(StringComparer.OrdinalIgnoreCase);
            if (tsProjects != null) {
                foreach (var p in tsProjects) {
                    if (!string.IsNullOrEmpty(p.Guid)) tsProjectByGuid[p.Guid] = p;
                }
            }

            // Build a summary project object for an assigned project GUID
            // (mirrors _build_assigned_project_obj in the Python dev server).
            object BuildAssignedProjectObj(string pguid) {
                if (string.IsNullOrEmpty(pguid)) return null;
                if (!tsProjectByGuid.TryGetValue(pguid, out var aproj)) return null;
                var rawState = aproj.State ?? "Active";
                string effState;
                string effSrc;
                if (statusOverrides.TryGetValue(aproj.Guid ?? "", out var ov) && !string.IsNullOrEmpty(ov)) {
                    effState = ov;
                    effSrc   = "override";
                } else {
                    effState = rawState;
                    effSrc   = "raw";
                }
                int assignedCount = 0;
                foreach (var kv in projectAssignmentsMap) {
                    if (kv.Value != null && kv.Value.Any(g => string.Equals(g, pguid, StringComparison.OrdinalIgnoreCase))) {
                        assignedCount++;
                    }
                }
                return new {
                    id              = aproj.Id,
                    guid            = aproj.Guid,
                    profileId       = aproj.ProfileId,
                    name            = aproj.Name,
                    description     = aproj.Description,
                    rawState        = rawState,
                    state           = effState,
                    stateSource     = effSrc,
                    priority        = aproj.Priority,
                    isMosaic        = aproj.IsMosaic,
                    createDate      = aproj.CreateDate?.ToString("o"),
                    activeDate      = aproj.ActiveDate?.ToString("o"),
                    inactiveDate    = aproj.InactiveDate?.ToString("o"),
                    minimumAltitude = aproj.MinimumAltitude,
                    maximumAltitude = aproj.MaximumAltitude,
                    targetCount     = aproj.Targets.Count + assignedCount,
                    percentComplete = (double?)null,
                    isCustom        = customGuids.Contains(aproj.Guid ?? ""),
                };
            }

            // Pre-compute NS target count per custom project from assignments
            var customProjectTargetCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in projectAssignmentsMap) {
                if (kv.Value == null) continue;
                foreach (var g in kv.Value)
                    if (customGuids.Contains(g))
                        customProjectTargetCounts[g] = (customProjectTargetCounts.ContainsKey(g) ? customProjectTargetCounts[g] : 0) + 1;
            }

            var result = details.Select(t => {
                (TsProjectInfo project, TsProjectTarget target)? ts = null;
                string matchedBy = null;

                // 1. Manual link (session target name → ts target guid) wins over auto-match
                if (tsProjects != null && manualLinks.TryGetValue((t.TargetName ?? "").ToLowerInvariant(), out var linkedGuid)
                    && tsTargetByGuid.TryGetValue(linkedGuid, out var linked)) {
                    ts = linked;
                    matchedBy = "manual";
                }
                // 2. Auto-match on case-insensitive target name
                else if (tsProjects != null && !string.IsNullOrEmpty(t.TargetName)
                    && tsTargetByNameLower.TryGetValue(t.TargetName, out var auto)) {
                    ts = auto;
                    matchedBy = "name";
                }

                string customPrimaryGuid = null;
                object tsObj = null;
                if (ts.HasValue) {
                    var proj = ts.Value.project;
                    var tgt  = ts.Value.target;

                    // Per-filter goals + progress
                    // effective = accepted when grading is active; falls back to acquired
                    // when grading is pending or disabled (accepted=0 but acquired>0).
                    int totalDesired   = 0;
                    int totalEffective = 0;
                    var goals = tgt.ExposurePlans.Select(ep => {
                        int effective = ep.Accepted > 0 ? ep.Accepted : ep.Acquired;
                        totalDesired   += ep.Desired;
                        totalEffective += effective;
                        return new {
                            filter       = ep.Filter,
                            templateName = ep.TemplateName,
                            exposureSec  = ep.ExposureSec,
                            desired      = ep.Desired,
                            acquired     = ep.Acquired,
                            accepted     = ep.Accepted,
                            effective    = effective,
                            percentComplete = ep.Desired > 0
                                ? Math.Round(Math.Min(100.0, (effective * 100.0) / ep.Desired), 1)
                                : (double?)null,
                        };
                    }).ToList();
                    double? projectPercent = totalDesired > 0
                        ? Math.Round(Math.Min(100.0, (totalEffective * 100.0) / totalDesired), 1)
                        : (double?)null;

                    // Effective state = override > inferred Completed > raw state
                    // "Completed" inferred when state == Closed and all filters with Desired>0 are fully accepted.
                    string effectiveState;
                    string effectiveStateSource;
                    if (statusOverrides.TryGetValue(proj.Guid ?? "", out var overrideState) && !string.IsNullOrEmpty(overrideState)) {
                        effectiveState = overrideState;
                        effectiveStateSource = "override";
                    } else if (proj.State == "Closed" && projectPercent.HasValue && projectPercent.Value >= 100.0) {
                        effectiveState = "Completed";
                        effectiveStateSource = "inferred";
                    } else {
                        effectiveState = proj.State;
                        effectiveStateSource = "raw";
                    }

                    tsObj = new {
                        project = new {
                            id              = proj.Id,
                            guid            = proj.Guid,
                            profileId       = proj.ProfileId,
                            name            = proj.Name,
                            description     = proj.Description,
                            rawState        = proj.State,
                            state           = effectiveState,
                            stateSource     = effectiveStateSource,
                            priority        = proj.Priority,
                            isMosaic        = proj.IsMosaic,
                            createDate      = proj.CreateDate?.ToString("o"),
                            activeDate      = proj.ActiveDate?.ToString("o"),
                            inactiveDate    = proj.InactiveDate?.ToString("o"),
                            minimumAltitude = proj.MinimumAltitude,
                            maximumAltitude = proj.MaximumAltitude,
                            targetCount     = proj.Targets.Count,
                            percentComplete = projectPercent,
                        },
                        target = new {
                            id       = tgt.Id,
                            guid     = tgt.Guid,
                            name     = tgt.Name,
                            active   = tgt.Active,
                            ra       = tgt.RA,
                            dec      = tgt.Dec,
                            rotation = tgt.Rotation,
                        },
                        goals,
                        matchedBy,
                    };
                } else if (projectAssignmentsMap.TryGetValue((t.TargetName ?? "").ToLowerInvariant(), out var customAssignGuids) && customAssignGuids != null) {
                    var cg = customAssignGuids.FirstOrDefault(g => customGuids.Contains(g));
                    if (cg != null && tsProjectByGuid.TryGetValue(cg, out var cp)) {
                        customPrimaryGuid = cp.Guid;
                        tsObj = new {
                            project = new {
                                id              = 0,
                                guid            = cp.Guid,
                                profileId       = (string)null,
                                name            = cp.Name,
                                description     = (string)null,
                                rawState        = "Active",
                                state           = "Active",
                                stateSource     = "raw",
                                priority        = (string)null,
                                isMosaic        = false,
                                createDate      = (string)null,
                                activeDate      = (string)null,
                                inactiveDate    = (string)null,
                                minimumAltitude = 0.0,
                                maximumAltitude = 0.0,
                                targetCount     = customProjectTargetCounts.ContainsKey(cp.Guid) ? customProjectTargetCounts[cp.Guid] : 0,
                                percentComplete = (double?)null,
                                isCustom        = true,
                            },
                            target    = (object)null,
                            goals     = (object)null,
                            matchedBy = "assigned",
                        };
                    }
                }

                // Build additionalProjects from cross-assignments (guids beyond primary)
                List<object> additionalProjects = null;
                if (projectAssignmentsMap.TryGetValue((t.TargetName ?? "").ToLowerInvariant(), out var assignedGuids)
                    && assignedGuids != null && assignedGuids.Count > 0) {
                    string primaryGuid = ts.HasValue ? ts.Value.project.Guid : customPrimaryGuid;
                    foreach (var aguid in assignedGuids) {
                        if (!string.IsNullOrEmpty(primaryGuid) && string.Equals(aguid, primaryGuid, StringComparison.OrdinalIgnoreCase))
                            continue;
                        var apObj = BuildAssignedProjectObj(aguid);
                        if (apObj != null) {
                            if (additionalProjects == null) additionalProjects = new List<object>();
                            additionalProjects.Add(apObj);
                        }
                    }
                }

                return new {
                    target = t.TargetName,
                    totalIntegrationSeconds = t.TotalIntegrationSeconds,
                    totalIntegrationHours = Math.Round(t.TotalIntegrationSeconds / 3600.0, 2),
                    sessionCount = t.SessionCount,
                    lastImaged = t.LastSessionStart > DateTime.MinValue ? t.LastSessionStart.ToString("o") : null,
                    latestSessionId = t.LatestSessionId,
                    totalFrames = t.TotalFrames,
                    acceptedFrames = t.AcceptedFrames,
                    avgHFR = t.AvgHFR > 0 ? (double?)t.AvgHFR : null,
                    avgFWHM = t.AvgFWHM > 0 ? (double?)t.AvgFWHM : null,
                    avgGuidingRMS = t.AvgGuidingRMS > 0 ? (double?)t.AvgGuidingRMS : null,
                    raHours = t.RaHours != 0 ? (double?)t.RaHours : null,
                    decDegrees = t.DecDegrees != 0 ? (double?)t.DecDegrees : null,
                    filters = t.Filters.Select(f => new {
                        filter = f.Filter,
                        totalSeconds = f.TotalSeconds,
                        totalHours = Math.Round(f.TotalSeconds / 3600.0, 2),
                        frameCount = f.FrameCount,
                        acceptedCount = f.AcceptedCount
                    }),
                    ts = tsObj,
                    additionalProjects = additionalProjects,
                };
            }).ToList();

            // Summary of TS projects for the manual-link picker and any global UI
            object tsProjectsSummary = null;
            if (tsProjects != null) {
                tsProjectsSummary = tsProjects.Select(p => new {
                    guid        = p.Guid,
                    name        = p.Name,
                    state       = statusOverrides.TryGetValue(p.Guid ?? "", out var ovState) && !string.IsNullOrEmpty(ovState) ? ovState : p.State,
                    isMosaic    = p.IsMosaic,
                    isCustom    = customGuids.Contains(p.Guid ?? ""),
                    targetCount = customGuids.Contains(p.Guid ?? "")
                        ? (customProjectTargetCounts.ContainsKey(p.Guid ?? "") ? customProjectTargetCounts[p.Guid] : 0)
                        : p.Targets.Count,
                    targets = p.Targets.Select(tt => new {
                        guid = tt.Guid,
                        name = tt.Name,
                    }),
                }).ToList();
            }

            await WriteJson(res, 200, new {
                targets = result,
                tsStatus,
                tsError,
                tsProjects = tsProjectsSummary,
                projectAssignments = projectAssignmentsMap,
                targetExclusions   = GetTargetExclusions(),
            });
            if (result.Count == 0) log?.Warn($"Target stats returned 0 targets (tsStatus: {tsStatus})");
            done?.Invoke(200, $"{result.Count} targets (ts: {tsStatus})");
        }

        private async Task HandleGetTargetSessions(TcpHttpResponse res, string targetName, Action<int, string> done) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, new { target = targetName, sessions = Array.Empty<object>() });
                done?.Invoke(200, "0 sessions (no db)");
                return;
            }

            var sessions = DbSessionsForTarget(targetName);

            // Parse moon phase from each session's report HTML (same pattern as HandleGetSessions)
            var moonBySessionId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in sessions) {
                var reportPath = Path.Combine(reportsDir, $"{s.SessionId}.html");
                if (!File.Exists(reportPath)) continue;
                try {
                    var html = File.ReadAllText(reportPath);
                    var moonMatch = Regex.Match(html, @"<div class='stat-value'>(\d+%\s*[^\<]*)</div>\s*<div class='stat-label'>Moon</div>");
                    if (moonMatch.Success) {
                        moonBySessionId[s.SessionId] = Regex.Replace(
                            WebUtility.HtmlDecode(moonMatch.Groups[1].Value), @"\s+", " ").Trim();
                    }
                } catch { }
            }

            // Aggregate totals across all sessions
            var totalSeconds = sessions.Sum(s => s.IntegrationSeconds);
            var totalFrames  = sessions.Sum(s => s.AcceptedFrames);
            var firstSession = sessions.Count > 0 ? sessions.Min(s => s.SessionStart) : DateTime.MinValue;
            var lastSession  = sessions.Count > 0 ? sessions.Max(s => s.SessionStart) : DateTime.MinValue;
            var avgHFR       = sessions.Where(s => s.AvgHFR > 0).Select(s => s.AvgHFR).DefaultIfEmpty(0).Average();
            var avgGuide     = sessions.Where(s => s.AvgGuidingRMS > 0).Select(s => s.AvgGuidingRMS).DefaultIfEmpty(0).Average();

            var result = new {
                target           = targetName,
                totalIntegrationHours = Math.Round(totalSeconds / 3600.0, 2),
                totalFrames,
                sessionCount     = sessions.Count,
                firstSession     = firstSession > DateTime.MinValue ? firstSession.ToString("o") : null,
                lastSession      = lastSession  > DateTime.MinValue ? lastSession.ToString("o")  : null,
                avgHFR           = avgHFR   > 0 ? (double?)Math.Round(avgHFR,   2) : null,
                avgGuidingRMS    = avgGuide > 0 ? (double?)Math.Round(avgGuide, 2) : null,
                sessions = sessions.Select(s => new {
                    sessionId             = s.SessionId,
                    sessionStart          = s.SessionStart.ToString("o"),
                    sessionEnd            = s.SessionEnd.ToString("o"),
                    durationMinutes       = (int)Math.Round((s.SessionEnd - s.SessionStart).TotalMinutes),
                    integrationHours      = Math.Round(s.IntegrationSeconds / 3600.0, 2),
                    integrationSeconds    = s.IntegrationSeconds,
                    frames                = s.AcceptedFrames,
                    totalFrames           = s.FrameCount,
                    avgHFR                = s.AvgHFR        > 0 ? (double?)s.AvgHFR        : null,
                    avgGuidingRMS         = s.AvgGuidingRMS > 0 ? (double?)s.AvgGuidingRMS : null,
                    moonPhase             = moonBySessionId.TryGetValue(s.SessionId, out var m) ? m : null,
                    filters = s.Filters.Select(f => new {
                        filter             = f.Filter,
                        integrationSeconds = f.IntegrationSeconds,
                        integrationHours   = Math.Round(f.IntegrationSeconds / 3600.0, 2),
                        frames             = f.AcceptedFrames,
                        totalFrames        = f.FrameCount,
                        avgHFR             = f.AvgHFR        > 0 ? (double?)f.AvgHFR        : null,
                        avgGuidingRMS      = f.AvgGuidingRMS > 0 ? (double?)f.AvgGuidingRMS : null,
                    })
                })
            };

            await WriteJson(res, 200, result);
            done?.Invoke(200, $"{sessions.Count} sessions for '{targetName}'");
        }

        // ── Project detail (Phase 3c) ─────────────────────────────────────────

        private async Task HandleGetProjectStats(TcpHttpResponse res, string projectGuid, Action<int, string> done) {
            if (string.IsNullOrEmpty(projectGuid)) {
                await WriteJson(res, 400, new { error = "Missing project guid" });
                done?.Invoke(400, null);
                return;
            }

            // TS must be available
            if (!TsAvailable()) {
                await WriteJson(res, 404, new { error = "Target Scheduler not available" });
                done?.Invoke(404, null);
                return;
            }

            List<TsProjectInfo> allProjects;
            try {
                allProjects = TsProjects();
            } catch (Exception ex) {
                await WriteJson(res, 500, new { error = "Internal server error" });
                done?.Invoke(500, ex.Message);
                return;
            }
            foreach (var cp in GetCustomProjects())
                if (!string.IsNullOrEmpty(cp.Guid) && !allProjects.Any(p => string.Equals(p.Guid, cp.Guid, StringComparison.OrdinalIgnoreCase)))
                    allProjects.Add(new TsProjectInfo { Guid = cp.Guid, Name = cp.Name, State = "Active", IsMosaic = false });

            var proj = allProjects.FirstOrDefault(p =>
                string.Equals(p.Guid, projectGuid, StringComparison.OrdinalIgnoreCase));
            if (proj == null) {
                await WriteJson(res, 404, new { error = $"Project '{projectGuid}' not found" });
                done?.Invoke(404, null);
                return;
            }

            // Load NS sessions for camera + FOV data
            SessionRecord[] allSessions = Array.Empty<SessionRecord>();
            Dictionary<string, double?> latestPaByTarget = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(dbPath)) {
                    allSessions = DbSessions().ToArray();

                // Query the most recent plate-solve PositionAngle per target name
                using (var conn = new System.Data.SQLite.SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    conn.Open();
                    string paSql = @"
                        SELECT TargetName, PositionAngle
                        FROM Images
                        WHERE TargetName IS NOT NULL AND TargetName != ''
                          AND PositionAngle IS NOT NULL
                        ORDER BY TargetName, Timestamp DESC";
                    using (var cmd = new System.Data.SQLite.SQLiteCommand(paSql, conn))
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            var tname = reader["TargetName"].ToString();
                            if (!latestPaByTarget.ContainsKey(tname)) {
                                latestPaByTarget[tname] = Convert.ToDouble(reader["PositionAngle"]);
                            }
                        }
                    }
                }
            }

            // Session map by SessionId for quick camera-field lookup
            var sessionById = allSessions.ToDictionary(s => s.SessionId, StringComparer.OrdinalIgnoreCase);

            // Status override for this project
            var statusOverrides = GetTsStatusOverrides();
            string effectiveState;
            if (statusOverrides.TryGetValue(proj.Guid ?? "", out var ov) && !string.IsNullOrEmpty(ov)) {
                effectiveState = ov;
            } else {
                // Check completion to infer "Completed" from "Closed"
                int totalDesired = proj.Targets.SelectMany(t => t.ExposurePlans).Sum(ep => ep.Desired);
                int totalAccepted = proj.Targets.SelectMany(t => t.ExposurePlans).Sum(ep => ep.Accepted);
                if (proj.State == "Closed" && totalDesired > 0 &&
                    totalAccepted >= totalDesired) {
                    effectiveState = "Completed";
                } else {
                    effectiveState = proj.State;
                }
            }

            // Load exclusions for this project
            var projectExclusions = GetTargetExclusions();
            var exclusionsForProj = projectExclusions.TryGetValue(proj.Guid ?? "", out var excList)
                ? excList : new List<string>();

            // Build per-panel data
            bool haveDb = File.Exists(dbPath);
            var panels = new List<object>();
            int aggFrames = 0;
            double aggSeconds = 0;
            int aggSessions = 0;
            DateTime? aggLastImaged = null;
            DateTime? aggFirstImaged = null;
            var panelNamesLower = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tgt in proj.Targets) {
                if (exclusionsForProj.Any(x => string.Equals(x, tgt.Name, StringComparison.OrdinalIgnoreCase))) continue;
                // Aggregate stats from NS DB (if available)
                List<TargetSessionDetail> tgtSessions = haveDb
                    ? DbSessionsForTarget(tgt.Name)
                    : new List<TargetSessionDetail>();

                double totalSec    = tgtSessions.Sum(s => s.IntegrationSeconds);
                int    totFrames   = tgtSessions.Sum(s => s.AcceptedFrames);
                int    sessCount   = tgtSessions.Count;
                DateTime? lastImg  = tgtSessions.Count > 0
                    ? tgtSessions.Max(s => s.SessionStart) as DateTime?
                    : null;
                DateTime? firstImg = tgtSessions.Count > 0
                    ? tgtSessions.Min(s => s.SessionStart) as DateTime?
                    : null;

                aggFrames   += totFrames;
                aggSeconds  += totalSec;
                aggSessions += sessCount;
                if (lastImg.HasValue  && (aggLastImaged  == null || lastImg.Value  > aggLastImaged.Value))
                    aggLastImaged  = lastImg;
                if (firstImg.HasValue && (aggFirstImaged == null || firstImg.Value < aggFirstImaged.Value))
                    aggFirstImaged = firstImg;

                // Camera data: use the most recent session for this target that has valid camera fields
                SessionRecord bestSession = null;
                foreach (var tsd in tgtSessions) {
                    if (sessionById.TryGetValue(tsd.SessionId, out var sr) &&
                        sr.CamXSize > 0 && sr.PixelSizeMicrons > 0 && sr.FocalLengthMm > 0) {
                        if (bestSession == null || sr.SessionStart > bestSession.SessionStart)
                            bestSession = sr;
                    }
                }

                // Compute FOV if camera data is available
                // PixelScale (arcsec/px) = (PixelSizeMicrons / FocalLengthMm) * 206.265
                double? pixelScale      = null;
                double? fovWidthDeg     = null;
                double? fovHeightDeg    = null;
                int?    camXSize        = null;
                int?    camYSize        = null;
                double? pixelSizeMicrons = null;
                double? focalLengthMm   = null;

                if (bestSession != null) {
                    camXSize         = bestSession.CamXSize;
                    camYSize         = bestSession.CamYSize;
                    pixelSizeMicrons = bestSession.PixelSizeMicrons;
                    focalLengthMm    = bestSession.FocalLengthMm;
                    pixelScale       = Math.Round((bestSession.PixelSizeMicrons / bestSession.FocalLengthMm) * 206.265, 4);
                    fovWidthDeg      = Math.Round(bestSession.CamXSize  * pixelScale.Value / 3600.0, 4);
                    fovHeightDeg     = Math.Round(bestSession.CamYSize * pixelScale.Value / 3600.0, 4);
                }

                // Most recent plate-solve position angle for this target
                latestPaByTarget.TryGetValue(tgt.Name ?? "", out var plateAngle);

                panels.Add(new {
                    guid             = tgt.Guid,
                    name             = tgt.Name,
                    active           = tgt.Active,
                    ra               = tgt.RA,
                    dec              = tgt.Dec,
                    rotation         = tgt.Rotation,
                    positionAngle    = plateAngle,
                    totalIntegrationHours = Math.Round(totalSec / 3600.0, 2),
                    acceptedFrames   = totFrames,
                    sessionCount     = sessCount,
                    lastImaged         = lastImg.HasValue ? lastImg.Value.ToString("o") : (string)null,
                    latestSessionId    = tgtSessions.Count > 0
                        ? tgtSessions.OrderByDescending(s => s.SessionStart).First().SessionId
                        : (string)null,
                    camXSize,
                    camYSize,
                    pixelSizeMicrons,
                    focalLengthMm,
                    pixelScaleArcSec = pixelScale,
                    fovWidthDeg,
                    fovHeightDeg,
                    filters          = tgtSessions.SelectMany(s => s.Filters)
                        .GroupBy(f => f.Filter)
                        .Select(g => new {
                            filter             = g.Key,
                            totalSeconds       = g.Sum(f => f.IntegrationSeconds),
                            totalHours         = Math.Round(g.Sum(f => f.IntegrationSeconds) / 3600.0, 2),
                            acceptedFrames     = g.Sum(f => f.AcceptedFrames),
                        })
                        .OrderByDescending(f => f.totalHours)
                        .ToList(),
                    tsGoals          = tgt.ExposurePlans.Select(ep => new {
                        filter       = ep.Filter,
                        templateName = ep.TemplateName,
                        exposureSec  = ep.ExposureSec,
                        desired      = ep.Desired,
                        accepted     = ep.Accepted,
                        acquired     = ep.Acquired,
                    }).ToList(),
                });
                panelNamesLower.Add(tgt.Name ?? "");
            }

            // Cross-assigned targets: targets assigned to this project via projectAssignments
            // that aren't native TS targets of this project. Look up exposure plans from the
            // target's native TS project so the cumulative TS progress bars in the UI include them.
            var projectAssignments = GetProjectAssignments();
            var projGuidLower = proj.Guid ?? "";

            // Proper-case name lookup: projectAssignments keys are lowercase, so fall back
            // to the NS Images table to recover the original casing for custom projects.
            var properNameByLower = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (haveDb) {
                foreach (var td in DbTargetDetails())
                    if (!string.IsNullOrEmpty(td.TargetName))
                        properNameByLower[td.TargetName.ToLowerInvariant()] = td.TargetName;
            }

            foreach (var kv in projectAssignments) {
                var tgtKey = kv.Key ?? "";
                var guids = kv.Value ?? new List<string>();
                if (panelNamesLower.Contains(tgtKey)) continue;
                if (!guids.Any(g => string.Equals(g, projGuidLower, StringComparison.OrdinalIgnoreCase))) continue;

                // Find the target's native TS target for RA/Dec + exposure plans
                TsProjectTarget nativeTarget = allProjects
                    .SelectMany(p => p.Targets)
                    .FirstOrDefault(x => string.Equals(x.Name, tgtKey, StringComparison.OrdinalIgnoreCase));

                var displayName = nativeTarget?.Name
                    ?? (properNameByLower.TryGetValue(tgtKey, out var pn) ? pn : tgtKey);
                List<TargetSessionDetail> caSessions = haveDb
                    ? DbSessionsForTarget(displayName)
                    : new List<TargetSessionDetail>();
                if (caSessions.Count == 0) continue;

                double caTotalSec    = caSessions.Sum(s => s.IntegrationSeconds);
                int    caTotFrames   = caSessions.Sum(s => s.AcceptedFrames);
                int    caSessCount   = caSessions.Count;
                DateTime? caLastImg  = caSessions.Max(s => s.SessionStart) as DateTime?;
                DateTime? caFirstImg = caSessions.Min(s => s.SessionStart) as DateTime?;

                aggFrames   += caTotFrames;
                aggSeconds  += caTotalSec;
                aggSessions += caSessCount;
                if (caLastImg.HasValue  && (aggLastImaged  == null || caLastImg.Value  > aggLastImaged.Value))
                    aggLastImaged  = caLastImg;
                if (caFirstImg.HasValue && (aggFirstImaged == null || caFirstImg.Value < aggFirstImaged.Value))
                    aggFirstImaged = caFirstImg;

                // Camera/FOV from most recent session with valid cam info
                SessionRecord caBest = null;
                foreach (var tsd in caSessions) {
                    if (sessionById.TryGetValue(tsd.SessionId, out var sr) &&
                        sr.CamXSize > 0 && sr.PixelSizeMicrons > 0 && sr.FocalLengthMm > 0) {
                        if (caBest == null || sr.SessionStart > caBest.SessionStart)
                            caBest = sr;
                    }
                }
                int?    caCamX = null, caCamY = null;
                double? caPx = null, caFl = null, caScale = null, caFovW = null, caFovH = null;
                if (caBest != null) {
                    caCamX  = caBest.CamXSize;
                    caCamY  = caBest.CamYSize;
                    caPx    = caBest.PixelSizeMicrons;
                    caFl    = caBest.FocalLengthMm;
                    caScale = Math.Round((caBest.PixelSizeMicrons / caBest.FocalLengthMm) * 206.265, 4);
                    caFovW  = Math.Round(caBest.CamXSize * caScale.Value / 3600.0, 4);
                    caFovH  = Math.Round(caBest.CamYSize * caScale.Value / 3600.0, 4);
                }

                latestPaByTarget.TryGetValue(displayName, out var caPlateAngle);

                panels.Add(new {
                    guid             = nativeTarget?.Guid,
                    name             = displayName,
                    active           = true,
                    ra               = nativeTarget?.RA ?? 0,
                    dec              = nativeTarget?.Dec ?? 0,
                    rotation         = nativeTarget?.Rotation ?? 0,
                    positionAngle    = caPlateAngle,
                    totalIntegrationHours = Math.Round(caTotalSec / 3600.0, 2),
                    acceptedFrames   = caTotFrames,
                    sessionCount     = caSessCount,
                    lastImaged       = caLastImg.HasValue  ? caLastImg.Value.ToString("o")  : (string)null,
                    firstImaged      = caFirstImg.HasValue ? caFirstImg.Value.ToString("o") : (string)null,
                    latestSessionId  = caSessions.OrderByDescending(s => s.SessionStart).First().SessionId,
                    camXSize         = caCamX,
                    camYSize         = caCamY,
                    pixelSizeMicrons = caPx,
                    focalLengthMm    = caFl,
                    pixelScaleArcSec = caScale,
                    fovWidthDeg      = caFovW,
                    fovHeightDeg     = caFovH,
                    filters          = caSessions.SelectMany(s => s.Filters)
                        .GroupBy(f => f.Filter)
                        .Select(g => new {
                            filter         = g.Key,
                            totalSeconds   = g.Sum(f => f.IntegrationSeconds),
                            totalHours     = Math.Round(g.Sum(f => f.IntegrationSeconds) / 3600.0, 2),
                            acceptedFrames = g.Sum(f => f.AcceptedFrames),
                        })
                        .OrderByDescending(f => f.totalHours)
                        .ToList(),
                    tsGoals          = (nativeTarget?.ExposurePlans ?? new List<TsProjectExposurePlan>()).Select(ep => new {
                        filter       = ep.Filter,
                        templateName = ep.TemplateName,
                        exposureSec  = ep.ExposureSec,
                        desired      = ep.Desired,
                        accepted     = ep.Accepted,
                        acquired     = ep.Acquired,
                    }).ToList(),
                    crossAssigned    = true,
                });
            }

            int totalDesiredProj = proj.Targets.SelectMany(t => t.ExposurePlans).Sum(ep => ep.Desired);
            int totalAcceptedProj = proj.Targets.SelectMany(t => t.ExposurePlans).Sum(ep => ep.Accepted);
            double? projectPercent = totalDesiredProj > 0
                ? Math.Round(Math.Min(100.0, (totalAcceptedProj * 100.0) / totalDesiredProj), 1)
                : (double?)null;

            var result = new {
                project = new {
                    guid            = proj.Guid,
                    name            = proj.Name,
                    description     = proj.Description,
                    state           = effectiveState,
                    rawState        = proj.State,
                    isMosaic        = proj.IsMosaic,
                    priority        = proj.Priority,
                    createDate      = proj.CreateDate?.ToString("o"),
                    activeDate      = proj.ActiveDate?.ToString("o"),
                    inactiveDate    = proj.InactiveDate?.ToString("o"),
                    minimumAltitude = proj.MinimumAltitude,
                    maximumAltitude = proj.MaximumAltitude,
                    percentComplete = projectPercent,
                },
                panels,
                aggregate = new {
                    totalIntegrationHours = Math.Round(aggSeconds / 3600.0, 2),
                    acceptedFrames        = aggFrames,
                    sessionCount          = aggSessions,
                    lastImaged            = aggLastImaged.HasValue  ? aggLastImaged.Value.ToString("o")  : (string)null,
                    firstImaged           = aggFirstImaged.HasValue ? aggFirstImaged.Value.ToString("o") : (string)null,
                    panelCount            = panels.Count,
                },
            };

            await WriteJson(res, 200, result);
            done?.Invoke(200, $"project '{proj.Name}' ({proj.Targets.Count} panels)");
        }

        // ── Project sessions (PDP session history + chart) ───────────────────
        private async Task HandleGetProjectSessions(TcpHttpResponse res, string projectGuid, Action<int, string> done) {
            if (string.IsNullOrEmpty(projectGuid)) {
                await WriteJson(res, 400, new { error = "Missing project guid" });
                done?.Invoke(400, null);
                return;
            }

            if (!TsAvailable()) {
                await WriteJson(res, 404, new { error = "Target Scheduler not available" });
                done?.Invoke(404, null);
                return;
            }

            List<TsProjectInfo> allProjects;
            try {
                allProjects = TsProjects();
            } catch (Exception ex) {
                await WriteJson(res, 500, new { error = "Internal server error" });
                done?.Invoke(500, ex.Message);
                return;
            }
            foreach (var cp in GetCustomProjects())
                if (!string.IsNullOrEmpty(cp.Guid) && !allProjects.Any(p => string.Equals(p.Guid, cp.Guid, StringComparison.OrdinalIgnoreCase)))
                    allProjects.Add(new TsProjectInfo { Guid = cp.Guid, Name = cp.Name, State = "Active", IsMosaic = false });

            var proj = allProjects.FirstOrDefault(p =>
                string.Equals(p.Guid, projectGuid, StringComparison.OrdinalIgnoreCase));
            if (proj == null) {
                await WriteJson(res, 404, new { error = $"Project '{projectGuid}' not found" });
                done?.Invoke(404, null);
                return;
            }

            // Panel target names (exclude targetExclusions + zero-coord stubs)
            var exclusions = GetTargetExclusions();
            var excList = exclusions.TryGetValue(proj.Guid ?? "", out var ex1) ? ex1 : new List<string>();
            var panelNames = new List<string>();
            foreach (var tgt in proj.Targets) {
                var tname = tgt.Name ?? "";
                if (excList.Any(e => string.Equals(e, tname, StringComparison.OrdinalIgnoreCase))) continue;
                if (tgt.RA == 0 && tgt.Dec == 0) continue;
                panelNames.Add(tname);
            }
            // Also include cross-assigned targets (projectAssignments pointing here)
            var assignments = GetProjectAssignments();
            foreach (var kv in assignments) {
                if (panelNames.Any(n => string.Equals(n, kv.Key, StringComparison.OrdinalIgnoreCase))) continue;
                if ((kv.Value ?? new List<string>()).Any(g => string.Equals(g, proj.Guid, StringComparison.OrdinalIgnoreCase))) {
                    panelNames.Add(kv.Key);
                }
            }
            var panelSetLower = new HashSet<string>(panelNames.Select(n => n.ToLowerInvariant()));

            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, new {
                    projectGuid, panelNames, totalIntegrationHours = 0.0, totalFrames = 0,
                    sessionCount = 0, sessions = Array.Empty<object>()
                });
                done?.Invoke(200, "0 sessions (no db)");
                return;
            }

            var allSessions = DbSessions().OrderByDescending(x => x.SessionStart).ToList();
            var resultSessions = new List<object>();
            double totalSec = 0;
            int totalFrames = 0;

            foreach (var s in allSessions) {
                if (string.IsNullOrEmpty(s.SessionId)) continue;

                var images = DbImages(s.SessionId);
                if (images == null || images.Count == 0) continue;

                var matching = images.Where(i => {
                    var tn = (i.TargetName ?? "").ToLowerInvariant();
                    if (!panelSetLower.Contains(tn)) return false;
                    var it = i.ImageType ?? "";
                    return it == "" || it == "LIGHT";
                }).ToList();
                if (matching.Count == 0) continue;

                var accepted = matching.Where(i => i.CountsAsAccepted).ToList();
                double integSec = accepted.Sum(i => i.ExposureDuration);
                var hfrs = accepted.Where(i => i.HFR > 0).Select(i => i.HFR).ToList();
                var guides = accepted.Where(i => i.GuidingRMSTotal > 0).Select(i => i.GuidingRMSTotal).ToList();
                double? avgHfr   = hfrs.Count   > 0 ? (double?)Math.Round(hfrs.Average(), 2)   : null;
                double? avgGuide = guides.Count > 0 ? (double?)Math.Round(guides.Average(), 2) : null;

                var byFilter = matching
                    .GroupBy(i => i.Filter ?? "Unknown")
                    .Select(g => {
                        var acc = g.Where(i => i.CountsAsAccepted).ToList();
                        var fHfrs   = acc.Where(i => i.HFR > 0).Select(i => i.HFR).ToList();
                        var fGuides = acc.Where(i => i.GuidingRMSTotal > 0).Select(i => i.GuidingRMSTotal).ToList();
                        double fSec = acc.Sum(i => i.ExposureDuration);
                        return new {
                            filter             = g.Key,
                            integrationSeconds = fSec,
                            integrationHours   = Math.Round(fSec / 3600.0, 2),
                            frames             = acc.Count,
                            totalFrames        = g.Count(),
                            avgHFR             = fHfrs.Count   > 0 ? (double?)Math.Round(fHfrs.Average(), 2)   : null,
                            avgGuidingRMS      = fGuides.Count > 0 ? (double?)Math.Round(fGuides.Average(), 2) : null,
                        };
                    })
                    .OrderByDescending(f => f.integrationSeconds)
                    .ToList();

                var targetsInSession = matching
                    .Select(i => i.TargetName ?? "")
                    .Where(n => panelSetLower.Contains(n.ToLowerInvariant()))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int durMin = (int)Math.Round((s.SessionEnd - s.SessionStart).TotalMinutes);

                string moon = null;
                var reportPath = Path.Combine(reportsDir, $"{s.SessionId}.html");
                if (File.Exists(reportPath)) {
                    try {
                        var html = File.ReadAllText(reportPath);
                        var moonMatch = Regex.Match(html, @"<div class='stat-value'>(\d+%\s*[^\<]*)</div>\s*<div class='stat-label'>Moon</div>");
                        if (moonMatch.Success) {
                            moon = Regex.Replace(
                                WebUtility.HtmlDecode(moonMatch.Groups[1].Value), @"\s+", " ").Trim();
                        }
                    } catch { }
                }

                resultSessions.Add(new {
                    sessionId          = s.SessionId,
                    sessionStart       = s.SessionStart.ToString("o"),
                    sessionEnd         = s.SessionEnd.ToString("o"),
                    durationMinutes    = durMin,
                    integrationHours   = Math.Round(integSec / 3600.0, 2),
                    integrationSeconds = integSec,
                    frames             = accepted.Count,
                    totalFrames        = matching.Count,
                    avgHFR             = avgHfr,
                    avgGuidingRMS      = avgGuide,
                    moonPhase          = moon,
                    targets            = targetsInSession,
                    filters            = byFilter,
                });
                totalSec    += integSec;
                totalFrames += accepted.Count;
            }

            await WriteJson(res, 200, new {
                projectGuid           = proj.Guid,
                panelNames,
                totalIntegrationHours = Math.Round(totalSec / 3600.0, 2),
                totalFrames,
                sessionCount          = resultSessions.Count,
                sessions              = resultSessions,
            });
            done?.Invoke(200, $"project sessions '{proj.Name}' ({resultSessions.Count} sessions)");
        }

        // ── Mosaic HiPS survey thumbnail ─────────────────────────────────────
        private async Task HandleGetProjectMosaicThumb(TcpHttpResponse res, string projectGuid, Action<int, string> done) {
            if (string.IsNullOrEmpty(projectGuid)) {
                await WriteJson(res, 400, new { error = "Missing project guid" });
                done?.Invoke(400, null);
                return;
            }

            if (!TsAvailable()) {
                await WriteJson(res, 404, new { error = "Target Scheduler not available" });
                done?.Invoke(404, null);
                return;
            }

            TsProjectInfo proj;
            try {
                proj = TsProjects().FirstOrDefault(p =>
                    string.Equals(p.Guid, projectGuid, StringComparison.OrdinalIgnoreCase));
            } catch (Exception ex) {
                await WriteJson(res, 500, new { error = "Internal server error" });
                done?.Invoke(500, ex.Message);
                return;
            }
            if (proj == null) {
                await WriteJson(res, 404, new { error = "Project not found" });
                done?.Invoke(404, null);
                return;
            }

            var coordTargets = proj.Targets
                .Where(t => !(t.RA == 0 && t.Dec == 0))
                .ToList();
            if (coordTargets.Count == 0) {
                await WriteJson(res, 404, new { error = "No targets with coordinates" });
                done?.Invoke(404, null);
                return;
            }

            // Helper: look up camera FOV (width_deg, height_deg) for a target by its most recent session with valid cam data
            bool haveCamDb = File.Exists(dbPath);
            Dictionary<string, SessionRecord> sessionById = haveCamDb
                ? DbSessions().ToDictionary(s => s.SessionId, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, SessionRecord>(StringComparer.OrdinalIgnoreCase);
            (double w, double h) GetCam(string tgtName) {
                if (string.IsNullOrEmpty(tgtName) || !haveCamDb) return (0, 0);
                List<TargetSessionDetail> tSessions;
                try { tSessions = DbSessionsForTarget(tgtName); }
                catch { return (0, 0); }
                foreach (var ts in tSessions.OrderByDescending(x => x.SessionStart)) {
                    if (!sessionById.TryGetValue(ts.SessionId, out var sr)) continue;
                    if (sr.CamXSize > 0 && sr.CamYSize > 0 && sr.PixelSizeMicrons > 0 && sr.FocalLengthMm > 0) {
                        double scale = (sr.PixelSizeMicrons / sr.FocalLengthMm) * 206.265;
                        return (sr.CamXSize * scale / 3600.0, sr.CamYSize * scale / 3600.0);
                    }
                }
                return (0, 0);
            }

            // Center on imaged panels only; fall back to all if nothing imaged yet
            var imaged = coordTargets.Where(t => {
                var c = GetCam(t.Name ?? "");
                return c.w > 0 && c.h > 0;
            }).ToList();
            var centerSource = imaged.Count > 0 ? imaged : coordTargets;

            double centerRa  = centerSource.Average(t => t.RA * 15.0);
            double centerDec = centerSource.Average(t => t.Dec);
            double cosCenter = Math.Cos(centerDec * Math.PI / 180.0);

            const int imgSize = 1024;
            double maxReach = 0.0;
            foreach (var t in coordTargets) {
                double dRa  = (t.RA * 15.0 - centerRa) * cosCenter;
                double dDec = t.Dec - centerDec;
                var cam = GetCam(t.Name ?? "");
                double halfDiag = (cam.w > 0 && cam.h > 0)
                    ? Math.Sqrt(cam.w * cam.w + cam.h * cam.h) / 2.0
                    : 0.0;
                double reach = Math.Sqrt(dRa * dRa + dDec * dDec) + halfDiag;
                if (reach > maxReach) maxReach = reach;
            }
            if (maxReach < 0.5) {
                var cam = GetCam(centerSource[0].Name ?? "");
                maxReach = (cam.w > 0 && cam.h > 0)
                    ? Math.Sqrt(cam.w * cam.w + cam.h * cam.h) / 2.0
                    : 1.0;
            }
            double hipsFov = maxReach * 2.0 * 1.15;

            // Cache key = MD5 of parameter string (auto-invalidates when layout changes)
            var cacheDir = Path.Combine(dataDir, "hips-cache");
            Directory.CreateDirectory(cacheDir);
            var paramStr = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F6}_{1:F6}_{2:F4}_{3}", centerRa, centerDec, hipsFov, imgSize);
            string cacheKey;
            using (var md5 = System.Security.Cryptography.MD5.Create()) {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(paramStr));
                cacheKey = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            var cachePath = Path.Combine(cacheDir, $"{cacheKey}.jpg");

            byte[] imgBytes;
            if (File.Exists(cachePath)) {
                imgBytes = File.ReadAllBytes(cachePath);
            } else {
                var url = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "https://alasky.u-strasbg.fr/hips-image-services/hips2fits" +
                    "?hips=CDS%2FP%2FDSS2%2Fcolor&ra={0:F6}&dec={1:F6}&fov={2:F4}" +
                    "&width={3}&height={3}&format=jpg&projection=TAN",
                    centerRa, centerDec, hipsFov, imgSize);
                try {
                    using (var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) }) {
                        http.DefaultRequestHeaders.UserAgent.ParseAdd("NightSummary/1.0");
                        imgBytes = await http.GetByteArrayAsync(url);
                    }
                } catch (Exception ex) {
                    log?.Warn($"HiPS fetch failed: {ex.Message}");
                    await WriteJson(res, 500, new { error = "HiPS thumbnail fetch failed" });
                    done?.Invoke(500, ex.Message);
                    return;
                }
                try { File.WriteAllBytes(cachePath, imgBytes); } catch { }
            }

            res.StatusCode = 200;
            res.ContentType = "image/jpeg";
            res.ContentLength64 = imgBytes.Length;
            res.Headers["Cache-Control"] = "public, max-age=86400";
            await res.OutputStream.WriteAsync(imgBytes, 0, imgBytes.Length);
            res.OutputStream.Close();
            done?.Invoke(200, $"mosaic-thumb cache={(File.Exists(cachePath) ? "hit" : "miss")} bytes={imgBytes.Length}");
        }

        // ── Settings & Regeneration ──────────────────────────────────────────

        private async Task HandleGetFilters(TcpHttpResponse res, Action<int, string> done) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, new { filters = Array.Empty<string>() });
                done?.Invoke(200, "0 filters (no db)");
                return;
            }
            var sessions = await _data.GetAllSessionsAsync();
            var filters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in sessions) {
                var images = DbImages(s.SessionId);
                foreach (var img in images) {
                    if (!string.IsNullOrEmpty(img.Filter)) filters.Add(img.Filter);
                }
            }
            var sorted = filters.OrderBy(f => f).ToList();
            await WriteJson(res, 200, new { filters = sorted });
            done?.Invoke(200, $"{sorted.Count} filters");
        }

        private async Task HandleGetSettings(TcpHttpResponse res) {
            var s = _settings.Current;
            await WriteJson(res, 200, new {
                tsAvailable            = TsAvailable(),
                reportDetailLevel      = s.ReportDetailLevel,
                reportLightMode        = s.ReportLightMode,
                expandSectionsDefault  = s.ExpandSectionsDefault,
                showMoonCurve          = s.ShowMoonCurve,
                showOverheadBreakdown  = s.ShowOverheadBreakdown,
                showSkyThumbnails      = s.ShowSkyThumbnails,
                showLiveStackImages    = s.ShowLiveStackImages,
                showSessionHistory     = s.ShowSessionHistory,
                showAltitudeChart      = s.ShowAltitudeChart,
                showMinAltitude        = s.ShowMinAltitude,
                showTSProgressBars     = s.ShowTSProgressBars,
                showStarCountCV        = s.ShowStarCountCV,
                showHFRGraph           = s.ShowHFRGraph,
                showChartAfMarkers     = s.ShowChartAfMarkers,
                showChartFlipMarkers   = s.ShowChartFlipMarkers,
                showChartRoofMarkers   = s.ShowChartRoofMarkers,
                showPerTargetIQ        = s.ShowPerTargetIQ,
                showEquipmentProfile   = s.ShowEquipmentProfile,
                timelineAltitudeDefault = s.TimelineAltitudeDefault,
                chartXAxisMetric       = s.ChartXAxisMetric,
                chartPrimaryMetric     = s.ChartPrimaryMetric,
                chartSecondaryMetric   = s.ChartSecondaryMetric,
                additionalChartConfigs = s.AdditionalChartConfigs,
                equipmentVisibleFields = s.EquipmentVisibleFields,
                filterClassifications  = s.FilterClassifications,
                filterTypeOverrides    = s.FilterTypeOverrides,
                equipmentOverrides     = s.EquipmentOverrides
            });
        }

        /// <summary>
        /// Returns the full Target Scheduler project tree in the exact shape expected by
        /// the dev server's <c>tools/dev-dashboard/data/ts-projects.json</c> file. Used by
        /// <c>snapshot.py</c> to capture a real TS snapshot for offline dev work.
        /// </summary>
        private async Task HandleGetTsProjects(TcpHttpResponse res, Action<int, string> done) {
            string tsStatus = "available";
            List<TsProjectInfo> projects = null;

            if (!TsAvailable()) {
                tsStatus = "not_installed";
                projects = new List<TsProjectInfo>();
                log?.Info("TS not available — returning empty project list");
            } else {
                try {
                    projects = TsProjects();
                    log?.Debug($"TS snapshot: {projects.Count} project(s)");
                } catch (Exception ex) {
                    tsStatus = "error";
                    _external.Error($"NightSummary: TS GetAllProjects threw during snapshot. {ex.Message}");
                    log?.Error($"TS GetAllProjects failed during snapshot: {ex.Message}");
                    projects = new List<TsProjectInfo>();
                }
            }

            var result = new {
                tsStatus,
                projects = projects.Select(p => new {
                    id              = p.Id,
                    guid            = p.Guid,
                    profileId       = p.ProfileId,
                    name            = p.Name,
                    description     = p.Description,
                    state           = p.State,
                    stateValue      = p.StateValue,
                    priority        = p.Priority,
                    isMosaic        = p.IsMosaic,
                    createDate      = p.CreateDate?.ToString("o"),
                    activeDate      = p.ActiveDate?.ToString("o"),
                    inactiveDate    = p.InactiveDate?.ToString("o"),
                    minimumAltitude = p.MinimumAltitude,
                    maximumAltitude = p.MaximumAltitude,
                    targets = p.Targets.Select(t => new {
                        id       = t.Id,
                        guid     = t.Guid,
                        projectId = t.ProjectId,
                        name     = t.Name,
                        active   = t.Active,
                        ra       = t.RA,
                        dec      = t.Dec,
                        rotation = t.Rotation,
                        exposurePlans = t.ExposurePlans.Select(ep => new {
                            filter       = ep.Filter,
                            templateName = ep.TemplateName,
                            exposureSec  = ep.ExposureSec,
                            desired      = ep.Desired,
                            acquired     = ep.Acquired,
                            accepted     = ep.Accepted,
                        }),
                    }),
                }),
            };

            await WriteJson(res, 200, result);
            done?.Invoke(200, $"{projects.Count} ts projects (status: {tsStatus})");
        }

        private static readonly System.Net.Http.HttpClient TonightApiClient = new System.Net.Http.HttpClient {
            Timeout = TimeSpan.FromSeconds(120)
        };

        // Tonight preview response cache (5-minute TTL; refreshed on first request after expiry)
        private string _tonightPreviewJson = null;
        private DateTime _tonightPreviewCachedAt = DateTime.MinValue;

        private async Task HandleGetTonightPreview(TcpHttpResponse res, Action<int, string> done) {
            try {
                // Return cached data if still fresh (5 min TTL — preview call takes ~25s)
                if (_tonightPreviewJson != null &&
                    (DateTime.UtcNow - _tonightPreviewCachedAt).TotalSeconds < 300) {
                    await WriteJsonRaw(res, 200, _tonightPreviewJson);
                    done?.Invoke(200, "tonight preview (cached)");
                    return;
                }

                    if (!TsAvailable()) {
                    await WriteJson(res, 200, new { error = "Target Scheduler is not installed or not available." });
                    done?.Invoke(200, "tonight: ts not available");
                    return;
                }

                var (apiEnabled, apiPort, apiHost) = TsApi();
                if (!apiEnabled) {
                    await WriteJson(res, 200, new { error = "Target Scheduler API is disabled. Enable it in Target Scheduler settings." });
                    done?.Invoke(200, "tonight: ts api disabled");
                    return;
                }

                var baseUrl = $"http://{apiHost}:{apiPort}/ts/v0";
                log?.Info($"Tonight preview: calling TS API at {baseUrl}");

                // Get active profile
                string profilesJson;
                try {
                    profilesJson = await TonightApiClient.GetStringAsync($"{baseUrl}/profiles");
                } catch (Exception ex) {
                    log?.Info($"Tonight preview: TS API unreachable: {ex.Message}");
                    await WriteJson(res, 200, new { error = "Could not reach Target Scheduler API. Make sure NINA is running with Target Scheduler installed and the API is enabled." });
                    done?.Invoke(200, "tonight: ts api unreachable");
                    return;
                }

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var profiles = JsonSerializer.Deserialize<List<TsProfileInfo>>(profilesJson, options);
                var active = profiles?.FirstOrDefault(p => p.Active);
                if (active == null) {
                    await WriteJson(res, 200, new { error = "No active NINA profile found in Target Scheduler." });
                    done?.Invoke(200, "tonight: no active profile");
                    return;
                }

                // Anchor at today 13:00 server-local — matches TS native preview default,
                // gives a full-night view, and is stable across refreshes.
                var now       = DateTime.Now;
                var startTime = now.Date.AddHours(13);

                var encodedStart = Uri.EscapeDataString(startTime.ToString("o"));
                var previewUrl = $"{baseUrl}/profiles/{active.Id}/preview?startTime={encodedStart}";
                log?.Info($"Tonight preview: start={startTime:HH:mm}, url={previewUrl}");

                // Call preview endpoint (~25s)
                string previewJson;
                try {
                    previewJson = await TonightApiClient.GetStringAsync(previewUrl);
                } catch (Exception ex) {
                    log?.Info($"Tonight preview: TS preview call failed: {ex.Message}");
                    await WriteJson(res, 200, new { error = "Target Scheduler preview failed. Check the NINA log for details." });
                    done?.Invoke(200, $"tonight: preview error: {ex.Message}");
                    return;
                }

                var entries = JsonSerializer.Deserialize<List<TsPreviewEntry>>(previewJson, options);
                if (entries == null) entries = new List<Data.TsPreviewEntry>();

                // Re-parse the raw TS JSON to recover original DateTimeOffset values for each
                // entry. Going through TsPreviewEntry's DateTime members coerces times to the
                // dashboard server's local TZ, which discards the rig's offset when those
                // differ (e.g. dev harness on a different TZ than the rig).
                var rawOffsets = new List<(DateTimeOffset start, DateTimeOffset end)>();
                using (var doc = JsonDocument.Parse(previewJson)) {
                    foreach (var el in doc.RootElement.EnumerateArray()) {
                        DateTimeOffset s = default, en = default;
                        if (el.TryGetProperty("StartTime", out var sEl) && sEl.ValueKind == JsonValueKind.String)
                            DateTimeOffset.TryParse(sEl.GetString(), out s);
                        if (el.TryGetProperty("EndTime", out var eEl) && eEl.ValueKind == JsonValueKind.String)
                            DateTimeOffset.TryParse(eEl.GetString(), out en);
                        rawOffsets.Add((s, en));
                    }
                }

                int tzOffsetMinutes = rawOffsets.Count > 0
                    ? (int)rawOffsets[0].start.Offset.TotalMinutes
                    : (int)TimeZoneInfo.Local.GetUtcOffset(now).TotalMinutes;

                var responseObj = new {
                    entries = entries.Select((e, i) => new {
                        id         = e.Id,
                        name       = e.Name,
                        waitPeriod = e.WaitPeriod,
                        startTime  = (i < rawOffsets.Count ? rawOffsets[i].start : new DateTimeOffset(e.StartTime)).ToString("o"),
                        endTime    = (i < rawOffsets.Count ? rawOffsets[i].end   : new DateTimeOffset(e.EndTime  )).ToString("o"),
                        exposurePlan = e.ExposurePlan.Select(ep => new {
                            filterName = ep.FilterName,
                            exposure   = ep.Exposure,
                            count      = ep.Count
                        }).ToList()
                    }).ToList(),
                    startTime       = startTime.ToString("o"),
                    tzOffsetMinutes = tzOffsetMinutes
                };

                _tonightPreviewJson = JsonSerializer.Serialize(responseObj, JsonOpts);
                _tonightPreviewCachedAt = DateTime.UtcNow;

                await WriteJsonRaw(res, 200, _tonightPreviewJson);
                done?.Invoke(200, $"tonight preview: {entries.Count} entries");
            } catch (Exception ex) {
                log?.Error("HandleGetTonightPreview", ex);
                await WriteJson(res, 500, new { error = "Internal server error" });
                done?.Invoke(500, ex.Message);
            }
        }

        private async Task HandleTsStatusOverride(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            try {
                var body = await ReadBodyCappedAsync(req, res, done);
                if (body == null) return;
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var root = doc.RootElement;
                var projectGuid = root.TryGetProperty("projectGuid", out var pg) && pg.ValueKind == JsonValueKind.String ? pg.GetString() : null;
                var status      = root.TryGetProperty("status",      out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : null;
                if (string.IsNullOrEmpty(projectGuid)) {
                    await WriteJson(res, 400, new { error = "projectGuid required" });
                    done?.Invoke(400, "missing projectGuid");
                    return;
                }
                // Valid statuses: Draft, Active, Inactive, Closed, Completed. Empty string clears override.
                if (!string.IsNullOrEmpty(status)) {
                    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                        "Draft", "Active", "Inactive", "Closed", "Completed"
                    };
                    if (!allowed.Contains(status)) {
                        await WriteJson(res, 400, new { error = "invalid status" });
                        done?.Invoke(400, "invalid status");
                        return;
                    }
                }
                SetTsStatusOverride(projectGuid, status);
                await WriteJson(res, 200, new { ok = true, projectGuid, status });
                done?.Invoke(200, $"ts override {projectGuid}={status ?? "(cleared)"}");
            } catch (Exception ex) {
                await WriteJson(res, 500, new { error = "Internal server error" });
                done?.Invoke(500, ex.Message);
            }
        }

        private async Task HandleTsTargetLink(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            try {
                var body = await ReadBodyCappedAsync(req, res, done);
                if (body == null) return;
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var root = doc.RootElement;
                var sessionTargetName = root.TryGetProperty("sessionTargetName", out var sn) && sn.ValueKind == JsonValueKind.String ? sn.GetString() : null;
                var tsTargetGuid      = root.TryGetProperty("tsTargetGuid",      out var tg) && tg.ValueKind == JsonValueKind.String ? tg.GetString() : null;
                if (string.IsNullOrEmpty(sessionTargetName)) {
                    await WriteJson(res, 400, new { error = "sessionTargetName required" });
                    done?.Invoke(400, "missing sessionTargetName");
                    return;
                }
                // Empty tsTargetGuid removes the manual link (reverts to auto-match)
                SetTsTargetLink(sessionTargetName.ToLowerInvariant(), tsTargetGuid);
                await WriteJson(res, 200, new { ok = true, sessionTargetName, tsTargetGuid });
                done?.Invoke(200, $"ts link '{sessionTargetName}' -> {tsTargetGuid ?? "(cleared)"}");
            } catch (Exception ex) {
                await WriteJson(res, 500, new { error = "Internal server error" });
                done?.Invoke(500, ex.Message);
            }
        }

        private async Task HandleTsAssign(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            try {
                var body = await ReadBodyCappedAsync(req, res, done);
                if (body == null) return;
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var root = doc.RootElement;
                var targetName  = root.TryGetProperty("targetName",  out var tn) && tn.ValueKind == JsonValueKind.String ? tn.GetString() : null;
                var projectGuid = root.TryGetProperty("projectGuid", out var pg) && pg.ValueKind == JsonValueKind.String ? pg.GetString() : null;
                if (string.IsNullOrEmpty(targetName)) {
                    await WriteJson(res, 400, new { error = "targetName required" });
                    done?.Invoke(400, "missing targetName");
                    return;
                }
                SetProjectAssignment(targetName.ToLowerInvariant(), projectGuid);
                await WriteJson(res, 200, new { ok = true, targetName, projectGuid });
                done?.Invoke(200, $"ts assign '{targetName}' -> {projectGuid ?? "(cleared)"}");
            } catch (Exception ex) {
                await WriteJson(res, 500, new { error = "Internal server error" });
                done?.Invoke(500, ex.Message);
            }
        }

        private async Task HandleTsExclude(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            try {
                var body = await ReadBodyCappedAsync(req, res, done);
                if (body == null) return;
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var root = doc.RootElement;
                var targetName  = root.TryGetProperty("targetName",  out var tn) && tn.ValueKind == JsonValueKind.String ? tn.GetString() : null;
                var projectGuid = root.TryGetProperty("projectGuid", out var pg) && pg.ValueKind == JsonValueKind.String ? pg.GetString() : null;
                var exclude     = !root.TryGetProperty("exclude", out var ex2) || ex2.ValueKind != JsonValueKind.False;
                if (string.IsNullOrEmpty(targetName) || string.IsNullOrEmpty(projectGuid)) {
                    await WriteJson(res, 400, new { error = "targetName and projectGuid required" });
                    done?.Invoke(400, "missing fields");
                    return;
                }
                SetTargetExclusion(projectGuid, targetName.ToLowerInvariant(), exclude);
                await WriteJson(res, 200, new { ok = true, targetName, projectGuid, excluded = exclude });
                done?.Invoke(200, $"ts exclude '{targetName}' from {projectGuid}: {exclude}");
            } catch (Exception ex) {
                await WriteJson(res, 500, new { error = "Internal server error" });
                done?.Invoke(500, ex.Message);
            }
        }

        private async Task HandleCustomProjects(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            var body = await ReadBodyCappedAsync(req, res, done);
            if (body == null) return;
            try {
                using var doc = JsonDocument.Parse(body);
                var root   = doc.RootElement;
                var action = root.TryGetProperty("action", out var a) ? a.GetString() : null;
                if (action == "create") {
                    var name = root.TryGetProperty("name", out var n) ? n.GetString()?.Trim() : null;
                    if (string.IsNullOrEmpty(name)) { await WriteJson(res, 400, new { error = "name required" }); return; }
                    var guid     = "custom-" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
                    var projects = GetCustomProjects();
                    projects.Add(new CustomProjectRecord(guid, name));
                    SaveCustomProjects(projects);
                    await WriteJson(res, 200, new { guid, name });
                    done?.Invoke(200, $"created custom project '{name}'");
                } else if (action == "delete") {
                    var guid = root.TryGetProperty("guid", out var g) ? g.GetString() : null;
                    if (string.IsNullOrEmpty(guid)) { await WriteJson(res, 400, new { error = "guid required" }); return; }
                    var projects = GetCustomProjects();
                    projects.RemoveAll(p => string.Equals(p.Guid, guid, StringComparison.OrdinalIgnoreCase));
                    SaveCustomProjects(projects);
                    await WriteJson(res, 200, new { ok = true });
                    done?.Invoke(200, $"deleted custom project {guid}");
                } else {
                    await WriteJson(res, 400, new { error = "unknown action" });
                }
            } catch (Exception ex) {
                await WriteJson(res, 500, new { error = "Internal server error" });
                done?.Invoke(500, ex.Message);
            }
        }

        private async Task HandleClientLog(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            try {
                var body = await ReadBodyCappedAsync(req, res, done);
                if (body == null) return;
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var root    = doc.RootElement;
                var level   = root.TryGetProperty("level",   out var lv) && lv.ValueKind == JsonValueKind.String ? lv.GetString() : "error";
                var message = root.TryGetProperty("message", out var mg) && mg.ValueKind == JsonValueKind.String ? mg.GetString() : "";
                var url     = root.TryGetProperty("url",     out var ul) && ul.ValueKind == JsonValueKind.String ? ul.GetString() : "";
                var entry   = string.IsNullOrEmpty(url) ? $"[JS] {message}" : $"[JS] {message} (page: {url})";
                switch (level?.ToLower()) {
                    case "warn":  log?.Warn(entry);  break;
                    case "error": log?.Error(entry); break;
                    default:      log?.Info(entry);  break;
                }
                await WriteJson(res, 200, new { ok = true });
                done?.Invoke(200, null);
            } catch (Exception ex) {
                log?.Error("HandleClientLog failed", ex);
                await WriteJson(res, 500, new { error = "internal error" });
                done?.Invoke(500, ex.Message);
            }
        }

        private async Task HandleProjectsReset(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            try {
                SetDashboardMeta(TsProjectAssignmentsKey, null);
                SetDashboardMeta(TsTargetExclusionsKey,   null);
                // Custom projects key cleared too for full reset
                SetDashboardMeta("ts.customProjects", null);
                await WriteJson(res, 200, new { ok = true, reset = true });
                done?.Invoke(200, "projects reset");
            } catch (Exception ex) {
                await WriteJson(res, 500, new { error = "Internal server error" });
                done?.Invoke(500, ex.Message);
            }
        }

        private async Task HandleProjectReset(TcpHttpRequest req, TcpHttpResponse res, string projectGuid, Action<int, string> done) {
            try {
                if (string.IsNullOrEmpty(projectGuid)) {
                    await WriteJson(res, 400, new { error = "projectGuid required" });
                    done?.Invoke(400, "missing guid");
                    return;
                }
                var map = GetTargetExclusions();
                map.Remove(projectGuid);
                SetDashboardMeta(TsTargetExclusionsKey, JsonSerializer.Serialize(map));

                var assignments = GetProjectAssignments();
                var keysToRemove = new List<string>();
                foreach (var kv in assignments) {
                    kv.Value.RemoveAll(g => string.Equals(g, projectGuid, StringComparison.OrdinalIgnoreCase));
                    if (kv.Value.Count == 0) keysToRemove.Add(kv.Key);
                }
                foreach (var k in keysToRemove) assignments.Remove(k);
                SetDashboardMeta(TsProjectAssignmentsKey, JsonSerializer.Serialize(assignments));

                await WriteJson(res, 200, new { ok = true, projectGuid });
                done?.Invoke(200, $"project reset {projectGuid}");
            } catch (Exception ex) {
                await WriteJson(res, 500, new { error = "Internal server error" });
                done?.Invoke(500, ex.Message);
            }
        }

        private async Task HandleRegenerateReport(TcpHttpRequest req, TcpHttpResponse res, string sessionId, Action<int, string> done) {
            if (_regen == null || !_regen.IsAvailable) {
                await WriteJson(res, 500, new { error = "Report generation not available" });
                done?.Invoke(500, "no regenerator");
                return;
            }

            try {
                log?.Info($"Regenerating report for {sessionId}");

                // Read settings overrides from POST body
                var body = await ReadBodyCappedAsync(req, res, done);
                if (body == null) return;

                var overrides = string.IsNullOrEmpty(body) ? null :
                    JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body);

                if (overrides != null)
                    log?.Debug($"Regenerate {sessionId}: {overrides.Count} setting overrides");

                // Save current settings, apply overrides, generate, restore
                var s = _settings.Current;
                var saved = SnapshotSettings(s);

                try {
                    ApplyOverrides(s, overrides);
                    // Tonight's Preview lives on the dashboard's Stats > Tonight tab,
                    // so the embedded report section is redundant when viewed in-dashboard.
                    // Not exposed in the per-report settings panel either.
                    s.ShowNextNightPreview = false;
                    log?.Debug($"Regenerate {sessionId} effective settings: {FormatSettingsForLog(s)}");

                    var err = await _regen.RegenerateAsync(sessionId);
                    if (err != null) {
                        await WriteJson(res, 500, new { error = err });
                        done?.Invoke(500, err);
                        return;
                    }
                    await SaveSessionSettings(sessionId, s);

                    thumbnailCache.TryRemove(sessionId, out _);
                    altitudeChartCache.TryRemove(sessionId, out _);
                    DeleteCachedChartJson(sessionId);
                    livestackCache.TryRemove(sessionId, out _);
                    log?.Info($"Regenerated report for {sessionId}");
                    _external.Info($"NightSummary: Dashboard regenerated report for {sessionId}");
                    await WriteJson(res, 200, new { status = "ok", sessionId });
                    done?.Invoke(200, sessionId);
                } finally {
                    RestoreSettings(s, saved);
                }
            } catch (Exception ex) {
                log?.Error($"Regeneration failed for {sessionId}", ex);
                _external.Error($"NightSummary: Dashboard report regeneration failed. {ex.Message}");
                await WriteJson(res, 500, new { error = "Internal server error" });
                done?.Invoke(500, ex.Message);
            }
        }

        private async Task HandleRegenerateAll(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            if (_regen == null || !_regen.IsAvailable) {
                await WriteJson(res, 500, new { error = "Report generation not available" });
                done?.Invoke(500, "no regenerator");
                return;
            }

            // Atomic check-and-set: only the caller that flips 0→1 wins. Without this
            // two simultaneous POSTs could both observe regenAllRunning==0, both pass
            // the gate, and run two concurrent regenerate-all loops corrupting counters.
            if (Interlocked.CompareExchange(ref regenAllRunning, 1, 0) != 0) {
                await WriteJson(res, 409, new { error = "Regeneration already in progress" });
                done?.Invoke(409, "already running");
                return;
            }

            try {
                var body = await ReadBodyCappedAsync(req, res, done);
                if (body == null) {
                    Interlocked.Exchange(ref regenAllRunning, 0);
                    return;
                }

                var overrides = string.IsNullOrEmpty(body) ? null :
                    JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body);

                if (!File.Exists(dbPath)) {
                    Interlocked.Exchange(ref regenAllRunning, 0);
                    await WriteJson(res, 404, new { error = "Database not found" });
                    done?.Invoke(404, "no db");
                    return;
                }

                    var sessions = DbSessions();

                // Initialize progress
                regenAllCurrent = 0;
                regenAllTotal = sessions.Count;
                regenAllGenerated = 0;
                regenAllFailed = 0;
                regenAllStatus = "running";
                regenAllError = null;

                log?.Info($"Bulk regeneration started — {sessions.Count} sessions");

                // Return immediately, run in background
                await WriteJson(res, 202, new { status = "started", total = sessions.Count });
                done?.Invoke(202, $"{sessions.Count} sessions queued");

                // Fire and forget the actual work
                _ = Task.Run(async () => {
                    var s = _settings.Current;
                    var saved = SnapshotSettings(s);
                    try {
                        ApplyOverrides(s, overrides);
                        s.ShowNextNightPreview = false; // see HandleRegenerate for rationale

                        for (int i = 0; i < sessions.Count; i++) {
                            regenAllCurrent = i + 1;
                            try {
                                var err = await _regen.RegenerateAsync(sessions[i].SessionId);
                                if (err != null) { regenAllFailed++; continue; }

                                await SaveSessionSettings(sessions[i].SessionId, s);
                                thumbnailCache.TryRemove(sessions[i].SessionId, out _);
                                altitudeChartCache.TryRemove(sessions[i].SessionId, out _);
                                DeleteCachedChartJson(sessions[i].SessionId);
                                livestackCache.TryRemove(sessions[i].SessionId, out _);
                                regenAllGenerated++;
                                log?.Debug($"Bulk regen {regenAllCurrent}/{sessions.Count}: {sessions[i].SessionId} OK");
                            } catch (Exception ex) {
                                log?.Warn($"Bulk regen {regenAllCurrent}/{sessions.Count}: {sessions[i].SessionId} FAILED — {ex.Message}");
                                _external.Warn($"NightSummary: Failed to regenerate report for {sessions[i].SessionId}. {ex.Message}");
                                regenAllFailed++;
                            }
                        }

                        regenAllStatus = "done";
                        log?.Info($"Bulk regeneration complete — {regenAllGenerated} generated, {regenAllFailed} failed");
                        _external.Info($"NightSummary: Dashboard bulk regeneration complete — {regenAllGenerated} generated, {regenAllFailed} failed");
                    } catch (Exception ex) {
                        regenAllStatus = "error";
                        regenAllError = ex.Message;
                        log?.Error("Bulk regeneration failed", ex);
                        _external.Error($"NightSummary: Dashboard bulk regeneration failed. {ex.Message}");
                    } finally {
                        RestoreSettings(s, saved);
                        Interlocked.Exchange(ref regenAllRunning, 0);
                    }
                });
            } catch (Exception ex) {
                Interlocked.Exchange(ref regenAllRunning, 0);
                regenAllStatus = "error";
                regenAllError = ex.Message;
                log?.Error("Bulk regeneration failed to start", ex);
                _external.Error($"NightSummary: Dashboard bulk regeneration failed. {ex.Message}");
                await WriteJson(res, 500, new { error = "Internal server error" });
                done?.Invoke(500, ex.Message);
            }
        }

        private async Task HandleRegenAllStatus(TcpHttpResponse res) {
            await WriteJson(res, 200, new {
                status = regenAllStatus ?? "idle",
                current = regenAllCurrent,
                total = regenAllTotal,
                generated = regenAllGenerated,
                failed = regenAllFailed,
                error = regenAllError
            });
        }

        private async Task HandleGetSessionSettings(TcpHttpResponse res, string sessionId, Action<int, string> done) {
            var settingsPath = Path.Combine(reportsDir, $"{sessionId}.settings.json");
            if (File.Exists(settingsPath)) {
                var json = await File.ReadAllTextAsync(settingsPath);
                json = AugmentSidecarWithDefaults(json);
                log?.Debug($"Settings for {sessionId} (sidecar): {json}");
                res.StatusCode = 200;
                res.ContentType = "application/json; charset=utf-8";
                var bytes = Encoding.UTF8.GetBytes(json);
                res.ContentLength64 = bytes.Length;
                await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                res.Close();
                done?.Invoke(200, $"{sessionId} (sidecar)");
            } else {
                // No saved settings — return current plugin settings as fallback
                log?.Debug($"Settings for {sessionId} (no sidecar, using plugin defaults): {FormatSettingsForLog(_settings.Current)}");
                await HandleGetSettings(res);
                done?.Invoke(200, $"{sessionId} (plugin defaults — no sidecar)");
            }
        }

        /// <summary>
        /// Sidecar files written before a setting was introduced lack that key, which
        /// would render the panel checkbox unchecked regardless of the user's actual
        /// preference. Patch missing keys with the current plugin default so old reports
        /// open with sensible defaults instead of phantom-off toggles.
        /// </summary>
        private string AugmentSidecarWithDefaults(string sidecarJson) {
            try {
                using var doc = JsonDocument.Parse(sidecarJson);
                var dict = new Dictionary<string, JsonElement>();
                foreach (var p in doc.RootElement.EnumerateObject()) dict[p.Name] = p.Value.Clone();
                bool patched = false;
                void PatchBool(string key, bool def) {
                    if (!dict.ContainsKey(key)) { dict[key] = JsonSerializer.SerializeToElement(def); patched = true; }
                }
                void PatchInt(string key, int def) {
                    if (!dict.ContainsKey(key)) { dict[key] = JsonSerializer.SerializeToElement(def); patched = true; }
                }
                var s = _settings.Current;
                PatchBool("timelineAltitudeDefault", s.TimelineAltitudeDefault);
                PatchInt("chartXAxisMetric",   s.ChartXAxisMetric);
                PatchInt("chartPrimaryMetric", s.ChartPrimaryMetric);
                PatchInt("chartSecondaryMetric", s.ChartSecondaryMetric);
                if (!patched) return sidecarJson;
                return JsonSerializer.Serialize(dict, JsonOpts);
            } catch (Exception ex) {
                log?.Warn($"AugmentSidecarWithDefaults: failed to parse, returning raw — {ex.Message}");
                return sidecarJson;
            }
        }

        /// <summary>
        /// Saves the current report display settings as a JSON sidecar file alongside
        /// the report HTML. This captures exactly what settings were used to generate
        /// the report, so the dashboard can accurately reflect them.
        /// </summary>
        private async Task SaveSessionSettings(string sessionId, NightSummarySettings s) {
            try {
                var settings = new {
                    reportDetailLevel      = s.ReportDetailLevel,
                    reportLightMode        = s.ReportLightMode,
                    expandSectionsDefault  = s.ExpandSectionsDefault,
                    showMoonCurve          = s.ShowMoonCurve,
                    showOverheadBreakdown  = s.ShowOverheadBreakdown,
                    showSkyThumbnails      = s.ShowSkyThumbnails,
                    showLiveStackImages    = s.ShowLiveStackImages,
                    showSessionHistory     = s.ShowSessionHistory,
                    showAltitudeChart      = s.ShowAltitudeChart,
                    showMinAltitude        = s.ShowMinAltitude,
                    showTSProgressBars     = s.ShowTSProgressBars,
                    showStarCountCV        = s.ShowStarCountCV,
                    showHFRGraph           = s.ShowHFRGraph,
                    showChartAfMarkers     = s.ShowChartAfMarkers,
                    showChartFlipMarkers   = s.ShowChartFlipMarkers,
                    showChartRoofMarkers   = s.ShowChartRoofMarkers,
                    showPerTargetIQ        = s.ShowPerTargetIQ,
                    showEquipmentProfile   = s.ShowEquipmentProfile,
                    timelineAltitudeDefault = s.TimelineAltitudeDefault,
                    chartXAxisMetric       = s.ChartXAxisMetric,
                    chartPrimaryMetric     = s.ChartPrimaryMetric,
                    chartSecondaryMetric   = s.ChartSecondaryMetric,
                    additionalChartConfigs = s.AdditionalChartConfigs,
                    equipmentVisibleFields = s.EquipmentVisibleFields,
                    filterClassifications  = s.FilterClassifications,
                    filterTypeOverrides    = s.FilterTypeOverrides,
                    equipmentOverrides     = s.EquipmentOverrides
                };
                var json = JsonSerializer.Serialize(settings, JsonOpts);
                var settingsPath = Path.Combine(reportsDir, $"{sessionId}.settings.json");
                await File.WriteAllTextAsync(settingsPath, json);
                log?.Debug($"Saved settings sidecar for {sessionId}");
            } catch (Exception ex) {
                log?.Warn($"Failed to save settings sidecar for {sessionId}: {ex.Message}");
                _external.Warn($"NightSummary: Failed to save settings for {sessionId}. {ex.Message}");
            }
        }

private static string FormatSettingsForLog(NightSummarySettings s) {
            var bools = new List<string>();
            if (s.ShowMoonCurve) bools.Add("Moon");
            if (s.ShowOverheadBreakdown) bools.Add("Overhead");
            if (s.ShowSkyThumbnails) bools.Add("Sky");
            if (s.ShowLiveStackImages) bools.Add("LiveStack");
            if (s.ShowSessionHistory) bools.Add("History");
            if (s.ShowAltitudeChart) bools.Add("Altitude");
            if (s.ShowMinAltitude) bools.Add("MinAlt");
            if (s.ShowTSProgressBars) bools.Add("TSProgress");
            if (s.ShowStarCountCV) bools.Add("StarCV");
            if (s.ShowHFRGraph) bools.Add("Metric");
            if (s.ShowChartAfMarkers) bools.Add("AFMarkers");
            if (s.ShowChartFlipMarkers) bools.Add("FlipMarkers");
            if (s.ShowChartRoofMarkers) bools.Add("RoofMarkers");
            if (s.ShowPerTargetIQ) bools.Add("PerTargetIQ");
            if (s.ShowEquipmentProfile) bools.Add("Equipment");
            if (s.ExpandSectionsDefault) bools.Add("Expand");
            if (s.ReportLightMode) bools.Add("Light");

            return $"detail={s.ReportDetailLevel}, " +
                $"sections=[{string.Join(",", bools)}], " +
                $"chart={s.ChartXAxisMetric}/{s.ChartPrimaryMetric}/{s.ChartSecondaryMetric}, " +
                $"additional=\"{s.AdditionalChartConfigs}\", " +
                $"filters=\"{s.FilterClassifications}\", " +
                $"eqVisible=\"{s.EquipmentVisibleFields}\", " +
                $"eqOverrides=\"{s.EquipmentOverrides}\"";
        }

        private static Dictionary<string, object> SnapshotSettings(NightSummarySettings s) {
            return new Dictionary<string, object> {
                ["ReportDetailLevel"]     = s.ReportDetailLevel,
                ["ReportLightMode"]       = s.ReportLightMode,
                ["ExpandSectionsDefault"] = s.ExpandSectionsDefault,
                ["ShowMoonCurve"]         = s.ShowMoonCurve,
                ["ShowOverheadBreakdown"] = s.ShowOverheadBreakdown,
                ["ShowSkyThumbnails"]     = s.ShowSkyThumbnails,
                ["ShowLiveStackImages"]   = s.ShowLiveStackImages,
                ["ShowSessionHistory"]    = s.ShowSessionHistory,
                ["ShowAltitudeChart"]     = s.ShowAltitudeChart,
                ["ShowMinAltitude"]       = s.ShowMinAltitude,
                ["ShowTSProgressBars"]    = s.ShowTSProgressBars,
                ["ShowStarCountCV"]       = s.ShowStarCountCV,
                ["ShowHFRGraph"]          = s.ShowHFRGraph,
                ["ShowChartAfMarkers"]    = s.ShowChartAfMarkers,
                ["ShowChartFlipMarkers"]  = s.ShowChartFlipMarkers,
                ["ShowChartRoofMarkers"]  = s.ShowChartRoofMarkers,
                ["ShowPerTargetIQ"]       = s.ShowPerTargetIQ,
                ["ShowNextNightPreview"]  = s.ShowNextNightPreview,
                ["ShowEquipmentProfile"]  = s.ShowEquipmentProfile,
                ["TimelineAltitudeDefault"] = s.TimelineAltitudeDefault,
                ["ChartXAxisMetric"]      = s.ChartXAxisMetric,
                ["ChartPrimaryMetric"]    = s.ChartPrimaryMetric,
                ["ChartSecondaryMetric"]  = s.ChartSecondaryMetric,
                ["AdditionalChartConfigs"]= s.AdditionalChartConfigs,
                ["EquipmentVisibleFields"]= s.EquipmentVisibleFields,
                ["FilterClassifications"] = s.FilterClassifications,
                ["FilterTypeOverrides"]   = s.FilterTypeOverrides,
                ["EquipmentOverrides"]    = s.EquipmentOverrides
            };
        }

        private static void RestoreSettings(NightSummarySettings s, Dictionary<string, object> saved) {
            s.ReportDetailLevel     = (int)saved["ReportDetailLevel"];
            s.ReportLightMode       = (bool)saved["ReportLightMode"];
            s.ExpandSectionsDefault = (bool)saved["ExpandSectionsDefault"];
            s.ShowMoonCurve         = (bool)saved["ShowMoonCurve"];
            s.ShowOverheadBreakdown = (bool)saved["ShowOverheadBreakdown"];
            s.ShowSkyThumbnails     = (bool)saved["ShowSkyThumbnails"];
            s.ShowLiveStackImages   = (bool)saved["ShowLiveStackImages"];
            s.ShowSessionHistory    = (bool)saved["ShowSessionHistory"];
            s.ShowAltitudeChart     = (bool)saved["ShowAltitudeChart"];
            s.ShowMinAltitude       = (bool)saved["ShowMinAltitude"];
            s.ShowTSProgressBars    = (bool)saved["ShowTSProgressBars"];
            s.ShowStarCountCV       = (bool)saved["ShowStarCountCV"];
            s.ShowHFRGraph          = (bool)saved["ShowHFRGraph"];
            s.ShowChartAfMarkers    = (bool)(saved.ContainsKey("ShowChartAfMarkers") ? saved["ShowChartAfMarkers"] : true);
            s.ShowChartFlipMarkers  = (bool)(saved.ContainsKey("ShowChartFlipMarkers") ? saved["ShowChartFlipMarkers"] : true);
            s.ShowChartRoofMarkers  = (bool)(saved.ContainsKey("ShowChartRoofMarkers") ? saved["ShowChartRoofMarkers"] : false);
            s.ShowPerTargetIQ       = (bool)saved["ShowPerTargetIQ"];
            s.ShowNextNightPreview  = (bool)saved["ShowNextNightPreview"];
            s.ShowEquipmentProfile  = (bool)saved["ShowEquipmentProfile"];
            s.TimelineAltitudeDefault = (bool)(saved.ContainsKey("TimelineAltitudeDefault") ? saved["TimelineAltitudeDefault"] : true);
            s.ChartXAxisMetric      = (int)saved["ChartXAxisMetric"];
            s.ChartPrimaryMetric    = (int)saved["ChartPrimaryMetric"];
            s.ChartSecondaryMetric  = (int)saved["ChartSecondaryMetric"];
            s.AdditionalChartConfigs= (string)saved["AdditionalChartConfigs"];
            s.EquipmentVisibleFields= (string)saved["EquipmentVisibleFields"];
            s.FilterClassifications = (string)saved["FilterClassifications"];
            s.FilterTypeOverrides   = saved.ContainsKey("FilterTypeOverrides") ? (string)saved["FilterTypeOverrides"] : "";
            s.EquipmentOverrides    = (string)saved["EquipmentOverrides"];
        }

        private static void ApplyOverrides(NightSummarySettings s, Dictionary<string, JsonElement> overrides) {
            if (overrides == null) return;
            foreach (var kv in overrides) {
                switch (kv.Key) {
                    case "reportDetailLevel":     s.ReportDetailLevel     = kv.Value.GetInt32(); break;
                    case "reportLightMode":        s.ReportLightMode       = kv.Value.GetBoolean(); break;
                    case "expandSectionsDefault":  s.ExpandSectionsDefault = kv.Value.GetBoolean(); break;
                    case "showMoonCurve":          s.ShowMoonCurve         = kv.Value.GetBoolean(); break;
                    case "showOverheadBreakdown":  s.ShowOverheadBreakdown = kv.Value.GetBoolean(); break;
                    case "showSkyThumbnails":      s.ShowSkyThumbnails     = kv.Value.GetBoolean(); break;
                    case "showLiveStackImages":    s.ShowLiveStackImages   = kv.Value.GetBoolean(); break;
                    case "showSessionHistory":     s.ShowSessionHistory    = kv.Value.GetBoolean(); break;
                    case "showAltitudeChart":      s.ShowAltitudeChart     = kv.Value.GetBoolean(); break;
                    case "showMinAltitude":        s.ShowMinAltitude       = kv.Value.GetBoolean(); break;
                    case "showTSProgressBars":     s.ShowTSProgressBars    = kv.Value.GetBoolean(); break;
                    case "showStarCountCV":        s.ShowStarCountCV       = kv.Value.GetBoolean(); break;
                    case "showHFRGraph":           s.ShowHFRGraph          = kv.Value.GetBoolean(); break;
                    case "showChartAfMarkers":     s.ShowChartAfMarkers    = kv.Value.GetBoolean(); break;
                    case "showChartFlipMarkers":   s.ShowChartFlipMarkers  = kv.Value.GetBoolean(); break;
                    case "showChartRoofMarkers":   s.ShowChartRoofMarkers  = kv.Value.GetBoolean(); break;
                    case "showPerTargetIQ":        s.ShowPerTargetIQ       = kv.Value.GetBoolean(); break;
                    case "showEquipmentProfile":   s.ShowEquipmentProfile  = kv.Value.GetBoolean(); break;
                    case "timelineAltitudeDefault": s.TimelineAltitudeDefault = kv.Value.GetBoolean(); break;
                    case "chartXAxisMetric":       s.ChartXAxisMetric      = kv.Value.GetInt32(); break;
                    case "chartPrimaryMetric":     s.ChartPrimaryMetric    = kv.Value.GetInt32(); break;
                    case "chartSecondaryMetric":   s.ChartSecondaryMetric  = kv.Value.GetInt32(); break;
                    case "additionalChartConfigs": s.AdditionalChartConfigs= kv.Value.GetString(); break;
                    case "equipmentVisibleFields": s.EquipmentVisibleFields= kv.Value.GetString(); break;
                    case "filterClassifications":  s.FilterClassifications = kv.Value.GetString(); break;
                    case "filterTypeOverrides":    s.FilterTypeOverrides   = kv.Value.GetString(); break;
                    case "equipmentOverrides":     s.EquipmentOverrides    = kv.Value.GetString(); break;
                }
            }
        }

        // 10MB cap on POST bodies — keeps a malicious LAN client (or future Phase 2
        // public surface) from OOMing the server with an unbounded ReadToEndAsync.
        private const int MaxRequestBodyBytes = 10 * 1024 * 1024;

        // Reads the request body up to MaxRequestBodyBytes. Writes 413 + done callback
        // and returns null if Content-Length advertises overflow or the unknown-length
        // body exceeds the cap mid-read. Callers must early-return on null.
        private static async Task<string> ReadBodyCappedAsync(
            TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            if (req.ContentLength64 > MaxRequestBodyBytes) {
                await WriteJson(res, 413, new { error = "Request body too large" });
                done?.Invoke(413, $"body {req.ContentLength64} > cap {MaxRequestBodyBytes}");
                return null;
            }
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
            if (req.ContentLength64 >= 0) {
                return await reader.ReadToEndAsync();
            }
            // Unknown length (chunked) — read in chunks and abort if we go over the cap.
            var buf = new char[16 * 1024];
            var sb = new StringBuilder();
            int total = 0;
            int n;
            while ((n = await reader.ReadAsync(buf, 0, buf.Length)) > 0) {
                total += n;
                if (total > MaxRequestBodyBytes) {
                    await WriteJson(res, 413, new { error = "Request body too large" });
                    done?.Invoke(413, $"body exceeded cap {MaxRequestBodyBytes}");
                    return null;
                }
                sb.Append(buf, 0, n);
            }
            return sb.ToString();
        }

        // ── Response Helpers ──────────────────────────────────────────────────

        private static async Task WriteJson(TcpHttpResponse res, int status, object data) {
            res.StatusCode = status;
            res.ContentType = "application/json; charset=utf-8";
            res.Headers.Add("Access-Control-Allow-Origin", "*");
            var json = JsonSerializer.Serialize(data, JsonOpts);
            var bytes = Encoding.UTF8.GetBytes(json);
            res.ContentLength64 = bytes.Length;
            await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            res.Close();
        }

        private static async Task WriteJsonRaw(TcpHttpResponse res, int status, string json) {
            res.StatusCode = status;
            res.ContentType = "application/json; charset=utf-8";
            res.Headers.Add("Access-Control-Allow-Origin", "*");
            var bytes = Encoding.UTF8.GetBytes(json);
            res.ContentLength64 = bytes.Length;
            await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            res.Close();
        }

        private static async Task WriteHtml(TcpHttpResponse res, int status, string html) {
            res.StatusCode = status;
            res.ContentType = "text/html; charset=utf-8";
            res.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
            res.Headers.Add("Pragma", "no-cache");
            var bytes = Encoding.UTF8.GetBytes(html);
            res.ContentLength64 = bytes.Length;
            await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            res.Close();
        }

        // ── Stats Summary ──────────────────────────────────────────────────────

        private async Task HandleGetStatsSummary(TcpHttpResponse res, Action<int, string> done) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, new {
                    totalSessions = 0, totalIntegrationHours = 0.0, totalImages = 0,
                    targetCount = 0, firstSession = (string)null, lastSession = (string)null
                });
                done?.Invoke(200, "empty (no db)");
                return;
            }

            var sessions = await _data.GetAllSessionsAsync();
            double totalIntegration = 0;
            int totalImages = 0;
            var allTargets = new HashSet<string>();

            foreach (var s in sessions) {
                var images = DbImages(s.SessionId);
                var lights = images.Where(i => string.IsNullOrEmpty(i.ImageType) || i.ImageType == "LIGHT").ToList();
                totalImages += lights.Count(i => i.CountsAsAccepted);
                totalIntegration += lights.Where(i => i.CountsAsAccepted).Sum(i => i.ExposureDuration);
                foreach (var t in lights.Where(i => !string.IsNullOrEmpty(i.TargetName)).Select(i => i.TargetName))
                    allTargets.Add(t);
            }

            if (sessions.Count == 0) log?.Warn("Stats summary: 0 sessions in DB");
            await WriteJson(res, 200, new {
                totalSessions = sessions.Count,
                totalIntegrationHours = Math.Round(totalIntegration / 3600.0, 1),
                totalImages,
                targetCount = allTargets.Count,
                firstSession = sessions.Count > 0 ? sessions.Last().SessionStart.ToString("o") : null,
                lastSession = sessions.Count > 0 ? sessions.First().SessionStart.ToString("o") : null
            });
            done?.Invoke(200, $"{sessions.Count} sessions, {allTargets.Count} targets");
        }

        // ── Tailscale Detection ──────────────────────────────────────────────────

        private static string GetTailscaleUrl(int port) {
            try {
                var psi = new System.Diagnostics.ProcessStartInfo {
                    FileName = "tailscale",
                    Arguments = "ip -4",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                var ip = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(3000);
                if (proc.ExitCode == 0 && !string.IsNullOrEmpty(ip) && ip.StartsWith("100.")) {
                    return $"http://{ip}:{port}";
                }
            } catch {
                // Tailscale not installed or not running — silently ignore
            }
            return null;
        }

        // ── ZeroTier Detection ───────────────────────────────────────────────────

        private static string GetZeroTierUrl(int port) {
            try {
                foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()) {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (!nic.Name.Contains("ZeroTier") && !nic.Description.Contains("ZeroTier")) continue;
                    foreach (var addr in nic.GetIPProperties().UnicastAddresses) {
                        if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            return $"http://{addr.Address}:{port}";
                    }
                }
            } catch {
                // ZeroTier not installed or not running — silently ignore
            }
            return null;
        }

        // ── Dashboard HTML (from embedded resources) ──────────────────────────

        private string GetDashboardHtml() {
            if (cachedDashboardHtml != null && !_webAssets.HotReload) return cachedDashboardHtml;

            try {
                var html = ReadAssetText("dashboard.html");
                var css  = ReadAssetText("flatpickr.min.css") + "\n" + ReadAssetText("dashboard.css");
                var js   = ReadAssetText("flatpickr.min.js")  + "\n" + ReadAssetText("dashboard.js");
                var iconBytes = _webAssets.ReadAsync("plugin-icon.png").GetAwaiter().GetResult();
                var iconBase64 = iconBytes != null
                    ? "data:image/png;base64," + Convert.ToBase64String(iconBytes)
                    : "";
                cachedDashboardHtml = html
                    .Replace("{{STYLES}}", css)
                    .Replace("{{SCRIPTS}}", js)
                    .Replace("{{ICON}}", iconBase64)
                    .Replace("{{VERSION}}", _settings.PluginVersion ?? "");
            } catch (Exception ex) {
                _external.Error($"NightSummary: Failed to load dashboard resources. {ex.Message}");
                cachedDashboardHtml = "<!DOCTYPE html><html><body><h1>Dashboard failed to load</h1>" +
                    "<p>Check the NINA log for details.</p></body></html>";
            }

            return cachedDashboardHtml;
        }

        private string ReadAssetText(string name) {
            var bytes = _webAssets.ReadAsync(name).GetAwaiter().GetResult();
            if (bytes == null) throw new FileNotFoundException($"Web asset '{name}' not found");
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }
}
