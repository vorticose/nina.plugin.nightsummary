#!/usr/bin/env python3
"""Generate the companion launcher app icons from the brand art.

Source : assets/plugin-icon.png  (the Night Summary report-over-starfield mark)
Outputs: assets/companion-icon/companion.ico   (Windows, multi-res 16..256)
         assets/companion-icon/companion.icns  (macOS .app bundle icon)
         assets/companion-icon/companion-256.png (Linux .desktop icon)

Run from the repo root:  python scripts/gen-companion-icons.py
Requires Pillow (PIL). Re-run whenever the brand art changes, then commit the
regenerated icons so the build scripts can pick them up without Pillow installed.
"""
import os
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SRC = os.path.join(ROOT, "assets", "plugin-icon.png")
OUTDIR = os.path.join(ROOT, "assets", "companion-icon")


def square(img):
    """Pad to a transparent square so non-square art is not stretched."""
    w, h = img.size
    if w == h:
        return img
    s = max(w, h)
    base = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    base.paste(img, ((s - w) // 2, (s - h) // 2))
    return base


def main():
    os.makedirs(OUTDIR, exist_ok=True)
    src = square(Image.open(SRC).convert("RGBA"))

    # A large master so every downscale is a clean LANCZOS reduction.
    master = src.resize((1024, 1024), Image.LANCZOS)

    ico = os.path.join(OUTDIR, "companion.ico")
    master.save(ico, sizes=[(16, 16), (32, 32), (48, 48),
                            (64, 64), (128, 128), (256, 256)])

    icns = os.path.join(OUTDIR, "companion.icns")
    master.save(icns)

    png = os.path.join(OUTDIR, "companion-256.png")
    src.resize((256, 256), Image.LANCZOS).save(png)

    for f in (ico, icns, png):
        print(f"  wrote {os.path.relpath(f, ROOT)} ({os.path.getsize(f)} bytes)")


if __name__ == "__main__":
    main()
