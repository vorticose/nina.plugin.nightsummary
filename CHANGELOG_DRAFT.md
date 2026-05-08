# Night Summary — Changelog


## Unreleased — v3.1.0 (in progress)

**New features**
- Raw image thumbnails (opt-in) — Night Summary can now save a small JPEG thumbnail of every LIGHT frame as it's captured, and surface them in a new dashboard gallery. Three views: per session (from the session detail page), cross-session per target, and per project (when Target Scheduler is installed). Click any thumbnail for a lightbox view with arrow-key navigation. Optional medium 800px thumbnails for higher-quality lightbox. Off by default — enable in Options → Raw Image Thumbnails. Three retention modes: keep all, roll over by days, or roll over by total disk usage. Existing Target Scheduler users can backfill thumbnails from past sessions with a one-click "Import from Target Scheduler" button. Thumbnails are stored at `%LOCALAPPDATA%\NINA\NightSummary\thumbs\{sessionId}\`.


## v3.0.0

**New features**
- Live Dashboard — built-in local web server accessible from any browser on your network, including phones and tablets. Browse your full session history with thumbnails, stat boxes, and altitude charts, and open any past report without regenerating it. View lifetime statistics per target or project. Use a VPN for remote access when viewing away from your home network or if your imaging machine is at an observatory. Enable in Options → Night Summary Settings → Local Dashboard.
- Per-target chip selector on metric charts — a target chip row is now stacked above the per-filter chip row, letting you isolate a single target's data points or combine target and filter to focus on one target/filter combination. Both rows can be independently disabled in settings.

**Bug fixes**
- Fixed overhead analysis incorrectly showing 100% accounted in sessions that ended with an aborted exposure.
- Fixed Target Scheduler progress bars showing duplicate or phantom exposure plans when the same target exists in multiple TS projects. Each project now renders as a separate labeled section.


## v2.11.1

**Bug fixes**
- Reverted graceful session cleanup logic added in v2.11.0 which resulted in some sessions being ended prematurely by sequence interrupt triggers such as "When Becomes Unsafe". Sessions are now only ended by running the Night Summary End sequence instruction. If the End instruction never ran, the session data is preserved — use "Resend Previous Session" to generate a report. Reports from those sessions include a notice that session duration is approximate and overhead analysis is unavailable.


## v2.11.0

**New features**
- Per-filter selector on metric charts -- click a filter chip above any metric chart to show only one filter's data points. Y-axes auto-rescale to the visible subset so per-filter trends are visible at maximum resolution -- especially useful for mono LRGB rotating workflows where alternating filters would otherwise mask the underlying trend within each filter. Filters with only a single image show a centered dot rather than a "no data" message. Hover tooltips show values at full precision (e.g. 1.72 px instead of 1.7 px). Applies to the primary chart and any additional charts, dark and light modes, and works on historical sessions via "Resend Previous Session". Note: the interactive filter selector requires JavaScript -- when reports are opened in email attachment previews (Gmail, iOS Quick Look) or other script-restricted environments, charts display as a static view showing all filters combined with a note explaining how to open the report in a browser for the full interactive version.
- Tonight's Preview now shows a multi-target altitude chart instead of a flat timeline — each scheduled target's altitude curve is plotted over the imaging window with color-coded shading per imaging block and hover tooltips showing the target name and window times. Moon curve shown when enabled. Coordinates are resolved automatically from the Target Scheduler database.

**Improvements**
- Expanded metric chart options from 20 to 35 metrics — added Sky Temperature (user-requested), Sky Brightness, Wind Direction, Wind Gust, Mean ADU, Std Deviation, MAD, Exposure, Gain, Offset, Cooler Setpoint, Rotator Position, Position Angle, Min ADU, and Max ADU. All available as primary, secondary, or x-axis metrics on the main chart and any additional charts.
- Now collecting all 12 ASCOM ObservingConditions weather fields (previously 8) — added Sky Brightness, Sky Temperature, Wind Direction, and Wind Gust so the data is stored even before new chart options use it
- Metric combo boxes reordered by usefulness — most commonly used metrics (HFR, FWHM, Guiding RMS, Star Count) at top, niche metrics (Position Angle, Min/Max ADU) at bottom, grouped by category
- Reorganized the plugin options page for easier navigation — high-frequency actions (Preview Report, Resend Previous Session) are now surfaced at the top, delivery channel settings and equipment profile are grouped behind collapsible sections, and the layout and labelling of controls is more consistent throughout
- Gmail app password hint now links directly to myaccount.google.com/apppasswords instead of describing the navigation path

**Bug fixes**
- Graceful session cleanup when sequence is stopped manually -- if the NINA sequence ends before the Night Summary End instruction runs (manual stop, error, or missing instruction), the session is now finalized automatically with an end time and all listeners are cleaned up. No report is generated or delivered -- use "Resend Previous Session" to get a report from the saved data.
- Rejected frame tracking -- frames rejected by Target Scheduler grading or manually thumbed-down in NINA's thumbnail panel are now counted and shown in the report. The per-target filter table gains a Rejected column when any rejections exist, with a hover tooltip breaking down rejection reasons and counts (e.g. "HFR too high: 4, Guiding RMS: 1" or "Manual: 2"). The session overview shows a rejected count alongside aborted exposures. Manual rejections are detected automatically via file system watching -- no extra setup required.
- Fixed event marker hover tooltips on metric charts not responding -- markers (AutoFocus, Meridian Flip, Safe/Unsafe) now reliably show their tooltip on hover
- Fixed additional chart settings showing dropdowns in a different order than the primary chart -- all chart configurations now show X-Axis, Primary Metric, Secondary Metric in that order
- Fixed filter chip selector causing a slight layout shift when switching filters -- chips are now consistently bold so toggling the active state no longer changes their width
- Fixed equipment section showing only a subset of connected equipment -- now captures equipment names on the first saved image instead of at session start, guaranteeing all devices are connected before the snapshot is taken
- Fixed filter change counts being inflated by no-op filter switches -- the plugin now only counts a filter change when the wheel actually moved, not every time the sequence asked for a filter that was already in position
- Overhead Analysis accuracy improvements: the full meridian flip window (slew + re-center + re-guide + settle) is now captured instead of slew-only; no-op `StartGuiding` calls (when PHD2 is already guiding) are no longer counted; plate solves internal to Center/CenterAndRotate are no longer double-counted alongside the centering event; sequence items that fail validation mid-run no longer leak as orphaned "in-progress" entries; sequencer-caused `WaitForTimeSpan` delays (e.g. post-unsafe safety buffers) are now categorized as `Wait`; and `WaitUntilSafe` (weather-gated) is no longer counted as overhead, since the rig physically cannot image during that time
- Fixed rejected count inflating when Target Scheduler had not finished grading by session end -- images still Pending in TS are no longer miscounted as rejected, and hover tooltips for rejections only show reasons for actually-rejected frames
- Fixed overhead analysis "Overhead Accounted %" dropping below typical values on nights where Target Scheduler had to wait for targets to rise -- idle wait periods are now excluded from the imaging window (the same way roof-closed time already was), so coverage reflects true overhead efficiency
- Fixed aborted exposures with no matching finish (e.g. sequence cut off by an unsafe trigger and NINA left running) inflating overhead with a ghost event extending to end-of-log -- abort duration is now capped at the requested exposure time plus a small grace, or 10 minutes if the requested duration can't be determined
- Fixed "Overhead Accounted %" dropping on nights with PHD2 guide-star failures or sequences cancelled mid-run by roof closure -- failed sequence items (StartGuiding retry timeouts, etc.) and items cancelled by WhenUnsafe now have their full wall-clock time credited to overhead instead of being silently dropped


## v2.10.0

**New features**
- Live Stack integration -- captures live-stacked thumbnails from the Live Stack plugin and displays them in the report per target/filter, with broadband/narrowband grouping and composite support
- Yield and Imaging Overhead Analysis -- parses NINA logs to show a per-category timing breakdown with stacked bar chart and detailed table. Tracks all major NINA sequence items (camera download, filter changes, dithering, autofocus, plate solves, image saves, centering, slew, guiding, dome operations, flat panel, camera temp, mount operations, and more) plus trigger-based meridian flips detected from NINA internal logs. Uses interval merging to accurately handle overlapping concurrent events. Automatically excludes roof-closed (unsafe) periods so safety events don't inflate overhead numbers. Exposures aborted by quality triggers (e.g. guiding RMS threshold) appear as a "Skipped Exposure" category so you can see time lost to poor conditions.
- Equipment profile section in report header -- shows all 12 NINA equipment types (Camera, Telescope, Mount, Filter Wheel, Focuser, Rotator, Guider, Dome, Flat Panel, Safety Monitor, Weather, Switch) with per-field visibility toggles and user-overridable display names
- NINA filename pattern variables in report save path -- use the same path variables as NINA's file save patterns, with clickable insertion buttons
- Customizable x-axis on metric charts -- choose Time, Frame Index, or any metric (Altitude, Temperature, etc.) as the x-axis, independently configurable per chart
- Configurable event markers on metric charts -- vertical dashed lines at AutoFocus, Meridian Flip, and Safe/Unsafe events with per-type toggle settings and hover tooltips (shown when x-axis is Time)
- Median ADU metric -- image median ADU value is now recorded per image and available as a primary, secondary, or x-axis metric in the metric chart. Useful for tracking sky background brightness changes throughout a session.
- Sky position angle displayed in target headers and FOV overlay on sky thumbnails

**Improvements**
- Filter name now shown in metric chart data point hover tooltips
- Tonight's Preview now shown even when session has zero images (weather-interrupted sessions)
- Session history now returns all previous sessions instead of a capped limit
- Plugin version and NINA version shown in report footer
- Settings now persist to a stable JSON file that survives NINA updates
- Per-filter exposure breakdown in overview now uses FormatDuration for consistent time formatting
- Active sessions show "In Progress" with duration so far within the preview window instead of negative numbers
- Updated Gmail app password setup instructions with direct link to Google app passwords page

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
