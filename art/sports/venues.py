"""Sporte!'s venues as code (run inside Blender 5.1; `venues_build.py` runs them headless):

    football()   a full-size stadium: the pitch, both goals' frames, ad boards, four stands with seats, roofs, floodlights,
                 a big screen, dugouts, corner flags
    track()      the athletics stadium around the 100 m straight
    beach()      the beach volleyball court on the Albanian coast

Each writes Assets/Games/Sports/Art/Venues/<Venue>.fbx (objects: Ground = grass / track / sand, coloured by vertex and
textured by the game; Lines; Structure; Seats; Boards = the ad boards' screens, uv.x = metres along the board; Screen =
the big screen, uv 0..1) and a crowd file
<Venue>_crowd.txt: one seat a line, "x y z yaw" in Unity metres and degrees, for the game's crowd of people.
Everything is in Unity coordinates (venue_kit.U); the gameplay spots are documented per venue.
"""

import math
import os
import random
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "tools"))
import tundu_art as ta  # noqa: E402
import venue_kit as vk  # noqa: E402
from venue_kit import Geo  # noqa: E402

# the brand (brand.json) and the venue's own colours
SUN, CORAL, SKY, GRAPE, LEAF, NIGHT = "#FFC93C", "#FF5A4E", "#31C6F4", "#7B61FF", "#6FD66A", "#141938"
WHITE, LINE = "#FFFFFF", "#F4F7FA"
CONCRETE, CONCRETE_DARK, CONCRETE_LIGHT = "#C9CCD6", "#9EA2B2", "#E2E4EA"
STEEL, STEEL_DARK = "#DDE3EE", "#8A93A8"


def fresh(name):
    ta.clear_scene()
    for coll in list(bpy.data.collections):
        for o in list(coll.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.collections.remove(coll)
    return ta.collection(name)


def write_seats(path, seats):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("# x y z yaw (Unity metres, degrees: the way the person faces), one seat a line\n")
        for x, y, z, yaw in seats:
            f.write(f"{x:.2f} {y:.2f} {z:.2f} {yaw:.0f}\n")


# ---------------------------------------------------------------------------------------------------- stands

def stand(geo, seats_geo, seats, origin, along, rows, length, rise=0.44, depth=0.84, front=2.2, colours=(SKY, WHITE),
          aisle=18.0, block_pattern=None, face_yaw=0.0, roof=False, roof_colour=WHITE, back_colour=None):
    """A stand of `rows` steps running `length` metres along `along` (unit x/z vector), its front edge centred at
    `origin` (x, z), climbing away from the pitch. Seats in blocks of the given colours, aisles every `aisle` m.
    Each seat (0.55 m apart) goes into `seats` for the crowd, facing `face_yaw`."""
    ox, oz = origin
    ax, az = along
    # the direction away from the pitch (to the stand's back)
    bx, bz = -az, ax
    half = length / 2

    def P(u, h, y):
        """u along the stand, h back from its front edge, y up."""
        return (ox + ax * u + bx * h, y, oz + az * u + bz * h)

    TO_PITCH = (-bx, 0, -bz)
    AWAY = (bx, 0, bz)
    UP = (0, 1, 0)
    DOWN = (0, -1, 0)

    # the front wall (painted band at the top) facing the pitch
    geo.quad(P(-half, 0, 0), P(-half, 0, front), P(half, 0, front), P(half, 0, 0), [CONCRETE_DARK, CONCRETE, CONCRETE, CONCRETE_DARK],
             normal=TO_PITCH)
    geo.quad(P(-half, -0.01, front - 0.5), P(-half, -0.01, front), P(half, -0.01, front), P(half, -0.01, front - 0.5), NIGHT,
             normal=TO_PITCH)
    top_y = front + rows * rise
    rng = random.Random(int(abs(ox * 7 + oz * 13)) + rows)
    for r in range(rows):
        y0 = front + r * rise
        h0, h1 = r * depth, (r + 1) * depth
        # riser (darker) then tread
        geo.quad(P(-half, h0, y0), P(-half, h0, y0 + rise), P(half, h0, y0 + rise), P(half, h0, y0), CONCRETE_DARK, normal=TO_PITCH)
        geo.quad(P(-half, h0, y0 + rise), P(-half, h1, y0 + rise), P(half, h1, y0 + rise), P(half, h0, y0 + rise), CONCRETE, normal=UP)
        # seat blocks between aisles
        n_blocks = max(1, int(length // aisle))
        block = length / n_blocks
        for b in range(n_blocks):
            u0 = -half + b * block + 0.7
            u1 = -half + (b + 1) * block - 0.7
            colour = colours[(b + (r // 6)) % len(colours)] if block_pattern is None else block_pattern(b, r)
            seat_h = h0 + 0.16
            # the seat: a pan and a back, one long bevelled piece per row and block
            cx, cy, cz = P((u0 + u1) / 2, seat_h + 0.18, y0 + rise + 0.22)
            seats_geo.box((cx, y0 + rise + 0.2, cz), (u1 - u0, 0.12, 0.42), colour, rot_y=math.degrees(math.atan2(ax, az)) - 90,
                          bevel=0.03, side=vk.mix(colour, NIGHT, 0.25))
            bx_, by_, bz_ = P((u0 + u1) / 2, seat_h + 0.42, y0 + rise + 0.45)
            seats_geo.box((bx_, y0 + rise + 0.45, bz_), (u1 - u0, 0.5, 0.08), colour, rot_y=math.degrees(math.atan2(ax, az)) - 90,
                          bevel=0.025, side=vk.mix(colour, NIGHT, 0.2))
            # people's places
            u = u0 + 0.3
            while u < u1 - 0.2:
                if rng.random() < 0.9:
                    sx, sy, sz = P(u + rng.uniform(-0.06, 0.06), h0 + 0.36, y0 + rise + 0.32)
                    seats.append((sx, y0 + rise + 0.3, sz, face_yaw))
                u += 0.62
        # aisle stairs: lighter concrete strips
        for b in range(n_blocks + 1):
            u = -half + b * (length / n_blocks)
            geo.quad(P(u - 0.6, h0, y0 + rise + 0.01), P(u - 0.6, h1, y0 + rise + 0.01), P(u + 0.6, h1, y0 + rise + 0.01),
                     P(u + 0.6, h0, y0 + rise + 0.01), CONCRETE_LIGHT, normal=UP)
    # the back wall, the ends, a railing at the front
    back = rows * depth
    bc = back_colour or CONCRETE
    wall_top = top_y + (8.0 if roof else 2.0)
    geo.quad(P(half, back, top_y), P(half, back, wall_top), P(-half, back, wall_top), P(-half, back, top_y), bc, normal=TO_PITCH)
    geo.quad(P(-half, back + 0.4, 0), P(-half, back + 0.4, wall_top), P(half, back + 0.4, wall_top), P(half, back + 0.4, 0), CONCRETE_DARK,
             normal=AWAY)
    geo.quad(P(-half, back, wall_top), P(-half, back + 0.4, wall_top), P(half, back + 0.4, wall_top), P(half, back, wall_top), CONCRETE,
             normal=UP)
    for s in (-1, 1):
        u = s * half
        pts = [P(u, 0, 0), P(u, 0, front)]
        for r in range(rows):
            pts += [P(u, r * depth, front + (r + 1) * rise), P(u, (r + 1) * depth, front + (r + 1) * rise)]
        pts += [P(u, back, wall_top), P(u, back, 0)]
        # a side wall as a fan from its front bottom corner (the profile is star-shaped from there)
        side_n = (ax * s, 0, az * s)
        for k in range(1, len(pts) - 1):
            geo.face_out([pts[0], pts[k], pts[k + 1]], CONCRETE, side_n)
    fx, fy, fz = P(0, -0.15, front + 0.55)
    geo.box((fx, front + 0.55, fz), (length if abs(ax) > 0.5 else 0.06, 0.05, 0.06 if abs(ax) > 0.5 else length), STEEL, bevel=0.0)
    if roof:
        # a cantilevered roof from the back wall out over the upper rows: white fascia, darker underside
        reach = back * 0.62
        y_back, y_front = wall_top, wall_top - 1.4
        geo.quad(P(-half - 2, back + 0.4, y_back + 0.5), P(-half - 2, back - reach, y_front + 0.5), P(half + 2, back - reach, y_front + 0.5),
                 P(half + 2, back + 0.4, y_back + 0.5), roof_colour, normal=UP)
        geo.quad(P(half + 2, back + 0.4, y_back), P(half + 2, back - reach, y_front), P(-half - 2, back - reach, y_front),
                 P(-half - 2, back + 0.4, y_back), "#7E8496", normal=DOWN)
        geo.quad(P(-half - 2, back - reach, y_front), P(-half - 2, back - reach, y_front + 0.5), P(half + 2, back - reach, y_front + 0.5),
                 P(half + 2, back - reach, y_front), roof_colour, normal=TO_PITCH)
        # trusses under the roof
        n = int(length // 12)
        for i in range(n + 1):
            u = -half + i * (length / n)
            a = P(u, back, y_back - 0.1)
            b = P(u, back - reach + 0.5, y_front - 0.1)
            geo.cylinder(a, b, 0.12, STEEL_DARK, segs=6, caps=False)
    return top_y


# ---------------------------------------------------------------------------------------------------- football

PITCH_X, PITCH_Z = 34.0, 52.5


def football():
    """The stadium for Futboll: the pitch 105 x 68 m, its middle at the origin, the far goal at z = +52.5 (the player
    attacks it from about z = 30), the near goal at z = -52.5 (the player keeps it, the camera behind at z < -55).
    The game adds the goals' nets, the balls, the athletes and the crowd (from Football_crowd.txt)."""
    coll = fresh("Football")
    ground, lines, structure, seats_geo, boards = Geo("Ground"), Geo("Lines"), Geo("Structure"), Geo("Seats"), Geo("Boards")
    screen = Geo("Screen")
    seats = []
    # grass: mown stripes across the pitch (lighter / darker), a darker apron round it, uv = metres / 4
    stripes = 18
    sl = 2 * PITCH_Z / stripes
    g1, g2, apron = "#5DBB4C", "#4FAA42", "#469A3C"
    for i in range(stripes):
        z0, z1 = -PITCH_Z + i * sl, -PITCH_Z + (i + 1) * sl
        c = g1 if i % 2 == 0 else g2
        ground.quad((-PITCH_X, 0, z0), (-PITCH_X, 0, z1), (PITCH_X, 0, z1), (PITCH_X, 0, z0), c,
                    uv=[(-PITCH_X / 4, z0 / 4), (-PITCH_X / 4, z1 / 4), (PITCH_X / 4, z1 / 4), (PITCH_X / 4, z0 / 4)], normal=(0, 1, 0))
    W, Z = 44.0, 64.0
    for (x0, z0, x1, z1) in ((-W, -Z, -PITCH_X, Z), (PITCH_X, -Z, W, Z), (-PITCH_X, -Z, PITCH_X, -PITCH_Z), (-PITCH_X, PITCH_Z, PITCH_X, Z)):
        ground.quad((x0, 0, z0), (x0, 0, z1), (x1, 0, z1), (x1, 0, z0), apron,
                    uv=[(x0 / 4, z0 / 4), (x0 / 4, z1 / 4), (x1 / 4, z1 / 4), (x1 / 4, z0 / 4)], normal=(0, 1, 0))
    # the lines (12 cm, a little above the grass)
    y, w = 0.012, 0.12
    lines.strip([(-PITCH_X, -PITCH_Z), (-PITCH_X, PITCH_Z), (PITCH_X, PITCH_Z), (PITCH_X, -PITCH_Z)], w, y, LINE, closed=True)
    lines.strip([(-PITCH_X, 0), (PITCH_X, 0)], w, y, LINE)
    lines.arc(0, 0, 9.15, 0, 360, w, y, LINE, segs=72)
    lines.disc(0, 0, 0.18, y, LINE)
    for s in (-1, 1):
        gz = s * PITCH_Z
        # penalty box 40.32 x 16.5, goal area 18.32 x 5.5, spot at 11 m, the arc outside the box
        lines.strip([(-20.16, gz), (-20.16, gz - s * 16.5), (20.16, gz - s * 16.5), (20.16, gz)], w, y, LINE)
        lines.strip([(-9.16, gz), (-9.16, gz - s * 5.5), (9.16, gz - s * 5.5), (9.16, gz)], w, y, LINE)
        lines.disc(0, gz - s * 11, 0.15, y, LINE)
        a = math.degrees(math.acos(5.5 / 9.15))
        # the arc round the spot, the part outside the box (toward the middle); angles from +z toward +x
        a0, a1 = (180 - a, 180 + a) if s > 0 else (-a, a)
        lines.arc(0, gz - s * 11, 9.15, a0, a1, w, y, LINE, segs=24)
        # the corner arcs (1 m), the quarter inside the pitch
        for cx in (-PITCH_X, PITCH_X):
            if s > 0:
                c0 = 90 if cx < 0 else 180
            else:
                c0 = 0 if cx < 0 else 270
            lines.arc(cx, gz, 1.0, c0, c0 + 90, w, y, LINE, segs=8)
    # goals: the frames (the game hangs the nets): posts 12 cm, 7.32 x 2.44 m, 2 m deep supports
    for s in (-1, 1):
        gz = s * PITCH_Z
        for x in (-3.72, 3.72):
            structure.cylinder((x, 0, gz), (x, 2.5, gz), 0.065, WHITE, segs=12)
            structure.cylinder((x, 0, gz + s * 2.0), (x, 2.0, gz + s * 2.0), 0.03, STEEL, segs=6)
            structure.cylinder((x, 2.44, gz), (x, 2.0, gz + s * 2.0), 0.03, STEEL, segs=6)
            structure.cylinder((x, 0.02, gz), (x, 0.02, gz + s * 2.0), 0.03, STEEL, segs=6)
        structure.cylinder((-3.72, 2.5, gz), (3.72, 2.5, gz), 0.065, WHITE, segs=12)
        structure.cylinder((-3.72, 2.0, gz + s * 2.0), (3.72, 2.0, gz + s * 2.0), 0.03, STEEL, segs=6)
        structure.cylinder((-3.72, 0.02, gz + s * 2.0), (3.72, 0.02, gz + s * 2.0), 0.03, STEEL, segs=6)
    # corner flags
    for cx in (-PITCH_X, PITCH_X):
        for cz in (-PITCH_Z, PITCH_Z):
            structure.cylinder((cx, 0, cz), (cx, 1.6, cz), 0.02, WHITE, segs=6)
            sx = -0.4 if cx > 0 else 0.4
            structure.face([(cx, 1.6, cz), (cx, 1.25, cz), (cx + sx * 0.0, 1.42, cz + (-0.45 if cz > 0 else 0.45))], CORAL)
            structure.face([(cx, 1.25, cz), (cx, 1.6, cz), (cx, 1.42, cz + (-0.45 if cz > 0 else 0.45))], CORAL)
    # ad boards round the pitch: dark frames, their screens (uv.x = metres along, uv.y = 0..1 up)
    def board_run(a, b):
        ax_, az_ = a
        bx_, bz_ = b
        L = math.hypot(bx_ - ax_, bz_ - az_)
        n = int(L // 6)
        for i in range(n):
            t0, t1 = i / n, (i + 1) / n
            p0 = (ax_ + (bx_ - ax_) * t0, az_ + (bz_ - az_) * t0)
            p1 = (ax_ + (bx_ - ax_) * t1, az_ + (bz_ - az_) * t1)
            dx, dz = (p1[0] - p0[0]), (p1[1] - p0[1])
            ln = math.hypot(dx, dz)
            nx, nz = -dz / ln, dx / ln
            if nx * (p0[0] + p1[0]) + nz * (p0[1] + p1[1]) > 0:   # the screen faces the pitch (the middle)
                nx, nz = -nx, -nz
            back = (-nx * 0.25, -nz * 0.25)
            u0, u1 = L * t0, L * t1
            facing = (nx, 0, nz)
            # the picture reads left to right for someone facing the board: u grows to their right
            right = (-nz, nx)   # cross(up, -facing) in x/z
            if right[0] * dx + right[1] * dz < 0:
                u0, u1 = L - u0, L - u1
            boards.quad((p0[0], 0.12, p0[1]), (p0[0], 1.0, p0[1]), (p1[0], 1.0, p1[1]), (p1[0], 0.12, p1[1]), WHITE,
                        uv=[(u0, 0), (u0, 1), (u1, 1), (u1, 0)], normal=facing)
            structure.quad((p1[0] + back[0], 0, p1[1] + back[1]), (p1[0] + back[0], 1.06, p1[1] + back[1]),
                           (p0[0] + back[0], 1.06, p0[1] + back[1]), (p0[0] + back[0], 0, p0[1] + back[1]), "#2A2F45",
                           normal=(-nx, 0, -nz))
            structure.quad((p0[0], 1.0, p0[1]), (p0[0] + back[0], 1.06, p0[1] + back[1]), (p1[0] + back[0], 1.06, p1[1] + back[1]),
                           (p1[0], 1.0, p1[1]), "#3A405A", normal=(0, 1, 0))
            structure.quad((p0[0], 0, p0[1]), (p0[0], 0.12, p0[1]), (p1[0], 0.12, p1[1]), (p1[0], 0, p1[1]), "#2A2F45", normal=facing)
    bx, bz = 38.5, 57.5
    board_run((-bx, -bz + 4), (-bx, bz - 4))
    board_run((bx, bz - 4), (bx, -bz + 4))
    board_run((bx - 6, bz), (-bx + 6, bz))
    board_run((-bx + 6, -bz), (bx - 6, -bz))
    # dugouts on the left touchline
    for z in (-9, 9):
        structure.box((-39.5, 0.05, z), (2.2, 0.1, 6.0), CONCRETE)
        structure.box((-40.4, 1.1, z), (0.2, 2.2, 6.0), SKY, bevel=0.04)
        structure.box((-39.6, 2.25, z), (2.2, 0.12, 6.2), WHITE, bevel=0.05, tilt_x=0.0)
        structure.box((-39.9, 0.45, z), (0.5, 0.9, 5.4), CORAL, bevel=0.05)
    # four stands (two long ones with roofs, two ends), seats in Tundu colours
    long_rows, end_rows = 26, 20
    stand(structure, seats_geo, seats, (-46.0, 0.0), (0, 1), long_rows, 112.0, colours=(SKY, SKY, WHITE), roof=True, face_yaw=90)
    stand(structure, seats_geo, seats, (46.0, 0.0), (0, -1), long_rows, 112.0, colours=(SKY, CORAL, SKY), roof=True, face_yaw=-90)
    stand(structure, seats_geo, seats, (0.0, 64.5), (1, 0), end_rows, 78.0, colours=(CORAL, SUN, CORAL), face_yaw=180)
    stand(structure, seats_geo, seats, (0.0, -64.5), (-1, 0), end_rows, 78.0, colours=(SKY, WHITE, SKY), face_yaw=0)
    # corner fill walls between the stands (low, closing the bowl)
    for sx in (-1, 1):
        for sz in (-1, 1):
            structure.box((sx * 48, 6.5, sz * 70), (14, 13, 14), CONCRETE_DARK, bevel=0.3, top=CONCRETE)
    # floodlight towers at the corners, their lamp banks facing the pitch
    for sx in (-1, 1):
        for sz in (-1, 1):
            base = (sx * 56, 0, sz * 78)
            top = (sx * 56, 40, sz * 78)
            structure.cylinder(base, top, 0.9, STEEL, segs=10, r2=0.55)
            yaw = math.degrees(math.atan2(-base[0], -base[2]))
            structure.box((top[0], 41.5, top[2]), (9, 5, 1.0), STEEL_DARK, rot_y=yaw, bevel=0.15, tilt_x=-20)
            for i in range(4):
                for j in range(2):
                    # the lamps: white panels on the front of the bank (lit by the bloom in the evening)
                    off = (i - 1.5) * 2.0
                    r = math.radians(yaw)
                    cx = top[0] + math.cos(r) * off + math.sin(r) * 0.6
                    cz = top[2] - math.sin(r) * off + math.cos(r) * 0.6
                    structure.box((cx, 40.4 + j * 2.0, cz), (1.6, 1.4, 0.2), "#FFF7D6", rot_y=yaw, tilt_x=-20)
    # the big screen over the far stand
    structure.box((0, 22, 80), (24, 11, 1.2), "#2A2F45", bevel=0.25)
    screen.quad((-11, 17, 79.35), (-11, 27, 79.35), (11, 27, 79.35), (11, 17, 79.35), WHITE, uv=[(0, 0), (0, 1), (1, 1), (1, 0)],
                normal=(0, 0, -1))
    structure.cylinder((-7, 0, 81), (-7, 17, 81), 0.5, STEEL_DARK, segs=8)
    structure.cylinder((7, 0, 81), (7, 17, 81), 0.5, STEEL_DARK, segs=8)
    objs = [g.to_object(coll) for g in (ground, lines, structure, seats_geo, boards, screen)]
    path = vk.export(os.path.join(vk.UNITY_VENUES, "Football.fbx"), objs)
    write_seats(os.path.join(vk.UNITY_VENUES, "Football_crowd.txt"), seats)
    return {"fbx": path, "seats": len(seats), "tris": {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs}}


# ---------------------------------------------------------------------------------------------------- athletics

LANE, LANES = 1.22, 8
TRACK_IN = -4.88            # the inner edge of lane 1 on the home straight (x)
R_IN = 36.5
R_OUT = R_IN + LANE * LANES
CX = TRACK_IN - R_IN        # the centres of the bends (x)
Z0, Z1 = -12.0, 112.0       # where the home straight meets the bends (the 100 m: z = 0 to 100)
TRACK_RED, TRACK_EDGE = "#D9573F", "#B8452F"


def ring_quads(geo, cx, cz, r0, r1, a0, a1, colour, segs, y=0.0, uv_scale=0.25):
    """A band of a ring (angles in degrees from +z toward +x), flat at height y."""
    for i in range(segs):
        t0 = math.radians(a0 + (a1 - a0) * i / segs)
        t1 = math.radians(a0 + (a1 - a0) * (i + 1) / segs)
        pts = [(cx + r0 * math.sin(t0), y, cz + r0 * math.cos(t0)), (cx + r1 * math.sin(t0), y, cz + r1 * math.cos(t0)),
               (cx + r1 * math.sin(t1), y, cz + r1 * math.cos(t1)), (cx + r0 * math.sin(t1), y, cz + r0 * math.cos(t1))]
        if r0 < 1e-6:
            pts = pts[1:]
        geo.face_out(pts, colour, (0, 1, 0), uv=[(p[0] * uv_scale, p[2] * uv_scale) for p in pts])


def cone_tree(geo, x, z, h, colour="#3F9E55"):
    geo.cylinder((x, 0, z), (x, h * 0.3, z), h * 0.05, "#8A5A3A", segs=6)
    for k, (y0, r) in enumerate(((0.22, 0.34), (0.48, 0.27), (0.7, 0.19))):
        geo.cylinder((x, h * y0, z), (x, h * (y0 + 0.36), z), h * r, vk.mix(colour, "#FFFFFF", 0.06 * k), segs=8, r2=0.0)


def track():
    """The athletics stadium for the 100 metres: the home straight along +z (the start at z = 0, the finish at z =
    100), lanes 1-8 from x = -4.88 (inside) outward, the race in lanes 3-6; the main stand along the straight on +x
    (the side camera on the infield looks at it), the bends and the back straight beyond, an end stand with the big
    screen past the finish."""
    coll = fresh("Track")
    ground, lines, structure, seats_geo, boards, screen = (Geo(n) for n in ("Ground", "Lines", "Structure", "Seats", "Boards", "Screen"))
    seats = []
    g1, g2 = "#5DBB4C", "#53AE45"
    # the infield: striped grass between the straights, fans of grass in the bends
    stripes = 20
    for i in range(stripes):
        za, zb = Z0 + (Z1 - Z0) * i / stripes, Z0 + (Z1 - Z0) * (i + 1) / stripes
        ground.quad((CX - R_IN, 0, za), (CX - R_IN, 0, zb), (TRACK_IN, 0, zb), (TRACK_IN, 0, za), g1 if i % 2 == 0 else g2,
                    uv=[((CX - R_IN) / 4, za / 4), ((CX - R_IN) / 4, zb / 4), (TRACK_IN / 4, zb / 4), (TRACK_IN / 4, za / 4)], normal=(0, 1, 0))
    for cz, a0 in ((Z1, -90), (Z0, 90)):
        ring_quads(ground, CX, cz, 0.0, R_IN, a0, a0 + 180, g1, 36)

    def rect(geo, x0, x1, z0, z1, colour, y=0.0):
        geo.quad((x0, y, z0), (x0, y, z1), (x1, y, z1), (x1, y, z0), colour,
                 uv=[(x0 / 4, z0 / 4), (x0 / 4, z1 / 4), (x1 / 4, z1 / 4), (x1 / 4, z0 / 4)], normal=(0, 1, 0))
    # the track: the straights, the bends, a margin outside, uv in metres / 4
    rect(ground, TRACK_IN, TRACK_IN + LANE * LANES, Z0 - 20, Z1, TRACK_RED)
    rect(ground, TRACK_IN + LANE * LANES, 7.0, Z0 - 20, Z1 + 10, TRACK_EDGE)
    rect(ground, CX - R_OUT, CX - R_IN, Z0, Z1, TRACK_RED)
    ring_quads(ground, CX, Z1, R_IN, R_OUT, -90, 90, TRACK_RED, 48)
    ring_quads(ground, CX, Z0, R_IN, R_OUT, 90, 270, TRACK_RED, 48)
    # grass all round outside the track
    W0, W1, D0, D1 = CX - R_OUT - 30, 30.0, Z0 - R_OUT - 30, Z1 + R_OUT + 30
    for (x0, x1, z0, z1) in ((W0, CX - R_OUT, D0, D1), (7.0, W1, D0, D1), (CX - R_OUT, 7.0, D0, Z0 - R_OUT), (CX - R_OUT, 7.0, Z1 + R_OUT, D1)):
        rect(ground, x0, x1, z0, z1, "#4FA444")
    for cz, a0 in ((Z1, -90), (Z0, 90)):
        ring_quads(ground, CX, cz, R_OUT, R_OUT + 12, a0, a0 + 180, "#4FA444", 36)
    rect(ground, CX + R_OUT - 0.5, 7.0, Z0 - R_OUT, Z0 - 20, "#4FA444")
    rect(ground, TRACK_IN, 7.0, Z1 + 10, Z1 + R_OUT, "#4FA444")
    # lane lines (8 cm, a little wider than real ones so they hold up far away) along the straights and the bends
    y, w = 0.02, 0.08
    for k in range(LANES + 1):
        x = TRACK_IN + k * LANE
        lines.strip([(x, Z0 - 20), (x, Z1)], w, y, LINE)
        lines.strip([(CX - R_IN - k * LANE, Z0), (CX - R_IN - k * LANE, Z1)], w, y, LINE)
        lines.arc(CX, Z1, R_IN + k * LANE, -90, 90, w, y, LINE, segs=60)
        lines.arc(CX, Z0, R_IN + k * LANE, 90, 270, w, y, LINE, segs=60)
    # the start and the finish lines
    lines.strip([(TRACK_IN, 0.0), (TRACK_IN + LANE * LANES, 0.0)], 0.08, y + 0.001, LINE)
    lines.strip([(TRACK_IN, 100.0), (TRACK_IN + LANE * LANES, 100.0)], 0.08, y + 0.001, LINE)
    for k in range(LANES):
        x = TRACK_IN + k * LANE
        # a small coloured mark in each lane behind the start
        lines.quad((x + 0.45, y, -2.6), (x + 0.45, y, -2.25), (x + LANE - 0.45, y, -2.25), (x + LANE - 0.45, y, -2.6),
                   [SKY, SUN, CORAL, GRAPE, LEAF, SKY, SUN, CORAL][k], normal=(0, 1, 0))
        # start blocks: two small pedals on a low rail
        structure.box((x + LANE / 2 - 0.09, 0.05, -0.38), (0.1, 0.06, 0.13), CORAL, bevel=0.015, tilt_x=-30)
        structure.box((x + LANE / 2 + 0.09, 0.05, -0.62), (0.1, 0.06, 0.13), CORAL, bevel=0.015, tilt_x=-30)
        structure.box((x + LANE / 2, 0.015, -0.5), (0.05, 0.03, 0.55), STEEL_DARK)
    # the finish gantry: two posts, a beam in the brand colours, the photo-finish camera on its pole
    for x in (TRACK_IN - 0.7, TRACK_IN + LANE * LANES + 0.7):
        structure.box((x, 2.4, 100.0), (0.35, 4.8, 0.35), WHITE, bevel=0.06)
    structure.box((0.0, 5.0, 100.0), (LANE * LANES + 2.0, 0.9, 0.5), NIGHT, bevel=0.12)
    span = (LANE * LANES + 1.6) / 5
    for i, c in enumerate((CORAL, SUN, SKY, GRAPE, LEAF)):
        structure.box((TRACK_IN - 0.8 + span * (i + 0.5), 4.48, 100.0), (span, 0.14, 0.52), c)
    boards.quad((-3.0, 4.65, 99.73), (-3.0, 5.35, 99.73), (3.0, 5.35, 99.73), (3.0, 4.65, 99.73), WHITE,
                uv=[(0, 0), (0, 1), (6, 1), (6, 0)], normal=(0, 0, -1))
    structure.cylinder((7.3, 0, 100.0), (7.3, 3.2, 100.0), 0.07, STEEL_DARK, segs=8)
    structure.box((7.3, 3.4, 100.0), (0.5, 0.35, 0.35), NIGHT, bevel=0.05)
    structure.cylinder((6.95, 3.4, 100.0), (7.06, 3.4, 100.0), 0.09, "#2A2F45", segs=10)

    # ad boards between the track and the main stand (facing the track, reading left to right from there)
    def board_line(x, z0, z1):
        n = int((z1 - z0) // 6)
        for i in range(n):
            a, b = z0 + i * 6, z0 + (i + 1) * 6
            boards.quad((x, 0.12, b), (x, 1.0, b), (x, 1.0, a), (x, 0.12, a), WHITE,
                        uv=[(i * 6, 0), (i * 6, 1), ((i + 1) * 6, 1), ((i + 1) * 6, 0)], normal=(-1, 0, 0))
            structure.quad((x + 0.25, 0, a), (x + 0.25, 1.06, a), (x + 0.25, 1.06, b), (x + 0.25, 0, b), "#2A2F45", normal=(1, 0, 0))
            structure.quad((x, 1.0, a), (x + 0.25, 1.06, a), (x + 0.25, 1.06, b), (x, 1.0, b), "#3A405A", normal=(0, 1, 0))
            structure.quad((x, 0, a), (x, 0.12, a), (x, 0.12, b), (x, 0, b), "#2A2F45", normal=(-1, 0, 0))
    board_line(7.2, Z0 - 18, Z1 + 6)
    # the main stand along the straight, roofed, its people facing the track
    stand(structure, seats_geo, seats, (8.6, 50.0), (0, -1), 22, 132.0, colours=(SKY, SKY, WHITE, CORAL), roof=True, face_yaw=-90,
          aisle=16.5)
    # the end stand past the finish (facing back down the straight) with the big screen
    ez = Z1 + R_OUT + 6.0
    stand(structure, seats_geo, seats, (CX + 2.0, ez), (1, 0), 16, 86.0, colours=(CORAL, SUN, CORAL), face_yaw=180)
    sx, sz = CX + 2.0, ez + 16 * 0.84 + 2.0
    structure.box((sx, 19.0, sz), (22, 10, 1.2), "#2A2F45", bevel=0.25)
    screen.quad((sx - 10, 14.5, sz - 0.65), (sx - 10, 23.5, sz - 0.65), (sx + 10, 23.5, sz - 0.65), (sx + 10, 14.5, sz - 0.65), WHITE,
                uv=[(0, 0), (0, 1), (1, 1), (1, 0)], normal=(0, 0, -1))
    for dx in (-6, 6):
        structure.cylinder((sx + dx, 0, sz + 1), (sx + dx, 14, sz + 1), 0.5, STEEL_DARK, segs=8)
    # the far side: a grass bank with trees and flags beyond the back straight
    xb = CX - R_OUT - 4
    for i in range(14):
        za, zb = Z0 - 30 + i * 12, Z0 - 30 + (i + 1) * 12
        ground.quad((xb, 0, za), (xb - 16, 7, za), (xb - 16, 7, zb), (xb, 0, zb), "#58B04A", normal=(1, 1, 0))
        ground.quad((xb - 16, 7, za), (xb - 40, 7, za), (xb - 40, 7, zb), (xb - 16, 7, zb), "#58B04A", normal=(0, 1, 0))
    rng = random.Random(12)
    for i in range(40):
        cone_tree(structure, xb - 17 - rng.uniform(0, 14), Z0 - 30 + rng.uniform(0, 170), rng.uniform(7, 12))
    for i in range(12):
        z = Z0 + i * 11
        structure.cylinder((xb - 3, 3.5, z), (xb - 3, 9.5, z), 0.06, WHITE, segs=6)
        c = (CORAL, SUN, SKY, GRAPE, LEAF)[i % 5]
        structure.face_out([(xb - 3, 9.4, z), (xb - 3, 8.2, z), (xb - 3, 8.8, z + 1.6)], c, (1, 0, 0))
        structure.face_out([(xb - 3, 9.4, z), (xb - 3, 8.2, z), (xb - 3, 8.8, z + 1.6)], c, (-1, 0, 0))
    # the start end: a low stand behind the start
    stand(structure, seats_geo, seats, (CX + 6.0, Z0 - R_OUT - 7.0), (-1, 0), 10, 70.0, colours=(SKY, WHITE), face_yaw=0)
    # floodlights at the ends of the main stand
    for z in (Z0 - 14, Z1 + 14):
        structure.cylinder((34, 0, z), (34, 36, z), 0.8, STEEL, segs=10, r2=0.5)
        structure.box((34, 37.5, z), (8, 4.5, 1.0), STEEL_DARK, rot_y=-90, bevel=0.15, tilt_x=-20)
    objs = [g.to_object(coll) for g in (ground, lines, structure, seats_geo, boards, screen)]
    path = vk.export(os.path.join(vk.UNITY_VENUES, "Track.fbx"), objs)
    write_seats(os.path.join(vk.UNITY_VENUES, "Track_crowd.txt"), seats)
    return {"fbx": path, "seats": len(seats), "tris": {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs}}


# ---------------------------------------------------------------------------------------------------- the beach

SAND, SAND_DARK, SAND_WET = "#F2D49B", "#E6C385", "#D2AE74"
WOOD, WOOD_DARK = "#B07A4A", "#8A5A34"


def palm(geo, x, z, h, lean_deg, yaw_deg, seed):
    """A palm: a curved, ringed trunk leaning out, a crown of drooping fronds, coconuts."""
    rng = random.Random(seed)
    yaw = math.radians(yaw_deg)
    dirx, dirz = math.sin(yaw), math.cos(yaw)
    pts = []
    n = 9
    for i in range(n + 1):
        t = i / n
        bend = math.radians(lean_deg) * t * t
        off = h * 0.45 * math.sin(bend)
        pts.append((x + dirx * off, h * t, z + dirz * off))
    for i in range(n):
        r0 = 0.24 - 0.1 * i / n
        geo.cylinder(pts[i], pts[i + 1], r0, WOOD if i % 2 == 0 else WOOD_DARK, segs=8, r2=r0 - 0.012, caps=False)
    top = pts[-1]
    for k in range(9):
        a = 2 * math.pi * k / 9 + rng.uniform(-0.15, 0.15)
        length = rng.uniform(2.6, 3.4)
        droop = rng.uniform(0.9, 1.4)
        # a frond: a thin leaf blade arching out and down, two strips wide
        prev = None
        for s in range(5):
            u = (s + 1) / 5
            px = top[0] + math.cos(a) * length * u
            pz = top[2] + math.sin(a) * length * u
            py = top[1] + 0.5 * u - droop * u * u
            w = 0.42 * math.sin(math.pi * min(1.0, u * 1.1)) + 0.05
            side = (-math.sin(a) * w, 0.0, math.cos(a) * w)
            cur = ((px - side[0], py, pz - side[2]), (px + side[0], py, pz + side[2]))
            if prev is None:
                prev = ((top[0], top[1], top[2]), (top[0], top[1], top[2]))
            colour = "#3FA34A" if s % 2 == 0 else "#4DB556"
            geo.face_out([prev[0], cur[0], cur[1], prev[1]], colour, (0, 1, 0))
            geo.face_out([prev[0], cur[0], cur[1], prev[1]], vk.mix(colour, "#1E5A2A", 0.3), (0, -1, 0))
            prev = cur
    for k in range(3):
        a = 2 * math.pi * k / 3
        geo.box((top[0] + math.cos(a) * 0.2, top[1] - 0.25, top[2] + math.sin(a) * 0.2), (0.22, 0.22, 0.22), "#6B4A2A", bevel=0.06)


def umbrella(geo, x, z, colours):
    geo.cylinder((x, 0, z), (x, 2.3, z), 0.04, WHITE, segs=6)
    segs = 10
    for i in range(segs):
        a0, a1 = 2 * math.pi * i / segs, 2 * math.pi * (i + 1) / segs
        c = colours[i % len(colours)]
        rim0 = (x + 1.35 * math.cos(a0), 1.95, z + 1.35 * math.sin(a0))
        rim1 = (x + 1.35 * math.cos(a1), 1.95, z + 1.35 * math.sin(a1))
        geo.face_out([(x, 2.45, z), rim0, rim1], c, (0, 1, 0))
        geo.face_out([(x, 2.45, z), rim0, rim1], vk.mix(c, NIGHT, 0.3), (0, -1, 0))


# Volejboll!'s net (m): its top (VolleyMatch.NetHeight) and how deep the mesh hangs (VolleyGame builds it)
NET_H, NET_DEPTH = 1.5, 0.8


def beach():
    """The beach volleyball court on the Albanian coast: the court 16 x 8 m in the sand (x across, the net at z = 0,
    our side z < 0, the camera behind it), the sea from z = 20 to the horizon, mountains beyond; sponsor fences round
    the court, small bleachers on both sides, palms, umbrellas, a lifeguard tower, a beach bar."""
    coll = fresh("Beach")
    ground, lines, structure, seats_geo, boards, water = (Geo(n) for n in ("Ground", "Lines", "Structure", "Seats", "Boards", "Water"))
    seats = []
    # sand in big tiles (darker toward the edges and wet by the sea), uv = metres / 4
    for i in range(-6, 6):
        for j in range(-5, 4):
            x0, x1, z0, z1 = i * 10.0, (i + 1) * 10.0, j * 6.0, (j + 1) * 6.0
            def c(x, z):
                d = min(1.0, math.hypot(x / 40.0, (z + 6) / 30.0))
                return vk.mix(SAND, SAND_DARK, 0.6 * d * d)
            ground.quad((x0, 0, z0), (x0, 0, z1), (x1, 0, z1), (x1, 0, z0), [c(x0, z0), c(x0, z1), c(x1, z1), c(x1, z0)],
                        uv=[(x0 / 4, z0 / 4), (x0 / 4, z1 / 4), (x1 / 4, z1 / 4), (x1 / 4, z0 / 4)], normal=(0, 1, 0))
    for i in range(-6, 6):
        x0, x1 = i * 10.0, (i + 1) * 10.0
        ground.quad((x0, 0, 18), (x0, -0.15, 22.5), (x1, -0.15, 22.5), (x1, 0, 18), [SAND_DARK, SAND_WET, SAND_WET, SAND_DARK],
                    uv=[(x0 / 4, 4.5), (x0 / 4, 5.6), (x1 / 4, 5.6), (x1 / 4, 4.5)], normal=(0, 1, 0))
    # the court: 5 cm blue tape, the centre line under the net
    y, w, tape = 0.02, 0.06, "#2F6FE0"
    lines.strip([(-4, -8), (-4, 8), (4, 8), (4, -8)], w, y, tape, closed=True)
    # the net: padded posts, the top and bottom tapes (the game hangs the mesh between them), a referee stand. Since
    # 2026-11-07 a children's net for Volejboll!'s chibis (as high as they are tall: VolleyMatch.NetHeight)
    for s in (-1, 1):
        structure.cylinder((s * 4.9, 0, 0), (s * 4.9, NET_H + 0.35, 0), 0.08, STEEL, segs=10)
        structure.cylinder((s * 4.9, 0, 0), (s * 4.9, NET_H - 0.25, 0), 0.17, SUN if s < 0 else CORAL, segs=12)
    structure.box((0, NET_H - 0.035, 0), (9.6, 0.07, 0.03), WHITE)
    structure.box((0, NET_H - NET_DEPTH, 0), (9.6, 0.05, 0.03), WHITE)
    rx, rh = 5.6, 1.0
    structure.box((rx, rh, 0), (0.9, 0.08, 0.9), WOOD, bevel=0.02)
    for dx in (-0.4, 0.4):
        for dz in (-0.4, 0.4):
            structure.box((rx + dx, rh / 2, dz), (0.06, rh, 0.06), WOOD_DARK)
    for k in range(3):
        structure.box((rx + 0.5, 0.25 + k * 0.3, 0), (0.06, 0.04, 0.6), WOOD_DARK)
    # sponsor fences round the court: frames and their screens (reading left to right from inside)
    def fence(a, b, facing):
        ax, az = a
        bx, bz = b
        L = math.hypot(bx - ax, bz - az)
        n = max(1, int(L // 6))
        for i in range(n):
            t0, t1 = i / n, (i + 1) / n
            p0 = (ax + (bx - ax) * t0, az + (bz - az) * t0)
            p1 = (ax + (bx - ax) * t1, az + (bz - az) * t1)
            right = (-facing[2], facing[0])
            dx, dz = p1[0] - p0[0], p1[1] - p0[1]
            u0, u1 = i * 6.0, (i + 1) * 6.0
            if right[0] * dx + right[1] * dz < 0:
                u0, u1 = u1, u0
            boards.quad((p0[0], 0.05, p0[1]), (p0[0], 0.95, p0[1]), (p1[0], 0.95, p1[1]), (p1[0], 0.05, p1[1]), WHITE,
                        uv=[(u0, 0), (u0, 1), (u1, 1), (u1, 0)], normal=facing)
            back = (-facing[0] * 0.08, -facing[2] * 0.08)
            structure.quad((p0[0] + back[0], 0, p0[1] + back[1]), (p0[0] + back[0], 1.0, p0[1] + back[1]),
                           (p1[0] + back[0], 1.0, p1[1] + back[1]), (p1[0] + back[0], 0, p1[1] + back[1]), "#2A2F45",
                           normal=(-facing[0], 0, -facing[2]))
    fence((-9, -12.5), (9, -12.5), (0, 0, 1))
    fence((-9, 12.5), (9, 12.5), (0, 0, -1))
    # little bleachers on both sides, people facing the court
    stand(structure, seats_geo, seats, (-10.5, 0.0), (0, 1), 6, 22.0, rise=0.42, depth=0.8, front=0.5, colours=(SKY, WHITE, SKY),
          aisle=11.0, face_yaw=90)
    stand(structure, seats_geo, seats, (10.5, 0.0), (0, -1), 6, 22.0, rise=0.42, depth=0.8, front=0.5, colours=(CORAL, SUN, CORAL),
          aisle=11.0, face_yaw=-90)
    # palms, umbrellas, towels, a lifeguard tower, a beach bar
    for i, (x, z, h, lean, yaw) in enumerate(((-15, -16, 8.5, 22, 220), (16, -15, 9.0, 18, 140), (-19, 5, 9.5, 25, 260),
                                              (19, 8, 8.0, 20, 100), (-13, 15, 7.5, 28, 330), (14, 16, 9.0, 24, 30),
                                              (-26, -4, 10.0, 16, 250), (25, -2, 8.8, 20, 110))):
        palm(structure, x, z, h, lean, yaw, i)
    for x, z, cols in ((-17, 12, (CORAL, WHITE)), (18, 13, (SKY, WHITE)), (-22, -12, (SUN, CORAL)), (22, -10, (GRAPE, WHITE)),
                       (-28, 9, (LEAF, WHITE)), (27, 4, (SUN, SKY))):
        umbrella(structure, x, z, cols)
        structure.box((x + 1.1, 0.02, z + 0.6), (0.9, 0.02, 1.8), cols[0], bevel=0.0)
    # the lifeguard tower
    lx, lz = 30.0, 16.0
    for dx in (-0.8, 0.8):
        for dz in (-0.8, 0.8):
            structure.box((lx + dx, 1.4, lz + dz), (0.12, 2.8, 0.12), WHITE)
    structure.box((lx, 2.9, lz), (2.0, 0.12, 2.0), WOOD, bevel=0.03)
    structure.box((lx, 3.8, lz), (1.9, 1.6, 1.9), CORAL, bevel=0.08)
    structure.box((lx, 4.75, lz), (2.4, 0.2, 2.4), WHITE, bevel=0.06)
    structure.cylinder((lx + 1.2, 4.8, lz), (lx + 1.2, 7.0, lz), 0.04, WHITE, segs=6)
    structure.face_out([(lx + 1.2, 7.0, lz), (lx + 1.2, 6.3, lz), (lx + 1.2, 6.65, lz - 1.0)], CORAL, (1, 0, 0))
    structure.face_out([(lx + 1.2, 7.0, lz), (lx + 1.2, 6.3, lz), (lx + 1.2, 6.65, lz - 1.0)], CORAL, (-1, 0, 0))
    # the beach bar: a wooden hut with a straw roof
    bx, bz = -27.0, -20.0
    structure.box((bx, 1.1, bz), (5.0, 2.2, 3.0), WOOD, bevel=0.06, top="#C99A5E")
    structure.box((bx, 1.2, bz + 1.7), (5.2, 0.12, 0.6), "#C99A5E", bevel=0.03)
    for i in range(2):
        structure.box((bx, 2.6 + i * 0.45, bz), (6.2 - i * 1.6, 0.5, 4.2 - i * 1.2), "#E2C27A" if i == 0 else "#D6B064", bevel=0.2)
    # the sea: shallow turquoise by the shore (foam in the alpha) to deep blue; uv = metres
    rows = [(22.5, "#5FD0D8", 0.85), (26, "#3BBAD6", 0.25), (40, "#21A4D6", 0.05), (90, "#1584C8", 0.0), (250, "#1468B0", 0.0),
            (700, "#1B5FA0", 0.0)]
    for (z0, c0, f0), (z1, c1, f1) in zip(rows, rows[1:]):
        for i in range(-6, 6):
            x0, x1 = i * 120.0, (i + 1) * 120.0
            water.quad((x0, -0.12, z0), (x0, -0.12, z1), (x1, -0.12, z1), (x1, -0.12, z0), [(c0, f0), (c1, f1), (c1, f1), (c0, f0)],
                       uv=[(x0, z0), (x0, z1), (x1, z1), (x1, z0)], normal=(0, 1, 0))
    # mountains along the horizon, hazy
    rng = random.Random(4)
    for i in range(11):
        cx = -700 + i * 140 + rng.uniform(-40, 40)
        cz = 620 + rng.uniform(-60, 80)
        h = rng.uniform(40, 85)
        r = rng.uniform(150, 230)
        structure.cylinder((cx, -1, cz), (cx, h, cz), r, vk.mix("#7B9CC4", "#9DBADA", rng.random()), segs=9, r2=r * 0.18, caps=False)
    # more sand off to the sides and behind
    for (x0, x1, z0, z1) in ((-200, -60, -120, 18), (60, 200, -120, 18), (-60, 60, -120, -30)):
        ground.quad((x0, 0, z0), (x0, 0, z1), (x1, 0, z1), (x1, 0, z0), SAND_DARK,
                    uv=[(x0 / 4, z0 / 4), (x0 / 4, z1 / 4), (x1 / 4, z1 / 4), (x1 / 4, z0 / 4)], normal=(0, 1, 0))
    for (x0, x1) in ((-200, -60), (60, 200)):
        ground.quad((x0, 0, 18), (x0, -0.15, 22.5), (x1, -0.15, 22.5), (x1, 0, 18), [SAND_DARK, SAND_WET, SAND_WET, SAND_DARK],
                    uv=[(x0 / 4, 4.5), (x0 / 4, 5.6), (x1 / 4, 5.6), (x1 / 4, 4.5)], normal=(0, 1, 0))
    objs = [g.to_object(coll) for g in (ground, lines, structure, seats_geo, boards, water)]
    path = vk.export(os.path.join(vk.UNITY_VENUES, "Beach.fbx"), objs)
    write_seats(os.path.join(vk.UNITY_VENUES, "Beach_crowd.txt"), seats)
    return {"fbx": path, "seats": len(seats), "tris": {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs}}
