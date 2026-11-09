"""Summarises tools/dev/box-two.sh's logs: per configuration the game's frame rate and late frames ([Perf]), the pose
AI's rate and the NPU's part of a frame (MoveItMotion stats), the SoC temperature at the start and the end, the GPU's
clock and the NPU's load.
    python tools/dev/box_two_summary.py <out_dir> [config ...]
"""
import os
import re
import sys


def mean(values):
    return sum(values) / len(values) if values else float("nan")


def summarise(path):
    fps, late, ai, npu, soc, gpu_mhz, npu_load, cpu_mhz, people = [], [], [], [], [], [], [], [], []
    for line in open(path, encoding="utf-8", errors="replace"):
        m = re.search(r"\[Perf\] \w+.*?: ([\d.]+) fps \(target \d+, ([\d.]+)% missed\)", line)
        if m:
            fps.append(float(m.group(1)))
            late.append(float(m.group(2)))
        m = re.search(r"AI ([\d.]+) fps, [\d.]+ ms, NPU \w+ \((?:crop [\d.]+ ms, )?landmarks ([\d.]+) ms \(NPU ([\d.]+)", line)
        if m and "(idle)" not in line:
            ai.append(float(m.group(1)))
            npu.append(float(m.group(3)))
        m = re.search(r"people (\d+)/(\d+) \((\d+) run", line)
        if m:
            people.append(int(m.group(3)))
        m = re.search(r"heat: SoC (\d+) GPU \d+ NPU load (\d+)% GPU (\d+) MHz \S+ CPU (\d+) MHz", line)
        if m:
            soc.append(int(m.group(1)) / 1000.0)
            npu_load.append(int(m.group(2)))
            gpu_mhz.append(int(m.group(3)))
            cpu_mhz.append(int(m.group(4)))
    # the first [Perf] line covers the switch: leave it out
    fps, late = fps[1:], late[1:]
    return dict(fps=mean(fps), late=mean(late), ai=mean(ai), npu=mean(npu), runs=mean(people),
                soc0=soc[0] if soc else float("nan"), soc1=soc[-1] if soc else float("nan"),
                gpu=mean(gpu_mhz), npuload=mean(npu_load), cpu=mean(cpu_mhz), n=len(fps))


def main():
    out = sys.argv[1]
    configs = sys.argv[2:] or sorted(f[:-4] for f in os.listdir(out) if f.endswith(".txt"))
    print(f"{'config':10} {'game fps':>9} {'late %':>7} {'AI/s':>6} {'runs':>5} {'NPU ms':>7} {'NPU %':>6} "
          f"{'GPU MHz':>8} {'CPU MHz':>8} {'SoC C':>12}  ([Perf] lines)")
    for c in configs:
        path = os.path.join(out, c + ".txt")
        if not os.path.exists(path):
            continue
        s = summarise(path)
        print(f"{c:10} {s['fps']:9.1f} {s['late']:7.1f} {s['ai']:6.1f} {s['runs']:5.2f} {s['npu']:7.1f} {s['npuload']:6.0f} "
              f"{s['gpu']:8.0f} {s['cpu']:8.0f} {s['soc0']:5.1f}->{s['soc1']:5.1f}  ({s['n']})")


if __name__ == "__main__":
    main()
