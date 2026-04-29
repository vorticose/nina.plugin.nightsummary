using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: InternalsVisibleTo("NINA.Plugin.NightSummary.Tests")]

[assembly: Guid("682531D1-5A23-4627-B961-0794282ECB4E")]
// AssemblyVersion and AssemblyFileVersion are auto-generated at build time
// from VersionPrefix in the .csproj + git commit count (see SetGitBuildNumber target)
[assembly: AssemblyInformationalVersion("3.0.0")]
[assembly: AssemblyTitle("Night Summary")]
[assembly: AssemblyDescription("Records your imaging session and delivers a detailed HTML report via email, Discord, or Pushover when your sequence ends. Includes a built-in local web dashboard for browsing session history and lifetime statistics from any device on your network.\n\nTo update: download the latest release from the link above and extract the zip to your existing NightSummary plugin folder, overwriting existing files.")]
[assembly: AssemblyCompany("Evan Pegors")]
[assembly: AssemblyProduct("Night Summary")]
[assembly: AssemblyCopyright("Copyright � 2026 Evan Pegors")]
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.2.0.9001")]
[assembly: AssemblyMetadata("License", "MPL-2.0")]
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]
[assembly: AssemblyMetadata("Repository", "https://github.com/vorticose/nina.plugin.nightsummary")]
[assembly: AssemblyMetadata("Homepage", "https://github.com/vorticose/nina.plugin.nightsummary")]
[assembly: AssemblyMetadata("Tags", "report,summary,email,logging")]
[assembly: AssemblyMetadata("ChangelogURL", "https://raw.githubusercontent.com/vorticose/nina.plugin.nightsummary/main/CHANGELOG.md")]
[assembly: AssemblyMetadata("FeaturedImageURL", "https://i.imgur.com/uvcC1dC.png")]
[assembly: AssemblyMetadata("ScreenshotURL", "")]
[assembly: AssemblyMetadata("AltScreenshotURL", "")]
[assembly: AssemblyMetadata("LongDescription", @"[Full documentation and setup guide](https://vorticose.github.io/nina.plugin.nightsummary/)

Night Summary automatically records your astrophotography session as it runs and delivers a rich HTML report the moment your sequence completes — so you wake up to a full breakdown of the night.

**How to use**

Add the ""Night Summary Start"" instruction near the beginning of your sequence and ""Night Summary End"" at the end. That's it — Night Summary handles the rest automatically.

**What's in the report**
- Session overview with at-a-glance stats: total images, total exposure time, target count, average HFR, average FWHM, average guiding RMS, and imaging yield
- Equipment profile showing your connected gear (camera, telescope, mount, filter wheel, focuser, rotator, guider, dome, flat panel, safety monitor, weather station, and switch) with customizable display names and per-field visibility
- Session event timeline showing AutoFocus runs, meridian flips, and safety monitor events
- Yield and Imaging Overhead Analysis — a per-category timing breakdown showing exactly where your night went (camera download, filter changes, dithering, autofocus, plate solves, centering, slew, and more) with a stacked bar chart and detailed table
- Per-target imaging summaries with filter breakdown, exposure counts, total integration time, sky position angle, a DSS sky survey thumbnail with FOV overlay, and an altitude chart with optional minimum altitude line from Target Scheduler
- Live Stack thumbnails — when the Live Stack plugin is running, captures and displays the latest stacked image per target and filter
- Per-target image quality stats: HFR, FWHM and Eccentricity (with Hocus Focus plugin), and guiding RMS with per-filter breakdowns
- Star count consistency — CV (Coefficient of Variation) measures how stable your star counts were across exposures. A low CV means consistent transparency and focus; a high CV suggests passing clouds, dew, or focus drift. Reported separately for broadband and narrowband filters.
- Target Scheduler integration — shows desired, acquired, and accepted frame counts per filter with a visual progress bar
- Session history table summarizing all past sessions for each target, including total integration and image quality stats
- Configurable Metric Charts — add multiple charts, each showing any two metrics with a customizable x-axis. Choose from HFR, FWHM, Eccentricity, Guiding RMS, Focuser Temperature, Ambient Temperature, Altitude, Airmass, Humidity, Focuser Position and more
- Tonight's Preview — a visual timeline of what Target Scheduler plans to image tonight, with per-target filter breakdowns

**Live Dashboard**
Night Summary v3 includes a built-in local web server for browsing all your session history and lifetime statistics from any browser on your network — desktop, laptop, tablet, or phone. The Sessions tab shows session cards with thumbnails, stat boxes, and altitude charts; click any card to open the full embedded report. The **Targets / Projects tab** shows lifetime totals and per-session history for everything you've imaged (the tab label reflects whether Target Scheduler is installed). Enable in Options → Night Summary Settings → Local Dashboard. The settings panel displays all available URLs for reaching the dashboard. Use Generate All Reports on first run to build reports for existing sessions.

**Report detail levels**
Three levels let you control how much is included: Snapshot (header and filter table only), Standard (adds timeline, altitude charts, and image quality), and Full (adds overhead analysis, metric charts, session history, and tonight's preview). Each section can also be toggled individually, and all sections can be expanded by default instead of collapsed.

**Optional plugin integrations**
- **Target Scheduler** — when installed, Night Summary reads your imaging targets and frame counts directly from the Target Scheduler database, adding per-filter progress bars and cumulative integration tracking to the report. With the Target Scheduler API enabled (Target Management → select your active profile → gear icon → API Preferences → enable API), the report also includes a Tonight's Preview showing the planned schedule for tonight. Without Target Scheduler, targets and coordinates are still captured from NINA's sequence data.
- **Hocus Focus** — when installed, Night Summary reads FWHM and Eccentricity measurements from each saved image. Without it, only HFR (provided natively by NINA) is included in image quality stats.
- **Live Stack** — when installed and running, Night Summary captures the latest stacked image for each target and filter and embeds it in the report. Supports broadband, narrowband, and color composite stacks.

**Delivery options**
- Email via SMTP — Gmail is the default and easiest to set up, but any SMTP provider is supported (Outlook, Yahoo, iCloud, and others)
- Discord webhook (embed summary + HTML file attachment)
- Pushover push notification (short text summary)
- Save report locally — supports NINA filename pattern variables in the save path for automatic organization by date, target, etc.

All channels can be enabled independently and tested directly from the plugin options page. Previous session reports can also be resent at any time without re-running a sequence. NINA shows toast notifications when reports are generated and delivered, including warnings if any section couldn't be included.

Settings are saved to a stable JSON file that persists across plugin updates. A built-in Report Preview lets you view reports with real session data or test data directly from the plugin options page.

**Troubleshooting**

- *No report received:* Make sure both the Night Summary Start and Night Summary End instructions are present in your sequence and that at least one delivery channel is enabled in the plugin options. Also check your email spam folder.
- *Email not sending:* Most providers require an App Password rather than your regular account password. For Gmail, generate one at myaccount.google.com under Security > App Passwords. For other providers, check your account's security settings for app-specific password or SMTP access options.
- *FOV overlay or survey image looks wrong:* The camera field of view box is calculated from your sensor dimensions and focal length as configured in your NINA equipment profile. If the box or image appears too large, too small, or misaligned, verify that your sensor pixel size, width, and height are correctly set in your NINA camera profile.
- *FWHM and Eccentricity not appearing:* These metrics require the Hocus Focus plugin to be installed and active during the imaging session.
- *Target Scheduler data not appearing:* Night Summary reads the Target Scheduler database automatically — no additional setup is needed beyond having that plugin installed.
- *Dashboard not reachable from another device:* Make sure the server is running — the URL appears in Settings → Local Dashboard when active. If the machine name doesn't resolve, try the local IP address shown instead. For access outside your home network, a VPN is required.")]
[assembly: ComVisible(false)]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
