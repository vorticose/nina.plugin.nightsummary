using NINA.Core.Utility;
using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Reporting {
    /// <summary>
    /// Sends the Night Summary report via SMTP. Defaults to Gmail (smtp.gmail.com:587).
    /// </summary>
    public class EmailSender {

        private readonly string smtpHost;
        private readonly int smtpPort;
        private readonly bool smtpSsl;
        private readonly string senderAddress;
        private readonly string password;
        private readonly string recipientAddress;

        public EmailSender(string smtpHost, int smtpPort, bool smtpSsl, string senderAddress, string password, string recipientAddress) {
            this.smtpHost        = smtpHost;
            this.smtpPort        = smtpPort;
            this.smtpSsl         = smtpSsl;
            this.senderAddress   = senderAddress;
            this.password        = password;
            this.recipientAddress = recipientAddress;
        }

        /// <summary>
        /// Sends a simple test email to verify credentials and connectivity.
        /// Returns true if successful, false if it failed.
        /// </summary>
        public async Task<bool> SendTestAsync() {
            try {
                Logger.Info($"NightSummary: Sending test email to {recipientAddress}");

                using (var message = new MailMessage {
                    From       = new MailAddress(senderAddress, "NINA Night Summary"),
                    Subject    = "Night Summary — Test Email",
                    Body       = "This is a test email from Night Summary. If you received this, your email settings are configured correctly.",
                    IsBodyHtml = false
                }) {
                    message.To.Add(recipientAddress);

                    using (var client = new SmtpClient(smtpHost, smtpPort)) {
                        client.EnableSsl      = smtpSsl;
                        client.Credentials    = new NetworkCredential(senderAddress, password);
                        client.DeliveryMethod = SmtpDeliveryMethod.Network;
                        await client.SendMailAsync(message);
                    }
                }

                Logger.Info("NightSummary: Test email sent successfully");
                return true;

            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to send test email. {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sends the HTML report as an email attachment with a brief plain-text body.
        /// Returns true if successful, false if it failed.
        /// </summary>
        public async Task<bool> SendReportAsync(string subject, string htmlReport, string plainTextBody, string attachmentFileName = null) {
            try {
                Logger.Info($"NightSummary: Sending report email to {recipientAddress}");

                using (var message = new MailMessage {
                    From       = new MailAddress(senderAddress, "NINA Night Summary"),
                    Subject    = subject,
                    Body       = plainTextBody,
                    IsBodyHtml = false
                }) {
                    message.To.Add(recipientAddress);

                    var fileBytes  = Encoding.UTF8.GetBytes(htmlReport);
                    var attachment = new Attachment(new MemoryStream(fileBytes), attachmentFileName ?? $"NightSummary_generated-{DateTime.Now:HH-mm-ss}.html", "text/html");
                    message.Attachments.Add(attachment);

                    using (var client = new SmtpClient(smtpHost, smtpPort)) {
                        client.EnableSsl      = smtpSsl;
                        client.Credentials    = new NetworkCredential(senderAddress, password);
                        client.DeliveryMethod = SmtpDeliveryMethod.Network;
                        await client.SendMailAsync(message);
                    }
                }

                Logger.Info("NightSummary: Report email sent successfully");
                return true;

            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to send report email. {ex.Message}");
                return false;
            }
        }
    }
}
