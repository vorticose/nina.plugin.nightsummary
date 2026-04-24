# Dashboard Server Unification Plan

**Status:** Planned, not started
**Branch:** `v3-dev` (this worktree)
**Created:** 2026-04-24
**Goal:** Kill the dev-vs-prod drift between `tools/dev-dashboard/server.py` and `NINA.Plugin.NightSummary/Server/DashboardServer.cs`.

---

## Why

Today we maintain two dashboard servers in two languages:

- **Prod:** `NINA.Plugin.NightSummary/Server/DashboardServer.cs` (3104 lines, C#, lives inside NINA plugin, reads SQLite + NINA mediators)
- **Dev:** `tools/dev-dashboard/server.py` (1963 lines, Python, reads JSON fixtures in `tools/dev-dashboard/data/`)

Every new endpoint must be written twice. Every URL-decode, every merge rule, every response shape. Drift is inevitable and already happening:

- `/api/ts/projects` GET — prod only (dev picker 404s)
- `/api/stats/projects/custom` POST — dev only (custom projects 404 in prod)
- `/api/tonight/preview` — dev normalizes PascalCase→camelCase, prod may not
- URL decoding mismatch (already fixed 2026-04-23 for livestack filenames)
- Custom project persistence logic copied between both files
- TS merge logic copied between both files

The Python server is a liability. It diverges silently, and changes that "work in dev" land broken in prod.

## Constraints (user-stated)

1. Dev server must run **without** prod — no NINA install, no SQLite plugin DB, no live sessions.
2. Dev server must support **rapid iteration** — no long rebuild/restart cycle on every change.
3. Dev and prod must be **as close to identical as possible** — deploys should "just work" without cleanup.

## Solution: Shared server library + provider abstraction

One C# codebase runs both places. Fixture vs live data swapped via interface.

```
NINA.Plugin.NightSummary.Dashboard/   ← NEW classlib (net8.0)
  DashboardServer.cs                   ← moved from plugin
  Web/                                 ← HTML/CSS/JS (embedded in release)
  IDashboardDataSource.cs              ← interface for all external data
  IDashboardPaths.cs                   ← cache dir, web assets dir
  Models/                              ← POCOs used across providers

NINA.Plugin.NightSummary/              ← existing plugin project
  Server/NinaDashboardDataSource.cs    ← wraps SessionDatabase, TS DB, Profile
  → references Dashboard classlib

tools/dev-dashboard-cs/                ← NEW C# console app (replaces Python)
  Program.cs                           ← boots server, wires FixtureDataSource
  FixtureDashboardDataSource.cs        ← reads JSON from --data dir
  data/                                ← existing fixtures, unchanged
  → references Dashboard classlib
```

**How constraints are satisfied:**

| Constraint | How it's met |
|---|---|
| Runs without prod | `FixtureDashboardDataSource` reads `data/*.json`; no NINA assemblies referenced |
| Rapid iteration | `dotnet watch run` in `tools/dev-dashboard-cs/` → ~1s rebuild on server edits. Web assets read from disk in dev (browser F5 = instant). |
| Parity | Same `DashboardServer.cs` serves both. Drift impossible by construction. |

---

## Phases

### Phase 1 — Extract dashboard into classlib

- Create `NINA.Plugin.NightSummary.Dashboard.csproj` targeting `net8.0` (no `-windows` suffix — keep it cross-platform so Mac dev works)
- Move `DashboardServer.cs`, `DashboardLog.cs`, `Server/Web/` into it
- Plugin project adds `ProjectReference` to classlib
- Verify:
  - Plugin builds green
  - Embedded web assets still resolve in release build (`<EmbeddedResource>` for all `Web/**`)
  - Plugin DLL deploys and dashboard serves on port 8181 as before

**Gotcha:** NINA plugin has hard `net8.0-windows` + NINA.Plugin SDK ref. Classlib must target plain `net8.0` so the console app can reference it cleanly on Mac/Linux too. Anything NINA-specific (WPF, profile UI) stays in the plugin project.

### Phase 2 — Define data source interface

Audit `DashboardServer.cs` for every external call. Current known dependencies:

- `SessionDatabase` — session list, session detail, images, events, timing, thumbnails, livestack, altitude chart cache
- `TargetSchedulerDatabase` (via `TSDbReader`) — TS projects tree, exposure plans, completion state
- NINA `IProfileService` — profile name, location, equipment, settings
- Plugin settings JSON (dashboard overrides, custom projects, TS links)
- Filesystem (report HTML parsing for thumbnails, livestack JPEG directory)
- `HiPS2FITS` client — mosaic thumb generation + disk cache

Hoist to `IDashboardDataSource` methods returning plain POCOs:

```csharp
public interface IDashboardDataSource {
    Task<IReadOnlyList<SessionSummary>> GetSessionsAsync();
    Task<SessionDetail?> GetSessionDetailAsync(string sessionId);
    Task<IReadOnlyList<ImageRecord>> GetSessionImagesAsync(string sessionId);
    // ... etc
    Task<TSProjectsSnapshot?> GetTSProjectsAsync();
    Task<DashboardSettings> GetSettingsAsync();
    Task SaveDashboardOverridesAsync(DashboardOverrides overrides);
    // etc
}
```

All methods return POCOs — no NINA types cross the boundary.

Define `IDashboardPaths` for:
- Thumbnail cache dir
- HiPS disk cache dir
- Per-session report HTML dir
- Web assets dir (dev: disk path; prod: null → embedded)

**This is the real work.** Plan 1–2 days for careful extraction. Incremental: one endpoint at a time, not big-bang.

### Phase 3 — Implement providers

**`NinaDashboardDataSource`** (plugin, Windows-only):
- Thin wrapper. Each method calls existing NINA accessors, maps to POCOs.
- Reuses `SessionDatabase`, `TargetSchedulerDatabase`, profile service.
- Goal: zero behavior change in prod.

**`FixtureDashboardDataSource`** (cross-platform, dev):
- Reads `data/sessions.json`, `data/ts-projects.json`, `data/settings.json`, `data/filters.json`
- Per-session: `data/sessions/{id}/detail.json`, `report.html`, `livestack/*.jpg`
- Writes dashboard overrides back to `data/ts-dashboard-meta.json`
- HiPS cache dir: `data/hips-cache/` (already exists)
- File-based, no DB.

### Phase 4 — Dev harness console app

`tools/dev-dashboard-cs/Program.cs`:

```csharp
// args: --port 8182 --data ./data --web-root ./NINA.Plugin.NightSummary.Dashboard/Web
var opts = ParseArgs(args);
var dataSource = new FixtureDashboardDataSource(opts.DataDir);
var paths = new FixtureDashboardPaths(opts.DataDir, opts.WebRoot);
var server = new DashboardServer(dataSource, paths, opts.Port);
await server.RunAsync();
```

Run via:
```bash
cd tools/dev-dashboard-cs
dotnet watch run -- --port 8182 --data ./data --web-root ../../NINA.Plugin.NightSummary.Dashboard/Web
```

`dotnet watch` re-runs on server source edits (~1s rebuild). Web asset edits don't even trigger rebuild — just reload browser.

### Phase 5 — Kill Python

- Delete `tools/dev-dashboard/server.py`
- Delete `tools/dev-dashboard/snapshot.py`
- Keep `tools/dev-dashboard/data/` — move into `tools/dev-dashboard-cs/data/` or symlink
- Update `.claude/launch.json` → `dev-dashboard` entry: swap `python server.py` for `dotnet watch run`
- Update `CLAUDE.md` references
- Update memory: `reference_dev_server.md`

### Phase 6 — Smoke test

Add xunit test project or extend existing `NINA.Plugin.NightSummary.Tests`:

```csharp
[Fact]
public async Task Dashboard_AllEndpoints_Return200_WithFixtureData() {
    var server = BootWithFixtures();
    foreach (var route in KnownRoutes) {
        var resp = await server.GetAsync(route);
        Assert.Equal(200, (int)resp.StatusCode);
    }
}
```

Catches missing route regressions before deploy.

---

## Effort estimate

| Phase | Est. |
|---|---|
| 1 — Extract classlib | 0.5 day |
| 2 — Define interface | 1.5 days |
| 3 — Implement providers | 1 day |
| 4 — Dev harness | 0.5 day |
| 5 — Delete Python, docs | 0.5 day |
| 6 — Smoke test | 0.5 day |
| **Total** | **~4–5 days** |

## Risks / open questions

- **NINA entanglement in DashboardServer.cs.** Some code likely reaches into NINA singletons directly. Audit in Phase 2 will surface how deep. Fallback: narrow the interface scope and keep a few NINA-specific branches gated behind `if (dataSource is NinaDashboardDataSource)`. Ugly but contained.
- **Embedded resources vs disk assets.** Dev reads Web from disk, prod from embedded. Need a single code path that checks `IDashboardPaths.WebRoot != null ? ReadFile(path) : ReadEmbedded(path)`. Straightforward but test both.
- **Port management.** Dev 8182, prod 8181. Separate configs, no collision.
- **Thread/async model parity.** Python uses `http.server` threaded; C# uses `HttpListener`. Behavior should match if all handlers stay stateless.
- **HiPS2FITS client.** Prod uses `HttpClient` with NINA's proxy config. Dev needs standalone `HttpClient`. Likely no change — `HttpClient` works the same in either host.
- **Altitude chart cache.** Prod persists to `nightsummary-dashboard-cache.sqlite` in `%LOCALAPPDATA%`. Dev currently doesn't cache. Decide: port the cache to `IDashboardPaths.AltitudeCacheDir` (Sqlite file), or skip caching in dev (compute on demand). Latter is simpler.
- **Breaking change to v3-dev consumers.** During the refactor, v3-dev may be partially broken. Do this on a dedicated `feature/dashboard-unification` branch cut from v3-dev, merge back only when all phases are done and smoke test green.

## Success criteria

- `dotnet build NINA.Plugin.NightSummary.sln -c Release` green
- Plugin deploys and dashboard serves at `http://localhost:8181/` with zero visible change
- `cd tools/dev-dashboard-cs && dotnet watch run` serves `http://localhost:8182/` with no NINA install
- Edit `DashboardServer.cs` → dev server auto-reloads in ≤2s
- Edit a file in `Web/` → browser refresh shows change, no rebuild
- `server.py` deleted from repo
- Smoke test asserts 100% of routes return 200 against fixture data

## Pickup checklist (next session)

1. Read this file + `reference_dev_server.md` memory
2. Create branch: `git checkout v3-dev && git checkout -b feature/dashboard-unification`
3. Start Phase 1: create `NINA.Plugin.NightSummary.Dashboard.csproj`, move files, verify plugin build
4. Commit per phase. Don't merge back until Phase 6 smoke test green.
