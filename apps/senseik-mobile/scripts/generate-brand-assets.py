"""Generates the Sense HR native brand assets (splash + launcher icons) into assets/images.

The mark mirrors src/components/auth/BrandMark.tsx on its 40-unit box: a white back square
(x=12, y=4, 24x24, r=6) behind a brand-blue front square (x=4, y=12, 24x24, r=6) carrying a
white "↗" arrow. The arrow is drawn as strokes (not a font glyph) so the output is identical on
every machine.

Usage (Pillow required):  python scripts/generate-brand-assets.py
"""

from pathlib import Path

from PIL import Image, ImageDraw

NIGHT = (0x0E, 0x00, 0x33, 255)  # theme palette.night
BRAND = (0x00, 0x8C, 0xF0, 255)  # theme palette.brand
WHITE = (255, 255, 255, 255)
TRANSPARENT = (0, 0, 0, 0)

SIZE = 1024
SUPERSAMPLE = 4  # draw at 4x, downsample with LANCZOS for smooth edges

BACK_SQUARE = (12, 4, 36, 28)
FRONT_SQUARE = (4, 12, 28, 36)
SQUARE_RADIUS = 6
# "↗" inside the front square (centre 16,24): shaft plus a two-stroke head at the top-right.
ARROW_TIP = (20.5, 19.5)
ARROW_TAIL = (11.5, 28.5)
ARROW_HEAD_LENGTH = 6.0
ARROW_STROKE = 2.6
# Transparent ring around the front square in the monochrome icon, so the overlap stays readable.
MONO_GAP = 1.6

# Farthest point of the mark from the box centre (20,20): the rounded outer corners.
MARK_RADIUS_UNITS = ((10**2 + 10**2) ** 0.5) + SQUARE_RADIUS
# Android adaptive icons: 108dp canvas, content guaranteed visible inside a 66dp circle.
ADAPTIVE_SAFE_RADIUS_PX = SIZE * (66 / 108) / 2

OUTPUT_DIR = Path(__file__).resolve().parent.parent / "assets" / "images"


def _scaled(box, unit, offset):
    return tuple(offset + value * unit for value in box)


def _draw_arrow(draw, unit, offset, color):
    width = max(1, round(ARROW_STROKE * unit))
    tip = (offset + ARROW_TIP[0] * unit, offset + ARROW_TIP[1] * unit)
    tail = (offset + ARROW_TAIL[0] * unit, offset + ARROW_TAIL[1] * unit)
    head_left = (tip[0] - ARROW_HEAD_LENGTH * unit, tip[1])
    head_down = (tip[0], tip[1] + ARROW_HEAD_LENGTH * unit)
    draw.line([tail, tip], fill=color, width=width)
    draw.line([head_left, tip, head_down], fill=color, width=width, joint="curve")
    # Round caps: PIL lines have square ends, so dot every stroke end.
    radius = width / 2
    for x, y in (tail, head_left, head_down, tip):
        draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=color)


def _mark_layer(box_px, monochrome=False):
    """Transparent SIZE x SIZE layer with the 40-unit mark box scaled to box_px, centred."""
    canvas = SIZE * SUPERSAMPLE
    unit = box_px * SUPERSAMPLE / 40
    offset = (canvas - 40 * unit) / 2
    layer = Image.new("RGBA", (canvas, canvas), TRANSPARENT)
    draw = ImageDraw.Draw(layer)
    radius = SQUARE_RADIUS * unit

    draw.rounded_rectangle(_scaled(BACK_SQUARE, unit, offset), radius, fill=WHITE)
    front = _scaled(FRONT_SQUARE, unit, offset)
    if monochrome:
        gap = MONO_GAP * unit
        ring = (front[0] - gap, front[1] - gap, front[2] + gap, front[3] + gap)
        draw.rounded_rectangle(ring, radius + gap, fill=TRANSPARENT)
        draw.rounded_rectangle(front, radius, fill=WHITE)
        _draw_arrow(draw, unit, offset, TRANSPARENT)
    else:
        draw.rounded_rectangle(front, radius, fill=BRAND)
        _draw_arrow(draw, unit, offset, WHITE)

    return layer.resize((SIZE, SIZE), Image.LANCZOS)


def _on_background(layer, color):
    background = Image.new("RGBA", (SIZE, SIZE), color)
    background.alpha_composite(layer)
    return background


def main():
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    # Box size whose mark fits the adaptive-icon safe circle with ~8% breathing room.
    adaptive_box_px = 40 * (ADAPTIVE_SAFE_RADIUS_PX * 0.92) / MARK_RADIUS_UNITS

    outputs = {
        # Native splash (expo-splash-screen, backgroundColor #0E0033): the full 40-unit box.
        "splash-icon.png": _mark_layer(SIZE),
        "android-icon-foreground.png": _mark_layer(adaptive_box_px),
        "android-icon-background.png": Image.new("RGBA", (SIZE, SIZE), NIGHT),
        "android-icon-monochrome.png": _mark_layer(adaptive_box_px, monochrome=True),
        "icon.png": _on_background(_mark_layer(SIZE * 0.62), NIGHT),
    }
    for name, image in outputs.items():
        path = OUTPUT_DIR / name
        image.save(path, optimize=True)
        print(f"wrote {path.relative_to(OUTPUT_DIR.parent.parent)} {image.size}")


if __name__ == "__main__":
    main()
