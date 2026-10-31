"""MoveIt motion service (PC dev version).

Camera -> MediaPipe Pose -> COCO-17 skeleton -> UDP (Motion Protocol v1, see docs/protocol.md).

Examples:
    python main.py --preview                      # laptop camera, preview window
    python main.py --camera 1 --model full        # external USB camera, more accurate model
    python main.py --record sessions/jump.jsonl   # also save every packet for replay.py

While the game asks for it (Pikturo's camera mode), small JPEG camera frames also go to the game on this machine
(video.py, docs/protocol.md "Camera preview stream"). --video-control-port 0 turns that off.
"""

import argparse
import threading
import time
from pathlib import Path

import cv2

from camera import FrameGrabber
from pose import PoseEstimator
from protocol import FpsMeter, UdpSender, build_packet, encode
from skeleton import BONES, JOINT_COUNT
from video import CONTROL_PORT, VideoStreamer

LEFT_COLOR = (255, 170, 0)    # BGR: player's left side
RIGHT_COLOR = (0, 140, 255)   # BGR: player's right side
MID_COLOR = (230, 230, 230)


def parse_args():
    p = argparse.ArgumentParser(description="MoveIt motion service (camera -> pose -> UDP)")
    p.add_argument("--camera", type=int, default=0, help="camera index (0 = built-in)")
    p.add_argument("--width", type=int, default=1280)
    p.add_argument("--height", type=int, default=720)
    p.add_argument("--fps", type=int, default=60, help="requested camera fps (the camera may give less)")
    p.add_argument("--backend", choices=["auto", "dshow", "msmf", "any"], default="auto")
    p.add_argument("--model", choices=["auto", "lite", "full"], default="auto",
                   help="auto = start with full (more accurate), drop to lite if it can't keep 22 fps")
    p.add_argument("--num-poses", type=int, default=1)
    p.add_argument("--host", default="127.0.0.1", help="where the game listens")
    p.add_argument("--port", type=int, default=5005)
    p.add_argument("--preview", action="store_true", help="show camera window with skeleton")
    p.add_argument("--record", type=Path, help="write every packet to this .jsonl file")
    p.add_argument("--video-control-port", type=int, default=CONTROL_PORT,
                   help="the game asks for camera preview frames here (Pikturo's camera mode); 0 = never send video")
    p.add_argument("--duration", type=float, default=0, help="stop after N seconds (0 = run forever)")
    p.add_argument("--stats-every", type=float, default=2.0, help="print stats every N seconds (0 = off)")
    return p.parse_args()


def side_color(*joints):
    """COCO-17: odd indices are the player's left side, even (except nose 0) the right side."""
    if all(j % 2 == 1 for j in joints):
        return LEFT_COLOR
    if all(j % 2 == 0 and j > 0 for j in joints):
        return RIGHT_COLOR
    return MID_COLOR


def draw_preview(frame, people, fps, inference_ms, latency_ms, camera_fps=0.0):
    """Draw mirrored camera image + skeletons, so it behaves like a mirror (same as the game)."""
    img = cv2.flip(frame, 1)
    h, w = img.shape[:2]
    for person in people:
        pts = [(int((1 - x) * w), int(y * h), v) for (x, y, _z, v) in person.joints]
        for a, b in BONES:
            if pts[a][2] > 0.3 and pts[b][2] > 0.3:
                cv2.line(img, pts[a][:2], pts[b][:2], side_color(a, b), 4, cv2.LINE_AA)
        for i, (x, y, v) in enumerate(pts):
            if v > 0.3:
                cv2.circle(img, (x, y), 6, side_color(i), -1, cv2.LINE_AA)

    lines = [
        f"poses {fps:5.1f} fps (camera {camera_fps:4.1f})   inference {inference_ms:5.1f} ms   capture->send {latency_ms:5.1f} ms",
        f"people: {len(people)}" + (f"   score {people[0].score:.2f}" if people else "   (step back until your whole body is visible)"),
        "q / Esc = quit",
    ]
    for i, text in enumerate(lines):
        y = 30 + i * 28
        cv2.putText(img, text, (12, y), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 0, 0), 4, cv2.LINE_AA)
        cv2.putText(img, text, (12, y), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 1, cv2.LINE_AA)
    return img


class PreviewWindow:
    """The preview window on its own thread. cv2.imshow + cv2.waitKey(1) take ~10 ms on Windows (measured 2026-10-19:
    median 11.5 ms), and inside the camera loop they made it skip every other frame of a 60 fps camera: the game got
    ~30 poses a second with gaps of 33 and 50 ms (motion-service-py/sessions/punches.jsonl). Here the loop never waits
    for the window; the window shows the newest frame whenever it is ready."""

    TITLE = "MoveIt motion service"

    def __init__(self):
        self._lock = threading.Lock()
        self._ready = threading.Event()
        self._item = None
        self.closed = False
        self._thread = threading.Thread(target=self._loop, name="Preview", daemon=True)
        self._thread.start()

    def show(self, frame, people, fps, inference_ms, latency_ms, camera_fps):
        with self._lock:
            self._item = (frame, people, fps, inference_ms, latency_ms, camera_fps)
        self._ready.set()

    def _loop(self):
        while not self.closed:
            if self._ready.wait(0.05):
                self._ready.clear()
                with self._lock:
                    item, self._item = self._item, None
                if item is not None:
                    cv2.imshow(self.TITLE, draw_preview(*item))
            if (cv2.waitKey(1) & 0xFF) in (27, ord("q")):   # also keeps the window responsive between frames
                self.closed = True
        cv2.destroyAllWindows()

    def close(self):
        self.closed = True
        self._thread.join(timeout=1)


def main():
    args = parse_args()
    model = "full" if args.model == "auto" else args.model

    grabber = FrameGrabber(args.camera, args.width, args.height, args.fps, args.backend).start()
    print(f"Camera {args.camera}: {grabber.width}x{grabber.height} @ {grabber.camera_fps:.0f} fps (requested)")
    estimator = PoseEstimator(model, args.num_poses)
    sender = UdpSender(args.host, args.port)
    video = VideoStreamer(args.video_control_port) if args.video_control_port else None
    print(f"Sending Motion Protocol v1.1 to udp://{args.host}:{args.port}  (model: {args.model} -> {model})")

    preview = PreviewWindow() if args.preview else None
    record_file = None
    if args.record:
        args.record.parent.mkdir(parents=True, exist_ok=True)
        record_file = args.record.open("w", encoding="utf-8")
        print(f"Recording to {args.record}")

    fps_meter = FpsMeter()
    slow_since = None
    slow_ms = 0.0
    frame_no = 0
    last_seq = -1
    started = time.perf_counter()
    last_stats = started
    inference_sum = latency_sum = 0.0
    stats_frames = 0
    detected_frames = 0

    try:
        while True:
            got = grabber.wait_new(last_seq)
            if got is None:
                if not grabber.running:
                    print(grabber.error or "Camera stopped.")
                    break
                continue
            last_seq, frame, capture_ms, capture_mono_ms = got

            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            t0 = time.perf_counter()
            # MediaPipe smooths the landmarks itself in VIDEO mode (One Euro, its dt from these timestamps): give it
            # the capture time, not the moment inference starts (that varies with the loop, the frame didn't), on the
            # monotonic clock (a wall clock set back would look like frames 1 ms apart).
            people = estimator.detect(rgb, capture_mono_ms)
            inference_ms = (time.perf_counter() - t0) * 1000
            fps = fps_meter.tick()

            # Auto model: full is more accurate; if inference alone can't keep ~25 fps (40 ms), switch to lite once.
            # Judged on inference time, not fps: a dim room slows the camera, which a smaller model can't fix.
            if args.model == "auto" and model == "full" and frame_no > 60:
                slow_ms = 0.9 * slow_ms + 0.1 * inference_ms if frame_no > 61 else inference_ms
                if slow_ms > 40:
                    slow_since = slow_since or time.perf_counter()
                    if time.perf_counter() - slow_since > 3:
                        estimator.close()
                        model = "lite"
                        estimator = PoseEstimator(model, args.num_poses)
                        print(f"Auto model: full needs {slow_ms:.0f} ms per frame, switched to lite", flush=True)
                else:
                    slow_since = None

            packet = build_packet(f"py-mediapipe-{model}", frame_no, capture_ms, grabber.width, grabber.height, fps,
                                  inference_ms, people, camera_fps=grabber.measured_fps, model=model)
            data = encode(packet)
            sender.send_raw(data)
            latency_ms = time.time() * 1000 - capture_ms
            if record_file:
                record_file.write(data.decode("utf-8") + "\n")
            if video and video.wanted():
                video.send(frame, capture_ms, frame_no)

            frame_no += 1
            stats_frames += 1
            inference_sum += inference_ms
            latency_sum += latency_ms
            detected_frames += 1 if people else 0

            if preview:
                if preview.closed:
                    break
                preview.show(frame, people, fps, inference_ms, latency_ms, grabber.measured_fps)

            now = time.perf_counter()
            if args.stats_every and now - last_stats >= args.stats_every:
                print(
                    f"[{model}] poses {fps:5.1f} fps (camera {grabber.measured_fps:4.1f}) | "
                    f"inference {inference_sum / stats_frames:5.1f} ms | "
                    f"capture->send {latency_sum / stats_frames:5.1f} ms | "
                    f"person detected in {100 * detected_frames / stats_frames:3.0f}% of frames",
                    flush=True,
                )
                last_stats = now
                inference_sum = latency_sum = 0.0
                stats_frames = detected_frames = 0

            if args.duration and now - started >= args.duration:
                break
    except KeyboardInterrupt:
        pass
    finally:
        if preview:
            preview.close()
        grabber.stop()
        estimator.close()
        sender.close()
        if video:
            video.close()
        if record_file:
            record_file.close()
        print(f"Stopped after {frame_no} frames.")


if __name__ == "__main__":
    assert JOINT_COUNT == 17
    main()
