"""Yjet!'s pictures as code (2026-11-14): the planets' maps, the landing target, and the HUD's
sheet (the move figures, icons, the planets small) in Lunapark!'s and Vishu!'s house style (the sky is tools/space/sky.py's) (tools/dress/draw.py: smooth
shapes drawn 4x supersampled, soft "pillow" shading, plum outlines) and the brand's palette.

    motion-service-py/.venv/Scripts/python tools/space/art.py [--preview]

Outputs in unity/MoveItGames/Assets/Games/Space/Art:
    planet_<kind>.png       1024 x 512 equirectangular maps (rgb; alpha = how much it glows: lava, the star planet)
    rings.png               the ringed giant's rings (a band across, alpha)
    target.png              the landing pad's target: bullseye, second and third ring (the rules' 0.7 / 1.6 / 2.6 m)
    space_ui.png + .json    the HUD's sheet and its named rectangles (pixels, y down)
--preview also writes tools/space/out/: every planet as a lit ball, the sheet on the night sky.
"""
import argparse
import json
import math
import os
import sys

import cv2
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "dress"))
from draw import INK, Piece, ellipse_pts, hexc, star_pts  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
ART = os.path.join(REPO, "unity", "MoveItGames", "Assets", "Games", "Space", "Art")
OUT = os.path.join(HERE, "out")

W, H = 1024, 512

# ------------------------------------------------------------------------------------------------- noise on the sphere

_TABLE = np.random.RandomState(7).rand(1 << 14).astype(np.float32)


def _hash(ix, iy, iz, seed):
    h = (ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791) ^ (seed * 2654435761)
    return _TABLE[h & ((1 << 14) - 1)]


def vnoise(x, y, z, seed=0):
    """Smooth value noise in 3D (0..1)."""
    ix, iy, iz = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64), np.floor(z).astype(np.int64)
    fx, fy, fz = x - ix, y - iy, z - iz
    ux, uy, uz = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy), fz * fz * (3 - 2 * fz)

    def L(a, b, t):
        return a + (b - a) * t
    c = {}
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                c[(dx, dy, dz)] = _hash(ix + dx, iy + dy, iz + dz, seed)
    x00 = L(c[(0, 0, 0)], c[(1, 0, 0)], ux)
    x10 = L(c[(0, 1, 0)], c[(1, 1, 0)], ux)
    x01 = L(c[(0, 0, 1)], c[(1, 0, 1)], ux)
    x11 = L(c[(0, 1, 1)], c[(1, 1, 1)], ux)
    return L(L(x00, x10, uy), L(x01, x11, uy), uz)


def fbm(p, scale=1.0, octaves=5, seed=0, gain=0.5):
    x, y, z = p[..., 0] * scale, p[..., 1] * scale, p[..., 2] * scale
    total, amp, norm = 0.0, 1.0, 0.0
    for o in range(octaves):
        total = total + amp * vnoise(x, y, z, seed + o * 31)
        norm += amp
        x, y, z = x * 2.03 + 11.7, y * 2.03 + 3.1, z * 2.03 + 7.9
        amp *= gain
    return total / norm


def sphere(w=W, h=H):
    """Every pixel of an equirectangular map: its point on the unit sphere (y up), its latitude and longitude."""
    j, i = np.mgrid[0:h, 0:w].astype(np.float32)
    lon = (i + 0.5) / w * 2 * np.pi - np.pi
    lat = np.pi / 2 - (j + 0.5) / h * np.pi
    p = np.stack([np.cos(lat) * np.cos(lon), np.sin(lat), np.cos(lat) * np.sin(lon)], -1).astype(np.float32)
    return p, lat, lon


def ss(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def mix(a, b, t):
    t = np.asarray(t, np.float32)[..., None] if np.ndim(t) else t
    return np.asarray(a, np.float32) * (1 - t) + np.asarray(b, np.float32) * t


def solid(c):
    return np.broadcast_to(np.array(hexc(c), np.float32), (H, W, 3)).copy()


def spots(p, n, rmin, rmax, seed):
    """n random caps on the sphere: (distance in radians to the nearest's centre / its radius, its index)."""
    rng = np.random.RandomState(seed)
    c = rng.normal(size=(n, 3))
    c /= np.linalg.norm(c, axis=1, keepdims=True)
    r = rng.uniform(rmin, rmax, n)
    best = np.full(p.shape[:2], 9.0, np.float32)
    idx = np.zeros(p.shape[:2], np.int32)
    flat = p.reshape(-1, 3)
    for k in range(n):
        d = np.arccos(np.clip(flat @ c[k], -1, 1)).reshape(p.shape[:2]) / r[k]
        better = d < best
        best = np.where(better, d, best)
        idx = np.where(better, k, idx)
    return best, idx


# ------------------------------------------------------------------------------------------------- the planets

def home(p, lat, lon):
    # land round the north pole (the launch pad stands there: a meadow, not an ice cap), snow only in the south
    e = fbm(p, 1.7, 6, seed=3) + 0.16 * ss(0.75, 1.35, lat)
    rgb = mix(solid("#2A6FD6"), solid("#31C6F4"), ss(0.42, 0.52, e))
    land = ss(0.525, 0.535, e)
    beach = ss(0.525, 0.53, e) * (1 - ss(0.54, 0.55, e))
    forest = ss(0.55, 0.58, fbm(p, 3.1, 4, seed=8))
    green = mix(solid("#6FD66A"), solid("#3FA650"), forest)
    green = mix(green, solid("#86C95A"), ss(0.6, 0.7, e) * 0.6)
    rgb = mix(rgb, green, land)
    rgb = mix(rgb, solid("#F2D49B"), beach)
    snow = ss(1.12, 1.18, -lat + 0.25 * fbm(p, 4, 3, seed=5))
    rgb = mix(rgb, solid("#FFFFFF"), snow)
    return rgb, np.zeros((H, W), np.float32)


def moon(p, lat, lon):
    rgb = mix(solid("#B9B3CB"), solid("#958EA9"), ss(0.46, 0.4, fbm(p, 1.5, 5, seed=11)))
    rgb = mix(rgb, solid("#CFCAE0"), ss(0.6, 0.68, fbm(p, 5, 3, seed=12)) * 0.5)
    for n, rmin, rmax, seed in ((14, 0.12, 0.26, 21), (40, 0.04, 0.1, 22)):
        d, _ = spots(p, n, rmin, rmax, seed)
        floor = ss(0.95, 0.8, d)
        rim = ss(0.75, 0.92, d) * ss(1.25, 1.0, d)
        rgb = mix(rgb, solid("#857E99"), floor * 0.85)
        rgb = mix(rgb, solid("#E0DCEC"), rim * 0.8)
    return rgb, np.zeros((H, W), np.float32)


def candy(p, lat, lon):
    warp = fbm(p, 1.4, 4, seed=31)
    band = np.sin(lat * 10 + lon * 1.5 + warp * 7)
    rgb = solid("#FF8FB0")
    rgb = mix(rgb, solid("#FFF1F6"), ss(0.55, 0.65, band))
    rgb = mix(rgb, solid("#FF5A8A"), ss(-0.62, -0.72, band))
    # frosting over the north, a wavy dripping edge
    edge = 0.72 + 0.09 * np.sin(lon * 9) + 0.05 * fbm(p, 6, 3, seed=33)
    frost = ss(edge - 0.02, edge + 0.02, lat)
    rgb = mix(rgb, solid("#FFF6E8"), frost)
    # sprinkles everywhere but the frosting has the most
    d, idx = spots(p, 260, 0.018, 0.03, 34)
    cols = np.array([hexc(c) for c in ("#FFC93C", "#31C6F4", "#6FD66A", "#7B61FF", "#FFFFFF", "#FF5A4E")], np.float32)
    rgb = mix(rgb, cols[idx % len(cols)], ss(1.0, 0.8, d))
    return rgb, np.zeros((H, W), np.float32)


def ice(p, lat, lon):
    rgb = solid("#BDEBFF")
    rgb = mix(rgb, solid("#FFFFFF"), ss(0.54, 0.6, fbm(p, 2.2, 5, seed=41)))
    rgb = mix(rgb, solid("#9FE2FF"), ss(0.36, 0.3, fbm(p, 1.6, 4, seed=42)))
    crack = np.abs(fbm(p, 3.3, 4, seed=43) - 0.5)
    rgb = mix(rgb, solid("#7FC8F0"), ss(0.012, 0.004, crack))
    return rgb, np.zeros((H, W), np.float32)


def jungle(p, lat, lon):
    e = fbm(p, 2.0, 5, seed=51)
    rgb = mix(solid("#2E9E5B"), solid("#3FA650"), ss(0.4, 0.5, e))
    rgb = mix(rgb, solid("#6FD66A"), ss(0.58, 0.64, e))
    rgb = mix(rgb, solid("#86C95A"), ss(0.66, 0.72, fbm(p, 4.5, 3, seed=52)) * 0.7)
    river = np.abs(fbm(p, 2.6, 4, seed=53) - 0.5)
    rgb = mix(rgb, solid("#2EC4B6"), ss(0.011, 0.005, river))
    rgb = mix(rgb, solid("#2EC4B6"), ss(0.33, 0.3, e))
    d, idx = spots(p, 90, 0.02, 0.035, 54)
    flowers = np.array([hexc(c) for c in ("#FF8FB0", "#FFC93C", "#FFFFFF")], np.float32)
    rgb = mix(rgb, flowers[idx % 3], ss(1.0, 0.75, d) * ss(0.5, 0.56, e))
    return rgb, np.zeros((H, W), np.float32)


def star_planet(p, lat, lon):
    warp = fbm(p, 1.8, 4, seed=61)
    swirl = np.sin(lat * 7 + warp * 9 + lon * 2)
    rgb = mix(solid("#FFC93C"), solid("#FFB020"), ss(0.2, 0.6, swirl))
    bright = ss(0.62, 0.7, fbm(p, 3.5, 4, seed=62))
    rgb = mix(rgb, solid("#FFF3B8"), bright)
    d, _ = spots(p, 60, 0.02, 0.05, 63)
    sparkle = ss(1.0, 0.5, d)
    rgb = mix(rgb, solid("#FFFFFF"), sparkle)
    glow = 0.35 + 0.65 * np.maximum(bright, sparkle)
    return rgb, glow.astype(np.float32)


def ringed(p, lat, lon):
    t = lat * 3.4 + 0.35 * fbm(p, 2.5, 5, seed=71)
    bands = (np.sin(t * 6) + 1) / 2
    rgb = mix(solid("#FFF1D6"), solid("#F2D49B"), ss(0.3, 0.6, bands))
    rgb = mix(rgb, solid("#DB6A3D"), ss(0.82, 0.92, bands) * 0.85)
    rgb = mix(rgb, solid("#D9A066"), ss(0.1, 0.02, bands) * 0.8)
    # the great storm
    c = np.array([math.cos(-0.35) * math.cos(0.8), math.sin(-0.35), math.cos(-0.35) * math.sin(0.8)], np.float32)
    d = np.arccos(np.clip(p @ c, -1, 1))
    storm = ss(0.24, 0.16, d * np.array(1.0) + np.abs(lat + 0.35) * 0.9)
    rgb = mix(rgb, solid("#FF7A4E"), storm)
    rgb = mix(rgb, solid("#FFB08A"), ss(0.12, 0.05, d + np.abs(lat + 0.35) * 0.9))
    return rgb, np.zeros((H, W), np.float32)


def volcano(p, lat, lon):
    rgb = mix(solid("#3E3450"), solid("#5A4C6C"), ss(0.45, 0.6, fbm(p, 2.0, 5, seed=81)))
    lava = np.abs(fbm(p, 2.4, 5, seed=82) - 0.5)
    river = ss(0.02, 0.006, lava)
    d, _ = spots(p, 18, 0.05, 0.11, 83)
    pools = ss(1.0, 0.75, d)
    hot = np.maximum(river, pools)
    rgb = mix(rgb, solid("#FF8A3D"), hot)
    rgb = mix(rgb, solid("#FFD25A"), ss(0.6, 1.0, hot) * ss(0.9, 0.5, d))
    return rgb, hot.astype(np.float32)


def crystal(p, lat, lon):
    d, idx = spots(p, 70, 0.3, 0.6, 91)
    rng = np.random.RandomState(92)
    shades = rng.uniform(0, 1, 70).astype(np.float32)
    rgb = mix(solid("#7B61FF"), solid("#C9A8FF"), shades[idx])
    # the cells' edges: the second nearest point close as well
    edges = np.zeros((H, W), np.float32)
    flat = p.reshape(-1, 3)
    rngc = np.random.RandomState(91)
    c = rngc.normal(size=(70, 3))
    c /= np.linalg.norm(c, axis=1, keepdims=True)
    dots = flat @ c.T
    top2 = np.sort(dots, axis=1)[:, -2:]
    edge = (top2[:, 1] - top2[:, 0]).reshape(H, W)
    rgb = mix(rgb, solid("#EDE2FF"), ss(0.03, 0.005, edge))
    return rgb, (ss(0.03, 0.005, edge) * 0.6).astype(np.float32)


PLANETS = {"home": home, "moon": moon, "candy": candy, "ice": ice, "jungle": jungle, "star": star_planet,
           "ringed": ringed, "volcano": volcano, "crystal": crystal}


def clouds(p):
    c = fbm(p, 2.4, 6, seed=101)
    a = ss(0.56, 0.66, c) * 0.95
    rgba = np.zeros((H, W, 4), np.float32)
    rgba[..., :3] = 1.0
    rgba[..., 3] = a
    return rgba


def rings_map():
    w, h = 512, 32
    x = (np.arange(w, dtype=np.float32) + 0.5) / w
    rng = np.random.RandomState(5)
    bands = np.zeros(w, np.float32)
    for k in range(18):
        c, r, s = rng.uniform(0.05, 0.95), rng.uniform(0.01, 0.06), rng.uniform(0.3, 1.0)
        bands = np.maximum(bands, s * ss(r, 0, np.abs(x - c)))
    a = np.clip(0.25 + 0.75 * bands, 0, 1) * ss(0.0, 0.06, x) * ss(1.0, 0.92, x)
    gap = ss(0.012, 0.0, np.abs(x - 0.62))
    a *= 1 - gap
    col = mix(np.array(hexc("#FFF1D6"), np.float32)[None, :].repeat(w, 0), np.array(hexc("#D9A066"), np.float32)[None, :].repeat(w, 0), bands)
    rgba = np.zeros((h, w, 4), np.float32)
    rgba[..., :3] = col[None]
    rgba[..., 3] = a[None]
    return rgba


# ------------------------------------------------------------------------------------------------- the sky's pictures

def soft_cloud(size, seed, colors):
    """A soft cloud of colour (alpha): a few big lobes, brightest and palest in the middle, smooth (blurred: no grain)."""
    j, i = np.mgrid[0:size, 0:size].astype(np.float32)
    x, y = (i + 0.5) / size * 2 - 1, (j + 0.5) / size * 2 - 1
    r = np.sqrt(x * x + y * y)
    p = np.stack([x * 1.1, y * 1.1, np.full_like(x, seed * 0.37)], -1)
    n = fbm(p, 1.0, 3, seed=seed, gain=0.45)
    n2 = fbm(p, 1.7, 3, seed=seed + 5, gain=0.45)
    a = np.clip(n * 2.1 - 0.62, 0, 1) * ss(1.0, 0.35, r + 0.3 * (n2 - 0.5))
    a = cv2.GaussianBlur(a.astype(np.float32), (0, 0), size / 40) ** 1.1
    c0, c1, c2 = [np.array(hexc(c), np.float32) for c in colors]
    t = np.clip(n2 * 1.8 - 0.4, 0, 1)
    rgb = c0 * (1 - t)[..., None] + c1 * t[..., None]
    core = ss(0.55, 0.0, r) * a
    rgb = rgb * (1 - core)[..., None] + c2 * core[..., None]
    return np.dstack([rgb, np.clip(a * 0.95, 0, 1)])


def galaxy(size=512):
    j, i = np.mgrid[0:size, 0:size].astype(np.float32)
    x, y = (i + 0.5) / size * 2 - 1, (j + 0.5) / size * 2 - 1
    y = y * 1.8  # seen tilted
    r = np.sqrt(x * x + y * y) + 1e-5
    a = np.arctan2(y, x)
    arms = (np.cos(2 * (a - 3.2 * np.log(r + 0.05))) + 1) / 2
    p = np.stack([x * 3, y * 3, np.zeros_like(x)], -1)
    n = fbm(p, 1.0, 4, seed=111)
    core = ss(0.35, 0.0, r)
    body = ss(0.95, 0.15, r) * (0.25 + 0.75 * arms ** 2) * (0.6 + 0.6 * n)
    alpha = np.clip(core + body * 0.85, 0, 1)
    c_arm, c_core = np.array(hexc("#B8C0FF"), np.float32), np.array(hexc("#FFF4D6"), np.float32)
    rgb = c_arm * (1 - core)[..., None] + c_core * core[..., None]
    return np.dstack([rgb, alpha])


def target(size=512):
    """The landing target: rings at the rules' 0.7, 1.6 and 2.6 (of 2.6 = the edge): a gold bullseye with a star, white,
    then coral, dark lines between, a soft glow out."""
    j, i = np.mgrid[0:size, 0:size].astype(np.float32)
    x, y = (i + 0.5) / size * 2 - 1, (j + 0.5) / size * 2 - 1
    r = np.sqrt(x * x + y * y) / 0.96
    px = 2.0 / size / 0.96
    r1, r2 = 0.7 / 2.6, 1.6 / 2.6
    sun, white, coral, ink = (np.array(hexc(c), np.float32) for c in ("#FFC93C", "#FFFFFF", "#FF5A4E", "#2B1E3D"))
    rgb = coral * np.ones((size, size, 1), np.float32)
    rgb = mix(rgb, white, ss(r2 + px, r2 - px, r))
    rgb = mix(rgb, sun, ss(r1 + px, r1 - px, r))
    for edge in (r1, r2, 1.0):
        rgb = mix(rgb, ink, ss(0.012 + px, 0.012 - px, np.abs(r - edge)) * 0.85)
    # a five-pointed star in the middle
    img = Image.new("L", (size * 4, size * 4), 0)
    pts = star_pts(size * 2, size * 2, size * 4 * 0.11, inner=0.45)
    ImageDraw.Draw(img).polygon(pts, fill=255)
    star = cv2.resize(np.asarray(img, np.float32) / 255, (size, size), interpolation=cv2.INTER_AREA)
    rgb = mix(rgb, white, star)
    alpha = ss(1.0 + px, 1.0 - px, r)
    return np.dstack([rgb, alpha])


# ------------------------------------------------------------------------------------------------- the HUD's sheet

def cell(name, w, h=None):
    h = h or w
    return Piece(name, w, h, (w / 2, h / 2), min(w, h) / 2)


def figure(name, pose):
    """A little white figure doing a move (the HUD tints it): a round head, a rounded body, thick limbs, plum outlines.
    Units: the cell's half size; y down."""
    p = cell(name, 192)
    o = 0.075   # outline
    head = (0.0, -0.58)
    neck, hip = (0.0, -0.36), (0.0, 0.18)
    arms = {
        "rest": [((-0.16, -0.3), (-0.3, 0.02), (-0.33, 0.24)), ((0.16, -0.3), (0.3, 0.02), (0.33, 0.24))],
        "wings": [((-0.16, -0.3), (-0.5, -0.3), (-0.84, -0.3)), ((0.16, -0.3), (0.5, -0.3), (0.84, -0.3))],
        "rocket": [((-0.14, -0.32), (-0.2, -0.72), (-0.06, -0.98)), ((0.14, -0.32), (0.2, -0.72), (0.06, -0.98))],
        "tilt_left": [((-0.16, -0.3), (-0.48, -0.12), (-0.78, 0.08)), ((0.16, -0.3), (0.48, -0.48), (0.78, -0.68))],
        "tilt_right": [((-0.16, -0.3), (-0.48, -0.48), (-0.78, -0.68)), ((0.16, -0.3), (0.48, -0.12), (0.78, 0.08))],
        "duck": [((-0.16, -0.06), (-0.42, 0.06), (-0.62, 0.0)), ((0.16, -0.06), (0.42, 0.06), (0.62, 0.0))],
        "jump": [((-0.16, -0.36), (-0.42, -0.62), (-0.5, -0.86)), ((0.16, -0.36), (0.42, -0.62), (0.5, -0.86))],
        # Yjet!'s music trip (2026-11-14): one hand reaching up and out to catch a star, a step to a side
        "catch": [((-0.16, -0.3), (-0.3, 0.02), (-0.33, 0.24)), ((0.16, -0.3), (0.48, -0.5), (0.78, -0.74))],
        "step": [((-0.16, -0.3), (-0.44, -0.12), (-0.66, 0.02)), ((0.16, -0.3), (0.44, -0.12), (0.66, 0.02))],
    }[pose]
    legs = [((-0.1, 0.18), (-0.13, 0.5), (-0.14, 0.86)), ((0.1, 0.18), (0.13, 0.5), (0.14, 0.86))]
    shift = 0.0
    if pose == "duck":
        head, neck, hip = (0.0, -0.18), (0.0, 0.02), (0.0, 0.42)
        legs = [((-0.1, 0.42), (-0.4, 0.5), (-0.24, 0.86)), ((0.1, 0.42), (0.4, 0.5), (0.24, 0.86))]
    if pose == "jump":
        shift = -0.14
        legs = [((-0.1, 0.18), (-0.24, 0.42), (-0.16, 0.64)), ((0.1, 0.18), (0.24, 0.42), (0.16, 0.64))]
    if pose == "step":
        legs = [((-0.1, 0.18), (-0.3, 0.52), (-0.5, 0.86)), ((0.1, 0.18), (0.22, 0.52), (0.3, 0.86))]
    # everything a little smaller and lower, so raised arms stay in the cell
    k, dy = 0.84, 0.08

    def sh(pt):
        return (pt[0] * k, (pt[1] + shift) * k + dy)
    white = (1.0, 1.0, 1.0)
    limbs = [[sh(a) for a in arm] for arm in arms] + [[sh(a) for a in leg] for leg in legs]
    # outlines first (thicker), then the white
    for pts in limbs:
        p.paint(p.line_mask(pts, (0.17 + 2 * o) * k), color=INK)
    mid = sh((0.0, (neck[1] + hip[1]) / 2))
    p.shape(ellipse_pts(mid[0], mid[1], 0.2 * k, ((hip[1] - neck[1]) / 2 + 0.08) * k, 48), color=white, outline=o * k)
    hc = sh(head)
    p.shape(ellipse_pts(hc[0], hc[1], 0.2 * k, 0.2 * k, 48), color=white, outline=o * k)
    for pts in limbs:
        p.paint(p.line_mask(pts, 0.17 * k), color=white)
    if pose == "jump":
        for x in (-0.3, 0.0, 0.3):
            p.paint(p.line_mask([sh((x, 0.92)), sh((x, 1.06))], 0.06 * k), color=white, alpha=0.8)
    return p


def star_icon(name="star", color="#FFC93C"):
    p = cell(name, 96)
    p.shape(star_pts(0.0, 0.05, 0.9, inner=0.48), color=hexc(color), outline=0.09, shade=0.3, shade_radius=0.3)
    return p


def arrow_icon():
    """A chunky arrow pointing right (the HUD mirrors it for the left): step this way."""
    p = cell("arrow", 128)
    pts = [(-0.85, -0.22), (0.1, -0.22), (0.1, -0.6), (0.88, 0.0), (0.1, 0.6), (0.1, 0.22), (-0.85, 0.22)]
    p.shape(pts, color=(1.0, 1.0, 1.0), outline=0.08)
    return p


def ring_icon():
    p = cell("ring", 96)
    outer = p.mask(ellipse_pts(0, 0, 0.88, 0.88, 64))
    inner = p.mask(ellipse_pts(0, 0, 0.55, 0.55, 64))
    p.fill(np.clip(outer - inner, 0, 1), color=hexc("#FFC93C"), outline=0.08, shade=0.25, shade_radius=0.2)
    return p


def alien_icon(i):
    body, belly, eyes = [("#6FD66A", "#9FF2C8", 1), ("#FF8FB0", "#FFF1D6", 2), ("#31C6F4", "#BDEBFF", 3), ("#7B61FF", "#C9A8FF", 2)][i]
    p = cell(f"alien{i}", 112)
    p.shape(ellipse_pts(0, 0.1, 0.78, 0.8, 64), color=hexc(body), outline=0.08, shade=0.3, shade_radius=0.4)
    p.shape(ellipse_pts(0, 0.45, 0.48, 0.32, 48), color=hexc(belly), outline=0.0)
    xs = {1: [0.0], 2: [-0.28, 0.28], 3: [-0.36, 0.0, 0.36]}[eyes]
    r = {1: 0.3, 2: 0.2, 3: 0.16}[eyes]
    for x in xs:
        y = -0.16 if eyes != 3 or x != 0.0 else -0.3
        p.shape(ellipse_pts(x, y, r, r * 1.1, 32), color=(1, 1, 1), outline=0.05)
        p.shape(ellipse_pts(x, y + r * 0.15, r * 0.55, r * 0.62, 24), color=hexc("#141938"), outline=0.0)
        p.shape(ellipse_pts(x - r * 0.2, y - r * 0.2, r * 0.18, r * 0.2, 16), color=(1, 1, 1), outline=0.0)
    p.shape(ellipse_pts(0, 0.3, 0.12, 0.08, 24), color=hexc("#141938"), outline=0.0)
    return p


def planet_icon(name, rgb):
    """A planet small: its map on a ball, lit from the top left, an outline."""
    size = 128
    p = cell(f"planet_{name}", size)
    s = size * 4
    j, i = np.mgrid[0:s, 0:s].astype(np.float32)
    x, y = (i + 0.5) / s * 2 - 1, (j + 0.5) / s * 2 - 1
    x, y = x / 0.86, y / 0.86
    r2 = x * x + y * y
    inside = r2 < 1
    z = np.sqrt(np.clip(1 - r2, 0, 1))
    lat = np.arcsin(np.clip(-y, -1, 1))
    lon = np.arctan2(x, z) + 0.6
    u = ((lon + np.pi) / (2 * np.pi) % 1.0) * (W - 1)
    v = (0.5 - lat / np.pi) * (H - 1)
    tex = cv2.remap(rgb, u.astype(np.float32), v.astype(np.float32), cv2.INTER_LINEAR)
    light = np.clip(0.55 + 0.65 * (-0.5 * x - 0.55 * y + 0.65 * z), 0.35, 1.15)
    col = np.clip(tex * light[..., None], 0, 1)
    rim = ss(0.75, 1.0, np.sqrt(r2)) * inside
    col = col * (1 - 0.25 * rim[..., None]) + 0.25 * rim[..., None] * np.array(hexc("#B8E6FF"), np.float32)
    m = cv2.resize(inside.astype(np.float32), (p.W, p.H), interpolation=cv2.INTER_AREA)
    c = cv2.resize(col.astype(np.float32), (p.W, p.H), interpolation=cv2.INTER_AREA)
    p.fill(m, color=c, outline=0.07)
    return p


def blocks():
    """A white disc and a white block (the HUD's panels and bars are drawn from these, tinted)."""
    out = []
    p = cell("disc", 128)
    p.paint(p.mask(ellipse_pts(0, 0, 0.98, 0.98, 96)), color=(1, 1, 1))
    out.append(p)
    p = cell("white", 24)
    p.paint(np.ones((p.H, p.W), np.float32), color=(1, 1, 1))
    out.append(p)
    p = Piece("capsule", 256, 64, (128, 32), 32)
    p.paint(p.mask([(x, y) for x, y in capsule_pts(3.9, 0.92)]), color=(1, 1, 1))
    out.append(p)
    p = cell("glow", 128)
    j, i = np.mgrid[0:p.H, 0:p.W].astype(np.float32)
    r = np.sqrt(((i + 0.5) / p.W * 2 - 1) ** 2 + ((j + 0.5) / p.H * 2 - 1) ** 2)
    p.paint(np.clip(1 - r, 0, 1) ** 2, color=(1, 1, 1))
    out.append(p)
    return out


def capsule_pts(half_len, r, n=24):
    pts = []
    for k in range(n + 1):
        a = -math.pi / 2 + math.pi * k / n
        pts.append((half_len - r + math.cos(a) * r, math.sin(a) * r))
    for k in range(n + 1):
        a = math.pi / 2 + math.pi * k / n
        pts.append((-half_len + r + math.cos(a) * r, math.sin(a) * r))
    return pts


def pack(images, width=1024, pad=10):
    order = sorted(images, key=lambda n: -images[n].shape[0])
    x = y = shelf = 0
    rects = {}
    for name in order:
        h, w = images[name].shape[:2]
        if x + w + pad > width:
            x = 0
            y += shelf + pad
            shelf = 0
        rects[name] = (x + pad, y + pad, w, h)
        x += w + pad
        shelf = max(shelf, h)
    return rects, (y + shelf + pad * 2 + 15) // 16 * 16


# ------------------------------------------------------------------------------------------------- main

def save_rgba(path, rgba):
    Image.fromarray((np.clip(rgba, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA").save(path, optimize=True)


def main():
    ap = argparse.ArgumentParser(description="Yjet!'s pictures as code")
    ap.add_argument("--preview", action="store_true")
    ap.add_argument("--only", nargs="*", help="planets to redraw (names), nothing else")
    ap.add_argument("--hud", action="store_true", help="only the HUD's sheet (the planets' icons from their maps on disk)")
    args = ap.parse_args()
    os.makedirs(ART, exist_ok=True)
    maps = {}
    if args.hud:
        for name in PLANETS:
            img = np.asarray(Image.open(os.path.join(ART, f"planet_{name}.png")).convert("RGB"), np.float32) / 255.0
            maps[name] = img
    else:
        p, lat, lon = sphere()
        for name, make in PLANETS.items():
            if args.only and name not in args.only:
                continue
            rgb, glow = make(p, lat, lon)
            maps[name] = rgb
            save_rgba(os.path.join(ART, f"planet_{name}.png"), np.dstack([rgb, glow]))
            print("  planet", name)
        if args.only:
            return
        save_rgba(os.path.join(ART, "rings.png"), rings_map())
        save_rgba(os.path.join(ART, "target.png"), target())
    # the HUD's sheet
    cells = [figure(f"pose_{k}", k) for k in ("rest", "wings", "rocket", "tilt_left", "tilt_right", "duck", "jump", "catch", "step")]
    cells += [star_icon(), ring_icon(), arrow_icon()] + [alien_icon(i) for i in range(4)] + blocks()
    cells += [planet_icon(n, maps[n]) for n in ("home", "moon", "candy", "ice", "jungle", "star")]
    images = {c.name: c.image()[0] for c in cells}
    rects, height = pack(images)
    sheet = np.zeros((height, 1024, 4), np.uint8)
    for name, (x, y, w, h) in rects.items():
        sheet[y:y + h, x:x + w] = images[name]
    Image.fromarray(sheet, "RGBA").save(os.path.join(ART, "space_ui.png"), optimize=True)
    with open(os.path.join(ART, "space_ui.json"), "w", encoding="utf-8") as f:
        json.dump({"size": [1024, height], "sprites": [{"name": n, "rect": list(r)} for n, r in sorted(rects.items())]}, f, indent=1)
    print(f"  {len(rects)} HUD pictures -> space_ui.png (1024 x {height})")
    if args.preview:
        os.makedirs(OUT, exist_ok=True)
        balls = [planet_icon(n, maps[n]).image()[0] for n in maps]
        strip = np.zeros((128, 128 * len(balls), 4), np.uint8)
        for k, b in enumerate(balls):
            strip[:, k * 128:(k + 1) * 128] = b
        bg = np.zeros_like(strip)
        bg[..., :3] = (np.array(hexc("#141938")) * 255).astype(np.uint8)
        bg[..., 3] = 255
        a = strip[..., 3:4].astype(np.float32) / 255
        Image.fromarray((strip[..., :3] * a + bg[..., :3] * (1 - a)).astype(np.uint8)).resize((256 * len(balls), 256)).save(os.path.join(OUT, "planets.png"))
        a = sheet[..., 3:4].astype(np.float32) / 255
        bg = np.zeros((height, 1024, 3), np.float32) + np.array(hexc("#2A2F66"), np.float32) * 255
        Image.fromarray((sheet[..., :3] * a + bg * (1 - a)).astype(np.uint8)).save(os.path.join(OUT, "sheet.png"))
        print("  previews in", os.path.relpath(OUT, REPO))


if __name__ == "__main__":
    main()
