# v2.10.0-beta1 Draft Changelog

**New features**
- Live Stack integration — captures live-stacked thumbnails from the Live Stack plugin and displays them in the report per target/filter, with broadband/narrowband grouping and composite support
- Yield and Imaging Overhead Analysis — parses NINA logs to show a per-category timing breakdown with stacked bar chart and detailed table. Tracks all major NINA sequence items (camera download, filter changes, dithering, autofocus, plate solves, image saves, centering, slew, guiding, dome operations, flat panel, camera temp, mount operations, and more). Uses interval merging to accurately handle overlapping concurrent events. Coverage validated at ~89% with unaccounted time explained via tooltip.
- Equipment profile section in report header — shows all 12 NINA equipment types (Camera, Telescope, Mount, Filter Wheel, Focuser, Rotator, Guider, Dome, Flat Panel, Safety Monitor, Weather, Switch) with per-field visibility toggles and user-overridable display names
- NINA filename pattern variables in report save path — use the same path variables as NINA's file save patterns, with clickable insertion buttons
- Customizable x-axis on metric charts — choose Time, Frame Index, or any metric (Altitude, Temperature, etc.) as the x-axis, independently configurable per chart
- Sky position angle displayed in target headers and FOV overlay on sky thumbnails
- Tonight's Preview now shown even when session has zero images (weather-interrupted sessions)

**Improvements**
- Session history now returns all previous sessions instead of a capped limit
- Plugin version, NINA version, and author credit shown in report footer
- Settings now persist to a stable JSON file that survives NINA plugin updates
- Per-filter exposure breakdown in overview now uses FormatDuration for consistent time formatting
- Equipment and overhead sections expanded by default for better discoverability
- Overhead stat boxes have info icons with hover tooltips explaining each metric
- Note below overhead table explains that category totals may exceed overall overhead due to concurrent operations
- Active sessions show "In Progress" with duration so far instead of negative numbers
- Preview and resend paths always re-parse NINA logs to pick up parser improvements
- Deploy scripts now save/restore current branch for seamless multi-branch workflows
- Enhanced beta diagnostics logging for log parser and live stack integration

**Bug fixes**
- Fixed filter classification progressive reset on settings load/refresh
- Fixed additional metric charts not inheriting x-axis setting
- Fixed live stack images not loading for historical sessions (report path resolution)

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
