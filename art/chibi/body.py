"""The chibi's body (skin: head with little ears, neck, torso, arms with mitten hands, legs) and its faces."""

import math

from mathutils import Vector

import kit
from kit import (ANKLE, ARM_DIR, ARM_R, ELBOW, HAND_C, HEAD_C, HIP, KNEE, LEG_R, NECK, SHOULDER, UPPER_ARM, WRIST,
                 FOREARM, Part, bezier, head_normal, head_point, mirror, on_surface, surface_frame)

HEAD_ROWS, HEAD_SEGS = 28, 48


def head_rows(out=0.0, el_from=-math.pi / 2, el_to=math.pi / 2, rows=HEAD_ROWS, segs=HEAD_SEGS):
    """Rings of the head's surface (pushed out by `out`), from elevation el_from to el_to, poles excluded."""
    rows, segs = kit.lod(rows, 8), kit.lod(segs, 12)
    out_rows = []
    for j in range(1, rows):
        el = el_from + (el_to - el_from) * j / rows
        out_rows.append([head_point(2 * math.pi * i / segs, el, out) for i in range(segs)])
    return out_rows


def head():
    p = Part("Head")
    rows = head_rows()
    p.grid(rows, "skin", caps=(head_point(0, -math.pi / 2), head_point(0, math.pi / 2)))
    # little round ears at eye height, a C of skin standing off the sides
    for side in (1, -1):
        az = side * math.radians(90)
        base = head_point(az, math.radians(-12), -0.01)
        n = head_normal(az, math.radians(-12))
        p.ellipsoid(base + n * 0.02, (0.040, 0.058, 0.062), axes=(n, Vector((0, 1, 0)), Vector((0, 0, 1))), segs=16,
                    rings=10)
    return p


def body():
    """Neck, torso, arms with mitten hands and legs (the skin under the clothes)."""
    p = Part("Body", kit.torso_weights)
    c, r = NECK[0], NECK[2]
    p.tube([NECK[0], NECK[1]], r, "skin", segs=18, caps=(False, False), weights=kit.HEAD_W)
    # the torso: superellipse sections, closed with a flat-ish top and bottom
    rows = []
    n = kit.lod(18, 6)
    for k in range(n):
        z = kit.lerp(kit.TORSO[0][0], kit.TORSO[-1][0], k / (n - 1))
        cy, hw, hd, pw = kit.torso_section(z)
        rows.append(kit.superellipse(Vector((0, cy, z)), Vector((1, 0, 0)), Vector((0, 1, 0)), hw, hd, pw, 28))
    p.grid(rows, "skin", caps=(Vector((0, 0.004, kit.TORSO[0][0] - 0.006)), Vector((0, 0.006, kit.TORSO[-1][0] + 0.004))),
           weights=kit.torso_weights)
    for side in (1, -1):
        sh, el, wr = mirror(SHOULDER, side), mirror(ELBOW, side), mirror(WRIST, side)
        d = mirror(ARM_DIR, side)
        path = [sh - d * 0.03, sh + d * 0.04, el, wr]
        p.tube(path, [ARM_R + 0.004, ARM_R + 0.002, ARM_R - 0.003, ARM_R - 0.008], "skin", segs=16,
               caps=(True, False), weights=kit.arm_weights(side))
        mitten(p, side)
        hip, knee, ankle = mirror(HIP, side), mirror(KNEE, side), mirror(ANKLE, side)
        p.tube([hip + Vector((0, 0, 0.03)), hip, knee, ankle + Vector((0, 0, 0.01))],
               [LEG_R + 0.004, LEG_R, LEG_R - 0.006, LEG_R - 0.012], "skin", segs=16, caps=(True, True),
               weights=kit.leg_weights(side))
    return p


def mitten(p, side, region="skin", scale=1.0):
    """A round mitten fist with a thumb over its front, at the end of the arm (closed around a handle along the arm)."""
    d = mirror(ARM_DIR, side)
    c = mirror(HAND_C, side)
    fwd = Vector((0, -1, 0))
    across = d.cross(Vector((0, 1, 0))).normalized()
    w = kit.hand_weights(side)
    p.ellipsoid(c, (0.072 * scale, 0.070 * scale, 0.074 * scale), axes=(across, fwd, d), region=region, segs=20,
                rings=14, weights=w)
    # the thumb: a soft bump on the front, toward the body
    t = c + fwd * 0.048 * scale - d * 0.012 * scale - across * side * 0.018 * scale
    p.ellipsoid(t, (0.026 * scale, 0.028 * scale, 0.034 * scale), axes=(across, fwd, d), region=region, segs=14,
                rings=10, weights=w)


def head_bvh():
    return head().bvh()


# ---------------------------------------------------------------------------------------------------- the face

EYE_X, EYE_Z = 0.118, 1.128
EYE = (0.037, 0.056)            # half width, half height
MOUTH_Z = 1.036


def on_face(bvh, x, z, lift=0.0):
    hit, n = on_surface(bvh, x, z)
    return hit + n * lift, n


def eye_pieces(bvh):
    """Eyes: open (black beans with two shines each) and happy (closed ^ ^ arcs)."""
    eyes = Part("Eyes", kit.W(Eyes=1.0))
    happy = Part("EyesHappy", kit.W(EyesHappy=1.0))
    for side in (1, -1):
        x = side * EYE_X
        c, n = on_face(bvh, x, EYE_Z, -0.004)
        sx, up, nn = surface_frame(n)
        eyes.ellipsoid(c, (EYE[0], EYE[1], 0.016), axes=(sx, up, nn), region="eyes", segs=22, rings=12)
        # shines: a big one up and to the screen's left, a small one down and to the right (light from the left)
        for (dx, dz, rx, rz) in ((-0.013, 0.020, 0.0135, 0.018), (0.014, -0.024, 0.0065, 0.0065)):
            sc, sn = on_face(bvh, x + dx, EYE_Z + dz, 0.0105)
            s2, u2, n2 = surface_frame(sn)
            eyes.ellipsoid(sc, (rx, rz, 0.004), axes=(s2, u2, n2), region="eye_shine", segs=14, rings=8)
        # happy: an arc like an upside-down U
        arc = []
        for k in range(9):
            a = math.pi * k / 8
            arc.append(on_face(bvh, x - math.cos(a) * 0.036, EYE_Z - 0.012 + math.sin(a) * 0.030, 0.004)[0])
        happy.tube(arc, 0.0095, "eyes", segs=10)
    return [eyes, happy]


def mouth_pieces(bvh):
    """A small smile, an open grin with a tongue, a sad mouth."""
    smile = Part("Mouth", kit.W(Mouth=1.0))
    pts = [on_face(bvh, x, MOUTH_Z + 0.010 * (x / 0.034) ** 2 - 0.006, 0.003)[0] for x in
           [-0.034 + 0.068 * k / 10 for k in range(11)]]
    smile.tube(pts, 0.0072, "mouth", segs=10)

    sad = Part("MouthSad", kit.W(MouthSad=1.0))
    pts = [on_face(bvh, x, MOUTH_Z - 0.008 * (x / 0.03) ** 2 - 0.002, 0.003)[0] for x in
           [-0.03 + 0.06 * k / 10 for k in range(11)]]
    sad.tube(pts, 0.0072, "mouth", segs=10)

    grin = Part("MouthOpen", kit.W(MouthOpen=1.0))
    # a D: flat top, round bottom, dark inside with a tongue at the bottom
    rows = []
    centre, n = on_face(bvh, 0.0, MOUTH_Z - 0.008, 0.0)
    sx, up, nn = surface_frame(n)
    w, h = 0.040, 0.034
    ring = []
    for k in range(24):
        a = 2 * math.pi * k / 24
        x = math.cos(a) * w
        y = math.sin(a) * h if math.sin(a) < 0 else math.sin(a) * h * 0.18
        ring.append((x, y))
    for depth, scale in ((0.0035, 1.0), (0.0055, 0.85), (0.0065, 0.5)):
        rows.append([on_face(bvh, x * scale, MOUTH_Z - 0.008 + y * scale, depth)[0] for x, y in ring])
    grin.grid(rows, "mouth", caps=(False, on_face(bvh, 0.0, MOUTH_Z - 0.010, 0.0068)[0]),
              weights=kit.W(MouthOpen=1.0))
    tongue_c, tn = on_face(bvh, 0.004, MOUTH_Z - 0.030, 0.0075)
    s2, u2, n2 = surface_frame(tn)
    grin.ellipsoid(tongue_c, (0.022, 0.012, 0.003), axes=(s2, u2, n2), region="tongue", segs=14, rings=8)
    return [smile, grin, sad]


def blush(bvh):
    p = Part("Blush")
    for side in (1, -1):
        c, n = on_face(bvh, side * 0.208, 1.066, -0.0015)
        sx, up, nn = surface_frame(n)
        p.ellipsoid(c, (0.046, 0.027, 0.004), axes=(sx, up, nn), region="blush", segs=16, rings=8)
    return p
