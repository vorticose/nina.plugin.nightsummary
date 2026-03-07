# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

**Night Summary** is a N.I.N.A. (Nighttime Imaging 'N' Astronomy) plugin that records astrophotography session data during sequence execution and sends an HTML summary report (email, Discord, Pushover, or local save) when the session ends.

- Target: `net8.0-windows`, x64, WPF, .NET 8
- N.I.N.A. plugin package: `NINA.Plugin` v3.2.0.9001 (requires NINA ≥ 3.0.0.2017)
- Plugin GUID (must never change): `682531D1-5A23-4627-B961-0794282ECB4E`
- Assembly/namespace: `NINA.Plugin.NightSummary`

## Build & Deploy

```powershell
# Build (Release)
dotnet build "NINA.Plugin.Template/NINA.Plugin.NightSummary.csproj" -c Release

# Full deploy: build → zip → update manifest.json + repository.json → copy DLL to local NINA
.\scripts\deploy.ps1
```

The deploy script reads the version from the compiled DLL, creates `scripts/NINA.Plugin.NightSummary.zip`, computes the SHA256, and updates both `manifest.json` and `repository.json`. After running it, manually create a GitHub Release tagged `v{version}`, upload the zip, then commit the updated JSON files.

**Version** is set in `NINA.Plugin.Template/Properties/AssemblyInfo.cs` via `[AssemblyVersion]` and `[AssemblyFileVersion]`. There is no separate IDE or test runner — build is via `dotnet build`.

## Architecture

The plugin uses MEF (Managed Extensibility Framework) for all exports. N.I.N.A. discovers and injects dependencies through constructor injection on exported types.

### Data Flow

```
NightSummaryInstruction (Start)
  └── SessionService.StartSession()
        ├── SessionCollector  — subscribes to IImageSaveMediator.ImageSaved
        └── SessionEventCollector — subscribes to focuser, safety monitor, telescope mediators

[Session runs, images captured & events logged to SQLite]

NightSummaryEndInstruction (End)
  └── SessionService.EndSession()
        ├── Collects session + images + events from SessionDatabase
        ├── Fetches TS progress from TargetSchedulerDatabase (optional)
        ├── Computes camera FOV from NINA profile
        └── Fires report tasks in parallel:
              ReportGenerator → HTML → EmailSender / DiscordSender / PushoverSender / local file
```

### Key Files

| File | Purpose |
|------|---------|
| `NightSummaryPlugin.cs` | MEF `[Export(IPluginManifest)]`; holds settings properties bound to `Options.xaml`; exposes test commands |
| `MyPluginSequenceItems/NightSummaryInstruction.cs` | "Night Summary Start" sequencer item — calls `SessionService.StartSession()` |
| `MyPluginSequenceItems/NightSummaryEndInstruction.cs` | "Night Summary End" sequencer item — waits 15 s for saves to complete, then calls `SessionService.EndSession()` |
| `Session/SessionService.cs` | Central coordinator; MEF-exported as `Shared`; drives start/end and dispatches all report sends |
| `Session/SessionCollector.cs` | Hooks `IImageSaveMediator.ImageSaved`; records `ImageRecord` per capture; reads FWHM/Eccentricity via reflection (optional Hocus Focus plugin) |
| `Session/SessionEventCollector.cs` | Implements `IFocuserConsumer`, `ISafetyMonitorConsumer`; logs AutoFocus, RoofOpen/Closed, MeridianFlip events |
| `Data/SessionDatabase.cs` | SQLite wrapper; tables: `Sessions`, `Images`, `SessionEvents`; handles schema migrations with `ALTER TABLE` catch |
| `Data/TargetSchedulerDatabase.cs` | Read-only access to Target Scheduler plugin's `schedulerdb.sqlite` at `%LOCALAPPDATA%\NINA\SchedulerPlugin\`; returns per-filter desired/acquired/accepted counts and RA/Dec/rotation |
| `Reporting/ReportGenerator.cs` | Builds the full HTML report from `ReportData`; includes inline CSS, filter sort order (`L R G B H S O`), CV statistics, DSS sky thumbnail overlay with FOV box |
| `Reporting/ChartGenerator.cs` | Generates inline SVG HFR-over-time line chart |
| `Reporting/EventTimelineGenerator.cs` | Generates inline SVG session timeline with target bands and event markers; includes JS hover tooltips |
| `Reporting/EmailSender.cs` | Gmail SMTP with app password |
| `Reporting/DiscordSender.cs` | Discord webhook; embeds text summary, uploads HTML as file attachment |
| `Reporting/PushoverSender.cs` | Pushover push notification |
| `MyPluginProperties/Settings.Designer.cs` | Auto-generated from `Settings.settings`; stores all user-configurable plugin settings |
| `Options.xaml` / `Options.xaml.cs` | Plugin options page; key is `Night Summary_Options` (must match `IPluginManifest.Name`) |
| `MyPluginSequenceItems/MyPluginTemplates.xaml` | DataTemplates for both sequencer items; exported as `ResourceDictionary` via MEF |

### Database Locations

- **Plugin DB**: `%LOCALAPPDATA%\NINA\Plugins\{NINAVersion}\NightSummary\nightsummary.sqlite`
- **Test DB**: `%LOCALAPPDATA%\NINA\Plugins\{NINAVersion}\NightSummary\test\nightsummary.sqlite`
- **Target Scheduler DB** (read-only, optional): `%LOCALAPPDATA%\NINA\SchedulerPlugin\schedulerdb.sqlite`
- **Saved reports**: `%USERPROFILE%\Documents\N.I.N.A.\Night Summary\Saved Reports\`

### Important Constraints

- **Namespace and type names of exported classes must never change** — they are serialized into NINA sequence JSON files. Changing them breaks existing user sequences.
- FWHM and Eccentricity are read from `IStarDetectionAnalysis` via reflection because they are only present when the Hocus Focus plugin is installed. This is intentional.
- `SessionDatabase` performs additive migrations on startup using `ALTER TABLE … ADD COLUMN` wrapped in try/catch to handle columns that already exist.
- The `NightSummaryEndInstruction` deliberately waits 15 seconds before ending the session to allow NINA to finish saving pending images to disk.

## Utility Scripts

All scripts are in `scripts/` and are PowerShell:

| Script | Purpose |
|--------|---------|
| `deploy.ps1` | Full build-zip-deploy workflow (see above) |
| `seed-test-db.ps1` | Populates the test SQLite DB with synthetic session data |
| `inspect-ns-db.ps1` | Dumps contents of the Night Summary SQLite DB |
| `inspect-ts-db.ps1` | Dumps contents of the Target Scheduler DB |
| `inspect-profile.ps1` | Reads NINA profile XML for camera/telescope settings |
| `list-nupkg.ps1` | Lists contents of a NuGet package for reference |
