"""Motion Protocol v1: JSON datagrams over UDP (see docs/protocol.md)."""

import json
import socket
import time

PROTOCOL_VERSION = 1


def now_ms():
    return int(time.time() * 1000)


def build_packet(src, frame, t_ms, width, height, fps, inference_ms, people, camera_fps=None, model=None):
    packet = {
        "v": PROTOCOL_VERSION,
        "src": src,
        "t": t_ms,
        "f": frame,
        "w": width,
        "h": height,
        "fps": round(fps, 1),
        "inf": round(inference_ms, 1),
        "people": [_person(i, p) for i, p in enumerate(people)],
    }
    if camera_fps is not None:
        packet["cam"] = round(camera_fps, 1)
    if model:
        packet["model"] = model
    return packet


def _person(i, p):
    person = {"id": i, "score": round(p.score, 3), "k": [round(c, 4) for joint in p.joints for c in joint]}
    if p.world:
        person["wk"] = [round(c, 4) for joint in p.world for c in joint]
    return person


def encode(packet):
    return json.dumps(packet, separators=(",", ":")).encode("utf-8")


class UdpSender:
    def __init__(self, host="127.0.0.1", port=5005):
        self.addr = (host, port)
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    def send(self, packet):
        self.send_raw(encode(packet))

    def send_raw(self, data):
        try:
            self.sock.sendto(data, self.addr)
        except OSError:
            # Nobody listening yet (Windows reports ICMP port-unreachable as an error). Keep going.
            pass

    def close(self):
        self.sock.close()


class FpsMeter:
    """Exponential moving average of frames per second."""

    def __init__(self, smoothing=0.9):
        self.smoothing = smoothing
        self.fps = 0.0
        self._last = None

    def tick(self):
        now = time.perf_counter()
        if self._last is not None:
            dt = now - self._last
            if dt > 0:
                inst = 1.0 / dt
                self.fps = inst if self.fps == 0 else self.smoothing * self.fps + (1 - self.smoothing) * inst
        self._last = now
        return self.fps
