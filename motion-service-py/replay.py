"""Replay a session recorded with `main.py --record file.jsonl` with its original timing.

Useful for tuning gesture thresholds in Unity against the same real movement again and again.

    python replay.py sessions/jump.jsonl --loop
"""

import argparse
import json
import time
from pathlib import Path

from protocol import UdpSender, encode, now_ms


def main():
    p = argparse.ArgumentParser(description="Replay a recorded motion session over UDP")
    p.add_argument("file", type=Path)
    p.add_argument("--host", default="127.0.0.1")
    p.add_argument("--port", type=int, default=5005)
    p.add_argument("--speed", type=float, default=1.0, help="playback speed multiplier")
    p.add_argument("--loop", action="store_true")
    args = p.parse_args()

    packets = [json.loads(line) for line in args.file.read_text(encoding="utf-8").splitlines() if line.strip()]
    if not packets:
        raise SystemExit(f"{args.file} is empty")
    duration = (packets[-1]["t"] - packets[0]["t"]) / 1000
    print(f"Replaying {len(packets)} frames ({duration:.1f} s) -> udp://{args.host}:{args.port}")

    sender = UdpSender(args.host, args.port)
    frame = 0
    try:
        while True:
            start = time.perf_counter()
            t0 = packets[0]["t"]
            for packet in packets:
                due = (packet["t"] - t0) / 1000 / args.speed
                time.sleep(max(0.0, due - (time.perf_counter() - start)))
                packet = dict(packet, src="py-replay", t=now_ms(), f=frame)
                sender.send_raw(encode(packet))
                frame += 1
            if not args.loop:
                break
    except KeyboardInterrupt:
        pass
    finally:
        sender.close()


if __name__ == "__main__":
    main()
