"""Camera capture on a background thread that always keeps only the newest frame.

Reading frames in the main loop lets the driver queue old frames while inference runs, which adds
latency. The grabber drains the camera continuously so the pose model always sees the latest image.
"""

import sys
import threading
import time

import cv2

from protocol import FpsMeter


class FrameGrabber:
    def __init__(self, index=0, width=1280, height=720, fps=30, backend="auto"):
        if backend == "auto":
            backend = "dshow" if sys.platform == "win32" else "any"
        api = {"dshow": cv2.CAP_DSHOW, "msmf": cv2.CAP_MSMF, "any": cv2.CAP_ANY}[backend]

        self.cap = cv2.VideoCapture(index, api)
        if not self.cap.isOpened():
            raise RuntimeError(f"Could not open camera {index} (backend {backend}).")
        self.cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))
        self.cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
        self.cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
        self.cap.set(cv2.CAP_PROP_FPS, fps)
        self.width = int(self.cap.get(cv2.CAP_PROP_FRAME_WIDTH))
        self.height = int(self.cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
        self.camera_fps = self.cap.get(cv2.CAP_PROP_FPS)

        self._cond = threading.Condition()
        self._frame = None
        self._t_ms = 0
        self._seq = 0
        self._running = False
        self._thread = None
        self._meter = FpsMeter()
        self.measured_fps = 0.0  # frames actually delivered by the camera per second
        self.error = None

    def start(self):
        self._running = True
        self._thread = threading.Thread(target=self._loop, name="FrameGrabber", daemon=True)
        self._thread.start()
        return self

    def _loop(self):
        failures = 0
        while self._running:
            ok, frame = self.cap.read()
            t_ms = int(time.time() * 1000)
            if not ok:
                failures += 1
                if failures > 50:
                    self.error = "Camera stopped delivering frames."
                    self._running = False
                    with self._cond:
                        self._cond.notify_all()
                    return
                time.sleep(0.01)
                continue
            failures = 0
            self.measured_fps = self._meter.tick()
            with self._cond:
                self._frame = frame
                self._t_ms = t_ms
                self._seq += 1
                self._cond.notify_all()

    def wait_new(self, last_seq, timeout=1.0):
        """Block until a frame newer than last_seq exists. Returns (seq, frame, capture_time_ms) or None."""
        with self._cond:
            if not self._cond.wait_for(lambda: self._seq != last_seq or not self._running, timeout):
                return None
            if self._frame is None or self._seq == last_seq:
                return None
            return self._seq, self._frame, self._t_ms

    @property
    def running(self):
        return self._running

    def stop(self):
        self._running = False
        if self._thread:
            self._thread.join(timeout=2)
        self.cap.release()
