# Dashboard Interface Design

**Status:** Proposed, awaiting review
**Branch:** `feature/dashboard-unification`
**Created:** 2026-04-24

Companion to [DASHBOARD_UNIFICATION_PLAN.md](DASHBOARD_UNIFICATION_PLAN.md). This captures the Phase 2 interface design (the one review gate before ~3 days of mechanical provider/server porting).

---

## Dependency audit summary

`DashboardServer.cs` touches 10 distinct external concerns:

| Dep | Call sites | Notes |
|---|---|---|
| `SessionDatabase` (plugin) | 13 methods | Sessions, images, events, targets |
| `SessionService` (plugin) | 2 methods | Report regeneration |
| NINA `Logger` | 40+ | Logging |
| `System.Data.SQLite` (direct) | 9 queries | Dashboard cache: altitude charts + metadata blobs |
| `System.Data.SQLite` (session DB direct) | 1 query | Latest position angle per target (`SELECT ... FROM Images`) |
| Filesystem | 25+ | Reports, livestack, HiPS cache, settings sidecars |
| `SettingsManager` | 6 | Plugin settings + snapshot/restore |
| `TargetSchedulerDatabase` | 7 | Projects + API settings |
| `HttpClient` (HiPS2FITS) | 1 | CDS mosaic thumb fetch |
| `HttpClient` (Tonight API) | 2 | TS /profiles, /preview |
| Embedded resources | 3 | dashboard.html/css/js |

**Structural takeaway:** raw HTTP goes inside the server untouched. Everything else gets an interface.

---

## Proposed interfaces (classlib)

Target: `NINA.Plugin.NightSummary.Dashboard.csproj`, TFM `net8.0`, no NINA/WPF/SQLite deps. All DTOs are plain POCOs.

### IDashboardDataSource — primary data access

```csharp
public interface IDashboardDataSource {
    // --- Sessions ---
    Task<IReadOnlyList<SessionDto>> GetAllSessionsAsync(CancellationToken ct = default);
    Task<SessionDto?> GetSessionAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<ImageDto>> GetImagesAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<EventDto>> GetEventsAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<TimingEventDto>> GetTimingEventsAsync(string sessionId, CancellationToken ct = default);

    // --- Targets ---
    Task<IReadOnlyList<TargetDetailDto>> GetTargetDetailsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SessionDto>> GetSessionsForTargetAsync(string targetName, CancellationToken ct = default);
    // Replaces the one direct-SQLite query inside the server
    Task<IReadOnlyDictionary<string, double>> GetLatestPositionAnglesAsync(CancellationToken ct = default);

    // --- Target Scheduler ---
    Task<bool> IsTargetSchedulerAvailableAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TSProjectDto>> GetTSProjectsAsync(CancellationToken ct = default);
    Task<TSApiSettingsDto?> GetTSApiSettingsAsync(CancellationToken ct = default);

    // --- Report artifacts (disk-backed) ---
    Task<string?> LoadReportHtmlAsync(string sessionId, CancellationToken ct = default);
    Task<byte[]?> LoadLivestackImageAsync(string sessionId, string filename, CancellationToken ct = default);
    Task<string?> LoadLivestackManifestAsync(string sessionId, CancellationToken ct = default);

    // --- Report regeneration (optional capability) ---
    bool SupportsReportRegeneration { get; }
    Task<ReportRegenerationResultDto> RegenerateReportAsync(
        string sessionId,
        SettingsOverridesDto? overrides,
        CancellationToken ct = default);
}
```

Fixture impl returns `SupportsReportRegeneration = false`; regen endpoint returns 501 in dev. Prod impl wraps `SessionService`.

### IDashboardCache — altitude charts + metadata blobs

```csharp
public interface IDashboardCache {
    Task<IReadOnlyDictionary<string, string>> LoadAllAltitudeChartsAsync(CancellationToken ct = default);
    Task<string?> GetAltitudeChartAsync(string sessionId, CancellationToken ct = default);
    Task SetAltitudeChartAsync(string sessionId, string chartJson, CancellationToken ct = default);
    Task InvalidateAltitudeChartAsync(string sessionId, CancellationToken ct = default);

    // Metadata store (status overrides, TS links, assignments, exclusions — serialized JSON blobs keyed by known names)
    Task<string?> GetMetadataAsync(string key, CancellationToken ct = default);
    Task SetMetadataAsync(string key, string value, CancellationToken ct = default);
}
```

- Prod impl: SQLite (`nightsummary-dashboard-cache.sqlite`, current schema, zero migration)
- Dev impl: JSON file (`data/dashboard-cache.json`) with an in-memory layer. Simpler than running SQLite.

### IPluginSettings — user-facing plugin settings + override scope

```csharp
public interface IPluginSettings {
    Task<DashboardSettingsDto> GetAsync(CancellationToken ct = default);
    Task UpdateAsync(DashboardSettingsDto settings, CancellationToken ct = default);

    // Used by report regeneration only. No-op implementation is fine in dev (since regen is unsupported).
    IDisposable ApplyOverrides(SettingsOverridesDto overrides);
}
```

### IWebAssets — HTML/CSS/JS

```csharp
public interface IWebAssets {
    Task<byte[]?> ReadAsync(string logicalName, CancellationToken ct = default);
}
```

- Prod impl: `Assembly.GetManifestResourceStream(logicalName)` (unchanged)
- Dev impl: `File.ReadAllBytesAsync(Path.Combine(webRoot, logicalName))` — supports hot reload on save

### IDashboardLogger — logging shim

```csharp
public interface IDashboardLogger {
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
    void Debug(string message);
}
```

- Prod impl: forwards to `NINA.Core.Utility.Logger`
- Dev impl: `Console.WriteLine` with ANSI color prefix

### IDashboardPaths — filesystem layout

```csharp
public interface IDashboardPaths {
    string ReportsDir { get; }                   // e.g. %LOCALAPPDATA%\NINA\NightSummary\reports
    string LogsDir { get; }
    string HipsCacheDir { get; }
    string ReportHtmlPath(string sessionId);
    string ReportSettingsPath(string sessionId); // {sessionId}.settings.json sidecar
    string LivestackDir(string sessionId);
    string LivestackManifestPath(string sessionId);
    string LivestackImagePath(string sessionId, string filename);
}
```

Every filesystem call in the server routes through this. Prod uses `%LOCALAPPDATA%` paths; dev uses fixture root.

---

## DTO schema

All DTOs are records in the classlib. Examples (abbreviated):

```csharp
public record SessionDto(
    string SessionId,
    DateTime StartLocal,
    DateTime? EndLocal,
    string? TargetName,
    string? ProjectName,
    int ImageCount,
    double IntegrationSeconds,
    // ... all fields currently serialized to JSON in /api/sessions
);

public record ImageDto(
    int Id,
    string SessionId,
    string? TargetName,
    string? Filter,
    double ExposureSeconds,
    double? Hfr,
    double? Snr,
    DateTime Timestamp,
    double? PositionAngle,
    // ... etc
);

public record TSProjectDto(
    string Guid,
    string Name,
    int State,
    bool IsMosaic,
    IReadOnlyList<TSTargetDto> Targets,
    // ...
);

// + EventDto, TimingEventDto, TargetDetailDto, TSApiSettingsDto,
//   DashboardSettingsDto, SettingsOverridesDto, ReportRegenerationResultDto
```

**Exact schema deferred to implementation time** — I'll mirror the current JSON responses field-for-field to guarantee zero frontend breakage.

---

## Wiring (dependency injection)

`DashboardServer` constructor gets all dependencies:

```csharp
public DashboardServer(
    IDashboardDataSource data,
    IDashboardCache cache,
    IPluginSettings settings,
    IWebAssets webAssets,
    IDashboardLogger log,
    IDashboardPaths paths,
    DashboardServerOptions options)  // port, bind address, etc.
{ ... }
```

Plugin wires the NINA-backed impls; dev harness wires the fixture impls.

---

## What stays inside DashboardServer (classlib)

- HTTP listener + routing (pure `System.Net.HttpListener`)
- JSON serialization (`System.Text.Json`)
- HiPS2FITS HTTP calls (`HttpClient`, stdlib)
- Tonight API HTTP calls (`HttpClient`, stdlib)
- Response shaping / query param parsing
- The entire altitude chart generation algorithm (pure math)
- URL encoding/decoding
- Per-request caching (`thumbnailCache`, `livestackCache`, `altitudeChartCache` — in-memory dicts)

---

## Open design questions

1. **Report regeneration in dev.** Proposed: return 501 Not Implemented. Alternative: stub out with a "pretend it worked" that just re-reads the existing fixture HTML. **My call: 501.** Cleaner, forces devs to test regen in real NINA.

2. **Direct-SQLite `GetLatestPositionAngles` query** (currently reaches into `nightsummary.sqlite`). Options:
   - (a) Add `GetLatestPositionAngles()` to `SessionDatabase` class, route through `IDashboardDataSource`. **Preferred** — it's a plugin-layer concern.
   - (b) Expose a generic "run raw SQL" hook on the interface. **Rejected** — breaks the abstraction.

3. **Async propagation.** Current server is partly sync, partly async. I'll make the interfaces fully async. Server method signatures will change accordingly. No functional change.

4. **SettingsOverrides shape.** Current implementation uses a snapshot/restore pattern with side-effects on a singleton. Proposed `IDisposable` scope pattern cleans that up. Low risk since dev never invokes it.

5. **Where does the HiPS cache live?** Currently disk (`hips-cache/{md5}.jpg`). Keep that as-is via `IDashboardPaths.HipsCacheDir`. Prod and dev both use disk cache.

---

## Risks

- **Behavior drift during port.** Every call-site swap is a chance to accidentally change behavior. Mitigation: one endpoint at a time, before/after JSON response comparison against the current server where possible.
- **Settings snapshot/restore regression.** That code is fragile. I'll preserve the current logic byte-for-byte in `NinaPluginSettings.ApplyOverrides`.
- **Embedded resource loading in classlib.** `Assembly.GetExecutingAssembly()` returns the classlib, not the plugin. Need `Assembly.GetCallingAssembly()` or explicit assembly ref, or embed the resources in the classlib instead of the plugin. **Preferred:** move the `<EmbeddedResource>` entries to the classlib csproj. Webroot for dev still comes from disk via `IWebAssets`.

---

## Check-in questions for the user

1. OK to proceed with this interface shape?
2. Agree on 501 Not Implemented for regen in dev?
3. Move embedded resources into classlib (yes/no)? Yes simplifies `IWebAssets` prod impl.
4. Anything I missed in the audit?

If yes-yes-yes-no, I start porting. ~3 working days estimated: 1 day classlib + interfaces + DTOs, 1 day server port, 1 day providers + dev harness. Then Phase 5 (delete Python — with your explicit confirm) and Phase 6 (smoke test).
