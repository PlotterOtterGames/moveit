"""Gara!'s scenery (run inside Blender 5.1 by build.py --props): low-poly props in Tundu's palette (one 64 x 64 palette
texture, one material for the whole world, like Vrapo!'s dino world), exported together into
Assets/Games/Kart/Art/KartProps.fbx (one mesh object per prop, each with its pivot on the ground at its middle).

    the coast      PalmTree, Umbrella, Lighthouse, HouseWhite (Mediterranean: white walls, terracotta roofs, blue shutters)
    the village    Cypress, OliveTree, HouseStone, CastleTower, CastleWall, Fence
    anywhere       RoundTree, Pine, Bush, Flowers, RockA, RockB, TyreStack, Cone, Pennant, Grandstand
    the race       Gantry (start and finish, spanning the road: pillars at +-9.3 m), GantryLights (its five lamps, coloured
                   by the game for the countdown), GiftBox (an item box, the game spins it)

The dino valley reuses Vrapo!'s own props (ferns, cycads, rocks, the volcano, the dino, pterodactyls).
"""

import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "tools"))
sys.path.insert(0, os.path.join(HERE, "..", "runner"))
import tundu_art as ta  # noqa: E402
import world  # noqa: E402   (Vrapo!'s rocks and fronds)

UNITY_ART = os.path.join(ta.REPO, "unity", "MoveItGames", "Assets", "Games", "Kart", "Art")

# ------------------------------------------------------------------------------------------------- shapes

def cyl(bm, a, b, r0, r1=None, segs=10):
    a, b = Vector(a), Vector(b)
    d = b - a
    m = Matrix.Translation((a + b) / 2) @ d.to_track_quat("Z", "Y").to_matrix().to_4x4()
    return bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r0,
                                 radius2=r0 if r1 is None else r1, depth=d.length, matrix=m)


def box(bm, centre, size, rot_z=0.0):
    m = Matrix.Translation(centre) @ Matrix.Rotation(rot_z, 4, "Z") @ Matrix.Diagonal((*size, 1))
    return bmesh.ops.create_cube(bm, size=1.0, matrix=m)


def torus(bm, centre, big, small, segs=18, rsegs=8):
    """A ring lying flat (round Z)."""
    c = Vector(centre)
    grid = []
    for i in range(segs):
        a = 2 * math.pi * i / segs
        row = []
        for j in range(rsegs):
            b = 2 * math.pi * j / rsegs
            rr = big + small * math.cos(b)
            row.append(bm.verts.new(c + Vector((rr * math.cos(a), rr * math.sin(a), small * math.sin(b)))))
        grid.append(row)
    for i in range(segs):
        for j in range(rsegs):
            i1, j1 = (i + 1) % segs, (j + 1) % rsegs
            bm.faces.new((grid[i][j], grid[i1][j], grid[i1][j1], grid[i][j1]))


def gable(bm, centre, width, depth, height, overhang=0.35):
    """A gable roof: the ridge along X (width), eaves overhanging."""
    c = Vector(centre)
    w, d = width / 2 + overhang, depth / 2 + overhang
    v = [bm.verts.new(c + Vector(p)) for p in ((-w, -d, 0), (w, -d, 0), (w, d, 0), (-w, d, 0), (-w, 0, height), (w, 0, height))]
    for f in ((v[0], v[1], v[5], v[4]), (v[2], v[3], v[4], v[5]), (v[1], v[2], v[5]), (v[3], v[0], v[4]), (v[3], v[2], v[1], v[0])):
        bm.faces.new(f)


def blob(bm, centre, r, rng, squash=0.85, segs=10, rings=7):
    """A soft lump (a tree crown's part)."""
    k = rng.uniform(0.9, 1.1)
    return ta.ellipsoid(bm, centre, (r * k, r * k * rng.uniform(0.9, 1.05), r * squash), segments=segs, rings=rings)


def up_light(colour_a, colour_b):
    """Faces facing up lighter: a crown's top catches the sun."""
    return lambda f: colour_a if f.normal.z > 0.35 else colour_b


# ------------------------------------------------------------------------------------------------- trees and plants

def palm(coll, seed=1, height=6.5):
    rng = random.Random(seed)
    pb = ta.PartBuilder()
    lean = Vector((rng.uniform(0.6, 1.2), rng.uniform(-0.3, 0.3), 0))
    pts = [Vector((0, 0, height * t)) + lean * t * t for t in [i / 9 for i in range(10)]]
    for i in range(9):
        pb.add("wood" if i % 2 else "wood_dark", lambda bm, a=pts[i], b=pts[i + 1], i=i: cyl(bm, a, b, 0.22 - 0.012 * i, 0.2 - 0.012 * i, 8))
    crown = pts[-1]
    for i in range(9):
        a = i * 40 + rng.uniform(-10, 10)
        pb.add("leaf" if i % 2 else "fern", lambda bm, a=a: world.frond(bm, crown, a, rng.uniform(2.2, 2.7), 0.55, 0.36, droop=1.1, segments=7))
    for i in range(3):
        a = i * 2.1
        pb.add("wood_dark", lambda bm, a=a: ta.ellipsoid(bm, crown + Vector((math.cos(a) * 0.22, math.sin(a) * 0.22, -0.25)), (0.15, 0.15, 0.15), segments=8, rings=6))
    return pb.to_object(f"PalmTree{seed}" if seed > 1 else "PalmTree", coll)


def cypress(coll, seed=1, height=7.5):
    rng = random.Random(seed)
    pb = ta.PartBuilder()
    pb.add("wood_dark", lambda bm: cyl(bm, (0, 0, 0), (0, 0, 0.9), 0.16, 0.14, 8))
    # a slim flame: stacked lumps, widest a third of the way up
    for i in range(7):
        t = i / 6
        z = 0.7 + (height - 1.2) * t
        r = 0.95 * math.sin(math.pi * (0.25 + 0.75 * t)) * (1 - 0.55 * t) + 0.18
        pb.add(up_light("pine", "cypress"), lambda bm, z=z, r=r: blob(bm, (rng.uniform(-0.05, 0.05), rng.uniform(-0.05, 0.05), z), r, rng, squash=1.4))
    return pb.to_object("Cypress", coll)


def olive(coll, seed=1):
    rng = random.Random(seed)
    pb = ta.PartBuilder()
    # a twisted trunk forking in two
    base, fork = Vector((0, 0, 0)), Vector((0.2, 0.1, 1.4))
    pb.add("wood_dark", lambda bm: cyl(bm, base, fork, 0.3, 0.22, 9))
    tops = [fork + Vector((0.8, 0.3, 1.0)), fork + Vector((-0.7, -0.2, 1.1))]
    for t in tops:
        pb.add("wood", lambda bm, t=t: cyl(bm, fork, t, 0.2, 0.12, 8))
    for i in range(7):
        c = Vector((rng.uniform(-1.4, 1.4), rng.uniform(-1.1, 1.1), rng.uniform(2.4, 3.3)))
        pb.add(up_light("olive", "olive_dark"), lambda bm, c=c: blob(bm, c, rng.uniform(0.9, 1.25), rng, squash=0.7))
    return pb.to_object("OliveTree", coll)


def round_tree(coll, seed=1, height=5.5):
    rng = random.Random(seed)
    pb = ta.PartBuilder()
    pb.add("wood", lambda bm: cyl(bm, (0, 0, 0), (0, 0, height * 0.5), 0.25, 0.18, 9))
    for i in range(5):
        a = i * 1.3
        c = Vector((math.cos(a) * 0.8, math.sin(a) * 0.8, height * 0.62 + rng.uniform(0, 0.8))) if i else Vector((0, 0, height * 0.8))
        pb.add(up_light("leaf", "leaf_dark"), lambda bm, c=c: blob(bm, c, rng.uniform(1.1, 1.5), rng))
    return pb.to_object("RoundTree", coll)


def pine(coll, seed=1, height=7.0):
    rng = random.Random(seed)
    pb = ta.PartBuilder()
    pb.add("wood_dark", lambda bm: cyl(bm, (0, 0, 0), (0, 0, 1.2), 0.22, 0.18, 8))
    for i in range(4):
        z = 1.0 + i * (height - 1.6) / 4
        r = 2.0 * (1 - i / 4.6)
        pb.add(up_light("pine", "cypress"), lambda bm, z=z, r=r: ta.cone(bm, (0, 0, z), (0, 0, z + r * 1.25), r, segments=10))
    return pb.to_object("Pine", coll)


def bush(coll, seed=1):
    rng = random.Random(seed)
    pb = ta.PartBuilder()
    for i in range(4):
        a = i * 1.7
        c = Vector((math.cos(a) * 0.45, math.sin(a) * 0.35, 0.45 + rng.uniform(0, 0.2))) if i else Vector((0, 0, 0.6))
        pb.add(up_light("leaf", "moss"), lambda bm, c=c: blob(bm, c, rng.uniform(0.5, 0.7), rng, squash=0.8))
    return pb.to_object("Bush", coll)


def flowers(coll, seed=1):
    rng = random.Random(seed)
    pb = ta.PartBuilder()
    colours = ["coral", "sun", "grape", "white", "pink", "sky"]
    for i in range(12):
        x, y = rng.uniform(-0.9, 0.9), rng.uniform(-0.6, 0.6)
        h = rng.uniform(0.3, 0.55)
        pb.add("leaf", lambda bm, x=x, y=y, h=h: cyl(bm, (x, y, 0), (x, y, h), 0.025, 0.02, 5))
        col = colours[i % len(colours)]
        pb.add(col, lambda bm, x=x, y=y, h=h: ta.ellipsoid(bm, (x, y, h + 0.06), (0.11, 0.11, 0.07), segments=8, rings=5))
        pb.add("sun" if col != "sun" else "white", lambda bm, x=x, y=y, h=h: ta.ellipsoid(bm, (x, y, h + 0.12), (0.04, 0.04, 0.03), segments=6, rings=4))
    for i in range(3):
        pb.add("leaf_dark", lambda bm, i=i: blob(bm, (rng.uniform(-0.6, 0.6), rng.uniform(-0.4, 0.4), 0.12), 0.32, rng, squash=0.5))
    return pb.to_object("Flowers", coll)


def rocks(coll, seed, size):
    rng = random.Random(seed)
    pb = ta.PartBuilder()
    pb.add(world.rock_color, lambda bm: world.rock(bm, (0, 0, size * 0.5), (size, size * 0.85, size * 0.65), rng))
    pb.add(world.rock_color, lambda bm: world.rock(bm, (size * 0.9, 0.3, size * 0.25), (size * 0.45,) * 3, rng))
    return pb.to_object("RockA" if seed == 1 else "RockB", coll)


# ------------------------------------------------------------------------------------------------- buildings

def house(coll, name, walls, roof, width=6.0, depth=5.0, height=3.6, shutters="blue", seed=1):
    rng = random.Random(seed)
    pb = ta.PartBuilder()
    pb.add(walls, lambda bm: box(bm, (0, 0, height / 2), (width, depth, height)))
    pb.add(up_light(roof, "roof_dark"), lambda bm: gable(bm, (0, 0, height), width, depth, height * 0.45))
    pb.add("wood_dark", lambda bm: box(bm, (0.8, -depth / 2 - 0.03, 1.05), (1.0, 0.12, 2.1)))           # the door
    for x in (-1.6, 2.3):
        if abs(x - 0.8) < 1.2:
            continue
        pb.add("night", lambda bm, x=x: box(bm, (x, -depth / 2 - 0.02, 2.0), (0.8, 0.1, 1.0)))       # a window
        for sx in (-0.62, 0.62):
            pb.add(shutters, lambda bm, x=x, sx=sx: box(bm, (x + sx, -depth / 2 - 0.05, 2.0), (0.42, 0.08, 1.1)))
    for y in (-0.9, 1.1):
        pb.add("night", lambda bm, y=y: box(bm, (width / 2 + 0.02, y, 2.0), (0.1, 0.8, 1.0)))
        for sy in (-0.62, 0.62):
            pb.add(shutters, lambda bm, y=y, sy=sy: box(bm, (width / 2 + 0.05, y + sy, 2.0), (0.08, 0.42, 1.1)))
    pb.add(walls, lambda bm: box(bm, (-width * 0.28, depth * 0.15, height + height * 0.42), (0.6, 0.6, 1.2)))   # the chimney
    pb.add("pink" if seed % 2 else "coral", lambda bm: blob(bm, (-width / 2 - 0.1, -depth / 2 + 0.6, 0.4), 0.55, rng, squash=0.8))  # a flowering bush
    return pb.to_object(name, coll)


def lighthouse(coll):
    pb = ta.PartBuilder()
    h = 15.0
    for i in range(6):
        z0, z1 = h * i / 6, h * (i + 1) / 6
        pb.add("red" if i % 2 else "white", lambda bm, z0=z0, z1=z1: cyl(bm, (0, 0, z0), (0, 0, z1), 2.2 - 0.12 * z0 / h * 6, 2.2 - 0.12 * z1 / h * 6, 14))
    pb.add("night", lambda bm: cyl(bm, (0, 0, h), (0, 0, h + 0.35), 2.0, 2.0, 14))                      # the gallery
    pb.add("sun", lambda bm: cyl(bm, (0, 0, h + 0.35), (0, 0, h + 2.0), 1.05, 1.05, 12))                 # the lamp
    pb.add("red", lambda bm: ta.cone(bm, (0, 0, h + 2.0), (0, 0, h + 3.3), 1.35, segments=12))           # the roof
    pb.add("wood_dark", lambda bm: box(bm, (0, -1.6, 1.1), (1.0, 0.9, 2.2)))                             # the door
    pb.add("stone", lambda bm: cyl(bm, (0, 0, -0.3), (0, 0, 0.4), 3.3, 3.2, 14))                          # the base
    return pb.to_object("Lighthouse", coll)


def castle_tower(coll):
    pb = ta.PartBuilder()
    h = 11.0
    pb.add(lambda f: "stone_dark" if (int(f.calc_center_median().z / 1.2) % 2) else "stone",
           lambda bm: cyl(bm, (0, 0, 0), (0, 0, h), 3.3, 3.0, 16))
    for i in range(10):
        a = 2 * math.pi * i / 10
        pb.add("stone", lambda bm, a=a: box(bm, (math.cos(a) * 2.85, math.sin(a) * 2.85, h + 0.55), (1.0, 1.0, 1.1), a))
    pb.add("night", lambda bm: box(bm, (0, -3.0, h * 0.6), (0.7, 0.4, 1.4)))
    pb.add("wood", lambda bm: cyl(bm, (0, 0, h), (0, 0, h + 4.5), 0.08, 0.08, 6))                         # the flag's pole
    pb.add("red", lambda bm: box(bm, (0.75, 0, h + 3.9), (1.4, 0.06, 0.9)))                               # the flag
    pb.add("checker", lambda bm: ta.ellipsoid(bm, (0.2, 0, h + 3.9), (0.25, 0.08, 0.25), segments=8, rings=6))   # its eagle (a dark mark)
    return pb.to_object("CastleTower", coll)


def castle_wall(coll):
    pb = ta.PartBuilder()
    pb.add(lambda f: "stone_dark" if f.normal.z < 0.5 and int(f.calc_center_median().z) % 2 else "stone",
           lambda bm: box(bm, (0, 0, 2.5), (10.0, 1.6, 5.0)))
    for i in range(6):
        pb.add("stone", lambda bm, i=i: box(bm, (-4.2 + i * 1.68, 0, 5.45), (0.95, 1.6, 0.9)))
    return pb.to_object("CastleWall", coll)


def fence(coll):
    pb = ta.PartBuilder()
    for x in (-2.0, 0.0, 2.0):
        pb.add("wood_dark", lambda bm, x=x: box(bm, (x, 0, 0.55), (0.14, 0.14, 1.1)))
    for z in (0.45, 0.85):
        pb.add("wood", lambda bm, z=z: box(bm, (0, 0, z), (4.2, 0.08, 0.14)))
    return pb.to_object("Fence", coll)


# ------------------------------------------------------------------------------------------------- the race

def umbrella(coll):
    pb = ta.PartBuilder()
    pb.add("white", lambda bm: cyl(bm, (0, 0, 0), (0, 0, 2.4), 0.04, 0.04, 6))
    segs = 10
    for i in range(segs):
        a0, a1 = 2 * math.pi * i / segs, 2 * math.pi * (i + 1) / segs

        def wedge(bm, a0=a0, a1=a1):
            for under in (0.0, -0.02):   # the top, and the underside on its own vertices (bmesh wants that)
                tip = bm.verts.new((0, 0, 2.75 + under))
                p0 = bm.verts.new((math.cos(a0) * 1.4, math.sin(a0) * 1.4, 2.15 + under))
                p1 = bm.verts.new((math.cos(a1) * 1.4, math.sin(a1) * 1.4, 2.15 + under))
                bm.faces.new((tip, p0, p1) if under == 0.0 else (tip, p1, p0))
        pb.add("coral" if i % 2 else "white", wedge)
    return pb.to_object("Umbrella", coll)


def tyre_stack(coll):
    pb = ta.PartBuilder()
    for i in range(3):
        pb.add("white" if i == 1 else "tyre", lambda bm, i=i: torus(bm, (0, 0, 0.16 + i * 0.3), 0.36, 0.15))
    return pb.to_object("TyreStack", coll)


def cone(coll):
    pb = ta.PartBuilder()
    pb.add("lava", lambda bm: box(bm, (0, 0, 0.03), (0.5, 0.5, 0.06)))
    pb.add(lambda f: "white" if 0.25 < f.calc_center_median().z < 0.38 else "lava", lambda bm: ta.cone(bm, (0, 0, 0.06), (0, 0, 0.7), 0.2, segments=10))
    return pb.to_object("Cone", coll)


def pennant(coll, colour="coral"):
    pb = ta.PartBuilder()
    pb.add("white", lambda bm: cyl(bm, (0, 0, 0), (0, 0, 4.2), 0.05, 0.05, 6))

    def flag(bm):
        v = [bm.verts.new(p) for p in ((0, 0, 4.15), (0, 0, 3.35), (1.4, 0, 3.75))]
        bm.faces.new(v)
        bm.faces.new(list(reversed([bm.verts.new(p) for p in ((0, 0.01, 4.15), (0, 0.01, 3.35), (1.4, 0.01, 3.75))])))
    pb.add(colour, flag)
    return pb.to_object("Pennant", coll)


GANTRY_X = 9.3


def gantry(coll):
    """The start and finish: two pillars beside the road, a beam over it with a checkered band, the logo panel."""
    pb = ta.PartBuilder()
    for x in (-GANTRY_X, GANTRY_X):
        pb.add("white", lambda bm, x=x: box(bm, (x, 0, 3.0), (0.8, 0.8, 6.0)))
        pb.add("red", lambda bm, x=x: box(bm, (x, 0, 0.4), (1.1, 1.1, 0.8)))
    pb.add("white", lambda bm: box(bm, (0, 0, 6.2), (2 * GANTRY_X + 0.8, 0.9, 1.4)))
    cells = 24
    w = 2 * GANTRY_X / cells
    for i in range(cells):
        for row in range(2):
            pb.add("checker" if (i + row) % 2 else "white",
                   lambda bm, i=i, row=row: box(bm, (-GANTRY_X + (i + 0.5) * w, -0.47, 5.75 + row * 0.42), (w, 0.06, 0.42)))
    # the lamps' housing (the lamps are GantryLights)
    pb.add("checker", lambda bm: box(bm, (0, -0.55, 6.95), (5.6, 0.4, 0.9)))
    return pb.to_object("Gantry", coll)


def gantry_lights(coll):
    """The five countdown lamps on the gantry (the game colours them: off, red, then green)."""
    pb = ta.PartBuilder()
    for i in range(5):
        x = -2.2 + i * 1.1
        pb.add("white", lambda bm, x=x: ta.ellipsoid(bm, (x, -0.78, 6.95), (0.36, 0.12, 0.36), segments=14, rings=8))
    return pb.to_object("GantryLights", coll)


def gift_box(coll):
    """A gift box: a cube with a ribbon round it and a bow (pivot in its middle: the game floats and spins it)."""
    pb = ta.PartBuilder()
    s = 0.95
    pb.add("sky", lambda bm: box(bm, (0, 0, 0), (s, s, s)))
    pb.add("sun", lambda bm: box(bm, (0, 0, 0), (s * 1.02, 0.2, s * 1.02)))
    pb.add("sun", lambda bm: box(bm, (0, 0, 0), (0.2, s * 1.02, s * 1.02)))
    for a in (45, -45, 135, -135):
        pb.add("sun", lambda bm, a=a: ta.ellipsoid(bm, (math.cos(math.radians(a)) * 0.17, math.sin(math.radians(a)) * 0.17, s / 2 + 0.1),
                                                    (0.2, 0.08, 0.1), rot_deg=(0, 0, a), segments=10, rings=6))
    return pb.to_object("GiftBox", coll)


def grandstand(coll, length=24.0):
    """A stand for the crowd by the start: four steps (the game seats the crowd on them), a striped roof on poles."""
    pb = ta.PartBuilder()
    for i in range(4):
        pb.add(up_light("white", "stone"), lambda bm, i=i: box(bm, (0, i * 0.9 + 0.45, 0.35 + i * 0.6), (length, 0.9 + (3 - i) * 0.0, 0.7 + i * 1.2)))
    pb.add("stone_dark", lambda bm: box(bm, (0, 1.35, 0.05), (length + 0.4, 3.9, 0.1)))
    for x in (-length / 2 + 0.3, -length / 6, length / 6, length / 2 - 0.3):
        pb.add("white", lambda bm, x=x: box(bm, (x, 3.4, 3.4), (0.25, 0.25, 6.8)))
    segs = 12
    for i in range(segs):
        x0 = -length / 2 - 0.4 + (length + 0.8) * i / segs
        pb.add("coral" if i % 2 else "white", lambda bm, x0=x0: box(bm, (x0 + (length + 0.8) / segs / 2, 1.6, 6.85), ((length + 0.8) / segs, 4.6, 0.18)))
    return pb.to_object("Grandstand", coll)


# ------------------------------------------------------------------------------------------------- all

PROPS = [
    lambda c: palm(c), lambda c: palm(c, seed=2, height=7.5), lambda c: cypress(c), lambda c: olive(c),
    lambda c: round_tree(c), lambda c: pine(c), lambda c: bush(c), lambda c: flowers(c),
    lambda c: rocks(c, 1, 0.9), lambda c: rocks(c, 2, 1.6),
    lambda c: house(c, "HouseWhite", "wall", "terracotta", shutters="blue", seed=1),
    lambda c: house(c, "HouseStone", "stone", "terracotta", width=5.0, depth=4.4, height=3.2, shutters="teal", seed=2),
    lambda c: lighthouse(c), lambda c: castle_tower(c), lambda c: castle_wall(c), lambda c: fence(c),
    lambda c: umbrella(c), lambda c: tyre_stack(c), lambda c: cone(c), lambda c: pennant(c),
    lambda c: gantry(c), lambda c: gantry_lights(c), lambda c: gift_box(c), lambda c: grandstand(c),
]


def build():
    ta.clear_scene()
    # the palette with the new colours first (a palette image loaded before has the old cells)
    ta.write_palette_png()
    old = bpy.data.images.get(ta.PALETTE_NAME)
    if old is not None:
        bpy.data.images.remove(old)
    coll = ta.collection("KartProps")
    for o in list(coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    made = []
    for make in PROPS:
        o = make(coll)
        ta.finish(o, smooth=False)
        made.append(o)
    return {o.name: ta.tri_count(o) for o in made}


def objects():
    return [o for o in bpy.data.collections["KartProps"].objects if o.type == "MESH"]


def preview():
    out_dir = os.path.join(HERE, "previews")
    os.makedirs(out_dir, exist_ok=True)
    objs = objects()
    # in rows, small things in front
    x = 0.0
    big = {"Gantry", "GantryLights", "Grandstand", "Lighthouse", "CastleTower", "CastleWall", "HouseWhite", "HouseStone"}
    xs = {"small": -14.0, "big": -26.0}
    for o in objs:
        if o.name == "GantryLights":
            o.location = bpy.data.objects["Gantry"].location
            continue
        row = "big" if o.name in big else "small"
        width = max(v.co.x for v in o.data.vertices) - min(v.co.x for v in o.data.vertices)
        o.location = (xs[row] + width / 2, 0 if row == "small" else 14, 0)
        xs[row] += width + 1.5
        if o.name == "Gantry":
            bpy.data.objects["GantryLights"].location = o.location
    paths = []
    for name, target, dist, yaw, pitch in (("props_small", (0, 0, 1.5), 34, -15, 14), ("props_big", (12, 14, 5), 62, -10, 12)):
        ta.preview_rig(target=target, distance=dist, yaw_deg=yaw, pitch_deg=pitch, lens=50)
        paths.append(ta.render_preview(os.path.join(out_dir, f"{name}.png"), res=(2000, 1000)))
    for o in objs:
        o.location = (0, 0, 0)
    return paths


def export():
    os.makedirs(UNITY_ART, exist_ok=True)
    path = os.path.join(UNITY_ART, "KartProps.fbx")
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects():
        o.location = (0, 0, 0)
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects()[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, bake_anim=False, mesh_smooth_type="FACE",
        use_mesh_modifiers=True, path_mode="STRIP")
    # the palette next to the art (the same texture as Vrapo!'s: the new colours are in its spare cells)
    ta.write_palette_png()
    import shutil
    shutil.copyfile(ta.PALETTE_PNG, os.path.join(UNITY_ART, "tundu_palette.png"))
    return path
