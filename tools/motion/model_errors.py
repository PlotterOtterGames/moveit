"""Count what no real body does, to compare pose models on the same footage (clip_models.py) without ground truth:
fast real movement (punches, waves) isn't counted, only the impossible.

- spikes: a joint that steps out by more than 10 % of a torso and comes straight back the next frame (its position two
  frames apart moved less than half that), i.e. one wrong frame;
- bone length in 3D (the world joints, metres): an upper arm or forearm more than 25 % off its median length, and how
  much it changes from one frame to the next (an arm doesn't grow or shrink);
- lost frames, and wrists the model isn't sure of (visibility < 0.5);
- with two or more recordings: how far apart their wrists are (torso lengths), frame by frame.

    motion-service-py/.venv/Scripts/python tools/motion/model_errors.py <a.jsonl> [<b.jsonl> ...] [--from S] [--to S]
"""
import argparse
import json

import numpy as np

JOINTS = {"l wrist": 9, "r wrist": 10, "l elbow": 7, "r elbow": 8, "nose": 0, "l shoulder": 5, "r shoulder": 6,
          "l hip": 11, "r hip": 12}
BONES = {"l upper arm": (5, 7), "r upper arm": (6, 8), "l forearm": (7, 9), "r forearm": (8, 10)}
SPIKE = 0.10          # torso lengths out and back
OFF = 0.25            # share of the median bone length


def load(path, start, end):
    t, k, w = {}, {}, {}
    t0 = None
    for line in open(path, encoding="utf-8"):
        p = json.loads(line)
        t0 = p["t"] if t0 is None else t0
        s = (p["t"] - t0) / 1000.0
        if not start <= s <= end:
            continue
        f = p["f"]
        t[f] = s
        if p.get("people"):
            person = p["people"][0]
            k[f] = np.array(person["k"], np.float64).reshape(17, 4) * (p["w"], p["h"], 1, 1)
            if "wk" in person:
                w[f] = np.array(person["wk"], np.float64).reshape(17, 3)
    return t, k, w


def torso(k):
    return np.median([np.linalg.norm(v[5:7, :2].mean(0) - v[11:13, :2].mean(0)) for v in k.values()])


def report(path, start, end):
    t, k, w = load(path, start, end)
    frames = sorted(t)
    minutes = (t[frames[-1]] - t[frames[0]]) / 60
    size = torso(k)
    print(f"{path}\n  {len(frames)} frames, {minutes * 60:.1f} s, person lost in {len(frames) - len(k)}, "
          f"torso {size:.0f} px")
    print("  spikes (out > 10 % of a torso and straight back), a minute:")
    line = []
    for name, j in JOINTS.items():
        n = 0
        for f in frames:
            if f - 1 in k and f in k and f + 1 in k:
                a, b, c = k[f - 1][j, :2], k[f][j, :2], k[f + 1][j, :2]
                out, back, net = np.linalg.norm(b - a), np.linalg.norm(c - b), np.linalg.norm(c - a)
                if out > SPIKE * size and back > SPIKE * size and net < 0.5 * min(out, back):
                    n += 1
        line.append(f"{name} {n / minutes:.1f}")
    print("    " + ", ".join(line))
    if w:
        print("  bones in 3D: median length, frames > 25 % off it, frame-to-frame change (median / 95th pct):")
        for name, (a, b) in BONES.items():
            lengths = np.array([np.linalg.norm(w[f][a] - w[f][b]) for f in frames if f in w])
            med = np.median(lengths)
            off = np.mean(np.abs(lengths / med - 1) > OFF) * 100
            steps = np.array([abs(np.linalg.norm(w[f][a] - w[f][b]) - np.linalg.norm(w[f - 1][a] - w[f - 1][b])) / med
                              for f in frames if f in w and f - 1 in w])
            print(f"    {name:12s} {med * 100:5.1f} cm, off {off:4.1f} %, change {np.median(steps) * 100:4.1f} % / "
                  f"{np.percentile(steps, 95) * 100:4.1f} %")
    unsure = [np.mean([k[f][j, 3] < 0.5 for f in k]) * 100 for j in (9, 10)]
    print(f"  wrists unsure (visibility < 0.5): left {unsure[0]:.1f} %, right {unsure[1]:.1f} % of the frames")
    return t, k, size


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("recordings", nargs="+")
    ap.add_argument("--from", dest="start", type=float, default=0)
    ap.add_argument("--to", dest="end", type=float, default=1e9)
    args = ap.parse_args()
    runs = [report(r, args.start, args.end) for r in args.recordings]
    if len(runs) > 1:
        (_, ka, size), names = runs[0], [r.split("\\")[-1].split("/")[-1] for r in args.recordings]
        for (_, kb, _), name in zip(runs[1:], names[1:]):
            both = sorted(set(ka) & set(kb))
            for j, side in ((9, "left"), (10, "right")):
                d = np.array([np.linalg.norm(ka[f][j, :2] - kb[f][j, :2]) for f in both]) / size
                print(f"  {names[0]} vs {name}, {side} wrist apart: median {np.median(d) * 100:.1f} %, 95th pct "
                      f"{np.percentile(d, 95) * 100:.1f} %, over 30 % of a torso in {np.mean(d > 0.3) * 100:.1f} % "
                      f"of the frames")


if __name__ == "__main__":
    main()
