"""Every hand cursor on real box recordings, scored against the heavy reference model like wand_replay.py (2026-11-17
night: after Zana!'s wand moved to the wrist, the user said "yes, do them" for the other games' hand cursors).

    python tools/motion/hand_replay.py <teacher.npz> <run.jsonl> ... [--games paint,arcade,arcadeu,hands,wand]
                                       [--points tip,wrist,stip,ftip,mtip,arm0.1,tip/nofade,...]

The runs, the teacher and the scoring are wand_replay.py's (read its header): the box engine's output on a recorded
clip, MediaPipe's heavy model on six crops a frame as the reference, smoothed both ways in time; jumps = game frames
whose cursor step differs from the reference's step by more than 40 / 80 / 150 HUD px, per minute of a hand's time.

What each game does with a hand, mirrored (the HUD is 1920 x 1080):
- paint: Pikturo!'s brush on the paper (PaintGame.HandHud, paper mode): the point round the smooth shoulders' middle,
  480 px a torso length, the shoulders at y 578; HandInterpolator 2 / 1.4 / 2 with FitWindow 0.25 s a camera frame
  behind, on the point in torso lengths; then CursorRope 6 px. HandPresence. Scored while the reference is on the paper.
- arcade: Lunapark!'s hands (ArcadeStage.Track): the point in HUD px through Godit!'s framing (3.55 torso lengths high:
  ~304 px a torso length), HandInterpolator 1 / 4 / 2 on the px themselves (so the One Euro hardly filters: its beta
  acts on px a second), half a camera frame behind, gliding up to 80 ms. Scored on every frame. arcadeu: the same
  filter in torso lengths (not in the game).
- hands: BodyHands (the shared cursor; no game reads its camera cursor today): the zone 3.2 torso lengths wide round the
  shoulders, from 0.3 below the hips to 1.3 above the shoulders, HandInterpolator 1 / 4 / 2 in zone units, half a
  camera frame behind, 80 ms glide. HandPresence. Scored on every frame.
- wand: Zana!'s wand as it is (wand_replay.py's `now`), for comparison.
(Vishu!'s cursor comes from its fitted pose: tools/motion/dress_bench, the Cursor test.)

The points (`point[/fade]`):
- tip: the raw wrist pushed 0.3 of the raw forearm on (Body.HandTip, every cursor until 2026-11-17): a third of the
  elbow's error rides along;
- wrist: the raw wrist (Zana!'s wand since 2026-11-17): the least noise, but off the hand's middle by the push and
  reaching 0.15 torso lengths less;
- stip / ftip: the raw wrist pushed along the smooth (0.5 / 1.5 / 1) or low-lag (1 / 4 / 1) track's forearm; mtip: along
  the raw forearm through a median of three;
- armK: the raw wrist carried on out along the arm from the smooth shoulder, as if the arm were K longer (Body.HandPoint
  with K = 0.1 since 2026-11-17: no elbow at all, the wrist's noise times 1 + K, as far a reach as the tip);
- /nofade, /sfade: the push without Body.HandTip's fade with the elbow's visibility (0.2..0.4), or with it low-passed.
Each one's reference is the same point on the teacher (its own smooth and fast tracks for stip and ftip), so "jumps"
and "err" ask whether a cursor moves the way its own point does. "hand" = the cursor's median distance from the
teacher's tip, i.e. from the hand's middle (what the camera-picture games need: a hand on its own image), lag
included; "palm" the same from the heavy model's palm (wrist, index and pinky knuckles); "floor" = the teacher's own
point through the same pipeline (what the cursor's own dynamics cost).

Findings (docs/hardware.md "The other hand cursors"): the wrist jumps least, the arm 0.1 longer keeps the reach and the
hand in the picture with a fifth to a third fewer jumps than the tip; the fade, the median and the smooth forearm
change little.
"""
import argparse
import os

import numpy as np

from wand_replay import (EL, GAME_FPS, HIP, HUD_H, HUD_W, LATENCY, NOW, OneEuro, PK, IX, SH, WR, load_run, play,
                         to_screen, view)

POINTS = ('tip', 'wrist', 'stip', 'ftip')
EXT = 0.3


# ---------------------------------------------------------------------------------------------- BodyTracker, mirrored
class Tracks:
    """BodyTracker on a run: per sample the raw, smooth (0.5 / 1.5 / 1) and fast (1 / 4 / 1) joints in view space
    (body-relative One Euro, the filter scale following the torso at 0.05), the smooth shoulders' and hips' middles, and
    the unit the games ease toward the smooth torso length (0.1 a sample)."""

    JOINTS = (SH[0], SH[1], EL[0], EL[1], WR[0], WR[1], HIP[0], HIP[1])

    def __init__(self, run):
        p, t = view(run['xy'], run['size']), run['t']
        n = len(t)
        self.raw = p
        self.smooth = np.full_like(p, np.nan)
        self.fast = np.full_like(p, np.nan)
        sf = {j: (OneEuro(0.5, 1.5, 1.0), OneEuro(0.5, 1.5, 1.0)) for j in self.JOINTS}
        ff = {j: (OneEuro(1.0, 4.0, 1.0), OneEuro(1.0, 4.0, 1.0)) for j in self.JOINTS}
        self.shoulders = np.full((n, 2), np.nan)
        self.hips = np.full((n, 2), np.nan)
        self.unit = np.full(n, np.nan)
        scale = unit = last = None
        for i in range(n):
            if not (np.isfinite(t[i]) and np.all(np.isfinite(p[i, list(self.JOINTS)]))):
                continue
            dt = t[i] - last if last is not None else 0.0
            last = t[i]
            torso = float(np.linalg.norm(p[i, list(SH)].mean(0) - p[i, list(HIP)].mean(0)))
            scale = max(torso, 0.02) if scale is None else scale + (max(torso, 0.02) - scale) * 0.05
            for j in self.JOINTS:
                for a in range(2):
                    self.smooth[i, j, a] = sf[j][a](p[i, j, a], dt, scale)
                    self.fast[i, j, a] = ff[j][a](p[i, j, a], dt, scale)
            self.shoulders[i] = self.smooth[i, list(SH)].mean(0)
            self.hips[i] = self.smooth[i, list(HIP)].mean(0)
            ts = float(np.linalg.norm(self.shoulders[i] - self.hips[i]))
            unit = ts if unit is None else unit + (ts - unit) * 0.1
            self.unit[i] = unit


def smoothed_fade(t, vis, cutoff=2.0):
    """The elbow's visibility low-passed (one pole at `cutoff` Hz on the camera clock), then Body.HandTip's 0.2..0.4."""
    out = np.zeros_like(vis)
    v, last = None, None
    for i in range(len(t)):
        if not np.isfinite(t[i]):
            out[i] = 0 if v is None else v
            continue
        dt = t[i] - last if last is not None else 0.0
        last = t[i]
        a = 1 / (1 + 1 / (2 * np.pi * cutoff * dt)) if dt > 0 else 1.0
        v = vis[i] if v is None else v + (vis[i] - v) * a
        out[i] = v
    return np.clip((out - 0.2) / 0.2, 0, 1)


def median3(a):
    """Each sample the median of itself and the two before (per component; NaN-safe at the start)."""
    out = a.copy()
    for i in range(2, len(a)):
        out[i] = np.median(a[i - 2:i + 1], axis=0)
    return out


def hand_points(run, tracks, kind):
    """(n, 2 hands, 2) a point of each hand in view space. `kind` = point[/fade]: fade `vis` (Body.HandTip: the push
    fades in with the elbow's visibility, 0.2..0.4, default), `nofade`, or `sfade` (the visibility low-passed at 2 Hz)."""
    raw, vis = tracks.raw, run['vis']
    point, _, how = kind.partition('/')
    out = np.full((len(raw), 2, 2), np.nan)
    for k in range(2):
        w = raw[:, WR[k]]
        if how == 'nofade':
            fade = np.ones(len(raw))[:, None]
        elif how == 'sfade':
            fade = smoothed_fade(run['t'], vis[:, EL[k]])[:, None]
        else:
            fade = np.clip((vis[:, EL[k]] - 0.2) / 0.2, 0, 1)[:, None]
        if point == 'wrist':
            out[:, k] = w
        elif point == 'tip':
            out[:, k] = w + (w - raw[:, EL[k]]) * EXT * fade
        elif point == 'stip':
            out[:, k] = w + (tracks.smooth[:, WR[k]] - tracks.smooth[:, EL[k]]) * EXT * fade
        elif point == 'ftip':
            out[:, k] = w + (tracks.fast[:, WR[k]] - tracks.fast[:, EL[k]]) * EXT * fade
        elif point == 'mtip':   # the raw forearm through a median of three (a one-frame elbow error never gets in)
            out[:, k] = w + median3(w - raw[:, EL[k]]) * EXT * fade
        elif point.startswith('arm'):   # the arm from the smooth shoulder made longer: armK = the wrist (1 + K) as far out
            s = tracks.smooth[:, SH[k]]
            out[:, k] = s + (w - s) * (1 + float(point[3:]))
        elif point == 'palm':
            out[:, k] = (w + raw[:, PK[k]] + raw[:, IX[k]]) / 3
    return out


# ------------------------------------------------------------------------------------------- the games, mirrored
class Interp:
    """HandInterpolator: samples on the camera clock, `margin` camera frames behind the newest, gliding past it at most
    `glide` s, a least-squares parabola through the last `fit` s if set, then the per-frame One Euro."""

    def __init__(self, f, margin, glide=0.0, fit=0.0):
        self.margin, self.glide, self.fit = margin, glide, fit
        self.times, self.pos = [], []
        self.offset, self.interval, self.vel = None, 1 / 30, np.zeros(2)
        self.f = f
        self.reset()

    def reset(self):
        self.times, self.pos = [], []
        self.vel = np.zeros(2)
        self.fx, self.fy = OneEuro(*self.f), OneEuro(*self.f)

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
        self.offset = off if self.offset is None or not self.times else self.offset + (off - self.offset) * 0.1
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
        if self.fit > 0 and len(self.times) >= 3:
            p = self._fit(t, p)
        return np.array([self.fx(p[0], dt), self.fy(p[1], dt)])

    def _fit(self, t, p):
        newest = self.times[-1]
        first = len(self.times) - 1
        while first > 0 and self.times[first - 1] >= newest - self.fit:
            first -= 1
        first = min(first, max(0, len(self.times) - 4))
        u = np.array(self.times[first:]) - newest
        a = np.stack([np.ones_like(u), u, u * u], 1)
        try:
            coef, *_ = np.linalg.lstsq(a, np.array(self.pos[first:]), rcond=None)
        except np.linalg.LinAlgError:
            return p
        x = max(self.times[first], min(newest, t)) - newest
        return coef[0] + coef[1] * x + coef[2] * x * x


class Presence:
    """HandPresence: found at visibility 0.5, kept down to 0.3, lost 0.3 s after the last usable sample."""

    def __init__(self):
        self.seen, self.last = False, -10.0

    def tracked(self, now):
        return self.seen and now - self.last <= 0.3

    def sample(self, vis, now):
        if vis < (0.3 if self.tracked(now) else 0.5):
            return False
        self.seen, self.last = True, now
        return True


def paint_map(q, tracks, i):
    """PaintGame.HandHud in paper mode: view point -> HUD px."""
    sh, u = tracks.shoulders[i], tracks.unit[i]
    return np.array([HUD_W / 2 + (q[0] - sh[0]) / u * 480.0, 578.0 - (q[1] - sh[1]) / u * 480.0])


def hands_map(q, tracks, i):
    """BodyHands' zone (0..1, y up) for a view point."""
    sh, hips, u = tracks.shoulders[i], tracks.hips[i], tracks.unit[i]
    bottom, top = hips[1] - 0.3 * u, sh[1] + 1.3 * u
    return np.array([0.5 + (q[0] - sh[0]) / (2 * 1.6 * u), (q[1] - bottom) / max(top - bottom, 0.01)])


def arcade_scale(tracks):
    """Godit!'s framing: HUD px per view unit (the stage locked on the median torso length)."""
    return HUD_H / (3.55 * float(np.nanmedian(tracks.unit)))


def run_game(run, tracks, kind, game, gate=True):
    """Every game frame, per hand: (moment on the camera clock, HUD px) or None. `gate` False = the teacher (no
    visibility rules)."""
    t, vis = run['t'], run['vis']
    pts = hand_points(run, tracks, kind)
    if game == 'paint':
        cur = [Interp((2.0, 1.4, 2.0), 1.0, 0.0, 0.25) for _ in range(2)]
    elif game in ('arcade', 'arcadeu'):
        cur = [Interp((1.0, 4.0, 2.0), 0.5, 0.08) for _ in range(2)]
        px = arcade_scale(tracks)
        unit_px = px * float(np.nanmedian(tracks.unit)) if game == 'arcadeu' else 1.0
    else:
        cur = [Interp((1.0, 4.0, 2.0), 0.5, 0.08) for _ in range(2)]
    pres = [Presence(), Presence()]
    rope = [None, None]
    frames = [[], []]
    valid = np.isfinite(t)
    now, end = t[valid][0] + LATENCY, t[valid][-1] + LATENCY
    i = 0
    while now <= end:
        while i < len(t) and (not valid[i] or t[i] + LATENCY <= now):
            if valid[i] and np.isfinite(tracks.unit[i]):
                for k in range(2):
                    q = pts[i, k]
                    if not np.all(np.isfinite(q)):
                        continue
                    if game in ('arcade', 'arcadeu'):
                        cur[k].sample(t[i], q * px / unit_px, t[i] + LATENCY)   # ArcadeStage has no presence rule
                        continue
                    if gate and not pres[k].sample(vis[i, WR[k]], t[i] + LATENCY):
                        continue
                    if not gate:
                        pres[k].sample(1.0, t[i] + LATENCY)
                    if game == 'paint':
                        cur[k].sample(t[i], paint_map(q, tracks, i) / 480.0, t[i] + LATENCY)
                    else:
                        cur[k].sample(t[i], hands_map(q, tracks, i), t[i] + LATENCY)
            i += 1
        for k in range(2):
            if game not in ('arcade', 'arcadeu') and not pres[k].tracked(now):
                cur[k].reset()
                rope[k] = None
            if not cur[k].times:
                frames[k].append(None)
                continue
            p = cur[k].evaluate(now, 1 / GAME_FPS)
            if game == 'arcadeu':
                p = p * unit_px
            if game == 'paint':
                p = p * 480.0
                if rope[k] is None:
                    rope[k] = p.copy()
                else:
                    d = p - rope[k]
                    length = float(np.linalg.norm(d))
                    if length > 6.0:
                        rope[k] = rope[k] + d * (1 - 6.0 / length)
                p = rope[k].copy()
            elif game == 'hands':
                p = np.array([p[0] * HUD_W, (1 - p[1]) * HUD_H])
            frames[k].append((cur[k].moment(now), p))
        now += 1 / GAME_FPS
    return frames


def reference(teacher, tracks, kind, game):
    """The teacher's point of the same kind (its own tracks) through the same mapping, smoothed both ways (5-sample
    quadratic fit), (n, 2, 2) HUD px."""
    pts = hand_points(teacher, tracks, kind)
    n = len(pts)
    s = np.full((n, 2, 2), np.nan)
    px = arcade_scale(tracks) if game in ('arcade', 'arcadeu') else None
    for i in range(n):
        if not np.isfinite(tracks.unit[i]):
            continue
        for k in range(2):
            q = pts[i, k]
            if game == 'paint':
                s[i, k] = paint_map(q, tracks, i)
            elif game in ('arcade', 'arcadeu'):
                s[i, k] = q * px
            else:
                z = hands_map(q, tracks, i)
                s[i, k] = [z[0] * HUD_W, (1 - z[1]) * HUD_H]
    out = s.copy()
    w = np.array([-3, 12, 17, 12, -3]) / 35.0
    for i in range(2, n - 2):
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


def score(frames, ref, hand_ref, palm_ref, t, ok):
    errs, hand, palm, steps = [], [], [], []
    n_frames = 0
    for k in range(2):
        prev = None
        for fr in frames[k]:
            if fr is None:
                prev = None
                continue
            moment, pos = fr
            i = min(int(np.searchsorted(t, moment)), len(t) - 1)
            r = at(ref, t, k, moment)
            if r is None or not ok[i, k]:
                prev = None
                continue
            n_frames += 1
            errs.append(np.linalg.norm(pos - r))
            h = at(hand_ref, t, k, moment)
            if h is not None:
                hand.append(np.linalg.norm(pos - h))
            pm = at(palm_ref, t, k, moment) if palm_ref is not None else None
            if pm is not None:
                palm.append(np.linalg.norm(pos - pm))
            if prev is not None:
                steps.append(np.linalg.norm((pos - prev[0]) - (r - prev[1])))
            prev = (pos, r)
    steps = np.array(steps)
    minutes = max(n_frames, 1) / GAME_FPS / 60
    med = lambda a: float(np.median(a)) if len(a) else float('nan')
    return dict(minutes=minutes, j40=np.sum(steps > 40) / minutes, j80=np.sum(steps > 80) / minutes,
                j150=np.sum(steps > 150) / minutes, err=med(errs),
                p90=float(np.percentile(errs, 90)) if errs else float('nan'), hand=med(hand), palm=med(palm))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('teacher')
    ap.add_argument('runs', nargs='+')
    ap.add_argument('--games', default='paint,arcade,hands,wand')
    ap.add_argument('--points', default=','.join(POINTS))
    a = ap.parse_args()
    teacher_all = load_run(a.teacher)
    for path in a.runs:
        run = load_run(path)
        n = min(len(run['t']), len(teacher_all['t']))
        run = dict(run, xy=run['xy'][:n], vis=run['vis'][:n], t=run['t'][:n])
        teacher = dict(teacher_all, xy=teacher_all['xy'][:n], vis=np.ones((n, 33)), t=run['t'].copy())
        rt, tt = Tracks(run), Tracks(teacher)
        sure = teacher_all['spread'][:n, list(WR)] < 0.08 * tt.unit[:, None] * teacher['size'][1]
        print(f'\n{os.path.basename(path)}')
        for game in a.games.split(','):
            if game == 'wand':
                # wand_replay's own pipeline and scoring (one hand at a time)
                from wand_replay import reference as wref, score as wscore, hand_points as whp, body_units
                centre, unit = body_units(teacher)
                wq = (whp(teacher, 'wrist') - centre[:, None]) / unit[:, None, None]
                raised = (wq[..., 1] + 1.0 > 0.33) & sure
                s = wscore(play(run, NOW), wref(teacher, NOW), run['t'], raised)
                print(f'  wand (now, the wrist)        {s["j40"]:6.1f} {s["j80"]:5.1f} {s["j150"]:5.1f} | '
                      f'{s["err"]:5.0f} {s["p90"]:5.0f} |   ({s["minutes"]:.2f} min)')
                continue
            tip_ref = reference(teacher, tt, 'tip', game)
            palm_ref = reference(teacher, tt, 'palm', game)
            if game == 'paint':
                ok = sure & (tip_ref[..., 0] > 432) & (tip_ref[..., 0] < 1488) & (tip_ref[..., 1] > 160) & (tip_ref[..., 1] < 952)
            else:
                ok = sure
            print(f'  {game:28s} {"jumps/min >40":>14s} {">80":>5s} {">150":>5s} | {"err":>5s} {"p90":>5s} | '
                  f'{"hand":>5s} {"palm":>5s}  (px)')
            for kind in a.points.split(','):
                ref = reference(teacher, tt, kind, game)
                s = score(run_game(run, rt, kind, game), ref, tip_ref, palm_ref, run['t'], ok)
                # the floor: the teacher's own point through the same pipeline (what the cursor's own dynamics cost)
                f = score(run_game(teacher, tt, kind, game, gate=False), ref, tip_ref, palm_ref, run['t'], ok)
                print(f'    {kind:26s} {s["j40"]:14.1f} {s["j80"]:5.1f} {s["j150"]:5.1f} | {s["err"]:5.0f} {s["p90"]:5.0f} | '
                      f'{s["hand"]:5.0f} {s["palm"]:5.0f}   floor {f["j40"]:5.1f} {f["j80"]:4.1f}, hand {f["hand"]:3.0f}   '
                      f'({s["minutes"]:.2f} hand-min)')
            frames = run_game(teacher, tt, 'tip', game, gate=False)
            s = score(frames, tip_ref, tip_ref, palm_ref, run['t'], ok)
            print(f'    {"(teacher, tip)":26s} {s["j40"]:14.1f} {s["j80"]:5.1f} {s["j150"]:5.1f} | {s["err"]:5.0f} '
                  f'{s["p90"]:5.0f} | {s["hand"]:5.0f} {s["palm"]:5.0f}')


if __name__ == '__main__':
    main()
