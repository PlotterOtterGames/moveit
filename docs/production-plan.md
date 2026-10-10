# Production readiness: P1 "Recognition on the box" (+ roadmap for brand, UI, polish)

## Context
The APK runs (tested in MuMu), but it is a gray-box: placeholder name "MoveIt" (which clashes with a known robotics
project), IMGUI debug-style screens, and, most importantly, **no camera recognition inside the APK**. Today the pose
AI runs only in Python on the PC and sends skeletons over UDP, so the box can't be played on its own.

Decisions from the user: **recognition on the box first**, then brand and UI. **Help with name + logo.**
**Bright & playful** look. **Sample boxes not ordered yet**, so we target Android generally (MuMu, phones) now and
add the Rockchip NPU path when boxes exist. **Action for the user:** order the Phase 1 samples now (docs/hardware.md),
since delivery takes 2–4 weeks and real speed numbers only come from the box.

## Roadmap
| Phase | Outcome |
|---|---|
| **P1 Recognition on the box** (this plan, detailed below) | The APK sees the player through the camera by itself, with lower latency and better gestures, on PC and Android |
| P2 Brand | Name shortlist (checked for clashes) → user picks → logo (SVG), palette, kid-friendly font with ë/ç, regenerated icon, banner, boot animation, brand board page for review. **2026-10-08: name "Tundu" picked; logo, palette, fonts (Fredoka + Nunito), icon, banner, boot animation generated from `brand.json` + `tools/branding/logo.py`; waiting for the user's review** |
| P3 Production UI | Replace IMGUI with **UI Toolkit** (one USS theme file = brand colors/fonts). Screens: first-run setup (language, camera placement with live silhouette, Wi-Fi), animated home with game cards, how-to-play cards, HUD, pause, results with stars/confetti, settings. Player mirror becomes a silhouette/avatar. Sounds and music. 10-foot TV sizing and overscan margins |
| P4 Game polish | Kenney art already on D: (platformer kit → Runner world, Cube Pets → player characters, Input Prompts → remote icons), particles, sound, tutorials, difficulty tuning |
| P5 Release engineering | Release keystore, versioning, OTA updates, local logs/crash capture (no tracking), 60 fps budget, QA checklist, git commits, scripted builds. **2026-10-10: cloud built (docs/cloud.md)**: signed OTA app/firmware updates with staged rollouts, crash reports via ApplicationExitInfo, versionCode scheme + release signing in BoxBuild, gift cards + Game Pass server (Cloudflare Worker + D1). Waiting for: Cloudflare setup and keys, a test on real boxes |

---

## P1 Recognition on the box (detailed)

### Architecture
```
Android (box / MuMu / phone)                                   PC (unchanged path)
CameraX ─▶ MediaPipe PoseLandmarker (GPU, CPU fallback)        main.py (MediaPipe Python)
   └─▶ 33→17 joints + world joints ─UDP 127.0.0.1:5005─▶ Unity MoveSDK (same code everywhere)
```
The same Motion Protocol and MoveSDK serve both, so games don't change. Rockchip NPU (RKNN) can later slot in as a
second backend behind the same protocol.

### Work items
1. **Android motion plugin** (Java, no Kotlin tooling needed): `Assets/Plugins/Android/MoveItMotion.androidlib/`
   (`build.gradle`, `AndroidManifest.xml` with CAMERA permission, `src/main/java/al/moveit/motion/*.java`).
   - Dependencies from Maven (Unity's Gradle already has google()/mavenCentral(), AndroidX on, Java 17):
     `com.google.mediapipe:tasks-vision`, `androidx.camera:camera-camera2 / camera-lifecycle`. Pin the latest stable
     versions at implementation time.
   - `CameraSource`: camera choice **USB/external → back → front**; ImageAnalysis 640×480 RGBA, keep-only-latest
     frame (no queue = no added latency). Uses a small own `LifecycleOwner`, because Unity's activity isn't one.
   - `PoseEngine`: PoseLandmarker LIVE_STREAM, **GPU delegate with CPU fallback**. **Auto model**: starts on `full`
     (more accurate) and drops to `lite` if it holds under 22 fps for 3 s. Models are bundled from
     `motion-service-py/models`.
   - `MotionService`: maps 33→17 joints (same table as `motion-service-py/skeleton.py`), adds world joints, sends
     protocol packets to 127.0.0.1:5005 with `src` like `android-mediapipe-gpu-full`, pauses and resumes with the app.
2. **Unity bridge** `Assets/MoveSDK/Runtime/Android/AndroidMotionService.cs`: start/stop/status through
   AndroidJavaClass. It is started from `Assets/Games/Common/BoxRuntime.cs` on Android only; in the editor the Python
   service still provides the data. **Camera permission flow** uses `UnityEngine.Android.Permission`, with a friendly
   Albanian screen when permission is refused. Settings "Kamera" row shows camera name, model, fps and inference ms
   (`Assets/Shell/Scripts/MainMenu.cs` `Value()`).
3. **Protocol v1.1** (backward compatible, new fields optional): `wk` = 17×3 world joints in metres (hip-centred),
   `cam` = camera fps, `model`. Update `docs/protocol.md`, `Assets/MoveSDK/Runtime/MotionPacket.cs`,
   `motion-service-py/pose.py`, `main.py`, `protocol.py`, `fake_sender.py`.
4. **Faster, more accurate gestures** in MoveSDK:
   - `Body.cs` / `BodyTracker.cs`: keep **two filter tracks**, a low-lag one for gestures and a smooth one for
     drawing. Add **latency compensation**: predict the torso and hands forward by the measured pipeline latency,
     capped at 80 ms. **Player continuity**: re-attach the same player after dropouts up to 1.5 s, so there's no
     recalibration and no pause flicker.
   - `BodyGestures.cs`: **jump detected at take-off** (upward velocity onset), about 100 ms earlier than now. Crouch
     onset uses the same idea. Thresholds stay body-relative.
   - `UnderhandThrowDetector.cs`: "in front of the body" check uses **world z** when present (much more reliable than
     image z). `BodyHands.cs`: uses the low-lag track.
5. **PC parity** (`motion-service-py/main.py`): `--model auto`, request 60 fps from the camera, send world joints.
6. **Recognition Lab** (upgrade of `Assets/Scenes/MotionDebug.unity` / `MotionDebugOverlay.cs`, opened from Settings →
   Kamera): live fps, inference ms, capture-to-game latency graph, model and camera name, gesture log. A
   **record session** button writes a `.jsonl` file on the device (adb pull) for replay and tuning on the PC with
   the existing `replay.py`.
7. **Tests** (extend `GesturePipelineTests.cs`): idle/fidget script → **zero false gestures**; dropout script
   (1 s gap) → **no recalibration**; jump-onset latency below a target; bowling with world z. New `fake_sender.py`
   scripts: `idle`, `dropout`.
8. **Dev loop on MuMu**: `tools/dev/run-on-mumu.ps1` connects adb to `127.0.0.1:16384`, builds via the existing
   *MoveIt → Box → Build Android APK*, installs with `-g` (camera permission), and streams `logcat` filtered to MoveIt.
   MuMu's camera setting must point at the laptop webcam (MuMu → Settings → Camera).
9. **Build machine hygiene**: move the Gradle cache (574 MB on C:, growing) to D: by setting the user environment
   variable `GRADLE_USER_HOME=D:\.gradle` (needs your OK; it's a persistent setting), and lower Unity's Gradle JVM
   heap to 2 GB (Preferences → External Tools) for this 16 GB laptop.

### Verification
- Unity: compile clean. All existing 14 tests pass plus the new idle/dropout/jump-latency/world-z tests.
- MuMu: install via `run-on-mumu.ps1`. The home screen shows "Kamera: OK". The Recognition Lab shows a skeleton from
  the webcam, with fps/latency logged via logcat. Runner, Fruta and Bowling are playable with the body inside the
  emulator. (MuMu may force the CPU fallback: functional check only; speed is judged on real hardware.)
- PC: `main.py --model auto` still drives the editor; replay a recorded on-device session with `replay.py` and get
  the same gestures.
- Numbers to record in `docs/hardware.md`: pose fps, inference ms, capture-to-game latency (target < 100 ms on a
  phone/box GPU), false triggers per minute (target 0 in idle).

### Risks
- MediaPipe GPU delegate inside the emulator: likely CPU fallback, so MuMu is a functional test, not a speed test.
- USB camera exposure on cheap boxes (Camera2 external HAL): checked on the samples. If missing, a libuvc capture
  path is added then.
- Low RAM during Gradle/IL2CPP builds: close IntelliJ during builds; the heap cap in item 9 helps.
