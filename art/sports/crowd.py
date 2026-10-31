"""Sporte!'s crowd: people rendered from the athlete model (run with build.py --crowd after the clips exist).

    render_atlas()   16 people x 2 poses (clapping, cheering with the arms up), the upper body from the front, into
                     Assets/Games/Sports/Art/crowd_atlas.png (8 columns x 4 rows; row 0-1 clapping, 2-3 cheering;
                     person i is at (i % 8, i // 8) and its cheering self two rows down)

The game draws one quad a seat (Tundu/Crowd): the people match the players' look, cost two triangles each.
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "tools"))
import athlete as at  # noqa: E402
import tundu_art as ta  # noqa: E402

CELL_W, CELL_H = 128, 176
COLS, ROWS = 8, 2

# shirts: the home colour (sky) the most, then the brand's others; skin, hair, style
PEOPLE = [
    ("#31C6F4", "#F2C6A0", "#3A2416", "Crop", True), ("#31C6F4", "#C98E66", "#1E1410", "Afro", False),
    ("#FFFFFF", "#F7D5B8", "#B8732E", "Ponytail", True), ("#31C6F4", "#E8B48F", "#2A1A12", "Bob", True),
    ("#FF5A4E", "#F2C6A0", "#4A2E1E", "Spiky", True), ("#31C6F4", "#8D5B3E", "#120C0A", "Buzz", False),
    ("#FFC93C", "#F7D5B8", "#6B3D22", "Bun", False), ("#141938", "#E8B48F", "#3A2416", "Crop", True),
    ("#31C6F4", "#F2C6A0", "#E0B060", "Pigtails", True), ("#FF5A4E", "#C98E66", "#1E1410", "Crop", False),
    ("#FFFFFF", "#8D5B3E", "#120C0A", "Afro", True), ("#31C6F4", "#F7D5B8", "#7A4A2A", "Spiky", False),
    ("#7B61FF", "#E8B48F", "#2A1A12", "Ponytail", False), ("#31C6F4", "#F2C6A0", "#4A2E1E", "Buzz", True),
    ("#6FD66A", "#C98E66", "#2A1A12", "Bob", False), ("#FF5A4E", "#F7D5B8", "#B8732E", "Bun", True),
]


def render_atlas():
    import athlete_anim
    if "Clap" not in bpy.data.actions:
        athlete_anim.animate(only=["Clap", "Cheer"])
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.film_transparent = True
    scene.render.resolution_x, scene.render.resolution_y = CELL_W, CELL_H
    scene.render.resolution_percentage = 100
    scene.view_settings.view_transform = "Standard"
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    arm = bpy.data.objects["AthleteRig"]
    cam = bpy.data.objects.get("CrowdCam") or bpy.data.objects.new("CrowdCam", bpy.data.cameras.new("CrowdCam"))
    if cam.name not in scene.collection.objects:
        scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 1.45
    cam.location = Vector((0, -6, 1.45))
    cam.rotation_euler = (math.radians(90), 0, 0)
    scene.camera = cam
    for name, rot, energy in (("CrowdKey", (55, 0, -25), 3.2), ("CrowdFill", (70, 0, 160), 1.0)):
        light = bpy.data.objects.get(name) or bpy.data.objects.new(name, bpy.data.lights.new(name, "SUN"))
        if light.name not in scene.collection.objects:
            scene.collection.objects.link(light)
        light.rotation_euler = [math.radians(a) for a in rot]
        light.data.energy = energy
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.6, 0.7, 0.9, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.7
    mat = at.preview_material()
    for o in at.pieces():
        o.data.materials.clear()
        o.data.materials.append(mat)
    out_dir = os.path.join(HERE, "previews", "crowd")
    os.makedirs(out_dir, exist_ok=True)
    cells = []
    for pose, action, frame in (("clap", "Clap", 9), ("cheer", "Cheer", 12)):
        arm.animation_data.action = bpy.data.actions[action]
        for i, (shirt, skin, hair, style, sleeves) in enumerate(PEOPLE):
            wear = ["Body", "Face", "Shirt", "Shorts", "Hands", f"Hair_{style}"] + (["Sleeves"] if sleeves else [])
            colours = {"shirt": shirt, "skin": skin, "hair": hair, "shirt_accent": "#FFFFFF" if shirt != "#FFFFFF" else "#31C6F4",
                       "shorts": "#2A3050"}
            for o in at.pieces():
                show = o.name in wear
                o.hide_render = not show
                if show:
                    at.recolour(o, colours)
            # a little variety: people lean and turn their heads differently
            scene.frame_set(frame + (i % 3))
            path = os.path.join(out_dir, f"{pose}_{i:02d}.png")
            scene.render.filepath = path
            bpy.ops.render.render(write_still=True)
            cells.append(path)
    for o in at.pieces():
        o.hide_render = False
    return cells
