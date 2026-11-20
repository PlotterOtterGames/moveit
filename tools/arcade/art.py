"""Lunapark!'s art as code (2026-11-09): every sprite of the six booths, the fun fair at night and the prize shelf, in
Vishu!'s style (tools/dress/draw.py: smooth shapes drawn 4x supersampled, soft "pillow" shading, plum outlines) and the
brand's palette, packed into one sheet (one texture, one material, one draw call for everything in the game).

    motion-service-py/.venv/Scripts/python tools/arcade/art.py [--preview]

Outputs in unity/MoveItGames/Assets/Games/Arcade/Art:
    arcade_sprites.png + arcade_sprites.json   the sheet and its named rectangles (pixels, y down) with pivots (0..1)
    arcade_wall.png                            Muri!'s wall board (512 px); the game cuts the hole with Tundu/Wall
--preview also writes tools/arcade/out/sheet.png: every sprite on the night sky with its name (for looking at).
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
from draw import (INK, WHITE, Piece, blur, catmull, dilate, ellipse_pts, heart_pts, hexc, pillow, rotate,  # noqa: E402
                  star_pts, sym)

REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
ART = os.path.join(REPO, "unity", "MoveItGames", "Assets", "Games", "Arcade", "Art")
OUT = os.path.join(HERE, "out")

SUN, CORAL, SKY, GRAPE, LEAF = hexc("#FFC93C"), hexc("#FF5A4E"), hexc("#30C6F5"), hexc("#7A61FF"), hexc("#70D66B")
NIGHT = hexc("#141938")
CREAM, PINK, BROWN = hexc("#FFF3D6"), hexc("#FF9FB5"), hexc("#8B5A3C")
GOLD, GOLD_DARK = (1.0, 0.8, 0.3), (0.8, 0.52, 0.12)


def cell(name, w, h=None, ppt=None, pivot=(0.5, 0.5)):
    """A w x h cell, its centre at (0, 0); `ppt` pixels per unit (default: half the smaller side = 1 unit), y down."""
    h = h or w
    ppt = ppt or min(w, h) / 2
    p = Piece(name, w, h, (w / 2, h / 2), ppt)
    p.meta["pivot"] = pivot
    return p


def lighter(c, k):
    return tuple(min(1.0, v + (1 - v) * k) for v in c)


def darker(c, k):
    return tuple(v * (1 - k) for v in c)


def glossy(p, pts, color, outline=0.035, shade=0.32, radius=0.18, clip=None, ink=None):
    """A filled shape with pillow shading and a plum outline (the house style)."""
    return p.shape(pts, color=color, outline=outline, shade=shade, shade_radius=radius, ink=ink or INK, clip=clip)


def shine(p, cx, cy, rx, ry, angle=-0.6, alpha=0.75, clip=None):
    m = p.mask(rotate(ellipse_pts(cx, cy, rx, ry, 32), angle, cx, cy))
    if clip is not None:
        m = m * clip
    p.paint(m, color=WHITE, alpha=alpha)


def eyes(p, cx, cy, r, gap, look=(0.0, 0.0), clip=None):
    """Big black bean eyes with two shines each (the chibis' eyes)."""
    for side in (-1, 1):
        x = cx + gap * side
        m = p.mask(ellipse_pts(x + look[0], cy + look[1], r * 0.82, r, 32))
        if clip is not None:
            m = m * clip
        p.paint(m, color=INK)
        p.paint(p.mask(ellipse_pts(x + look[0] - r * 0.3, cy + look[1] - r * 0.38, r * 0.34, r * 0.38, 20)) * (1 if clip is None else clip), color=WHITE)
        p.paint(p.mask(ellipse_pts(x + look[0] + r * 0.28, cy + look[1] + r * 0.36, r * 0.16, r * 0.16, 16)) * (1 if clip is None else clip), color=WHITE)


def blush(p, cx, cy, r, gap, color=PINK, alpha=0.65):
    for side in (-1, 1):
        p.paint(p.mask(ellipse_pts(cx + gap * side, cy, r, r * 0.6, 24)), color=color, alpha=alpha)


def smile(p, cx, cy, w, width=0.035, open_=False):
    if open_:
        p.shape(catmull([(cx - w, cy), (cx, cy + w * 0.9), (cx + w, cy)], True, 8), color=hexc("#5A2236"), outline=width * 0.6)
        p.shape(ellipse_pts(cx, cy + w * 0.55, w * 0.45, w * 0.22, 20), color=hexc("#FF7A8A"), outline=0)
    else:
        p.stroke(catmull([(cx - w, cy - w * 0.15), (cx, cy + w * 0.45), (cx + w, cy - w * 0.15)], False, 8), width, color=INK)


def soft_glow(p, cx, cy, r, color=WHITE, alpha=0.6):
    p.paint(blur(p.circle_mask(cx, cy, r * 0.55), p.units(r * 0.35)), color=color, alpha=alpha)


# ---------------------------------------------------------------------------------------------------- effects and UI

def fx_cells():
    out = []
    g = cell("glow", 128)
    X, Y = g.grid()
    g.paint(np.clip(1 - np.hypot(X, Y), 0, 1) ** 2.0, color=WHITE)
    out.append(g)
    d = cell("dot", 64)
    X, Y = d.grid()
    d.paint(np.clip(1.25 - np.hypot(X, Y) * 1.25, 0, 1) ** 1.2, color=WHITE)
    out.append(d)
    s = cell("spark", 128)
    s.paint(blur(s.circle_mask(0, 0, 0.42), s.units(0.16)), color=WHITE, alpha=0.75)
    s.shape(star_pts(0, 0, 0.86, 0.22, 4), color=WHITE, outline=0)
    s.shape(star_pts(0, 0, 0.5, 0.3, 4, rot=-math.pi / 4), color=WHITE, outline=0, alpha=0.7)
    out.append(s)
    st = cell("star", 128, ppt=56)
    glossy(st, star_pts(0, 0.06, 0.88, 0.47, 5, round_=1), SUN, outline=0.06, shade=0.35, radius=0.2)
    shine(st, -0.22, -0.18, 0.18, 0.09, alpha=0.8)
    out.append(st)
    sw = cell("star_white", 128)
    sw.shape(star_pts(0, 0.06, 0.88, 0.47, 5, round_=1), color=WHITE, outline=0)
    out.append(sw)
    c = cell("confetti", 48, 32)
    c.shape([(-0.9, -0.55), (0.9, -0.55), (0.9, 0.55), (-0.9, 0.55)], color=WHITE, outline=0)
    out.append(c)
    r = cell("ring", 128)
    r.paint(np.clip(r.circle_mask(0, 0, 0.92) - r.circle_mask(0, 0, 0.72), 0, 1), color=WHITE)
    out.append(r)
    h = cell("heart", 96)
    glossy(h, heart_pts(0, 0.05, 0.85), CORAL, outline=0.06)
    shine(h, -0.3, -0.25, 0.16, 0.09)
    out.append(h)
    pf = cell("puff", 128)
    for x, y, rr in ((-0.35, 0.15, 0.42), (0.3, 0.2, 0.45), (0.0, -0.2, 0.5), (-0.55, -0.15, 0.3), (0.58, -0.1, 0.32)):
        pf.paint(pf.circle_mask(x, y, rr), color=WHITE)
    out.append(pf)
    b = cell("burst", 160)
    pts = []
    for k in range(24):
        a = -math.pi / 2 + math.pi * k / 12
        rr = 0.92 if k % 2 == 0 else 0.55 + 0.08 * math.sin(k * 1.7)
        pts.append((math.cos(a) * rr, math.sin(a) * rr))
    b.shape(pts, color=WHITE, outline=0)
    out.append(b)
    sp = cell("splat", 200)
    rng = np.random.default_rng(5)
    blob = [(math.cos(a) * (0.55 + 0.18 * math.sin(a * 5 + 1) + rng.uniform(-0.05, 0.05)),
             math.sin(a) * (0.5 + 0.15 * math.cos(a * 4)) + 0.05) for a in np.linspace(0, 2 * math.pi, 18, endpoint=False)]
    sp.shape(catmull(blob, True, 6), color=WHITE, outline=0)
    for k in range(9):
        a = rng.uniform(0, 2 * math.pi)
        dd = rng.uniform(0.62, 0.9)
        sp.shape(ellipse_pts(math.cos(a) * dd, math.sin(a) * dd, rng.uniform(0.05, 0.1), rng.uniform(0.05, 0.1), 16), color=WHITE, outline=0)
    out.append(sp)
    dr = cell("drop", 48)
    dr.shape(catmull([(0, -0.9), (0.55, 0.15), (0.45, 0.6), (0, 0.8), (-0.45, 0.6), (-0.55, 0.15)], True, 6, sharp=(0,)), color=WHITE, outline=0)
    out.append(dr)
    t = cell("ticket", 160, 96, ppt=48)
    body = [(-1.55, -0.85), (1.55, -0.85), (1.55, 0.85), (-1.55, 0.85)]
    m = t.mask(body)
    for side in (-1, 1):
        m = m * (1 - t.circle_mask(1.55 * side, 0, 0.32))
    t.fill(m, color=CORAL, outline=0.06, shade=0.25, shade_radius=0.2)
    t.paint(np.clip(t.mask([(-1.2, -0.62), (1.2, -0.62), (1.2, 0.62), (-1.2, 0.62)]) - t.mask([(-1.1, -0.52), (1.1, -0.52), (1.1, 0.52), (-1.1, 0.52)]), 0, 1),
            color=lighter(CORAL, 0.55))
    t.shape(star_pts(0, 0.04, 0.42, 0.45, 5, round_=1), color=SUN, outline=0.04)
    for x in (-0.82, 0.82):
        t.paint(t.circle_mask(x, 0, 0.09), color=lighter(CORAL, 0.55))
    out.append(t)
    for name, lit in (("bulb_on", True), ("bulb_off", False)):
        bl = cell(name, 64)
        if lit:
            soft_glow(bl, 0, 0, 0.62, SUN, 0.75)
        glossy(bl, ellipse_pts(0, 0, 0.5, 0.5, 32), lighter(SUN, 0.45) if lit else hexc("#6B5E8A"), outline=0.08, shade=0.3, radius=0.25)
        shine(bl, -0.15, -0.18, 0.15, 0.09, alpha=0.9 if lit else 0.35)
        out.append(bl)
    return out


# ---------------------------------------------------------------------------------------------------- digits

DIGIT_PATHS = {
    "0": [catmull([(0.5, 0.06), (0.86, 0.35), (0.88, 0.8), (0.82, 1.25), (0.5, 1.52), (0.18, 1.25), (0.12, 0.8), (0.14, 0.35)], True, 10)],
    "1": [[(0.24, 0.32), (0.56, 0.08), (0.56, 1.52)]],
    "2": [catmull([(0.14, 0.42), (0.3, 0.12), (0.62, 0.08), (0.84, 0.32), (0.76, 0.66), (0.42, 1.0), (0.13, 1.5)], False, 10)
          + [(0.88, 1.5)]],
    "3": [catmull([(0.14, 0.3), (0.42, 0.07), (0.8, 0.24), (0.74, 0.62), (0.44, 0.76)], False, 10),
          catmull([(0.44, 0.76), (0.82, 0.92), (0.86, 1.3), (0.5, 1.53), (0.12, 1.32)], False, 10)],
    "4": [[(0.7, 1.52), (0.7, 0.08), (0.1, 1.08), (0.92, 1.08)]],
    "5": [[(0.84, 0.08), (0.24, 0.08), (0.18, 0.7)] + catmull([(0.18, 0.7), (0.52, 0.58), (0.86, 0.86), (0.84, 1.28), (0.5, 1.53), (0.12, 1.36)], False, 10)],
    "6": [catmull([(0.78, 0.14), (0.42, 0.14), (0.16, 0.6), (0.16, 1.15), (0.5, 1.52), (0.84, 1.18), (0.78, 0.82), (0.48, 0.7), (0.17, 0.92)], False, 10)],
    "7": [[(0.12, 0.08), (0.88, 0.08), (0.4, 1.52)]],
    "8": [catmull([(0.5, 0.76), (0.18, 0.56), (0.22, 0.2), (0.5, 0.07), (0.78, 0.2), (0.82, 0.56)], True, 10),
          catmull([(0.5, 0.76), (0.86, 0.98), (0.84, 1.36), (0.5, 1.53), (0.16, 1.36), (0.14, 0.98)], True, 10)],
    "9": [catmull([(0.83, 0.6), (0.52, 0.82), (0.18, 0.62), (0.2, 0.25), (0.5, 0.07), (0.82, 0.3), (0.84, 0.86), (0.6, 1.4), (0.24, 1.42)], False, 10)],
    "plus": [[(0.5, 0.42), (0.5, 1.18)], [(0.12, 0.8), (0.88, 0.8)]],
    "minus": [[(0.16, 0.8), (0.84, 0.8)]],
    "x": [[(0.2, 0.46), (0.8, 1.14)], [(0.8, 0.46), (0.2, 1.14)]],
}


def digit_cells():
    """Rounded-stroke numbers (like the logo's lettering): white with a plum outline and a soft drop, tinted by the game."""
    out = []
    for name, paths in DIGIT_PATHS.items():
        w, h = 90, 130
        p = Piece("n_" + name, w, h, (0, 0), 62)   # units: x 0..1.25, y 0..1.75 (the paths use 0..1 by 0..1.6)
        p.meta["pivot"] = (0.5, 0.5)
        m = np.zeros((p.H, p.W), np.float32)
        for path in paths:
            pts = [(0.22 + x, 0.2 + y) for x, y in path]
            m = np.maximum(m, p.line_mask(pts, 0.26))
        drop = np.roll(m, int(p.units(0.07)), axis=0)
        p.paint(dilate(drop, p.units(0.07)), color=INK, alpha=0.45)
        p.paint(dilate(m, p.units(0.07)), color=INK)
        p.paint(m, color=WHITE)
        out.append(p)
    return out


# ---------------------------------------------------------------------------------------------------- Godit! (moles)

def island(front):
    """A floating grass island the mole pops out of: the back (its top, with the burrow) or the front (the grass lip
    and the soil underneath with roots, drawn over the mole's lower half)."""
    p = cell("island_front" if front else "island_back", 300, 240, ppt=150, pivot=(0.5, 0.5))
    grass, grass_dark, soil, soil_dark = LEAF, darker(LEAF, 0.28), hexc("#B9814F"), hexc("#7A4E2E")
    top = ellipse_pts(0, 0, 0.94, 0.36, 64)
    if not front:
        glossy(p, top, grass, outline=0.03, shade=0.2, radius=0.12)
        p.shape(ellipse_pts(0, 0.02, 0.5, 0.17, 48), color=hexc("#3B2522"), outline=0.02)
        p.paint(p.mask(ellipse_pts(0, 0.08, 0.44, 0.1, 48)), color=hexc("#22131A"))
        return p
    under = catmull([(-0.94, 0.0), (-0.7, 0.45), (-0.3, 0.72), (0.0, 0.76), (0.32, 0.7), (0.72, 0.42), (0.94, 0.0)], False, 10)
    p.shape(under + [(0.94, -0.02), (-0.94, -0.02)], color=soil, outline=0.03, shade=0.25, shade_radius=0.14)
    # soil stripes and pebbles
    for y, k in ((0.28, 0.85), (0.5, 0.6)):
        p.stroke(catmull([(-k, y), (-k * 0.4, y + 0.05), (k * 0.4, y + 0.03), (k, y - 0.02)], False, 8), 0.035, color=soil_dark)
    for x, y, rr in ((-0.45, 0.4, 0.06), (0.2, 0.55, 0.05), (0.55, 0.3, 0.045)):
        p.shape(ellipse_pts(x, y, rr * 1.3, rr, 16), color=hexc("#D9C3A5"), outline=0.012)
    for x in (-0.15, 0.25):
        p.stroke(catmull([(x, 0.6), (x + 0.04, 0.78), (x - 0.03, 0.92)], False, 6), 0.025, color=soil_dark)
    # the grass lip: the near half of the top, in front of the burrow
    lip = p.mask(ellipse_pts(0, 0, 0.94, 0.36, 64)) * (1 - p.mask(ellipse_pts(0, -0.06, 0.62, 0.2, 48)))
    lip *= (p.grid()[1] > -0.02).astype(np.float32) + (p.grid()[1] <= -0.02) * (np.abs(p.grid()[0]) > 0.5)
    p.fill(lip, color=grass, outline=0.03, shade=0.2, shade_radius=0.1)
    for k in range(9):
        x = -0.8 + k * 0.2
        p.shape([(x - 0.05, 0.2 + 0.06 * math.sin(k)), (x + 0.02, 0.06), (x + 0.07, 0.22 + 0.05 * math.cos(k))], color=grass_dark, outline=0)
    return p


def mole_body(p, fur, belly, clip=None):
    body = sym([(0, -0.82), (0.42, -0.72), (0.62, -0.3), (0.66, 0.4), (0.62, 1.0), (0, 1.0)], sharp=[4, 5])
    glossy(p, body, fur, outline=0.035, shade=0.3, radius=0.16, clip=clip)
    p.shape(sym([(0, -0.1), (0.36, 0.0), (0.42, 0.6), (0.4, 1.0), (0, 1.0)], sharp=[3, 4]), color=belly, outline=0, clip=clip)
    for k in (-1, 0, 1):                                                          # a tuft on top
        p.stroke(catmull([(0.06 * k, -0.78), (0.1 * k + 0.03, -0.95), (0.16 * k + 0.08, -1.0)], False, 6), 0.035, color=darker(fur, 0.35))
    for side in (-1, 1):                                                          # little paws up
        glossy(p, rotate(ellipse_pts(0.5 * side, 0.36, 0.2, 0.15, 24), -0.5 * side, 0.5 * side, 0.36), hexc("#FFC9A8"), outline=0.03, shade=0.25, radius=0.1)
        for k in (-1, 0, 1):
            p.stroke([(0.5 * side + 0.07 * k, 0.24), (0.5 * side + 0.09 * k, 0.17)], 0.025, color=INK)


def mole(kind):
    """A cute mole popping up: brown with a pink nose (the hit one dizzy; the golden one with a crown)."""
    name = {"normal": "mole", "hit": "mole_hit", "gold": "mole_gold"}[kind]
    p = cell(name, 220, 240, ppt=100, pivot=(0.5, 1.0))
    p.origin = (110, 140)   # body bottom at the cell's bottom
    fur = GOLD if kind == "gold" else BROWN
    belly = lighter(GOLD, 0.5) if kind == "gold" else hexc("#C99B76")
    if kind == "gold":
        soft_glow(p, 0, -0.25, 0.75, SUN, 0.6)
    mole_body(p, fur, belly)
    if kind == "hit":
        for side in (-1, 1):
            x = 0.24 * side
            p.stroke([(x - 0.09, -0.4), (x + 0.09, -0.24)], 0.05, color=INK)
            p.stroke([(x + 0.09, -0.4), (x - 0.09, -0.24)], 0.05, color=INK)
        p.shape(ellipse_pts(0, 0.12, 0.12, 0.1, 24), color=hexc("#5A2236"), outline=0.025)
        glossy(p, ellipse_pts(0.18, -0.68, 0.16, 0.12, 24), lighter(fur, 0.25), outline=0.03)
    else:
        eyes(p, 0, -0.33, 0.1, 0.24)
        blush(p, 0, -0.08, 0.12, 0.38)
        for side in (-1, 1):
            for k in (-1, 1):
                p.stroke([(0.2 * side, -0.05 + 0.04 * k), (0.52 * side, -0.1 + 0.1 * k)], 0.018, color=INK)
        p.shape([(-0.08, 0.06), (0.08, 0.06), (0.07, 0.2), (-0.07, 0.2)], color=WHITE, outline=0.02)
        p.stroke([(0, 0.07), (0, 0.19)], 0.015, color=INK)
    glossy(p, ellipse_pts(0, -0.06, 0.15, 0.11, 24), hexc("#FF7A9C"), outline=0.03, shade=0.3, radius=0.08)
    shine(p, -0.05, -0.1, 0.05, 0.03, alpha=0.9)
    if kind == "gold":
        crown = [(-0.28, -0.72), (-0.32, -1.02), (-0.14, -0.86), (0, -1.08), (0.14, -0.86), (0.32, -1.02), (0.28, -0.72)]
        glossy(p, crown, SUN, outline=0.03, shade=0.3, radius=0.06)
        for x, y in ((-0.32, -1.04), (0, -1.1), (0.32, -1.04)):
            p.shape(ellipse_pts(x, y, 0.05, 0.05, 12), color=CORAL, outline=0.015)
        for x, y, r in ((-0.75, -0.6, 0.12), (0.72, -0.25, 0.09), (-0.6, 0.35, 0.07)):
            p.shape(star_pts(x, y, r, 0.3, 4), color=WHITE, outline=0)
    return p


def hedgehog():
    """A hedgehog popping up instead of a mole: don't touch! Spiky, a frown, a red nose."""
    p = cell("hedgehog", 220, 240, ppt=100, pivot=(0.5, 1.0))
    p.origin = (110, 140)
    spikes = []
    for k in range(15):
        a = math.pi + math.pi * k / 14
        r = 1.0 if k % 2 == 0 else 0.7
        spikes.append((math.cos(a) * 0.78 * r, -0.12 + math.sin(a) * 0.95 * r))
    spikes = [(0.62, 1.0), (0.78, 0.1)] + spikes[::-1] + [(-0.78, 0.1), (-0.62, 1.0)]
    p.shape(spikes, color=hexc("#6B3D2A"), outline=0.035, shade=0.25, shade_radius=0.1)
    for k in range(10):                                                           # light spike tips
        a = math.pi + math.pi * (k + 0.5) / 10
        x, y = math.cos(a) * 0.72, -0.12 + math.sin(a) * 0.86
        p.stroke([(x * 0.85, y * 0.85 - 0.02), (x, y)], 0.04, color=hexc("#C89B7B"))
    face = sym([(0, -0.5), (0.4, -0.4), (0.52, 0.1), (0.5, 1.0), (0, 1.0)], sharp=[3, 4])
    glossy(p, face, hexc("#F2CFA6"), outline=0.035, shade=0.25, radius=0.12)
    eyes(p, 0, -0.18, 0.085, 0.2)
    for side in (-1, 1):                                                          # cross eyebrows
        p.stroke([(0.1 * side, -0.36), (0.32 * side, -0.42)], 0.05, color=INK)
    glossy(p, ellipse_pts(0, 0.05, 0.12, 0.09, 24), CORAL, outline=0.03)
    p.stroke(catmull([(-0.14, 0.3), (0, 0.24), (0.14, 0.3)], False, 6), 0.035, color=INK)
    for side in (-1, 1):
        glossy(p, ellipse_pts(0.4 * side, 0.48, 0.13, 0.11, 20), hexc("#F2CFA6"), outline=0.03)
    return p


def hammer():
    """A toy squeaky hammer for each hand: a red barrel head with yellow caps on a striped handle (pivot: the grip)."""
    p = cell("hammer", 220, 220, ppt=100, pivot=(0.67, 0.88))
    handle = rotate([(-0.08, -0.35), (0.08, -0.35), (0.08, 0.95), (-0.08, 0.95)], -0.6, 0, 0.3)
    m = p.mask(handle)
    p.fill(m, color=WHITE, outline=0.03)
    for k in range(5):
        band = rotate([(-0.09, 0.95 - k * 0.27), (0.09, 0.95 - k * 0.27), (0.09, 0.82 - k * 0.27), (-0.09, 0.82 - k * 0.27)], -0.6, 0, 0.3)
        p.shape(band, color=SKY, outline=0, clip=m)
    head = rotate([(-0.7, -0.62), (0.4, -0.62), (0.4, -0.18), (-0.7, -0.18)], -0.6, 0, 0.3)
    head_c = rotate(ellipse_pts(-0.15, -0.4, 0.55, 0.26, 48), -0.6, 0, 0.3)
    glossy(p, catmull(head_c, True, 2), CORAL, outline=0.035, shade=0.35, radius=0.12)
    for x in (-0.66, 0.36):
        cap = rotate(ellipse_pts(x, -0.4, 0.12, 0.27, 32), -0.6, 0, 0.3)
        glossy(p, cap, SUN, outline=0.03, shade=0.3, radius=0.07)
    shine(p, *rotate([(-0.25, -0.55)], -0.6, 0, 0.3)[0], 0.22, 0.05, angle=-0.6, alpha=0.7)
    return p


# ---------------------------------------------------------------------------------------------------- Fruta! (fruit)

FLESH = {"apple": hexc("#FFF0C2"), "orange": hexc("#FFC46B"), "melon": hexc("#FF5466"), "banana": hexc("#FFF2B3"),
         "berry": hexc("#FF6F7E"), "pear": hexc("#F7F4C6"), "star": hexc("#FFF1A6")}
JUICE = {"apple": hexc("#FFE38A"), "orange": hexc("#FFA53A"), "melon": hexc("#FF4D5E"), "banana": hexc("#FFE680"),
         "berry": hexc("#FF3F62"), "pear": hexc("#E5F08A"), "star": hexc("#FFE45C"), "bomb": hexc("#9AA0B5")}


def fruit_shape(kind):
    if kind == "apple":
        return catmull([(0, -0.55), (0.42, -0.72), (0.8, -0.35), (0.78, 0.25), (0.45, 0.78), (0, 0.7), (-0.45, 0.78), (-0.78, 0.25), (-0.8, -0.35), (-0.42, -0.72)], True, 10)
    if kind == "orange":
        return ellipse_pts(0, 0.04, 0.8, 0.78, 64)
    if kind == "melon":
        return ellipse_pts(0, 0, 0.9, 0.78, 64)
    if kind == "banana":
        return catmull([(-0.85, -0.55), (-0.55, 0.25), (0.0, 0.62), (0.6, 0.5), (0.9, 0.1), (0.72, 0.18), (0.2, 0.25), (-0.4, -0.1), (-0.68, -0.62)], True, 10, sharp=(0, 8))
    if kind == "berry":
        return catmull([(0, -0.55), (0.55, -0.62), (0.78, -0.25), (0.5, 0.45), (0, 0.85), (-0.5, 0.45), (-0.78, -0.25), (-0.55, -0.62)], True, 10)
    if kind == "pear":
        return catmull([(0, -0.82), (0.3, -0.62), (0.4, -0.15), (0.72, 0.32), (0.55, 0.78), (0, 0.84), (-0.55, 0.78), (-0.72, 0.32), (-0.4, -0.15), (-0.3, -0.62)], True, 10)
    return star_pts(0, 0.05, 0.92, 0.55, 5, round_=1)


def fruit(kind, part):
    """A fruit whole, or its left / right half after a slice (the cut face showing its flesh)."""
    name = kind if part == "whole" else f"{kind}_{part}"
    p = cell(name, 160, ppt=72)
    X, Y = p.grid()
    clip = None
    if part == "left":
        clip = (X < -0.02).astype(np.float32)
    elif part == "right":
        clip = (X > 0.02).astype(np.float32)
    shape = fruit_shape(kind)
    colours = {"apple": hexc("#FF4D4D"), "orange": hexc("#FFA53A"), "melon": hexc("#3DBE5B"), "banana": hexc("#FFD84A"),
               "berry": hexc("#FF3B57"), "pear": hexc("#B8DC4A"), "star": SUN}
    if kind == "star" and part == "whole":
        soft_glow(p, 0, 0, 0.62, SUN, 0.5)
    m = glossy(p, shape, colours[kind], outline=0.045, shade=0.34, radius=0.2, clip=clip)
    if kind == "melon":
        for k in range(-3, 4):
            stripe = [(k * 0.24 + 0.08 * math.sin(t * 3), t) for t in np.linspace(-0.8, 0.8, 12)]
            p.paint(p.line_mask(stripe, 0.07) * m, color=darker(colours[kind], 0.4))
    if kind == "orange":
        rng = np.random.default_rng(3)
        for _ in range(26):
            x, y = rng.uniform(-0.6, 0.6, 2)
            if x * x + y * y < 0.4:
                p.paint(p.circle_mask(x, y, 0.025) * m, color=darker(colours[kind], 0.12))
    if kind == "berry":
        for x, y in [(-0.35, -0.2), (0.0, -0.3), (0.35, -0.2), (-0.2, 0.15), (0.2, 0.15), (0.0, 0.45), (-0.45, 0.1), (0.45, 0.1)]:
            p.paint(p.mask(ellipse_pts(x, y, 0.035, 0.05, 12)) * m, color=hexc("#FFE38A"))
    if part == "whole":
        if kind in ("apple", "pear", "orange"):
            top = -0.6 if kind == "apple" else -0.82 if kind == "pear" else -0.72
            p.stroke([(0, top + 0.05), (0.06, top - 0.22)], 0.07, color=hexc("#6B4226"))
            leaf = rotate(ellipse_pts(0.24, top - 0.18, 0.2, 0.09, 24), -0.5, 0.24, top - 0.18)
            glossy(p, leaf, LEAF, outline=0.03, shade=0.25, radius=0.06)
        if kind == "berry":
            for k in range(5):
                a = math.pi + math.pi * k / 4
                glossy(p, rotate(ellipse_pts(0.18 * math.cos(a), -0.62 + 0.12 * math.sin(a), 0.18, 0.07, 16), a, 0.18 * math.cos(a), -0.62 + 0.12 * math.sin(a)),
                       LEAF, outline=0.025, shade=0.2, radius=0.04)
        if kind == "banana":
            p.shape(ellipse_pts(-0.78, -0.6, 0.07, 0.07, 12), color=hexc("#6B4226"), outline=0.02)
        shine(p, -0.32, -0.3, 0.2, 0.1, alpha=0.7)
        return p
    # the cut face: a narrow ellipse of flesh along the cut, on the half's inner side
    side = -1 if part == "left" else 1
    ys = np.where(m.max(axis=1) > 0.5)[0]
    top, bottom = (ys.min() / 4 - p.origin[1]) / p.ppt, (ys.max() / 4 - p.origin[1]) / p.ppt
    cy, ry = (top + bottom) / 2, (bottom - top) / 2 * 0.92
    face = p.mask(ellipse_pts(0.02 * side, cy, 0.22, ry, 48)) * clip
    p.fill(face, color=FLESH[kind], outline=0.03, shade=0.15, shade_radius=0.08)
    if kind == "melon":
        p.paint(np.clip(face - p.mask(ellipse_pts(0.02 * side, cy, 0.17, ry * 0.9, 48)), 0, 1), color=hexc("#F2FFE8"))
        for y in (-0.35, 0.0, 0.32):
            p.paint(p.mask(ellipse_pts(0.08 * side, cy + y, 0.025, 0.045, 12)) * face, color=INK)
    if kind in ("apple", "pear"):
        for y in (-0.08, 0.1):
            p.paint(p.mask(ellipse_pts(0.06 * side, cy + y, 0.03, 0.05, 12)) * face, color=hexc("#4A2A1A"))
    if kind == "orange":
        for k in range(5):
            a = -math.pi / 2 + math.pi * k / 4
            p.stroke([(0.02 * side, cy), (0.02 * side + 0.2 * math.cos(a) * side * 0.5, cy + ry * 0.85 * math.sin(a))], 0.02,
                     color=lighter(FLESH[kind], 0.5))
    return p


def bomb():
    p = cell("bomb", 160, ppt=68)
    glossy(p, ellipse_pts(0, 0.12, 0.72, 0.72, 64), hexc("#2F3550"), outline=0.045, shade=0.45, radius=0.24)
    p.shape([(-0.2, -0.62), (0.2, -0.62), (0.2, -0.45), (-0.2, -0.45)], color=hexc("#6B7194"), outline=0.035)
    p.stroke(catmull([(0, -0.62), (0.15, -0.82), (0.38, -0.84), (0.5, -0.72)], False, 8), 0.06, color=hexc("#C9A36B"))
    soft_glow(p, 0.55, -0.74, 0.5, SUN, 0.8)
    p.shape(star_pts(0.55, -0.74, 0.2, 0.35, 6), color=hexc("#FFE45C"), outline=0)
    p.shape(star_pts(0.55, -0.74, 0.1, 0.4, 6), color=WHITE, outline=0)
    shine(p, -0.28, -0.14, 0.2, 0.1, alpha=0.6)
    for side in (-1, 1):                                                          # cross little eyes: it's trouble
        p.stroke([(0.2 * side - 0.08, 0.02), (0.2 * side + 0.08, 0.12)], 0.05, color=WHITE)
        p.shape(ellipse_pts(0.22 * side, 0.22, 0.06, 0.07, 16), color=WHITE, outline=0)
    return p


# ---------------------------------------------------------------------------------------------------- Kap! (sweets)

def candy(i):
    colour = (CORAL, SKY, GRAPE, LEAF)[i]
    p = cell(f"candy{i}", 150, 100, ppt=50)
    for side in (-1, 1):
        twist = [(0.55 * side, 0.0), (1.4 * side, -0.62), (1.25 * side, 0.0), (1.4 * side, 0.62)]
        glossy(p, twist, lighter(colour, 0.25), outline=0.07, shade=0.25, radius=0.15)
        p.stroke([(0.75 * side, -0.25), (1.2 * side, -0.4)], 0.05, color=darker(colour, 0.2))
    m = glossy(p, ellipse_pts(0, 0, 0.78, 0.62, 48), colour, outline=0.07, shade=0.35, radius=0.25)
    for k in (-1, 0, 1):
        p.paint(p.line_mask([(-0.3 + 0.45 * k, -0.7), (0.15 + 0.45 * k, 0.7)], 0.14) * m, color=WHITE, alpha=0.85)
    shine(p, -0.3, -0.25, 0.22, 0.08, alpha=0.8)
    return p


def lollipop():
    p = cell("lollipop", 120, 200, ppt=60, pivot=(0.5, 0.35))
    p.shape([(-0.07, 0.2), (0.07, 0.2), (0.07, 1.6), (-0.07, 1.6)], color=WHITE, outline=0.05)
    disc = p.mask(ellipse_pts(0, -0.55, 0.88, 0.88, 64))
    p.paint(dilate(disc, p.units(0.07)), color=INK)
    X, Y = p.grid()
    ang = np.arctan2(Y + 0.55, X)
    r = np.hypot(X, Y + 0.55)
    swirl = (np.sin(ang * 1 + r * 9.0) > 0).astype(np.float32)
    p.paint(disc, color=CORAL)
    p.paint(disc * swirl, color=SUN)
    p.paint(disc * (np.sin(ang * 1 + r * 9.0 + 1.6) > 0.82), color=WHITE)
    p.paint(disc, color=WHITE, alpha=0.0)
    shine(p, -0.3, -0.85, 0.25, 0.1, alpha=0.75)
    return p


def floss():
    p = cell("floss", 150, 220, ppt=70, pivot=(0.5, 0.4))
    cone = [(-0.22, 0.2), (0.22, 0.2), (0.0, 1.5)]
    m = glossy(p, cone, WHITE, outline=0.05, shade=0.2, radius=0.08)
    for k in range(3):
        p.paint(p.line_mask([(-0.3, 0.42 + k * 0.32), (0.3, 0.26 + k * 0.32)], 0.08) * m, color=SKY)
    for x, y, r in ((-0.42, -0.25, 0.42), (0.38, -0.3, 0.45), (0.0, -0.6, 0.5), (-0.2, 0.08, 0.38), (0.25, 0.05, 0.36), (0.0, -0.95, 0.32)):
        p.shape(ellipse_pts(x, y, r, r * 0.92, 40), color=hexc("#FFB3D6"), outline=0.045, shade=0.3, shade_radius=0.16)
    shine(p, -0.25, -0.75, 0.2, 0.08, alpha=0.6)
    return p


def balloon():
    """A balloon (white: the game tints it), its knot and a curly string (pivot: the balloon's middle)."""
    p = cell("balloon", 150, 260, ppt=75, pivot=(0.5, 0.33))
    p.origin = (75, 86)
    p.stroke(catmull([(0, 0.95), (0.12, 1.25), (-0.1, 1.55), (0.1, 1.85), (-0.02, 2.2)], False, 10), 0.035, color=hexc("#E8E4F2"))
    m = glossy(p, catmull([(0, -1.05), (0.72, -0.65), (0.78, 0.15), (0.38, 0.82), (0, 0.95), (-0.38, 0.82), (-0.78, 0.15), (-0.72, -0.65)], True, 10),
               WHITE, outline=0.05, shade=0.25, radius=0.3, ink=hexc("#5B4C7A"))
    p.shape([(-0.12, 1.05), (0.12, 1.05), (0.0, 0.88)], color=WHITE, outline=0.04, ink=hexc("#5B4C7A"))
    shine(p, -0.32, -0.45, 0.16, 0.3, angle=0.35, alpha=0.9)
    p.paint(p.circle_mask(-0.18, -0.02, 0.05) * m, color=WHITE, alpha=0.8)
    return p


def basket(front):
    """A woven basket: the back wall with the handle, or the front wall with the rim (sweets fall in between)."""
    p = cell("basket_front" if front else "basket_back", 340, 260, ppt=110, pivot=(0.5, 0.5))
    tan, dark = hexc("#E3A35E"), hexc("#A8672F")
    if not front:
        handle = np.clip(p.mask(ellipse_pts(0, 0.05, 1.12, 1.05, 64)) - p.mask(ellipse_pts(0, 0.05, 1.0, 0.94, 64)), 0, 1) * (p.grid()[1] < 0.05)
        p.paint(dilate(handle, p.units(0.04)), color=INK)
        p.paint(handle, color=tan)
        back = ellipse_pts(0, 0.05, 1.2, 0.36, 64)
        p.shape(back, color=darker(tan, 0.35), outline=0.04)
        return p
    wall = catmull([(-1.25, 0.05), (-1.05, 0.95), (0.0, 1.1), (1.05, 0.95), (1.25, 0.05)], False, 10)
    m = p.mask(wall + [(1.25, 0.05), (-1.25, 0.05)]) * (1 - p.mask(ellipse_pts(0, 0.05, 1.2, 0.36, 64)) * (p.grid()[1] < 0.05))
    p.fill(m, color=tan, outline=0.045, shade=0.25, shade_radius=0.15)
    X, Y = p.grid()
    weave = ((np.sin(X * 9) * np.sin(Y * 9 + X * 2)) > 0.35).astype(np.float32) * m
    p.paint(weave, color=darker(tan, 0.18))
    for y in (0.35, 0.65):
        p.stroke(catmull([(-1.15, y - 0.05), (0, y + 0.08), (1.15, y - 0.05)], False, 10), 0.05, color=dark)
    rim = np.clip(p.mask(ellipse_pts(0, 0.05, 1.27, 0.42, 64)) - p.mask(ellipse_pts(0, 0.0, 1.17, 0.32, 64)), 0, 1) * (Y > -0.02)
    p.fill(rim, color=lighter(tan, 0.15), outline=0.04)
    return p


# ---------------------------------------------------------------------------------------------------- Zogu! (sky)

def bird(colour, name, wing):
    """A round little bird (body in the player's colour), wings up / mid / down."""
    p = cell(name, 220, 190, ppt=85)
    dark = darker(colour, 0.3)
    wing_shapes = {
        "up": [(-0.15, -0.15), (-0.55, -0.95), (-0.95, -0.85), (-0.65, -0.1)],
        "mid": [(-0.1, -0.05), (-0.85, -0.35), (-1.05, 0.0), (-0.45, 0.2)],
        "down": [(-0.1, 0.05), (-0.75, 0.55), (-0.85, 0.95), (-0.3, 0.45)],
    }
    tail = [(-0.75, 0.1), (-1.2, -0.15), (-1.15, 0.15), (-1.25, 0.35), (-0.75, 0.35)]
    glossy(p, tail, dark, outline=0.04, shade=0.25, radius=0.08)
    back_wing = [(x + 0.35, y - 0.02) for x, y in wing_shapes[wing]]
    glossy(p, catmull(back_wing, True, 6), dark, outline=0.04, shade=0.2, radius=0.08)
    body = ellipse_pts(0.05, 0.1, 0.78, 0.7, 64)
    m = glossy(p, body, colour, outline=0.045, shade=0.32, radius=0.22)
    p.paint(p.mask(ellipse_pts(0.25, 0.38, 0.45, 0.32, 48)) * m, color=hexc("#FFF0C8"))
    eyes(p, 0.42, -0.12, 0.13, 0.0, look=(0.04, 0))
    blush(p, 0.62, 0.12, 0.1, 0.0)
    beak = [(0.72, -0.02), (1.12, 0.1), (0.74, 0.24)]
    glossy(p, beak, SUN, outline=0.035, shade=0.25, radius=0.06)
    for k in (-1, 0, 1):                                                         # a tuft
        p.stroke(catmull([(0.1 + 0.08 * k, -0.55), (0.18 + 0.12 * k, -0.78), (0.3 + 0.16 * k, -0.82)], False, 6), 0.05, color=dark)
    glossy(p, catmull(wing_shapes[wing], True, 6), lighter(colour, 0.2), outline=0.04, shade=0.25, radius=0.08)
    shine(p, -0.2, -0.3, 0.2, 0.09, alpha=0.6)
    return p


def cloud(storm):
    p = cell("storm" if storm else "cloud", 380, 240, ppt=110)
    base = hexc("#8B7FB3") if storm else WHITE
    shade_c = hexc("#5E5585") if storm else hexc("#D8E2FF")
    puffs = ((-0.85, 0.15, 0.55), (-0.3, -0.25, 0.7), (0.35, -0.2, 0.65), (0.9, 0.15, 0.55), (0.0, 0.3, 0.6))
    m = np.zeros((p.H, p.W), np.float32)
    for x, y, r in puffs:
        m = np.maximum(m, p.circle_mask(x, y, r))
    p.paint(dilate(m, p.units(0.05)), color=hexc("#3A2E5C") if storm else hexc("#9AA8E0"))
    p.paint(m, color=base)
    X, Y = p.grid()
    p.paint(m * np.clip((Y - 0.0) / 0.6, 0, 1), color=shade_c, alpha=0.8)
    if storm:
        eyes(p, 0, 0.0, 0.1, 0.25)
        for side in (-1, 1):
            p.stroke([(0.12 * side, -0.2), (0.38 * side, -0.14)], 0.05, color=INK)
        p.stroke(catmull([(-0.14, 0.32), (0, 0.24), (0.14, 0.32)], False, 6), 0.04, color=INK)
        bolt = [(0.55, 0.45), (0.85, 0.45), (0.68, 0.75), (0.92, 0.75), (0.45, 1.25), (0.6, 0.85), (0.38, 0.85)]
        glossy(p, bolt, hexc("#FFE45C"), outline=0.04, shade=0.2, radius=0.05)
    else:
        shine(p, -0.35, -0.5, 0.25, 0.08, angle=-0.2, alpha=0.6)
    return p


def hoop():
    p = cell("hoop", 220)
    ring = np.clip(p.circle_mask(0, 0, 0.9) - p.circle_mask(0, 0, 0.68), 0, 1)
    p.fill(ring, color=SUN, outline=0.04, shade=0.35, shade_radius=0.08)
    X, Y = p.grid()
    ang = np.arctan2(Y, X)
    p.paint(ring * (np.sin(ang * 8) > 0.55), color=CORAL)
    soft_glow(p, 0, 0, 0.62, SUN, 0.25)
    return p


def moon():
    p = cell("moon", 256)
    soft_glow(p, 0, 0, 0.62, hexc("#FFF4C2"), 0.4)
    m = np.clip(p.circle_mask(0, 0, 0.62) - p.circle_mask(0.28, -0.18, 0.52), 0, 1)
    p.fill(m, color=hexc("#FFF4C2"), outline=0, shade=0.2, shade_radius=0.1)
    return p


# ---------------------------------------------------------------------------------------------------- Topi! (balls)

def football():
    p = cell("football", 170)
    m = glossy(p, ellipse_pts(0, 0, 0.88, 0.88, 64), WHITE, outline=0.05, shade=0.3, radius=0.3)
    def pent(cx, cy, r, rot):
        return [(cx + r * math.cos(rot + 2 * math.pi * k / 5), cy + r * math.sin(rot + 2 * math.pi * k / 5)) for k in range(5)]
    p.paint(p.mask(pent(0, 0, 0.3, -math.pi / 2)) * m, color=INK)
    for k in range(5):
        a = -math.pi / 2 + 2 * math.pi * k / 5 + math.pi / 5
        cx, cy = math.cos(a) * 0.78, math.sin(a) * 0.78
        p.paint(p.mask(pent(cx, cy, 0.28, a + math.pi)) * m, color=INK)
        p.stroke([(math.cos(a - math.pi / 5) * 0.3, math.sin(a - math.pi / 5) * 0.3), (cx * 0.62, cy * 0.62)], 0.025, color=hexc("#B9B4C8"))
    shine(p, -0.32, -0.36, 0.22, 0.12, alpha=0.75)
    return p


def beachball():
    p = cell("beachball", 170)
    m = glossy(p, ellipse_pts(0, 0, 0.88, 0.88, 64), WHITE, outline=0.05, shade=0.3, radius=0.3)
    X, Y = p.grid()
    ang = np.arctan2(Y + 0.25, X - 0.1)
    colours = (CORAL, WHITE, SKY, WHITE, SUN, WHITE)
    for k, c in enumerate(colours):
        a0 = -math.pi + 2 * math.pi * k / 6
        seg = ((ang >= a0) & (ang < a0 + 2 * math.pi / 6)).astype(np.float32) * m
        p.paint(seg, color=c)
    p.paint(p.circle_mask(0.1, -0.25, 0.16), color=WHITE)
    p.paint(p.mask(ellipse_pts(0, 0, 0.88, 0.88, 64)) * np.clip(0.2 * pillow(m, p.units(0.3)), 0, 1), color=WHITE, alpha=0.5)
    shine(p, -0.32, -0.38, 0.22, 0.12, alpha=0.75)
    return p


# ---------------------------------------------------------------------------------------------------- the fun fair

def icon(name):
    p = cell("icon_" + name, 220)
    soft_glow(p, 0, 0, 0.6, WHITE, 0.3)
    if name == "whack":
        island_cell = None
        glossy(p, ellipse_pts(0, 0.55, 0.85, 0.28, 48), LEAF, outline=0.04)
        p.shape(ellipse_pts(0, 0.55, 0.45, 0.14, 32), color=hexc("#3B2522"), outline=0.02)
        body = sym([(0, -0.35), (0.3, -0.28), (0.42, 0.05), (0.42, 0.55), (0, 0.55)], sharp=[3, 4])
        glossy(p, body, BROWN, outline=0.04, shade=0.3, radius=0.12)
        eyes(p, 0, -0.08, 0.07, 0.15)
        glossy(p, ellipse_pts(0, 0.1, 0.1, 0.075, 20), hexc("#FF7A9C"), outline=0.03)
        glossy(p, ellipse_pts(0, 0.62, 0.85, 0.22, 48), LEAF, outline=0.04, clip=(p.grid()[1] > 0.55).astype(np.float32))
        h = rotate([(-0.06, -0.4), (0.06, -0.4), (0.06, 0.4), (-0.06, 0.4)], 0.7, 0.45, -0.2)
        p.shape([(x + 0.25, y - 0.35) for x, y in h], color=SKY, outline=0.03)
        head = rotate(ellipse_pts(0.42, -0.72, 0.32, 0.16, 32), 0.7, 0.42, -0.72)
        glossy(p, head, CORAL, outline=0.04, shade=0.3, radius=0.08)
    elif name == "fruit":
        wedge = catmull([(-0.85, -0.2), (0.0, 0.75), (0.85, -0.2)], True, 2, sharp=(0, 1, 2))
        wedge = [(x, y) for x, y in ellipse_pts(0, -0.25, 0.9, 0.95, 64) if y >= -0.25]
        m = glossy(p, wedge + [(0.9, -0.25), (-0.9, -0.25)], hexc("#3DBE5B"), outline=0.045)
        inner = [(x, y) for x, y in ellipse_pts(0, -0.25, 0.75, 0.8, 64) if y >= -0.25]
        p.shape(inner + [(0.75, -0.25), (-0.75, -0.25)], color=hexc("#F2FFE8"), outline=0)
        inner2 = [(x, y) for x, y in ellipse_pts(0, -0.25, 0.68, 0.72, 64) if y >= -0.25]
        p.shape(inner2 + [(0.68, -0.25), (-0.68, -0.25)], color=hexc("#FF5466"), outline=0, shade=0.2, shade_radius=0.1)
        for x, y in ((-0.3, 0.05), (0.0, 0.2), (0.3, 0.05), (-0.15, 0.4), (0.15, 0.4)):
            p.shape(ellipse_pts(x, y, 0.035, 0.06, 12), color=INK, outline=0)
        p.stroke([(-0.95, 0.75), (0.95, -0.8)], 0.07, color=WHITE)
    elif name == "wall":
        board = [(-0.9, -0.8), (0.9, -0.8), (0.9, 0.85), (-0.9, 0.85)]
        m = p.mask(board)
        X, Y = p.grid()
        stripes = (np.sin((X + Y) * 9) > 0).astype(np.float32)
        hole = np.zeros_like(m)
        for a, b, r in (((0, -0.52), (0, -0.52), 0.17), ((0, -0.3), (0, 0.25), 0.2), ((0, -0.25), (-0.55, -0.6), 0.09),
                        ((0, -0.25), (0.55, -0.6), 0.09), ((0, 0.25), (-0.25, 0.75), 0.1), ((0, 0.25), (0.25, 0.75), 0.1)):
            hole = np.maximum(hole, p.line_mask([a, b], r * 2))
        board_m = m * (1 - hole)
        p.paint(dilate(m, p.units(0.04)), color=INK)
        p.paint(board_m, color=CORAL)
        p.paint(board_m * stripes, color=SUN)
        p.paint(np.clip(dilate(hole, p.units(0.035)) - hole, 0, 1) * m, color=WHITE)
    elif name == "catch":
        glossy(p, ellipse_pts(0, 0.1, 0.85, 0.25, 48), darker(hexc("#E3A35E"), 0.35), outline=0.04)
        for x, y, c in ((-0.3, -0.05, CORAL), (0.25, -0.15, SKY), (0.0, -0.4, GRAPE)):
            glossy(p, ellipse_pts(x, y, 0.22, 0.17, 32), c, outline=0.04, shade=0.3, radius=0.08)
        wall = catmull([(-0.9, 0.1), (-0.75, 0.75), (0.0, 0.85), (0.75, 0.75), (0.9, 0.1)], False, 10)
        glossy(p, wall + [(0.9, 0.1), (-0.9, 0.1)], hexc("#E3A35E"), outline=0.045)
        p.shape(star_pts(0.55, -0.7, 0.22, 0.45, 5, round_=1), color=SUN, outline=0.04)
    elif name == "bird":
        b = bird(CORAL, "tmp", "up")
        return place_scaled(p, b, 0.95)
    elif name == "ball":
        f = football()
        return place_scaled(p, f, 0.9)
    return p


def place_scaled(dst, src, k):
    """Draws another piece's picture into dst, scaled by k (icons made from the sprites)."""
    img, _ = src.image()
    s = int(dst.w * k)
    small = cv2.resize(img, (s, int(s * src.h / src.w)), interpolation=cv2.INTER_AREA).astype(np.float32) / 255
    big = cv2.resize(small, (small.shape[1] * 4, small.shape[0] * 4), interpolation=cv2.INTER_LINEAR)
    oy, ox = (dst.H - big.shape[0]) // 2, (dst.W - big.shape[1]) // 2
    a = big[..., 3]
    region = (slice(oy, oy + big.shape[0]), slice(ox, ox + big.shape[1]))
    dst.P[region] = big[..., :3] * a[..., None] + dst.P[region] * (1 - a[..., None])
    dst.A[region] = a + dst.A[region] * (1 - a)
    return dst


def awning(tinted):
    """A booth's striped awning with a scalloped edge: the white stripes and shading, or only the coloured stripes
    (white: the game tints them in the booth's colour)."""
    p = cell("awning_tint" if tinted else "awning", 520, 180, ppt=86)
    X, Y = p.grid()
    top = [(-2.9, -0.9), (2.9, -0.9), (2.9, 0.35)]
    scallop = []
    n = 8
    for k in range(n, -1, -1):
        x0 = -2.9 + 5.8 * k / n
        for t in np.linspace(0, math.pi, 8):
            scallop.append((x0 - 5.8 / n * (1 - math.cos(t)) / 2, 0.35 + 0.42 * math.sin(t)))
    shape = top + scallop + [(-2.9, 0.35)]
    m = p.mask(shape)
    stripes = ((np.floor((X + 2.9) / (5.8 / n)) % 2) == 0).astype(np.float32) * m
    if tinted:
        p.paint(stripes, color=WHITE)
        return p
    p.paint(dilate(m, p.units(0.05)), color=INK)
    p.paint(m, color=WHITE)
    p.paint(m * np.clip((Y + 0.9) / 1.7, 0, 1) * 0.25, color=hexc("#1E1840"), alpha=0.6)
    p.paint(np.clip(p.mask([(-2.9, -0.9), (2.9, -0.9), (2.9, -0.62), (-2.9, -0.62)]), 0, 1), color=hexc("#5A3A8A"))
    for k in range(n + 1):
        x = -2.9 + 5.8 * k / n
        p.paint(p.circle_mask(x, -0.76, 0.1), color=SUN)
    return p


def wheel_rim():
    """The ferris wheel's turning part: double rim, spokes, a hub (lit lilac lines on the night sky)."""
    p = cell("wheel_rim", 640, ppt=312)
    lilac, deep = hexc("#B8A6FF"), hexc("#6E5BC8")
    m = np.zeros((p.H, p.W), np.float32)
    for r in (0.96, 0.84):
        m = np.maximum(m, np.clip(p.circle_mask(0, 0, r + 0.018) - p.circle_mask(0, 0, r - 0.018), 0, 1))
    for k in range(16):
        a = 2 * math.pi * k / 16
        m = np.maximum(m, p.line_mask([(0, 0), (math.cos(a) * 0.96, math.sin(a) * 0.96)], 0.022))
        a2 = a + math.pi / 16
        m = np.maximum(m, p.line_mask([(math.cos(a) * 0.84, math.sin(a) * 0.84), (math.cos(a2) * 0.96, math.sin(a2) * 0.96)], 0.016))
    p.paint(blur(m, p.units(0.02)), color=lilac, alpha=0.5)
    p.paint(m, color=lilac)
    glossy(p, ellipse_pts(0, 0, 0.09, 0.09, 32), deep, outline=0.012)
    p.paint(p.circle_mask(0, 0, 0.04), color=SUN)
    return p


def wheel_legs():
    p = cell("wheel_legs", 512, 300, ppt=228, pivot=(0.5, 0.0))
    p.origin = (256, 10)
    deep = hexc("#6E5BC8")
    for side in (-1, 1):
        p.stroke([(0, 0), (0.55 * side, 1.22)], 0.045, color=deep)
        p.stroke([(0.18 * side, 0.4), (0.42 * side, 0.92)], 0.025, color=deep)
    p.stroke([(-0.38, 0.85), (0.38, 0.85)], 0.025, color=deep)
    p.shape([(-0.75, 1.18), (0.75, 1.18), (0.7, 1.24), (-0.7, 1.24)], color=hexc("#3D2F7A"), outline=0)
    return p


def cabin(i):
    colour = (CORAL, SKY, SUN, LEAF)[i]
    p = cell(f"cabin{i}", 100, 110, ppt=45, pivot=(0.5, 0.0))
    p.origin = (50, 8)
    p.stroke([(0, 0), (0, 0.45)], 0.08, color=hexc("#6E5BC8"))
    roof = catmull([(-0.85, 0.75), (0, 0.38), (0.85, 0.75)], True, 6, sharp=(0, 2))
    glossy(p, roof, colour, outline=0.08, shade=0.3, radius=0.1)
    body = [(-0.72, 0.75), (0.72, 0.75), (0.65, 2.0), (-0.65, 2.0)]
    glossy(p, body, lighter(colour, 0.15), outline=0.08, shade=0.25, radius=0.12)
    p.shape([(-0.5, 0.95), (0.5, 0.95), (0.47, 1.45), (-0.47, 1.45)], color=hexc("#FFF4C2"), outline=0.05)
    return p


def tent():
    p = cell("tent", 520, 380, ppt=130, pivot=(0.5, 1.0))
    p.origin = (260, 380)
    X, Y = p.grid()
    roof = [(0, -2.6), (1.85, -1.25), (-1.85, -1.25)]
    walls = [(-1.65, -1.3), (1.65, -1.3), (1.65, 0.0), (-1.65, 0.0)]
    wm = p.mask(walls)
    p.paint(dilate(wm, p.units(0.04)), color=INK)
    p.paint(wm, color=WHITE)
    p.paint(wm * (np.floor((X + 1.65) / 0.41) % 2 == 0), color=CORAL)
    p.shape([(-0.45, 0.0), (0, -0.95), (0.45, 0.0)], color=hexc("#3A1F3D"), outline=0.03)
    rm = p.mask(roof)
    p.paint(dilate(rm, p.units(0.04)), color=INK)
    p.paint(rm, color=WHITE)
    ang = np.arctan2(X, Y + 2.6)
    p.paint(rm * (np.floor(ang / 0.16) % 2 == 0), color=CORAL)
    for k in range(9):
        x = -1.85 + 3.7 * k / 8
        p.paint(p.circle_mask(x, -1.25, 0.1), color=SUN)
    p.stroke([(0, -2.6), (0, -2.95)], 0.04, color=hexc("#6B4226"))
    p.shape([(0, -2.95), (0.38, -2.82), (0, -2.7)], color=SUN, outline=0.025)
    return p


# ---------------------------------------------------------------------------------------------------- plush prizes

def plush(i):
    """Eight plush toys for the prize shelf: dino, bear, bunny, cat, unicorn, octopus, panda, duck."""
    p = cell(f"plush{i}", 190, ppt=78)
    kinds = ["dino", "bear", "bunny", "cat", "unicorn", "octopus", "panda", "duck"]
    kind = kinds[i]
    col = {"dino": LEAF, "bear": hexc("#C98B5B"), "bunny": hexc("#FFC2DD"), "cat": hexc("#FFA94D"), "unicorn": hexc("#FBF6FF"),
           "octopus": hexc("#A98BFF"), "panda": WHITE, "duck": SUN}[kind]
    belly = lighter(col, 0.45)
    # ears and things behind the head
    if kind in ("bear", "panda"):
        ec = INK if kind == "panda" else col
        for side in (-1, 1):
            glossy(p, ellipse_pts(0.42 * side, -0.62, 0.18, 0.18, 24), ec, outline=0.035)
    if kind == "bunny":
        for side in (-1, 1):
            glossy(p, rotate(ellipse_pts(0.25 * side, -0.85, 0.13, 0.32, 32), 0.2 * side, 0.25 * side, -0.6), col, outline=0.035)
            p.shape(rotate(ellipse_pts(0.25 * side, -0.85, 0.06, 0.22, 24), 0.2 * side, 0.25 * side, -0.6), color=PINK, outline=0)
    if kind == "cat":
        for side in (-1, 1):
            glossy(p, [(0.15 * side, -0.62), (0.5 * side, -0.95), (0.55 * side, -0.45)], col, outline=0.035)
    if kind == "dino":
        for k in range(4):
            x = -0.3 + k * 0.2
            glossy(p, [(x - 0.09, -0.6), (x, -0.85 + 0.05 * abs(k - 1.5)), (x + 0.09, -0.6)], SUN, outline=0.03)
    if kind == "octopus":
        for k in range(6):
            x = -0.62 + k * 0.25
            glossy(p, catmull([(x, 0.2), (x + 0.12, 0.6), (x - 0.04, 0.9), (x + 0.1, 0.95), (x + 0.2, 0.55), (x + 0.14, 0.2)], True, 6), col,
                   outline=0.03, shade=0.2, radius=0.06)
    # body and arms
    if kind != "octopus":
        glossy(p, ellipse_pts(0, 0.55, 0.52, 0.42, 48), col, outline=0.035, shade=0.3, radius=0.15)
        p.shape(ellipse_pts(0, 0.6, 0.3, 0.26, 32), color=belly, outline=0)
        for side in (-1, 1):
            glossy(p, ellipse_pts(0.25 * side, 0.92, 0.16, 0.1, 24), INK if kind == "panda" else col, outline=0.03)
            glossy(p, rotate(ellipse_pts(0.5 * side, 0.45, 0.13, 0.2, 24), 0.4 * side, 0.5 * side, 0.45), INK if kind == "panda" else col, outline=0.03)
    head = ellipse_pts(0, -0.2, 0.58, 0.5, 64)
    glossy(p, head, col, outline=0.035, shade=0.3, radius=0.16)
    if kind == "panda":
        for side in (-1, 1):
            p.shape(rotate(ellipse_pts(0.22 * side, -0.2, 0.15, 0.11, 24), 0.5 * side, 0.22 * side, -0.2), color=INK, outline=0)
    if kind == "unicorn":
        horn = [(-0.08, -0.62), (0.0, -1.0), (0.08, -0.62)]
        glossy(p, horn, SUN, outline=0.03)
        for k, c in enumerate((CORAL, SUN, SKY, GRAPE)):
            glossy(p, ellipse_pts(0.42 - k * 0.08, -0.55 + k * 0.12, 0.13, 0.11, 20), c, outline=0.025)
    if kind == "duck":
        glossy(p, ellipse_pts(0, -0.02, 0.2, 0.1, 24), hexc("#FF9A3D"), outline=0.03)
    eyes(p, 0, -0.24, 0.085 if kind != "panda" else 0.07, 0.22)
    if kind != "duck":
        blush(p, 0, -0.04, 0.09, 0.35)
        p.shape(ellipse_pts(0, -0.1, 0.05, 0.035, 12), color=INK, outline=0)
        smile(p, 0, -0.03, 0.08, 0.025)
    if kind == "cat":
        for side in (-1, 1):
            for k in (-1, 1):
                p.stroke([(0.25 * side, -0.08 + 0.03 * k), (0.55 * side, -0.12 + 0.07 * k)], 0.015, color=INK)
    shine(p, -0.25, -0.45, 0.16, 0.07, alpha=0.5)
    return p


def shelf():
    p = cell("shelf", 760, 60, ppt=30)
    wood, dark = hexc("#C98B5B"), hexc("#8A5A35")
    glossy(p, [(-12.5, -0.6), (12.5, -0.6), (12.5, 0.6), (-12.5, 0.6)], wood, outline=0.12, shade=0.25, radius=0.4)
    for x in range(-12, 13, 3):
        p.stroke([(x + 0.5, -0.3), (x + 2.2, -0.25)], 0.08, color=dark)
    return p


# ---------------------------------------------------------------------------------------------------- Muri!'s wall

def wall_board():
    """The funhouse wall (512 px, opaque): diagonal stripes in the brand's colours inside a wooden frame studded with
    light bulbs, stars painted on. The game cuts the hole in the player's shape out of it (Tundu/Wall)."""
    s, ss = 512, 2
    W = s * ss
    yy, xx = np.mgrid[0:W, 0:W].astype(np.float32) / W   # 0..1
    img = np.zeros((W, W, 3), np.float32)
    stripes = (np.floor((xx + yy) * 9) % 2 == 0)
    img[:] = np.where(stripes[..., None], np.array(GRAPE), np.array(darker(GRAPE, 0.18)))
    # a big swirl of light in the middle, painted stars
    r = np.hypot(xx - 0.5, yy - 0.5)
    img += (np.clip(0.4 - r, 0, 1) * 0.35)[..., None] * np.array([1.0, 0.9, 0.6])
    canvas = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8))
    d = ImageDraw.Draw(canvas)
    rng = np.random.default_rng(7)
    for _ in range(14):
        cx, cy = rng.uniform(0.12, 0.88, 2) * W
        rr = rng.uniform(0.025, 0.05) * W
        pts = star_pts(cx, cy, rr, 0.45, 5)
        d.polygon(pts, fill=tuple(int(v * 255) for v in (SUN if rng.random() < 0.6 else WHITE)))
    # the wooden frame
    frame = 0.07 * W
    wood = tuple(int(v * 255) for v in hexc("#C98B5B"))
    dark = tuple(int(v * 255) for v in hexc("#7A4E2E"))
    for rect in ((0, 0, W, frame), (0, W - frame, W, W), (0, 0, frame, W), (W - frame, 0, W, W)):
        d.rectangle(rect, fill=wood)
    d.rectangle((frame, frame, W - frame, W - frame), outline=dark, width=int(0.012 * W))
    d.rectangle((2, 2, W - 3, W - 3), outline=dark, width=int(0.01 * W))
    # bulbs along the frame
    n = 9
    for k in range(n):
        t = (k + 0.5) / n * W
        for (x, y) in ((t, frame / 2), (t, W - frame / 2), (frame / 2, t), (W - frame / 2, t)):
            rr = frame * 0.32
            d.ellipse((x - rr * 1.6, y - rr * 1.6, x + rr * 1.6, y + rr * 1.6), fill=tuple(int(v * 255) for v in lighter(SUN, 0.1)) + (0,))
            d.ellipse((x - rr, y - rr, x + rr, y + rr), fill=tuple(int(v * 255) for v in lighter(SUN, 0.6)), outline=dark, width=3)
    out = canvas.resize((s, s), Image.LANCZOS)
    return np.asarray(out.convert("RGBA"))


# ---------------------------------------------------------------------------------------------------- the sheet

def white_cell():
    """A plain white square: gradients and solid shapes drawn from the sheet."""
    p = cell("white", 16)
    p.paint(np.ones((p.H, p.W), np.float32), color=WHITE)
    return p


def panel_cell():
    """A rounded panel (white: the game tints it) drawn as nine pieces: its corners stay round at any size (the
    middle 40 % stretches)."""
    p = cell("panel", 160, ppt=80)
    r = 0.42
    m = p.mask(catmull([(-1 + r, -0.97), (1 - r, -0.97), (0.97, -1 + r), (0.97, 1 - r), (1 - r, 0.97), (-1 + r, 0.97), (-0.97, 1 - r), (-0.97, -1 + r)], True, 8))
    X, Y = p.grid()
    p.paint(m, color=WHITE)
    p.paint(m * np.clip((Y - 0.2) / 0.8, 0, 1) * 0.12, color=(0.0, 0.0, 0.1))
    return p


def all_cells():
    cells = [white_cell(), panel_cell()]
    cells += fx_cells()
    cells += digit_cells()
    cells += [island(False), island(True), mole("normal"), mole("hit"), mole("gold"), hedgehog(), hammer()]
    for kind in ("apple", "orange", "melon", "banana", "berry", "pear", "star"):
        for part in ("whole", "left", "right"):
            cells.append(fruit(kind, part))
    cells.append(bomb())
    cells += [candy(i) for i in range(4)] + [lollipop(), floss(), balloon(), basket(False), basket(True)]
    for colour, tag in ((CORAL, "c"), (SKY, "s")):
        for wing in ("up", "mid", "down"):
            cells.append(bird(colour, f"bird_{tag}_{wing}", wing))
    cells += [cloud(False), cloud(True), hoop(), moon(), football(), beachball()]
    cells += [icon(n) for n in ("whack", "fruit", "wall", "catch", "bird", "ball")]
    cells += [awning(False), awning(True), wheel_rim(), wheel_legs()] + [cabin(i) for i in range(4)] + [tent()]
    cells += [plush(i) for i in range(8)] + [shelf()]
    return cells


def pack(images, width=2048, pad=10):
    """Shelf packing, tallest first: returns {name: (x, y, w, h)} and the sheet's height (a multiple of 16)."""
    order = sorted(images, key=lambda n: -images[n].shape[0])
    x = y = shelf_h = 0
    rects = {}
    for name in order:
        h, w = images[name].shape[:2]
        if x + w + pad > width:
            x = 0
            y += shelf_h + pad
            shelf_h = 0
        rects[name] = (x + pad, y + pad, w, h)
        x += w + pad
        shelf_h = max(shelf_h, h)
    # not a power of two: the game's import keeps the size (OpenGL ES 3 has no trouble with it), and a 2048 x 2048 sheet
    # would be one size too small while 4096 would waste a third of the memory
    return rects, (y + shelf_h + pad * 2 + 15) // 16 * 16


def main():
    ap = argparse.ArgumentParser(description="Lunapark!'s sprites as code")
    ap.add_argument("--preview", action="store_true")
    args = ap.parse_args()
    os.makedirs(ART, exist_ok=True)
    cells = all_cells()
    images, pivots, ppts = {}, {}, {}
    for c in cells:
        img, _ = c.image()
        images[c.name] = img
        pivots[c.name] = c.meta.get("pivot", (0.5, 0.5))
        ppts[c.name] = c.ppt
    rects, height = pack(images)
    sheet = np.zeros((height, 2048, 4), np.uint8)
    for name, (x, y, w, h) in rects.items():
        sheet[y:y + h, x:x + w] = images[name]
    Image.fromarray(sheet, "RGBA").save(os.path.join(ART, "arcade_sprites.png"), optimize=True)
    with open(os.path.join(ART, "arcade_sprites.json"), "w", encoding="utf-8") as f:
        json.dump({"size": [2048, height], "sprites": [{"name": n, "rect": list(r), "pivot": [round(v, 4) for v in pivots[n]], "ppt": round(ppts[n], 3)}
                                                      for n, r in sorted(rects.items())]}, f, indent=1)
    Image.fromarray(wall_board(), "RGBA").save(os.path.join(ART, "arcade_wall.png"), optimize=True)
    print(f"  {len(rects)} sprites -> {os.path.relpath(os.path.join(ART, 'arcade_sprites.png'), REPO)} (2048 x {height})")
    if args.preview:
        os.makedirs(OUT, exist_ok=True)
        bg = np.zeros((height, 2048, 4), np.uint8)
        bg[..., :3] = (np.array(NIGHT) * 255).astype(np.uint8)
        bg[..., 3] = 255
        a = sheet[..., 3:4].astype(np.float32) / 255
        comp = (sheet[..., :3] * a + bg[..., :3] * (1 - a)).astype(np.uint8)
        img = Image.fromarray(comp)
        d = ImageDraw.Draw(img)
        for name, (x, y, w, h) in rects.items():
            d.rectangle((x, y, x + w, y + h), outline=(60, 70, 120))
            d.text((x + 3, y + 2), name, fill=(255, 255, 255))
        img.save(os.path.join(OUT, "sheet.png"))
        Image.fromarray(wall_board()).save(os.path.join(OUT, "wall.png"))
        print(f"  preview -> {os.path.relpath(os.path.join(OUT, 'sheet.png'), REPO)}")


if __name__ == "__main__":
    main()
