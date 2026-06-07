# Night Summary — Changelog


## Unreleased — v3.2.0 (in progress)

<!-- DRAFT: companion + readonly + bug-fix sections below need stable-to-stable consolidation before release -->

### Companion pairing

Pairing-token rollout for the standalone Night Summary Companion app. The shared `CompanionApiKey` field stays in place for the transition window — existing companions keep syncing without changes — but new companions can now be set up entirely in a browser via a generated per-companion token, without copy-pasting JSON. Fold this section into whichever release pulls `feature/companion-rd` into `dev`.

**New features**
- **Companion Pairing panel in Options** — under Dashboard Server → Companion Pairing. Click **+ Generate Token** to issue a 16-character per-companion token (formatted as `XXXX-XXXX-XXXX-XXXX`); the plain token is shown once with a Copy button. Lists paired companions and unclaimed tokens with humanized timestamps; **Revoke** is per-entry and confirms before disabling the pairing.
- **Setup wizard in the Companion app** — fresh installs now redirect from the dashboard to a 5-step browser wizard (Welcome → Connect → Pair → Sync settings → First sync). Specific user-facing messages for each failure mode: unknown token, revoked token, already-paired-with-another-companion, server-doesn't-support-pairing, connection refused, timeout.
- **`/api/companion/info`** (unauthenticated) returns the Night Summary version, NINA version, and paired-companion count so the wizard can distinguish "wrong host" from "wrong software" before any token exists.
- **`/api/companion/pair` and `/api/companion/revoke`** — claim and revoke pairing tokens over HTTP. The companion side sends these via new `/api/setup/probe` and `/api/setup/claim` proxy endpoints so the wizard isn't blocked by browser CORS.

**Improvements**
- **Dual-auth on existing sync endpoints** — `/api/export/*` accepts either the new per-companion pairing token (preferred) or the legacy shared `CompanionApiKey` as `Authorization: Bearer`. When a request uses the legacy key, a one-shot deprecation warning is logged ("Re-pair the companion to migrate to a pairing token") and the request still succeeds. Companions can migrate at any time without downtime.
- **Pairing tokens are stored separately** from the main settings file — in `%LOCALAPPDATA%\NINA\NightSummary\companion_tokens.json` — so they survive plugin updates and database migrations, and don't get included in companion sync payloads.
- **Token storage uses SHA-256 hashing + constant-time lookup** so the plain token can never be recovered from the sidecar file even with full disk access. Atomic write with `.tmp` + rename and retry-on-sharing-violation so concurrent reads can't see a torn file.

**Migration notes**
- Existing companions with only `nina.apiKey` set keep working. The deprecation warning is informational; nothing breaks.
- The legacy `nina.apiKey` fallback is scheduled for removal "next release after wizard ships" (two releases out by current plan) — see `COMPANION_PAIRING_DESIGN.md` for the full migration timeline.
- After re-pairing through the wizard, `companion.json` gains a `nina.pairingToken` field. The old `nina.apiKey` is left in place but ignored when the pairing token is set (token takes precedence).

**Internal / API surface**
- New `CompanionTokenStore` (`Add` / `FindByToken` / `Revoke` / `MarkPaired` / `TouchLastUsed`) backed by `companion_tokens.json` with atomic writes, soft-delete revocation, and constant-time lookups.
- `IPluginSettings` gains a default `NinaVersion` property (returns "" unless the host overrides) so `/api/companion/info` can surface the NINA build version.
- `ICompanionController` gains `ProbePrimaryAsync` and `ClaimPairingAsync`.
- 85 new tests across `CompanionTokenStoreTests`, `CompanionTokenViewTests`, `CompanionPairingEndpointsTests`, `CompanionAuthShimTests`, `CompanionWizardEndpointsTests`. Full suite at 818 passing.


### Companion desktop integration

Native-feeling install + launch experience for the standalone Companion app on all three platforms. Fold into whichever release ships the companion.

**New features**
- **Real app icon on every platform** — the Companion now carries the Night Summary brand icon: an embedded `.ico` on the Windows launcher (Explorer + taskbar), a `.icns` in the macOS `.app` (Finder / Login Items), and a `.desktop` entry + PNG on Linux (app menu / launcher, registered by `install.sh`).
- **Start at login** — a one-click toggle in the Companion's Settings tab enables autostart with no admin rights or code-signing: a Startup-folder shortcut on Windows, a LaunchAgent on macOS, and a `systemd --user` unit on Linux.
- **One-file Windows launcher** — `NightSummaryCompanion.exe` is now a single windowless (no console) app you double-click directly, with the native dependencies baked in so you can drop it anywhere (Desktop, pin to taskbar) — no surrounding folder required. It replaces the previous `.vbs` + `.cmd` pair; the dashboard Restart button is handled in-process by a self-respawn. Config + synced data live in `%LOCALAPPDATA%\NightSummaryCompanion`, so moving or updating the exe never loses settings or history. (The macOS `.app` and Linux binary are likewise fully self-contained.)
- **Linux AppImage** — alongside the tarball, Linux now ships a single double-click `NightSummaryCompanion-x86_64.AppImage` (no extract/install step). Start-at-login on a Linux AppImage correctly points the systemd unit at the stable AppImage file, so autostart survives across runs.
- **Linux `.deb` package** — for Debian/Ubuntu/Mint/Pop, a `.deb` you double-click to install via the Software Center (or `sudo apt install ./…deb`): no manual `chmod`, it lands in the app menu with its icon, and it auto-pulls the `libfontconfig1`/`libfreetype6` runtime libraries. Three Linux delivery options now: `.deb` (Debian family), AppImage (portable), tarball (manual/headless).

**Improvements**
- Settings tab process-control wording, the Quit/Restart confirmations, and the autostart status now match the OS the companion is actually running on (no more macOS "Applications folder" text on Windows/Linux).
- The autostart status shows a simple **Enabled** with a hover tooltip explaining what was installed; the "Sync when the companion starts" option moved next to "Accept push notifications."
- **Live first-sync progress** — the setup wizard's first sync now shows a moving progress bar with the current phase and download size (e.g. "Step 4 of 5 — Downloading thumbnails… 11.8 MB") instead of a silent spinner that looked stuck.
- **Opening the app always shows the dashboard** — the Companion is a headless background agent, so before this, double-clicking the icon while it was already running (e.g. started at login) appeared to do nothing. Launching it now opens the dashboard in your browser every time — if an instance is already running it just brings that one's dashboard up instead of starting a second. Autostart-at-login stays silent (no surprise browser tab on every boot).
- **Pairing survives updates on all platforms** — the Companion's config (host + pairing token) now always lives in the per-user app-data dir, outside the install artifact, so replacing the app on update no longer wipes it: pair once, not once per update (macOS `~/Library/Application Support`, Windows `%LOCALAPPDATA%`, Linux `~/.local/share`). A config left over from an older build beside the binary is migrated across automatically on first launch. Config writes are now atomic with a `.bak` fallback, so a crash mid-save can't lose your pairing.
- **Dashboard appears instantly and refreshes itself after a sync** — launching the Companion now shows the dashboard immediately (rendering the data already on disk) instead of waiting out the initial sync first; the fresh data fills in automatically when the background sync lands. This live auto-refresh also applies to scheduled and push-triggered syncs while you're looking at the Sessions or Stats view — new sessions show up on their own, no manual reload.


### Read-Only Mirror + bug fixes

**New features**
- Read-Only Mirror — a second dashboard instance bound to a separate port that refuses every write action at the server level, designed to sit behind a reverse proxy (Caddy / nginx / Cloudflare Tunnel) or Tailscale Funnel so the public-facing dashboard cannot mutate state. Enable in Options → Local Dashboard → Read-Only Mirror; default port 8281. See the new Public Exposure docs page for setup recipes for all four exposers.

**Bug fixes**
- Raw image thumbnails no longer include NINA's star/HFR annotation overlay. When the user's profile had Imaging → Annotate Image enabled, the bitmap NINA delivered to the plugin was the post-annotation version with HFR numbers and detection circles baked in, and that ended up as the saved thumbnail. Thumbnails are now captured from the pre-annotation stretched bitmap, regardless of the Annotate Image setting.
- Target Scheduler grading: frames that TS hasn't finished grading yet (status: Pending) no longer render as "Manual Rejected" in the dashboard Frames view and lightbox, and no longer drop out of integration totals / frame counts on the session card, target detail panel, and lifetime stats. The session-end TS sync was writing Accepted=false for Pending images; the dashboard now treats Pending as not-rejected everywhere and the session-end sync only flips Accepted=false on an explicit TS Rejected (2) verdict.
- Target Scheduler grading: when TS reaches a verdict for an image after the session has ended (e.g., it needed more frames to compare against), the dashboard now picks up the new grading the next time you open the session — a background re-sync runs after the session detail loads and refreshes the NS database in place. Skipped automatically when nothing is Pending, so already-graded sessions pay no cost.
- Targets imaged in two or more non-continuous windows during a single session (e.g., the target set before the meridian and rose again later, or Target Scheduler swapped it out and back in) no longer render as one continuous block. The altitude chart highlights each imaging window separately, and the per-target filter table is split into one sub-table per window with a Grand Total row across all windows.
- Dashboard: opening the Frames gallery from a session report that was launched from a target or project detail panel no longer breaks the in-page back button — back now returns to the originating TDP/PDP via the report, instead of dead-ending on the Sessions list.
- Dashboard: long values in the Frames lightbox stat boxes (e.g., a long Target Scheduler project name or Exposure Profile name) no longer overflow the box on mobile — they now truncate with an ellipsis like file paths already did.
- Dashboard: the report view no longer collapses to a tiny ~150px sliver in browsers without dynamic-viewport (`dvh`) support — Firefox < 101, older Safari, older WebViews. The report height used `calc(100dvh − header)` with no fallback; those browsers couldn't compute it, so the flex layout collapsed and the report iframe fell back to its intrinsic height. Now `@supports`-gated with a `100vh` fallback so the report fills the window in every engine.
- Overhead Analysis: fixed a phantom multi-hour "Wait" category that could appear on sessions where a `WaitForTimeSpan` was started inside a safety-recovery container (When Unsafe / OnceSafe / WhenPlugin IfContainer) and then orphaned when the parent container exited without logging a finish line for the child wait. A later sequence interrupt would flush the stale wait with its wall-clock span. Wait events are now capped at the requested duration parsed from the log, with a small grace.
- Overhead Analysis: "Overhead Accounted %" no longer silently pegs at 100%. Several issues conspired to push the numerator past the denominator and clamp the ratio: overhead events running concurrently with exposures (image saves, plate solves, derived camera-download tail) were counted in the numerator but excluded from the denominator; and on sessions where the safety monitor logged duplicate RoofClosed/RoofOpen pairs in tight succession, `ExtendForAbortedExposures` pulled them all back to the same aborted-exposure timestamp and the overlapping intervals double-counted. The numerator now subtracts integration intervals built from the saved images list, and overlapping roof-closed intervals are merged before subtraction.
- Reports now render correctly for users whose Windows region uses a comma decimal separator (most of Europe). Previously, sky-thumbnail images could silently fail to load and the target framing overlay could render incorrectly on those systems, because numeric values in image URLs and SVG coordinates were formatted with the local decimal separator instead of a dot. All report output is now formatted locale-independently.
- Session and image timestamps are now read back from the database with their time-zone information preserved, fixing altitude charts and session-date grouping that could shift by the local UTC offset for users in time zones east of GMT.


## Unreleased — v3.1.0 (in progress)

**New features**
- Raw image thumbnails (opt-in) — Night Summary can now save a small JPEG of every LIGHT frame as it's captured and browse them in a new dashboard gallery, accessed via the Frames pill in the session report toolbar. Click any thumb for a lightbox with capture/ADU/guiding/environment metrics; project name, Exposure Profile, and per-axis guiding RMS are pulled from Target Scheduler when available. Arrow-key (desktop) or swipe (mobile) navigation. Optional medium 800px thumbnails for sharper lightbox viewing. Three retention modes (keep all, roll over by days, roll over by disk GB). Existing TS users can backfill TS captured thumbnails from past sessions via one-click "Import from Target Scheduler" in Options. Off by default — enable in Options → Raw Image Thumbnails.

- Donation link — new "Donate" pill in the dashboard header and a Support line in the plugin description point at https://ko-fi.com/sleepypuppy15. GitHub Sponsors also enabled. Plugin stays free and open source; donations always optional.

**Improvements**
- Dashboard header title is clickable and returns to the session list.
- Smoother dashboard navigation with additional polish and multiple fixes

**Bug fixes**
- Sessions page activity waveform fixes: re-renders on browser resize, click-to-open works at any desktop window width (not just ≥720 px), hover tooltip dismisses on click and hash change.
- Activity waveform and calendar heatmap on the Sessions page opened the static report in a new tab when clicked; now open in the in-app session view, matching the session-card click behavior.
- Reports opened on mobile (Discord/email) were reflowing to phone width instead of scaling the desktop layout. Fixed viewport meta restores scale-to-fit.
- Per-image timestamps now record exposure-start time rather than save time to match FITS `DATE-OBS` headers, filenames, and Target Scheduler's convention.


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
