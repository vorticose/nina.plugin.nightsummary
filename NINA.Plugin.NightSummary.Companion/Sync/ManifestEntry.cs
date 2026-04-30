using System;
using System.Text.Json.Serialization;

namespace NINA.Plugin.NightSummary.Companion.Sync;

// Wire format for /api/export/manifest (one entry per file under reports/).
public sealed class ManifestEntry {
    [JsonPropertyName("path")]  public string Path  { get; set; } = "";
    [JsonPropertyName("size")]  public long   Size  { get; set; }
    [JsonPropertyName("mtime")] public DateTime Mtime { get; set; }
}

public sealed class ManifestResponse {
    [JsonPropertyName("files")]
    public ManifestEntry[] Files { get; set; } = Array.Empty<ManifestEntry>();
}
