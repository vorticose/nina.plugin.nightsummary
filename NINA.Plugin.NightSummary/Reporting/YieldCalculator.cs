using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.NightSummary.Reporting {

    /// <summary>
    /// Calculates imaging yield percentage, accounting for roof-closed time exclusion.
    /// </summary>
    internal static class YieldCalculator {

        public class YieldResult {
            public double YieldPct { get; set; }
            public bool HasSafetyMonitor { get; set; }
        }

        /// <summary>
        /// Computes yield as total exposure time ÷ effective imaging window.
        /// The effective window is (first image → last image) minus any roof-closed periods.
        /// </summary>
        public static YieldResult Calculate(
            List<ImageRecord> images,
            List<SessionEvent> events,
            DateTime sessionStart,
            DateTime sessionEnd) {

            var firstImage = images.Any() ? images.Min(i => i.Timestamp) : sessionStart;
            var lastImage  = images.Any() ? images.Max(i => i.Timestamp) : sessionEnd;
            var windowSec  = (lastImage - firstImage).TotalSeconds;

            var roofEvents = (events ?? new List<SessionEvent>())
                .Where(e => e.EventType == "RoofClosed" || e.EventType == "RoofOpen")
                .OrderBy(e => e.Timestamp)
                .ToList();

            double roofClosedSec = 0;
            DateTime? closedAt = null;
            foreach (var ev in roofEvents) {
                if (ev.EventType == "RoofClosed") {
                    closedAt = ev.Timestamp;
                } else if (ev.EventType == "RoofOpen" && closedAt.HasValue) {
                    var overlapStart = closedAt.Value < firstImage ? firstImage : closedAt.Value;
                    var overlapEnd   = ev.Timestamp   > lastImage  ? lastImage  : ev.Timestamp;
                    if (overlapEnd > overlapStart)
                        roofClosedSec += (overlapEnd - overlapStart).TotalSeconds;
                    closedAt = null;
                }
            }
            if (closedAt.HasValue && closedAt.Value < lastImage)
                roofClosedSec += (lastImage - closedAt.Value).TotalSeconds;

            var effectiveWindowSec = windowSec - roofClosedSec;
            var totalExposureSec   = images.Sum(i => i.ExposureDuration);
            double yieldPct = effectiveWindowSec > 0
                ? Math.Min(totalExposureSec / effectiveWindowSec * 100.0, 100.0)
                : 0;

            return new YieldResult {
                YieldPct = yieldPct,
                HasSafetyMonitor = roofEvents.Any()
            };
        }
    }
}
