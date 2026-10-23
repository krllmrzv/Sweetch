"""
Draws the Sweetch mark — three wheel slices in the Shift, Ctrl and Alt colours on an ink squircle —
into src/Sweetch.App/Assets/Sweetch.png (512 px) and Sweetch.ico (16–256 px).

    python scripts/build-logo.py      (needs Pillow)
"""
import math
from pathlib import Path

from PIL import Image, ImageDraw

ASSETS = Path(__file__).resolve().parent.parent / "src" / "Sweetch.App" / "Assets"
INK = (15, 15, 14, 255)
SLICES = [(255, 107, 61, 255), (169, 156, 255, 255), (228, 238, 90, 255)]  # Shift, Ctrl, Alt
SUPER = 8  # supersampling


def squircle(size: float, n: float = 5, steps: int = 400):
    h = size / 2
    pts = []
    for i in range(steps):
        t = i / steps * 2 * math.pi
        c, s = math.cos(t), math.sin(t)
        pts.append((h + h * math.copysign(abs(c) ** (2 / n), c), h + h * math.copysign(abs(s) ** (2 / n), s)))
    return pts


def sector(cx, cy, a0, a1, r_out, r_in, gap, corner, steps=120):
    """Ring segment shrunk by the corner radius; drawing it with a round 2×corner outline restores the size."""
    d = gap / 2 + corner
    ro, ri = r_out - corner, r_in + corner

    def off(r):
        return math.degrees(math.asin(d / r))

    def pt(r, a):
        t = math.radians(a)
        return cx + r * math.sin(t), cy - r * math.cos(t)

    os_, oe = a0 + off(ro), a1 - off(ro)
    is_, ie = a0 + off(ri), a1 - off(ri)
    outer = [pt(ro, os_ + (oe - os_) * k / steps) for k in range(steps + 1)]
    inner = [pt(ri, ie - (ie - is_) * k / steps) for k in range(steps + 1)]
    return outer + inner


def mark(px: int) -> Image.Image:
    s = px * SUPER
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    u = s / 100  # design units
    small = px <= 24
    # Tray and small sizes drop the ink square and let the wheel fill the frame, with fatter slices and
    # wider gaps so the three colours stay apart.
    if not small:
        draw.polygon(squircle(s), fill=INK)
    r_out, r_in, gap, corner = (50, 13, 11, 3) if small else (39, 15, 7, 3.5)
    for i, colour in enumerate(SLICES):
        pts = sector(50 * u, 50 * u, i * 120 - 60, i * 120 + 60, r_out * u, r_in * u, gap * u, corner * u)
        draw.polygon(pts, fill=colour)
        draw.line(pts + [pts[0]], fill=colour, width=round(2 * corner * u), joint="curve")
        rad = corner * u
        for x, y in pts[:: max(1, len(pts) // 60)] + [pts[0], pts[len(pts) // 2 - 1], pts[len(pts) // 2], pts[-1]]:
            draw.ellipse((x - rad, y - rad, x + rad, y + rad), fill=colour)
    return img.resize((px, px), Image.LANCZOS)


if __name__ == "__main__":
    mark(512).save(ASSETS / "Sweetch.png")
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = [mark(px) for px in sizes]
    frames[-1].save(ASSETS / "Sweetch.ico", sizes=[(p, p) for p in sizes], append_images=frames[:-1])
    print("Sweetch.png, Sweetch.ico")
