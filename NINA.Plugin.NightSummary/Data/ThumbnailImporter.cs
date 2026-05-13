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

        // Match window for TS acquireddate vs NS-derived shifted timestamp. TS stamps
        // ExposureStart; pre-fix NS rows stamp ImageSaved (= ExposureStart + ExposureDuration
        // + ~5–15s overhead). We compensate by subtracting ExposureDuration from NS
        // timestamps below, so the residual drift is just the post-exposure overhead.
        // ±30s covers all observed jitter (FITS write + plate solve variance).
        private const int MatchWindowSeconds = 30;

        public class ImportResult {
            public int Candidates       { get; set; }   // NS rows missing thumbs at the start of this run
            public int AlreadyImported  { get; set; }   // NS LIGHT rows that already had ThumbnailVersion>0
            public int Imported         { get; set; }   // wrote a JPEG to disk + bumped ThumbnailVersion this run
            public int Skipped          { get; set; }   // no TS match
            public int Failed           { get; set; }   // exception during write
        }

        /// <summary>
        /// Imports TS imagedata blobs for every NS image row with no existing
        /// thumbnail. Writes <c>{thumbsRoot}/{sessionId}/{imageId}_sm.jpg</c>
        /// and sets <c>ThumbnailVersion = 1</c> on success.
        ///
        /// Safe to re-run — only operates on rows with NULL ThumbnailVersion.
        ///
        /// <paramref name="onProgress"/> is invoked roughly every 10 rows with
        /// (processed, total). Total is determined upfront via a COUNT query
        /// before the iteration begins. Pass null to disable progress reporting.
        /// </summary>
        public static ImportResult ImportFromTargetScheduler(string nsDbPath, string thumbsRoot = null, Action<int, int> onProgress = null) {
            var result = new ImportResult();

            var ts = new TargetSchedulerDatabase();
            if (!ts.IsAvailable) {
                Logger.Info("NightSummary: ThumbnailImporter — TS database unavailable, nothing to import");
                return result;
            }

            // Caller passes the resolved root (from settings); fall back to default if missing.
            if (string.IsNullOrEmpty(thumbsRoot)) {
                thumbsRoot = Thumbnails.GetThumbnailsRoot(null);
            }

            // Open NS DB directly for the iteration. TS DB also held open across the
            // whole loop — opening per-row was the original bottleneck (~10× slowdown
            // vs single open + prepared command).
            using var nsConn = new SQLiteConnection($"Data Source={nsDbPath};Version=3;");
            nsConn.Open();
            using var tsConn = new SQLiteConnection($"Data Source={ts.GetDbPath()};Version=3;Read Only=True;");
            tsConn.Open();

            // Prepared TS lookup command — params get rebound per row, command compiled once.
            using var tsCmd = new SQLiteCommand(@"
                SELECT i.imagedata
                FROM acquiredimage a
                JOIN target       t ON a.targetId = t.id
                JOIN imagedata    i ON i.AcquiredImageId = a.Id
                WHERE a.acquireddate BETWEEN @Lo AND @Hi
                  AND t.name      = @Target COLLATE NOCASE
                  AND a.filtername = @Filter COLLATE NOCASE
                ORDER BY ABS(a.acquireddate - @Center)
                LIMIT 1", tsConn);
            var pLo     = tsCmd.Parameters.Add("@Lo",     System.Data.DbType.Int64);
            var pHi     = tsCmd.Parameters.Add("@Hi",     System.Data.DbType.Int64);
            var pCenter = tsCmd.Parameters.Add("@Center", System.Data.DbType.Int64);
            var pTarget = tsCmd.Parameters.Add("@Target", System.Data.DbType.String);
            var pFilter = tsCmd.Parameters.Add("@Filter", System.Data.DbType.String);
            tsCmd.Prepare();

            // Pre-count total candidates so the UI can render "x of y" progress, and
            // pre-count already-imported rows so the user sees real numbers when they
            // re-run an idempotent job (instead of a confusing "0 of 0").
            int total = 0;
            using (var ccmd = new SQLiteCommand(
                @"SELECT COUNT(*) FROM Images
                  WHERE (ThumbnailVersion IS NULL OR ThumbnailVersion = 0) AND ImageType = 'LIGHT'", nsConn)) {
                total = Convert.ToInt32(ccmd.ExecuteScalar());
            }
            using (var ccmd = new SQLiteCommand(
                @"SELECT COUNT(*) FROM Images
                  WHERE ThumbnailVersion > 0 AND ImageType = 'LIGHT'", nsConn)) {
                result.AlreadyImported = Convert.ToInt32(ccmd.ExecuteScalar());
            }
            onProgress?.Invoke(0, total);

            // Wrap every UPDATE in one transaction so SQLite fsyncs once at the end
            // instead of per-row. Crash mid-import = files-on-disk are real but their
            // ThumbnailVersion didn't get bumped, so a re-run will re-import them
            // (idempotent — same blob, overwrites same path).
            using var tx = nsConn.BeginTransaction();

            // Prepared NS update — params rebound per row, command compiled once.
            using var updCmd = new SQLiteCommand(
                "UPDATE Images SET ThumbnailVersion = @v WHERE Id = @id", nsConn, tx);
            var pVer = updCmd.Parameters.Add("@v",  System.Data.DbType.Int32);
            var pId  = updCmd.Parameters.Add("@id", System.Data.DbType.Int64);
            pVer.Value = Thumbnails.VersionSmall;
            updCmd.Prepare();

            // 1. Find NS rows that need thumbs.
            const string nsQuery = @"
                SELECT Id, SessionId, Timestamp, TargetName, Filter, ExposureDuration
                FROM Images
                WHERE (ThumbnailVersion IS NULL OR ThumbnailVersion = 0)
                  AND ImageType = 'LIGHT'
                ORDER BY Timestamp";
            using var cmd = new SQLiteCommand(nsQuery, nsConn, tx);
            using var reader = cmd.ExecuteReader();

            int processed = 0;
            while (reader.Read()) {
                result.Candidates++;
                long imageId    = Convert.ToInt64(reader["Id"]);
                string sessionId = reader["SessionId"].ToString();
                DateTime ts0     = DateTime.Parse(reader["Timestamp"].ToString());
                string target    = reader["TargetName"]?.ToString() ?? "";
                string filter    = reader["Filter"]?.ToString() ?? "";
                double dur       = reader["ExposureDuration"] == DBNull.Value
                    ? 0 : Convert.ToDouble(reader["ExposureDuration"]);

                // Legacy NS rows stamp ImageSaved time, not ExposureStart. Shift back by
                // exposure duration so the search center aligns with TS's acquireddate.
                // (Empirically recovers 100% of rows that have any TS counterpart, vs
                // 0% with the unshifted ±2s window.)
                var searchTs = dur > 0 ? ts0.AddSeconds(-dur) : ts0;

                try {
                    long center = new DateTimeOffset(searchTs.ToUniversalTime()).ToUnixTimeSeconds();
                    pLo.Value     = center - MatchWindowSeconds;
                    pHi.Value     = center + MatchWindowSeconds;
                    pCenter.Value = center;
                    pTarget.Value = target;
                    pFilter.Value = filter ?? "";
                    var rawBlob = tsCmd.ExecuteScalar();
                    byte[] blob = (rawBlob == null || rawBlob == DBNull.Value) ? null : (byte[])rawBlob;
                    if (blob == null) { result.Skipped++; continue; }

                    var path = Thumbnails.GetThumbnailPath(thumbsRoot, sessionId, imageId, Thumbnails.VersionSmall);
                    if (!Thumbnails.WriteToDisk(path, blob)) { result.Failed++; continue; }

                    pId.Value = imageId;
                    updCmd.ExecuteNonQuery();

                    result.Imported++;
                } catch (Exception ex) {
                    Logger.Warning($"NightSummary: ThumbnailImporter — failed for id={imageId}: {ex.Message}");
                    result.Failed++;
                }

                processed++;
                // Throttle UI updates to every 10 rows — fine-grained enough for live
                // feedback, infrequent enough to avoid hammering the dispatcher queue.
                if (processed % 10 == 0 || processed == total) {
                    onProgress?.Invoke(processed, total);
                }
            }

            // Reader must close before commit — SQLite won't let us commit while a
            // statement is still open on the connection.
            reader.Close();
            tx.Commit();

            Logger.Info($"NightSummary: ThumbnailImporter complete — candidates={result.Candidates}, imported={result.Imported}, skipped={result.Skipped}, failed={result.Failed}");
            return result;
        }
    }
}
