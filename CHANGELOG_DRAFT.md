# v2.10.0 Draft Changelog

**New features**
- NINA-style filename patterns for saved reports — use variables like `$DATEMINUS12$`, `$DATE$`, `$CAMERAID$`, `$TELESCOPEID$`, `$SEQUENCETITLE$` with clickable insertion buttons and live preview (#11)
- Sky position angle displayed in target headers and FOV overlay — uses plate solve PA, falls back gracefully when unavailable (#18)
- Plugin version, NINA version, and author credit shown in report footer (#14)
- Collapsible equipment profile section in report header — shows all connected equipment with user-overridable display names (#15)

**Improvements**
- Settings now persist to a stable JSON file that survives NINA updates
- Per-filter exposure breakdown in overview uses human-readable duration format instead of decimal hours (#13)
- Session history no longer artificially capped — all previous sessions for a target are shown (#16)
- CI workflow publishes dev build artifacts for beta testers

**Bug fixes**
- Fixed filter classification progressive reset when loading or refreshing options (#12)

**In progress (not yet merged)**
- Live Stack plugin integration — embed stacked images in reports (#6)

# v2.9.0 Draft Changelog

**New features**
- Expandable filter breakdown in the session overview stat boxes — click Total Images or Total Exposure to see a per-filter breakdown
- Support for up to 4 additional metric charts per report — configure extra charts from the Options page, each with independent primary and secondary metric selection
- Improved metric chart axis scaling — axis labels now use sensible round-number steps (e.g. 5° for altitude, 0.5px for HFR) instead of arbitrary intervals

**Improvements**
- Target Scheduler settings grouped into a dedicated subsection in Options for clarity
- Tonight's Preview toggle is now greyed out when the Target Scheduler API is not enabled, preventing misconfiguration
- Target Scheduler progress bars are suppressed in the report when Target Scheduler is not installed, eliminating spurious warnings for users without TS
- Discord webhook URL validation no longer rejects legacy discordapp.com webhook URLs — the actual API response is used to determine success or failure instead

# v2.8.0 Draft Changelog

**New features**
- Minimum altitude line on altitude chart — when Target Scheduler is installed, the per-target altitude chart shows a dotted red line at the project's minimum altitude setting, with a new toggle in Options
- Added 4 new metric chart options: Altitude, Airmass, Humidity, and Focuser Position
- Added option to expand all report sections by default instead of collapsed
- Report Preview window — preview your report with real session data or test data directly from the Options page using a built-in viewer

**Improvements**
- Hover tooltips on metric chart data points show timestamp and value
- Target Scheduler features now silently skip when TS is not installed instead of showing toast warnings
- Tonight's Preview section moved from Standard to Full detail level
