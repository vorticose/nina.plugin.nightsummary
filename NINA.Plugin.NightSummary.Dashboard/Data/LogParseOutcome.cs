namespace NINA.Plugin.NightSummary.Data {
    /// <summary>
    /// Why NinaLogParser.Parse returned no timing events, so callers can show an
    /// accurate message instead of guessing. Distinguishes "no NINA log file could be
    /// located for this session" (log rotated away, aged off, moved), "the log had no
    /// Info lines in the session window" (log level below Info), and "the log had Info
    /// lines but nothing the parser recognized".
    /// </summary>
    public enum LogParseOutcome {
        Success,
        NoLogFileFound,
        NoEventsInWindow,
        // Appended, not inserted, so existing values keep their numbers.
        NoInfoLinesInWindow
    }
}
