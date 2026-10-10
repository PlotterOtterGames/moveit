"""Vrapo!'s sky (run inside Blender 5.1): a puffy cloud and a cute pterodactyl with a flapping "Fly" loop.

    build()    Cloud + Ptero (+ PteroRig) in the "Sky" collection
    export()   Cloud.fbx, Ptero.fbx into Assets/Games/Runner/Art
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Quaternion, Vector

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "tools"))
import tundu_art as ta  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))


def _smooth_blob(name, parts, coll, tris, voxel=0.06):
    bm = bmesh.new()
    for c, r in parts:
        ta.ellipsoid(bm, c, r)
    obj = ta.mesh_object(name, bm, coll)
    rm = obj.modifiers.new("Remesh", "REMESH")
    rm.mode, rm.voxel_size, rm.adaptivity = "VOXEL", voxel, 0.0
    sm = obj.modifiers.new("Smooth", "LAPLACIANSMOOTH")
    sm.lambda_factor, sm.iterations, sm.use_volume_preserve = 0.8, 6, True
    ta.bake_modifiers(obj)
    dec = obj.modifiers.new("Decimate", "DECIMATE")
    dec.ratio = min(1.0, tris / max(1, ta.tri_count(obj)))
    ta.bake_modifiers(obj)
    return obj


def build_cloud(coll):
    """A flat-bottomed puffy cloud, about 8 m wide (it's far away in the sky)."""
    parts = [((0, 0, 1.0), (2.2, 1.6, 1.3)), ((-2.3, 0.2, 0.6), (1.6, 1.3, 0.9)), ((2.4, -0.1, 0.7), (1.8, 1.4, 1.0)),
             ((0.8, 0.4, 1.9), (1.4, 1.1, 1.1)), ((-1.0, -0.3, 1.6), (1.2, 1.0, 1.0))]
    cloud = _smooth_blob("Cloud", parts, coll, tris=700, voxel=0.12)
    for v in cloud.data.vertices:             # flatten the underside like a real cumulus
        if v.co.z < 0.25:
            v.co.z = 0.25 + (v.co.z - 0.25) * 0.15
    ta.paint(cloud, lambda c, n: "white" if n.z > -0.3 else "smoke")
    return ta.finish(cloud, smooth=True)


def build_ptero(coll):
    """A friendly pterodactyl (about 2.6 m wingspan) facing -Y: purple body, coral crest and wings, sun beak."""
    body = _smooth_blob("PteroBody", [((0, 0, 0), (0.17, 0.42, 0.15)), ((0, -0.48, 0.08), (0.14, 0.2, 0.13)),
                                      ((0, 0.45, -0.02), (0.07, 0.25, 0.06))], coll, tris=900, voxel=0.025)
    ta.paint_all(body, "grape")
    pb = ta.PartBuilder()
    pb.add("sun", lambda bm: ta.cone(bm, (0, -0.62, 0.05), (0, -0.98, -0.02), 0.07, segments=8))
    pb.add("coral", lambda bm: ta.cone(bm, (0, -0.42, 0.17), (0, -0.05, 0.42), 0.09, segments=8, flat=0.3))
    for s in (1, -1):
        pb.add("white", lambda bm, s=s: ta.ellipsoid(bm, (0.1 * s, -0.56, 0.13), (0.045, 0.045, 0.045), segments=10, rings=6))
        pb.add("night", lambda bm, s=s: ta.ellipsoid(bm, (0.125 * s, -0.59, 0.135), (0.025, 0.025, 0.025), segments=8, rings=5))

        def wing(bm, s=s):
            # a curved membrane: leading edge from the shoulder to the tip, trailing edge back to the hip
            pts = [Vector((0.1 * s, -0.18, 0.06)), Vector((0.75 * s, -0.12, 0.14)), Vector((1.3 * s, 0.02, 0.08)),
                   Vector((0.7 * s, 0.22, 0.02)), Vector((0.12 * s, 0.28, 0.0))]
            for under in (False, True):
                drop = Vector((0, 0, -0.02)) if under else Vector()
                verts = [bm.verts.new(p + drop) for p in pts]
                face = bm.faces.new(list(reversed(verts)) if (under ^ (s < 0)) else verts)
                face.normal_update()
        pb.add("coral", wing)
    details = pb.to_object("PteroDetails", coll, smooth_colors=("white", "night"))

    # rig: body + two wings, rigid weights (it's small in the sky)
    arm_data = bpy.data.armatures.new("PteroRig")
    rig = bpy.data.objects.new("PteroRig", arm_data)
    coll.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    root = arm_data.edit_bones.new("Body")
    root.head, root.tail = (0, 0.3, 0), (0, -0.3, 0)
    for side, s in (("L", 1), ("R", -1)):
        b = arm_data.edit_bones.new(f"Wing.{side}")
        b.head, b.tail = (0.1 * s, 0.0, 0.06), (1.3 * s, 0.02, 0.08)
        b.align_roll(Vector((0, 0, 1)))
        b.parent = root
    bpy.ops.object.mode_set(mode="OBJECT")

    bpy.ops.object.select_all(action="DESELECT")
    for o in (body, details):
        o.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    ptero = body
    ptero.name = ptero.data.name = "Ptero"
    ptero.data.materials.clear()
    ptero.data.materials.append(ta.palette_material())
    groups = {n: ptero.vertex_groups.new(name=n) for n in ("Body", "Wing.L", "Wing.R")}
    for v in ptero.data.vertices:
        x = v.co.x
        name = "Wing.L" if x > 0.16 else "Wing.R" if x < -0.16 else "Body"
        groups[name].add([v.index], 1.0, "REPLACE")
    ptero.parent = rig
    mod = ptero.modifiers.new("Armature", "ARMATURE")
    mod.object = rig

    # Fly: 24 frames, wings flap up/down (roll), body bobs opposite
    old = bpy.data.actions.get("Fly")
    if old:
        bpy.data.actions.remove(old)
    action = bpy.data.actions.new("Fly")
    action.use_fake_user = True
    rig.animation_data_create()
    rig.animation_data.action = action
    rest = {pb_.name: pb_.bone.matrix_local.to_3x3() for pb_ in rig.pose.bones}
    for f in range(25):
        a = 2 * math.pi * (f % 24) / 24
        for pb_ in rig.pose.bones:
            pb_.rotation_mode = "QUATERNION"
            pb_.location = (0, 0, 0)
            pb_.rotation_quaternion = Quaternion()
        rig.pose.bones["Body"].location = rest["Body"].inverted() @ Vector((0, 0, -0.06 * math.sin(a)))
        for side, s in (("L", 1), ("R", -1)):
            angle = math.radians(38 * math.sin(a)) * s          # wing tips up and down together
            r = rest[f"Wing.{side}"]
            q = (r.inverted() @ Quaternion((0, 1, 0), angle).to_matrix() @ r).to_quaternion()
            rig.pose.bones[f"Wing.{side}"].rotation_quaternion = q
        for pb_ in rig.pose.bones:
            pb_.keyframe_insert("rotation_quaternion", frame=f + 1)
            pb_.keyframe_insert("location", frame=f + 1)
    rig.location = (6, 0, 3)
    return ptero


def build():
    for name in ("Cloud", "Ptero", "PteroRig", "PteroBody", "PteroDetails"):
        o = bpy.data.objects.get(name)
        if o:
            bpy.data.objects.remove(o, do_unlink=True)
    coll = ta.collection("Sky")
    cloud = build_cloud(coll)
    cloud.location = (-6, 0, 0)
    ptero = build_ptero(coll)
    return {"cloud": ta.tri_count(cloud), "ptero": ta.tri_count(ptero)}


def preview(name="sky"):
    ta.preview_rig(target=(0, 0, 1.6), distance=16, yaw_deg=-25, pitch_deg=10, lens=40)
    return ta.render_preview(os.path.join(HERE, "previews", f"{name}.png"), res=(1100, 600))


def export():
    out = []
    cloud = bpy.data.objects["Cloud"]
    keep = cloud.location.copy()
    cloud.location = (0, 0, 0)
    bpy.ops.object.select_all(action="DESELECT")
    cloud.select_set(True)
    bpy.context.view_layer.objects.active = cloud
    path = os.path.join(ta.UNITY_ART, "Cloud.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True, bake_anim=False,
                             mesh_smooth_type="FACE", path_mode="STRIP")
    cloud.location = keep
    out.append(path)
    rig, ptero = bpy.data.objects["PteroRig"], bpy.data.objects["Ptero"]
    keep = rig.location.copy()
    rig.location = (0, 0, 0)
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    ptero.select_set(True)
    bpy.context.view_layer.objects.active = rig
    path = os.path.join(ta.UNITY_ART, "Ptero.fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"},
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, add_leaf_bones=False, bake_anim=True,
                             bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, mesh_smooth_type="FACE", path_mode="STRIP")
    rig.location = keep
    out.append(path)
    return [os.path.basename(p) for p in out]
