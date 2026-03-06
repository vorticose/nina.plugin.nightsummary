\# Night Summary Plugin - Development Roadmap



\## V1 — Core (Complete)

\- Plugin loads in NINA with icon

\- Start Session and End Session sequencer instructions

\- SQLite database recording all image data per session

\- HTML email report with dark theme styling

\- Gmail SMTP sending

\- Settings UI in NINA options panel

\- Dynamic NINA version path



\## V2 — Notifications \& Data Quality (Complete)

\- Pushover integration for instant mobile notifications on session end

\- Discord webhook integration for session summaries

\- FWHM and Eccentricity via Hocus Focus plugin integration

\- HFR over time chart in HTML report (inline SVG, renders in browser)

\- Full HTML report attached to email and Discord messages (plain-text summary in email body)

\- Test notification buttons in settings UI (per-channel ping + full test report from test database)

\- Per-target visual separators in HTML report

\- Settings UI refinements (global send toggle, reorganized sections)



\## V2.1 — Session Event Timeline & Logging (Complete)

\- Session event timeline near the top of the HTML report (inline SVG):
  - Target imaging periods as color-coded bands (one color per target, consistent with per-target report sections)
  - Roof open/close markers
  - Autofocus run markers
  - Meridian flip markers
  - Ruler-style time axis with adaptive tick intervals
  - Interactive hover tooltips on event markers
\- Safety monitor event logging (roof open/close with timestamps)
\- Autofocus run logging (filter, temperature, focuser position)
\- Meridian flip logging
\- Save HTML report locally to Documents/NINA/Night Summary/Saved Reports/ with generation timestamp in filename



\## V3.2 — Historical Context

\- Compare current session to previous sessions on same target (HFR, FWHM, guiding RMS)
\- Per-target cumulative integration time from our own session database
\- Survey image per target in HTML report (DSS/SkyView URL-based, using RA/Dec from image metadata)



\## V3.3 — Target Scheduler Integration

\- Target Scheduler completion percentages per target/filter
\- Per-target cumulative integration time from Target Scheduler database
\- Tonight's opportunity snapshot (altitude curves, moon separation, imaging windows)
\- FOV overlay on survey image using Aladin Lite JS widget (sensor + focal length from NINA profile, rotation from Target Scheduler)



\## V3.4 — External Data & Diagnostics

\- Equipment error logging (connection losses, reconnection attempts, device affected)
\- Astrospheric or Clear Outside weather snapshot embedded in report



\## V4 — Pier Camera \& Timelapse

\- Separate plugin or standalone app for nightly timelapse creation

\- Time-synced metadata overlay showing imaging events

\- Weather event correlation with visual anomalies in timelapse

\- Night Summary embeds timelapse thumbnail/link in report



\## V5 — Polish \& Publishing

\- DPAPI encryption for Gmail app password

\- Proper SVG icons for sequencer instructions

\- Report preview in NINA settings panel

\- NINA plugin marketplace submission

\- Settings UI refinements





