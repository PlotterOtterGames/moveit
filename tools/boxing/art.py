"""Superboks!'s HUD and effect pictures as code (2026-11-19, the overhaul), in the house style of Vrapo!, Zana! and Vishu!
(tools/dress/draw.py: smooth shapes 4x supersampled, soft pillow shading, plum outlines) and the brand's palette; the
words are our own chunky stroke lettering (no font), like the logo's.

    motion-service-py/.venv/Scripts/python tools/boxing/art.py [--preview]

Writes unity/MoveItGames/Assets/Games/Boxing/Art/boxing_ui.png + boxing_ui.json (named rectangles in pixels, y down):
    pow, bam, wham, boom   comic bursts where a punch lands (a jagged star, sunny in the middle, a word on it)
    ko                     the knockout's burst ("K.O.!")
    vs                     the intro's "VS"
    burst                  a white sunburst (tinted: a counter's flash, the count's backing)
    panel, capsule, disc   white shapes the HUD tints (panel and capsule drawn in nine pieces)
    ring                   a circle's outline (the portraits' frames)
    seg                    one segment of a health bar (white: tinted)
    star                   a five-pointed star (knockdowns, stars won)
    glove                  a boxing glove (white: blue for the hero, red for the opponent)
    arms_up, guard, lean, duck, punch   little figures doing the moves (the how-to, the super's call)
    bell, belt             the round's bell, a champion's belt
    stage_gym, stage_roof, stage_mountain, stage_stars   the climb's four places
    sweat, streak          a drop (hits), a speed line (dodges)
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
from draw import INK, Piece, dilate, ellipse_pts, hexc, star_pts  # noqa: E402

_spec = importlib.util.spec_from_file_location("space_art", os.path.join(REPO, "tools", "space", "art.py"))
space_art = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(space_art)
cell, pack, capsule_pts = space_art.cell, space_art.pack, space_art.capsule_pts

ART = os.path.join(REPO, "unity", "MoveItGames", "Assets", "Games", "Boxing", "Art")
OUT = os.path.join(HERE, "out")
WHITE = (1.0, 1.0, 1.0)
SUN, ORANGE, CORAL, RED, SKY, GRAPE = "#FFE15A", "#FF8A3D", "#FF5A4E", "#E8403A", "#31C6F4", "#7B4FB5"


def arc(cx, cy, r, a0, a1, n=24, ry=None):
    ry = ry if ry is not None else r
    return [(cx + math.cos(math.radians(a)) * r, cy + math.sin(math.radians(a)) * ry) for a in np.linspace(a0, a1, n)]


# letters in em (x right, y down: 0 = the capitals' top, 1 = the baseline), as stroke polylines; dots as (x, y) circles
LETTERS = {
    "P": ([[(0.08, 1.0), (0.08, 0.04), (0.4, 0.04)] + arc(0.4, 0.29, 0.25, -90, 90, 18) + [(0.4, 0.54), (0.08, 0.54)]], [], 0.74),
    "O": ([arc(0.4, 0.51, 0.34, 0, 360, 40, ry=0.47)], [], 0.8),
    "W": ([[(0.0, 0.04), (0.2, 0.98), (0.43, 0.36), (0.66, 0.98), (0.86, 0.04)]], [], 0.88),
    "B": ([[(0.08, 0.02), (0.08, 0.98)], [(0.08, 0.04), (0.36, 0.04)] + arc(0.36, 0.26, 0.22, -90, 90, 16) + [(0.36, 0.48), (0.08, 0.48)],
           [(0.08, 0.48), (0.42, 0.48)] + arc(0.42, 0.73, 0.25, -90, 90, 16) + [(0.42, 0.98), (0.08, 0.98)]], [], 0.76),
    "A": ([[(0.0, 0.98), (0.38, 0.03), (0.76, 0.98)], [(0.16, 0.64), (0.6, 0.64)]], [], 0.78),
    "M": ([[(0.05, 0.98), (0.05, 0.03), (0.43, 0.62), (0.81, 0.03), (0.81, 0.98)]], [], 0.88),
    "H": ([[(0.07, 0.02), (0.07, 0.98)], [(0.67, 0.02), (0.67, 0.98)], [(0.07, 0.5), (0.67, 0.5)]], [], 0.76),
    "K": ([[(0.07, 0.02), (0.07, 0.98)], [(0.66, 0.03), (0.09, 0.6)], [(0.3, 0.38), (0.7, 0.98)]], [], 0.74),
    "V": ([[(0.0, 0.03), (0.37, 0.98), (0.74, 0.03)]], [], 0.76),
    "S": ([arc(0.37, 0.27, 0.25, 330, 90, 18) + arc(0.37, 0.75, 0.25, 270, 510, 20)], [], 0.74),
    "!": ([[(0.1, 0.03), (0.1, 0.62)]], [(0.1, 0.9)], 0.34),
    ".": ([], [(0.1, 0.9)], 0.3),
}


def word_mask(p, text, cx, cy, em, width, spacing=0.06, tilt=0.0, bounce=0.04):
    """The word's strokes as one mask, centred at (cx, cy) in the cell's units, each letter turned and bounced a little."""
    total = sum(LETTERS[c][2] for c in text) + spacing * (len(text) - 1)
    x = cx - total * em / 2
    masks = []
    ct, st = math.cos(math.radians(tilt)), math.sin(math.radians(tilt))
    for i, c in enumerate(text):
        strokes, dots, adv = LETTERS[c]
        b = bounce * (1 if i % 2 else -1)
        lx = x + adv * em / 2
        for s in strokes:
            pts = []
            for ex, ey in s:
                ux, uy = x + ex * em - lx, (ey - 0.5 + b) * em
                pts.append((lx + ux * ct - uy * st, cy + uy * ct + ux * st))
            masks.append(p.line_mask(pts, width * em))
        for ex, ey in dots:
            ux, uy = x + ex * em - lx, (ey - 0.5 + b) * em
            masks.append(p.circle_mask(lx + ux * ct - uy * st, cy + uy * ct + ux * st, width * em * 0.62))
        x += (adv + spacing) * em
    return np.clip(sum(masks), 0, 1)


def shifted(m, down, right):
    out = np.zeros_like(m)
    h, w = m.shape
    if down >= 0 and right >= 0:
        out[down:, right:] = m[:h - down, :w - right]
    return out


def lettering(p, letters, face_top, face_bottom, edge, outline=0.06, depth=0.07):
    """Letters drawn as a sticker: a 3D edge stepped down-right, a plum outline round everything, a gradient face, a
    shine."""
    d = int(p.units(depth))
    e = np.zeros_like(letters)
    for k in range(1, d + 1, max(1, d // 10)):
        e = np.maximum(e, shifted(letters, k, k // 2))
    p.paint(dilate(np.maximum(letters, e), p.units(outline)), color=INK)
    p.paint(e, color=hexc(edge))
    ys = (np.arange(p.H, dtype=np.float32) + 0.5) / p.H
    t = np.clip((ys - 0.2) / 0.6, 0, 1)[:, None, None]
    grad = np.array(hexc(face_top), np.float32) * (1 - t) + np.array(hexc(face_bottom), np.float32) * t
    p.paint(letters, color=np.broadcast_to(grad, (p.H, p.W, 3)))
    shine = np.clip(letters - shifted(letters, int(p.units(0.04)), int(p.units(0.025))), 0, 1)
    p.paint(shine, color=(1.0, 1.0, 1.0), alpha=0.7)


def jag(cx, cy, rx, ry, spikes, inner, seed, rot=0.0):
    """A comic burst's outline: spikes of uneven length round an ellipse."""
    rng = np.random.default_rng(seed)
    pts = []
    for k in range(spikes * 2):
        a = rot + math.pi * k / spikes
        r = (1.0 - rng.uniform(0.0, 0.14)) if k % 2 == 0 else inner * (1.0 + rng.uniform(-0.06, 0.06))
        pts.append((cx + math.cos(a) * rx * r, cy + math.sin(a) * ry * r))
    return pts


def comic(name, text, core, rim, face="#FFFFFF", face_bottom="#FFE9B0", edge=CORAL, seed=1, w=420, h=280, tilt=-7.0):
    """A comic punch burst: a jagged star (light in the middle, hot at the rim, a darker star behind it), the word."""
    p = Piece(name, w, h, (w / 2, h / 2), h / 2)
    hw = w / h
    back = jag(0, 0, hw * 0.98, 0.98, 13, 0.72, seed, rot=0.2)
    p.shape(back, color=hexc(rim), outline=0.05, shade=0.25, shade_radius=0.25)
    front = jag(0, 0, hw * 0.8, 0.78, 11, 0.74, seed + 5)
    m = p.shape(front, color=hexc(core), outline=0.04, shade=0.35, shade_radius=0.35)
    glow = p.mask(ellipse_pts(0, 0, hw * 0.42, 0.42, 48))
    p.paint(glow * m, color=(1.0, 1.0, 0.92), alpha=0.55)
    letters = word_mask(p, text, 0.0, 0.02, 0.86 if len(text) <= 4 else 0.7, 0.2, tilt=tilt)
    lettering(p, letters, face, face_bottom, edge)
    return p


def vs():
    p = Piece("vs", 300, 260, (150, 130), 130)
    p.shape(jag(0, 0, 1.12, 0.98, 12, 0.7, 41), color=hexc(RED), outline=0.05, shade=0.3, shade_radius=0.3)
    letters = word_mask(p, "VS", 0.0, 0.0, 0.95, 0.22, tilt=-6.0)
    lettering(p, letters, SUN, ORANGE, "#B8341E")
    return p


def burst():
    p = cell("burst", 256)
    rays = 16
    pts = []
    for k in range(rays * 2):
        a = math.pi * k / rays
        r = 0.98 if k % 2 == 0 else 0.6
        pts.append((math.cos(a) * r, math.sin(a) * r))
    p.shape(pts, color=WHITE, outline=0.0, alpha=0.9)
    p.shape(ellipse_pts(0, 0, 0.58, 0.58, 48), color=WHITE, outline=0.0)
    return p


def shapes():
    out = []
    p = cell("panel", 128)
    r = 40 / 64.0
    pts = []
    for (cx, cy), a0 in (((1 - r, -1 + r), -90), ((1 - r, 1 - r), 0), ((-1 + r, 1 - r), 90), ((-1 + r, -1 + r), 180)):
        pts += arc(cx * 0.97, cy * 0.97, r * 0.97, a0, a0 + 90, 10)
    p.shape(pts, color=WHITE, outline=0.0)
    out.append(p)
    q = cell("capsule", 128, 64)
    q.shape(capsule_pts(1.95, 0.97), color=WHITE, outline=0.0)
    out.append(q)
    d = cell("disc", 128)
    d.shape(ellipse_pts(0, 0, 0.97, 0.97, 64), color=WHITE, outline=0.0)
    out.append(d)
    ring = cell("ring", 192)
    outer = ring.mask(ellipse_pts(0, 0, 0.97, 0.97, 96))
    inner = ring.mask(ellipse_pts(0, 0, 0.8, 0.8, 96))
    ring.paint(np.clip(outer - inner, 0, 1), color=WHITE)
    out.append(ring)
    seg2 = Piece("seg", 48, 96, (24, 48), 24)
    seg2.shape(rounded(0.82, 1.8, 0.4), color=WHITE, outline=0.0)
    out.append(seg2)
    s = cell("star", 128)
    s.shape(star_pts(0, 0.04, 0.95, inner=0.42), color=WHITE, outline=0.0)
    out.append(s)
    return out


def rounded(hw, hh, r, n=8):
    pts = []
    for (cx, cy), a0 in (((hw - r, -hh + r), -90), ((hw - r, hh - r), 0), ((-hw + r, hh - r), 90), ((-hw + r, -hh + r), 180)):
        pts += arc(cx, cy, r, a0, a0 + 90, n)
    return pts


def glove():
    """A boxing glove from the side (white: the HUD tints it): the round fist, the thumb, the cuff with its band."""
    p = cell("glove", 192)
    p.shape([(x - 0.05, y + 0.5) for x, y in rounded(0.34, 0.2, 0.12)], color=(0.86, 0.88, 0.94),
            outline=0.07, shade=0.3, shade_radius=0.2)
    fist = ellipse_pts(0.05, -0.12, 0.62, 0.52, 64)
    p.shape(fist, color=WHITE, outline=0.08, shade=0.35, shade_radius=0.3)
    p.shape(ellipse_pts(-0.42, -0.02, 0.2, 0.3, 32), color=WHITE, outline=0.06, shade=0.3, shade_radius=0.2)
    p.stroke([(0.0, 0.32), (0.3, 0.34)], 0.08, color=(0.78, 0.8, 0.9))
    shine = p.mask(ellipse_pts(0.18, -0.38, 0.26, 0.14, 24))
    p.paint(shine, color=WHITE, alpha=0.8)
    return p


def figure(name, pose):
    """A little white figure doing a move (the HUD tints it), as the other games' how-to figures: a round head, a body,
    thick limbs, gloves on its fists, plum outlines. Units: the cell's half size; y down."""
    p = cell(name, 192)
    o = 0.07
    lean = {"lean": 0.22, "duck": 0.0}.get(pose, 0.0)
    drop = 0.22 if pose == "duck" else 0.0
    head = (lean * 1.6, -0.56 + drop)
    neck = (lean * 1.1, -0.34 + drop)
    hip = (0.0, 0.2)
    arms = {
        "arms_up": [((-0.14, -0.3), (-0.26, -0.62), (-0.2, -0.92)), ((0.14, -0.3), (0.26, -0.62), (0.2, -0.92))],
        "guard": [((-0.14, -0.3), (-0.32, -0.1), (-0.12, -0.5)), ((0.14, -0.3), (0.32, -0.1), (0.12, -0.5))],
        "lean": [((-0.0, -0.28), (-0.16, -0.06), (0.06, -0.42)), ((0.28, -0.28), (0.44, -0.06), (0.3, -0.42))],
        "duck": [((-0.14, -0.08), (-0.3, 0.1), (-0.12, -0.24)), ((0.14, -0.08), (0.3, 0.1), (0.12, -0.24))],
        "punch": [((-0.14, -0.3), (-0.32, -0.1), (-0.12, -0.5)), ((0.14, -0.3), (0.5, -0.34), (0.86, -0.38))],
    }[pose]
    legs = [((-0.1, 0.2), (-0.2, 0.52 - drop * 0.3), (-0.22, 0.86)), ((0.1, 0.2), (0.2, 0.52 - drop * 0.3), (0.22, 0.86))]
    limbs = [(s, 0.13) for s in arms] + [(s, 0.15) for s in legs]
    ink = np.zeros((p.H, p.W), np.float32)
    body = np.zeros((p.H, p.W), np.float32)
    for pts, w in limbs:
        body = np.maximum(body, p.line_mask(list(pts), w))
    body = np.maximum(body, p.line_mask([neck, (neck[0] * 0.5, 0.0), hip], 0.3))
    body = np.maximum(body, p.circle_mask(head[0], head[1], 0.2))
    fists = [a[-1] for a in arms]
    gl = np.zeros_like(body)
    for fx, fy in fists:
        gl = np.maximum(gl, p.circle_mask(fx, fy, 0.11))
    ink = dilate(np.maximum(body, gl), p.units(o))
    p.paint(ink, color=INK)
    p.paint(body, color=WHITE)
    p.paint(gl, color=hexc(RED))
    if pose == "lean":
        p.stroke([(-0.62, -0.5), (-0.36, -0.5)], 0.07, color=hexc(SUN))
        p.stroke([(-0.62, -0.3), (-0.42, -0.3)], 0.07, color=hexc(SUN))
    if pose == "punch":
        for y in (-0.48, -0.36, -0.26):
            p.stroke([(0.5, y), (0.68, y)], 0.05, color=hexc(SUN))
    return p


def bell():
    p = cell("bell", 192)
    p.shape(ellipse_pts(0, 0.06, 0.8, 0.8, 64), color=hexc("#FFC93C"), outline=0.07, shade=0.4, shade_radius=0.4)
    p.shape(ellipse_pts(0, 0.06, 0.6, 0.6, 64), color=hexc("#FFE38A"), outline=0.03, shade=0.25, shade_radius=0.3)
    p.shape(ellipse_pts(0, 0.06, 0.14, 0.14, 24), color=hexc("#C99A3A"), outline=0.04)
    p.paint(p.mask(ellipse_pts(-0.3, -0.3, 0.2, 0.1, 24)), color=WHITE, alpha=0.8)
    return p


def belt():
    p = cell("belt", 256, 160)
    hw = 256 / 160
    p.shape(rounded(hw * 0.96, 0.32, 0.12), color=hexc("#1E1A2C"), outline=0.06)
    for s in (-1, 1):
        p.shape(ellipse_pts(s * hw * 0.58, 0.0, 0.28, 0.24, 32), color=hexc("#FFC93C"), outline=0.05, shade=0.35, shade_radius=0.2)
    p.shape(ellipse_pts(0, 0, 0.62, 0.7, 48), color=hexc("#FFC93C"), outline=0.07, shade=0.45, shade_radius=0.35)
    p.shape(star_pts(0, 0.02, 0.4, inner=0.42), color=hexc("#FFF2A8"), outline=0.04)
    p.shape(ellipse_pts(0, 0.02, 0.13, 0.13, 24), color=hexc(RED), outline=0.03)
    return p


def stage_icons():
    out = []
    g = cell("stage_gym", 192)
    g.shape(ellipse_pts(0, 0, 0.92, 0.92, 64), color=hexc("#2E6FE8"), outline=0.06, shade=0.3, shade_radius=0.4)
    g.shape(rounded(0.62, 0.42, 0.08), color=hexc("#F4F2F8"), outline=0.04)
    for y in (-0.22, 0.0, 0.22):
        g.stroke([(-0.6, y), (0.6, y)], 0.05, color=hexc(RED))
    for s in (-1, 1):
        g.stroke([(s * 0.62, -0.46), (s * 0.62, 0.42)], 0.09, color=hexc("#B9BDCC"))
    out.append(g)
    r = cell("stage_roof", 192)
    r.shape(ellipse_pts(0, 0, 0.92, 0.92, 64), color=hexc("#FF8A5A"), outline=0.06, shade=0.3, shade_radius=0.4)
    r.shape(ellipse_pts(0, 0.1, 0.36, 0.36, 40), color=hexc("#FFE15A"), outline=0.0)
    for x, h in ((-0.56, 0.5), (-0.28, 0.8), (0.0, 0.62), (0.3, 0.95), (0.58, 0.55)):
        r.shape([(x + px, 0.5 - h / 2 + py) for px, py in rounded(0.13, h / 2, 0.03)], color=hexc("#3E3460"), outline=0.03)
        for k in range(int(h * 4)):
            r.shape([(x + px, 0.5 - h + 0.12 + k * 0.2 + py) for px, py in rounded(0.035, 0.03, 0.01)], color=hexc("#FFD36A"), outline=0.0)
    out.append(r)
    m = cell("stage_mountain", 192)
    m.shape(ellipse_pts(0, 0, 0.92, 0.92, 64), color=hexc("#6AAEF2"), outline=0.06, shade=0.3, shade_radius=0.4)
    m.shape([(-0.8, 0.5), (-0.15, -0.55), (0.3, 0.05), (0.45, -0.2), (0.85, 0.5)], color=hexc("#6E7A9A"), outline=0.04)
    m.shape([(-0.36, -0.2), (-0.15, -0.55), (0.05, -0.22), (-0.08, -0.28), (-0.2, -0.16)], color=WHITE, outline=0.02)
    m.shape([(-0.9, 0.42), (0.9, 0.42), (0.7, 0.75), (-0.7, 0.75)], color=hexc("#F4F8FF"), outline=0.03)
    out.append(m)
    s = cell("stage_stars", 192)
    s.shape(ellipse_pts(0, 0, 0.92, 0.92, 64), color=hexc("#2A1E5C"), outline=0.06, shade=0.3, shade_radius=0.4)
    s.shape(ellipse_pts(0.1, 0.08, 0.4, 0.4, 48), color=hexc(GRAPE), outline=0.04, shade=0.35, shade_radius=0.3)
    ring = s.mask(ellipse_pts(0.1, 0.1, 0.72, 0.2, 48)) - s.mask(ellipse_pts(0.1, 0.1, 0.56, 0.12, 48))
    s.paint(np.clip(ring, 0, 1), color=hexc("#FFC93C"))
    for x, y, rr in ((-0.5, -0.5, 0.1), (0.55, -0.45, 0.07), (-0.6, 0.45, 0.06)):
        s.shape(star_pts(x, y, rr, inner=0.4, points=4), color=WHITE, outline=0.0)
    out.append(s)
    return out


def bits():
    out = []
    sw = cell("sweat", 64)
    pts = [(0.0, -0.9)] + arc(0.0, 0.25, 0.55, -30, 210, 24)
    sw.shape(pts, color=hexc("#BFE9FF"), outline=0.12, shade=0.3, shade_radius=0.3)
    sw.paint(sw.mask(ellipse_pts(-0.18, 0.2, 0.12, 0.2, 16)), color=WHITE, alpha=0.9)
    out.append(sw)
    st = Piece("streak", 256, 32, (128, 16), 16)
    ys = np.linspace(-1, 1, st.H)[:, None]
    xs = np.linspace(-1, 1, st.W)[None, :]
    a = np.clip(1 - np.abs(ys) * 1.1, 0, 1) * np.clip((xs + 1) / 2, 0, 1) ** 1.5
    st.paint(a.astype(np.float32), color=WHITE)
    out.append(st)
    return out


def main():
    ap = argparse.ArgumentParser(description="Superboks!'s HUD pictures as code")
    ap.add_argument("--preview", action="store_true")
    args = ap.parse_args()
    os.makedirs(ART, exist_ok=True)
    cells = [
        comic("pow", "POW!", SUN, ORANGE, seed=3),
        comic("bam", "BAM!", "#9FE8FF", SKY, face_bottom="#FFFFFF", edge="#2E6FE8", seed=7),
        comic("wham", "WHAM!", SUN, CORAL, seed=11, w=480),
        comic("boom", "BOOM!", "#FFB0E8", "#FF5AD2", face_bottom="#FFFFFF", edge=GRAPE, seed=13, w=480),
        comic("ko", "K.O.!", SUN, RED, seed=17, w=560, h=360, tilt=-5.0),
        vs(), burst(), glove(), bell(), belt(),
        figure("arms_up", "arms_up"), figure("guard", "guard"), figure("lean", "lean"), figure("duck", "duck"),
        figure("punch", "punch"),
    ] + shapes() + stage_icons() + bits()
    images = {c.name: c.image()[0] for c in cells}
    rects, height = pack(images, width=2048)
    sheet = np.zeros((height, 2048, 4), np.uint8)
    for name, (x, y, w, h) in rects.items():
        sheet[y:y + h, x:x + w] = images[name]
    Image.fromarray(sheet, "RGBA").save(os.path.join(ART, "boxing_ui.png"), optimize=True)
    with open(os.path.join(ART, "boxing_ui.json"), "w", encoding="utf-8") as f:
        json.dump({"size": [2048, height], "sprites": [{"name": n, "rect": list(r)} for n, r in sorted(rects.items())]}, f, indent=1)
    print(f"  {len(rects)} pictures -> boxing_ui.png (2048 x {height})")
    if args.preview:
        os.makedirs(OUT, exist_ok=True)
        a = sheet[..., 3:4].astype(np.float32) / 255
        bg = np.zeros((height, 2048, 3), np.float32) + np.array(hexc("#2A2F66"), np.float32) * 255
        Image.fromarray((sheet[..., :3] * a + bg * (1 - a)).astype(np.uint8)).save(os.path.join(OUT, "sheet.png"))


if __name__ == "__main__":
    main()
