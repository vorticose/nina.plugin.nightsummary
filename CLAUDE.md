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
- **CDS HiPS2FITS API returns FITS by default, not JPEG**: the URL must include
  `&format=jpg` to get a browser-renderable image. Without it, the API returns
  a binary FITS file which passes the `> 500 bytes` check but browsers cannot
  render it as an image -- thumbnails silently disappear. Fixed after v2.8.1.

## Branching Strategy

```
main       ← always matches the latest published release, tagged (v2.8.0, v2.8.1 etc.)
dev        ← integration branch, accumulates features between releases
feature/*  ← short-lived branches for individual features/fixes
nina-3.3   ← long-running NINA 3.3 port, periodically synced from dev
```

**Day-to-day workflow:**
1. Cut a feature branch from `dev`: `git checkout dev && git checkout -b feature/my-feature`
   - Each GitHub issue gets its own feature branch (e.g. `feature/filter-breakdown` for issue #1)
2. Do the work, commit freely on the feature branch
3. Merge back to `dev` when done: `git checkout dev && git merge feature/my-feature`
4. Delete the feature branch: `git branch -d feature/my-feature`
5. For trivial one-line fixes, committing directly to `dev` is fine

**Releasing:**
1. Run `dotnet test NINA.Plugin.NightSummary.Tests` on Windows — must be 0 failures
2. Merge `dev` → `main`: `git checkout main && git merge dev`
3. Tag the release: `git tag v2.8.1`
4. Follow the full release process below

**Keeping nina-3.3 in sync:**
- Periodically (every few sessions or before a release): `git checkout nina-3.3 && git merge dev`
- No conflicts expected — only difference is 3 lines in the .csproj

**Note:** `main` should always reflect exactly what's published. Never commit unreleased
work directly to `main`.

## Workflow Notes

- Always push from Mac using `gh auth` credentials (token needs `repo` scope)
- The remote URL must temporarily embed the token for push:
  `git remote set-url origin "https://$(gh auth token)@github.com/..."` then restore
- **CAUTION**: after restoring the clean URL, verify `.git/config` has a non-empty URL.
  If the restore command fails (e.g., gh can't detect the remote), the URL will be blank.
  Repo URL: `https://github.com/vorticose/nina.plugin.nightsummary.git`
- **Quick deploy from Mac**: mount `//RBFocus:@100.86.208.29/Night%20Summary`, copy DLL, unmount:
  ```
  mkdir -p /tmp/nina-deploy && mount_smbfs "//RBFocus:@100.86.208.29/Night%20Summary" /tmp/nina-deploy
  cp NINA.Plugin.NightSummary/bin/Release/net8.0-windows/NINA.Plugin.NightSummary.dll /tmp/nina-deploy/
  diskutil unmount /tmp/nina-deploy
  ```
- GitHub raw CDN caches aggressively -- use the Contents API for reliable downloads:
  `Invoke-RestMethod "https://api.github.com/repos/.../contents/..."`
- PowerShell scripts must be pure ASCII -- no em dashes, box-drawing chars, or
  smart quotes, even in comments

## Release Process

To publish a new version:

1. **Run tests** (on Windows machine): `dotnet test NINA.Plugin.NightSummary.Tests` — must be 0 failures before release
2. **Clean up dev markers** in `AssemblyInfo.cs`:
   - Remove `*** DEV BUILD ***` from `AssemblyDescription`
   - Remove `[assembly: AssemblyInformationalVersion("X.Y.Z-dev")]` line
3. **Build**: `dotnet build NINA.Plugin.NightSummary.sln -c Release`
2. **Package**: `cd NINA.Plugin.NightSummary/bin/Release/net8.0-windows && zip -r /tmp/NINA.Plugin.NightSummary.zip . --exclude "*.pdb" --exclude "*.xml"`
3. **Checksum**: `shasum -a 256 /tmp/NINA.Plugin.NightSummary.zip | awk '{print toupper($1)}'`
4. **GitHub Release**: Update existing or create new release tagged `vX.Y.Z`, upload ZIP
5. **Update our repo**: Update `manifest.json` and `repository.json` with new version, URL, checksum
6. **Update manifest fork**: In `~/nina.plugin.manifests`, sync with upstream, update `manifests/n/Night Summary/3.0.0/manifest.json`
7. **Validate**: `cd ~/nina.plugin.manifests && npm install && node gather.js` — must show 0 failed
8. **Submit PR**: to `isbeorn/nina.plugin.manifests` from `vorticose:main`

### Manifest fields to keep correct
- `Author`: must be `"Evan Pegors @sleepypuppy15"` (easy to lose the @sleepypuppy15)
- `MinimumApplicationVersion`: `3.2.0.9001` (not 3.0.0.2017)
- Fork path: `~/nina.plugin.manifests` → `manifests/n/Night Summary/3.0.0/manifest.json`
- Always sync fork with upstream before editing — the fork can fall behind and cause merge conflicts

### PR template for isbeorn/nina.plugin.manifests
```
## Summary
* Update Night Summary to vX.Y.Z
* [one line describing what's new]

## Changes
* Version: X.Y.Z-1 → X.Y.Z
* Updated download URL and SHA256 checksum
* No changes to MinimumApplicationVersion or other manifest fields

## Validation
* validate-latest-manifest.js passes (schema valid, checksum verified)
```

## NINA 3.3 Branch (`nina-3.3`)

A separate branch exists for NINA 3.3 compatibility. NINA 3.3 is still in nightly builds
and a stable release is expected several months away (as of March 2026).

### What's different on `nina-3.3`
- `TargetFramework`: `net10.0-windows` (was `net8.0`)
- `NINA.Plugin`: `3.3.0.1017-nightly` (was `3.2.0.9001`)
- `Microsoft.Web.WebView2`: `1.0.3650.58` (was `1.0.3296.44`, required by NINA 3.3)
- No API changes were needed — the port compiled clean with 0 errors

### Keeping branches in sync
Periodically merge `main` into `nina-3.3` to keep them in sync (no need to do this
after every commit — every few sessions or before a release is fine):
```bash
git checkout nina-3.3
git merge main
git push origin nina-3.3
git checkout main
```
Merges will always be clean since the only difference is 3 lines in the `.csproj`.
**Important**: always merge `main` → `nina-3.3`, NEVER `nina-3.3` → `main`. Merging
the wrong direction will pull the net10.0 csproj changes into main and break the 3.2 build.

### When NINA 3.3 goes stable
1. Test the `nina-3.3` DLL against a stable NINA 3.3 install
2. Bump version in `manifest.json` + `repository.json`
3. Set `MinimumApplicationVersion` to the stable 3.3 build number
4. Create GitHub Release from the `nina-3.3` branch
5. Add `manifests/n/Night Summary/3.3.0/manifest.json` to the manifests fork
6. Submit PR to `isbeorn/nina.plugin.manifests` — both 3.2 and 3.3 manifests coexist,
   NINA's plugin manager shows each version only to users on the matching NINA version

### Pressure units note
NINA 3.3 changed atmospheric pressure from MSL (sea level) to QFE (local,
elevation-adjusted). No code change needed but worth mentioning in the 3.3 release notes.

## UI Standards (Options.xaml)

- **Inline utility buttons** (Browse, + Add Chart, ✕ Remove, etc.): use `MinWidth` to ensure horizontal breathing room — NINA's ControlTemplate ignores `Padding`. No fixed Width.
- **Primary action buttons** (Preview Report, Send Report, Send Test *): use `Width="180"` — no explicit padding
- Apply these standards to any new buttons added in future

## Testing

- Unit/integration tests: `dotnet test NINA.Plugin.NightSummary.Tests` (run on Windows machine)
  - 73 tests covering ChartGenerator, SessionDatabase, ReportGenerator, FilterHelper
  - Tests compile on Mac but must run on Windows (net8.0-windows target)
  - **When adding new features, add corresponding tests to the test project**
    - New metrics → add to ChartGeneratorTests Theory data
    - New DB columns → add round-trip test to SessionDatabaseTests
    - New report sections → add content check to ReportGeneratorTests
    - New filter/calc logic → add to FilterHelperTests or a new test class
  - **Before writing any HTML content assertion in a test, grep the production code first**
    to confirm the exact string, CSS class, or attribute exists in the output. Never assume
    a class or element name — verify it with Grep before asserting on it.
- Migration tests: `scripts/test-migration.ps1` (run on Windows machine)
- See `scripts/TEST-MIGRATION-NOTES.md` for prerequisites and known gotchas
- All 19 migration scenarios pass as of v2.8.1
- After running the test suite, `NightSummary` is left as a directory junction --
  this is normal and NINA works correctly through it
