using NINA.Core.Utility;
using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Reporting {
    /// <summary>
    /// Sends the Night Summary report via Gmail SMTP.
    /// </summary>
    public class EmailSender {

        private readonly string gmailAddress;
        private readonly string gmailAppPassword;
        private readonly string recipientAddress;

        public EmailSender(string gmailAddress, string gmailAppPassword, string recipientAddress) {
            this.gmailAddress = gmailAddress;
            this.gmailAppPassword = gmailAppPassword;
            this.recipientAddress = recipientAddress;
        }

        /// <summary>
        /// Sends the HTML report as an email attachment with a brief plain-text body.
        /// Returns true if successful, false if it failed.
        /// </summary>
        public async Task<bool> SendReportAsync(string subject, string htmlReport, string plainTextBody) {
            try {
                Logger.Info($"NightSummary: Sending report email to {recipientAddress}");

                var message = new MailMessage {
                    From = new MailAddress(gmailAddress, "NINA Night Summary"),
                    Subject = subject,
                    Body = plainTextBody,
                    IsBodyHtml = false
                };

                message.To.Add(recipientAddress);

                var fileBytes = Encoding.UTF8.GetBytes(htmlReport);
                var attachment = new Attachment(new MemoryStream(fileBytes), $"{subject}.html", "text/html");
                message.Attachments.Add(attachment);

                using (var client = new SmtpClient("smtp.gmail.com", 587)) {
                    client.EnableSsl = true;
                    client.Credentials = new NetworkCredential(gmailAddress, gmailAppPassword);
                    client.DeliveryMethod = SmtpDeliveryMethod.Network;

                    await client.SendMailAsync(message);
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