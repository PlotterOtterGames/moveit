"""Dino's skeleton, weights, clips and export (Blender 5.1, headless, after dino.build(); build.py --dino).

    rig()       the armature "DinoRig" and the mesh's weights: the body weighted smoothly (capsule envelopes, legs and
                arms on their own side, then smoothed over the mesh), the details following one bone each
    animate()   the clips at 30 fps (loops start and end on the same pose)
    export()    Assets/Games/Runner/Art/Dino.fbx (mesh + armature + every clip)
    preview()   renders into art/runner/previews/: dino_front/back/side/three, a sheet of poses

Bones: Root, Hips, Spine, Neck, Head, Tail1-5, Thigh/Shin/Foot.L/R, UpperArm/Hand.L/R, the face (Eye, EyeHappy .L/.R,
Mouth, MouthOpen: scaled to show or hide), the scarf's tails (ScarfL1-3, ScarfR1-3: the game swings them in the wind).

Clips: Idle, LookAround, Wave, Ready, Run, Jump, Flip, Slide, Hit, Dizzy, Cheer, Dance.
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "tools"))
import tundu_art as ta  # noqa: E402
import tundu_rig as tr  # noqa: E402
import kit  # noqa: E402
import dino  # noqa: E402

UNITY_ART = os.path.join(ta.REPO, "unity", "MoveItGames", "Assets", "Games", "Runner", "Art")
FPS = 30


def _eye(side):
    c = dino.on_ellipsoid(dino.HEAD_C, dino.HEAD_R, (0.5 * side, -0.76, 0.42))
    return c - (c - dino.HEAD_C).normalized() * 0.045


M0 = dino.MOUTH

# name: (head, tail, parent, envelope radius: 0 = no smooth weights)
BONES = {
    "Root": ((0, 0, 0), (0, 0.25, 0), None, 0.0),
    "Hips": ((0, 0.08, 0.72), (0, 0.0, 0.96), "Root", 0.36),
    "Spine": ((0, 0.0, 0.96), (0, -0.13, 1.1), "Hips", 0.3),
    "Neck": ((0, -0.13, 1.1), (0, -0.23, 1.29), "Spine", 0.21),
    "Head": ((0, -0.23, 1.29), (0, -0.88, 1.4), "Neck", 0.38),
    "Tail1": ((0, 0.3, 0.76), (0, 0.56, 0.69), "Hips", 0.22),
    "Tail2": ((0, 0.56, 0.69), (0, 0.84, 0.61), "Tail1", 0.17),
    "Tail3": ((0, 0.84, 0.61), (0, 1.1, 0.58), "Tail2", 0.12),
    "Tail4": ((0, 1.1, 0.58), (0, 1.34, 0.61), "Tail3", 0.08),
    "Tail5": ((0, 1.34, 0.61), (0, 1.58, 0.68), "Tail4", 0.05),
    "Mouth": (tuple(M0), tuple(M0 + Vector((0, -0.06, 0))), "Head", 0.0),
    "MouthOpen": (tuple(M0 + Vector((0, 0.02, -0.01))), tuple(M0 + Vector((0, -0.04, -0.01))), "Head", 0.0),
}
for side, s in (("L", 1), ("R", -1)):
    e = _eye(s)
    BONES.update({
        f"Thigh.{side}": ((0.25 * s, 0.06, 0.76), (0.25 * s, -0.02, 0.44), "Hips", 0.19),
        f"Shin.{side}": ((0.25 * s, -0.02, 0.44), (0.25 * s, 0.1, 0.16), f"Thigh.{side}", 0.13),
        f"Foot.{side}": ((0.25 * s, 0.1, 0.16), (0.25 * s, -0.26, 0.07), f"Shin.{side}", 0.11),
        f"UpperArm.{side}": ((0.22 * s, -0.2, 1.1), (0.33 * s, -0.33, 1.0), "Spine", 0.085),
        f"Hand.{side}": ((0.33 * s, -0.33, 1.0), (0.36 * s, -0.55, 0.94), f"UpperArm.{side}", 0.08),
        f"Eye.{side}": (tuple(e), tuple(e + Vector((0, -0.08, 0))), "Head", 0.0),
        f"EyeHappy.{side}": (tuple(e + Vector((0, -0.02, 0))), tuple(e + Vector((0, -0.1, 0))), "Head", 0.0),
    })


def scarf_bones():
    """The scarf's tails' bones, along the tails dino.build() made (run after it)."""
    for side, s in (("L", 1), ("R", -1)):
        a, b = dino.SCARF[s]
        pts = [a.lerp(b, k / 3) for k in range(4)]
        for k in range(3):
            BONES[f"Scarf{side}{k + 1}"] = (tuple(pts[k]), tuple(pts[k + 1]), "Neck" if k == 0 else f"Scarf{side}{k}", 0.0)


def bone_side(name):
    return 1 if name.endswith(".L") else -1 if name.endswith(".R") else 0


def segment_distance(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return (p - (a + ab * t)).length


def envelope_weights(p):
    """Weights {bone: w} for a body point, from the distance outside each bone's capsule."""
    raw = {}
    for name, (h, t, _, r) in BONES.items():
        if r <= 0:
            continue
        side = bone_side(name)
        if side:
            limb = name.split(".")[0]
            lo, hi = (0.2, 0.25) if limb in ("UpperArm", "Hand") else (0.04, 0.14)
            k = tr.smoothstep(lo, hi, p.x * side)
            if k <= 0:
                continue
        else:
            k = 1.0
        d = max(0.0, segment_distance(p, Vector(h), Vector(t)) - r)
        raw[name] = k / (d + 0.03) ** 3
    top = sorted(raw.items(), key=lambda kv: -kv[1])[:4]
    total = sum(w for _, w in top) or 1.0
    return {n: w / total for n, w in top}


def scarf_weights(p):
    """A scarf tail's point: its chain on its side, by how far along the tail it is."""
    side = "L" if p.x > 0 else "R"
    best, bd = None, 1e9
    for k in range(3):
        h, t, _, _ = BONES[f"Scarf{side}{k + 1}"]
        d = segment_distance(p, Vector(h), Vector(t))
        if d < bd:
            best, bd = k, d
    out = {f"Scarf{side}{best + 1}": 0.75}
    if best > 0:
        out[f"Scarf{side}{best}"] = 0.25
    else:
        out["Neck"] = 0.25
    return out


def rig():
    scarf_bones()
    coll = ta.collection("Dino")
    mesh = bpy.data.objects["Dino"]
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
        direction = (Vector(t) - Vector(h)).normalized()
        eb.align_roll(Vector((0, 0, 1)) if abs(direction.z) < 0.9 else Vector((0, -1, 0)))
        if parent:
            eb.parent = arm_data.edit_bones[parent]
            eb.use_connect = (Vector(arm_data.edit_bones[parent].tail) - Vector(h)).length < 1e-4
        eb.use_deform = name != "Root"
    bpy.ops.object.mode_set(mode="OBJECT")
    arm_data.display_type = "STICK"

    me = mesh.data
    parts = dino.DATA["vertex_parts"]
    bones_of = dino.DATA["part_bones"]
    weights = []
    for v in me.vertices:
        bone = bones_of[parts[v.index] - 1]
        if bone == "smooth":
            weights.append(envelope_weights(v.co))
        elif bone == "scarf":
            weights.append(scarf_weights(v.co))
        else:
            weights.append({bone: 1.0})
    # smooth the body's weights over its edges (no seams where two bones meet)
    smooth = [bones_of[parts[v.index] - 1] == "smooth" for v in me.vertices]
    nbrs = [[] for _ in me.vertices]
    for e in me.edges:
        a, b = e.vertices
        if smooth[a] and smooth[b]:
            nbrs[a].append(b)
            nbrs[b].append(a)
    for _ in range(3):
        new = []
        for i, w in enumerate(weights):
            if not smooth[i] or not nbrs[i]:
                new.append(w)
                continue
            acc = dict((k, v * 0.5) for k, v in w.items())
            share = 0.5 / len(nbrs[i])
            for j in nbrs[i]:
                for k, v in weights[j].items():
                    acc[k] = acc.get(k, 0.0) + v * share
            top = sorted(acc.items(), key=lambda kv: -kv[1])[:4]
            tot = sum(v for _, v in top) or 1.0
            new.append({k: v / tot for k, v in top})
        weights = new
    for name in BONES:
        if name != "Root":
            mesh.vertex_groups.new(name=name)
    for vi, w in enumerate(weights):
        for n, x in w.items():
            mesh.vertex_groups[n].add([vi], x, "REPLACE")
    mesh.parent = arm
    mod = mesh.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    return {"verts": len(me.vertices), "tris": ta.tri_count(mesh), "bones": len(BONES)}


# ---------------------------------------------------------------------------------------------------- clips

S, C, TAU = math.sin, math.cos, 2 * math.pi
TAIL = ("Tail1", "Tail2", "Tail3", "Tail4", "Tail5")
ease = tr.ease
lerp = tr.lerp


def face(p, eyes=1.0, happy=0.0, mouth=1.0, open_=0.0):
    for side in ("L", "R"):
        p.squash(f"Eye.{side}", max(0.001, eyes))
        p.squash(f"EyeHappy.{side}", max(0.001, happy))
    p.squash("Mouth", max(0.001, mouth))
    p.squash("MouthOpen", max(0.001, open_))


def arm(p, side, pitch, splay=0.0, hand=-25.0):
    """An arm: pitch swings it about the shoulder (- = forward and up: -90 is straight up, the arms rest pointing
    forward, down and a little out), splay tilts it out to its side afterwards (the wave, a V of cheering arms)."""
    s = 1 if side == "L" else -1
    r = Matrix.Rotation(math.radians(splay * s), 3, "Y") @ Matrix.Rotation(math.radians(pitch), 3, "X")
    p.rot_m(f"UpperArm.{side}", r)
    p.rot(f"Hand.{side}", pitch=hand)


def tail_sway(p, a, yaw=8.0, grow=3.0, lag=0.55, pitch=0.0, bob=0.0):
    for i, b in enumerate(TAIL):
        p.rot(b, yaw=(yaw + grow * i) * S(a - lag * (i + 1)), pitch=pitch + bob * C(2 * a - 0.5 * i))


def idle(t, p):
    a = TAU * t
    face(p)
    p.move("Hips", z=0.012 * S(2 * a))
    p.rot("Hips", roll=2.0 * S(a))
    p.rot("Spine", pitch=-2 + 2 * S(2 * a + 0.5), roll=-1.5 * S(a))
    p.rot("Neck", pitch=-3)
    p.rot("Head", pitch=2 * S(2 * a + 1.0), roll=4 * S(a + 0.4))
    for side in ("L", "R"):
        arm(p, side, 8 + 6 * S(2 * a + 1.2), -4)
    tail_sway(p, a, 9.0, 4.0, 0.6, pitch=-2.0)


def look_around(t, p):
    a = TAU * t
    face(p)
    p.move("Hips", z=0.01 * S(2 * a))
    turn = 32 * S(a) * (0.6 + 0.4 * abs(S(a)))
    p.rot("Spine", yaw=turn * 0.25, pitch=-2)
    p.rot("Neck", yaw=turn * 0.35, pitch=-4)
    p.rot("Head", yaw=turn * 0.45, pitch=-4 + 3 * S(2 * a), roll=-turn * 0.12)
    for side in ("L", "R"):
        arm(p, side, 8, -4)
    tail_sway(p, a + 1.0, 12.0, 4.0, 0.6, pitch=-2.0)


def wave(t, p):
    """Hello! The right arm waves over the head, the head tilts to it, a little bounce."""
    a = TAU * t
    up = ease(min(1.0, t / 0.15)) * ease(min(1.0, (1 - t) / 0.15))
    face(p, eyes=1.0, mouth=1.0 - up * 0.0, open_=0.0)
    p.move("Hips", z=0.02 * abs(S(2 * a)) * up)
    p.rot("Hips", roll=-3 * up)
    p.rot("Spine", roll=4 * up, pitch=-3)
    p.rot("Head", roll=-10 * up + 3 * S(2 * a), pitch=-6 * up)
    arm(p, "R", lerp(8, -112, up), (18 + 28 * S(3 * a)) * up, hand=-15 - 20 * S(3 * a + 0.7) * up)
    arm(p, "L", 8, -4)
    tail_sway(p, 2 * a, 14.0, 4.0, 0.6, pitch=4.0 * up)


def ready(t, p):
    """On your marks: crouched, leaning forward, bouncing on its toes."""
    a = TAU * t
    face(p)
    b = 0.012 * S(2 * a)
    p.move("Hips", z=-0.1 + b, y=0.02)
    p.rot("Hips", pitch=20)
    p.rot("Spine", pitch=6)
    p.rot("Neck", pitch=-18)
    p.rot("Head", pitch=-10 + 2 * S(2 * a))
    for side, s, off in (("L", 1, 0.0), ("R", -1, 0.6)):
        p.rot(f"Thigh.{side}", pitch=-44 - 6 * off)
        p.rot(f"Shin.{side}", pitch=62)
        p.rot(f"Foot.{side}", pitch=-38 + 6 * off)
        arm(p, side, 40, 6, hand=-30)
    tail_sway(p, a, 4.0, 2.0, 0.6, pitch=12.0)


def legs_run(p, t, stride=42.0, knee=112.0):
    for side, off in (("L", 0.0), ("R", 0.5)):
        s = (t + off) % 1.0
        swing = max(0.0, S(TAU * (s - 0.5))) if s > 0.5 else 0.0
        thigh = -stride * C(TAU * s) - 22 * swing
        shin = 16 + knee * swing ** 0.9 + 10 * max(0.0, S(TAU * s)) * (1 if s < 0.5 else 0)
        foot = -thigh * 0.55 - shin * 0.55 + 18 + 25 * swing
        p.rot(f"Thigh.{side}", pitch=thigh)
        p.rot(f"Shin.{side}", pitch=shin)
        p.rot(f"Foot.{side}", pitch=foot)


def run(t, p):
    a = TAU * t
    face(p)
    legs_run(p, t)
    bob = 0.5 - 0.5 * C(2 * TAU * (t - 0.1))
    p.move("Hips", z=-0.045 + 0.075 * bob)
    p.rot("Hips", pitch=13 + 2 * C(2 * a), yaw=9 * S(a), roll=3 * S(a))
    p.rot("Spine", yaw=-12 * S(a), pitch=2)
    p.rot("Neck", pitch=-10 - 3 * C(2 * a), yaw=4 * S(a))
    p.rot("Head", pitch=-5 + 4 * C(2 * a + 0.6), yaw=-4 * S(a))
    for side, off in (("L", 0.0), ("R", math.pi)):
        arm(p, side, -6 + 46 * C(a + off), 4, hand=-40)
    for i, b in enumerate(TAIL):
        p.rot(b, yaw=(10 + 5 * i) * S(a - 0.7 * (i + 1)), pitch=-5 + 5 * C(2 * a - 0.6 * (i + 1)))


def jump(t, p):
    """Takeoff stretch, a tuck at the top (knees up, arms up, tail up, mouth open), opening for the landing."""
    up = ease(t / 0.18) if t < 0.18 else 1.0 if t < 0.68 else 1.0 - ease((t - 0.68) / 0.32)
    tuck = ease((t - 0.12) / 0.2) if t < 0.32 else 1.0 if t < 0.62 else 1.0 - ease((t - 0.62) / 0.25)
    face(p, mouth=1 - up, open_=up)
    p.rot("Hips", pitch=6 - 12 * up)
    p.rot("Spine", pitch=-6 * up)
    p.rot("Neck", pitch=-10 * up)
    p.rot("Head", pitch=-12 * up)
    for side, s in (("L", 1), ("R", -1)):
        p.rot(f"Thigh.{side}", pitch=lerp(14, -78, tuck) + (8 if side == "R" else -4) * tuck)
        p.rot(f"Shin.{side}", pitch=lerp(10, 112, tuck))
        p.rot(f"Foot.{side}", pitch=lerp(30, -30, tuck))
        arm(p, side, -10 - 85 * up, 22 * up)
    for i, b in enumerate(TAIL):
        p.rot(b, pitch=(6 + 6 * i) * up - 4 * (1 - up), yaw=4 * S(TAU * t * 2 - i))


def flip(t, p):
    """A front flip at the top of the jump (a trick, now and then)."""
    jump(t, p)
    spin = ease((t - 0.14) / 0.6) if t > 0.14 else 0.0
    if t > 0.74:
        spin = 1.0
    p.rot("Hips", pitch=6 + 360 * spin)
    for side, s in (("L", 1), ("R", -1)):
        k = ease((t - 0.12) / 0.15) if t < 0.27 else 1.0 if t < 0.7 else 1.0 - ease((t - 0.7) / 0.2)
        p.rot(f"Thigh.{side}", pitch=lerp(14, -95, k))
        p.rot(f"Shin.{side}", pitch=lerp(10, 125, k))
        arm(p, side, lerp(-20, -60, k), 25 * k)
    face(p, eyes=0.001 if 0.3 < t < 0.6 else 1.0, happy=1.0 if 0.3 < t < 0.6 else 0.0, mouth=0.0, open_=1.0)


def slide(t, p):
    """A belly slide: body down and forward, legs stretched back, arms ahead, head up watching the way."""
    a = TAU * t
    face(p, mouth=0.0, open_=1.0)
    p.move("Hips", z=-0.4, y=-0.05)
    p.rot("Hips", pitch=72, roll=4 * S(a))
    p.rot("Spine", pitch=4)
    p.rot("Neck", pitch=-46)
    p.rot("Head", pitch=-30, yaw=5 * S(a))
    for side, s, off in (("L", 1, 0.0), ("R", -1, math.pi)):
        p.rot(f"Thigh.{side}", pitch=-8 + 5 * S(a + off), roll=4 * s)
        p.rot(f"Shin.{side}", pitch=8)
        p.rot(f"Foot.{side}", pitch=40)
        arm(p, side, -58 + 6 * S(2 * a + off), 12, hand=-10)
    for i, b in enumerate(TAIL):
        p.rot(b, pitch=(-55 if i == 0 else 5 + 2 * i), yaw=(6 + 3 * i) * S(a - 0.5 * i))


SIT_Z = -0.42


def sitting(p, k, lean=-28.0):
    """Sat down hard: on its bottom, legs out in front, knees up a little."""
    p.move("Hips", z=SIT_Z * k, y=0.08 * k)
    p.rot("Hips", pitch=lean * k)
    for side, s in (("L", 1), ("R", -1)):
        p.rot(f"Thigh.{side}", pitch=-82 * k, roll=6 * s * k)
        p.rot(f"Shin.{side}", pitch=42 * k)
        p.rot(f"Foot.{side}", pitch=-10 * k)


def hit(t, p):
    """Bonk: knocked back, arms flung up, sits down hard, then sways dazed."""
    k1 = ease(t / 0.12)
    k2 = ease((t - 0.08) / 0.3)
    sway = S(TAU * 2.0 * t) * ease((t - 0.35) / 0.3)
    face(p, mouth=0.0, open_=1.0)
    sitting(p, k2)
    p.rot("Hips", pitch=-28 * k2 - 18 * k1 * (1 - k2))
    p.rot("Spine", pitch=-10 * k1 * (1 - k2) + 6 * k2, roll=5 * sway)
    p.rot("Neck", pitch=-22 * k1 * (1 - k2) + 14 * k2, roll=10 * sway)
    p.rot("Head", pitch=-16 * k1 * (1 - k2) + 6 * k2, roll=14 * sway)
    for side in ("L", "R"):
        up = k1 * (1 - k2 * 0.85)
        arm(p, side, -100 * up + 20 * k2, 30 * up + 10 * k2)
    for i, b in enumerate(TAIL):
        p.rot(b, pitch=12 * k1 * (1 - k2) - 10 * k2, yaw=8 * sway)


def dizzy(t, p):
    """Still sat, the head going round and round."""
    a = TAU * t
    face(p, mouth=1.0)
    sitting(p, 1.0)
    p.rot("Hips", pitch=-28 + 3 * S(a))
    p.rot("Spine", pitch=6, roll=6 * S(a))
    p.rot("Neck", pitch=14 + 6 * C(a), roll=10 * S(a))
    p.rot("Head", pitch=6 + 6 * C(a), roll=14 * S(a))
    for side, s in (("L", 1), ("R", -1)):
        arm(p, side, 18, 14 + 4 * S(a + (0 if s > 0 else 1.5)))
    for i, b in enumerate(TAIL):
        p.rot(b, pitch=-10, yaw=6 * S(a - 0.5 * i))


def cheer(t, p):
    """Hooray: hops with both arms up, happy eyes, the tail wagging."""
    a = TAU * t
    hop = max(0.0, S(2 * a))
    face(p, eyes=0.001, happy=1.0, mouth=0.0, open_=1.0)
    p.move("Hips", z=0.13 * hop - 0.03)
    p.rot("Hips", pitch=-4)
    p.rot("Head", pitch=-12, roll=6 * S(a))
    for side, s in (("L", 1), ("R", -1)):
        arm(p, side, -100 + 12 * S(2 * a), 20 + 10 * S(2 * a), hand=-15)
        p.rot(f"Thigh.{side}", pitch=-6 - 18 * hop)
        p.rot(f"Shin.{side}", pitch=12 + 30 * hop)
        p.rot(f"Foot.{side}", pitch=-8 * hop)
    for i, b in enumerate(TAIL):
        p.rot(b, yaw=(14 + 5 * i) * S(2 * a - 0.6 * i), pitch=8)


def dance(t, p):
    """A little victory dance: side steps, hips wiggling, arms pumping in turn, head bobbing."""
    a = TAU * t
    face(p, eyes=0.001, happy=1.0, mouth=0.0, open_=1.0)
    p.move("Hips", x=0.06 * S(a), z=-0.04 + 0.04 * abs(S(2 * a)))
    p.rot("Hips", roll=8 * S(a), yaw=10 * S(a))
    p.rot("Spine", roll=-10 * S(a), yaw=-8 * S(a))
    p.rot("Head", roll=8 * S(a + 0.5), pitch=-6 + 6 * abs(S(2 * a)))
    for side, s, off in (("L", 1, 0.0), ("R", -1, math.pi)):
        arm(p, side, -35 - 55 * S(a + off), 15 + 8 * S(2 * a), hand=-30)
        step = max(0.0, S(a + off))
        p.rot(f"Thigh.{side}", pitch=-20 * step)
        p.rot(f"Shin.{side}", pitch=35 * step)
        p.rot(f"Foot.{side}", pitch=-10 * step)
    for i, b in enumerate(TAIL):
        p.rot(b, yaw=(16 + 5 * i) * S(a * 2 - 0.6 * i), pitch=6)


CLIPS = [  # name, frames at 30 fps, function, loops
    ("Idle", 60, idle, True),
    ("LookAround", 90, look_around, True),
    ("Wave", 48, wave, True),
    ("Ready", 30, ready, True),
    ("Run", 16, run, True),
    ("Jump", 24, jump, False),
    ("Flip", 24, flip, False),
    ("Slide", 24, slide, True),
    ("Hit", 36, hit, False),
    ("Dizzy", 40, dizzy, True),
    ("Cheer", 30, cheer, True),
    ("Dance", 40, dance, True),
]


def animate():
    arm = bpy.data.objects["DinoRig"]
    bpy.context.scene.render.fps = FPS
    for name, frames, fn, loop in CLIPS:
        tr.sample(arm, name, frames, fn, loop)
    arm.animation_data.action = bpy.data.actions["Run"]
    return [c[0] for c in CLIPS]


def show(action_name, frame):
    arm = bpy.data.objects["DinoRig"]
    arm.animation_data.action = bpy.data.actions[action_name]
    bpy.context.scene.frame_set(frame)


def export():
    arm = bpy.data.objects["DinoRig"]
    mesh = bpy.data.objects["Dino"]
    path = os.path.join(UNITY_ART, "Dino.fbx")
    tr.rest_pose(arm)
    return tr.export_fbx(path, arm, [mesh])


# ---------------------------------------------------------------------------------------------------- previews

def preview():
    out_dir = os.path.join(HERE, "previews")
    for o in bpy.data.objects:
        if o.type == "MESH" and o.name != "Dino":
            o.hide_render = True
    kit.sun((50, 0, 210), 3.2)
    kit.world_colour("#BFE3F5")
    arm = bpy.data.objects["DinoRig"]
    paths = []
    show("Idle", 1)
    target = Vector((0, 0.1, 0.9))
    for name, yaw, pitch, dist in (("dino_front", 0, 8, 4.2), ("dino_three", -38, 12, 4.4), ("dino_side", -90, 5, 4.6),
                                   ("dino_back", 180, 22, 4.6)):
        y, p = math.radians(yaw), math.radians(pitch)
        loc = target + Vector((math.sin(y) * math.cos(p), -math.cos(y) * math.cos(p), math.sin(p))) * dist
        kit.camera("DinoCam", loc, target, 32.0)
        paths.append(kit.render(os.path.join(out_dir, f"{name}.png"), res=(900, 900)))
    # the game's view of it: from behind and above, mid-run
    show("Run", 5)
    kit.camera("DinoCam", Vector((0, 6.5, 3.4)), Vector((0, -3.5, 0.9)), 60.0)
    paths.append(kit.render(os.path.join(out_dir, "dino_game.png"), res=(1280, 720)))
    # poses
    for clip, frame in (("Run", 1), ("Run", 5), ("Jump", 10), ("Flip", 12), ("Slide", 5), ("Hit", 20), ("Dizzy", 10),
                        ("Cheer", 8), ("Dance", 10), ("Wave", 14), ("Ready", 1), ("LookAround", 22)):
        show(clip, frame)
        kit.camera("DinoCam", target + Vector((-2.9, -3.0, 1.2)), target, 34.0)
        paths.append(kit.render(os.path.join(out_dir, f"pose_{clip}_{frame}.png"), res=(500, 500)))
    arm.animation_data.action = None
    tr.rest_pose(arm)
    return paths
