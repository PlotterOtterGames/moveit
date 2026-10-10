"""Camera preview stream (docs/protocol.md, "Camera preview stream"): small JPEG frames for the game, sent only
while the game asks for them (e.g. Pikturo!'s camera mode). Localhost only: the picture never leaves the machine.

The game sends a control datagram to 127.0.0.1:<control port> about twice a second while it wants video:
    {"video": 1, "port": 5006, "width": 640}        (and {"video": 0} to stop at once)
Frames go to 127.0.0.1:<port>, one datagram each: header (magic "TVF1", capture time ms, frame number, width,
height; little-endian) + JPEG. The image is the raw camera picture (not mirrored), like the joints in the packets.
"""

import json
import socket
import struct
import time

import cv2

MAGIC = b"TVF1"
HEADER = struct.Struct("<4sqIHH")
MAX_DATAGRAM = 60000          # one UDP datagram, no fragmentation games
CONTROL_PORT = 5007
VIDEO_PORT = 5006
KEEPALIVE_S = 2.0             # stop streaming when the game hasn't asked for this long


class VideoStreamer:
    def __init__(self, control_port=CONTROL_PORT):
        self.port = VIDEO_PORT
        self.width = 640
        self.frames = 0
        self._until = 0.0
        self._out = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._control = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        try:
            self._control.bind(("127.0.0.1", control_port))
            self._control.setblocking(False)
        except OSError as e:
            print(f"Camera preview stream off: can't listen on control port {control_port} ({e})")
            self._control.close()
            self._control = None

    def wanted(self):
        """Reads the game's requests. True while the game wants frames."""
        while self._control is not None:
            try:
                data, _ = self._control.recvfrom(2048)
            except (BlockingIOError, InterruptedError):
                break
            except OSError:
                continue  # Windows reports an earlier ICMP "port unreachable" here: ignore it
            try:
                msg = json.loads(data.decode("utf-8"))
            except ValueError:
                continue
            if msg.get("video"):
                if not self.streaming:
                    print("Camera preview stream: on", flush=True)
                self.port = int(msg.get("port", self.port))
                self.width = max(160, min(1280, int(msg.get("width", self.width))))
                self._until = time.monotonic() + KEEPALIVE_S
            else:
                self._until = 0.0
        return self.streaming

    @property
    def streaming(self):
        return time.monotonic() < self._until

    def send(self, frame_bgr, t_ms, frame_no):
        """Scales the frame to the requested width, encodes it as JPEG small enough for one datagram, sends it."""
        h, w = frame_bgr.shape[:2]
        if w > self.width:
            h, w = round(h * self.width / w), self.width
            frame_bgr = cv2.resize(frame_bgr, (w, h), interpolation=cv2.INTER_AREA)
        for quality in (80, 65, 50, 35):
            ok, jpg = cv2.imencode(".jpg", frame_bgr, [cv2.IMWRITE_JPEG_QUALITY, quality])
            if ok and len(jpg) + HEADER.size <= MAX_DATAGRAM:
                break
        else:
            return False
        try:
            self._out.sendto(HEADER.pack(MAGIC, int(t_ms), frame_no & 0xFFFFFFFF, w, h) + jpg.tobytes(),
                             ("127.0.0.1", self.port))
        except OSError:
            return False  # the game stopped listening; the keepalive runs out soon
        self.frames += 1
        return True

    def close(self):
        self._out.close()
        if self._control is not None:
            self._control.close()
