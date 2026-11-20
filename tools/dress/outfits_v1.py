"""Vishu!'s wardrobe as code: every outfit piece drawn in the shared body template (draw.py), packed into one atlas
for Unity, with a manifest that tells the game's rig how each piece follows the player.

    motion-service-py/.venv/Scripts/python tools/dress/outfits.py            # atlas + manifest + thumbnails
    motion-service-py/.venv/Scripts/python tools/dress/outfits.py --preview  # also tools/dress/out/*.png

Outputs (unity/MoveItGames/Assets/Games/Dress/Art):
    outfit_atlas.png       colour (RGBA): fixed colours, or a grey where the piece takes the chosen colour
    outfit_mask.png        tint mask (grey, half size): 1 = this pixel shows the chosen colour
    outfit_atlas.json      pieces: atlas rect, template origin + scale, rig, layer, sparkle points, ...
    outfit_thumbs.png      one 160x160 thumbnail per item (same encoding), outfit_thumbs_mask.png

The shader (Tundu/Outfit) colours a tinted pixel as tint * min(grey / 0.8, 1), turning toward white above 0.8.

Rigs (Unity: Games/Dress/Scripts/OutfitRig.cs):
    torso   tops:     rows at the shoulders (y 0, half width 0.40) and the hips (y 1, 0.24), bilinear
    skirt   skirts:   the waist row (torso at y 0.8), then rigid down along the thighs (never pinched by the knees)
    tail    mermaid:  waist row, knees row, ankles row
    arm     sleeves:  own frame along the arm: y 0 shoulder, 0.64 elbow, 1.21 wrist; x across (torso lengths)
    head    hats:     a similarity from the eyes (template eyes at +-0.064, -0.47; head width 0.30)
    hair    wigs:     head, but below "hang" the hair hangs down (gravity) and sways
    face    glasses:  x from the two eyes, y from the head size
    back    wings:    shoulder centre + torso length + shoulder line; flaps around "pivot"
    cape    capes:    the shoulder row, then rigid down, the hem sways
    hand    held:     own frame, the grip at (0, 0), the item up (-y); mode along / upright / hang / float
Layers, back to front: back (hidden where the body is), bottom, top (both hidden where the arms are), sleeve,
hairfront, head, face, hand.
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
from draw import (BASE, GOLD, GOLD_DARK, INK, PEARL, WHITE, Piece, blur, bow, catmull, dilate, dots,  # noqa: E402
                  ellipse_pts, erode, flower, gem, heart_pts, hexc, leaf, mirror_x, move, pearl, pearl_string,
                  pillow, radial_folds, resample, rotate, satin_sheen, scales, scallops, scatter_shapes, sequins,
                  sparkle_points, star_pts, sym, vertical_gradient, bezier)

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
ART = os.path.join(REPO, "unity", "MoveItGames", "Assets", "Games", "Dress", "Art")
OUT = os.path.join(HERE, "out")

PAD = 6
BODY_PPT = 300      # pixels per torso length for clothes (a TV shows the torso ~330 px tall)
HEAD_PPT = 420      # hats, hair, glasses: small on screen, need the detail
HAND_PPT = 360

PIECES = []         # every Piece, in drawing order
ITEMS = {}          # item id -> [piece names]


def piece(item, part, bounds, ppt, rig, layer, **meta):
    """A new cell covering `bounds` (x0, y0, x1, y1 in the piece's template units)."""
    x0, y0, x1, y1 = bounds
    w = int(math.ceil((x1 - x0) * ppt)) + 2 * PAD
    h = int(math.ceil((y1 - y0) * ppt)) + 2 * PAD
    p = Piece(f"{item}.{part}", w, h, (PAD - x0 * ppt, PAD - y0 * ppt), ppt, rig=rig, layer=layer, **meta)
    PIECES.append(p)
    ITEMS.setdefault(item, []).append(p.name)
    return p


# ---------------------------------------------------------------------------------------------------- tops

def neck_yoke(p, grey, alpha=1.0, neck_y=0.0, neck_w=0.11, collar=None):
    """The shoulders and upper chest, from a round neckline out to the shoulder tips (covers the player's own shirt)."""
    half = [(0, neck_y + 0.03), (neck_w, neck_y - 0.05), (0.30, -0.035), (0.45, 0.04), (0.48, 0.15), (0.42, 0.30),
            (0.30, 0.34), (0, 0.36)]
    return p.shape(sym(half, sharp=[0, len(half) - 1]), grey=grey, outline=0.010, shade=0.28, shade_radius=0.05,
                   alpha=alpha)


def top_princess():
    p = piece("top_princess", "torso", (-0.56, -0.12, 0.56, 1.04), BODY_PPT, "torso", "top")
    # a lighter tulle yoke over the shoulders, then the satin sweetheart bodice
    yoke = neck_yoke(p, 0.93)
    scallops(p, catmull([(-0.115, -0.045), (0, 0.035), (0.115, -0.045)], False, 8), 0.018, grey=0.97, outline=0.006)
    dots(p, yoke * (1 - p.mask(sym([(0, 0.2), (0.4, 0.2), (0, 0.4)]))), 0.06, 0.007, grey=0.99, alpha=0.8)
    half = [(0, 0.215), (0.09, 0.115), (0.2, 0.125), (0.36, 0.19), (0.405, 0.30), (0.37, 0.55), (0.31, 0.77),
            (0.22, 0.86), (0, 0.97)]
    body = p.shape(sym(half, sharp=[0, 8]), grey=BASE, outline=0.012, shade=0.35, shade_radius=0.07)
    # seams and boning, satin sheen
    for x in (-0.2, 0.2):
        p.stroke(catmull([(x * 0.7, 0.2), (x * 0.78, 0.5), (x * 0.62, 0.86)], False, 8), 0.008, grey=0.66, clip=body)
    p.stroke(catmull([(0, 0.24), (0, 0.6), (0, 0.95)], False, 4), 0.006, grey=0.7, clip=body, alpha=0.6)
    satin_sheen(p, body, -0.28, 0.25, -0.18, 0.8, 0.05, 0.14)
    satin_sheen(p, body, 0.12, 0.3, 0.22, 0.75, 0.03, 0.08)
    # gold piping on the neckline and the pointed waist, a gem in the dip, pearls along the top
    neck = catmull([(-0.37, 0.19), (-0.2, 0.125), (-0.09, 0.115), (0, 0.215), (0.09, 0.115), (0.2, 0.125), (0.37, 0.19)],
                   False, 8, sharp=(3,))
    p.stroke(neck, 0.022, color=GOLD_DARK)
    p.stroke(neck, 0.012, color=GOLD)
    waist = [(-0.31, 0.77), (-0.22, 0.86), (0, 0.97), (0.22, 0.86), (0.31, 0.77)]
    p.stroke(waist, 0.024, color=GOLD_DARK)
    p.stroke(waist, 0.013, color=GOLD)
    pearl_string(p, catmull([(-0.3, 0.2), (-0.15, 0.17), (0, 0.27), (0.15, 0.17), (0.3, 0.2)], False, 8), 0.011)
    gem(p, 0, 0.28, 0.035, hexc("#FF4F8B"))
    for x in (-0.13, 0.13):
        gem(p, x, 0.62, 0.018, hexc("#7FD8FF"))
    sparkle_points(p, body, 6, 0.05, seed=2)

    sleeve = piece("top_princess", "sleeve", (-0.22, -0.10, 0.22, 0.42), BODY_PPT, "arm", "sleeve", side="both")
    puff_sleeve(sleeve, BASE, cuff=GOLD)


def petal(p, base, tip, width, grey, outline=0.008, vein=True, light=0.08, clip=None, alpha=1.0):
    """A soft petal from `base` to `tip`: narrow at the base, round and full, a gentle point; lighter toward the tip."""
    dx, dy = tip[0] - base[0], tip[1] - base[1]
    L = math.hypot(dx, dy)
    ux, uy = dx / L, dy / L
    nx, ny = -uy, ux
    prof = [(0.0, 0.18), (0.25, 0.75), (0.55, 1.0), (0.82, 0.78), (0.95, 0.35), (1.0, 0.0)]
    left = [(base[0] + ux * L * t + nx * width * w, base[1] + uy * L * t + ny * width * w) for t, w in prof]
    right = [(base[0] + ux * L * t - nx * width * w, base[1] + uy * L * t - ny * width * w) for t, w in reversed(prof[:-1])]
    m = p.mask(catmull(left + right, True, 8))
    if clip is not None:
        m = m * clip
    p.fill(m, grey=grey, outline=outline, alpha=alpha)
    X, Y = p.grid()
    along = np.clip(((X - base[0]) * ux + (Y - base[1]) * uy) / L, 0, 1)
    p.paint(m, grey=np.clip(grey + light * along + 0.28 * pillow(m, p.units(width * 0.5)), 0, 1), alpha=alpha)
    if vein:
        p.stroke([(base[0] + ux * L * 0.08, base[1] + uy * L * 0.08), (base[0] + ux * L * 0.78, base[1] + uy * L * 0.78)],
                 width * 0.08, grey=grey - 0.14, clip=m, alpha=0.5 * alpha)
    return m


def top_fairy():
    """Zanë: a satin bodice in a cup of petals (they rise over the bust like a flower opening), a petal peplum over the
    hips, leaf caps on the shoulders, vine straps, a flower at the heart."""
    p = piece("top_fairy", "torso", (-0.60, -0.10, 0.60, 1.10), BODY_PPT, "torso", "top")
    green, green_dark = hexc("#4CB860"), hexc("#2E8B47")
    # the peplum first: big soft petals fanning out under the waist
    for k in range(6):
        u = (k + 0.5) / 6 * 2 - 1
        petal(p, (u * 0.22, 0.70), (u * 0.44, 1.04 - 0.06 * u * u), 0.12, 0.74 + (0.05 if k % 2 else 0.0), vein=False)
    half = [(0, 0.20), (0.12, 0.17), (0.30, 0.18), (0.39, 0.26), (0.38, 0.45), (0.33, 0.62), (0.31, 0.78), (0, 0.80)]
    body = p.shape(sym(half, sharp=[0, 7]), grey=0.76, outline=0.012, shade=0.35, shade_radius=0.08)
    satin_sheen(p, body, -0.20, 0.30, -0.14, 0.74, 0.05, 0.13)
    satin_sheen(p, body, 0.16, 0.34, 0.20, 0.72, 0.03, 0.08)
    # the petal cup over the bust: the outer petals first, leaning out, the middle one on top
    for u in (-1.0, 1.0, -0.5, 0.5, 0.0):
        base = (u * 0.24, 0.40 - 0.03 * abs(u))
        tip = (u * 0.40, 0.11 + 0.04 * abs(u))
        petal(p, base, tip, 0.12, 0.84 + 0.04 * (1 - abs(u)), vein=True, light=0.1)
    # a vine belt
    belt = catmull([(-0.32, 0.72), (0, 0.75), (0.32, 0.72)], False, 8)
    p.stroke(belt, 0.04, color=green_dark)
    p.stroke(belt, 0.026, color=green)
    for k, (x, y) in enumerate(resample(belt, 0.08)):
        leaf(p, x, y, x + (0.05 if k % 2 else -0.05), y + 0.04, 0.022, green, vein=False)
    # leaf caps on the shoulders, vine straps
    for side in (-1, 1):
        for k, (a, l) in enumerate(((0.55, 0.20), (1.15, 0.19))):
            x0, y0 = 0.34 * side, 0.14
            leaf(p, x0, y0, x0 + math.sin(a) * l * side, y0 - math.cos(a) * l * 0.55, 0.075, green if k else green_dark)
        strap = catmull([(0.14 * side, 0.20), (0.19 * side, 0.06), (0.27 * side, -0.01), (0.35 * side, 0.10)], False, 8)
        p.stroke(strap, 0.018, color=green_dark)
        p.stroke(strap, 0.010, color=green)
        for x, y in resample(strap, 0.07)[1:-1]:
            leaf(p, x, y, x + 0.04 * side, y - 0.045, 0.018, green, vein=False)
    flower(p, 0, 0.30, 0.085, hexc("#FFFFFF"), center=GOLD, petals=5)
    for x in (-0.16, 0.16):
        flower(p, x, 0.74, 0.045, hexc("#FFC2E2"), center=GOLD, petals=5, rot=0.5)
    sparkle_points(p, body, 8, 0.05, seed=21)


def top_mermaid():
    """Sirenë: a shimmering scale bodice, two seashells, pearls over the shoulders and at the waist, a starfish."""
    p = piece("top_mermaid", "torso", (-0.52, -0.10, 0.52, 1.04), BODY_PPT, "torso", "top")
    half = [(0, 0.17), (0.12, 0.10), (0.26, 0.12), (0.38, 0.2), (0.40, 0.32), (0.37, 0.55), (0.31, 0.78), (0.2, 0.88),
            (0, 0.93)]
    body = p.shape(sym(half, sharp=[0, 8]), grey=0.78, outline=0.012)
    scales(p, body, 0.07)
    p.shade_inside(pillow(body, p.units(0.08)), 0.12)
    # seashells: a fan of ribs, lighter, each on its side of the chest
    for side in (-1, 1):
        cx, cy = 0.15 * side, 0.27
        fan = [(cx, cy + 0.13)]
        for k in range(9):
            a = math.pi * (0.1 + 0.8 * k / 8)
            r = 0.17 if k % 2 == 0 else 0.155
            fan.append((cx - math.cos(a) * r, cy + 0.11 - math.sin(a) * r * 1.05))
        m = p.shape(catmull(fan, True, 6, sharp=(0,)), grey=0.92, outline=0.011, shade=0.35, shade_radius=0.04)
        for k in range(1, 8):
            a = math.pi * (0.1 + 0.8 * k / 8)
            p.stroke([(cx, cy + 0.12), (cx - math.cos(a) * 0.15, cy + 0.11 - math.sin(a) * 0.15)], 0.008, grey=0.7,
                     clip=m, alpha=0.8)
        p.shape(ellipse_pts(cx, cy + 0.13, 0.04, 0.025, 24), grey=0.86, outline=0.008)
    # pearl straps from the shells over the shoulders, a pearl belt
    for side in (-1, 1):
        pearl_string(p, catmull([(0.27 * side, 0.18), (0.25 * side, 0.08), (0.2 * side, -0.02), (0.11 * side, -0.04)],
                                False, 8), 0.016)
    pearl_string(p, catmull([(-0.31, 0.8), (-0.15, 0.88), (0, 0.92), (0.15, 0.88), (0.31, 0.8)], False, 8), 0.019)
    starfish(p, -0.2, 0.2, 0.06, hexc("#FF8A5B"))
    sparkle_points(p, body, 10, 0.05, seed=31)


def starfish(p, cx, cy, r, color):
    pts = star_pts(cx, cy, r, 0.5, 5, rot=-math.pi / 2 + 0.3, round_=1)
    p.shape(pts, color=color, outline=0.007, shade=0.4, shade_radius=r * 0.3)
    for k in range(5):
        a = -math.pi / 2 + 0.3 + 2 * math.pi * k / 5
        for t in (0.35, 0.6):
            pearl(p, cx + math.cos(a) * r * t, cy + math.sin(a) * r * t, r * 0.08, color=hexc("#FFE0C8"))


def top_hero():
    """Superheroinë: a fitted suit with a chest panel, a shining star emblem, a gold belt; long sleeves with gloves."""
    p = piece("top_hero", "torso", (-0.54, -0.12, 0.54, 1.04), BODY_PPT, "torso", "top")
    half = [(0, -0.01), (0.11, -0.06), (0.30, -0.035), (0.45, 0.04), (0.47, 0.15), (0.41, 0.30), (0.38, 0.52),
            (0.32, 0.78), (0.35, 0.92), (0.36, 1.0), (0, 1.0)]
    body = p.shape(sym(half, sharp=[0, len(half) - 1]), grey=BASE, outline=0.012, shade=0.4, shade_radius=0.08)
    # the darker side panels and the V of the chest panel
    for side in (-1, 1):
        panel = [(0.34 * side, 0.32), (0.41 * side, 0.30), (0.38 * side, 0.52), (0.32 * side, 0.78), (0.36 * side, 0.95),
                 (0.24 * side, 0.95), (0.27 * side, 0.6)]
        p.paint(p.mask(catmull(panel, True, 6)) * body, grey=0.6)
    v = p.mask(sym([(0, 0.02), (0.11, -0.05), (0.2, -0.03), (0.08, 0.36), (0, 0.42)], sharp=[0, 4]))
    p.paint(v * body, grey=0.66)
    p.stroke([(-0.2, -0.03), (0, 0.42), (0.2, -0.03)], 0.014, color=GOLD)
    satin_sheen(p, body, -0.3, 0.2, -0.24, 0.8, 0.04, 0.14)
    # emblem
    em = p.shape(ellipse_pts(0, 0.33, 0.12, 0.11, 48), color=GOLD, outline=0.012, shade=0.5, shade_radius=0.03)
    p.shape(ellipse_pts(0, 0.33, 0.095, 0.085, 48), grey=0.62, outline=0.0)
    p.shape(star_pts(0, 0.335, 0.075, 0.45, 5), color=WHITE, outline=0.006, shade=0.3, shade_radius=0.02)
    p.meta.setdefault("sparkles", []).append([-0.04, 0.29, 0.07])
    # belt with a buckle
    belt = catmull([(-0.33, 0.79), (0, 0.83), (0.33, 0.79)], False, 8)
    p.stroke(belt, 0.07, color=GOLD_DARK)
    p.stroke(belt, 0.05, color=GOLD)
    p.shape([(-0.06, 0.78), (0.06, 0.78), (0.06, 0.88), (-0.06, 0.88)], color=hexc("#FFE38A"), outline=0.008)
    p.shape(star_pts(0, 0.83, 0.035, 0.45, 5), grey=0.66, outline=0.0)

    s = piece("top_hero", "sleeve", (-0.15, -0.10, 0.15, 1.34), BODY_PPT, "arm", "sleeve", side="both")
    sm = long_sleeve(s, BASE, width=0.105)
    glove = s.mask(catmull([(-0.115, 0.88), (0.115, 0.88), (0.10, 1.32), (-0.10, 1.32)], True, 4))
    s.paint(glove * sm, grey=0.6)
    cuff = [(-0.12, 0.9), (0, 0.93), (0.12, 0.9)]
    s.stroke(cuff, 0.05, color=GOLD_DARK)
    s.stroke(cuff, 0.034, color=GOLD)
    s.stroke([(-0.03, 0.05), (-0.05, 0.6), (-0.04, 0.85)], 0.025, grey=0.92, clip=sm, alpha=0.5)


def long_sleeve(p, grey, width=0.1, length=1.3, flare=0.0):
    """A sleeve down the whole arm (arm frame: shoulder 0, elbow 0.64, wrist 1.21)."""
    w0, w1, w2 = width * 1.15, width, width * 0.85 + flare
    pts = catmull([(0, -0.08), (w0, -0.03), (w1, 0.64), (w2, 1.2), (w2 + 0.01, length), (-(w2 + 0.01), length),
                   (-w2, 1.2), (-w1, 0.64), (-w0, -0.03)], True, 8, sharp=(4, 5))
    m = p.shape(pts, grey=grey, outline=0.012, shade=0.4, shade_radius=0.05)
    p.stroke([(-w1 * 0.9, 0.6), (0, 0.66), (w1 * 0.9, 0.6)], 0.008, grey=grey - 0.15, clip=m, alpha=0.6)
    return m


def top_winter():
    """Dimër: a cosy coat with a white fur collar and fur cuffs, gold toggles down the front."""
    p = piece("top_winter", "torso", (-0.58, -0.20, 0.58, 1.18), BODY_PPT, "torso", "top")
    half = [(0, 0.0), (0.12, -0.06), (0.31, -0.03), (0.47, 0.05), (0.49, 0.16), (0.43, 0.32), (0.40, 0.55),
            (0.36, 0.78), (0.42, 0.98), (0.46, 1.12), (0, 1.13)]
    body = p.shape(sym(half, sharp=[0, len(half) - 1]), grey=BASE, outline=0.013, shade=0.4, shade_radius=0.09)
    # quilting: soft diagonal channels
    X, Y = p.grid()
    q = (np.sin((X + Y) * 34) * np.sin((X - Y) * 34))
    p.shade_inside(body * np.clip(q, -1, 0), 0.06)
    p.stroke([(0, 0.05), (0, 1.12)], 0.012, grey=0.55, clip=body)
    for side in (-1, 1):
        p.stroke(catmull([(0.16 * side, 0.72), (0.27 * side, 0.74), (0.3 * side, 0.86)], False, 6), 0.01, grey=0.58,
                 clip=body)
    # toggles
    for y in (0.3, 0.5, 0.7, 0.9):
        p.stroke([(-0.06, y), (0.06, y)], 0.016, color=hexc("#B07A3C"))
        p.shape(ellipse_pts(0, y, 0.03, 0.022, 24), color=GOLD, outline=0.006, shade=0.4, shade_radius=0.01)
    # fur collar: fluffy all around the neckline and down to the chest
    collar = catmull([(-0.42, 0.10), (-0.3, 0.0), (-0.14, -0.04), (0, 0.02), (0.14, -0.04), (0.3, 0.0), (0.42, 0.10)],
                     False, 10)
    fur(p, collar, 0.07)
    p.meta["sparkles"] = []

    s = piece("top_winter", "sleeve", (-0.17, -0.10, 0.17, 1.34), BODY_PPT, "arm", "sleeve", side="both")
    long_sleeve(s, BASE, width=0.12, flare=0.02)
    fur(s, [(-0.15, 1.18), (0, 1.2), (0.15, 1.18)], 0.06)


def fur(p, pts, r):
    """Fluffy white fur along a line: overlapping soft bumps with a little shading."""
    pts = resample(catmull(pts, False, 4) if len(pts) < 6 else pts, r * 0.6)
    m = np.zeros((p.H, p.W), np.float32)
    rng = np.random.default_rng(len(pts))
    for x, y in pts:
        m = np.maximum(m, p.circle_mask(x + rng.uniform(-0.2, 0.2) * r, y + rng.uniform(-0.25, 0.25) * r,
                                        r * rng.uniform(0.75, 1.05)))
    p.fill(m, color=hexc("#FBF8FF"), outline=0.008, shade=0.35, shade_radius=r * 0.5, ink=hexc("#8E86A8"))
    for x, y in pts[::2]:
        p.stroke([(x, y - r * 0.3), (x + r * 0.2, y + r * 0.3)], 0.006, color=hexc("#D9D2EA"), clip=m, alpha=0.7)
    return m


def top_party():
    """Festë: a sequin top with an off-shoulder ruffle and a little peplum."""
    p = piece("top_party", "torso", (-0.58, -0.06, 0.58, 1.10), BODY_PPT, "torso", "top")
    half = [(0, 0.08), (0.2, 0.08), (0.40, 0.12), (0.41, 0.30), (0.37, 0.55), (0.31, 0.78), (0, 0.80)]
    body = p.shape(sym(half, sharp=[0, len(half) - 1]), grey=0.7, outline=0.012)
    sequins(p, body, 0.045, 0.024, seed=41)
    p.shade_inside(pillow(body, p.units(0.08)), 0.1)
    # peplum: a short flared frill below the waist
    pep = [(0, 0.76), (0.31, 0.76), (0.44, 0.96), (0.3, 1.02), (0.15, 0.98), (0, 1.02)]
    pm = p.mask(sym(pep, sharp=[0, 5]))
    p.fill(pm, grey=BASE, outline=0.011)
    p.paint(pm, grey=np.clip(BASE + radial_folds(p, (0, 0.4), 30, 0.08, 0.78, 1.02), 0, 1))
    # the waist band and the off-shoulder ruffle
    band = catmull([(-0.31, 0.77), (0, 0.8), (0.31, 0.77)], False, 8)
    p.stroke(band, 0.05, grey=0.5)
    p.stroke(band, 0.03, grey=0.66)
    ruffle = catmull([(-0.50, 0.12), (-0.36, 0.05), (-0.18, 0.06), (0, 0.08), (0.18, 0.06), (0.36, 0.05), (0.50, 0.12)],
                     False, 10)
    r2 = [(x, y + 0.08) for x, y in ruffle]
    m = np.zeros((p.H, p.W), np.float32)
    for x, y in resample(r2, 0.06):
        m = np.maximum(m, p.mask(catmull([(x - 0.06, y - 0.07), (x + 0.06, y - 0.07), (x + 0.07, y + 0.07),
                                          (x, y + 0.1), (x - 0.07, y + 0.07)], True, 6)))
    p.fill(m, grey=0.86, outline=0.010, shade=0.4, shade_radius=0.03)
    for x, y in resample(r2, 0.06):
        p.stroke([(x, y - 0.05), (x, y + 0.07)], 0.007, grey=0.68, clip=m, alpha=0.7)
    bow(p, 0.0, 0.14, 0.08, grey=0.86)
    sparkle_points(p, body, 6, 0.05, seed=42)


def puff_sleeve(p, grey, cuff=GOLD, length=0.32):
    """A round puffed sleeve on the upper arm (arm frame: y down the arm from the shoulder)."""
    half = [(0, -0.05), (0.10, -0.04), (0.165, 0.05), (0.17, 0.15), (0.14, length - 0.05), (0.10, length), (0, length + 0.01)]
    m = p.shape(sym(half), grey=grey, outline=0.012, shade=0.45, shade_radius=0.06)
    for k in range(-3, 4):
        x = k * 0.042
        p.stroke(catmull([(x * 0.4, -0.03), (x * 1.25, 0.11), (x * 0.75, length - 0.02)], False, 8), 0.008,
                 grey=grey - 0.16, clip=m, alpha=0.7)
    satin_sheen(p, m, -0.08, -0.01, -0.07, length - 0.06, 0.03, 0.12)
    band = [(-0.105, length - 0.015), (0, length + 0.008), (0.105, length - 0.015)]
    p.stroke(band, 0.036, color=GOLD_DARK)
    p.stroke(band, 0.022, color=cuff)


# ---------------------------------------------------------------------------------------------------- bottoms

def bottom_ballgown():
    p = piece("bottom_ballgown", "skirt", (-1.08, 0.70, 1.08, 3.02), BODY_PPT, "skirt", "bottom")
    hem_y = 2.92
    # the underskirt (lighter) shows in front, the overskirt opens over it in drapes
    under_half = [(0, 0.76), (0.30, 0.78), (0.52, 1.25), (0.78, 2.0), (0.98, 2.62), (1.02, hem_y - 0.05), (0.7, hem_y + 0.02),
                  (0, hem_y + 0.04)]
    under = p.shape(sym(under_half, sharp=[0, 7]), grey=0.9, outline=0.012, shade=0.3, shade_radius=0.12)
    folds = radial_folds(p, (0, 0.2), 26, 0.07, 1.0, hem_y)
    p.shade_inside(under, 0, clip=None)
    p.paint(under, grey=np.clip(0.9 + folds + 0.12 * pillow(under, p.units(0.12)), 0, 1))
    # ruffled hem of the underskirt
    scallops(p, catmull([(-1.0, hem_y - 0.04), (-0.5, hem_y + 0.02), (0, hem_y + 0.04), (0.5, hem_y + 0.02),
                         (1.0, hem_y - 0.04)], False, 12), 0.05, grey=0.95, outline=0.008)
    # overskirt: two big drapes, gathered up at the sides of the front
    for side in (-1, 1):
        drape = [(0.0, 0.76), (0.30, 0.78), (0.52, 1.25), (0.80, 2.0), (1.0, 2.62), (1.0, hem_y - 0.14), (0.78, hem_y - 0.02),
                 (0.55, hem_y - 0.24), (0.38, 2.1), (0.22, 1.5), (0.10, 1.05)]
        pts = catmull([(x * side, y) for x, y in drape], True, 10, sharp=(0,))
        m = p.mask(pts)
        p.fill(m, grey=BASE, outline=0.013)
        f = radial_folds(p, (0.15 * side, 0.3), 22, 0.09, 0.95, hem_y, phase=1.3 * side)
        p.paint(m, grey=np.clip(BASE + f + 0.3 * pillow(m, p.units(0.1)), 0, 1))
        satin_sheen(p, m, 0.42 * side, 1.15, 0.78 * side, 2.55, 0.06, 0.12)
        # a gold rosette where each drape is gathered
        flower(p, 0.42 * side, 2.1, 0.09, GOLD, center=hexc("#FF4F8B"), petals=6, rot=0.3)
    # the waistband
    band = catmull([(-0.30, 0.8), (0, 0.83), (0.30, 0.8)], False, 8)
    p.stroke(band, 0.05, color=GOLD_DARK)
    p.stroke(band, 0.032, color=GOLD)
    # glitter on the underskirt, glints to twinkle
    scatter_shapes(p, under * (1 - p.mask(catmull([(0, 0.76), (0.3, 0.78), (0.52, 1.25), (0.8, 2.0), (1.0, 2.62),
                                                    (0.55, hem_y - 0.24), (0.22, 1.5), (0, 1.05), (-0.22, 1.5),
                                                    (-0.55, hem_y - 0.24), (-1.0, 2.62), (-0.8, 2.0), (-0.52, 1.25),
                                                    (-0.3, 0.78)], True, 6))),
                   40, lambda x, y, r: star_pts(x, y, r.uniform(0.012, 0.022), 0.42, 4, rot=r.uniform(0, 1)),
                   color=WHITE, alpha=0.9, seed=11, min_dist=0.06)
    sparkle_points(p, under, 10, 0.06, seed=12)


def bottom_petals():
    """Zanë: a knee-length skirt of big soft petals in three layers over a see-through tulle underskirt, a leafy belt."""
    p = piece("bottom_petals", "skirt", (-0.88, 0.70, 0.88, 1.92), BODY_PPT, "skirt", "bottom")
    green, green_dark = hexc("#4CB860"), hexc("#2E8B47")
    tulle = [(0, 0.78), (0.3, 0.79), (0.6, 1.3), (0.8, 1.76), (0.4, 1.82), (0, 1.84)]
    tm = p.mask(sym(tulle, sharp=[0, 5]))
    p.fill(tm, grey=0.97, outline=0.0, alpha=0.5)
    scallops(p, catmull([(-0.8, 1.76), (-0.4, 1.82), (0, 1.84), (0.4, 1.82), (0.8, 1.76)], False, 10), 0.03, grey=0.97,
             outline=0.0)
    for y_end, half_w, n, g, width in ((1.80, 0.74, 7, 0.70, 0.17), (1.50, 0.62, 6, 0.79, 0.17),
                                       (1.22, 0.48, 5, 0.88, 0.15)):
        for k in range(n):
            u = (k + 0.5) / n * 2 - 1
            petal(p, (u * 0.25, 0.80), (u * half_w, y_end - 0.06 * u * u), width, g + (0.04 if k % 2 else -0.02))
    belt = catmull([(-0.30, 0.8), (0, 0.83), (0.30, 0.8)], False, 8)
    p.stroke(belt, 0.045, color=green_dark)
    p.stroke(belt, 0.03, color=green)
    for k, (x, y) in enumerate(resample(belt, 0.075)):
        leaf(p, x, y, x + (0.05 if k % 2 else -0.05), y + 0.05, 0.025, green, vein=False)
    flower(p, 0.18, 0.82, 0.065, hexc("#FFC2E2"), center=GOLD, petals=5)
    sparkle_points(p, p.coverage(), 10, 0.05, seed=51)


def bottom_tail():
    """Sirenë: a mermaid tail (follows the legs: waist, knees, ankles) with scales, a frill and a big fin."""
    p = piece("bottom_tail", "tail", (-0.70, 0.70, 0.70, 3.18), BODY_PPT, "tail", "bottom")
    half = [(0, 0.78), (0.30, 0.79), (0.40, 1.05), (0.37, 1.5), (0.29, 1.92), (0.2, 2.4), (0.15, 2.72), (0, 2.78)]
    tail = p.mask(sym(half, sharp=[0, len(half) - 1]))
    # the fin: two big lobes with ribs, lighter and a little see-through at the ends
    for side in (-1, 1):
        lobe = [(0.02 * side, 2.62), (0.2 * side, 2.7), (0.48 * side, 2.85), (0.64 * side, 3.06), (0.42 * side, 3.1),
                (0.2 * side, 2.98), (0.04 * side, 2.92)]
        m = p.mask(catmull(lobe, True, 10))
        p.fill(m, grey=0.86, outline=0.011)
        p.paint(m, grey=np.clip(vertical_gradient(p, 2.65, 3.1, 0.8, 0.95) + 0.2 * pillow(m, p.units(0.05)), 0, 1))
        for k in range(6):
            a = 0.25 + 0.55 * k / 5
            p.stroke(catmull([(0.03 * side, 2.7), (math.sin(a) * 0.35 * side, 2.7 + math.cos(a) * 0.22),
                              (math.sin(a) * 0.62 * side, 2.72 + math.cos(a) * 0.36)], False, 6), 0.007, grey=0.66,
                     clip=m, alpha=0.7)
    p.fill(tail, grey=BASE, outline=0.012)
    scales(p, tail, 0.08)
    p.shade_inside(pillow(tail, p.units(0.1)), 0.14)
    satin_sheen(p, tail, -0.18, 1.0, -0.1, 2.5, 0.05, 0.1)
    # the frill at the waist
    frill = catmull([(-0.31, 0.82), (-0.15, 0.86), (0, 0.87), (0.15, 0.86), (0.31, 0.82)], False, 8)
    m = np.zeros((p.H, p.W), np.float32)
    for x, y in resample(frill, 0.07):
        m = np.maximum(m, p.mask(catmull([(x - 0.05, y - 0.03), (x + 0.05, y - 0.03), (x + 0.04 + x * 0.1, y + 0.13),
                                          (x + x * 0.1, y + 0.16), (x - 0.04 + x * 0.1, y + 0.13)], True, 6, sharp=(3,))))
    p.fill(m, grey=0.9, outline=0.009, shade=0.3, shade_radius=0.03)
    pearl_string(p, frill, 0.017)
    sparkle_points(p, tail, 14, 0.05, seed=61)


def bottom_hero():
    """Superheroinë: a short pleated skirt and a gold belt with a star buckle."""
    p = piece("bottom_hero", "skirt", (-0.62, 0.70, 0.62, 1.46), BODY_PPT, "skirt", "bottom")
    half = [(0, 0.78), (0.31, 0.78), (0.43, 1.05), (0.55, 1.36), (0.3, 1.39), (0, 1.40)]
    m = p.mask(sym(half, sharp=[0, len(half) - 1]))
    p.fill(m, grey=BASE, outline=0.012)
    X, Y = p.grid()
    ang = np.arctan2(X, Y + 0.2)
    pleat = (ang * 18) % 1.0
    p.paint(m, grey=np.clip(BASE - 0.14 * (pleat > 0.5) + 0.1 * pillow(m, p.units(0.08)), 0, 1))
    hem = catmull([(-0.55, 1.36), (-0.3, 1.39), (0, 1.40), (0.3, 1.39), (0.55, 1.36)], False, 8)
    p.stroke(hem, 0.02, color=GOLD)
    belt = catmull([(-0.32, 0.81), (0, 0.84), (0.32, 0.81)], False, 8)
    p.stroke(belt, 0.075, color=GOLD_DARK)
    p.stroke(belt, 0.055, color=GOLD)
    p.shape(ellipse_pts(0, 0.84, 0.07, 0.06, 32), color=hexc("#FFE38A"), outline=0.009, shade=0.4, shade_radius=0.02)
    p.shape(star_pts(0, 0.845, 0.05, 0.45, 5), grey=0.66, outline=0.0)
    p.meta.setdefault("sparkles", []).append([-0.02, 0.82, 0.06])


def bottom_tutu():
    """Festë / Zanë: a puffy tulle tutu, three see-through layers with glitter, a satin waistband and a bow."""
    p = piece("bottom_tutu", "skirt", (-0.92, 0.70, 0.92, 1.42), BODY_PPT, "skirt", "bottom")
    for layer, (half_w, y_end, g, a) in enumerate(((0.88, 1.32, 0.78, 0.95), (0.78, 1.26, 0.86, 0.85), (0.66, 1.16, 0.93, 0.8))):
        edge = [(-half_w + 2 * half_w * k / 16, y_end + (0.03 if k % 2 else -0.02)) for k in range(17)]
        pts = [(-0.3, 0.8), (0.3, 0.8)] + list(reversed(edge))
        pts = [(0.3, 0.8), (half_w * 0.98, y_end - 0.08)] + list(reversed(edge)) + [(-half_w * 0.98, y_end - 0.08), (-0.3, 0.8)]
        m = p.mask(catmull(pts, True, 6))
        p.fill(m, grey=g, outline=0.008, alpha=a)
        X, Y = p.grid()
        pleat = np.sin(np.arctan2(X, Y - 0.5) * 40) * 0.05
        p.paint(m, grey=np.clip(g + pleat + 0.2 * pillow(m, p.units(0.05)), 0, 1), alpha=a * 0.9)
        dots(p, m, 0.07, 0.006, color=WHITE, alpha=0.8, offset=(0.02 * layer, 0.03 * layer))
    band = catmull([(-0.31, 0.8), (0, 0.83), (0.31, 0.8)], False, 8)
    p.stroke(band, 0.06, grey=0.5)
    p.stroke(band, 0.045, grey=0.7)
    satin_sheen(p, p.line_mask(band, 0.045), -0.3, 0.8, 0.3, 0.8, 0.01, 0.12)
    bow(p, -0.2, 0.83, 0.07, grey=0.72)
    sparkle_points(p, p.coverage(), 12, 0.05, seed=71)


def bottom_winter():
    """Dimër: an A-line skirt with white fur at the hem, snowflakes, a ribbon belt."""
    p = piece("bottom_winter", "skirt", (-0.80, 0.70, 0.80, 1.98), BODY_PPT, "skirt", "bottom")
    half = [(0, 0.78), (0.31, 0.78), (0.48, 1.25), (0.68, 1.82), (0.35, 1.86), (0, 1.87)]
    m = p.mask(sym(half, sharp=[0, len(half) - 1]))
    p.fill(m, grey=BASE, outline=0.012)
    p.paint(m, grey=np.clip(BASE + radial_folds(p, (0, 0.2), 16, 0.06, 0.9, 1.85) + 0.3 * pillow(m, p.units(0.1)), 0, 1))
    for k, (x, y) in enumerate([(-0.25, 1.1), (0.1, 1.0), (0.32, 1.3), (-0.4, 1.5), (-0.05, 1.45), (0.45, 1.65),
                                (0.15, 1.65), (-0.2, 1.72), (0.25, 1.15), (-0.12, 1.25)]):
        snowflake(p, x, y, 0.045 + 0.015 * (k % 3), WHITE, clip=m)
    fur(p, catmull([(-0.7, 1.83), (-0.35, 1.87), (0, 1.88), (0.35, 1.87), (0.7, 1.83)], False, 10), 0.06)
    belt = catmull([(-0.31, 0.8), (0, 0.83), (0.31, 0.8)], False, 8)
    p.stroke(belt, 0.06, grey=0.55)
    p.stroke(belt, 0.04, grey=0.72)
    bow(p, 0.0, 0.84, 0.075, grey=0.72)
    sparkle_points(p, m, 10, 0.05, seed=81)


def snowflake(p, cx, cy, r, color, clip=None, width=None):
    w = width or r * 0.16
    for k in range(6):
        a = math.pi / 6 + math.pi * k / 3
        ex, ey = cx + math.cos(a) * r, cy + math.sin(a) * r
        p.stroke([(cx, cy), (ex, ey)], w, color=color, clip=clip)
        for t in (0.5, 0.75):
            bx, by = cx + math.cos(a) * r * t, cy + math.sin(a) * r * t
            for s in (-1, 1):
                p.stroke([(bx, by), (bx + math.cos(a + s * 0.8) * r * 0.28, by + math.sin(a + s * 0.8) * r * 0.28)],
                         w * 0.8, color=color, clip=clip)


# ---------------------------------------------------------------------------------------------------- hair

def lock(p, spine, w0, w1, grey=BASE, wave=0.018, waves=2.5, strands=7, phase=0.0, outline=0.009, taper=0.35):
    """A flowing lock of hair along `spine` (points, top to tip): width w0 at the top to w1, waving edges, a pointed
    tip, strands following it. Returns its mask."""
    c = catmull(spine, False, 12)
    n = len(c)
    lens = np.cumsum([0] + [math.hypot(c[i + 1][0] - c[i][0], c[i + 1][1] - c[i][1]) for i in range(n - 1)])
    L = lens[-1]
    left, right, normals = [], [], []
    for i in range(n):
        a = c[max(0, i - 1)]
        b = c[min(n - 1, i + 1)]
        dx, dy = b[0] - a[0], b[1] - a[1]
        d = math.hypot(dx, dy) or 1
        nx, ny = -dy / d, dx / d
        t = lens[i] / L
        w = (w0 + (w1 - w0) * t) * (1 if t < 1 - taper else max(0.05, (1 - t) / taper) ** 0.7)
        s = wave * math.sin((t * waves + phase) * 2 * math.pi)
        left.append((c[i][0] + nx * (w + s), c[i][1] + ny * (w + s)))
        right.append((c[i][0] - nx * (w - s), c[i][1] - ny * (w - s)))
        normals.append((nx, ny))
    m = p.mask(left + list(reversed(right)))
    p.fill(m, grey=grey, outline=outline)
    roots_to_tips = vertical_gradient(p, c[0][1], c[-1][1], -0.07, 0.05)    # darker at the roots, sunlit tips
    p.paint(m, grey=np.clip(grey + roots_to_tips + 0.3 * pillow(m, p.units(max(w0, w1) * 0.6)), 0, 1))
    for k in range(strands):
        u = (k + 0.5) / strands * 2 - 1
        pts = []
        for i in range(0, n, 2):
            t = lens[i] / L
            w = (w0 + (w1 - w0) * t) * (1 if t < 1 - taper else max(0.05, (1 - t) / taper) ** 0.7)
            s = wave * math.sin((t * waves + phase) * 2 * math.pi)
            nx, ny = normals[i]
            pts.append((c[i][0] + nx * (u * w * 0.85 + s), c[i][1] + ny * (u * w * 0.85 + s)))
        g = grey - 0.17 if k % 2 == 0 else min(1.0, grey + 0.12)
        p.stroke(pts, 0.006 + 0.003 * (k % 3), grey=g, clip=m, alpha=0.6)
    return m


def crown_shine(p, y=-0.70, w=0.19, strength=0.16):
    """The ring of light on shiny hair."""
    ring = p.mask(catmull([(-w, y + 0.04), (-w * 0.45, y - 0.035), (w * 0.45, y - 0.035), (w, y + 0.04),
                           (w * 0.95, y + 0.07), (w * 0.45, y + 0.0), (-w * 0.45, y + 0.0), (-w * 0.95, y + 0.07)], True, 8))
    p.shade_inside(blur(ring, p.units(0.01)), strength)


def hair_cap(p, top=-0.80, side=0.255, low=-0.38, grey=BASE, part=0.05):
    """The hair over the skull, framing the face (a hole is cut for the face later)."""
    cap = [(part, top), (0.16, top + 0.025), (side - 0.01, -0.66), (side, -0.52), (side - 0.03, low), (0.17, low - 0.05),
           (0.15, -0.55), (0.0, -0.63), (-0.15, -0.55), (-0.17, low - 0.05), (-(side - 0.03), low), (-side, -0.52),
           (-(side - 0.01), -0.66), (-0.16, top + 0.025)]
    m = p.shape(catmull(cap, True, 10), grey=grey, outline=0.010, shade=0.35, shade_radius=0.06)
    return m


def hair_long():
    """Princeshë: long golden waves, a side part, locks over the shoulders."""
    back = piece("hair_long", "back", (-0.48, -0.82, 0.48, 0.72), HEAD_PPT, "hair", "back", hang=-0.30)
    for k, x in enumerate((-0.3, -0.18, -0.06, 0.06, 0.18, 0.3)):
        lock(back, [(x * 0.6, -0.72), (x * 1.05, -0.4), (x * 1.25, 0.0), (x * 1.3, 0.35), (x * 1.2, 0.66)], 0.12, 0.1,
             grey=0.7 if k % 2 else 0.76, wave=0.025, waves=2.2, phase=k * 0.3, strands=5)
    front = piece("hair_long", "front", (-0.48, -0.84, 0.48, 0.50), HEAD_PPT, "hair", "hairfront", hang=-0.30)
    hair_cap(front)
    for side in (-1, 1):
        lock(front, [(0.2 * side, -0.58), (0.25 * side, -0.35), (0.26 * side, -0.1), (0.3 * side, 0.15),
                     (0.27 * side, 0.45)], 0.07, 0.075, wave=0.022, waves=2.5, phase=0.2 if side > 0 else 0.7, strands=6)
        lock(front, [(0.16 * side, -0.56), (0.2 * side, -0.35), (0.19 * side, -0.1), (0.22 * side, 0.12),
                     (0.2 * side, 0.3)], 0.04, 0.05, grey=0.86, wave=0.018, waves=2.0, phase=0.5, strands=4)
    # bangs sweeping from the part to the left
    for k, (x0, x1, y1) in enumerate(((0.05, -0.17, -0.55), (0.06, -0.08, -0.59), (0.08, 0.04, -0.6), (0.1, 0.16, -0.56))):
        lock(front, [(x0, -0.8), ((x0 + x1) / 2 - 0.02, -0.7), (x1, y1)], 0.06, 0.045, grey=0.84 if k % 2 else 0.8,
             wave=0.006, waves=1, strands=4, taper=0.5)
    crown_shine(front)
    erase(front, face_hole(front))


def hair_buns():
    """Festë: two space buns on top with bright scrunchies, a straight fringe, a short bob behind."""
    back = piece("hair_buns", "back", (-0.36, -0.80, 0.36, -0.08), HEAD_PPT, "hair", "back", hang=-0.30)
    back.shape(sym([(0, -0.74), (0.22, -0.68), (0.29, -0.45), (0.28, -0.22), (0.2, -0.16), (0, -0.2)]), grey=0.72,
               outline=0.01, shade=0.3, shade_radius=0.06)
    front = piece("hair_buns", "front", (-0.36, -1.02, 0.36, -0.30), HEAD_PPT, "hair", "hairfront", hang=-0.2)
    for side in (-1, 1):
        cx, cy = 0.17 * side, -0.83
        m = front.shape(ellipse_pts(cx, cy, 0.115, 0.105, 64), grey=BASE, outline=0.011, shade=0.45, shade_radius=0.05)
        for k in range(4):
            a0 = k * 1.6 + side
            front.stroke([(cx + math.cos(a0 + t) * 0.09 * (1 - t / 6), cy + math.sin(a0 + t) * 0.08 * (1 - t / 6))
                          for t in np.linspace(0, 2.6, 14)], 0.008, grey=0.64, clip=m, alpha=0.7)
    hair_cap(front, top=-0.79, side=0.25, low=-0.40)
    # scrunchies
    for side in (-1, 1):
        cx, cy = 0.15 * side, -0.735
        sm = np.zeros((front.H, front.W), np.float32)
        for k in range(9):
            a = math.pi * (0.05 + 0.9 * k / 8)
            sm = np.maximum(sm, front.circle_mask(cx - math.cos(a) * 0.1 * side * 0.9, cy - math.sin(a) * 0.03 + 0.01, 0.028))
        front.fill(sm, color=hexc("#FF7EC8") if side < 0 else hexc("#6EC3FF"), outline=0.007, shade=0.4,
                   shade_radius=0.012)
    # a blunt fringe
    fr = [(-0.19, -0.66), (-0.1, -0.74), (0.1, -0.74), (0.19, -0.66), (0.17, -0.555), (0.1, -0.565), (0.04, -0.55),
          (-0.03, -0.565), (-0.1, -0.55), (-0.17, -0.56)]
    fm = front.shape(catmull(fr, True, 8), grey=0.83, outline=0.009, shade=0.35, shade_radius=0.04)
    for x in np.linspace(-0.15, 0.15, 9):
        front.stroke([(x * 0.7, -0.72), (x, -0.57)], 0.007, grey=0.68, clip=fm, alpha=0.6)
    crown_shine(front, y=-0.71, w=0.17)
    erase(front, face_hole(front))


def hair_braid():
    """Dimër / Princeshë: a thick braid over the shoulder, side-swept bangs, a snowflake clip."""
    back = piece("hair_braid", "back", (-0.40, -0.82, 0.40, 0.25), HEAD_PPT, "hair", "back", hang=-0.30)
    back.shape(sym([(0, -0.76), (0.22, -0.7), (0.3, -0.48), (0.31, -0.2), (0.25, 0.1), (0, 0.15)]), grey=0.72,
               outline=0.01, shade=0.3, shade_radius=0.07)
    front = piece("hair_braid", "front", (-0.40, -0.84, 0.50, 0.78), HEAD_PPT, "hair", "hairfront", hang=-0.25)
    hair_cap(front, part=-0.06)
    # the braid: interleaved lobes from behind the right ear down over the shoulder
    spine = catmull([(0.2, -0.45), (0.26, -0.25), (0.30, 0.0), (0.33, 0.25), (0.32, 0.55)], False, 12)
    pts = resample(spine, 0.07)
    for k, (x, y) in enumerate(pts):
        s = 1 if k % 2 else -1
        r = 0.075 * (1 - 0.35 * k / len(pts))
        lobe = rotate(ellipse_pts(x + s * r * 0.35, y, r * 0.75, r * 0.55, 32), 0.5 * s, x + s * r * 0.35, y)
        m = front.shape(lobe, grey=0.8 if k % 2 else 0.74, outline=0.008, shade=0.45, shade_radius=r * 0.4)
        front.stroke([(x - s * r * 0.2, y - r * 0.3), (x + s * r * 0.7, y + r * 0.3)], 0.006, grey=0.62, clip=m, alpha=0.6)
    end = pts[-1]
    front.shape(ellipse_pts(end[0], end[1] + 0.03, 0.035, 0.02, 24), color=hexc("#6EC3FF"), outline=0.007)
    lock(front, [(end[0], end[1] + 0.04), (end[0] + 0.02, end[1] + 0.12), (end[0] - 0.01, end[1] + 0.2)], 0.04, 0.03,
         wave=0.01, waves=1, strands=3, taper=0.6)
    # side-swept bangs to the right
    for k, (x0, x1, y1) in enumerate(((-0.06, 0.17, -0.55), (-0.08, 0.08, -0.6), (-0.1, -0.04, -0.6), (-0.12, -0.16, -0.56))):
        lock(front, [(x0, -0.8), ((x0 + x1) / 2 + 0.02, -0.7), (x1, y1)], 0.06, 0.045, grey=0.84 if k % 2 else 0.8,
             wave=0.006, waves=1, strands=4, taper=0.5)
    snowflake(front, -0.2, -0.64, 0.045, hexc("#E8F6FF"), width=0.012)
    crown_shine(front)
    erase(front, face_hole(front))


def hair_curly():
    """Verë / Festë: a big cloud of curls."""
    back = piece("hair_curly", "back", (-0.52, -0.97, 0.52, 0.14), HEAD_PPT, "hair", "back", hang=-0.25)
    curl_cloud(back, 0, -0.42, 0.40, 0.48, 0.72, seed=3)
    front = piece("hair_curly", "front", (-0.50, -0.98, 0.50, -0.18), HEAD_PPT, "hair", "hairfront", hang=-0.25)
    curl_cloud(front, 0, -0.58, 0.37, 0.36, BASE, seed=5)
    erase(front, front.mask(ellipse_pts(0, -0.38, 0.165, 0.25, 48)))
    # little ringlets along the hairline
    for k in range(9):
        a = math.pi * (1.1 + 0.8 * k / 8)
        x, y = math.cos(a) * 0.165, -0.42 + math.sin(a) * 0.235
        ringlet(front, x, y, 0.042, 0.84 if k % 2 else 0.78)
    erase(front, face_hole(front, -0.012))


def curl_cloud(p, cx, cy, rx, ry, grey, seed=1):
    """A cloud of curly hair: a bumpy outline, soft shading, and curl swirls (C shapes) all over it."""
    rng = np.random.default_rng(seed)
    m = p.mask(ellipse_pts(cx, cy, rx * 0.9, ry * 0.9, 48))
    for k in range(34):
        a = 2 * math.pi * k / 34 + rng.uniform(-0.05, 0.05)
        r = rng.uniform(0.05, 0.075)
        m = np.maximum(m, p.circle_mask(cx + math.cos(a) * (rx - r * 0.6), cy + math.sin(a) * (ry - r * 0.6), r))
    p.fill(m, grey=grey, outline=0.010)
    p.paint(m, grey=np.clip(grey + 0.3 * pillow(m, p.units(0.09)), 0, 1))
    ys, xs = np.nonzero(m > 0.5)
    for k in range(90):
        i = rng.integers(len(xs))
        x = (xs[i] / 4 - p.origin[0]) / p.ppt
        y = (ys[i] / 4 - p.origin[1]) / p.ppt
        r = rng.uniform(0.022, 0.04)
        a0 = rng.uniform(0, 2 * math.pi)
        arc = [(x + math.cos(a0 + t) * r, y + math.sin(a0 + t) * r) for t in np.linspace(0, 4.2, 10)]
        p.stroke(arc, 0.008, grey=grey - 0.2 if k % 2 else min(1.0, grey + 0.1), clip=m, alpha=0.7)
    return m


def ringlet(p, x, y, r, grey):
    m = p.shape(ellipse_pts(x, y, r, r * 1.15, 24), grey=grey, outline=0.008, shade=0.3, shade_radius=r * 0.4)
    p.stroke([(x + math.cos(t) * r * 0.55 * (1 - t / 10), y + math.sin(t) * r * 0.6 * (1 - t / 10))
              for t in np.linspace(0, 5, 12)], 0.006, grey=grey - 0.2, clip=m, alpha=0.7)


def hair_ponytail():
    """Superheroinë: a high ponytail that swings, side bangs, a scrunchie."""
    back = piece("hair_ponytail", "back", (-0.30, -0.86, 0.52, 0.30), HEAD_PPT, "hair", "back", hang=-0.62)
    lock(back, [(0.06, -0.8), (0.24, -0.78), (0.34, -0.55), (0.36, -0.25), (0.32, 0.05), (0.36, 0.25)], 0.08, 0.1,
         grey=0.76, wave=0.03, waves=1.8, strands=7, taper=0.45)
    back.shape(sym([(0, -0.76), (0.2, -0.7), (0.27, -0.5), (0.26, -0.3), (0, -0.3)]), grey=0.7, outline=0.01)
    front = piece("hair_ponytail", "front", (-0.34, -0.90, 0.34, -0.30), HEAD_PPT, "hair", "hairfront", hang=-0.2)
    hair_cap(front, top=-0.80, side=0.24, low=-0.44, part=0.0)
    sm = np.zeros((front.H, front.W), np.float32)
    for k in range(8):
        a = 2 * math.pi * k / 8
        sm = np.maximum(sm, front.circle_mask(0.1 + math.cos(a) * 0.045, -0.79 + math.sin(a) * 0.025, 0.026))
    front.fill(sm, color=hexc("#FF5A4E"), outline=0.007, shade=0.4, shade_radius=0.012)
    for k, (x0, x1, y1) in enumerate(((0.0, -0.18, -0.56), (0.0, -0.1, -0.6), (0.02, 0.1, -0.6), (0.02, 0.18, -0.56))):
        lock(front, [(x0, -0.79), ((x0 + x1) / 2, -0.7), (x1, y1)], 0.055, 0.04, grey=0.84 if k % 2 else 0.8,
             wave=0.006, waves=1, strands=4, taper=0.5)
    crown_shine(front, y=-0.71, w=0.17)
    erase(front, face_hole(front))


def hair_mermaid():
    """Sirenë: very long flowing waves with a starfish clip and pearls."""
    back = piece("hair_mermaid", "back", (-0.55, -0.82, 0.55, 1.02), HEAD_PPT, "hair", "back", hang=-0.30)
    for k, x in enumerate((-0.34, -0.2, -0.07, 0.07, 0.2, 0.34)):
        lock(back, [(x * 0.55, -0.72), (x * 1.05, -0.35), (x * 1.3, 0.05), (x * 1.35, 0.5), (x * 1.25, 0.96)], 0.12, 0.11,
             grey=0.7 if k % 2 else 0.77, wave=0.04, waves=3.2, phase=k * 0.25, strands=5)
    front = piece("hair_mermaid", "front", (-0.52, -0.84, 0.52, 0.86), HEAD_PPT, "hair", "hairfront", hang=-0.30)
    hair_cap(front, part=0.0)
    for side in (-1, 1):
        lock(front, [(0.2 * side, -0.58), (0.26 * side, -0.3), (0.3 * side, 0.0), (0.33 * side, 0.4), (0.3 * side, 0.82)],
             0.08, 0.08, wave=0.035, waves=3.5, phase=0.3 if side > 0 else 0.8, strands=6)
        lock(front, [(0.16 * side, -0.56), (0.2 * side, -0.3), (0.2 * side, 0.0), (0.24 * side, 0.3)], 0.04, 0.045,
             grey=0.87, wave=0.025, waves=2.5, phase=0.6, strands=4)
    for k, (x0, x1, y1) in enumerate(((0.0, -0.17, -0.55), (0.0, -0.08, -0.6), (0.0, 0.08, -0.6), (0.0, 0.17, -0.55))):
        lock(front, [(x0, -0.8), ((x0 + x1) / 2, -0.71), (x1, y1)], 0.06, 0.045, grey=0.84 if k % 2 else 0.8,
             wave=0.008, waves=1, strands=4, taper=0.5)
    starfish(front, 0.2, -0.66, 0.06, hexc("#FF8A5B"))
    pearl_string(front, catmull([(-0.24, -0.64), (-0.12, -0.75), (0.0, -0.78)], False, 6), 0.012)
    crown_shine(front)
    erase(front, face_hole(front))


def face_hole(p, grow=0.0):
    """The face oval (where wigs and hats must leave the player's face visible)."""
    return p.mask(ellipse_pts(0, -0.44, 0.140 + grow, 0.185 + grow, 64))


def erase(p, m):
    k = np.clip(m, 0, 1)
    p.P *= (1 - k)[..., None]
    p.A *= (1 - k)
    p.M *= (1 - k)


def hair_strands(p, region, top, bottom, count, g_dark, g_light, seed=1, curve=0.08):
    """Fine strands (darker and lighter lines) that follow the hair from `top` to `bottom`, clipped to region."""
    rng = np.random.default_rng(seed)
    ys, xs = np.nonzero(region > 0.5)
    if len(xs) == 0:
        return
    x0 = (xs.min() / 4 - p.origin[0]) / p.ppt
    x1 = (xs.max() / 4 - p.origin[0]) / p.ppt
    y0 = (ys.min() / 4 - p.origin[1]) / p.ppt
    y1 = (ys.max() / 4 - p.origin[1]) / p.ppt
    for k in range(count):
        u = (k + rng.uniform(0.2, 0.8)) / count
        sx = x0 + (x1 - x0) * u
        pts = []
        for t in np.linspace(0, 1, 12):
            x = sx + (top[0] - sx) * (1 - t) * 0.3 + curve * math.sin(t * math.pi * 2 + k) * 0.5
            y = y0 + (y1 - y0) * t
            pts.append((x, y))
        g = g_dark if k % 2 == 0 else g_light
        p.stroke(catmull(pts, False, 4), rng.uniform(0.006, 0.012), grey=g, clip=region, alpha=0.55)


# ---------------------------------------------------------------------------------------------------- head

def head_crown():
    p = piece("head_crown", "crown", (-0.22, -1.02, 0.22, -0.62), HEAD_PPT, "head", "head")
    base_y, top_y = -0.70, -0.92
    # a band with five points, each with a ball; the band is in perspective (an arc)
    outline = [(-0.17, -0.66), (-0.19, -0.86), (-0.12, -0.78), (-0.07, -0.93), (0, -0.82), (0.07, -0.93), (0.12, -0.78),
               (0.19, -0.86), (0.17, -0.66), (0.0, -0.63)]
    pts = catmull(outline, True, 6, sharp=tuple(range(9)))
    m = p.shape(pts, grey=0.86, outline=0.010, shade=0.5, shade_radius=0.03)
    band = p.mask(catmull([(-0.175, -0.70), (0, -0.665), (0.175, -0.70), (0.17, -0.655), (0, -0.625), (-0.17, -0.655)],
                          True, 8, sharp=(0, 2, 3, 5)))
    p.fill(band, grey=0.7, outline=0.006, shade=0.4, shade_radius=0.02)
    for x, y in ((-0.19, -0.875), (-0.07, -0.945), (0.07, -0.945), (0.19, -0.875)):
        pearl(p, x, y, 0.016)
    gem(p, 0, -0.83 + 0.06, 0.03, hexc("#FF3B6B"))
    for x in (-0.11, 0.11):
        gem(p, x, -0.70, 0.018, hexc("#3FA9FF"))
    for x in (-0.055, 0.055):
        gem(p, x, -0.668, 0.013, hexc("#3BD07A"))
    satin_sheen(p, m, -0.15, -0.82, 0.15, -0.82, 0.02, 0.15)


def head_tiara():
    """Dimër / Princeshë: a delicate tiara with a big snowflake and little gems."""
    p = piece("head_tiara", "tiara", (-0.22, -0.88, 0.22, -0.60), HEAD_PPT, "head", "head")
    arc = catmull([(-0.18, -0.62), (-0.1, -0.675), (0, -0.69), (0.1, -0.675), (0.18, -0.62)], False, 10)
    p.stroke(arc, 0.022, grey=0.45)
    p.stroke(arc, 0.014, grey=0.9)
    for side in (-1, 1):
        sw = catmull([(0.03 * side, -0.69), (0.08 * side, -0.75), (0.13 * side, -0.72), (0.12 * side, -0.68),
                      (0.09 * side, -0.69)], False, 8)
        p.stroke(sw, 0.02, grey=0.45)
        p.stroke(sw, 0.012, grey=0.92)
        gem(p, 0.15 * side, -0.66, 0.014, hexc("#A8E6FF"))
        pearl(p, 0.09 * side, -0.755, 0.012)
    snowflake(p, 0, -0.77, 0.075, (0.32, 0.36, 0.5), width=0.026)
    snowflake(p, 0, -0.77, 0.075, (1, 1, 1), width=0.015)
    m = p.coverage()
    gem(p, 0, -0.77, 0.024, hexc("#7FD8FF"))
    sparkle_points(p, m, 4, 0.04, seed=91)


def head_flowers():
    """Zanë / Verë: a wreath of flowers and leaves around the head."""
    p = piece("head_flowers", "wreath", (-0.30, -0.86, 0.30, -0.50), HEAD_PPT, "head", "head")
    green = hexc("#4CB860")
    arc = [(math.cos(a) * 0.22, -0.60 + math.sin(a) * 0.12) for a in np.linspace(math.pi * 1.02, math.pi * 1.98, 14)]
    p.stroke(arc, 0.02, color=hexc("#2E8B47"))
    for k, (x, y) in enumerate(arc[::2]):
        a = math.atan2(y + 0.6, x)
        leaf(p, x, y, x + math.cos(a + 1.2) * 0.07, y + math.sin(a + 1.2) * 0.07 - 0.02, 0.028, green, vein=False)
        leaf(p, x, y, x + math.cos(a - 1.4) * 0.06, y + math.sin(a - 1.4) * 0.06, 0.024, hexc("#6FD66A"), vein=False)
    for k, (x, y) in enumerate(arc[1::2]):
        big = k % 2 == 0
        flower(p, x, y, 0.065 if big else 0.05, 0.84 if big else WHITE, center=GOLD, petals=5 if big else 6, rot=k)
    for x, y in arc[::3]:
        pearl(p, x, y - 0.03, 0.01, color=hexc("#FFF4B0"))
    sparkle_points(p, p.coverage(), 4, 0.04, seed=92)


def head_catears():
    """Festë: a headband with fluffy cat ears."""
    p = piece("head_catears", "ears", (-0.26, -0.96, 0.26, -0.50), HEAD_PPT, "head", "head")
    band = [(math.cos(a) * 0.205, -0.555 + math.sin(a) * 0.235) for a in np.linspace(math.pi * 1.08, math.pi * 1.92, 16)]
    p.stroke(band, 0.03, grey=0.4)
    p.stroke(band, 0.02, grey=BASE)
    for side in (-1, 1):
        ear = [(0.07 * side, -0.76), (0.17 * side, -0.93), (0.21 * side, -0.69)]
        p.shape(catmull(ear, True, 8, sharp=(1,)), grey=BASE, outline=0.012, shade=0.45, shade_radius=0.03)
        inner = [(0.105 * side, -0.755), (0.168 * side, -0.875), (0.19 * side, -0.72)]
        p.shape(catmull(inner, True, 8, sharp=(1,)), color=hexc("#FFB3CF"), outline=0.0, shade=0.3, shade_radius=0.015)
        for t in (0.3, 0.5):
            p.stroke([(0.15 * side, -0.73 - t * 0.1), (0.16 * side, -0.76 - t * 0.12)], 0.005, color=WHITE, alpha=0.8)
    bow(p, 0.13, -0.68, 0.05, color=hexc("#FF4F8B"), rot=-0.6)


def head_unicorn():
    """Zanë / Festë: a spiral unicorn horn with little ears and flowers."""
    p = piece("head_unicorn", "horn", (-0.24, -1.12, 0.24, -0.58), HEAD_PPT, "head", "head")
    for side in (-1, 1):
        ear = [(0.08 * side, -0.70), (0.16 * side, -0.86), (0.2 * side, -0.66)]
        p.shape(catmull(ear, True, 8, sharp=(1,)), color=hexc("#FFFFFF"), outline=0.011, shade=0.35, shade_radius=0.03)
        p.shape(catmull([(0.11 * side, -0.70), (0.157 * side, -0.81), (0.18 * side, -0.68)], True, 8, sharp=(1,)),
                color=hexc("#FFC2E2"), outline=0.0)
    horn = [(-0.065, -0.74), (0.0, -1.08), (0.065, -0.74)]
    hm = p.shape(catmull(horn, True, 8, sharp=(0, 1, 2)), grey=0.86, outline=0.012, shade=0.5, shade_radius=0.025)
    for k in range(6):
        y = -0.77 - k * 0.05
        w = 0.06 * (1 - (k + 0.5) / 6.6)
        p.stroke(catmull([(-w, y + 0.012), (0, y - 0.006), (w, y - 0.03)], False, 6), 0.009, grey=0.62, clip=hm)
    satin_sheen(p, hm, -0.02, -0.76, -0.005, -1.0, 0.012, 0.15)
    for x, c in ((-0.11, hexc("#FF7EC8")), (0.0, hexc("#B08CFF")), (0.11, hexc("#6EC3FF"))):
        flower(p, x, -0.71, 0.05, c, center=GOLD, petals=5, rot=x * 9)
    p.meta.setdefault("sparkles", []).append([-0.01, -1.0, 0.05])


def head_sunhat():
    """Verë: a big straw sun hat with a ribbon, a bow and a flower."""
    p = piece("head_sunhat", "hat", (-0.46, -0.98, 0.46, -0.48), HEAD_PPT, "head", "head")
    straw = hexc("#F2D08A")
    brim = p.shape(ellipse_pts(0, -0.62, 0.42, 0.115, 80), color=straw, outline=0.012, shade=0.3, shade_radius=0.05)
    X, Y = p.grid()
    weave = (np.sin(np.hypot(X / 0.42, (Y + 0.62) / 0.115) * 40) > 0.6)
    p.paint(weave * brim, color=hexc("#D9B26A"), alpha=0.6)
    crown = [(-0.19, -0.64), (-0.19, -0.80), (-0.12, -0.9), (0.12, -0.9), (0.19, -0.80), (0.19, -0.64)]
    cm = p.shape(catmull(crown, True, 10, sharp=(0, 5)), color=straw, outline=0.012, shade=0.4, shade_radius=0.05)
    p.paint(((np.sin(Y * 160) > 0.7) * cm), color=hexc("#D9B26A"), alpha=0.5)
    band = [(-0.19, -0.69), (0, -0.66), (0.19, -0.69), (0.19, -0.63), (0, -0.60), (-0.19, -0.63)]
    p.shape(catmull(band, True, 8, sharp=(0, 2, 3, 5)), grey=BASE, outline=0.009, shade=0.35, shade_radius=0.02)
    bow(p, 0.16, -0.65, 0.07, grey=BASE)
    flower(p, -0.1, -0.67, 0.06, hexc("#FFFFFF"), center=GOLD, petals=6)
    # the brim must not hide the eyes: keep the face clear below the brim's front edge
    erase(p, p.mask(ellipse_pts(0, -0.44, 0.14, 0.11, 48)))


# ---------------------------------------------------------------------------------------------------- face

EYE_X, EYE_Y = 0.064, -0.47


def lens(p, pts, color=(1.0, 0.82, 0.9), alpha=0.35):
    m = p.mask(pts)
    p.paint(m, color=color, alpha=alpha)
    X, Y = p.grid()
    xs = [x for x, _ in pts]
    ys = [y for _, y in pts]
    cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
    streak = np.exp(-(((X - cx) + (Y - cy) * 0.8 + 0.01) / 0.012) ** 2) * m
    p.paint(streak, color=WHITE, alpha=0.7)
    return m


def frames(p, shape_fn, w=0.016):
    """Glasses: the same frame shape around both eyes, a bridge, short temples toward the ears."""
    ring = np.zeros((p.H, p.W), np.float32)
    for side in (-1, 1):
        pts = shape_fn(EYE_X * side, EYE_Y, side)
        outer = p.mask(pts)
        ring = np.maximum(ring, np.clip(dilate(outer, p.units(w)) - erode(outer, p.units(w * 0.3)), 0, 1))
    bridge = p.line_mask(catmull([(-0.03, -0.485), (0, -0.495), (0.03, -0.485)], False, 6), w * 0.9)
    temples = sum(p.line_mask([(0.115 * s, -0.48), (0.145 * s, -0.47)], w * 0.9) for s in (-1, 1))
    m = np.clip(ring + bridge + temples, 0, 1)
    p.fill(m, grey=BASE, outline=0.007, shade=0.45, shade_radius=0.012)
    return m


def face_hearts():
    p = piece("face_hearts", "glasses", (-0.17, -0.58, 0.17, -0.38), HEAD_PPT, "face", "face")
    shape = lambda x, y, s: heart_pts(x, y - 0.002, 0.066, rot=0.0)
    for side in (-1, 1):
        lens(p, shape(EYE_X * side, EYE_Y, side), color=(1.0, 0.6, 0.8), alpha=0.32)
    frames(p, shape)
    for side in (-1, 1):
        gem(p, (EYE_X + 0.05) * side, EYE_Y - 0.04, 0.009, hexc("#FFFFFF"))


def face_stars():
    p = piece("face_stars", "glasses", (-0.18, -0.60, 0.18, -0.36), HEAD_PPT, "face", "face")
    shape = lambda x, y, s: star_pts(x, y, 0.08, 0.6, 5, rot=-math.pi / 2 + 0.15 * s, round_=1)
    for side in (-1, 1):
        lens(p, shape(EYE_X * side, EYE_Y, side), color=(0.7, 0.85, 1.0), alpha=0.3)
    frames(p, shape, w=0.014)


def face_sunglasses():
    p = piece("face_sunglasses", "glasses", (-0.18, -0.56, 0.18, -0.40), HEAD_PPT, "face", "face")

    def shape(x, y, s):
        pts = [(x - 0.055 * s, y - 0.02), (x + 0.02 * s, y - 0.035), (x + 0.07 * s, y - 0.05), (x + 0.065 * s, y + 0.01),
               (x + 0.02 * s, y + 0.035), (x - 0.035 * s, y + 0.03)]
        return catmull(pts, True, 8, sharp=(2,))
    for side in (-1, 1):
        m = p.mask(shape(EYE_X * side, EYE_Y, side))
        p.paint(m, color=vertical_gradient_rgb(p, EYE_Y - 0.05, EYE_Y + 0.03, (0.12, 0.08, 0.22), (0.35, 0.22, 0.45)),
                alpha=0.88)
        X, Y = p.grid()
        streak = np.exp(-(((X - EYE_X * side) + (Y - EYE_Y) + 0.012) / 0.01) ** 2) * m
        p.paint(streak, color=WHITE, alpha=0.6)
    frames(p, shape, w=0.014)
    for side in (-1, 1):
        for k in range(3):
            gem(p, (EYE_X + 0.065 - k * 0.018) * side, EYE_Y - 0.05 + k * 0.004, 0.007, hexc("#FFFFFF"), sparkle=k == 0)


def vertical_gradient_rgb(p, y0, y1, c0, c1):
    _, Y = p.grid()
    t = np.clip((Y - y0) / (y1 - y0), 0, 1)[..., None]
    return np.asarray(c0, np.float32) * (1 - t) + np.asarray(c1, np.float32) * t


def face_mask():
    """Princeshë / Festë: a masquerade mask with swirls, gems and a feather."""
    p = piece("face_mask", "mask", (-0.22, -0.82, 0.36, -0.36), HEAD_PPT, "face", "face")
    half = [(0, -0.50), (0.06, -0.535), (0.13, -0.53), (0.2, -0.57), (0.19, -0.48), (0.13, -0.42), (0.06, -0.425),
            (0, -0.46)]
    m = p.shape(sym(half, sharp=[0, 3, 7]), grey=BASE, outline=0.011, shade=0.45, shade_radius=0.03)
    for side in (-1, 1):
        sw = catmull([(0.04 * side, -0.505), (0.1 * side, -0.53), (0.17 * side, -0.53), (0.18 * side, -0.5),
                      (0.15 * side, -0.49)], False, 6)
        p.stroke(sw, 0.009, color=GOLD, clip=m)
        p.stroke(catmull([(0.05 * side, -0.44), (0.12 * side, -0.43), (0.165 * side, -0.47)], False, 6), 0.008,
                 color=GOLD, clip=m)
    # the feather on the right
    fe = catmull([(0.19, -0.54), (0.26, -0.62), (0.32, -0.76), (0.28, -0.78), (0.22, -0.66), (0.17, -0.56)], True, 10)
    fm = p.shape(fe, grey=0.88, outline=0.009, shade=0.4, shade_radius=0.02)
    p.stroke(catmull([(0.18, -0.545), (0.25, -0.65), (0.3, -0.77)], False, 8), 0.006, grey=0.6, clip=fm)
    for t in np.linspace(0.1, 0.9, 8):
        x, y = 0.18 + 0.12 * t, -0.545 - 0.225 * t
        p.stroke([(x, y), (x + 0.03, y + 0.01)], 0.004, grey=0.68, clip=fm, alpha=0.7)
    gem(p, 0, -0.505, 0.02, hexc("#FF3B6B"))
    gem(p, 0.19, -0.54, 0.016, hexc("#7FD8FF"))
    for side in (-1, 1):
        erase(p, p.mask(eye_hole(EYE_X * side, EYE_Y, side)))


def eye_hole(x, y, s, w=0.042, h=0.026):
    return catmull([(x - w * s, y + 0.004), (x - w * 0.2 * s, y - h), (x + w * s, y - 0.006), (x + w * 0.3 * s, y + h * 0.9)],
                   True, 10, sharp=(0, 2))


def face_hero():
    """Superheroinë: a domino mask."""
    p = piece("face_hero", "mask", (-0.22, -0.58, 0.22, -0.38), HEAD_PPT, "face", "face")
    half = [(0, -0.49), (0.06, -0.525), (0.15, -0.525), (0.21, -0.54), (0.17, -0.46), (0.12, -0.425), (0.05, -0.44),
            (0, -0.46)]
    p.shape(sym(half, sharp=[0, 3, 7]), grey=BASE, outline=0.011, shade=0.5, shade_radius=0.03)
    for side in (-1, 1):
        hole = eye_hole(EYE_X * side, EYE_Y, side, w=0.045, h=0.028)
        erase(p, p.mask(hole))
        rim = dilate(p.mask(hole), p.units(0.008)) * (1 - p.mask(hole))
        p.paint(rim * (p.A > 0.5), grey=0.95, alpha=0.8)


def face_cheeks():
    """Zanë / Princeshë: little hearts on the cheeks and golden freckle stars."""
    p = piece("face_cheeks", "cheeks", (-0.19, -0.48, 0.19, -0.32), HEAD_PPT, "face", "face")
    for side in (-1, 1):
        cx, cy = 0.105 * side, -0.395
        m = p.shape(heart_pts(cx, cy, 0.034, rot=0.25 * side), grey=BASE, outline=0.0, alpha=0.85)
        p.shade_inside(blur(p.mask(heart_pts(cx - 0.008, cy - 0.01, 0.014)), p.units(0.004)), 0.16)
        for k, (dx, dy) in enumerate(((-0.05, -0.04), (0.045, -0.035), (0.0, 0.045))):
            p.shape(star_pts(cx + dx * side, cy + dy, 0.012, 0.45, 5), color=GOLD, outline=0.004, ink=GOLD_DARK)
    sparkle_points(p, p.coverage(), 4, 0.03, seed=95)


# ---------------------------------------------------------------------------------------------------- back

def wing_piece(item, bounds, **meta):
    """One wing (the screen's right one; Unity mirrors it for the left), root near the spine."""
    return piece(item, "wing", bounds, 260, "back", "back", side="both", pivot=[0.06, 0.24], **meta)


def back_fairy():
    """Zanë: see-through fairy wings, two lobes, fine veins, glitter."""
    p = wing_piece("back_fairy", (-0.02, -0.75, 1.0, 1.05), flap=9)
    upper = catmull([(0.05, 0.2), (0.25, -0.15), (0.55, -0.55), (0.82, -0.68), (0.9, -0.5), (0.72, -0.12), (0.4, 0.15),
                     (0.08, 0.27)], True, 10)
    lower = catmull([(0.06, 0.28), (0.35, 0.35), (0.62, 0.55), (0.66, 0.85), (0.5, 0.95), (0.3, 0.75), (0.1, 0.4)],
                    True, 10)
    for pts, alpha in ((lower, 0.72), (upper, 0.72)):
        m = p.mask(pts)
        p.fill(m, grey=0.3, outline=0.0)
        X, Y = p.grid()
        d = np.hypot(X - 0.06, Y - 0.25)
        g = np.clip(0.95 - d * 0.3, 0.6, 1)
        p.paint(m, grey=g, alpha=1.0)
        p.paint(dilate(m, p.units(0.012)) - m, grey=0.38, alpha=1.0)
        p.A *= np.where(m > 0.5, alpha, 1.0)
        p.P *= np.where(m > 0.5, alpha, 1.0)[..., None]
        p.M *= np.where(m > 0.5, alpha, 1.0)
    for tip in ((0.8, -0.62), (0.6, -0.4), (0.45, 0.0), (0.58, 0.8), (0.4, 0.62), (0.62, 0.58)):
        p.stroke(catmull([(0.07, 0.25), ((0.07 + tip[0]) / 2, (0.25 + tip[1]) / 2 + 0.04), tip], False, 8), 0.007,
                 grey=0.55, clip=p.coverage() > 0.1, alpha=0.7)
    dots(p, p.coverage() > 0.3, 0.06, 0.006, color=WHITE, alpha=0.8, jitter=0.02, seed=3)
    sparkle_points(p, p.coverage() > 0.3, 10, 0.05, seed=101)


def back_butterfly():
    """Zanë / Verë: butterfly wings with dark borders, white spots and eye spots."""
    p = wing_piece("back_butterfly", (-0.02, -0.80, 1.05, 1.1), flap=7)
    upper = catmull([(0.05, 0.18), (0.3, -0.25), (0.62, -0.68), (0.95, -0.62), (0.98, -0.25), (0.7, 0.1), (0.3, 0.22),
                     (0.08, 0.26)], True, 10)
    lower = catmull([(0.06, 0.27), (0.4, 0.28), (0.7, 0.5), (0.72, 0.78), (0.55, 1.02), (0.45, 0.9), (0.28, 0.75),
                     (0.08, 0.42)], True, 10)
    ink = hexc("#2B1E3A")
    for pts in (lower, upper):
        m = p.mask(pts)
        p.fill(m, color=ink, outline=0.0)
        inner = erode(m, p.units(0.05))
        p.paint(inner, grey=BASE)
        p.paint(inner, grey=np.clip(vertical_gradient(p, -0.6, 0.9, 0.9, 0.7) + 0.2 * pillow(inner, p.units(0.08)), 0, 1))
        rim = np.clip(m - erode(m, p.units(0.035)), 0, 1)
        dots(p, rim, 0.07, 0.013, color=WHITE, alpha=0.95, seed=5)
    for tip in ((0.9, -0.55), (0.75, -0.3), (0.6, 0.05), (0.6, 0.7), (0.5, 0.9)):
        p.stroke(catmull([(0.07, 0.25), ((0.07 + tip[0]) / 2, (0.25 + tip[1]) / 2), tip], False, 6), 0.012, color=ink,
                 clip=p.coverage() > 0.1)
    for cx, cy, r in ((0.62, -0.3, 0.09), (0.5, 0.62, 0.07)):
        p.shape(ellipse_pts(cx, cy, r, r, 40), color=ink, outline=0.0)
        p.shape(ellipse_pts(cx, cy, r * 0.75, r * 0.75, 40), color=hexc("#FFD23F"), outline=0.0)
        p.shape(ellipse_pts(cx, cy, r * 0.4, r * 0.4, 32), color=ink, outline=0.0)
        p.shape(ellipse_pts(cx - r * 0.12, cy - r * 0.12, r * 0.13, r * 0.13, 16), color=WHITE, outline=0.0)
    p.paint(dilate((p.coverage() > 0.5).astype(np.float32), p.units(0.01)), color=ink, under=True)


def feather(p, root, tip, width, grey, outline=0.007):
    """One feather from root to tip: widest two thirds along, a soft round end with a point, a shaft."""
    dx, dy = tip[0] - root[0], tip[1] - root[1]
    L = math.hypot(dx, dy)
    ux, uy = dx / L, dy / L
    nx, ny = -uy, ux
    prof = [(0.0, 0.25), (0.3, 0.75), (0.65, 1.0), (0.88, 0.8), (1.0, 0.0)]
    left = [(root[0] + ux * L * t + nx * width * w, root[1] + uy * L * t + ny * width * w) for t, w in prof]
    right = [(root[0] + ux * L * t - nx * width * w, root[1] + uy * L * t - ny * width * w) for t, w in reversed(prof[:-1])]
    m = p.mask(catmull(left + right, True, 8, sharp=(4,)))
    p.fill(m, grey=grey, outline=outline, shade=0.35, shade_radius=width * 0.6)
    p.stroke([(root[0] + ux * L * 0.05, root[1] + uy * L * 0.05), (root[0] + ux * L * 0.9, root[1] + uy * L * 0.9)],
             width * 0.12, grey=grey - 0.2, clip=m, alpha=0.6)
    return m


def back_angel():
    """Dimër / Princeshë: soft feathered angel wings: long flight feathers fanning out, rows of rounded coverts."""
    p = wing_piece("back_angel", (-0.02, -0.82, 1.32, 0.86), flap=6)
    # the wing's arm arches from the back up and out; every feather hangs down from it like a curtain
    arm = catmull([(0.06, 0.22), (0.2, -0.22), (0.45, -0.6), (0.72, -0.72), (0.92, -0.62)], False, 12)
    roots = resample(arm, 0.075)
    n = len(roots)
    for k, root in enumerate(roots):                       # flight feathers, inner ones first, longest outside
        t = k / max(1, n - 1)
        a = math.radians(98 - 24 * t)                      # pointing down, a little outward toward the tip
        length = 0.45 + 0.7 * t ** 0.8
        tip = (root[0] + math.cos(a) * length, root[1] + math.sin(a) * length)
        feather(p, root, tip, 0.07, 0.84 + 0.05 * (k % 2))
    for row, (drop, length, g) in enumerate(((0.12, 0.34, 0.9), (0.05, 0.22, 0.94))):     # coverts over them
        for k, (x, y) in enumerate(resample(arm, 0.065)[:-1]):
            t = k / max(1, n - 1)
            a = math.radians(100 - 30 * t)
            root = (x, y + drop)
            feather(p, root, (root[0] + math.cos(a) * length, root[1] + math.sin(a) * length), 0.06 - 0.01 * row, g)
    for x, y in resample(arm, 0.035):                      # soft down along the top edge
        p.fill(p.circle_mask(x, y, 0.045), grey=0.97, outline=0.007, shade=0.3, shade_radius=0.02)
    sparkle_points(p, p.coverage() > 0.5, 6, 0.05, seed=111)


def back_dragon():
    """Superheroinë / Festë: little dragon wings: ribs, a scalloped membrane, a claw."""
    p = wing_piece("back_dragon", (-0.02, -0.80, 1.05, 0.95), flap=10)
    tips = [(0.95, -0.55), (0.98, 0.05), (0.72, 0.55), (0.38, 0.75)]
    top = (0.55, -0.72)
    outline = [(0.06, 0.22), (0.3, -0.35), top, tips[0]]
    for a, b in zip(tips, tips[1:]):
        mid = ((a[0] + b[0]) / 2 - 0.12 * (b[1] - a[1]) * 0 - 0.08, (a[1] + b[1]) / 2 - 0.05)
        outline += [mid, b]
    outline += [(0.12, 0.42)]
    m = p.shape(catmull(outline, True, 6, sharp=tuple(range(len(outline)))), grey=BASE, outline=0.012, shade=0.3,
                shade_radius=0.08)
    p.paint(m, grey=np.clip(vertical_gradient(p, -0.7, 0.8, 0.86, 0.68) + 0.2 * pillow(m, p.units(0.06)), 0, 1))
    for tip in [top] + tips:
        p.stroke(catmull([(0.07, 0.22), (0.3, -0.2), tip], False, 8) if tip is top else
                 catmull([(0.35, -0.45), ((0.35 + tip[0]) / 2, (-0.45 + tip[1]) / 2), tip], False, 8),
                 0.022, grey=0.5)
    p.stroke(catmull([(0.07, 0.22), (0.3, -0.2), (0.55, -0.72)], False, 8), 0.03, grey=0.5)
    p.shape(catmull([(0.5, -0.7), (0.58, -0.82), (0.6, -0.7)], True, 6, sharp=(1,)), color=hexc("#FFF4E0"), outline=0.007)


def back_cape():
    """Superheroinë: a flowing cape with a high collar; gold clasps on the shoulders (a front piece)."""
    p = piece("back_cape", "cape", (-0.95, -0.40, 0.95, 2.45), 260, "cape", "back")
    half = [(0, -0.05), (0.2, -0.04), (0.36, -0.02), (0.5, 0.25), (0.62, 0.9), (0.78, 1.7), (0.88, 2.3),
            (0.6, 2.38), (0.3, 2.32), (0, 2.4)]
    m = p.mask(sym(half, sharp=[0, len(half) - 1]))
    p.fill(m, grey=0.62, outline=0.013)
    p.paint(m, grey=np.clip(0.74 + radial_folds(p, (0, -0.5), 12, 0.1, 0.3, 2.3) + 0.2 * pillow(m, p.units(0.1)), 0, 1))
    collar = sym([(0, -0.08), (0.22, -0.12), (0.34, -0.36), (0.42, -0.3), (0.38, 0.0), (0, 0.04)], sharp=[0, 5])
    p.shape(collar, grey=0.6, outline=0.012, shade=0.35, shade_radius=0.05)
    satin_sheen(p, m, -0.4, 0.4, -0.55, 2.0, 0.08, 0.1)
    c = piece("back_cape", "clasp", (-0.48, -0.08, 0.48, 0.14), BODY_PPT, "torso", "top")
    c.stroke(catmull([(-0.34, 0.03), (0, 0.08), (0.34, 0.03)], False, 8), 0.016, color=GOLD_DARK)
    for side in (-1, 1):
        c.shape(ellipse_pts(0.36 * side, 0.03, 0.06, 0.06, 40), color=GOLD, outline=0.01, shade=0.5, shade_radius=0.02)
        c.shape(star_pts(0.36 * side, 0.032, 0.035, 0.45, 5), color=hexc("#FFF4C0"), outline=0.0)


def back_cloak():
    """Princeshë / Dimër: a royal velvet cloak with white fur, a fur collar in front."""
    p = piece("back_cloak", "cloak", (-1.05, -0.30, 1.05, 2.70), 260, "cape", "back")
    half = [(0, -0.12), (0.25, -0.1), (0.42, -0.02), (0.58, 0.3), (0.75, 1.0), (0.9, 1.9), (0.98, 2.55), (0.6, 2.62),
            (0, 2.65)]
    m = p.mask(sym(half, sharp=[0, len(half) - 1]))
    p.fill(m, grey=0.6, outline=0.013)
    p.paint(m, grey=np.clip(0.72 + radial_folds(p, (0, -0.6), 14, 0.09, 0.2, 2.6) + 0.2 * pillow(m, p.units(0.12)), 0, 1))
    dots(p, m, 0.16, 0.012, color=GOLD, alpha=0.9, seed=2)
    hem = catmull([(-0.96, 2.54), (-0.5, 2.62), (0, 2.65), (0.5, 2.62), (0.96, 2.54)], False, 10)
    fur(p, hem, 0.07)
    for side in (-1, 1):
        fur(p, catmull([(0.42 * side, -0.02), (0.6 * side, 0.4), (0.78 * side, 1.2), (0.92 * side, 2.0),
                        (0.97 * side, 2.5)], False, 10), 0.055)
    col = piece("back_cloak", "collar", (-0.56, -0.16, 0.56, 0.30), BODY_PPT, "torso", "top")
    fur(col, catmull([(-0.48, 0.16), (-0.38, 0.02), (-0.2, -0.05), (0, -0.04), (0.2, -0.05), (0.38, 0.02), (0.48, 0.16)],
                     False, 10), 0.075)
    col.shape(ellipse_pts(0, 0.04, 0.045, 0.045, 32), color=GOLD, outline=0.009, shade=0.5, shade_radius=0.015)
    gem(col, 0, 0.04, 0.025, hexc("#FF3B6B"))


# ---------------------------------------------------------------------------------------------------- hand

def hand_bag():
    """Festë / Verë: a little handbag that swings from the hand."""
    p = piece("hand_bag", "bag", (-0.22, -0.06, 0.22, 0.46), HAND_PPT, "hand", "hand", mode="hang")
    p.stroke(catmull([(-0.09, 0.17), (-0.07, 0.02), (0, -0.02), (0.07, 0.02), (0.09, 0.17)], False, 10), 0.024,
             color=GOLD_DARK)
    p.stroke(catmull([(-0.09, 0.17), (-0.07, 0.02), (0, -0.02), (0.07, 0.02), (0.09, 0.17)], False, 10), 0.014, color=GOLD)
    body = [(-0.15, 0.16), (0.15, 0.16), (0.19, 0.38), (0.12, 0.43), (-0.12, 0.43), (-0.19, 0.38)]
    m = p.shape(catmull(body, True, 8, sharp=(0, 1)), grey=BASE, outline=0.012, shade=0.45, shade_radius=0.05)
    X, Y = p.grid()
    quilt = np.sin((X + Y) * 60) * np.sin((X - Y) * 60)
    p.shade_inside(m * np.clip(quilt, -1, 0), 0.06)
    flap = [(-0.15, 0.16), (0.15, 0.16), (0.12, 0.27), (0, 0.31), (-0.12, 0.27)]
    p.shape(catmull(flap, True, 8, sharp=(0, 1, 3)), grey=0.7, outline=0.01, shade=0.4, shade_radius=0.03)
    p.shape(ellipse_pts(0, 0.29, 0.03, 0.025, 24), color=GOLD, outline=0.007, shade=0.5, shade_radius=0.01)
    bow(p, 0.1, 0.19, 0.045, color=hexc("#FFFFFF"))
    p.meta.setdefault("sparkles", []).append([0.0, 0.28, 0.04])


def hand_bouquet():
    """Princeshë / Verë: a bouquet of flowers in a paper cone with a ribbon."""
    p = piece("hand_bouquet", "bouquet", (-0.24, -0.44, 0.24, 0.14), HAND_PPT, "hand", "hand", mode="upright")
    green = hexc("#4CB860")
    for k, (a, l) in enumerate(((-0.9, 0.24), (0.9, 0.24), (-0.4, 0.3), (0.45, 0.3))):
        leaf(p, 0, -0.12, math.sin(a) * l, -0.12 - math.cos(a) * l, 0.04, green if k % 2 else hexc("#2E8B47"), vein=False)
    for k, (x, y, r, c) in enumerate(((-0.11, -0.2, 0.07, hexc("#FF7EC8")), (0.1, -0.21, 0.07, hexc("#FFFFFF")),
                                      (0.0, -0.3, 0.08, hexc("#FF4F8B")), (-0.06, -0.12, 0.06, hexc("#FFD23F")),
                                      (0.07, -0.11, 0.06, hexc("#B08CFF")))):
        flower(p, x, y, r, c, center=GOLD if c != hexc("#FFD23F") else hexc("#FF9F1C"), petals=5 + k % 2, rot=k)
    cone = [(-0.15, -0.12), (0.15, -0.12), (0.02, 0.12), (-0.02, 0.12)]
    m = p.shape(catmull(cone, True, 6, sharp=(0, 1, 2, 3)), grey=BASE, outline=0.012, shade=0.4, shade_radius=0.04)
    p.stroke([(-0.15, -0.12), (0.0, -0.07), (0.15, -0.12)], 0.012, grey=0.95)
    dots(p, m, 0.05, 0.007, color=WHITE, alpha=0.8)
    bow(p, 0, 0.04, 0.05, color=hexc("#FFFFFF"))


def hand_mic():
    """Festë: a pop star's microphone."""
    p = piece("hand_mic", "mic", (-0.12, -0.42, 0.12, 0.12), HAND_PPT, "hand", "hand", mode="along")
    body = [(-0.035, 0.1), (0.035, 0.1), (0.05, -0.17), (-0.05, -0.17)]
    m = p.shape(body, grey=BASE, outline=0.011, shade=0.5, shade_radius=0.02)
    p.stroke([(-0.045, -0.12), (0.045, -0.12)], 0.02, color=GOLD)
    sequins(p, p.mask([(-0.04, -0.06), (0.04, -0.06), (0.04, 0.0), (-0.04, 0.0)]) * m, 0.022, 0.011)
    head = p.shape(ellipse_pts(0, -0.27, 0.09, 0.095, 48), color=hexc("#C9CEDA"), outline=0.011, shade=0.6,
                   shade_radius=0.03)
    X, Y = p.grid()
    mesh = ((np.sin((X + Y) * 140) > 0.6) | (np.sin((X - Y) * 140) > 0.6)) * head
    p.paint(mesh.astype(np.float32), color=hexc("#8E95A8"), alpha=0.7)
    p.shape(ellipse_pts(-0.03, -0.3, 0.025, 0.02, 16), color=WHITE, outline=0.0, alpha=0.8)
    p.meta.setdefault("sparkles", []).append([-0.03, -0.3, 0.05])


def hand_icecream():
    """Verë: an ice cream cone, two scoops, sprinkles and a cherry."""
    p = piece("hand_icecream", "icecream", (-0.14, -0.50, 0.14, 0.14), HAND_PPT, "hand", "hand", mode="upright")
    cone = [(-0.085, -0.12), (0.085, -0.12), (0.0, 0.12)]
    m = p.shape(cone, color=hexc("#E9B36A"), outline=0.011, shade=0.3, shade_radius=0.03)
    X, Y = p.grid()
    hatch = ((np.sin((X + Y) * 90) > 0.75) | (np.sin((X - Y) * 90) > 0.75)) * m
    p.paint(hatch.astype(np.float32), color=hexc("#B5793A"), alpha=0.8)
    p.shape(ellipse_pts(0, -0.15, 0.1, 0.07, 40), color=hexc("#FFF2D9"), outline=0.011, shade=0.4, shade_radius=0.03)
    scallops(p, catmull([(-0.1, -0.12), (0, -0.1), (0.1, -0.12)], False, 6), 0.02, color=hexc("#FFF2D9"), outline=0.008)
    top = p.shape(ellipse_pts(0, -0.27, 0.095, 0.085, 40), grey=BASE, outline=0.011, shade=0.45, shade_radius=0.03)
    scallops(p, catmull([(-0.095, -0.24), (0, -0.21), (0.095, -0.24)], False, 6), 0.02, grey=BASE, outline=0.008)
    rng = np.random.default_rng(4)
    for k in range(14):
        x, y = rng.uniform(-0.07, 0.07), rng.uniform(-0.33, -0.21)
        a = rng.uniform(0, math.pi)
        c = [hexc("#FFFFFF"), hexc("#FFD23F"), hexc("#6EC3FF"), hexc("#6FD66A")][k % 4]
        p.stroke([(x, y), (x + math.cos(a) * 0.018, y + math.sin(a) * 0.018)], 0.008, color=c, clip=top)
    p.shape(ellipse_pts(0.0, -0.37, 0.028, 0.028, 24), color=hexc("#E8304A"), outline=0.008, shade=0.5, shade_radius=0.01)
    p.stroke(catmull([(0.0, -0.39), (0.01, -0.43), (0.03, -0.45)], False, 6), 0.006, color=hexc("#3D8B3D"))


def hand_balloon():
    """Festë: a heart balloon on a string (the game draws the string from the hand to the knot)."""
    p = piece("hand_balloon", "balloon", (-0.24, -0.26, 0.24, 0.27), HAND_PPT, "hand", "hand", mode="float",
              knot=[0, 0.24])
    m = p.shape(heart_pts(0, -0.02, 0.21), grey=BASE, outline=0.012, shade=0.5, shade_radius=0.06)
    p.shade_inside(blur(p.mask(ellipse_pts(-0.08, -0.1, 0.05, 0.03, 24)), p.units(0.01)), 0.18)
    p.shape([(-0.02, 0.215), (0.02, 0.215), (0.0, 0.25)], grey=0.6, outline=0.006)


def hand_wand():
    p = piece("hand_wand", "wand", (-0.16, -0.70, 0.16, 0.10), HAND_PPT, "hand", "hand", mode="along", tip=[0, -0.56])
    # a striped stick, a big puffy star with a face of light, ribbons
    stick = p.shape([(-0.016, 0.07), (0.016, 0.07), (0.013, -0.45), (-0.013, -0.45)], color=WHITE, outline=0.008)
    for k in range(9):
        y = 0.04 - k * 0.055
        p.paint(p.mask([(-0.02, y), (0.02, y - 0.02), (0.02, y - 0.04), (-0.02, y - 0.02)]) * stick, grey=BASE)
    s = p.shape(star_pts(0, -0.55, 0.14, 0.5, 5, round_=1), grey=BASE, outline=0.012, shade=0.5, shade_radius=0.035)
    p.shade_inside(p.mask(star_pts(-0.02, -0.57, 0.07, 0.5, 5, round_=1)), 0.12)
    for side in (-1, 1):
        rib = catmull([(0, -0.43), (0.06 * side, -0.36), (0.03 * side, -0.27), (0.08 * side, -0.2)], False, 8)
        p.stroke(rib, 0.018, color=hexc("#FF7EC8"))
    gem(p, 0, -0.55, 0.025, hexc("#FFFFFF"))
    sparkle_points(p, s, 3, 0.05, seed=4)


# ---------------------------------------------------------------------------------------------------- the doll

def doll():
    """The paper doll shown when nobody is in front of the camera: a cute little mannequin in a lilac leotard, white
    socks and pink shoes, a round face. Her own bob (item "dollhair") shows while no hair is chosen."""
    skin, ink = hexc("#F8D9C6"), hexc("#C98E7A")
    lilac, lilac_dark = hexc("#C9B8FF"), hexc("#8E74D9")
    body = piece("doll", "body", (-0.5, -0.12, 0.5, 1.22), BODY_PPT, "torso", "doll")
    half = [(0, -0.10), (0.10, -0.08), (0.30, -0.02), (0.44, 0.06), (0.46, 0.18), (0.38, 0.32), (0.34, 0.55),
            (0.30, 0.78), (0.37, 0.98), (0.40, 1.12), (0, 1.18)]
    body.shape(sym(half), color=skin, outline=0.012, shade=0.25, shade_radius=0.08, ink=ink)
    leo = [(0, 0.10), (0.16, 0.04), (0.33, 0.04), (0.44, 0.10), (0.45, 0.20), (0.38, 0.32), (0.34, 0.55), (0.30, 0.78),
           (0.37, 0.98), (0.30, 1.10), (0.12, 1.16), (0, 1.17)]
    lm = body.shape(sym(leo, sharp=[0, len(leo) - 1]), color=lilac, outline=0.011, shade=0.3, shade_radius=0.08, ink=lilac_dark)
    body.paint(body.mask(heart_pts(-0.15, 0.32, 0.05)) * lm, color=hexc("#FF9FD0"), alpha=0.9)
    body.stroke(catmull([(-0.16, 0.06), (0, 0.13), (0.16, 0.06)], False, 8), 0.012, color=WHITE, alpha=0.85)
    arm = piece("doll", "arm", (-0.12, -0.08, 0.12, 1.46), BODY_PPT, "arm", "doll", side="both")
    # the arm with a little mitten hand past the wrist (1.21)
    arm.shape(catmull([(0, -0.06), (0.095, 0.0), (0.085, 0.64), (0.066, 1.17), (0.082, 1.26), (0.078, 1.37),
                       (0.04, 1.43), (-0.04, 1.43), (-0.078, 1.37), (-0.082, 1.26), (-0.066, 1.17), (-0.085, 0.64),
                       (-0.095, 0.0)], True, 8), color=skin, outline=0.012, shade=0.25, shade_radius=0.05, ink=ink)
    leg = piece("doll", "leg", (-0.17, -0.12, 0.17, 2.02), BODY_PPT, "leg", "doll", side="both")
    lg = leg.shape(catmull([(0, -0.1), (0.14, -0.05), (0.12, 0.92), (0.09, 1.80), (-0.09, 1.80), (-0.12, 0.92),
                            (-0.14, -0.05)], True, 8), color=skin, outline=0.012, shade=0.25, shade_radius=0.05, ink=ink)
    sock = leg.mask(catmull([(-0.105, 1.48), (0.105, 1.48), (0.098, 1.84), (-0.098, 1.84)], True, 4))
    leg.fill(sock * dilate(lg, leg.units(0.004)), color=WHITE, outline=0.006, ink=hexc("#B9B0CC"), shade=0.2, shade_radius=0.03)
    leg.stroke([(-0.1, 1.52), (0.1, 1.52)], 0.012, color=hexc("#FFB3D6"))
    shoe = catmull([(-0.11, 1.80), (0.11, 1.80), (0.15, 1.92), (0.12, 1.99), (-0.12, 1.99), (-0.15, 1.92)], True, 8)
    leg.shape(shoe, color=hexc("#FF7EC8"), outline=0.011, ink=hexc("#B8417F"), shade=0.45, shade_radius=0.03)
    leg.stroke([(-0.09, 1.84), (0.09, 1.84)], 0.014, color=hexc("#B8417F"))
    leg.shape(ellipse_pts(-0.05, 1.88, 0.025, 0.015, 16), color=WHITE, outline=0, alpha=0.7)
    head = piece("doll", "head", (-0.25, -0.76, 0.25, -0.06), HEAD_PPT, "head", "doll")
    head.shape(catmull([(-0.06, -0.3), (-0.06, -0.08), (0.06, -0.08), (0.06, -0.3)], True, 4), color=skin,
               outline=0.01, ink=ink)
    for side in (-1, 1):
        head.shape(ellipse_pts(0.168 * side, -0.45, 0.035, 0.045, 24), color=skin, outline=0.010, ink=ink)
    head.shape(ellipse_pts(0, -0.47, 0.175, 0.205, 64), color=skin, outline=0.012, shade=0.25, shade_radius=0.05, ink=ink)
    for side in (-1, 1):
        x = 0.066 * side
        head.shape(ellipse_pts(x, -0.465, 0.027, 0.035, 32), color=hexc("#3A2547"), outline=0)
        head.shape(ellipse_pts(x - 0.009, -0.477, 0.010, 0.011, 16), color=WHITE, outline=0)
        head.shape(ellipse_pts(x + 0.008, -0.452, 0.005, 0.005, 12), color=WHITE, outline=0)
        head.stroke(catmull([(x - 0.03 * side, -0.52), (x, -0.532), (x + 0.03 * side, -0.525)], False, 6), 0.008,
                    color=hexc("#7A4A3A"))
        head.shape(ellipse_pts(x * 1.45, -0.40, 0.036, 0.022, 32), color=hexc("#FF8FB0"), outline=0, alpha=0.55)
    head.shape(ellipse_pts(0, -0.418, 0.009, 0.006, 12), color=hexc("#E8A890"), outline=0)
    head.stroke(catmull([(-0.04, -0.378), (0, -0.355), (0.04, -0.378)], False, 8), 0.012, color=hexc("#C2526E"))


def dollhair():
    """The doll's own bob (dark brown, not tinted): only while no hair is chosen."""
    brown, brown_dark = hexc("#6B4330"), hexc("#3E2418")
    back = piece("dollhair", "back", (-0.30, -0.80, 0.30, -0.24), HEAD_PPT, "hair", "back")
    back.shape(sym([(0, -0.76), (0.2, -0.70), (0.27, -0.5), (0.26, -0.32), (0.18, -0.27), (0, -0.30)]), color=brown,
               outline=0.010, ink=brown_dark, shade=0.3, shade_radius=0.06)
    front = piece("dollhair", "front", (-0.30, -0.82, 0.30, -0.26), HEAD_PPT, "hair", "hairfront")
    cap = front.shape(catmull([(0.04, -0.79), (0.17, -0.76), (0.245, -0.64), (0.25, -0.48), (0.22, -0.33), (0.17, -0.34),
                               (0.165, -0.5), (0.1, -0.585), (0.0, -0.6), (-0.1, -0.585), (-0.165, -0.5), (-0.17, -0.34),
                               (-0.22, -0.33), (-0.25, -0.48), (-0.245, -0.64), (-0.17, -0.76)], True, 10),
                      color=brown, outline=0.010, ink=brown_dark, shade=0.35, shade_radius=0.05)
    for x in np.linspace(-0.16, 0.16, 8):
        front.stroke([(x * 0.6, -0.76), (x, -0.6)], 0.007, color=brown_dark, clip=cap, alpha=0.5)
    ring = front.mask(catmull([(-0.18, -0.67), (-0.08, -0.73), (0.08, -0.73), (0.18, -0.67), (0.17, -0.645), (0.08, -0.7),
                               (-0.08, -0.7), (-0.17, -0.645)], True, 8))
    front.paint(blur(ring, front.units(0.01)) * cap, color=hexc("#A9775C"), alpha=0.8)
    front.shape(star_pts(0.15, -0.66, 0.04, 0.45, 5, round_=1), color=hexc("#FF7EC8"), outline=0.007, ink=hexc("#B8417F"))
    erase(front, face_hole(front))


# ---------------------------------------------------------------------------------------------------- build

ALL = [doll, dollhair,
       top_princess, top_fairy, top_mermaid, top_hero, top_winter, top_party,
       bottom_ballgown, bottom_petals, bottom_tail, bottom_hero, bottom_tutu, bottom_winter,
       hair_long, hair_buns, hair_braid, hair_curly, hair_ponytail, hair_mermaid,
       head_crown, head_tiara, head_flowers, head_catears, head_unicorn, head_sunhat,
       face_hearts, face_stars, face_sunglasses, face_mask, face_hero, face_cheeks,
       back_fairy, back_butterfly, back_angel, back_cape, back_cloak, back_dragon,
       hand_wand, hand_bag, hand_bouquet, hand_mic, hand_icecream, hand_balloon]


GAP = 16   # empty pixels between cells: mipmaps and filtering must not pull a neighbour's edge into a piece


def profile(pc, rgba, n=24):
    """The art's half width (template units) at n heights across the cell, smoothed: the cross-section the game wraps
    the piece around when the body turns (OutfitRig.Turn)."""
    a = rgba[..., 3].astype(np.float32) / 255
    h, w = a.shape
    xs = (np.arange(w) + 0.5 - pc.origin[0]) / pc.ppt
    y0 = (0.5 - pc.origin[1]) / pc.ppt
    y1 = (h - 0.5 - pc.origin[1]) / pc.ppt
    band = max(1, h // (2 * n))
    vals = []
    for k in range(n):
        yc = int(round(k / (n - 1) * (h - 1)))
        cols = a[max(0, yc - band):min(h, yc + band + 1)].max(0) > 0.15
        vals.append(float(np.abs(xs[cols]).max()) if cols.any() else 0.0)
    v = np.array(vals)
    v = np.maximum(v, np.maximum(np.roll(v, 1), np.roll(v, -1)))      # a little wider, never a dent
    v = np.convolve(np.pad(v, 2, mode="edge"), np.ones(5) / 5, mode="valid")
    v = np.maximum(v, 0.04)
    return [round(float(x), 4) for x in v], [round(y0, 4), round(y1, 4)]


def pack(pieces, width):
    """Shelf packing, tallest first, GAP apart. Returns {name: (x, y)} and the atlas height."""
    order = sorted(pieces, key=lambda p: (-p.h, -p.w))
    pos = {}
    x = y = shelf = 0
    for p in order:
        if x + p.w > width:
            x, y, shelf = 0, y + shelf + GAP, 0
        pos[p.name] = (x, y)
        x += p.w + GAP
        shelf = max(shelf, p.h)
    return pos, y + shelf


def build(preview, only=None):
    PIECES.clear()
    ITEMS.clear()
    for f in ALL:
        if only and f not in (doll, dollhair) and not any(f.__name__.startswith(o) for o in only):
            continue
        f()
    images = {p.name: p.image() for p in PIECES}
    for pc in PIECES:
        pc.meta["profile"], pc.meta["profileY"] = profile(pc, images[pc.name][0])
    if only:   # a quick look at some items: previews only, the atlas stays as it is
        os.makedirs(OUT, exist_ok=True)
        from preview import preview_all
        info = {"pieces": {p.name: {"origin": list(p.origin), "ppt": p.ppt, **p.meta} for p in PIECES}, "items": ITEMS}
        preview_all(images, info, OUT)
        return
    width = 4096
    pos, height = pack(PIECES, width)
    used = height
    height = 1 << (height - 1).bit_length()
    print(f"atlas {width} x {height}, {used} rows used ({len(PIECES)} pieces, {len(ITEMS)} items)")
    atlas = np.zeros((height, width, 4), np.uint8)
    mask = np.zeros((height, width), np.uint8)
    manifest = {"atlas": [width, height], "pieces": {}, "items": ITEMS}
    for p in PIECES:
        x, y = pos[p.name]
        rgba, m = images[p.name]
        atlas[y:y + p.h, x:x + p.w] = rgba
        mask[y:y + p.h, x:x + p.w] = m
        meta = {k: v for k, v in p.meta.items()}
        manifest["pieces"][p.name] = {
            "rect": [x, y, p.w, p.h], "origin": [round(p.origin[0], 2), round(p.origin[1], 2)], "ppt": p.ppt, **meta}
    # one opaque white texel block in the bottom-right corner, for lines (a balloon's string)
    assert atlas[-8:, -8:, 3].max() == 0, "a piece reaches the atlas corner kept for the white texels"
    atlas[-8:, -8:] = 255
    mask[-8:, -8:] = 0
    os.makedirs(ART, exist_ok=True)
    Image.fromarray(atlas, "RGBA").save(os.path.join(ART, "outfit_atlas.png"), optimize=True)
    Image.fromarray(mask, "L").resize((width // 2, height // 2), Image.LANCZOS).save(os.path.join(ART, "outfit_mask.png"))
    with open(os.path.join(ART, "outfit_atlas.json"), "w", encoding="utf-8") as f:
        json.dump(unity_manifest(manifest), f, indent=1)
    if preview:
        os.makedirs(OUT, exist_ok=True)
        from preview import preview_all
        preview_all(images, manifest, OUT)


def unity_manifest(manifest):
    """Lists instead of dictionaries, every field present (Unity's JsonUtility reads no dictionaries)."""
    pieces = []
    for name, info in manifest["pieces"].items():
        pieces.append({
            "name": name, "item": name.split(".")[0], "rig": info["rig"], "layer": info["layer"], "rect": info["rect"],
            "origin": info["origin"], "ppt": info["ppt"], "side": info.get("side", ""),
            "pivot": info.get("pivot", [0, 0]), "flap": info.get("flap", 0), "mode": info.get("mode", ""),
            "tip": info.get("tip", [0, 0]), "knot": info.get("knot", [0, 0]), "hang": info.get("hang", 99),
            "sparkles": [v for s in info.get("sparkles", []) for v in s],
            "profile": info.get("profile", []), "profileY": info.get("profileY", [0, 1])})
    return {"atlas": manifest["atlas"], "white": [manifest["atlas"][0] - 4, manifest["atlas"][1] - 4],
            "pieces": pieces, "items": [{"id": k, "pieces": v} for k, v in manifest["items"].items()]}


def load_manifest(path):
    """The Unity manifest back as {"atlas", "pieces": {name: info}, "items": {id: [names]}} (for ui_art.py)."""
    with open(path, encoding="utf-8") as f:
        m = json.load(f)
    return {"atlas": m["atlas"], "pieces": {p["name"]: p for p in m["pieces"]},
            "items": {i["id"]: i["pieces"] for i in m["items"]}}


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--preview", action="store_true")
    ap.add_argument("--only", nargs="*", help="draw only these items (name prefixes) for a quick preview")
    args = ap.parse_args()
    build(args.preview, args.only)


if __name__ == "__main__":
    main()
