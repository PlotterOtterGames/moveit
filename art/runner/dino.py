"""Dino, Vrapo!'s runner (Blender 5.1, headless; build.py --dino), remade 2026-11-18 for the console polish: a chunky,
cute T-rex kid in a red scarf. Seen from behind most of the game, so the back carries the design: a big round head,
orange plates down the spine, a swinging tail, the scarf's two tails trailing in the wind.

The body is metaballs (smooth fillets where the head, the limbs and the tail join it, like a vinyl toy), remeshed into
even quads; the details are separate pieces that follow one bone each. Everything carries the kit's vertex data
(colour rgb, AO in alpha; uv = (gloss, glow)) for Tundu/Hero.

Faces are parts the game shows and hides by scaling their bone (like the chibis): the open eyes (Eye.L/R), happy closed
eyes (EyeHappy.L/R), the smile (Mouth), the open mouth (MouthOpen).

Facing -Y (Unity +Z), feet on Z = 0, the top of the head ~1.72 m (the game's standing player is 1.8 m).
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector
from mathutils.noise import noise

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "tools"))
import tundu_art as ta  # noqa: E402
import kit  # noqa: E402
from kit import Build, mix, rgb  # noqa: E402

kit.COLOURS.update({
    "dino": "#4CC46A", "dino_dark": "#2B9561", "dino_deep": "#1F7458", "dino_light": "#8EE07A",
    "belly": "#FFE7A6", "belly_line": "#F2C878", "spot": "#2E9A62",
    "plate_base": "#FF7A3D", "plate_tip": "#FFD34A",
    "eye_white": "#FFFFFF", "pupil": "#1B1730", "iris": "#4A2E22", "lid": "#3B8F56", "glint": "#FFFFFF",
    "mouth": "#3A1E2C", "tongue": "#FF7A8A", "tooth": "#FFFFFF", "nostril": "#1F6A4C", "blush": "#FF8F86",
    "claw": "#FFF1D6", "scarf": "#E8403A", "scarf_dark": "#B82E35", "scarf_stripe": "#FFD24A",
})

METABALL = 0.5746          # a metaball element's surface radius per unit of radius (threshold 0.6, stiffness 2)

HEAD_C, HEAD_R = Vector((0, -0.31, 1.47)), Vector((0.385, 0.385, 0.335))
MUZZLE_C, MUZZLE_R = Vector((0, -0.69, 1.35)), Vector((0.27, 0.25, 0.205))

# (centre, radii, rotation degrees, stiffness): the body; limbs on the left side (+X), mirrored
BODY = [
    ((0, 0.06, 0.78), (0.35, 0.37, 0.34), (0, 0, 0), 2.0),       # hips
    ((0, -0.06, 0.86), (0.37, 0.38, 0.4), (-12, 0, 0), 2.0),     # belly
    ((0, -0.13, 1.03), (0.29, 0.29, 0.28), (0, 0, 0), 2.2),      # chest
    ((0, -0.21, 1.2), (0.2, 0.2, 0.17), (-15, 0, 0), 2.6),       # neck
    (tuple(HEAD_C), tuple(HEAD_R), (6, 0, 0), 2.4),             # head
    (tuple(MUZZLE_C), tuple(MUZZLE_R), (4, 0, 0), 2.6),         # muzzle
    ((0, -0.56, 1.24), (0.2, 0.2, 0.11), (0, 0, 0), 2.8),       # chin
    ((0.19, -0.52, 1.36), (0.13, 0.14, 0.12), (0, 0, 0), 2.8),  # cheek (mirrored)
]
LIMBS = [
    ((0.25, 0.05, 0.6), (0.19, 0.23, 0.25), (18, 0, 0), 2.6),     # thigh
    ((0.25, 0.08, 0.32), (0.125, 0.13, 0.17), (-14, 0, 0), 3.0),  # shin
    ((0.25, -0.06, 0.085), (0.15, 0.245, 0.09), (0, 0, 0), 3.0),  # foot
]


def tail_parts(n=8):
    """A tapering tail that dips and curls up a little at the tip."""
    out = []
    for i in range(n):
        t = i / (n - 1)
        y = 0.36 + 1.18 * t
        z = 0.74 - 0.22 * t + 0.16 * t * t
        r = 0.22 * (1 - t) + 0.05 * t
        out.append(((0, y, z), (r, 0.1 + r * 0.6, r * 0.95), (6, 0, 0), 2.4))
    return out


def on_ellipsoid(center, radii, direction):
    u = Vector(direction).normalized()
    t = 1 / math.sqrt(sum((u[i] / radii[i]) ** 2 for i in range(3)))
    return center + u * t


MOUTH = on_ellipsoid(MUZZLE_C, MUZZLE_R, (0, -0.92, -0.3))


def body_mesh(resolution=0.022, voxel=0.019, target=7600):
    """The metaball body as a mesh: even quads (voxel remesh), lightly smoothed, decimated to `target` triangles."""
    mb = bpy.data.metaballs.new("DinoMeta")
    mb.resolution = resolution
    mb.render_resolution = resolution
    mb.threshold = 0.6

    def element(c, radii, rot, stiffness):
        r = max(radii)
        e = mb.elements.new(type="ELLIPSOID")
        e.co = c
        e.radius = r / METABALL
        e.size_x, e.size_y, e.size_z = radii[0] / r, radii[1] / r, radii[2] / r
        e.rotation = Euler([math.radians(a) for a in rot]).to_quaternion()
        e.stiffness = stiffness

    for c, r, rot, s in BODY + tail_parts():
        element(c, r, rot, s)
        if c[0] != 0:
            element((-c[0], c[1], c[2]), r, (rot[0], -rot[1], -rot[2]), s)
    for c, r, rot, s in LIMBS:
        element(c, r, rot, s)
        element((-c[0], c[1], c[2]), r, (rot[0], -rot[1], -rot[2]), s)
    obj = bpy.data.objects.new("DinoMeta", mb)
    bpy.context.scene.collection.objects.link(obj)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(obj.evaluated_get(dg))
    bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.metaballs.remove(mb)
    tmp = bpy.data.objects.new("DinoTmp", me)
    bpy.context.scene.collection.objects.link(tmp)
    rm = tmp.modifiers.new("Remesh", "REMESH")
    rm.mode, rm.voxel_size, rm.adaptivity = "VOXEL", voxel, 0.0
    sm = tmp.modifiers.new("Smooth", "LAPLACIANSMOOTH")
    sm.lambda_factor, sm.iterations, sm.use_volume_preserve = 0.5, 4, True
    ta.bake_modifiers(tmp)
    dec = tmp.modifiers.new("Decimate", "DECIMATE")
    dec.ratio = min(1.0, target / max(1, ta.tri_count(tmp)))
    ta.bake_modifiers(tmp)
    out = tmp.data
    bpy.data.objects.remove(tmp, do_unlink=True)
    return out


SPOTS = [(0.16, 0.18, 1.12, 0.07), (-0.13, 0.05, 1.18, 0.06), (0.06, 0.3, 0.98, 0.05), (-0.2, 0.36, 0.9, 0.065),
         (0.22, -0.18, 1.72, 0.055), (-0.14, -0.28, 1.76, 0.05), (0.0, 0.62, 0.78, 0.045), (0.12, 0.85, 0.66, 0.04),
         (-0.09, 1.05, 0.62, 0.035), (0.3, 0.12, 0.8, 0.05), (-0.31, 0.0, 0.92, 0.055)]


def body_colour(p, n):
    """Green, a deeper green down the back and over the head, round dark spots on the back, the cream belly from the
    chest down the tummy and under the jaw and the tail (clean edges: the game draws no texture)."""
    up = max(0.0, n.z)
    back = max(0.0, min(1.0, (p.y + 0.1) * 1.6)) * max(0.0, n.z * 0.6 + n.y * 0.6)
    c = mix("dino_light", "dino", 0.5 + 0.5 * up)
    c = mix(c, "dino_dark", min(1.0, back) * 0.65)
    for sx, sy, sz, sr in SPOTS:
        d = (p - Vector((sx, sy, sz))).length
        if d < sr * 1.25:
            c = mix(c, "spot", min(1.0, (sr * 1.25 - d) / (sr * 0.25)) * 0.9)
    front = max(0.0, -n.y)
    belly = front * max(0.0, 1.0 - abs(p.x) / 0.27) * (1.0 if 0.42 < p.z < 1.18 else 0.0)
    chin = max(0.0, -n.z - 0.25) * (1.0 if p.y < -0.38 and 1.12 < p.z < 1.32 else 0.0)
    under_tail = max(0.0, -n.z - 0.2) * (1.0 if p.y > 0.45 else 0.0)
    k = max(min(1.0, belly * 2.2 - 0.5), min(1.0, chin * 2.5), min(1.0, under_tail * 2.0))
    if k > 0.0:
        c = mix(c, "belly", k)
    return c


def top_of(bvh, y, x=0.0, z0=3.0):
    hit = bvh.ray_cast(Vector((x, y, z0)), Vector((0, 0, -1)))
    return (hit[0], hit[1]) if hit[0] is not None else (None, None)


# the parts and the bone each follows ("smooth" = weighted by the rig)
PARTS = []


def part(B, make, colour, bone="smooth", gloss=0.0, glow=0.0, sphere=None):
    B.add(make, colour, sway=gloss, glow=glow, sphere=sphere)
    PARTS.append(bone)


def build():
    coll = ta.collection("Dino")
    for o in list(coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    PARTS.clear()
    B = Build("Dino")
    B.ao = 0.6
    me = body_mesh()

    def body(bm):
        bm.from_mesh(me)
    part(B, body, body_colour, "smooth", gloss=0.25)
    # a BVH of the body to sit things on it
    from mathutils.bvhtree import BVHTree
    bm_tmp = bmesh.new()
    bm_tmp.from_mesh(me)
    bvh = BVHTree.FromBMesh(bm_tmp)
    bm_tmp.free()

    # ---- eyes: a white ball, a big dark pupil with a brown ring, two glints; on the head's front, set wide
    for side, name in ((1, "L"), (-1, "R")):
        centre = on_ellipsoid(HEAD_C, HEAD_R, (0.5 * side, -0.76, 0.42))
        centre -= (centre - HEAD_C).normalized() * 0.045
        look = Vector((0.18 * side, -1.0, 0.08)).normalized()
        r = 0.118
        part(B, kit.ellipsoid(centre, (r, r * 0.9, r * 1.08), segs=20, rings=14), "eye_white", f"Eye.{name}", gloss=0.6)
        pc = centre + look * r * 0.72
        part(B, kit.ellipsoid(pc, (0.072, 0.045, 0.082), rot=(0, 0, 10 * side), segs=16, rings=10),
             lambda p, n, c=pc: mix("pupil", "iris", max(0.0, ((p - c).length - 0.045) / 0.03)), f"Eye.{name}", gloss=0.9)
        part(B, kit.ellipsoid(pc + look * 0.04 + Vector((0.028 * side, 0, 0.035)), (0.022, 0.012, 0.024), segs=8, rings=6),
             "glint", f"Eye.{name}", glow=1.0)
        part(B, kit.ellipsoid(pc + look * 0.042 + Vector((-0.02 * side, 0, -0.03)), (0.011, 0.007, 0.011), segs=6, rings=4),
             "glint", f"Eye.{name}", glow=1.0)
        # happy closed eye: an arch (shown instead of the eye)
        arc = [centre + look * r * 0.9 + Vector((dx * side, 0.0, dz)) for dx, dz in ((-0.07, -0.02), (-0.035, 0.03), (0.0, 0.045),
                                                                                    (0.035, 0.03), (0.07, -0.02))]
        part(B, kit.tube(arc, 0.016, segs=6, sub=2), "pupil", f"EyeHappy.{name}")
        # a lid line over the eye (the brow: gives the face its friendly look)
        lid = [centre + Vector((dx * side, -0.02, 0.0)) + (centre - HEAD_C).normalized() * 0.0 + Vector((0, 0, r * 1.05 + dz))
               for dx, dz in ((-0.09, -0.025), (-0.03, 0.012), (0.04, 0.012), (0.1, -0.03))]
        part(B, kit.tube(lid, 0.02, segs=6, sub=2), "lid", "Head")
        # rosy cheek, nostril
        part(B, kit.ellipsoid(on_ellipsoid(MUZZLE_C, MUZZLE_R, (0.85 * side, -0.4, -0.1)), (0.018, 0.07, 0.05),
                              rot=(0, 0, -30 * side), segs=10, rings=6), "blush", "Head")
        part(B, kit.ellipsoid(on_ellipsoid(MUZZLE_C, MUZZLE_R, (0.26 * side, -0.82, 0.5)), (0.022, 0.016, 0.014), segs=8, rings=5),
             "nostril", "Head")

    # ---- the mouth: a smile, and an open mouth with a tongue (shown instead)
    m0 = MOUTH
    smile = []
    for k in range(7):
        u = (k / 6 - 0.5) * 2.0
        q = on_ellipsoid(MUZZLE_C, MUZZLE_R, (u * 0.55, -0.92, -0.32 + 0.16 * u * u))
        smile.append(q + (q - MUZZLE_C).normalized() * 0.006)
    part(B, kit.tube(smile, 0.016, segs=6, sub=2), "mouth", "Mouth")
    mo = m0 + Vector((0, 0.02, -0.015))
    part(B, kit.ellipsoid(mo, (0.11, 0.05, 0.075), segs=14, rings=8), "mouth", "MouthOpen")
    part(B, kit.ellipsoid(mo + Vector((0, -0.025, -0.035)), (0.07, 0.035, 0.035), segs=10, rings=6), "tongue", "MouthOpen")

    # ---- back plates: rounded, orange to yellow, from the back of the head down to the tail's tip
    plates = [(-0.32, 0.075), (-0.12, 0.11), (0.08, 0.13), (0.28, 0.125), (0.48, 0.11), (0.68, 0.095), (0.88, 0.08),
              (1.07, 0.065), (1.25, 0.05), (1.42, 0.038)]
    for k, (y, size) in enumerate(plates):
        top, normal = top_of(bvh, y)
        if top is None:
            continue
        bone = "Head" if y < -0.25 else "Neck" if y < -0.05 else "Spine" if y < 0.15 else "Hips" if y < 0.36 else \
            "Tail1" if y < 0.62 else "Tail2" if y < 0.86 else "Tail3" if y < 1.1 else "Tail4" if y < 1.3 else "Tail5"
        d = (Vector(normal) * 0.7 + Vector((0, 0.25, 0.7)) * 0.3).normalized()
        base = top - d * size * 0.35
        c = base + d * size * 0.75
        part(B, kit.blob(c, (size * 0.5, size * 0.72, size * 1.0), seed=k, amp=0.02, subdiv=3),
             lambda p, n, b=base, s=size: mix("plate_base", "plate_tip", ((p - b).length / (s * 1.7)) ** 1.2), bone, gloss=0.3)

    # ---- the arms: separate little limbs (metaballs melted them into the body), weighted like the body
    for side in (1, -1):
        shoulder = Vector((0.22 * side, -0.2, 1.1))
        elbow = Vector((0.33 * side, -0.33, 1.0))
        wrist = Vector((0.355 * side, -0.47, 0.95))
        part(B, kit.tube([shoulder, elbow, wrist], [0.085, 0.074, 0.066], segs=12, sub=3, cap=False), body_colour, "smooth",
             gloss=0.25)
        part(B, kit.ellipsoid(wrist + Vector((0.0, -0.025, -0.005)), (0.07, 0.085, 0.068), segs=12, rings=8), body_colour,
             "smooth", gloss=0.25)

    # ---- claws: three on each foot, little fingers on each hand
    for side, name in ((1, "L"), (-1, "R")):
        for dx in (-0.075, 0.0, 0.075):
            b = Vector((0.25 * side + dx, -0.27 + abs(dx) * 0.45, 0.07))
            part(B, kit.cone(b, b + Vector((dx * 0.25, -0.075, -0.035)), 0.035, segs=8), "claw", f"Foot.{name}", gloss=0.4)
        hand = Vector((0.355 * side, -0.54, 0.945))
        for dx, dz in ((-0.03, -0.015), (0.0, 0.02), (0.03, -0.015)):
            b = hand + Vector((dx * side, 0.01, dz))
            part(B, kit.cone(b, b + Vector((0.008 * side, -0.045, -0.015)), 0.02, segs=6), "claw", f"Hand.{name}", gloss=0.4)

    # ---- the scarf: a band snug round the neck (on the body's surface), a knot at the back, two tails streaming back
    # (flat ribbons; the game swings them in the wind)
    ring = []
    axis = Vector((0, -0.21, 0.0))
    for k in range(25):
        a = 2 * math.pi * k / 24
        d = Vector((math.cos(a), math.sin(a), 0.0))
        z = 1.165 + 0.065 * math.sin(a)       # low at the front (under his chin), high at the back
        start = Vector((axis.x, axis.y, z)) + d * 0.9
        hit = bvh.ray_cast(start, -d)
        q = hit[0] if hit[0] is not None else Vector((axis.x, axis.y, z)) + d * 0.3
        ring.append(q + d * 0.012)
    stripe = lambda p, n: mix("scarf", "scarf_dark", max(0.0, -n.z) * 0.55)
    part(B, kit.tube(ring, 0.042, segs=8, sub=1, cap=False, squash=((0, 0, 1), 1.25)), stripe, "smooth", gloss=0.2)
    back = ring[6]                                           # a = 90 degrees: the back of the neck
    knot = back + Vector((0, 0.05, 0.0))
    part(B, kit.ellipsoid(knot, (0.08, 0.065, 0.075), segs=12, rings=8), "scarf", "smooth", gloss=0.2)
    SCARF["knot"] = knot
    for side, length in ((1, 0.74), (-1, 0.58)):
        pts = [knot + Vector((0.03 * side, 0.02, -0.01))]
        for k in range(1, 6):
            t = k / 5
            pts.append(knot + Vector((0.04 * side + 0.05 * side * t, length * t, -0.06 * t - 0.05 * t * t)))
        SCARF[side] = (pts[0], pts[-1])

        def tail_colour(p, n, b=knot, L=length):
            t = (p - b).length / L
            return rgb("scarf_stripe") if 0.74 < t < 0.86 else mix("scarf", "scarf_dark", max(0.0, -n.z) * 0.5)
        part(B, kit.tube(pts, [0.045, 0.06, 0.068, 0.072, 0.075, 0.078], segs=8, sub=2, cap=True, squash=((0, 0, 1), 0.28)),
             tail_colour, "scarf", gloss=0.2)

    obj = B.to_object(coll)
    obj["parts"] = len(PARTS)
    DATA["vertex_parts"] = B.vertex_parts
    DATA["part_bones"] = list(PARTS)
    return {"tris": ta.tri_count(obj), "verts": len(obj.data.vertices)}


DATA = {}
SCARF = {}
