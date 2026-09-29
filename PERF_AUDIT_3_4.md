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
