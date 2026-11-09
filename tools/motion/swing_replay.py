"""Tenis!'s swing detector against the pose model's glitches and small flicks (2026-11-06). An exact Python mirror of
SwingDetector.cs (raw wrists through a median of three, a swing measured from where the hand trustably was, confirmed
while its fastest moment is looked for: far enough, not all in one step, not straight back; gentle below 3 torso
lengths a second) and of the detector before it (the low-lag track, 3 torso lengths a second), to see on real
recordings and simulated arms what each one reads as a swing. The C# replayed the same recordings to the same counts
(2026-11-06), so numbers found here hold for the game; keep the two in step when changing either.

    motion-service-py/.venv/Scripts/python tools/motion/swing_replay.py recordings <recording.jsonl ...> [--list]
    motion-service-py/.venv/Scripts/python tools/motion/swing_replay.py agree <a.jsonl> <b.jsonl>
    motion-service-py/.venv/Scripts/python tools/motion/swing_replay.py sim

recordings  swings a minute each detector sees, how many the new one took for glitches and ignored; --list prints the
            new one's strong swings (box recordings: /sdcard/Android/data/al.tundu.games/files/sessions)
agree       two recordings of the very same frames by two models (tools/motion/clip_models.py on a box_clip.py clip):
            a swing seen in the first is real when the second model's wrist made the same move over those frames, else a
            glitch of the first model. 2026-11-06 on routine-20261101 (lite, judged by full): the old detector 63 real
            and 2 glitch swings, the new 122 real and 1 glitch (it also sees small moves)
sim         simulated arms (minimum-jerk moves, the box's noise model, the frames falling anywhere) with the glitches
            measured on the box (a wrist flipped away or pulled onto its elbow for a few frames): flicks found, false
            swings, a glitch in the middle of a swing
"""
import argparse
import json
import math

import numpy as np

L_SH, R_SH, L_EL, R_EL, L_WR, R_WR, L_HIP, R_HIP = 5, 6, 7, 8, 9, 10, 11, 12


# ------------------------------------------------------------------------------------------------ recordings

def load(path, person=0):
    """Samples in the SDK's view space: x = (1 - kx) * aspect, y = 1 - ky (image heights), times in seconds."""
    out = []
    for line in open(path, encoding='utf-8'):
        p = json.loads(line)
        ppl = p.get('people') or []
        if len(ppl) <= person:
            continue
        k = np.array(ppl[person]['k'], np.float64).reshape(17, 4)
        aspect = p['w'] / p['h']
        out.append(dict(t=p['t'] / 1000.0, f=p.get('f'), xy=np.stack([(1 - k[:, 0]) * aspect, 1 - k[:, 1]], 1), vis=k[:, 3]))
    return out


def torso_of(xy):
    return float(np.linalg.norm((xy[L_SH] + xy[R_SH]) / 2 - (xy[L_HIP] + xy[R_HIP]) / 2))


# ------------------------------------------------------------------------------------------------ the old detector

class OneEuro:
    def __init__(self, mincut, beta, dcut=1.0):
        self.mincut, self.beta, self.dcut = mincut, beta, dcut
        self.x, self.dx = None, 0.0

    @staticmethod
    def alpha(cut, dt):
        return 1.0 / (1.0 + 1.0 / (2 * math.pi * cut) / dt)

    def __call__(self, v, dt, scale=1.0):
        if self.x is None or dt <= 0:
            self.x, self.dx = np.array(v, np.float64), np.zeros(len(v))
            return self.x.copy()
        self.dx = self.dx + ((v - self.x) / dt - self.dx) * self.alpha(self.dcut, dt)
        cut = self.mincut + self.beta * np.abs(self.dx) / max(scale, 1e-4)
        self.x = self.x + (v - self.x) * np.array([self.alpha(c, dt) for c in cut])
        return self.x.copy()


def fast_track(samples):
    """BodyTracker's low-lag track (One Euro 1 / 4 / 1, scale = the slowly smoothed torso): what the old detector read."""
    filters, scale, last, out = [OneEuro(1.0, 4.0) for _ in range(17)], 0.0, None, []
    for s in samples:
        torso = max(torso_of(s['xy']), 0.02)
        scale = torso if scale <= 0 else scale + (torso - scale) * 0.05
        dt = s['t'] - last if last is not None else 0.0
        last = s['t']
        out.append(np.array([filters[j](s['xy'][j], dt, scale) for j in range(17)]))
    return out


class OldDetector:
    """SwingDetector.cs before 2026-11-06, exactly: the low-lag wrists faster than 3 torso lengths a second over ~0.08 s
    (the free hand up or down never a swing, one move one swing, a 0.22 s cooldown)."""

    def __init__(self, speed=3.0, right_handed=True):
        self.speed, self.right_handed = speed, right_handed
        self.hands = [self._hand(), self._hand()]
        self.last_swing = -10.0

    @staticmethod
    def _hand():
        return dict(t=[0.0] * 8, p=[np.zeros(2)] * 8, count=0, newest=-1, found=0.0, swung=-10.0, armed=True, pending=False,
                    n=0, peak=0.0, pv=np.zeros(2))

    @staticmethod
    def _direction(h):
        n = np.linalg.norm(h['pv'])
        return h['pv'] / n if n > 1e-4 else np.array([1.0, 0.0])

    def update(self, t, shoulders, torso, lw, lv, rw, rv):
        if torso < 1e-4:
            return None
        res = [self._sample(self.hands[0], t, (lw - shoulders) / torso, lv), self._sample(self.hands[1], t, (rw - shoulders) / torso, rv)]
        racket, free = (1, 0) if self.right_handed else (0, 1)
        if res[free] and (abs(self._direction(self.hands[free])[1]) >= 0.77 or self.hands[racket]['pending']):
            res[free] = False
        if not any(res):
            return None
        right = res[1] and (not res[0] or self.right_handed)
        h, other = self.hands[1 if right else 0], self.hands[0 if right else 1]
        other.update(armed=False, pending=False, swung=t)
        if h['swung'] - self.last_swing < 0.22:
            return None
        self.last_swing = h['swung']
        return dict(start=h['swung'], reported=t, right=right, speed=h['peak'], dir=self._direction(h), gentle=False)

    @staticmethod
    def _before(h, window):
        best = -1
        for k in range(1, h['count']):
            i = (h['newest'] - k + 8) % 8
            best = i
            if h['t'][h['newest']] - h['t'][i] >= window:
                break
        return best if best >= 0 and h['t'][h['newest']] - h['t'][best] >= window * 0.6 else -1

    def _sample(self, h, t, p, vis):
        if vis < 0.3:
            return False
        if h['count'] > 0:
            since = t - h['t'][h['newest']]
            if since < 0.012:
                return False
            if since > 0.25:
                h['count'] = 0
        if h['count'] == 0:
            h['found'] = t
        h['newest'] = (h['newest'] + 1) % 8
        h['t'][h['newest']], h['p'][h['newest']] = t, p
        h['count'] = min(h['count'] + 1, 8)
        then = self._before(h, 0.08)
        if then < 0 or t - h['found'] < 0.25:
            return False
        v = (p - h['p'][then]) / (t - h['t'][then])
        speed = float(np.linalg.norm(v))
        if h['pending']:
            h['n'] += 1
            if speed > h['peak']:
                h['peak'], h['pv'] = speed, v
            if speed >= h['peak'] * 0.9 and h['n'] < 3 and t - h['swung'] < 0.1:
                return False
            h['pending'] = False
            return True
        if not h['armed'] and (speed < 1.5 or np.dot(v, h['pv']) < 0 or t - h['swung'] >= 0.7):
            h['armed'] = True
        if not h['armed'] or t - h['swung'] < 0.22 or speed < self.speed:
            return False
        h.update(armed=False, swung=t, pending=True, n=0, peak=speed, pv=v)
        return False


# ------------------------------------------------------------------------------------------------ the detector (SwingDetector.cs)

class Detector:
    """SwingDetector.cs, number for number (its field names in the comments)."""
    SWING, STRONG, TRAVEL, WINDOW, ANCHOR_AGE = 1.6, 3.0, 0.18, 0.08, 0.12   # SwingSpeed, StrongSpeed, MinTravel, ...
    STEP_SHARE, MAX_STEP_SHARE, STEP_NOISE, SURE_VIS = 0.3, 0.7, 0.05, 0.8
    BACK_SHARE, BACK_SAMPLES, CONFIRM_WITHIN, REARM_SHARE = 0.45, 2, 0.2, 0.5
    REPEAT_AFTER, COOLDOWN, MIN_VIS, SETTLE, PEAK_SAMPLES, VERTICAL, LEAD, HISTORY = 0.7, 0.22, 0.6, 0.25, 3, 0.77, 0.05, 16

    def __init__(self, right_handed=True):
        self.right_handed = right_handed
        self.hands = [self._hand(), self._hand()]
        self.medians = [[], []]
        self.last_swing = -10.0
        self.ignored = 0

    @staticmethod
    def _hand():
        return dict(t=[], p=[], ok=[], vis=[], found=None, swung=-10.0, armed=True, pending=False, peak=0.0, pv=np.zeros(2),
                    dir=np.array([1.0, 0.0]), anchor=None, anchor_i=0, cand=0, max_out=0.0, back=0, slow=0, n=0, speed=0.0)

    def _median(self, k, t, p, v):
        buf = self.medians[k]
        buf.append((t, np.array(p, np.float64), v))
        del buf[:-3]
        if len(buf) < 3:
            return None
        (t0, a, va), (t1, b, vb), (t2, c, vc) = buf
        if t2 - t1 > 0.25 or t1 - t0 > 0.25:
            return t1, b, vb
        return t1, np.median(np.stack([a, b, c]), axis=0), min(vb, max(va, vc))

    def update(self, t, shoulders, torso, lw, lv, rw, rv):
        if torso < 1e-4:
            return None
        ml = self._median(0, t, (lw - shoulders) / torso, lv)
        mr = self._median(1, t, (rw - shoulders) / torso, rv)
        if ml is None or mr is None:
            return None
        t = ml[0]
        res = [self._sample(self.hands[0], t, ml[1], ml[2]), self._sample(self.hands[1], t, mr[1], mr[2])]
        racket, free = (1, 0) if self.right_handed else (0, 1)
        fh = self.hands[free]
        dfree = fh['pv'] / max(np.linalg.norm(fh['pv']), 1e-6)
        if res[free] and (abs(dfree[1]) >= self.VERTICAL or self.hands[racket]['pending']):
            res[free] = False
        if not any(res):
            return None
        right = res[1] and (not res[0] or self.right_handed)
        h, other = self.hands[1 if right else 0], self.hands[0 if right else 1]
        other.update(armed=False, pending=False, swung=t)
        if h['swung'] - self.last_swing < self.COOLDOWN:
            return None
        self.last_swing = h['swung']
        d = h['pv'] / max(np.linalg.norm(h['pv']), 1e-6)
        return dict(start=h['swung'], reported=t, right=right, speed=h['peak'], dir=d, gentle=h['peak'] < self.STRONG)

    def _reject(self, h):
        h['pending'] = False
        for i in range(h['cand'], len(h['ok'])):
            h['ok'][i] = False
        self.ignored += 1

    @staticmethod
    def _trusted_before(h, age):
        for i in range(len(h['t']) - 2, -1, -1):
            if h['ok'][i] and h['t'][-1] - h['t'][i] >= age:
                return i
        return -1

    def _sample(self, h, t, p, vis):
        if vis < self.MIN_VIS:
            if h['pending']:
                self._reject(h)
            return False
        if h['t']:
            since = t - h['t'][-1]
            if since < 0.012:
                return False
            last = next((i for i in range(len(h['t']) - 2, -1, -1) if h['ok'][i]), -1)
            if since > 0.25 or (last >= 0 and t - h['t'][last] > 0.3) or (last < 0 and len(h['t']) > 4):
                h.update(self._hand())
        if not h['t']:
            h['found'] = t
        for key, value in (('t', t), ('p', np.array(p, np.float64)), ('ok', True), ('vis', vis)):
            h[key].append(value)
        if len(h['t']) > self.HISTORY:
            for key in ('t', 'p', 'ok', 'vis'):
                h[key].pop(0)
            h['cand'], h['anchor_i'] = max(0, h['cand'] - 1), max(0, h['anchor_i'] - 1)
        if t - h['found'] < self.SETTLE or len(h['t']) < 3:
            h['speed'] = 0.0
            return False
        then = self._trusted_before(h, self.WINDOW * 0.6)
        if then < 0:
            return False
        v = (h['p'][-1] - h['p'][then]) / (t - h['t'][then])
        speed = h['speed'] = float(np.linalg.norm(v))
        if h['pending']:
            h['n'] += 1
            out = float(np.dot(h['p'][-1] - h['anchor'], h['dir']))
            h['max_out'] = max(h['max_out'], out)
            if speed > h['peak'] and np.dot(v, h['dir']) > 0:
                h['peak'], h['pv'] = speed, v
            h['back'] = h['back'] + 1 if out < self.BACK_SHARE * h['max_out'] else 0
            if h['back'] >= self.BACK_SAMPLES:
                self._reject(h)
                return False
            if h['back']:
                return False
            if speed >= h['peak'] * 0.9 and h['n'] < self.PEAK_SAMPLES and t - h['swung'] < 0.1:
                return False
            unsure = min(h['vis'][h['anchor_i']:]) < self.SURE_VIS
            if unsure and h['n'] < 2 and t - h['swung'] < self.CONFIRM_WITHIN:
                return False
            if h['max_out'] < self.TRAVEL:
                if t - h['swung'] < self.CONFIRM_WITHIN and np.dot(v, h['dir']) >= self.SWING * self.REARM_SHARE:
                    return False
                self._reject(h)
                return False
            dist = float(np.linalg.norm(h['p'][-1] - h['anchor']))
            biggest = max(np.linalg.norm(h['p'][i] - h['p'][i - 1]) for i in range(h['anchor_i'] + 1, len(h['p'])))
            if biggest > self.MAX_STEP_SHARE * dist + (0.0 if unsure else self.STEP_NOISE):
                self._reject(h)
                return False
            h['pending'] = False
            return True
        h['slow'] = h['slow'] + 1 if speed < self.SWING * self.REARM_SHARE else 0
        pk = h['pv'] / max(np.linalg.norm(h['pv']), 1e-6) if np.linalg.norm(h['pv']) > 1e-4 else np.zeros(2)
        along = float(np.dot(v, pk))
        if not h['armed'] and (h['slow'] >= 2 or along < -self.SWING * self.REARM_SHARE or (speed >= self.SWING and along < 0)
                               or t - h['swung'] >= self.REPEAT_AFTER):
            h['armed'] = True
        if not h['armed'] or t - h['swung'] < self.COOLDOWN or speed < self.SWING:
            return False
        a = self._trusted_before(h, self.ANCHOR_AGE)
        if a < 0 or not (h['ok'][-2] and h['ok'][-3]):
            return False
        d = v / speed
        need = self.STEP_SHARE * max(self.SWING, speed)
        for i in (-1, -2):
            if np.dot((h['p'][i] - h['p'][i - 1]) / (h['t'][i] - h['t'][i - 1]), d) < need:
                return False
        h.update(armed=False, swung=t, pending=True, n=0, peak=speed, pv=v, dir=d, anchor=h['p'][a].copy(), anchor_i=a,
                 cand=a + 1, back=0)
        h['max_out'] = float(np.dot(h['p'][-1] - h['anchor'], d))
        return False


def run(samples, detector, fast=None):
    """Every swing of a recording (`fast`: the old detector's low-lag track)."""
    out = []
    for i, s in enumerate(samples):
        xy = fast[i] if fast is not None else s['xy']
        sh = (xy[L_SH] + xy[R_SH]) / 2
        e = detector.update(s['t'], sh, torso_of(xy), xy[L_WR], s['vis'][L_WR], xy[R_WR], s['vis'][R_WR])
        if e:
            e['i'] = i
            out.append(e)
    return out


# ------------------------------------------------------------------------------------------------ commands

def recordings(paths, show):
    for path in paths:
        s = load(path)
        if len(s) < 30:
            continue
        span = (s[-1]['t'] - s[0]['t']) / 60
        old = run(s, OldDetector(), fast_track(s))
        d = Detector()
        new = run(s, d)
        strong = [e for e in new if not e['gentle']]
        print(f'{path}: {span * 60:.0f} s; old {len(old) / span:.1f} swings a minute; new {len(new) / span:.1f} '
              f'({len(strong) / span:.1f} strong), {d.ignored} glitches ignored')
        if show:
            for e in strong:
                print(f'    {"R" if e["right"] else "L"} {e["start"] - s[0]["t"]:6.2f} s {e["speed"]:4.1f} torso/s '
                      f'({e["dir"][0]:+.2f}, {e["dir"][1]:+.2f})')


def agree(path_a, path_b):
    by_frame = lambda p: {s['f']: s for s in load(p)}   # noqa: E731
    a, b = by_frame(path_a), by_frame(path_b)
    s = [a[f] for f in sorted(a)]

    def rel(frame, w):
        xy = frame['xy']
        return (xy[w] - (xy[L_SH] + xy[R_SH]) / 2) / torso_of(xy)

    def real(e):
        w = R_WR if e['right'] else L_WR
        f1 = s[e['i']]['f']
        f0 = f1 - 5
        if f0 not in a or f0 not in b or f1 not in b:
            return None
        da, db = rel(a[f1], w) - rel(a[f0], w), rel(b[f1], w) - rel(b[f0], w)
        if np.linalg.norm(da) < 1e-6:
            return None
        return float(np.dot(db, da) / np.dot(da, da)) >= 0.5 and np.linalg.norm(db - da) <= 0.6 * np.linalg.norm(da)

    new = run(s, Detector())
    for name, swings in (('old', run(s, OldDetector(), fast_track(s))), ('new, all', new),
                         ('new, strong', [e for e in new if not e['gentle']])):
        labels = [real(e) for e in swings]
        print(f'{name:12s}: {len(swings)} swings, real {labels.count(True)}, glitch {labels.count(False)}')


TORSO, SHOULDERS, ELBOW = 0.25, np.array([0.9, 0.62]), np.array([0.42, -0.62])


def minjerk(k):
    k = min(max(k, 0.0), 1.0)
    return k * k * k * (10 - 15 * k + 6 * k * k)


def move(a, b, seconds, start=1.0):
    a, b = np.array(a, np.float64), np.array(b, np.float64)
    return lambda t: a + (b - a) * minjerk((t - start) / seconds)


def simulate(right, seconds=2.5, fps=30.0, seed=1, glitches=(), phase=0.0):
    """(old swings, new swings) of a right hand along `right` (torso lengths from the shoulders, mirrored screen), with
    the box-like noise and `glitches` (time, 'flip' or 'collapse', size, frames)."""
    rng = np.random.default_rng(seed)
    old, new = OldDetector(), Detector()
    filters = [OneEuro(1, 4) for _ in range(3)]
    left = np.array([-0.45, -0.95])
    out_old, out_new, last = [], [], None
    for i in range(int(seconds * fps)):
        t = i / fps + phase
        r = right(t)
        speed = np.linalg.norm(r - last) * fps if last is not None else 0.0
        last, seen, vis = r, r.copy(), 0.95
        for at, kind, size, frames in glitches:
            first = math.ceil((at - phase) * fps - 1e-6)
            if first <= i < first + frames:
                seen = r + (ELBOW - r) * size if kind == 'collapse' else r + np.array([math.cos(size * 7.3), math.sin(size * 7.3)]) * size
                vis = 0.8
        sh = SHOULDERS + rng.normal(0, 0.006 * TORSO, 2)
        lw = SHOULDERS + left * TORSO + rng.normal(0, 0.009 * TORSO, 2)
        rw = SHOULDERS + seen * TORSO + rng.normal(0, (0.009 + min(0.08, speed * 0.01)) * TORSO, 2)
        dt = 1 / fps
        e = old.update(t, filters[0](sh, dt, TORSO), TORSO, filters[1](lw, dt, TORSO), 0.95, filters[2](rw, dt, TORSO), vis)
        if e:
            out_old.append(e)
        e = new.update(t, sh, TORSO, lw, 0.95, rw, vis)
        if e:
            out_new.append(e)
    return out_old, out_new


def sim():
    cases = [('full forehand 2.7 T in 0.3 s', move((1.6, -0.3), (-1.1, 0.1), 0.3), 1),
             ("a child's forehand 1.0 T in 0.42 s", move((1.1, -0.6), (0.1, -0.3), 0.42), 1),
             ('forearm flick 0.5 T in 0.25 s', move((0.55, -0.75), (0.05, -0.65), 0.25), 1),
             ('small flick 0.35 T in 0.25 s', move((0.5, -0.75), (0.15, -0.7), 0.25), 1),
             ('small quick flick 0.3 T in 0.15 s', move((0.5, -0.75), (0.2, -0.72), 0.15), 1),
             ('tiny flick 0.25 T in 0.2 s', move((0.45, -0.75), (0.2, -0.72), 0.2), 1),
             ('lazy flick 0.3 T in 0.4 s', move((0.5, -0.75), (0.2, -0.72), 0.4), 1),
             ('still', move((0.45, -0.95), (0.45, -0.95), 1), 0)]
    for fps in (30.0, 15.0):
        print(f'{fps:.0f} samples a second (two players share the box: 15), 40 runs each: exactly the swings wanted')
        for name, path, want in cases:
            o = n = 0
            for seed in range(40):
                a, b = simulate(path, fps=fps, seed=seed, phase=(seed % 6) / (6 * fps))
                o, n = o + (len(a) == want), n + (len(b) == want)
            print(f'  {name:36s} old {o:2d}  new {n:2d}')
    print('a still hand with glitches (two in each of 40 runs): swings of any kind')
    still = move((0.3, -0.7), (0.3, -0.7), 1)
    for kind, size, frames in (('collapse', 0.6, 2), ('collapse', 0.5, 4), ('flip', 0.6, 1), ('flip', 1.2, 1), ('flip', 0.8, 2),
                               ('flip', 0.4, 3), ('flip', 0.25, 4)):
        o = n = 0
        for seed in range(40):
            a, b = simulate(still, 3.0, seed=seed, glitches=[(1.0 + seed * 0.003, kind, size, frames), (2.0, kind, size, frames)])
            o, n = o + len(a), n + len(b)
        print(f'  {kind} {size} T, {frames} frame(s): old {o}, new {n}')
    print('a forearm flick with a glitch in it, 40 runs: exactly one swing')
    flick = move((0.55, -0.75), (0.05, -0.65), 0.25)
    for kind, size, frames, at in (('flip', 0.6, 1, 1.1), ('flip', 1.0, 1, 1.15), ('collapse', 0.5, 2, 1.05), ('flip', 0.6, 2, 1.1)):
        o = n = 0
        for seed in range(40):
            a, b = simulate(flick, seed=seed, glitches=[(at, kind, size, frames)])
            o, n = o + (len(a) == 1), n + (len(b) == 1)
        print(f'  {kind} {size} T, {frames} frame(s) at {at - 1:+.2f} s: old {o}, new {n}')


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    sub = ap.add_subparsers(dest='cmd', required=True)
    r = sub.add_parser('recordings')
    r.add_argument('paths', nargs='+')
    r.add_argument('--list', action='store_true')
    g = sub.add_parser('agree')
    g.add_argument('a')
    g.add_argument('b')
    sub.add_parser('sim')
    args = ap.parse_args()
    if args.cmd == 'recordings':
        recordings(args.paths, args.list)
    elif args.cmd == 'agree':
        agree(args.a, args.b)
    else:
        sim()


if __name__ == '__main__':
    main()
