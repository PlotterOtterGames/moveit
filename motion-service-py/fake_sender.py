"""Synthetic player: sends a scripted skeleton over Motion Protocol v1, so the Unity side can be tested
without standing in front of a camera.

Scripts:
    basic  idle, step left/right, jump, crouch, hands up   (Runner gestures)
    bowl   hands up (start), three underhand bowling swings, hands up again (must NOT count as a throw)
    slash  hands up (start), six fast right-hand swipes across the body   (Fruta)
    idle   a fidgeting player (sway, weight shift, scratching head, small knee bend, tiptoe): must trigger NOTHING
    dropout  calibrate, camera loses the player for 1 s, player is back, steps left: must NOT recalibrate
    jumps  real jumps (knees bend first, then take-off, landing): hop, small hop, big jump; then a duck and a fast
           stand-up. Must give 3 jumps (the dips must not turn them into crouches) and the stand-up must NOT be a jump
    runner  plays Vrapo! for a visual check: hands up to start, countdown, then sidesteps, hops and ducks
    paint  Pikturo!: picks a colour on the right edge (hold), then paints with the right hand while the left hand is
           raised as the switch: a circle, then (switch down) moves to a new spot and paints a line
    sword  Shpata! (sword right hand, shield left hand): hands up, waits for the knight, swings at its guard, then
           holds the shield overhead, left and right, each followed by a counterattack, then lets one blow through

While the game asks for camera frames (Pikturo's camera mode), it also sends a drawn "camera picture" of the room
and the player, exactly where the joints are (video.py), so the camera mode can be checked without a camera.

    python fake_sender.py                      # basic, loops forever
    python fake_sender.py --script bowl --once # one pass of the bowling script
"""

import argparse
import math
import random
import time

from protocol import FpsMeter, UdpSender, build_packet, now_ms
from skeleton import J, JOINT_NAMES, Person
from video import CONTROL_PORT, VideoStreamer

WIDTH, HEIGHT = 1280, 720
VIEW_HEIGHT_M = 2.6                          # metres of the scene visible vertically at the player's distance
VIEW_WIDTH_M = VIEW_HEIGHT_M * WIDTH / HEIGHT

# Standing 1.7 m player, metres. X > 0 = image right = the player's LEFT side (camera faces the player).
STAND = {
    "nose": (0.00, 1.60), "left_eye": (0.03, 1.64), "right_eye": (-0.03, 1.64),
    "left_ear": (0.07, 1.62), "right_ear": (-0.07, 1.62),
    "left_shoulder": (0.19, 1.42), "right_shoulder": (-0.19, 1.42),
    "left_elbow": (0.24, 1.12), "right_elbow": (-0.24, 1.12),
    "left_wrist": (0.26, 0.85), "right_wrist": (-0.26, 0.85),
    "left_hip": (0.11, 0.95), "right_hip": (-0.11, 0.95),
    "left_knee": (0.12, 0.52), "right_knee": (-0.12, 0.52),
    "left_ankle": (0.12, 0.08), "right_ankle": (-0.12, 0.08),
}
HANDS_UP = {"left_elbow": (0.22, 1.66), "right_elbow": (-0.22, 1.66),
            "left_wrist": (0.18, 1.95), "right_wrist": (-0.18, 1.95)}
UPPER_BODY = {"nose", "left_eye", "right_eye", "left_ear", "right_ear", "left_shoulder", "right_shoulder",
              "left_elbow", "right_elbow", "left_wrist", "right_wrist"}

# (name, duration s, target state). State: x = sideways offset (m, + = player's left), crouch 0..1, hands 0..1.
# "jump" plays a jump arc during the segment; "arm" animates the right arm (see ARM_MOTIONS).
IDLE = dict(x=0.0, crouch=0, hands=0)
BASIC = [
    ("idle (calibration)", 2.5, dict(x=0.0, crouch=0, hands=0)),
    ("step LEFT", 1.6, dict(x=0.40, crouch=0, hands=0)),
    ("back to center", 1.6, dict(x=0.0, crouch=0, hands=0)),
    ("step RIGHT", 1.6, dict(x=-0.40, crouch=0, hands=0)),
    ("back to center", 1.6, dict(x=0.0, crouch=0, hands=0)),
    ("JUMP", 1.4, dict(x=0.0, crouch=0, hands=0, jump=True)),
    ("CROUCH", 1.4, dict(x=0.0, crouch=1, hands=0)),
    ("stand up", 1.2, dict(x=0.0, crouch=0, hands=0)),
    ("HANDS UP", 1.4, dict(x=0.0, crouch=0, hands=1)),
    ("hands down", 1.2, dict(x=0.0, crouch=0, hands=0)),
]
START = [("HANDS UP (start / confirm)", 1.4, dict(IDLE, hands=1)), ("hands down", 1.0, IDLE)]
BOWL = [
    ("idle (calibration)", 2.5, IDLE),
    *START,
    ("BOWL 1", 1.8, dict(IDLE, arm="bowl")),
    ("pause", 0.8, IDLE),
    ("BOWL 2", 1.8, dict(IDLE, arm="bowl")),
    ("pause", 0.8, IDLE),
    ("BOWL 3", 1.8, dict(IDLE, arm="bowl")),
    ("pause", 0.8, IDLE),
    ("HANDS UP (not a throw)", 1.4, dict(IDLE, hands=1)),
    ("hands down", 1.2, IDLE),
]
SLASH = [
    ("idle (calibration)", 2.5, IDLE),
    *START,
    *[("SWIPE", 1.2, dict(IDLE, arm="swipe")), ("pause", 0.4, IDLE)] * 6,
]
IDLE_FIDGET = [
    ("idle (calibration)", 2.5, IDLE),
    ("sway", 4.0, dict(IDLE, move="sway")),
    ("weight shift", 1.5, dict(IDLE, x=0.12)),
    ("weight back", 1.5, IDLE),
    ("scratch head", 2.5, dict(IDLE, arm="scratch")),
    ("small knee bend", 1.0, dict(IDLE, crouch=0.15)),
    ("stand", 1.0, IDLE),
    ("tiptoe", 2.0, dict(IDLE, move="tiptoe")),
    ("arms crossed", 2.5, dict(IDLE, arm="cross")),
    ("idle", 1.5, IDLE),
]
DROPOUT = [
    ("idle (calibration)", 2.5, IDLE),
    ("idle", 1.0, IDLE),
    ("CAMERA LOSES PLAYER", 1.0, dict(IDLE, absent=True)),
    ("back in view", 1.0, IDLE),
    ("step LEFT", 1.6, dict(IDLE, x=0.40)),
    ("back to center", 1.2, IDLE),
]
JUMPS = [
    ("idle (calibration)", 2.5, IDLE),
    ("HOP (knees dip 12 cm)", 1.6, dict(IDLE, hop=(0.12, 0.40))),
    ("SMALL HOP (dip 8 cm)", 1.6, dict(IDLE, hop=(0.08, 0.30))),
    ("BIG JUMP (dip 20 cm)", 1.8, dict(IDLE, hop=(0.20, 0.45))),
    ("CROUCH (duck and hold)", 1.4, dict(IDLE, crouch=0.95)),
    ("stand up fast", 1.2, IDLE),
]
RUNNER = [  # plays Vrapo!: start with hands up, wait out the countdown, then dodge left/right, hop and duck
    ("idle (calibration)", 2.5, IDLE),
    *START,
    ("countdown", 3.2, IDLE),
    *[("step LEFT", 1.1, dict(IDLE, x=0.40)), ("HOP", 1.2, dict(IDLE, x=0.40, hop=(0.12, 0.40))),
      ("back to center", 0.9, IDLE), ("DUCK", 1.0, dict(IDLE, crouch=0.95)), ("stand", 0.8, IDLE),
      ("step RIGHT", 1.1, dict(IDLE, x=-0.40)), ("HOP", 1.2, dict(IDLE, x=-0.40, hop=(0.12, 0.40))),
      ("back to center", 0.9, IDLE)] * 2,
]
PAINT = [  # Pikturo!: hand targets are given in screen ("zone") coordinates, see zone_hand()
    ("idle (calibration)", 2.5, IDLE),
    ("CHOOSE A COLOUR (right hand holds on the right edge)", 2.6, dict(IDLE, arm="paint_pick")),
    ("PAINT A CIRCLE (right hand paints while the left hand is raised)", 4.6, dict(IDLE, arm="paint_circle")),
    ("MOVE (switch down), THEN PAINT A LINE", 4.0, dict(IDLE, arm="paint_line")),
    ("idle", 1.0, IDLE),
]
SWORD = [  # Shpata!: a swing at the knight's guard (bounces off), then three shield blocks, each with a counterattack
    ("idle (calibration)", 2.5, IDLE),
    *START,
    ("countdown, the knight walks in", 5.8, IDLE),
    ("SWING AT ITS GUARD (bounces off its shield)", 1.3, dict(IDLE, arm="cut_right")), ("pause", 0.5, IDLE),
    ("SHIELD OVERHEAD (left hand above the head)", 1.8, dict(IDLE, arm="shield_over")),
    ("COUNTERATTACK", 1.3, dict(IDLE, arm="cut_left")), ("pause", 0.5, IDLE),
    ("SHIELD LEFT (left hand out on the screen's left)", 1.8, dict(IDLE, arm="shield_left")),
    ("COUNTERATTACK", 1.3, dict(IDLE, arm="cut_down")), ("pause", 0.5, IDLE),
    ("SHIELD RIGHT (left hand across to the screen's right)", 1.8, dict(IDLE, arm="shield_right")),
    ("COUNTERATTACK", 1.3, dict(IDLE, arm="cut_right")), ("pause", 0.5, IDLE),
    ("NO BLOCK (arms down while it attacks)", 2.5, IDLE),
    ("idle", 1.0, IDLE),
]
SCRIPTS = {"basic": BASIC, "bowl": BOWL, "slash": SLASH, "idle": IDLE_FIDGET, "dropout": DROPOUT, "jumps": JUMPS,
           "runner": RUNNER, "paint": PAINT, "sword": SWORD}
TRANSITION_S = 0.25
JUMP_HEIGHT_M = 0.35
JUMP_TIME_S = 0.55


def smoothstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def minjerk(t):
    t = max(0.0, min(1.0, t))
    return 10 * t ** 3 - 15 * t ** 4 + 6 * t ** 5


def hop(t, dip, flight, start=0.2, dip_t=0.28, push_t=0.22, land_dip=0.10, land_t=0.35):
    """A real jump: knees dip, push up (fastest at take-off), ballistic flight, knees bend on landing.
    Returns (extra crouch 0..1, jump height m). Crouch 1 lowers the torso centre by ~0.41 m (see body_at)."""
    g = 9.81
    v_takeoff = g * flight / 2
    t -= start
    if t < 0:
        return 0.0, 0.0
    if t < dip_t:
        return dip * minjerk(t / dip_t) / 0.41, 0.0
    t -= dip_t
    if t < push_t:  # cubic Hermite: from -dip at rest to 0 at take-off speed
        s = t / push_t
        y = -dip * (2 * s ** 3 - 3 * s ** 2 + 1) + v_takeoff * push_t * (s ** 3 - s ** 2)
        return -y / 0.41, 0.0
    t -= push_t
    if t < flight:
        return 0.0, v_takeoff * t - 0.5 * g * t * t
    t -= flight
    if t < land_t:
        return land_dip * math.sin(math.pi * t / land_t) / 0.41, 0.0
    return 0.0, 0.0


def bowl_arm(t):
    """Underhand swing of the right arm: back (behind the body), fast forward, follow-through, return.
    Angle 0 = arm hanging, + = forward (toward the camera, negative z)."""
    if t < 0.5:
        deg = -60 * smoothstep(t / 0.5)
    elif t < 0.85:
        deg = -60 + 140 * smoothstep((t - 0.5) / 0.35)
    elif t < 1.3:
        deg = 80
    else:
        deg = 80 * (1 - smoothstep((t - 1.3) / 0.5))
    a = math.radians(deg)
    shoulder_y = STAND["right_shoulder"][1]
    return {
        "right_elbow": (-0.24, shoulder_y - 0.3 * math.cos(a), -0.3 * math.sin(a)),
        "right_wrist": (-0.26, shoulder_y - 0.6 * math.cos(a), -0.6 * math.sin(a)),
    }


def swipe_arm(t):
    """Right hand: wind up across the body, fast swipe out to the right side, return to rest."""
    rest, start, end = (-0.26, 0.85), (0.45, 1.30), (-0.75, 1.30)
    if t < 0.3:
        a, b, k = rest, start, smoothstep(t / 0.3)
    elif t < 0.55:
        a, b, k = start, end, smoothstep((t - 0.3) / 0.25)
    else:
        a, b, k = end, rest, smoothstep((t - 0.55) / 0.65)
    wx, wy = a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k
    sx, sy = STAND["right_shoulder"]
    return {"right_wrist": (wx, wy, -0.3), "right_elbow": ((sx + wx) / 2, (sy + wy) / 2, -0.15)}


def scratch_arm(t):
    """Right hand up to the side of the head (one hand only, no backswing), hold, back down."""
    up = smoothstep(t / 0.8) * (1 - smoothstep((t - 1.7) / 0.8))
    rest_w, head_w = (-0.26, 0.85), (-0.12, 1.70)
    rest_e, head_e = (-0.24, 1.12), (-0.26, 1.45)
    lerp = lambda a, b: (a[0] + (b[0] - a[0]) * up, a[1] + (b[1] - a[1]) * up)
    w, e = lerp(rest_w, head_w), lerp(rest_e, head_e)
    return {"right_wrist": (w[0], w[1], -0.08), "right_elbow": (e[0], e[1], -0.05)}


def cross_arms(t):
    """Both wrists slowly to the chest (arms crossed)."""
    k = smoothstep(t / 1.0)
    out = {}
    for side, sx in (("left", 1), ("right", -1)):
        wx, wy = STAND[f"{side}_wrist"]
        ex, ey = STAND[f"{side}_elbow"]
        out[f"{side}_wrist"] = (wx + (-0.08 * sx - wx) * k, wy + (1.22 - wy) * k, -0.15 * k)
        out[f"{side}_elbow"] = (ex + (0.22 * sx - ex) * k, ey + (1.12 - ey) * k, -0.05 * k)
    return out


# Unity's BodyHands maps a hand into a "zone" that is the screen: x 0..1 = 1.6 torso lengths either side of the
# shoulders, y 0..1 = from 0.3 torso lengths below the hips to 1.3 above the shoulders. Its point is the wrist pushed
# 30% of the forearm further. With the elbow halfway between shoulder and wrist that is wrist + 15% of (wrist -
# shoulder), so a screen target can be turned back into a wrist position for the standing player.
TORSO_M = STAND["left_shoulder"][1] - STAND["left_hip"][1]
ZONE_BOTTOM_M = STAND["left_hip"][1] - 0.3 * TORSO_M
ZONE_HEIGHT_M = STAND["left_shoulder"][1] + 1.3 * TORSO_M - ZONE_BOTTOM_M
ZONE_WIDTH_M = 2 * 1.6 * TORSO_M


def zone_hand(side, zx, zy):
    """Wrist and elbow (metres) that put this hand's cursor at screen point (zx, zy). The screen is a mirror: the
    player's right hand (negative x) appears on the right."""
    tip = (-(zx - 0.5) * ZONE_WIDTH_M, ZONE_BOTTOM_M + zy * ZONE_HEIGHT_M)
    sx, sy = STAND[f"{side}_shoulder"]
    wx, wy = sx + (tip[0] - sx) / 1.15, sy + (tip[1] - sy) / 1.15
    return {f"{side}_wrist": (wx, wy, -0.30), f"{side}_elbow": ((sx + wx) / 2, (sy + wy) / 2, -0.12)}


def hand_path(side, keys):
    """keys: [(time s, (zx, zy) screen point, or None = arm resting)], linear in between (keys are dense where the
    path curves), eased when leaving or reaching the rest pose. Returns an arm motion."""
    rest = {f"{side}_wrist": (*STAND[f"{side}_wrist"], 0.0), f"{side}_elbow": (*STAND[f"{side}_elbow"], 0.0)}
    poses = [(t, rest if p is None else zone_hand(side, *p)) for t, p in keys]

    def arm(t):
        if t <= poses[0][0]:
            return poses[0][1]
        for (t0, a), (t1, b) in zip(poses, poses[1:]):
            if t <= t1:
                k = (t - t0) / (t1 - t0)
                if a is rest or b is rest:
                    k = smoothstep(k)
                return {j: tuple(a[j][i] + (b[j][i] - a[j][i]) * k for i in range(3)) for j in a}
        return poses[-1][1]
    return arm


def switch_hand(side, spans, ramp=0.3):
    """Pikturo's switch: the free hand lifted to the chest (wrist ~0.8 torso lengths above the hips, out to the side,
    forearm up) during each (on, off) span, resting otherwise; `ramp` seconds to lift or lower."""
    sx = STAND[f"{side}_shoulder"][0]
    out = 1 if sx > 0 else -1                         # the player's left side has positive x
    hip_y = STAND["left_hip"][1]
    up = {f"{side}_wrist": (sx + out * 0.21, hip_y + 0.8 * TORSO_M, -0.25),
          f"{side}_elbow": (sx + out * 0.12, hip_y + 0.25 * TORSO_M, -0.10)}
    rest = {f"{side}_wrist": (*STAND[f"{side}_wrist"], 0.0), f"{side}_elbow": (*STAND[f"{side}_elbow"], 0.0)}

    def arm(t):
        k = 0.0
        for on, off in spans:
            k = max(k, smoothstep((t - on) / ramp) * (1 - smoothstep((t - off) / ramp)))
        return {j: tuple(rest[j][i] + (up[j][i] - rest[j][i]) * k for i in range(3)) for j in rest}
    return arm


def both(*arms):
    return lambda t: {k: v for arm in arms for k, v in arm(t).items()}


def circle_keys(start, center, rx, ry, seconds, steps=32):
    return [(start + seconds * i / steps, (center[0] + rx * math.cos(2 * math.pi * i / steps),
                                          center[1] + ry * math.sin(2 * math.pi * i / steps))) for i in range(steps + 1)]


def arm_keys(side, keys):
    """keys: [(time s, wrist (x, y, z) or None = resting, elbow (x, y, z) or None = halfway to the shoulder)], min-jerk
    between keys (a real arm). Metres, x > 0 = the player's left (the screen's left), z < 0 = toward the camera."""
    sx, sy = STAND[f"{side}_shoulder"]
    rest_w, rest_e = (*STAND[f"{side}_wrist"], 0.0), (*STAND[f"{side}_elbow"], 0.0)

    def pose(w, e):
        if w is None:
            return rest_w, rest_e
        return w, e if e is not None else ((sx + w[0]) / 2, (sy + w[1]) / 2 - 0.04, w[2] * 0.5)
    poses = [(t, *pose(w, e)) for t, w, e in keys]

    def arm(t):
        if t <= poses[0][0]:
            w, e = poses[0][1:]
        else:
            w, e = poses[-1][1:]
            for (t0, w0, e0), (t1, w1, e1) in zip(poses, poses[1:]):
                if t <= t1:
                    k = minjerk((t - t0) / (t1 - t0))
                    w = tuple(a + (b - a) * k for a, b in zip(w0, w1))
                    e = tuple(a + (b - a) * k for a, b in zip(e0, e1))
                    break
        return {f"{side}_wrist": w, f"{side}_elbow": e}
    return arm


def sword_cut(start, end, wind=0.35, swing=0.2, hold=0.2):
    """A sword cut with the right hand: bring it to the start, a fast swing to the end, back to rest."""
    return arm_keys("right", [(0, None, None), (wind, start, None), (wind + 0.12, start, None),
                              (wind + 0.12 + swing, end, None), (wind + 0.32 + swing + hold, end, None), (1.3, None, None)])


def shield_hold(wrist, elbow, hold=1.2):
    """A shield block: the left hand raises the shield into place, holds it there, lowers it."""
    return arm_keys("left", [(0, None, None), (0.35, wrist, elbow), (0.35 + hold, wrist, elbow), (1.8, None, None)])


ARM_MOTIONS = {"bowl": bowl_arm, "swipe": swipe_arm, "scratch": scratch_arm, "cross": cross_arms,
               # Shpata! (sword in the right hand, shield in the left; the screen is a mirror, so the player's left,
               # x > 0, is the screen's left)
               "cut_right": sword_cut((0.25, 1.45, -0.35), (-0.75, 1.35, -0.35)),
               "cut_left": sword_cut((-0.75, 1.35, -0.35), (0.25, 1.45, -0.35)),
               "cut_down": sword_cut((-0.2, 2.0, -0.15), (-0.15, 1.0, -0.5)),
               "cut_up": sword_cut((-0.15, 1.0, -0.5), (-0.2, 2.0, -0.15)),
               "shield_over": shield_hold((0.0, 1.95, -0.3), (0.22, 1.75, -0.2)),
               "shield_left": shield_hold((0.45, 1.5, -0.35), (0.36, 1.2, -0.25)),
               "shield_right": shield_hold((-0.32, 1.48, -0.38), (0.02, 1.28, -0.3)),
               # hold on the colour column (right edge), long enough for a dwell selection, then back down
               "paint_pick": hand_path("right", [(0, None), (0.7, (0.955, 0.45)), (2.0, (0.955, 0.45)), (2.6, None)]),
               # the right hand goes to the start, the left hand switches on, one slow circle, switch off, lower
               "paint_circle": both(hand_path("right", [(0, None), (0.7, (0.78, 0.55)), (1.1, (0.78, 0.55)),
                                                        *circle_keys(1.1, (0.62, 0.55), 0.16, 0.2, 2.4)[1:],
                                                        (3.9, (0.78, 0.55)), (4.6, None)]),
                                    switch_hand("left", [(0.9, 3.6)])),
               # with the switch down the right hand crosses to a new spot (no line!), switch on, a line down, off
               "paint_line": both(hand_path("right", [(0, None), (0.8, (0.3, 0.75)), (1.2, (0.3, 0.75)),
                                                      (2.4, (0.3, 0.35)), (2.7, (0.3, 0.35)), (3.3, None)]),
                                  switch_hand("left", [(1.0, 2.45)]))}

# Whole-body offsets (dx, dy) in metres over time.
BODY_MOTIONS = {
    "sway": lambda t: (0.06 * math.sin(2 * math.pi * 0.5 * t), 0.0),
    "tiptoe": lambda t: (0.0, 0.04 * smoothstep(t / 0.6) * (1 - smoothstep((t - 1.2) / 0.6))),
}


def body_at(state, jump_y, arm=None):
    joints = {}
    for name, (x, y) in STAND.items():
        if name in HANDS_UP:
            hx, hy = HANDS_UP[name]
            x += (hx - x) * state["hands"]
            y += (hy - y) * state["hands"]
        c = state["crouch"]
        if name in UPPER_BODY:
            y -= 0.42 * c
        elif name.endswith("hip"):
            y -= 0.40 * c
        elif name.endswith("knee"):
            y -= 0.15 * c
            x += math.copysign(0.08 * c, x)
        z = 0.0
        if arm and name in arm:
            x, y, z = arm[name]
        joints[name] = (x + state["x"], y + jump_y, z)
    return joints


def to_person(joints_m, noise=0.003, world_noise=0.005):
    out, world = [], []
    hx = (joints_m["left_hip"][0] + joints_m["right_hip"][0]) / 2
    hy = (joints_m["left_hip"][1] + joints_m["right_hip"][1]) / 2
    hz = (joints_m["left_hip"][2] + joints_m["right_hip"][2]) / 2
    for name in JOINT_NAMES:
        x, y, z = joints_m[name]
        nx = 0.5 + x / VIEW_WIDTH_M + random.gauss(0, noise)
        ny = 0.95 - y / VIEW_HEIGHT_M + random.gauss(0, noise)
        nz = z / VIEW_WIDTH_M  # MediaPipe-style depth: same scale as x, negative = toward the camera
        out.append((nx, ny, nz, 0.98))
        # MediaPipe world joints: metres, origin at the hips, x image-right, y DOWN, z toward the camera = negative
        world.append((x - hx + random.gauss(0, world_noise), -(y - hy) + random.gauss(0, world_noise),
                      z - hz + random.gauss(0, world_noise)))
    return Person(out, world=world)


# ---- drawn camera picture for the camera preview stream (what a camera would see: not mirrored, no noise)

VIDEO_W, VIDEO_H = 640, 360
_room = None


def room_background():
    """A simple living room: wall, floor, window, sofa, picture. Drawn once (BGR, like a camera frame in OpenCV)."""
    global _room
    if _room is None:
        import cv2
        import numpy as np
        h, w = VIDEO_H, VIDEO_W
        img = np.zeros((h, w, 3), np.uint8)
        floor_y = int(0.80 * h)
        for y in range(h):
            k = y / h
            img[y] = (150 + 40 * k, 196 + 20 * k, 222 + 15 * k) if y < floor_y else (88, 120, 150)
        cv2.rectangle(img, (40, 50), (170, 190), (235, 225, 200), -1)          # window
        cv2.rectangle(img, (40, 50), (170, 190), (90, 90, 90), 4)
        cv2.line(img, (105, 50), (105, 190), (90, 90, 90), 3)
        cv2.rectangle(img, (440, 200), (620, 290), (160, 80, 70), -1)        # sofa
        cv2.rectangle(img, (440, 175), (620, 215), (140, 70, 60), -1)
        cv2.rectangle(img, (480, 70), (580, 140), (230, 160, 60), -1)        # picture
        cv2.rectangle(img, (480, 70), (580, 140), (40, 40, 40), 3)
        _room = img
    return _room.copy()


def render_camera(joints_m):
    """The player drawn over the room: trousers, shirt, bare forearms, head. Raw camera view (not mirrored)."""
    import cv2
    import numpy as np
    img = room_background()

    def px(name):
        x, y, _ = joints_m[name]
        return int((0.5 + x / VIEW_WIDTH_M) * VIDEO_W), int((0.95 - y / VIEW_HEIGHT_M) * VIDEO_H)

    limb = max(2, int(0.12 / VIEW_HEIGHT_M * VIDEO_H))
    skin, shirt, trousers, hair = (150, 180, 230), (200, 110, 50), (90, 60, 40), (40, 50, 70)
    for side in ("left", "right"):
        cv2.line(img, px(f"{side}_hip"), px(f"{side}_knee"), trousers, limb + 3, cv2.LINE_AA)
        cv2.line(img, px(f"{side}_knee"), px(f"{side}_ankle"), trousers, limb + 1, cv2.LINE_AA)
    torso = [px("left_shoulder"), px("right_shoulder"), px("right_hip"), px("left_hip")]
    cv2.fillConvexPoly(img, np.array(torso, np.int32), shirt, cv2.LINE_AA)
    for side in ("left", "right"):
        cv2.line(img, px(f"{side}_shoulder"), px(f"{side}_elbow"), shirt, limb + 2, cv2.LINE_AA)
        cv2.line(img, px(f"{side}_elbow"), px(f"{side}_wrist"), skin, limb, cv2.LINE_AA)
        cv2.circle(img, px(f"{side}_wrist"), limb // 2 + 2, skin, -1, cv2.LINE_AA)
    nose = px("nose")
    head_r = int(0.11 / VIEW_HEIGHT_M * VIDEO_H)
    cv2.circle(img, (nose[0], nose[1] - head_r // 3), head_r, skin, -1, cv2.LINE_AA)
    cv2.ellipse(img, (nose[0], nose[1] - head_r // 2), (head_r, head_r), 0, 180, 360, hair, -1, cv2.LINE_AA)
    return img


def main():
    p = argparse.ArgumentParser(description="Send a synthetic test player over UDP")
    p.add_argument("--host", default="127.0.0.1")
    p.add_argument("--port", type=int, default=5005)
    p.add_argument("--fps", type=float, default=30)
    p.add_argument("--once", action="store_true", help="play the script once instead of looping")
    p.add_argument("--script", choices=sorted(SCRIPTS), default="basic")
    p.add_argument("--video-control-port", type=int, default=CONTROL_PORT,
                   help="send a drawn camera picture while the game asks for one; 0 = never")
    args = p.parse_args()
    script = SCRIPTS[args.script]

    sender = UdpSender(args.host, args.port)
    video = VideoStreamer(args.video_control_port) if args.video_control_port else None
    fps_meter = FpsMeter()
    print(f"Fake player ({args.script}) -> udp://{args.host}:{args.port} at {args.fps:.0f} fps")
    frame = 0
    prev = dict(script[-1][2])
    period = 1.0 / args.fps
    next_tick = time.perf_counter()
    try:
        while True:
            for name, duration, target in script:
                try:
                    print(f"[{time.strftime('%H:%M:%S')}] {name}", flush=True)
                except OSError:
                    pass  # no console (e.g. started by the Unity tests): keep sending
                seg_start = time.perf_counter()
                while (t := time.perf_counter() - seg_start) < duration:
                    k = smoothstep(t / TRANSITION_S)
                    state = {key: prev[key] + (target[key] - prev[key]) * k for key in ("x", "crouch", "hands")}
                    jump_y = JUMP_HEIGHT_M * math.sin(math.pi * t / JUMP_TIME_S) if target.get("jump") and t < JUMP_TIME_S else 0.0
                    arm = ARM_MOTIONS[target["arm"]](t) if target.get("arm") else None
                    dx, dy = BODY_MOTIONS[target["move"]](t) if target.get("move") else (0.0, 0.0)
                    if target.get("hop"):
                        extra_crouch, hop_y = hop(t, *target["hop"])
                        state["crouch"] += extra_crouch
                        dy += hop_y
                    state["x"] += dx
                    joints_m = body_at(state, jump_y + dy, arm)
                    people = [] if target.get("absent") else [to_person(joints_m)]
                    t_ms = now_ms()
                    packet = build_packet("py-fake", frame, t_ms, WIDTH, HEIGHT, fps_meter.tick(), 0.0, people,
                                          camera_fps=30.0, model="fake")
                    sender.send(packet)
                    if video and video.wanted():
                        video.send(render_camera(joints_m) if people else room_background(), t_ms, frame)
                    frame += 1
                    next_tick += period
                    time.sleep(max(0.0, next_tick - time.perf_counter()))
                prev = {key: target[key] for key in ("x", "crouch", "hands")}
            if args.once:
                break
    except KeyboardInterrupt:
        pass
    finally:
        sender.close()
        if video:
            video.close()


if __name__ == "__main__":
    assert J["right_ankle"] == 16
    main()
