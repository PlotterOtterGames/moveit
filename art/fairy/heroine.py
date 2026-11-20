"""Zana!'s fairies (2026-11-15, the user: "tall thin fairies like in winx and other fairy cartoons", "more vibrant"): a
slender heroine about 7 heads tall: long legs, a narrow waist, slim arms, a small face with big lashed eyes, big hair,
fashionable outfits. Original designs. She has the chibis' bone names and colour regions (art/chibi/kit.py), so Unity's
Chibi runtime merges, colours and poses her the same way: exported to Resources/Chibi/Heroine.fbx (ChibiArtImport,
ChibiMaterial).

    pieces    Head (+ ears, nose, brows, blush), Body (neck, torso, arms, hands, legs, feet)
    face      Eyes, EyesHappy, Mouth, MouthOpen, MouthSad (shown and hidden by their bones, like the chibis')
    hair      Hair_Wavy (long, down her back), Hair_Pony (a high ponytail), Hair_Bob (chin length: the pixies)
    top       CropTop (straps, a gem), Bodice (a sweetheart neckline, laces, a peplum)
    bottom    MiniSkirt (scalloped, an underlayer), PetalSkirt (two layers of pointed petals)
    shoes     Boots (knee high, a cuff), hat Tiara, acc Choker

Regions: skin, blush (cheeks), eyes (the iris), eye_shine (eye whites, highlights), mouth (lips), tongue (the mouth's
inside, the smile line), ears (lashes, brows, pupils), hair / hair_accent, top / top_accent / top_trim, bottom /
bottom_accent, shoes / shoes_accent / soles, hat / hat_accent, acc / acc_accent.
"""

import json
import math
import os
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "chibi"))
sys.path.insert(0, os.path.join(HERE, "..", "tools"))

import kit  # noqa: E402
import tundu_art as ta  # noqa: E402
import tundu_rig as tr  # noqa: E402
from kit import Part, W, lerp, mirror, side_name, smooth, superellipse  # noqa: E402

D = math.radians
PIECES = {}

# ---------------------------------------------------------------------------------------------------- proportions
# metres, Z up, she faces -Y; 1.70 m to the top of her head

HEAD_C = Vector((0.0, 0.006, 1.562))
SHOULDER = Vector((0.142, 0.010, 1.338))
ARM_DIR = Vector((math.sin(D(9)), 0.0, -math.cos(D(9))))
UPPER_ARM, FOREARM = 0.265, 0.235
ELBOW = SHOULDER + ARM_DIR * UPPER_ARM
WRIST = ELBOW + ARM_DIR * FOREARM
HAND_C = WRIST + ARM_DIR * 0.045
HIP = Vector((0.074, 0.004, 0.905))
KNEE = Vector((0.068, -0.006, 0.495))
ANKLE = Vector((0.062, 0.014, 0.075))
TOE = Vector((0.062, -0.118, 0.012))
EYE_Z = HEAD_C.z - 0.013
EYE_X = 0.0375

TORSO = [   # z, half width, half depth, centre y, power
    (0.840, 0.060, 0.050, 0.004, 2.0),
    (0.880, 0.106, 0.070, 0.004, 2.3),
    (0.930, 0.121, 0.078, 0.006, 2.5),
    (0.990, 0.112, 0.072, 0.006, 2.5),
    (1.060, 0.084, 0.060, 0.004, 2.4),
    (1.110, 0.078, 0.057, 0.002, 2.4),
    (1.170, 0.089, 0.063, -0.002, 2.5),
    (1.230, 0.103, 0.070, -0.005, 2.6),
    (1.290, 0.111, 0.066, -0.002, 2.6),
    (1.330, 0.126, 0.058, 0.006, 2.5),
    (1.360, 0.112, 0.050, 0.008, 2.4),
    (1.385, 0.072, 0.042, 0.008, 2.2),
    (1.400, 0.040, 0.036, 0.008, 2.0),
]


def section(z):
    t = TORSO
    if z <= t[0][0]:
        return t[0][3], t[0][1], t[0][2], t[0][4]
    if z >= t[-1][0]:
        return t[-1][3], t[-1][1], t[-1][2], t[-1][4]
    for i in range(len(t) - 1):
        if t[i][0] <= z <= t[i + 1][0]:
            k = kit.ease((z - t[i][0]) / (t[i + 1][0] - t[i][0]))
            a, b = t[i], t[i + 1]
            return lerp(a[3], b[3], k), lerp(a[1], b[1], k), lerp(a[2], b[2], k), lerp(a[4], b[4], k)


def torso_ring(z, off, segs=40, keep=False):
    cy, hw, hd, pw = section(z)
    return superellipse(Vector((0, cy, z)), Vector((1, 0, 0)), Vector((0, 1, 0)), hw + off, hd + off, pw, segs, keep=keep)


def head_point(az, el, out=0.0):
    """Her head at azimuth az (0 = the face, + toward her left) and elevation el (radians), `out` outward: an egg, the
    skull fuller at the back, the face a little flat, narrowing to a small chin that sits forward."""
    ce, se = math.cos(el), math.sin(el)
    ca, sa = math.cos(az), math.sin(az)
    below = max(0.0, -se)
    rx = 0.086 * (1.0 - 0.42 * below ** 1.5)
    ry = lerp(0.110, 0.088, kit.ease((ca + 0.35) / 0.7)) * (1.0 - 0.22 * below ** 1.8)
    rz = 0.130 if se > 0 else 0.108
    x = sa * ce * (rx + out)
    y = -ca * ce * (ry + out) - 0.018 * below ** 1.2
    z = se * (rz + out)
    return HEAD_C + Vector((x, y, z))


def head_normal(az, el):
    d = 1e-3
    p = head_point(az, el)
    n = (head_point(az, el + d) - p).cross(head_point(az + d, el) - p)
    if n.length < 1e-9:
        return (p - HEAD_C).normalized()
    n.normalize()
    return n if n.dot(p - HEAD_C) > 0 else -n


# ---------------------------------------------------------------------------------------------------- weights

def torso_w(p):
    k = smooth(1.02, 1.14, p.z)
    return {"Hips": 1 - k, "Spine": k}


def neck_w(p):
    k = smooth(1.42, 1.47, p.z)
    return {"Spine": 1 - k, "Head": k}


def hair_w(p):
    k = smooth(1.36, 1.46, p.z)
    return {"Spine": 1 - k, "Head": k}


def arm_w(side):
    n = side_name(side)
    sh, d = mirror(SHOULDER, side), mirror(ARM_DIR, side)
    total = UPPER_ARM + FOREARM

    def f(p):
        t = (p - sh).dot(d)
        if t < 0.03:
            k = smooth(-0.03, 0.03, t)
            return {"Spine": 1 - k, f"UpperArm.{n}": k}
        if t > total - 0.01:
            k = smooth(total - 0.01, total + 0.02, t)
            return {f"Forearm.{n}": 1 - k, f"Hand.{n}": k}
        k = smooth(UPPER_ARM - 0.03, UPPER_ARM + 0.03, t)
        return {f"UpperArm.{n}": 1 - k, f"Forearm.{n}": k}
    return f


def leg_w(side):
    n = side_name(side)

    def f(p):
        z = p.z
        if z > HIP.z - 0.03:
            k = smooth(HIP.z + 0.03, HIP.z - 0.05, z)
            return {"Hips": 1 - k, f"Thigh.{n}": k}
        if z < ANKLE.z + 0.03:
            k = smooth(ANKLE.z + 0.04, ANKLE.z, z)
            return {f"Shin.{n}": 1 - k, f"Foot.{n}": k}
        k = smooth(KNEE.z + 0.04, KNEE.z - 0.04, z)
        return {f"Thigh.{n}": 1 - k, f"Shin.{n}": k}
    return f


def lower_w(p):
    side = 1 if p.x >= 0 else -1
    if p.z > HIP.z + 0.01 or abs(p.x) < 0.015:
        return torso_w(p)
    return leg_w(side)(p)


# ---------------------------------------------------------------------------------------------------- the body

def head_grid(p, out, el0, el1, rows, segs, region="skin", weights=None, line=None):
    """The head's surface from elevation el0 (or line(az)) up to el1, `out` off it."""
    rings = []
    for j in range(rows):
        t = j / (rows - 1)
        ring = []
        for i in range(segs):
            az = 2 * math.pi * i / segs
            e0 = line(az) if line else el0
            ring.append(head_point(az, lerp(e0, el1, t), out))
        rings.append(ring)
    return p.grid(rings, region, caps=(False, head_point(0, el1, out) if el1 >= math.pi / 2 - 1e-3 else False),
                  weights=weights)


def head_piece():
    p = Part("Head", W(Head=1.0))
    rings = []
    for j in range(1, 28):
        el = -math.pi / 2 + math.pi * j / 28
        rings.append([head_point(2 * math.pi * i / 44, el) for i in range(44)])
    p.grid(rings, "skin", caps=(head_point(0, -math.pi / 2 + 1e-4), head_point(0, math.pi / 2 - 1e-4)))
    # a little nose and ears (mostly under the hair)
    nose = head_point(0, D(-12), -0.006)
    p.ellipsoid(nose, (0.0058, 0.0085, 0.0115), region="skin", segs=12, rings=8)
    for side in (1, -1):
        e = head_point(side * D(88), D(-8), -0.004)
        p.ellipsoid(e, (0.009, 0.014, 0.022), region="skin", segs=12, rings=8)
    return p


def body_piece():
    p = Part("Body", torso_w)
    # the torso, from the hips' bottom to the neck's base
    zs = [lerp(0.84, 1.40, (k / 24) ** 1.0) for k in range(25)]
    p.grid([torso_ring(z, 0.0) for z in zs], "skin", caps=(Vector((0, 0.004, 0.83)), Vector((0, 0.008, 1.405))),
           weights=torso_w)
    # the neck
    p.tube([Vector((0, 0.012, 1.36)), Vector((0, 0.008, 1.43)), Vector((0, 0.004, 1.505))], [0.034, 0.032, 0.030],
           "skin", segs=16, caps=(False, False), weights=neck_w)
    for side in (1, -1):
        sh, d = mirror(SHOULDER, side), mirror(ARM_DIR, side)
        # the shoulder's roundness, the arm, the hand
        p.ellipsoid(sh + Vector((-side * 0.006, 0, 0.0)), (0.034, 0.031, 0.026), region="skin", segs=16, rings=10,
                    weights=arm_w(side))
        path = [sh, sh + d * 0.06, sh + d * (UPPER_ARM * 0.6), sh + d * UPPER_ARM, sh + d * (UPPER_ARM + FOREARM * 0.35),
                sh + d * (UPPER_ARM + FOREARM)]
        p.tube(path, [0.030, 0.029, 0.025, 0.021, 0.0215, 0.016], "skin", segs=14, caps=(True, False),
               weights=arm_w(side))
        wr = mirror(WRIST, side)
        across = d.cross(Vector((0, 1, 0))).normalized()
        p.ellipsoid(wr + d * 0.03, (0.009, 0.021, 0.032), axes=(Vector((0, 1, 0)), across, d), region="skin", segs=14,
                    rings=8, weights=W(**{f"Hand.{side_name(side)}": 1.0}))
        p.ellipsoid(wr + d * 0.062 + across * 0.003, (0.0075, 0.017, 0.024), axes=(Vector((0, 1, 0)), across, d),
                    region="skin", segs=12, rings=8, weights=W(**{f"Hand.{side_name(side)}": 1.0}))
        p.ellipsoid(wr + d * 0.022 + Vector((0, -0.016, 0)) - across * 0.008 * side, (0.008, 0.008, 0.02),
                    axes=(Vector((0, 1, 0)), across, (d + Vector((0, -0.6, 0))).normalized()), region="skin", segs=10,
                    rings=6, weights=W(**{f"Hand.{side_name(side)}": 1.0}))
        # the leg: a long thigh, a slim knee, the calf, a fine ankle; the foot
        hip, knee, ankle = mirror(HIP, side), mirror(KNEE, side), mirror(ANKLE, side)
        path = [hip + Vector((0, 0, 0.05)), hip, hip.lerp(knee, 0.45), knee, knee.lerp(ankle, 0.32), knee.lerp(ankle, 0.75),
                ankle]
        p.tube(path, [0.066, 0.066, 0.052, 0.040, 0.043, 0.032, 0.026], "skin", segs=16, caps=(True, False),
               weights=leg_w(side))
        foot = [ankle + Vector((0, 0.012, -0.01)), ankle.lerp(mirror(TOE, side), 0.5) + Vector((0, 0, -0.012)),
                mirror(TOE, side) + Vector((0, 0, 0.004))]
        p.tube(foot, [(0.021, 0.025), (0.021, 0.018), (0.011, 0.008)], "skin", segs=12,
               weights=W(**{f"Foot.{side_name(side)}": 1.0}))
    return p


# ---------------------------------------------------------------------------------------------------- the face

def head_bvh():
    h = head_piece()
    return BVHTree.FromPolygons([tuple(c) for c in h.co], [list(f) for f in h.faces])


def project(bvh, x, z, out):
    hit = bvh.ray_cast(Vector((x, -1.0, z)), Vector((0, 1, 0)))
    loc, nrm = hit[0], hit[1]
    if loc is None:
        return Vector((x, -0.05, z)), Vector((0, -1, 0))
    nrm = Vector(nrm)
    if nrm.y > 0:
        nrm = -nrm
    return Vector(loc) + nrm * out, nrm


def decal(p, bvh, outline, out, region, weights, thick=0.0007):
    """A flat shape lying on her face: outline in (x, z), pressed onto the head along -Y, `out` off it."""
    pts = [project(bvh, x, z, out) for x, z in outline]
    cx = sum(x for x, _ in outline) / len(outline)
    cz = sum(z for _, z in outline) / len(outline)
    c, cn = project(bvh, cx, cz, out)
    top = [q for q, _ in pts]
    bottom = [q - n * thick for q, n in pts]
    p.grid([bottom, top], region, caps=(c - cn * thick, c), weights=weights)


def ribbon(p, bvh, path, widths, out, region, weights, thick=0.0008):
    """A stroke along a path on her face (lashes, brows, the mouth's line): widths per point."""
    rows = []
    n = len(path)
    for i, (x, z) in enumerate(path):
        a = Vector(path[max(0, i - 1)])
        b = Vector(path[min(n - 1, i + 1)])
        t = (b - a).normalized()
        nx, nz = -t.y, t.x
        w = widths[i]
        up_q, up_n = project(bvh, x + nx * w, z + nz * w, out)
        dn_q, dn_n = project(bvh, x - nx * w, z - nz * w, out)
        rows.append([up_q, dn_q, dn_q - dn_n * thick, up_q - up_n * thick])
    p.grid(rows, region, caps=(True, True), weights=weights)


def clip(subject, clipper):
    """Sutherland-Hodgman: subject polygon inside a convex clipper (both counter-clockwise in x, z)."""
    out = list(subject)
    for i in range(len(clipper)):
        a, b = Vector(clipper[i]), Vector(clipper[(i + 1) % len(clipper)])
        inp, out = out, []
        if not inp:
            break

        def inside(q):
            return (b.x - a.x) * (q[1] - a.y) - (b.y - a.y) * (q[0] - a.x) >= 0

        def cross(p1, p2):
            p1, p2 = Vector(p1), Vector(p2)
            d1, d2 = p2 - p1, b - a
            den = d1.x * d2.y - d1.y * d2.x
            if abs(den) < 1e-12:
                return tuple(p2)
            t = ((a.x - p1.x) * d2.y - (a.y - p1.y) * d2.x) / den
            return tuple(p1 + d1 * t)
        prev = inp[-1]
        for q in inp:
            if inside(q):
                if not inside(prev):
                    out.append(cross(prev, q))
                out.append(q)
            elif inside(prev):
                out.append(cross(prev, q))
            prev = q
    return out


def ellipse(cx, cz, rx, rz, n=28, a0=0.0):
    return [(cx + rx * math.cos(a0 + 2 * math.pi * k / n), cz + rz * math.sin(a0 + 2 * math.pi * k / n)) for k in range(n)]


def eye_outline(side, n=36):
    """An almond: a high arched top lid, a flatter bottom, the outer corner lifted."""
    cx = side * EYE_X
    pts = []
    for k in range(n):
        a = 2 * math.pi * k / n
        x = math.cos(a) * 0.0238
        z = math.sin(a) * (0.0160 if math.sin(a) > 0 else 0.0100)
        z += 0.12 * x * side          # the outer corner up
        pts.append((cx + x, EYE_Z + z))
    return pts


def eyes_piece(bvh):
    p = Part("Eyes", W(Eyes=1.0))
    w = W(Eyes=1.0)
    for side in (1, -1):
        cx = side * EYE_X
        almond = eye_outline(side)
        decal(p, bvh, almond, 0.0012, "eye_shine", w)
        # a big iris (its top under the lid), a pupil, two highlights
        iris = clip(ellipse(cx + side * 0.0012, EYE_Z + 0.001, 0.0135, 0.0166), almond)
        if len(iris) >= 3:
            decal(p, bvh, iris, 0.0022, "eyes", w)
        pupil = clip(ellipse(cx + side * 0.0012, EYE_Z + 0.0012, 0.006, 0.0078), almond)
        if len(pupil) >= 3:
            decal(p, bvh, pupil, 0.0030, "ears", w)
        decal(p, bvh, ellipse(cx - 0.0045 + side * 0.0012, EYE_Z + 0.0052, 0.0034, 0.0038, 14), 0.0037, "eye_shine", w)
        decal(p, bvh, ellipse(cx + 0.0048 + side * 0.0012, EYE_Z - 0.0042, 0.0017, 0.0017, 10), 0.0037, "eye_shine", w)
        # the upper lash line along the top lid, thicker to the outer corner, a flick of lashes out there
        top = [pt for pt in almond[:len(almond) // 2 + 1]]
        if side < 0:
            top = list(reversed(top))
        # inner corner -> outer corner
        top = sorted(top, key=lambda q: q[0] * side)
        widths = [lerp(0.0007, 0.0021, (i / (len(top) - 1)) ** 1.4) for i in range(len(top))]
        ribbon(p, bvh, top, widths, 0.0042, "ears", w)
        ox, oz = top[-1]
        for k, (dx, dz) in enumerate(((0.0062, 0.0048), (0.0052, 0.0016))):
            flick = [(ox - side * 0.0022, oz - 0.0004), (ox + side * dx * 0.5, oz + dz * 0.45), (ox + side * dx, oz + dz)]
            ribbon(p, bvh, flick, [0.0011, 0.0008, 0.0002], 0.0042, "ears", w)
        # a short lower lash at the outer end
        low = [(cx + side * x, EYE_Z - 0.0082 + 0.12 * side * side * x + 0.0) for x in (0.008, 0.013, 0.018)]
        ribbon(p, bvh, low, [0.0003, 0.0005, 0.0003], 0.0040, "ears", w)
    return p


def eyes_happy_piece(bvh):
    p = Part("EyesHappy", W(EyesHappy=1.0))
    w = W(EyesHappy=1.0)
    for side in (1, -1):
        cx = side * EYE_X
        arc = [(cx + 0.019 * math.cos(a), EYE_Z - 0.003 + 0.010 * math.sin(a) + 0.002 * side * math.cos(a))
               for a in (math.pi * k / 10 for k in range(11))]
        ribbon(p, bvh, arc, [0.0012 + 0.0008 * math.sin(math.pi * k / 10) for k in range(11)], 0.0042, "ears", w)
        ox, oz = arc[0] if side > 0 else arc[-1]
        ribbon(p, bvh, [(ox, oz), (ox + side * 0.005, oz + 0.004)], [0.001, 0.0003], 0.0042, "ears", w)
    return p


MOUTH_Z = HEAD_C.z - 0.080


def mouth_pieces(bvh):
    out = []
    # a soft smile: the line, a little lower lip
    p = Part("Mouth", W(Mouth=1.0))
    w = W(Mouth=1.0)
    line = [(x, MOUTH_Z + 0.0022 * (x / 0.012) ** 2) for x in (-0.012, -0.006, 0.0, 0.006, 0.012)]
    ribbon(p, bvh, line, [0.0004, 0.0008, 0.0009, 0.0008, 0.0004], 0.0018, "tongue", w)
    decal(p, bvh, ellipse(0.0, MOUTH_Z - 0.0042, 0.0062, 0.0024, 16), 0.0012, "mouth", w)
    out.append(p)
    # open and happy: a rounded "D", the tongue's pink at the bottom
    p = Part("MouthOpen", W(MouthOpen=1.0))
    w = W(MouthOpen=1.0)
    d = [(0.0135 * math.cos(a), MOUTH_Z + 0.001 - 0.0105 * math.sin(a) ** 1.0) for a in (math.pi * k / 16 for k in range(17))]
    d += [(-0.0135 + 0.027 * k / 6, MOUTH_Z + 0.0022 + 0.0006 * math.sin(math.pi * k / 6)) for k in range(1, 6)]
    decal(p, bvh, d, 0.0016, "tongue", w)
    decal(p, bvh, ellipse(0.0, MOUTH_Z - 0.0068, 0.0058, 0.003, 14), 0.0024, "mouth", w)
    out.append(p)
    # a little frown
    p = Part("MouthSad", W(MouthSad=1.0))
    w = W(MouthSad=1.0)
    line = [(x, MOUTH_Z - 0.0024 * (x / 0.010) ** 2 + 0.001) for x in (-0.010, -0.005, 0.0, 0.005, 0.010)]
    ribbon(p, bvh, line, [0.0004, 0.0008, 0.0009, 0.0008, 0.0004], 0.0018, "tongue", w)
    out.append(p)
    return out


def face_details(p, bvh):
    """On the Head piece: thin arched brows, a blush on each cheek."""
    w = W(Head=1.0)
    for side in (1, -1):
        cx = side * EYE_X
        brow = [(cx - side * 0.016 + side * 0.032 * k / 6, EYE_Z + 0.025 + 0.0042 * math.sin(math.pi * (k / 6) ** 0.8))
                for k in range(7)]
        ribbon(p, bvh, brow, [0.0007, 0.0011, 0.0012, 0.0011, 0.0009, 0.0006, 0.0003], 0.0016, "ears", w)
        decal(p, bvh, ellipse(side * 0.046, EYE_Z - 0.026, 0.0105, 0.0055, 18), 0.0009, "blush", w)


# ---------------------------------------------------------------------------------------------------- hair

def hairline(az, front, side, back):
    c = math.cos(az)
    return lerp(side, front, kit.ease(c)) if c >= 0 else lerp(side, back, kit.ease(-c))


def shell(p, line, out, inner=-0.002, rows=12, segs=44, region="hair"):
    """A thick cap over her head from line(az) up: the outer surface `out` off the head, the inner just under it."""
    top = math.pi / 2 - 0.02
    outer = [[head_point(2 * math.pi * i / segs, lerp(line(2 * math.pi * i / segs), top, (j / rows) ** 0.85), out)
              for i in range(segs)] for j in range(rows)]
    inside = [[head_point(2 * math.pi * i / segs, lerp(line(2 * math.pi * i / segs), top, (j / rows) ** 0.85), inner)
               for i in range(segs)] for j in range(rows)]
    lip = [head_point(2 * math.pi * i / segs, line(2 * math.pi * i / segs) - 0.03, (out + inner) / 2) for i in range(segs)]
    p.grid(list(reversed(inside)) + [lip] + outer, region,
           caps=(head_point(0, math.pi / 2, inner), head_point(0, math.pi / 2, out)), weights=W(Head=1.0))


def strand(p, az0, el0, az1, el1, width, thick, out=0.012, lift=0.0, flick=0.0, n=9, region="hair", bend=None):
    """A clump lying on her head from (az0, el0) to (az1, el1), bulging by `lift`, the tip flicking out by `flick`."""
    pts, fronts, rads = [], [], []
    for k in range(n):
        t = k / (n - 1)
        az = lerp(az0, az1, t) + (bend(t) if bend else 0.0)
        el = lerp(el0, el1, t)
        pts.append(head_point(az, el, out + lift * math.sin(math.pi * t) + flick * t * t))
        fronts.append(head_normal(az, el))
        s = (1 - t) ** 0.7 * (0.85 + 0.15 * math.sin(math.pi * min(1.0, t * 2.2)))
        rads.append((max(0.0015, width * s), max(0.0012, thick * s)))
    p.tube(pts, rads, region, segs=8, fronts=fronts, power=2.2, cap_scale=0.6, weights=W(Head=1.0))


def back_y(z):
    cy, hw, hd, pw = section(z)
    return cy + hd


def lock(p, az, el0, el1, z_end, width, thick, out=0.012, spread=1.25, back=0.03, waves=2.0, amp=0.010, curl=0.03,
         region="hair", n_head=6, n_hang=14, seed=0.0):
    """A long lock: over her head from (az, el0) to (az, el1), then falling down her back (or past her shoulders) to
    z_end, spreading out (`spread`: wider at the bottom, the hair's volume), waving, its tip curling out."""
    pts, fronts = [], []
    for k in range(n_head):
        t = k / (n_head - 1)
        el = lerp(el0, el1, t)
        pts.append(head_point(az, el, out))
        fronts.append(head_normal(az, el))
    p0 = pts[-1]
    out_dir = Vector((math.sin(az), -math.cos(az), 0.0))
    for k in range(1, n_hang + 1):
        t = k / n_hang
        z = lerp(p0.z, z_end, t)
        x = p0.x * lerp(1.0, spread, t ** 1.2)
        # behind her back (or in front of her shoulders for the face's locks), never inside her
        if out_dir.y > 0.2:
            y = max(lerp(p0.y, back_y(z) + back, kit.ease(min(1.0, t * 2.5))), back_y(z) + back * 0.7)
        else:
            y = lerp(p0.y, p0.y - 0.01, t)
        wave = math.sin(t * waves * 2 * math.pi + seed) * amp * min(1.0, t * 3)
        q = Vector((x + wave * math.cos(az), y + wave * 0.5 * out_dir.y, z))
        q += out_dir * curl * max(0.0, (t - 0.82) / 0.18) ** 2
        pts.append(q)
        fronts.append(Vector((math.sin(az) * 1.2, -math.cos(az), 0.0)).normalized())
    n = len(pts)
    rads = []
    for k in range(n):
        u = k / (n - 1)
        s = 0.75 + 0.25 * min(1.0, u * 4)
        if u > 0.78:
            s *= 0.35 + 0.65 * ((1 - u) / 0.22) ** 0.9
        rads.append((max(0.002, width * s), max(0.0015, thick * s)))
    p.tube(pts, rads, region, segs=8, fronts=fronts, power=2.2, cap_scale=1.0, weights=hair_w)


def bangs(p, sweep=1, long=False):
    """A fringe swept to one side, its strands over the forehead down to the brows."""
    for k, az in enumerate((-58, -40, -22, -5, 12, 30, 48, 64)):
        a = D(az)
        end = D(17 + 4 * math.sin(k * 1.7)) if not long else D(8 + 5 * math.sin(k * 1.3))
        strand(p, a * 0.35 + sweep * D(14), D(78), a - sweep * D(12) * (1 - abs(az) / 80), end, 0.036, 0.014, out=0.02,
               lift=0.01, flick=0.008, bend=lambda t, s=sweep: -s * D(10) * t * t)


def face_locks(p, length_z, sides=(1, -1), width=0.026):
    for side in sides:
        for k, (az, w) in enumerate(((74, width), (88, width * 0.9))):
            lock(p, side * D(az), D(42), D(-10), length_z - 0.03 * k, w, w * 0.45, out=0.02, spread=1.2, back=0.0,
                 waves=1.4, amp=0.008, curl=0.016, n_hang=10, seed=k + side)


def curtain(p, z_bottom, half=D(66), out_top=0.03, out_bottom=0.06, points=7, tips=0.07, rows=16, n=30,
            region="hair"):
    """A sheet of hair down her back: from round the back of her head (under its shell) down to z_bottom, an arc round
    her back `half` each side, fuller lower (out_top .. out_bottom off her), its bottom edge in `points` pointed tips.
    Weighted to the head at the top and the spine lower down."""
    rows_v = []
    for j in range(rows):
        t = j / (rows - 1)
        row = []
        for i in range(n):
            u = i / (n - 1)
            a = (u - 0.5) * 2 * half                 # 0 = straight back
            tip = abs(math.sin(math.pi * u * points)) ** 0.8
            zb = z_bottom - tips * tip * (1 - 0.35 * abs(u - 0.5) * 2)
            k_head = max(0.0, 1 - t / 0.22)
            # the top rows follow the back of her head, the rest her back
            hp = head_point(D(180) + a * 1.15, lerp(D(-30), D(-58), min(1.0, t / 0.22)), 0.018)
            z = lerp(hp.z, zb, max(0.0, (t - 0.22) / 0.78)) if t > 0.22 else hp.z
            cy, hw, hd, pw = section(min(z, 1.40))
            r = lerp(out_top, out_bottom, t ** 0.8)
            body = Vector((math.sin(a) * (hw + r) * 1.05, cy + math.cos(a) * (hd + r), z))
            wave = 0.008 * math.sin(u * math.pi * 9 + t * 5) * t
            q = hp.lerp(body, 1 - k_head) + Vector((0, wave, 0))
            row.append(q)
        rows_v.append(row)
    p.grid(rows_v, region, closed=False, weights=hair_w)


def hair_wavy():
    p = Part("Hair_Wavy", W(Head=1.0))
    shell(p, lambda az: hairline(az, D(26), D(-6), D(-40)), 0.02)
    bangs(p, sweep=1)
    face_locks(p, 1.18, width=0.032)
    curtain(p, 1.0, half=D(64), out_top=0.03, out_bottom=0.07, points=7, tips=0.09)
    for k, az in enumerate(range(128, 233, 15)):
        mid = 1 - abs(az - 180) / 52.0
        lock(p, D(az), D(64), D(-48), lerp(0.98, 0.90, mid), 0.042, 0.017, out=0.024, spread=1.35, back=0.075,
             waves=2.0, amp=0.014, curl=0.03, seed=k * 1.3)
    return p


def hair_pony():
    p = Part("Hair_Pony", W(Head=1.0))
    shell(p, lambda az: hairline(az, D(26), D(-4), D(-30)), 0.016)
    bangs(p, sweep=-1)
    face_locks(p, 1.40, width=0.024)
    tie_el = D(48)
    tie = head_point(D(180), tie_el, 0.018)
    n = head_normal(D(180), tie_el)
    a1 = n.cross(Vector((1, 0, 0))).normalized()
    a2 = n.cross(a1).normalized()
    p.ring([tie + (a1 * math.cos(a) + a2 * math.sin(a)) * 0.022 for a in [2 * math.pi * k / 16 for k in range(16)]], 0.009,
           "hair_accent", weights=W(Head=1.0))
    for k, (dx, dy, drop, w) in enumerate(((-0.026, 0.0, 0.0, 0.046), (0.026, 0.0, 0.02, 0.046), (0.0, 0.024, -0.02, 0.05),
                                           (0.0, -0.018, 0.04, 0.04), (-0.016, 0.034, 0.06, 0.036), (0.018, 0.034, 0.05, 0.036))):
        p0 = head_point(D(180), tie_el, 0.008) + Vector((dx * 0.4, 0, 0))
        c1 = tie + Vector((dx, 0.13 + dy, 0.06))
        c2 = Vector((dx * 2.2, 0.26 + dy, 1.40 + drop))
        e = Vector((dx * 2.6, 0.17 + dy, 1.10 + drop))
        pts = [p0 * (1 - t) ** 3 + c1 * 3 * t * (1 - t) ** 2 + c2 * 3 * t * t * (1 - t) + e * t ** 3
               for t in (i / 16 for i in range(17))]
        for i in range(len(pts)):
            t = i / 16
            pts[i] = pts[i] + Vector((math.sin(t * 4 * math.pi + k) * 0.008 * t, 0, 0))
        rads = [(max(0.002, w * s), max(0.0018, w * 0.7 * s)) for s in
                ((0.6 + 0.4 * min(1, u * 4)) * (0.3 + 0.7 * max(0.0, (1 - u) / 0.3) ** 0.8 if u > 0.7 else 1.0) for u in
                 (i / 16 for i in range(17)))]
        p.tube(pts, rads, "hair", segs=8, front=Vector((0, 1, 0.3)), power=2.2, cap_scale=0.8, weights=W(Head=1.0))
    return p


def hair_bob():
    p = Part("Hair_Bob", W(Head=1.0))
    shell(p, lambda az: hairline(az, D(26), D(-8), D(-34)), 0.022)
    bangs(p, sweep=1)
    # layers round her head down to the jaw, their tips flicking out
    for k, az in enumerate(range(58, 303, 14)):
        a = D(az)
        strand(p, a, D(62), a + D(6) * math.sin(k * 1.9), D(-50), 0.040, 0.016, out=0.022, lift=0.012,
               flick=0.028 + 0.008 * math.sin(k))
    for k, az in enumerate(range(80, 281, 25)):
        a = D(az + 7)
        strand(p, a, D(30), a + D(10), D(-58), 0.030, 0.013, out=0.03, lift=0.004, flick=0.035)
    return p


# ---------------------------------------------------------------------------------------------------- outfits

def front_frame(z, x=0.0, off=0.0):
    cy, hw, hd, pw = section(z)
    u = max(-0.999, min(0.999, x / (hw + off)))
    y = cy - (hd + off) * (1 - abs(u) ** pw) ** (1 / pw)
    return Vector((x, y, z))


def crop_top():
    p = Part("CropTop", torso_w)
    def z_top(a):   # the neckline: lower at the front's middle (a sweetheart), higher at the back
        front = max(0.0, -math.sin(a))      # superellipse param: sin < 0 = the front (-y)
        middle = max(0.0, 1 - abs(math.cos(a)) / 0.45)
        return 1.322 - 0.035 * front * middle ** 1.6 + 0.012 * max(0.0, math.sin(a))
    n = 40
    rows = []
    for k in range(9):
        t = k / 8
        row = []
        for i in range(n):
            a = 2 * math.pi * i / n
            z = lerp(1.165, z_top(a), t)
            cy, hw, hd, pw = section(z)
            ca, sa = math.cos(a), math.sin(a)
            row.append(Vector((kit.spow(ca, 2 / pw) * (hw + 0.006), cy + kit.spow(sa, 2 / pw) * (hd + 0.006), z)))
        rows.append(row)
    p.grid(rows, "top", weights=torso_w)
    p.ring(torso_ring(1.168, 0.009, keep=True), (0.006, 0.004), "top_trim", segs=6, weights=torso_w)
    # straps over the shoulders
    for side in (1, -1):
        f = front_frame(1.30, side * 0.062, 0.006)
        b = Vector((side * 0.064, back_y(1.33) + 0.004, 1.33))
        over = Vector((side * 0.095, 0.010, 1.398))
        p.tube([f, f + Vector((side * 0.012, -0.004, 0.04)), over, b], 0.0055, "top_trim", segs=8, weights=torso_w)
    g = front_frame(1.272, 0.0, 0.012)
    p.ellipsoid(g + Vector((0, -0.004, 0)), (0.011, 0.006, 0.013), region="top_accent", segs=12, rings=8, weights=torso_w)
    return p


def bodice():
    p = Part("Bodice", torso_w)
    n = 44
    rows = []
    for k in range(12):
        t = k / 11
        row = []
        for i in range(n):
            a = 2 * math.pi * i / n
            front = max(0.0, -math.sin(a))
            middle = max(0.0, 1 - abs(math.cos(a)) / 0.5)
            top = 1.318 - 0.045 * front * middle ** 1.4 + 0.01 * max(0.0, math.sin(a))
            z = lerp(1.03, top, t)
            cy, hw, hd, pw = section(z)
            flare = 0.03 * max(0.0, 1 - t / 0.16)
            ca, sa = math.cos(a), math.sin(a)
            row.append(Vector((kit.spow(ca, 2 / pw) * (hw + 0.007 + flare), cy + kit.spow(sa, 2 / pw) * (hd + 0.007 + flare), z)))
        rows.append(row)
    p.grid(rows, lambda i, j, c: "top_accent" if i < 2 else "top", weights=torso_w)
    p.ring(torso_ring(1.075, 0.011, keep=True), (0.006, 0.004), "top_trim", segs=6, weights=torso_w)
    for k in range(4):
        z = 1.12 + k * 0.042
        a = front_frame(z, -0.018, 0.009)
        b = front_frame(z + 0.03, 0.018, 0.009)
        p.tube([a, (a + b) / 2 + Vector((0, -0.003, 0)), b], 0.0022, "top_trim", segs=6, weights=torso_w)
        a = front_frame(z, 0.018, 0.009)
        b = front_frame(z + 0.03, -0.018, 0.009)
        p.tube([a, (a + b) / 2 + Vector((0, -0.003, 0)), b], 0.0022, "top_trim", segs=6, weights=torso_w)
    for side in (1, -1):
        f = front_frame(1.30, side * 0.06, 0.007)
        b = Vector((side * 0.064, back_y(1.33) + 0.005, 1.33))
        over = Vector((side * 0.10, 0.010, 1.392))
        p.tube([f, f + Vector((side * 0.012, -0.004, 0.04)), over, b], 0.007, "top", segs=8, weights=torso_w)
    g = front_frame(1.262, 0.0, 0.014)
    from_heart = [(0.012 * math.sin(t) ** 3 * 1.0, 0.0) for t in ()]
    p.ellipsoid(g + Vector((0, -0.003, 0)), (0.012, 0.006, 0.012), region="top_trim", segs=12, rings=8, weights=torso_w)
    return p


def skirt_layer(p, z_top, z_join, z_tip, grow0, grow1, petals, phase, region, pointed=True, steps=8, n=64):
    rows = []
    for k in range(steps):
        t = k / (steps - 1)
        row = []
        for i in range(n):
            a = 2 * math.pi * i / n
            u = (i / n * petals + phase) % 1.0
            tip = math.sin(math.pi * u) ** (0.9 if pointed else 0.35)
            z = lerp(z_top, lerp(z_join, z_tip, tip), t)
            cy, hw, hd, pw = section(max(z, 0.86))
            if z < 0.93:
                hw, hd = max(hw, 0.121), max(hd, 0.078)
            f = min(1.0, (z_top - z) / (z_top - 0.76)) ** 0.9
            grow = grow0 + (grow1 - grow0) * f + 0.006 * tip * math.sin(math.pi * t)
            ca, sa = math.cos(a), math.sin(a)
            row.append(Vector((kit.spow(ca, 2 / pw) * (hw + grow), cy + kit.spow(sa, 2 / pw) * (hd + grow), z)))
        rows.append(row)
    p.grid(rows, region, weights=W(Hips=1.0))


def mini_skirt():
    p = Part("MiniSkirt", W(Hips=1.0))
    skirt_layer(p, 1.05, 0.825, 0.795, 0.004, 0.082, 10, 0.5, "bottom_accent", pointed=False)
    skirt_layer(p, 1.06, 0.85, 0.82, 0.012, 0.10, 10, 0.0, "bottom", pointed=False)
    p.ring(torso_ring(1.05, 0.016, keep=True), (0.009, 0.006), "bottom_accent", segs=6, weights=W(Hips=1.0))
    p.grid([torso_ring(z, 0.009) for z in (0.85, 0.90, 0.96, 1.02)], "bottom_accent", caps=(Vector((0, 0.004, 0.835)), False),
           weights=lower_w)
    return p


def petal_skirt():
    p = Part("PetalSkirt", W(Hips=1.0))
    skirt_layer(p, 1.05, 0.815, 0.755, 0.004, 0.095, 8, 0.5, "bottom_accent")
    skirt_layer(p, 1.06, 0.85, 0.79, 0.012, 0.12, 8, 0.0, "bottom")
    p.ring(torso_ring(1.05, 0.016, keep=True), (0.009, 0.006), "bottom_accent", segs=6, weights=W(Hips=1.0))
    p.grid([torso_ring(z, 0.009) for z in (0.85, 0.90, 0.96, 1.02)], "bottom_accent", caps=(Vector((0, 0.004, 0.835)), False),
           weights=lower_w)
    return p


def boots():
    p = Part("Boots")
    for side in (1, -1):
        knee, ankle = mirror(KNEE, side), mirror(ANKLE, side)
        w = leg_w(side)
        path = [ankle + Vector((0, 0, -0.035)), ankle, knee.lerp(ankle, 0.75), knee.lerp(ankle, 0.32), knee.lerp(ankle, 0.1)]
        p.tube(path, [0.034, 0.033, 0.038, 0.050, 0.047], "shoes", segs=16, caps=(False, False), weights=w)
        top = knee.lerp(ankle, 0.1)
        p.ring(superellipse(top, Vector((1, 0, 0)), Vector((0, 1, 0)), 0.05, 0.05, 2.0, 18), (0.009, 0.007), "shoes_accent",
               segs=8, front=Vector((0, 0, 1)), weights=w)
        # the foot: a slim pointed boot, a little heel
        toe = mirror(TOE, side)
        fw = W(**{f"Foot.{side_name(side)}": 1.0})
        foot = [ankle + Vector((0, 0.018, -0.028)), ankle.lerp(toe, 0.4) + Vector((0, 0, -0.016)),
                ankle.lerp(toe, 0.78) + Vector((0, 0, -0.006)), toe + Vector((0, -0.01, 0.004))]
        p.tube(foot, [(0.031, 0.034), (0.030, 0.026), (0.025, 0.019), (0.012, 0.009)], "shoes", segs=14, weights=fw)
        p.tube([ankle + Vector((0, 0.02, -0.052)), ankle.lerp(toe, 0.5) + Vector((0, 0, -0.036)), toe + Vector((0, -0.006, -0.004))],
               [(0.024, 0.007), (0.022, 0.006), (0.008, 0.004)], "soles", segs=10, weights=fw)
    return p


def tiara():
    p = Part("Tiara", W(Head=1.0))
    n = 31
    base = lambda az: D(52) - D(22) * (az / D(70)) ** 2   # noqa: E731
    peak = lambda az: D(14) * max(0.0, 1 - abs(math.degrees(az)) / 16) ** 1.4 + D(6) * max(0.0, 1 - abs(abs(math.degrees(az)) - 34) / 10) ** 1.4  # noqa: E731
    span = [D(-70) + D(140) * i / (n - 1) for i in range(n)]
    rows = []
    for o, top in ((0.024, False), (0.029, False), (0.029, True), (0.024, True)):
        rows.append([head_point(az, base(az) + (D(4) + peak(az) if top else 0.0), o) for az in span])
    p.grid(rows, "hat", closed=False, loop=True)
    for az, el_up, size in ((0.0, D(13), 0.0075), (D(34), D(6), 0.0045), (D(-34), D(6), 0.0045), (D(-70), 0, 0.005), (D(70), 0, 0.005)):
        el = base(az) + D(2.5) + el_up
        nn = head_normal(az, el)
        sx, up, n_ = kit.surface_frame(nn)
        p.ellipsoid(head_point(az, el, 0.030), (size, size, size * 0.7), axes=(sx, up, n_), region="hat_accent", segs=12, rings=8)
    return p


def choker():
    p = Part("Choker", neck_w)
    c = Vector((0, 0.008, 1.425))
    p.ring(superellipse(c, Vector((1, 0, 0)), Vector((0, 1, 0)), 0.0325, 0.0325, 2.0, 24), (0.004, 0.006), "acc", segs=8,
           front=Vector((0, 0, 1)), weights=neck_w)
    p.ellipsoid(c + Vector((0, -0.036, -0.004)), (0.006, 0.004, 0.007), region="acc_accent", segs=10, rings=6, weights=neck_w)
    return p


# ---------------------------------------------------------------------------------------------------- the rig

def bones():
    b = {
        "Root": ((0, 0, 0), (0, 0.15, 0), None),
        "Hips": ((0, 0, HIP.z), (0, 0, 1.08), "Root"),
        "Spine": ((0, 0, 1.08), (0, 0, 1.36), "Hips"),
        "Head": ((0, 0, 1.43), (0, 0, 1.70), "Spine"),
    }
    for name, z in (("Eyes", EYE_Z), ("EyesHappy", EYE_Z), ("Mouth", MOUTH_Z), ("MouthOpen", MOUTH_Z), ("MouthSad", MOUTH_Z)):
        b[name] = ((0, -0.08, z), (0, -0.08, z + 0.03), "Head")
    for side in (1, -1):
        n = side_name(side)
        b[f"UpperArm.{n}"] = (tuple(mirror(SHOULDER, side)), tuple(mirror(ELBOW, side)), "Spine")
        b[f"Forearm.{n}"] = (tuple(mirror(ELBOW, side)), tuple(mirror(WRIST, side)), f"UpperArm.{n}")
        b[f"Hand.{n}"] = (tuple(mirror(WRIST, side)), tuple(mirror(HAND_C + ARM_DIR * 0.05, side)), f"Forearm.{n}")
        b[f"Thigh.{n}"] = (tuple(mirror(HIP, side)), tuple(mirror(KNEE, side)), "Hips")
        b[f"Shin.{n}"] = (tuple(mirror(KNEE, side)), tuple(mirror(ANKLE, side)), f"Thigh.{n}")
        b[f"Foot.{n}"] = (tuple(mirror(ANKLE, side)), tuple(mirror(TOE, side)), f"Shin.{n}")
    return b


def make_armature(coll):
    data = bpy.data.armatures.new("HeroineRig")
    arm = bpy.data.objects.new("HeroineRig", data)
    coll.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for name, (h, t, parent) in bones().items():
        eb = data.edit_bones.new(name)
        eb.head, eb.tail = h, t
        direction = (Vector(t) - Vector(h)).normalized()
        eb.align_roll(Vector((0, -1, 0)) if abs(direction.y) < 0.9 else Vector((0, 0, 1)))
        if parent:
            eb.parent = data.edit_bones[parent]
            eb.use_connect = False
        eb.use_deform = name != "Root"
    bpy.ops.object.mode_set(mode="OBJECT")
    data.display_type = "STICK"
    return arm


# ---------------------------------------------------------------------------------------------------- build, preview, export

def build():
    ta.clear_scene()
    for coll in list(bpy.data.collections):
        if coll.name in ("Heroine", "Preview"):
            for o in list(coll.objects):
                bpy.data.objects.remove(o, do_unlink=True)
    PIECES.clear()
    coll = ta.collection("Heroine")
    arm = make_armature(coll)
    made = []

    def add(part, slot):
        o = part.to_object(coll, arm)
        PIECES[o.name] = slot
        made.append(o)
    bvh = head_bvh()
    head = head_piece()
    face_details(head, bvh)
    add(head, "head")
    add(body_piece(), "body")
    add(eyes_piece(bvh), "eyes")
    add(eyes_happy_piece(bvh), "eyes")
    for m in mouth_pieces(bvh):
        add(m, "mouth")
    for h in (hair_wavy(), hair_pony(), hair_bob()):
        add(h, "hair")
    add(crop_top(), "top")
    add(bodice(), "top")
    add(mini_skirt(), "bottom")
    add(petal_skirt(), "bottom")
    add(boots(), "shoes")
    add(tiara(), "hat")
    add(choker(), "acc")
    return {"pieces": len(made), "tris": sum(sum(len(pl.vertices) - 2 for pl in o.data.polygons) for o in made)}


def pieces():
    return [o for o in bpy.data.collections["Heroine"].objects if o.type == "MESH"]


FACE = ["Head", "Body", "Eyes", "Mouth"]
LOOKS = [
    (FACE + ["Hair_Wavy", "CropTop", "MiniSkirt", "Boots", "Tiara", "Choker"],
     {"skin": "#FFD9C6", "blush": "#FF9AA8", "eyes": "#19B6E8", "ears": "#2A1630", "mouth": "#F2557E", "tongue": "#A3263F",
      "hair": "#FFC12E", "hair_accent": "#FF5AA8", "top": "#FF2E93", "top_accent": "#7DF2FF", "top_trim": "#FFE15A",
      "bottom": "#2EC4FF", "bottom_accent": "#FF8ED0", "shoes": "#FF2E93", "shoes_accent": "#FFE15A", "soles": "#FFE15A",
      "hat": "#FFE15A", "hat_accent": "#7DF2FF", "acc": "#FF2E93", "acc_accent": "#FFE15A"}),
    (FACE + ["Hair_Pony", "Bodice", "PetalSkirt", "Boots", "Choker"],
     {"skin": "#C98E6A", "blush": "#E8707E", "eyes": "#8B4DFF", "ears": "#1E1028", "mouth": "#D8406A", "tongue": "#8E1E38",
      "hair": "#3A1E5C", "hair_accent": "#4DF0D0", "top": "#18C9B5", "top_accent": "#B8FFF0", "top_trim": "#FFFFFF",
      "bottom": "#7A4DFF", "bottom_accent": "#4DF0D0", "shoes": "#7A4DFF", "shoes_accent": "#4DF0D0", "soles": "#FFFFFF",
      "acc": "#4DF0D0", "acc_accent": "#FFFFFF"}),
    (FACE + ["Hair_Bob", "CropTop", "PetalSkirt", "Boots", "Tiara"],
     {"skin": "#FFE2D2", "blush": "#FF9AB8", "eyes": "#3CCB5A", "ears": "#24102A", "mouth": "#FF5A8C", "tongue": "#A3263F",
      "hair": "#FF5AB0", "hair_accent": "#FFE15A", "top": "#A65BFF", "top_accent": "#FFE15A", "top_trim": "#FFFFFF",
      "bottom": "#FFB13B", "bottom_accent": "#FFE7A0", "shoes": "#A65BFF", "shoes_accent": "#FFFFFF", "soles": "#FFFFFF",
      "hat": "#FFE15A", "hat_accent": "#FF2E93"}),
]


def preview(name="heroine"):
    import chibi
    out_dir = os.path.join(HERE, "previews")
    os.makedirs(out_dir, exist_ok=True)
    coll = ta.collection("Preview")
    for o in list(coll.objects):
        if o.type == "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    for i, (names, colours) in enumerate(LOOKS):
        off = Vector(((i - (len(LOOKS) - 1) / 2) * 0.62, 0, 0))
        for n in names:
            src = bpy.data.objects.get(n)
            if src is None:
                print("[heroine] no piece", n)
                continue
            kit.recolour(src, colours)
            chibi.place_copy(src, coll, off, f"pv{i}_{n}")
    for o in pieces():
        o.hide_render = True
    paths = []
    for view, yaw, pitch, target, dist, res in (("front", 0, 4, (0, 0, 0.9), 3.6, (1800, 1300)),
                                               ("three", -30, 8, (0, 0, 0.9), 3.6, (1800, 1300)),
                                               ("back", 180, 6, (0, 0, 0.9), 3.6, (1800, 1300)),
                                               ("side", 90, 4, (0.62, 0, 0.95), 2.4, (1000, 1300))):
        ta.preview_rig(target=target, distance=dist, yaw_deg=yaw, pitch_deg=pitch, lens=50)
        chibi.lights()
        paths.append(ta.render_preview(os.path.join(out_dir, f"{name}_{view}.png"), res=res))
    for i in range(len(LOOKS)):
        x = (i - (len(LOOKS) - 1) / 2) * 0.62
        ta.preview_rig(target=(x, 0, 1.55), distance=0.75, yaw_deg=-14, pitch_deg=2, lens=50)
        chibi.lights()
        paths.append(ta.render_preview(os.path.join(out_dir, f"{name}_face{i}.png"), res=(900, 900)))
    for o in pieces():
        o.hide_render = False
    return paths


def export():
    arm = bpy.data.objects["HeroineRig"]
    for o in pieces():
        for nm in ("Col", "AO"):
            attr = o.data.color_attributes.get(nm)
            if attr:
                o.data.color_attributes.remove(attr)
    path = tr.export_fbx(os.path.join(kit.UNITY_ART, "Heroine.fbx"), arm, pieces())
    info = {"regions": kit.REGIONS, "pieces": {o.name: PIECES.get(o.name, "?") for o in pieces()},
            "tris": {o.name: sum(len(pl.vertices) - 2 for pl in o.data.polygons) for o in pieces()}}
    with open(os.path.join(HERE, "heroine.json"), "w", encoding="utf-8") as f:
        json.dump(info, f, indent=1)
    return path
