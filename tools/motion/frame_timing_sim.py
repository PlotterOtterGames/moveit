"""How evenly does a moving joint (or the camera picture) move on the TV, on the X98K Pro's timing?

The pipeline (CLAUDE.md, docs/hardware.md; 2026-10-30/31): the USB camera at 29.8 fps (its UVC stats), the HAL's MJPEG
decode and CameraX's RGBA conversion (~14 ms, a little jitter), the analysis thread with KEEP_ONLY_LATEST (a frame that
arrives while the AI is busy waits; a newer one replaces it), the NPU's lite model ~19-25 ms a frame (+ the person
detector ~43 ms on the frame after the AI lost the person), the UDP packet, and MotionSystem handling it at the start of
the next game frame (30 fps 3D scenes, 60 fps camera test / menu with the remote / Bowling / Pikturo!).

A joint sweeping at a steady speed should move the same distance every frame. For each way of drawing it we count the
frames whose step is under half the even step ("stalls") or over 1.5 times ("jumps"):
  - "newest pose": the newest packet's joint as it arrived (the skeleton mirror until 2026-10-31);
  - "timeline": JointTimeline / HandInterpolator (samples on the capture clock, drawn half a camera frame behind the
    newest, gliding up to 80 ms), fed every packet or only the newest one each frame (the avatars and cursors poll
    body.SampleTime once a frame).
And for the camera picture (Vishu!, Pikturo!'s camera mode), repeated and skipped camera frames: the newest frame each
game frame (now) vs one held half a frame behind on the capture clock.

    python3 tools/motion/frame_timing_sim.py

2026-10-31: newest pose at 30 fps ~5 % stalls + ~4 % jumps with the AI keeping up, ~18 % + 14 % when it loses the
person twice a second; at 60 fps every frame of a move is a stall or a jump (each 30 Hz pose stays up two frames).
The timeline: under 1 % either way. The picture: 3-8 % of the frames repeated or skipped at 30 fps, ~0.8 % when held.
"""
import math
import random
import statistics as st

CAM_FPS = 29.8


def pipeline(seconds, rng, ai_ms=23.0, ai_sd=2.5, lost_per_s=0.0):
    """(arrival in the game, capture) of the pose packets."""
    period = 1.0 / CAM_FPS
    captures, t = [], 0.0
    while t < seconds:
        captures.append(t)
        t += period + rng.gauss(0, 0.0002)
    deliver = [(c + 0.014 + abs(rng.gauss(0, 0.0015)), c) for c in captures]   # HAL decode + RGBA conversion
    out, busy_until, waiting, i, tracking = [], 0.0, None, 0, True
    while i < len(deliver) or waiting is not None:
        if waiting is None:
            waiting = deliver[i]
            i += 1
        start = max(busy_until, waiting[0])
        while i < len(deliver) and deliver[i][0] <= start:   # KEEP_ONLY_LATEST
            waiting = deliver[i]
            i += 1
        cost = max(0.012, rng.gauss(ai_ms, ai_sd) / 1000)
        if not tracking:
            cost += 0.043                                     # the person detector first
            tracking = True
        busy_until = start + cost
        if lost_per_s and rng.random() < lost_per_s / CAM_FPS:
            tracking = False                                  # presence < 0.5: no pose from this frame
        else:
            out.append((busy_until + 0.0005 + abs(rng.gauss(0.0003, 0.0003)), waiting[1]))
        waiting = None
    return out


def game_frames(seconds, fps, rng):
    ts, t = [], 0.5
    while t < seconds:
        ts.append(t + rng.gauss(0, 0.0004))
        t += 1.0 / fps
    return ts


class Newest:
    def __init__(self):
        self.x = None

    def packet(self, cap, x, now):
        self.x = x

    def frame(self, now):
        return self.x


class Timeline:
    """JointTimeline (and HandInterpolator's timing) with the game's settings."""

    def __init__(self, every_packet=True, margin=0.5, glide=0.08):
        self.s, self.every, self.margin, self.glide = [], every_packet, margin, glide
        self.offset, self.interval, self.v, self.pending = None, 1 / 30, 0.0, None

    def packet(self, cap, x, now):
        if self.every:
            self.add(cap, x, now)
        else:
            self.pending = (cap, x, now)

    def add(self, cap, x, now):
        if self.s:
            dt = cap - self.s[-1][0]
            if dt <= 0:
                return
            self.interval += (min(max(dt, 0.01), 0.1) - self.interval) * 0.1
            self.v = 0.0 if dt >= 0.25 else (x - self.s[-1][1]) / dt * 0.6 + self.v * 0.4
        off = now - cap
        self.offset = off if self.offset is None else self.offset + (off - self.offset) * 0.1
        self.s = (self.s + [(cap, x)])[-6:]

    def frame(self, now):
        if self.pending:
            self.add(*self.pending)
            self.pending = None
        if not self.s:
            return None
        t = now - self.offset - self.margin * self.interval
        if t <= self.s[0][0]:
            return self.s[0][1]
        if t >= self.s[-1][0]:
            return self.s[-1][1] + self.v * min(t - self.s[-1][0], self.glide)
        for (t0, x0), (t1, x1) in zip(self.s, self.s[1:]):
            if t0 <= t <= t1:
                return x0 + (x1 - x0) * (t - t0) / (t1 - t0)


def evenness(make, fps, seconds=60.0, seed=1, **pipe):
    rng = random.Random(seed)
    packets = pipeline(seconds, rng, **pipe)
    view, k, shown = make(), 0, []
    for now in game_frames(seconds, fps, rng):
        while k < len(packets) and packets[k][0] <= now:
            view.packet(packets[k][1], packets[k][1], now)    # the joint's position = its capture time (1 unit/s)
            k += 1
        x = view.frame(now)
        if x is not None:
            shown.append(x)
    steps = [(b - a) * fps for a, b in zip(shown[60:], shown[61:])]
    return (sum(s < 0.5 for s in steps) / len(steps), sum(s > 1.5 for s in steps) / len(steps),
            math.sqrt(st.fmean([(s - 1) ** 2 for s in steps])))


def picture(fps, jitter, held, seconds=120.0, seed=1):
    """Repeated and skipped camera frames (30 fps), or frames not up for exactly 2 game frames (60 fps)."""
    rng = random.Random(seed)
    frames, t = [], 0.0
    while t < seconds:   # FrameShare.offer at the start of analyze(): after the decode and the conversion
        frames.append((t + 0.015 + abs(rng.gauss(0, jitter)), t))
        t += 1 / CAM_FPS + rng.gauss(0, 0.0002)
    k, ring, offset, interval, shown, now = 0, [], None, 1 / 30, [], 1.0
    while now < seconds:
        n = now + rng.gauss(0, 0.0004)
        while k < len(frames) and frames[k][0] <= n:
            cap = frames[k][1]
            if ring:
                interval += (min(max(cap - ring[-1], 0.01), 0.1) - interval) * 0.1
            off = n - cap
            offset = off if offset is None else offset + (off - offset) * 0.05
            ring = (ring + [cap])[-3:]
            k += 1
        if ring:
            if held:
                pick = [c for c in ring if c <= n - offset - 0.5 * interval + 0.004]
                shown.append(pick[-1] if pick else ring[0])
            else:
                shown.append(ring[-1])
        now += 1 / fps
    steps = [round((b - a) * CAM_FPS) for a, b in zip(shown, shown[1:])]
    if fps == 30:
        return steps.count(0) / len(steps), sum(s >= 2 for s in steps) / len(steps)
    runs, cur = [], 1
    for s in steps:
        if s == 0:
            cur += 1
        else:
            runs.append(cur)
            cur = 1
    return sum(r != 2 for r in runs) / len(runs), 0.0


def main():
    cases = [("the AI keeps up (lite, 23 +- 2.5 ms a frame)", {}),
             ("it loses the person twice a second (detector + landmarks on the next frame)", {"lost_per_s": 2.0}),
             ("slower AI (31 +- 4 ms: the full model, heat, a busy CPU)", {"ai_ms": 31.0, "ai_sd": 4.0})]
    views = [("newest pose as it arrived", Newest),
             ("timeline, every packet", lambda: Timeline(True)),
             ("timeline, newest packet per frame", lambda: Timeline(False))]
    for title, pipe in cases:
        print(f"\n== {title}")
        for fps in (30, 60):
            for name, make in views:
                res = [evenness(make, fps, seed=s, **pipe) for s in range(1, 6)]
                print(f"  {fps} fps  {name:34s} stalls {100 * st.fmean(r[0] for r in res):5.1f} %  "
                      f"jumps {100 * st.fmean(r[1] for r in res):5.1f} %  uneven (rms) {st.fmean(r[2] for r in res):.2f}")
    print("\n== the camera picture")
    for jitter in (0.0015, 0.004):
        for fps in (30, 60):
            for held in (False, True):
                res = [picture(fps, jitter, held, seed=s) for s in range(1, 6)]
                a, b = 100 * st.fmean(r[0] for r in res), 100 * st.fmean(r[1] for r in res)
                what = (f"repeated {a:4.1f} %, skipped {b:4.1f} % of the frames" if fps == 30
                        else f"camera frames not up for exactly 2 frames: {a:4.1f} %")
                print(f"  delivery jitter {jitter * 1000:.1f} ms, {fps} fps, {'held half a frame' if held else 'newest frame':17s} {what}")


if __name__ == "__main__":
    main()
