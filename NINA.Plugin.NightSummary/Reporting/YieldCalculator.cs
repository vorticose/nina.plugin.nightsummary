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

            var roofIntervals = RoofClosedHelper.GetIntervals(events, firstImage, lastImage);
            var roofClosedSec = RoofClosedHelper.TotalSeconds(roofIntervals);

            var effectiveWindowSec = windowSec - roofClosedSec;
            var totalExposureSec   = images.Sum(i => i.ExposureDuration);
            double yieldPct = effectiveWindowSec > 0
                ? Math.Min(totalExposureSec / effectiveWindowSec * 100.0, 100.0)
                : 0;

            return new YieldResult {
                YieldPct = yieldPct,
                HasSafetyMonitor = (events ?? new List<SessionEvent>()).Any(e => e.EventType == "RoofClosed" || e.EventType == "RoofOpen")
            };
        }
    }
}
