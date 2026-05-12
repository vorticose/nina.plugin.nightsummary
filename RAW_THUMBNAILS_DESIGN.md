# Raw Image Thumbnails — Design Doc

**Status:** Design (not implemented)
**Branch target:** v3-dev (feature, not 2.x bugfix)
**Authors:** ep, claude
**Last updated:** 2026-05-07

## Motivation

User request: view single raw images per session, like the **Web Session History
Viewer** (WSHV, by `@tcpalmer`) plugin offers. Initial spike showed Target
Scheduler (TS) already stores 192px JPEG thumbs in its `imagedata` table, but
**TS-only is too narrow** — non-TS sequences (manual capture, SGP-imported, etc.)
have no thumbs. Need a generic capture path so all NS users get this regardless
of whether they run TS.

This doc covers the generic capture mechanism, file/DB layout, the three
viewing modes (per-session, per-target, per-project), retention, and an
optional one-shot import from TS for existing users.

## Non-Goals

- Full-res FITS rendering in the dashboard (WSHV does this with server-side
  stretch). Out of scope for v1 — `_md` 800px thumb is the "lightbox" replacement.
- NS-native project/grouping concept. Project view leans on TS DB; without TS,
  show the per-target view.
- Cloud sync / public dashboard (separate Phase 2 work).

## Capture

### Where

`SessionCollector.OnImageSaved` (already wired to
`IImageSaveMediator.ImageSaved`). Thumb generation is a new branch inside the
existing handler — **only when `ImageType == LIGHT`** and the user has the
toggle on.

### How (copy TS pattern)

Reference: `nina-ts-source/NINA.Plugin.TargetScheduler/Utils/Thumbnails.cs`
+ `Sequencer/ImageSaveWatcher.cs`. TS itself cribbed from Lightbucket. Battle-
tested code, has been working in production for years.

```csharp
public static (int w, int h, byte[] data) CreateThumbnail(BitmapSource src, int targetHeightPx, int quality) {
    double scale = (double)targetHeightPx / src.Height;
    var bmp = new TransformedBitmap(src, new ScaleTransform(scale, scale));
    int w = (int)bmp.Width, h = (int)bmp.Height;
    var enc = new JpegBitmapEncoder { QualityLevel = quality };
    enc.Frames.Add(BitmapFrame.Create(bmp));
    using var ms = new MemoryStream();
    enc.Save(ms);
    return (w, h, ms.ToArray());
}
```

Source: `imageSavedEventArgs.Image` (`IRenderedImage`) — already auto-stretched
by NINA's render pipeline. Matches what user saw in the Imaging tab.

### Threading

TS encodes **inline on the save thread**, no marshaling, no background queue.
Works in production. Follow suit. If profiling later shows pipeline lag, can
move to `Channel<EncodeJob>` + background writer task.

### Sizes

Two output sizes, both gated by settings:

| Tag    | Height | Quality | ~Size  | Use         |
|--------|--------|---------|--------|-------------|
| `_sm`  | 192px  | q85     | ~15 KB | Grid view   |
| `_md`  | 800px  | q85     | ~80 KB | Lightbox    |

`_sm` is the always-on case when capture is enabled. `_md` is a separate
toggle (off by default — disk hog opt-in).

## Settings

Added to `NightSummarySettings` (and synced to `Options.xaml`,
`SessionService.cs` sidecar JSON, dashboard `buildSettingsPanel`/form-collect
per the **three-places-in-sync** rule in CLAUDE.md):

| Setting                          | Type   | Default  | Notes                                              |
|----------------------------------|--------|----------|----------------------------------------------------|
| `CaptureRawThumbnails`           | bool   | **off**  | Master toggle — gates everything                   |
| `CaptureMediumThumbnails`        | bool   | off      | Adds `_md` 800px on top of `_sm`                   |
| `ThumbnailRetentionMode`         | enum   | KeepAll  | `KeepAll` \| `RolloverByDays` \| `RolloverByGB`    |
| `ThumbnailRetentionDays`         | int    | 90       | Used when mode = `RolloverByDays`                  |
| `ThumbnailRetentionMaxGB`        | double | 5.0      | Used when mode = `RolloverByGB`; LRU by session    |

**Master toggle off by default** — opt-in feature. Avoids surprising existing
users with new disk usage on upgrade.

## File Layout

Single canonical store. No fs duplication for the three views — DB joins do
the routing.

```
%LOCALAPPDATA%\NINA\NightSummary\thumbs\
  {sessionId}\
    {imageId}_sm.jpg
    {imageId}_md.jpg          # only when CaptureMediumThumbnails=true
```

Why flat-by-session:
- Directory size bounded (~100–500 frames/session) — fs handles fine
- Session delete = `rmdir /s` — atomic cleanup
- LRU retention by session = walk top-level dirs by mtime, delete oldest
- Backup-friendly (per-session zip)
- Path is **derivable from `(sessionId, imageId, size)`** — no need to store
  full path in DB

## DB Schema

Add to NS image table (e.g. `Image` or whatever the row is named — confirm at
implementation):

| Column              | Type | Nullable | Purpose                                          |
|---------------------|------|----------|--------------------------------------------------|
| `ThumbnailVersion`  | INT  | yes      | Bitmask: 1=`_sm`, 2=`_md`, 3=both, NULL/0=none   |
| `FilePath`          | TEXT | yes      | Persisted FITS path (currently in-memory only)   |

`FilePath` add: not strictly required for thumbs (we don't re-stretch FITS) but
cheap to add now and unblocks future "open in viewer" / re-stretch features.
Currently `_pathToTimestamp` is in-memory only.

Indexes:
- `idx_image_target` on `(TargetName COLLATE NOCASE)` — for cross-session
  per-target view

Schema bump: increment NS DB schema version, add migration step.

## Three Viewing Modes

| Mode         | DB Query                                            | Index    |
|--------------|-----------------------------------------------------|----------|
| Per session  | `WHERE SessionId=?`                                 | exists   |
| Per target   | `WHERE TargetName=? COLLATE NOCASE`                 | new      |
| Per project  | TS DB → project GUID → target name list → above     | none new |

### Project view caveat

NS has no native project concept. Project view is **TS-mediated**:
1. Read TS `project` table for project GUID + name + target names
2. Map target names back to NS image rows
3. Hide tab entirely if `TargetSchedulerDatabase.IsAvailable` is false, or
   show empty-state with "Install Target Scheduler to group by project"

Future (post-v1): NS-native tagging/grouping for non-TS users. Out of scope.

## Endpoints

```
GET /api/sessions/{sid}/frames                  # list w/ thumb metadata
GET /api/frames/{imageId}/thumb?size=sm|md      # binary JPEG
GET /api/targets/{name}/frames                  # cross-session
GET /api/projects/{guid}/frames                 # TS-mediated; 404 w/o TS
```

`/thumb` response:
```
Content-Type: image/jpeg
Cache-Control: public, max-age=31536000, immutable
```

Thumbs are content-addressed (never mutate post-capture) → aggressive caching
is safe. ETag based on `(imageId, size)` for invalidation if regen ever needed.

`{imageId}_md.jpg` 404 when `CaptureMediumThumbnails` was off at capture time
— dashboard falls back to `_sm` upscaled.

## Retention

Run on `SessionService.OnSessionEnd` and on app startup (catch sessions that
crashed). Modes:

- **KeepAll** — no-op
- **RolloverByDays** — delete `thumbs/{sid}/` where session.startTime <
  `now - retentionDays`. Walk session table, not fs (avoid orphans).
- **RolloverByGB** — sum `thumbs/*` dir sizes, sort by session start desc,
  pop until under cap, delete the popped sessions' dirs. LRU by recency.

DB doesn't need to know thumbs are gone — `ThumbnailVersion` stays set,
`/thumb` endpoint returns 404 if file missing, dashboard shows placeholder.

Optionally: clear `ThumbnailVersion` on cleanup to reflect truth. Decide at
implementation — flag this in the impl ticket.

## TS Historical Import

Optional one-shot job for users who had TS running before this feature
shipped. Pulls existing TS `imagedata` blobs into NS's thumb store.

### Trigger

Manual button in Options: **"Import thumbnails from Target Scheduler"**.
Greyed out if TS DB unavailable.

### Algorithm

1. Open TS DB read-only (existing `TargetSchedulerDatabase` already does this)
2. For each NS image row where `ThumbnailVersion IS NULL`:
   - Match TS `acquiredimage` row by `(profileId, targetName, filterName,
     acquireddate ≈ imageStartTime)` — same matching used for grading
   - If TS has `imagedata` blob: write to `thumbs/{sid}/{imageId}_sm.jpg`,
     set `ThumbnailVersion = 1`
3. Report counts: imported / skipped (no match) / failed

### Caveats

- TS thumb is q100, not q85 — they'll be slightly larger than native captures
  (~30 KB vs ~15 KB). Acceptable, only path-difference is one-time.
- TS only stored thumbs for LIGHT frames it processed → matches our LIGHT-
  only scope.
- Timestamp tolerance: ±2s match window (TS `acquireddate` is image start;
  NS `Timestamp` is also image start in event-collector code). Confirm at
  impl time.
- No `_md` import (TS has only one size). User opts into `_md` going forward
  if they want.

### Why not auto-import?

- Disk-usage surprise (could be hundreds of MB)
- TS-DB read on every startup is wasteful for users who don't care
- Explicit button = explicit consent

## Phase 2 Alignment

Per `project_phase2_principles.md`:

- Disk-cache pattern ports cleanly to S3:
  `s3://bucket/thumbs/{sid}/{iid}_sm.jpg` — same key scheme
- DB stores `ThumbnailVersion` (bitmask), not full URL → backend swap is
  one config change
- Read endpoint already abstracts file location (handler decides local fs
  vs S3 fetch) → no schema change for cloud port
- TS import is a one-shot tool, runs on local box — no cloud implications

## Open Questions / Decisions Pending

1. **DB column name** — `Image` table is the assumption; confirm at impl. May
   be `ImageRecord` or similar.
2. **Cleanup-vs-truth tradeoff** — does retention cleanup also clear
   `ThumbnailVersion`, or leave it stale? Lean toward clearing for honesty.
3. **Dashboard UI design** — grid layout, lightbox component, target/project
   tab placement. Defer to UI spike when picking this up.
4. **Encoding format** — JPEG q85 chosen to match WSHV-ish sizes. WebP would
   be ~30% smaller but adds platform encoder dep risk. Stick with JPEG.
5. **Re-encode failures** — log to NS log, mark image row with
   `ThumbnailVersion = 0` (capture attempted, none succeeded), don't retry.

## Estimated Scope

Claude-execution time, broken down (per `feedback_claude_velocity_estimates`):

- Capture path + settings (3-places-sync) + DB schema bump: ~30 min
- Endpoints + static serving + cache headers: ~20 min
- Retention engine + tests: ~30 min
- TS import job + Options button: ~30 min
- Dashboard UI (grid + lightbox + 3 tabs): ~1 hr
- Tests (FilterHelper-style for capture decisions, retention math, import
  matcher): ~30 min

Total: **~3 hours** end-to-end. Single sitting if uninterrupted, two if
broken across UI vs backend.

## Files Touched (anticipated)

- `NINA.Plugin.NightSummary/Util/Thumbnails.cs` — **new**, copied TS pattern
- `NINA.Plugin.NightSummary/Session/SessionCollector.cs` — capture hook
- `NINA.Plugin.NightSummary/NightSummaryPlugin.cs` — settings properties
- `NINA.Plugin.NightSummary/Options.xaml` — settings UI
- `NINA.Plugin.NightSummary/Session/SessionService.cs` — sidecar settings
  serialization (~L568)
- `NINA.Plugin.NightSummary/Data/SessionDatabase.cs` — schema migration,
  `ThumbnailVersion`/`FilePath` columns, retention queries
- `NINA.Plugin.NightSummary/Data/TargetSchedulerDatabase.cs` — extend with
  `GetImageDataBlob(imageId)` for import
- `NINA.Plugin.NightSummary.Dashboard/Server/DashboardServer.cs` — endpoints
- `NINA.Plugin.NightSummary.Dashboard/Web/dashboard.js` — UI
  (`buildSettingsPanel` + new gallery component)
- `NINA.Plugin.NightSummary.Tests/ThumbnailsTests.cs` — **new**
- `NINA.Plugin.NightSummary.Tests/RetentionTests.cs` — **new**
- `NINA.Plugin.NightSummary.Tests/SessionDatabaseTests.cs` — round-trip
  asserts for new columns

## Risks

- **Disk space surprise** — mitigated by off-by-default + retention options
- **Save-thread perf** — TS proves it's fine; if not, queue + bg writer
- **Frozen BitmapSource** — TS code path works on save thread; verify on
  re-rendered images that haven't been Freeze()'d yet
- **Schema migration failures** — follow existing migration pattern in
  `SessionDatabase.cs`, test on production-shape DB before merge

## When We Pick This Up

1. Re-read this doc
2. Confirm decisions on Open Questions §
3. Cut feature branch from `v3-dev`: `feature/raw-thumbnails`
4. Start with `Thumbnails.cs` + capture path (smallest unit, end-to-end
   testable with one frame)
5. Add settings/sync per CLAUDE.md three-places rule before adding more
   surface area
6. UI last — backend should be working with curl/Postman first
