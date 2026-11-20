"""Zana!'s wand on real box recordings: how often the cursor jumps where the hand didn't, scored against the heavy
reference model (2026-11-17; the user: "it jumps a lot during drawing the spell").

    python tools/motion/wand_replay.py <teacher.npz> <run.jsonl|run.npz> ... [--variants now,before,...|all] [--last N]

The runs are the box engine's output on a recorded clip (pose_npu_test --clip: `<clip>.<model>.jsonl`, COCO-17 joints)
or a tracklab run (.npz: all 33 landmarks, for the hand points); the teacher is MediaPipe's heavy model on six crops a
frame (tools/npu/tracklab.py run <clip> --model heavy --variant teacher). Clips and results of 2026-11-17:
`D:/rknn/bench/spells` (the user drawing spells, spells-20261117) and `D:/rknn/bench` (routine-20261101).

The game's pipeline, mirrored: BodyTracker's smooth shoulders and torso length (FairyGame.ReadBody), the hand's point
round the shoulders' middle in torso lengths, Wand's zone, raise rule and visibility gate, HandInterpolator on the
camera clock (Margin, glide, the per-frame One Euro) evaluated every game frame at 30 fps with the samples arriving 70
ms after capture. The reference: the teacher's same point through the same zone, smoothed both ways in time (a centred
5-sample quadratic fit: no lag), at the camera moment each frame shows. Scored on frames where the teacher sees the
hand raised and is sure of the wrist:

- jumps / min: game frames whose cursor step differs from the reference's step by more than 40 / 80 / 150 HUD px (of
  1920 x 1080), per minute of wand time: what the user sees as the wand jumping;
- error: the cursor's distance from the reference (median, 90th percentile, share over 150 px);
- lag: the reference's time shift that fits the cursor best (17 ms steps).

Findings (docs/hardware.md "Zana!'s wand jumps"): the point (`wrist` against the old `tip`, the wrist pushed 0.3 of the
forearm out) halved the jumps; the One Euro's derivative cutoff 4 Hz (was 2) trimmed lag and jumps a little more, and
the cursor a camera frame behind instead of half (between real samples, not guessing past the newest) most of the
biggest ones left; a lower visibility gate, median of three, a prediction gate, a Kalman filter, the mirrored crop
averaged in (twice the NPU) or alternated: no better or worse.
"""
import argparse
import json
import math
import os

import numpy as np

COCO = (0, 2, 5, 7, 8, 11, 12, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28)   # COCO-17 in MediaPipe's 33
SH, EL, WR, PK, IX, TH, HIP = (11, 12), (13, 14), (15, 16), (17, 18), (19, 20), (21, 22), (23, 24)
HUD_W, HUD_H = 1920.0, 1080.0
LATENCY = 0.07
GAME_FPS = 30.0

# the wand as it is (Wand.cs, FairyGame.ReadBody) and as it was before 2026-11-17 evening
NOW = dict(point='wrist', zone=(1.25, 0.36, -0.82, 0.83), raise_=(0.33, 0.15), filter=(1.0, 4.0, 4.0), margin=1.0)
VARIANTS = {
    'now': NOW,
    'before': dict(point='tip', zone=(1.25, 0.3, -0.75, 0.9), raise_=(0.4, 0.22), filter=(1.0, 4.0, 2.0), margin=0.5),
    'wrist only': dict(NOW, filter=(1.0, 4.0, 2.0), margin=0.5),
    'now+tip': dict(NOW, point='tip', zone=(1.25, 0.3, -0.75, 0.9), raise_=(0.4, 0.22)),
    'now+filter1/4/2': dict(NOW, filter=(1.0, 4.0, 2.0)),
    'now+margin0.5': dict(NOW, margin=0.5),
    'now+palm': dict(NOW, point='palm'),
    'now+vis0.3': dict(NOW, vis=0.3),
}


# ----------------------------------------------------------------------------------------------------------- loading
def load_run(path):
    """xy (n, 33, 2) pixels (NaN where a run has no such landmark), vis (n, 33), t (n,) seconds, size."""
    if path.endswith('.npz'):
        d = np.load(path)
        xy = d['tta_xy'] if 'tta_xy' in d.files else d['f_xy']
        vis = d['f_vis'] if 'f_vis' in d.files and 'tta_xy' not in d.files else np.ones(xy.shape[:2], np.float32)
        out = dict(xy=xy.astype(np.float64), vis=vis, t=d['t'] - d['t'][0], size=tuple(int(v) for v in d['size']))
        if 'tta_spread' in d.files:
            out['spread'] = d['tta_spread']
        return out
    lines = [json.loads(line) for line in open(path, encoding='utf-8')]
    n = max(p['f'] for p in lines) + 1
    xy = np.full((n, 33, 2), np.nan)
    vis = np.zeros((n, 33), np.float32)
    t = np.full(n, np.nan)
    w = h = 0
    for p in lines:
        f, w, h = p['f'], p['w'], p['h']
        t[f] = (p['t'] - lines[0]['t']) / 1000.0
        if p.get('people'):
            k = np.array(p['people'][0]['k'], np.float64).reshape(17, 4)
            xy[f, COCO, 0], xy[f, COCO, 1] = k[:, 0] * w, k[:, 1] * h
            vis[f, COCO] = k[:, 3]
    return dict(xy=xy, vis=vis, t=t, size=(w, h))


# ------------------------------------------------------------------------------------------------------ the game
class OneEuro:
    def __init__(self, mc, beta, dc):
        self.mc, self.beta, self.dc = mc, beta, dc
        self.x, self.dx = None, 0.0

    @staticmethod
    def alpha(c, dt):
        return 1 / (1 + 1 / (2 * math.pi * c * dt))

    def __call__(self, v, dt, scale=1.0):
        if self.x is None or dt <= 0:
            self.x, self.dx = v, 0.0
            return v
        self.dx += ((v - self.x) / dt - self.dx) * self.alpha(self.dc, dt)
        self.x += (v - self.x) * self.alpha(self.mc + self.beta * abs(self.dx) / max(scale, 1e-4), dt)
        return self.x


class Cursor:
    """HandInterpolator: samples on the camera clock, the cursor `margin` camera frames behind the newest, gliding past
    it at most `glide` s, then the per-frame One Euro."""

    def __init__(self, margin, glide, f):
        self.margin, self.glide = margin, glide
        self.times, self.pos = [], []
        self.offset, self.interval, self.vel = None, 1 / 30, np.zeros(2)
        self.fx, self.fy = OneEuro(*f), OneEuro(*f)

    def sample(self, t, p, now):
        if self.times:
            dt = t - self.times[-1]
            if dt <= 0:
                return
            self.interval += (min(max(dt, 0.01), 0.1) - self.interval) * 0.1
            if dt >= 0.25:
                self.vel = np.zeros(2)
            elif dt >= 0.012:
                v = (p - self.pos[-1]) / dt
                self.vel = v + (self.vel - v) * 0.4
        off = now - t
        self.offset = off if self.offset is None else self.offset + (off - self.offset) * 0.1
        self.times.append(t)
        self.pos.append(np.asarray(p, float))
        del self.times[:-24], self.pos[:-24]

    def moment(self, now):
        return now - self.offset - self.margin * self.interval

    def evaluate(self, now, dt):
        t = self.moment(now)
        if t <= self.times[0]:
            p = self.pos[0]
        elif t >= self.times[-1]:
            p = self.pos[-1] + self.vel * min(t - self.times[-1], self.glide)
        else:
            i = int(np.searchsorted(self.times, t))
            k = (t - self.times[i - 1]) / (self.times[i] - self.times[i - 1])
            p = self.pos[i - 1] + (self.pos[i] - self.pos[i - 1]) * k
        return np.array([self.fx(p[0], dt), self.fy(p[1], dt)])


def view(xy, size):
    """BodyTracker's view space: x to the player's right (the picture mirrored), y up, units of the picture's height."""
    w, h = size
    return np.stack([(w - xy[..., 0]) / h, (h - xy[..., 1]) / h], -1)


def hand_points(run, kind):
    """(n, 2 hands, 2) the point each hand drives the wand with, in view space."""
    p, vis = view(run['xy'], run['size']), run['vis']
    out = np.full((len(p), 2, 2), np.nan)
    for k in range(2):
        wr, el = p[:, WR[k]], p[:, EL[k]]
        if kind == 'wrist':
            out[:, k] = wr
        elif kind == 'tip':                      # Body.HandTip(raw, elbow, wrist, 0.3)
            ext = 0.3 * np.clip((vis[:, EL[k]] - 0.2) / 0.2, 0, 1)
            out[:, k] = wr + (wr - el) * ext[:, None]
        elif kind == 'palm':
            out[:, k] = (wr + p[:, PK[k]] + p[:, IX[k]] + p[:, TH[k]]) / 4
    return out


def body_units(run):
    """Per sample: the smooth shoulders' middle and the torso unit (BodyTracker's smooth One Euro 0.5 / 1.5 / 1 on the
    shoulders and hips, body-relative; FairyGame's unit eased 0.1 toward the smooth torso length)."""
    p, t = view(run['xy'], run['size']), run['t']
    n = len(t)
    filters = [OneEuro(0.5, 1.5, 1.0) for _ in range(8)]
    centre = np.full((n, 2), np.nan)
    unit = np.full(n, np.nan)
    scale = u = last = None
    for i in range(n):
        joints = p[i, [SH[0], SH[1], HIP[0], HIP[1]]]
        if not (np.isfinite(t[i]) and np.all(np.isfinite(joints))):
            continue
        dt = t[i] - last if last is not None else 0.0
        last = t[i]
        torso = float(np.linalg.norm(joints[:2].mean(0) - joints[2:].mean(0)))
        scale = torso if scale is None else scale + (max(torso, 0.02) - scale) * 0.05
        s = np.array([filters[2 * j + a](joints[j, a], dt, scale) for j in range(4) for a in range(2)]).reshape(4, 2)
        centre[i] = s[:2].mean(0)
        ts = float(np.linalg.norm(s[:2].mean(0) - s[2:].mean(0)))
        u = ts if u is None else u + (ts - u) * 0.1
        unit[i] = u
    return centre, unit


def to_screen(hand, q, zone):
    """Wand.ToScreen, in HUD pixels (y down)."""
    half, inward, bottom, top = zone
    side = 1.0 if hand == 1 else -1.0
    x = 0.5 + (q[..., 0] - side * inward) / (2 * half)
    y = (q[..., 1] - bottom) / (top - bottom)
    return np.stack([x * HUD_W, (1 - y) * HUD_H], -1)


def play(run, v, gate=True):
    """The wand every game frame: (moment on the camera clock, hand, HUD point) or None while it's down."""
    t = run['t']
    centre, unit = body_units(run)
    q = (hand_points(run, v['point']) - centre[:, None]) / unit[:, None, None]
    wvis = run['vis'][:, list(WR)] if gate else np.ones((len(t), 2))
    raise_on, raise_off = v['raise_']
    cursors = [Cursor(v.get('margin', 0.5), v.get('glide', 0.08), v['filter']) for _ in range(2)]
    up, seen, hand = [False, False], [-10.0, -10.0], 1
    frames = []
    valid = np.isfinite(t)
    now, end = t[valid][0] + LATENCY, t[valid][-1] + LATENCY
    i = 0
    while now <= end:
        while i < len(t) and (not valid[i] or t[i] + LATENCY <= now):
            if valid[i] and np.all(np.isfinite(q[i])):
                for k in range(2):
                    if wvis[i, k] < v.get('vis', 0.45):
                        continue
                    seen[k] = t[i] + LATENCY
                    above = q[i, k, 1] + 1.0
                    up[k] = above > raise_off if up[k] else above > raise_on
                    # in screens (the cursor's filter works in screen units, like Wand's)
                    cursors[k].sample(t[i], to_screen(k, q[i, k], v['zone']) / [HUD_W, HUD_H], t[i] + LATENCY)
                if not up[hand] and up[1 - hand]:
                    hand = 1 - hand
            i += 1
        shown = [None, None]
        for k in range(2):
            if now - seen[k] > 0.35:
                up[k] = False
            if cursors[k].times:
                shown[k] = cursors[k].evaluate(now, 1 / GAME_FPS)
        if not up[hand] and up[1 - hand]:
            hand = 1 - hand
        if up[hand] and cursors[hand].times:
            frames.append((cursors[hand].moment(now), hand, np.clip(shown[hand], -0.03, 1.03) * [HUD_W, HUD_H]))
        else:
            frames.append(None)
        now += 1 / GAME_FPS
    return frames


# --------------------------------------------------------------------------------------------------------- scoring
def reference(teacher, v):
    """The teacher's point through the same zone, smoothed both ways (5-sample quadratic fit), (n, 2, 2) HUD px."""
    centre, unit = body_units(teacher)
    q = (hand_points(teacher, v['point']) - centre[:, None]) / unit[:, None, None]
    s = np.stack([to_screen(k, q[:, k], v['zone']) for k in range(2)], 1)
    out = s.copy()
    w = np.array([-3, 12, 17, 12, -3]) / 35.0
    for i in range(2, len(s) - 2):
        win = s[i - 2:i + 3]
        if np.all(np.isfinite(win)):
            out[i] = np.tensordot(w, win, axes=(0, 0))
    return out


def at(path, t, hand, moment):
    i = int(np.searchsorted(t, moment))
    if i <= 0 or i >= len(t):
        return None
    a, b = path[i - 1, hand], path[i, hand]
    if not (np.all(np.isfinite(a)) and np.all(np.isfinite(b))):
        return None
    return a + (b - a) * (moment - t[i - 1]) / (t[i] - t[i - 1])


def score(frames, ref, t, raised):
    errs, steps, lag_err = [], [], {}
    prev = None
    for fr in frames:
        if fr is None:
            prev = None
            continue
        moment, hand, pos = fr
        i = min(int(np.searchsorted(t, moment)), len(t) - 1)
        r = at(ref, t, hand, moment)
        if r is None or not raised[i, hand]:
            prev = None
            continue
        errs.append(np.linalg.norm(pos - r))
        for lag in np.arange(0, 0.101, 1 / 60):
            rl = at(ref, t, hand, moment - lag)
            if rl is not None:
                lag_err.setdefault(round(lag, 3), []).append(np.linalg.norm(pos - rl))
        if prev is not None and prev[0] == hand:
            steps.append(np.linalg.norm((pos - prev[1]) - (r - prev[2])))
        prev = (hand, pos, r)
    errs, steps = np.array(errs), np.array(steps)
    minutes = max(len(steps), 1) / GAME_FPS / 60
    lag = min(lag_err, key=lambda k: np.median(lag_err[k])) if lag_err else float('nan')
    return dict(minutes=minutes, err=np.median(errs), p90=np.percentile(errs, 90), over150=np.mean(errs > 150) * 100,
                j40=np.sum(steps > 40) / minutes, j80=np.sum(steps > 80) / minutes, j150=np.sum(steps > 150) / minutes,
                lag=lag * 1000)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('teacher')
    ap.add_argument('runs', nargs='+')
    ap.add_argument('--variants', default='now,before')
    ap.add_argument('--last', type=int, help='only the first N frames')
    a = ap.parse_args()
    teacher = load_run(a.teacher)
    names = list(VARIANTS) if a.variants == 'all' else a.variants.split(',')
    for path in a.runs:
        run = load_run(path)
        n = min(len(run['t']), len(teacher['t']), a.last or 10 ** 9)
        run = dict(run, xy=run['xy'][:n], vis=run['vis'][:n], t=run['t'][:n])
        ref_run = dict(teacher, xy=teacher['xy'][:n], vis=np.ones((n, 33)), t=run['t'].copy())
        # where the teacher sees the hand raised (its wrist over the old raise height) and is sure of the wrist
        centre, unit = body_units(ref_run)
        wq = (hand_points(ref_run, 'wrist') - centre[:, None]) / unit[:, None, None]
        sure = teacher['spread'][:n, list(WR)] < 0.08 * unit[:, None] * teacher['size'][1]
        raised = (wq[..., 1] + 1.0 > 0.33) & sure
        print(f'\n{os.path.basename(path)}')
        print(f'  {"variant":26s} {"jumps/min >40":>14s} {">80":>6s} {">150":>6s} | {"error med":>9s} {"p90":>5s} '
              f'{">150 px":>8s} | {"lag":>6s}')
        for name in names + ['(teacher, now)']:
            v = NOW if name == '(teacher, now)' else VARIANTS[name]
            if v['point'] == 'palm' and not np.isfinite(run['xy'][:, PK[0]]).any():
                continue                                  # a box run has no hand points
            ref = reference(ref_run, v)
            frames = play(ref_run if name.startswith('(') else run, v, gate=not name.startswith('('))
            s = score(frames, ref, run['t'], raised)
            print(f'  {name:26s} {s["j40"]:14.1f} {s["j80"]:6.1f} {s["j150"]:6.1f} | {s["err"]:9.0f} {s["p90"]:5.0f} '
                  f'{s["over150"]:7.1f}% | {s["lag"]:4.0f} ms   ({s["minutes"]:.2f} min)')


if __name__ == '__main__':
    main()
