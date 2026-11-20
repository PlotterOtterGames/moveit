"""What Vrapo!'s camera sees, rendered in Blender for look development (run by build.py --game after --valley [--dino]).

A stand-in for the game's world: the path (three trails worn into the earth, grass ridges between them), the grassy
banks rising to the valley's walls, the kit's pieces scattered like RunnerWorld does, obstacles and eggs in the lanes,
the curved world (the material bends everything down with distance, like Tundu/Valley), a gradient sky. The game camera:
3.4 m up, 6.5 m behind the runner, 14 degrees down, 60 degrees of vertical view. Renders into art/runner/previews/.
"""

import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "tools"))
import tundu_art as ta  # noqa: E402
import kit  # noqa: E402
from kit import U  # noqa: E402

CURVE = 0.0009          # RunnerGame's worldCurve: metres of drop per metre squared ahead of the camera
CAM = (0.0, 3.4, -6.5)  # Unity
LANE = 2.2


def ground_height(x):
    """The valley's floor (Unity x): the path and its verges flat, gentle banks beyond 9 m, steeper toward the walls."""
    ax = abs(x)
    if ax < 9.0:
        return 0.0
    t = ax - 9.0
    return 0.022 * t * t + 0.03 * t


def path_amount(x):
    """1 on the path, 0 on the grass, a soft edge (the game's pixel makes it ragged)."""
    ax = abs(x)
    return max(0.0, min(1.0, (3.95 - ax) / 0.4))


def trail_amount(x):
    """The three worn trails (packed earth along each lane's middle), 1 in a trail's middle."""
    return max(0.0, 1.0 - min(abs(x - c) for c in (-LANE, 0.0, LANE)) / 0.75)


def ground(coll, z0=-70.0, z1=330.0):
    """The preview's ground: vertex columns dense near the path, rows every 2 m (the curve bends it smoothly)."""
    xs = sorted({*[s * v for s in (-1, 1) for v in (0.0, 0.4, 0.75, 1.1, 1.45, 1.8, 2.2, 2.6, 2.95, 3.3, 3.6, 3.95, 4.4,
                                                         5.2, 6.5, 8.0, 10.0, 13.0, 17.0, 22.0, 28.0, 36.0, 48.0, 70.0)]})
    zs = [z0 + i * 2.0 for i in range(int((z1 - z0) / 2.0) + 1)]
    B = kit.Build("PreviewGround")

    def make(bm):
        grid = []
        for z in zs:
            row = []
            for x in xs:
                row.append(bm.verts.new(U(x, ground_height(x), z)))
            grid.append(row)
        for j in range(len(zs) - 1):
            for i in range(len(xs) - 1):
                bm.faces.new((grid[j][i], grid[j + 1][i], grid[j + 1][i + 1], grid[j][i + 1]))

    def colour(p, n):
        x, z = -p.x, -p.y
        k = path_amount(x)
        grass = kit.mix("#4E9C45", "#7DBA4E", 0.5 + 0.5 * math.sin(x * 0.35 + z * 0.13) * math.cos(z * 0.07))
        if abs(x) > 9.0:
            grass = kit.mix(grass, "#3E8445", min(1.0, (abs(x) - 9.0) / 20.0))
        path = kit.mix("#E5B47C", "#C98E5C", 0.55 * trail_amount(x))
        return kit.mix(grass, path, k)
    B.add(make, colour)
    obj = B.to_object(coll)
    return obj


def bend_material(mat):
    """The curved world in the preview: every vertex dropped by CURVE * (distance ahead of the camera)^2."""
    if mat.get("bent"):
        return
    mat["bent"] = 1
    nt = mat.node_tree
    out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(geo.outputs["Position"], sep.inputs[0])
    cam_y = -CAM[2]                 # Blender y of the camera
    ahead = nt.nodes.new("ShaderNodeMath")
    ahead.operation = "SUBTRACT"
    ahead.inputs[0].default_value = cam_y
    nt.links.new(sep.outputs["Y"], ahead.inputs[1])
    pos = nt.nodes.new("ShaderNodeMath")
    pos.operation = "MAXIMUM"
    pos.inputs[1].default_value = 0.0
    nt.links.new(ahead.outputs[0], pos.inputs[0])
    sq = nt.nodes.new("ShaderNodeMath")
    sq.operation = "MULTIPLY"
    nt.links.new(pos.outputs[0], sq.inputs[0])
    nt.links.new(pos.outputs[0], sq.inputs[1])
    k = nt.nodes.new("ShaderNodeMath")
    k.operation = "MULTIPLY"
    k.inputs[1].default_value = -CURVE
    nt.links.new(sq.outputs[0], k.inputs[0])
    comb = nt.nodes.new("ShaderNodeCombineXYZ")
    nt.links.new(k.outputs[0], comb.inputs["Z"])
    disp = nt.nodes.new("ShaderNodeVectorDisplacement")
    disp.space = "WORLD"
    nt.links.new(comb.outputs[0], disp.inputs["Vector"])
    nt.links.new(disp.outputs[0], out.inputs["Displacement"])
    mat.displacement_method = "DISPLACEMENT"


def sky(top="#4C9DE8", horizon="#CFE8F7", glow="#FFE6C8"):
    """A gradient sky for the camera (nothing is lit by it)."""
    scene = bpy.context.scene
    world = bpy.data.worlds.get("ValleySky") or bpy.data.worlds.new("ValleySky")
    scene.world = world
    world.use_nodes = True
    nt = world.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputWorld")
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tc.outputs["Generated"], sep.inputs[0])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    cr = ramp.color_ramp
    cr.elements[0].position = 0.0
    cr.elements[0].color = (*[ta.srgb_to_linear(c) for c in ta.hex_rgb(glow)], 1)
    cr.elements[1].position = 0.3
    cr.elements[1].color = (*[ta.srgb_to_linear(c) for c in ta.hex_rgb(top)], 1)
    e = cr.elements.new(0.06)
    e.color = (*[ta.srgb_to_linear(c) for c in ta.hex_rgb(horizon)], 1)
    nt.links.new(sep.outputs["Z"], ramp.inputs["Fac"])
    bg = nt.nodes.new("ShaderNodeBackground")
    nt.links.new(ramp.outputs["Color"], bg.inputs["Color"])
    dark = nt.nodes.new("ShaderNodeBackground")
    dark.inputs["Color"].default_value = (0, 0, 0, 1)
    path = nt.nodes.new("ShaderNodeLightPath")
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(path.outputs["Is Camera Ray"], mix.inputs[0])
    nt.links.new(dark.outputs[0], mix.inputs[1])
    nt.links.new(bg.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs["Surface"])


def mist(start=40.0, depth=180.0, colour="#D9ECF6"):
    """The haze: EEVEE's mist pass isn't composited, so a volume-free fake: the preview material mixes toward the haze
    by view distance (added to the bent material once)."""
    mat = kit.toon_material()
    if mat.get("hazed"):
        return
    mat["hazed"] = 1
    nt = mat.node_tree
    emit = next(n for n in nt.nodes if n.type == "EMISSION")
    src = emit.inputs["Color"].links[0].from_socket
    cam = nt.nodes.new("ShaderNodeCameraData")
    mr = nt.nodes.new("ShaderNodeMapRange")
    mr.inputs["From Min"].default_value = start
    mr.inputs["From Max"].default_value = start + depth
    nt.links.new(cam.outputs["View Distance"], mr.inputs["Value"])
    curve = nt.nodes.new("ShaderNodeMath")
    curve.operation = "POWER"
    curve.inputs[1].default_value = 0.8
    nt.links.new(mr.outputs[0], curve.inputs[0])
    k = nt.nodes.new("ShaderNodeMath")
    k.operation = "MULTIPLY"
    k.inputs[1].default_value = 0.85
    nt.links.new(curve.outputs[0], k.inputs[0])
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "VECTOR"
    nt.links.new(k.outputs[0], kit._sock(mix.inputs, "Factor_Float"))
    nt.links.new(src, kit._sock(mix.inputs, "A_Vector"))
    kit._sock(mix.inputs, "B_Vector").default_value = [ta.srgb_to_linear(c) for c in ta.hex_rgb(colour)]
    nt.links.new(kit._sock(mix.outputs, "Result_Vector"), emit.inputs["Color"])


def place(name, x, y, z, yaw=0.0, scale=1.0, coll=None):
    src = bpy.data.objects.get(name)
    if src is None:
        return None
    o = src.copy()
    (coll or ta.collection("GameView")).objects.link(o)
    o.location = U(x, y, z)
    o.rotation_euler = (0, 0, math.radians(yaw))
    o.scale = (scale, scale, scale)
    o.hide_render = False
    return o


VERGE = ["Verge", "Verge2", "Verge", "FlowerBed", "FlowerBed2", "GrassTall", "GrassFlowers", "RockSmall", "Mushrooms",
         "Pebbles", "BushFlower"]
EDGE = ["GiantLeaf", "GiantLeaf2", "FernBig", "Bush", "BushBerry", "Horsetail", "Verge2", "RockSmall2"]
MID = ["TreeFern", "TreeFern2", "Cycad", "Cycad2", "Palm", "TreeRound", "RockBig", "RockBig2", "Araucaria", "TreeFern",
       "GiantLeaf2", "Bush"]


def scatter(seed=5, z0=-60.0, z1=150.0):
    """The scenery beside the path, like RunnerWorld lays it out per 10 m tile: a verge of low plants right at the path's
    edge, big leaves and bushes behind it walling the path in, trees and big rocks beyond, far forest clumps on the banks,
    the valley's walls."""
    rng = random.Random(seed)
    z = z0
    while z < z1:
        for side in (-1, 1):
            for k in range(4):
                x = side * rng.uniform(4.2, 5.2)
                place(rng.choice(VERGE), x, ground_height(x), z + (k + rng.uniform(0.0, 0.8)) * 2.5, rng.uniform(0, 360),
                      rng.uniform(0.9, 1.25))
            for k in range(3):
                x = side * rng.uniform(5.6, 8.0)
                place(rng.choice(EDGE), x, ground_height(x), z + (k + rng.uniform(0.0, 0.9)) * 3.3, rng.uniform(0, 360),
                      rng.uniform(0.9, 1.3))
            for _ in range(2):
                x = side * rng.uniform(8.5, 17.0)
                place(rng.choice(MID), x, ground_height(x), z + rng.uniform(0, 10), rng.uniform(0, 360), rng.uniform(0.85, 1.2))
            if rng.random() < 0.8:
                x = side * rng.uniform(17.0, 30.0)
                place(rng.choice(["ForestFar", "ForestFar2"]), x, ground_height(x) - 0.3, z + rng.uniform(0, 10),
                      rng.uniform(-20, 20) + (90 if side > 0 else -90), rng.uniform(0.9, 1.3))
        z += 10.0
    for k in range(-2, 6):
        for side in (-1, 1):
            place("Cliff", side * 34.0, ground_height(34.0) - 1.0, k * 40.0 + 20.0, 0.0 if side < 0 else 180.0, 1.0)
    place("Volcano", -70.0, 0.0, 330.0, 20.0, 1.0)
    place("Mountains", 30.0, 0.0, 380.0, 0.0, 1.0)
    place("Mountains", -120.0, 0.0, 360.0, 0.0, 0.9)
    for k, (x, y, z, s) in enumerate(((-30, 34, 140, 2.0), (25, 40, 170, 2.6), (-5, 46, 220, 3.0), (55, 30, 120, 1.8))):
        place(f"Cloud{k % 3 + 1}", x, y, z, rng.uniform(-30, 30), s)


def lanes(layout):
    """Obstacles and eggs: (name, lane -1..1 or None for the whole way, metres ahead)."""
    for name, lane, ahead in layout:
        x = 0.0 if lane is None else lane * LANE
        if name.startswith("Egg"):
            place(name, x, 1.0, ahead, 25.0)
        else:
            place(name, x, 0.0, ahead)


def hero(x=0.0):
    rig = bpy.data.objects.get("DinoRig")
    if rig is not None:
        rig.location = U(x, 0, 0)
        rig.rotation_euler = (0, 0, 0)
        for o in bpy.data.objects:
            if o.parent is rig:
                o.hide_render = False


def render_all():
    out_dir = os.path.join(HERE, "previews")
    coll = ta.collection("GameView")
    for o in list(coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for o in kit_objects():
        o.hide_render = True
    mat = kit.toon_material()
    bend_material(mat)
    mist()
    ground(coll)
    scatter()
    kit.sun((52, 0, 200), 3.2)
    sky()
    hero()
    paths = []
    lanes([("LogLow", -1, 22.0), ("Egg", 0, 18.0), ("Egg", 0, 20.5), ("Egg", 0, 23.0), ("Boulder", 1, 30.0),
           ("BrontoNeck", None, 46.0), ("Trike", -1, 62.0), ("VineBridge", 1, 64.0), ("LogLong", None, 80.0),
           ("EggGold", 0, 70.0)])
    kit.camera("GameCam", U(*CAM), U(0.0, 3.4 - math.tan(math.radians(14)) * 10, CAM[2] + 10.0), 60.0, 500.0)
    paths.append(kit.render(os.path.join(out_dir, "game_run.png"), res=(1280, 720)))
    # the title view: in front of the runner, looking back at it
    kit.camera("TitleCam", U(2.6, 1.6, 4.2), U(0.0, 1.0, 0.0), 40.0, 500.0)
    paths.append(kit.render(os.path.join(out_dir, "game_title.png"), res=(1280, 720)))
    return paths


def kit_objects():
    coll = bpy.data.collections.get("Valley")
    return list(coll.objects) if coll else []
