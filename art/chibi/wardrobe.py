"""The chibi's wardrobe: every piece that fits the body (art/chibi/kit.py). Slots (one piece each per character):

    hair    Spiky, Fluffy, Shaggy, Buns, Short (for under hats)
    hat     Cap, CapBack (worn backwards), CapStar, Bucket, Crown, FrogHat, CatHood
    ears    CatEars, BunnyEars, BearEars, DevilHorns, UnicornHorn
    top     Hoodie, HoodieStar, BearHoodie, ZipHoodie, Jacket, TrackJacket, Checkered, Tee, TeeStar
    bottom  Shorts, Pants, Skirt
    shoes   Sneakers, HighTops
    acc     Glasses, RoundGlasses, HeartGlasses, Headphones, StarClip, Mask
    back    AngelWings, DevilWings, CatTail, DevilTail, RaccoonTail, BunnyTail

Colours come from the character (regions: hair / hair_accent, hat / hat_accent, ears / ears_inner, top / top_accent /
top_trim, bottom / bottom_accent, shoes / shoes_accent / soles, acc / acc_accent, back / back_accent).
"""

import math

from mathutils import Matrix, Vector

import body
import kit
from kit import (ANKLE, ARM_DIR, ARM_R, FOREARM, HEAD_C, HIP, LEG_R, SHOULDER, UPPER_ARM, WRIST, Part, bezier,
                 ease, head_normal, head_point, lerp, mirror, superellipse, surface_frame)

D = math.radians


# ---------------------------------------------------------------------------------------------------- shells and strands

def hairline(az, front, side, back):
    """The elevation (radians) of an edge round the head: front (az 0), sides (90 degrees), back (180), smooth."""
    c = math.cos(az)
    return lerp(side, front, ease(c)) if c >= 0 else lerp(side, back, ease(-c))


def shell(p, line, out, inner, region, rows=12, segs=48, lip=None, lip_region=None):
    """A thick cap over the head above an edge line(az): the outer surface `out` off the head, the inner `inner` off it,
    joined by a rounded lip at the edge."""
    rows, segs = kit.lod(rows, 4), kit.lod(segs, 12)

    def ring(el_of, o):
        return [head_point(2 * math.pi * i / segs, el_of(2 * math.pi * i / segs), o) for i in range(segs)]
    top = math.pi / 2 - 0.02
    outer = [ring(lambda az, j=j: lerp(line(az), top, (j / rows) ** 0.9), out) for j in range(rows)]
    inside = [ring(lambda az, j=j: lerp(line(az), top, (j / rows) ** 0.9), inner) for j in range(rows)]
    lip_row = ring(lambda az: line(az) - 0.035, (out + inner) / 2)
    all_rows = list(reversed(inside)) + [lip_row] + outer
    lr = lip_region or region
    n_inner = len(inside)

    def reg(i, j, c):
        return lr if n_inner - 1 <= i <= n_inner else region
    p.grid(all_rows, reg, caps=(head_point(0, math.pi / 2, inner), head_point(0, math.pi / 2, out)))
    return p


def strand(p, az0, el0, az1, el1, width, thick=None, out=0.03, lift=0.0, flick=0.0, n=9, region="hair",
           power=2.2, tip=0.004, bend=None):
    """A clump of hair lying on the head from (az0, el0) to (az1, el1) (radians), `out` off the head, bulging by `lift`
    in the middle and flicking out by `flick` at the tip; flat (width across, thick off the head), tapering to a point.
    bend(t) -> extra azimuth (a curl)."""
    thick = thick or width * 0.45
    n = kit.lod(n, 4)
    pts, fronts, rads = [], [], []
    for k in range(n):
        t = k / (n - 1)
        az = lerp(az0, az1, t) + (bend(t) if bend else 0.0)
        el = lerp(el0, el1, t)
        o = out + lift * math.sin(math.pi * t) + flick * t * t
        pts.append(head_point(az, el, o))
        fronts.append(head_normal(az, el))
        s = (1 - t) ** 0.75 * (0.85 + 0.15 * math.sin(math.pi * min(1.0, t * 2.2)))
        rads.append((max(tip, width * s), max(tip * 0.7, thick * s)))
    # 9 rings of 8 (was 12 of 10: a hairstyle 13.5k triangles; four chibis share the TV box's GPU)
    p.tube(pts, rads, region, segs=8, fronts=fronts, power=power, cap_scale=0.6)
    return p


def star_outline(r_out, r_in, n=5, rot=90.0, round_k=0.0):
    pts = []
    for k in range(n * 2):
        a = D(rot) + math.pi * k / n
        r = r_out if k % 2 == 0 else r_in
        pts.append((math.cos(a) * r, math.sin(a) * r))
    # a little rounding: more points along each edge pulled toward the circle
    if round_k > 0:
        out = []
        for i in range(len(pts)):
            a, b = Vector(pts[i]), Vector(pts[(i + 1) % len(pts)])
            for s in range(3):
                q = a.lerp(b, s / 3)
                out.append((q.x, q.y))
        pts = out
    return pts


def heart_outline(size, n=28):
    pts = []
    for k in range(n):
        t = 2 * math.pi * k / n
        x = 16 * math.sin(t) ** 3
        y = 13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t)
        pts.append((x * size / 17, (y + 2) * size / 17))
    return pts


def on_front(x, z, y=None):
    """A point and frame on the body's front at (x, z): the torso's surface (a top's chest)."""
    cy, hw, hd, pw = kit.torso_section(z)
    u = max(-0.999, min(0.999, x / hw))
    yy = cy - hd * (1 - abs(u) ** pw) ** (1 / pw)
    pt = Vector((x, yy if y is None else y, z))
    n = Vector((x / hw * 0.4, -1.0, 0)).normalized()
    sx, up, nn = surface_frame(n)
    return pt, sx, up, nn


# ---------------------------------------------------------------------------------------------------- hair

def hair_spiky(name="Spiky", fluffy=False, long_sides=False):
    p = Part(f"Hair_{name}")
    thick_k = 0.8 if fluffy else 0.62
    out = 0.045 if fluffy else 0.032
    line = lambda az: hairline(az, D(26), D(-2), D(-30))   # noqa: E731
    shell(p, line, out - 0.004, -0.004, "hair")
    w = 0.10 if fluffy else 0.092
    # crown: clumps falling from near the top all round
    for k in range(14):
        az = D(k * 360 / 14 + 8)
        front = math.cos(az)
        end = hairline(az, D(36), D(10) if not long_sides else D(-22), D(-34))
        strand(p, az, D(78), az + D(10) * math.sin(az * 3), end, w, w * thick_k, out=out, lift=0.03 if fluffy else 0.022,
               flick=0.05 if fluffy else 0.045, bend=lambda t, s=math.sin(az * 2): s * D(8) * t * t)
    # the fringe over the forehead, tips curling to the sides
    for k, az in enumerate([-48, -28, -10, 10, 28, 48]):
        a = D(az)
        drop = D(16) if k in (2, 3) else D(22)
        strand(p, a * 0.6, D(62), a + D(6) * (1 if az > 0 else -1), drop, w * 0.95, w * thick_k * 0.9, out=out + 0.004,
               lift=0.018, flick=0.02 if not fluffy else 0.035,
               bend=lambda t, sgn=(1 if az > 0 else -1): sgn * D(10) * t * t)
    # the sides over the ears and the back
    for side in (1, -1):
        for az in (80, 100, 118):
            a = D(az) * side
            strand(p, a, D(50), a + side * D(6), D(-28) if long_sides else D(-8), w * 0.95, w * thick_k, out=out,
                   lift=0.02, flick=0.05 if fluffy else 0.04)
    for az in (140, 160, 180, 200, 220):
        a = D(az)
        strand(p, a, D(55), a + D(8) * math.sin(a), D(-42) if long_sides else D(-30), w, w * thick_k, out=out,
               lift=0.02, flick=0.045)
    return p


def hair_short():
    """Close-cropped with a short fringe: for under caps and hats."""
    p = Part("Hair_Short")
    line = lambda az: hairline(az, D(30), D(0), D(-28))   # noqa: E731
    shell(p, line, 0.026, -0.004, "hair")
    for az in (-40, -20, 0, 20, 40):
        a = D(az)
        strand(p, a * 0.7, D(55), a, D(24), 0.07, 0.025, out=0.026, lift=0.01, flick=0.012)
    for side in (1, -1):
        strand(p, side * D(80), D(30), side * D(84), D(-8), 0.06, 0.02, out=0.024, flick=0.01)
    return p


def hair_buns():
    """Smooth with a fringe and two round buns on top, short tails hanging under them, hair ties."""
    p = Part("Hair_Buns")
    line = lambda az: hairline(az, D(28), D(-4), D(-26))   # noqa: E731
    shell(p, line, 0.034, -0.004, "hair")
    for az in (-44, -26, -9, 8, 25, 43):
        a = D(az)
        strand(p, a * 0.5, D(66), a + D(4) * (1 if az > 0 else -1), D(18), 0.085, 0.04, out=0.036, lift=0.02, flick=0.02,
               bend=lambda t, sgn=(1 if az > 0 else -1): sgn * D(8) * t * t)
    for side in (1, -1):
        c = head_point(side * D(50), D(62), 0.10)
        n = head_normal(side * D(50), D(62))
        p.ellipsoid(c, (0.10, 0.10, 0.095), axes=(n.cross(Vector((0, 0, 1))).normalized(), Vector((0, 0, 1)).cross(
            n.cross(Vector((0, 0, 1))).normalized()), n), region="hair", segs=20, rings=14)
        tie = head_point(side * D(50), D(62), 0.02)
        p.ring([tie + (n.cross(Vector((0, 0, 1))).normalized() * math.cos(a) + n.cross(n.cross(Vector((0, 0, 1)))).normalized()
                       * math.sin(a)) * 0.058 for a in [2 * math.pi * k / 16 for k in range(16)]], 0.016, "hair_accent")
        # two short tails from under the bun, falling down the side
        for d_az in (-8, 8):
            strand(p, side * D(62 + d_az), D(40), side * D(78 + d_az), D(-36), 0.07, 0.035, out=0.05, lift=0.02, flick=0.05)
    for az in (150, 180, 210):
        strand(p, D(az), D(50), D(az), D(-30), 0.09, 0.04, out=0.036, lift=0.02, flick=0.03)
    return p


def squashed(hair, suffix, line, limit, bvh):
    """A copy of a hairstyle pressed down under a hat: wherever the hat covers (above line(az)), no hair stands more
    than `limit` off the head, so it stays inside the hat (any hat or hood: they are `limit` + 4 mm or more thick)."""
    p = Part(hair.name + suffix)
    p.merge(hair)
    for i, co in enumerate(p.co):
        d = co - HEAD_C
        if d.length < 1e-6:
            continue
        d.normalize()
        el = math.asin(max(-1.0, min(1.0, d.z)))
        az = math.atan2(d.x, -d.y)
        cover = line(az)
        if el < cover - D(6):
            continue
        hit = bvh.ray_cast(HEAD_C, d)[0]
        if hit is None:
            continue
        r_head = (hit - HEAD_C).length
        k = kit.smooth(cover - D(6), cover + D(2), el)
        allowed = lerp(1.0, limit, k)
        off = (co - HEAD_C).length - r_head
        if off > allowed:
            p.co[i] = HEAD_C + d * (r_head + allowed)
    return p


HAT_LINE = lambda az: hairline(az, D(34), D(6), D(-12))    # noqa: E731   caps, bucket, frog hat
HOOD_LINE = lambda az: hairline(az, D(38), D(-52), D(-72))  # noqa: E731   the cat hood


def hairs():
    styles = [hair_spiky("Spiky"), hair_spiky("Fluffy", fluffy=True), hair_spiky("Shaggy", long_sides=True), hair_buns(),
              hair_short()]
    bvh = body.head_bvh()
    out = list(styles)
    for h in styles:
        out.append(squashed(h, "_Hat", HAT_LINE, 0.024, bvh))
        out.append(squashed(h, "_Hood", HOOD_LINE, 0.032, bvh))
    return out


# ---------------------------------------------------------------------------------------------------- hats

def cap(name="Cap", backwards=False, star=False):
    p = Part(name)
    if backwards:
        # built facing back (its brim behind), then turned: its high edge ends up over the face
        line = lambda az: hairline(az, D(-6), D(12), D(38))   # noqa: E731
    else:
        line = lambda az: hairline(az, D(38), D(12), D(-6))   # noqa: E731
    shell(p, line, 0.062, 0.030, "hat", rows=10)
    # the button on top and six seams
    top = head_point(0, math.pi / 2, 0.062)
    p.ellipsoid(top + Vector((0, 0, 0.004)), (0.022, 0.022, 0.012), region="hat_accent", segs=12, rings=6)
    for k in range(6):
        az = D(k * 60 + 30)
        seam = [head_point(az, lerp(line(az), D(88), t / 7), 0.066) for t in range(8)]
        p.tube(seam, 0.004, "hat_accent" if k % 3 == 0 else "hat", segs=6, caps=(False, False))
    # the brim: a curved visor out the front
    rows = [[], [], [], []]
    for k in range(17):
        az = D(lerp(-62, 62, k / 16))
        base = head_point(az, line(az), 0.060)
        nrm = head_normal(az, line(az))
        flat = Vector((nrm.x, nrm.y, 0)).normalized()
        reach = 0.17 * math.cos(az * 1.25) ** 0.6 if abs(az) < D(70) else 0.0
        tip = base + flat * reach + Vector((0, 0, -0.03 * reach / 0.17))
        rows[0].append(base + Vector((0, 0, 0.006)))
        rows[1].append(tip + Vector((0, 0, 0.004)))
        rows[2].append(tip - Vector((0, 0, 0.006)))
        rows[3].append(base - Vector((0, 0, 0.008)))
    p.grid(rows, "hat", closed=False, loop=True)
    if star:
        pt = head_point(0, D(58), 0.064)
        sx, up, nn = surface_frame(head_normal(0, D(58)))
        p.slab(star_outline(0.052, 0.022), pt, sx, up, nn, 0.008, "hat_accent")
    if backwards:
        rot = Matrix.Translation(HEAD_C) @ Matrix.Rotation(math.pi, 4, "Z") @ Matrix.Translation(-HEAD_C)
        p.co = [rot @ c for c in p.co]
    return p


def bucket():
    p = Part("Bucket")
    line = lambda az: hairline(az, D(26), D(18), D(10))   # noqa: E731
    shell(p, line, 0.064, 0.034, "hat", rows=10)
    rows = []
    for depth, out, dz in ((0.0, 0.06, 0.0), (0.06, 0.13, -0.035), (0.10, 0.17, -0.06)):
        rows.append([head_point(D(i * 7.5), line(D(i * 7.5)), out) + Vector((0, 0, dz)) for i in range(48)])
    under = [[q + Vector((0, 0, -0.012)) for q in row] for row in reversed(rows)]
    p.grid(rows + under, "hat", loop=True)
    band = [head_point(D(i * 7.5), line(D(i * 7.5)) + D(7), 0.068) for i in range(48)]
    p.ring(band, (0.022, 0.006), "hat_accent", segs=8)
    return p


def crown():
    p = Part("Crown")
    z0, r_out, r_in = 1.468, 0.205, 0.188
    segs = 60
    rows = []
    for r, top in ((r_in, False), (r_out, False), (r_out, True), (r_in, True)):
        row = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            spike = max(0.0, math.cos(5 * a)) ** 3
            z = z0 + (0.075 + 0.085 * spike if top else 0.0)
            row.append(Vector((math.sin(a) * r, -math.cos(a) * r * 0.95, z)))
        rows.append(row)
    p.grid(rows, "hat", loop=True)
    for k in range(5):
        a = 2 * math.pi * k / 5
        tip = Vector((math.sin(a) * (r_out - 0.008), -math.cos(a) * (r_out - 0.008) * 0.95, z0 + 0.17))
        p.ellipsoid(tip, (0.018, 0.018, 0.018), region="hat", segs=12, rings=8)
        gem = Vector((math.sin(a) * (r_out + 0.004), -math.cos(a) * (r_out + 0.004) * 0.95, z0 + 0.04))
        n = Vector((math.sin(a), -math.cos(a), 0))
        sx, up, nn = surface_frame(n)
        p.ellipsoid(gem, (0.017, 0.017, 0.007), axes=(sx, up, nn), region="hat_accent", segs=12, rings=8)
    return p


def frog_hat():
    p = Part("FrogHat")
    line = lambda az: hairline(az, D(34), D(-8), D(-24))   # noqa: E731
    shell(p, line, 0.068, 0.036, "hat", rows=11)
    for side in (1, -1):
        az, el = side * D(30), D(70)
        c = head_point(az, el, 0.12)
        n = head_normal(az, el)
        p.ellipsoid(c, (0.072, 0.07, 0.068), region="hat", segs=18, rings=12)
        white = c + Vector((0, -0.03, 0.006))
        p.ellipsoid(white, (0.052, 0.045, 0.05), region="hat_accent", segs=16, rings=10)
        p.ellipsoid(white + Vector((side * 0.004, -0.04, 0.0)), (0.024, 0.012, 0.03), region="eyes", segs=12, rings=8)
    mouth = [head_point(D(a), D(44) + 0.0006 * a * a / 40, 0.072) for a in range(-40, 41, 8)]
    p.tube(mouth, 0.007, "mouth", segs=8)
    return p


def cat_hood():
    p = Part("CatHood")
    line = lambda az: hairline(az, D(40), D(-48), D(-70))   # noqa: E731
    shell(p, line, 0.075, 0.040, "hat", rows=12, lip_region="hat")
    for side in (1, -1):
        az, el = side * D(44), D(58)
        base = head_point(az, el, 0.06)
        n = head_normal(az, el)
        up = Vector((side * 0.28, 0, 1)).normalized()
        path = [base - n * 0.01, base + up * 0.07 + n * 0.02, base + up * 0.15 + n * 0.02]
        p.tube(path, [(0.085, 0.04), (0.06, 0.03), (0.008, 0.006)], "hat", segs=12, fronts=[n, n, n], power=2.4,
               cap_scale=0.4)
        inner = [q - n * 0.0 + (Vector((0, -1, 0)) * 0.018) for q in path]
        p.tube(inner, [(0.055, 0.02), (0.036, 0.016), (0.006, 0.004)], "hat_accent", segs=10, fronts=[n, n, n],
               power=2.4, cap_scale=0.4)
    return p


def hats():
    return [cap(), cap("CapBack", backwards=True), cap("CapStar", star=True), bucket(), crown(), frog_hat(), cat_hood()]


# ---------------------------------------------------------------------------------------------------- animal ears and horns

def ears_parts():
    out = []
    p = Part("CatEars")
    for side in (1, -1):
        az, el = side * D(42), D(56)
        scalp = head_point(az, el, 0.0)
        n = head_normal(az, el)
        up = Vector((side * 0.3, -0.05, 1)).normalized()
        base = scalp + n * 0.055
        path = [scalp, base, base + up * 0.08, base + up * 0.17]
        p.tube(path, [(0.075, 0.036), (0.085, 0.038), (0.058, 0.029), (0.008, 0.006)], "ears", segs=12, fronts=[n] * 4,
               power=2.5, cap_scale=0.4)
        p.tube([q + Vector((0, -0.022, 0)) for q in path[1:]], [(0.054, 0.016), (0.034, 0.013), (0.005, 0.004)],
               "ears_inner", segs=10, fronts=[n] * 3, power=2.5, cap_scale=0.4)
    out.append(p)

    p = Part("BunnyEars")
    for side in (1, -1):
        az, el = side * D(22), D(72)
        base = head_point(az, el, 0.025)
        path = bezier([base, base + Vector((side * 0.03, 0.01, 0.16)), base + Vector((side * 0.09, 0.03, 0.36))], 10)
        rads = [(0.04 + 0.022 * math.sin(math.pi * k / 9), 0.022) for k in range(10)]
        p.tube(path, rads, "ears", segs=12, front=Vector((0, -1, 0)), cap_scale=0.9)
        p.tube([q + Vector((0, -0.016, 0)) for q in path[1:]], [(r[0] * 0.55, 0.008) for r in rads[1:]], "ears_inner",
               segs=10, front=Vector((0, -1, 0)), cap_scale=0.9)
    out.append(p)

    p = Part("BearEars")
    for side in (1, -1):
        az, el = side * D(48), D(55)
        c = head_point(az, el, 0.085)
        n = head_normal(az, el)
        p.tube([head_point(az, el, 0.0), c], 0.045, "ears", segs=12, caps=(False, False))
        sx = Vector((0, 0, 1)).cross(n).normalized()
        up = n.cross(sx).normalized()
        p.ellipsoid(c, (0.072, 0.068, 0.036), axes=(sx, up, Vector((0, -1, 0))), region="ears", segs=18, rings=10)
        p.ellipsoid(c + Vector((0, -0.024, 0)), (0.042, 0.04, 0.016), axes=(sx, up, Vector((0, -1, 0))),
                    region="ears_inner", segs=14, rings=8)
    out.append(p)

    p = Part("DevilHorns")
    for side in (1, -1):
        az, el = side * D(36), D(52)
        base = head_point(az, el, 0.02)
        n = head_normal(az, el)
        path = bezier([base - n * 0.02, base + n * 0.06 + Vector((side * 0.02, 0, 0.03)),
                       base + n * 0.1 + Vector((side * 0.06, 0.02, 0.1)), base + Vector((side * 0.07, 0.06, 0.2))], 12)
        rads = [0.045 * (1 - k / 11) ** 0.8 + 0.004 for k in range(12)]
        p.tube(path, rads, lambda i, j, c: "ears_inner" if (c - base).length > 0.13 else "ears", segs=12, cap_scale=0.5)
    out.append(p)

    p = Part("UnicornHorn")
    base = head_point(0, D(48), 0.0)
    n = head_normal(0, D(48))
    d = (n + Vector((0, 0, 0.9))).normalized()
    path = [base + d * (0.24 * k / 12) for k in range(13)]
    rads = [0.05 * (1 - k / 12) + 0.003 for k in range(13)]
    p.tube(path, rads, lambda i, j, c: "ears" if int((c - base).length / 0.03 + j / 3) % 2 == 0 else "ears_inner",
           segs=16, cap_scale=0.4)
    out.append(p)
    return out


# ---------------------------------------------------------------------------------------------------- tops

def torso_rows(off, z0, z1, steps=14, segs=32, region_fn=None):
    steps = kit.lod(steps, 4)
    rows = []
    for k in range(steps):
        z = lerp(z0, z1, k / (steps - 1))
        cy, hw, hd, pw = kit.torso_section(z)
        flare = 0.012 * max(0.0, 1 - (z - z0) / 0.06) if z0 < 0.55 else 0.0
        rows.append(superellipse(Vector((0, cy, z)), Vector((1, 0, 0)), Vector((0, 1, 0)), hw + off + flare,
                                 hd + off + flare, max(2.0, pw - 0.2), segs))
    return rows


def loop_at(off, z, segs=32, lift=0.0):
    cy, hw, hd, pw = kit.torso_section(z)
    return superellipse(Vector((0, cy, z + lift)), Vector((1, 0, 0)), Vector((0, 1, 0)), hw + off, hd + off,
                        max(2.0, pw - 0.2), segs)


def sleeve(p, side, length, r0, r1, region="top", cuff="top_trim", stripes=None, checker=None):
    """A sleeve over the arm from inside the shoulder to `length` along it (UPPER_ARM + FOREARM = the wrist)."""
    d = mirror(ARM_DIR, side)
    sh = mirror(SHOULDER, side)
    path = [sh - d * 0.035 + Vector((0, 0, 0.012)), sh + d * 0.05, sh + d * (length * 0.6), sh + d * length]
    rads = [r0 + 0.006, r0, lerp(r0, r1, 0.6), r1]
    reg = region
    if checker:
        reg = lambda i, j, c: checker(c)   # noqa: E731
    p.tube(path, rads, reg, segs=20, caps=(True, False), weights=kit.arm_weights(side))
    end = sh + d * length
    fr = kit.frame(d)
    p.ring(superellipse(end, fr[0], fr[1], r1 - 0.004, r1 - 0.004, 2.0, 20), 0.014, cuff, segs=8,
           front=d, weights=kit.arm_weights(side))
    if stripes:
        for off in (-0.022, 0.022):
            # along the outside of the sleeve
            out = Vector((side, 0, 0.35)).normalized()
            out = (out - d * out.dot(d)).normalized()
            fwd = d.cross(out).normalized()
            line = [sh + d * (0.03 + (length - 0.04) * k / 9) + out * (lerp(r0, r1, k / 9) + 0.001) + fwd * off
                    for k in range(10)]
            p.tube(line, (0.006, 0.0035), stripes, segs=6, caps=(False, False), front=out, weights=kit.arm_weights(side))


def hood(p, region="top", inner="top_trim"):
    """A hood lying round the back of the neck, open at the front."""
    pts = []
    for k in range(15):
        a = D(lerp(-120, 120, k / 14))
        pts.append(Vector((math.sin(a) * 0.15, math.cos(a) * 0.13 + 0.035, 0.885 + 0.02 * math.cos(a))))
    rads = [(0.05 + 0.025 * math.cos(D(lerp(-120, 120, k / 14)) * 0.5), 0.045) for k in range(15)]
    p.tube(pts, rads, region, segs=14, front=Vector((0, 0, 1)), weights=kit.W(Spine=1.0))


def hoodie(name="Hoodie", star=False, bear=False, zip_=False):
    p = Part(name, kit.torso_weights)
    p.grid(torso_rows(0.024, 0.475, 0.872), "top", weights=kit.torso_weights)
    p.ring(loop_at(0.03, 0.478), (0.02, 0.012), "top_trim", segs=8)       # the hem band
    p.ring(loop_at(0.016, 0.868), (0.012, 0.012), "top_trim", segs=8)     # the neckline
    for side in (1, -1):
        sleeve(p, side, UPPER_ARM + FOREARM - 0.012, ARM_R + 0.026, ARM_R + 0.034)
    hood(p)
    if not zip_:
        # the pocket on the belly, its edge a shade darker
        pt, sx, up, nn = on_front(0.0, 0.565)
        outline = [(-0.115, -0.05), (0.115, -0.05), (0.09, 0.045), (-0.09, 0.045)]
        fine = []
        for i in range(4):
            a, b = Vector(outline[i]), Vector(outline[(i + 1) % 4])
            for s in range(6):
                q = a.lerp(b, s / 6)
                fine.append((q.x, q.y))
        p.slab(fine, pt + nn * 0.02, sx, up, nn, 0.01, "top", side_region="top_trim")
        # drawstrings
        for side in (1, -1):
            a, _, _, n0 = on_front(side * 0.045, 0.84)
            path = [a + n0 * 0.035, a + n0 * 0.04 + Vector((side * 0.004, 0, -0.07)), a + n0 * 0.04 + Vector((side * 0.006, 0, -0.12))]
            p.tube(path, 0.007, "top_trim", segs=8, weights=kit.W(Spine=1.0))
    else:
        for z in [0.49 + k * 0.025 for k in range(15)]:
            pt, sx, up, nn = on_front(0.0, z)
            p.ellipsoid(pt + nn * 0.025, (0.009, 0.007, 0.004), axes=(sx, up, nn), region="top_trim", segs=8, rings=4)
        pt, sx, up, nn = on_front(0.0, 0.84)
        p.slab([(-0.012, -0.03), (0.012, -0.03), (0.012, 0.0), (-0.012, 0.0)], pt + nn * 0.026, sx, up, nn, 0.006, "top_trim")
    if star:
        pt, sx, up, nn = on_front(0.0, 0.70)
        p.slab(star_outline(0.072, 0.03), pt + nn * 0.02, sx, up, nn, 0.01, "top_accent")
    if bear:
        pt, sx, up, nn = on_front(0.0, 0.70)
        base = pt + nn * 0.02
        circle = lambda r, n=24: [(math.cos(2 * math.pi * k / n) * r, math.sin(2 * math.pi * k / n) * r) for k in range(n)]  # noqa: E731
        p.slab(circle(0.068), base, sx, up, nn, 0.008, "top_accent")
        for side in (1, -1):
            p.slab(circle(0.026), base + sx * side * 0.052 + up * 0.05 - nn * 0.002, sx, up, nn, 0.008, "top_accent")
            p.ellipsoid(base + sx * side * 0.026 + up * 0.014 + nn * 0.009, (0.008, 0.009, 0.004), axes=(sx, up, nn),
                        region="eyes", segs=10, rings=6)
        p.slab([(math.cos(2 * math.pi * k / 20) * 0.03, math.sin(2 * math.pi * k / 20) * 0.022 - 0.022) for k in range(20)],
               base + nn * 0.008, sx, up, nn, 0.004, "top_trim")
        p.ellipsoid(base + up * -0.012 + nn * 0.014, (0.011, 0.008, 0.005), axes=(sx, up, nn), region="eyes", segs=10, rings=6)
    return p


def checker_region(cell=0.06):
    def f(c):
        u = math.atan2(c.x, -(c.y - 0.0)) * 0.19 / cell
        v = c.z / cell
        return "top" if (math.floor(u) + math.floor(v)) % 2 == 0 else "top_accent"
    return f


def jacket(name="Jacket", stripes=False, checker=False):
    p = Part(name, kit.torso_weights)
    reg = (lambda i, j, c: checker_region()(c)) if checker else "top"   # noqa: E731
    p.grid(torso_rows(0.026, 0.478, 0.86), reg, weights=kit.torso_weights)
    p.ring(loop_at(0.032, 0.48), (0.02, 0.012), "top_trim", segs=8)
    # an upright collar
    collar = [loop_at(0.018, 0.85 + 0.03 * k / 3) for k in range(4)]
    p.grid(collar, "top_trim")
    p.ring(loop_at(0.02, 0.882), 0.01, "top_trim", segs=8)
    for side in (1, -1):
        sleeve(p, side, UPPER_ARM + FOREARM - 0.012, ARM_R + 0.024, ARM_R + 0.028,
               region="top_accent" if not checker else "top", stripes="top_trim" if stripes else None,
               checker=checker_region() if checker else None)
    # the zip down the front
    line = []
    for k in range(12):
        z = lerp(0.49, 0.85, k / 11)
        pt, sx, up, nn = on_front(0.0, z)
        line.append(pt + nn * 0.03)
    p.tube(line, (0.006, 0.004), "top_trim", segs=6, caps=(False, False), front=Vector((0, -1, 0)))
    return p


def tee(name="Tee", star=False, collar=False):
    p = Part(name, kit.torso_weights)
    p.grid(torso_rows(0.02, 0.49, 0.874), "top", weights=kit.torso_weights)
    p.ring(loop_at(0.024, 0.492), (0.012, 0.008), "top", segs=8)
    p.ring(loop_at(0.014, 0.872), (0.012, 0.01), "top_trim", segs=8)
    for side in (1, -1):
        sleeve(p, side, UPPER_ARM * 0.95, ARM_R + 0.03, ARM_R + 0.034, cuff="top_trim")
    if star:
        pt, sx, up, nn = on_front(0.0, 0.68)
        p.slab(star_outline(0.075, 0.032), pt + nn * 0.018, sx, up, nn, 0.008, "top_accent")
    if collar:
        for side in (1, -1):
            pt, sx, up, nn = on_front(side * 0.05, 0.85)
            p.slab([(0, 0.02), (side * 0.07, 0.01), (side * 0.03, -0.05)] if side > 0 else
                   [(0, 0.02), (side * 0.03, -0.05), (side * 0.07, 0.01)], pt + nn * 0.02, sx, up, nn, 0.008, "top_trim",
                   centre=(side * 0.03, -0.005))
    return p


def tops():
    return [hoodie(), hoodie("HoodieStar", star=True), hoodie("BearHoodie", bear=True), hoodie("ZipHoodie", zip_=True),
            jacket(), jacket("TrackJacket", stripes=True), jacket("Checkered", checker=True), tee(),
            tee("TeeStar", star=True), tee("Polo", collar=True)]


# ---------------------------------------------------------------------------------------------------- bottoms

def bottoms():
    out = []
    for name, knee_z, r_hem in (("Shorts", 0.33, LEG_R + 0.036), ("Pants", 0.15, LEG_R + 0.022)):
        p = Part(name, kit.lower_weights)
        rows = torso_rows(0.018, 0.448, 0.56, steps=7, segs=32)
        p.grid(rows, "bottom", caps=(Vector((0, 0.004, 0.43)), False), weights=kit.lower_weights)
        p.ring(loop_at(0.012, 0.552), (0.014, 0.008), "bottom_accent", segs=8, weights=kit.W(Hips=1.0))
        for side in (1, -1):
            hip = mirror(HIP, side)
            path = [hip + Vector((0, 0, 0.04)), hip, Vector((hip.x, 0.0, lerp(hip.z, knee_z, 0.5))), Vector((hip.x, 0.0, knee_z))]
            p.tube(path, [LEG_R + 0.03, LEG_R + 0.03, lerp(LEG_R + 0.03, r_hem, 0.5), r_hem], "bottom", segs=18,
                   caps=(True, False), weights=kit.leg_weights(side))
            end = Vector((hip.x, 0.0, knee_z))
            p.ring(superellipse(end, Vector((1, 0, 0)), Vector((0, 1, 0)), r_hem - 0.003, r_hem - 0.003, 2.0, 18),
                   (0.012, 0.009), "bottom_accent" if name == "Shorts" else "bottom", segs=8,
                   front=Vector((0, 0, -1)), weights=kit.leg_weights(side))
        out.append(p)
    p = Part("Skirt", kit.W(Hips=1.0))
    rows = []
    for k in range(6):
        t = k / 5
        z = lerp(0.56, 0.36, t)
        cy, hw, hd, pw = kit.torso_section(max(z, 0.47))
        grow = 0.02 + 0.12 * t
        rows.append([q + Vector((0, 0, 0.012 * math.cos(16 * math.atan2(q.x, q.y)) * t)) for q in
                     superellipse(Vector((0, cy, z)), Vector((1, 0, 0)), Vector((0, 1, 0)), hw + grow, hd + grow, 2.2, 48,
                                  keep=True)])   # 16 waves round the hem: fewer points alias them
    p.grid(rows, lambda i, j, c: "bottom_accent" if i == 4 else "bottom", weights=kit.W(Hips=1.0))
    p.grid(torso_rows(0.016, 0.448, 0.56, steps=5, segs=32), "bottom", caps=(Vector((0, 0.004, 0.43)), False))
    p.ring(loop_at(0.012, 0.552), (0.014, 0.008), "bottom_accent", segs=8)
    out.append(p)
    return out


# ---------------------------------------------------------------------------------------------------- shoes

SHOE = [   # y (forward is -), half width, top height
    (0.090, 0.050, 0.10), (0.075, 0.068, 0.15), (0.040, 0.080, 0.155), (0.000, 0.084, 0.150), (-0.050, 0.087, 0.118),
    (-0.100, 0.088, 0.098), (-0.150, 0.083, 0.088), (-0.190, 0.070, 0.078), (-0.215, 0.048, 0.066),
]


def shoe(p, side, high=False):
    x0 = mirror(ANKLE, side).x
    rows = []
    fine = []
    for k in range(len(SHOE) - 1):
        a, b = SHOE[k], SHOE[k + 1]
        sub = kit.lod(3, 1)
        for s_ in range(sub):
            t = s_ / sub
            fine.append(tuple(lerp(a[i], b[i], t) for i in range(3)))
    fine.append(SHOE[-1])
    for y, hw, h in fine:
        if high and y > -0.03:
            h += 0.08 * min(1.0, (y + 0.03) / 0.06)
        c = Vector((x0, y, h / 2))
        rows.append([Vector((q.x, q.y, max(0.0, q.z))) for q in
                     superellipse(c, Vector((1, 0, 0)), Vector((0, 0, 1)), hw, h / 2 + 0.006, 3.0, 40)])

    def reg(i, j, c):
        if c.z < 0.032:
            return "soles"
        if c.y < -0.145 and c.z < 0.07:
            return "shoes_accent"
        if c.z > 0.085 and -0.11 < c.y < 0.01 and abs(c.x - x0) < 0.045:
            return "shoes_accent"
        if high and c.z > 0.06 and c.y > -0.02 and abs(abs(c.x - x0) - 0.07) < 0.025 and abs(c.z - 0.12) < 0.03:
            return "shoes_accent"
        return "shoes"
    p.grid(rows, reg, caps=(Vector((x0, 0.098, 0.07)), Vector((x0, -0.224, 0.034))), weights=kit.foot_weights(side))
    # laces: three little bars across the tongue
    for k in range(3):
        y = -0.085 + k * 0.032
        top = [r for r in SHOE if abs(r[0] - y) < 0.06]
        h = lerp(0.10, 0.15, (y + 0.1) / 0.12) + 0.005
        p.tube([Vector((x0 - 0.032, y, h - 0.004)), Vector((x0, y - 0.004, h + 0.006)), Vector((x0 + 0.032, y, h - 0.004))],
               0.0075, "shoes", segs=6, weights=kit.foot_weights(side))


def shoes_parts():
    out = []
    for name, high in (("Sneakers", False), ("HighTops", True)):
        p = Part(name)
        for side in (1, -1):
            shoe(p, side, high)
        out.append(p)
    return out


# ---------------------------------------------------------------------------------------------------- accessories

def glasses(name, shape):
    bvh = body.head_bvh()
    p = Part(name)
    rims = []
    for side in (1, -1):
        cx = side * body.EYE_X
        pts = []
        n = 28
        for k in range(n):
            a = 2 * math.pi * k / n
            if shape == "square":
                x, z = kit.spow(math.cos(a), 0.45) * 0.068, kit.spow(math.sin(a), 0.45) * 0.054
            elif shape == "round":
                x, z = math.cos(a) * 0.064, math.sin(a) * 0.064
            else:
                hx, hz = heart_outline(0.07, n)[k]
                x, z = hx, hz - 0.006
            pts.append(body.on_face(bvh, cx + x, body.EYE_Z + z, 0.032)[0])
        p.ring(pts, 0.0095 if shape != "square" else (0.012, 0.009), "acc", segs=8, front=Vector((0, -1, 0)))
        rims.append(pts)
    # the bridge and the arms back to the ears
    a = body.on_face(bvh, -body.EYE_X + 0.066, body.EYE_Z + 0.02, 0.036)[0]
    b = body.on_face(bvh, body.EYE_X - 0.066, body.EYE_Z + 0.02, 0.036)[0]
    p.tube(bezier([a, (a + b) / 2 + Vector((0, -0.006, 0.008)), b], 7), 0.007, "acc", segs=6)
    for side in (1, -1):
        start = body.on_face(bvh, side * (body.EYE_X + 0.068), body.EYE_Z + 0.02, 0.03)[0]
        ear = head_point(side * D(86), D(-6), 0.03)
        p.tube(bezier([start, head_point(side * D(60), D(-2), 0.035), ear], 8), 0.007, "acc", segs=6)
    return p


def headphones():
    p = Part("Headphones")
    band = []
    for k in range(25):
        t = k / 24
        el = D(lerp(-2, 90, min(1.0, t * 2))) if t <= 0.5 else D(lerp(90, -2, (t - 0.5) * 2))
        az = D(90) if t <= 0.5 else D(-90)
        band.append(head_point(az, el, 0.088))
    p.tube(band, (0.024, 0.012), "acc", segs=10, front=Vector((0, 1, 0)), caps=(False, False))
    for side in (1, -1):
        c = head_point(side * D(90), D(-8), 0.06)
        n = Vector((side, 0, 0))
        p.tube([c - n * 0.02, c + n * 0.035], [(0.085, 0.085), (0.08, 0.08)], "acc", segs=24, front=Vector((0, -1, 0)))
        p.tube([c - n * 0.045, c - n * 0.02], [(0.07, 0.07), (0.072, 0.072)], "acc_accent", segs=24, front=Vector((0, -1, 0)))
        p.slab([(math.cos(2 * math.pi * k / 20) * 0.04, math.sin(2 * math.pi * k / 20) * 0.04) for k in range(20)],
               c + n * 0.06, Vector((0, side, 0)), Vector((0, 0, 1)), n, 0.006, "acc_accent")
    return p


def star_clip():
    p = Part("StarClip")
    az, el = D(-52), D(48)
    pt = head_point(az, el, 0.06)
    sx, up, nn = surface_frame(head_normal(az, el))
    p.slab(star_outline(0.07, 0.03, round_k=1), pt, sx, up, nn, 0.026, "acc")
    return p


def mask():
    bvh = body.head_bvh()
    p = Part("Mask")
    rows = []
    for k in range(7):
        z = lerp(0.955, 1.075, k / 6)
        w = 0.14 - 0.03 * (k / 6) ** 2
        rows.append([body.on_face(bvh, lerp(-w, w, i / 16), z, 0.012 + 0.006 * math.sin(math.pi * i / 16))[0]
                     for i in range(17)])
    back = [[q + (q - HEAD_C).normalized() * -0.004 for q in row] for row in rows]
    p.grid(rows, "acc", closed=False)
    p.grid(back, "acc", closed=False)
    for side in (1, -1):
        a = body.on_face(bvh, side * 0.14, 1.04, 0.012)[0]
        p.tube(bezier([a, head_point(side * D(60), D(-8), 0.008), head_point(side * D(84), D(-6), 0.01)], 8), 0.006, "acc", segs=6)
    return p


def accessories():
    return [glasses("Glasses", "square"), glasses("RoundGlasses", "round"), glasses("HeartGlasses", "heart"),
            headphones(), star_clip(), mask()]


# ---------------------------------------------------------------------------------------------------- wings and tails

BACK = Vector((0, 0.145, 0.70))


def wings_angel():
    p = Part("AngelWings", kit.W(Spine=1.0))
    for side in (1, -1):
        root = BACK + Vector((side * 0.06, 0.0, 0.05))
        for k, (ang, length, width) in enumerate(((30, 0.30, 0.075), (5, 0.27, 0.07), (-20, 0.22, 0.065), (-42, 0.16, 0.055))):
            a = D(ang)
            d = Vector((side * math.cos(a), 0.35, math.sin(a))).normalized()
            c = root + d * length * 0.55 + Vector((0, 0.02 * k, 0))
            up = Vector((0, 0, 1)).cross(d).normalized() if abs(d.z) < 0.95 else Vector((1, 0, 0))
            p.ellipsoid(c, (length * 0.55, width, 0.022), axes=(d, d.cross(Vector((0, 1, 0))).normalized(), Vector((0, 1, 0))),
                        region="back" if k < 3 else "back_accent", segs=18, rings=10)
        p.ellipsoid(root + Vector((side * 0.06, 0.015, 0.05)), (0.09, 0.06, 0.03), region="back", segs=16, rings=10)
    return p


def wings_devil():
    p = Part("DevilWings", kit.W(Spine=1.0))
    for side in (1, -1):
        root = BACK + Vector((side * 0.05, 0.01, 0.06))
        sx = Vector((side, 0.3, 0.0)).normalized()
        up = Vector((0, 0, 1))
        nn = sx.cross(up).normalized()
        # the membrane: a scalloped bat wing in the wing's plane
        outline = [(0.0, 0.05), (0.12, 0.13), (0.26, 0.17), (0.34, 0.10), (0.29, 0.04), (0.25, -0.05), (0.19, 0.0),
                   (0.13, -0.07), (0.08, -0.01), (0.03, -0.05)]
        p.slab(outline, root, sx, up, nn, 0.012, "back_accent", side_region="back", centre=(0.14, 0.05))
        for a, b in (((0.0, 0.05), (0.12, 0.13)), ((0.12, 0.13), (0.26, 0.17)), ((0.26, 0.17), (0.34, 0.10)),
                     ((0.12, 0.13), (0.25, -0.05)), ((0.12, 0.13), (0.13, -0.07))):
            pa = root + sx * a[0] + up * a[1] + nn * 0.006
            pb = root + sx * b[0] + up * b[1] + nn * 0.006
            p.tube([pa, pb], 0.011, "back", segs=8, cap_scale=0.8)
    return p


def tail(name, path, rads, region_fn, weights=kit.W(Hips=1.0)):
    p = Part(name, weights)
    n = kit.lod(18, 6)
    pts = bezier(path, n)
    p.tube(pts, [rads(k / (n - 1)) for k in range(n)], region_fn, segs=16, cap_scale=0.9)
    return p


def tails():
    root = Vector((0, 0.13, 0.52))
    cat = tail("CatTail", [root, root + Vector((0.04, 0.12, -0.03)), root + Vector((0.10, 0.22, 0.08)),
                           root + Vector((0.08, 0.25, 0.25)), root + Vector((0.02, 0.22, 0.34))],
               lambda t: 0.038 - 0.006 * t, "back")
    devil = tail("DevilTail", [root, root + Vector((-0.05, 0.14, -0.05)), root + Vector((-0.12, 0.26, 0.04)),
                               root + Vector((-0.12, 0.30, 0.18)), root + Vector((-0.06, 0.28, 0.26))],
                 lambda t: 0.018, "back")
    end = root + Vector((-0.06, 0.28, 0.26))
    d = (end - (root + Vector((-0.12, 0.30, 0.18)))).normalized()
    sx = d.cross(Vector((0, 1, 0))).normalized()
    devil.slab([(0.0, 0.0), (0.05, -0.012), (0.0, 0.07), (-0.05, -0.012)], end - d * 0.01, sx, d, sx.cross(d).normalized(),
               0.016, "back_accent", side_region="back", centre=(0.0, 0.015))
    raccoon = tail("RaccoonTail", [root, root + Vector((0.03, 0.15, -0.05)), root + Vector((0.08, 0.30, 0.0)),
                                   root + Vector((0.10, 0.38, 0.12))],
                   lambda t: 0.055 + 0.03 * math.sin(math.pi * min(1.0, t * 1.3)),
                   lambda i, j, c: "back_accent" if int(((c - root).length) / 0.06) % 2 == 1 else "back")
    bunny = Part("BunnyTail", kit.W(Hips=1.0))
    c = root + Vector((0, 0.06, -0.02))
    for k, off in enumerate([(0, 0, 0), (0.035, 0.01, 0.025), (-0.035, 0.01, 0.025), (0.0, 0.02, 0.05), (0.03, 0.025, -0.02),
                             (-0.03, 0.025, -0.02)]):
        bunny.ellipsoid(c + Vector(off), (0.05, 0.045, 0.048), region="back", segs=14, rings=10)
    return [cat, devil, raccoon, bunny]


def backs():
    return [wings_angel(), wings_devil()] + tails()


# ---------------------------------------------------------------------------------------------------- everything

def all_pieces(bvh):
    return {"hair": hairs(), "hat": hats(), "ears": ears_parts(), "top": tops(), "bottom": bottoms(),
            "shoes": shoes_parts(), "acc": accessories(), "back": backs()}


FACE = ["Head", "Body", "Eyes", "Mouth"]

# previews: the reference sheet's four characters, the plain one, and a few more
LOOKS = [
    (FACE + ["Tee", "Pants", "Sneakers"],
     {"top": "#F7F5F9", "top_trim": "#E9E6EE", "bottom": "#F7F5F9", "bottom_accent": "#E9E6EE", "shoes": "#FFFFFF",
      "shoes_accent": "#EEEEF2", "soles": "#E0DDE6"}),
    (FACE + ["Hair_Short_Hat", "CapBack", "TrackJacket", "Shorts", "Sneakers"],
     {"hair": "#2A1A14", "hat": "#E8403A", "hat_accent": "#FFFFFF", "top": "#E8403A", "top_accent": "#E8403A",
      "top_trim": "#FFFFFF", "bottom": "#24305E", "bottom_accent": "#24305E", "shoes": "#E8403A",
      "shoes_accent": "#FFFFFF"}),
    (FACE + ["Hair_Fluffy_Hood", "CatHood", "HoodieStar", "Shorts", "Sneakers"],
     {"hair": "#FF8FC0", "hat": "#FFF6F8", "hat_accent": "#FFB3C7", "top": "#FF9AC4", "top_accent": "#FFFFFF",
      "top_trim": "#FFE3EE", "bottom": "#FF9AC4", "bottom_accent": "#FFB8D6", "shoes": "#FFB8D6",
      "shoes_accent": "#FFFFFF"}),
    (FACE + ["Hair_Shaggy", "DevilHorns", "DevilWings", "DevilTail", "TeeStar", "Skirt", "HighTops"],
     {"hair": "#A27BFF", "ears": "#5B2E8C", "ears_inner": "#2E1450", "back": "#3A1E5C", "back_accent": "#7B4FB5",
      "top": "#26203A", "top_accent": "#A27BFF", "top_trim": "#3A3054", "bottom": "#7B4FB5", "bottom_accent": "#26203A",
      "shoes": "#26203A", "shoes_accent": "#FFFFFF"}),
    (FACE + ["Hair_Spiky", "Headphones", "Jacket", "Pants", "Sneakers"],
     {"hair": "#7A4526", "acc": "#2457C8", "acc_accent": "#1B1726", "top": "#2E6FE8", "top_accent": "#1E1A2C",
      "top_trim": "#1E1A2C", "bottom": "#1E1A2C", "bottom_accent": "#1E1A2C", "shoes": "#2E6FE8",
      "shoes_accent": "#FFFFFF"}),
    (FACE + ["Hair_Buns", "RoundGlasses", "BearHoodie", "Shorts", "Sneakers", "BearEars"],
     {"hair": "#2A1A3A", "hair_accent": "#FF5A8A", "acc": "#D4A836", "top": "#FFF1DC", "top_accent": "#9A6234",
      "top_trim": "#E8C9A0", "ears": "#9A6234", "ears_inner": "#E8C9A0", "bottom": "#9A6234", "shoes": "#FFF1DC"}),
    (FACE + ["Hair_Spiky", "Crown", "Checkered", "Pants", "HighTops", "AngelWings"],
     {"hair": "#F2D35C", "hat": "#F6C33B", "hat_accent": "#E8403A", "top": "#1E1A2C", "top_accent": "#F7F5F9",
      "top_trim": "#1E1A2C", "bottom": "#3A3E5C", "shoes": "#F7F5F9", "shoes_accent": "#1E1A2C"}),
    (FACE + ["Hair_Fluffy_Hat", "FrogHat", "ZipHoodie", "Shorts", "Sneakers", "RaccoonTail"],
     {"hair": "#F2F2F7", "hat": "#5DB04E", "hat_accent": "#FFFFFF", "top": "#4E9A44", "top_trim": "#2F6A2A",
      "bottom": "#2F6A2A", "back": "#8A7D78", "back_accent": "#3A3438", "shoes": "#2F6A2A"}),
]
