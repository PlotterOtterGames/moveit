"""Shared rigging and animation helpers for Tundu's Blender art (run inside Blender 5.1).

    Pose            sets pose-bone rotations with axes given in armature space
    sample()        keys a clip by sampling a pose function every frame
    action_fcurves  f-curves of an action (Blender 4.4+ layered actions)
    export_fbx()    mesh(es) + armature + every action as its own clip, Unity axes

Axes: characters face -Y, Z up, their left = +X. Rotations in armature axes, degrees:
    pitch (around X): + = the top goes forward (a hanging limb swings BACK, a vertical bone leans forward)
    yaw   (around Z): + = turns to the character's left
    roll  (around Y): + = the top tilts toward the character's left
"""

import math
import os

import bpy
from mathutils import Matrix, Quaternion, Vector

FPS = 30


class Pose:
    def __init__(self, arm):
        self.arm = arm
        self.rest = {pb.name: pb.bone.matrix_local.to_3x3() for pb in arm.pose.bones}
        self.rest_head = {pb.name: pb.bone.head_local.copy() for pb in arm.pose.bones}

    def clear(self):
        for pb in self.arm.pose.bones:
            pb.rotation_mode = "QUATERNION"
            pb.rotation_quaternion = Quaternion()
            pb.location = (0, 0, 0)
            pb.scale = (1, 1, 1)

    def rot(self, bone, pitch=0.0, yaw=0.0, roll=0.0):
        """Sets (replaces) a bone's rotation. Degrees, armature axes (see the module docstring)."""
        r = (Matrix.Rotation(math.radians(yaw), 3, "Z") @ Matrix.Rotation(math.radians(pitch), 3, "X")
             @ Matrix.Rotation(math.radians(roll), 3, "Y"))
        m = self.rest[bone]
        self.arm.pose.bones[bone].rotation_quaternion = (m.inverted() @ r @ m).to_quaternion()

    def rot_m(self, bone, r):
        """Sets a bone's rotation from a 3x3 matrix in armature axes."""
        m = self.rest[bone]
        self.arm.pose.bones[bone].rotation_quaternion = (m.inverted() @ r @ m).to_quaternion()

    def move(self, bone, x=0.0, y=0.0, z=0.0):
        """Moves a bone (metres, armature axes)."""
        m = self.rest[bone]
        self.arm.pose.bones[bone].location = m.inverted() @ Vector((x, y, z))

    def squash(self, bone, s):
        self.arm.pose.bones[bone].scale = (s, s, s)

    def key(self, frame):
        for pb in self.arm.pose.bones:
            pb.keyframe_insert("rotation_quaternion", frame=frame)
            pb.keyframe_insert("location", frame=frame)
            pb.keyframe_insert("scale", frame=frame)


def new_action(arm, name):
    old = bpy.data.actions.get(name)
    if old:
        bpy.data.actions.remove(old)
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = action
    return action


def action_fcurves(action):
    """F-curves of an action (Blender 4.4+ layered actions keep them in layers/strips/channelbags)."""
    if hasattr(action, "fcurves") and len(getattr(action, "fcurves", [])):
        return list(action.fcurves)
    curves = []
    for layer in getattr(action, "layers", []):
        for strip in layer.strips:
            for bag in getattr(strip, "channelbags", []):
                curves.extend(bag.fcurves)
    return curves


def sample(arm, name, frames, pose_at, loop):
    """Keys pose_at(t, pose) for t in 0..1 over `frames` frames (the last key equals the first when looping)."""
    action = new_action(arm, name)
    p = Pose(arm)
    for f in range(frames + 1):
        t = f / frames
        p.clear()
        pose_at(0.0 if (loop and f == frames) else t, p)
        p.key(f + 1)
    action.frame_range = (1, frames + 1)
    for fc in action_fcurves(action):
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR"   # sampled every frame already
    return action


def rest_pose(arm):
    arm.animation_data_create()
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = Quaternion()
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)


def export_fbx(path, arm, meshes):
    """The armature, its meshes and every action (one clip each), for Unity: -Z forward, Y up, scale 1."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    rest_pose(arm)
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    for m in meshes:
        m.hide_set(False)
        m.hide_render = False
        m.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
        bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
        mesh_smooth_type="FACE", use_mesh_modifiers=False, path_mode="STRIP",
        colors_type="LINEAR", use_custom_props=False)
    return path


def smoothstep(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def ease(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def lerp(a, b, t):
    return a + (b - a) * t
