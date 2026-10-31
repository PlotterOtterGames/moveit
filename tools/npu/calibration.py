"""Calibration pictures for the INT8 models (convert.py --quant): what the two models really see, made with blazepose.py
from camera pictures: the person detector's letterboxed 224 x 224 frame of the whole picture, and the landmark model's
rotated 256 x 256 crop around the person (from a first frame's landmarks, as while tracking), each also mirrored, a
little darker or brighter, and (the crops) with the crop a little off, as when the person moves between frames. Saved as
uint8 RGB .npy files (channels first, as RKNN wants them) with one list per model for RKNN's build(dataset=...):
detector.txt, landmarks_lite.txt, landmarks_full.txt. A picture where the original models find nobody gives detector frames only.

Run in WSL with the RKNN venv (see convert.py), on the unpacked .task files (convert.py --keep <folder>):
    python tools/npu/calibration.py --tflite /mnt/d/rknn/models --out /mnt/d/rknn/calib/set <pictures or folders>...
--sheet writes a contact sheet of the crops (sheet_<model>.jpg) to look at.
"""
import argparse
import json
import math
import os
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import blazepose  # noqa: E402
from compare import tflite_runner  # noqa: E402

PICTURES = ('.jpg', '.jpeg', '.png')


def pictures(paths):
    for path in paths:
        if os.path.isdir(path):
            yield from sorted(os.path.join(path, f) for f in os.listdir(path) if f.lower().endswith(PICTURES))
        else:
            yield path


def gamma(image, g):
    """Darker (g > 1) or brighter (g < 1), like another room's light or the camera tuner's levels."""
    table = np.array([255 * (i / 255) ** g for i in range(256)], np.uint8)
    return table[image]


def variants(image, rng):
    """The picture as the camera gave it, mirrored (a person facing the other way) and with other light."""
    yield 'orig', image
    yield 'flip', image[:, ::-1].copy()
    yield 'light', gamma(image, float(rng.choice([0.7, 0.8, 1.25, 1.4])))


def jitter(roi, rng):
    """The tracking crop a little off: the person moved since the frame it came from."""
    cx, cy, size, rotation = roi
    return (cx + rng.uniform(-0.08, 0.08) * size, cy + rng.uniform(-0.08, 0.08) * size,
            size * rng.uniform(0.88, 1.12), rotation + rng.uniform(-0.2, 0.2))


def tracking_roi(run, image):
    """The crop the landmark model sees on the second frame of a still picture (from the first frame's landmarks)."""
    tracker = blazepose.Tracker(run)
    if tracker(image) is None:
        return None
    return tracker.roi


def save(out, name, image, lists, key):
    """RKNN reads .npy calibration inputs channels first (3, h, w), whatever the model's own layout."""
    path = os.path.join(out, name + '.npy')
    np.save(path, np.ascontiguousarray(image.transpose(2, 0, 1)))
    lists.setdefault(key, []).append(path)


def sheet(out, model, paths, columns=12, cell=128):
    if not paths:
        return
    rows = math.ceil(len(paths) / columns)
    board = np.zeros((rows * cell, columns * cell, 3), np.uint8)
    for i, path in enumerate(paths):
        small = cv2.resize(np.load(path).transpose(1, 2, 0), (cell, cell), interpolation=cv2.INTER_AREA)
        r, c = divmod(i, columns)
        board[r * cell:(r + 1) * cell, c * cell:(c + 1) * cell] = small
    cv2.imwrite(os.path.join(out, f'sheet_{model}.jpg'), cv2.cvtColor(board, cv2.COLOR_RGB2BGR), [cv2.IMWRITE_JPEG_QUALITY, 85])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--tflite', required=True, help='folder with lite/ and full/ (the .task contents)')
    ap.add_argument('--out', required=True)
    ap.add_argument('--seed', type=int, default=7)
    ap.add_argument('--sheet', action='store_true')
    ap.add_argument('paths', nargs='+')
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    rng = np.random.default_rng(args.seed)
    runs = {model: tflite_runner(os.path.join(args.tflite, model)) for model in ('lite', 'full')}
    lists, manifest = {}, []
    for path in pictures(args.paths):
        image = cv2.cvtColor(cv2.imread(path), cv2.COLOR_BGR2RGB)
        base = os.path.splitext(os.path.basename(path))[0]
        found = []
        for label, picture in variants(image, rng):
            name = f'{base}.{label}'
            save(args.out, f'{name}.det', blazepose.letterbox(picture)[0], lists, 'detector')
            entry = dict(picture=path, variant=label, crops={})
            for model, run in runs.items():
                roi = tracking_roi(run, picture)
                if roi is None:
                    continue
                rois = [('track', roi), ('moved', jitter(roi, rng))] if label == 'orig' else [('track', roi)]
                for kind, r in rois:
                    save(args.out, f'{name}.{model}.{kind}', blazepose.crop(picture, r), lists, f'landmarks_{model}')
                entry['crops'][model] = [list(map(float, roi))]
                found.append(f'{label}/{model}')
            manifest.append(entry)
        print(f'{base}: person found in {", ".join(found) or "none"}')
    for key, paths in lists.items():
        with open(os.path.join(args.out, f'{key}.txt'), 'w') as f:
            f.write('\n'.join(paths) + '\n')
        print(f'{key}: {len(paths)} samples')
        if args.sheet:
            sheet(args.out, key, paths)
    with open(os.path.join(args.out, 'manifest.json'), 'w') as f:
        json.dump(manifest, f, indent=1)


if __name__ == '__main__':
    main()
