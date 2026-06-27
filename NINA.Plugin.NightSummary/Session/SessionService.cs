using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.Interfaces;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Session {

    [Export(typeof(SessionService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class SessionService {

        private readonly SessionCollector      collector;
        private readonly SessionEventCollector eventCollector;
        private readonly ReportGenerator       reportGenerator;
        private readonly IProfileService        profileService;
        private readonly ICameraMediator        cameraMediator;
        private readonly ITelescopeMediator     telescopeMediator;
        private readonly ISequenceMediator      sequenceMediator;
        private readonly IFilterWheelMediator   filterWheelMediator;
        private readonly IFocuserMediator       focuserMediator;
        private readonly IRotatorMediator       rotatorMediator;
        private readonly IGuiderMediator        guiderMediator;
        private readonly ISafetyMonitorMediator safetyMonitorMediator;
        private readonly IDomeMediator          domeMediator;
        private readonly IFlatDeviceMediator    flatDeviceMediator;
        private readonly IWeatherDataMediator   weatherDataMediator;
        private readonly ISwitchMediator        switchMediator;
        private readonly IMessageBroker         messageBroker;
        private LiveStackCapture               liveStackCapture;
        private bool                           sequenceFinishedSubscribed;

        // Tracks fire-and-forget report-generation Tasks spawned from EndSession so
        // Teardown can wait for in-flight reports rather than dropping them when NINA
        // closes immediately after a session ends.
        private readonly object _pendingReportsLock = new object();
        private readonly List<Task> _pendingReports = new List<Task>();

        private static NightSummarySettings S => SettingsManager.Instance.Current;

        [ImportingConstructor]
        public SessionService(
            IImageSaveMediator     imageSaveMediator,
            IProfileService        profileService,
            ISafetyMonitorMediator safetyMonitorMediator,
            IFocuserMediator       focuserMediator,
            ITelescopeMediator     telescopeMediator,
            ICameraMediator        cameraMediator,
            ISequenceMediator      sequenceMediator,
            IFilterWheelMediator   filterWheelMediator,
            IRotatorMediator       rotatorMediator,
            IGuiderMediator        guiderMediator,
            IDomeMediator          domeMediator,
            IFlatDeviceMediator    flatDeviceMediator,
            IWeatherDataMediator   weatherDataMediator,
            ISwitchMediator        switchMediator,
            IMessageBroker         messageBroker)
            : this(imageSaveMediator, profileService, safetyMonitorMediator,
                   focuserMediator, telescopeMediator, cameraMediator, sequenceMediator,
                   filterWheelMediator, rotatorMediator, guiderMediator,
                   domeMediator, flatDeviceMediator, weatherDataMediator, switchMediator,
                   messageBroker, databasePath: null) { }

        /// <summary>
        /// Internal constructor for test replay. Accepts an explicit database path
        /// to isolate tests from the production LOCALAPPDATA database.
        /// When databasePath is null, uses the default production path.
        /// </summary>
        internal SessionService(
            IImageSaveMediator     imageSaveMediator,
            IProfileService        profileService,
            ISafetyMonitorMediator safetyMonitorMediator,
            IFocuserMediator       focuserMediator,
            ITelescopeMediator     telescopeMediator,
            ICameraMediator        cameraMediator,
            ISequenceMediator      sequenceMediator,
            IFilterWheelMediator   filterWheelMediator,
            IRotatorMediator       rotatorMediator,
            IGuiderMediator        guiderMediator,
            IDomeMediator          domeMediator,
            IFlatDeviceMediator    flatDeviceMediator,
            IWeatherDataMediator   weatherDataMediator,
            ISwitchMediator        switchMediator,
            IMessageBroker         messageBroker,
            string                 databasePath) {

            this.profileService        = profileService;
            this.cameraMediator        = cameraMediator;
            this.telescopeMediator     = telescopeMediator;
            this.sequenceMediator      = sequenceMediator;
            this.filterWheelMediator   = filterWheelMediator;
            this.focuserMediator       = focuserMediator;
            this.rotatorMediator       = rotatorMediator;
            this.guiderMediator        = guiderMediator;
            this.safetyMonitorMediator = safetyMonitorMediator;
            this.domeMediator          = domeMediator;
            this.flatDeviceMediator    = flatDeviceMediator;
            this.weatherDataMediator   = weatherDataMediator;
            this.switchMediator        = switchMediator;
            this.messageBroker         = messageBroker;
            var database        = databasePath != null ? new SessionDatabase(databasePath) : new SessionDatabase();
            this.collector       = new SessionCollector(imageSaveMediator, sequenceMediator, database);
            this.eventCollector  = new SessionEventCollector(database, safetyMonitorMediator, focuserMediator, telescopeMediator);
            this.reportGenerator = new ReportGenerator(
                new Server.NinaPluginSettings(profileService),
                new Server.NinaDashboardLogger(),
                new TargetSchedulerDatabase());

            // NOTE: SequenceFinished subscription happens in StartSession, not here.
            // At plugin-load time NINA's SequenceMediator has no backing delegate yet and
            // subscribing NREs inside the mediator's add accessor. By the time the Night
            // Summary Start instruction runs the sequencer is fully initialized. Nothing
            // to clean up before the first session begins anyway.

            Logger.Info($"NightSummary: SessionService created — messageBroker={messageBroker != null}");
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

            CaptureEquipmentNames();
            collector.FirstImageSaved += OnFirstImageSaved;

            // Subscribe to SequenceFinished lazily — safe now that sequencer is initialized.
            if (!sequenceFinishedSubscribed && sequenceMediator != null) {
                try {
                    sequenceMediator.SequenceFinished += OnSequenceFinished;
                    sequenceFinishedSubscribed = true;
                } catch (Exception ex) {
                    Logger.Warning($"NightSummary: Could not subscribe to SequenceFinished: {ex.Message}");
                }
            }

            if (messageBroker != null && S.ShowLiveStackImages) {
                liveStackCapture = new LiveStackCapture(messageBroker);
                Logger.Info("NightSummary: LiveStack capture started for this session");
            } else {
                Logger.Info($"NightSummary: LiveStack capture skipped — broker={messageBroker != null}, setting={S.ShowLiveStackImages}");
            }
        }

        public void EndSession() {
            if (collector.GetCurrentSessionId() == null) {
                Logger.Warning("NightSummary: EndSession called but no active session — nothing to do");
                return;
            }

            var sessionId = collector.GetCurrentSessionId();

            collector.FirstImageSaved -= OnFirstImageSaved;

            // Fill in any equipment that wasn't connected at session start
            CaptureEquipmentNames();

            collector.EndSession();
            eventCollector.EndSession();
            var liveStackImages = liveStackCapture?.StopAndCollect() ?? new List<LiveStackImage>();
            liveStackCapture = null;

            // Apply thumbnail retention policy after the session is closed. Best-effort:
            // failures here never block report delivery. See RAW_THUMBNAILS_DESIGN.md.
            try {
                var thumbsRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NINA", "NightSummary", "thumbs");
                ThumbnailRetention.Apply(thumbsRoot, S, sid => collector.Database.GetSession(sid)?.SessionStart);
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: ThumbnailRetention threw on session-end: {ex.Message}");
            }

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

            // Parse NINA logs for per-event overhead timing data
            List<TimingEvent> timingEvents;
            try {
                Logger.Info($"NightSummary: EndSession — parsing logs for session {sessionId} (start={session.SessionStart:o}, end={session.SessionEnd:o}, images={images.Count})");
                timingEvents = NinaLogParser.Parse(session.SessionStart, session.SessionEnd, images.Count);
                Logger.Info($"NightSummary: EndSession — parser returned {timingEvents.Count} events");
                if (timingEvents.Any())
                    database.SaveTimingEvents(sessionId, timingEvents);
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: Log parsing failed — overhead breakdown will be unavailable. {ex.Message}");
                Logger.Warning($"NightSummary: Log parsing stack trace: {ex.StackTrace}");
                timingEvents = new List<TimingEvent>();
            }

            var profileId    = profileService?.ActiveProfile?.Id.ToString();
            var tsData       = FetchTsData(images, profileId);
            var cumulative   = database.GetCumulativeIntegrationByTarget(sessionId);
            var history          = BuildSessionHistory(database, images, sessionId);
            var historyAggregate = BuildSessionHistoryAggregate(database, images, sessionId);
            var (fovW, fovH) = ComputeCameraFov(session);
            var (lat, lon)   = GetObserverCoords();
            var reportData   = new ReportData {
                Session                      = session,
                Images                       = images,
                Events                       = events,
                TsData                       = tsData,
                CumulativeIntegrationSeconds = cumulative,
                SessionHistory               = history,
                SessionHistoryAggregate      = historyAggregate,
                CameraFovWidthDeg            = fovW,
                CameraFovHeightDeg           = fovH,
                ObserverLatitude             = lat,
                ObserverLongitude            = lon,
                ActiveProfileId              = profileId,
                SkippedExposures             = collector.SkippedExposures,
                TimingEvents                 = timingEvents,
                Equipment                    = BuildEquipmentDictionary(session),
                LiveStackImages              = liveStackImages
            };

            var generation = Task.Run(async () => {
                try {
                    await GenerateAndSendAsync(reportData);
                } catch (Exception ex) {
                    Logger.Error($"NightSummary: Unhandled error in report generation. {ex.Message}");
                }
            });
            lock (_pendingReportsLock) _pendingReports.Add(generation);
            // Self-cleanup so the list doesn't grow unbounded across many sessions.
            _ = generation.ContinueWith(t => {
                lock (_pendingReportsLock) _pendingReports.Remove(t);
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// Awaits any outstanding fire-and-forget report-generation Tasks (up to a timeout)
        /// so plugin Teardown can let post-session reports finish sending instead of
        /// abandoning them when NINA closes.
        /// </summary>
        public async Task WaitForPendingReportsAsync(TimeSpan timeout) {
            Task[] snapshot;
            lock (_pendingReportsLock) snapshot = _pendingReports.ToArray();
            if (snapshot.Length == 0) return;
            Logger.Info($"NightSummary: Waiting for {snapshot.Length} in-flight report(s) before teardown (timeout {timeout.TotalSeconds:F0}s)");
            try {
                await Task.WhenAny(Task.WhenAll(snapshot), Task.Delay(timeout));
            } catch { /* individual tasks already log their own errors */ }
        }

        /// <summary>
        /// SequenceFinished fires on true stops, WhenUnsafe restarts, manual pause/resume,
        /// and any other cancel-and-restart pattern. We intentionally do nothing here —
        /// only the End Session instruction ends an active session. This means sessions
        /// survive restarts cleanly. Sessions where End never runs are left open in the DB
        /// (orphaned) and the report will note that the End instruction was missing.
        /// </summary>
        private Task OnSequenceFinished(object sender, EventArgs e) {
            var sessionId = collector.GetCurrentSessionId();
            if (sessionId == null) return Task.CompletedTask;

            Logger.Warning($"NightSummary: Sequence finished with active session {sessionId} — End Session instruction did not run. Session data preserved; use Resend Previous Session for a report.");
            return Task.CompletedTask;
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
                if (S.SaveReportLocally) channels.Add("Local Save");
                if (S.EmailEnabled) channels.Add("Email");
                if (S.PushoverEnabled) channels.Add("Pushover");
                if (S.DiscordEnabled) channels.Add("Discord");
                if (S.DashboardEnabled) channels.Add("Dashboard");
                Logger.Info($"NightSummary: Delivering report to: {(channels.Any() ? string.Join(", ", channels) : "no channels enabled")}");

                var tasks = new List<Task>();
                if (S.SaveReportLocally)
                    tasks.Add(SaveReportLocallyAsync(reportData, htmlReport));
                if (S.EmailEnabled)
                    tasks.Add(SendReportWithDataAsync(reportData, htmlReport));
                if (S.PushoverEnabled)
                    tasks.Add(SendPushoverWithDataAsync(reportData));
                if (S.DiscordEnabled)
                    tasks.Add(SendDiscordWithDataAsync(reportData, htmlReport));
                if (S.DashboardEnabled)
                    tasks.Add(SendDashboardWithDataAsync(reportData, htmlReport));

                // Always save a copy to the local dashboard reports directory
                // so the embedded dashboard server can serve it
                tasks.Add(SaveReportForDashboardAsync(reportData.Session.SessionId, htmlReport, reportData.LiveStackImages,
                                                      reportData.ObserverLatitude, reportData.ObserverLongitude));

                await Task.WhenAll(tasks);
                Notification.ShowSuccess("Night Summary: Report delivered successfully");

                // Push companion notification AFTER the report file is on disk
                // so the companion's pull picks up the fresh DB + new HTML in
                // one round trip. Fire-and-forget — never block / never throw.
                _ = NotifyAllPairedCompanionsAsync();
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to generate/send report. {ex.Message}");
                Notification.ShowError($"Night Summary: Failed to send report — {ex.Message}");
            }
        }

        // Pings every paired companion's auto-detected push URL so they pull
        // fresh data immediately instead of waiting for their scheduled poll.
        // URLs come from CompanionTokenStore — captured at pair time + refreshed
        // on every authenticated request, so they self-heal across DHCP / port
        // changes. No manual configuration anywhere.
        //
        // Hard 5s timeout per companion, fire-and-forget. Failures are logged
        // but never surfaced to the user — companion's own scheduler catches
        // up on the next interval.
        private static async Task NotifyAllPairedCompanionsAsync() {
            IReadOnlyList<CompanionTokenEntry> entries;
            try {
                entries = CompanionTokenStore.Instance.List();
            } catch (Exception ex) {
                Logger.Info($"NightSummary: Companion notify skipped ({ex.Message}) — token store unavailable");
                return;
            }
            var tasks = new List<Task>();
            foreach (var e in entries) {
                if (e.IsRevoked || !e.IsPaired) continue;
                if (string.IsNullOrWhiteSpace(e.PushUrl)) continue;
                tasks.Add(NotifyCompanionAsync(e.PushUrl, e.CompanionName ?? e.Id));
            }
            if (tasks.Count == 0) {
                Logger.Info("NightSummary: No paired companions with a known push URL — skipping notify.");
                return;
            }
            try { await Task.WhenAll(tasks); } catch { /* per-call errors already logged */ }
        }

        private static async Task NotifyCompanionAsync(string companionUrl, string label) {
            try {
                var url = companionUrl.TrimEnd('/') + "/api/companion/sync";
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url) {
                    Content = new System.Net.Http.StringContent(""),
                };
                // Tag the request so the companion can distinguish push-driven
                // triggers from user-clicked manual syncs. Lets users disable
                // push without losing the Sync button.
                req.Headers.TryAddWithoutValidation("X-Sync-Trigger", "push");
                using var resp = await http.SendAsync(req);
                if (resp.IsSuccessStatusCode) {
                    Logger.Info($"NightSummary: Companion '{label}' notified at {url} (HTTP {(int)resp.StatusCode})");
                } else {
                    Logger.Warning($"NightSummary: Companion '{label}' notify returned HTTP {(int)resp.StatusCode} for {url}");
                }
            } catch (Exception ex) {
                Logger.Info($"NightSummary: Companion '{label}' notify failed ({ex.Message}) — will pull on schedule");
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
                var history          = BuildSessionHistory(testDb, images, session.SessionId);
                var historyAggregate = BuildSessionHistoryAggregate(testDb, images, session.SessionId);
                var (fovW, fovH) = ComputeCameraFov(session);
                var (lat, lon)   = GetObserverCoords();
                // Fallback for test reports when no profile location is configured
                if (lat == 0 && lon == 0) { lat = 32.9; lon = -105.5; }

                // Always re-parse timing events from logs to pick up parser improvements.
                // Falls back to cached DB data only if the log file is no longer available.
                List<TimingEvent> timingEvents;
                try {
                    timingEvents = NinaLogParser.Parse(session.SessionStart, session.SessionEnd, images.Count);
                    if (timingEvents.Any()) {
                        testDb.ClearTimingEvents(session.SessionId);
                        testDb.SaveTimingEvents(session.SessionId, timingEvents);
                    }
                } catch (Exception ex) {
                    Logger.Warning($"NightSummary: Log re-parse failed, using cached data — {ex.Message}");
                    timingEvents = null;  // fall through to DB lookup below
                }
                // If log parsing returned nothing (no log file, or empty), use cached DB data
                if (timingEvents == null || !timingEvents.Any()) {
                    timingEvents = testDb.GetTimingEventsForSession(session.SessionId);
                }

                var reportData   = new ReportData {
                    Session                      = session,
                    Images                       = images,
                    Events                       = events,
                    TsData                       = tsData,
                    CumulativeIntegrationSeconds = cumulative,
                    SessionHistory               = history,
                    SessionHistoryAggregate      = historyAggregate,
                    CameraFovWidthDeg            = fovW,
                    CameraFovHeightDeg           = fovH,
                    ObserverLatitude             = lat,
                    ObserverLongitude            = lon,
                    ActiveProfileId              = profileId,
                    SkippedExposures             = session.SkippedExposures,
                    Equipment                    = BuildEquipmentDictionary(session),
                    TimingEvents                 = timingEvents
                };

                // Try to load persisted live stack masters for this session
                var (resolvedDir, resolvedFilename) = ResolveReportSavePath(reportData, scanForExisting: true);
                if (resolvedDir != null) {
                    reportData.LiveStackImages = LoadLiveStackMasters(resolvedDir, resolvedFilename);
                }

                var htmlReport = await reportGenerator.GenerateHtmlReport(reportData);

                if (reportGenerator.Warnings.Any()) {
                    foreach (var warning in reportGenerator.Warnings)
                        Notification.ShowWarning($"Night Summary: {warning}");
                } else {
                    Notification.ShowSuccess("Night Summary: Report generated successfully");
                }

                var tasks = new List<Task>();
                if (S.SaveReportLocally)
                    tasks.Add(SaveReportLocallyAsync(reportData, htmlReport));
                if (S.EmailEnabled)
                    tasks.Add(SendReportWithDataAsync(reportData, htmlReport));
                if (S.PushoverEnabled)
                    tasks.Add(SendPushoverWithDataAsync(reportData));
                if (S.DiscordEnabled)
                    tasks.Add(SendDiscordWithDataAsync(reportData, htmlReport));
                if (S.DashboardEnabled)
                    tasks.Add(SendDashboardWithDataAsync(reportData, htmlReport));

                tasks.Add(SaveReportForDashboardAsync(reportData.Session.SessionId, htmlReport, reportData.LiveStackImages,
                                                      reportData.ObserverLatitude, reportData.ObserverLongitude));

                await Task.WhenAll(tasks);
                Notification.ShowSuccess("Night Summary: Report delivered successfully");
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to send test report. {ex.Message}");
                Notification.ShowError($"Night Summary: Failed to send report — {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves the save directory and filename for a report based on current settings.
        /// The resolved pattern becomes a session folder, with the HTML file inside using the same name.
        /// e.g. pattern "$DATEMINUS12$" → Saved Reports/NightSummary_2026-03-31/NightSummary_2026-03-31.html
        /// </summary>
        /// <summary>
        /// Resolves the save directory and filename for a report.
        /// When scanForExisting is true (used by preview/resend to load assets),
        /// falls back to scanning for an existing folder by session date if the
        /// resolved path doesn't exist. When false (used by live save), always
        /// creates a new folder.
        /// </summary>
        private (string dir, string filename) ResolveReportSavePath(ReportData reportData, bool scanForExisting = false) {
            var basePath = S.SaveReportPath;
            var saveRoot = !string.IsNullOrWhiteSpace(basePath)
                ? basePath
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "N.I.N.A.", "Night Summary", "Saved Reports");

            var pattern = S.SaveReportFilePattern;
            var context = BuildPatternContext(reportData);
            string folderName;
            if (!string.IsNullOrWhiteSpace(pattern)) {
                folderName = ResolveFilePattern(pattern, context);
            } else {
                // Strip .html from default filename to use as folder name
                folderName = Path.GetFileNameWithoutExtension(GetReportFileName(reportData));
            }

            var sessionDir = Path.Combine(saveRoot, folderName);
            var filename = folderName;
            // If pattern included path separators, use only the last segment as filename
            if (filename.Contains(Path.DirectorySeparatorChar) || filename.Contains(Path.AltDirectorySeparatorChar)) {
                filename = Path.GetFileName(filename);
            }

            // Only scan for existing folders when loading assets (preview/resend), not when saving
            if (scanForExisting && !Directory.Exists(sessionDir) && reportData?.Session != null) {
                Logger.Info($"NightSummary: Resolved report path doesn't exist: {sessionDir}, scanning for date match...");
                var found = FindSavedReportDir(saveRoot, reportData.Session.SessionStart);
                if (found != null) {
                    Logger.Info($"NightSummary: Found saved report folder by date: {found}");
                    sessionDir = found;
                    filename = Path.GetFileName(found);
                } else {
                    Logger.Info($"NightSummary: No saved report folder found for session date {reportData.Session.SessionStart:yyyy-MM-dd}");
                }
            }

            return (sessionDir, filename + ".html");
        }

        /// <summary>
        /// Scans the save root for existing report folders whose name contains the session date.
        /// Checks both the session start date and the next calendar day (since reports are
        /// typically generated in the early morning after an overnight session).
        /// Returns the most recently modified match, or null if none found.
        /// </summary>
        private static string FindSavedReportDir(string saveRoot, DateTime sessionStart) {
            if (!Directory.Exists(saveRoot)) return null;

            var dateStr = sessionStart.ToString("yyyy-MM-dd");
            var nextDayStr = sessionStart.AddDays(1).ToString("yyyy-MM-dd");
            try {
                var matches = Directory.GetDirectories(saveRoot)
                    .Where(d => {
                        var name = Path.GetFileName(d);
                        return name.Contains(dateStr) || name.Contains(nextDayStr);
                    })
                    .OrderByDescending(d => Directory.GetLastWriteTime(d))
                    .ToList();
                return matches.FirstOrDefault();
            } catch {
                return null;
            }
        }

        private async Task SaveReportLocallyAsync(ReportData reportData, string htmlReport = null) {
            try {
                var (saveDir, filename) = ResolveReportSavePath(reportData);
                Directory.CreateDirectory(saveDir);
                var filePath = Path.Combine(saveDir, filename);

                htmlReport ??= await reportGenerator.GenerateHtmlReport(reportData);
                await File.WriteAllTextAsync(filePath, htmlReport);
                Logger.Info($"NightSummary: Report saved locally to {filePath}");

                // Save live stack master images alongside the report
                SaveLiveStackMasters(saveDir, filename, reportData);
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to save report locally. {ex.Message}");
            }
        }

        /// <summary>
        /// Saves an HTML report to the local dashboard reports directory so the embedded
        /// DashboardServer can serve it. This is always called on report generation,
        /// independent of the user's "Save Report Locally" setting.
        /// </summary>
        private async Task SaveReportForDashboardAsync(string sessionId, string htmlReport, List<LiveStackImage> liveStackImages = null, double observerLatitude = 0, double observerLongitude = 0) {
            try {
                var reportsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NINA", "NightSummary", "reports");
                Directory.CreateDirectory(reportsDir);
                var filePath = Path.Combine(reportsDir, $"{sessionId}.html");
                await File.WriteAllTextAsync(filePath, htmlReport);

                // Save live stack masters per-session for the dashboard to serve
                if (liveStackImages != null && liveStackImages.Count > 0) {
                    var lsDir = Path.Combine(reportsDir, "livestack", sessionId);
                    Directory.CreateDirectory(lsDir);
                    var manifest = new List<Dictionary<string, object>>();
                    foreach (var img in liveStackImages) {
                        var data = img.MasterJpegData ?? img.JpegData;
                        var safeName = SanitizeFileName($"{img.Target}_{img.Filter}");
                        var jpgFile = safeName + ".jpg";
                        File.WriteAllBytes(Path.Combine(lsDir, jpgFile), data);
                        manifest.Add(new Dictionary<string, object> {
                            ["file"] = jpgFile,
                            ["target"] = img.Target,
                            ["filter"] = img.Filter,
                            ["isMonochrome"] = img.IsMonochrome,
                            ["stackCount"] = img.StackCount,
                            ["redStackCount"] = img.RedStackCount,
                            ["greenStackCount"] = img.GreenStackCount,
                            ["blueStackCount"] = img.BlueStackCount
                        });
                    }
                    var lsJson = System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(Path.Combine(lsDir, "livestack.json"), lsJson);
                    Logger.Debug($"NightSummary: Saved {liveStackImages.Count} livestack master(s) to dashboard: {lsDir}");
                }

                // Save settings sidecar so dashboard knows what was used
                var settings = new {
                    reportDetailLevel      = S.ReportDetailLevel,
                    reportLightMode        = S.ReportLightMode,
                    expandSectionsDefault  = S.ExpandSectionsDefault,
                    showMoonCurve          = S.ShowMoonCurve,
                    showOverheadBreakdown  = S.ShowOverheadBreakdown,
                    showSkyThumbnails      = S.ShowSkyThumbnails,
                    showLiveStackImages    = S.ShowLiveStackImages,
                    showSessionHistory     = S.ShowSessionHistory,
                    showAltitudeChart      = S.ShowAltitudeChart,
                    showMinAltitude        = S.ShowMinAltitude,
                    showTSProgressBars     = S.ShowTSProgressBars,
                    showStarCountCV        = S.ShowStarCountCV,
                    showHFRGraph           = S.ShowHFRGraph,
                    showChartAfMarkers     = S.ShowChartAfMarkers,
                    showChartFlipMarkers   = S.ShowChartFlipMarkers,
                    showChartRoofMarkers   = S.ShowChartRoofMarkers,
                    showPerTargetIQ        = S.ShowPerTargetIQ,
                    showEquipmentProfile   = S.ShowEquipmentProfile,
                    timelineAltitudeDefault = S.TimelineAltitudeDefault,
                    chartXAxisMetric       = S.ChartXAxisMetric,
                    chartPrimaryMetric     = S.ChartPrimaryMetric,
                    chartSecondaryMetric   = S.ChartSecondaryMetric,
                    additionalChartConfigs = S.AdditionalChartConfigs,
                    equipmentVisibleFields = S.EquipmentVisibleFields,
                    filterClassifications  = S.FilterClassifications,
                    filterTypeOverrides    = S.FilterTypeOverrides,
                    equipmentOverrides     = S.EquipmentOverrides,
                    // Raw image thumbnails — see RAW_THUMBNAILS_DESIGN.md.
                    captureRawThumbnails    = S.CaptureRawThumbnails,
                    captureMediumThumbnails = S.CaptureMediumThumbnails,
                    thumbnailRetentionMode  = S.ThumbnailRetentionMode,
                    thumbnailRetentionDays  = S.ThumbnailRetentionDays,
                    thumbnailRetentionMaxGB = S.ThumbnailRetentionMaxGB,
                    thumbnailStorageDir     = S.ThumbnailStorageDir,
                    // Stamped on the sidecar so the companion's local regen
                    // path can render altitude curves without contacting the
                    // primary or NINA. (CompanionPluginSettings has no access
                    // to NINA's profile.) Stored as session-time values rather
                    // than live-profile reads — closer to "what the report
                    // was generated with" anyway.
                    observerLatitude       = observerLatitude,
                    observerLongitude      = observerLongitude
                };
                var json = System.Text.Json.JsonSerializer.Serialize(settings,
                    new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                await File.WriteAllTextAsync(Path.Combine(reportsDir, $"{sessionId}.settings.json"), json);

                Logger.Debug($"NightSummary: Report saved to dashboard directory: {filePath}");
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to save report to dashboard directory. {ex.Message}");
            }
        }

        private static void SaveLiveStackMasters(string reportDir, string reportFilename, ReportData reportData) {
            if (reportData.LiveStackImages == null || reportData.LiveStackImages.Count == 0) return;

            var assetsDir = Path.Combine(reportDir, "assets");

            try {
                Directory.CreateDirectory(assetsDir);
                var manifest = new List<Dictionary<string, object>>();
                foreach (var img in reportData.LiveStackImages) {
                    var data = img.MasterJpegData ?? img.JpegData;
                    var safeName = SanitizeFileName($"{img.Target}_{img.Filter}");
                    var jpgFile = safeName + ".jpg";
                    File.WriteAllBytes(Path.Combine(assetsDir, jpgFile), data);

                    manifest.Add(new Dictionary<string, object> {
                        ["file"] = jpgFile,
                        ["target"] = img.Target,
                        ["filter"] = img.Filter,
                        ["isMonochrome"] = img.IsMonochrome,
                        ["stackCount"] = img.StackCount,
                        ["redStackCount"] = img.RedStackCount,
                        ["greenStackCount"] = img.GreenStackCount,
                        ["blueStackCount"] = img.BlueStackCount
                    });
                }

                var json = System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(assetsDir, "livestack.json"), json);
                Logger.Info($"NightSummary: Saved {reportData.LiveStackImages.Count} live stack master(s) to {assetsDir}");
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: Failed to save live stack masters: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads live stack master images from an assets directory alongside a saved report.
        /// Returns empty list if no assets found.
        /// </summary>
        internal static List<LiveStackImage> LoadLiveStackMasters(string reportDir, string reportFilename) {
            var assetsDir = Path.Combine(reportDir, "assets");
            var manifestPath = Path.Combine(assetsDir, "livestack.json");

            if (!File.Exists(manifestPath)) {
                Logger.Info($"NightSummary: No livestack.json manifest at {manifestPath}");
                return new List<LiveStackImage>();
            }

            try {
                var json = File.ReadAllText(manifestPath);
                var entries = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, System.Text.Json.JsonElement>>>(json);
                var images = new List<LiveStackImage>();

                foreach (var entry in entries) {
                    var jpgPath = Path.Combine(assetsDir, entry["file"].GetString());
                    if (!File.Exists(jpgPath)) {
                        Logger.Warning($"NightSummary: Live stack JPEG missing: {jpgPath}");
                        continue;
                    }

                    var masterData = File.ReadAllBytes(jpgPath);
                    // Re-scale master to report-embed size for inline base64
                    var reportData = LiveStackCapture.ScaleJpegForReport(masterData);
                    images.Add(new LiveStackImage {
                        Target = entry["target"].GetString(),
                        Filter = entry["filter"].GetString(),
                        IsMonochrome = entry["isMonochrome"].GetBoolean(),
                        JpegData = reportData,
                        MasterJpegData = masterData,
                        StackCount = entry["stackCount"].GetInt32(),
                        RedStackCount = entry.TryGetValue("redStackCount", out var r) && r.ValueKind != System.Text.Json.JsonValueKind.Null ? r.GetInt32() : null,
                        GreenStackCount = entry.TryGetValue("greenStackCount", out var g) && g.ValueKind != System.Text.Json.JsonValueKind.Null ? g.GetInt32() : null,
                        BlueStackCount = entry.TryGetValue("blueStackCount", out var b) && b.ValueKind != System.Text.Json.JsonValueKind.Null ? b.GetInt32() : null,
                    });
                }

                Logger.Info($"NightSummary: Loaded {images.Count} live stack master(s) from {assetsDir}");
                return images;
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: Failed to load live stack masters: {ex.Message}");
                return new List<LiveStackImage>();
            }
        }

        private static string SanitizeFileName(string name) {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
        }

        /// <summary>
        /// Resolves NINA-style $$PATTERN$$ variables in a file pattern string.
        /// Date/time patterns always resolve. Equipment/session patterns resolve
        /// when context values are provided (null values become empty strings).
        /// </summary>
        internal static string ResolveFilePattern(string pattern, Dictionary<string, string> context = null) {
            var now = DateTime.Now;
            var utcNow = DateTime.UtcNow;
            var minus12 = now.AddHours(-12);

            var result = pattern
                .Replace("$$DATEMINUS12$$", minus12.ToString("yyyy-MM-dd"))
                .Replace("$$DATE$$", now.ToString("yyyy-MM-dd"))
                .Replace("$$DATEUTC$$", utcNow.ToString("yyyy-MM-dd"))
                .Replace("$$DATETIME$$", now.ToString("yyyy-MM-dd_HH-mm-ss"))
                .Replace("$$TIME$$", now.ToString("HH-mm-ss"))
                .Replace("$$TIMEUTC$$", utcNow.ToString("HH-mm-ss"))
                .Replace("$$CAMERA$$", context?.GetValueOrDefault("$$CAMERA$$") ?? "")
                .Replace("$$TELESCOPE$$", context?.GetValueOrDefault("$$TELESCOPE$$") ?? "")
                .Replace("$$SEQUENCETITLE$$", context?.GetValueOrDefault("$$SEQUENCETITLE$$") ?? "");

            // Sanitize each path segment
            var segments = result.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            return Path.Combine(segments.Select(s => string.Join("_",
                s.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim()
            ).ToArray());
        }

        /// <summary>
        /// Builds the context dictionary for pattern resolution from available session data.
        /// </summary>
        private Dictionary<string, string> BuildPatternContext(ReportData reportData) {
            var ctx = new Dictionary<string, string>();

            try { ctx["$$CAMERA$$"] = cameraMediator?.GetInfo()?.Name ?? ""; } catch { ctx["$$CAMERA$$"] = ""; }
            try { ctx["$$TELESCOPE$$"] = profileService?.ActiveProfile?.TelescopeSettings?.Name ?? ""; } catch { ctx["$$TELESCOPE$$"] = ""; }
            try {
                var seqPath = sequenceMediator?.GetAdvancedSequencerSavePath();
                ctx["$$SEQUENCETITLE$$"] = !string.IsNullOrEmpty(seqPath) ? Path.GetFileNameWithoutExtension(seqPath) : "";
            } catch { ctx["$$SEQUENCETITLE$$"] = ""; }

            return ctx;
        }

        /// <summary>
        /// Returns the resolved report filename (no directory, with .html extension).
        /// Used by all delivery channels for consistent naming.
        /// </summary>
        internal string GetReportFileName(ReportData reportData = null) {
            var pattern = S.SaveReportFilePattern;
            if (string.IsNullOrWhiteSpace(pattern))
                return $"NightSummary_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.html";
            var context = reportData != null ? BuildPatternContext(reportData) : null;
            var resolved = ResolveFilePattern(pattern, context);
            // Strip any directory parts — only the filename portion applies to non-local channels
            return Path.GetFileName(resolved) + ".html";
        }

        private async Task SendPushoverWithDataAsync(ReportData reportData) {
            try {
                var appToken = S.PushoverAppToken;
                var userKey  = S.PushoverUserKey;

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
                var webhookUrl = S.DiscordWebhookUrl;

                if (string.IsNullOrWhiteSpace(webhookUrl)) {
                    Logger.Warning("NightSummary: Discord webhook URL not configured — skipping");
                    return;
                }

                htmlReport ??= await reportGenerator.GenerateHtmlReport(reportData);
                var fileName   = GetReportFileName(reportData);
                var sender     = new DiscordSender(webhookUrl);
                await sender.SendReportAsync(reportData, htmlReport, fileName);
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

            var dashboardUrl = S.DashboardUrl;
            var apiKey       = S.DashboardApiKey;

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
                    var history          = BuildSessionHistory(db, images, session.SessionId);
                    var historyAggregate = BuildSessionHistoryAggregate(db, images, session.SessionId);
                    var (fovW, fovH) = ComputeCameraFov(session);
                    var (lat, lon)   = GetObserverCoords();
                    var reportData = new ReportData {
                        Session                      = session,
                        Images                       = images,
                        Events                       = events,
                        TsData                       = tsData,
                        CumulativeIntegrationSeconds = cumulative,
                        SessionHistory               = history,
                        SessionHistoryAggregate      = historyAggregate,
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

        /// <summary>
        /// Generates HTML reports for all sessions in the database that don't already have
        /// a report saved in the local dashboard reports directory. Used to backfill reports
        /// for users who enable the dashboard after already having session history.
        /// </summary>
        public async Task<(int generated, int skipped, int failed)> GenerateAllDashboardReportsAsync(
            string dbPath, Action<int, int> onProgress = null) {

            var reportsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA", "NightSummary", "reports");
            Directory.CreateDirectory(reportsDir);

            var db       = new SessionDatabase(dbPath);
            var sessions = db.GetAllSessions();
            int generated = 0, skipped = 0, failed = 0;

            for (int i = 0; i < sessions.Count; i++) {
                var session = sessions[i];
                onProgress?.Invoke(i + 1, sessions.Count);

                var reportPath = Path.Combine(reportsDir, $"{session.SessionId}.html");
                if (File.Exists(reportPath)) {
                    skipped++;
                    continue;
                }

                try {
                    var reportData = await BuildReportDataAsync(dbPath, session.SessionId);
                    if (reportData == null) {
                        failed++;
                        continue;
                    }

                    var htmlReport = await GenerateReportForDashboard(reportData);
                    await File.WriteAllTextAsync(reportPath, htmlReport);
                    generated++;
                } catch (Exception ex) {
                    Logger.Warning($"NightSummary: Failed to generate dashboard report for session {session.SessionId}. {ex.Message}");
                    failed++;
                }
            }

            Logger.Info($"NightSummary: Dashboard report generation complete — {generated} generated, {skipped} already existed, {failed} failed");
            return (generated, skipped, failed);
        }

        private async Task SendDashboardWithDataAsync(ReportData reportData, string htmlReport = null) {
            try {
                var dashboardUrl = S.DashboardUrl;
                var apiKey       = S.DashboardApiKey;

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
            var savedPreview = S.ShowNextNightPreview;
            try {
                S.ShowNextNightPreview = false;
                return await reportGenerator.GenerateHtmlReport(reportData);
            } finally {
                S.ShowNextNightPreview = savedPreview;
            }
        }

        private async Task SendReportWithDataAsync(ReportData reportData, string htmlReport = null) {
            try {
                var senderAddress = S.SenderAddress;
                var smtpPassword  = S.SmtpPassword;
                var recipient     = S.RecipientAddress;

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

                var attachmentFileName = GetReportFileName(reportData);
                bool useGmail = S.UseGmailSmtp;
                var sender = new EmailSender(
                    useGmail ? "smtp.gmail.com" : S.SmtpHost,
                    useGmail ? 587 : S.SmtpPort,
                    useGmail ? true : S.SmtpSsl,
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
                result[targetName] = database.GetSessionHistoryForTarget(targetName, sessionId);
            }
            return result;
        }

        // Parallel to BuildSessionHistory: the all-prior-sessions roll-up (totals +
        // per-filter breakdown) per target, for the report's Session History totals
        // band. Skips targets with no prior frames (reader returns null).
        private Dictionary<string, TargetSessionHistoryAggregate> BuildSessionHistoryAggregate(SessionDatabase database, List<ImageRecord> images, string sessionId) {
            var result = new Dictionary<string, TargetSessionHistoryAggregate>(StringComparer.OrdinalIgnoreCase);
            foreach (var targetName in images.Select(i => i.TargetName).Distinct()) {
                var agg = database.GetSessionHistoryAggregateForTarget(targetName, sessionId);
                if (agg != null) result[targetName] = agg;
            }
            return result;
        }

        /// <summary>
        /// Delegates to <see cref="TsGradingResync.Sync"/> with try/catch — session-end sync
        /// is non-fatal (a TS schema mismatch or missing DB should not block report generation).
        /// The dashboard's on-demand resync uses the same helper directly.
        /// </summary>
        private static void SyncTsGrading(SessionDatabase database, string sessionId,
                                           DateTime sessionStart, DateTime sessionEnd,
                                           List<ImageRecord> images) {
            try {
                var tsDb = new TargetSchedulerDatabase();
                int changed = TsGradingResync.Sync(database, tsDb, sessionId, sessionStart, sessionEnd, images);
                if (changed > 0) {
                    Logger.Info($"NightSummary: Synced TS grading for {changed}/{images.Count} images");
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
        /// <summary>
        /// Builds an ordered dictionary of equipment names for the report, applying user overrides where set.
        /// Empty/null entries are omitted.
        /// </summary>
        private static Dictionary<string, string> BuildEquipmentDictionary(SessionRecord session) {
            var overrides = ParseEquipmentOverrides(S.EquipmentOverrides);
            var equipment = new Dictionary<string, string>();

            void Add(string key, string dbValue) {
                var value = overrides.TryGetValue(key, out var ov) && !string.IsNullOrWhiteSpace(ov) ? ov : dbValue;
                if (!string.IsNullOrWhiteSpace(value))
                    equipment[key] = value;
            }

            var visible = new HashSet<string>(
                (S.EquipmentVisibleFields ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);

            void AddIfVisible(string key, string dbValue) {
                if (visible.Contains(key)) Add(key, dbValue);
            }

            AddIfVisible("Camera",         session.CameraName);
            AddIfVisible("Telescope",      session.TelescopeName);
            AddIfVisible("Mount",          session.MountName);
            AddIfVisible("Filter Wheel",   session.FilterWheelName);
            AddIfVisible("Focuser",        session.FocuserName);
            AddIfVisible("Rotator",        session.RotatorName);
            AddIfVisible("Guider",         session.GuiderName);
            AddIfVisible("Dome",           session.DomeName);
            AddIfVisible("Flat Panel",     session.FlatDeviceName);
            AddIfVisible("Safety Monitor", session.SafetyMonitorName);
            AddIfVisible("Weather",        session.WeatherName);
            AddIfVisible("Switch",         session.SwitchName);

            return equipment;
        }

        internal static Dictionary<string, string> ParseEquipmentOverrides(string raw) =>
            string.IsNullOrWhiteSpace(raw)
                ? new Dictionary<string, string>()
                : raw.Split(',')
                    .Select(p => p.Split(':', 2))
                    .Where(p => p.Length == 2)
                    .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Captures equipment names from all connected mediators and stores them in the session database.
        /// Uses COALESCE logic in SQL so calling this multiple times only fills empty fields.
        /// </summary>
        private void CaptureEquipmentNames() {
            var sessionId = collector.GetCurrentSessionId();
            if (sessionId == null) return;

            try {
                string SafeName(Func<string> getter) { try { return getter(); } catch { return null; } }

                var camera        = SafeName(() => cameraMediator?.GetInfo()?.Name);
                var telescope     = SafeName(() => profileService?.ActiveProfile?.TelescopeSettings?.Name);
                var mount         = SafeName(() => telescopeMediator?.GetInfo()?.Name);
                var filterWheel   = SafeName(() => filterWheelMediator?.GetInfo()?.Name);
                var focuser       = SafeName(() => focuserMediator?.GetInfo()?.Name);
                var rotator       = SafeName(() => rotatorMediator?.GetInfo()?.Name);
                var guider        = SafeName(() => guiderMediator?.GetInfo()?.Name);
                var dome          = SafeName(() => domeMediator?.GetInfo()?.Name);
                var flatDevice    = SafeName(() => flatDeviceMediator?.GetInfo()?.Name);
                var safetyMonitor = SafeName(() => safetyMonitorMediator?.GetInfo()?.Name);
                var weather       = SafeName(() => weatherDataMediator?.GetInfo()?.Name);
                var switchHub     = SafeName(() => switchMediator?.GetInfo()?.Name);

                collector.Database.UpdateSessionEquipment(sessionId,
                    camera, telescope, mount, filterWheel, focuser, rotator, guider,
                    dome, flatDevice, safetyMonitor, weather, switchHub);

                Logger.Info($"NightSummary: Equipment captured — Camera={camera ?? "n/a"}, Telescope={telescope ?? "n/a"}, Mount={mount ?? "n/a"}, " +
                    $"FilterWheel={filterWheel ?? "n/a"}, Focuser={focuser ?? "n/a"}, Rotator={rotator ?? "n/a"}, Guider={guider ?? "n/a"}");
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: Could not capture equipment names. {ex.Message}");
            }
        }

        private void OnFirstImageSaved(object sender, EventArgs e) {
            collector.FirstImageSaved -= OnFirstImageSaved;
            CaptureEquipmentNames();
        }

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
        public async Task<ReportData> BuildReportDataAsync(string dbPath, string sessionId = null, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var db      = new SessionDatabase(dbPath);
            var session = sessionId != null ? db.GetSession(sessionId) : db.GetLatestSession();
            if (session == null) return null;

            ct.ThrowIfCancellationRequested();
            var images     = db.GetImagesForSession(session.SessionId);
            var events     = db.GetEventsForSession(session.SessionId);
            var profileId  = profileService?.ActiveProfile?.Id.ToString();
            var tsData     = FetchTsData(images, profileId);
            var cumulative = db.GetCumulativeIntegrationByTarget(session.SessionId);
            var history          = BuildSessionHistory(db, images, session.SessionId);
            var historyAggregate = BuildSessionHistoryAggregate(db, images, session.SessionId);
            var (fovW, fovH) = ComputeCameraFov(session);
            var (lat, lon)   = GetObserverCoords();

            // Always re-parse timing events from logs (fast, < 1s) to pick up parser improvements.
            // Falls back to cached DB data only if the log file is no longer available.
            List<TimingEvent> timingEvents;
            try {
                timingEvents = NinaLogParser.Parse(session.SessionStart, session.SessionEnd, images.Count);
                if (timingEvents.Any()) {
                    db.ClearTimingEvents(session.SessionId);
                    db.SaveTimingEvents(session.SessionId, timingEvents);
                }
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: Log re-parse failed, using cached data — {ex.Message}");
                timingEvents = null;
            }
            if (timingEvents == null || !timingEvents.Any()) {
                timingEvents = db.GetTimingEventsForSession(session.SessionId);
            }

            var reportData = new ReportData {
                Session                      = session,
                Images                       = images,
                Events                       = events,
                TsData                       = tsData,
                CumulativeIntegrationSeconds = cumulative,
                SessionHistory               = history,
                SessionHistoryAggregate      = historyAggregate,
                CameraFovWidthDeg            = fovW,
                CameraFovHeightDeg           = fovH,
                ObserverLatitude             = lat,
                ObserverLongitude            = lon,
                ActiveProfileId              = profileId,
                SkippedExposures             = session.SkippedExposures,
                Equipment                    = BuildEquipmentDictionary(session),
                TimingEvents                 = timingEvents
            };

            // Try to load persisted live stack masters for this session
            var (resolvedDir, resolvedFilename) = ResolveReportSavePath(reportData, scanForExisting: true);
            if (resolvedDir != null) {
                reportData.LiveStackImages = LoadLiveStackMasters(resolvedDir, resolvedFilename);
            }

            return reportData;
        }

        /// <summary>
        /// Generates HTML from existing ReportData without sending. Used by the preview window for re-renders.
        /// </summary>
        public async Task<string> GenerateHtmlAsync(ReportData reportData, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
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
            // Pending counts as accepted — see ImageRecord.CountsAsAccepted.
            var accepted     = images.Count(i => i.CountsAsAccepted);
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
