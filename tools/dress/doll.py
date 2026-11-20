"""The paper doll: shown when nobody is in front of the camera (and under the clothes while the camera picture is
missing). A fashion doll like the dress-up games the user pointed to (2026-11-09): soft shaded skin, a pretty face
with big lashed eyes, a pastel bodysuit and ballet flats; her own sleek bob ("dollhair") while no hair is chosen.
Rigs: body = torso, arm = arm frame (shoulder 0, elbow 0.64, wrist 1.21), leg = leg frame (hip 0, knee 0.92,
ankle 1.85), head = head frame (eyes at (+-0.064, -0.47))."""
import math

import numpy as np

from draw import blur, catmull, ellipse_pts, erode, hexc, rotate, star_pts, sym
import style as st
from wardrobe import BODY_PPT, HEAD_PPT, WHITE, erase, face_hole, piece

SKIN = hexc("#F6D2BD")
SKIN_LINE = hexc("#C98E7A")
SUIT = hexc("#CDB9FF")
SHOE = hexc("#FF78B9")


def skin(p, m, form=None, line=0.005):
    return st.solid(p, m, SKIN, form=st.dome(p, m, 0.05) if form is None else form, up=0.05, down=0.18, line=line,
                    line_color=SKIN_LINE)


def doll():
    body = piece("doll", "body", (-0.50, -0.13, 0.50, 1.23), BODY_PPT, "torso", "doll")
    torso = body.mask(sym([(0, -0.10), (0.09, -0.09), (0.13, -0.04), (0.30, 0.0), (0.42, 0.07), (0.45, 0.18), (0.38, 0.33),
                           (0.33, 0.55), (0.29, 0.76), (0.36, 0.96), (0.40, 1.10), (0, 1.18)]))
    skin(body, torso, st.cylinder(torso, depth=0.7))
    suit_m = body.mask(sym([(0, 0.13), (0.10, 0.06), (0.24, 0.05), (0.40, 0.11), (0.43, 0.20), (0.37, 0.33), (0.33, 0.55),
                            (0.29, 0.76), (0.36, 0.96), (0.30, 1.10), (0.12, 1.16), (0, 1.17)], sharp=[0, 11]))
    fl = st.hanging_folds(body, -0.3, 0.3, 2.3, 0.008, 0.2, 1.1, seed=3, wobble=0.0, sharp=3, grow=0.0)
    st.solid(body, suit_m, SUIT, form=st.cylinder(suit_m, depth=0.75), folds=fl, spec=0.25, shininess=18, line=0.0055,
             line_color=hexc("#8E74D9"))
    neckline = catmull([(-0.24, 0.05), (-0.10, 0.06), (0, 0.13), (0.10, 0.06), (0.24, 0.05)], False, 8)
    st.inner_line(body, neckline, 0.008, color=WHITE, alpha=0.8)
    st.glitter(body, suit_m, density=400, size=0.003, tinted=False, flares=0.03, twinkles=0)
    collar = [(-0.10, 0.065), (0.10, 0.065)]
    st.inner_line(body, [(-0.06, -0.02), (0, 0.0), (0.06, -0.02)], 0.004, color=SKIN_LINE, alpha=0.35)

    arm = piece("doll", "arm", (-0.12, -0.09, 0.12, 1.47), BODY_PPT, "arm", "doll", side="both")
    am = arm.mask(catmull([(0, -0.07), (0.09, -0.01), (0.075, 0.36), (0.07, 0.64), (0.055, 1.0), (0.05, 1.17), (0.07, 1.25),
                           (0.068, 1.36), (0.035, 1.43), (-0.035, 1.43), (-0.07, 1.36), (-0.07, 1.25), (-0.05, 1.17),
                           (-0.055, 1.0), (-0.07, 0.64), (-0.075, 0.36), (-0.09, -0.01)], True, 8))
    skin(arm, am, st.cylinder(am, depth=1.0))
    thumb = arm.mask(catmull([(-0.06, 1.22), (-0.09, 1.28), (-0.08, 1.33), (-0.05, 1.30)], True, 6))
    skin(arm, thumb, line=0.004)
    for y in (1.37, 1.40):
        st.inner_line(arm, [(-0.03, y - 0.04), (-0.025, y)], 0.003, color=SKIN_LINE, alpha=0.4)
    st.inner_line(arm, [(-0.03, 0.63), (0.0, 0.645), (0.03, 0.63)], 0.003, color=SKIN_LINE, alpha=0.3)

    leg = piece("doll", "leg", (-0.17, -0.13, 0.17, 2.03), BODY_PPT, "leg", "doll", side="both")
    lm = leg.mask(catmull([(0, -0.10), (0.14, -0.05), (0.12, 0.45), (0.09, 0.92), (0.085, 1.30), (0.06, 1.72), (-0.06, 1.72),
                           (-0.085, 1.30), (-0.09, 0.92), (-0.12, 0.45), (-0.14, -0.05)], True, 8))
    skin(leg, lm, st.cylinder(lm, depth=1.0))
    knee = blur(leg.mask(ellipse_pts(0.0, 0.92, 0.05, 0.035, 24)), leg.units(0.02)) * lm
    st.darken(leg, knee, 0.12)
    shoe = leg.mask(catmull([(-0.068, 1.74), (0.068, 1.74), (0.085, 1.84), (0.05, 1.95), (0.0, 1.98), (-0.05, 1.95),
                             (-0.085, 1.84)], True, 8))
    st.solid(leg, shoe, SHOE, form=st.dome(leg, shoe, 0.04), spec=0.55, shininess=24, line=0.005)
    bowm = leg.mask(catmull([(-0.03, 1.775), (0.0, 1.79), (0.03, 1.775), (0.03, 1.805), (0, 1.80), (-0.03, 1.805)], True, 6))
    st.solid(leg, bowm, hexc("#FFE0F0"), form=st.dome(leg, bowm, 0.01), line=0.003)

    head = piece("doll", "head", (-0.25, -0.77, 0.25, -0.05), HEAD_PPT, "head", "doll")
    neck = head.mask(catmull([(-0.058, -0.32), (-0.055, -0.08), (0.055, -0.08), (0.058, -0.32)], True, 4))
    skin(head, neck, st.cylinder(neck, depth=1.0))
    for side in (-1, 1):
        ear = head.mask(ellipse_pts(0.162 * side, -0.45, 0.034, 0.048, 24))
        skin(head, ear)
    face = head.mask(catmull([(0, -0.665), (0.12, -0.64), (0.16, -0.55), (0.158, -0.45), (0.14, -0.36), (0.095, -0.29),
                              (0.0, -0.255), (-0.095, -0.29), (-0.14, -0.36), (-0.158, -0.45), (-0.16, -0.55), (-0.12, -0.64)],
                             True, 10))
    st.cast_shadow(head, face, receiver=neck, dy=0.03, softness=0.02, amount=0.35)
    skin(head, face, st.dome(head, face, 0.10))
    for side in (-1, 1):
        cheek = blur(head.mask(ellipse_pts(0.095 * side, -0.385, 0.04, 0.024, 24)), head.units(0.012))
        head.paint(cheek, color=hexc("#FF8FAF"), alpha=0.45)
    for side in (-1, 1):
        eye(head, 0.064 * side, -0.47, side)
        brow = catmull([(0.03 * side, -0.525), (0.065 * side, -0.538), (0.10 * side, -0.528)], False, 6)
        st.inner_line(head, brow, 0.009, color=hexc("#6B4330"), alpha=0.9)
    st.inner_line(head, catmull([(0.006, -0.445), (0.012, -0.405), (0.0, -0.395)], False, 6), 0.005, color=SKIN_LINE, alpha=0.45)
    head.paint(blur(head.mask(ellipse_pts(-0.006, -0.43, 0.006, 0.014, 12)), head.units(0.003)), color=WHITE, alpha=0.5)
    lips(head, 0, -0.352)


def eye(p, x, y, side):
    """A fashion-doll eye: an almond white, a big iris (light below, dark above), a pupil, two shines, thick upper
    lashes flicking out, a lid crease, a hint of lower lashes."""
    w, h = 0.043, 0.031
    almond = catmull([(x - w * side, y + 0.002), (x - w * 0.3 * side, y - h), (x + w * 0.55 * side, y - h * 0.95),
                      (x + w * side, y - 0.004), (x + w * 0.4 * side, y + h * 0.75), (x - w * 0.4 * side, y + h * 0.7)],
                     True, 10, sharp=(0, 3))
    white = p.mask(almond)
    p.paint(white, color=hexc("#FFFDFE"))
    p.paint(blur(p.mask(ellipse_pts(x, y - h * 0.6, w, h * 0.6, 24)), p.units(0.006)) * white, color=hexc("#D9CDE6"), alpha=0.6)
    iris = p.mask(ellipse_pts(x + 0.002 * side, y, 0.025, 0.027, 32)) * white
    X, Y = p.grid()
    t = np.clip((Y - (y - 0.027)) / 0.054, 0, 1)[..., None]
    col = np.asarray(hexc("#3E2A6E"), np.float32) * (1 - t) + np.asarray(hexc("#9C7BE0"), np.float32) * t
    p.paint(iris, color=col)
    p.paint(p.mask(ellipse_pts(x + 0.002 * side, y + 0.001, 0.012, 0.013, 24)) * white, color=hexc("#1E1430"))
    p.paint(p.mask(ellipse_pts(x - 0.008 * side, y - 0.011, 0.0078, 0.0066, 16)) * white, color=WHITE)
    p.paint(p.mask(ellipse_pts(x + 0.008 * side, y + 0.008, 0.0035, 0.003, 12)) * white, color=WHITE, alpha=0.9)
    lid = catmull([(x - w * 1.05 * side, y + 0.004), (x - w * 0.3 * side, y - h * 1.05), (x + w * 0.55 * side, y - h),
                   (x + w * 1.1 * side, y - 0.008), (x + w * 1.35 * side, y - 0.018)], False, 10)
    lm = p.line_mask(lid, 0.010)
    p.paint(lm, color=hexc("#22152E"))
    for k in range(3):
        a = 0.6 + 0.25 * k
        bx, by = x + w * (0.6 + 0.2 * k) * side, y - h * (0.9 - 0.25 * k)
        p.paint(p.line_mask([(bx, by), (bx + math.cos(a) * 0.019 * side, by - math.sin(a) * 0.015)], 0.0045),
                color=hexc("#22152E"))
    crease = catmull([(x - w * 0.8 * side, y - h * 1.2), (x, y - h * 1.6), (x + w * 0.9 * side, y - h * 1.2)], False, 8)
    st.inner_line(p, crease, 0.003, color=SKIN_LINE, alpha=0.45)
    st.inner_line(p, catmull([(x - w * 0.5 * side, y + h * 0.75), (x + w * 0.5 * side, y + h * 0.72)], False, 6), 0.003,
                  color=hexc("#8E6A6A"), alpha=0.35)


def lips(p, x, y):
    """Small glossy lips: a darker line between them, a shine on the lower one."""
    upper = p.mask(catmull([(x - 0.032, y), (x - 0.012, y - 0.012), (x, y - 0.008), (x + 0.012, y - 0.012), (x + 0.032, y),
                            (x, y + 0.003)], True, 8, sharp=(0, 4)))
    lower = p.mask(catmull([(x - 0.03, y + 0.001), (x, y + 0.004), (x + 0.03, y + 0.001), (x + 0.016, y + 0.017),
                            (x, y + 0.02), (x - 0.016, y + 0.017)], True, 8, sharp=(0, 2)))
    st.solid(p, upper, hexc("#E8657F"), form=st.dome(p, upper, 0.008), line=0.0)
    st.solid(p, lower, hexc("#F27F96"), form=st.dome(p, lower, 0.01), spec=0.6, shininess=30, line=0.0)
    st.inner_line(p, catmull([(x - 0.031, y + 0.0005), (x, y + 0.004), (x + 0.031, y + 0.0005)], False, 6), 0.003,
                  color=hexc("#B03A55"), alpha=0.8)
    p.paint(blur(p.mask(ellipse_pts(x + 0.006, y + 0.011, 0.008, 0.003, 12)), p.units(0.002)), color=WHITE, alpha=0.7)


def dollhair():
    """The doll's own sleek bob with a side fringe (dark chocolate brown, not tinted): only while no hair is chosen."""
    brown = hexc("#5B3626")
    back = piece("dollhair", "back", (-0.31, -0.81, 0.31, -0.20), HEAD_PPT, "hair", "back")
    bm = back.mask(catmull(sym([(0, -0.77), (0.19, -0.72), (0.245, -0.55), (0.235, -0.36), (0.19, -0.26), (0, -0.29)]), True, 6))
    st.solid(back, bm, st.deeper(brown, 0.25), form=st.dome(back, bm, 0.1), line=0.005)
    front = piece("dollhair", "front", (-0.31, -0.83, 0.31, -0.24), HEAD_PPT, "hair", "hairfront")
    cap = front.mask(catmull([(0.06, -0.80), (0.17, -0.77), (0.235, -0.66), (0.245, -0.50), (0.225, -0.35), (0.17, -0.285),
                              (0.15, -0.36), (0.165, -0.50), (0.11, -0.60), (0.0, -0.635), (-0.12, -0.60), (-0.17, -0.50),
                              (-0.15, -0.36), (-0.17, -0.285), (-0.225, -0.35), (-0.245, -0.50), (-0.235, -0.66),
                              (-0.15, -0.775)], True, 10))
    fl = st.hanging_folds(front, -0.26, 0.26, 8, 0.008, -0.8, -0.3, seed=4, sharp=1.6)
    st.solid(front, cap, brown, form=st.dome(front, cap, 0.1), folds=fl, valley=0.2, line=0.005,
             line_color=st.deeper(brown, 0.6))
    rng = np.random.default_rng(3)
    for k in range(18):
        u = (k + 0.5) / 18 * 2 - 1
        pts = catmull([(0.04 + u * 0.02, -0.79), (u * 0.2, -0.70), (u * 0.25, -0.40 + 0.05 * abs(u))], False, 8)
        st.inner_line(front, pts, rng.uniform(0.003, 0.006), color=st.deeper(brown, 0.4) if k % 2 else st.lighter(brown, 0.2),
                      alpha=0.45, clip=cap)
    ring = front.mask(catmull([(-0.18, -0.67), (-0.08, -0.73), (0.08, -0.73), (0.18, -0.67), (0.17, -0.645), (0.08, -0.70),
                               (-0.08, -0.70), (-0.17, -0.645)], True, 8))
    st.brighten(front, blur(ring, front.units(0.012)) * cap, 0.3)
    sm = front.mask(star_pts(0.15, -0.66, 0.035, 0.45, 5, round_=1))
    st.solid(front, sm, hexc("#FF7EC8"), form=st.dome(front, sm, 0.012), spec=0.5, line=0.003)
    erase(front, face_hole(front))


ALL = [doll, dollhair]
