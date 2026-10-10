"""COCO-17 skeleton definition shared by every sender (see docs/protocol.md)."""

JOINT_NAMES = [
    "nose",
    "left_eye", "right_eye",
    "left_ear", "right_ear",
    "left_shoulder", "right_shoulder",
    "left_elbow", "right_elbow",
    "left_wrist", "right_wrist",
    "left_hip", "right_hip",
    "left_knee", "right_knee",
    "left_ankle", "right_ankle",
]
JOINT_COUNT = len(JOINT_NAMES)
J = {name: i for i, name in enumerate(JOINT_NAMES)}

# MediaPipe Pose (33 landmarks) index for each COCO-17 joint.
MEDIAPIPE_TO_COCO17 = [0, 2, 5, 7, 8, 11, 12, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28]

BONES = [
    (J["left_shoulder"], J["right_shoulder"]),
    (J["left_shoulder"], J["left_elbow"]), (J["left_elbow"], J["left_wrist"]),
    (J["right_shoulder"], J["right_elbow"]), (J["right_elbow"], J["right_wrist"]),
    (J["left_shoulder"], J["left_hip"]), (J["right_shoulder"], J["right_hip"]),
    (J["left_hip"], J["right_hip"]),
    (J["left_hip"], J["left_knee"]), (J["left_knee"], J["left_ankle"]),
    (J["right_hip"], J["right_knee"]), (J["right_knee"], J["right_ankle"]),
    (J["nose"], J["left_eye"]), (J["nose"], J["right_eye"]),
    (J["left_eye"], J["left_ear"]), (J["right_eye"], J["right_ear"]),
]

TORSO = [J["left_shoulder"], J["right_shoulder"], J["left_hip"], J["right_hip"]]


class Person:
    """One detected person: 17 joints as (x, y, z, visibility) in raw normalized camera coords, plus optional
    world joints (x, y, z) in metres, origin at the hips, MediaPipe axes (x image-right, y down, z toward camera = -)."""

    __slots__ = ("joints", "world", "score")

    def __init__(self, joints, score=None, world=None):
        self.joints = joints
        self.world = world
        self.score = score if score is not None else sum(joints[i][3] for i in TORSO) / len(TORSO)
