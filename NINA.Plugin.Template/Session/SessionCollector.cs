using NINA.Core.Utility;
using NINA.Image.Interfaces;
using NINA.Plugin.NightSummary.Data;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Session {
    public class SessionCollector {
        private readonly SessionDatabase database;
        private readonly IImageSaveMediator imageSaveMediator;
        private SessionRecord currentSession;
        private bool isCollecting = false;

        public SessionDatabase Database { get; private set; }

        public SessionCollector(IImageSaveMediator imageSaveMediator, SessionDatabase database) {
            this.imageSaveMediator = imageSaveMediator;
            this.database = database;
            this.Database = database;
        }

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

        public void EndSession() {
            if (!isCollecting) return;
            imageSaveMediator.ImageSaved -= OnImageSaved;
            isCollecting = false;
            database.FinalizeSession(currentSession.SessionId, DateTime.Now, false);
            Logger.Info($"NightSummary: Session ended. SessionId={currentSession.SessionId}");
        }

        public string GetCurrentSessionId() {
            return currentSession?.SessionId;
        }

        private void OnImageSaved(object sender, ImageSavedEventArgs e) {
            try {
                // Read guiding scale from NINA - this converts pixels to arcseconds
                // Default to 1 if not available so values are still stored (as pixels)
                double guidingScale = e.MetaData?.Image?.RecordedRMS?.Scale ?? 1;

                var record = new ImageRecord {
                    SessionId = currentSession.SessionId,
                    Timestamp = DateTime.Now,
                    TargetName = e.MetaData?.Target?.Name ?? "Unknown",
                    Filter = e.MetaData?.FilterWheel?.Filter ?? "None",
                    ExposureDuration = e.MetaData?.Image?.ExposureTime ?? 0,
                    HFR = e.StarDetectionAnalysis?.HFR ?? 0,
                    StarCount = e.StarDetectionAnalysis?.DetectedStars ?? 0,
                    // Multiply RMS by Scale to store in arcseconds
                    GuidingRMSTotal = (e.MetaData?.Image?.RecordedRMS?.Total ?? 0) * guidingScale,
                    GuidingScale = guidingScale,
                    Accepted = true
                };

                database.SaveImageRecord(record);
                Logger.Debug($"NightSummary: Recorded image - Target={record.TargetName}, Filter={record.Filter}, HFR={record.HFR:F2}, GuidingRMS={record.GuidingRMSTotal:F2}\"");
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to record image. {ex.Message}");
            }
        }
    }
}