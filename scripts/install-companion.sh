#!/bin/sh
# Night Summary Companion - one-line Linux installer.
#
#   curl -fsSL https://github.com/vorticose/nina.plugin.nightsummary/releases/latest/download/install-companion.sh | sh
#
# User-scoped: installs under ~/.local (NO root), adds an app-menu entry + icon,
# and a `nightsummary-companion` launcher on PATH. Works on desktop or headless,
# any glibc x86_64 distro. POSIX sh (runs under dash when piped to `sh`).
#
# Test against a local build without a published release:
#   NSC_TARBALL=/path/to/NightSummaryCompanion-linux-x64.tar.gz sh scripts/install-companion.sh
# Or point at an arbitrary URL:
#   NSC_URL=http://host:8000/NightSummaryCompanion-linux-x64.tar.gz sh scripts/install-companion.sh
set -eu

REPO="vorticose/nina.plugin.nightsummary"
TARBALL="NightSummaryCompanion-linux-x64.tar.gz"
URL="${NSC_URL:-https://github.com/$REPO/releases/latest/download/$TARBALL}"

ARCH="$(uname -m 2>/dev/null || echo unknown)"
case "$ARCH" in
    x86_64|amd64) : ;;
    *) echo "Night Summary Companion ships x86_64 only (detected '$ARCH'). Build from source for your arch." >&2; exit 1 ;;
esac
command -v tar >/dev/null 2>&1 || { echo "tar is required." >&2; exit 1; }

PREFIX="$HOME/.local"
APPDIR="$PREFIX/share/nightsummary-companion"
BINDIR="$PREFIX/bin"
DESKTOPDIR="$PREFIX/share/applications"
ICONDIR="$PREFIX/share/icons/hicolor/256x256/apps"

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT INT TERM

if [ -n "${NSC_TARBALL:-}" ]; then
    echo "Using local tarball: $NSC_TARBALL"
    cp "$NSC_TARBALL" "$TMP/c.tar.gz"
else
    command -v curl >/dev/null 2>&1 || { echo "curl is required." >&2; exit 1; }
    echo "Downloading Night Summary Companion ..."
    curl -fSL "$URL" -o "$TMP/c.tar.gz"
fi

tar -xzf "$TMP/c.tar.gz" -C "$TMP"
SRC="$TMP/NightSummaryCompanion"
[ -f "$SRC/NightSummaryCompanion-bin" ] || { echo "unexpected archive layout (no NightSummaryCompanion-bin)." >&2; exit 1; }

echo "Installing to $APPDIR (no root needed) ..."
mkdir -p "$APPDIR" "$BINDIR" "$DESKTOPDIR" "$ICONDIR"
cp "$SRC/NightSummaryCompanion-bin" "$SRC/NightSummaryCompanion" "$APPDIR/"
chmod 0755 "$APPDIR/NightSummaryCompanion-bin" "$APPDIR/NightSummaryCompanion"

# PATH launcher -> the watchdog. The watchdog resolves its own real path via
# readlink -f, so launching through this symlink still finds the binary in APPDIR.
ln -sf "$APPDIR/NightSummaryCompanion" "$BINDIR/nightsummary-companion"

if [ -f "$SRC/companion.png" ]; then
    cp "$SRC/companion.png" "$ICONDIR/nightsummary-companion.png"
fi

cat > "$DESKTOPDIR/nightsummary-companion.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Night Summary Companion
Comment=Local dashboard mirroring your Night Summary imaging history
Exec=$BINDIR/nightsummary-companion serve
Icon=$ICONDIR/nightsummary-companion.png
Categories=Utility;Network;
Terminal=false
EOF
# Icon= is an absolute path (not a theme name) so the launcher icon shows without
# depending on the hicolor icon cache being rebuilt -- a user-scoped install can't
# assume the desktop will rescan. Still refresh the cache best-effort so anything
# that resolves the icon by name (e.g. taskbar/WM_CLASS) finds it too.
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q "$DESKTOPDIR" 2>/dev/null || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -f -t "$PREFIX/share/icons/hicolor" 2>/dev/null || true
fi

# Runtime libs SkiaSharp needs for report thumbnails. Can't auto-install without
# root, so just flag them.
MISSING=""
if command -v ldconfig >/dev/null 2>&1; then
    ldconfig -p 2>/dev/null | grep -q libfontconfig || MISSING="$MISSING libfontconfig1"
    ldconfig -p 2>/dev/null | grep -q libfreetype   || MISSING="$MISSING libfreetype6"
fi

echo ""
echo "Installed Night Summary Companion."
if [ -n "$MISSING" ]; then
    echo ""
    echo "NOTE: report thumbnails need:$MISSING"
    echo "      sudo apt-get install -y$MISSING   (or your distro's equivalent)"
fi
echo ""
echo "Start it:"
case ":$PATH:" in
    *":$BINDIR:"*) echo "  nightsummary-companion" ;;
    *) echo "  $BINDIR/nightsummary-companion     ($BINDIR is not on your PATH yet)" ;;
esac
echo "  ...or launch \"Night Summary Companion\" from your app menu."
echo ""
echo "It opens the setup wizard in your browser. Enable start-at-login from the"
echo "dashboard: Settings -> Start at login."
