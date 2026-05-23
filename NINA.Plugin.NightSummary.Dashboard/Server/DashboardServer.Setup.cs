using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Server {

    // Setup wizard endpoints + page. Companion-only — every route 404s when
    // _companion is null (primary mode never needs a setup flow, and the wizard
    // would have no implementation behind it to call).
    public partial class DashboardServer {

        // GET /setup — the wizard HTML page. Embedded resource served verbatim.
        // No auth gate; setup is the only flow available before pairing.
        private async Task HandleSetupHtml(TcpHttpResponse res, Action<int, string> done) {
            if (_companion == null) {
                await WriteJson(res, 404, new { error = "setup wizard only runs in companion mode" });
                done?.Invoke(404, null);
                return;
            }
            var bytes = await _webAssets.ReadAsync("setup.html");
            if (bytes == null) {
                await WriteJson(res, 500, new { error = "setup.html asset missing" });
                done?.Invoke(500, "setup.html absent");
                return;
            }
            await WriteHtml(res, 200, System.Text.Encoding.UTF8.GetString(bytes));
            done?.Invoke(200, "setup html");
        }

        // GET /setup.js / /setup.css — embedded sibling assets. Same hot-reload
        // story as dashboard.js (DiskWebAssets in dev, EmbeddedWebAssets in prod).
        private async Task HandleSetupAsset(TcpHttpResponse res, string asset, string contentType, Action<int, string> done) {
            if (_companion == null) {
                await WriteJson(res, 404, new { error = "setup wizard only runs in companion mode" });
                done?.Invoke(404, null);
                return;
            }
            await HandleStaticAsset(res, asset, contentType, done);
        }

        // GET /dashboard.css and similar standalone assets. Companion-mode
        // agnostic — works in both primary and companion servers so the
        // wizard's <link> tag resolves in companion mode and any future
        // shared standalone asset works in either.
        private async Task HandleStaticAsset(TcpHttpResponse res, string asset, string contentType, Action<int, string> done) {
            var bytes = await _webAssets.ReadAsync(asset);
            if (bytes == null) {
                await WriteJson(res, 404, new { error = $"{asset} not found" });
                done?.Invoke(404, $"{asset} absent");
                return;
            }
            res.ContentType = contentType;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            done?.Invoke(200, asset);
        }

        // GET /api/setup/probe?host=X&port=Y — server-side fetch of the
        // primary's /api/companion/info. Going through the companion avoids
        // browser CORS (the wizard's origin is the companion, not the primary)
        // and centralizes timeout + parsing logic.
        private async Task HandleSetupProbe(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            if (_companion == null) {
                await WriteJson(res, 404, new { error = "setup wizard only runs in companion mode" });
                done?.Invoke(404, null);
                return;
            }
            var host = req.QueryString["host"] ?? "";
            int.TryParse(req.QueryString["port"] ?? "", out var port);
            var r = await _companion.ProbePrimaryAsync(host, port);
            await WriteJson(res, 200, new {
                ok                   = r.Ok,
                nsVersion            = r.NsVersion,
                ninaVersion          = r.NinaVersion,
                hasNs                = r.HasNs,
                pairedCount          = r.PairedCount,
                minCompanionVersion  = r.MinCompanionVersion,
                error                = r.Error,
            });
            done?.Invoke(200, r.Ok ? $"probe ok: {r.NsVersion}" : $"probe failed: {r.Error}");
        }

        // POST /api/setup/claim  body { host, port, token, companionName }
        // Forwards to the primary's /api/companion/pair; on success the
        // controller persists the token to companion.json and reloads the
        // SyncEngine. The wizard advances to step 4 after a 200 here.
        private async Task HandleSetupClaim(TcpHttpRequest req, TcpHttpResponse res, Action<int, string> done) {
            if (_companion == null) {
                await WriteJson(res, 404, new { error = "setup wizard only runs in companion mode" });
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
                var token = GetStr(root, "token", "");
                var name  = GetStr(root, "companionName", "");

                var r = await _companion.ClaimPairingAsync(host, port, token, name);
                // Wire-format mirrors the primary's responses (errorCode,
                // companionName for already_paired). Status is always 200 so
                // the wizard's fetch promise resolves and inspects the body.
                await WriteJson(res, 200, new {
                    ok                          = r.Ok,
                    companionId                 = r.CompanionId,
                    ninaVersion                 = r.NinaVersion,
                    nsVersion                   = r.NsVersion,
                    errorCode                   = r.ErrorCode,
                    error                       = r.Error,
                    alreadyPairedCompanionName  = r.AlreadyPairedCompanionName,
                });
                done?.Invoke(200, r.Ok ? "pair ok" : $"pair failed: {r.ErrorCode ?? r.Error}");
            } catch (JsonException ex) {
                await WriteJson(res, 400, new { ok = false, error = "invalid json: " + ex.Message });
                done?.Invoke(400, ex.Message);
            } catch (Exception ex) {
                log?.Error("Setup claim failed", ex);
                await WriteJson(res, 500, new { ok = false, error = ex.Message });
                done?.Invoke(500, ex.Message);
            }
        }
    }
}
