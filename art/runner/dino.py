"""Dino, the Vrapo! runner: a cute, chunky low-poly dinosaur in Tundu colours (run inside Blender 5.1).

    build()      body (ellipsoids -> voxel remesh -> smooth -> decimate) + eyes, spikes, claws, palette UVs
    preview()    renders art/runner/previews/dino.png

Facing -Y (Unity +Z), feet on Z = 0, about 1.75 m tall like the game's 1.8 m standing player.
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "tools"))
import tundu_art as ta  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
TARGET_TRIS = 3200

HEAD_C, HEAD_R = Vector((0, -0.36, 1.42)), Vector((0.30, 0.33, 0.28))
SNOUT_C, SNOUT_R = Vector((0, -0.64, 1.34)), Vector((0.23, 0.22, 0.17))

# (centre, radii, rotation degrees); left side parts are mirrored to the right
BODY = [
    ((0, -0.04, 0.84), (0.34, 0.40, 0.37), (-18, 0, 0)),   # belly
    ((0, 0.14, 0.78), (0.31, 0.30, 0.29), (0, 0, 0)),      # hips
    ((0, -0.20, 1.00), (0.27, 0.27, 0.29), (0, 0, 0)),     # chest
    ((0, -0.28, 1.18), (0.20, 0.20, 0.24), (-15, 0, 0)),   # neck
    (tuple(HEAD_C), tuple(HEAD_R), (8, 0, 0)),             # head
    (tuple(SNOUT_C), tuple(SNOUT_R), (6, 0, 0)),           # snout
]


def tail_parts(n=9):
    """A smooth, tapering tail that curls up a little at the tip: overlapping ellipsoids along a curve."""
    parts = []
    for i in range(n):
        t = i / (n - 1)
        y = 0.30 + 0.98 * t
        z = 0.74 - 0.20 * t + 0.10 * t * t
        r = 0.21 * (1 - t) + 0.045 * t
        parts.append(((0, y, z), (r, 0.075 + r * 0.6, r * 0.92), (8, 0, 0)))
    return parts
LIMBS_LEFT = [
    ((0.21, 0.06, 0.58), (0.15, 0.20, 0.24), (22, 0, 0)),   # thigh
    ((0.22, 0.06, 0.29), (0.10, 0.10, 0.18), (-22, 0, 0)),  # shin
    ((0.22, -0.08, 0.07), (0.12, 0.19, 0.075), (0, 0, 0)),  # foot
    ((0.25, -0.36, 0.93), (0.065, 0.11, 0.065), (40, 0, -10)),  # upper arm
    ((0.27, -0.46, 0.87), (0.06, 0.065, 0.055), (0, 0, 0)),     # hand
]


def on_ellipsoid(center, radii, direction):
    u = Vector(direction).normalized()
    t = 1 / math.sqrt(sum((u[i] / radii[i]) ** 2 for i in range(3)))
    return center + u * t


def build_body(coll):
    bm = bmesh.new()
    for c, r, rot in BODY + tail_parts():
        ta.ellipsoid(bm, c, r, rot)
    for c, r, rot in LIMBS_LEFT:
        ta.ellipsoid(bm, c, r, rot)
        ta.ellipsoid(bm, (-c[0], c[1], c[2]), r, (rot[0], -rot[1], -rot[2]))
    body = ta.mesh_object("DinoBody", bm, coll)
    rm = body.modifiers.new("Remesh", "REMESH")
    rm.mode, rm.voxel_size, rm.adaptivity = "VOXEL", 0.022, 0.0
    sm = body.modifiers.new("Smooth", "LAPLACIANSMOOTH")
    sm.lambda_factor, sm.iterations, sm.use_volume_preserve = 0.8, 6, True
    ta.bake_modifiers(body)
    dec = body.modifiers.new("Decimate", "DECIMATE")
    dec.ratio = min(1.0, TARGET_TRIS / max(1, ta.tri_count(body)))
    ta.bake_modifiers(body)
    return body


BELLY = [
    ((0, -0.11, 0.82), (0.25, 0.36, 0.33), (-18, 0, 0)),   # belly plate, just in front of the belly
    ((0, -0.29, 1.06), (0.16, 0.22, 0.28), (-25, 0, 0)),   # up the chest and neck
]


def build_belly(coll):
    """The yellow belly as its own smooth shape poking out of the body: a clean curved edge, no zigzag."""
    bm = bmesh.new()
    for c, r, rot in BELLY:
        ta.ellipsoid(bm, c, r, rot)
    belly = ta.mesh_object("DinoBelly", bm, coll)
    rm = belly.modifiers.new("Remesh", "REMESH")
    rm.mode, rm.voxel_size, rm.adaptivity = "VOXEL", 0.02, 0.0
    sm = belly.modifiers.new("Smooth", "LAPLACIANSMOOTH")
    sm.lambda_factor, sm.iterations, sm.use_volume_preserve = 0.8, 6, True
    ta.bake_modifiers(belly)
    dec = belly.modifiers.new("Decimate", "DECIMATE")
    dec.ratio = min(1.0, 900 / max(1, ta.tri_count(belly)))
    ta.bake_modifiers(belly)
    ta.paint_all(belly, "sun")
    return ta.finish(belly, smooth=True)


def top_of(body, y, x=0.0):
    """Highest surface point of the body at (x, y), and its normal."""
    inv = body.matrix_world.inverted()
    hit, loc, normal, _ = body.ray_cast(inv @ Vector((x, y, 3.0)), Vector((0, 0, -1)))
    return (body.matrix_world @ loc, normal) if hit else (None, None)


def build_details(body, coll):
    pb = ta.PartBuilder()

    def sphere(c, r, color, radii=None):
        pb.add(color, lambda bm: ta.ellipsoid(bm, c, radii or (r, r, r), segments=16, rings=10))

    # eyes: white ball, night pupil looking forward, a tiny highlight
    for s in (1, -1):
        eye_c = on_ellipsoid(HEAD_C, HEAD_R, (0.60 * s, -0.62, 0.50))
        eye_c -= (eye_c - HEAD_C).normalized() * 0.035
        sphere(eye_c, 0.105, "white")
        look = Vector((0.25 * s, -1.0, 0.05)).normalized()
        pupil_c = eye_c + look * 0.075
        sphere(pupil_c, 0.06, "night")
        sphere(pupil_c + look * 0.035 + Vector((0.02 * s, 0, 0.03)), 0.018, "white")
        # rosy cheek on the snout, and a nostril
        sphere(on_ellipsoid(SNOUT_C, SNOUT_R, (0.85 * s, -0.35, -0.25)), 0, "blush", radii=(0.012, 0.055, 0.04))
        sphere(on_ellipsoid(SNOUT_C, SNOUT_R, (0.28 * s, -0.9, 0.35)), 0, "leaf_dark", radii=(0.018, 0.012, 0.012))

    # spikes along the back, from the head to the tail tip (rounded coral plates)
    for y, size in ((-0.22, 0.11), (-0.03, 0.15), (0.17, 0.16), (0.38, 0.14), (0.59, 0.12),
                    (0.80, 0.10), (0.99, 0.08), (1.15, 0.06)):
        top, normal = top_of(body, y)
        if top is None:
            continue
        direction = (Vector(normal) * 0.6 + Vector((0, 0.35, 1.0)) * 0.4).normalized()
        base = top - direction * size * 0.25
        pb.add("coral", lambda bm, base=base, d=direction, s=size:
               ta.cone(bm, base, base + d * s * 1.3, s * 0.6, segments=10, flat=0.55))

    # three claws on each foot, two little teeth under the snout
    for s in (1, -1):
        for dx in (-0.065, 0.0, 0.065):
            base = Vector((0.22 * s + dx, -0.24 + abs(dx) * 0.5, 0.06))
            pb.add("cream", lambda bm, b=base: ta.cone(bm, b, b + Vector((0, -0.07, -0.025)), 0.028, segments=8))
        base = on_ellipsoid(SNOUT_C, SNOUT_R, (0.35 * s, -0.75, -0.55)) + Vector((0, 0.01, 0.02))
        pb.add("white", lambda bm, b=base: ta.cone(bm, b, b + Vector((0, -0.005, -0.06)), 0.024, segments=6))

    return pb.to_object("DinoDetails", coll, smooth_colors=("white", "night", "blush", "leaf_dark"))


def build():
    ta.clear_scene()
    for block in (bpy.data.materials.get("TunduPalette"), bpy.data.images.get(ta.PALETTE_NAME)):
        if block:
            (bpy.data.materials if isinstance(block, bpy.types.Material) else bpy.data.images).remove(block)
    ta.write_palette_png()
    coll = ta.collection("Dino")
    body = build_body(coll)
    ta.paint_all(body, "leaf")
    ta.finish(body, smooth=True)
    belly = build_belly(coll)
    details = build_details(body, coll)
    return {"body_tris": ta.tri_count(body), "belly_tris": ta.tri_count(belly), "detail_tris": ta.tri_count(details)}


def preview(name="dino", yaw=-35, pitch=12, distance=4.2):
    ta.preview_rig(target=(0, 0.1, 0.85), distance=distance, yaw_deg=yaw, pitch_deg=pitch)
    out = os.path.join(HERE, "previews", f"{name}.png")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    return ta.render_preview(out)
