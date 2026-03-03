using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.MyPluginProperties;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.ComponentModel.Composition;
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

            // Send report if enabled and settings are configured
            if (Settings.Default.SendReportOnSessionEnd) {
                Task.Run(async () => await SendReportAsync(sessionId));
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