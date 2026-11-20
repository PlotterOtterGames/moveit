"""Superboks!'s boxing kit (run inside Blender 5.1; `build.py` runs it headless), as code:

    Ring     the ring: a padded canvas with the logo, the apron round it down to the floor (a trim band, panels), four
             corner posts with turnbuckle pads (the hero's corner, the opponent's, two neutral), three ropes a side
             sagging a little, ties at their middles, steps at the hero's corner
    Glove    a right boxing glove (the game mirrors it for the left hand): a round fist with a thumb, a padded cuff, a strap
    Belt     a championship belt: a strap with a big plate (a star, a gem) and two side plates (the stages' prizes)
    Bag      a punching bag on its chain (the gym)
    Lamp     a hanging lamp over the ring (a shade, a bulb)

Colours are not in the model: like the chibis and Gara!'s kart, every face says which REGION it is in the second UV map
(u = region, v = baked ambient occlusion) and the game colours the regions per stage and per glove (Tundu/Athlete's
table), so one ring is every stage's ring. Facing -Y (Unity +Z), the ring centred on the origin, the canvas at RING_H,
metres; chibi-sized (a chibi is 1.52 m tall).
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "tools"))
sys.path.insert(0, os.path.join(HERE, "..", "chibi"))
import kit  # noqa: E402   (the chibis' mesh helpers)
import tundu_art as ta  # noqa: E402

UNITY_ART = os.path.join(ta.REPO, "unity", "MoveItGames", "Assets", "Games", "Boxing", "Art")

# Region numbers: the colour table (Tundu/Athlete _Regions[], 24 at most); keep in step with BoxingLooks.cs.
REGIONS = ["canvas", "logo", "logo2", "apron", "apron_trim", "post", "pad_a", "pad_b", "pad_n", "rope", "rope_tie", "steps",
           "metal", "glove", "glove_cuff", "glove_trim", "belt_strap", "belt_plate", "belt_gem", "bag", "bag_band", "dark",
           "light", "wood"]
R = {name: i for i, name in enumerate(REGIONS)}

PREVIEW = {"canvas": "#2E6FE8", "logo": "#FFC93C", "logo2": "#FFFFFF", "apron": "#1E2A5C", "apron_trim": "#FFC93C",
           "post": "#C9CED9", "pad_a": "#2E6FE8", "pad_b": "#E8403A", "pad_n": "#F4F2F8", "rope": "#F4F2F8",
           "rope_tie": "#E8403A", "steps": "#3A3E5C", "metal": "#B9BDCC", "glove": "#E8403A", "glove_cuff": "#F4F2F8",
           "glove_trim": "#FFC93C", "belt_strap": "#1E1A2C", "belt_plate": "#FFC93C", "belt_gem": "#E8403A",
           "bag": "#E8403A", "bag_band": "#1E1A2C", "dark": "#2A2D3E", "light": "#FFF6C8", "wood": "#B9844F"}

# ------------------------------------------------------------------------------------------------- proportions

RING_H = 0.62          # the canvas's top above the floor
POST = 2.3             # the posts' centres, from the middle (4.6 m apart)
EDGE = 2.6             # the canvas's edge
POST_TOP = 1.32        # the posts above the canvas
ROPES = (0.36, 0.68, 1.0)   # the ropes above the canvas
ROPE_R = 0.034


class Shape(kit.Part):
    """kit.Part without bones: faces carry the boxing kit's regions."""

    def __init__(self, name):
        super().__init__(name, weights={})

    def face(self, idx, region):
        self.faces.append(tuple(idx))
        self.freg.append(R[region] if isinstance(region, str) else region)

    def slab(self, outline, origin, sx, up, n, depth, region, side_region=None, weights=None, centre=(0.0, 0.0)):
        # kit.Part.slab looks its front's region up in the chibis' table: hand it numbers
        return super().slab(outline, origin, sx, up, n, depth, R[region] if isinstance(region, str) else region,
                            R[side_region] if isinstance(side_region, str) else side_region, weights, centre)

    def rbox(self, centre, half, region, segs=16, rings=10, power=3.0):
        """A rounded box: a superellipsoid (power > 2 = boxier)."""
        segs, rings = kit.lod(segs, 8), kit.lod(rings, 4)
        c = Vector(centre)
        rows = []
        e = 2.0 / power
        for j in range(1, rings):
            el = -math.pi / 2 + math.pi * j / rings
            ce, se = math.cos(el), math.sin(el)
            row = []
            for i in range(segs):
                az = 2 * math.pi * i / segs
                ca, sa = math.cos(az), math.sin(az)
                row.append(c + Vector((kit.spow(ca, e) * kit.spow(ce, e) * half[0], kit.spow(sa, e) * kit.spow(ce, e) * half[1],
                                       kit.spow(se, e) * half[2])))
            rows.append(row)
        self.grid(rows, region, caps=(c - Vector((0, 0, half[2])), c + Vector((0, 0, half[2]))))
        return self

    def lathe(self, profile, origin, axis, region, segs=24, caps=(False, False)):
        """A surface of revolution: profile = [(radius, along)] round axis through origin."""
        segs = kit.lod(segs, 8)
        axis = Vector(axis).normalized()
        o = Vector(origin)
        side = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((1, 0, 0))
        u = axis.cross(side).normalized()
        w = axis.cross(u).normalized()
        rows = [[o + axis * a + (u * math.cos(2 * math.pi * k / segs) + w * math.sin(2 * math.pi * k / segs)) * r
                 for k in range(segs)] for r, a in profile]
        self.grid(rows, region if callable(region) else (lambda i, j, p: region), caps=caps)
        return self


def star(r_out, r_in, n=5, rot=90.0):
    pts = []
    for k in range(2 * n):
        a = math.radians(rot) + math.pi * k / n
        r = r_out if k % 2 == 0 else r_in
        pts.append((math.cos(a) * r, math.sin(a) * r))
    return pts


def disc(r, n=32):
    return [(math.cos(2 * math.pi * k / n) * r, math.sin(2 * math.pi * k / n) * r) for k in range(n)]


def rounded_rect(hw, hh, r=None, n=5):
    r = r if r is not None else min(hw, hh) * 0.45
    pts = []
    for cx, cy, a0 in ((hw - r, hh - r, 0), (-hw + r, hh - r, 90), (-hw + r, -hh + r, 180), (hw - r, -hh + r, 270)):
        for k in range(n + 1):
            a = math.radians(a0 + 90 * k / n)
            pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    return pts


UP, FWD, RIGHT = Vector((0, 0, 1)), Vector((0, -1, 0)), Vector((1, 0, 0))


# ------------------------------------------------------------------------------------------------- the ring

def ring():
    s = Shape("Ring")
    top = RING_H
    # the canvas: a padded slab, a little rounded at its edges
    s.rbox((0, 0, top - 0.05), (EDGE, EDGE, 0.05), "canvas", segs=kit.lod(36, 16), rings=kit.lod(8, 4), power=8.0)
    # the logo in the middle: a big disc with a ring, a star on it
    s.slab(disc(0.95, kit.lod(48, 20)), Vector((0, 0, top)), RIGHT, -FWD, UP, 0.006, "logo")
    s.slab(disc(0.8, kit.lod(48, 20)), Vector((0, 0, top + 0.006)), RIGHT, -FWD, UP, 0.004, "logo2")
    s.slab(star(0.68, 0.29), Vector((0, 0, top + 0.01)), RIGHT, -FWD, UP, 0.004, "logo")
    # a band of the trim colour round the canvas's edge
    for k in range(4):
        a = math.pi / 2 * k
        d = Vector((math.cos(a), math.sin(a), 0))
        t = Vector((-d.y, d.x, 0))
        c = d * (EDGE - 0.16) + Vector((0, 0, top + 0.002))
        s.slab([(-(EDGE - 0.08), -0.05), (EDGE - 0.08, -0.05), (EDGE - 0.08, 0.05), (-(EDGE - 0.08), 0.05)], c, t, d, UP, 0.004, "logo2")
    # the apron: the skirt round the platform from the canvas's edge to the floor, a trim band at its top, panels
    sk = EDGE + 0.02
    rows = []
    for z in (top - 0.02, top - 0.12, 0.08, 0.0):
        rows.append(kit.superellipse(Vector((0, 0, z)), RIGHT, -FWD, sk + (0.03 if z < 0.1 else 0.0), sk + (0.03 if z < 0.1 else 0.0), 14.0,
                                     kit.lod(64, 24), keep=True))
    s.grid(rows, lambda i, j, p: "apron_trim" if i == 0 else "apron", closed=True)
    for k in range(4):
        a = math.pi / 2 * k
        d = Vector((math.cos(a), math.sin(a), 0))
        t = Vector((-d.y, d.x, 0))
        for off in (-1.3, 0.0, 1.3):
            c = d * (sk + 0.012) + t * off + Vector((0, 0, top * 0.48))
            s.slab(rounded_rect(0.42, 0.15), c, t, UP, d, 0.008, "logo" if off == 0.0 else "apron_trim")
            if off == 0.0:
                s.slab(star(0.11, 0.045), c + d * 0.009, t, UP, d, 0.006, "logo2")
    # the corner posts with their pads
    corners = [(-1, 1, "pad_a"), (1, -1, "pad_b"), (1, 1, "pad_n"), (-1, -1, "pad_n")]
    for cx, cy, pad in corners:
        base = Vector((cx * POST, cy * POST, top - 0.1))
        s.tube([base, base + Vector((0, 0, POST_TOP + 0.12))], 0.055, "post", segs=14, caps=(False, True))
        # the pad: a tall cushion over the post, facing the middle
        inward = Vector((-cx, -cy, 0)).normalized()
        c = Vector((cx * POST, cy * POST, top + 0.68)) + inward * 0.07
        s.rbox(c, (0.17, 0.17, 0.5), pad, power=2.4)
        s.ellipsoid(Vector((cx * POST, cy * POST, top + POST_TOP + 0.14)), (0.08, 0.08, 0.05), region="metal", segs=14, rings=8)
    # the ropes: three a side, sagging a little in the middle, wrapped where they meet the posts
    pts_n = kit.lod(13, 7)
    for h in ROPES:
        z = top + h
        for (ax, ay), (bx, by) in (((-1, -1), (1, -1)), ((1, -1), (1, 1)), ((1, 1), (-1, 1)), ((-1, 1), (-1, -1))):
            a = Vector((ax * POST, ay * POST, z))
            b = Vector((bx * POST, by * POST, z))
            mid = (a + b) / 2
            inward = -Vector((mid.x, mid.y, 0)).normalized()
            path = []
            for k in range(pts_n):
                t = k / (pts_n - 1)
                sag = 4 * t * (1 - t)
                path.append(a.lerp(b, t) + Vector((0, 0, -0.035 * sag)) + inward * (0.06 + 0.0 * sag))
            s.tube(path, ROPE_R, "rope", segs=10, caps=(False, False))
            # the tie at the middle
            s.tube([mid + Vector((0, 0, -0.05)) + inward * 0.06, mid + Vector((0, 0, 0.05)) + inward * 0.06], (0.05, 0.045), "rope_tie",
                   segs=10, front=inward, caps=(True, True))
    # the steps at the hero's corner (outside, behind the near ropes)
    for k in range(3):
        z = top * (k + 1) / 3.0
        c = Vector((-POST - 0.2 + 0.0 * k, POST + 0.55 - 0.25 * k, z / 2))
        s.rbox(c, (0.32, 0.13, z / 2), "steps", power=5.0)
    return s


# ------------------------------------------------------------------------------------------------- the gloves, the belt

GLOVE_LEN = 0.26


def glove():
    """A right boxing glove pointing along -Y (the punch), its cuff round the origin (the wrist), the thumb on the
    inside (-X) and a little up; the palm down (-Z). About twice a chibi's mitten."""
    s = Shape("Glove")
    # the fist: a fat rounded body, fuller at the knuckles
    def fist_region(i, j, p):
        return "glove"
    rows = []
    n = kit.lod(24, 12)
    sections = [(0.02, 0.066, 0.062, 2.2), (0.05, 0.08, 0.074, 2.3), (0.1, 0.098, 0.088, 2.5), (0.15, 0.106, 0.094, 2.6),
                (0.19, 0.104, 0.092, 2.5), (0.22, 0.094, 0.083, 2.3), (0.243, 0.072, 0.064, 2.1), (0.256, 0.04, 0.036, 2.0)]
    for y, hw, hh, pw in sections:
        rows.append(kit.superellipse(Vector((0, -y, 0.006)), RIGHT, UP, hw, hh, pw, n, keep=True))
    s.grid(rows, fist_region, caps=(Vector((0, -0.01, 0.006)), Vector((0, -0.262, 0.006))))
    # the thumb, folded along the inside of the fist
    s.tube(kit.bezier([Vector((-0.07, -0.06, 0.02)), Vector((-0.11, -0.12, 0.03)), Vector((-0.085, -0.18, 0.04))], 7),
           [(0.036, 0.034), (0.04, 0.038), (0.04, 0.038), (0.038, 0.036), (0.034, 0.032), (0.028, 0.026), (0.02, 0.02)], "glove",
           segs=12, front=UP)
    # the cuff: a padded band round the wrist, a strap round it
    s.lathe([(0.052, 0.07), (0.066, 0.06), (0.07, 0.03), (0.07, -0.01), (0.064, -0.03), (0.05, -0.034)], (0, 0, 0.004), (0, -1, 0),
            "glove_cuff", segs=20)
    s.lathe([(0.073, 0.026), (0.077, 0.02), (0.077, 0.004), (0.073, -0.002)], (0, 0, 0.004), (0, -1, 0), "glove_trim", segs=20)
    # a stripe over the knuckles
    s.tube(kit.bezier([Vector((-0.08, -0.17, 0.066)), Vector((0.0, -0.18, 0.098)), Vector((0.08, -0.17, 0.066))], 9), (0.012, 0.004),
           "glove_trim", segs=6, front=UP, caps=(True, True))
    return s


def belt():
    """A championship belt standing up, its front (-Y): a strap in a loop (belt_strap), a big plate (belt_plate) with a ring
    of studs, a star and a gem (belt_gem), two side plates."""
    s = Shape("Belt")
    n = kit.lod(40, 16)
    # the strap: a flat band round an oval
    loop = [Vector((math.cos(2 * math.pi * k / n) * 0.42, math.sin(2 * math.pi * k / n) * 0.3, 0.0)) for k in range(n)]
    s.ring(loop, (0.02, 0.09), "belt_strap", segs=8, front=None)
    front = Vector((0, -1, 0))
    c = Vector((0, -0.33, 0.0))
    s.slab(rounded_rect(0.2, 0.15, 0.06), c, RIGHT, UP, front, 0.03, "belt_plate")
    s.slab(disc(0.11, 28), c + front * 0.03, RIGHT, UP, front, 0.012, "belt_strap")
    s.slab(star(0.09, 0.04), c + front * 0.042, RIGHT, UP, front, 0.012, "belt_plate")
    s.ellipsoid(c + front * 0.06, (0.03, 0.016, 0.03), region="belt_gem", segs=12, rings=8)
    for k in range(10):
        a = 2 * math.pi * k / 10
        s.ellipsoid(c + front * 0.034 + RIGHT * math.cos(a) * 0.16 + UP * math.sin(a) * 0.115, (0.012, 0.008, 0.012), region="belt_gem",
                    segs=8, rings=6)
    for side in (1, -1):
        a = math.radians(90 - side * 52)
        p = Vector((math.cos(a) * 0.44 * side * side, -math.sin(a) * 0.32, 0))
        nrm = Vector((p.x / 0.42 ** 2, p.y / 0.3 ** 2, 0)).normalized()
        sx = Vector((-nrm.y, nrm.x, 0)).normalized()
        s.slab(rounded_rect(0.07, 0.07, 0.03), p, sx, UP, nrm, 0.02, "belt_plate")
        s.slab(star(0.04, 0.018), p + nrm * 0.02, sx, UP, nrm, 0.008, "belt_gem")
    return s


def bag():
    """A punching bag hanging from its chain: a long padded cylinder (bag) with bands (bag_band), the chain (metal)."""
    s = Shape("Bag")
    prof = [(0.0, 0.0), (0.12, 0.01), (0.2, 0.06), (0.22, 0.12), (0.22, 0.14), (0.222, 0.3), (0.222, 0.5), (0.222, 0.56),
            (0.222, 0.72), (0.22, 0.86), (0.22, 0.9), (0.21, 0.98), (0.15, 1.04), (0.0, 1.06)]
    s.lathe(prof, (0, 0, 0), (0, 0, 1), lambda i, j, p: "bag_band" if p.z > 0.86 or p.z < 0.12 or 0.5 < p.z < 0.56 else "bag",
            segs=24)
    for k in range(4):
        a = math.pi / 2 * k + math.pi / 4
        s.tube([Vector((math.cos(a) * 0.17, math.sin(a) * 0.17, 1.02)), Vector((0, 0, 1.3))], 0.008, "metal", segs=6)
    for k in range(6):
        z = 1.3 + 0.07 * k
        loop = [Vector((math.cos(2 * math.pi * i / 12) * 0.028 * (1 if k % 2 else 0.4), math.sin(2 * math.pi * i / 12) * 0.028 * (0.4 if k % 2 else 1),
                        z + math.sin(2 * math.pi * i / 12) * 0.0)) for i in range(12)]
        s.ring([Vector((p.x, p.y, z + (p.x if k % 2 else p.y) * 0.0)) + Vector((0, 0, 0)) for p in loop], 0.008, "metal", segs=6,
               front=Vector((0, 0, 1)))
    return s


def lamp():
    """A hanging lamp: a cone shade (dark) open below with a bulb (light), its cord up."""
    s = Shape("Lamp")
    s.lathe([(0.04, 0.0), (0.06, -0.05), (0.2, -0.2), (0.26, -0.28), (0.25, -0.3), (0.19, -0.22), (0.05, -0.07)], (0, 0, 0), (0, 0, 1),
            "dark", segs=24)
    s.ellipsoid(Vector((0, 0, -0.22)), (0.09, 0.09, 0.09), region="light", segs=14, rings=10)
    s.tube([Vector((0, 0, 0.0)), Vector((0, 0, 1.6))], 0.012, "dark", segs=6, caps=(False, False))
    return s


# ------------------------------------------------------------------------------------------------- objects

def to_object(shape, coll):
    me = bpy.data.meshes.new(shape.name)
    me.from_pydata([tuple(c) for c in shape.co], [], [list(f) for f in shape.faces])
    me.update()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    uv0 = me.uv_layers.new(name="UVMap")
    uv1 = me.uv_layers.new(name="Region")
    col = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    for poly in me.polygons:
        reg = shape.freg[poly.index]
        c = [ta.srgb_to_linear(x) for x in ta.hex_rgb(PREVIEW[REGIONS[reg]])]
        for li in poly.loop_indices:
            uv0.data[li].uv = (0.0, 0.0)
            uv1.data[li].uv = (reg + 0.0, 1.0)
            col.data[li].color = (*c, 1.0)
    me.shade_smooth()
    obj = bpy.data.objects.new(shape.name, me)
    coll.objects.link(obj)
    obj.data.materials.append(kit.preview_material())
    return obj


PARTS = (ring, glove, belt, bag, lamp)


def build():
    ta.clear_scene()
    coll = ta.collection("Boxing")
    for o in list(coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    made = {}
    for fn in PARTS:
        shape = fn()
        made[shape.name] = to_object(shape, coll)
    return {n: sum(len(p.vertices) - 2 for p in o.data.polygons) for n, o in made.items()}


def objects():
    return [o for o in bpy.data.collections["Boxing"].objects if o.type == "MESH"]


def recolour(obj, overrides):
    me = obj.data
    col = me.color_attributes.get("Col")
    uv1 = me.uv_layers.get("Region")
    if col is None or uv1 is None:
        return
    for poly in me.polygons:
        reg = int(round(uv1.data[poly.loop_indices[0]].uv[0]))
        name = REGIONS[reg]
        c = [ta.srgb_to_linear(x) for x in ta.hex_rgb(overrides.get(name, PREVIEW[name]))]
        for li in poly.loop_indices:
            col.data[li].color = (*c, 1.0)


def copy(src, coll, mw, name, colours=None):
    o = bpy.data.objects.new(name, src.data.copy())
    o.matrix_world = mw
    coll.objects.link(o)
    if colours:
        recolour(o, colours)
    return o


def stand_in(coll, at, scale=1.0, colour="#FFD9C2"):
    """A chibi-sized stand-in (head and body): the ring's proportions in the previews."""
    bm = bmesh.new()
    s = scale
    ta.ellipsoid(bm, Vector((0, 0, 1.12 * s)) + at, (0.36 * s, 0.33 * s, 0.34 * s))
    ta.ellipsoid(bm, Vector((0, 0, 0.6 * s)) + at, (0.2 * s, 0.15 * s, 0.3 * s))
    o = ta.mesh_object("pv_stand", bm, coll)
    me = o.data
    col = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    c = [ta.srgb_to_linear(x) for x in ta.hex_rgb(colour)]
    for li in range(len(me.loops)):
        col.data[li].color = (*c, 1.0)
    me.shade_smooth()
    o.data.materials.append(kit.preview_material())
    return o


STAGES = [
    {},
    {"canvas": "#3A3E5C", "logo": "#FF5A4E", "apron": "#141938", "apron_trim": "#FF5A4E", "pad_a": "#FFC93C", "pad_b": "#FF5A4E",
     "pad_n": "#3A3E5C", "rope": "#FFC93C", "rope_tie": "#141938"},
    {"canvas": "#E8F2FF", "logo": "#31C6F4", "logo2": "#FFFFFF", "apron": "#6B4A32", "apron_trim": "#E8F2FF", "post": "#8A5A36",
     "pad_a": "#31C6F4", "pad_b": "#E8403A", "pad_n": "#FFFFFF", "rope": "#C9A26B", "rope_tie": "#6B4A32"},
    {"canvas": "#2A1E5C", "logo": "#7DF2FF", "logo2": "#FF5AD2", "apron": "#141938", "apron_trim": "#7DF2FF", "pad_a": "#7DF2FF",
     "pad_b": "#FF5AD2", "pad_n": "#3A2E80", "rope": "#7DF2FF", "rope_tie": "#FF5AD2", "post": "#5A6178"},
]


def preview():
    out_dir = os.path.join(HERE, "previews")
    os.makedirs(out_dir, exist_ok=True)
    coll = ta.collection("Preview")
    for o in list(coll.objects):
        if o.type == "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    for o in objects():
        o.hide_render = True
    src = {o.name: o for o in objects()}
    paths = []
    # the ring as each stage colours it, from the game's camera (behind the hero, at its head's height)
    for i, look in enumerate(STAGES):
        for o in list(coll.objects):
            if o.type == "MESH":
                bpy.data.objects.remove(o, do_unlink=True)
        copy(src["Ring"], coll, Matrix(), "pv_ring", look)
        stand_in(coll, Vector((0, 1.3, RING_H)), colour="#9FB4FF")
        stand_in(coll, Vector((0, -1.0, RING_H)), colour="#FFD9C2")
        for k, x in enumerate((-1.6, 1.6)):
            copy(src["Lamp"], coll, Matrix.Translation((x, 0, 4.3)), f"pv_lamp{k}")
        ta.preview_rig(target=(0, -1.0, RING_H + 0.9), distance=3.4, yaw_deg=180, pitch_deg=4, lens=32)
        lights()
        paths.append(ta.render_preview(os.path.join(out_dir, f"ring{i}_game.png"), res=(1600, 900)))
        if i == 0:
            ta.preview_rig(target=(0, 0, RING_H), distance=10.5, yaw_deg=-35, pitch_deg=24, lens=40)
            lights()
            paths.append(ta.render_preview(os.path.join(out_dir, "ring_three.png"), res=(1600, 900)))
    # the gloves, the belt, the bag
    for o in list(coll.objects):
        if o.type == "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    for k, look in enumerate(({}, {"glove": "#2E6FE8", "glove_trim": "#FFFFFF"}, {"glove": "#B9BDCC", "glove_cuff": "#5A6178",
                                                                                     "glove_trim": "#FFC93C"},
                              {"glove": "#7DF2FF", "glove_cuff": "#2A1E5C", "glove_trim": "#FF5AD2"})):
        copy(src["Glove"], coll, Matrix.Translation((-0.9 + 0.6 * k, 0, 0.4)) @ Matrix.Rotation(math.radians(-30), 4, "Z"), f"pv_glove{k}", look)
    copy(src["Belt"], coll, Matrix.Translation((0.0, 0.6, 0.9)), "pv_belt")
    copy(src["Bag"], coll, Matrix.Translation((1.6, 0.6, 0.0)), "pv_bag")
    ta.preview_rig(target=(0.2, 0.3, 0.6), distance=3.6, yaw_deg=-20, pitch_deg=14, lens=45)
    lights()
    paths.append(ta.render_preview(os.path.join(out_dir, "props.png"), res=(1600, 900)))
    for o in objects():
        o.hide_render = False
    return paths


def lights():
    import chibi
    chibi.lights()


# ------------------------------------------------------------------------------------------------- ambient occlusion

def bake_ao(samples=48):
    """Ambient occlusion into the second UV map's v (0.35 + 0.65 * occlusion), each part on a floor."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.render.bake.target = "VERTEX_COLORS"
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.light_settings.distance = 0.5
    coll = ta.collection("Bake")
    done = 0
    for src in objects():
        for o in list(coll.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        target = copy(src, coll, Matrix(), "bk_" + src.name)
        if src.name in ("Ring", "Bag"):
            bm = bmesh.new()
            bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=8.0)
            ta.mesh_object("bk_floor", bm, coll)
        for o in objects():
            o.hide_render = True
            o.hide_set(True)
        me = target.data
        attr = me.color_attributes.get("AO") or me.color_attributes.new("AO", "FLOAT_COLOR", "POINT")
        me.color_attributes.active_color = attr
        for o in bpy.context.view_layer.objects:
            o.select_set(False)
        target.select_set(True)
        bpy.context.view_layer.objects.active = target
        bpy.ops.object.bake(type="AO")
        uv = src.data.uv_layers["Region"]
        for poly in src.data.polygons:
            for li in poly.loop_indices:
                c = attr.data[src.data.loops[li].vertex_index].color
                ao = (c[0] + c[1] + c[2]) / 3
                uv.data[li].uv = (uv.data[li].uv[0], 0.35 + 0.65 * ao)
        done += 1
    for o in list(coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for o in objects():
        o.hide_render = False
        o.hide_set(False)
    return done


# ------------------------------------------------------------------------------------------------- export

def export():
    os.makedirs(UNITY_ART, exist_ok=True)
    path = os.path.join(UNITY_ART, "Boxing.fbx")
    for o in objects():
        for name in ("Col", "AO"):
            attr = o.data.color_attributes.get(name)
            if attr:
                o.data.color_attributes.remove(attr)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects():
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects()[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE", use_mesh_modifiers=False,
        path_mode="STRIP", colors_type="LINEAR", use_custom_props=False, bake_anim=False)
    return path
