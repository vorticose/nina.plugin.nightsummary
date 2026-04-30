using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NINA.Plugin.NightSummary.Companion;

// User-editable config for the companion. Lives next to the binary by default,
// or wherever the user points the --config flag. Reflects the schema documented
// in COMPANION_PLAN.md.
public sealed class CompanionConfig {

    [JsonPropertyName("port")]
    public int Port { get; set; } = 8182;

    [JsonPropertyName("dataDir")]
    public string DataDir { get; set; } = "";

    [JsonPropertyName("nina")]
    public NinaConfig Nina { get; set; } = new();

    [JsonPropertyName("sync")]
    public SyncConfig Sync { get; set; } = new();

    public sealed class NinaConfig {
        [JsonPropertyName("host")]    public string Host    { get; set; } = "";
        [JsonPropertyName("port")]    public int    Port    { get; set; } = 8181;
        [JsonPropertyName("apiKey")]  public string ApiKey  { get; set; } = "";
    }

    public sealed class SyncConfig {
        [JsonPropertyName("onBoot")]
        public bool OnBoot { get; set; } = true;

        [JsonPropertyName("pollingIntervalMinutesOnFailure")]
        public int PollingIntervalMinutesOnFailure { get; set; } = 30;

        [JsonPropertyName("pollingIntervalHoursOnSuccess")]
        public int PollingIntervalHoursOnSuccess { get; set; } = 4;
    }

    // Where the synced data lives on disk. Defaults to a platform-appropriate
    // app-data dir when not specified in the file (handy for first run).
    public string ResolvedDataDir() {
        if (!string.IsNullOrWhiteSpace(DataDir)) return DataDir;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(appData, "NightSummaryCompanion");
    }

    public string ResolvedNinaUrl() {
        var host = string.IsNullOrWhiteSpace(Nina.Host) ? "localhost" : Nina.Host;
        return $"http://{host}:{Nina.Port}";
    }

    // ── Load / Save ──────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new() {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static CompanionConfig Load(string path) {
        if (!File.Exists(path)) {
            // Materialize a default file so the user has something concrete to edit
            var def = new CompanionConfig();
            Save(def, path);
            return def;
        }
        try {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<CompanionConfig>(json, JsonOpts);
            return loaded ?? new CompanionConfig();
        } catch (Exception ex) {
            throw new InvalidOperationException($"Failed to read {path}: {ex.Message}", ex);
        }
    }

    public static void Save(CompanionConfig cfg, string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(cfg, JsonOpts));
    }

    public void Validate() {
        if (string.IsNullOrWhiteSpace(Nina.Host))
            throw new InvalidOperationException("companion.json: nina.host is required");
        if (string.IsNullOrWhiteSpace(Nina.ApiKey))
            throw new InvalidOperationException("companion.json: nina.apiKey is required (copy from plugin settings)");
        if (Nina.Port <= 0 || Nina.Port > 65535)
            throw new InvalidOperationException($"companion.json: nina.port {Nina.Port} out of range");
        if (Port <= 0 || Port > 65535)
            throw new InvalidOperationException($"companion.json: port {Port} out of range");
    }
}
