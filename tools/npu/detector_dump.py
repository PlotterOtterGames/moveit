"""Camera frames and the lab's detector inputs for them, for tools/npu/trust_check.cpp (the box's DetectorEye must make
the same pictures):

    python tools/npu/detector_dump.py <clip.mjpg> <first frame> <frames> <out.bin> [--idle]

Every frame is looked at (the detector runs on each while nobody is followed and the service isn't idle yet); --idle:
like the idle service, three frames watched, then one looked at.
"""
import argparse
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import blazepose  # noqa: E402
import tracklab  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('clip')
    ap.add_argument('first', type=int)
    ap.add_argument('frames', type=int)
    ap.add_argument('out')
    ap.add_argument('--idle', action='store_true')
    args = ap.parse_args()
    clip = tracklab.Clip(args.clip)
    engine = tracklab.Engine(None, det='mean')
    with open(args.out, 'wb') as f:
        for k in range(args.frames):
            image = clip.rgb(args.first + k)
            t_ns = int(round(clip.times[args.first + k] * 1e9)) + 1_000_000_000
            if k == 0:
                f.write(b'EYECHECK' + np.array([args.frames, image.shape[1], image.shape[0], blazepose.DET_SIZE],
                                               np.int32).tobytes())
            look = not args.idle or k % 4 == 3
            if look:
                inp, _ = engine.detector_input(image, t_ns * 1e-9)
            else:
                engine.watch(image, t_ns * 1e-9)
                inp = np.zeros((blazepose.DET_SIZE, blazepose.DET_SIZE, 3), np.uint8)
            f.write(np.int64(t_ns).tobytes() + np.int32(1 if look else 0).tobytes())
            f.write(np.ascontiguousarray(image, np.uint8).tobytes())
            f.write(np.ascontiguousarray(inp, np.uint8).tobytes())
    print(f'{args.out}: {args.frames} frames from {args.first}')


if __name__ == '__main__':
    main()
