# v3.4 Performance Audit: Findings (pass 1)

Goal: better user experience, not speed for its own sake. Reports and dashboard should
load faster, interactions should update in place instead of reloading, and animations
should feel snappy. Priority devices: iPad or phone over Tailscale, and the companion.

Pass 1 covers what is measurable from the Mac: the dashboard server, the dashboard
frontend, and report page load. Plugin runtime (report generation inside NINA) and
companion sync are pass 2, on the Windows dev PC (never the live rig).

## How this was measured

- Dev harness (`tools/dev-dashboard-cs`) built from `dev` @ 22a14a9, run against an
  APFS clone of the dev snapshot (27 sessions on the list, 32 targets, 29 TS projects,
  172 reports). The original snapshot was not touched.
- **iPad profile**: a local throttling proxy adds 60 ms RTT (including TCP handshake
  cost per connection) and a 20 Mbps downstream cap. Browser: Chromium in the Claude
  desktop app, no CPU throttling. A real iPad CPU will make render costs 3 to 5x worse,
  so the main-thread numbers below are a floor.
- Server handler times are best-of-3, warm, on localhost (Apple Silicon). The Windows
  rig with NINA running will be slower.

## Baseline (iPad profile)

| Scenario | Requests | Bytes | Time |
|---|---:|---:|---:|
| First load, Sessions: HTML arrives | 1 | 802 KB | 0.49 s |
| First load, Sessions: list data in (`/api/sessions`) | | | 0.86 s |
| First load, Sessions: all card thumbnails in | | | 2.44 s |
| First load, Sessions: everything settled | 80 | 2.5 MB | 2.97 s |
| Stats, first visit | 24 | 1.8 MB | 1.29 s |
| Stats revisit, **and every Targets-tab edit** | 5 | 119 KB | 0.52 s |
| Open a large report (4-target night) | 5 | 2.4 MB | 1.50 s |
| Re-open the same report | 4 | 2.4 MB | 1.43 s |
| Back to Sessions (client cache works) | 0 | 0 | 0 |

Same first load on localhost with no throttling: ~0.63 s settled. On the iPad profile,
round trips and uncompressed bytes account for the other 2.3 s.

## Findings, ranked by user-visible impact

### F1. Targets-tab edits rebuild the whole Stats page (the "page reloads" you feel)

Seven mutation paths call `renderStats()`: create mosaic (dashboard.js ~8375), TS status
override (~8490), close Manage Projects (~8522), close the project-assign picker (~8969),
clear assignment (~9114), TS link picker (~9206, ~9215), and unlink (~2029).
`renderStats()` re-fetches `/api/stats/targets`, `/api/stats/summary`, `/api/settings`
and `/api/sessions`, then replaces `#content` wholesale.

Measured on one edit (iPad profile, scrolled 1500 px down the Targets grid):
- at ~200 ms the 29,143 px page collapses to a 104 px "Loading stats..." line
- at ~420 ms the grid comes back, **scrolled to the top** (1500 px -> 0)

Fix: update in place (see `feedback_modal_inplace`). Apply the change to the local state
(`statsProjectAssignments`, `statsTsProjects`, and similar), re-render only the affected
cards or the grid without the loader, and keep the scroll position. Where server-derived
data is needed, have the POST return it, or re-fetch quietly in the background without
the loader or the scroll reset.

### F2. No HTTP compression on anything

All responses go out uncompressed (`TcpHttpResponse.Close()`, one central place).

| Response | Raw | gzip |
|---|---:|---:|
| `/` (dashboard HTML, all JS + CSS inlined) | 802 KB | 202 KB |
| `/api/sessions` | 50 KB | 11 KB |
| `/api/stats/targets` | 71 KB | 11 KB |
| altitude-chart (per card) | 12 KB | 2.4 KB |
| thumbnails JSON (per card) | 41 KB | 24 KB |
| large report | 2.5 MB | 1.6 MB |

Fix: gzip in `Close()` when the request sends `Accept-Encoding: gzip`, the content type
is text or JSON, and the body is over ~1 KB. Leave `StreamAsync` (exports) alone.

### F3. The 802 KB dashboard page is `no-store`: downloaded again on every open

`WriteHtml` sends `Cache-Control: no-cache, no-store, must-revalidate`, so every open,
refresh and iOS tab restore pulls the full page. Fix: an ETag (a hash of the assembled
HTML, which is already cached in `cachedDashboardHtml`) with `no-cache`, so a repeat
load is a single 304 round trip. Together with F2, a repeat open goes from ~0.5 s to
~0.12 s on the iPad profile.

### F4. Every request opens a new TCP connection, and Sessions fans out 80 requests

The server answers `HTTP/1.0` + `Connection: close`, so every request pays a handshake.
The Sessions list makes 3 requests per card (thumbnails, livestack, altitude-chart) for
all 27 cards up front, although an iPad shows about 4 to 6 above the fold. On the iPad
profile that fan-out is ~2.1 s after the list data arrives.

Fix, in order: (a) lazy-load card extras with an IntersectionObserver so only visible
cards fetch; (b) optionally, HTTP/1.1 keep-alive in the TCP server (riskier: custom
server, export streaming, companion auth path, so do it after (a) and measure).

### F5. Thumbnails travel as base64 inside JSON, so the browser never caches them

Session-card thumbnails, Stats target cards and mosaic thumbs (60 KB) are data URIs in
JSON: +33% size, no gzip benefit, no HTTP caching, and they are thrown away whenever the
client caches are cleared (rig switch, companion sync). The first Stats visit spent
753 KB of its transfer on thumbnail JSON.

Fix: serve thumbnails as image URLs (`.../thumb.jpg`) with an ETag or immutable cache
headers, and have the JSON carry URLs. Re-renders then cost nothing and `loading="lazy"`
works. This also makes F1's and F8's re-renders cheap.

### F6. Reports are re-downloaded on every open, and older reports carry a 781 KB icon

`/api/sessions/{id}/report` has no validators, so re-opening a report costs the full file
again (1.4 s for a 2.4 MB report on the iPad profile). Separately, every report generated
before the icon fix (merged 2026-08-23) embeds the 781 KB full-size header icon. That is
~64% of the median report (1.2 MB) in this snapshot.

Fix: an ETag from file mtime + size, so a re-open is a 304. Optional: when serving to the
dashboard, swap the known legacy icon data URI for the small header icon (read-side only,
never rewriting the file on disk). New reports: the Live Stack JPEG embeds are the next
biggest item (pass 2 lead).

### F7. `/api/sessions` takes ~210 ms, on the critical path of first load and every Stats load

`feature/sessions-list-scale` (unmerged) aggregates in SQL: **210 ms -> 9 ms** on this
snapshot, and the JSON is identical except that it now fills `moonPhase` for 11 sessions
that were missing it. It merges onto `dev` with only a `CHANGELOG_DRAFT.md` conflict,
but it also carries 3 unrelated commits (mobile activity-stripe snap x2, test logger
path). Decide: merge all 7, or cherry-pick the 2 perf commits plus the changelog note.

### F8. Companion: every sync wipes the dashboard's caches, even when nothing changed

`maybeAutoRefreshAfterSync` clears the thumbnail, altitude, detail and session caches
and re-routes whenever `LastSuccessUtc` advances. The sync engine advances it on every
successful run, including scheduled polls with zero changes. Fix: only refresh when the
sync reports changes (added/updated/deleted, or the DB changed), and re-fetch without
discarding image caches (free once F5 lands).

### F9. Small server hot spots

`/api/filters` takes 78 ms to return 41 bytes (report open), and `/api/stats/summary`
takes 77 ms (Stats). Both are candidates for a cache keyed on DB mtime.

### F10. Animation feel

- `--t-medium` is 450 ms (12 uses: card lift, shelf reveal, opacity/width reveals) and
  `--t-slow` is 650 ms. Card hover also has a 150 ms transition delay. Proposal: medium
  ~280 ms, slow ~400 ms, and review the delay.
- Layout-triggering transitions (`width`, `left`, `all`) at dashboard.css ~1636, 2702,
  2744, 3114, 4042, 4177, 4262, 5012, 5174: move to `transform`/`opacity` where possible.
- `backdrop-filter`: the sticky `header` blurs 14 px over scrolling content, and every
  `.target-card-ts-badge` has its own blur layer (one per target card). Both are known
  iPad Safari scroll-jank sources. Verify on a real iPad before and after.

## Closed leads

- **Cold altitude-chart cache**: the harness started with no cache DB. All 27 card
  charts were built within ~0.6 s of page load. Not a problem on current code.

## Pass 2 (Windows dev PC and companion), leads only

- **Companion sync re-downloads the full NS DB and the full TS DB (400 MB on the dev
  snapshot) on every sync**, with no unchanged check (`SyncEngine.PullSqliteAsync`).
  This affects how fresh the companion is and bandwidth over Tailscale.
- Report generation time inside NINA, and Live Stack embed size (2.2 MB of a 3 MB
  report on a 4-target night).
- NINA WebView2 report preview.
- Real-device iPad check: CPU cost of the Sessions and Targets renders, and scroll jank.

## Proposed batches

| Batch | Contents | Addresses | Risk |
|---|---|---|---|
| 1 | gzip + ETag for the page and reports | F2, F3, F6 (ETag) | Low: one central writer |
| 2 | Land `feature/sessions-list-scale` | F7 | Low: already tested on the rig |
| 3 | Targets edits in place, keep scroll | F1 | Medium: 7 call sites |
| 4 | Thumbnails as cacheable image URLs + lazy card extras | F5, F4a | Medium |
| 5 | Sync-refresh gating, filters/summary cache, animation tuning, legacy icon swap | F8, F9, F10, F6 | Low |
| later | Keep-alive; companion sync pass | F4b, pass 2 | Higher |

Each batch lands with before/after numbers from the same harness and throttle profile.

## Targets (guides, not hard requirements)

Agreed 2026-09-29. All on the iPad profile (60 ms RTT, 20 Mbps). Derived from perception
thresholds (under 100 ms feels instant, under 1 s keeps flow) and the estimated latency
floor for the current content.

| What | Baseline | Target |
|---|---:|---:|
| Sessions first load, list visible | 0.86 s | < 0.5 s |
| Sessions first load, fully settled | 2.97 s | < 1 s |
| Repeat dashboard open | 0.5 s | < 0.2 s |
| Report re-open | 1.43 s | < 0.2 s |
| First report open (large) | 1.50 s | < 1 s |
| Targets edit, visible response | 0.42 s + scroll lost | < 100 ms, scroll kept |

Stopping rules: about 1-2 hours of work per batch, then report. Stop when the remaining
gap is physics (the latency floor), not code. Stop and ask if a fix spreads into riskier
areas. No merge, push or deploy without asking.

`feature/sessions-list-scale`: cherry-pick the perf commits only (b30e24d, 93a7ffa, plus
the changelog note); the other commits get reviewed separately.

## Results

### Batch 1: gzip + ETag revalidation (`feature/perf-transport-caching`)

Changes: gzip in `TcpHttpResponse.Close()` for text/JSON over 1 KB when the client
asks (level Optimal: +9 ms CPU on the 800 KB page vs Fastest, 49 KB smaller); ETag +
`no-cache` on the dashboard page (content hash) and reports (mtime, size, theme, plugin
version); status line `HTTP/1.1` instead of `HTTP/1.0`.

The status line change was necessary, not cosmetic: Chromium never revalidates an
HTTP/1.0 response (verified with a probe server: identical headers, no `If-None-Match`
sent under 1.0, sent under 1.1). That also means the mosaic-thumb ETag shipped in 3.3.2
never produced a 304 in Chromium; it re-downloaded every time (still correct, just not
saving bandwidth).

| Scenario (iPad profile) | Before | After | Target |
|---|---:|---:|---:|
| First load: HTML arrives | 0.49 s, 802 KB | 0.27 s, 197 KB | |
| First load: list data in | 0.86 s | 0.86 s | < 0.5 s (batch 2) |
| First load: settled | 2.97 s, 2.5 MB | 2.75 s, 1.1 MB | < 1 s (batch 4) |
| Repeat dashboard open | 0.49 s | **0.14 s**, 300 B | < 0.2 s: met |
| Large report, first open | 1.50 s, 2.4 MB | 1.23 s, 1.6 MB | < 1 s (icon swap, batch 5) |
| Large report, re-open | 1.43 s | **0.33 s**, report 304 | < 0.2 s (see note) |
| Stats first visit | 1.29 s, 1.8 MB | 1.11 s, 1.1 MB | |

Report re-open note: the report itself is a 0-byte 304; the remaining 0.33 s is three
metadata requests (`/api/sessions/{id}`, `/settings`, `/api/filters` at 78 ms server
time), each on a new TCP connection. Closing the gap needs the filters cache (batch 5)
and eventually keep-alive.

Tests: `HttpCompressionTests` (negotiation, policy, wire bytes, HTTP/1.1 status line) and
`DashboardHttpCachingTests` (real server: page and report 304s, theme and regeneration
bust the tag, gzip only when asked). 42 tests pass on macOS via a scratch project that
links these files; the Windows suite still needs a run before merge.

## Goal (set 2026-09-29)

Finish batches 2, 3, 4 and the non-visual parts of 5. One branch per batch, each cut
from the previous one so they stack. Batch 3 is done when an edit produces the same page
as a full reload, with no loader and the scroll position kept. Stopping rules as above.
No merge, push or deploy. Deliverables: a before/after table against the targets, and
a separate proposal for animations and blur (the visual part of batch 5).

### Batch 2: session-list SQL aggregation (`feature/perf-sessions-list`)

Cherry-picked from `feature/sessions-list-scale`: 93a7ffa (moon without reading report
HTML) and b30e24d (aggregate list stats in SQL), plus the changelog line from 8422ffb
re-applied by hand. The branch's other commits (mobile activity-stripe snap x2, test
logger path) were left for separate review.

`/api/sessions`: 210 ms -> 9 ms warm on the snapshot. JSON is identical to `dev` except
`moonPhase` is now filled for 11 sessions that were missing it.

| Scenario (iPad profile) | Batch 1 | Batch 2 | Target |
|---|---:|---:|---:|
| First load: list data in | 0.86 s | **0.43 s** | < 0.5 s: met |
| First load: settled | 2.75 s | 2.30 s | < 1 s (batch 4) |

Tests: `MoonPhaseTests` and `SessionListMoonHttpTests` pass on macOS;
`SessionDatabaseQueryTests` uses System.Data.SQLite and runs on Windows only.

### Batch 3: Targets edits in place (`feature/perf-targets-inplace`)

`refreshStatsInPlace()` replaces `renderStats()` at the 8 edit call sites. It re-fetches
only `/api/stats/targets` (these edits never change the summary, settings or session
list), applies it with the same `applyStatsTargetData()` the full render uses, repaints
the tab content in one task (the tab row only if it changed), carries the old mosaic FOV
overlays over so they don't blink while re-fetching, and restores the scroll position.
Falls back to `renderStats()` if Stats isn't on screen or the fetch fails.

Done criterion checked on the iPad profile by sampling every 16 ms during a real edit,
then diffing `#content` against a full `renderStats()` of the same state:

| Edit path | Loader | Scroll | Content updated | Same as full reload |
|---|---|---|---:|---|
| Assign target to mosaic (grouped view, scrolled 6000 px) | none | kept | (no height change) | byte-identical, 879 nodes |
| TS status override (ungrouped, scrolled 3000 px) | none | kept | 0.31 s | byte-identical, 709 KB |
| Create mosaic (ungrouped, scrolled 2500 px) | none | new mosaic scrolled into view | 0.33 s | byte-identical, 942 nodes |

Before: page blanked to "Loading stats..." at 0.2 s, came back at 0.42 s, scroll reset
to the top.

Deliberate exception: create-mosaic switches the view to project grouping, so instead
of keeping a now-meaningless offset it scrolls the new mosaic to just below the header.

Target "< 100 ms visible response": the immediate acknowledgement (picker or modal
closes, checkbox toggles) is instant. The card content follows ~0.3 s later: two
sequential round trips (save, then re-read), each on a new connection. Options if that
matters: return the refreshed targets payload from the POST (saves ~0.12 s), or
optimistic local updates (under 100 ms, but gives up the "identical to reload" guarantee).

First-ever render of a brand-new mosaic's survey image waits on the external HiPS
service (seconds); this is unchanged and not part of the in-place path.

### Batch 4: cacheable thumbnails + lazy card extras (`feature/perf-lazy-thumbs`)

Server: `/api/sessions/{id}/thumbnails` now returns a versioned image URL
(`/api/sessions/{id}/target-thumb/{i}?v=<report mtime>`) in the existing `dataUri` field
instead of the base64 JPEG (the field already carried plain https URLs in the offline
fallback, so every consumer works unchanged). The new endpoint decodes the report's
embedded JPEG and serves it `Cache-Control: public, max-age=31536000, immutable`. The
request's `?rig=` is appended per response so `<img>` tags stay rig-scoped on a
multi-rig companion; the server cache stays rig-neutral. JSON per session: 41 KB -> 0.8 KB.

Client (Sessions list): cards on screen (viewport + 200 px) load their extras first;
the rest load through one IntersectionObserver with 1000 px lookahead. The first-load
"reveal together" now waits only for on-screen cards, including their images. Live
stack data is skipped below desktop width (the badges are hidden there anyway). Stats
target card images use `loading="lazy"`.

**Correction to the baseline:** the Sessions list is held at `opacity: 0` until its
assets are in, so "list data in" was not what the user sees. Measured the real reveal
on a pristine `dev` build: **3.13 s**.

| Scenario (iPad profile) | Baseline (dev) | Batch 2 | Batch 4 | Target |
|---|---:|---:|---:|---:|
| Sessions list appears (with images) | 3.13 s | ~2.3 s | **0.86 s** | < 1 s: met |
| First load: settled | 2.97 s, 80 req, 2.5 MB | 2.30 s | **0.98 s**, 23 req, 373 KB | < 1 s: met |
| Stats first visit | 1.29 s, 24 req, 1.8 MB | 1.11 s | 1.16 s, 29 req, **41 KB** | |
| Stats revisit | 0.52 s, 119 KB | | 0.60 s, 25 KB (images from cache) | |

Stats time is now round trips, not bytes: thumbnail lookups and mosaic data, each on
a new connection, plus the 77 ms summary query (batch 5). Keep-alive is the structural
fix. Not converted in this batch: the "All rigs" merged Sessions view
(`renderSessionsAllRigs`) still loads extras for every card up front.

Tests: target-thumb endpoint (versioned URL, bytes, immutable cache, rig param,
regeneration changes the version, bad index 404). 80 tests pass on macOS, including the
existing `DashboardServerReadOnlyTests` and `TnsEndpointsTests`.

### Batch 5 (non-visual): `feature/perf-polish`

- `/api/filters` and `/api/stats/summary` memoized on the DB + WAL file stamp, per rig
  DB path: ~78 ms -> ~0.5 ms, outputs identical.
- Companion post-sync refresh gated on actual change: the view's data is re-fetched and
  compared; unchanged syncs do nothing. Changed syncs refresh without the fade-out and
  keep scroll (the old restore helper measured `.shell`, which only scrolls in report
  view, so sync refreshes always jumped to the top). Report caches are dropped only when
  the sync touched report files.
- Images the first-load reveal waits on are switched to eager (background tabs don't
  run the lazy loader; the reveal stalled to its 5 s safety timeout).
- Legacy header icon swapped for the small one when serving older reports (read-side
  only): large report 2.46 MB -> 1.69 MB, identical apart from that tag.
- `reportVersion` in the session detail + `/report?v=` served immutable: re-opening an
  unchanged report needs no request. Unversioned URLs keep ETag revalidation.

## Final results vs targets (iPad profile: 60 ms RTT, 20 Mbps)

| What | Baseline (`dev`) | Final | Target | |
|---|---:|---:|---:|---|
| Sessions list visible, cold | 3.13 s | 0.78 s | < 0.5 s | missed (see note) |
| Sessions first load settled | 2.97 s, 80 req, 2.5 MB | 0.78-0.98 s, 9-23 req, 250-370 KB | < 1 s | met |
| Repeat dashboard open (page) | 0.49 s, 802 KB | 0.15 s, 300 B | < 0.2 s | met |
| Repeat open, list visible | ~3 s | 0.50 s, 29 KB total | | |
| Large report, first open | 1.50 s, 2.4 MB | 0.91 s, 1.0 MB | < 1 s | met |
| Report re-open | 1.43 s | 0.19 s, no report request | < 0.2 s | met |
| Targets edit | page blanks, 0.42 s, scroll to top | in place, 0.31 s, scroll kept, identical DOM | < 100 ms, scroll kept | scroll met; content 0.3 s |
| Stats first visit | 1.29 s, 1.8 MB | 1.16 s, 41 KB | | |
| `/api/sessions` (server) | 210 ms | 9 ms | | |

"List visible" note: the list data itself arrives at 0.41 s; the reveal then waits for
the on-screen cards' thumbnails and charts (two more sequential round trips, each on a
new TCP connection). What remains is round trips, not code or bytes. Options: HTTP
keep-alive in the TCP server (saves a handshake per request), or embedding the
on-screen cards' thumbnail URLs and chart data in `/api/sessions`. Both are bigger
changes; not done under the stopping rules.

Targets edit note: the acknowledgement (picker or modal closes) is instant; the card
content follows after two sequential round trips (save, re-read). Returning the
refreshed targets from the POST would save one (~0.12 s).

## Not done / next

- HTTP keep-alive (F4b): the biggest remaining lever for every screen; riskier (custom
  TCP server, export streaming, companion auth).
- "All rigs" merged Sessions view still loads every card's extras up front.
- Pass 2 (Windows dev PC): companion sync re-downloads the full NS and TS databases on
  every sync; report generation time; Live Stack embed size; WebView2 preview.
- Before merging any batch: `dotnet test` on Windows, and a look on a real iPad and
  the companion.

## Proposal: animations and blur (visual, needs a real iPad)

Nothing below is implemented; each item is a small CSS change to try on the iPad.

1. **Timing tokens.** `--t-medium` 450 ms -> ~280 ms and `--t-slow` 650 ms -> ~400 ms
   (dashboard.css:28-30). `--t-medium` drives card lift (`.session-card`, `.target-card`),
   thumbnail zoom (`.card-thumb-wrap`), the stat expand popup and labels; `--t-slow`
   only the integration bar. The live-stack shelf reveal (`shelf-reveal 450ms`) and the
   first-load list fade (`opacity 400ms`, dashboard.js) to ~250-300 ms. The curve
   (0.22, 1, 0.36, 1) is front-loaded, so shorter durations keep the soft landing.
2. **Hover delay.** `.card-thumb-wrap:hover` waits 150 ms before zooming (dashboard.css
   ~1187). Keep if it prevents accidental zooms while scrolling on desktop; otherwise
   ~60 ms.
3. **Layout-triggering transitions -> transform.** Progress fills
   (`.integration-bar`, `.targets-project-progress-overall-fill`, `.tdp-progress-bar-fill`,
   `.pdp-panel-progress-fill`) animate `width`; switch to `transform: scaleX()` with
   `transform-origin: left`. The tab thumb (`.stats-tab-thumb`) animates `left` + `width`;
   use `transform: translateX() scaleX()`. `transition: all` on `.tdp-project-action-btn`
   and `.ts-link-picker-item` -> list only the properties that change.
4. **Blur.** The sticky `header` blurs 14 px of the page scrolling under it, repainted on
   every scroll frame; the most likely iPad scroll-jank source. Option A: solid,
   slightly translucent background on touch/narrow screens, blur kept on desktop.
   Option B: drop to 6 px everywhere. `.target-card-ts-badge` has its own 6 px blur on
   every target card (~30 compositing layers on Stats); a solid badge background would
   look almost identical. Modal backdrops (TDP, PDP, link picker, lightbox) blur only
   while open over still content; leave them.

Suggested way to judge it: implement 1-4 on one branch, open the dashboard on the iPad
side by side with the current build, scroll Sessions and Stats, open a few cards.
