"""Two people from a one-person box clip (tools/motion/box_clip.py), for the engine's two-player mode while there is no
two-person recording (2026-11-12, docs/hardware.md "Two players after the tracking rework"): the picture's window
[xa, xa + 512) at frame i on the left half, the window [xb, xb + 512) at frame i + offset on the right half, re-encoded as
MJPEG with the clip's capture times and a .log beside it (pose_npu_test --people 2 --clip reads both). The routine's
player stands at x ~530 of 1024: --xa 270 --xb 270 puts the two ~500 px apart (their crops don't overlap), --xa 170 --xb
382 ~300 px (~1.5-2 torso lengths: each crop holds part of the other, as in a real match). --scale shrinks each picture
around its middle first (people farther away; the edges filled with the picture's own edge rows).

    motion-service-py/.venv/Scripts/python tools/npu/two_clip.py <clip.mjpg> <out.mjpg> [--xa 270] [--xb 270]
        [--offset 1125] [--scale 1.0]

Score a run with tools/npu/two_score.py (the same --xa as --x0, --xb as --x0b, --offset, --scale).
"""
import argparse
import os
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'motion'))
from mjpeg_clip import frames  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('clip')
    ap.add_argument('out', type=Path)
    ap.add_argument('--xa', type=int, default=270)
    ap.add_argument('--xb', type=int, default=270)
    ap.add_argument('--offset', type=int, default=1125)
    ap.add_argument('--scale', type=float, default=1.0)
    args = ap.parse_args()
    shots = frames(args.clip)
    n = len(shots)

    def picture(i):
        img = cv2.imdecode(np.frombuffer(shots[i % n][1], np.uint8), cv2.IMREAD_COLOR)
        if args.scale != 1.0:
            h, w = img.shape[:2]
            small = cv2.resize(img, (int(w * args.scale), int(h * args.scale)), interpolation=cv2.INTER_AREA)
            top, left = (h - small.shape[0]) // 2, (w - small.shape[1]) // 2
            img = cv2.copyMakeBorder(small, top, h - small.shape[0] - top, left, w - small.shape[1] - left,
                                     cv2.BORDER_REPLICATE)
        return img

    with open(args.out, 'wb') as out, open(args.out.with_suffix('.log'), 'w') as log:
        for i in range(n):
            both = np.hstack([picture(i)[:, args.xa:args.xa + 512], picture(i + args.offset)[:, args.xb:args.xb + 512]])
            ok, enc = cv2.imencode('.jpg', both, [cv2.IMWRITE_JPEG_QUALITY, 92])
            out.write(enc.tobytes())
            log.write(f'idx: {i % 4} seq: {i} bytesused: {len(enc)} ts: {1000.0 + shots[i][0]:.6f}\n')
    print(n, 'frames ->', args.out)


if __name__ == '__main__':
    main()
