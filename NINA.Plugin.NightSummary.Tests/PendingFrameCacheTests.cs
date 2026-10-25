using NINA.Plugin.NightSummary.Session;
using System;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// The pre-annotation bitmap stash between BeforeFinalizeImageSaved and
    /// ImageSaved. Issue #29: entries that were never taken stayed until
    /// EndSession, ~260 MB each.
    /// </summary>
    public class PendingFrameCacheTests {

        private static readonly DateTime T0 = new DateTime(2026, 10, 24, 5, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Take_ReturnsAndRemovesEntry() {
            var cache = new PendingFrameCache<string>(3);
            cache.Put(T0, "frame");

            Assert.Equal("frame", cache.Take(T0));
            Assert.Equal(0, cache.Count);
            Assert.Null(cache.Take(T0));
        }

        [Fact]
        public void Take_UnknownKey_ReturnsNull() {
            var cache = new PendingFrameCache<string>(3);
            cache.Put(T0, "frame");

            Assert.Null(cache.Take(T0.AddSeconds(1)));
            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public void Put_PastCapacity_EvictsOldestExposure() {
            var cache = new PendingFrameCache<string>(3);
            // Inserted out of order: eviction goes by ExposureStart, not insertion.
            cache.Put(T0.AddMinutes(1), "b");
            cache.Put(T0, "a");
            cache.Put(T0.AddMinutes(2), "c");
            cache.Put(T0.AddMinutes(3), "d");

            Assert.Equal(3, cache.Count);
            Assert.Null(cache.Take(T0));
            Assert.Equal("b", cache.Take(T0.AddMinutes(1)));
            Assert.Equal("d", cache.Take(T0.AddMinutes(3)));
        }

        [Fact]
        public void Put_ManyNeverTaken_StaysBounded() {
            // A dawn flat run whose ImageSaved never takes its entries must not grow.
            var cache = new PendingFrameCache<string>(3);
            for (int i = 0; i < 30; i++)
                cache.Put(T0.AddSeconds(i * 5), "flat" + i);

            Assert.Equal(3, cache.Count);
            Assert.Equal("flat29", cache.Take(T0.AddSeconds(29 * 5)));
        }

        [Fact]
        public void Put_SameKey_Replaces() {
            var cache = new PendingFrameCache<string>(3);
            cache.Put(T0, "first");
            cache.Put(T0, "second");

            Assert.Equal(1, cache.Count);
            Assert.Equal("second", cache.Take(T0));
        }

        [Fact]
        public void Clear_EmptiesCache() {
            var cache = new PendingFrameCache<string>(3);
            cache.Put(T0, "a");
            cache.Put(T0.AddSeconds(1), "b");
            cache.Clear();

            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void Constructor_RejectsZeroCapacity() {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PendingFrameCache<string>(0));
        }
    }
}
