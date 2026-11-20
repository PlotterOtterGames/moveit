"""Do picture filters make the pose AI better? Runs MediaPipe's pose models (the box runs the same models and steps on
its NPU, within ~0.5 px of MediaPipe) on a raw box-camera clip (tools/motion/box_clip.py) through picture filters, and
scores every run on what no real body does and on how far the joints land from a reference.

The filters are what's usually suggested for camera input: brightening (gamma), auto levels (white balance and contrast
stretch), local contrast (CLAHE), denoising (Gaussian, bilateral, median, the mirror's motion-adaptive temporal one),
sharpening, edge detection (the picture replaced by its edges, or its edges drawn over it), grayscale, and the
"mirrored twin" (each frame also run mirrored, both answers averaged: two model runs a frame). "control" re-saves each
frame as JPEG 95, a change nobody can see: how far its numbers move from the unfiltered run is the noise floor (any
change to the pixels sends the tracking down a slightly different path). "find-<filter>" uses the filter only on frames
where nobody is followed yet (MediaPipe runs its person detector exactly then; the landmark model then tracks on the
plain picture), "lowdet" lowers the person detector's threshold from 0.5 to 0.3.

    python tools/motion/filter_bench.py dim <clip.mjpg> --out <dir>       # writes <dir>/dim.mjpg: a dimmer room
    python tools/motion/filter_bench.py run <clip.mjpg> --out <dir> [--filters a,b] [--models lite,full] [--jobs 3]
    python tools/motion/filter_bench.py score --out <dir> [--json results.json]

`run` writes <dir>/<clip stem>.<filter>.<model>.npz (capture times, COCO-17 joints in pixels with visibility, world
joints in metres). `score` compares them: lost frames, the time until the person is first found, one-frame spikes (out
> 10 % of a torso and straight back), 3D forearms more than 25 % off their median length, shake (RMS distance from the
run's own path smoothed both ways in time; "calm": only where the reference moves slower than 0.6 torso lengths a
second, though in a routine full of movement it still rewards sluggish answers), and the distance from a reference: the
full model on the unfiltered clip, smoothed both ways (for a dimmer copy, the same model on the original clip), also on
"same" frames only (where the unfiltered run found the person too: a run that finds them sooner is otherwise scored on
frames the unfiltered run never had). Joint distances are in % of the torso length (shoulders' middle to hips').

"A dimmer room" (`dim`): two stops less light at the same camera settings, calibrated on the box camera's own static
clips (sessions/clips/sweep/level3.mjpg, a still room at tuner level 3): the temporal noise doubled at every brightness (the camera at full
gain can't make up for less light without more noise), a little darker, grain ~1.5 px wide like the camera's (its ISP
demosaics and denoises), JPEG quality 80 like its MJPEG stream.
"""
import argparse
import json
import mmap
import re
import sys
import time
from multiprocessing import Pool
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "motion-service-py"))
sys.path.insert(0, str(Path(__file__).parent))
from mjpeg_clip import capture_times  # noqa: E402

# COCO-17 indices: 0 nose, 1/2 eyes, 3/4 ears, 5/6 shoulders, 7/8 elbows, 9/10 wrists, 11/12 hips, 13/14 knees, 15/16 ankles
FLIP = [0, 2, 1, 4, 3, 6, 5, 8, 7, 10, 9, 12, 11, 14, 13, 16, 15]
FILTERS = ["none", "control", "gamma", "levels", "clahe", "clahe4", "blur", "bilateral", "median", "temporal", "sharpen",
           "edges", "edgeboost", "gray", "twin", "temporal+clahe", "find-levels", "find-clahe4", "lowdet"]
# find-<filter>: the filter only on frames where nobody is followed yet (MediaPipe runs its person detector exactly then;
# once it follows someone the landmark model gets the plain picture). lowdet: no filter, the person detector's
# threshold 0.3 instead of 0.5.


# ---------------------------------------------------------------------------------------------------------- the clip
def index(clip):
    """Byte ranges of the clip's JPEG frames and their capture times (seconds from the first)."""
    with open(clip, "rb") as f, mmap.mmap(f.fileno(), 0, access=mmap.ACCESS_READ) as mm:
        starts = [m.start() for m in re.finditer(rb"\xff\xd8\xff", mm)]
        size = len(mm)
    ranges = list(zip(starts, starts[1:] + [size]))
    log = Path(clip).with_suffix(".log")
    times = capture_times(log) if log.exists() else []
    if len(times) < len(ranges):
        times = [i / 30.0 for i in range(len(ranges))]
    t0 = times[0]
    return ranges, [t - t0 for t in times[:len(ranges)]]


# ----------------------------------------------------------------------------------------------------- a dimmer room
# The box camera's temporal noise (8-bit levels, std) by brightness at tuner level 3 in the user's evening room,
# measured on sessions/clips/sweep/level3.mjpg (85 frames of a still room): the darkest tones are crushed by the
# camera's tone curve, the mid-tones noisiest, the highlights compressed.
NOISE_AT = np.array([0, 20, 45, 80, 125, 200, 255], np.float32)
NOISE_STD = np.array([2.5, 4.3, 6.5, 7.9, 4.0, 3.0, 2.5], np.float32)


def dim_frame(bgr, rng):
    """Two stops less light: the noise doubled at each brightness (3x the variance added), 10 % darker (the camera
    tuner brightens the rest back with gamma), the grain softened like the camera's ISP leaves it, JPEG 80."""
    img = bgr.astype(np.float32)
    level = img.mean(axis=2, keepdims=True)
    std = np.interp(level, NOISE_AT, NOISE_STD) * np.sqrt(3.0)
    noise = rng.standard_normal(img.shape).astype(np.float32)
    noise = cv2.GaussianBlur(noise, (0, 0), 0.7) * 2.1      # a blur of sigma 0.7 leaves ~0.48 of the std: back to 1
    lum = rng.standard_normal(img.shape[:2]).astype(np.float32)
    lum = cv2.GaussianBlur(lum, (0, 0), 0.7)[..., None] * 2.1
    out = img * 0.9 + std * (0.6 * noise + 0.8 * lum)        # luma and chroma grain, 0.6^2 + 0.8^2 = 1
    ok, jpeg = cv2.imencode(".jpg", np.clip(out, 0, 255).astype(np.uint8), [cv2.IMWRITE_JPEG_QUALITY, 80])
    return jpeg.tobytes()


def make_dim(clip, out_dir):
    ranges, times = index(clip)
    out_dir.mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(1)
    dst = out_dir / "dim.mjpg"
    with open(clip, "rb") as f, mmap.mmap(f.fileno(), 0, access=mmap.ACCESS_READ) as mm, open(dst, "wb") as out:
        for a, b in ranges:
            bgr = cv2.imdecode(np.frombuffer(mm[a:b], np.uint8), cv2.IMREAD_COLOR)
            out.write(dim_frame(bgr, rng))
    # the capture times, in the camera log's format
    (out_dir / "dim.log").write_text("".join(f"idx: 0 ts: {1000.0 + t:.6f}\n" for t in times))
    print(f"{dst}: {len(ranges)} frames", flush=True)


# ----------------------------------------------------------------------------------------------------------- filters
class Filter:
    """One picture filter (some keep state from frame to frame)."""

    def __init__(self, name):
        self.name = name
        self.prev = None
        self.clahe = cv2.createCLAHE(clipLimit=4.0 if name == "clahe4" else 2.0, tileGridSize=(8, 8))
        self.gamma = np.array([255 * (i / 255) ** 0.6 for i in range(256)], np.uint8)

    def __call__(self, bgr):
        n = self.name
        if n in ("none", "twin"):
            return bgr
        if n == "control":                     # re-saved as JPEG 95: a change nobody can see (how much the numbers
            ok, jpeg = cv2.imencode(".jpg", bgr, [cv2.IMWRITE_JPEG_QUALITY, 95])   # move by chance)
            return cv2.imdecode(jpeg, cv2.IMREAD_COLOR)
        if n == "gamma":                       # brighten the shadows and mid-tones
            return cv2.LUT(bgr, self.gamma)
        if n == "levels":                      # each channel stretched from its 1st to 99th percentile
            small = bgr[::4, ::4].reshape(-1, 3).astype(np.float32)
            lo, hi = np.percentile(small, 1, axis=0), np.percentile(small, 99, axis=0)
            scale = 255.0 / np.maximum(hi - lo, 1)
            return np.clip((bgr.astype(np.float32) - lo) * scale, 0, 255).astype(np.uint8)
        if n in ("clahe", "clahe4"):           # local contrast on the lightness only
            return self.local_contrast(bgr)
        if n == "blur":
            return cv2.GaussianBlur(bgr, (5, 5), 1.0)
        if n == "bilateral":                   # smooths flat areas, keeps edges
            return cv2.bilateralFilter(bgr, 7, 30, 5)
        if n == "median":
            return cv2.medianBlur(bgr, 3)
        if n in ("temporal", "temporal+clahe"):
            out = self.temporal(bgr)
            return self.local_contrast(out) if n == "temporal+clahe" else out
        if n == "sharpen":                     # unsharp mask
            return cv2.addWeighted(bgr, 2.0, cv2.GaussianBlur(bgr, (0, 0), 1.5), -1.0, 0)
        if n == "edges":                       # the picture replaced by its edges (Canny)
            gray = cv2.GaussianBlur(cv2.cvtColor(bgr, cv2.COLOR_BGR2GRAY), (5, 5), 1.2)
            return cv2.cvtColor(cv2.Canny(gray, 30, 90), cv2.COLOR_GRAY2BGR)
        if n == "edgeboost":                   # the edges drawn over the picture (Sobel magnitude added)
            gray = cv2.cvtColor(bgr, cv2.COLOR_BGR2GRAY).astype(np.float32)
            mag = cv2.magnitude(cv2.Sobel(gray, cv2.CV_32F, 1, 0), cv2.Sobel(gray, cv2.CV_32F, 0, 1))
            return np.clip(bgr.astype(np.float32) + 0.5 * mag[..., None], 0, 255).astype(np.uint8)
        if n == "gray":
            return cv2.cvtColor(cv2.cvtColor(bgr, cv2.COLOR_BGR2GRAY), cv2.COLOR_GRAY2BGR)
        raise ValueError(f"unknown filter {n}")

    def local_contrast(self, bgr):
        lab = cv2.cvtColor(bgr, cv2.COLOR_BGR2LAB)
        lab[..., 0] = self.clahe.apply(lab[..., 0])
        return cv2.cvtColor(lab, cv2.COLOR_LAB2BGR)

    def temporal(self, bgr):
        """Vishu!'s mirror denoise (Hidden/Tundu/MirrorPrep): a still pixel keeps 62 % of the last result, a moving one
        (a change above 0.10 in sqrt space) takes the new frame at once."""
        cur = bgr.astype(np.float32) / 255.0
        if self.prev is None:
            self.prev = cur
            return bgr
        d = np.abs(np.sqrt(cur) - np.sqrt(self.prev)).max(axis=2, keepdims=True)
        x = np.clip((d - 0.035) / (0.10 - 0.035), 0, 1)
        keep = 0.62 * (1 - x * x * (3 - 2 * x))
        out = cur + (self.prev - cur) * keep
        self.prev = out
        return (out * 255 + 0.5).astype(np.uint8)


# ------------------------------------------------------------------------------------------------------- the models
def landmarker(model, min_detection=0.5):
    import mediapipe as mp  # noqa: F401  (each worker process loads its own)
    from mediapipe.tasks import python as mp_python
    from mediapipe.tasks.python import vision
    from pose import model_path
    options = vision.PoseLandmarkerOptions(
        base_options=mp_python.BaseOptions(model_asset_path=str(model_path(model))),
        running_mode=vision.RunningMode.VIDEO, num_poses=1, min_pose_detection_confidence=min_detection,
        min_pose_presence_confidence=0.5, min_tracking_confidence=0.5, output_segmentation_masks=False)
    return vision.PoseLandmarker.create_from_options(options)


def detect(lmk, rgb, t_ms):
    """The first person's COCO-17 joints (x, y in 0..1, z, visibility) and world joints, or None."""
    import mediapipe as mp
    from skeleton import MEDIAPIPE_TO_COCO17
    result = lmk.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), t_ms)
    if not result.pose_landmarks:
        return None, None
    lm = result.pose_landmarks[0]
    k = np.array([[lm[i].x, lm[i].y, lm[i].z, lm[i].visibility or 0.0] for i in MEDIAPIPE_TO_COCO17], np.float32)
    w = None
    if result.pose_world_landmarks:
        wl = result.pose_world_landmarks[0]
        w = np.array([[wl[i].x, wl[i].y, wl[i].z] for i in MEDIAPIPE_TO_COCO17], np.float32)
    return k, w


def run_one(job):
    clip, flt, model, out_path, limit = job
    ranges, times = index(clip)
    if limit:
        ranges, times = ranges[:limit], times[:limit]
    find_only = flt.startswith("find-")
    lmk = landmarker(model, 0.3 if flt == "lowdet" else 0.5)
    twin = landmarker(model) if flt == "twin" else None
    f = Filter("none" if flt == "lowdet" else flt[5:] if find_only else flt)
    following = False
    n = len(ranges)
    kp = np.full((n, 17, 4), np.nan, np.float32)
    world = np.full((n, 17, 3), np.nan, np.float32)
    spent = 0.0
    width = height = 0
    with open(clip, "rb") as fh, mmap.mmap(fh.fileno(), 0, access=mmap.ACCESS_READ) as mm:
        for i, ((a, b), t) in enumerate(zip(ranges, times)):
            bgr = cv2.imdecode(np.frombuffer(mm[a:b], np.uint8), cv2.IMREAD_COLOR)
            height, width = bgr.shape[:2]
            rgb = cv2.cvtColor(bgr if find_only and following else f(bgr), cv2.COLOR_BGR2RGB)
            t_ms = int(round(t * 1000)) + i          # strictly increasing
            t0 = time.perf_counter()
            k, w = detect(lmk, rgb, t_ms)
            if twin is not None:                     # the same frame mirrored, its answer mirrored back and averaged
                k2, w2 = detect(twin, np.ascontiguousarray(rgb[:, ::-1]), t_ms)
                if k2 is not None:
                    k2 = k2[FLIP].copy()
                    k2[:, 0] = 1 - k2[:, 0]
                    if w2 is not None:
                        w2 = w2[FLIP].copy()
                        w2[:, 0] = -w2[:, 0]
                    if k is None:
                        k, w = k2, w2
                    else:
                        k = (k + k2) / 2
                        w = (w + w2) / 2 if w is not None and w2 is not None else w
            spent += time.perf_counter() - t0
            following = k is not None
            if k is not None:
                kp[i] = k
                if w is not None:
                    world[i] = w
    lmk.close()
    if twin is not None:
        twin.close()
    kp[..., 0] *= width
    kp[..., 1] *= height
    np.savez_compressed(out_path, t=np.array(times), kp=kp, world=world, size=np.array([width, height]))
    found = np.isfinite(kp[:, 0, 0]).mean() * 100
    return f"{out_path.name}: {n} frames, a person in {found:.1f} %, {1000 * spent / n:.1f} ms a frame"


# ---------------------------------------------------------------------------------------------------------- scoring
def smooth(t, a, sigma=0.1):
    """Each joint's path smoothed both ways in time (a centred Gaussian on the capture times; NaN = no sample)."""
    out = np.full_like(a, np.nan)
    ok = np.isfinite(a[:, 0])
    tt, aa = t[ok], a[ok]
    lo = np.searchsorted(tt, t - 3 * sigma, side="left")
    hi = np.searchsorted(tt, t + 3 * sigma, side="right")
    for i in np.flatnonzero(ok):
        w = np.exp(-0.5 * ((tt[lo[i]:hi[i]] - t[i]) / sigma) ** 2)
        out[i] = (w[:, None] * aa[lo[i]:hi[i]]).sum(0) / w.sum()
    return out


GROUPS = {"wrists": (9, 10), "elbows": (7, 8), "shoulders": (5, 6), "hips": (11, 12), "nose": (0,), "knees": (13, 14)}


def load(path):
    d = np.load(path)
    return d["t"], d["kp"], d["world"]


def torso_px(kp):
    sh = kp[:, 5:7, :2].mean(1)
    hp = kp[:, 11:13, :2].mean(1)
    return float(np.nanmedian(np.linalg.norm(sh - hp, axis=1)))


def paths(t, kp):
    return {j: smooth(t, kp[:, j, :2]) for j in range(17)}


def score_run(t, kp, world, size, ref_paths, same=None):
    """same: the frames where the unfiltered run on this clip found the person too (errors there compare like with like:
    a run that finds the person sooner is otherwise also scored on frames the unfiltered run never had)."""
    n = len(t)
    minutes = (t[-1] - t[0]) / 60
    found = np.isfinite(kp[:, 0, 0])
    r = {"lost %": 100 * (1 - found.mean())}
    episodes = np.sum(np.diff(found.astype(int)) == -1) + (0 if found[0] else 1)
    r["losses"] = int(episodes)
    r["first found s"] = float(t[np.argmax(found)] - t[0]) if found.any() else float("nan")
    # one-frame spikes (model_errors.py's rule), per minute, wrists and elbows
    for name, joints in (("wrist spikes/min", (9, 10)), ("elbow spikes/min", (7, 8))):
        c = 0
        for j in joints:
            p = kp[:, j, :2]
            a, b, cc = p[:-2], p[1:-1], p[2:]
            out, back, net = (np.linalg.norm(b - a, axis=1), np.linalg.norm(cc - b, axis=1),
                              np.linalg.norm(cc - a, axis=1))
            ok = np.isfinite(out) & np.isfinite(back)
            c += int(np.sum(ok & (out > 0.1 * size) & (back > 0.1 * size) & (net < 0.5 * np.minimum(out, back))))
        r[name] = c / len(joints) / minutes
    # 3D forearms more than 25 % off their median length
    offs = []
    for a, b in ((7, 9), (8, 10)):
        length = np.linalg.norm(world[:, a] - world[:, b], axis=1)
        ok = np.isfinite(length)
        if ok.sum() > 10:
            offs.append(np.mean(np.abs(length[ok] / np.median(length[ok]) - 1) > 0.25) * 100)
    r["3D forearm off %"] = float(np.mean(offs)) if offs else float("nan")
    # unsure wrists
    vis = kp[:, (9, 10), 3]
    r["wrist unsure %"] = float(np.nanmean(vis < 0.5) * 100) if np.isfinite(vis).any() else float("nan")
    own = paths(t, kp)
    for g, joints in GROUPS.items():
        shake, calm, err, big = [], [], [], []
        for j in joints:
            p = kp[:, j, :2]
            s = np.linalg.norm(p - own[j], axis=1) / size * 100
            shake.append(s[np.isfinite(s)])
            if ref_paths is not None:
                # calm frames: the reference joint moving slower than 0.6 torso lengths a second (the same frames for
                # every run), where what's left around the run's own path is jitter, not fast movement
                speed = np.full(n, np.nan)
                dt = np.diff(t)
                speed[1:] = np.linalg.norm(np.diff(ref_paths[j], axis=0), axis=1) / np.maximum(dt, 1e-3) / size
                c = s[np.isfinite(s) & (speed < 0.6)]
                calm.append(c)
            if ref_paths is not None:
                e = np.linalg.norm(p - ref_paths[j], axis=1) / size * 100
                e = e[np.isfinite(e)]
                err.append(e)
        s = np.concatenate(shake)
        r[f"{g} shake"] = float(np.sqrt(np.mean(s ** 2))) if len(s) else float("nan")
        if calm:
            c = np.concatenate(calm)
            r[f"{g} shake calm"] = float(np.sqrt(np.mean(c ** 2))) if len(c) > 30 else float("nan")
        if ref_paths is not None:
            e = np.concatenate(err)
            r[f"{g} err med"] = float(np.median(e)) if len(e) else float("nan")
            r[f"{g} err p90"] = float(np.percentile(e, 90)) if len(e) else float("nan")
            r[f"{g} err >20%"] = float(np.mean(e > 20) * 100) if len(e) else float("nan")
            if same is not None:
                es = np.concatenate([np.linalg.norm(kp[same, j, :2] - ref_paths[j][same], axis=1) / size * 100
                                     for j in joints])
                es = es[np.isfinite(es)]
                r[f"{g} err med same"] = float(np.median(es)) if len(es) else float("nan")
                r[f"{g} err >20% same"] = float(np.mean(es > 20) * 100) if len(es) else float("nan")
    return r


def score(out_dir, json_path=None):
    runs = sorted(out_dir.glob("*.npz"))
    by = {}
    for p in runs:
        clip, flt, model = p.stem.rsplit(".", 2)
        by[(clip, flt, model)] = p
    clips = sorted({c for c, _, _ in by})
    base = next(c for c in clips if c != "dim")
    ref_key = (base, "none", "full")
    t, kp_ref, _ = load(by[ref_key])
    size = torso_px(kp_ref)
    ref_full = paths(t, kp_ref)
    model_refs = {}
    results = []
    for clip in clips:
        for model in ("lite", "full"):
            for flt in FILTERS:
                key = (clip, flt, model)
                if key not in by:
                    continue
                t, kp, world = load(by[key])
                if clip == base:
                    ref = ref_full
                else:   # a dimmer copy: against the same model on the original clip
                    if model not in model_refs:
                        tr, kr, _ = load(by[(base, "none", model)])
                        model_refs[model] = paths(tr, kr)
                    ref = model_refs[model]
                base_kp = load(by[(clip, "none", model)])[1] if (clip, "none", model) in by else kp
                same = np.isfinite(base_kp[:, 0, 0]) & np.isfinite(kp[:, 0, 0])
                r = score_run(t, kp, world, size, ref, same)
                r.update({"clip": clip, "filter": flt, "model": model})
                results.append(r)
    cols = ["lost %", "first found s", "wrist spikes/min", "3D forearm off %", "wrists shake calm", "wrists err med",
            "wrists err p90", "wrists err >20%", "elbows err med", "shoulders err med", "hips err med", "nose err med"]
    print(f"torso {size:.0f} px; reference: the full model on the unfiltered clip, smoothed both ways "
          f"(dim: the same model on the original clip)")
    print(f"{'clip':6s} {'model':5s} {'filter':15s} " + " ".join(f"{c[:16]:>16s}" for c in cols))
    for r in results:
        print(f"{r['clip'][:6]:6s} {r['model']:5s} {r['filter']:15s} " +
              " ".join(f"{r.get(c, float('nan')):16.2f}" for c in cols))
    if json_path:
        Path(json_path).write_text(json.dumps({"torso_px": size, "runs": results}, indent=1))


# -------------------------------------------------------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("action", choices=["dim", "run", "score"])
    ap.add_argument("clip", type=Path, nargs="?")
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--filters", default=",".join(FILTERS))
    ap.add_argument("--models", default="lite,full")
    ap.add_argument("--jobs", type=int, default=3)
    ap.add_argument("--json", type=Path)
    ap.add_argument("--limit", type=int, default=0, help="only the first N frames (a quick check)")
    args = ap.parse_args()
    if args.action == "dim":
        make_dim(args.clip, args.out)
        return
    if args.action == "score":
        score(args.out, args.json)
        return
    args.out.mkdir(parents=True, exist_ok=True)
    stem = args.clip.stem
    jobs = []
    for model in args.models.split(","):
        for flt in args.filters.split(","):
            path = args.out / f"{stem}.{flt}.{model}.npz"
            if not path.exists():
                jobs.append((args.clip, flt, model, path, args.limit))
    # the slow ones first so the pool ends together
    jobs.sort(key=lambda j: (j[1] != "twin", j[2] != "full"))
    print(f"{len(jobs)} runs, {args.jobs} at a time", flush=True)
    with Pool(args.jobs) as pool:
        for line in pool.imap_unordered(run_one, jobs):
            print(line, flush=True)


if __name__ == "__main__":
    main()
