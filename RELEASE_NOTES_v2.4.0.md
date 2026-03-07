# Night Summary v2.4.0

This release covers three milestones (V2.2, V2.3, V2.4) since the v2.1.0 release.

---

## What's New

### Altitude Curve (V2.4)
Each target section now includes an inline altitude chart generated from your NINA profile's observer location and the target's RA/Dec coordinates.

- Plots the full 24-hour rise/set arc (noon-to-noon) so you can see the complete trajectory
- Highlights the actual imaging window for that target with vertical Start/End markers and a subtle shaded region
- Y axis: 0°, 30°, 60°, 90° grid lines; below-horizon region shaded
- X axis: time labels every 3 hours
- Moon separation at imaging midpoint shown below the chart
- RA/Dec sourced from Target Scheduler if available, falls back to FITS header metadata
- Requires observer latitude/longitude to be set in NINA (Options → General → Location)

### FOV Overlay on Survey Images (V2.4)
The per-target sky survey thumbnail now displays a camera FOV overlay rectangle showing the exact sensor footprint at the correct rotation angle (sourced from Target Scheduler).

### Session History Table (V2.3)
Each target section includes a collapsible history table showing up to 5 previous sessions with date, total integration, avg HFR, avg FWHM, and avg guiding RMS.

### Sky Survey Thumbnails (V2.3)
Each target in the report now shows a DSS2 color survey image thumbnail centered on the target coordinates.

### Cumulative Integration (V2.3)
Total integration time across all sessions is displayed per target, combining the current session with historical data from the Night Summary database.

### Target Scheduler Progress Bars (V2.2)
When Target Scheduler is installed, each target shows per-filter progress bars displaying accepted vs. acquired vs. desired exposures, plus total cumulative integration from the TS database.

### Filter Sort Order (V2.2)
Filters are now sorted consistently: L, R, G, B, Ha, Sii, Oiii, then any others alphabetically.

### Brand Icons (V2.2)
Discord and email icons added to the report header to identify which notification channels delivered the report.

---

## Requirements

- N.I.N.A. 3.0 or later
- Observer location configured in NINA profile (for altitude chart)
- Target Scheduler plugin (optional — enables progress bars, FOV rotation, and RA/Dec fallback)
- Hocus Focus plugin (optional — enables FWHM and Eccentricity metrics)
