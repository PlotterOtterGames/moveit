"""A small vector toolkit for Vishu!'s outfit art: smooth shapes in body units, drawn 4x supersampled, with soft
"pillow" shading, cartoon outlines, fabric folds and little patterns (dots, stars, sequins, scales, lace).

Every piece is drawn in the shared BODY TEMPLATE (units of T = one torso length, from the shoulder centre to the hip
centre; x to the screen's right, y down; origin = the middle between the shoulders):

    shoulders  (+-0.40, 0)      hips   (+-0.24, 1.00)    knees (+-0.25, 1.92)   ankles (+-0.25, 2.85)
    eyes       (+-0.064, -0.47) ears   (+-0.15, -0.43)   nose  (0, -0.38)

Unity's rig (Games/Dress/Scripts/OutfitRig.cs) maps these anchors onto the player's joints, so a piece only has to fit
this one body. Sleeves and hand items have their own small frames (along the arm, around the grip).

A piece keeps three layers (premultiplied while drawing):
    colour   fixed colours, or a grey level where the piece takes the player's chosen colour
    alpha    coverage
    tint     1 where the colour is the chosen one (grey 0.80 = exactly the colour, darker = shade, lighter = sheen)
"""
import math

import cv2
import numpy as np
from PIL import Image, ImageDraw

SS = 4                       # supersampling
BASE = 0.80                  # tinted grey that shows the chosen colour as it is
OUTLINE_GREY = 0.30          # outlines of tinted shapes: a dark shade of the colour
INK = (0.23, 0.15, 0.28)     # outlines of fixed-colour details (deep plum, the brand's ink)
LIGHT = np.array([-0.45, -0.89], np.float32)   # light from the top left (image coordinates, y down)

GOLD = (1.00, 0.80, 0.30)
GOLD_DARK = (0.80, 0.52, 0.12)
WHITE = (1.0, 1.0, 1.0)
PEARL = (0.98, 0.96, 1.0)


def hexc(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


# ---------------------------------------------------------------------------------------------------- curves

def catmull(pts, closed=True, samples=10, sharp=()):
    """A smooth curve through pts (Catmull-Rom). Indices in `sharp` are corners."""
    n = len(pts)
    out = []
    rng = range(n) if closed else range(n - 1)
    for i in rng:
        p0 = np.array(pts[(i - 1) % n] if closed or i > 0 else pts[0], float)
        p1 = np.array(pts[i], float)
        p2 = np.array(pts[(i + 1) % n], float)
        p3 = np.array(pts[(i + 2) % n] if closed or i + 2 < n else pts[-1], float)
        if i in sharp:
            p0 = p1
        if (i + 1) % n in sharp:
            p3 = p2
        for k in range(samples):
            t = k / samples
            t2, t3 = t * t, t * t * t
            p = 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)
            out.append((float(p[0]), float(p[1])))
    if not closed:
        out.append(tuple(map(float, pts[-1])))
    return out


def sym(half, samples=10, sharp=()):
    """A closed symmetric outline from its right half: points from the top centre (x = 0) down the right side to the
    bottom centre (x = 0). The left side is the mirror image."""
    right = list(half)
    left = [(-x, y) for x, y in reversed(half[1:-1])]
    pts = right + left
    n = len(right)
    sharp_all = set(sharp) | {2 * n - 2 - i for i in sharp if 0 < i < n - 1}
    return catmull(pts, True, samples, sharp_all)


def ellipse_pts(cx, cy, rx, ry, n=64, a0=0.0):
    return [(cx + rx * math.cos(a0 + 2 * math.pi * k / n), cy + ry * math.sin(a0 + 2 * math.pi * k / n)) for k in range(n)]


def star_pts(cx, cy, r, inner=0.45, points=5, rot=-math.pi / 2, round_=0.0):
    pts = []
    for k in range(points * 2):
        rr = r if k % 2 == 0 else r * inner
        a = rot + math.pi * k / points
        pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
    if round_ > 0:
        return catmull(pts, True, 4)
    return pts


def heart_pts(cx, cy, r, n=80, rot=0.0):
    pts = []
    for k in range(n):
        t = 2 * math.pi * k / n
        x = 16 * math.sin(t) ** 3
        y = -(13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t))
        x, y = x / 17 * r, (y + 2) / 17 * r
        c, s = math.cos(rot), math.sin(rot)
        pts.append((cx + x * c - y * s, cy + x * s + y * c))
    return pts


def rotate(pts, angle, cx=0.0, cy=0.0):
    c, s = math.cos(angle), math.sin(angle)
    return [(cx + (x - cx) * c - (y - cy) * s, cy + (x - cx) * s + (y - cy) * c) for x, y in pts]


def move(pts, dx, dy, k=1.0):
    return [(x * k + dx, y * k + dy) for x, y in pts]


def mirror_x(pts):
    return [(-x, y) for x, y in pts]


def bezier(p0, p1, p2, p3, n=24):
    out = []
    for k in range(n + 1):
        t = k / n
        u = 1 - t
        out.append((u ** 3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t ** 3 * p3[0],
                    u ** 3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t ** 3 * p3[1]))
    return out


# ---------------------------------------------------------------------------------------------------- the piece

class Piece:
    """One cell of the atlas: w x h output pixels; body template point (0, 0) lands at `origin` (output pixels),
    one torso length is `ppt` output pixels."""

    def __init__(self, name, w, h, origin, ppt, **meta):
        self.name, self.w, self.h = name, w, h
        self.origin, self.ppt = origin, ppt
        self.meta = meta
        self.W, self.H = w * SS, h * SS
        self.P = np.zeros((self.H, self.W, 3), np.float32)   # premultiplied colour
        self.A = np.zeros((self.H, self.W), np.float32)
        self.M = np.zeros((self.H, self.W), np.float32)      # premultiplied tint mask

    # ---- coordinates
    def px(self, x, y):
        return ((self.origin[0] + x * self.ppt) * SS, (self.origin[1] + y * self.ppt) * SS)

    def units(self, d):
        """A distance in body units -> supersampled pixels."""
        return d * self.ppt * SS

    def grid(self):
        """Body-template coordinates of every supersampled pixel (x, y arrays)."""
        ys, xs = np.mgrid[0:self.H, 0:self.W].astype(np.float32)
        return ((xs + 0.5) / SS - self.origin[0]) / self.ppt, ((ys + 0.5) / SS - self.origin[1]) / self.ppt

    # ---- masks
    def mask(self, pts):
        img = Image.new("L", (self.W, self.H), 0)
        ImageDraw.Draw(img).polygon([self.px(x, y) for x, y in pts], fill=255)
        return np.asarray(img, np.float32) / 255

    def line_mask(self, pts, width, round_caps=True):
        img = Image.new("L", (self.W, self.H), 0)
        d = ImageDraw.Draw(img)
        w = max(1, int(round(self.units(width))))
        p = [self.px(x, y) for x, y in pts]
        d.line(p, fill=255, width=w, joint="curve")
        if round_caps:
            r = w / 2
            for q in (p[0], p[-1]):
                d.ellipse([q[0] - r, q[1] - r, q[0] + r, q[1] + r], fill=255)
        return np.asarray(img, np.float32) / 255

    def circle_mask(self, cx, cy, r):
        return self.mask(ellipse_pts(cx, cy, r, r, n=max(24, int(self.units(r) / 2))))

    def coverage(self):
        return self.A.copy()

    # ---- painting
    def paint(self, mask, color=None, grey=None, alpha=1.0, under=False):
        """Composites `mask` (0..1) with a fixed colour (rgb, or an H x W x 3 array) or a tinted grey (number or H x W
        array). under=True paints behind what is there (outlines)."""
        k = np.clip(mask * alpha, 0, 1).astype(np.float32)
        if grey is not None:
            g = np.asarray(grey, np.float32)
            col = np.repeat((g if g.ndim == 2 else np.full((self.H, self.W), float(g), np.float32))[..., None], 3, 2)
            t = 1.0
        else:
            c = np.asarray(color, np.float32)
            col = c if c.ndim == 3 else np.broadcast_to(c, (self.H, self.W, 3))
            t = 0.0
        if under:
            free = 1 - self.A
            self.P += col * (k * free)[..., None]
            self.M += t * k * free
            self.A += k * free
        else:
            self.P = col * k[..., None] + self.P * (1 - k)[..., None]
            self.M = t * k + self.M * (1 - k)
            self.A = k + self.A * (1 - k)

    def shape(self, pts, grey=None, color=None, outline=0.012, shade=0.0, shade_radius=0.06, ink=None, alpha=1.0,
              clip=None):
        """A filled shape with an outline behind it and optional pillow shading. Returns its mask."""
        m = self.mask(pts)
        if clip is not None:
            m *= clip
        self.fill(m, grey=grey, color=color, outline=outline, shade=shade, shade_radius=shade_radius, ink=ink,
                  alpha=alpha)
        return m

    def fill(self, m, grey=None, color=None, outline=0.012, shade=0.0, shade_radius=0.06, ink=None, alpha=1.0):
        if outline > 0:
            o = dilate(m, self.units(outline))
            if grey is not None and ink is None:
                self.paint(o, grey=OUTLINE_GREY, alpha=alpha)
            else:
                self.paint(o, color=ink if ink is not None else INK, alpha=alpha)
        if shade and grey is not None:
            g = np.asarray(grey, np.float32) + shade * pillow(m, self.units(shade_radius))
            self.paint(m, grey=np.clip(g, 0.0, 1.0), alpha=alpha)
        elif shade and color is not None:
            s = pillow(m, self.units(shade_radius))[..., None] * shade
            base = np.broadcast_to(np.asarray(color, np.float32), (self.H, self.W, 3))
            self.paint(m, color=np.clip(base * (1 + s), 0, 1), alpha=alpha)
        else:
            self.paint(m, grey=grey, color=color, alpha=alpha)

    def stroke(self, pts, width, grey=None, color=None, alpha=1.0, clip=None):
        m = self.line_mask(pts, width)
        if clip is not None:
            m *= clip
        self.paint(m, grey=grey, color=color, alpha=alpha)
        return m

    def shade_inside(self, region, amount, clip=None):
        """Darkens (amount < 0) or lightens tinted pixels inside `region` (a mask), keeping colours/greys apart."""
        r = region if clip is None else region * clip
        tint = np.where(self.A > 1e-4, self.M / np.maximum(self.A, 1e-4), 0)
        k = r * tint
        self.P += (amount * k * self.A)[..., None]
        self.P = np.clip(self.P, 0, self.A[..., None])

    def tint_coverage(self):
        """Where the tinted area is (0..1): for clipping patterns to the fabric."""
        return np.where(self.A > 1e-4, self.M / np.maximum(self.A, 1e-4), 0) * np.clip(self.A, 0, 1)

    # ---- output
    def image(self):
        """(RGBA colour, L tint mask) at output size, straight alpha with colours bled outward (clean mip edges)."""
        a = np.clip(self.A, 0, 1)
        rgb = np.where(a[..., None] > 1e-4, self.P / np.maximum(a[..., None], 1e-4), 0)
        m = np.where(a > 1e-4, self.M / np.maximum(a, 1e-4), 0)
        small = lambda x: cv2.resize(x, (self.w, self.h), interpolation=cv2.INTER_AREA)
        a_s = small(a)
        # downsample premultiplied (correct averaging), then un-premultiply
        rgb_s = small(rgb * a[..., None]) / np.maximum(a_s[..., None], 1e-4)
        m_s = small(m * a) / np.maximum(a_s, 1e-4)
        rgb_s, m_s = bleed(rgb_s, m_s, a_s)
        rgba = np.dstack([np.clip(rgb_s, 0, 1), np.clip(a_s, 0, 1)])
        return (rgba * 255 + 0.5).astype(np.uint8), (np.clip(m_s, 0, 1) * 255 + 0.5).astype(np.uint8)


# ---------------------------------------------------------------------------------------------------- helpers

def dilate(m, r):
    r = max(1, int(round(r)))
    k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * r + 1, 2 * r + 1))
    return cv2.dilate(m, k)


def erode(m, r):
    r = max(1, int(round(r)))
    k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * r + 1, 2 * r + 1))
    return cv2.erode(m, k)


def blur(m, sigma):
    if sigma <= 0.5:
        return m
    k = int(sigma * 3) * 2 + 1
    return cv2.GaussianBlur(m, (k, k), sigma)


def pillow(m, radius):
    """-1..1: light on the edges facing the top left, shade on the others, flat inside: a soft rounded look."""
    b = blur(m, radius)
    gy, gx = np.gradient(b)
    norm = radius * math.sqrt(2 * math.pi)
    n = -(gx * LIGHT[0] + gy * LIGHT[1]) * norm
    return np.clip(n, -1, 1) * (m > 0)


def bleed(rgb, m, a, steps=12):
    """Spreads the colour of covered pixels into the empty ones next to them (no dark halos when filtered)."""
    filled = a > 0.02
    rgb = rgb.copy()
    m = m.copy()
    k = np.ones((3, 3), np.float32)
    for _ in range(steps):
        if filled.all():
            break
        w = cv2.filter2D(filled.astype(np.float32), -1, k, borderType=cv2.BORDER_CONSTANT)
        s = cv2.filter2D(rgb * filled[..., None], -1, k, borderType=cv2.BORDER_CONSTANT)
        sm = cv2.filter2D(m * filled, -1, k, borderType=cv2.BORDER_CONSTANT)
        grow = (~filled) & (w > 0)
        rgb[grow] = s[grow] / w[grow][..., None]
        m[grow] = sm[grow] / w[grow]
        filled = filled | grow
    return rgb, m


def radial_folds(piece, focus, count, amount, start_y, end_y, phase=0.0):
    """Skirt folds: a ripple around a point above the waist, growing toward the hem (a grey offset field)."""
    X, Y = piece.grid()
    ang = np.arctan2(X - focus[0], Y - focus[1])
    t = np.clip((Y - start_y) / max(1e-3, end_y - start_y), 0, 1)
    return amount * np.sin(ang * count + phase) * (0.25 + 0.75 * t)


def vertical_gradient(piece, y0, y1, g0, g1):
    _, Y = piece.grid()
    t = np.clip((Y - y0) / (y1 - y0), 0, 1)
    return g0 + (g1 - g0) * t


def color_gradient(piece, y0, y1, c0, c1):
    _, Y = piece.grid()
    t = np.clip((Y - y0) / (y1 - y0), 0, 1)[..., None]
    return np.asarray(c0, np.float32) * (1 - t) + np.asarray(c1, np.float32) * t


# ---------------------------------------------------------------------------------------------------- details

def gem(piece, cx, cy, r, color, sparkle=True):
    """A faceted gem: an outlined octagon, darker lower facets, a bright table and a twinkle."""
    pts = ellipse_pts(cx, cy, r, r * 1.1, n=8, a0=math.pi / 8)
    m = piece.mask(pts)
    piece.paint(dilate(m, piece.units(0.008)), color=INK)
    c = np.asarray(color, np.float32)
    piece.paint(m, color=c * 0.75)
    piece.paint(piece.mask(ellipse_pts(cx - r * 0.12, cy - r * 0.15, r * 0.62, r * 0.62, n=8, a0=math.pi / 8)),
                color=np.clip(c * 1.1 + 0.08, 0, 1))
    piece.paint(piece.mask(ellipse_pts(cx - r * 0.3, cy - r * 0.38, r * 0.22, r * 0.16, n=12)), color=WHITE, alpha=0.9)
    if sparkle:
        piece.meta.setdefault("sparkles", []).append([round(cx - r * 0.3, 4), round(cy - r * 0.38, 4), round(r * 1.6, 4)])


def pearl(piece, cx, cy, r, color=PEARL):
    m = piece.circle_mask(cx, cy, r)
    piece.paint(dilate(m, piece.units(0.006)), color=INK, alpha=0.8)
    c = np.asarray(color, np.float32)
    piece.paint(m, color=np.clip(c * (1 + 0.18 * pillow(m, piece.units(r * 0.6))[..., None]), 0, 1))
    piece.paint(piece.circle_mask(cx - r * 0.35, cy - r * 0.35, r * 0.3), color=WHITE, alpha=0.85)


def pearl_string(piece, pts, r, gap=0.95):
    """Pearls along a polyline."""
    pts = resample(pts, 2 * r * gap)
    for x, y in pts:
        pearl(piece, x, y, r)


def resample(pts, step):
    out = [pts[0]]
    acc = 0.0
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        seg = math.hypot(x1 - x0, y1 - y0)
        d = step - acc
        while d <= seg:
            k = d / seg
            out.append((x0 + (x1 - x0) * k, y0 + (y1 - y0) * k))
            d += step
        acc = (acc + seg) % step
    return out


def dots(piece, region, spacing, r, grey=None, color=None, alpha=1.0, offset=(0, 0), jitter=0.0, seed=1):
    """Polka dots clipped to `region`."""
    rng = np.random.default_rng(seed)
    m = np.zeros((piece.H, piece.W), np.float32)
    x0 = -piece.origin[0] / piece.ppt
    y0 = -piece.origin[1] / piece.ppt
    x1 = (piece.w - piece.origin[0]) / piece.ppt
    y1 = (piece.h - piece.origin[1]) / piece.ppt
    row = 0
    y = y0 + offset[1]
    img = Image.new("L", (piece.W, piece.H), 0)
    d = ImageDraw.Draw(img)
    while y < y1 + spacing:
        x = x0 + offset[0] + (spacing / 2 if row % 2 else 0)
        while x < x1 + spacing:
            jx, jy = rng.uniform(-jitter, jitter, 2) if jitter else (0, 0)
            cx, cy = piece.px(x + jx, y + jy)
            rr = piece.units(r)
            d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=255)
            x += spacing
        y += spacing * 0.866
        row += 1
    m = np.asarray(img, np.float32) / 255 * region
    piece.paint(m, grey=grey, color=color, alpha=alpha)
    return m


def scatter_shapes(piece, region, count, make, grey=None, color=None, alpha=1.0, seed=3, min_dist=0.0):
    """`count` small shapes (make(x, y, rng) -> points) at random spots inside `region`."""
    rng = np.random.default_rng(seed)
    ys, xs = np.nonzero(region > 0.5)
    if len(xs) == 0:
        return
    placed = []
    tries = 0
    while len(placed) < count and tries < count * 40:
        tries += 1
        i = rng.integers(len(xs))
        x = (xs[i] / SS - piece.origin[0]) / piece.ppt
        y = (ys[i] / SS - piece.origin[1]) / piece.ppt
        if any(math.hypot(x - a, y - b) < min_dist for a, b in placed):
            continue
        placed.append((x, y))
        piece.paint(piece.mask(make(x, y, rng)) * region, grey=grey, color=color, alpha=alpha)
    return placed


def scallops(piece, pts, r, grey=None, color=None, outline=0.008, inward=False):
    """Round bumps along a polyline (ruffles, lace edges, fur)."""
    pts = resample(pts, r * 1.5)
    m = np.zeros((piece.H, piece.W), np.float32)
    for x, y in pts:
        m = np.maximum(m, piece.circle_mask(x, y, r))
    piece.fill(m, grey=grey, color=color, outline=outline)
    return m


def sequins(piece, region, spacing, r, grey_hi=0.98, seed=5):
    """Overlapping shiny discs (tinted): each one lighter at the top left."""
    rng = np.random.default_rng(seed)
    X, Y = piece.grid()
    out = np.zeros_like(X)
    sx = X / spacing
    sy = Y / (spacing * 0.8)
    row = np.floor(sy)
    sx = sx + 0.5 * (row % 2)
    fx = sx - np.floor(sx) - 0.5
    fy = sy - row - 0.5
    d = np.sqrt(fx * fx + (fy * 0.8) ** 2) * spacing
    disc = np.clip((r - d) / (0.15 * r) + 0.5, 0, 1)
    light = np.clip(0.5 - (fx * 0.7 + fy * 0.7), 0, 1)
    cell_rand = (np.sin(np.floor(sx) * 12.9898 + row * 78.233) * 43758.5453) % 1.0
    g = 0.62 + 0.36 * light * (0.6 + 0.4 * cell_rand)
    piece.paint(disc * region, grey=g)
    # a few strong glints the game can twinkle
    ys, xs = np.nonzero((disc * region > 0.9) & (cell_rand > 0.93))
    for k in rng.choice(len(xs), size=min(14, len(xs)), replace=False) if len(xs) else []:
        x = (xs[k] / SS - piece.origin[0]) / piece.ppt
        y = (ys[k] / SS - piece.origin[1]) / piece.ppt
        piece.meta.setdefault("sparkles", []).append([round(x, 4), round(y, 4), round(r * 2.2, 4)])
    return out


def scales(piece, region, size, seed=7):
    """Mermaid scales: rows of overlapping arcs, each lighter in its middle (tinted)."""
    X, Y = piece.grid()
    sy = Y / (size * 0.62)
    row = np.floor(sy)
    sx = X / size + 0.5 * (row % 2)
    fx = sx - np.floor(sx) - 0.5
    fy = sy - row
    d = np.sqrt(fx * fx + (fy * 0.62) ** 2)
    rim = np.clip((d - 0.42) / 0.06, 0, 1)
    g = 0.72 + 0.22 * (1 - fy) * (1 - rim) - 0.18 * rim
    piece.paint(region, grey=np.clip(g, 0, 1))


def sparkle_points(piece, region, count, size, seed=9):
    """Places twinkle points (for the game's glints) inside region, without drawing anything."""
    rng = np.random.default_rng(seed)
    ys, xs = np.nonzero(region > 0.5)
    if len(xs) == 0:
        return
    for k in rng.choice(len(xs), size=min(count, len(xs)), replace=False):
        x = (xs[k] / SS - piece.origin[0]) / piece.ppt
        y = (ys[k] / SS - piece.origin[1]) / piece.ppt
        piece.meta.setdefault("sparkles", []).append([round(x, 4), round(y, 4), round(size, 4)])


def flower(piece, cx, cy, r, petal, center=GOLD, petals=5, rot=0.0, outline=0.007):
    """A little flower: round petals (fixed colour or tinted grey via petal=float) and a centre."""
    m = np.zeros((piece.H, piece.W), np.float32)
    for k in range(petals):
        a = rot + 2 * math.pi * k / petals
        m = np.maximum(m, piece.circle_mask(cx + math.cos(a) * r * 0.55, cy + math.sin(a) * r * 0.55, r * 0.48))
    if isinstance(petal, float):
        piece.fill(m, grey=petal, outline=outline, shade=0.25, shade_radius=r * 0.3)
    else:
        piece.fill(m, color=petal, outline=outline, shade=0.25, shade_radius=r * 0.3)
    piece.fill(piece.circle_mask(cx, cy, r * 0.3), color=center, outline=outline * 0.8, shade=0.3, shade_radius=r * 0.12)


def leaf(piece, x0, y0, x1, y1, width, color, outline=0.007, vein=True):
    """A pointed leaf from (x0, y0) to (x1, y1)."""
    dx, dy = x1 - x0, y1 - y0
    L = math.hypot(dx, dy)
    nx, ny = -dy / L, dx / L
    mid = [(x0 + dx * t + nx * width * math.sin(math.pi * t) * s, y0 + dy * t + ny * width * math.sin(math.pi * t) * s)
           for s in (1,) for t in np.linspace(0, 1, 14)]
    back = [(x0 + dx * t - nx * width * math.sin(math.pi * t), y0 + dy * t - ny * width * math.sin(math.pi * t))
            for t in np.linspace(1, 0, 14)]
    m = piece.mask(mid + back[1:-1])
    if isinstance(color, float):
        piece.fill(m, grey=color, outline=outline, shade=0.3, shade_radius=width * 0.5)
    else:
        piece.fill(m, color=color, outline=outline, shade=0.3, shade_radius=width * 0.5)
    if vein:
        c = (np.asarray(color, np.float32) * 0.7) if not isinstance(color, float) else None
        piece.stroke([(x0 + dx * 0.1, y0 + dy * 0.1), (x0 + dx * 0.85, y0 + dy * 0.85)], width * 0.12,
                     color=c, grey=(color * 0.8 if isinstance(color, float) else None), alpha=0.6, clip=m)
    return m


def bow(piece, cx, cy, r, grey=None, color=None, rot=0.0):
    """A ribbon bow: two loops, two tails, a knot."""
    loop = [(0, 0), (0.55, -0.45), (1.0, -0.3), (1.0, 0.3), (0.55, 0.45)]
    tail = [(0.05, 0.05), (0.45, 0.85), (0.3, 0.95), (0.18, 0.78), (0.0, 0.2)]
    shapes = []
    for side in (1, -1):
        shapes.append(rotate([(cx + x * r * side, cy + y * r) for x, y in catmull(loop, True, 6)], rot, cx, cy))
        shapes.append(rotate([(cx + x * r * side * 0.8, cy + y * r) for x, y in tail], rot, cx, cy))
    for s in shapes[1::2] + shapes[0::2]:
        piece.shape(s, grey=grey, color=color, outline=0.008, shade=0.35, shade_radius=r * 0.25)
    piece.shape(rotate(ellipse_pts(cx, cy, r * 0.24, r * 0.3, 24), rot, cx, cy), grey=grey, color=color,
                outline=0.008, shade=0.4, shade_radius=r * 0.1)


def satin_sheen(piece, region, x0, y0, x1, y1, width, strength=0.12):
    """A soft highlight band (satin, silk) along the line (x0, y0) - (x1, y1): lighter grey, so it stays the fabric's
    colour (the shader turns greys above 0.80 toward white)."""
    X, Y = piece.grid()
    dx, dy = x1 - x0, y1 - y0
    L = math.hypot(dx, dy)
    d = ((X - x0) * dy - (Y - y0) * dx) / L
    t = ((X - x0) * dx + (Y - y0) * dy) / (L * L)
    band = np.exp(-(d / width) ** 2) * np.clip(1 - np.abs(t * 2 - 1), 0, 1) ** 0.5 * region
    piece.shade_inside(band, strength)
