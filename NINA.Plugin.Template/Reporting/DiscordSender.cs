using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.Generic;
using System.IO;
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

        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly HashSet<string> BroadbandFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "L", "R", "G", "B" };
        private static readonly HashSet<string> NarrowbandFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "H", "Ha", "S", "Sii", "O", "Oiii" };

        private readonly string webhookUrl;

        public DiscordSender(string webhookUrl) {
            this.webhookUrl = webhookUrl;
        }

        /// <summary>
        /// Sends a session summary embed to Discord with the full HTML report attached as a file.
        /// </summary>
        public async Task<bool> SendReportAsync(SessionRecord session, List<ImageRecord> images, string htmlReport) {
            try {
                Logger.Info("NightSummary: Sending Discord report");
                var payload = BuildReportPayload(session, images);
                var json    = JsonSerializer.Serialize(payload);
                var fileName = $"NightSummary_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.html";
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

        private object BuildReportPayload(SessionRecord session, List<ImageRecord> images) {
            var fields = new List<object>();
            var duration = session.SessionEnd - session.SessionStart;

            // Session info
            fields.Add(Field("Date", session.SessionStart.ToString("yyyy-MM-dd"), true));
            fields.Add(Field("Duration", $"{duration.TotalHours:F1}h", true));
            fields.Add(Field("Profile", session.ProfileName ?? "—", true));
            fields.Add(Field("Start", session.SessionStart.ToString("HH:mm:ss"), true));
            fields.Add(Field("End", session.SessionEnd.ToString("HH:mm:ss"), true));

            if (!images.Any()) {
                fields.Add(Field("Images", "No images recorded during this session."));
                return Payload(fields, session.SessionEnd);
            }

            // Session overview
            var accepted = images.Where(i => i.Accepted).ToList();
            var totalExp = TimeSpan.FromSeconds(images.Sum(i => i.ExposureDuration));
            fields.Add(Field("Total Images", images.Count.ToString(), true));
            fields.Add(Field("Accepted", accepted.Count.ToString(), true));
            fields.Add(Field("Rejected", (images.Count - accepted.Count).ToString(), true));
            fields.Add(Field("Total Exposure", $"{totalExp.TotalHours:F1}h", true));

            // Per-target breakdown
            var targets = images.GroupBy(i => i.TargetName).OrderBy(g => g.Key);
            foreach (var target in targets) {
                var sb = new StringBuilder();

                var filterGroups = target.GroupBy(i => i.Filter).OrderBy(g => g.Key);
                foreach (var fg in filterGroups) {
                    var totalTime = TimeSpan.FromSeconds(fg.Sum(i => i.ExposureDuration));
                    sb.AppendLine($"{fg.Key}: {fg.Count()}×{fg.First().ExposureDuration:F0}s ({totalTime.TotalMinutes:F1} min)");
                }

                var targetTotal = TimeSpan.FromSeconds(target.Sum(i => i.ExposureDuration));
                sb.AppendLine($"**Total: {targetTotal.TotalMinutes:F1} min**");

                // Star count CV
                var bbImages = target.Where(i => BroadbandFilters.Contains(i.Filter) && i.StarCount > 0).ToList();
                var nbImages = target.Where(i => NarrowbandFilters.Contains(i.Filter) && i.StarCount > 0).ToList();
                string bbCV = bbImages.Count >= 2 ? $"{CV(bbImages.Select(i => (double)i.StarCount).ToList()):F0}%" : "—";
                string nbCV = nbImages.Count >= 2 ? $"{CV(nbImages.Select(i => (double)i.StarCount).ToList()):F0}%" : "—";
                sb.Append($"Star count CV — Broadband: {bbCV} | Narrowband: {nbCV}");

                fields.Add(Field($"🌌 {target.Key}", sb.ToString().TrimEnd()));
            }

            // Image quality
            var withHFR  = images.Where(i => i.HFR > 0).ToList();
            var withFWHM = images.Where(i => i.FWHM > 0).ToList();
            var withEcc  = images.Where(i => i.Eccentricity > 0).ToList();

            if (withHFR.Any()) {
                var hfrVals = withHFR.Select(i => i.HFR).ToList();
                fields.Add(Field("HFR", $"Min {hfrVals.Min():F2} | Max {hfrVals.Max():F2} | Mean {hfrVals.Average():F2} | CV {CV(hfrVals):F0}%"));
            }

            if (withFWHM.Any()) {
                var fwhmVals = withFWHM.Select(i => i.FWHM).ToList();
                fields.Add(Field("FWHM", $"Min {fwhmVals.Min():F2} | Max {fwhmVals.Max():F2} | Mean {fwhmVals.Average():F2} | CV {CV(fwhmVals):F0}%"));
            }

            if (withEcc.Any()) {
                var eccVals = withEcc.Select(i => i.Eccentricity).ToList();
                fields.Add(Field("Eccentricity", $"Min {eccVals.Min():F3} | Max {eccVals.Max():F3} | Mean {eccVals.Average():F3} | CV {CV(eccVals):F0}%"));
            }

            // Guiding
            var withGuiding = images.Where(i => i.GuidingRMSTotal > 0).ToList();
            if (withGuiding.Any()) {
                var rmsVals = withGuiding.Select(i => i.GuidingRMSTotal).ToList();
                fields.Add(Field("Guiding RMS", $"Min {rmsVals.Min():F2}\" | Max {rmsVals.Max():F2}\" | Mean {rmsVals.Average():F2}\" | CV {CV(rmsVals):F0}%"));
            }

            return Payload(fields, session.SessionEnd);
        }

        private object Payload(List<object> fields, DateTime timestamp) {
            return new {
                username = "Night Summary",
                embeds = new[] {
                    new {
                        title = "🔭 Night Summary Report",
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
            var response = await httpClient.PostAsync(webhookUrl, content);
            return await LogResult(response);
        }

        private async Task<bool> PostWithAttachment(string payloadJson, string htmlContent, string fileName) {
            var multipart = new MultipartFormDataContent();
            multipart.Add(new StringContent(payloadJson, Encoding.UTF8, "application/json"), "payload_json");
            var fileBytes = Encoding.UTF8.GetBytes(htmlContent);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            multipart.Add(fileContent, "files[0]", fileName);
            var response = await httpClient.PostAsync(webhookUrl, multipart);
            return await LogResult(response);
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

        private static double CV(List<double> values) {
            if (values.Count < 2) return 0;
            var avg = values.Average();
            if (avg == 0) return 0;
            return (StdDev(values) / avg) * 100;
        }

        private static double StdDev(List<double> values) {
            if (values.Count < 2) return 0;
            var avg = values.Average();
            var sumOfSquares = values.Sum(v => Math.Pow(v - avg, 2));
            return Math.Sqrt(sumOfSquares / (values.Count - 1));
        }
    }
}
