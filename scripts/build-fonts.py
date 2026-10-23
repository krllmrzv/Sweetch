"""
Rebuilds the fonts in src/Sweetch.App/Assets/Fonts.

  * Onest (OFL) — the UI face, static weights 400–800.
  * Sweetch Icons — Material Symbols Rounded (Apache 2.0), filled, weight 700, cut down to the
    code points listed in src/Sweetch.Core/Glyphs.cs. WPF can't set the axes of a variable font,
    so Google Fonts is asked for a static instance.

Run after adding an icon to Glyphs.cs:  python scripts/build-fonts.py   (needs fonttools)
"""
import re
import sys
import tempfile
import urllib.request
from pathlib import Path

from fontTools import subset
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "src" / "Sweetch.App" / "Assets" / "Fonts"
GLYPHS = ROOT / "src" / "Sweetch.Core" / "Glyphs.cs"
CSS = "https://fonts.googleapis.com/css2?family="


def fetch_ttf(family: str, target: Path) -> None:
    # A client that doesn't announce WOFF2 support gets plain TrueType.
    request = urllib.request.Request(CSS + family, headers={"User-Agent": "curl/8"})
    css = urllib.request.urlopen(request).read().decode()
    url = re.search(r"url\((https://fonts\.gstatic\.com/[^)]+\.ttf)\)", css)
    if not url:
        sys.exit(f"No TrueType file for {family}")
    target.write_bytes(urllib.request.urlopen(url.group(1)).read())


def build_icons(work: Path) -> None:
    source = work / "symbols.ttf"
    fetch_ttf("Material+Symbols+Rounded:opsz,wght,FILL,GRAD@24,700,1,0", source)
    text = GLYPHS.read_text(encoding="utf-8")
    codes = sorted({int(c, 16) for c in re.findall(r"\\u([0-9A-Fa-f]{4})", text)} | {ord(c) for c in re.findall("[\uE000-\uF8FF]", text)})
    font = TTFont(source)
    options = subset.Options()
    options.layout_features = []  # code points only, no ligatures
    options.name_IDs = ["*"]
    subsetter = subset.Subsetter(options)
    subsetter.populate(unicodes=codes)
    subsetter.subset(font)

    # One plain "Regular" face, so WPF never tries to embolden it.
    name = font["name"]
    for record in list(name.names):
        if record.nameID in (16, 17, 21, 22):
            name.removeNames(nameID=record.nameID)
    for nid, value in {1: "Sweetch Icons", 2: "Regular", 4: "Sweetch Icons", 6: "SweetchIcons-Regular"}.items():
        name.setName(value, nid, 3, 1, 0x409)
        name.setName(value, nid, 1, 0, 0)
    font["OS/2"].usWeightClass = 400
    font["OS/2"].fsSelection = (font["OS/2"].fsSelection & ~0b100001) | 0b1000000
    font["head"].macStyle = 0
    font.save(OUT / "SweetchIcons.ttf")
    print(f"SweetchIcons.ttf: {len(codes)} icons")


def build_text() -> None:
    for weight in (400, 500, 600, 700, 800):
        fetch_ttf(f"Onest:wght@{weight}", OUT / f"Onest-{weight}.ttf")
    print("Onest 400–800")


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        build_icons(Path(tmp))
    build_text()
