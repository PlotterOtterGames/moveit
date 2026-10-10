"""MediaPipe Pose Landmarker wrapper that outputs COCO-17 people."""

from pathlib import Path

import mediapipe as mp
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision

from skeleton import MEDIAPIPE_TO_COCO17, Person

MODELS_DIR = Path(__file__).parent / "models"


def model_path(name):
    """name: 'lite' or 'full' -> models/pose_landmarker_<name>.task"""
    path = MODELS_DIR / f"pose_landmarker_{name}.task"
    if not path.exists():
        raise FileNotFoundError(
            f"{path} not found. Download it from "
            f"https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_{name}/float16/latest/pose_landmarker_{name}.task"
        )
    return path


class PoseEstimator:
    def __init__(self, model="lite", num_poses=1, min_detection=0.5, min_presence=0.5, min_tracking=0.5):
        options = vision.PoseLandmarkerOptions(
            base_options=mp_python.BaseOptions(model_asset_path=str(model_path(model))),
            running_mode=vision.RunningMode.VIDEO,
            num_poses=num_poses,
            min_pose_detection_confidence=min_detection,
            min_pose_presence_confidence=min_presence,
            min_tracking_confidence=min_tracking,
            output_segmentation_masks=False,
        )
        self._landmarker = vision.PoseLandmarker.create_from_options(options)
        self._last_ts = -1

    def detect(self, rgb, timestamp_ms):
        """rgb: HxWx3 uint8 RGB image. timestamp_ms must increase every call (VIDEO mode)."""
        if timestamp_ms <= self._last_ts:
            timestamp_ms = self._last_ts + 1
        self._last_ts = timestamp_ms

        image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
        result = self._landmarker.detect_for_video(image, timestamp_ms)

        people = []
        world_all = result.pose_world_landmarks or []
        for n, landmarks in enumerate(result.pose_landmarks):
            joints = []
            for idx in MEDIAPIPE_TO_COCO17:
                lm = landmarks[idx]
                joints.append((lm.x, lm.y, lm.z, lm.visibility if lm.visibility is not None else 0.0))
            world = None
            if n < len(world_all):
                world = [(world_all[n][idx].x, world_all[n][idx].y, world_all[n][idx].z) for idx in MEDIAPIPE_TO_COCO17]
            people.append(Person(joints, world=world))
        people.sort(key=lambda p: p.score, reverse=True)
        return people

    def close(self):
        self._landmarker.close()
