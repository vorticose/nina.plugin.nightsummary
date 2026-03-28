# v2.9.0 Draft Changelog

**New features**
- Expandable filter breakdown in the session overview stat boxes — click Total Images or Total Exposure to see a per-filter breakdown
- Support for up to 4 additional metric charts per report — configure extra charts from the Options page, each with independent primary and secondary metric selection
- Improved metric chart axis scaling — axis labels now use sensible round-number steps (e.g. 5° for altitude, 0.5px for HFR) instead of arbitrary intervals
- Added 8 new metric chart options: Sky Quality, Cloud Cover, Camera Temperature, Dew Point, Wind Speed, Pressure, Star Count, and Azimuth

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
