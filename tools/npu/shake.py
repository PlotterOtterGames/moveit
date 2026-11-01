"""How much the pose landmarks shake for a person standing still, on real camera pictures: each picture becomes a short
video (fresh camera-like noise + JPEG every frame), tracked like the box tracks (the crop from the previous frame's
landmarks), with the original models (the box's float16 matches them to ~0.3 px). Reported: the raw frame-to-frame step
of the joints before any smoothing, in % of the torso length, median over frames and pictures, for lite and full and
for two ways to calm it: the crop shrunk by area averaging instead of bilinear samples ("area"), and the model also run
on the mirrored crop with both answers averaged ("flip"). Run in WSL with the RKNN venv (see convert.py):
    python tools/npu/shake.py --tflite /mnt/d/rknn/models [--noise 3] <pictures>...
2026-11-01 (docs/hardware.md "The camera, and why the skeleton shakes"): on evening box snapshots lite stepped 12 % of
a torso at the wrists against ~1 % on MediaPipe's clean test photo; full -30 %, flip -20-35 %, area no gain.
"""
import argparse
import os
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import blazepose  # noqa: E402
from compare import tflite_runner  # noqa: E402

JOINTS = {'wrist': (15, 16), 'elbow': (13, 14), 'shoulder': (11, 12), 'hip': (23, 24), 'knee': (25, 26)}
PAIRS = [(1, 4), (2, 5), (3, 6), (7, 8), (9, 10), (11, 12), (13, 14), (15, 16), (17, 18), (19, 20), (21, 22),
         (23, 24), (25, 26), (27, 28), (29, 30), (31, 32)]
SWAP = list(range(blazepose.NUM_LANDMARKS))       # a landmark's index in the mirrored picture
for a, b in PAIRS:
    SWAP[a], SWAP[b] = b, a
VARIANTS = (('bilinear', False, False), ('area', True, False), ('flip', False, True), ('area+flip', True, True))


def area_crop(image, roi):
    """The crop with the picture first shrunk by area averaging, so the warp itself scales ~1:1."""
    scale = roi[2] / blazepose.LM_SIZE
    if scale <= 1.05:
        return blazepose.crop(image, roi)
    small = cv2.resize(image, None, fx=1 / scale, fy=1 / scale, interpolation=cv2.INTER_AREA)
    sx, sy = small.shape[1] / image.shape[1], small.shape[0] / image.shape[0]
    return blazepose.crop(small, (roi[0] * sx, roi[1] * sy, roi[2] * sx, roi[3]))


class Tracker(blazepose.Tracker):
    def __init__(self, run, area=False, flip=False):
        super().__init__(run)
        self.area, self.flip = area, flip

    def __call__(self, image):
        roi = self.roi or self.detect(image)
        if roi is None:
            return None
        crop = (area_crop if self.area else blazepose.crop)(image, roi)
        pose = blazepose.decode_landmarks(blazepose.by_name(self.run('landmarks', crop)), roi)
        if self.flip:
            whole = (blazepose.LM_SIZE / 2, blazepose.LM_SIZE / 2, float(blazepose.LM_SIZE), 0.0)
            mirrored = blazepose.decode_landmarks(blazepose.by_name(self.run('landmarks', crop[:, ::-1].copy())), whole)
            xy = mirrored['xy'].copy()
            xy[:, 0] = blazepose.LM_SIZE - 1 - xy[:, 0]          # back from the mirror, crop pixels
            m = blazepose.crop_matrix(roi)
            pose['xy'] = (pose['xy'] + xy[SWAP] @ m[:, :2].T + m[:, 2]) / 2
            pose['score'] = (pose['score'] + mirrored['score']) / 2
        if pose['score'] < self.min_presence:
            self.roi = None
            return None
        self.roi = blazepose.roi_from_points(pose['xy'][33], pose['xy'][34])
        return pose


def video(path, noise, frames, seed):
    base = cv2.cvtColor(cv2.imread(path), cv2.COLOR_BGR2RGB).astype(np.float32)
    rng = np.random.default_rng(seed)
    out = []
    for _ in range(frames):
        frame = np.clip(base + rng.normal(0, noise, base.shape), 0, 255).astype(np.uint8)
        jpg = cv2.imencode('.jpg', frame, [cv2.IMWRITE_JPEG_QUALITY, 85])[1]
        out.append(cv2.imdecode(jpg, cv2.IMREAD_UNCHANGED))
    return out


def steps(tracker, frames, settle=2):
    """The joints' median frame-to-frame step in % of the torso, or None when the person was mostly lost."""
    got = [p['xy'][:33] for i, p in enumerate(tracker(f) for f in frames) if p is not None and i >= settle]
    if len(got) < 4:
        return None
    a = np.array(got)
    torso = np.linalg.norm((a[:, 11] + a[:, 12]) / 2 - (a[:, 23] + a[:, 24]) / 2, axis=1).mean()
    return {k: 100 * np.median([np.linalg.norm(np.diff(a[:, j], axis=0), axis=1) for j in js]) / torso
            for k, js in JOINTS.items()}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--tflite', required=True, help='folder with lite/ and full/ (the .task contents)')
    ap.add_argument('--noise', type=float, default=3.0, help='camera noise, sigma in 8-bit levels')
    ap.add_argument('--frames', type=int, default=14)
    ap.add_argument('pictures', nargs='+')
    args = ap.parse_args()
    clips = [(os.path.basename(p), video(p, args.noise, args.frames, seed)) for seed, p in enumerate(args.pictures)]
    for model in ('lite', 'full'):
        run = tflite_runner(os.path.join(args.tflite, model))
        print(f'=== {model}, noise {args.noise}: median frame-to-frame step, % of the torso')
        for name, area, flip in VARIANTS:
            results = [steps(Tracker(run, area, flip), frames) for _, frames in clips]
            found = [r for r in results if r is not None]
            if not found:
                print(f'  {name:10s} the person was lost in every picture')
                continue
            med = {k: np.median([r[k] for r in found]) for k in JOINTS}
            print(f'  {name:10s} {len(found)} pictures ({len(results) - len(found)} lost)  '
                  + '  '.join(f'{k} {v:5.2f}' for k, v in med.items()))


if __name__ == '__main__':
    main()
