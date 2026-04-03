---
layout: default
title: Home
nav_order: 1
---

# Night Summary

**Night Summary** is a plugin for [N.I.N.A.](https://nighttime-imaging.eu/) that automatically records your imaging sessions and generates detailed HTML reports when your sequence ends.

Whether you run a remote observatory or image from your backyard, Night Summary gives you a comprehensive look at what happened overnight — delivered to your inbox, Discord, or phone before you've had your morning coffee.

## Key Features

- **Automatic session recording** — captures every exposure, filter change, autofocus run, and equipment event as your sequence runs
- **Rich HTML reports** — event timelines, overhead breakdowns, altitude charts, sky thumbnails, image quality tables, and more
- **Multiple delivery channels** — email, Discord webhook, Pushover push notifications, or local file save
- **Live Stack integration** — includes live-stacked thumbnails from the Live Stack plugin in your report
- **Target Scheduler integration** — shows acquisition progress bars, minimum altitude lines, and tonight's preview when Target Scheduler is installed
- **Metric charts** — plot any combination of HFR, FWHM, guiding RMS, temperature, humidity, and 15+ other metrics
- **Overhead breakdown** — parses NINA logs to show exactly where your non-imaging time went (downloads, filter changes, autofocus, dithering, etc.)
- **Equipment profile** — collapsible section showing all connected equipment with customizable display names
- **Session history** — cumulative integration time per target across all sessions
- **Customizable detail levels** — Snapshot, Standard, or Full reports to match your preference
- **Light and dark mode** — reports render in your choice of color scheme
- **File naming patterns** — use NINA-style `$$PATTERN$$` variables in report filenames

## Installation

1. Open NINA and go to **Plugins** (the puzzle piece icon in the sidebar)
2. Search for **Night Summary**
3. Click **Install**
4. Restart NINA when prompted

Night Summary requires **NINA 3.2 or later**.

## Quick Start

After installing, Night Summary works with zero configuration — it records your session and generates a report automatically when your sequence ends. To actually *receive* the report, set up at least one [delivery channel]({% link delivery-channels.md %}).

See [Getting Started]({% link getting-started.md %}) for a walkthrough.

## Report Detail Levels

Night Summary offers three detail levels that control how much information appears in your report:

| Level | What's Included |
|-------|----------------|
| **Snapshot** | Header, stat boxes, filter table |
| **Standard** | + Event timeline, altitude charts, image quality, per-target IQ, Target Scheduler bars |
| **Full** | + Overhead breakdown, metric charts, session history, tonight's preview |

## Version

This documentation covers **Night Summary v2.10.0** for NINA 3.2+.
