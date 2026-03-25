# CLAUDE.md -- Project Memory for Night Summary NINA Plugin

This file is continuously updated as new lessons, decisions, and context are learned.
It should be merged with any CLAUDE.md from other development machines.

---

## Project Overview

Night Summary is a plugin for NINA (Nighttime Imaging 'N' Astronomy), a Windows
astrophotography sequencing application. The plugin records imaging sessions and
generates HTML reports summarizing each night's work.

- **Language**: C# / .NET 8, targeting net8.0-windows
- **UI**: WPF with WebView2 for HTML report rendering
- **Database**: SQLite via System.Data.SQLite
- **NINA version**: 3.0.0
- **Plugin location on target machine**: `%LOCALAPPDATA%\NINA\Plugins\3.0.0\Night Summary\`
- **Database location**: `%LOCALAPPDATA%\NINA\NightSummary\nightsummary.sqlite`

## Development Setup

- Code is edited on Mac using VS Code
- Built with `dotnet build NINA.Plugin.NightSummary.sln -c Release`
- Deployed to a remote Windows machine running NINA
- The built DLL is copied to the NINA plugins folder on the Windows machine
- Git is used for source control; GitHub CLI (`gh`) is authenticated for push

## Architecture Notes

- `SessionDatabase.cs` handles all SQLite access and the legacy migration logic
- `SessionService` and `NightSummaryPlugin` each create their own `SessionDatabase`
  instance -- this is intentional (two log lines on startup is expected behavior)
- WebView2 is used for the preview window -- `NavigateToString` has a ~2MB limit;
  use a temp file + `Navigate()` for large reports
- SVG `fill` attributes do not resolve CSS variables like `var(--text)` -- use
  explicit color variables instead

## Migration System (v2.8.1)

Migration runs once when the new DB path does not exist. It:
1. Scans all version folders under `%LOCALAPPDATA%\NINA\Plugins\` for legacy DBs
2. Selects the most recently modified valid (non-corrupt) DB as primary
3. Copies it atomically via a temp file
4. Merges sessions from other legacy DBs (deduplication by SessionId)
5. Uses a `.merge_state` file to enable resume if interrupted
6. Keeps a `.pre_merge_backup` as a safety net for one version cycle

See `scripts/TEST-MIGRATION-NOTES.md` for hard-won lessons from testing this.

## Known Issues / Decisions

- The `var(--text)` SVG fill bug: light mode SVG labels may render incorrectly.
  Fixed in v2.8.1 by using explicit color variables in SVG fill attributes.
- Two `SessionDatabase` constructor log lines on startup is normal -- not a bug.

## Workflow Notes

- Always push from Mac using `gh auth` credentials (token needs `repo` scope)
- The remote URL must temporarily embed the token for push:
  `git remote set-url origin "https://$(gh auth token)@github.com/..."` then restore
- GitHub raw CDN caches aggressively -- use the Contents API for reliable downloads:
  `Invoke-RestMethod "https://api.github.com/repos/.../contents/..."`
- PowerShell scripts must be pure ASCII -- no em dashes, box-drawing chars, or
  smart quotes, even in comments

## Testing

- Migration tests: `scripts/test-migration.ps1` (run on Windows machine)
- See `scripts/TEST-MIGRATION-NOTES.md` for prerequisites and known gotchas
- All 19 migration scenarios pass as of v2.8.1
- After running the test suite, `NightSummary` is left as a directory junction --
  this is normal and NINA works correctly through it
