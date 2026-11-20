"""Things held in the hand (hand rig: its own frame, the grip at (0, 0), the item up (-y); mode "along" (with the
forearm), "upright", "hang" (swings below the hand) or "float" (a balloon on a string to `knot`))."""
import math

import numpy as np

from draw import BASE, blur, catmull, dilate, ellipse_pts, erode, heart_pts, hexc, star_pts, sym
import style as st
from wardrobe import HAND_PPT, GOLD, WHITE, bow, flower, gold, leaf, piece
from heads import rose


def hand_wand():
    """Shkop magjik: a twisted pearly stick, a big glittering star in the chosen colour with a gem, satin ribbons."""
    p = piece("hand_wand", "wand", (-0.17, -0.72, 0.17, 0.11), HAND_PPT, "hand", "hand", mode="along", tip=[0, -0.56])
    stick = p.mask([(-0.016, 0.08), (0.016, 0.08), (0.013, -0.45), (-0.013, -0.45)])
    X, Y = p.grid()
    twist = st.Folds(p.units(0.004) * (0.5 + 0.5 * np.cos((Y + X * 0.5) * 120)), p.units(0.004))
    st.solid(p, stick, hexc("#FFF6FB"), form=st.cylinder(stick, depth=1.0), folds=twist, spec=0.6, valley=0.3, line=0.004,
             line_color=hexc("#B9A4C8"))
    for side in (-1, 1):
        rib = p.line_mask(catmull([(0, -0.43), (0.06 * side, -0.36), (0.03 * side, -0.27), (0.08 * side, -0.19)], False, 8), 0.02)
        st.fabric(p, rib, st.dome(p, rib, 0.008), None, base=0.86, up=0.08, down=0.22, spec=0.4, line=0.003)
    star = p.mask(star_pts(0, -0.55, 0.145, 0.5, 5, round_=1))
    st.fabric(p, star, st.dome(p, star, 0.05), None, base=BASE, up=0.09, down=0.26, spec=0.55, shininess=20, line=0.0055,
              line_grey=0.26)
    st.glitter(p, star, density=2500, size=0.003, flares=0.06, twinkles=4)
    st.gem(p, 0, -0.55, 0.026, hexc("#FFFFFF"))


def hand_bag():
    """Çantë: a quilted little handbag in the chosen colour on a gold chain, a gold clasp, a bow."""
    p = piece("hand_bag", "bag", (-0.23, -0.07, 0.23, 0.47), HAND_PPT, "hand", "hand", mode="hang")
    chain = catmull([(-0.09, 0.17), (-0.075, 0.02), (0, -0.025), (0.075, 0.02), (0.09, 0.17)], False, 12)
    from wardrobe import braid_trim
    braid_trim(p, chain, 0.016)
    body = p.mask(catmull([(-0.15, 0.16), (0.15, 0.16), (0.19, 0.38), (0.12, 0.43), (-0.12, 0.43), (-0.19, 0.38)], True, 8,
                          sharp=(0, 1)))
    X, Y = p.grid()
    quilt = st.Folds(p.units(0.012) * np.abs(np.sin((X + Y) * 34) * np.sin((X - Y) * 34)) ** 0.6, p.units(0.012))
    st.fabric(p, body, st.dome(p, body, 0.05), quilt, base=BASE, up=0.07, down=0.24, spec=0.35, shininess=16, valley=0.35,
              line=0.006, line_grey=0.26)
    flap = p.mask(catmull([(-0.15, 0.16), (0.15, 0.16), (0.12, 0.27), (0, 0.31), (-0.12, 0.27)], True, 8, sharp=(0, 1, 3)))
    st.cast_shadow(p, flap, receiver=body, dy=0.012, softness=0.01, amount=0.35)
    st.fabric(p, flap, st.dome(p, flap, 0.03), None, base=0.72, up=0.08, down=0.24, spec=0.4, line=0.005, line_grey=0.26)
    clasp = p.mask(ellipse_pts(0, 0.29, 0.03, 0.025, 24))
    gold(p, clasp)
    bow(p, 0.10, 0.19, 0.045, color=WHITE)
    p.meta.setdefault("sparkles", []).append([0.0, 0.28, 0.04])


def hand_bouquet():
    """Buqetë: roses in the chosen colour with white blossoms and leaves in a paper cone, a satin ribbon."""
    p = piece("hand_bouquet", "bouquet", (-0.25, -0.45, 0.25, 0.15), HAND_PPT, "hand", "hand", mode="upright")
    green, green_deep = hexc("#4CB860"), hexc("#2E8B47")
    for k, (a, l) in enumerate(((-0.9, 0.25), (0.9, 0.25), (-0.4, 0.31), (0.45, 0.31))):
        leaf(p, 0, -0.12, math.sin(a) * l, -0.12 - math.cos(a) * l, 0.042, color=green if k % 2 else green_deep)
    for k, (x, y, r) in enumerate(((-0.10, -0.20, 0.072), (0.10, -0.21, 0.07), (0.0, -0.30, 0.08))):
        rose(p, x, y, r, grey=0.82, rot=k, seed=k)
    for k, (x, y, r) in enumerate(((-0.06, -0.12, 0.05), (0.07, -0.11, 0.05), (-0.16, -0.27, 0.04), (0.16, -0.28, 0.04))):
        flower(p, x, y, r, petal=WHITE, center=GOLD, petals=6, rot=k)
    cone = p.mask(catmull([(-0.15, -0.12), (0.15, -0.12), (0.02, 0.13), (-0.02, 0.13)], True, 6, sharp=(0, 1, 2, 3)))
    folds = st.fan_folds(p, cone, (0, 0.2), 4, 0.01, -0.12, 0.13, seed=3)
    st.solid(p, cone, hexc("#FFF4FA"), form=st.cylinder(cone, depth=0.6), folds=folds, valley=0.2, line=0.005,
             line_color=hexc("#C9B4D0"))
    st.inner_line(p, [(-0.15, -0.12), (0.0, -0.07), (0.15, -0.12)], 0.01, color=WHITE, alpha=0.9)
    bow(p, 0, 0.04, 0.055, grey=0.82)


def hand_mic():
    """Mikrofon: a pop star's microphone, a sequin handle in the chosen colour, a silver grille that catches the light."""
    p = piece("hand_mic", "mic", (-0.13, -0.43, 0.13, 0.13), HAND_PPT, "hand", "hand", mode="along")
    body = p.mask([(-0.035, 0.11), (0.035, 0.11), (0.05, -0.17), (-0.05, -0.17)])
    st.fabric(p, body, st.cylinder(body, depth=1.0), None, base=0.72, spec=0.4, line=0.0045, line_grey=0.26)
    st.sequin_rows(p, body, size=0.016, seed=7)
    ring = p.line_mask([(-0.048, -0.12), (0.048, -0.12)], 0.022)
    gold(p, ring)
    head = p.mask(ellipse_pts(0, -0.27, 0.092, 0.097, 48))
    X, Y = p.grid()
    mesh = st.Folds(p.units(0.004) * (np.abs(np.sin((X + Y) * 140)) * np.abs(np.sin((X - Y) * 140))) ** 0.5, p.units(0.004))
    st.solid(p, head, hexc("#D3D8E4"), form=st.dome(p, head, 0.08), folds=mesh, spec=0.6, shininess=20, valley=0.4,
             line=0.0045, line_color=hexc("#7A8094"))
    p.paint(blur(p.mask(ellipse_pts(-0.03, -0.31, 0.024, 0.018, 16)), p.units(0.004)), color=WHITE, alpha=0.85)
    p.meta.setdefault("sparkles", []).append([-0.03, -0.3, 0.05])


def hand_icecream():
    """Akullore: a waffle cone, a vanilla scoop, a scoop in the chosen colour with sprinkles, a cherry on top."""
    p = piece("hand_icecream", "icecream", (-0.15, -0.51, 0.15, 0.15), HAND_PPT, "hand", "hand", mode="upright")
    cone = p.mask([(-0.085, -0.12), (0.085, -0.12), (0.0, 0.125)])
    X, Y = p.grid()
    waffle = st.Folds(p.units(0.006) * (np.abs(np.sin((X + Y) * 90)) * np.abs(np.sin((X - Y) * 90))) ** 0.4, p.units(0.006))
    st.solid(p, cone, hexc("#E9B36A"), form=st.cylinder(cone, depth=0.8), folds=waffle, valley=0.4, line=0.005)
    vanilla = p.mask(catmull([(-0.10, -0.13), (-0.07, -0.20), (0.0, -0.22), (0.07, -0.20), (0.10, -0.13), (0.06, -0.10),
                              (0.02, -0.115), (-0.03, -0.10), (-0.07, -0.115)], True, 8))
    st.solid(p, vanilla, hexc("#FFF2D9"), form=st.dome(p, vanilla, 0.05), spec=0.25, line=0.005)
    top = p.mask(catmull([(-0.095, -0.24), (-0.075, -0.33), (0.0, -0.36), (0.075, -0.33), (0.095, -0.24), (0.05, -0.205),
                          (0.0, -0.225), (-0.05, -0.205)], True, 8))
    st.fabric(p, top, st.dome(p, top, 0.06), None, base=BASE, up=0.08, down=0.24, spec=0.3, line=0.005, line_grey=0.28)
    rng = np.random.default_rng(4)
    for k in range(16):
        x, y = rng.uniform(-0.07, 0.07), rng.uniform(-0.33, -0.22)
        a = rng.uniform(0, math.pi)
        c = [hexc("#FFFFFF"), hexc("#FFD23F"), hexc("#6EC3FF"), hexc("#6FD66A")][k % 4]
        sm = p.line_mask([(x, y), (x + math.cos(a) * 0.018, y + math.sin(a) * 0.018)], 0.008) * top
        st.solid(p, sm, c, form=st.dome(p, sm, 0.004), line=0.0)
    cherry = p.mask(ellipse_pts(0.0, -0.375, 0.029, 0.029, 24))
    st.solid(p, cherry, hexc("#E8304A"), form=st.dome(p, cherry, 0.03), spec=0.7, shininess=30, line=0.004)
    st.inner_line(p, catmull([(0.0, -0.40), (0.01, -0.44), (0.03, -0.46)], False, 6), 0.006, color=hexc("#3D8B3D"), alpha=1.0)


def hand_balloon():
    """Tullumbace: a glossy heart balloon in the chosen colour (the game draws the string from the hand to the knot)."""
    p = piece("hand_balloon", "balloon", (-0.25, -0.27, 0.25, 0.28), HAND_PPT, "hand", "hand", mode="float", knot=[0, 0.24])
    m = p.mask(heart_pts(0, -0.02, 0.21))
    st.fabric(p, m, st.dome(p, m, 0.12), None, base=BASE, up=0.10, down=0.28, soft=1.8, spec=0.75, shininess=36,
              line=0.006, line_grey=0.26, alpha=0.97)
    p.paint(blur(p.mask(ellipse_pts(-0.085, -0.10, 0.045, 0.026, 24)), p.units(0.006)), color=WHITE, alpha=0.85)
    knot = p.mask([(-0.02, 0.215), (0.02, 0.215), (0.0, 0.25)])
    st.fabric(p, knot, st.dome(p, knot, 0.01), None, base=0.6, line=0.003)


ALL = [hand_wand, hand_bag, hand_bouquet, hand_mic, hand_icecream, hand_balloon]
