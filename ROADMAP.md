# Night Summary Plugin - Development Roadmap


## V1 — Core (Complete)

- Plugin loads in NINA with icon
- Start Session and End Session sequencer instructions
- SQLite database recording all image data per session
- HTML email report with dark theme styling
- Gmail SMTP sending
- Settings UI in NINA options panel
- Dynamic NINA version path



## V2 — Notifications & Data Quality (Complete)

- Pushover integration for instant mobile notifications on session end
- Discord webhook integration for session summaries
- FWHM and Eccentricity via Hocus Focus plugin integration
- HFR over time chart in HTML report (inline SVG, renders in browser)
- Full HTML report attached to email and Discord messages (plain-text summary in email body)
- Test notification buttons in settings UI (per-channel ping + full test report from test database)
- Per-target visual separators in HTML report
- Settings UI refinements (global send toggle, reorganized sections)



## V2.1 — Session Event Timeline & Logging (Complete)

- Session event timeline near the top of the HTML report (inline SVG):
  - Target imaging periods as color-coded bands (one color per target, consistent with per-target report sections)
  - Roof open/close markers
  - Autofocus run markers
  - Meridian flip markers
  - Ruler-style time axis with adaptive tick intervals
  - Interactive hover tooltips on event markers
- Safety monitor event logging (roof open/close with timestamps)
- Autofocus run logging (filter, temperature, focuser position)
- Meridian flip logging
- Save HTML report locally to Documents/NINA/Night Summary/Saved Reports/ with generation timestamp in filename



## V2.2 — Target Scheduler Integration & Brand Icons (Complete)

- Target Scheduler completion percentages per target/filter (progress bars: accepted vs acquired vs desired)
- Per-target cumulative integration time from Target Scheduler database
- Custom filter sort order (L, R, G, B, Ha, Sii, Oiii, then others alphabetically)
- Discord and email brand icons in report header
- Sequencer instruction name and description cleanup



## V2.3 — Historical Context (Complete)

- Per-target session history table in report: date, integration, avg HFR, avg FWHM, avg guiding RMS — collapsible, up to 5 previous sessions
- Per-target cumulative integration time from own session database
- Survey image per target in HTML report (SkyView URL-based, using RA/Dec from image metadata; inline in saved report, link in email)



## V2.4 — Tonight's Opportunity & FOV (Complete)

- FOV overlay on survey image using Aladin Lite JS widget (sensor + focal length from NINA profile, rotation from Target Scheduler)
- Per-target altitude curve (inline SVG, one per target)
  - Full 24-hour noon-to-noon arc showing complete rise/set cycle
  - Session imaging window highlighted with vertical Start/End markers and subtle fill
  - Y axis: altitude in degrees (0°, 30°, 60°, 90° grid lines); horizon shaded red
  - X axis: time labels every 3 hours
  - Computed from target RA/Dec + observer lat/lon (NINA profile) using spherical trig — no external library
  - Moon separation at session midpoint shown below chart
  - RA/Dec sourced from Target Scheduler if available, falls back to image metadata



## V2.5 — External Data & Diagnostics

- Equipment error logging (connection losses, reconnection attempts, device affected)
- Astrospheric or Clear Outside weather snapshot embedded in report



## V3 — Pier Camera & Timelapse

- Separate plugin or standalone app for nightly timelapse creation
- Time-synced metadata overlay showing imaging events
- Weather event correlation with visual anomalies in timelapse
- Night Summary embeds timelapse thumbnail/link in report



## V4 — Polish & Publishing

- DPAPI encryption for Gmail app password
- Proper SVG icons for sequencer instructions
- Report preview in NINA settings panel
- NINA plugin marketplace submission
- Settings UI refinements
