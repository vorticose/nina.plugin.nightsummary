using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NINA.Plugin.NightSummary.Data {

    /// <summary>
    /// Parses NINA log files to extract per-event timing data for overhead analysis.
    /// Log format: Timestamp|Level|Source|Member|Line|Message (pipe-delimited).
    /// Matches on Source + Member + message prefix — never on line numbers.
    /// </summary>
    internal static class NinaLogParser {

        private static readonly string[] KnownNinaVersionPrefixes = { "3.2.", "3.1.", "3.0.", "3.3." };

        /// <summary>
        /// Maps NINA SequenceItem names to overhead event categories.
        /// Unknown items not in this map use their raw name as the category.
        /// </summary>
        private static readonly Dictionary<string, string> ItemCategoryMap = new(StringComparer.OrdinalIgnoreCase) {
            // Imaging (special-cased for download derivation, not in generic path)
            // ["TakeExposure"] handled separately

            // Filter
            ["SwitchFilter"]              = "FilterChange",

            // Focusing
            ["RunAutofocus"]              = "Autofocus",
            ["MoveFocuserByTemperature"]  = "TempCompFocus",
            ["MoveFocuserAbsolute"]       = "FocuserMove",
            ["MoveFocuserRelative"]       = "FocuserMove",

            // Guiding
            ["Dither"]                    = "Dither",
            ["StartGuiding"]             = "Guiding",
            ["StopGuiding"]              = "Guiding",

            // Mount / Slew
            ["SlewScopeToRaDec"]         = "Slew",
            ["SlewScopeToAltAz"]         = "Slew",
            ["ParkScope"]                = "MountOps",
            ["UnparkScope"]              = "MountOps",
            ["FindHome"]                 = "MountOps",
            ["SetTracking"]              = "MountOps",

            // Centering / Plate solving
            ["Center"]                   = "Centering",
            ["CenterAndRotate"]          = "Centering",
            ["SolveAndSync"]             = "PlateSolve",
            ["SolveAndRotate"]           = "PlateSolve",

            // Dome
            ["SynchronizeDome"]          = "DomeSync",
            ["OpenDomeShutter"]          = "DomeOps",
            ["CloseDomeShutter"]         = "DomeOps",
            ["ParkDome"]                 = "DomeOps",
            ["FindHomeDome"]             = "DomeOps",
            ["SlewDomeAzimuth"]          = "DomeOps",
            ["EnableDomeSynchronization"]  = "DomeOps",
            ["DisableDomeSynchronization"] = "DomeOps",

            // Flat panel
            ["SetBrightness"]            = "FlatPanel",
            ["ToggleLight"]              = "FlatPanel",
            ["OpenCover"]                = "FlatPanel",
            ["CloseCover"]               = "FlatPanel",

            // Camera temp
            ["CoolCamera"]              = "CameraTemp",
            ["WarmCamera"]              = "CameraTemp",

            // Rotator
            ["MoveRotatorMechanical"]   = "Rotator",

            // Switch
            ["SetSwitchValue"]          = "Switch",

            // Safety
            ["WaitUntilSafe"]           = "SafetyWait",

            // Meridian flip
            ["MeridianFlip"]            = "MeridianFlip",
        };

        // Only items in ItemCategoryMap (plus TakeExposure/TakeSubframeExposure) are tracked.
        // Everything else — containers, triggers, conditions, utility items — is silently skipped.
        // This allow-list approach is more robust than a deny-list since NINA and plugins
        // can introduce arbitrary new sequence items.

        /// <summary>
        /// Parses the NINA log file for the given session window and returns timing events.
        /// </summary>
        /// <param name="sessionStart">Session start time (used to locate the correct log file).</param>
        /// <param name="sessionEnd">Session end time (only lines within this window are parsed).</param>
        /// <param name="expectedImageCount">Expected number of images from Night Summary's own count, for cross-check. Pass -1 to skip.</param>
        /// <returns>Parsed timing events, or empty list if log not found or unparseable.</returns>
        public static List<TimingEvent> Parse(DateTime sessionStart, DateTime sessionEnd, int expectedImageCount = -1) {
            var logPath = FindLogFile(sessionStart);
            if (logPath == null) {
                Logger.Warning("NightSummary: LogParser — no matching NINA log file found for session");
                return new List<TimingEvent>();
            }

            return ParseFile(logPath, sessionStart, sessionEnd, expectedImageCount);
        }

        /// <summary>
        /// Parses a specific log file. Exposed for testing.
        /// </summary>
        internal static List<TimingEvent> ParseFile(string logPath, DateTime sessionStart, DateTime sessionEnd, int expectedImageCount = -1) {
            var events = new List<TimingEvent>();
            var lines = File.ReadAllLines(logPath);

            if (lines.Length < 5) {
                Logger.Warning("NightSummary: LogParser — log file too short to be valid");
                return events;
            }

            // Read NINA version from header (line 3, 0-indexed)
            var ninaVersion = ExtractNinaVersion(lines);
            if (ninaVersion != null) {
                bool knownVersion = KnownNinaVersionPrefixes.Any(p => ninaVersion.StartsWith(p));
                if (!knownVersion)
                    Logger.Warning($"NightSummary: LogParser — unrecognized NINA version '{ninaVersion}', parser may produce incorrect results");
            }

            // State tracking for Starting/Finishing pairs
            DateTime? exposureStart = null;
            string exposureDetails = null;
            double exposureRequestedSeconds = 0;

            // Generic tracker for all non-exposure SequenceItem Starting/Finishing pairs
            var pendingStarts = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            int parsedExposureCount = 0;
            int parsedImageSaveCount = 0;

            for (int i = 0; i < lines.Length; i++) {
                var parts = lines[i].Split('|');
                if (parts.Length < 6) continue;

                if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
                    continue;

                // Only process lines within the session window (with a small buffer for pre-session setup)
                if (timestamp < sessionStart.AddMinutes(-5) || timestamp > sessionEnd.AddMinutes(5))
                    continue;

                var level = parts[1];
                if (level != "INFO") continue;

                var source = parts[2];
                var member = parts[3];
                // parts[4] is line number — intentionally ignored
                var message = string.Join("|", parts.Skip(5)); // rejoin in case message contains pipes

                // === SequenceItem.cs|Run — Starting/Finishing pairs ===
                if (source == "SequenceItem.cs" && member == "Run") {
                    var itemName = ExtractItemName(message);
                    if (itemName == null) {
                        // Unparseable message
                    } else if (message.StartsWith("Starting ")) {
                        if (itemName == "TakeExposure" || itemName == "TakeSubframeExposure") {
                            exposureStart = timestamp;
                            exposureDetails = ExtractExposureDetails(message);
                            exposureRequestedSeconds = ExtractExposureTime(message);
                        } else if (ItemCategoryMap.ContainsKey(itemName)) {
                            pendingStarts[itemName] = timestamp;
                        }
                    } else if (message.StartsWith("Finishing ")) {
                        if ((itemName == "TakeExposure" || itemName == "TakeSubframeExposure") && exposureStart.HasValue) {
                            var totalDuration = (timestamp - exposureStart.Value).TotalSeconds;
                            events.Add(new TimingEvent {
                                EventType = "Exposure",
                                StartTime = exposureStart.Value,
                                EndTime = timestamp,
                                DurationSeconds = totalDuration,
                                Details = exposureDetails
                            });

                            // Derive camera download time
                            if (exposureRequestedSeconds > 0 && totalDuration > exposureRequestedSeconds) {
                                var downloadTime = totalDuration - exposureRequestedSeconds;
                                events.Add(new TimingEvent {
                                    EventType = "CameraDownload",
                                    StartTime = exposureStart.Value.AddSeconds(exposureRequestedSeconds),
                                    EndTime = timestamp,
                                    DurationSeconds = downloadTime,
                                    Details = $"Derived from {exposureRequestedSeconds}s exposure"
                                });
                            }

                            parsedExposureCount++;
                            exposureStart = null;
                            exposureDetails = null;
                            exposureRequestedSeconds = 0;
                        } else if (pendingStarts.TryGetValue(itemName, out var startTime)) {
                            var eventType = ItemCategoryMap.TryGetValue(itemName, out var mapped) ? mapped : itemName;
                            events.Add(new TimingEvent {
                                EventType = eventType,
                                StartTime = startTime,
                                EndTime = timestamp,
                                DurationSeconds = (timestamp - startTime).TotalSeconds,
                                Details = ExtractItemDetails(itemName, message)
                            });
                            pendingStarts.Remove(itemName);
                        }
                    }
                }

                // === ImageSaveController.cs|DoWork — self-contained timing (async, runs during next exposure) ===
                else if (source == "ImageSaveController.cs" && member == "DoWork") {
                    var saveDuration = ExtractImageSaveDuration(message);
                    if (saveDuration > 0) {
                        events.Add(new TimingEvent {
                            EventType = "ImageSave",
                            StartTime = timestamp.AddSeconds(-saveDuration),
                            EndTime = timestamp,
                            DurationSeconds = saveDuration,
                            Details = ExtractImageSaveSubTimings(message)
                        });
                        parsedImageSaveCount++;
                    }
                }
            }

            // Warn about unmatched starts
            if (exposureStart.HasValue)
                Logger.Warning($"NightSummary: LogParser — unmatched TakeExposure start at {exposureStart.Value:o}");
            foreach (var pending in pendingStarts)
                Logger.Warning($"NightSummary: LogParser — unmatched {pending.Key} start at {pending.Value:o}");

            // Cross-check exposure count
            if (expectedImageCount >= 0 && parsedExposureCount != expectedImageCount) {
                Logger.Warning($"NightSummary: LogParser — parsed {parsedExposureCount} exposures but Night Summary recorded {expectedImageCount} images");
            }

            Logger.Info($"NightSummary: LogParser — parsed {events.Count} timing events ({parsedExposureCount} exposures, {parsedImageSaveCount} saves) from {logPath}");
            return events;
        }

        /// <summary>
        /// Finds the NINA log file whose filename timestamp is closest to (but before) the session start.
        /// </summary>
        internal static string FindLogFile(DateTime sessionStart) {
            var logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA", "Logs");

            if (!Directory.Exists(logsDir)) return null;

            // Pattern: {yyyyMMdd}-{HHmmss}-{version}.{pid}-{yyyyMM}.log
            var logFiles = Directory.GetFiles(logsDir, "*.log");
            string bestMatch = null;
            TimeSpan bestDelta = TimeSpan.MaxValue;

            foreach (var file in logFiles) {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var fileTimestamp = ExtractLogFileTimestamp(fileName);
                if (fileTimestamp == null) continue;

                var delta = sessionStart - fileTimestamp.Value;
                if (delta >= TimeSpan.Zero && delta < bestDelta) {
                    bestDelta = delta;
                    bestMatch = file;
                }
            }

            return bestMatch;
        }

        /// <summary>
        /// Extracts the timestamp from a NINA log filename.
        /// Format: {yyyyMMdd}-{HHmmss}-{rest}.log → e.g., "20260330-212110-3.2.0.9001.13884-202603"
        /// </summary>
        internal static DateTime? ExtractLogFileTimestamp(string fileNameWithoutExtension) {
            // First 15 chars should be: yyyyMMdd-HHmmss
            if (fileNameWithoutExtension.Length < 15) return null;

            var dateTimePart = fileNameWithoutExtension.Substring(0, 15); // "20260330-212110"
            if (DateTime.TryParseExact(dateTimePart, "yyyyMMdd-HHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)) {
                return result;
            }
            return null;
        }

        /// <summary>
        /// Reads the NINA version from the log header (line 3, 0-indexed).
        /// Expected format: "--------------------------Version X.Y.Z.NNNN--------------------------"
        /// </summary>
        internal static string ExtractNinaVersion(string[] lines) {
            if (lines.Length < 4) return null;
            var match = Regex.Match(lines[2], @"Version\s+([\d.]+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Extracts the item name from a SequenceItem log message.
        /// Format: "Starting Category: X, Item: SwitchFilter, ..."
        /// </summary>
        private static string ExtractItemName(string message) {
            var match = Regex.Match(message, @"Item:\s*(\w+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Extracts relevant details from a SequenceItem log message based on the item type.
        /// </summary>
        private static string ExtractItemDetails(string itemName, string message) {
            return itemName switch {
                "SwitchFilter" => ExtractFilterName(message),
                "MoveFocuserByTemperature" => ExtractTempCompDetails(message),
                _ => null
            };
        }

        private static string ExtractExposureDetails(string message) {
            // "Starting Category: Scheduler, Item: TakeExposure, ExposureTime 600, Gain 100, Offset 19, ImageType LIGHT, Binning 1x1"
            var match = Regex.Match(message, @"ExposureTime (\d+(?:\.\d+)?),.*?Gain (\d+)");
            if (match.Success)
                return $"Exposure {match.Groups[1].Value}s, Gain {match.Groups[2].Value}";
            return null;
        }

        private static double ExtractExposureTime(string message) {
            var match = Regex.Match(message, @"ExposureTime (\d+(?:\.\d+)?)");
            if (match.Success && double.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var seconds))
                return seconds;
            return 0;
        }

        private static string ExtractFilterName(string message) {
            // "Starting Category: Scheduler, Item: SwitchFilter, Filter: S"
            var match = Regex.Match(message, @"Filter:\s*(\S+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string ExtractTempCompDetails(string message) {
            // "Starting Category: Focuser, Item: MoveFocuserByTemperature, Slope: -8.5545, Intercept 31749.69"
            var match = Regex.Match(message, @"Slope:\s*([-\d.]+),\s*Intercept\s*([-\d.]+)");
            if (match.Success)
                return $"Slope {match.Groups[1].Value}, Intercept {match.Groups[2].Value}";
            return null;
        }

        private static double ExtractImageSaveDuration(string message) {
            // "Duration Total: 00:00:10.7636414"
            var match = Regex.Match(message, @"Duration Total:\s*(\d+:\d+:[\d.]+)");
            if (match.Success && TimeSpan.TryParse(match.Groups[1].Value, out var ts))
                return ts.TotalSeconds;
            return 0;
        }

        private static string ExtractImageSaveSubTimings(string message) {
            // Extract BeforeSave, BeforeFinalizeImageSaved, FinalizeSaveTime
            var parts = new List<string>();
            var beforeSave = Regex.Match(message, @"BeforeSave:\s*(\d+:\d+:[\d.]+)");
            var beforeFinalize = Regex.Match(message, @"BeforeFinalizeImageSaved:\s*(\d+:\d+:[\d.]+)");
            var finalize = Regex.Match(message, @"FinalizeSaveTime:\s*(\d+:\d+:[\d.]+)");

            if (beforeSave.Success) parts.Add($"BeforeSave: {beforeSave.Groups[1].Value}");
            if (beforeFinalize.Success) parts.Add($"BeforeFinalize: {beforeFinalize.Groups[1].Value}");
            if (finalize.Success) parts.Add($"Finalize: {finalize.Groups[1].Value}");

            return parts.Any() ? string.Join(", ", parts) : null;
        }
    }
}
