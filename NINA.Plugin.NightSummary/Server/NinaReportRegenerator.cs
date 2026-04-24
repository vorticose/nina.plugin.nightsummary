using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Session;

namespace NINA.Plugin.NightSummary.Server;

// Wraps SessionService.BuildReportDataAsync + GenerateHtmlAsync + livestack
// master persistence. Settings snapshot/apply/restore stays in DashboardServer
// so bulk regen can apply once for the whole batch.
internal sealed class NinaReportRegenerator : IReportRegenerator {
    private readonly SessionService sessionService;
    private readonly string dbPath;
    private readonly string reportsDir;

    public NinaReportRegenerator(SessionService sessionService, string dbPath, string reportsDir) {
        this.sessionService = sessionService;
        this.dbPath         = dbPath;
        this.reportsDir     = reportsDir;
    }

    public bool IsAvailable => sessionService != null;

    public async Task<string?> RegenerateAsync(string sessionId, CancellationToken ct = default) {
        try {
            var reportData = await sessionService.BuildReportDataAsync(dbPath, sessionId);
            if (reportData == null) return "Session not found";

            var html       = await sessionService.GenerateHtmlAsync(reportData);
            var reportPath = Path.Combine(reportsDir, $"{sessionId}.html");
            Directory.CreateDirectory(reportsDir);
            await File.WriteAllTextAsync(reportPath, html, ct);
            SaveLiveStackMasters(sessionId, reportData);
            return null;
        } catch (Exception ex) {
            Logger.Error($"NightSummary: Regenerate failed for {sessionId}. {ex.Message}");
            return ex.Message;
        }
    }

    private void SaveLiveStackMasters(string sessionId, ReportData reportData) {
        if (reportData.LiveStackImages == null || reportData.LiveStackImages.Count == 0) return;
        try {
            var lsDir = Path.Combine(reportsDir, "livestack", sessionId);
            Directory.CreateDirectory(lsDir);
            var manifest = new List<Dictionary<string, object>>();
            foreach (var img in reportData.LiveStackImages) {
                var data     = img.MasterJpegData ?? img.JpegData;
                var safeName = Regex.Replace($"{img.Target}_{img.Filter}", @"[^\w\-.]", "_");
                var jpgFile  = safeName + ".jpg";
                File.WriteAllBytes(Path.Combine(lsDir, jpgFile), data);
                manifest.Add(new Dictionary<string, object> {
                    ["file"]            = jpgFile,
                    ["target"]          = img.Target,
                    ["filter"]          = img.Filter,
                    ["isMonochrome"]    = img.IsMonochrome,
                    ["stackCount"]      = img.StackCount,
                    ["redStackCount"]   = img.RedStackCount,
                    ["greenStackCount"] = img.GreenStackCount,
                    ["blueStackCount"]  = img.BlueStackCount
                });
            }
            var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(lsDir, "livestack.json"), json);
        } catch (Exception ex) {
            Logger.Warning($"NightSummary: Failed to save livestack masters for {sessionId}. {ex.Message}");
        }
    }
}
