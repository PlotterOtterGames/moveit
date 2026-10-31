"""Sporte!'s athletes: one stylised toy athlete in kit pieces, rigged (run inside Blender 5.1; `build.py` runs it all
headless).

    build()      every piece as its own mesh object, skinned to the armature "AthleteRig" (in "Athlete")
    bake_ao()    ambient occlusion baked per vertex (Cycles) into each piece's second UV map
    preview()    renders kits on a line-up to art/sports/previews/

Style: chunky rounded toy proportions (about 4.8 heads tall, like Wii Sports Resort's people), smooth lofted body
parts with crisp clothing edges (each garment is its own shell, with a thick hem), cute bean eyes. Colours are not in
the model: every vertex says which REGION it is (skin, shirt, shorts, ...) in its second UV map (u = region, v = baked
ambient occlusion) and the game colours the regions per character (Tundu/Athlete shader), so one model dresses a
whole football team, the runners and the beach players. The game picks the pieces (shirt with or without sleeves, long
or short socks, shoes or bare feet, gloves, a headband, one of eight hairstyles) and merges them into one mesh.

Facing -Y (Unity +Z), feet on Z = 0, the character's left = +X, metres.
"""

import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "tools"))
import tundu_art as ta  # noqa: E402
import tundu_rig as tr  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
UNITY_ART = os.path.join(ta.REPO, "unity", "MoveItGames", "Assets", "Games", "Sports", "Art")

# Region numbers: the shader's colour table (Tundu/Athlete _Regions[]); keep in step with AthleteLook.cs.
REGIONS = ["skin", "shirt", "shirt_accent", "shorts", "shorts_accent", "socks", "socks_accent", "shoes", "soles",
           "hair", "eyes", "eye_shine", "mouth", "mouth_open", "eyes_closed", "blush", "gloves", "gloves_accent",
           "headband", "tongue"]
R = {name: i for i, name in enumerate(REGIONS)}

# preview colours (the game sets its own per character)
PREVIEW = {
    "skin": "#F2C6A0", "shirt": "#31C6F4", "shirt_accent": "#FFFFFF", "shorts": "#FFFFFF", "shorts_accent": "#31C6F4",
    "socks": "#31C6F4", "socks_accent": "#FFFFFF", "shoes": "#FF5A4E", "soles": "#FFFFFF", "hair": "#4A2E1E",
    "eyes": "#1C1830", "eye_shine": "#FFFFFF", "mouth": "#7A2E2E", "mouth_open": "#5A1E28", "eyes_closed": "#1C1830",
    "blush": "#F59C8F", "gloves": "#FFC93C", "gloves_accent": "#141938", "headband": "#FF5A4E", "tongue": "#F07A7A",
}

# ---------------------------------------------------------------------------------------------------- proportions

HEAD_C = Vector((0.0, 0.0, 1.665))
HEAD_R = (0.176, 0.168, 0.190)          # x (side), y (front-back), z
SHOULDER = Vector((0.205, 0.010, 1.345))  # the left shoulder joint (right = mirrored)
ARM_DIR = Vector((math.cos(math.radians(50)), 0.0, -math.sin(math.radians(50))))   # A-pose, 50 degrees down
UPPER_ARM, FOREARM, HAND = 0.255, 0.235, 0.085
ELBOW = SHOULDER + ARM_DIR * UPPER_ARM
WRIST = ELBOW + ARM_DIR * FOREARM
HIP = Vector((0.095, 0.0, 0.875))
KNEE = Vector((0.100, -0.005, 0.485))
ANKLE = Vector((0.100, 0.015, 0.095))
TOE = Vector((0.105, -0.135, 0.030))

# the torso's sections: z, half width, half depth, centre y (forward is -), superellipse power
TORSO = [
    (0.785, 0.105, 0.085, 0.010, 2.0),
    (0.80, 0.122, 0.095, 0.010, 2.1),
    (0.84, 0.150, 0.108, 0.012, 2.2),
    (0.89, 0.168, 0.118, 0.012, 2.3),
    (0.95, 0.166, 0.117, 0.008, 2.3),
    (1.01, 0.158, 0.113, 0.002, 2.3),
    (1.08, 0.163, 0.118, -0.006, 2.3),
    (1.15, 0.176, 0.125, -0.012, 2.35),
    (1.22, 0.186, 0.127, -0.012, 2.4),
    (1.28, 0.191, 0.121, -0.006, 2.4),
    (1.33, 0.184, 0.110, 0.000, 2.3),
    (1.37, 0.160, 0.097, 0.004, 2.2),
    (1.40, 0.122, 0.083, 0.008, 2.1),
    (1.425, 0.080, 0.070, 0.010, 2.0),
    (1.44, 0.064, 0.062, 0.010, 2.0),
]


def torso_section(z):
    """(centre y, half width, half depth, power) at height z, smoothly interpolated."""
    t = TORSO
    if z <= t[0][0]:
        return t[0][3], t[0][1], t[0][2], t[0][4]
    if z >= t[-1][0]:
        return t[-1][3], t[-1][1], t[-1][2], t[-1][4]
    for i in range(len(t) - 1):
        if t[i][0] <= z <= t[i + 1][0]:
            k = tr.ease((z - t[i][0]) / (t[i + 1][0] - t[i][0]))
            a, b = t[i], t[i + 1]
            return (tr.lerp(a[3], b[3], k), tr.lerp(a[1], b[1], k), tr.lerp(a[2], b[2], k), tr.lerp(a[4], b[4], k))


def profile(points, s):
    """Linear-eased interpolation in a [(s, value)] table."""
    if s <= points[0][0]:
        return points[0][1]
    for i in range(len(points) - 1):
        a, b = points[i], points[i + 1]
        if a[0] <= s <= b[0]:
            return tr.lerp(a[1], b[1], tr.ease((s - a[0]) / (b[0] - a[0])))
    return points[-1][1]


ARM_R = [(-0.05, 0.063), (-0.01, 0.068), (0.06, 0.066), (0.16, 0.058), (0.255, 0.051), (0.29, 0.055), (0.36, 0.053),
         (0.44, 0.045), (0.49, 0.041), (0.51, 0.039)]
LEG_R = [(-0.02, 0.092), (0.08, 0.092), (0.20, 0.085), (0.32, 0.073), (0.39, 0.067), (0.46, 0.071), (0.53, 0.074),
         (0.64, 0.062), (0.74, 0.052), (0.79, 0.049), (0.81, 0.047)]


def leg_point(side, s):
    """A point on the leg's axis, s metres from the hip joint (thigh then shin)."""
    h, k, a = mirror(HIP, side), mirror(KNEE, side), mirror(ANKLE, side)
    lt = (k - h).length
    if s <= lt:
        return h + (k - h) * (s / lt), (k - h).normalized()
    ls = (a - k).length
    return k + (a - k) * min(1.0, (s - lt) / ls), (a - k).normalized()


THIGH_LEN = (KNEE - HIP).length
SHIN_LEN = (ANKLE - KNEE).length


def mirror(v, side):
    return Vector((v.x * side, v.y, v.z))


# ---------------------------------------------------------------------------------------------------- the head

def jaw(uz):
    """How much narrower the head is at unit height uz (a softer, rounder chin)."""
    return 1.0 - 0.13 * max(0.0, -uz) ** 2


def head_point(u):
    """The head's surface along unit direction u (from the head's centre)."""
    k = jaw(u.z)
    return HEAD_C + Vector((u.x * HEAD_R[0] * k, u.y * HEAD_R[1] * k, u.z * HEAD_R[2]))


def head_dir(az, el):
    """Unit direction: azimuth from the front (+ toward the character's left), elevation up."""
    return Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))


def head_normal(az, el):
    e = 1e-3
    p0 = head_point(head_dir(az, el))
    pa = head_point(head_dir(az + e, el))
    pe = head_point(head_dir(az, el + e))
    n = (pa - p0).cross(pe - p0)
    if n.dot(p0 - HEAD_C) < 0:
        n = -n
    return n.normalized()


def face_point(x, z):
    """The point on the face (the front of the head) at width x and height z, with its normal."""
    uz = max(-0.99, min(0.99, (z - HEAD_C.z) / HEAD_R[2]))
    ux = x / (HEAD_R[0] * jaw(uz))
    uy = -math.sqrt(max(0.0, 1.0 - ux * ux - uz * uz))
    u = Vector((ux, uy, uz)).normalized()
    az = math.atan2(u.x, -u.y)
    el = math.asin(max(-1.0, min(1.0, u.z)))
    return head_point(u), head_normal(az, el)


# ---------------------------------------------------------------------------------------------------- mesh building

class Part:
    """Vertices with bone weights, a region and texture coordinates; faces oriented outward from a given inside."""

    def __init__(self, name):
        self.name = name
        self.co, self.w, self.reg, self.uv, self.faces = [], [], [], [], []

    def v(self, co, weights, region, uv=(0.0, 0.0)):
        self.co.append(Vector(co))
        self.w.append(dict(weights(Vector(co)) if callable(weights) else weights))
        self.reg.append(R[region] if isinstance(region, str) else region)
        self.uv.append(uv)
        return len(self.co) - 1

    def ring(self, pts, weights, region, uvs=None):
        return [self.v(p, weights, region, uvs[i] if uvs else (0, 0))
                for i, p in enumerate(pts)]

    def face(self, idx, inside):
        """Adds a polygon, flipped if its normal points toward `inside`."""
        pts = [self.co[i] for i in idx]
        n = Vector((0, 0, 0))
        for i in range(len(pts)):
            a, b = pts[i], pts[(i + 1) % len(pts)]
            n += Vector(((a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y)))
        centre = sum(pts, Vector()) / len(pts)
        if n.dot(centre - Vector(inside)) < 0:
            idx = list(reversed(idx))
        self.faces.append(tuple(idx))

    def bridge(self, a, b, inside_a, inside_b, closed=True):
        n = len(a)
        mid = (Vector(inside_a) + Vector(inside_b)) / 2
        for i in range(n if closed else n - 1):
            j = (i + 1) % n
            self.face([a[i], a[j], b[j], b[i]], mid)

    def fan(self, ring, tip, inside):
        n = len(ring)
        for i in range(n):
            self.face([ring[i], ring[(i + 1) % n], tip], inside)

    def ellipsoid(self, centre, radii, axes, region, weights, segs=16, rings=10):
        """axes: three unit vectors (radii along each)."""
        centre = Vector(centre)
        ax = [Vector(a).normalized() for a in axes]
        rows = []
        bottom = self.v(centre - ax[2] * radii[2], weights, region)
        for j in range(1, rings):
            el = -math.pi / 2 + math.pi * j / rings
            row = []
            for i in range(segs):
                az = 2 * math.pi * i / segs
                p = centre + (ax[0] * math.cos(az) * radii[0] + ax[1] * math.sin(az) * radii[1]) * math.cos(el) \
                    + ax[2] * math.sin(el) * radii[2]
                row.append(self.v(p, weights, region))
            rows.append(row)
        top = self.v(centre + ax[2] * radii[2], weights, region)
        self.fan(rows[0], bottom, centre)
        for a, b in zip(rows, rows[1:]):
            self.bridge(a, b, centre, centre)
        self.fan(rows[-1], top, centre)

    def tube(self, rings, region, weight_fn, caps=(True, True), uv_fn=None):
        """rings: [(centre, side axis, front axis, half width, half depth, power)] along a path; rounded caps."""
        made = []
        for c, xa, ya, rx, ry, pw in rings:
            pts = superellipse(c, xa, ya, rx, ry, pw, self._n)
            uvs = [uv_fn(c, p, i) for i, p in enumerate(pts)] if uv_fn else None
            made.append((self.ring(pts, weight_fn, region, uvs), c))
        for (a, ca), (b, cb) in zip(made, made[1:]):
            self.bridge(a, b, ca, cb)
        for end, (ring, c) in ((0, made[0]), (1, made[-1])):
            if not caps[end]:
                continue
            other = made[1][1] if end == 0 else made[-2][1]
            out = (c - other).normalized()
            rx, ry = rings[0 if end == 0 else -1][3], rings[0 if end == 0 else -1][4]
            r = (rx + ry) / 2
            prev, prev_c = ring, c
            for k, f in ((1, 0.75), (2, 0.4)):
                cc = c + out * r * (0.5 if k == 1 else 0.85)
                pts = [cc + (self.co[i] - c) * f for i in ring]
                nxt = self.ring(pts, weight_fn, region)
                self.bridge(prev, nxt, prev_c - out * 0.01, cc - out * 0.01)
                prev, prev_c = nxt, cc
            tip = self.v(c + out * r, weight_fn(c + out * r), region)
            self.fan(prev, tip, c)
        return made

    _n = 20

    def segments(self, n):
        self._n = n
        return self

    def merge(self, other):
        base = len(self.co)
        self.co += other.co
        self.w += other.w
        self.reg += other.reg
        self.uv += other.uv
        self.faces += [tuple(i + base for i in f) for f in other.faces]
        return self

    def to_object(self, coll, arm):
        me = bpy.data.meshes.new(self.name)
        me.from_pydata([tuple(c) for c in self.co], [], self.faces)
        me.update()
        uv0 = me.uv_layers.new(name="UVMap")
        uv1 = me.uv_layers.new(name="Region")
        for poly in me.polygons:
            for li in poly.loop_indices:
                vi = me.loops[li].vertex_index
                uv0.data[li].uv = self.uv[vi]
                uv1.data[li].uv = (self.reg[vi] + 0.0, 1.0)
        col = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
        for vi, r in enumerate(self.reg):
            c = [ta.srgb_to_linear(x) for x in ta.hex_rgb(PREVIEW[REGIONS[r]])]
            col.data[vi].color = (*c, 1.0)
        me.shade_smooth()
        obj = bpy.data.objects.new(self.name, me)
        coll.objects.link(obj)
        for vi, weights in enumerate(self.w):
            for bone, wt in weights.items():
                if wt <= 1e-4:
                    continue
                g = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
                g.add([vi], wt, "REPLACE")
        obj.parent = arm
        mod = obj.modifiers.new("Armature", "ARMATURE")
        mod.object = arm
        obj.data.materials.append(preview_material())
        return obj


def superellipse(c, xa, ya, rx, ry, power, n):
    pts = []
    e = 2.0 / power
    for i in range(n):
        a = 2 * math.pi * i / n
        ca, sa = math.cos(a), math.sin(a)
        x = math.copysign(abs(ca) ** e, ca)
        y = math.copysign(abs(sa) ** e, sa)
        pts.append(c + xa * (x * rx) + ya * (y * ry))
    return pts


def frame(direction, front_hint=Vector((0, -1, 0))):
    """Side and front axes perpendicular to a path direction (the front axis as close to front_hint as can be)."""
    d = direction.normalized()
    f = front_hint - d * front_hint.dot(d)
    if f.length < 1e-4:
        f = Vector((0, 0, 1)) - d * d.z
    f.normalize()
    return d.cross(f).normalized(), f   # side axis, front axis


def blend(w):
    total = sum(w.values()) or 1.0
    return {k: v / total for k, v in w.items() if v > 1e-4}


def side_name(side):
    return "L" if side > 0 else "R"


# ---------------------------------------------------------------------------------------------------- weights

def torso_weights(p):
    z = p.z
    hips = 1.0 - tr.smoothstep(0.97, 1.06, z)
    chest = tr.smoothstep(1.12, 1.24, z)
    w = {"Hips": hips, "Spine": max(0.0, 1.0 - hips - chest), "Chest": chest}
    k = tr.smoothstep(0.12, 0.19, abs(p.x)) * tr.smoothstep(1.22, 1.32, z) * 0.55
    if k > 0:
        w["Chest"] *= (1 - k)
        w["Spine"] *= (1 - k)
        w[f"Shoulder.{side_name(1 if p.x > 0 else -1)}"] = k
    return blend(w)


def arm_weights(side, s):
    n = side_name(side)
    fore = tr.smoothstep(UPPER_ARM - 0.04, UPPER_ARM + 0.035, s)
    sh = (1.0 - tr.smoothstep(-0.06, 0.0, s)) * 0.35
    return blend({f"Shoulder.{n}": sh, f"UpperArm.{n}": (1 - fore) * (1 - sh), f"Forearm.{n}": fore})


def leg_weights(side, s):
    n = side_name(side)
    shin = tr.smoothstep(THIGH_LEN - 0.04, THIGH_LEN + 0.04, s)
    hip = (1.0 - tr.smoothstep(-0.02, 0.06, s)) * 0.35
    foot = tr.smoothstep(THIGH_LEN + SHIN_LEN - 0.02, THIGH_LEN + SHIN_LEN + 0.03, s) * 0.5
    return blend({"Hips": hip, f"Thigh.{n}": (1 - shin) * (1 - hip), f"Shin.{n}": shin * (1 - foot), f"Foot.{n}": foot})


# ---------------------------------------------------------------------------------------------------- pieces

def torso_rings(z0, z1, offset, flare_bottom=0.0, flare_top=0.0, steps=26):
    """offset: metres outside the body, or a function of z."""
    rings = []
    for i in range(steps + 1):
        z = tr.lerp(z0, z1, i / steps)
        cy, rx, ry, pw = torso_section(z)
        extra = (offset(z) if callable(offset) else offset) + flare_bottom * (1 - tr.smoothstep(0.0, 0.25, i / steps)) + flare_top * tr.smoothstep(0.75, 1.0, i / steps)
        rings.append((Vector((0, cy, z)), Vector((1, 0, 0)), Vector((0, -1, 0)), rx + extra, ry + extra, pw))
    return rings


def cyl_uv(c, p, i, z0=0.935, z1=1.44):
    az = math.atan2(p.x, -(p.y - c.y))           # 0 = the front, pi = the back
    return ((az / (2 * math.pi)) % 1.0, (p.z - z0) / (z1 - z0))


def shirt():
    part = Part("Shirt").segments(28)
    rings = torso_rings(0.948, 1.44, 0.008, flare_bottom=0.014)
    # the hem: an inner lip, the bottom edge, then the shell up to the collar
    cy, rx, ry, pw = torso_section(0.95)
    hem_in = (Vector((0, cy, 0.960)), Vector((1, 0, 0)), Vector((0, -1, 0)), rx + 0.009, ry + 0.009, pw)
    hem_low = (Vector((0, cy, 0.936)), Vector((1, 0, 0)), Vector((0, -1, 0)), rx + 0.016, ry + 0.016, pw)
    made = part.tube([hem_in, hem_low] + rings, "shirt", torso_weights, caps=(False, False), uv_fn=cyl_uv)
    # the collar: a soft rolled ring around the neck opening
    collar = Part("Collar")
    cy, rx, ry, pw = torso_section(1.44)
    ring_pts = superellipse(Vector((0, cy, 1.442)), Vector((1, 0, 0)), Vector((0, -1, 0)), rx + 0.012, ry + 0.012, pw, 32)
    centre = Vector((0, cy, 1.442))
    rows = []
    for k in range(10):
        a = 2 * math.pi * k / 10
        row = []
        for p in ring_pts:
            radial = (p - centre)
            radial.z = 0
            radial.normalize()
            q = p + radial * (math.cos(a) * 0.014) + Vector((0, 0, math.sin(a) * 0.012))
            row.append(collar.v(q, torso_weights(q), "shirt_accent"))
        rows.append((row, p))
    for i in range(10):
        a, b = rows[i][0], rows[(i + 1) % 10][0]
        # the tube's own axis is the ring of points: orient by the local ring point
        for j in range(32):
            jn = (j + 1) % 32
            mid = ring_pts[j]
            collar.face([a[j], a[jn], b[jn], b[j]], mid)
    part.merge(collar)
    return part


def arm_rings(side, s0, s1, extra=0.0, flare_end=0.0, steps=14, r_table=ARM_R):
    sh = mirror(SHOULDER, side)
    d = Vector((ARM_DIR.x * side, ARM_DIR.y, ARM_DIR.z))
    xa, fa = frame(d)
    rings = []
    for i in range(steps + 1):
        s = tr.lerp(s0, s1, i / steps)
        r = profile(r_table, s) + extra + flare_end * tr.smoothstep(0.6, 1.0, i / steps)
        rings.append((sh + d * s, xa, fa, r, r * 0.93, 2.0))
    return rings, d


def arm_piece(side):
    part = Part(f"Arm.{side_name(side)}").segments(16)
    rings, d = arm_rings(side, -0.05, 0.505, steps=22)
    part.tube(rings, "skin", lambda c: arm_weights(side, (c - mirror(SHOULDER, side)).dot(d)))
    return part


def sleeve(side):
    part = Part(f"Sleeve.{side_name(side)}").segments(18)
    rings, d = arm_rings(side, -0.07, 0.112, extra=0.016, flare_end=0.006, steps=8)
    end_c, xa, fa, r, ry, pw = rings[-1]
    lip = (end_c - d * 0.012, xa, fa, r - 0.012, ry - 0.012, pw)
    edge = (end_c + d * 0.004, xa, fa, r - 0.003, ry - 0.003, pw)
    wf = lambda c: arm_weights(side, (c - mirror(SHOULDER, side)).dot(d))
    part.tube(rings + [edge, lip], "shirt", wf, caps=(True, False))
    # the sleeve's band in the accent colour
    band = Part("band").segments(18)
    b_rings, _ = arm_rings(side, 0.088, 0.110, extra=0.0215, flare_end=0.004, steps=2)
    band.tube(b_rings, "shirt_accent", wf, caps=(False, False))
    return part.merge(band)


def fist(side, scale=1.0, region="skin", cuff=None):
    n = side_name(side)
    d = Vector((ARM_DIR.x * side, ARM_DIR.y, ARM_DIR.z))
    xa, fa = frame(d)
    inward = xa * side
    scale *= 1.2
    c = mirror(WRIST, side) + d * 0.058 * scale
    part = Part(f"Hand.{n}")
    w = {f"Hand.{n}": 1.0}
    # the fist: a rounded block, the knuckles forward
    part.ellipsoid(c, (0.047 * scale, 0.044 * scale, 0.060 * scale), (xa, fa, d), region, w, segs=16, rings=10)
    # the thumb along the front, over the fingers
    tc = c + fa * 0.030 * scale + inward * 0.020 * scale - d * 0.008 * scale
    part.ellipsoid(tc, (0.019 * scale, 0.019 * scale, 0.031 * scale), (xa, fa, (d + fa * 0.3).normalized()), region, w,
                   segs=10, rings=6)
    if cuff:
        cuff_p = Part("cuff").segments(16)
        rings, _ = arm_rings(side, 0.46, 0.52, extra=0.022, steps=2)
        cuff_p.tube(rings, cuff, lambda cc: {f"Hand.{n}": 0.6, f"Forearm.{n}": 0.4}, caps=(False, False))
        part.merge(cuff_p)
    return part


def leg_rings(side, s0, s1, extra=0.0, steps=24, flare_end=0.0):
    rings = []
    for i in range(steps + 1):
        s = tr.lerp(s0, s1, i / steps)
        c, d = leg_point(side, s)
        xa, fa = frame(d)
        r = profile(LEG_R, s) + extra + flare_end * tr.smoothstep(0.6, 1.0, i / steps)
        # the calf bulges at the back
        calf = math.exp(-((s - 0.52) / 0.07) ** 2) * 0.008
        rings.append((c + fa * (-calf), xa, fa, r, r * 0.95 + calf, 2.0))
    return rings


def leg(side):
    part = Part(f"Leg.{side_name(side)}").segments(16)
    rings = leg_rings(side, -0.02, 0.80, steps=28)
    part.tube(rings, "skin", lambda c: leg_weights(side, s_on_leg(side, c)))
    return part


def s_on_leg(side, c):
    h, k = mirror(HIP, side), mirror(KNEE, side)
    if c.z >= k.z:
        return (c - h).length
    return THIGH_LEN + (c - k).length


def socks(side, top):
    """top: s along the leg where the sock starts (long football socks 0.43, short ones 0.70)."""
    part = Part(f"Sock.{side_name(side)}").segments(16)
    rings = leg_rings(side, top, 0.80, extra=0.008, steps=12)
    wf = lambda c: leg_weights(side, s_on_leg(side, c))
    part.tube(rings, "socks", wf, caps=(False, True))
    band = Part("band").segments(16)
    band.tube(leg_rings(side, top - 0.004, top + 0.032, extra=0.0115, steps=2), "socks_accent", wf, caps=(False, False))
    return part.merge(band)


def shorts(hem_z, loose=0.010):
    part = Part("Shorts").segments(28)
    # the pelvis: from under the shirt down to the crotch, closed underneath
    # tucked under the shirt's hem above 0.93 m (so its top never pokes through the hem)
    rings = torso_rings(0.785, 1.005, lambda z: tr.lerp(0.010, 0.002, tr.smoothstep(0.90, 0.95, z)), steps=12)
    part.tube(rings, "shorts", lambda c: {"Hips": 1.0}, caps=(True, False))
    # the legs: from inside the pelvis to the hem, a little flared, with an inner lip at the hem
    for side in (1, -1):
        n = side_name(side)
        top = mirror(Vector((0.088, 0.0, 0.90)), side)
        hem = mirror(Vector((0.104, -0.006, hem_z)), side)
        d = (hem - top).normalized()
        xa, fa = frame(d)
        legp = Part("leg").segments(18)
        steps = 6
        rr = []
        for i in range(steps + 1):
            k = i / steps
            c = top.lerp(hem, k)
            s = s_on_leg(side, c)
            r = profile(LEG_R, s) + loose + 0.004 + 0.006 * k
            rr.append((c, xa, fa, r * 1.04, r * 0.98, 2.1))
        c, xa_, fa_, rx, ry, pw = rr[-1]
        rr.append((c + d * 0.004, xa_, fa_, rx - 0.004, ry - 0.004, pw))
        rr.append((c - d * 0.016, xa_, fa_, rx - 0.011, ry - 0.011, pw))
        legp.tube(rr, "shorts", lambda cc, n=n: blend({"Hips": 0.5 * tr.smoothstep(0.80, 0.90, cc.z), f"Thigh.{n}": 1.0}),
                  caps=(False, False))
        part.merge(legp)
    return part


def shoe(side, region="shoes", sole="soles", bare=False):
    """A chunky sneaker (or a bare foot): an upper lofted heel to toe on a thick rounded sole."""
    n = side_name(side)
    part = Part(f"Shoe.{n}" if not bare else f"Foot.{n}").segments(18)
    x0 = 0.10 * side
    heel_y, toe_y = 0.085, -0.195 if not bare else -0.17
    w = {f"Foot.{n}": 1.0}
    wf = lambda c: blend({f"Foot.{n}": 1.0, f"Shin.{n}": 0.3 * tr.smoothstep(0.09, 0.12, c.z)})
    steps = 14
    rings = []
    base = 0.028 if not bare else 0.0
    for i in range(steps + 1):
        u = i / steps
        y = tr.lerp(heel_y, toe_y, u)
        half_w = profile([(0, 0.044), (0.3, 0.050), (0.65, 0.056), (0.9, 0.052), (1.0, 0.044)], u) * (0.86 if bare else 1.0)
        height = profile([(0, 0.118), (0.25, 0.11), (0.5, 0.082), (0.78, 0.06), (1.0, 0.046)], u) * (0.66 if bare else 1.0)
        c = Vector((x0 + side * 0.006 * u, y, base + height * 0.5))
        rings.append((c, Vector((1, 0, 0)), Vector((0, 0, 1)), half_w, height * 0.5, 2.8))
    # a loft along -Y: the "front" axis of these sections is up (Z)
    part.tube(rings, "skin" if bare else region, wf)
    if not bare:
        # the sole: a thick rounded slab under the upper, a little bigger
        sole_p = Part("sole").segments(24)
        outline = []
        for i in range(24):
            a = 2 * math.pi * i / 24
            ca, sa = math.cos(a), math.sin(a)
            # a stretched superellipse: long along Y
            u = 0.5 - 0.5 * math.copysign(abs(sa) ** 0.8, sa)
            half_w = profile([(0, 0.050), (0.3, 0.055), (0.65, 0.062), (0.9, 0.058), (1.0, 0.05)], u)
            length = (heel_y - toe_y) / 2 + 0.008
            cy = (heel_y + toe_y) / 2
            outline.append(Vector((x0 + side * 0.003 + math.copysign(abs(ca) ** 0.7, ca) * half_w,
                                   cy + math.copysign(abs(sa) ** 0.7, sa) * length, 0.0)))
        centre = Vector((x0, (heel_y + toe_y) / 2, 0.015))
        rows = []
        for z, inset in ((0.0, 0.006), (0.006, 0.0), (0.024, 0.0), (0.031, 0.006)):
            pts = [centre.lerp(Vector((p.x, p.y, z)), 1.0) + (centre - Vector((p.x, p.y, centre.z))).normalized() * inset
                   for p in outline]
            pts = [Vector((p.x, p.y, z)) for p in pts]
            rows.append(sole_p.ring(pts, wf, sole))
        for a, b in zip(rows, rows[1:]):
            sole_p.bridge(a, b, centre, centre)
        bottom = sole_p.v(Vector((x0, centre.y, 0.0)), wf, sole)
        top = sole_p.v(Vector((x0, centre.y, 0.031)), wf, sole)
        sole_p.fan(rows[0], bottom, centre + Vector((0, 0, 0.05)))
        sole_p.fan(rows[-1], top, centre - Vector((0, 0, 0.05)))
        part.merge(sole_p)
    return part


def neck():
    part = Part("Neck").segments(16)
    rings = []
    for i in range(7):
        k = i / 6
        c = Vector((0, 0.012 * (1 - k), tr.lerp(1.38, 1.56, k)))
        rings.append((c, Vector((1, 0, 0)), Vector((0, -1, 0)), 0.058, 0.055, 2.0))
    part.tube(rings, "skin", lambda c: blend({"Chest": 0.5 * (1 - tr.smoothstep(1.39, 1.45, c.z)), "Neck": 1.0,
                                             "Head": 0.6 * tr.smoothstep(1.48, 1.55, c.z)}), caps=(True, True))
    return part


def head():
    part = Part("Head")
    w = {"Head": 1.0}
    segs, rows_n = 36, 26
    rows = []
    bottom = part.v(head_point(Vector((0, 0, -1))), w, "skin")
    for j in range(1, rows_n):
        el = -math.pi / 2 + math.pi * j / rows_n
        rows.append(part.ring([head_point(head_dir(2 * math.pi * i / segs, el)) for i in range(segs)], w, "skin"))
    top = part.v(head_point(Vector((0, 0, 1))), w, "skin")
    part.fan(rows[0], bottom, HEAD_C)
    for a, b in zip(rows, rows[1:]):
        part.bridge(a, b, HEAD_C, HEAD_C)
    part.fan(rows[-1], top, HEAD_C)
    # ears and a little nose
    for side in (1, -1):
        c = HEAD_C + Vector((0.168 * side, 0.006, -0.012))
        part.ellipsoid(c, (0.026, 0.021, 0.041), (Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))), "skin", w,
                       segs=12, rings=8)
    p, n = face_point(0.0, 1.627)
    part.ellipsoid(p + n * 0.006, (0.023, 0.019, 0.018), (Vector((1, 0, 0)), n, Vector((0, 0, 1))), "skin", w,
                   segs=12, rings=8)
    return part


def on_face(part, x, z, radii, region, lift=0.0, tilt=0.0, segs=14, rings=8, bone="Head"):
    """A small flattened shape lying on the face at (x, z): radii (across, out of the face, up)."""
    p, n = face_point(x, z)
    up = Vector((0, 0, 1)) - n * n.z
    up.normalize()
    across = up.cross(n).normalized()
    if tilt:
        rot = Matrix.Rotation(math.radians(tilt), 3, n)
        up, across = rot @ up, rot @ across
    part.ellipsoid(p + n * lift, radii, (across, n, up), region, {bone: 1.0}, segs=segs, rings=rings)


def face_curve(part, pts_xz, radius, region, lift=0.002, bone="Head"):
    """A thin tube lying on the face through (x, z) points (brows, a smile, closed eyes)."""
    pts = []
    for x, z in pts_xz:
        p, n = face_point(x, z)
        pts.append(p + n * lift)
    rings = []
    for i, p in enumerate(pts):
        d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        _, n = face_point(*pts_xz[i])
        side = d.cross(n).normalized()
        rings.append((p, side, n, radius, radius * 0.6, 2.0))
    part.segments(8).tube(rings, region, lambda c: {bone: 1.0})


def face():
    """Eyes, brows, cheeks and three mouths. Expressions are bones: a clip shows a part by keeping its bone's scale
    at 1 and hides it by scaling it to nothing (inside the head): Eyes / EyesShut (happy closed eyes), Mouth (a
    smile) / MouthOpen (shouting, cheering, effort) / MouthSad."""
    part = Part("Face")
    for side in (1, -1):
        x = 0.061 * side
        on_face(part, x, 1.676, (0.0215, 0.011, 0.0325), "eyes", lift=-0.002, bone="Eyes")
        # the shine: up and toward the character's left on both eyes (one light)
        on_face(part, x + 0.0075, 1.688, (0.0072, 0.006, 0.0085), "eye_shine", lift=0.006, segs=10, rings=6, bone="Eyes")
        # closed, happy eyes
        sub = Part("closed")
        face_curve(sub, [(x - 0.021, 1.668), (x - 0.011, 1.679), (x, 1.682), (x + 0.011, 1.679), (x + 0.021, 1.668)],
                   0.0048, "eyes_closed", lift=0.004, bone="EyesShut")
        part.merge(sub)
        # brows
        sub = Part("brow")
        face_curve(sub, [(x - 0.024 * side, 1.733), (x, 1.740), (x + 0.024 * side, 1.735)], 0.0058, "hair", lift=0.004)
        part.merge(sub)
        # cheeks
        on_face(part, 0.103 * side, 1.623, (0.025, 0.004, 0.015), "blush", lift=0.0)
    sub = Part("smile")
    face_curve(sub, [(-0.031, 1.6045), (-0.018, 1.5955), (0.0, 1.5925), (0.018, 1.5955), (0.031, 1.6045)], 0.0055,
               "mouth", lift=0.003, bone="Mouth")
    part.merge(sub)
    sub = Part("sad")
    face_curve(sub, [(-0.026, 1.5885), (-0.013, 1.5955), (0.0, 1.5975), (0.013, 1.5955), (0.026, 1.5885)], 0.0052,
               "mouth", lift=0.003, bone="MouthSad")
    part.merge(sub)
    on_face(part, 0.0, 1.590, (0.030, 0.008, 0.021), "mouth_open", lift=-0.001, segs=16, rings=8, bone="MouthOpen")
    on_face(part, 0.0, 1.5785, (0.017, 0.006, 0.008), "tongue", lift=0.004, segs=10, rings=6, bone="MouthOpen")
    return part


def headband():
    part = Part("Headband").segments(8)
    rings = []
    n = 40
    pts = []
    for i in range(n):
        az = 2 * math.pi * i / n
        el = math.radians(tr.lerp(21.0, 31.0, (1 - math.cos(az)) / 2))   # lower in front, higher at the back
        pts.append((head_point(head_dir(az, el)) + head_normal(az, el) * 0.004, head_normal(az, el)))
    made = []
    for i, (p, nrm) in enumerate(pts):
        d = (pts[(i + 1) % n][0] - pts[i - 1][0]).normalized()
        side = d.cross(nrm).normalized()
        ring = superellipse(p + nrm * 0.004, side, nrm, 0.017, 0.007, 3.0, 8)
        made.append((part.ring(ring, {"Head": 1.0}, "headband"), p - nrm * 0.02))
    for i in range(n):
        a, ca = made[i]
        b, cb = made[(i + 1) % n]
        part.bridge(a, b, ca, cb)
    return part


# ---------------------------------------------------------------------------------------------------- hair

def hairline(az, front, temple, side, back):
    """Height of the hairline (metres above the head's centre) at azimuth az: smooth through the four values."""
    a = abs(math.atan2(math.sin(az), math.cos(az)))
    keys = [(0.0, front), (math.radians(55), temple), (math.radians(100), side), (math.pi, back)]
    for (a0, h0), (a1, h1) in zip(keys, keys[1:]):
        if a <= a1:
            return tr.lerp(h0, h1, tr.ease((a - a0) / (a1 - a0)))
    return back


def hair_cap(name, line, thick, segs=56, rows=14):
    """A shell over the head from the hairline to the crown, `thick(az, el)` metres thick, with a rounded edge."""
    part = Part(name)
    w = {"Head": 1.0}
    grid, inner = [], []
    for i in range(segs):
        az = 2 * math.pi * i / segs
        h = line(az)
        el0 = math.asin(max(-0.98, min(0.98, h / HEAD_R[2])))
        col = []
        for j in range(rows):
            k = (j / (rows - 1)) ** 0.85
            el = tr.lerp(el0, math.radians(89.0), k)
            n = head_normal(az, el)
            col.append(head_point(head_dir(az, el)) + n * thick(az, el, k))
        grid.append(col)
        n0 = head_normal(az, el0)
        inner.append(head_point(head_dir(az, el0)) + n0 * 0.0015 + Vector((0, 0, 0.004)))
    idx = [[part.v(p, w, "hair") for p in col] for col in grid]
    lip = [part.v(p, w, "hair") for p in inner]
    for i in range(segs):
        i2 = (i + 1) % segs
        for j in range(rows - 1):
            part.face([idx[i][j], idx[i2][j], idx[i2][j + 1], idx[i][j + 1]], HEAD_C)
        part.face([lip[i], lip[i2], idx[i2][0], idx[i][0]], HEAD_C + Vector((0, 0, 0.3)))
    crown = part.v(head_point(Vector((0, 0, 1))) + Vector((0, 0, thick(0.0, math.pi / 2, 1.0))), w, "hair")
    part.fan([idx[i][-1] for i in range(segs)], crown, HEAD_C)
    return part


def blob(part, centre, radii, axes=None, region="hair"):
    axes = axes or (Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)))
    part.ellipsoid(centre, radii, axes, region, {"Head": 1.0}, segs=16, rings=10)


def surface_at(az_deg, el_deg, out=0.0):
    az, el = math.radians(az_deg), math.radians(el_deg)
    return head_point(head_dir(az, el)) + head_normal(az, el) * out, head_normal(az, el)


def axes_along(d):
    d = d.normalized()
    x = Vector((0, 0, 1)).cross(d)
    if x.length < 1e-3:
        x = Vector((1, 0, 0))
    x.normalize()
    return (x, d.cross(x).normalized(), d)


def hair_styles():
    styles = {}
    crop_line = lambda az: hairline(az, 0.083, 0.068, 0.030, -0.100)
    top_thick = lambda az, el, k: 0.010 + 0.016 * math.sin(el) ** 2 + 0.006 * max(0.0, math.cos(az)) * k

    p = hair_cap("Hair_Crop", crop_line, top_thick)
    c, n = surface_at(0, 62, 0.012)
    blob(p, c + Vector((0.012, 0.0, 0.0)), (0.085, 0.045, 0.034), axes_along(Vector((0.25, -0.35, 1))))
    c, n = surface_at(-25, 50, 0.006)
    blob(p, c, (0.06, 0.03, 0.03), axes_along(Vector((-0.4, -0.2, 1))))
    styles["Crop"] = p

    styles["Buzz"] = hair_cap("Hair_Buzz", crop_line, lambda az, el, k: 0.0055)

    p = hair_cap("Hair_Spiky", crop_line, lambda az, el, k: 0.009 + 0.008 * math.sin(el))
    for az, el, ln in ((0, 55, 0.075), (35, 50, 0.07), (-35, 50, 0.07), (70, 40, 0.06), (-70, 40, 0.06),
                       (110, 40, 0.06), (-110, 40, 0.06), (150, 45, 0.06), (-150, 45, 0.06), (180, 55, 0.06),
                       (20, 75, 0.08), (-20, 75, 0.08), (90, 70, 0.07), (-90, 70, 0.07), (160, 75, 0.07)):
        c, n = surface_at(az, el, 0.004)
        d = (n + Vector((0, 0.35, 0.25))).normalized()
        blob(p, c + d * ln * 0.5, (0.026, 0.026, ln * 0.78), axes_along(d))
    styles["Spiky"] = p

    afro_line = lambda az: hairline(az, 0.074, 0.058, 0.020, -0.095)
    p = hair_cap("Hair_Afro", afro_line, lambda az, el, k: 0.02 + 0.03 * k)
    rng = [(az, el) for el in (15, 38, 60, 80) for az in range(0, 360, 360 // (8 if el < 50 else 6 if el < 75 else 3))]
    for az, el in rng:
        if el < 40 and (az < 50 or az > 310):
            continue
        c, n = surface_at(az, el, 0.035 + 0.01 * (el / 90))
        blob(p, c, (0.072, 0.072, 0.066), axes_along(n))
    styles["Afro"] = p

    sleek = lambda az, el, k: 0.007 + 0.006 * math.sin(el) ** 2
    pony_line = lambda az: hairline(az, 0.092, 0.075, 0.032, -0.095)
    p = hair_cap("Hair_Ponytail", pony_line, sleek)
    c, n = surface_at(180, 22, 0.0)
    tie = c + Vector((0, 0.015, 0.0))
    blob(p, tie, (0.03, 0.022, 0.03), axes_along(Vector((0, 1, 0))), region="headband")
    pts = [tie + Vector((0, 0.03, 0.01)), tie + Vector((0, 0.07, -0.02)), tie + Vector((0, 0.09, -0.08)),
           tie + Vector((0, 0.085, -0.14)), tie + Vector((0, 0.07, -0.19))]
    for i, q in enumerate(pts):
        r = tr.lerp(0.056, 0.028, i / (len(pts) - 1))
        d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        blob(p, q, (r, r * 0.9, r * 1.6), axes_along(d))
    styles["Ponytail"] = p

    p = hair_cap("Hair_Bun", pony_line, sleek)
    c, n = surface_at(180, 58, 0.03)
    blob(p, c + Vector((0, 0.02, 0.02)), (0.068, 0.064, 0.062))
    blob(p, c - n * 0.028, (0.045, 0.04, 0.02), axes_along(n), region="headband")
    styles["Bun"] = p

    bob_line = lambda az: hairline(az, 0.074, 0.020, -0.115, -0.125)
    bob_thick = lambda az, el, k: 0.014 + 0.012 * math.sin(el) ** 2 + 0.016 * (1 - k) ** 2 * (1 - max(0.0, math.cos(az)) ** 4)
    styles["Bob"] = hair_cap("Hair_Bob", bob_line, bob_thick)

    p = hair_cap("Hair_Pigtails", pony_line, sleek)
    for side in (1, -1):
        c, n = surface_at(115 * side, 18, 0.03)
        blob(p, c + n * 0.035 + Vector((0, 0.01, -0.01)), (0.052, 0.05, 0.06))
        blob(p, c + n * 0.004, (0.026, 0.022, 0.026), axes_along(n), region="headband")
    styles["Pigtails"] = p
    return styles


# ---------------------------------------------------------------------------------------------------- the rig

def bones():
    b = {
        "Root": ((0, 0, 0), (0, 0.18, 0), None),
        "Hips": ((0, 0.0, 0.875), (0, 0.0, 1.0), "Root"),
        "Spine": ((0, 0.0, 1.0), (0, -0.004, 1.17), "Hips"),
        "Chest": ((0, -0.004, 1.17), (0, 0.004, 1.38), "Spine"),
        "Neck": ((0, 0.008, 1.405), (0, 0.0, 1.53), "Chest"),
        "Head": ((0, 0.0, 1.53), (0, 0.0, 1.86), "Neck"),
        # expressions (see face())
        "Eyes": ((0, -0.115, 1.678), (0, -0.165, 1.678), "Head"),
        "EyesShut": ((0, -0.115, 1.674), (0, -0.165, 1.674), "Head"),
        "Mouth": ((0, -0.115, 1.597), (0, -0.165, 1.597), "Head"),
        "MouthOpen": ((0, -0.115, 1.590), (0, -0.165, 1.590), "Head"),
        "MouthSad": ((0, -0.115, 1.594), (0, -0.165, 1.594), "Head"),
    }
    for side in (1, -1):
        n = side_name(side)
        sh = mirror(SHOULDER, side)
        b[f"Shoulder.{n}"] = (tuple(mirror(Vector((0.035, 0.0, 1.36)), side)), tuple(sh), "Chest")
        b[f"UpperArm.{n}"] = (tuple(sh), tuple(mirror(ELBOW, side)), f"Shoulder.{n}")
        b[f"Forearm.{n}"] = (tuple(mirror(ELBOW, side)), tuple(mirror(WRIST, side)), f"UpperArm.{n}")
        b[f"Hand.{n}"] = (tuple(mirror(WRIST, side)), tuple(mirror(WRIST + ARM_DIR * HAND, side)), f"Forearm.{n}")
        b[f"Thigh.{n}"] = (tuple(mirror(HIP, side)), tuple(mirror(KNEE, side)), "Hips")
        b[f"Shin.{n}"] = (tuple(mirror(KNEE, side)), tuple(mirror(ANKLE, side)), f"Thigh.{n}")
        b[f"Foot.{n}"] = (tuple(mirror(ANKLE, side)), tuple(mirror(TOE, side)), f"Shin.{n}")
    return b


def make_armature(coll):
    data = bpy.data.armatures.new("AthleteRig")
    arm = bpy.data.objects.new("AthleteRig", data)
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


# ---------------------------------------------------------------------------------------------------- build

PIECES = {}   # object name -> what it is (written to athlete.json for the game)


def preview_material():
    mat = bpy.data.materials.get("AthletePreview")
    if mat:
        return mat
    mat = bpy.data.materials.new("AthletePreview")
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_name = "Col"
    nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.65
    return mat


def build():
    ta.clear_scene()
    for coll in list(bpy.data.collections):
        if coll.name in ("Athlete", "Preview"):
            for o in list(coll.objects):
                bpy.data.objects.remove(o, do_unlink=True)
    coll = ta.collection("Athlete")
    arm = make_armature(coll)
    objs = []

    def add(part, kind):
        o = part.to_object(coll, arm)
        PIECES[o.name] = kind
        objs.append(o)
        return o

    body = head().merge(neck()).merge(arm_piece(1)).merge(arm_piece(-1)).merge(leg(1)).merge(leg(-1))
    body.name = "Body"
    add(body, "body")
    add(face(), "face")
    add(shirt(), "shirt")
    p = sleeve(1).merge(sleeve(-1))
    p.name = "Sleeves"
    add(p, "sleeves")
    add(shorts(0.640), "shorts")
    p = shorts(0.715, loose=0.007)
    p.name = "RunShorts"
    add(p, "shorts")
    for name, top in (("SocksLong", 0.43), ("SocksShort", 0.70)):
        p = socks(1, top).merge(socks(-1, top))
        p.name = name
        add(p, "socks")
    p = shoe(1).merge(shoe(-1))
    p.name = "Shoes"
    add(p, "feet")
    p = shoe(1, bare=True).merge(shoe(-1, bare=True))
    p.name = "BareFeet"
    add(p, "feet")
    p = fist(1).merge(fist(-1))
    p.name = "Hands"
    add(p, "hands")
    p = fist(1, 1.3, "gloves", cuff="gloves_accent").merge(fist(-1, 1.3, "gloves", cuff="gloves_accent"))
    p.name = "Gloves"
    add(p, "hands")
    add(headband(), "headband")
    for style, part in hair_styles().items():
        part.name = f"Hair_{style}"
        add(part, "hair")
    return {"pieces": len(objs), "verts": sum(len(o.data.vertices) for o in objs),
            "tris": {o.name: sum(len(pl.vertices) - 2 for pl in o.data.polygons) for o in objs}}


def pieces():
    return [o for o in bpy.data.collections["Athlete"].objects if o.type == "MESH"]


UNITY_SCRIPTS = os.path.join(ta.REPO, "unity", "MoveItGames", "Assets", "Games", "Sports", "Scripts", "Athletes")


def write_manifest():
    """athlete.json next to the FBX, and AthleteData.cs (regions, pieces, clips with their lengths, loops, contact
    moments and ground speeds) for the game."""
    import athlete_anim
    os.makedirs(UNITY_ART, exist_ok=True)
    clips = athlete_anim.CLIPS
    info = {"regions": REGIONS, "pieces": {o.name: PIECES.get(o.name, "?") for o in pieces()}, "clips": clips}
    with open(os.path.join(UNITY_ART, "athlete.json"), "w", encoding="utf-8") as f:
        json.dump(info, f, indent=1)
    os.makedirs(UNITY_SCRIPTS, exist_ok=True)

    def f(x):
        return f"{x:.4f}f"
    lines = [
        "// Generated by art/sports/build.py (athlete.write_manifest): do not edit, rebuild the art instead.",
        "using UnityEngine;",
        "",
        "namespace MoveIt.Games.Sports",
        "{",
        "    /// <summary>What art/sports/athlete.py made: the colour regions, the pieces and the clips.</summary>",
        "    public static class AthleteData",
        "    {",
        "        public static readonly string[] Regions =",
        "        {",
        "            " + ", ".join(f'"{r}"' for r in REGIONS),
        "        };",
        "",
        "        public static readonly string[] Pieces =",
        "        {",
        "            " + ", ".join(f'"{o.name}"' for o in pieces()),
        "        };",
        "",
        "        /// <summary>A clip: its length (s), whether it loops, when the ball is met (s, -1 = never) and how fast",
        "        /// its feet carry the body at speed 1 (m/s, 0 = standing).</summary>",
        "        public readonly struct Clip",
        "        {",
        "            public readonly string Name;",
        "            public readonly float Seconds, Contact, Speed;",
        "            public readonly bool Loop;",
        "",
        "            public Clip(string name, float seconds, bool loop, float contact, float speed)",
        "            {",
        "                Name = name;",
        "                Seconds = seconds;",
        "                Loop = loop;",
        "                Contact = contact;",
        "                Speed = speed;",
        "            }",
        "        }",
        "",
        "        public static readonly Clip[] Clips =",
        "        {",
    ]
    for name, c in clips.items():
        contact = c["contact"] if c["contact"] is not None else -1.0
        speed = c["speed"] if c["speed"] is not None else 0.0
        lines.append(f'            new Clip("{name}", {f(c["seconds"])}, {"true" if c["loop"] else "false"}, {f(contact)}, {f(speed)}),')
    kb = athlete_anim.KICK_BALL
    lines += [
        "        };",
        "",
        "        /// <summary>Where the ball lies (metres, the athlete's own space: x right, y up, z forward) for KickR to",
        "        /// meet it at the contact moment (KickL: x mirrored).</summary>",
        f"        public static readonly Vector3 KickBall = new Vector3({f(-kb.x)}, {f(kb.z)}, {f(-kb.y)});",
        "",
        "        public static Clip Find(string name)",
        "        {",
        "            foreach (var c in Clips)",
        "                if (c.Name == name) return c;",
        "            return default;",
        "        }",
        "    }",
        "}",
        "",
    ]
    with open(os.path.join(UNITY_SCRIPTS, "AthleteData.cs"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(lines))


# ---------------------------------------------------------------------------------------------------- ambient occlusion

def bake_ao(samples=96):
    """Bakes ambient occlusion into each piece's second UV map (v), each piece next to the pieces always worn with
    it."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.render.bake.target = "VERTEX_COLORS"
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.light_settings.distance = 0.35
    baked = {}

    def bake(target, visible):
        for o in pieces():
            show = o.name in visible
            o.hide_render = not show
            o.hide_set(not show)
        obj = bpy.data.objects[target]
        me = obj.data
        attr = me.color_attributes.get("AO") or me.color_attributes.new("AO", "FLOAT_COLOR", "POINT")
        me.color_attributes.active_color = attr
        for o in bpy.context.view_layer.objects:
            o.select_set(False)
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.bake(type="AO")
        uv = me.uv_layers["Region"]
        for poly in me.polygons:
            for li in poly.loop_indices:
                vi = me.loops[li].vertex_index
                c = attr.data[vi].color
                ao = (c[0] + c[1] + c[2]) / 3
                uv.data[li].uv = (uv.data[li].uv[0], 0.35 + 0.65 * ao)
        baked[target] = True

    # each piece is baked with the pieces that are always next to it (never with optional ones that would leave
    # their shadow behind when they are not worn: sleeves, socks, shoes)
    always = ["Body", "Face", "Shirt", "Shorts", "Hands"]
    plan = {
        "Body": always, "Face": ["Body", "Face"], "Shirt": always, "Sleeves": ["Body", "Shirt", "Sleeves"],
        "Shorts": ["Body", "Shirt", "Shorts"], "RunShorts": ["Body", "Shirt", "RunShorts"],
        "SocksLong": ["Body", "SocksLong", "Shoes"], "SocksShort": ["Body", "SocksShort", "Shoes"],
        "Shoes": ["Body", "Shoes", "SocksShort"], "BareFeet": ["Body", "BareFeet"],
        "Hands": ["Body", "Shirt", "Hands"], "Gloves": ["Body", "Shirt", "Gloves"], "Headband": ["Body", "Face", "Headband"],
    }
    for name in [o.name for o in pieces() if o.name.startswith("Hair_")]:
        plan[name] = ["Body", "Face", "Shirt", name]
    for name, visible in plan.items():
        bake(name, visible)
    for o in pieces():
        o.hide_render = False
        o.hide_set(False)
    return sorted(baked)


# ---------------------------------------------------------------------------------------------------- previews

LOOKS = [
    # (pieces, colour overrides)
    (["Body", "Face", "Shirt", "Sleeves", "Shorts", "SocksLong", "Shoes", "Hands", "Hair_Crop"], {}),
    (["Body", "Face", "Shirt", "RunShorts", "SocksShort", "Shoes", "Hands", "Hair_Ponytail", "Headband"],
     {"shirt": "#FF5A4E", "shirt_accent": "#FFC93C", "shorts": "#141938", "skin": "#C98E66", "hair": "#2A1A12",
      "shoes": "#FFC93C"}),
    (["Body", "Face", "Shirt", "Shorts", "BareFeet", "Hands", "Hair_Afro"],
     {"shirt": "#FFC93C", "shorts": "#31C6F4", "skin": "#8D5B3E", "hair": "#1E1410"}),
    (["Body", "Face", "Shirt", "Sleeves", "Shorts", "SocksLong", "Shoes", "Gloves", "Hair_Spiky"],
     {"shirt": "#7B61FF", "shirt_accent": "#FFC93C", "shorts": "#141938", "hair": "#E0B060", "skin": "#F7D5B8"}),
    (["Body", "Face", "Shirt", "Shorts", "BareFeet", "Hands", "Hair_Bob"],
     {"shirt": "#FF8FB0", "shorts": "#FFFFFF", "hair": "#6B3D22"}),
    (["Body", "Face", "Shirt", "RunShorts", "SocksShort", "Shoes", "Hands", "Hair_Bun"],
     {"shirt": "#6FD66A", "shorts": "#141938", "hair": "#B8732E", "skin": "#E8B48F"}),
    (["Body", "Face", "Shirt", "Sleeves", "Shorts", "SocksLong", "Shoes", "Hands", "Hair_Pigtails"],
     {"shirt": "#FF5A4E", "hair": "#2A1A12", "skin": "#C98E66"}),
    (["Body", "Face", "Shirt", "RunShorts", "SocksShort", "Shoes", "Hands", "Hair_Buzz"],
     {"shirt": "#141938", "shirt_accent": "#31C6F4", "skin": "#6E4530", "hair": "#120C0A"}),
]


def recolour(obj, overrides):
    me = obj.data
    col = me.color_attributes["Col"]
    reg = me.uv_layers["Region"]
    per_vertex = {}
    for poly in me.polygons:
        for li in poly.loop_indices:
            per_vertex[me.loops[li].vertex_index] = reg.data[li].uv
    for vi, (u, v) in per_vertex.items():
        name = REGIONS[int(round(u))]
        hexc = overrides.get(name, PREVIEW[name])
        c = [ta.srgb_to_linear(x) for x in ta.hex_rgb(hexc)]
        ao = v
        col.data[vi].color = (*(x * ao for x in c), 1.0)


def preview(name="lineup.png", pose_fn=None):
    """Renders the looks side by side (copies of the pieces, moved apart), from the front and the back."""
    out_dir = os.path.join(HERE, "previews")
    os.makedirs(out_dir, exist_ok=True)
    coll = ta.collection("Preview")
    for o in list(coll.objects):
        if o.type == "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    arm = bpy.data.objects["AthleteRig"]
    # the standing pose (it also sets the expression: a smile, open eyes)
    import athlete_anim
    if "Stand" not in bpy.data.actions:
        athlete_anim.animate(only=["Stand"])
    arm.animation_data.action = bpy.data.actions["Stand"]
    bpy.context.scene.frame_set(1)
    for i, (names, colours) in enumerate(LOOKS):
        for n in names:
            src = bpy.data.objects[n]
            recolour(src, colours)
            src.data.update()
            dg = bpy.context.evaluated_depsgraph_get()
            dg.update()
            me = bpy.data.meshes.new_from_object(src.evaluated_get(dg))
            o = bpy.data.objects.new(f"pv{i}_{n}", me)
            o.matrix_world = Matrix.Translation(((i - (len(LOOKS) - 1) / 2) * 0.75, 0, 0)) @ src.matrix_world
            coll.objects.link(o)
    for o in pieces():
        o.hide_render = True
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    ta.preview_rig(target=(0, 0, 0.95), distance=7.4, yaw_deg=0, pitch_deg=6, lens=42)
    scene.render.film_transparent = False
    paths = []
    for view, yaw in (("front", 0), ("back", 180), ("three", -30)):
        ta.preview_rig(target=(0, 0, 0.95), distance=7.4 if view != "three" else 6.4, yaw_deg=yaw, pitch_deg=6, lens=42)
        paths.append(ta.render_preview(os.path.join(out_dir, f"{os.path.splitext(name)[0]}_{view}.png"), res=(1800, 700)))
    for o in pieces():
        o.hide_render = False
    return paths


def _hide_material(mat):
    """The preview material leaves out hidden regions (colour alpha 0)."""
    nt = mat.node_tree
    if nt.nodes.get("HideMix"):
        return
    out = nt.nodes.get("Material Output")
    bsdf = nt.nodes.get("Principled BSDF")
    attr = next(n for n in nt.nodes if n.type == "ATTRIBUTE")
    transp = nt.nodes.new("ShaderNodeBsdfTransparent")
    mix = nt.nodes.new("ShaderNodeMixShader")
    mix.name = "HideMix"
    nt.links.new(attr.outputs["Alpha"], mix.inputs["Fac"])
    nt.links.new(transp.outputs["BSDF"], mix.inputs[1])
    nt.links.new(bsdf.outputs["BSDF"], mix.inputs[2])
    nt.links.new(mix.outputs["Shader"], out.inputs["Surface"])
    try:
        mat.surface_render_method = "DITHERED"
    except (AttributeError, TypeError):
        pass
