"""Gara!'s kart (run inside Blender 5.1; `build.py` runs it headless): a chunky toy kart for the chibis, as code.

Three static meshes, exported together into Assets/Games/Kart/Art/Kart.fbx:

    KartBody      the soap-bar body with a round nose, side pods, the seat, the engine with two exhausts, a rear wing,
                  bumpers, headlights, a number plate, the steering column
    KartWheel     one wheel (a fat rounded tyre, a rim with a hub cap), axle along X; the game places four (the front pair
                  turned with the wheel, scaled a little smaller)
    KartSteering  the steering wheel (a ring, three spokes, a hub), in the plane facing the driver; the game turns it with
                  the player's own invisible wheel

Colours are not in the model: like the chibis, every face says which REGION it is in the second UV map (u = region,
v = baked ambient occlusion) and the game colours the regions per racer (Tundu/Athlete's table), so one kart model is
every racer's kart. Facing -Y (Unity +Z), wheels on Z = 0, the kart's left = +X, metres; about 2.2 m long, 1.5 m wide.
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "tools"))
sys.path.insert(0, os.path.join(HERE, "..", "chibi"))
import kit  # noqa: E402   (the chibis' mesh helpers: tubes, rings, superellipses)
import tundu_art as ta  # noqa: E402

UNITY_ART = os.path.join(ta.REPO, "unity", "MoveItGames", "Assets", "Games", "Kart", "Art")

# Region numbers: the colour table (Tundu/Athlete _Regions[]); keep in step with KartLooks.cs.
REGIONS = ["body", "accent", "trim", "seat", "dark", "metal", "tyre", "rim", "lights", "plate", "hub", "glow"]
R = {name: i for i, name in enumerate(REGIONS)}

PREVIEW = {"body": "#E8403A", "accent": "#FFC93C", "trim": "#FFFFFF", "seat": "#24305E", "dark": "#2A2D3E",
           "metal": "#B9BDCC", "tyre": "#262833", "rim": "#F4F4F8", "lights": "#FFF6C8", "plate": "#FFFFFF",
           "hub": "#FFC93C", "glow": "#FF8A3D"}

# ------------------------------------------------------------------------------------------------- proportions

FRONT_AXLE, REAR_AXLE = -0.70, 0.62      # y (front is -Y)
FRONT_R, REAR_R = 0.25, 0.29             # wheel radii (the front wheel is the wheel mesh scaled 0.86)
WHEEL_X = 0.63                           # wheel centres across
WHEEL_W = 0.26                           # the wheel mesh's width (rear)


class Shape(kit.Part):
    """kit.Part without bones: faces carry Gara!'s regions."""

    def __init__(self, name):
        super().__init__(name, weights={})

    def face(self, idx, region):
        self.faces.append(tuple(idx))
        self.freg.append(R[region] if isinstance(region, str) else region)

    def lathe(self, profile, axis_origin, axis, region, segs=24, caps=(False, False)):
        """A surface of revolution: profile = [(radius, along)] round axis (unit) through axis_origin."""
        axis = Vector(axis).normalized()
        o = Vector(axis_origin)
        side = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((1, 0, 0))
        u = axis.cross(side).normalized()
        w = axis.cross(u).normalized()
        rows = []
        for r, a in profile:
            rows.append([o + axis * a + (u * math.cos(2 * math.pi * k / segs) + w * math.sin(2 * math.pi * k / segs)) * r
                         for k in range(segs)])
        reg = region if callable(region) else (lambda i, j, p: region)
        self.grid(rows, reg, caps=caps)
        return self

    def rbox(self, centre, half, region, round_=0.35, segs=16, rings=10, power=3.0):
        """A rounded box: a superellipsoid (power > 2 = boxier)."""
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
                x = kit.spow(ca, e) * kit.spow(ce, e) * half[0]
                y = kit.spow(sa, e) * kit.spow(ce, e) * half[1]
                z = kit.spow(se, e) * half[2]
                row.append(c + Vector((x, y, z)))
            rows.append(row)
        self.grid(rows, region, caps=(c - Vector((0, 0, half[2])), c + Vector((0, 0, half[2]))))
        return self


def loft(shape, sections, region, segs=28):
    """A closed body lofted through sections (y, centre z, half width, half height, power): a soap bar."""
    rows = []
    for y, cz, hw, hh, pw in sections:
        rows.append(kit.superellipse(Vector((0, y, cz)), Vector((1, 0, 0)), Vector((0, 0, 1)), hw, hh, pw, segs, keep=True))
    y0, y1 = sections[0][0], sections[-1][0]
    shape.grid(rows, region, caps=(Vector((0, y0 - 0.02, sections[0][1])), Vector((0, y1 + 0.02, sections[-1][1]))))


# ------------------------------------------------------------------------------------------------- the parts

def body():
    s = Shape("KartBody")
    # the soap bar: low, round-nosed, widest at the seat, a little higher at the back
    def region(i, j, p):
        # a stripe of the accent colour along each side, the belly dark
        if p.z < 0.17:
            return "dark"
        if abs(p.x) > 0.3 and 0.27 < p.z < 0.33:
            return "accent"
        return "body"
    loft(s, [
        (-1.12, 0.26, 0.10, 0.08, 2.2),
        (-1.08, 0.27, 0.22, 0.12, 2.4),
        (-0.98, 0.28, 0.32, 0.15, 2.6),
        (-0.80, 0.29, 0.38, 0.17, 2.8),
        (-0.50, 0.30, 0.42, 0.18, 3.0),
        (-0.15, 0.30, 0.46, 0.19, 3.2),
        (0.20, 0.31, 0.47, 0.20, 3.2),
        (0.50, 0.33, 0.44, 0.21, 3.0),
        (0.72, 0.34, 0.38, 0.19, 2.8),
        (0.84, 0.34, 0.28, 0.15, 2.4),
        (0.88, 0.34, 0.14, 0.09, 2.2),
    ], region)
    # side pods between the wheels
    for side in (1, -1):
        s.tube([(side * 0.50, -0.42, 0.24), (side * 0.53, -0.1, 0.25), (side * 0.53, 0.2, 0.26), (side * 0.50, 0.38, 0.27)],
               [(0.11, 0.10), (0.13, 0.11), (0.13, 0.11), (0.11, 0.10)], "body", segs=16, front=Vector((0, 0, 1)), power=2.6)
        # a white trim line along the pod's top
        s.tube([(side * 0.51, -0.36, 0.345), (side * 0.535, -0.1, 0.355), (side * 0.535, 0.2, 0.365), (side * 0.51, 0.34, 0.365)],
               0.018, "trim", segs=8)
    # the nose: a round bumper ball and a bumper bar
    s.ellipsoid((0, -1.10, 0.25), (0.20, 0.12, 0.13), region="accent", segs=20, rings=12)
    s.tube([(-0.34, -1.0, 0.18), (-0.2, -1.09, 0.17), (0.2, -1.09, 0.17), (0.34, -1.0, 0.18)], 0.035, "dark", segs=10)
    # headlights and the number plate on the nose's top
    for side in (1, -1):
        s.ellipsoid((side * 0.2, -1.03, 0.33), (0.06, 0.035, 0.05), region="lights", segs=12, rings=8)
    s.rbox((0, -0.92, 0.445), (0.13, 0.075, 0.012), "plate", power=4.0)
    # the floor pan under it all, and the rear bumper
    s.rbox((0, -0.05, 0.13), (0.36, 0.86, 0.05), "dark", power=4.0)
    s.tube([(-0.40, 0.88, 0.2), (-0.2, 0.95, 0.19), (0.2, 0.95, 0.19), (0.40, 0.88, 0.2)], 0.04, "dark", segs=10)
    # the seat: a padded bucket on top of the body
    s.rbox((0, 0.18, 0.53), (0.23, 0.2, 0.05), "seat", power=3.0)                 # the cushion
    s.rbox((0, 0.34, 0.76), (0.24, 0.07, 0.26), "seat", power=3.2)                # the back
    for side in (1, -1):
        s.rbox((side * 0.24, 0.24, 0.62), (0.045, 0.16, 0.11), "seat", power=3.0)  # the sides
    s.rbox((0, 0.30, 0.93), (0.18, 0.08, 0.04), "accent", power=3.0)              # a stripe on the headrest
    # the engine behind the seat, its two exhausts, a glowing grille
    s.rbox((0, 0.62, 0.50), (0.26, 0.17, 0.13), "dark", power=3.5)
    s.rbox((0, 0.62, 0.64), (0.2, 0.12, 0.03), "metal", power=4.0)
    for side in (1, -1):
        path = [(side * 0.15, 0.62, 0.52), (side * 0.17, 0.80, 0.56), (side * 0.19, 0.93, 0.66)]
        s.tube(path, 0.055, "metal", segs=14, caps=(True, False))
        s.ellipsoid((side * 0.192, 0.935, 0.665), (0.04, 0.015, 0.04), region="glow", segs=10, rings=6)
    # the rear wing on two struts
    for side in (1, -1):
        s.tube([(side * 0.32, 0.70, 0.55), (side * 0.32, 0.80, 0.86)], 0.022, "metal", segs=8)
    s.rbox((0, 0.80, 0.92), (0.62, 0.11, 0.028), "accent", power=4.0)
    for side in (1, -1):
        s.rbox((side * 0.63, 0.80, 0.94), (0.022, 0.14, 0.07), "body", power=3.0)
    # the steering column (the wheel itself is KartSteering)
    s.tube([(0, -0.52, 0.40), (0, -0.30, 0.56), (0, -0.10, 0.70)], 0.03, "dark", segs=10)
    # axles and their housings (the wheels are KartWheel)
    for y in (FRONT_AXLE, REAR_AXLE):
        s.tube([(-WHEEL_X + 0.1, y, 0.24 if y < 0 else 0.28), (WHEEL_X - 0.1, y, 0.24 if y < 0 else 0.28)], 0.03, "metal", segs=8)
    return s


def wheel():
    """A fat, rounded tyre with a bright rim and a hub cap; axle along X, centred on the origin, radius REAR_R."""
    s = Shape("KartWheel")
    r, w = REAR_R, WHEEL_W * 0.5
    # the tyre: a rounded rectangle swept round the axle
    prof = []
    steps = 10
    for k in range(steps + 1):
        a = math.pi * k / steps - math.pi / 2       # -90 .. 90 degrees round the tyre's section
        x = w * math.sin(a) * 1.0
        rr = r - 0.07 + 0.07 * math.cos(a) ** 0.6
        prof.append((rr, x))
    s.lathe(prof, (0, 0, 0), (1, 0, 0), "tyre", segs=28)
    # the tread: three soft ribs round the tyre (the baked shade between them reads as grooves)
    for x in (-w * 0.5, 0.0, w * 0.5):
        pts = [(x, math.cos(2 * math.pi * k / 40) * (r - 0.004), math.sin(2 * math.pi * k / 40) * (r - 0.004)) for k in range(40)]
        s.ring(pts, (0.012, 0.022), "tyre", segs=8, front=Vector((1, 0, 0)))
    # the rim: a dish on each side, the hub cap on the outside (+X)
    for side in (1, -1):
        s.lathe([(r - 0.075, side * (w - 0.01)), (r - 0.12, side * (w + 0.008)), (0.06, side * (w + 0.02)), (0.0, side * (w + 0.022))],
                (0, 0, 0), (1, 0, 0), "rim", segs=24)
    s.ellipsoid((w + 0.03, 0, 0), (0.025, 0.055, 0.055), region="hub", segs=12, rings=6)
    return s


def steering():
    """The steering wheel: a ring with three spokes and a hub, facing the driver (its axis along +Z tilted back)."""
    s = Shape("KartSteering")
    ring_r, tube_r = 0.15, 0.024
    pts = [(math.cos(2 * math.pi * k / 24) * ring_r, 0.0, math.sin(2 * math.pi * k / 24) * ring_r) for k in range(24)]
    s.ring(pts, tube_r, "dark", segs=8, front=Vector((0, 1, 0)))
    for k in range(3):
        a = math.radians(-90 + 120 * k)
        s.tube([(0, 0, 0), (math.cos(a) * ring_r * 0.92, 0, math.sin(a) * ring_r * 0.92)], 0.016, "dark", segs=6, caps=(False, True))
    s.ellipsoid((0, -0.005, 0), (0.05, 0.03, 0.05), region="accent", segs=12, rings=6)
    # two grips where the mittens hold it (the accent colour)
    for side in (1, -1):
        a = math.radians(90 + side * 70)
        c = Vector((math.cos(a) * ring_r, 0, math.sin(a) * ring_r))
        tangent = Vector((-math.sin(a), 0, math.cos(a)))
        s.tube([c - tangent * 0.04, c + tangent * 0.04], 0.03, "accent", segs=8)
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


def build():
    ta.clear_scene()
    coll = ta.collection("Kart")
    for o in list(coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    made = {}
    for fn in (body, wheel, steering):
        shape = fn()
        made[shape.name] = to_object(shape, coll)
    return {n: sum(len(p.vertices) - 2 for p in o.data.polygons) for n, o in made.items()}


def objects():
    return [o for o in bpy.data.collections["Kart"].objects if o.type == "MESH"]


# ------------------------------------------------------------------------------------------------- assembled (previews, AO)

WHEELS = [(WHEEL_X, FRONT_AXLE, FRONT_R, 0.86), (-WHEEL_X, FRONT_AXLE, FRONT_R, 0.86),
          (WHEEL_X, REAR_AXLE, REAR_R, 1.0), (-WHEEL_X, REAR_AXLE, REAR_R, 1.0)]
STEERING_AT = Vector((0, -0.06, 0.73))   # close to the driver: the chibis' arms are short
STEERING_TILT = 35.0   # degrees back from upright


def assemble(coll, offset=Vector(), name="pv", colours=None):
    """Copies of the parts put together as the game does (for previews and the ambient-occlusion bake)."""
    from mathutils import Euler, Matrix
    made = []
    body_o = bpy.data.objects["KartBody"]
    wheel_o = bpy.data.objects["KartWheel"]
    steer_o = bpy.data.objects["KartSteering"]

    def copy(src, mw, n):
        o = bpy.data.objects.new(n, src.data.copy())
        o.matrix_world = Matrix.Translation(offset) @ mw
        coll.objects.link(o)
        made.append(o)
        if colours:
            recolour(o, colours)
        return o
    copy(body_o, Matrix(), f"{name}_body")
    for i, (x, y, r, sc) in enumerate(WHEELS):
        mw = Matrix.Translation((x, y, r)) @ Matrix.Diagonal((sc, sc, sc, 1))
        if x < 0:
            mw = mw @ Matrix.Rotation(math.pi, 4, "Z")   # hub caps outward
        copy(wheel_o, mw, f"{name}_wheel{i}")
    copy(steer_o, Matrix.Translation(STEERING_AT) @ Euler((math.radians(-STEERING_TILT), 0, 0)).to_matrix().to_4x4(), f"{name}_steer")
    return made


def recolour(obj, overrides):
    me = obj.data
    col = me.color_attributes.get("Col")
    uv1 = me.uv_layers.get("Region")
    if col is None or uv1 is None:
        return
    for poly in me.polygons:
        reg = int(round(uv1.data[poly.loop_indices[0]].uv[0]))
        name = REGIONS[reg]
        hexc = overrides.get(name, PREVIEW[name])
        c = [ta.srgb_to_linear(x) for x in ta.hex_rgb(hexc)]
        for li in poly.loop_indices:
            col.data[li].color = (*c, 1.0)


def driver_stand_in(coll, offset=Vector(), scale=0.8):
    """A chibi-sized stand-in (head and body) sitting in the seat: the proportions in the previews."""
    bm = bmesh.new()
    s = scale
    ta.ellipsoid(bm, Vector((0, 0.08, 0.53 + 0.62 * s)) + offset, (0.37 * s, 0.34 * s, 0.35 * s))
    ta.ellipsoid(bm, Vector((0, 0.12, 0.53 + 0.22 * s)) + offset, (0.19 * s, 0.14 * s, 0.2 * s))
    o = ta.mesh_object("pv_driver", bm, coll)
    me = o.data
    col = me.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
    c = [ta.srgb_to_linear(x) for x in ta.hex_rgb("#FFD9C2")]
    for li in range(len(me.loops)):
        col.data[li].color = (*c, 1.0)
    me.shade_smooth()
    o.data.materials.append(kit.preview_material())
    return o


LOOKS = [
    {},
    {"body": "#31C6F4", "accent": "#FFFFFF", "seat": "#24305E", "hub": "#31C6F4"},
    {"body": "#7B61FF", "accent": "#FF5AA8", "seat": "#1E1A2C", "hub": "#FF5AA8"},
    {"body": "#5DB04E", "accent": "#FFC93C", "seat": "#24305E", "hub": "#FFC93C"},
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
    for i, look in enumerate(LOOKS):
        off = Vector(((i - 1.5) * 2.0, 0, 0))
        assemble(coll, off, f"pv{i}", look)
        if i == 0:
            driver_stand_in(coll, off)
    paths = []
    for view, yaw, pitch, dist in (("front", -30, 18, 8.5), ("back", 160, 22, 8.5), ("side", -90, 8, 8.5)):
        ta.preview_rig(target=(0, 0, 0.45), distance=dist, yaw_deg=yaw, pitch_deg=pitch, lens=50)
        kit_lights()
        paths.append(ta.render_preview(os.path.join(out_dir, f"kart_{view}.png"), res=(1800, 900)))
    # the first kart close, from the game's chase camera
    ta.preview_rig(target=(-3.0, -0.3, 0.5), distance=4.2, yaw_deg=180, pitch_deg=20, lens=40)
    kit_lights()
    paths.append(ta.render_preview(os.path.join(out_dir, "kart_chase.png"), res=(1200, 800)))
    for o in objects():
        o.hide_render = False
    return paths


def kit_lights():
    import chibi
    chibi.lights()


# ------------------------------------------------------------------------------------------------- ambient occlusion

def bake_ao(samples=48):
    """Ambient occlusion into the second UV map's v (0.35 + 0.65 * occlusion), each part baked in the assembled kart."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.render.bake.target = "VERTEX_COLORS"
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.light_settings.distance = 0.35
    coll = ta.collection("Bake")
    for o in list(coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    parts = assemble(coll, name="bk")
    # the ground under it (the wheels and the belly darker)
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=3.0)
    ground = ta.mesh_object("bk_ground", bm, coll)
    for o in objects():
        o.hide_render = True
        o.hide_set(True)
    done = 0
    for src_name, copy_name in (("KartBody", "bk_body"), ("KartWheel", "bk_wheel2"), ("KartSteering", "bk_steer")):
        target = bpy.data.objects[copy_name]
        me = target.data
        attr = me.color_attributes.get("AO") or me.color_attributes.new("AO", "FLOAT_COLOR", "POINT")
        me.color_attributes.active_color = attr
        for o in bpy.context.view_layer.objects:
            o.select_set(False)
        target.select_set(True)
        bpy.context.view_layer.objects.active = target
        bpy.ops.object.bake(type="AO")
        # back onto the part itself (same topology)
        src = bpy.data.objects[src_name].data
        uv = src.uv_layers["Region"]
        for poly in src.polygons:
            for li in poly.loop_indices:
                c = attr.data[src.loops[li].vertex_index].color
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
    path = os.path.join(UNITY_ART, "Kart.fbx")
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
