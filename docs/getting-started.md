---
layout: default
title: Getting Started
nav_order: 2
---

# Getting Started

This guide walks you through installing Night Summary, configuring basic settings, and generating your first report.

## Install the Plugin

1. Open NINA and click the **Plugins** icon in the left sidebar (the puzzle piece)
2. In the **Available** tab, search for **Night Summary**
3. Click **Install** and restart NINA when prompted

After installation, Night Summary appears under **Options > Night Summary Settings** (expand the section).

## How It Works

Night Summary runs automatically in the background:

1. **Session starts** when your NINA sequence begins imaging
2. **Data is recorded** for every exposure — HFR, guiding RMS, star count, filter, temperature, and more
3. **Session ends** when the sequence completes (or you stop it manually)
4. **Report is generated** and delivered through your enabled channels

No sequence instructions are needed — the plugin hooks into NINA's image save events directly.

## Basic Configuration

Open **Options > Night Summary Settings** to configure the plugin.

### Choose a Detail Level

The **Detail Level** dropdown controls how much information your report includes:

- **Snapshot** — just the essentials: header, stat boxes, and filter table. Good for a quick glance.
- **Standard** — adds event timeline, altitude charts, image quality tables, and Target Scheduler progress bars. The best balance for most users.
- **Full** — everything in Standard plus overhead breakdown, customizable metric charts, session history, and tonight's preview. For users who want maximum detail.

### Set Up a Delivery Channel

Night Summary can deliver reports through four channels. You need at least one enabled to receive reports. See [Delivery Channels]({% link delivery-channels.md %}) for detailed setup instructions.

- **Email** — full HTML report sent to your inbox
- **Discord** — summary with stats posted to a Discord channel via webhook
- **Pushover** — push notification to your phone with key session stats
- **Local Save** — HTML file saved to a folder on your computer

### Preview Your Report

Before waiting for a real imaging session, you can preview how your report will look:

1. Click **Preview Report** at the bottom of the Report Content section
2. A preview window opens showing a report generated from your most recent session data (or test data if no sessions exist)

This lets you tweak settings like detail level, chart metrics, and toggles to get the report looking the way you want.

## Your First Real Report

1. Run an imaging sequence as you normally would
2. When the sequence completes, Night Summary automatically generates and delivers your report
3. Check your configured delivery channel (email, Discord, etc.) for the report

If something doesn't look right, check the [FAQ]({% link faq.md %}) for common issues, or adjust settings and use **Preview Report** to iterate.

## Resending a Previous Report

If you need to resend a report (for example, after changing delivery settings):

1. Go to **Options > Night Summary Settings** and scroll to **Resend Previous Session**
2. Select a session from the dropdown (shows the 30 most recent by default)
3. Click **Send Report** to deliver it through all currently enabled channels

Use the **Search older sessions** expander to find sessions by date range.

## Next Steps

- [Report Sections]({% link report-sections.md %}) — understand what each part of the report shows
- [Settings Reference]({% link settings-reference.md %}) — full list of every setting and what it does
- [Delivery Channels]({% link delivery-channels.md %}) — detailed setup for email, Discord, Pushover, and local save
