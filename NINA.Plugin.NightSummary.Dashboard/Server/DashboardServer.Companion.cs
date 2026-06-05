using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Data;
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
        // X-Sync-Trigger: push  marks the request as a session-end push from the
        // primary. When the user has disabled AcceptPush, push-triggered calls
        // return 200 with skipped=true and no sync runs. Manual user-clicked
        // syncs (no header / non-push value) always run.
        private async Task HandleCompanionSync(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            if (_companion == null) {
                await WriteJson(res, 404, new { error = "companion mode not active" });
                done?.Invoke(404, null);
                return;
            }
            var c = _companion.GetConfig();
            var isPush = req.Headers != null
                         && req.Headers.TryGetValue("X-Sync-Trigger", out var tr)
                         && string.Equals(tr, "push", StringComparison.OrdinalIgnoreCase);
            if (isPush && !c.AcceptPush) {
                await WriteJson(res, 200, new { ok = true, skipped = true, reason = "push notifications disabled" });
                done?.Invoke(200, "push skipped (user-disabled)");
                return;
            }
            try {
                var s = await _companion.TriggerSyncAsync();
                c = _companion.GetConfig();
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
        // Body: { host, port, onBoot, pollingIntervalHoursOnSuccess,
        //         pollingIntervalMinutesOnFailure, dashboardPort? }
        // Pairing tokens are managed via /api/setup/claim (wizard), not here.
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
                    OnBoot:                          GetBool(root, "onBoot", true),
                    PollingIntervalHoursOnSuccess:   GetInt(root, "pollingIntervalHoursOnSuccess", 4),
                    PollingIntervalMinutesOnFailure: GetInt(root, "pollingIntervalMinutesOnFailure", 30),
                    DashboardPort:                   GetOptionalInt(root, "dashboardPort"),
                    AcceptPush:                      GetOptionalBool(root, "acceptPush"),
                    EnableReadOnlyMirror:            GetOptionalBool(root, "enableReadOnlyMirror"),
                    ReadOnlyMirrorPort:              GetOptionalInt(root, "readOnlyMirrorPort"));

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
            dataDir                         = s.DataDir,
            onBoot                          = s.OnBoot,
            pollingIntervalHoursOnSuccess   = s.PollingIntervalHoursOnSuccess,
            pollingIntervalMinutesOnFailure = s.PollingIntervalMinutesOnFailure,
            dashboardPort                   = s.DashboardPort,
            isComplete                      = s.IsComplete,
            incompleteReason                = s.IncompleteReason,
            pairingTokenSet                 = s.PairingTokenSet,
            acceptPush                      = s.AcceptPush,
            enableReadOnlyMirror            = s.EnableReadOnlyMirror,
            readOnlyMirrorPort              = s.ReadOnlyMirrorPort,
        };

        // POST /api/companion/quit  — companion-only. Returns 200 then exits
        // process with code 0. Watchdog wrapper in the .app sees the clean
        // exit and stops the respawn loop, so the companion really goes away
        // until the user relaunches the .app.
        private async Task HandleCompanionQuit(TcpHttpResponse res, Action<int, string> done) {
            if (_companion == null) {
                await WriteJson(res, 404, new { error = "companion mode not active" });
                done?.Invoke(404, null);
                return;
            }
            await WriteJson(res, 200, new { ok = true, action = "quit" });
            done?.Invoke(200, "quit requested");
            // Brief delay so the response actually flushes to the client
            // before the process dies. Fire-and-forget — not awaited.
            _ = Task.Run(async () => {
                await Task.Delay(250);
                log?.Info("Companion: quit requested via dashboard — exiting cleanly (code 0).");
                Environment.Exit(0);
            });
        }

        // POST /api/companion/restart  — companion-only. Returns 200 then
        // exits with code 88 (sentinel for "respawn me"). The watchdog
        // wrapper sees the non-zero/non-0 exit code and restarts the binary
        // within ~1s. Dashboard polls /api/health to detect when the new
        // process is ready, then reloads the page.
        private async Task HandleCompanionRestart(TcpHttpResponse res, Action<int, string> done) {
            if (_companion == null) {
                await WriteJson(res, 404, new { error = "companion mode not active" });
                done?.Invoke(404, null);
                return;
            }
            await WriteJson(res, 200, new { ok = true, action = "restart" });
            done?.Invoke(200, "restart requested");
            _ = Task.Run(async () => {
                await Task.Delay(250);
                log?.Info("Companion: restart requested via dashboard — exiting code 88 for watchdog respawn.");
                Environment.Exit(88);
            });
        }

        // GET /api/companion/autostart — companion-only. Reports whether
        // "start at login" is supported on this OS/packaging and currently on.
        private async Task HandleGetAutostart(TcpHttpResponse res, Action<int, string> done) {
            if (_companion == null) {
                await WriteJson(res, 404, new { error = "companion mode not active" });
                done?.Invoke(404, null);
                return;
            }
            var st = CompanionAutostart.GetStatus();
            await WriteJson(res, 200, new { st.supported, st.enabled, st.mechanism, st.detail });
            done?.Invoke(200, $"autostart status supported={st.supported} enabled={st.enabled}");
        }

        // POST /api/companion/autostart — body {"enabled": true|false}. Writes
        // (or removes) the per-OS user-domain autostart entry, then returns the
        // refreshed status so the UI can reflect reality (incl. "saved, applies
        // next login" partial-success cases).
        private async Task HandleSetAutostart(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            if (_companion == null) {
                await WriteJson(res, 404, new { error = "companion mode not active" });
                done?.Invoke(404, null);
                return;
            }
            var body = await ReadBodyCappedAsync(req, res, done);
            if (body == null) return;
            bool enabled;
            try {
                using var doc = JsonDocument.Parse(body);
                enabled = doc.RootElement.TryGetProperty("enabled", out var e) && e.GetBoolean();
            } catch {
                await WriteJson(res, 400, new { error = "expected { \"enabled\": true|false }" });
                done?.Invoke(400, "bad body");
                return;
            }

            var (ok, error) = enabled ? CompanionAutostart.Enable() : CompanionAutostart.Disable();
            log?.Info($"Companion: autostart {(enabled ? "enable" : "disable")} -> ok={ok}{(error != null ? $" ({error})" : "")}");
            var st = CompanionAutostart.GetStatus();
            await WriteJson(res, ok ? 200 : 500,
                new { ok, error, st.supported, st.enabled, st.mechanism, st.detail });
            done?.Invoke(ok ? 200 : 500, $"autostart {(enabled ? "enable" : "disable")} ok={ok}");
        }

        // ── Pairing endpoints (primary only) ─────────────────────────────────
        //
        // _tokenStore is non-null only in primary mode. All three endpoints 404
        // with a clear message when _tokenStore is null so the companion process
        // never accidentally exposes pair-management routes that would have no
        // store to consult.

        // GET /api/companion/info — unauthenticated probe used by the wizard's
        // "Test Connection" step to distinguish "wrong host" from "wrong software"
        // from "version mismatch". Cheap; safe to hit before pairing.
        private async Task HandleCompanionInfo(TcpHttpResponse res, Action<int, string> done) {
            if (_tokenStore == null) {
                await WriteJson(res, 404, new { error = "pairing not available in companion mode" });
                done?.Invoke(404, null);
                return;
            }
            int pairedCount = _tokenStore.List().Count(t => t.IsPaired && !t.IsRevoked);
            await WriteJson(res, 200, new {
                ninaVersion         = _settings.NinaVersion ?? "",
                nsVersion           = _settings.PluginVersion ?? GetServerAssemblyVersion(),
                hasNs               = true,
                minCompanionVersion = "0.0.0",
                pairedCount,
            });
            done?.Invoke(200, $"info: {pairedCount} paired");
        }

        // POST /api/companion/pair  body: { token, companionName }
        // Claims a previously-generated pairing token. Logic matches the design's
        // failure-mode table:
        //   - missing/invalid body → 400
        //   - unknown token        → 401 unknown_token
        //   - revoked token        → 401 revoked
        //   - already paired with another companion and used within 7 days
        //                          → 409 already_paired
        //   - already paired but stale (>7d since lastUsedAt) → silently rebind
        //   - happy path           → 200 + { companionId, ninaVersion, nsVersion }
        private async Task HandleCompanionPair(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            if (_tokenStore == null) {
                await WriteJson(res, 404, new { error = "pairing not available in companion mode" });
                done?.Invoke(404, null);
                return;
            }
            try {
                using var sr = new StreamReader(req.InputStream);
                var body = await sr.ReadToEndAsync();
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var root = doc.RootElement;

                var token         = GetStr(root, "token", "");
                var companionName = GetStr(root, "companionName", "");
                if (string.IsNullOrWhiteSpace(token)) {
                    await WriteJson(res, 400, new { error = "token required" });
                    done?.Invoke(400, "missing token");
                    return;
                }
                if (string.IsNullOrWhiteSpace(companionName)) {
                    await WriteJson(res, 400, new { error = "companionName required" });
                    done?.Invoke(400, "missing companion name");
                    return;
                }

                var entry = _tokenStore.FindByToken(token);
                if (entry == null) {
                    await WriteJson(res, 401, new { error = "unknown_token" });
                    done?.Invoke(401, "unknown_token");
                    return;
                }
                if (entry.IsRevoked) {
                    await WriteJson(res, 401, new { error = "revoked" });
                    done?.Invoke(401, "revoked");
                    return;
                }

                // Already-paired guard: only refuse when a *different* companion
                // claimed the token recently. >7 days since lastUsedAt is treated
                // as the original companion gone for good — silently rebind.
                if (entry.IsPaired
                    && !string.IsNullOrEmpty(entry.CompanionName)
                    && !string.Equals(entry.CompanionName, companionName, StringComparison.OrdinalIgnoreCase)) {
                    var lastSeen = entry.LastUsedAt ?? entry.PairedAt ?? DateTime.UtcNow;
                    if (DateTime.UtcNow - lastSeen < TimeSpan.FromDays(7)) {
                        await WriteJson(res, 409, new {
                            error         = "already_paired",
                            companionName = entry.CompanionName,
                        });
                        done?.Invoke(409, $"already_paired: {entry.CompanionName}");
                        return;
                    }
                }

                _tokenStore.MarkPaired(entry.Id, companionName);
                // Capture initial push URL from the pairing request itself so
                // session-end pushes work immediately after pairing — no need
                // to wait for the first authenticated sync to refresh it.
                UpdatePushUrlFromRequest(req, entry.Id);
                await WriteJson(res, 200, new {
                    companionId = entry.Id,
                    ninaVersion = _settings.NinaVersion ?? "",
                    nsVersion   = _settings.PluginVersion ?? GetServerAssemblyVersion(),
                });
                done?.Invoke(200, $"paired: {entry.Id} ({companionName})");
            } catch (JsonException ex) {
                await WriteJson(res, 400, new { error = "invalid json: " + ex.Message });
                done?.Invoke(400, ex.Message);
            } catch (Exception ex) {
                log?.Error("Companion pair failed", ex);
                await WriteJson(res, 500, new { error = ex.Message });
                done?.Invoke(500, ex.Message);
            }
        }

        // POST /api/companion/revoke  body: { id }
        // Auth: bearer token must hash to a valid, non-revoked, paired entry in
        // the store. This is "companion revoking itself" — the WPF Options panel
        // revokes in-process via CompanionTokenStore.Revoke directly (no HTTP).
        private async Task HandleCompanionRevoke(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            if (_tokenStore == null) {
                await WriteJson(res, 404, new { error = "pairing not available in companion mode" });
                done?.Invoke(404, null);
                return;
            }
            try {
                var authHeader = req.Authorization;
                if (string.IsNullOrEmpty(authHeader)
                    || !authHeader.StartsWith("Bearer ", StringComparison.Ordinal)) {
                    await WriteJson(res, 401, new { error = "unauthorized" });
                    done?.Invoke(401, null);
                    return;
                }
                var bearer = authHeader.Substring("Bearer ".Length);
                var caller = _tokenStore.FindByToken(bearer);
                if (caller == null || caller.IsRevoked) {
                    await WriteJson(res, 401, new { error = "unauthorized" });
                    done?.Invoke(401, null);
                    return;
                }

                using var sr = new StreamReader(req.InputStream);
                var body = await sr.ReadToEndAsync();
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var id = GetStr(doc.RootElement, "id", "");
                if (string.IsNullOrWhiteSpace(id)) {
                    await WriteJson(res, 400, new { error = "id required" });
                    done?.Invoke(400, "missing id");
                    return;
                }

                if (!_tokenStore.Revoke(id)) {
                    // Either id unknown or already-revoked — same response either
                    // way; don't leak which it was.
                    await WriteJson(res, 404, new { error = "not_found_or_already_revoked" });
                    done?.Invoke(404, "no-op revoke");
                    return;
                }

                res.StatusCode = 204;
                done?.Invoke(204, $"revoked: {id}");
            } catch (JsonException ex) {
                await WriteJson(res, 400, new { error = "invalid json: " + ex.Message });
                done?.Invoke(400, ex.Message);
            } catch (Exception ex) {
                log?.Error("Companion revoke failed", ex);
                await WriteJson(res, 500, new { error = ex.Message });
                done?.Invoke(500, ex.Message);
            }
        }

        // Forgiving JSON pluckers — POST bodies come straight from the form
        // and may omit fields; treat missing/null as the default.
        private static string GetStr(JsonElement e, string name, string def) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? def) : def;
        private static int GetInt(JsonElement e, string name, int def) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : def;
        private static int? GetOptionalInt(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : (int?)null;
        private static bool? GetOptionalBool(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : (bool?)null;
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
