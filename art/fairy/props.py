"""Zana!'s garden as code (Blender 5.1, headless; run by build.py --props): a storybook garden in the late-afternoon sun
(the look's overhaul of 2026-11-17: the user wanted it to "look like an actual stylized game without breaking
performance"). Exported together into Assets/Games/Fairy/Art/FairyProps.fbx (one mesh object each, its pivot on the
ground under it, facing -Y = the camera once the game turns it round). Sizes are real metres at scale 1.

No textures: every vertex carries what the game's Tundu/Realm needs, so the box lights it per vertex and the pixel only
blends the fog:
    colour  ("Col", linear): rgb = its own colour (gradients painted in: crowns darker and teal underneath, sunny
            yellow-green on top; bark darker in its grooves), a = baked ambient occlusion (0 = shut in .. 1 = open)
    uv      ("Data"): u = how much it sways in the wind (0 rooted .. 1 the tips), v = how much it glows (lit windows,
            lanterns, glowing caps, crystals: drawn bright whatever the light)
    normal  crowns, bushes and clouds have their normals turned out from their middle (they light as one soft volume,
            not as lumps: the stylized-tree trick); grass points up (it lights like the meadow it grows from)

    the heart      FairyTree (the spirit tree the fairy defends: a trunk of twisting strands with veins of light, a glowing
                   portal at its foot, roots with light running along them, a luminous lilac canopy of cushions,
                   wisteria glowing at its tips, spirit orbs, crystals and glowing mushrooms)
    trees          TreeRound, TreeBlossom, TreeTall, Pine (+ ...Far: fewer triangles, for the forest behind)
    the meadow     Bush, BushFlower, FlowerPatch, FlowerPatchBlue, GrassTuft, GrassTall, GrassFlowers, Fern,
                   Rock, RockSmall, PathStone, Pebbles, Mushroom, MushroomGlow, MushroomTrio, CrystalCluster, Lantern,
                   FlowerDaisy, FlowerTulip, FlowerBell (the garden's decorations, round the tree)
    far away       Cloud1, Cloud2, Cloud3, Mountains, Castle
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
sys.path.insert(0, os.path.join(HERE, "..", "tools"))
import tundu_art as ta  # noqa: E402

UNITY_ART = os.path.join(ta.REPO, "unity", "MoveItGames", "Assets", "Games", "Fairy", "Art")

# ------------------------------------------------------------------------------------------------- colours (sRGB)

C = {
    "bark": "#9C6448", "bark_dark": "#5A3646", "bark_light": "#C9925E",
    "wood": "#D69A5C", "wood_dark": "#8A5236", "frame": "#5B3552", "gold": "#FFCF5A",
    "window": "#FFD27A", "window_hot": "#FFF4C8",
    "stem": "#4C9C50", "stem_dark": "#2C6E4E", "leaf": "#4FAE62",
    "blossom": "#FF84B5", "blossom_light": "#FFC6DE", "blossom_deep": "#E85A97",
    "white": "#FFF8F0", "pink": "#FF9EC4", "rose": "#FF5C8E", "lilac": "#B79CFF", "yellow": "#FFE066",
    "blue": "#8CD0FF", "peach": "#FFB892", "centre": "#FFC23C",
    "cap_red": "#FF4F72", "cap_spot": "#FFF6E8", "stalk": "#FFF0DA", "gill": "#F2CFC0",
    "cap_glow": "#6FE4FF", "cap_glow2": "#C69CFF",
    "stone": "#D3CADB", "stone_dark": "#9086A8", "moss": "#79C25A", "moss_dark": "#4E9A56",
    "crystal_a": "#8FEAFF", "crystal_b": "#C9A2FF", "crystal_c": "#FF9ED8",
    "cloud": "#FFFFFF", "cloud_under": "#D8CDF0", "cloud_warm": "#FFE9DC",
    "mount_base": "#7F93CF", "mount_top": "#AFC0EE", "snow": "#FFFFFF", "mount_rock": "#9AA6DA",
    "castle_wall": "#F3E4EE", "castle_shade": "#CDB8D8", "roof_blue": "#6E86F0", "roof_pink": "#FF84B5",
    "lantern_metal": "#43355A", "lantern_light": "#FFCE6E",
    # the spirit tree (2026-11-17): silvery-lilac bark, veins of light, a luminous lilac canopy, wisteria, a glowing portal
    "bark_myst": "#7A6A9E", "bark_myst_dark": "#3E3263", "bark_myst_light": "#B3A3D6",
    "vein": "#8CF6FF", "vein_pink": "#FFA8EE", "portal_core": "#F0A24C", "portal_edge": "#9A4FE8",
    "pad_under": "#6C5BD8", "pad_mid": "#A28CF2", "pad_top": "#E7CFFF", "pad_tip": "#FFE6F7",
    "wis_top": "#8D72EE", "wis_mid": "#D58CF2", "wis_bottom": "#FFB4E4", "orb": "#F0FDFF",
}
# a crown's colours from underneath to its sunny top (the hue shifts: teal below, yellow-green above)
LEAF_RAMP = [(0.0, "#327C6C"), (0.33, "#48A466"), (0.68, "#80CC5C"), (1.0, "#C9EC6E")]
TALL_RAMP = [(0.0, "#25615E"), (0.4, "#358A5E"), (0.75, "#62B45A"), (1.0, "#A8DA66")]
PINE_RAMP = [(0.0, "#1F5560"), (0.45, "#2E7C66"), (0.8, "#4FA466"), (1.0, "#8CCB6E")]
BLOSSOM_RAMP = [(0.0, "#C84E8A"), (0.4, "#EC77AA"), (0.75, "#FFA6C9"), (1.0, "#FFDCEA")]
GRASS_RAMP = [(0.0, "#2C7457"), (0.45, "#4EA85A"), (0.8, "#94D164"), (1.0, "#C9EB78")]


def rgb(h):
    return ta.hex_rgb(C.get(h, h)) if isinstance(h, str) else h


def mix(a, b, t):
    a, b = rgb(a), rgb(b)
    t = max(0.0, min(1.0, t))
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def ramp(stops, t):
    t = max(0.0, min(1.0, t))
    for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
        if t <= t1:
            return mix(c0, c1, (t - t0) / max(1e-6, t1 - t0))
    return rgb(stops[-1][1])


def shade(c, k):
    c = rgb(c)
    return tuple(max(0.0, min(1.0, x * k)) for x in c)


def by_height(stops, z0, z1, seed=0.0, wobble=0.12, freq=0.9):
    """A colour rule: the ramp by height between z0 and z1, nudged by noise (patches of lighter and darker leaves)."""
    off = Vector((seed * 1.7, seed * 2.9, seed * 0.3))

    def rule(p, n):
        t = (p.z - z0) / max(1e-6, z1 - z0) + wobble * noise(p * freq + off)
        return ramp(stops, t)
    return rule


# ------------------------------------------------------------------------------------------------- the builder

class Build:
    """Parts into one bmesh, each part's vertices remembering their colour rule, sway, glow and how their normals turn.
    colour: a colour name/hex/rgb or rule(p, n) -> rgb; sway, glow: a number or rule(p) -> number; sphere: (centre,
    amount) = normals turned out from that centre; up: normals point up; flat: faceted faces."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.vinfo = []
        self.ao = 1.0

    def add(self, make, colour, sway=0.0, glow=0.0, sphere=None, up=False, flat=False):
        # new elements may reuse freed slots anywhere in the sequence (an op that deletes, like a cone's tip), so each
        # part marks its own vertices and faces in a layer rather than counting on them coming last
        bm = self.bm
        vl = bm.verts.layers.int.get("part") or bm.verts.layers.int.new("part")
        fl = bm.faces.layers.int.get("flat") or bm.faces.layers.int.new("flat")
        make(bm)
        self.vinfo.append((colour, sway, glow, sphere, up))
        part = len(self.vinfo)
        for v in bm.verts:
            if v[vl] == 0:
                v[vl] = part
        if flat:
            for f in bm.faces:
                if f[fl] == 0:
                    f[fl] = 1
        else:
            for f in bm.faces:
                if f[fl] == 0:
                    f[fl] = -1
        return self

    def to_object(self, coll):
        bm = self.bm
        bm.verts.ensure_lookup_table()
        bm.faces.ensure_lookup_table()
        bm.verts.index_update()
        bm.faces.index_update()
        bm.normal_update()
        vl = bm.verts.layers.int["part"]
        fl = bm.faces.layers.int["flat"]
        flat = {f.index for f in bm.faces if f[fl] == 1}
        n = len(bm.verts)
        cols, data, norms = [None] * n, [None] * n, [None] * n
        for v in bm.verts:
            colour, sway, glow, sphere, up = self.vinfo[v[vl] - 1]
            p, nr = v.co.copy(), v.normal.copy()
            if up:
                nr = Vector((0, 0, 1))
            elif sphere is not None:
                c, k = sphere
                d = p - Vector(c)
                if d.length > 1e-6:
                    nr = (nr * (1 - k) + d.normalized() * k).normalized()
            cols[v.index] = colour(p, v.normal) if callable(colour) else rgb(colour)
            data[v.index] = (sway(p) if callable(sway) else sway, glow(p) if callable(glow) else glow)
            norms[v.index] = nr
        face_normals = [f.normal.copy() for f in bm.faces]
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me)
        bm.free()
        obj = bpy.data.objects.new(self.name, me)
        coll.objects.link(obj)
        for name in ("part", "flat"):
            if name in me.attributes:
                me.attributes.remove(me.attributes[name])
        me.shade_smooth()
        col = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
        me.color_attributes.active_color = col
        uv = me.uv_layers.new(name="Data")
        loop_normals = [None] * len(me.loops)
        lin = ta.srgb_to_linear
        for poly in me.polygons:
            fn = face_normals[poly.index] if poly.index in flat else None
            for li in poly.loop_indices:
                vi = me.loops[li].vertex_index
                r, g, b = cols[vi]
                col.data[li].color = (lin(r), lin(g), lin(b), 1.0)
                uv.data[li].uv = data[vi]
                loop_normals[li] = fn if fn is not None else norms[vi]
        me.normals_split_custom_set(loop_normals)
        me.materials.append(preview_material())
        obj["ao_distance"] = self.ao
        return obj


# ------------------------------------------------------------------------------------------------- shapes

def buried(f, others, margin=0.84):
    """True when every corner of a face lies well inside one of `others` (centre, radii; the margin allows for the other
    ball's own lumps): hidden in a cluster of balls, never seen."""
    for v in f.verts:
        inside = False
        for oc, orad in others:
            d = v.co - Vector(oc)
            if (d.x / orad[0]) ** 2 + (d.y / orad[1]) ** 2 + (d.z / orad[2]) ** 2 < margin * margin:
                inside = True
                break
        if not inside:
            return False
    return True


def blob(c, radii, seed=0, amp=0.08, subdiv=3, freq=1.6, flat_bottom=None, others=()):
    """A soft lumpy ball (an icosphere moved by noise); flat_bottom = z under which it's pressed flat (clouds); its faces
    buried inside `others` (the cluster's other balls: (centre, radii)) are left out (2026-11-17: ~a third of a crown)."""
    def make(bm):
        geom = bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
        off = Vector((seed * 3.1, seed * 1.7, seed * 2.3))
        cc = Vector(c)
        for v in geom["verts"]:
            d = v.co.normalized()
            k = 1.0 + amp * noise(d * freq + off) + amp * 0.35 * noise(d * freq * 2.7 + off * 1.3)
            p = cc + Vector((d.x * radii[0] * k, d.y * radii[1] * k, d.z * radii[2] * k))
            if flat_bottom is not None and p.z < flat_bottom:
                p.z = flat_bottom + (p.z - flat_bottom) * 0.12
            v.co = p
        if others:
            faces = {f for v in geom["verts"] for f in v.link_faces}
            gone = [f for f in faces if buried(f, others)]
            if gone:
                bmesh.ops.delete(bm, geom=gone, context="FACES")
    return make


def ellipsoid(c, radii, rot=(0, 0, 0), segs=16, rings=10):
    return lambda bm: ta.ellipsoid(bm, c, radii, rot_deg=rot, segments=segs, rings=rings)


def cone(base, tip, r, segs=10):
    return lambda bm: ta.cone(bm, base, tip, r, segments=segs)


def smooth_path(pts, sub=3):
    """Catmull-Rom through the points, `sub` steps a span."""
    pts = [Vector(p) for p in pts]
    if len(pts) < 3 or sub <= 1:
        return pts
    out = []
    for i in range(len(pts) - 1):
        p0, p1, p2 = pts[max(0, i - 1)], pts[i], pts[i + 1]
        p3 = pts[min(len(pts) - 1, i + 2)]
        for s in range(sub):
            t = s / sub
            t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    out.append(pts[-1])
    return out


def lerp_list(keys, n):
    if not isinstance(keys, (list, tuple)):
        return [keys] * n
    out = []
    for i in range(n):
        u = i / max(1, n - 1) * (len(keys) - 1)
        a = min(int(u), len(keys) - 2)
        t = u - a
        out.append(keys[a] + (keys[a + 1] - keys[a]) * t)
    return out


def tube(pts, radii, segs=10, sub=3, cap=True, ridge=None, start_cap=False):
    """A smooth tapering tube through points (parallel-transported rings); ridge(angle, t) -> extra radius share."""
    def make(bm):
        path = smooth_path(pts, sub)
        n = len(path)
        rs = lerp_list(radii, n)
        tans = [(path[min(n - 1, i + 1)] - path[max(0, i - 1)]).normalized() for i in range(n)]
        t0 = tans[0]
        ref = Vector((1, 0, 0)) if abs(t0.x) < 0.9 else Vector((0, 1, 0))
        nrm = (ref - t0 * ref.dot(t0)).normalized()
        rings = []
        for i in range(n):
            t = tans[i]
            nrm = (nrm - t * nrm.dot(t)).normalized()
            bi = t.cross(nrm)
            ring = []
            for k in range(segs):
                a = 2 * math.pi * k / segs
                d = nrm * math.cos(a) + bi * math.sin(a)
                rr = rs[i] * (1 + (ridge(a, i / max(1, n - 1)) if ridge else 0))
                ring.append(bm.verts.new(path[i] + d * rr))
            rings.append(ring)
        for i in range(n - 1):
            for k in range(segs):
                k2 = (k + 1) % segs
                bm.faces.new((rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]))
        if cap:
            tip = bm.verts.new(path[-1] + tans[-1] * rs[-1] * 0.9)
            for k in range(segs):
                bm.faces.new((rings[-1][k], rings[-1][(k + 1) % segs], tip))
        if start_cap:
            tip = bm.verts.new(path[0] - tans[0] * rs[0] * 0.3)
            for k in range(segs):
                bm.faces.new((rings[0][(k + 1) % segs], rings[0][k], tip))
    return make


def rounded_box(c, half, rot_z=0.0, segs=8, rings=6, power=3.0):
    """A soft box (a superellipsoid)."""
    def make(bm):
        geom = bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=1.0)
        m = Matrix.Translation(c) @ Matrix.Rotation(rot_z, 4, "Z")
        for v in geom["verts"]:
            d = v.co
            s = Vector((math.copysign(abs(d.x) ** (2 / power), d.x), math.copysign(abs(d.y) ** (2 / power), d.y),
                        math.copysign(abs(d.z) ** (2 / power), d.z)))
            v.co = m @ Vector((s.x * half[0], s.y * half[1], s.z * half[2]))
    return make


def lobed_disc(c, radius, lobes, normal=(0, 0, 1), thick=0.03, inner=0.45, pts=30, cup=0.0):
    """A flower's petals as one disc with round lobes (closed: top, bottom, rim); cup lifts the rim."""
    def make(bm):
        nrm = Vector(normal).normalized()
        ref = Vector((1, 0, 0)) if abs(nrm.x) < 0.9 else Vector((0, 1, 0))
        u = (ref - nrm * ref.dot(nrm)).normalized()
        w = nrm.cross(u)
        cc = Vector(c)
        top, bot = [], []
        for k in range(pts):
            a = 2 * math.pi * k / pts
            r = radius * (inner + (1 - inner) * abs(math.cos(lobes * a / 2)) ** 0.7)
            p = cc + (u * math.cos(a) + w * math.sin(a)) * r + nrm * (cup * (r / radius) ** 2)
            top.append(bm.verts.new(p + nrm * thick * 0.5))
            bot.append(bm.verts.new(p - nrm * thick * 0.5))
        ct = bm.verts.new(cc + nrm * thick * 0.8)
        cb = bm.verts.new(cc - nrm * thick * 0.8)
        for k in range(pts):
            k2 = (k + 1) % pts
            bm.faces.new((ct, top[k], top[k2]))
            bm.faces.new((cb, bot[k2], bot[k]))
            bm.faces.new((top[k2], top[k], bot[k], bot[k2]))
    return make


def blade(base, angle, height, width, lean, bend=0.5, rows=2):
    """A grass blade: a single-sided tapering strip curving over (the grass material draws both sides)."""
    def make(bm):
        b = Vector(base)
        d = Vector((math.cos(angle), math.sin(angle), 0))
        side = Vector((-d.y, d.x, 0))
        left, right = [], []
        for i in range(rows):
            t = i / rows
            p = b + d * lean * height * t ** (1 + bend) + Vector((0, 0, height * t * (1 - 0.25 * lean * t)))
            wdt = width * (1 - t * 0.85)
            left.append(bm.verts.new(p - side * wdt * 0.5))
            right.append(bm.verts.new(p + side * wdt * 0.5))
        tip = bm.verts.new(b + d * lean * height + Vector((0, 0, height * (1 - 0.25 * lean))))
        for i in range(rows - 1):
            bm.faces.new((left[i], right[i], right[i + 1], left[i + 1]))
        bm.faces.new((left[-1], right[-1], tip))
    return make


def strip_leaf(pts, widths, rib=0.03, thick=0.02, up=Vector((0, 0, 1))):
    """A thin closed leaf along points (both sides), its middle raised by `rib`."""
    def make(bm):
        P = [Vector(p) for p in pts]
        n = len(P)
        rows = []
        for i, p in enumerate(P):
            t = (P[min(n - 1, i + 1)] - P[max(0, i - 1)]).normalized()
            side = t.cross(up)
            if side.length < 1e-4:
                side = t.cross(Vector((1, 0, 0)))
            side.normalize()
            nrm = side.cross(t).normalized()
            wd = widths[i]
            top = [p + side * wd, p + nrm * rib, p - side * wd]
            bottom = [q - nrm * thick for q in top]
            rows.append(([bm.verts.new(q) for q in top], [bm.verts.new(q) for q in bottom]))
        for i in range(n - 1):
            (a, ab), (b, bb) = rows[i], rows[i + 1]
            for k in range(2):
                bm.faces.new((a[k], b[k], b[k + 1], a[k + 1]))
                bm.faces.new((ab[k + 1], bb[k + 1], bb[k], ab[k]))
            bm.faces.new((a[0], ab[0], bb[0], b[0]))
            bm.faces.new((b[2], bb[2], ab[2], a[2]))
    return make


def bezier(p0, p1, p2, n):
    p0, p1, p2 = Vector(p0), Vector(p1), Vector(p2)
    return [p0 * (1 - t) ** 2 + p1 * 2 * t * (1 - t) + p2 * t * t for t in (k / (n - 1) for k in range(n))]


def crystal(base, axis, length, r, sides=6):
    """A crystal: a prism with a pointed tip (faceted)."""
    def make(bm):
        b, ax = Vector(base), Vector(axis).normalized()
        ref = Vector((1, 0, 0)) if abs(ax.x) < 0.9 else Vector((0, 1, 0))
        u = (ref - ax * ref.dot(ax)).normalized()
        w = ax.cross(u)
        low, high = [], []
        for k in range(sides):
            a = 2 * math.pi * k / sides
            d = u * math.cos(a) + w * math.sin(a)
            low.append(bm.verts.new(b + d * r * 0.85))
            high.append(bm.verts.new(b + ax * length + d * r))
        tip = bm.verts.new(b + ax * (length + r * 1.8))
        bottom = bm.verts.new(b - ax * r * 0.3)
        for k in range(sides):
            k2 = (k + 1) % sides
            bm.faces.new((low[k], low[k2], high[k2], high[k]))
            bm.faces.new((high[k], high[k2], tip))
            bm.faces.new((low[k2], low[k], bottom))
    return make


def sway_height(h0, h1, power=1.6, top=1.0):
    return lambda p: top * max(0.0, min(1.0, (p.z - h0) / max(1e-6, h1 - h0))) ** power


# ------------------------------------------------------------------------------------------------- pieces shared by props

def crown(B, balls, centre, z0, z1, seed, stops=LEAF_RAMP, subdiv=3, sway=0.22, k=0.8, amp=0.08):
    """A crown: soft balls coloured as one (the ramp by height), normals out from the crown's middle."""
    rule = by_height(stops, z0, z1, seed)
    shapes = [(c, r if isinstance(r, tuple) else (r, r, r * 0.92)) for c, r in balls]
    for i, (c, rr) in enumerate(shapes):
        others = [o for j, o in enumerate(shapes) if j != i]
        B.add(blob(c, rr, seed + i, amp, subdiv, others=others), rule, sway=sway, sphere=(centre, k))


def blossoms(B, balls, centre, count, seed, colours=("blossom", "blossom_light"), size=(0.12, 0.2), above=-0.15, sway=0.22, front=None, middle="window_hot", pts=15):
    """Little five-petal blossoms sitting on a crown's balls, facing out (on the upper side, or toward `front`)."""
    rng = random.Random(seed)
    placed = 0
    tries = 0
    while placed < count and tries < count * 30:
        tries += 1
        c, r = balls[rng.randrange(len(balls))]
        r = r[0] if isinstance(r, tuple) else r
        a, el = rng.uniform(0, 2 * math.pi), rng.uniform(above, 1.2)
        d = Vector((math.cos(a) * math.cos(el), math.sin(a) * math.cos(el), math.sin(el)))
        if front is not None and d.dot(Vector(front).normalized()) < -0.1:
            continue
        p = Vector(c) + d * r * 1.0
        # only where it shows: not inside another ball
        if any((p - Vector(c2)).length < (r2[0] if isinstance(r2, tuple) else r2) * 0.92 for c2, r2 in balls if Vector(c2) != Vector(c)):
            continue
        s = rng.uniform(*size)
        out = (d + Vector((0, 0, 0.3))).normalized()
        colour = colours[placed % len(colours)]
        B.add(lobed_disc(p, s, 5, normal=out, thick=s * 0.25, inner=0.5, pts=pts, cup=s * 0.25), middle_rule(p, s, colour, middle),
              sway=sway, sphere=(centre, 0.55))
        placed += 1


def middle_rule(centre, radius, colour, middle):
    """Petals with their middle painted in (a soft dot of `middle` at the disc's centre: no extra geometry)."""
    c = Vector(centre)
    return lambda p, n: mix(middle, colour, ((p - c).length / radius - 0.12) / 0.3)


def flower(B, base, height, colour, lobes=5, radius=0.12, seed=0, lean=(0.0, 0.0), centre_colour="centre", face=(0, -0.3, 1)):
    """A small flower: a stem and a lobed disc of petals facing up (and a little toward the camera), its middle painted
    in on the small ones (2026-11-17: tiny triangles cost the box's GPU dearly), a ball on the bigger ones."""
    b = Vector(base)
    top = b + Vector((lean[0], lean[1], height))
    mid = b.lerp(top, 0.55) + Vector((lean[0] * 0.3, lean[1] * 0.3, 0))
    sw = sway_height(b.z, b.z + height, 1.5, 0.6)
    small = radius < 0.14
    if small:
        if height > 0.01:
            B.add(tube([b, top], [radius * 0.12, radius * 0.1], segs=3, sub=1, cap=False), "stem", sway=sw)
        B.add(lobed_disc(top, radius, lobes, normal=face, thick=radius * 0.18, cup=radius * 0.2, pts=lobes * 3),
              middle_rule(top, radius, colour, centre_colour), sway=sw)
        return
    B.add(tube([b, mid, top], [radius * 0.12, radius * 0.1], segs=5, sub=2, cap=False), "stem", sway=sw)
    B.add(lobed_disc(top, radius, lobes, normal=face, thick=radius * 0.18, cup=radius * 0.2, pts=30), colour, sway=sw)
    B.add(ellipsoid(top + Vector(face).normalized() * radius * 0.12, (radius * 0.3, radius * 0.3, radius * 0.22), segs=8, rings=5), centre_colour, sway=sw)


# ------------------------------------------------------------------------------------------------- trees

def tree_round(coll, name="TreeRound", far=False, seed=7, stops=LEAF_RAMP, flowers=14):
    B = Build(name)
    B.ao = 1.6
    segs = 7 if far else 12
    B.add(tube([(0, 0, -0.3), (0.12, 0.05, 1.2), (-0.05, 0, 2.3), (0.05, 0, 3.0)], [0.42, 0.32, 0.24, 0.2], segs=segs),
          by_height([(0, "bark_dark"), (0.6, "bark"), (1, "bark_light")], -0.3, 3.0, seed), sway=sway_height(1.0, 3.0, 2, 0.1))
    for s, a in ((1, 0.3), (-1, 2.4), (1, 4.2)):
        B.add(tube([(0.05, 0, 2.2), (math.cos(a) * 0.7, math.sin(a) * 0.7, 2.9), (math.cos(a) * 1.2, math.sin(a) * 1.2, 3.4)], [0.14, 0.08], segs=6),
              "bark", sway=0.1)
    balls = [((0, 0, 4.1), 1.55), ((1.15, 0.2, 3.7), 1.05), ((-1.1, -0.15, 3.75), 1.1), ((0.35, -0.85, 3.6), 1.0),
             ((-0.3, 0.85, 3.9), 1.0), ((0.15, 0.05, 5.0), 1.05), ((0.95, -0.5, 4.6), 0.8), ((-0.9, -0.55, 4.55), 0.82)]
    crown(B, balls, (0, 0, 4.0), 2.6, 6.0, seed, stops, subdiv=2 if far else 3)
    if flowers and not far:
        blossoms(B, balls, (0, 0, 4.0), flowers, seed + 50, ("blossom_light", "white"), (0.16, 0.22), above=0.0)
    return B.to_object(coll)


def tree_tall(coll, name="TreeTall", far=False, seed=20):
    B = Build(name)
    B.ao = 1.6
    segs = 7 if far else 12
    B.add(tube([(0, 0, -0.3), (0.1, 0, 1.6), (-0.05, 0, 3.0)], [0.36, 0.26, 0.2], segs=segs),
          by_height([(0, "bark_dark"), (0.7, "bark"), (1, "bark_light")], -0.3, 3.0, seed), sway=sway_height(1.0, 3.0, 2, 0.08))
    balls = [((0, 0, 4.2), (1.25, 1.25, 1.6)), ((0.1, 0.05, 5.8), (1.05, 1.05, 1.4)), ((0, 0, 7.1), (0.75, 0.75, 1.0)),
             ((0.7, -0.3, 4.9), 0.85), ((-0.7, -0.2, 5.1), 0.85), ((-0.2, 0.6, 4.6), 0.8)]
    crown(B, balls, (0, 0, 5.2), 2.8, 8.0, seed, TALL_RAMP, subdiv=2 if far else 3, k=0.75)
    return B.to_object(coll)


def pine(coll, name="Pine", far=False, seed=33):
    B = Build(name)
    B.ao = 1.4
    B.add(tube([(0, 0, -0.3), (0, 0, 1.6)], [0.3, 0.22], segs=7 if far else 10), "bark", sway=0.0)
    tiers = ((1.1, 1.9, 2.4), (2.5, 1.55, 2.2), (3.8, 1.15, 2.0), (5.0, 0.75, 1.7))
    rule = by_height(PINE_RAMP, 1.0, 6.9, seed, 0.08)
    for i, (z, r, h) in enumerate(tiers):
        def make(bm, z=z, r=r, h=h, i=i):
            # a soft cone with a scalloped hem (8 lobes): rings from the hem up to the tip
            segs = 16 if far else 24
            rows = 4 if far else 6
            ring_verts = []
            for j in range(rows):
                t = j / rows
                rad = r * (1 - t) ** 1.15
                zz = z + h * t - (0.18 * r if j == 0 else 0)
                ring = []
                for k in range(segs):
                    a = 2 * math.pi * k / segs + i * 0.4
                    lobe = 1 + (0.12 * math.cos(8 * a) if j == 0 else 0.05 * math.cos(8 * a) * (1 - t))
                    ring.append(bm.verts.new((math.cos(a) * rad * lobe, math.sin(a) * rad * lobe, zz)))
                ring_verts.append(ring)
            tip = bm.verts.new((0, 0, z + h))
            under = bm.verts.new((0, 0, z + 0.15 * h))
            for j in range(rows - 1):
                for k in range(segs):
                    k2 = (k + 1) % segs
                    bm.faces.new((ring_verts[j][k], ring_verts[j][k2], ring_verts[j + 1][k2], ring_verts[j + 1][k]))
            for k in range(segs):
                k2 = (k + 1) % segs
                bm.faces.new((ring_verts[-1][k], ring_verts[-1][k2], tip))
                bm.faces.new((ring_verts[0][k2], ring_verts[0][k], under))
        B.add(make, rule, sway=sway_height(1.0, 7.0, 1.5, 0.2), sphere=((0, 0, z + h * 0.3), 0.45))
    return B.to_object(coll)


# ------------------------------------------------------------------------------------------------- the fairy tree

def arch_inside(a, z, front=-math.pi / 2, r=1.2, half=0.5, top=1.95, soft=0.12):
    """How far inside the portal's arch a point on the trunk is (0 outside .. 1 well inside): the angle round the trunk
    and the height, the arch's half width (m, at radius r) and the height of its top."""
    da = math.atan2(math.sin(a - front), math.cos(a - front))
    x = abs(da) * r
    if x > half + soft:
        return 0.0
    edge_z = top - half + math.sqrt(max(0.0, half * half - min(x, half) ** 2)) if x <= half else top - half
    side = (half - x) / soft
    up = (edge_z - z) / soft
    low = (z + 0.2) / soft
    return max(0.0, min(1.0, min(side, up, low)))


def fairy_tree(coll):
    """The spirit tree at the heart of the garden (2026-11-17, the user: the treehouse was "very ugly, and not mystic
    enough"): a trunk of five strands twisting round each other, glowing veins in the grooves between them; a glowing
    portal in the trunk's foot (its door, -Y); roots flowing out with light running along them; the branches spreading
    into cushions of a luminous lilac canopy (cloud-pruned, softly glowing underneath); wisteria hanging from it, its
    blossoms glowing brighter toward their tips; spirit orbs on threads; crystals and glowing mushrooms at its foot."""
    B = Build("FairyTree")
    B.ao = 2.0
    rng = random.Random(77)
    strands, twist = 5, 3.4
    trunk = [(0, 0, -0.3), (0.08, 0.04, 1.0), (-0.08, 0.02, 2.2), (0.06, -0.04, 3.3), (0.0, 0.05, 4.4), (-0.1, 0.15, 5.6),
             (0.0, 0.3, 6.8)]
    radii = [1.3, 1.08, 0.92, 0.84, 0.74, 0.52, 0.34]
    path = smooth_path(trunk, 6)
    n = len(path)
    rs = lerp_list(radii, n)

    def zt(t):
        return path[0].z + (path[-1].z - path[0].z) * t

    # the trunk: strands bulging out and twisting up, dark grooves between them, the portal's niche at its foot
    def ridge(a, t):
        s = math.cos(strands * (a - twist * t))
        bulge = 0.17 * max(0.0, s) ** 1.4 - 0.05
        inside = arch_inside(a, zt(t))
        return bulge * (1 - inside) - 0.34 * inside

    def portal_colour(p, k):
        h = (p.z + 0.2) / 2.2
        return mix("portal_edge", "portal_core", k * k * (1.1 - 0.4 * h))

    def bark_rule(p, nrm):
        a = math.atan2(p.y, p.x)
        t = (p.z - path[0].z) / (path[-1].z - path[0].z)
        k = arch_inside(a, p.z)
        if k > 0.02:
            return mix("bark_myst", "portal_edge", k) if k < 0.5 else portal_colour(p, k)
        s = math.cos(strands * (a - twist * t))
        base = ramp([(0, "bark_myst_dark"), (0.45, "bark_myst"), (1, "bark_myst_light")], t * 0.8 + 0.25 * max(0.0, s) + 0.06 * noise(p * 1.4))
        return mix(base, "bark_myst_dark", max(0.0, -s) * 0.6)

    def bark_glow(p):
        a = math.atan2(p.y, p.x)
        k = arch_inside(a, p.z)
        return 1.0 if k > 0.55 else k * 0.8
    B.add(tube(trunk, radii, segs=40, sub=6, ridge=ridge), bark_rule, glow=bark_glow)

    # glowing veins in the grooves, winding up from the roots into the branches (cyan below, pink above)
    for m in range(strands):
        pts = []
        for i in range(2, int(n * 0.8)):
            t = i / (n - 1)
            a = twist * t + math.pi / strands + 2 * math.pi * m / strands
            if arch_inside(a, path[i].z) > 0:
                if pts:
                    break
                continue
            r = rs[i] * (1 - 0.035)
            pts.append(path[i] + Vector((math.cos(a) * r, math.sin(a) * r, 0)))
        if len(pts) > 3:
            B.add(tube(pts, 0.03, segs=4, sub=2), lambda p, nn: mix("vein", "vein_pink", (p.z - 0.3) / 4.5), glow=0.85)

    # the portal's frame: a twist of bark round the arch, a vein of light along it
    arch = []
    for k in range(17):
        u = k / 16
        if u < 0.25:
            x, z = -0.52, (u / 0.25) * 1.45 - 0.1
        elif u > 0.75:
            x, z = 0.52, ((1 - u) / 0.25) * 1.45 - 0.1
        else:
            ang = math.pi * (u - 0.25) / 0.5
            x, z = -0.52 * math.cos(ang), 1.35 + 0.52 * math.sin(ang)
        y = -math.sqrt(max(0.1, 1.24 ** 2 - x * x)) + 0.1 - 0.12 * (z / 1.9)
        arch.append((x, y, z))
    B.add(tube(arch, [0.15, 0.12, 0.15], segs=10, sub=2), "bark_myst_light")
    B.add(tube([(x * 1.0, y - 0.13, z + 0.04) for x, y, z in arch], 0.03, segs=4, sub=2), "vein", glow=0.85)
    B.add(blob((0, -1.2, 0.02), (0.75, 0.4, 0.1), 3, 0.1, 2), by_height([(0, "stone_dark"), (1, "stone")], -0.05, 0.12, 3))

    # roots flowing out (none across the portal), light running along their backs
    for deg in (12, 62, 112, 160, 205, 335):
        a = math.radians(deg)
        d = Vector((math.cos(a), math.sin(a), 0))
        side = Vector((-d.y, d.x, 0)) * 0.25 * math.sin(deg)
        pts = [d * 0.75 + Vector((0, 0, 0.85)), d * 1.6 + side + Vector((0, 0, 0.35)), d * 2.4 - side + Vector((0, 0, 0.1)), d * 3.0 + side * 0.5 + Vector((0, 0, -0.04))]
        rr = [0.55, 0.33, 0.18, 0.07]
        B.add(tube(pts, rr, segs=12), by_height([(0, "bark_myst_dark"), (1, "bark_myst")], -0.05, 0.9, deg))
        top = [Vector(q) + Vector((0, 0, r * 0.93)) for q, r in zip(pts[:3], rr[:3])]
        B.add(tube(top, 0.03, segs=4, sub=3), "vein", glow=0.85)

    # the canopy: puffy cushions of soft lilac with flat undersides (cloud-pruned), close together, glowing a little
    # underneath (they give the garden its light), each lit as one; limbs curving up out of the trunk carry them
    pads = [((0.0, 0.5, 8.1), (2.3, 2.0, 1.2)), ((-2.5, 0.0, 7.0), (1.85, 1.6, 1.0)), ((2.6, 0.2, 7.1), (1.9, 1.6, 1.0)),
            ((-1.35, -1.45, 6.05), (1.6, 1.35, 0.9)), ((1.55, -1.3, 5.9), (1.55, 1.3, 0.88)), ((0.2, 1.9, 6.6), (1.75, 1.5, 0.95)),
            ((-3.75, 0.9, 6.0), (1.3, 1.1, 0.78)), ((3.85, 0.8, 6.1), (1.3, 1.1, 0.78))]
    for i, (c, r) in enumerate(pads[1:], start=1):
        c = Vector(c)
        d = Vector((c.x, c.y, 0)).normalized()
        base = Vector((0, 0.05, 4.2 + 0.25 * (i % 3)))
        end = c - Vector((0, 0, r[2] * 0.35))
        ctrl = base + d * (Vector((c.x, c.y, 0)).length * 0.35) + Vector((0, 0, 0.2))
        B.add(tube(bezier(base, ctrl, end, 6), [0.4, 0.3, 0.2, 0.14], segs=10),
              by_height([(0, "bark_myst"), (1, "bark_myst_light")], 4.0, 7.5, i), sway=0.05)
    for i, (c, r) in enumerate(pads):
        c = Vector(c)
        lumps = [(c, r)]
        for j, (dx, dy, dz, k) in enumerate(((0.5, 0.3, 0.38, 0.6), (-0.5, -0.25, 0.36, 0.58), (0.1, -0.5, 0.3, 0.55),
                                              (-0.2, 0.55, 0.4, 0.55), (0.62, -0.35, 0.05, 0.5))):
            lumps.append((c + Vector((dx * r[0], dy * r[1], dz * r[2])), tuple(x * k for x in r)))
        z0, z1 = c.z - r[2] * 0.6, c.z + r[2] * 1.4
        flat = c.z - r[2] * 0.55

        def pad_rule(p, nn, z0=z0, z1=z1, i=i):
            t = (p.z - z0) / (z1 - z0) + 0.08 * noise(p * 1.1 + Vector((i, 0, 0)))
            return ramp([(0, "pad_under"), (0.3, "pad_mid"), (0.7, "pad_top"), (1, "pad_tip")], t)

        def pad_glow(p, z0=z0, z1=z1):
            return 0.4 * max(0.0, 1 - (p.z - z0) / (z1 - z0) * 1.8)
        for j, (lc, lr) in enumerate(lumps):
            others = [o for k, o in enumerate(lumps) if k != j]
            # the two low cushions over the fairy in full detail (their outline shows most), the rest lighter
            B.add(blob(lc, lr, 300 + i * 7 + j, 0.06, 4 if j == 0 and i in (3, 4) else 3, 1.4, flat_bottom=flat, others=others), pad_rule,
                  glow=pad_glow, sway=0.1, sphere=(c - Vector((0, 0, r[2] * 0.3)), 0.75))

    # wisteria hanging from the cushions: soft clusters of blossoms (one bumpy tapering shape each), lilac at the top,
    # glowing pink-white toward their tips, swaying
    for i, (c, r) in enumerate(pads[1:], start=1):
        c = Vector(c)
        for k in range(6 if c.z < 6.5 else 4):
            ang = rng.uniform(0, 2 * math.pi)
            if math.sin(ang) > 0.4:
                ang = -ang                                  # mostly on the front and the sides, where they show
            rad = rng.uniform(0.3, 0.85)
            top = c + Vector((math.cos(ang) * r[0] * rad, math.sin(ang) * r[1] * rad, -r[2] * 0.5))
            length = rng.uniform(0.75, 1.35)
            lean = Vector((rng.uniform(-0.12, 0.12), rng.uniform(-0.1, 0.05), 0))
            pts = [top + lean * (u * u) - Vector((0, 0, length * u)) for u in (0.0, 0.25, 0.5, 0.75, 1.0)]
            seed = rng.uniform(0, 6)

            def bumps(a, t, seed=seed):
                return 0.3 * (0.5 + 0.5 * math.cos(4 * a + seed)) * (0.5 + 0.5 * math.cos(t * 16 + seed)) - 0.1
            B.add(tube(pts, [0.2, 0.28, 0.25, 0.17, 0.1], segs=7, sub=2, ridge=bumps, start_cap=True),
                  lambda p, nn, top=top, length=length: ramp([(0, "wis_top"), (0.55, "wis_mid"), (1, "wis_bottom")], (top.z - p.z) / length * 1.05),
                  glow=lambda p, top=top, length=length: 0.2 + 0.45 * max(0.0, min(1.0, (top.z - p.z) / length)),
                  sway=lambda p, top=top, length=length: 0.3 + 0.7 * max(0.0, min(1.0, (top.z - p.z) / length)))

    # spirit orbs on threads under the canopy (their light the game's glows)
    for x, y, z in ((-2.3, -0.9, 4.75), (2.4, -0.7, 4.6), (0.95, -1.95, 4.35), (-1.0, -1.9, 4.6), (0.1, -0.6, 3.95)):
        B.add(tube([(x, y, z + 0.9), (x, y, z + 0.2)], 0.012, segs=4, sub=1, cap=False), "vein", sway=0.3, glow=0.6)
        B.add(ellipsoid((x, y, z), (0.17, 0.17, 0.2), segs=12, rings=8), lambda p, nn, z=z: mix("orb", "vein", (z + 0.2 - p.z) * 1.5),
              sway=0.3, glow=1.0)

    # crystals and glowing mushrooms at its foot, little flowers
    for side in (-1, 1):
        for k in range(4):
            a = math.radians(rng.uniform(20, 160))
            base = Vector((side * (1.35 + 0.12 * k), -0.95 - 0.08 * k, 0))
            B.add(crystal(base, (side * math.cos(a) * 0.35, -0.2, 1), rng.uniform(0.35, 0.8), rng.uniform(0.08, 0.14)),
                  ("crystal_a", "crystal_b", "crystal_c")[(k + (side > 0)) % 3], glow=0.65, flat=True)
    for x, y, h, r, cap in ((-1.95, -1.25, 0.42, 0.34, "cap_glow"), (2.05, -1.1, 0.36, 0.3, "cap_glow2"),
                            (-1.3, -1.9, 0.24, 0.2, "cap_glow2"), (1.5, -1.85, 0.28, 0.22, "cap_glow")):
        B.add(tube([(x, y, 0), (x + 0.03, y, h * 0.6), (x, y, h)], [r * 0.32, r * 0.26], segs=8, cap=False), "stalk")
        B.add(blob((x, y, h + r * 0.15), (r, r, r * 0.62), int(x * 10), 0.04, 2), cap, glow=0.6)
    for i in range(7):
        a = math.radians(205 + i * 22)
        p = Vector((math.cos(a) * 1.95, math.sin(a) * 1.7 - 0.2, 0))
        if abs(p.x) < 0.8:
            continue
        flower(B, p, rng.uniform(0.22, 0.35), ("white", "lilac", "pink", "blue")[i % 4], radius=0.12, seed=i)
    return B.to_object(coll)


# ------------------------------------------------------------------------------------------------- the meadow

def bush(coll, name="Bush", flowers=("white", "yellow"), seed=10, count=10):
    B = Build(name)
    B.ao = 0.7
    balls = [((0, 0, 0.6), 0.72), ((0.62, 0.1, 0.48), 0.52), ((-0.62, 0.0, 0.5), 0.56), ((0.1, 0.45, 0.72), 0.5), ((0.15, -0.35, 0.82), 0.45)]
    crown(B, balls, (0, 0, 0.45), 0.0, 1.3, seed, LEAF_RAMP, subdiv=3, sway=0.12, k=0.8, amp=0.1)
    if flowers:
        blossoms(B, balls, (0, 0, 0.45), count, seed + 5, flowers, (0.09, 0.13), above=0.15, sway=0.12, middle="centre")
    return B.to_object(coll)


def flower_patch(coll, name="FlowerPatch", colours=("pink", "white", "yellow", "lilac", "peach"), seed=4, n=6):
    B = Build(name)
    B.ao = 0.25
    rng = random.Random(seed)
    for i in range(n):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(0.0, 0.55)
        p = (math.cos(a) * r, math.sin(a) * r, 0)
        flower(B, p, rng.uniform(0.18, 0.42), colours[i % len(colours)], lobes=rng.choice((5, 5, 6)), radius=rng.uniform(0.08, 0.12),
               lean=(rng.uniform(-0.05, 0.05), rng.uniform(-0.05, 0.05)))
    for i in range(3):
        a = rng.uniform(0, 2 * math.pi)
        p = Vector((math.cos(a) * 0.3, math.sin(a) * 0.3, 0.02))
        d = Vector((math.cos(a), math.sin(a), 0))
        pts = bezier(p, p + d * 0.12 + Vector((0, 0, 0.12)), p + d * 0.3 + Vector((0, 0, 0.04)), 5)
        B.add(strip_leaf(pts, [0.02, 0.05, 0.06, 0.04, 0.01], 0.015, 0.01), "leaf", sway=0.2)
    return B.to_object(coll)


def grass(coll, name="GrassTuft", n=7, h=(0.22, 0.38), seed=1, flowers=0):
    B = Build(name)
    rng = random.Random(seed)
    top = h[1] * 1.05
    rule = lambda p, nn: ramp(GRASS_RAMP, p.z / top + 0.06 * noise(p * 4))
    for i in range(n):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(0.0, 0.12)
        base = (math.cos(a) * r, math.sin(a) * r, -0.02)
        B.add(blade(base, a + rng.uniform(-0.6, 0.6), rng.uniform(*h), rng.uniform(0.05, 0.08), rng.uniform(0.25, 0.6)),
              rule, sway=sway_height(0.0, top, 1.4), up=True)
    for i in range(flowers):
        a = rng.uniform(0, 2 * math.pi)
        p = (math.cos(a) * 0.1, math.sin(a) * 0.1, 0)
        flower(B, p, h[1] * rng.uniform(1.0, 1.3), ("white", "yellow", "pink")[i % 3], radius=0.06)
    obj = B.to_object(coll)
    obj["grass"] = True
    return obj


def fern(coll):
    B = Build("Fern")
    B.ao = 0.5
    for k in range(9):
        a = 2 * math.pi * k / 9 + (0.15 if k % 2 else 0)
        d = Vector((math.cos(a), math.sin(a), 0))
        reach = 1.05 if k % 2 else 1.3
        pts = bezier(Vector((0, 0, 0.05)), d * reach * 0.35 + Vector((0, 0, 1.05 if k % 2 else 1.2)), d * reach + Vector((0, 0, 0.35)), 15)
        widths = [0.19 * math.sin(math.pi * min(1, (i / 14) ** 0.8 * 1.05)) * (1.0 if i % 2 else 0.6) + 0.012 for i in range(15)]
        B.add(strip_leaf(pts, widths, 0.035, 0.02), lambda p, n: ramp(GRASS_RAMP, 0.25 + p.z * 0.55 + 0.1 * noise(p * 3)),
              sway=lambda p: min(1.0, (p.xy.length / 1.2) ** 1.3))
    return B.to_object(coll)


def rock(coll, name="Rock", size=(0.95, 0.8, 0.62), seed=4, moss=True):
    B = Build(name)
    B.ao = 0.6
    def rule(p, n):
        c = ramp([(0, "stone_dark"), (1, "stone")], 0.45 + 0.55 * n.z + 0.15 * noise(p * 2.5 + Vector((seed, 0, 0))))
        if moss and n.z > 0.55 + 0.15 * noise(p * 3):
            c = mix(c, "moss", 0.85)
        return c
    B.add(blob((0, 0, size[2] * 0.6), size, seed, 0.18, 3, 1.2), rule)
    return B.to_object(coll)


def path_stone(coll):
    B = Build("PathStone")
    B.ao = 0.15
    B.add(blob((0, 0, 0.03), (0.38, 0.3, 0.07), 9, 0.15, 2, 1.4),
          lambda p, n: ramp([(0, "stone_dark"), (1, "stone")], 0.55 + 0.45 * n.z + 0.12 * noise(p * 5)))
    return B.to_object(coll)


def pebbles(coll):
    B = Build("Pebbles")
    B.ao = 0.12
    rng = random.Random(5)
    for i in range(6):
        a, r = rng.uniform(0, 6.28), rng.uniform(0.05, 0.35)
        s = rng.uniform(0.05, 0.11)
        B.add(blob((math.cos(a) * r, math.sin(a) * r, s * 0.4), (s, s * 0.85, s * 0.6), i, 0.12, 1),
              lambda p, n: ramp([(0, "stone_dark"), (1, "stone")], 0.5 + 0.5 * n.z))
    return B.to_object(coll)


def mushroom(coll, name="Mushroom", cap="cap_red", spots="cap_spot", glow=0.0, height=0.75, r=0.55, seed=3):
    B = Build(name)
    B.ao = 0.35
    B.add(tube([(0, 0, 0), (0.04, 0, height * 0.55), (0, 0, height)], [r * 0.36, r * 0.3, r * 0.27], segs=12, cap=False),
          by_height([(0, "gill"), (1, "stalk")], 0, height, seed))
    top = height
    B.add(blob((0, 0, top + r * 0.12), (r, r, r * 0.66), seed, 0.04, 3, flat_bottom=top - r * 0.08), lambda p, n: mix(cap, "gill", 1.0 if p.z < top - r * 0.02 else 0.0), glow=lambda p: glow if p.z > top - r * 0.02 else glow * 0.5)
    rng = random.Random(seed)
    for k in range(7):
        a = rng.uniform(0, 2 * math.pi)
        el = rng.uniform(0.35, 1.15)
        d = Vector((math.cos(a) * math.cos(el), math.sin(a) * math.cos(el), math.sin(el)))
        p = Vector((0, 0, top + r * 0.12)) + Vector((d.x * r, d.y * r, d.z * r * 0.66)) * 0.97
        s = rng.uniform(0.09, 0.15) * r / 0.55
        B.add(ellipsoid(p, (s, s, s * 0.45), segs=8, rings=5), spots, glow=glow * 0.6)
    return B.to_object(coll)


def mushroom_trio(coll):
    B = Build("MushroomTrio")
    B.ao = 0.35
    for x, y, h, r, cap in ((0, 0, 0.55, 0.38, "cap_red"), (0.42, -0.18, 0.32, 0.24, "cap_red"), (-0.35, -0.22, 0.22, 0.18, "cap_glow2")):
        B.add(tube([(x, y, 0), (x + 0.03, y, h * 0.55), (x, y, h)], [r * 0.36, r * 0.3], segs=10, cap=False), "stalk")
        B.add(blob((x, y, h + r * 0.12), (r, r, r * 0.66), int(h * 20), 0.04, 2), cap, glow=0.5 if cap == "cap_glow2" else 0.0)
        for k in range(4):
            a = k * 1.7 + x
            p = Vector((x, y, h + r * 0.12)) + Vector((math.cos(a) * r * 0.55, math.sin(a) * r * 0.55, r * 0.5))
            B.add(ellipsoid(p, (r * 0.18, r * 0.18, r * 0.08), segs=6, rings=4), "cap_spot")
    return B.to_object(coll)


def crystal_cluster(coll):
    B = Build("CrystalCluster")
    B.ao = 0.3
    rng = random.Random(23)
    for k in range(8):
        h = rng.uniform(0.4, 1.3) * (1.3 if k == 0 else 1)
        r = rng.uniform(0.1, 0.18)
        a = rng.uniform(0, 2 * math.pi)
        base = Vector((math.cos(a) * 0.3 * (k > 0), math.sin(a) * 0.3 * (k > 0), -0.05))
        axis = (Vector((0, 0, 1)) + Vector((math.cos(a), math.sin(a), 0)) * rng.uniform(0.25, 0.6) * (k > 0)).normalized()
        colour = ("crystal_a", "crystal_b", "crystal_c")[k % 3]
        B.add(crystal(base, axis, h, r), lambda p, n, colour=colour: mix(colour, "white", max(0.0, p.z - 0.4) * 0.5), glow=0.65, flat=True)
    B.add(blob((0, 0, 0.0), (0.5, 0.45, 0.12), 2, 0.1, 2), "stone")
    return B.to_object(coll)


def lantern(coll):
    B = Build("Lantern")
    B.ao = 0.4
    B.add(tube([(0, 0, 0), (0, 0, 1.6), (0.06, 0, 1.95), (0.32, 0, 2.05), (0.5, 0, 1.95)], [0.06, 0.05, 0.045, 0.04, 0.035], segs=6), "lantern_metal")
    B.add(tube([(0.5, 0, 1.95), (0.5, 0, 1.78)], 0.015, segs=4, sub=1, cap=False), "lantern_metal", sway=0.4)
    B.add(cone((0.5, 0, 1.68), (0.5, 0, 1.82), 0.17, 10), "lantern_metal", sway=0.4)
    B.add(ellipsoid((0.5, 0, 1.52), (0.14, 0.14, 0.19), segs=12, rings=8), "lantern_light", sway=0.4, glow=1.0)
    B.add(ellipsoid((0.5, 0, 1.33), (0.07, 0.07, 0.04), segs=8, rings=4), "lantern_metal", sway=0.4)
    B.add(blob((0, 0, 0.0), (0.18, 0.18, 0.08), 1, 0.05, 1), "stone")
    flower(B, (0.12, -0.1, 0), 0.25, "pink", radius=0.08)
    flower(B, (-0.1, -0.06, 0), 0.2, "yellow", radius=0.07)
    return B.to_object(coll)


# the garden's decorations (they grow round the tree, a wave saved at a time): bigger, cuter flowers

def flower_daisy(coll):
    B = Build("FlowerDaisy")
    B.ao = 0.5
    sw = sway_height(0.0, 2.2, 1.6, 0.7)
    B.add(tube([(0, 0, 0), (0.05, 0.02, 0.8), (0.0, -0.05, 1.6), (-0.05, -0.12, 2.1)], [0.07, 0.06, 0.05], segs=7), "stem", sway=sw)
    for s, z in ((1, 0.55), (-1, 1.0)):
        pts = bezier((0, 0, z), (s * 0.3, -0.05, z + 0.25), (s * 0.55, -0.1, z + 0.15), 6)
        B.add(strip_leaf(pts, [0.02, 0.09, 0.12, 0.1, 0.06, 0.01]), "leaf", sway=sw)
    head = Vector((-0.05, -0.16, 2.12))
    B.add(lobed_disc(head, 0.55, 14, normal=(0, -1, 0.35), thick=0.06, inner=0.35, pts=126, cup=0.06), "white", sway=sw)
    B.add(ellipsoid(head + Vector((0, -0.06, 0.02)), (0.2, 0.12, 0.2), segs=14, rings=8), "centre", sway=sw)
    return B.to_object(coll)


def flower_tulip(coll):
    B = Build("FlowerTulip")
    B.ao = 0.5
    sw = sway_height(0.0, 2.0, 1.6, 0.7)
    B.add(tube([(0, 0, 0), (-0.04, 0.02, 0.9), (0.02, 0, 1.75)], [0.07, 0.06, 0.055], segs=7), "stem", sway=sw)
    for s in (1, -1):
        pts = bezier((0, 0, 0.1), (s * 0.25, -0.02, 0.7), (s * 0.18, -0.05, 1.25), 7)
        B.add(strip_leaf(pts, [0.06, 0.12, 0.14, 0.12, 0.09, 0.05, 0.01], 0.03, 0.02), "leaf", sway=sw)
    cup = Vector((0, -0.02, 1.95))
    for k in range(6):
        a = 2 * math.pi * k / 6
        p = cup + Vector((math.cos(a) * 0.16, math.sin(a) * 0.16, 0))
        B.add(ellipsoid(p, (0.17, 0.11, 0.36), rot=(math.sin(a) * 12, -math.cos(a) * 12, math.degrees(a)), segs=12, rings=8),
              lambda p, n: mix("rose", "pink", (p.z - 1.7) / 0.5), sway=sw)
    return B.to_object(coll)


def flower_bell(coll):
    B = Build("FlowerBell")
    B.ao = 0.5
    sw = sway_height(0.0, 2.0, 1.5, 0.7)
    stem = [(0, 0, 0), (0.05, 0, 1.0), (0.3, 0, 1.75), (0.75, 0, 1.95), (1.0, 0, 1.75)]
    B.add(tube(stem, [0.065, 0.05, 0.04], segs=7), "stem", sway=sw)
    path = smooth_path(stem, 4)
    for t in (0.45, 0.68, 0.92):
        p = path[int(t * (len(path) - 1))] - Vector((0, 0, 0.2))
        B.add(tube([p + Vector((0, 0, 0.2)), p + Vector((0, 0, 0.05))], 0.018, segs=4, sub=1, cap=False), "stem", sway=sw)
        B.add(lambda bm, p=p: ta.cone(bm, p + Vector((0, 0, 0.1)), p - Vector((0, 0, 0.28)), 0.24, segments=14), "lilac", sway=sw)
        B.add(ellipsoid(p + Vector((0, 0, 0.06)), (0.15, 0.15, 0.15), segs=12, rings=8), "lilac", sway=sw)
        B.add(ellipsoid(p - Vector((0, 0, 0.28)), (0.05, 0.05, 0.05), segs=8, rings=5), "yellow", sway=sw, glow=0.3)
    for s in (1, -1):
        pts = bezier((0, 0, 0.1), (s * 0.3, -0.02, 0.5), (s * 0.4, -0.05, 0.9), 6)
        B.add(strip_leaf(pts, [0.04, 0.1, 0.12, 0.1, 0.06, 0.01]), "leaf", sway=sw)
    return B.to_object(coll)


# ------------------------------------------------------------------------------------------------- far away

def cloud(coll, name, balls, seed):
    B = Build(name)
    B.ao = 8.0
    zs = [c[2] for c, r in balls]
    z0, z1 = min(zs) - 6, max(zs) + max(r for c, r in balls)
    rule = lambda p, n: mix(mix("cloud_under", "cloud", (p.z - z0) / (z1 - z0) * 1.4), "cloud_warm", max(0.0, n.x) * 0.5)
    shapes = [(c, (r, r * 0.8, r * 0.82)) for c, r in balls]
    for i, (c, rr) in enumerate(shapes):
        others = [o for j, o in enumerate(shapes) if j != i]
        B.add(blob(c, rr, seed + i, 0.06, 2 if rr[0] < 5 else 3, flat_bottom=0.0, others=others), rule, sphere=((0, 0, -4), 0.7))
    return B.to_object(coll)


def mountains(coll):
    """A long ridge of soft mountains with snowy tops, its front toward -Y, 420 m wide, 0 .. ~70 m high."""
    B = Build("Mountains")
    B.ao = 20.0
    cols, rows = 90, 7

    def h(x):
        u = x / 210
        peaks = 40 + 22 * math.sin(u * 7.3 + 0.6) + 14 * math.sin(u * 17.1 + 1.9) + 6 * noise(Vector((u * 9, 0.3, 0)))
        return max(12.0, peaks * (1 - 0.35 * abs(u) ** 2))

    def make(bm):
        grid = []
        for i in range(cols + 1):
            x = -210 + 420 * i / cols
            top = h(x)
            col = []
            for j in range(rows + 1):
                t = j / rows
                z = -6 + (top + 6) * t ** 0.9
                y = 18 * (1 - t) ** 2 + 4 * noise(Vector((x * 0.05, t * 3, 0.7)))   # the foot spreads toward the camera
                col.append(bm.verts.new((x, -y, z)))
            grid.append(col)
        for i in range(cols):
            for j in range(rows):
                bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))

    def rule(p, n):
        top = h(p.x)
        t = p.z / max(1.0, top)
        c = ramp([(0, "mount_base"), (0.7, "mount_rock"), (1, "mount_top")], t)
        if t > 0.78 + 0.08 * noise(Vector((p.x * 0.08, 0, 0))) and top > 38:
            c = mix(c, "snow", 0.9)
        return c
    B.add(make, rule)
    return B.to_object(coll)


def castle(coll):
    """A storybook castle for the far hill (a few hundred metres off: simple shapes, bright roofs)."""
    B = Build("Castle")
    B.ao = 3.0
    wall = lambda p, n: mix("castle_shade", "castle_wall", 0.5 + 0.5 * n.z + 0.25 * n.x)
    B.add(rounded_box((0, 0, 4), (7, 4, 4), segs=12, rings=8, power=6), wall)
    for x, y, r, h, roof in ((-7, -4, 1.8, 13, "roof_blue"), (7, -4, 1.8, 12, "roof_blue"), (-6.5, 4, 1.6, 10, "roof_pink"),
                             (6.5, 4, 1.6, 11, "roof_pink"), (0, 0, 2.6, 19, "roof_pink"), (2.8, -1.5, 1.2, 15, "roof_blue")):
        B.add(tube([(x, y, 0), (x, y, h)], r, segs=12, sub=1, cap=False), wall)
        B.add(cone((x, y, h - 0.2), (x, y, h + r * 2.4), r * 1.35, 12), roof)
        B.add(tube([(x, y, h + r * 2.3), (x, y, h + r * 2.3 + 1.6)], 0.08, segs=4, sub=1), "lantern_metal")
        B.add(rounded_box((x + 0.5, y, h + r * 2.3 + 1.3), (0.5, 0.05, 0.28), segs=6, rings=4), "rose")
        for k in range(2):
            z = h * (0.45 + 0.25 * k)
            B.add(rounded_box((x, y - r * 0.98, z), (0.3, 0.08, 0.5), segs=6, rings=4), "window", glow=0.4)
    return B.to_object(coll)


# ------------------------------------------------------------------------------------------------- all of it

PROPS = [
    fairy_tree,
    lambda c: tree_round(c), lambda c: tree_round(c, "TreeRoundFar", far=True),
    lambda c: tree_round(c, "TreeBlossom", seed=61, stops=BLOSSOM_RAMP, flowers=0), lambda c: tree_round(c, "TreeBlossomFar", far=True, seed=61, stops=BLOSSOM_RAMP),
    lambda c: tree_tall(c), lambda c: tree_tall(c, "TreeTallFar", far=True),
    lambda c: pine(c), lambda c: pine(c, "PineFar", far=True),
    lambda c: bush(c), lambda c: bush(c, "BushFlower", ("pink", "blossom_light", "rose"), seed=31, count=16),
    lambda c: flower_patch(c), lambda c: flower_patch(c, "FlowerPatchBlue", ("blue", "white", "lilac", "blue", "yellow"), seed=9),
    lambda c: grass(c, n=6), lambda c: grass(c, "GrassTall", n=7, h=(0.4, 0.62), seed=2), lambda c: grass(c, "GrassFlowers", n=4, h=(0.2, 0.32), seed=3, flowers=2),
    fern, lambda c: rock(c), lambda c: rock(c, "RockSmall", (0.45, 0.4, 0.3), 8), path_stone, pebbles,
    lambda c: mushroom(c), lambda c: mushroom(c, "MushroomGlow", "cap_glow", "white", 0.6, 1.0, 0.5, 5), mushroom_trio,
    crystal_cluster, lantern, flower_daisy, flower_tulip, flower_bell,
    lambda c: cloud(c, "Cloud1", [((0, 0, 6), 8), ((8, 1, 4.5), 6), ((-8.5, 0, 4.2), 6.2), ((3, -2, 9), 5.5), ((-4, 1, 8.5), 5), ((14, 0, 2.5), 4), ((-14.5, 1, 2.4), 3.8)], 101),
    lambda c: cloud(c, "Cloud2", [((0, 0, 5), 6.5), ((6.5, 0, 3.6), 4.6), ((-6, 1, 3.4), 4.8), ((2, -1, 7.5), 4.2)], 202),
    lambda c: cloud(c, "Cloud3", [((0, 0, 3.5), 5), ((5.5, 1, 2.5), 3.6), ((-5, 0, 2.4), 3.4), ((10, 0, 1.6), 2.6), ((-9.5, 0, 1.5), 2.4), ((1.5, -1, 5.6), 3.2)], 303),
    mountains, castle,
]


def build():
    ta.clear_scene()
    coll = ta.collection("FairyProps")
    for o in list(coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    made = [make(coll) for make in PROPS]
    return {o.name: ta.tri_count(o) for o in made}


def objects():
    return [o for o in bpy.data.collections["FairyProps"].objects if o.type == "MESH"]


def preview_material():
    mat = bpy.data.materials.get("GardenPreview")
    if mat:
        return mat
    mat = bpy.data.materials.new("GardenPreview")
    mat.use_backface_culling = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_name = "Col"
    ao = nt.nodes.new("ShaderNodeMath")
    ao.operation = "MULTIPLY_ADD"
    ao.inputs[1].default_value = 0.7
    ao.inputs[2].default_value = 0.3
    nt.links.new(attr.outputs["Alpha"], ao.inputs[0])
    mul = nt.nodes.new("ShaderNodeVectorMath")
    mul.operation = "SCALE"
    nt.links.new(attr.outputs["Color"], mul.inputs[0])
    nt.links.new(ao.outputs[0], mul.inputs["Scale"])
    nt.links.new(mul.outputs[0], bsdf.inputs["Base Color"])
    uv = nt.nodes.new("ShaderNodeUVMap")
    uv.uv_map = "Data"
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(uv.outputs["UV"], sep.inputs[0])
    nt.links.new(attr.outputs["Color"], bsdf.inputs["Emission Color"])
    glow = nt.nodes.new("ShaderNodeMath")
    glow.operation = "MULTIPLY"
    glow.inputs[1].default_value = 1.2
    nt.links.new(sep.outputs["Y"], glow.inputs[0])
    nt.links.new(glow.outputs[0], bsdf.inputs["Emission Strength"])
    bsdf.inputs["Roughness"].default_value = 0.8
    return mat


def bake_ao(samples=24):
    """Each prop's ambient occlusion on its own (standing on the ground) into its colour's alpha."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.render.bake.target = "VERTEX_COLORS"
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    coll = ta.collection("BakeGround")
    ground = bpy.data.objects.get("BakeGround")
    if ground is None:
        bm = bmesh.new()
        bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=60)
        ground = ta.mesh_object("BakeGround", bm, coll)
    done = 0
    for target in objects():
        if target.get("grass"):
            # grass: no bake, darker at its roots
            me = target.data
            col = me.color_attributes["Col"]
            for poly in me.polygons:
                for li in poly.loop_indices:
                    z = me.vertices[me.loops[li].vertex_index].co.z
                    c = col.data[li].color
                    col.data[li].color = (c[0], c[1], c[2], 0.45 + 0.55 * min(1.0, z / 0.3))
            continue
        sky = target.name.startswith(("Cloud", "Mountains"))
        for o in objects():
            o.hide_render = o is not target
            o.hide_set(o is not target)
        ground.hide_render = sky
        world.light_settings.distance = target.get("ao_distance", 1.0)
        me = target.data
        attr = me.color_attributes.get("AO") or me.color_attributes.new("AO", "FLOAT_COLOR", "POINT")
        me.color_attributes.active_color = attr
        for o in bpy.context.view_layer.objects:
            o.select_set(False)
        target.select_set(True)
        bpy.context.view_layer.objects.active = target
        bpy.ops.object.bake(type="AO")
        col = me.color_attributes["Col"]
        for poly in me.polygons:
            for li in poly.loop_indices:
                a = attr.data[me.loops[li].vertex_index].color
                c = col.data[li].color
                col.data[li].color = (c[0], c[1], c[2], (a[0] + a[1] + a[2]) / 3)
        me.color_attributes.remove(attr)
        me.color_attributes.active_color = me.color_attributes["Col"]
        done += 1
    for o in objects():
        o.hide_render = False
        o.hide_set(False)
    bpy.data.objects.remove(ground, do_unlink=True)
    return done


def preview():
    out_dir = os.path.join(HERE, "previews")
    os.makedirs(out_dir, exist_ok=True)
    objs = {o.name: o for o in objects()}
    groups = [
        ("garden_tree", ["FairyTree"], "#6E7FB8", -12, 6),
        ("garden_trees", ["TreeRound", "TreeBlossom", "TreeTall", "Pine"], "#F4D9C6", -10, 8),
        ("garden_far", ["TreeRoundFar", "TreeBlossomFar", "TreeTallFar", "PineFar", "Castle"], "#F4D9C6", -10, 8),
        ("garden_meadow", ["Bush", "BushFlower", "FlowerPatch", "FlowerPatchBlue", "GrassTuft", "GrassTall", "GrassFlowers", "Fern"], "#F4D9C6", -10, 18),
        ("garden_small", ["Rock", "RockSmall", "PathStone", "Pebbles", "Mushroom", "MushroomGlow", "MushroomTrio", "CrystalCluster", "Lantern"], "#F4D9C6", -10, 18),
        ("garden_decor", ["FlowerDaisy", "FlowerTulip", "FlowerBell"], "#F4D9C6", -10, 8),
        ("garden_sky", ["Cloud1", "Cloud2", "Cloud3"], "#8CC4F2", 0, 4),
    ]
    paths = []
    for name, names, bgc, yaw, pitch in groups:
        for o in objs.values():
            o.hide_render = True
        x = 0.0
        names = [n for n in names if n in objs]
        for n in names:
            o = objs[n]
            xs = [v.co.x for v in o.data.vertices]
            o.location = (x - min(xs), 0, 0)
            o.hide_render = False
            x += max(xs) - min(xs) + 0.6
        bpy.context.view_layer.update()
        top = max(max((objs[n].matrix_world @ v.co).z for v in objs[n].data.vertices) for n in names)
        bottom = min(min((objs[n].matrix_world @ v.co).z for v in objs[n].data.vertices) for n in names)
        width = x - 0.6
        dist = max(width / 0.72 * 1.12, (top - bottom) / 0.42 * 1.15, 2.5)
        ta.preview_rig(target=(width / 2, 0, (top + bottom) / 2), distance=dist, yaw_deg=yaw, pitch_deg=pitch, lens=50)
        key = bpy.data.objects.get("PreviewKey")
        key.rotation_euler = (math.radians(50), math.radians(-25), math.radians(-60))
        key.data.color = (1.0, 0.92, 0.8)
        bg = bpy.context.scene.world.node_tree.nodes.get("Background")
        bg.inputs["Color"].default_value = (*[ta.srgb_to_linear(c) for c in ta.hex_rgb(bgc)], 1)
        paths.append(ta.render_preview(os.path.join(out_dir, f"{name}.png"), res=(1800, 900)))
    for o in objs.values():
        o.location = (0, 0, 0)
        o.hide_render = False
    return paths


def export():
    os.makedirs(UNITY_ART, exist_ok=True)
    path = os.path.join(UNITY_ART, "FairyProps.fbx")
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects():
        o.location = (0, 0, 0)
        o.select_set(True)
        for a in list(o.data.color_attributes):
            if a.name != "Col":
                o.data.color_attributes.remove(a)
    bpy.context.view_layer.objects.active = objects()[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, bake_anim=False, mesh_smooth_type="OFF",
        use_mesh_modifiers=True, path_mode="STRIP", colors_type="LINEAR", use_custom_props=False)
    info = {o.name: {"tris": ta.tri_count(o), "verts": len(o.data.vertices)} for o in objects()}
    with open(os.path.join(HERE, "props.json"), "w", encoding="utf-8") as f:
        json.dump(info, f, indent=1)
    return path
