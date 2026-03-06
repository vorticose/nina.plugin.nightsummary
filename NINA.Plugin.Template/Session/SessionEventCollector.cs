using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Equipment.MySafetyMonitor;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.NightSummary.Data;
using OxyPlot;
using System;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Session {
    /// <summary>
    /// Subscribes to NINA equipment mediators to capture session events
    /// (autofocus runs, safety monitor state changes, meridian flips).
    /// Registered/unregistered on session start/end.
    /// </summary>
    public class SessionEventCollector : IFocuserConsumer, ISafetyMonitorConsumer {

        private readonly SessionDatabase database;
        private readonly ISafetyMonitorMediator safetyMonitorMediator;
        private readonly IFocuserMediator focuserMediator;
        private readonly ITelescopeMediator telescopeMediator;

        private string currentSessionId;
        private bool isCollecting;
        private bool? lastIsSafe;

        public SessionEventCollector(
            SessionDatabase database,
            ISafetyMonitorMediator safetyMonitorMediator,
            IFocuserMediator focuserMediator,
            ITelescopeMediator telescopeMediator) {

            this.database               = database;
            this.safetyMonitorMediator  = safetyMonitorMediator;
            this.focuserMediator        = focuserMediator;
            this.telescopeMediator      = telescopeMediator;
        }

        public void StartSession(string sessionId) {
            currentSessionId = sessionId;
            lastIsSafe       = null;
            isCollecting     = true;

            safetyMonitorMediator?.RegisterConsumer(this);
            focuserMediator?.RegisterConsumer(this);
            if (telescopeMediator != null)
                telescopeMediator.AfterMeridianFlip += OnAfterMeridianFlip;
        }

        public void EndSession() {
            isCollecting = false;

            safetyMonitorMediator?.RemoveConsumer(this);
            focuserMediator?.RemoveConsumer(this);
            if (telescopeMediator != null)
                telescopeMediator.AfterMeridianFlip -= OnAfterMeridianFlip;

            currentSessionId = null;
        }

        // ── ISafetyMonitorConsumer ──────────────────────────────────────────

        public void UpdateDeviceInfo(SafetyMonitorInfo deviceInfo) {
            if (!isCollecting || currentSessionId == null) return;
            if (deviceInfo == null) return;

            bool isSafe = deviceInfo.IsSafe;
            if (lastIsSafe.HasValue && lastIsSafe.Value == isSafe) return;

            // Only log the change, not the initial state query on session start
            if (lastIsSafe.HasValue) {
                var eventType   = isSafe ? "RoofOpen" : "RoofClosed";
                var description = isSafe
                    ? "Safety monitor: Safe — roof opened"
                    : "Safety monitor: Unsafe — roof closed";
                SaveEvent(eventType, description);
            }

            lastIsSafe = isSafe;
        }

        // ── IFocuserConsumer ────────────────────────────────────────────────

        public void UpdateEndAutoFocusRun(AutoFocusInfo info) {
            if (!isCollecting || currentSessionId == null) return;

            var description = $"AutoFocus completed — Filter: {info?.Filter ?? "N/A"}, Temp: {info?.Temperature:F1}°C, Position: {info?.Position}";
            SaveEvent("AutoFocus", description);
        }

        public void UpdateDeviceInfo(FocuserInfo deviceInfo) { }
        public void UpdateUserFocused(FocuserInfo info) { }
        public void AutoFocusRunStarting() { }
        public void NewAutoFocusPoint(DataPoint dataPoint) { }

        // ── Meridian flip ───────────────────────────────────────────────────

        private Task OnAfterMeridianFlip(object sender, AfterMeridianFlipEventArgs e) {
            if (!isCollecting || currentSessionId == null) return Task.CompletedTask;

            var description = e.Success
                ? "Meridian flip completed successfully"
                : "Meridian flip failed";
            SaveEvent("MeridianFlip", description);

            return Task.CompletedTask;
        }

        // ── IDisposable (required by IDeviceConsumer) ───────────────────────

        public void Dispose() {
            EndSession();
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private void SaveEvent(string eventType, string description) {
            try {
                database.SaveEvent(new SessionEvent {
                    SessionId   = currentSessionId,
                    Timestamp   = DateTime.Now,
                    EventType   = eventType,
                    Description = description
                });
                Logger.Info($"NightSummary: Event logged — {eventType}: {description}");
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to save session event. {ex.Message}");
            }
        }
    }
}
