"""Headwear (head rig: placed and sized from the eyes; template eyes at (+-0.064, -0.47), head width 0.30, the
forehead's top at about -0.63, the skull's at about -0.80)."""
import math

import numpy as np

from draw import BASE, blur, catmull, dilate, ellipse_pts, erode, hexc, resample, rotate, star_pts, sym
import style as st
from wardrobe import HEAD_PPT, GOLD, PEARL, WHITE, bow, erase, flower, leaf, pearls, piece


def metal(p, m, base=BASE, spec=0.75, line=0.004, form=None, shininess=24):
    """Tinted polished metal (the item's colour is the metal: gold, silver, rose...): a strong bevel, a sharp shine."""
    form = st.dome(p, m, 0.012) if form is None else form
    return st.fabric(p, m, form, None, base=base, up=0.10, down=0.32, soft=2.4, spec=spec, shininess=shininess, line=line,
                     line_grey=0.26)


def head_crown():
    """Kurorë: a royal crown, five arched points with cut-outs crowned with pearls, a band set with a big ruby,
    sapphires and emeralds, in the chosen metal."""
    p = piece("head_crown", "crown", (-0.23, -1.03, 0.23, -0.61), HEAD_PPT, "head", "head")
    tips = [(-0.19, -0.875), (-0.095, -0.93), (0.0, -0.965), (0.095, -0.93), (0.19, -0.875)]
    valleys = [(-0.145, -0.76), (-0.05, -0.79), (0.05, -0.79), (0.145, -0.76)]
    outline = [(-0.175, -0.655), (-0.19, -0.72)] + [pt for pair in zip(tips, valleys + [None]) for pt in pair if pt is not None] + \
              [(0.19, -0.72), (0.175, -0.655), (0.0, -0.625)]
    m = p.mask(catmull(outline, True, 6, sharp=tuple(range(len(outline)))))
    # cut-outs in the points: little arches
    holes = np.zeros_like(m)
    for (x, y) in tips:
        holes = np.maximum(holes, p.mask(catmull([(x - 0.025, -0.74), (x, y + 0.07), (x + 0.025, -0.74), (x, -0.725)], True, 8)))
    m = np.clip(m - holes, 0, 1)
    metal(p, m, form=st.dome(p, m, 0.02))
    band = p.mask(catmull([(-0.178, -0.705), (0, -0.672), (0.178, -0.705), (0.172, -0.655), (0, -0.623), (-0.172, -0.655)],
                          True, 8, sharp=(0, 2, 3, 5)))
    metal(p, band, base=0.74, form=st.dome(p, band, 0.012))
    st.cast_shadow(p, band, dy=0.008, softness=0.006, amount=0.25)
    for x, y in tips:
        st.pearl(p, x, y - 0.012, 0.019)
    st.gem(p, 0, -0.72, 0.034, hexc("#E8306A"), cut="drop")
    for x in (-0.115, 0.115):
        st.gem(p, x, -0.668, 0.019, hexc("#3FA9FF"))
    for x in (-0.06, 0.06):
        st.gem(p, x, -0.652, 0.013, hexc("#3BD07A"))
    for x in (-0.155, 0.155):
        st.gem(p, x, -0.678, 0.011, hexc("#FFFFFF"))


def scroll(p, cx, cy, r, turn, side=1):
    """A filigree scroll (a curl of wire) as a list of points."""
    return [(cx + side * math.cos(a) * r * (1 - a / (turn * 1.6)), cy + math.sin(a) * r * (1 - a / (turn * 1.6)))
            for a in np.linspace(0, turn, 22)]


def head_tiara():
    """Diademë bore: a filigree tiara of curling scrolls round a big snowflake, rhinestones and pearls."""
    p = piece("head_tiara", "tiara", (-0.23, -0.90, 0.23, -0.59), HEAD_PPT, "head", "head")
    arc = catmull([(-0.185, -0.62), (-0.10, -0.675), (0, -0.69), (0.10, -0.675), (0.185, -0.62)], False, 10)
    wire = p.line_mask(arc, 0.018)
    for side in (-1, 1):
        for cx, cy, r, t in ((0.06, -0.73, 0.045, 4.2), (0.13, -0.70, 0.035, 4.0), (0.17, -0.655, 0.022, 3.6)):
            wire = np.maximum(wire, p.line_mask(scroll(p, cx * side, cy, r, t, side), 0.010))
    metal(p, wire, form=st.dome(p, wire, 0.008))
    from bottoms import snowflake
    sf = p.mask(ellipse_pts(0, -0.775, 0.085, 0.085, 6))
    snowflake(p, 0, -0.775, 0.085, color=hexc("#F2F6FF"), width=0.02)
    st.gem(p, 0, -0.775, 0.026, hexc("#7FD8FF"))
    for x, y in resample(arc, 0.035)[1:-1]:
        st.gem(p, x, y + 0.002, 0.008, hexc("#FFFFFF"), sparkle=False)
    for side in (-1, 1):
        st.pearl(p, 0.10 * side, -0.775, 0.013)
        st.gem(p, 0.155 * side, -0.68, 0.014, hexc("#A8E6FF"))
    p.meta.setdefault("sparkles", []).extend([[-0.03, -0.80, 0.05], [0.12, -0.70, 0.03]])


def rose(p, cx, cy, r, grey=BASE, color=None, rot=0.0, seed=1):
    """A rose seen from above: petals in rings spiralling into a tight bud, each petal cupped and shaded."""
    rng = np.random.default_rng(seed)
    rings = [(1.0, 6), (0.74, 5), (0.5, 4)]
    for ring, (scale, n) in enumerate(rings):
        for k in range(n):
            a = rot + 2 * math.pi * (k + 0.5 * ring) / n + rng.uniform(-0.15, 0.15)
            pr = r * scale
            px, py = cx + math.cos(a) * pr * 0.42, cy + math.sin(a) * pr * 0.42
            pts = rotate(ellipse_pts(px, py, pr * 0.55, pr * 0.40, 24), a, px, py)
            m = p.mask(pts)
            form = st.dome(p, m, pr * 0.3)
            if color is None:
                st.fabric(p, m, form, None, base=grey - 0.06 * (2 - ring), up=0.08, down=0.24, line=0.0035, line_grey=0.3)
            else:
                st.solid(p, m, st.deeper(color, 0.12 * (2 - ring)), form=form, line=0.0035)
    bud = p.mask(rotate(ellipse_pts(cx, cy, r * 0.22, r * 0.18, 16), rot, cx, cy))
    if color is None:
        st.fabric(p, bud, st.dome(p, bud, r * 0.1), None, base=grey - 0.14, line=0.003, line_grey=0.3)
    else:
        st.solid(p, bud, st.deeper(color, 0.3), form=st.dome(p, bud, r * 0.1), line=0.003)
    st.inner_line(p, [(cx + math.cos(t) * r * 0.16 * (1 - t / 9), cy + math.sin(t) * r * 0.14 * (1 - t / 9))
                      for t in np.linspace(0, 5, 12)], 0.003, grey=grey - 0.3 if color is None else None,
                  color=st.deeper(color, 0.6) if color is not None else None, alpha=0.6)


def head_flowers():
    """Kurorë me lule: a wreath of roses in the chosen colour, white blossoms, leaves and seed pearls on a vine."""
    p = piece("head_flowers", "wreath", (-0.31, -0.87, 0.31, -0.49), HEAD_PPT, "head", "head")
    green, green_light = hexc("#3FA85A"), hexc("#7AD67A")
    arc = [(math.cos(a) * 0.22, -0.60 + math.sin(a) * 0.12) for a in np.linspace(math.pi * 1.02, math.pi * 1.98, 15)]
    vine = p.line_mask(catmull(arc, False, 6), 0.016)
    st.solid(p, vine, hexc("#3B7F44"), form=st.dome(p, vine, 0.008), line=0.0025)
    for k, (x, y) in enumerate(arc[::2]):
        a = math.atan2(y + 0.6, x)
        leaf(p, x, y, x + math.cos(a + 1.2) * 0.075, y + math.sin(a + 1.2) * 0.075 - 0.02, 0.028, green)
        leaf(p, x, y, x + math.cos(a - 1.4) * 0.065, y + math.sin(a - 1.4) * 0.065, 0.024, green_light)
    for k, (x, y) in enumerate(arc[1::2]):
        if k % 2 == 0:
            rose(p, x, y, 0.062, grey=0.82, rot=k * 0.7, seed=k)
        else:
            flower(p, x, y, 0.046, petal=WHITE, center=GOLD, petals=6, rot=k)
    for x, y in arc[::3]:
        st.pearl(p, x + 0.01, y - 0.035, 0.009, color=hexc("#FFF4C0"))
    p.meta.setdefault("sparkles", []).extend([[-0.12, -0.72, 0.03], [0.14, -0.70, 0.03]])


def fluffy(p, m, grey=BASE, seed=3, length=0.012, color=None):
    """Plush fur on a shape: soft shading and short hair strokes, a wispy edge."""
    rng = np.random.default_rng(seed)
    edge = np.clip(dilate(m, p.units(0.006)) - erode(m, p.units(0.004)), 0, 1)
    ys, xs = np.nonzero(edge > 0.5)
    wisps = np.zeros_like(m)
    for i in rng.choice(len(xs), size=min(160, len(xs)), replace=False) if len(xs) else []:
        x, y = xs[i], ys[i]
        a = rng.uniform(0, 2 * math.pi)
        L = p.units(length) * rng.uniform(0.6, 1.2)
        import cv2
        cv2.line(wisps, (int(x), int(y)), (int(x + math.cos(a) * L), int(y + math.sin(a) * L)), 1.0, max(1, int(p.units(0.0025))),
                 cv2.LINE_AA)
    full = np.clip(m + wisps * 0.9, 0, 1)
    if color is None:
        st.fabric(p, full, st.dome(p, full, 0.03), None, base=grey, up=0.06, down=0.24, line=0.004, line_grey=0.3)
    else:
        st.solid(p, full, color, form=st.dome(p, full, 0.03), line=0.004)
    ys, xs = np.nonzero(m > 0.5)
    for i in rng.choice(len(xs), size=min(60, len(xs)), replace=False) if len(xs) else []:
        x = (xs[i] / 4 - p.origin[0]) / p.ppt
        y = (ys[i] / 4 - p.origin[1]) / p.ppt
        a = rng.uniform(-0.6, 0.6) + math.pi / 2
        st.inner_line(p, [(x, y), (x + math.cos(a) * 0.012, y + math.sin(a) * 0.012)], 0.0025,
                      grey=grey - 0.18 if color is None else None, color=st.deeper(color, 0.3) if color is not None else None,
                      alpha=0.45, clip=m)
    return full


def head_catears():
    """Veshë maceje: a headband with plush cat ears, pink inner ears with fluff, a little bow."""
    p = piece("head_catears", "ears", (-0.27, -0.97, 0.27, -0.49), HEAD_PPT, "head", "head")
    band = [(math.cos(a) * 0.205, -0.555 + math.sin(a) * 0.235) for a in np.linspace(math.pi * 1.08, math.pi * 1.92, 16)]
    bm = p.line_mask(catmull(band, False, 6), 0.024)
    st.fabric(p, bm, st.dome(p, bm, 0.01), None, base=0.62, up=0.08, down=0.24, spec=0.5, line=0.004, line_grey=0.26)
    for side in (-1, 1):
        ear = p.mask(catmull([(0.065 * side, -0.755), (0.16 * side, -0.935), (0.215 * side, -0.69)], True, 8, sharp=(1,)))
        fluffy(p, ear, seed=4 + side)
        inner = p.mask(catmull([(0.105 * side, -0.752), (0.163 * side, -0.88), (0.192 * side, -0.718)], True, 8, sharp=(1,)))
        st.solid(p, inner, hexc("#FFB3CF"), form=st.dome(p, inner, 0.02), line=0.0)
        for t in (0.25, 0.45, 0.6):
            st.inner_line(p, [(0.15 * side, -0.725 - t * 0.09), (0.162 * side, -0.76 - t * 0.12)], 0.0035, color=WHITE,
                          alpha=0.8, clip=dilate(inner, p.units(0.01)))
    bow(p, 0.13, -0.68, 0.05, color=hexc("#FF4F8B"), rot=-0.6)


def head_unicorn():
    """Bri njëbrirëshi: a spiral unicorn horn in the chosen colour with a shine, pastel ears, flowers and star gems."""
    p = piece("head_unicorn", "horn", (-0.25, -1.13, 0.25, -0.57), HEAD_PPT, "head", "head")
    for side in (-1, 1):
        ear = p.mask(catmull([(0.08 * side, -0.70), (0.16 * side, -0.865), (0.205 * side, -0.66)], True, 8, sharp=(1,)))
        fluffy(p, ear, color=hexc("#FFFFFF"), seed=7 + side)
        inner = p.mask(catmull([(0.11 * side, -0.70), (0.157 * side, -0.81), (0.18 * side, -0.68)], True, 8, sharp=(1,)))
        st.solid(p, inner, hexc("#FFC2E2"), form=st.dome(p, inner, 0.015), line=0.0)
    horn = p.mask(catmull([(-0.066, -0.74), (0.0, -1.09), (0.066, -0.74)], True, 8, sharp=(0, 1, 2)))
    X, Y = p.grid()
    twist = st.Folds(p.units(0.012) * (0.5 + 0.5 * np.cos((Y * 40 + X * 60))), p.units(0.012))
    st.fabric(p, horn, st.cylinder(horn, depth=0.9), twist, base=0.84, up=0.08, down=0.28, spec=0.6, shininess=20,
              valley=0.3, line=0.0055, line_grey=0.26)
    st.glitter(p, horn, density=900, size=0.003, flares=0.06, twinkles=3)
    for x, c in ((-0.11, hexc("#FF7EC8")), (0.0, hexc("#B08CFF")), (0.11, hexc("#6EC3FF"))):
        flower(p, x, -0.712, 0.05, petal=c, center=GOLD, petals=5, rot=x * 9)
    for x, y in ((-0.17, -0.75), (0.17, -0.75)):
        sm = p.mask(star_pts(x, y, 0.022, 0.45, 5))
        st.solid(p, sm, GOLD, form=st.dome(p, sm, 0.008), spec=0.6, line=0.003)
    p.meta.setdefault("sparkles", []).append([-0.01, -1.0, 0.05])


def head_sunhat():
    """Kapelë dielli: a wide straw hat, the weave catching the light, a satin ribbon band and bow in the chosen colour,
    a white flower."""
    p = piece("head_sunhat", "hat", (-0.47, -0.99, 0.47, -0.47), HEAD_PPT, "head", "head")
    straw = hexc("#EFCB84")
    X, Y = p.grid()
    brim = p.mask(ellipse_pts(0, -0.62, 0.42, 0.115, 96))
    weave = st.Folds(p.units(0.004) * (0.5 + 0.5 * np.sin(np.hypot(X / 0.42, (Y + 0.62) / 0.115) * 90))
                     * (0.5 + 0.5 * np.sin(np.arctan2(Y + 0.62, X) * 140)), p.units(0.004))
    st.solid(p, brim, straw, form=st.dome(p, brim, 0.06), folds=weave, valley=0.2, line=0.005)
    st.inner_line(p, ellipse_pts(0, -0.62, 0.40, 0.105, 96), 0.004, color=st.deeper(straw, 0.3), alpha=0.5, closed=True, clip=brim)
    crown = p.mask(catmull([(-0.19, -0.64), (-0.19, -0.80), (-0.12, -0.90), (0.12, -0.90), (0.19, -0.80), (0.19, -0.64)],
                           True, 10, sharp=(0, 5)))
    st.cast_shadow(p, crown, receiver=brim, dx=0.02, dy=0.01, softness=0.02, amount=0.4)
    rows = st.Folds(p.units(0.004) * (0.5 + 0.5 * np.sin(Y * 260)), p.units(0.004))
    st.solid(p, crown, straw, form=st.cylinder(crown, depth=0.6), folds=rows, valley=0.25, line=0.005)
    band = p.mask(catmull([(-0.19, -0.69), (0, -0.66), (0.19, -0.69), (0.19, -0.63), (0, -0.60), (-0.19, -0.63)], True, 8,
                          sharp=(0, 2, 3, 5)))
    st.fabric(p, band, st.cylinder(band, depth=0.5), None, base=BASE, spec=0.4, line=0.004)
    bow(p, 0.16, -0.65, 0.07, grey=BASE)
    flower(p, -0.10, -0.67, 0.06, petal=WHITE, center=GOLD, petals=6)
    # the brim must not hide the eyes: keep the face clear below the brim's front edge
    erase(p, p.mask(ellipse_pts(0, -0.44, 0.14, 0.11, 48)))


ALL = [head_crown, head_tiara, head_flowers, head_catears, head_unicorn, head_sunhat]
