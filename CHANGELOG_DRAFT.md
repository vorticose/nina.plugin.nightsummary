# v2.10.0 Draft Changelog

**New features**
- Live Stack integration — captures live-stacked thumbnails from the Live Stack plugin and displays them in the report per target/filter
- Imaging overhead breakdown section — parses NINA logs at session end to show a per-category timing breakdown (camera download, filter changes, dithering, autofocus, plate solves, star detection, image saves, centering, temp comp focus) with a stacked bar chart and detailed table. Includes a yield cross-validation metric that compares parsed overhead against the existing yield calculation to measure coverage completeness.
- NINA filename pattern variables in report save path — use the same path variables as NINA's file save patterns, with clickable insertion buttons
- Customizable x-axis on metric charts — choose Time, Frame Index, or any metric (Altitude, Temperature, etc.) as the x-axis
- Sky position angle displayed in target headers and FOV overlay on sky thumbnails

**Improvements**
- Session history now returns all previous sessions instead of a capped limit
- Plugin version, NINA version, and author credit shown in report footer
- Settings now persist to a stable JSON file that survives NINA plugin updates
- Per-filter exposure breakdown in overview now uses FormatDuration for consistent time formatting

**Bug fixes**
- Fixed filter classification progressive reset on settings load/refresh

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
