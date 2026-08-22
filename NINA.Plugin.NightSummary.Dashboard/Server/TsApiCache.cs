using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NINA.Plugin.NightSummary.Server {

    // Persistent cache for the Tonight's Preview response. Lives on disk so the
    // companion can serve last-good when the primary (and its live TS API) is
    // off. Validity is gated on a noon-boundary expiry so overnight sessions
    // stay coherent — data cached at 11 PM is still valid at 2 AM the same
    // imaging night; the cache invalidates only after the next local noon.
    //
    // Schema: tonight-preview-cache.json next to nightsummary.sqlite in the
    // data dir. JSON over SQLite here is deliberate — single blob, no concurrent
    // writers, the existing nightsummary-dashboard-cache.sqlite is already
    // schema-managed for image/altitude work and we don't need to expand it
    // for one row.
    internal sealed class TsApiCache {

        [JsonPropertyName("cachedAtUtc")] public DateTime CachedAtUtc { get; set; }
        [JsonPropertyName("payload")]     public string Payload       { get; set; } = "";

        private static readonly JsonSerializerOptions JsonOpts = new() {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static TsApiCache? Load(string path) {
            try {
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<TsApiCache>(json, JsonOpts);
            } catch {
                // Corrupt / partial write → treat as miss; primary will refresh on next call.
                return null;
            }
        }

        public static void Save(string path, string payload) {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                var entry = new TsApiCache { CachedAtUtc = DateTime.UtcNow, Payload = payload };
                // Write to temp then rename — no half-written file ever observable to a reader.
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(entry, JsonOpts));
                if (File.Exists(path)) File.Replace(tmp, path, null, ignoreMetadataErrors: true);
                else                   File.Move(tmp, path);
            } catch { /* best effort */ }
        }

        // Valid when cachedAt is at or after the most recent local noon. Handles
        // overnight sessions correctly: 11 PM cache stays valid at 2 AM the same
        // imaging night because the previous noon is still the active boundary.
        public bool IsValidAt(DateTime nowLocal) {
            var lastNoon = nowLocal.Date.AddHours(12);
            if (lastNoon > nowLocal) lastNoon = lastNoon.AddDays(-1);
            var lastNoonUtc = lastNoon.ToUniversalTime();
            return CachedAtUtc >= lastNoonUtc;
        }
    }
}
