"""Yjet!'s props (run inside Blender 5.1 by build.py): low-poly, rounded, in Tundu's palette (one 64 x 64 palette texture,
one material for all of them, like Gara!'s scenery), exported together into Assets/Games/Space/Art/SpaceProps.fbx (one
mesh object each, its pivot where the game holds it).

    the cast       Alien0..3 (four little lost aliens: green with one big eye, pink with two and two antennas, blue
                   with three, purple with two and curly antennas; feet on the ground at the origin, facing -Y) and
                   AlienArm0..3 (an arm each, its pivot at the shoulder, reaching along +X: the game places, mirrors
                   and waves them), TowShip (the rescue tug that brings a dry astronaut home: a friendly robot face
                   in its window, a crane with a hook below; its pivot in its middle, facing -Y)
    in space       Rock0..2 (asteroids, radius 1 round the pivot: the game scales them), Star (a puffy faceted star,
                   radius 0.5: stardust and the big stars), Satellite (scenery)
    on the planets Pad (the landing pad, radius 3 m, its top at z 0.25: the game draws the target rings on it), Flag,
                   Lollipop, CandyCane, IceCrystal, GoldCrystal (planet dressing; the home planet also gets Gara!'s
                   houses and trees, the jungle Vrapo!'s ferns)
"""

import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.noise import noise

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "tools"))
import tundu_art as ta  # noqa: E402

UNITY_ART = os.path.join(ta.REPO, "unity", "MoveItGames", "Assets", "Games", "Space", "Art")

# the colours this game adds to the shared palette (appended: every cell before keeps its place)
for name, hex_ in (("ice", "#BDEBFF"), ("ice_dark", "#7FC8F0"), ("mint", "#9FF2C8"), ("lilac", "#C9A8FF"),
                   ("gold", "#FFB020"), ("glass", "#9FE2FF")):
    ta.PALETTE.setdefault(name, hex_)


# ------------------------------------------------------------------------------------------------- shapes

def cyl(bm, a, b, r0, r1=None, segs=12):
    a, b = Vector(a), Vector(b)
    d = b - a
    m = Matrix.Translation((a + b) / 2) @ d.to_track_quat("Z", "Y").to_matrix().to_4x4()
    return bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r0,
                                 radius2=r0 if r1 is None else r1, depth=d.length, matrix=m)


def box(bm, centre, size, rot=(0, 0, 0)):
    from mathutils import Euler
    m = Matrix.Translation(centre) @ Euler([math.radians(a) for a in rot]).to_matrix().to_4x4() @ Matrix.Diagonal((*size, 1))
    return bmesh.ops.create_cube(bm, size=1.0, matrix=m)


def tube(bm, pts, r, segs=10):
    """A tube through points (each segment a cylinder, a ball at each joint so the bends are round)."""
    pts = [Vector(p) for p in pts]
    for i in range(len(pts) - 1):
        cyl(bm, pts[i], pts[i + 1], r, r, segs)
        if 0 < i:
            ta.ellipsoid(bm, pts[i], (r, r, r), segments=segs, rings=max(4, segs // 2))
    ta.ellipsoid(bm, pts[-1], (r, r, r), segments=segs, rings=max(4, segs // 2))


def star_outline(r_out, r_in, n=5, rot=90.0):
    return [(math.cos(math.radians(rot) + math.pi * k / n) * (r_out if k % 2 == 0 else r_in),
             math.sin(math.radians(rot) + math.pi * k / n) * (r_out if k % 2 == 0 else r_in)) for k in range(2 * n)]


def puffy_star(bm, centre, r_out, r_in, depth, axis_front=Vector((0, -1, 0)), up=Vector((0, 0, 1))):
    """A faceted, puffy star: its outline joined to a point in front and one behind (a gem of a star)."""
    c = Vector(centre)
    side = up.cross(axis_front).normalized()
    outline = [bm.verts.new(c + side * x + up * y) for x, y in star_outline(r_out, r_in)]
    front = bm.verts.new(c + axis_front * depth)
    back = bm.verts.new(c - axis_front * depth)
    n = len(outline)
    for i in range(n):
        a, b = outline[i], outline[(i + 1) % n]
        bm.faces.new((a, b, front))
        bm.faces.new((b, a, back))


def by_height(top, bottom, split=0.0):
    return lambda f: top if f.calc_center_median().z > split else bottom


# ------------------------------------------------------------------------------------------------- the cast

ALIENS = [  # body, belly, accents, eyes, antennas
    ("leaf", "mint", "sun", 1, "one"),
    ("pink", "cream", "coral", 2, "two"),
    ("sky", "ice", "sun", 3, "bumps"),
    ("grape", "lilac", "pink", 2, "curly"),
]


def alien(coll, i):
    body, belly, accent, eyes, antennas = ALIENS[i]
    pb = ta.PartBuilder()
    # a soft gumdrop body, wider at the bottom
    pb.add(body, lambda bm: ta.ellipsoid(bm, (0, 0, 0.47), (0.42, 0.38, 0.46), segments=24, rings=16))
    pb.add(belly, lambda bm: ta.ellipsoid(bm, (0, -0.2, 0.36), (0.28, 0.2, 0.25), segments=18, rings=12))
    # feet
    for x in (-0.17, 0.17):
        pb.add(body, lambda bm, x=x: ta.ellipsoid(bm, (x, -0.06, 0.07), (0.13, 0.17, 0.08), segments=14, rings=8))
    # eyes on the upper front: white, a dark pupil looking at you, a shine
    places = {1: [(0.0, 0.66, 0.17)], 2: [(-0.15, 0.64, 0.11), (0.15, 0.64, 0.11)],
              3: [(-0.19, 0.6, 0.09), (0.0, 0.7, 0.1), (0.19, 0.6, 0.09)]}[eyes]
    for x, z, r in places:
        y = -math.sqrt(max(0.0, 1 - (x / 0.42) ** 2 - ((z - 0.47) / 0.46) ** 2)) * 0.38 + 0.03
        pb.add("white", lambda bm, x=x, y=y, z=z, r=r: ta.ellipsoid(bm, (x, y - r * 0.15, z), (r, r * 0.6, r * 1.1), segments=18, rings=12))
        pb.add("night", lambda bm, x=x, y=y, z=z, r=r: ta.ellipsoid(bm, (x, y - r * 0.62, z - r * 0.12), (r * 0.55, r * 0.25, r * 0.65), segments=14, rings=10))
        pb.add("white", lambda bm, x=x, y=y, z=z, r=r: ta.ellipsoid(bm, (x - r * 0.2, y - r * 0.82, z + r * 0.22), (r * 0.17, r * 0.08, r * 0.2), segments=10, rings=6))
    # a happy open mouth and pink cheeks
    pb.add("night", lambda bm: ta.ellipsoid(bm, (0, -0.37, 0.42), (0.07, 0.04, 0.045), segments=14, rings=8))
    pb.add("coral", lambda bm: ta.ellipsoid(bm, (0, -0.39, 0.405), (0.04, 0.02, 0.022), segments=10, rings=6))
    for x in (-0.27, 0.27):
        pb.add("blush", lambda bm, x=x: ta.ellipsoid(bm, (x, -0.29, 0.48), (0.07, 0.04, 0.045), segments=12, rings=8))
    # the antennas
    if antennas == "one":
        tube_pts = [(0, 0, 0.88), (0.04, 0.02, 1.08), (0.0, 0.0, 1.22)]
        pb.add(body, lambda bm: tube(bm, tube_pts, 0.025, 8))
        pb.add(accent, lambda bm: ta.ellipsoid(bm, (0.0, 0.0, 1.26), (0.08, 0.08, 0.08), segments=14, rings=10))
    elif antennas == "two":
        for s in (-1, 1):
            pts = [(s * 0.12, 0, 0.86), (s * 0.2, 0.02, 1.04), (s * 0.24, 0.0, 1.14)]
            pb.add(body, lambda bm, pts=pts: tube(bm, pts, 0.022, 8))
            pb.add(accent, lambda bm, s=s: ta.ellipsoid(bm, (s * 0.25, 0.0, 1.19), (0.065, 0.065, 0.065), segments=12, rings=8))
    elif antennas == "bumps":
        for x, z, r in ((-0.2, 0.86, 0.07), (0.0, 0.94, 0.085), (0.2, 0.86, 0.07)):
            pb.add(accent, lambda bm, x=x, z=z, r=r: ta.ellipsoid(bm, (x, 0.04, z), (r, r, r * 1.2), segments=12, rings=8))
    else:
        for s in (-1, 1):
            pts = [(s * 0.1, 0, 0.87)] + [(s * (0.12 + 0.1 * math.sin(t * 2.6)), 0.0, 0.92 + 0.2 * t + 0.06 * math.cos(t * 5.2)) for t in [k / 6 for k in range(1, 7)]]
            pb.add(body, lambda bm, pts=pts: tube(bm, pts, 0.02, 8))
            pb.add(accent, lambda bm, s=s, pts=pts: ta.ellipsoid(bm, pts[-1], (0.06, 0.06, 0.06), segments=12, rings=8))
    return pb.to_object(f"Alien{i}", coll)


def alien_arm(coll, i):
    """An arm from the shoulder (the origin) along +X, a round hand at the end."""
    body = ALIENS[i][0]
    pb = ta.PartBuilder()
    pb.add(body, lambda bm: cyl(bm, (0, 0, 0), (0.2, 0, 0.02), 0.055, 0.045, 10))
    pb.add(body, lambda bm: ta.ellipsoid(bm, (0.24, 0, 0.03), (0.075, 0.07, 0.075), segments=12, rings=8))
    return pb.to_object(f"AlienArm{i}", coll)


def tow_ship(coll):
    """The rescue tug: a round white body with a coral band, a big window with a friendly robot face, two thrusters,
    little fins, a beacon on top, a crane with a hook below (it lifts the astronaut). Facing -Y, ~3 m long."""
    pb = ta.PartBuilder()
    pb.add("white", lambda bm: ta.ellipsoid(bm, (0, 0, 0), (1.05, 1.5, 0.85), segments=28, rings=18))
    pb.add("coral", lambda bm: ta.ellipsoid(bm, (0, 0.05, 0), (1.08, 1.4, 0.3), segments=28, rings=8))
    # the window and the robot face behind it
    pb.add("night", lambda bm: ta.ellipsoid(bm, (0, -1.22, 0.32), (0.62, 0.32, 0.46), segments=24, rings=14))
    pb.add("glass", lambda bm: ta.ellipsoid(bm, (0, -1.28, 0.33), (0.56, 0.28, 0.41), segments=24, rings=14))
    for x in (-0.2, 0.2):
        pb.add("white", lambda bm, x=x: ta.ellipsoid(bm, (x, -1.53, 0.4), (0.11, 0.05, 0.14), segments=14, rings=8))
        pb.add("night", lambda bm, x=x: ta.ellipsoid(bm, (x, -1.57, 0.38), (0.055, 0.03, 0.07), segments=12, rings=8))
    pb.add("night", lambda bm: tube(bm, [(-0.14, -1.55, 0.2), (0.0, -1.58, 0.16), (0.14, -1.55, 0.2)], 0.02, 6))
    # the thrusters and their nozzles
    for x in (-1.15, 1.15):
        pb.add("metal", lambda bm, x=x: cyl(bm, (x, -0.4, -0.15), (x, 0.9, -0.15), 0.32, 0.3, 16))
        pb.add("coral", lambda bm, x=x: ta.ellipsoid(bm, (x, -0.42, -0.15), (0.32, 0.12, 0.32), segments=16, rings=8))
        pb.add("tyre", lambda bm, x=x: cyl(bm, (x, 0.9, -0.15), (x, 1.15, -0.15), 0.26, 0.34, 16))
        # a fin up on each side
        pb.add("coral", lambda bm, x=x: box(bm, (x * 0.85, 0.9, 0.55), (0.08, 0.6, 0.55), rot=(20, 0, 0)))
    # the beacon
    pb.add("metal", lambda bm: cyl(bm, (0, 0.1, 0.8), (0, 0.1, 0.95), 0.14, 0.14, 12))
    pb.add("sun", lambda bm: ta.ellipsoid(bm, (0, 0.1, 1.02), (0.16, 0.16, 0.16), segments=14, rings=10))
    # the crane: an arm down from the belly, a hook
    pb.add("metal", lambda bm: cyl(bm, (0, 0.2, -0.75), (0, 0.2, -1.35), 0.07, 0.07, 10))
    hook = [(0, 0.2, -1.35), (0, 0.2, -1.55), (0, 0.05, -1.68), (0, -0.12, -1.6), (0, -0.14, -1.45)]
    pb.add("tyre", lambda bm: tube(bm, hook, 0.055, 10))
    return pb.to_object("TowShip", coll)


# ------------------------------------------------------------------------------------------------- in space

ROCKS = [("rock", "rock_dark", "volcano_dark"), ("dirt_dark", "wood_dark", "volcano_dark"), ("volcano", "volcano_dark", "night")]


def rock(coll, i):
    """A lumpy asteroid of radius ~1 with craters: lighter faces up, darker below, the craters darker still."""
    rng = random.Random(40 + i)
    top, bottom, crater = ROCKS[i]
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=3, radius=1.0)
    off = Vector((rng.uniform(0, 50), rng.uniform(0, 50), rng.uniform(0, 50)))
    squash = Vector((rng.uniform(0.85, 1.1), rng.uniform(0.85, 1.05), rng.uniform(0.75, 0.95)))
    craters = [Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))).normalized() for _ in range(5)]
    sizes = [rng.uniform(0.25, 0.42) for _ in craters]
    for v in bm.verts:
        d = v.co.normalized()
        r = 1.0 + 0.16 * noise(d * 1.6 + off) + 0.07 * noise(d * 3.4 + off * 2)
        for c, s in zip(craters, sizes):
            a = d.angle(c)
            if a < s:
                k = a / s
                r -= 0.11 * (1 - k * k) - (0.05 if 0.8 < k < 1.0 else 0.0)
        v.co = Vector((d.x * r * squash.x, d.y * r * squash.y, d.z * r * squash.z))
    bm.normal_update()
    obj = ta.mesh_object(f"Rock{i}", bm, coll)
    names = []
    for poly in obj.data.polygons:
        d = poly.center.normalized()
        in_crater = any(d.angle(c) < s * 0.85 for c, s in zip(craters, sizes))
        names.append(crater if in_crater else (top if poly.normal.z > -0.2 else bottom))
    ta.paint_faces(obj, names)
    obj.data.materials.append(ta.palette_material())
    return obj


def star(coll):
    pb = ta.PartBuilder()
    pb.add(lambda f: "sun" if f.normal.z > -0.3 else "gold", lambda bm: puffy_star(bm, (0, 0, 0), 0.5, 0.23, 0.17))
    return pb.to_object("Star", coll)


def satellite(coll):
    pb = ta.PartBuilder()
    pb.add("gold", lambda bm: box(bm, (0, 0, 0), (0.9, 0.9, 1.0)))
    pb.add("metal", lambda bm: cyl(bm, (0, 0, 0.5), (0, 0, 0.7), 0.3, 0.2, 12))
    for s in (-1, 1):
        pb.add("metal", lambda bm, s=s: cyl(bm, (s * 0.45, 0, 0), (s * 0.9, 0, 0), 0.05, 0.05, 8))
        for k in range(3):
            pb.add("blue", lambda bm, s=s, k=k: box(bm, (s * (1.15 + 0.62 * k), 0, 0), (0.58, 0.04, 0.95)))
            pb.add("metal", lambda bm, s=s, k=k: box(bm, (s * (1.15 + 0.62 * k), 0, 0), (0.6, 0.03, 0.04)))
    # the dish and its antenna
    pb.add("white", lambda bm: ta.cone(bm, (0, -0.5, 0.3), (0, -0.95, 0.65), 0.55, segments=16))
    pb.add("coral", lambda bm: ta.ellipsoid(bm, (0, -0.95, 0.65), (0.07, 0.07, 0.07), segments=10, rings=6))
    return pb.to_object("Satellite", coll)


# ------------------------------------------------------------------------------------------------- on the planets

def pad(coll):
    """The landing pad: a round platform with a coral rim and lamps round it (the game draws the target on its top)."""
    pb = ta.PartBuilder()
    pb.add(by_height("white", "metal", 0.2), lambda bm: cyl(bm, (0, 0, 0), (0, 0, 0.25), 3.0, 3.0, 40))
    pb.add("coral", lambda bm: cyl(bm, (0, 0, 0.0), (0, 0, 0.18), 3.18, 3.18, 40))
    for k in range(12):
        a = 2 * math.pi * k / 12
        pb.add("sun", lambda bm, a=a: ta.ellipsoid(bm, (math.cos(a) * 3.08, math.sin(a) * 3.08, 0.24), (0.11, 0.11, 0.09), segments=10, rings=6))
    return pb.to_object("Pad", coll)


def flag(coll):
    pb = ta.PartBuilder()
    pb.add("metal", lambda bm: cyl(bm, (0, 0, 0), (0, 0, 2.6), 0.045, 0.04, 8))
    pb.add("sun", lambda bm: ta.ellipsoid(bm, (0, 0, 2.66), (0.08, 0.08, 0.08), segments=10, rings=6))
    # a waving flag (a bent strip), a star on it
    pb.add("coral", lambda bm: wave_flag(bm, (0.04, 0, 2.5), 1.1, 0.7))
    pb.add("sun", lambda bm: puffy_star(bm, (0.55, -0.06, 2.15), 0.2, 0.09, 0.03))
    return pb.to_object("Flag", coll)


def wave_flag(bm, top_left, width, height, n=8, t=0.03):
    """A waving flag: a thin bent slab (closed, so it shows from both sides)."""
    x0, y0, z0 = top_left
    grid = {}
    for i in range(n + 1):
        x = x0 + width * i / n
        y = y0 + 0.08 * math.sin(i / n * math.pi * 1.5)
        for j, z in enumerate((z0, z0 - height)):
            for k, dy in enumerate((-t / 2, t / 2)):
                grid[(i, j, k)] = bm.verts.new((x, y + dy, z))
    for i in range(n):
        for k in range(2):
            bm.faces.new((grid[(i, 0, k)], grid[(i + 1, 0, k)], grid[(i + 1, 1, k)], grid[(i, 1, k)]))
        for j in range(2):
            bm.faces.new((grid[(i, j, 0)], grid[(i, j, 1)], grid[(i + 1, j, 1)], grid[(i + 1, j, 0)]))
    for i in (0, n):
        bm.faces.new((grid[(i, 0, 0)], grid[(i, 1, 0)], grid[(i, 1, 1)], grid[(i, 0, 1)]))


def lollipop(coll, seed=1):
    pb = ta.PartBuilder()
    pb.add("white", lambda bm: cyl(bm, (0, 0, 0), (0, 0, 1.6), 0.06, 0.06, 8))
    # a swirl: alternating wedges of a thick disc
    for k in range(10):
        a0, a1 = 2 * math.pi * k / 10, 2 * math.pi * (k + 1) / 10
        colour = "pink" if k % 2 else "white"
        pb.add(colour, lambda bm, a0=a0, a1=a1: wedge(bm, (0, 0, 2.15), 0.62, a0, a1, 0.18))
    pb.add("coral", lambda bm: ta.ellipsoid(bm, (0, 0, 2.15), (0.16, 0.12, 0.16), segments=12, rings=8))
    return pb.to_object("Lollipop", coll)


def wedge(bm, centre, r, a0, a1, depth, n=4):
    """A pie wedge of a disc standing up (in the XZ plane), depth along Y: a closed prism."""
    c = Vector(centre)
    pts = [c] + [c + Vector((math.cos(a0 + (a1 - a0) * k / n) * r, 0, math.sin(a0 + (a1 - a0) * k / n) * r)) for k in range(n + 1)]
    front = [bm.verts.new(p + Vector((0, -depth / 2, 0))) for p in pts]
    back = [bm.verts.new(p + Vector((0, depth / 2, 0))) for p in pts]
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    m = len(pts)
    for i in range(m):
        j = (i + 1) % m
        bm.faces.new((front[i], back[i], back[j], front[j]))


def candy_cane(coll):
    pb = ta.PartBuilder()
    pts = [Vector((0, 0, 0))] + [Vector((0, 0, 0.3 * k)) for k in range(1, 7)]
    for k in range(1, 8):
        a = math.pi * k / 7
        pts.append(Vector((0.35 - math.cos(a) * 0.35, 0, 1.8 + math.sin(a) * 0.35)))
    for i in range(len(pts) - 1):
        pb.add("red" if i % 2 else "white", lambda bm, a=pts[i], b=pts[i + 1]: cyl(bm, a, b, 0.11, 0.11, 10))
        pb.add("red" if i % 2 else "white", lambda bm, a=pts[i + 1]: ta.ellipsoid(bm, a, (0.11, 0.11, 0.11), segments=10, rings=6))
    return pb.to_object("CandyCane", coll)


def crystals(coll, name, light, dark, seed):
    """A cluster of six-sided crystals with pointed tips, leaning out from the middle."""
    rng = random.Random(seed)
    pb = ta.PartBuilder()
    for k in range(6):
        h = rng.uniform(0.9, 2.2) if k else 2.6
        r = rng.uniform(0.18, 0.3) if k else 0.36
        a = rng.uniform(0, 2 * math.pi)
        lean = Vector((math.cos(a), math.sin(a), 0)) * (rng.uniform(0.25, 0.6) if k else 0.0)
        base = Vector((math.cos(a) * 0.3 * (k > 0), math.sin(a) * 0.3 * (k > 0), 0))
        axis = (Vector((0, 0, 1)) + lean).normalized()
        top = base + axis * h
        pb.add(lambda f, light=light, dark=dark: light if f.normal.dot(Vector((-0.4, -0.6, 0.7)).normalized()) > 0.2 else dark,
               lambda bm, base=base, top=top, r=r: cyl(bm, base, top, r, r, 6))
        pb.add(lambda f: "white" if f.normal.z > 0.6 else light, lambda bm, top=top, axis=axis, r=r: ta.cone(bm, top, top + axis * r * 1.6, r, segments=6))
    return pb.to_object(name, coll)


# ------------------------------------------------------------------------------------------------- all

PROPS = [
    *[lambda c, i=i: alien(c, i) for i in range(4)], *[lambda c, i=i: alien_arm(c, i) for i in range(4)],
    lambda c: tow_ship(c), *[lambda c, i=i: rock(c, i) for i in range(3)], lambda c: star(c), lambda c: satellite(c),
    lambda c: pad(c), lambda c: flag(c), lambda c: lollipop(c), lambda c: candy_cane(c),
    lambda c: crystals(c, "IceCrystal", "ice", "ice_dark", 3), lambda c: crystals(c, "GoldCrystal", "sun", "gold", 5),
]


def build():
    ta.clear_scene()
    # the palette with this game's colours first (a palette image loaded before has the old cells)
    ta.write_palette_png()
    old = bpy.data.images.get(ta.PALETTE_NAME)
    if old is not None:
        bpy.data.images.remove(old)
    coll = ta.collection("SpaceProps")
    for o in list(coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    made = []
    for make in PROPS:
        o = make(coll)
        # outward normals (the shapes' windings vary): the game culls back faces
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(o.data)
        bm.free()
        smooth = o.name.startswith(("Alien", "TowShip", "Lollipop", "CandyCane"))
        ta.finish(o, smooth=smooth)
        made.append(o)
    return {o.name: ta.tri_count(o) for o in made}


def objects():
    return [o for o in bpy.data.collections["SpaceProps"].objects if o.type == "MESH"]


def preview():
    """Three line-ups: the aliens, the tow ship with the things in space, the planet dressing."""
    out_dir = os.path.join(HERE, "previews")
    os.makedirs(out_dir, exist_ok=True)
    objs = {o.name: o for o in objects()}
    groups = [
        ("props_cast", ["Alien0", "Alien1", "Alien2", "Alien3"], 1.1, 0.0),
        ("props_space", ["TowShip", "Rock0", "Rock1", "Rock2", "Star", "Satellite"], 1.0, 1.4),
        ("props_planets", ["Pad", "Flag", "Lollipop", "CandyCane", "IceCrystal", "GoldCrystal"], 1.0, 0.0),
    ]
    paths = []
    for name, names, gap, lift in groups:
        for o in objs.values():
            o.hide_render = True
        x = 0.0
        for n in names:
            o = objs[n]
            xs = [v.co.x for v in o.data.vertices]
            width = max(xs) - min(xs)
            o.location = (x - min(xs), 0, lift)
            o.hide_render = False
            x += width + gap
            if n.startswith("Alien"):
                arm = objs[n.replace("Alien", "AlienArm")]
                arm.location = o.location + Vector((0.36, -0.05, 0.5))
                arm.hide_render = False
        dist = max(4.0, x * 1.15)
        ta.preview_rig(target=(x / 2, 0, 0.9 + lift * 0.5), distance=dist, yaw_deg=-15, pitch_deg=10, lens=50)
        bg = bpy.context.scene.world.node_tree.nodes.get("Background")
        bg.inputs["Color"].default_value = (*[ta.srgb_to_linear(c) for c in ta.hex_rgb("#2A2F66")], 1)
        paths.append(ta.render_preview(os.path.join(out_dir, f"{name}.png"), res=(2000, 1000)))
    for o in objs.values():
        o.location = (0, 0, 0)
        o.hide_render = False
    return paths


def export():
    os.makedirs(UNITY_ART, exist_ok=True)
    path = os.path.join(UNITY_ART, "SpaceProps.fbx")
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects():
        o.location = (0, 0, 0)
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects()[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, bake_anim=False, mesh_smooth_type="FACE",
        use_mesh_modifiers=True, path_mode="STRIP")
    # the palette next to the art (the shared one, this game's colours in its spare cells)
    ta.write_palette_png()
    import shutil
    shutil.copyfile(ta.PALETTE_PNG, os.path.join(UNITY_ART, "tundu_palette.png"))
    return path
