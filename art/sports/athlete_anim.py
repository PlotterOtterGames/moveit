"""Sporte!'s athlete clips (run inside Blender 5.1 after athlete.build(); `build.py --anim`).

    animate()          keys every clip (30 fps) on AthleteRig and returns their names
    contact_sheets()   renders frames of each clip side by side to art/sports/previews/clips/
    CLIPS              name -> frames, loops, when the ball is met (s), the speed the feet carry (m/s)

A pose is a dict of controls (STAND has them all):
    hips (x, y, z) metres moved, hrot (pitch, yaw, roll) degrees; spine / chest / neck / head / shL / shR / handL /
    handR (pitch, yaw, roll) relative to the parent; uaL faL uaR faR: upper arm / forearm directions in the CHEST's
    frame (they lean with the body); or ikL / ikR: where the wrist goes (armature space, IK) with elbowL / elbowR
    hints; footL / footR: where the ankle goes (IK, the foot stays flat unless toeL / toeR pitch it, + = toes down)
    with kneeL / kneeR hints; or thighL / shinL ...: leg directions in the HIPS' frame (kicks); face (eyes, shut eyes,
    smile, open mouth, sad mouth) as 1 / 0.
Key poses are eased between (arms and legs that are IK in one key and aimed in the other become IK); cycles (runs,
stances) are functions of the phase. Axes: facing -Y, Z up, the character's left = +X (pitch + = top goes forward).
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
import tundu_rig as tr  # noqa: E402

FPS = 30
TAU = 2 * math.pi
S, C = math.sin, math.cos


def V(*v):
    return Vector(v)


def norm(*v):
    return Vector(v).normalized()


def arm(side, fwd, out, up):
    """A limb direction: forward, outward (away from the body's middle), up."""
    return norm(side * out, -fwd, up)


leg = arm

NEUTRAL, HAPPY, JOY, EFFORT, SAD, SHUT_SMILE = (1, 0, 1, 0, 0), (0, 1, 0, 1, 0), (1, 0, 0, 1, 0), (1, 0, 0, 1, 0), \
    (1, 0, 0, 0, 1), (0, 1, 1, 0, 0)
ANKLE_Z = at.ANKLE.z

STAND = dict(
    hips=(0.0, 0.0, 0.0), hrot=(0.0, 0.0, 0.0), spine=(0.0, 0.0, 0.0), chest=(0.0, 0.0, 0.0), neck=(0.0, 0.0, 0.0),
    head=(0.0, 0.0, 0.0), shL=(0.0, 0.0, 0.0), shR=(0.0, 0.0, 0.0), handL=(0.0, 0.0, 0.0), handR=(0.0, 0.0, 0.0),
    uaL=arm(1, 0.02, 0.29, -0.957), faL=arm(1, 0.25, 0.15, -0.957),
    uaR=arm(-1, 0.02, 0.29, -0.957), faR=arm(-1, 0.25, 0.15, -0.957),
    footL=V(0.10, 0.015, ANKLE_Z), footR=V(-0.10, 0.015, ANKLE_Z), kneeL=V(0.08, -1, 0), kneeR=V(-0.08, -1, 0),
    toeL=0.0, toeR=0.0, face=NEUTRAL,
)

LIMB_KEYS = {"L": ("uaL", "faL", "ikL", "elbowL"), "R": ("uaR", "faR", "ikR", "elbowR")}
LEG_KEYS = {"L": ("thighL", "shinL", "footL", "kneeL"), "R": ("thighR", "shinR", "footR", "kneeR")}


def pose(**kw):
    """A pose: STAND with some controls changed (a limb given by IK drops its aimed directions and vice versa)."""
    p = dict(STAND)
    for side in "LR":
        ua, fa, ik, _ = LIMB_KEYS[side]
        if ik in kw:
            p.pop(ua, None)
            p.pop(fa, None)
        th, sh, ft, _ = LEG_KEYS[side]
        if th in kw:
            p.pop(ft, None)
    p.update(kw)
    return p


# ---------------------------------------------------------------------------------------------------- solving

def rot(pyr):
    p, y, r = pyr
    return (Matrix.Rotation(math.radians(y), 3, "Z") @ Matrix.Rotation(math.radians(p), 3, "X")
            @ Matrix.Rotation(math.radians(r), 3, "Y"))


def two_bone(a, t, l1, l2, hint):
    d = t - a
    dist = max(1e-4, min(d.length, l1 + l2 - 1e-4))
    dn = d.normalized()
    cos_a = max(-1.0, min(1.0, (l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist)))
    h = hint - dn * hint.dot(dn)
    if h.length < 1e-5:
        h = dn.orthogonal()
    h.normalize()
    return a + (dn * cos_a + h * math.sqrt(1 - cos_a * cos_a)) * l1


class Solver:
    def __init__(self, arm_obj):
        bones = arm_obj.data.bones
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in bones}
        self.head = {b.name: b.head_local.copy() for b in bones}
        self.dir = {b.name: (b.tail_local - b.head_local).normalized() for b in bones}
        self.length = {b.name: (b.tail_local - b.head_local).length for b in bones}
        self.order = [b.name for b in bones]   # parents come first in Blender's bone list

    def solve(self, p):
        A, pos, R = {}, {}, {}
        I = Matrix.Identity(3)

        def place(bone, r):
            par = self.parent[bone]
            pa = A[par] if par else I
            R[bone] = r
            A[bone] = pa @ r
            if par:
                pos[bone] = pos[par] + pa @ (self.head[bone] - self.head[par])
            return A[bone]

        def aim(bone, direction):
            par = self.parent[bone]
            pa = A[par] if par else I
            local = pa.inverted() @ direction
            r = self.dir[bone].rotation_difference(local).to_matrix()
            return place(bone, r)

        A["Root"], pos["Root"], R["Root"] = I, self.head["Root"].copy(), I
        place("Hips", rot(p["hrot"]))
        pos["Hips"] = self.head["Hips"] + Vector(p["hips"])
        place("Spine", rot(p["spine"]))
        place("Chest", rot(p["chest"]))
        place("Neck", rot(p["neck"]))
        place("Head", rot(p["head"]))
        for b in ("Eyes", "EyesShut", "Mouth", "MouthOpen", "MouthSad"):
            place(b, I)
        ends = {}
        for side in "LR":
            ua, fa, ik, el = LIMB_KEYS[side]
            place(f"Shoulder.{side}", rot(p[f"sh{side}"]))
            # where the upper arm starts: placed with the shoulder's rotation
            sh = f"Shoulder.{side}"
            pos[f"UpperArm.{side}"] = pos[sh] + A[sh] @ (self.head[f"UpperArm.{side}"] - self.head[sh])
            if ik in p:
                s0 = pos[f"UpperArm.{side}"]
                hint = Vector(p.get(el, V(1 if side == "L" else -1, 0.3, -0.6)))
                e = two_bone(s0, Vector(p[ik]), self.length[f"UpperArm.{side}"], self.length[f"Forearm.{side}"], hint)
                du, df = (e - s0).normalized(), (Vector(p[ik]) - e).normalized()
            else:
                du, df = A["Chest"] @ Vector(p[ua]), A["Chest"] @ Vector(p[fa])
            aim(f"UpperArm.{side}", du.normalized())
            aim(f"Forearm.{side}", df.normalized())
            place(f"Hand.{side}", rot(p[f"hand{side}"]))
            ends[ik] = pos[f"Forearm.{side}"] + A[f"Forearm.{side}"] @ self.dir[f"Forearm.{side}"] * self.length[f"Forearm.{side}"]
        hip_yaw = Matrix.Rotation(math.radians(p["hrot"][1]), 3, "Z")
        for side in "LR":
            th, sh, ft, kn = LEG_KEYS[side]
            thigh = f"Thigh.{side}"
            pos[thigh] = pos["Hips"] + A["Hips"] @ (self.head[thigh] - self.head["Hips"])
            if th in p:
                dt, ds = A["Hips"] @ Vector(p[th]), A["Hips"] @ Vector(p[sh])
            else:
                h0 = pos[thigh]
                k = two_bone(h0, Vector(p[ft]), self.length[thigh], self.length[f"Shin.{side}"], Vector(p.get(kn, V(0, -1, 0))))
                dt, ds = (k - h0).normalized(), (Vector(p[ft]) - k).normalized()
            aim(thigh, dt.normalized())
            aim(f"Shin.{side}", ds.normalized())
            toe = p.get(f"toe{side}", 0.0)
            if th in p:
                place(f"Foot.{side}", Matrix.Rotation(math.radians(toe), 3, "X"))
            else:
                flat = hip_yaw @ self.dir[f"Foot.{side}"]
                side_axis = flat.cross(Vector((0, 0, 1))).normalized()
                aim(f"Foot.{side}", Matrix.Rotation(math.radians(-toe), 3, side_axis) @ flat)
            ends[ft] = pos[f"Shin.{side}"] + A[f"Shin.{side}"] @ self.dir[f"Shin.{side}"] * self.length[f"Shin.{side}"]
        return R, ends

    def write(self, posebones, p, R):
        for name, r in R.items():
            if name == "Root":
                continue
            m = posebones[name].bone.matrix_local.to_3x3()
            pb = posebones[name]
            pb.rotation_mode = "QUATERNION"
            pb.rotation_quaternion = (m.inverted() @ r @ m).to_quaternion()
            pb.location = (0, 0, 0)
            pb.scale = (1, 1, 1)
        hips = posebones["Hips"]
        hips.location = hips.bone.matrix_local.to_3x3().inverted() @ Vector(p["hips"])
        eyes, shut, smile, opened, sad = p["face"]
        for b, on in (("Eyes", eyes), ("EyesShut", shut), ("Mouth", smile), ("MouthOpen", opened), ("MouthSad", sad)):
            s = 1.0 if on else 0.001
            posebones[b].scale = (s, s, s)


# ---------------------------------------------------------------------------------------------------- blending keys

def mix(a, b, k, solver):
    """A pose between two key poses (k = 0..1, already eased)."""
    out = {}
    pa, pb = dict(a), dict(b)
    for side in "LR":
        ua, fa, ik, el = LIMB_KEYS[side]
        if (ik in pa) != (ik in pb):
            for src in (pa, pb):
                if ik not in src:
                    _, ends = solver.solve(src)
                    src[ik] = ends[ik]
                    hint = (pb if src is pa else pa).get(el)
                    if hint is not None:
                        src[el] = hint
                    src.pop(ua, None)
                    src.pop(fa, None)
        th, sh, ft, kn = LEG_KEYS[side]
        if (th in pa) != (th in pb):
            for src in (pa, pb):
                if th in src:
                    _, ends = solver.solve(src)
                    src[ft] = ends[ft]
                    src.pop(th, None)
                    src.pop(sh, None)
    for key in set(pa) | set(pb):
        va, vb = pa.get(key), pb.get(key)
        if va is None:
            out[key] = vb
        elif vb is None:
            out[key] = va
        elif key == "face":
            out[key] = va if k < 0.5 else vb
        elif isinstance(va, (int, float)):
            out[key] = tr.lerp(va, vb, k)
        else:
            v = Vector(va).lerp(Vector(vb), k)
            if key[:2] in ("ua", "fa") or key[:5] in ("thigh",) or key[:4] in ("shin", "knee", "elbo"):
                if v.length > 1e-6:
                    v = v.normalized()
            out[key] = tuple(v) if isinstance(va, tuple) else v
    return out


def keyed(keys):
    """A pose function from [(t, pose)] (t from 0 to 1), eased between keys."""
    keys = sorted(keys, key=lambda kv: kv[0])

    def at_time(t, solver):
        if t <= keys[0][0]:
            return keys[0][1]
        for (t0, a), (t1, b) in zip(keys, keys[1:]):
            if t0 <= t <= t1:
                return mix(a, b, tr.ease((t - t0) / max(1e-6, t1 - t0)), solver)
        return keys[-1][1]
    return at_time


def cycle(fn):
    return lambda t, solver: fn(t)


def mirror_pose(p):
    out = {}
    for key, v in p.items():
        if key[-1:] in ("L", "R") and key not in ("hrot",):
            key2 = key[:-1] + ("R" if key[-1] == "L" else "L")
        else:
            key2 = key
        if key == "face" or isinstance(v, (int, float)):
            out[key2] = v
        elif key in ("hips",) or key[:2] in ("ua", "fa", "ik") or key[:4] in ("foot", "knee", "elbo", "shin") or key[:5] == "thigh":
            out[key2] = Vector((-v[0], v[1], v[2]))
        else:   # rotations: pitch stays, yaw and roll flip
            out[key2] = (v[0], -v[1], -v[2])
    return out


def mirrored(fn):
    return lambda t, solver: mirror_pose(fn(t, solver))


# ---------------------------------------------------------------------------------------------------- the clips

def idle(t):
    a = TAU * t
    return pose(hips=(0.006 * S(a), 0.0, 0.003 * S(2 * a)), hrot=(0.0, 0.0, 1.2 * S(a)),
                chest=(-1.0 * S(2 * a), 0.0, -0.8 * S(a)), head=(1.5 * S(a + 1), 4 * S(a + 0.5), 1.5 * S(a)),
                uaL=arm(1, 0.02 + 0.03 * S(a), 0.29, -0.957), faL=arm(1, 0.25 + 0.04 * S(a + 0.5), 0.15, -0.957),
                uaR=arm(-1, 0.02 - 0.03 * S(a), 0.29, -0.957), faR=arm(-1, 0.25 - 0.04 * S(a + 0.5), 0.15, -0.957))


def ready(t, depth=0.12, lean=16.0, bounce=0.012, feet=0.17, arms="forward"):
    a = TAU * t * 2
    p = pose(hips=(0.0, 0.03, -depth + bounce * S(a)), hrot=(lean, 0.0, 0.0), chest=(6.0, 0.0, 0.0), neck=(-6.0, 0.0, 0.0),
             head=(-lean * 0.7, 0.0, 0.0),
             footL=V(feet, 0.02, ANKLE_Z), footR=V(-feet, 0.03, ANKLE_Z), kneeL=V(0.35, -1, 0), kneeR=V(-0.35, -1, 0))
    if arms == "forward":
        p.update(uaL=arm(1, 0.55, 0.35, -0.76), faL=arm(1, 0.95, 0.05, -0.15),
                 uaR=arm(-1, 0.55, 0.35, -0.76), faR=arm(-1, 0.95, 0.05, -0.15))
    return p


def keeper_ready(t):
    p = ready(t, depth=0.17, lean=20.0, bounce=0.014, feet=0.24)
    p.update(uaL=arm(1, 0.55, 0.55, -0.62), faL=arm(1, 0.85, 0.25, 0.45),
             uaR=arm(-1, 0.55, 0.55, -0.62), faR=arm(-1, 0.85, 0.25, 0.45), head=(-16.0, 0.0, 0.0))
    return p


def run_cycle(t, stride=0.25, stance=0.32, lift=0.28, lean=8.0, swing=40.0, bounce=0.05, drop=0.05, face=NEUTRAL,
              arms_out=None, bank=0.0, elbow=85.0):
    p = pose(face=face)
    for side, off in (("L", 0.0), ("R", 0.5)):
        sgn = 1 if side == "L" else -1
        u = (t + off) % 1.0
        if u < stance:
            k = u / stance
            y = tr.lerp(-stride, stride, k)
            z = ANKLE_Z
            toe = 25.0 * tr.smoothstep(0.55, 1.0, k)
        else:
            k = (u - stance) / (1 - stance)
            y = tr.lerp(stride, -stride, tr.ease(k))
            z = ANKLE_Z + lift * S(math.pi * k) ** 1.2
            toe = tr.lerp(25.0, -10.0, tr.smoothstep(0.0, 0.5, k))
        p[f"foot{side}"] = V(sgn * 0.10, y + 0.015, z)
        p[f"knee{side}"] = V(0.05 * sgn, -1, 0.3)
        p[f"toe{side}"] = toe
    hz = -drop - bounce * 0.5 * C(TAU * 2 * (t - stance / 2))
    p["hips"] = (0.0, 0.0, hz)
    p["hrot"] = (lean, 7.0 * S(TAU * t), 0.0)
    p["chest"] = (2.0, -14.0 * S(TAU * t), bank * S(TAU * t))
    p["head"] = (-lean * 0.8 + 2.0 * S(TAU * 2 * t), 6.0 * S(TAU * t), 0.0)
    for side, off in (("L", 0.0), ("R", 0.5)):
        sgn = 1 if side == "L" else -1
        if arms_out is not None:
            p[f"ua{side}"] = arm(sgn, 0.0, 1.0, arms_out)
            p[f"fa{side}"] = arm(sgn, 0.05, 1.0, arms_out + 0.1)
            continue
        th = math.radians(swing * C(TAU * (t + off) + math.pi))
        p[f"ua{side}"] = norm(sgn * 0.14, -S(th), -C(th))
        fe = th + math.radians(elbow)
        p[f"fa{side}"] = norm(sgn * 0.04, -S(fe), -C(fe))
    return p


def run(t):
    return run_cycle(t)


def sprint(t):
    return run_cycle(t, stride=0.27, stance=0.22, lift=0.46, lean=12.0, swing=60.0, bounce=0.08, drop=0.06, face=EFFORT,
                     elbow=95.0)


def walk(t):
    return run_cycle(t, stride=0.24, stance=0.62, lift=0.09, lean=2.0, swing=18.0, bounce=0.02, drop=0.015,
                     elbow=20.0)


def airplane(t):
    return run_cycle(t, stride=0.24, stance=0.32, lift=0.26, lean=10.0, bounce=0.05, face=JOY, arms_out=0.05, bank=16.0)


def shuffle_left(t, stride=0.11, stance=0.55, lift=0.09):
    p = ready(0.0, depth=0.15, lean=18.0, bounce=0.0, feet=0.17)
    for side, off in (("L", 0.0), ("R", 0.5)):
        sgn = 1 if side == "L" else -1
        u = (t + off) % 1.0
        if u < stance:
            k = u / stance
            x = tr.lerp(stride, -stride, k)
            z = ANKLE_Z
        else:
            k = (u - stance) / (1 - stance)
            x = tr.lerp(-stride, stride, tr.ease(k))
            z = ANKLE_Z + lift * S(math.pi * k)
        p[f"foot{side}"] = V(sgn * 0.17 + x, 0.02, z)
    p["hips"] = (0.0, 0.03, -0.15 + 0.012 * C(TAU * 2 * t))
    p["hrot"] = (18.0, 0.0, 3.0)
    return p


def cheer_keys():
    up = dict(uaL=arm(1, 0.1, 0.45, 0.89), faL=arm(1, 0.05, 0.25, 0.97), uaR=arm(-1, 0.1, 0.45, 0.89), faR=arm(-1, 0.05, 0.25, 0.97))
    up2 = dict(uaL=arm(1, 0.15, 0.55, 0.82), faL=arm(1, 0.1, 0.4, 0.9), uaR=arm(-1, 0.15, 0.55, 0.82), faR=arm(-1, 0.1, 0.4, 0.9))
    return [
        (0.0, pose(face=HAPPY, **up)),
        (0.16, pose(face=HAPPY, hips=(0, 0.02, -0.11), hrot=(10, 0, 0), kneeL=V(0.3, -1, 0), kneeR=V(-0.3, -1, 0), **up2)),
        (0.38, pose(face=HAPPY, hips=(0, 0.0, 0.22), hrot=(-6, 0, 0), footL=V(0.12, 0.06, 0.26), footR=V(-0.12, 0.06, 0.26),
                    toeL=30, toeR=30, head=(-10, 0, 0), **up)),
        (0.56, pose(face=HAPPY, hips=(0, 0.02, -0.08), hrot=(8, 0, 0), kneeL=V(0.3, -1, 0), kneeR=V(-0.3, -1, 0), **up2)),
        (0.78, pose(face=HAPPY, **up)),
        (1.0, pose(face=HAPPY, **up)),
    ]


def fist_pump_keys():
    raised = dict(uaR=arm(-1, 0.3, 0.2, 0.93), faR=arm(-1, 0.25, 0.15, 0.95))
    pulled = dict(uaR=arm(-1, 0.25, 0.35, -0.9), faR=arm(-1, 0.65, 0.1, 0.75), chest=(10, 0, 0), hips=(0, 0, -0.06),
                  kneeL=V(0.2, -1, 0), kneeR=V(-0.2, -1, 0), uaL=arm(1, 0.1, 0.3, -0.95), faL=arm(1, 0.5, 0.2, -0.85))
    return [(0.0, pose(face=JOY)), (0.2, pose(face=JOY, **raised)), (0.38, pose(face=JOY, **pulled)),
            (0.55, pose(face=JOY, **raised)), (0.74, pose(face=JOY, **pulled)), (1.0, pose(face=JOY, **pulled))]


def sad(t):
    a = TAU * t
    return pose(face=SAD, hips=(0, 0.01, -0.02), hrot=(0, 3 * S(a), 0), spine=(3, 0, 0), chest=(6, 0, 0), neck=(4, 0, 0),
                head=(12 + 2 * S(a), 8 * S(a), 3 * S(a)), shL=(8, 0, 6), shR=(8, 0, -6),
                uaL=arm(1, 0.12, 0.18, -0.98), faL=arm(1, 0.22, 0.12, -0.97),
                uaR=arm(-1, 0.12, 0.18, -0.98), faR=arm(-1, 0.22, 0.12, -0.97))


def hands_on_head(t):
    a = TAU * t
    return pose(face=SAD, hips=(0, 0.01, 0.0), hrot=(-3, 4 * S(a), 0), chest=(-6, 0, 0), head=(-8 + 3 * S(a), 0, 4 * S(a)),
                ikL=V(0.15, -0.01, 1.73), elbowL=V(1, -0.1, 0.2), ikR=V(-0.15, -0.01, 1.73), elbowR=V(-1, -0.1, 0.2))


def clap(t):
    c = (C(TAU * t) + 1) / 2   # 1 = hands apart, 0 = together (half way)
    x = 0.04 + 0.13 * c
    return pose(face=SHUT_SMILE if c < 0.3 else NEUTRAL, hips=(0, 0, -0.01 * (1 - c)),
                ikL=V(x, -0.30, 1.17), elbowL=V(1, 0.3, -1), ikR=V(-x, -0.30, 1.17), elbowR=V(-1, 0.3, -1))


def wave(t):
    a = TAU * t * 2
    return pose(face=(1, 0, 1, 0, 0), head=(0, -6, 4), hrot=(0, 0, -3),
                uaR=arm(-1, 0.15, 0.6, 0.78), faR=arm(-1, 0.12, 0.25 + 0.38 * S(a), 0.92))


def high_five_keys():
    return [(0.0, pose()), (0.4, pose(face=JOY, ikR=V(-0.16, -0.36, 1.98), elbowR=V(-1, 0, -0.4), hrot=(0, 0, 4))),
            (0.6, pose(face=JOY, ikR=V(-0.16, -0.32, 1.95), elbowR=V(-1, 0, -0.4), hrot=(0, 0, 4))), (1.0, pose(face=JOY))]


# ---- football

PLANT_L = V(0.13, -0.12, ANKLE_Z)


def kick_keys(power=1.0):
    """A right-footed shot: wind-up, the strike (the foot meets a ball lying in front, see KICK_BALL), follow-through."""
    k = power
    base = dict(footL=PLANT_L, kneeL=V(0.2, -1, 0))
    return [
        (0.0, pose(hips=(0, 0, -0.03), hrot=(4, 8, 0), footR=V(-0.10, 0.05, ANKLE_Z), **base)),
        (0.2, pose(hips=(0, 0.02, -0.06), hrot=(-6, 18 * k, -4), thighR=leg(-1, -0.45 * k, 0.05, -0.89),
                   shinR=leg(-1, -0.9, 0.05, -0.4 + 0.3 * (1 - k)),
                   uaL=arm(1, 0.5, 0.75, 0.1), faL=arm(1, 0.6, 0.7, 0.2), uaR=arm(-1, -0.5, 0.4, -0.75),
                   faR=arm(-1, -0.2, 0.3, -0.93), head=(10, -6, 0), **base)),
        (0.375, pose(face=EFFORT, hips=(0, -0.02, -0.07), hrot=(-10, -8 * k, 0), thighR=leg(-1, 0.45, 0.0, -0.89),
                     shinR=leg(-1, 0.55, 0.0, -0.83), uaL=arm(1, -0.3, 0.8, -0.2), faL=arm(1, -0.2, 0.7, -0.3),
                     uaR=arm(-1, 0.45, 0.55, -0.4), faR=arm(-1, 0.5, 0.6, -0.2), head=(12, 0, 0), **base)),
        (0.55, pose(face=EFFORT, hips=(0, -0.01, -0.04), hrot=(-16 * k, -14 * k, 0), thighR=leg(-1, 0.95, 0.0, -0.1 + 0.4 * k),
                    shinR=leg(-1, 0.95, 0.0, -0.3 + 0.55 * k), uaL=arm(1, -0.4, 0.75, 0.1), faL=arm(1, -0.2, 0.8, 0.1),
                    uaR=arm(-1, 0.6, 0.5, 0.1), faR=arm(-1, 0.6, 0.6, 0.3), toeL=15, **base)),
        (0.68, pose(hips=(0, -0.05, -0.04), hrot=(-4, -8, 0), thighR=leg(-1, 0.55, 0.0, -0.83), shinR=leg(-1, 0.45, 0.0, -0.89),
                    uaL=arm(1, -0.2, 0.6, -0.5), faL=arm(1, -0.1, 0.6, -0.5), uaR=arm(-1, 0.4, 0.4, -0.6), faR=arm(-1, 0.4, 0.4, -0.5),
                    **base)),
        (0.82, pose(hips=(0, -0.08, -0.03), hrot=(4, -6, 0), footR=V(-0.10, -0.28, ANKLE_Z), **base)),
        (1.0, pose(face=NEUTRAL)),
    ]


KICK_BALL = V(-0.095, -0.40, 0.11)   # where the ball lies for KickR (the foot meets it at CLIPS["KickR"]["contact"])


def keeper_dive_keys():
    """Diving to the character's left (+X), landing on the side with the arms over the head."""
    reach = dict(uaL=arm(1, 0.15, 0.25, 0.95), faL=arm(1, 0.15, 0.2, 0.97), uaR=arm(-1, 0.25, -0.05, 0.96),
                 faR=arm(-1, 0.2, -0.1, 0.97))
    air_legs = dict(thighL=leg(1, 0.05, 0.15, -0.98), shinL=leg(1, -0.05, 0.12, -0.99),
                    thighR=leg(-1, 0.0, 0.35, -0.94), shinR=leg(-1, -0.1, 0.4, -0.91), toeL=30, toeR=30)
    start = keeper_ready(0.0)
    load = dict(start)
    load.update(hips=(-0.04, 0.03, -0.24), hrot=(18, 0, -10), face=EFFORT)
    air = pose(face=EFFORT, hips=(0.42, -0.04, 0.06), hrot=(8, 8, 74), head=(0, 0, -10), **reach, **air_legs)
    down = pose(face=EFFORT, hips=(0.58, -0.04, -0.54), hrot=(4, 10, 88), head=(0, 0, -16), **reach,
                thighL=leg(1, 0.2, 0.1, -0.97), shinL=leg(1, -0.2, 0.1, -0.97),
                thighR=leg(-1, 0.25, 0.3, -0.92), shinR=leg(-1, -0.05, 0.35, -0.93), toeL=30, toeR=30)
    settle = dict(down)
    settle.update(hips=(0.58, -0.04, -0.56))
    return [(0.0, start), (0.15, load), (0.4, air), (0.62, down), (0.8, settle), (1.0, down)]


def jump_catch_keys():
    up = dict(uaL=arm(1, 0.25, 0.2, 0.95), faL=arm(1, 0.3, 0.15, 0.94), uaR=arm(-1, 0.25, 0.2, 0.95), faR=arm(-1, 0.3, 0.15, 0.94))
    start = keeper_ready(0.0)
    crouch = dict(start)
    crouch.update(hips=(0, 0.03, -0.25), face=EFFORT)
    air = pose(face=EFFORT, hips=(0, 0.0, 0.28), hrot=(-4, 0, 0), head=(-18, 0, 0), footL=V(0.16, 0.05, 0.30),
               footR=V(-0.16, 0.05, 0.30), toeL=30, toeR=30, **up)
    land = dict(start)
    land.update(hips=(0, 0.03, -0.22), **up)
    return [(0.0, start), (0.2, crouch), (0.45, air), (0.72, land), (1.0, start)]


# ---- the 100 metres

def marks(t):
    a = TAU * t
    return pose(hips=(0, 0.18, -0.47 + 0.004 * S(a)), hrot=(62, 0, 0), chest=(8 + 1.5 * S(a), 0, 0), neck=(-8, 0, 0),
                head=(18, 0, 0), ikL=V(0.21, -0.40, 0.10), elbowL=V(1, 0.3, 0), ikR=V(-0.21, -0.40, 0.10),
                elbowR=V(-1, 0.3, 0), footL=V(0.10, -0.10, 0.10), kneeL=V(0.05, -1, 0.6), toeL=-10,
                footR=V(-0.10, 0.46, 0.085), kneeR=V(-0.05, -0.4, -1), toeR=40)


def set_pos(t):
    a = TAU * t
    return pose(face=EFFORT, hips=(0, 0.10, -0.30 + 0.003 * S(a)), hrot=(80, 0, 0), chest=(4, 0, 0), neck=(-12, 0, 0),
                head=(10, 0, 0), ikL=V(0.21, -0.40, 0.10), elbowL=V(1, 0.3, 0), ikR=V(-0.21, -0.40, 0.10),
                elbowR=V(-1, 0.3, 0), footL=V(0.10, -0.10, 0.10), kneeL=V(0.05, -1, 0.3), toeL=-10,
                footR=V(-0.10, 0.46, 0.085), kneeR=V(-0.05, -1, -0.3), toeR=35)


def dip_keys():
    stride = sprint(0.1)
    dip = dict(stride)
    dip.update(face=EFFORT, hips=(0, -0.06, -0.12), hrot=(42, 0, 0), chest=(14, 0, 0), neck=(-12, 0, 0), head=(-22, 0, 0),
               uaL=arm(1, -0.85, 0.22, -0.48), faL=arm(1, -0.7, 0.25, -0.66), uaR=arm(-1, -0.85, 0.22, -0.48),
               faR=arm(-1, -0.7, 0.25, -0.66))
    return [(0.0, stride), (0.35, dip), (1.0, dip)]


# ---- beach volleyball

def bump_keys():
    start = ready(0.0, depth=0.14, lean=18)
    low = pose(hips=(0, 0.04, -0.21), hrot=(24, 0, 0), chest=(6, 0, 0), head=(-18, 0, 0),
               footL=V(0.18, 0.02, ANKLE_Z), footR=V(-0.18, 0.03, ANKLE_Z), kneeL=V(0.35, -1, 0), kneeR=V(-0.35, -1, 0),
               ikL=V(0.035, -0.44, 0.62), elbowL=V(1, 0, -1), ikR=V(-0.035, -0.44, 0.62), elbowR=V(-1, 0, -1))
    hit = dict(low)
    hit.update(hips=(0, 0.02, -0.08), hrot=(12, 0, 0), ikL=V(0.035, -0.55, 0.92), ikR=V(-0.035, -0.55, 0.92), head=(-22, 0, 0))
    follow = dict(hit)
    follow.update(hips=(0, 0.02, -0.05), ikL=V(0.035, -0.52, 1.02), ikR=V(-0.035, -0.52, 1.02))
    return [(0.0, start), (0.2, low), (0.4, hit), (0.6, follow), (1.0, start)]


def set_keys():
    start = ready(0.0, depth=0.14, lean=18)
    load = pose(hips=(0, 0.02, -0.16), hrot=(6, 0, 0), head=(-26, 0, 0), footL=V(0.16, 0.02, ANKLE_Z),
                footR=V(-0.16, 0.03, ANKLE_Z), kneeL=V(0.3, -1, 0), kneeR=V(-0.3, -1, 0),
                ikL=V(0.10, -0.20, 1.64), elbowL=V(1, -0.3, -0.3), ikR=V(-0.10, -0.20, 1.64), elbowR=V(-1, -0.3, -0.3))
    hit = dict(load)
    hit.update(hips=(0, 0.0, -0.02), ikL=V(0.075, -0.17, 1.86), ikR=V(-0.075, -0.17, 1.86), head=(-30, 0, 0))
    follow = dict(hit)
    follow.update(hips=(0, 0.0, 0.0), ikL=V(0.06, -0.12, 1.97), ikR=V(-0.06, -0.12, 1.97), toeL=12, toeR=12)
    return [(0.0, start), (0.25, load), (0.45, hit), (0.65, follow), (1.0, start)]


def spike_keys():
    start = ready(0.0, depth=0.10, lean=12)
    load = pose(hips=(0, 0.05, -0.21), hrot=(26, 0, 0), head=(-20, 0, 0), kneeL=V(0.2, -1, 0), kneeR=V(-0.2, -1, 0),
                uaL=arm(1, -0.6, 0.25, -0.75), faL=arm(1, -0.45, 0.2, -0.87), uaR=arm(-1, -0.6, 0.25, -0.75),
                faR=arm(-1, -0.45, 0.2, -0.87))
    air = pose(face=EFFORT, hips=(0, 0.0, 0.30), hrot=(-8, 15, 0), chest=(-6, 6, 0), head=(-24, -10, 0),
               footL=V(0.12, 0.06, 0.42), footR=V(-0.12, 0.10, 0.37), kneeL=V(0.2, -1, 0), kneeR=V(-0.2, -1, 0),
               toeL=30, toeR=30, uaL=arm(1, 0.5, 0.15, 0.85), faL=arm(1, 0.55, 0.15, 0.82),
               uaR=arm(-1, -0.15, 0.55, 0.82), faR=arm(-1, -0.75, 0.2, 0.62))
    hit = dict(air)
    hit.update(hips=(0, 0.0, 0.36), hrot=(10, -10, 0), chest=(6, -6, 0), head=(-18, 6, 0),
               uaR=arm(-1, 0.6, 0.1, 0.79), faR=arm(-1, 0.85, 0.05, 0.52), uaL=arm(1, 0.2, 0.3, -0.93),
               faL=arm(1, 0.3, 0.2, -0.93))
    follow = dict(hit)
    follow.update(hips=(0, 0.0, 0.15), hrot=(18, -14, 0), footL=V(0.12, 0.0, 0.20), footR=V(-0.12, 0.0, 0.2),
                  uaR=arm(-1, 0.6, -0.1, -0.79), faR=arm(-1, 0.4, -0.3, -0.87))
    land = pose(face=EFFORT, hips=(0, 0.02, -0.16), hrot=(20, 0, 0), footL=V(0.15, 0.0, ANKLE_Z), footR=V(-0.15, 0.0, ANKLE_Z),
                kneeL=V(0.3, -1, 0), kneeR=V(-0.3, -1, 0), uaR=arm(-1, 0.4, 0.2, -0.9), faR=arm(-1, 0.4, 0.2, -0.9))
    return [(0.0, start), (0.17, load), (0.36, air), (0.47, hit), (0.6, follow), (0.75, land), (1.0, start)]


def serve_keys():
    hold = pose(ikL=V(0.08, -0.32, 1.05), elbowL=V(1, 0.2, -1), footR=V(-0.12, 0.10, ANKLE_Z))
    low = dict(hold)
    low.update(ikL=V(0.08, -0.30, 0.96), uaR=arm(-1, -0.3, 0.3, -0.9), faR=arm(-1, -0.2, 0.3, -0.93), hips=(0, 0.01, -0.04))
    toss = pose(face=NEUTRAL, hrot=(-4, 16, 0), chest=(-6, 8, 0), head=(-26, -10, 0), footR=V(-0.12, 0.10, ANKLE_Z),
                uaL=arm(1, 0.45, 0.15, 0.88), faL=arm(1, 0.45, 0.12, 0.88), uaR=arm(-1, -0.25, 0.55, 0.8),
                faR=arm(-1, -0.8, 0.1, 0.6))
    hit = pose(face=EFFORT, hrot=(10, -10, 0), chest=(6, -8, 0), head=(-20, 4, 0), footR=V(-0.12, -0.08, ANKLE_Z),
               uaR=arm(-1, 0.55, 0.1, 0.83), faR=arm(-1, 0.75, 0.0, 0.66), uaL=arm(1, 0.2, 0.25, -0.95),
               faL=arm(1, 0.3, 0.2, -0.93), toeR=20)
    follow = dict(hit)
    follow.update(hrot=(14, -14, 0), uaR=arm(-1, 0.65, -0.1, -0.75), faR=arm(-1, 0.55, -0.2, -0.81), face=NEUTRAL)
    return [(0.0, hold), (0.2, low), (0.36, toss), (0.6, hit), (0.8, follow), (1.0, pose())]


def dig_keys():
    start = ready(0.0, depth=0.14, lean=18)
    platform = dict(ikL=V(0.035, -0.75, 0.40), elbowL=V(1, 0, -1), ikR=V(-0.035, -0.75, 0.40), elbowR=V(-1, 0, -1))
    lunge = pose(face=EFFORT, hips=(0, -0.30, -0.33), hrot=(50, 0, 0), head=(-30, 0, 0), footL=V(0.15, -0.35, ANKLE_Z),
                 footR=V(-0.15, 0.25, ANKLE_Z), kneeL=V(0.2, -1, 0), toeR=30, **platform)
    air = pose(face=EFFORT, hips=(0, -0.72, -0.43), hrot=(84, 0, 0), head=(-40, 0, 0), uaL=arm(1, 0.05, 0.15, 0.99),
               faL=arm(1, 0.05, 0.1, 0.99), uaR=arm(-1, 0.05, 0.15, 0.99), faR=arm(-1, 0.05, 0.1, 0.99),
               thighL=leg(1, -0.15, 0.1, -0.98), shinL=leg(1, -0.4, 0.05, -0.91), thighR=leg(-1, -0.15, 0.1, -0.98),
               shinR=leg(-1, -0.4, 0.05, -0.91), toeL=40, toeR=40)
    down = dict(air)
    down.update(hips=(0, -0.86, -0.69), hrot=(88, 0, 0), head=(-46, 0, 0))
    return [(0.0, start), (0.25, lunge), (0.5, air), (0.7, down), (1.0, down)]


def block_keys():
    start = ready(0.0, depth=0.1, lean=6, arms="none")
    start.update(uaL=arm(1, 0.35, 0.3, 0.2), faL=arm(1, 0.1, 0.2, 0.97), uaR=arm(-1, 0.35, 0.3, 0.2), faR=arm(-1, 0.1, 0.2, 0.97))
    crouch = dict(start)
    crouch.update(hips=(0, 0.03, -0.22), hrot=(14, 0, 0), face=EFFORT)
    up = dict(uaL=arm(1, 0.18, 0.15, 0.97), faL=arm(1, 0.2, 0.12, 0.97), uaR=arm(-1, 0.18, 0.15, 0.97), faR=arm(-1, 0.2, 0.12, 0.97))
    air = pose(face=EFFORT, hips=(0, 0.0, 0.32), head=(-10, 0, 0), footL=V(0.14, 0.03, 0.36), footR=V(-0.14, 0.03, 0.36),
               toeL=30, toeR=30, **up)
    land = dict(crouch)
    land.update(up)
    return [(0.0, start), (0.25, crouch), (0.5, air), (0.75, land), (1.0, start)]


def standing(t):
    return pose()


# name: (frames, loops, pose function, the moment of contact (s) or None, ground speed of the feet (m/s) or None)
def clip_table():
    return {
        "Stand": (30, True, cycle(standing), None, None),
        "Idle": (60, True, cycle(idle), None, None),
        "Ready": (24, True, cycle(ready), None, None),
        "Walk": (36, True, cycle(walk), None, 2 * 0.24 / (0.62 * 36 / FPS)),
        "Run": (20, True, cycle(run), None, 2 * 0.25 / (0.32 * 20 / FPS)),
        "Sprint": (16, True, cycle(sprint), None, 2 * 0.27 / (0.22 * 16 / FPS)),
        "ShuffleL": (20, True, cycle(shuffle_left), None, 2 * 0.11 / (0.55 * 20 / FPS)),
        "ShuffleR": (20, True, mirrored(cycle(shuffle_left)), None, 2 * 0.11 / (0.55 * 20 / FPS)),
        "Cheer": (30, True, keyed(cheer_keys()), None, None),
        "FistPump": (30, False, keyed(fist_pump_keys()), None, None),
        "Sad": (48, True, cycle(sad), None, None),
        "HandsOnHead": (48, True, cycle(hands_on_head), None, None),
        "Clap": (16, True, cycle(clap), None, None),
        "Wave": (30, True, cycle(wave), None, None),
        "HighFive": (20, False, keyed(high_five_keys()), 8 / FPS, None),
        "Airplane": (20, True, cycle(airplane), None, 2 * 0.24 / (0.32 * 20 / FPS)),
        "KickR": (24, False, keyed(kick_keys()), 9 / FPS, None),
        "KickL": (24, False, mirrored(keyed(kick_keys())), 9 / FPS, None),
        "PassR": (20, False, keyed(kick_keys(0.55)), 7.5 / FPS, None),
        "PassL": (20, False, mirrored(keyed(kick_keys(0.55))), 7.5 / FPS, None),
        "KeeperReady": (24, True, cycle(keeper_ready), None, None),
        "DiveL": (30, False, keyed(keeper_dive_keys()), 12 / FPS, None),
        "DiveR": (30, False, mirrored(keyed(keeper_dive_keys())), 12 / FPS, None),
        "JumpCatch": (24, False, keyed(jump_catch_keys()), 11 / FPS, None),
        "Marks": (40, True, cycle(marks), None, None),
        "SetPosition": (40, True, cycle(set_pos), None, None),
        "Dip": (14, False, keyed(dip_keys()), 5 / FPS, None),
        "Bump": (20, False, keyed(bump_keys()), 8 / FPS, None),
        "Set": (20, False, keyed(set_keys()), 9 / FPS, None),
        "Spike": (30, False, keyed(spike_keys()), 14 / FPS, None),
        "Serve": (40, False, keyed(serve_keys()), 24 / FPS, None),
        "Dig": (30, False, keyed(dig_keys()), 12 / FPS, None),
        "Block": (24, False, keyed(block_keys()), 12 / FPS, None),
    }


CLIPS = {}


def animate(only=None):
    arm_obj = bpy.data.objects["AthleteRig"]
    bpy.context.scene.render.fps = FPS
    solver = Solver(arm_obj)
    made = []
    for name, (frames, loops, fn, contact, speed) in clip_table().items():
        if only and name not in only:
            continue
        action = tr.new_action(arm_obj, name)
        pbs = arm_obj.pose.bones
        for f in range(frames + 1):
            t = 0.0 if (loops and f == frames) else f / frames
            p = fn(t, solver)
            R, _ = solver.solve(p)
            solver.write(pbs, p, R)
            for pb in pbs:
                pb.keyframe_insert("rotation_quaternion", frame=f + 1)
                pb.keyframe_insert("location", frame=f + 1)
                pb.keyframe_insert("scale", frame=f + 1)
        action.frame_range = (1, frames + 1)
        for fc in tr.action_fcurves(action):
            for kp in fc.keyframe_points:
                kp.interpolation = "LINEAR"
        CLIPS[name] = {"frames": frames, "seconds": frames / FPS, "loop": loops,
                       "contact": round(contact, 4) if contact is not None else None,
                       "speed": round(speed, 3) if speed is not None else None}
        made.append(name)
    tr.rest_pose(arm_obj)
    return made


# ---------------------------------------------------------------------------------------------------- previews

SHEET_PIECES = ["Body", "Face", "Shirt", "Sleeves", "Shorts", "SocksLong", "Shoes", "Hands", "Hair_Crop"]


def contact_sheets(names=None, frames_per=6, yaw=-35, out=None):
    """For each clip, its frames side by side (copies of the posed pieces), from three-quarters in front."""
    out = out or os.path.join(HERE, "previews", "clips")
    os.makedirs(out, exist_ok=True)
    arm_obj = bpy.data.objects["AthleteRig"]
    coll = ta.collection("Preview")
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    paths = []
    names = names or list(CLIPS)
    for o in at.pieces():
        o.hide_render = True
    for name in names:
        for o in list(coll.objects):
            if o.type == "MESH":
                bpy.data.objects.remove(o, do_unlink=True)
        info = CLIPS[name]
        arm_obj.animation_data.action = bpy.data.actions[name]
        n = frames_per
        for i in range(n):
            f = 1 + round(i * (info["frames"] - (0 if not info["loop"] else 1)) / max(1, n - 1))
            scene.frame_set(f)
            dg = bpy.context.evaluated_depsgraph_get()
            for pn in SHEET_PIECES:
                src = bpy.data.objects[pn]
                me = bpy.data.meshes.new_from_object(src.evaluated_get(dg))
                o = bpy.data.objects.new(f"cs{i}_{pn}", me)
                o.matrix_world = Matrix.Translation(((i - (n - 1) / 2) * 1.25, 0, 0)) @ src.matrix_world
                coll.objects.link(o)
        ta.preview_rig(target=(0, 0, 0.9), distance=9.0, yaw_deg=yaw, pitch_deg=8, lens=40)
        paths.append(ta.render_preview(os.path.join(out, f"{name}.png"), res=(1600, 520)))
    for o in list(coll.objects):
        if o.type == "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    for o in at.pieces():
        o.hide_render = False
    tr.rest_pose(arm_obj)
    return paths
