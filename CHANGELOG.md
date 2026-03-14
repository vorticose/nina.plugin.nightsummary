# Night Summary — Changelog


## v2.5.0

**Report improvements**
- Eccentricity added as a standalone image quality metric throughout the report
- Per-target image quality section with HFR, FWHM, Eccentricity, and guiding RMS — each with expandable per-filter breakdowns
- HFR chart replaced with a configurable Metric Chart — choose any two metrics to plot over time (HFR, FWHM, Eccentricity, Guiding RMS, Focuser Temperature, Ambient Temperature)
- Report detail levels: Snapshot (header and filter table), Standard (adds timeline, charts, image quality), Full (adds metric chart and session history) — each section also individually toggleable

**Data collection**
- Additional image metadata now recorded per capture: gain, offset, binning, camera temperature, cooler setpoint, focuser position, rotator position, humidity, dew point, wind speed, and atmospheric pressure
- Target Scheduler grading sync — accepted/rejected status from the Target Scheduler database is matched to recorded images at session end

**Email**
- Generic SMTP support — any SMTP provider now works (Outlook, Yahoo, iCloud, and others); Gmail remains the default with simplified setup
- Resource leak fix — MailMessage objects now correctly disposed after each send

**Options UI**
- Email section redesigned with Gmail / Other provider radio button selection; Other provider shows full SMTP fields and per-provider setup guidance
- Input validation added to all three test commands — catches malformed addresses, wrong URL format, and short/invalid tokens before attempting a send
- Resend Previous Session section moved to the top of the options page


## v2.4.0

**FOV overlay and altitude charts**
- FOV overlay on the DSS sky survey thumbnail using sensor dimensions and focal length from the NINA equipment profile, with rotation from Target Scheduler where available
- Per-target altitude curve — full 24-hour rise/set arc with the session imaging window highlighted, computed from target RA/Dec and observer location using spherical trigonometry
- Moon separation at session midpoint shown below each altitude chart


## v2.3.0

**Historical context**
- Per-target session history table — date, integration time, average HFR, average FWHM, and average guiding RMS for up to five previous sessions; collapsible
- Per-target cumulative integration time from the Night Summary session database
- DSS sky survey thumbnail per target, sourced from SkyView using RA/Dec from image metadata


## v2.2.0

**Target Scheduler integration**
- Per-filter progress bars showing desired, acquired, and accepted frame counts from the Target Scheduler database
- Per-target cumulative integration time from the Target Scheduler database
- Custom filter sort order: L, R, G, B, Ha, Sii, Oiii, then others alphabetically

**Report improvements**
- Discord and email brand icons in the report header
- Sequencer instruction names and descriptions cleaned up


## v2.1.0

**Session event timeline**
- Inline SVG timeline near the top of the report showing target imaging periods as color-coded bands, with markers for AutoFocus runs, meridian flips, and safety monitor events
- Ruler-style time axis with adaptive tick intervals
- Interactive hover tooltips on event markers

**Event logging**
- Safety monitor events logged with timestamps (roof open / roof closed)
- AutoFocus runs logged with filter, temperature, and focuser position
- Meridian flips logged

**Saved reports**
- HTML report can now be saved locally to `Documents\N.I.N.A.\Night Summary\Saved Reports\` with a generation timestamp in the filename


## v2.0.0

**New notification channels**
- Pushover — instant push notification on session end with a per-target image summary
- Discord — full session summary embed posted to a Discord server via webhook, with the HTML report attached as a file

**Report improvements**
- FWHM and Eccentricity metrics included when the Hocus Focus plugin is installed
- HFR over time chart added as an inline SVG
- Per-target sections now have clear visual separators
- HTML report sent as an attachment across all channels

**Settings improvements**
- Test buttons for each notification channel
- Full test report from a separate test database, isolated from real session data


## v1.0.0

- Records all images captured during a NINA sequence — target name, filter, exposure duration, HFR, and star count logged automatically
- Sends a dark-themed HTML email report on sequence completion
- Per-target and per-filter breakdowns with total exposure times and image counts
- Gmail SMTP configuration in the NINA options panel
- Two sequencer instructions: **Night Summary Start** and **Night Summary End**
