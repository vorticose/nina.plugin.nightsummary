# Live Stack Thumbnail Integration — Research & Planning

## Feature Request

Multiple users (3+) have requested that the "finished" live stacked image for each
target appear in the Night Summary report. Users run the Live Stack plugin throughout
the night and want to see what they actually captured, not just the DSS survey image.

---

## Source Analysis: Live Stack Plugin

**Repo:** https://github.com/isbeorn/nina.plugin.livestack
**Local clone:** C:\Users\Evan\Documents\nina.plugin.livestack

### How Live Stack Exposes Data

1. **Message Broker Broadcast** (preferred integration point)
   - Topic: `Livestack_LivestackDockable_StackUpdateBroadcast`
   - Payload: `LivestackBroadcast` containing `LiveStackBroadcastContent`
     - Monochrome: stack count, filter, target, `BitmapSource StackImage`
     - Color (OSC): R/G/B stack counts, combined RGB `BitmapSource`
   - Fires after every frame is stacked — last one received = final stack
   - Status topic: `Livestack_LivestackDockable_StatusBroadcast` (Running/Stopped)

2. **Disk Files** (alternative)
   - Stacked FITS written to `{WorkingDirectory}/stacks/{Target}-{Filter}.fits`
   - Would require knowing Live Stack's working directory
   - FITS parsing adds complexity

3. **In-Memory** (internal only)
   - `LiveStackBag.Stack` — float[] array, not publicly accessible
   - Tabs hold `BitmapSource` for display

### Approach Comparison: Broadcast vs File-Based

**File-based (read saved stacks from disk at report time):**
- Per-filter stacks saved to `{WorkingDirectory}/stacks/{Target}-{Filter}.fits` (FITS format)
- Color composites saved to `{WorkingDirectory}/stacks/{Target}-RGB.png` (PNG format)
- Atomic write pattern (writes .tmp then moves) — safe to read at any time

Dealbreakers:
- **Autosave is OFF by default** — user must enable `SaveStackedLights` in Live Stack
  settings. We'd need to instruct users to enable a setting in a different plugin.
- **Working directory** defaults to `Path.GetTempPath()`, stored in NINA's profile-specific
  plugin options — not easily discoverable cross-plugin.
- **FITS parsing required** for per-filter stacks — non-trivial dependency (libcfitsio or
  custom reader). Color composite is PNG (easy) but only exists if user manually created one.

**Broadcast-based (IMessageBroker subscription):**
- Works regardless of autosave setting — broadcasts fire every frame unconditionally
- BitmapSource is already rendered and stretched — just compress to JPEG
- No FITS parsing, no file discovery
- Topic is a string constant — no type references to Live Stack

Tradeoff: must hold images in memory during session. Mitigated by compressing to
JPEG byte[] immediately on receipt (~150-300 KB each). 5 targets × 6 filters ×
300 KB = ~9 MB worst case across a full night. Acceptable.

### Decision: Message Broker Subscription

Broadcast approach wins decisively. File-based has two dealbreakers (autosave default
off, FITS parsing) and requires users to configure Live Stack specifically for NS.

---

## Current Report Layout (Per-Target Section)

```
┌─────────────────────────────────────────────────────────┐
│  Target Name — metadata line (times, coords, moon, etc) │
├─────────────────────────────────────────────────────────┤
│  [DSS Thumbnail]     [Altitude Chart                  ] │
│   200x200 px          fills remaining width             │
│   + SVG FOV box                                         │
├─────────────────────────────────────────────────────────┤
│  Filter Table (filter, images, exposure, total time)    │
├─────────────────────────────────────────────────────────┤
│  > Image Quality (collapsible)                          │
│  > Session History (collapsible)                        │
│  Target Scheduler Progress bars                         │
└─────────────────────────────────────────────────────────┘
```

Key constraints:
- DSS thumbnail: 200x200 CSS (400x400 fetched for retina)
- Altitude chart: flexible width, fills space right of thumbnail
- Both are in a flex row (`ts-target-header`)
- Either can be independently hidden via settings
- Report width varies (email clients, browser, Discord embed)

---

## Layout Options

### Option A: Below DSS thumbnail (stacked left column)

```
│  [DSS + FOV ]     [Altitude Chart                  ] │
│   200x200                                             │
│  [Live Stack]                                         │
│   200x200                                             │
```

Pros:
- All imagery grouped in the left column
- Minimal layout disruption
- Natural visual flow: "where I pointed" → "what I got"

Cons:
- Makes the header section taller (400px+ left column)
- Altitude chart doesn't fill the extra height well
- Asymmetric — left column much taller than right

### Option B: Dedicated row between header and filter table

```
│  [DSS + FOV ]     [Altitude Chart                  ] │
├───────────────────────────────────────────────────────┤
│  [Live Stack — wider/larger, maybe with label]        │
│   e.g. 400x300 or aspect-ratio preserved              │
```

Pros:
- More room for the live stack image (it deserves prominence)
- Doesn't distort the existing header layout
- Can show at native aspect ratio (astro images aren't always square)
- Room for a caption ("Live Stack · H: 47 frames · S: 38 frames")

Cons:
- Adds vertical space to every target section
- Might feel disconnected from the DSS thumbnail

### Option C: Replace DSS when live stack available

Pros:
- No extra space needed
- Clean swap

Cons:
- Loses framing reference (FOV box, survey context)
- Users without Live Stack see no change
- Two different things (reference vs result) — bad UX

### Option D: Overlay/toggle (interactive reports only)

Pros:
- Zero extra space

Cons:
- Email reports can't have interactivity
- Complexity for minimal gain

---

## Layout Decision: Full-Width Row (Option B, refined)

**Chosen layout:** The live stack image spans the full content width (760px),
placed as a dedicated row between the target header and the filter table.

```
┌─────────────────────────────────────────────────────────┐
│  Target Name — metadata line                            │
├─────────────────────────────────────────────────────────┤
│  [DSS + FOV]     [Altitude Chart                      ] │
│   200x200         flex:1                                │
├─────────────────────────────────────────────────────────┤
│  [Live Stack Image ─────────────────── 760px wide ────] │
│  height: native aspect ratio (e.g. ~507px for 3:2)     │
│  caption: "Live Stack · H: 47 frames · S: 38 frames"   │
├─────────────────────────────────────────────────────────┤
│  Filter Table                                           │
└─────────────────────────────────────────────────────────┘
```

Only rendered when live stack data exists for the target. No layout change when absent.

---

## Image Sizing & Compression

### Report container math
- `body { max-width: 800px; padding: 20px; }` → content area = 760px
- Target header: thumb 200px + gap 16px + chart flex:1
- Live stack row: full 760px width, aspect-ratio preserved height

### Resolution
- Render width: **760px** (CSS), source at **760px** (no retina upscale needed —
  astro images are noisy enough that 2x adds size without visible benefit)
- Height: native aspect ratio. Typical cameras:
  - 4:3 sensor → ~570px tall
  - 3:2 sensor → ~507px tall
  - 16:9 sensor → ~428px tall

### Compression budget
- Target per-image size: **100-300 KB** (JPEG quality 75-80)
- Astro images compress well: large dark sky regions are cheap in JPEG
- Worst case: 5 targets × 300 KB = 1.5 MB of live stack images
- Existing report baseline: ~200-500 KB (DSS thumbs ~10-20 KB each, SVG charts, HTML)
- Total with live stack: **~2 MB typical**, well under 5 MB ceiling

### Compression strategy
- Convert BitmapSource → JPEG at quality 75
- If resulting base64 exceeds 500 KB, re-encode at quality 60
- Two-pass approach keeps quality high when possible, caps outliers
- base64 encoding adds ~33% overhead: 300 KB JPEG → ~400 KB in HTML

---

## Display Mode Decision: OSC vs Mono

Live Stack's `ColorCombinationPrompt` confirms it supports color composites for mono
cameras too — auto-maps filters to RGB channels using string similarity:
- Broadband (R/G/B filters): R→Red, G→Green, B→Blue
- Narrowband 3+ filters: SHO palette (S→Red, H→Green, O→Blue)
- Narrowband 2 filters: H→Red, O→Green+Blue

### OSC cameras (one-shot color)
- **Default: color composite** — single full-width image
- This is what OSC users expect; they think in terms of one color image
- Live Stack produces this automatically via debayering

### Mono cameras
- **Default: per-filter grayscale stacks** — one image per filter, side by side
- This is what mono users expect; they think in terms of individual channels
- **Additional option: color composite** — shown alongside or instead of per-filter
  when the user has created one in Live Stack

### Layout with multiple filter stacks (mono default)

```
┌─────────────────────────────────────────────────────────┐
│  [DSS + FOV]     [Altitude Chart                      ] │
├─────────────────────────────────────────────────────────┤
│  [ H stack ]  [ S stack ]  [ O stack ]                  │
│   ~253px ea    ~253px ea    ~253px ea                    │
│   "H · 47"    "S · 38"    "O · 12"                      │
├─────────────────────────────────────────────────────────┤
│  Filter Table                                           │
└─────────────────────────────────────────────────────────┘
```

- Width per filter = (760 - gaps) / filter_count
- 2 filters: ~375px each (gap 10px)
- 3 filters: ~247px each (gap 10px)
- 4+ filters: may need to cap at 3 per row and wrap, or show most-imaged filters
- Each image labeled with filter name and frame count
- All images share the same height (native aspect ratio from sensor)

### Layout with color composite (OSC default)

```
┌─────────────────────────────────────────────────────────┐
│  [DSS + FOV]     [Altitude Chart                      ] │
├─────────────────────────────────────────────────────────┤
│  [Color Composite ──────────────── 760px wide ────────] │
│  "Live Stack · 47 frames"                               │
├─────────────────────────────────────────────────────────┤
│  Filter Table                                           │
└─────────────────────────────────────────────────────────┘
```

### Layout with per-filter + composite (mono, both always shown when available)

```
┌─────────────────────────────────────────────────────────┐
│  [DSS + FOV]     [Altitude Chart                      ] │
├─────────────────────────────────────────────────────────┤
│  [ H stack ]  [ S stack ]  [ O stack ]                  │
│   "H · 47"    "S · 38"    "O · 12"                      │
├─────────────────────────────────────────────────────────┤
│  [SHO Composite ───────────────── 760px wide ─────────] │
│  "SHO Composite"                                        │
├─────────────────────────────────────────────────────────┤
│  Filter Table                                           │
└─────────────────────────────────────────────────────────┘
```

Per-filter stacks shown first (raw data per channel), then the composite below
(combined result). Both always rendered when data is available.

### Compression with multiple images (mono)
- 2 filters × ~150 KB = ~300 KB per target (well within budget)
- 3 filters × ~100 KB = ~300 KB per target
- Smaller individual images compress better (less data per image)
- Total budget still comfortable: 5 targets × 300 KB = 1.5 MB

---

## Resolved Questions

1. **Setting** — DECIDED: Auto-show when Live Stack is detected and broadcasts are
   received. `ShowLiveStackImages` toggle in Options, default true. If Live Stack is
   not installed, grey out the option and force off (same pattern as TS API settings).

2. **Stretch/clip** — CONFIRMED: The BitmapSource from the broadcast is already
   auto-stretched via `ImageUtility.GetColorRemappingFilter()` in `LiveStackTab.Render()`.
   No stretch work needed on our end.

3. **Memory management** — DECIDED: Convert BitmapSource → JPEG byte[] immediately
   on receipt (~150-300 KB each). Discard BitmapSource. Worst case: 5 targets ×
   6 filters × 300 KB = ~9 MB held across a full night. Acceptable.

4. **Timing** — DECIDED: Hold latest broadcast per target/filter, overwriting as new
   frames arrive. Use whatever we have at report generation time. If Live Stack stopped
   mid-session or was never started for a target, skip the image row for that target.

5. **4+ filters** — DECIDED: Cap at 4 per row, wrap to second row if more.
   Common case: SHO primary + RGB stars = 6 filters = 4+2 rows.
   At 4 per row: ~185px each, still reasonable.

## Remaining Open Questions

None — ready for implementation planning.

---

## Implementation Sketch (rough)

1. New class: `LiveStackCapture` — subscribes to message broker, stores latest image per target
2. `SessionService` — passes captured images to `ReportGenerator` via `ReportData`
3. `ReportGenerator` — converts BitmapSource to base64 JPEG, renders in HTML
4. `NightSummarySettings` — add `ShowLiveStackThumbnails` (bool, default true)
5. `Options.xaml` — toggle in Report Display section
6. HTML/CSS — new element in per-target section based on chosen layout

No compile-time dependency on Live Stack plugin. Pure message broker integration.
