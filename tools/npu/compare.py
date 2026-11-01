"""Checks the NPU pose path against MediaPipe on still pictures (run in WSL with the RKNN venv, see convert.py):
  1. blazepose.py around the original TFLite models vs MediaPipe's own result (mp_reference.py): are the steps right?
  2. the same steps around the converted RKNN models in Rockchip's simulator: did the conversion keep the numbers?
  3. --box <dir>: what the box's NPU gave (pose_npu_test dumps), the same comparison.
Errors are in pixels of the picture (landmarks) and centimetres (world landmarks), over the 33 pose landmarks.
--crops: the landmark model alone, every runtime on exactly the crop the original model sees while tracking, against
the original (TFLite) model's answer: a converted model's own error, without the tracking chain (on a hard picture a
tiny difference moves the next crop, and the answers drift apart far more than the models do). --write-crops <dir>
saves those crops for pose_npu_test --crop.

    python tools/npu/compare.py --reference images/mp_reference.json --images images --tflite <.task contents>
        [--rknn [--quant normal --calibration <calibration.py folder>]] [--box dumps --box-labels {model},{model}-int8]
        [--frames 2] [--crops [--write-crops <dir>]]
"""
import argparse
import json
import os
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import blazepose  # noqa: E402


def tflite_runner(folder):
    from ai_edge_litert.interpreter import Interpreter
    nets = {}
    for name, file in (('detector', 'pose_detector.tflite'), ('landmarks', 'pose_landmarks_detector.tflite')):
        it = Interpreter(model_path=os.path.join(folder, file))
        it.allocate_tensors()
        nets[name] = it

    def run(name, image):
        it = nets[name]
        x = image.astype(np.float32)
        x = x / 127.5 - 1.0 if name == 'detector' else x / 255.0
        it.set_tensor(it.get_input_details()[0]['index'], x[None])
        it.invoke()
        return [it.get_tensor(d['index']).copy() for d in it.get_output_details()]
    return run


def rknn_runner(folder, model, quant=None, calibration=None, quantized=('detector', 'landmarks')):
    """The RKNN builds in Rockchip's simulator: float16, or INT8 (quant, calibration = calibration.py's folder) for the
    models named in quantized."""
    import convert
    nets = {}
    for name in ('detector', 'landmarks'):
        # the simulator needs the build, not just the exported file: rebuild from the TFLite with the same config
        q = quant if name in quantized else None
        dataset = None
        if q:
            dataset = os.path.join(calibration, 'detector.txt' if name == 'detector' else f'landmarks_{model}.txt')
        r = convert.make(convert.source(folder, model, name), name, quant=q, dataset=dataset)
        assert r.init_runtime() == 0
        nets[name] = r

    def run(name, image):
        return [np.asarray(o, np.float32) for o in nets[name].inference(inputs=[image[None]], data_format='nhwc')]
    return run


def errors(pose, ref, size):
    w, h = size
    ref_xy = np.array([[p[0] * w, p[1] * h] for p in ref['landmarks']])
    px = np.linalg.norm(pose['xy'][:33] - ref_xy, axis=1)
    ref_world = np.array(ref['world'])
    cm = np.linalg.norm(pose['world'][:33] - ref_world, axis=1) * 100
    vis = np.abs(pose['visibility'][:33] - np.array([p[3] for p in ref['landmarks']]))
    return px, cm, vis


def track(run, image, frames):
    """The picture as `frames` frames in a row (2 = the second crop from the first frame's landmarks)."""
    tracker = blazepose.Tracker(run)
    for _ in range(frames):
        pose = tracker(image)
    return pose


def report(label, pose, ref, size, totals):
    """One picture's errors; totals[label] collects them for the summary."""
    total = totals.setdefault(label, dict(px=[], px_max=[], cm=[], cm_max=[], vis=[], missed=0, extra=0))
    if pose is None or ref is None:
        print(f"  {label:22s} found: ours {pose is not None}, MediaPipe {ref is not None}")
        if pose is None and ref is not None:
            total['missed'] += 1
        elif pose is not None and ref is None:
            total['extra'] += 1
        return
    px, cm, vis = errors(pose, ref, size)
    for key, value in (('px', px.mean()), ('px_max', px.max()), ('cm', cm.mean()), ('cm_max', cm.max()),
                       ('vis', vis.mean())):
        total[key].append(value)
    print(f"  {label:22s} landmarks {px.mean():5.2f} px mean, {px.max():6.2f} max | world {cm.mean():5.2f} cm mean,"
          f" {cm.max():5.2f} max | visibility {vis.mean():.3f} | presence score {pose['score']:.3f}")


IN_CROP = (blazepose.LM_SIZE / 2, blazepose.LM_SIZE / 2, float(blazepose.LM_SIZE), 0.0)   # a crop as its own picture


def box_pose(path):
    """A pose_npu_test dump."""
    d = json.load(open(path))
    return dict(xy=np.array(d['xy']), world=np.array(d['world']), visibility=np.array(d['visibility']),
                score=d['score'], found=d.get('found', False))


def crops(args, model, runners, refs):
    """Every runtime's landmark model on the crop the original model sees on the second frame of the picture; errors
    against the original's answer in picture pixels (crop pixels x the crop's size / 256) and centimetres."""
    reference = runners['tflite']
    rois = {}
    totals = {}
    for picture in refs:
        image = cv2.cvtColor(cv2.imread(os.path.join(args.images, picture)), cv2.COLOR_BGR2RGB)
        tracker = blazepose.Tracker(reference)
        if tracker(image) is None:
            print(f'{picture}: nobody found by the original model')
            continue
        roi = tracker.roi
        rois[picture] = roi
        crop = blazepose.crop(image, roi)
        if args.write_crops:
            os.makedirs(args.write_crops, exist_ok=True)
            name = f'{picture}.{model}-crop.{blazepose.LM_SIZE}x{blazepose.LM_SIZE}.rgba'
            cv2.cvtColor(crop, cv2.COLOR_RGB2RGBA).tofile(os.path.join(args.write_crops, name))
        truth = blazepose.decode_landmarks(blazepose.by_name(reference('landmarks', crop)), IN_CROP)
        truth_ref = dict(landmarks=[[x / blazepose.LM_SIZE, y / blazepose.LM_SIZE, 0, v, 0]
                                    for (x, y), v in zip(truth['xy'][:33], truth['visibility'][:33])],
                         world=truth['world'][:33].tolist())
        to_picture = roi[2] / blazepose.LM_SIZE
        print(picture)
        poses = {label: blazepose.decode_landmarks(blazepose.by_name(run('landmarks', crop)), IN_CROP)
                 for label, run in runners.items() if label != 'tflite'}
        for label in args.box_labels.format(model=model).split(',') if args.box else ():
            dump = os.path.join(args.box, f'{picture}.{model}-crop.{label}.json')
            if os.path.exists(dump):
                poses[f'box {label}'] = box_pose(dump)
        for label, pose in poses.items():
            pose = dict(pose, xy=pose['xy'] * to_picture)
            report(label, pose, truth_ref, (blazepose.LM_SIZE * to_picture,) * 2, totals)
    if args.write_crops:
        with open(os.path.join(args.write_crops, f'rois.{model}.json'), 'w') as f:
            json.dump({k: list(map(float, v)) for k, v in rois.items()}, f)
    return totals


def summary(totals):
    """Over all pictures: the mean of the pictures' mean errors, the worst picture's mean, the worst landmark."""
    for label, t in totals.items():
        if not t['px']:
            print(f"  {label:22s} nothing to compare (missed {t['missed']}, extra {t['extra']})")
            continue
        print(f"  {label:22s} {len(t['px'])} pictures | landmarks {np.mean(t['px']):5.2f} px mean (worst picture"
              f" {np.max(t['px']):5.2f}), {np.max(t['px_max']):6.2f} max | world {np.mean(t['cm']):5.2f} cm mean"
              f" (worst {np.max(t['cm']):5.2f}), {np.max(t['cm_max']):5.2f} max | visibility {np.mean(t['vis']):.3f}"
              f" | missed {t['missed']}, extra {t['extra']}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--reference', required=True)
    ap.add_argument('--images', required=True)
    ap.add_argument('--tflite', default=None, help='folder with lite/ and full/ (the .task contents)')
    ap.add_argument('--rknn', action='store_true', help='also run the RKNN conversion in the simulator')
    ap.add_argument('--quant', default=None, help='--rknn: INT8, one of convert.QUANT (normal, mmse, kl, ...)')
    ap.add_argument('--calibration', default=None, help='--quant: calibration.py output folder')
    ap.add_argument('--quantized', default='detector,landmarks', help='--quant: which models (the others float16)')
    ap.add_argument('--models', default='lite,full')
    ap.add_argument('--box', default=None, help='folder with the box NPU dumps (<picture>.<label>.json)')
    ap.add_argument('--box-labels', default='{model}',
                    help='the pose_npu_test labels to compare, e.g. {model},{model}-int8')
    ap.add_argument('--no-tflite', action='store_true', help='skip the original models (box dumps only)')
    ap.add_argument('--frames', type=int, default=1, help='2 = compare the tracking crop (mp_reference --frames=2)')
    ap.add_argument('--crops', action='store_true', help='the landmark model alone on the same crops (see above)')
    ap.add_argument('--write-crops', default=None, help='--crops: also save them for pose_npu_test --crop')
    args = ap.parse_args()
    refs = json.load(open(args.reference))
    for model in args.models.split(','):
        runners = {}
        if args.crops or not args.no_tflite:
            runners['tflite'] = tflite_runner(os.path.join(args.tflite, model))
        if args.rknn:
            runners['rknn simulator'] = rknn_runner(args.tflite, model, args.quant, args.calibration,
                                                    args.quantized.split(','))
        print(f"=== {model}")
        if args.crops:
            totals = crops(args, model, runners, refs)
            print(f"--- {model}: all crops")
            summary(totals)
            continue
        totals = {}
        for picture, by_model in refs.items():
            image = cv2.cvtColor(cv2.imread(os.path.join(args.images, picture)), cv2.COLOR_BGR2RGB)
            size = image.shape[1::-1]
            print(picture)
            for label, run in runners.items():
                report(label, track(run, image, args.frames), by_model[model], size, totals)
            for label in args.box_labels.format(model=model).split(',') if args.box else ():
                dump = os.path.join(args.box, f"{picture}.{label}.json")
                if os.path.exists(dump):
                    pose = box_pose(dump)
                    report(f'box {label}', pose if pose['found'] else None, by_model[model], size, totals)
        print(f"--- {model}: all pictures")
        summary(totals)


if __name__ == '__main__':
    main()
