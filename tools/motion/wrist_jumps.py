"""Where the wrists jump in a recording (a frame-to-frame step over 15 % of a torso): in front of the torso or beside it,
how far the other hand is, and how short the forearm looks in the jumping frame. Made for the hand-circle tests on the
box (2026-11-01: both hands circling in front of the body, forearms toward the TV): ~85 % of the jumps came while that
hand passed in front of the chest (2.9 % of those frames, 0.5 % beside the body), the wrist pulled toward its elbow for
1-7 frames (at a jump's short end the forearm in the picture is about half its length, ~0.8 in 3D, against ~0.8-1.05 in
the frames around it; under 3/4 of them in ~80 % of the jumps: no real arm does that in 33 ms).
A gate for exactly that (--gate: hold a wrist that suddenly lands on its elbow) removed only 1-5 of ~25 jumps per hand
there and held back real jabs in punches.jsonl: not a fix. Use it to compare models (PerfLab model=lite|full) on the
same movement.

    motion-service-py/.venv/Scripts/python tools/motion/wrist_jumps.py <recording.jsonl> [--from S] [--to S] [--plot out.png] [--gate]

--from / --to keep a part of the recording (seconds from its start: leave out walking up and putting the hands down).
--plot draws both wrists' paths around the shoulders (torso units) with the jumps in red.
--gate replays the elbow-collapse gate (GATE_* below) and counts the jumps it leaves.
"""
import argparse
import json

import numpy as np

JUMP = 0.15            # torso lengths in one frame
HANDS = ((10, 8, 6, 'right'), (9, 7, 5, 'left'))     # wrist, elbow, shoulder (COCO 17)
GATE_COLLAPSE = 0.6    # a wrist closer to its elbow than this share of the recent forearm (picture) ...
GATE_STEP = 0.12       # ... that also moved this many torso lengths since the last kept sample is held back ...
GATE_HOLD = 3          # ... for at most this many frames (a collapse that lasts is taken: an arm pointing at the TV)


def collapse_gate(t, xy, W, E, torso):
    """The wrist with collapses onto the elbow held back (it stays where it was): positions, holds that ended with the
    forearm back to its length (caught) and holds that ran out (a real move made late, or a long mistake let through)."""
    out = xy[:, W].copy()
    caught = lasted = hold = 0
    kept, lengths = None, []
    for i in range(len(t)):
        s = xy[i, W]
        length = np.linalg.norm(s - xy[i, E])
        if kept is None or (i and t[i] - t[i - 1] > 0.1):
            kept, hold, lengths = s, 0, [length]
            continue
        recent = np.median(lengths[-30:])
        if length < GATE_COLLAPSE * recent and np.linalg.norm(s - kept) / torso > GATE_STEP and hold < GATE_HOLD:
            hold += 1
            out[i] = kept
            continue
        if hold:
            caught, lasted = (caught + 1, lasted) if length >= GATE_COLLAPSE * recent else (caught, lasted + 1)
        hold = 0
        kept = s
        lengths.append(length)
    return out, caught, lasted


def load(path):
    t, xy, vis, world = [], [], [], []
    for line in open(path):
        p = json.loads(line)
        if not p.get('people'):
            continue
        person = p['people'][0]
        k = np.array(person['k'], np.float64).reshape(17, 4)
        t.append(p['t'] / 1000.0)
        xy.append(k[:, :2] * (p['w'], p['h']))
        vis.append(k[:, 3])
        world.append(np.array(person['wk'], np.float64).reshape(17, 3) if person.get('wk') else np.full((17, 3), np.nan))
    t = np.array(t)
    return t - t[0], np.array(xy), np.array(vis), np.array(world)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('recording')
    ap.add_argument('--from', dest='t0', type=float, default=0)
    ap.add_argument('--to', dest='t1', type=float, default=1e9)
    ap.add_argument('--plot')
    ap.add_argument('--gate', action='store_true')
    a = ap.parse_args()
    t, xy, vis, world = load(a.recording)
    keep = (t >= a.t0) & (t <= a.t1)
    sh = xy[:, 5:7].mean(1)
    hip = xy[:, 11:13].mean(1)
    torso = np.median(np.linalg.norm(sh - hip, axis=1))
    gap = np.linalg.norm(xy[:, 9] - xy[:, 10], axis=1) / torso
    span = t[keep][-1] - t[keep][0]
    print(f'{a.recording}\n  {keep.sum()} frames over {span:.1f} s, torso {torso:.0f} px; hands closer than 0.3 torso in'
          f' {np.mean(gap[keep] < 0.3) * 100:.0f} % of the frames')
    jumps = {}
    for W, E, S, name in HANDS:
        one = np.r_[False, np.diff(t) < 0.045]           # steps only between samples one frame apart
        step = np.r_[0, np.linalg.norm(np.diff(xy[:, W], axis=0), axis=1)] / torso
        jump = keep & one & (step > JUMP)
        jumps[W] = jump
        # in front of the torso: between the shoulders (a little margin), from the shoulder line down to the hips
        lo = np.minimum(xy[:, 5, 0], xy[:, 6, 0]) - 0.1 * torso
        hi = np.maximum(xy[:, 5, 0], xy[:, 6, 0]) + 0.1 * torso
        front = (xy[:, W, 0] > lo) & (xy[:, W, 0] < hi) & (xy[:, W, 1] > sh[:, 1] - 0.1 * torso) & (xy[:, W, 1] < hip[:, 1])
        f2 = np.linalg.norm(xy[:, W] - xy[:, E], axis=1)
        f2 /= np.median(f2[keep])
        f3 = np.linalg.norm(world[:, W] - world[:, E], axis=1)
        f3 /= np.nanmedian(f3[keep]) if np.isfinite(f3[keep]).any() else 1
        toward = -(world[:, W, 2] - world[:, E, 2]) / np.maximum(np.linalg.norm(world[:, W] - world[:, E], axis=1), 1e-6)
        m_front, m_side = keep & front, keep & ~front
        print(f'  {name:5s} wrist: {jump.sum():3d} jumps ({jump.sum() / span * 60:4.1f} a minute);'
              f' in front of the torso {jump[m_front].sum():3d} in {m_front.sum():4d} frames ({jump[m_front].mean() * 100:4.1f} %),'
              f' beside it {jump[m_side].sum():3d} in {m_side.sum():4d} ({jump[m_side].mean() * 100:4.1f} %)')
        if jump.any():
            # a jump's short end (the frame before or the frame of the step, whichever has the shorter forearm) against
            # the frames 2-5 before and after it: how far the wrist was pulled toward its elbow
            idx = np.nonzero(jump)[0]
            short2 = np.minimum(f2[idx - 1], f2[idx])
            short3 = np.minimum(f3[idx - 1], f3[idx])
            around = np.array([np.median(np.r_[f2[max(i - 6, 0):i - 2], f2[i + 2:i + 6]]) for i in idx])
            print(f'         at a jump\'s short end the forearm looks {np.median(short2):.2f} of its length in the picture'
                  f' (frames around it {np.median(around):.2f}; under 3/4 of them in {np.mean(short2 < 0.75 * around) * 100:.0f} %'
                  f' of the jumps), {np.nanmedian(short3):.2f} in 3D; forearm toward the camera {np.nanmedian(toward[keep]):+.2f}'
                  f' (median of all frames); wrist visibility {np.median(vis[idx, W]):.2f} (all {np.median(vis[keep, W]):.2f})')
            bins = ((0, 0.3), (0.3, 0.6), (0.6, 1.0), (1.0, 99))
            print('         by the distance to the other hand (torso lengths): ' + ', '.join(
                f'{lo_:g}-{hi_ if hi_ < 99 else ""}: {jump[keep & (gap >= lo_) & (gap < hi_)].sum()}'
                f'/{(keep & (gap >= lo_) & (gap < hi_)).sum()}' for lo_, hi_ in bins))
        if a.gate:
            gated, caught, lasted = collapse_gate(t, xy, W, E, torso)
            gstep = np.r_[0, np.linalg.norm(np.diff(gated, axis=0), axis=1)] / torso
            print(f'         collapse gate: {(keep & one & (gstep > JUMP)).sum()} jumps left; holds that came back {caught},'
                  f' that ran out {lasted} (the whole recording)')
    if a.plot:
        import cv2
        img = np.full((900, 1600, 3), 255, np.uint8)
        for W, E, S, name in HANDS:
            col = (40, 120, 255) if W == 10 else (200, 120, 30)
            pts = np.stack([800 + (xy[:, W, 0] - sh[:, 0]) / torso * 300,
                            300 + (xy[:, W, 1] - sh[:, 1]) / torso * 300], 1).astype(np.int32)
            for i in np.nonzero(keep)[0][1:]:
                cv2.line(img, tuple(pts[i - 1]), tuple(pts[i]), col, 1)
            for i in np.nonzero(jumps[W])[0]:
                cv2.line(img, tuple(pts[i - 1]), tuple(pts[i]), (0, 0, 220), 3)
        for j, c in ((5, (0, 0, 0)), (6, (0, 0, 0)), (7, (0, 0, 0)), (8, (0, 0, 0)), (11, (90, 90, 90)), (12, (90, 90, 90)),
                     (0, (0, 160, 0))):
            q = np.median((xy[keep, j] - sh[keep]) / torso, axis=0)
            cv2.circle(img, (int(800 + q[0] * 300), int(300 + q[1] * 300)), 9, c, -1)
        cv2.putText(img, 'orange = right wrist, blue = left wrist, red = jumps; dots: shoulders, elbows, hips, nose (green)',
                    (20, 880), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 0), 1)
        cv2.imwrite(a.plot, img)
        print(f'  plot: {a.plot}')


if __name__ == '__main__':
    main()
