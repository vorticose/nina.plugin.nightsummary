---
layout: default
title: Metric Charts
nav_order: 11
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

| Metric | Unit | Notes |
|--------|------|-------|
| **HFR** | pixels | Half-flux radius — NINA's native focus quality metric |
| **FWHM** | arcsec | Full-width at half-maximum. Requires the Hocus Focus plugin. |
| **Guiding RMS** | arcsec | Total guiding RMS from PHD2 or similar |
| **Focuser Temp** | C | Temperature sensor on the focuser. Requires a focuser with built-in temp sensor. |
| **Ambient Temp** | C | Outside air temperature. Requires a weather data source. |
| **Eccentricity** | | Star elongation metric. Requires the Hocus Focus plugin. |
| **Altitude** | degrees | Target altitude above the horizon |
| **Airmass** | | Atmospheric airmass (derived from altitude) |
| **Humidity** | % | Relative humidity. Requires a weather data source. |
| **Focuser Position** | steps | Motorized focuser position in steps |
| **Sky Quality** | mag/arcsec2 | Sky brightness. Requires a sky quality meter. |
| **Cloud Cover** | % | Cloud coverage. Requires a cloud sensor. |
| **Camera Temp** | C | Sensor cooling temperature |
| **Dew Point** | C | Dew point temperature. Requires a weather data source. |
| **Wind Speed** | m/s | Wind speed. Requires a weather data source. |
| **Pressure** | hPa | Atmospheric pressure. Requires a weather data source. |
| **Star Count** | | Number of stars detected in each frame |
| **Azimuth** | degrees | Target azimuth |
| **Seeing FWHM** | arcsec | Atmospheric seeing measurement. Requires an ASCOM-compatible seeing monitor connected as a NINA weather data source. |

{: .note }
> Metrics that require external hardware or plugins will show no data if the equipment isn't connected. The chart simply omits data points where the metric value is zero or missing.

## Additional Charts

You can add more charts beyond the primary one:

1. Click **+ Add Chart** in the settings
2. Configure the primary metric, secondary metric, and X-axis for the new chart
3. Click the **X** button next to a chart to remove it

Each additional chart has its own independent metric selections. This lets you create a dashboard-style report with multiple views of your session data — for example, one chart for HFR over time, another for temperature vs. focuser position, and a third for guiding RMS vs. altitude.

## Chart Interaction

In the HTML report, chart data points show **hover tooltips** with the exact timestamp and value. This makes it easy to identify specific frames with unusual values.

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
