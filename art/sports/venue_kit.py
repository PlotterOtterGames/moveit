"""Building blocks for Sporte!'s venues (run inside Blender 5.1): geometry is written in UNITY coordinates (x = right,
y = up, z = forward, metres) and converted, colours are sRGB hex strings stored as linear vertex colours (Unity reads
vertex colours as they are; the project is linear), alpha = gloss for Tundu/Toon.

    Geo        a mesh being built: boxes with bevelled edges, cylinders, quads, strips; per-corner colours
    U(x, y, z) a Unity position in Blender axes
    export()   objects to an FBX in Assets/Games/Sports/Art/Venues (no materials: the game gives them theirs)
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "tools"))
import tundu_art as ta  # noqa: E402

UNITY_VENUES = os.path.join(ta.REPO, "unity", "MoveItGames", "Assets", "Games", "Sports", "Art", "Venues")


def U(x, y, z):
    """A Unity position (x right, y up, z forward) in Blender's axes (facing -Y, Z up, the left = +X)."""
    return Vector((-x, -z, y))


def lin(hexc, gloss=0.0, shade=1.0):
    r, g, b = ta.hex_rgb(hexc)
    return (ta.srgb_to_linear(r) * shade, ta.srgb_to_linear(g) * shade, ta.srgb_to_linear(b) * shade, gloss)


def mix(a, b, t):
    ra, ga, ba = ta.hex_rgb(a)
    rb, gb, bb = ta.hex_rgb(b)
    r, g, bl = ra + (rb - ra) * t, ga + (gb - ga) * t, ba + (bb - ba) * t
    return "#%02X%02X%02X" % (round(r * 255), round(g * 255), round(bl * 255))


class Geo:
    """Faces with per-corner colours (and texture coordinates) in Unity coordinates."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.col = self.bm.loops.layers.float_color.new("Col")
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.flat = set()

    # ---- primitives

    def face(self, pts, colour, uv=None, flat=True):
        """A polygon through Unity points (counter-clockwise seen from the side it faces)."""
        verts = [self.bm.verts.new(U(*p)) for p in pts]
        # Unity -> Blender flips handedness (x mirrored): reverse to keep the facing side
        f = self.bm.faces.new(list(reversed(verts)))
        cols = colour if isinstance(colour, list) else [colour] * len(pts)
        uvs = list(reversed(uv)) if uv else None
        for k, loop in enumerate(f.loops):
            c = cols[len(pts) - 1 - k]
            loop[self.col] = lin(*c) if isinstance(c, tuple) else lin(c)
            if uvs:
                loop[self.uv].uv = uvs[k]
        if flat:
            self.flat.add(f)
        return f

    def quad(self, a, b, c, d, colour, uv=None, normal=None):
        if normal is not None:
            return self.face_out([a, b, c, d], colour, normal, uv)
        return self.face([a, b, c, d], colour, uv)

    def face_out(self, pts, colour, normal, uv=None):
        """A polygon facing `normal` (a Unity direction): its corners are put in the right order."""
        v = [Vector(p) for p in pts]
        n = Vector((0, 0, 0))
        for i in range(1, len(v) - 1):
            n += (v[i] - v[0]).cross(v[i + 1] - v[0])
        if n.dot(Vector(normal)) < 0:
            pts = list(reversed(pts))
            if isinstance(colour, list):
                colour = list(reversed(colour))
            if uv:
                uv = list(reversed(uv))
        return self.face(pts, colour, uv)

    def box(self, centre, size, colour, rot_y=0.0, bevel=0.0, top=None, side=None, bottom=None, tilt_x=0.0):
        """A box (Unity centre and size), turned rot_y degrees around y (and tilt_x around x first); bevelled edges
        catch the light. Colours: one for all, or top / side / bottom."""
        hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
        m = (Matrix.Translation(Vector(centre)) @ Matrix.Rotation(math.radians(rot_y), 4, "Y")
             @ Matrix.Rotation(math.radians(tilt_x), 4, "X"))

        def P(x, y, z):
            v = m @ Vector((x, y, z))
            return (v.x, v.y, v.z)
        b = bevel
        top = top or colour
        side = side or colour
        bottom = bottom or side
        if b <= 0:
            c = [P(sx * hx, sy * hy, sz * hz) for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
            # indices: (sx, sy, sz) -> ((sx>0)*4 + (sy>0)*2 + (sz>0))
            def I(sx, sy, sz):
                return c[(sx > 0) * 4 + (sy > 0) * 2 + (sz > 0)]
            self.quad(I(-1, 1, -1), I(-1, 1, 1), I(1, 1, 1), I(1, 1, -1), top)          # top (+y)
            self.quad(I(-1, -1, -1), I(1, -1, -1), I(1, -1, 1), I(-1, -1, 1), bottom)   # bottom
            self.quad(I(-1, -1, -1), I(-1, 1, -1), I(1, 1, -1), I(1, -1, -1), side)     # front (-z)
            self.quad(I(1, -1, 1), I(1, 1, 1), I(-1, 1, 1), I(-1, -1, 1), side)         # back (+z)
            self.quad(I(-1, -1, 1), I(-1, 1, 1), I(-1, 1, -1), I(-1, -1, -1), side)     # left (-x)
            self.quad(I(1, -1, -1), I(1, 1, -1), I(1, 1, 1), I(1, -1, 1), side)         # right (+x)
            return
        # bevelled: the six faces shrunk by b, joined by chamfer faces (edges) and corner triangles
        def face_pts(axis, s):
            # four corners of the face on axis (0 x, 1 y, 2 z) at side s, inset by b on the other axes
            o = [hx, hy, hz]
            idx = [i for i in range(3) if i != axis]
            corners = []
            for (u, v) in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                q = [0.0, 0.0, 0.0]
                q[axis] = s * o[axis]
                q[idx[0]] = u * (o[idx[0]] - b)
                q[idx[1]] = v * (o[idx[1]] - b)
                corners.append(tuple(q))
            return corners

        def emit(local_pts, colour):
            pts = [P(*q) for q in local_pts]
            # orient outward: compare with the box centre
            a, bb, cc = Vector(pts[0]), Vector(pts[1]), Vector(pts[2])
            n = (bb - a).cross(cc - a)
            ctr = sum((Vector(p) for p in pts), Vector()) / len(pts)
            if n.dot(ctr - Vector(centre)) < 0:
                pts = list(reversed(pts))
            self.face(pts, colour)

        cols = {(1, 1): top, (1, -1): bottom}
        for axis in range(3):
            for s in (-1, 1):
                emit(face_pts(axis, s), cols.get((axis, s), side))
        # chamfers along the 12 edges
        o = [hx, hy, hz]
        for a1 in range(3):
            for a2 in range(a1 + 1, 3):
                free = 3 - a1 - a2
                for s1 in (-1, 1):
                    for s2 in (-1, 1):
                        q = []
                        for t in (-1, 1):
                            p1 = [0.0, 0.0, 0.0]
                            p1[a1] = s1 * o[a1]
                            p1[a2] = s2 * (o[a2] - b)
                            p1[free] = t * (o[free] - b)
                            p2 = [0.0, 0.0, 0.0]
                            p2[a1] = s1 * (o[a1] - b)
                            p2[a2] = s2 * o[a2]
                            p2[free] = t * (o[free] - b)
                            q.append((p1, p2))
                        c = top if (a1 == 1 and s1 > 0) or (a2 == 1 and s2 > 0) else side
                        emit([tuple(q[0][0]), tuple(q[1][0]), tuple(q[1][1]), tuple(q[0][1])], c)
        for sx in (-1, 1):
            for sy in (-1, 1):
                for sz in (-1, 1):
                    tri = [(sx * hx, sy * (hy - b), sz * (hz - b)), (sx * (hx - b), sy * hy, sz * (hz - b)),
                           (sx * (hx - b), sy * (hy - b), sz * hz)]
                    emit(tri, top if sy > 0 else side)

    def cylinder(self, a, b, r, colour, segs=10, caps=True, r2=None, cap_colour=None):
        """A cylinder (or cone, r2) between two Unity points."""
        a, b = Vector(a), Vector(b)
        d = (b - a).normalized()
        x = d.cross(Vector((0, 1, 0)) if abs(d.y) < 0.9 else Vector((1, 0, 0))).normalized()
        y = d.cross(x).normalized()
        r2 = r if r2 is None else r2
        ra = [a + (x * math.cos(2 * math.pi * i / segs) + y * math.sin(2 * math.pi * i / segs)) * r for i in range(segs)]
        rb = [b + (x * math.cos(2 * math.pi * i / segs) + y * math.sin(2 * math.pi * i / segs)) * r2 for i in range(segs)]
        for i in range(segs):
            j = (i + 1) % segs
            pts = [tuple(ra[i]), tuple(ra[j]), tuple(rb[j]), tuple(rb[i])]
            mid = (ra[i] + rb[j]) / 2
            axis_pt = a + d * (mid - a).dot(d)
            n = (Vector(pts[1]) - Vector(pts[0])).cross(Vector(pts[2]) - Vector(pts[0]))
            if n.dot(mid - axis_pt) < 0:
                pts.reverse()
            f = self.face(pts, colour, flat=False)
        if caps:
            for ring, c, out in ((ra, a, -d), (rb, b, d)):
                pts = [tuple(p) for p in ring]
                n = (Vector(pts[1]) - Vector(pts[0])).cross(Vector(pts[2]) - Vector(pts[0]))
                if n.dot(out) < 0:
                    pts.reverse()
                self.face(pts, cap_colour or colour)

    def strip(self, pts, width, y, colour, closed=False):
        """A flat band `width` wide along a polyline of (x, z) points at height y (painted lines, tape)."""
        n = len(pts)
        for i in range(n if closed else n - 1):
            p0, p1 = Vector((pts[i][0], 0, pts[i][1])), Vector((pts[(i + 1) % n][0], 0, pts[(i + 1) % n][1]))
            d = (p1 - p0).normalized()
            s = Vector((d.z, 0, -d.x)) * (width / 2)
            a, b, c, e = p0 - s, p1 - s, p1 + s, p0 + s
            self.face_out([(a.x, y, a.z), (e.x, y, e.z), (c.x, y, c.z), (b.x, y, b.z)], colour, (0, 1, 0))

    def arc(self, cx, cz, r, a0, a1, width, y, colour, segs=48):
        pts = [(cx + r * math.sin(math.radians(a0 + (a1 - a0) * i / segs)), cz + r * math.cos(math.radians(a0 + (a1 - a0) * i / segs)))
               for i in range(segs + 1)]
        self.strip(pts, width, y, colour)

    def disc(self, cx, cz, r, y, colour, segs=20):
        pts = [(cx + r * math.cos(2 * math.pi * i / segs), y, cz - r * math.sin(2 * math.pi * i / segs)) for i in range(segs)]
        self.face_out(pts, colour, (0, 1, 0))

    # ---- output

    def to_object(self, coll):
        me = bpy.data.meshes.new(self.name)
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=1e-5)
        self.bm.to_mesh(me)
        self.bm.free()
        for poly in me.polygons:
            poly.use_smooth = False
        obj = bpy.data.objects.new(self.name, me)
        coll.objects.link(obj)
        return obj


def export(path, objects):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE", use_mesh_modifiers=True,
        path_mode="STRIP", colors_type="LINEAR", bake_anim=False)
    return path


def preview(path, cam_unity, look_unity, lens=24, res=(1600, 900)):
    """A quick EEVEE look from a Unity camera position (vertex colours shown)."""
    scene = bpy.context.scene
    mat = bpy.data.materials.get("VenuePreview")
    if mat is None:
        mat = bpy.data.materials.new("VenuePreview")
        nt = mat.node_tree
        bsdf = nt.nodes.get("Principled BSDF")
        attr = nt.nodes.new("ShaderNodeAttribute")
        attr.attribute_name = "Col"
        nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = 0.8
    for o in scene.objects:
        if o.type == "MESH":
            o.data.materials.clear()
            o.data.materials.append(mat)
    cam = bpy.data.objects.get("VenueCam")
    if cam is None:
        cam = bpy.data.objects.new("VenueCam", bpy.data.cameras.new("VenueCam"))
        scene.collection.objects.link(cam)
    cam.data.lens = lens
    cam.data.clip_end = 600
    cam.location = U(*cam_unity)
    cam.rotation_euler = (U(*look_unity) - U(*cam_unity)).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    sun = bpy.data.objects.get("VenueSun")
    if sun is None:
        sun = bpy.data.objects.new("VenueSun", bpy.data.lights.new("VenueSun", "SUN"))
        scene.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(40), math.radians(10), math.radians(150))
    sun.data.energy = 3.0
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.45, 0.65, 0.95, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.8
    return ta.render_preview(path, res=res)
