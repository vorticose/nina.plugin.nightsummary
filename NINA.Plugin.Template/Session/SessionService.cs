using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.MyPluginProperties;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Session {

    [Export(typeof(SessionService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class SessionService {

        private readonly SessionCollector collector;
        private readonly ReportGenerator reportGenerator;
        private readonly IProfileService profileService;

        [ImportingConstructor]
        public SessionService(
            IImageSaveMediator imageSaveMediator,
            IProfileService profileService) {

            this.profileService = profileService;
            var database = new SessionDatabase();
            this.collector = new SessionCollector(imageSaveMediator, database);
            this.reportGenerator = new ReportGenerator();
        }

        public void StartSession(string profileName) {
            var name = profileService?.ActiveProfile?.Name ?? profileName;
            collector.StartSession(name);
        }

        public void EndSession() {
            if (collector.GetCurrentSessionId() == null) return;

            var sessionId = collector.GetCurrentSessionId();
            collector.EndSession();

            if (Settings.Default.SendReportOnSessionEnd) {
                Task.Run(async () => await SendReportAsync(sessionId));
            }

            if (Settings.Default.PushoverEnabled) {
                Task.Run(async () => await SendPushoverAsync(sessionId));
            }

            if (Settings.Default.DiscordEnabled) {
                Task.Run(async () => await SendDiscordAsync(sessionId));
            }
        }

        private async Task SendPushoverAsync(string sessionId) {
            try {
                var appToken = Settings.Default.PushoverAppToken;
                var userKey  = Settings.Default.PushoverUserKey;

                if (string.IsNullOrWhiteSpace(appToken) || string.IsNullOrWhiteSpace(userKey)) {
                    Logger.Warning("NightSummary: Pushover not configured — skipping notification");
                    return;
                }

                var database = collector.Database;
                var session  = database.GetSession(sessionId);
                var images   = database.GetImagesForSession(sessionId);

                if (session == null) return;

                var duration = (session.SessionEnd - session.SessionStart).TotalHours;
                var accepted = images.Count(i => i.Accepted);
                var message  = $"{accepted} images accepted in {duration:F1}h";

                var sender = new PushoverSender(appToken, userKey);
                await sender.SendAsync("Night Summary", message);
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to send Pushover notification. {ex.Message}");
            }
        }

        private async Task SendDiscordAsync(string sessionId) {
            try {
                var webhookUrl = Settings.Default.DiscordWebhookUrl;

                if (string.IsNullOrWhiteSpace(webhookUrl)) {
                    Logger.Warning("NightSummary: Discord webhook URL not configured — skipping");
                    return;
                }

                var database = collector.Database;
                var session  = database.GetSession(sessionId);
                var images   = database.GetImagesForSession(sessionId);

                if (session == null) return;

                var sender = new DiscordSender(webhookUrl);
                await sender.SendReportAsync(session, images);
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to send Discord report. {ex.Message}");
            }
        }

        private async Task SendReportAsync(string sessionId) {
            try {
                var gmailAddress = Settings.Default.GmailAddress;
                var appPassword = Settings.Default.GmailAppPassword;
                var recipient = Settings.Default.RecipientAddress;

                if (string.IsNullOrWhiteSpace(gmailAddress) ||
                    string.IsNullOrWhiteSpace(appPassword) ||
                    string.IsNullOrWhiteSpace(recipient)) {
                    Logger.Warning("NightSummary: Email settings not configured - skipping report");
                    return;
                }

                var database = collector.Database;
                var session = database.GetSession(sessionId);
                var images = database.GetImagesForSession(sessionId);

                if (session == null) {
                    Logger.Error("NightSummary: Could not find session record for report generation");
                    return;
                }

                var htmlReport = reportGenerator.GenerateHtmlReport(session, images);
                var subject = $"Night Summary Report - {session.SessionStart:yyyy-MM-dd} - {images.Count} images";

                var sender = new EmailSender(gmailAddress, appPassword, recipient);
                var success = await sender.SendReportAsync(subject, htmlReport);

                if (success) {
                    database.FinalizeSession(sessionId, session.SessionEnd, true);
                    Logger.Info("NightSummary: Report sent and session marked as complete");
                }

            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to generate/send report. {ex.Message}");
            }
        }

        public string GetCurrentSessionId() {
            return collector.GetCurrentSessionId();
        }

        public SessionDatabase Database => collector.Database;
    }
}