"""Tops (torso rig: shoulders (+-0.40, 0), hips (+-0.24, 1.0), the waist row skirts start from at y 0.8) and their
sleeves (arm rig: y 0 at the shoulder, 0.64 the elbow, 1.21 the wrist; x across the arm). Every top covers from the
neck to the hips and over the upper arms: in the mirror a T-shirt must not show around it."""
import math

import numpy as np

from draw import BASE, catmull, dilate, ellipse_pts, erode, hexc, resample, rotate, star_pts, sym, blur
import style as st
from wardrobe import (BODY_PPT, GOLD, PEARL, SILVER, WHITE, bow, braid_trim, fabric_light, flower, gold, gold_line,
                      leaf, pearls, piece)

TORSO = (-0.58, -0.14, 0.58, 1.10)


# ---------------------------------------------------------------------------------------------------- shared shapes

def yoke_shape(neck_y=0.0, neck_w=0.115, low=0.36):
    """The shoulders and upper chest from a round neckline out to the shoulder tips (half outline, centre top first)."""
    return [(0, neck_y + 0.035), (neck_w * 0.6, neck_y + 0.01), (neck_w, neck_y - 0.05), (0.30, -0.04), (0.42, 0.0),
            (0.475, 0.08), (0.48, 0.17), (0.43, 0.30), (0.30, low - 0.02), (0, low)]


def bodice(p, half, sharp=(), depth=0.75, spec=0.25, shininess=22, seams=True, base=BASE, valley=0.05, line=0.0065):
    """A fitted bodice: a cylinder lit from the top left, satin shine, princess seams."""
    m = p.mask(sym(half, sharp=list(sharp) + [0, len(half) - 1]))
    folds = st.hanging_folds(p, -0.3, 0.3, 2.3, 0.009, 0.1, 0.9, seed=3, wobble=0.0, sharp=3, grow=0.0) if seams else None
    st.fabric(p, m, st.cylinder(m, depth=depth), folds, base=base, spec=spec, shininess=shininess, valley=valley, line=line)
    return m


def princess_seams(p, m, top=0.16, x0=0.15, grey=0.56):
    for s in (-1, 1):
        st.inner_line(p, catmull([(x0 * s, top), (x0 * 0.92 * s, 0.4), (x0 * 0.85 * s, 0.62), (x0 * 0.8 * s, 0.9)], False, 8),
                      0.0035, grey=grey, alpha=0.45, clip=m)


def lace_pattern(p, region, grey=0.95, scale=0.05, seed=1, alpha=0.55):
    """A lace motif (little five-petal flowers and dots on a fine net) drawn as lighter marks: opaque enough to hide a
    T-shirt under it."""
    from PIL import Image, ImageDraw
    rng = np.random.default_rng(seed)
    X, Y = p.grid()
    net = (np.abs(np.sin((X + Y) / scale * 3.1)) < 0.08) | (np.abs(np.sin((X - Y) / scale * 3.1)) < 0.08)
    p.paint(region * net.astype(np.float32), grey=grey, alpha=0.35 * alpha)
    ys, xs = np.nonzero(region > 0.5)
    if len(xs) == 0:
        return
    img = Image.new("L", (p.W, p.H), 0)
    d = ImageDraw.Draw(img)
    count = int(len(xs) / (p.ppt * 4) ** 2 / (scale * scale) * 0.55)
    for _ in range(count):
        k = rng.integers(len(xs))
        cx, cy = float(xs[k]), float(ys[k])
        r = p.units(scale * rng.uniform(0.22, 0.34))
        for j in range(5):
            a = j * 2 * math.pi / 5 + rng.uniform(0, 1)
            px, py = cx + math.cos(a) * r * 0.55, cy + math.sin(a) * r * 0.55
            pts = [(px + math.cos(a) * r * 0.42 * math.cos(t) - math.sin(a) * r * 0.26 * math.sin(t),
                    py + math.sin(a) * r * 0.42 * math.cos(t) + math.cos(a) * r * 0.26 * math.sin(t))
                   for t in np.linspace(0, 2 * math.pi, 14)]
            d.polygon(pts, fill=255)
        d.ellipse([cx - r * 0.22, cy - r * 0.22, cx + r * 0.22, cy + r * 0.22], fill=255)
    m = np.asarray(img, np.float32) / 255 * region
    p.paint(m, grey=grey, alpha=alpha)


def puff_sleeve(s, length=0.34, band=GOLD, grey=BASE, gem_color=None):
    """A round puffed sleeve gathered into a band (arm frame)."""
    half = [(0, -0.07), (0.10, -0.055), (0.165, 0.02), (0.185, 0.12), (0.17, 0.22), (0.13, length - 0.03), (0.085, length),
            (0, length + 0.005)]
    m = s.mask(sym(half))
    fl = st.gather_folds(s, (0, length + 0.02), 13, 0.014, 0.28, seed=11) + st.gather_folds(s, (0, -0.09), 9, 0.008, 0.12, seed=12)
    st.fabric(s, m, st.dome(s, m, 0.12), fl, base=grey, up=0.06, down=0.22, spec=0.28, shininess=18, valley=0.2, line=0.006)
    b = s.mask(catmull([(-0.105, length - 0.03), (0, length - 0.01), (0.105, length - 0.03), (0.10, length + 0.02),
                        (0, length + 0.04), (-0.10, length + 0.02)], True, 6))
    gold(s, b, color=band)
    if gem_color is not None:
        st.gem(s, 0, length + 0.008, 0.016, gem_color)
    return m


def long_sleeve(s, width=0.105, length=1.30, flare=0.0, grey=BASE, spec=0.2, folds=True):
    """A sleeve down the whole arm, a little fuller at the shoulder, soft folds at the elbow."""
    w0, w1, w2 = width * 1.18, width, width * 0.84 + flare
    m = s.mask(catmull([(0, -0.09), (w0, -0.04), (w1 * 1.02, 0.4), (w1, 0.64), (w2, 1.12), (w2 + 0.006, length),
                        (-(w2 + 0.006), length), (-w2, 1.12), (-w1, 0.64), (-w1 * 1.02, 0.4), (-w0, -0.04)], True, 8,
                       sharp=(5, 6)))
    fl = st.hanging_folds(s, -w0, w0, 1.6, 0.006, 0.4, 0.9, seed=21, wobble=0.2) if folds else None
    st.fabric(s, m, st.cylinder(m, depth=0.9), fl, base=grey, spec=spec, shininess=20, valley=0.15, line=0.006)
    return m


def cap_sleeve(s, length=0.30, grey=0.9, alpha=0.95, edge=None):
    """A short sleeve over the shoulder (covers a T-shirt's sleeve)."""
    half = [(0, -0.07), (0.11, -0.05), (0.16, 0.05), (0.155, length - 0.06), (0.14, length), (0, length + 0.015)]
    m = s.mask(sym(half))
    fl = st.hanging_folds(s, -0.16, 0.16, 3, 0.008, 0.0, length, seed=31)
    st.fabric(s, m, st.cylinder(m, depth=0.9), fl, base=grey, spec=0.15, valley=0.1, line=0.005, alpha=alpha)
    if edge is not None:
        edge(s, [(-0.145, length - 0.005), (0, length + 0.012), (0.145, length - 0.005)])
    return m


def flutter_sleeve(s, length=0.34, grey=0.88, alpha=0.93, lobes=0, seed=41, folds=4):
    """A flutter sleeve: narrow where it's sewn on over the shoulder, flaring like a bell to a soft wavy hem that follows
    its folds; the darker lining shows inside the hem. lobes > 0: the hem in petal-shaped points (a fairy's)."""
    rng = np.random.default_rng(seed)
    fl = st.hanging_folds(s, -0.24, 0.24, folds, 0.016, -0.02, length, seed=seed, wobble=0.25, sharp=1.3, grow=0.85)

    def hem(width, y, depth, n=31):
        pts = []
        for k in range(n):
            u = k / (n - 1)
            x = -width + 2 * width * u
            ridge = 0.5 + 0.5 * math.cos(((x + 0.24) / 0.48 * folds) * 2 * math.pi)
            dy = depth * ridge
            if lobes:
                dy += 0.05 * abs(math.sin(u * lobes * math.pi)) ** 0.6
            pts.append((x, y + dy))
        return pts

    # the lining (back of the bell), a little wider and darker
    back = [(-0.11, -0.08), (0.11, -0.08), (0.19, length * 0.55)] + hem(0.215, length - 0.02, 0.03)[::-1] + [(-0.19, length * 0.55)]
    bm = s.mask(catmull(back, True, 4))
    st.fabric(s, bm, st.cylinder(bm, depth=0.5), fl, base=grey - 0.22, up=0.04, down=0.16, line=0.005, alpha=alpha)
    front = [(-0.12, -0.085), (0.12, -0.085), (0.18, length * 0.5)] + hem(0.20, length - 0.035, 0.035)[::-1] + [(-0.18, length * 0.5)]
    fm = s.mask(catmull(front, True, 4))
    form = st.cylinder(fm, depth=0.7)
    st.fabric(s, fm, form, fl, base=grey, up=0.06, down=0.22, spec=0.18, shininess=16, valley=0.18, line=0.0055, alpha=alpha)
    # gathered where it's sewn on
    st.inner_line(s, catmull([(-0.115, -0.06), (0, -0.045), (0.115, -0.06)], False, 6), 0.004, grey=grey - 0.2, alpha=0.6, clip=fm)
    return np.maximum(bm, fm)


# ---------------------------------------------------------------------------------------------------- tops

def top_princess():
    """Princeshë: a satin basque with a pointed waist, a sweetheart neckline piped in gold and strung with pearls, a
    lace yoke up to the neck, a jewel at the heart; puffed sleeves gathered into gold bands."""
    p = piece("top_princess", "torso", TORSO, BODY_PPT, "torso", "top")
    yoke = p.mask(sym(yoke_shape(), sharp=[0, 9]))
    st.fabric(p, yoke, st.cylinder(yoke, depth=0.6), None, base=0.93, up=0.03, down=0.12, line=0.005)
    lace_pattern(p, erode(yoke, p.units(0.01)), grey=0.99, scale=0.055, seed=3)
    neck = catmull([(-0.115, -0.05), (-0.07, 0.01), (0, 0.035), (0.07, 0.01), (0.115, -0.05)], False, 8)
    braid_trim(p, neck, 0.016)
    half = [(0, 0.215), (0.07, 0.13), (0.17, 0.11), (0.29, 0.15), (0.38, 0.24), (0.39, 0.36), (0.35, 0.55), (0.30, 0.74),
            (0.26, 0.84), (0.14, 0.92), (0, 1.0)]
    body = bodice(p, half, depth=0.7, spec=0.3)
    princess_seams(p, body, top=0.15, x0=0.17)
    sweet = catmull([(-0.38, 0.24), (-0.29, 0.15), (-0.17, 0.11), (-0.07, 0.13), (0, 0.215), (0.07, 0.13), (0.17, 0.11),
                     (0.29, 0.15), (0.38, 0.24)], False, 10, sharp=(4,))
    gold_line(p, sweet, 0.02)
    pearls(p, [(x, y + 0.026) for x, y in sweet[4:-4]], 0.0105)
    waist = [(-0.29, 0.76), (-0.20, 0.85), (0, 1.0), (0.20, 0.85), (0.29, 0.76)]
    gold_line(p, catmull(waist, False, 8, sharp=(2,)), 0.022)
    st.gem(p, 0, 0.30, 0.036, hexc("#E8306A"), cut="drop")
    for x, y in ((-0.13, 0.66), (0.13, 0.66), (0, 0.82)):
        st.gem(p, x, y, 0.017, hexc("#6FD3FF"))
    st.glitter(p, body, density=300, size=0.0035, lightness=fabric_light(p, body), flares=0.03, twinkles=4)

    s = piece("top_princess", "sleeve", (-0.22, -0.11, 0.22, 0.42), BODY_PPT, "arm", "sleeve", side="both")
    puff_sleeve(s, gem_color=hexc("#E8306A"))


def petal(p, base, tip, width, grey, vein=True, alpha=1.0, spec=0.18):
    """A soft petal from `base` to `tip`, shaded as a cupped form, lighter toward the tip."""
    dx, dy = tip[0] - base[0], tip[1] - base[1]
    L = math.hypot(dx, dy)
    ux, uy = dx / L, dy / L
    nx, ny = -uy, ux
    prof = [(0.0, 0.16), (0.22, 0.72), (0.52, 1.0), (0.80, 0.80), (0.94, 0.38), (1.0, 0.0)]
    left = [(base[0] + ux * L * t + nx * width * w, base[1] + uy * L * t + ny * width * w) for t, w in prof]
    right = [(base[0] + ux * L * t - nx * width * w, base[1] + uy * L * t - ny * width * w) for t, w in reversed(prof[:-1])]
    m = p.mask(catmull(left + right, True, 8))
    X, Y = p.grid()
    along = np.clip(((X - base[0]) * ux + (Y - base[1]) * uy) / L, 0, 1)
    st.cast_shadow(p, m, dx=0.006, dy=0.012, softness=0.012, amount=0.3)
    form = st.dome(p, m, width * 0.7) + p.units(0.01) * along
    g = st.fabric(p, m, form, None, base=grey, up=0.07, down=0.2, spec=spec, line=0.006, line_grey=0.3, alpha=alpha)
    p.paint(m * along * 0.55, grey=np.clip(g + 0.07, 0, 1), alpha=alpha)
    if vein:
        st.inner_line(p, [(base[0] + ux * L * 0.1, base[1] + uy * L * 0.1), (base[0] + ux * L * 0.8, base[1] + uy * L * 0.8)],
                      width * 0.06, grey=grey - 0.16, alpha=0.4 * alpha, clip=m)
    return m


def top_fairy():
    """Zanë: a bodice of petals cupped like a flower opening, a chiffon yoke dusted with sparkle, a vine belt with
    little blossoms; flutter sleeves of petals."""
    p = piece("top_fairy", "torso", TORSO, BODY_PPT, "torso", "top")
    green, green_deep = hexc("#5CC46E"), hexc("#2F8A48")
    yoke = p.mask(sym(yoke_shape(low=0.42), sharp=[0, 9]))
    st.fabric(p, yoke, st.cylinder(yoke, depth=0.6), None, base=0.94, up=0.03, down=0.12, line=0.005, alpha=0.97)
    st.glitter(p, yoke, density=500, size=0.003, flares=0.02, twinkles=3)
    # the peplum: petals fanning out below the waist
    for k in (0, 6, 1, 5, 2, 4, 3):
        u = (k + 0.5) / 7 * 2 - 1
        petal(p, (u * 0.2, 0.70), (u * 0.44, 1.08 - 0.06 * u * u), 0.10, 0.70 if k % 2 else 0.84, vein=True)
    half = [(0, 0.20), (0.12, 0.16), (0.28, 0.18), (0.37, 0.26), (0.37, 0.45), (0.32, 0.62), (0.29, 0.78), (0, 0.80)]
    body = bodice(p, half, depth=0.7, spec=0.22, base=0.72)
    for u in (-1.0, 1.0, -0.5, 0.5, 0.0):
        petal(p, (u * 0.20, 0.44 - 0.03 * abs(u)), (u * 0.40, 0.10 + 0.05 * abs(u)), 0.115, 0.92 if u == 0 else 0.84 - 0.06 * abs(u))
    belt = catmull([(-0.31, 0.73), (0, 0.765), (0.31, 0.73)], False, 8)
    vine = p.line_mask(belt, 0.03)
    st.solid(p, vine, green, form=st.dome(p, vine, 0.015), line=0.003)
    for k, (x, y) in enumerate(resample(belt, 0.075)):
        leaf(p, x, y, x + (0.05 if k % 2 else -0.05), y + 0.035, 0.02, green)
    flower(p, 0, 0.28, 0.07, petal=WHITE, center=GOLD)
    for x in (-0.16, 0.16):
        flower(p, x, 0.75, 0.042, petal=hexc("#FFC2E2"), center=GOLD, rot=0.5)
    st.glitter(p, body, density=600, size=0.0032, lightness=fabric_light(p, body), flares=0.04, twinkles=6)

    s = piece("top_fairy", "sleeve", (-0.25, -0.11, 0.25, 0.46), BODY_PPT, "arm", "sleeve", side="both")
    m = flutter_sleeve(s, length=0.34, grey=0.86, lobes=3, seed=43)
    st.glitter(s, m, density=500, size=0.003, flares=0.02, twinkles=2)


def shell(p, cx, cy, r, color, ribs=9):
    """A scallop shell: ribs fanning from the hinge, shaded, a pearly sheen."""
    fan = [(cx, cy + r * 0.78)]
    for k in range(ribs * 2 + 1):
        a = math.pi * (0.08 + 0.84 * k / (ribs * 2))
        rr = r if k % 2 == 0 else r * 0.93
        fan.append((cx - math.cos(a) * rr, cy + r * 0.66 - math.sin(a) * rr))
    m = p.mask(catmull(fan, True, 4, sharp=(0,)))
    X, Y = p.grid()
    ang = np.arctan2(-(Y - (cy + r * 0.66)), -(X - cx))
    ribs_h = st.Folds(p.units(r * 0.08) * (0.5 + 0.5 * np.cos(ang * ribs * 2)), p.units(r * 0.08))
    st.solid(p, m, color, form=st.dome(p, m, r * 0.5), folds=ribs_h, spec=0.45, shininess=22, valley=0.15, line=0.004)
    hinge = p.mask(ellipse_pts(cx, cy + r * 0.74, r * 0.26, r * 0.13, 20))
    st.solid(p, hinge, color, form=st.dome(p, hinge, r * 0.1), line=0.003)
    return m


def top_mermaid():
    """Sirenë: a bodice of iridescent sequin scales, a sheer sea-foam yoke strewn with seed pearls, two pearly scallop
    shells, pearl straps and a pearl belt with a starfish; sheer cap sleeves."""
    p = piece("top_mermaid", "torso", TORSO, BODY_PPT, "torso", "top")
    yoke = p.mask(sym(yoke_shape(low=0.40), sharp=[0, 9]))
    st.fabric(p, yoke, st.cylinder(yoke, depth=0.6), None, base=0.94, up=0.03, down=0.12, line=0.005, alpha=0.97)
    X, Y = p.grid()
    rng = np.random.default_rng(5)
    for _ in range(26):
        x, y = rng.uniform(-0.42, 0.42), rng.uniform(-0.02, 0.34)
        st.pearl(p, x, y, 0.006)
    half = [(0, 0.17), (0.12, 0.10), (0.26, 0.12), (0.37, 0.20), (0.39, 0.32), (0.36, 0.55), (0.30, 0.77), (0.2, 0.88),
            (0, 0.93)]
    body = p.mask(sym(half, sharp=[0, len(half) - 1]))
    st.fabric(p, body, st.cylinder(body, depth=0.7), None, base=0.78, spec=0.2, line=0.0065)
    st.sequin_rows(p, body, size=0.032, lightness=fabric_light(p, body) * 0.6 + 0.4, seed=31)
    st.glitter(p, body, density=500, size=0.003, lightness=fabric_light(p, body), flares=0.05, twinkles=6)
    st.outline(p, body, 0.0065)
    for side in (-1, 1):
        shell(p, 0.15 * side, 0.20, 0.155, hexc("#FFD9E4"))
        pearls(p, catmull([(0.27 * side, 0.18), (0.25 * side, 0.07), (0.19 * side, -0.02), (0.11 * side, -0.05)], False, 8), 0.014)
    pearls(p, catmull([(-0.30, 0.79), (-0.15, 0.87), (0, 0.92), (0.15, 0.87), (0.30, 0.79)], False, 8), 0.017)
    star = star_pts(-0.21, 0.80, 0.06, 0.5, 5, rot=-math.pi / 2 + 0.3, round_=1)
    sm = p.mask(star)
    st.solid(p, sm, hexc("#FF8A5B"), form=st.dome(p, sm, 0.03), spec=0.3, line=0.004)
    for k in range(5):
        a = -math.pi / 2 + 0.3 + 2 * math.pi * k / 5
        for t in (0.35, 0.62):
            st.pearl(p, -0.21 + math.cos(a) * 0.06 * t, 0.80 + math.sin(a) * 0.06 * t, 0.0055, color=hexc("#FFE6D2"))

    s = piece("top_mermaid", "sleeve", (-0.20, -0.10, 0.20, 0.40), BODY_PPT, "arm", "sleeve", side="both")
    m = cap_sleeve(s, length=0.30, grey=0.92, alpha=0.95, edge=lambda s_, pts: pearls(s_, pts, 0.012))
    st.glitter(s, m, density=400, size=0.003, flares=0.02, twinkles=2)


def emblem(p, cx, cy, r):
    """A hero's emblem: a gold-rimmed shield with a white star."""
    shield = catmull([(cx, cy - r), (cx + r * 0.95, cy - r * 0.78), (cx + r * 0.9, cy + r * 0.1), (cx + r * 0.45, cy + r * 0.72),
                      (cx, cy + r), (cx - r * 0.45, cy + r * 0.72), (cx - r * 0.9, cy + r * 0.1), (cx - r * 0.95, cy - r * 0.78)],
                     True, 8, sharp=(0, 4))
    rim = p.mask(shield)
    gold(p, rim)
    inner = erode(rim, p.units(r * 0.13))
    st.fabric(p, inner, st.dome(p, inner, r * 0.4), None, base=0.62, up=0.06, down=0.18, spec=0.4, line=0.0)
    sm = p.mask(star_pts(cx, cy + r * 0.02, r * 0.58, 0.45, 5))
    st.solid(p, sm, WHITE, form=st.dome(p, sm, r * 0.18), spec=0.5, line=0.003, line_color=hexc("#9AA0C0"))
    p.meta.setdefault("sparkles", []).append([round(cx - r * 0.3, 4), round(cy - r * 0.35, 4), round(r * 0.9, 4)])


def top_hero():
    """Superheroinë: a glossy high-necked suit with darker side panels and a gold V, a shield emblem, a utility belt
    with a star buckle; long sleeves with gauntlets and gold cuffs."""
    p = piece("top_hero", "torso", TORSO, BODY_PPT, "torso", "top")
    half = [(0, -0.035), (0.08, -0.04), (0.12, -0.06), (0.30, -0.04), (0.44, 0.03), (0.47, 0.15), (0.41, 0.30),
            (0.38, 0.52), (0.32, 0.78), (0.35, 0.92), (0.36, 1.0), (0, 1.0)]
    body = bodice(p, half, depth=0.75, spec=0.45, shininess=30, seams=False)
    for side in (-1, 1):
        panel = p.mask(catmull([(0.33 * side, 0.30), (0.42 * side, 0.30), (0.38 * side, 0.52), (0.32 * side, 0.78),
                                (0.36 * side, 0.96), (0.23 * side, 0.96), (0.26 * side, 0.6)], True, 6)) * body
        st.darken(p, blur(panel, p.units(0.004)), 0.32)
        st.inner_line(p, catmull([(0.33 * side, 0.30), (0.26 * side, 0.6), (0.23 * side, 0.96)], False, 8), 0.0035,
                      grey=0.45, alpha=0.6, clip=body)
    vline = catmull([(-0.20, -0.03), (-0.1, 0.18), (0, 0.42), (0.1, 0.18), (0.20, -0.03)], False, 8, sharp=(2,))
    gold_line(p, vline, 0.014)
    collar = p.mask(catmull([(-0.13, -0.065), (0, -0.04), (0.13, -0.065), (0.13, -0.025), (0, 0.0), (-0.13, -0.025)], True, 6))
    gold(p, collar)
    emblem(p, 0, 0.30, 0.13)
    belt_pts = catmull([(-0.33, 0.80), (0, 0.835), (0.33, 0.80)], False, 8)
    belt = p.line_mask(belt_pts, 0.07)
    gold(p, belt, color=hexc("#F4B63A"))
    st.cast_shadow(p, belt, receiver=body, dy=0.012, softness=0.01, amount=0.3)
    buckle = p.mask(catmull([(-0.065, 0.78), (0.065, 0.78), (0.07, 0.89), (-0.07, 0.89)], True, 2, sharp=(0, 1, 2, 3)))
    gold(p, buckle, color=hexc("#FFE38A"))
    sm = p.mask(star_pts(0, 0.835, 0.035, 0.45, 5))
    st.solid(p, sm, hexc("#E8304A"), form=st.dome(p, sm, 0.01), line=0.002)
    for x in (-0.22, 0.22):
        pouch = p.mask(catmull([(x - 0.045, 0.785), (x + 0.045, 0.785), (x + 0.045, 0.875), (x - 0.045, 0.875)], True, 3))
        gold(p, pouch, color=hexc("#E2A532"))

    s = piece("top_hero", "sleeve", (-0.16, -0.11, 0.16, 1.34), BODY_PPT, "arm", "sleeve", side="both")
    sm = long_sleeve(s, width=0.105, spec=0.45)
    glove = s.mask(catmull([(-0.12, 0.84), (0.12, 0.84), (0.10, 1.32), (-0.10, 1.32)], True, 4)) * sm
    st.darken(s, glove, 0.35)
    cuff = s.mask(catmull([(-0.125, 0.83), (0, 0.80), (0.125, 0.83), (0.12, 0.89), (0, 0.86), (-0.12, 0.89)], True, 6, sharp=(0, 2, 3, 5)))
    gold(s, cuff)


def quilt(p, region, rows, base=BASE, spec=0.3):
    """A quilted puffer: soft horizontal baffles (rows = (top, bottom, count)), each puffed between its stitched seams."""
    X, Y = p.grid()
    ys = np.linspace(rows[0], rows[1], rows[2] + 1)
    period = (rows[1] - rows[0]) / rows[2]
    t = ((Y - rows[0]) / period) % 1.0
    h = np.sin(np.clip(t, 0, 1) * math.pi) ** 0.6 * p.units(period * 0.35)
    form = st.cylinder(region, depth=0.7) + h * region
    g = st.fabric(p, region, form, None, base=base, up=0.06, down=0.24, spec=spec, shininess=16, line=0.0065)
    for y in ys[1:-1]:
        st.inner_line(p, [(-0.6, y), (0.6, y)], 0.004, grey=0.5, alpha=0.6, clip=region)
    return g


def fur(p, pts, r, color=hexc("#FBF8FF"), seed=7, strands=True):
    """Fluffy faux fur along a line: tufts with soft shading and fine hair strokes at the edges (drawn in one pass)."""
    from PIL import Image, ImageDraw
    rng = np.random.default_rng(seed)
    pts = resample(catmull(pts, False, 6), r * 0.5)
    img = Image.new("L", (p.W, p.H), 0)
    d = ImageDraw.Draw(img)
    for x, y in pts:
        cx, cy = p.px(x + rng.uniform(-0.2, 0.2) * r, y + rng.uniform(-0.25, 0.25) * r)
        rr = p.units(r * rng.uniform(0.7, 1.05))
        d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=255)
        if strands:
            for _ in range(5):
                a = rng.uniform(0, 2 * math.pi)
                L = r * rng.uniform(0.9, 1.35)
                q0 = p.px(x + math.cos(a) * r * 0.5, y + math.sin(a) * r * 0.5)
                q1 = p.px(x + math.cos(a) * L, y + math.sin(a) * L)
                d.line([q0, q1], fill=255, width=max(1, int(p.units(r * 0.12))))
    m = blur(np.asarray(img, np.float32) / 255, p.units(0.0015))
    st.solid(p, m, color, form=st.dome(p, m, r * 0.7), up=0.05, down=0.25, line=0.004, line_color=hexc("#A49CBE"))
    for x, y in pts:
        a = rng.uniform(-0.8, 0.8)
        st.inner_line(p, [(x, y - r * 0.2), (x + math.sin(a) * r * 0.5, y + r * 0.45)], r * 0.06,
                      color=hexc("#CFC7E4"), alpha=0.6, clip=m)
    return m


def top_winter():
    """Dimër: a quilted puffer coat with a silver zip, a white faux-fur collar, pockets; quilted sleeves with fur cuffs."""
    p = piece("top_winter", "torso", (-0.60, -0.20, 0.60, 1.18), BODY_PPT, "torso", "top")
    half = [(0, 0.0), (0.12, -0.06), (0.31, -0.03), (0.47, 0.05), (0.50, 0.16), (0.44, 0.32), (0.41, 0.55),
            (0.38, 0.78), (0.43, 0.98), (0.47, 1.12), (0, 1.13)]
    body = p.mask(sym(half, sharp=[0, len(half) - 1]))
    quilt(p, body, (0.0, 1.13, 7))
    zip_ = p.line_mask([(0, 0.02), (0, 1.12)], 0.016)
    gold(p, zip_, color=SILVER)
    for y in np.arange(0.05, 1.1, 0.03):
        st.inner_line(p, [(-0.012, y), (0.012, y)], 0.004, color=hexc("#8A90A8"), alpha=0.6, clip=zip_)
    pull = p.mask(catmull([(-0.016, 0.20), (0.016, 0.20), (0.012, 0.29), (-0.012, 0.29)], True, 3))
    gold(p, pull, color=SILVER)
    for side in (-1, 1):
        pocket = catmull([(0.14 * side, 0.68), (0.30 * side, 0.70), (0.33 * side, 0.82)], False, 6)
        st.inner_line(p, pocket, 0.008, grey=0.42, alpha=0.8, clip=body)
    collar = catmull([(-0.44, 0.10), (-0.32, -0.01), (-0.15, -0.05), (0, 0.02), (0.15, -0.05), (0.32, -0.01), (0.44, 0.10)], False, 10)
    fur(p, collar, 0.07)
    st.cast_shadow(p, p.line_mask(collar, 0.12), receiver=body, dy=0.02, softness=0.02, amount=0.35)
    p.meta["sparkles"] = []

    s = piece("top_winter", "sleeve", (-0.23, -0.11, 0.23, 1.38), BODY_PPT, "arm", "sleeve", side="both")
    sm = s.mask(catmull([(0, -0.09), (0.15, -0.04), (0.135, 0.64), (0.125, 1.12), (0.13, 1.24), (-0.13, 1.24), (-0.125, 1.12),
                         (-0.135, 0.64), (-0.15, -0.04)], True, 8, sharp=(4, 5)))
    quilt(s, sm, (-0.05, 1.24, 6))
    fur(s, [(-0.15, 1.20), (0, 1.23), (0.15, 1.20)], 0.06, seed=9)


def top_party():
    """Festë: an off-the-shoulder sequin top that sparkles, over a sheer chiffon yoke, a big satin bow at the waist;
    ruffled sheer sleeves."""
    p = piece("top_party", "torso", TORSO, BODY_PPT, "torso", "top")
    yoke = p.mask(sym(yoke_shape(low=0.36), sharp=[0, 9]))
    st.fabric(p, yoke, st.cylinder(yoke, depth=0.6), None, base=0.94, up=0.03, down=0.12, line=0.005, alpha=0.96)
    st.glitter(p, yoke, density=400, size=0.003, flares=0.02, twinkles=2)
    half = [(0, 0.10), (0.20, 0.09), (0.40, 0.13), (0.41, 0.30), (0.37, 0.55), (0.31, 0.78), (0.33, 0.92), (0, 0.95)]
    body = p.mask(sym(half, sharp=[0, len(half) - 1]))
    st.fabric(p, body, st.cylinder(body, depth=0.75), None, base=0.74, spec=0.2, line=0.0065)
    st.sequin_rows(p, body, size=0.028, lightness=fabric_light(p, body) * 0.5 + 0.5, seed=41)
    st.glitter(p, body, density=700, size=0.0032, lightness=fabric_light(p, body), flares=0.06, twinkles=8)
    st.outline(p, body, 0.0065)
    top_edge = [(-0.50, 0.10), (-0.36, 0.04), (-0.18, 0.05), (0, 0.07), (0.18, 0.05), (0.36, 0.04), (0.50, 0.10)]
    low = []
    for k in range(41):
        u = k / 40
        x = -0.50 + u
        base_y = 0.17 + 0.05 * (abs(x) / 0.5) ** 2
        low.append((x, base_y + 0.025 * (0.5 + 0.5 * math.cos(u * 18 * 2 * math.pi))))
    frill = p.mask(catmull(top_edge + low[::-1], True, 4))
    fl = st.hanging_folds(p, -0.5, 0.5, 18, 0.014, 0.04, 0.2, seed=43, sharp=1.2, grow=0.6)
    st.fabric(p, frill, st.cylinder(frill, depth=0.4), fl, base=0.92, spec=0.25, valley=0.22, line=0.0055, line_grey=0.32)
    st.cast_shadow(p, frill, receiver=body, dy=0.02, softness=0.015, amount=0.4)
    bow(p, 0.0, 0.80, 0.15, grey=0.95)

    s = piece("top_party", "sleeve", (-0.25, -0.11, 0.25, 0.44), BODY_PPT, "arm", "sleeve", side="both")
    m = flutter_sleeve(s, length=0.32, grey=0.92, alpha=0.93, seed=47)
    st.glitter(s, m, density=500, size=0.003, flares=0.03, twinkles=2)


ALL = [top_princess, top_fairy, top_mermaid, top_hero, top_winter, top_party]
