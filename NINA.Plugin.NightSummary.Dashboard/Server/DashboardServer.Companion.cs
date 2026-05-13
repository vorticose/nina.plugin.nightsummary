using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Server {

    // Sync controls surfaced to the dashboard UI when running in companion mode.
    // Routes are registered unconditionally on the shared server; both endpoints
    // 404 (with a clear message) when _companion is null so the primary plugin
    // doesn't accidentally expose them.
    public partial class DashboardServer {

        // GET /api/companion/status — cheap, safe to poll.
        // We fold config.isComplete into the wire payload so the banner can
        // distinguish "primary offline" from "user hasn't filled out setup yet"
        // without a second round-trip.
        private async Task HandleCompanionStatus(TcpHttpResponse res, Action<int, string> done) {
            if (_companion == null) {
                await WriteJson(res, 404, new { error = "companion mode not active" });
                done?.Invoke(404, null);
                return;
            }
            var s = _companion.GetStatus();
            var c = _companion.GetConfig();
            await WriteJson(res, 200, ToWire(s, c));
            done?.Invoke(200, null);
        }

        // POST /api/companion/sync — coalesces concurrent calls inside the controller.
        private async Task HandleCompanionSync(TcpHttpResponse res, Action<int, string> done) {
            if (_companion == null) {
                await WriteJson(res, 404, new { error = "companion mode not active" });
                done?.Invoke(404, null);
                return;
            }
            try {
                var s = await _companion.TriggerSyncAsync();
                var c = _companion.GetConfig();
                await WriteJson(res, 200, ToWire(s, c));
                done?.Invoke(200, s.LastError == null ? "sync ok" : $"sync failed: {s.LastError}");
            } catch (Exception ex) {
                log?.Error("Companion sync trigger failed", ex);
                await WriteJson(res, 500, new { error = ex.Message });
                done?.Invoke(500, ex.Message);
            }
        }

        // GET /api/companion/config — masked api key, drives the Settings tab.
        private async Task HandleCompanionConfigGet(TcpHttpResponse res, Action<int, string> done) {
            if (_companion == null) {
                await WriteJson(res, 404, new { error = "companion mode not active" });
                done?.Invoke(404, null);
                return;
            }
            var c = _companion.GetConfig();
            await WriteJson(res, 200, ConfigToWire(c));
            done?.Invoke(200, null);
        }

        // POST /api/companion/config — save edits, hot-reload SyncEngine.
        // Body: { host, port, apiKey?, onBoot, pollingIntervalHoursOnSuccess,
        //         pollingIntervalMinutesOnFailure }
        // Omit apiKey to keep the existing one.
        private async Task HandleCompanionConfigSave(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            if (_companion == null) {
                await WriteJson(res, 404, new { error = "companion mode not active" });
                done?.Invoke(404, null);
                return;
            }
            try {
                using var sr = new StreamReader(req.InputStream);
                var body = await sr.ReadToEndAsync();
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var root = doc.RootElement;
                var edit = new CompanionConfigEdit(
                    Host:                            GetStr(root, "host", ""),
                    Port:                            GetInt(root, "port", 0),
                    ApiKey:                          GetOptionalStr(root, "apiKey"),
                    OnBoot:                          GetBool(root, "onBoot", true),
                    PollingIntervalHoursOnSuccess:   GetInt(root, "pollingIntervalHoursOnSuccess", 4),
                    PollingIntervalMinutesOnFailure: GetInt(root, "pollingIntervalMinutesOnFailure", 30));

                var result = await _companion.SaveConfigAsync(edit);
                if (!result.Ok) {
                    await WriteJson(res, 400, new { ok = false, error = result.Error, config = ConfigToWire(result.Snapshot) });
                    done?.Invoke(400, result.Error);
                    return;
                }
                await WriteJson(res, 200, new { ok = true, config = ConfigToWire(result.Snapshot) });
                done?.Invoke(200, "config saved");
            } catch (JsonException ex) {
                await WriteJson(res, 400, new { ok = false, error = "invalid json: " + ex.Message });
                done?.Invoke(400, ex.Message);
            } catch (Exception ex) {
                log?.Error("Companion config save failed", ex);
                await WriteJson(res, 500, new { ok = false, error = ex.Message });
                done?.Invoke(500, ex.Message);
            }
        }

        // POST /api/companion/test-connection — body: { host, port, apiKey? }
        // Empty/omitted apiKey reuses the saved value so the user doesn't have
        // to retype it just to verify host/port edits.
        private async Task HandleCompanionTestConnection(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            if (_companion == null) {
                await WriteJson(res, 404, new { error = "companion mode not active" });
                done?.Invoke(404, null);
                return;
            }
            try {
                using var sr = new StreamReader(req.InputStream);
                var body = await sr.ReadToEndAsync();
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var root = doc.RootElement;
                var host = GetStr(root, "host", "");
                var port = GetInt(root, "port", 0);
                var key  = GetStr(root, "apiKey", "");
                var r = await _companion.TestConnectionAsync(host, port, key);
                await WriteJson(res, 200, new {
                    ok      = r.Ok,
                    error   = r.Error,
                    version = r.Version,
                    schema  = r.Schema,
                });
                done?.Invoke(200, r.Ok ? "test ok" : $"test failed: {r.Error}");
            } catch (JsonException ex) {
                await WriteJson(res, 400, new { ok = false, error = "invalid json: " + ex.Message });
                done?.Invoke(400, ex.Message);
            } catch (Exception ex) {
                log?.Error("Companion test-connection failed", ex);
                await WriteJson(res, 500, new { ok = false, error = ex.Message });
                done?.Invoke(500, ex.Message);
            }
        }

        private static object ConfigToWire(CompanionConfigSnapshot s) => new {
            host                            = s.Host,
            port                            = s.Port,
            apiKeyMasked                    = s.ApiKeyMasked,
            apiKeySet                       = s.ApiKeySet,
            dataDir                         = s.DataDir,
            onBoot                          = s.OnBoot,
            pollingIntervalHoursOnSuccess   = s.PollingIntervalHoursOnSuccess,
            pollingIntervalMinutesOnFailure = s.PollingIntervalMinutesOnFailure,
            dashboardPort                   = s.DashboardPort,
            isComplete                      = s.IsComplete,
            incompleteReason                = s.IncompleteReason,
        };

        // Forgiving JSON pluckers — POST bodies come straight from the form
        // and may omit fields; treat missing/null as the default.
        private static string GetStr(JsonElement e, string name, string def) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? def) : def;
        private static string? GetOptionalStr(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        private static int GetInt(JsonElement e, string name, int def) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : def;
        private static bool GetBool(JsonElement e, string name, bool def) =>
            e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : def;

        // Wire shape — flat object, ISO timestamps, byte counts as numbers.
        private static object ToWire(CompanionSyncStatus s, CompanionConfigSnapshot c) => new {
            lastAttemptUtc        = s.LastAttemptUtc?.ToString("o"),
            lastSuccessUtc        = s.LastSuccessUtc?.ToString("o"),
            lastError             = s.LastError,
            primaryVersion        = s.PrimaryVersion,
            primarySchema         = s.PrimarySchema,
            dbBytes               = s.DbBytes,
            tsDbBytes             = s.TsDbBytes,
            filesAdded            = s.FilesAdded,
            filesUpdated          = s.FilesUpdated,
            filesDeleted          = s.FilesDeleted,
            thumbsAdded           = s.ThumbsAdded,
            thumbsUpdated         = s.ThumbsUpdated,
            thumbsDeleted         = s.ThumbsDeleted,
            isRunning             = s.IsRunning,
            primaryReachable      = s.PrimaryReachable,
            primaryLastCheckedUtc = s.PrimaryLastCheckedUtc?.ToString("o"),
            isComplete            = c.IsComplete,
            incompleteReason      = c.IncompleteReason,
        };
    }
}
