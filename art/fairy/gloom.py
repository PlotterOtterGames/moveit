"""Zana!'s creatures as a kit (Blender 5.1, headless; run by build.py): ten bodies and the parts the game puts on them
(GloomViews.SpeciesMesh puts a species together: a body, its eyes and mouth twice, grumpy and happy, horns or ears,
wings both sides, a tail; a chief wears the crown). Built with the chibi kit's geometry (art/chibi/kit.py: tubes,
rings, ellipsoids, slabs), rounded and soft like the chibis, ~1 m across, facing -Y (the game's +Z), up +Z.

Every face has a region (uv1.x; the game colours it per species: Tundu/Gloom):
    0 body   1 belly   2 accent (spikes, fins, tips)   3 eye white   4 pupil   5 mouth   6 wing   7 cheek
and ambient occlusion baked into uv1.y.

    Body_Blob, Body_Imp, Body_Bat, Body_Puff, Body_Bug, Body_Jelly, Body_Spiky, Body_Ghost, Body_Crystal, Body_Fish
    Eyes_<0-3>_gloom / Eyes_<0-3>_happy    (a pair round the origin, the game puts them at the body's eye anchor)
    Mouth_<0-2>_gloom / Mouth_<0-2>_happy
    Horns_<0-3>   (0 little horns, 1 antennae, 2 long ears, 3 crystal spikes; their base at the origin)
    Wing_<0-2>    (0 bat, 1 bug, 2 feathers; one wing reaching out along +X from its hinge at the origin)
    Tail_<0-1>    (0 curly, 1 pointed; from its root at the origin, back along +Y)
    Crown         (a chief's: its base at the origin)

gloom_kit.json (next to the FBX): each body's anchors in the game's coordinates (x across, y up, z forward).
"""

import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "chibi"))
sys.path.insert(0, os.path.join(HERE, "..", "tools"))
import kit  # noqa: E402
import tundu_art as ta  # noqa: E402
from kit import Part, bezier, superellipse  # noqa: E402

UNITY_ART = os.path.join(ta.REPO, "unity", "MoveItGames", "Assets", "Games", "Fairy", "Art")
D = math.radians
BODY, BELLY, ACCENT, WHITE, PUPIL, MOUTH, WING, CHEEK = range(8)
REGION_NAMES = ["body", "belly", "accent", "eye_white", "pupil", "mouth", "wing", "cheek"]
# preview colours per region (a purified creature: the game greys them while gloomy)
PREVIEW = ["#B79BFF", "#E6DBFF", "#FF8FC4", "#FFFFFF", "#241B3A", "#8A2E4E", "#CDE8FF", "#FF9CB6"]
STATIC = {}


def W0(p):
    return {"Root": 1.0}


def part(name):
    return Part(name, W0)


def taper(n, keys):
    """n radii interpolated from a few keys (numbers or (width, depth) pairs) along a tube's points."""
    if isinstance(keys, (int, float, tuple)):
        return keys
    keys = [(k, k) if isinstance(k, (int, float)) else tuple(k) for k in keys]
    if len(keys) == n:
        return keys
    out = []
    for i in range(n):
        u = i / max(1, n - 1) * (len(keys) - 1)
        a = min(int(u), len(keys) - 2)
        t = u - a
        out.append((keys[a][0] + (keys[a + 1][0] - keys[a][0]) * t, keys[a][1] + (keys[a + 1][1] - keys[a][1]) * t))
    return out


def tube(p, pts, keys, region, **kw):
    pts = list(pts)
    return p.tube(pts, taper(len(pts), keys), region, **kw)


# ------------------------------------------------------------------------------------------------- body helpers

def blob_rows(profile, segs=40, squash_y=0.92):
    """Rings of a round body from a profile [(z, r)] (front -Y slightly flatter by squash_y)."""
    rows = []
    for z, r in profile:
        rows.append([Vector((math.cos(a) * r, math.sin(a) * r * squash_y, z))
                     for a in [2 * math.pi * i / segs for i in range(segs)]])
    return rows


def front_point(rows_profile, z, squash_y=0.92):
    """Where the body's front surface is at height z (for anchors)."""
    for (z0, r0), (z1, r1) in zip(rows_profile, rows_profile[1:]):
        if min(z0, z1) <= z <= max(z0, z1):
            t = (z - z0) / (z1 - z0) if z1 != z0 else 0
            r = r0 + (r1 - r0) * t
            return Vector((0, -r * squash_y, z))
    return Vector((0, -0.4, z))


def belly_region(cut_z_hi, cut_z_lo, width):
    """A region function: the front lower patch is the belly."""
    def reg(i, j, c):
        if c.y < -0.05 and cut_z_lo < c.z < cut_z_hi and abs(c.x) < width * (1 - (c.z - (cut_z_lo + cut_z_hi) / 2) ** 2 * 4):
            return BELLY
        return BODY
    return reg


def gumdrop(z0=-0.45, z1=0.5, r=0.5, bulge=0.12, n=14):
    prof = []
    for k in range(n + 1):
        t = k / n
        z = z0 + (z1 - z0) * t
        # wider low, round on top
        rr = r * math.sin(math.pi * (0.05 + 0.95 * t) * 0.5 + 0.0) ** 0.0
        rr = r * (math.sqrt(max(0.0, 1 - (2 * t - 1) ** 2)) ** 0.8) * (1 + bulge * (1 - t))
        prof.append((z, max(0.012, rr)))
    return prof


# ------------------------------------------------------------------------------------------------- the bodies

ANCHORS = {}


def anchors(name, eye, mouth, horn, wing, tail, crown, eye_size=1.0, mouth_size=1.0, horn_size=1.0, wing_size=1.0,
            tail_size=1.0):
    def U(v):   # Blender (x, y, z) -> the game's (x, z, -y)
        v = Vector(v)
        return [round(v.x, 4), round(v.z, 4), round(-v.y, 4)]
    ANCHORS[name] = {"name": name, "eye": U(eye), "mouth": U(mouth), "horn": U(horn), "wing": U(wing), "tail": U(tail),
                     "crown": U(crown), "eyeSize": eye_size, "mouthSize": mouth_size, "hornSize": horn_size,
                     "wingSize": wing_size, "tailSize": tail_size, "height": 1.0}


def body_blob():
    p = part("Body_Blob")
    prof = gumdrop()
    rows = blob_rows(prof)
    p.grid(rows, BODY, caps=(Vector((0, 0, -0.45)), Vector((0, 0, 0.5))))
    # a little drip tuft on top
    tube(p, bezier([(0, 0, 0.44), (0.03, 0, 0.58), (-0.05, 0.02, 0.66)], 7), [0.07, 0.05, 0.02], BODY, segs=10)
    fz = front_point(prof, 0.1)
    p.ellipsoid((0, -0.3, -0.18), (0.3, 0.2, 0.24), region=BELLY, segs=24, rings=14)
    anchors("Blob", eye=fz + Vector((0, -0.02, 0.06)), mouth=front_point(prof, -0.12) + Vector((0, -0.01, 0)),
            horn=(0, 0, 0.42), wing=(0.3, 0.18, 0.05), tail=(0, 0.35, -0.3), crown=(0, 0, 0.5))
    return p


def body_imp():
    p = part("Body_Imp")
    prof = [(-0.4, 0.05), (-0.38, 0.22), (-0.3, 0.35), (-0.15, 0.42), (0.05, 0.44), (0.22, 0.42), (0.36, 0.34), (0.46, 0.2),
            (0.52, 0.06)]
    rows = blob_rows(prof, squash_y=0.9)
    p.grid(rows, BODY, caps=(Vector((0, 0, -0.41)), Vector((0, 0, 0.53))))
    # stubby arms and feet
    for s in (1, -1):
        tube(p, bezier([(s * 0.36, -0.04, -0.02), (s * 0.52, -0.08, -0.08), (s * 0.58, -0.12, -0.16)], 6), [0.09, 0.08, 0.075], BODY, segs=12)
        p.ellipsoid((s * 0.18, -0.08, -0.42), (0.12, 0.14, 0.07), region=ACCENT, segs=14, rings=8)
    # a pointy tuft
    tube(p, bezier([(0, 0, 0.48), (0.02, 0.02, 0.62), (0.08, 0.06, 0.7)], 7), [0.08, 0.04, 0.01], BODY, segs=10)
    p.ellipsoid((0, -0.3, -0.16), (0.26, 0.17, 0.2), region=BELLY, segs=24, rings=14)
    anchors("Imp", eye=front_point(prof, 0.18, 0.9) + Vector((0, -0.03, 0)), mouth=front_point(prof, -0.03, 0.9) + Vector((0, -0.02, 0)),
            horn=(0, 0, 0.46), wing=(0.26, 0.2, 0.16), tail=(0, 0.33, -0.28), crown=(0, 0, 0.52))
    return p


def body_bat():
    p = part("Body_Bat")
    prof = [(-0.36, 0.04), (-0.32, 0.2), (-0.2, 0.32), (0.0, 0.37), (0.2, 0.33), (0.32, 0.22), (0.38, 0.05)]
    rows = blob_rows(prof, squash_y=0.95)
    p.grid(rows, BODY, caps=(Vector((0, 0, -0.37)), Vector((0, 0, 0.39))))
    # big pointed ears
    for s in (1, -1):
        base = Vector((s * 0.2, 0.0, 0.28))
        tip = Vector((s * 0.36, 0.02, 0.62))
        tube(p, bezier([base, (base + tip) / 2 + Vector((s * 0.03, 0, 0)), tip], 7), [(0.12, 0.05), (0.08, 0.04), (0.01, 0.01)], BODY,
               segs=12, front=Vector((0, -1, 0)))
        tube(p, bezier([base + Vector((0, -0.03, 0.04)), (base + tip) / 2 + Vector((s * 0.03, -0.035, 0)), tip + Vector((0, -0.02, -0.06))], 6),
               [(0.07, 0.015), (0.05, 0.012), (0.01, 0.01)], ACCENT, segs=10, front=Vector((0, -1, 0)))
        # little feet
        p.ellipsoid((s * 0.12, -0.05, -0.37), (0.07, 0.09, 0.05), region=ACCENT, segs=12, rings=8)
    p.ellipsoid((0, -0.25, -0.2), (0.2, 0.13, 0.15), region=BELLY, segs=22, rings=12)
    anchors("Bat", eye=front_point(prof, 0.08, 0.95) + Vector((0, -0.02, 0)), mouth=front_point(prof, -0.1, 0.95) + Vector((0, -0.01, 0)),
            horn=(0, 0, 0.36), wing=(0.24, 0.12, 0.05), tail=(0, 0.3, -0.25), crown=(0, 0, 0.42), wing_size=1.25)
    return p


def body_puff():
    p = part("Body_Puff")
    balls = [((0, 0, 0), 0.4), ((0.3, 0.02, -0.06), 0.27), ((-0.3, 0.02, -0.06), 0.27), ((0.18, 0.04, 0.24), 0.26),
             ((-0.17, 0.05, 0.26), 0.25), ((0.0, 0.08, 0.34), 0.24), ((0.4, 0.06, 0.12), 0.18), ((-0.4, 0.06, 0.12), 0.18),
             ((0.0, -0.05, -0.28), 0.25)]
    for c, r in balls:
        p.ellipsoid(c, (r, r * 0.92, r * 0.9), region=BODY, segs=22, rings=14)
    p.ellipsoid((0, -0.22, -0.12), (0.24, 0.12, 0.18), region=BELLY, segs=18, rings=10)
    anchors("Puff", eye=(0, -0.38, 0.06), mouth=(0, -0.38, -0.12), horn=(0, 0, 0.5), wing=(0.36, 0.16, 0.05), tail=(0, 0.36, -0.2),
            crown=(0, 0.05, 0.56))
    return p


def body_bug():
    p = part("Body_Bug")
    # the shell: an oval dome with a seam down the middle, a round head in front
    prof = [(-0.3, 0.05), (-0.26, 0.3), (-0.14, 0.42), (0.04, 0.45), (0.2, 0.4), (0.3, 0.28), (0.36, 0.06)]
    rows = blob_rows(prof, squash_y=1.0)

    def reg(i, j, c):
        if abs(c.x) < 0.035 and c.y > -0.15 and c.z > -0.2:
            return ACCENT
        if c.y < -0.2 and c.z < 0.05:
            return BELLY
        return BODY
    p.grid(rows, reg, caps=(Vector((0, 0, -0.31)), Vector((0, 0, 0.37))))
    # spots
    for x, y, z in ((0.22, 0.2, 0.2), (-0.24, 0.15, 0.12), (0.1, 0.3, -0.05), (-0.12, 0.32, 0.25)):
        n = Vector((x, y, z)).normalized()
        sx, up, nn = kit.surface_frame(n)
        p.slab([(math.cos(a) * 0.06, math.sin(a) * 0.06) for a in [2 * math.pi * k / 12 for k in range(12)]],
               Vector((x, y, z)) * 1.05, sx, up, nn, 0.012, ACCENT)
    # six little legs
    for s in (1, -1):
        for k, z in enumerate((-0.1, -0.2, -0.28)):
            a = Vector((s * 0.34, -0.05 + k * 0.1, z))
            b = Vector((s * 0.5, -0.08 + k * 0.12, z - 0.12))
            tube(p, [a, b], 0.03, ACCENT, segs=8)
    anchors("Bug", eye=(0, -0.42, 0.12), mouth=(0, -0.43, -0.05), horn=(0, -0.1, 0.38), wing=(0.22, 0.1, 0.22), tail=(0, 0.35, -0.2),
            crown=(0, 0, 0.44))
    return p


def body_jelly():
    p = part("Body_Jelly")
    # a dome with a frilly rim, tentacles hanging down
    prof = [(-0.08, 0.46), (0.06, 0.45), (0.2, 0.4), (0.32, 0.3), (0.42, 0.15), (0.47, 0.02)]
    rows = []
    segs = 40
    for z, r in prof:
        row = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            frill = 0.035 * math.cos(a * 10) if z < 0 else 0.0
            row.append(Vector((math.cos(a) * (r + frill), math.sin(a) * (r + frill) * 0.95, z + (frill * 0.8 if z < 0 else 0))))
        rows.append(row)
    under = [Vector((q.x * 0.82, q.y * 0.82, q.z + 0.03)) for q in rows[0]]
    p.grid([under] + rows, lambda i, j, c: BELLY if i == 0 or (c.y < -0.25 and c.z < 0.15) else BODY,
           caps=(Vector((0, 0, -0.02)), Vector((0, 0, 0.48))))
    for k in range(7):
        a = 2 * math.pi * k / 7 + 0.2
        root = Vector((math.cos(a) * 0.28, math.sin(a) * 0.26, -0.06))
        pts = [root + Vector((math.cos(a) * 0.05 * math.sin(t * 3), math.sin(a) * 0.05 * math.cos(t * 3), -0.45 * t)) for t in
               [i / 6 for i in range(7)]]
        tube(p, pts, [0.05 * (1 - 0.7 * t) + 0.01 for t in [i / 6 for i in range(7)]], ACCENT, segs=10)
    anchors("Jelly", eye=(0, -0.4, 0.2), mouth=(0, -0.43, 0.04), horn=(0, 0, 0.44), wing=(0.36, 0.1, 0.1), tail=(0, 0.3, -0.3),
            crown=(0, 0, 0.48))
    return p


def body_spiky():
    p = part("Body_Spiky")
    prof = [(-0.42, 0.04), (-0.38, 0.2), (-0.24, 0.36), (0.0, 0.42), (0.24, 0.36), (0.38, 0.2), (0.42, 0.04)]
    rows = blob_rows(prof, squash_y=0.96)
    p.grid(rows, BODY, caps=(Vector((0, 0, -0.43)), Vector((0, 0, 0.43))))
    # spikes all round but not on its face
    k = 0
    for el in (-30, 0, 30, 60):
        n = 7 if el < 50 else 5
        for i in range(n):
            az = 2 * math.pi * i / n + (0.4 if el % 60 else 0)
            d = Vector((math.cos(D(el)) * math.sin(az), math.cos(D(el)) * math.cos(az), math.sin(D(el))))
            if d.y < -0.55 and el < 50:
                continue
            base = d * 0.36
            tip = d * 0.62
            tube(p, [base, base.lerp(tip, 0.5), tip], [0.085, 0.05, 0.006], ACCENT, segs=10, caps=(True, False))
            k += 1
    p.ellipsoid((0, -0.29, -0.23), (0.22, 0.14, 0.16), region=BELLY, segs=22, rings=12)
    anchors("Spiky", eye=(0, -0.4, 0.08), mouth=(0, -0.4, -0.1), horn=(0, 0, 0.42), wing=(0.3, 0.16, 0.08), tail=(0, 0.36, -0.26),
            crown=(0, 0, 0.52))
    return p


def body_ghost():
    p = part("Body_Ghost")
    rows = []
    segs = 40
    prof = [(0.48, 0.05), (0.44, 0.22), (0.34, 0.34), (0.18, 0.4), (0.0, 0.41), (-0.2, 0.42), (-0.38, 0.44), (-0.48, 0.45)]
    for z, r in prof:
        row = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            wave = 0.07 * math.cos(a * 5) if z < -0.4 else 0.0
            row.append(Vector((math.cos(a) * r, math.sin(a) * r * 0.9, z + wave)))
        rows.append(row)
    rows.reverse()
    inner = [Vector((q.x * 0.9, q.y * 0.9, q.z + 0.05)) for q in rows[0]]
    p.grid([inner] + rows, lambda i, j, c: BELLY if i == 0 else BODY, caps=(Vector((0, 0, -0.3)), Vector((0, 0, 0.49))))
    for s in (1, -1):
        tube(p, bezier([(s * 0.38, -0.05, 0.05), (s * 0.55, -0.1, 0.12), (s * 0.6, -0.14, 0.24)], 6), [0.08, 0.07, 0.05], BODY, segs=12)
    anchors("Ghost", eye=(0, -0.38, 0.15), mouth=(0, -0.39, -0.02), horn=(0, 0, 0.45), wing=(0.32, 0.14, 0.1), tail=(0, 0.35, -0.3),
            crown=(0, 0, 0.5))
    return p


def body_crystal():
    p = part("Body_Crystal")
    bm = bmesh.new()
    # a faceted gem: a ring of eight round its middle, points top and bottom
    top = bm.verts.new((0, 0, 0.6))
    bottom = bm.verts.new((0, 0, -0.5))
    upper = [bm.verts.new((math.cos(a) * 0.36, math.sin(a) * 0.32, 0.22)) for a in [2 * math.pi * i / 8 + 0.2 for i in range(8)]]
    lower = [bm.verts.new((math.cos(a) * 0.42, math.sin(a) * 0.38, -0.08)) for a in [2 * math.pi * i / 8 for i in range(8)]]
    faces = []
    for i in range(8):
        j = (i + 1) % 8
        faces.append((bm.faces.new((upper[i], upper[j], top)), i))
        faces.append((bm.faces.new((lower[i], lower[j], upper[j], upper[i])), i + 1))
        faces.append((bm.faces.new((lower[j], lower[i], bottom)), i))
    bm.normal_update()
    bm.verts.index_update()
    for v in bm.verts:
        p.v(v.co.copy())
    for f, k in faces:
        p.face([v.index for v in f.verts], BODY if k % 2 == 0 else BELLY)
    bm.free()
    anchors("Crystal", eye=(0, -0.38, 0.04), mouth=(0, -0.38, -0.16), horn=(0, 0, 0.5), wing=(0.3, 0.12, 0.1), tail=(0, 0.33, -0.3),
            crown=(0, 0, 0.58))
    return p


def body_fish():
    p = part("Body_Fish")
    # a round fish looking at you: a fat oval body, a tail fin behind, fins on the sides and on top
    prof = [(-0.36, 0.04), (-0.32, 0.22), (-0.2, 0.34), (0.0, 0.38), (0.2, 0.34), (0.32, 0.22), (0.37, 0.04)]
    rows = blob_rows(prof, squash_y=1.12)
    p.grid(rows, BODY, caps=(Vector((0, 0, -0.37)), Vector((0, 0, 0.38))))
    sx, up, nn = Vector((1, 0, 0)), Vector((0, 0, 1)), Vector((0, 1, 0))
    p.slab([(0.0, 0.0), (0.16, 0.28), (0.3, 0.22), (0.22, 0.0), (0.3, -0.22), (0.16, -0.28)], Vector((0, 0.38, 0)), Vector((0, 1, 0)),
           Vector((0, 0, 1)), Vector((1, 0, 0)), 0.04, ACCENT, centre=(0.12, 0.0))
    p.slab([(-0.18, 0.0), (0.0, 0.2), (0.18, 0.0)], Vector((0, 0.05, 0.34)), Vector((0, 1, 0)), Vector((0, 0, 1)), Vector((1, 0, 0)),
           0.03, ACCENT, centre=(0.0, 0.06))
    for s in (1, -1):
        p.slab([(0.0, 0.0), (0.18, 0.1), (0.2, -0.06)], Vector((s * 0.36, -0.02, -0.05)), Vector((s, 0.2, 0)).normalized(),
               Vector((0, 0, 1)), Vector((0, 1, 0)), 0.025, ACCENT, centre=(0.1, 0.0))
    p.ellipsoid((0, -0.32, -0.21), (0.2, 0.13, 0.13), region=BELLY, segs=22, rings=12)
    anchors("Fish", eye=(0, -0.42, 0.08), mouth=(0, -0.43, -0.1), horn=(0, 0, 0.36), wing=(0.36, 0.0, 0.0), tail=(0, 0.45, 0.0),
            crown=(0, 0, 0.4))
    return p


# ------------------------------------------------------------------------------------------------- faces

def eye_pair(name, style, happy):
    """A pair of eyes round the origin (each ±0.17 across), on a gently curved front, facing -Y."""
    p = part(name)
    for s in (1, -1):
        c = Vector((s * 0.17, 0.0, 0.0))
        if style == 3 and s == -1:
            continue
        if style == 3:
            c = Vector((0, 0, 0.02))
        r = 0.15 if style == 3 else 0.105
        if happy and style == 1:
            # closed, smiling arcs (^ ^)
            arc = [c + Vector((math.cos(a) * r * 0.9, -0.02, math.sin(a) * r * 0.6 - r * 0.15)) for a in
                   [D(20 + 140 * k / 10) for k in range(11)]]
            tube(p, arc, 0.022, PUPIL, segs=8)
            continue
        # the white, the pupil, a shine (two when happy)
        p.ellipsoid(c, (r, r * 0.55, r * 1.15), region=WHITE, segs=20, rings=12)
        pr = r * (0.62 if happy else 0.55)
        p.ellipsoid(c + Vector((s * 0.008 if style != 3 else 0, -r * 0.42, -r * 0.08)), (pr, pr * 0.4, pr * (1.1 if happy else 0.95)),
                    region=PUPIL, segs=16, rings=10)
        p.ellipsoid(c + Vector((-r * 0.25, -r * 0.62, r * 0.32)), (r * 0.2, r * 0.08, r * 0.22), region=WHITE, segs=10, rings=6)
        if happy:
            p.ellipsoid(c + Vector((r * 0.22, -r * 0.6, -r * 0.3)), (r * 0.1, r * 0.06, r * 0.11), region=WHITE, segs=8, rings=6)
        else:
            # a grumpy lid over the top of the eye (the body's colour), slanting down toward the middle, and a brow
            lid_drop = {0: 0.38, 1: 0.62, 2: 0.48, 3: 0.42}[style]
            inside = -s if style != 3 else 0
            tilt = D(16) * (s if style != 3 else 0)
            ax_x = Vector((math.cos(tilt), 0, -math.sin(tilt)))
            ax_z = Vector((math.sin(tilt), 0, math.cos(tilt)))
            p.ellipsoid(c + Vector((0, -r * 0.12, r * (1.18 - lid_drop * 1.1))), (r * 1.12, r * 0.64, r * 0.62),
                        axes=(ax_x, Vector((0, 1, 0)), ax_z), region=BODY, segs=20, rings=10)
            if style == 3:
                brow = [c + Vector((-r * 1.15, -r * 0.58, r * 1.62)), c + Vector((0, -r * 0.66, r * 1.18)), c + Vector((r * 1.15, -r * 0.58, r * 1.62))]
            else:
                inner = c + Vector((-s * r * 1.05, -r * 0.6, r * 1.02))
                outer = c + Vector((s * r * 1.0, -r * 0.56, r * 1.72))
                brow = [inner, inner.lerp(outer, 0.5) + Vector((0, -0.006, 0.02)), outer]
            tube(p, bezier(brow, 8), [0.016, 0.015, 0.008], ACCENT, segs=8)
    # rosy cheeks when happy
    if happy:
        for s in (1, -1):
            p.ellipsoid(Vector((s * 0.27, 0.02, -0.12)), (0.055, 0.02, 0.035), region=CHEEK, segs=12, rings=6)
    return p


def mouth(name, style, happy):
    p = part(name)
    if happy:
        if style == 0:
            arc = [Vector((math.cos(a) * 0.08, -0.01, math.sin(a) * 0.05 + 0.02)) for a in [D(200 + 140 * k / 12) for k in range(13)]]
            tube(p, arc, 0.016, MOUTH, segs=8)
        elif style == 1:
            # an open smile with a tongue
            p.ellipsoid((0, -0.01, -0.005), (0.085, 0.03, 0.05), region=MOUTH, segs=18, rings=10)
            p.ellipsoid((0, -0.032, -0.03), (0.045, 0.018, 0.022), region=CHEEK, segs=12, rings=6)
            tube(p, [Vector((-0.09, -0.03, 0.035)), Vector((0, -0.036, 0.04)), Vector((0.09, -0.03, 0.035))], 0.014, BODY, segs=8)
        else:
            # a cat's "w"
            pts = [Vector((-0.08, -0.01, 0.02)), Vector((-0.04, -0.015, -0.02)), Vector((0, -0.01, 0.01)), Vector((0.04, -0.015, -0.02)),
                   Vector((0.08, -0.01, 0.02))]
            tube(p, bezier(pts, 12), 0.014, MOUTH, segs=8)
    else:
        if style == 0:
            arc = [Vector((math.cos(a) * 0.07, -0.01, math.sin(a) * 0.035 - 0.02)) for a in [D(20 + 140 * k / 12) for k in range(13)]]
            tube(p, arc, 0.016, MOUTH, segs=8)
        elif style == 1:
            # a jagged grin with little teeth
            pts = [Vector((-0.09, -0.01, 0.0)), Vector((0.09, -0.01, 0.0))]
            p.ellipsoid((0, -0.005, -0.01), (0.09, 0.025, 0.035), region=MOUTH, segs=16, rings=8)
            for k in range(4):
                x = -0.06 + k * 0.04
                tube(p, [Vector((x - 0.015, -0.03, 0.02)), Vector((x, -0.032, -0.012)), Vector((x + 0.015, -0.03, 0.02))], 0.008, WHITE, segs=6)
        else:
            # a pout
            p.ellipsoid((0, -0.01, 0), (0.045, 0.025, 0.028), region=MOUTH, segs=14, rings=8)
    return p


# ------------------------------------------------------------------------------------------------- horns, wings, tails, the crown

def horns(i):
    p = part(f"Horns_{i}")
    for s in (1, -1):
        if i == 0:
            base = Vector((s * 0.16, 0, 0))
            tube(p, bezier([base, base + Vector((s * 0.06, 0, 0.12)), base + Vector((s * 0.02, 0.02, 0.22))], 8), [0.065, 0.04, 0.006], 2,
                   segs=12, caps=(True, False))
        elif i == 1:
            base = Vector((s * 0.1, 0, 0))
            pts = bezier([base, base + Vector((s * 0.08, 0.02, 0.18)), base + Vector((s * 0.18, 0.0, 0.3))], 8)
            tube(p, pts, 0.018, BODY, segs=8)
            p.ellipsoid(pts[-1] + Vector((s * 0.02, 0, 0.03)), (0.05, 0.05, 0.05), region=ACCENT, segs=14, rings=8)
        elif i == 2:
            base = Vector((s * 0.14, 0, 0))
            tip = base + Vector((s * 0.12, 0.04, 0.42))
            tube(p, bezier([base, base.lerp(tip, 0.5) + Vector((s * 0.04, 0, 0)), tip], 8), [(0.08, 0.04), (0.07, 0.035), (0.02, 0.012)], BODY,
                   segs=12, front=Vector((0, -1, 0)))
            tube(p, bezier([base + Vector((0, -0.025, 0.05)), base.lerp(tip, 0.5) + Vector((s * 0.04, -0.03, 0)), tip + Vector((0, -0.015, -0.06))], 7),
                   [(0.045, 0.012), (0.04, 0.01), (0.012, 0.008)], ACCENT, segs=10, front=Vector((0, -1, 0)))
        else:
            for k, (dx, h) in enumerate(((0.0, 0.26), (0.12, 0.18))):
                if k == 0 and s == -1:
                    continue
                base = Vector((s * dx, 0, 0))
                tube(p, [base, base + Vector((s * 0.01, 0, h * 0.6)), base + Vector((s * 0.02, 0, h))], [0.06, 0.035, 0.004], ACCENT, segs=6,
                       caps=(True, False))
    return p


def wing(i):
    """One wing reaching out along +X from its hinge at the origin (the game mirrors it for the other side)."""
    p = part(f"Wing_{i}")
    sx, up, nn = Vector((1, 0.25, 0)).normalized(), Vector((0, 0, 1)), Vector((-0.25, 1, 0)).normalized()
    if i == 0:
        outline = [(0.0, 0.04), (0.14, 0.18), (0.32, 0.24), (0.46, 0.14), (0.4, 0.04), (0.36, -0.08), (0.27, -0.02), (0.2, -0.1),
                   (0.13, -0.02), (0.05, -0.06)]
        p.slab(outline, Vector((0, 0, 0)), sx, up, nn, 0.02, WING, side_region=ACCENT, centre=(0.2, 0.06))
        for a, b in (((0.0, 0.04), (0.14, 0.18)), ((0.14, 0.18), (0.32, 0.24)), ((0.32, 0.24), (0.46, 0.14)), ((0.14, 0.18), (0.36, -0.08)),
                     ((0.14, 0.18), (0.2, -0.1))):
            pa = sx * a[0] + up * a[1] + nn * 0.012
            pb = sx * b[0] + up * b[1] + nn * 0.012
            tube(p, [pa, pb], 0.014, ACCENT, segs=8, cap_scale=0.8)
    elif i == 1:
        for k, (ang, length, width) in enumerate(((22, 0.42, 0.13), (-12, 0.34, 0.1))):
            a = D(ang)
            d = Vector((math.cos(a), 0.2, math.sin(a))).normalized()
            c = d * length * 0.5
            p.ellipsoid(c, (length * 0.5, 0.012, width), axes=(d, Vector((0, 1, 0)), d.cross(Vector((0, 1, 0))).normalized() * -1),
                        region=WING, segs=20, rings=8)
            tube(p, [d * 0.02, d * length * 0.95], 0.008, ACCENT, segs=6)
    else:
        for k, (ang, length, width) in enumerate(((40, 0.38, 0.08), (18, 0.42, 0.085), (-4, 0.36, 0.08), (-24, 0.28, 0.07))):
            a = D(ang)
            d = Vector((math.cos(a), 0.25, math.sin(a))).normalized()
            c = d * length * 0.55
            p.ellipsoid(c, (length * 0.55, 0.022, width), axes=(d, Vector((0, 1, 0)), d.cross(Vector((0, 1, 0))).normalized() * -1),
                        region=WING if k % 2 == 0 else ACCENT, segs=18, rings=8)
        p.ellipsoid((0.06, 0.02, 0.04), (0.09, 0.05, 0.07), region=WING, segs=14, rings=8)
    return p


def tail(i):
    p = part(f"Tail_{i}")
    if i == 0:
        pts = []
        for k in range(16):
            t = k / 15
            a = t * math.pi * 3.2
            r = 0.12 * (1 - t * 0.6)
            pts.append(Vector((math.sin(a) * r * 0.4, 0.05 + t * 0.22 + math.cos(a) * r * 0.2, 0.02 + math.sin(a) * r)))
        tube(p, pts, [0.05 * (1 - 0.6 * k / 15) + 0.01 for k in range(16)], BODY, segs=10)
    else:
        pts = bezier([(0, 0, 0), (0.0, 0.18, -0.06), (0.06, 0.32, 0.06), (0.02, 0.36, 0.18)], 12)
        tube(p, pts, 0.03, BODY, segs=10)
        end = pts[-1]
        d = (pts[-1] - pts[-3]).normalized()
        sx = d.cross(Vector((0, 1, 0))).normalized()
        p.slab([(0.0, 0.0), (0.06, -0.02), (0.0, 0.1), (-0.06, -0.02)], end - d * 0.01, sx, d, sx.cross(d).normalized(), 0.025, ACCENT,
               centre=(0.0, 0.02))
    return p


def crown_part():
    p = part("Crown")
    z0, r_out, r_in = 0.0, 0.22, 0.2
    segs = 60
    rows = []
    for r, top in ((r_in, False), (r_out, False), (r_out, True), (r_in, True)):
        row = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            spike = max(0.0, math.cos(5 * a)) ** 3
            z = z0 + (0.08 + 0.1 * spike if top else 0.0)
            row.append(Vector((math.sin(a) * r, -math.cos(a) * r * 0.95, z)))
        rows.append(row)
    p.grid(rows, ACCENT, loop=True)
    for k in range(5):
        a = 2 * math.pi * k / 5
        tip = Vector((math.sin(a) * (r_out - 0.008), -math.cos(a) * (r_out - 0.008) * 0.95, z0 + 0.19))
        p.ellipsoid(tip, (0.022, 0.022, 0.022), region=ACCENT, segs=12, rings=8)
        gem = Vector((math.sin(a) * (r_out + 0.004), -math.cos(a) * (r_out + 0.004) * 0.95, z0 + 0.045))
        n = Vector((math.sin(a), -math.cos(a), 0))
        sx, up, nn = kit.surface_frame(n)
        p.ellipsoid(gem, (0.02, 0.02, 0.008), axes=(sx, up, nn), region=CHEEK, segs=12, rings=8)
    return p


# ------------------------------------------------------------------------------------------------- building

def all_parts():
    out = [body_blob(), body_imp(), body_bat(), body_puff(), body_bug(), body_jelly(), body_spiky(), body_ghost(), body_crystal(),
           body_fish()]
    for s in range(4):
        out.append(eye_pair(f"Eyes_{s}_gloom", s, False))
        out.append(eye_pair(f"Eyes_{s}_happy", s, True))
    for s in range(3):
        out.append(mouth(f"Mouth_{s}_gloom", s, False))
        out.append(mouth(f"Mouth_{s}_happy", s, True))
    for i in range(4):
        out.append(horns(i))
    for i in range(3):
        out.append(wing(i))
    for i in range(2):
        out.append(tail(i))
    out.append(crown_part())
    return out


def to_object(pt, coll):
    """A static mesh: uv0 nothing, uv1 'Region' = (region, ambient occlusion), a preview colour per face."""
    me = bpy.data.meshes.new(pt.name)
    # faces that repeat a vertex (a tube's tip, a cap's centre on the ring) are dropped: Blender can't hold them
    faces, regions = [], []
    for f, r in zip(pt.faces, pt.freg):
        if len(set(f)) == len(f) and len(f) >= 3:
            faces.append(list(f))
            regions.append(r)
    pt.faces, pt.freg = faces, regions
    me.from_pydata([tuple(c) for c in pt.co], [], faces)
    me.update()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    uv0 = me.uv_layers.new(name="UVMap")
    uv1 = me.uv_layers.new(name="Region")
    col = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    for poly in me.polygons:
        reg = pt.freg[poly.index]
        c = [ta.srgb_to_linear(x) for x in ta.hex_rgb(PREVIEW[reg])]
        for li in poly.loop_indices:
            uv0.data[li].uv = (0.0, 0.0)
            uv1.data[li].uv = (reg + 0.0, 1.0)
            col.data[li].color = (*c, 1.0)
    # crystals stay faceted, everything else smooth
    if pt.name == "Body_Crystal":
        me.shade_flat()
    else:
        me.shade_smooth()
    obj = bpy.data.objects.new(pt.name, me)
    coll.objects.link(obj)
    obj.data.materials.append(kit.preview_material())
    return obj


def build():
    ta.clear_scene()
    coll = ta.collection("Gloom")
    for o in list(coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    made = [to_object(p, coll) for p in all_parts()]
    return {"pieces": len(made), "tris": sum(ta.tri_count(o) for o in made)}


def objects():
    return [o for o in bpy.data.collections["Gloom"].objects if o.type == "MESH"]


def bake_ao(samples=32):
    """Each piece's ambient occlusion (on its own) into its uv1.y (0.4 + 0.6 * occlusion)."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.render.bake.target = "VERTEX_COLORS"
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.light_settings.distance = 0.3
    for target in objects():
        for o in objects():
            o.hide_render = o is not target
            o.hide_set(o is not target)
        me = target.data
        attr = me.color_attributes.get("AO") or me.color_attributes.new("AO", "FLOAT_COLOR", "POINT")
        me.color_attributes.active_color = attr
        for o in bpy.context.view_layer.objects:
            o.select_set(False)
        target.select_set(True)
        bpy.context.view_layer.objects.active = target
        bpy.ops.object.bake(type="AO")
        uv = me.uv_layers["Region"]
        for poly in me.polygons:
            for li in poly.loop_indices:
                c = attr.data[me.loops[li].vertex_index].color
                ao = (c[0] + c[1] + c[2]) / 3
                uv.data[li].uv = (uv.data[li].uv[0], 0.4 + 0.6 * ao)
    for o in objects():
        o.hide_render = False
        o.hide_set(False)
    return len(objects())


# ------------------------------------------------------------------------------------------------- previews

SAMPLES = [  # body, eyes, mouth, horns, wings, tail, hue
    ("Blob", 0, 0, 0, -1, 0, "#C7A8FF"), ("Imp", 2, 1, 2, 0, 1, "#FF9CC6"), ("Bat", 1, 0, -1, 0, -1, "#9FB6FF"),
    ("Puff", 0, 2, 1, 2, -1, "#A9E8FF"), ("Bug", 3, 0, 1, 1, -1, "#B6F09A"), ("Jelly", 2, 2, -1, -1, -1, "#FFB8E4"),
    ("Spiky", 0, 1, 3, -1, 0, "#FFC98A"), ("Ghost", 1, 2, 0, 2, -1, "#E8E4FF"), ("Crystal", 3, 0, 3, 1, -1, "#9FF2E8"),
    ("Fish", 0, 1, -1, -1, -1, "#7FD8FF"),
]


def assemble(coll, i, spec, happy, offset):
    """A creature put together the way GloomViews does (for the previews)."""
    body, eyes, mth, hrn, wng, tl, hue = spec
    a = ANCHORS[body]

    def B(u):   # the game's (x, y, z) -> Blender
        return Vector((u[0], -u[2], u[1]))
    names = [(f"Body_{body}", Vector(), 1.0, 1)]
    names.append((f"Eyes_{eyes}_{'happy' if happy else 'gloom'}", B(a["eye"]), a["eyeSize"], 1))
    names.append((f"Mouth_{mth}_{'happy' if happy else 'gloom'}", B(a["mouth"]), a["mouthSize"], 1))
    if hrn >= 0:
        names.append((f"Horns_{hrn}", B(a["horn"]), a["hornSize"], 1))
    if wng >= 0:
        w = B(a["wing"])
        names.append((f"Wing_{wng}", Vector((w.x, w.y, w.z)), a["wingSize"], 1))
        names.append((f"Wing_{wng}", Vector((-w.x, w.y, w.z)), a["wingSize"], -1))
    if tl >= 0:
        names.append((f"Tail_{tl}", B(a["tail"]), a["tailSize"], 1))
    colours = list(PREVIEW)
    colours[0] = hue
    if not happy:
        colours = ["#5A5070", "#77708A", "#6A5A84", "#2A2438", "#D06CFF", "#3A2440", "#7C7690", "#5A5070"]
    for n, at, size, side in names:
        src = bpy.data.objects.get(n)
        if src is None:
            continue
        me = src.data.copy()
        o = bpy.data.objects.new(f"pv{i}_{n}_{side}", me)
        o.location = offset + at
        o.scale = (size * side, size, size)
        coll.objects.link(o)
        col = me.color_attributes["Col"]
        reg = me.uv_layers["Region"]
        for poly in me.polygons:
            for li in poly.loop_indices:
                u, v = reg.data[li].uv
                c = [ta.srgb_to_linear(x) for x in ta.hex_rgb(colours[int(round(u))])]
                col.data[li].color = (*(x * v for x in c), 1.0)


def preview():
    out_dir = os.path.join(HERE, "previews")
    os.makedirs(out_dir, exist_ok=True)
    paths = []
    for happy in (False, True):
        coll = ta.collection("GloomPreview")
        for o in list(coll.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        for o in objects():
            o.hide_render = True
        for i, spec in enumerate(SAMPLES):
            r, c = divmod(i, 5)
            assemble(coll, i, spec, happy, Vector(((c - 2) * 1.35, r * 1.6, -r * 1.25)))
        ta.preview_rig(target=(0, 0.8, -0.6), distance=10.5, yaw_deg=0, pitch_deg=8, lens=50)
        bg = bpy.context.scene.world.node_tree.nodes.get("Background")
        bg.inputs["Color"].default_value = (*[ta.srgb_to_linear(x) for x in ta.hex_rgb("#2A2F66" if not happy else "#F4ECF7")], 1)
        paths.append(ta.render_preview(os.path.join(out_dir, f"gloom_{'happy' if happy else 'gloomy'}.png"), res=(2000, 1100)))
    for o in objects():
        o.hide_render = False
    return paths


def export():
    os.makedirs(UNITY_ART, exist_ok=True)
    for o in objects():
        for name in ("Col", "AO"):
            attr = o.data.color_attributes.get(name)
            if attr:
                o.data.color_attributes.remove(attr)
    path = os.path.join(UNITY_ART, "GloomKit.fbx")
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects():
        o.location = (0, 0, 0)
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects()[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, bake_anim=False, mesh_smooth_type="FACE",
        use_mesh_modifiers=True, path_mode="STRIP", colors_type="LINEAR")
    with open(os.path.join(UNITY_ART, "gloom_kit.json"), "w", encoding="utf-8") as f:
        json.dump({"regions": REGION_NAMES, "bodies": list(ANCHORS.values())}, f, indent=1)
    return path
