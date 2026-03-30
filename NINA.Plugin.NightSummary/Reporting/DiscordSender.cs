using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Reporting {
    /// <summary>
    /// Sends Night Summary reports to a Discord channel via webhook.
    /// </summary>
    public class DiscordSender {

        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private readonly string webhookUrl;

        public DiscordSender(string webhookUrl) {
            this.webhookUrl = webhookUrl;
        }

        /// <summary>
        /// Sends a session summary embed to Discord with the full HTML report attached as a file.
        /// </summary>
        public async Task<bool> SendReportAsync(ReportData reportData, string htmlReport, string fileName = null) {
            try {
                Logger.Info("NightSummary: Sending Discord report");
                var payload  = BuildReportPayload(reportData);
                var json     = JsonSerializer.Serialize(payload);
                fileName   ??= $"NightSummary_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.html";
                return await PostWithAttachment(json, htmlReport, fileName);
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to send Discord report. {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sends a simple test message to verify the webhook is configured correctly.
        /// </summary>
        public async Task<bool> SendTestAsync() {
            try {
                Logger.Info("NightSummary: Sending Discord test message");
                var payload = new {
                    username = "Night Summary",
                    embeds = new[] {
                        new {
                            title = "Night Summary",
                            description = "Discord is configured correctly!",
                            color = 8302839
                        }
                    }
                };
                return await PostPayload(payload);
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to send Discord test. {ex.Message}");
                return false;
            }
        }

        internal object BuildReportPayload(ReportData reportData) {
            var session = reportData.Session;
            var images  = reportData.Images;
            var events  = reportData.Events ?? new List<SessionEvent>();
            var fields  = new List<object>();

            // ── Session Overview ───────────────────────────────────────────────
            var totalExpSec = images.Sum(i => i.ExposureDuration);
            var hfrImages   = images.Where(i => i.HFR > 0).ToList();
            var rmsImages   = images.Where(i => i.GuidingRMSTotal > 0).ToList();

            var yield = YieldCalculator.Calculate(images, events, session.SessionStart, session.SessionEnd);
            var yieldPct = yield.YieldPct;

            var overview = new StringBuilder();
            var skippedNote = reportData.SkippedExposures > 0 ? $" ({reportData.SkippedExposures} aborted)" : "";
            overview.AppendLine($"Total Images: {images.Count}{skippedNote}");
            overview.AppendLine($"Total Exposure: {totalExpSec / 3600.0:F1}h");
            if (hfrImages.Any()) overview.AppendLine($"Avg HFR: {hfrImages.Average(i => i.HFR):F2}px");
            if (rmsImages.Any()) overview.AppendLine($"Avg Guiding RMS: {rmsImages.Average(i => i.GuidingRMSTotal):F2}\"");
            overview.Append($"Yield: {yieldPct:F0}%");
            fields.Add(Field("Session Overview", overview.ToString()));

            if (!images.Any()) {
                fields.Add(Field("Images", "No images recorded during this session."));
                return Payload(fields, session.SessionEnd);
            }

            // ── Per-target breakdown ───────────────────────────────────────────
            var targets = images.GroupBy(i => i.TargetName).OrderBy(g => g.Min(i => i.Timestamp));
            foreach (var target in targets) {
                var sb = new StringBuilder();

                var filterGroups = target.GroupBy(i => i.Filter)
                                         .OrderBy(g => FilterHelper.SortKey(g.Key)).ThenBy(g => g.Key);
                foreach (var fg in filterGroups) {
                    var totalTime = TimeSpan.FromSeconds(fg.Sum(i => i.ExposureDuration));
                    sb.AppendLine($"{fg.Key}: {fg.Count()}\u00d7{fg.First().ExposureDuration:F0}s ({totalTime.TotalHours:F1}h)");
                }

                var targetTotal = TimeSpan.FromSeconds(target.Sum(i => i.ExposureDuration));
                sb.AppendLine($"**Total: {targetTotal.TotalHours:F1}h**");

                // Star count CV
                var bbImages = target.Where(i => FilterHelper.IsBroadband(i.Filter) && i.StarCount > 0).ToList();
                var nbImages = target.Where(i => FilterHelper.IsNarrowband(i.Filter) && i.StarCount > 0).ToList();
                string bbCV = bbImages.Count >= 2 ? $"{FilterHelper.CV(bbImages.Select(i => (double)i.StarCount).ToList()):F0}%" : "\u2014";
                string nbCV = nbImages.Count >= 2 ? $"{FilterHelper.CV(nbImages.Select(i => (double)i.StarCount).ToList()):F0}%" : "\u2014";
                sb.Append($"Star count CV \u2014 Broadband: {bbCV} | Narrowband: {nbCV}");

                fields.Add(Field($"\ud83c\udf0c {target.Key}", sb.ToString().TrimEnd()));
            }

            // ── Image quality ──────────────────────────────────────────────────
            var withHFR  = images.Where(i => i.HFR > 0).ToList();
            var withFWHM = images.Where(i => i.FWHM > 0).ToList();
            var withEcc  = images.Where(i => i.Eccentricity > 0).ToList();

            if (withHFR.Any()) {
                var hfrVals = withHFR.Select(i => i.HFR).ToList();
                fields.Add(Field("HFR", $"Min {hfrVals.Min():F2} | Max {hfrVals.Max():F2} | Mean {hfrVals.Average():F2} | CV {FilterHelper.CV(hfrVals):F0}%"));
            }

            if (withFWHM.Any()) {
                var fwhmVals = withFWHM.Select(i => i.FWHM).ToList();
                fields.Add(Field("FWHM", $"Min {fwhmVals.Min():F2} | Max {fwhmVals.Max():F2} | Mean {fwhmVals.Average():F2} | CV {FilterHelper.CV(fwhmVals):F0}%"));
            }

            if (withEcc.Any()) {
                var eccVals = withEcc.Select(i => i.Eccentricity).ToList();
                fields.Add(Field("Eccentricity", $"Min {eccVals.Min():F3} | Max {eccVals.Max():F3} | Mean {eccVals.Average():F3} | CV {FilterHelper.CV(eccVals):F0}%"));
            }

            // ── Guiding ────────────────────────────────────────────────────────
            var withGuiding = images.Where(i => i.GuidingRMSTotal > 0).ToList();
            if (withGuiding.Any()) {
                var rmsVals = withGuiding.Select(i => i.GuidingRMSTotal).ToList();
                fields.Add(Field("Guiding RMS", $"Min {rmsVals.Min():F2}\" | Max {rmsVals.Max():F2}\" | Mean {rmsVals.Average():F2}\" | CV {FilterHelper.CV(rmsVals):F0}%"));
            }

            return Payload(fields, session.SessionEnd);
        }

        private object Payload(List<object> fields, DateTime timestamp) {
            return new {
                username = "Night Summary",
                embeds = new[] {
                    new {
                        title = "\ud83d\udd2d Night Summary Report",
                        color = 8302839, // #7eb8f7
                        fields = fields.ToArray(),
                        footer = new { text = "Generated by Night Summary for N.I.N.A." },
                        timestamp = timestamp.ToUniversalTime().ToString("o")
                    }
                }
            };
        }

        private async Task<bool> PostPayload(object payload) {
            var json    = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            using (var response = await httpClient.PostAsync(webhookUrl, content)) {
                return await LogResult(response);
            }
        }

        private async Task<bool> PostWithAttachment(string payloadJson, string htmlContent, string fileName) {
            using (var multipart = new MultipartFormDataContent()) {
                multipart.Add(new StringContent(payloadJson, Encoding.UTF8, "application/json"), "payload_json");
                var fileBytes = Encoding.UTF8.GetBytes(htmlContent);
                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                multipart.Add(fileContent, "files[0]", fileName);
                using (var response = await httpClient.PostAsync(webhookUrl, multipart)) {
                    return await LogResult(response);
                }
            }
        }

        private async Task<bool> LogResult(HttpResponseMessage response) {
            if (response.IsSuccessStatusCode) {
                Logger.Info("NightSummary: Discord message sent successfully");
                return true;
            } else {
                var body = await response.Content.ReadAsStringAsync();
                Logger.Error($"NightSummary: Discord webhook returned {(int)response.StatusCode} — {body}");
                return false;
            }
        }

        private static object Field(string name, string value, bool inline = false) {
            return new { name, value, inline };
        }
    }
}
