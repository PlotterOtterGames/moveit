"""Summarises tools/dev/box-cpu-ab.sh's logs: per configuration the game's frame rate and its main and render thread
times ([Perf]), the camera AI's rates (MoveItMotion stats: frames analysed, poses, the NPU's part, camera frames it never
saw), and the CPU share of the app's busiest threads over the measured stretch, in % of one core: the camera thread
(pool-N-thread-1: the analyser, the camera picture for the game, the crop for the pose model), Unity's main thread, its
render thread (Thread-5 on the X98K Pro), the NPU's thread, CameraX's frame conversion, the GPU driver's thread, and the
whole app.
    python tools/dev/box_cpu_summary.py <out_dir> [config ...]
"""
import os
import re
import sys


def mean(values):
    return sum(values) / len(values) if values else float("nan")


def summarise(path):
    fps, late, main, render, cam, ai, npu, skipped, soc, gpu, cpu = ([] for _ in range(11))
    a, b = {}, {}
    for line in open(path, encoding="utf-8", errors="replace"):
        line = line.rstrip("\n")
        if line[:2] in ("A|", "B|"):
            name, _, value = line[2:].rpartition("|")
            d = a if line[0] == "A" else b
            try:
                d[name] = d.get(name, 0) + float(value)
            except ValueError:
                pass    # a thread that ended while it was read
            continue
        m = re.search(r"\[Perf\] \w+.*?: ([\d.]+) fps \(target \d+[^,]*, ([\d.]+)% missed\).*?main ([\d.]+) ms.*?render ([\d.]+) ms", line)
        if m:
            fps.append(float(m.group(1)))
            late.append(float(m.group(2)))
            main.append(float(m.group(3)))
            render.append(float(m.group(4)))
        m = re.search(r"stats: camera ([\d.]+) fps, AI ([\d.]+) fps", line)
        if m:
            cam.append(float(m.group(1)))
            ai.append(float(m.group(2)))
            k = re.search(r"crop [\d.]+ ms, landmarks [\d.]+ ms \(NPU ([\d.]+)", line) or \
                re.search(r"landmarks [\d.]+ ms \(crop [\d.]+, NPU ([\d.]+)", line)
            if k:
                npu.append(float(k.group(1)))
            k = re.search(r"gaps: (\d+) camera frames skipped", line)
            if k:
                skipped.append(int(k.group(1)))
        m = re.search(r"heat: SoC (\d+) NPU load (\d+)% GPU (\d+) MHz \S+ CPU (\d+) MHz", line)
        if m:
            soc.append(int(m.group(1)) / 1000)
            gpu.append(int(m.group(3)))
            cpu.append(int(m.group(4)))
    seconds = (b.get("time", 0) - a.get("time", 0)) / 1000 or float("nan")
    # jiffies of 10 ms a second = % of a core
    share = {name: (b[name] - a.get(name, 0)) / seconds for name in b if name != "time"}
    # the first [Perf] line covers the switch: leave it out
    return dict(fps=mean(fps[1:]), late=mean(late[1:]), main=mean(main[1:]), render=mean(render[1:]), cam=mean(cam),
                ai=mean(ai), npu=mean(npu), skipped=mean(skipped), soc=(soc[0], soc[-1]) if soc else (0, 0),
                gpu=mean(gpu), cpu=mean(cpu), share=share, total=sum(share.values()), n=max(0, len(fps) - 1))


def main():
    out = sys.argv[1]
    names = sys.argv[2:] or sorted(f[:-4] for f in os.listdir(out) if f.endswith(".txt"))
    print(f"{'':12} {'game':>5} {'late':>6} {'main':>5} {'rend':>5} | {'cam':>5} {'poses':>5} {'NPU':>5} {'skip':>5} |"
          f" {'--- % of a core: camera':>24} {'main':>5} {'rend':>5} {'npu':>4} {'camX':>5} {'mali':>5} {'app':>5} |"
          f" {'CPU':>5} {'GPU':>4} {'SoC C':>10}")
    print(f"{'config':12} {'fps':>5} {'%':>6} {'ms':>5} {'ms':>5} | {'/s':>5} {'/s':>5} {'ms':>5} {'/5 s':>5} |"
          f" {'':>24} {'':>5} {'':>5} {'':>4} {'':>5} {'':>5} {'':>5} | {'MHz':>5} {'MHz':>4}")
    for name in names:
        path = os.path.join(out, name + ".txt")
        if not os.path.exists(path):
            continue
        s = summarise(path)
        sh = s["share"]
        camera = sum(v for k, v in sh.items() if re.match(r"pool-\d+-thread-1$", k))
        unity_main = max([v for k, v in sh.items() if k == "UnityMain"] or [0])
        print(f"{name:12} {s['fps']:5.1f} {s['late']:6.1f} {s['main']:5.1f} {s['render']:5.1f} | {s['cam']:5.1f} {s['ai']:5.1f} "
              f"{s['npu']:5.1f} {s['skipped']:5.1f} | {camera:24.1f} {unity_main:5.1f} {sh.get('Thread-5', 0):5.1f} "
              f"{sh.get('pose-npu', 0):4.1f} {sh.get('CameraX-camerax', 0):5.1f} {sh.get('mali-cmar-backe', 0):5.1f} "
              f"{s['total']:5.0f} | {s['cpu']:5.0f} {s['gpu']:4.0f} {s['soc'][0]:5.1f}>{s['soc'][1]:4.1f}  ({s['n']} [Perf] lines)")


if __name__ == "__main__":
    main()
