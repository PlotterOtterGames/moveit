"""A raw MJPEG clip from the box camera: its frames and their capture times.

Record one on the box with the app's camera closed (another activity in front, e.g. Android's settings), as root:

    su 0 v4l2-ctl -d /dev/video9 --set-fmt-video=width=1024,height=768,pixelformat=MJPG --set-parm=30 \
        --stream-mmap=4 --stream-count=1800 --stream-to=/data/local/tmp/clip/<name>.mjpg --verbose \
        > /data/local/tmp/clip/<name>.log 2>&1

(tools/motion/box_clip.sh does that and pulls both files). The .mjpg is the camera's own JPEG frames back to back;
the .log has a "ts: <seconds>" line per frame (the kernel's capture time), which `frames()` pairs with them in order.
Without a log the frames are spaced 1/fps apart.

    python tools/motion/mjpeg_clip.py <clip.mjpg> [--save <dir>] [--every N]    # count, timing, save some as JPEG
"""
import argparse
import re
from pathlib import Path

import numpy as np

SOI, EOI = b"\xff\xd8", b"\xff\xd9"


def jpegs(data):
    """The JPEG frames of an MJPEG stream (each from its start-of-image to the next frame's)."""
    starts = [m.start() for m in re.finditer(re.escape(SOI + b"\xff"), data)]
    for a, b in zip(starts, starts[1:] + [len(data)]):
        yield data[a:b]


def capture_times(log_path):
    """Seconds, one per frame, from v4l2-ctl --verbose ("ts: 1922.690083")."""
    times = []
    for line in Path(log_path).read_text(errors="replace").splitlines():
        m = re.search(r"\bts:\s*([0-9]+\.[0-9]+)", line)
        if m:
            times.append(float(m.group(1)))
    return times


def frames(clip_path, fps=30.0):
    """(seconds from the first frame, JPEG bytes) for every frame of a clip, with the log's capture times when there
    is a <clip>.log next to it."""
    data = Path(clip_path).read_bytes()
    shots = list(jpegs(data))
    log = Path(clip_path).with_suffix(".log")
    times = capture_times(log) if log.exists() else []
    if len(times) < len(shots):
        times = [i / fps for i in range(len(shots))]
    t0 = times[0]
    return [(t - t0, shot) for t, shot in zip(times, shots)]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("clip")
    ap.add_argument("--save", type=Path, help="write every Nth frame here as a .jpg")
    ap.add_argument("--every", type=int, default=30)
    args = ap.parse_args()
    shots = frames(args.clip)
    t = np.array([s[0] for s in shots])
    dt = np.diff(t) * 1000 if len(t) > 1 else np.zeros(1)
    print(f"{len(shots)} frames over {t[-1]:.2f} s: {len(shots) / max(t[-1], 1e-9):.2f} fps, "
          f"interval median {np.median(dt):.1f} ms, min {dt.min():.1f}, max {dt.max():.1f}, "
          f"{np.sum(dt > 50)} gaps > 50 ms")
    if args.save:
        args.save.mkdir(parents=True, exist_ok=True)
        for i in range(0, len(shots), args.every):
            (args.save / f"frame{i:05d}.jpg").write_bytes(shots[i][1])


if __name__ == "__main__":
    main()
