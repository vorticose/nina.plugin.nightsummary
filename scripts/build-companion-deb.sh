#!/bin/bash
# Builds a Debian/Ubuntu .deb of the companion: double-click -> App Center ->
# Install (one sudo prompt) -> appears in the app menu with its icon, fully
# executable, no chmod. Declares libfontconfig1/libfreetype6 so apt pulls the
# SkiaSharp runtime deps automatically.
#
# Produces: build/companion-linux/nightsummary-companion_<ver>_amd64.deb
#
# Consumes the self-contained linux binary + watchdog staged by
# build-companion-linux.ps1 (run that first). Runs on Linux/WSL/CI (needs dpkg-deb,
# always present on Debian/Ubuntu).
#
# Usage:  bash scripts/build-companion-deb.sh
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
OUT="$ROOT/build/companion-linux"
STAGE="$OUT/staging-x64/NightSummaryCompanion"
BIN="$STAGE/NightSummaryCompanion-bin"
WATCHDOG="$STAGE/NightSummaryCompanion"
ICON="$ROOT/assets/companion-icon/companion-256.png"

[ -f "$BIN" ]      || { echo "ERROR: $BIN missing — run scripts/build-companion-linux.ps1 first."; exit 1; }
[ -f "$WATCHDOG" ] || { echo "ERROR: $WATCHDOG (watchdog) missing — run scripts/build-companion-linux.ps1 first."; exit 1; }
[ -f "$ICON" ]     || { echo "ERROR: $ICON missing — run scripts/gen-companion-icons.py first."; exit 1; }
command -v dpkg-deb >/dev/null 2>&1 || { echo "ERROR: dpkg-deb required (Debian/Ubuntu)."; exit 1; }

VER="$(grep -oPm1 '(?<=<VersionPrefix>)[^<]+' "$ROOT/NINA.Plugin.NightSummary/NINA.Plugin.NightSummary.csproj" || echo 0.0.0)"
ARCH="amd64"
PKG="nightsummary-companion"
DEB="$OUT/${PKG}_${VER}_${ARCH}.deb"

# Stage on the native Linux filesystem, NOT the output dir — when OUT is a
# Windows drive mount (WSL /mnt/c, DrvFs), every file reports mode 777 and chmod
# doesn't stick, which dpkg-deb rejects ("control directory has bad permissions").
# Build in a tmpdir (ext4 honors modes), then copy only the finished .deb to OUT.
BUILD="$(mktemp -d)"
trap 'rm -rf "$BUILD"' EXIT
ROOTFS="$BUILD/$PKG"
mkdir -p "$ROOTFS/DEBIAN" \
         "$ROOTFS/opt/$PKG" \
         "$ROOTFS/usr/bin" \
         "$ROOTFS/usr/share/applications" \
         "$ROOTFS/usr/share/icons/hicolor/256x256/apps"

# Payload: the self-contained binary + the watchdog, kept side-by-side and named
# exactly as the tarball so CompanionAutostart's launcher resolution (the script
# next to the -bin) and the exit-88/0 contract both work unchanged.
cp "$BIN"      "$ROOTFS/opt/$PKG/NightSummaryCompanion-bin"
cp "$WATCHDOG" "$ROOTFS/opt/$PKG/NightSummaryCompanion"
chmod 0755 "$ROOTFS/opt/$PKG/NightSummaryCompanion-bin" "$ROOTFS/opt/$PKG/NightSummaryCompanion"

# PATH entry -> the watchdog (also what the .desktop Exec uses).
ln -s "/opt/$PKG/NightSummaryCompanion" "$ROOTFS/usr/bin/$PKG"

# Icon (hicolor theme -> app menu picks it up).
cp "$ICON" "$ROOTFS/usr/share/icons/hicolor/256x256/apps/$PKG.png"

# Desktop entry.
cat > "$ROOTFS/usr/share/applications/$PKG.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Night Summary Companion
Comment=Local dashboard mirroring your Night Summary imaging history
Exec=$PKG serve
Icon=$PKG
Categories=Utility;Network;
Terminal=false
EOF

# Package metadata. Depends pulls the SkiaSharp runtime libs automatically.
INSTALLED_KB="$(du -sk "$ROOTFS" | cut -f1)"
cat > "$ROOTFS/DEBIAN/control" <<EOF
Package: $PKG
Version: $VER
Section: utils
Priority: optional
Architecture: $ARCH
Depends: libfontconfig1, libfreetype6
Installed-Size: $INSTALLED_KB
Maintainer: Evan Pegors @sleepypuppy15
Description: Night Summary Companion
 A local web dashboard that mirrors your Night Summary imaging history from the
 primary (NINA) machine. Runs a small local web server and opens the dashboard
 in your browser. Config and synced data live under ~/.local/share.
EOF

# Refresh desktop + icon caches after install/remove (best-effort).
cat > "$ROOTFS/DEBIAN/postinst" <<'EOF'
#!/bin/bash
set -e
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q || true
command -v gtk-update-icon-cache  >/dev/null 2>&1 && gtk-update-icon-cache -q -f /usr/share/icons/hicolor || true
exit 0
EOF
cat > "$ROOTFS/DEBIAN/postrm" <<'EOF'
#!/bin/bash
set -e
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q || true
exit 0
EOF
chmod 0755 "$ROOTFS/DEBIAN" "$ROOTFS/DEBIAN/postinst" "$ROOTFS/DEBIAN/postrm"

DEB_TMP="$BUILD/$(basename "$DEB")"
dpkg-deb --build --root-owner-group "$ROOTFS" "$DEB_TMP"
mkdir -p "$OUT"
rm -f "$DEB"
cp "$DEB_TMP" "$DEB"

SIZE="$(du -h "$DEB" | cut -f1)"
echo "-> $DEB ($SIZE)"
echo "Install:  sudo apt install ./$(basename "$DEB")   (or double-click in the file manager)"
