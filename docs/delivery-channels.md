---
layout: default
title: Delivery Channels
nav_order: 4
---

# Delivery Channels

Night Summary can deliver your report through four channels. You can enable any combination — they all run in parallel when the session ends.

## Email

Sends the full HTML report as an email. The report renders inline in most email clients (Gmail, Outlook, Apple Mail).

### Gmail Setup

1. In Night Summary settings, enable **Email Reports** and select **Gmail**
2. Enter your **Gmail address**
3. Generate a **Gmail App Password**:
   - Go to [myaccount.google.com](https://myaccount.google.com)
   - Navigate to **Security > App Passwords** (you must have 2-Factor Authentication enabled)
   - Create a new app password for "Mail"
   - Copy the 16-character password
4. Paste the app password into the **App Password** field
5. Enter the **Recipient Email Address** (can be the same as your Gmail address, or any other address)
6. Click **Send Test Email** to verify

{: .important }
> Do **not** use your regular Gmail password. Google requires an App Password for third-party SMTP access. If you don't see the App Passwords option, enable 2-Factor Authentication first.

### Other Email Providers

Select **Other provider** to configure any SMTP-capable email service:

1. Enter your **Sender Email**, **App Password / API Key**, and **Recipient Email Address**
2. Configure the SMTP server settings:

| Provider | SMTP Server | Port | TLS |
|----------|------------|------|-----|
| **Outlook.com** | smtp-mail.outlook.com | 587 | On |
| **Yahoo** | smtp.mail.yahoo.com | 587 | On |
| **iCloud** | smtp.mail.me.com | 587 | On |

{: .note }
> All major providers require an App Password or API key — not your regular account password. Check your provider's security settings to generate one.

## Discord

Posts a summary message with key stats to a Discord channel via webhook. The message includes an embedded summary (not the full HTML report).

### Setup

1. In your Discord server, go to the channel where you want reports
2. Click the **gear icon** (Edit Channel) > **Integrations** > **Webhooks**
3. Click **New Webhook**, give it a name (e.g., "Night Summary"), and copy the webhook URL
4. In Night Summary settings, enable **Discord** and paste the **Webhook URL**
5. Click **Send Test Message** to verify

## Pushover

Sends a push notification to your phone with key session stats (total images, exposure time, targets). Good for a quick heads-up that your session completed.

### Setup

1. Install the Pushover app on your phone ([iOS](https://pushover.net/clients/ios) / [Android](https://pushover.net/clients/android))
2. Create an account at [pushover.net](https://pushover.net)
3. Create a new application at [pushover.net/apps/build](https://pushover.net/apps/build) — name it "Night Summary" or similar
4. Copy your **App Token** (from the application page) and **User Key** (from your Pushover dashboard)
5. In Night Summary settings, enable **Pushover** and paste both keys
6. Click **Send Test Notification** to verify

## Local File Save

Saves the HTML report as a file on your computer. Useful for archiving or viewing reports in a browser.

### Setup

1. Enable **Save Report Locally**
2. Optionally set a **Save Path** — leave blank to use the default: `Documents\N.I.N.A.\Night Summary\Saved Reports\`
3. Configure the **File name pattern** if you want custom naming (see [File Naming Patterns]({% link file-naming-patterns.md %}))

Reports are saved as `.html` files inside a session-specific subfolder. You can open them in any web browser.

## Delivery Timing

All enabled channels are triggered simultaneously when your sequence ends. If one channel fails (e.g., network issue with Discord), the others still deliver independently. Check NINA's notification area for success/failure messages.

## Test Buttons

Each channel has a **Send Test** button in settings. These use your most recent session data (or test data) to generate and deliver a real report, so you can verify your setup without waiting for a full imaging session.

There's also a **Send Test Report** section at the bottom of settings that sends through all enabled channels at once using a test database.
