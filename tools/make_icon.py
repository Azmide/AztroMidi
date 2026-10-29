"""Generates src/AztroMidi/Assets/icon.ico (blue rounded square with piano keys)."""
import os
import sys

from PIL import Image, ImageDraw

SIZE = 256
OUT = os.path.join(os.path.dirname(__file__), "..", "src", "AztroMidi", "Assets", "icon.ico")


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def render():
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))

    # Diagonal brand gradient inside a rounded square.
    grad = Image.new("RGBA", (SIZE, SIZE))
    top, bottom = (0x4F, 0xC8, 0xF2), (0x0A, 0x80, 0xAD)
    px = grad.load()
    for y in range(SIZE):
        for x in range(SIZE):
            px[x, y] = lerp(top, bottom, (x + y) / (2 * SIZE)) + (255,)
    mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).rounded_rectangle((8, 8, SIZE - 8, SIZE - 8), radius=58, fill=255)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)
    # Three white keys with two black keys: a tiny piano.
    left, right, top_y, bot_y = 52, 204, 62, 196
    kw = (right - left) / 3
    for i in range(3):
        x0 = left + i * kw
        d.rounded_rectangle((x0 + 4, top_y, x0 + kw - 4, bot_y), radius=14, fill=(255, 255, 255, 255))
    bw = kw * 0.55
    for i in (1, 2):
        cx = left + i * kw
        d.rounded_rectangle((cx - bw / 2, top_y, cx + bw / 2, top_y + 80), radius=10, fill=(0x14, 0x17, 0x1A, 255))
    return img


def main():
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    render().save(OUT, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print("wrote", os.path.abspath(OUT))


if __name__ == "__main__":
    sys.exit(main())
