"""Tundu's chibi characters: builds every piece, renders previews, bakes ambient occlusion, exports (Blender 5.1).

    build()      every piece as its own mesh object, skinned to the armature "ChibiRig" (in "Chibi")
    bake_ao()    ambient occlusion baked into each piece's second UV map (v), each piece next to what it is worn with
    preview()    line-ups of characters and the wardrobe, rendered to art/chibi/previews/
    export()     Chibi.fbx + chibi.json for Unity, and the generated ChibiData.cs; export(low=True) after a build at
                 kit.LOW_DETAIL: Chibi_Low.fbx + chibi_low.json (the same pieces and rig, about a third of the triangles)
"""

import json
import math
import os

import bpy
from mathutils import Matrix, Vector

import body
import kit
import tundu_art as ta
import tundu_rig as tr

PIECES = {}   # object name -> slot


def build():
    ta.clear_scene()
    for coll in list(bpy.data.collections):
        if coll.name in ("Chibi", "Preview"):
            for o in list(coll.objects):
                bpy.data.objects.remove(o, do_unlink=True)
    PIECES.clear()
    coll = ta.collection("Chibi")
    arm = kit.make_armature(coll)
    made = []

    def add(part, slot):
        o = part.to_object(coll, arm)
        PIECES[o.name] = slot
        made.append(o)

    bvh = body.head_bvh()
    add(body.head().merge(body.blush(bvh)), "head")
    add(body.body(), "body")
    for p in body.eye_pieces(bvh):
        add(p, "eyes")
    for p in body.mouth_pieces(bvh):
        add(p, "mouth")
    try:
        import wardrobe
        for slot, parts in wardrobe.all_pieces(bvh).items():
            for p in parts:
                add(p, slot)
    except ImportError:
        pass
    return {"pieces": len(made), "tris": sum(sum(len(pl.vertices) - 2 for pl in o.data.polygons) for o in made)}


def pieces():
    return [o for o in bpy.data.collections["Chibi"].objects if o.type == "MESH"]


# ---------------------------------------------------------------------------------------------------- previews

BASE = ["Head", "Body", "Eyes", "Mouth"]


def looks():
    try:
        import wardrobe
        return wardrobe.LOOKS
    except ImportError:
        return [(BASE, {})]


def place_copy(src, coll, offset, name):
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(src.evaluated_get(dg))
    o = bpy.data.objects.new(name, me)
    o.matrix_world = Matrix.Translation(offset) @ src.matrix_world
    coll.objects.link(o)
    return o


def preview(name="lineup", only=None):
    """Renders the looks side by side (copies of their pieces), from the front, three-quarter and the back, plus a
    close-up of the first one's face."""
    out_dir = os.path.join(kit.HERE, "previews")
    os.makedirs(out_dir, exist_ok=True)
    coll = ta.collection("Preview")
    for o in list(coll.objects):
        if o.type == "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    lks = looks() if only is None else [looks()[i] for i in only]
    gap = 1.05
    per_row = 4
    rows = (len(lks) + per_row - 1) // per_row

    def place(i):
        r, c = divmod(i, per_row)
        return Vector(((c - (per_row - 1) / 2) * gap, r * 1.4, -r * 0.0))
    for i, (names, colours) in enumerate(lks):
        off = place(i)
        for n in names:
            src = bpy.data.objects.get(n)
            if src is None:
                print("[chibi] no piece", n)
                continue
            kit.recolour(src, colours)
            place_copy(src, coll, off, f"pv{i}_{n}")
    for o in pieces():
        o.hide_render = True
    scene = bpy.context.scene
    paths = []
    for r in range(rows):
        for view, yaw, pitch in (("front", 0, 4), ("three", -28, 8), ("back", 180, 4)):
            # each row on its own (the others hidden)
            for o in coll.objects:
                if o.type == "MESH":
                    o.hide_render = not o.name.startswith(tuple(f"pv{i}_" for i in range(r * per_row, (r + 1) * per_row)))
            ta.preview_rig(target=(0, r * 1.4, 0.8), distance=5.4, yaw_deg=yaw, pitch_deg=pitch, lens=50)
            lights()
            paths.append(ta.render_preview(os.path.join(out_dir, f"{name}{r}_{view}.png"), res=(2000, 1000)))
    for o in coll.objects:
        o.hide_render = False
    # the first look's face, close
    off = place(0)
    ta.preview_rig(target=(off.x, 0, 1.1), distance=1.5, yaw_deg=-12, pitch_deg=2, lens=50)
    lights()
    paths.append(ta.render_preview(os.path.join(out_dir, f"{name}_face.png"), res=(900, 900)))
    for o in pieces():
        o.hide_render = False
    return paths


def lights():
    """Soft studio light like the reference sheet: a warm key from the front left, a cool fill, a rim from behind."""
    coll = ta.collection("Preview")
    for name, rot, energy, colour in (("PreviewKey", (48, 0, -32), 3.2, (1.0, 0.97, 0.92)),
                                      ("PreviewFill", (70, 0, 120), 1.1, (0.85, 0.9, 1.0)),
                                      ("PreviewRim", (60, 0, 175), 1.8, (1.0, 1.0, 1.0))):
        light = bpy.data.objects.get(name)
        if light is None:
            light = bpy.data.objects.new(name, bpy.data.lights.new(name, "SUN"))
            coll.objects.link(light)
        light.rotation_euler = [math.radians(a) for a in rot]
        light.data.energy = energy
        light.data.color = colour
        light.data.angle = math.radians(12)
    world = bpy.context.scene.world
    bg = world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (*[ta.srgb_to_linear(c) for c in ta.hex_rgb("#F4ECF7")], 1)
    bg.inputs["Strength"].default_value = 0.85


# ---------------------------------------------------------------------------------------------------- ambient occlusion

def bake_plan():
    """Which pieces each piece is baked next to: what it is always worn with (never optional pieces that would leave
    their shadow behind when they are not worn)."""
    names = [o.name for o in pieces()]
    plan = {}
    for o in pieces():
        slot = PIECES.get(o.name, "")
        n = o.name
        if n == "Head":
            plan[n] = ["Head", "Body"]
        elif n == "Body":
            plan[n] = ["Body", "Head", "Tee", "Shorts"]
        elif slot in ("eyes", "mouth"):
            plan[n] = ["Head", n]
        elif slot == "hair":
            plan[n] = ["Head", "Body", n]
        elif slot in ("hat", "ears", "acc"):
            plan[n] = ["Head", "Body", n]
        elif slot == "top":
            plan[n] = ["Head", "Body", "Shorts", n]
        elif slot == "bottom":
            plan[n] = ["Body", "Tee", "Sneakers", n]
        elif slot == "shoes":
            plan[n] = ["Body", n]
        else:
            plan[n] = ["Head", "Body", "Tee", n]
        plan[n] = [x for x in plan[n] if x in names]
    return plan


def bake_ao(samples=40):
    """Bakes ambient occlusion into each piece's second UV map (v = 0.35 + 0.65 * occlusion)."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.render.bake.target = "VERTEX_COLORS"
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.light_settings.distance = 0.28
    done = []
    for target, visible in bake_plan().items():
        for o in pieces():
            show = o.name in visible
            o.hide_render = not show
            o.hide_set(not show)
        obj = bpy.data.objects[target]
        me = obj.data
        attr = me.color_attributes.get("AO") or me.color_attributes.new("AO", "FLOAT_COLOR", "POINT")
        me.color_attributes.active_color = attr
        for o in bpy.context.view_layer.objects:
            o.select_set(False)
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.bake(type="AO")
        uv = me.uv_layers["Region"]
        for poly in me.polygons:
            for li in poly.loop_indices:
                c = attr.data[me.loops[li].vertex_index].color
                ao = (c[0] + c[1] + c[2]) / 3
                uv.data[li].uv = (uv.data[li].uv[0], 0.35 + 0.65 * ao)
        done.append(target)
    for o in pieces():
        o.hide_render = False
        o.hide_set(False)
    return len(done)


# ---------------------------------------------------------------------------------------------------- export

def export(low=False):
    """Chibi.fbx (every piece skinned to the rig) into the Unity art folder, ChibiData.cs next to the runtime,
    art/chibi/chibi.json (the pieces, their slots and triangle counts). low: the build was made at kit.LOW_DETAIL:
    Chibi_Low.fbx and chibi_low.json (ChibiData.cs stays: the same pieces)."""
    arm = bpy.data.objects["ChibiRig"]
    for o in pieces():
        for name in ("Col", "AO"):
            attr = o.data.color_attributes.get(name)
            if attr:
                o.data.color_attributes.remove(attr)
    os.makedirs(kit.UNITY_ART, exist_ok=True)
    path = tr.export_fbx(os.path.join(kit.UNITY_ART, "Chibi_Low.fbx" if low else "Chibi.fbx"), arm, pieces())
    info = {"regions": kit.REGIONS, "pieces": {o.name: PIECES.get(o.name, "?") for o in pieces()},
            "tris": {o.name: sum(len(pl.vertices) - 2 for pl in o.data.polygons) for o in pieces()}}
    with open(os.path.join(kit.HERE, "chibi_low.json" if low else "chibi.json"), "w", encoding="utf-8") as f:
        json.dump(info, f, indent=1)
    if low:
        return path
    names = [o.name for o in pieces()]
    lines = [
        "// Generated by art/chibi/build.py (chibi.export): do not edit, rebuild the art instead.",
        "namespace MoveIt.Games",
        "{",
        "    /// <summary>What art/chibi made: the colour regions (Tundu/Athlete's table) and the pieces with their slots.</summary>",
        "    public static class ChibiData",
        "    {",
        "        public static readonly string[] Regions =",
        "        {",
        "            " + ", ".join(f'"{r}"' for r in kit.REGIONS),
        "        };",
        "",
        "        public static readonly string[] Pieces =",
        "        {",
    ]
    for k in range(0, len(names), 8):
        lines.append("            " + ", ".join(f'"{n}"' for n in names[k:k + 8]) + ",")
    lines += [
        "        };",
        "",
        "        /// <summary>The slot of each of <see cref=\"Pieces\"/> (head, body, eyes, mouth, hair, hat, ears, top, bottom,",
        "        /// shoes, acc, back).</summary>",
        "        public static readonly string[] Slots =",
        "        {",
    ]
    for k in range(0, len(names), 8):
        lines.append("            " + ", ".join(f'"{PIECES.get(n, "?")}"' for n in names[k:k + 8]) + ",")
    lines += [
        "        };",
        "",
        "        public static string SlotOf(string piece)",
        "        {",
        "            int i = System.Array.IndexOf(Pieces, piece);",
        "            return i < 0 ? null : Slots[i];",
        "        }",
        "    }",
        "}",
        "",
    ]
    os.makedirs(kit.UNITY_SCRIPTS, exist_ok=True)
    with open(os.path.join(kit.UNITY_SCRIPTS, "ChibiData.cs"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(lines))
    return path
