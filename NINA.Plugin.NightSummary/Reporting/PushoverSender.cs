using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Reporting {
    /// <summary>
    /// Sends push notifications via the Pushover API.
    /// </summary>
    public class PushoverSender {

        private static readonly HttpClient httpClient = new HttpClient();
        private const string ApiUrl = "https://api.pushover.net/1/messages.json";

        private readonly string appToken;
        private readonly string userKey;

        public PushoverSender(string appToken, string userKey) {
            this.appToken = appToken;
            this.userKey = userKey;
        }

        /// <summary>
        /// Sends a push notification. Returns true if the API accepted it.
        /// </summary>
        public async Task<bool> SendAsync(string title, string message) {
            try {
                Logger.Info($"NightSummary: Sending Pushover notification — {title}");

                var formData = new FormUrlEncodedContent(new[] {
                    new KeyValuePair<string, string>("token", appToken),
                    new KeyValuePair<string, string>("user",  userKey),
                    new KeyValuePair<string, string>("title", title),
                    new KeyValuePair<string, string>("message", message)
                });

                var response = await httpClient.PostAsync(ApiUrl, formData);

                if (response.IsSuccessStatusCode) {
                    Logger.Info("NightSummary: Pushover notification sent successfully");
                    return true;
                } else {
                    var body = await response.Content.ReadAsStringAsync();
                    Logger.Error($"NightSummary: Pushover API returned {(int)response.StatusCode} — {body}");
                    return false;
                }
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to send Pushover notification. {ex.Message}");
                return false;
            }
        }
    }
}
