using System;
using System.IO;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Companion;

// One-time relocation of a v1 (single-rig) flat data dir into the v2 per-rig
// layout. v1 stored everything directly under {dataDir}:
//   {dataDir}/nightsummary.sqlite, schedulerdb.sqlite, reports/, thumbs/,
//            last_synced.json, tonight-preview-cache.json
// v2 nests each rig's complete tree under {dataDir}/rigs/{rigId}/ so N rigs
// never collide. logs/, hips-cache/, and the shared dashboard cache sqlite stay
// at the root (rig-agnostic).
//
// Safety model (per the plan's risk register):
//   - Skip entirely once {dataDir}/migration.done exists.
//   - Move (not copy) on the same volume — each rename is atomic.
//   - Move directories + state files first, the SQLite DBs LAST, so an interrupt
//     leaves the DB at the old path and the next boot re-runs cleanly (already
//     moved items are simply absent from root → skipped).
//   - On any failure, log and leave the marker UNwritten → retried next boot.
public static class CompanionMigration {

    // Items that belong to a single rig and must move under rigs/{id}/. Order
    // matters: DBs last (see class comment). Each entry is (name, isDirectory).
    private static readonly (string name, bool isDir)[] RigItems = {
        ("reports",                     true),
        ("thumbs",                      true),
        ("tonight-preview-cache.json",  false),
        ("last_synced.json",            false),
        ("schedulerdb.sqlite",          false),
        ("nightsummary.sqlite",         false),
    };

    // Relocate the flat root tree into rigs/{rigId}/ if (and only if) a v1 layout
    // is detected and not already migrated. rigId is the id of the rig the legacy
    // data belongs to — config.DefaultRig().Id after CompanionConfig.Load has run
    // its config-shape migration. No-op when rigId is null/empty.
    public static void RelocateDataDirIfNeeded(string rootDataDir, string? rigId, IDashboardLogger log) {
        if (string.IsNullOrWhiteSpace(rootDataDir) || string.IsNullOrWhiteSpace(rigId)) return;
        var marker = Path.Combine(rootDataDir, "migration.done");
        if (File.Exists(marker)) return;

        var rigRoot = Path.Combine(rootDataDir, "rigs", rigId);

        // Detect a v1 layout: a DB or reports dir sitting directly at the root.
        // Fresh installs (no flat data) just get the marker written so we don't
        // probe every boot.
        bool hasFlatData =
            File.Exists(Path.Combine(rootDataDir, "nightsummary.sqlite")) ||
            Directory.Exists(Path.Combine(rootDataDir, "reports")) ||
            Directory.Exists(Path.Combine(rootDataDir, "thumbs"));

        if (!hasFlatData) {
            TryWriteMarker(marker, log);
            return;
        }

        // If the target rig dir already holds a DB, a prior run got far enough —
        // finish reconciling any stragglers, then mark done.
        log.Info($"Companion: migrating v1 data dir → rigs/{rigId}/");
        try {
            Directory.CreateDirectory(rigRoot);
            foreach (var (name, isDir) in RigItems) {
                MoveItem(rootDataDir, rigRoot, name, isDir, log);
                // SQLite sidecars travel with their DB.
                if (!isDir && name.EndsWith(".sqlite", StringComparison.Ordinal)) {
                    foreach (var suffix in new[] { "-wal", "-shm", ".bak" })
                        MoveItem(rootDataDir, rigRoot, name + suffix, false, log);
                }
                if (!isDir && name.EndsWith(".json", StringComparison.Ordinal))
                    MoveItem(rootDataDir, rigRoot, name + ".bak", false, log);
            }
        } catch (Exception ex) {
            // Leave the marker unwritten — next boot retries the remaining moves.
            log.Error($"Companion: data-dir migration incomplete ({ex.Message}); will retry next boot.", ex);
            return;
        }

        TryWriteMarker(marker, log);
        log.Info("Companion: data-dir migration complete.");
    }

    // Atomic same-volume rename of a file or directory from src→dst. No-op when
    // the source is absent (already moved on a prior run) or the destination
    // already exists (don't clobber freshly synced data).
    private static void MoveItem(string srcRoot, string dstRoot, string name, bool isDir, IDashboardLogger log) {
        var src = Path.Combine(srcRoot, name);
        var dst = Path.Combine(dstRoot, name);
        if (isDir) {
            if (!Directory.Exists(src)) return;
            if (Directory.Exists(dst)) { log.Warn($"Companion: migration skip {name}/ — destination exists"); return; }
            Directory.Move(src, dst);
        } else {
            if (!File.Exists(src)) return;
            if (File.Exists(dst)) { log.Warn($"Companion: migration skip {name} — destination exists"); return; }
            File.Move(src, dst);
        }
    }

    private static void TryWriteMarker(string marker, IDashboardLogger log) {
        try {
            File.WriteAllText(marker,
                "Night Summary companion v2 data-dir migration completed.\n" +
                "Each rig's data lives under rigs/<rigId>/. Safe to leave this file in place.\n");
        } catch (Exception ex) {
            log.Warn($"Companion: could not write migration marker ({ex.Message}); migration may re-run.");
        }
    }
}
