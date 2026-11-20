"""The wardrobe's registry and shared pieces: every item module (tops.py, bottoms.py, hair.py, heads.py, faces.py,
backs.py, hands.py, doll.py) draws its pieces with `piece()`, which records them for outfits.py to pack.

Art direction (2026-11-09, the user's references: fashion dress-up games): drawn like a fashion illustration, not clip
art. Thin outlines in a deep shade of each fabric's own colour, forms lit from the top left (style.py), folds, satin
highlights, glitter, gold with a shine, real details (lace, pearls, cut gems, stitching). In the camera mirror the
clothes sit on a real person: tops cover from the neck to the hips and over the upper arms (a T-shirt must not show
around them), hair leaves the face open (face_hole), and nothing relies on a skin colour.
"""
import math

import numpy as np

from draw import BASE, Piece, blur, catmull, dilate, ellipse_pts, erode, hexc, resample, sym  # noqa: F401
import style as st

PAD = 6
BODY_PPT = 330      # pixels per torso length for clothes (a TV shows the torso ~330 px tall)
HEAD_PPT = 460      # hats, hair, glasses: small on screen, need the detail
HAND_PPT = 400

PIECES = []         # every Piece, in drawing order
ITEMS = {}          # item id -> [piece names]

GOLD = hexc("#FFCC52")
GOLD_DEEP = hexc("#C98A1E")
SILVER = hexc("#E4E8F2")
PEARL = (0.98, 0.96, 1.0)
WHITE = (1.0, 1.0, 1.0)
INK = hexc("#3A2547")


def piece(item, part, bounds, ppt, rig, layer, **meta):
    """A new cell covering `bounds` (x0, y0, x1, y1 in the piece's template units)."""
    x0, y0, x1, y1 = bounds
    w = int(math.ceil((x1 - x0) * ppt)) + 2 * PAD
    h = int(math.ceil((y1 - y0) * ppt)) + 2 * PAD
    p = Piece(f"{item}.{part}", w, h, (PAD - x0 * ppt, PAD - y0 * ppt), ppt, rig=rig, layer=layer, **meta)
    PIECES.append(p)
    ITEMS.setdefault(item, []).append(p.name)
    return p


def clear():
    PIECES.clear()
    ITEMS.clear()


# ---------------------------------------------------------------------------------------------------- shared parts

def gold(p, m, spec=0.65, line=0.003, color=GOLD):
    """Polished gold (or another metal) on mask m: a rounded bevel, a bright shine."""
    return st.solid(p, m, color, form=st.dome(p, m, 0.012), spec=spec, shininess=26, line=line,
                    line_color=st.deeper(color, 0.6))


def gold_line(p, pts, width=0.016, color=GOLD, closed=False):
    """A gold cord or piping along pts."""
    m = p.line_mask(list(pts) + ([pts[0]] if closed else []), width)
    gold(p, m, color=color)
    return m


def braid_trim(p, pts, width=0.02, color=GOLD):
    """A braided gold trim: little slanted links along pts."""
    base = p.line_mask(pts, width)
    gold(p, base, color=st.deeper(color, 0.15))
    for (x0, y0), (x1, y1) in zip(resample(pts, width * 0.9)[:-1], resample(pts, width * 0.9)[1:]):
        dx, dy = x1 - x0, y1 - y0
        L = math.hypot(dx, dy) or 1
        nx, ny = -dy / L, dx / L
        link = [(x0 - nx * width * 0.42, y0 - ny * width * 0.42), (x1 + nx * width * 0.42, y1 + ny * width * 0.42)]
        gold(p, p.line_mask(link, width * 0.42), spec=0.8, line=0.0015, color=color)
    return base


def pearls(p, pts, r=0.011, gap=0.95, color=PEARL):
    for x, y in resample(pts, 2 * r * gap):
        st.pearl(p, x, y, r, color)


def bow(p, cx, cy, r, grey=None, color=None, rot=0.0, tails=True):
    """A satin bow: two loops with gathered folds, tails, a knot, shaded (tinted grey or a fixed colour)."""
    from draw import rotate
    loop = [(0.0, -0.05), (0.35, -0.42), (0.85, -0.48), (1.02, -0.12), (0.95, 0.28), (0.55, 0.40), (0.1, 0.12)]
    tail = [(0.06, 0.12), (0.30, 0.62), (0.52, 1.00), (0.40, 1.08), (0.30, 0.95), (0.16, 1.04), (0.02, 0.35)]
    parts = []
    for side in (1, -1):
        if tails:
            parts.append(("tail", rotate([(cx + x * r * side * 0.75, cy + y * r) for x, y in catmull(tail, True, 6)], rot, cx, cy)))
        parts.append(("loop", rotate([(cx + x * r * side, cy + y * r) for x, y in catmull(loop, True, 8)], rot, cx, cy)))
    for kind, pts in parts:
        m = p.mask(pts)
        fl = st.gather_folds(p, (cx, cy), 7, r * 0.12, r * 0.9, seed=int(cx * 100) % 7 + (3 if kind == "tail" else 1))
        form = st.dome(p, m, r * 0.35)
        if grey is not None:
            st.fabric(p, m, form, fl, base=grey, up=0.07, down=0.24, spec=0.35, shininess=18, valley=0.2, line=0.004)
        else:
            st.solid(p, m, color, form=form, folds=fl, spec=0.35, shininess=18, line=0.004, valley=0.2)
    knot = p.mask(rotate(ellipse_pts(cx, cy, r * 0.22, r * 0.28, 24), rot, cx, cy))
    if grey is not None:
        st.fabric(p, knot, st.dome(p, knot, r * 0.15), None, base=grey, up=0.08, down=0.22, spec=0.4, line=0.004)
    else:
        st.solid(p, knot, color, form=st.dome(p, knot, r * 0.15), spec=0.4, line=0.004)


def flower(p, cx, cy, r, petal=None, grey=None, center=GOLD, petals=5, rot=0.0, cup=0.0):
    """A small flower: shaded round petals (a fixed colour, or a tinted grey), a centre with dots."""
    pm_all = np.zeros((p.H, p.W), np.float32)
    for k in range(petals):
        a = rot + 2 * math.pi * k / petals
        px, py = cx + math.cos(a) * r * 0.52, cy + math.sin(a) * r * 0.52
        pts = ellipse_pts(px, py, r * 0.5, r * 0.40, 32, a0=0)
        from draw import rotate
        pts = rotate(pts, a, px, py)
        m = p.mask(pts) * (1 - pm_all * 0.0)
        form = st.dome(p, m, r * 0.25)
        if grey is not None:
            st.fabric(p, m, form, None, base=grey, up=0.08, down=0.2, line=0.003)
        else:
            st.solid(p, m, petal, form=form, line=0.003, up=0.08, down=0.2)
        pm_all = np.maximum(pm_all, m)
    c = p.circle_mask(cx, cy, r * 0.3)
    st.solid(p, c, center, form=st.dome(p, c, r * 0.3), spec=0.3, line=0.003)
    return pm_all


def leaf(p, x0, y0, x1, y1, width, color=None, grey=None, vein=True):
    """A pointed leaf from (x0, y0) to (x1, y1), shaded, with a vein."""
    dx, dy = x1 - x0, y1 - y0
    L = math.hypot(dx, dy) or 1
    nx, ny = -dy / L, dx / L
    side_a = [(x0 + dx * t + nx * width * math.sin(math.pi * t) ** 0.8, y0 + dy * t + ny * width * math.sin(math.pi * t) ** 0.8)
              for t in np.linspace(0, 1, 16)]
    side_b = [(x0 + dx * t - nx * width * math.sin(math.pi * t) ** 0.8, y0 + dy * t - ny * width * math.sin(math.pi * t) ** 0.8)
              for t in np.linspace(1, 0, 16)][1:-1]
    m = p.mask(side_a + side_b)
    form = st.dome(p, m, width * 0.8)
    if grey is not None:
        st.fabric(p, m, form, None, base=grey, up=0.08, down=0.22, line=0.003)
    else:
        st.solid(p, m, color, form=form, line=0.003)
    if vein:
        st.inner_line(p, [(x0 + dx * 0.08, y0 + dy * 0.08), (x0 + dx * 0.85, y0 + dy * 0.85)], width * 0.14,
                      color=st.deeper(color, 0.3) if color is not None else None, grey=None if color is not None else grey - 0.15,
                      alpha=0.5, clip=m)
    return m


def fabric_light(p, m):
    """0..1: how lit the painted fabric is inside m (for glitter and sequins to sparkle where the light falls)."""
    a = np.clip(p.A, 1e-4, None)
    g = p.P[..., 0] / a
    return np.clip((g - 0.6) / 0.32, 0, 1) * (m > 0.5)


def face_hole(p, grow=0.0):
    """The player's face oval (template: eyes at y -0.47), kept clear by hair and hats."""
    return p.mask(ellipse_pts(0, -0.44, 0.140 + grow, 0.185 + grow, 64))


def erase(p, m):
    """Clears what's painted where m is (premultiplied: everything scales down together)."""
    keep = 1 - np.clip(m, 0, 1)
    p.P *= keep[..., None]
    p.A *= keep
    p.M *= keep
