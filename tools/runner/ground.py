"""Vrapo!'s valley floor detail as code (2026-11-18): one seamless 512 x 512 texture the ground's pixel reads once
(Tundu/Valley _GROUND), covering 4.4 m across (two lanes: the lanes' trails line up with it) by 5 m along the path.

    motion-service-py/.venv/Scripts/python tools/runner/ground.py [--preview]

    R  the grass's dabs and blades (0.5 = as painted; lighter and darker strokes)
    G  the path: 0.5 = bare earth; pebbles light with a dark rim, cracks dark, three-toed dino footprints along the
       trails (at x = 0 and 2.2 m: every lane's middle), striding forward
    B  a soft wobble (0..1, ~0.5 m blobs): the path's ragged edge into the grass

Writes unity/MoveItGames/Assets/Games/Runner/Art/ground_detail.png; --preview also tools/runner/out/ground.png (the
channels side by side and the path and grass as the game colours them).
"""
import argparse
import math
import os

import cv2
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
ART = os.path.join(REPO, "unity", "MoveItGames", "Assets", "Games", "Runner", "Art")
OUT = os.path.join(HERE, "out")
N = 512
WIDTH_M, LENGTH_M = 4.4, 5.0


def periodic_noise(rng, scale, n=N):
    """Seamless noise: white noise filtered in the frequency domain (features about `scale` px), normalised to 0..1."""
    x = rng.standard_normal((n, n))
    f = np.fft.fft2(x)
    ky = np.fft.fftfreq(n)[:, None]
    kx = np.fft.fftfreq(n)[None, :]
    k = np.sqrt(kx * kx + ky * ky)
    f *= np.exp(-(k * scale) ** 2 * 4.0)
    y = np.real(np.fft.ifft2(f))
    y -= y.min()
    return y / max(1e-9, y.max())


def stamp(canvas, cx, cy, draw):
    """Draws `draw(img, cx, cy)` wrapped round the tile's edges (seamless)."""
    for dx in (-N, 0, N):
        for dy in (-N, 0, N):
            draw(canvas, cx + dx, cy + dy)


def px(x_m, z_m):
    return x_m / WIDTH_M * N, z_m / LENGTH_M * N


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", action="store_true")
    args = ap.parse_args()
    rng = np.random.default_rng(20261118)

    # ---- R: grass dabs and blades
    r = 0.5 + 0.22 * (periodic_noise(rng, 22) - 0.5) + 0.14 * (periodic_noise(rng, 6) - 0.5)
    blades = np.zeros((N, N), np.float32)
    for _ in range(1400):
        cx, cy = rng.uniform(0, N), rng.uniform(0, N)
        length = rng.uniform(6, 16)
        ang = rng.uniform(-0.5, 0.5) - math.pi / 2
        val = rng.choice([1.0, -1.0], p=[0.55, 0.45]) * rng.uniform(0.12, 0.26)

        def line(img, x, y, length=length, ang=ang, val=val):
            p0 = (int(x * 4), int(y * 4))
            p1 = (int((x + math.cos(ang) * length) * 4), int((y + math.sin(ang) * length) * 4))
            cv2.line(img, p0, p1, float(val), 5, cv2.LINE_AA, shift=2)
        stamp(blades, cx, cy, line)
    r = np.clip(r + blades, 0.12, 0.9)

    # ---- G: the path
    g = 0.5 + 0.1 * (periodic_noise(rng, 30) - 0.5) + 0.06 * (periodic_noise(rng, 5) - 0.5)
    # cracks: a few wandering thin lines
    cracks = np.zeros((N, N), np.float32)
    for _ in range(14):
        x, y = rng.uniform(0, N), rng.uniform(0, N)
        ang = rng.uniform(0, 2 * math.pi)
        pts = []
        for k in range(rng.integers(5, 10)):
            pts.append((x, y))
            ang += rng.uniform(-0.6, 0.6)
            step = rng.uniform(8, 18)
            x, y = x + math.cos(ang) * step, y + math.sin(ang) * step

        def poly(img, ox, oy, pts=pts, x0=pts[0][0], y0=pts[0][1]):
            q = np.array([[(px_ - x0 + ox) * 4, (py_ - y0 + oy) * 4] for px_, py_ in pts], np.int32)
            cv2.polylines(img, [q], False, -0.24, 4, cv2.LINE_AA, shift=2)
        stamp(cracks, pts[0][0], pts[0][1], poly)
    g += cracks
    # pebbles: light stones with a darker rim and a little shadow under them
    peb = np.zeros((N, N), np.float32)
    for _ in range(170):
        cx, cy = rng.uniform(0, N), rng.uniform(0, N)
        rr = rng.uniform(2.5, 7.0)
        ax = rng.uniform(0.7, 1.3)
        rot = rng.uniform(0, 180)
        light = rng.uniform(0.22, 0.38)

        def pebble(img, x, y, rr=rr, ax=ax, rot=rot, light=light):
            c = (int(x * 4), int(y * 4))
            cv2.ellipse(img, (c[0] + 4, c[1] + 6), (int(rr * ax * 4 + 4), int(rr * 4 + 4)), rot, 0, 360, -0.2, -1, cv2.LINE_AA, shift=2)
            cv2.ellipse(img, c, (int(rr * ax * 4), int(rr * 4)), rot, 0, 360, light, -1, cv2.LINE_AA, shift=2)
        stamp(peb, cx, cy, pebble)
    g = np.where(peb != 0, 0.5 + peb, g)
    # footprints along the two trails in the tile (x = 0 and 2.2 m), striding forward: a print every metre, left and right
    prints = np.zeros((N, N), np.float32)
    for trail_x in (0.0, 2.2):
        for k in range(5):
            z = 0.4 + k * 1.0
            side = 1 if k % 2 == 0 else -1
            cx, cy = px(trail_x + side * 0.16, z)

            def foot(img, x, y, side=side):
                # the heel pad and three toes pointing forward (+z = down the texture), all at 4x
                s = 4
                # toes point up the image: Unity samples the PNG's top at v = 1, the far end (+z, the way Dino runs)
                heel = (int(x * s), int(y * s))
                cv2.ellipse(img, heel, (int(7 * s), int(6 * s)), 0, 0, 360, -0.3, -1, cv2.LINE_AA, shift=2)
                for ang, length in ((-34, 22), (0, 27), (34, 22)):
                    a = math.radians(ang - 90)
                    tip = (int((x + math.cos(a) * length) * s), int((y + math.sin(a) * length) * s))
                    base = (int((x + math.cos(a) * 5) * s), int((y + math.sin(a) * 5) * s))
                    cv2.line(img, base, tip, -0.3, int(4 * s), cv2.LINE_AA, shift=2)
                    cv2.circle(img, tip, int(2.5 * s), -0.3, -1, cv2.LINE_AA, shift=2)
            stamp(prints, cx, cy, foot)
    prints = cv2.GaussianBlur(prints, (0, 0), 0.5)
    g = np.clip(g + prints, 0.08, 0.95)

    # ---- B: the edge's wobble
    b = periodic_noise(rng, 40) * 0.7 + periodic_noise(rng, 12) * 0.3
    b = (b - b.min()) / (b.max() - b.min())

    rgb = np.dstack([r, g, b])
    os.makedirs(ART, exist_ok=True)
    Image.fromarray((np.clip(rgb, 0, 1) * 255 + 0.5).astype(np.uint8), "RGB").save(os.path.join(ART, "ground_detail.png"))
    print(f"  ground_detail.png ({N} x {N}: {WIDTH_M} m x {LENGTH_M} m)")
    if args.preview:
        os.makedirs(OUT, exist_ok=True)
        path = np.array([0.93, 0.74, 0.5]) * np.where(g[..., None] < 0.5, 0.55 + 0.9 * g[..., None], 1.0 + (g[..., None] - 0.5) * 0.5)
        grass = np.array([0.36, 0.62, 0.3]) * (0.62 + 0.76 * r[..., None])
        sheet = np.hstack([np.dstack([r] * 3), np.dstack([g] * 3), np.dstack([b] * 3), np.clip(path, 0, 1), np.clip(grass, 0, 1)])
        Image.fromarray((sheet * 255).astype(np.uint8)).save(os.path.join(OUT, "ground.png"))


if __name__ == "__main__":
    main()
