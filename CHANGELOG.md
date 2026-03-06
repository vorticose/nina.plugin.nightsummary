# Night Summary — Changelog


## v2.0.0 — Notifications & Data Quality

**New notification channels**
- **Pushover** — receive an instant push notification on your phone or tablet when your session ends, including a per-target summary of images captured
- **Discord** — post a full session summary embed to your Discord server via webhook, with the complete HTML report attached as a file

**Report improvements**
- FWHM and eccentricity metrics now included in the report when the Hocus Focus plugin is installed
- HFR over time chart added — visualize how focus quality trended across your imaging session (renders in the attached HTML report)
- Per-target sections now have clear visual separators for easier reading
- HTML report is now sent as an attachment across all channels, so charts and formatting render correctly when opened in a browser

**Settings improvements**
- Global "Send Reports on Session End" toggle moved to the top of the settings panel — one switch to enable or disable all outgoing reports
- Test buttons for each notification channel — send a ping or a full test report without running a sequence
- Full test report reads from a separate test database you place in the Night Summary plugin data folder, keeping test data isolated from real sessions


## v1.0.0 — Initial Release

- Records all images captured during your NINA sequence — target name, filter, exposure duration, HFR, and star count logged automatically
- Sends a dark-themed HTML email report to your inbox when the sequence completes
- Report includes per-target and per-filter breakdowns with total exposure times and image counts
- Configure your Gmail credentials and recipient address in NINA's options panel
- Two sequencer instructions available: **Start Session** (begin recording) and **End Session** (finalize and send report)
