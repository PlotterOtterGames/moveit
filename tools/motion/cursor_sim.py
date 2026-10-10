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


def sample_joints(motion, t, rng):
    w = wrist_at(motion, t)
    true = {"ls": (SHOULDER_C[0] - 0.07, SHOULDER_C[1]), "rs": R_SHOULDER,
            "lh": (HIP_C[0] - 0.04, HIP_C[1]), "rh": (HIP_C[0] + 0.04, HIP_C[1]),
            "elbow": elbow_for(w), "wrist": w}
    return {j: (x + rng.gauss(0, NOISE * ASPECT), y + rng.gauss(0, NOISE)) for j, (x, y) in true.items()}


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

PIPELINE = "regular"            # "regular": a sample every 1/CAM_FPS; "inference": main.py on a PC, see below
INFERENCE, INFERENCE_JITTER = 0.031, 0.006
DIPS = (0.0, 0.0)               # chance that a sample's wrist visibility dips below 0.5: hand slow, hand fast (> 1.5 m/s)


def capture_times(seconds, rng):
    """(arrival, capture) times of the samples. "inference": the camera delivers CAM_FPS, the pose model takes about
    as long as a camera frame and always takes the newest one (main.py's FrameGrabber), so frames are skipped unevenly."""
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


def run(make_hands, motion, seconds=6.0, seed=1, stats=None):
    rng = random.Random(seed)
    packets = []
    for a, c in capture_times(seconds, rng):
        joints, vis = sample_joints(motion, c, rng), 0.95
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

    def frame(self, tr, now, lead, frame_dt, new_sample):
        if new_sample and tr.t != self.last_t:
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
                self.buf = (self.buf + [(tr.t, p)])[-6:]
                off = now - tr.t
                self.offset = off if self.offset is None else self.offset + (off - self.offset) * 0.1
                self.tracked, self.last_good = True, now
            elif not self.robust:
                self.lose()
        if self.tracked and now - self.last_good > 0.3:
            self.lose()
        if not self.tracked:
            return None
        td, b = now - self.offset - self.margin * self.interval, self.buf
        if td <= b[0][0]:
            p = b[0][1]
        elif td >= b[-1][0]:
            p = add(b[-1][1], mul(self.v, min(td - b[-1][0], self.max_ahead)))
        else:
            i = next(i for i in range(1, len(b)) if b[i][0] >= td)
            (t0, a), (t1, c) = b[i - 1], b[i]
            p = add(a, mul(sub(c, a), (td - t0) / (t1 - t0)))
        return self.out(p, frame_dt)


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


def main():
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


def load_recording(path, hand):
    """A main.py --record session as packets for one hand: (arrival, capture, joints, wrist visibility), in view space
    like BodyTracker (mirrored, y up, image heights), times from 0. Arrival = capture + inference + 2 ms."""
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
            capture = p["t"] / 1000
            out.append((capture + p.get("inf", 30) / 1000 + 0.002, capture, joints, k[wrist * 4 + 3]))
    t0 = out[0][1]
    return [(a - t0, c - t0, j, v) for a, c, j, v in out]


def replay(path):
    """What the camera really delivers (timing, wrist visibility) and how each cursor copes with it. No ground truth
    here: "accel" (RMS of the per-frame change of the cursor's step, px/frame^2) and "worst" (its largest value) show
    stutter and snaps; lag is measured against the raw samples' path."""
    schemes = {
        "Fruta before (frame-timed)": FrameTimedHands,
        "Fruta now (raw, camera clock)": HandTimeline,
        "Pikturo brush before": lambda: HandTimeline(1.0, 4.0, 1.0, 1.0, 0.0, "f", robust=False),
        "Pikturo brush now": lambda: HandTimeline(1.0, 4.0, 1.0, 1.0, 0.0, "f"),
    }
    for hand in ("right", "left"):
        packets = load_recording(path, hand)
        caps = [c for _, c, _, _ in packets]
        gaps = [b - a for a, b in zip(caps, caps[1:])]
        vis = [v for *_, v in packets]
        torso = statistics.median(math.dist(mid(j["ls"], j["rs"]), mid(j["lh"], j["rh"])) for _, _, j, _ in packets)
        fast = [math.dist(packets[i][2]["wrist"], packets[i - 1][2]["wrist"]) / max(gaps[i - 1], 1e-3) > 4 * torso
                for i in range(1, len(packets))]
        dips = [v < 0.5 for v in vis[1:]]
        print(f"\n{hand} hand: {len(packets)} samples in {caps[-1]:.0f} s, median gap {1000 * statistics.median(gaps):.0f} ms, "
              f"gaps > 50 ms: {sum(g > 0.05 for g in gaps)}, longest {1000 * max(gaps):.0f} ms")
        print(f"    wrist visibility < 0.5 in {100 * sum(v < 0.5 for v in vis) / len(vis):.1f}% of samples "
              f"(< 0.3: {100 * sum(v < 0.3 for v in vis) / len(vis):.1f}%), while the hand moves fast "
              f"(> 4 torsos/s): {100 * sum(d for d, f in zip(dips, fast) if f) / max(1, sum(fast)):.1f}% of {sum(fast)}")

        # the raw path (zone, as the new cursor sees it) to measure lag against
        tr, probe, path_pts = Tracker(), HandTimeline(), []
        for _, c, j, v in packets:
            tr.update(j, c, v)
            path_pts.append((c, to_screen(hand_zone(probe, tr, "raw"))))

        def raw_at(t):
            i = next((i for i in range(1, len(path_pts)) if path_pts[i][0] >= t), None)
            if i is None:
                return None
            (t0, a), (t1, b) = path_pts[i - 1], path_pts[i]
            k = (t - t0) / max(t1 - t0, 1e-6)
            return a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k

        for name, make in schemes.items():
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
            shown = [(t, p) for t, p in frames if p is not None]
            acc = [math.dist((c[0] - 2 * b[0] + a[0], c[1] - 2 * b[1] + a[1]), (0, 0))
                   for (_, a), (_, b), (_, c) in zip(shown, shown[1:], shown[2:])]
            best = None
            for lag_ms in range(0, 301, 5):
                errs = [math.dist(p, r) for t, p in shown[::3] if (r := raw_at(t - lag_ms / 1000)) is not None]
                e = statistics.fmean(errs) if errs else float("inf")
                if best is None or e < best[1]:
                    best = (lag_ms, e)
            print(f"    {name:30s} accel {rms(acc):.1f}, worst {max(acc):.0f} px/frame^2, lag {best[0]} ms, "
                  f"hidden {len(frames) - len(shown)} frames")


if __name__ == "__main__":
    args = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    args.add_argument("--recording", help="replay a main.py --record session (.jsonl) instead of the simulations")
    args = args.parse_args()
    replay(args.recording) if args.recording else main()
