---
layout: default
title: Metric Charts
nav_order: 12
---

# Metric Charts

Night Summary can generate charts showing how imaging metrics changed over the course of your session. This helps you spot trends, correlations, and problems.

Available at **Full** detail level only.

## Primary and Secondary Metrics

Each chart's Y-axis can show up to two metrics:

- **Primary Metric** — plotted as the main data series (left Y-axis, solid line)
- **Secondary Metric** — optional second series on a separate right Y-axis (dashed line). Set to "None" to show only the primary.

Plotting two metrics on the same Y-axis is useful for spotting correlations — for example, HFR vs. temperature to see how focus changes with cooling.

## X-Axis Options

The horizontal axis can be set to any of the following:

| X-Axis | Description |
|--------|-------------|
| **Time** | Wall-clock timestamp of each exposure (default) |
| **Frame Index** | Sequential frame number (1, 2, 3...) |
| Any metric | Use any metric listed below as the X-axis for correlation plots |

Setting the X-axis to a metric instead of time is useful for correlation analysis — for example, "does my HFR correlate with altitude?"

## Available Metrics

All of these can be used as primary metric, secondary metric, or X-axis:

**Image quality**

| Metric | Unit | Notes |
|--------|------|-------|
| **HFR** | pixels | Half-flux radius — NINA's native focus quality metric |
| **FWHM** | arcsec | Full-width at half-maximum. Requires the Hocus Focus plugin. |
| **Eccentricity** | | Star elongation metric. Requires the Hocus Focus plugin. |
| **Seeing FWHM** | arcsec | Atmospheric seeing. Requires an ASCOM seeing monitor connected as a NINA weather source. |
| **Star Count** | | Number of stars detected in each frame |
| **Guiding RMS** | arcsec | Total guiding RMS from PHD2 or similar |

**Image statistics**

| Metric | Unit | Notes |
|--------|------|-------|
| **Median ADU** | ADU | Median pixel value. Useful for tracking sky background through a session. |
| **Mean ADU** | ADU | Mean pixel value |
| **Std Dev** | ADU | Standard deviation of pixel values |
| **MAD** | ADU | Median absolute deviation — a robust measure of image noise spread |
| **Min ADU** | ADU | Minimum pixel value |
| **Max ADU** | ADU | Maximum pixel value |

**Capture settings**

| Metric | Unit | Notes |
|--------|------|-------|
| **Exposure** | seconds | Exposure duration for each frame |
| **Gain** | | Camera gain setting |
| **Offset** | | Camera offset setting |

**Temperature**

| Metric | Unit | Notes |
|--------|------|-------|
| **Camera Temp** | C | Sensor temperature (actual) |
| **Cooler Setpoint** | C | Camera cooler target temperature |
| **Focuser Temp** | C | Temperature sensor on the focuser. Requires a focuser with built-in temp sensor. |
| **Ambient Temp** | C | Outside air temperature. Requires a weather data source. |
| **Dew Point** | C | Dew point. Requires a weather data source. |
| **Sky Temperature** | C | Sky temperature from a cloud sensor. Requires a weather data source. |

**Pointing and equipment**

| Metric | Unit | Notes |
|--------|------|-------|
| **Altitude** | degrees | Target altitude above the horizon |
| **Azimuth** | degrees | Target azimuth |
| **Airmass** | | Atmospheric airmass (derived from altitude) |
| **Sky Position Angle** | degrees | Sky rotation angle of the frame (from plate solve) |
| **Rotator Position** | degrees | Mechanical rotator position. Requires a motorized rotator. |
| **Focuser Position** | steps | Motorized focuser position in steps |

**Sky and weather**

| Metric | Unit | Notes |
|--------|------|-------|
| **Sky Quality** | mag/arcsec² | Sky darkness. Requires a sky quality meter connected as a NINA weather source. |
| **Sky Brightness** | | Sky background brightness. Requires a weather data source with this sensor. |
| **Cloud Cover** | % | Cloud coverage. Requires a cloud sensor. |
| **Humidity** | % | Relative humidity. Requires a weather data source. |
| **Wind Speed** | m/s | Wind speed. Requires a weather data source. |
| **Wind Gust** | m/s | Wind gust speed. Requires a weather data source. |
| **Wind Direction** | degrees | Wind direction. Requires a weather data source. |
| **Pressure** | hPa | Atmospheric pressure. Requires a weather data source. |

{: .note }
> Metrics that require external hardware or plugins will show no data if the equipment isn't connected. The chart simply omits data points where the metric value is zero or missing.

## Event Markers

When the X-axis is set to **Time**, you can overlay vertical dashed lines on the chart marking specific events that occurred during the session:

| Marker | Label | Color | Triggered by |
|--------|-------|-------|--------------|
| **AutoFocus** | AF | Purple | Each AutoFocus run |
| **Meridian Flip** | MF | Amber | Meridian flip events |
| **Safe** | S | Green | Safety monitor transitions to Safe |
| **Unsafe** | US | Red | Safety monitor transitions to Unsafe |

Each marker type can be toggled independently in **Options → Metric Chart Settings** (see the Settings Reference for exact toggle names). The colors match the markers shown on the session event timeline at the top of the report.

Hover over any marker to see a tooltip with the event description and timestamp. Markers are only displayed when the chart's X-axis is set to Time — they don't appear for Frame Index or metric x-axes.

## Additional Charts

You can add up to 4 additional charts beyond the primary one:

1. Click **+ Add Chart** in the settings
2. Configure the primary metric, secondary metric, and X-axis for the new chart
3. Click the **X** button next to a chart to remove it

Each additional chart has its own independent metric selections. This lets you create a dashboard-style report with multiple views of your session data — for example, one chart for HFR over time, another for temperature vs. focuser position, and a third for guiding RMS vs. altitude.

## Target and Filter Chips

In multi-target or multi-filter sessions, chip rows appear above the chart for narrowing which data points are shown. Up to two rows are displayed:

**Target chips** — shown when a session contains 2 or more distinct targets. Chips are labeled with each target name plus an "All Targets" chip that restores the full view.

**Filter chips** — shown when a session contains 2 or more distinct filters. Chips are labeled with each filter name plus an "All" chip.

Both rows are independent and combine additively: selecting Target=M51 and Filter=Ha shows only exposures of M51 taken through Ha, with the Y-axis rescaling to fit that subset. Either row can be kept at "All" while the other is narrowed.

Single-target sessions skip the target row; single-filter sessions skip the filter row. If a session has only one target and one filter, no chip rows appear.

Both chip selectors can be disabled independently in **Options → Metric Chart Settings** if you prefer a cleaner look.

{: .note }
> The chip selectors are implemented with pure CSS radio inputs — they work in every HTML viewer including email clients and iOS Quick Look, with no JavaScript required.

## Chart Interaction

In the HTML report, chart data points show **hover tooltips** with the exact timestamp, metric value, and the filter used for that exposure (e.g. `22:15 — 1.67 px [Ha]`). This makes it easy to identify specific frames with unusual values or spot filter-specific patterns.

## Common Chart Configurations

Here are some useful metric combinations to try:

| Purpose | Primary | Secondary | X-Axis |
|---------|---------|-----------|--------|
| **Focus quality over time** | HFR | Focuser Temp | Time |
| **Guiding performance** | Guiding RMS | Altitude | Time |
| **Focus vs temperature** | HFR | — | Focuser Temp |
| **Star count consistency** | Star Count | Humidity | Time |
| **Altitude effects** | HFR | Airmass | Altitude |
| **Seeing conditions** | FWHM | Wind Speed | Time |
| **Sky background** | Median ADU | — | Time |
