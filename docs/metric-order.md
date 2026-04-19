# Metric Chart Combo Box Ordering

Edit the order below. Top = most useful/most likely to be used. Bottom = least useful.
When finalized, this order will be applied to all 3 combo boxes (X-axis, Primary, Secondary)
and the corresponding constants in ChartGenerator.cs.

The Secondary combo box always has "None" as index 0; everything else follows this order.
The X-axis combo box always has "Time" and "Frame Index" as indices 0-1; everything else follows.

## Proposed Order

| # | Metric | Unit | Why this rank | Category |
|---|--------|------|---------------|----------|
| 1 | HFR | px | Universal quality metric, every imager watches this | Image Quality |
| 2 | FWHM | arcsec | Second most popular quality metric (requires Hocus Focus) | Image Quality |
| 3 | Guiding RMS | arcsec | Everyone who guides watches this | Guiding |
| 4 | Star Count | count | Common quality/sky condition indicator | Image Quality |
| 5 | Eccentricity | ratio | Popular with Hocus Focus users, shows optical/tracking issues | Image Quality |
| 6 | Altitude | degrees | Shows target rising/setting through the night | Pointing |
| 7 | Airmass | ratio | Atmospheric extinction, pairs with altitude | Pointing |
| 8 | Focuser Temp | C | Key driver of focus drift | Temperature |
| 9 | Ambient Temp | C | Affects focus and dew, widely available | Temperature |
| 10 | Focuser Position | steps | Shows focus drift directly | Equipment |
| 11 | Camera Temp | C | Verify cooler is holding setpoint | Temperature |
| 12 | Cooler Setpoint | C | Paired with camera temp for cooling health | Temperature |
| 13 | Humidity | % | Dew prevention, widely available from weather devices | Weather |
| 14 | Dew Point | C | Dew prevention, paired with ambient temp | Weather |
| 15 | Sky Quality | mag/arcsec2 | Light pollution monitoring (requires SQM device) | Sky Conditions |
| 16 | Cloud Cover | % | Weather monitoring (requires cloud sensor) | Sky Conditions |
| 17 | Sky Temp | C | IR cloud detection -- user-requested feature | Sky Conditions |
| 18 | Median ADU | ADU | Background level, shows sky darkening/brightening | Image Statistics |
| 19 | Mean ADU | ADU | Background level (less robust than median) | Image Statistics |
| 20 | Wind Speed | m/s | Seeing/mount stability/safety | Weather |
| 21 | Wind Gust | m/s | Peak wind, complements wind speed | Weather |
| 22 | Pressure | hPa | Atmospheric conditions, slow-moving | Weather |
| 23 | Seeing FWHM | arcsec | External ASCOM seeing monitor (rare device) | Sky Conditions |
| 24 | Std Deviation | ADU | Noise metric, complements HFR/FWHM | Image Statistics |
| 25 | MAD | ADU | Robust noise metric (less outlier-sensitive than StDev) | Image Statistics |
| 26 | Exposure | s | Usually constant, useful for mixed-length workflows | Capture Settings |
| 27 | Gain | value | Usually constant, useful for multi-gain workflows | Capture Settings |
| 28 | Azimuth | degrees | Niche, mostly flat unless tracking across meridian | Pointing |
| 29 | Wind Direction | degrees | Niche, requires ASCOM ObservingConditions | Weather |
| 30 | Sky Brightness | Lux | Niche, few people have Lux sensors | Sky Conditions |
| 31 | Rotator Position | degrees | Only for rotator users | Equipment |
| 32 | Position Angle | degrees | From plate solving, niche | Pointing |
| 33 | Offset | value | Almost never changes mid-session | Capture Settings |
| 34 | Min ADU | ADU | Sensor floor, noisy, dominated by hot pixels | Image Statistics |
| 35 | Max ADU | ADU | Saturation detection, noisy, dominated by cosmic rays | Image Statistics |
