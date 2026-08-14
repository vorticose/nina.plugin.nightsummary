# "All rigs" merged Sessions view — handoff doc

Status as of 2026-08-12. Written for another agent picking this up mid-stream.
The work is committed (`d8e0894`) on `claude/new-session-57a9c4`, rebased
onto `origin/dev` — see "Branch and commit status" at the bottom. Not
pushed.

## TL;DR

A user with the multi-rig companion asked for an "All" option in the rig
selector to see every rig's sessions in one merged view, drilling into a
specific rig when wanted. We designed it interactively (static mockup ->
real dev-server prototype using the user's actual data) and landed a working
version in the real dashboard: the rig switcher now has an "All rigs" option
that shows a merged, date-grouped session list with cross-rig cumulative
stats and a per-rig "latest session" snapshot row. Single-rig behavior is
byte-for-byte unchanged.

**What's missing before this is a real, shippable feature**: filtering/sort/
pagination in merged mode, hide-session, live-stack badges, FOV overlay, and
real (non-duplicated) multi-rig data to validate against. See "Not done yet".

## Why (user's own words, paraphrased across the conversation)

- Original ask: "an option in the companion selector of 'all' to show all of
  them in a single view... then if I want to drill down into a specific rig,
  I can just choose that rig."
- Scoped by the user to: aggregate the session view, and "perhaps" an
  aggregate stats view. Explicitly estimated as serving <5% of the user base
  (most people run one rig) — so the guidance throughout was "cheap and
  low-risk over exhaustive."
- Refined over several rounds:
  1. "Maintain as much of the UI as possible from the standard view... group
     under [a shared] day but show the cards for both, labeled for each rig"
     — only when rigs actually overlap on the same night; solo nights should
     look untouched.
  2. "I want the full card for each rig, including thumbs and altitude
     chart... stacked vertically and grouped by date" — not a shrunk 2-up
     grid.
  3. "Could we do a full mock on the dev server using realistic data? ...
     duplicate my single rig and show it twice." — moved from a static HTML
     mockup to a live dev-server prototype against the user's real session
     DB.
  4. "Rig dropdown should have All as the first option, then each rig
     individually. Stats overview block should reflect what's selected,
     including a cumulative block if all is selected." — wired into the
     *real* rig switcher and Sessions tab, not a side prototype route.
  5. "Gold highlight on the most recent session from each rig, even on
     different dates" — two rigs at one site usually share weather and land
     on the same night; two rigs at different sites (or backyard vs remote)
     can diverge a lot. Wanted a snapshot of "the most relevant info for
     each rig" regardless of date.

## Design decisions and why (read before changing behavior)

- **Single-rig view is untouched.** Every change is gated behind
  `ACTIVE_RIG === 'all'`. This was a hard requirement from round 1 — a
  multi-rig user picking one specific rig should see literally the same UI
  that shipped before this work. Verified via `.claude/worktrees` browser
  testing — see "Bugs found" below for a subtle regression this nearly
  caused.
- **Client-side merge, not a server aggregation endpoint.** `renderSessionsAllRigs`
  fetches `/api/sessions?rig=<id>` once per enabled rig and merges/groups in
  JS. No new DashboardServer endpoints. Chosen because: (a) the existing
  `IRigRegistry` / `?rig=` per-request scoping already makes every endpoint
  rig-addressable, so N explicit fetches is trivial; (b) it keeps 100% of
  the blast radius in dashboard.js/css, zero risk to the server's read path;
  (c) matches the "<5% of users, keep it cheap" framing.
- **Grouping is date-driven, not rig-driven.** A night with sessions from
  only one rig renders as a completely standard `.session-card` (same
  markup as single-rig mode) plus a small rig-colored chip in the corner. A
  night where >1 rig has a session gets a shared `.allrigs-day-label` date
  header once, with each rig's **full** card (thumbs + altitude chart +
  stats, not a stripped-down summary) stacked vertically underneath — full
  width, not a shrunk side-by-side grid (explicitly rejected in round 2).
- **The lifetime stats strip is reused verbatim.** `renderLifetimeStrip(sessions)`
  was already a pure reducer over a plain session array — passing it the
  merged multi-rig array gives correct cumulative Sessions/Images/Integration
  sums, *and* the Targets count deduplicates by name for free (it's keyed by
  target name, not by (rig, name)), because the function was never touched.
  This resolved the "does the same target on two rigs count once or twice"
  design question implicitly, in the direction that reads correctly.
- **"Latest" is per-rig, not global, and date-independent.** Each rig's own
  most-recent session (by `sessionStart`, plain string comparison since it's
  ISO 8601) gets pulled into a pinned row at the top with the gold
  `.session-card--latest` / `.latest-label` treatment reused verbatim from
  the original single-rig hero card. These pinned cards are excluded from
  the chronological list below so nothing appears twice. This means: if two
  rigs' latest sessions land on the same night, you get two gold cards
  showing the same date; if they diverge, you get two gold cards on two
  different dates. Both are correct and expected — this was the whole point
  per the user's use case (shared-weather rigs vs. different-site rigs).
- **"All rigs" is a client-only pseudo-rig id, never sent to the server.**
  `ACTIVE_RIG` can now be `'all'`, but `rigParamActive()` explicitly excludes
  it, so the `withRig()` auto-patch (which appends `?rig=<ACTIVE_RIG>` to
  every `/api/*` fetch) becomes a no-op whenever `'all'` is active — any code
  path *other* than the ones explicitly rewritten for merging (Sessions tab)
  silently resolves server-side to the registry's Default rig. This is
  intentional graceful degradation, not a bug: Targets, Tonight, Settings,
  and the standalone Stats page are **not** rig-aware yet and nobody
  designed what "All" should mean for them.

## What's implemented (files touched)

All changes are in this worktree, uncommitted. No production C# server code
(`DashboardServer.cs` etc.) was touched — everything is either dev-harness
tooling or dashboard.js/css.

### `tools/dev-dashboard-cs/DevFakeMultiRigRegistry.cs` (new file)

Dev-only `IRigRegistry` implementation. Wraps N `RigBackend`s that all point
at the *same* `IDashboardDataSource`/`IDashboardPaths` instances — i.e. "show
my one real rig N times under different labels," with zero data duplication
on disk. `SupportsManagement => false`; Add/Remove/Enable/Rename all throw
`NotSupportedException`, matching the existing `SingleRigRegistry` pattern.

### `tools/dev-dashboard-cs/Program.cs` (modified)

Added `--fake-rigs N` (default 0 = off). When `N >= 2`:
- Forces companion mode on (`settings.Mode = "companion"`, wires
  `DevStubCompanionController` + `DevCompanionRegenerator`) even without
  `--companion-mode`, so the real switcher/banner render.
- Builds N `RigBackend`s named "Rig A", "Rig B", ... (ids `rig-a`, `rig-b`,
  ...), all sharing the one configured `--db`/`--data`/`--reports`.
- Uses `DashboardServer`'s multi-rig constructor
  (`new DashboardServer(rigs: new DevFakeMultiRigRegistry(backends), ...)`)
  instead of the single-rig one.
- Usage text updated to document the flag.

### `NINA.Plugin.NightSummary.Dashboard/Web/dashboard.js` (modified, ~209 lines added)

Existing functions changed:
- `rigParamActive()` (~L286) — excludes `ACTIVE_RIG === 'all'` so `withRig()`
  never sends a literal `?rig=all` to the server.
- `initCompanionBanner()` (~L9671) — (a) accepts a stored `'all'` value when
  resolving `ACTIVE_RIG` from localStorage; (b) after settling
  `ACTIVE_RIG`/`DEFAULT_RIG`, calls `route()` again if they differ. Needed
  because the very first `route()` call at script init runs *before*
  `/api/mode` resolves and paints unscoped (Default-rig) data — see "Bugs
  found" for the second half of this fix.
- `renderRigSwitcher()` (~L9722) — prepends `<option value="all">All rigs</option>`
  before the per-rig options.
- `switchRig(id)` (~L9743) — allows `id === 'all'` past the "is this a known
  rig id" guard.
- `renderSessionList(params)` (~L4117) — one new guard clause at the very
  top: `if (ACTIVE_RIG === 'all') { renderSessionsAllRigs(el, sub); return; }`.
  Everything else in this function is untouched. Also, further down, the
  original unscoped `/api/sessions` fetch's `.then()`/`.catch()` now bail
  early if `ACTIVE_RIG === 'all'` by the time they resolve (stale-response
  guard, see "Bugs found").

New functions/state, all added together (~L4181-4327):
- `var RIG_COLORS` / `rigColor(rigId)` — small fixed palette (violet, teal,
  amber, pink, blue), indexed by the rig's position in `RIGS`, used for chip
  color and the rig-as-header-label color in grouped cards.
- `allRigsDrill(rigId, sessionId)` — click handler for merged-view cards:
  calls `switchRig(rigId)`, then manually syncs the `<select>`'s displayed
  value (see "Bugs found"), then `navigate('#/sessions/' + sessionId)` to
  open the real report, now correctly rig-scoped.
- `buildRigSessionCard(rig, s, showRigAsHeader, isLatestForRig)` — builds one
  full `.session-card` (thumbs container + stat boxes + altitude container,
  DOM ids namespaced `rigId__sessionId` to avoid collisions across rigs
  sharing session data in the dev fixture). `showRigAsHeader` swaps the
  date-in-header for a colored rig-name-in-header (grouped-night mode).
  `isLatestForRig` adds the gold class + label and forces date-in-header
  (never rig-name-in-header) plus the rig chip, since latest cards always
  need their own date shown.
- `hydrateRigSessionCard(rig, s)` — fetches
  `/api/sessions/{id}/thumbnails?rig={rigId}` and
  `/api/sessions/{id}/altitude-chart?rig={rigId}` explicitly (bypasses the
  global `ACTIVE_RIG`-keyed `loadThumbnails`/`loadAltitudeCharts`/caches
  entirely, since those are hardcoded to DOM-id-by-sessionId and would
  collide when the same session appears under two rigs). Reuses
  `setupThumbsScrollMode`, `fixChartTextDistortion`, `applyChartPullUp` from
  the existing single-rig code for visual parity.
- `renderSessionsAllRigs(el, sub)` — the main entry point. Fetches every
  enabled rig's sessions in parallel, computes each rig's latest session,
  renders: lifetime strip (merged array) -> pinned latest-per-rig cards ->
  `"All sessions"` section label (only if there's anything left) ->
  date-grouped chronological list of everything else. Hydrates all cards
  after insert.

### `NINA.Plugin.NightSummary.Dashboard/Web/dashboard.css` (modified, ~39 lines added)

New rules, all prefixed `.allrigs-*`, added near the existing `.session-card`
block: `.allrigs-chip`, `.allrigs-section-label`, `.allrigs-day-group`,
`.allrigs-day-label`, `.allrigs-day-stack`. Everything else (the gold glow,
`.latest-label`, `.card-thumbs`, `.card-altitude`, etc.) is reused verbatim
from the existing single-rig styles — nothing was duplicated or forked.

### `.claude/launch.json` (local, gitignored — not part of any commit)

The `dev-dashboard` entry was pointed at Windows paths from a different
worktree (stale template copy). Rewritten for this Mac worktree: runs via
`dotnet exec` (see "Bugs found" — the native apphost fails here) with
`--fake-rigs 2` and real `/Users/evan/Documents/ns-snapshot/*` paths.

## Bugs found and fixed along the way (don't rediscover these)

1. **Mac apphost can't find the .NET runtime.** Running the built
   `nightsummary-dev-dashboard` binary directly fails with "You must install
   .NET... Failed to resolve libhostfxr.dylib" even though `dotnet` works
   fine on PATH — the native apphost's own runtime probing doesn't know
   about `/Users/evan/.dotnet`. Fix: launch via
   `dotnet tools/dev-dashboard-cs/bin/Release/net8.0/nightsummary-dev-dashboard.dll ...`
   instead of the apphost. `.claude/launch.json` already reflects this.
2. **`preview_start`'s process wrapper silently never bound the port** for
   this binary (the `disclaimer` helper process spawned it but nothing ever
   listened on 8183, no logs, no error). Direct `Bash` with
   `run_in_background: true` worked reliably. Not root-caused further —
   worth a look if `preview_start` is preferred going forward.
3. **The whole dashboard is one inlined `<script>` in the served HTML** —
   there's no real standalone `/dashboard.js` URL serving live content (a
   `curl .../dashboard.js` hits something else/stale and is useless for
   verification). More importantly: **hash-only navigation
   (`location.hash = '#/...'`) does NOT re-fetch or re-execute the inline
   script**, so editing dashboard.js and then just changing the hash in an
   already-loaded tab will keep running the *old* JS. A full page
   reload/navigate is required after every dashboard.js edit to see it take
   effect. Cost real time twice in this session before being diagnosed.
4. **First-paint race**: the boot sequence is `route(); initCompanionBanner();`
   — `route()` runs synchronously first, *before* `/api/mode` has resolved,
   so it always paints as if single-rig/unscoped (server resolves to
   Default rig). If the eventually-settled `ACTIVE_RIG` differs from
   `DEFAULT_RIG` (a stored non-default rig, or now `'all'`), that first
   paint is simply wrong and nothing corrected it before this session. Two
   part fix, both required together:
   - `initCompanionBanner()` now calls `route()` again once `ACTIVE_RIG` is
     known, if it differs from `DEFAULT_RIG`.
   - The *original* unscoped `/api/sessions` fetch kicked off by that first
     `route()` call is still in flight and will resolve eventually — if it
     resolves *after* the corrected re-render, it would silently clobber
     the correct content with stale single-rig data. Its `.then()`/`.catch()`
     now bail if `ACTIVE_RIG === 'all'` by the time they fire. (This exact
     race could in principle also bite a stored non-default *specific* rig
     id, not just `'all'` — that pre-existing gap was not separately
     hardened beyond the `route()` re-run, since it never visibly broke
     anything before merged mode existed and wasn't in scope to chase down
     further.)
5. **`switchRig()` never syncs the `<select>` element itself** — it only
   updates the `ACTIVE_RIG` JS variable and re-renders content. In normal
   use this is invisible because the `<select>`'s own native `onchange`
   fires *after* the browser has already updated its displayed value. But
   `allRigsDrill()` calls `switchRig()` programmatically from a card click,
   bypassing that — so it has to set `document.getElementById('companion-rig-select').value = rigId`
   itself, or the pill shows the rig you left, not the one whose report
   you're now looking at.

## Not done yet / explicitly out of scope

- **No filter bar, sort, date-range, target filter, "show empty", FOV
  toggle, or pagination in merged mode.** `renderSessionsAllRigs` renders
  everything, always, unsorted-by-user-choice (fixed date-desc). The
  single-rig view's `doRenderList`/`renderSessionsV2` machinery for all of
  this was deliberately not touched or replicated — real scope decision
  needed on how much of it merged mode should get.
- **Hide-session (✕ button) is not wired** for merged cards at all —
  `buildRigSessionCard` doesn't emit a hide button, and `hiddenSessions`
  state isn't consulted or applied in `renderSessionsAllRigs`.
- **Live Stack badges are not wired** — `loadLiveStacks` is never called for
  merged cards, so no live-stack hover shelf appears even if data exists.
- **FOV overlay on thumbnails is not wired** — `hydrateRigSessionCard`'s
  thumbnail rendering skips the `fovSvg` handling that `renderThumbnails`
  does for the single-rig view.
- **Targets, Tonight's Preview, Settings, and the separate Stats/lifetime
  page are not rig-aware for `'all'`.** They silently read the Default
  rig's data when "All rigs" is selected (see design decision above — this
  is intentional graceful degradation, not a crash, but nobody has decided
  what these pages *should* show in "All" mode).
- **Never tested against real divergent multi-rig data.** The dev fixture
  (`--fake-rigs 2`) duplicates one real rig's DB under two labels — same
  session IDs, same dates, same everything. This proved the merge/group/
  gold-highlight *mechanics* work, but never actually exercised: a solo
  night with only one rig active, two rigs whose "latest" genuinely falls on
  different calendar dates, or any real weather/site divergence. The logic
  is date/rig-independent by construction and *should* handle this
  correctly, but it's reasoned, not observed. If a second real rig becomes
  available, or the harness gets extended to fake asymmetric data (e.g. a
  flag to cap one fake rig's visible session date range), re-verify here
  first.
- **No automated tests added.** Consistent with existing project convention
  — zero test coverage exists anywhere under `tools/dev-dashboard-cs/*`
  (checked: `DevDashboardDataSource`, `DevDashboardPaths`,
  `DevStubCompanionController` etc. are all untested too), so
  `DevFakeMultiRigRegistry` matches that precedent rather than being a gap.
  dashboard.js has no JS test suite at all (project-wide, not specific to
  this work).

## How to run and test it

Rebuild only needed if `tools/dev-dashboard-cs/*.cs` changes — dashboard.js/
css hot-reload from disk on every request (`DiskWebAssets.HotReload = true`,
verified: it truly re-reads the file every time, no caching layer) but
remember bug #3 above — you need a hard page reload in the browser, hash
navigation isn't enough.

```bash
# Only if Program.cs / DevFakeMultiRigRegistry.cs changed:
dotnet build tools/dev-dashboard-cs/DevDashboardHost.csproj -c Release

# Launch (Mac — use dotnet exec, not the apphost binary directly, see bug #1):
/Users/evan/.dotnet/dotnet tools/dev-dashboard-cs/bin/Release/net8.0/nightsummary-dev-dashboard.dll \
  --port 8183 --host + \
  --db /Users/evan/Documents/ns-snapshot/nightsummary.sqlite \
  --data /Users/evan/Documents/ns-snapshot \
  --reports /Users/evan/Documents/ns-snapshot/reports \
  --fake-rigs 2
```

Or `preview_start` with `name: "dev-dashboard"` (reads the fixed-up
`.claude/launch.json` in this worktree) — but see bug #2, it didn't reliably
bind for me; direct Bash was more dependable this session.

Open `http://localhost:8183/` (or the printed Tailnet URL). The rig dropdown
in the companion banner will show "All rigs", "Rig A", "Rig B". Pick "All
rigs" to see the merged view; pick a specific rig to confirm the single-rig
view is unchanged from before this work.

Real session data lives at `~/Documents/ns-snapshot/` (Evan's real dev
snapshot, synced from the observatory — see the `reference_dashboard_dev_harness`
memory for how to refresh it). No `thumbs/` dir exists in this snapshot as of
writing, so raw-thumbnail images won't render, but altitude charts and stats
are fully real.

## Branch and commit status — read before doing anything else

- Worktree: `.claude/worktrees/companion-app-issues-616b84`
- Branch: `claude/new-session-57a9c4`
- **Originally cut from `main` at the v3.3.0 release tip (`2c3302e`), not
  from `dev`** — same worktree-base mistake CLAUDE.md's Multi-Agent Rule
  warns about. **Fixed 2026-08-12**: rebased with
  `git rebase --onto origin/dev 2c3302e claude/new-session-57a9c4` (clean,
  no conflicts). The branch now sits directly on `origin/dev`'s tip and
  carries only its own commit — none of the release-only commits it had
  picked up from `main`. Verified: `git diff --stat origin/dev
  claude/new-session-57a9c4` shows exactly the 5 files this doc describes,
  681 insertions / 20 deletions, nothing extra.
- **Committed** as `d8e0894` ("feat(dashboard): prototype "All rigs" merged
  Sessions view"). **Not pushed** — still local-only to this worktree/machine.
  If picking this up in a genuinely different worktree or machine, fetch/pull
  this branch across first (or ask for it to be pushed) — it won't exist
  anywhere else yet.

## Suggested next steps, roughly in order

1. Decide whether/when to push `claude/new-session-57a9c4` and open a PR
   into `dev`, or keep iterating locally first.
2. Decide the merged-mode scope for filter/sort/pagination — even a
   stripped-down version (e.g. just a date-range filter) may be worth more
   than nothing once real multi-rig usage starts.
3. Wire hide-session and Live Stack badges into `buildRigSessionCard`/
   `hydrateRigSessionCard` if they're wanted — both are small, mechanical
   additions following the existing single-rig code as a template.
4. Decide what (if anything) Targets/Tonight/Settings/Stats-page should do
   in "All rigs" mode, or explicitly document that they intentionally stay
   Default-rig-scoped for now.
5. If/when a genuine second rig (or asymmetric fake data) is available,
   re-verify the cross-date "latest per rig" behavior visually — it's only
   been verified by reasoning + identical-data structural checks so far.
