using NINA.Core.Utility;
using NINA.Image.Interfaces;
using NINA.Plugin.NightSummary.Data;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Session {
    /// <summary>
    /// Listens to NINA image save events and records each captured image
    /// into the NightSummary database. This is the core data collection
    /// component - it runs silently in the background during a sequence.
    /// </summary>
    public class SessionCollector {

        private readonly SessionDatabase database;
        private readonly IImageSaveMediator imageSaveMediator;
        private SessionRecord currentSession;
        private bool isCollecting = false;

        public SessionCollector(IImageSaveMediator imageSaveMediator, SessionDatabase database) {
            this.imageSaveMediator = imageSaveMediator;
            this.database = database;
        }

        /// <summary>
        /// Starts a new session and begins listening for images.
        /// Call this when the sequence starts.
        /// </summary>
        public void StartSession(string profileName) {
            if (isCollecting) {
                Logger.Warning("NightSummary: StartSession called but a session is already active. Ending previous session first.");
                EndSession();
            }

            currentSession = new SessionRecord {
                SessionId = Guid.NewGuid().ToString(),
                SessionStart = DateTime.Now,
                ProfileName = profileName,
                ReportSent = false
            };

            database.CreateSession(currentSession);
            imageSaveMediator.ImageSaved += OnImageSaved;
            isCollecting = true;

            Logger.Info($"NightSummary: Session started. SessionId={currentSession.SessionId}");
        }

        /// <summary>
        /// Ends the current session and stops listening for images.
        /// Call this when the sequence ends.
        /// </summary>
        public void EndSession() {
            if (!isCollecting) return;

            imageSaveMediator.ImageSaved -= OnImageSaved;
            isCollecting = false;

            database.FinalizeSession(currentSession.SessionId, DateTime.Now, false);

            Logger.Info($"NightSummary: Session ended. SessionId={currentSession.SessionId}");
        }

        /// <summary>
        /// Returns the current session ID so other components
        /// (like the report generator) can query data for this session.
        /// </summary>
        public string GetCurrentSessionId() {
            return currentSession?.SessionId;
        }

        /// <summary>
        /// Fired by NINA each time an image is saved.
        /// Extracts all available metadata and writes it to the database.
        /// </summary>
        private void OnImageSaved(object sender, ImageSavedEventArgs e) {
            try {
                var record = new ImageRecord {
                    SessionId = currentSession.SessionId,
                    Timestamp = DateTime.Now,

                    // Target and exposure info
                    TargetName = e.MetaData?.Target?.Name ?? "Unknown",
                    Filter = e.MetaData?.FilterWheel?.Filter ?? "None",
                    ExposureDuration = e.MetaData?.Image?.ExposureTime ?? 0,

                    // Image quality
                    HFR = e.StarDetectionAnalysis?.HFR ?? 0,
                    StarCount = e.StarDetectionAnalysis?.DetectedStars ?? 0,

                    // FWHM and Eccentricity - set to 0 for now, populated via metadata
                    FWHM = 0,
                    Eccentricity = 0,

                    // Guiding RMS
                    GuidingRMSTotal = e.MetaData?.Image?.RecordedRMS?.Total ?? 0,
                    GuidingRMSRA = e.MetaData?.Image?.RecordedRMS?.RA ?? 0,
                    GuidingRMSDec = e.MetaData?.Image?.RecordedRMS?.Dec ?? 0,

                    // Equipment state
                    FocuserPosition = e.MetaData?.Focuser?.Position ?? 0,
                    CameraTemperature = e.MetaData?.Camera?.Temperature ?? 0,

                    Accepted = true
                };

                database.SaveImageRecord(record);
                Logger.Debug($"NightSummary: Recorded image - Target={record.TargetName}, Filter={record.Filter}, HFR={record.HFR:F2}");
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to record image. {ex.Message}");
            }
        }
    }
}