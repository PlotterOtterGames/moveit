"""Record a raw clip from the box's USB camera (the camera's own MJPEG frames and their capture times), to run every
pose model on exactly the same pictures afterwards (tools/motion/clip_models.py).

The app must let go of the camera first: this opens Android's settings in front of it (Tundu pauses its camera AI), so
the TV shows the settings while recording. The camera's light controls are set like the app's CameraTuner would (its
ladder; level "auto" steps it on test captures until the middle of the picture is neither dark nor bright), with
exposure priority off. Needs root on the box (this firmware's su). Afterwards: home key or
`adb shell am start -n al.tundu.games/com.unity3d.player.UnityPlayerActivity`.

    motion-service-py/.venv/Scripts/python tools/motion/box_clip.py <name> [--seconds 60] [--delay 10]
        [--mode 1024x768@30] [--level auto|0..5] [--device 192.168.1.57:5555] [--out motion-service-py/sessions/clips]

Writes <out>/<name>.mjpg and <name>.log (gitignored like every recording).
"""
import argparse
import subprocess
import sys
import time
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
from mjpeg_clip import frames  # noqa: E402

ADB = r"D:\Unity\6000.3.9f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
NODE = "/dev/video9"
BOX_DIR = "/data/local/tmp/clip"
# CameraTuner's ladder (gain share of its range, gamma and saturation times their defaults) and its brightness bands.
GAIN = [0, 0.4, 0.8, 1, 1, 1]
GAMMA = [1, 1, 1.1, 1.3, 1.6, 1.9]
SATURATION = [1, 1.17, 1.33, 1.5, 1.67, 1.83]
GAIN_MAX, GAMMA_DEFAULT, SATURATION_DEFAULT = 100, 100, 60          # the X98K Pro's camera (v4l2-ctl --list-ctrls)
VERY_DARK, DARK, BRIGHT, VERY_BRIGHT = 55, 90, 170, 210


def adb(device, *args, check=True):
    return subprocess.run([ADB, "-s", device, *args], capture_output=True, text=True, check=check).stdout


def shell(device, command):
    return adb(device, "shell", command)


def set_level(device, level):
    controls = (f"gain={round(GAIN[level] * GAIN_MAX)},gamma={round(GAMMA[level] * GAMMA_DEFAULT)},"
                f"saturation={round(SATURATION[level] * SATURATION_DEFAULT)},exposure_auto_priority=0")
    shell(device, f"su 0 v4l2-ctl -d {NODE} --set-ctrl={controls}")
    return controls


def capture(device, name, width, height, fps, count, verbose):
    log = f" --verbose > {BOX_DIR}/{name}.log 2>&1" if verbose else " > /dev/null 2>&1"
    shell(device, f"mkdir -p {BOX_DIR}; su 0 v4l2-ctl -d {NODE} --set-fmt-video=width={width},height={height},"
                  f"pixelformat=MJPG --set-parm={fps} --stream-mmap=4 --stream-count={count} "
                  f"--stream-to={BOX_DIR}/{name}.mjpg{log}")


def middle_mean(path):
    """CameraTuner's measure: the mean brightness of the middle of the picture (where the player stands)."""
    t, jpeg = frames(path)[-1]
    img = cv2.imdecode(np.frombuffer(jpeg, np.uint8), cv2.IMREAD_GRAYSCALE)
    h, w = img.shape
    return float(img[h // 4:3 * h // 4, w // 4:3 * w // 4].mean())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("name")
    ap.add_argument("--seconds", type=float, default=60)
    ap.add_argument("--delay", type=float, default=10, help="seconds to wait before recording (time to walk there)")
    ap.add_argument("--mode", default="1024x768@30")
    ap.add_argument("--level", default="auto")
    ap.add_argument("--device", default="192.168.1.57:5555")
    ap.add_argument("--out", type=Path, default=Path("motion-service-py/sessions/clips"))
    args = ap.parse_args()
    size, fps = args.mode.split("@")
    width, height = (int(v) for v in size.split("x"))
    fps = int(fps)
    args.out.mkdir(parents=True, exist_ok=True)

    subprocess.run([ADB, "connect", args.device], capture_output=True)
    shell(args.device, "am start -a android.settings.SETTINGS")
    time.sleep(2)

    if args.level == "auto":
        level = 3
        for _ in range(5):
            set_level(args.device, level)
            capture(args.device, "probe", width, height, fps, 2 * fps, verbose=False)
            adb(args.device, "pull", f"{BOX_DIR}/probe.mjpg", str(args.out / "probe.mjpg"))
            mean = middle_mean(args.out / "probe.mjpg")
            step = 2 if mean < VERY_DARK else 1 if mean < DARK else -2 if mean > VERY_BRIGHT else -1 if mean > BRIGHT else 0
            print(f"light level {level}: middle of the picture {mean:.0f}")
            nxt = min(5, max(0, level + step))
            if nxt == level:
                break
            level = nxt
        (args.out / "probe.mjpg").unlink(missing_ok=True)
    else:
        level = int(args.level)
    print("camera controls:", set_level(args.device, level))

    if args.delay > 0:
        print(f"recording starts in {args.delay:.0f} s ...", flush=True)
        time.sleep(args.delay)
    count = round(args.seconds * fps)
    print(f"recording {count} frames ({args.seconds:.0f} s at {width}x{height}@{fps}) ...", flush=True)
    started = time.time()
    capture(args.device, args.name, width, height, fps, count, verbose=True)
    print(f"done in {time.time() - started:.0f} s; pulling", flush=True)
    for ext in ("mjpg", "log"):
        adb(args.device, "pull", f"{BOX_DIR}/{args.name}.{ext}", str(args.out / f"{args.name}.{ext}"))
    shell(args.device, f"rm -f {BOX_DIR}/{args.name}.mjpg {BOX_DIR}/{args.name}.log {BOX_DIR}/probe.mjpg")
    shots = frames(args.out / f"{args.name}.mjpg", fps)
    t = np.array([s[0] for s in shots])
    dt = np.diff(t) * 1000
    print(f"{args.out / (args.name + '.mjpg')}: {len(shots)} frames, {len(shots) / t[-1]:.2f} fps, interval median "
          f"{np.median(dt):.1f} ms, {np.sum(dt > 1.5 * 1000 / fps)} late, light level {level}")


if __name__ == "__main__":
    main()
