# MoveIt (working name)

Motion games on any TV: a cheap Android box plus a USB camera, with no controllers and no depth sensor.
The camera sees your body, AI finds your skeleton, and you play by moving.
Full plan: [docs/plan.md](docs/plan.md) · protocol: [docs/protocol.md](docs/protocol.md) · hardware: [docs/hardware.md](docs/hardware.md) · box OS: [docs/box-os.md](docs/box-os.md)

```
camera → motion service (pose AI) ──UDP 5005──▶ Unity app (MoveSDK → games)
```

| Folder | What |
|---|---|
| `motion-service-py/` | PC motion service: camera → MediaPipe Pose → 17-joint skeleton → UDP. Also `fake_sender.py` (scripted test player) and `replay.py`. |
| `unity/MoveItGames/` | Unity 6000.3 (URP) project: **MoveSDK** (`Assets/MoveSDK`), games (`Assets/Games/Runner`, `Slash`, `Bowling`, shared code in `Common`) and the menu (`Assets/Shell`). |
| `tools/branding/` | Boot animation, boot logo, icon and TV banner generated from `Assets/Resources/brand.json`. |
| `tools/provision/` | Turns a stock Android box into a MoveIt box over adb (home screen, hide Android, boot animation). |
| `tools/dev/run-on-mumu.ps1` | Installs the APK into MuMu Player (or any adb device), grants the camera, starts it, shows its logs. |
| `docs/` | Plan, protocol spec, hardware notes, box OS. |

## 1. Run the motion service (PC)

First time only:

```bash
cd motion-service-py && python -m venv .venv && .venv/Scripts/python -m pip install -r requirements.txt
```

Every time (the preview window shows your skeleton; `q` quits):

```bash
cd motion-service-py && .venv/Scripts/python main.py --preview
```

Options: `--camera 1` (external USB camera), `--model full` (more accurate, slower), `--record sessions/x.jsonl`
(save a session, replay it later with `replay.py`).

No camera, or don't want to stand up? Send a scripted player instead. `--script basic` (default) steps, jumps,
crouches and raises hands. `--script bowl` raises hands, then bowls 3 times. `--script slash` raises hands, then swipes.

```bash
cd motion-service-py && .venv/Scripts/python fake_sender.py --script bowl
```

## 2. Run the games (Unity)

1. **Unity Hub → sign in → Preferences → Licenses → Add → free Personal license** (one time).
2. Unity Hub → **Add project from disk** → `unity/MoveItGames` → open with **6000.3.9f1**.
3. Menu **MoveIt → Build All Scenes** (generates the gray-box scenes; re-running overwrites them).
4. Open `Assets/Scenes/Menu.unity` (the box's home screen) and press **Play**. Step left/right (or ←/→) to pick a game,
   both hands up (or Enter) to start it, ↓ then Enter for **Settings**, **Esc** returns to the menu. `Assets/Scenes/MotionDebug.unity` shows the skeleton full
   screen with gesture thresholds (yellow lines) and the event log.

Every game: **stand still ~1 s** to calibrate (automatic), **both hands up / Enter** to start, **F1** skeleton view,
**L** Albanian↔English, **Esc** menu.

### The games
Picked from what people actually play most on camera-based systems: Nex Playground's
[top 10 of 2025](https://www.nexplayground.com/blog/the-top-10-nex-playground-games-of-2025) and the best-selling
Kinect games ([Kinect Adventures](https://en.wikipedia.org/wiki/Kinect_Adventures!), Kinect Sports, Fruit Ninja Kinect).

| Game | Why | Body | Keyboard / mouse |
|---|---|---|---|
| **Vrapo!** (Runner) | Auto-runner (TMNT runner is Nex #10; the original Subway Surfers idea) | Step left/right = lane, jump, crouch = slide | ← → / Space / ↓ (hold) |
| **Fruta!** (slash) | Slashing is the only mechanic with two hits in Nex's top 6 (Starri #3, Fruit Ninja #6) | Swipe hands fast through fruit, avoid bombs. 60 s rounds, combos for 3+ in one swipe | Swipe the mouse |
| **Bowling** | Nex #4 and its most-played group game; headline event of Kinect Sports | Step sideways to place the ball, swing arm underhand to roll (swing speed = ball speed). 1–4 players take turns, 5 frames | ← → place, ↑ ↓ aim, hold Space for power |

**On the TV:** connect the laptop to the TV with HDMI and maximize the Game view (or make a Windows build).
The laptop camera is narrow, so stand **~3 m** away so your whole body fits. The wide-angle USB camera from
[docs/hardware.md](docs/hardware.md) works at ~2.2 m.

**Tips**
- Unity → **Edit → Preferences → General → Script Changes While Playing → Recompile After Finished Playing**.
  Otherwise saving a script while a game runs reloads the code mid-game and breaks it (NullReferenceExceptions).
- The Console shows every detected gesture (`[Gesture] Jump lane=0 …`) and each game's state changes (`[Bowling] …`),
  which helps when tuning thresholds with real people. Turn it off with **Log Gestures** on the `Motion` object.
- Hands-off screenshots: put times in seconds in `unity/MoveItGames/Temp/moveit_capture.txt` (e.g. `5,12,20`) before
  pressing Play. Screenshots land in `unity/captures/`. Delete the file to stop.

## 3. Tests

Unity → **Window → General → Test Runner**:
- **EditMode** `BowlingScoreTests`: strikes, spares, last-frame bonus balls, 300 perfect game, 5-frame games.
- **PlayMode** `GesturePipelineTests`: starts `fake_sender.py` and checks the whole camera pipeline end to end.
  `basic` must give exactly calibrate → left → center → right → center → jump → crouch → stand → hands up.
  `bowl` must give exactly 3 throws, with the two-hand raise not counted. `slash` must sweep the hand across the screen fast enough to slice.

## Running on Android (box, phone, MuMu)
The APK carries its own camera AI (`Assets/Plugins/Android/MoveItMotion.androidlib`: CameraX + MediaPipe, GPU with
CPU fallback, full model that drops to lite on slow devices). Build with *MoveIt → Box → Build Android APK*, then:

```bash
powershell -ExecutionPolicy Bypass -File tools/dev/run-on-mumu.ps1
```
In MuMu, set the camera to the laptop webcam first (MuMu → Settings → Camera). **Settings → Kamera** opens the
camera test (Recognition Lab): camera, model, GPU/CPU, fps, AI time, latency graph, and **OK = record a session**
(saved under `Android/data/al.moveit.games/files/sessions`, replay on the PC with `replay.py`).

## Status
Phase 0 ✅ · P1 "recognition on the box": camera AI inside the APK ✅ (builds, 54 MB) · faster gestures (jump/crouch
at onset, latency-compensated hands, 3D depth for bowling, 1.5 s dropout tolerance) ✅ · Recognition Lab ✅ ·
tests 16/16 ✅ · runs on Android with a real camera (MuMu + webcam: 30 fps, ~13 ms, Fruta playable by body) ✅ · sample boxes ⏳ (not ordered yet)
