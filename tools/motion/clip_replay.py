"""Replay a box camera clip into the game as if the box were running: the camera's own frames (the camera preview
stream, docs/protocol.md) and the poses a model found on them (the motion packets), each sent when the box would have
had it ready, with the same capture time. Vishu! and Pikturo!'s camera mode then show the real picture with the real
poses on the laptop, again and again, so fixes can be judged on what the box showed.

    motion-service-py/.venv/Scripts/python tools/motion/clip_replay.py <clip.mjpg> [--poses <recording.jsonl>]
        [--from S] [--to S] [--loop] [--picture-delay MS] [--pose-delay MS] [--speed X]

The clip comes from tools/motion/box_clip.py (raw MJPEG + a .log of capture times), the poses from
tools/motion/clip_models.py (default: <clip>.full.jsonl, the model Vishu! uses on the box). On the box a frame reaches
the game ~45 ms after its capture (the camera HAL's buffers, the copy) and its pose ~70 ms after (the NPU on top):
--picture-delay and --pose-delay. Frames go out only while the game asks for the picture (like video.py), scaled to the
width it asks for.
"""
import argparse
import heapq
import json
import sys
import time
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "motion-service-py"))
sys.path.insert(0, str(Path(__file__).parent))
from mjpeg_clip import frames  # noqa: E402
from protocol import UdpSender, encode, now_ms  # noqa: E402
from video import CONTROL_PORT, VideoStreamer  # noqa: E402


def load_poses(path):
    poses = {}
    for line in Path(path).read_text(encoding="utf-8").splitlines():
        if line.strip():
            p = json.loads(line)
            poses[p["f"]] = p
    return poses


def main():
    ap = argparse.ArgumentParser(description="Replay a box camera clip (picture + poses) into the game")
    ap.add_argument("clip", type=Path)
    ap.add_argument("--poses", type=Path, help="pose recording of the clip (default <clip>.full.jsonl)")
    ap.add_argument("--from", dest="start", type=float, default=0.0, help="start at this second of the clip")
    ap.add_argument("--to", dest="end", type=float, default=1e9, help="stop at this second of the clip")
    ap.add_argument("--loop", action="store_true")
    ap.add_argument("--speed", type=float, default=1.0)
    ap.add_argument("--picture-delay", type=float, default=45.0, help="ms from capture to the game having the frame")
    ap.add_argument("--pose-delay", type=float, default=70.0, help="ms from capture to the game having the pose")
    ap.add_argument("--port", type=int, default=5005)
    ap.add_argument("--video-control-port", type=int, default=CONTROL_PORT)
    ap.add_argument("--wait", type=float, default=0.0, help="seconds of nobody (no packets) before the clip starts")
    args = ap.parse_args()

    poses_path = args.poses or args.clip.with_suffix(".full.jsonl")
    poses = load_poses(poses_path)
    shots = [(i, t, jpg) for i, (t, jpg) in enumerate(frames(args.clip)) if args.start <= t <= args.end]
    if not shots:
        raise SystemExit("no frames in that range")
    print(f"{args.clip.name}: {len(shots)} frames ({shots[0][1]:.1f}-{shots[-1][1]:.1f} s), poses from {poses_path.name}, "
          f"picture +{args.picture_delay:.0f} ms, pose +{args.pose_delay:.0f} ms", flush=True)

    sender = UdpSender("127.0.0.1", args.port)
    video = VideoStreamer(args.video_control_port) if args.video_control_port else None
    sent = 0
    frame_no = 0
    try:
        if args.wait > 0:
            until = time.perf_counter() + args.wait
            while time.perf_counter() < until:
                if video:
                    video.wanted()
                time.sleep(0.02)
        while True:
            start = time.perf_counter()
            wall0 = now_ms()
            t0 = shots[0][1]
            # (due seconds after start, order, kind, payload)
            events = []
            for n, (i, t, jpg) in enumerate(shots):
                capture = (t - t0) / args.speed
                heapq.heappush(events, (capture + args.picture_delay / 1000, 2 * n, "picture", (i, t, jpg)))
                heapq.heappush(events, (capture + args.pose_delay / 1000, 2 * n + 1, "pose", (i, t, jpg)))
            while events:
                due, _, kind, (i, t, jpg) = heapq.heappop(events)
                wait = due - (time.perf_counter() - start)
                if wait > 0:
                    time.sleep(wait)
                capture_ms = wall0 + round((t - t0) / args.speed * 1000)
                if kind == "picture":
                    if video and video.wanted():
                        img = cv2.imdecode(np.frombuffer(jpg, np.uint8), cv2.IMREAD_COLOR)
                        video.send(img, capture_ms, i)
                else:
                    packet = poses.get(i)
                    if packet is None:
                        continue
                    packet = dict(packet, src="clip-replay", t=capture_ms, f=frame_no)
                    frame_no += 1
                    sender.send_raw(encode(packet))
                    sent += 1
            if not args.loop:
                break
    except KeyboardInterrupt:
        pass
    finally:
        sender.close()
        if video:
            video.close()
    print(f"sent {sent} poses, {video.frames if video else 0} pictures", flush=True)


if __name__ == "__main__":
    main()
