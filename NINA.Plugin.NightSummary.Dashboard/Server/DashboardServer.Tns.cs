using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Server {

    // ── Touch 'N' Stars compat namespace ─────────────────────────────────────
    // /api/nightsummary/* — a small, stable surface consumed by the Touch 'N'
    // Stars web app (see TNS_INTEGRATION.md). TNS treats Night Summary as a
    // report delivery channel: list sessions, display the actual report HTML,
    // resend or delete. Responses use TNS's envelope convention
    // { Success, Response, Error, StatusCode, Type } with PascalCase property
    // names — hence the separate serializer options; the rest of the dashboard
    // API stays camelCase.
    public partial class DashboardServer {

        private static readonly JsonSerializerOptions TnsJsonOpts = new JsonSerializerOptions {
            PropertyNamingPolicy = null, // preserve C# PascalCase (TNS contract)
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        private sealed class TnsEnvelope {
            public bool Success { get; set; }
            public object? Response { get; set; }
            public string Error { get; set; } = "";
            public int StatusCode { get; set; }
            public string Type { get; set; } = "NightSummary";
        }

        private static Task WriteTnsOk(TcpHttpResponse res, object response, int status = 200) =>
            WriteJsonRaw(res, status, JsonSerializer.Serialize(
                new TnsEnvelope { Success = true, Response = response, StatusCode = status }, TnsJsonOpts));

        private static Task WriteTnsError(TcpHttpResponse res, int status, string error) =>
            WriteJsonRaw(res, status, JsonSerializer.Serialize(
                new TnsEnvelope { Success = false, Error = error, StatusCode = status }, TnsJsonOpts));

        private async Task HandleTnsGet(TcpHttpRequest req, TcpHttpResponse res, string path, Action<int, string> done) {
            if (path == "/api/nightsummary/status") {
                await HandleTnsStatus(res, done);
            } else if (path == "/api/nightsummary/sessions") {
                await HandleTnsSessions(req, res, done);
            } else if (path.StartsWith("/api/nightsummary/report/", StringComparison.Ordinal)) {
                // The report is the one non-JSON endpoint: it streams the exact
                // self-contained HTML file NS generated for the session, with the
                // same optional ?theme= injection the dashboard's own report view
                // uses (iframe-friendly overscroll CSS included).
                var sessionId = path.Substring("/api/nightsummary/report/".Length);
                if (!File.Exists(Path.Combine(reportsDir, $"{sessionId}.html"))) {
                    // Envelope the 404 (the delegate below emits the dashboard's
                    // camelCase error shape, which TNS clients don't parse).
                    await WriteTnsError(res, 404, "Report not found");
                    done?.Invoke(404, sessionId);
                    return;
                }
                await HandleGetSessionReport(res, sessionId, req.QueryString["theme"], done);
            } else {
                await WriteTnsError(res, 404, "Not found");
                done?.Invoke(404, null);
            }
        }

        private async Task HandleTnsPost(TcpHttpRequest req, TcpHttpResponse res, string path, Action<int, string> done) {
            if (path.StartsWith("/api/nightsummary/sessions/", StringComparison.Ordinal)
                && path.EndsWith("/resend", StringComparison.Ordinal)) {
                var start = "/api/nightsummary/sessions/".Length;
                var sessionId = path.Substring(start, path.Length - start - "/resend".Length);
                await HandleTnsResend(res, sessionId, done);
            } else {
                await WriteTnsError(res, 404, "Not found");
                done?.Invoke(404, null);
            }
        }

        private async Task HandleTnsDelete(TcpHttpResponse res, string path, Action<int, string> done) {
            if (path.StartsWith("/api/nightsummary/sessions/", StringComparison.Ordinal)) {
                var sessionId = path.Substring("/api/nightsummary/sessions/".Length);
                await HandleTnsDeleteSession(res, sessionId, done);
            } else {
                await WriteTnsError(res, 404, "Not found");
                done?.Invoke(404, null);
            }
        }

        private async Task HandleTnsStatus(TcpHttpResponse res, Action<int, string> done) {
            int sessionCount = 0;
            if (File.Exists(dbPath)) {
                try { sessionCount = DbSessions().Count(s => s.SessionEnd > s.SessionStart); } catch { }
            }
            await WriteTnsOk(res, new {
                Installed = true,
                Version = string.IsNullOrEmpty(_settings.PluginVersion)
                              ? GetServerAssemblyVersion()
                              : _settings.PluginVersion,
                ReadOnly = _readOnly,
                CanResendAndDelete = !_readOnly && _maintenance != null,
                SessionCount = sessionCount
            });
            done?.Invoke(200, "tns status");
        }

        private async Task HandleTnsSessions(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            if (!File.Exists(dbPath)) {
                await WriteTnsOk(res, Array.Empty<object>());
                done?.Invoke(200, "tns sessions (no db)");
                return;
            }

            int limit = 100;
            if (int.TryParse(req.QueryString["limit"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) && l > 0) {
                limit = Math.Min(l, 1000);
            }

            // Same completed-session gate as HandleGetSessions: in-progress rows
            // have no report and no finalized data.
            var completed = DbSessions()
                .Where(s => s.SessionEnd > s.SessionStart)
                .OrderByDescending(s => s.SessionStart)
                .Take(limit)
                .ToList();

            var result = completed.Select(s => {
                var lights = DbImages(s.SessionId)
                    .Where(i => string.IsNullOrEmpty(i.ImageType) || i.ImageType == "LIGHT")
                    .ToList();
                var targets = lights.Where(i => !string.IsNullOrEmpty(i.TargetName))
                                    .Select(i => i.TargetName).Distinct().ToList();
                var expSec = lights.Where(i => i.CountsAsAccepted).Sum(i => i.ExposureDuration);
                return new {
                    s.SessionId,
                    SessionDate  = s.SessionStart.ToString("o"),
                    SessionStart = s.SessionStart.ToString("o"),
                    SessionEnd   = s.SessionEnd.ToString("o"),
                    DisplayLabel = BuildTnsSessionLabel(s.SessionStart, targets, lights.Count, expSec),
                    s.ProfileName,
                    Targets      = targets,
                    ImageCount   = lights.Count,
                    TotalExposureSeconds = expSec,
                    HasReport    = File.Exists(Path.Combine(reportsDir, $"{s.SessionId}.html"))
                };
            }).ToList();

            await WriteTnsOk(res, result);
            done?.Invoke(200, $"tns sessions ({result.Count})");
        }

        // "Jul 18 · Seagull Nebula · 142 img · 4.2h" — built server-side so the
        // TNS session picker needs no formatting logic. Invariant culture: the
        // label crosses process/locale boundaries.
        private static string BuildTnsSessionLabel(DateTime start, System.Collections.Generic.List<string> targets, int imageCount, double expSec) {
            var date = start.ToString("MMM d", CultureInfo.InvariantCulture);
            var targetPart = targets.Count == 0 ? "no targets"
                           : targets.Count <= 2 ? string.Join(", ", targets)
                           : $"{targets[0]} +{targets.Count - 1} more";
            var hours = expSec / 3600.0;
            var expPart = hours >= 1
                ? hours.ToString("0.0", CultureInfo.InvariantCulture) + "h"
                : Math.Round(expSec / 60.0).ToString(CultureInfo.InvariantCulture) + "m";
            return $"{date} · {targetPart} · {imageCount} img · {expPart}";
        }

        private async Task HandleTnsResend(TcpHttpResponse res, string sessionId, Action<int, string> done) {
            if (_maintenance == null) {
                await WriteTnsError(res, 501, "Resend is not available on this server");
                done?.Invoke(501, "tns resend unavailable");
                return;
            }
            if (DbSession(sessionId) == null) {
                await WriteTnsError(res, 404, "Session not found");
                done?.Invoke(404, sessionId);
                return;
            }
            try {
                await _maintenance.ResendAsync(sessionId);
                await WriteTnsOk(res, new { Ok = true, Message = "Report sent" });
                done?.Invoke(200, $"tns resend {sessionId}");
            } catch (Exception ex) {
                log?.Error($"TNS resend failed for {sessionId}", ex);
                await WriteTnsError(res, 500, ex.Message);
                done?.Invoke(500, ex.Message);
            }
        }

        private async Task HandleTnsDeleteSession(TcpHttpResponse res, string sessionId, Action<int, string> done) {
            if (_maintenance == null) {
                await WriteTnsError(res, 501, "Delete is not available on this server");
                done?.Invoke(501, "tns delete unavailable");
                return;
            }
            try {
                var deleted = await _maintenance.DeleteAsync(sessionId);
                if (!deleted) {
                    await WriteTnsError(res, 404, "Session not found");
                    done?.Invoke(404, sessionId);
                    return;
                }
                // Drop server-side caches keyed by this session so a stale
                // thumbnail/chart can't outlive the row.
                thumbnailCache.TryRemove(sessionId, out _);
                altitudeChartCache.TryRemove(sessionId, out _);
                livestackCache.TryRemove(sessionId, out _);
                await WriteTnsOk(res, new { Ok = true });
                done?.Invoke(200, $"tns delete {sessionId}");
            } catch (Exception ex) {
                log?.Error($"TNS delete failed for {sessionId}", ex);
                await WriteTnsError(res, 500, ex.Message);
                done?.Invoke(500, ex.Message);
            }
        }
    }
}
