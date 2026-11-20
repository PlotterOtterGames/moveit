"""Yjet!'s planets as code (2026-11-14, the exploration rework): the eight height maps every planet of the galaxy is
coloured from. A planet = one of these maps (turned, maybe mirrored) + its own colour ramp, sea level, clouds, rings
and moons (Galaxy.cs), so 278 planets look their own with no picture drawn by hand.

    motion-service-py/.venv/Scripts/python tools/space/heights.py [--size 2048] [--preview] [--only 0,3]

Writes unity/MoveItGames/Assets/Games/Space/Art/planet_heights.png: the maps stacked (2048 x 1024 each, 8-bit grey,
equirectangular, seamless across), imported as an 8-slice texture array (SpaceArtImport). Every map is spread evenly
over 0..1 (a ramp key at 0.4 colours 40 % of the planet; a sea level of 0.6 makes 60 % water) unless its shape needs
its own profile (craters, crystals).

    0 continents   warped noise with ridged mountains on the high ground
    1 islands      a finer, more broken version: archipelagos at a high sea level
    2 craters      a dusty plain, craters of every size (a bowl, a raised rim, rays of ejecta)
    3 cells        cracked plates (ice floes; lava between crust): the cracks are the lowest heights
    4 swirls       marbled, twice-warped noise (jungles, toxic seas, candy)
    5 dunes        wind-combed dune fields over rolling ground
    6 bands        a gas giant's bands: latitude stripes torn by turbulence, with storms
    7 facets       crystal facets (each cell a tilted plane) with veins at the top of the range

Also writes Art/clouds.png (--clouds: only that): every cloudy planet's layer (2048 x 1024, white, alpha = the cloud),
wispy streaks along the latitudes and a few cyclones, about 40 % of the sphere.

--preview writes tools/space/out/heights.png: every map flat and on a lit ball with a sample ramp.
"""
import argparse
import os
import time

import cv2
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
ART = os.path.join(REPO, "unity", "MoveItGames", "Assets", "Games", "Space", "Art")
OUT = os.path.join(HERE, "out")

NAMES = ["continents", "islands", "craters", "cells", "swirls", "dunes", "bands", "facets"]

# ------------------------------------------------------------------------------------------------- gradient noise

_PERM = np.random.RandomState(1).permutation(256).astype(np.int32)
_PERM = np.concatenate([_PERM, _PERM])
_GRAD = np.array([[1, 1, 0], [-1, 1, 0], [1, -1, 0], [-1, -1, 0], [1, 0, 1], [-1, 0, 1], [1, 0, -1], [-1, 0, -1],
                  [0, 1, 1], [0, -1, 1], [0, 1, -1], [0, -1, -1]], np.float32)
# each octave turned a little (no grid lines lining up between octaves)
_ROT = cv2.Rodrigues(np.array([0.61, 0.37, 0.85], np.float64))[0].astype(np.float32)


def perlin(x, y, z):
    """3D gradient noise, about -1..1."""
    xi, yi, zi = np.floor(x), np.floor(y), np.floor(z)
    xf, yf, zf = x - xi, y - yi, z - zi
    xi = xi.astype(np.int32) & 255
    yi = yi.astype(np.int32) & 255
    zi = zi.astype(np.int32) & 255
    u = xf * xf * xf * (xf * (xf * 6 - 15) + 10)
    v = yf * yf * yf * (yf * (yf * 6 - 15) + 10)
    w = zf * zf * zf * (zf * (zf * 6 - 15) + 10)

    def g(ix, iy, iz, dx, dy, dz):
        h = _PERM[_PERM[_PERM[ix] + iy] + iz] % 12
        gr = _GRAD[h]
        return gr[..., 0] * dx + gr[..., 1] * dy + gr[..., 2] * dz

    x1, y1, z1 = xi + 1, yi + 1, zi + 1
    n000 = g(xi, yi, zi, xf, yf, zf)
    n100 = g(x1, yi, zi, xf - 1, yf, zf)
    n010 = g(xi, y1, zi, xf, yf - 1, zf)
    n110 = g(x1, y1, zi, xf - 1, yf - 1, zf)
    n001 = g(xi, yi, z1, xf, yf, zf - 1)
    n101 = g(x1, yi, z1, xf - 1, yf, zf - 1)
    n011 = g(xi, y1, z1, xf, yf - 1, zf - 1)
    n111 = g(x1, y1, z1, xf - 1, yf - 1, zf - 1)
    a = n000 + u * (n100 - n000)
    b = n010 + u * (n110 - n010)
    c = n001 + u * (n101 - n001)
    d = n011 + u * (n111 - n011)
    e = a + v * (b - a)
    f = c + v * (d - c)
    return (e + w * (f - e)).astype(np.float32)


def fbm(p, freq=1.0, octaves=6, seed=0, gain=0.5, ridged=False):
    """Fractal noise on points p (..., 3): about -1..1 (ridged: 0..1, sharp crests)."""
    q = p * freq + np.array([seed * 17.31, seed * 7.77, seed * 3.19], np.float32)
    total = np.zeros(p.shape[:-1], np.float32)
    amp, norm, weight = 1.0, 0.0, 1.0
    for _ in range(octaves):
        n = perlin(q[..., 0], q[..., 1], q[..., 2])
        if ridged:
            n = (1.0 - np.abs(n)) ** 2
            n = n * weight
            weight = np.clip(n * 1.6, 0, 1)
        total += amp * n
        norm += amp
        q = (q @ _ROT.T) * 2.02 + 5.3
        amp *= gain
    return total / norm


def warp(p, amp, freq, seed, octaves=4):
    d = np.stack([fbm(p, freq, octaves, seed + k * 101) for k in range(3)], -1)
    return p + amp * d


def onto_sphere(q):
    return (q / np.linalg.norm(q, axis=-1, keepdims=True)).astype(np.float32)


def ss(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def even(h, share=1.0):
    """Spread h evenly over 0..1 (by rank); share < 1 keeps some of its own profile (min-max)."""
    flat = h.ravel()
    rank = np.empty(flat.size, np.float32)
    rank[np.argsort(flat, kind="stable")] = np.linspace(0, 1, flat.size, dtype=np.float32)
    lo, hi = np.percentile(flat, 0.2), np.percentile(flat, 99.8)
    mm = np.clip((flat - lo) / max(1e-6, hi - lo), 0, 1)
    return (share * rank + (1 - share) * mm).reshape(h.shape).astype(np.float32)


def sphere(w, h):
    """Every pixel of an equirectangular map: its point on the unit sphere (y up). u = 0..1 round from longitude -180
    degrees, v = 0 at the north pole (row 0) to 1 at the south. The Unity sphere is made to match (SpaceArt.Globe)."""
    j, i = np.mgrid[0:h, 0:w].astype(np.float32)
    lon = (i + 0.5) / w * 2 * np.pi - np.pi
    lat = np.pi / 2 - (j + 0.5) / h * np.pi
    return np.stack([np.cos(lat) * np.cos(lon), np.sin(lat), np.cos(lat) * np.sin(lon)], -1).astype(np.float32), lat, lon


def fib_points(n, seed, jitter=0.35):
    """n points spread evenly over the sphere (a Fibonacci spiral, jittered)."""
    rng = np.random.RandomState(seed)
    k = np.arange(n) + 0.5
    phi = np.arccos(1 - 2 * k / n)
    th = np.pi * (1 + 5 ** 0.5) * k
    c = np.stack([np.sin(phi) * np.cos(th), np.cos(phi), np.sin(phi) * np.sin(th)], -1)
    c = c + rng.normal(size=c.shape) * jitter * np.sqrt(4 * np.pi / n)
    return (c / np.linalg.norm(c, axis=1, keepdims=True)).astype(np.float32)


def voronoi(p, centres, chunk=1 << 16):
    """For every point: the angle to its nearest centre, to the second nearest, and the nearest's index."""
    flat = p.reshape(-1, 3)
    f1 = np.empty(flat.shape[0], np.float32)
    f2 = np.empty(flat.shape[0], np.float32)
    idx = np.empty(flat.shape[0], np.int32)
    for a in range(0, flat.shape[0], chunk):
        dots = flat[a:a + chunk] @ centres.T
        part = np.argpartition(-dots, 1, axis=1)[:, :2]
        d0 = np.take_along_axis(dots, part[:, :1], 1)[:, 0]
        d1 = np.take_along_axis(dots, part[:, 1:2], 1)[:, 0]
        swap = d1 > d0
        best = np.where(swap, part[:, 1], part[:, 0])
        hi, lo = np.maximum(d0, d1), np.minimum(d0, d1)
        f1[a:a + chunk] = np.arccos(np.clip(hi, -1, 1))
        f2[a:a + chunk] = np.arccos(np.clip(lo, -1, 1))
        idx[a:a + chunk] = best
    shape = p.shape[:-1]
    return f1.reshape(shape), f2.reshape(shape), idx.reshape(shape)


# ------------------------------------------------------------------------------------------------- the maps

def continents(p):
    q = warp(p, 0.42, 1.1, 10)
    base = fbm(q, 1.5, 7, 11)
    mountains = fbm(q, 3.4, 5, 12, ridged=True) * ss(0.0, 0.3, base)
    return even(base + 0.32 * mountains)


def islands(p):
    q = warp(p, 0.55, 1.8, 20)
    h = fbm(q, 3.0, 7, 21) + 0.12 * fbm(q, 7.0, 4, 22, ridged=True) + 0.25 * fbm(p, 0.9, 3, 23)
    return even(h)


def craters(p, seed=30):
    rng = np.random.RandomState(seed)
    h = 0.28 * fbm(warp(p, 0.25, 2.0, seed), 2.4, 6, seed + 1)
    flat = p.reshape(-1, 3)
    out = h.ravel().copy()
    n = 520
    centres = fib_points(n, seed + 2, jitter=1.0)
    radii = np.clip(0.014 * rng.uniform(0.02, 1.0, n) ** -0.62, 0.014, 0.42)
    order = np.argsort(-radii)                      # the big ones first: the small ones dent them
    for k in order:
        r = radii[k]
        dots = flat @ centres[k]
        near = dots > np.cos(min(np.pi, r * 2.6))
        if not near.any():
            continue
        d = np.arccos(np.clip(dots[near], -1, 1)) / r
        depth = 0.55 * r ** 0.55
        bowl = -depth * np.clip(1 - d * d, 0, 1) ** 0.8
        rim = 0.45 * depth * np.exp(-((d - 1.0) / 0.22) ** 2)
        ejecta = 0.12 * depth * np.exp(-np.maximum(d - 1.0, 0) * 2.2) * (d > 1)
        floor = (d < 0.35) * (r > 0.12) * 0.15 * depth * (1 - d / 0.35)   # a central peak in the biggest
        out[near] = out[near] * np.where(d < 1.2, 0.35, 1.0) + bowl + rim + ejecta + floor
    out = out.reshape(h.shape) + 0.05 * fbm(p, 9.0, 4, seed + 3)
    return even(out, 0.45)


def cells(p, seed=40):
    q = onto_sphere(warp(p, 0.07, 3.0, seed))
    centres = fib_points(150, seed + 1, jitter=0.45)
    f1, f2, idx = voronoi(q, centres)
    rng = np.random.RandomState(seed + 2)
    plate = rng.uniform(0, 1, len(centres)).astype(np.float32)[idx]
    crack = 1 - ss(0.0, 0.035 + 0.02 * (fbm(p, 6.0, 3, seed + 3) + 1), f2 - f1)
    h = 0.6 * plate + 0.4 * (fbm(p, 4.5, 6, seed + 4) * 0.5 + 0.5)
    h = even(h) * 0.85 + 0.15
    h = h * (1 - crack) + crack * (0.02 + 0.06 * (fbm(p, 12.0, 3, seed + 5) * 0.5 + 0.5))
    return even(h, 0.7)


def swirls(p):
    q = warp(warp(p, 0.65, 1.4, 50), 0.55, 2.4, 60)
    h = fbm(q, 2.1, 6, 61) + 0.1 * fbm(p, 8.0, 3, 62)
    return even(h)


def dunes(p, seed=70):
    q = warp(p, 0.22, 1.6, seed)
    axis = np.array([0.3, 0.86, 0.41], np.float32)
    axis /= np.linalg.norm(axis)
    d = (q @ axis) * 26.0 + 2.2 * fbm(p, 1.4, 3, seed + 1)
    fr = d - np.floor(d)
    dune = ss(0.0, 0.78, fr) * ss(1.0, 0.8, fr)          # a long windward slope, a short steep lee
    field = 0.5 + 0.5 * fbm(p, 1.8, 4, seed + 2)
    # no dunes round the two ends of the axis (the lines would ring round them like a target): sand seas there
    field = field * ss(0.9, 0.62, np.abs(p @ axis))
    h = 0.55 * (fbm(p, 2.0, 6, seed + 3) * 0.5 + 0.5) + 0.45 * dune * ss(0.25, 0.65, field)
    return even(h)


def bands(p, lat, lon, seed=80):
    # turbulence stretched along the bands, the bands' edges torn into waves
    streak = np.stack([p[..., 0] * 1.0, p[..., 1] * 7.0, p[..., 2] * 1.0], -1)
    turb = fbm(warp(streak, 0.35, 0.9, seed), 1.6, 6, seed + 1)
    t = np.sin(lat) * 6.0 + 0.55 * fbm(warp(p, 0.3, 2.6, seed + 2), 2.0, 5, seed + 3)
    b = 0.55 * np.sin(t * 2.1) + 0.3 * np.sin(t * 4.7 + 1.3) + 0.15 * np.sin(t * 9.9 + 0.4)
    h = b + 0.45 * turb
    # storms: ovals, some with a light eye
    rng = np.random.RandomState(seed + 4)
    for _ in range(9):
        clat, clon = rng.uniform(-0.9, 0.9), rng.uniform(-np.pi, np.pi)
        rx, ry = rng.uniform(0.12, 0.32), rng.uniform(0.05, 0.1)
        dlon = np.angle(np.exp(1j * (lon - clon)))
        e = (dlon * np.cos(clat) / rx) ** 2 + ((lat - clat) / ry) ** 2
        storm = np.exp(-e * 1.6)
        ring = np.exp(-((np.sqrt(e) - 1.0) / 0.25) ** 2)
        h = h + rng.choice([-1, 1]) * 0.9 * storm + 0.35 * ring
    return even(h)


def facets(p, seed=90):
    centres = fib_points(110, seed, jitter=0.5)
    q = onto_sphere(warp(p, 0.03, 4.0, seed + 1))
    f1, f2, idx = voronoi(q, centres)
    rng = np.random.RandomState(seed + 2)
    tilt = rng.normal(size=(len(centres), 3)).astype(np.float32) * 2.2
    base = rng.uniform(0.05, 0.62, len(centres)).astype(np.float32)
    face = base[idx] + np.einsum("...k,...k->...", p - centres[idx], tilt[idx]) * 0.35
    face = np.clip(face, 0.02, 0.7)
    vein = 1 - ss(0.0, 0.022, f2 - f1)
    h = face * (1 - vein) + vein * (0.9 + 0.1 * (fbm(p, 10.0, 3, seed + 3) * 0.5 + 0.5))
    return np.clip(h, 0, 1).astype(np.float32)


def swirl(p, n, seed, strength=4.5):
    """p turned round n random axes, the more the nearer each axis (cyclones)."""
    rng = np.random.RandomState(seed)
    q = p.copy()
    for _ in range(n):
        k = rng.normal(size=3).astype(np.float32)
        k /= np.linalg.norm(k)
        r = rng.uniform(0.18, 0.42)
        d = np.arccos(np.clip(q @ k, -1, 1))
        th = (strength * rng.choice([-1, 1]) * np.clip(1 - d / r, 0, 1) ** 2)[..., None]
        kx = np.cross(np.broadcast_to(k, q.shape), q)
        q = q * np.cos(th) + kx * np.sin(th) + k * (q @ k)[..., None] * (1 - np.cos(th))
    return q.astype(np.float32)


def clouds(size):
    w, h = size, size // 2
    p, lat, lon = sphere(w, h)
    q = swirl(p, 7, 320)
    q = warp(q, 0.35, 1.7, 300)
    streak = np.stack([q[..., 0], q[..., 1] * 3.4, q[..., 2]], -1)
    base = fbm(warp(streak, 0.28, 2.3, 310), 1.9, 7, 311)
    c = base + 0.22 * fbm(p, 8.5, 4, 312)
    thr = np.percentile(c, 60)
    spread = np.std(c) * 0.75
    a = ss(thr - spread * 0.3, thr + spread, c) * 0.94
    rgba = np.zeros((h, w, 4), np.float32)
    rgba[..., :3] = (0.86 + 0.14 * a)[..., None]
    rgba[..., 3] = a
    return rgba


def make(index, size):
    w, h = size, size // 2
    p, lat, lon = sphere(w, h)
    if index == 0:
        return continents(p)
    if index == 1:
        return islands(p)
    if index == 2:
        return craters(p)
    if index == 3:
        return cells(p)
    if index == 4:
        return swirls(p)
    if index == 5:
        return dunes(p)
    if index == 6:
        return bands(p, lat, lon)
    return facets(p)


# ------------------------------------------------------------------------------------------------- the preview

RAMPS = {  # (height, hex colour), the kind of planet each map is used for
    0: [(0, "#123a78"), (0.46, "#2f7fc1"), (0.5, "#e8d9a0"), (0.54, "#4f9a45"), (0.75, "#3d6e33"), (0.88, "#8a7458"), (0.95, "#f4f4ff")],
    1: [(0, "#0b3a6b"), (0.55, "#1f78b4"), (0.7, "#5ec8e5"), (0.72, "#f1e3a8"), (0.78, "#5fae4f"), (1, "#9bd27c")],
    2: [(0, "#4a4440"), (0.5, "#8d857b"), (1, "#d9d2c6")],
    3: [(0, "#ffd34a"), (0.1, "#ff5a1f"), (0.2, "#4a1a12"), (0.5, "#2b1714"), (1, "#5a4038")],
    4: [(0, "#1d6b5b"), (0.25, "#2c8c6a"), (0.29, "#1f5e2a"), (0.6, "#3f8f3a"), (1, "#a6d067")],
    5: [(0, "#7a3e1c"), (0.35, "#c9762f"), (0.7, "#e6a75a"), (1, "#f7dca6")],
    6: [(0, "#a3643c"), (0.25, "#e6c49a"), (0.5, "#c47a4a"), (0.75, "#f2e3c9"), (1, "#b5824e")],
    7: [(0, "#3b1f5e"), (0.55, "#7b4fb5"), (0.78, "#e48fff"), (1, "#ffffff")],
}


def ramp_colours(index, hmap):
    keys = RAMPS[index]
    xs = np.array([k[0] for k in keys], np.float32)
    cols = np.array([[int(k[1][i:i + 2], 16) / 255 for i in (1, 3, 5)] for k in keys], np.float32)
    out = np.stack([np.interp(hmap, xs, cols[:, c]) for c in range(3)], -1)
    return out.astype(np.float32)


def ball(hmap, index, size=300):
    """The map on a lit ball (seen from the front, light from the top left), bumped like the game's shader does."""
    hh, ww = hmap.shape
    j, i = np.mgrid[0:size, 0:size].astype(np.float32)
    x, y = (i + 0.5) / size * 2 - 1, 1 - (j + 0.5) / size * 2
    r2 = x * x + y * y
    inside = r2 < 1
    z = np.sqrt(np.clip(1 - r2, 0, 1))
    n = np.stack([x, y, -z], -1)          # facing the viewer (looking along +z)
    lat = np.arcsin(np.clip(n[..., 1], -1, 1))
    lon = np.arctan2(n[..., 2], n[..., 0]) + 0.6
    u = ((lon + np.pi) / (2 * np.pi)) % 1.0
    v = (np.pi / 2 - lat) / np.pi
    px = np.clip((u * ww).astype(np.int32), 0, ww - 1)
    py = np.clip((v * hh).astype(np.int32), 0, hh - 1)
    gy, gx = np.gradient(hmap.astype(np.float32))
    h = hmap[py, px]
    col = ramp_colours(index, h)
    bump = 0.12 if index != 6 else 0.03
    sea = {0: 0.5, 1: 0.7, 4: 0.27}.get(index, -1)
    land = (h > sea).astype(np.float32)
    east = np.stack([-np.sin(lon), np.zeros_like(lon), np.cos(lon)], -1)
    north = np.cross(n, east)
    gxs, gys = gx[py, px] * ww / (2 * np.pi), -gy[py, px] * hh / np.pi
    nb = n - bump * land[..., None] * (east * gxs[..., None] + north * gys[..., None])
    nb /= np.linalg.norm(nb, axis=-1, keepdims=True)
    light = np.array([-0.55, 0.55, -0.63], np.float32)
    light /= np.linalg.norm(light)
    ndl = np.clip(nb @ light, 0, 1)
    shade = 0.08 + 0.92 * ndl
    rim = (1 - z) ** 2.5 * 0.6
    rgb = col * shade[..., None] + rim[..., None] * np.array([0.6, 0.8, 1.0], np.float32)
    rgb = np.where(inside[..., None], rgb, 0.03)
    return (np.clip(rgb, 0, 1) * 255).astype(np.uint8)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--size", type=int, default=2048)
    ap.add_argument("--preview", action="store_true")
    ap.add_argument("--only", default="", help="comma-separated map numbers: preview those only, write nothing")
    ap.add_argument("--clouds", action="store_true", help="only the clouds")
    a = ap.parse_args()
    if a.clouds or not a.only:
        t = time.time()
        rgba = clouds(a.size)
        path = os.path.join(ART, "clouds.png")
        cv2.imwrite(path, cv2.cvtColor((np.clip(rgba, 0, 1) * 255 + 0.5).astype(np.uint8), cv2.COLOR_RGBA2BGRA))
        print(f"clouds: {time.time() - t:.1f} s, cover {np.mean(rgba[..., 3] > 0.3):.0%}; wrote {path}")
        if a.clouds:
            return
    which = [int(x) for x in a.only.split(",") if x != ""] or list(range(len(NAMES)))
    maps = []
    for k in which:
        t = time.time()
        m = make(k, a.size)
        maps.append(m)
        print(f"{k} {NAMES[k]}: {time.time() - t:.1f} s, mean {m.mean():.2f}")
    if not a.only:
        stack = np.concatenate(maps, 0)
        path = os.path.join(ART, "planet_heights.png")
        cv2.imwrite(path, (np.clip(stack, 0, 1) * 255 + 0.5).astype(np.uint8))
        print("wrote", path, stack.shape)
    if a.preview or a.only:
        os.makedirs(OUT, exist_ok=True)
        rows = []
        for k, m in zip(which, maps):
            flat = cv2.resize((m * 255).astype(np.uint8), (600, 300), interpolation=cv2.INTER_AREA)
            flat = cv2.cvtColor(flat, cv2.COLOR_GRAY2BGR)
            col = cv2.cvtColor((ramp_colours(k, cv2.resize(m, (600, 300), interpolation=cv2.INTER_AREA)) * 255).astype(np.uint8), cv2.COLOR_RGB2BGR)
            b = cv2.cvtColor(ball(m, k), cv2.COLOR_RGB2BGR)
            b = cv2.copyMakeBorder(b, 0, 0, 0, 0, cv2.BORDER_CONSTANT)
            row = np.concatenate([flat, col, cv2.resize(b, (300, 300))], 1)
            cv2.putText(row, f"{k} {NAMES[k]}", (10, 26), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2)
            rows.append(row)
        sheet = np.concatenate(rows, 0)
        path = os.path.join(OUT, "heights.png")
        cv2.imwrite(path, sheet)
        print("wrote", path)


if __name__ == "__main__":
    main()
