using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NINA.Plugin.NightSummary.Data {

    /// <summary>
    /// Disk-bounded retention for the raw image thumbnails store. Three modes:
    ///
    ///   * KeepAll        — no-op
    ///   * RolloverByDays — drop session dirs older than N days (by session start time)
    ///   * RolloverByGB   — keep newest sessions until total bytes ≤ cap
    ///
    /// Runs on session-end and on app startup (catches sessions that died mid-flight).
    /// Does **not** clear <c>ThumbnailVersion</c> in the DB — readers fall back to 404
    /// when a thumb file is gone, dashboard shows a placeholder. Keeps the cleanup
    /// path idempotent and DB-write-free for safety.
    ///
    /// See RAW_THUMBNAILS_DESIGN.md §"Retention".
    /// </summary>
    public static class ThumbnailRetention {

        public class RetentionResult {
            public int  SessionsScanned { get; set; }
            public int  SessionsRemoved { get; set; }
            public long BytesRemoved    { get; set; }
        }

        /// <summary>
        /// Applies the configured retention policy. Reads settings via <see cref="SettingsManager"/>.
        /// Tolerates missing thumbs root (returns empty result with no error).
        /// </summary>
        public static RetentionResult Apply(string thumbsRoot, NightSummarySettings settings, Func<string, DateTime?> getSessionStart) {
            var result = new RetentionResult();
            if (settings == null || !settings.CaptureRawThumbnails) return result;
            if (string.IsNullOrEmpty(thumbsRoot) || !Directory.Exists(thumbsRoot)) return result;

            try {
                var sessionDirs = Directory.GetDirectories(thumbsRoot);
                result.SessionsScanned = sessionDirs.Length;
                if (sessionDirs.Length == 0) return result;

                switch (settings.ThumbnailRetentionMode) {
                    case "RolloverByDays":
                        ApplyByDays(sessionDirs, settings.ThumbnailRetentionDays, getSessionStart, result);
                        break;
                    case "RolloverByGB":
                        ApplyByGB(sessionDirs, settings.ThumbnailRetentionMaxGB, getSessionStart, result);
                        break;
                    case "KeepAll":
                    default:
                        // no-op
                        break;
                }

                if (result.SessionsRemoved > 0) {
                    Logger.Info($"NightSummary: ThumbnailRetention removed {result.SessionsRemoved} session dir(s), freed {result.BytesRemoved / (1024 * 1024)}MB");
                }
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: ThumbnailRetention failed: {ex.Message}");
            }
            return result;
        }

        private static void ApplyByDays(string[] sessionDirs, int days, Func<string, DateTime?> getSessionStart, RetentionResult result) {
            if (days <= 0) return;
            var cutoff = DateTime.UtcNow.AddDays(-days);
            foreach (var dir in sessionDirs) {
                var sid   = Path.GetFileName(dir);
                var start = getSessionStart?.Invoke(sid)?.ToUniversalTime();
                // Fall back to dir mtime when DB lookup fails (orphan dirs).
                var when  = start ?? Directory.GetLastWriteTimeUtc(dir);
                if (when < cutoff) RemoveDir(dir, result);
            }
        }

        private static void ApplyByGB(string[] sessionDirs, double maxGB, Func<string, DateTime?> getSessionStart, RetentionResult result) {
            if (maxGB <= 0) return;
            long maxBytes = (long)(maxGB * 1024 * 1024 * 1024);

            // Sort newest-first by session start (or dir mtime fallback).
            var ordered = sessionDirs
                .Select(d => new {
                    Path = d,
                    Start = getSessionStart?.Invoke(Path.GetFileName(d)) ?? Directory.GetLastWriteTimeUtc(d),
                    Bytes = DirSize(d)
                })
                .OrderByDescending(x => x.Start)
                .ToList();

            long running = 0;
            foreach (var entry in ordered) {
                running += entry.Bytes;
                if (running > maxBytes) {
                    // Past the cap — remove this session and all older.
                    RemoveDir(entry.Path, result);
                }
            }
        }

        private static long DirSize(string dir) {
            try {
                long sum = 0;
                foreach (var f in Directory.EnumerateFiles(dir, "*.jpg", SearchOption.AllDirectories)) {
                    try { sum += new FileInfo(f).Length; } catch { /* ignore */ }
                }
                return sum;
            } catch { return 0; }
        }

        private static void RemoveDir(string dir, RetentionResult result) {
            try {
                long bytes = DirSize(dir);
                Directory.Delete(dir, recursive: true);
                result.SessionsRemoved++;
                result.BytesRemoved += bytes;
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: ThumbnailRetention could not remove {dir}: {ex.Message}");
            }
        }
    }
}
