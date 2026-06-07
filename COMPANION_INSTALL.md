# Night Summary Companion — Install

The **Companion** is a separate, read-only dashboard app you run on another computer
(Mac, Windows, or Linux). It syncs a copy of your Night Summary sessions from the
machine running NINA and serves the same dashboard — so you can browse your imaging
history from the couch, another room, or anywhere on your network, without touching
the imaging rig.

Download the latest build from the
[**Releases page**](https://github.com/vorticose/nina.plugin.nightsummary/releases/latest),
then follow the steps for your OS. Once it's running it opens a setup wizard in your
browser: enter your NINA machine's address and a **pairing token** — generate one in
NINA under *Options → Night Summary → Local Dashboard → Companion Pairing*.

## Windows

1. Download **`NightSummaryCompanion-win-x64.zip`** and unzip it.
2. Double-click **`NightSummaryCompanion.exe`** — it's a single file, no install, drop it anywhere.
3. Your browser opens the setup wizard. Pair it and you're done.

## macOS

1. Download **`NightSummaryCompanion-mac-arm64.tar.gz`** (Apple Silicon).
2. Double-click to extract, then drag **`NightSummaryCompanion.app`** into **Applications**.
3. Open it (first launch on a downloaded copy: right-click → Open). The setup wizard opens in your browser.

## Linux

**Easiest — one line, no root:**

```sh
curl -fsSL https://github.com/vorticose/nina.plugin.nightsummary/releases/latest/download/install-companion.sh | sh
```

Then launch **Night Summary Companion** from your applications menu.

**Other options:**

- **.deb** (Debian / Ubuntu / Mint / Pop) — download `nightsummary-companion_*.deb`, then
  `sudo apt install ./nightsummary-companion_*.deb`
- **AppImage** (portable) — download `NightSummaryCompanion-x86_64.AppImage`, mark it
  executable (`chmod +x`), and run it.
- **Tarball** (manual / headless) — download `NightSummaryCompanion-linux-x64.tar.gz`,
  extract, run `./install.sh` (or just `./NightSummaryCompanion serve`).

## After install

The Companion runs as a background app and opens its dashboard in your browser. It keeps
syncing on a schedule and whenever NINA finishes a session, and the dashboard refreshes
itself as new data arrives. You can enable **Start at login** in its Settings tab.

Your pairing survives updates — you pair once per computer, not once per release.
