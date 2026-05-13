using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Companion.Sync;

// Probes an arbitrary primary (host/port/apiKey triple) without touching the
// running SyncEngine. Used by the dashboard "Test connection" button so the
// user can validate creds before saving config.
public static class ConnectionTester {

    public sealed record Result(bool Ok, string? Version, int? Schema, string? Error);

    public static async Task<Result> TestAsync(string host, int port, string apiKey, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(host)) return new Result(false, null, null, "host is empty");
        if (port <= 0 || port > 65535)        return new Result(false, null, null, $"port {port} out of range");
        if (string.IsNullOrWhiteSpace(apiKey)) return new Result(false, null, null, "apiKey is empty");

        using var http = new HttpClient { BaseAddress = new Uri($"http://{host}:{port}") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        http.Timeout = TimeSpan.FromSeconds(5);

        try {
            using var resp = await http.GetAsync("/api/health", ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return new Result(false, null, null, "unauthorized — check apiKey");
            if (!resp.IsSuccessStatusCode)
                return new Result(false, null, null, $"primary returned {(int)resp.StatusCode}");

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var version = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
            int? schema = doc.RootElement.TryGetProperty("schemaVersion", out var s) && s.ValueKind == JsonValueKind.Number
                ? s.GetInt32() : null;
            return new Result(true, version, schema, null);
        } catch (TaskCanceledException) {
            return new Result(false, null, null, "timed out (5s) — primary not reachable");
        } catch (HttpRequestException ex) {
            return new Result(false, null, null, ex.Message);
        } catch (Exception ex) {
            return new Result(false, null, null, ex.Message);
        }
    }
}
