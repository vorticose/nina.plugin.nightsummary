using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.NightSummary.Session {
    /// <summary>
    /// Holds the pre-annotation bitmap between BeforeFinalizeImageSaved and the
    /// matching ImageSaved, keyed by ExposureStart. Each entry is a full-resolution
    /// bitmap (~260 MB on a large colour sensor), so the cache is capped: when it
    /// grows past <c>capacity</c> the oldest exposure is dropped. NINA saves frames
    /// one at a time, so only a stranded entry (a save whose ImageSaved never
    /// arrives) is ever evicted. Issue #29: entries stranded until EndSession
    /// pushed NINA to 58 GB.
    /// </summary>
    internal sealed class PendingFrameCache<T> where T : class {
        private readonly Dictionary<DateTime, T> _items = new Dictionary<DateTime, T>();
        private readonly object _lock = new object();
        private readonly int _capacity;

        public PendingFrameCache(int capacity) {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        public int Count { get { lock (_lock) return _items.Count; } }

        public void Put(DateTime exposureStart, T value) {
            lock (_lock) {
                _items[exposureStart] = value;
                while (_items.Count > _capacity)
                    _items.Remove(_items.Keys.Min());
            }
        }

        /// <summary>Removes and returns the entry, or null if there is none.</summary>
        public T Take(DateTime exposureStart) {
            lock (_lock) {
                return _items.Remove(exposureStart, out var value) ? value : null;
            }
        }

        public void Clear() {
            lock (_lock) _items.Clear();
        }
    }
}
