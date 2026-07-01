#!/usr/bin/env python3
"""
Generate the app icon (multi-size Alarm.ico) from a single high-res source PNG.

Takes src/Alarm.Presentation/Assets/AppIcon.png (a 1024x1024 dark alarm-clock line
glyph on transparent), pads it, composites it onto a rounded-square light background
(so the dark glyph stays visible on dark taskbars), and writes a multi-resolution
src/Alarm.Presentation/Assets/Alarm.ico used by both <ApplicationIcon> (the embedded
Alarm.exe icon) and AppWindow.SetIcon (the title-bar/taskbar icon at runtime).

The .ico is committed (not generated at build time) because <ApplicationIcon> is read
early in the build. Re-run this after changing AppIcon.png or the constants below:
  mise exec -- uv run --with Pillow python tools/make-icon.py
  # or: just icon
"""
from __future__ import annotations

import sys
from pathlib import Path

# Force UTF-8 stdout. CI runners (windows-latest) default to cp1252 which
# can't encode the "→" / "…" we use in progress messages below.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "Alarm.Presentation" / "Assets"
SRC = ASSETS / "AppIcon.png"
OUT = ASSETS / "Alarm.ico"

# ── Tunables (tweak the look here, then re-run) ────────────────────────────────
CANVAS = 1024            # working resolution before downscaling to icon sizes
PADDING_RATIO = 0.14     # empty margin on each side (glyph fills ~72% of the canvas)
CORNER_RATIO = 0.18      # rounded-square corner radius as a fraction of CANVAS
BACKGROUND = (255, 255, 255, 255)  # rounded-square fill (RGBA); light for dark taskbars
ICON_SIZES = [16, 24, 32, 48, 64, 128, 256]  # frames stored in the .ico


def build() -> None:
    if not SRC.is_file():
        sys.exit(f"missing source icon: {SRC}")

    src = Image.open(SRC).convert("RGBA")

    # Tight-crop to the glyph's actual (non-transparent) bounds so padding is measured
    # from the artwork, not the source canvas's own whitespace.
    bbox = src.getbbox()
    glyph = src.crop(bbox) if bbox else src

    # Scale the glyph to fit inside the padded area, preserving aspect ratio.
    inner = round(CANVAS * (1 - 2 * PADDING_RATIO))
    scale = min(inner / glyph.width, inner / glyph.height)
    glyph = glyph.resize(
        (max(1, round(glyph.width * scale)), max(1, round(glyph.height * scale))),
        Image.Resampling.LANCZOS,
    )

    # Rounded-square background.
    mask = Image.new("L", (CANVAS, CANVAS), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, CANVAS - 1, CANVAS - 1], radius=round(CANVAS * CORNER_RATIO), fill=255
    )
    canvas = Image.composite(
        Image.new("RGBA", (CANVAS, CANVAS), BACKGROUND),
        Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0)),
        mask,
    )

    # Paste the glyph centred.
    canvas.alpha_composite(
        glyph, ((CANVAS - glyph.width) // 2, (CANVAS - glyph.height) // 2)
    )

    # Write the multi-resolution .ico. Pillow downscales the base to each size.
    OUT.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(OUT, format="ICO", sizes=[(s, s) for s in ICON_SIZES])
    print(
        f"  {SRC.name} → {OUT.name}  "
        f"{OUT.stat().st_size:,} bytes, sizes {ICON_SIZES}"
    )


if __name__ == "__main__":
    print("Building app icon…")
    build()
