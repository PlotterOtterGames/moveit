"""Tundu's chibi characters: the shared kit (run inside Blender 5.1; `build.py` runs it all headless).

One cute toy body (a big round head with bean eyes, a small body, mitten hands, big sneakers), about 2.3 heads tall,
and a wardrobe of pieces that fit it (hair, hats, animal ears and horns, tops, bottoms, shoes, glasses and headphones,
wings and tails). Every game uses the same characters: the game picks one piece per slot and merges them into one
skinned mesh.

Colours are not in the model: every face says which REGION it is (skin, hair, top, top_accent, ...) in the second UV
map (u = region, v = baked ambient occlusion), and the game colours the regions per character (Tundu/Athlete shader,
24 regions), so one hoodie is blue on one character and pink on the next. Regions are per face: borders are crisp.

Faces are pieces too (eyes, happy closed eyes, a smile, an open grin, a sad mouth), each on its own bone: the game shows
one eye piece and one mouth piece by scaling the others' bones to 0 (and blinks by squashing the eyes' bone).

Facing -Y (Unity +Z), feet on Z = 0, the character's left = +X, metres.
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "tools"))
import tundu_art as ta  # noqa: E402
import tundu_rig as tr  # noqa: E402

UNITY_ART = os.path.join(ta.REPO, "unity", "MoveItGames", "Assets", "Games", "Common", "Characters", "Resources", "Chibi")
UNITY_SCRIPTS = os.path.join(ta.REPO, "unity", "MoveItGames", "Assets", "Games", "Common", "Characters")

# Region numbers: the shader's colour table (Tundu/Athlete _Regions[], 24); keep in step with ChibiLook.cs (generated).
REGIONS = ["skin", "blush", "eyes", "eye_shine", "mouth", "tongue", "hair", "hair_accent", "top", "top_accent",
           "top_trim", "bottom", "bottom_accent", "shoes", "shoes_accent", "soles", "hat", "hat_accent", "ears",
           "ears_inner", "acc", "acc_accent", "back", "back_accent"]
R = {name: i for i, name in enumerate(REGIONS)}

# Mesh detail: 1 = the full chibi (Chibi.fbx). build.py --low sets LOW_DETAIL and exports Chibi_Low.fbx, the same pieces
# with fewer segments around every tube, ring and ellipsoid, a coarser head and shells, fewer rings along hair strands
# and rounder caps in two steps instead of three: about a third of the triangles, for chibis small on the screen (the
# far team, two players' split screen on the TV box).
DETAIL = 1.0
LOW_DETAIL = 0.6


def lod(n, lo=3):
    """A segment count at the current DETAIL (never below lo)."""
    return n if DETAIL >= 0.999 else max(lo, int(round(n * DETAIL)))

# preview colours (the game sets its own per character)
PREVIEW = {
    "skin": "#FFD9C2", "blush": "#FFAFA0", "eyes": "#1B1726", "eye_shine": "#FFFFFF", "mouth": "#4A1E2A",
    "tongue": "#F2707A", "hair": "#6B3D22", "hair_accent": "#8A5530", "top": "#3B7BE8", "top_accent": "#FFC93C",
    "top_trim": "#2A5BC0", "bottom": "#24305E", "bottom_accent": "#FFFFFF", "shoes": "#3B7BE8",
    "shoes_accent": "#FFFFFF", "soles": "#F4F4F8", "hat": "#E8403A", "hat_accent": "#FFFFFF", "ears": "#FFFFFF",
    "ears_inner": "#FFB3C7", "acc": "#1B1726", "acc_accent": "#3B7BE8", "back": "#FFFFFF", "back_accent": "#CFE6FF",
}

# ---------------------------------------------------------------------------------------------------- proportions

HEAD_C = Vector((0.0, 0.0, 1.175))
HEAD_R = (0.372, 0.338, 0.345)        # half width, half depth, half height (above the centre)
HEAD_BOTTOM = 0.90                    # below the centre the head is this much shorter (a softer chin)
HEAD_SQUARE = 0.82                    # superellipsoid exponent: < 1 = a little squarer (a mochi, not a ball)
NECK = (Vector((0, 0.005, 0.80)), Vector((0, 0.005, 0.95)), 0.075)

SHOULDER = Vector((0.168, 0.0, 0.795))
ARM_DIR = Vector((math.cos(math.radians(64)), 0.0, -math.sin(math.radians(64))))   # arms hang a little out
UPPER_ARM, FOREARM = 0.145, 0.135
ELBOW = SHOULDER + ARM_DIR * UPPER_ARM
WRIST = ELBOW + ARM_DIR * FOREARM
HAND_C = WRIST + ARM_DIR * 0.058
ARM_R = 0.060

HIP = Vector((0.090, 0.0, 0.470))
KNEE = Vector((0.090, -0.004, 0.300))
ANKLE = Vector((0.090, 0.004, 0.125))
TOE = Vector((0.090, -0.200, 0.035))
LEG_R = 0.072

# the torso's sections: z, half width, half depth, centre y (forward is -), superellipse power
TORSO = [
    (0.445, 0.120, 0.098, 0.004, 2.2),
    (0.465, 0.165, 0.122, 0.004, 2.5),
    (0.50, 0.188, 0.136, 0.003, 2.7),
    (0.58, 0.196, 0.140, 0.002, 2.8),
    (0.67, 0.192, 0.138, 0.000, 2.8),
    (0.75, 0.186, 0.133, 0.000, 2.8),
    (0.81, 0.176, 0.126, 0.002, 2.7),
    (0.85, 0.156, 0.114, 0.004, 2.5),
    (0.875, 0.122, 0.096, 0.006, 2.3),
    (0.89, 0.080, 0.075, 0.006, 2.0),
]


def lerp(a, b, t):
    return a + (b - a) * t


def ease(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def smooth(e0, e1, x):
    return ease((x - e0) / (e1 - e0))


def mirror(v, side):
    return Vector((v.x * side, v.y, v.z))


def side_name(side):
    return "L" if side > 0 else "R"


def torso_section(z):
    """(centre y, half width, half depth, power) at height z, smoothly interpolated."""
    t = TORSO
    if z <= t[0][0]:
        return t[0][3], t[0][1], t[0][2], t[0][4]
    if z >= t[-1][0]:
        return t[-1][3], t[-1][1], t[-1][2], t[-1][4]
    for i in range(len(t) - 1):
        if t[i][0] <= z <= t[i + 1][0]:
            k = ease((z - t[i][0]) / (t[i + 1][0] - t[i][0]))
            a, b = t[i], t[i + 1]
            return lerp(a[3], b[3], k), lerp(a[1], b[1], k), lerp(a[2], b[2], k), lerp(a[4], b[4], k)


def spow(x, e):
    return math.copysign(abs(x) ** e, x)


def head_point(az, el, out=0.0):
    """The head's surface at azimuth az (0 = the face, + toward the character's left) and elevation el (radians), pushed
    `out` metres outward (a shell over it: hair, a hat)."""
    ce, se = math.cos(el), math.sin(el)
    ca, sa = math.cos(az), math.sin(az)
    e = HEAD_SQUARE
    x = spow(ce, e) * spow(sa, e)
    y = -spow(ce, e) * spow(ca, e)
    z = spow(se, e)
    jaw = 1.0 - 0.10 * max(0.0, -se) ** 2
    rz = HEAD_R[2] * (HEAD_BOTTOM if z < 0 else 1.0)
    return HEAD_C + Vector((x * (HEAD_R[0] * jaw + out), y * (HEAD_R[1] * jaw + out), z * (rz + out)))


def head_normal(az, el):
    d = 1e-3
    p = head_point(az, el)
    a = head_point(az + d, el) - p
    b = head_point(az, el + d) - p
    n = b.cross(a)
    if n.length < 1e-9:
        return (p - HEAD_C).normalized()
    n.normalize()
    return n if n.dot(p - HEAD_C) > 0 else -n


# ---------------------------------------------------------------------------------------------------- weights

def segment_t(p, a, b):
    ab = b - a
    return max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))


def W(**w):
    return lambda p: w


HEAD_W = W(Head=1.0)


def torso_weights(p):
    k = smooth(0.52, 0.66, p.z)
    return {"Hips": 1 - k, "Spine": k}


def arm_weights(side):
    n = side_name(side)
    sh, wr = mirror(SHOULDER, side), mirror(WRIST, side)

    def f(p):
        t = (p - sh).dot(mirror(ARM_DIR, side))
        L = UPPER_ARM + FOREARM
        if t < 0.05:
            k = smooth(-0.04, 0.05, t)
            return {"Spine": 1 - k, f"UpperArm.{n}": k}
        if t > L - 0.01:
            k = smooth(L - 0.01, L + 0.03, t)
            return {f"Forearm.{n}": 1 - k, f"Hand.{n}": k}
        k = smooth(UPPER_ARM - 0.04, UPPER_ARM + 0.04, t)
        return {f"UpperArm.{n}": 1 - k, f"Forearm.{n}": k}
    return f


def hand_weights(side):
    n = side_name(side)
    return W(**{f"Hand.{n}": 1.0})


def leg_weights(side):
    n = side_name(side)
    hip = mirror(HIP, side)

    def f(p):
        z = p.z
        if z > hip.z - 0.03:
            k = smooth(hip.z + 0.02, hip.z - 0.05, z)
            return {"Hips": 1 - k, f"Thigh.{n}": k}
        if z < ANKLE.z + 0.02:
            k = smooth(ANKLE.z + 0.03, ANKLE.z - 0.01, z)
            return {f"Shin.{n}": 1 - k, f"Foot.{n}": k}
        k = smooth(KNEE.z + 0.03, KNEE.z - 0.03, z)
        return {f"Thigh.{n}": 1 - k, f"Shin.{n}": k}
    return f


def foot_weights(side):
    n = side_name(side)

    def f(p):
        k = smooth(ANKLE.z + 0.05, ANKLE.z, p.z)
        return {f"Shin.{n}": 1 - k, f"Foot.{n}": k}
    return f


def lower_weights(p):
    """The hips and legs together (shorts, pants): by side and height."""
    side = 1 if p.x >= 0 else -1
    if p.z > HIP.z + 0.02 or abs(p.x) < 0.02:
        return {"Hips": 1.0}
    return leg_weights(side)(p)


def upper_weights(p):
    """The torso and arms together (tops): sleeves by how far out along the arm, the rest the torso."""
    side = 1 if p.x >= 0 else -1
    sh = mirror(SHOULDER, side)
    t = (p - sh).dot(mirror(ARM_DIR, side))
    radial = ((p - sh) - mirror(ARM_DIR, side) * t).length
    if t > 0.0 and radial < ARM_R + 0.06:
        return arm_weights(side)(p)
    return torso_weights(p)


# ---------------------------------------------------------------------------------------------------- mesh building

class Part:
    """Vertices with bone weights, faces with a region each (crisp colour borders)."""

    def __init__(self, name, weights=HEAD_W):
        self.name = name
        self.weights = weights
        self.co, self.w, self.faces, self.freg = [], [], [], []

    def v(self, co, weights=None):
        co = Vector(co)
        wf = weights or self.weights
        self.co.append(co)
        self.w.append(dict(wf(co) if callable(wf) else wf))
        return len(self.co) - 1

    def face(self, idx, region):
        self.faces.append(tuple(idx))
        self.freg.append(R[region] if isinstance(region, str) else region)

    def grid(self, rows, region, closed=True, caps=(False, False), weights=None, flip=False, loop=False):
        """Rings of points (all the same count) bridged into quads; caps close the ends with a centre point; loop joins
        the last ring back to the first (a torus). region: a name or fn(row, column, point) -> name."""
        idx = [[self.v(p, weights) for p in row] for row in rows]
        n = len(rows[0])
        reg = region if callable(region) else (lambda i, j, p: region)
        m = len(rows)
        for i in range(m if loop else m - 1):
            i1 = (i + 1) % m
            for j in range(n if closed else n - 1):
                jn = (j + 1) % n
                q = (idx[i][j], idx[i][jn], idx[i1][jn], idx[i1][j])
                c = (rows[i][j] + rows[i1][jn]) * 0.5
                self.face(reversed(q) if flip else q, reg(i, j, c))
        for end, row in ((0, 0), (1, len(rows) - 1)):
            if not caps[end]:
                continue
            ring = idx[row]
            c = sum(rows[row], Vector()) / n
            if isinstance(caps[end], Vector):
                c = caps[end]
            tip = self.v(c, weights)
            for j in range(n):
                jn = (j + 1) % n
                tri = (ring[j], ring[jn], tip) if end == 1 else (ring[jn], ring[j], tip)
                self.face(reversed(tri) if flip else tri, reg(row, j, c))
        return idx

    def ellipsoid(self, centre, radii, axes=None, region="skin", segs=20, rings=12, weights=None, squash=1.0):
        """axes: three unit vectors (radii along each); squash < 1 flattens the -axes[2] half."""
        segs, rings = lod(segs, 6), lod(rings, 4)
        centre = Vector(centre)
        ax = [Vector(a).normalized() for a in (axes or ((1, 0, 0), (0, 1, 0), (0, 0, 1)))]
        rows = []
        for j in range(1, rings):
            el = -math.pi / 2 + math.pi * j / rings
            row = []
            zr = radii[2] * (squash if el < 0 else 1.0)
            for i in range(segs):
                az = 2 * math.pi * i / segs
                row.append(centre + (ax[0] * math.cos(az) * radii[0] + ax[1] * math.sin(az) * radii[1]) * math.cos(el)
                           + ax[2] * math.sin(el) * zr)
            rows.append(row)
        bottom = centre - ax[2] * radii[2] * squash
        top = centre + ax[2] * radii[2]
        self.grid(rows, region, caps=(bottom, top), weights=weights)
        return self

    def ring(self, loop_pts, radius, region, segs=12, front=None, power=2.0, weights=None):
        """A closed tube along a closed loop of points (a hem band, a frame): radius = number or (half width along the
        loop's plane, half depth), front = the depth direction (default: away from the loop's centre)."""
        loop_pts = [Vector(p) for p in loop_pts]
        n = len(loop_pts)
        centre = sum(loop_pts, Vector()) / n
        rx, ry = (radius, radius) if isinstance(radius, (int, float)) else radius
        rows = []
        for i, p in enumerate(loop_pts):
            d = (loop_pts[(i + 1) % n] - loop_pts[i - 1]).normalized()
            hint = (p - centre) if front is None else Vector(front)
            sx, fy = frame(d, hint)
            rows.append(superellipse(p, sx, fy, ry, rx, power, segs))
        self.grid(rows, region, weights=weights, loop=True)
        return self

    def tube(self, path, radii, region, segs=16, caps=(True, True), front=Vector((0, -1, 0)), power=2.0,
             weights=None, cap_scale=1.0, fronts=None):
        """A tube along a list of points. radii: one number, or one entry per point (a number or (half width, half
        depth)). Rounded caps (a quarter ellipse as long as the radius times cap_scale); an open end stays open.
        fronts: a depth direction per point (a strand lying flat on the head: its normals)."""
        path = [Vector(p) for p in path]
        n = len(path)
        # a number or a tuple (half width, half depth) is the same all along; a list gives one per point
        rads = [radii] * n if isinstance(radii, (int, float, tuple)) else list(radii)
        rads = [(r, r) if isinstance(r, (int, float)) else tuple(r) for r in rads]
        rows = []
        for i, p in enumerate(path):
            d = (path[min(i + 1, n - 1)] - path[max(i - 1, 0)]).normalized()
            sx, fy = frame(d, fronts[i] if fronts else front)
            rows.append(superellipse(p, sx, fy, rads[i][0], rads[i][1], power, segs))
        tips = [False, False]
        body_rows = rows
        for end in (0, 1):
            if not caps[end]:
                continue
            i, j = (0, 1) if end == 0 else (n - 1, n - 2)
            d = (path[i] - path[j]).normalized()
            r = (rads[i][0] + rads[i][1]) / 2 * cap_scale
            cap = []
            for a in ((30, 55, 75) if DETAIL >= 0.999 else (40, 72)):
                k, f = math.sin(math.radians(a)), math.cos(math.radians(a))
                c = path[i] + d * r * k
                cap.append([c + (q - path[i]) * f for q in body_rows[i]])
            tips[end] = path[i] + d * r
            rows = (list(reversed(cap)) + rows) if end == 0 else (rows + cap)
        self.grid(rows, region, caps=tuple(tips), weights=weights)
        return self

    def slab(self, outline, origin, sx, up, n, depth, region, side_region=None, weights=None, centre=(0.0, 0.0)):
        """A flat shape (a 2D outline in sx/up units, star-shaped around `centre`) standing `depth` off a surface along
        n: its back on the surface, its front out, and the sides."""
        o = Vector(origin)
        back = [o + sx * x + up * y for x, y in outline]
        front = [q + n * depth for q in back]
        c = o + sx * centre[0] + up * centre[1]
        reg = side_region or region
        idx = self.grid([back, front], reg, caps=(c, c + n * depth), weights=weights)
        # the front fan takes the front's region
        k = len(outline)
        for f in range(len(self.freg) - k, len(self.freg)):
            self.freg[f] = R[region] if isinstance(region, str) else region
        return idx

    def merge(self, other):
        base = len(self.co)
        self.co += other.co
        self.w += other.w
        self.faces += [tuple(i + base for i in f) for f in other.faces]
        self.freg += other.freg
        return self

    def bvh(self):
        return BVHTree.FromPolygons([tuple(c) for c in self.co], [list(f) for f in self.faces])

    def to_object(self, coll, arm):
        me = bpy.data.meshes.new(self.name)
        me.from_pydata([tuple(c) for c in self.co], [], [list(f) for f in self.faces])
        me.update()
        # outward normals (the generators' windings vary): recalculated per connected shell
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(me)
        bm.free()
        uv0 = me.uv_layers.new(name="UVMap")
        uv1 = me.uv_layers.new(name="Region")
        col = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
        for poly in me.polygons:
            reg = self.freg[poly.index]
            c = [ta.srgb_to_linear(x) for x in ta.hex_rgb(PREVIEW[REGIONS[reg]])]
            for li in poly.loop_indices:
                uv0.data[li].uv = (0.0, 0.0)
                uv1.data[li].uv = (reg + 0.0, 1.0)
                col.data[li].color = (*c, 1.0)
        me.shade_smooth()
        obj = bpy.data.objects.new(self.name, me)
        coll.objects.link(obj)
        for vi, weights in enumerate(self.w):
            total = sum(max(0.0, x) for x in weights.values()) or 1.0
            for bone, wt in weights.items():
                if wt <= 1e-4:
                    continue
                g = obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
                g.add([vi], wt / total, "REPLACE")
        obj.parent = arm
        mod = obj.modifiers.new("Armature", "ARMATURE")
        mod.object = arm
        obj.data.materials.append(preview_material())
        return obj


def superellipse(c, xa, ya, rx, ry, power, n, keep=False):
    """n points round a superellipse (n at the current DETAIL: every tube's and ring's section comes from here; keep:
    always n, for a shape with a pattern round it)."""
    n = n if keep else lod(n, 4)
    pts = []
    e = 2.0 / power
    for i in range(n):
        a = 2 * math.pi * i / n
        pts.append(c + xa * (spow(math.cos(a), e) * rx) + ya * (spow(math.sin(a), e) * ry))
    return pts


def frame(direction, front_hint=Vector((0, -1, 0))):
    """Side and front axes perpendicular to a path direction (the front axis as close to front_hint as can be)."""
    d = direction.normalized()
    f = front_hint - d * front_hint.dot(d)
    if f.length < 1e-4:
        f = Vector((0, 0, 1)) - d * d.z
        if f.length < 1e-4:
            f = Vector((1, 0, 0))
    f.normalize()
    return d.cross(f).normalized(), f


def bezier(points, n):
    """n points along a Catmull-Rom spline through `points`."""
    pts = [Vector(p) for p in points]
    if len(pts) == 2:
        return [pts[0].lerp(pts[1], i / (n - 1)) for i in range(n)]
    out = []
    segs = len(pts) - 1
    for k in range(n):
        u = k / (n - 1) * segs
        i = min(int(u), segs - 1)
        t = u - i
        p0 = pts[max(i - 1, 0)]
        p1, p2 = pts[i], pts[i + 1]
        p3 = pts[min(i + 2, segs)]
        t2, t3 = t * t, t * t * t
        out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    return out


def on_surface(bvh, x, z, from_y=-2.0):
    """The point on a mesh (a face) seen from the front at (x, z), and its normal."""
    hit, normal, _, _ = bvh.ray_cast(Vector((x, from_y, z)), Vector((0, 1, 0)))
    return hit, normal


def surface_frame(normal, up=Vector((0, 0, 1))):
    """(side, up, normal) axes on a surface."""
    side = up.cross(normal).normalized()
    if side.length < 1e-4:
        side = Vector((1, 0, 0))
    upv = normal.cross(side).normalized()
    return side, upv, normal


# ---------------------------------------------------------------------------------------------------- the rig

FACE_BONES = ["Eyes", "EyesHappy", "Mouth", "MouthOpen", "MouthSad"]


def bones():
    b = {
        "Root": ((0, 0, 0), (0, 0.15, 0), None),
        "Hips": ((0, 0, 0.47), (0, 0, 0.62), "Root"),
        "Spine": ((0, 0, 0.62), (0, 0, 0.84), "Hips"),
        "Head": ((0, 0, 0.88), (0, 0, 1.52), "Spine"),
    }
    for name, z in (("Eyes", 1.13), ("EyesHappy", 1.13), ("Mouth", 1.03), ("MouthOpen", 1.03), ("MouthSad", 1.03)):
        y = -0.30
        b[name] = ((0, y, z), (0, y, z + 0.06), "Head")
    for side in (1, -1):
        n = side_name(side)
        b[f"UpperArm.{n}"] = (tuple(mirror(SHOULDER, side)), tuple(mirror(ELBOW, side)), "Spine")
        b[f"Forearm.{n}"] = (tuple(mirror(ELBOW, side)), tuple(mirror(WRIST, side)), f"UpperArm.{n}")
        b[f"Hand.{n}"] = (tuple(mirror(WRIST, side)), tuple(mirror(HAND_C + ARM_DIR * 0.06, side)), f"Forearm.{n}")
        b[f"Thigh.{n}"] = (tuple(mirror(HIP, side)), tuple(mirror(KNEE, side)), "Hips")
        b[f"Shin.{n}"] = (tuple(mirror(KNEE, side)), tuple(mirror(ANKLE, side)), f"Thigh.{n}")
        b[f"Foot.{n}"] = (tuple(mirror(ANKLE, side)), tuple(mirror(TOE, side)), f"Shin.{n}")
    return b


def make_armature(coll):
    data = bpy.data.armatures.new("ChibiRig")
    arm = bpy.data.objects.new("ChibiRig", data)
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


# ---------------------------------------------------------------------------------------------------- materials, previews

def preview_material():
    mat = bpy.data.materials.get("ChibiPreview")
    if mat:
        return mat
    mat = bpy.data.materials.new("ChibiPreview")
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_name = "Col"
    nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.55
    bsdf.inputs["Coat Weight"].default_value = 0.15
    return mat


def recolour(obj, overrides):
    """Sets the preview colours of a piece's regions (times its baked ambient occlusion, if any)."""
    me = obj.data
    col = me.color_attributes["Col"]
    reg = me.uv_layers["Region"]
    for poly in me.polygons:
        for li in poly.loop_indices:
            u, v = reg.data[li].uv
            name = REGIONS[int(round(u))]
            c = [ta.srgb_to_linear(x) for x in ta.hex_rgb(overrides.get(name, PREVIEW[name]))]
            col.data[li].color = (*(x * v for x in c), 1.0)
