using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Session;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Server {

    public class DashboardServer {

        private HttpListener listener;
        private CancellationTokenSource cts;
        private readonly string dbPath;
        private readonly string reportsDir;
        private readonly string dataDir;
        private readonly SessionService sessionService;
        private string cachedDashboardHtml;
        private DashboardLog log;

        // Regenerate-all progress tracking
        private volatile bool regenAllRunning;
        private volatile int regenAllCurrent;
        private volatile int regenAllTotal;
        private volatile int regenAllGenerated;
        private volatile int regenAllFailed;
        private volatile string regenAllStatus; // "running", "done", "error"
        private volatile string regenAllError;

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        public bool IsRunning { get; private set; }
        public string Url { get; private set; }
        public string TailscaleUrl { get; private set; }

        public DashboardServer(string dbPath, SessionService sessionService) {
            this.dbPath = dbPath;
            this.sessionService = sessionService;
            this.dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA", "NightSummary");
            this.reportsDir = Path.Combine(dataDir, "reports");
            Directory.CreateDirectory(reportsDir);
        }

        public Task StartAsync(int port) {
            if (IsRunning) return Task.CompletedTask;

            try {
                log = DashboardLog.Init(Path.Combine(dataDir, "dashboard.log"));

                cts = new CancellationTokenSource();
                listener = new HttpListener();
                listener.Prefixes.Add($"http://+:{port}/");
                listener.Start();

                var hostname = Dns.GetHostName();
                Url = $"http://{hostname}:{port}";
                TailscaleUrl = GetTailscaleUrl(port);
                IsRunning = true;

                // Fire-and-forget the request loop
                _ = AcceptLoop(cts.Token);

                log.Info($"Server started on port {port} — local: {Url}" +
                    (TailscaleUrl != null ? $", tailnet: {TailscaleUrl}" : ""));
                log.Info($"DB: {dbPath}");
                log.Info($"Reports: {reportsDir}");

                Logger.Info($"NightSummary: Local dashboard started at {Url}");
                if (TailscaleUrl != null)
                    Logger.Info($"NightSummary: Tailnet URL: {TailscaleUrl}");
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to start local dashboard. {ex.Message}");
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
                listener?.Stop();
                listener?.Close();
                listener = null;
                IsRunning = false;
                Url = null;
                TailscaleUrl = null;
                log?.Info("Server stopped");
                Logger.Info("NightSummary: Local dashboard stopped");
                DashboardLog.Shutdown();
                log = null;
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Error stopping local dashboard. {ex.Message}");
            }

            return Task.CompletedTask;
        }

        public string GetReportsDirectory() => reportsDir;

        private async Task AcceptLoop(CancellationToken ct) {
            while (!ct.IsCancellationRequested) {
                try {
                    var context = await listener.GetContextAsync();
                    // Handle each request without blocking the accept loop
                    _ = Task.Run(() => HandleRequest(context), ct);
                } catch (ObjectDisposedException) {
                    break;
                } catch (HttpListenerException) when (ct.IsCancellationRequested) {
                    break;
                } catch (Exception ex) {
                    log?.Error("Accept loop error", ex);
                    Logger.Error($"NightSummary: Dashboard accept error. {ex.Message}");
                }
            }
        }

        private async Task HandleRequest(HttpListenerContext context) {
            var req = context.Request;
            var res = context.Response;
            var path = req.Url.AbsolutePath.TrimEnd('/');
            if (string.IsNullOrEmpty(path)) path = "/";
            var done = log?.BeginRequest(req.HttpMethod, path);

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
                        await HandleGetSessionReport(res, sessionId, done);
                    } else if (path.StartsWith("/api/sessions/") && path.EndsWith("/settings")) {
                        var sessionId = ExtractSessionId(path, "/settings");
                        await HandleGetSessionSettings(res, sessionId, done);
                    } else if (path.StartsWith("/api/sessions/") && !path.Substring("/api/sessions/".Length).Contains("/")) {
                        var sessionId = path.Substring("/api/sessions/".Length);
                        await HandleGetSession(res, sessionId, done);
                    } else if (path == "/api/stats/targets") {
                        await HandleGetTargetStats(res, done);
                    } else if (path == "/api/stats/summary") {
                        await HandleGetStatsSummary(res, done);
                    } else if (path == "/api/filters") {
                        await HandleGetFilters(res, done);
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
                Logger.Error($"NightSummary: Dashboard request error for {req.Url}. {ex.Message}");
                done?.Invoke(500, ex.Message);
                try { await WriteJson(res, 500, new { error = ex.Message }); } catch { res.Close(); }
            }
        }

        private string ExtractSessionId(string path, string suffix) {
            var start = "/api/sessions/".Length;
            var end = path.Length - suffix.Length;
            return path.Substring(start, end - start);
        }

        // ── API Handlers ──────────────────────────────────────────────────────

        private async Task HandleGetSessions(HttpListenerResponse res, Action<int, string> done) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, Array.Empty<object>());
                done?.Invoke(200, "0 sessions (no db)");
                return;
            }

            var db = new SessionDatabase(dbPath);
            var sessions = db.GetAllSessions();
            var result = sessions.Select(s => {
                var images = db.GetImagesForSession(s.SessionId);
                var lightImages = images.Where(i => string.IsNullOrEmpty(i.ImageType) || i.ImageType == "LIGHT").ToList();
                return new {
                    sessionId = s.SessionId,
                    sessionStart = s.SessionStart.ToString("o"),
                    sessionEnd = s.SessionEnd.ToString("o"),
                    profileName = s.ProfileName,
                    imageCount = lightImages.Count,
                    targets = lightImages
                        .Where(i => !string.IsNullOrEmpty(i.TargetName))
                        .Select(i => i.TargetName).Distinct().ToList(),
                    totalIntegrationSeconds = lightImages.Where(i => i.Accepted).Sum(i => i.ExposureDuration),
                    avgHfr = lightImages.Where(i => i.HFR > 0).Select(i => i.HFR).DefaultIfEmpty(0).Average(),
                    avgGuiding = lightImages.Where(i => i.GuidingRMSTotal > 0).Select(i => i.GuidingRMSTotal).DefaultIfEmpty(0).Average(),
                    hasReport = File.Exists(Path.Combine(reportsDir, $"{s.SessionId}.html"))
                };
            }).ToList();

            await WriteJson(res, 200, result);
            done?.Invoke(200, $"{result.Count} sessions");
        }

        private async Task HandleGetSession(HttpListenerResponse res, string sessionId, Action<int, string> done) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 404, new { error = "Database not found" });
                done?.Invoke(404, "no db");
                return;
            }

            var db = new SessionDatabase(dbPath);
            var session = db.GetSession(sessionId);
            if (session == null) {
                await WriteJson(res, 404, new { error = "Session not found" });
                done?.Invoke(404, sessionId);
                return;
            }

            var images = db.GetImagesForSession(sessionId);
            var lightImages = images.Where(i => string.IsNullOrEmpty(i.ImageType) || i.ImageType == "LIGHT").ToList();
            var events = db.GetEventsForSession(sessionId);

            var targetBreakdown = lightImages
                .Where(i => !string.IsNullOrEmpty(i.TargetName))
                .GroupBy(i => i.TargetName)
                .Select(g => new {
                    target = g.Key,
                    imageCount = g.Count(),
                    accepted = g.Count(i => i.Accepted),
                    rejected = g.Count(i => !i.Accepted),
                    integrationSeconds = g.Where(i => i.Accepted).Sum(i => i.ExposureDuration),
                    avgHfr = g.Where(i => i.HFR > 0).Select(i => i.HFR).DefaultIfEmpty(0).Average(),
                    avgFwhm = g.Where(i => i.FWHM > 0).Select(i => i.FWHM).DefaultIfEmpty(0).Average(),
                    avgGuiding = g.Where(i => i.GuidingRMSTotal > 0).Select(i => i.GuidingRMSTotal).DefaultIfEmpty(0).Average(),
                    avgStarCount = g.Where(i => i.StarCount > 0).Select(i => (double)i.StarCount).DefaultIfEmpty(0).Average(),
                    filters = g.GroupBy(i => i.Filter ?? "Unknown").Select(fg => new {
                        filter = fg.Key,
                        count = fg.Count(),
                        accepted = fg.Count(i => i.Accepted),
                        integrationSeconds = fg.Where(i => i.Accepted).Sum(i => i.ExposureDuration)
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
                    accepted = lightImages.Count(i => i.Accepted),
                    rejected = lightImages.Count(i => !i.Accepted),
                    totalIntegrationSeconds = lightImages.Where(i => i.Accepted).Sum(i => i.ExposureDuration),
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

        private async Task HandleGetSessionImages(HttpListenerResponse res, string sessionId, Action<int, string> done) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, Array.Empty<object>());
                done?.Invoke(200, "0 images (no db)");
                return;
            }

            var db = new SessionDatabase(dbPath);
            var images = db.GetImagesForSession(sessionId);
            var result = images.Select(i => new {
                id = i.Id,
                timestamp = i.Timestamp.ToString("o"),
                targetName = i.TargetName,
                filter = i.Filter,
                exposureDuration = i.ExposureDuration,
                imageType = i.ImageType,
                accepted = i.Accepted,
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
                gradingStatus = i.GradingStatus,
                rejectReason = i.RejectReason
            }).ToList();

            await WriteJson(res, 200, result);
            done?.Invoke(200, $"{result.Count} images for {sessionId}");
        }

        private async Task HandleGetSessionEvents(HttpListenerResponse res, string sessionId, Action<int, string> done) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, Array.Empty<object>());
                done?.Invoke(200, "0 events (no db)");
                return;
            }

            var db = new SessionDatabase(dbPath);
            var events = db.GetEventsForSession(sessionId);
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

        private async Task HandleGetSessionTiming(HttpListenerResponse res, string sessionId, Action<int, string> done) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, Array.Empty<object>());
                done?.Invoke(200, "0 timing events (no db)");
                return;
            }

            var db = new SessionDatabase(dbPath);
            var events = db.GetTimingEventsForSession(sessionId);
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

        private async Task HandleGetSessionReport(HttpListenerResponse res, string sessionId, Action<int, string> done) {
            var reportPath = Path.Combine(reportsDir, $"{sessionId}.html");
            if (!File.Exists(reportPath)) {
                await WriteJson(res, 404, new { error = "Report not found" });
                done?.Invoke(404, sessionId);
                return;
            }
            var html = File.ReadAllText(reportPath);
            await WriteHtml(res, 200, html);
            done?.Invoke(200, $"{sessionId} ({html.Length / 1024}KB)");
        }

        private async Task HandleGetTargetStats(HttpListenerResponse res, Action<int, string> done) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, new { targets = Array.Empty<object>() });
                done?.Invoke(200, "0 targets (no db)");
                return;
            }

            var db = new SessionDatabase(dbPath);
            var cumulative = db.GetCumulativeIntegrationByTarget(null);
            var result = cumulative.Select(kv => new {
                target = kv.Key,
                totalIntegrationSeconds = kv.Value,
                totalIntegrationHours = Math.Round(kv.Value / 3600.0, 2)
            }).OrderByDescending(t => t.totalIntegrationSeconds).ToList();

            await WriteJson(res, 200, new { targets = result });
            done?.Invoke(200, $"{result.Count} targets");
        }

        // ── Settings & Regeneration ──────────────────────────────────────────

        private async Task HandleGetFilters(HttpListenerResponse res, Action<int, string> done) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, new { filters = Array.Empty<string>() });
                done?.Invoke(200, "0 filters (no db)");
                return;
            }
            var db = new SessionDatabase(dbPath);
            var sessions = db.GetAllSessions();
            var filters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in sessions) {
                var images = db.GetImagesForSession(s.SessionId);
                foreach (var img in images) {
                    if (!string.IsNullOrEmpty(img.Filter)) filters.Add(img.Filter);
                }
            }
            var sorted = filters.OrderBy(f => f).ToList();
            await WriteJson(res, 200, new { filters = sorted });
            done?.Invoke(200, $"{sorted.Count} filters");
        }

        private async Task HandleGetSettings(HttpListenerResponse res) {
            var s = SettingsManager.Instance.Current;
            await WriteJson(res, 200, new {
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
                showPerTargetIQ        = s.ShowPerTargetIQ,
                showEquipmentProfile   = s.ShowEquipmentProfile,
                chartXAxisMetric       = s.ChartXAxisMetric,
                chartPrimaryMetric     = s.ChartPrimaryMetric,
                chartSecondaryMetric   = s.ChartSecondaryMetric,
                additionalChartConfigs = s.AdditionalChartConfigs,
                equipmentVisibleFields = s.EquipmentVisibleFields,
                filterClassifications  = s.FilterClassifications,
                equipmentOverrides     = s.EquipmentOverrides
            });
        }

        private async Task HandleRegenerateReport(HttpListenerRequest req, HttpListenerResponse res, string sessionId, Action<int, string> done) {
            if (sessionService == null) {
                await WriteJson(res, 500, new { error = "Report generation not available" });
                done?.Invoke(500, "no session service");
                return;
            }

            try {
                log?.Info($"Regenerating report for {sessionId}");

                // Read settings overrides from POST body
                string body = "";
                using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
                    body = await reader.ReadToEndAsync();

                var overrides = string.IsNullOrEmpty(body) ? null :
                    JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body);

                if (overrides != null)
                    log?.Debug($"Regenerate {sessionId}: {overrides.Count} setting overrides");

                // Save current settings, apply overrides, generate, restore
                var s = SettingsManager.Instance.Current;
                var saved = SnapshotSettings(s);

                try {
                    ApplyOverrides(s, overrides);
                    s.ShowNextNightPreview = false; // Always off for dashboard
                    log?.Debug($"Regenerate {sessionId} effective settings: {FormatSettingsForLog(s)}");

                    var reportData = await sessionService.BuildReportDataAsync(dbPath, sessionId);
                    if (reportData == null) {
                        await WriteJson(res, 404, new { error = "Session not found" });
                        done?.Invoke(404, sessionId);
                        return;
                    }

                    var html = await sessionService.GenerateHtmlAsync(reportData);
                    var reportPath = Path.Combine(reportsDir, $"{sessionId}.html");
                    await File.WriteAllTextAsync(reportPath, html);
                    await SaveSessionSettings(sessionId, s);

                    log?.Info($"Regenerated report for {sessionId} ({html.Length / 1024}KB)");
                    Logger.Info($"NightSummary: Dashboard regenerated report for {sessionId}");
                    await WriteJson(res, 200, new { status = "ok", sessionId });
                    done?.Invoke(200, sessionId);
                } finally {
                    RestoreSettings(s, saved);
                }
            } catch (Exception ex) {
                log?.Error($"Regeneration failed for {sessionId}", ex);
                Logger.Error($"NightSummary: Dashboard report regeneration failed. {ex.Message}");
                await WriteJson(res, 500, new { error = ex.Message });
                done?.Invoke(500, ex.Message);
            }
        }

        private async Task HandleRegenerateAll(HttpListenerRequest req, HttpListenerResponse res, Action<int, string> done) {
            if (sessionService == null) {
                await WriteJson(res, 500, new { error = "Report generation not available" });
                done?.Invoke(500, "no session service");
                return;
            }

            if (regenAllRunning) {
                await WriteJson(res, 409, new { error = "Regeneration already in progress" });
                done?.Invoke(409, "already running");
                return;
            }

            try {
                string body = "";
                using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
                    body = await reader.ReadToEndAsync();

                var overrides = string.IsNullOrEmpty(body) ? null :
                    JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body);

                if (!File.Exists(dbPath)) {
                    await WriteJson(res, 404, new { error = "Database not found" });
                    done?.Invoke(404, "no db");
                    return;
                }

                var db = new SessionDatabase(dbPath);
                var sessions = db.GetAllSessions();

                // Initialize progress
                regenAllRunning = true;
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
                    var s = SettingsManager.Instance.Current;
                    var saved = SnapshotSettings(s);
                    try {
                        ApplyOverrides(s, overrides);
                        s.ShowNextNightPreview = false;

                        for (int i = 0; i < sessions.Count; i++) {
                            regenAllCurrent = i + 1;
                            try {
                                var reportData = await sessionService.BuildReportDataAsync(dbPath, sessions[i].SessionId);
                                if (reportData == null) { regenAllFailed++; continue; }

                                var html = await sessionService.GenerateHtmlAsync(reportData);
                                var reportPath = Path.Combine(reportsDir, $"{sessions[i].SessionId}.html");
                                await File.WriteAllTextAsync(reportPath, html);
                                await SaveSessionSettings(sessions[i].SessionId, s);
                                regenAllGenerated++;
                                log?.Debug($"Bulk regen {regenAllCurrent}/{sessions.Count}: {sessions[i].SessionId} OK");
                            } catch (Exception ex) {
                                log?.Warn($"Bulk regen {regenAllCurrent}/{sessions.Count}: {sessions[i].SessionId} FAILED — {ex.Message}");
                                Logger.Warning($"NightSummary: Failed to regenerate report for {sessions[i].SessionId}. {ex.Message}");
                                regenAllFailed++;
                            }
                        }

                        regenAllStatus = "done";
                        log?.Info($"Bulk regeneration complete — {regenAllGenerated} generated, {regenAllFailed} failed");
                        Logger.Info($"NightSummary: Dashboard bulk regeneration complete — {regenAllGenerated} generated, {regenAllFailed} failed");
                    } catch (Exception ex) {
                        regenAllStatus = "error";
                        regenAllError = ex.Message;
                        log?.Error("Bulk regeneration failed", ex);
                        Logger.Error($"NightSummary: Dashboard bulk regeneration failed. {ex.Message}");
                    } finally {
                        RestoreSettings(s, saved);
                        regenAllRunning = false;
                    }
                });
            } catch (Exception ex) {
                regenAllRunning = false;
                regenAllStatus = "error";
                regenAllError = ex.Message;
                log?.Error("Bulk regeneration failed to start", ex);
                Logger.Error($"NightSummary: Dashboard bulk regeneration failed. {ex.Message}");
                await WriteJson(res, 500, new { error = ex.Message });
                done?.Invoke(500, ex.Message);
            }
        }

        private async Task HandleRegenAllStatus(HttpListenerResponse res) {
            await WriteJson(res, 200, new {
                status = regenAllStatus ?? "idle",
                current = regenAllCurrent,
                total = regenAllTotal,
                generated = regenAllGenerated,
                failed = regenAllFailed,
                error = regenAllError
            });
        }

        private async Task HandleGetSessionSettings(HttpListenerResponse res, string sessionId, Action<int, string> done) {
            var settingsPath = Path.Combine(reportsDir, $"{sessionId}.settings.json");
            if (File.Exists(settingsPath)) {
                var json = await File.ReadAllTextAsync(settingsPath);
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
                log?.Debug($"Settings for {sessionId} (no sidecar, using plugin defaults): {FormatSettingsForLog(SettingsManager.Instance.Current)}");
                await HandleGetSettings(res);
                done?.Invoke(200, $"{sessionId} (plugin defaults — no sidecar)");
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
                    showPerTargetIQ        = s.ShowPerTargetIQ,
                    showEquipmentProfile   = s.ShowEquipmentProfile,
                    chartXAxisMetric       = s.ChartXAxisMetric,
                    chartPrimaryMetric     = s.ChartPrimaryMetric,
                    chartSecondaryMetric   = s.ChartSecondaryMetric,
                    additionalChartConfigs = s.AdditionalChartConfigs,
                    equipmentVisibleFields = s.EquipmentVisibleFields,
                    filterClassifications  = s.FilterClassifications,
                    equipmentOverrides     = s.EquipmentOverrides
                };
                var json = JsonSerializer.Serialize(settings, JsonOpts);
                var settingsPath = Path.Combine(reportsDir, $"{sessionId}.settings.json");
                await File.WriteAllTextAsync(settingsPath, json);
                log?.Debug($"Saved settings sidecar for {sessionId}");
            } catch (Exception ex) {
                log?.Warn($"Failed to save settings sidecar for {sessionId}: {ex.Message}");
                Logger.Warning($"NightSummary: Failed to save settings for {sessionId}. {ex.Message}");
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
                ["ShowPerTargetIQ"]       = s.ShowPerTargetIQ,
                ["ShowNextNightPreview"]  = s.ShowNextNightPreview,
                ["ShowEquipmentProfile"]  = s.ShowEquipmentProfile,
                ["ChartXAxisMetric"]      = s.ChartXAxisMetric,
                ["ChartPrimaryMetric"]    = s.ChartPrimaryMetric,
                ["ChartSecondaryMetric"]  = s.ChartSecondaryMetric,
                ["AdditionalChartConfigs"]= s.AdditionalChartConfigs,
                ["EquipmentVisibleFields"]= s.EquipmentVisibleFields,
                ["FilterClassifications"] = s.FilterClassifications,
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
            s.ShowPerTargetIQ       = (bool)saved["ShowPerTargetIQ"];
            s.ShowNextNightPreview  = (bool)saved["ShowNextNightPreview"];
            s.ShowEquipmentProfile  = (bool)saved["ShowEquipmentProfile"];
            s.ChartXAxisMetric      = (int)saved["ChartXAxisMetric"];
            s.ChartPrimaryMetric    = (int)saved["ChartPrimaryMetric"];
            s.ChartSecondaryMetric  = (int)saved["ChartSecondaryMetric"];
            s.AdditionalChartConfigs= (string)saved["AdditionalChartConfigs"];
            s.EquipmentVisibleFields= (string)saved["EquipmentVisibleFields"];
            s.FilterClassifications = (string)saved["FilterClassifications"];
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
                    case "showPerTargetIQ":        s.ShowPerTargetIQ       = kv.Value.GetBoolean(); break;
                    case "showEquipmentProfile":   s.ShowEquipmentProfile  = kv.Value.GetBoolean(); break;
                    case "chartXAxisMetric":       s.ChartXAxisMetric      = kv.Value.GetInt32(); break;
                    case "chartPrimaryMetric":     s.ChartPrimaryMetric    = kv.Value.GetInt32(); break;
                    case "chartSecondaryMetric":   s.ChartSecondaryMetric  = kv.Value.GetInt32(); break;
                    case "additionalChartConfigs": s.AdditionalChartConfigs= kv.Value.GetString(); break;
                    case "equipmentVisibleFields": s.EquipmentVisibleFields= kv.Value.GetString(); break;
                    case "filterClassifications":  s.FilterClassifications = kv.Value.GetString(); break;
                    case "equipmentOverrides":     s.EquipmentOverrides    = kv.Value.GetString(); break;
                }
            }
        }

        // ── Response Helpers ──────────────────────────────────────────────────

        private static async Task WriteJson(HttpListenerResponse res, int status, object data) {
            res.StatusCode = status;
            res.ContentType = "application/json; charset=utf-8";
            res.Headers.Add("Access-Control-Allow-Origin", "*");
            var json = JsonSerializer.Serialize(data, JsonOpts);
            var bytes = Encoding.UTF8.GetBytes(json);
            res.ContentLength64 = bytes.Length;
            await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            res.Close();
        }

        private static async Task WriteHtml(HttpListenerResponse res, int status, string html) {
            res.StatusCode = status;
            res.ContentType = "text/html; charset=utf-8";
            var bytes = Encoding.UTF8.GetBytes(html);
            res.ContentLength64 = bytes.Length;
            await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            res.Close();
        }

        // ── Stats Summary ──────────────────────────────────────────────────────

        private async Task HandleGetStatsSummary(HttpListenerResponse res, Action<int, string> done) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, new {
                    totalSessions = 0, totalIntegrationHours = 0.0,
                    targetCount = 0, firstSession = (string)null, lastSession = (string)null
                });
                done?.Invoke(200, "empty (no db)");
                return;
            }

            var db = new SessionDatabase(dbPath);
            var sessions = db.GetAllSessions();
            double totalIntegration = 0;
            var allTargets = new HashSet<string>();

            foreach (var s in sessions) {
                var images = db.GetImagesForSession(s.SessionId);
                var lights = images.Where(i => string.IsNullOrEmpty(i.ImageType) || i.ImageType == "LIGHT").ToList();
                totalIntegration += lights.Where(i => i.Accepted).Sum(i => i.ExposureDuration);
                foreach (var t in lights.Where(i => !string.IsNullOrEmpty(i.TargetName)).Select(i => i.TargetName))
                    allTargets.Add(t);
            }

            await WriteJson(res, 200, new {
                totalSessions = sessions.Count,
                totalIntegrationHours = Math.Round(totalIntegration / 3600.0, 1),
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

        // ── Dashboard HTML (from embedded resources) ──────────────────────────

        private string GetDashboardHtml() {
            if (cachedDashboardHtml != null) return cachedDashboardHtml;

            try {
                var asm = Assembly.GetExecutingAssembly();
                var html = ReadResource(asm, "dashboard.html");
                var css = ReadResource(asm, "dashboard.css");
                var js = ReadResource(asm, "dashboard.js");
                cachedDashboardHtml = html
                    .Replace("{{STYLES}}", css)
                    .Replace("{{SCRIPTS}}", js);
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to load dashboard resources. {ex.Message}");
                cachedDashboardHtml = "<!DOCTYPE html><html><body><h1>Dashboard failed to load</h1><p>" +
                    System.Net.WebUtility.HtmlEncode(ex.Message) + "</p></body></html>";
            }

            return cachedDashboardHtml;
        }

        private static string ReadResource(Assembly asm, string name) {
            using var stream = asm.GetManifestResourceStream(name);
            if (stream == null) throw new FileNotFoundException($"Embedded resource '{name}' not found");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
