"""Vrapo!'s valley as code (Blender 5.1, headless; run by build.py --valley): a prehistoric valley in the morning sun,
the look's overhaul of 2026-11-18 (the user: "a complete polish ... ready for production for a console ... so that it
doesn't look like an AI game"). Exported together into Assets/Games/Runner/Art/Valley.fbx (one mesh object each, its
pivot on the ground under it, its front toward the camera: Blender +Y). Sizes are real metres.

The obstacles keep the game's hitboxes (RunnerGame.cs): a lane is 2.2 m (a lane's piece at most 1.98 m wide); jump =
under 0.7 m; duck = nothing in the lanes below 1.15 m (vines may hang to ~1.0 m); dodge = fills its lane, 6 m long.
Families read by shape and colour, like Subway Surfers' barriers: jump things are low and wooden or stony across the way,
duck things are a bar high over it with hanging green, dodge things are tall and fill a lane.

    jump      LogLow (a lane), StoneRow (a lane), LogLong (the whole way across: a fallen tree, roots and crown outside)
    duck      VineBridge (a lane), BrontoNeck (the whole way: a sleeping brontosaurus's neck across the path)
    dodge     Boulder, Trike (a sleeping triceratops)
    eggs      Egg, EggGold
    path side GrassTuft, GrassTall, GrassFlowers, Fern, FernBig, FlowerPatch, FlowerPatchB, Bush, BushBerry, RockSmall,
              RockSmall2, Pebbles, Mushrooms, Horsetail
    beside    TreeFern, TreeFern2, Cycad, Cycad2, Palm, Araucaria, TreeRound, RockBig, RockBig2
    far       ForestFar, ForestFar2, Cliff
    backdrop  Volcano, Mountains, Cloud1, Cloud2, Cloud3
    sights    RockArch, RibArch
"""

import json
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.noise import noise

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "tools"))
import tundu_art as ta  # noqa: E402
import kit  # noqa: E402
from kit import Build, blob, by_height, by_noise, cone, crown, ellipsoid, frond, lobed_disc, mix, rock_shape, rgb, \
    rounded_box, strip_leaf, sway_height, tube  # noqa: E402

UNITY_ART = os.path.join(ta.REPO, "unity", "MoveItGames", "Assets", "Games", "Runner", "Art")

kit.COLOURS.update({
    # wood
    "bark": "#8A5A3C", "bark_dark": "#55352A", "bark_light": "#B98656", "wood_end": "#EBC58E", "wood_ring": "#C99560",
    "fern_trunk": "#6B4636", "fern_trunk_dark": "#41291F",
    # stone
    "rock": "#C4B29E", "rock_dark": "#86766C", "rock_light": "#EADFCD", "rock_warm": "#D8B99A",
    "moss": "#5FA748", "moss_dark": "#3A7C44",
    "sandstone": "#E3A46E", "sandstone_dark": "#B9754E", "sandstone_light": "#F4C995",
    # plants
    "stem": "#3F8E4E", "leaf_dark": "#1F6150", "leaf": "#3E9E5C", "leaf_light": "#9ED86A",
    "pink": "#FF7FA8", "yellow": "#FFD84A", "white": "#FFF6E8", "coral": "#FF6B4E", "lilac": "#B58CFF",
    "orange": "#FF9F43", "centre": "#FFC23C", "berry": "#E8405A", "blue": "#6FB6FF",
    "cap": "#FF6A3D", "cap_spot": "#FFF4E2", "stalk": "#FFF1DC",
    # earth
    "dirt": "#C98C5A", "dirt_dark": "#8E5A3B", "sand": "#EDC48C",
    # eggs
    "shell": "#FFF3DE", "spot_teal": "#2EC4B6", "spot_coral": "#FF6B5A", "gold": "#FFC93C", "gold_light": "#FFF2A8",
    "gold_dark": "#E59A1E",
    # the sleepers
    "trike": "#8E7DEB", "trike_dark": "#6650C8", "trike_belly": "#F6E3C6", "frill": "#FF8A5B", "frill_light": "#FFD08A",
    "horn": "#FFF1D6", "bronto": "#6FAEE8", "bronto_dark": "#4C80C8", "bronto_belly": "#D5EAF8",
    "eye_shut": "#2A2340", "blush": "#FF8F9C",
    # far away
    "volcano": "#6A577A", "volcano_dark": "#43344F", "volcano_top": "#8A7096", "lava": "#FF6A2A", "lava_hot": "#FFC24A",
    "mount_base": "#5C86B8", "mount_mid": "#7FA6D2", "mount_top": "#B9D2EE",
    "cloud": "#FFFFFF", "cloud_under": "#DCD3F2", "bone": "#F4E6CB", "bone_dark": "#CDB894",
})

LEAF_RAMP = [(0.0, "#174F49"), (0.35, "#26804F"), (0.7, "#55AC48"), (1.0, "#AED65A")]
FERN_RAMP = [(0.0, "#16503F"), (0.45, "#2A8452"), (1.0, "#86C85A")]
PALM_RAMP = [(0.0, "#1F6448"), (0.55, "#4E9F48"), (1.0, "#A8D257")]
DARK_RAMP = [(0.0, "#174E4A"), (0.5, "#24745A"), (1.0, "#5DA85A")]
GRASS_RAMP = [(0.0, "#256A4C"), (0.45, "#46994E"), (0.8, "#86C25A"), (1.0, "#BFE16E")]
FAR_RAMP = [(0.0, "#2E6A6A"), (0.6, "#4E9468"), (1.0, "#8CC070")]


def objects():
    coll = bpy.data.collections.get("Valley")
    return [o for o in coll.objects if o.type == "MESH"] if coll else []


# ------------------------------------------------------------------------------------------------- small pieces

def disc(c, axis, r, segs=14, ring=None):
    """A flat round disc facing `axis` (a log's cut end); ring = a list of radii shares for the rings' loops."""
    def make(bm):
        ax = Vector(axis).normalized()
        ref = Vector((0, 0, 1)) if abs(ax.z) < 0.9 else Vector((1, 0, 0))
        u = (ref - ax * ref.dot(ax)).normalized()
        w = ax.cross(u)
        cc = Vector(c)
        shares = ring or (0.33, 0.66, 1.0)
        centre = bm.verts.new(cc)
        prev = None
        for s in shares:
            loop = [bm.verts.new(cc + (u * math.cos(2 * math.pi * k / segs) + w * math.sin(2 * math.pi * k / segs)) * r * s)
                    for k in range(segs)]
            for k in range(segs):
                k2 = (k + 1) % segs
                if prev is None:
                    bm.faces.new((centre, loop[k], loop[k2]))
                else:
                    bm.faces.new((prev[k], loop[k], loop[k2], prev[k2]))
            prev = loop
    return make


def rings_rule(c, axis, r):
    """A cut log's end: pale wood with darker year rings and a dark bark rim."""
    cc, ax = Vector(c), Vector(axis).normalized()

    def rule(p, n):
        d = p - cc
        d -= ax * d.dot(ax)
        s = d.length / r
        if s > 0.93:
            return rgb("bark_dark")
        return mix("wood_end", "wood_ring", 0.5 + 0.5 * math.sin(s * 19.0))
    return rule


def bark_rule(seed=0.0):
    return lambda p, n: mix("bark_dark", "bark", 0.55 + 0.45 * noise(Vector((p.x * 3.0, p.y * 9.0 + seed, p.z * 9.0))))


def grooves(count=9, depth=0.06, twist=2.0):
    return lambda a, t: depth * math.sin(count * a + twist * math.sin(t * 5.0))


def mushroom(B, base, height, r, lean=(0.0, 0.0), cap="cap"):
    b = Vector(base)
    top = b + Vector((lean[0], lean[1], height))
    B.add(tube([b, b.lerp(top, 0.5), top], [r * 0.32, r * 0.26, r * 0.24], segs=8, sub=2, cap=False), "stalk")
    B.add(blob(top + Vector((0, 0, r * 0.12)), (r, r, r * 0.62), seed=int(r * 100), amp=0.04, subdiv=2,
               flat_bottom=top.z + r * 0.02), kit.spotted(cap, "cap_spot", cells=6.0 / r, size=0.18, seed=r))


def grass_clump(B, centre, n, h, spread, seed, ramp_=GRASS_RAMP, width=0.06, lean=0.45):
    rng = random.Random(seed)
    c = Vector(centre)
    for i in range(n):
        a = rng.uniform(0, 2 * math.pi)
        r = spread * math.sqrt(rng.random())
        base = c + Vector((math.cos(a) * r, math.sin(a) * r, 0))
        hh = rng.uniform(*h)
        B.add(kit.blade(base, a + rng.uniform(-0.6, 0.6), hh, width * rng.uniform(0.8, 1.2), lean * rng.uniform(0.5, 1.2),
                        bend=0.6, rows=3),
              by_height(ramp_, c.z, c.z + h[1], seed + i, 0.06), sway=sway_height(c.z, c.z + h[1], 1.3, 0.7), up=0.85)


def flower(B, base, height, colour, radius=0.09, lobes=5, face=(0, 0.35, 1), seed=0):
    b = Vector(base)
    top = b + Vector((0, 0, height))
    sw = sway_height(b.z, b.z + height, 1.5, 0.6)
    B.add(tube([b, top], [radius * 0.13, radius * 0.1], segs=3, sub=1, cap=False), "stem", sway=sw)
    cen = top
    B.add(lobed_disc(top, radius, lobes, normal=face, thick=radius * 0.2, cup=radius * 0.25, pts=lobes * 3),
          lambda p, n, c=cen, r=radius: mix("centre", colour, ((p - c).length / r - 0.15) / 0.3), sway=sw)


# ------------------------------------------------------------------------------------------------- jump: low things

def log_low(coll):
    """A mossy fallen log across one lane (top ~0.64 m): jump it."""
    B = Build("LogLow")
    B.ao = 0.8
    r, x0, x1, z = 0.29, -0.93, 0.93, 0.3
    B.add(tube([(x0, 0, z), (0, 0.02, z + 0.01), (x1, 0, z)], r, segs=14, sub=4, cap=False, ridge=grooves(9, 0.07)),
          bark_rule(1.0))
    for x, ax in ((x0, (-1, 0, 0)), (x1, (1, 0, 0))):
        B.add(disc((x, 0, z), ax, r * 0.98, segs=14, ring=(0.3, 0.6, 0.85, 1.0)), rings_rule((x, 0, z), ax, r))
    # moss along the top and a mushroom family
    for x, s in ((-0.55, 0.22), (0.05, 0.28), (0.6, 0.18)):
        B.add(blob((x, -0.02, z + r * 0.86), (s, 0.17, 0.07), seed=int(x * 10 + 5), amp=0.15, subdiv=2,
                   flat_bottom=z + r * 0.72), by_noise("moss", "moss_dark", 4.0, x), sway=0.0)
    mushroom(B, (0.32, 0.12, z + r * 0.75), 0.11, 0.07, lean=(0.01, 0.03))
    mushroom(B, (0.42, 0.16, z + r * 0.62), 0.07, 0.045, lean=(0.03, 0.03))
    # a snapped branch with a leafy sprig
    B.add(tube([(-0.25, -0.08, z + 0.2), (-0.18, -0.18, z + 0.42), (-0.2, -0.22, z + 0.5)], [0.06, 0.04, 0.035], segs=6),
          "bark")
    for k, a in enumerate((0.3, 1.6, 2.9, 4.3)):
        d = Vector((math.cos(a), math.sin(a), 0.6)).normalized()
        base = Vector((-0.2, -0.22, z + 0.5))
        B.add(strip_leaf([base, base + d * 0.12, base + d * 0.22], [0.01, 0.045, 0.004], rib=0.008, thick=0.006),
              "leaf_light" if k % 2 else "leaf", sway=0.6)
    grass_clump(B, (0.0, 0.32, 0.0), 9, (0.16, 0.3), 0.85, 31)
    grass_clump(B, (0.0, -0.3, 0.0), 6, (0.14, 0.26), 0.8, 32)
    return B.to_object(coll)


def stone_row(coll):
    """A row of mossy stones across one lane (top ~0.55 m), flowers between: jump it."""
    B = Build("StoneRow")
    B.ao = 0.8
    for i, (x, s) in enumerate(((-0.72, 0.27), (-0.24, 0.31), (0.24, 0.29), (0.71, 0.26))):
        h = s * 1.75
        B.add(rock_shape((x, 0.02 * i, h * 0.5), (s * 0.95, s * 0.9, h * 0.5), seed=40 + i, jag=0.1,
                         flat_bottom=0.02),
              lambda p, n: mix("rock_dark", "rock_light", 0.25 + 0.6 * max(0.0, n.z)) if n.z < 0.62 else rgb("moss"))
    for x in (-0.48, 0.0, 0.48):
        flower(B, (x, 0.18, 0.0), 0.32, "pink" if x else "yellow", radius=0.08)
        flower(B, (x + 0.06, -0.12, 0.0), 0.25, "white", radius=0.07)
    grass_clump(B, (0.0, 0.22, 0.0), 10, (0.15, 0.3), 0.9, 41)
    return B.to_object(coll)


def log_long(coll):
    """A fallen tree right across the path (the trunk ~0.64 m high over all three lanes; its roots on the right, its
    broken crown on the left, both outside the path): jump it."""
    B = Build("LogLong")
    B.ao = 1.2
    r, z = 0.31, 0.31
    path = [(-5.6, 0.1, z + 0.12), (-3.0, 0.0, z), (0.0, -0.05, z + 0.01), (3.0, 0.0, z), (4.6, 0.05, z + 0.05)]
    B.add(tube(path, [r * 1.05, r, r, r * 1.02, r * 1.15], segs=16, sub=5, cap=False, ridge=grooves(10, 0.06, 3.0)),
          bark_rule(2.0))
    # the root plate on the right (Blender -X = Unity +X... the camera sees Blender +X on its left)
    root = Vector((4.75, 0.05, 0.62))
    B.add(blob(root + Vector((0.15, 0, 0.1)), (0.35, 1.0, 0.9), seed=7, amp=0.18, subdiv=3, flat_bottom=0.0),
          by_noise("dirt", "dirt_dark", 3.0, 1.0))
    rng = random.Random(5)
    for k in range(9):
        a = 2 * math.pi * k / 9 + rng.uniform(-0.2, 0.2)
        d = Vector((0.0, math.cos(a), math.sin(a)))
        tip = root + Vector((rng.uniform(0.2, 0.7), 0, 0)) + d * rng.uniform(1.0, 1.5)
        tip.z = max(0.05, tip.z)
        B.add(tube([root, root + d * 0.5 + Vector((0.25, 0, 0)), tip], [0.13, 0.08, 0.03], segs=6, sub=2), "bark_dark")
    # the broken top on the left: branches with leaf clumps lying in the grass
    top = Vector((-5.6, 0.1, z + 0.12))
    for k, (dx, dy, dz) in enumerate(((-1.4, 0.8, 0.7), (-1.6, -0.6, 0.5), (-0.9, 0.2, 1.3))):
        tip = top + Vector((dx, dy, dz))
        B.add(tube([top, top.lerp(tip, 0.5) + Vector((0, 0, 0.2)), tip], [0.12, 0.08, 0.05], segs=7, sub=2), "bark")
        balls = [(tip + Vector((0, 0, 0.1)), 0.55), (tip + Vector((-0.4, 0.3, -0.1)), 0.42), (tip + Vector((0.2, -0.35, -0.05)), 0.4)]
        crown(B, balls, tip, tip.z - 0.6, tip.z + 0.6, 30 + k, LEAF_RAMP, subdiv=2, sway=0.25)
    for x, s in ((-3.6, 0.32), (-1.2, 0.36), (1.4, 0.3), (3.4, 0.34)):
        B.add(blob((x, -0.02, z + r * 0.85), (s, 0.2, 0.08), seed=int(x * 10 + 50), amp=0.15, subdiv=2,
                   flat_bottom=z + r * 0.7), by_noise("moss", "moss_dark", 4.0, x))
    mushroom(B, (-2.2, 0.16, z + r * 0.6), 0.12, 0.08, lean=(0.0, 0.04))
    mushroom(B, (-2.05, 0.2, z + r * 0.45), 0.08, 0.05, lean=(0.03, 0.04))
    mushroom(B, (2.4, 0.18, z + r * 0.55), 0.1, 0.07, lean=(0.0, 0.04))
    for x in (-2.8, -0.6, 1.0, 2.9):
        flower(B, (x, 0.3, 0.0), 0.28, "yellow" if x < 0 else "pink", radius=0.08)
    for x in (-3.5, -1.5, 0.5, 2.5):
        grass_clump(B, (x, 0.28, 0.0), 8, (0.16, 0.3), 0.7, int(60 + x * 3))
    return B.to_object(coll)


# ------------------------------------------------------------------------------------------------- duck: high things

def vine_bridge(coll):
    """A log on two stone pillars over one lane (from 1.16 m up), vines hanging from it: duck."""
    B = Build("VineBridge")
    B.ao = 1.0
    for x in (-0.86, 0.86):
        for k, (z, s) in enumerate(((0.28, 0.24), (0.72, 0.21), (1.12, 0.2))):
            B.add(rock_shape((x + 0.02 * k, 0.0, z), (s * 0.95, s * 0.9, 0.24), seed=int(60 + x * 10 + k), jag=0.1),
                  lambda p, n: rgb("moss") if n.z > 0.72 else mix("rock_dark", "rock", 0.35 + 0.6 * max(0.0, n.z)))
    z, r = 1.38, 0.22
    B.add(tube([(-1.02, 0, z), (0, 0.02, z + 0.02), (1.02, 0, z)], r, segs=12, sub=3, cap=False, ridge=grooves(8, 0.07)),
          bark_rule(3.0))
    for x, ax in ((-1.02, (-1, 0, 0)), (1.02, (1, 0, 0))):
        B.add(disc((x, 0, z), ax, r * 0.98, segs=12, ring=(0.3, 0.65, 1.0)), rings_rule((x, 0, z), ax, r))
    B.add(blob((0.1, 0, z + r * 0.85), (0.6, 0.18, 0.08), seed=3, amp=0.15, subdiv=2, flat_bottom=z + r * 0.7),
          by_noise("moss", "moss_dark", 4.0))
    rng = random.Random(9)
    for x in (-0.6, -0.33, -0.05, 0.22, 0.5, 0.72):
        bottom = rng.uniform(1.0, 1.08)
        top = Vector((x, 0.05, z - r * 0.6))
        pts = [top, Vector((x + 0.03, 0.08, (top.z + bottom) / 2)), Vector((x - 0.02, 0.1, bottom))]
        sw = lambda p, zz=top.z, bb=bottom: 0.25 + 0.6 * max(0.0, min(1.0, (zz - p.z) / max(0.01, zz - bb)))
        B.add(tube(pts, [0.018, 0.014], segs=4, sub=2, cap=True), "leaf_dark", sway=sw)
        for k in range(3):
            pz = top.z - (top.z - bottom) * (0.3 + 0.3 * k)
            side = 1 if k % 2 else -1
            base = Vector((x, 0.09, pz))
            d = Vector((0.6 * side, 0.6, -0.3)).normalized()
            B.add(strip_leaf([base, base + d * 0.07, base + d * 0.13], [0.008, 0.04, 0.003], rib=0.006, thick=0.005),
                  "leaf_light" if k % 2 else "leaf", sway=sw)
        if rng.random() < 0.6:
            flower(B, (x, 0.12, bottom + 0.06), 0.0, "pink", radius=0.055, face=(0, 1, 0.2))
    grass_clump(B, (-0.86, 0.2, 0.0), 6, (0.15, 0.32), 0.3, 71)
    grass_clump(B, (0.86, 0.2, 0.0), 6, (0.15, 0.32), 0.3, 72)
    return B.to_object(coll)


def bronto_neck(coll):
    """A sleeping brontosaurus beside the path (on the camera's left), its neck right across the path (from 1.2 m up),
    its chin resting on a mossy rock on the right: duck."""
    B = Build("BrontoNeck")
    B.ao = 2.2
    # Blender +X = the camera's left (Unity -X)
    body = Vector((8.2, 0.6, 1.35))
    B.add(blob(body, (2.0, 3.3, 1.45), seed=11, amp=0.03, subdiv=4, flat_bottom=0.15),
          lambda p, n: mix("bronto_belly", "bronto", 0.35 + 0.65 * max(0.0, min(1.0, (p.z - 0.5) / 1.2))))
    # dark spots along the back
    for k, (dy, dz, s) in enumerate(((-1.8, 2.55, 0.32), (-0.6, 2.75, 0.36), (0.6, 2.75, 0.34), (1.8, 2.5, 0.3),
                                     (-1.2, 2.45, 0.22), (1.2, 2.5, 0.22))):
        B.add(ellipsoid(body + Vector((0.25 * (k % 2) - 0.1, dy, dz - 1.35)), (s, s * 1.2, s * 0.3), segs=10, rings=6),
              "bronto_dark")
    # tucked legs
    for dy, side in ((-2.0, 1), (1.9, 1), (-2.0, -1), (1.9, -1)):
        foot = body + Vector((1.3 * side, dy, -1.05))
        B.add(blob(foot, (0.55, 0.65, 0.42), seed=int(dy * 10 + side * 3 + 20), amp=0.03, subdiv=3, flat_bottom=0.02),
              "bronto")
        for t in (-0.25, 0.0, 0.25):
            B.add(ellipsoid(foot + Vector((-0.38 * side, t + 0.0, -0.18)), (0.08, 0.1, 0.07), segs=8, rings=5), "horn")
    # the tail, curling back toward the camera
    B.add(tube([body + Vector((-0.2, 2.6, -0.4)), body + Vector((-0.6, 4.6, -0.95)), body + Vector((-2.0, 6.2, -1.15)),
                body + Vector((-3.6, 6.6, -1.2))], [0.9, 0.55, 0.28, 0.08], segs=12, sub=4),
          lambda p, n: mix("bronto_belly", "bronto", 0.3 + 0.7 * max(0.0, n.z + 0.4)))
    # the neck: up from the shoulders, a long arch over the path (its underside from ~1.25 m), down to the rock
    neck = [body + Vector((-1.3, -1.6, 0.35)), Vector((5.4, -1.6, 2.15)), Vector((3.4, -1.55, 1.82)), Vector((0.0, -1.5, 1.74)),
            Vector((-3.0, -1.45, 1.7)), Vector((-4.6, -1.45, 1.42)), Vector((-5.5, -1.5, 1.16))]
    B.add(tube(neck, [0.78, 0.5, 0.4, 0.37, 0.36, 0.38, 0.4], segs=14, sub=5, cap=False, squash=((0, 1, 0), 0.92)),
          lambda p, n: mix("bronto_belly", "bronto", 0.25 + 0.75 * max(0.0, min(1.0, n.z * 1.4 + 0.45))))
    # the head on its rock, eyes shut, a little smile, a blush
    head = Vector((-6.0, -1.5, 1.12))
    B.add(blob(head, (0.62, 0.48, 0.42), seed=13, amp=0.02, subdiv=4),
          lambda p, n: mix("bronto_belly", "bronto", 0.3 + 0.7 * max(0.0, min(1.0, n.z + 0.6))))
    B.add(blob(head + Vector((-0.45, 0.0, -0.06)), (0.36, 0.38, 0.3), seed=14, amp=0.02, subdiv=3), "bronto")
    for side in (1, -1):
        eye = head + Vector((-0.12, 0.38 * side, 0.18))
        B.add(tube([eye + Vector((0.1, 0.02 * side, 0.03)), eye + Vector((0.0, 0.05 * side, -0.02)),
                    eye + Vector((-0.1, 0.02 * side, 0.03))], 0.022, segs=5, sub=2), "eye_shut")
        B.add(ellipsoid(head + Vector((-0.42, 0.36 * side, -0.05)), (0.09, 0.03, 0.06), segs=8, rings=5), "blush")
        B.add(ellipsoid(head + Vector((-0.78, 0.12 * side, 0.12)), (0.03, 0.025, 0.02), segs=6, rings=4), "bronto_dark")
    B.add(tube([head + Vector((-0.7, 0.32, -0.18)), head + Vector((-0.82, 0.0, -0.22)), head + Vector((-0.7, -0.32, -0.18))],
               0.018, segs=5, sub=3), "eye_shut")
    # the rock the head rests on, ferns round it
    B.add(rock_shape((-6.0, -1.5, 0.42), (0.95, 0.85, 0.5), seed=81, jag=0.1, flat_bottom=0.02),
          lambda p, n: rgb("moss") if n.z > 0.75 else mix("rock_dark", "rock", 0.4 + 0.5 * max(0.0, n.z)))
    for k in range(7):
        a = 2 * math.pi * k / 7
        B.add(frond((-6.0 + math.cos(a) * 0.7, -1.5 + math.sin(a) * 0.6, 0.05), (math.cos(a), math.sin(a), 0), 0.9, 0.11,
                    rise=0.45, droop=0.9, segs=7, leaflets=1), by_height(FERN_RAMP, 0.0, 0.6, k), sway=0.45)
    grass_clump(B, (6.0, -1.8, 0.0), 12, (0.2, 0.45), 1.6, 91)
    grass_clump(B, (9.5, 1.5, 0.0), 12, (0.2, 0.45), 1.8, 92)
    return B.to_object(coll)


# ------------------------------------------------------------------------------------------------- dodge: tall things

def boulder(coll):
    """A big mossy boulder filling one lane (2.4 m tall, 6 m long): step aside."""
    B = Build("Boulder")
    B.ao = 1.4
    rule = lambda p, n: rgb("moss") if n.z > 0.7 and p.z > 1.2 else mix("rock_dark", "rock_light", 0.2 + 0.7 * max(0.0, min(1.0, n.z * 0.6 + p.z / 3.2)))
    B.add(rock_shape((0, 0.6, 1.2), (0.88, 2.2, 1.2), seed=3, jag=0.08, flat_bottom=0.03), rule)
    B.add(rock_shape((0.05, -1.7, 0.85), (0.86, 1.2, 0.85), seed=4, jag=0.1, flat_bottom=0.03), rule)
    B.add(rock_shape((-0.15, 2.4, 0.55), (0.7, 0.75, 0.55), seed=5, jag=0.12, flat_bottom=0.03), rule)
    B.add(rock_shape((0.2, 0.0, 2.15), (0.55, 0.9, 0.4), seed=6, jag=0.12), rule)
    # moss caps and a fern on top
    B.add(blob((0.0, 0.5, 2.38), (0.45, 1.1, 0.12), seed=7, amp=0.2, subdiv=3, flat_bottom=2.28), by_noise("moss", "moss_dark", 2.5))
    for k in range(6):
        a = 2 * math.pi * k / 6 + 0.3
        B.add(frond((0.0, -0.1, 2.42), (math.cos(a), math.sin(a), 0), 0.75, 0.09, rise=0.5, droop=0.8, segs=6, leaflets=1),
              by_height(FERN_RAMP, 2.3, 2.9, k), sway=0.45)
    # ferns and grass round its foot, a little flower patch in front
    for y in (2.8, -2.6):
        for k in range(5):
            a = math.pi * (0.15 + 0.7 * k / 4) * (1 if y > 0 else -1)
            B.add(frond((0.0, y, 0.03), (math.cos(a), math.sin(a), 0), 0.8, 0.1, rise=0.45, droop=0.9, segs=6, leaflets=1),
                  by_height(FERN_RAMP, 0.0, 0.5, k + 10), sway=0.45)
    for x in (-0.45, 0.4):
        flower(B, (x, 3.05, 0.0), 0.3, "yellow" if x < 0 else "white", radius=0.08)
    grass_clump(B, (0.0, 3.0, 0.0), 10, (0.18, 0.34), 0.8, 51)
    grass_clump(B, (0.6, -0.5, 0.0), 6, (0.18, 0.34), 0.3, 52)
    grass_clump(B, (-0.6, 0.9, 0.0), 6, (0.18, 0.34), 0.3, 53)
    return B.to_object(coll)


def trike(coll):
    """A sleeping triceratops lying in one lane, its face toward the camera (frill ~2.1 m, 5.6 m long, 1.8 m wide):
    step aside (don't wake it)."""
    B = Build("Trike")
    B.ao = 1.4

    def body_rule(p, n):
        stripe = 0.5 + 0.5 * math.sin(p.y * 4.2)
        top = max(0.0, min(1.0, (p.z - 0.45) / 0.9))
        c = mix("trike_belly", "trike", 0.25 + 0.75 * top)
        return mix(c, "trike_dark", 0.55 * top * stripe * max(0.0, n.z))

    body = Vector((0.0, -0.6, 0.86))
    B.add(blob(body, (0.88, 2.0, 0.84), seed=21, amp=0.03, subdiv=4, flat_bottom=0.06), body_rule)
    # legs folded under, toes peeking
    for dy in (0.9, -1.9):
        for side in (1, -1):
            leg = Vector((0.62 * side, -0.6 + dy, 0.32))
            B.add(blob(leg, (0.36, 0.5, 0.32), seed=int(22 + dy * 5 + side), amp=0.03, subdiv=3, flat_bottom=0.02), "trike")
            for t in (-0.12, 0.0, 0.12):
                B.add(ellipsoid(leg + Vector((t + 0.1 * side, 0.45, -0.18)), (0.06, 0.06, 0.05), segs=8, rings=5), "horn")
    # the tail, round to one side
    B.add(tube([body + Vector((0.0, -1.8, -0.2)), body + Vector((0.25, -2.9, -0.55)), body + Vector((0.7, -3.4, -0.7))],
               [0.45, 0.22, 0.06], segs=12, sub=4), body_rule)
    # the head: low and forward, resting on the ground
    head = Vector((0.0, 1.85, 0.7))
    B.add(blob(head, (0.55, 0.62, 0.5), seed=23, amp=0.02, subdiv=4, flat_bottom=0.15), body_rule)
    B.add(blob(head + Vector((0.0, 0.55, -0.12)), (0.36, 0.42, 0.34), seed=24, amp=0.02, subdiv=3, flat_bottom=0.15), "trike")
    # the beak and the nose horn
    B.add(cone(head + Vector((0, 0.92, -0.08)), head + Vector((0, 1.08, -0.2)), 0.16, segs=10), "horn")
    B.add(cone(head + Vector((0, 0.78, 0.18)), head + Vector((0, 0.95, 0.42)), 0.11, segs=10), "horn")
    # the frill: a big scalloped plate standing behind the head, coral rim with light dots
    frill_c = head + Vector((0, -0.35, 0.55))
    B.add(lobed_disc(frill_c, 1.05, 7, normal=(0, 0.75, 0.66), thick=0.12, inner=0.82, pts=42, cup=-0.12),
          lambda p, n: mix("frill", "frill_light", 0.6 + 0.4 * math.sin((p - frill_c).length * 9.0))
          if (p - frill_c).length > 0.78 else mix("trike", "trike_dark", 0.3))
    for k in range(7):
        a = math.pi * (0.05 + 0.9 * k / 6)
        d = Vector((math.cos(a), -0.48 * math.sin(a), 0.75 * math.sin(a)))
        B.add(ellipsoid(frill_c + d * 0.93 + Vector((0, 0.07, 0)), (0.07, 0.04, 0.07), segs=8, rings=5), "frill_light")
    # brow horns, rounded: pointing up and forward
    for side in (1, -1):
        base = head + Vector((0.26 * side, 0.18, 0.38))
        B.add(cone(base, base + Vector((0.08 * side, 0.32, 0.5)), 0.1, segs=10), "horn")
        # shut eyes: little arcs, a blush
        eye = head + Vector((0.4 * side, 0.32, 0.18))
        B.add(tube([eye + Vector((0.0, 0.1, 0.03)), eye + Vector((0.03 * side, 0.0, -0.03)), eye + Vector((0.0, -0.1, 0.03))],
                   0.022, segs=5, sub=2), "eye_shut")
        B.add(ellipsoid(head + Vector((0.42 * side, 0.6, -0.1)), (0.04, 0.1, 0.07), segs=8, rings=5), "blush")
    grass_clump(B, (0.0, 2.6, 0.0), 10, (0.16, 0.3), 0.8, 61)
    grass_clump(B, (0.5, -3.0, 0.0), 6, (0.16, 0.3), 0.5, 62)
    return B.to_object(coll)


# ------------------------------------------------------------------------------------------------- eggs

def egg_shape(c, r=0.17, up=0.24, down=0.19, segs=18, rings=12):
    def make(bm):
        geom = bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=1.0)
        for v in geom["verts"]:
            z = v.co.z
            k = 1 - 0.14 * max(0.0, z)
            v.co = Vector((v.co.x * r * k, v.co.y * r * k, z * (up if z > 0 else down))) + Vector(c)
    return make


def egg(coll):
    """An egg to collect (pivot at its middle: the game floats and spins it): a cream shell with teal and coral spots."""
    B = Build("Egg")
    B.ao = 0.2

    def rule(p, n):
        a = noise(p * 9.0 + Vector((1.3, 2.2, 0.4)))
        b = noise(p * 9.0 + Vector((7.1, 0.3, 5.5)))
        c = rgb("shell")
        if a > 0.42:
            c = mix(c, "spot_teal", (a - 0.42) / 0.05)
        if b > 0.45:
            c = mix(c, "spot_coral", (b - 0.45) / 0.05)
        return c
    B.add(egg_shape((0, 0, 0)), rule, glow=0.12)
    return B.to_object(coll)


def egg_gold(coll):
    """The rare golden egg: gold with pale glints, glowing."""
    B = Build("EggGold")
    B.ao = 0.2

    def rule(p, n):
        a = noise(p * 11.0 + Vector((4.1, 1.2, 3.3)))
        c = mix("gold_dark", "gold", 0.4 + 0.6 * max(0.0, n.z * 0.5 + 0.5))
        return mix(c, "gold_light", (a - 0.38) / 0.06) if a > 0.38 else c
    B.add(egg_shape((0, 0, 0), r=0.19, up=0.27, down=0.21), rule, glow=0.45)
    return B.to_object(coll)


# ------------------------------------------------------------------------------------------------- by the path

def grass_tuft(coll, name="GrassTuft", n=12, h=(0.22, 0.42), spread=0.3, seed=1, flowers=0):
    B = Build(name)
    B.grass = True
    grass_clump(B, (0, 0, 0), n, h, spread, seed)
    rng = random.Random(seed + 7)
    for k in range(flowers):
        a = rng.uniform(0, 2 * math.pi)
        flower(B, (math.cos(a) * spread * 0.6, math.sin(a) * spread * 0.6, 0), rng.uniform(0.3, 0.48),
               ("yellow", "white", "pink", "lilac")[k % 4], radius=rng.uniform(0.06, 0.08))
    return B.to_object(coll)


def fern(coll, name="Fern", size=1.0, seed=2, n=9):
    B = Build(name)
    B.two_sided = True
    B.ao = 0.7 * size
    rng = random.Random(seed)
    for k in range(n):
        a = 2 * math.pi * k / n + rng.uniform(-0.2, 0.2)
        B.add(kit.pinnate((math.cos(a) * 0.06, math.sin(a) * 0.06, 0.05), (math.cos(a), math.sin(a), 0), size * rng.uniform(0.8, 1.05),
                          size * 0.27, rise=rng.uniform(0.45, 0.65), droop=0.9, pairs=9, leaflet_shape=0.55),
              by_height(FERN_RAMP, 0.0, size * 0.6, seed + k * 0.3), sway=lambda p, s=size: 0.15 + 0.6 * min(1.0, (p.xy.length) / s),
              up=0.5)
    # a fiddlehead or two in the middle
    for k in range(2):
        a = rng.uniform(0, 2 * math.pi)
        base = Vector((math.cos(a) * 0.05, math.sin(a) * 0.05, 0))
        tip = base + Vector((0, 0, size * 0.45))
        B.add(tube([base, tip, tip + Vector((math.cos(a) * 0.08, math.sin(a) * 0.08, 0.06)),
                    tip + Vector((math.cos(a) * 0.12, math.sin(a) * 0.12, -0.02))], [0.02, 0.018, 0.03, 0.028], segs=5, sub=2),
              "leaf_light", sway=0.3)
    return B.to_object(coll)


def flower_patch(coll, name="FlowerPatch", colours=("pink", "white", "yellow", "pink", "lilac"), seed=4, n=7):
    B = Build(name)
    B.grass = True
    rng = random.Random(seed)
    grass_clump(B, (0, 0, 0), 8, (0.12, 0.26), 0.35, seed)
    for k in range(n):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(0.05, 0.4)
        flower(B, (math.cos(a) * r, math.sin(a) * r, 0), rng.uniform(0.18, 0.42), colours[k % len(colours)],
               radius=rng.uniform(0.07, 0.11), lobes=5 if k % 3 else 6)
    return B.to_object(coll)


def bush(coll, name="Bush", seed=10, berries=None, size=1.0):
    B = Build(name)
    B.ao = 1.0 * size
    s = size
    balls = [((0, 0, 0.55 * s), (0.75 * s, 0.7 * s, 0.6 * s)), ((0.5 * s, 0.25 * s, 0.42 * s), 0.5 * s),
             ((-0.5 * s, 0.15 * s, 0.45 * s), 0.52 * s), ((0.1 * s, -0.4 * s, 0.5 * s), 0.5 * s),
             ((0.0, 0.1 * s, 0.95 * s), 0.45 * s)]
    crown(B, balls, (0, 0, 0.5 * s), 0.0, 1.3 * s, seed, LEAF_RAMP, subdiv=3, sway=0.12, k=0.75)
    if berries:
        rng = random.Random(seed)
        placed = 0
        while placed < 10:
            c, r = balls[rng.randrange(len(balls))]
            r = r[0] if isinstance(r, tuple) else r
            a, el = rng.uniform(0, 2 * math.pi), rng.uniform(-0.1, 1.0)
            d = Vector((math.cos(a) * math.cos(el), math.sin(a) * math.cos(el), math.sin(el)))
            p = Vector(c) + d * r * 0.98
            if d.y < -0.3:
                continue
            if berries == "flowers":
                flower(B, p, 0.0, ("white", "pink", "yellow")[placed % 3], radius=0.09 * s,
                       face=tuple((d + Vector((0, 0.4, 0.4))).normalized()))
            else:
                B.add(ellipsoid(p, (0.06 * s, 0.06 * s, 0.06 * s), segs=8, rings=5), berries, sway=0.12)
            placed += 1
    return B.to_object(coll)


def rock_small(coll, name="RockSmall", size=(0.6, 0.5, 0.42), seed=4, moss=True):
    B = Build(name)
    B.ao = 0.6
    rule = (lambda p, n: rgb("moss") if moss and n.z > 0.72 else mix("rock_dark", "rock_light", 0.25 + 0.65 * max(0.0, n.z)))
    B.add(rock_shape((0, 0, size[2] * 0.85), size, seed=seed, jag=0.12, flat_bottom=0.02), rule)
    if seed % 2:
        B.add(rock_shape((size[0] * 0.95, size[1] * 0.3, size[2] * 0.4), (size[0] * 0.45, size[1] * 0.45, size[2] * 0.4),
                         seed=seed + 1, jag=0.12, flat_bottom=0.02), rule)
    grass_clump(B, (0, size[1] * 0.8, 0), 5, (0.12, 0.24), 0.4, seed + 3)
    return B.to_object(coll)


def pebbles(coll):
    B = Build("Pebbles")
    B.ao = 0.3
    rng = random.Random(8)
    for k in range(7):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(0.0, 0.45)
        s = rng.uniform(0.06, 0.13)
        B.add(rock_shape((math.cos(a) * r, math.sin(a) * r, s * 0.4), (s * 1.3, s, s * 0.6), seed=80 + k, jag=0.15, subdiv=1,
                         flat_bottom=0.01),
              lambda p, n: mix("rock_dark", "rock_warm", 0.4 + 0.6 * max(0.0, n.z)))
    return B.to_object(coll)


def mushrooms(coll):
    B = Build("Mushrooms")
    B.ao = 0.4
    for (x, y), h, r in (((0.0, 0.0), 0.32, 0.17), ((0.2, 0.12), 0.22, 0.12), ((-0.16, 0.1), 0.15, 0.09)):
        mushroom(B, (x, y, 0), h, r, lean=(x * 0.2, 0.03))
    grass_clump(B, (0, 0.05, 0), 6, (0.1, 0.2), 0.3, 5)
    return B.to_object(coll)


def horsetail(coll, seed=12, name="Horsetail"):
    """Prehistoric horsetail reeds: jointed green stems with dark rings and whorls of needles."""
    B = Build(name)
    B.ao = 0.5
    rng = random.Random(seed)
    for k in range(9):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(0.0, 0.3)
        base = Vector((math.cos(a) * r, math.sin(a) * r, 0))
        h = rng.uniform(0.8, 1.45)
        lean = Vector((math.cos(a), math.sin(a), 0)) * rng.uniform(0.05, 0.2)
        top = base + Vector((0, 0, h)) + lean
        sw = sway_height(0.0, 1.4, 1.4, 0.8)
        B.add(tube([base, base.lerp(top, 0.5) + lean * 0.3, top], [0.028, 0.024, 0.012], segs=5, sub=3),
              lambda p, n, hh=h: mix("stem", "leaf_light", 0.5 + 0.5 * math.sin(p.z * 26.0)) if (p.z * 26.0) % 6.28 > 0.7 else rgb("leaf_dark"),
              sway=sw)
        for j in range(3):
            z = h * (0.35 + 0.2 * j)
            c = base.lerp(top, z / h)
            for m in range(3 if kit.low() else 5):
                b2 = 2 * math.pi * m / (3 if kit.low() else 5) + j
                d = Vector((math.cos(b2), math.sin(b2), 0.7)).normalized()
                B.add(tube([c, c + d * 0.14], [0.006, 0.003], segs=3, sub=1, cap=False), "leaf_light", sway=sw)
    return B.to_object(coll)


def giant_leaf(coll, name="GiantLeaf", seed=21, leaves=7, size=1.0):
    """Elephant ears: big heart-shaped leaves on long arching stalks (the jungle's big shapes, framing the path)."""
    B = Build(name)
    B.ao = 1.2 * size
    rng = random.Random(seed)
    for k in range(leaves):
        a = 2 * math.pi * k / leaves + rng.uniform(-0.25, 0.25)
        d = Vector((math.cos(a), math.sin(a), 0))
        h = size * rng.uniform(0.9, 1.6)
        reach = size * rng.uniform(0.35, 0.65)
        base = Vector((0, 0, 0))
        tip = base + d * reach + Vector((0, 0, h))
        B.add(tube([base, base + d * reach * 0.2 + Vector((0, 0, h * 0.6)), tip], [0.035 * size, 0.025 * size], segs=5, sub=3,
                   cap=False), "stem", sway=sway_height(0.0, h, 1.5, 0.35))
        # the blade hangs from the stalk's top, tipping down and out; heart-shaped (two round lobes at its base)
        length = size * rng.uniform(0.75, 1.05)
        out = (d + Vector((0, 0, -0.55 + rng.uniform(-0.15, 0.15)))).normalized()
        n = 9
        pts, widths = [], []
        for i in range(n):
            t = i / (n - 1)
            p = tip + out * length * t + Vector((0, 0, -0.18 * length * t * t))
            w = length * 0.42 * (math.sin(math.pi * min(1.0, 0.08 + t * 0.92)) ** 0.75) * (1.0 + 0.35 * max(0.0, 0.25 - t) * 4)
            pts.append(p)
            widths.append(max(0.01, w))
        axis = (tip, out)

        def leaf_rule(p, nrm, axis=axis, L=length):
            o, dd = axis
            q = p - o
            along = max(0.0, min(1.0, q.dot(dd) / L))
            side = (q - dd * q.dot(dd)).length / (L * 0.42)
            c = kit.ramp([(0.0, "#1C5E46"), (0.5, "#2F8C4E"), (1.0, "#6DBA52")], 0.25 + 0.55 * along + 0.2 * side)
            return mix(c, "#B9E07A", max(0.0, 1.0 - side * 9.0) * 0.6)
        B.add(strip_leaf(pts, widths, rib=length * 0.05, thick=0.012), leaf_rule,
              sway=lambda p, hh=h: 0.25 + 0.55 * min(1.0, p.z / max(0.1, hh)))
    return B.to_object(coll)


def flower_bed(coll, name="FlowerBed", seed=31, colours=("pink", "yellow", "white", "coral", "lilac")):
    """A low dense bed of flowers and leaves for the path's edge."""
    B = Build(name)
    B.ao = 0.5
    rng = random.Random(seed)
    balls = [((0, 0, 0.18), (0.55, 0.45, 0.2)), ((0.5, 0.1, 0.15), (0.35, 0.3, 0.16)), ((-0.5, -0.05, 0.15), (0.38, 0.3, 0.16))]
    crown(B, balls, (0, 0, 0.1), -0.05, 0.4, seed, LEAF_RAMP, subdiv=2, sway=0.1, k=0.6)
    for k in range(14):
        c, r = balls[rng.randrange(len(balls))]
        a = rng.uniform(0, 2 * math.pi)
        p = Vector(c) + Vector((math.cos(a) * r[0] * 0.8, math.sin(a) * r[1] * 0.8, r[2] * 0.7))
        flower(B, p, rng.uniform(0.05, 0.16), colours[k % len(colours)], radius=rng.uniform(0.07, 0.1))
    return B.to_object(coll)


def verge(coll, name="Verge", seed=41, big=False):
    """A clump for the path's edge in one mesh (cheaper than its parts one by one): a fern, grass, flowers, a stone."""
    B = Build(name)
    B.two_sided = True
    B.ao = 0.8
    rng = random.Random(seed)
    size = 1.25 if big else 0.95
    for k in range(9):
        a = 2 * math.pi * k / 9 + rng.uniform(-0.2, 0.2)
        B.add(kit.pinnate((math.cos(a) * 0.05, math.sin(a) * 0.05, 0.04), (math.cos(a), math.sin(a), 0), size * rng.uniform(0.8, 1.05),
                          size * 0.27, rise=rng.uniform(0.45, 0.65), droop=0.9, pairs=9, leaflet_shape=0.55),
              by_height(FERN_RAMP, 0.0, size * 0.6, seed + k * 0.3), sway=lambda p, s=size: 0.15 + 0.6 * min(1.0, p.xy.length / s),
              up=0.5)
    grass_clump(B, (0.55, 0.35, 0.0), 12, (0.2, 0.42), 0.4, seed + 1)
    grass_clump(B, (-0.5, 0.45, 0.0), 10, (0.18, 0.36), 0.35, seed + 2)
    for k in range(5):
        a = rng.uniform(-0.5, 3.6)
        r = rng.uniform(0.45, 0.8)
        flower(B, (math.cos(a) * r, math.sin(a) * r, 0), rng.uniform(0.22, 0.42), ("yellow", "white", "pink", "coral", "lilac")[(k + seed) % 5],
               radius=rng.uniform(0.07, 0.1))
    if rng.random() < 0.6:
        B.add(rock_shape((0.3, 0.75, 0.14), (0.24, 0.2, 0.16), seed=seed + 7, jag=0.14, flat_bottom=0.02),
              lambda p, n: mix("rock_dark", "rock_light", 0.3 + 0.6 * max(0.0, n.z)))
    return B.to_object(coll)


# ------------------------------------------------------------------------------------------------- beside the path

def tree_fern(coll, name="TreeFern", height=4.2, seed=3, fronds=11, far=False):
    """A tree fern: a shaggy trunk and a crown of long arching fronds, two fiddleheads unrolling at the top. far: the
    version for 45 m and beyond (a quarter of the triangles: fewer fronds and leaflets, a plainer trunk)."""
    if far:
        fronds = max(7, fronds // 2)
    B = Build(name)
    B.two_sided = True
    B.ao = 1.6
    rng = random.Random(seed)
    lean = Vector((rng.uniform(-0.25, 0.25), rng.uniform(-0.25, 0.25), 0))
    trunk = [Vector((0, 0, -0.2)), Vector((0, 0, height * 0.35)) + lean * 0.2, Vector((0, 0, height * 0.7)) + lean * 0.6,
             Vector((0, 0, height)) + lean]
    B.add(tube(trunk, [0.3, 0.23, 0.2, 0.21], segs=6 if far else 10, sub=2 if far else 4, cap=False,
               ridge=lambda a, t: 0.12 * abs(math.sin(t * 28.0)) + 0.04 * math.sin(7 * a)),
          lambda p, n: mix("fern_trunk_dark", "fern_trunk", 0.5 + 0.5 * math.sin(p.z * 9.0 + noise(p * 3.0) * 2.0)),
          sway=sway_height(1.0, height, 2.0, 0.08))
    top = trunk[-1]
    for k in range(fronds):
        a = 2 * math.pi * k / fronds + rng.uniform(-0.15, 0.15)
        d = Vector((math.cos(a), math.sin(a), 0))
        length = rng.uniform(2.2, 2.9)
        B.add(kit.pinnate(top, d, length, 0.8 if far else 0.72, rise=rng.uniform(0.2, 0.42), droop=1.3, pairs=6 if far else 13,
                          rachis=0.02, leaflet_shape=0.75 if far else 0.5),
              by_height(FERN_RAMP, top.z - 1.6, top.z + 0.6, seed + k * 0.37, 0.1),
              sway=lambda p, t=top: 0.1 + 0.8 * min(1.0, (p - t).length / 2.4), up=0.45)
    for k in range(0 if far else 2):
        a = rng.uniform(0, 2 * math.pi)
        d = Vector((math.cos(a), math.sin(a), 0))
        base = top + d * 0.05
        mid = base + Vector((0, 0, 0.5)) + d * 0.1
        B.add(tube([base, mid, mid + d * 0.15 + Vector((0, 0, 0.08)), mid + d * 0.2 - Vector((0, 0, 0.03))],
                   [0.04, 0.035, 0.05, 0.045], segs=6, sub=2), "leaf_light", sway=0.3)
    return B.to_object(coll)


def cycad(coll, name="Cycad", seed=5, height=1.6, far=False):
    """A cycad: a stout scaly trunk (diamond scales) and a crown of stiff glossy fronds. far: fewer of them, plainer."""
    B = Build(name)
    B.two_sided = True
    B.ao = 1.0
    rng = random.Random(seed)
    B.add(tube([(0, 0, -0.1), (0, 0, height * 0.5), (0, 0, height)], [0.36, 0.34, 0.3], segs=7 if far else 12, sub=2 if far else 3, cap=True,
               ridge=lambda a, t: 0.06 * abs(math.sin(6 * a + t * 14.0)) * abs(math.sin(t * 22.0))),
          lambda p, n: mix("bark", "sandstone_dark", 0.5 + 0.5 * math.sin(p.z * 20.0) * math.sin(math.atan2(p.y, p.x) * 6.0)))
    top = Vector((0, 0, height))
    for ring in range(1 if far else 2):
        n = (8 if far else 9) if ring == 0 else 7
        for k in range(n):
            a = 2 * math.pi * k / n + ring * 0.35 + rng.uniform(-0.1, 0.1)
            d = Vector((math.cos(a), math.sin(a), 0))
            B.add(kit.pinnate(top + Vector((0, 0, 0.05 * ring)), d, rng.uniform(1.4, 1.8) * (0.85 if ring else 1.0), 0.32,
                              rise=0.55 + 0.25 * ring, droop=0.9, pairs=5 if far else 11, rachis=0.018, tilt=0.45, taper=0.4,
                              leaflet_shape=0.5 if far else 0.32),
                  by_height(PALM_RAMP, top.z - 0.6, top.z + 0.9, seed + k + ring * 10), sway=lambda p, t=top: 0.1 + 0.6 * min(1.0, (p - t).length / 1.6))
    return B.to_object(coll)


def palm(coll, name="Palm", seed=7, height=6.5, far=False):
    """A tall palm leaning over: a ringed trunk, a crown of long drooping fronds. far: fewer leaflets, a plainer trunk."""
    B = Build(name)
    B.two_sided = True
    B.ao = 1.8
    rng = random.Random(seed)
    bend = Vector((rng.choice((-1, 1)) * 1.2, 0.3, 0))
    pts = [Vector((0, 0, -0.2)), Vector((0, 0, height * 0.4)) + bend * 0.25, Vector((0, 0, height * 0.75)) + bend * 0.65,
           Vector((0, 0, height)) + bend]
    B.add(tube(pts, [0.3, 0.24, 0.2, 0.19], segs=6 if far else 10, sub=2 if far else 5, cap=True,
               ridge=lambda a, t: 0.07 * abs(math.sin(t * 34.0))),
          lambda p, n: mix("bark", "bark_light", 0.5 + 0.5 * math.sin(p.z * 11.0)), sway=sway_height(1.0, height, 2.0, 0.12))
    top = pts[-1]
    for k in range(8):
        a = 2 * math.pi * k / 8 + rng.uniform(-0.1, 0.1)
        d = Vector((math.cos(a), math.sin(a), 0))
        B.add(kit.pinnate(top, d, rng.uniform(2.6, 3.2), 0.9 if far else 0.8, rise=0.25, droop=1.5, pairs=6 if far else 14, rachis=0.025,
                          tilt=0.15, taper=0.5, leaflet_shape=0.6 if far else 0.4),
              by_height(PALM_RAMP, top.z - 2.0, top.z + 0.5, seed + k * 0.5),
              sway=lambda p, t=top: 0.15 + 0.85 * min(1.0, (p - t).length / 3.0))
    for k in range(0 if far else 3):
        a = 2 * math.pi * k / 3
        B.add(ellipsoid(top + Vector((math.cos(a) * 0.17, math.sin(a) * 0.17, -0.2)), (0.12, 0.12, 0.13), segs=8, rings=6),
              "bark_dark")
    return B.to_object(coll)


def araucaria(coll, name="Araucaria", seed=9, height=9.0, far=False):
    """A monkey-puzzle tree, the Jurassic's own silhouette: a straight trunk, tiers of upturned branches ending in tufts,
    flat-topped. far: fewer tiers and plainer tufts."""
    B = Build(name)
    B.ao = 2.0
    rng = random.Random(seed)
    B.add(tube([(0, 0, -0.2), (0, 0, height * 0.5), (0, 0, height)], [0.34, 0.26, 0.12], segs=6 if far else 8, sub=2),
          bark_rule(5.0), sway=sway_height(2.0, height, 2.0, 0.06))
    tiers = 4 if far else 5
    for t in range(tiers):
        z = height * (0.42 + 0.56 * t / (tiers - 1))
        span = 2.4 * (1 - 0.55 * t / (tiers - 1)) + 0.4
        n = 4 if far else 5
        for k in range(n):
            a = 2 * math.pi * k / n + t * 0.7 + rng.uniform(-0.15, 0.15)
            d = Vector((math.cos(a), math.sin(a), 0))
            base = Vector((0, 0, z))
            mid = base + d * span * 0.55 + Vector((0, 0, -0.15))
            tip = base + d * span + Vector((0, 0, 0.35))
            B.add(tube([base, mid, tip], [0.07, 0.05, 0.035], segs=4, sub=1 if far else 2), "bark", sway=0.15)
            balls = [(tip + Vector((0, 0, 0.05)), (0.5, 0.5, 0.32))]
            crown(B, balls, tip, z - 0.5, z + 0.6, seed + t * 7 + k, DARK_RAMP, subdiv=1 if far else 2, sway=0.3, k=0.6, amp=0.12)
    crown(B, [((0, 0, height + 0.1), (0.9, 0.9, 0.38))], (0, 0, height), height - 0.4, height + 0.5, seed + 99, DARK_RAMP,
          subdiv=1 if far else 2, sway=0.3)
    return B.to_object(coll)


def tree_round(coll, name="TreeRound", seed=7, scale=1.0, far=False):
    """A broad-leaved tree with a big round crown (the valley's softer trees). far: a plainer crown."""
    B = Build(name)
    B.ao = 1.8
    s = scale
    B.add(tube([(0, 0, -0.3), (0.1 * s, 0.05, 1.3 * s), (-0.05, 0, 2.4 * s), (0.05, 0, 3.1 * s)], [0.4 * s, 0.3 * s, 0.22 * s, 0.18 * s],
               segs=6 if far else 10, sub=2 if far else 3, ridge=grooves(7, 0.05)), bark_rule(7.0), sway=sway_height(1.0, 3.0 * s, 2, 0.1))
    for a in (() if far else (0.3, 2.4, 4.2)):
        B.add(tube([(0.05, 0, 2.3 * s), (math.cos(a) * 0.7 * s, math.sin(a) * 0.7 * s, 3.0 * s),
                    (math.cos(a) * 1.2 * s, math.sin(a) * 1.2 * s, 3.5 * s)], [0.13 * s, 0.07 * s], segs=6), "bark", sway=0.1)
    balls = [((0, 0, 4.2 * s), 1.6 * s), ((1.2 * s, 0.2 * s, 3.8 * s), 1.1 * s), ((-1.15 * s, -0.1 * s, 3.85 * s), 1.15 * s),
             ((0.35 * s, -0.9 * s, 3.7 * s), 1.05 * s), ((-0.3 * s, 0.9 * s, 4.0 * s), 1.05 * s), ((0.15 * s, 0.05 * s, 5.1 * s), 1.1 * s)]
    crown(B, balls[:4] if far else balls, (0, 0, 4.1 * s), 2.6 * s, 6.2 * s, seed, LEAF_RAMP, subdiv=2 if far else 3, sway=0.2)
    return B.to_object(coll)


def rock_big(coll, name="RockBig", size=(1.6, 1.3, 1.25), seed=11):
    B = Build(name)
    B.ao = 1.4
    rule = lambda p, n: rgb("moss") if n.z > 0.68 else mix("rock_dark", "rock_light", 0.2 + 0.7 * max(0.0, min(1.0, n.z * 0.6 + p.z / (size[2] * 2.4))))
    B.add(rock_shape((0, 0, size[2] * 0.9), size, seed=seed, jag=0.1, flat_bottom=0.02), rule)
    B.add(rock_shape((size[0] * 0.9, -size[1] * 0.4, size[2] * 0.35), (size[0] * 0.45, size[1] * 0.5, size[2] * 0.38),
                     seed=seed + 1, jag=0.12, flat_bottom=0.02), rule)
    for k in range(5):
        a = math.pi * (0.1 + 0.8 * k / 4)
        B.add(frond((math.cos(a) * size[0] * 0.8, math.sin(a) * size[1] * 0.9, 0.03), (math.cos(a), math.sin(a), 0), 0.75, 0.1,
                    rise=0.45, droop=0.9, segs=6, leaflets=1), by_height(FERN_RAMP, 0.0, 0.5, k + seed), sway=0.45)
    grass_clump(B, (0, size[1] * 0.9, 0), 8, (0.15, 0.32), 0.8, seed + 2)
    return B.to_object(coll)


# ------------------------------------------------------------------------------------------------- far away

def forest_far(coll, name="ForestFar", seed=17, n=7, width=14.0):
    """A clump of far trees in one cheap mesh (round crowns and pointed ones), for the valley's slopes."""
    B = Build(name)
    B.ao = 2.5
    rng = random.Random(seed)
    for k in range(n):
        x = (k / max(1, n - 1) - 0.5) * width + rng.uniform(-0.8, 0.8)
        y = rng.uniform(-2.5, 2.5)
        h = rng.uniform(4.5, 8.0)
        if k % 3 == 2:
            # a pointed one
            B.add(cone((x, y, 0.0), (x, y, h * 1.25), h * 0.32, segs=8), by_height(DARK_RAMP, 0.0, h * 1.2, seed + k))
        else:
            B.add(tube([(x, y, -0.2), (x, y, h * 0.55)], [0.25, 0.18], segs=5, sub=1, cap=False), "bark")
            r = h * 0.3
            crown(B, [((x, y, h * 0.62), (r, r, r * 0.9)), ((x + r * 0.6, y, h * 0.5), (r * 0.7,) * 3),
                      ((x - r * 0.6, y, h * 0.52), (r * 0.7,) * 3)], (x, y, h * 0.6), h * 0.2, h, seed + k, FAR_RAMP,
                  subdiv=2, sway=0.0, amp=0.1)
    return B.to_object(coll)


def cliff(coll, name="Cliff", length=40.0, height=16.0, seed=19):
    """A stretch of the valley's wall (sandstone strata, a grassy top, bushes on its ledges), `length` m along the path
    (Blender Y), its face toward +X (the path when it stands on the camera's left; the game mirrors it for the right)."""
    B = Build(name)
    B.ao = 4.0
    rng = random.Random(seed)
    ny, nz = 26, 10

    def surface(bm):
        rows = []
        for j in range(nz + 1):
            t = j / nz
            z = height * t
            row = []
            for i in range(ny + 1):
                y = (i / ny - 0.5) * length
                # ledges: the face steps back at each stratum, and wobbles along its length
                stratum = math.floor(t * 4.0) / 4.0
                x = -2.5 * stratum - 1.2 * t + 1.6 * noise(Vector((y * 0.08, z * 0.15, seed))) + 0.6 * noise(Vector((y * 0.3, z * 0.4, seed + 3)))
                row.append(bm.verts.new((x, y, z)))
            rows.append(row)
        top = []
        for i in range(ny + 1):
            y = (i / ny - 0.5) * length
            top.append(bm.verts.new((rows[-1][i].co.x - 6.0, y, height + 0.6 * noise(Vector((y * 0.1, 0, seed))))))
        for j in range(nz):
            for i in range(ny):
                bm.faces.new((rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i]))
        for i in range(ny):
            bm.faces.new((rows[-1][i], rows[-1][i + 1], top[i + 1], top[i]))

    def rule(p, n):
        if n.z > 0.55:
            return mix("moss_dark", "moss", 0.5 + 0.5 * noise(p * 0.3))
        band = 0.5 + 0.5 * math.sin(p.z * 1.9 + 0.8 * noise(p * 0.12))
        c = mix("sandstone_dark", "sandstone", band)
        return mix(c, "sandstone_light", max(0.0, noise(p * 0.2 + Vector((5, 1, 2)))) * 0.6)
    B.add(surface, rule)
    for k in range(9):
        y = (rng.random() - 0.5) * length * 0.9
        z = height * rng.choice((0.25, 0.5, 0.75, 1.0)) + 0.1
        x = -2.5 * (z / height) - 1.6
        s = rng.uniform(0.8, 1.5)
        crown(B, [((x, y, z + 0.4 * s), (s, s * 1.1, s * 0.7)), ((x - 0.4, y + s * 0.8, z + 0.3 * s), (s * 0.7,) * 3)],
              (x, y, z), z - 0.3, z + s, seed + k, LEAF_RAMP, subdiv=2, sway=0.1, amp=0.12)
    return B.to_object(coll)


def volcano(coll):
    """The valley's landmark, far behind everything: a big volcano, lava streaks glowing down its sides, the crater
    glowing (the game puffs smoke from its top). About 70 m tall."""
    B = Build("Volcano")
    B.ao = 12.0
    height, r_base, r_top = 70.0, 95.0, 13.0
    rng = random.Random(4)

    def body(bm):
        segs, rings = 40, 14
        grid = []
        for j in range(rings + 1):
            t = j / rings
            z = height * t
            r = r_top + (r_base - r_top) * (1 - t) ** 2.1
            row = []
            for i in range(segs):
                a = 2 * math.pi * i / segs
                k = 1 + (0.06 * noise(Vector((math.cos(a) * 2.0, math.sin(a) * 2.0, t * 3.0))) if 0 < j < rings else 0)
                gully = 1 - 0.05 * abs(math.sin(a * 7.0)) * t
                row.append(bm.verts.new((math.cos(a) * r * k * gully, math.sin(a) * r * k * gully, z)))
            grid.append(row)
        for j in range(rings):
            for i in range(segs):
                a, b = grid[j][i], grid[j][(i + 1) % segs]
                c, d = grid[j + 1][(i + 1) % segs], grid[j + 1][i]
                bm.faces.new((a, b, c, d))
        # the crater: a ring dipping in, then a flat lake of lava
        rim = grid[-1]
        inner = [bm.verts.new((v.co.x * 0.7, v.co.y * 0.7, height - 4.0)) for v in rim]
        for i in range(segs):
            bm.faces.new((rim[i], rim[(i + 1) % segs], inner[(i + 1) % segs], inner[i]))
        centre = bm.verts.new((0, 0, height - 4.2))
        for i in range(segs):
            bm.faces.new((inner[i], inner[(i + 1) % segs], centre))

    streaks = (35.0, 95.0, 160.0, 230.0, 300.0)

    def lava_amount(p):
        a = math.degrees(math.atan2(p.y, p.x)) % 360
        t = p.z / height
        if t > 0.94:
            return 1.0
        w = 7.0 - 4.0 * (1 - t) + 2.0 * noise(Vector((p.z * 0.1, a * 0.05, 1)))
        best = min(abs((a - s + 180) % 360 - 180) for s in streaks)
        return max(0.0, min(1.0, (w - best) / 2.0)) * (1.0 if t > 0.35 else max(0.0, (t - 0.15) / 0.2))

    def rule(p, n):
        t = p.z / height
        base = kit.ramp([(0.0, "volcano_dark"), (0.6, "volcano"), (1.0, "volcano_top")], t + 0.06 * noise(p * 0.08))
        k = lava_amount(p)
        return mix(base, "lava_hot" if t > 0.9 else "lava", k)
    B.add(body, rule, glow=lambda p: 0.95 * lava_amount(p))
    return B.to_object(coll)


def mountains(coll):
    """A long range of blue mountains (300 m), for behind the valley; the haze melts them into the sky."""
    B = Build("Mountains")
    B.ao = 20.0
    nx, ny = 64, 6
    width, depth = 320.0, 40.0

    def h(x):
        return (34 + 26 * noise(Vector((x * 0.012, 0.3, 0))) + 14 * noise(Vector((x * 0.035, 1.7, 0)))
                + 6 * noise(Vector((x * 0.09, 2.9, 0))))

    def make(bm):
        grid = []
        for j in range(ny + 1):
            t = j / ny
            row = []
            for i in range(nx + 1):
                x = (i / nx - 0.5) * width
                z = max(0.0, h(x) * math.sin(math.pi * min(1.0, t * 1.05)) ** 0.7)
                row.append(bm.verts.new((x, -depth * (t - 0.5), z)))
            grid.append(row)
        for j in range(ny):
            for i in range(nx):
                bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))

    B.add(make, lambda p, n: kit.ramp([(0.0, "mount_base"), (0.6, "mount_mid"), (1.0, "mount_top")], p.z / 70.0 + 0.1 * noise(p * 0.05)))
    obj = B.to_object(coll)
    obj["sky"] = 1
    return obj


def cloud(coll, name, balls, seed):
    B = Build(name)
    B.ao = 6.0
    shapes = [(c, r) for c, r in balls]
    for i, (c, r) in enumerate(shapes):
        others = [o for j, o in enumerate(shapes) if j != i]
        B.add(blob(c, r, seed + i, 0.06, 3, flat_bottom=0.0, others=others),
              lambda p, n: mix("cloud_under", "cloud", 0.15 + 0.85 * max(0.0, min(1.0, n.z + 0.35))), sphere=((0, 0, 1.2), 0.7))
    obj = B.to_object(coll)
    obj["sky"] = 1
    return obj


# ------------------------------------------------------------------------------------------------- sights

def rock_arch(coll):
    """A natural stone arch right over the path (its legs well outside the lanes, its top ~8 m up), moss on top and
    vines hanging: a landmark to run under."""
    B = Build("RockArch")
    B.ao = 3.0
    pts = [(-7.2, 0.3, -0.5), (-6.6, 0.0, 3.0), (-5.0, -0.2, 6.6), (-2.0, 0.0, 8.4), (1.5, 0.1, 8.6), (4.6, 0.0, 7.2),
           (6.4, -0.2, 3.6), (7.0, 0.3, -0.5)]

    def ridge(a, t):
        return 0.18 * noise(Vector((math.cos(a) * 1.5, math.sin(a) * 1.5, t * 6.0)))

    def rule(p, n):
        if n.z > 0.62 and p.z > 6.0:
            return mix("moss_dark", "moss", 0.5 + 0.5 * noise(p * 0.8))
        band = 0.5 + 0.5 * math.sin(p.z * 2.4 + 0.6 * noise(p * 0.4))
        return mix(mix("sandstone_dark", "sandstone", band), "sandstone_light", max(0.0, noise(p * 0.5)) * 0.5)
    B.add(tube(pts, [2.0, 1.7, 1.35, 1.2, 1.2, 1.35, 1.75, 2.05], segs=14, sub=4, cap=False, ridge=ridge,
               squash=((0, 1, 0), 0.75)), rule)
    rng = random.Random(3)
    for k in range(10):
        x = rng.uniform(-3.5, 3.5)
        top = Vector((x, 0.9, 7.2 + 0.35 * math.cos(x * 0.35)))
        bottom = top.z - rng.uniform(1.0, 2.4)
        sw = lambda p, zz=top.z, bb=bottom: 0.2 + 0.7 * max(0.0, min(1.0, (zz - p.z) / max(0.01, zz - bb)))
        B.add(tube([top, Vector((x + 0.05, 1.0, (top.z + bottom) / 2)), Vector((x, 1.05, bottom))], [0.03, 0.02], segs=4, sub=2),
              "leaf_dark", sway=sw)
        for j in range(4):
            pz = top.z - (top.z - bottom) * (0.2 + 0.25 * j)
            d = Vector((0.6 * (1 if j % 2 else -1), 0.6, -0.3)).normalized()
            base = Vector((x, 1.05, pz))
            B.add(strip_leaf([base, base + d * 0.12, base + d * 0.22], [0.012, 0.06, 0.004], rib=0.008, thick=0.006),
                  "leaf_light" if j % 2 else "leaf", sway=sw)
    for x in (-6.2, 6.0):
        crown(B, [((x, 0.6, 1.0), (1.1, 0.9, 0.8)), ((x + 0.8, 1.2, 0.6), (0.8,) * 3)], (x, 0.6, 0.8), 0.0, 1.8,
              int(abs(x) * 3), LEAF_RAMP, subdiv=2, sway=0.15)
    return B.to_object(coll)


def rib_arch(coll):
    """A giant fossil ribcage half-buried across the path: a spine high over the lanes, ribs curving down to the ground
    outside them. A landmark (the volcano's land begins)."""
    B = Build("RibArch")
    B.ao = 2.5
    bone = lambda p, n: mix("bone_dark", "bone", 0.45 + 0.55 * max(0.0, n.z * 0.5 + 0.5))
    spine = [(0.0, 2.2, 7.4), (0.0, 0.0, 7.9), (0.0, -2.4, 7.6)]
    B.add(tube(spine, [0.45, 0.5, 0.42], segs=10, sub=3), bone)
    for k, y in enumerate((1.8, 0.6, -0.6, -1.8)):
        B.add(ellipsoid((0, y, 8.3), (0.3, 0.22, 0.4), segs=10, rings=6), bone)
        for side in (1, -1):
            pts = [(0.0, y, 7.6), (2.6 * side, y, 7.8), (5.2 * side, y + 0.1, 5.6), (6.3 * side, y + 0.2, 2.2),
                   (6.0 * side, y + 0.3, -0.4)]
            B.add(tube(pts, [0.3, 0.28, 0.24, 0.2, 0.18], segs=8, sub=4, cap=True), bone)
    for side in (1, -1):
        for y in (1.8, -0.6):
            grass_clump(B, (6.0 * side, y + 0.3, 0.0), 8, (0.2, 0.45), 0.6, int(70 + y * 10 + side))
    return B.to_object(coll)


# ------------------------------------------------------------------------------------------------- all

def half_detail(fn, *args, **kw):
    """A piece built again at half the detail (kit.DETAIL 0.5), its name given among the arguments ("...Far")."""
    def make(c):
        kit.DETAIL = 0.5
        try:
            return fn(c, *args, **kw)
        finally:
            kit.DETAIL = 1.0
    return make


PIECES = [
    log_low, stone_row, log_long, vine_bridge, bronto_neck, boulder, trike, egg, egg_gold,
    lambda c: grass_tuft(c, "GrassTuft", 18, (0.2, 0.42), 0.32, 1),
    lambda c: grass_tuft(c, "GrassTall", 14, (0.45, 0.8), 0.35, 2),
    lambda c: grass_tuft(c, "GrassFlowers", 10, (0.2, 0.38), 0.3, 3, flowers=4),
    lambda c: fern(c, "Fern", 0.9, 2),
    lambda c: fern(c, "FernBig", 1.7, 5, 12),
    lambda c: giant_leaf(c, "GiantLeaf", 21, 7, 1.0),
    lambda c: giant_leaf(c, "GiantLeaf2", 27, 9, 1.35),
    lambda c: flower_bed(c, "FlowerBed", 31),
    lambda c: flower_bed(c, "FlowerBed2", 35, ("orange", "yellow", "white", "pink", "blue")),
    lambda c: verge(c, "Verge", 41),
    lambda c: verge(c, "Verge2", 47, big=True),
    lambda c: flower_patch(c, "FlowerPatch"),
    lambda c: flower_patch(c, "FlowerPatchB", ("orange", "yellow", "white", "lilac", "coral"), 9, 8),
    lambda c: bush(c, "Bush", 10, None, 1.0),
    lambda c: bush(c, "BushBerry", 12, "berry", 1.1),
    lambda c: bush(c, "BushFlower", 14, "flowers", 0.9),
    lambda c: rock_small(c, "RockSmall", (0.55, 0.45, 0.4), 4),
    lambda c: rock_small(c, "RockSmall2", (0.75, 0.6, 0.5), 7),
    pebbles, mushrooms, horsetail,
    lambda c: tree_fern(c, "TreeFern", 4.2, 3, 15),
    lambda c: tree_fern(c, "TreeFern2", 5.4, 8, 16),
    lambda c: cycad(c, "Cycad", 5, 1.5),
    lambda c: cycad(c, "Cycad2", 9, 2.2),
    lambda c: palm(c, "Palm", 7, 6.5),
    lambda c: araucaria(c, "Araucaria", 9, 9.5),
    lambda c: tree_round(c, "TreeRound", 7, 1.0),
    lambda c: tree_fern(c, "TreeFernFar", 4.2, 3, 15, far=True),
    lambda c: tree_fern(c, "TreeFern2Far", 5.4, 8, 16, far=True),
    lambda c: cycad(c, "CycadFar", 5, 1.5, far=True),
    lambda c: cycad(c, "Cycad2Far", 9, 2.2, far=True),
    lambda c: palm(c, "PalmFar", 7, 6.5, far=True),
    lambda c: araucaria(c, "AraucariaFar", 9, 9.5, far=True),
    lambda c: tree_round(c, "TreeRoundFar", 7, 1.0, far=True),
    # the plants beside the path at half the detail, for 24-30 m on (the trees' far versions above are made by hand)
    half_detail(verge, "VergeFar", 41),
    half_detail(verge, "Verge2Far", 47, big=True),
    half_detail(flower_bed, "FlowerBedFar", 31),
    half_detail(flower_bed, "FlowerBed2Far", 35, ("orange", "yellow", "white", "pink", "blue")),
    half_detail(fern, "FernFar", 0.9, 2),
    half_detail(fern, "FernBigFar", 1.7, 5, 12),
    half_detail(giant_leaf, "GiantLeafFar", 21, 7, 1.0),
    half_detail(giant_leaf, "GiantLeaf2Far", 27, 9, 1.35),
    half_detail(bush, "BushFar", 10, None, 1.0),
    half_detail(bush, "BushBerryFar", 12, "berry", 1.1),
    half_detail(bush, "BushFlowerFar", 14, "flowers", 0.9),
    half_detail(horsetail, 12, "HorsetailFar"),
    lambda c: rock_big(c, "RockBig", (1.6, 1.3, 1.25), 11),
    lambda c: rock_big(c, "RockBig2", (2.2, 1.8, 1.6), 14),
    lambda c: forest_far(c, "ForestFar", 17, 7, 14.0),
    lambda c: forest_far(c, "ForestFar2", 23, 9, 18.0),
    cliff, volcano, mountains,
    lambda c: cloud(c, "Cloud1", [((0, 0, 2.0), (4.2, 2.8, 2.4)), ((-4.0, 0.3, 1.2), (2.8, 2.2, 1.7)), ((4.2, -0.2, 1.4), (3.1, 2.4, 1.9)),
                                  ((1.5, 0.6, 3.4), (2.4, 1.9, 1.9))], 1),
    lambda c: cloud(c, "Cloud2", [((0, 0, 1.6), (3.0, 2.2, 2.0)), ((3.2, 0.2, 1.0), (2.2, 1.8, 1.4)), ((-2.6, -0.2, 1.1), (2.0, 1.6, 1.3))], 5),
    lambda c: cloud(c, "Cloud3", [((0, 0, 1.2), (5.5, 2.0, 1.4)), ((-3.5, 0, 1.6), (2.4, 1.6, 1.6)), ((2.5, 0.3, 1.9), (2.6, 1.7, 1.8))], 9),
    rock_arch, rib_arch,
]


def build():
    coll = ta.collection("Valley")
    for o in list(coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    made = {}
    for fn in PIECES:
        obj = fn(coll)
        made[obj.name] = ta.tri_count(obj)
    return made


def bake_ao():
    return kit.bake_ao(objects())


def export():
    objs = objects()
    for o in objs:
        o.location = (0, 0, 0)
    path = kit.export_fbx(objs, os.path.join(UNITY_ART, "Valley.fbx"))
    info = {o.name: {"tris": ta.tri_count(o), "verts": len(o.data.vertices)} for o in objs}
    with open(os.path.join(HERE, "valley.json"), "w", encoding="utf-8") as f:
        json.dump(info, f, indent=1)
    return path


# ------------------------------------------------------------------------------------------------- previews

SHEETS = [
    ("valley_obstacles", ["LogLow", "StoneRow", "VineBridge", "Boulder", "Trike", "Egg", "EggGold"], "#BFE3F5", -24, 12),
    ("valley_wide", ["LogLong", "BrontoNeck"], "#BFE3F5", -20, 14),
    ("valley_near", ["GrassTuft", "GrassTall", "GrassFlowers", "Fern", "FernBig", "FlowerPatch", "FlowerPatchB", "Bush",
                     "BushBerry", "BushFlower", "RockSmall", "RockSmall2", "Pebbles", "Mushrooms", "Horsetail"], "#BFE3F5", -15, 16),
    ("valley_jungle", ["GiantLeaf", "GiantLeaf2", "FlowerBed", "FlowerBed2", "Verge", "Verge2", "FernBig"], "#BFE3F5", -15, 14),
    ("valley_trees", ["TreeFern", "TreeFern2", "Cycad", "Cycad2", "Palm", "Araucaria", "TreeRound", "RockBig", "RockBig2"],
     "#BFE3F5", -15, 10),
    ("valley_far", ["ForestFar", "ForestFar2", "Cliff", "RockArch", "RibArch"], "#BFE3F5", -25, 12),
    ("valley_backdrop", ["Volcano", "Mountains"], "#BFE3F5", -10, 8),
    # each plant beside its far version (24-30 m on in the game)
    ("valley_lod_plants", ["Verge", "VergeFar", "Verge2", "Verge2Far", "FlowerBed", "FlowerBedFar", "FernBig", "FernBigFar",
                           "GiantLeaf2", "GiantLeaf2Far", "Horsetail", "HorsetailFar"], "#BFE3F5", -15, 14),
    ("valley_lod_bushes", ["Bush", "BushFar", "BushBerry", "BushBerryFar", "BushFlower", "BushFlowerFar", "Fern", "FernFar",
                           "GiantLeaf", "GiantLeafFar"], "#BFE3F5", -15, 14),
]


def preview():
    out_dir = os.path.join(HERE, "previews")
    objs = {o.name: o for o in objects()}
    kit.sun()
    paths = []
    only = os.environ.get("RUNNER_SHEETS")            # e.g. RUNNER_SHEETS=valley_lod_plants,valley_lod_bushes
    for name, names, bgc, yaw, pitch in SHEETS:
        if only and name not in only.split(","):
            continue
        for o in objs.values():
            o.hide_render = True
        names = [n for n in names if n in objs]
        width, bottom, top = kit.lineup([objs[n] for n in names])
        bpy.context.view_layer.update()
        dist = max(width / 0.92, (top - bottom) / 0.5, 2.5)
        target = Vector((width / 2, 0, (top + bottom) / 2))
        y, p = math.radians(yaw), math.radians(pitch)
        loc = target + Vector((math.sin(y) * math.cos(p), math.cos(y) * math.cos(p), math.sin(p))) * dist
        kit.camera("SheetCam", loc, target, fov_v_deg=32.0, clip=max(400.0, dist * 3))
        kit.world_colour(bgc)
        paths.append(kit.render(os.path.join(out_dir, f"{name}.png"), res=(1800, 900)))
    for o in objs.values():
        o.location = (0, 0, 0)
        o.hide_render = False
    return paths
