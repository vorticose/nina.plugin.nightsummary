---
layout: default
title: File Naming Patterns
nav_order: 8
---

# File Naming Patterns

Night Summary uses NINA-style `$$PATTERN$$` variables for report filenames. These work the same way as NINA's file save patterns, so the syntax should be familiar.

## Setting the Pattern

Go to **Options > Night Summary Settings > Report File Naming**:

1. Type or edit the pattern in the **File name pattern** field
2. The **Pattern preview** field shows what the current pattern resolves to in real time
3. Use the clickable variable buttons below the field to insert variables at the cursor position

The default pattern is `$$DATEMINUS12$$`, which produces filenames like `NightSummary_2026-03-31`.

## Available Variables

| Variable | Description | Example Output |
|----------|-------------|---------------|
| `$$DATEMINUS12$$` | Session date, shifted back 12 hours (so a session ending at 3 AM on April 1 shows as March 31) | `2026-03-31` |
| `$$DATE$$` | Current date at report generation time | `2026-04-01` |
| `$$DATETIME$$` | Current date and time | `2026-04-01_03-45-12` |
| `$$TIME$$` | Current time | `03-45-12` |
| `$$DATEUTC$$` | Current UTC date | `2026-04-01` |
| `$$TIMEUTC$$` | Current UTC time | `10-45-12` |
| `$$CAMERA$$` | Camera name from NINA | `ZWO ASI2600MM Pro` |
| `$$TELESCOPE$$` | Telescope name from NINA | `Esprit 100ED` |
| `$$SEQUENCETITLE$$` | Title of the NINA sequence that was running | `Spring Galaxies` |

## Subdirectories

Use backslashes (`\`) in the pattern to create subdirectories within the save path. For example:

- Pattern: `$$CAMERA$$\$$DATEMINUS12$$`
- Result: `Saved Reports\ZWO ASI2600MM Pro\2026-03-31\2026-03-31.html`

## How It Works

The pattern is applied to all delivery channels that use filenames:

- **Local save** — the pattern becomes a subfolder name inside the save directory, with the HTML file inside using the same resolved name
- **Email** — the pattern is used as the email subject suffix
- **Discord / Pushover** — the pattern appears in the message metadata

Each session gets its own folder. For example, with pattern `$$DATEMINUS12$$`:

```
Saved Reports/
  NightSummary_2026-03-31/
    NightSummary_2026-03-31.html
    (live stack master images, if saved)
  NightSummary_2026-04-01/
    NightSummary_2026-04-01.html
```

## Tips

- `$$DATEMINUS12$$` is the most useful for astrophotography since imaging sessions typically span midnight. A session that ends at 3 AM on April 1 will be filed under March 31.
- Combine variables for detailed naming: `$$CAMERA$$_$$DATEMINUS12$$` produces `ZWO ASI2600MM Pro_2026-03-31`
- Keep patterns simple — long filenames with many variables can be hard to scan when browsing your report archive
