using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
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

        // GET /api/companion/status/all — one status entry per configured rig, for
        // the settings page rig list + the switcher's aggregate "another rig is
        // erroring" dot. Each rig resolves to its own controller; a rig with no
        // controller (shouldn't happen in companion mode) is skipped.
        private async Task HandleCompanionStatusAll(TcpHttpResponse res, Action<int, string> done) {
            if (_rigs.Default.Companion == null) {
                await WriteJson(res, 404, new { error = "companion mode not active" });
                done?.Invoke(404, null);
                return;
            }
            var rigs = _rigs.All.Where(r => r.Companion != null).Select(r => {
                var s = r.Companion!.GetStatus();
                var c = r.Companion!.GetConfig();
                return new {
                    id      = r.Id,
                    name    = r.Name,
                    enabled = r.Enabled,
                    status  = ToWire(s, c),
                };
            }).ToList();
            await WriteJson(res, 200, new { defaultRig = _rigs.Default.Id, rigs });
            done?.Invoke(200, $"status/all: {rigs.Count} rig(s)");
        }

        // GET /api/companion/rigs — light list for the settings rig section:
        // id, name, enabled, host, completeness. Heavier per-rig sync status comes
        // from /api/companion/status/all.
        private async Task HandleCompanionRigsList(TcpHttpResponse res, Action<int, string> done) {
            if (_rigs.Default.Companion == null) {
                await WriteJson(res, 404, new { error = "companion mode not active" });
                done?.Invoke(404, null);
                return;
            }
            var rigs = _rigs.All.Where(r => r.Companion != null).Select(r => {
                var c = r.Companion!.GetConfig();
                return new {
                    id              = r.Id,
                    name            = r.Name,
                    enabled         = r.Enabled,
                    host            = c.Host,
                    port            = c.Port,
                    isComplete      = c.IsComplete,
                    pairingTokenSet = c.PairingTokenSet,
                };
            }).ToList();
            await WriteJson(res, 200, new {
                supportsManagement = _rigs.SupportsManagement,
                defaultRig         = _rigs.Default.Id,
                rigs,
            });
            done?.Invoke(200, $"rigs: {rigs.Count}");
        }

        // POST /api/companion/rigs  body { name } — create a new unpaired rig and
        // return its id. The wizard then pairs it via /api/setup/claim?rig=<id>.
        private async Task HandleCompanionRigAdd(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            if (!_rigs.SupportsManagement) {
                await WriteJson(res, 400, new { error = "multi-rig management not available" });
                done?.Invoke(400, "no management");
                return;
            }
            try {
                var body = await ReadBodyCappedAsync(req, res, done);
                if (body == null) return;
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var name = GetStr(doc.RootElement, "name", "");
                var id = await _rigs.AddRigAsync(name);
                await WriteJson(res, 200, new { ok = true, id });
                done?.Invoke(200, $"rig added: {id}");
            } catch (Exception ex) {
                log?.Error("Companion add-rig failed", ex);
                await WriteJson(res, 500, new { ok = false, error = ex.Message });
                done?.Invoke(500, ex.Message);
            }
        }

        // POST /api/companion/rigs/{id}/remove  body { deleteData? } — tear down a
        // rig; optionally delete its synced data dir.
        private async Task HandleCompanionRigRemove(TcpHttpRequest req, TcpHttpResponse res, string rigId, Action<int, string> done) {
            if (!_rigs.SupportsManagement) {
                await WriteJson(res, 400, new { error = "multi-rig management not available" });
                done?.Invoke(400, "no management");
                return;
            }
            try {
                var body = await ReadBodyCappedAsync(req, res, done);
                if (body == null) return;
                bool deleteData = false;
                if (!string.IsNullOrWhiteSpace(body)) {
                    using var doc = JsonDocument.Parse(body);
                    deleteData = GetBool(doc.RootElement, "deleteData", false);
                }
                var ok = _rigs.RemoveRig(rigId, deleteData);
                await WriteJson(res, ok ? 200 : 404, new { ok, error = ok ? null : "unknown rig or last rig" });
                done?.Invoke(ok ? 200 : 404, ok ? $"rig removed: {rigId}" : "remove no-op");
            } catch (Exception ex) {
                log?.Error("Companion remove-rig failed", ex);
                await WriteJson(res, 500, new { ok = false, error = ex.Message });
                done?.Invoke(500, ex.Message);
            }
        }

        // POST /api/companion/rigs/{id}/enable  body { enabled } — toggle a rig's
        // sync loops on/off without removing it.
        private async Task HandleCompanionRigEnable(TcpHttpRequest req, TcpHttpResponse res, string rigId, Action<int, string> done) {
            if (!_rigs.SupportsManagement) {
                await WriteJson(res, 400, new { error = "multi-rig management not available" });
                done?.Invoke(400, "no management");
                return;
            }
            try {
                var body = await ReadBodyCappedAsync(req, res, done);
                if (body == null) return;
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var enabled = GetBool(doc.RootElement, "enabled", true);
                var ok = _rigs.SetRigEnabled(rigId, enabled);
                await WriteJson(res, ok ? 200 : 404, new { ok, enabled, error = ok ? null : "unknown rig" });
                done?.Invoke(ok ? 200 : 404, $"rig {rigId} enabled={enabled} ok={ok}");
            } catch (Exception ex) {
                log?.Error("Companion enable-rig failed", ex);
                await WriteJson(res, 500, new { ok = false, error = ex.Message });
                done?.Invoke(500, ex.Message);
            }
        }

        // POST /api/companion/rigs/{id}/rename  body { name } — change a rig's
        // display label. Works for a single-rig install too.
        private async Task HandleCompanionRigRename(TcpHttpRequest req, TcpHttpResponse res, string rigId, Action<int, string> done) {
            if (!_rigs.SupportsManagement) {
                await WriteJson(res, 400, new { error = "multi-rig management not available" });
                done?.Invoke(400, "no management");
                return;
            }
            try {
                var body = await ReadBodyCappedAsync(req, res, done);
                if (body == null) return;
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var name = GetStr(doc.RootElement, "name", "");
                if (string.IsNullOrWhiteSpace(name)) {
                    await WriteJson(res, 400, new { ok = false, error = "name required" });
                    done?.Invoke(400, "blank name");
                    return;
                }
                var ok = _rigs.SetRigName(rigId, name);
                await WriteJson(res, ok ? 200 : 404, new { ok, name = name.Trim(), error = ok ? null : "unknown rig" });
                done?.Invoke(ok ? 200 : 404, $"rig {rigId} rename ok={ok}");
            } catch (Exception ex) {
                log?.Error("Companion rename-rig failed", ex);
                await WriteJson(res, 500, new { ok = false, error = ex.Message });
                done?.Invoke(500, ex.Message);
            }
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
            var isPush = req.Headers != null
                         && req.Headers.TryGetValue("X-Sync-Trigger", out var tr)
                         && string.Equals(tr, "push", StringComparison.OrdinalIgnoreCase);
            var explicitRig = req.QueryString["rig"];

            // A session-end push from the primary carries no ?rig=, so route it by
            // matching the request's source address to a rig's configured host.
            // No confident match → fan out to every enabled rig (pulls are
            // incremental + coalesced, so a spurious extra sync is cheap and safe).
            // A user-clicked Sync Now always carries ?rig=ACTIVE, so it stays on
            // the single active controller below.
            if (isPush && string.IsNullOrEmpty(explicitRig)) {
                var targets = ResolvePushTargets(req);
                var ran = new List<object>();
                int skipped = 0;
                foreach (var b in targets) {
                    var cc = b.Companion!.GetConfig();
                    if (!cc.AcceptPush) { skipped++; continue; }
                    try {
                        var st = await b.Companion!.TriggerSyncAsync();
                        ran.Add(new { id = b.Id, ok = st.LastError == null, error = st.LastError });
                    } catch (Exception ex) {
                        log?.Error($"Companion push sync failed for rig {b.Id}", ex);
                        ran.Add(new { id = b.Id, ok = false, error = ex.Message });
                    }
                }
                await WriteJson(res, 200, new { ok = true, push = true, synced = ran, skippedDisabled = skipped });
                done?.Invoke(200, $"push synced {ran.Count} rig(s){(skipped > 0 ? $", {skipped} skipped" : "")}");
                return;
            }

            var c = _companion.GetConfig();
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

        // Pick the rig(s) a session-end push should sync. Prefer the rig whose
        // configured host string-matches the request's source IP; if nothing
        // matches (NAT, Tailscale MagicDNS, hostname host), fall back to all
        // enabled rigs. Only ever returns rigs with a controller + AcceptPush is
        // re-checked by the caller.
        private List<RigBackend> ResolvePushTargets(TcpHttpRequest req) {
            var enabled = _rigs.All.Where(r => r.Enabled && r.Companion != null).ToList();
            var ip = req.RemoteIp?.ToString();
            if (!string.IsNullOrEmpty(ip)) {
                var matched = enabled
                    .Where(r => string.Equals(r.Companion!.GetConfig().Host, ip, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matched.Count > 0) {
                    log?.Info($"Push routed by source IP {ip} → rig(s) {string.Join(",", matched.Select(m => m.Id))}");
                    return matched;
                }
            }
            log?.Info($"Push source {ip ?? "?"} matched no rig host — fanning out to {enabled.Count} enabled rig(s).");
            return enabled;
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
            // OS the companion is running on, so the dashboard can localize
            // process-control language (e.g. "Applications folder" on macOS vs
            // "wherever you unzipped it" on Windows). "windows" | "macos" | "linux".
            os                              = OsName(),
        };

        // Coarse OS bucket for UI string localization. Matches the three
        // platforms the companion ships builds for.
        private static string OsName() {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))     return "macos";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))   return "linux";
            return "other";
        }

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

        // POST /api/companion/restart  — companion-only. Returns 200, then
        // brings the process back one of two ways depending on platform:
        //
        //   macOS / Linux: exit 88 (the "respawn me" sentinel). The bash
        //     watchdog launcher inside the .app / install dir sees the code
        //     and relaunches the binary within ~1s.
        //   Windows: there is no external watchdog (the exe is a WinExe with
        //     an embedded icon, launched directly — no .cmd/.vbs). So we spawn
        //     a fresh detached copy of ourselves and exit 0. The new process
        //     bind-retries the port (see StartAsync) to ride out the brief
        //     window where this process is still releasing it.
        //
        // Either way the dashboard polls /api/health to detect the new process
        // and reloads.
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
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    log?.Info("Companion: restart requested via dashboard — self-respawning (Windows, no external watchdog).");
                    try { RespawnSelfWindows(); }
                    catch (Exception ex) { log?.Error($"Companion: self-respawn failed: {ex.Message}"); }
                    Environment.Exit(0);
                } else {
                    log?.Info("Companion: restart requested via dashboard — exiting code 88 for watchdog respawn.");
                    Environment.Exit(88);
                }
            });
        }

        // Launch a fresh, detached copy of this exe with the same arguments.
        // Windows-only: a child started with UseShellExecute=false is NOT tied
        // to the parent's lifetime (no job object), so it survives our Exit(0).
        // CreateNoWindow + the WinExe subsystem mean no console flashes up.
        private void RespawnSelfWindows() {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) {
                log?.Warn("Companion: cannot self-respawn — Environment.ProcessPath is null.");
                return;
            }
            var psi = new ProcessStartInfo {
                FileName        = exe,
                UseShellExecute = false,
                CreateNoWindow  = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
            };
            // Preserve the original launch args (e.g. "serve", "--config ...").
            foreach (var a in Environment.GetCommandLineArgs().Skip(1)) psi.ArgumentList.Add(a);
            Process.Start(psi);
            log?.Info($"Companion: spawned replacement process: {exe}");
        }

        // ── In-app update (companion-only) ───────────────────────────────────
        //
        // Two endpoints. The check is read-only + cached; the apply downloads the
        // matching release asset, verifies its checksum, swaps the install in
        // place, and brings the new version back via the SAME restart machinery
        // (exit 88 watchdog respawn on Unix tarball, detached self-relaunch on
        // Windows, detached re-install on macOS). Nothing auto-applies — the apply
        // only runs when the user clicks "Update now", which POSTs here.

        private UpdateChecker? _updateChecker;
        private UpdateChecker UpdateCheckerInstance => _updateChecker ??= new UpdateChecker();

        // GET /api/companion/update-check[?force=1] — compares this companion's
        // version against GitHub releases/latest and reports whether (and how) it
        // could self-update. Cached 24 h unless force=1. Never errors hard: a
        // network failure comes back as { error } with updateAvailable=false.
        private async Task HandleCompanionUpdateCheck(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            if (_companion == null) {
                await WriteJson(res, 404, new { error = "companion mode not active" });
                done?.Invoke(404, null);
                return;
            }
            var force = IsTruthy(req.QueryString["force"]);
            var info = await UpdateCheckerInstance.CheckAsync(_settings.PluginVersion ?? "", force, CancellationToken.None);
            await WriteJson(res, 200, new {
                current         = info.Current,
                latest          = info.Latest,
                updateAvailable = info.UpdateAvailable,
                canSelfUpdate   = info.CanSelfUpdate,
                strategy        = info.Strategy,
                releaseUrl      = info.ReleaseUrl,
                notes           = info.Notes,
                assetName       = info.AssetName,
                error           = info.Error,
            });
            done?.Invoke(200, $"update-check current={info.Current} latest={info.Latest} avail={info.UpdateAvailable} self={info.CanSelfUpdate}");
        }

        private static bool IsTruthy(string? v) =>
            v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);

        // POST /api/companion/update — apply the latest release to THIS install.
        // Re-checks (force) before doing anything, then acks 200 and runs the
        // download+swap detached so the response flushes before the process exits.
        // 409 if already current; 422 if this packaging can't self-update (AppImage
        // / .deb / non-writable) — the UI falls back to the release link.
        private async Task HandleCompanionUpdate(TcpHttpResponse res, Action<int, string> done) {
            if (_companion == null) {
                await WriteJson(res, 404, new { error = "companion mode not active" });
                done?.Invoke(404, null);
                return;
            }
            var info = await UpdateCheckerInstance.CheckAsync(_settings.PluginVersion ?? "", force: true, CancellationToken.None);
            if (info.Error != null) {
                await WriteJson(res, 502, new { error = "update check failed", detail = info.Error });
                done?.Invoke(502, "update check failed");
                return;
            }
            if (!info.UpdateAvailable) {
                await WriteJson(res, 409, new { error = "already up to date", current = info.Current });
                done?.Invoke(409, "already up to date");
                return;
            }
            if (!info.CanSelfUpdate || string.IsNullOrEmpty(info.AssetUrl)) {
                await WriteJson(res, 422, new { error = "self-update unsupported for this install", strategy = info.Strategy, releaseUrl = info.ReleaseUrl });
                done?.Invoke(422, $"self-update unsupported ({info.Strategy})");
                return;
            }
            await WriteJson(res, 200, new { ok = true, action = "update", latest = info.Latest, strategy = info.Strategy });
            done?.Invoke(200, $"update -> v{info.Latest} ({info.Strategy})");
            _ = Task.Run(async () => {
                try { await ApplyUpdateAsync(info); }
                catch (Exception ex) { log?.Error($"Companion: in-app update failed: {ex.Message}"); }
            });
        }

        // Download the asset, verify it, and hand off to the platform-specific
        // swap. Each swap ends in a process exit (or relaunch) so this never
        // returns on the happy path. Failures log and leave the running companion
        // untouched (the download sits in a temp dir; nothing was replaced yet).
        private async Task ApplyUpdateAsync(UpdateInfo info) {
            log?.Info($"Companion: starting in-app update {info.Current} -> {info.Latest} ({info.Strategy})");
            var tmp = Path.Combine(Path.GetTempPath(), "ns-companion-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            var assetPath = Path.Combine(tmp, info.AssetName);

            using (var http = NewUpdateHttpClient()) {
                log?.Info($"Companion: downloading {info.AssetName} from {info.AssetUrl}");
                await DownloadAsync(http, info.AssetUrl, assetPath);
                await VerifyChecksumAsync(http, assetPath, info.AssetName);
            }

            var strategy = Enum.TryParse<UpdateStrategy>(info.Strategy, out var s) ? s : UpdateStrategy.NotifyOnly;
            switch (strategy) {
                case UpdateStrategy.WindowsZipSwap:      ApplyWindowsUpdate(assetPath, tmp); break;
                case UpdateStrategy.LinuxTarballInPlace: ApplyLinuxUpdate(assetPath, tmp); break;
                case UpdateStrategy.MacAppReplace:       await ApplyMacUpdate(assetPath, tmp); break;
                default:
                    log?.Warn($"Companion: strategy {info.Strategy} can't self-apply; download left at {assetPath}.");
                    break;
            }
        }

        // Windows: a running .exe is write-locked, so we can't overwrite ourselves.
        // Extract the new exe, spawn a detached PowerShell helper that waits for our
        // PID to exit (unlocking the file), copies the new exe over the old one, and
        // relaunches with our original args — then we exit 0.
        private void ApplyWindowsUpdate(string zipPath, string tmp) {
            var extract = Path.Combine(tmp, "extract");
            var newExe = UpdateInstaller.ExtractZipFindExe(zipPath, extract);
            var targetExe = Environment.ProcessPath ?? throw new InvalidOperationException("ProcessPath is null");
            var launchArgs = string.Join(" ", Environment.GetCommandLineArgs().Skip(1).Select(QuoteArg));

            var helper = Path.Combine(tmp, "ns-update.ps1");
            File.WriteAllText(helper, WindowsUpdateHelperScript);

            var psi = new ProcessStartInfo {
                FileName         = "powershell.exe",
                UseShellExecute  = false,
                CreateNoWindow   = true,
                WorkingDirectory = tmp,
            };
            foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", helper,
                                      "-OldPid", Environment.ProcessId.ToString(),
                                      "-NewExe", newExe, "-TargetExe", targetExe, "-LaunchArgs", launchArgs }) {
                psi.ArgumentList.Add(a);
            }
            Process.Start(psi);
            log?.Info("Companion: update helper launched; exiting 0 so the .exe can be replaced and relaunched.");
            Environment.Exit(0);
        }

        private static string QuoteArg(string a) => a.Contains(' ') ? "\"" + a + "\"" : a;

        // Linux (user-scoped tarball install only — AppImage/.deb are NotifyOnly):
        // swap the -bin (and launcher) in the install dir, then exit 88 so the bash
        // watchdog relaunches the NEW bytes.
        private void ApplyLinuxUpdate(string tarPath, string tmp) {
            var extract = Path.Combine(tmp, "extract");
            var (newBin, newLauncher) = UpdateInstaller.ExtractTarGzFindBin(tarPath, extract);

            var installDir = Path.GetDirectoryName(Environment.ProcessPath)
                             ?? throw new InvalidOperationException("ProcessPath is null");
            var destBin      = Path.Combine(installDir, "NightSummaryCompanion-bin");
            var destLauncher = Path.Combine(installDir, "NightSummaryCompanion");
            SwapExecutableInPlace(newBin, destBin, installDir);
            if (newLauncher != null && File.Exists(newLauncher)) SwapExecutableInPlace(newLauncher, destLauncher, installDir);

            log?.Info("Companion: Linux binary replaced in place; exiting 88 for watchdog respawn of the new version.");
            Environment.Exit(88);
        }

        // Stages `source` next to `dest` (same directory => same filesystem, so the
        // final move is a real rename(), not a cross-device copy+delete) and swaps it
        // in with File.Move. This is safe to run on `dest` while it's the currently
        // executing binary: Linux allows unlinking/renaming over an open/mapped file —
        // our own already-running process keeps its old inode mapped until it exits,
        // it's only the *directory entry* that flips. File.Copy(overwrite: true) is
        // NOT safe here — it opens the destination for writing (truncate), which the
        // kernel rejects with ETXTBSY while we're still executing out of it, so the
        // swap would silently fail every time (caught upstream, logged, old version
        // keeps running — no corruption, but the update never actually applies).
        private static void SwapExecutableInPlace(string source, string dest, string sameDirAs) {
            var staged = Path.Combine(sameDirAs, Path.GetFileName(dest) + ".new-" + Guid.NewGuid().ToString("N"));
            File.Copy(source, staged, overwrite: true);
            ChmodExec(staged);
            File.Move(staged, dest, overwrite: true);
        }

        private static void ChmodExec(string path) {
            if (!File.Exists(path)) return;
            try {
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead  | UnixFileMode.UserWrite  | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            } catch { /* best-effort */ }
        }

        // macOS: a running .app can't be replaced from inside itself without breaking
        // the ad-hoc signature, so hand off to the same install-companion-mac.sh the
        // release ships (quits us, replaces /Applications/.app, relaunches). We pass
        // our already-downloaded .dmg via NSC_DMG so it skips its own curl, then exit
        // 0 (watchdog stops) and let the detached installer take over.
        private async Task ApplyMacUpdate(string dmgPath, string tmp) {
            var scriptUrl  = UpdateChecker.DownloadUrl("install-companion-mac.sh");
            var scriptPath = Path.Combine(tmp, "install-companion-mac.sh");
            using (var http = NewUpdateHttpClient()) {
                await DownloadAsync(http, scriptUrl, scriptPath);
            }
            var psi = new ProcessStartInfo {
                FileName         = "/bin/sh",
                UseShellExecute  = false,
                CreateNoWindow   = true,
                WorkingDirectory = tmp,
            };
            psi.ArgumentList.Add(scriptPath);
            psi.Environment["NSC_DMG"] = dmgPath;   // reuse our verified download; installer skips its curl
            Process.Start(psi);
            log?.Info("Companion: launched detached mac installer (NSC_DMG set); exiting 0 so it can replace the .app.");
            await Task.Delay(300);                   // let the detached sh start before we vanish
            Environment.Exit(0);
        }

        private static HttpClient NewUpdateHttpClient() {
            var h = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };   // assets are ~70 MB
            h.DefaultRequestHeaders.UserAgent.ParseAdd("NightSummaryCompanion-Updater");
            return h;
        }

        private static async Task DownloadAsync(HttpClient http, string url, string dest) {
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(dest);
            await resp.Content.CopyToAsync(fs);
        }

        // Verify the download against the release's checksums.txt. If the release
        // doesn't publish one (older releases) or the asset isn't listed, log and
        // skip — HTTPS-from-GitHub is the fallback trust anchor. A real mismatch
        // throws, aborting the swap before anything is replaced.
        private async Task VerifyChecksumAsync(HttpClient http, string filePath, string assetName) {
            string? text = null;
            try {
                text = await http.GetStringAsync(UpdateChecker.DownloadUrl("checksums.txt"));
            } catch {
                // No checksums.txt on the release (older builds) — fall through;
                // VerifyChecksum treats null text as a graceful skip.
            }
            if (!UpdateInstaller.VerifyChecksum(filePath, text, assetName, out var skipped, out var detail)) {
                throw new InvalidOperationException(detail);
            }
            if (skipped) log?.Warn($"Companion: integrity check skipped ({detail}); HTTPS download trusted.");
            else log?.Info($"Companion: {assetName} checksum verified.");
        }

        // PowerShell helper that swaps the Windows .exe once we've exited. Pure
        // ASCII (project rule). Waits up to 30 s for the old PID, retries the copy
        // past a transient AV/lock, then relaunches with the original args.
        private const string WindowsUpdateHelperScript = @"
param(
    [int]$OldPid,
    [string]$NewExe,
    [string]$TargetExe,
    [string]$LaunchArgs = ''
)
$ErrorActionPreference = 'SilentlyContinue'
for ($i = 0; $i -lt 60; $i++) {
    if (-not (Get-Process -Id $OldPid -ErrorAction SilentlyContinue)) { break }
    Start-Sleep -Milliseconds 500
}
Start-Sleep -Milliseconds 500
$copied = $false
for ($i = 0; $i -lt 20; $i++) {
    try {
        Copy-Item -LiteralPath $NewExe -Destination $TargetExe -Force -ErrorAction Stop
        $copied = $true
        break
    } catch {
        Start-Sleep -Milliseconds 500
    }
}
if (-not $copied) { exit 1 }
if ($LaunchArgs -and $LaunchArgs.Trim().Length -gt 0) {
    Start-Process -FilePath $TargetExe -ArgumentList $LaunchArgs
} else {
    Start-Process -FilePath $TargetExe
}
";

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
            // Live sync progress (null when idle) so the wizard/dashboard can show
            // a moving phase + byte indicator instead of a silent spinner.
            progress              = s.Progress == null ? null : new {
                phase          = s.Progress.Phase,
                step           = s.Progress.Step,
                totalSteps     = s.Progress.TotalSteps,
                bytesThisPhase = s.Progress.BytesThisPhase,
                detail         = s.Progress.Detail,
            },
        };
    }
}
