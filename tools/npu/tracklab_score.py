"""Scores tracklab runs against the reference run (the heavy model as 'teacher': several crops a frame, the median).

    python tools/npu/tracklab_score.py <reference.npz> <run.npz> [<run.npz> ...] [--key f_xy] [--from S] [--to S]

Per joint group, on the frames where the reference sees the joint (its visibility > 0.5) and is sure of it (its own
answers within 8 % of a torso of each other):
  err     distance from the reference, % of the torso (shoulders' middle to hips'): median / 90th percentile
  bad     frames more than 25 % of a torso off: share, and a minute
  shake   the run's own frame-to-frame acceleration |x(t-1) - 2 x(t) + x(t+1)|, % of the torso, median, on calm frames
          (the reference moving < 1 torso a second) / on all; the reference's own in brackets
  lag     the time shift that best lines the run's wrists up with the reference's (ms, + = the run is late)
  hidden  where the reference says the joint can't be seen (visibility < 0.3): the run's median frame-to-frame step,
          % of the torso (a hidden limb should rest, not flail)
  conf    how well the run's visibility tells its bad frames from its good ones (AUC, 1 = perfectly), and the share of
          bad / good frames it marks under 0.5
"""
import argparse
import os

import numpy as np

GROUPS = (('nose', (0,)), ('shoulders', (11, 12)), ('elbows', (13, 14)), ('wrists', (15, 16)), ('hips', (23, 24)),
          ('knees', (25, 26)), ('ankles', (27, 28)))
BAD = 0.25


COCO = (0, 2, 5, 7, 8, 11, 12, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28)    # the recordings' 17 joints as landmarks


def load(path):
    """A tracklab run (.npz), or a recording in the box's format (.jsonl: pose_npu_test --clip on the box, one line a
    frame): its 17 joints at their landmarks' places, the others empty."""
    if not path.endswith('.jsonl'):
        d = np.load(path)
        return {k: d[k] for k in d.files}
    import json
    lines = [json.loads(line) for line in open(path, encoding='utf-8')]
    n = max(p['f'] for p in lines) + 1
    run = dict(found=np.zeros(n, bool), detected=np.zeros(n, bool), f_xy=np.full((n, 33, 2), np.nan, np.float32),
               f_vis=np.full((n, 33), np.nan, np.float32), dip=np.zeros(n, bool), swapped=np.zeros(n, bool),
               f_world=np.full((n, 33, 3), np.nan, np.float32))
    for p in lines:
        f = p['f']
        run['detected'][f] = bool(p.get('det', 0))
        run['dip'][f] = bool(p.get('dip', 0))
        run['swapped'][f] = bool(p.get('sw', 0))
        if p.get('people'):
            person = p['people'][0]
            k = np.array(person['k'], np.float32).reshape(17, 4)
            run['found'][f] = True
            run['f_xy'][f, COCO, 0] = k[:, 0] * p['w']
            run['f_xy'][f, COCO, 1] = k[:, 1] * p['h']
            run['f_vis'][f, COCO] = k[:, 3]
            if 'wk' in person:
                run['f_world'][f, COCO] = np.array(person['wk'], np.float32).reshape(17, 3)
    return run


def auc(score, bad):
    """Probability that a bad frame has a lower score than a good one."""
    nb, ng = int(bad.sum()), int((~bad).sum())
    if nb == 0 or ng == 0:
        return float('nan')
    order = np.argsort(score, kind='stable')
    ranks = np.empty(len(score))
    ranks[order] = np.arange(len(score))
    return 1 - (ranks[bad].sum() - nb * (nb - 1) / 2) / (nb * ng)


def reference(ref):
    xy = ref['tta_xy'] if 'tta_xy' in ref else ref['xy'][:, :33]
    torso = np.linalg.norm((xy[:, 11] + xy[:, 12]) / 2 - (xy[:, 23] + xy[:, 24]) / 2, axis=1)
    spread = ref['tta_spread'] if 'tta_spread' in ref else np.zeros(xy.shape[:2], np.float32)
    return xy, torso, spread


def lag_ms(t, cand, ref, ok):
    """The shift (ms) of cand against ref that gives the smallest mean distance, on the frames ok (both finite)."""
    best, best_shift = None, 0.0
    shifts = np.arange(-66, 200, 4) / 1000.0
    for s in shifts:
        # the reference s seconds earlier, at the candidate's times
        rx = np.interp(t - s, t[ok], ref[ok, 0])
        ry = np.interp(t - s, t[ok], ref[ok, 1])
        d = np.hypot(cand[ok, 0] - rx[ok], cand[ok, 1] - ry[ok]).mean()
        if best is None or d < best:
            best, best_shift = d, s
    return best_shift * 1000


def score(ref, run, key, start, end, label):
    rxy, torso, spread = reference(ref)
    t = ref['t']
    window = (t >= start) & (t <= end)
    both = ref['found'] & run['found'] & window
    cand = run[key][:, :33]
    vis = run['f_vis'] if 'f_vis' in run else None
    minutes = (t[window][-1] - t[window][0]) / 60
    T = np.nanmedian(torso[both])
    lost = int((window & ref['found'] & ~run['found']).sum())
    print(f'== {label}: {int(both.sum())} frames with both ({lost} lost where the reference had someone), '
          f'{int(run["detected"][window].sum())} detector runs, torso {T:.0f} px')
    speed = np.full((len(t), 33), np.nan)
    speed[1:-1] = np.linalg.norm(rxy[2:] - rxy[:-2], axis=2) / (t[2:] - t[:-2])[:, None] / torso[1:-1, None]
    out = {}
    for name, joints in GROUPS:
        errs, shakes_calm, shakes_all, ref_shake, hidden, cv, bads = [], [], [], [], [], [], []
        lags = []
        for j in joints:
            sure = both & (ref['vis'][:, j] > 0.5) & (spread[:, j] < 0.08 * torso)
            e = np.linalg.norm(cand[:, j] - rxy[:, j], axis=1) / torso
            errs.append(e[sure])
            if vis is not None:
                cv.append(vis[sure, j])
            # acceleration, where three frames in a row are there
            acc = np.full(len(t), np.nan)
            acc[1:-1] = np.linalg.norm(cand[:-2, j] - 2 * cand[1:-1, j] + cand[2:, j], axis=1) / torso[1:-1]
            racc = np.full(len(t), np.nan)
            racc[1:-1] = np.linalg.norm(rxy[:-2, j] - 2 * rxy[1:-1, j] + rxy[2:, j], axis=1) / torso[1:-1]
            ok3 = sure & np.roll(sure, 1) & np.roll(sure, -1) & np.isfinite(acc)
            calm = ok3 & (speed[:, j] < 1.0)
            shakes_calm.append(acc[calm])
            shakes_all.append(acc[ok3])
            ref_shake.append(racc[calm])
            hid = both & (ref['vis'][:, j] < 0.3)
            step = np.full(len(t), np.nan)
            step[1:] = np.linalg.norm(cand[1:, j] - cand[:-1, j], axis=1) / torso[1:]
            hidden.append(step[hid & np.roll(hid, 1) & np.isfinite(step)])
            if name == 'wrists' and sure.sum() > 100:
                lags.append(lag_ms(t, cand[:, j], rxy[:, j], sure))
        e = np.concatenate(errs)
        if len(e) == 0:
            continue
        bad = e > BAD
        sc, sa, rs, hd = (np.concatenate(x) for x in (shakes_calm, shakes_all, ref_shake, hidden))
        line = (f'  {name:9s} err {np.median(e) * 100:4.1f} / {np.percentile(e, 90) * 100:5.1f} | bad {bad.mean() * 100:5.2f} % '
                f'({bad.sum() / minutes / len(joints):5.1f}/min a joint) | shake calm {np.median(sc) * 100 if len(sc) else float("nan"):4.2f} '
                f'[{np.median(rs) * 100 if len(rs) else float("nan"):4.2f}] all {np.median(sa) * 100 if len(sa) else float("nan"):4.2f}')
        if lags:
            line += f' | lag {np.mean(lags):4.0f} ms'
        if len(hd) > 20:
            line += f' | hidden step {np.median(hd) * 100:4.2f} / {np.percentile(hd, 90) * 100:5.2f} ({len(hd)})'
        if cv:
            c = np.concatenate(cv)
            line += (f' | conf AUC {auc(c, bad):.3f}, < 0.5: {100 * (c[bad] < 0.5).mean() if bad.any() else float("nan"):4.0f} % of bad, '
                     f'{100 * (c[~bad] < 0.5).mean():4.1f} % of good')
        print(line)
        out[name] = dict(err=float(np.median(e)), err90=float(np.percentile(e, 90)), bad=float(bad.mean()),
                         bad_min=float(bad.sum() / minutes / len(joints)))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('reference')
    ap.add_argument('runs', nargs='+')
    ap.add_argument('--key', default='f_xy')
    ap.add_argument('--from', dest='start', type=float, default=0)
    ap.add_argument('--to', dest='end', type=float, default=1e9)
    args = ap.parse_args()
    ref = load(args.reference)
    for path in args.runs:
        run = load(path)
        label = os.path.basename(path).replace('.npz', '')
        score(ref, run, args.key if args.key in run else 'xy', args.start, args.end, label + f' [{args.key}]')


if __name__ == '__main__':
    main()
