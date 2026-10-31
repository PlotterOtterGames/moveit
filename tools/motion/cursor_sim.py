"""Hand-cursor simulator: why the hand cursors stutter, and what fixes it, measured offline.

Mirrors the exact path a hand cursor takes in the game:
    camera (30 fps, noisy joints, uneven arrival) -> MotionSystem (every packet, in order) -> BodyTracker
    (One Euro, body-relative) -> BodyHands (zone mapping, latency prediction) -> a 60 fps game loop
and compares cursor schemes on realistic hand motions:
    rest     hand held still at chest height (jitter at rest)
    paint    a slow circle, 0.6 m/s (smoothness while painting, lag)
    stopgo   painting with stops and turns: move 0.5 s, stop 0.3 s (overshoot = sliding past the stop point)
    swipe    a fast Fruta swipe, 1.2 m in 0.25 s (peak speed must stay above Fruta's slash speed)
    zigzag   Fruta: slashing back and forth, 0.8 m at 1.4 Hz (~3.5 m/s)
Rooms: bright (30 fps, noise 0.003), dim (20 fps, noise 0.005, 60 ms latency, editor frame hiccups), a PC whose pose
model takes about a camera frame (frames skipped unevenly, PIPELINE = "inference"), and wrist-visibility dips (DIPS).

Metrics (px on a 1920x1080 screen, the zone mapped to the whole screen):
    stutter   RMS of (cursor step - true step) per frame while moving: 30 Hz steps on a 60 Hz screen show here
    worst     the largest single (cursor step - true step): snaps and jumps
    jitter    RMS of frame-to-frame movement while the hand is still
    lag       time shift that best aligns the cursor with the true hand; error = what's left at that shift
    HIDDEN    frames the cursor wasn't shown (hand dropped)

    motion-service-py/.venv/Scripts/python tools/motion/cursor_sim.py
"""

import argparse
import bisect
import json
import math
import random
import statistics

ASPECT = 16 / 9
U = 0.18                        # torso length in view units (image heights): a player ~2.5 m from a 100° camera
NOISE = 0.003                   # MediaPipe joint noise, normalized image coordinates (as in fake_sender)
CAM_FPS, GAME_FPS = 30.0, 60.0
LATENCY, LATENCY_JITTER = 0.035, 0.006
GAME_JITTER = 0.0015            # frame time jitter, s
HITCH_EVERY = 0.0               # a long frame (editor hiccup) about this often, s (0 = never)
SCREEN_W, SCREEN_H = 1920, 1080
ZONE_HALF_W, ZONE_BELOW_HIPS, ZONE_ABOVE_SHOULDERS, HAND_EXT = 1.6, 0.3, 1.3, 0.3
M_PER_UNIT = 2.6                # metres per view unit at the player's distance


class OneEuro:
    """Same as MoveSDK/Runtime/OneEuroFilter.cs."""

    def __init__(self, min_cutoff, beta, d_cutoff=1.0):
        self.min_cutoff, self.beta, self.d_cutoff = min_cutoff, beta, d_cutoff
        self.x = self.dx = 0.0
        self.init = False

    @staticmethod
    def alpha(cutoff, dt):
        tau = 1 / (2 * math.pi * cutoff)
        return 1 / (1 + tau / dt)

    def __call__(self, value, dt, scale=1.0):
        if not self.init or dt <= 0:
            self.x, self.dx, self.init = value, 0.0, True
            return value
        raw = (value - self.x) / dt
        self.dx += (raw - self.dx) * self.alpha(self.d_cutoff, dt)
        cutoff = self.min_cutoff + self.beta * abs(self.dx) / max(scale, 1e-4)
        self.x += (value - self.x) * self.alpha(cutoff, dt)
        return self.x


class Filter2:
    def __init__(self, *a):
        self.fx, self.fy = OneEuro(*a), OneEuro(*a)

    def __call__(self, p, dt, scale=1.0):
        return self.fx(p[0], dt, scale), self.fy(p[1], dt, scale)

    @property
    def velocity(self):
        return self.fx.dx, self.fy.dx

    def reset(self):
        self.fx.init = self.fy.init = False


# ---------------------------------------------------------------- the body (view units, y up)

SHOULDER_C, HIP_C = (0.89, 0.62), (0.89, 0.62 - U)
R_SHOULDER = (SHOULDER_C[0] + 0.07, SHOULDER_C[1])   # the player's right hand appears on the screen's right


def min_jerk(t):
    t = max(0.0, min(1.0, t))
    return 10 * t ** 3 - 15 * t ** 4 + 6 * t ** 5


def wrist_at(motion, t):
    if motion == "rest":
        return (1.03, 0.66)
    if motion == "paint":                                 # radius 0.09 units = 0.23 m, 0.42 rev/s = 0.6 m/s
        a = 2 * math.pi * 0.42 * t
        return (1.03 + 0.09 * math.cos(a), 0.66 + 0.09 * math.sin(a))
    if motion == "swipe":                                 # 1.2 m left to right in 0.25 s, then hold
        k = min_jerk((t - 1.0) / 0.25)
        return (0.70 + (1.16 - 0.70) * k, 0.68)
    if motion == "stopgo":                                # painting: move 0.5 s, stop 0.3 s, change direction
        return stopgo(t)[0]
    if motion == "zigzag":                                # Fruta: slashing back and forth, 0.8 m at 1.4 Hz (~3.5 m/s)
        a = math.sin(2 * math.pi * 1.4 * max(0.0, t - 1.0))
        return (0.93 + 0.155 * a, 0.70 + 0.04 * a)
    raise ValueError(motion)


STOPGO_POINTS = [(1.00, 0.70), (1.13, 0.70), (1.13, 0.57), (0.97, 0.60), (1.05, 0.76)]
STOPGO_MOVE, STOPGO_HOLD, STOPGO_START = 0.5, 0.3, 1.0


def stopgo(t):
    """Wrist on a stop-and-go path; also returns (stop time, stop point, direction) of the leg it's on."""
    pts = STOPGO_POINTS
    t -= STOPGO_START
    leg = int(max(0.0, t) // (STOPGO_MOVE + STOPGO_HOLD))
    a, b = pts[leg % len(pts)], pts[(leg + 1) % len(pts)]
    u = max(0.0, t) - leg * (STOPGO_MOVE + STOPGO_HOLD)
    k = min_jerk(u / STOPGO_MOVE)
    stop = STOPGO_START + leg * (STOPGO_MOVE + STOPGO_HOLD) + STOPGO_MOVE
    d = (b[0] - a[0], b[1] - a[1])
    n = math.hypot(*d)
    return (a[0] + d[0] * k, a[1] + d[1] * k), stop, b, (d[0] / n, d[1] / n)


def elbow_for(w):
    return ((R_SHOULDER[0] + w[0]) / 2, (R_SHOULDER[1] + w[1]) / 2 - 0.03)


def true_tip(w):
    e = elbow_for(w)
    return (w[0] + (w[0] - e[0]) * HAND_EXT, w[1] + (w[1] - e[1]) * HAND_EXT)


def to_screen(zone):
    if MAP == "mirror":
        kx = MIRROR_PX_PER_TORSO * 2 * ZONE_HALF_W
        ky = MIRROR_PX_PER_TORSO * (1 + ZONE_BELOW_HIPS + ZONE_ABOVE_SHOULDERS)
        return SCREEN_W / 2 + (zone[0] - 0.5) * kx, SCREEN_H / 2 + (0.5 - zone[1]) * ky
    return zone[0] * SCREEN_W, (1 - zone[1]) * SCREEN_H


def zone_of(tip, shoulders, hips, unit):
    bottom = hips[1] - ZONE_BELOW_HIPS * unit
    top = shoulders[1] + ZONE_ABOVE_SHOULDERS * unit
    return 0.5 + (tip[0] - shoulders[0]) / (2 * ZONE_HALF_W * unit), (tip[1] - bottom) / max(0.01, top - bottom)


# ---------------------------------------------------------------- the pipeline

class Tracker:
    """BodyTracker for the joints a cursor needs: smooth and fast tracks, FastVelocity."""
    JOINTS = ("ls", "rs", "lh", "rh", "elbow", "wrist")

    def __init__(self):
        self.fast = {j: Filter2(1.0, 4.0) for j in self.JOINTS}
        self.smooth = {j: Filter2(0.5, 1.5) for j in self.JOINTS}
        self.f, self.s, self.v = {}, {}, {}
        self.scale = 0.0
        self.t = None
        self.vis = 0.95                                   # the wrist's visibility in the latest sample

    def update(self, joints, t, vis=0.95):
        dt = t - self.t if self.t is not None else 0.0
        self.t = t
        self.vis = vis
        torso = math.dist(mid(joints["ls"], joints["rs"]), mid(joints["lh"], joints["rh"]))
        self.scale = torso if self.scale <= 0 else self.scale + (max(torso, 0.02) - self.scale) * 0.05
        self.raw = joints
        for j, p in joints.items():
            self.f[j] = self.fast[j](p, dt, self.scale)
            self.v[j] = self.fast[j].velocity
            self.s[j] = self.smooth[j](p, dt, self.scale)


def mid(a, b):
    return (a[0] + b[0]) / 2, (a[1] + b[1]) / 2


# Noise measured in a real session (motion-service-py/sessions/paint.jsonl, 2026-10-20: laptop webcam in a dim room,
# 20 fps): the hand point's residual from a 7-sample parabola was 0.5-0.9 % of a torso length with the hand still,
# 2.4-3 % moving slowly (painting), 8-15 % fast: the camera gets far noisier while the hand moves (motion blur at long
# exposures). None = the old constant NOISE; else (still, per torso length per second) in torso lengths, per axis,
# for the arm joints (the body joints get the still part), and it replaces MP_SMOOTH (it was measured after it).
SPEED_NOISE = None
DIM_ROOM = (0.005, 0.010)           # calibrated with residual_check() against the recording
# The same recording, by the wrist's visibility at painting speeds: >= 0.9 0.92 %, 0.7-0.9 3.0 %, 0.5-0.7 3.6 %,
# 0.3-0.5 5.1 %: unsure frames are 3-5x noisier. VIS_MODEL draws each sample's visibility (85 / 6.5 / 6 / 3 % of the
# usable samples, more unsure ones when the hand moves fast, in runs) with that much noise, the base noise lowered so
# the whole stays calibrated (DIM_ROOM / 1.83).
VIS_MODEL = False
VIS_BANDS = ((0.97, 1.0), (0.8, 3.3), (0.6, 3.9), (0.4, 5.6))      # (visibility, noise x)
VIS_BASE = (0.0027, 0.0055)


def sample_joints(motion, t, rng, noise_scale=1.0):
    w = wrist_at(motion, t)
    true = {"ls": (SHOULDER_C[0] - 0.07, SHOULDER_C[1]), "rs": R_SHOULDER,
            "lh": (HIP_C[0] - 0.04, HIP_C[1]), "rh": (HIP_C[0] + 0.04, HIP_C[1]),
            "elbow": elbow_for(w), "wrist": w}
    if SPEED_NOISE:
        still, per_speed = VIS_BASE if VIS_MODEL else SPEED_NOISE
        v = wrist_speed(motion, t) / M_PER_UNIT / U                     # torso lengths per second
        arm = (still + per_speed * v) * U * noise_scale
        return {j: (x + rng.gauss(0, arm if j in ("elbow", "wrist") else still * U),
                    y + rng.gauss(0, arm if j in ("elbow", "wrist") else still * U)) for j, (x, y) in true.items()}
    return {j: (x + rng.gauss(0, NOISE * ASPECT), y + rng.gauss(0, NOISE)) for j, (x, y) in true.items()}


def residual_check(motion="paint", seconds=20.0, seed=3):
    """The recording's noise measure on simulated samples (to calibrate SPEED_NOISE): rms residual of the hand point
    from a 7-sample parabola, % of a torso length, by hand speed."""
    rng = random.Random(seed)
    pts = []
    for item in capture_times(seconds, rng):
        c = item[1]
        j = sample_joints(motion, c, rng)
        w, e = j["wrist"], j["elbow"]
        pts.append((c, (w[0] + (w[0] - e[0]) * HAND_EXT, w[1] + (w[1] - e[1]) * HAND_EXT), wrist_speed(motion, c) / M_PER_UNIT / U))
    bands = {"still": [], "slow": [], "fast": []}
    for i in range(3, len(pts) - 3):
        win = pts[i - 3:i + 4]
        ts = [q[0] - win[3][0] for q in win]
        r = []
        for k in range(2):
            coef = solve([[sum(t ** (a + b) for t in ts) for b in range(3)] for a in range(3)],
                         [sum(q[1][k] * t ** a for q, t in zip(win, ts)) for a in range(3)])
            r.append(win[3][1][k] - sum(cf * ts[3] ** a for a, cf in enumerate(coef)))
        v = win[3][2]
        bands["still" if v < 0.3 else "slow" if v < 1.5 else "fast"].append(math.hypot(*r) / U)
    return {k: 100 * math.sqrt(statistics.fmean(x * x for x in xs)) for k, xs in bands.items() if xs}


class CurrentHands:
    """BodyHands today: recomputed only when a new camera sample arrives, predicted ahead by the latency,
    the zone anchored on the low-lag (fast) shoulders and hips."""

    def __init__(self):
        self.last_t = None
        self.unit = 0.0
        self.pos = None
        self.vel = (0.0, 0.0)

    def frame(self, tr, now, lead, frame_dt, new_sample):
        if new_sample:
            dt = tr.t - self.last_t if self.last_t is not None else 0.0
            if not (0 < dt < 0.012):
                self.last_t = tr.t
                self.unit = fast_torso(tr) if self.unit <= 0 else self.unit + (fast_torso(tr) - self.unit) * 0.1
                w = add(tr.f["wrist"], mul(tr.v["wrist"], lead))
                e = add(tr.f["elbow"], mul(tr.v["elbow"], lead))
                tip = add(w, mul(sub(w, e), HAND_EXT))
                pos = zone_of(tip, mid(tr.f["ls"], tr.f["rs"]), mid(tr.f["lh"], tr.f["rh"]), self.unit)
                if self.pos is not None and dt > 0:
                    raw = mul(sub(pos, self.pos), 1 / dt)
                    self.vel = add(mul(raw, 0.6), mul(self.vel, 0.4))
                self.pos = pos
        return self.pos


def fast_torso(tr):
    return math.dist(mid(tr.f["ls"], tr.f["rs"]), mid(tr.f["lh"], tr.f["rh"]))


def add(a, b): return a[0] + b[0], a[1] + b[1]
def sub(a, b): return a[0] - b[0], a[1] - b[1]
def mul(a, k): return a[0] * k, a[1] * k


class SmoothedHands(CurrentHands):
    """Today's cursor plus a per-frame One Euro on top (what Pikturo did on 2026-10-08)."""

    def __init__(self, min_cutoff=1.5, beta=4.0):
        super().__init__()
        self.out = Filter2(min_cutoff, beta)

    def frame(self, tr, now, lead, frame_dt, new_sample):
        p = super().frame(tr, now, lead, frame_dt, new_sample)
        return None if p is None else self.out(p, frame_dt)


class ContinuousHands:
    """Proposed BodyHands: the zone is anchored on the smooth body (shoulders/hips jitter no longer shakes every
    cursor), each camera sample gives a position and a velocity, and every game frame the cursor is extrapolated to
    "now" (time since that sample + the pipeline latency) and smoothed by a One Euro filter that runs at the frame
    rate. So it moves every frame instead of in 30 Hz steps, and corrections when the next sample lands are eased."""

    def __init__(self, min_cutoff=3.0, beta=1.5, d_cutoff=2.0, max_ahead=0.05, anchor="smooth", lead_scale=1.0):
        self.unit = 0.0
        self.p = self.v = None
        self.recv = 0.0
        self.last_t = None
        self.out = Filter2(min_cutoff, beta, d_cutoff)
        self.max_ahead, self.anchor, self.lead_scale = max_ahead, anchor, lead_scale

    def frame(self, tr, now, lead, frame_dt, new_sample):
        if new_sample and tr.t != self.last_t:
            self.last_t = tr.t
            src = tr.s if self.anchor == "smooth" else tr.f
            torso = math.dist(mid(src["ls"], src["rs"]), mid(src["lh"], src["rh"]))
            self.unit = torso if self.unit <= 0 else self.unit + (torso - self.unit) * 0.1
            sh, hp = mid(src["ls"], src["rs"]), mid(src["lh"], src["rh"])
            w, e = tr.f["wrist"], tr.f["elbow"]
            tip = add(w, mul(sub(w, e), HAND_EXT))
            vt = add(tr.v["wrist"], mul(sub(tr.v["wrist"], tr.v["elbow"]), HAND_EXT))
            self.p = zone_of(tip, sh, hp, self.unit)
            span_y = (sh[1] + ZONE_ABOVE_SHOULDERS * self.unit) - (hp[1] - ZONE_BELOW_HIPS * self.unit)
            self.v = (vt[0] / (2 * ZONE_HALF_W * self.unit), vt[1] / span_y)
            self.recv = now
        if self.p is None:
            return None
        ahead = lead * self.lead_scale + min(now - self.recv, self.max_ahead)
        return self.out(add(self.p, mul(self.v, ahead)), frame_dt)


class KalmanHands(ContinuousHands):
    """Like the proposal, but each axis is tracked by a constant-velocity Kalman filter on the camera samples
    (position + velocity, the noise level known), predicted to "now" every frame, then lightly smoothed."""

    def __init__(self, accel=6.0, meas=0.0095, out_cutoff=6.0, out_beta=0.5, lead_scale=1.0):
        super().__init__(min_cutoff=out_cutoff, beta=out_beta, lead_scale=lead_scale)
        self.accel, self.meas = accel, meas          # zone units/s^2 of hand acceleration; zone units of noise
        self.kx = self.ky = None
        self.kt = None

    def frame(self, tr, now, lead, frame_dt, new_sample):
        if new_sample and tr.t != self.last_t:
            self.last_t = tr.t
            src = tr.s
            torso = math.dist(mid(src["ls"], src["rs"]), mid(src["lh"], src["rh"]))
            self.unit = torso if self.unit <= 0 else self.unit + (torso - self.unit) * 0.1
            sh, hp = mid(src["ls"], src["rs"]), mid(src["lh"], src["rh"])
            w, e = tr.raw["wrist"], tr.raw["elbow"]                 # raw camera joints: the Kalman does the smoothing
            z = zone_of(add(w, mul(sub(w, e), HAND_EXT)), sh, hp, self.unit)
            if self.kx is None:
                self.kx, self.ky = Kalman1(z[0]), Kalman1(z[1])
            else:
                dt = tr.t - self.kt
                for k, v in ((self.kx, z[0]), (self.ky, z[1])):
                    k.predict(dt, self.accel)
                    k.update(v, self.meas)
            self.kt = tr.t
            self.recv = now
            self.p = (self.kx.x, self.ky.x)
            self.v = (self.kx.v, self.ky.v)
        if self.p is None:
            return None
        ahead = lead * self.lead_scale + min(now - self.recv, self.max_ahead)
        return self.out(add(self.p, mul(self.v, ahead)), frame_dt)


class Kalman1:
    def __init__(self, x):
        self.x, self.v = x, 0.0
        self.P = [[1e-2, 0.0], [0.0, 1.0]]

    def predict(self, dt, accel):
        self.x += self.v * dt
        P = self.P
        q = accel * accel
        p00 = P[0][0] + dt * (P[1][0] + P[0][1]) + dt * dt * P[1][1] + q * dt ** 4 / 4
        p01 = P[0][1] + dt * P[1][1] + q * dt ** 3 / 2
        p11 = P[1][1] + q * dt * dt
        self.P = [[p00, p01], [p01, p11]]

    def update(self, z, meas):
        P = self.P
        s = P[0][0] + meas * meas
        k0, k1 = P[0][0] / s, P[1][0] / s
        y = z - self.x
        self.x += k0 * y
        self.v += k1 * y
        self.P = [[(1 - k0) * P[0][0], (1 - k0) * P[0][1]], [P[1][0] - k1 * P[0][0], P[1][1] - k1 * P[0][1]]]


class SmoothTrackHands(ContinuousHands):
    """The proposal fed with the smooth track (the one the skeleton mirror draws) instead of the low-lag one."""

    def frame(self, tr, now, lead, frame_dt, new_sample):
        saved = tr.f
        tr.f = tr.s
        try:
            return super().frame(tr, now, lead, frame_dt, new_sample)
        finally:
            tr.f = saved


# ---------------------------------------------------------------- running it

PIPELINE = "regular"            # "regular": a sample every 1/CAM_FPS; "inference": main.py on a PC, see below; "pc": the
                                # laptop as recorded (see capture_times)
INFERENCE, INFERENCE_JITTER = 0.031, 0.006
LOOP_EXTRA, LOOP_EXTRA_JITTER = 0.020, 0.002   # "pc": the rest of main.py's loop after the packet is sent (preview etc.)
DIPS = (0.0, 0.0)               # chance that a sample's wrist visibility dips below 0.5: hand slow, hand fast (> 1.5 m/s)
MP_SMOOTH = False               # pass the noisy joints through MediaPipe's own landmark filter (see MediaPipeEuro)
MP_TIME = "capture"             # the timestamps MediaPipe's filter gets: "capture" or "detect" (main.py before 2026-10-19)
MAP = "zone"                    # where the cursor is drawn: "zone" = BodyHands' hand zone over the whole screen (Pikturo's
                                # paper), "mirror" = the camera picture framed knees to above the head (Vishu!, Pikturo's
                                # camera mode: 327 px per torso length both ways, vs 600 x 415 in the zone)
MIRROR_PX_PER_TORSO = 1080 / 3.3


def capture_times(seconds, rng):
    """(arrival, capture) times of the samples. "inference": the camera delivers CAM_FPS, the pose model takes about
    as long as a camera frame and always takes the newest one (main.py's FrameGrabber), so frames are skipped unevenly.
    "pc": the laptop in motion-service-py/sessions/punches.jsonl (2026-10-15): a 60 fps camera, inference ~13 ms, but
    main.py's whole loop (preview window included) ~40 ms, so it takes every 2nd camera frame and now and then the 3rd:
    gaps of 33 ms and ~9% of 50 ms. The packet leaves right after inference."""
    if PIPELINE == "pc":
        frames = [k / CAM_FPS for k in range(int(seconds * CAM_FPS))]
        out, t, done = [], 0.0, -1.0
        for f in frames:
            ready = f + 0.008                                       # 8 ms to get the frame off the camera
            if f <= done or ready < t and any(g > f and g + 0.008 <= t for g in frames):
                continue                                            # a newer frame is waiting: this one is skipped
            start = max(t, ready)
            inference = max(0.006, rng.gauss(INFERENCE, INFERENCE_JITTER))
            out.append((start + inference + 0.002, f, start))
            t = start + inference + max(0.0, rng.gauss(LOOP_EXTRA, LOOP_EXTRA_JITTER))
            done = f
        return out
    if PIPELINE == "regular":
        captures, t = [], 0.0
        while t < seconds:
            captures.append(t)
            t += 1 / CAM_FPS + rng.gauss(0, 0.002)
        arrivals, last = [], 0.0
        for c in captures:
            last = max(last + 0.001, c + max(0.015, rng.gauss(LATENCY, LATENCY_JITTER)))
            arrivals.append(last)
        return list(zip(arrivals, captures))
    frames = [k / CAM_FPS for k in range(int(seconds * CAM_FPS))]
    out, t, done = [], 0.0, -1.0
    while True:
        ready = [f for f in frames if f > done and f + 0.008 <= t]   # 8 ms to get the frame off the camera
        if not ready:
            later = [f for f in frames if f > done]
            if not later:
                return out
            t = later[0] + 0.008
            continue
        done = ready[-1]
        t += max(0.012, rng.gauss(INFERENCE, INFERENCE_JITTER))
        out.append((t + 0.002, done))


def wrist_speed(motion, t):
    a, b = wrist_at(motion, t - 0.005), wrist_at(motion, t + 0.005)
    return math.dist(a, b) / 0.01 * M_PER_UNIT


class MediaPipeEuro:
    """MediaPipe's own landmark smoothing, on in VIDEO mode with one pose (pose_landmarks_detector_graph.cc): One Euro,
    min cutoff 0.05 Hz, beta 80, derivative cutoff 1 Hz, on pixels, the speed in object scales (the pose's ROI) per
    second and taken from the last *raw* value (mediapipe/util/filtering/one_euro_filter.cc). Its dt comes from the
    timestamps passed to detect_for_video."""

    def __init__(self):
        self.x = self.raw = self.dx = self.t = None
        self.freq = 30.0

    def alpha(self, cutoff):
        return 1 / (1 + self.freq / (2 * math.pi * cutoff))

    def __call__(self, value, t, value_scale):
        if self.t is not None:
            if t <= self.t:
                return value
            self.freq = 1 / (t - self.t)
        self.t = t
        d = 0.0 if self.raw is None else (value - self.raw) * value_scale * self.freq
        self.raw = value
        self.dx = d if self.dx is None else self.dx + (d - self.dx) * self.alpha(1.0)
        a = self.alpha(0.05 + 80 * abs(self.dx))
        self.x = value if self.x is None else self.x + (value - self.x) * a
        return self.x


def run(make_hands, motion, seconds=6.0, seed=1, stats=None):
    rng = random.Random(seed)
    packets = []
    mp = {}
    unsure_run = [False]
    image_h = 720.0
    roi = 3.9 * U * image_h                                # the pose ROI (body + margin) in pixels
    for item in capture_times(seconds, rng):
        a, c = item[0], item[1]
        vis, scale = 0.95, 1.0
        if VIS_MODEL:
            fast = wrist_speed(motion, c) / M_PER_UNIT / U > 1.5
            # a Markov chain: 15 % unsure frames when slow, 31 % fast, in runs (an unsure frame is followed by another 60 %)
            unsure = rng.random() < (0.6 if unsure_run[0] else 0.18 if fast else 0.07)
            unsure_run[0] = unsure
            r = rng.random()
            band = VIS_BANDS[0] if not unsure else VIS_BANDS[1 if r < 0.42 else 2 if r < 0.81 else 3]
            vis, scale = band
        joints = sample_joints(motion, c, rng, scale)
        if MP_SMOOTH and not SPEED_NOISE:
            ts = c if MP_TIME == "capture" else (item[2] if len(item) > 2 else a - 0.002)
            for j, (x, y) in joints.items():
                fx, fy = mp.setdefault(j, (MediaPipeEuro(), MediaPipeEuro()))
                joints[j] = (fx(x * image_h, ts, 1 / roi) / image_h, fy(y * image_h, ts, 1 / roi) / image_h)
        if any(DIPS) and rng.random() < DIPS[wrist_speed(motion, c) > 1.5]:
            vis = 0.4
        packets.append((a, c, joints, vis))

    tracker, hands = Tracker(), make_hands()
    latency_ms, k, out, hidden = 0.0, 0, [], 0
    frame_t, prev_frame = 0.0, None
    while frame_t < seconds:
        new = False
        while k < len(packets) and packets[k][0] <= frame_t:
            _, cap, joints, vis = packets[k]
            lat = (frame_t - cap) * 1000
            latency_ms = lat if latency_ms <= 0 else latency_ms + (lat - latency_ms) * 0.1
            tracker.update(joints, cap, vis)
            if getattr(hands, "per_packet", False):
                hands.packet(tracker, frame_t)
            k += 1
            new = True
        lead = min(max(latency_ms / 1000, 0), 0.08)
        dt = frame_t - prev_frame if prev_frame is not None else 0.0
        p = hands.frame(tracker, frame_t, lead, dt, new) if tracker.t is not None else None
        if p is None and tracker.t is not None and frame_t > 1.0 / 3:
            hidden += 1
        if p is not None and frame_t > 1.0 / 3:            # skip the first few samples (filters settling)
            unit = U
            truth = zone_of(true_tip(wrist_at(motion, frame_t)), SHOULDER_C, HIP_C, unit)
            out.append((frame_t, to_screen(p), to_screen(truth), getattr(hands, "vel", None)))
        prev_frame = frame_t
        frame_t += 1 / GAME_FPS + rng.gauss(0, GAME_JITTER)
        if HITCH_EVERY and rng.random() < (1 / GAME_FPS) / HITCH_EVERY:
            frame_t += 0.035                              # an editor hiccup: one frame takes ~50 ms
    if stats is not None:
        stats["hidden frames"] = hidden
    return out


def metrics(motion, samples):
    ts = [s[0] for s in samples]
    cur = [s[1] for s in samples]
    if motion == "rest":
        steps = [math.dist(a, b) for a, b in zip(cur, cur[1:])]
        cx, cy = statistics.fmean(p[0] for p in cur), statistics.fmean(p[1] for p in cur)
        spread = math.sqrt(statistics.fmean((p[0] - cx) ** 2 + (p[1] - cy) ** 2 for p in cur))
        return {"jitter px/frame": rms(steps), "wander px": spread}
    window = [i for i, t in enumerate(ts) if (1.2 <= t <= 5.5 if motion in ("paint", "stopgo", "zigzag") else 0.8 <= t <= 2.0)]
    best = None
    for lag_ms in range(0, 301, 5):
        lag = lag_ms / 1000
        err = statistics.fmean(math.dist(cur[i], to_screen(zone_of(true_tip(wrist_at(motion, ts[i] - lag)), SHOULDER_C, HIP_C, U)))
                               for i in window)
        if best is None or err < best[1]:
            best = (lag_ms, err)
    lag = best[0] / 1000
    truth = [to_screen(zone_of(true_tip(wrist_at(motion, ts[i] - lag)), SHOULDER_C, HIP_C, U)) for i in window]
    c = [cur[i] for i in window]
    step_errors = [math.dist(c[j], c[j - 1]) - math.dist(truth[j], truth[j - 1]) for j in range(1, len(c))]
    out = {"stutter px/frame": rms(step_errors), "worst px": max(map(abs, step_errors)), "lag ms": best[0], "error px": best[1]}
    if motion == "stopgo":
        out["overshoot px"] = overshoot(ts, cur)
        out["short px"] = settle(ts, cur)
    if motion == "swipe":
        speeds = [math.dist(cur[i], cur[i - 1]) / (ts[i] - ts[i - 1]) / SCREEN_W for i in range(1, len(cur)) if 0.8 <= ts[i] <= 2.0]
        out["peak screens/s"] = max(speeds)
    return out


def overshoot(ts, cur):
    """Mean of the worst slide past each stop point (px, along the approach direction): rubber-banding."""
    worst = {}
    for t, c in zip(ts, cur):
        _, stop, point, d = stopgo(t - 0.15)      # the leg the cursor is finishing (it lags)
        if not (stop - 0.1 <= t <= stop + 0.45):
            continue
        sp = to_screen(zone_of(true_tip(point), SHOULDER_C, HIP_C, U))
        ds = (d[0], -d[1])                        # screen y points down
        past = (c[0] - sp[0]) * ds[0] + (c[1] - sp[1]) * ds[1]
        worst[stop] = max(worst.get(stop, 0.0), past)
    return statistics.fmean(worst.values()) if worst else float("nan")


def settle(ts, cur):
    """Mean distance from each stop point just before the hand moves on (px): where a line really ends."""
    gaps = {}
    for t, c in zip(ts, cur):
        _, stop, point, _ = stopgo(t)
        if stop + STOPGO_HOLD - 0.03 <= t <= stop + STOPGO_HOLD - 0.01:
            gaps[stop] = math.dist(c, to_screen(zone_of(true_tip(point), SHOULDER_C, HIP_C, U)))
    return statistics.fmean(gaps.values()) if gaps else float("nan")


def from_screen(px):
    if MAP == "mirror":
        kx = MIRROR_PX_PER_TORSO * 2 * ZONE_HALF_W
        ky = MIRROR_PX_PER_TORSO * (1 + ZONE_BELOW_HIPS + ZONE_ABOVE_SHOULDERS)
        return 0.5 + (px[0] - SCREEN_W / 2) / kx, 0.5 - (px[1] - SCREEN_H / 2) / ky
    return px[0] / SCREEN_W, 1 - px[1] / SCREEN_H


class Rope:
    """A brush stabilizer from drawing programs (Krita's stabilizer, Procreate's StreamLine, Lazy Nezumi, SAI): the
    brush tip hangs on a string of `radius` px behind the cursor and moves only when the string is pulled tight, so
    a hand held still paints nothing new and shake smaller than the string never reaches the paint. Lines end
    `radius` short of where the cursor stopped unless `settle` (s) pulls the tip in once the cursor rests."""

    def __init__(self, inner, radius, settle=0.0):
        self.inner, self.radius, self.settle_s = inner, radius, settle
        self.tip = None
        self.per_packet = getattr(inner, "per_packet", False)

    def packet(self, tr, now):
        self.inner.packet(tr, now)

    def frame(self, tr, now, lead, frame_dt, new_sample):
        p = self.inner.frame(tr, now, lead, frame_dt, new_sample)
        if p is None:
            self.tip = None
            return None
        c = to_screen(p)
        if self.tip is None:
            self.tip = c
        d = math.dist(c, self.tip)
        if d > self.radius:
            self.tip = add(self.tip, mul(sub(c, self.tip), 1 - self.radius / d))
        elif self.settle_s > 0 and frame_dt > 0:
            self.tip = add(self.tip, mul(sub(c, self.tip), 1 - math.exp(-frame_dt / self.settle_s)))
        return from_screen(self.tip)


def rms(xs):
    return math.sqrt(statistics.fmean(x * x for x in xs)) if xs else float("nan")


SCHEMES = {
    "today (BodyHands)": CurrentHands,
    "today + Pikturo's smoothing": SmoothedHands,
    "proposed: per frame, smooth anchor": lambda: ContinuousHands(),
    "proposed, fast anchor": lambda: ContinuousHands(anchor="fast"),
    "proposed, calmer (cut 2, beta 1)": lambda: ContinuousHands(min_cutoff=2.0, beta=1.0),
    "proposed, livelier (cut 4, beta 2)": lambda: ContinuousHands(min_cutoff=4.0, beta=2.0),
    "proposed on the smooth track": lambda: SmoothTrackHands(),
    "Kalman (accel 6)": lambda: KalmanHands(),
    "Kalman (accel 3)": lambda: KalmanHands(accel=3.0),
    "Kalman (accel 12)": lambda: KalmanHands(accel=12.0),
    "Kalman (accel 6), output cut 3": lambda: KalmanHands(out_cutoff=3.0),
}


class InterpHands(ContinuousHands):
    """Draws the past instead of guessing the future: the cursor is placed between the last two camera samples, one
    sample interval behind (plus the measured latency), so it moves evenly every frame and never overshoots a stop.
    Costs about one camera frame of lag; then a light per-frame One Euro for the camera noise."""

    def __init__(self, min_cutoff=3.0, beta=0.5, d_cutoff=1.0, margin=1.2):
        super().__init__(min_cutoff=min_cutoff, beta=beta, d_cutoff=d_cutoff)
        self.samples = []                         # (capture time, zone position), oldest first
        self.interval = 1 / CAM_FPS
        self.margin = margin

    def frame(self, tr, now, lead, frame_dt, new_sample):
        if new_sample and tr.t != self.last_t:
            if self.last_t is not None:
                self.interval += (min(0.1, tr.t - self.last_t) - self.interval) * 0.1
            self.last_t = tr.t
            src = tr.s
            torso = math.dist(mid(src["ls"], src["rs"]), mid(src["lh"], src["rh"]))
            self.unit = torso if self.unit <= 0 else self.unit + (torso - self.unit) * 0.1
            w, e = tr.f["wrist"], tr.f["elbow"]
            tip = add(w, mul(sub(w, e), HAND_EXT))
            self.samples.append((tr.t, zone_of(tip, mid(src["ls"], src["rs"]), mid(src["lh"], src["rh"]), self.unit)))
            self.samples = self.samples[-4:]
        if not self.samples:
            return None
        # "now" on the camera's clock is now - latency; draw a margin of sample intervals before that
        td = now - lead - self.margin * self.interval
        pts = self.samples
        if td <= pts[0][0]:
            p = pts[0][1]
        elif td >= pts[-1][0]:
            p = pts[-1][1]
        else:
            for (t0, a), (t1, b) in zip(pts, pts[1:]):
                if t0 <= td <= t1:
                    k = (td - t0) / (t1 - t0)
                    p = (a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k)
                    break
        return self.out(p, frame_dt)


def brush_report():
    """Pikturo's brush cursor (smooth lines matter more than speed) in a normal and a dim room."""
    global NOISE, CAM_FPS, LATENCY, LATENCY_JITTER, GAME_JITTER, HITCH_EVERY
    rooms = {
        "bright room": dict(NOISE=0.003, CAM_FPS=30.0, LATENCY=0.035, LATENCY_JITTER=0.006, GAME_JITTER=0.0015, HITCH_EVERY=0.0),
        "dim room + editor hiccups": dict(NOISE=0.005, CAM_FPS=20.0, LATENCY=0.06, LATENCY_JITTER=0.012, GAME_JITTER=0.003, HITCH_EVERY=2.0),
    }
    schemes = {
        "cursor today (extrapolate, 2/2/2, glide 50 ms)": lambda: ContinuousHands(2.0, 2.0, 2.0),
        "  + brush filter 3/0.5 (Pikturo today)": lambda: Chain(ContinuousHands(2.0, 2.0, 2.0), 3.0, 0.5),
        "extrapolate, glide 100 ms + brush filter": lambda: Chain(ContinuousHands(2.0, 2.0, 2.0, max_ahead=0.1), 3.0, 0.5),
        "interpolate (1.2 intervals) + 3/0.5": lambda: InterpHands(3.0, 0.5),
        "interpolate (1.2 intervals) + 4/0.5": lambda: InterpHands(4.0, 0.5),
        "interpolate (1.2 intervals) + 2/0.5": lambda: InterpHands(2.0, 0.5),
        "interpolate (1.5 intervals) + 3/0.5": lambda: InterpHands(3.0, 0.5, margin=1.5),
    }
    saved = {k: globals()[k] for k in rooms["bright room"]}
    try:
        for room, conditions in rooms.items():
            globals().update(conditions)
            print(f"\n{room}:")
            for name, make in schemes.items():
                m = {}
                for motion in ("rest", "stopgo"):
                    for seed in range(1, 6):
                        for k, v in metrics(motion, run(make, motion, seed=seed)).items():
                            m.setdefault(k, []).append(v)
                print(f"    {name:48s} " + ", ".join(f"{k} {statistics.fmean(v):.1f}" for k, v in m.items()))
    finally:
        globals().update(saved)


class ExtrapFD(ContinuousHands):
    """BodyHands since 2026-10-09: glides on with the speed from the last two samples (smoothed 0.4, like BodyHands'
    velocitySmoothing) instead of the tracker's lagging smoothed speed, and no latency guess."""

    def __init__(self, *a, vsmooth=0.4, **k):
        super().__init__(*a, **k)
        self.prev = None
        self.vel_s = (0.0, 0.0)
        self.vsmooth = vsmooth

    def frame(self, tr, now, lead, frame_dt, new_sample):
        fresh = new_sample and tr.t != self.last_t
        out = super().frame(tr, now, lead, frame_dt, new_sample)
        if fresh and self.p is not None:
            if self.prev is not None and tr.t - self.prev[0] > 0.012:
                raw = mul(sub(self.p, self.prev[1]), 1 / (tr.t - self.prev[0]))
                self.vel_s = add(mul(raw, 1 - self.vsmooth), mul(self.vel_s, self.vsmooth))
            self.prev = (tr.t, self.p)
            self.v = self.vel_s
        return out


def final_report():
    """2026-10-09 choices vs the first per-frame cursor, bright and dim room (stop-and-go painting, swipes)."""
    global NOISE, CAM_FPS, LATENCY, LATENCY_JITTER, GAME_JITTER, HITCH_EVERY
    rooms = {
        "bright room": dict(NOISE=0.003, CAM_FPS=30.0, LATENCY=0.035, LATENCY_JITTER=0.006, GAME_JITTER=0.0015, HITCH_EVERY=0.0),
        "dim room + editor hiccups": dict(NOISE=0.005, CAM_FPS=20.0, LATENCY=0.06, LATENCY_JITTER=0.012, GAME_JITTER=0.003, HITCH_EVERY=2.0),
    }
    schemes = {
        "per-frame cursor of 2026-10-08": lambda: ContinuousHands(2.0, 2.0, 2.0),
        "BodyHands (Fruta, menus)": lambda: ExtrapFD(2.0, 2.0, 2.0, lead_scale=0.0, max_ahead=0.08),
        "Pikturo brush (HandInterpolator)": lambda: InterpHands(4.0, 1.0, margin=1.0),
    }
    saved = {k: globals()[k] for k in rooms["bright room"]}
    try:
        for room, conditions in rooms.items():
            globals().update(conditions)
            print()
            print(f"{room}:")
            for name, make in schemes.items():
                m = {}
                for motion in ("rest", "stopgo", "swipe"):
                    for seed in range(1, 5):
                        for k, v in metrics(motion, run(make, motion, seed=seed)).items():
                            m.setdefault(f"{motion} {k}", []).append(v)
                print(f"    {name:34s} " + ", ".join(f"{k} {statistics.fmean(v):.1f}" for k, v in m.items()))
    finally:
        globals().update(saved)


class Chain:
    """A cursor followed by an extra per-frame One Euro (Pikturo's brush filter)."""

    def __init__(self, inner, min_cutoff, beta, d_cutoff=1.0):
        self.inner, self.extra = inner, Filter2(min_cutoff, beta, d_cutoff)

    def frame(self, tr, now, lead, frame_dt, new_sample):
        p = self.inner.frame(tr, now, lead, frame_dt, new_sample)
        return None if p is None else self.extra(p, frame_dt)


# ---------------------------------------------------------------- Pikturo's pen: when does the brush go down?

RAISE = {"from": (0.99, 0.34), "to": (1.03, 0.66)}     # hand hanging at the side (below the screen) -> chest height


def raise_wrist(seconds):
    def wrist(t):
        k = min_jerk((t - 1.0) / seconds)
        a, b = RAISE["from"], RAISE["to"]
        return a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k
    return wrist


def pen_trial(rule, raise_s, seed):
    """Raise the hand in raise_s seconds (starting at 1 s), then hold it. Returns (seconds after the hand stopped
    that the brush went down, px from where it stopped). Negative = it painted while the hand was still coming."""
    global wrist_at
    original = wrist_at
    w = raise_wrist(raise_s)
    wrist_at = lambda motion, t: w(t)
    try:
        frames = run(lambda: PaintCursor(), "raise", seconds=1.0 + raise_s + 1.5, seed=seed)
    finally:
        wrist_at = original
    stop = 1.0 + raise_s
    target = to_screen(zone_of(true_tip(RAISE["to"]), SHOULDER_C, HIP_C, U))
    still, anchor, prev = 0.0, None, None
    for t, pos, _, vel in frames:
        on = 0 <= pos[0] <= SCREEN_W and 0 <= pos[1] <= SCREEN_H
        dt = t - prev if prev is not None else 0.0
        prev = t
        if not on:
            still, anchor = 0.0, None
            continue
        if rule == "old":                     # 2026-10-08: hand speed (BodyHands velocity) < 0.5 screens/s for 0.25 s
            speed = math.hypot(*vel) if vel else 0.0
            still = still + dt if speed < 0.5 else max(0.0, still - 2 * dt)
            if still >= 0.25:
                return t - stop, math.dist(pos, target)
        else:                                 # new (PaintPen): the cursor stays inside a 55 px circle for 0.35 s
            if anchor is None or math.dist(pos, anchor) > 55:
                anchor, still = pos, 0.0
            else:
                still += dt
            if still >= 0.35:
                return t - stop, math.dist(pos, target)
    return float("nan"), float("nan")


class PaintCursor(ContinuousHands):
    """The proposed cursor plus Pikturo's extra smoothing; also keeps the per-sample velocity like BodyHands."""

    def __init__(self):
        super().__init__(min_cutoff=2.0, beta=2.0, d_cutoff=2.0)
        self.extra = Filter2(3.0, 0.5, 1.0)
        self.vel = (0.0, 0.0)
        self._last = None

    def frame(self, tr, now, lead, frame_dt, new_sample):
        fresh = new_sample and tr.t != self.last_t
        p = super().frame(tr, now, lead, frame_dt, new_sample)
        if fresh and self.p is not None:
            if self._last is not None:
                dt = tr.t - self._last[0]
                raw = mul(sub(self.p, self._last[1]), 1 / dt) if dt > 0.012 else self.vel
                self.vel = add(mul(raw, 0.6), mul(self.vel, 0.4))
            self._last = (tr.t, self.p)
        return None if p is None else self.extra(p, frame_dt)


def pen_report():
    print()
    print("Pikturo pen: seconds after the hand stops that the brush goes down (negative = paints on the way)")
    for raise_s in (0.5, 0.8, 1.2, 1.6):
        for rule in ("old", "new"):
            r = [pen_trial(rule, raise_s, seed) for seed in range(1, 9)]
            when = [x for x, _ in r if x == x]
            where = [d for _, d in r if d == d]
            print(f"    raise in {raise_s:.1f} s, {rule} rule: {statistics.fmean(when):+.2f} s "
                  f"(earliest {min(when):+.2f}), {statistics.fmean(where):.0f} px from where the hand stops")


# ---------------------------------------------------------------- 2026-10-09: "jarring, skips from point to point"

class FrameTimedHands:
    """BodyHands + HandSmoother until 2026-10-09: low-lag track samples, glide on from the game frame that received
    the sample (speed from the last two samples), per-frame One Euro 2/2/2; a sample with wrist visibility < 0.5
    dropped the hand (cursor hidden, filter reset, the next sample started from a standstill)."""

    def __init__(self):
        self.unit = 0.0
        self.out = Filter2(2.0, 2.0, 2.0)
        self.last_t = self.p = self.prev = None
        self.v = (0.0, 0.0)
        self.tracked = False
        self.recv = 0.0

    def frame(self, tr, now, lead, frame_dt, new_sample):
        if new_sample and tr.t != self.last_t:
            dt = tr.t - self.last_t if self.last_t is not None else 0.0
            if not (0 < dt < 0.012):
                self.last_t = tr.t
                was = self.tracked
                if tr.vis < 0.5:
                    self.tracked, self.v = False, (0.0, 0.0)
                    self.out.reset()
                else:
                    p = hand_zone(self, tr, "f")
                    self.v = add(mul(mul(sub(p, self.prev), 1 / dt), 0.6), mul(self.v, 0.4)) if was and 0 < dt < 0.25 else (0.0, 0.0)
                    self.p = self.prev = p
                    self.tracked, self.recv = True, now
        if not self.tracked:
            return None
        return self.out(add(self.p, mul(self.v, min(max(now - self.recv, 0.0), 0.08))), frame_dt)


def hand_zone(hands, tr, track):
    """BodyHands' zone: anchored on the smooth body, the hand from `track` ("f" low-lag, "raw" camera, "s" smooth)."""
    src = tr.s
    torso = math.dist(mid(src["ls"], src["rs"]), mid(src["lh"], src["rh"]))
    hands.unit = torso if hands.unit <= 0 else hands.unit + (torso - hands.unit) * 0.1
    j = getattr(tr, track)
    w, e = j["wrist"], j["elbow"]
    return zone_of(add(w, mul(sub(w, e), HAND_EXT)), mid(src["ls"], src["rs"]), mid(src["lh"], src["rh"]), hands.unit)


class HandTimeline:
    """HandInterpolator + HandPresence since 2026-10-09: samples on the camera's clock, the cursor at "now - measured
    latency - margin camera frames" (interpolated between samples, gliding on past the newest one up to max_ahead),
    then a per-frame One Euro. A hand is found at visibility 0.5, kept down to 0.3, lost after 0.3 s without a usable
    sample (robust=False: dropped on the first unsure sample, like before)."""

    def __init__(self, margin=0.5, cut=1.0, beta=4.0, dcut=2.0, max_ahead=0.08, track="raw", robust=True):
        self.unit = 0.0
        self.out = Filter2(cut, beta, dcut)
        self.buf = []
        self.keep = 6
        self.v = (0.0, 0.0)
        self.offset = None
        self.interval = 1 / 30
        self.margin, self.max_ahead, self.track, self.robust = margin, max_ahead, track, robust
        self.last_t = None
        self.tracked = False
        self.last_good = -1.0

    def lose(self):
        self.tracked = False
        self.buf = []
        self.v = (0.0, 0.0)
        self.out.reset()

    per_packet = False                  # True: the game hands it every packet (MotionSystem.PacketReceived), not just
                                        # the newest one each frame (two packets in one frame lost one, often at 60 fps)

    def frame(self, tr, now, lead, frame_dt, new_sample):
        if new_sample and not self.per_packet:
            self.packet(tr, now)
        if self.tracked and now - self.last_good > 0.3:
            self.lose()
        if not self.tracked:
            return None
        return self.out(self.at(now - self.offset - self.margin * self.interval), frame_dt)

    def packet(self, tr, now):
        if tr.t != self.last_t:
            self.last_t = tr.t
            if tr.vis >= (0.3 if self.robust and self.tracked else 0.5):
                p = hand_zone(self, tr, self.track)
                if self.buf:
                    dt = tr.t - self.buf[-1][0]
                    self.interval += (min(0.1, max(0.01, dt)) - self.interval) * 0.1
                    if dt >= 0.25:
                        self.v = (0.0, 0.0)
                    elif dt >= 0.012:
                        self.v = add(mul(mul(sub(p, self.buf[-1][1]), 1 / dt), 0.6), mul(self.v, 0.4))
                self.buf = (self.buf + [(tr.t, p)])[-self.keep:]
                off = now - tr.t
                self.offset = off if self.offset is None else self.offset + (off - self.offset) * 0.1
                self.tracked, self.last_good = True, now
            elif not self.robust:
                self.lose()

    def at(self, td):
        """The hand at camera time td: between samples (linear), past the newest gliding on up to max_ahead."""
        b = self.buf
        if td <= b[0][0]:
            return b[0][1]
        if td >= b[-1][0]:
            return add(b[-1][1], mul(self.v, min(td - b[-1][0], self.max_ahead)))
        i = next(i for i in range(1, len(b)) if b[i][0] >= td)
        (t0, a), (t1, c) = b[i - 1], b[i]
        return add(a, mul(sub(c, a), (td - t0) / (t1 - t0)))


class FitTimeline(HandTimeline):
    """HandTimeline that draws a least-squares line (degree 1) or parabola (2) through the last `count` samples, at
    the drawn moment: a moving regression (Savitzky-Golay on uneven times), like the Kinect white paper's "use future
    frames when a few frames of delay are acceptable". Pikturo's brush on 2026-10-19 only: on the real recording it
    jumped a little whenever a sample arrived (the refit moves the drawn point), 3.45 px/frame^2 of stutter vs 2.51.
    Weighing the samples by visibility and cross-fading old and new fits didn't fix that (2026-10-20)."""

    def __init__(self, *a, count=5, degree=2, window=None, **k):
        super().__init__(*a, **k)
        self.count, self.degree, self.window = count, degree, window
        if window:
            self.keep = 64

    def at(self, td):
        if self.window:                                   # the samples of the last `window` seconds (at least degree + 2)
            b = [s for s in self.buf if s[0] >= self.buf[-1][0] - self.window]
            if len(b) < self.degree + 2:
                b = self.buf[-(self.degree + 2):]
        else:
            b = self.buf[-self.count:]
        if len(b) <= self.degree:
            return super().at(td)
        td = min(max(td, b[0][0]), b[-1][0])             # never extrapolate the curve
        t_ref = b[-1][0]
        rows = [[(t - t_ref) ** e for e in range(self.degree + 1)] for t, _ in b]
        x = td - t_ref
        basis = [x ** e for e in range(self.degree + 1)]
        # normal equations, (degree+1) x (degree+1)
        n = self.degree + 1
        ata = [[sum(r[i] * r[j] for r in rows) for j in range(n)] for i in range(n)]
        out = []
        for k in range(2):
            atb = [sum(r[i] * p[k] for r, (_, p) in zip(rows, b)) for i in range(n)]
            coef = solve(ata, atb)
            out.append(sum(c * v for c, v in zip(coef, basis)))
        return tuple(out)


class SampleFiltered(HandTimeline):
    """Each raw sample smoothed when it arrives (One Euro in zone units on the camera clock: `scut` Hz, `sbeta` per zone
    per second), then straight lines between the smoothed samples, `margin` frames back, then the per-frame filter.
    The old brush's shape (the low-lag track is such a per-sample filter) with its own numbers."""

    def __init__(self, margin, cut, beta, dcut, scut, sbeta, sdcut=1.0):
        super().__init__(margin, cut, beta, dcut, 0.0, "raw")
        self.sample_filter = Filter2(scut, sbeta, sdcut)
        self.prev_t = None

    def lose(self):
        super().lose()
        self.sample_filter.reset()
        self.prev_t = None

    def packet(self, tr, now):
        n = len(self.buf)
        newest = self.buf[-1][0] if self.buf else None
        super().packet(tr, now)
        if self.buf and self.buf[-1][0] != newest:          # a new sample went in: smooth it
            t, p = self.buf[-1]
            dt = t - self.prev_t if self.prev_t is not None else 0.0
            self.prev_t = t
            self.buf[-1] = (t, self.sample_filter(p, dt))


def solve(a, b):
    """Gaussian elimination for the small normal equations."""
    n = len(b)
    m = [row[:] + [b[i]] for i, row in enumerate(a)]
    for c in range(n):
        p = max(range(c, n), key=lambda r: abs(m[r][c]))
        m[c], m[p] = m[p], m[c]
        for r in range(n):
            if r != c and m[c][c] != 0:
                f = m[r][c] / m[c][c]
                m[r] = [x - f * y for x, y in zip(m[r], m[c])]
    return [m[i][n] / m[i][i] for i in range(n)]


def report_2026_10_09():
    """The cursors before and after 2026-10-09, in the default room, on a PC where the pose model takes about a
    camera frame (frames skipped unevenly), and with wrist-visibility dips. The dip rate is a guess (2% of samples,
    15% while the hand moves > 1.5 m/s); a recording (main.py --record) would give the real one."""
    global PIPELINE, DIPS
    rooms = {
        "30 fps, even": ("regular", (0.0, 0.0)),
        "PC: inference-bound frames": ("inference", (0.0, 0.0)),
        "PC + wrist visibility dips": ("inference", (0.02, 0.15)),
    }
    schemes = {
        "Fruta before (frame-timed)": FrameTimedHands,
        "Fruta now (raw, camera clock)": HandTimeline,
        "Pikturo brush before": lambda: HandTimeline(1.0, 4.0, 1.0, 1.0, 0.0, "f", robust=False),
        "Pikturo brush now": lambda: HandTimeline(1.0, 4.0, 1.0, 1.0, 0.0, "f"),
    }
    keep = ("jitter px/frame", "stutter px/frame", "worst px", "lag ms", "overshoot px")
    saved = PIPELINE, DIPS
    try:
        for room, (PIPELINE, DIPS) in rooms.items():
            print(f"\n{room}:")
            for name, make in schemes.items():
                parts = []
                for motion in ("rest", "stopgo", "zigzag", "swipe"):
                    m, hidden = {}, 0
                    for seed in range(1, 4):
                        stats = {}
                        for k, v in metrics(motion, run(make, motion, seed=seed, stats=stats)).items():
                            m.setdefault(k, []).append(v)
                        hidden += stats["hidden frames"]
                    text = " ".join(f"{k.split()[0]} {statistics.fmean(v):.0f}" for k, v in m.items() if k in keep)
                    parts.append(f"{motion}: {text}" + (f" HIDDEN {hidden / 3:.0f} fr" if hidden else ""))
                print(f"    {name:30s} " + " | ".join(parts))
    finally:
        PIPELINE, DIPS = saved


LAPTOP = dict(PIPELINE="pc", CAM_FPS=60.0, INFERENCE=0.013, INFERENCE_JITTER=0.0015, MP_SMOOTH=True)
BOX = dict(PIPELINE="regular", CAM_FPS=30.0, LATENCY=0.045, LATENCY_JITTER=0.008, MP_SMOOTH=True)


def conditions(**changes):
    """Sets module settings for a block of runs; returns the old values to put back."""
    saved = {k: globals()[k] for k in changes}
    globals().update(changes)
    return saved


def score(make, motions, seeds=range(1, 5)):
    out = {}
    for motion in motions:
        m = {}
        for seed in seeds:
            for k, v in metrics(motion, run(make, motion, seed=seed)).items():
                m.setdefault(k, []).append(v)
        out[motion] = {k: statistics.fmean(v) for k, v in m.items()}
    return out


def line(name, result, keep=("jitter px/frame", "stutter px/frame", "worst px", "lag ms", "overshoot px")):
    parts = [f"{motion}: " + " ".join(f"{k.split()[0]} {v:.0f}" if k != "jitter px/frame" else f"jitter {v:.1f}"
                                      for k, v in m.items() if k in keep) for motion, m in result.items()]
    return f"    {name:44s} " + " | ".join(parts)


def PikturoBrush():
    """Pikturo's brush since 2026-10-19 (PaintGame.Hand.Track + CursorRope): raw samples, a parabola through the last
    250 ms of them drawn one camera frame back, One Euro 2 / 4 / 2, and a 6 px string between cursor and paint. On the
    real recording it scored jerkier than per-sample smoothing (PerSampleBrush), but the user found it felt better
    (2026-10-20): it is ~10-20 ms quicker, which this score doesn't weigh."""
    return Rope(FitTimeline(1.0, 2.0, 4.0, 2.0, 0.0, "raw", degree=2, window=0.25), 6)


def PerSampleBrush():
    """Pikturo's hands for a few hours on 2026-10-20, then reverted (the user: "it felt better before"): raw samples,
    each smoothed as it arrives (One Euro 1.5 Hz, beta 30 per zone width per second), straight lines between them one
    camera frame back, One Euro 4 / 0.5 / 1. Best score on the recording (report_2026_10_20)."""
    return SampleFiltered(1.0, 4.0, 0.5, 1.0, 1.5, 30.0)


def report_2026_10_19():
    """(Overturned the next day by a real recording, see report_2026_10_20: this noise model missed that the camera gets
    3-5x noisier while the hand moves and when it is unsure, and that refitting a curve jumps.)
    2026-10-19, "the hands feel much better in Vishu! than in Pikturo!" and "reduce the stutter". The earlier reports
    measured every cursor in the hand zone; here each is drawn where its game draws it (Vishu!: the framed camera
    picture, 327 px per torso; Pikturo's paper: the zone, 600 x 415 px per torso, so camera noise is 1.8x / 1.3x larger
    on the screen), with the laptop's real frame timing (a 60 fps camera of which main.py --preview used every 2nd or
    3rd frame), MediaPipe's own filter on the joints, and the same with the preview off the camera loop (60 fps).
    Tried and dropped: linear interpolation on raw samples (less lag, twice the jitter), cubic Hermite curves (no
    better than linear), the rope alone (still wobbles while moving), lines (degree 1) and longer windows (overshoot
    at stops), sampling every packet instead of the newest one per frame (no measurable change), MediaPipe getting
    capture vs detection timestamps (no measurable change)."""
    keep = ("jitter px/frame", "stutter px/frame", "worst px", "lag ms", "overshoot px", "short px")
    schemes = {
        "Vishu! cursor (mirror)": ("mirror", HandTimeline),
        "Pikturo free hand = BodyHands (zone)": ("zone", HandTimeline),
        "Pikturo brush until 2026-10-19 (zone)": ("zone", lambda: HandTimeline(1.0, 4.0, 1.0, 1.0, 0.0, "f")),
        "  raw samples, linear, 1/4/2": ("zone", lambda: HandTimeline(1.0, 1.0, 4.0, 2.0, 0.0, "raw")),
        "  + rope 6 px": ("zone", lambda: Rope(HandTimeline(1.0, 1.0, 4.0, 2.0, 0.0, "raw"), 6)),
        "  parabola through 250 ms, 2/4/2": ("zone", lambda: FitTimeline(1.0, 2.0, 4.0, 2.0, 0.0, "raw", degree=2, window=0.25)),
        "Pikturo brush now (+ rope 6 px)": ("zone", PikturoBrush),
        "per-sample brush (2026-10-20, reverted)": ("zone", PerSampleBrush),
    }
    rooms = {
        "laptop, main.py --preview until 2026-10-19 (~30 poses/s)": LAPTOP,
        "laptop, preview off the camera loop (60 poses/s)": dict(LAPTOP, LOOP_EXTRA=0.002, LOOP_EXTRA_JITTER=0.0005),
        "box-like (30 fps camera)": BOX,
    }
    for room, settings in rooms.items():
        saved = conditions(**settings)
        try:
            print(f"\n{room}:")
            for name, (where, make) in schemes.items():
                old = conditions(MAP=where)
                try:
                    print(line(name, score(make, ("rest", "paint", "stopgo", "zigzag")), keep))
                finally:
                    globals().update(old)
        finally:
            globals().update(saved)


def main():
    report_2026_10_19()
    print()
    print("(2026-10-09)")
    report_2026_10_09()
    print()
    print("(2026-10-09 morning)")
    final_report()
    print()
    print("(first round, constant-speed motions)")
    for name, make in SCHEMES.items():
        line = []
        for motion in ("rest", "paint", "swipe"):
            m = {}
            for seed in range(1, 6):
                for k, v in metrics(motion, run(make, motion, seed=seed)).items():
                    m.setdefault(k, []).append(v)
            line.append(f"{motion}: " + ", ".join(f"{k} {statistics.fmean(v):.1f}" for k, v in m.items()))
        print(f"{name}\n    " + "\n    ".join(line))


# ---------------------------------------------------------------- a real session: main.py --record <file>.jsonl

COCO = {"ls": 5, "rs": 6, "lh": 11, "rh": 12}
ARMS = {"right": (8, 10), "left": (7, 9)}          # (elbow, wrist)


HK = {"left": (0, 1), "right": (3, 4)}            # hk indices of (pinky, index) per hand


def load_recording(path, hand, palm=False):
    """A main.py --record session as packets for one hand: (arrival, capture, joints, wrist visibility), in view space
    like BodyTracker (mirrored, y up, image heights), times from 0. Arrival = capture + inference + 2 ms.
    palm: the hand point is the middle of wrist, index and pinky (the pose model's hand points, "hk", recorded since
    2026-10-19), pushed on along wrist -> palm, instead of the wrist pushed along the forearm."""
    out = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            p = json.loads(line)
            if not p.get("people"):
                continue
            k, aspect = p["people"][0]["k"], p["w"] / p["h"]
            joint = lambda i: ((1 - k[i * 4]) * aspect, 1 - k[i * 4 + 1])
            elbow, wrist = ARMS[hand]
            joints = {n: joint(i) for n, i in COCO.items()}
            joints["elbow"], joints["wrist"] = joint(elbow), joint(wrist)
            hk = p["people"][0].get("hk")
            if palm:
                if not hk:
                    raise SystemExit(f"{path} has no hand points (hk): record it again with main.py --record")
                w = joints["wrist"]
                pts = [((1 - hk[i * 4]) * aspect, 1 - hk[i * 4 + 1]) for i in HK[hand]]
                c = ((w[0] + pts[0][0] + pts[1][0]) / 3, (w[1] + pts[0][1] + pts[1][1]) / 3)
                joints["elbow"], joints["wrist"] = w, c
            capture = p["t"] / 1000
            out.append((capture + p.get("inf", 30) / 1000 + 0.002, capture, joints, k[wrist * 4 + 3]))
    t0 = out[0][1]
    return [(a - t0, c - t0, j, v) for a, c, j, v in out]


def reference_path(packets):
    """Where the hand really went, as well as the samples can tell: a parabola through the raw samples of +-150 ms
    around each moment (Gaussian weights, sd 60 ms; unsure samples, visibility < 0.3, left out), every 5 ms. Smoothed
    both ways in time, so it has no lag; a cursor is scored against it at the cursor's own lag."""
    tr, probe, raw = Tracker(), HandTimeline(), []
    for _, c, j, v in packets:
        tr.update(j, c, v)
        if v >= 0.3:
            raw.append((c, to_screen(hand_zone(probe, tr, "raw"))))
    times = [q[0] for q in raw]
    grid, t = {}, packets[0][1]
    while t < packets[-1][1]:
        lo, hi = bisect.bisect_left(times, t - 0.15), bisect.bisect_right(times, t + 0.15)
        win = raw[lo:hi]
        if len(win) >= 5 and win[0][0] <= t - 0.05 and win[-1][0] >= t + 0.05:
            rows = [[(q[0] - t) ** e for e in range(3)] for q in win]
            w = [math.exp(-0.5 * ((q[0] - t) / 0.06) ** 2) for q in win]
            ata = [[sum(wk * r[i] * r[j] for wk, r in zip(w, rows)) for j in range(3)] for i in range(3)]
            grid[round(t * 200)] = tuple(solve(ata, [sum(wk * r[i] * q[1][k] for wk, r, q in zip(w, rows, win))
                                                     for i in range(3)])[0] for k in range(2))
        t += 0.005

    def at(t):
        i = t * 200
        k = math.floor(i)
        p, q = grid.get(k), grid.get(k + 1)
        if p is None or q is None:
            return None
        return p[0] + (q[0] - p[0]) * (i - k), p[1] + (q[1] - p[1]) * (i - k)
    return at


def score_on_recording(packets, ref, make):
    """A cursor on a recording at 60 frames a second, against the reference path: stutter = RMS of the cursor's
    per-frame acceleration minus the reference's (px/frame^2), jolts = frames where that is > 25, wobble = RMS distance
    to the reference (px), lag = the time shift that fits best (ms), hidden = frames without a cursor."""
    tr, hands, k, frames = Tracker(), make(), 0, []
    frame_t, prev = packets[0][0], None
    while frame_t < packets[-1][0]:
        new = False
        while k < len(packets) and packets[k][0] <= frame_t:
            _, c, j, v = packets[k]
            tr.update(j, c, v)
            k, new = k + 1, True
        p = hands.frame(tr, frame_t, 0.0, frame_t - prev if prev is not None else 0.0, new)
        frames.append((frame_t, None if p is None else to_screen(p)))
        prev, frame_t = frame_t, frame_t + 1 / GAME_FPS
    best = None
    for lag_ms in range(40, 221, 5):
        errs = [math.dist(p, r) for t, p in frames[::2] if p and (r := ref(t - lag_ms / 1000))]
        e = math.sqrt(statistics.fmean(x * x for x in errs))
        if best is None or e < best[1]:
            best = (lag_ms, e)
    lag = best[0] / 1000
    excess = []
    for a, b, c in zip(frames, frames[1:], frames[2:]):
        if not (a[1] and b[1] and c[1]):
            continue                                       # a hand coming back after it was lost is not a jump
        ra, rb, rc = ref(a[0] - lag), ref(b[0] - lag), ref(c[0] - lag)
        if ra and rb and rc:
            excess.append(math.dist((c[1][0] - 2 * b[1][0] + a[1][0], c[1][1] - 2 * b[1][1] + a[1][1]),
                                    (rc[0] - 2 * rb[0] + ra[0], rc[1] - 2 * rb[1] + ra[1])))
    return {"stutter": rms(excess), "jolts": sum(x > 25 for x in excess), "wobble": best[1], "lag": best[0],
            "hidden": sum(p is None for _, p in frames)}


REPLAY_SCHEMES = {
    "hand cursor (BodyHands, Vishu!)": HandTimeline,
    "Pikturo brush until 2026-10-19": lambda: HandTimeline(1.0, 4.0, 1.0, 1.0, 0.0, "f"),
    "Pikturo brush now (fit + rope, 2026-10-19)": PikturoBrush,
    "per-sample brush (2026-10-20, reverted)": PerSampleBrush,
}


def replay(path, palm=False, schemes=None):
    """What the camera really delivers (timing, wrist visibility) and how each cursor copes with it, scored against
    the path the samples make when smoothed both ways in time (reference_path, score_on_recording)."""
    for hand in ("right", "left"):
        packets = load_recording(path, hand, palm)
        caps = [c for _, c, _, _ in packets]
        gaps = [b - a for a, b in zip(caps, caps[1:])]
        vis = [v for *_, v in packets]
        print(f"\n{hand} hand: {len(packets)} samples in {caps[-1]:.0f} s, median gap {1000 * statistics.median(gaps):.0f} ms, "
              f"gaps > 50 ms: {sum(g > 0.05 for g in gaps)}, longest {1000 * max(gaps):.0f} ms; wrist visibility "
              f"< 0.5 in {100 * sum(v < 0.5 for v in vis) / len(vis):.1f}% of samples (< 0.3: {100 * sum(v < 0.3 for v in vis) / len(vis):.1f}%)")
        ref = reference_path(packets)
        for name, make in (schemes or REPLAY_SCHEMES).items():
            m = score_on_recording(packets, ref, make)
            print(f"    {name:44s} stutter {m['stutter']:5.2f}  jolts {m['jolts']:3d}  wobble {m['wobble']:5.1f} px  "
                  f"lag {m['lag']:3d} ms  hidden {m['hidden']} frames")


def report_2026_10_20(path="motion-service-py/sessions/paint.jsonl"):
    """2026-10-20: the user's painting session (2.7 min at the laptop, the webcam at 20 fps in a dim room). The
    simulator had ranked the 2026-10-19 brush as smooth as the old one; the recording didn't (3.45 vs 2.51). What it
    showed: the hand point's noise 0.5-0.9 % of a torso length still, 2.4-3 % moving slowly, 8-15 % fast (DIM_ROOM),
    3-5x more in frames the camera is unsure of (VIS_BANDS); a curve refitted at every sample jumps. The old shape
    (smooth each sample, straight lines between) scored best, with these numbers better than the old brush on every
    count. But the user tried it and preferred the 2026-10-19 brush: this score misses how much ~10-20 ms of lag is
    felt. Weigh lag more next time, and let the user's feel decide."""
    schemes = {
        "Pikturo brush until 2026-10-19": lambda: HandTimeline(1.0, 4.0, 1.0, 1.0, 0.0, "f"),
        "Pikturo brush now (fit + rope, 2026-10-19)": PikturoBrush,
        "per sample 1.5/12, out 4/1": lambda: SampleFiltered(1.0, 4.0, 1.0, 1.0, 1.5, 12.0),
        "per sample 1.5/30, out 4/1": lambda: SampleFiltered(1.0, 4.0, 1.0, 1.0, 1.5, 30.0),
        "per sample 1.5/30, out 4/0.5 (best score)": PerSampleBrush,
        "per sample 1.5/120, out 4/0.5": lambda: SampleFiltered(1.0, 4.0, 0.5, 1.0, 1.5, 120.0),
        "per sample 1.5/30, margin 0.5": lambda: SampleFiltered(0.5, 4.0, 0.5, 1.0, 1.5, 30.0),
        "best score + rope 6": lambda: Rope(PerSampleBrush(), 6),
    }
    replay(path, schemes=schemes)


if __name__ == "__main__":
    args = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    args.add_argument("--recording", help="replay a main.py --record session (.jsonl) instead of the simulations")
    args.add_argument("--palm", action="store_true", help="with --recording: the hand point from the palm (needs hk)")
    args.add_argument("--tune", action="store_true", help="with --recording: the 2026-10-20 settings search")
    args = args.parse_args()
    if args.recording and args.tune:
        report_2026_10_20(args.recording)
    elif args.recording:
        replay(args.recording, args.palm)
    else:
        main()
