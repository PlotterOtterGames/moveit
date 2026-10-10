"""Shared helpers for Tundu's Blender art scripts (run inside Blender 5.1).

Style: low-poly, rounded, flat colours from one small palette texture. Every model maps each face's UVs to the
centre of one palette cell, so a whole game world uses ONE material and ONE 64x64 texture (cheap on the box).

Conventions: metres, Z up, characters face -Y in Blender (= +Z, "forward", in Unity after FBX export).
"""

import math
import os

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
UNITY_ART = os.path.join(REPO, "unity", "MoveItGames", "Assets", "Games", "Runner", "Art")

# Palette: 8x8 cells of 8 px = a 64x64 texture. Index = row * 8 + column. Brand colours first (brand.json).
PALETTE = {
    "leaf": "#6FD66A", "leaf_dark": "#3FA650", "sun": "#FFC93C", "coral": "#FF5A4E",
    "white": "#FFFFFF", "night": "#141938", "cream": "#FFF1D6", "sky": "#31C6F4",
    "wood": "#9A5B34", "wood_dark": "#6B3D22", "wood_end": "#E0B07A", "rock": "#A39DB3",
    "rock_dark": "#7A7490", "fern": "#2E9E5B", "fern_light": "#58C46A", "grass": "#86C95A",
    "dirt": "#D9A066", "dirt_dark": "#B98250", "egg": "#FFF4E0", "lava": "#FF8A3D",
    "volcano": "#6A5A78", "volcano_dark": "#4E4260", "sand": "#F2D49B", "grape": "#7B61FF",
    "blush": "#FF8F86", "moss": "#5DB04E", "bone": "#F6E9CF", "smoke": "#E8E6F0",
}
CELLS = 8
CELL_PX = 8
PALETTE_NAME = "TunduPalette"


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def color_index(name):
    return list(PALETTE).index(name)


def cell_uv(name):
    i = color_index(name)
    col, row = i % CELLS, i // CELLS
    return ((col + 0.5) / CELLS, 1 - (row + 0.5) / CELLS)


PALETTE_PNG = os.path.join(REPO, "art", "tools", "tundu_palette.png")


def write_palette_png(path=PALETTE_PNG):
    """Writes the palette PNG (also copied next to the Unity art). Unknown cells are magenta."""
    size = CELLS * CELL_PX
    img = bpy.data.images.new("_palette_tmp", size, size, alpha=False)
    px = [0.0] * (size * size * 4)
    names = list(PALETTE)
    for y in range(size):
        row = (size - 1 - y) // CELL_PX          # image rows start at the bottom
        for x in range(size):
            i = row * CELLS + x // CELL_PX
            r, g, b = hex_rgb(PALETTE[names[i]]) if i < len(names) else (1.0, 0.0, 1.0)
            o = (y * size + x) * 4
            px[o:o + 4] = (r, g, b, 1.0)
    img.pixels.foreach_set(px)
    img.update()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    os.makedirs(UNITY_ART, exist_ok=True)
    import shutil
    shutil.copyfile(path, os.path.join(UNITY_ART, "tundu_palette.png"))
    return path


def palette_image():
    """The palette as a Blender image loaded from the PNG (written first if missing)."""
    img = bpy.data.images.get(PALETTE_NAME)
    if img is None:
        if not os.path.exists(PALETTE_PNG):
            write_palette_png()
        img = bpy.data.images.load(PALETTE_PNG)
        img.name = PALETTE_NAME
    return img


def palette_material():
    mat = bpy.data.materials.get("TunduPalette")
    if mat:
        return mat
    mat = bpy.data.materials.new("TunduPalette")
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = palette_image()
    tex.interpolation = "Closest"
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.75
    return mat


# ---------------------------------------------------------------------------------------------------- building

def clear_scene():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions, bpy.data.cameras, bpy.data.lights,
                 bpy.data.curves):
        for block in list(coll):
            if block.users == 0:
                coll.remove(block)


def collection(name, parent=None):
    coll = bpy.data.collections.get(name)
    if coll is None:
        coll = bpy.data.collections.new(name)
        (parent or bpy.context.scene.collection).children.link(coll)
    return coll


def ellipsoid(bm, center, radii, rot_deg=(0, 0, 0), segments=24, rings=14):
    """Adds a UV-sphere ellipsoid to bmesh bm."""
    m = (Matrix.Translation(center) @ Euler([math.radians(a) for a in rot_deg]).to_matrix().to_4x4()
         @ Matrix.Diagonal((*radii, 1)))
    bmesh.ops.create_uvsphere(bm, u_segments=segments, v_segments=rings, radius=1.0, matrix=m)


def cone(bm, base, tip, r_base, segments=8, flat=1.0, side=None):
    """A cone from base centre to tip. flat < 1 squashes it sideways (along `side`, default world X)."""
    base, tip = Vector(base), Vector(tip)
    axis = tip - base
    length = axis.length
    rot = axis.to_track_quat("Z", "Y").to_matrix().to_4x4()
    m = Matrix.Translation(base) @ rot @ Matrix.Translation((0, 0, length / 2))
    geom = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments, radius1=r_base, radius2=0.0,
                                 depth=length, matrix=m)
    if flat != 1.0:
        side = Vector(side or (1, 0, 0)).normalized()
        verts = geom["verts"]
        for v in verts:
            d = v.co - base
            v.co -= side * d.dot(side) * (1 - flat)
    return geom


def mesh_object(name, bm, coll=None):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    (coll or bpy.context.scene.collection).objects.link(obj)
    return obj


def bake_modifiers(obj):
    """Applies all modifiers (without operators) and returns the object."""
    dg = bpy.context.evaluated_depsgraph_get()
    new_me = bpy.data.meshes.new_from_object(obj.evaluated_get(dg))
    old = obj.data
    obj.modifiers.clear()
    obj.data = new_me
    bpy.data.meshes.remove(old)
    return obj


def paint(obj, rule):
    """Sets every face's UVs to a palette cell. rule(face_center, face_normal) -> palette colour name."""
    me = obj.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    uv = me.uv_layers.active.data
    mw = obj.matrix_world
    for poly in me.polygons:
        c = mw @ poly.center
        n = (mw.to_3x3() @ poly.normal).normalized()
        u, v = cell_uv(rule(c, n))
        for li in poly.loop_indices:
            uv[li].uv = (u, v)


def paint_faces(obj, names):
    """Sets polygon i's UVs to palette colour names[i]."""
    me = obj.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    uv = me.uv_layers.active.data
    for poly, name in zip(me.polygons, names):
        u, v = cell_uv(name)
        for li in poly.loop_indices:
            uv[li].uv = (u, v)


class PartBuilder:
    """Builds several small parts into one bmesh and remembers each face's palette colour."""

    def __init__(self):
        self.bm = bmesh.new()
        self.color_of = {}

    def add(self, color, make):
        before = set(self.bm.faces)
        make(self.bm)
        for f in set(self.bm.faces) - before:
            self.color_of[f] = color

    def to_object(self, name, coll=None, smooth_colors=()):
        """color may be a palette name or a function(face) -> name (e.g. by normal: lighter tops)."""
        self.bm.faces.ensure_lookup_table()
        self.bm.normal_update()
        names = []
        for f in self.bm.faces:
            c = self.color_of.get(f, "night")
            names.append(c(f) if callable(c) else c)
        obj = mesh_object(name, self.bm, coll)
        paint_faces(obj, names)
        for poly, n in zip(obj.data.polygons, names):
            poly.use_smooth = n in smooth_colors
        if not obj.data.materials:
            obj.data.materials.append(palette_material())
        return obj


def paint_all(obj, name):
    paint(obj, lambda c, n: name)


def finish(obj, smooth=True):
    me = obj.data
    if smooth:
        me.shade_smooth()
    else:
        me.shade_flat()
    if not me.materials:
        me.materials.append(palette_material())
    return obj


def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


# ---------------------------------------------------------------------------------------------------- preview

def preview_rig(target=(0, 0, 0.9), distance=4.2, yaw_deg=-35, pitch_deg=12, lens=50):
    coll = collection("Preview")
    cam = bpy.data.objects.get("PreviewCam")
    if cam is None:
        cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
        coll.objects.link(cam)
    cam.data.lens = lens
    yaw, pitch = math.radians(yaw_deg), math.radians(pitch_deg)
    # yaw 0 = looking at the character's face (camera on -Y), positive = to the character's left
    pos = Vector(target) + Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch),
                                   math.sin(pitch))) * distance
    cam.location = pos
    cam.rotation_euler = (Vector(target) - pos).to_track_quat("-Z", "Y").to_euler()
    for name, rot, energy in (("PreviewKey", (50, 10, -40), 3.5), ("PreviewFill", (60, -10, 140), 1.2)):
        light = bpy.data.objects.get(name)
        if light is None:
            light = bpy.data.objects.new(name, bpy.data.lights.new(name, "SUN"))
            coll.objects.link(light)
        light.rotation_euler = [math.radians(a) for a in rot]
        light.data.energy = energy
    scene = bpy.context.scene
    scene.camera = cam
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (*[srgb_to_linear(c) for c in hex_rgb("#BFE7F7")], 1)
    bg.inputs["Strength"].default_value = 0.9
    return cam


def render_preview(path, res=(900, 700)):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "Standard"
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path
