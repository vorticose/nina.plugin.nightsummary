using NINA.Plugin.NightSummary.Data;
using System;
using System.IO;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Disk-bounded retention pass tests. Builds a fake thumbs root with a few
    /// session dirs of known size + age, runs Apply, asserts which dirs survive.
    ///
    /// See RAW_THUMBNAILS_DESIGN.md §"Retention".
    /// </summary>
    public class ThumbnailRetentionTests : IDisposable {

        private readonly string _root;

        public ThumbnailRetentionTests() {
            _root = Path.Combine(Path.GetTempPath(), "ns_retention_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose() {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        // Creates {root}/{sid}/dummy.jpg of {bytes} length.
        private void Seed(string sid, int bytes) {
            var dir = Path.Combine(_root, sid);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "1_sm.jpg"), new byte[bytes]);
        }

        // ── KeepAll ───────────────────────────────────────────────────────────

        [Fact]
        public void KeepAll_RemovesNothing() {
            Seed("a", 1024); Seed("b", 1024);
            var s = new NightSummarySettings { CaptureRawThumbnails = true, ThumbnailRetentionMode = "KeepAll" };
            var r = ThumbnailRetention.Apply(_root, s, sid => DateTime.UtcNow);

            Assert.Equal(0, r.SessionsRemoved);
            Assert.Equal(2, r.SessionsScanned);
            Assert.True(Directory.Exists(Path.Combine(_root, "a")));
            Assert.True(Directory.Exists(Path.Combine(_root, "b")));
        }

        // ── Disabled (master toggle off) ──────────────────────────────────────

        [Fact]
        public void Disabled_RemovesNothing_EvenInRolloverMode() {
            Seed("a", 1024);
            var s = new NightSummarySettings {
                CaptureRawThumbnails = false,   // master off
                ThumbnailRetentionMode = "RolloverByDays",
                ThumbnailRetentionDays = 1
            };
            var r = ThumbnailRetention.Apply(_root, s, sid => DateTime.UtcNow.AddDays(-365));

            Assert.Equal(0, r.SessionsRemoved);
            Assert.Equal(0, r.SessionsScanned);   // early return
            Assert.True(Directory.Exists(Path.Combine(_root, "a")));
        }

        // ── RolloverByDays ────────────────────────────────────────────────────

        [Fact]
        public void RolloverByDays_RemovesOnlyOlderThanCutoff() {
            Seed("recent", 1024);
            Seed("old",    1024);
            var s = new NightSummarySettings {
                CaptureRawThumbnails = true,
                ThumbnailRetentionMode = "RolloverByDays",
                ThumbnailRetentionDays = 30
            };

            var r = ThumbnailRetention.Apply(_root, s, sid =>
                sid == "old" ? DateTime.UtcNow.AddDays(-90) : DateTime.UtcNow.AddDays(-3));

            Assert.Equal(1, r.SessionsRemoved);
            Assert.True(Directory.Exists(Path.Combine(_root, "recent")));
            Assert.False(Directory.Exists(Path.Combine(_root, "old")));
        }

        [Fact]
        public void RolloverByDays_OrphanDirNoDbMatch_FallsBackToMtime() {
            // Dir on disk but no Sessions row — fallback to dir mtime. New dir
            // = recent → keeps (ages we can't backdate cheaply on Windows).
            Seed("orphan", 1024);
            var s = new NightSummarySettings {
                CaptureRawThumbnails = true,
                ThumbnailRetentionMode = "RolloverByDays",
                ThumbnailRetentionDays = 1
            };
            var r = ThumbnailRetention.Apply(_root, s, sid => null);

            Assert.Equal(0, r.SessionsRemoved);
            Assert.True(Directory.Exists(Path.Combine(_root, "orphan")));
        }

        // ── RolloverByGB ──────────────────────────────────────────────────────

        [Fact]
        public void RolloverByGB_KeepsNewestUntilCapReached() {
            // Three 1 KB sessions; cap at 0.000002 GB ≈ 2 KB → should drop the
            // oldest one. Newest two run to 2 KB exactly which is still > cap
            // (running total only crosses cap on third), so only the third
            // (oldest) gets removed.
            Seed("new", 1024);
            Seed("mid", 1024);
            Seed("old", 1024);
            var s = new NightSummarySettings {
                CaptureRawThumbnails = true,
                ThumbnailRetentionMode = "RolloverByGB",
                ThumbnailRetentionMaxGB = 2.0 / (1024.0 * 1024.0)   // ~2 KB cap
            };
            var r = ThumbnailRetention.Apply(_root, s, sid => sid switch {
                "new" => DateTime.UtcNow,
                "mid" => DateTime.UtcNow.AddDays(-1),
                "old" => DateTime.UtcNow.AddDays(-2),
                _     => DateTime.UtcNow
            });

            Assert.True(Directory.Exists(Path.Combine(_root, "new")));
            Assert.True(Directory.Exists(Path.Combine(_root, "mid")));
            Assert.False(Directory.Exists(Path.Combine(_root, "old")));
            Assert.Equal(1, r.SessionsRemoved);
        }

        [Fact]
        public void RolloverByGB_CapZero_NoOp() {
            Seed("a", 1024);
            var s = new NightSummarySettings {
                CaptureRawThumbnails = true,
                ThumbnailRetentionMode = "RolloverByGB",
                ThumbnailRetentionMaxGB = 0.0
            };
            var r = ThumbnailRetention.Apply(_root, s, sid => DateTime.UtcNow);

            Assert.Equal(0, r.SessionsRemoved);
            Assert.True(Directory.Exists(Path.Combine(_root, "a")));
        }

        // ── Missing root ──────────────────────────────────────────────────────

        [Fact]
        public void MissingRoot_ReturnsEmptyResult() {
            var nonexistent = Path.Combine(Path.GetTempPath(), "ns_missing_" + Guid.NewGuid().ToString("N"));
            var s = new NightSummarySettings {
                CaptureRawThumbnails = true,
                ThumbnailRetentionMode = "RolloverByDays",
                ThumbnailRetentionDays = 1
            };
            var r = ThumbnailRetention.Apply(nonexistent, s, sid => DateTime.UtcNow.AddDays(-100));
            Assert.Equal(0, r.SessionsScanned);
            Assert.Equal(0, r.SessionsRemoved);
        }
    }
}
