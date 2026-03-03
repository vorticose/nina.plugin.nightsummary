\# Night Summary Plugin - Development Roadmap



\## V1 — Core (Complete)

\- Plugin loads in NINA with icon

\- Start Session and End Session sequencer instructions

\- SQLite database recording all image data per session

\- HTML email report with dark theme styling

\- Gmail SMTP sending

\- Settings UI in NINA options panel

\- Dynamic NINA version path



\## V2 — Notifications \& Data Quality

\- Pushover integration for instant mobile notifications on session end

\- Discord webhook integration for session summaries

\- FWHM and Eccentricity via Hocus Focus plugin integration

\- Rejected image tracking with configurable HFR/FWHM thresholds

\- Star count timeline as cloud/transparency proxy

\- Test notification button in settings UI



\## V3 — Context \& Intelligence

\- Target Scheduler integration — completion percentages per target/filter

\- Per-target cumulative integration time pulled from Target Scheduler database

\- Compare current session to previous sessions on same target (HFR, FWHM, guiding RMS)

\- Tonight's opportunity snapshot — altitude curves and imaging windows for active Target Scheduler targets, including moon separation and best darkness window

\- Astrospheric or Clear Outside weather snapshot embedded in report

\- Safety monitor event logging — roof open/close events with timestamps

\- Equipment error logging — connection losses, reconnection attempts, and which device was affected

\- Autofocus trigger logging — when autofocus runs were initiated and which trigger caused them

\- Session interruption notes (meridian flip times)



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

```



Save and close Notepad, then commit:

```

git add ROADMAP.md

git commit -m "Add development roadmap"

git push

