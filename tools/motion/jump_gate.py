"""A jump gate for hands and feet, replayed on real recordings: how many single-frame jumps it would remove and how often
it would hold back a real fast move for one frame. **Not adopted** (2026-11-01): on the box's recordings it made the
limbs leave their path more often (wrists > 20 % of a torso off: 3.5 -> 5.1 % of the moving frames in the first test),
about half of its holds were real fast moves (6-30 a minute per wrist), and true one-frame spikes (out and straight
back by > 20 % of a torso) were rare: ~0.6-1.3 a minute per wrist or ankle (4.6 in one test). What looked like jumps
was mostly real fast movement that the both-ways smoothed reference can't follow. Kept to check that claim on new
recordings before anyone adds such a gate.

The rule: each limb joint (elbows, wrists, knees, ankles) is predicted from its last
position and speed; a sample further than GATE torso lengths from the prediction is held back for one frame (the
prediction is used instead). The next sample is always taken, so a real fast move is late by one frame at most, and a
one-frame jump never shows. A gap in the samples (> 0.1 s) starts over.

Scored against each joint's path smoothed both ways in time (shake_report.smooth): frames more than 20 % of a torso off
that path while the joint moves, before and after the gate; holds of real moves = a held sample the next one confirmed
(within CONFIRM of it).

    motion-service-py/.venv/Scripts/python tools/motion/jump_gate.py <recording.jsonl> [...] [--gate 0.25]
"""
import argparse
import sys

import numpy as np

sys.path.insert(0, __import__('os').path.dirname(__file__))
import shake_report as sr  # noqa: E402

LIMBS = (7, 8, 9, 10, 13, 14, 15, 16)    # elbows, wrists, knees, ankles (COCO 17)
GATE = 0.25            # torso lengths from the prediction
CONFIRM = 0.15         # a held sample counts as a real move when the next sample lands this close to it
VELOCITY_SMOOTHING = 0.5
MAX_GAP = 0.1          # seconds


def gate(t, xy, torso, gate_size=GATE):
    """The gated positions and, per joint, the held frames and whether the next sample confirmed them."""
    out = xy.copy()
    held = np.zeros(xy.shape[:2], bool)
    confirmed = np.zeros(xy.shape[:2], bool)
    for j in LIMBS:
        last = vel = pending = None
        was_held = False
        for i in range(len(t)):
            s = xy[i, j]
            dt = t[i] - t[i - 1] if i else 0.0
            if last is None or dt <= 0 or dt > MAX_GAP:
                last, vel, was_held, pending = s, np.zeros(2), False, None
                continue
            pred = last + vel * dt
            if not was_held and np.linalg.norm(s - pred) > gate_size * torso:
                out[i, j] = pred              # held back one frame
                held[i, j] = True
                pending, was_held = s, True
                last = pred
                continue
            if was_held and pending is not None and np.linalg.norm(s - pending) < CONFIRM * torso:
                confirmed[i - 1, j] = True    # the held jump was real: it cost one frame
            vel = VELOCITY_SMOOTHING * vel + (1 - VELOCITY_SMOOTHING) * (s - last) / dt
            last, was_held, pending = s, False, None
    return out, held, confirmed


def score(t, xy, ref, torso):
    speed = np.linalg.norm(np.gradient(ref, t, axis=0), axis=2) / torso
    off = np.linalg.norm(xy - ref, axis=2) / torso * 100
    moving = speed > 0.3
    still = np.all(speed[:, [5, 6, 7, 8, 9, 10, 11, 12]] < sr.STILL, axis=1)
    return off, moving, still


def report(path, gate_size):
    t, xy, vis = sr.load(path)
    torso = np.median(np.linalg.norm(xy[:, 5:7].mean(1) - xy[:, 11:13].mean(1), axis=1))
    ref = sr.smooth(t, xy)
    gated, held, confirmed = gate(t, xy, torso, gate_size)
    off0, moving, still = score(t, xy, ref, torso)
    off1, _, _ = score(t, gated, ref, torso)
    minutes = (t[-1] - t[0]) / 60
    print(f'{path}  ({len(t)} packets, {minutes * 60:.0f} s, gate {gate_size:.2f} torso)')
    for name, js in (('elbows', (7, 8)), ('wrists', (9, 10)), ('knees', (13, 14)), ('ankles', (15, 16))):
        m = moving[:, js]
        b, a = off0[:, js][m], off1[:, js][m]
        s0, s1 = off0[still][:, js], off1[still][:, js]
        h = held[:, js].sum() / len(js) / minutes
        c = confirmed[:, js].sum() / len(js) / minutes
        print(f'  {name:7s} moving > 20 % off: {np.mean(b > 20) * 100:4.1f} % -> {np.mean(a > 20) * 100:4.1f} % of frames,'
              f' > 10 %: {np.mean(b > 10) * 100:4.1f} % -> {np.mean(a > 10) * 100:4.1f} % | still RMS'
              f' {np.sqrt(np.mean(s0 ** 2)) if s0.size else float("nan"):4.2f} -> {np.sqrt(np.mean(s1 ** 2)) if s1.size else float("nan"):4.2f} %'
              f' | held {h:5.1f}/min, of them real moves {c:4.1f}/min')


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('recordings', nargs='+')
    ap.add_argument('--gate', type=float, default=GATE)
    args = ap.parse_args()
    for p in args.recordings:
        report(p, args.gate)
