"""Zana!'s HUD pictures as code (2026-11-15), in the house style of Lunapark!, Vishu! and Yjet! (tools/dress/draw.py:
smooth shapes drawn 4x supersampled, soft "pillow" shading, plum outlines) and the brand's palette.

    motion-service-py/.venv/Scripts/python tools/fairy/art.py [--preview]

Writes unity/MoveItGames/Assets/Games/Fairy/Art/fairy_ui.png + fairy_ui.json (named rectangles in pixels, y down):
    rune_swipe, rune_vee, rune_peak, rune_circle, rune_bolt, rune_heart   the six shapes (white strokes, plum outline)
    bubble        a soap bubble (white: tinted with a shape's colour; the creatures' bubbles and the spellbook's)
    heart, heart_empty, star, star_empty, crown, crystal, alert, ring, dot, glow, white, panel, capsule
    spell_lightning, spell_rainbow, spell_shield, spell_seeds, spell_burst, spell_freeze, spell_heal   the spells
    wand, wand_blue   the magic wand drawn where a player points (its star's middle at 0.27, 0.27 of the picture: the point
                      it points at), leaning like a mouse pointer; player 2's with a blue star (2026-11-16)
    pose_point, pose_trace, pose_arms_up   the how-to figures (white: the HUD tints them): a raised hand points the
                                           wand, a hand follows a dotted shape, both arms up
    (the HUD's frames, 2026-11-17: the look's overhaul)
    tree_badge    the fairy tree in a round gold-rimmed medallion: the tree's health starts at it
    bar_frame     the health bar's frame: a deep plum capsule with a gold rim, leaves curling at both ends (520 x 96)
    bar_fill      the bar's fill: a capsule in greys, a shine along its top (tinted; cut to the health left)
    ribbon        the wave's banner: a pink ribbon with folded tails and gold edges (620 x 150)
    medallion     a round gold-rimmed plum frame (the crystal and the spells sit on it)
The panel and the capsule are drawn stretched in nine pieces (FairyGameHud.Picture): their corners are PanelCorner and
the capsule's half height.
--preview also writes tools/fairy/out/sheet.png (the sheet on the night colour).
"""
import argparse
import importlib.util
import json
import math
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(REPO, "tools", "dress"))
from draw import INK, Piece, ellipse_pts, heart_pts, hexc, star_pts  # noqa: E402

_spec = importlib.util.spec_from_file_location("space_art", os.path.join(REPO, "tools", "space", "art.py"))
space_art = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(space_art)
cell, pack, capsule_pts = space_art.cell, space_art.pack, space_art.capsule_pts

ART = os.path.join(REPO, "unity", "MoveItGames", "Assets", "Games", "Fairy", "Art")
OUT = os.path.join(HERE, "out")
WHITE = (1.0, 1.0, 1.0)
PANEL_CORNER = 40   # px of the 128 px panel: its rounded corners


# ------------------------------------------------------------------------------------------------- the runes

def rune_paths(name):
    """The six shapes as strokes in a cell's units (-1..1, y down), as a player draws them."""
    if name == "swipe":
        return [[(-0.78, 0.0), (0.62, 0.0)], [(0.3, -0.3), (0.66, 0.0), (0.3, 0.3)]]
    if name == "vee":
        return [[(-0.66, -0.58), (0.0, 0.62), (0.66, -0.58)]]
    if name == "peak":
        return [[(-0.66, 0.58), (0.0, -0.62), (0.66, 0.58)]]
    if name == "circle":
        return [ellipse_pts(0, 0, 0.64, 0.64, 64) + [(0.64, 0.0)]]
    if name == "bolt":
        return [[(-0.5, -0.7), (0.24, -0.16), (-0.24, 0.16), (0.5, 0.7)]]
    pts = heart_pts(0.0, -0.02, 0.74, n=96)
    return [pts + [pts[0]]]


def rune_icon(name):
    p = cell(f"rune_{name}", 128)
    paths = rune_paths(name)
    width, o = 0.24, 0.085
    for pts in paths:
        p.paint(p.line_mask(pts, width + 2 * o), color=INK)
    for pts in paths:
        p.paint(p.line_mask(pts, width), color=WHITE)
    # a sparkle where the stroke starts
    x, y = paths[0][0]
    p.shape(star_pts(x, y, 0.2, inner=0.38, points=4, rot=0.0), color=hexc("#FFF6C8"), outline=0.04)
    return p


# ------------------------------------------------------------------------------------------------- icons

def bubble():
    """A soap bubble: a faint body, a bright rim, a highlight in the top left, a little sparkle (white: tinted)."""
    p = cell("bubble", 160)
    j, i = np.mgrid[0:p.H, 0:p.W].astype(np.float32)
    x, y = (i + 0.5) / p.W * 2 - 1, (j + 0.5) / p.H * 2 - 1
    r = np.sqrt(x * x + y * y) / 0.94
    inside = np.clip((1 - r) * p.W * 0.25, 0, 1)
    body = inside * (0.32 + 0.68 * np.clip((r - 0.62) / 0.38, 0, 1) ** 2.2)
    p.paint(body, color=WHITE, alpha=1.0)
    p.paint(p.line_mask(ellipse_pts(0, 0, 0.92, 0.92, 96) + [(0.92, 0.0)], 0.05), color=WHITE)
    p.shape(ellipse_pts(-0.4, -0.44, 0.22, 0.13, 32, a0=0.0), color=WHITE, outline=0.0)
    p.paint(p.line_mask([(-0.66, 0.05), (-0.6, -0.2), (-0.46, -0.42)], 0.07), color=WHITE)
    p.shape(star_pts(0.5, 0.48, 0.13, inner=0.35, points=4, rot=0.0), color=WHITE, outline=0.0)
    return p


def hearts():
    p = cell("heart", 96)
    p.shape(heart_pts(0.0, 0.02, 0.86), color=hexc("#FF5A8C"), outline=0.1, shade=0.35, shade_radius=0.35)
    p.shape(ellipse_pts(-0.36, -0.3, 0.16, 0.1, 24), color=hexc("#FFD1E4"), outline=0.0)
    q = cell("heart_empty", 96)
    q.shape(heart_pts(0.0, 0.02, 0.86), color=hexc("#3A2E5C"), outline=0.1)
    q.paint(q.line_mask(heart_pts(0.0, 0.02, 0.78) + [heart_pts(0.0, 0.02, 0.78)[0]], 0.06), color=hexc("#8E7BB8"))
    return [p, q]


def stars():
    p = cell("star", 96)
    p.shape(star_pts(0.0, 0.06, 0.92, inner=0.48, round_=1.0), color=hexc("#FFC93C"), outline=0.09, shade=0.3,
            shade_radius=0.3)
    q = cell("star_empty", 96)
    q.shape(star_pts(0.0, 0.06, 0.92, inner=0.48, round_=1.0), color=hexc("#3A2E5C"), outline=0.09)
    return [p, q]


def crown():
    """The chief's crown (white: tinted)."""
    p = cell("crown", 96)
    pts = [(-0.78, 0.55), (-0.82, -0.32), (-0.42, 0.02), (0.0, -0.62), (0.42, 0.02), (0.82, -0.32), (0.78, 0.55)]
    p.shape(pts, color=WHITE, outline=0.09, shade=0.25, shade_radius=0.3)
    p.shape(ellipse_pts(0, 0.38, 0.62, 0.12, 32), color=hexc("#E6E1F2"), outline=0.04)
    for x, y in ((-0.82, -0.36), (0.0, -0.66), (0.82, -0.36)):
        p.shape(ellipse_pts(x, y, 0.12, 0.12, 24), color=WHITE, outline=0.05)
    return p


def mini_fairy(name, size, dress, hair, wings, skin="#FFD9C2", tinted=False):
    """A little fairy: wings behind, a round head with hair, a petal dress (the HUD's marker on the way, a pixie)."""
    p = cell(name, size)
    if tinted:
        dress = hair = wings = skin = "#FFFFFF"
    for s in (-1, 1):
        p.shape([(s * x, y) for x, y in ellipse_pts(0.42, -0.12, 0.36, 0.24, 32, a0=0.0)], color=hexc(wings), outline=0.06,
                alpha=0.95)
        p.shape([(s * x, y) for x, y in ellipse_pts(0.36, 0.22, 0.24, 0.16, 32)], color=hexc(wings), outline=0.06, alpha=0.95)
    p.shape([(-0.12, 0.06), (0.12, 0.06), (0.36, 0.62), (0.18, 0.54), (0.0, 0.66), (-0.18, 0.54), (-0.36, 0.62)],
            color=hexc(dress), outline=0.06, shade=0.25, shade_radius=0.25)
    p.shape(ellipse_pts(0.0, -0.36, 0.34, 0.32, 48), color=hexc(hair), outline=0.06, shade=0.2, shade_radius=0.25)
    p.shape(ellipse_pts(0.0, -0.28, 0.26, 0.22, 40), color=hexc(skin), outline=0.0)
    if not tinted:
        for x in (-0.1, 0.1):
            p.shape(ellipse_pts(x, -0.28, 0.04, 0.055, 16), color=hexc("#1B1726"), outline=0.0)
        p.shape(star_pts(0.0, -0.66, 0.12, inner=0.42), color=hexc("#FFE15A"), outline=0.04)
    return p


def crystal():
    """The magic crystal: a gem with facets in greys (tinted, filled from below by the HUD)."""
    p = cell("crystal", 160)
    outline = [(0.0, -0.92), (0.52, -0.42), (0.52, 0.36), (0.0, 0.92), (-0.52, 0.36), (-0.52, -0.42)]
    p.shape(outline, color=WHITE, outline=0.07)
    facets = [([(0.0, -0.92), (0.0, 0.92), (-0.52, 0.36), (-0.52, -0.42)], 0.98),
              ([(0.0, -0.92), (0.52, -0.42), (0.52, 0.36), (0.0, 0.92)], 0.8),
              ([(0.0, -0.92), (0.26, -0.3), (0.0, 0.92), (-0.26, -0.3)], 0.9)]
    for pts, g in facets:
        p.shape(pts, color=(g, g, g), outline=0.0)
    p.paint(p.line_mask([(0.0, -0.92), (0.0, 0.92)], 0.025), color=(0.72, 0.72, 0.78))
    p.shape(star_pts(-0.24, -0.4, 0.16, inner=0.32, points=4, rot=0.0), color=WHITE, outline=0.0)
    return p


def alert():
    p = cell("alert", 96)
    p.shape(ellipse_pts(0, 0, 0.88, 0.88, 64), color=WHITE, outline=0.09, shade=0.2, shade_radius=0.3)
    p.paint(p.line_mask([(0.0, -0.52), (0.0, 0.14)], 0.24), color=INK)
    p.shape(ellipse_pts(0.0, 0.48, 0.14, 0.14, 24), color=INK, outline=0.0)
    return p


def ring():
    p = cell("ring", 96)
    p.paint(p.line_mask(ellipse_pts(0, 0, 0.76, 0.76, 72) + [(0.76, 0.0)], 0.22), color=WHITE)
    return p


def shapes():
    """White shapes the HUD tints: a dot, a block, a glow, a panel (rounded, nine-sliced) and a capsule."""
    out = []
    p = cell("dot", 48)
    p.paint(p.mask(ellipse_pts(0, 0, 0.94, 0.94, 64)), color=WHITE)
    out.append(p)
    p = cell("white", 24)
    p.paint(np.ones((p.H, p.W), np.float32), color=WHITE)
    out.append(p)
    p = cell("glow", 128)
    j, i = np.mgrid[0:p.H, 0:p.W].astype(np.float32)
    r = np.sqrt(((i + 0.5) / p.W * 2 - 1) ** 2 + ((j + 0.5) / p.H * 2 - 1) ** 2)
    p.paint(np.clip(1 - r, 0, 1) ** 2, color=WHITE)
    out.append(p)
    p = cell("panel", 128)
    k = PANEL_CORNER / 64.0
    pts = []
    for cx, cy, a0 in ((1 - k, -1 + k, -90), (1 - k, 1 - k, 0), (-1 + k, 1 - k, 90), (-1 + k, -1 + k, 180)):
        for s in range(13):
            a = math.radians(a0 + 90 * s / 12)
            pts.append((cx + math.cos(a) * k * 0.97, cy + math.sin(a) * k * 0.97))
    p.paint(p.mask(pts), color=WHITE)
    out.append(p)
    p = Piece("capsule", 256, 64, (128, 32), 32)
    p.paint(p.mask(capsule_pts(3.94, 0.97)), color=WHITE)
    out.append(p)
    return out


# ------------------------------------------------------------------------------------------------- the HUD's frames

GOLD, GOLD_DARK, PLUM, PLUM_DEEP = "#FFD46A", "#E7A33C", "#47336E", "#2C2048"


def ring_line(p, r, width, colour, cx=0.0, cy=0.0):
    pts = ellipse_pts(cx, cy, r, r, 96)
    p.paint(p.line_mask(pts + [pts[0]], width), color=hexc(colour))


def leaf(p, x, y, length, angle, colour="#6CC85C", vein="#3E9A56"):
    """A little leaf (template units) pointing along `angle` (degrees, y down)."""
    a = math.radians(angle)
    d, n = (math.cos(a), math.sin(a)), (-math.sin(a), math.cos(a))
    pts = []
    for k in range(25):
        t = k / 24
        w = math.sin(math.pi * t) * length * 0.32
        pts.append((x + d[0] * length * t + n[0] * w, y + d[1] * length * t + n[1] * w))
    for k in range(24, -1, -1):
        t = k / 24
        w = math.sin(math.pi * t) * length * 0.32
        pts.append((x + d[0] * length * t - n[0] * w, y + d[1] * length * t - n[1] * w))
    p.shape(pts, color=hexc(colour), outline=0.05, shade=0.3, shade_radius=0.15)
    p.paint(p.line_mask([(x + d[0] * length * 0.1, y + d[1] * length * 0.1), (x + d[0] * length * 0.8, y + d[1] * length * 0.8)], 0.035),
            color=hexc(vein))


def medallion(name="medallion", size=160):
    p = cell(name, size)
    p.shape(ellipse_pts(0, 0, 0.93, 0.93, 96), color=hexc(PLUM), outline=0.06, shade=0.25, shade_radius=0.4)
    p.shape(ellipse_pts(0, 0.04, 0.72, 0.72, 96), color=hexc(PLUM_DEEP), outline=0.0)
    ring_line(p, 0.84, 0.11, GOLD)
    ring_line(p, 0.84, 0.03, "#FFF1C4", cy=-0.02)
    return p


def tree_badge():
    """The fairy tree on a medallion: a brown trunk with a round door, a lush crown with pink blossoms."""
    p = medallion("tree_badge", 160)
    p.shape([(-0.2, 0.62), (-0.13, 0.05), (0.13, 0.05), (0.2, 0.62)], color=hexc("#A8693F"), outline=0.05, shade=0.3, shade_radius=0.12)
    p.shape(ellipse_pts(0.0, 0.47, 0.08, 0.13, 24), color=hexc("#FFCF6A"), outline=0.03)
    for cx, cy, r in ((0.0, -0.22, 0.42), (-0.3, -0.04, 0.3), (0.3, -0.04, 0.3), (-0.17, -0.42, 0.26), (0.18, -0.42, 0.26)):
        p.shape(ellipse_pts(cx, cy, r, r * 0.92, 48), color=hexc("#5CC064"), outline=0.05, shade=0.45, shade_radius=0.2)
    for cx, cy in ((-0.25, -0.2), (0.12, -0.36), (0.3, -0.08), (-0.05, -0.05), (-0.12, -0.5), (0.22, -0.55)):
        p.shape(ellipse_pts(cx, cy, 0.065, 0.065, 16), color=hexc("#FF8DBA"), outline=0.02)
    return p


def bar():
    W, H = 520, 96
    hl = W / H - 0.06
    p = Piece("bar_frame", W, H, (W / 2, H / 2), H / 2)
    p.shape(capsule_pts(hl - 0.04, 0.86), color=hexc(PLUM), outline=0.08, shade=0.2, shade_radius=0.3)
    p.shape(capsule_pts(hl - 0.24, 0.6), color=hexc(PLUM_DEEP), outline=0.0)
    rim = capsule_pts(hl - 0.1, 0.79)
    p.paint(p.line_mask(rim + [rim[0]], 0.1), color=hexc(GOLD))
    for side in (-1, 1):
        x = side * (hl - 0.55)
        leaf(p, x, -0.62, 0.62, -90 - side * 55)
        leaf(p, x + side * 0.28, -0.5, 0.5, -90 - side * 100, "#8DD86A", "#4FA85A")
    q = Piece("bar_fill", W, H, (W / 2, H / 2), H / 2)
    inner = capsule_pts(hl - 0.24, 0.6)
    q.paint(q.mask(inner), color=(0.78, 0.78, 0.78))
    j, i = np.mgrid[0:q.H, 0:q.W].astype(np.float32)
    y = (j + 0.5) / q.H * 2 - 1
    top = q.mask(capsule_pts(hl - 0.36, 0.36))
    top *= np.clip((-y - 0.0) / 0.5, 0, 1)
    q.paint(q.mask(inner) * np.clip((y - 0.15) / 0.6, 0, 1), color=(0.6, 0.6, 0.6), alpha=0.8)
    q.paint(top, color=WHITE, alpha=0.9)
    return [p, q]


def ribbon():
    """The wave's banner: a pink band bowing a little, its tails folded behind, gold edges."""
    W, H = 620, 150
    p = Piece("ribbon", W, H, (W / 2, H / 2), H / 2)
    hw = W / H
    for s in (-1, 1):
        tail = [(s * (hw - 1.15), -0.2), (s * (hw - 0.05), -0.2), (s * (hw - 0.45), 0.28), (s * (hw - 0.05), 0.78), (s * (hw - 1.15), 0.78)]
        p.shape(tail, color=hexc("#D84A87"), outline=0.07, shade=0.2, shade_radius=0.2)
        fold = [(s * (hw - 1.15), 0.78), (s * (hw - 0.75), 0.78), (s * (hw - 0.75), 0.52)]
        p.shape(fold, color=hexc("#9E2E66"), outline=0.04)
    top, bottom = [], []
    n = 40
    for k in range(n + 1):
        t = k / n
        x = (-hw + 0.75) + t * (2 * hw - 1.5)
        bow = 0.16 * (1 - (2 * t - 1) ** 2)
        top.append((x, -0.62 + bow))
        bottom.append((x, 0.52 + bow))
    band = top + list(reversed(bottom))
    p.shape(band, color=hexc("#FF6FA8"), outline=0.08, shade=0.35, shade_radius=0.35)
    p.paint(p.line_mask([(x, y + 0.13) for x, y in top], 0.07), color=hexc(GOLD))
    p.paint(p.line_mask([(x, y - 0.13) for x, y in bottom], 0.07), color=hexc(GOLD))
    for s in (-1, 1):
        p.shape(star_pts(s * (hw - 1.12), -0.06 + 0.16 * 0.1, 0.17, inner=0.42), color=hexc("#FFF1C4"), outline=0.04)
    return p


# ------------------------------------------------------------------------------------------------- the kingdoms

def kingdom(k):
    p = cell(f"kingdom_{k}", 128)
    if k == 0:      # the flower garden
        for s in (-1, 1):
            p.shape([(s * x, y) for x, y in ellipse_pts(0.42, 0.66, 0.36, 0.16, 32, a0=0.0)], color=hexc("#6FD66A"), outline=0.07)
        for a in range(5):
            ang = -math.pi / 2 + 2 * math.pi * a / 5
            cx, cy = math.cos(ang) * 0.4, math.sin(ang) * 0.4 - 0.08
            p.shape(ellipse_pts(cx, cy, 0.34, 0.34, 40), color=hexc("#FF8FC4"), outline=0.07, shade=0.25, shade_radius=0.25)
        p.shape(ellipse_pts(0, -0.08, 0.26, 0.26, 40), color=hexc("#FFC93C"), outline=0.07, shade=0.3, shade_radius=0.2)
    elif k == 1:    # the crystal lake: a drop over a little wave
        drop = [(0.0, -0.9)] + [(math.cos(a) * 0.56, 0.14 + math.sin(a) * 0.56) for a in
                                 np.linspace(-math.pi * 0.18, math.pi * 1.18, 40)]
        p.shape(drop, color=hexc("#39C6E6"), outline=0.08, shade=0.35, shade_radius=0.3)
        p.shape(ellipse_pts(-0.2, 0.0, 0.1, 0.2, 24), color=hexc("#D9F7FF"), outline=0.0)
        p.paint(p.line_mask([(-0.86, 0.82), (-0.5, 0.7), (-0.14, 0.82), (0.22, 0.7), (0.58, 0.82), (0.86, 0.72)], 0.12), color=INK)
        p.paint(p.line_mask([(-0.86, 0.82), (-0.5, 0.7), (-0.14, 0.82), (0.22, 0.7), (0.58, 0.82), (0.86, 0.72)], 0.06),
                color=hexc("#7DF2FF"))
    elif k == 2:    # the firefly forest: a round tree, two fireflies
        p.shape([(-0.12, 0.9), (0.12, 0.9), (0.1, 0.3), (-0.1, 0.3)], color=hexc("#8A5A3C"), outline=0.07)
        p.shape(ellipse_pts(0.0, -0.12, 0.66, 0.6, 48), color=hexc("#2E9E5B"), outline=0.08, shade=0.3, shade_radius=0.3)
        for x, y in ((-0.6, -0.72), (0.66, 0.2)):
            p.shape(ellipse_pts(x, y, 0.22, 0.22, 32), color=hexc("#FFF6C8"), outline=0.0, alpha=0.6)
            p.shape(ellipse_pts(x, y, 0.11, 0.11, 24), color=hexc("#FFE15A"), outline=0.05)
    elif k == 3:    # the snowy mountains: a snowflake
        for a in range(6):
            ang = math.pi / 2 + math.pi * a / 3
            d = (math.cos(ang), math.sin(ang))
            arm = [(0.0, 0.0), (d[0] * 0.86, d[1] * 0.86)]
            p.paint(p.line_mask(arm, 0.3), color=INK)
            for t in (0.5,):
                b = (d[0] * 0.86 * t, d[1] * 0.86 * t)
                for s in (-1, 1):
                    ang2 = ang + s * 0.8
                    p.paint(p.line_mask([b, (b[0] + math.cos(ang2) * 0.3, b[1] + math.sin(ang2) * 0.3)], 0.24), color=INK)
        for a in range(6):
            ang = math.pi / 2 + math.pi * a / 3
            d = (math.cos(ang), math.sin(ang))
            p.paint(p.line_mask([(0.0, 0.0), (d[0] * 0.86, d[1] * 0.86)], 0.16), color=hexc("#DDF4FF"))
            b = (d[0] * 0.43, d[1] * 0.43)
            for s in (-1, 1):
                ang2 = ang + s * 0.8
                p.paint(p.line_mask([b, (b[0] + math.cos(ang2) * 0.3, b[1] + math.sin(ang2) * 0.3)], 0.1), color=hexc("#DDF4FF"))
        p.shape(ellipse_pts(0, 0, 0.16, 0.16, 24), color=hexc("#7FC8F0"), outline=0.05)
    elif k == 4:    # the castle on the clouds
        p.shape([(-0.24, 0.2), (0.24, 0.2), (0.22, -0.38), (-0.22, -0.38)], color=hexc("#FFF4E2"), outline=0.07)
        p.shape([(-0.34, -0.36), (0.34, -0.36), (0.0, -0.92)], color=hexc("#B9A4FF"), outline=0.07, shade=0.25, shade_radius=0.2)
        p.shape(ellipse_pts(0.0, -0.1, 0.07, 0.11, 16), color=hexc("#3A3480"), outline=0.0)
        for x, y, r in ((-0.5, 0.42, 0.36), (0.0, 0.32, 0.44), (0.52, 0.44, 0.34), (-0.22, 0.6, 0.3), (0.28, 0.62, 0.3)):
            p.shape(ellipse_pts(x, y, r, r * 0.8, 40), color=WHITE, outline=0.07, shade=0.2, shade_radius=0.3)
        for x, y, r in ((-0.5, 0.42, 0.36), (0.0, 0.32, 0.44), (0.52, 0.44, 0.34), (-0.22, 0.6, 0.3), (0.28, 0.62, 0.3)):
            p.shape(ellipse_pts(x, y, r - 0.02, r * 0.8 - 0.02, 40), color=hexc("#FFF0F8"), outline=0.0)
    else:           # the moon palace: a crescent and a star
        outer = p.mask(ellipse_pts(-0.08, 0.04, 0.76, 0.76, 72))
        inner = p.mask(ellipse_pts(0.26, -0.18, 0.62, 0.62, 72))
        p.fill(np.clip(outer - inner, 0, 1), color=hexc("#FFE15A"), outline=0.08, shade=0.3, shade_radius=0.3)
        p.shape(star_pts(0.5, 0.36, 0.28, inner=0.45), color=hexc("#FFF6D6"), outline=0.06)
    return p


# ------------------------------------------------------------------------------------------------- the spells

def spell_icons():
    """The garden's spells as round-ish icons in their own colours (the HUD's spell bar)."""
    out = []
    # the lightning: a bolt, a spark at its point
    p = cell("spell_lightning", 128)
    bolt = [(0.2, -0.92), (-0.5, 0.1), (-0.04, 0.1), (-0.26, 0.92), (0.54, -0.18), (0.08, -0.18), (0.36, -0.92)]
    p.shape(bolt, color=hexc("#FFE15A"), outline=0.08, shade=0.25, shade_radius=0.25)
    p.shape([(0.22, -0.8), (-0.26, -0.02), (-0.1, -0.02), (0.28, -0.8)], color=hexc("#FFFBE0"), outline=0.0)
    for x, y in ((-0.62, -0.5), (0.66, 0.42), (-0.5, 0.66)):
        p.shape(star_pts(x, y, 0.13, inner=0.4, points=4, rot=0.0), color=hexc("#FFFFFF"), outline=0.04)
    out.append(p)
    # the rainbow: an arch of bands on two little clouds
    p = cell("spell_rainbow", 128)
    bands = ("#FF5A8C", "#FFA43C", "#FFE15A", "#6FD66A", "#39C6E6", "#9A6BFF")
    width = 0.1
    outer_pts = [(math.cos(math.pi + math.pi * t / 40) * 0.9, 0.36 + math.sin(math.pi + math.pi * t / 40) * 0.9) for t in range(41)]
    p.paint(p.mask(outer_pts + [(0.9 - width * len(bands), 0.36), (-0.9 + width * len(bands), 0.36)]), color=INK)
    for k, colour in enumerate(bands):
        r0 = 0.86 - k * width
        r1 = r0 - width
        arc = [(math.cos(math.pi + math.pi * t / 40) * r0, 0.36 + math.sin(math.pi + math.pi * t / 40) * r0) for t in range(41)]
        arc += [(math.cos(2 * math.pi - math.pi * t / 40) * r1, 0.36 + math.sin(2 * math.pi - math.pi * t / 40) * r1) for t in range(41)]
        p.paint(p.mask(arc), color=hexc(colour))
    for cx in (-0.6, 0.6):
        for dx, dy, r in ((-0.16, 0.06, 0.17), (0.0, -0.04, 0.22), (0.18, 0.06, 0.16)):
            p.shape(ellipse_pts(cx + dx, 0.42 + dy, r, r * 0.85, 32), color=hexc("#FFFFFF"), outline=0.05)
    out.append(p)
    # the shield: a bubble with a little tree inside
    p = cell("spell_shield", 128)
    p.shape(ellipse_pts(0, 0, 0.86, 0.86, 72), color=hexc("#8FD8FF"), outline=0.07, alpha=0.95)
    p.shape([(-0.08, 0.62), (0.08, 0.62), (0.07, 0.18), (-0.07, 0.18)], color=hexc("#8A5A3C"), outline=0.05)
    p.shape(ellipse_pts(0, -0.02, 0.38, 0.32, 40), color=hexc("#6FD66A"), outline=0.05, shade=0.25, shade_radius=0.25)
    p.shape(ellipse_pts(-0.36, -0.46, 0.18, 0.1, 24), color=hexc("#FFFFFF"), outline=0.0)
    p.paint(p.line_mask([(-0.7, 0.05), (-0.62, -0.25), (-0.44, -0.5)], 0.06), color=WHITE)
    out.append(p)
    # the seeds: seeds bursting out, sprouts in their middles
    p = cell("spell_seeds", 128)
    for k in range(7):
        a = math.radians(k * 360 / 7 - 90)
        x, y = math.cos(a) * 0.58, math.sin(a) * 0.58
        p.shape(ellipse_pts(x, y, 0.17, 0.13, 24, a0=a), color=hexc("#C9853F"), outline=0.05, shade=0.3, shade_radius=0.2)
        p.paint(p.line_mask([(math.cos(a) * 0.3, math.sin(a) * 0.3), (math.cos(a) * 0.42, math.sin(a) * 0.42)], 0.05), color=hexc("#FFE97A"))
    p.shape(ellipse_pts(0, 0.05, 0.24, 0.24, 40), color=hexc("#6FD66A"), outline=0.06, shade=0.3, shade_radius=0.2)
    for s_ in (-1, 1):
        p.shape([(0.0, 0.0), (s_ * 0.3, -0.24), (s_ * 0.1, -0.06)], color=hexc("#B6FF8A"), outline=0.04)
    out.append(p)
    # the big magic: a rainbow star
    p = cell("spell_burst", 128)
    for k, colour in enumerate(("#FF5A8C", "#FFA43C", "#FFE15A", "#6FD66A", "#39C6E6", "#9A6BFF")):
        r = 0.94 - k * 0.11
        p.shape(star_pts(0.0, 0.04, r, inner=0.5, points=5), color=hexc(colour), outline=0.05 if k == 0 else 0.0)
    out.append(p)
    # the freeze: a snowflake
    p = cell("spell_freeze", 128)
    for a in range(6):
        ang = math.pi / 2 + math.pi * a / 3
        d = (math.cos(ang), math.sin(ang))
        p.paint(p.line_mask([(0.0, 0.0), (d[0] * 0.86, d[1] * 0.86)], 0.3), color=INK)
        b = (d[0] * 0.5, d[1] * 0.5)
        for s_ in (-1, 1):
            ang2 = ang + s_ * 0.8
            p.paint(p.line_mask([b, (b[0] + math.cos(ang2) * 0.3, b[1] + math.sin(ang2) * 0.3)], 0.24), color=INK)
    for a in range(6):
        ang = math.pi / 2 + math.pi * a / 3
        d = (math.cos(ang), math.sin(ang))
        p.paint(p.line_mask([(0.0, 0.0), (d[0] * 0.86, d[1] * 0.86)], 0.16), color=hexc("#DDF4FF"))
        b = (d[0] * 0.5, d[1] * 0.5)
        for s_ in (-1, 1):
            ang2 = ang + s_ * 0.8
            p.paint(p.line_mask([b, (b[0] + math.cos(ang2) * 0.3, b[1] + math.sin(ang2) * 0.3)], 0.1), color=hexc("#DDF4FF"))
    p.shape(ellipse_pts(0, 0, 0.16, 0.16, 24), color=hexc("#7FC8F0"), outline=0.05)
    out.append(p)
    # the healing rain: a heart under drops
    p = cell("spell_heal", 128)
    p.shape(heart_pts(0.0, 0.2, 0.62), color=hexc("#FF5A8C"), outline=0.08, shade=0.35, shade_radius=0.35)
    for x, y in ((-0.5, -0.62), (0.0, -0.78), (0.5, -0.6)):
        drop = [(x, y - 0.16)] + [(x + math.cos(a) * 0.1, y + 0.02 + math.sin(a) * 0.1) for a in [math.pi * (-0.2 + 1.4 * t / 12) for t in range(13)]]
        p.shape(drop, color=hexc("#8FD8FF"), outline=0.04)
    out.append(p)
    return out


# ------------------------------------------------------------------------------------------------- the wand

def wand(name, star_colour, grip_colour):
    """The magic wand the HUD draws where a player points (2026-11-16): a pearl stick leaning down to the right like a
    mouse pointer, a gold band under the star, a coloured grip at its end, a big star at its tip (its middle at -0.46,
    -0.46 of the cell: 0.27, 0.27 of the picture, the point the wand points at), little sparkles round it."""
    p = cell(name, 192)
    star = (-0.46, -0.46)
    end = (0.8, 0.8)

    def at(t):
        return (star[0] + (end[0] - star[0]) * t, star[1] + (end[1] - star[1]) * t)
    o = 0.045
    stick = [at(0.12), at(1.0)]
    p.paint(p.line_mask(stick, 0.13 + 2 * o), color=INK)
    p.paint(p.line_mask(stick, 0.13), color=hexc("#FFF1FA"))
    # a soft shade down one side of the stick, a shine up the other
    p.paint(p.line_mask([(x + 0.025, y - 0.025) for x, y in stick], 0.04), color=hexc("#D9B8F0"), alpha=0.8)
    p.paint(p.line_mask([(x - 0.03, y + 0.03) for x, y in [at(0.2), at(0.9)]], 0.025), color=WHITE, alpha=0.9)
    # the gold band under the star, the grip at the end
    p.paint(p.line_mask([at(0.24), at(0.31)], 0.2 + 2 * o), color=INK)
    p.paint(p.line_mask([at(0.24), at(0.31)], 0.2), color=hexc("#FFC93C"))
    p.paint(p.line_mask([at(0.7), at(0.98)], 0.19 + 2 * o), color=INK)
    p.paint(p.line_mask([at(0.7), at(0.98)], 0.19), color=hexc(grip_colour))
    p.paint(p.line_mask([at(0.74), at(0.94)], 0.04), color=WHITE, alpha=0.55)
    # the star, with a highlight
    p.shape(star_pts(star[0], star[1], 0.44, inner=0.45, points=5, rot=-math.pi / 2), color=hexc(star_colour), outline=0.06,
            shade=0.3, shade_radius=0.3)
    p.shape(star_pts(star[0] - 0.05, star[1] - 0.06, 0.16, inner=0.5, points=5, rot=-math.pi / 2), color=WHITE, outline=0.0,
            alpha=0.75)
    for x, y, r in ((0.06, -0.78, 0.12), (-0.86, 0.02, 0.1), (0.2, -0.32, 0.07)):
        p.shape(star_pts(x, y, r, inner=0.38, points=4, rot=0.0), color=WHITE, outline=0.035)
    return p


# ------------------------------------------------------------------------------------------------- the how-to figures

def figure(name, pose):
    """A little white figure doing a move (Yjet!'s figures with this game's moves): lean to a side, draw a shape in the
    air with one hand (a dotted trail of a heart by it), both arms up."""
    p = cell(name, 192)
    o, k, dy = 0.075, 0.84, 0.08
    head, neck, hip = (0.0, -0.58), (0.0, -0.36), (0.0, 0.18)
    legs = [((-0.1, 0.18), (-0.13, 0.5), (-0.14, 0.86)), ((0.1, 0.18), (0.13, 0.5), (0.14, 0.86))]
    lean = 0.0
    if pose == "lean":
        arms = [((-0.16, -0.3), (-0.42, -0.08), (-0.62, 0.12)), ((0.16, -0.3), (0.42, -0.08), (0.62, 0.12))]
        lean = -0.32
        legs = [((-0.1, 0.18), (-0.18, 0.5), (-0.3, 0.86)), ((0.1, 0.18), (0.13, 0.5), (0.14, 0.86))]
    elif pose == "draw":
        arms = [((-0.16, -0.3), (-0.3, 0.02), (-0.33, 0.24)), ((0.16, -0.3), (0.4, -0.5), (0.6, -0.74))]
    elif pose == "swipe":
        arms = [((-0.16, -0.3), (-0.3, 0.02), (-0.33, 0.24)), ((0.16, -0.3), (0.46, -0.34), (0.8, -0.36))]
    elif pose in ("circle", "trace"):
        arms = [((-0.16, -0.3), (-0.3, 0.02), (-0.33, 0.24)), ((0.16, -0.3), (0.36, -0.48), (0.42, -0.72))]
    elif pose == "point":
        arms = [((-0.16, -0.3), (-0.3, 0.02), (-0.33, 0.24)), ((0.16, -0.3), (0.42, -0.44), (0.62, -0.62))]
    elif pose == "clap":
        arms = [((-0.16, -0.3), (-0.4, -0.08), (-0.05, -0.06)), ((0.16, -0.3), (0.4, -0.08), (0.05, -0.06))]
    else:
        arms = [((-0.16, -0.3), (-0.36, -0.62), (-0.52, -0.92)), ((0.16, -0.3), (0.36, -0.62), (0.52, -0.92))]

    def sh(pt):
        x, y = pt
        if lean:
            # the upper body turns round the hips
            t = max(0.0, (hip[1] - y) / (hip[1] - head[1] + 1e-6))
            ang = lean * t
            dx, dyy = x - hip[0], y - hip[1]
            x = hip[0] + dx * math.cos(ang) - dyy * math.sin(ang)
            y = hip[1] + dx * math.sin(ang) + dyy * math.cos(ang)
        return (x * k, y * k + dy)
    limbs = [[sh(a) for a in arm] for arm in arms] + [[sh(a) for a in leg] for leg in legs]
    for pts in limbs:
        p.paint(p.line_mask(pts, (0.17 + 2 * o) * k), color=INK)
    mid = sh((0.0, (neck[1] + hip[1]) / 2))
    p.shape(ellipse_pts(mid[0], mid[1], 0.2 * k, ((hip[1] - neck[1]) / 2 + 0.08) * k, 48), color=WHITE, outline=o * k)
    hc = sh(head)
    p.shape(ellipse_pts(hc[0], hc[1], 0.2 * k, 0.2 * k, 48), color=WHITE, outline=o * k)
    for pts in limbs:
        p.paint(p.line_mask(pts, 0.17 * k), color=WHITE)
    if pose == "draw":
        hand = sh((0.6, -0.74))
        trail = heart_pts(hand[0] + 0.26, hand[1] + 0.36, 0.3, n=18)
        for x, y in trail[::2]:
            p.shape(ellipse_pts(x, y, 0.045, 0.045, 12), color=WHITE, outline=0.03)
        p.shape(star_pts(hand[0], hand[1] - 0.06, 0.13, inner=0.38, points=4, rot=0.0), color=WHITE, outline=0.04)
    if pose == "lean":
        for s, y in ((-1, -0.1), (-1, 0.1)):
            p.paint(p.line_mask([(-0.94, y), (-0.74, y)], 0.06), color=WHITE, alpha=0.8)
    if pose == "swipe":
        # the sweep: lines above and below the arm, behind the hand, and a sparkle where it ends
        for y, x0, x1 in ((-0.58, 0.34, 0.66), (-0.14, 0.34, 0.66), (-0.7, 0.44, 0.62)):
            p.paint(p.line_mask([sh((x0, y)), sh((x1, y + 0.02))], 0.055), color=WHITE, alpha=0.9)
        hand = sh((0.8, -0.36))
        p.shape(star_pts(hand[0] + 0.06, hand[1], 0.13, inner=0.38, points=4, rot=0.0), color=WHITE, outline=0.04)
    if pose == "point":
        # the wand: a stick on from the hand, a star at its tip with little rays
        hand = sh((0.62, -0.62))
        tip = (hand[0] + 0.2, hand[1] - 0.2)
        p.paint(p.line_mask([hand, tip], 0.07 + 2 * 0.03), color=INK)
        p.paint(p.line_mask([hand, tip], 0.07), color=WHITE)
        p.shape(star_pts(tip[0], tip[1], 0.16, inner=0.42, points=5, rot=-math.pi / 2), color=WHITE, outline=0.04)
        for a in (-20, -65, -110):
            d = (math.cos(math.radians(a)), math.sin(math.radians(a)))
            p.paint(p.line_mask([(tip[0] + d[0] * 0.22, tip[1] + d[1] * 0.22), (tip[0] + d[0] * 0.34, tip[1] + d[1] * 0.34)], 0.05), color=WHITE)
    if pose == "trace":
        # a dotted circle the hand follows, the part behind it lit (big dots), the rest small
        hand = sh((0.42, -0.72))
        r = 0.3
        cx, cy = hand[0] + r * 0.92, hand[1] + r * 0.38
        start = math.atan2(hand[1] - cy, hand[0] - cx)
        for i in range(14):
            ang = start + i / 14 * 2 * math.pi
            x, y = cx + math.cos(ang) * r, cy + math.sin(ang) * r
            lit = i < 5
            d = 0.058 if lit else 0.036
            p.shape(ellipse_pts(x, y, d, d, 12), color=WHITE, outline=0.025, alpha=1.0 if lit else 0.85)
        p.shape(star_pts(hand[0], hand[1] - 0.04, 0.12, inner=0.38, points=4, rot=0.0), color=WHITE, outline=0.04)
    if pose == "circle":
        hand = sh((0.42, -0.72))
        for k in range(12):
            a = k / 12 * 2 * math.pi
            x, y = hand[0] + 0.02 + math.cos(a) * 0.28, hand[1] + 0.3 + math.sin(a) * 0.28
            p.shape(ellipse_pts(x, y, 0.04, 0.04, 12), color=WHITE, outline=0.03)
        p.shape(star_pts(hand[0], hand[1] - 0.04, 0.12, inner=0.38, points=4, rot=0.0), color=WHITE, outline=0.04)
    if pose == "clap":
        c = sh((0.0, -0.06))
        for a in (165, 200, 15, -20):
            d = (math.cos(math.radians(a)), math.sin(math.radians(a)))
            p.paint(p.line_mask([(c[0] + d[0] * 0.2, c[1] + d[1] * 0.2), (c[0] + d[0] * 0.4, c[1] + d[1] * 0.4)], 0.06), color=WHITE)
    if pose == "arms_up":
        for x in (-0.62, 0.62):
            p.shape(star_pts(x, -0.86, 0.12, inner=0.38, points=4, rot=0.0), color=WHITE, outline=0.04)
    return p


# ------------------------------------------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser(description="Zana!'s HUD pictures as code")
    ap.add_argument("--preview", action="store_true")
    args = ap.parse_args()
    os.makedirs(ART, exist_ok=True)
    cells = [bubble(), crown(), crystal(), alert(), ring()] + hearts() + stars() + shapes()
    cells += spell_icons()
    cells += [figure(f"pose_{n}", n) for n in ("point", "trace", "arms_up")]
    cells += [wand("wand", "#FFE15A", "#FF5AA8"), wand("wand_blue", "#7DF2FF", "#3A8BFF")]
    cells += [tree_badge(), medallion(), ribbon()] + bar()
    images = {c.name: c.image()[0] for c in cells}
    rects, height = pack(images)
    sheet = np.zeros((height, 1024, 4), np.uint8)
    for name, (x, y, w, h) in rects.items():
        sheet[y:y + h, x:x + w] = images[name]
    Image.fromarray(sheet, "RGBA").save(os.path.join(ART, "fairy_ui.png"), optimize=True)
    with open(os.path.join(ART, "fairy_ui.json"), "w", encoding="utf-8") as f:
        json.dump({"size": [1024, height], "sprites": [{"name": n, "rect": list(r)} for n, r in sorted(rects.items())]}, f,
                  indent=1)
    print(f"  {len(rects)} HUD pictures -> fairy_ui.png (1024 x {height})")
    if args.preview:
        os.makedirs(OUT, exist_ok=True)
        a = sheet[..., 3:4].astype(np.float32) / 255
        bg = np.zeros((height, 1024, 3), np.float32) + np.array(hexc("#2A2F66"), np.float32) * 255
        Image.fromarray((sheet[..., :3] * a + bg * (1 - a)).astype(np.uint8)).save(os.path.join(OUT, "sheet.png"))


if __name__ == "__main__":
    main()
