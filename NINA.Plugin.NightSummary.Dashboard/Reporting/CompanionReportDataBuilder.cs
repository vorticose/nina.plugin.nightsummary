using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Session;

namespace NINA.Plugin.NightSummary.Reporting;

// Cross-platform ReportData builder for the companion app. Lives in Dashboard
// (net8.0) so it works on win/mac/linux without WPF or System.Data.SQLite.
// Plugin-side SessionService keeps its own richer BuildReportDataAsync that
// can also re-parse logs, fetch TS progress, etc. — the companion never has
// access to those on a non-imaging machine.
public sealed class CompanionReportDataBuilder {

    private readonly SqliteSessionReader _reader;
    private readonly IPluginSettings _settings;
    private readonly IDashboardLogger _log;
    private readonly IDashboardPaths _paths;
    private readonly ITargetSchedulerDatabase _tsDb;

    public CompanionReportDataBuilder(
        SqliteSessionReader reader,
        IPluginSettings settings,
        IDashboardLogger log,
        IDashboardPaths paths,
        ITargetSchedulerDatabase? tsDb = null) {

        _reader   = reader;
        _settings = settings;
        _log      = log;
        _paths    = paths;
        _tsDb     = tsDb ?? new NINA.Plugin.NightSummary.Dashboard.Adapters.NullTargetSchedulerDatabase();
    }

    // Builds a fully populated ReportData from synced data. Returns null when
    // the session is not present (e.g. companion sync hasn't run yet).
    public ReportData? Build(string sessionId) {
        var session = _reader.GetSession(sessionId);
        if (session == null) {
            _log.Warn($"CompanionReportDataBuilder: session not found — {sessionId}");
            return null;
        }

        var images        = _reader.GetImagesForSession(session.SessionId);
        var events        = _reader.GetEventsForSession(session.SessionId);
        var timingEvents  = _reader.GetTimingEventsForSession(session.SessionId);
        var cumulative    = _reader.GetCumulativeIntegrationByTarget(session.SessionId);
        var history       = BuildHistory(images, session.SessionId);
        var (fovW, fovH)  = ComputeCameraFov(session);
        var liveStack     = LoadLiveStackMastersForSession(session);
        var (lat, lon)    = ReadObserverCoordsFromSidecar(session.SessionId);

        // TS progress per (project, target, filter) for any target imaged in
        // this session. profileId is null on the companion path — the synced
        // TS DB may carry multiple profiles; passing null returns matches
        // across all of them (cheap, since nameSet filters down).
        var uniqueTargets = images.Select(i => i.TargetName)
                                  .Where(n => !string.IsNullOrEmpty(n))
                                  .Distinct()
                                  .ToList();
        var tsData = _tsDb.IsAvailable && uniqueTargets.Count > 0
            ? _tsDb.GetProgressForTargets(uniqueTargets, null)
            : new List<TsTargetData>();

        return new ReportData {
            Session                      = session,
            Images                       = images,
            Events                       = events,
            TsData                       = tsData,
            CumulativeIntegrationSeconds = cumulative,
            SessionHistory               = history,
            CameraFovWidthDeg            = fovW,
            CameraFovHeightDeg           = fovH,
            ObserverLatitude             = lat,
            ObserverLongitude            = lon,
            ActiveProfileId              = null,
            SkippedExposures             = session.SkippedExposures,
            Equipment                    = BuildEquipment(session, _settings.Current),
            TimingEvents                 = timingEvents,
            LiveStackImages              = liveStack,
        };
    }

    // Observer coords aren't in the SQLite session row — they live on the
    // session sidecar JSON the primary writes (SessionService +
    // DashboardServer.SaveSessionSettings). Sidecar is included in the synced
    // reports tree. If absent or malformed, fall back to (0,0) which the
    // altitude chart already treats as "unset → hide curves".
    private (double lat, double lon) ReadObserverCoordsFromSidecar(string sessionId) {
        try {
            var path = _paths.ReportSettingsPath(sessionId);
            if (!File.Exists(path)) return (0, 0);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            double lat = 0, lon = 0;
            if (doc.RootElement.TryGetProperty("observerLatitude",  out var elLat)
                && elLat.ValueKind == JsonValueKind.Number) lat = elLat.GetDouble();
            if (doc.RootElement.TryGetProperty("observerLongitude", out var elLon)
                && elLon.ValueKind == JsonValueKind.Number) lon = elLon.GetDouble();
            return (lat, lon);
        } catch (Exception ex) {
            _log.Warn($"CompanionReportDataBuilder: failed to read observer coords from sidecar — {ex.Message}");
            return (0, 0);
        }
    }

    private Dictionary<string, List<TargetSessionHistory>> BuildHistory(List<ImageRecord> images, string sessionId) {
        var result = new Dictionary<string, List<TargetSessionHistory>>(StringComparer.OrdinalIgnoreCase);
        foreach (var targetName in images.Select(i => i.TargetName).Where(n => !string.IsNullOrEmpty(n)).Distinct()) {
            result[targetName] = _reader.GetSessionHistoryForTarget(targetName, sessionId);
        }
        return result;
    }

    // Session-only camera FOV. Plugin's path falls back to NINA profile values
    // when the session row is incomplete; companion never has live profile data
    // so the fallback degrades to (1, 1) which the report treats as "unknown".
    private (double widthDeg, double heightDeg) ComputeCameraFov(SessionRecord session) {
        if (session.CamXSize > 0 && session.CamYSize > 0
            && session.PixelSizeMicrons > 0 && session.FocalLengthMm > 0) {
            var ps = 206.265 * session.PixelSizeMicrons / session.FocalLengthMm;
            return (ps * session.CamXSize / 3600.0, ps * session.CamYSize / 3600.0);
        }
        return (1.0, 1.0);
    }

    // Equipment dictionary built from the synced session row, with the same
    // override + visible-fields filtering the plugin applies on primary
    // (SessionService.BuildEquipmentDictionary). Reading from settings here
    // is required for parity — the user's renamed Telescope ("WO Cat 91"
    // overrides "ZWO Whatever") and hidden fields (Dome/Weather) need to
    // travel through the regen unchanged.
    private static Dictionary<string, string> BuildEquipment(SessionRecord session, NightSummarySettings s) {
        var overrides = ParseEquipmentOverrides(s.EquipmentOverrides);
        var visible   = new HashSet<string>(
            (s.EquipmentVisibleFields ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);
        // If the setting is empty (e.g. fresh install never touched), fall
        // back to showing everything — matches the plugin's behavior when
        // the visible-fields preference hasn't been configured.
        bool filterByVisible = visible.Count > 0;

        var equipment = new Dictionary<string, string>();
        void Add(string key, string? dbValue) {
            if (filterByVisible && !visible.Contains(key)) return;
            var value = overrides.TryGetValue(key, out var ov) && !string.IsNullOrWhiteSpace(ov)
                        ? ov : dbValue;
            if (!string.IsNullOrWhiteSpace(value)) equipment[key] = value;
        }

        Add("Camera",         session.CameraName);
        Add("Telescope",      session.TelescopeName);
        Add("Mount",          session.MountName);
        Add("Filter Wheel",   session.FilterWheelName);
        Add("Focuser",        session.FocuserName);
        Add("Rotator",        session.RotatorName);
        Add("Guider",         session.GuiderName);
        Add("Dome",           session.DomeName);
        Add("Flat Panel",     session.FlatDeviceName);
        Add("Safety Monitor", session.SafetyMonitorName);
        Add("Weather",        session.WeatherName);
        Add("Switch",         session.SwitchName);
        return equipment;
    }

    // Mirrors SessionService.ParseEquipmentOverrides. Format: "Key1:Value1,Key2:Value2"
    // (the raw form stored in NightSummarySettings.EquipmentOverrides).
    private static Dictionary<string, string> ParseEquipmentOverrides(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? new Dictionary<string, string>()
            : raw.Split(',')
                .Select(p => p.Split(':', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);

    // Reads livestack masters via IDashboardPaths.LivestackManifestPath /
    // LivestackImagePath, which now resolves to reports/livestack/{sessionId}/
    // — matching NinaReportRegenerator.SaveLiveStackMasters' actual on-disk
    // layout. The export zip from primary carries files under that prefix
    // verbatim.
    //
    // Masters are 2000px @ q90 (~500 KB each). Embedding them directly
    // inflates HTML ~4×; primary rescales to 760px @ q75 via WPF's
    // JpegBitmapEncoder (Windows-only). JpegRescaler does the same with
    // SkiaSharp so the companion's output matches primary's size on Mac/Linux.
    private List<LiveStackImage> LoadLiveStackMastersForSession(SessionRecord session) {
        try {
            var manifestPath = _paths.LivestackManifestPath(session.SessionId);
            if (!File.Exists(manifestPath)) return new List<LiveStackImage>();

            var json = File.ReadAllText(manifestPath);
            var entries = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json);
            if (entries == null) return new List<LiveStackImage>();

            var images = new List<LiveStackImage>();
            foreach (var entry in entries) {
                if (!entry.TryGetValue("file", out var fileEl)) continue;
                var fileName = fileEl.GetString();
                if (string.IsNullOrEmpty(fileName)) continue;
                var jpgPath = _paths.LivestackImagePath(session.SessionId, fileName);
                if (!File.Exists(jpgPath)) continue;

                var masterData = File.ReadAllBytes(jpgPath);
                var embedData  = JpegRescaler.ScaleForReport(masterData);
                images.Add(new LiveStackImage {
                    Target       = entry.TryGetValue("target", out var t) ? (t.GetString() ?? "") : "",
                    Filter       = entry.TryGetValue("filter", out var f) ? (f.GetString() ?? "") : "",
                    IsMonochrome = entry.TryGetValue("isMonochrome", out var m) && m.GetBoolean(),
                    JpegData     = embedData,
                    MasterJpegData  = masterData,
                    StackCount      = entry.TryGetValue("stackCount", out var sc) ? sc.GetInt32() : 0,
                    RedStackCount   = TryNullableInt(entry, "redStackCount"),
                    GreenStackCount = TryNullableInt(entry, "greenStackCount"),
                    BlueStackCount  = TryNullableInt(entry, "blueStackCount"),
                });
            }
            return images;
        } catch (Exception ex) {
            _log.Warn($"CompanionReportDataBuilder: failed to load live stack masters — {ex.Message}");
            return new List<LiveStackImage>();
        }
    }

    private static int? TryNullableInt(Dictionary<string, JsonElement> entry, string key) {
        if (!entry.TryGetValue(key, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Null) return null;
        if (el.ValueKind == JsonValueKind.Number) return el.GetInt32();
        return null;
    }
}
