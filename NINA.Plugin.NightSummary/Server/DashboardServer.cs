using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Data;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Server {

    public class DashboardServer {

        private WebApplication app;
        private CancellationTokenSource cts;
        private readonly string dbPath;
        private readonly string reportsDir;

        public bool IsRunning { get; private set; }
        public string Url { get; private set; }

        public DashboardServer(string dbPath) {
            this.dbPath = dbPath;
            this.reportsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA", "NightSummary", "reports");
            Directory.CreateDirectory(reportsDir);
        }

        public async Task StartAsync(int port) {
            if (IsRunning) return;

            try {
                cts = new CancellationTokenSource();

                var builder = WebApplication.CreateSlimBuilder();
                builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
                builder.WebHost.SuppressStatusMessages(true);

                app = builder.Build();

                MapEndpoints();

                await app.StartAsync(cts.Token);

                var hostname = Dns.GetHostName();
                Url = $"http://{hostname}:{port}";
                IsRunning = true;

                Logger.Info($"NightSummary: Dashboard server started at {Url}");
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to start dashboard server. {ex.Message}");
                IsRunning = false;
                throw;
            }
        }

        public async Task StopAsync() {
            if (!IsRunning) return;

            try {
                cts?.Cancel();
                if (app != null) {
                    await app.StopAsync();
                    await app.DisposeAsync();
                }
                app = null;
                IsRunning = false;
                Url = null;
                Logger.Info("NightSummary: Dashboard server stopped");
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Error stopping dashboard server. {ex.Message}");
            }
        }

        public string GetReportsDirectory() => reportsDir;

        private void MapEndpoints() {
            app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

            app.MapGet("/api/sessions", () => {
                try {
                    if (!File.Exists(dbPath)) return Results.Ok(Array.Empty<object>());
                    var db = new SessionDatabase(dbPath);
                    var sessions = db.GetAllSessions();
                    var result = sessions.Select(s => {
                        var images = db.GetImagesForSession(s.SessionId);
                        return new {
                            sessionId = s.SessionId,
                            sessionStart = s.SessionStart.ToString("o"),
                            sessionEnd = s.SessionEnd.ToString("o"),
                            profileName = s.ProfileName,
                            imageCount = images.Count,
                            targets = images.GroupBy(i => i.TargetName).Select(g => g.Key).ToList(),
                            totalIntegrationSeconds = images.Sum(i => i.ExposureDuration),
                            hasReport = File.Exists(Path.Combine(reportsDir, $"{s.SessionId}.html"))
                        };
                    }).ToList();
                    return Results.Ok(result);
                } catch (Exception ex) {
                    Logger.Error($"NightSummary: Dashboard API error. {ex.Message}");
                    return Results.Problem(ex.Message);
                }
            });

            app.MapGet("/api/sessions/{sessionId}/report", (string sessionId) => {
                var reportPath = Path.Combine(reportsDir, $"{sessionId}.html");
                if (!File.Exists(reportPath))
                    return Results.NotFound(new { error = "Report not found" });
                var html = File.ReadAllText(reportPath);
                return Results.Content(html, "text/html");
            });

            app.MapGet("/", () => {
                var html = BuildDashboardHtml();
                return Results.Content(html, "text/html");
            });
        }

        private string BuildDashboardHtml() {
            return @"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<title>Night Summary Dashboard</title>
<style>
  body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; background: #0d1117; color: #c9d1d9; margin: 0; padding: 20px; }
  h1 { color: #58a6ff; margin-bottom: 4px; }
  .subtitle { color: #666; font-size: 13px; margin-bottom: 24px; }
  .session-list { max-width: 900px; }
  .session-card { background: #161b22; border: 1px solid #30363d; border-radius: 8px; padding: 16px; margin-bottom: 12px; cursor: pointer; transition: border-color 0.2s; }
  .session-card:hover { border-color: #58a6ff; }
  .session-header { display: flex; justify-content: space-between; align-items: center; }
  .session-date { font-size: 16px; font-weight: 600; color: #e6edf3; }
  .session-profile { color: #8b949e; font-size: 13px; }
  .session-meta { display: flex; gap: 20px; margin-top: 8px; font-size: 13px; color: #8b949e; }
  .stat { display: flex; align-items: center; gap: 4px; }
  .no-report { color: #f85149; font-size: 12px; font-style: italic; }
  .has-report { color: #3fb950; font-size: 12px; }
  .loading { text-align: center; padding: 40px; color: #666; }
  a { color: #58a6ff; text-decoration: none; }
  a:hover { text-decoration: underline; }
</style>
</head>
<body>
<h1>Night Summary Dashboard</h1>
<div class='subtitle'>Session history from this NINA instance</div>
<div class='session-list' id='sessions'>
  <div class='loading'>Loading sessions...</div>
</div>
<script>
async function loadSessions() {
  try {
    const res = await fetch('/api/sessions');
    const sessions = await res.json();
    const container = document.getElementById('sessions');
    if (sessions.length === 0) {
      container.innerHTML = '<p style=""color:#666"">No sessions found.</p>';
      return;
    }
    container.innerHTML = sessions.map(s => {
      const start = new Date(s.sessionStart);
      const end = new Date(s.sessionEnd);
      const duration = ((end - start) / 3600000).toFixed(1);
      const targets = s.targets.join(', ') || 'No targets';
      const integration = (s.totalIntegrationSeconds / 3600).toFixed(1);
      const reportBadge = s.hasReport
        ? ""<span class='has-report'>Report available</span>""
        : ""<span class='no-report'>No report saved</span>"";
      const onclick = s.hasReport
        ? ""window.open('/api/sessions/"" + s.sessionId + ""/report', '_blank')""
        : """";
      const cursor = s.hasReport ? 'cursor:pointer' : 'cursor:default';
      return ""<div class='session-card' style='"" + cursor + ""' onclick=\\"""" + onclick + ""\\"">"" +
        ""<div class='session-header'>"" +
          ""<span class='session-date'>"" + start.toLocaleDateString(undefined, {year:'numeric',month:'short',day:'numeric'}) + ""</span>"" +
          reportBadge +
        ""</div>"" +
        ""<div class='session-meta'>"" +
          ""<span class='stat'>Profile: "" + (s.profileName || 'Unknown') + ""</span>"" +
          ""<span class='stat'>Images: "" + s.imageCount + ""</span>"" +
          ""<span class='stat'>Duration: "" + duration + ""h</span>"" +
          ""<span class='stat'>Integration: "" + integration + ""h</span>"" +
        ""</div>"" +
        ""<div style='margin-top:6px;font-size:13px;color:#8b949e'>Targets: "" + targets + ""</div>"" +
      ""</div>"";
    }).join('');
  } catch (err) {
    document.getElementById('sessions').innerHTML = '<p style=""color:#f85149"">Failed to load sessions: ' + err.message + '</p>';
  }
}
loadSessions();
</script>
</body>
</html>";
        }
    }
}
