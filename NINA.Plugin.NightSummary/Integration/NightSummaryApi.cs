using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Server;
using NINA.Plugin.NightSummary.Session;

namespace NINA.Plugin.NightSummary.Integration {

    /// <summary>
    /// Stable, public integration surface for other in-process NINA plugins
    /// (Touch 'N' Stars) to bind to by reflection, instead of reaching into
    /// internal types like SessionDatabase / SettingsManager whose names change
    /// between releases.
    ///
    /// CONTRACT STABILITY: the fully-qualified type name
    /// (NINA.Plugin.NightSummary.Integration.NightSummaryApi) and every public
    /// method signature here are a frozen external contract. Do not rename, move,
    /// or change signatures — add new methods and bump ApiVersion instead.
    ///
    /// Every method returns a JSON string using the { Success, Response, Error }
    /// envelope (PascalCase) so consumers need zero type coupling: invoke by name,
    /// parse the string. The plugin wires dependencies at startup via Wire();
    /// before that (or after Teardown) methods return a "not loaded" envelope
    /// rather than throwing.
    /// </summary>
    public static class NightSummaryApi {

        private static readonly object _lock = new();
        private static SessionService _sessionService;
        private static NinaDashboardPaths _paths;
        private static NinaSessionMaintenance _maintenance;
        private static Func<List<string>> _filterNames;

        // Secret fields masked on GetSettings and treated write-only on
        // UpdateSettings. Mirrors SettingsManager's encrypted set.
        private static readonly string[] SecretFields =
            { "SmtpPassword", "DiscordWebhookUrl", "PushoverAppToken", "PushoverUserKey", "DashboardApiKey" };

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
            WriteIndented = false
        };

        /// <summary>Called by NightSummaryPlugin at startup to supply live dependencies.
        /// Internal: not part of the external contract (takes internal types).</summary>
        internal static void Wire(SessionService sessionService, NinaDashboardPaths paths, Func<List<string>> filterNames) {
            lock (_lock) {
                _sessionService = sessionService;
                _paths          = paths;
                _filterNames    = filterNames;
                _maintenance    = new NinaSessionMaintenance(sessionService, paths.DatabasePath, paths);
            }
        }

        internal static void Unwire() {
            lock (_lock) {
                _sessionService = null;
                _paths          = null;
                _maintenance    = null;
                _filterNames    = null;
            }
        }

        /// <summary>Contract version. Bump when adding methods so consumers can feature-gate.</summary>
        public static string ApiVersion() => "1.0";

        public static string Status() {
            var installed = _paths != null;
            int sessionCount = 0;
            if (installed) {
                try { sessionCount = new SessionDatabase(_paths.DatabasePath).GetRecentSessions(int.MaxValue).Count; } catch { }
            }
            return Ok(new {
                Installed = installed,
                Version   = typeof(NightSummaryApi).Assembly.GetName().Version?.ToString(),
                ApiVersion = ApiVersion(),
                SessionCount = sessionCount
            });
        }

        /// <summary>Recent sessions, newest first (SessionRecord shape).</summary>
        public static string Sessions(int limit = 50) {
            var paths = _paths;
            if (paths == null) return NotLoaded();
            try {
                var db = new SessionDatabase(paths.DatabasePath);
                var sessions = db.GetRecentSessions(limit <= 0 ? 50 : limit);
                return Ok(sessions);
            } catch (Exception ex) { return Err(ex); }
        }

        /// <summary>
        /// One session's full data bundle: the session record, its images, events,
        /// timing events, and per-target session history. Consumers compute display
        /// stats from Images (LIGHT / accepted) as they see fit.
        /// </summary>
        public static string Session(string sessionId) {
            var paths = _paths;
            if (paths == null) return NotLoaded();
            try {
                var db = new SessionDatabase(paths.DatabasePath);
                var session = db.GetSession(sessionId);
                if (session == null) return Err("Session not found");

                var images = db.GetImagesForSession(sessionId);
                var history = new Dictionary<string, object>();
                foreach (var target in images.Select(i => i.TargetName).Where(n => !string.IsNullOrEmpty(n)).Distinct()) {
                    history[target] = db.GetSessionHistoryForTarget(target, sessionId);
                }
                return Ok(new {
                    Session        = session,
                    Images         = images,
                    Events         = db.GetEventsForSession(sessionId),
                    TimingEvents   = db.GetTimingEventsForSession(sessionId),
                    SessionHistory = history
                });
            } catch (Exception ex) { return Err(ex); }
        }

        /// <summary>
        /// The session's self-contained report HTML (thumbnails/charts embedded), or
        /// a not-found envelope. Read from the always-written reports dir, not the
        /// user-configurable "save report locally" path.
        /// </summary>
        public static string ReportHtml(string sessionId) {
            var paths = _paths;
            if (paths == null) return NotLoaded();
            if (!IsSafeSessionId(sessionId)) return Err("Invalid session id");
            try {
                var path = paths.ReportHtmlPath(sessionId);
                if (!File.Exists(path)) return Err("Report not found");
                return Ok(File.ReadAllText(path));
            } catch (Exception ex) { return Err(ex); }
        }

        /// <summary>Absolute path to the session's report HTML (may not exist), or not-found.</summary>
        public static string ReportPath(string sessionId) {
            var paths = _paths;
            if (paths == null) return NotLoaded();
            if (!IsSafeSessionId(sessionId)) return Err("Invalid session id");
            var path = paths.ReportHtmlPath(sessionId);
            return Ok(new { Path = path, Exists = File.Exists(path) });
        }

        /// <summary>
        /// Current settings with secret fields masked: each secret is removed and a
        /// "<field>Set" boolean added. Includes "_filterNames" from the active NINA
        /// profile. Never returns credential values.
        /// </summary>
        public static string GetSettings() {
            try {
                var node = MaskSettings(SettingsManager.Instance.Current, _filterNames?.Invoke());
                return Envelope(true, node, null);
            } catch (Exception ex) { return Err(ex); }
        }

        // Serializes settings with secret fields masked ("<field>" removed, "<field>Set"
        // boolean added) and "_filterNames" appended. Internal so tests can exercise it
        // without the production SettingsManager singleton.
        internal static JsonObject MaskSettings(NightSummarySettings settings, IEnumerable<string> filterNames) {
            var node = JsonSerializer.SerializeToNode(settings, JsonOpts) as JsonObject
                       ?? new JsonObject();
            foreach (var f in SecretFields) {
                var isSet = node[f] is JsonValue v && !string.IsNullOrEmpty(v.GetValue<string>());
                node.Remove(f);
                node[f + "Set"] = isSet;
            }
            var filters = new JsonArray();
            foreach (var name in (filterNames ?? Enumerable.Empty<string>())) filters.Add(name);
            node["_filterNames"] = filters;
            return node;
        }

        /// <summary>
        /// Applies a JSON patch to settings. Secret fields are write-only: a blank or
        /// absent secret keeps the current value; a non-blank secret replaces it.
        /// Writes through SettingsManager.Save so the value is encrypted at rest.
        /// </summary>
        public static string UpdateSettings(string patchJson) {
            if (string.IsNullOrWhiteSpace(patchJson)) return Err("Empty patch");
            try {
                ApplyPatch(SettingsManager.Instance.Current, patchJson);
                SettingsManager.Instance.Save();
                return Ok("Settings saved");
            } catch (Exception ex) { return Err(ex); }
        }

        // Applies a JSON patch with write-only secret semantics: a blank/absent secret
        // keeps the current value, a non-blank secret replaces it. Internal so tests can
        // exercise it against a plain NightSummarySettings.
        internal static void ApplyPatch(NightSummarySettings settings, string patchJson) {
            var props = settings.GetType().GetProperties();
            using var doc = JsonDocument.Parse(patchJson);
            foreach (var member in doc.RootElement.EnumerateObject()) {
                if (SecretFields.Contains(member.Name)) {
                    var s = member.Value.ValueKind == JsonValueKind.String ? member.Value.GetString() : null;
                    if (string.IsNullOrEmpty(s)) continue; // blank secret → keep current
                }
                var prop = props.FirstOrDefault(p => p.Name == member.Name && p.CanWrite);
                if (prop == null) continue;
                var value = ConvertJsonValue(member.Value, prop.PropertyType);
                if (value != null || !prop.PropertyType.IsValueType) prop.SetValue(settings, value);
            }
        }

        // Exposed for tests: GUID-shape id validation used by report endpoints.
        internal static bool IsSafeSessionIdForTest(string id) => IsSafeSessionId(id);

        /// <summary>
        /// Re-fires the configured delivery channels (email / Discord / Pushover /
        /// dashboard) for a historical session.
        /// </summary>
        public static string Resend(string sessionId) {
            var m = _maintenance; var paths = _paths;
            if (m == null || paths == null) return NotLoaded();
            try {
                if (new SessionDatabase(paths.DatabasePath).GetSession(sessionId) == null) return Err("Session not found");
                Task.Run(() => m.ResendAsync(sessionId)).GetAwaiter().GetResult();
                return Ok("Report sent");
            } catch (Exception ex) { return Err(ex); }
        }

        /// <summary>
        /// Deletes a session and all its on-disk artifacts (report HTML, settings
        /// sidecar, livestack masters, thumbnails). Returns Success=false with a
        /// "Session not found" error when the id does not exist.
        /// </summary>
        public static string DeleteSession(string sessionId) {
            var m = _maintenance;
            if (m == null) return NotLoaded();
            try {
                var deleted = Task.Run(() => m.DeleteAsync(sessionId)).GetAwaiter().GetResult();
                return deleted ? Ok("Session deleted") : Err("Session not found");
            } catch (Exception ex) { return Err(ex); }
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static string Ok(object response) => Envelope(true, response, null);
        private static string NotLoaded() => Envelope(false, null, "Night Summary plugin not loaded");
        private static string Err(string message) => Envelope(false, null, message);
        private static string Err(Exception ex) {
            Logger.Warning($"NightSummary: NightSummaryApi call failed: {ex.Message}");
            return Envelope(false, null, ex.Message);
        }

        private static string Envelope(bool success, object response, string error) =>
            JsonSerializer.Serialize(new { Success = success, Response = response, Error = error ?? "" }, JsonOpts);

        private static object ConvertJsonValue(JsonElement el, Type target) {
            var t = Nullable.GetUnderlyingType(target) ?? target;
            if (t == typeof(string)) return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
            if (t == typeof(bool))   return el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False ? el.GetBoolean() : Convert.ToBoolean(el.ToString());
            if (t == typeof(int))    return el.TryGetInt32(out var i) ? i : Convert.ToInt32(el.ToString());
            if (t == typeof(double)) return el.TryGetDouble(out var d) ? d : Convert.ToDouble(el.ToString());
            if (t.IsEnum)            return Enum.Parse(t, el.ToString());
            return Convert.ChangeType(el.ToString(), t);
        }

        // Session ids are GUIDs; reject anything with path separators or "..".
        private static bool IsSafeSessionId(string id) {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
            foreach (var c in id) {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-';
                if (!ok) return false;
            }
            return true;
        }
    }
}
