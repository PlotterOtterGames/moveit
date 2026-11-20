"""Wings (back rig: the screen's right wing, mirrored for the left; flapping round `pivot`) and capes (cape rig: the
shoulder row, then rigid down, the hem sways), with their front pieces (clasps, a fur collar: torso rig)."""
import math

import numpy as np

from draw import BASE, blur, catmull, dilate, ellipse_pts, erode, hexc, resample, star_pts, sym
import style as st
from wardrobe import BODY_PPT, GOLD, WHITE, gold, piece
from tops import fur

WING_PPT = 300


def wing_piece(item, bounds, **meta):
    """One wing (the screen's right one; Unity mirrors it for the left), its root near the spine."""
    return piece(item, "wing", bounds, WING_PPT, "back", "back", side="both", pivot=[0.06, 0.24], **meta)


def veins(p, root, tips, width, region, grey=0.5, color=None, alpha=0.7):
    for tip in tips:
        mid = ((root[0] + tip[0]) / 2 + 0.03, (root[1] + tip[1]) / 2 + 0.04)
        st.inner_line(p, catmull([root, mid, tip], False, 8), width, grey=grey if color is None else None, color=color,
                      alpha=alpha, clip=region)


def back_fairy():
    """Krahë zane: see-through fairy wings, two lobes, an iridescent sheen toward the edges, fine veins, glitter."""
    p = wing_piece("back_fairy", (-0.02, -0.76, 1.02, 1.07), flap=9)
    upper = catmull([(0.05, 0.2), (0.25, -0.15), (0.55, -0.55), (0.82, -0.69), (0.92, -0.5), (0.72, -0.12), (0.4, 0.15),
                     (0.08, 0.27)], True, 10)
    lower = catmull([(0.06, 0.28), (0.35, 0.35), (0.62, 0.55), (0.67, 0.86), (0.5, 0.96), (0.3, 0.76), (0.1, 0.4)], True, 10)
    X, Y = p.grid()
    whole = np.zeros((p.H, p.W), np.float32)
    for pts in (lower, upper):
        m = p.mask(pts)
        d = np.hypot(X - 0.06, Y - 0.25)
        edge = np.clip(1 - cv_dist(m, p) / p.units(0.10), 0, 1)
        g = np.clip(0.80 + 0.12 * np.clip(d, 0, 1) + 0.08 * edge, 0, 1)
        st.outline(p, m, 0.006, grey=0.5, alpha=0.85)
        p.paint(m, grey=g, alpha=0.62)
        p.paint(m * edge, grey=np.clip(g + 0.05, 0, 1), alpha=0.25)
        whole = np.maximum(whole, m)
    veins(p, (0.07, 0.25), [(0.80, -0.62), (0.60, -0.40), (0.45, 0.0), (0.58, 0.80), (0.40, 0.62), (0.62, 0.58)], 0.006,
          whole, grey=0.55, alpha=0.7)
    st.glitter(p, whole, density=900, size=0.0035, flares=0.06, twinkles=10)


def cv_dist(m, p):
    import cv2
    return cv2.distanceTransform((m > 0.5).astype(np.uint8), cv2.DIST_L2, 3)


def back_butterfly():
    """Krahë flutureje: butterfly wings, the colour glowing out from the body to a dark border with white spots, veins,
    eye spots."""
    p = wing_piece("back_butterfly", (-0.02, -0.81, 1.07, 1.11), flap=7)
    upper = catmull([(0.05, 0.18), (0.3, -0.25), (0.62, -0.69), (0.96, -0.62), (0.99, -0.25), (0.7, 0.1), (0.3, 0.22),
                     (0.08, 0.26)], True, 10)
    lower = catmull([(0.06, 0.27), (0.4, 0.28), (0.7, 0.5), (0.72, 0.78), (0.55, 1.03), (0.45, 0.9), (0.28, 0.75),
                     (0.08, 0.42)], True, 10)
    ink = hexc("#2B1E3A")
    whole = np.zeros((p.H, p.W), np.float32)
    X, Y = p.grid()
    for pts in (lower, upper):
        m = p.mask(pts)
        st.solid(p, m, ink, form=st.dome(p, m, 0.04), line=0.004, line_color=hexc("#120A1C"))
        inner = erode(m, p.units(0.05))
        d = np.hypot(X - 0.06, Y - 0.25)
        st.fabric(p, inner, st.dome(p, inner, 0.08), None, base=0.80, up=0.06, down=0.18, line=0.0)
        p.paint(inner, grey=np.clip(0.92 - 0.2 * np.clip(d, 0, 1.2), 0, 1), alpha=0.6)
        rim = np.clip(m - erode(m, p.units(0.035)), 0, 1)
        for x, y in resample(catmull(pts[::6] + [pts[0]], False, 4), 0.07):
            dm = p.circle_mask(x, y, 0.013) * rim
            p.paint(dm, color=WHITE, alpha=0.95)
        whole = np.maximum(whole, m)
    veins(p, (0.07, 0.25), [(0.90, -0.55), (0.75, -0.30), (0.60, 0.05), (0.60, 0.70), (0.50, 0.90)], 0.010, whole,
          color=ink, alpha=0.9)
    for cx, cy, r in ((0.62, -0.30, 0.09), (0.50, 0.62, 0.07)):
        st.solid(p, p.mask(ellipse_pts(cx, cy, r, r, 40)), ink, line=0.0)
        st.solid(p, p.mask(ellipse_pts(cx, cy, r * 0.75, r * 0.75, 40)), hexc("#FFD23F"), spec=0.3, line=0.0)
        st.solid(p, p.mask(ellipse_pts(cx, cy, r * 0.4, r * 0.4, 32)), ink, line=0.0)
        p.paint(p.mask(ellipse_pts(cx - r * 0.12, cy - r * 0.12, r * 0.13, r * 0.13, 16)), color=WHITE)
    st.glitter(p, whole, density=400, size=0.0035, flares=0.05, twinkles=6)


def feather(p, root, tip, width, grey, barbs=True):
    """One feather: widest two thirds along, a soft rounded end with a point, a shaft, fine barbs, shaded round."""
    dx, dy = tip[0] - root[0], tip[1] - root[1]
    L = math.hypot(dx, dy)
    ux, uy = dx / L, dy / L
    nx, ny = -uy, ux
    prof = [(0.0, 0.25), (0.3, 0.75), (0.65, 1.0), (0.88, 0.8), (1.0, 0.0)]
    left = [(root[0] + ux * L * t + nx * width * w, root[1] + uy * L * t + ny * width * w) for t, w in prof]
    right = [(root[0] + ux * L * t - nx * width * w, root[1] + uy * L * t - ny * width * w) for t, w in reversed(prof[:-1])]
    m = p.mask(catmull(left + right, True, 8, sharp=(4,)))
    st.cast_shadow(p, m, dx=0.006, dy=0.012, softness=0.012, amount=0.25)
    st.fabric(p, m, st.dome(p, m, width * 0.7), None, base=grey, up=0.05, down=0.2, line=0.0045, line_grey=0.42)
    st.inner_line(p, [(root[0] + ux * L * 0.05, root[1] + uy * L * 0.05), (root[0] + ux * L * 0.9, root[1] + uy * L * 0.9)],
                  width * 0.08, grey=grey - 0.18, alpha=0.5, clip=m)
    if barbs:
        for t in np.linspace(0.2, 0.85, 6):
            bx, by = root[0] + ux * L * t, root[1] + uy * L * t
            for s in (-1, 1):
                st.inner_line(p, [(bx, by), (bx + nx * s * width * 0.8 + ux * width * 0.5, by + ny * s * width * 0.8 + uy * width * 0.5)],
                              0.0025, grey=grey - 0.1, alpha=0.35, clip=m)
    return m


def back_angel():
    """Krahë engjëlli: soft feathered angel wings: long flight feathers fanning out under rows of rounded coverts, down
    along the top."""
    p = wing_piece("back_angel", (-0.07, -0.84, 1.36, 0.90), flap=6)
    arm = catmull([(0.06, 0.22), (0.2, -0.22), (0.45, -0.6), (0.72, -0.72), (0.92, -0.62)], False, 12)
    roots = resample(arm, 0.075)
    n = len(roots)
    for k, root in enumerate(roots):
        t = k / max(1, n - 1)
        a = math.radians(98 - 24 * t)
        length = 0.45 + 0.7 * t ** 0.8
        feather(p, root, (root[0] + math.cos(a) * length, root[1] + math.sin(a) * length), 0.07, 0.82 + 0.05 * (k % 2))
    for row, (drop, length, g) in enumerate(((0.12, 0.34, 0.88), (0.05, 0.22, 0.93))):
        for k, (x, y) in enumerate(resample(arm, 0.065)[:-1]):
            t = k / max(1, n - 1)
            a = math.radians(100 - 30 * t)
            root = (x, y + drop)
            feather(p, root, (root[0] + math.cos(a) * length, root[1] + math.sin(a) * length), 0.06 - 0.01 * row, g,
                    barbs=row == 0)
    down = np.zeros((p.H, p.W), np.float32)
    for x, y in resample(arm, 0.035):
        down = np.maximum(down, p.circle_mask(x, y, 0.045))
    st.fabric(p, down, st.dome(p, down, 0.04), None, base=0.95, up=0.04, down=0.16, line=0.0045, line_grey=0.45)
    st.glitter(p, p.coverage() > 0.5, density=250, size=0.0035, flares=0.05, twinkles=6)


def back_dragon():
    """Krahë dragoi: dragon wings: a bony arm with long fingers, a leathery membrane between them with scalloped edges,
    a claw at the top."""
    p = wing_piece("back_dragon", (-0.03, -0.90, 1.08, 0.97), flap=10)
    knuckle = (0.36, -0.46)
    top = (0.56, -0.73)
    tips = [(0.96, -0.55), (0.99, 0.05), (0.72, 0.55), (0.38, 0.76)]
    outline = [(0.06, 0.22), (0.30, -0.35), top, tips[0]]
    for a, b in zip(tips, tips[1:]):
        mid = ((a[0] + b[0]) / 2 - 0.10, (a[1] + b[1]) / 2 - 0.06)
        outline += [mid, b]
    outline += [(0.12, 0.42)]
    m = p.mask(catmull(outline, True, 6, sharp=tuple(range(len(outline)))))
    X, Y = p.grid()
    panels = st.Folds(p.units(0.02) * (0.5 + 0.5 * np.cos(np.arctan2(Y - knuckle[1], X - knuckle[0]) * 7)), p.units(0.02))
    g = st.fabric(p, m, st.dome(p, m, 0.12), panels, base=0.80, up=0.06, down=0.22, spec=0.15, valley=0.2, line=0.007,
                  line_grey=0.28)
    p.paint(m, grey=np.clip(g - 0.12 * np.clip((Y + 0.6) / 1.4, 0, 1), 0, 1), alpha=0.5)
    bone = p.line_mask(catmull([(0.07, 0.22), (0.30, -0.2), knuckle, top], False, 8), 0.034)
    for tip in tips:
        bone = np.maximum(bone, p.line_mask(catmull([knuckle, ((knuckle[0] + tip[0]) / 2, (knuckle[1] + tip[1]) / 2 - 0.03), tip],
                                                    False, 8), 0.02))
    st.fabric(p, bone, st.dome(p, bone, 0.012), None, base=0.58, up=0.08, down=0.24, spec=0.3, line=0.004, line_grey=0.24)
    claw = p.mask(catmull([(top[0] - 0.05, top[1] + 0.03), (top[0] + 0.02, top[1] - 0.10), (top[0] + 0.04, top[1] + 0.02)],
                          True, 6, sharp=(1,)))
    st.solid(p, claw, hexc("#FFF4E0"), form=st.dome(p, claw, 0.015), spec=0.4, line=0.003)


def back_cape():
    """Pelerinë heroine: a flowing satin cape with a high collar; gold star clasps on the shoulders (a front piece)."""
    p = piece("back_cape", "cape", (-0.97, -0.42, 0.97, 2.47), 280, "cape", "back")
    half = [(0, -0.05), (0.20, -0.04), (0.36, -0.02), (0.50, 0.25), (0.62, 0.90), (0.78, 1.70), (0.90, 2.30),
            (0.60, 2.38), (0.30, 2.32), (0, 2.40)]
    m = p.mask(sym(half, sharp=[0, len(half) - 1]))
    fl = st.fan_folds(p, m, (0, -0.6), 6, 0.08, 0.3, 2.3, seed=5, wobble=0.4, sharp=1.25)
    st.fabric(p, m, st.cylinder(m, depth=0.3), fl, base=0.74, up=0.06, down=0.24, spec=0.35, shininess=14, valley=0.24,
              line=0.008)
    collar = p.mask(sym([(0, -0.08), (0.22, -0.12), (0.34, -0.37), (0.43, -0.31), (0.38, 0.0), (0, 0.04)], sharp=[0, 5]))
    st.cast_shadow(p, collar, receiver=m, dy=0.03, softness=0.03, amount=0.4)
    st.fabric(p, collar, st.cylinder(collar, depth=0.5), None, base=0.64, spec=0.35, line=0.007)
    c = piece("back_cape", "clasp", (-0.49, -0.09, 0.49, 0.15), BODY_PPT, "torso", "top")
    gold(c, c.line_mask(catmull([(-0.34, 0.03), (0, 0.08), (0.34, 0.03)], False, 8), 0.012), spec=0.6)
    for side in (-1, 1):
        cm = c.mask(ellipse_pts(0.36 * side, 0.03, 0.06, 0.06, 40))
        gold(c, cm)
        sm = c.mask(star_pts(0.36 * side, 0.032, 0.035, 0.45, 5))
        st.solid(c, sm, hexc("#FFF4C0"), form=st.dome(c, sm, 0.01), spec=0.6, line=0.002)


def ermine(p, region, seed=3):
    """Ermine fur: white with little black tails dotted over it."""
    rng = np.random.default_rng(seed)
    ys, xs = np.nonzero(region > 0.5)
    if len(xs) == 0:
        return
    for _ in range(max(4, int(len(xs) / (p.ppt * 4) ** 2 / 0.012))):
        i = rng.integers(len(xs))
        x = (xs[i] / 4 - p.origin[0]) / p.ppt
        y = (ys[i] / 4 - p.origin[1]) / p.ppt
        tail = p.mask(catmull([(x - 0.008, y - 0.015), (x + 0.008, y - 0.015), (x + 0.004, y + 0.02), (x, y + 0.03), (x - 0.004, y + 0.02)],
                              True, 6, sharp=(3,))) * region
        p.paint(tail, color=hexc("#1E1A24"))


def back_cloak():
    """Mantel mbretëror: a royal velvet cloak scattered with gold stars, ermine fur down the fronts and the hem; an
    ermine collar with a jewelled clasp in front."""
    p = piece("back_cloak", "cloak", (-1.09, -0.32, 1.09, 2.80), 280, "cape", "back")
    half = [(0, -0.12), (0.25, -0.10), (0.42, -0.02), (0.58, 0.30), (0.75, 1.0), (0.90, 1.9), (0.98, 2.55), (0.60, 2.62),
            (0, 2.65)]
    m = p.mask(sym(half, sharp=[0, len(half) - 1]))
    fl = st.fan_folds(p, m, (0, -0.7), 7, 0.08, 0.2, 2.6, seed=9, wobble=0.4, sharp=1.25)
    st.fabric(p, m, st.cylinder(m, depth=0.3), fl, base=0.72, up=0.09, down=0.24, soft=1.0, spec=0.10, shininess=5,
              valley=0.2, line=0.008)
    rng = np.random.default_rng(2)
    ys, xs = np.nonzero(erode(m, p.units(0.1)) > 0.5)
    for _ in range(40):
        i = rng.integers(len(xs))
        x = (xs[i] / 4 - p.origin[0]) / p.ppt
        y = (ys[i] / 4 - p.origin[1]) / p.ppt
        sm = p.mask(star_pts(x, y, 0.022, 0.45, 5, rot=rng.uniform(0, 1)))
        gold(p, sm, spec=0.6, line=0.0015)
    hem = catmull([(-0.96, 2.54), (-0.5, 2.62), (0, 2.65), (0.5, 2.62), (0.96, 2.54)], False, 10)
    fm = fur(p, hem, 0.07)
    for side in (-1, 1):
        fm = np.maximum(fm, fur(p, catmull([(0.42 * side, -0.02), (0.6 * side, 0.4), (0.78 * side, 1.2), (0.92 * side, 2.0),
                                            (0.97 * side, 2.5)], False, 10), 0.055, seed=5 + side))
    ermine(p, erode(fm, p.units(0.02)), seed=4)
    col = piece("back_cloak", "collar", (-0.57, -0.17, 0.57, 0.31), BODY_PPT, "torso", "top")
    cm = fur(col, catmull([(-0.48, 0.16), (-0.38, 0.02), (-0.2, -0.05), (0, -0.04), (0.2, -0.05), (0.38, 0.02), (0.48, 0.16)],
                          False, 10), 0.075, seed=8)
    ermine(col, erode(cm, col.units(0.02)), seed=6)
    clasp = col.mask(ellipse_pts(0, 0.04, 0.045, 0.045, 32))
    gold(col, clasp)
    st.gem(col, 0, 0.04, 0.026, hexc("#FF3B6B"))


ALL = [back_fairy, back_butterfly, back_angel, back_cape, back_cloak, back_dragon]
