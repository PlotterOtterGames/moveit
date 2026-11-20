"""Yjet!'s sky as code (2026-11-14, the "toy galaxy" look): one painted panorama round the whole trip, an
equirectangular picture the skybox shader (Tundu/SkyPanorama) wraps round the camera.

A deep indigo night that is never black; a luminous galaxy band across it with a warm core ahead and to the upper left
of the flight (the way runs along Unity's +Z), dark dust lanes along the band; soft, wispy clouds of colour (magenta,
violet, teal, coral) here and there, the brightest behind the astronauts on the menu (the camera looks back at them);
thousands of small stars, denser along the band, and a few big sparkling ones with a glow and a four-point flare. The
twinkling star field (a mesh round the camera) and the planets add the rest.

    motion-service-py/.venv/Scripts/python tools/space/sky.py [--preview]

Writes unity/MoveItGames/Assets/Games/Space/Art/space_sky.png (4096 x 2048, RGB): column = longitude, atan2(z, x) from
-180 degrees at the left, row = latitude from +90 at the top (the shader's mapping); --preview also writes
tools/space/out/sky_preview.png (smaller) and sky_views.png (what the flight and the menu cameras see).
"""
import argparse
import os
import sys

import cv2
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import art  # noqa: E402  (the noise on the sphere, mixing helpers)
from art import fbm, hexc, ss  # noqa: E402

ART = art.ART
OUT = art.OUT
W, H = 4096, 2048          # the picture
NW, NH = 2048, 1024        # the soft layers are painted at half size (they're smooth) and scaled up


def directions(w, h):
    """Each pixel's direction (Unity axes: x right, y up, z forward), latitude and longitude."""
    j, i = np.mgrid[0:h, 0:w].astype(np.float32)
    lon = (i + 0.5) / w * 2 * np.pi - np.pi
    lat = np.pi / 2 - (j + 0.5) / h * np.pi
    d = np.stack([np.cos(lat) * np.cos(lon), np.sin(lat), np.cos(lat) * np.sin(lon)], -1).astype(np.float32)
    return d, lat, lon


def unit(v):
    v = np.asarray(v, np.float32)
    return v / np.linalg.norm(v)


def colour(c):
    return np.array(hexc(c), np.float32)


def angle_to(d, c):
    return np.arccos(np.clip(d @ unit(c), -1.0, 1.0))


# the galaxy's plane: through ahead-up-left (where its core is) and up-right, so the band crosses the flight's view
CORE = unit((-0.42, 0.26, 1.0))
BAND_N = unit(np.cross(CORE, unit((1.0, 0.85, -0.25))))

# clouds of colour: (centre, angular size (radians), colours from the edge to the middle, strength)
NEBULAE = [
    ((-0.75, 0.12, 1.0), 0.55, ("#5B3FD9", "#FF4FA0", "#FFD6F0"), 0.95),   # magenta and violet beside the core
    ((0.95, -0.28, 0.75), 0.62, ("#1287C9", "#1FE0C8", "#D6FFF8"), 1.0),   # teal down to the right of the way
    ((0.35, 0.32, -1.0), 0.6, ("#7B3FD9", "#FF5A8A", "#FFE0EC"), 0.95),   # coral-pink behind the menu's astronauts
    ((-1.0, -0.25, -0.35), 0.7, ("#2A2A9A", "#7B61FF", "#E0DAFF"), 0.75),  # deep violet to the left behind
    ((0.25, 1.0, 0.25), 0.55, ("#1F7FA0", "#3FD9B0", "#E0FFF4"), 0.6),     # a green-teal overhead
    ((-0.25, -0.9, 0.35), 0.7, ("#3A1E7A", "#B04FD9", "#F0D6FF"), 0.55),   # violet far below the way
]


def soft_layers():
    """The night, the band, its core and dust, the clouds of colour (half size, linear-ish 0..1 rgb)."""
    d, lat, lon = directions(NW, NH)
    # the night: deep indigo, a little lighter and more violet toward the galaxy's plane
    b = np.arcsin(np.clip(d @ BAND_N, -1, 1))                      # angle from the galaxy's plane
    near_band = np.exp(-(b / 0.55) ** 2)
    rgb = art.mix(colour("#070A26"), colour("#171552"), near_band * 0.85)
    rgb = art.mix(rgb, colour("#0B1238"), ss(0.2, -0.9, d[..., 1]) * 0.5)   # a bluer night below the way
    # the band: clumpy light along the plane, warm toward the core, cut by dark wisps of dust
    clump = fbm(d * 2.2, 1.0, 5, seed=401)
    fine = fbm(d * 7.0, 1.0, 4, seed=402)
    band = np.exp(-(b / (0.16 + 0.08 * clump)) ** 2) * (0.45 + 0.9 * clump) * (0.75 + 0.5 * fine)
    core = np.exp(-(angle_to(d, CORE) / 0.26) ** 2)
    halo = np.exp(-(angle_to(d, CORE) / 0.6) ** 2)
    band_rgb = art.mix(colour("#8C7CFF"), colour("#FFC98A"), np.clip(core * 1.5, 0, 1))
    rgb = rgb + band_rgb * (band * 0.4 + core * 0.38 + halo * 0.1)[..., None]
    dust = ss(0.52, 0.66, fbm(d * 5.0, 1.0, 4, seed=403)) * np.exp(-((b - 0.015) / 0.07) ** 2)
    rgb = rgb * (1 - 0.45 * dust)[..., None] + colour("#2A1440") * (0.2 * dust)[..., None]
    # the clouds of colour: wispy (warped noise), brightest and palest in the middle, screened onto the night
    for k, (centre, size, cols, strength) in enumerate(NEBULAE):
        a = angle_to(d, centre) / size
        warp = np.stack([fbm(d * 3.0, 1.0, 3, seed=500 + k * 7 + m) for m in range(3)], -1) - 0.5
        n = fbm(d * 2.6 + warp * 1.4, 1.0, 5, seed=520 + k * 7)
        wisps = ss(0.38, 0.75, n) * np.exp(-a ** 2 * 1.6)
        heart = np.exp(-(a / 0.45) ** 2) * ss(0.45, 0.85, n)
        c0, c1, c2 = (colour(c) for c in cols)
        t = np.clip(wisps * 1.4, 0, 1)
        cloud = art.mix(c0, c1, t)
        cloud = art.mix(cloud, c2, np.clip(heart, 0, 1))
        alpha = np.clip(wisps * 0.85 + heart * 0.5, 0, 1) * strength
        rgb = 1 - (1 - rgb) * (1 - cloud * alpha[..., None] * 0.8)        # screen
    return np.clip(rgb, 0, 1)


def draw_stars(img, rng):
    """Thousands of small stars (denser along the band) and a few big sparkling ones, drawn at full size (crisp)."""
    h, w = img.shape[:2]
    tints = [colour(c) for c in ("#FFFFFF", "#D6E4FF", "#FFF1CC", "#FFD6EA", "#CFF8FF")]

    def place(dirs):
        lat = np.arcsin(np.clip(dirs[:, 1], -1, 1))
        lon = np.arctan2(dirs[:, 2], dirs[:, 0])
        x = (lon + np.pi) / (2 * np.pi) * w
        y = (np.pi / 2 - lat) / np.pi * h
        return x, y, lat

    def dot(x, y, lat, r, col, k):
        # a soft round dot, stretched across where the picture is stretched (toward the poles)
        sx = r / max(0.15, np.cos(lat))
        x0, x1 = int(x - 3 * sx) - 1, int(x + 3 * sx) + 2
        y0, y1 = int(y - 3 * r) - 1, int(y + 3 * r) + 2
        if y0 < 0 or y1 >= h:
            return
        xs = np.arange(x0, x1)
        ys = np.arange(y0, y1)
        gx = np.exp(-((xs + 0.5 - x) / sx) ** 2)
        gy = np.exp(-((ys + 0.5 - y) / r) ** 2)
        g = (gy[:, None] * gx[None, :] * k)[..., None] * col
        cols = np.mod(xs, w)
        img[y0:y1, cols] = 1 - (1 - img[y0:y1, cols]) * (1 - np.clip(g, 0, 1))

    # small stars: everywhere, and many more along the band
    every = rng.normal(size=(7000, 3))
    every /= np.linalg.norm(every, axis=1, keepdims=True)
    along = rng.normal(size=(9000, 3))
    along -= np.outer(along @ BAND_N, BAND_N)                       # onto the plane...
    along /= np.linalg.norm(along, axis=1, keepdims=True)
    along = along + np.outer(rng.normal(scale=0.12, size=9000), BAND_N)   # ...and a little off it
    along /= np.linalg.norm(along, axis=1, keepdims=True)
    for dirs, base in ((every, 0.75), (along, 0.6)):
        x, y, lat = place(dirs)
        bright = rng.random(len(dirs)) ** 3
        size = 0.55 + 0.9 * rng.random(len(dirs)) ** 2
        tint = rng.integers(0, len(tints), len(dirs))
        for k in range(len(dirs)):
            dot(x[k], y[k], lat[k], size[k], tints[tint[k]], base * (0.25 + 0.75 * bright[k]))
    # big sparkling stars: a glow, a bright middle and a four-point flare
    big = rng.normal(size=(60, 3))
    big /= np.linalg.norm(big, axis=1, keepdims=True)
    x, y, lat = place(big)
    for k in range(len(big)):
        col = tints[rng.integers(0, len(tints))]
        r = 1.6 + 1.6 * rng.random()
        dot(x[k], y[k], lat[k], r * 3.2, col, 0.22)                 # the glow
        dot(x[k], y[k], lat[k], r, col, 1.0)                        # the star
        length = 9 + 16 * rng.random()
        stretch = 1 / max(0.15, np.cos(lat[k]))
        for dx, dy in ((1, 0), (0, 1)):
            n = int(length * 2)
            for s in range(-n, n + 1):
                fall = (1 - abs(s) / (n + 1)) ** 2
                px, py = x[k] + dx * s * stretch * 0.5, y[k] + dy * s * 0.5
                ix, iy = int(px) % w, int(py)
                if 0 <= iy < h:
                    img[iy, ix] = 1 - (1 - img[iy, ix]) * (1 - np.clip(col * fall * 0.8, 0, 1))


def views(sky):
    """What the flight camera (ahead, a little down) and the menu camera (behind, level) see, side by side."""
    out = []
    for yaw, pitch in ((0.0, -6.0), (180.0, -2.0)):
        vw, vh, fov = 960, 540, 60.0
        j, i = np.mgrid[0:vh, 0:vw].astype(np.float32)
        t = np.tan(np.radians(fov / 2))
        x = ((i + 0.5) / vw * 2 - 1) * t * vw / vh
        y = (1 - (j + 0.5) / vh * 2) * t
        dirs = np.stack([x, y, np.ones_like(x)], -1)
        dirs /= np.linalg.norm(dirs, axis=-1, keepdims=True)
        p, q = np.radians(pitch), np.radians(yaw)
        rx = np.array([[1, 0, 0], [0, np.cos(p), np.sin(p)], [0, -np.sin(p), np.cos(p)]], np.float32)
        ry = np.array([[np.cos(q), 0, np.sin(q)], [0, 1, 0], [-np.sin(q), 0, np.cos(q)]], np.float32)
        dirs = dirs @ rx.T @ ry.T
        lat = np.arcsin(np.clip(dirs[..., 1], -1, 1))
        lon = np.arctan2(dirs[..., 2], dirs[..., 0])
        u = ((lon + np.pi) / (2 * np.pi) * sky.shape[1]).astype(np.float32)
        v = ((np.pi / 2 - lat) / np.pi * sky.shape[0]).astype(np.float32)
        out.append(cv2.remap(sky, u, v, cv2.INTER_LINEAR, borderMode=cv2.BORDER_WRAP))
    return np.concatenate(out, axis=1)


def main():
    ap = argparse.ArgumentParser(description="Yjet!'s sky as code")
    ap.add_argument("--preview", action="store_true")
    args = ap.parse_args()
    soft = soft_layers()
    sky = cv2.resize(soft.astype(np.float32), (W, H), interpolation=cv2.INTER_CUBIC)
    sky = np.clip(sky, 0, 1)
    draw_stars(sky, np.random.default_rng(77))
    os.makedirs(ART, exist_ok=True)
    Image.fromarray((np.clip(sky, 0, 1) * 255 + 0.5).astype(np.uint8), "RGB").save(os.path.join(ART, "space_sky.png"), optimize=True)
    print(f"  sky -> {os.path.relpath(os.path.join(ART, 'space_sky.png'), art.REPO)} ({W} x {H})")
    if args.preview:
        os.makedirs(OUT, exist_ok=True)
        small = cv2.resize(sky, (2048, 1024), interpolation=cv2.INTER_AREA)
        Image.fromarray((small * 255).astype(np.uint8)).save(os.path.join(OUT, "sky_preview.png"))
        Image.fromarray((np.clip(views(sky), 0, 1) * 255).astype(np.uint8)).save(os.path.join(OUT, "sky_views.png"))
        print("  previews in", os.path.relpath(OUT, art.REPO))


if __name__ == "__main__":
    main()
