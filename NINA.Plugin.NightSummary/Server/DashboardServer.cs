using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        public bool IsRunning { get; private set; }
        public string Url { get; private set; }

        public DashboardServer(string dbPath) {
            this.dbPath = dbPath;
            this.reportsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA", "NightSummary", "reports");
            Directory.CreateDirectory(reportsDir);
        }

        public Task StartAsync(int port) {
            if (IsRunning) return Task.CompletedTask;

            try {
                cts = new CancellationTokenSource();
                listener = new HttpListener();
                listener.Prefixes.Add($"http://+:{port}/");
                listener.Start();

                var hostname = Dns.GetHostName();
                Url = $"http://{hostname}:{port}";
                IsRunning = true;

                // Fire-and-forget the request loop
                _ = AcceptLoop(cts.Token);

                Logger.Info($"NightSummary: Local dashboard started at {Url}");
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to start local dashboard. {ex.Message}");
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
                Logger.Info("NightSummary: Local dashboard stopped");
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
                    Logger.Error($"NightSummary: Dashboard accept error. {ex.Message}");
                }
            }
        }

        private async Task HandleRequest(HttpListenerContext context) {
            var req = context.Request;
            var res = context.Response;

            try {
                var path = req.Url.AbsolutePath.TrimEnd('/');
                if (string.IsNullOrEmpty(path)) path = "/";

                if (req.HttpMethod == "GET") {
                    if (path == "/api/health") {
                        await WriteJson(res, 200, new { status = "ok" });
                    } else if (path == "/api/sessions") {
                        await HandleGetSessions(res);
                    } else if (path.StartsWith("/api/sessions/") && path.EndsWith("/images")) {
                        var sessionId = ExtractSessionId(path, "/images");
                        await HandleGetSessionImages(res, sessionId);
                    } else if (path.StartsWith("/api/sessions/") && path.EndsWith("/events")) {
                        var sessionId = ExtractSessionId(path, "/events");
                        await HandleGetSessionEvents(res, sessionId);
                    } else if (path.StartsWith("/api/sessions/") && path.EndsWith("/timing")) {
                        var sessionId = ExtractSessionId(path, "/timing");
                        await HandleGetSessionTiming(res, sessionId);
                    } else if (path.StartsWith("/api/sessions/") && path.EndsWith("/report")) {
                        var sessionId = ExtractSessionId(path, "/report");
                        await HandleGetSessionReport(res, sessionId);
                    } else if (path.StartsWith("/api/sessions/") && !path.Substring("/api/sessions/".Length).Contains("/")) {
                        var sessionId = path.Substring("/api/sessions/".Length);
                        await HandleGetSession(res, sessionId);
                    } else if (path == "/api/stats/targets") {
                        await HandleGetTargetStats(res);
                    } else if (path == "/") {
                        await WriteHtml(res, 200, BuildDashboardHtml());
                    } else {
                        await WriteJson(res, 404, new { error = "Not found" });
                    }
                } else {
                    res.StatusCode = 405;
                    res.Close();
                }
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Dashboard request error for {req.Url}. {ex.Message}");
                try { await WriteJson(res, 500, new { error = ex.Message }); } catch { res.Close(); }
            }
        }

        private string ExtractSessionId(string path, string suffix) {
            var start = "/api/sessions/".Length;
            var end = path.Length - suffix.Length;
            return path.Substring(start, end - start);
        }

        // ── API Handlers ──────────────────────────────────────────────────────

        private async Task HandleGetSessions(HttpListenerResponse res) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, Array.Empty<object>());
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
        }

        private async Task HandleGetSession(HttpListenerResponse res, string sessionId) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 404, new { error = "Database not found" });
                return;
            }

            var db = new SessionDatabase(dbPath);
            var session = db.GetSession(sessionId);
            if (session == null) {
                await WriteJson(res, 404, new { error = "Session not found" });
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
        }

        private async Task HandleGetSessionImages(HttpListenerResponse res, string sessionId) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, Array.Empty<object>());
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
        }

        private async Task HandleGetSessionEvents(HttpListenerResponse res, string sessionId) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, Array.Empty<object>());
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
        }

        private async Task HandleGetSessionTiming(HttpListenerResponse res, string sessionId) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, Array.Empty<object>());
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
        }

        private async Task HandleGetSessionReport(HttpListenerResponse res, string sessionId) {
            var reportPath = Path.Combine(reportsDir, $"{sessionId}.html");
            if (!File.Exists(reportPath)) {
                await WriteJson(res, 404, new { error = "Report not found" });
                return;
            }
            var html = File.ReadAllText(reportPath);
            await WriteHtml(res, 200, html);
        }

        private async Task HandleGetTargetStats(HttpListenerResponse res) {
            if (!File.Exists(dbPath)) {
                await WriteJson(res, 200, new { targets = Array.Empty<object>() });
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

        // ── Dashboard SPA ─────────────────────────────────────────────────────

        private string BuildDashboardHtml() {
            return @"<!DOCTYPE html>
<html lang='en'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<title>Night Summary Dashboard</title>
<style>
:root {
  --bg: #0d1117;
  --surface: #161b22;
  --border: #30363d;
  --text: #c9d1d9;
  --text-muted: #8b949e;
  --accent: #58a6ff;
  --green: #3fb950;
  --red: #f85149;
  --yellow: #d29922;
}
* { box-sizing: border-box; margin: 0; padding: 0; }
body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; background: var(--bg); color: var(--text); }

/* ── Layout ── */
.container { max-width: 1200px; margin: 0 auto; padding: 20px; }
header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 24px; flex-wrap: wrap; gap: 12px; }
header h1 { color: var(--accent); font-size: 22px; }
.subtitle { color: var(--text-muted); font-size: 13px; margin-top: 2px; }
.back-btn { background: var(--surface); border: 1px solid var(--border); color: var(--accent); padding: 6px 14px; border-radius: 6px; cursor: pointer; font-size: 13px; display: none; }
.back-btn:hover { border-color: var(--accent); }

/* ── Session list ── */
.session-card { background: var(--surface); border: 1px solid var(--border); border-radius: 8px; padding: 16px; margin-bottom: 12px; cursor: pointer; transition: border-color 0.2s; }
.session-card:hover { border-color: var(--accent); }
.session-header { display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 8px; }
.session-date { font-size: 16px; font-weight: 600; color: #e6edf3; }
.badge { font-size: 11px; padding: 2px 8px; border-radius: 10px; }
.badge-green { background: rgba(63,185,80,0.15); color: var(--green); }
.badge-red { background: rgba(248,81,73,0.15); color: var(--red); }
.session-meta { display: flex; gap: 20px; margin-top: 8px; font-size: 13px; color: var(--text-muted); flex-wrap: wrap; }
.stat-label { color: var(--text-muted); }
.stat-value { color: var(--text); font-weight: 500; }
.targets-row { margin-top: 6px; font-size: 13px; color: var(--text-muted); }

/* ── Session detail ── */
.detail-section { background: var(--surface); border: 1px solid var(--border); border-radius: 8px; padding: 20px; margin-bottom: 16px; }
.detail-section h2 { font-size: 16px; color: var(--accent); margin-bottom: 12px; }
.detail-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 12px; }
.detail-item { }
.detail-item .label { font-size: 11px; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.5px; }
.detail-item .value { font-size: 15px; color: var(--text); margin-top: 2px; }

/* ── Target cards ── */
.target-card { background: var(--bg); border: 1px solid var(--border); border-radius: 6px; padding: 14px; margin-bottom: 10px; }
.target-card h3 { font-size: 14px; color: #e6edf3; margin-bottom: 8px; }
.target-stats { display: flex; gap: 16px; flex-wrap: wrap; font-size: 13px; }
.filter-pills { display: flex; gap: 6px; flex-wrap: wrap; margin-top: 8px; }
.filter-pill { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 2px 10px; font-size: 11px; color: var(--text-muted); }

/* ── Image table ── */
.image-table { width: 100%; border-collapse: collapse; font-size: 13px; }
.image-table th { text-align: left; padding: 8px 12px; border-bottom: 1px solid var(--border); color: var(--text-muted); font-size: 11px; text-transform: uppercase; letter-spacing: 0.5px; position: sticky; top: 0; background: var(--surface); }
.image-table td { padding: 6px 12px; border-bottom: 1px solid rgba(48,54,61,0.5); }
.image-table tr:hover td { background: rgba(88,166,255,0.05); }
.table-scroll { max-height: 500px; overflow-y: auto; border-radius: 6px; }

/* ── Loading / empty ── */
.loading { text-align: center; padding: 40px; color: var(--text-muted); }
.empty { text-align: center; padding: 60px 20px; color: var(--text-muted); }
.error { text-align: center; padding: 40px; color: var(--red); }

/* ── Responsive ── */
@media (max-width: 600px) {
  .container { padding: 12px; }
  header h1 { font-size: 18px; }
  .session-meta { gap: 12px; }
  .detail-grid { grid-template-columns: repeat(2, 1fr); }
}
</style>
</head>
<body>
<div class='container'>
  <header>
    <div>
      <h1>Night Summary</h1>
      <div class='subtitle' id='page-subtitle'>Session history</div>
    </div>
    <button class='back-btn' id='back-btn' onclick='showList()'>&#8592; All Sessions</button>
  </header>
  <div id='content'>
    <div class='loading'>Loading sessions...</div>
  </div>
</div>

<script>
let sessionsCache = [];

function fmt(seconds) {
  const h = (seconds / 3600).toFixed(1);
  return h + 'h';
}

function fmtNum(n, decimals) {
  if (n == null || n === 0) return '--';
  return Number(n).toFixed(decimals || 2);
}

function fmtDate(iso) {
  const d = new Date(iso);
  return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

function fmtTime(iso) {
  const d = new Date(iso);
  return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
}

function fmtDuration(startIso, endIso) {
  const ms = new Date(endIso) - new Date(startIso);
  const h = Math.floor(ms / 3600000);
  const m = Math.floor((ms % 3600000) / 60000);
  return h > 0 ? h + 'h ' + m + 'm' : m + 'm';
}

// ── Session List ──

async function loadSessions() {
  try {
    const res = await fetch('/api/sessions');
    sessionsCache = await res.json();
    renderSessionList();
  } catch (err) {
    document.getElementById('content').innerHTML =
      ""<div class='error'>Failed to load sessions: "" + err.message + '</div>';
  }
}

function renderSessionList() {
  const el = document.getElementById('content');
  document.getElementById('back-btn').style.display = 'none';
  document.getElementById('page-subtitle').textContent = sessionsCache.length + ' sessions';

  if (sessionsCache.length === 0) {
    el.innerHTML = ""<div class='empty'>No sessions recorded yet.</div>"";
    return;
  }

  el.innerHTML = sessionsCache.map(s => {
    const duration = fmtDuration(s.sessionStart, s.sessionEnd);
    const targets = s.targets.length > 0 ? s.targets.join(', ') : 'No targets';
    const badge = s.hasReport
      ? ""<span class='badge badge-green'>Report</span>""
      : ""<span class='badge badge-red'>No report</span>"";

    return ""<div class='session-card' onclick='showSession(\"""" + s.sessionId + ""\"")'>""
      + ""<div class='session-header'>""
      + ""<span class='session-date'>"" + fmtDate(s.sessionStart) + '  ' + fmtTime(s.sessionStart) + '</span>'
      + badge
      + '</div>'
      + ""<div class='session-meta'>""
      + ""<span><span class='stat-label'>Profile </span><span class='stat-value'>"" + (s.profileName || 'Unknown') + '</span></span>'
      + ""<span><span class='stat-label'>Images </span><span class='stat-value'>"" + s.imageCount + '</span></span>'
      + ""<span><span class='stat-label'>Duration </span><span class='stat-value'>"" + duration + '</span></span>'
      + ""<span><span class='stat-label'>Integration </span><span class='stat-value'>"" + fmt(s.totalIntegrationSeconds) + '</span></span>'
      + ""<span><span class='stat-label'>HFR </span><span class='stat-value'>"" + fmtNum(s.avgHfr) + '</span></span>'
      + ""<span><span class='stat-label'>Guiding </span><span class='stat-value'>"" + fmtNum(s.avgGuiding) + '""</span></span>'
      + '</div>'
      + ""<div class='targets-row'>Targets: "" + targets + '</div>'
      + '</div>';
  }).join('');
}

function showList() {
  window.location.hash = '';
  renderSessionList();
}

// ── Session Detail ──

async function showSession(sessionId) {
  window.location.hash = sessionId;
  const el = document.getElementById('content');
  el.innerHTML = ""<div class='loading'>Loading session...</div>"";
  document.getElementById('back-btn').style.display = 'block';

  try {
    const [detailRes, imagesRes, eventsRes] = await Promise.all([
      fetch('/api/sessions/' + sessionId),
      fetch('/api/sessions/' + sessionId + '/images'),
      fetch('/api/sessions/' + sessionId + '/events')
    ]);

    const detail = await detailRes.json();
    const images = await imagesRes.json();
    const events = await eventsRes.json();

    document.getElementById('page-subtitle').textContent =
      fmtDate(detail.sessionStart) + ' \u2014 ' + (detail.profileName || 'Unknown profile');

    let html = '';

    // Summary section
    html += ""<div class='detail-section'><h2>Summary</h2><div class='detail-grid'>"";
    html += detailItem('Duration', fmtDuration(detail.sessionStart, detail.sessionEnd));
    html += detailItem('Images', detail.summary.totalImages + ' (' + detail.summary.accepted + ' accepted)');
    html += detailItem('Integration', fmt(detail.summary.totalIntegrationSeconds));
    html += detailItem('Avg HFR', fmtNum(detail.summary.avgHfr));
    html += detailItem('Avg FWHM', fmtNum(detail.summary.avgFwhm));
    html += detailItem('Avg Guiding', fmtNum(detail.summary.avgGuiding) + ' ""');
    html += detailItem('Avg Stars', fmtNum(detail.summary.avgStarCount, 0));
    html += detailItem('AutoFocus Runs', detail.summary.autoFocusRuns);
    html += detailItem('Meridian Flips', detail.summary.meridianFlips);
    if (detail.skippedExposures > 0) html += detailItem('Skipped', detail.skippedExposures);
    html += '</div></div>';

    // Equipment section
    const eq = detail.equipment;
    const eqEntries = Object.entries(eq).filter(([,v]) => v);
    if (eqEntries.length > 0) {
      html += ""<div class='detail-section'><h2>Equipment</h2><div class='detail-grid'>"";
      eqEntries.forEach(([k, v]) => {
        const label = k.replace(/([A-Z])/g, ' $1').replace(/^./, c => c.toUpperCase()).trim();
        html += detailItem(label, v);
      });
      html += '</div></div>';
    }

    // Targets section
    if (detail.targets.length > 0) {
      html += ""<div class='detail-section'><h2>Targets</h2>"";
      detail.targets.forEach(t => {
        html += ""<div class='target-card'><h3>"" + t.target + '</h3>';
        html += ""<div class='target-stats'>""
          + stat('Images', t.imageCount)
          + stat('Accepted', t.accepted)
          + stat('Integration', fmt(t.integrationSeconds))
          + stat('HFR', fmtNum(t.avgHfr))
          + stat('FWHM', fmtNum(t.avgFwhm))
          + stat('Guiding', fmtNum(t.avgGuiding) + ' ""')
          + stat('Stars', fmtNum(t.avgStarCount, 0))
          + '</div>';
        if (t.filters && t.filters.length > 0) {
          html += ""<div class='filter-pills'>"";
          t.filters.forEach(f => {
            html += ""<span class='filter-pill'>"" + f.filter + ': ' + f.accepted + '/' + f.count + ' (' + fmt(f.integrationSeconds) + ')</span>';
          });
          html += '</div>';
        }
        html += '</div>';
      });
      html += '</div>';
    }

    // Events section
    if (events.length > 0) {
      html += ""<div class='detail-section'><h2>Events</h2>"";
      html += ""<div class='table-scroll'><table class='image-table'>"";
      html += '<thead><tr><th>Time</th><th>Type</th><th>Details</th></tr></thead><tbody>';
      events.forEach(e => {
        let desc = e.description || '';
        if (e.eventType === 'AutoFocus') {
          desc = (e.afSucceeded ? 'Success' : 'Failed') + (e.afHfr > 0 ? ' \u2014 HFR: ' + fmtNum(e.afHfr) : '');
        }
        html += '<tr><td>' + fmtTime(e.timestamp) + '</td><td>' + e.eventType + '</td><td>' + desc + '</td></tr>';
      });
      html += '</tbody></table></div></div>';
    }

    // Images table
    if (images.length > 0) {
      html += ""<div class='detail-section'><h2>Images ("" + images.length + ')</h2>';
      html += ""<div class='table-scroll'><table class='image-table'>"";
      html += '<thead><tr><th>Time</th><th>Target</th><th>Filter</th><th>Exp</th><th>HFR</th><th>FWHM</th><th>Stars</th><th>Guiding</th><th>Alt</th><th>Status</th></tr></thead><tbody>';
      images.forEach(i => {
        const status = i.accepted ? ""<span style='color:var(--green)'>OK</span>"" : ""<span style='color:var(--red)'>Rejected</span>"";
        html += '<tr>'
          + '<td>' + fmtTime(i.timestamp) + '</td>'
          + '<td>' + (i.targetName || '--') + '</td>'
          + '<td>' + (i.filter || '--') + '</td>'
          + '<td>' + (i.exposureDuration || '--') + 's</td>'
          + '<td>' + fmtNum(i.hfr) + '</td>'
          + '<td>' + fmtNum(i.fwhm) + '</td>'
          + '<td>' + (i.starCount > 0 ? i.starCount : '--') + '</td>'
          + '<td>' + fmtNum(i.guidingRmsTotal) + '</td>'
          + '<td>' + fmtNum(i.altitude, 1) + '\u00b0</td>'
          + '<td>' + status + '</td>'
          + '</tr>';
      });
      html += '</tbody></table></div></div>';
    }

    // Report link
    if (detail.hasReport) {
      html += ""<div style='text-align:center;margin:16px 0'>"";
      html += ""<a href='/api/sessions/"" + sessionId + ""/report' target='_blank' style='color:var(--accent);font-size:14px'>View Full Report \u2192</a>"";
      html += '</div>';
    }

    el.innerHTML = html;
  } catch (err) {
    el.innerHTML = ""<div class='error'>Failed to load session: "" + err.message + '</div>';
  }
}

function detailItem(label, value) {
  return ""<div class='detail-item'><div class='label'>"" + label + ""</div><div class='value'>"" + (value != null ? value : '--') + '</div></div>';
}

function stat(label, value) {
  return ""<span><span class='stat-label'>"" + label + "" </span><span class='stat-value'>"" + (value != null ? value : '--') + '</span></span>';
}

// ── Routing ──

function handleHash() {
  const hash = window.location.hash.substring(1);
  if (hash) {
    showSession(hash);
  } else {
    if (sessionsCache.length > 0) {
      renderSessionList();
    } else {
      loadSessions();
    }
  }
}

window.addEventListener('hashchange', handleHash);
loadSessions().then(() => {
  if (window.location.hash.length > 1) {
    handleHash();
  }
});
</script>
</body>
</html>";
        }
    }
}
