using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
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

        private readonly SessionCollector      collector;
        private readonly SessionEventCollector eventCollector;
        private readonly ReportGenerator       reportGenerator;
        private readonly IProfileService       profileService;

        [ImportingConstructor]
        public SessionService(
            IImageSaveMediator     imageSaveMediator,
            IProfileService        profileService,
            ISafetyMonitorMediator safetyMonitorMediator,
            IFocuserMediator       focuserMediator,
            ITelescopeMediator     telescopeMediator) {

            this.profileService  = profileService;
            var database         = new SessionDatabase();
            this.collector       = new SessionCollector(imageSaveMediator, database);
            this.eventCollector  = new SessionEventCollector(database, safetyMonitorMediator, focuserMediator, telescopeMediator);
            this.reportGenerator = new ReportGenerator();
        }

        public void StartSession(string profileName) {
            var name = profileService?.ActiveProfile?.Name ?? profileName;
            collector.StartSession(name);
            eventCollector.StartSession(collector.GetCurrentSessionId());
        }

        public void EndSession() {
            if (collector.GetCurrentSessionId() == null) return;

            var sessionId = collector.GetCurrentSessionId();
            collector.EndSession();
            eventCollector.EndSession();

            var database   = collector.Database;
            var session    = database.GetSession(sessionId);
            var images     = database.GetImagesForSession(sessionId);
            var events     = database.GetEventsForSession(sessionId);

            if (session == null) return;

            var reportData = new ReportData { Session = session, Images = images, Events = events };

            if (Settings.Default.SaveReportLocally) {
                Task.Run(async () => await SaveReportLocallyAsync(reportData));
            }

            if (Settings.Default.EmailEnabled) {
                Task.Run(async () => await SendReportWithDataAsync(reportData));
            }

            if (Settings.Default.PushoverEnabled) {
                Task.Run(async () => await SendPushoverWithDataAsync(reportData));
            }

            if (Settings.Default.DiscordEnabled) {
                Task.Run(async () => await SendDiscordWithDataAsync(reportData));
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
                var events = testDb.GetEventsForSession(session.SessionId);
                Logger.Info($"NightSummary: Sending test report for session {session.SessionId} ({images.Count} images, {events.Count} events)");

                var reportData = new ReportData { Session = session, Images = images, Events = events };

                await Task.WhenAll(
                    Settings.Default.SaveReportLocally ? SaveReportLocallyAsync(reportData)  : Task.CompletedTask,
                    Settings.Default.EmailEnabled      ? SendReportWithDataAsync(reportData)  : Task.CompletedTask,
                    Settings.Default.PushoverEnabled   ? SendPushoverWithDataAsync(reportData) : Task.CompletedTask,
                    Settings.Default.DiscordEnabled    ? SendDiscordWithDataAsync(reportData)  : Task.CompletedTask
                );
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to send test report. {ex.Message}");
            }
        }

        private async Task SaveReportLocallyAsync(ReportData reportData) {
            try {
                var saveDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "N.I.N.A.", "Night Summary", "Saved Reports");
                Directory.CreateDirectory(saveDir);

                var filename = $"NightSummary_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.html";
                var filePath = Path.Combine(saveDir, filename);

                var htmlReport = reportGenerator.GenerateHtmlReport(reportData);
                await File.WriteAllTextAsync(filePath, htmlReport);

                Logger.Info($"NightSummary: Report saved locally to {filePath}");
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to save report locally. {ex.Message}");
            }
        }

        private async Task SendPushoverWithDataAsync(ReportData reportData) {
            try {
                var appToken = Settings.Default.PushoverAppToken;
                var userKey  = Settings.Default.PushoverUserKey;

                if (string.IsNullOrWhiteSpace(appToken) || string.IsNullOrWhiteSpace(userKey)) {
                    Logger.Warning("NightSummary: Pushover not configured — skipping notification");
                    return;
                }

                var session  = reportData.Session;
                var images   = reportData.Images;
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

        private async Task SendDiscordWithDataAsync(ReportData reportData) {
            try {
                var webhookUrl = Settings.Default.DiscordWebhookUrl;

                if (string.IsNullOrWhiteSpace(webhookUrl)) {
                    Logger.Warning("NightSummary: Discord webhook URL not configured — skipping");
                    return;
                }

                var htmlReport = reportGenerator.GenerateHtmlReport(reportData);
                var sender     = new DiscordSender(webhookUrl);
                await sender.SendReportAsync(reportData.Session, reportData.Images, htmlReport);
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to send Discord report. {ex.Message}");
            }
        }

        private async Task SendReportWithDataAsync(ReportData reportData) {
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

                var session    = reportData.Session;
                var images     = reportData.Images;
                var htmlReport = reportGenerator.GenerateHtmlReport(reportData);
                var subject    = $"Night Summary Report - {session.SessionStart:yyyy-MM-dd} - {images.Count} images";
                var duration   = (session.SessionEnd - session.SessionStart).TotalHours;
                var accepted   = images.Count(i => i.Accepted);

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

                var attachmentFileName = $"NightSummary_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.html";
                var sender  = new EmailSender(gmailAddress, appPassword, recipient);
                var success = await sender.SendReportAsync(subject, htmlReport, body.ToString(), attachmentFileName);

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
