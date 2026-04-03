using System;

namespace NINA.Plugin.NightSummary.Session {

    /// <summary>
    /// Injectable clock for timestamp generation. In production, delegates to DateTime.Now.
    /// During replay testing, the harness overrides Now/UtcNow to return recorded timestamps,
    /// enabling fast replay of multi-hour sessions in seconds.
    /// </summary>
    internal static class Clock {
        internal static Func<DateTime> Now = () => DateTime.Now;
        internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;

        /// <summary>
        /// When true, SessionCollector skips the 1-second skip-poll timer.
        /// Required during fast replay to avoid wall-clock timer interference.
        /// </summary>
        internal static bool DisableSkipPolling = false;

        /// <summary>
        /// Resets all overrides back to production defaults.
        /// Must be called in test cleanup (Dispose) to avoid cross-test contamination.
        /// </summary>
        internal static void Reset() {
            Now = () => DateTime.Now;
            UtcNow = () => DateTime.UtcNow;
            DisableSkipPolling = false;
        }
    }
}
