# Night Summary — Changelog


## v2.10.0-beta2

**Bug fixes**
- Fixed Yield and Imaging Overhead section not appearing in live session reports
- Fixed new reports overwriting previous session's saved report folder
- Fixed new settings defaulting to off for users upgrading from a previous version


## v2.10.0-beta1

**Beta release** — please report any issues at https://github.com/vorticose/nina.plugin.nightsummary/issues

**New features**
- Live Stack integration — captures live-stacked thumbnails from the Live Stack plugin and displays them in the report per target/filter, with broadband/narrowband grouping and composite support
- Yield and Imaging Overhead Analysis — parses NINA logs to show a per-category timing breakdown with stacked bar chart and detailed table. Tracks all major NINA sequence items (camera download, filter changes, dithering, autofocus, plate solves, image saves, centering, slew, guiding, dome operations, flat panel, camera temp, mount operations, and more). Uses interval merging to accurately handle overlapping concurrent events.
- Equipment profile section in report header — shows all 12 NINA equipment types (Camera, Telescope, Mount, Filter Wheel, Focuser, Rotator, Guider, Dome, Flat Panel, Safety Monitor, Weather, Switch) with per-field visibility toggles and user-overridable display names
- NINA filename pattern variables in report save path — use the same path variables as NINA's file save patterns, with clickable insertion buttons
- Customizable x-axis on metric charts — choose Time, Frame Index, or any metric (Altitude, Temperature, etc.) as the x-axis, independently configurable per chart
- Sky position angle displayed in target headers and FOV overlay on sky thumbnails

**Improvements**
- Tonight's Preview now shown even when session has zero images (weather-interrupted sessions)
- Session history now returns all previous sessions instead of a capped limit
- Plugin version, and NINA version shown in report footer
- Settings now persist to a stable JSON file that survives NINA updates
- Per-filter exposure breakdown in overview now uses FormatDuration for consistent time formatting
- Active sessions show "In Progress" with duration so far within the preview window instead of negative numbers

**Bug fixes**
- Fixed filter classification progressive reset on settings load/refresh



## v2.9.0

**New features**
- Seeing FWHM metric — ASCOM seeing monitor star FWHM (arcseconds) is now recorded per image and available as a primary or secondary metric in the metric chart. Requires an ASCOM-compatible seeing monitor connected as a NINA weather data source.
- Expandable filter breakdown in the session overview stat boxes — click Total Images or Total Exposure to see a per-filter breakdown
- Support for up to 4 additional metric charts per report — configure extra charts from the Options page, each with independent primary and secondary metric selection

**Improvements**
- Improved metric chart axis scaling — axis labels now use sensible round-number steps (e.g. 5° for altitude, 0.5px for HFR) instead of arbitrary intervals
- Aborted exposure count now includes image download and save failures in addition to skipped exposures
- Target Scheduler settings grouped into a dedicated subsection in Options for clarity
- Tonight's Preview toggle is now greyed out when the Target Scheduler API is not enabled, preventing misconfiguration
- Target Scheduler progress bars are suppressed in the report when Target Scheduler is not installed, eliminating spurious warnings for users without TS
- Discord webhook URL validation no longer rejects legacy discordapp.com webhook URLs — the actual API response is used to determine success or failure instead


## v2.8.1

**New features**
- Light mode — reports can now be generated in a light theme, toggled in Options.
- All metrics collected by NS that can be graphed in the metric chart are now available as options. Added sky quality, cloud cover, camera temperature, dew point, wind speed, atmospheric pressure, star count, and azimuth.

**Improvements**
- Added a backup thumbnail image service (NASA SkyView DSS2).
- Reports with multiple targets generate noticeably faster (thumbnails fetched in parallel).

**Bug fixes**
- Fixed preview window failing to load on large sessions.
- Fixed a database issue that would result in historical session data not being carried forward with NINA updates. The fix migrates all legacy NS databases to a folder unaffected by NINA updates.


## v2.8.0

**New features**
- Report Preview window — preview your report with real session data or test data directly from the Options page using a built-in viewer
- Minimum altitude line on altitude chart — when Target Scheduler is installed, the per-target altitude chart shows a dotted red line at the project's minimum altitude setting, with a new toggle in Options
- Added 4 new metric chart options: Altitude, Airmass, Humidity, and Focuser Position
- Added option to expand all report sections by default instead of collapsed

**Improvements**
- Hover tooltips on metric chart data points show timestamp and value
- Target Scheduler features now silently skip when TS is not installed instead of showing toast warnings



## v2.7.0

**New features**
- Aborted exposure tracking — detects exposures that were skipped or aborted during the session (e.g. by RMS triggers, safety monitor events, or manual skip) and displays the count in the session overview, email, Discord, and Pushover summaries
- Save report path override — browse for a custom folder to save local HTML reports instead of the default Documents location

**Improvements**
- Updated Target Scheduler API enable instructions with more precise navigation steps

**Bug fixes**
- Fixed HFR units displayed as arcseconds (") instead of pixels (px) in email, Discord, and Pushover text summaries


## v2.6.3

**Improvements**
- Filter classification UI — users can manually classify broadband/narrowband/exclude per filter in plugin options for Star Count CV calculation
- Added diagnostic logging for Tonight's Preview TS API checks — logs profile ID, API enabled status, port, and connection URL for easier troubleshooting
- Report warnings banner — any issues encountered during report generation are now shown in an amber box at the top of the report

**Bug fixes**
- Fixed Tonight's Preview failing with 400 Bad Request for users in positive UTC timezones (e.g. UTC+2) 
- Fixed issue where calibration frames where being recorded and reported on. Only LIGHT frames are now recorded — darks, flats, bias, and snapshot frames are excluded from session data
- Filter classification for Star Count CV now uses first-letter matching, supporting common filter naming variants (Luminance, Red, Halpha, Sulfur, etc.) when in auto mode.  Users can also manually classify filters in plugin options.
- Target Scheduler queries now filter by the active NINA profile, fixing incorrect results for users with multiple profiles


## v2.6.0

**Tonight's Preview**
- New report section showing Target Scheduler's planned schedule for the next night, powered by the TS REST API
- Visual SVG timeline from first target to end of night, with colored blocks per target and hatched wait periods
- Per-target summary table with imaging window, image count, and total time
- Expandable per-target filter breakdown matching the main report's grouping (same filter + same exposure = one row, different exposures = separate rows)
- Sunset-anchored start time computed from observer coordinates
- Graceful degradation with specific in-report messages when TS is not installed, API is disabled, or the API is unreachable

**Notifications**
- NINA toast notifications for report generation and delivery — success, warning, and error states
- Warnings shown when report sections are omitted (e.g. Tonight's Preview unavailable)

**Options UI**
- Target Scheduler options (progress bars and Tonight's Preview) are now greyed out with a "Target Scheduler not installed" message when TS is not detected
- "Show TS Progress Bars" renamed to "Show Target Scheduler Progress"
- Homepage and changelog links added to the plugin page in NINA

**Improvements**
- Report generated once and shared across all delivery channels, eliminating redundant generation
- Separate HTTP client for TS API calls with 60-second timeout


## v2.5.2

**Bug fixes**
- Fixed mixed-exposure filter grouping — same filter with different exposure lengths now correctly appear as separate rows
- Default detail level changed to Full with all sections enabled


## v2.5.1

**Bug fixes**
- Fixed long description formatting in NINA plugin window — em-dashes replaced with regular dashes to prevent jumbled text


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

**Bug fixes**
- DSS sky survey thumbnail and altitude chart now render correctly when Target Scheduler is not installed; previously both required TS data even when RA/Dec was available from image metadata

**First-run experience**
- Demo session data (M31 + Rosette Nebula) bundled with the plugin — Send Test Report works out of the box on a fresh install with no setup required


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
