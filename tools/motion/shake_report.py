"""How much a recorded skeleton shakes while the player holds still: a recording from the camera test screen (Settings →
Kamera, OK; on the box /sdcard/Android/data/al.tundu.games/files/sessions) or main.py --record, one line per packet.

Each joint's path is smoothed both ways in time (a centred Gaussian on the samples' capture times, sigma 0.1 s: the
movement; skipped frames and gaps don't bend it); the shake is what is left (the packet minus that path), counted only
on frames where the smoothed path is slow (the player holding still). Steps count only between samples one frame apart.
Reported per joint, in % of the torso length (shoulders' middle to hips' middle, the unit the games use) and in pixels of
a 1080p skeleton drawn full height: the shake's RMS, the median frame-to-frame step, and how many still frames jump more
than 3 % of a torso. Also the packet timing and the joints' visibility.

    motion-service-py/.venv/Scripts/python tools/motion/shake_report.py <recording.jsonl> [...]
"""
import json
import sys

import numpy as np

NAMES = ['nose', 'l eye', 'r eye', 'l ear', 'r ear', 'l shoulder', 'r shoulder', 'l elbow', 'r elbow', 'l wrist',
         'r wrist', 'l hip', 'r hip', 'l knee', 'r knee', 'l ankle', 'r ankle']
GROUPS = {'head': (0,), 'shoulders': (5, 6), 'elbows': (7, 8), 'wrists': (9, 10), 'hips': (11, 12), 'knees': (13, 14),
          'ankles': (15, 16)}
STILL = 0.3          # torso lengths per second: slower than this counts as holding still
SIGMA = 0.1          # seconds (~3 frames at 30 fps)


def load(path):
    t, xy, vis = [], [], []
    for line in open(path):
        p = json.loads(line)
        if not p.get('people'):
            continue
        k = np.array(p['people'][0]['k'], np.float64).reshape(17, 4)
        t.append(p['t'] / 1000.0)
        xy.append(k[:, :2] * (p['w'], p['h']))
        vis.append(k[:, 3])
    return np.array(t), np.array(xy), np.array(vis)


def smooth(t, a, sigma=SIGMA):
    """Centred Gaussian in time (the samples' own capture times, so skipped frames and gaps don't bend the path):
    each sample the weighted mean of its neighbours within 3 sigma."""
    out = np.empty_like(a)
    lo = hi = 0
    for i, ti in enumerate(t):
        while t[lo] < ti - 3 * sigma:
            lo += 1
        while hi < len(t) and t[hi] <= ti + 3 * sigma:
            hi += 1
        w = np.exp(-0.5 * ((t[lo:hi] - ti) / sigma) ** 2)
        out[i] = np.tensordot(w / w.sum(), a[lo:hi], axes=1)
    return out


def report(path):
    t, xy, vis = load(path)
    dt = np.diff(t)
    torso = np.median(np.linalg.norm(xy[:, 5:7].mean(1) - xy[:, 11:13].mean(1), axis=1))
    ref = smooth(t, xy)
    speed = np.linalg.norm(np.gradient(ref, t, axis=0), axis=2) / torso          # torso lengths / s, per joint
    still = np.all(speed[:, [5, 6, 7, 8, 9, 10, 11, 12]] < STILL, axis=1)
    shake = np.linalg.norm(xy - ref, axis=2) / torso * 100                        # % of torso
    # the step between consecutive samples, only where they are one frame apart (a skipped frame doubles a step)
    one = np.r_[False, np.diff(t) < 0.045]
    step = np.r_[np.zeros((1, 17)), np.linalg.norm(np.diff(xy, axis=0), axis=2)] / torso * 100
    step[~one] = np.nan
    to1080 = 1080 * 0.25 / 100            # a torso ~25 % of a 1080p screen when the skeleton is drawn full height
    print(f'{path}\n  {len(t)} packets over {t[-1] - t[0]:.1f} s, interval median {np.median(dt) * 1000:.0f} ms,'
          f' worst {dt.max() * 1000:.0f} ms; torso {torso:.0f} px of the camera picture; still {still.mean() * 100:.0f} %'
          f' of the time ({still.sum()} frames)')
    if still.sum() < 10:
        print('  too little holding still to measure the shake')
        return
    print(f'  {"joint":10s} {"shake RMS":>14s} {"step median":>14s} {"jumps >3%":>10s} {"visibility":>10s}   (still frames;'
          f' % of torso, ~px on a 1080p mirror)')
    for name, js in GROUPS.items():
        s = shake[still][:, js].ravel()
        st = step[still][:, js].ravel()
        st = st[~np.isnan(st)]
        rms = np.sqrt(np.mean(s ** 2))
        print(f'  {name:10s} {rms:6.2f} % {rms * to1080:4.1f} px {np.median(st):6.2f} % {np.median(st) * to1080:4.1f} px'
              f' {np.mean(st > 3) * 100:8.1f} % {np.mean(vis[still][:, js]):10.2f}')


if __name__ == '__main__':
    for p in sys.argv[1:]:
        report(p)
