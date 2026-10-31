"""Vishu!'s interface art as code: icons, pose figures, the three judges, stage curtains, particle shapes, and a
thumbnail of every item (same colour + tint-mask encoding as the outfit atlas, so the game colours them alike).

    motion-service-py/.venv/Scripts/python tools/dress/ui_art.py [--preview]

Run tools/dress/outfits.py first (the thumbnails are made from its pieces). Outputs in
unity/MoveItGames/Assets/Games/Dress/Art: dress_ui.png + dress_ui.json (named rects), dress_fx.png (4 x 2 particle
shapes, white, tinted by the particles), outfit_thumbs.png + outfit_thumbs_mask.png (8 x 8 cells of 160 px, in the
order of outfit_atlas.json's items).
"""
import argparse
import json
import math
import os
import sys

import cv2
import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from draw import (GOLD, GOLD_DARK, INK, WHITE, Piece, blur, bow, catmull, dilate, ellipse_pts, erode, heart_pts,  # noqa
                  hexc, pillow, rotate, star_pts, sym)
import preview  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
ART = os.path.join(REPO, "unity", "MoveItGames", "Assets", "Games", "Dress", "Art")
OUT = os.path.join(HERE, "out")


def cell(name, size=128, units=1.0):
    """A square cell; units -1..1 (times `units`) cover it, y down."""
    return Piece(name, size, size, (size / 2, size / 2), size / 2 / units)


def white(p, pts, outline=0.0, alpha=1.0):
    return p.shape(pts, color=WHITE, outline=outline, ink=(0.2, 0.15, 0.3), alpha=alpha)


def rgba(p):
    img, _ = p.image()
    return img


# ---------------------------------------------------------------------------------------------------- particles

def fx_cells():
    cells = []
    s = cell("sparkle")                       # a four-pointed twinkle with a soft glow and a bright core
    s.paint(blur(s.circle_mask(0, 0, 0.42), s.units(0.16)), color=WHITE, alpha=0.75)
    white(s, star_pts(0, 0, 0.82, 0.24, 4, rot=-math.pi / 2, round_=0))   # rays thick enough to survive small sizes
    white(s, star_pts(0, 0, 0.5, 0.3, 4, rot=-math.pi / 4, round_=0), alpha=0.7)
    s.paint(s.circle_mask(0, 0, 0.16), color=WHITE)
    cells.append(s)
    h = cell("heart")
    h.paint(blur(h.mask(heart_pts(0, 0.02, 0.8)), h.units(0.08)), color=WHITE, alpha=0.5)
    white(h, heart_pts(0, 0.02, 0.72))
    cells.append(h)
    b = cell("butterfly")
    for side in (-1, 1):
        white(b, catmull([(0.05 * side, -0.05), (0.55 * side, -0.75), (0.9 * side, -0.45), (0.6 * side, 0.0)], True, 8))
        white(b, catmull([(0.05 * side, 0.02), (0.6 * side, 0.15), (0.55 * side, 0.65), (0.2 * side, 0.5)], True, 8))
    b.shape(ellipse_pts(0, 0, 0.07, 0.42, 24), color=(0.55, 0.5, 0.6), outline=0.0)
    for side in (-1, 1):
        b.shape(ellipse_pts(0.45 * side, -0.38, 0.12, 0.12, 24), color=(0.75, 0.72, 0.8), outline=0.0)
    cells.append(b)
    u = cell("bubble")
    ring = np.clip(u.circle_mask(0, 0, 0.85) - u.circle_mask(0, 0, 0.72), 0, 1)
    u.paint(u.circle_mask(0, 0, 0.85), color=WHITE, alpha=0.18)
    u.paint(blur(ring, u.units(0.03)), color=WHITE, alpha=0.9)
    u.paint(u.mask(rotate(ellipse_pts(-0.35, -0.4, 0.2, 0.09, 24), -0.7, -0.35, -0.4)), color=WHITE, alpha=0.9)
    cells.append(u)
    f = cell("snowflake")
    for k in range(6):
        a = math.pi / 6 + math.pi * k / 3
        f.stroke([(0, 0), (math.cos(a) * 0.85, math.sin(a) * 0.85)], 0.12, color=WHITE)
        for t in (0.45, 0.7):
            bx, by = math.cos(a) * 0.85 * t, math.sin(a) * 0.85 * t
            for sgn in (-1, 1):
                f.stroke([(bx, by), (bx + math.cos(a + sgn * 0.8) * 0.25, by + math.sin(a + sgn * 0.8) * 0.25)], 0.09,
                         color=WHITE)
    cells.append(f)
    t = cell("star")
    t.paint(blur(t.mask(star_pts(0, 0.05, 0.9, 0.45, 5)), t.units(0.08)), color=WHITE, alpha=0.5)
    white(t, star_pts(0, 0.05, 0.82, 0.45, 5, round_=1))
    cells.append(t)
    c = cell("confetti")
    white(c, [(-0.55, -0.3), (0.55, -0.3), (0.55, 0.3), (-0.55, 0.3)])
    cells.append(c)
    cells.append(glow_cell())
    return cells


def glow_cell(size=128):
    g = cell("glow", size)
    X, Y = g.grid()
    g.paint(np.clip(1 - np.hypot(X, Y), 0, 1) ** 2.2, color=WHITE)
    return g


# ---------------------------------------------------------------------------------------------------- icons

def icon_top(p):
    half = [(0, -0.5), (0.25, -0.6), (0.62, -0.42), (0.85, -0.05), (0.62, 0.08), (0.48, -0.12), (0.45, 0.6), (0, 0.66)]
    white(p, sym(half, sharp=[0, 2, 3, 4, 5, 6, 7]))
    p.shape(sym([(0, -0.5), (0.25, -0.6), (0, -0.28)], sharp=[0, 1, 2]), color=(0.75, 0.72, 0.85), outline=0)


def icon_bottom(p):
    white(p, sym([(0, -0.6), (0.35, -0.6), (0.85, 0.62), (0, 0.7)], sharp=[0, 1, 2, 3]))
    p.shape([(-0.38, -0.6), (0.38, -0.6), (0.4, -0.4), (-0.4, -0.4)], color=(0.75, 0.72, 0.85), outline=0)


def icon_hair(p):
    white(p, catmull([(0, -0.85), (0.55, -0.6), (0.7, 0.0), (0.75, 0.75), (0.35, 0.8), (0.3, 0.0), (0.0, -0.25),
                      (-0.3, 0.0), (-0.35, 0.8), (-0.75, 0.75), (-0.7, 0.0), (-0.55, -0.6)], True, 8))
    p.shape(ellipse_pts(0, 0.12, 0.3, 0.38, 32), color=(0.75, 0.72, 0.85), outline=0)


def icon_crown(p):
    white(p, [(-0.8, 0.5), (-0.85, -0.45), (-0.4, 0.0), (0, -0.7), (0.4, 0.0), (0.85, -0.45), (0.8, 0.5)])
    for x, y in ((-0.85, -0.5), (0, -0.75), (0.85, -0.5)):
        white(p, ellipse_pts(x, y, 0.13, 0.13, 20))


def icon_glasses(p):
    for side in (-1, 1):
        r = np.clip(p.mask(heart_pts(0.42 * side, 0.0, 0.45)) - p.mask(heart_pts(0.42 * side, 0.02, 0.28)), 0, 1)
        p.paint(r, color=WHITE)
    p.stroke([(-0.12, -0.1), (0.12, -0.1)], 0.12, color=WHITE)


def icon_wings(p):
    for side in (-1, 1):
        white(p, catmull([(0.05 * side, 0.0), (0.45 * side, -0.85), (0.95 * side, -0.55), (0.5 * side, 0.05)], True, 8))
        white(p, catmull([(0.05 * side, 0.08), (0.6 * side, 0.2), (0.6 * side, 0.75), (0.2 * side, 0.55)], True, 8))


def icon_wand(p):
    p.stroke([(-0.55, 0.75), (0.2, -0.1)], 0.14, color=WHITE)
    white(p, star_pts(0.32, -0.3, 0.5, 0.45, 5, round_=1))


def icon_magic(p):
    white(p, star_pts(-0.15, 0.1, 0.62, 0.15, 4, rot=-math.pi / 2))
    white(p, star_pts(0.5, -0.5, 0.32, 0.15, 4, rot=-math.pi / 2))
    white(p, star_pts(0.55, 0.55, 0.22, 0.15, 4, rot=-math.pi / 2))


def icon_dice(p):
    white(p, catmull([(-0.7, -0.7), (0.7, -0.7), (0.7, 0.7), (-0.7, 0.7)], True, 3))
    for x, y in ((-0.35, -0.35), (0.35, 0.35), (0, 0), (0.35, -0.35), (-0.35, 0.35)):
        p.shape(ellipse_pts(x, y, 0.13, 0.13, 20), color=(0.3, 0.22, 0.4), outline=0)


def icon_show(p):
    white(p, star_pts(0, 0.05, 0.9, 0.45, 5, round_=1))


def icon_photo(p):
    white(p, catmull([(-0.85, -0.45), (-0.35, -0.45), (-0.25, -0.65), (0.25, -0.65), (0.35, -0.45), (0.85, -0.45),
                      (0.85, 0.65), (-0.85, 0.65)], True, 3))
    p.shape(ellipse_pts(0, 0.08, 0.4, 0.4, 32), color=(0.3, 0.22, 0.4), outline=0)
    p.shape(ellipse_pts(0, 0.08, 0.25, 0.25, 32), color=WHITE, outline=0)


def icon_remove(p):
    p.stroke([(-0.6, -0.6), (0.6, 0.6)], 0.24, color=WHITE)
    p.stroke([(0.6, -0.6), (-0.6, 0.6)], 0.24, color=WHITE)


def icon_ready(p):
    p.stroke([(-0.65, 0.05), (-0.2, 0.5), (0.7, -0.5)], 0.26, color=WHITE)


def icon_none(p):
    ring = np.clip(p.circle_mask(0, 0, 0.75) - p.circle_mask(0, 0, 0.55), 0, 1)
    p.paint(ring, color=WHITE)
    p.stroke([(-0.5, 0.5), (0.5, -0.5)], 0.18, color=WHITE)


def icon_hand(p):
    """A raised open hand (for 'hold your hand here')."""
    white(p, catmull([(-0.5, 0.85), (-0.55, 0.1), (-0.75, -0.2), (-0.62, -0.32), (-0.4, -0.05), (-0.42, -0.7),
                      (-0.28, -0.78), (-0.18, -0.2), (-0.12, -0.9), (0.04, -0.9), (0.06, -0.2), (0.16, -0.8),
                      (0.32, -0.75), (0.28, -0.12), (0.42, -0.55), (0.56, -0.5), (0.48, 0.2), (0.4, 0.85)], True, 4))


# ---------------------------------------------------------------------------------------------------- poses

POSES = {   # stick figures: [(joint a, joint b)], joints in -1..1 (y down)
    "star":   {"lw": (-0.75, -0.75), "le": (-0.4, -0.5), "rw": (0.75, -0.75), "re": (0.4, -0.5), "lk": (-0.3, 0.55),
               "la": (-0.5, 0.95), "rk": (0.3, 0.55), "ra": (0.5, 0.95)},
    "hips":   {"lw": (-0.18, 0.12), "le": (-0.48, -0.08), "rw": (0.18, 0.12), "re": (0.48, -0.08), "lk": (-0.12, 0.55),
               "la": (-0.12, 0.95), "rk": (0.12, 0.55), "ra": (0.12, 0.95)},
    "heart":  {"lw": (-0.06, -0.95), "le": (-0.45, -0.85), "rw": (0.06, -0.95), "re": (0.45, -0.85), "lk": (-0.12, 0.55),
               "la": (-0.12, 0.95), "rk": (0.12, 0.55), "ra": (0.12, 0.95)},
    "wave":   {"lw": (-0.3, 0.2), "le": (-0.28, -0.1), "rw": (0.55, -0.9), "re": (0.5, -0.55), "lk": (-0.12, 0.55),
               "la": (-0.12, 0.95), "rk": (0.12, 0.55), "ra": (0.12, 0.95)},
    "super":  {"lw": (-0.18, 0.12), "le": (-0.48, -0.08), "rw": (0.25, -1.0), "re": (0.25, -0.62), "lk": (-0.2, 0.55),
               "la": (-0.3, 0.95), "rk": (0.2, 0.55), "ra": (0.3, 0.95)},
    "plane":  {"lw": (-0.95, -0.25), "le": (-0.6, -0.27), "rw": (0.95, -0.25), "re": (0.6, -0.27), "lk": (-0.12, 0.55),
               "la": (-0.12, 0.95), "rk": (0.12, 0.55), "ra": (0.12, 0.95)},
}


def pose_icon(name, joints):
    p = cell("pose_" + name, 192, 1.12)
    ls, rs, lh, rh = (-0.22, -0.3), (0.22, -0.3), (-0.13, 0.15), (0.13, 0.15)
    j = joints
    w = 0.13
    for a, b in ((ls, j["le"]), (j["le"], j["lw"]), (rs, j["re"]), (j["re"], j["rw"]), (lh, j["lk"]), (j["lk"], j["la"]),
                 (rh, j["rk"]), (j["rk"], j["ra"])):
        p.stroke([a, b], w, color=WHITE)
    white(p, catmull([ls, rs, rh, lh], True, 3))
    white(p, ellipse_pts(0, -0.58, 0.2, 0.2, 32))
    return p


# ---------------------------------------------------------------------------------------------------- judges

def judge_cat():
    """Mimi the cat: pink, a bow, big eyes."""
    p = cell("judge_cat", 256)
    pink, inner, ink = hexc("#FFB3D6"), hexc("#FF7EC8"), hexc("#3A2547")
    p.shape(sym([(0, 0.6), (0.6, 0.65), (0.8, 1.0), (0, 1.0)]), color=hexc("#B08CFF"), outline=0.025, ink=ink)
    for side in (-1, 1):
        p.shape(catmull([(0.25 * side, -0.55), (0.62 * side, -0.95), (0.72 * side, -0.3)], True, 6, sharp=(1,)),
                color=pink, outline=0.025, ink=ink)
        p.shape(catmull([(0.35 * side, -0.5), (0.6 * side, -0.8), (0.64 * side, -0.38)], True, 6, sharp=(1,)),
                color=inner, outline=0)
    head = p.shape(ellipse_pts(0, -0.05, 0.72, 0.62, 64), color=pink, outline=0.025, shade=0.25, shade_radius=0.1,
                   ink=ink)
    face(p, ink, cheeks=inner)
    for side in (-1, 1):
        for k in (-1, 0, 1):
            p.stroke([(0.38 * side, 0.12 + k * 0.08), (0.85 * side, 0.08 + k * 0.14)], 0.018, color=ink)
    bow(p, 0.42, -0.55, 0.22, color=hexc("#FF4F8B"), rot=-0.4)
    return p


def judge_bunny():
    """Lumi the bunny: white, round glasses, a little tie."""
    p = cell("judge_bunny", 256)
    fur_c, inner, ink = hexc("#FBF8FF"), hexc("#FFC2E2"), hexc("#3A2547")
    p.shape(sym([(0, 0.6), (0.6, 0.65), (0.8, 1.0), (0, 1.0)]), color=hexc("#6EC3FF"), outline=0.025, ink=ink)
    for side in (-1, 1):
        ear = rotate(ellipse_pts(0.28 * side, -0.72, 0.17, 0.42, 48), 0.25 * side, 0.28 * side, -0.4)
        p.shape(ear, color=fur_c, outline=0.025, ink=ink, shade=0.2, shade_radius=0.08)
        p.shape(rotate(ellipse_pts(0.28 * side, -0.72, 0.08, 0.3, 48), 0.25 * side, 0.28 * side, -0.4), color=inner,
                outline=0)
    p.shape(ellipse_pts(0, 0.0, 0.68, 0.6, 64), color=fur_c, outline=0.025, shade=0.25, shade_radius=0.1, ink=ink)
    face(p, ink, cheeks=inner, teeth=True)
    for side in (-1, 1):
        ring = np.clip(p.circle_mask(0.25 * side, -0.07, 0.22) - p.circle_mask(0.25 * side, -0.07, 0.17), 0, 1)
        p.paint(ring, color=hexc("#7B4DDB"))
    p.stroke([(-0.06, -0.08), (0.06, -0.08)], 0.04, color=hexc("#7B4DDB"))
    p.shape([(0, 0.68), (-0.12, 0.62), (-0.12, 0.76)], color=hexc("#FF5A4E"), outline=0.015, ink=ink)
    p.shape([(0, 0.68), (0.12, 0.62), (0.12, 0.76)], color=hexc("#FF5A4E"), outline=0.015, ink=ink)
    return p


def judge_dino():
    """Tundu the dino (from Vrapo!): green, round, a tiny crown of spikes."""
    p = cell("judge_dino", 256)
    green, belly, ink = hexc("#6FD66A"), hexc("#FFF0B8"), hexc("#3A2547")
    p.shape(sym([(0, 0.55), (0.65, 0.6), (0.85, 1.0), (0, 1.0)]), color=green, outline=0.025, ink=ink)
    p.shape(sym([(0, 0.6), (0.32, 0.66), (0.4, 1.0), (0, 1.0)]), color=belly, outline=0)
    for k, x in enumerate((-0.32, 0.0, 0.32)):
        p.shape([(x - 0.13, -0.5), (x, -0.85 + 0.08 * abs(k - 1)), (x + 0.13, -0.5)], color=hexc("#FFC93C"),
                outline=0.022, ink=ink)
    p.shape(ellipse_pts(0, 0.0, 0.75, 0.6, 64), color=green, outline=0.025, shade=0.25, shade_radius=0.1, ink=ink)
    face(p, ink, cheeks=hexc("#FF9FB5"), eye_y=-0.12)
    for side in (-1, 1):
        p.shape(ellipse_pts(0.12 * side, 0.12, 0.035, 0.025, 16), color=ink, outline=0)
    return p


def face(p, ink, cheeks, eye_y=-0.1, teeth=False):
    for side in (-1, 1):
        p.shape(ellipse_pts(0.25 * side, eye_y, 0.11, 0.14, 40), color=ink, outline=0)
        p.shape(ellipse_pts(0.25 * side - 0.035, eye_y - 0.05, 0.045, 0.05, 20), color=WHITE, outline=0)
        p.shape(ellipse_pts(0.25 * side + 0.04, eye_y + 0.05, 0.02, 0.02, 12), color=WHITE, outline=0)
        p.shape(ellipse_pts(0.45 * side, eye_y + 0.22, 0.12, 0.07, 32), color=cheeks, outline=0, alpha=0.7)
    p.stroke(catmull([(-0.14, eye_y + 0.27), (-0.07, eye_y + 0.33), (0, eye_y + 0.28), (0.07, eye_y + 0.33),
                      (0.14, eye_y + 0.27)], False, 6), 0.03, color=ink)
    if teeth:
        p.shape([(-0.05, eye_y + 0.3), (0.05, eye_y + 0.3), (0.05, eye_y + 0.4), (-0.05, eye_y + 0.4)], color=WHITE,
                outline=0.012, ink=ink)


# ---------------------------------------------------------------------------------------------------- stage

def curtain():
    """Red velvet stage curtain (one side), folds and a gold tassel tie: 192 x 768."""
    p = Piece("curtain", 192, 768, (0, 0), 192)
    X, Y = p.grid()                       # x 0..1, y 0..4
    edge = 0.75 + 0.1 * np.sin(Y * 2.0) - 0.25 * np.exp(-((Y - 2.3) / 0.35) ** 2)   # gathered in at the tie
    m = (X < edge).astype(np.float32)
    folds = 0.5 + 0.5 * np.sin(X * 28 + np.sin(Y * 1.5) * 0.6)
    red = np.dstack([0.55 + 0.35 * folds, 0.05 + 0.07 * folds, 0.12 + 0.08 * folds])
    p.paint(m, color=red)
    p.paint(np.clip(m * ((edge - X) < 0.03), 0, 1), color=GOLD, alpha=0.9)
    tie = p.mask(ellipse_pts(edge.mean() - 0.12, 2.3, 0.2, 0.06, 32))
    p.fill(tie, color=GOLD, outline=0.01, shade=0.4, shade_radius=0.03)
    p.shape(catmull([(0.55, 2.32), (0.62, 2.7), (0.5, 2.72)], True, 6), color=GOLD_DARK, outline=0.008)
    valance = (Y < 0.32 + 0.05 * np.sin(X * 20)).astype(np.float32)
    p.paint(valance, color=(0.6, 0.06, 0.14))
    p.paint(((Y > 0.26) & (Y < 0.32 + 0.05 * np.sin(X * 20))).astype(np.float32), color=GOLD)
    return p


def bulb():
    p = cell("bulb", 64)
    p.paint(blur(p.circle_mask(0, 0, 0.55), p.units(0.25)), color=(1, 0.95, 0.75), alpha=0.8)
    p.shape(ellipse_pts(0, 0, 0.42, 0.42, 32), color=(1, 0.98, 0.88), outline=0.06, ink=GOLD_DARK)
    p.shape(ellipse_pts(-0.14, -0.15, 0.13, 0.1, 16), color=WHITE, outline=0)
    return p


def star_full():
    p = cell("star_full", 128)
    p.shape(star_pts(0, 0.06, 0.92, 0.48, 5, round_=1), color=hexc("#FFC93C"), outline=0.06, ink=hexc("#B07A1C"),
            shade=0.4, shade_radius=0.12)
    p.shape(rotate(ellipse_pts(-0.25, -0.15, 0.16, 0.08, 20), -0.6, -0.25, -0.15), color=WHITE, outline=0, alpha=0.9)
    return p


def star_empty():
    p = cell("star_empty", 128)
    p.shape(star_pts(0, 0.06, 0.92, 0.48, 5, round_=1), color=(0.32, 0.3, 0.42), outline=0.06, ink=(0.2, 0.18, 0.28),
            alpha=0.9)
    return p


def panel():
    """A rounded panel (9-slice: 24 px borders)."""
    p = Piece("panel", 96, 96, (0, 0), 96)
    X, Y = p.grid()
    r = 22 / 96
    dx = np.maximum(np.maximum(r - X, X - (1 - r)), 0)
    dy = np.maximum(np.maximum(r - Y, Y - (1 - r)), 0)
    m = (np.hypot(dx, dy) <= r).astype(np.float32)
    p.paint(m, color=WHITE)
    return p


def bubble_button():
    """A round glossy button (white, tinted by the game)."""
    p = cell("bubble_button", 160)
    m = p.circle_mask(0, 0, 0.94)
    X, Y = p.grid()
    p.paint(m, color=np.dstack([np.clip(0.86 - Y * 0.12, 0, 1)] * 3))
    p.paint(np.clip(m - p.circle_mask(0, 0, 0.84), 0, 1), color=WHITE, alpha=0.9)
    p.paint(blur(p.mask(ellipse_pts(-0.25, -0.5, 0.42, 0.2, 32)), p.units(0.06)), color=WHITE, alpha=0.7)
    return p


ICONS = {"cat_top": icon_top, "cat_bottom": icon_bottom, "cat_hair": icon_hair, "cat_head": icon_crown,
         "cat_face": icon_glasses, "cat_back": icon_wings, "cat_hand": icon_wand, "act_magic": icon_magic,
         "act_dice": icon_dice, "act_show": icon_show, "act_photo": icon_photo, "act_remove": icon_remove,
         "act_ready": icon_ready, "none": icon_none, "hand": icon_hand}


def theme_icon(name, draw):
    p = cell("theme_" + name, 160)
    draw(p)
    return p


def th_princess(p):
    p.shape([(-0.8, 0.45), (-0.85, -0.45), (-0.4, 0.0), (0, -0.7), (0.4, 0.0), (0.85, -0.45), (0.8, 0.45)],
            color=GOLD, outline=0.05, ink=GOLD_DARK, shade=0.4, shade_radius=0.1)
    p.shape(ellipse_pts(0, 0.15, 0.13, 0.15, 6), color=hexc("#FF3B6B"), outline=0.03, ink=INK)


def th_fairy(p):
    for side in (-1, 1):
        p.shape(catmull([(0.05 * side, 0.0), (0.45 * side, -0.85), (0.95 * side, -0.55), (0.5 * side, 0.05)], True, 8),
                color=hexc("#7FE3C0"), outline=0.04, ink=hexc("#2E8B6A"), alpha=0.95)
        p.shape(catmull([(0.05 * side, 0.08), (0.6 * side, 0.2), (0.6 * side, 0.75), (0.2 * side, 0.55)], True, 8),
                color=hexc("#B3F0DA"), outline=0.04, ink=hexc("#2E8B6A"))
    p.shape(ellipse_pts(0, 0.0, 0.08, 0.4, 20), color=hexc("#FF7EC8"), outline=0.03, ink=INK)


def th_mermaid(p):
    p.shape(catmull([(-0.25, -0.85), (0.25, -0.85), (0.2, 0.2), (0.05, 0.45), (-0.05, 0.45), (-0.2, 0.2)], True, 8),
            color=hexc("#2EC9D0"), outline=0.05, ink=hexc("#16707A"), shade=0.3, shade_radius=0.1)
    for side in (-1, 1):
        p.shape(catmull([(0, 0.4), (0.4 * side, 0.55), (0.75 * side, 0.9), (0.3 * side, 0.85)], True, 8),
                color=hexc("#7DE8EC"), outline=0.05, ink=hexc("#16707A"))


def th_hero(p):
    p.shape(ellipse_pts(0, 0, 0.88, 0.88, 64), color=hexc("#E8304A"), outline=0.05, ink=INK)
    p.shape(star_pts(0, 0.04, 0.62, 0.45, 5), color=WHITE, outline=0.0)


def th_summer(p):
    for k in range(12):
        a = 2 * math.pi * k / 12
        p.shape([(math.cos(a - 0.12) * 0.55, math.sin(a - 0.12) * 0.55), (math.cos(a) * 0.95, math.sin(a) * 0.95),
                 (math.cos(a + 0.12) * 0.55, math.sin(a + 0.12) * 0.55)], color=hexc("#FF9F1C"), outline=0.0)
    p.shape(ellipse_pts(0, 0, 0.58, 0.58, 48), color=hexc("#FFD23F"), outline=0.04, ink=hexc("#C77700"), shade=0.3,
            shade_radius=0.1)


def th_winter(p):
    for k in range(6):
        a = math.pi / 6 + math.pi * k / 3
        p.stroke([(0, 0), (math.cos(a) * 0.9, math.sin(a) * 0.9)], 0.16, color=hexc("#6EC3FF"))
        for t in (0.45, 0.7):
            bx, by = math.cos(a) * 0.9 * t, math.sin(a) * 0.9 * t
            for sgn in (-1, 1):
                p.stroke([(bx, by), (bx + math.cos(a + sgn * 0.8) * 0.28, by + math.sin(a + sgn * 0.8) * 0.28)], 0.12,
                         color=hexc("#6EC3FF"))
    p.paint(dilate(p.coverage(), p.units(0.04)), color=WHITE, under=True)


def th_party(p):
    p.shape(heart_pts(-0.15, -0.15, 0.62), color=hexc("#FF4F8B"), outline=0.05, ink=INK, shade=0.4, shade_radius=0.12)
    p.stroke(catmull([(-0.15, 0.45), (-0.05, 0.65), (-0.2, 0.9)], False, 6), 0.05, color=INK)
    for k, (x, y, c) in enumerate(((0.6, -0.6, "#FFD23F"), (0.75, 0.1, "#6EC3FF"), (0.45, 0.6, "#6FD66A"))):
        p.shape(rotate([(x - 0.1, y - 0.06), (x + 0.1, y - 0.06), (x + 0.1, y + 0.06), (x - 0.1, y + 0.06)], k, x, y),
                color=hexc(c), outline=0.02, ink=INK)


THEMES = {"princess": th_princess, "fairy": th_fairy, "mermaid": th_mermaid, "hero": th_hero, "summer": th_summer,
          "winter": th_winter, "party": th_party}


# ---------------------------------------------------------------------------------------------------- thumbnails

def thumbnails(manifest):
    """Every item alone, centred in a 160 px cell: colour + tint mask, from the outfit atlas."""
    atlas = np.asarray(Image.open(os.path.join(ART, "outfit_atlas.png")).convert("RGBA"))
    mask_half = np.asarray(Image.open(os.path.join(ART, "outfit_mask.png")).convert("L"))
    mask = cv2.resize(mask_half, (atlas.shape[1], atlas.shape[0]), interpolation=cv2.INTER_LINEAR)
    images = {}
    for name, info in manifest["pieces"].items():
        x, y, w, h = info["rect"]
        images[name] = (atlas[y:y + h, x:x + w], mask[y:y + h, x:x + w])
    size = 160
    items = [i for i in manifest["items"] if i not in ("doll", "dollhair")]
    cols = 8
    thumb = np.zeros((cols * size, cols * size, 4), np.float32)
    tmask = np.zeros((cols * size, cols * size), np.float32)
    order = []
    for k, item in enumerate(items):
        parts = manifest["items"][item]
        # draw at a large scale (premultiplied colour + mask), crop to the item, fit the cell
        canvas = np.zeros((1400, 1000, 4), np.float32)
        cmask = np.zeros((1400, 1000), np.float32)
        for name in sorted(parts, key=lambda n: preview.LAYERS.index(manifest["pieces"][n]["layer"])):
            rgba, m = images[name]
            place2(canvas, cmask, rgba.astype(np.float32) / 255, m.astype(np.float32) / 255, manifest["pieces"][name])
        a = canvas[..., 3]
        ys, xs = np.nonzero(a > 0.02)
        if len(xs) == 0:
            continue
        x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
        s = (size - 16) / max(y1 - y0, x1 - x0)
        dims = (max(1, int((x1 - x0) * s)), max(1, int((y1 - y0) * s)))
        small = cv2.resize(canvas[y0:y1, x0:x1], dims, interpolation=cv2.INTER_AREA)
        small_m = cv2.resize(cmask[y0:y1, x0:x1], dims, interpolation=cv2.INTER_AREA)
        cx = (k % cols) * size + (size - small.shape[1]) // 2
        cy = (k // cols) * size + (size - small.shape[0]) // 2
        aa = small[..., 3:4]
        thumb[cy:cy + small.shape[0], cx:cx + small.shape[1], :3] = small[..., :3] / np.maximum(aa, 1e-4)
        thumb[cy:cy + small.shape[0], cx:cx + small.shape[1], 3] = small[..., 3]
        tmask[cy:cy + small.shape[0], cx:cx + small.shape[1]] = small_m / np.maximum(small[..., 3], 1e-4)
        order.append(item)
    out = (np.clip(thumb, 0, 1) * 255 + 0.5).astype(np.uint8)
    Image.fromarray(out, "RGBA").save(os.path.join(ART, "outfit_thumbs.png"), optimize=True)
    Image.fromarray((np.clip(tmask, 0, 1) * 255 + 0.5).astype(np.uint8), "L").save(os.path.join(ART, "outfit_thumbs_mask.png"))
    return {item: [(k % cols) * size, (k // cols) * size, size, size] for k, item in enumerate(order)}


def place2(canvas, cmask, rgba, mask, info, scale=260):
    """Premultiplied 'over' of a piece (rgba + its tint mask) in the body template, like preview.render_look (arms on
    the doll's arms, wings mirrored, hand items at the grip)."""
    rig = info["rig"]
    cx, top = canvas.shape[1] / 2, 1.05
    to = lambda x, y: (cx + x * scale, (y + top) * scale)
    jobs = []
    if rig == "arm":
        for flip in (False, True):
            sx = -1 if flip else 1
            sh, el = (preview.SHOULDER[0] * sx, preview.SHOULDER[1]), (preview.ELBOW[0] * sx, preview.ELBOW[1])
            jobs.append((to(*sh), preview.bone_angle(sh, el), flip))
    elif rig == "hand":
        jobs.append((to(0, 0), 0.0, False))
    elif info.get("side") == "both":
        jobs += [(to(0, 0), 0.0, False), (to(0, 0), 0.0, True)]
    else:
        jobs.append((to(0, 0), 0.0, False))
    h, w = rgba.shape[:2]
    if mask.shape != rgba.shape[:2]:
        mask = cv2.resize(mask, (w, h), interpolation=cv2.INTER_LINEAR)
    for origin, angle, flip in jobs:
        img, m = (rgba[:, ::-1], mask[:, ::-1]) if flip else (rgba, mask)
        ox, oy = info["origin"]
        if flip:
            ox = w - ox
        k = scale / info["ppt"]
        c, s = math.cos(angle), math.sin(angle)
        M = np.array([[k * c, -k * s, 0], [k * s, k * c, 0]], np.float32)
        M[:, 2] = np.array(origin, np.float32) - M[:, :2] @ np.array([ox, oy], np.float32)
        pm = np.ascontiguousarray(img.copy())
        pm[..., :3] *= pm[..., 3:4]
        pmm = np.ascontiguousarray(m * img[..., 3])
        size = (canvas.shape[1], canvas.shape[0])
        warped = cv2.warpAffine(pm, M, size, flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=0)
        warped_m = cv2.warpAffine(pmm, M, size, flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=0)
        a = warped[..., 3:4]
        canvas[..., :3] = warped[..., :3] + canvas[..., :3] * (1 - a)
        cmask[:] = warped_m + cmask * (1 - a[..., 0])
        canvas[..., 3:4] = a + canvas[..., 3:4] * (1 - a)


# ---------------------------------------------------------------------------------------------------- build

def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--preview", action="store_true")
    args = ap.parse_args()
    os.makedirs(ART, exist_ok=True)

    fx = fx_cells()
    sheet = np.zeros((256, 512, 4), np.uint8)
    for k, c in enumerate(fx):
        sheet[(k // 4) * 128:(k // 4 + 1) * 128, (k % 4) * 128:(k % 4 + 1) * 128] = rgba(c)
    Image.fromarray(sheet, "RGBA").save(os.path.join(ART, "dress_fx.png"))

    cells = []
    for name, f in ICONS.items():
        p = cell(name, 128)
        f(p)
        cells.append(p)
    cells += [pose_icon(n, j) for n, j in POSES.items()]
    cells += [theme_icon(n, f) for n, f in THEMES.items()]
    cells += [judge_cat(), judge_bunny(), judge_dino(), curtain(), bulb(), star_full(), star_empty(), panel(),
              bubble_button(), glow_cell()]
    images = [(c.name, rgba(c)) for c in cells]
    images.sort(key=lambda t: (-t[1].shape[0], -t[1].shape[1]))
    width = 1024
    pos, x, y, shelf = {}, 0, 0, 0
    for name, img in images:
        h, w = img.shape[:2]
        if x + w + 8 > width:
            x, y, shelf = 0, y + shelf + 8, 0
        pos[name] = (x, y, w, h)
        x += w + 8
        shelf = max(shelf, h)
    height = 1 << (y + shelf - 1).bit_length()
    ui = np.zeros((height, width, 4), np.uint8)
    for name, img in images:
        x, y, w, h = pos[name]
        ui[y:y + h, x:x + w] = img
    Image.fromarray(ui, "RGBA").save(os.path.join(ART, "dress_ui.png"), optimize=True)

    from outfits import load_manifest
    manifest = load_manifest(os.path.join(ART, "outfit_atlas.json"))
    thumbs = thumbnails(manifest)
    with open(os.path.join(ART, "dress_ui.json"), "w", encoding="utf-8") as f:
        json.dump({"size": [width, height], "rects": [{"name": n, "rect": list(r)} for n, r in pos.items()],
                   "thumbSize": [1280, 1280], "thumbs": [{"name": n, "rect": r} for n, r in thumbs.items()]}, f, indent=1)
    print(f"ui {width} x {height}: {len(pos)} images; fx 8; thumbnails {len(thumbs)}")
    if args.preview:
        os.makedirs(OUT, exist_ok=True)
        bg = np.zeros_like(ui)
        bg[..., :3] = (60, 50, 90)
        bg[..., 3] = 255
        a = ui[..., 3:4].astype(np.float32) / 255
        comp = (ui[..., :3] * a + bg[..., :3] * (1 - a)).astype(np.uint8)
        Image.fromarray(comp).save(os.path.join(OUT, "ui.png"))
        t = np.asarray(Image.open(os.path.join(ART, "outfit_thumbs.png")).convert("RGBA")).astype(np.float32) / 255
        tm = np.asarray(Image.open(os.path.join(ART, "outfit_thumbs_mask.png"))).astype(np.uint8)
        col = preview.tint((t * 255).astype(np.uint8), tm, (1.0, 0.5, 0.78))
        comp = col[..., :3] * col[..., 3:] + np.array([0.24, 0.2, 0.35]) * (1 - col[..., 3:])
        Image.fromarray((comp * 255).astype(np.uint8)).save(os.path.join(OUT, "thumbs.png"))


if __name__ == "__main__":
    main()
