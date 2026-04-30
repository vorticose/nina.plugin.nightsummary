# Night Summary Companion — Plan

A lightweight companion app that lets users access the full NS dashboard from any machine on their local network, even when the NINA machine is off.

---

## Problem

The NS dashboard server runs inside the NINA plugin. When the NINA machine (typically an observatory or backyard PC) is off, the dashboard is inaccessible. Users who image remotely or on a dedicated machine want to browse session history, view reports, and check TS progress from their main PC or tablet at any time.

---

## Decision: DB Sync + Same Server Binary

Several approaches were considered:

| Approach | Verdict |
|----------|---------|
| Static file generation + simple file server | Rejected — requires full dashboard JS refactor; risk of visual regressions |
| Add all missing API endpoints, companion pulls via HTTP | Rejected — enormous API surface; TS, LiveStack, per-image data all need new endpoints |
| Sync SQLite + reports, companion runs identical server | **Selected** — full parity guaranteed, no dashboard changes, reuses existing server code |

**Core principle:** the companion is not a new server. It runs the exact same NS server binary pointed at a local copy of the data. The dashboard has no idea it's running on a different machine.

---

## Architecture

```
NINA Machine (imaging PC, may be off)
  %LOCALAPPDATA%\NINA\NightSummary\
    nightsummary.sqlite          ← primary session/image/event DB
    reports\*.html               ← per-session HTML reports
    reports\*.settings.json      ← per-session settings sidecars
    reports\livestack\**\*.jpg   ← LiveStack master images
    reports\livestack\**\livestack.json
  %LOCALAPPDATA%\NINA\SchedulerPlugin\
    schedulerdb.sqlite           ← Target Scheduler DB (optional)

              ↕ pull on sync (HTTP export endpoints)

Companion Machine (always-on PC, NAS, Mac, Pi, etc.)
  [data dir]/
    nightsummary.sqlite          ← synced copy
    reports\...                  ← synced copy
    nightsummary-dashboard-cache.sqlite  ← auto-created by server
    logs\...                     ← auto-created by server

  NS Companion (same server binary, cross-platform)
    → serves dashboard at http://companion-machine:8182
```

---

## User Experience

**One-time setup:**
1. Download companion binary for their OS from GitHub releases
2. Run it — auto-starts on boot
3. In NS plugin settings on the NINA machine: enter companion's IP and port
4. Done

**Ongoing (automatic):**
- Companion boots → attempts sync with NINA machine immediately
- NINA unreachable → retries every 30 minutes
- NINA reachable → syncs all data, updates "Last synced" timestamp
- User browses `http://companion-machine:8182` at any time

**Manual sync:**
- Sync button on dashboard (grayed out if NINA unreachable)
- Sync settings tab with NINA machine address, last synced time, sync status

---

## Data Footprint (What Gets Synced)

| Source | Path on NINA Machine | Notes |
|--------|----------------------|-------|
| Main DB | `%LOCALAPPDATA%\NINA\NightSummary\nightsummary.sqlite` | Full file copy |
| Reports | `%LOCALAPPDATA%\NINA\NightSummary\reports\*.html` | Per-session HTML |
| Settings sidecars | `%LOCALAPPDATA%\NINA\NightSummary\reports\*.settings.json` | Per-session settings |
| LiveStack images | `%LOCALAPPDATA%\NINA\NightSummary\reports\livestack\**\*` | JPEGs + manifests |
| TS DB | `%LOCALAPPDATA%\NINA\SchedulerPlugin\schedulerdb.sqlite` | Optional; skip if absent |

**Not synced (auto-created by server on startup):**
- `nightsummary-dashboard-cache.sqlite` — altitude charts, thumbnail cache
- `logs\` — diagnostic logs
- `hips-cache\` — CDS mosaic thumbnails (re-fetched on demand)

---

## New Plugin Endpoints (Minimal)

The NS plugin's embedded server needs new export endpoints the companion calls during sync. **All export endpoints require `Authorization: Bearer <key>` — see [Authentication](#authentication) below.**

```
GET /api/export/database
  → stream nightsummary.sqlite via SQLite backup API (VACUUM INTO temp file)
    NOT a raw file copy — avoids WAL corruption while server writes
  → application/octet-stream

GET /api/export/ts-database
  → same pattern, 404 if TS not installed

GET /api/export/reports?since=ISO8601
  → zip stream of reports\ tree (html + settings.json + livestack\**)
  → mtime-based filter; first sync = full pull, subsequent = incremental

GET /api/export/manifest?since=ISO8601
  → JSON list of current report files: path, mtime, size
  → companion uses to compute deletions and resume partial syncs

GET /api/health
  → { ok, version, schemaVersion }
  → reachable check + schema compatibility gate

GET /api/mode
  → "primary" | "companion"
  → dashboard reads on load to gate sync UI / staleness banner
```

### Authentication

LAN is not a trusted boundary (roommates, guests, IoT, accidental UPnP exposure). Export endpoints require a bearer token from day one.

- Plugin generates API key on first run, stores in existing NS settings store.
- Plugin settings UI shows key with "Copy" button.
- Companion reads key from `companion.json` → `nina.apiKey`, sends as `Authorization: Bearer <key>` header.
- Missing/wrong key → 401.
- Non-export endpoints (the dashboard itself) remain unauthenticated as today.

---

## Sync Mechanism

**Direction:** Pull (companion calls NINA machine). Push was rejected because it requires the companion to be running at the exact moment NINA finishes a session.

**Triggers:**
1. Companion boot — immediate attempt
2. Periodic poll — every 30 min if last attempt failed; every 4 h if last succeeded; immediate on unreachable→reachable transition
3. Manual — "Sync with NINA" button in dashboard

**Sync flow:**
```
1. GET /api/health                          → reachable + schema compat check
2. If reachable:
   a. GET /api/export/manifest?since=...    → remote file list (path, mtime, size)
   b. Diff against local manifest           → new/changed + orphans
   c. GET /api/export/database              → VACUUM INTO copy, atomic replace local
   d. GET /api/export/ts-database           → same (if 200)
   e. GET /api/export/reports?since=...     → unzip into local reports\
       (use HTTP Range / resumable for large payloads)
   f. Delete local orphans (files in local manifest, not in remote)
       Guard: if remote manifest empty/errored, skip delete — never nuke on bad response
   g. Write last_synced.json
3. If unreachable: log, schedule retry per cadence above
```

**Deletion propagation:** without manifest diff, deleted sessions on NINA leave orphan files on companion forever. Manifest-based reconcile handles this. Orphan pass is gated on a non-empty manifest response.

---

## Dashboard Changes

### Sync Settings Tab (new)
- NINA machine address: `[hostname or IP]:[port]`
- Last synced: `2026-04-28 11:42 PM` (or "Never")
- Sync status: `Connected` / `NINA unreachable` / `Syncing...`
- `[Sync Now]` button (disabled when unreachable)
- Auto-sync on boot: toggle

### Staleness Indicator
- When not running on the NINA machine itself, dashboard shows a subtle banner:
  `Viewing synced data — last updated 3 days ago`
- Threshold for "stale" warning: configurable, default 7 days

### Sync Button on Main Dashboard
- Accessible from the session list header
- Grayed out when NINA machine is unreachable
- Shows spinner + "Syncing..." during active sync

---

## Target Scheduler API Cache

The dashboard queries the TS live HTTP API for "Tonight's Preview" (real-time project status). When the companion is serving and NINA is off, this call fails. Solution: cache the last TS API response with a noon-boundary expiry.

**Cache location:** `nightsummary-dashboard-cache.sqlite` — new `ts_api_cache` table (fits the existing cache pattern alongside altitude charts and thumbnails).

**Schema:**
```sql
CREATE TABLE ts_api_cache (
    cached_at TEXT NOT NULL,   -- ISO 8601 timestamp
    data      TEXT NOT NULL    -- full TS API JSON response
);
```

**Expiry logic — noon boundary:**
Cache is valid if `cached_at` is after the most recent local noon. This handles overnight sessions correctly: data cached at 11 PM remains valid at 2 AM the same imaging night (midnight does not invalidate it). A new calendar day after noon invalidates the previous night's cache.

```
last_noon = today's date at 12:00 PM local time
if last_noon > now: last_noon = yesterday at 12:00 PM local time
cache_valid = (cached_at >= last_noon)
```

**Behavior:**
- NINA reachable → always fetch live, update cache
- NINA unreachable + cache valid → serve cached response, show "Tonight's Preview (cached)" label
- NINA unreachable + cache stale/empty → hide Tonight's Preview section gracefully (existing no-TS behavior)

---

## Companion Binary

**Runtime:** .NET 8 single-file publish — same codebase as the production server, no code changes to server logic.

**Platform targets:**
| OS | Binary |
|----|--------|
| Windows x64 | `NightSummaryCompanion-win-x64.exe` |
| macOS arm64 | `NightSummaryCompanion-osx-arm64` |
| macOS x64 | `NightSummaryCompanion-osx-x64` |
| Linux x64 | `NightSummaryCompanion-linux-x64` |

**Auto-start:**
- Windows: Windows Service or HKCU Run registry entry
- macOS: Login item / launchd plist
- Linux: systemd user service

**Mode flag:** companion launches server with `--companion` arg (or detects `companion.json` presence). Server exposes `/api/mode` so dashboard can gate sync UI and staleness banner. The "server has no idea" principle holds for business logic; mode is a thin presentation-layer signal only.

**Configuration (companion-specific `companion.json`):**
```json
{
  "port": 8182,
  "dataDir": "C:/ProgramData/NightSummaryCompanion",
  "nina": {
    "host": "192.168.1.100",
    "port": 8182,
    "apiKey": "<paste from NS plugin settings>"
  },
  "sync": {
    "onBoot": true,
    "pollingIntervalMinutesOnFailure": 30,
    "pollingIntervalHoursOnSuccess": 4
  }
}
```

---

## Mobile Access & Notifications

### Progressive Web App (PWA)

The dashboard will support installation as a PWA — no App Store, no native code. User visits the companion URL in Safari or Chrome, taps "Add to Home Screen," and it installs like a native app with a home screen icon and full-screen launch.

**What this requires (dashboard changes):**
- `manifest.json` — app name, icon, theme color, display mode
- Service worker — caches dashboard JS/CSS/assets for offline load
- HTTPS or localhost for service worker registration (see Notifications below)

**What this gives users:**
- Home screen icon on iPhone, iPad, Android
- Full-screen launch (no browser chrome)
- Offline load of the dashboard shell (data still requires sync to be current)
- Foundation for Web Push notifications

### Notifications

Two tiers based on infrastructure complexity:

**Tier 1 — Existing channels (ship first, zero new infrastructure):**
NS already has Pushover, Discord, and email wired up. Add companion-specific events to those channels:
- "New session synced from NINA" (companion detected new data after sync)
- "NINA back online" (companion reached NINA after previously being unreachable)

Users who already configured Pushover or Discord get these for free. Works on any network, no HTTPS required.

**Tier 2 — Web Push (stretch goal, requires HTTPS):**
Native browser push notifications — fire even with browser closed, appear in OS notification center. Requires a secure context (HTTPS or localhost). Accessing companion at `http://192.168.1.x:8182` from a phone is not a secure context.

**Path to HTTPS:**
- **Tailscale (recommended):** If companion is on the tailnet, Tailscale provides real HTTPS certs automatically at `https://machine-name.tailscale.net`. Many astrophotographers already run Tailscale. Zero cost, no cert management.
- **Self-signed cert:** User trusts it once on device. Workable but annoying setup.
- **Local reverse proxy (Caddy):** More setup than most users want.

**Recommendation:** Ship Tier 1 with Phase 1. Document Tailscale HTTPS + Web Push as an opt-in stretch feature for users who want native notifications. Do not block companion v1 on HTTPS.

---

## What Doesn't Change

- Dashboard JS/CSS — zero changes
- Server business logic — zero changes
- Plugin data collection — zero changes
- Existing single-machine users — zero impact; companion is purely additive

---

## Out of Scope (Deferred)

- **mDNS/Bonjour discovery** — auto-discover NINA machine on local network. Nice to have, not required for v1. Manual IP config is acceptable.
- **SQLite delta sync** — WAL-based or page-level incremental sync of the SQLite file. Full file copy is fine until DBs get large (multi-year users). Revisit if sync time becomes a complaint.
- **Authentication on export endpoints** — export endpoints should be gated by the existing API key to prevent unauthorized DB downloads. Deferred but should be done before any public-facing deployment.
- **Public cloud hosting** — see CLOUD_PLAN.md for prior architecture notes. Companion is local-network only.
- **VPS as companion host** — a personal VPS ($5-8/month, e.g. Hetzner) could host the companion for 24/7 uptime without needing a dedicated always-on local machine. Key challenge: NINA machine sits behind home NAT, so the pull-based sync breaks. Tailscale (free for personal use) likely solves this cleanly by putting both machines on the same tailnet. Worth researching as an alternative deployment target before committing to local-only architecture.
- **Companion UI** — the companion has no native UI of its own. Configuration is done via the dashboard's Sync Settings tab or the `companion.json` file directly.

---

## Implementation Phases

### Phase 1 — Export Endpoints + Sync Engine

**Server-side (NS plugin):**
- `GET /api/export/database` — stream via SQLite `VACUUM INTO` temp file, not raw copy
- `GET /api/export/ts-database` — same pattern, 404 if TS absent
- `GET /api/export/reports?since=ISO8601` — zip stream, mtime-filtered
- `GET /api/export/manifest?since=ISO8601` — JSON file list (path, mtime, size) for diff + resume
- `GET /api/health` — `{ ok, version, schemaVersion }`; verify exists, add if not
- `GET /api/mode` — `"primary"` / `"companion"`
- All export endpoints: `Authorization: Bearer <key>`, 401 on miss

**API key plumbing:**
- Generate on first run, store in existing NS settings store
- Display in plugin settings UI with Copy button

**Mode flag:**
- Companion launches with `--companion` arg or `companion.json` presence
- Dashboard reads `/api/mode` on load to gate sync UI / banner

**Sync engine (companion side):**
- Boot trigger + poll loop (30 min on fail / 4 h on success / immediate on reachability flip)
- Manifest diff → download new + changed → DB + ts-db → orphan delete (guarded)
- HTTP Range / resumable downloads for reports zip over threshold
- `last_synced.json` for status reporting

**.NET version check:**
- Confirm existing server target. If <.NET 8, decide upgrade vs. build companion on existing runtime. **Block before Phase 2.**

**Tests:**
- `VACUUM INTO` during active write → no corruption
- Manifest diff with mixed mtime/size changes
- Orphan deletion with truncated/empty remote manifest = no-op
- 401 on missing/wrong key

**CLI validation (no dashboard UI yet):**
- `companion sync` — one-shot manual sync, prints progress
- `companion serve` — runs server pointed at synced data dir
- Validate: dashboard loads from companion, sessions render, reports open, livestack renders

### Phase 2 — Companion Binary + Distribution
- .NET 8 single-file publish for all four platforms
- `companion.json` config
- Auto-start setup (Windows first, Mac/Linux secondary)
- GitHub releases pipeline

### Phase 3 — Dashboard Integration
- Sync Settings tab
- Sync button on session list
- Staleness banner
- TS API cache (noon boundary)
- "Connected to NINA" / "Viewing synced data" mode detection

### Phase 4 — PWA + Notifications
- `manifest.json` + service worker for home screen install
- Pushover/Discord events for sync notifications (Tier 1)
- Document Tailscale HTTPS path for Web Push (Tier 2)

### Phase 5 — Polish + Docs
- Error states (NINA unreachable, partial sync, disk full)
- Setup guide in docs/
- Link to companion download from plugin settings UI

---

## Open Questions

1. **API key bootstrap UX** — resolved-ish: user copies from plugin settings UI into `companion.json`. Pairing flow (companion shows code, user enters in plugin) is nicer but deferred.
2. **First-sync size** — multi-year users may pull GBs. Mitigated by HTTP Range / resumable download in sync engine. Watch for complaints; if painful, chunk reports zip per-month.
3. **`?since=` semantics for LiveStack masters** — masters get rewritten each new frame during active sessions. Mtime filter will re-pull active masters every sync. Acceptable; flag if bandwidth becomes issue.
4. **iOS PWA over plain HTTP** — Safari requires secure context for service worker registration. `http://192.168.x:8182` from iPad = no install, no offline, no push. Phase 4 must commit to one: (a) Tailscale prereq for iOS users, or (b) self-signed cert generator baked into companion with one-tap trust. Decide before Phase 4 starts.
5. **Same-machine port conflict** — running companion on the NINA box is just NINA itself; document as unsupported rather than auto-fallback.
