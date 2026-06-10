# Night Summary Companion — Install

The **Companion** is a standalone dashboard app you run on another computer
(Mac, Windows, or Linux). It keeps its own synced copy of your Night Summary sessions
from the machine running NINA and serves the full dashboard independently — a one-way
sync, so it never changes anything on the imaging rig. Browse your imaging history from
the couch, another room, or anywhere on your network.

Download the latest build from the
[**Releases page**](https://github.com/vorticose/nina.plugin.nightsummary/releases/latest),
then follow the steps for your OS. Once it's running it opens a setup wizard in your
browser: enter your NINA machine's address and a **pairing token** — generate one in
NINA under *Options → Night Summary → Local Dashboard → Companion Pairing*.

## Windows

1. Download **`NightSummaryCompanion-win-x64.zip`** and unzip it.
2. Double-click **`NightSummaryCompanion.exe`** — it's a single file, no install, drop it anywhere.
   - First launch may show Windows SmartScreen (*"Windows protected your PC"*). Click **More info → Run anyway** — expected for an unsigned open-source app.
3. Your browser opens the setup wizard. Pair it and you're done.

## macOS

**Easiest — one line, no Gatekeeper prompt.** Paste into **Terminal**:

```sh
curl -fsSL https://github.com/vorticose/nina.plugin.nightsummary/releases/latest/download/install-companion-mac.sh | sh
```

It auto-detects Apple Silicon vs Intel, installs the app to **Applications**, and
launches it. Because the download comes through `curl` rather than a browser, macOS
never tags it as quarantined — so there's **no "unidentified developer" wall** and
nothing else to click.

> The app is ad-hoc signed (it's open-source, with no paid Apple Developer account
> behind it). That only matters for a DMG downloaded in a **browser**, which macOS
> quarantines and then blocks — recent macOS no longer offers the old right-click →
> Open bypass. The `curl` installer above sidesteps that entirely.

**Manual (.dmg)** — if you'd rather use the disk image:

1. Download **`NightSummaryCompanion-mac-arm64.dmg`** (Apple Silicon — M1 and newer) or **`NightSummaryCompanion-mac-x64.dmg`** (Intel).
2. Open it and drag **`NightSummaryCompanion.app`** onto the **Applications** folder.
3. Clear the download flag once, in **Terminal**:
   ```sh
   xattr -dr com.apple.quarantine /Applications/NightSummaryCompanion.app
   ```
   Then open the app normally. (Or try **System Settings → Privacy & Security → Open
   Anyway** — but on current macOS that's unreliable for unsigned apps, so the command
   above is the sure path.)

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
  executable (`chmod +x`), and run it. If it errors about `libfuse.so.2`, either install
  FUSE 2 (`sudo apt install libfuse2`) or run it with `--appimage-extract-and-run`.
- **Tarball** (manual / headless) — download `NightSummaryCompanion-linux-x64.tar.gz`,
  extract, run `./install.sh` (or just `./NightSummaryCompanion serve`).

## After install

The Companion runs as a background app and opens its dashboard in your browser. It keeps
syncing on a schedule and whenever NINA finishes a session, and the dashboard refreshes
itself as new data arrives. You can enable **Start at login** in its Settings tab.

Your pairing survives updates — you pair once per computer, not once per release.
