"""Hair (hair rig: on the head like a hat, but below `hang` it hangs down with gravity and sways). Every style has a
"back" piece (behind the body: long hair down the back) and a "front" piece (the cap over the skull, the fringe, locks
falling over the shoulders). Drawn like a fashion illustration: locks shaded round, fine strands, darker at the
roots, a shine on the crown and on the waves; the player's face stays clear (wardrobe.face_hole)."""
import math

import numpy as np

from draw import BASE, blur, catmull, ellipse_pts, erode, hexc, resample, rotate, star_pts, sym
import style as st
from wardrobe import HEAD_PPT, GOLD, PEARL, erase, face_hole, flower, pearls, piece


def _frame(c):
    """Arc lengths and unit normals along a polyline."""
    n = len(c)
    lens = np.cumsum([0] + [math.hypot(c[i + 1][0] - c[i][0], c[i + 1][1] - c[i][1]) for i in range(n - 1)])
    normals = []
    for i in range(n):
        a, b = c[max(0, i - 1)], c[min(n - 1, i + 1)]
        dx, dy = b[0] - a[0], b[1] - a[1]
        d = math.hypot(dx, dy) or 1
        normals.append((-dy / d, dx / d))
    return lens, normals


def lock(p, spine, w0, w1, grey=BASE, wave=0.018, waves=2.5, phase=0.0, strands=7, taper=0.35, sheen=0.10, root=0.06,
         line=0.0055, seed=1):
    """A flowing lock of hair along `spine` (top to tip): width w0 to w1, snaking with the waves, a pointed tip; shaded
    round, fine darker and lighter strands along it, darker at the root, a shine on each wave's crest. Returns its mask."""
    rng = np.random.default_rng(seed)
    c = catmull(spine, False, 14)
    n = len(c)
    lens, normals = _frame(c)
    L = lens[-1]

    def width(t):
        w = w0 + (w1 - w0) * t
        return w * (1 if t < 1 - taper else max(0.04, (1 - t) / taper) ** 0.75)

    def offset(t):
        return wave * math.sin((t * waves + phase) * 2 * math.pi)

    left, right, mid = [], [], []
    for i in range(n):
        t = lens[i] / L
        nx, ny = normals[i]
        w, s = width(t), offset(t)
        left.append((c[i][0] + nx * (w + s), c[i][1] + ny * (w + s)))
        right.append((c[i][0] - nx * (w - s), c[i][1] - ny * (w - s)))
        mid.append((c[i][0] + nx * s, c[i][1] + ny * s))
    m = p.mask(left + right[::-1])
    form = st.dome(p, m, max(w0, w1) * 0.9)
    g = st.fabric(p, m, form, None, base=grey, up=0.06, down=0.22, spec=0.0, line=line, line_grey=0.28)
    # darker at the root (worked out in the lock's own box)
    box = st.box_of(m, 2)
    if box is not None:
        ys = (np.arange(box[0].start, box[0].stop, dtype=np.float32) + 0.5) / 4
        xs = (np.arange(box[1].start, box[1].stop, dtype=np.float32) + 0.5) / 4
        Xb = (xs[None, :] - p.origin[0]) / p.ppt
        Yb = (ys[:, None] - p.origin[1]) / p.ppt
        d0 = np.hypot(Xb - c[0][0], Yb - c[0][1])
        st.darken(p, m[box] * np.clip(1 - d0 / max(L * 0.35, 1e-3), 0, 1), root * 4, box=box)
    # strands along the lock
    for k in range(strands):
        u = (k + rng.uniform(0.3, 0.7)) / strands * 2 - 1
        pts = []
        for i in range(0, n, 2):
            t = lens[i] / L
            nx, ny = normals[i]
            off = u * width(t) * 0.82 + offset(t)
            pts.append((c[i][0] + nx * off, c[i][1] + ny * off))
        dark = k % 2 == 0
        st.inner_line(p, pts, rng.uniform(0.0035, 0.0065), grey=max(0.0, grey - 0.2) if dark else min(1.0, grey + 0.1),
                      alpha=0.5 if dark else 0.35, clip=m)
    # a glossy streak along the lock where each wave bulges toward the light
    if sheen:
        crests = []
        for i in range(1, n - 1):
            t = lens[i] / L
            if t > 0.9:
                break
            ph = (t * waves + phase) % 1.0
            if waves >= 1.2 and abs(ph - 0.0) < 0.5 / max(1, n) * waves * 8 and (not crests or i - crests[-1] > 4):
                crests.append(i)
        if not crests:
            crests = [max(1, int(n * 0.25))]
        for i in crests:
            run = range(max(0, i - 4), min(n, i + 5))
            pts = []
            for j in run:
                t = lens[j] / L
                nx, ny = normals[j]
                off = offset(t) + width(t) * 0.28
                pts.append((c[j][0] + nx * off, c[j][1] + ny * off))
            if len(pts) > 2:
                st.inner_line(p, pts, width(lens[i] / L) * 0.32, grey=min(1.0, grey + 0.14), alpha=sheen * 4.5, clip=m)
    return m


def hair_cap(p, top=-0.80, side=0.255, low=-0.38, grey=BASE, part=0.05, strands=16, seed=3):
    """The hair over the skull down to the ears (the face is cut out later), strands combed from the part."""
    cap = [(part, top), (0.16, top + 0.025), (side - 0.01, -0.66), (side, -0.52), (side - 0.03, low), (0.17, low - 0.05),
           (0.15, -0.55), (0.0, -0.63), (-0.15, -0.55), (-0.17, low - 0.05), (-(side - 0.03), low), (-side, -0.52),
           (-(side - 0.01), -0.66), (-0.16, top + 0.025)]
    m = p.mask(catmull(cap, True, 10))
    st.fabric(p, m, st.dome(p, m, 0.14), None, base=grey, up=0.06, down=0.24, line=0.0055, line_grey=0.28)
    rng = np.random.default_rng(seed)
    for k in range(strands):
        u = (k + 0.5) / strands * 2 - 1
        sx = part + u * 0.02
        ex = u * (side + 0.02)
        pts = catmull([(sx, top + 0.01), (sx + (ex - sx) * 0.55, top + 0.08 + 0.05 * abs(u)), (ex, low - 0.02 + 0.08 * (1 - abs(u)))],
                      False, 8)
        dark = k % 2 == 0
        st.inner_line(p, pts, rng.uniform(0.004, 0.007), grey=grey - 0.2 if dark else min(1.0, grey + 0.1),
                      alpha=0.45 if dark else 0.3, clip=m)
    return m


def skull_trim(p, side=0.27, top=-0.80, flare=0.85, shoulders=-0.12):
    """Long hair behind the head mustn't stick out above it: round the skull it stays inside the head's outline, below
    the ears it may widen gradually (flare per torso length) down to the shoulders."""
    X, Y = p.grid()
    cy, ry = (top + -0.24) / 2, (-0.24 - top) / 2
    in_skull = (X / side) ** 2 + ((Y - cy) / ry) ** 2 <= 1.0
    w = side + flare * np.clip(Y - cy, 0, None)
    outside = (np.abs(X) > w) & (Y < shoulders) & ~((Y < cy) & in_skull)
    outside |= (Y < cy) & ~in_skull
    erase(p, blur(outside.astype(np.float32), p.units(0.008)))


def crown_shine(p, region, y=-0.71, w=0.19, strength=0.30):
    """The ring of light round shiny hair."""
    ring = p.mask(catmull([(-w, y + 0.04), (-w * 0.45, y - 0.035), (w * 0.45, y - 0.035), (w, y + 0.04),
                           (w * 0.95, y + 0.07), (w * 0.45, y + 0.0), (-w * 0.45, y + 0.0), (-w * 0.95, y + 0.07)], True, 8))
    st.brighten(p, blur(ring, p.units(0.012)) * region, strength)


def fringe(p, part, locks, cap, grey=BASE, top=-0.80):
    """Bangs: locks from the part across the forehead (locks: [(x at the part, x at the tip, tip y), ...]). They start
    inside the cap and nothing of them shows above it (their roots stuck out as little spikes)."""
    keep_P, keep_A, keep_M = p.P.copy(), p.A.copy(), p.M.copy()
    m = np.zeros((p.H, p.W), np.float32)
    for k, (x0, x1, y1) in enumerate(locks):
        mk = lock(p, [(part + x0, top + 0.05), ((part + x0 + x1) / 2 + 0.01, top + 0.12), (x1, y1)], 0.06, 0.045,
                  grey=grey + (0.04 if k % 2 else 0.0), wave=0.006, waves=0.8, strands=4, taper=0.55, sheen=0.08, seed=k)
        m = np.maximum(m, mk)
    # outside the cap, only below the hairline counts: put back what was there above it
    X, Y = p.grid()
    above = np.clip((1 - cap) * ((Y < -0.60).astype(np.float32)), 0, 1)
    above = blur(above, p.units(0.003))
    k = above[..., None]
    p.P = p.P * (1 - k) + keep_P * k
    p.A = p.A * (1 - above) + keep_A * above
    p.M = p.M * (1 - above) + keep_M * above
    return m


def scrunchie(p, cx, cy, w, h, color):
    """A ruffled fabric hair tie."""
    m = np.zeros((p.H, p.W), np.float32)
    for k in range(10):
        a = 2 * math.pi * k / 10
        m = np.maximum(m, p.circle_mask(cx + math.cos(a) * w, cy + math.sin(a) * h, max(w, h) * 0.42))
    fl = st.gather_folds(p, (cx, cy), 10, 0.006, max(w, h) * 1.5, seed=4)
    st.solid(p, m, color, form=st.dome(p, m, max(w, h) * 0.5), folds=fl, spec=0.25, valley=0.2, line=0.004)


# ---------------------------------------------------------------------------------------------------- styles

def hair_long():
    """Princeshë: long golden waves from a side part, locks falling over the shoulders, a sweep of fringe."""
    back = piece("hair_long", "back", (-0.58, -0.84, 0.58, 0.76), HEAD_PPT, "hair", "back", hang=-0.30)
    for k, x in enumerate((-0.30, 0.30, -0.18, 0.18, -0.06, 0.06)):
        lock(back, [(x * 0.6, -0.70), (x * 1.05, -0.4), (x * 1.25, 0.0), (x * 1.3, 0.35), (x * 1.2, 0.68)], 0.12, 0.10,
             grey=0.70 if k % 2 else 0.76, wave=0.026, waves=2.2, phase=k * 0.3, strands=6, seed=k)
    skull_trim(back)
    front = piece("hair_long", "front", (-0.54, -0.86, 0.54, 0.54), HEAD_PPT, "hair", "hairfront", hang=-0.30)
    cap = hair_cap(front, part=0.05)
    for side in (-1, 1):
        lock(front, [(0.20 * side, -0.58), (0.25 * side, -0.35), (0.26 * side, -0.1), (0.30 * side, 0.15), (0.27 * side, 0.47)],
             0.07, 0.075, wave=0.022, waves=2.5, phase=0.2 if side > 0 else 0.7, strands=6, seed=10 + side)
        lock(front, [(0.16 * side, -0.56), (0.20 * side, -0.35), (0.19 * side, -0.1), (0.22 * side, 0.12), (0.20 * side, 0.30)],
             0.04, 0.05, grey=0.86, wave=0.018, waves=2.0, phase=0.5, strands=4, seed=20 + side)
    fringe(front, 0.05, [(0.0, -0.17, -0.55), (0.01, -0.08, -0.59), (0.03, 0.04, -0.60), (0.05, 0.16, -0.56)], cap)
    crown_shine(front, cap)
    erase(front, face_hole(front))


def bun(p, cx, cy, r, grey=BASE, seed=1):
    """A twisted bun: a round coil with strands spiralling into it."""
    m = p.mask(ellipse_pts(cx, cy, r, r * 0.92, 64))
    st.fabric(p, m, st.dome(p, m, r * 0.9), None, base=grey, up=0.07, down=0.24, line=0.0055, line_grey=0.28)
    rng = np.random.default_rng(seed)
    for k in range(7):
        a0 = k * 0.9 + rng.uniform(0, 0.4)
        pts = [(cx + math.cos(a0 + t) * r * (0.9 - t / 7.5), cy + math.sin(a0 + t) * r * 0.85 * (0.9 - t / 7.5))
               for t in np.linspace(0, 4.8, 18)]
        st.inner_line(p, pts, 0.005, grey=grey - 0.22, alpha=0.5, clip=m)
        st.inner_line(p, [(x - 0.004, y - 0.004) for x, y in pts], 0.003, grey=min(1.0, grey + 0.12), alpha=0.35, clip=m)
    shine = blur(p.mask(rotate(ellipse_pts(cx - r * 0.35, cy - r * 0.35, r * 0.35, r * 0.16, 24), -0.7, cx - r * 0.35, cy - r * 0.35)),
                 p.units(0.01)) * m
    st.brighten(p, shine, 0.5)
    return m


def hair_buns():
    """Festë: two space buns with bright scrunchies, a blunt fringe, a short bob behind."""
    back = piece("hair_buns", "back", (-0.36, -0.80, 0.36, -0.08), HEAD_PPT, "hair", "back", hang=-0.30)
    bob = back.mask(catmull(sym([(0, -0.74), (0.22, -0.68), (0.29, -0.45), (0.28, -0.22), (0.20, -0.15), (0, -0.19)]), True, 6))
    st.fabric(back, bob, st.dome(back, bob, 0.1), None, base=0.72, up=0.05, down=0.2, line=0.0055, line_grey=0.28)
    front = piece("hair_buns", "front", (-0.36, -1.04, 0.36, -0.30), HEAD_PPT, "hair", "hairfront", hang=-0.2)
    for side in (-1, 1):
        bun(front, 0.17 * side, -0.85, 0.115, seed=3 + side)
    cap = hair_cap(front, top=-0.79, side=0.25, low=-0.40, part=0.0)
    for side in (-1, 1):
        scrunchie(front, 0.155 * side, -0.745, 0.075, 0.022, hexc("#FF7EC8") if side < 0 else hexc("#6EC3FF"))
    fr = [(-0.19, -0.66), (-0.10, -0.74), (0.10, -0.74), (0.19, -0.66), (0.17, -0.555), (0.10, -0.565), (0.04, -0.55),
          (-0.03, -0.565), (-0.10, -0.55), (-0.17, -0.56)]
    fm = front.mask(catmull(fr, True, 8))
    st.fabric(front, fm, st.dome(front, fm, 0.05), st.hanging_folds(front, -0.19, 0.19, 9, 0.006, -0.74, -0.55, seed=5),
              base=0.84, up=0.06, down=0.2, valley=0.15, line=0.0055, line_grey=0.28)
    for x in np.linspace(-0.15, 0.15, 11):
        st.inner_line(front, [(x * 0.7, -0.72), (x, -0.57)], 0.004, grey=0.62, alpha=0.45, clip=fm)
    crown_shine(front, cap, y=-0.71, w=0.17)
    erase(front, face_hole(front))


def braid(p, spine, r0, r1, grey=BASE, seed=1):
    """A three-strand braid down `spine`: interlocking lobes, alternating sides, each shaded round with strands."""
    c = catmull(spine, False, 12)
    pts = resample(c, r0 * 0.62)
    m_all = np.zeros((p.H, p.W), np.float32)
    for k, (x, y) in enumerate(pts):
        t = k / max(1, len(pts) - 1)
        r = r0 + (r1 - r0) * t
        s = 1 if k % 2 else -1
        j = min(len(pts) - 1, k + 1)
        ang = math.atan2(pts[j][1] - y, pts[j][0] - x) if j != k else math.pi / 2
        lob = rotate(ellipse_pts(x + s * r * 0.30 * math.sin(ang), y - s * r * 0.30 * math.cos(ang), r * 0.95, r * 0.46, 32),
                     ang + 0.62 * s, x, y)
        m = p.mask(lob)
        st.cast_shadow(p, m, dx=0.004 * s, dy=0.008, softness=0.008, amount=0.3)
        st.fabric(p, m, st.dome(p, m, r * 0.5), None, base=grey + (0.04 if k % 2 else 0.0), up=0.07, down=0.24,
                  line=0.005, line_grey=0.28)
        st.inner_line(p, [(x - s * r * 0.15, y - r * 0.35), (x + s * r * 0.5, y + r * 0.25)], 0.004, grey=grey - 0.22,
                      alpha=0.5, clip=m)
        st.brighten(p, blur(p.mask(ellipse_pts(x - r * 0.2, y - r * 0.12, r * 0.3, r * 0.12, 16)), p.units(0.006)) * m, 0.4)
        m_all = np.maximum(m_all, m)
    return m_all, pts[-1]


def hair_braid():
    """Dimër / Princeshë: a thick braid over the shoulder, side-swept bangs, a snowflake clip."""
    back = piece("hair_braid", "back", (-0.40, -0.82, 0.40, 0.25), HEAD_PPT, "hair", "back", hang=-0.30)
    bm = back.mask(catmull(sym([(0, -0.76), (0.22, -0.70), (0.30, -0.48), (0.31, -0.20), (0.25, 0.10), (0, 0.15)]), True, 6))
    st.fabric(back, bm, st.dome(back, bm, 0.12), st.hanging_folds(back, -0.31, 0.31, 6, 0.01, -0.7, 0.15, seed=2),
              base=0.72, up=0.05, down=0.2, valley=0.15, line=0.0055, line_grey=0.28)
    front = piece("hair_braid", "front", (-0.40, -0.86, 0.52, 0.80), HEAD_PPT, "hair", "hairfront", hang=-0.25)
    cap = hair_cap(front, part=-0.06)
    _, end = braid(front, [(0.20, -0.45), (0.26, -0.25), (0.30, 0.0), (0.33, 0.25), (0.32, 0.55)], 0.078, 0.052, seed=4)
    tie = front.mask(ellipse_pts(end[0], end[1] + 0.03, 0.036, 0.02, 24))
    st.solid(front, tie, hexc("#6EC3FF"), form=st.dome(front, tie, 0.015), spec=0.4, line=0.003)
    lock(front, [(end[0], end[1] + 0.04), (end[0] + 0.02, end[1] + 0.12), (end[0] - 0.01, end[1] + 0.20)], 0.04, 0.03,
         wave=0.01, waves=1, strands=3, taper=0.6, seed=9)
    fringe(front, -0.06, [(0.0, 0.17, -0.55), (-0.02, 0.08, -0.60), (-0.04, -0.04, -0.60), (-0.06, -0.16, -0.56)], cap)
    from bottoms import snowflake
    snowflake(front, -0.20, -0.65, 0.045, color=hexc("#E8F6FF"))
    crown_shine(front, cap)
    erase(front, face_hole(front))


def curl_cloud(p, cx, cy, rx, ry, grey, seed=1, curls=120):
    """A cloud of curls: a bumpy outline of round curls, each curl a shaded spiral."""
    rng = np.random.default_rng(seed)
    m = p.mask(ellipse_pts(cx, cy, rx * 0.88, ry * 0.88, 48))
    for k in range(38):
        a = 2 * math.pi * k / 38 + rng.uniform(-0.05, 0.05)
        r = rng.uniform(0.05, 0.075)
        m = np.maximum(m, p.circle_mask(cx + math.cos(a) * (rx - r * 0.6), cy + math.sin(a) * (ry - r * 0.6), r))
    st.fabric(p, m, st.dome(p, m, 0.14), None, base=grey, up=0.06, down=0.24, line=0.0055, line_grey=0.28)
    ys, xs = np.nonzero(m > 0.5)
    for k in range(curls):
        i = rng.integers(len(xs))
        x = (xs[i] / 4 - p.origin[0]) / p.ppt
        y = (ys[i] / 4 - p.origin[1]) / p.ppt
        r = rng.uniform(0.022, 0.04)
        a0 = rng.uniform(0, 2 * math.pi)
        spiral = [(x + math.cos(a0 + t) * r * (1 - t / 9), y + math.sin(a0 + t) * r * (1 - t / 9)) for t in np.linspace(0, 5.0, 14)]
        st.inner_line(p, spiral, 0.0055, grey=grey - 0.22, alpha=0.55, clip=m)
        st.inner_line(p, [(sx - r * 0.12, sy - r * 0.12) for sx, sy in spiral[:7]], 0.004, grey=min(1.0, grey + 0.14),
                      alpha=0.45, clip=m)
    return m


def hair_curly():
    """Verë / Festë: a big cloud of curls round the face, ringlets along the hairline."""
    back = piece("hair_curly", "back", (-0.52, -0.97, 0.52, 0.14), HEAD_PPT, "hair", "back", hang=-0.25)
    curl_cloud(back, 0, -0.42, 0.40, 0.48, 0.70, seed=3)
    front = piece("hair_curly", "front", (-0.50, -0.98, 0.50, -0.18), HEAD_PPT, "hair", "hairfront", hang=-0.25)
    m = curl_cloud(front, 0, -0.58, 0.37, 0.36, BASE, seed=5)
    erase(front, front.mask(ellipse_pts(0, -0.38, 0.165, 0.25, 48)))
    for k in range(9):
        a = math.pi * (1.1 + 0.8 * k / 8)
        x, y = math.cos(a) * 0.165, -0.42 + math.sin(a) * 0.235
        rm = front.mask(ellipse_pts(x, y, 0.042, 0.048, 24))
        st.fabric(front, rm, st.dome(front, rm, 0.03), None, base=0.84 if k % 2 else 0.78, up=0.07, down=0.22, line=0.005,
                  line_grey=0.28)
        st.inner_line(front, [(x + math.cos(t) * 0.023 * (1 - t / 10), y + math.sin(t) * 0.026 * (1 - t / 10))
                              for t in np.linspace(0, 5, 12)], 0.004, grey=0.58, alpha=0.6, clip=rm)
    crown_shine(front, m, y=-0.80, w=0.22, strength=0.18)
    erase(front, face_hole(front, -0.012))


def hair_ponytail():
    """Superheroinë: a high ponytail that swings, swept-back hair, a scrunchie, side bangs."""
    back = piece("hair_ponytail", "back", (-0.30, -0.88, 0.54, 0.32), HEAD_PPT, "hair", "back", hang=-0.62)
    lock(back, [(0.06, -0.80), (0.24, -0.78), (0.34, -0.55), (0.36, -0.25), (0.32, 0.05), (0.36, 0.26)], 0.08, 0.10,
         grey=0.76, wave=0.03, waves=1.8, strands=8, taper=0.45, seed=3)
    bm = back.mask(catmull(sym([(0, -0.76), (0.20, -0.70), (0.27, -0.50), (0.26, -0.30), (0, -0.30)]), True, 6))
    st.fabric(back, bm, st.dome(back, bm, 0.1), None, base=0.70, up=0.05, down=0.2, line=0.0055, line_grey=0.28)
    front = piece("hair_ponytail", "front", (-0.34, -0.92, 0.34, -0.30), HEAD_PPT, "hair", "hairfront", hang=-0.2)
    cap = hair_cap(front, top=-0.80, side=0.24, low=-0.44, part=0.0, strands=18)
    scrunchie(front, 0.10, -0.79, 0.05, 0.028, hexc("#FF5A4E"))
    fringe(front, 0.0, [(0.0, -0.18, -0.56), (0.0, -0.10, -0.60), (0.02, 0.10, -0.60), (0.02, 0.18, -0.56)], cap)
    crown_shine(front, cap, y=-0.71, w=0.17)
    erase(front, face_hole(front))


def hair_mermaid():
    """Sirenë: very long flowing waves, a starfish clip, a string of pearls woven in."""
    back = piece("hair_mermaid", "back", (-0.64, -0.84, 0.64, 1.06), HEAD_PPT, "hair", "back", hang=-0.30)
    for k, x in enumerate((-0.34, 0.34, -0.20, 0.20, -0.07, 0.07)):
        lock(back, [(x * 0.55, -0.70), (x * 1.05, -0.35), (x * 1.3, 0.05), (x * 1.35, 0.5), (x * 1.25, 0.98)], 0.12, 0.11,
             grey=0.70 if k % 2 else 0.77, wave=0.04, waves=3.2, phase=k * 0.25, strands=6, seed=30 + k)
    skull_trim(back)
    front = piece("hair_mermaid", "front", (-0.58, -0.86, 0.58, 0.90), HEAD_PPT, "hair", "hairfront", hang=-0.30)
    cap = hair_cap(front, part=0.0)
    for side in (-1, 1):
        lock(front, [(0.20 * side, -0.58), (0.26 * side, -0.3), (0.30 * side, 0.0), (0.33 * side, 0.4), (0.30 * side, 0.84)],
             0.08, 0.08, wave=0.035, waves=3.5, phase=0.3 if side > 0 else 0.8, strands=7, seed=40 + side)
        lock(front, [(0.16 * side, -0.56), (0.20 * side, -0.3), (0.20 * side, 0.0), (0.24 * side, 0.3)], 0.04, 0.045,
             grey=0.87, wave=0.025, waves=2.5, phase=0.6, strands=4, seed=50 + side)
    fringe(front, 0.0, [(0.0, -0.17, -0.55), (0.0, -0.08, -0.60), (0.0, 0.08, -0.60), (0.0, 0.17, -0.55)], cap)
    sm = front.mask(star_pts(0.20, -0.66, 0.06, 0.5, 5, rot=-math.pi / 2 + 0.3, round_=1))
    st.solid(front, sm, hexc("#FF8A5B"), form=st.dome(front, sm, 0.025), spec=0.3, line=0.004)
    pearls(front, catmull([(-0.24, -0.64), (-0.12, -0.75), (0.0, -0.78)], False, 6), 0.012)
    crown_shine(front, cap)
    erase(front, face_hole(front))


ALL = [hair_long, hair_buns, hair_braid, hair_curly, hair_ponytail, hair_mermaid]
