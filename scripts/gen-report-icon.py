#!/usr/bin/env python3
"""Generate the small report/dashboard header icon from the brand art.

Source : assets/plugin-icon.png       (776x776 RGBA master, ~600 KB)
Output : assets/report-icon.png       (144x144 palette PNG, ~7 KB)

The report header embeds this icon as a base64 data URI, so its byte size is
paid on every generated report -- and again in the base64 MIME body of every
emailed report. The master art is ~600 KB, which made the icon ~90% of a whole
report and left the email far more exposed to gateway rewriting/truncation.

144px is 3x the 48px render size, so it stays crisp on 3x HiDPI displays.
The master's alpha channel is fully opaque, so RGB (no alpha) loses nothing.

Run from the repo root:  python scripts/gen-report-icon.py
Requires Pillow (PIL). Commit the regenerated PNG -- the build embeds the
committed asset and never runs this script.
"""
import os
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SRC = os.path.join(ROOT, "assets", "plugin-icon.png")
DST = os.path.join(ROOT, "assets", "report-icon.png")

# 3x the 48px render size in both the report header and the dashboard header.
SIZE = 144


def main():
    src = Image.open(SRC)
    if src.mode == "RGBA" and src.getchannel("A").getextrema() != (255, 255):
        raise SystemExit("source has real transparency; drop the RGB conversion below")

    small = src.convert("RGB").resize((SIZE, SIZE), Image.LANCZOS)
    # Octree to a 256-colour palette, no dithering. On this starfield-over-report
    # art that measures RMSE 1.6 against the full-colour reference at the 48px
    # render size -- visually indistinguishable, and still clean at 4x zoom --
    # while being ~4x smaller than an optimized full-colour PNG of the same
    # dimensions. Median-cut and Floyd-Steinberg dithering both compress far
    # worse here (~18 KB) for strictly worse fidelity; measured, not assumed.
    # pngquant/libimagequant would reach ~7 KB, but Pillow alone keeps this
    # script dependency-identical to gen-companion-icons.py.
    small.quantize(colors=256, method=Image.FASTOCTREE,
                   dither=Image.Dither.NONE).save(DST, optimize=True)

    print(f"  wrote {os.path.relpath(DST, ROOT)} "
          f"({os.path.getsize(DST)} bytes, was {os.path.getsize(SRC)})")


if __name__ == "__main__":
    main()
