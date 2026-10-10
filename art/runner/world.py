"""Vrapo!'s dinosaur world (run inside Blender 5.1): obstacles sized to the game's hitboxes, eggs, scenery.

    build_all()    every prop in the "World" collection, laid out in a row for previews
    export_all()   one FBX per prop into Assets/Games/Runner/Art (pivot at the ground, centred)

Hitboxes (RunnerGame.cs): lane 2.2 m (props 1.98 m wide), log = jump (0.7 m tall), bridge = duck (bar from 1.15 m),
boulders = dodge (2.8 m tall, 6 m long). Runner props face +Y here (toward the camera in Unity).
"""

import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "tools"))
import tundu_art as ta  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))


# ---------------------------------------------------------------------------------------------------- shapes

def rock(bm, center, radii, rng, subdiv=2, jag=0.16):
    """A chunky low-poly rock: a lumpy, flat-bottomed icosphere with a random twist."""
    # twist the unit sphere first, then stretch: a long rock stays long in the direction it was asked for
    twist = Matrix.Rotation(rng.uniform(0, math.tau), 4, "Z") @ Matrix.Rotation(rng.uniform(-0.25, 0.25), 4, "X")
    m = Matrix.Translation(center) @ Matrix.Diagonal((*radii, 1)) @ twist
    geom = bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0, matrix=Matrix())
    lumps = [(Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))).normalized(), rng.uniform(0.1, 0.25))
             for _ in range(4)]
    for v in geom["verts"]:
        d = v.co.normalized()
        k = 1 + rng.uniform(-jag, jag) * 0.5 + sum(h * max(0.0, d.dot(c)) ** 3 for c, h in lumps)
        v.co = d * k
        if v.co.z < -0.55:
            v.co.z = -0.55 + (v.co.z + 0.55) * 0.3   # flatter bottom: sits on the ground
        v.co = m @ v.co
    return geom


def rock_color(f):
    n = f.normal
    if n.z > 0.75:
        return "moss" if (hash(tuple(round(c, 2) for c in f.calc_center_median())) % 5 == 0) else "rock"
    return "rock" if n.z > 0.1 else "rock_dark"


def log_body(bm, x0, x1, center_yz, radius, rng, segments=10):
    """A bark log along X with wobbly sides."""
    length = x1 - x0
    m = (Matrix.Translation((x0 + length / 2, *center_yz)) @ Matrix.Rotation(math.radians(90), 4, "Y"))
    geom = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments, radius1=radius,
                                 radius2=radius, depth=length, matrix=m)
    for v in geom["verts"]:
        offset = Vector((0, v.co.y - center_yz[0], v.co.z - center_yz[1]))
        v.co += offset * rng.uniform(-0.07, 0.07)
    return geom


def log_color(f):
    if abs(f.normal.x) > 0.8:
        return "wood_end"
    return "wood_dark" if (hash(tuple(round(c, 2) for c in f.calc_center_median())) % 3 == 0) else "wood"


def frond(bm, base, angle_deg, length, height, width, droop=0.35, segments=6, tilt=0.0):
    """A curved leaf: rises then droops, with a centre ridge; both sides have faces (no back-face holes)."""
    a = math.radians(angle_deg)
    d = Vector((math.cos(a), math.sin(a), 0))
    side = Vector((-d.y, d.x, 0))
    pts = []
    for i in range(segments + 1):
        t = i / segments
        p = Vector(base) + d * length * t
        p.z += height * math.sin(math.pi * t * (1 - droop * 0.5)) - droop * height * t * t + tilt * t
        w = width * math.sin(math.pi * min(1.0, t * 1.1)) if i < segments else 0.0
        pts.append((p, w))
    faces = []
    for under in (False, True):          # top side, then the underside on its own vertices (bmesh needs that)
        drop = Vector((0, 0, -0.012)) if under else Vector()
        rows = []
        for p, w in pts:
            rows.append((bm.verts.new(p + side * w - Vector((0, 0, w * 0.25)) + drop),
                         bm.verts.new(p + Vector((0, 0, w * 0.15)) + drop),
                         bm.verts.new(p - side * w - Vector((0, 0, w * 0.25)) + drop)))
        for (l0, m0, r0), (l1, m1, r1) in zip(rows, rows[1:]):
            for quad in ((l0, l1, m1, m0), (m0, m1, r1, r0)):
                faces.append(bm.faces.new(tuple(reversed(quad)) if under else quad))
    return faces


def egg_shape(bm, center, r=0.24, h=0.31):
    geom = bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=12, radius=1.0, matrix=Matrix())
    for v in geom["verts"]:
        z = v.co.z
        v.co = Vector((v.co.x * r * (1 - 0.12 * max(0, z)), v.co.y * r * (1 - 0.12 * max(0, z)),
                       z * (h * 1.15 if z > 0 else h * 0.9))) + Vector(center)
    return geom


# ---------------------------------------------------------------------------------------------------- props

def build_log(coll, at=(0, 0, 0)):
    """LowBarrier: a fallen log across the lane, 0.7 m tall: jump over it."""
    rng = random.Random(11)
    pb = ta.PartBuilder()
    pb.add(log_color, lambda bm: log_body(bm, -0.99, 0.99, (0, 0.31), 0.31, rng, segments=12))
    for x, ang in ((-0.45, 35), (0.38, -28)):
        pb.add("wood", lambda bm, x=x, ang=ang: ta.cone(
            bm, (x, 0, 0.5), (x + 0.1 * math.copysign(1, ang), -0.05, 0.78), 0.07, segments=6))
    for x in (-0.7, 0.05, 0.6):
        pb.add("moss", lambda bm, x=x: ta.ellipsoid(bm, (x, 0.03, 0.6), (0.16, 0.13, 0.05), segments=8, rings=5))
    return place(pb.to_object("Log", coll), at)


def build_bridge(coll, at=(0, 0, 0)):
    """HighBarrier: a log resting on two rock pillars, vines hanging: duck under (clear below 1.15 m)."""
    rng = random.Random(5)
    pb = ta.PartBuilder()
    for x in (-0.9, 0.9):
        pb.add(rock_color, lambda bm, x=x: rock(bm, (x, 0, 0.55), (0.26, 0.26, 0.62), rng, jag=0.1))
        pb.add(rock_color, lambda bm, x=x: rock(bm, (x * 1.05, 0.05, 0.22), (0.34, 0.32, 0.24), rng))
    pb.add(log_color, lambda bm: log_body(bm, -1.12, 1.12, (0, 1.40), 0.24, rng, segments=10))
    for x in (-0.55, -0.2, 0.25, 0.6):
        top = 1.18
        for k in range(3):   # a short vine of leaves, stays above ~1.0 m
            z = top - k * 0.07
            pb.add("fern" if k % 2 else "fern_light", lambda bm, x=x, z=z, k=k: ta.ellipsoid(
                bm, (x + 0.03 * (k % 2), 0.12, z), (0.05, 0.02, 0.06), segments=6, rings=4))
    return place(fit(pb.to_object("VineBridge", coll), half_width=1.1), at)   # no wider than its lane


def build_boulders(coll, at=(0, 0, 0)):
    """Block: a big mossy boulder pile filling the lane (2.8 m tall, 6 m long): move aside."""
    rng = random.Random(3)
    pb = ta.PartBuilder()
    pb.add(rock_color, lambda bm: rock(bm, (0, 0, 1.35), (0.98, 2.85, 1.42), rng, subdiv=2, jag=0.08))
    for y, r in ((-2.2, 0.6), (1.9, 0.7), (0.4, 0.55)):
        pb.add(rock_color, lambda bm, y=y, r=r: rock(bm, (rng.uniform(-0.4, 0.4), y, 2.3), (r, r, r * 0.8), rng))
    for y in (-2.9, 2.85):
        pb.add(rock_color, lambda bm, y=y: rock(bm, (0.3, y, 0.35), (0.45, 0.4, 0.35), rng))
    for k, y in enumerate((-1.1, 1.2)):
        for a in range(0, 360, 60):
            pb.add("fern" if a % 120 else "fern_light", lambda bm, y=y, a=a, k=k: frond(
                bm, (0.1, y, 2.6), a + 15 * k, 0.55, 0.3, 0.09))
    # Stay inside the lane (2.2 m): at most 1.8 m wide so the free lanes beside it stay visible, and about 2.4 m
    # tall (it can't be jumped anyway; lower hides less of the road behind it).
    return place(fit(pb.to_object("Boulders", coll), half_width=0.9, height=2.4), at)


def fit(obj, half_width=None, height=None):
    """Squeezes a prop (pivot on the ground, centred) into a lane-sized box."""
    xs = [abs(v.co.x) for v in obj.data.vertices]
    zs = [v.co.z for v in obj.data.vertices]
    sx = min(1.0, half_width / max(xs)) if half_width else 1.0
    sz = min(1.0, height / max(zs)) if height else 1.0
    for v in obj.data.vertices:
        v.co.x *= sx
        v.co.z *= sz
    obj.data.update()
    return obj


def build_egg(coll, at=(0, 0, 0)):
    """Coin: a dino egg (pivot at its centre, the game floats it 1 m up and spins it)."""
    rng = random.Random(8)
    pb = ta.PartBuilder()
    pb.add("egg", lambda bm: egg_shape(bm, (0, 0, 0)))
    for i in range(7):
        a = rng.uniform(0, math.tau)
        z = rng.uniform(-0.17, 0.24)
        rr = 0.24 * (1 - 0.12 * max(0, z / 0.35)) * math.sqrt(max(0.05, 1 - (z / 0.34) ** 2))
        c = Vector((math.cos(a) * rr, math.sin(a) * rr, z))
        n = Vector((math.cos(a), math.sin(a), 0.3 * z / 0.2)).normalized()
        size = rng.uniform(0.035, 0.055)
        color = "sky" if i % 2 else "coral"
        pb.add(color, lambda bm, c=c, s=size: ta.ellipsoid(bm, c, (s, s, s * 0.9), segments=8, rings=5))
    return place(pb.to_object("Egg", coll, smooth_colors=("egg", "sky", "coral")), at)


def build_fern(coll, at=(0, 0, 0), seed=1, scale=1.0):
    rng = random.Random(seed)
    pb = ta.PartBuilder()
    n = rng.randint(7, 9)
    for i in range(n):
        a = i * 360 / n + rng.uniform(-12, 12)
        pb.add("fern" if i % 2 else "fern_light", lambda bm, a=a: frond(
            bm, (0, 0, 0.02), a, rng.uniform(0.7, 0.95) * scale, rng.uniform(0.45, 0.6) * scale, 0.12 * scale))
    return place(pb.to_object(f"Fern{seed}", coll), at)


def build_cycad(coll, at=(0, 0, 0), seed=1, height=4.2):
    """A palm-like cycad: ringed trunk, a crown of big drooping fronds."""
    rng = random.Random(seed)
    pb = ta.PartBuilder()
    rings = 8
    lean = Vector((rng.uniform(-0.25, 0.25), rng.uniform(-0.25, 0.25), 0))
    for i in range(rings):
        t0, t1 = i / rings, (i + 1) / rings
        r0, r1 = 0.28 - 0.1 * t0, 0.28 - 0.1 * t1
        b = Vector((0, 0, height * t0)) + lean * t0 * t0
        top = Vector((0, 0, height * t1)) + lean * t1 * t1
        mid = (b + top) / 2
        m = Matrix.Translation(mid) @ (top - b).to_track_quat("Z", "Y").to_matrix().to_4x4()
        pb.add("wood" if i % 2 else "wood_dark", lambda bm, m=m, r0=r0, r1=r1, l=(top - b).length: bmesh.ops.create_cone(
            bm, cap_ends=True, cap_tris=False, segments=8, radius1=r0 * 1.1, radius2=r1, depth=l, matrix=m))
    crown = Vector((0, 0, height)) + lean
    for i in range(9):
        a = i * 40 + rng.uniform(-8, 8)
        pb.add("fern" if i % 2 else "leaf", lambda bm, a=a: frond(
            bm, crown, a, rng.uniform(1.8, 2.3), 0.6, 0.34, droop=0.9, segments=7))
    return place(pb.to_object(f"Cycad{seed}", coll), at)


def build_rock(coll, at=(0, 0, 0), seed=1, size=0.6):
    rng = random.Random(seed)
    pb = ta.PartBuilder()
    pb.add(rock_color, lambda bm: rock(bm, (0, 0, size * 0.55), (size, size * 0.85, size * 0.7), rng))
    if seed % 2:
        pb.add(rock_color, lambda bm: rock(bm, (size * 0.8, 0.2, size * 0.3), (size * 0.5,) * 3, rng))
    return place(pb.to_object(f"Rock{seed}", coll), at)


def build_volcano(coll, at=(0, 0, 0)):
    """Backdrop: a big smoking volcano with lava streaks (about 34 m tall)."""
    rng = random.Random(4)
    pb = ta.PartBuilder()
    height, r_base, r_top = 34.0, 36.0, 7.0

    def body(bm):
        """Rings of vertices with a concave profile (steeper near the top), quads between them."""
        segs, rings = 18, 6
        grid = []
        for j in range(rings + 1):
            t = j / rings
            z = height * t
            r = r_top + (r_base - r_top) * (1 - t) ** 1.7
            row = []
            for i in range(segs):
                a = math.tau * i / segs + (0.08 * j)
                k = 1 + (rng.uniform(-0.07, 0.07) if 0 < j < rings else 0)
                row.append(bm.verts.new((math.cos(a) * r * k, math.sin(a) * r * k, z)))
            grid.append(row)
        for j in range(rings):
            for i in range(segs):
                a, b = grid[j][i], grid[j][(i + 1) % segs]
                c, d = grid[j + 1][(i + 1) % segs], grid[j + 1][i]
                bm.faces.new((a, b, c, d))

    def volcano_color(f):
        c = f.calc_center_median()
        ang = math.degrees(math.atan2(c.y, c.x)) % 360
        streak = any(abs((ang - s + 180) % 360 - 180) < 11 - 6 * (1 - c.z / height) for s in (35, 150, 265))
        if streak and c.z > height * 0.3:
            return "lava"
        return "volcano" if (int(ang / 20) + int(c.z / 6)) % 2 else "volcano_dark"

    pb.add(volcano_color, body)
    pb.add("lava", lambda bm: bmesh.ops.create_circle(bm, cap_ends=True, segments=18, radius=r_top * 0.98,
                                                      matrix=Matrix.Translation((0, 0, height - 0.8))))
    for k, (dx, dz, r) in enumerate(((0, 6, 5.5), (3, 12, 6.5), (-2, 19, 7.5), (5, 26, 8))):
        pb.add("smoke", lambda bm, dx=dx, dz=dz, r=r: ta.ellipsoid(
            bm, (dx, 0, height + dz), (r, r * 0.8, r * 0.7), segments=10, rings=7))
    return place(pb.to_object("Volcano", coll, smooth_colors=("smoke",)), at)


def place(obj, at):
    obj.location = at
    return obj


# ---------------------------------------------------------------------------------------------------- all

PROPS = [  # (builder, name, preview position, kwargs)
    (build_log, "Log", (-6.5, 0, 0), {}),
    (build_bridge, "VineBridge", (-3.6, 0, 0), {}),
    (build_boulders, "Boulders", (0.2, 3.0, 0), {}),
    (build_egg, "Egg", (2.6, -1.0, 1.0), {}),
    (build_fern, "Fern1", (4.0, -0.5, 0), {"seed": 1}),
    (build_fern, "Fern2", (5.4, 0.5, 0), {"seed": 2, "scale": 1.3}),
    (build_cycad, "Cycad1", (7.4, 1.5, 0), {"seed": 1}),
    (build_cycad, "Cycad2", (10.4, 3.0, 0), {"seed": 2, "height": 5.2}),
    (build_rock, "Rock1", (6.2, -1.2, 0), {"seed": 1}),
    (build_rock, "Rock2", (8.8, -1.5, 0), {"seed": 2, "size": 0.9}),
    (build_volcano, "Volcano", (-30, 140, 0), {}),
]


def build_all():
    for obj in list(bpy.data.objects):
        if obj.users_collection and obj.users_collection[0].name == "World":
            bpy.data.objects.remove(obj, do_unlink=True)
    coll = ta.collection("World")
    made = {}
    for fn, name, pos, kw in PROPS:
        obj = fn(coll, at=pos, **kw)
        made[obj.name] = ta.tri_count(obj)
    return made


def preview(name="world"):
    ground = bpy.data.objects.get("PreviewGround")
    if ground is None:
        bm = bmesh.new()
        bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=200)
        ground = ta.mesh_object("PreviewGround", bm, ta.collection("Preview"))
        ta.paint_all(ground, "grass")
        ta.finish(ground, smooth=False)
    ta.preview_rig(target=(1.8, 0.5, 1.0), distance=19, yaw_deg=-18, pitch_deg=16, lens=40)
    bpy.context.scene.camera.data.clip_end = 300
    out = os.path.join(HERE, "previews", f"{name}.png")
    return ta.render_preview(out, res=(1400, 700))


def game_view(name="game_view", layout=(("Boulders", 0, 20), ("Log", -1, 34), ("Egg", 1, 26)), lane=2.2):
    """Renders what the player sees in Vrapo!: the game camera (3.4 m up, 6.5 m behind, 14° down, 60° vertical FOV),
    a 3-lane dirt road with grass beside it, the dino in the middle lane and props at (name, lane -1..1, metres ahead).
    Copies of the props are temporary; the originals stay where they are."""
    coll = ta.collection("GameView")
    for o in list(coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for nm, color, width, z in (("Road", "dirt", lane * 3 + 1, 0.001), ("GrassL", "grass", 60, -0.01)):
        bm = bmesh.new()
        bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=0.5,
                              matrix=Matrix.Translation((0, -60, z)) @ Matrix.Diagonal((width, 160, 1, 1)))
        obj = ta.mesh_object(f"GV_{nm}", bm, coll)
        ta.paint_all(obj, color)
        ta.finish(obj, smooth=False)
    for prop, ln, ahead in layout:
        src = bpy.data.objects[prop]
        dup = src.copy()
        coll.objects.link(dup)
        dup.location = (ln * lane, -ahead, 1.0 if prop == "Egg" else 0.0)
    hidden = []
    for o in bpy.data.objects:
        if o.users_collection and o.users_collection[0].name in ("World", "Preview") and not o.hide_render:
            o.hide_render = True
            hidden.append(o)
    cam = bpy.data.objects.get("GameCam") or bpy.data.objects.new("GameCam", bpy.data.cameras.new("GameCam"))
    if cam.name not in coll.objects:
        coll.objects.link(cam)
    cam.data.sensor_fit = "VERTICAL"
    cam.data.angle_y = math.radians(60)
    cam.data.clip_end = 200
    cam.location = (0, 6.5, 3.4)
    cam.rotation_euler = (math.radians(90 - 14), 0, math.radians(180))
    scene = bpy.context.scene
    old_cam = scene.camera
    scene.camera = cam
    out = os.path.join(HERE, "previews", f"{name}.png")
    ta.render_preview(out, res=(1280, 720))
    scene.camera = old_cam
    for o in hidden:
        o.hide_render = False
    for o in list(coll.objects):
        if o.name != "GameCam":
            bpy.data.objects.remove(o, do_unlink=True)
    return out


def export_all():
    paths = []
    for obj in [o for o in bpy.data.objects if o.users_collection and o.users_collection[0].name == "World"]:
        keep = obj.location.copy()
        obj.location = (0, 0, 0)
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        path = os.path.join(ta.UNITY_ART, f"{obj.name}.fbx")
        bpy.ops.export_scene.fbx(
            filepath=path, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
            axis_forward="-Z", axis_up="Y", bake_space_transform=True, bake_anim=False, mesh_smooth_type="FACE",
            use_mesh_modifiers=True, path_mode="STRIP")
        obj.location = keep
        paths.append(os.path.basename(path))
    return paths
