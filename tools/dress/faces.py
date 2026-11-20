"""Things on the face (face rig: x from the two eyes, y from the head's size; template eyes at (+-0.064, -0.47))."""
import math

import numpy as np

from draw import BASE, blur, catmull, dilate, ellipse_pts, erode, heart_pts, hexc, star_pts, sym
import style as st
from wardrobe import HEAD_PPT, GOLD, WHITE, erase, gold, piece

EYE_X, EYE_Y = 0.064, -0.47


def lens(p, pts, color=(1.0, 0.70, 0.86), alpha=0.42, dark=False):
    """A lens: a soft colour gradient (darker at the top), a curved reflection, a bright glint."""
    m = p.mask(pts)
    X, Y = p.grid()
    xs, ys = [x for x, _ in pts], [y for _, y in pts]
    cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
    h = max(ys) - min(ys)
    t = np.clip((Y - min(ys)) / max(h, 1e-3), 0, 1)[..., None]
    c = np.asarray(color, np.float32)
    top = c * (0.35 if dark else 0.75)
    grad = top * (1 - t) + c * t
    p.paint(m, color=np.clip(grad, 0, 1), alpha=alpha)
    arc = np.exp(-((np.hypot(X - cx - 0.02, Y - cy - 0.03) - h * 0.55) / 0.006) ** 2) * (X < cx) * (Y < cy + h * 0.1) * m
    p.paint(arc, color=WHITE, alpha=0.55)
    glint = blur(p.mask(ellipse_pts(cx + (max(xs) - cx) * 0.45, cy - h * 0.22, 0.010, 0.006, 16)), p.units(0.002)) * m
    p.paint(glint, color=WHITE, alpha=0.9)
    return m


def frame(p, shape_fn, w=0.016, glitter=False):
    """Glasses: the same frame round both eyes, a bridge, short temples toward the ears; glossy plastic in the chosen
    colour."""
    ring = np.zeros((p.H, p.W), np.float32)
    for side in (-1, 1):
        outer = p.mask(shape_fn(EYE_X * side, EYE_Y, side))
        ring = np.maximum(ring, np.clip(dilate(outer, p.units(w)) - erode(outer, p.units(w * 0.25)), 0, 1))
    bridge = p.line_mask(catmull([(-0.03, -0.485), (0, -0.497), (0.03, -0.485)], False, 6), w * 0.9)
    temples = sum(p.line_mask([(0.115 * s, -0.48), (0.148 * s, -0.47)], w * 0.9) for s in (-1, 1))
    m = np.clip(ring + bridge + temples, 0, 1)
    st.fabric(p, m, st.dome(p, m, w * 0.6), None, base=BASE, up=0.09, down=0.28, soft=2.2, spec=0.7, shininess=26,
              line=0.0045, line_grey=0.26)
    if glitter:
        st.glitter(p, m, density=3000, size=0.0025, flares=0.05, twinkles=4)
    return m


def rhinestones(p, pts, r=0.0075):
    for x, y in pts:
        st.gem(p, x, y, r, hexc("#FFFFFF"), sparkle=False)
    p.meta.setdefault("sparkles", []).append([round(pts[0][0], 4), round(pts[0][1], 4), round(r * 4, 4)])


def face_hearts():
    """Syze zemra: heart-shaped glasses, glossy frames, rosy lenses, rhinestones on the corners."""
    p = piece("face_hearts", "glasses", (-0.18, -0.59, 0.18, -0.37), HEAD_PPT, "face", "face")
    shape = lambda x, y, s: heart_pts(x, y - 0.002, 0.066, rot=0.0)
    for side in (-1, 1):
        lens(p, shape(EYE_X * side, EYE_Y, side), color=(1.0, 0.55, 0.78), alpha=0.40)
    frame(p, shape)
    for side in (-1, 1):
        rhinestones(p, [((EYE_X + 0.05) * side, EYE_Y - 0.042), ((EYE_X + 0.064) * side, EYE_Y - 0.02)])


def face_stars():
    """Syze yje: star-shaped glasses with glitter frames and sky-blue lenses."""
    p = piece("face_stars", "glasses", (-0.19, -0.61, 0.19, -0.35), HEAD_PPT, "face", "face")
    shape = lambda x, y, s: star_pts(x, y, 0.08, 0.6, 5, rot=-math.pi / 2 + 0.15 * s, round_=1)
    for side in (-1, 1):
        lens(p, shape(EYE_X * side, EYE_Y, side), color=(0.62, 0.84, 1.0), alpha=0.36)
    frame(p, shape, w=0.014, glitter=True)


def face_sunglasses():
    """Syze dielli: cat-eye sunglasses, dark gradient lenses with a reflection, rhinestones on the wings."""
    p = piece("face_sunglasses", "glasses", (-0.19, -0.57, 0.19, -0.39), HEAD_PPT, "face", "face")

    def shape(x, y, s):
        pts = [(x - 0.055 * s, y - 0.02), (x + 0.02 * s, y - 0.035), (x + 0.07 * s, y - 0.052), (x + 0.065 * s, y + 0.01),
               (x + 0.02 * s, y + 0.036), (x - 0.035 * s, y + 0.03)]
        return catmull(pts, True, 8, sharp=(2,))
    for side in (-1, 1):
        lens(p, shape(EYE_X * side, EYE_Y, side), color=(0.30, 0.18, 0.40), alpha=0.88, dark=True)
    frame(p, shape, w=0.014)
    for side in (-1, 1):
        rhinestones(p, [((EYE_X + 0.066 - k * 0.017) * side, EYE_Y - 0.05 + k * 0.004) for k in range(3)], r=0.0065)


def filigree(p, pts, width=0.008):
    gold(p, p.line_mask(pts, width), spec=0.7, line=0.0015)


def eye_hole(x, y, s, w=0.042, h=0.026):
    return catmull([(x - w * s, y + 0.004), (x - w * 0.2 * s, y - h), (x + w * s, y - 0.006), (x + w * 0.3 * s, y + h * 0.9)],
                   True, 10, sharp=(0, 2))


def plume(p, root, tip, width, grey=0.86, seed=1):
    """A curling feather: a soft vane of fine barbs either side of a shaft."""
    rng = np.random.default_rng(seed)
    dx, dy = tip[0] - root[0], tip[1] - root[1]
    L = math.hypot(dx, dy)
    ux, uy = dx / L, dy / L
    nx, ny = -uy, ux
    vane = np.zeros((p.H, p.W), np.float32)
    for k in range(40):
        t = 0.08 + 0.9 * k / 39
        bx, by = root[0] + dx * t + nx * 0.02 * math.sin(t * 3), root[1] + dy * t + ny * 0.02 * math.sin(t * 3)
        w = width * math.sin(math.pi * min(1.0, t * 1.1)) ** 0.7
        for s in (-1, 1):
            a = 0.55 * s
            ex = bx + (nx * s * math.cos(0.5) + ux * math.sin(0.5)) * w
            ey = by + (ny * s * math.cos(0.5) + uy * math.sin(0.5)) * w
            vane = np.maximum(vane, p.line_mask([(bx, by), (ex, ey)], 0.0045 + rng.uniform(0, 0.002)))
    vane = blur(vane, p.units(0.002))
    st.fabric(p, vane, st.dome(p, vane, width * 0.5), None, base=grey, up=0.07, down=0.22, line=0.0, alpha=0.95)
    st.outline(p, vane, 0.003, grey=0.4, alpha=0.6)
    gold(p, p.line_mask([root, (root[0] + dx * 0.95, root[1] + dy * 0.95)], 0.006), spec=0.5, line=0.0)
    return vane


def face_mask():
    """Maskë balloje: a masquerade mask in glittering satin, gold filigree, a jewel, a feather plume on the side."""
    p = piece("face_mask", "mask", (-0.23, -0.84, 0.38, -0.35), HEAD_PPT, "face", "face")
    plume(p, (0.19, -0.545), (0.33, -0.80), 0.06, seed=3)
    plume(p, (0.18, -0.55), (0.25, -0.79), 0.05, grey=0.78, seed=4)
    half = [(0, -0.50), (0.06, -0.535), (0.13, -0.53), (0.20, -0.572), (0.19, -0.48), (0.13, -0.42), (0.06, -0.425),
            (0, -0.46)]
    m = p.mask(sym(half, sharp=[0, 3, 7]))
    st.fabric(p, m, st.dome(p, m, 0.03), None, base=BASE, up=0.08, down=0.26, spec=0.5, shininess=20, line=0.0055,
              line_grey=0.26)
    st.glitter(p, m, density=2600, size=0.0028, flares=0.05, twinkles=5)
    for side in (-1, 1):
        filigree(p, catmull([(0.04 * side, -0.505), (0.10 * side, -0.53), (0.17 * side, -0.53), (0.18 * side, -0.50),
                             (0.15 * side, -0.49)], False, 6))
        filigree(p, catmull([(0.05 * side, -0.44), (0.12 * side, -0.43), (0.165 * side, -0.47)], False, 6))
    st.gem(p, 0, -0.505, 0.02, hexc("#FF3B6B"))
    st.gem(p, 0.19, -0.545, 0.016, hexc("#7FD8FF"))
    for side in (-1, 1):
        hole = p.mask(eye_hole(EYE_X * side, EYE_Y, side))
        rim = np.clip(dilate(hole, p.units(0.007)) - hole, 0, 1) * (p.A > 0.5)
        gold(p, rim, spec=0.6, line=0.0)
        erase(p, hole)


def face_hero():
    """Maskë heroine: a glossy domino mask, a lighter rim round the eyes."""
    p = piece("face_hero", "mask", (-0.23, -0.59, 0.23, -0.37), HEAD_PPT, "face", "face")
    half = [(0, -0.49), (0.06, -0.525), (0.15, -0.525), (0.21, -0.542), (0.17, -0.46), (0.12, -0.425), (0.05, -0.44),
            (0, -0.46)]
    m = p.mask(sym(half, sharp=[0, 3, 7]))
    st.fabric(p, m, st.dome(p, m, 0.03), None, base=BASE, up=0.09, down=0.28, soft=2.2, spec=0.65, shininess=24,
              line=0.0055, line_grey=0.26)
    for side in (-1, 1):
        hole = p.mask(eye_hole(EYE_X * side, EYE_Y, side, w=0.045, h=0.028))
        rim = np.clip(dilate(hole, p.units(0.008)) - hole, 0, 1) * (p.A > 0.5)
        p.paint(rim, grey=0.95, alpha=0.85)
        erase(p, hole)


def face_cheeks():
    """Zemra në faqe: shiny heart stickers on the cheeks, little gold stars like freckles."""
    p = piece("face_cheeks", "cheeks", (-0.20, -0.49, 0.20, -0.31), HEAD_PPT, "face", "face")
    for side in (-1, 1):
        cx, cy = 0.105 * side, -0.395
        m = p.mask(heart_pts(cx, cy, 0.034, rot=0.25 * side))
        st.fabric(p, m, st.dome(p, m, 0.02), None, base=BASE, up=0.09, down=0.22, spec=0.6, shininess=20, line=0.004,
                  line_grey=0.3, alpha=0.95)
        for dx, dy in ((-0.05, -0.04), (0.045, -0.035), (0.0, 0.045)):
            sm = p.mask(star_pts(cx + dx * side, cy + dy, 0.012, 0.45, 5))
            gold(p, sm, spec=0.7, line=0.002)
    p.meta.setdefault("sparkles", []).extend([[-0.11, -0.41, 0.03], [0.1, -0.41, 0.03]])


ALL = [face_hearts, face_stars, face_sunglasses, face_mask, face_hero, face_cheeks]
