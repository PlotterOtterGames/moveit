"""Pikturo! on a real recording: what would this player's hands have painted?

Replays a recorded session (main.py --record, or the box's recorder: /sdcard/Android/data/al.tundu.games/files/
sessions), or one of fake_sender's scripts, through a mirror of Pikturo's brush pipeline and writes the painting as a
PNG, so the pen rules can be judged by looking at the picture a real arm makes instead of by guessing:

    hand point (Body.HandPoint: the raw wrist carried on along the arm from the smooth shoulder, 10 %; the wrist pushed
    30 % of the forearm on until 2026-11-17) -> torso lengths from the shoulders' middle (on the smooth
    track) -> HandInterpolator (camera clock, a parabola through the last 0.25 s, one camera frame back, One Euro)
    -> the screen -> CursorRope (6 px) -> PaintPen (when does the brush paint?) -> the wet paint wiped as a hand leaves

    motion-service-py/.venv/Scripts/python tools/motion/paint_sim.py <recording.jsonl> [--sheet 4] [--pen easy]
    motion-service-py/.venv/Scripts/python tools/motion/paint_sim.py --script paint        # fake_sender's script

In the pictures: painted = blue (the right hand) and red (the left); the way in before the brush went down = thin
grey; paint taken back as the hand left = thin orange. --sheet N cuts the session into N-second panels (single
strokes show); without it everything is on one sheet.

Pens (--pen):
    easy     (default; PaintPen.UpdateEasy, since 2026-11-10) raise a hand and it paints: the brush goes down once the
             hand has arrived (stopped, turned, or 0.8 s passed), lifts when it leaves the paper or rests
    switch   (PaintPen.UpdateSwitch, 2026-10-09) the right hand paints while the left wrist is raised to the chest
    always   every moment a hand is over the paper (what the raw brush path looks like)
Mappings (--map):
    easel    (default) since 2026-11-10: 480 px per torso length both ways, the paper (1056 x 792) in the middle of the
             screen from 0.87 torso lengths above the shoulders to 0.78 below
    zone     until 2026-11-10: the hands' zone stretched over the whole 16:9 screen (600 x 415 px per torso length: a
             circle in the air is an ellipse on the screen)

What it showed (2026-11-10, the box's recordings of 2026-11-01): the brush draws recognisable loops from the user's
hand circles; the pose model's wrist-over-chest glitch is a dent of 50-80 px in them, and a jump gate before the
brush (--guard: BrushGuard below) dropped 3-7 samples of a minute and held back 36-60 real ones: not adopted, like
tools/motion/jump_gate.py before it. With the easy pen the ways up from the hip and down again leave no lines.

Keep EasyPen and leaving_since here in step with unity/MoveItGames/Assets/Games/Paint/Scripts/PaintPen.cs (and
PaintGame.Apply / SettleTail) when changing either.
"""

import argparse
import json
import math
import os
import socket
import subprocess
import sys
import tempfile
import threading

sys.path.insert(0, os.path.dirname(__file__))
import cursor_sim as cs  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SCREEN_W, SCREEN_H = 1920, 1080
ARM_LONGER = 0.1   # Body.HandPoint
JOINTS = {"nose": 0, "ls": 5, "rs": 6, "le": 7, "re": 8, "lw": 9, "rw": 10, "lh": 11, "rh": 12}
# PaintGame: paperScale, paperShoulderY, the paper from PaperAbove above the shoulders, 2.2 x 1.65 torso lengths
SCALE, SHOULDER_Y, PAPER_ABOVE, PAPER_W, PAPER_H = 480.0, 578.0, 0.87, 2.2, 1.65
# PaintGame: leavingSpeed, droppingSpeed (torso lengths a second), leftAfterSeconds, easyWetSeconds
LEAVING_SPEED, DROPPING_SPEED, LEFT_AFTER, WET = 2.0, 0.5, 0.35, 1.2


def load(path, t_from=0.0, t_to=1e9):
    """Packets as (arrival, capture, joints, visibilities), view space like BodyTracker (mirrored, y up, image
    heights), times from 0."""
    out = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            if not line.strip():
                continue
            p = json.loads(line)
            if not p.get("people"):
                continue
            k, aspect = p["people"][0]["k"], p["w"] / p["h"]
            joints = {n: ((1 - k[i * 4]) * aspect, 1 - k[i * 4 + 1]) for n, i in JOINTS.items()}
            vis = {n: k[i * 4 + 3] for n, i in JOINTS.items()}
            capture = p["t"] / 1000
            out.append((capture + p.get("inf", 30) / 1000 + 0.002, capture, joints, vis))
    t0 = out[0][1]
    out = [(a - t0, c - t0, j, v) for a, c, j, v in out]
    return [q for q in out if t_from <= q[1] <= t_to]


def record_script(script, glitches=0.0):
    """fake_sender.py --script <script> --once, its packets caught on a UDP port and written as a recording.
    glitches: the box's pose-model glitches a second (fake_sender --glitches)."""
    port = 5098
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind(("127.0.0.1", port))
    sock.settimeout(1.5)
    service = os.path.join(ROOT, "motion-service-py")
    command = [sys.executable, "fake_sender.py", "--script", script, "--port", str(port), "--once"]
    if glitches > 0:
        command += ["--glitches", str(glitches)]
    proc = subprocess.Popen(command, cwd=service, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    lines = []

    def listen():
        while True:
            try:
                data, _ = sock.recvfrom(65535)
            except socket.timeout:
                if proc.poll() is not None:
                    return
                continue
            lines.append(data.decode("utf-8"))
    t = threading.Thread(target=listen)
    t.start()
    proc.wait()
    t.join()
    sock.close()
    path = os.path.join(tempfile.gettempdir(), f"paint_sim_{script}.jsonl")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
    return path


class BodyFrame:
    """The smooth body the hands are measured against (BodyTracker's smooth track: One Euro 0.5 / 1.5, body-relative)."""

    def __init__(self):
        self.f = {j: cs.Filter2(0.5, 1.5) for j in ("ls", "rs", "lh", "rh")}
        self.scale = 0.0
        self.unit = 0.0
        self.t = None
        self.s = {}

    def update(self, joints, t):
        dt = t - self.t if self.t is not None else 0.0
        self.t = t
        torso = math.dist(cs.mid(joints["ls"], joints["rs"]), cs.mid(joints["lh"], joints["rh"]))
        self.scale = torso if self.scale <= 0 else self.scale + (max(torso, 0.02) - self.scale) * 0.05
        for j in self.f:
            self.s[j] = self.f[j](joints[j], dt, self.scale)
        smooth = math.dist(cs.mid(self.s["ls"], self.s["rs"]), cs.mid(self.s["lh"], self.s["rh"]))
        self.unit = smooth if self.unit <= 0 else self.unit + (smooth - self.unit) * 0.1

    def to_body(self, p):
        """View space -> torso lengths from the shoulders' middle (x to the screen's right, y up)."""
        c = cs.mid(self.s["ls"], self.s["rs"])
        return (p[0] - c[0]) / self.unit, (p[1] - c[1]) / self.unit


def hand_point(joints, side, body):
    """Body.HandPoint: the raw wrist carried on out along the arm from the smooth shoulder, as if 10 % longer."""
    w, s = joints[side + "w"], body.s[side + "s"]
    return s[0] + (w[0] - s[0]) * (1 + ARM_LONGER), s[1] + (w[1] - s[1]) * (1 + ARM_LONGER)


def resting(joints, side, unit):
    """PaintGame.IsResting: the arm hanging straight down at the side, full length in the picture."""
    s, e, w = joints[side + "s"], joints[side + "e"], joints[side + "w"]

    def angle_down(a, b):
        d = (b[0] - a[0], b[1] - a[1])
        n = math.hypot(*d)
        return 180.0 if n < 1e-6 else math.degrees(math.acos(max(-1.0, min(1.0, -d[1] / n))))
    if angle_down(s, e) >= 35 or angle_down(e, w) >= 35:
        return False
    return math.dist(s, w) >= 0.85 * unit


# ---------------------------------------------------------------- BrushGuard (tried 2026-11-10, not adopted)

class BrushGuard:
    """A jump gate before the brush: a sample more than `jump` torso lengths (and `ratio` times the hand's recent
    step) from the last trusted one is held back; back near where the hand was going within `hold` seconds = a glitch,
    dropped; else the held samples are let through. On the box's hand circles it changed nothing to see (the brush's
    parabola already rounds a one-frame jump, and the wrist-over-chest glitch is slow: several frames, no jump)."""

    def __init__(self, jump=0.28, ratio=3.0, hold=0.2, back=0.6):
        self.jump, self.ratio, self.hold, self.back = jump, ratio, hold, back
        self.last, self.step, self.velocity, self.held = None, 0.0, (0.0, 0.0), []
        self.dropped = self.released = 0

    def reset(self):
        self.last, self.step, self.velocity, self.held = None, 0.0, (0.0, 0.0), []

    def push(self, t, p):
        if self.last is None:
            self.last = (t, p)
            return [(t, p)]
        lt, lp = self.last
        dt = max(t - lt, 1e-3)
        if self.held:
            expect = (lp[0] + self.velocity[0] * min(dt, 0.1), lp[1] + self.velocity[1] * min(dt, 0.1))
            away = math.dist(self.held[0][1], lp)
            if math.dist(p, expect) <= self.back * away and math.dist(p, lp) < math.dist(p, self.held[-1][1]):
                self.dropped += len(self.held)
                self.held = []
                return self._trust(t, p)
            if t - self.held[0][0] >= self.hold or len(self.held) >= 6:
                out = self.held + [(t, p)]
                self.released += len(self.held)
                self.held, self.step, self.velocity, self.last = [], 0.0, (0.0, 0.0), (t, p)
                return out
            self.held.append((t, p))
            return []
        if math.dist(p, lp) > max(self.jump, self.ratio * self.step) * max(1.0, dt / 0.034):
            self.held = [(t, p)]
            return []
        return self._trust(t, p)

    def _trust(self, t, p):
        lt, lp = self.last
        dt = max(t - lt, 1e-3)
        self.step += (min(math.dist(p, lp), 0.5) * min(1.0, 0.034 / dt) - self.step) * 0.3
        v = ((p[0] - lp[0]) / dt, (p[1] - lp[1]) / dt)
        self.velocity = (self.velocity[0] + (v[0] - self.velocity[0]) * 0.5, self.velocity[1] + (v[1] - self.velocity[1]) * 0.5)
        self.last = (t, p)
        return [(t, p)]


# ---------------------------------------------------------------- HandInterpolator (mirror of HandInterpolator.cs)

class Interp:
    def __init__(self, cut, beta, dcut, margin=1.0, fit=0.25, ahead=0.0):
        self.fx, self.fy = cs.OneEuro(cut, beta, dcut), cs.OneEuro(cut, beta, dcut)
        self.margin, self.fit_window, self.ahead = margin, fit, ahead
        self.buf = []
        self.offset = 0.0
        self.interval = 1 / 30
        self.v = (0.0, 0.0)

    def reset(self):
        self.buf = []
        self.v = (0.0, 0.0)
        self.fx.init = self.fy.init = False

    def sample(self, t, p, now):
        if self.buf:
            dt = t - self.buf[-1][0]
            if dt <= 0:
                return
            self.interval += (min(0.1, max(0.01, dt)) - self.interval) * 0.1
            if dt >= 0.25:
                self.v = (0.0, 0.0)
            elif dt >= 0.012:
                lp = self.buf[-1][1]
                self.v = ((p[0] - lp[0]) / dt * 0.6 + self.v[0] * 0.4, (p[1] - lp[1]) / dt * 0.6 + self.v[1] * 0.4)
        off = now - t
        self.offset = off if not self.buf else self.offset + (off - self.offset) * 0.1
        self.buf = (self.buf + [(t, p)])[-24:]

    def evaluate(self, now, dt):
        b = self.buf
        t = now - self.offset - self.margin * self.interval
        if t <= b[0][0]:
            p = b[0][1]
        elif t >= b[-1][0]:
            a = min(t - b[-1][0], self.ahead)
            p = (b[-1][1][0] + self.v[0] * a, b[-1][1][1] + self.v[1] * a)
        else:
            i = next(i for i in range(1, len(b)) if b[i][0] >= t)
            k = (t - b[i - 1][0]) / (b[i][0] - b[i - 1][0])
            p = (b[i - 1][1][0] + (b[i][1][0] - b[i - 1][1][0]) * k, b[i - 1][1][1] + (b[i][1][1] - b[i - 1][1][1]) * k)
        if self.fit_window > 0 and len(b) >= 3:
            win = [s for s in b if s[0] >= b[-1][0] - self.fit_window]
            if len(win) < 4:
                win = b[-4:]
            tr = b[-1][0]
            x = min(max(t, win[0][0]), tr) - tr
            rows = [[(s[0] - tr) ** e for e in range(3)] for s in win]
            ata = [[sum(r[i] * r[j] for r in rows) for j in range(3)] for i in range(3)]
            try:
                out = []
                for k in range(2):
                    coef = cs.solve(ata, [sum(r[i] * s[1][k] for r, s in zip(rows, win)) for i in range(3)])
                    out.append(coef[0] + coef[1] * x + coef[2] * x * x)
                if all(math.isfinite(v) for v in out):
                    p = tuple(out)
            except ZeroDivisionError:
                pass
        return self.fx(p[0], dt), self.fy(p[1], dt)


# ---------------------------------------------------------------- the pens (mirror of PaintPen.cs)

AWAY, AIMING, PAINTING = "away", "aiming", "painting"


class EasyPen:
    """PaintPen.UpdateEasy: raise a hand and it paints. The brush goes down once the hand has arrived on the paper (it
    stayed within `stop_radius` for `stop_seconds`, or heads `turn_deg` away from the way it came in, or `timeout`
    passed); it lifts when the hand leaves the paper, rests or is lost. Off the edge and back within `back` seconds
    (without resting) = the same stroke goes on at once. Lengths in torso lengths, `unit` = px per torso length."""

    def __init__(self, unit=SCALE, stop_radius=0.075, stop_seconds=0.14, turn_deg=65.0, heading=0.15, timeout=0.8,
                 back=0.6, margin=0.03):
        self.unit, self.stop_radius, self.stop_seconds, self.turn_deg = unit, stop_radius, stop_seconds, turn_deg
        self.heading, self.timeout, self.back, self.margin = heading, timeout, back, margin
        self.state, self.on_paper, self.left_at = AWAY, False, -1e9
        self.arrival, self.lift = "", ""

    def update(self, t, p, tracked, rest, inside):
        """inside = px inside the paper (negative outside). Returns +1 when the brush goes down, -1 when it lifts."""
        before = self.state
        if not tracked or rest:
            if before == PAINTING:
                self.lift = "rest" if tracked else "lost"
            self.state, self.on_paper, self.left_at = AWAY, False, -1e9
            return self._change(before)
        margin = self.margin * self.unit
        was_on = self.on_paper
        self.on_paper = inside >= (-margin if was_on else margin)
        if not self.on_paper:
            if before == PAINTING:
                self.lift, self.left_at = "off", t
            self.state = AIMING
            return self._change(before)
        if not was_on:
            if t - self.left_at <= self.back:
                self.left_at, self.arrival, self.state = -1e9, "back", PAINTING
                return self._change(before)
            self.entry_t = self.anchor_t = t
            self.entry = self.anchor = p
            self.trail = []
            self.state = AIMING
        if self.state != PAINTING:
            arrived = self._arrived(t, p)
            if arrived:
                self.arrival, self.state = arrived, PAINTING
        return self._change(before)

    def _arrived(self, t, p):
        self.trail = (self.trail + [(t, p)])[-16:]
        if math.dist(p, self.anchor) > self.stop_radius * self.unit:
            self.anchor, self.anchor_t = p, t
        if t - self.anchor_t >= self.stop_seconds:
            return "stop"
        if t - self.entry_t >= self.timeout:
            return "timeout"
        came = (p[0] - self.entry[0], p[1] - self.entry[1])
        if math.hypot(*came) < self.heading * self.unit:
            return ""
        for qt, q in reversed(self.trail):
            if t - qt < 0.1:
                continue
            now = (p[0] - q[0], p[1] - q[1])
            if math.hypot(*now) >= 0.04 * self.unit:
                cos = (came[0] * now[0] + came[1] * now[1]) / (math.hypot(*came) * math.hypot(*now))
                if math.degrees(math.acos(max(-1.0, min(1.0, cos)))) > self.turn_deg:
                    return "turn"
            break
        return ""

    def _change(self, before):
        return 1 if self.state == PAINTING and before != PAINTING else -1 if self.state != PAINTING and before == PAINTING else 0


def leaving_since(history, now, tail_speed, down_speed, down):
    """PaintPen.LeavingSince: the brush lifted because the hand left. From which moment on was it leaving, not
    painting? history = [(t, speed px/s, downward speed px/s, ...)] while painting, oldest first. None = it just
    stopped where it was."""
    def leaving(h):
        return h[1] >= tail_speed or (down and h[2] >= down_speed)
    k = len(history) - 1
    while k >= 0 and not leaving(history[k]) and now - history[k][0] < 0.15:
        k -= 1
    if k < 0 or not leaving(history[k]):
        return None
    while k > 0 and leaving(history[k - 1]):
        k -= 1
    return history[k][0] - 0.05


def inside_paper(p, paper):
    return min(p[0] - paper[0], paper[2] - p[0], p[1] - paper[1], paper[3] - p[1])


def paint_easy(frames, paper, unit=SCALE):
    """PaintGame.Apply the easy way, per frame: 'away', 'aiming', 'painting' or 'wiped' (painted, then taken back as
    the hand left). Returns (states, how each stroke's brush went down)."""
    pen = EasyPen(unit)
    states, history, arrivals = [], [], []
    prev, v, pending = None, (0.0, 0.0), None

    def settle(now, down):
        since = leaving_since(history, pending[0], LEAVING_SPEED * unit, DROPPING_SPEED * unit, down)
        if since is not None:
            for ht, _, _, hi in history:
                if ht >= since and now - ht <= WET:
                    states[hi] = "wiped"

    for i, (t, p, b, rest) in enumerate(frames):
        if p is not None and prev is not None and prev[1] is not None and t > prev[0]:
            dt = t - prev[0]
            k = 1 - math.exp(-dt / 0.05)
            v = (v[0] + ((p[0] - prev[1][0]) / dt - v[0]) * k, v[1] + ((p[1] - prev[1][1]) / dt - v[1]) * k)
        elif p is None:
            v = (0.0, 0.0)
        on_screen = p is not None and 0 <= p[0] <= SCREEN_W and 0 <= p[1] <= SCREEN_H
        change = pen.update(t, p, on_screen, rest, inside_paper(p, paper) if p else -1e9)
        resumed = False
        if pending:
            if change > 0 and pen.arrival == "back":
                pending, resumed = None, True
            elif change > 0 or t - pending[0] > LEFT_AFTER or pen.state == AWAY:
                settle(t, pending[1] or pen.state == AWAY)
                pending = None
        if change > 0 and not resumed:
            history = []
            arrivals.append(pen.arrival)
        states.append(pen.state)
        if change < 0:
            pending = (t, pen.lift != "off" or p[1] > paper[3] - 0.05 * unit)
            if pen.lift != "off":
                settle(t, True)
                pending = None
        if pen.state == PAINTING:
            history.append((t, math.hypot(*v), v[1], i))
        prev = (t, p)
    return states, arrivals


def paint_switch(frames, switch_at, paper):
    """PaintPen.UpdateSwitch: the hand paints while the other wrist is raised (and it is on the paper, not resting)."""
    states, strokes, was = [], 0, False
    for t, p, b, rest in frames:
        on = p is not None and not rest and inside_paper(p, paper) >= 0 and switch_at(t)
        states.append(PAINTING if on else AWAY if p is None or rest else AIMING)
        strokes += on and not was
        was = on
    return states, ["switch"] * strokes


# ---------------------------------------------------------------- mappings: torso lengths -> screen px (y down)

def zone_map(b):
    """Until 2026-11-10: the zone (1.6 torso lengths to each side, 1.3 below the shoulders to 1.3 above) = the screen."""
    return (0.5 + b[0] / 3.2) * SCREEN_W, (1 - (b[1] + 1.3) / 2.6) * SCREEN_H


def easel_map(b):
    return SCREEN_W / 2 + b[0] * SCALE, SHOULDER_Y - b[1] * SCALE


EASEL_PAPER = (SCREEN_W / 2 - PAPER_W / 2 * SCALE, SHOULDER_Y - PAPER_ABOVE * SCALE,
               SCREEN_W / 2 + PAPER_W / 2 * SCALE, SHOULDER_Y + (PAPER_H - PAPER_ABOVE) * SCALE)
MAPS = {"easel": (easel_map, EASEL_PAPER, 4.0 / 2.9), "zone": (zone_map, (0, 0, SCREEN_W, SCREEN_H), 4.0 / 2.9)}


# ---------------------------------------------------------------- the replay

def replay(packets, side, mapping, beta, guard=False, rope=6.0, fps=60.0):
    """One hand's cursor every frame: [(t, (x, y) px or None, body point, resting)]. (The brush's One Euro works on
    torso lengths: 2 Hz, `beta` per torso length a second = 4 per zone width before.)"""
    body, interp, g = BodyFrame(), Interp(2.0, beta, 2.0), BrushGuard() if guard else None
    frames, k, tip, rest = [], 0, None, False
    seen, last_good = False, -1.0
    t, prev = packets[0][0], None
    while t < packets[-1][0]:
        while k < len(packets) and packets[k][0] <= t:
            _, c, j, v = packets[k]
            k += 1
            body.update(j, c)
            if v[side + "w"] < (0.3 if seen and t - last_good <= 0.3 else 0.5):
                continue
            seen, last_good = True, t
            rest = resting(j, side, body.unit)
            p = body.to_body(hand_point(j, side, body))
            for st, sp in (g.push(c, p) if g else [(c, p)]):
                interp.sample(st, sp, t)
        if not seen or t - last_good > 0.3 or not interp.buf:
            if interp.buf:
                interp.reset()
                if g:
                    g.reset()
            tip, seen = None, False
            frames.append((t, None, None, False))
        else:
            b = interp.evaluate(t, t - prev if prev is not None else 0.0)
            px = mapping(b)
            if tip is None:
                tip = px
            d = math.dist(px, tip)
            if d > rope:
                tip = (tip[0] + (px[0] - tip[0]) * (1 - rope / d), tip[1] + (px[1] - tip[1]) * (1 - rope / d))
            frames.append((t, tip, b, rest))
        prev, t = t, t + 1 / fps
    return frames, g


def switch_of(packets, side="l", on=0.6, off=0.2):
    """PaintGame's switch: is this hand's wrist raised (above `on` of the way from the hips to the shoulders; down
    again below `off`)? Returns a function of time."""
    import bisect
    times, states, up = [], [], False
    for a, c, j, v in packets:
        hips, shoulders = cs.mid(j["lh"], j["rh"]), cs.mid(j["ls"], j["rs"])
        if v[side + "w"] >= 0.3:
            h = (j[side + "w"][1] - hips[1]) / max(math.dist(hips, shoulders), 1e-3)
            up = h > (off if up else on)
        times.append(a)
        states.append(up)
    return lambda t: states[max(0, bisect.bisect_right(times, t - 0.07) - 1)]


COLOURS = {"r": (47, 107, 255), "l": (255, 90, 78)}


def draw(d, hands, paper, t0, t1, origin=(0.0, 0.0), k=1.0, width=36):
    """The strokes between t0 and t1 on a sheet drawn at `origin`, scaled by k."""
    for side, (frames, states) in hands.items():
        prev = None
        for (t, p, b, rest), s in zip(frames, states):
            if not t0 <= t < t1:
                continue
            if p is None or s == AWAY:
                prev = None
                continue
            q = (origin[0] + (p[0] - paper[0]) * k, origin[1] + (p[1] - paper[1]) * k)
            if prev is not None:
                if s == PAINTING:
                    d.line([prev, q], fill=COLOURS[side], width=max(2, int(width * k)))
                elif s == "wiped":
                    d.line([prev, q], fill=(255, 170, 60), width=max(1, int(4 * k)))
                else:
                    d.line([prev, q], fill=(150, 150, 160), width=max(1, int(3 * k)))
            prev = q


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("recording", nargs="?", help="a recorded session (.jsonl)")
    ap.add_argument("--script", help="replay this fake_sender script instead of a recording (paint, paint2, paint_page)")
    ap.add_argument("--glitches", type=float, default=0.0, help="with --script: the box's pose-model glitches a second")
    ap.add_argument("--pen", choices=("easy", "switch", "always"), default="easy")
    ap.add_argument("--map", choices=tuple(MAPS), default="easel")
    ap.add_argument("--guard", action="store_true", help="the jump gate before the brush (not adopted)")
    ap.add_argument("--sheet", type=float, default=0.0, help="panels of this many seconds instead of one picture")
    ap.add_argument("--from", dest="t_from", type=float, default=0.0)
    ap.add_argument("--to", dest="t_to", type=float, default=1e9)
    ap.add_argument("--hands", default="rl")
    ap.add_argument("--out", default=os.path.join(os.path.dirname(__file__), "out"))
    args = ap.parse_args()
    if not args.recording and not args.script:
        ap.error("give a recording or --script")
    from PIL import Image, ImageDraw

    path = record_script(args.script, args.glitches) if args.script else args.recording
    packets = load(path, args.t_from, args.t_to)
    name = (args.script + ("_glitches" if args.glitches > 0 else "")) if args.script else os.path.splitext(os.path.basename(path))[0]
    print(f"{name}: {len(packets)} packets, {packets[-1][1] - packets[0][1]:.1f} s")
    mapping, paper, beta = MAPS[args.map]
    hands = {}
    for side in ("r" if args.pen == "switch" else args.hands):
        frames, g = replay(packets, side, mapping, beta, args.guard)
        if args.pen == "easy":
            states, arrivals = paint_easy(frames, paper)
        elif args.pen == "switch":
            states, arrivals = paint_switch(frames, switch_of(packets), paper)
        else:
            states = [PAINTING if p is not None and not rest and inside_paper(p, paper) >= 0 else AWAY for _, p, _, rest in frames]
            arrivals = []
        hands[side] = (frames, states)
        count = {s: states.count(s) for s in (AWAY, AIMING, PAINTING, "wiped")}
        seconds = {s: round(n / 60.0, 1) for s, n in count.items()}
        extra = f"; the guard dropped {g.dropped} samples and held back {g.released}" if g else ""
        print(f"  {'right' if side == 'r' else 'left'} hand: {len(arrivals)} strokes "
              f"{ {a: arrivals.count(a) for a in sorted(set(arrivals))} }, seconds {seconds}{extra}")

    os.makedirs(args.out, exist_ok=True)
    out = os.path.join(args.out, f"paint_{name}_{args.map}_{args.pen}.png")
    t0 = min(f[0][0] for f, _ in hands.values())
    t1 = max(f[-1][0] for f, _ in hands.values())
    pw, ph = paper[2] - paper[0], paper[3] - paper[1]
    if args.sheet > 0:
        n, cols, k = max(1, math.ceil((t1 - t0) / args.sheet)), 5, 0.36
        cw, ch = int(pw * k) + 12, int(ph * k) + 12
        img = Image.new("RGB", (cols * cw + 12, math.ceil(n / cols) * ch + 40), (20, 25, 56))
        d = ImageDraw.Draw(img)
        d.text((12, 10), f"{name}   {args.map}, {args.pen} pen   ({args.sheet:g} s a panel)", fill=(255, 255, 255))
        for i in range(n):
            ox, oy = 12 + (i % cols) * cw, 34 + (i // cols) * ch
            d.rounded_rectangle((ox, oy, ox + pw * k, oy + ph * k), 8, fill=(255, 248, 238))
            d.text((ox + 6, oy + 4), f"{t0 + i * args.sheet:.0f} s", fill=(120, 120, 140))
            draw(d, hands, paper, t0 + i * args.sheet, t0 + (i + 1) * args.sheet, (ox, oy), k, 26)
    else:
        img = Image.new("RGB", (SCREEN_W, SCREEN_H), (20, 25, 56))
        d = ImageDraw.Draw(img)
        d.rectangle(paper, fill=(255, 248, 238))
        d.text((30, 20), f"{name}   {args.map}, {args.pen} pen", fill=(255, 255, 255))
        draw(d, hands, paper, t0, t1 + 1, (paper[0], paper[1]))
    img.save(out)
    print("  ", out)


if __name__ == "__main__":
    main()
