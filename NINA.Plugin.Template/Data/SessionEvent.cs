using System;

namespace NINA.Plugin.NightSummary.Data {
    public class SessionEvent {
        public int Id { get; set; }
        public string SessionId { get; set; }
        public DateTime Timestamp { get; set; }

        // EventType values: "RoofOpen", "RoofClosed", "AutoFocus", "MeridianFlip"
        public string EventType   { get; set; }
        public string Description { get; set; }

        // AutoFocus enrichment; null for non-AF events or if data unavailable
        public bool?   AfSucceeded { get; set; }
        public double? AfHfr       { get; set; }
    }
}
