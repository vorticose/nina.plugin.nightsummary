using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NINA.Plugin.NightSummary.Companion.Sync;

// Persisted in <dataDir>/last_synced.json so subsequent runs know what mtime
// cutoff to use for the incremental reports zip and what to surface in the
// dashboard's staleness banner.
public sealed class SyncState {
    [JsonPropertyName("lastAttemptUtc")]   public DateTime? LastAttemptUtc   { get; set; }
    [JsonPropertyName("lastSuccessUtc")]   public DateTime? LastSuccessUtc   { get; set; }
    [JsonPropertyName("lastError")]        public string?   LastError        { get; set; }
    [JsonPropertyName("primaryVersion")]   public string?   PrimaryVersion   { get; set; }
    [JsonPropertyName("primarySchema")]    public int?      PrimarySchema    { get; set; }

    // High-water mtime from the last successful manifest. The next sync passes
    // this as ?since= to /api/export/reports so we only re-download what changed
    // (and what's still being actively written, e.g. live stack masters).
    [JsonPropertyName("lastReportMtimeUtc")]
    public DateTime? LastReportMtimeUtc { get; set; }

    private static readonly JsonSerializerOptions JsonOpts = new() {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static SyncState Load(string path) {
        if (!File.Exists(path)) return new SyncState();
        try {
            return JsonSerializer.Deserialize<SyncState>(File.ReadAllText(path), JsonOpts) ?? new SyncState();
        } catch {
            return new SyncState();
        }
    }

    public void Save(string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
    }
}
