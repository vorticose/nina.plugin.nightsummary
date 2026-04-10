---
layout: default
title: FAQ & Troubleshooting
nav_order: 12
---

# FAQ & Troubleshooting

## General

### How does Night Summary know when my session starts and ends?

Night Summary uses two sequence instructions: **Night Summary Start** and **Night Summary End**. Place Start near the beginning of your sequence (before imaging) and End at the very end (after all imaging and park/warm-up steps). The session begins when Start executes and ends when End executes or the sequence is stopped.

### Can I use Night Summary without any delivery channels enabled?

Yes. Night Summary always records session data to its local database. You can enable delivery channels later and use the **Resend Previous Session** feature to generate reports for past sessions.

### Where is my data stored?

Session data is stored in a SQLite database at:
```
%LOCALAPPDATA%\NINA\NightSummary\nightsummary.sqlite
```
Plugin settings are stored as a JSON file in the same directory.

### Do settings survive plugin updates?

Yes. Settings are stored outside the plugin folder in the NINA data directory, so they persist across plugin updates and NINA version changes.

---

## Report Issues

### My report didn't send / I didn't receive it

1. Make sure both **Night Summary Start** and **Night Summary End** instructions are in your sequence — the End instruction is what triggers report generation
2. Check NINA's notification area (bottom of the screen) for error messages
3. Verify your delivery channel settings using the **Send Test** buttons
4. For email: check your spam/junk folder
5. For Discord: verify the webhook URL is still valid (webhooks can be deleted from the Discord server side)
6. For Pushover: verify your app token and user key are correct

### Sky thumbnails are missing

Sky thumbnails are fetched from the CDS HiPS2FITS service, which requires an internet connection. If thumbnails are missing:

- Check your internet connection
- The CDS service may be temporarily unavailable — thumbnails will be missing for that report but should work next time
- Verify **Show Sky Thumbnails** is enabled in settings

### The report preview window is blank or shows an error

The preview window uses Microsoft WebView2, which is normally installed with Windows 10/11. If you see a blank window:

1. Check if WebView2 is installed: look for "Microsoft Edge WebView2 Runtime" in **Settings > Apps**
2. If missing, download it from [developer.microsoft.com/en-us/microsoft-edge/webview2](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)
3. Install the "Evergreen Bootstrapper" version

### Charts show no data for some metrics

Metrics require the corresponding equipment or plugin to be connected:

- **FWHM, Eccentricity** — requires the Hocus Focus plugin
- **Focuser Temp** — requires a focuser with a built-in temperature sensor
- **Ambient Temp, Humidity, Dew Point, Wind Speed, Pressure** — requires a device connected as a NINA weather data source (e.g., Pegasus UPB, AAG CloudWatcher)
- **Sky Quality** — requires a sky quality meter connected as a NINA weather data source
- **Cloud Cover** — requires a cloud sensor connected as a NINA weather data source

If the equipment isn't connected, those data points are simply omitted from charts.

### Yield and overhead analysis shows high "Unaccounted" time

Some unaccounted time is normal — it represents gaps between operations that the log parser can't attribute to a specific category (NINA internal processing, brief pauses between sequence items, etc.).

If unaccounted time is unusually high:
- Check if you have custom sequence items or third-party plugins that add operations not tracked by the parser
- Verify the NINA log file exists and is accessible (the parser reads from `%LOCALAPPDATA%\NINA\Logs\`)

---

## Email

### Gmail says "Less secure app access" or blocks my login

Gmail requires an **App Password**, not your regular password. You must also have **2-Factor Authentication** enabled on your Google account:

1. Go to [myaccount.google.com](https://myaccount.google.com) > **Security**
2. Enable **2-Step Verification** if not already on
3. Go to **App Passwords**
4. Generate a new app password for "Mail"
5. Use this 16-character password in Night Summary settings

### Email test succeeds but I don't receive it

1. Check your **spam/junk** folder — automated emails often land there
2. If using a different recipient than sender, verify the recipient address is correct
3. Some corporate email servers may block or delay automated SMTP messages

---

## Target Scheduler

### Target Scheduler options are greyed out

This means Target Scheduler is not installed or not detected. Install Target Scheduler from NINA's plugin manager and restart NINA.

### Tonight's Preview doesn't appear

The preview requires the Target Scheduler API to be enabled:

1. In Target Scheduler, go to **Target Management**
2. Select your active profile
3. Click the **gear icon** > **API Preferences**
4. Enable the API

Also check that your detail level is set to **Full** and the **Show Tonight's Preview** toggle is on.

---

## Live Stack

### Live Stack images don't appear in my report

- Live Stack must be **installed and running** during the imaging session
- The **Show Live Stack Images** setting must be enabled
- Live Stack must have received at least one frame for the target
- If you started Live Stack after imaging a target, images won't be captured for that target

### Can I see Live Stack images when resending an old report?

Only if you had **Save Report Locally** enabled when the original report was generated. Live stack master images are saved alongside the HTML report. Without local save, the images aren't persisted and can't be recovered for resend.

---

## Performance

### Report generation seems slow

The most time-consuming parts of report generation are:

1. **Sky thumbnails** — each thumbnail requires an HTTP request to the CDS service (~1-2 seconds per target)
2. **Tonight's Preview** — if Target Scheduler API is enabled, TS needs to compute the full night plan
3. **Log parsing** — for very long sessions, parsing the NINA log takes a few seconds

If speed is a concern, you can disable sky thumbnails and tonight's preview in settings.

### Does Night Summary affect imaging performance?

No. Night Summary's data recording is lightweight — it subscribes to NINA's existing events and writes to a local SQLite database. The report is generated only after the sequence ends, so it never interferes with active imaging.

The only background activity during imaging is Live Stack image capture, which compresses received images asynchronously (~1 MB of work per broadcast, typically every few minutes).
