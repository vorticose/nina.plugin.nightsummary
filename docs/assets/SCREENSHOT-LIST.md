# Screenshot List

Copy-paste each filename when saving your screenshot.
After capturing, drop the file into `docs/assets/` and the preview server will reload automatically.

---

## Status

| File | Page | Status |
|------|------|--------|
| `hero-report.png` | index.md | ⚠️ recapture |
| `plugin-icon.png` | index.md | ✅ ok |
| `settings-page.png` | getting-started.md | ⚠️ recapture |
| `sequence-instructions.png` | getting-started.md | ✅ ok |
| `preview-window.png` | getting-started.md | ⚠️ recapture |
| `stat-boxes.png` | report-sections.md | ✅ ok |
| `event-timeline.png` | report-sections.md | ⚠️ recapture |
| `overhead-section.png` | report-sections.md | ✅ ok |
| `target-area.png` | report-sections.md | ✅ ok |
| `ts-progress-bars.png` | report-sections.md | ✅ ok |
| `per-target-iq.png` | report-sections.md | ✅ ok |
| `metric-chart.png` | report-sections.md + metric-charts.md | ⚠️ recapture |
| `tonights-preview.png` | report-sections.md | ⚠️ recapture |
| `discord-embed.png` | delivery-channels.md | ✅ ok |
| `settings-full.png` | settings-reference.md | ✅ ok |
| `equipment-settings.png` | equipment-profile.md | ✅ ok |
| `equipment-report.png` | equipment-profile.md | ✅ ok |
| `file-naming-settings.png` | file-naming-patterns.md | ✅ ok |
| `overhead-detail.png` | overhead-breakdown.md | ✅ ok |
| `livestack-report.png` | live-stack-integration.md | ✅ ok |
| `ts-bars-detail.png` | target-scheduler-integration.md | ✅ ok |
| `dashboard-sessions.png` | dashboard.md | ❌ needed |
| `metric-chart-filter.png` | metric-charts.md + report-sections.md | ❌ needed |
| `rejected-frames.png` | report-sections.md | ❌ needed |

---

## New Screenshots Needed

### dashboard-sessions.png
**Page:** dashboard.md

The dashboard Sessions tab in a browser showing at least 2-3 session cards. Each card should show:
- Target badges (color-coded pills) at the top
- Sky thumbnails for one or more targets
- Stat boxes (FRAMES, INTEGRATION, HFR, GUIDING, MOON)
- Altitude chart at the bottom of the card

Ideal: dark mode, 2-3 sessions visible, one card with multiple targets. Capture in a desktop browser at normal zoom so the full card layout is visible. The filter bar at the top (target picker, date range, etc.) should be visible.

---

### metric-chart-filter.png
**Page:** metric-charts.md + report-sections.md

A metric chart from a multi-target, multi-filter session showing **both** chip rows — target chips on top, filter chips below. One chip in each row should be selected (not "All") so both selectors are visibly active simultaneously. The chart should show the Y-axis rescaled to the selected target+filter combination.

Ideal: a session with 2+ targets (e.g. M51, NGC 7000) and 2+ filters (e.g. Ha, OIII, Lum). Select Target=M51 and Filter=Ha so both rows show an active selection. HFR on the Y-axis with a visible trend. Capture the full chart including both chip rows and the chart area.

---

### rejected-frames.png
**Page:** report-sections.md

The per-target filter table from a session where at least one filter has rejected frames. The table should show the Rejected column with a non-zero count in at least one row. Hover state is not needed — just the table with the count visible.

Ideal: a row with something like "Ha | 42 | 300s | 3.5h | 3 rejected" where the rejected count is clearly visible.

---

## Existing Screenshot Notes

These screenshots exist but need recapture:

- **`hero-report.png`** ⚠️ — recapture showing a v3-era report. Full detail level, dark mode, header through first target section.

- **`settings-page.png`** ⚠️ — recapture showing the updated settings panel including the new **Local Dashboard** section. NINA Options → Night Summary Settings, scrolled to show the Local Dashboard block (Enable, Port, Start/Stop, Tailscale URL, Generate All Reports).

- **`preview-window.png`** ⚠️ — recapture showing the current Preview Report window with a recent report rendered.

- **`event-timeline.png`** ⚠️ — must show the **Altitude view** with the "Altitude / Simple" toggle chips visible. Multi-target session preferred so multiple altitude curves show. Capture from a rendered HTML report in a browser, wide enough to show the full x-axis.

- **`metric-chart.png`** ⚠️ — needs the filter chip row (and target chip row if multi-target session available) visible below the chart, with one chip selected. Event markers (AF/MF vertical dashed lines) are a bonus. This is the overview screenshot for report-sections.md.

- **`tonights-preview.png`** ⚠️ — recapture showing the current Tonight's Preview section layout.

Already-ok screenshots to verify before publishing:

- **`stat-boxes.png`** — confirm Yield stat box is present (v2.10.0)
- **`per-target-iq.png`** — confirm Rejected column header is visible
- **`overhead-detail.png`** — confirm stacked bar chart is color-coded with 8+ category rows
