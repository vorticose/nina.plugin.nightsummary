#!/bin/sh
# Night Summary Companion - one-line macOS installer.
#
#   curl -fsSL https://github.com/vorticose/nina.plugin.nightsummary/releases/latest/download/install-companion-mac.sh | sh
#
# Why this exists: the .app is ad-hoc signed (no paid Apple Developer account),
# so a DMG downloaded in a browser gets the com.apple.quarantine flag and
# macOS 26 blocks it -- and Apple removed the old right-click->Open bypass, so
# most users just give up. A file downloaded by *curl* is NOT quarantined, so
# this installer pulls the DMG itself, copies the app into /Applications, clears
# any quarantine flag defensively, and launches it -- no Gatekeeper prompt, no
# Terminal gymnastics for the user beyond pasting this one line.
#
# User-scoped where it can be: only /Applications needs write (standard for app
# installs). POSIX sh so it runs under /bin/sh when piped.
#
# Test against a local build without a published release:
#   NSC_DMG=/path/to/NightSummaryCompanion-mac-arm64.dmg sh scripts/install-companion-mac.sh
# Or point at an arbitrary URL:
#   NSC_URL=http://host:8000/NightSummaryCompanion-mac-arm64.dmg sh scripts/install-companion-mac.sh
set -eu

REPO="vorticose/nina.plugin.nightsummary"
APP="NightSummaryCompanion.app"
DEST="/Applications/$APP"

[ "$(uname -s 2>/dev/null)" = "Darwin" ] || {
    echo "This installer is for macOS. On Linux use install-companion.sh." >&2; exit 1; }

ARCH="$(uname -m 2>/dev/null || echo unknown)"
case "$ARCH" in
    arm64)  DMG="NightSummaryCompanion-mac-arm64.dmg" ;;
    x86_64) DMG="NightSummaryCompanion-mac-x64.dmg" ;;
    *) echo "Unsupported arch '$ARCH' (need arm64 or x86_64)." >&2; exit 1 ;;
esac
URL="${NSC_URL:-https://github.com/$REPO/releases/latest/download/$DMG}"

command -v hdiutil >/dev/null 2>&1 || { echo "hdiutil is required (macOS)." >&2; exit 1; }

TMP="$(mktemp -d)"
MNT=""
cleanup() {
    [ -n "$MNT" ] && hdiutil detach "$MNT" -quiet >/dev/null 2>&1 || true
    rm -rf "$TMP"
}
trap cleanup EXIT INT TERM

if [ -n "${NSC_DMG:-}" ]; then
    echo "Using local DMG: $NSC_DMG"
    cp "$NSC_DMG" "$TMP/c.dmg"
else
    command -v curl >/dev/null 2>&1 || { echo "curl is required." >&2; exit 1; }
    echo "Downloading Night Summary Companion ($ARCH) ..."
    curl -fSL "$URL" -o "$TMP/c.dmg"
fi

echo "Mounting ..."
MNT="$(hdiutil attach "$TMP/c.dmg" -nobrowse -readonly | grep -o '/Volumes/.*' | head -1)"
[ -n "$MNT" ] || { echo "Failed to mount the DMG." >&2; exit 1; }

SRC="$(find "$MNT" -maxdepth 2 -name "$APP" -type d 2>/dev/null | head -1)"
[ -n "$SRC" ] || { echo "Could not find $APP inside the DMG." >&2; exit 1; }

# Stop a running instance so the copy can overwrite it cleanly.
osascript -e 'quit app "NightSummaryCompanion"' >/dev/null 2>&1 || true
pkill -x NightSummaryCompanion-bin >/dev/null 2>&1 || true
sleep 1

echo "Installing to $DEST ..."
rm -rf "$DEST"
cp -R "$SRC" "$DEST"

# Defensive: a curl'd DMG isn't quarantined, but strip it anyway in case the
# user pre-downloaded the DMG via a browser and pointed NSC_DMG at it. Also make
# sure the bundle's launcher scripts kept their exec bit through the copy.
xattr -dr com.apple.quarantine "$DEST" >/dev/null 2>&1 || true
chmod +x "$DEST/Contents/MacOS/"* >/dev/null 2>&1 || true

echo ""
echo "Installed Night Summary Companion to /Applications."
echo "Launching ..."
open "$DEST" || true
echo ""
echo "It opens the setup wizard in your browser. Pair it with NINA, then enable"
echo "start-at-login from the dashboard: Settings -> Start at login."
