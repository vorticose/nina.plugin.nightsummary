#!/bin/bash
# Builds a Linux AppImage of the companion: a single double-click file, no
# extract/install step for the user (the idiomatic "real Linux user" delivery).
#
# Produces: build/companion-linux/NightSummaryCompanion-x86_64.AppImage
#
# Consumes the self-contained linux binary staged by build-companion-linux.ps1
# (build/companion-linux/staging-x64/NightSummaryCompanion/NightSummaryCompanion-bin),
# so run that first. Wraps it in an AppDir whose AppRun is the same exit-88/0
# watchdog the tarball uses, then packs with appimagetool.
#
# Runs on Linux (a real box, WSL, or the CI ubuntu runner). No FUSE needed --
# appimagetool is invoked with --appimage-extract-and-run.
#
# Usage:  bash scripts/build-companion-appimage.sh
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
OUT="$ROOT/build/companion-linux"
STAGE="$OUT/staging-x64/NightSummaryCompanion"
BIN="$STAGE/NightSummaryCompanion-bin"
ICON="$ROOT/assets/companion-icon/companion-256.png"

[ -f "$BIN" ]  || { echo "ERROR: $BIN missing -- run scripts/build-companion-linux.ps1 first."; exit 1; }
[ -f "$ICON" ] || { echo "ERROR: $ICON missing -- run scripts/gen-companion-icons.py first."; exit 1; }
# appimagetool shells out to `file` even when ARCH is set. Present on CI ubuntu
# runners; a bare WSL/Debian needs it installed.
command -v file >/dev/null 2>&1 || { echo "ERROR: 'file' command required by appimagetool. Install it (e.g. sudo apt-get install -y file)."; exit 1; }

APPDIR="$OUT/NightSummaryCompanion.AppDir"
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"
cp "$BIN" "$APPDIR/usr/bin/NightSummaryCompanion-bin"
chmod +x "$APPDIR/usr/bin/NightSummaryCompanion-bin"

# AppRun = the watchdog launcher (identical contract to the tarball/mac/win:
# exit 88 from the app -> respawn (dashboard Restart), exit 0 -> quit). Args
# passed to the AppImage flow straight through to the binary; no args -> the
# binary defaults to `serve`.
cat > "$APPDIR/AppRun" <<'APPRUN'
#!/bin/bash
HERE="$(dirname "$(readlink -f "$0")")"
BIN="$HERE/usr/bin/NightSummaryCompanion-bin"
while :; do
    "$BIN" "$@"
    code=$?
    case $code in
        88) sleep 1 ;;     # dashboard Restart: brief pause, respawn
        0)  exit 0 ;;      # dashboard Quit / clean shutdown
        *)  exit $code ;;  # crash: propagate, don't spin
    esac
done
APPRUN
chmod +x "$APPDIR/AppRun"

# Top-level .desktop (required by appimagetool). AppImage launches AppRun
# regardless of Exec; the Exec value is a placeholder used by menu integration.
cat > "$APPDIR/nightsummary-companion.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=Night Summary Companion
Comment=Local dashboard mirroring your Night Summary imaging history
Exec=NightSummaryCompanion
Icon=nightsummary-companion
Categories=Utility;Network;
Terminal=false
DESKTOP

# Icon: appimagetool wants one matching the .desktop Icon= at the AppDir root,
# plus a .DirIcon thumbnail.
cp "$ICON" "$APPDIR/nightsummary-companion.png"
cp "$ICON" "$APPDIR/.DirIcon"

# Fetch appimagetool (cached in the build dir). It's itself an AppImage.
TOOL="$OUT/appimagetool-x86_64.AppImage"
if [ ! -f "$TOOL" ]; then
    echo "Downloading appimagetool..."
    curl -fL -o "$TOOL" \
        https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
    chmod +x "$TOOL"
fi

OUTFILE="$OUT/NightSummaryCompanion-x86_64.AppImage"
rm -f "$OUTFILE"
# ARCH explicit (no `file` cmd needed); --appimage-extract-and-run avoids FUSE.
echo "Packing AppImage..."
ARCH=x86_64 "$TOOL" --appimage-extract-and-run "$APPDIR" "$OUTFILE"
chmod +x "$OUTFILE"

SIZE=$(du -h "$OUTFILE" | cut -f1)
echo "-> $OUTFILE ($SIZE)"
echo "Done."
