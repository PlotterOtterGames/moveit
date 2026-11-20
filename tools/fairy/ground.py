"""Zana!'s meadow detail as code (numpy + PIL; the motion-service venv): one seamless 512 x 512 data texture that
Tundu/Realm's ground reads once a pixel, in world space (2026-11-17, the look's overhaul).

    R  grass: soft painterly dabs, lighter and darker, in clumps: the grass's colour times 2R (0.5 = no change)
    G  the paths: rounded cobbles with darker gaps between them: the path's colour is lerp(gaps, stones, G)
    B  a soft wobble the path's edge is broken up by (0 .. 1), so the edges aren't the grid's straight lines
(2026-11-17: the channels hold what the pixel needs, so the meadow's pixel is a handful of operations: on the box's GPU
at 200 MHz every operation over the meadow cost ~0.2 ms)

    python tools/fairy/ground.py [--preview]   -> unity/MoveItGames/Assets/Games/Fairy/Art/garden_detail.png
"""

import os
import sys

import numpy as np
from PIL import Image

N = 512
REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(REPO, "unity", "MoveItGames", "Assets", "Games", "Fairy", "Art", "garden_detail.png")
rng = np.random.default_rng(20261117)


def periodic_noise(octaves):
    """Smooth noise that tiles: random sinusoids with whole-number frequencies; octaves = [(freq, amp, count)]."""
    y, x = np.mgrid[0:N, 0:N] / N
    out = np.zeros((N, N))
    for freq, amp, count in octaves:
        for _ in range(count):
            fx, fy = rng.integers(-freq, freq + 1, 2)
            if fx == 0 and fy == 0:
                fx = freq
            ph = rng.uniform(0, 2 * np.pi)
            out += amp * np.sin(2 * np.pi * (fx * x + fy * y) + ph) / np.sqrt(count)
    return out


def splats(count, sigma_long, sigma_short, angles, signed=True):
    """Soft elongated dabs at random places (periodic: a convolution done by FFT wraps round the tile)."""
    total = np.zeros((N, N))
    groups = len(angles)
    yy, xx = np.mgrid[0:N, 0:N]
    xx = np.where(xx > N // 2, xx - N, xx).astype(float)
    yy = np.where(yy > N // 2, yy - N, yy).astype(float)
    for a in angles:
        imp = np.zeros((N, N))
        k = count // groups
        ys, xs = rng.integers(0, N, k), rng.integers(0, N, k)
        vals = rng.uniform(-1, 1, k) if signed else rng.uniform(0.4, 1, k)
        np.add.at(imp, (ys, xs), vals)
        c, s = np.cos(a), np.sin(a)
        u = xx * c + yy * s
        v = -xx * s + yy * c
        kern = np.exp(-(u * u) / (2 * sigma_long ** 2) - (v * v) / (2 * sigma_short ** 2))
        total += np.real(np.fft.ifft2(np.fft.fft2(imp) * np.fft.fft2(kern)))
    return total


def normalise(a, lo=0.0, hi=1.0):
    a = (a - a.mean()) / (a.std() + 1e-9)
    return a


def grass():
    clumps = periodic_noise([(3, 1.0, 6), (6, 0.6, 8)])
    dabs = splats(9000, 5.0, 1.8, np.linspace(0, np.pi, 6, endpoint=False))
    fine = splats(16000, 2.2, 0.9, np.linspace(0, np.pi, 4, endpoint=False))
    g = 0.55 * normalise(clumps) + 0.8 * normalise(dabs) + 0.35 * normalise(fine)
    return np.clip(0.5 + 0.11 * g, 0, 1)


def cobbles():
    """Voronoi cells (wrapped): each cell a rounded stone, its own shade; dark gaps; little pebbles between."""
    pts = rng.uniform(0, N, (150, 2))
    y, x = np.mgrid[0:N, 0:N].astype(float)
    d1 = np.full((N, N), 1e9)
    d2 = np.full((N, N), 1e9)
    shade = np.zeros((N, N))
    tone = rng.uniform(-1, 1, len(pts))
    for i, (px, py) in enumerate(pts):
        dx = np.abs(x - px)
        dy = np.abs(y - py)
        dx = np.minimum(dx, N - dx)
        dy = np.minimum(dy, N - dy)
        d = np.sqrt(dx * dx + dy * dy)
        closer = d < d1
        d2 = np.where(closer, d1, np.minimum(d2, d))
        shade = np.where(closer, tone[i], shade)
        d1 = np.where(closer, d, d1)
    edge = d2 - d1                       # 0 on the border between two stones
    gap = np.clip(edge / 6.0, 0, 1) ** 0.6
    dome = np.clip(1 - d1 / 26.0, 0, 1) ** 0.5   # stones a little lighter in their middles
    pebbles = splats(5000, 1.4, 1.1, [0.0], signed=True)
    g = 0.5 + 0.13 * shade * gap + 0.08 * dome * gap - 0.32 * (1 - gap) + 0.04 * normalise(pebbles)
    return np.clip(g, 0, 1)


def wobble():
    w = periodic_noise([(4, 1.0, 6), (9, 0.5, 8), (17, 0.25, 8)])
    w = normalise(w)
    return np.clip(0.5 + 0.22 * w, 0, 1)


def main():
    r, g, b = grass(), cobbles(), wobble()
    # ready for the pixel: the grass's multiplier halved (0.5 + dabs, the dabs at half strength), the stones' blend
    r = np.clip((0.5 + r) * 0.5, 0, 1)
    g = np.clip(np.clip((g - 0.11) * 4, 0, 1) * (0.41 + 0.9 * g), 0, 1)
    img = np.stack([r, g, b], axis=-1)
    Image.fromarray((img * 255 + 0.5).astype(np.uint8), "RGB").save(OUT)
    print("wrote", OUT)
    if "--preview" in sys.argv:
        out_dir = os.path.join(os.path.dirname(__file__), "out")
        os.makedirs(out_dir, exist_ok=True)
        tiles = []
        for ch in (r, g, b):
            t = np.tile(ch, (2, 2))
            tiles.append(t)
        prev = np.concatenate(tiles, axis=1)
        Image.fromarray((prev * 255).astype(np.uint8), "L").save(os.path.join(out_dir, "garden_detail_preview.png"))
        # how the meadow and the path look with it (the shader's mix, roughly)
        grass_c = np.array([0.42, 0.72, 0.36])
        path_c = np.array([0.91, 0.79, 0.6])
        mask = np.zeros((N * 2, N * 2))
        yy = np.arange(N * 2)[:, None]
        mask[:] = (np.abs(yy - N) < N * 0.3)
        bb = np.tile(b, (2, 2))
        m = np.clip((mask + (bb - 0.5) * 1.2 - 0.5) * 6 + 0.5, 0, 1)[..., None]
        gr = grass_c * (2 * np.tile(r, (2, 2)))[..., None]
        gaps = np.array([0.73, 0.56, 0.53])
        gg = np.tile(g, (2, 2))[..., None]
        pa = gaps * (1 - gg) + path_c * gg
        look = gr * (1 - m) + pa * m
        Image.fromarray((np.clip(look, 0, 1) * 255).astype(np.uint8), "RGB").save(os.path.join(out_dir, "garden_detail_look.png"))


if __name__ == "__main__":
    main()
