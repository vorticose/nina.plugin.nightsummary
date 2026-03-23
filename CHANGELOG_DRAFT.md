# v2.7.1 Draft Changelog

**New features**
- Aborted exposure tracking — detects exposures that were skipped or aborted during the session (e.g. by RMS triggers, safety monitor events, or manual skip) and displays the count in the session overview, email, Discord, and Pushover summaries
- Save report path override — browse for a custom folder to save local HTML reports instead of the default Documents location
- Minimum altitude line on altitude chart — when Target Scheduler is installed, the per-target altitude chart now shows a dotted red line at the project's minimum altitude setting

**Improvements**
- Updated Target Scheduler API enable instructions with more precise navigation steps

**Bug fixes**
- Fixed HFR units displayed as arcseconds (") instead of pixels (px) in email, Discord, and Pushover text summaries
