# Night Summary

A [N.I.N.A.](https://nighttime-imaging.eu/) plugin that records your astrophotography session as it runs and delivers a rich HTML report the moment your sequence completes — so you wake up to a full breakdown of the night.

<img src="assets/DemoReportScreenshot.png" width="700" alt="Night Summary Report" />



## Features

**Session data**
- Per-target and per-filter exposure counts, total integration time, and imaging yield
- Image quality metrics: HFR, FWHM, Eccentricity, and guiding RMS — with per-filter breakdowns
- Star count consistency (CV) — measures how stable transparency and focus were across exposures, reported separately for broadband and narrowband
- Session event timeline — AutoFocus runs, meridian flips, and safety monitor events shown on an interactive SVG timeline

**Visuals**
- DSS sky survey thumbnail per target with FOV overlay (uses your sensor and focal length from the NINA profile)
- Altitude curve per target — full rise/set arc with your imaging window highlighted
- Configurable Metric Chart — plot any two metrics over time (HFR, FWHM, Eccentricity, Guiding RMS, Focuser Temp, Ambient Temp)

**History**
- Per-target session history table — date, integration, avg HFR, avg FWHM, avg guiding RMS for previous sessions
- Cumulative integration time per target across all previous sessions

**Target Scheduler integration**
- Per-filter progress bars showing desired, acquired, and accepted frame counts
- Tonight's Preview — a visual timeline of what Target Scheduler plans to image tonight, with per-target filter breakdowns (requires the Target Scheduler API to be enabled)

**Delivery and notifications**
- Email via SMTP — Gmail is the default and easiest to set up, but any SMTP provider is supported
- Discord webhook — embed summary + HTML report as file attachment
- Pushover — instant push notification with a short text summary
- Save locally — HTML report saved to `Documents\N.I.N.A.\Night Summary\Saved Reports\`

All channels can be enabled independently, tested without running a sequence, and previous sessions can be resent at any time. NINA shows toast notifications when reports are generated and delivered, including warnings if any section couldn't be included.


## Optional Integrations

**Target Scheduler** — when installed, Night Summary reads imaging targets and frame counts directly from the Target Scheduler database, adding per-filter progress bars and cumulative integration tracking. With the Target Scheduler API enabled, the report also includes Tonight's Preview showing the planned imaging schedule for tonight. Without Target Scheduler, targets and coordinates are captured from NINA's sequence data.

**Hocus Focus** — when installed, Night Summary reads FWHM and Eccentricity measurements from each saved image. Without it, only HFR (provided natively by NINA) is included.


## Requirements

- N.I.N.A. 3.0 or later


## Installation

1. Download `NINA.Plugin.NightSummary.zip` from the [Releases](../../releases/latest) page.

2. Open File Explorer and navigate to `%LOCALAPPDATA%\NINA\Plugins\3.0.0\`. You can paste this path directly into the address bar. This should be the same folder where all your other NINA plugins are installed.

3. Create a new folder inside called `NightSummary`.

4. Extract the contents of the zip into that `NightSummary` folder. 

5. Start (or restart) NINA. The plugin will appear under **Options → Plugins → Night Summary**.


## Initial Setup

Before your first session, open **Options → Plugins → Night Summary** and configure at least one delivery channel:

- **Email** — enter your Gmail address and an App Password (not your regular account password). If you're using another provider, select *Other provider* and enter your SMTP host, port, and credentials.
- **Discord** — paste your webhook URL.
- **Pushover** — enter your User Key and App Token.
- **Save locally** — enable this to always save the HTML report to `Documents\N.I.N.A.\Night Summary\Saved Reports\`, no account required.

Use the **Send Test Report** button in each section to verify your settings. If using email and you don't see it arrive, check your spam folder.


## Adding the Sequence Instructions

Night Summary uses two sequence instructions that must both be present: **Night Summary Start** and **Night Summary End**.

1. In the NINA sequencer, search for **Night Summary** in the instruction list — both instructions will appear.

2. Place **Night Summary Start** near the top of your sequence, before any imaging begins.

3. Place **Night Summary End** at the very end of your sequence, after all imaging is complete and any park/warm-up instructions. This is what triggers the report — it must execute for the report to be delivered.

If you use **Target Scheduler**, place Night Summary Start before the Target Scheduler Container and Night Summary End after it.

Once your sequence completes, check your configured delivery channel for the report. You can also resend any previous session report at any time from the plugin options page.


## License

[Mozilla Public License 2.0](LICENSE.txt)
