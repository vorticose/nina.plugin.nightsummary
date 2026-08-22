namespace NINA.Plugin.NightSummary.Data {
    /// <summary>
    /// Why NinaLogParser.Parse returned no timing events, so callers can show an
    /// accurate message instead of guessing. Distinguishes "no NINA log file could be
    /// located for this session" (log rotated away, aged off, moved) from "a log file
    /// was found but it contained nothing matching" (usually log level below Info).
    /// </summary>
    public enum LogParseOutcome {
        Success,
        NoLogFileFound,
        NoEventsInWindow
    }
}
