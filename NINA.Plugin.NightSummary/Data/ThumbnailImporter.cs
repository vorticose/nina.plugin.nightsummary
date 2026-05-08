using NINA.Core.Utility;
using System;
using System.Data.SQLite;
using System.IO;

namespace NINA.Plugin.NightSummary.Data {

    /// <summary>
    /// One-shot job that backfills NightSummary thumbnails for past sessions by
    /// pulling existing JPEG blobs from the Target Scheduler <c>imagedata</c> table.
    /// Triggered manually from Options ("Import from Target Scheduler"). Greyed
    /// out when TS is not installed.
    ///
    /// Matching: TS <c>acquiredimage</c> rows are joined to NS <c>Images</c> rows
    /// on (TargetName, FilterName, |timestamp delta| ≤ ±2s).
    ///
    /// See RAW_THUMBNAILS_DESIGN.md §"TS Historical Import".
    /// </summary>
    public static class ThumbnailImporter {

        // Match window for TS acquireddate vs NS Timestamp. TS stores image-start
        // time and so does NS — same source, same value to within driver jitter.
        private const int MatchWindowSeconds = 2;

        public class ImportResult {
            public int Candidates { get; set; }   // NS rows missing thumbs
            public int Imported   { get; set; }   // wrote a JPEG to disk + bumped ThumbnailVersion
            public int Skipped    { get; set; }   // no TS match
            public int Failed     { get; set; }   // exception during write
        }

        /// <summary>
        /// Imports TS imagedata blobs for every NS image row with no existing
        /// thumbnail. Writes <c>{thumbsRoot}/{sessionId}/{imageId}_sm.jpg</c>
        /// and sets <c>ThumbnailVersion = 1</c> on success.
        ///
        /// Safe to re-run — only operates on rows with NULL ThumbnailVersion.
        /// </summary>
        public static ImportResult ImportFromTargetScheduler(string nsDbPath) {
            var result = new ImportResult();

            var ts = new TargetSchedulerDatabase();
            if (!ts.IsAvailable) {
                Logger.Info("NightSummary: ThumbnailImporter — TS database unavailable, nothing to import");
                return result;
            }

            var thumbsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA", "NightSummary", "thumbs");

            // Open NS DB directly for the iteration; TS DB read-only via the helper.
            using var nsConn = new SQLiteConnection($"Data Source={nsDbPath};Version=3;");
            nsConn.Open();

            // 1. Find NS rows that need thumbs.
            const string nsQuery = @"
                SELECT Id, SessionId, Timestamp, TargetName, Filter
                FROM Images
                WHERE (ThumbnailVersion IS NULL OR ThumbnailVersion = 0)
                  AND ImageType = 'LIGHT'
                ORDER BY Timestamp";
            using var cmd = new SQLiteCommand(nsQuery, nsConn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read()) {
                result.Candidates++;
                long imageId    = Convert.ToInt64(reader["Id"]);
                string sessionId = reader["SessionId"].ToString();
                DateTime ts0     = DateTime.Parse(reader["Timestamp"].ToString());
                string target    = reader["TargetName"]?.ToString() ?? "";
                string filter    = reader["Filter"]?.ToString() ?? "";

                try {
                    byte[] blob = ts.GetThumbnailBlob(target, filter, ts0, MatchWindowSeconds);
                    if (blob == null) { result.Skipped++; continue; }

                    var path = Thumbnails.GetThumbnailPath(thumbsRoot, sessionId, imageId, Thumbnails.VersionSmall);
                    if (!Thumbnails.WriteToDisk(path, blob)) { result.Failed++; continue; }

                    using var upd = new SQLiteCommand(
                        "UPDATE Images SET ThumbnailVersion = @v WHERE Id = @id", nsConn);
                    upd.Parameters.AddWithValue("@v", Thumbnails.VersionSmall);
                    upd.Parameters.AddWithValue("@id", imageId);
                    upd.ExecuteNonQuery();

                    result.Imported++;
                } catch (Exception ex) {
                    Logger.Warning($"NightSummary: ThumbnailImporter — failed for id={imageId}: {ex.Message}");
                    result.Failed++;
                }
            }

            Logger.Info($"NightSummary: ThumbnailImporter complete — candidates={result.Candidates}, imported={result.Imported}, skipped={result.Skipped}, failed={result.Failed}");
            return result;
        }
    }
}
