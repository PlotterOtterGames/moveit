"""Look at what the pose models saw: frames of a clip with the skeletons of several tracklab runs drawn over them.

    python tools/npu/tracklab_view.py sheet <clip.mjpg> --runs a.npz,b.npz[,c.npz] --frames 100,101,102 --out sheet.png
        [--key f_xy|xy] [--zoom 2] [--heat 15] [--cols 4] [--pad 0.15]
    python tools/npu/tracklab_view.py apart <a.npz> <b.npz> [--joint 15,16] [--min 0.3] [--key xy]

sheet: one tile per frame, cropped to the first run's landmark crop box (padded), each run's skeleton in its own colour
(run 1 green, 2 red, 3 blue, 4 yellow); --heat N adds the first run's heatmap of landmark N as a tint.
apart: the frames where two runs' joints are furthest apart (torso lengths), grouped into stretches.
"""
import argparse
import os
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import blazepose  # noqa: E402
from tracklab import Clip  # noqa: E402

BONES = [(11, 12), (11, 13), (13, 15), (12, 14), (14, 16), (11, 23), (12, 24), (23, 24), (23, 25), (25, 27), (24, 26),
         (26, 28), (0, 11), (0, 12)]
COLOURS = [(60, 220, 60), (60, 60, 255), (255, 140, 40), (40, 230, 255), (255, 60, 255)]   # BGR
NAMES = {0: 'nose', 11: 'l shoulder', 12: 'r shoulder', 13: 'l elbow', 14: 'r elbow', 15: 'l wrist', 16: 'r wrist',
         23: 'l hip', 24: 'r hip', 25: 'l knee', 26: 'r knee', 27: 'l ankle', 28: 'r ankle'}


def load(path):
    d = np.load(path)
    return {k: d[k] for k in d.files}


def torso(xy):
    """Shoulders' middle to hips' middle, per frame (pixels)."""
    return np.linalg.norm((xy[:, 11] + xy[:, 12]) / 2 - (xy[:, 23] + xy[:, 24]) / 2, axis=1)


def draw(tile, xy, colour, to_tile, thick=2, left_dot=True):
    pts = [to_tile(p) for p in xy]
    for a, b in BONES:
        if np.all(np.isfinite(xy[[a, b]])):
            cv2.line(tile, pts[a], pts[b], colour, thick, cv2.LINE_AA)
    for j in (15, 16):
        if np.all(np.isfinite(xy[j])):
            cv2.circle(tile, pts[j], thick + 3, colour, -1 if (j == 15) == left_dot else 2, cv2.LINE_AA)


def sheet(args):
    clip = Clip(args.clip)
    runs = [load(p) for p in args.runs.split(',')]
    frames = [int(f) for f in args.frames.split(',')]
    tiles = []
    for f in frames:
        image = cv2.cvtColor(clip.rgb(f), cv2.COLOR_RGB2BGR)
        roi = next((r['roi'][f] for r in runs if np.all(np.isfinite(r['roi'][f]))), None)
        if roi is None:
            x0, y0, x1, y1 = 0, 0, image.shape[1], image.shape[0]
        else:
            half = roi[2] / 2 * (1 + args.pad) if args.box <= 0 else args.box / 2
            cx, cy = (roi[0], roi[1]) if args.centre is None else args.centre
            x0, y0, x1, y1 = int(cx - half), int(cy - half), int(cx + half), int(cy + half)
        x0c, y0c, x1c, y1c = max(0, x0), max(0, y0), min(image.shape[1], x1), min(image.shape[0], y1)
        part = image[y0c:y1c, x0c:x1c]
        tile = cv2.resize(part, None, fx=args.zoom, fy=args.zoom, interpolation=cv2.INTER_CUBIC)
        if args.gain != 1:
            tile = np.clip(tile.astype(np.float32) * args.gain, 0, 255).astype(np.uint8)

        def to_tile(p, x0c=x0c, y0c=y0c):
            return int(round((p[0] - x0c) * args.zoom)), int(round((p[1] - y0c) * args.zoom))
        if args.heat is not None and 'heat' in runs[0] and np.all(np.isfinite(runs[0]['roi'][f])):
            # the heatmap of one landmark, warped from the crop back onto the picture
            h = runs[0]['heat'][f, args.heat].astype(np.float32) / 255.0
            m = blazepose.crop_matrix(runs[0]['roi'][f])              # crop pixel -> picture
            m64 = m.copy()
            m64[:, :2] *= blazepose.LM_SIZE / 64.0                    # heat cell -> picture
            full = cv2.warpAffine(h, m64, (image.shape[1], image.shape[0]), flags=cv2.INTER_LINEAR)
            part_h = cv2.resize(full[y0c:y1c, x0c:x1c], (tile.shape[1], tile.shape[0]))
            tint = np.zeros_like(tile)
            tint[..., 2] = 255
            tint[..., 0] = 255
            a = np.clip(part_h, 0, 1)[..., None] * 0.6
            tile = (tile * (1 - a) + tint * a).astype(np.uint8)
        for k, r in enumerate(runs):
            if r['found'][f]:
                draw(tile, r[args.key][f], COLOURS[k % len(COLOURS)], to_tile)
        label = f'{f}  {clip.times[f]:.2f}s'
        cv2.putText(tile, label, (6, 18), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 0, 0), 3, cv2.LINE_AA)
        cv2.putText(tile, label, (6, 18), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1, cv2.LINE_AA)
        tiles.append(tile)
    h = max(t.shape[0] for t in tiles)
    w = max(t.shape[1] for t in tiles)
    cols = min(args.cols, len(tiles))
    rows = (len(tiles) + cols - 1) // cols
    out = np.zeros((rows * h, cols * w, 3), np.uint8)
    for i, t in enumerate(tiles):
        r, c = divmod(i, cols)
        out[r * h:r * h + t.shape[0], c * w:c * w + t.shape[1]] = t
    cv2.imwrite(args.out, out)
    print(f'{args.out}: {out.shape[1]} x {out.shape[0]}, {len(tiles)} frames')


def apart(args):
    a, b = load(args.a), load(args.b)
    both = a['found'] & b['found']
    size = np.nanmedian(torso(b[args.key][both]))
    for j in [int(x) for x in args.joint.split(',')]:
        d = np.linalg.norm(a[args.key][:, j] - b[args.key][:, j], axis=1) / size
        d[~both] = np.nan
        far = np.nonzero(d > args.min)[0]
        print(f'{NAMES.get(j, j)}: median {np.nanmedian(d) * 100:.1f} %, 90th {np.nanpercentile(d, 90) * 100:.1f} %, '
              f'> {args.min:.2f} T in {len(far)} of {both.sum()} frames ({100 * len(far) / max(1, both.sum()):.1f} %)')
        # stretches of consecutive frames
        runs, start = [], None
        for f in far:
            if start is None:
                start = prev = f
            elif f - prev > 2:
                runs.append((start, prev))
                start = f
            prev = f
        if start is not None:
            runs.append((start, prev))
        runs.sort(key=lambda r: -(r[1] - r[0]))
        print('  stretches (first-last frame, peak): ' + ', '.join(
            f'{s}-{e} ({np.nanmax(d[s:e + 1]):.2f})' for s, e in runs[:args.top]))


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest='cmd', required=True)
    s = sub.add_parser('sheet')
    s.add_argument('clip')
    s.add_argument('--runs', required=True)
    s.add_argument('--frames', required=True)
    s.add_argument('--out', required=True)
    s.add_argument('--key', default='xy')
    s.add_argument('--zoom', type=float, default=1.0)
    s.add_argument('--heat', type=int)
    s.add_argument('--cols', type=int, default=4)
    s.add_argument('--pad', type=float, default=0.0)
    s.add_argument('--box', type=float, default=0, help='tile size in picture pixels instead of the crop box')
    s.add_argument('--centre', type=lambda v: tuple(float(x) for x in v.split(',')))
    s.add_argument('--gain', type=float, default=1.0)
    p = sub.add_parser('apart')
    p.add_argument('a')
    p.add_argument('b')
    p.add_argument('--joint', default='15,16')
    p.add_argument('--min', type=float, default=0.3)
    p.add_argument('--key', default='xy')
    p.add_argument('--top', type=int, default=12)
    args = ap.parse_args()
    {'sheet': sheet, 'apart': apart}[args.cmd](args)


if __name__ == '__main__':
    main()
