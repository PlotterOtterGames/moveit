"""Sporte!'s textures as code (PIL): ground details, ad boards, nets, the big screen.

    python tools/sports/textures.py [--preview]

Writes into unity/MoveItGames/Assets/Games/Sports/Art/Textures:
    grass_detail.png, track_detail.png, sand_detail.png   tileable greys around white (multiplied with the ground's
                                                          vertex colours; 256 px = 4 m)
    ads.png        the ad boards' strip (uv.x = metres along a board, 1 px = 1 cm, wraps; the game scrolls it)
    net.png        a net's cords (alpha), tileable (one cell = 12 cm)
    screen.png     the big screen when idle: the logo on night blue with the brand's colours
"""

import math
import os
import random
import sys

from PIL import Image, ImageDraw, ImageFilter

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(REPO, "unity", "MoveItGames", "Assets", "Games", "Sports", "Art", "Textures")
LOGO = os.path.join(REPO, "brand", "logo", "tundu-logo-white.png")
MARK = os.path.join(REPO, "brand", "logo", "tundu-mark.png")

SUN, CORAL, SKY, GRAPE, LEAF, NIGHT = (255, 201, 60), (255, 90, 78), (49, 198, 244), (123, 97, 255), (111, 214, 106), (20, 25, 56)


def tileable_noise(size, scale, seed, octaves=3):
    """Smooth tileable value noise in 0..1 (bilinear over a wrapped lattice, a few octaves)."""
    rng = random.Random(seed)
    out = [[0.0] * size for _ in range(size)]
    amp, total = 1.0, 0.0
    for o in range(octaves):
        n = max(2, int(size / (scale / (2 ** o))))
        grid = [[rng.random() for _ in range(n)] for _ in range(n)]
        for y in range(size):
            gy = y * n / size
            y0 = int(gy) % n
            y1 = (y0 + 1) % n
            fy = gy - int(gy)
            fy = fy * fy * (3 - 2 * fy)
            for x in range(size):
                gx = x * n / size
                x0 = int(gx) % n
                x1 = (x0 + 1) % n
                fx = gx - int(gx)
                fx = fx * fx * (3 - 2 * fx)
                a = grid[y0][x0] + (grid[y0][x1] - grid[y0][x0]) * fx
                b = grid[y1][x0] + (grid[y1][x1] - grid[y1][x0]) * fx
                out[y][x] += (a + (b - a) * fy) * amp
        total += amp
        amp *= 0.5
    return [[v / total for v in row] for row in out]


def grey(values, lo, hi, path):
    size = len(values)
    img = Image.new("L", (size, size))
    img.putdata([int(255 * (lo + (hi - lo) * v)) for row in values for v in row])
    img.save(path)
    return img


def grass():
    size = 256
    base = tileable_noise(size, 64, 1, octaves=3)
    img = Image.new("L", (size, size))
    px = img.load()
    for y in range(size):
        for x in range(size):
            px[x, y] = int(255 * (0.86 + 0.12 * base[y][x]))
    # blades: short darker and lighter strokes, wrapped
    d = ImageDraw.Draw(img)
    rng = random.Random(2)
    for _ in range(5200):
        x, y = rng.randrange(size), rng.randrange(size)
        ln = rng.randint(2, 5)
        ang = rng.uniform(-0.6, 0.6) - math.pi / 2
        c = rng.choice((205, 214, 236, 248, 255))
        for ox in (-size, 0, size):
            for oy in (-size, 0, size):
                d.line((x + ox, y + oy, x + ox + math.cos(ang) * ln, y + oy + math.sin(ang) * ln), fill=c, width=1)
    img = img.filter(ImageFilter.SMOOTH)
    img.save(os.path.join(OUT, "grass_detail.png"))


def track():
    size = 256
    base = tileable_noise(size, 32, 3, octaves=2)
    rng = random.Random(4)
    img = Image.new("L", (size, size))
    px = img.load()
    for y in range(size):
        for x in range(size):
            v = 0.9 + 0.08 * base[y][x] + rng.uniform(-0.06, 0.06)
            px[x, y] = int(255 * max(0.0, min(1.0, v)))
    img.save(os.path.join(OUT, "track_detail.png"))


def sand():
    size = 256
    base = tileable_noise(size, 96, 5, octaves=3)
    rng = random.Random(6)
    img = Image.new("L", (size, size))
    px = img.load()
    for y in range(size):
        for x in range(size):
            ripple = 0.5 + 0.5 * math.sin((x * 2 * math.pi / size) * 6 + base[y][x] * 6)
            v = 0.88 + 0.06 * base[y][x] + 0.04 * ripple + rng.uniform(-0.035, 0.035)
            px[x, y] = int(255 * max(0.0, min(1.0, v)))
    img.save(os.path.join(OUT, "sand_detail.png"))


def logo(height, colour=None):
    im = Image.open(LOGO).convert("RGBA")
    w = int(im.width * height / im.height)
    im = im.resize((w, height), Image.LANCZOS)
    if colour:
        tint = Image.new("RGBA", im.size, colour + (255,))
        tint.putalpha(im.getchannel("A"))
        im = tint
    return im


def ads():
    """1 px = 1 cm: panels of 6 m (600 px) side by side, 2400 px wide, 100 px tall (1 m)."""
    w, h = 2400, 100
    img = Image.new("RGB", (w, h), NIGHT)
    d = ImageDraw.Draw(img)
    panels = [("logo", NIGHT, None), ("stripes", CORAL, SUN), ("logo", SKY, NIGHT), ("dots", SUN, CORAL)]
    for i, (kind, bg, fg) in enumerate(panels):
        x0 = i * 600
        d.rectangle((x0, 0, x0 + 600, h), fill=bg)
        if kind == "logo":
            lg = logo(70, fg)
            for k in range(2):
                img.paste(lg, (x0 + 60 + k * 300, 15), lg)
        elif kind == "stripes":
            panel = Image.new("RGB", (600, h), bg)
            pd = ImageDraw.Draw(panel)
            for k in range(-2, 16):
                xs = k * 46
                pd.polygon([(xs, h), (xs + 22, h), (xs + 22 + 50, 0), (xs + 50, 0)], fill=fg)
            pd.rounded_rectangle((150, 12, 450, 88), 30, fill=bg)
            lg = logo(60, NIGHT)
            panel.paste(lg, (300 - lg.width // 2, 20), lg)
            img.paste(panel, (x0, 0))
        elif kind == "dots":
            for k in range(12):
                for j in range(2):
                    cx, cy = x0 + 25 + k * 50, 25 + j * 50
                    d.ellipse((cx - 14, cy - 14, cx + 14, cy + 14), fill=fg)
            d.rounded_rectangle((x0 + 150, 12, x0 + 450, 88), 30, fill=NIGHT)
            lg = logo(60)
            img.paste(lg, (x0 + 300 - lg.width // 2, 20), lg)
    img.save(os.path.join(OUT, "ads.png"))


def net():
    """A square net cell, 64 px: knotted cords on the diagonals (alpha), white."""
    s = 64
    img = Image.new("RGBA", (s, s), (255, 255, 255, 0))
    d = ImageDraw.Draw(img)
    for off in (-s, 0, s):
        d.line((off, 0, off + s, s), fill=(255, 255, 255, 255), width=4)
        d.line((off + s, 0, off, s), fill=(255, 255, 255, 255), width=4)
    img = img.filter(ImageFilter.SMOOTH)
    img.save(os.path.join(OUT, "net.png"))


def screen():
    w, h = 1024, 465
    img = Image.new("RGB", (w, h), NIGHT)
    d = ImageDraw.Draw(img)
    for i, c in enumerate((CORAL, SUN, SKY, GRAPE, LEAF)):
        d.rectangle((i * w // 5, h - 26, (i + 1) * w // 5, h), fill=c)
    lg = logo(200)
    img.paste(lg, ((w - lg.width) // 2, (h - 26 - lg.height) // 2), lg)
    img.save(os.path.join(OUT, "screen.png"))


def main():
    os.makedirs(OUT, exist_ok=True)
    grass()
    track()
    sand()
    ads()
    net()
    screen()
    print("[textures] written to", OUT)


if __name__ == "__main__":
    main()
