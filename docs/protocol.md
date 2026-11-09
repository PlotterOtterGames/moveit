# Motion Protocol v1.1

The **motion service** (camera → pose model) sends one UDP datagram per processed camera frame
to the **game app** (Unity MoveSDK). Default target: `127.0.0.1:5005`.

The same protocol is used by every backend:

| Backend | Where | Status |
|---|---|---|
| `py-mediapipe-lite` / `py-mediapipe-full` | PC dev (`motion-service-py/main.py`) | Phase 0 |
| `py-fake` | Synthetic test skeleton (`motion-service-py/fake_sender.py`) | Phase 0 |
| `py-replay` | Replays a recorded `.jsonl` session (`motion-service-py/replay.py`) | Phase 0 |
| `py-mediapipe-full` / `-lite` | PC dev service, `--model auto` picks full and drops to lite if too slow | v1.1 |
| `android-mediapipe-gpu-full` (or `-cpu-`, `-lite`) | Android: Java library inside the APK (`Assets/Plugins/Android/MoveItMotion.androidlib`) | v1.1 |
| `android-rknn-full` / `-lite` | Android on a Rockchip NPU: MediaPipe's models converted to RKNN, run by the same Java library's native engine (`src/main/cpp/pose_npu.cpp`), same landmarks and smoothing; used first where the APK has models for the chip | v1.1 (2026-10-30, RK3566) |

## Datagram

UTF-8 JSON, one object per datagram, no newline. A frame is sent **even when nobody is visible**
(`people: []`) so the game knows the service is alive.

```json
{
  "v": 1,
  "src": "py-mediapipe-lite",
  "t": 1791034567123,
  "f": 1024,
  "w": 1280,
  "h": 720,
  "fps": 29.8,
  "inf": 11.2,
  "cam": 30.0,
  "model": "full",
  "people": [
    { "id": 0, "score": 0.93, "k": ["... 17 joints x [x, y, z, visibility] = 68 numbers"],
      "wk": ["... 17 joints x [x, y, z] in metres = 51 numbers"] }
  ]
}
```

| Field | Type | Meaning |
|---|---|---|
| `v` | int | Protocol version (`1`). Receivers ignore datagrams with an unknown major version. |
| `src` | string | Backend id (see table above). |
| `t` | int64 | Capture time, **Unix epoch milliseconds** (wall clock). Sender and receiver run on the same device, so `now - t` = pipeline latency. |
| `f` | int64 | Frame counter, increases by 1 per sent frame. |
| `w`, `h` | int | Camera image size in pixels. **Needed to undo the aspect ratio** (x and y are normalized separately). |
| `fps` | float | Pipeline throughput (frames processed per second). |
| `inf` | float | Pose inference time for this frame, ms. |
| `people` | array | Detected people, best first. Maximum set by the service (`--num-poses`; on the box the game asks: `MotionService.setPeople`). |
| `people[].id` | int | Index within **this** frame only. Not stable across frames. Stable tracking is the SDK's job. |
| `people[].tid` | int | *v1.1, optional (2026-11-05).* The service's track: the same id while it follows this person, a new one after it lost them (0 or missing = not known: the PC service, MediaPipe gives none). The box's NPU engine follows up to two people and, sharing its NPU, runs the pose model on them in turns, so **a packet may carry only one of two people**; the SDK matches bodies by `tid` first (`BodyTracker`), by position only for people without one or after a loss. |
| `people[].score` | float | 0..1 confidence (mean visibility of the torso joints). |
| `people[].k` | float[68] | 17 joints × `[x, y, z, visibility]`, flat, in the joint order below. |
| `cam` | float | *v1.1, optional.* Frames per second the camera actually delivers (a dim room lowers it). |
| `model` | string | *v1.1, optional.* Pose model in use: `full`, `lite`, `fake`. |
| `people[].wk` | float[51] | *v1.1, optional.* 17 joints × `[x, y, z]` **world** coordinates in metres, origin between the hips, MediaPipe axes (x image-right, y down, z toward the camera = negative). Real 3D depth, much more reliable than `k`'s z for "in front of / behind the body". |
| `people[].hk` | float[24] | *v1.1, optional.* The pose model's hand points, 6 × `[x, y, z, visibility]` like `k`: left pinky, left index, left thumb, right pinky, right index, right thumb (MediaPipe landmarks 17, 19, 21, 18, 20, 22). Sent by the PC service since 2026-10-19 so recordings can show whether a palm point is steadier than the wrist pushed along the forearm (`tools/motion/cursor_sim.py --recording ... --palm`); games don't use it yet. |

v1.1 only **adds** optional fields; v1 receivers ignore them and v1 senders still work (the game falls back to
image-space depth when `wk` is missing). `v` stays `1`.

### Coordinates (raw camera space, not mirrored)
- `x`: 0 = left edge of the camera image, 1 = right edge
- `y`: 0 = top edge, 1 = bottom edge
- `z`: relative depth, roughly the same scale as `x`. Smaller = closer to the camera. Origin at the hip center.
- `visibility`: 0..1, likelihood that the joint is visible (not occluded or out of frame)

The camera faces the player, so the player's **left** side appears on the image's **right**.
The SDK mirrors x so the screen behaves like a mirror.

## Joint order (COCO-17)

| # | Joint | # | Joint |
|---|---|---|---|
| 0 | nose | 9 | left_wrist |
| 1 | left_eye | 10 | right_wrist |
| 2 | right_eye | 11 | left_hip |
| 3 | left_ear | 12 | right_hip |
| 4 | right_ear | 13 | left_knee |
| 5 | left_shoulder | 14 | right_knee |
| 6 | right_shoulder | 15 | left_ankle |
| 7 | left_elbow | 16 | right_ankle |
| 8 | right_elbow | | |

"Left" and "right" are the **player's** anatomical sides. MediaPipe's 33 landmarks map onto these as
`[0, 2, 5, 7, 8, 11, 12, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28]`. YOLO-pose already outputs this order.

## Size and rate
About 650 bytes per person (~1.3 KB with `wk` and `hk`) at 30-60 fps, well under one UDP datagram (no fragmentation). A binary v2 can
replace JSON later if profiling on the box shows parsing cost.

**Two people on the box** (2026-11-05): with two followed, the NPU's landmark runs are shared (the game's budget, e.g. 30
a second: each person ~15 poses a second, every packet carries the one whose turn it was, with its own capture time `t`);
the person detector looks for a missing second person every 0.5 s. Each person's samples then come ~66 ms apart, and the
SDK's interpolation on the camera clock (`HandInterpolator`, `JointTimeline`) draws them smoothly between. The PC service
sends both people in every packet (`main.py --num-poses 2`); `fake_sender.py --script tennis_duo [--alternate]` plays two
scripted people (`--alternate`: one per packet, with track ids, like the box).

## Camera preview stream (v1.1, optional)

For games that show the player (Pikturo's camera mode), the motion service can also send the **camera picture**.
It only does so while a game asks for it, and only to this device (`127.0.0.1`): the picture never leaves the box
and is not stored by the service.

**Asking for frames.** The game repeats its request about twice a second while it wants frames; the service stops by
itself 2–3 s after the last one.
- PC service (`motion-service-py/video.py`, also `fake_sender.py`): UTF-8 JSON datagram to UDP `127.0.0.1:5007`
  (`--video-control-port`, 0 = off): `{"video":1,"port":5006,"width":640}`; `{"video":0}` stops at once.
- Android library: `MotionService.setVideo(port, width, on)` through JNI (`VideoStreamer.java`).

**Frames.** One UDP datagram per frame to `127.0.0.1:<port>` (default 5006), at most 60,000 bytes (no fragmentation;
the JPEG quality drops until it fits):

| Bytes | Field |
|---|---|
| 0–3 | magic `TVF1` |
| 4–11 | int64 capture time, Unix ms (same clock and frame as the motion packet's `t`) |
| 12–15 | uint32 frame number |
| 16–17, 18–19 | uint16 width, height |
| 20– | JPEG |

All little-endian. The image is the **raw camera view, not mirrored**, scaled to the requested width: the same
space as the joints `k`. In Unity (`MoveSDK/Runtime/CameraFeed.cs`) a view-space point `(x, y)` of a `Body` is at
texture `u = 1 - x / aspect, v = y`. Typical size: 640×360 (PC) or 640×480 (Android), 12–40 KB, 30 fps;
decoding takes ~1 ms on the laptop.

**On the box: raw frames inside the process (2026-10-31).** On a Cortex-A55 the JPEG round trip was the expensive
part (the encode ~9 % of the app's CPU in Java, the decode ~15 ms of every frame on Unity's main thread: Vishu! ran
23.5 fps). So when the Android library can read CameraX's RGBA buffer as it is (no rotation, 4 bytes per pixel) it
doesn't send datagrams: it scales the newest frame to the requested width into a buffer in `libtundupose.so`
(`FrameShare.java` → `src/main/cpp/frame_share.cpp`), and the game copies it straight into its texture with two C
calls (`[DllImport("tundupose")]` in `CameraFeed.cs`):

- `int64 tundu_frame_info(int32* width, int32* height, int64* time)`: the newest frame's number (0 = none yet), size
  and capture time (Unix ms, as above).
- `int64 tundu_frame_copy(uint8* dst, int32 capacity, int32* width, int32* height, int64* time)`: copies that frame
  (RGBA, 4 bytes per pixel, **rows bottom-up** as Unity textures want them) if it fits; returns its number or 0.

The same request (`setVideo`) turns it on and off, and the UDP stream above stays the fallback (a rotated camera, a
build without the library); `CameraFeed` takes whichever brings frames.
