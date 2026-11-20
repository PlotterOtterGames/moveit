"""Vrapo!'s art kit helpers (Blender 5.1, headless): vertex-coloured props for the box's per-vertex light.

The pattern of Zana!'s garden kit (art/fairy/props.py, 2026-11-17), made generic for the runner: no textures, every
vertex carries what the game's Tundu/Valley shader needs, so the box lights it at the vertices:
    colour  ("Col", linear): rgb = its own colour (gradients painted in), a = baked ambient occlusion (0 shut .. 1 open)
    uv      ("Data"): u = how much it sways in the wind (0 rooted .. 1 the tips), v = how much it glows (lit, whatever the
            light: lava, eggs at night, glowing mushrooms)
    normal  crowns, bushes and clouds have their normals turned out from their middle (they light as one soft volume, the
            stylized-tree trick); grass points up (it lights like the ground it grows from)

Coordinates: Blender, Z up; a prop's front (what the runner's camera sees) faces +Y, its pivot is on the ground under it.
Unity gets (-x, z, -y) (see U()); the game's lanes run along Blender -Y.
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

# colour names any piece may use (sRGB hex); valley.py and dino.py add theirs
COLOURS = {}

# How much detail the pieces made now get (1 = full). valley.py builds the plants' far versions ("...Far", drawn from
# 24-30 m on: on the box every vertex runs the whole light) at 0.5: fewer leaflets and no frond stems, coarser balls,
# tubes, blades, leaves and petals. The layout stays (the same random numbers are drawn), so the switch moves nothing.
DETAIL = 1.0


def low():
    return DETAIL < 0.75


def fewer(n, lowest, power=1.0):
    """A count of segments for the detail being made."""
    return n if DETAIL >= 0.999 else max(lowest, int(round(n * DETAIL ** power)))


def rgb(h):
    if isinstance(h, str):
        h = COLOURS.get(h, h)
        return ta.hex_rgb(h)
    return tuple(h)


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


def by_noise(a, b, freq=1.5, seed=0.0, bias=0.0, sharp=1.0):
    """A colour rule: two colours mixed by noise (patches)."""
    off = Vector((seed * 2.3, seed * 1.1, seed * 3.7))

    def rule(p, n):
        t = 0.5 + 0.5 * noise(p * freq + off) * sharp + bias
        return mix(a, b, t)
    return rule


def U(x, y, z):
    """Unity coordinates (x right, y up, z ahead) -> Blender (what the FBX export turns back into those)."""
    return Vector((-x, -z, y))


# ------------------------------------------------------------------------------------------------- the builder

class Build:
    """Parts into one bmesh, each part's vertices remembering their colour rule, sway, glow and how their normals turn.
    colour: a colour name/hex/rgb or rule(p, n) -> rgb; sway, glow: a number or rule(p) -> number; sphere: (centre,
    amount) = normals turned out from that centre; up: normals point up (amount 0..1 or True); flat: faceted faces."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.vinfo = []
        self.ao = 1.0           # the AO bake's distance (m)
        self.grass = False      # no AO bake: darker at the roots instead
        self.two_sided = False  # single-sided leaves: the game draws it without culling

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
        for f in bm.faces:
            if f[fl] == 0:
                f[fl] = 1 if flat else -1
        return self

    def transform(self, m, from_part=0):
        """Moves the vertices of the parts added since `from_part` (a count of add() calls) by matrix m."""
        vl = self.bm.verts.layers.int.get("part")
        for v in self.bm.verts:
            if v[vl] > from_part:
                v.co = m @ v.co
        return self

    @property
    def parts(self):
        return len(self.vinfo)

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
                k = 1.0 if up is True else float(up)
                nr = (nr * (1 - k) + Vector((0, 0, 1)) * k).normalized()
            elif sphere is not None:
                c, k = sphere
                d = p - Vector(c)
                if d.length > 1e-6:
                    nr = (nr * (1 - k) + d.normalized() * k).normalized()
            cols[v.index] = colour(p, v.normal) if callable(colour) else rgb(colour)
            data[v.index] = (sway(p) if callable(sway) else sway, glow(p) if callable(glow) else glow)
            norms[v.index] = nr
        face_normals = [f.normal.copy() for f in bm.faces]
        # which part each vertex came from (the dino's rig weights its parts by it)
        self.vertex_parts = [v[vl] for v in bm.verts]
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
        me.materials.append(toon_material())
        obj["ao_distance"] = self.ao
        if self.grass:
            obj["grass"] = 1
        if self.two_sided or self.grass:
            obj["two_sided"] = 1
        return obj


# ------------------------------------------------------------------------------------------------- shapes

def buried(f, others, margin=0.84):
    """True when every corner of a face lies well inside one of `others` (centre, radii): hidden in a cluster of balls."""
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


def blob(c, radii, seed=0, amp=0.08, subdiv=3, freq=1.6, flat_bottom=None, others=(), squash_top=None):
    """A soft lumpy ball (an icosphere moved by noise); flat_bottom = z under which it's pressed flat (clouds, rocks); its
    faces buried inside `others` (the cluster's other balls: (centre, radii)) are left out."""
    def make(bm):
        geom = bmesh.ops.create_icosphere(bm, subdivisions=max(1, subdiv - 1) if low() else subdiv, radius=1.0)
        off = Vector((seed * 3.1, seed * 1.7, seed * 2.3))
        cc = Vector(c)
        for v in geom["verts"]:
            d = v.co.normalized()
            k = 1.0 + amp * noise(d * freq + off) + amp * 0.35 * noise(d * freq * 2.7 + off * 1.3)
            p = cc + Vector((d.x * radii[0] * k, d.y * radii[1] * k, d.z * radii[2] * k))
            if flat_bottom is not None and p.z < flat_bottom:
                p.z = flat_bottom + (p.z - flat_bottom) * 0.12
            if squash_top is not None and p.z > squash_top:
                p.z = squash_top + (p.z - squash_top) * 0.5
            v.co = p
        if others:
            faces = {f for v in geom["verts"] for f in v.link_faces}
            gone = [f for f in faces if buried(f, others)]
            if gone:
                bmesh.ops.delete(bm, geom=gone, context="FACES")
    return make


def ellipsoid(c, radii, rot=(0, 0, 0), segs=16, rings=10):
    return lambda bm: ta.ellipsoid(bm, c, radii, rot_deg=rot, segments=fewer(segs, 6, 0.5), rings=fewer(rings, 4, 0.5))


def cone(base, tip, r, segs=10, flat=1.0, side=None):
    return lambda bm: ta.cone(bm, base, tip, r, segments=fewer(segs, 5, 0.5), flat=flat, side=side)


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


def tube(pts, radii, segs=10, sub=3, cap=True, ridge=None, start_cap=False, squash=None):
    """A smooth tapering tube through points (parallel-transported rings); ridge(angle, t) -> extra radius share;
    squash = (axis vector, factor): rings flattened along that axis (a flat neck, a flat tail)."""
    def make(bm):
        segs_ = fewer(segs, 3, 0.5)
        path = smooth_path(pts, fewer(sub, 1))
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
            for k in range(segs_):
                a = 2 * math.pi * k / segs_
                d = nrm * math.cos(a) + bi * math.sin(a)
                rr = rs[i] * (1 + (ridge(a, i / max(1, n - 1)) if ridge else 0))
                off = d * rr
                if squash is not None:
                    ax, f = Vector(squash[0]).normalized(), squash[1]
                    off -= ax * off.dot(ax) * (1 - f)
                ring.append(bm.verts.new(path[i] + off))
            rings.append(ring)
        for i in range(n - 1):
            for k in range(segs_):
                k2 = (k + 1) % segs_
                bm.faces.new((rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]))
        if cap:
            tip = bm.verts.new(path[-1] + tans[-1] * rs[-1] * 0.9)
            for k in range(segs_):
                bm.faces.new((rings[-1][k], rings[-1][(k + 1) % segs_], tip))
        if start_cap:
            tip = bm.verts.new(path[0] - tans[0] * rs[0] * 0.3)
            for k in range(segs_):
                bm.faces.new((rings[0][(k + 1) % segs_], rings[0][k], tip))
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
        pts_ = max(lobes * 2, fewer(pts, 6))
        nrm = Vector(normal).normalized()
        ref = Vector((1, 0, 0)) if abs(nrm.x) < 0.9 else Vector((0, 1, 0))
        u = (ref - nrm * ref.dot(nrm)).normalized()
        w = nrm.cross(u)
        cc = Vector(c)
        top, bot = [], []
        for k in range(pts_):
            a = 2 * math.pi * k / pts_
            r = radius * (inner + (1 - inner) * abs(math.cos(lobes * a / 2)) ** 0.7)
            p = cc + (u * math.cos(a) + w * math.sin(a)) * r + nrm * (cup * (r / radius) ** 2)
            top.append(bm.verts.new(p + nrm * thick * 0.5))
            bot.append(bm.verts.new(p - nrm * thick * 0.5))
        ct = bm.verts.new(cc + nrm * thick * 0.8)
        cb = bm.verts.new(cc - nrm * thick * 0.8)
        for k in range(pts_):
            k2 = (k + 1) % pts_
            bm.faces.new((ct, top[k], top[k2]))
            bm.faces.new((cb, bot[k2], bot[k]))
            bm.faces.new((top[k2], top[k], bot[k], bot[k2]))
    return make


def blade(base, angle, height, width, lean, bend=0.5, rows=2):
    """A grass blade: a single-sided tapering strip curving over (the grass material draws both sides)."""
    def make(bm):
        rows_ = max(1, rows - 1) if low() else rows
        b = Vector(base)
        d = Vector((math.cos(angle), math.sin(angle), 0))
        side = Vector((-d.y, d.x, 0))
        left, right = [], []
        for i in range(rows_):
            t = i / rows_
            p = b + d * lean * height * t ** (1 + bend) + Vector((0, 0, height * t * (1 - 0.25 * lean * t)))
            wdt = width * (1 - t * 0.85)
            left.append(bm.verts.new(p - side * wdt * 0.5))
            right.append(bm.verts.new(p + side * wdt * 0.5))
        tip = bm.verts.new(b + d * lean * height + Vector((0, 0, height * (1 - 0.25 * lean))))
        for i in range(rows_ - 1):
            bm.faces.new((left[i], right[i], right[i + 1], left[i + 1]))
        bm.faces.new((left[-1], right[-1], tip))
    return make


def strip_leaf(pts, widths, rib=0.03, thick=0.02, up=Vector((0, 0, 1))):
    """A thin closed leaf along points (both sides), its middle raised by `rib`."""
    def make(bm):
        P = [Vector(p) for p in pts]
        W = list(widths)
        if low() and len(P) > 4:
            keep = sorted(set(list(range(0, len(P), 2)) + [len(P) - 1]))
            P, W = [P[i] for i in keep], [W[i] for i in keep]
        n = len(P)
        rows = []
        for i, p in enumerate(P):
            t = (P[min(n - 1, i + 1)] - P[max(0, i - 1)]).normalized()
            side = t.cross(up)
            if side.length < 1e-4:
                side = t.cross(Vector((1, 0, 0)))
            side.normalize()
            nrm = side.cross(t).normalized()
            wd = W[i]
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


def frond(base, direction, length, width, rise=0.35, droop=1.0, segs=7, leaflets=0, up=Vector((0, 0, 1))):
    """A fern or palm frond: a leaf strip that rises and droops, optionally serrated into leaflets (its edge zigzags).
    direction: a horizontal-ish vector; length, width in metres."""
    b = Vector(base)
    d = Vector(direction).normalized()
    pts, widths = [], []
    for i in range(segs + 1):
        t = i / segs
        p = b + d * length * t + Vector((0, 0, length * (rise * math.sin(math.pi * t * 0.8) - droop * 0.35 * t * t)))
        w = width * math.sin(math.pi * min(1.0, t * 1.05 + 0.05)) ** 0.8
        if leaflets and 0 < i < segs:
            w *= 1.0 if i % 2 else 0.55
        pts.append(p)
        widths.append(max(0.004, w))
    return strip_leaf(pts, widths, rib=width * 0.18, thick=width * 0.08, up=up)


def pinnate(base, direction, length, width, rise=0.35, droop=1.0, pairs=10, rachis=0.012, tilt=0.35, taper=0.75,
            leaflet_shape=0.28):
    """A feathery frond (ferns, cycads, palms): a thin rachis that rises and droops, with pairs of narrow single-sided
    leaflets along it (draw it with a material that shows both sides). width: a leaflet's length at the frond's widest;
    tilt: the leaflets lift above the frond's plane (a soft V); taper: how much shorter they get toward the tip."""
    def make(bm):
        b = Vector(base)
        d = Vector(direction)
        d.z = 0
        d.normalize()
        side = Vector((-d.y, d.x, 0))
        pairs_ = fewer(pairs, 3)
        shape = min(1.0, leaflet_shape * (pairs / pairs_) ** 0.5)
        n = pairs_ * 2 + 2

        def at(t):
            return b + d * length * t + Vector((0, 0, length * (rise * math.sin(math.pi * t * 0.8) - droop * 0.35 * t * t)))
        pts = [at(i / (n - 1)) for i in range(n)]
        # the rachis: a thin flat ribbon (2 faces wide), upright
        prev = None
        for i, p in enumerate(pts if not low() else []):     # (far away the stem doesn't show: leaflets only)
            t = (pts[min(n - 1, i + 1)] - pts[max(0, i - 1)]).normalized()
            s2 = t.cross(Vector((0, 0, 1)))
            if s2.length < 1e-4:
                s2 = side.copy()
            s2.normalize()
            r = rachis * (1 - 0.7 * i / (n - 1))
            row = (bm.verts.new(p - s2 * r), bm.verts.new(p + s2 * r))
            if prev is not None:
                bm.faces.new((prev[0], prev[1], row[1], row[0]))
            prev = row
        # the leaflets: narrow diamonds, longest near the middle
        for k in range(pairs_):
            t = 0.12 + 0.84 * k / max(1, pairs_ - 1)
            p = at(t)
            tan = (at(min(1.0, t + 0.02)) - at(max(0.0, t - 0.02))).normalized()
            s2 = tan.cross(Vector((0, 0, 1)))
            if s2.length < 1e-4:
                s2 = side.copy()
            s2.normalize()
            up = s2.cross(tan).normalized()
            L = width * math.sin(math.pi * min(1.0, 0.15 + t)) ** 0.6 * (1 - taper * max(0.0, t - 0.5))
            for sgn in (1, -1):
                out = (s2 * sgn * (1 - tilt) + up * tilt * 0.9 + tan * 0.35).normalized()
                tip = p + out * L
                mid = p + out * L * 0.45
                w = L * shape * 0.5
                a1 = bm.verts.new(p - tan * w * 0.6)
                a2 = bm.verts.new(mid + tan * w)
                a3 = bm.verts.new(tip)
                a4 = bm.verts.new(mid - tan * w)
                bm.faces.new((a1, a2, a3, a4) if sgn > 0 else (a1, a4, a3, a2))
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


def crown(B, balls, centre, z0, z1, seed, stops, subdiv=3, sway=0.22, k=0.8, amp=0.08):
    """A crown: soft balls coloured as one (the ramp by height), normals out from the crown's middle."""
    rule = by_height(stops, z0, z1, seed)
    shapes = [(c, r if isinstance(r, tuple) else (r, r, r * 0.92)) for c, r in balls]
    for i, (c, rr) in enumerate(shapes):
        others = [o for j, o in enumerate(shapes) if j != i]
        B.add(blob(c, rr, seed + i, amp, subdiv, others=others), rule, sway=sway, sphere=(centre, k))


def rock_shape(c, radii, seed, jag=0.14, subdiv=2, flat_bottom=None, facet=True):
    """A chunky stylised rock: a noisy icosphere with a few big planes cut into it (flat facets, soft edges)."""
    def make(bm):
        rng = random.Random(seed)
        geom = bmesh.ops.create_icosphere(bm, subdivisions=max(1, subdiv - 1) if low() else subdiv, radius=1.0)
        off = Vector((seed * 1.3, seed * 2.1, seed * 0.7))
        cuts = [(Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-0.2, 1))).normalized(), rng.uniform(0.62, 0.82))
                for _ in range(6)] if facet else []
        cc = Vector(c)
        for v in geom["verts"]:
            d = v.co.normalized()
            k = 1.0 + jag * noise(d * 1.4 + off)
            q = d * k
            for nrm, depth in cuts:
                h = q.dot(nrm)
                if h > depth:
                    q -= nrm * (h - depth) * 0.85
            p = cc + Vector((q.x * radii[0], q.y * radii[1], q.z * radii[2]))
            if flat_bottom is not None and p.z < flat_bottom:
                p.z = flat_bottom + (p.z - flat_bottom) * 0.15
            v.co = p
    return make


def spotted(base, spot, cells=7.0, size=0.22, seed=0.0):
    """A colour rule: a base colour with round soft spots (noise cells)."""
    off = Vector((seed * 5.1, seed * 2.7, seed * 1.9))

    def rule(p, n):
        q = p * cells + off
        v = noise(q) * 0.5 + 0.5
        return mix(base, spot, (v - (1.0 - size)) / 0.06)
    return rule


# ------------------------------------------------------------------------------------------------- preview material

def _sock(sockets, identifier):
    """A node socket by its identifier (a Mix node has a Float, a Vector and a Color socket all named "A")."""
    for s in sockets:
        if s.identifier == identifier:
            return s
    return sockets[identifier]


def toon_material(name="ValleyToon", shade=(0.42, 0.44, 0.78), sunlight=(1.05, 0.98, 0.88), rim_strength=0.22):
    """A preview of the game's look in EEVEE: the vertex colour lit by a soft toon terminator (cool violet shade, warm
    sun), the sky from above, the AO, a rim, the glow (uv v). Emission only: the colour is what the game draws."""
    mat = bpy.data.materials.get(name)
    if mat:
        return mat
    mat = bpy.data.materials.new(name)
    mat.use_backface_culling = False
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    L = nt.links.new
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_name = "Col"
    diffuse = nt.nodes.new("ShaderNodeBsdfDiffuse")
    diffuse.inputs["Color"].default_value = (1, 1, 1, 1)
    to_rgb = nt.nodes.new("ShaderNodeShaderToRGB")
    L(diffuse.outputs[0], to_rgb.inputs[0])
    bw = nt.nodes.new("ShaderNodeRGBToBW")
    L(to_rgb.outputs["Color"], bw.inputs[0])
    lit = nt.nodes.new("ShaderNodeMapRange")          # the terminator, soft
    lit.interpolation_type = "SMOOTHSTEP"
    lit.inputs["From Min"].default_value = 0.05
    lit.inputs["From Max"].default_value = 0.2
    L(bw.outputs[0], lit.inputs["Value"])
    light = nt.nodes.new("ShaderNodeMix")
    light.data_type = "RGBA"
    _sock(light.inputs, "A_Color").default_value = (*shade, 1)
    _sock(light.inputs, "B_Color").default_value = (*sunlight, 1)
    L(lit.outputs[0], _sock(light.inputs, "Factor_Float"))
    # ambient occlusion (the colour's alpha) darkens
    ao = nt.nodes.new("ShaderNodeMath")
    ao.operation = "MULTIPLY_ADD"
    ao.inputs[1].default_value = 0.65
    ao.inputs[2].default_value = 0.35
    L(attr.outputs["Alpha"], ao.inputs[0])
    lit_col = nt.nodes.new("ShaderNodeVectorMath")
    lit_col.operation = "MULTIPLY"
    L(attr.outputs["Color"], lit_col.inputs[0])
    L(_sock(light.outputs, "Result_Color"), lit_col.inputs[1])
    scale = nt.nodes.new("ShaderNodeVectorMath")
    scale.operation = "SCALE"
    L(lit_col.outputs[0], scale.inputs[0])
    L(ao.outputs[0], scale.inputs["Scale"])
    # sky light from above
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    L(geo.outputs["Normal"], sep.inputs[0])
    sky = nt.nodes.new("ShaderNodeMath")
    sky.operation = "MULTIPLY_ADD"
    sky.inputs[1].default_value = 0.1
    sky.inputs[2].default_value = 0.16
    L(sep.outputs["Z"], sky.inputs[0])
    amb = nt.nodes.new("ShaderNodeVectorMath")
    amb.operation = "SCALE"
    L(attr.outputs["Color"], amb.inputs[0])
    L(sky.outputs[0], amb.inputs["Scale"])
    add = nt.nodes.new("ShaderNodeVectorMath")
    add.operation = "ADD"
    L(scale.outputs[0], add.inputs[0])
    L(amb.outputs[0], add.inputs[1])
    # a rim of light
    lw = nt.nodes.new("ShaderNodeLayerWeight")
    lw.inputs["Blend"].default_value = 0.25
    rim = nt.nodes.new("ShaderNodeMath")
    rim.operation = "POWER"
    rim.inputs[1].default_value = 2.5
    L(lw.outputs["Facing"], rim.inputs[0])
    rim_k = nt.nodes.new("ShaderNodeMath")
    rim_k.operation = "MULTIPLY"
    rim_k.inputs[1].default_value = rim_strength
    L(rim.outputs[0], rim_k.inputs[0])
    comb = nt.nodes.new("ShaderNodeCombineXYZ")
    for k in ("X", "Y", "Z"):
        L(rim_k.outputs[0], comb.inputs[k])
    add2 = nt.nodes.new("ShaderNodeVectorMath")
    add2.operation = "ADD"
    L(add.outputs[0], add2.inputs[0])
    L(comb.outputs[0], add2.inputs[1])
    # glow (uv v): the colour as it is, bright
    uvmap = nt.nodes.new("ShaderNodeUVMap")
    uvmap.uv_map = "Data"
    sep2 = nt.nodes.new("ShaderNodeSeparateXYZ")
    L(uvmap.outputs["UV"], sep2.inputs[0])
    glowc = nt.nodes.new("ShaderNodeVectorMath")
    glowc.operation = "SCALE"
    glowc.inputs["Scale"].default_value = 1.5
    L(attr.outputs["Color"], glowc.inputs[0])
    glowmix = nt.nodes.new("ShaderNodeMix")
    glowmix.data_type = "VECTOR"
    L(sep2.outputs["Y"], _sock(glowmix.inputs, "Factor_Float"))
    L(add2.outputs[0], _sock(glowmix.inputs, "A_Vector"))
    L(glowc.outputs[0], _sock(glowmix.inputs, "B_Vector"))
    emit = nt.nodes.new("ShaderNodeEmission")
    L(_sock(glowmix.outputs, "Result_Vector"), emit.inputs["Color"])
    emit.inputs["Strength"].default_value = 1.0
    L(emit.outputs[0], out.inputs["Surface"])
    return mat


# ------------------------------------------------------------------------------------------------- bake, export

def bake_ao(objs, samples=24, ground=True):
    """Each object's ambient occlusion on its own (standing on the ground) into its colour's alpha. Grass gets no bake:
    darker at its roots instead."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.render.bake.target = "VERTEX_COLORS"
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    coll = ta.collection("BakeGround")
    plane = bpy.data.objects.get("BakeGround")
    if plane is None:
        bm = bmesh.new()
        bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=80)
        plane = ta.mesh_object("BakeGround", bm, coll)
    bpy.context.view_layer.update()
    hidden = [o for o in bpy.data.objects if o is not None and o.type == "MESH" and o not in objs and o is not plane
              and not o.hide_render]
    for o in hidden:
        o.hide_render = True
    done = 0
    for target in objs:
        if target.get("grass"):
            me = target.data
            col = me.color_attributes["Col"]
            top = max((v.co.z for v in me.vertices), default=0.3) or 0.3
            for poly in me.polygons:
                for li in poly.loop_indices:
                    z = me.vertices[me.loops[li].vertex_index].co.z
                    c = col.data[li].color
                    col.data[li].color = (c[0], c[1], c[2], 0.45 + 0.55 * min(1.0, z / (top * 0.7)))
            continue
        sky = target.get("sky", 0)
        for o in objs:
            o.hide_render = o is not target
            o.hide_set(o is not target)
        plane.hide_render = bool(sky) or not ground
        world.light_settings.distance = target.get("ao_distance", 1.0)
        me = target.data
        attr = me.color_attributes.get("AO") or me.color_attributes.new("AO", "FLOAT_COLOR", "POINT")
        me.color_attributes.active_color = attr
        for o in bpy.context.view_layer.objects:
            if o is not None:
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
    for o in objs:
        o.hide_render = False
        o.hide_set(False)
    for o in hidden:
        o.hide_render = False
    bpy.data.objects.remove(plane, do_unlink=True)
    return done


def export_fbx(objs, path):
    """The objects into one FBX (each its own mesh, pivot where it is: put them at the origin first)."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
        for a in list(o.data.color_attributes):
            if a.name != "Col":
                o.data.color_attributes.remove(a)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, bake_anim=False, mesh_smooth_type="OFF",
        use_mesh_modifiers=True, path_mode="STRIP", colors_type="LINEAR", use_custom_props=False)
    return path


# ------------------------------------------------------------------------------------------------- preview

def world_colour(hex_colour, strength=1.0):
    """The background the camera sees; nothing else is lit by it (the toon material's light is the sun's alone)."""
    scene = bpy.context.scene
    world = bpy.data.worlds.get("ValleyWorld") or bpy.data.worlds.new("ValleyWorld")
    scene.world = world
    world.use_nodes = True
    nt = world.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputWorld")
    bg = nt.nodes.new("ShaderNodeBackground")
    bg.inputs["Color"].default_value = (*[ta.srgb_to_linear(c) for c in ta.hex_rgb(hex_colour)], 1)
    bg.inputs["Strength"].default_value = strength
    dark = nt.nodes.new("ShaderNodeBackground")
    dark.inputs["Color"].default_value = (0, 0, 0, 1)
    path = nt.nodes.new("ShaderNodeLightPath")
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(path.outputs["Is Camera Ray"], mix.inputs[0])
    nt.links.new(dark.outputs[0], mix.inputs[1])
    nt.links.new(bg.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs["Surface"])


def sun(rotation_deg=(48, 0, 155), energy=3.0, colour=(1.0, 0.95, 0.85)):
    """One sun for the toon previews (the material reads light/shade from it)."""
    coll = ta.collection("Preview")
    for name in ("PreviewKey", "PreviewFill"):
        o = bpy.data.objects.get(name)
        if o is not None:
            o.hide_render = True
    light = bpy.data.objects.get("ValleySun")
    if light is None:
        light = bpy.data.objects.new("ValleySun", bpy.data.lights.new("ValleySun", "SUN"))
        coll.objects.link(light)
    light.hide_render = False
    light.rotation_euler = [math.radians(a) for a in rotation_deg]
    light.data.energy = energy
    light.data.color = colour
    light.data.angle = math.radians(2)
    return light


def camera(name, location, look_at, fov_v_deg=60.0, clip=400.0):
    coll = ta.collection("Preview")
    cam = bpy.data.objects.get(name)
    if cam is None:
        cam = bpy.data.objects.new(name, bpy.data.cameras.new(name))
        coll.objects.link(cam)
    cam.data.sensor_fit = "VERTICAL"
    cam.data.angle_y = math.radians(fov_v_deg)
    cam.data.clip_end = clip
    cam.data.clip_start = 0.1
    cam.location = Vector(location)
    cam.rotation_euler = (Vector(look_at) - Vector(location)).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = cam
    return cam


def render(path, res=(1280, 720), samples=16):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    try:
        scene.eevee.taa_render_samples = samples
    except AttributeError:
        pass
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = path
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.render.render(write_still=True)
    return path


def lineup(objs, gap=0.6):
    """Puts objects side by side along X (for a sheet), returns (width, bottom, top)."""
    x = 0.0
    top, bottom = -1e9, 1e9
    for o in objs:
        xs = [v.co.x for v in o.data.vertices]
        zs = [v.co.z for v in o.data.vertices]
        o.location = (x - min(xs), 0, 0)
        o.hide_render = False
        x += max(xs) - min(xs) + gap
        top, bottom = max(top, max(zs)), min(bottom, min(zs))
    return x - gap, bottom, top
