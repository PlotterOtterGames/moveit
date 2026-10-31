"""Fluturo!'s city textures as code: 16 tiles in one 1024x1024 PNG that Unity imports as a 2D texture array
(Assets/Editor/FlyArtImport.cs: 4x4 flipbook, slice 0 top-left, row by row). Each tile repeats on its own
(walls one floor x one bay, roads per 6 m, ...), so one material and one draw call cover a whole city block.

Alpha says what the city shader (Tundu/City) does with a pixel:
    255  wall: multiplied by the building's colour (vertex colour), so one facade serves every colour
    128  fixed colour: window frames, doors, road paint
      0  glass / water: reflects the sky (fresnel) and the sun
Draws at 4x and downsamples, so edges are smooth at any distance.

    motion-service-py/.venv/Scripts/python tools/fly/city_textures.py [--preview]

Slices (keep in step with FlyWorld.Slice):
 0 plain        1 apartment   2 office      3 classic
 4 balcony      5 shop        6 cafe        7 brick
 8 roof         9 road       10 crossroads 11 pavement
12 grass       13 cobbles    14 water      15 leaves
"""
import argparse
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(REPO, "unity", "MoveItGames", "Assets", "Games", "Fly", "Art", "fly_city.png")
PREVIEW = os.path.join(HERE, "out", "fly_city_preview.png")

TILE = 256
SS = 4                      # supersampling
S = TILE * SS               # drawing size of one tile
WALL, FIXED, GLASS = 255, 128, 0


def noise(size, cells, seed, octaves=3):
    """Smooth value noise in -1..1, tileable (wraps at the edges)."""
    rng = np.random.default_rng(seed)
    out = np.zeros((size, size), np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        n = cells * (2 ** o)
        grid = rng.uniform(-1, 1, (n, n)).astype(np.float32)
        grid = np.concatenate([grid, grid[:1]], 0)
        grid = np.concatenate([grid, grid[:, :1]], 1)
        img = Image.fromarray(((grid + 1) * 127.5).astype(np.uint8)).resize((size + size // n, size + size // n),
                                                                           Image.BICUBIC)
        layer = np.asarray(img, np.float32)[:size, :size] / 127.5 - 1
        out += layer * amp
        total += amp
        amp *= 0.5
    return out / total


class Tile:
    """One tile drawn at 4x: rgb + alpha (the material) layers."""

    def __init__(self, base, alpha=WALL):
        self.rgb = Image.new("RGB", (S, S), base)
        self.a = Image.new("L", (S, S), alpha)
        self.d = ImageDraw.Draw(self.rgb)
        self.da = ImageDraw.Draw(self.a)

    def rect(self, x0, y0, x1, y1, color, alpha):
        """Fractions of the tile (0..1, y down)."""
        box = [round(x0 * S), round(y0 * S), round(x1 * S) - 1, round(y1 * S) - 1]
        if box[2] < box[0] or box[3] < box[1]:
            return
        self.d.rectangle(box, fill=color)
        self.da.rectangle(box, fill=alpha)

    def ellipse(self, x0, y0, x1, y1, color, alpha):
        box = [round(x0 * S), round(y0 * S), round(x1 * S) - 1, round(y1 * S) - 1]
        self.d.ellipse(box, fill=color)
        self.da.ellipse(box, fill=alpha)

    def polygon(self, pts, color, alpha):
        p = [(round(x * S), round(y * S)) for x, y in pts]
        self.d.polygon(p, fill=color)
        self.da.polygon(p, fill=alpha)

    def gradient(self, x0, y0, x1, y1, top, bottom, alpha):
        """A vertical gradient (glass: darker at the top, lighter below, like the sky in it)."""
        h = max(1, round((y1 - y0) * S))
        w = max(1, round((x1 - x0) * S))
        t = np.linspace(0, 1, h)[:, None, None]
        g = (np.array(top, np.float32) * (1 - t) + np.array(bottom, np.float32) * t)
        g = np.repeat(g, w, 1).astype(np.uint8)
        self.rgb.paste(Image.fromarray(g, "RGB"), (round(x0 * S), round(y0 * S)))
        self.da.rectangle([round(x0 * S), round(y0 * S), round(x0 * S) + w - 1, round(y0 * S) + h - 1], fill=alpha)

    def grain(self, amount, seed, cells=8, mask_alpha=None):
        """Adds noise to the colour (where the alpha equals mask_alpha, or everywhere)."""
        n = noise(S, cells, seed)
        arr = np.asarray(self.rgb, np.float32)
        delta = n[..., None] * amount
        if mask_alpha is not None:
            delta *= (np.asarray(self.a) == mask_alpha)[..., None]
        self.rgb = Image.fromarray(np.clip(arr + delta, 0, 255).astype(np.uint8))
        self.d = ImageDraw.Draw(self.rgb)

    def image(self):
        # colour and alpha downsampled apart: resizing RGBA premultiplies, which would blacken the glass (alpha 0)
        rgb = self.rgb.resize((TILE, TILE), Image.LANCZOS)
        a = self.a.resize((TILE, TILE), Image.LANCZOS)
        return Image.merge("RGBA", (*rgb.split(), a))


# ---------------------------------------------------------------------------------------------------- facades

WALL_RGB = (238, 233, 224)
FRAME = (246, 246, 244)
SILL = (222, 220, 214)
GLASS_TOP, GLASS_BOTTOM = (58, 92, 132), (122, 170, 204)


def floor_line(t):
    """The floor band at the bottom of every storey: a shadowed ledge."""
    t.rect(0, 0.93, 1, 1, (214, 208, 198), WALL)
    t.rect(0, 0.915, 1, 0.935, (250, 248, 244), WALL)


def window(t, x0, y0, x1, y1, frame=0.035, panes=2, transom=True):
    t.rect(x0, y0, x1, y1, FRAME, FIXED)
    t.gradient(x0 + frame, y0 + frame, x1 - frame, y1 - frame, GLASS_TOP, GLASS_BOTTOM, GLASS)
    # a soft diagonal highlight, as if the sky were reflected
    t.polygon([(x0 + frame, y0 + (y1 - y0) * 0.55), (x0 + (x1 - x0) * 0.45, y0 + frame),
               (x0 + (x1 - x0) * 0.6, y0 + frame), (x0 + frame, y0 + (y1 - y0) * 0.75)], (150, 190, 218), GLASS)
    bar = frame * 0.6
    for k in range(1, panes):
        x = x0 + (x1 - x0) * k / panes
        t.rect(x - bar / 2, y0, x + bar / 2, y1, FRAME, FIXED)
    if transom:
        y = y0 + (y1 - y0) * 0.3
        t.rect(x0, y - bar / 2, x1, y + bar / 2, FRAME, FIXED)


def apartment():
    t = Tile(WALL_RGB)
    t.grain(7, 11, mask_alpha=WALL)
    floor_line(t)
    # shutters (fixed teal), the window, its sill
    t.rect(0.18, 0.2, 0.3, 0.74, (74, 128, 132), FIXED)
    t.rect(0.7, 0.2, 0.82, 0.74, (74, 128, 132), FIXED)
    for k in range(6):
        y = 0.24 + k * 0.08
        t.rect(0.19, y, 0.29, y + 0.012, (60, 108, 112), FIXED)
        t.rect(0.71, y, 0.81, y + 0.012, (60, 108, 112), FIXED)
    window(t, 0.3, 0.2, 0.7, 0.74)
    t.rect(0.27, 0.74, 0.73, 0.78, SILL, FIXED)
    t.rect(0.27, 0.78, 0.73, 0.795, (190, 186, 178), FIXED)
    return t.image()


def office():
    t = Tile((232, 233, 236))
    t.grain(4, 12, mask_alpha=WALL)
    # glass from the ceiling down to the spandrel; mullions split each bay in two
    t.gradient(0, 0, 1, 0.72, (52, 86, 128), (132, 182, 214), GLASS)
    t.polygon([(0, 0.5), (0.5, 0), (0.62, 0), (0, 0.66)], (150, 196, 224), GLASS)
    t.polygon([(0.55, 0.72), (1, 0.28), (1, 0.36), (0.66, 0.72)], (142, 188, 220), GLASS)
    for x in (0.0, 0.5, 1.0):
        t.rect(x - 0.018, 0, x + 0.018, 0.74, (96, 106, 122), FIXED)
    t.rect(0, 0.72, 1, 0.76, (96, 106, 122), FIXED)
    t.rect(0, 0.76, 1, 0.97, (232, 233, 236), WALL)       # spandrel panel (the building's colour)
    t.rect(0, 0.97, 1, 1, (96, 106, 122), FIXED)
    return t.image()


def classic():
    t = Tile((240, 232, 216))
    t.grain(8, 13, mask_alpha=WALL)
    for k in range(1, 4):                                  # rusticated joints
        t.rect(0, k * 0.23, 1, k * 0.23 + 0.008, (214, 204, 188), WALL)
    # an arched window with a keystone
    x0, x1, y0, y1 = 0.31, 0.69, 0.16, 0.8
    r = (x1 - x0) / 2
    t.ellipse(x0, y0, x1, y0 + 2 * r, FRAME, FIXED)
    t.rect(x0, y0 + r, x1, y1, FRAME, FIXED)
    f = 0.035
    t.ellipse(x0 + f, y0 + f, x1 - f, y0 + 2 * r - f, GLASS_TOP, GLASS)
    t.gradient(x0 + f, y0 + r, x1 - f, y1 - f, (70, 108, 146), GLASS_BOTTOM, GLASS)
    t.rect(0.5 - 0.01, y0, 0.5 + 0.01, y1, FRAME, FIXED)
    t.rect(x0, y0 + r, x1, y0 + r + 0.02, FRAME, FIXED)
    t.polygon([(0.46, y0 - 0.02), (0.54, y0 - 0.02), (0.525, y0 + 0.06), (0.475, y0 + 0.06)], (250, 246, 236), WALL)
    t.rect(0.27, y1, 0.73, y1 + 0.04, SILL, FIXED)
    t.rect(0, 0.0, 1, 0.05, (252, 248, 240), WALL)         # cornice
    t.rect(0, 0.05, 1, 0.065, (206, 196, 180), WALL)
    floor_line(t)
    return t.image()


def balcony():
    t = Tile(WALL_RGB)
    t.grain(7, 14, mask_alpha=WALL)
    floor_line(t)
    window(t, 0.28, 0.14, 0.72, 0.86, panes=2, transom=False)      # a glass door
    # the balcony: a slab and a white railing in front of the door
    t.rect(0.14, 0.82, 0.86, 0.88, (226, 224, 220), FIXED)
    t.rect(0.14, 0.88, 0.86, 0.9, (176, 174, 170), FIXED)
    t.rect(0.14, 0.56, 0.86, 0.585, FRAME, FIXED)
    for k in range(13):
        x = 0.15 + k * 0.0575
        t.rect(x, 0.585, x + 0.014, 0.82, FRAME, FIXED)
    # flower pots on the rail
    for x in (0.2, 0.74):
        t.rect(x, 0.5, x + 0.07, 0.56, (196, 110, 80), FIXED)
        t.ellipse(x - 0.015, 0.44, x + 0.085, 0.53, (96, 170, 90), FIXED)
        t.ellipse(x + 0.01, 0.45, x + 0.04, 0.48, (255, 120, 120), FIXED)
    return t.image()


def awning(t, y0, y1, stripes=8):
    for k in range(stripes):
        x0, x1 = k / stripes, (k + 1) / stripes
        t.polygon([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], (250, 250, 250), WALL if k % 2 == 0 else FIXED)
    # scalloped edge
    for k in range(stripes):
        x0, x1 = k / stripes, (k + 1) / stripes
        t.ellipse(x0, y1 - 0.04, x1, y1 + 0.04, (250, 250, 250), WALL if k % 2 == 0 else FIXED)
    t.rect(0, y1 + 0.04, 1, y1 + 0.07, (150, 146, 140), WALL)    # its shadow on the wall


def shop():
    t = Tile(WALL_RGB)
    t.grain(6, 15, mask_alpha=WALL)
    t.rect(0, 0.0, 1, 0.1, (240, 236, 230), WALL)                  # sign band
    awning(t, 0.1, 0.3)
    # display window with goods, a door with a glass panel
    t.rect(0.05, 0.4, 0.7, 0.95, FRAME, FIXED)
    t.gradient(0.08, 0.43, 0.67, 0.92, (66, 100, 140), (140, 186, 214), GLASS)
    for k, c in enumerate([(255, 196, 70), (255, 110, 96), (120, 200, 250), (140, 120, 255)]):
        x = 0.12 + k * 0.13
        t.rect(x, 0.74, x + 0.09, 0.91, c, FIXED)
    t.rect(0.08, 0.91, 0.67, 0.92, (200, 200, 200), FIXED)
    t.rect(0.76, 0.42, 0.95, 1.0, (126, 86, 58), FIXED)
    t.gradient(0.79, 0.46, 0.92, 0.7, GLASS_TOP, GLASS_BOTTOM, GLASS)
    t.ellipse(0.785, 0.76, 0.81, 0.785, (232, 200, 90), FIXED)
    t.rect(0, 0.97, 1, 1, (170, 166, 160), WALL)                   # plinth
    return t.image()


def cafe():
    t = Tile(WALL_RGB)
    t.grain(6, 16, mask_alpha=WALL)
    # a dark sign board with a little coffee cup, lamps, a door between two windows
    t.rect(0.12, 0.08, 0.88, 0.24, (54, 58, 74), FIXED)
    t.ellipse(0.44, 0.11, 0.56, 0.21, (250, 236, 210), FIXED)
    t.rect(0.47, 0.13, 0.53, 0.14, (54, 58, 74), FIXED)
    for x in (0.08, 0.86):
        t.rect(x, 0.12, x + 0.06, 0.15, (60, 60, 64), FIXED)
        t.ellipse(x - 0.005, 0.15, x + 0.065, 0.21, (255, 214, 120), FIXED)
    for x0 in (0.06, 0.68):
        window(t, x0, 0.36, x0 + 0.26, 0.86, panes=2, transom=False)
        t.rect(x0 - 0.02, 0.86, x0 + 0.28, 0.9, SILL, FIXED)
    t.rect(0.38, 0.32, 0.62, 1.0, FRAME, FIXED)
    t.gradient(0.41, 0.35, 0.59, 0.97, GLASS_TOP, GLASS_BOTTOM, GLASS)
    t.rect(0.495, 0.35, 0.505, 0.97, FRAME, FIXED)
    t.rect(0, 0.97, 1, 1, (170, 166, 160), WALL)
    return t.image()


def brick():
    t = Tile((224, 220, 212), FIXED)       # the mortar: light, not tinted
    rng = np.random.default_rng(17)
    rows, cols = 12, 4
    for r in range(rows):
        y0 = r / rows
        offset = 0.5 / cols if r % 2 else 0.0
        for c in range(-1, cols + 1):
            x0 = c / cols + offset
            shade = int(rng.integers(-14, 10))
            col = tuple(int(np.clip(v + shade, 0, 255)) for v in (236, 228, 220))
            t.rect(max(0, x0 + 0.006), y0 + 0.008, min(1, x0 + 1 / cols - 0.006), y0 + 1 / rows - 0.008, col, WALL)
    window(t, 0.34, 0.24, 0.66, 0.72, panes=2, transom=False)
    t.rect(0.31, 0.72, 0.69, 0.76, SILL, FIXED)
    return t.image()


# ---------------------------------------------------------------------------------------------------- ground

def roof():
    t = Tile((206, 202, 194))
    t.grain(26, 18, cells=32)
    arr = np.asarray(t.rgb, np.float32)
    speck = np.random.default_rng(19).random((S, S)) > 0.985
    arr[speck] *= 0.8
    t.rgb = Image.fromarray(arr.astype(np.uint8))
    return t.image()


def asphalt(seed):
    t = Tile((66, 70, 80), FIXED)
    t.grain(9, seed, cells=24)
    arr = np.asarray(t.rgb, np.float32)
    speck = np.random.default_rng(seed).random((S, S))
    arr[speck > 0.99] += 22
    arr[speck < 0.008] -= 14
    t.rgb = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))
    t.d = ImageDraw.Draw(t.rgb)
    return t


def road():
    """One road tile: 9 m across (u), 6 m along (v): edge lines, a dashed centre line, lighter wheel tracks."""
    t = asphalt(20)
    for x0 in (0.16, 0.34, 0.66, 0.84):
        t.rect(x0 - 0.05, 0, x0 + 0.05, 1, (72, 76, 86), FIXED)
    paint = (236, 236, 230)
    t.rect(0.035, 0, 0.06, 1, paint, FIXED)
    t.rect(0.94, 0, 0.965, 1, paint, FIXED)
    t.rect(0.488, 0.0, 0.512, 0.5, paint, FIXED)
    return t.image()


def crossroads():
    """14 x 14 m: asphalt with a zebra crossing where each street comes in (the corners are pavement)."""
    t = asphalt(21)
    paint = (236, 236, 230)
    a, b = 2.6 / 14, 11.4 / 14          # the road part of each side
    depth = 2.4 / 14                    # how far the crossing reaches in
    stripe = 0.5 / 14
    k = a + stripe * 0.5
    while k + stripe <= b:
        # south and north sides: stripes along the direction of travel
        t.rect(k, 1 - depth, k + stripe, 1 - 0.25 / 14, paint, FIXED)
        t.rect(k, 0.25 / 14, k + stripe, depth, paint, FIXED)
        # west and east sides
        t.rect(0.25 / 14, k, depth, k + stripe, paint, FIXED)
        t.rect(1 - depth, k, 1 - 0.25 / 14, k + stripe, paint, FIXED)
        k += stripe * 2
    return t.image()


def pavement():
    t = Tile((212, 210, 204))
    rng = np.random.default_rng(22)
    n = 4
    for r in range(n):
        for c in range(n):
            s = int(rng.integers(-10, 10))
            col = tuple(int(np.clip(v + s, 0, 255)) for v in (212, 210, 204))
            t.rect(c / n + 0.008, r / n + 0.008, (c + 1) / n - 0.008, (r + 1) / n - 0.008, col, WALL)
    t.grain(5, 23, cells=16)
    return t.image()


def grass():
    t = Tile((112, 186, 86))
    t.grain(16, 24, cells=12)
    rng = np.random.default_rng(25)
    for _ in range(900):
        x, y = rng.random(), rng.random()
        L = 0.012 + rng.random() * 0.02
        c = (150, 214, 110) if rng.random() < 0.5 else (88, 156, 70)
        t.polygon([(x, y), (x + 0.004, y - L), (x + 0.008, y)], c, WALL)
    return t.image()


def cobbles():
    t = Tile((170, 160, 146))
    rng = np.random.default_rng(26)
    rows, cols = 10, 8
    for r in range(rows):
        off = 0.5 / cols if r % 2 else 0
        for c in range(-1, cols + 1):
            x0 = c / cols + off + rng.uniform(-0.006, 0.006)
            s = int(rng.integers(-18, 14))
            col = tuple(int(np.clip(v + s, 0, 255)) for v in (228, 214, 190))
            box = [round((x0 + 0.008) * S), round((r / rows + 0.008) * S),
                   round((x0 + 1 / cols - 0.008) * S), round(((r + 1) / rows - 0.008) * S)]
            t.d.rounded_rectangle(box, radius=SS * 6, fill=col)
            t.da.rounded_rectangle(box, radius=SS * 6, fill=WALL)
    t.grain(6, 30, cells=16)
    return t.image()


def water():
    t = Tile((64, 150, 196), GLASS)
    t.grain(14, 27, cells=6)
    rng = np.random.default_rng(28)
    for _ in range(40):
        x, y, w = rng.random(), rng.random(), 0.05 + rng.random() * 0.1
        t.d.ellipse([round(x * S), round(y * S), round((x + w) * S), round((y + w * 0.25) * S)],
                    outline=(170, 220, 240), width=SS * 2)
    return t.image()


def leaves():
    t = Tile((196, 222, 180))
    rng = np.random.default_rng(29)
    for _ in range(260):
        x, y, r = rng.random(), rng.random(), 0.03 + rng.random() * 0.05
        c = (150, 186, 136) if rng.random() < 0.55 else (230, 246, 214)
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                t.ellipse(x - r + dx, y - r + dy, x + r + dx, y + r + dy, c, WALL)
    return t.image()


def plain():
    return Tile((255, 255, 255)).image()


SLICES = [plain, apartment, office, classic, balcony, shop, cafe, brick, roof, road, crossroads, pavement, grass,
          cobbles, water, leaves]


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--preview", action="store_true", help="also write a preview with the slice numbers")
    args = ap.parse_args()
    atlas = Image.new("RGBA", (TILE * 4, TILE * 4))
    for i, make in enumerate(SLICES):
        atlas.paste(make(), ((i % 4) * TILE, (i // 4) * TILE))
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    atlas.save(OUT)
    print(f"wrote {OUT}")
    if args.preview:
        os.makedirs(os.path.dirname(PREVIEW), exist_ok=True)
        rgb = Image.new("RGB", atlas.size, (255, 0, 255))
        rgb.paste(atlas.convert("RGB"))
        # tint the walls with a sample colour so the alpha meaning shows
        arr = np.asarray(atlas, np.float32)
        tint = np.array([255, 170, 150], np.float32) / 255
        wall = (arr[..., 3:4] > 200)
        arr[..., :3] = np.where(wall, arr[..., :3] * tint, arr[..., :3])
        preview = Image.fromarray(arr[..., :3].astype(np.uint8))
        d = ImageDraw.Draw(preview)
        for i in range(16):
            d.text(((i % 4) * TILE + 6, (i // 4) * TILE + 4), str(i), fill=(255, 0, 255))
        preview.save(PREVIEW)
        print(f"wrote {PREVIEW}")


if __name__ == "__main__":
    main()
