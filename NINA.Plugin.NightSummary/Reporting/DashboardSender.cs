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
    /// Uploads Night Summary session data and HTML report to the nightsummary-server dashboard.
    /// </summary>
    public class DashboardSender {

        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private readonly string baseUrl;
        private readonly string apiKey;

        public DashboardSender(string baseUrl, string apiKey) {
            this.baseUrl = baseUrl.TrimEnd('/');
            this.apiKey = apiKey;
        }

        /// <summary>
        /// Uploads session metadata and the HTML report to the dashboard server.
        /// </summary>
        public async Task<bool> SendReportAsync(ReportData reportData, string htmlReport) {
            try {
                Logger.Info("NightSummary: Uploading report to dashboard");

                var metadata = BuildMetadata(reportData);
                var metadataJson = JsonSerializer.Serialize(metadata);

                using (var multipart = new MultipartFormDataContent()) {
                    multipart.Add(new StringContent(metadataJson, Encoding.UTF8, "application/json"), "metadata");

                    var fileBytes = Encoding.UTF8.GetBytes(htmlReport);
                    var fileContent = new ByteArrayContent(fileBytes);
                    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");
                    var fileName = $"NightSummary_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.html";
                    multipart.Add(fileContent, "report", fileName);

                    var url = $"{baseUrl}/api/sessions";
                    using (var response = await httpClient.PostAsync(url, multipart)) {
                        if (response.IsSuccessStatusCode) {
                            Logger.Info("NightSummary: Dashboard upload successful");
                            return true;
                        } else {
                            var body = await response.Content.ReadAsStringAsync();
                            Logger.Error($"NightSummary: Dashboard upload returned {(int)response.StatusCode} — {body}");
                            return false;
                        }
                    }
                }
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to upload to dashboard. {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sends a health check request to verify the dashboard server is reachable.
        /// </summary>
        public async Task<bool> TestConnectionAsync() {
            try {
                Logger.Info("NightSummary: Testing dashboard connection");
                var url = $"{baseUrl}/api/health";
                using (var response = await httpClient.GetAsync(url)) {
                    if (response.IsSuccessStatusCode) {
                        Logger.Info("NightSummary: Dashboard connection test successful");
                        return true;
                    } else {
                        Logger.Error($"NightSummary: Dashboard health check returned {(int)response.StatusCode}");
                        return false;
                    }
                }
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Dashboard connection test failed. {ex.Message}");
                return false;
            }
        }

        private object BuildMetadata(ReportData reportData) {
            var session = reportData.Session;
            var images = reportData.Images;

            var hfrImages = images.Where(i => i.HFR > 0).ToList();
            var rmsImages = images.Where(i => i.GuidingRMSTotal > 0).ToList();
            var starImages = images.Where(i => i.StarCount > 0).ToList();

            var targets = images.GroupBy(i => i.TargetName).Select(g => {
                var filters = g.GroupBy(i => i.Filter).Select(fg => new {
                    name = fg.Key,
                    count = fg.Count(),
                    integration_seconds = fg.Sum(i => i.ExposureDuration),
                    avg_hfr = fg.Where(i => i.HFR > 0).Select(i => (double?)i.HFR).DefaultIfEmpty(null).Average(),
                    avg_guiding_rms = fg.Where(i => i.GuidingRMSTotal > 0).Select(i => (double?)i.GuidingRMSTotal).DefaultIfEmpty(null).Average()
                }).ToList();

                return new {
                    name = g.Key,
                    ra = (double?)null,
                    dec = (double?)null,
                    filters
                };
            }).ToList();

            return new {
                api_key = apiKey,
                session = new {
                    session_id = session.SessionId,
                    session_start = session.SessionStart.ToString("o"),
                    session_end = session.SessionEnd.ToString("o"),
                    profile_name = session.ProfileName,
                    targets,
                    total_frames = images.Count,
                    total_integration_seconds = images.Sum(i => i.ExposureDuration),
                    avg_hfr = hfrImages.Any() ? (double?)hfrImages.Average(i => i.HFR) : null,
                    avg_guiding_rms = rmsImages.Any() ? (double?)rmsImages.Average(i => i.GuidingRMSTotal) : null,
                    avg_star_count = starImages.Any() ? (double?)starImages.Average(i => i.StarCount) : null
                }
            };
        }
    }
}
