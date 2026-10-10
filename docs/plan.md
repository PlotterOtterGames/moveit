# MoveIt (working name): camera motion-game box for Albanian families

## Context
Someone "playing" Subway Surfers on YouTube on a TV shows people want to play games with their body, without controllers. Kinect proved this, and in 2025 **Nex Playground** proved it again *without a depth sensor*: one normal camera plus on-device AI, $249, about 650k units sold. Nobody serves Albania with this, or Albanian-language content.

**Goal:** a cheap box that plugs into any TV over HDMI, with a USB camera on top of the TV, running our own motion games. Target retail **≤ €99**, sold to **families at home** in Albania (later Kosovo, North Macedonia and the diaspora).
**Constraints:** solo developer, **< €500 before first sales**, starting with only the laptop (Ryzen 7 7840HS, RTX 4060, 16 GB RAM, built-in camera). Unity 6000.3.9f1 with the Android module is already installed at `D:\Unity`, plus Python 3.12 and Git.

---

## 1. Key decisions

| Topic | Decision | Why |
|---|---|---|
| Sensing | **Normal RGB camera + AI pose estimation. No depth sensor.** | Nex proves it is enough commercially. Depth cameras cost €100+. |
| Box | **Rockchip Android TV box** (RK3566 cheap tier, or RK3576 with 6 TOPS NPU) | €25–90 retail, cheapest computer with HDMI, Wi-Fi, remote and case included. Can be white-labeled from Shenzhen ODMs. |
| Not Raspberry Pi | Rejected | Pi 5 4GB is now about $110 because of the RAM shortage. Body tracking runs at only about 6 fps on its CPU. Unity can't target Linux ARM without an enterprise licence. |
| Engine | **Unity 6 (URP, Android/IL2CPP/ARM64)** | Already installed with Android support. Free under $200k revenue. Cheap art on the Asset Store. Unity and Blender tools are connected here, so I can build scenes and models directly. |
| Pose AI | **MediaPipe Pose Landmarker (lite)**. Later an optional **RKNN NPU backend** (YOLO-pose from Rockchip's model zoo) | MediaPipe runs on PC (Python) and Android (GPU). The NPU backend frees the GPU on cheap boxes. |
| Architecture | **Thin "Motion Service" → UDP (localhost) → Unity "MoveSDK"** | One Unity code path on PC and on the box. The pose backend can be swapped without touching the games. |
| Skeleton format | **17 COCO-style joints** (our own format) | The common subset of MediaPipe (33 joints) and YOLO-pose (17), so backends stay swappable. |

---

## 2. Hardware plan

### 2.1 Camera
- USB UVC, **1080p30 or 720p60, MJPEG**, **~100–120° diagonal field of view** (wide, *not* fisheye). This keeps the full body in frame at **2–2.5 m**, typical for an Albanian living room.
- Low-light Sony sensor (IMX291/IMX307 class) if the price allows. Evening living-room light is the main tracking risk.
- Universal TV-top clip with tilt, plus a 1.5–3 m USB extension cable.
- Price: about €15–25 retail as a sample, €8–15 in bulk.

### 2.2 Box (pick after benchmarking in Phase 1)
| Tier | Chip | Retail sample | Bulk (est.) | Notes |
|---|---|---|---|---|
| A (target) | RK3566, 4GB/32GB, Android 11 | €30–45 | €22–35 | Mali-G52 GPU, weak 0.8 TOPS NPU. Must prove ≥25 fps pose *while* the game renders. |
| B (fallback) | RK3576, 4GB/32GB, Android 14 | €60–90 | €45–70 | 6 TOPS NPU, plenty of headroom, allows 2 players. |
- Avoid Allwinner H313/H616 and Amlogic S905W2 boxes (weak GPU, no NPU).
- **Security:** many cheap boxes ship with malware (BadBox 2.0). Keep samples on guest Wi-Fi. For production, require clean firmware from the ODM or flash a clean Rockchip Android image.
- Prices move with the 2025–26 DRAM shortage. Get written quotes before committing.

### 2.3 "Sensors at worst case" (only if testing proves they're needed)
1. **Low-light / IR camera + IR LED ring** (+€3–8) for dark rooms.
2. **BLE wristband accessory** (ESP32-C3 + IMU, about €5–8 BOM) for very fast moves (boxing, tennis). A paid accessory later.
3. Depth camera: **no**, too expensive.

### 2.4 Enclosure and product, by stage
| Stage | Units | What | Tooling cost |
|---|---|---|---|
| Pilot | 10–30 | Off-the-shelf box + webcam, our sticker logo, printed sleeve box, Albanian quick-start card. Optional 3D-printed TV mount (print shop in Tirana). | €0–30 |
| Batch 1 | 100–300 | ODM white-label: our logo on the shell, custom boot animation, clean firmware, our app preinstalled and auto-starting. Branded camera and retail packaging (typical MOQ 100–500, negotiate). | €0–300 |
| v2 | 1000+/yr | **All-in-one "bar" on top of the TV** (like Kinect/Nex): RK3566/RK3576 board + MIPI camera in one case, one HDMI cable + power. Designed in Fusion 360 / Onshape, 3D-print prototypes, then injection mold. | €3k–15k |

### 2.5 Unit economics (Batch 1, RK3566 tier, estimates)
Box €25–35 + camera €8–15 + cable, clip, packaging and manual €3–5. Add **~30% for shipping, customs and 20% VAT** (confirm with an *agjent doganor*). Landed cost is about **€50–70**. **Retail €89–99** leaves about €25–40 margin. Nex costs $249, so our price is a strong hook.

---

## 3. Software architecture

```
[USB camera] → MOTION SERVICE ──UDP 127.0.0.1:5005 (JSON v1)──▶ UNITY APP
               capture → pose model → 17-joint                  MoveSDK (C#): receive → One-Euro smoothing →
               skeleton(s) → send                               main-player selection → body calibration →
               PC:  Python + OpenCV + MediaPipe                 gestures (lean L/R, jump, crouch, hands-up, swipe)
               Box: Kotlin AAR inside the same APK              + hand pointer
                    (CameraX/Camera2, libuvc fallback;          Games: Runner, Slash, Goalkeeper
                     MediaPipe GPU, later RKNN NPU)             Shell: onboarding, menu (TV remote + raise-hand), updates
```

- **Protocol v1** (one UDP datagram per frame, documented in `docs/protocol.md`):
  `{"v":1,"t":<ms>,"f":<frame>,"fps":28.4,"people":[{"id":0,"score":0.93,"k":[[x,y,z,vis] ×17]}]}`
  x and y are normalized raw camera coordinates (the SDK mirrors them). z is relative depth.
- **Gestures use body-relative thresholds.** Calibration at game start records hip height, shoulder width and torso length, so the same code works for a 6-year-old and an adult at any distance. Every gesture has hysteresis and a cooldown to prevent double triggers.
  - Lean/step: hip-center X offset > k × shoulder width
  - Jump: hip rises > k × torso length with upward velocity
  - Crouch: hip/head drops > k × torso length
  - Swipe: wrist speed above a threshold
- **Input abstraction `IGameInput`:** `MotionInput` and `KeyboardInput`, so every game can be tested without a camera.
- **Latency budget, target < 150 ms end-to-end:** camera ~40 ms + inference ~30–50 ms + game frame ~33 ms + TV lag (onboarding tells users to turn on the TV's "Game mode"). Games use generous timing windows and show obstacles early.
- **Privacy:** video never leaves the box and is never stored. This is a selling point and is required by Albania's GDPR-aligned data-protection law.
- **Updates:** the app checks a JSON manifest on free hosting (GitHub Releases / Cloudflare R2), downloads the new APK and asks once to install. Silent OTA comes later with ODM system privileges.

---

## 4. Games (families, Albanian-language UI and voice prompts)
Chosen from what people actually play most on camera-based systems (October 2026 research): Nex Playground's most-played
games of 2025 were 1 Mirrorama (AR filters), 2 Party Fowl (minigames), **3 Starri (rhythm slashing)**, **4 Bowling**,
5 Baseball, **6 Fruit Ninja**, 7 Tennis, 8 Racing, 9 Tumbobots, 10 an auto-runner. On Kinect: Kinect Adventures (24M,
minigames), Kinect Sports (bowling headline), Fruit Ninja Kinect.

1. **Vrapo! / Runner** (inspired by Subway Surfers): step or lean to change lanes, jump, crouch to slide. Albanian-themed worlds (Tirana streets, Berat, Gjirokastër, bunkers) for local pride and marketing. *Gray-box built.*
2. **Fruta! / Slash**: swipe hands through flying fruit, avoid bombs, combos, 60 s rounds. Slashing is the only mechanic with two games in Nex's top 6. Hands only, so it works in small rooms and for small kids. *Gray-box built.*
3. **Bowling**: step sideways to place the ball, swing the arm underhand to roll. 1–4 players take turns, 5 or 10 frames. Nex's #4 and most-played group game, and it fits single-person tracking on the cheap box. *Gray-box built.*
4. **Pikturo! / Paint** (added 2026-10-08): paint on the TV with both hands (hold a hand still to put the brush down, drop the arm to lift it), brushes, stamps, bucket, colouring pages, photos. **Camera mode** shows the camera picture behind the painting, so children paint a dress or a crown on themselves: the first Mirrorama-style AR mode, using the camera preview stream (protocol v1.1). *Built.*
- **Turns mode** (parent vs child take turns) gives family multiplayer for free. Simultaneous 2-player comes in v1.1 (needs the RK3576 or the NPU backend).
- Next candidates: **Mirrorama-style AR fun** (#1 on Nex; needs the camera image in the game, i.e. protocol v2), a **minigame party pack** (#2), **Goalkeeper** (football culture), *valle* rhythm game, boxing/fitness for adults.
- **Art:** Kenney.nl and Quaternius (free, CC0) plus cheap Synty packs. Custom Albanian props made in Blender.

---

## 5. Repository layout (`D:\Projects\moveit`, git init)
```
docs/                       plan.md, protocol.md, hardware.md (suppliers, BOM, benchmarks)
motion-service-py/          PC dev service: main.py, pose.py, mapping.py (33→17), protocol.py, requirements.txt
motion-service-android/     (Phase 1) Kotlin library: camera + MediaPipe/RKNN → UDP; also a standalone benchmark app
unity/MoveItGames/          Unity 6000.3 URP project
  Assets/MoveSDK/Runtime/   MotionReceiver, Skeleton, OneEuroFilter, PlayerTracker, BodyCalibration,
                            GestureDetector, HandPointer, IGameInput (+ MotionInput, KeyboardInput)
  Assets/MoveSDK/Debug/     SkeletonView + MotionDebug scene
  Assets/Games/Runner|Slash|Goalkeeper/
  Assets/Shell/             onboarding/camera-placement guide, main menu, settings, updater
hardware/                   camera-mount CAD/STL, BOM sheet
```

---

## 6. Phases, timeline and budget (start: October 2026)

| Phase | When | Deliverable | Spend |
|---|---|---|---|
| **0: PC prototype** | Weeks 1–3 (Oct) | Python motion service on the laptop camera, MoveSDK + debug scene, gray-box **Runner** playable on a TV via laptop HDMI. Test with 5–10 people incl. kids. | €0 |
| *(week 1, in parallel)* | | **Order samples now** (AliExpress delivery to Albania takes 2–4 weeks): 1× RK3566 box, 1× RK3576 box, 1–2 wide-angle UVC cameras, HDMI cable, USB extension | ~€150–190 |
| **1: Box port + choice** | Weeks 4–8 (Nov) | Kotlin motion library + benchmark APK. Test UVC camera support, pose fps *with the game running*, latency on both boxes. **Pick the box tier.** Runner runs on the box. | — |
| **2: MVP** | Weeks 8–16 (Dec–Jan) | Slash + Goalkeeper, onboarding (camera placement, "step back until your whole body fits"), Albanian UI and voice, TV-remote menu, turns mode, auto-start on boot, updater, final art pass. | ~€50–100 (assets, domain) |
| **3: Family pilot** | Weeks 16–22 (Feb–Mar) | 10–20 families via **pre-orders with deposit** (cash on delivery is normal in Albania). Collect feedback, fix tracking edge cases (light, small rooms). | Funded by deposits |
| **4: Batch 1 launch** | Weeks 22–34 (Apr–May) | 100–300 white-label units, packaging, CE paperwork from the supplier, register at QKB. **Launch for 1 June (Children's Day).** | Funded by pre-orders |
| **5: Grow** | Jun 2027 → | Monthly free game updates, 2-player, Christmas 2027 push, Kosovo/North Macedonia, later a "camera-only kit" for existing Android boxes, v2 all-in-one hardware. | — |

**Reserve (~€200–250 of the €500):** 3D-printed mount (€10–20), asset packs, small TikTok/Instagram/Facebook ad tests (€50–100). Demo videos of kids jumping in front of the TV are the main marketing.

---

## 7. Top risks and mitigations
| Risk | Mitigation |
|---|---|
| Box firmware doesn't expose the USB camera to apps | Test on samples before any bulk order. Fallback to a libuvc-based capture library. Require it from the ODM in writing. |
| Too few fps on RK3566 with the game rendering | Lite model at 256px input, game rendered at 720p, RKNN NPU backend, or step up to RK3576. |
| Dark rooms or backlight from windows | Low-light camera, auto-exposure tuning, onboarding light check ("turn on a light"). |
| Small rooms | Wide lens. Games designed for in-place moves (one step left/right max). |
| Malware or uncertified Google apps on cheap boxes | Clean firmware only. Don't preinstall Google, Play Store or IPTV apps on units you sell. |
| DRAM price spikes raise box cost | Written quotes, 2GB SKU if benchmarks allow, keep the RK3576 option. |
| Name clash: "MoveIt" is a well-known robotics software name | Check the trademark (Albanian DPPI / EUIPO) before branding. Consider an Albanian name. |

---

## 8. First coding sprint (right after plan approval) = Phase 0
1. `git init` and the folders above, plus `docs/plan.md` (copy of this plan) and `docs/protocol.md`.
2. **motion-service-py**: venv, `pip install mediapipe opencv-python numpy`, download `pose_landmarker_lite.task` (~6 MB, from Google's MediaPipe model storage). `main.py --camera 0 --port 5005 --preview` handles capture, Pose Landmarker (VIDEO mode), the 33→17 mapping, JSON over UDP, and a preview window with skeleton and fps.
3. **Unity project** `unity/MoveItGames` (URP, 6000.3.9f1, created via batch mode). Add the MCP for Unity package so I can drive the editor. MoveSDK runtime scripts (UDP receiver on a background thread with latest-frame buffer, One-Euro filter, player selection, calibration, gesture detector, `IGameInput`).
4. **MotionDebug scene**: live skeleton, gesture event log, fps and latency readout.
5. **Runner gray-box**: 3 lanes, endless chunk spawner (low barrier → jump, high barrier → crouch, blocked lane → switch), coins, speed ramp, score/game-over, keyboard fallback.

## 9. Verification
- `python motion-service-py/main.py --preview` shows the skeleton on the laptop camera at ≥25 fps.
- In Unity, playing **MotionDebug** shows the skeleton moving in sync. Lean, jump, crouch and hands-up appear in the event log.
- **Runner** is fully playable with the keyboard, then with the body from 2–2.5 m, laptop → TV over HDMI.
- **Latency test:** film yourself and the TV with a phone in slow-motion and count frames from the jump to the on-screen jump. Target < 150 ms.
- **Robustness:** 5+ testers of different heights (incl. a child). False triggers < 1 per minute. Detection under evening lighting.
- **Phase 1 gate:** on the chosen box, pose ≥25 fps *and* game ≥30 fps at the same time, latency < 150 ms, and the camera works after a reboot and replug.

## Sources
- Pi 5 prices: https://www.tomshardware.com/raspberry-pi/raspberry-pi-5-price-increases-drastically-as-ai-shortage-bites-16gb-version-now-usd205-second-price-increase-in-three-months-over-70-percent-more-expensive-than-original-msrp, https://www.raspberrypi.com/news/1gb-raspberry-pi-5-now-available-at-45-and-memory-driven-price-rises/
- Pi 5 MediaPipe pose fps: https://hackaday.io/project/203704-gesturebot-ros2-computer-vision-mobile-platform/log/242569-mediapipe-pose-detection-real-time-performance-analysis
- Unity Linux ARM licensing: https://unity.com/solutions/embedded
- Nex Playground: https://the-gadgeteer.com/2026/01/30/this-249-nex-playground-motion-gaming-startup-outsold-xbox/, https://www.engadget.com/gaming/the-nex-playground-is-everything-xbox-kinect-wanted-to-be-133000397.html
- RK3576 boxes: https://www.ebay.com/itm/396163038750, https://www.gearbest.ma/product/h96-max-m9-ai-android-14-tv-box-npu-6tops-rockchip-rk3576-8-core-2-4g-5g-wifi6-bt5-4-1000m-lan-8k-4k-120fps-4gb-64gb-8gb-128gb/
