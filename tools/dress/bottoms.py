"""Skirts (skirt rig: from the top's waist row at y 0.8, then rigid down along the thighs) and the mermaid's tail (tail
rig: waist, knees (y 1.92), ankles (y 2.85))."""
import math

import numpy as np

from draw import BASE, catmull, ellipse_pts, erode, hexc, resample, star_pts, sym, blur
import style as st
from wardrobe import BODY_PPT, GOLD, PEARL, WHITE, bow, fabric_light, flower, gold, leaf, pearls, piece
from tops import fur, petal

WAIST = [(-0.30, 0.79), (0, 0.83), (0.30, 0.79)]


def waistband(p, grey=None, color=None, width=0.055):
    """A satin or gold waistband over the skirt's top edge (it meets any top's waist)."""
    pts = catmull(WAIST, False, 8)
    m = p.line_mask(pts, width)
    if color is not None:
        gold(p, m, color=color)
    else:
        st.fabric(p, m, st.dome(p, m, width * 0.4), None, base=grey, up=0.07, down=0.2, spec=0.4, shininess=20, line=0.005)
    return m


def folded_hem(x0, x1, y, depth, count, phase=0.0, n=60, wobble=0.0, seed=1):
    """A hem following its folds: where a fold's ridge comes forward the hem hangs lower (count ridges across)."""
    rng = np.random.default_rng(seed)
    pts = []
    for k in range(n + 1):
        u = k / n
        x = x0 + (x1 - x0) * u
        ridge = 0.5 + 0.5 * math.cos(u * count * 2 * math.pi + phase)
        pts.append((x, y + depth * ridge ** 1.3 + wobble * rng.uniform(-1, 1)))
    return pts


def bottom_ballgown():
    """Princeshë: a full ball gown. A glittering underskirt in front, a satin overskirt swept back into two drapes
    gathered at gold rosettes, a hem that follows the folds, tulle peeking out below."""
    p = piece("bottom_ballgown", "skirt", (-1.17, 0.70, 1.17, 3.10), BODY_PPT, "skirt", "bottom")
    hem_y = 2.90
    # tulle petticoat peeking out
    pet = folded_hem(1.04, -1.04, hem_y + 0.05, 0.035, 30, seed=2)
    pm = p.mask(catmull([(-0.95, 2.5), (0.95, 2.5)] + pet, True, 4))
    st.tulle(p, pm, base=0.93, alpha=0.7, folds=st.hanging_folds(p, -1.05, 1.05, 30, 0.01, 2.5, 3.0, seed=12))
    # the underskirt: the whole bell, lighter
    count = 9
    under_hem = folded_hem(1.02, -1.02, hem_y - 0.02, 0.05, count, phase=0.6)
    under = p.mask(catmull([(-0.30, 0.79), (0, 0.83), (0.30, 0.79), (0.45, 1.12), (0.72, 1.85), (0.95, 2.55)] + under_hem
                           + [(-0.95, 2.55), (-0.72, 1.85), (-0.45, 1.12)], True, 6, sharp=(0, 2)))
    fl = st.fan_folds(p, under, (0, 0.15), count, 0.05, 0.9, 2.9, seed=7, wobble=0.45, sharp=1.3)
    g = st.fabric(p, under, st.cylinder(under, depth=0.35), fl, base=0.90, up=0.05, down=0.2, spec=0.16, shininess=14,
                  valley=0.16, line=0.007)
    st.glitter(p, under, density=1700, size=0.0036, lightness=np.clip((g - 0.66) / 0.3, 0, 1), flares=0.05, twinkles=10)
    # the overskirt: two drapes, swept back from the front and gathered at the hips
    for side in (-1, 1):
        drape = [(0.0, 0.80), (0.30 * side, 0.79), (0.46 * side, 1.12), (0.74 * side, 1.85), (0.98 * side, 2.55),
                 (1.0 * side, 2.80), (0.80 * side, 2.86), (0.60 * side, 2.62), (0.46 * side, 2.14), (0.40 * side, 1.62),
                 (0.30 * side, 1.20), (0.12 * side, 0.96)]
        dm = p.mask(catmull(drape, True, 8, sharp=(0,)))
        fold = st.fan_folds(p, dm, (0.38 * side, 0.9), 5, 0.07, 1.0, 2.8, seed=20 + side, wobble=0.4, sharp=1.2)
        st.cast_shadow(p, dm, receiver=under, dx=-0.01 * side, dy=0.01, softness=0.03, amount=0.45)
        st.fabric(p, dm, st.cylinder(dm, depth=0.5), fold, base=BASE, up=0.06, down=0.23, spec=0.32, shininess=16,
                  valley=0.22, line=0.0075)
        flower(p, 0.40 * side, 1.62, 0.085, petal=GOLD, center=hexc("#FF4F8B"), petals=6, rot=0.3)
        pearls(p, catmull([(0.40 * side, 1.70), (0.37 * side, 1.86), (0.40 * side, 2.0)], False, 6), 0.011)
    wb = waistband(p, color=GOLD, width=0.05)
    st.cast_shadow(p, wb, dy=0.02, softness=0.02, amount=0.35)


def bottom_petals():
    """Zanë: a skirt of big soft petals in three layers over a glittering tulle underskirt, a vine belt with a flower."""
    p = piece("bottom_petals", "skirt", (-0.92, 0.70, 0.92, 1.96), BODY_PPT, "skirt", "bottom")
    green = hexc("#5CC46E")
    tul = folded_hem(0.84, -0.84, 1.80, 0.04, 14)
    tm = p.mask(catmull([(-0.30, 0.79), (0.30, 0.79), (0.62, 1.3)] + tul + [(-0.62, 1.3)], True, 4))
    st.tulle(p, tm, base=0.94, alpha=0.6, folds=st.fan_folds(p, tm, (0, 0.4), 16, 0.012, 0.9, 1.85, seed=5))
    st.glitter(p, tm, density=900, size=0.0032, flares=0.05, twinkles=4)
    for y_end, half_w, n, g, width in ((1.82, 0.76, 7, 0.68, 0.17), (1.52, 0.63, 6, 0.79, 0.17), (1.24, 0.49, 5, 0.9, 0.15)):
        order = sorted(range(n), key=lambda k: -abs((k + 0.5) / n * 2 - 1))
        for k in order:
            u = (k + 0.5) / n * 2 - 1
            petal(p, (u * 0.25, 0.80), (u * half_w, y_end - 0.06 * u * u), width, g + (0.04 if k % 2 else -0.02))
    belt = catmull(WAIST, False, 8)
    vine = p.line_mask(belt, 0.035)
    st.solid(p, vine, green, form=st.dome(p, vine, 0.017), line=0.003)
    for k, (x, y) in enumerate(resample(belt, 0.075)):
        leaf(p, x, y, x + (0.05 if k % 2 else -0.05), y + 0.045, 0.024, green)
    flower(p, 0.18, 0.82, 0.068, petal=hexc("#FFC2E2"), center=GOLD)


def bottom_tail():
    """Sirenë: a mermaid's tail of iridescent sequin scales (it follows the legs: waist, knees, ankles), a chiffon frill
    with pearls at the waist, a big sheer fin with ribs."""
    p = piece("bottom_tail", "tail", (-0.72, 0.70, 0.72, 3.22), BODY_PPT, "tail", "bottom")
    for side in (-1, 1):
        lobe = [(0.02 * side, 2.60), (0.22 * side, 2.68), (0.50 * side, 2.84), (0.68 * side, 3.08), (0.44 * side, 3.13),
                (0.22 * side, 3.0), (0.04 * side, 2.92)]
        m = p.mask(catmull(lobe, True, 10))
        X, Y = p.grid()
        ribs = st.Folds(p.units(0.012) * (0.5 + 0.5 * np.cos(np.arctan2(X - 0.0, Y - 2.62) * 26)), p.units(0.012))
        g = st.fabric(p, m, st.dome(p, m, 0.08), ribs, base=0.86, up=0.06, down=0.2, spec=0.3, valley=0.25, line=0.006,
                      alpha=0.9)
        fade = np.clip((Y - 2.75) / 0.35, 0, 1)
        p.paint(m * fade * 0.6, grey=np.clip(g + 0.08, 0, 1), alpha=0.8)
        st.glitter(p, m, density=900, size=0.003, flares=0.04, twinkles=3)
    half = [(0, 0.79), (0.30, 0.79), (0.40, 1.05), (0.37, 1.5), (0.29, 1.92), (0.20, 2.40), (0.15, 2.72), (0, 2.78)]
    tail = p.mask(sym(half, sharp=[0, len(half) - 1]))
    st.fabric(p, tail, st.cylinder(tail, depth=0.8), None, base=0.78, spec=0.25, line=0.007)
    st.sequin_rows(p, tail, size=0.036, lightness=fabric_light(p, tail) * 0.6 + 0.4, seed=61)
    st.glitter(p, tail, density=700, size=0.0032, lightness=fabric_light(p, tail), flares=0.05, twinkles=8)
    st.outline(p, tail, 0.007)
    # the chiffon frill and pearls at the waist
    top_edge = catmull(WAIST, False, 8)
    low = folded_hem(0.36, -0.36, 0.96, 0.05, 7)
    fm = p.mask(catmull(list(top_edge) + low, True, 4))
    st.fabric(p, fm, st.cylinder(fm, depth=0.5), st.hanging_folds(p, -0.36, 0.36, 7, 0.014, 0.8, 1.0, seed=3),
              base=0.92, spec=0.2, valley=0.2, line=0.005, alpha=0.95)
    st.cast_shadow(p, fm, receiver=tail, dy=0.02, softness=0.02, amount=0.35)
    pearls(p, top_edge, 0.017)


def bottom_hero():
    """Superheroinë: a short skirt of crisp knife pleats, a gold hem, a gold belt with a star buckle."""
    p = piece("bottom_hero", "skirt", (-0.64, 0.70, 0.64, 1.48), BODY_PPT, "skirt", "bottom")
    half = [(0, 0.79), (0.30, 0.79), (0.42, 1.04), (0.56, 1.37), (0.30, 1.40), (0, 1.41)]
    m = p.mask(sym(half, sharp=[0, len(half) - 1]))
    X, Y = p.grid()
    ang = np.arctan2(X, Y + 0.3)
    u = (ang * 16 / math.pi) % 1.0
    pleats = st.Folds(p.units(0.02) * np.where(u < 0.65, u / 0.65, (1 - u) / 0.35), p.units(0.02))
    st.fabric(p, m, st.cylinder(m, depth=0.5), pleats, base=BASE, up=0.06, down=0.24, soft=2.4, spec=0.2, valley=0.3,
              line=0.007)
    hem = catmull([(-0.55, 1.365), (-0.3, 1.395), (0, 1.405), (0.3, 1.395), (0.55, 1.365)], False, 8)
    gold(p, p.line_mask(hem, 0.022))
    belt = waistband(p, color=GOLD, width=0.075)
    st.cast_shadow(p, belt, dy=0.016, softness=0.012, amount=0.35)
    buckle = p.mask(ellipse_pts(0, 0.83, 0.07, 0.06, 32))
    gold(p, buckle, color=hexc("#FFE38A"))
    sm = p.mask(star_pts(0, 0.835, 0.048, 0.45, 5))
    st.solid(p, sm, hexc("#E8304A"), form=st.dome(p, sm, 0.012), spec=0.4, line=0.003)
    p.meta.setdefault("sparkles", []).append([-0.02, 0.81, 0.06])


def bottom_tutu():
    """Festë / Zanë: a puffy tutu of four tulle layers, sparkling, a satin waistband with a bow."""
    p = piece("bottom_tutu", "skirt", (-0.95, 0.70, 0.95, 1.44), BODY_PPT, "skirt", "bottom")
    layers = ((0.90, 1.32, 0.74, 0.92), (0.82, 1.27, 0.82, 0.85), (0.72, 1.20, 0.88, 0.8), (0.60, 1.10, 0.93, 0.75))
    for k, (half_w, y_end, g, a) in enumerate(layers):
        edge = folded_hem(half_w, -half_w, y_end - 0.03, 0.05, 11 + 2 * k, phase=k * 0.9, seed=k)
        m = p.mask(catmull([(-0.30, 0.80), (0.30, 0.80), (half_w * 0.97, y_end - 0.09)] + edge + [(-half_w * 0.97, y_end - 0.09)],
                           True, 4))
        fl = st.fan_folds(p, m, (0, 0.55), 18 + 3 * k, 0.012, 0.85, y_end, seed=30 + k, sharp=1.1)
        st.tulle(p, m, base=g, alpha=a, folds=fl)
        st.outline(p, m, 0.004, grey=0.45, alpha=0.5)
        st.glitter(p, m, density=600, size=0.0032, flares=0.05, twinkles=3, seed=40 + k)
    wb = waistband(p, grey=0.72, width=0.06)
    st.cast_shadow(p, wb, dy=0.016, softness=0.015, amount=0.35)
    bow(p, -0.18, 0.84, 0.075, grey=0.72)


def snowflake(p, cx, cy, r, color=WHITE, width=None, clip=None):
    """An embroidered snowflake: six arms with little branches, a bead in the middle."""
    w = width or r * 0.15
    m = np.zeros((p.H, p.W), np.float32)
    for k in range(6):
        a = math.pi / 6 + math.pi * k / 3
        ex, ey = cx + math.cos(a) * r, cy + math.sin(a) * r
        m = np.maximum(m, p.line_mask([(cx, cy), (ex, ey)], w))
        for t in (0.5, 0.78):
            bx, by = cx + math.cos(a) * r * t, cy + math.sin(a) * r * t
            for s in (-1, 1):
                m = np.maximum(m, p.line_mask([(bx, by), (bx + math.cos(a + s * 0.8) * r * 0.28, by + math.sin(a + s * 0.8) * r * 0.28)],
                                              w * 0.8))
    if clip is not None:
        m = m * clip
    st.solid(p, m, color, form=st.dome(p, m, w * 0.5), spec=0.4, line=0.002, line_color=hexc("#9AA4C8"))
    st.pearl(p, cx, cy, r * 0.13, color=hexc("#EAF6FF"))


def bottom_winter():
    """Dimër: an A-line velvet skirt with embroidered snowflakes, a white fur hem, a satin ribbon belt with a bow."""
    p = piece("bottom_winter", "skirt", (-0.82, 0.70, 0.82, 2.02), BODY_PPT, "skirt", "bottom")
    hem = folded_hem(0.70, -0.70, 1.84, 0.03, 6)
    m = p.mask(catmull([(-0.30, 0.79), (0, 0.83), (0.30, 0.79), (0.48, 1.25)] + hem + [(-0.48, 1.25)], True, 6, sharp=(0, 2)))
    fl = st.fan_folds(p, m, (0, 0.2), 6, 0.045, 0.9, 1.85, seed=81, wobble=0.4, sharp=1.2)
    st.fabric(p, m, st.cylinder(m, depth=0.45), fl, base=BASE, up=0.08, down=0.24, soft=1.1, spec=0.12, shininess=6,
              valley=0.15, line=0.0075)
    for k, (x, y) in enumerate([(-0.25, 1.10), (0.10, 1.00), (0.32, 1.30), (-0.40, 1.50), (-0.05, 1.45), (0.45, 1.62),
                                (0.15, 1.66), (-0.22, 1.72), (0.26, 1.13), (-0.12, 1.25)]):
        snowflake(p, x, y, 0.042 + 0.014 * (k % 3), clip=m)
    fur(p, catmull([(-0.72, 1.84), (-0.36, 1.87), (0, 1.88), (0.36, 1.87), (0.72, 1.84)], False, 10), 0.06)
    wb = waistband(p, grey=0.68, width=0.06)
    st.cast_shadow(p, wb, dy=0.016, softness=0.015, amount=0.35)
    bow(p, 0.0, 0.84, 0.08, grey=0.68)


ALL = [bottom_ballgown, bottom_petals, bottom_tail, bottom_hero, bottom_tutu, bottom_winter]
