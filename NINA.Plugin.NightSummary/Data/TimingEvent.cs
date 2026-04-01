using System;

namespace NINA.Plugin.NightSummary.Data {
    public class TimingEvent {
        public string EventType { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public double DurationSeconds { get; set; }
        public string Details { get; set; }
    }
}
