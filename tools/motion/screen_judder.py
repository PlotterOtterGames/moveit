"""How evenly the skeleton moves on the TV: a screen recording of the camera test screen (Settings → Kamera, the
full-screen skeleton) taken on the box with `adb shell screenrecord`, read frame by frame.

1. When new pictures reached the screen: the video's frame times (screenrecord writes a frame only when the screen
   changed). At 60 Hz every frame should follow the last after 16.7 ms; a frame held for two or three refreshes shows
   the skeleton standing still that long and then jumping on (judder).
2. How the drawn joints moved: the white joint squares found in each frame (outside the text panels), matched to the
   previous frame's, their step against the time between the frames. Per pair of frames the fastest joint's speed; a
   "stall" = a frame where that joint barely moved while it moved in the frames around it, a "lurch" = a step much
   bigger than its neighbours' for the time.

    motion-service-py/.venv/Scripts/python tools/motion/screen_judder.py <screen.mp4> [--from S] [--to S]
"""
import argparse
import subprocess

import cv2
import numpy as np

ROI = (250, 140, 1300, 1040)   # x0, y0, x1, y1: the skeleton's area, the text panels left out (1920 x 1080 screen)


def frame_times(path):
    out = subprocess.run(['ffprobe', '-v', 'error', '-select_streams', 'v', '-show_entries', 'frame=pts_time',
                          '-of', 'csv=p=0', path], capture_output=True, text=True, check=True).stdout
    return np.array([float(x) for x in out.split()])


def joints(frame):
    x0, y0, x1, y1 = ROI
    roi = frame[y0:y1, x0:x1]
    white = (roi.min(axis=2) > 225).astype(np.uint8)
    n, _, stats, cent = cv2.connectedComponentsWithStats(white)
    keep = [(cent[i][0] + x0, cent[i][1] + y0) for i in range(1, n) if 40 <= stats[i, cv2.CC_STAT_AREA] <= 400]
    return np.array(keep).reshape(-1, 2)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('video')
    ap.add_argument('--from', dest='start', type=float, default=0)
    ap.add_argument('--to', dest='end', type=float, default=1e9)
    args = ap.parse_args()
    t = frame_times(args.video)
    dt = np.diff(t) * 1000
    print(f'{len(t)} frames over {t[-1] - t[0]:.1f} s: {len(t) / (t[-1] - t[0]):.1f} new pictures a second')
    for lo, hi, name in ((0, 20, '1 refresh (<= 20 ms)'), (20, 37, '2 refreshes (~33 ms)'), (37, 55, '3 (~50 ms)'),
                         (55, 1e9, '4 or more (> 55 ms)')):
        print(f'  {name:22s} {np.mean((dt > lo) & (dt <= hi)) * 100:5.1f} % of the gaps')
    # the skeleton's steps
    cap = cv2.VideoCapture(args.video)
    prev, rows, i = None, [], 0
    while True:
        ok, frame = cap.read()
        if not ok or i >= len(t):
            break
        if args.start <= t[i] <= args.end:
            pts = joints(frame)
            if prev is not None and len(pts) >= 8 and len(prev[1]) >= 8:
                d = np.linalg.norm(pts[:, None, :] - prev[1][None, :, :], axis=2)
                step = d.min(axis=1)              # each joint to the nearest joint of the previous frame
                gap = t[i] - prev[0]
                rows.append((t[i], gap, np.max(step[step < 150]) if np.any(step < 150) else 0.0))
            prev = (t[i], pts)
        i += 1
    rows = np.array(rows)
    if len(rows) < 10:
        print('no skeleton found')
        return
    tt, gap, step = rows.T
    speed = step / gap                            # px per second, the fastest joint
    moving = speed > 150                          # the skeleton visibly moving
    print(f'skeleton found in {len(rows)} frame pairs; moving in {moving.mean() * 100:.0f} %')
    # stalls and lurches: the step against the steps before and after, scaled by the time each covered
    stall = lurch = 0
    n = 0
    for k in range(1, len(rows) - 1):
        if not (moving[k - 1] and moving[k + 1]):
            continue
        n += 1
        around = (speed[k - 1] + speed[k + 1]) / 2
        if speed[k] < 0.35 * around:
            stall += 1
        elif speed[k] > 2.0 * around:
            lurch += 1
    print(f'  while moving: {n} frames; stalls (the joint stopped for a frame) {stall / max(n, 1) * 100:.1f} %,'
          f' lurches (> 2x the speed around it) {lurch / max(n, 1) * 100:.1f} %')
    mg = gap[moving] * 1000
    print(f'  frame gaps while moving: median {np.median(mg):.0f} ms, {np.mean(mg > 37) * 100:.0f} % longer than 2 refreshes')


if __name__ == '__main__':
    main()
