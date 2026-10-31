"""MediaPipe's pose landmarker (BlazePose) written out as plain steps around its two models, so the box can run the
models on its NPU (Rockchip RKNN) instead of through MediaPipe. This is the reference for the C++ port in the camera
service (MoveItMotion.androidlib/src/main/cpp/pose_npu.cpp): the same anchors, decoding, regions, crops and projections
as MediaPipe's graphs (pose_detection, pose_landmark_by_roi, pose_landmarks_to_roi), checked against MediaPipe itself
with compare.py.

The models come out of MediaPipe's .task files (a zip): pose_detector.tflite (224 x 224, the person and two alignment
points) and pose_landmarks_detector.tflite (256 x 256 crop, 39 landmarks, world landmarks, presence, heatmap).
Inference is pluggable: run(name, uint8 RGB HWC image) -> list of float32 output arrays in the model's order.
"""
import math

import cv2
import numpy as np

DET_SIZE = 224
LM_SIZE = 256
NUM_LANDMARKS = 39          # 33 pose landmarks + 2 alignment points (33 hips, 34 size/rotation) + 4 unused
HEATMAP_KERNEL = 7
DET_MIN_SCORE = 0.5
NMS_IOU = 0.3
ROI_SCALE = 1.25


def sigmoid(x):
    return 1.0 / (1.0 + np.exp(-x))


def ssd_anchors():
    """SsdAnchorsCalculator with the pose detector's options: 5 layers, strides 8 16 32 32 32, aspect ratio 1 plus the
    interpolated scale (2 anchors per layer), fixed anchor size. Layers with the same stride share a grid."""
    strides = [8, 16, 32, 32, 32]
    anchors = []
    layer = 0
    while layer < len(strides):
        per_cell = 0
        last = layer
        while last < len(strides) and strides[last] == strides[layer]:
            per_cell += 2
            last += 1
        cells = math.ceil(DET_SIZE / strides[layer])
        for y in range(cells):
            for x in range(cells):
                for _ in range(per_cell):
                    anchors.append(((x + 0.5) / cells, (y + 0.5) / cells))
        layer = last
    return np.array(anchors, np.float32)


ANCHORS = ssd_anchors()


def warp(image, m, size, border):
    """Bilinear warp; m takes an output pixel (u, v) to a picture position. Like MediaPipe's ImageToTensor (which maps
    the ROI's corners onto the output's corners with OpenCV), the coordinates are used as they are, with no half-pixel
    shifts: the models learned on crops made that way (half-pixel shifts put us ~2 px off MediaPipe's landmarks)."""
    return cv2.warpAffine(image, np.array(m, np.float32), (size, size), flags=cv2.INTER_LINEAR | cv2.WARP_INVERSE_MAP,
                          borderMode=border, borderValue=0)


def letterbox(image):
    """The detector's input: the whole picture fitted into 224 x 224, black bars (ImageToTensor, BORDER_ZERO).
    Returns the input and (scale, pad_x, pad_y) in input pixels."""
    h, w = image.shape[:2]
    s = DET_SIZE / max(w, h)
    px, py = (DET_SIZE - w * s) / 2, (DET_SIZE - h * s) / 2
    out = warp(image, [[1 / s, 0, -px / s], [0, 1 / s, -py / s]], DET_SIZE, cv2.BORDER_CONSTANT)
    return out, (s, px, py)


def decode_detection(boxes, scores, image_size, pad):
    """TensorsToDetections (sigmoid scores, x before y, scale 224) + weighted NMS keeping the best cluster, then the
    letterbox removed. Returns (score, keypoints[4, 2] in picture pixels) or None."""
    boxes = boxes.reshape(-1, 12)
    scores = sigmoid(np.clip(scores.reshape(-1), -100, 100))
    keep = np.nonzero(scores >= DET_MIN_SCORE)[0]
    if len(keep) == 0:
        return None
    b = boxes[keep] / DET_SIZE
    a = ANCHORS[keep]
    cx, cy = b[:, 0] + a[:, 0], b[:, 1] + a[:, 1]
    half_w, half_h = b[:, 2] / 2, b[:, 3] / 2
    rects = np.stack([cx - half_w, cy - half_h, cx + half_w, cy + half_h], 1)
    kps = b[:, 4:12].reshape(-1, 4, 2) + a[:, None, :]
    s = scores[keep]
    best = int(np.argmax(s))
    # weighted NMS: average every detection overlapping the best one by IoU > 0.3, weighted by score
    r0 = rects[best]
    ix = np.clip(np.minimum(rects[:, 2], r0[2]) - np.maximum(rects[:, 0], r0[0]), 0, None)
    iy = np.clip(np.minimum(rects[:, 3], r0[3]) - np.maximum(rects[:, 1], r0[1]), 0, None)
    inter = ix * iy
    area = (rects[:, 2] - rects[:, 0]) * (rects[:, 3] - rects[:, 1])
    iou = inter / np.maximum(area + area[best] - inter, 1e-9)
    group = iou > NMS_IOU
    weights = s[group] / s[group].sum()
    kp = (kps[group] * weights[:, None, None]).sum(0)
    scale, px, py = pad
    kp = (kp * DET_SIZE - np.array([px, py])) / scale          # back into picture pixels
    return float(s[best]), kp


def roi_from_points(center, scale_point):
    """AlignmentPointsRects (rotation so the body points up, size = twice the distance) + RectTransformation (x 1.25,
    square). Returns (cx, cy, size, rotation) in picture pixels / radians."""
    dx, dy = scale_point[0] - center[0], scale_point[1] - center[1]
    size = 2.0 * math.hypot(dx, dy) * ROI_SCALE
    rotation = math.pi / 2 - math.atan2(-dy, dx)
    rotation = rotation - 2 * math.pi * math.floor((rotation + math.pi) / (2 * math.pi))
    return float(center[0]), float(center[1]), size, rotation


def crop_matrix(roi):
    """2 x 3 matrix taking a point of the 256 x 256 crop to the picture: the rotated square ROI (the crop, and the
    landmarks' way back)."""
    cx, cy, size, r = roi
    c, s = math.cos(r) * size / LM_SIZE, math.sin(r) * size / LM_SIZE
    h = LM_SIZE / 2
    return np.array([[c, -s, cx - c * h + s * h], [s, c, cy - s * h - c * h]], np.float32)


def crop(image, roi):
    """The landmark model's input: the rotated square ROI, edge pixels repeated outside the picture."""
    return warp(image, crop_matrix(roi), LM_SIZE, cv2.BORDER_REPLICATE)


def refine(xy, heatmap):
    """RefineLandmarksFromHeatmap: the score-weighted centre of a 7 x 7 window of the heatmap around each landmark."""
    hh, hw = heatmap.shape[:2]
    out = xy.copy()
    off = (HEATMAP_KERNEL - 1) // 2
    for i in range(len(xy)):
        col, row = int(xy[i, 0] * hw), int(xy[i, 1] * hh)
        if col < 0 or col >= hw or row < 0 or row >= hh:
            continue
        r0, r1, c0, c1 = max(0, row - off), min(hh, row + off + 1), max(0, col - off), min(hw, col + off + 1)
        conf = sigmoid(heatmap[r0:r1, c0:c1, i])
        total = conf.sum()
        if conf.max() >= 0.5 and total > 0:
            rows, cols = np.mgrid[r0:r1, c0:c1]
            out[i, 0] = (cols * conf).sum() / hw / total
            out[i, 1] = (rows * conf).sum() / hh / total
    return out


def decode_landmarks(outputs, roi):
    """Landmarks (crop pixels / 256, sigmoid visibility and presence, heatmap refinement) projected into the picture,
    world landmarks (metres, hips at 0) turned by the ROI's rotation, the pose presence score."""
    raw = outputs['landmarks'].reshape(NUM_LANDMARKS, 5)
    xy = raw[:, :2] / LM_SIZE
    heat = outputs.get('heatmap')
    if heat is not None:
        heat = heat.reshape(heat.shape[-3:])
        if heat.shape[0] == NUM_LANDMARKS:     # channels first (the NPU build, convert.channels_first)
            heat = heat.transpose(1, 2, 0)
        xy = refine(xy, heat)
    m = crop_matrix(roi)
    pic = (xy * LM_SIZE) @ m[:, :2].T + m[:, 2]
    z = raw[:, 2] / LM_SIZE * roi[2]                          # same scale as x, in picture pixels
    vis, pres = sigmoid(raw[:, 3]), sigmoid(raw[:, 4])
    world = outputs['world'].reshape(NUM_LANDMARKS, 3).copy()
    c, s = math.cos(roi[3]), math.sin(roi[3])
    world[:, :2] = world[:, :2] @ np.array([[c, s], [-s, c]], np.float32)
    flag = float(np.asarray(outputs['flag']).reshape(-1)[0])
    return dict(xy=pic, z=z, visibility=vis, presence=pres, world=world, score=flag)


OUTPUT_SIZES = {2254 * 12: 'boxes', 2254: 'scores', NUM_LANDMARKS * 5: 'landmarks', 1: 'flag',
                NUM_LANDMARKS * 3: 'world', 64 * 64 * NUM_LANDMARKS: 'heatmap', LM_SIZE * LM_SIZE: 'segmentation'}


def by_name(outputs):
    """The models' outputs told apart by their sizes (TFLite and RKNN may list them in different orders)."""
    return {OUTPUT_SIZES[o.size]: o for o in outputs if o.size in OUTPUT_SIZES}


class Tracker:
    """One person, frame after frame: the detector only when nobody is tracked; otherwise the next crop comes from the
    alignment points of the last landmarks (pose_landmarks_to_roi)."""

    def __init__(self, run, min_presence=0.5):
        self.run = run
        self.min_presence = min_presence
        self.roi = None

    def detect(self, image):
        inp, pad = letterbox(image)
        out = by_name(self.run('detector', inp))
        found = decode_detection(out['boxes'], out['scores'], image.shape[1::-1], pad)
        if found is None:
            return None
        return roi_from_points(found[1][0], found[1][1])

    def __call__(self, image):
        roi = self.roi or self.detect(image)
        if roi is None:
            return None
        pose = decode_landmarks(by_name(self.run('landmarks', crop(image, roi))), roi)
        if pose['score'] < self.min_presence:
            self.roi = None
            return None
        self.roi = roi_from_points(pose['xy'][33], pose['xy'][34])
        pose['roi'] = roi
        return pose
