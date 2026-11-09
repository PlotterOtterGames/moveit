"""Side-by-side video of a raw camera clip with each model's skeleton drawn on its own frame (the way The Link shows
it): one panel per recording made by clip_models.py from that clip, in the order given.

    motion-service-py/.venv/Scripts/python tools/motion/clip_video.py <clip.mjpg> <a.jsonl> [<b.jsonl> ...]
        --out <video.mp4> [--from S] [--to S] [--height 540] [--slow N] [--crop x0,y0,x1,y1]

Stick figure: the 17 joints plus a neck (nose to the shoulders' middle) and a spine (to the hips' middle); joints the
model isn't sure of (visibility < 0.5) are drawn thin. --slow N writes every frame N times (slow motion). --crop keeps a
part of the picture (fractions of its width and height), e.g. 0.2,0,0.8,0.75 around a player in the middle. With ffmpeg
on the PATH the video is re-encoded as H.264 (plays everywhere; OpenCV alone writes MPEG-4 part 2).
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
from mjpeg_clip import frames  # noqa: E402

BONES = [(5, 6), (5, 7), (7, 9), (6, 8), (8, 10), (11, 12), (11, 13), (13, 15), (12, 14), (14, 16)]
LEFT, RIGHT, MIDDLE = (255, 170, 60), (60, 120, 255), (240, 240, 240)       # BGR: the player's left / right


def load(path):
    out = {}
    for line in open(path, encoding="utf-8"):
        p = json.loads(line)
        if p.get("people"):
            out[p["f"]] = np.array(p["people"][0]["k"], np.float64).reshape(17, 4)
    return out


def draw(img, k, scale):
    h, w = img.shape[:2]
    pt = lambda i: (int(k[i, 0] * w), int(k[i, 1] * h))
    thick, thin = max(2, round(4 * scale)), max(1, round(2 * scale))
    sure = k[:, 3] >= 0.5
    for a, b in BONES:
        color = LEFT if a % 2 == 1 and b % 2 == 1 else RIGHT if a % 2 == 0 and b % 2 == 0 else MIDDLE
        cv2.line(img, pt(a), pt(b), color, thick if sure[a] and sure[b] else thin, cv2.LINE_AA)
    shoulders = ((k[5, :2] + k[6, :2]) / 2 * (w, h)).astype(int)
    hips = ((k[11, :2] + k[12, :2]) / 2 * (w, h)).astype(int)
    cv2.line(img, pt(0), tuple(shoulders), MIDDLE, thick, cv2.LINE_AA)
    cv2.line(img, tuple(shoulders), tuple(hips), MIDDLE, thick, cv2.LINE_AA)
    cv2.circle(img, pt(0), round(9 * scale), MIDDLE, thick, cv2.LINE_AA)
    for i in (9, 10):
        cv2.circle(img, pt(i), round(7 * scale), LEFT if i == 9 else RIGHT, -1, cv2.LINE_AA)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("clip")
    ap.add_argument("recordings", nargs="+")
    ap.add_argument("--out", required=True)
    ap.add_argument("--from", dest="start", type=float, default=0)
    ap.add_argument("--to", dest="end", type=float, default=1e9)
    ap.add_argument("--height", type=int, default=540)
    ap.add_argument("--slow", type=int, default=1)
    ap.add_argument("--crop", default="0,0,1,1")
    args = ap.parse_args()
    cx0, cy0, cx1, cy1 = (float(v) for v in args.crop.split(","))
    shots = frames(args.clip)
    recs = [load(r) for r in args.recordings]
    labels = [Path(r).stem.split(".", 1)[1] if "." in Path(r).stem else Path(r).stem for r in args.recordings]
    writer = None
    fps = (len(shots) - 1) / max(shots[-1][0], 1e-9)
    for n, (t, jpeg) in enumerate(shots):
        if not args.start <= t <= args.end:
            continue
        img = cv2.imdecode(np.frombuffer(jpeg, np.uint8), cv2.IMREAD_COLOR)
        full_h, full_w = img.shape[:2]
        x0, y0, x1, y1 = round(cx0 * full_w), round(cy0 * full_h), round(cx1 * full_w), round(cy1 * full_h)
        scale = args.height / (y1 - y0)
        panels = []
        for rec, label in zip(recs, labels):
            panel = img.copy()
            if n in rec:
                draw(panel, rec[n], 1.0)
            panel = cv2.resize(panel[y0:y1, x0:x1], (round((x1 - x0) * scale), args.height),
                               interpolation=cv2.INTER_AREA)
            cv2.putText(panel, f"{label}  {t:5.2f} s", (12, 36), cv2.FONT_HERSHEY_SIMPLEX, 1.0, (0, 0, 0), 5,
                        cv2.LINE_AA)
            cv2.putText(panel, f"{label}  {t:5.2f} s", (12, 36), cv2.FONT_HERSHEY_SIMPLEX, 1.0, (255, 255, 255), 2,
                        cv2.LINE_AA)
            panels.append(panel)
        sheet = np.hstack(panels)
        if writer is None:
            writer = cv2.VideoWriter(args.out, cv2.VideoWriter_fourcc(*"mp4v"), fps, (sheet.shape[1], sheet.shape[0]))
        for _ in range(args.slow):
            writer.write(sheet)
    if writer:
        writer.release()
    if writer and shutil.which("ffmpeg"):
        tmp = args.out + ".h264.mp4"
        done = subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", args.out, "-c:v", "libx264", "-crf", "22",
                               "-pix_fmt", "yuv420p", "-movflags", "+faststart", tmp])
        if done.returncode == 0:
            os.replace(tmp, args.out)
    print("wrote", args.out)


if __name__ == "__main__":
    main()
