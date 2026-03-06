using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.MyPluginProperties;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
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

            var database = collector.Database;
            var session  = database.GetSession(sessionId);
            var images   = database.GetImagesForSession(sessionId);

            if (session == null) return;

            if (Settings.Default.SaveReportLocally) {
                Task.Run(async () => await SaveReportLocallyAsync(session, images));
            }

            if (Settings.Default.EmailEnabled) {
                Task.Run(async () => await SendReportWithDataAsync(session, images));
            }

            if (Settings.Default.PushoverEnabled) {
                Task.Run(async () => await SendPushoverWithDataAsync(session, images));
            }

            if (Settings.Default.DiscordEnabled) {
                Task.Run(async () => await SendDiscordWithDataAsync(session, images));
            }
        }

        /// <summary>
        /// Sends all enabled reports for the most recent session in the given database file.
        /// Used by the "Send Test Report" button in the Options UI.
        /// </summary>
        public async Task SendFromDatabaseAsync(string dbPath) {
            try {
                var testDb  = new SessionDatabase(dbPath);
                var session = testDb.GetLatestSession();

                if (session == null) {
                    Logger.Warning("NightSummary: No sessions found in test database");
                    return;
                }

                var images = testDb.GetImagesForSession(session.SessionId);
                Logger.Info($"NightSummary: Sending test report for session {session.SessionId} ({images.Count} images)");

                await Task.WhenAll(
                    Settings.Default.SaveReportLocally ? SaveReportLocallyAsync(session, images)  : Task.CompletedTask,
                    Settings.Default.EmailEnabled      ? SendReportWithDataAsync(session, images)  : Task.CompletedTask,
                    Settings.Default.PushoverEnabled   ? SendPushoverWithDataAsync(session, images) : Task.CompletedTask,
                    Settings.Default.DiscordEnabled    ? SendDiscordWithDataAsync(session, images)  : Task.CompletedTask
                );
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to send test report. {ex.Message}");
            }
        }

        private async Task SaveReportLocallyAsync(SessionRecord session, List<ImageRecord> images) {
            try {
                var saveDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "N.I.N.A.", "Night Summary", "Saved Reports");
                Directory.CreateDirectory(saveDir);

                var filename = $"NightSummary_{session.SessionStart:yyyy-MM-dd_HH-mm-ss}.html";
                var filePath = Path.Combine(saveDir, filename);

                var htmlReport = reportGenerator.GenerateHtmlReport(session, images);
                await File.WriteAllTextAsync(filePath, htmlReport);

                Logger.Info($"NightSummary: Report saved locally to {filePath}");
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to save report locally. {ex.Message}");
            }
        }

        private async Task SendPushoverWithDataAsync(SessionRecord session, List<ImageRecord> images) {
            try {
                var appToken = Settings.Default.PushoverAppToken;
                var userKey  = Settings.Default.PushoverUserKey;

                if (string.IsNullOrWhiteSpace(appToken) || string.IsNullOrWhiteSpace(userKey)) {
                    Logger.Warning("NightSummary: Pushover not configured — skipping notification");
                    return;
                }

                var duration = (session.SessionEnd - session.SessionStart).TotalHours;
                var accepted = images.Count(i => i.Accepted);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Session complete — {duration:F1}h total");
                sb.AppendLine();

                var targets = images.GroupBy(i => i.TargetName).OrderBy(g => g.Key);
                foreach (var target in targets) {
                    var targetExp = TimeSpan.FromSeconds(target.Sum(i => i.ExposureDuration));
                    sb.AppendLine($"{target.Key}: {target.Count()} images ({targetExp.TotalHours:F1}h)");
                }

                sb.AppendLine();
                sb.Append($"{accepted} accepted of {images.Count} total");

                var sender = new PushoverSender(appToken, userKey);
                await sender.SendAsync("Night Summary", sb.ToString());
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to send Pushover notification. {ex.Message}");
            }
        }

        private async Task SendDiscordWithDataAsync(SessionRecord session, List<ImageRecord> images) {
            try {
                var webhookUrl = Settings.Default.DiscordWebhookUrl;

                if (string.IsNullOrWhiteSpace(webhookUrl)) {
                    Logger.Warning("NightSummary: Discord webhook URL not configured — skipping");
                    return;
                }

                var htmlReport = reportGenerator.GenerateHtmlReport(session, images);
                var sender     = new DiscordSender(webhookUrl);
                await sender.SendReportAsync(session, images, htmlReport);
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to send Discord report. {ex.Message}");
            }
        }

        private async Task SendReportWithDataAsync(SessionRecord session, List<ImageRecord> images) {
            try {
                var gmailAddress = Settings.Default.GmailAddress;
                var appPassword  = Settings.Default.GmailAppPassword;
                var recipient    = Settings.Default.RecipientAddress;

                if (string.IsNullOrWhiteSpace(gmailAddress) ||
                    string.IsNullOrWhiteSpace(appPassword) ||
                    string.IsNullOrWhiteSpace(recipient)) {
                    Logger.Warning("NightSummary: Email settings not configured - skipping report");
                    return;
                }

                var htmlReport = reportGenerator.GenerateHtmlReport(session, images);
                var subject    = $"Night Summary Report - {session.SessionStart:yyyy-MM-dd} - {images.Count} images";
                var duration   = (session.SessionEnd - session.SessionStart).TotalHours;
                var accepted   = images.Count(i => i.Accepted);
                var totalExp   = System.TimeSpan.FromSeconds(images.Sum(i => i.ExposureDuration));

                var body = new System.Text.StringBuilder();
                body.AppendLine($"Session complete — {duration:F1}h total");
                body.AppendLine();
                var targets = images.GroupBy(i => i.TargetName).OrderBy(g => g.Key);
                foreach (var target in targets) {
                    var targetExp = System.TimeSpan.FromSeconds(target.Sum(i => i.ExposureDuration));
                    body.AppendLine($"{target.Key}: {target.Count()} images ({targetExp.TotalHours:F1}h)");
                }
                body.AppendLine();
                body.AppendLine($"{accepted} accepted of {images.Count} total");
                body.AppendLine();
                body.AppendLine("Full report attached.");

                var sender  = new EmailSender(gmailAddress, appPassword, recipient);
                var success = await sender.SendReportAsync(subject, htmlReport, body.ToString());

                if (success) {
                    collector.Database.FinalizeSession(session.SessionId, session.SessionEnd, true);
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
