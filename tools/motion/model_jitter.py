"""How much do MediaPipe's pose landmarks shake for a person standing still: lite vs full, bright vs a dim room?

A still photo becomes a "video": every frame gets fresh sensor noise and the camera's MJPEG compression, like a USB
camera filming someone who doesn't move. The picture is scaled to the box camera's 1024 x 768 with the person ~60 % of
its height (an adult ~2.5 m from the box's camera). VIDEO mode = MediaPipe's own smoothing on (the box's NPU engine
does the same), IMAGE mode = the raw model output. Reported per joint: how far it moves from one frame to the next
(what you see as shaking) / how far it wanders from its average, both in % of the torso length (the unit the game's
filters and gestures use; on the full-screen skeleton at 1080p a torso is ~240 px at 2.5 m).

    motion-service-py/.venv/bin/python -I tools/motion/model_jitter.py <photo of a whole person> [models folder]

MediaPipe's test picture is https://storage.googleapis.com/mediapipe-assets/pose.jpg (the NPU checks used it too). In a
cloud container MediaPipe needs libEGL: apt-get install -y libegl1 libgles2.

2026-10-31 (pose.jpg, 150 frames): with MediaPipe's smoothing, lite moved 0.06-0.27 % of a torso per frame in good light
(wrist 0.27) against full's 0.01-0.07 (wrist 0.03); in a dim room with the camera's gain 0.13-0.46 (knee 0.46) against
0.06-0.20. Raw, lite 0.5-1.1 % / full 0.4-0.8 % in good light, 1.3-1.9 % / 1.0-1.4 % dim. Synthetic noise on a still
picture understates a real camera (motion blur, exposure changes, a person who breathes): the user's real recording on
2026-10-20 gave 0.5-0.9 % still and 8-15 % on fast moves with the full model. The ratios are the point: lite shakes
several times more than full, and a dark room makes both shakier.
"""

import sys
from pathlib import Path

import cv2
import mediapipe as mp
import numpy as np
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision

PHOTO = sys.argv[1]
MODELS = sys.argv[2] if len(sys.argv) > 2 else str(Path(__file__).resolve().parents[2] / "motion-service-py" / "models")
FRAMES = 150
JOINTS = {"wrist": (15, 16), "elbow": (13, 14), "shoulder": (11, 12), "hip": (23, 24), "knee": (25, 26), "ankle": (27, 28)}


def make_frame(base, rng, light, noise):
    """light < 1: a dim room brightened by the camera's gain (the noise grows with it)."""
    img = base.astype(np.float32) * light
    img += rng.normal(0, noise, img.shape)               # sensor noise (after gain)
    img = np.clip(img / light if light < 1 else img, 0, 255)   # the gain brings the brightness back, noise and all
    ok, jpg = cv2.imencode(".jpg", img.astype(np.uint8), [cv2.IMWRITE_JPEG_QUALITY, 80])
    return cv2.imdecode(jpg, cv2.IMREAD_COLOR)


def run(model, mode, light, noise, seed=1):
    base = cv2.imread(PHOTO)
    # pad to 4:3 and scale to 1024 x 768 (the box's camera picture)
    h, w = base.shape[:2]
    target_w = int(h * 4 / 3)
    pad = (target_w - w) // 2
    if pad > 0:
        base = cv2.copyMakeBorder(base, 0, 0, pad, target_w - w - pad, cv2.BORDER_REPLICATE)
    else:
        base = base[:, -pad:-pad + target_w]
    base = cv2.resize(base, (1024, 768), interpolation=cv2.INTER_AREA)
    options = vision.PoseLandmarkerOptions(
        base_options=mp_python.BaseOptions(model_asset_path=f"{MODELS}/pose_landmarker_{model}.task"),
        running_mode=vision.RunningMode.VIDEO if mode == "video" else vision.RunningMode.IMAGE,
        num_poses=1)
    lm = vision.PoseLandmarker.create_from_options(options)
    rng = np.random.default_rng(seed)
    pts, found = [], 0
    for i in range(FRAMES):
        frame = make_frame(base, rng, light, noise)
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
        res = lm.detect_for_video(image, i * 33) if mode == "video" else lm.detect(image)
        if res.pose_landmarks:
            found += 1
            p = res.pose_landmarks[0]
            pts.append([(q.x * 1024, q.y * 768, q.visibility) for q in p])
    lm.close()
    a = np.array(pts[20:])                     # skip the filters settling
    torso = np.linalg.norm((a[:, 11, :2] + a[:, 12, :2]) / 2 - (a[:, 23, :2] + a[:, 24, :2]) / 2, axis=1).mean()
    out = {}
    for name, (l, r) in JOINTS.items():
        # frame-to-frame movement of a still joint (what you see as shaking), mean of both sides
        step = np.mean([np.linalg.norm(np.diff(a[:, j, :2], axis=0), axis=1).mean() for j in (l, r)])
        sd = np.mean([np.linalg.norm(a[:, j, :2] - a[:, j, :2].mean(axis=0), axis=1).mean() for j in (l, r)])
        out[name] = (100 * step / torso, 100 * sd / torso)
    return found, torso, out


if __name__ == "__main__":
    conditions = [("bright", 1.0, 2.0), ("dim room + gain", 0.35, 6.0)]
    print(f"{FRAMES} frames each; per joint: frame-to-frame move / wander from its average, % of torso length")
    for cname, light, noise in conditions:
        for mode in ("image", "video"):
            for model in ("lite", "full"):
                found, torso, res = run(model, mode, light, noise)
                cells = "  ".join(f"{k} {v[0]:4.2f}/{v[1]:4.2f}" for k, v in res.items())
                print(f"{cname:16s} {mode:5s} {model:4s} found {found}/{FRAMES} torso {torso:5.1f}px  {cells}")
