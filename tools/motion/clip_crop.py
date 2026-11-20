"""A box clip (tools/motion/box_clip.py) cropped to a window (x0, x1 in pixels of the camera picture), re-encoded as
MJPEG with the same capture times and a .log beside it, for pose_npu_test --clip: the routine's player, who stands in
the middle, becomes someone half in view at the picture's right edge (the detector's back-off, docs/hardware.md "The
detector's back-off for someone half in view").
    motion-service-py/.venv/Scripts/python tools/motion/clip_crop.py <clip.mjpg> <out.mjpg> <x0> <x1>
"""
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
from mjpeg_clip import frames  # noqa: E402

src, dst, x0, x1 = sys.argv[1], Path(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4])
shots = frames(src)
with open(dst, "wb") as out, open(dst.with_suffix(".log"), "w") as log:
    for i, (t, jpeg) in enumerate(shots):
        img = cv2.imdecode(np.frombuffer(jpeg, np.uint8), cv2.IMREAD_COLOR)
        ok, enc = cv2.imencode(".jpg", img[:, x0:x1], [cv2.IMWRITE_JPEG_QUALITY, 92])
        out.write(enc.tobytes())
        log.write(f"idx: {i % 4} seq: {i} bytesused: {len(enc)} ts: {1000.0 + t:.6f}\n")
print(len(shots), "frames ->", dst)
