"""Dino's skeleton, skin weights, animations and FBX export (run inside Blender 5.1 after dino.build()).

    rig()        joins the parts into one mesh "Dino", adds the armature "DinoRig" and weights
    animate()    Idle, Run, Jump, Slide, Hit, Cheer (30 fps; loops start and end on the same pose)
    export()     Assets/Games/Runner/Art/Dino.fbx (mesh + armature + every action as its own clip)

Weights: capsule "envelopes" per bone (distance outside the bone's radius), legs and arms only on their own side.
Small separate parts (eyes, spikes, claws, teeth) follow one bone rigidly.
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "tools"))
import tundu_art as ta  # noqa: E402

FPS = 30

# name: (head, tail, parent, radius). Positions in metres, dino facing -Y.
BONES = {
    "Root": ((0, 0, 0), (0, 0.25, 0), None, 0.0),
    "Hips": ((0, 0.10, 0.70), (0, -0.05, 0.95), "Root", 0.30),
    "Chest": ((0, -0.05, 0.95), (0, -0.25, 1.12), "Hips", 0.28),
    "Neck": ((0, -0.25, 1.12), (0, -0.32, 1.28), "Chest", 0.19),
    "Head": ((0, -0.30, 1.30), (0, -0.84, 1.38), "Neck", 0.27),
    "Tail1": ((0, 0.28, 0.74), (0, 0.52, 0.70), "Hips", 0.20),
    "Tail2": ((0, 0.52, 0.70), (0, 0.78, 0.65), "Tail1", 0.15),
    "Tail3": ((0, 0.78, 0.65), (0, 1.03, 0.63), "Tail2", 0.10),
    "Tail4": ((0, 1.03, 0.63), (0, 1.30, 0.64), "Tail3", 0.06),
}
for side, s in (("L", 1), ("R", -1)):
    BONES.update({
        f"Thigh.{side}": ((0.21 * s, 0.08, 0.78), (0.22 * s, -0.02, 0.42), "Hips", 0.15),
        f"Shin.{side}": ((0.22 * s, -0.02, 0.42), (0.22 * s, 0.10, 0.13), f"Thigh.{side}", 0.10),
        f"Foot.{side}": ((0.22 * s, 0.10, 0.13), (0.22 * s, -0.25, 0.06), f"Shin.{side}", 0.09),
        f"Arm.{side}": ((0.21 * s, -0.30, 0.99), (0.27 * s, -0.42, 0.88), "Chest", 0.065),
        f"Hand.{side}": ((0.27 * s, -0.42, 0.88), (0.28 * s, -0.53, 0.86), f"Arm.{side}", 0.055),
    })


def bone_side(name):
    return 1 if name.endswith(".L") else -1 if name.endswith(".R") else 0


def segment_distance(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return (p - (a + ab * t)).length


def smoothstep(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def envelope_weights(p):
    """Weights {bone: w} for a point, from the distance outside each bone's capsule."""
    raw = {}
    for name, (h, t, _, r) in BONES.items():
        if r <= 0:
            continue
        side = bone_side(name)
        if side:
            limb = name.split(".")[0]
            lo, hi = (0.12, 0.20) if limb in ("Arm", "Hand") else (0.05, 0.15)
            k = smoothstep(lo, hi, p.x * side)
            if k <= 0:
                continue
        else:
            k = 1.0
        d = max(0.0, segment_distance(p, Vector(h), Vector(t)) - r)
        raw[name] = k / (d + 0.025) ** 3
    top = sorted(raw.items(), key=lambda kv: -kv[1])[:3]
    total = sum(w for _, w in top) or 1.0
    return {n: w / total for n, w in top}


# ---------------------------------------------------------------------------------------------------- rig

def rig():
    coll = ta.collection("Dino")
    parts = [bpy.data.objects[n] for n in ("DinoBody", "DinoBelly", "DinoDetails")]
    shells = {"DinoBody": "smooth", "DinoBelly": "smooth", "DinoDetails": "rigid"}

    # armature
    arm_data = bpy.data.armatures.new("DinoRig")
    arm = bpy.data.objects.new("DinoRig", arm_data)
    coll.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for name, (h, t, parent, _) in BONES.items():
        eb = arm_data.edit_bones.new(name)
        eb.head, eb.tail = h, t
        # roll: bone Z axis toward world up where possible, so "pitch" is the same axis for every bone
        direction = (Vector(t) - Vector(h)).normalized()
        eb.align_roll(Vector((0, 0, 1)) if abs(direction.z) < 0.9 else Vector((0, -1, 0)))
        if parent:
            eb.parent = arm_data.edit_bones[parent]
            eb.use_connect = (Vector(arm_data.edit_bones[parent].tail) - Vector(h)).length < 1e-4
        eb.use_deform = name != "Root"
    bpy.ops.object.mode_set(mode="OBJECT")
    arm_data.display_type = "STICK"

    # one mesh, vertex groups per bone
    for o in parts:
        o.data.materials.clear()
    me_parts = []
    for o in parts:
        me_parts.append((o, shells[o.name]))
    groups_per_vertex = []
    for o, mode in me_parts:
        mw = o.matrix_world
        if mode == "smooth":
            for v in o.data.vertices:
                groups_per_vertex.append(envelope_weights(mw @ v.co))
        else:
            # rigid: every connected shell follows the bone with the largest total weight
            islands = connected_islands(o.data)
            vw = [None] * len(o.data.vertices)
            for island in islands:
                totals = {}
                for vi in island:
                    for n, w in envelope_weights(mw @ o.data.vertices[vi].co).items():
                        totals[n] = totals.get(n, 0) + w
                best = max(totals, key=totals.get)
                if best in ("Neck", "Chest") and any(o.data.vertices[vi].co.z > 1.25 for vi in island):
                    best = "Head" if (mw @ o.data.vertices[island[0]].co).y < -0.2 else best
                for vi in island:
                    vw[vi] = {best: 1.0}
            groups_per_vertex.extend(vw)

    bpy.ops.object.select_all(action="DESELECT")
    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    dino = parts[0]
    dino.name = dino.data.name = "Dino"
    dino.data.materials.clear()
    dino.data.materials.append(ta.palette_material())
    for name in BONES:
        if name != "Root":
            dino.vertex_groups.new(name=name)
    for vi, weights in enumerate(groups_per_vertex):
        for n, w in weights.items():
            dino.vertex_groups[n].add([vi], w, "REPLACE")
    dino.parent = arm
    mod = dino.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    return {"verts": len(dino.data.vertices), "tris": ta.tri_count(dino), "bones": len(BONES)}


def connected_islands(me):
    parent = list(range(len(me.vertices)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for e in me.edges:
        a, b = find(e.vertices[0]), find(e.vertices[1])
        if a != b:
            parent[a] = b
    islands = {}
    for i in range(len(me.vertices)):
        islands.setdefault(find(i), []).append(i)
    return list(islands.values())


# ---------------------------------------------------------------------------------------------------- animation

class Pose:
    """Sets pose-bone rotations with axes given in armature space (X = dino's left-right, i.e. pitch)."""

    def __init__(self, arm):
        self.arm = arm
        self.rest = {pb.name: pb.bone.matrix_local.to_3x3() for pb in arm.pose.bones}

    def clear(self):
        for pb in self.arm.pose.bones:
            pb.rotation_mode = "QUATERNION"
            pb.rotation_quaternion = Quaternion()
            pb.location = (0, 0, 0)

    def rot(self, bone, pitch=0.0, yaw=0.0, roll=0.0):
        """Degrees, armature axes. pitch: around X (+ = top goes forward), yaw: around Z, roll: around Y."""
        r = (Matrix.Rotation(math.radians(yaw), 3, "Z") @ Matrix.Rotation(math.radians(pitch), 3, "X")
             @ Matrix.Rotation(math.radians(roll), 3, "Y"))
        m = self.rest[bone]
        self.arm.pose.bones[bone].rotation_quaternion = (m.inverted() @ r @ m).to_quaternion()

    def move(self, bone, x=0.0, y=0.0, z=0.0):
        m = self.rest[bone]
        self.arm.pose.bones[bone].location = m.inverted() @ Vector((x, y, z))

    def key(self, frame):
        for pb in self.arm.pose.bones:
            pb.keyframe_insert("rotation_quaternion", frame=frame)
            pb.keyframe_insert("location", frame=frame)


def new_action(arm, name):
    old = bpy.data.actions.get(name)
    if old:
        bpy.data.actions.remove(old)
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = action
    return action


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


S = math.sin
C = math.cos
TAU = 2 * math.pi

# Pitch sign (rotation around the armature's X axis, the dino faces -Y): + = "top goes forward".
# So: + leans the body forward, swings a leg BACK, lifts the tail tip, lowers the snout; - raises an arm forward.


def legs(p, phase, stride=34.0, lift=55.0):
    """Running legs: phase 0..1, left leg forward at 0."""
    for side, off in (("L", 0.0), ("R", 0.5)):
        a = TAU * (phase + off)
        swing = S(a)                        # +1 = leg forward
        recover = max(0.0, C(a))            # foot lifted on the way forward
        p.rot(f"Thigh.{side}", pitch=-stride * swing - 6)
        p.rot(f"Shin.{side}", pitch=12 + lift * recover)
        p.rot(f"Foot.{side}", pitch=stride * swing * 0.6 - lift * recover * 0.7)


def idle(t, p):
    a = TAU * t
    p.move("Hips", z=0.012 * S(2 * a))
    p.rot("Chest", pitch=2 * S(a))
    p.rot("Head", pitch=3 * S(a + 0.6), roll=4 * S(a))
    for i, b in enumerate(("Tail1", "Tail2", "Tail3", "Tail4")):
        p.rot(b, yaw=(6 + 3 * i) * S(a - 0.5 * i), pitch=2 * S(a))
    for side in ("L", "R"):
        p.rot(f"Arm.{side}", pitch=8 * S(a + 1.0))


def run(t, p):
    a = TAU * t
    legs(p, t)
    p.move("Hips", z=0.06 * abs(S(a)) - 0.03)                 # two bounces per stride
    p.rot("Hips", pitch=10, yaw=6 * S(a))                     # lean into the run, hips twist with the legs
    p.rot("Chest", yaw=-8 * S(a))
    p.rot("Neck", pitch=-8)
    p.rot("Head", pitch=-4 + 4 * S(2 * a + 0.8), yaw=3 * S(a))
    for i, b in enumerate(("Tail1", "Tail2", "Tail3", "Tail4")):
        p.rot(b, yaw=(10 + 4 * i) * S(a - 0.7 * (i + 1)), pitch=-6 + 3 * C(2 * a - 0.5 * i))
    for side in ("L", "R"):
        p.rot(f"Arm.{side}", pitch=30 * S(a + (0 if side == "L" else math.pi)) - 10)
        p.rot(f"Hand.{side}", pitch=-20)


def jump(t, p):
    # tuck up fast, hold, open up again for the landing; arms up, tail up, snout up
    up = min(1.0, t / 0.2) if t < 0.7 else max(0.0, 1 - (t - 0.7) / 0.3)
    up = up * up * (3 - 2 * up)
    for side in ("L", "R"):
        p.rot(f"Thigh.{side}", pitch=-6 - 55 * up)
        p.rot(f"Shin.{side}", pitch=12 + 75 * up)
        p.rot(f"Foot.{side}", pitch=-25 * up)
        p.rot(f"Arm.{side}", pitch=-10 - 85 * up)
        p.rot(f"Hand.{side}", pitch=-20 * up)
    p.rot("Hips", pitch=6 - 10 * up)
    p.rot("Head", pitch=-10 * up)
    for i, b in enumerate(("Tail1", "Tail2", "Tail3", "Tail4")):
        p.rot(b, pitch=(8 + 5 * i) * up)


def slide(t, p):
    # belly slide: body flat, legs stretched back, arms forward, head up looking ahead; a little wiggle
    a = TAU * t
    p.move("Hips", z=-0.40, y=0.10)
    p.rot("Hips", pitch=62, roll=4 * S(a))
    p.rot("Neck", pitch=-38)
    p.rot("Head", pitch=-28, yaw=4 * S(a))
    for side in ("L", "R"):
        p.rot(f"Thigh.{side}", pitch=22 + 4 * S(a + (0 if side == "L" else math.pi)))
        p.rot(f"Shin.{side}", pitch=0)
        p.rot(f"Foot.{side}", pitch=35)
        p.rot(f"Arm.{side}", pitch=-55)
        p.rot(f"Hand.{side}", pitch=-10)
    for i, b in enumerate(("Tail1", "Tail2", "Tail3", "Tail4")):
        p.rot(b, pitch=(-52 if i == 0 else 4), yaw=(6 + 2 * i) * S(a - 0.5 * i))


def hit(t, p):
    # bonk: recoil backwards, arms up, then a dizzy sway
    k = min(1.0, t / 0.25)
    k = k * k * (3 - 2 * k)
    sway = S(TAU * 2 * t) * min(1.0, t * 3)
    p.rot("Hips", pitch=-16 * k)
    p.rot("Chest", pitch=-8 * k)
    p.rot("Neck", pitch=-8 * k, roll=10 * sway)
    p.rot("Head", pitch=-12 * k, roll=14 * sway)
    for side in ("L", "R"):
        p.rot(f"Arm.{side}", pitch=-95 * k)
        p.rot(f"Thigh.{side}", pitch=-14 * k)
        p.rot(f"Shin.{side}", pitch=10 * k)
    for i, b in enumerate(("Tail1", "Tail2", "Tail3", "Tail4")):
        p.rot(b, pitch=14 * k, yaw=8 * sway)


def cheer(t, p):
    # arms up (the Tundu "start" gesture), little hops, tail wag
    a = TAU * t
    hop = max(0.0, S(2 * a))
    p.move("Hips", z=0.10 * hop - 0.02)
    p.rot("Head", pitch=-10, roll=6 * S(a))
    for side, s in (("L", 1), ("R", -1)):
        p.rot(f"Arm.{side}", pitch=-120 + 15 * S(2 * a), yaw=15 * s)
        p.rot(f"Hand.{side}", pitch=-20)
        p.rot(f"Thigh.{side}", pitch=-6 - 15 * hop)
        p.rot(f"Shin.{side}", pitch=12 + 25 * hop)
        p.rot(f"Foot.{side}", pitch=-10 * hop)
    for i, b in enumerate(("Tail1", "Tail2", "Tail3", "Tail4")):
        p.rot(b, yaw=(12 + 5 * i) * S(2 * a - 0.6 * i), pitch=6)


CLIPS = [  # name, frames at 30 fps, function, loops
    ("Idle", 60, idle, True),
    ("Run", 16, run, True),
    ("Jump", 24, jump, False),
    ("Slide", 30, slide, True),
    ("Hit", 36, hit, False),
    ("Cheer", 30, cheer, True),
]


def animate():
    arm = bpy.data.objects["DinoRig"]
    scene = bpy.context.scene
    scene.render.fps = FPS
    made = []
    for name, frames, fn, loop in CLIPS:
        sample(arm, name, frames, fn, loop)
        made.append(name)
    arm.animation_data.action = bpy.data.actions["Run"]
    scene.frame_start, scene.frame_end = 1, 17
    return made


def show(action_name, frame):
    arm = bpy.data.objects["DinoRig"]
    arm.animation_data.action = bpy.data.actions[action_name]
    bpy.context.scene.frame_set(frame)


# ---------------------------------------------------------------------------------------------------- export

def export(path=None):
    path = path or os.path.join(ta.UNITY_ART, "Dino.fbx")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    arm = bpy.data.objects["DinoRig"]
    dino = bpy.data.objects["Dino"]
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.rotation_quaternion = Quaternion()
        pb.location = (0, 0, 0)
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    dino.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
        bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
        mesh_smooth_type="FACE", use_mesh_modifiers=False, path_mode="STRIP")
    return path
