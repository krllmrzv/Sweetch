"""Builds the README artwork in docs/ from a self-test run.

    ./publish/Sweetch.exe --self-test ./self-test
    python scripts/build-readme-art.py ./self-test

hero-dark.png / hero-light.png: the three wheels (Shift, Ctrl, Alt) with the title, one per GitHub theme.
screen-*.png: settings screenshots with rounded corners and a soft shadow on transparent, so they sit well
on either theme.
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
FONTS = ROOT / 'src/Sweetch.App/Assets/Fonts'
DOCS = ROOT / 'docs'
SHIFT, CTRL, ALT = (255, 107, 61), (169, 156, 255), (228, 238, 90)


def font(weight, size):
    return ImageFont.truetype(str(FONTS / f'Onest-{weight}.ttf'), size)


def glow(canvas, center, radius, color, strength):
    """A soft coloured light behind a wheel."""
    layer = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
    x, y = center
    ImageDraw.Draw(layer).ellipse((x - radius, y - radius, x + radius, y + radius), fill=(*color, strength))
    canvas.alpha_composite(layer.filter(ImageFilter.GaussianBlur(radius * 0.45)))


def shadow(canvas, image, at, blur, opacity):
    alpha = image.getchannel('A').point(lambda a: a * opacity // 255)
    layer = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
    black = Image.new('RGBA', image.size, (0, 0, 0, 255))
    black.putalpha(alpha)
    layer.paste(black, (at[0], at[1] + blur // 3), black)
    canvas.alpha_composite(layer.filter(ImageFilter.GaussianBlur(blur)))


def hero(shots, theme):
    dark = theme == 'dark'
    width, height = 2000, 1240
    page = (11, 11, 10) if dark else (237, 236, 232)
    text, muted = ((243, 241, 235), (150, 148, 140)) if dark else ((15, 15, 14), (110, 108, 102))
    canvas = Image.new('RGBA', (width, height), (*page, 255))

    wheels = [  # (file, size, centre, glow colour); drawn back to front
        ('art-wheel-image-tools.png', 600, (470, 840), CTRL),
        ('art-wheel-actions.png', 600, (1530, 840), ALT),
        ('art-wheel-image-convert.png', 760, (1000, 800), SHIFT),
    ]
    for name, size, centre, color in wheels:
        glow(canvas, centre, int(size * 0.62), color, 70 if dark else 90)
    for name, size, centre, color in wheels:
        wheel = Image.open(shots / name).convert('RGBA').resize((size, size), Image.LANCZOS)
        at = (centre[0] - size // 2, centre[1] - size // 2)
        shadow(canvas, wheel, at, 40, 140 if dark else 70)
        canvas.alpha_composite(wheel, at)

    draw = ImageDraw.Draw(canvas)
    title = font(800, 132)
    draw.text((width // 2, 150), 'Sweetch 2.1', font=title, fill=text, anchor='mm')
    draw.text((width // 2, 250), 'Перетащите файл, нажмите клавишу — и отпустите над форматом', font=font(500, 40), fill=muted, anchor='mm')
    return canvas.convert('RGB')  # full size: sharp on high-DPI screens


def framed(path, ratio, radius=36, pad=60):
    """A screenshot as a floating window: cut to a window's proportions, rounded corners, a soft shadow."""
    shot = Image.open(path).convert('RGBA')
    shot = shot.crop((0, 0, shot.width, min(shot.height, int(shot.width * ratio))))
    mask = Image.new('L', shot.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, *shot.size), radius, fill=255)
    shot.putalpha(mask)
    canvas = Image.new('RGBA', (shot.width + 2 * pad, shot.height + 2 * pad), (0, 0, 0, 0))
    shadow(canvas, shot, (pad, pad), 28, 110)
    canvas.alpha_composite(shot, (pad, pad))
    return canvas


def main():
    shots = Path(sys.argv[1] if len(sys.argv) > 1 else ROOT / 'self-test')
    DOCS.mkdir(exist_ok=True)
    hero(shots / 'ru', 'dark').save(DOCS / 'hero-dark.png', optimize=True)
    hero(shots / 'ru-light', 'light').save(DOCS / 'hero-light.png', optimize=True)
    for source, target, ratio in [('ru/settings.png', 'screen-settings.png', 0.7), ('ru/settings-about.png', 'screen-about.png', 0.64),
                                  ('ru-light/settings-wheels.png', 'screen-wheels.png', 0.62), ('ru/settings-gestures.png', 'screen-gestures.png', 0.7)]:
        image = framed(shots / source, ratio)
        image.thumbnail((1400, 1400), Image.LANCZOS)
        image.save(DOCS / target, optimize=True)
    print('README art written to', DOCS)


if __name__ == '__main__':
    main()
