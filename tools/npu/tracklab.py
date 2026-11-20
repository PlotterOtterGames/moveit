"""The box's pose engine (pose_npu.cpp) in Python, run on recorded box-camera clips (tools/motion/box_clip.py): the
lab where changes to the tracking are measured before they go into the C++ (2026-11-10, docs/hardware.md "Tracking
that listens to the model").

Runs in WSL with the RKNN venv (LiteRT runs MediaPipe's original models; the box's float16 builds match them to ~0.3 px):

    . /mnt/d/rknn/venv/bin/activate
    python tools/npu/tracklab.py run <clip.mjpg> --model lite|full|heavy --out <dir> [--variant trust|stock|teacher]
        [--det mean|clahe|plain] [--heat] [--first N --last M] [--tag .name] [--dump <file>]

Variants (tracklab_variants.py): trust = the box's engine today (tracklab_trust.py, the lab twin of pose_trust.h);
stock = MediaPipe's own tracking (with --det clahe: the box before 2026-11-10); teacher = the reference the others are
scored against (the heavy model, several crops a frame). <variant>:<NAME>=<value>,... sets the engine's numbers.

`run` writes <dir>/<clip stem>.<model>.<variant><tag>.npz: per frame the capture time, whether someone was found, the
crop, the model's raw answers (regression, heatmap peaks, visibility, presence, world joints) and the engine's
smoothed output, for tracklab_score.py and tracklab_view.py. --dump also writes the frames' raw model answers and
results for tools/npu/trust_check.cpp (the C++ replayed on the PC).

Models: a folder per model under --models (default /mnt/d/rknn/work) with pose_detector.tflite and
pose_landmarks_detector.tflite (the contents of MediaPipe's .task files; "heavy" = MediaPipe's
pose_landmark_heavy.tflite, the reference).
"""
import argparse
import math
import mmap
import os
import re
import sys
import time

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import blazepose  # noqa: E402
from mpfilters import LowPass, OneEuro, RelativeVelocity  # noqa: E402

N = blazepose.NUM_LANDMARKS
POSE = 33
# a landmark's index in the mirrored picture (left and right exchanged)
PAIRS = [(1, 4), (2, 5), (3, 6), (7, 8), (9, 10), (11, 12), (13, 14), (15, 16), (17, 18), (19, 20), (21, 22),
         (23, 24), (25, 26), (27, 28), (29, 30), (31, 32)]
SWAP = list(range(N))
for _a, _b in PAIRS:
    SWAP[_a], SWAP[_b] = _b, _a
MIN_PRESENCE = 0.5
UPRIGHT_FRAMES = 15


# ------------------------------------------------------------------------------------------------------------ clips
def clip_index(clip):
    """Byte ranges of a raw MJPEG clip's frames and their capture times (seconds from the first frame)."""
    with open(clip, 'rb') as f, mmap.mmap(f.fileno(), 0, access=mmap.ACCESS_READ) as mm:
        starts = [m.start() for m in re.finditer(rb'\xff\xd8\xff', mm)]
        size = len(mm)
    ranges = list(zip(starts, starts[1:] + [size]))
    log = os.path.splitext(clip)[0] + '.log'
    times = []
    if os.path.exists(log):
        for line in open(log, errors='replace'):
            m = re.search(r'\bts:\s*([0-9]+\.[0-9]+)', line)
            if m:
                times.append(float(m.group(1)))
    if len(times) < len(ranges):
        times = [i / 30.0 for i in range(len(ranges))]
    return ranges, np.array(times[:len(ranges)]) - times[0]


class Clip:
    def __init__(self, path):
        self.path = path
        self.ranges, self.times = clip_index(path)
        self.file = open(path, 'rb')
        self.mm = mmap.mmap(self.file.fileno(), 0, access=mmap.ACCESS_READ)

    def __len__(self):
        return len(self.ranges)

    def rgb(self, i):
        a, b = self.ranges[i]
        bgr = cv2.imdecode(np.frombuffer(self.mm[a:b], np.uint8), cv2.IMREAD_COLOR)
        return cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)


# ----------------------------------------------------------------------------------------------------------- models
def litert_runner(folder, threads=4):
    from ai_edge_litert.interpreter import Interpreter
    nets = {}
    for name, file in (('detector', 'pose_detector.tflite'), ('landmarks', 'pose_landmarks_detector.tflite')):
        it = Interpreter(model_path=os.path.join(folder, file), num_threads=threads)
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


# ------------------------------------------------------------------------------------------------------- the engine
def local_contrast(rgb, x0, y0, x1, y1, clip=4.0):
    """The detector's local contrast (pose_npu.cpp LocalContrast): CLAHE on the luma, the colour moved along."""
    part = rgb[y0:y1, x0:x1].astype(np.int32)
    luma = ((77 * part[..., 0] + 150 * part[..., 1] + 29 * part[..., 2] + 128) >> 8).astype(np.uint8)
    out = cv2.createCLAHE(clipLimit=clip, tileGridSize=(8, 8)).apply(luma)
    d = out.astype(np.int32) - luma.astype(np.int32)
    res = rgb.copy()
    res[y0:y1, x0:x1] = np.clip(part + d[..., None], 0, 255).astype(np.uint8)
    return res


def block_shrink(image, S, s, px, py):
    """The picture shrunk into the detector's square the way pose_npu.cpp does it (ShrinkInto): each output pixel
    inside the picture's area is the average of a b x b block of camera pixels around its centre, b = the whole
    pixels a detector pixel covers (4 of 4.57 for a 1024 px wide picture); outside: black. Returns the square as
    floats and the picture's rectangle in it."""
    h, w = image.shape[:2]
    b = max(1, int(1.0 / s))
    x0, x1 = int(math.ceil(px)), min(S, int(math.floor(px + w * s)))
    y0, y1 = int(math.ceil(py)), min(S, int(math.floor(py + h * s)))
    integral = cv2.integral(image)                           # (h + 1, w + 1, 3)
    us = np.arange(x0, x1)
    vs = np.arange(y0, y1)
    cx = np.clip(np.floor((us + 0.5 - px) / s - b / 2.0 + 0.5).astype(int), 0, w - b)
    cy = np.clip(np.floor((vs + 0.5 - py) / s - b / 2.0 + 0.5).astype(int), 0, h - b)
    a = integral[cy[:, None], cx[None, :]]
    bb = integral[cy[:, None], cx[None, :] + b]
    c = integral[cy[:, None] + b, cx[None, :]]
    d = integral[cy[:, None] + b, cx[None, :] + b]
    block = (d - bb - c + a).astype(np.float32) / (b * b)
    out = np.zeros((S, S, 3), np.float32)
    out[y0:y1, x0:x1] = np.floor(block + 0.5)
    return out, (x0, y0, x1, y1)


def upright(xy):
    shoulders, hips = 0.5 * (xy[11, 1] + xy[12, 1]), 0.5 * (xy[23, 1] + xy[24, 1])
    return shoulders < hips and xy[0, 1] < shoulders + 0.1 * (hips - shoulders)


def heat_peaks(conf):
    """Per landmark, the heatmap's global peak: (x, y) in 0..1 of the crop by the score-weighted centre of the 7 x 7
    window around the best cell, and its confidence. conf: (64, 64, n) sigmoid scores."""
    hh, hw, n = conf.shape
    flat = conf.reshape(-1, n)
    best = flat.argmax(0)
    rows, cols = best // hw, best % hw
    out = np.zeros((n, 3), np.float32)
    off = (blazepose.HEATMAP_KERNEL - 1) // 2
    for i in range(n):
        r0, r1 = max(0, rows[i] - off), min(hh, rows[i] + off + 1)
        c0, c1 = max(0, cols[i] - off), min(hw, cols[i] + off + 1)
        w = conf[r0:r1, c0:c1, i]
        total = w.sum()
        rr, cc = np.mgrid[r0:r1, c0:c1]
        if total > 0:
            out[i] = ((cc * w).sum() / hw / total, (rr * w).sum() / hh / total, flat[best[i], i])
    return out


class Engine:
    """One person followed frame after frame, like pose_npu.cpp: the detector (with its local contrast) when nobody is
    followed, the landmark model on the crop from the last frame's alignment points, the upright check on new tracks,
    MediaPipe's smoothing. Subclasses change `decode` (how the landmark positions come out of the model's answers) and
    `smooth`."""

    # the detector's input: 'clahe' = bilinear letterbox + local contrast (the box until 2026-11-10), 'plain' =
    # bilinear letterbox (MediaPipe), 'mean' = the picture shrunk by area averaging, the last DET_FRAMES frames averaged
    # (those no older than DET_WINDOW seconds), brightened toward DET_TARGET when darker (gamma)
    DET_FRAMES, DET_WINDOW, DET_TARGET, DET_GAMMA_MIN = 4, 0.15, 0.45, 0.5
    DET_ALTERNATE = 1
    DET_BLOCK = 1
    # a pose score under min_presence: the track is kept this many landmark runs in a row before it ends (0 = MediaPipe:
    # it ends at once); those frames carry no pose
    COAST_FRAMES = 0

    def __init__(self, run, boost=True, upright_check=True, min_presence=MIN_PRESENCE, det='clahe'):
        self.run = run
        self.boost, self.upright_check, self.min_presence = boost, upright_check, min_presence
        self.det = det if boost else 'plain'
        self.ring = []              # (seconds, the area-averaged letterboxed picture as floats)
        self.looks = 0
        self.roi = None
        self.frames = 0
        self.low = 0
        self.track = 0
        self.next_track = 1
        self.reset_filters()

    def reset_filters(self):
        self.f_lm = [OneEuro(0.05, 80.0, 1.0) for _ in range(POSE * 3)]
        self.f_world = [OneEuro(0.1, 40.0, 1.0) for _ in range(POSE * 3)]
        self.f_vis = [LowPass(0.1) for _ in range(POSE)]
        self.f_aux = [RelativeVelocity(5, 10.0) for _ in range(4)]

    def lose(self):
        self.roi = None
        self.reset_filters()

    # -- the detector
    def watch(self, image, seconds):
        """A frame for the detector's running average ('mean'): the picture shrunk by area averaging."""
        S = blazepose.DET_SIZE
        h, w = image.shape[:2]
        s = S / max(w, h)
        px, py = (S - w * s) / 2, (S - h * s) / 2
        if self.DET_BLOCK:
            out, rect = block_shrink(image, S, s, px, py)
        else:
            small = cv2.resize(image, (int(round(w * s)), int(round(h * s))), interpolation=cv2.INTER_AREA)
            out = np.zeros((S, S, 3), np.float32)
            y0, x0 = int(round(py)), int(round(px))
            out[y0:y0 + small.shape[0], x0:x0 + small.shape[1]] = small
            rect = (x0, y0, x0 + small.shape[1], y0 + small.shape[0])
        self.ring = [r for r in self.ring if r[0] != seconds][-(self.DET_FRAMES - 1):] + [(seconds, out)]
        return (s, px, py), rect

    def detector_input(self, image, seconds=0.0):
        if self.det == 'mean':
            pad, (x0, y0, x1, y1) = self.watch(image, seconds)
            # the looks take turns: the newest frame alone (someone walking in), then the last few averaged (a dark
            # room's grain)
            self.looks += 1
            recent = [r[1] for r in self.ring if seconds - r[0] <= self.DET_WINDOW]
            if self.DET_ALTERNATE and self.looks % 2 == 1:
                recent = recent[-1:]
            inp = np.clip(np.mean(recent, axis=0), 0, 255).astype(np.uint8)
            mean = inp[y0:y1, x0:x1].mean() / 255.0
            if 1e-3 < mean < self.DET_TARGET:
                g = max(self.DET_GAMMA_MIN, math.log(self.DET_TARGET) / math.log(mean))
                inp = cv2.LUT(inp, np.array([255 * (i / 255) ** g for i in range(256)], np.uint8))
            return inp, pad
        inp, pad = blazepose.letterbox(image)
        if self.det == 'clahe':
            s, px, py = pad
            h, w = image.shape[:2]
            x0, x1 = math.ceil(px), min(blazepose.DET_SIZE, math.floor(px + w * s))
            y0, y1 = math.ceil(py), min(blazepose.DET_SIZE, math.floor(py + h * s))
            inp = local_contrast(inp, x0, y0, x1, y1)
        return inp, pad

    def detect(self, image, seconds=0.0):
        inp, pad = self.detector_input(image, seconds)
        out = blazepose.by_name(self.run('detector', inp))
        found = blazepose.decode_detection(out['boxes'], out['scores'], image.shape[1::-1], pad)
        if found is None:
            return None
        return blazepose.roi_from_points(found[1][0], found[1][1])

    # -- the landmark model's answers into positions (crop coordinates 0..1), overridable
    def decode(self, raw, conf, roi, t_ns):
        """raw: (39, 5) regression output; conf: (64, 64, 39) heatmap scores. Returns xy (39, 2) in 0..1 of the crop."""
        xy = raw[:, :2] / blazepose.LM_SIZE
        return refine(xy, conf)

    def smooth(self, t_ns, pic, z, vis, world, aux, width, height):
        """MediaPipe's pose filtering (pose_npu.cpp Follow). Returns the smoothed (xy, z, vis, world, aux)."""
        box = 2.0 * math.hypot(aux[1, 0] - aux[0, 0], aux[1, 1] - aux[0, 1])
        scale = 1.0 / box if box > 1e-6 else 0.0
        f_xy, f_z, f_vis, f_world = pic.copy(), z.copy(), vis.copy(), world.copy()
        for i in range(POSE):
            f_vis[i] = self.f_vis[i].apply(float(vis[i]))
            if scale > 0:
                f_xy[i, 0] = self.f_lm[i * 3].apply(t_ns, scale, float(pic[i, 0]))
                f_xy[i, 1] = self.f_lm[i * 3 + 1].apply(t_ns, scale, float(pic[i, 1]))
                f_z[i] = self.f_lm[i * 3 + 2].apply(t_ns, scale, float(z[i]))
            for k in range(3):
                f_world[i, k] = self.f_world[i * 3 + k].apply(t_ns, 1.0, float(world[i, k]))
        f_aux = aux.copy()
        if scale > 0:
            for j in range(2):
                for k in range(2):
                    f_aux[j, k] = self.f_aux[j * 2 + k].apply(t_ns, scale, float(aux[j, k]))
        return f_xy, f_z, f_vis, f_world, f_aux

    # experiments on the landmark crop: CROP_AREA = the picture shrunk by area averaging first (anti-aliased),
    # CROP_GAMMA = brightened toward this mean when darker (0 = off), CROP_GAMMA_MIN = the strongest gamma
    CROP_AREA, CROP_GAMMA, CROP_GAMMA_MIN = 0, 0.0, 0.5

    def crop(self, image, roi):
        if self.CROP_AREA and roi[2] / blazepose.LM_SIZE > 1.05:
            scale = roi[2] / blazepose.LM_SIZE
            small = cv2.resize(image, None, fx=1 / scale, fy=1 / scale, interpolation=cv2.INTER_AREA)
            sx, sy = small.shape[1] / image.shape[1], small.shape[0] / image.shape[0]
            out = blazepose.crop(small, (roi[0] * sx, roi[1] * sy, roi[2] * sx, roi[3]))
        else:
            out = blazepose.crop(image, roi)
        if self.CROP_GAMMA > 0:
            mean = out.mean() / 255.0
            if 1e-3 < mean < self.CROP_GAMMA:
                g = max(self.CROP_GAMMA_MIN, math.log(self.CROP_GAMMA) / math.log(mean))
                out = cv2.LUT(out, np.array([255 * (i / 255) ** g for i in range(256)], np.uint8))
        return out

    def landmarks_at(self, image, roi, mirror=False):
        """The landmark model on the crop roi: raw (39, 5) regression output, conf (64, 64, 39) heatmap scores, the
        pose score, world (39, 3) as the model gives it (not yet turned by the crop's rotation). mirror: the crop
        flipped left to right before the model, its answers flipped back (left and right landmarks exchanged)."""
        crop = self.crop(image, roi)
        if mirror:
            crop = crop[:, ::-1].copy()
        out = blazepose.by_name(self.run('landmarks', crop))
        raw = out['landmarks'].reshape(N, 5).astype(np.float32)
        heat = out['heatmap'].reshape(out['heatmap'].shape[-3:])
        if heat.shape[0] == N:
            heat = heat.transpose(1, 2, 0)
        conf = blazepose.sigmoid(np.clip(heat.astype(np.float32), -30, 30))
        score = float(np.asarray(out['flag']).reshape(-1)[0])
        world = out['world'].reshape(N, 3).astype(np.float32).copy()
        if not mirror:      # for --dump: the model's answers as they came
            self.last_logits = heat.astype(np.float32)
            self.last_world_raw = world.copy()
        if mirror:
            raw = raw[SWAP].copy()
            raw[:, 0] = blazepose.LM_SIZE - 1 - raw[:, 0]
            conf = conf[:, ::-1, :][:, :, SWAP].copy()
            world = world[SWAP].copy()
            world[:, 0] = -world[:, 0]
        return raw, conf, score, world

    def process(self, image, t_ns):
        h, w = image.shape[:2]
        res = dict(found=False, detected=False, rejected=False, lost=False)
        roi = self.roi
        if roi is None:
            res['detected'] = True
            roi = self.detect(image, t_ns * 1e-9)
            if roi is None:
                return res
            self.frames = 0
            self.low = 0
            self.track = self.next_track
            self.next_track += 1
            self.reset_filters()
        raw, conf, score, world_raw = self.landmarks_at(image, roi)
        res.update(roi=np.array(roi, np.float32), raw=raw, conf=conf, score=score, track=self.track)
        if score < self.min_presence:
            # a new track (the upright check's frames) ends at once; one that has been followed is kept a few frames
            if self.frames >= UPRIGHT_FRAMES and self.low < self.COAST_FRAMES:
                self.low += 1
                res['coasting'] = True
                return res
            res['lost'] = True
            self.lose()
            return res
        self.low = 0
        xy = self.decode(raw, conf, roi, t_ns)
        m = blazepose.crop_matrix(roi)
        pic = (xy * blazepose.LM_SIZE) @ m[:, :2].T + m[:, 2]
        if self.upright_check and self.frames < UPRIGHT_FRAMES and not upright(pic):
            res['rejected'] = True
            self.lose()
            return res
        z = raw[:, 2] / blazepose.LM_SIZE * roi[2]
        vis, pres = blazepose.sigmoid(raw[:, 3]), blazepose.sigmoid(raw[:, 4])
        world = world_raw.copy()
        c, s = math.cos(roi[3]), math.sin(roi[3])
        world[:, :2] = world[:, :2] @ np.array([[c, s], [-s, c]], np.float32)
        aux = pic[33:35].astype(np.float64)
        f_xy, f_z, f_vis, f_world, f_aux = self.smooth(t_ns, pic[:POSE].astype(np.float64), z[:POSE].astype(np.float64),
                                                       vis[:POSE].astype(np.float64), world[:POSE].astype(np.float64),
                                                       aux, w, h)
        self.roi = blazepose.roi_from_points(f_aux[0], f_aux[1])
        self.frames += 1
        res.update(found=True, crop_xy=xy, xy=pic, z=z, vis=vis, pres=pres, world=world, f_xy=f_xy, f_z=f_z,
                   f_vis=f_vis, f_world=f_world)
        return res


def refine(xy, conf):
    """RefineLandmarksFromHeatmap on sigmoid scores (blazepose.refine takes logits)."""
    hh, hw = conf.shape[:2]
    out = xy.copy()
    off = (blazepose.HEATMAP_KERNEL - 1) // 2
    for i in range(len(xy)):
        col, row = int(xy[i, 0] * hw), int(xy[i, 1] * hh)
        if col < 0 or col >= hw or row < 0 or row >= hh:
            continue
        r0, r1, c0, c1 = max(0, row - off), min(hh, row + off + 1), max(0, col - off), min(hw, col + off + 1)
        w = conf[r0:r1, c0:c1, i]
        total = w.sum()
        if w.max() >= 0.5 and total > 0:
            rows, cols = np.mgrid[r0:r1, c0:c1]
            out[i, 0] = (cols * w).sum() / hw / total
            out[i, 1] = (rows * w).sum() / hh / total
    return out


# ------------------------------------------------------------------------------------------------------ run a clip
def dump_frame(f, t_ns, r, engine):
    """One frame for trust_check.cpp: the landmark model's answers and what the lab made of them (little endian:
    int64 time, int32 flags (1 found, 2 a landmark run, 4 sides put back, 8 coasting, 16 the detector ran), float32 score, crop box, raw 39 x 5, world
    39 x 3, heatmap logits 64 x 64 x 39, then the expected crop positions 39 x 2, under 33, rescued 33, landmarks 33 x 2
    (pixels), visibility 33, world 33 x 3, trust 33)."""
    ran = 'raw' in r
    extra = r.get('extra', {})
    flags = ((1 if r['found'] else 0) | (2 if ran else 0) | (4 if float(extra.get('swapped', 0)) > 0 else 0)
             | (8 if r.get('coasting') else 0) | (16 if r['detected'] else 0))
    f.write(np.int64(t_ns).tobytes())
    f.write(np.int32(flags).tobytes())
    f.write(np.float32(r.get('score', 0.0)).tobytes())

    def put(value, shape):
        a = np.zeros(shape, np.float32) if value is None else np.asarray(value, np.float32).reshape(shape)
        f.write(a.tobytes())
    put(r.get('roi'), 4)
    put(r.get('raw'), (N, 5))
    put(engine.last_world_raw if ran else None, (N, 3))
    put(engine.last_logits if ran else None, (64, 64, N))
    found = r['found']
    put(r['crop_xy'] if found else None, (N, 2))
    put(extra.get('under') if found else None, POSE)
    put(extra.get('rescued') if found else None, POSE)
    put(r['f_xy'] if found else None, (POSE, 2))
    put(r['f_vis'] if found else None, POSE)
    put(r['f_world'] if found else None, (POSE, 3))
    put(extra.get('w') if found else None, POSE)


def run_clip(clip_path, engine, out_path, keep_heat=False, first=0, last=None, progress=True, dump=None):
    clip = Clip(clip_path)
    last = len(clip) if last is None else min(last, len(clip))
    n = last - first
    rec = dict(
        t=clip.times[first:last].astype(np.float64), found=np.zeros(n, bool), detected=np.zeros(n, bool),
        rejected=np.zeros(n, bool), lost=np.zeros(n, bool), track=np.zeros(n, np.int32),
        score=np.full(n, np.nan, np.float32), roi=np.full((n, 4), np.nan, np.float32),
        raw=np.full((n, N, 5), np.nan, np.float32), peak=np.full((n, N, 3), np.nan, np.float32),
        reg_conf=np.full((n, N), np.nan, np.float32),
        xy=np.full((n, N, 2), np.nan, np.float32), world=np.full((n, POSE, 3), np.nan, np.float32),
        vis=np.full((n, POSE), np.nan, np.float32), pres=np.full((n, POSE), np.nan, np.float32),
        f_xy=np.full((n, POSE, 2), np.nan, np.float32), f_world=np.full((n, POSE, 3), np.nan, np.float32),
        f_vis=np.full((n, POSE), np.nan, np.float32), size=np.zeros(2, np.int32))
    heat = np.zeros((n, POSE, 64, 64), np.uint8) if keep_heat else None
    t0 = time.time()
    dump_file = None
    for k in range(n):
        image = clip.rgb(first + k)
        rec['size'][:] = image.shape[1], image.shape[0]
        t_ns = int(round(clip.times[first + k] * 1e9)) + 1_000_000_000
        r = engine.process(image, t_ns)
        if dump:
            if dump_file is None:
                dump_file = open(dump, 'wb')
                dump_file.write(b'TRUSTCHK' + np.array([n, image.shape[1], image.shape[0]], np.int32).tobytes())
            dump_frame(dump_file, t_ns, r, engine)
        rec['found'][k], rec['detected'][k] = r['found'], r['detected']
        rec['rejected'][k], rec['lost'][k] = r['rejected'], r['lost']
        if 'raw' in r:
            rec['score'][k] = r['score']
            rec['roi'][k] = r['roi']
            rec['raw'][k] = r['raw']
            rec['track'][k] = r['track']
            conf = r['conf']
            rec['peak'][k] = heat_peaks(conf)
            cols = np.clip((r['raw'][:, 0] / blazepose.LM_SIZE * 64).astype(int), 0, 63)
            rows = np.clip((r['raw'][:, 1] / blazepose.LM_SIZE * 64).astype(int), 0, 63)
            rec['reg_conf'][k] = conf[rows, cols, np.arange(N)]
            if keep_heat:
                heat[k] = np.round(conf[:, :, :POSE].transpose(2, 0, 1) * 255).astype(np.uint8)
        if r['found']:
            for key in ('xy', 'world', 'vis', 'pres', 'f_xy', 'f_world', 'f_vis'):
                rec[key][k] = r[key][:rec[key].shape[1]]
        for key, value in r.get('extra', {}).items():
            value = np.asarray(value, np.float32)
            if key not in rec:
                rec[key] = np.full((n,) + value.shape, np.nan, np.float32)
            rec[key][k] = value
        if progress and (k + 1) % 500 == 0:
            print(f'  {k + 1}/{n} frames, {1000 * (time.time() - t0) / (k + 1):.1f} ms a frame', flush=True)
    if dump_file is not None:
        dump_file.close()
    if keep_heat:
        rec['heat'] = heat
    np.savez_compressed(out_path, **rec)
    found = rec['found']
    print(f'{os.path.basename(out_path)}: {n} frames, found in {found.sum()} ({100 * found.mean():.1f} %), '
          f'{rec["detected"].sum()} detector runs, {rec["lost"].sum()} losses, {rec["rejected"].sum()} rejected, '
          f'{1000 * (time.time() - t0) / n:.1f} ms a frame', flush=True)
    return rec


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest='cmd', required=True)
    r = sub.add_parser('run')
    r.add_argument('clip')
    r.add_argument('--model', default='lite')
    r.add_argument('--models', default='/mnt/d/rknn/work')
    r.add_argument('--out', required=True)
    r.add_argument('--variant', default='trust')
    r.add_argument('--heat', action='store_true', help='keep the whole heatmaps (big)')
    r.add_argument('--first', type=int, default=0)
    r.add_argument('--last', type=int)
    r.add_argument('--threads', type=int, default=4)
    r.add_argument('--no-boost', action='store_true')
    r.add_argument('--det', help="the detector's input: mean (averaged and brightened: the box, the default for trust), "
                                 "clahe (the box of 2026-11-09, the default otherwise), plain (MediaPipe)")
    r.add_argument('--tag', default='', help='added to the output name')
    r.add_argument('--dump', help="the frames' model answers and results for tools/npu/trust_check.cpp (640 KB a frame)")
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    run = litert_runner(os.path.join(args.models, args.model), args.threads)
    import tracklab_variants
    det = args.det or ('mean' if args.variant.startswith('trust') else 'clahe')
    engine = tracklab_variants.make(args.variant, run, boost=not args.no_boost, det=det)
    stem = os.path.splitext(os.path.basename(args.clip))[0]
    name = f'{stem}.{args.model}.{args.variant}{args.tag}.npz'.replace(':', '@')    # Windows can't open a ':' name
    run_clip(args.clip, engine, os.path.join(args.out, name), keep_heat=args.heat, first=args.first, last=args.last,
             dump=args.dump)


if __name__ == '__main__':
    main()
