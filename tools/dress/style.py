"""Fashion-illustration shading for Vishu!'s wardrobe (draw.py draws the shapes; this makes them look like cloth).

The clip-art look of the first wardrobe came from thick dark outlines and "pillow" shading (every shape lit the same
way along its edges, flat inside). Fashion games (the user's references, 2026-11-09: Fashion Diva-like dress-up games,
Stardoll) shade clothes like an illustrator:

  * form:      a body is a cylinder, a skirt a cone: light on the side toward the light (top left), a soft terminator,
               the far side in shade, a little reflected light at the very edge (`cylinder`, `cone`, `lit`)
  * folds:     gathered cloth makes ridges and valleys that catch the light; valleys get a crisp darker line
               (`folds`, the height field's curvature in `lit`)
  * shine:     satin and silk get narrow white highlights along the ridges (specular in `lit`)
  * shadows:   a layer casts a soft shadow on the one below (a bodice on its skirt, a belt, a collar: `cast_shadow`)
  * outlines:  thin, in a deep shade of the fabric's own colour, a little heavier on the shadow side (`outline`)
  * texture:   glitter that sparkles where the light falls, sequins in rows, tulle, lace (`glitter`, `sequin_rows`,
               `tulle`, `lace_band`)

Tinted greys (the player chooses the colour in the game) keep draw.py's encoding: 0.80 = the colour itself, darker =
its shade, lighter = toward white. The shader (Tundu/Outfit `Shade`) and `shade_rgb` here turn shades into a deeper,
more saturated, slightly cool version of the colour (as an illustrator mixes a shadow), not just a darker one.
"""
import math

import cv2
import numpy as np

from draw import BASE, SS, Piece, blur, catmull, dilate, ellipse_pts, erode, resample, star_pts  # noqa: F401

# light from the top left and a little in front (image coordinates: x right, y down, z toward the viewer)
_L = np.array([-0.50, -0.62, 0.60], np.float32)
LIGHT3 = _L / np.linalg.norm(_L)
HALF = (LIGHT3 + np.array([0, 0, 1], np.float32))
HALF = HALF / np.linalg.norm(HALF)


# ---------------------------------------------------------------------------------------------------- colour

def shade_rgb(tint, g):
    """The shader's colouring of a tinted grey (Tundu/Outfit Shade): g 0.8 = the tint; below, toward a deep,
    saturated, slightly cool shade of it; above, toward white. tint: (3,) or (..., 3); g: (...) or (..., 1)."""
    tint = np.asarray(tint, np.float32)
    g = np.asarray(g, np.float32)
    if g.ndim and g.shape[-1] != 1:
        g = g[..., None]
    k = np.clip(g / 0.8, 0, 1)
    dark = np.clip(tint * tint * 0.6 + np.array([-0.02, -0.01, 0.03], np.float32), 0, 1)
    base = dark + (tint - dark) * k
    hi = np.clip((g - 0.8) / 0.2, 0, 1)
    return base + (1 - base) * hi * 0.75


def deeper(color, k=0.55):
    """A fixed colour's outline / shadow shade: darker, a little more saturated and cooler."""
    c = np.asarray(color, np.float32)
    d = np.clip(c * c * 0.6 + np.array([-0.02, -0.01, 0.03], np.float32), 0, 1)
    return tuple(float(x) for x in d + (c - d) * (1 - k))


def lighter(color, k=0.35):
    c = np.asarray(color, np.float32)
    return tuple(float(x) for x in c + (1 - c) * k)


# ---------------------------------------------------------------------------------------------------- height fields
# Heights are in supersampled pixels (so their gradients are slopes); a height field shaped like the cloth gives the
# normals the light needs.

def _rows_extent(m):
    """Per row of a mask: (left, right) column of its covered part, -1 where the row is empty."""
    on = m > 0.5
    any_row = on.any(1)
    left = np.where(any_row, on.argmax(1), -1)
    right = np.where(any_row, m.shape[1] - 1 - on[:, ::-1].argmax(1), -1)
    return left, right


def cylinder(m, depth=0.9, smooth=6, flat=None):
    """A body-like form: each row of the shape bulges toward the viewer like a cylinder across its own width. The bulge
    is the same height on every row (`depth` times the shape's median half width, or `flat` supersampled px), so a
    widening skirt isn't tilted toward the light as a whole; its wider rows just turn more gently."""
    left, right = _rows_extent(m)
    h, w = m.shape
    mid = (left + right) / 2.0
    half = np.maximum((right - left) / 2.0, 1.0)
    rows = np.nonzero(left >= 0)[0]
    if len(rows) == 0:
        return np.zeros_like(m)
    if len(rows) > 3 and smooth:
        n = smooth * SS
        k = np.ones(2 * n + 1) / (2 * n + 1)
        mid[rows] = np.convolve(np.pad(mid[rows], n, mode="edge"), k, mode="valid")
        half[rows] = np.convolve(np.pad(half[rows], n, mode="edge"), k, mode="valid")
    bulge = flat if flat is not None else depth * float(np.median(half[rows]))
    xs = np.arange(w, dtype=np.float32)[None, :]
    u = (xs - mid[:, None]) / half[:, None]
    hgt = np.sqrt(np.clip(1 - u * u, 0, 1)) * bulge
    hgt[left < 0] = 0
    return (hgt * (m > 0.02)).astype(np.float32)


def dome(piece, m, radius):
    """A rounded edge: rises over `radius` (template units) from the outline, flat inside."""
    R = max(piece.units(radius), 1.0)
    out = np.zeros(m.shape, np.float32)
    box = box_of(m, 4)
    if box is None:
        return out
    d = cv2.distanceTransform((m[box] > 0.5).astype(np.uint8), cv2.DIST_L2, 5)
    t = np.clip(d / R, 0, 1)
    out[box] = R * (1 - (1 - t) ** 2)
    return out


class Folds:
    """A fold field: heights (supersampled px) and how deep each point sits in a valley (0..1), for `lit`."""

    def __init__(self, h, depth_px):
        self.h = h.astype(np.float32)
        self.depth = max(depth_px, 1e-3)

    def __add__(self, other):
        return Folds(self.h + other.h, max(self.depth, other.depth))

    def cavity(self, piece, width=0.05):
        """0..1: below the folds' local mean (in the valleys)."""
        local = blur(self.h, piece.units(width))
        return np.clip((local - self.h) / (0.5 * self.depth), 0, 1)


def fan_folds(piece, m, focus, count, amplitude, y0, y1, seed=1, wobble=0.35, grow=1.0, sharp=1.6):
    """Gathered folds fanning out from `focus` (template units, above the waist) across the shape `m`: `count` of them
    over its width, rounded ridges and narrower valleys, growing from y0 to y1 (amplitude in template units)."""
    rng = np.random.default_rng(seed)
    X, Y = piece.grid()
    ang = np.arctan2(X - focus[0], Y - focus[1])
    inside = ang[m > 0.5]
    lo, hi = (float(np.percentile(inside, 1)), float(np.percentile(inside, 99))) if inside.size else (-1.0, 1.0)
    u = (ang - lo) / max(hi - lo, 1e-3)                      # 0..1 across the skirt
    warp = np.zeros_like(u)
    for k in range(1, 4):
        warp += rng.uniform(-1, 1) * wobble / k * np.sin(u * math.pi * (2 + 3 * k) + rng.uniform(0, 6.28))
    # uneven widths: a smooth random stretch of u
    phase = (u + warp / count) * count * 2 * math.pi + rng.uniform(0, 6.28)
    ridge = (0.5 + 0.5 * np.cos(phase)) ** sharp
    t = np.clip((Y - y0) / max(1e-3, y1 - y0), 0, 1) ** grow
    amp = piece.units(amplitude)
    return Folds(amp * ridge * (0.12 + 0.88 * t), amp)


def hanging_folds(piece, x0, x1, count, amplitude, y0, y1, seed=2, wobble=0.3, sharp=1.5, grow=0.7):
    """Folds hanging straight down (a sleeve, a cape, a straight skirt): `count` across x0..x1."""
    rng = np.random.default_rng(seed)
    X, Y = piece.grid()
    u = (X - x0) / max(1e-3, x1 - x0)
    warp = sum(rng.uniform(-1, 1) * wobble / k * np.sin(Y * (3 + 2 * k) + rng.uniform(0, 6.28)) for k in range(1, 3))
    ridge = (0.5 + 0.5 * np.cos((u * count + warp) * 2 * math.pi)) ** sharp
    t = np.clip((Y - y0) / max(1e-3, y1 - y0), 0, 1)
    amp = piece.units(amplitude)
    return Folds(amp * ridge * ((1 - grow) + grow * t), amp)


def gather_folds(piece, center, count, amplitude, radius, seed=3, sharp=1.4):
    """Folds radiating from a gathered point (a puff sleeve's cuff, a bow's knot): strongest near `center`."""
    rng = np.random.default_rng(seed)
    X, Y = piece.grid()
    ang = np.arctan2(Y - center[1], X - center[0])
    r = np.hypot(X - center[0], Y - center[1])
    phase = ang * count + 0.6 * np.sin(ang * 3 + rng.uniform(0, 6.28))
    ridge = (0.5 + 0.5 * np.cos(phase)) ** sharp
    falloff = np.exp(-(r / max(radius, 1e-3)) ** 2)
    amp = piece.units(amplitude)
    return Folds(amp * ridge * (0.25 + 0.75 * falloff), amp)


# ---------------------------------------------------------------------------------------------------- light

def normals(h, smooth=1.5):
    hs = blur(h, smooth * SS) if smooth else h
    gy, gx = np.gradient(hs)
    nz = np.ones_like(hs)
    n = np.sqrt(gx * gx + gy * gy + 1)
    return -gx / n, -gy / n, nz / n


def lit(form, m, folds=None, base=BASE, up=0.055, down=0.21, soft=1.6, spec=0.0, shininess=30.0, valley=0.0,
        rim=0.035, piece=None, cavity_width=0.05):
    """A grey field for a tinted fabric: `base` where it faces the viewer, a little lighter toward the light (`up`),
    darker away from it (`down`, a soft terminator: `soft`), narrow white highlights (`spec`: satin), darker valleys
    between folds (`valley`) and a touch of reflected light at the shadow-side edge (`rim`). form: the big shape's
    height field (cylinder, dome); folds: a Folds field on top. Only meaningful where m > 0."""
    h = form if folds is None else form + folds.h
    nx, ny, nz = normals(h)
    d = nx * LIGHT3[0] + ny * LIGHT3[1] + nz * LIGHT3[2]
    shade = np.tanh((d - LIGHT3[2]) * soft * 2.0)
    g = base + np.where(shade > 0, up * shade, down * shade)
    if spec:
        s = np.clip(nx * HALF[0] + ny * HALF[1] + nz * HALF[2], 0, 1) ** shininess
        g = g + spec * s
    if valley and folds is not None and piece is not None:
        g = g - valley * folds.cavity(piece, cavity_width)
    if rim:
        edge = np.clip(1 - cv2.distanceTransform((m > 0.5).astype(np.uint8), cv2.DIST_L2, 3) / (3 * SS), 0, 1)
        away = np.clip(-(nx * LIGHT3[0] + ny * LIGHT3[1]) * 2, 0, 1)
        g = g + rim * edge * away
    return np.clip(g, 0, 1).astype(np.float32)


def lit_color(color, form, m, folds=None, up=0.06, down=0.22, soft=1.6, spec=0.0, shininess=30.0, valley=0.0,
              piece=None):
    """A fixed colour shaded like `lit`: an H x W x 3 array."""
    g = lit(form, m, folds, BASE, up, down, soft, spec, shininess, valley, 0.03, piece)
    return shade_rgb(np.asarray(color, np.float32), g)


# ---------------------------------------------------------------------------------------------------- painting

def box_of(m, pad):
    """The slices around where mask m is (with `pad` supersampled pixels around it), or None if it's empty."""
    on = m > 0.002
    rows = np.flatnonzero(on.any(1))
    if len(rows) == 0:
        return None
    cols = np.flatnonzero(on.any(0))
    pad = int(pad) + 2
    return (slice(max(0, rows[0] - pad), min(m.shape[0], rows[-1] + pad + 1)),
            slice(max(0, cols[0] - pad), min(m.shape[1], cols[-1] + pad + 1)))


def outline(piece, m, width=0.006, grey=0.34, color=None, alpha=0.95, heavy=1.7):
    """A thin outline behind `m`, heavier on the shadow side (bottom right): grey for tinted fabrics (a deep shade of
    their own colour in the game), or a fixed colour."""
    w = piece.units(width)
    box = box_of(m, w * (heavy + 1.5))
    if box is None:
        return None
    mc = m[box]
    o = dilate(mc, w)
    shift = max(1, int(round(w * (heavy - 1))))
    o = np.maximum(o, np.roll(np.roll(o, shift, 0), shift, 1))
    o = np.clip(o - mc, 0, 1)
    if color is not None:
        piece.paint(o, color=color, alpha=alpha, under=True, box=box)
    else:
        piece.paint(o, grey=grey, alpha=alpha, under=True, box=box)
    return o


def inner_line(piece, pts, width=0.004, grey=0.45, color=None, alpha=0.7, clip=None, closed=False):
    """A thin line inside a shape (a seam, the edge of a fold, a strand), softened a little; drawn in its own box."""
    from PIL import Image, ImageDraw
    p = [piece.px(x, y) for x, y in (list(pts) + ([pts[0]] if closed else []))]
    w = max(1, int(round(piece.units(width))))
    b = piece._box(p, w + 4)
    if b is None:
        return None
    x0, y0, x1, y1 = b
    img = Image.new("L", (x1 - x0, y1 - y0), 0)
    d = ImageDraw.Draw(img)
    q = [(qx - x0, qy - y0) for qx, qy in p]
    d.line(q, fill=255, width=w, joint="curve")
    r = w / 2
    for c in (q[0], q[-1]):
        d.ellipse([c[0] - r, c[1] - r, c[0] + r, c[1] + r], fill=255)
    m = blur(np.asarray(img, np.float32) / 255, 0.6 * SS)
    box = (slice(y0, y1), slice(x0, x1))
    if clip is not None:
        m = m * clip[box]
    piece.paint(m, grey=grey if color is None else None, color=color, alpha=alpha, box=box)
    return m


def darken(piece, region, amount, box=None):
    """Darkens what is painted inside `region` (0..1) by `amount`: tinted greys go down (a deeper shade in the game),
    fixed colours toward their deeper shade. With `box`, region is that part of the cell already."""
    if box is None:
        box = box_of(region, 0)
        if box is None:
            return
        region = region[box]
    A = piece.A[box]
    a = np.clip(A, 1e-4, None)
    tint = np.clip(piece.M[box] / a, 0, 1)
    rgb = piece.P[box] / a[..., None]
    k = np.clip(region * amount, 0, 1)
    grey_new = rgb[..., 0] * (1 - k * 0.55)
    fixed = rgb * (1 - k[..., None] * 0.45)
    fixed = fixed * (1 - k[..., None] * 0.15) + (fixed * fixed) * (k[..., None] * 0.15)
    out = fixed * (1 - tint[..., None]) + np.repeat(grey_new[..., None], 3, 2) * tint[..., None]
    piece.P[box] = out * A[..., None]


def brighten(piece, region, amount):
    """Lightens what is painted inside `region` (a highlight, a sheen)."""
    box = box_of(region, 0)
    if box is None:
        return
    A = piece.A[box]
    rgb = piece.P[box] / np.clip(A, 1e-4, None)[..., None]
    k = np.clip(region[box] * amount, 0, 1)[..., None]
    piece.P[box] = (rgb + (1 - rgb) * k) * A[..., None]


def cast_shadow(piece, occluder, receiver=None, dx=0.004, dy=0.016, softness=0.012, amount=0.32):
    """A soft shadow of `occluder` (a mask) on what is under it (`receiver`, default: everything painted), offset
    down-right, not on the occluder itself."""
    sx, sy = int(round(piece.units(dx))), int(round(piece.units(dy)))
    soft = piece.units(softness)
    box = box_of(occluder, abs(sx) + abs(sy) + 3 * soft)
    if box is None:
        return
    occ = occluder[box]
    s = np.roll(np.roll(occ, sy, 0), sx, 1)
    if sy > 0:
        s[:sy] = 0
    elif sy < 0:
        s[sy:] = 0
    if sx > 0:
        s[:, :sx] = 0
    elif sx < 0:
        s[:, sx:] = 0
    s = blur(s, soft)
    s = s * (1 - np.clip(occ * 1.2, 0, 1))
    if receiver is not None:
        s = s * receiver[box]
    darken(piece, s, amount, box=box)


def box_of(m, pad):
    """The slices around where mask m is (with `pad` supersampled pixels around it), or None if it's empty."""
    on = m > 0.002
    rows = np.flatnonzero(on.any(1))
    if len(rows) == 0:
        return None
    cols = np.flatnonzero(on.any(0))
    pad = int(pad) + 2
    return (slice(max(0, rows[0] - pad), min(m.shape[0], rows[-1] + pad + 1)),
            slice(max(0, cols[0] - pad), min(m.shape[1], cols[-1] + pad + 1)))


def outline(piece, m, width=0.006, grey=0.34, color=None, alpha=0.95, heavy=1.7):
    """A thin outline behind `m`, heavier on the shadow side (bottom right): grey for tinted fabrics (a deep shade of
    their own colour in the game), or a fixed colour."""
    w = piece.units(width)
    box = box_of(m, w * (heavy + 1.5))
    if box is None:
        return None
    mc = m[box]
    o = dilate(mc, w)
    shift = max(1, int(round(w * (heavy - 1))))
    o = np.maximum(o, np.roll(np.roll(o, shift, 0), shift, 1))
    o = np.clip(o - mc, 0, 1)
    if color is not None:
        piece.paint(o, color=color, alpha=alpha, under=True, box=box)
    else:
        piece.paint(o, grey=grey, alpha=alpha, under=True, box=box)
    return o


def inner_line(piece, pts, width=0.004, grey=0.45, color=None, alpha=0.7, clip=None, closed=False):
    """A thin line inside a shape (a seam, the edge of a fold, a strand), softened a little; drawn in its own box."""
    from PIL import Image, ImageDraw
    p = [piece.px(x, y) for x, y in (list(pts) + ([pts[0]] if closed else []))]
    w = max(1, int(round(piece.units(width))))
    b = piece._box(p, w + 4)
    if b is None:
        return None
    x0, y0, x1, y1 = b
    img = Image.new("L", (x1 - x0, y1 - y0), 0)
    d = ImageDraw.Draw(img)
    q = [(qx - x0, qy - y0) for qx, qy in p]
    d.line(q, fill=255, width=w, joint="curve")
    r = w / 2
    for c in (q[0], q[-1]):
        d.ellipse([c[0] - r, c[1] - r, c[0] + r, c[1] + r], fill=255)
    m = blur(np.asarray(img, np.float32) / 255, 0.6 * SS)
    box = (slice(y0, y1), slice(x0, x1))
    if clip is not None:
        m = m * clip[box]
    piece.paint(m, grey=grey if color is None else None, color=color, alpha=alpha, box=box)
    return m


def darken(piece, region, amount, box=None):
    """Darkens what is painted inside `region` (0..1) by `amount`: tinted greys go down (a deeper shade in the game),
    fixed colours toward their deeper shade. With `box`, region is that part of the cell already."""
    if box is None:
        box = box_of(region, 0)
        if box is None:
            return
        region = region[box]
    A = piece.A[box]
    a = np.clip(A, 1e-4, None)
    tint = np.clip(piece.M[box] / a, 0, 1)
    rgb = piece.P[box] / a[..., None]
    k = np.clip(region * amount, 0, 1)
    grey_new = rgb[..., 0] * (1 - k * 0.55)
    fixed = rgb * (1 - k[..., None] * 0.45)
    fixed = fixed * (1 - k[..., None] * 0.15) + (fixed * fixed) * (k[..., None] * 0.15)
    out = fixed * (1 - tint[..., None]) + np.repeat(grey_new[..., None], 3, 2) * tint[..., None]
    piece.P[box] = out * A[..., None]


def brighten(piece, region, amount):
    """Lightens what is painted inside `region` (a highlight, a sheen)."""
    box = box_of(region, 0)
    if box is None:
        return
    A = piece.A[box]
    rgb = piece.P[box] / np.clip(A, 1e-4, None)[..., None]
    k = np.clip(region[box] * amount, 0, 1)[..., None]
    piece.P[box] = (rgb + (1 - rgb) * k) * A[..., None]


def cast_shadow(piece, occluder, receiver=None, dx=0.004, dy=0.016, softness=0.012, amount=0.32):
    """A soft shadow of `occluder` (a mask) on what is under it (`receiver`, default: everything painted), offset
    down-right, not on the occluder itself."""
    sx, sy = int(round(piece.units(dx))), int(round(piece.units(dy)))
    s = np.roll(np.roll(occluder, sy, 0), sx, 1)
    if sy > 0:
        s[:sy] = 0
    if sx > 0:
        s[:, :sx] = 0
    s = blur(s, piece.units(softness))
    s = s * (1 - np.clip(occluder * 1.2, 0, 1))
    if receiver is not None:
        s = s * receiver
    darken(piece, s, amount)


def _crop(x, box):
    return None if x is None else (x[box] if np.ndim(x) == 2 else x)


def fabric(piece, m, form=None, folds=None, base=BASE, up=0.055, down=0.21, soft=1.6, spec=0.0, shininess=30.0,
           valley=0.0, line=0.006, line_grey=0.34, alpha=1.0, rim=0.035):
    """A tinted fabric: shaded from its form (default: a cylinder) and folds, with an outline. Returns the grey field
    (full size, 0 outside the shape's box)."""
    box = box_of(m, piece.units(line) * 3 + piece.units(0.03))
    out = np.zeros(m.shape, np.float32)
    if box is None:
        return out
    mc = m[box]
    fc = cylinder(mc) if form is None else _crop(form, box)
    flc = None if folds is None else Folds(folds.h[box], folds.depth)
    g = lit(fc, mc, flc, base, up, down, soft, spec, shininess, valley, rim, piece)
    if line:
        outline(piece, m, line, grey=line_grey, alpha=alpha)
    piece.paint(mc, grey=g, alpha=alpha, box=box)
    out[box] = g
    return out


def solid(piece, m, color, form=None, folds=None, up=0.07, down=0.24, soft=1.6, spec=0.0, shininess=30.0, line=0.006,
          line_color=None, alpha=1.0, valley=0.0):
    """A fixed-colour part (gold, skin, a gem's setting) shaded the same way. Returns the mask."""
    box = box_of(m, piece.units(line) * 3 + piece.units(0.03))
    if box is None:
        return m
    mc = m[box]
    fc = dome(piece, mc, 0.03) if form is None else _crop(form, box)
    flc = None if folds is None else Folds(folds.h[box], folds.depth)
    c = lit_color(color, fc, mc, flc, up, down, soft, spec, shininess, valley, piece)
    if line:
        outline(piece, m, line, color=line_color if line_color is not None else deeper(color), alpha=alpha)
    piece.paint(mc, color=c, alpha=alpha, box=box)
    return m


# ---------------------------------------------------------------------------------------------------- textures

def glitter(piece, region, density=900, size=0.0045, seed=11, lightness=None, tinted=True, flares=0.04,
            twinkles=10, strength=1.0):
    """Glitter: many tiny bright flecks, a few bigger ones with a four-point flare, more and brighter where the light
    falls (`lightness`: a 0..1 field, e.g. the fabric's grey). density = flecks per square torso length."""
    rng = np.random.default_rng(seed)
    ys, xs = np.nonzero(region > 0.5)
    if len(xs) == 0:
        return
    area = (region > 0.5).sum() / (piece.ppt * SS) ** 2
    n = int(density * area)
    pick = rng.choice(len(xs), size=min(n, len(xs)), replace=False)
    dots = np.zeros((piece.H, piece.W), np.float32)
    flare_layer = np.zeros_like(dots)
    r0 = piece.units(size)
    for k in pick:
        x, y = int(xs[k]), int(ys[k])
        lv = 1.0 if lightness is None else float(lightness[y, x])
        if rng.random() > 0.35 + 0.65 * lv:
            continue
        s = r0 * (0.5 + rng.pareto(3.0) * 0.6)
        v = (0.55 + 0.45 * rng.random()) * (0.6 + 0.4 * lv)
        cv2.circle(dots, (x, y), max(1, int(round(s))), float(v), -1, lineType=cv2.LINE_AA)
        if rng.random() < flares * (0.5 + lv):
            L = int(s * rng.uniform(4, 7))
            cv2.line(flare_layer, (x - L, y), (x + L, y), float(v), max(1, int(s * 0.35)), cv2.LINE_AA)
            cv2.line(flare_layer, (x, y - L), (x, y + L), float(v), max(1, int(s * 0.35)), cv2.LINE_AA)
    sparkle = np.clip(dots + blur(flare_layer, 0.8 * SS) * 1.3, 0, 1) * region * strength
    if tinted:
        a = np.clip(piece.A, 1e-4, None)
        g = piece.P[..., 0] / a
        piece.paint(sparkle, grey=np.clip(np.maximum(g, 0.80) + 0.2, 0, 1))
    else:
        piece.paint(sparkle, color=(1.0, 1.0, 1.0))
    # points the game twinkles (gems and sequins too)
    big = np.nonzero((dots * region > 0.8))
    if len(big[0]) and twinkles:
        for k in rng.choice(len(big[0]), size=min(twinkles, len(big[0])), replace=False):
            x = (big[1][k] / SS - piece.origin[0]) / piece.ppt
            y = (big[0][k] / SS - piece.origin[1]) / piece.ppt
            piece.meta.setdefault("sparkles", []).append([round(float(x), 4), round(float(y), 4), round(size * 6, 4)])


def sequin_rows(piece, region, size=0.022, seed=5, lightness=None, angle=0.0, tinted=True, color=None):
    """Sequins in overlapping rows (like scales, each disc a little cup catching the light at its own tilt): a lit
    upper rim, a dark lower edge where the next row overlaps. lightness (0..1 field) lifts the ones in the light."""
    rng = np.random.default_rng(seed)
    X, Y = piece.grid()
    c, s = math.cos(angle), math.sin(angle)
    U, V = X * c + Y * s, -X * s + Y * c
    sy = V / (size * 0.72)
    row = np.floor(sy)
    sx = U / size + 0.5 * (row % 2)
    col = np.floor(sx)
    fx = sx - col - 0.5
    fy = sy - row - 0.5
    d = np.sqrt(fx * fx + (fy * 0.72) ** 2)
    disc = np.clip((0.5 - d) / 0.06, 0, 1)
    rnd = (np.sin(col * 12.9898 + row * 78.233) * 43758.5453) % 1.0
    rnd2 = (np.sin(col * 39.346 + row * 11.135) * 24634.6345) % 1.0
    tilt = (rnd - 0.5) * 1.6
    glint = np.clip(1 - np.hypot(fx + 0.12 + tilt * 0.1, fy + 0.18) * 2.4, 0, 1) ** 2
    lv = 0.6 if lightness is None else lightness
    tone = 0.62 + 0.20 * (rnd2 - 0.5) + 0.22 * glint * (0.5 + rnd) + 0.18 * (lv - 0.5)
    tone = tone - 0.22 * np.clip((fy - 0.25) / 0.25, 0, 1)            # the lower edge in the next row's shadow
    tone = np.clip(tone + 0.25 * (rnd > 0.9) * glint, 0, 1)
    m = disc * region
    if tinted:
        piece.paint(m, grey=tone)
    else:
        piece.paint(m, color=shade_rgb(np.asarray(color, np.float32), tone))
    ys, xs = np.nonzero((m > 0.9) & (rnd > 0.97) & (glint > 0.5))
    for k in rng.choice(len(xs), size=min(12, len(xs)), replace=False) if len(xs) else []:
        x = (xs[k] / SS - piece.origin[0]) / piece.ppt
        y = (ys[k] / SS - piece.origin[1]) / piece.ppt
        piece.meta.setdefault("sparkles", []).append([round(float(x), 4), round(float(y), 4), round(size * 1.6, 4)])


def tulle(piece, m, base=0.88, alpha=0.5, folds=None, mesh=0.009, edge=0.005, seed=3):
    """Sheer tulle: see-through where it is one layer, denser where it gathers (the folds' ridges), a fine dotted mesh
    and a slightly stronger edge."""
    a = np.full(m.shape, alpha, np.float32)
    g = np.full(m.shape, base, np.float32)
    if folds is not None:
        f = folds.h / max(folds.depth, 1e-3)
        a = a * (0.45 + 0.9 * f)
        g = g + 0.07 * (f - 0.5)
    X, Y = piece.grid()
    dotsm = ((np.sin(X / mesh * math.pi) * np.sin(Y / mesh * math.pi)) > 0.9).astype(np.float32)
    g = g + 0.05 * dotsm
    rim = np.clip(m - erode(m, piece.units(edge)), 0, 1)
    a = np.clip(a + rim * 0.3, 0, 0.95)
    piece.paint(m * a, grey=np.clip(g, 0, 1))
    return m


def lace_band(piece, pts, width=0.03, scallop=0.018, grey=None, color=(1.0, 1.0, 1.0), alpha=0.95, holes=True):
    """A lace trim along a polyline: a band with a scalloped lower edge and little eyelets."""
    band = piece.line_mask(pts, width, round_caps=False)
    sc = np.zeros_like(band)
    for x, y in resample(pts, scallop * 1.4):
        sc = np.maximum(sc, piece.circle_mask(x, y + width * 0.5, scallop * 0.75))
    m = np.clip(band + sc, 0, 1)
    if holes:
        for x, y in resample(pts, scallop * 1.4):
            m = m * (1 - piece.circle_mask(x, y + width * 0.15, scallop * 0.22))
    if color is not None:
        outline(piece, m, 0.0035, color=deeper(color, 0.35), alpha=0.6)
        piece.paint(m, color=lit_color(color, dome(piece, m, 0.008), m, up=0.04, down=0.12), alpha=alpha)
    else:
        outline(piece, m, 0.0035, grey=0.5, alpha=0.6)
        piece.paint(m, grey=lit(dome(piece, m, 0.008), m, None, grey, 0.03, 0.10), alpha=alpha)
    return m


# ---------------------------------------------------------------------------------------------------- details

def gem(piece, cx, cy, r, color, cut="round", sparkle=True):
    """A cut gem: a gold setting, facets (darker below, a bright table), a white glint."""
    c = np.asarray(color, np.float32)
    n = 8 if cut == "round" else 6
    pts = ellipse_pts(cx, cy, r, r * (1.15 if cut == "drop" else 1.0), n=n, a0=math.pi / n)
    if cut == "heart":
        from draw import heart_pts
        pts = heart_pts(cx, cy, r)
    m = piece.mask(pts)
    setting = dilate(m, piece.units(r * 0.22))
    solid(piece, setting, (1.0, 0.80, 0.30), form=dome(piece, setting, r * 0.2), spec=0.5, line=0.004)
    # facets: a ring of darker / lighter wedges, a table in the middle
    X, Y = piece.grid()
    ang = np.arctan2(Y - cy, X - cx)
    facet = np.cos(ang * n + 0.3)
    side = np.clip(-(np.cos(ang) * LIGHT3[0] + np.sin(ang) * LIGHT3[1]), -1, 1)
    tone = np.clip(0.55 + 0.25 * facet * 0.5 - 0.25 * side, 0.2, 1)[..., None]
    col = np.clip(c * (0.45 + tone) + 0.1 * (tone > 0.75), 0, 1)
    piece.paint(m, color=col)
    table = piece.mask(ellipse_pts(cx - r * 0.08, cy - r * 0.1, r * 0.5, r * 0.5, n=n, a0=math.pi / n))
    piece.paint(table * m, color=np.clip(c * 1.15 + 0.12, 0, 1), alpha=0.9)
    piece.paint(piece.mask(ellipse_pts(cx - r * 0.32, cy - r * 0.36, r * 0.2, r * 0.13, 12)), color=(1, 1, 1), alpha=0.95)
    if sparkle:
        piece.meta.setdefault("sparkles", []).append([round(cx - r * 0.3, 4), round(cy - r * 0.35, 4), round(r * 1.8, 4)])
    return m


def pearl(piece, cx, cy, r, color=(0.98, 0.96, 1.0)):
    m = piece.circle_mask(cx, cy, r)
    solid(piece, m, color, form=dome(piece, m, r), spec=0.55, shininess=40, line=0.003,
          line_color=(0.6, 0.55, 0.7))
    piece.paint(piece.circle_mask(cx - r * 0.35, cy - r * 0.38, r * 0.28), color=(1, 1, 1), alpha=0.9)


def sparkle_star(piece, cx, cy, r, color=(1, 1, 1), alpha=1.0, glow=True):
    """A four-point star flare (a drawn twinkle)."""
    m = piece.mask(star_pts(cx, cy, r, 0.18, 4, rot=0.0))
    if glow:
        piece.paint(blur(piece.circle_mask(cx, cy, r * 0.5), piece.units(r * 0.4)), color=color, alpha=0.5 * alpha)
    piece.paint(m, color=color, alpha=alpha)
