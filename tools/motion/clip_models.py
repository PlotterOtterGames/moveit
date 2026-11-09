"""Run pose models on a raw camera clip (tools/motion/box_clip.py) and write one recording per model, in the format of
the box's camera-test recorder and main.py --record, so shake_report.py and wrist_jumps.py score them unchanged.

Every model sees exactly the same pictures (decoded from the camera's own JPEGs) with their capture times, through
MediaPipe's PoseLandmarker in VIDEO mode, like the box (whose NPU engine runs MediaPipe's own models and steps, within
~0.5 px of MediaPipe per frame) and like The Link (MediaPipe Holistic: the same pose model and smoothing; checked
2026-11-01, docs/hardware.md "The Link").

    motion-service-py/.venv/Scripts/python tools/motion/clip_models.py <clip.mjpg> [--models lite,full]
        [--stride N] [--scale S] [--out <dir>] [--report]

--stride N   use every Nth frame (a 60 fps clip as 30, a 120 fps clip as 60 or 30)
--scale S    shrink the pictures first (S < 1), e.g. to see what a smaller camera mode would give
--report     run shake_report.py and wrist_jumps.py on each recording afterwards
Writes <out>/<clip name>.<model>[.s<stride>].jsonl (default: next to the clip).
"""
import argparse
import subprocess
import sys
import time
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "motion-service-py"))
sys.path.insert(0, str(Path(__file__).parent))
from mjpeg_clip import frames  # noqa: E402
from pose import PoseEstimator  # noqa: E402
from protocol import build_packet, encode  # noqa: E402

EPOCH_MS = 1_800_000_000_000      # recordings carry wall-clock ms; any fixed origin works for the scorers


def run(clip, model, stride, scale, out_path):
    shots = frames(clip)[::stride]
    estimator = PoseEstimator(model)
    found = 0
    spent = 0.0
    with open(out_path, "w", encoding="utf-8") as out:
        for n, (t, jpeg) in enumerate(shots):
            img = cv2.imdecode(np.frombuffer(jpeg, np.uint8), cv2.IMREAD_COLOR)
            if scale != 1:
                img = cv2.resize(img, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA)
            rgb = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
            t_ms = round(t * 1000)
            t0 = time.perf_counter()
            people = estimator.detect(rgb, t_ms)
            spent += time.perf_counter() - t0
            found += 1 if people else 0
            packet = build_packet(f"clip-mediapipe-{model}", n, EPOCH_MS + t_ms, img.shape[1], img.shape[0],
                                  0.0, 0.0, people[:1], model=model)
            out.write(encode(packet).decode("utf-8") + "\n")
    estimator.close()
    print(f"{out_path.name}: {len(shots)} frames, a person in {found} ({100 * found / max(1, len(shots)):.0f} %), "
          f"{1000 * spent / max(1, len(shots)):.1f} ms a frame", flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("clip", type=Path)
    ap.add_argument("--models", default="lite,full")
    ap.add_argument("--stride", type=int, default=1)
    ap.add_argument("--scale", type=float, default=1.0)
    ap.add_argument("--out", type=Path)
    ap.add_argument("--report", action="store_true")
    args = ap.parse_args()
    out_dir = args.out or args.clip.parent
    out_dir.mkdir(parents=True, exist_ok=True)
    outputs = []
    for model in args.models.split(","):
        suffix = (f".s{args.stride}" if args.stride != 1 else "") + (f".x{args.scale:g}" if args.scale != 1 else "")
        path = out_dir / f"{args.clip.stem}.{model}{suffix}.jsonl"
        run(args.clip, model, args.stride, args.scale, path)
        outputs.append(path)
    if args.report:
        for path in outputs:
            for tool in ("shake_report.py", "wrist_jumps.py"):
                print(f"\n===== {tool} {path.name}", flush=True)
                subprocess.run([sys.executable, str(Path(__file__).parent / tool), str(path)])


if __name__ == "__main__":
    main()
