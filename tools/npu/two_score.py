"""Two people from one clip, scored person by person: a two-person clip made from a one-person recording (two windows of
it side by side, the right one some frames later: tools/npu/two_clip.py), run through the box engine's two-player mode
(pose_npu_test --people 2 --rate 30 --clip, a line per person with its track), each person against the heavy model's
teacher on that person's own frames (tools/npu/tracklab.py --variant teacher on the one-person clip), and what Tenis!'s
swing detector (tools/motion/swing_replay.py, SwingDetector.cs number for number) makes of each person's samples.

    motion-service-py/.venv/Scripts/python tools/npu/two_score.py <teacher.npz> <run.jsonl> [<run.jsonl> ...]
        [--x0 270] [--x0b 270] [--offset 1125] [--scale 1.0] [--single]

(--x0 / --x0b / --offset / --scale = two_clip.py's --xa / --xb / --offset / --scale; --single: a one-person run of the
one-person clip itself.) Per person (left = the clip's frames, right = `offset` frames later), per run:
  poses/s     the person's poses a second (two people share the NPU's 30 landmark runs: ~15 each)
  wrist err   distance from the teacher, % of a torso, median / 90th percentile (frames where the teacher is sure)
  lag         the time shift that best lines the run's wrists up with the teacher's (ms, + = late)
  unsure      the share of wrist samples under 0.6 / 0.8 visibility, on all frames / while the teacher's wrist moves
              faster than 2 torso lengths a second (the swing detector skips under its MinVisibility, 0.45 since
              2026-11-12, and waits a sample for under 0.8)
  swings      the swing detector on the run's samples: swings, strong ones, moves it took for glitches and ignored; then
              against the same detector on the teacher at the run's own sample times (visibility 1): found / missed /
              extra, and how much later the found ones are reported (ms); then against the teacher at 30 samples a
              second (every camera frame: what a one-player game would have seen)
Numbers of 2026-11-12 in docs/hardware.md "Two players after the tracking rework".
"""
import argparse
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'motion'))
import swing_replay as sr  # noqa: E402

COCO = (0, 2, 5, 7, 8, 11, 12, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28)


def load_people(path):
    """{track id: [(frame, t, k (17 x 4 normalised), w, h)]} from a --people 2 run."""
    tracks = {}
    for line in open(path, encoding='utf-8'):
        p = json.loads(line)
        for person in p.get('people') or []:
            k = np.array(person['k'], np.float64).reshape(17, 4)
            tracks.setdefault(person['id'], []).append((p['f'], p['t'] / 1000.0, k, p['w'], p['h']))
    return tracks


def split_sides(tracks, width):
    """Each track's samples to the side of the picture its hips are on: {'left': [...], 'right': [...]}, by frame."""
    sides = {'left': {}, 'right': {}}
    for samples in tracks.values():
        for s in samples:
            hips = (s[2][11, 0] + s[2][12, 0]) * 0.5 * width
            sides['left' if hips < width / 2 else 'right'][s[0]] = s
    return {side: [v[f] for f in sorted(v)] for side, v in sides.items()}


def to_view(k, w, h):
    """The SDK's view space (swing_replay.load): x = (1 - kx) * aspect, y = 1 - ky."""
    aspect = w / h
    return np.stack([(1 - k[:, 0]) * aspect, 1 - k[:, 1]], 1)


def detect(samples):
    """Swings of a list of (t, xy (17 x 2 view), vis (17)) through the game's detector."""
    d = sr.Detector()
    out = []
    for i, (t, xy, vis) in enumerate(samples):
        sh = (xy[sr.L_SH] + xy[sr.R_SH]) / 2
        e = d.update(t, sh, sr.torso_of(xy), xy[sr.L_WR], vis[sr.L_WR], xy[sr.R_WR], vis[sr.R_WR])
        if e:
            out.append(e)
    return out, d.ignored


def match(found, truth, within=0.2):
    """Swings of `found` that start within `within` s of a swing of the same hand in `truth`: (pairs, missed, extra)."""
    used, pairs = set(), []
    for e in found:
        best = None
        for j, r in enumerate(truth):
            if j in used or r['right'] != e['right'] or abs(r['start'] - e['start']) > within:
                continue
            if best is None or abs(r['start'] - e['start']) < abs(truth[best]['start'] - e['start']):
                best = j
        if best is not None:
            used.add(best)
            pairs.append((e, truth[best]))
    return pairs, len(truth) - len(used), len(found) - len(pairs)


def lag_ms(t, cand, ref):
    best, best_shift = None, 0.0
    for s in np.arange(-66, 300, 4) / 1000.0:
        rx, ry = np.interp(t - s, t, ref[:, 0]), np.interp(t - s, t, ref[:, 1])
        d = np.hypot(cand[:, 0] - rx, cand[:, 1] - ry).mean()
        if best is None or d < best:
            best, best_shift = d, s
    return best_shift * 1000


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('teacher')
    ap.add_argument('runs', nargs='+')
    ap.add_argument('--x0', type=int, default=270, help="the left person's window in the one-person picture")
    ap.add_argument('--x0b', type=int, default=None, help="the right person's (default: --x0)")
    ap.add_argument('--offset', type=int, default=1125)
    ap.add_argument('--half', type=int, default=512)
    ap.add_argument('--scale', type=float, default=1.0, help='the one-person picture shrunk around its middle by this')
    ap.add_argument('--single', action='store_true', help='a one-person run of the one-person clip itself (pose_npu_test '
                    '--clip): every pose is the person, no window')
    args = ap.parse_args()
    ref = np.load(args.teacher)
    n = len(ref['t'])
    rxy = ref['tta_xy']                                   # 33 landmarks, pixels of the one-person picture
    torso_all = np.linalg.norm((rxy[:, 11] + rxy[:, 12]) / 2 - (rxy[:, 23] + rxy[:, 24]) / 2, axis=1)
    sure_all = ref['found'][:, None] & (ref['vis'] > 0.5) & (ref['tta_spread'] < 0.08 * torso_all[:, None])
    for path in args.runs:
        tracks = load_people(path)
        any_s = next(iter(tracks.values()))[0]
        W, H = any_s[3], any_s[4]
        sides = split_sides(tracks, W)
        if args.single:
            every = {}
            for samples in tracks.values():
                for x in samples:
                    every[x[0]] = x
            sides = {'one': [every[f] for f in sorted(every)]}
        print(f'== {os.path.basename(path)}: {len(tracks)} tracks')
        for side, samples in sides.items():
            if len(samples) < 30:
                print(f'  {side}: {len(samples)} poses')
                continue
            shift = 0 if args.single else args.half - (args.x0 if args.x0b is None else args.x0b) if side == 'right' else -args.x0
            frames = np.array([s[0] for s in samples])
            src = (frames + (args.offset if side == 'right' else 0)) % n   # the one-person frame each pose is of
            t = ref['t'][frames]                                # the composite's capture times (the left frames')
            k = np.array([s[2] for s in samples])               # poses x 17 x 4
            px = np.stack([k[:, :, 0] * W, k[:, :, 1] * H], 2)  # pixels of the composite
            # the teacher in the composite's pixels, on the same frames
            r = rxy[src][:, COCO].copy()
            r[:, :, 0] = (r[:, :, 0] - W / 2) * args.scale + W / 2 + shift
            r[:, :, 1] = (r[:, :, 1] - H / 2) * args.scale + H / 2
            torso = torso_all[src] * args.scale
            sure = sure_all[src][:, COCO]
            ok = ref['found'][src]
            span = t[-1] - t[0]
            # the wrists: error, lag, unsure samples
            wr = (9, 10)
            err = np.concatenate([(np.linalg.norm(px[:, j] - r[:, j], axis=1) / torso)[sure[:, j] & ok] for j in wr])
            lags = [lag_ms(t[ok], px[ok, j], r[ok, j]) for j in wr]
            speed = np.zeros((len(t), 17))
            speed[1:-1] = np.linalg.norm(r[2:] - r[:-2], axis=2) / (t[2:] - t[:-2])[:, None] / torso[1:-1, None]
            vis = k[:, :, 3]
            wv = np.concatenate([vis[:, j] for j in wr])
            fast = np.concatenate([speed[:, j] > 2 for j in wr])
            print(f'  {side:5s}: {len(t)} poses, {len(t) / span:.1f}/s | wrist err {100 * np.median(err):.1f} / '
                  f'{100 * np.percentile(err, 90):.1f} %, lag {lags[0]:.0f} / {lags[1]:.0f} ms | unsure (<0.6 / <0.8) '
                  f'all {100 * np.mean(wv < 0.6):.0f} / {100 * np.mean(wv < 0.8):.0f} %, fast '
                  f'{100 * np.mean(wv[fast] < 0.6):.0f} / {100 * np.mean(wv[fast] < 0.8):.0f} % ({int(fast.sum())})')
            # swings: the run's, against the teacher's at the same sample times
            run_s = [(t[i], to_view(k[i], W, H), vis[i]) for i in range(len(t))]
            ref_k = np.zeros((len(t), 17, 2))
            ref_k[:, :, 0] = r[:, :, 0] / W
            ref_k[:, :, 1] = r[:, :, 1] / H
            ref_s = [(t[i], to_view(ref_k[i], W, H), np.ones(17)) for i in range(len(t)) if ok[i]]
            found, ignored = detect(run_s)
            truth, _ = detect(ref_s)
            pairs, missed, extra = match(found, truth)
            # and against the teacher on every camera frame of the person's time (30 a second): what the camera saw
            every = np.arange(frames[0], frames[-1] + 1)
            src30 = (every + (args.offset if side == 'right' else 0)) % n
            r30 = rxy[src30][:, COCO].copy()
            r30[:, :, 0] = ((r30[:, :, 0] - W / 2) * args.scale + W / 2 + shift) / W
            r30[:, :, 1] = ((r30[:, :, 1] - H / 2) * args.scale + H / 2) / H
            truth30, _ = detect([(ref['t'][f], to_view(r30[i], W, H), np.ones(17))
                                 for i, f in enumerate(every) if ref['found'][src30[i]]])
            pairs30, missed30, _ = match(found, truth30)
            strong30 = [e for e in truth30 if not e['gentle']]
            spairs30, _, _ = match([e for e in found if not e['gentle']], strong30, within=0.25)
            delay30 = [1000 * (e['reported'] - r_['reported']) for e, r_ in pairs30]
            strong_truth = [e for e in truth if not e['gentle']]
            spairs, smissed, _ = match([e for e in found if not e['gentle']], strong_truth)
            delay = [1000 * (e['reported'] - r_['reported']) for e, r_ in pairs]
            print(f'         swings {len(found)} ({sum(not e["gentle"] for e in found)} strong), {ignored} ignored | '
                  f'teacher {len(truth)} ({len(strong_truth)} strong): found {len(pairs)}, missed {missed}, extra '
                  f'{extra}; strong found {len(spairs)} of {len(strong_truth)} | reported '
                  f'{np.mean(delay) if delay else float("nan"):+.0f} ms vs the teacher')
            print(f'         vs the teacher at 30/s: {len(truth30)} swings ({len(strong30)} strong): found {len(pairs30)}, '
                  f'strong found {len(spairs30)} of {len(strong30)} ({100 * len(spairs30) / max(1, len(strong30)):.0f} %), '
                  f'reported {np.mean(delay30) if delay30 else float("nan"):+.0f} ms later')


if __name__ == '__main__':
    main()
