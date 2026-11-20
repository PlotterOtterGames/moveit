"""Superboks!'s four arenas as code (Blender 5.1, headless; run by build.py --arena), the overhaul of 2026-11-19 (the user:
"a complete graphics and gameplay overhaul ... production ready ... don't over-AI it, keep it well designed"). Each is a
place the climb goes through, higher and stranger each time, all round the same ring:

    gym       Palestra: the neighbourhood's boxing gym at night: bleachers full of people on three sides, a lighting rig
              over the ring with its beams, a hanging scoreboard, big arched windows on the city, heavy bags, banners
    roof      Çatia: the ring on a tower's roof at sunset: string lights round it, a water tower, air conditioners, a
              lattice antenna with a blinking light, a billboard, the city all round, the sun going down behind
    mountain  Mali: a wooden platform on a snowy summit above a sea of clouds: prayer flags, a stone hut, snowy pines,
              rocks, the other peaks all round
    stars     Yjet: the villain's floating arena among the stars: a deck with glowing lines, pylons with energy orbs,
              crystals and asteroids, his citadel behind, the Earth, a ringed planet, a nebula

Like Vrapo!'s valley and Zana!'s garden there are no textures: every vertex carries what Tundu/Arena needs (kit.py: the
colour, baked ambient occlusion in its alpha, glow in uv0.y), so the box lights it all at the vertices. Every piece is
built where it stands round the ring (Unity coordinates through kit.U: x right, y up, z away from the hero's side; the
ring's middle at the origin, its canvas RING_H up) and exported into Assets/Games/Boxing/Art/Arena.fbx:

    Ring_<Stage>     the ring in the stage's colours (canvas, logo, apron and its print, posts, corner pads: the hero's
                     corner blue, the opponent's red, ropes and their ties, steps, stools and buckets)
    <Stage>_Set      everything near (floor, stands, walls, props): Tundu/Arena
    <Stage>_Far      the far city, peaks, planets: Tundu/Arena _LITE
    <Stage>_Sky      a dome with its colours painted in (gradient, sun glow, nebula): Tundu/Arena _SKY
    <Stage>_Beams    light beams and halos (additive): Tundu/ArenaGlow
    Roof_Beacon      the antenna's light (the game blinks it)
The crowd's seats go to Art/arena_seats_<stage>.txt ("x y z yaw" lines, Sporte!'s Fans reads them).
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
sys.path.insert(0, os.path.join(HERE, "..", "runner"))
import tundu_art as ta  # noqa: E402
import kit  # noqa: E402
from kit import Build, U, blob, crystal, mix, rgb, rock_shape  # noqa: E402

UNITY_ART = os.path.join(ta.REPO, "unity", "MoveItGames", "Assets", "Games", "Boxing", "Art")
STAGES = ("gym", "roof", "mountain", "stars")

RING_H = 0.62          # the canvas's top above the floor
POST = 2.3             # the posts' centres from the middle
EDGE = 2.6             # the canvas's edge
POST_TOP = 1.32        # the posts above the canvas
ROPES = (0.36, 0.68, 1.0)



class FastBuild(Build):
    """kit.Build whose add() costs what its part costs: the part is made in a bmesh of its own and copied in, its vertices
    marked as they're made (kit.Build scans every vertex on every add: thousands of windows and bulbs took minutes)."""

    def add(self, make, colour, sway=0.0, glow=0.0, sphere=None, up=False, flat=False):
        bm = self.bm
        vl = bm.verts.layers.int.get("part") or bm.verts.layers.int.new("part")
        fl = bm.faces.layers.int.get("flat") or bm.faces.layers.int.new("flat")
        tmp = bmesh.new()
        make(tmp)
        self.vinfo.append((colour, sway, glow, sphere, up))
        part = len(self.vinfo)
        made = {}
        for v in tmp.verts:
            nv = bm.verts.new(v.co)
            nv[vl] = part
            made[v] = nv
        for f in tmp.faces:
            try:
                nf = bm.faces.new([made[v] for v in f.verts])
            except ValueError:
                continue
            nf[fl] = 1 if flat else -1
        tmp.free()
        return self


def UP(p):
    """Blender -> Unity coordinates (the colour rules work in Unity's)."""
    return Vector((-p.x, p.z, -p.y))


# ------------------------------------------------------------------------------------------------- shapes (Unity coords)

def _orient(bm, faces, want):
    for f in faces:
        f.normal_update()
        if f.normal.dot(want) < 0:
            f.normal_flip()


def box(c, size, yaw=0.0, bevel=0.0, pitch=0.0, roll=0.0):
    """A box: centre c, size (sx, sy, sz), turned yaw degrees round the up axis (then pitched about x, rolled about z);
    bevel = a chamfer on every edge (m): its edges catch the light."""
    def make(bm):
        geom = bmesh.ops.create_cube(bm, size=1.0)
        verts = geom["verts"]
        m = (Matrix.Translation(U(*c)) @ Matrix.Rotation(math.radians(-yaw), 4, "Z") @ Matrix.Rotation(math.radians(pitch), 4, "X")
             @ Matrix.Rotation(math.radians(-roll), 4, "Y") @ Matrix.Diagonal((size[0], size[2], size[1], 1.0)))
        bmesh.ops.transform(bm, matrix=m, verts=verts)
        if bevel > 0:
            edges = list({e for v in verts for e in v.link_edges})
            bmesh.ops.bevel(bm, geom=edges, offset=min(bevel, 0.45 * min(size)), segments=1, affect="EDGES", profile=0.5)
    return make


def cyl(base, r, h, segs=12, r_top=None, axis=(0.0, 1.0, 0.0), caps=True):
    """A cylinder (or a cone frustum) from `base` along `axis` (Unity) for h metres."""
    def make(bm):
        ax = Vector(axis).normalized()
        b = U(*base)
        a_bl = U(*ax) - U(0, 0, 0)
        rot = a_bl.to_track_quat("Z", "Y").to_matrix().to_4x4()
        m = Matrix.Translation(b) @ rot @ Matrix.Translation((0, 0, h / 2))
        bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=segs, radius1=r, radius2=r if r_top is None else r_top,
                              depth=h, matrix=m)
    return make


def ball(c, r, segs=14, rings=9, squash=1.0):
    def make(bm):
        geom = bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=1.0)
        r3 = r if isinstance(r, (tuple, list)) else (r, r * squash, r)
        m = Matrix.Translation(U(*c)) @ Matrix.Diagonal((r3[0], r3[2], r3[1], 1.0))
        bmesh.ops.transform(bm, matrix=m, verts=geom["verts"])
    return make


def grid(x0, x1, z0, z1, nx, nz, height=lambda x, z: 0.0, down=False):
    """A horizontal sheet of nx x nz quads over Unity x0..x1, z0..z1, at height(x, z), facing up (or down)."""
    def make(bm):
        rows = []
        for j in range(nz + 1):
            z = z0 + (z1 - z0) * j / nz
            rows.append([bm.verts.new(U(x0 + (x1 - x0) * i / nx, height(x0 + (x1 - x0) * i / nx, z), z)) for i in range(nx + 1)])
        faces = []
        for j in range(nz):
            for i in range(nx):
                faces.append(bm.faces.new((rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i])))
        _orient(bm, faces, Vector((0, 0, -1 if down else 1)))
    return make


def wall(a, b, y0, y1, nu=1, nv=1, facing=None):
    """A vertical sheet between ground points a and b (Unity (x, z)), from y0 to y1, facing `facing` (a Unity direction;
    default: toward the ring's middle)."""
    def make(bm):
        ax, az = a
        bx, bz = b
        rows = []
        for j in range(nv + 1):
            y = y0 + (y1 - y0) * j / nv
            rows.append([bm.verts.new(U(ax + (bx - ax) * i / nu, y, az + (bz - az) * i / nu)) for i in range(nu + 1)])
        faces = [bm.faces.new((rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i])) for j in range(nv) for i in range(nu)]
        if facing is None:
            mx, mz = (ax + bx) / 2, (az + bz) / 2
            want = U(-mx, 0, -mz) - U(0, 0, 0)
        else:
            want = U(*facing) - U(0, 0, 0)
        _orient(bm, faces, want.normalized())
    return make


def prism(outline, origin, u, v, depth, facing, back=False):
    """A flat shape (a star, a disc, a panel, a flag): the 2D outline (in metres along the Unity directions u and v from
    origin) raised `depth` toward `facing` (the Unity direction its front looks to); back = also a back face (things seen
    from both sides: flags)."""
    def make(bm):
        o = Vector(origin)
        uu, vv = Vector(u).normalized(), Vector(v).normalized()
        n = Vector(facing).normalized()
        cx = sum(x for x, _ in outline) / len(outline)
        cy = sum(y for _, y in outline) / len(outline)
        front = [bm.verts.new(U(*(o + uu * x + vv * y + n * depth))) for x, y in outline]
        rear = [bm.verts.new(U(*(o + uu * x + vv * y))) for x, y in outline]
        want = U(*n) - U(0, 0, 0)
        c = bm.verts.new(U(*(o + n * depth + uu * cx + vv * cy)))
        faces = [bm.faces.new((c, front[k], front[(k + 1) % len(front)])) for k in range(len(front))]
        _orient(bm, faces, want)
        for k in range(len(front)):
            k2 = (k + 1) % len(front)
            f = bm.faces.new((front[k], rear[k], rear[k2], front[k2]))
            _orient(bm, [f], (front[k].co + front[k2].co) / 2 - c.co)
        if back:
            cb = bm.verts.new(U(*(o + uu * cx + vv * cy)))
            faces = [bm.faces.new((cb, rear[(k + 1) % len(rear)], rear[k])) for k in range(len(rear))]
            _orient(bm, faces, -want)
    return make


def star(r_out, r_in, n=5, rot=90.0):
    return [(math.cos(math.radians(rot + 180.0 * k / n)) * (r_out if k % 2 == 0 else r_in),
             math.sin(math.radians(rot + 180.0 * k / n)) * (r_out if k % 2 == 0 else r_in)) for k in range(2 * n)]


def circle(r, n=24):
    return [(math.cos(2 * math.pi * k / n) * r, math.sin(2 * math.pi * k / n) * r) for k in range(n)]


def rect(hw, hh):
    return [(-hw, -hh), (hw, -hh), (hw, hh), (-hw, hh)]


def rope(pts, r, segs=8):
    """A tube through Unity points."""
    return kit.tube([U(*p) for p in pts], r, segs=segs, sub=1, cap=False)


def sag(a, b, drop, n=8):
    """Points from a to b hanging `drop` in the middle (a rope, a wire)."""
    a, b = Vector(a), Vector(b)
    return [a.lerp(b, t) - Vector((0, drop * 4 * t * (1 - t), 0)) for t in (k / n for k in range(n + 1))]


# ------------------------------------------------------------------------------------------------- colour rules

def flat(c):
    return c


def by_y(stops, y0, y1, wobble=0.0, freq=0.6, seed=0.0):
    """The ramp by Unity height, nudged by noise."""
    off = Vector((seed, seed * 1.7, seed * 0.3))

    def rule(p, n):
        q = UP(p)
        t = (q.y - y0) / max(1e-6, y1 - y0) + wobble * noise(q * freq + off)
        return kit.ramp(stops, t)
    return rule


def noisy(a, b, freq=0.8, seed=0.0, bias=0.0):
    off = Vector((seed * 2.3, seed * 1.1, seed * 3.7))

    def rule(p, n):
        return mix(a, b, 0.5 + 0.5 * noise(UP(p) * freq + off) + bias)
    return rule


# ------------------------------------------------------------------------------------------------- the ring

RING_COLOURS = {
    # canvas, canvas edge, logo, logo2, apron, apron trim, apron print, posts, neutral pads, ropes (top, middle, bottom), ties,
    # steps
    "gym": dict(canvas="#3B7BE8", canvas_edge="#2A5BC0", logo="#FFC93C", logo2="#FFFFFF", apron="#1E2A5C", trim="#FFC93C",
                print="#2E6FE8", post="#C9CED9", neutral="#F4F2F8", ropes=("#E8403A", "#F4F2F8", "#2E6FE8"), tie="#1E2A5C",
                steps="#2A2D46"),
    "roof": dict(canvas="#3A3654", canvas_edge="#2A2640", logo="#FF7A4E", logo2="#FFC93C", apron="#1A1630", trim="#FF7A4E",
                 print="#FFC93C", post="#2A2D3E", neutral="#FFC93C", ropes=("#FFC93C", "#F4F2F8", "#FF7A4E"), tie="#1A1630",
                 steps="#3A3E5C"),
    # a warm canvas against the snow and the sky (white, the boxers and the summit melted into one bright haze)
    "mountain": dict(canvas="#D9483E", canvas_edge="#A8322C", logo="#FFFFFF", logo2="#FFC93C", apron="#7A5236", trim="#E9F2FF",
                     print="#5A3A26", post="#8A5A36", neutral="#E9F2FF", ropes=("#C9A26B", "#C9A26B", "#C9A26B"), tie="#6B4A32",
                     steps="#7A5236"),
    "stars": dict(canvas="#221A4A", canvas_edge="#7DF2FF", logo="#FF5AD2", logo2="#7DF2FF", apron="#120E2A", trim="#7DF2FF",
                  print="#FF5AD2", post="#5A6178", neutral="#7B4FB5", ropes=("#7DF2FF", "#FF5AD2", "#7DF2FF"), tie="#120E2A",
                  steps="#2A2650"),
}
HERO_CORNER, FOE_CORNER = "#2E6FE8", "#E8403A"


def ring(coll, stage):
    c = RING_COLOURS[stage]
    glow_ropes = stage == "stars"
    B = Build(f"Ring_{stage.capitalize()}")
    B.ao = 0.8
    # the platform: the apron's skirt down to the floor, a dark plinth at its foot
    B.add(box((0, 0.29, 0), (2 * EDGE, 0.58, 2 * EDGE), bevel=0.02), by_y([(0.0, mix(c["apron"], "#000000", 0.35)), (0.3, c["apron"]), (1.0, c["apron"])], 0.0, 0.58))
    B.add(box((0, 0.04, 0), (2 * EDGE + 0.12, 0.08, 2 * EDGE + 0.12), bevel=0.02), mix(c["apron"], "#000000", 0.5))
    # the skirt's trim band and its print (a star on each side, stripes beside it)
    for k in range(4):
        yaw = 90.0 * k
        d = Vector((math.sin(math.radians(yaw)), 0, math.cos(math.radians(yaw))))
        side = Vector((d.z, 0, -d.x))
        face_at = d * (EDGE + 0.001)
        B.add(box(tuple(face_at + Vector((0, 0.5, 0))), (2 * EDGE + 0.02, 0.06, 0.02), yaw=yaw), c["trim"])
        B.add(prism(star(0.17, 0.075), face_at + Vector((0, 0.27, 0)), side, Vector((0, 1, 0)), 0.012, facing=d), c["print"])
        for s in (-1, 1):
            for j in range(3):
                B.add(prism(rect(0.32, 0.018), face_at + side * s * (0.62 + 0.0) + Vector((0, 0.19 + 0.08 * j, 0)), side, Vector((0, 1, 0)), 0.01, facing=d),
                      c["print"] if j != 1 else c["trim"])
    # the canvas: brighter in the middle, where the lights are; a padded edge round it
    def canvas(p, n):
        q = UP(p)
        r = math.hypot(q.x, q.z) / EDGE
        return mix(mix(c["canvas"], "#FFFFFF", 0.06), c["canvas"], min(1.0, r * 1.1))
    B.add(grid(-EDGE + 0.08, EDGE - 0.08, -EDGE + 0.08, EDGE - 0.08, 12, 12, lambda x, z: RING_H), canvas)
    for k in range(4):
        yaw = 90.0 * k
        d = Vector((math.sin(math.radians(yaw)), 0, math.cos(math.radians(yaw))))
        B.add(box(tuple(d * (EDGE - 0.04) + Vector((0, RING_H - 0.01, 0))), (2 * EDGE, 0.06, 0.09), yaw=yaw, bevel=0.025), c["canvas_edge"],
              glow=0.6 if glow_ropes else 0.0)
    # the logo in the middle: a ring, a star
    B.add(prism(circle(1.0, 40), Vector((0, RING_H, 0)), Vector((1, 0, 0)), Vector((0, 0, 1)), 0.004, facing=(0, 1, 0)), c["logo"],
          glow=0.35 if glow_ropes else 0.0)
    B.add(prism(circle(0.86, 40), Vector((0, RING_H, 0)), Vector((1, 0, 0)), Vector((0, 0, 1)), 0.006, facing=(0, 1, 0)), mix(c["canvas"], "#FFFFFF", 0.1))
    B.add(prism(star(0.72, 0.3), Vector((0, RING_H, 0)), Vector((1, 0, 0)), Vector((0, 0, 1)), 0.008, facing=(0, 1, 0)), c["logo"],
          glow=0.35 if glow_ropes else 0.0)
    B.add(prism(star(0.4, 0.17), Vector((0, RING_H, 0)), Vector((1, 0, 0)), Vector((0, 0, 1)), 0.01, facing=(0, 1, 0)), c["logo2"])
    # the posts and their pads: the hero's corner (-, -) blue, the opponent's (+, +) red, the others neutral
    corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    for sx, sz in corners:
        base = (sx * POST, RING_H, sz * POST)
        B.add(cyl(base, 0.06, POST_TOP + 0.02, 12), c["post"])
        B.add(ball((sx * POST, RING_H + POST_TOP + 0.03, sz * POST), 0.075), c["post"])
        pad = HERO_CORNER if (sx, sz) == (-1, -1) else FOE_CORNER if (sx, sz) == (1, 1) else c["neutral"]
        yaw = math.degrees(math.atan2(sx, sz))
        B.add(box((sx * (POST - 0.06), RING_H + 0.68, sz * (POST - 0.06)), (0.26, 0.98, 0.16), yaw=yaw, bevel=0.06),
              by_y([(0.0, mix(pad, "#000000", 0.18)), (0.5, pad), (1.0, mix(pad, "#FFFFFF", 0.15))], RING_H + 0.2, RING_H + 1.17))
        for y in (RING_H + 0.36, RING_H + 0.68, RING_H + 1.0):
            B.add(box((sx * (POST - 0.12), y, sz * (POST - 0.12)), (0.28, 0.035, 0.035), yaw=yaw), mix(pad, "#000000", 0.35))
    # the ropes, three a side, sagging a little, ties at their middles
    for k in range(4):
        a = corners[k]
        b = corners[(k + 1) % 4]
        for j, h in enumerate(ROPES):
            pa = (a[0] * (POST - 0.1), RING_H + h, a[1] * (POST - 0.1))
            pb = (b[0] * (POST - 0.1), RING_H + h, b[1] * (POST - 0.1))
            B.add(rope(sag(pa, pb, 0.022, 10), 0.034, 10), c["ropes"][j], glow=0.85 if glow_ropes else 0.0)
        mid = Vector(((a[0] + b[0]) * 0.5 * (POST - 0.1), 0, (a[1] + b[1]) * 0.5 * (POST - 0.1)))
        for t in (-0.32, 0.32):
            along = Vector((b[0] - a[0], 0, b[1] - a[1])).normalized() * t * POST
            x, z = (mid + along).x, (mid + along).z
            B.add(box((x, RING_H + 0.68, z), (0.07 if abs(a[0] - b[0]) < 1 else 0.05, 0.7, 0.07 if abs(a[1] - b[1]) < 1 else 0.05)), c["tie"])
    # steps at the two fighters' corners, outside
    for sx, sz in ((-1, -1), (1, 1)):
        for j in range(3):
            d = 0.42 + j * 0.3
            B.add(box((sx * (EDGE + d * 0.72), 0.1 + j * 0.18, sz * (EDGE + d * 0.72)), (0.62, 0.2 + j * 0.36, 0.34),
                      yaw=math.degrees(math.atan2(sx, sz)) + 90.0, bevel=0.02), c["steps"])
    # the corners' stools and buckets on the floor beside them
    for sx, sz, colour in ((-1, -1, HERO_CORNER), (1, 1, FOE_CORNER)):
        at = Vector((sx * (EDGE + 0.55), 0, sz * (EDGE - 0.6)))
        B.add(cyl((at.x, 0.0, at.z), 0.2, 0.46, 14), mix(colour, "#000000", 0.25))
        B.add(cyl((at.x, 0.46, at.z), 0.24, 0.07, 14), colour)
        bk = at + Vector((sx * 0.15, 0, -sz * 0.55))
        B.add(cyl((bk.x, 0.0, bk.z), 0.16, 0.3, 12, r_top=0.19), "#C9CED9")
        B.add(cyl((bk.x, 0.3, bk.z), 0.185, 0.02, 12), "#7DB8F2")
    return B.to_object(coll)


# ------------------------------------------------------------------------------------------------- the crowd's seats

SEATS = {}


def seat(stage, x, y, z, yaw):
    SEATS.setdefault(stage, []).append((x, y, z, yaw))


# ------------------------------------------------------------------------------------------------- the gym

GYM_ROWS = 6


def gym(coll):
    """The neighbourhood's boxing gym at night: the ring under a lighting rig in the middle of a hall, bleachers on three
    sides (the crowd), the gym's own things behind the hero (bags, a speed bag, a rack), big arched windows on the city
    at the far end, banners, a hanging scoreboard."""
    rnd = random.Random(7)
    B = FastBuild("Gym_Set")
    B.ao = 1.2
    W, D, H = 15.0, 15.0, 10.0      # half width, half depth, height
    # the floor: polished boards, darker toward the walls, a pool of the lights' warmth round the ring
    def floor(p, n):
        q = UP(p)
        plank = (math.floor((q.x + 40) / 0.45) * 7.3) % 1.0
        wood = mix("#6E4732", "#8E5E40", 0.25 + 0.5 * plank + 0.15 * noise(Vector((q.x * 2.2, q.z * 0.15, 0.0))))
        r = max(abs(q.x), abs(q.z))
        wood = mix(mix(wood, "#F2C58E", 0.22), wood, min(1.0, max(0.0, (r - 3.0) / 3.2)))
        return mix(wood, "#3A2A2E", min(1.0, max(0.0, (r - 9.0) / 6.0)) * 0.6)
    B.add(grid(-W, W, -D, D, 30, 30), floor)
    # a dark mat round the ring with a gold line
    B.add(grid(-4.1, 4.1, -4.1, 4.1, 8, 8, lambda x, z: 0.01), "#1E2440")
    for k in range(4):
        yaw = 90.0 * k
        d = Vector((math.sin(math.radians(yaw)), 0, math.cos(math.radians(yaw))))
        B.add(box(tuple(d * 4.05 + Vector((0, 0.015, 0))), (8.2, 0.012, 0.08), yaw=yaw), "#FFC93C", glow=0.2)
    # the barrier round the floor in front of the bleachers, its boards glowing in the brand's colours
    boards = ["#E8403A", "#2E6FE8", "#FFC93C", "#7B4FB5", "#3E9A4E"]
    for k, (cx, cz, yaw, length) in enumerate([(0, 5.0, 0, 10.4), (-5.0, 0, -90, 10.4), (5.0, 0, 90, 10.4)]):
        d = Vector((math.sin(math.radians(yaw)), 0, math.cos(math.radians(yaw))))
        side = Vector((d.z, 0, -d.x))
        B.add(box((cx, 0.45, cz), (length, 0.9, 0.16), yaw=yaw, bevel=0.03), "#1E2440")
        n = 5
        for i in range(n):
            at = Vector((cx, 0.5, cz)) + side * ((i - (n - 1) / 2) * (length / n)) - Vector((cx, 0, cz)).normalized() * 0.085
            colour = boards[(i + k) % len(boards)]
            B.add(prism(rect(length / n / 2 - 0.08, 0.28), at, side, Vector((0, 1, 0)), 0.01, facing=-d), colour, glow=0.45)
            B.add(prism(star(0.16, 0.07), at + Vector((0, 0, 0)) - Vector((cx, 0, cz)).normalized() * 0.012, side, Vector((0, 1, 0)), 0.01, facing=-d),
                  "#FFFFFF", glow=0.6)
    # the bleachers: far side (behind the opponent) and both sides, rows rising, seats in the corners' colours
    def bleacher(cx, cz, yaw, length, rows, colour_a, colour_b, stage_seats=True):
        d = Vector((math.sin(math.radians(yaw)), 0, math.cos(math.radians(yaw))))   # away from the ring
        side = Vector((d.z, 0, -d.x))
        base = Vector((cx, 0, cz))
        for r in range(rows):
            out = 0.6 + r * 0.85
            y = 0.42 + r * 0.46
            centre = base + d * out
            B.add(box(tuple(centre + Vector((0, y / 2, 0))), (length, y, 0.86), yaw=yaw, bevel=0.03),
                  by_y([(0.0, "#26243C"), (1.0, "#3A3858")], 0.0, y))
            # the seat line: a coloured band at the step's front edge
            B.add(box(tuple(centre - d * 0.25 + Vector((0, y + 0.05, 0))), (length - 0.1, 0.1, 0.32), yaw=yaw, bevel=0.02),
                  colour_a if r % 2 == 0 else colour_b)
            if stage_seats:
                xs = int(length / 0.62)
                for i in range(xs):
                    t = (i - (xs - 1) / 2) * 0.62 + rnd.uniform(-0.06, 0.06)
                    pos = centre - d * 0.2 + side * t
                    if rnd.random() < 0.9:
                        seat("gym", pos.x, y + 0.1, pos.z, (yaw + 180.0) % 360.0)
        # the railing along its front
        top = base + d * 0.25
        B.add(box(tuple(top + Vector((0, 1.0, 0))), (length, 0.06, 0.06), yaw=yaw), "#C9CED9")
        for i in range(int(length / 2.0) + 1):
            t = (i - int(length / 2.0) / 2) * 2.0
            p = top + side * t
            B.add(box((p.x, 0.5, p.z), (0.05, 1.0, 0.05)), "#9AA0B4")
    bleacher(0, 5.4, 0, 13.0, GYM_ROWS, "#E8403A", "#C8343C")
    bleacher(-5.4, 0.6, -90, 9.0, GYM_ROWS - 1, "#2E6FE8", "#2858C8")
    bleacher(5.4, 0.6, 90, 9.0, GYM_ROWS - 1, "#2E6FE8", "#2858C8")
    # the walls: plum, darker toward the ceiling, over a dark dado; pilasters every few metres, a lamp on each
    def plum(p, n):
        q = UP(p)
        c = mix("#5A3A5E", "#6A4468", 0.5 + 0.5 * noise(q * 0.35))
        return mix(c, "#1E1630", min(1.0, max(0.0, (q.y - 3.0) / 6.0)))
    for (a, b) in [((-W, D), (W, D)), ((-W, -D), (-W, D)), ((W, D), (W, -D)), ((W, -D), (-W, -D))]:
        B.add(wall(a, b, 2.2, H, 30, 12), plum)
        B.add(wall(a, b, 0.0, 2.2, 20, 2), by_y([(0.0, "#1E1A2C"), (1.0, "#2E2840")], 0.0, 2.2))
    for k in range(4):
        yaw = 90.0 * k
        d = Vector((math.sin(math.radians(yaw)), 0, math.cos(math.radians(yaw))))
        side = Vector((d.z, 0, -d.x))
        B.add(box(tuple(d * (D - 0.05) + Vector((0, 2.2, 0))), (2 * W, 0.1, 0.1), yaw=yaw), "#FFC93C", glow=0.15)
        for i in range(-5, 6):
            if k == 0 and i % 2 == 0 and abs(i) <= 4:
                continue   # the far wall's windows go there
            at = d * (D - 0.2) + side * (i * 2.7)
            B.add(box((at.x, H / 2, at.z), (0.5, H, 0.4), yaw=yaw, bevel=0.04), by_y([(0.0, "#2E2440"), (0.4, "#4A3458"), (1.0, "#241A34")], 0.0, H))
            B.add(box(tuple(at - d * 0.25 + Vector((0, 4.6, 0))), (0.34, 0.5, 0.12), yaw=yaw, bevel=0.03), "#FFE2A8", glow=1.0)
    # the far wall's arched windows on the night city (the glass glows faintly, the city's windows brightly)
    for i in range(-2, 3):
        cx = i * 5.4
        wb, wt = 3.3, 8.3
        B.add(prism(arch(1.6, wt - wb), Vector((cx, wb, D - 0.06)), Vector((-1, 0, 0)), Vector((0, 1, 0)), 0.04, facing=(0, 0, -1)),
              by_y([(0.0, "#1C2A5E"), (0.7, "#2C2A6A"), (1.0, "#4A3A8A")], wb, wt), glow=0.55)
        B.add(prism(arch_frame(1.6, wt - wb, 0.12), Vector((cx, wb, D - 0.1)), Vector((-1, 0, 0)), Vector((0, 1, 0)), 0.06, facing=(0, 0, -1)), "#3A2E4A")
        B.add(box((cx, wb + (wt - wb) * 0.42, D - 0.12), (0.08, (wt - wb) * 0.84, 0.06)), "#3A2E4A")
        B.add(box((cx, wb + 1.6, D - 0.12), (3.2, 0.08, 0.06)), "#3A2E4A")
        B.add(box((cx, wb - 0.1, D - 0.2), (3.6, 0.16, 0.3), bevel=0.03), "#4A3A56")
        # the city through it: towers' silhouettes, lit windows
        for t in range(5):
            tx = cx + rnd.uniform(-1.3, 1.3)
            th = rnd.uniform(1.0, 2.6)
            B.add(prism(rect(rnd.uniform(0.18, 0.35), th / 2), Vector((tx, wb + th / 2, D - 0.08)), Vector((-1, 0, 0)), Vector((0, 1, 0)), 0.01, facing=(0, 0, -1)),
                  "#121638", glow=0.6)
            for w_ in range(rnd.randint(2, 5)):
                B.add(prism(rect(0.035, 0.05), Vector((tx + rnd.uniform(-0.12, 0.12), wb + rnd.uniform(0.2, th - 0.1), D - 0.075)),
                            Vector((-1, 0, 0)), Vector((0, 1, 0)), 0.006, facing=(0, 0, -1)), rnd.choice(["#FFD36A", "#FFE6A0", "#7DF2FF"]), glow=1.0)
    # banners hanging along the side walls: brand colours, a star, a gold fringe
    for side in (-1, 1):
        for j, colour in enumerate(["#E8403A", "#2E6FE8", "#FFC93C", "#7B4FB5"]):
            z = -6.0 + j * 4.0
            x = side * (W - 0.15)
            facing = (-side, 0, 0)
            u = Vector((0, 0, side))
            B.add(prism(banner(0.75, 2.6), Vector((x, 8.4, z)), u, Vector((0, 1, 0)), 0.03, facing=facing), colour)
            B.add(prism(star(0.42, 0.18), Vector((x - side * 0.035, 7.3, z)), u, Vector((0, 1, 0)), 0.01, facing=facing), "#FFFFFF")
            B.add(box((x - side * 0.03, 8.45, z), (0.06, 0.08, 1.7)), "#FFC93C")
    # the ceiling: dark, steel trusses across it
    B.add(grid(-W, W, -D, D, 6, 6, lambda x, z: H, down=True), "#16142A")
    for i in range(-3, 4):
        B.add(box((i * 4.3, H - 0.5, 0), (0.35, 0.7, 2 * D), bevel=0.03), "#2E2C46")
        for j in range(-7, 8):
            B.add(box((i * 4.3, H - 0.5, j * 2.0), (0.32, 0.06, 0.9), pitch=45.0 * (1 if j % 2 else -1)), "#3A3858")
    # the lighting rig over the ring: a square of trusses on cables, eight lamps aimed at the canvas
    rig_y = 5.3
    for k in range(4):
        yaw = 90.0 * k
        d = Vector((math.sin(math.radians(yaw)), 0, math.cos(math.radians(yaw))))
        side = Vector((d.z, 0, -d.x))
        c = d * 3.0 + Vector((0, rig_y, 0))
        for dy in (0.0, 0.42):
            for ds in (-0.2, 0.2):
                p = c + Vector((0, dy, 0)) + d * ds
                B.add(box(tuple(p), (6.4, 0.06, 0.06), yaw=yaw), "#3A3E58")
        for i in range(-6, 7):
            p = c + side * (i * 0.5) + Vector((0, 0.21, 0))
            B.add(box(tuple(p), (0.04, 0.5, 0.42), yaw=yaw, roll=35.0 * (1 if i % 2 else -1)), "#4A4E6A")
    for sx in (-1, 1):
        for sz in (-1, 1):
            B.add(cyl((sx * 3.0, rig_y + 0.42, sz * 3.0), 0.015, H - rig_y - 0.6, 4), "#5A5E78")
    for k, (lx, lz) in enumerate([(-1.6, -3.0), (1.6, -3.0), (-1.6, 3.0), (1.6, 3.0), (-3.0, -1.6), (-3.0, 1.6), (3.0, -1.6), (3.0, 1.6)]):
        lamp = Vector((lx, rig_y - 0.25, lz))
        aim = (Vector((lx * 0.15, RING_H, lz * 0.15)) - lamp).normalized()
        B.add(cyl(tuple(lamp), 0.17, 0.42, 12, r_top=0.21, axis=tuple(aim)), "#262838")
        B.add(cyl(tuple(lamp + aim * 0.42), 0.19, 0.03, 12, axis=tuple(aim)), "#FFF2C8", glow=1.0)
    # the scoreboard hanging over the rig: four screens, glowing edges, the star
    sb = Vector((0, 7.6, 0))
    B.add(box(tuple(sb), (2.2, 1.3, 2.2), bevel=0.06), "#1E1A2C")
    for k in range(4):
        yaw = 90.0 * k
        d = Vector((math.sin(math.radians(yaw)), 0, math.cos(math.radians(yaw))))
        side = Vector((d.z, 0, -d.x))
        f = sb + d * 1.105
        B.add(prism(rect(0.95, 0.52), f, side, Vector((0, 1, 0)), 0.01, facing=d), by_y([(0.0, "#1A2A6A"), (1.0, "#3A2A8A")], 7.1, 8.1), glow=0.7)
        B.add(prism(star(0.32, 0.14), f + d * 0.012, side, Vector((0, 1, 0)), 0.01, facing=d), "#FFC93C", glow=1.0)
        for j in (-1, 1):
            B.add(box(tuple(f + Vector((0, j * 0.6, 0))), (2.2, 0.05, 0.02), yaw=yaw), "#7DF2FF", glow=1.0)
    B.add(cyl((0, 8.25, 0), 0.03, H - 8.25, 4), "#5A5E78")
    # behind the hero: the gym's own things: heavy bags on chains, a speed bag, a rack of dumbbells, a bench, mats
    for i, x in enumerate((-7.5, -4.5, 4.5, 7.5)):
        z = -10.0 + (i % 2) * 0.6
        B.add(cyl((x, 0.95, z), 0.32, 1.35, 16), by_y([(0.0, "#A82A34"), (0.5, "#D8343C"), (1.0, "#A82A34")], 0.95, 2.3))
        B.add(cyl((x, 0.88, z), 0.3, 0.08, 16), "#1E1A2C")
        B.add(cyl((x, 2.3, z), 0.3, 0.07, 16), "#1E1A2C")
        B.add(cyl((x, 2.37, z), 0.012, H - 2.37, 4), "#9AA0B4")
    B.add(box((-9.5, 1.6, -13.8), (2.4, 0.08, 0.6)), "#5A3A26")
    B.add(ball((-9.5, 1.38, -13.6), (0.16, 0.22, 0.16)), "#D8343C")
    B.add(box((9.5, 0.45, -13.4), (3.0, 0.9, 0.6), bevel=0.03), "#2E2C46")
    for i in range(7):
        B.add(cyl((8.3 + i * 0.4, 0.95, -13.4), 0.09, 0.16, 10, axis=(1, 0, 0)), "#3A3E58")
    for i in range(3):
        B.add(box((-3 + i * 3.0, 0.03, -12.5), (2.6, 0.06, 1.4), bevel=0.02), "#2E6FE8" if i % 2 else "#E8403A")
    # posters on the near wall (behind the hero): the champions of the climb, framed
    for i, colour in enumerate(["#E8403A", "#FFC93C", "#31C6F4", "#7B4FB5"]):
        x = -9.0 + i * 6.0
        B.add(prism(rect(0.9, 1.25), Vector((x, 4.2, -D + 0.07)), Vector((1, 0, 0)), Vector((0, 1, 0)), 0.02, facing=(0, 0, 1)),
              by_y([(0.0, mix(colour, "#000000", 0.35)), (1.0, colour)], 2.95, 5.45))
        B.add(prism(star(0.42, 0.18), Vector((x, 4.35, -D + 0.1)), Vector((1, 0, 0)), Vector((0, 1, 0)), 0.01, facing=(0, 0, 1)), "#FFFFFF")
    # an exit sign over the near door
    B.add(box((0, 3.6, -D + 0.1), (1.0, 0.36, 0.12), bevel=0.02), "#3E9A4E", glow=0.9)
    B.add(box((0, 1.25, -D + 0.05), (2.2, 2.5, 0.1)), "#2A2238")
    objs = [B.to_object(coll)]
    # the beams from the rig's lamps to the canvas, and halos round the lamps (additive)
    G = FastBuild("Gym_Beams")
    for lx, lz in [(-1.6, -3.0), (1.6, -3.0), (-1.6, 3.0), (1.6, 3.0), (-3.0, -1.6), (-3.0, 1.6), (3.0, -1.6), (3.0, 1.6)]:
        lamp = Vector((lx, rig_y - 0.25, lz))
        target = Vector((lx * 0.15, RING_H, lz * 0.15))
        G.add(beam(lamp, target, 0.2, 0.95), by_y([(0.0, "#3A3020"), (0.6, "#B8A47C"), (1.0, "#FFE8B8")], RING_H, rig_y))
    objs.append(G.to_object(coll))
    return objs


def arch(hw, h, n=14):
    """A window's outline: straight sides, a round top."""
    pts = [(-hw, 0.0), (hw, 0.0), (hw, h - hw)]
    for k in range(1, n):
        a = math.pi * k / n
        pts.append((math.cos(a) * hw, h - hw + math.sin(a) * hw))
    pts.append((-hw, h - hw))
    return pts


def arch_frame(hw, h, t, n=14):
    """The frame round an arched window (a thin band along its outline, as one outline going round and back)."""
    outer = arch(hw + t, h + t, n)
    return [(x, y - t) for x, y in outer]


def banner(hw, h):
    """A hanging banner: a rectangle with a V cut at the bottom (origin = its top middle)."""
    return [(-hw, 0.0), (-hw, -h), (0.0, -h + 0.45), (hw, -h), (hw, 0.0)]


def beam(a, b, r0, r1, segs=14, rings=6):
    """A cone of light from a to b, open, in rings along its length: Tundu/ArenaGlow fades it at the vertices (by how
    squarely the eye looks through it and how near the eye is) and draws its near side only; the rings carry those fades
    smoothly. (Two rings faded per pixel, both sides, cost the box's GPU ~35 ms a frame in the gym's fight, 2026-11-19.)"""
    def make(bm):
        A, Bv = Vector(a), Vector(b)
        ax = (Bv - A).normalized()
        ref = Vector((0, 1, 0)) if abs(ax.y) < 0.9 else Vector((1, 0, 0))
        u = ax.cross(ref).normalized()
        w = ax.cross(u).normalized()
        rows = []
        for j in range(rings + 1):
            t = j / rings
            c = A.lerp(Bv, t)
            r = r0 + (r1 - r0) * t
            rows.append([bm.verts.new(U(*(c + (u * math.cos(2 * math.pi * k / segs) + w * math.sin(2 * math.pi * k / segs)) * r)))
                         for k in range(segs)])
        for j in range(rings):
            axis_pt = U(*A.lerp(Bv, (j + 0.5) / rings))
            for k in range(segs):
                k2 = (k + 1) % segs
                f = bm.faces.new((rows[j][k], rows[j][k2], rows[j + 1][k2], rows[j + 1][k]))
                mid = (rows[j][k].co + rows[j + 1][k2].co) / 2
                _orient(bm, [f], mid - axis_pt)
    return make


# ------------------------------------------------------------------------------------------------- the roof

def roof(coll):
    """The ring on a tower's roof at sunset: the deck and its parapet, string lights round the ring, a water tower, air
    conditioners and vents, a stairwell hut, a lattice antenna (its light is Roof_Beacon), a billboard behind the
    opponent; the city's towers round it are Roof_Far, the sky Roof_Sky."""
    rnd = random.Random(11)
    B = FastBuild("Roof_Set")
    B.ao = 1.0
    E = 11.0
    def deck(p, n):
        q = UP(p)
        ix, iz = math.floor(q.x / 1.1), math.floor(q.z / 1.1)
        tile = mix("#857A8E", "#776C82", 0.5 + 0.5 * math.sin(ix * 12.9898 + iz * 78.233))
        seam = min(abs(q.x / 1.1 - round(q.x / 1.1)), abs(q.z / 1.1 - round(q.z / 1.1))) < 0.04
        tile = mix(tile, "#5A5068", 0.6 if seam else 0.0)
        # the sun's warmth from behind the opponent (+z)
        return mix(tile, "#C48A7E", max(0.0, min(1.0, (q.z + 2.0) / 14.0)) * 0.25)
    B.add(grid(-E, E, -E, E, 44, 44), deck)
    # the parapet round the edge: its top in hazard stripes
    for k in range(4):
        yaw = 90.0 * k
        d = Vector((math.sin(math.radians(yaw)), 0, math.cos(math.radians(yaw))))
        B.add(box(tuple(d * (E + 0.2) + Vector((0, 0.55, 0))), (2 * E + 0.8, 1.1, 0.4), yaw=yaw, bevel=0.03),
              by_y([(0.0, "#4E4660"), (1.0, "#6A6080")], 0.0, 1.1))
        side = Vector((d.z, 0, -d.x))
        for i in range(-22, 23):
            p = d * (E + 0.2) + side * (i * 0.5) + Vector((0, 1.13, 0))
            B.add(box(tuple(p), (0.5, 0.06, 0.44), yaw=yaw), "#FFC93C" if i % 2 == 0 else "#1E1A2C")
    # the water tower (back left): a barrel on legs, its bands, a cone roof
    wt = Vector((-7.2, 0, 7.4))
    for sx in (-1, 1):
        for sz in (-1, 1):
            B.add(cyl((wt.x + sx * 0.9, 0, wt.z + sz * 0.9), 0.07, 3.2, 6), "#3A3550")
    B.add(cyl((wt.x, 3.2, wt.z), 1.35, 2.4, 18), by_y([(0.0, "#7A4E32"), (1.0, "#A86E46")], 3.2, 5.6))
    for j in range(4):
        B.add(cyl((wt.x, 3.35 + j * 0.65, wt.z), 1.38, 0.06, 18), "#2E2A40")
    B.add(cyl((wt.x, 5.6, wt.z), 1.5, 0.9, 18, r_top=0.05), "#3A3550")
    # air conditioners and vents along the right
    for i in range(3):
        p = Vector((7.6, 0, -5.0 + i * 3.2))
        B.add(box((p.x, 0.55, p.z), (1.6, 1.1, 1.4), bevel=0.05), by_y([(0.0, "#8E8AA0"), (1.0, "#C2BED2")], 0.0, 1.1))
        B.add(cyl((p.x, 1.1, p.z), 0.48, 0.05, 16), "#2E2A40")
        for b in range(3):
            B.add(box((p.x, 1.14, p.z), (0.9, 0.02, 0.06), yaw=60.0 * b), "#5A5670")
    for i in range(4):
        p = Vector((-8.8 + i * 1.3, 0, -3.0 - (i % 2) * 1.6))
        B.add(cyl((p.x, 0, p.z), 0.14, 1.1 + 0.3 * (i % 2), 10), "#A8A4BA")
        B.add(cyl((p.x, 1.1 + 0.3 * (i % 2), p.z), 0.22, 0.12, 10, r_top=0.1), "#6E6A82")
    # the stairwell hut (near right), a lamp over its door
    hut = Vector((7.0, 0, -8.6))
    B.add(box((hut.x, 1.4, hut.z), (3.0, 2.8, 2.4), bevel=0.05), by_y([(0.0, "#5E5470"), (1.0, "#7A6E8C")], 0.0, 2.8))
    B.add(box((hut.x, 2.9, hut.z), (3.3, 0.2, 2.7), bevel=0.04), "#3A3550")
    B.add(box((hut.x - 0.4, 1.0, hut.z + 1.21), (0.9, 2.0, 0.04)), "#2E2A40")
    B.add(ball((hut.x - 0.4, 2.3, hut.z + 1.3), 0.11), "#FFE6A0", glow=1.0)
    # the lattice antenna (back right): four legs, rungs, the beacon on top (its own object)
    an = Vector((7.8, 0, 8.2))
    top_h = 9.5
    for sx in (-1, 1):
        for sz in (-1, 1):
            B.add(rope([(an.x + sx * 0.6, 0.0, an.z + sz * 0.6), (an.x + sx * 0.08, top_h, an.z + sz * 0.08)], 0.04, 5), "#B0ACC2")
    for j in range(9):
        y = 0.6 + j * 0.98
        w = 0.6 - 0.52 * y / top_h
        for k in range(4):
            yaw = 90.0 * k
            d = Vector((math.sin(math.radians(yaw)), 0, math.cos(math.radians(yaw))))
            B.add(box(tuple(an + d * w + Vector((0, y, 0))), (2 * w, 0.03, 0.03), yaw=yaw), "#B0ACC2")
    # the billboard behind the opponent: scaffold, a lit face (a boxer's star, stripes in the brand's colours), lamps over it
    bb = Vector((0, 4.6, 10.2))
    for x in (-3.0, -1.0, 1.0, 3.0):
        B.add(box((x, 1.7, bb.z + 0.4), (0.12, 3.4, 0.12)), "#2E2A40")
    B.add(box((0, 3.3, bb.z + 0.4), (7.0, 0.1, 0.1)), "#2E2A40")
    B.add(box(tuple(bb), (7.6, 2.9, 0.3), bevel=0.05), "#1E1A2C")
    face = bb - Vector((0, 0, 0.16))
    B.add(prism(rect(3.6, 1.28), face, Vector((-1, 0, 0)), Vector((0, 1, 0)), 0.01, facing=(0, 0, -1)),
          by_y([(0.0, "#E8403A"), (1.0, "#FF8A5A")], bb.y - 1.3, bb.y + 1.3), glow=0.55)
    B.add(prism(star(1.0, 0.42), face - Vector((-2.0, 0, 0.012)), Vector((-1, 0, 0)), Vector((0, 1, 0)), 0.01, facing=(0, 0, -1)), "#FFC93C", glow=0.8)
    for j, colour in enumerate(["#FFFFFF", "#FFC93C", "#1E1A2C"]):
        B.add(prism(rect(1.5, 0.12), face - Vector((1.0, -0.5 + j * 0.45, 0.012)), Vector((-1, 0, 0)), Vector((0, 1, 0)), 0.01, facing=(0, 0, -1)), colour,
              glow=0.6 if colour != "#1E1A2C" else 0.0)
    for x in (-2.6, 0.0, 2.6):
        B.add(cyl((x, bb.y + 1.55, bb.z - 0.3), 0.08, 0.45, 8, axis=(0, -0.3, -1)), "#2E2A40")
    # the string lights round the ring: poles at its corners and halfway, wires sagging between, bulbs along them
    poles = [Vector((sx * 4.4, 0, sz * 4.4)) for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    for p in poles:
        B.add(cyl((p.x, 0, p.z), 0.06, 3.6, 8), "#2E2A40")
        B.add(ball((p.x, 3.62, p.z), 0.09), "#2E2A40")
    bulbs = []
    for k in range(4):
        a, b = poles[k], poles[(k + 1) % 4]
        pts = sag((a.x, 3.5, a.z), (b.x, 3.5, b.z), 0.55, 16)
        B.add(rope(pts, 0.012, 4), "#1E1A2C")
        for i in range(1, 16):
            q = pts[i]
            bulbs.append(q)
            B.add(ball((q.x, q.y - 0.07, q.z), 0.055, 8, 6), rnd.choice(["#FFE6A0", "#FFD36A", "#FF9A6A", "#FFF2C8"]), glow=1.0)
    # crates and a couple of benches where people sit to watch
    for i in range(4):
        x = (-1 if i < 2 else 1) * (6.2 + (i % 2) * 0.9)
        B.add(box((x, 0.3, 2.2 - (i % 2) * 1.1), (0.8, 0.6, 0.8), yaw=rnd.uniform(-12, 12), bevel=0.04), "#A8724A")
    for side in (-1, 1):
        for r in range(2):
            x = side * (5.6 + r * 0.95)
            B.add(box((x, 0.42, -1.0), (0.5, 0.08, 6.0), bevel=0.02), "#8A5A3C")
            for z in (-3.6, -1.0, 1.6):
                B.add(box((x, 0.2, z), (0.42, 0.4, 0.08)), "#3A3550")
            for i in range(9):
                if rnd.random() < 0.85:
                    seat("roof", x, 0.46, -3.6 + i * 0.66 + rnd.uniform(-0.05, 0.05), 90.0 if side > 0 else 270.0)
    objs = [B.to_object(coll)]
    # the beacon (blinks in the game)
    L = FastBuild("Roof_Beacon")
    L.add(ball((an.x, top_h + 0.15, an.z), 0.16), "#FF3A3A", glow=1.0)
    objs.append(L.to_object(coll))
    # halos round the bulbs and the billboard's lamps (additive)
    G = FastBuild("Roof_Beams")
    for q in bulbs[::2]:
        G.add(halo((q.x, q.y - 0.07, q.z), 0.28), "#FFC88A")
    for x in (-2.6, 0.0, 2.6):
        G.add(beam(Vector((x, bb.y + 1.4, bb.z - 0.45)), Vector((x, bb.y - 0.4, bb.z - 0.2)), 0.12, 0.9, 10),
              by_y([(0.0, "#3A2E20"), (1.0, "#FFE6B0")], bb.y - 0.4, bb.y + 1.4))
    objs.append(G.to_object(coll))
    objs.append(roof_far(coll))
    objs.append(sky_dome(coll, "Roof_Sky", roof_sky_colour))
    return objs


def halo(c, r, segs=10):
    """A glow round a bulb: a flat disc, one side toward the fight's camera (-z) and one away (the shader draws front
    faces only; a face needs its own vertices)."""
    def make(bm):
        for facing in (-1, 1):
            centre = bm.verts.new(U(*c))
            ring_ = [bm.verts.new(U(c[0] + math.cos(2 * math.pi * k / segs) * r, c[1] + math.sin(2 * math.pi * k / segs) * r, c[2]))
                     for k in range(segs)]
            faces = [bm.faces.new((centre, ring_[k], ring_[(k + 1) % segs])) for k in range(segs)]
            _orient(bm, faces, U(0, 0, facing) - U(0, 0, 0))
    return make


def roof_far(coll):
    """The city round the tower: blocks of every height (the ones behind the opponent tallest), lit windows on the sides
    facing the ring, the sunset's glow on the faces toward the sun (+z), water tanks and antennas on their roofs."""
    rnd = random.Random(23)
    B = FastBuild("Roof_Far")
    B.ao = 4.0
    walls = ["#4A3E6A", "#5A4878", "#3E3460", "#6A4E7E", "#54406E"]
    for i in range(70):
        a = rnd.uniform(-180, 180)
        r = rnd.uniform(26, 120)
        x, z = math.sin(math.radians(a)) * r, math.cos(math.radians(a)) * r
        w, d = rnd.uniform(5, 11), rnd.uniform(5, 11)
        top = rnd.uniform(-14, 28) + (8 if z > 20 else 0)
        base = -40.0
        h = top - base
        yaw = rnd.uniform(-20, 20)
        colour = rnd.choice(walls)
        def wall_rule(p, n, colour=colour, top=top):
            q = UP(p)
            nn = Vector((-n.x, n.z, -n.y))
            t = max(0.0, min(1.0, (q.y + 30.0) / (top + 30.0)))
            c = mix(mix(colour, "#000000", 0.45), colour, t)
            # faces toward the sunset glow warm
            return mix(c, "#E8806A", max(0.0, nn.z) * 0.45 * t)
        B.add(box((x, base + h / 2, z), (w, h, d), yaw=yaw, bevel=0.15), wall_rule)
        B.add(box((x, top + 0.3, z), (w * 0.94, 0.6, d * 0.94), yaw=yaw), mix(colour, "#FFFFFF", 0.12))
        if rnd.random() < 0.4:
            B.add(cyl((x + rnd.uniform(-1, 1), top + 0.6, z + rnd.uniform(-1, 1)), 1.0, 2.0, 10), "#3A2E4A")
        if rnd.random() < 0.3:
            B.add(cyl((x, top + 0.6, z), 0.08, rnd.uniform(4, 8), 4), "#2A2238")
        # lit windows on the side facing the ring
        to_ring = Vector((-x, 0, -z)).normalized()
        yaw_r = math.radians(yaw)
        normals = [Vector((math.sin(yaw_r + k * math.pi / 2), 0, math.cos(yaw_r + k * math.pi / 2))) for k in range(4)]
        facing = max(normals, key=lambda nn: nn.dot(to_ring))
        half = d / 2 if abs(facing.dot(Vector((math.sin(yaw_r), 0, math.cos(yaw_r))))) > 0.7 else w / 2
        span = w / 2 if half == d / 2 else d / 2
        side = Vector((facing.z, 0, -facing.x))
        centre = Vector((x, 0, z)) + facing * (half + 0.02)
        cols = max(2, int(span * 2 / 1.4))
        for row in range(int((top + 12) / 2.4)):
            y = top - 1.6 - row * 2.4
            for cc in range(cols):
                if rnd.random() > 0.38:
                    continue
                p = centre + side * ((cc - (cols - 1) / 2) * 1.4) + Vector((0, y, 0))
                B.add(prism(rect(0.38, 0.55), p, side, Vector((0, 1, 0)), 0.01, facing=facing), rnd.choice(["#FFD36A", "#FFE6A0", "#FF9A6A", "#FFC27A"]),
                      glow=1.0)
    return B.to_object(coll)


def roof_sky_colour(d):
    """The sunset: violet above, pink, then orange-gold low behind the opponent (+z), the sun's glow round it."""
    up = max(0.0, d.y)
    c = kit.ramp([(0.0, "#FFB070"), (0.08, "#FF8A6A"), (0.22, "#C86A9A"), (0.5, "#5A4A9A"), (1.0, "#2A2A6A")], up ** 0.8)
    sun = Vector((0.15, 0.06, 1.0)).normalized()
    k = max(0.0, d.dot(sun))
    c = mix(c, "#FFE2A0", k ** 18 * 0.95 + k ** 4 * 0.25)
    if d.y < 0:
        c = mix(c, "#6A4A7A", min(1.0, -d.y * 4))
    return c


# ------------------------------------------------------------------------------------------------- the mountain

def mountain(coll):
    """A wooden platform on a snowy summit above a sea of clouds: the deck the ring stands on, its posts down into the
    rock, the summit's rocks and snow, prayer flags across the back, a stone hut, snowy pines; the peaks round it and the
    cloud sea are Mountain_Far, the sky Mountain_Sky."""
    rnd = random.Random(31)
    B = FastBuild("Mountain_Set")
    B.ao = 1.2
    P = 5.4
    # the deck: planks, a darker edge, a snowdrift in each corner
    def planks(p, n):
        q = UP(p)
        k = math.floor((q.x + 20) / 0.3)
        c = mix("#B07A4E", "#C48E5C", 0.5 + 0.5 * math.sin(k * 7.13))
        gap = abs((q.x + 20) / 0.3 - k) < 0.06
        c = mix(c, "#5A3A26", 0.65 if gap else 0.0)
        return mix(c, "#E6EEF8", max(0.0, min(1.0, (max(abs(q.x), abs(q.z)) - P + 0.6) / 0.6)) * 0.7)
    B.add(box((0, -0.15, 0), (2 * P, 0.3, 2 * P), bevel=0.04), planks)
    for k in range(4):
        yaw = 90.0 * k
        d = Vector((math.sin(math.radians(yaw)), 0, math.cos(math.radians(yaw))))
        B.add(box(tuple(d * P + Vector((0, -0.2, 0))), (2 * P + 0.3, 0.42, 0.3), yaw=yaw, bevel=0.05), "#6B4A32")
    for sx in (-1, 1):
        for sz in (-1, 1):
            B.add(blob(U(sx * (P - 0.7), 0.0, sz * (P - 0.7)), (0.9, 0.9, 0.35), seed=sx * 3 + sz, amp=0.12, subdiv=2, flat_bottom=U(0, 0, 0).z),
                  "#F4F8FF")
            # the posts down into the rock, a rope railing between them
            B.add(cyl((sx * P, -3.5, sz * P), 0.16, 4.7, 10), by_y([(0.0, "#4A3222"), (1.0, "#7A5236")], -3.5, 1.2))
            B.add(cyl((sx * P, 1.2, sz * P), 0.2, 0.18, 10, r_top=0.02), "#F4F8FF")
    for k in range(4):
        a = [(-1, -1), (1, -1), (1, 1), (-1, 1)][k]
        b = [(-1, -1), (1, -1), (1, 1), (-1, 1)][(k + 1) % 4]
        if k == 0:
            continue   # the hero's side open (the camera looks over it)
        pa = (a[0] * P, 1.0, a[1] * P)
        pb = (b[0] * P, 1.0, b[1] * P)
        B.add(rope(sag(pa, pb, 0.25, 12), 0.03, 6), "#C9A26B")
    # the summit under it: big rocks, snow on everything facing up
    def rock_snow(p, n):
        q = UP(p)
        nn = Vector((-n.x, n.z, -n.y))
        rock = mix("#6E7288", "#8C90A6", 0.5 + 0.5 * noise(q * 0.45))
        return mix(rock, "#F4F8FF", max(0.0, min(1.0, (nn.y - 0.45) * 3.0)))
    B.add(rock_shape(U(0, -6.5, 0), (9.0, 9.0, 5.8), seed=3, jag=0.18, subdiv=3), rock_snow)
    for i in range(14):
        a = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(6.2, 9.5)
        s = rnd.uniform(1.0, 2.4)
        B.add(rock_shape(U(math.cos(a) * r, -1.6 + rnd.uniform(-1.0, 0.8), math.sin(a) * r), (s, s, s * 0.8), seed=40 + i, jag=0.2), rock_snow)
    # prayer flags across the back: two tall poles, three lines of little flags in five colours
    flags = ["#2E6FE8", "#F4F8FF", "#E8403A", "#3E9A4E", "#FFC93C"]
    pa, pb = Vector((-7.5, 0, 8.0)), Vector((7.5, 0, 7.0))
    for p in (pa, pb):
        B.add(cyl((p.x, -1.5, p.z), 0.09, 8.2, 8), "#6B4A32")
    for j, h in enumerate((6.4, 5.4, 4.4)):
        pts = sag((pa.x, h, pa.z), (pb.x, h - 0.3, pb.z), 0.9, 30)
        B.add(rope(pts, 0.012, 4), "#4A3222")
        for i in range(1, 30):
            q = pts[i]
            nxt = pts[min(30, i + 1)]
            along = (nxt - q).normalized()
            B.add(prism([(-0.17, 0.0), (0.17, 0.0), (0.0, -0.36)], q, along, Vector((0, 1, 0)), 0.006, facing=(0, 0, -1), back=True), flags[(i + j) % 5])
    # the stone hut (left), a window lit warm, snow on its roof, a chimney
    hut = Vector((-8.4, 0, -2.0))
    B.add(box((hut.x, 1.1, hut.z), (3.2, 2.6, 3.0), bevel=0.08), noisy("#8C8C9C", "#6E6E7E", 1.4, 5))
    B.add(box((hut.x, 2.6, hut.z), (3.7, 0.5, 3.5), bevel=0.15), "#F4F8FF")
    B.add(box((hut.x + 1.62, 1.3, hut.z + 0.4), (0.04, 0.8, 0.7)), "#FFC27A", glow=1.0)
    B.add(box((hut.x - 0.8, 3.3, hut.z + 0.6), (0.5, 1.1, 0.5), bevel=0.04), "#6E6E7E")
    # snowy pines on the slopes
    for i in range(10):
        a = rnd.uniform(0, 2 * math.pi)
        if 1.2 < a < 1.95:
            continue   # not right behind the opponent's head
        r = rnd.uniform(7.5, 11.0)
        x, z = math.cos(a) * r, math.sin(a) * r
        y0 = -1.4 - (r - 7.5) * 0.4
        h = rnd.uniform(2.6, 4.2)
        pine(B, (x, y0, z), h)
    objs = [B.to_object(coll)]
    objs.append(mountain_far(coll))
    objs.append(sky_dome(coll, "Mountain_Sky", mountain_sky_colour))
    return objs


def pine(B, base, h):
    x, y, z = base
    B.add(cyl((x, y, z), 0.14, h * 0.3, 6), "#5A3A26")
    for k in range(4):
        t = k / 4
        r = h * 0.34 * (1 - t * 0.72)
        yb = y + h * (0.18 + t * 0.2)
        B.add(cyl((x, yb, z), r, h * 0.38, 9, r_top=r * 0.15),
              by_y([(0.0, "#1E5A44"), (0.65, "#2E7A58"), (0.85, "#E6EEF8"), (1.0, "#FFFFFF")], yb, yb + h * 0.38))


def mountain_far(coll):
    """The peaks round the summit rising out of a sea of clouds, snow on their tops, and the cloud sea itself."""
    rnd = random.Random(37)
    B = FastBuild("Mountain_Far")
    B.ao = 6.0
    for i in range(26):
        a = rnd.uniform(-180, 180)
        if abs(a) < 14:
            continue
        r = rnd.uniform(45, 140)
        x, z = math.sin(math.radians(a)) * r, math.cos(math.radians(a)) * r
        h = rnd.uniform(18, 52)
        w = h * rnd.uniform(0.6, 0.9)
        base = -24.0
        def peak_colour(p, n, top=base + h):
            q = UP(p)
            nn = Vector((-n.x, n.z, -n.y))
            t = (q.y - base) / max(1.0, top - base)
            rock = mix("#5E6A8C", "#7C88AA", 0.5 + 0.5 * noise(q * 0.08))
            snow = t > 0.62 - 0.12 * noise(q * 0.1) or nn.y > 0.75 and t > 0.4
            return mix(rock, "#F2F6FF", 1.0 if snow else 0.0)
        B.add(rock_shape(U(x, base + h * 0.45, z), (w, w * 0.9, h * 0.55), seed=60 + i, jag=0.22, subdiv=2), peak_colour)
    # the cloud sea: flattened puffs, white on top, lavender underneath
    for i in range(60):
        a = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(10, 110)
        x, z = math.cos(a) * r, math.sin(a) * r
        s = rnd.uniform(6, 14)
        B.add(blob(U(x, -14.0 + rnd.uniform(-2, 1.5), z), (s, s * 0.8, s * 0.35), seed=i, amp=0.15, subdiv=2),
              by_y([(0.0, "#B8C2E6"), (0.6, "#E8EEFF"), (1.0, "#FFFFFF")], -17.0, -10.0), glow=0.15)
    return B.to_object(coll)


def mountain_sky_colour(d):
    up = max(0.0, d.y)
    c = kit.ramp([(0.0, "#DDEFFF"), (0.12, "#B8DCFF"), (0.4, "#6AAEF2"), (1.0, "#2E6FD8")], up ** 0.7)
    sun = Vector((-0.5, 0.7, 0.5)).normalized()
    k = max(0.0, d.dot(sun))
    c = mix(c, "#FFFFFF", k ** 30 * 0.9 + k ** 6 * 0.15)
    if d.y < 0:
        c = mix(c, "#E8EEFF", min(1.0, -d.y * 5))
    return c


# ------------------------------------------------------------------------------------------------- the stars

def stars(coll):
    """The villain's floating arena: an eight-sided deck with glowing lines, its rim in neon, a rocky underside with
    crystals, four pylons with energy orbs; floating crystals and asteroids; his citadel behind. The planets are
    Stars_Far, the nebula Stars_Sky, the orbs' halos Stars_Beams."""
    rnd = random.Random(41)
    B = FastBuild("Stars_Set")
    B.ao = 1.0
    R = 7.2
    oct_pts = [(math.cos(math.pi / 8 + k * math.pi / 4) * R, math.sin(math.pi / 8 + k * math.pi / 4) * R) for k in range(8)]
    # the deck: dark, a pattern of glowing lines (rings and spokes) painted into its vertices
    def deck(p, n):
        q = UP(p)
        r = math.hypot(q.x, q.z)
        a = math.atan2(q.z, q.x)
        base = mix("#1E1A3E", "#2A2452", 0.5 + 0.5 * noise(q * 0.5))
        line = min(abs(r - 4.0), abs(r - 6.2)) < 0.07 or abs(((a / (math.pi / 8)) % 2) - 1) > 0.985 and r > 4.0
        return mix(base, "#7DF2FF", 1.0 if line else 0.0)
    def deck_glow(p):
        q = UP(p)
        r = math.hypot(q.x, q.z)
        a = math.atan2(q.z, q.x)
        return 1.0 if (min(abs(r - 4.0), abs(r - 6.2)) < 0.07 or abs(((a / (math.pi / 8)) % 2) - 1) > 0.985 and r > 4.0) else 0.0
    B.add(octagon_grid(R, 0.0), deck, glow=deck_glow)
    # the rim: a neon band round the edge, magenta under it
    for k in range(8):
        a, b = Vector((oct_pts[k][0], 0, oct_pts[k][1])), Vector((oct_pts[(k + 1) % 8][0], 0, oct_pts[(k + 1) % 8][1]))
        mid = (a + b) / 2
        length = (b - a).length
        yaw = math.degrees(math.atan2((b - a).x, (b - a).z)) + 90.0
        B.add(box((mid.x, -0.12, mid.z), (length + 0.1, 0.28, 0.22), yaw=yaw, bevel=0.04), "#2A2650")
        B.add(box((mid.x * 1.005, 0.03, mid.z * 1.005), (length, 0.06, 0.06), yaw=yaw), "#7DF2FF", glow=1.0)
        B.add(box((mid.x * 1.01, -0.26, mid.z * 1.01), (length * 0.96, 0.05, 0.05), yaw=yaw), "#FF5AD2", glow=1.0)
    # the underside: a rocky cone hanging below, crystals growing from it
    B.add(rock_shape(U(0, -4.8, 0), (6.6, 6.6, 4.4), seed=8, jag=0.2, subdiv=3), by_y([(0.0, "#1A1430"), (1.0, "#3A2E5C")], -9.0, -0.4))
    for i in range(16):
        a = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(2.0, 5.5)
        y = -1.0 - rnd.uniform(0.5, 4.0)
        d = Vector((math.cos(a) * 0.6, -1.0, math.sin(a) * 0.6)).normalized()
        colour = rnd.choice(["#7DF2FF", "#FF5AD2", "#B69CFF"])
        B.add(crystal(U(math.cos(a) * r, y, math.sin(a) * r), U(*d) - U(0, 0, 0), rnd.uniform(0.8, 2.0), rnd.uniform(0.2, 0.45)), colour, glow=0.7)
    # the pylons at four corners of the deck, an energy orb on each
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        p = Vector((math.cos(a) * (R - 0.9), 0, math.sin(a) * (R - 0.9)))
        B.add(cyl((p.x, 0, p.z), 0.42, 0.3, 8), "#3A3E5C")
        B.add(cyl((p.x, 0.3, p.z), 0.22, 2.6, 8, r_top=0.12), by_y([(0.0, "#3A3E5C"), (1.0, "#5A6178")], 0.3, 2.9))
        for j in range(3):
            B.add(cyl((p.x, 0.8 + j * 0.7, p.z), 0.24 - j * 0.03, 0.06, 8), "#7DF2FF", glow=1.0)
        B.add(ball((p.x, 3.3, p.z), 0.36), "#BFF8FF", glow=1.0)
    # floating crystals and asteroids round the arena
    for i in range(18):
        a = rnd.uniform(0, 2 * math.pi)
        r = rnd.uniform(9.0, 18.0)
        if abs(math.cos(a)) < 0.3 and math.sin(a) > 0 and r < 13:
            continue   # not right behind the opponent
        p = (math.cos(a) * r, rnd.uniform(-3.0, 6.0), math.sin(a) * r)
        if i % 3 == 0:
            colour = rnd.choice(["#7DF2FF", "#FF5AD2", "#B69CFF"])
            B.add(crystal(U(*p), U(rnd.uniform(-0.5, 0.5), 1.0, rnd.uniform(-0.5, 0.5)) - U(0, 0, 0), rnd.uniform(1.0, 2.2), rnd.uniform(0.3, 0.6)),
                  colour, glow=0.8)
        else:
            s = rnd.uniform(0.6, 1.6)
            B.add(rock_shape(U(*p), (s, s * 0.8, s), seed=100 + i, jag=0.25, subdiv=2), noisy("#4A4060", "#6A5E80", 0.8, i))
    # the citadel behind the opponent: dark spires, magenta windows, an emblem
    for i in range(7):
        x = -16.0 + i * 5.3
        z = 30.0 + abs(i - 3) * 4.0
        h = 22.0 + (3 - abs(i - 3)) * 7.0
        base = -18.0
        B.add(box((x, base + h / 2, z), (3.6, h, 3.6), bevel=0.2), by_y([(0.0, "#0E0A1E"), (1.0, "#2A1E46")], base, base + h))
        B.add(cyl((x, base + h, z), 2.4, 6.0, 8, r_top=0.05), "#3A1E5C")
        for j in range(6):
            B.add(box((x, base + h * 0.35 + j * 2.4, z - 1.82), (0.5, 0.9, 0.04)), "#FF5AD2", glow=1.0)
    B.add(prism(circle(3.2, 28), Vector((0, 18.0, 41.0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), 0.3, facing=(0, 0, -1)), "#2A1E46")
    B.add(prism(star(2.6, 1.1, 6), Vector((0, 18.0, 40.65)), Vector((-1, 0, 0)), Vector((0, 1, 0)), 0.05, facing=(0, 0, -1)), "#FF5AD2", glow=1.0)
    objs = [B.to_object(coll)]
    # the orbs' halos (additive)
    G = FastBuild("Stars_Beams")
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        p = Vector((math.cos(a) * (R - 0.9), 3.3, math.sin(a) * (R - 0.9)))
        G.add(beam(p + Vector((0, 0.1, 0)), p - Vector((0, 3.0, 0)), 0.3, 0.9, 12), by_y([(0.0, "#0A2A30"), (1.0, "#7DF2FF")], p.y - 3.0, p.y + 0.1))
    objs.append(G.to_object(coll))
    objs.append(stars_far(coll))
    objs.append(sky_dome(coll, "Stars_Sky", stars_sky_colour, segs=48, rings=24))
    return objs


def octagon_grid(R, y, rings=10):
    """An octagon's top face in rings (dense enough for a pattern painted at the vertices)."""
    def make(bm):
        prev = None
        centre = bm.verts.new(U(0, y, 0))
        n = 64
        for j in range(1, rings + 1):
            rr = R * j / rings
            loop = []
            for k in range(n):
                a = 2 * math.pi * k / n
                # an octagon's radius at this angle
                seg = math.pi / 4
                local = (a - math.pi / 8) % seg - seg / 2
                r = rr * math.cos(seg / 2) / math.cos(local)
                loop.append(bm.verts.new(U(math.cos(a) * r, y, math.sin(a) * r)))
            faces = []
            for k in range(n):
                k2 = (k + 1) % n
                if prev is None:
                    faces.append(bm.faces.new((centre, loop[k], loop[k2])))
                else:
                    faces.append(bm.faces.new((prev[k], loop[k], loop[k2], prev[k2])))
            _orient(bm, faces, Vector((0, 0, 1)))
            prev = loop
    return make


def stars_far(coll):
    """The Earth (oceans, land, clouds) high behind the citadel, a ringed planet, the moon."""
    rnd = random.Random(53)
    B = FastBuild("Stars_Far")
    B.ao = 0.0
    def earth(p, n):
        d = Vector((-n.x, n.z, -n.y))
        land = noise(d * 2.2 + Vector((3.1, 1.2, 0.4))) > 0.12
        cloud = noise(d * 4.0 + Vector((7.3, 2.1, 5.5))) > 0.32
        c = "#2E6FE8" if not land else "#5DB04E"
        if cloud:
            c = "#F4F8FF"
        return rgb(c)
    B.add(ball((-34.0, 34.0, 120.0), 20.0, 32, 20), earth, glow=0.35)
    def ringed(p, n):
        d = Vector((-n.x, n.z, -n.y))
        return mix("#7B4FB5", "#FF8AD8", 0.5 + 0.5 * math.sin(d.y * 14.0 + noise(d * 3.0) * 2.0))
    B.add(ball((48.0, 22.0, 110.0), 12.0, 28, 18), ringed, glow=0.3)
    B.add(planet_ring((48.0, 22.0, 110.0), 15.0, 23.0, 18.0), "#FFD36A", glow=0.4)
    def moon(p, n):
        d = Vector((-n.x, n.z, -n.y))
        crater = noise(d * 5.0 + Vector((1.1, 4.4, 2.2))) > 0.35
        return mix("#C8CCD8", "#8A8EA0", 0.8 if crater else 0.0)
    B.add(ball((-60.0, 10.0, 70.0), 6.0, 20, 14), moon, glow=0.25)
    return B.to_object(coll)


def planet_ring(c, r0, r1, tilt, segs=48):
    def make(bm):
        rot = Matrix.Rotation(math.radians(tilt), 4, "X") @ Matrix.Rotation(math.radians(20), 4, "Z")
        cc = U(*c)
        for side in (1, -1):
            lift = rot @ Vector((0, 0, 0.02 * side))
            inner = [bm.verts.new(cc + lift + rot @ Vector((math.cos(2 * math.pi * k / segs) * r0, math.sin(2 * math.pi * k / segs) * r0, 0)))
                     for k in range(segs)]
            outer = [bm.verts.new(cc + lift + rot @ Vector((math.cos(2 * math.pi * k / segs) * r1, math.sin(2 * math.pi * k / segs) * r1, 0)))
                     for k in range(segs)]
            for k in range(segs):
                k2 = (k + 1) % segs
                f = bm.faces.new((inner[k], outer[k], outer[k2], inner[k2]))
                _orient(bm, [f], rot @ Vector((0, 0, side)))
    return make


def stars_sky_colour(d):
    """Deep space: navy above, a violet band, nebulae of magenta, cyan and violet drifting through it."""
    up = d.y
    c = kit.ramp([(0.0, "#140C2E"), (0.5, "#0A0820"), (1.0, "#05060F")], abs(up) ** 0.6)
    n1 = noise(d * 2.2 + Vector((1.0, 2.0, 3.0)))
    n2 = noise(d * 3.4 + Vector((5.0, 1.0, 2.0)))
    c = mix(c, "#7B2E9A", max(0.0, n1 - 0.05) * 1.1)
    c = mix(c, "#2E7AB8", max(0.0, n2 - 0.25) * 1.0)
    c = mix(c, "#FF5AD2", max(0.0, n1 * n2 - 0.08) * 1.4)
    return c


# ------------------------------------------------------------------------------------------------- skies

def sky_dome(coll, name, colour_of, segs=40, rings=20, radius=380.0):
    """A dome round the camera, its colours painted at the vertices (drawn at the far plane, unlit)."""
    B = Build(name)
    B.ao = 0.0

    def make(bm):
        geom = bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=radius)
        for f in bm.faces:
            f.normal_update()
        # inward
        for f in bm.faces:
            f.normal_flip()

    def colour(p, n):
        d = UP(p).normalized()
        return colour_of(d)
    B.add(make, colour, glow=1.0)
    obj = B.to_object(coll)
    obj["sky"] = 1
    return obj


# ------------------------------------------------------------------------------------------------- all

def objects():
    coll = bpy.data.collections.get("Arena")
    return [o for o in coll.objects if o.type == "MESH"] if coll else []


def build(stages=STAGES):
    for name in ("Cube", "Light", "Camera"):
        o = bpy.data.objects.get(name)
        if o is not None:
            bpy.data.objects.remove(o, do_unlink=True)
    coll = ta.collection("Arena")
    for o in list(coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    SEATS.clear()
    made = {}
    for s in stages:
        objs = [ring(coll, s)]
        objs += {"gym": gym, "roof": roof, "mountain": mountain, "stars": stars}[s](coll)
        for o in objs:
            made[o.name] = ta.tri_count(o)
    return made


def bake_ao():
    """Each piece's occlusion on its own; the gym's, the roof's and the rings on the floor (a ground under them), the
    mountain's and the stars' pieces in the air (none: the summit's rocks and the floating rock go down below 0)."""
    objs = [o for o in objects() if not o.get("sky") and not o.name.endswith("_Beams") and not o.name.endswith("_Beacon")]
    floating = [o for o in objs if o.name.startswith(("Mountain_", "Stars_"))]
    grounded = [o for o in objs if o not in floating]
    return kit.bake_ao(grounded, samples=20) + kit.bake_ao(floating, samples=20, ground=False)


def export():
    objs = objects()
    for o in objs:
        o.location = (0, 0, 0)
    path = kit.export_fbx(objs, os.path.join(UNITY_ART, "Arena.fbx"))
    info = {o.name: {"tris": ta.tri_count(o), "verts": len(o.data.vertices)} for o in objs}
    with open(os.path.join(HERE, "arena.json"), "w", encoding="utf-8") as f:
        json.dump(info, f, indent=1)
    for stage, seats in SEATS.items():
        with open(os.path.join(UNITY_ART, f"arena_seats_{stage}.txt"), "w", encoding="utf-8", newline="\n") as f:
            for x, y, z, yaw in seats:
                f.write(f"{x:.2f} {y:.2f} {z:.2f} {yaw:.0f}\n")
    return path


# ------------------------------------------------------------------------------------------------- previews

# the game's fight camera (BoxingGame.FightCamera, Unity): behind and above the hero, at the opponent's face
FIGHT_CAM = ((0.0, 2.65, -2.75), (0.0, 1.62, 0.6), 48.0)
SIDE_CAM = ((3.9, 1.9, -1.4), (0.0, 1.2, 0.2), 46.0)
FRONT_CAM = ((-1.2, 1.7, 3.6), (0.0, 1.3, -0.4), 46.0)
SKY_HEX = {"gym": "#120E1E", "roof": "#3A2A6A", "mountain": "#6AAEF2", "stars": "#05060F"}


def stand_ins(coll):
    """Two boxers to frame the shots: the hero (see-through in the game: a pale figure) and the opponent."""
    out = []
    for name, z, colour, face in (("StandHero", -0.5, "#B8D8FF", -1), ("StandFoe", 0.55, "#E8403A", 1)):
        B = Build(name)
        y0 = RING_H
        B.add(ball((0, y0 + 0.25, z), (0.1, 0.25, 0.1)), "#2E2A40")
        B.add(ball((0, y0 + 0.62, z), (0.22, 0.24, 0.17)), colour)
        B.add(ball((0, y0 + 1.12, z), (0.3, 0.29, 0.28)), "#FFD9C2")
        for s in (-1, 1):
            B.add(ball((s * 0.22, y0 + 1.0, z - face * 0.22), 0.11), "#E8403A" if name == "StandHero" else "#2E6FE8")
        out.append(B.to_object(coll))
    return out


def preview(stages=STAGES):
    out_dir = os.path.join(HERE, "previews")
    coll = ta.collection("ArenaPreview")
    for o in list(coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    figures = stand_ins(coll)
    kit.sun(rotation_deg=(48, 0, 155))
    paths = []
    objs = objects()
    for s in stages:
        prefix = {"gym": "Gym", "roof": "Roof", "mountain": "Mountain", "stars": "Stars"}[s]
        for o in objs:
            o.hide_render = not (o.name.startswith(prefix + "_") or o.name == f"Ring_{prefix}")
            if o.name.endswith("_Beams"):
                o.hide_render = True
        for f in figures:
            f.hide_render = False
        kit.world_colour(SKY_HEX[s])
        for shot, (cam, look, fov) in (("fight", FIGHT_CAM), ("side", SIDE_CAM), ("front", FRONT_CAM)):
            kit.camera("ArenaCam", U(*cam), U(*look), fov_v_deg=fov, clip=900.0)
            paths.append(kit.render(os.path.join(out_dir, f"arena_{s}_{shot}.png"), res=(1280, 720)))
    return paths
