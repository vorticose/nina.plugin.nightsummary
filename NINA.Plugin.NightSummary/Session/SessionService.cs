using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.MyPluginProperties;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Interfaces.Mediator;
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
        private readonly ICameraMediator       cameraMediator;

        [ImportingConstructor]
        public SessionService(
            IImageSaveMediator     imageSaveMediator,
            IProfileService        profileService,
            ISafetyMonitorMediator safetyMonitorMediator,
            IFocuserMediator       focuserMediator,
            ITelescopeMediator     telescopeMediator,
            ICameraMediator        cameraMediator,
            ISequenceMediator      sequenceMediator) {

            this.profileService  = profileService;
            this.cameraMediator  = cameraMediator;
            var database         = new SessionDatabase();
            this.collector       = new SessionCollector(imageSaveMediator, sequenceMediator, database);
            this.eventCollector  = new SessionEventCollector(database, safetyMonitorMediator, focuserMediator, telescopeMediator);
            this.reportGenerator = new ReportGenerator();
        }

        public void StartSession(string profileName) {
            var name = profileService?.ActiveProfile?.Name ?? profileName;
            collector.StartSession(name);
            eventCollector.StartSession(collector.GetCurrentSessionId());

            // Capture camera hardware info while the camera is connected.
            // Stored in the session so report generation works correctly even
            // after the camera is disconnected (e.g. when resending old sessions).
            try {
                var camInfo     = cameraMediator?.GetInfo();
                var focalLength = profileService?.ActiveProfile?.TelescopeSettings?.FocalLength ?? 0;
                if (camInfo != null && camInfo.XSize > 0 && camInfo.YSize > 0
                    && camInfo.PixelSize > 0 && focalLength > 0) {
                    collector.Database.UpdateSessionCameraInfo(
                        collector.GetCurrentSessionId(),
                        camInfo.XSize, camInfo.YSize,
                        camInfo.PixelSize, focalLength);
                    Logger.Info($"NightSummary: Stored camera info — {camInfo.XSize}×{camInfo.YSize}px, {camInfo.PixelSize}µm, {focalLength}mm focal");
                }
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: Could not read camera info at session start. {ex.Message}");
            }
        }

        public void EndSession() {
            if (collector.GetCurrentSessionId() == null) {
                Logger.Warning("NightSummary: EndSession called but no active session — nothing to do");
                return;
            }

            var sessionId = collector.GetCurrentSessionId();
            collector.EndSession();
            eventCollector.EndSession();

            var database   = collector.Database;
            var session    = database.GetSession(sessionId);
            var images     = database.GetImagesForSession(sessionId);
            var events     = database.GetEventsForSession(sessionId);

            if (session == null) {
                Logger.Warning($"NightSummary: Session record not found in database for SessionId={sessionId}");
                return;
            }

            // Sync Target Scheduler grading results into our Images table (best-effort, TS optional)
            SyncTsGrading(database, sessionId, session.SessionStart, session.SessionEnd, images);
            // Reload images so report uses updated Accepted/GradingStatus/RejectReason values
            images = database.GetImagesForSession(sessionId);

            var profileId    = profileService?.ActiveProfile?.Id.ToString();
            var tsData       = FetchTsData(images, profileId);
            var cumulative   = database.GetCumulativeIntegrationByTarget(sessionId);
            var history      = BuildSessionHistory(database, images, sessionId);
            var (fovW, fovH) = ComputeCameraFov(session);
            var (lat, lon)   = GetObserverCoords();
            var reportData   = new ReportData {
                Session                      = session,
                Images                       = images,
                Events                       = events,
                TsData                       = tsData,
                CumulativeIntegrationSeconds = cumulative,
                SessionHistory               = history,
                CameraFovWidthDeg            = fovW,
                CameraFovHeightDeg           = fovH,
                ObserverLatitude             = lat,
                ObserverLongitude            = lon,
                ActiveProfileId              = profileId,
                SkippedExposures             = collector.SkippedExposures
            };

            _ = Task.Run(async () => {
                try {
                    await GenerateAndSendAsync(reportData);
                } catch (Exception ex) {
                    Logger.Error($"NightSummary: Unhandled error in report generation. {ex.Message}");
                }
            });
        }

        private async Task GenerateAndSendAsync(ReportData reportData) {
            try {
                Logger.Info($"NightSummary: Generating report for session {reportData.Session.SessionId} (profile: {reportData.ActiveProfileId ?? "unknown"})");
                var htmlReport = await reportGenerator.GenerateHtmlReport(reportData);

                // Show NINA toast notifications and log warnings
                if (reportGenerator.Warnings.Any()) {
                    foreach (var warning in reportGenerator.Warnings) {
                        Logger.Warning($"NightSummary: Report warning — {warning}");
                        Notification.ShowWarning($"Night Summary: {warning}");
                    }
                } else {
                    Notification.ShowSuccess("Night Summary: Report generated successfully");
                }

                // Build list of enabled delivery channels
                var channels = new List<string>();
                if (Settings.Default.SaveReportLocally) channels.Add("Local Save");
                if (Settings.Default.EmailEnabled) channels.Add("Email");
                if (Settings.Default.PushoverEnabled) channels.Add("Pushover");
                if (Settings.Default.DiscordEnabled) channels.Add("Discord");
                if (Settings.Default.DashboardEnabled) channels.Add("Dashboard");
                Logger.Info($"NightSummary: Delivering report to: {(channels.Any() ? string.Join(", ", channels) : "no channels enabled")}");

                var tasks = new List<Task>();
                if (Settings.Default.SaveReportLocally)
                    tasks.Add(SaveReportLocallyAsync(reportData, htmlReport));
                if (Settings.Default.EmailEnabled)
                    tasks.Add(SendReportWithDataAsync(reportData, htmlReport));
                if (Settings.Default.PushoverEnabled)
                    tasks.Add(SendPushoverWithDataAsync(reportData));
                if (Settings.Default.DiscordEnabled)
                    tasks.Add(SendDiscordWithDataAsync(reportData, htmlReport));
                if (Settings.Default.DashboardEnabled)
                    tasks.Add(SendDashboardWithDataAsync(reportData, htmlReport));

                await Task.WhenAll(tasks);
                Notification.ShowSuccess("Night Summary: Report delivered successfully");
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to generate/send report. {ex.Message}");
                Notification.ShowError($"Night Summary: Failed to send report — {ex.Message}");
            }
        }

        /// <summary>
        /// Sends all enabled reports for the most recent session in the given database file.
        /// Used by the "Send Test Report" button in the Options UI.
        /// </summary>
        public async Task SendFromDatabaseAsync(string dbPath, string sessionId = null) {
            try {
                var testDb  = new SessionDatabase(dbPath);
                var session = sessionId != null ? testDb.GetSession(sessionId) : testDb.GetLatestSession();

                if (session == null) {
                    Logger.Warning("NightSummary: No sessions found in test database");
                    return;
                }

                var images = testDb.GetImagesForSession(session.SessionId);
                var events = testDb.GetEventsForSession(session.SessionId);
                Logger.Info($"NightSummary: Sending test report for session {session.SessionId} ({images.Count} images, {events.Count} events)");

                var profileId    = profileService?.ActiveProfile?.Id.ToString();
                var tsData       = FetchTsData(images, profileId);
                var cumulative   = testDb.GetCumulativeIntegrationByTarget(session.SessionId);
                var history      = BuildSessionHistory(testDb, images, session.SessionId);
                var (fovW, fovH) = ComputeCameraFov(session);
                var (lat, lon)   = GetObserverCoords();
                var reportData   = new ReportData {
                    Session                      = session,
                    Images                       = images,
                    Events                       = events,
                    TsData                       = tsData,
                    CumulativeIntegrationSeconds = cumulative,
                    SessionHistory               = history,
                    CameraFovWidthDeg            = fovW,
                    CameraFovHeightDeg           = fovH,
                    ObserverLatitude             = lat,
                    ObserverLongitude            = lon,
                    ActiveProfileId              = profileId,
                    SkippedExposures             = session.SkippedExposures
                };

                var htmlReport = await reportGenerator.GenerateHtmlReport(reportData);

                if (reportGenerator.Warnings.Any()) {
                    foreach (var warning in reportGenerator.Warnings)
                        Notification.ShowWarning($"Night Summary: {warning}");
                } else {
                    Notification.ShowSuccess("Night Summary: Report generated successfully");
                }

                var tasks = new List<Task>();
                if (Settings.Default.SaveReportLocally)
                    tasks.Add(SaveReportLocallyAsync(reportData, htmlReport));
                if (Settings.Default.EmailEnabled)
                    tasks.Add(SendReportWithDataAsync(reportData, htmlReport));
                if (Settings.Default.PushoverEnabled)
                    tasks.Add(SendPushoverWithDataAsync(reportData));
                if (Settings.Default.DiscordEnabled)
                    tasks.Add(SendDiscordWithDataAsync(reportData, htmlReport));
                if (Settings.Default.DashboardEnabled)
                    tasks.Add(SendDashboardWithDataAsync(reportData, htmlReport));

                await Task.WhenAll(tasks);
                Notification.ShowSuccess("Night Summary: Report delivered successfully");
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to send test report. {ex.Message}");
                Notification.ShowError($"Night Summary: Failed to send report — {ex.Message}");
            }
        }

        private async Task SaveReportLocallyAsync(ReportData reportData, string htmlReport = null) {
            try {
                var customPath = Settings.Default.SaveReportPath;
                var saveDir = !string.IsNullOrWhiteSpace(customPath)
                    ? customPath
                    : Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "N.I.N.A.", "Night Summary", "Saved Reports");
                Directory.CreateDirectory(saveDir);

                var filename = $"NightSummary_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.html";
                var filePath = Path.Combine(saveDir, filename);

                htmlReport ??= await reportGenerator.GenerateHtmlReport(reportData);
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

                var title   = $"Night Summary — {reportData.Session.SessionStart:yyyy-MM-dd}";
                var message = BuildSessionSummary(reportData, compact: true);
                var sender  = new PushoverSender(appToken, userKey);
                await sender.SendAsync(title, message);
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to send Pushover notification. {ex.Message}");
            }
        }

        private async Task SendDiscordWithDataAsync(ReportData reportData, string htmlReport = null) {
            try {
                var webhookUrl = Settings.Default.DiscordWebhookUrl;

                if (string.IsNullOrWhiteSpace(webhookUrl)) {
                    Logger.Warning("NightSummary: Discord webhook URL not configured — skipping");
                    return;
                }

                htmlReport ??= await reportGenerator.GenerateHtmlReport(reportData);
                var sender     = new DiscordSender(webhookUrl);
                await sender.SendReportAsync(reportData, htmlReport);
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to send Discord report. {ex.Message}");
            }
        }

        /// <summary>
        /// Uploads all sessions from the given database to the dashboard server.
        /// Skips sessions that already exist on the server (server returns "already_exists").
        /// </summary>
        public async Task<(int uploaded, int skipped, int failed)> UploadAllToDashboardAsync(
            string dbPath, Action<int, int> onProgress = null) {

            var dashboardUrl = Settings.Default.DashboardUrl;
            var apiKey       = Settings.Default.DashboardApiKey;

            if (string.IsNullOrWhiteSpace(dashboardUrl)) {
                Logger.Warning("NightSummary: Dashboard URL not configured");
                return (0, 0, 0);
            }

            var db       = new SessionDatabase(dbPath);
            var sessions = db.GetAllSessions();
            var sender   = new DashboardSender(dashboardUrl, apiKey ?? "");
            int uploaded = 0, skipped = 0, failed = 0;

            for (int i = 0; i < sessions.Count; i++) {
                var session = sessions[i];
                onProgress?.Invoke(i + 1, sessions.Count);

                try {
                    var images     = db.GetImagesForSession(session.SessionId);
                    var events     = db.GetEventsForSession(session.SessionId);
                    var profileId  = profileService?.ActiveProfile?.Id.ToString();
                    var tsData     = FetchTsData(images, profileId);
                    var cumulative = db.GetCumulativeIntegrationByTarget(session.SessionId);
                    var history    = BuildSessionHistory(db, images, session.SessionId);
                    var (fovW, fovH) = ComputeCameraFov(session);
                    var (lat, lon)   = GetObserverCoords();
                    var reportData = new ReportData {
                        Session                      = session,
                        Images                       = images,
                        Events                       = events,
                        TsData                       = tsData,
                        CumulativeIntegrationSeconds = cumulative,
                        SessionHistory               = history,
                        CameraFovWidthDeg            = fovW,
                        CameraFovHeightDeg           = fovH,
                        ObserverLatitude             = lat,
                        ObserverLongitude            = lon,
                        ActiveProfileId              = profileId
                    };

                    var htmlReport = await GenerateReportForDashboard(reportData);
                    bool ok = await sender.SendReportAsync(reportData, htmlReport);
                    if (ok) uploaded++; else skipped++;
                } catch (Exception ex) {
                    Logger.Warning($"NightSummary: Failed to upload session {session.SessionId}. {ex.Message}");
                    failed++;
                }
            }

            Logger.Info($"NightSummary: Dashboard bulk upload complete — {uploaded} uploaded, {skipped} skipped, {failed} failed");
            return (uploaded, skipped, failed);
        }

        private async Task SendDashboardWithDataAsync(ReportData reportData, string htmlReport = null) {
            try {
                var dashboardUrl = Settings.Default.DashboardUrl;
                var apiKey       = Settings.Default.DashboardApiKey;

                if (string.IsNullOrWhiteSpace(dashboardUrl)) {
                    Logger.Warning("NightSummary: Dashboard URL not configured — skipping");
                    return;
                }

                htmlReport ??= await GenerateReportForDashboard(reportData);
                var sender     = new DashboardSender(dashboardUrl, apiKey ?? "");
                await sender.SendReportAsync(reportData, htmlReport);
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to upload to dashboard. {ex.Message}");
            }
        }

        /// <summary>
        /// Generates an HTML report with Tonight's Preview disabled, since the dashboard
        /// is for historical review and the preview section only shows the current night.
        /// </summary>
        private async Task<string> GenerateReportForDashboard(ReportData reportData) {
            var savedPreview = Settings.Default.ShowNextNightPreview;
            try {
                Settings.Default.ShowNextNightPreview = false;
                return await reportGenerator.GenerateHtmlReport(reportData);
            } finally {
                Settings.Default.ShowNextNightPreview = savedPreview;
            }
        }

        private async Task SendReportWithDataAsync(ReportData reportData, string htmlReport = null) {
            try {
                var senderAddress = Settings.Default.SenderAddress;
                var smtpPassword  = Settings.Default.SmtpPassword;
                var recipient     = Settings.Default.RecipientAddress;

                if (string.IsNullOrWhiteSpace(senderAddress) ||
                    string.IsNullOrWhiteSpace(smtpPassword) ||
                    string.IsNullOrWhiteSpace(recipient)) {
                    Logger.Warning("NightSummary: Email settings not configured - skipping report");
                    return;
                }

                var session    = reportData.Session;
                var images     = reportData.Images;
                htmlReport   ??= await reportGenerator.GenerateHtmlReport(reportData);
                var subject    = $"Night Summary Report - {session.SessionStart:yyyy-MM-dd} - {images.Count} images";
                var body       = BuildSessionSummary(reportData, compact: false);

                var attachmentFileName = $"NightSummary_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.html";
                bool useGmail = Settings.Default.UseGmailSmtp;
                var sender = new EmailSender(
                    useGmail ? "smtp.gmail.com" : Settings.Default.SmtpHost,
                    useGmail ? 587 : Settings.Default.SmtpPort,
                    useGmail ? true : Settings.Default.SmtpSsl,
                    senderAddress, smtpPassword, recipient);
                var success = await sender.SendReportAsync(subject, htmlReport, body.ToString(), attachmentFileName);

                if (success) {
                    collector.Database.FinalizeSession(session.SessionId, session.SessionEnd, true);
                    Logger.Info("NightSummary: Report sent and session marked as complete");
                }

            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to generate/send report. {ex.Message}");
            }
        }

        private Dictionary<string, List<TargetSessionHistory>> BuildSessionHistory(SessionDatabase database, List<ImageRecord> images, string sessionId) {
            var result = new Dictionary<string, List<TargetSessionHistory>>(StringComparer.OrdinalIgnoreCase);
            foreach (var targetName in images.Select(i => i.TargetName).Distinct()) {
                result[targetName] = database.GetSessionHistoryForTarget(targetName, sessionId, 5);
            }
            return result;
        }

        /// <summary>
        /// Queries the Target Scheduler database for grading results that overlap the session window
        /// and batch-updates our Images rows. Matched on filter name + timestamp within ±60 s.
        /// Entirely wrapped in try/catch — TS unavailability or schema differences are non-fatal.
        /// </summary>
        private static void SyncTsGrading(SessionDatabase database, string sessionId,
                                           DateTime sessionStart, DateTime sessionEnd,
                                           List<ImageRecord> images) {
            try {
                var tsDb = new TargetSchedulerDatabase();
                if (!tsDb.IsAvailable) return;

                var tsRows = tsDb.GetAcquiredImagesForDateRange(sessionStart, sessionEnd);
                if (tsRows.Count == 0) return;

                var updates = new List<(int imageId, int gradingStatus, string rejectReason)>();
                foreach (var img in images) {
                    // Match by filter (case-insensitive) and timestamp within ±60 s
                    var match = tsRows.FirstOrDefault(r =>
                        string.Equals(r.FilterName, img.Filter, StringComparison.OrdinalIgnoreCase) &&
                        Math.Abs((r.AcquiredAt - img.Timestamp).TotalSeconds) <= 60);

                    if (match != null)
                        updates.Add((img.Id, match.GradingStatus, match.RejectReason));
                }

                if (updates.Count > 0) {
                    database.UpdateImageGradingFromTs(sessionId, updates);
                    Logger.Info($"NightSummary: Synced TS grading for {updates.Count}/{images.Count} images");
                }
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: TS grading sync failed (non-fatal). {ex.Message}");
            }
        }

        private List<TsTargetData> FetchTsData(List<ImageRecord> images, string profileId) {
            var targetNames = images.Select(i => i.TargetName).Distinct();
            var tsDb = new TargetSchedulerDatabase();
            return tsDb.GetProgressForTargets(targetNames, profileId);
        }

        /// <summary>
        /// Computes the imaging camera's field of view in degrees.
        /// Primary source: camera hardware info stored in the session record (captured from the first image).
        /// Fallback: pixel size and focal length from the active NINA profile, with sensor size from FramingAssistantSettings.
        /// Falls back to (1.0, 1.0) if no usable values are found.
        /// </summary>
        private (double widthDeg, double heightDeg) ComputeCameraFov(SessionRecord session = null) {
            try {
                // Prefer values captured from the actual connected camera at session time
                if (session != null && session.CamXSize > 0 && session.CamYSize > 0
                    && session.PixelSizeMicrons > 0 && session.FocalLengthMm > 0) {
                    var ps  = 206.265 * session.PixelSizeMicrons / session.FocalLengthMm;
                    var w   = ps * session.CamXSize  / 3600.0;
                    var h   = ps * session.CamYSize  / 3600.0;
                    Logger.Info($"NightSummary: ComputeCameraFov (from session) — {session.CamXSize}×{session.CamYSize}px, {session.PixelSizeMicrons}µm, {session.FocalLengthMm}mm → FOV={w:F4}° × {h:F4}°");
                    return (w, h);
                }

                // Fallback: read from the active NINA profile
                var pixelSize   = profileService?.ActiveProfile?.CameraSettings?.PixelSize     ?? 0;
                var focalLength = profileService?.ActiveProfile?.TelescopeSettings?.FocalLength ?? 0;
                var camWidth    = profileService?.ActiveProfile?.FramingAssistantSettings?.CameraWidth  ?? 0;
                var camHeight   = profileService?.ActiveProfile?.FramingAssistantSettings?.CameraHeight ?? 0;

                Logger.Info($"NightSummary: ComputeCameraFov (from profile) — pixelSize={pixelSize} focalLength={focalLength} camWidth={camWidth} camHeight={camHeight}");

                if (pixelSize <= 0 || focalLength <= 0 || camWidth <= 0 || camHeight <= 0) {
                    Logger.Warning("NightSummary: ComputeCameraFov — profile values missing, falling back to (1.0, 1.0)");
                    return (1.0, 1.0);
                }

                var plateScale = 206.265 * pixelSize / focalLength;
                var widthDeg   = plateScale * camWidth  / 3600.0;
                var heightDeg  = plateScale * camHeight / 3600.0;
                Logger.Info($"NightSummary: ComputeCameraFov — plateScale={plateScale:F4} arcsec/px, FOV={widthDeg:F4}° × {heightDeg:F4}°");
                return (widthDeg, heightDeg);
            } catch {
                return (1.0, 1.0);
            }
        }

        private (double lat, double lon) GetObserverCoords() {
            try {
                var lat = profileService?.ActiveProfile?.AstrometrySettings?.Latitude  ?? 0;
                var lon = profileService?.ActiveProfile?.AstrometrySettings?.Longitude ?? 0;
                return (lat, lon);
            } catch {
                return (0, 0);
            }
        }

        /// <summary>
        /// Builds ReportData from a database without sending. Used by the preview window.
        /// </summary>
        public async Task<ReportData> BuildReportDataAsync(string dbPath, string sessionId = null) {
            var db      = new SessionDatabase(dbPath);
            var session = sessionId != null ? db.GetSession(sessionId) : db.GetLatestSession();
            if (session == null) return null;

            var images     = db.GetImagesForSession(session.SessionId);
            var events     = db.GetEventsForSession(session.SessionId);
            var profileId  = profileService?.ActiveProfile?.Id.ToString();
            var tsData     = FetchTsData(images, profileId);
            var cumulative = db.GetCumulativeIntegrationByTarget(session.SessionId);
            var history    = BuildSessionHistory(db, images, session.SessionId);
            var (fovW, fovH) = ComputeCameraFov(session);
            var (lat, lon)   = GetObserverCoords();

            return new ReportData {
                Session                      = session,
                Images                       = images,
                Events                       = events,
                TsData                       = tsData,
                CumulativeIntegrationSeconds = cumulative,
                SessionHistory               = history,
                CameraFovWidthDeg            = fovW,
                CameraFovHeightDeg           = fovH,
                ObserverLatitude             = lat,
                ObserverLongitude            = lon,
                ActiveProfileId              = profileId,
                SkippedExposures             = session.SkippedExposures
            };
        }

        /// <summary>
        /// Generates HTML from existing ReportData without sending. Used by the preview window for re-renders.
        /// </summary>
        public async Task<string> GenerateHtmlAsync(ReportData reportData) {
            return await reportGenerator.GenerateHtmlReport(reportData);
        }

        public string GetCurrentSessionId() {
            return collector.GetCurrentSessionId();
        }

        public SessionDatabase Database => collector.Database;

        // ── Summary text helpers ──────────────────────────────────────────────

        /// <summary>
        /// Builds a plain-text session summary for email (compact=false) or Pushover (compact=true).
        /// Mirrors the language, metric names, and formatting conventions of the HTML report.
        /// </summary>
        private static string BuildSessionSummary(ReportData reportData, bool compact) {
            var session  = reportData.Session;
            var images   = reportData.Images;
            var events   = reportData.Events ?? new List<Data.SessionEvent>();

            var totalExpSec  = images.Sum(i => i.ExposureDuration);
            var accepted     = images.Count(i => i.Accepted);
            var hfrImages    = images.Where(i => i.HFR > 0).ToList();
            var rmsImages    = images.Where(i => i.GuidingRMSTotal > 0).ToList();

            var yield = Reporting.YieldCalculator.Calculate(images, events, session.SessionStart, session.SessionEnd);
            var yieldPct        = yield.YieldPct;
            var hasSafetyMonitor = yield.HasSafetyMonitor;

            var targets = images.GroupBy(i => i.TargetName).OrderBy(g => g.Min(i => i.Timestamp)).ToList();
            var sb = new System.Text.StringBuilder();

            if (compact) {
                // ── Pushover ──────────────────────────────────────────────────
                var skippedNote = reportData.SkippedExposures > 0 ? $" ({reportData.SkippedExposures} aborted)" : "";
                sb.AppendLine($"Total Images: {images.Count}{skippedNote}  ·  Total Exposure: {totalExpSec / 3600.0:F1}h");
                var pushoverParts = new List<string>();
                if (hfrImages.Any()) pushoverParts.Add($"Avg HFR: {hfrImages.Average(i => i.HFR):F2}px");
                if (rmsImages.Any()) pushoverParts.Add($"Avg Guiding RMS: {rmsImages.Average(i => i.GuidingRMSTotal):F2}\"");
                pushoverParts.Add($"Yield: {yieldPct:F0}%");
                sb.AppendLine(string.Join("  ·  ", pushoverParts));
                sb.AppendLine();
                foreach (var target in targets) {
                    var tExp = target.Sum(i => i.ExposureDuration);
                    sb.AppendLine($"{target.Key}: {target.Count()} images  ·  {tExp / 3600.0:F1}h");
                }
            } else {
                // ── Email ─────────────────────────────────────────────────────
                var yieldNote = hasSafetyMonitor ? "" : "*";
                sb.AppendLine("Session Overview");
                sb.AppendLine("────────────────");
                var skippedNote2 = reportData.SkippedExposures > 0 ? $" ({reportData.SkippedExposures} aborted)" : "";
                sb.AppendLine($"Total Images:    {images.Count}{skippedNote2}");
                sb.AppendLine($"Total Exposure:  {totalExpSec / 3600.0:F1}h");
                if (hfrImages.Any()) sb.AppendLine($"Avg HFR:         {hfrImages.Average(i => i.HFR):F2}px");
                if (rmsImages.Any()) sb.AppendLine($"Avg Guiding RMS: {rmsImages.Average(i => i.GuidingRMSTotal):F2}\"");
                sb.AppendLine($"Yield:           {yieldPct:F0}%{yieldNote}");
                sb.AppendLine($"Profile:         {session.ProfileName}");
                sb.AppendLine($"Start:           {session.SessionStart:HH:mm}");
                sb.AppendLine($"End:             {session.SessionEnd:HH:mm}");
                if (!hasSafetyMonitor)
                    sb.AppendLine("* Yield calculated without cloud exclusion — no safety monitor events recorded");
                sb.AppendLine();
                sb.AppendLine("Targets Imaged");
                sb.AppendLine("──────────────");
                foreach (var target in targets) {
                    sb.AppendLine(target.Key);
                    var filterGroups = target.GroupBy(i => i.Filter)
                                             .OrderBy(g => Reporting.FilterHelper.SortKey(g.Key)).ThenBy(g => g.Key);
                    var filterParts = filterGroups.Select(g => $"{g.Key}: {g.Count()} \u00d7 {g.First().ExposureDuration:F0}s");
                    sb.AppendLine($"  {string.Join("   ", filterParts)}");
                    var tExp = target.Sum(i => i.ExposureDuration);
                    sb.AppendLine($"  {target.Count()} images  ·  {tExp / 3600.0:F1}h total exposure");
                    sb.AppendLine();
                }
                sb.AppendLine("Full report attached.");
            }

            return sb.ToString();
        }
    }
}
