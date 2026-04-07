# Night Summary Dashboard — Design Language Reference

This document captures every design decision in `dashboard.css` and `dashboard.js`
precisely enough to replicate the same language in the HTML report
(`ReportGenerator.cs`). Values are exact; nothing is approximate.

---

## 1. Color System

### CSS Variables — Dark Mode (default, `:root`)

| Variable | Value | Role |
|---|---|---|
| `--bg` | `#111224` | Page background — deepest layer |
| `--surface` | `#181930` | Card / panel surface — one step up |
| `--surface-well` | `#1e1f3c` | Inset elements (stat boxes, recessed wells) |
| `--border` | `#2d2d5e` | All borders and dividers |
| `--text` | `#e0e0e0` | General body text |
| `--accent` | `#7eb8f7` | Primary interactive / brand color (links, active nav, chart) |
| `--accent-light` | `#a0c4ff` | Section headings, slightly brighter accent |
| `--accent-lighter` | `#c0d8ff` | Lightest accent, rarely used |
| `--muted` | `#888` | Secondary labels, placeholder text |
| `--dim` | `#555` | Footer text, faintest non-interactive content |
| `--green` | `#3fb950` | Success states |
| `--red` | `#f85149` | Error states, hide-button color |
| `--yellow` | `#d29922` | Warning states, "Regenerate All" button |

### Four-Tier Text Hierarchy — Dark Mode

| Variable | Value | Intended use |
|---|---|---|
| `--text-primary` | `#e8eaf0` | Stat values, target names — spectral white |
| `--text-secondary` | `#b0b4bc` | Times, filter names, secondary data |
| `--text-tertiary` | `#6e7380` | Stat labels (FRAMES, HFR), timestamps |
| `--text-quaternary` | `#484c56` | Metadata, hints, hover-only detail |

### CSS Variables — Light Mode (`:root.light`)

| Variable | Value |
|---|---|
| `--bg` | `#eef0f5` |
| `--surface` | `#f8f9fc` |
| `--surface-well` | `#eef0f5` |
| `--border` | `#c0c8d4` |
| `--text` | `#1a1a2e` |
| `--accent` | `#2563b8` |
| `--accent-light` | `#3b7dd8` |
| `--accent-lighter` | `#5a9ae6` |
| `--muted` | `#666` |
| `--dim` | `#888` |
| `--green` | `#1a7f37` |
| `--red` | `#cf222e` |
| `--yellow` | `#9a6700` |

### Four-Tier Text Hierarchy — Light Mode

| Variable | Value |
|---|---|
| `--text-primary` | `#1a1c24` |
| `--text-secondary` | `#3a3d4a` |
| `--text-tertiary` | `#6b6e7a` |
| `--text-quaternary` | `#9a9da8` |

### Status / Badge Colors

Status badges use a semi-transparent tint of the semantic color as background,
with the opaque semantic color as text:

- Green badge: `background: rgba(63,185,80,0.15)`, `color: var(--green)`
- Red badge: `background: rgba(248,81,73,0.15)`, `color: var(--red)`

### Target Color Palette

Six-color categorical palette shared between the JS badge renderer and
`DashboardServer.TargetColors`. Assignment is by index modulo 6, so the color
is deterministic based on display order:

```
#4e79a7  (muted blue)
#f28e2b  (orange)
#e15759  (salmon red)
#76b7b2  (teal)
#59a14f  (green)
#edc948  (yellow)
```

---

## 2. Typography

### Font Families

- Body / UI: `Arial, sans-serif` — applied on `body` and explicitly on all
  interactive controls (`input`, `select`, `button`) via inline `font-family`
  declarations, because browser resets often omit form elements.

### Size Scale

| px | Usage |
|---|---|
| 22px / 18px (mobile) | Page `<h1>` title |
| 16px | Session date on cards |
| 15px | Report-view nav date |
| 14px | General body text, table cells |
| 13px | Nav links, filter controls, card session times, inline stats line |
| 12px | Filter action links, settings labels (small), table headers |
| 11px | Stat box labels, badge text, compact-mode card elements |
| 10px | Legend items, crosshair tooltip text, stat-expand-header |
| 9px | Live stack image badge |
| 8px | Thumb label default font size (scales down with name length; see §8) |

### Weight Usage

- `400` (normal): body text
- `500`: target badge text
- `600`: active nav links, stat labels (uppercase), session date, session times
- `700` (bold): stat values (23px), live stack badge

### Letter-Spacing Conventions

- Uppercase stat labels: `letter-spacing: 1px`
- Settings section labels: `letter-spacing: 0.5px`
- Stat expand header: `letter-spacing: 1px`
- Stat expand filter name: `letter-spacing: 0.3px`
- Thumb label: `letter-spacing: 0.3px`

### Text-Transform

`text-transform: uppercase` is used exclusively on labels that accompany a
prominent numeric value: `.card-stat-label`, `.stat-box .stat-label`,
`.stat-expand-header`, `.settings-label`, table `<th>`.

### Tabular Numerics

Any element displaying a number that may change width (stat values, popup
values) gets both:

```css
font-variant-numeric: tabular-nums;
font-feature-settings: "tnum";
```

Applied to: `.card-stat-value`, `.stat-box .stat-value`, `.stat-expand-val`.

---

## 3. Depth and 3D Effects

The design creates the illusion of depth entirely through `box-shadow`; no
`border` is used for 3D on primary elements. The pattern combines:

1. An outer **thin bright rim** (simulates top face catching ambient light)
2. An outer **diffuse drop shadow** (object casting shadow below)
3. An outer **spread drop shadow** (soft ambient occlusion)
4. An **inset top highlight** (inner top edge lit by light source)
5. An **inset bottom darken** (inner bottom edge in shadow)

### Session Card — Resting State

```css
box-shadow:
  0px 0px 0px 1px rgba(255,255,255,0.07),   /* outer rim */
  0px 2px 4px rgba(0,0,0,0.3),              /* close drop shadow */
  0px 8px 16px -4px rgba(0,0,0,0.25),       /* distant soft shadow */
  inset 0px 1px 0px rgba(255,255,255,0.04); /* inner top highlight */
```

### Session Card — Hover State

```css
transform: translateY(-3px);
box-shadow:
  0px 0px 0px 1px rgba(255,255,255,0.11),   /* rim brightens */
  0px 4px 8px rgba(0,0,0,0.35),             /* shadow deepens */
  0px 12px 24px -4px rgba(0,0,14,0.35),     /* distant shadow grows */
  inset 0px 1px 0px rgba(255,255,255,0.06); /* top highlight brightens */
transition: box-shadow 0.25s ease-out, transform 0.25s ease-out;
```

The card rises 3px and both the outer rim and inset highlight brighten to
reinforce the lift.

### Stat Boxes (`.card-stat`) — Raised Tile

```css
background: linear-gradient(to bottom, rgba(255,255,255,0.04) 0%, transparent 60%), var(--surface-well);
border: 1px solid rgba(255,255,255,0.08);
box-shadow:
  inset 0 1px 0 rgba(255,255,255,0.10),  /* top inner highlight */
  inset 0 -1px 0 rgba(0,0,0,0.18),       /* bottom inner darkening */
  0 2px 8px rgba(0,0,0,0.3),             /* drop shadow */
  0 1px 2px rgba(0,0,0,0.2);             /* tight close shadow */
```

The gradient sheen (`rgba(255,255,255,0.04)`) adds a subtle top-face glow.

### Target Badges (`.card-target-badge`) — Raised Pill

```css
box-shadow:
  inset 0 1px 0 rgba(255,255,255,0.28),  /* bright top highlight */
  inset 0 -1px 0 rgba(0,0,0,0.2),        /* bottom shadow */
  0 2px 6px rgba(0,0,0,0.4),             /* drop shadow */
  0 1px 2px rgba(0,0,0,0.3);             /* tight shadow */
```

The `0.28` top highlight is notably stronger than on stat boxes (`0.10`),
giving badges more 3D pop appropriate to their small size.

### Thumbnails (`.card-thumb-wrap`) — Drop Shadow

```css
box-shadow:
  0 2px 6px rgba(0,0,0,0.45),
  0 1px 2px rgba(0,0,0,0.35);
```

The inset lighting overlay is handled by the `::after` pseudo-element (see §8).

### Chart Legend Box (`.chart-legend`) — Floating Panel

```css
background: rgba(13, 17, 23, 0.7);  /* semi-transparent dark */
box-shadow:
  inset 0 1px 0 rgba(255,255,255,0.08),
  inset 0 -1px 0 rgba(0,0,0,0.2),
  0 2px 6px rgba(0,0,0,0.4),
  0 1px 2px rgba(0,0,0,0.3);
```

### Altitude Chart Container (`.chart-svg-wrap`) — Recessed Well

```css
background: rgba(0,0,0,0.22);
box-shadow:
  inset 0 3px 10px rgba(0,0,0,0.45),     /* deep inner shadow — sunken look */
  inset 0 0 0 1px rgba(0,0,0,0.35),      /* inner border rim */
  0 1px 0 rgba(255,255,255,0.06);        /* outer bottom highlight — "lip" */
```

The outer `0 1px 0` is the critical "lip" that lifts the well out of the card
surface and makes it read as recessed rather than flat.

### Stat-Expand Popup (`.stat-expand-popup`)

```css
box-shadow:
  0 8px 24px rgba(0,0,0,0.55),
  0 2px 6px rgba(0,0,0,0.35),
  inset 0 1px 0 rgba(255,255,255,0.08);
```

The heavy outer shadow pushes the popup above the card surface.

### Universal Rule — All Interactive Pills and Boxes

**Every interactive pill or box element uses a 3D shadow by default.** Flat
elements exist only as internal structural containers (e.g. `.filter-bar`,
`nav`). Anything the user can click or read as a discrete interactive chip
gets depth. Two base variants apply based on shape:

#### Raised Pill — for pill-shaped elements (`border-radius ≥ 16px`)

Used on: `.subtitle` (sessions count), `.filter-bar .target-check` (toggle
pills), `.target-dropdown-menu .target-check` (filter popover pills),
`.card-target-badge`.

```css
box-shadow:
  inset 0 1px 0 rgba(255,255,255,0.18),  /* top inner highlight */
  inset 0 -1px 0 rgba(0,0,0,0.18),       /* bottom inner shadow */
  0 2px 4px rgba(0,0,0,0.35),            /* drop shadow */
  0 1px 2px rgba(0,0,0,0.2);             /* tight shadow */
```

The `0.18` inset highlight is between stat boxes (`0.10`) and target badges
(`0.28`) — appropriate for medium-prominence pills.

#### Raised Tile — for box-shaped elements (`border-radius < 16px`)

Used on: `.nav-link`, `.theme-toggle`, `.target-dropdown-btn`,
`.view-toggle-btn.active`.

```css
box-shadow:
  inset 0 1px 0 rgba(255,255,255,0.08),  /* subtle top highlight */
  inset 0 -1px 0 rgba(0,0,0,0.15),       /* bottom inner shadow */
  0 2px 6px rgba(0,0,0,0.3),             /* drop shadow */
  0 1px 2px rgba(0,0,0,0.2);             /* tight shadow */
```

The weaker `0.08` highlight is appropriate for larger box elements where
the inset lighting would otherwise look exaggerated.

#### Recessed Well — for segmented control containers

Used on: `.view-toggle` (Compact/Expanded container).

```css
box-shadow:
  inset 0 2px 6px rgba(0,0,0,0.35),  /* deep inner shadow — sunken */
  inset 0 0 0 1px rgba(0,0,0,0.2),   /* inner border rim */
  0 1px 0 rgba(255,255,255,0.05);    /* outer lip */
```

The active button inside a recessed container gets the Raised Tile shadow,
making it appear to pop out of the sunken container.

---

## 4. Text Shadow / Emboss

A three-layer text shadow pattern is used on text that sits over dark
backgrounds where it needs to read clearly and feel physically present.

### Session Date (`.session-date`)

```css
text-shadow:
  0 -1px 0 rgba(255,255,255,0.12),  /* highlight above — emboss top edge */
  0 2px 6px rgba(0,0,0,0.8),        /* deep diffuse drop shadow */
  0 1px 0 rgba(0,0,0,0.6);          /* hard 1px drop shadow for crispness */
```

### Session Times (`.session-times`)

```css
text-shadow:
  0 -1px 0 rgba(255,255,255,0.08),  /* lighter highlight (secondary text) */
  0 2px 4px rgba(0,0,0,0.7);        /* slightly softer shadow */
```

### Thumbnail Label (`.thumb-label`)

```css
text-shadow:
  0 -1px 0 rgba(255,255,255,0.15),  /* strong highlight for contrast */
  0 1px 0 rgba(0,0,0,0.7),          /* hard 1px shadow */
  0 2px 6px rgba(0,0,0,0.9),        /* heavy diffuse — text over image */
  0 0 8px rgba(0,0,0,0.5);          /* wide glow for legibility */
```

The thumb label uses a fourth layer (`0 0 8px`) for a glow halo, because it
overlays a raw astrophotography image where contrast is unpredictable.

### Pattern Summary

The canonical emboss pattern is always: **white highlight above + hard black
drop + soft diffuse shadow**. The white highlight magnitude tracks text
importance (`0.15` for strong primary, `0.12` for date, `0.08` for secondary).

---

## 5. Surface Hierarchy

Three background surfaces nest to create three depth levels:

| Level | Variable | Color (dark) | Used for |
|---|---|---|---|
| 0 — page floor | `--bg` | `#111224` | Page background, stat-box bg on stats page |
| 1 — raised card | `--surface` | `#181930` | Session cards, panels, dropdowns, popups |
| 2 — recessed well | `--surface-well` | `#1e1f3c` | Stat boxes inside cards, settings selects |

**Counterintuitive but correct**: `--surface-well` is *lighter* than `--surface`
in dark mode. The recessed appearance comes from the inset `box-shadow`, not
from a darker background color. The slightly lighter `--surface-well` provides
enough contrast for the inset shadow to read.

In light mode, `--bg` and `--surface-well` share the same value (`#eef0f5`),
so the depth is subtler — the shadow is the sole differentiator.

---

## 6. Card Design

### Structure

```
.session-card                        ← full-width block, surface background
  .hide-btn                          ← ghost ✕, top-right, opacity:0 until hover
  .card-header                       ← flex row, baseline-aligned
    .session-date                    ← 16px bold, text-primary with emboss
    .session-times                   ← 13px, text-secondary with emboss
    .card-targets-line               ← flex wrap of .card-target-badge pills
    .badge                           ← optional status badge (e.g. "No report")
  .card-body                         ← flex row, min-height:220px
    .card-content                    ← fixed-width left column (500–750px)
      .card-thumbs                   ← flex wrap of thumbnail wrappers
      .card-stats-line               ← compact-mode only inline text
      .card-stats                    ← expanded-mode stat box row
    .card-altitude                   ← flex-1 right column: legend + SVG wrap
```

### Hover Lift Animation

```css
transition: box-shadow 0.25s ease-out, transform 0.25s ease-out;
```

On hover: `transform: translateY(-3px)` + `z-index: 10` (to clear siblings).
The `ease-out` easing means the lift starts fast and decelerates — feels like
picking up a physical card.

### Ghost Hide Button Pattern

The `×` button at `top: 6px; right: 6px` is `opacity: 0` by default and
`pointer-events: none` is implicit through `opacity: 0` (it IS clickable when
visible). It appears at full opacity only when the parent card is hovered:

```css
.hide-btn { opacity: 0; transition: opacity 0.15s, background 0.15s; }
.session-card:hover .hide-btn { opacity: 1; background: rgba(207,34,46,0.12); }
.hide-btn:hover { opacity: 1; background: rgba(207,34,46,0.25); }
```

The hide animation (when clicked) fades + shrinks the card:

```js
card.style.transition = 'opacity 0.2s, transform 0.2s';
card.style.opacity = '0';
card.style.transform = 'scale(0.97)';
// remove from DOM after 200ms
```

---

## 7. Target Badges

### Three-Layer Design

Each badge is a pill (`.card-target-badge`, `border-radius: 100px`) where color
is derived from the target's index in the six-color palette:

```
background: rgba(<R>,<G>,<B>, 0.10)   ← 10% tint of the target color
border:     rgba(<R>,<G>,<B>, 0.28)   ← 28% tint border
color:      <hex color>               ← full saturation text
```

All three components use the same color, just different opacities. This means
each badge self-coordinates: you never need separate variables for bg, border,
and text — one color drives all three.

The `hexToRgb()` function extracts the R,G,B tuple for constructing the
`rgba()` strings:

```js
function hexToRgb(hex) {
  return parseInt(hex.slice(1,3),16)+','+parseInt(hex.slice(3,5),16)+','+parseInt(hex.slice(5,7),16);
}
```

### Sizing and Spacing

```css
padding: 2px 7px 2px 5px;  /* slightly less left padding for optical balance */
font-size: 11px;
font-weight: 500;
gap: 4px;                   /* between icon and text if any */
```

In compact mode: `font-size: 10px`, `padding: 1px 5px 1px 4px`.

### Palette Reference

```js
var TARGET_COLORS = ['#4e79a7', '#f28e2b', '#e15759', '#76b7b2', '#59a14f', '#edc948'];
```

Assignment: `TARGET_COLORS[index % 6]`. Thumbnails are loaded in report order
and the target list is re-sorted to match, so the badge color always matches
the thumbnail color.

---

## 8. Thumbnails

### Base Dimensions

```css
.card-thumb-wrap {
  width: 120px;
  height: 120px;
  border-radius: 6px;
  overflow: hidden;
}
```

### Scale-on-Hover (1.67×)

```css
.card-thumb-wrap:hover,
.card-thumb-wrap.shelf-active {
  transform: scale(1.67);
  z-index: 10;
  transition-delay: 150ms;  /* delay before scale starts */
}
```

Default transition is `transition: transform 0.2s ease`. The `transition-delay`
prevents accidental triggers on mouse-pass. `shelf-active` is added by JS when
the live stack shelf is open, keeping the thumbnail scaled while mousing into
the shelf.

### Inset Lighting Overlay (`::after` pseudo-element)

A pseudo-element layered at `z-index: 3` (above image, below label at z-index 4)
renders the 3D lighting effect using only `box-shadow`:

```css
.card-thumb-wrap::after {
  content: '';
  position: absolute;
  inset: 0;
  border-radius: 6px;
  box-shadow:
    inset 0 1px 0 rgba(255,255,255,0.14),   /* top inner rim highlight */
    inset 0 0 0 1px rgba(255,255,255,0.07), /* full inner rim glow */
    inset 0 -3px 8px rgba(0,0,0,0.4),       /* bottom inner darkening */
    inset 3px 0 8px rgba(0,0,0,0.12),       /* left edge darkening */
    inset -3px 0 8px rgba(0,0,0,0.12);      /* right edge darkening */
  pointer-events: none;
}
```

The result: top edge appears lit, bottom and side edges are subtly darkened,
giving the image a physically rounded-glass appearance.

### Hover Label

```css
.thumb-label {
  position: absolute;
  top: 0; left: 0; right: 0;
  z-index: 4;
  padding: 3px 3px 8px;
  background: linear-gradient(to bottom, rgba(0,0,0,0.72) 0%, transparent 100%);
  color: #fff;
  font-size: 8px;           /* default; overridden by JS for long names */
  font-weight: 600;
  letter-spacing: 0.3px;
  text-align: center;
  opacity: 0;
  transition: opacity 0.18s ease-out;
  transition-delay: 150ms;  /* matches scale delay */
}
.card-thumb-wrap:hover .thumb-label { opacity: 1; }
```

Font size is scaled down by JS based on name length after 30-character truncation:

```js
var labelName = t.target.length > 30 ? t.target.substring(0, 29) + '\u2026' : t.target;
var labelFontStyle = labelName.length <= 14 ? '' :
  labelName.length <= 20 ? ' style="font-size:7px"' :
                            ' style="font-size:6px"';
```

| Name length | Font size |
|---|---|
| ≤ 14 chars | 8px (default) |
| 15–20 chars | 7px |
| > 20 chars | 6px |

### Live Stack Badge Hide on Hover

The badge (`position: absolute; bottom: 4px; right: 4px`) hides when the
thumbnail scales to avoid obscuring the zoomed content:

```css
.card-thumb-wrap:hover .livestack-badge,
.card-thumb-wrap.shelf-active .livestack-badge {
  opacity: 0;
  transition: opacity 0.15s;
}
```

---

## 9. Interactive Delays

The design uses three distinct delay values to prevent accidental triggers:

### 150ms — CSS Transition Delay (Thumbnails)

Thumbnail scale and label reveal both use `transition-delay: 150ms`. Hovering
less than 150ms (e.g., moving the mouse across the card) has no visual effect.
This prevents the row of thumbnails from constantly popping as the mouse passes.

### 200ms — JS Timer (Live Stack Shelf)

```js
thumbWrap.addEventListener('mouseenter', function() {
  hoverTimer = setTimeout(showShelf, 200);
});
thumbWrap.addEventListener('mouseleave', function() {
  clearTimeout(hoverTimer);
  shelfLeaveTimer = setTimeout(hideShelf, 100);
});
```

200ms dwell required before the shelf appears. A 100ms grace period on
`mouseleave` prevents the shelf from hiding if the mouse briefly exits and
re-enters (e.g., clipping a pixel boundary). The shelf also stays alive when
the mouse moves into it directly via `mouseenter` on the shelf itself.

### 350ms — JS Timer (Stat Expand Popup)

```js
document.addEventListener('mouseenter', function(e) {
  var el = e.target.closest('.card-stat-expandable');
  if (!el) return;
  statExpandTimer = setTimeout(function() {
    showStatExpand(el, sessionId, type);
  }, 350);
}, true);
```

The longest delay — 350ms — is used for the stat popup because it's a more
complex floating element with an API fetch. It fires only after sustained hover
intent.

---

## 10. Stat Boxes

### Tile Design

Stat boxes sit inside `.card-stats` (flex row, 6px gap). They use the
`--surface-well` background with a subtle gradient sheen:

```css
.card-stat {
  background: linear-gradient(to bottom, rgba(255,255,255,0.04) 0%, transparent 60%),
              var(--surface-well);
  border: 1px solid rgba(255,255,255,0.08);
  border-radius: 6px;
  padding: 12px 32px;
  text-align: center;
}
```

### Value / Label Pairing

- Value: 23px bold, `--text-primary`, tabular-nums
- Label: 11px, `--text-tertiary`, uppercase, `letter-spacing: 1px`, `font-weight: 600`

### Expandable Indicator

Two stat boxes (`images` and `integration`) are expandable. A subtle accent
underline signals this:

```css
.card-stat-expandable::after {
  content: '';
  position: absolute;
  bottom: 5px;
  left: 50%;
  transform: translateX(-50%);
  width: 16px;
  height: 2px;
  border-radius: 1px;
  background: var(--accent);
  opacity: 0.35;
  transition: opacity 0.2s, width 0.2s;
}
.card-stat-expandable:hover::after {
  opacity: 0.7;
  width: 22px;
}
```

The indicator grows from 16px to 22px wide on hover, signaling interactivity
without being obtrusive.

### Expand Popup

The popup positions centered below (or above if near the bottom of the
viewport) the stat box. It starts at `translateY(4px)` with `opacity: 0` and
transitions to `translateY(0)` + `opacity: 1`:

```css
.stat-expand-popup {
  transform: translateX(-50%) translateY(4px);
  opacity: 0;
  transition: opacity 0.18s ease-out, transform 0.18s ease-out;
}
.stat-expand-popup.stat-expand-visible {
  opacity: 1;
  transform: translateX(-50%) translateY(0);
}
.stat-expand-popup.stat-expand-hiding {
  opacity: 0;
  transform: translateX(-50%) translateY(4px);
  transition-duration: 0.18s;
}
```

Removal is deferred 180ms to allow the hide animation to complete before
removing the DOM node.

Viewport edge clamping is applied via `requestAnimationFrame` after insertion
(12px minimum padding from either side).

---

## 11. Live Stack Shelf

### Structure

The shelf is a floating `div.livestack-shelf` appended to `.card-thumbs`
(not the document body). It is positioned absolutely:

- Horizontally centered on the hovered thumbnail (`transform: translateX(-50%)`)
- Vertically: `thumbBottom - containerTop + 75px` (accounts for the 1.67×
  scaled visual size of the thumbnail)

### Arrow Connector

Two CSS triangles, stacked, form a border + fill arrow pointing up:

```css
.livestack-shelf::before {  /* border triangle */
  top: -6px;
  border-left: 6px solid transparent;
  border-right: 6px solid transparent;
  border-bottom: 6px solid var(--border);
}
.livestack-shelf::after {  /* fill triangle */
  top: -5px;
  border-left: 5px solid transparent;
  border-right: 5px solid transparent;
  border-bottom: 5px solid var(--surface);
}
```

### Reveal / Hide Animations

The shelf uses `clip-path` for a center-expand reveal effect:

```css
@keyframes shelf-reveal {
  from { opacity: 0; clip-path: inset(0 50% 0 50%); }
  to   { opacity: 1; clip-path: inset(0 0 0 0); }
}
@keyframes shelf-hide {
  from { opacity: 1; clip-path: inset(0 0 0 0); }
  to   { opacity: 0; clip-path: inset(0 50% 0 50%); }
}
```

Duration: 200ms reveal, 150ms hide. The hide class (`shelf-hiding`) is added
by JS; the node is removed after 150ms.

### Shelf Images

Each image item starts invisible and fades in with a scale:

```css
@keyframes shelf-item-in {
  from { opacity: 0; transform: scale(0.8); }
  to   { opacity: 1; transform: scale(1); }
}
.livestack-shelf-item {
  width: 300px;
  animation: shelf-item-in 200ms ease-out forwards;
  animation-delay: <idx * 40>ms;  /* staggered in JS */
}
```

Items are staggered 40ms apart, so a three-image shelf completes its entrance
at 200ms + 80ms = 280ms total.

### shelf-active Class

Added to `.card-thumb-wrap` when the shelf is open. This keeps `transform: scale(1.67)`
applied even when the mouse leaves the thumb to enter the shelf, so the
thumbnail does not shrink while the user reads the shelf.

---

## 12. Altitude Chart

### SVG Coordinate System

The SVG uses `preserveAspectRatio=none` (stretches to fill container). The
plot area occupies a fixed rectangle within the viewBox:

- Left margin: x = 38
- Top: y = 20
- Bottom: y = 220
- Right: x = viewBox.width - 10

### Recessed Container

```css
.chart-svg-wrap {
  background: rgba(0,0,0,0.22);
  border-radius: 6px;
  box-shadow:
    inset 0 3px 10px rgba(0,0,0,0.45),
    inset 0 0 0 1px rgba(0,0,0,0.35),
    0 1px 0 rgba(255,255,255,0.06);   /* outer lip highlight */
}
```

The SVG is positioned `absolute` within, with 10px top offset to keep chart
content below the top shadow. The card altitude column dynamically adjusts
`margin-top` to pull the chart up behind the card header when the header is
tall enough.

### Light Mode Overrides

The SVG contains hardcoded dark-mode fill/stroke attributes. Light mode
overrides these via CSS attribute selectors — no SVG changes required:

```css
:root.light .chart-svg-wrap svg rect[fill='#0d1117'] { fill: #e8eef5; }
:root.light .chart-svg-wrap svg [stroke='#2d2d5e']   { stroke: #c0c8d4; }
:root.light .chart-svg-wrap svg [fill='#2d2d5e']     { fill: #c0c8d4; }
:root.light .chart-svg-wrap svg text                  { fill: #555; }
:root.light .chart-svg-wrap svg [stroke='#c0c0c0']   { stroke: #7a8a9e; }
:root.light .chart-svg-wrap svg [stroke='#7eb8f7']   { stroke: #2563b8; }
```

The chart legend background also adapts:

```css
:root.light .chart-legend { background: rgba(238,240,245,0.9); }
```

Light mode chart-svg-wrap also reduces the inset shadow:

```css
:root.light .chart-svg-wrap {
  background: rgba(0,0,0,0.04);
  box-shadow:
    inset 0px 2px 6px rgba(0,0,0,0.08),
    inset 0px 0px 0px 1px rgba(0,0,0,0.06);
}
```

### Crosshair Interaction

The crosshair is implemented as persistent SVG elements (created once, updated
on mousemove — no DOM churn):

- White dashed vertical line: `stroke: #ffffff`, `stroke-width: 0.5`,
  `stroke-dasharray: 3,3`, `opacity: 0.5`
- Time label: `fill: #fff`, `font-size: 9`, `font-weight: bold`,
  `text-anchor: middle`
- Per-target altitude dots: `r: 3`, `fill: <target color>`, `stroke: #fff`,
  `stroke-width: 0.8`
- Per-target altitude text: `fill: <target color>`, `font-size: 8`,
  `font-weight: bold`

When the crosshair is inside a target's imaging window, all other target groups
are dimmed to `opacity: 0.15`.

### Text Distortion Fix

Because the SVG uses `preserveAspectRatio=none`, text is horizontally
compressed. `fixChartTextDistortion()` corrects this on load:

```js
var ratio = ctm.d / ctm.a; // yScale / xScale
// Apply counter-transform to each text element
t.setAttribute('transform',
  'translate(x,y) scale(ratio, 1) translate(-x,-y)');
```

The same transform is applied live during crosshair mousemove to keep tooltip
text undistorted as the viewport resizes.

### Animated Curve Drawing

Target polylines are drawn with `strokeDashoffset` animation triggered by
`IntersectionObserver` at threshold 0.3:

```js
p.style.transition = 'stroke-dashoffset 0.5s ease-out';
p.style.strokeDashoffset = '0';
```

Reset (instant, no transition) when the card scrolls out of view, so the
animation re-fires on the next scroll-in.

---

## 13. Animation Patterns

### Transition Properties

Elements use targeted `transition` declarations (never `all`):

| Element | Properties | Duration | Easing |
|---|---|---|---|
| Session card | `box-shadow, transform` | `0.25s` | `ease-out` |
| Stat box hover indicator | `opacity, width` | `0.2s` | default |
| Hide button | `opacity, background` | `0.15s` | default |
| Nav link | `background, color` | `0.15s` | default |
| Theme toggle border | `border-color` | `0.15s` | default |
| Thumb scale | `transform` | `0.2s` | `ease` |
| Thumb label opacity | `opacity` | `0.18s` | `ease-out` |
| Livestack badge | `opacity` | `0.15s` | default |
| Stats iframe loading | `opacity, transform` | `0.2s` | default |

### Popup Fade + Slide Pattern

All floating popups share the same entry/exit pattern:

```
enter: opacity 0→1, translateY(4px)→translateY(0), duration 0.18s ease-out
exit:  opacity 1→0, translateY(0)→translateY(4px), duration 0.18s
```

The 4px downward offset on entry creates a sense of the popup descending from
its trigger point.

### Altitude Curve Draw

```
stroke-dashoffset animation: 0.5s ease-out
trigger: IntersectionObserver at 0.3 threshold
reset on exit: instant (no transition)
```

### Shelf Expand / Collapse

200ms clip-path expand (center → full width), 150ms collapse. Items stagger
40ms each at 200ms + (index × 40ms).

### Card Hide

200ms fade + scale(0.97). The scale-down is subtle (3%) — just enough to
signal removal without being distracting.

---

## 14. Light Mode

### Toggle Mechanism

Light mode is controlled by adding/removing the `light` class on
`document.documentElement` (`<html>`):

```js
document.documentElement.classList.toggle('light');
localStorage.setItem('ns-theme', isLight ? 'light' : 'dark');
```

Theme persists in `localStorage` under the key `ns-theme`. On init,
`localStorage.getItem('ns-theme') === 'light'` triggers the class.

Theme toggle button icon: `☀` (U+2600) for light mode, `☾` (U+263E) for dark.

### CSS Overrides

All overrides live in `:root.light { }` — a single cascade source. The only
additional rule is the `.chart-svg-wrap` reskin (see §12) and the SVG
attribute selector overrides.

No JavaScript is involved in the visual light-mode switch — the entire
recoloring is CSS.

---

## 15. Compact vs Expanded View

View mode is stored in `localStorage` under `ns-card-view` (`'compact'` or
`'expanded'`, default `'expanded'`).

The `cards-compact` class is added to the `.cards-container` wrapper when
compact mode is active.

### Elements Hidden in Compact Mode

```css
.cards-compact .card-thumbs    { display: none; }
.cards-compact .card-altitude  { display: none; }
.cards-compact .card-stats     { display: none; }
.cards-compact .session-times  { display: none; }
```

Compact card padding shrinks: `8px 12px` (vs `8px 28px 12px 14px`).

### Elements Shown Only in Compact Mode

```css
/* Hidden by default, shown in compact: */
.card-stats-line { display: none; }
.cards-compact .card-stats-line { display: block; font-size: 11px; margin-top: 1px; }
```

The stats line is a condensed inline summary (`N imgs · Xh Ym · HFR N.NN ·
N.NN″ guiding`) that replaces the stat boxes. Values are colored
`var(--accent)` with `font-weight: 600`.

### Elements Also Hidden at `max-width: 700px` (Mobile)

At the 700px breakpoint, the dashboard automatically applies compact-like
behavior: thumbnails, altitude chart, and stat boxes are hidden; the
stats line is shown. This is independent of the compact view toggle.

The "Show FOV" checkbox is disabled in compact mode (via the `disabled` class
on `.target-check`).

---

## 16. Design Principles

### Dark-First

The design is authored in dark mode. Light mode is a layer of overrides.
Shadows at low opacity (`rgba(0,0,0,0.3)`) read correctly in dark mode; in
light mode the `--surface` and `--bg` values are already light enough that
the same shadows still register.

### Depth Through Shadows, Not Flat Color Differences

Cards are not differentiated from `--bg` by color alone — they use `box-shadow`
(the outer rim + drop shadow stack) to float above the background. This is why
`--surface` (`#181930`) is only marginally lighter than `--bg` (`#111224`);
the visual separation comes from shadow, not hue.

### Inset vs Raised Semantics

- `inset` shadow → recessed / sunken → used for wells that hold content
  (`.chart-svg-wrap`, text input backgrounds)
- Outward shadow → raised / floating → used for interactive elements that lift
  (cards, badges, stat boxes, popups)

A consistent reading: you put things *into* wells and *pick up* tiles.

### Subtle Motion That Does Not Distract

All durations are short: 0.15s–0.25s for micro-interactions, 0.5s only for the
altitude curve draw (a deliberate reveal). Easing is always `ease-out` (fast
entry, gradual settle) — never `ease-in` or linear. Animations are triggered
by user action, never on page load.

### Hover Delays to Reduce Noise

Three tiers of delay (150ms CSS, 200ms JS, 350ms JS) match the cost of showing
the triggered element. Thumbnails scale cheaply (CSS only) but still get 150ms
to avoid flicker while panning. The stat popup fetches from the API — it waits
350ms to ensure the hover was intentional.

### Information Revealed Progressively

At a glance: date, times, target badges, stats line (compact) or stat boxes
(expanded).
On hover of a stat box: per-target breakdown popup.
On hover of a thumbnail: target name label.
On dwell on a thumbnail with live stack: shelf of all live stack images.

No information is hidden permanently; everything is accessible without
navigation.

### Consistent Focus Ring

Interactive controls that need keyboard accessibility use
`border-color: var(--accent)` on `:focus` / `:hover` (not a separate
`outline`). This integrates with the existing border design rather than
adding a browser-default blue ring.

### No Hardcoded Colors in Functional CSS

All colors in CSS rules reference CSS variables. The only hardcoded hex values
appear in:
1. The `:root { }` and `:root.light { }` variable definitions
2. The SVG attribute selector overrides (which must match hardcoded SVG
   presentation attributes generated by C#)
3. The chart-legend semi-transparent background (`rgba(13,17,23,0.7)`)

Everything else adapts automatically to light/dark mode through variable
inheritance.

---

## 17. Page Header

The header is `position: sticky; top: 0; z-index: 100` — it stays pinned while the card list scrolls beneath it.

### Frosted Glass Background

```css
--header-bg: rgba(17,18,36,0.82)   /* dark mode */
--header-bg: rgba(238,240,245,0.88) /* light mode */

header {
  background: var(--header-bg);
  backdrop-filter: blur(14px);
  -webkit-backdrop-filter: blur(14px);
  border-bottom: 1px solid rgba(255,255,255,0.07);
}
```

### Scroll Shadow

A `scrolled` class is added to `<header>` via a passive scroll listener when `window.scrollY > 0`:

```css
header.scrolled {
  box-shadow:
    0 1px 0 rgba(255,255,255,0.05),
    0 4px 24px rgba(0,0,0,0.4);
}
```

### Layout

```css
.header-inner {
  max-width: 1800px;
  margin: 0 auto;
  padding: 5px max(20px, env(safe-area-inset-left));
  display: flex;
  align-items: center;
  justify-content: space-between;
}
```

Left side: icon + title + session count pill (all baseline-aligned).
Right side: nav links + theme toggle button.

### Title Glow

The `<h1>` uses a three-layer glow (not emboss) — appropriate for text on a dark blurred background:

```css
h1 {
  text-shadow:
    0 0 6px rgba(126,184,247,0.35),
    0 0 18px rgba(126,184,247,0.18),
    0 0 40px rgba(126,184,247,0.08);
}
```

Three concentric halos at decreasing opacity create depth without a harsh directional shadow.

### Icon

```css
.header-icon {
  width: 48px;
  height: 48px;
  filter:
    drop-shadow(0 0 4px rgba(126,184,247,0.28))
    drop-shadow(0 0 10px rgba(126,184,247,0.12));
}
```

`drop-shadow` (not `box-shadow`) is used because the icon is a PNG with transparency — the shadow follows the alpha channel.

### Session Count Pill

```css
.session-count-pill {
  display: inline-block;
  border-radius: 20px;
  padding: 2px 9px;
  font-size: 11px;
  background: rgba(230,232,240,0.07);
  border: 1px solid rgba(230,232,240,0.14);
  color: var(--text-tertiary);
}
```

### Nav Tabs

Active nav link uses an `::after` pseudo-element as an underline indicator:

```css
.nav-link.active::after {
  content: '';
  position: absolute;
  bottom: -1px;
  left: 0; right: 0;
  height: 2px;
  border-radius: 1px;
  background: var(--accent);
  box-shadow: 0 0 6px rgba(126,184,247,0.5);
}
```

The `box-shadow` glow on the indicator echoes the title glow, tying the nav to the accent color system.

---

## 18. Filter Bar

All primary filter controls share a unified height and box model:

```css
height: 32px;
box-sizing: border-box;
padding: 0 10px;
```

This applies to: the target dropdown button, date-from and date-to inputs, and the sort-order select.

### Clear Filters — Ghost Pill Link

```css
.filter-link {
  display: inline-flex;
  align-items: center;
  height: 32px;
  padding: 0 12px;
  border-radius: 100px;
  border: 1px solid rgba(248,81,73,0.25);
  color: var(--red);
  background: transparent;
  font-size: 12px;
  cursor: pointer;
}
.filter-link:hover {
  background: rgba(248,81,73,0.1);
  border-color: rgba(248,81,73,0.45);
}
```

### Toggle Pills (Compact, Expanded, Show empty, Show FOV, Show hidden)

Each toggle is a `<label>` wrapping a hidden `<input type="checkbox">`. The pill's active state is driven entirely by CSS with no JavaScript:

```css
/* Native checkbox hidden visually */
.target-check input[type="checkbox"] {
  position: absolute;
  opacity: 0;
  width: 0; height: 0;
}

/* Base pill — ghost outline */
.target-check {
  display: inline-flex;
  align-items: center;
  height: 32px;
  padding: 0 12px;
  border-radius: 100px;
  border: 1px solid rgba(126,184,247,0.18);
  color: var(--text-secondary);
  background: transparent;
  cursor: pointer;
  user-select: none;
}

/* Active state via :has() — no JS needed */
.target-check:has(input:checked) {
  background: rgba(126,184,247,0.12);
  border-color: rgba(126,184,247,0.35);
  color: var(--accent);
}
```

`:has(input:checked)` is the key — the parent label tracks its child input's state without any JS event wiring.

---

## 19. Card Date/Time Glow

Unlike thumbnails and the title, session date and time text uses a subtle glow rather than an emboss:

### Session Date (`.session-date`)

```css
text-shadow:
  0 0 8px rgba(232,234,240,0.22),
  0 0 20px rgba(232,234,240,0.08);
```

Two halos at neutral white (not accent blue), very low opacity. The effect is barely perceptible — just enough to make the date feel luminous against the card surface without competing with the title's stronger accent glow.

### Session Times (`.session-times`)

```css
text-shadow:
  0 0 6px rgba(232,234,240,0.15),
  0 0 16px rgba(232,234,240,0.06);
```

Even softer than the date — secondary text gets proportionally less glow.

**Design rationale**: The previous emboss pattern (white highlight + black drop shadow) looked appropriate on headline text but felt heavy on card-level metadata. The glow pattern is more ethereal and appropriate for data that the eye should register without dwelling on.

---

## 20. Thumbnail Z-Index Hover Handoff

When thumbnails overlap during the 1.67× scale, z-index must pass cleanly from the old thumb to the new one without both being elevated simultaneously.

### The Problem

If `z-index` transitions linearly, the leaving thumb and entering thumb both have `z-index: 10` for the duration of the transition — the new thumb appears behind the old scaled thumb during mouse-over.

### The Fix

```css
/* Base state: z-index drops IMMEDIATELY when hover ends */
.card-thumb-wrap {
  transition: transform var(--t-medium), z-index 0s;
}

/* Hover state: z-index elevates after a short delay */
.card-thumb-wrap:hover {
  z-index: 10;
  transition: transform var(--t-medium) 150ms, z-index 0s 150ms;
}
```

`z-index 0s` means no easing/interpolation — it's a discrete switch.
`0s 150ms` on hover: elevation is delayed 150ms (matches the transform delay).
`0s 0ms` on base: elevation drops instantly when hover ends — the leaving thumb relinquishes z-index before the entering thumb claims it.

---

## 21. Asset Loading & Performance

### Client-Side Caching

Three in-memory caches persist for the browser session:

```js
var thumbnailCache = {};      // sessionId -> thumbnails array
var altitudeChartCache = {};  // sessionId -> { svg, legend }
var livestackMap = {};        // sessionId -> { targetName -> [...] }
```

Once a session's assets are fetched, subsequent filter changes re-render from cache with no network requests.

### In-Flight Guards

Duplicate concurrent requests for the same session are prevented:

```js
var altitudeChartFetching = {}; // sessionId -> true while in flight
var thumbnailFetching = {};     // sessionId -> true while in flight
```

`fetchAltitudeChart` and `loadThumbnails` bail immediately if `*Fetching[sessionId]` is set. The flag is cleared on both success and error. Without this guard, rapid filter toggles during initial load caused 4+ concurrent requests for the same session.

### Initial Load Reveal

On first page load (expanded mode), the cards container starts `opacity: 0` and reveals after assets arrive:

```js
// First load only — controlled by initialLoadDone flag
if (allCached) {
  requestAnimationFrame(revealContainer);  // instant for cached data
} else {
  setTimeout(revealContainer, 600);        // gather window for fresh fetches
}
initialLoadDone = true;
```

`allCached` checks `thumbnailCache[s.sessionId] && altitudeChartCache[s.sessionId]` — livestack is excluded from the gate (it's a lazy hover element, not visually blocking).

### Filter Toggle Reveal

After the initial load, `initialLoadDone = true` and subsequent filter changes render with `opacity: 1` inline style — no fade, no timeout. Response is a single `requestAnimationFrame` (< 16ms).

### Viewport-Priority Loading

`loadAltitudeCharts` loads visible charts immediately, offscreen charts after 150ms:

```js
sessions.forEach(function(s) {
  if (altitudeChartCache[s.sessionId]) {
    renderAltitudeChart(s, altitudeChartCache[s.sessionId]); // no fetch needed
    return;
  }
  var rect = el.getBoundingClientRect();
  if (rect.top < window.innerHeight + 100) visible.push(s);
  else offscreen.push(s);
});
visible.forEach(fetchAltitudeChart);
setTimeout(function() { offscreen.forEach(fetchAltitudeChart); }, 150);
```

### Server-Side Persistent Cache

Altitude charts are expensive to generate (HTML file read + regex parsing). The server persists parsed chart JSON in a separate SQLite database:

- **File**: `%LOCALAPPDATA%\NINA\NightSummary\nightsummary-dashboard-cache.sqlite`
- **Table**: `AltitudeCharts (SessionId TEXT PRIMARY KEY, ChartJson TEXT, GeneratedAt TEXT)`
- **Warmup**: On server start, a background task bulk-loads all cached entries from DB into `altitudeChartCache` Dictionary. Sessions with no report file are skipped entirely.
- **Persistence**: After parsing a report's HTML, the resulting JSON is written to the DB with `INSERT OR REPLACE`.
- **Invalidation**: When a report is regenerated (single or bulk), the corresponding DB row is deleted so the next request re-parses the updated HTML.

This means after the first server start (which parses all reports), every subsequent start loads charts from DB — sub-millisecond vs. 1–2 seconds per chart.

---

## 22. Dashboard Logging

The dashboard server writes to its own log file, separate from NINA's main log:

- **Location**: `%LOCALAPPDATA%\NINA\NightSummary\logs\dashboard-YYYY-MM-DD_HH-mm-ss.log`
- **One file per server start** — timestamp to seconds ensures no two starts share a file
- **Age-off**: On each server start, log files with `LastWriteTime` older than 14 days are deleted (`PurgeOldLogs`, pattern `dashboard-*.log*`)
- **Size cap**: Each file rotates at 5MB (`.log` → `.log.1`) as a safety net within a single session
- **Request logging**: Each HTTP request logs method, path, status code, and elapsed ms via `BeginRequest` / `done(status, detail)` pattern

---

## 23. Target Filter Popover

The target filter is a searchable 2-column popover, replacing a plain scrolling
dropdown. Design rationale and spec:

### Why a popover, not a dropdown list

A single-column scrolling list breaks down at 20+ targets. A popover with a
2-column pill grid shows ~16 targets at once before scrolling and scales to any
count without feeling overwhelming. The searchable input handles large libraries
(30–50+ targets across a season) without requiring the user to scroll at all.

### Structure

```
.target-dropdown              ← anchor wrapper (position: relative)
  .target-dropdown-btn        ← trigger button ("All targets ▾" or "17 targets ▾")
  .target-dropdown-menu       ← popover panel (position: absolute, open class)
    .target-search            ← text input, filters pills in-place
    .target-dropdown-actions  ← "All" / "None" quick-action links
    .target-pill-list         ← flex-wrap 2-column pill grid
      .target-check (×N)      ← one per target, checkbox hidden
```

### Popover Panel

```css
.target-dropdown-menu {
  width: 360px;
  max-height: 420px;
  border-radius: 10px;         /* rounder than 6px tiles — feels like a panel */
  padding: 10px 12px 12px;
  box-shadow:
    0 8px 24px rgba(0,0,0,0.55),
    0 2px 6px rgba(0,0,0,0.35),
    inset 0 1px 0 rgba(255,255,255,0.08);
}
```

Shadow matches `.stat-expand-popup` — heavy outer shadow lifts the panel
above the page surface.

### Search Input

```css
.target-search {
  background: var(--surface-well);   /* one level deeper than panel */
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 5px 10px;
  font-size: 12px;
}
.target-search:focus { border-color: rgba(126,184,247,0.40); }
```

The search filters pills via DOM `display: none` only — no API call or
re-render. `targetSearch` is a module-level string preserved across
`refresh()` calls so the filter survives pill-click re-renders.

### 2-Column Pill Grid

```css
.target-pill-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.target-dropdown-menu .target-check {
  flex: 1 0 calc(50% - 3px);  /* prefer 2-per-row; long names span full width */
  justify-content: center;
}
```

`flex-basis: calc(50% - 3px)` is half the container minus half the 6px gap.
`flex-grow: 1` means both items in a row expand equally. A pill whose natural
width exceeds the basis (very long target names) wraps to its own full row.

### Pill State

Pills use neutral/accent — no per-target colors. Target colors carry meaning
only on cards where they match the altitude chart line for that target; in
the filter panel there is no chart, so color would be arbitrary noise.

- **Checked** (target visible): `background: rgba(126,184,247,0.12)`, accent border and text
- **Unchecked** (target hidden): neutral dark background, `--text-tertiary`
- Shadow: Raised Pill variant (see §3)

### Stay-Open Behavior

`dropdownOpen` is a module-level boolean. `refresh()` rebuilds the entire
filter bar HTML, which would normally reset the dropdown to closed. On each
`bindListEvents()` call, if `dropdownOpen` is true the `open` class is
immediately re-applied to the new menu element before the browser paints.
