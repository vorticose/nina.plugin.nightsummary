using System.Reflection;
using System.Runtime.InteropServices;

[assembly: Guid("682531D1-5A23-4627-B961-0794282ECB4E")]
[assembly: AssemblyVersion("2.5.0.0")]
[assembly: AssemblyFileVersion("2.5.0.0")]
[assembly: AssemblyTitle("Night Summary")]
[assembly: AssemblyDescription("Records your imaging session and delivers a detailed HTML report via email, Discord, or Pushover when your sequence ends.")]
[assembly: AssemblyCompany("Evan Pegors")]
[assembly: AssemblyProduct("Night Summary")]
[assembly: AssemblyCopyright("Copyright © 2026 Evan Pegors")]
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.0.0.2017")]
[assembly: AssemblyMetadata("License", "MPL-2.0")]
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]
[assembly: AssemblyMetadata("Repository", "")]
[assembly: AssemblyMetadata("Homepage", "")]
[assembly: AssemblyMetadata("Tags", "report,summary,email,logging")]
[assembly: AssemblyMetadata("ChangelogURL", "")]
[assembly: AssemblyMetadata("FeaturedImageURL", "https://i.imgur.com/uvcC1dC.png")]
[assembly: AssemblyMetadata("ScreenshotURL", "")]
[assembly: AssemblyMetadata("AltScreenshotURL", "")]
[assembly: AssemblyMetadata("LongDescription", @"Night Summary automatically records your astrophotography session as it runs and delivers a rich HTML report the moment your sequence completes — so you wake up to a full breakdown of the night.

**What's in the report**
- Session overview with at-a-glance stats: total images, total exposure time, target count, average HFR, average guiding RMS, and imaging yield
- Session event timeline showing AutoFocus runs, meridian flips, and safety monitor events
- Per-target imaging summary with filter breakdown, exposure counts, and total integration time — including a DSS sky survey thumbnail with FOV overlay and an altitude chart
- Star count consistency — CV (Coefficient of Variation) measures how stable your star counts were across exposures. A low CV means consistent transparency and focus; a high CV suggests passing clouds, dew, or focus drift. Reported separately for broadband and narrowband filters.
- Target Scheduler integration — shows desired, acquired, and accepted frame counts per filter with a visual progress bar
- Session history table summarizing all past sessions for each target
- Cumulative integration time per target across all previous sessions
- Image quality stats: HFR, FWHM, Eccentricity, and guiding RMS — with expandable per-filter breakdowns
- HFR trend chart over the session

**Optional plugin integrations**
- **Target Scheduler** — when installed, Night Summary reads your imaging targets and frame counts directly from the Target Scheduler database, adding per-filter progress bars and cumulative integration tracking to the report. Without it, targets and coordinates are still captured from NINA's sequence data.
- **Hocus Focus** — when installed, Night Summary reads FWHM and Eccentricity measurements from each saved image. Without it, only HFR (provided natively by NINA) is included in image quality stats.

**Delivery options**
- Email via Gmail SMTP (HTML report attached)
- Discord webhook (embed summary + HTML file attachment)
- Pushover push notification (summary text)
- Save report locally to Documents\N.I.N.A.\Night Summary\Saved Reports\

All channels can be enabled independently and tested directly from the plugin options page. Previous session reports can also be resent at any time without re-running a sequence.

**How to use**

Add the ""Night Summary Start"" instruction near the beginning of your sequence and ""Night Summary End"" at the end. That's it — Night Summary handles the rest automatically.

**Troubleshooting**

- *No report received:* Make sure both the Night Summary Start and Night Summary End instructions are present in your sequence and that at least one delivery channel is enabled in the plugin options.
- *Email not sending:* Gmail requires an App Password rather than your regular account password. Generate one at myaccount.google.com under Security > 2-Step Verification > App Passwords.
- *FOV overlay or survey image looks wrong:* The camera field of view box is calculated from your sensor dimensions and focal length as configured in your NINA equipment profile. If the box or image appears too large, too small, or misaligned, verify that your sensor pixel size, width, and height are correctly set in your NINA camera profile.
- *FWHM and Eccentricity not appearing:* These metrics require the Hocus Focus plugin to be installed and active during the imaging session.
- *Target Scheduler data not appearing:* Night Summary reads the Target Scheduler database automatically — no additional setup is needed beyond having that plugin installed.")]
[assembly: ComVisible(false)]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]