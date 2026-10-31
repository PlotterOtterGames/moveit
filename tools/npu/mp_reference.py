"""MediaPipe's own answer for still pictures: the pose landmarker in IMAGE mode (detector + landmarks, no smoothing),
saved as JSON for compare.py. Runs with the PC service's venv (it has MediaPipe):
    motion-service-py/.venv/Scripts/python tools/npu/mp_reference.py <out.json> [--frames=2] <picture> [<picture> ...]
"""
import json
import os
import sys

import cv2
import mediapipe as mp

MODELS = os.path.join(os.path.dirname(__file__), "..", "..", "motion-service-py", "models")


def landmarks(path, model, frames):
    """frames 1: IMAGE mode (the detector, then the landmarks). frames 2: VIDEO mode, the picture twice, the second
    result (the crop then comes from the first frame's landmarks, as while tracking)."""
    mode = mp.tasks.vision.RunningMode.IMAGE if frames == 1 else mp.tasks.vision.RunningMode.VIDEO
    options = mp.tasks.vision.PoseLandmarkerOptions(
        base_options=mp.tasks.BaseOptions(model_asset_path=os.path.join(MODELS, f"pose_landmarker_{model}.task")),
        running_mode=mode, num_poses=1)
    with mp.tasks.vision.PoseLandmarker.create_from_options(options) as landmarker:
        # decoded by OpenCV, like compare.py, so both see exactly the same pixels
        image = mp.Image(image_format=mp.ImageFormat.SRGB, data=cv2.cvtColor(cv2.imread(path), cv2.COLOR_BGR2RGB))
        if frames == 1:
            result = landmarker.detect(image)
        else:
            for f in range(frames):
                result = landmarker.detect_for_video(image, f * 33)
        if not result.pose_landmarks:
            return None
        return dict(
            size=[image.width, image.height],
            landmarks=[[p.x, p.y, p.z, p.visibility, p.presence] for p in result.pose_landmarks[0]],
            world=[[p.x, p.y, p.z] for p in result.pose_world_landmarks[0]])


def main():
    out = {}
    frames = 1
    args = sys.argv[2:]
    if args and args[0].startswith("--frames="):
        frames = int(args.pop(0).split("=")[1])
    for path in args:
        out[os.path.basename(path)] = {model: landmarks(path, model, frames) for model in ("lite", "full")}
        found = {m: v is not None for m, v in out[os.path.basename(path)].items()}
        print(os.path.basename(path), found)
    with open(sys.argv[1], "w") as f:
        json.dump(out, f)


if __name__ == "__main__":
    main()
