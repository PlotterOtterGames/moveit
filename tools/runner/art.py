"""Vrapo!'s HUD pictures as code (2026-11-18, the console polish), in the house style of Zana!, Lunapark! and Vishu!
(tools/dress/draw.py: smooth shapes drawn 4x supersampled, soft "pillow" shading, plum outlines) and the brand's palette.

    motion-service-py/.venv/Scripts/python tools/runner/art.py [--preview]

Writes unity/MoveItGames/Assets/Games/Runner/Art/runner_ui.png + runner_ui.json (named rectangles in pixels, y down):
    logo          "Vrapo!": our own chunky rounded lettering, sunny gradient, a plum outline, a 3D edge, the "!"'s dot an egg
    egg, egg_gold the eggs to collect (the counter's icon, the eggs flying to it)
    footprint     a three-toed dino footprint (the distance)
    trophy        the best score's cup
    dino_head     Dino's head with his red scarf (the marker running toward the best score)
    panel         a rounded panel (white: the HUD tints it; drawn in nine pieces, corners PanelCorner px)
    capsule       a pill (white; nine pieces, corners its half height)
    ribbon        the new record's banner: gold, folded tails, star ends
    burst         the countdown's sunburst (white: tinted)
    star          a four-pointed sparkle
    hint_jump, hint_duck, hint_side   the moves over the obstacles while learning (white: tinted): a little dino
                                      jumping with an arrow up, ducking under a bar, stepping aside between arrows
Also the particles' textures: Art/fx_leaf.png (a leaf), Art/fx_sparkle.png (a soft four-pointed star).
--preview also writes tools/runner/out/sheet.png (the sheet on a night colour).
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
from draw import INK, SS, Piece, dilate, ellipse_pts, hexc, star_pts  # noqa: E402

_spec = importlib.util.spec_from_file_location("space_art", os.path.join(REPO, "tools", "space", "art.py"))
space_art = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(space_art)
cell, pack, capsule_pts = space_art.cell, space_art.pack, space_art.capsule_pts

ART = os.path.join(REPO, "unity", "MoveItGames", "Assets", "Games", "Runner", "Art")
OUT = os.path.join(HERE, "out")
WHITE = (1.0, 1.0, 1.0)
PANEL_CORNER = 40

SUN, ORANGE, CORAL = "#FFD84A", "#FF8A2A", "#FF5A4E"
GREEN, GREEN_DARK, GREEN_LIGHT = "#4CC46A", "#2B9561", "#8EE07A"
CREAM, TEAL = "#FFF3DE", "#2EC4B6"


# ------------------------------------------------------------------------------------------------- strokes

def arc(cx, cy, r, a0, a1, n=28):
    return [(cx + math.cos(math.radians(a)) * r, cy + math.sin(math.radians(a)) * r)
            for a in np.linspace(a0, a1, n)]


# letters in em (x right, y down: 0 = the capitals' top, 1 = the baseline; lower case from 0.38), as stroke polylines
LETTERS = {
    "V": ([[(0.0, 0.02), (0.36, 0.98), (0.72, 0.02)]], 0.78),
    "r": ([[(0.06, 0.4), (0.06, 0.98)], arc(0.4, 0.66, 0.32, 180, 275, 16)], 0.62),
    "a": ([arc(0.32, 0.69, 0.27, 0, 360, 40), [(0.6, 0.42), (0.6, 0.98)]], 0.72),
    "p": ([arc(0.36, 0.69, 0.27, 0, 360, 40), [(0.08, 0.42), (0.08, 1.3)]], 0.72),
    "o": ([arc(0.31, 0.69, 0.28, 0, 360, 40)], 0.66),
    "!": ([[(0.1, 0.02), (0.1, 0.62)]], 0.34),
}


def shifted(m, down, right):
    """A mask moved down and right by whole pixels, the edges filled with nothing (np.roll would wrap them round)."""
    out = np.zeros_like(m)
    h, w = m.shape
    out[down:, right:] = m[:h - down, :w - right]
    return out


def logo():
    """Vrapo!: chunky rounded strokes, each letter bouncing a little off the baseline, a sunny gradient, a 3D edge, a
    plum outline, a shine, and the "!"'s dot a spotted egg."""
    W, H = 1000, 390
    p = Piece("logo", W, H, (W / 2, H / 2), H / 2)
    em = 1.12                      # units (half the cell's height = 1) per em
    width = 0.24                   # stroke width, em
    text = "Vrapo!"
    total = sum(LETTERS[c][1] for c in text) + 0.08 * (len(text) - 1)
    x = -total * em / 2
    masks = []
    egg_at = None
    for i, c in enumerate(text):
        strokes, adv = LETTERS[c]
        bounce = (-0.05, 0.03, -0.02, 0.04, -0.03, -0.05)[i]
        tilt = (-5, 3, -2, 4, -3, 6)[i]
        ca, sa = math.cos(math.radians(tilt)), math.sin(math.radians(tilt))
        cx = x + adv * em / 2
        for s in strokes:
            pts = []
            for ex, ey in s:
                ux, uy = x + ex * em, (ey - 0.66 + bounce) * em
                dx, dy = ux - cx, uy
                pts.append((cx + dx * ca - dy * sa, dy * ca + dx * sa))
            masks.append(p.line_mask(pts, width * em))
        if c == "!":
            egg_at = (x + 0.1 * em, (0.9 - 0.66 + bounce) * em)
        x += (adv + 0.08) * em
    letters = np.clip(sum(masks), 0, 1)
    # the 3D edge: the letters stepped down and right in a deep orange, under everything
    depth = int(p.units(0.07 * em))
    edge = np.zeros_like(letters)
    for k in range(1, depth + 1, max(1, depth // 10)):
        edge = np.maximum(edge, shifted(letters, k, k // 2))
    outline = dilate(np.maximum(letters, edge), p.units(0.055 * em))
    p.paint(outline, color=INK)
    p.paint(edge, color=hexc("#C2501E"))
    # the face: a gradient from sun at the top to orange at the bottom
    ys = (np.arange(p.H, dtype=np.float32) + 0.5) / p.H
    t = np.clip((ys - 0.18) / 0.62, 0, 1)[:, None, None]
    grad = np.array(hexc(SUN), np.float32) * (1 - t) + np.array(hexc(ORANGE), np.float32) * t
    p.paint(letters, color=np.broadcast_to(grad, (p.H, p.W, 3)))
    # a shine along the letters' upper left
    shine = np.clip(letters - shifted(letters, int(p.units(0.05 * em)), int(p.units(0.03 * em))), 0, 1)
    p.paint(shine, color=(1.0, 0.98, 0.85), alpha=0.75)
    if egg_at is not None:
        ex, ey = egg_at
        egg_shape(p, ex, ey + 0.02, 0.19 * em)
    return p


def egg_shape(p, cx, cy, r, gold=False):
    """An egg (taller than wide), spotted (or gold with glints), outlined, shaded."""
    pts = []
    for k in range(64):
        a = 2 * math.pi * k / 64
        s = math.sin(a)
        rr = r * (1.0 - 0.13 * max(0.0, -s))     # narrower at the top (y down: sin < 0 is up)
        pts.append((cx + math.cos(a) * rr * 0.8, cy + s * r * (1.12 if s < 0 else 0.92)))
    m = p.shape(pts, color=hexc("#FFC93C" if gold else CREAM), outline=r * 0.16, shade=0.35, shade_radius=r * 0.8)
    if gold:
        for gx, gy, gr in ((-0.25, -0.35, 0.16), (0.3, 0.1, 0.11), (-0.1, 0.4, 0.09)):
            p.shape(star_pts(cx + gx * r, cy + gy * r, gr * r * 1.6, inner=0.38, points=4), color=hexc("#FFF6C8"), outline=0.0, clip=m)
    else:
        for sx, sy, sr, col in ((-0.3, -0.25, 0.17, TEAL), (0.28, 0.05, 0.14, CORAL), (-0.12, 0.42, 0.12, TEAL),
                                (0.32, -0.48, 0.09, CORAL), (-0.42, 0.2, 0.08, CORAL)):
            p.shape(ellipse_pts(cx + sx * r, cy + sy * r, sr * r, sr * r * 0.9, 20), color=hexc(col), outline=0.0, clip=m)
    # a glint
    p.shape(ellipse_pts(cx - 0.32 * r, cy - 0.5 * r, 0.12 * r, 0.2 * r, 16), color=WHITE, outline=0.0, alpha=0.85, clip=m)
    return m


def egg_icon(name, gold=False):
    p = cell(name, 128)
    egg_shape(p, 0.0, 0.05, 0.82, gold)
    return p


def footprint():
    """A three-toed dino footprint: a small heel pad and three long toes spreading from it, each ending in a pointed claw:
    one cream shape outlined."""
    p = cell("footprint", 128)
    parts = [ellipse_pts(0.0, 0.5, 0.24, 0.22, 32)]
    for ang, l in ((-40, 0.82), (0, 0.92), (40, 0.82)):
        a = math.radians(ang - 90)
        dx, dy = math.cos(a), math.sin(a)
        nx, ny = -dy, dx
        b = (dx * 0.08, 0.42 + dy * 0.08)
        tip = (dx * l, 0.42 + dy * l)
        w0, w1 = 0.15, 0.09
        mid = (b[0] + dx * (l - 0.08) * 0.62, b[1] + dy * (l - 0.08) * 0.62)
        parts.append([(b[0] + nx * w0, b[1] + ny * w0), (mid[0] + nx * w1, mid[1] + ny * w1), (tip[0], tip[1]),
                      (mid[0] - nx * w1, mid[1] - ny * w1), (b[0] - nx * w0, b[1] - ny * w0)])
    m = np.clip(sum(p.mask(q) for q in parts), 0, 1)
    p.paint(dilate(m, p.units(0.11)), color=INK)
    p.paint(m, color=hexc(CREAM))
    return p


INK_HEX = "#3B2647"


def trophy():
    p = cell("trophy", 128)
    gold, dark = hexc("#FFC93C"), hexc("#D98A1A")
    for s in (-1, 1):
        p.stroke(arc(s * 0.46, -0.32, 0.22, -90, 90, 16) if s > 0 else arc(s * 0.46, -0.32, 0.22, 90, 270, 16), 0.13, color=hexc(INK_HEX))
        p.stroke(arc(s * 0.46, -0.32, 0.22, -90, 90, 16) if s > 0 else arc(s * 0.46, -0.32, 0.22, 90, 270, 16), 0.07, color=gold)
    p.shape([(-0.58, -0.72), (0.58, -0.72), (0.5, -0.2), (0.25, 0.12), (-0.25, 0.12), (-0.5, -0.2)], color=gold, outline=0.08,
            shade=0.4, shade_radius=0.3)
    p.shape([(-0.12, 0.1), (0.12, 0.1), (0.16, 0.42), (-0.16, 0.42)], color=dark, outline=0.08)
    p.shape([(-0.42, 0.42), (0.42, 0.42), (0.48, 0.74), (-0.48, 0.74)], color=gold, outline=0.08, shade=0.3, shade_radius=0.2)
    p.shape(star_pts(0.0, -0.42, 0.2, inner=0.45), color=hexc("#FFF6C8"), outline=0.0)
    return p


def dino_head():
    """Dino's head for the progress bar: green, a big eye, a smile, his red scarf, an orange plate on top."""
    p = cell("dino_head", 128)
    p.shape(star_pts(0.08, -0.6, 0.2, inner=0.6, points=3, rot=-math.pi / 2), color=hexc(ORANGE), outline=0.07)
    p.shape(ellipse_pts(0.0, -0.05, 0.62, 0.56, 48), color=hexc(GREEN), outline=0.09, shade=0.35, shade_radius=0.4)
    p.shape(ellipse_pts(0.42, 0.16, 0.42, 0.32, 40), color=hexc(GREEN), outline=0.09, shade=0.3, shade_radius=0.3)
    p.shape(ellipse_pts(0.42, 0.16, 0.36, 0.26, 40), color=hexc(GREEN), outline=0.0, shade=0.3, shade_radius=0.3)
    p.shape(ellipse_pts(0.18, -0.2, 0.22, 0.24, 32), color=WHITE, outline=0.05)
    p.shape(ellipse_pts(0.24, -0.18, 0.13, 0.15, 24), color=hexc("#1B1730"), outline=0.0)
    p.shape(ellipse_pts(0.2, -0.24, 0.045, 0.05, 12), color=WHITE, outline=0.0)
    p.stroke(arc(0.48, 0.12, 0.2, 20, 110, 12), 0.05, color=hexc("#3A1E2C"))
    p.shape(ellipse_pts(0.06, 0.18, 0.08, 0.05, 12), color=hexc("#FF8F86"), outline=0.0, alpha=0.8)
    p.shape([(-0.62, 0.38), (0.5, 0.42), (0.46, 0.66), (-0.6, 0.62)], color=hexc("#E8403A"), outline=0.07, shade=0.25, shade_radius=0.2)
    p.shape([(-0.6, 0.5), (-0.95, 0.72), (-0.8, 0.86), (-0.5, 0.6)], color=hexc("#E8403A"), outline=0.06)
    return p


def panel():
    p = cell("panel", 128)
    r = PANEL_CORNER / 64.0
    pts = []
    for (cx, cy), a0 in (((1 - r, -1 + r), -90), ((1 - r, 1 - r), 0), ((-1 + r, 1 - r), 90), ((-1 + r, -1 + r), 180)):
        pts += arc(cx * 0.97, cy * 0.97, r * 0.97, a0, a0 + 90, 10)
    p.shape(pts, color=WHITE, outline=0.0)
    q = cell("capsule", 128, 64)
    q.shape(capsule_pts(1.95, 0.97), color=WHITE, outline=0.0)
    return [p, q]


def ribbon():
    W, H = 640, 150
    p = Piece("ribbon", W, H, (W / 2, H / 2), H / 2)
    hw = W / H
    for s in (-1, 1):
        tail = [(s * (hw - 1.15), -0.2), (s * (hw - 0.05), -0.2), (s * (hw - 0.45), 0.28), (s * (hw - 0.05), 0.78), (s * (hw - 1.15), 0.78)]
        p.shape(tail, color=hexc("#E0662A"), outline=0.07, shade=0.2, shade_radius=0.2)
        fold = [(s * (hw - 1.15), 0.78), (s * (hw - 0.75), 0.78), (s * (hw - 0.75), 0.52)]
        p.shape(fold, color=hexc("#9E3A16"), outline=0.04)
    top, bottom = [], []
    n = 40
    for k in range(n + 1):
        t = k / n
        x = (-hw + 0.75) + t * (2 * hw - 1.5)
        bow = 0.16 * (1 - (2 * t - 1) ** 2)
        top.append((x, -0.62 + bow))
        bottom.append((x, 0.52 + bow))
    p.shape(top + list(reversed(bottom)), color=hexc("#FFB52E"), outline=0.08, shade=0.35, shade_radius=0.35)
    p.paint(p.line_mask([(x, y + 0.13) for x, y in top], 0.06), color=hexc("#FFF0B0"))
    p.paint(p.line_mask([(x, y - 0.13) for x, y in bottom], 0.06), color=hexc("#D97A12"))
    for s in (-1, 1):
        p.shape(star_pts(s * (hw - 1.12), -0.04, 0.17, inner=0.42), color=hexc("#FFF6C8"), outline=0.04)
    return p


def burst():
    p = cell("burst", 256)
    rays = 14
    pts = []
    for k in range(rays * 2):
        a = math.pi * k / rays
        r = 0.98 if k % 2 == 0 else 0.62
        pts.append((math.cos(a) * r, math.sin(a) * r))
    p.shape(pts, color=WHITE, outline=0.0, alpha=0.9)
    p.shape(ellipse_pts(0, 0, 0.6, 0.6, 48), color=WHITE, outline=0.0)
    return p


def star():
    p = cell("star", 96)
    p.shape(star_pts(0, 0, 0.95, inner=0.3, points=4), color=WHITE, outline=0.0)
    return p


def mini_dino(p, x, y, s, pose):
    """A little white dino silhouette (the hints): its body, head, tail and legs, plum outlined; pose: run, jump, duck."""
    parts = []
    if pose == "duck":
        parts.append(ellipse_pts(x, y + 0.16 * s, 0.42 * s, 0.2 * s, 32))
        parts.append(ellipse_pts(x + 0.36 * s, y + 0.06 * s, 0.22 * s, 0.17 * s, 24))
        parts.append([(x - 0.3 * s, y + 0.12 * s), (x - 0.85 * s, y + 0.24 * s), (x - 0.3 * s, y + 0.3 * s)])
    else:
        lift = -0.25 * s if pose == "jump" else 0.0
        parts.append(ellipse_pts(x, y + lift, 0.3 * s, 0.34 * s, 32))
        parts.append(ellipse_pts(x + 0.2 * s, y - 0.42 * s + lift, 0.26 * s, 0.24 * s, 24))
        parts.append([(x - 0.18 * s, y + 0.08 * s + lift), (x - 0.8 * s, y + 0.3 * s + lift), (x - 0.2 * s, y + 0.28 * s + lift)])
        legs = [(x - 0.12 * s, y + 0.25 * s + lift), (x + 0.12 * s, y + 0.25 * s + lift)]
        for lx, ly in legs:
            parts.append(ellipse_pts(lx, ly + 0.2 * s, 0.09 * s, 0.17 * s, 16))
    masks = [p.mask(pts) for pts in parts]
    m = np.clip(sum(masks), 0, 1)
    p.paint(dilate(m, p.units(0.07)), color=INK)
    p.paint(m, color=WHITE)
    return m


def arrow(p, x0, y0, x1, y1, w=0.16):
    d = math.hypot(x1 - x0, y1 - y0)
    ux, uy = (x1 - x0) / d, (y1 - y0) / d
    px, py = -uy, ux
    hx, hy = x1 - ux * 0.32, y1 - uy * 0.32
    shaft = [(x0 + px * w / 2, y0 + py * w / 2), (hx + px * w / 2, hy + py * w / 2), (hx - px * w / 2, hy - py * w / 2),
             (x0 - px * w / 2, y0 - py * w / 2)]
    head = [(hx + px * 0.3, hy + py * 0.3), (x1, y1), (hx - px * 0.3, hy - py * 0.3)]
    m = np.clip(p.mask(shaft) + p.mask(head), 0, 1)
    p.paint(dilate(m, p.units(0.06)), color=INK)
    p.paint(m, color=WHITE)


def hints():
    out = []
    p = cell("hint_jump", 192)
    p.shape([(-0.8, 0.62), (0.8, 0.62), (0.8, 0.82), (-0.8, 0.82)], color=WHITE, outline=0.06)
    mini_dino(p, -0.05, 0.05, 0.85, "jump")
    arrow(p, 0.62, 0.3, 0.62, -0.85)
    out.append(p)
    p = cell("hint_duck", 192)
    p.shape([(-0.85, -0.55), (0.85, -0.55), (0.85, -0.32), (-0.85, -0.32)], color=WHITE, outline=0.06)
    mini_dino(p, 0.0, 0.42, 0.95, "duck")
    arrow(p, 0.7, -0.15, 0.7, 0.55)
    out.append(p)
    p = cell("hint_side", 192)
    mini_dino(p, 0.0, 0.05, 0.85, "run")
    arrow(p, -0.38, -0.05, -0.95, -0.05)
    arrow(p, 0.38, -0.05, 0.95, -0.05)
    out.append(p)
    return out


# ------------------------------------------------------------------------------------------------- particle textures

def fx_textures():
    """A leaf (for leaves, petals and confetti: tinted by the particle) and a soft four-pointed sparkle."""
    n = 64
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    u, v = (x + 0.5) / n * 2 - 1, (y + 0.5) / n * 2 - 1
    # leaf: a pointed oval with a vein
    ru, rv = u * 0.95, v
    inside = (ru / 0.55) ** 2 + (rv / 0.95) ** 2
    shape = np.clip((1.0 - inside) * 6.0, 0, 1) * np.clip((0.98 - np.abs(rv)) * 8, 0, 1)
    vein = np.clip(1.0 - np.abs(ru) * 18, 0, 1) * np.clip(0.8 - np.abs(rv), 0, 1)
    shade = 0.82 + 0.18 * (1 - v) * 0.5
    rgb = np.stack([shade - vein * 0.25] * 3, -1)
    leaf = np.dstack([np.clip(rgb, 0, 1), shape])
    Image.fromarray((leaf * 255).astype(np.uint8), "RGBA").save(os.path.join(ART, "fx_leaf.png"))
    r = np.sqrt(u * u + v * v)
    cross = np.exp(-np.abs(u) * 9) * np.exp(-np.abs(v) * 2.2) + np.exp(-np.abs(v) * 9) * np.exp(-np.abs(u) * 2.2)
    core = np.exp(-r * r * 9)
    a = np.clip(cross * 0.9 + core, 0, 1)
    spark = np.dstack([np.ones((n, n, 3), np.float32), a])
    Image.fromarray((spark * 255).astype(np.uint8), "RGBA").save(os.path.join(ART, "fx_sparkle.png"))


def main():
    ap = argparse.ArgumentParser(description="Vrapo!'s HUD pictures as code")
    ap.add_argument("--preview", action="store_true")
    args = ap.parse_args()
    os.makedirs(ART, exist_ok=True)
    cells = [logo(), egg_icon("egg"), egg_icon("egg_gold", True), footprint(), trophy(), dino_head(), ribbon(), burst(),
             star()] + panel() + hints()
    images = {c.name: c.image()[0] for c in cells}
    rects, height = pack(images)
    sheet = np.zeros((height, 1024, 4), np.uint8)
    for name, (x, y, w, h) in rects.items():
        sheet[y:y + h, x:x + w] = images[name]
    Image.fromarray(sheet, "RGBA").save(os.path.join(ART, "runner_ui.png"), optimize=True)
    with open(os.path.join(ART, "runner_ui.json"), "w", encoding="utf-8") as f:
        json.dump({"size": [1024, height], "sprites": [{"name": n, "rect": list(r)} for n, r in sorted(rects.items())]}, f, indent=1)
    fx_textures()
    print(f"  {len(rects)} HUD pictures -> runner_ui.png (1024 x {height}), fx_leaf.png, fx_sparkle.png")
    if args.preview:
        os.makedirs(OUT, exist_ok=True)
        a = sheet[..., 3:4].astype(np.float32) / 255
        bg = np.zeros((height, 1024, 3), np.float32) + np.array(hexc("#2A2F66"), np.float32) * 255
        Image.fromarray((sheet[..., :3] * a + bg * (1 - a)).astype(np.uint8)).save(os.path.join(OUT, "sheet.png"))


if __name__ == "__main__":
    main()
