#!/usr/bin/env python3
"""
Subset Geist + Geist Mono variable fonts down to the glyphs the app actually uses.

Invoked as a pre-build MSBuild step. Output lives in
`src/Alarm.Presentation/obj/SubsetFonts/` and is wired into the project via
<Content Include="..." Link="Assets/Fonts/Geist.ttf" /> so XAML's
`ms-appx:///Assets/Fonts/Geist.ttf#Geist` keeps working unchanged.

Run manually with:
  mise exec -- uv run --with fonttools python tools/subset-fonts.py
"""
from __future__ import annotations

import sys
from pathlib import Path

# Force UTF-8 stdout. CI runners (windows-latest) default to cp1252 which
# can't encode the "→" / "…" we use in progress messages below.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from fontTools.subset import main as subset_main

ROOT = Path(__file__).resolve().parent.parent
FONTS_IN = ROOT / "src" / "Alarm.Presentation" / "Assets" / "Fonts"
OUT_DIR = ROOT / "src" / "Alarm.Presentation" / "obj" / "SubsetFonts"

# Geist (body text): full printable ASCII covers every UI label *and*
# user-typed sound file names. ~95 glyphs.
GEIST_CHARS = "".join(chr(c) for c in range(0x20, 0x7F))

# Geist Mono is used for the clock-style 07:30 / 6:45:23 / 00:09 strings
# plus the literal "CANCEL" label. Keep the alphabet for that and digits/colon.
GEIST_MONO_CHARS = "0123456789: CANCEL"


def subset(src: Path, dst: Path, text: str) -> None:
    if not src.is_file():
        sys.exit(f"missing source font: {src}")
    dst.parent.mkdir(parents=True, exist_ok=True)
    subset_main([
        str(src),
        f"--text={text}",
        f"--output-file={dst}",
        "--layout-features=*",
        "--no-hinting",
        "--desubroutinize",
    ])
    print(
        f"  {src.name:14s} {src.stat().st_size:>7,} → "
        f"{dst.stat().st_size:>6,} bytes "
        f"({dst.stat().st_size / src.stat().st_size * 100:.0f}%)"
    )


def main() -> None:
    print("Subsetting fonts…")
    subset(FONTS_IN / "Geist.ttf", OUT_DIR / "Geist.ttf", GEIST_CHARS)
    subset(FONTS_IN / "GeistMono.ttf", OUT_DIR / "GeistMono.ttf", GEIST_MONO_CHARS)


if __name__ == "__main__":
    main()
