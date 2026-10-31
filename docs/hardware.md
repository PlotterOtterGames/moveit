# Hardware: Phase 1 sample order (order in week 1, ~€150–190)

AliExpress delivery to Albania takes 2–4 weeks, so order while Phase 0 (PC prototype) is running.
Prices are rough retail estimates (October 2026, RAM prices are volatile). Check the listing details, not only the title.
**Concrete links, current prices (higher than below, ~€255 for the recommended set), batch suppliers and the
import process: `docs/sourcing.md`.**

## Shopping list

| # | Item | What to search | Must have | ~Price |
|---|---|---|---|---|
| 1 | **Box A (cheap tier)** | `RK3566 Android 11 TV box 4GB 32GB` | Rockchip **RK3566**, 4 GB RAM, Android 11+, USB 3.0 port, Wi-Fi 5 | €30–45 |
| 2 | **Box B (NPU tier)** | `RK3576 Android 14 TV box 4GB 32GB` | Rockchip **RK3576** (6 TOPS NPU), 4 GB RAM, Android 14 | €60–90 |
| 3 | **Wide-angle camera** | `USB camera 1080P wide angle 100 degree UVC` | **UVC** (driver-free), MJPEG, 1080p30 or 720p60, **90–110° diagonal** (not 160° fisheye), TV/monitor clip | €15–25 |
| 4 | Optional 2nd camera (low light) | `USB camera IMX307 wide angle UVC` / `starlight USB camera` | Sony IMX291/IMX307 sensor, same specs as #3 | €20–35 |
| 5 | USB extension cable | `USB 3.0 extension cable 2m` | 1.5–3 m, male to female | €3–5 |
| 6 | HDMI cable | (if you don't have one) | any HDMI 2.0 | €3–5 |

**Avoid:** Allwinner H313/H616/H618 and Amlogic S905W/S905W2 boxes (weak GPU, no NPU), and "8K, 16 GB RAM" listings
at €20 (fake specs).

## Why ~100° field of view
To see a whole 1.8 m player with margin you need about 2.5 m of vertical view. At 2.2 m distance
(a typical Albanian living room) that is ~60° vertical, which on a 16:9 camera is ~90° horizontal or ~100° diagonal.
The laptop's built-in camera (~75°) needs the player at about 3 m. Wider than ~120° bends the body at the edges
and hurts tracking.

## When the boxes arrive (Phase 1 checklist)
1. **Security first:** connect them to a guest Wi-Fi, not your main network (cheap boxes sometimes ship with malware).
2. Note the exact model, chip, RAM and Android version (Settings → About) in the results table below.
3. Plug in the camera, then check that our benchmark app (built in Phase 1) sees it through Android's camera API.
   If not, note it; we fall back to direct USB (libuvc).
4. Run the benchmark: pose fps with the Runner game rendering at the same time, plus latency.
5. Reboot, unplug and replug the camera, and check it still works.

## Benchmark results

| Box | Chip / RAM / Android | Camera visible to Camera2? | Pose fps (with game) | Game fps | Latency | Notes |
|---|---|---|---|---|---|---|
| A: **X98K Pro** (2026-10-30) | RK3566 (4× Cortex-A55 1.8 GHz, Mali-G52 2EE up to 700 MHz, NPU: driver 0.7.2 + RKNN runtime 1.3.0 in the firmware) / 4 GB / Android 11 userdebug | **Yes** (2026-10-30 evening): an "HD USB Camera" (USB 32e4:9230) as external camera 109; CameraX starts after its retries. Android gets only 1024x768, 1280x1024 and 1920x1080 at 30 fps (see "USB camera on the X98K Pro") | MediaPipe on the GPU **~10–11** (86–113 ms a frame, lite no faster than full); **on the NPU ~20.5 while looking for someone** (the detector, 41 ms), tracking frames 25.5 (lite) / 34.4 ms (full) on the box without a game | First build (Vulkan, HDR, render scale 0.8, post-processing), waiting screens, no player: menu 49, Bowling 60, Pikturo! 58–60, Vishu! (doll) 30, Fluturo! 25, Shpata! 25, Vrapo! 23–25, Tenis! 22, Sporte! 13. **With the box settings (2026-10-30, see below): menu, Bowling, Pikturo! 60; every other game, Sporte!'s events included, a steady 30 (0–0.7 % frames late)**. With the camera AI on the GPU beside it: Vrapo! 22 (46 % frames late); **on the NPU: Vrapo! 29.9–30.0** | ~170 ms camera to game | **GPU-bound**: GPU load 97–99 % at 700 MHz in every scene but Bowling and Pikturo!, render thread only 2–11 ms. Memory 0.44–0.51 GB (PSS). 58–61 °C after 10 min, no throttling seen yet. No ALLM. Firmware wide open (adb without authorisation on the network, setuid su, SELinux permissive): docs/box-os.md |
| B | | | | | | |

**Gate to pick a box:** pose ≥ 25 fps **and** game ≥ 30 fps at the same time, latency < 150 ms,
camera survives reboot and replug.

### GPU tuning on the X98K Pro (2026-10-30)

Measured with `PerfLab` (render settings switched on the box while a game runs, from a file pushed over adb; see
`Assets/Games/Common/PerfLab.cs`) and `PerfLog` (`[Perf]` lines), every scene's waiting screen, 10 s per setting.
What each thing cost (frame time at 1080p, Mali-G52 at 700 MHz, no throttling up to 70 °C):

| What | Cost | Decision |
|---|---|---|
| Post-processing (bloom + colour grading + vignette) | ~13 ms in every 3D game; colour grading without bloom was even slower (18 fps in Vrapo!, an odd driver path) | **off on the box** (`BoxGraphics`); the editor keeps it |
| Vulkan instead of OpenGL ES | menu 49 vs 58 fps, 3D games 1–3 fps slower | **OpenGL ES 3 first**, Vulkan as fallback (`BoxBuild`) |
| Rendering into a texture copied to the screen (HDR, or a render scale ≠ 1) | ~3–4 ms; the copy alone kept the menu under 60 | **HDR off** (`Mobile_RPAsset`); 2D scenes at scale 1 draw straight to the screen |
| Render scale 1.0 instead of 0.8 in 3D | +20–25 % frame time | 3D games stay at **0.8** |
| Sporte!'s clouds (procedural noise, 32 `sin()` per sky pixel) | ~14 ms | **a noise texture** (`SkyNoise`, one sample per octave): same look |
| IMGUI HUDs (outlined text drawn 10×, panels 5 layers) | Sporte!'s menu ~20 ms; others 0–3 ms | SportHud lighter: 1 shadow layer, only the chosen card casts one, no faint rims (alpha < 0.2), 4 outline copies for thin outlines and big text, the drop-shadow copy only under thick outlines. The real fix is P3 (UI Toolkit) |
| Shadows, sky gradient, particles, transparency, skinned characters | ≤ 1 ms each in most games | kept |
| Tenis! crowd / Sporte! crowd (chooser view) | ~3.5 / ~2 ms | kept |

**Sporte!** (measured in its events too, started through PerfLab's `activity=`; 13 → 22–25 fps after the round above):

| What | Cost | Decision |
|---|---|---|
| Real-time shadows | race 25 → 30 fps and football 18 → 24 when off. Not the shadow map: limiting the shadow distance or stopping the stands from casting changed nothing; every surface **sampling** it per pixel did | **off in Sporte! on the box** (`BoxGraphics`); the athletes get soft blob shadows instead (`Athlete.MakeBlobShadow`) |
| The athletes (9 at a free kick) | ~7 ms with the shadows off | `Tundu/Athlete`'s dither `clip` became a `_FADE` variant (any discard costs Mali its early depth test), pieces merged outermost first, the closed pieces (body, face, hands, shoes) a second submesh with back faces culled |
| The venue's toon shading over the pitch and stands | ~2–3 ms | `Tundu/Toon`'s `_TOON_SIMPLE` (no rim or gloss, sky light per vertex) on the two venue materials |
| Render scale | 0.8 → 0.72: +3 fps at a free kick | **0.72** in Sporte! (the HUD stays sharp) |
| The crowd | ~3 ms in the race (half the screen) | kept; its shadow looked up per fan (vertex), rows sorted lowest first: no measurable change |

Frame rates: 3D games target a **steady 30 fps** with Android's frame pacing (Swappy) on; the menu, Bowling and
Pikturo! 60. Final check (2026-10-30, the same build): menu, Bowling, Pikturo! 60.0; Vishu!, Vrapo!, Shpata!, Fluturo!,
Tenis! 29.9–30.0; Sporte!'s menu, 100 m and volleyball 30.0, football's free kick 29.9 (0–0.7 % frames late
everywhere); 69 °C, no throttling.

**The pose AI on top (2026-10-30):** MediaPipe's GPU delegate gave ~10–11 poses/s and pulled Vrapo! down to 22 fps,
so the pose AI moved to the NPU (0.8 TOPS; MediaPipe can't use it and this firmware has no NNAPI driver): MediaPipe's
own models converted to RKNN float16, run by our native engine on Rockchip's runtime 2.3.2, which works with the
firmware's 2022 driver 0.7.2 (a warning only). Vrapo! is back to a steady 30 with the camera AI running. Below.

### NPU pose engine on the X98K Pro (2026-10-30)

A tracked frame (the crop + the lite landmark model), measured on the box with `pose_npu_test` (no game running):

| Step | Lite frame | What it fixed |
|---|---|---|
| MediaPipe's GPU delegate (for comparison, in the app) | 86 ms | — |
| MediaPipe's model converted as it is (float16) | 110 ms | — |
| Without the segmentation mask | 50 ms | its 256 x 256 upsampling took ~55 ms on the NPU; we don't use it |
| The heatmap output channels first | 39 ms | the converter had appended an NHWC transpose that ran on the CPU (~10 ms) |
| Our crop writes the NPU's float16 input | 28.7 ms | RKNN converted the uint8 picture on the CPU (~7 ms) |
| The crop in fixed point, on two cores | **25.5 ms** | 11 → ~4–6 ms |

The full model: 34.4 ms; the person detector (only while nobody is followed): ~43 ms. The NPU clock (600 MHz, governor
`userspace`) at 900 MHz: 36.3 → 35.3 ms, so memory-bound, not clock-bound; the CPU runs at 1.8 GHz. **In the app**,
nobody in view: Vrapo! 29.9–30.0 fps (0–0.7 % frames late), the AI ~20.5 poses/s (the detector every frame, 41 ms), GPU
76 %, the app 166 % of the 4 cores. **Accuracy**: on MediaPipe's test picture and turned copies, the box's landmarks
are within 0.13–0.53 px mean of MediaPipe's own (3.5 px worst, 1000 px pictures), world joints within 0.1–0.4 cm.
**Heat**: after ~1.5 hours with the camera AI running nonstop (nobody in view, so the detector every frame, ~80 % of the
NPU's time) and the games, the SoC reached **81–82 °C, past its first passive trip point (75 °C; the next 85, critical
115)**: GPU 700 → 600 MHz, CPU 1.8 → 1.6 GHz. Vrapo! still held 29.8–29.9 fps. Earlier, the games alone stayed at
69 °C. **Next**: run the detector less often while nobody is in view (it only has to notice someone arriving); a person
tracked in a game, with light (speed and feel); INT8 quantization (the NPU's native precision: needs calibration
pictures and an accuracy check); the detector's file (29.5 MB: MediaPipe's depth-to-space layers become large
transposed convolutions); the box's cooling for long sessions.

### USB camera on the X98K Pro (2026-10-30)

An "HD USB Camera" (USB 32e4:9230, USB 2.0, plugged in before boot). What the camera offers (its UVC descriptors,
`/sys/bus/usb/devices/5-1/descriptors`) and what reaches Android:

| Size | MJPEG | YUYV | Android (camera HAL) |
|---|---|---|---|
| 320x240 | 120 fps | 30 | dropped |
| 640x480 | 120 | 30 | dropped |
| 800x600 | 60 | 20 | dropped |
| 1024x768 | 30 | 6 | **30** (what CameraX picks for our 640x480 request) |
| 1280x720 | 60 | 9 | dropped |
| 1280x1024 | 30 | 6 | 30 |
| 1920x1080 | 30 | 6 | 30 |

The camera lists one frame rate per size, and the external camera HAL bounds each size by the first `FpsList` entry at
least as tall in `/vendor/etc/external_camera_config.xml` (1280x720 gets 1024x768's 30, 640x480 its own 60) and drops
faster rates (`getFrameRateList: failed to get supported frame rates` in logcat), so the fast sizes vanish. It also
offers only [f/2, f] fps ranges ([3, 6], [4, 9], [10, 20], [15, 30]), never a fixed 30. Fixes: an `FpsList` whose
bounds let the camera's rates through (the ODM; on this userdebug box we could edit it ourselves after `adb remount`),
reading the camera over USB ourselves (UVC), or a camera that lists several rates per size. Not hurting yet (the pose
AI at 10–11 fps is the bottleneck), but 1024x768 frames cost more to convert than 640x480, and 30 fps caps the latency.

**Low light (2026-10-31, the user: "the room has low light, the camera seems to struggle")**: the camera still delivered
29.8 fps (counted in `/sys/kernel/debug/usb/uvcvideo/5-2/stats`), but the picture was nearly black (Vishu!'s mirror:
mean brightness 31 of 255, 18 % of it under 10). Its UVC controls (`su 0 v4l2-ctl -d /dev/video9 --list-ctrls-menus`;
the box has `v4l2-ctl`): brightness −64..64 (0), contrast 0..64 (32), saturation 0..128 (60), gamma 72..500 (100;
higher = brighter here), **gain 0..100 (was 0)**, power line 50 Hz (right for Albania), backlight compensation 0..2 (1),
exposure auto (aperture priority) or manual 1..5000 × 100 µs, **exposure auto priority (default 0, found 1: it may drop
the frame rate in the dark)**. Tried live, measured on the TV: gain 100 → mean 46; + gamma 160 → 110 (200 washes out
the highlights); + saturation 100 (low light leaves it nearly grey) → 107, nothing crushed, still 30 fps; exposure auto
priority 0 keeps 30 fps when it gets darker. The AI followed the user throughout (lite, ~23 ms a frame). None of these
controls reach Android's camera API on this box (the external camera HAL exposes none: no exposure compensation, no
manual sensor), and they reset when the camera restarts. **The camera service now sets them itself**
(`CameraTuner`): it measures the middle of the picture and steps gain, gamma and saturation up a ladder in the dark and
down in the light, exposure priority always off; through the camera's V4L2 node where the firmware lets the app open
it, else as root on test boxes (this firmware's `su` serves any app), else not at all. For families' boxes that means
the box maker opening the node to our app, or the camera's own power-on defaults from its supplier. A camera with a
low-light sensor (Sony STARVIS IMX307 / IMX462 / IMX291, `docs/sourcing.md`) is the hardware fix.

### Heat and frame rate with the camera on (2026-10-31)

The user: "fully optimize everything, reduce the heat and increase the fps". With the USB camera and the pose AI running
all the time the box sat at 76–77 °C on the home screen (82–83 °C after hours of play, then throttled: GPU 200–400 MHz,
CPU 1.4 GHz); the home screen ran 43–49 fps against its 60 (20–39 % of the frames late), Vishu! 23.5 fps. Where the
time went, from simpleperf with the build's own symbols (`tools/dev/box-profile.sh`) and `top -H`:

| Where | Cost | Change |
|---|---|---|
| Vishu!: the camera picture's JPEG decoded on Unity's main thread | 15 ms of its 39 ms frame | raw frames handed over inside the process (`frame_share.cpp` → `CameraFeed`, docs/protocol.md): a ~1 ms copy |
| The same picture's JPEG encoding in Java | ~9 % of the app's CPU | gone with it |
| The pose AI copying each frame into a Bitmap first | 3 MB a frame | reads CameraX's buffer in place |
| The person detector on every frame while nobody is in view | 20 detections/s, the NPU ~80 % busy | after 2 s without a person one look every 150 ms; the analysis thread sleeps in between, so CameraX converts no frames either |
| The full pose model on the NPU (29 ms a frame: it kept up, so the old rule kept it) | NPU load 64 % against 34 % with lite; when it switched, the SoC went 77 → 75 °C in 2–3 minutes | the NPU keeps full only under 24 ms a frame (~70 % of a frame): lite on the RK3566 |
| IL2CPP "faster (smaller) builds": generic code over value types through a slow shared path | 17 % of the home screen's main thread, 9 % of Vishu!'s | "faster runtime" (APK 77 → 84 MB, the build ~1 min longer) |
| The home screen at 60 fps: ~44 IMGUI labels a frame (each text twice, for its shadow) and URP | main thread 16.9 ms with someone in view: 43 fps, 39 % of the frames late | 30 fps unless the remote was used in the last 10 s (`BoxGraphics.Idle`; Bowling and Pikturo! also keep 60 while a player is in view); the background is the camera's clear colour, not a full-screen quad |
| Vishu!'s label widths measured every frame | 3.6 % of its main thread | measured once |
| A 3D game for 10 minutes (Tenis!, after the changes above) | SoC 80–81 °C; the kernel's power allocator capped the GPU at 300–600 MHz (~100 % load) and the CPU at 1.6 GHz: 28.6–30 fps, up to 9 % of the frames late | tried: 3D scenes one render-scale step smaller from 79 °C (0.8 → 0.7). The next 10 minutes sat at 80.5–81.6 °C anyway, the GPU capped lower (200–400 MHz) and Tenis! ran 26.9–29.5 fps (someone was playing: more AI work, so not a clean test, but no gain). The power allocator holds the SoC near its set point by itself and gives freed GPU power to the CPU. **Removed** |

Results, someone on the couch in view of the camera (the AI tracking them), `tools/dev/box-heat.sh` and
`box-scenes.sh`:

| | Before | After |
|---|---|---|
| Home screen | 43 fps against 60, 39 % of the frames late; main thread 16.9 ms, render thread 10.8 ms | 30.0 fps, 0 % late (60 while the remote is used); main 11–13 ms, render 6.6–7.5 ms |
| Tundu's CPU / the whole box's (home screen) | 190 % / 287 % of a core | 120–124 % / 197 % |
| GPU (home screen) | 700 MHz | 400 MHz |
| NPU load (someone in view) | 41 % (lite) – 64 % (full) | 30–44 % (lite) |
| SoC (home screen, steady) | 76.8–77.5 °C | 75.0–75.6 °C |
| Vishu! | 23.5 fps, 47 % late, main thread 39 ms | 29.9 fps, 0.7 % late, main 20.2 ms |

Every scene after (one build, `box-scenes.sh`, the AI on lite, the SoC 75 → 80 °C over the round):

| Scene | fps (frames late) | main / render thread | Tundu's CPU | GPU clock, load |
|---|---|---|---|---|
| Home screen (idle rule) | 30.0 (0 %) | 11.1 / 6.6 ms | 124 % | 400 MHz, 34 % |
| Vrapo! | 30.0 (0 %) | 16.7 / 12.5 ms | 160 % | 700, 62 % |
| Vishu! | 29.9 (0.7 %) | 20.2 / 16.7 ms | 198 % | 700, 60 % |
| Bowling (60) | 56.6 (6 %) | 12.2 / 7.4 ms | 183 % | 600, 23 % |
| Pikturo! (60) | 58.8 (2 %) | 9.8 / 7.1 ms | 173 % | 700, 47 % |
| Shpata! | 30.0 (0 %) | 11.2 / 6.7 ms | 126 % | 700, 76 % |
| Fluturo! | 30.0 (0 %) | 14.6 / 9.6 ms | 145 % | 700, 72 % |
| Tenis! | 29.9 (0.7 %) | 16.8 / 14.3 ms | 175 % | 700, 68 % |
| Sporte! (its menu) | 29.9 (0.3 %) | 20.2 / 13.8 ms | 187 % | 600, 44 % |

**What the app can't change** (firmware, docs/box-os.md item 20): the CPU governor (`interactive`, target load 65 %,
20 ms samples) holds all four cores at 1.8 GHz for any game's per-frame bursts (100 % of the time on the home screen
even at 30 fps); the GPU governor holds 700 MHz from ~40 % load (972 s at 700 against ~10 s at 300–600 over a session);
the cooling is a small passive box. No thermal throttling was active at 75–77 °C (power allocator: control temperature
85 °C, cooling states 0). Rockchip's power HAL only knows "interactive" and a performance boost, so Android's
sustained-performance hint would raise clocks.

## Recognition benchmarks (fill in as devices are tested)

| Device | Backend | Model | Camera fps | AI fps | AI time | Capture→game latency | Notes |
|---|---|---|---|---|---|---|---|
| Laptop (Ryzen 7 7840HS, CPU) | py-mediapipe | full | 30 (20 in a dim room) | 30 | 15 ms | ~17 ms to send | 2026-10-05 |
| MuMu Player (emulator, x86 + ARM translation) | android-mediapipe GPU | lite (auto) | 30 | 30 | 12–14 ms | 14 ms (full model, earlier run) | 2026-10-07. Works end to end: body tracked, Fruta playable. First GPU frame takes ~15 s to set up |
| MuMu Player (same) | android-mediapipe GPU | full (auto kept it) | 30 (AE range [30, 30]) | 29.9 | 9.7 ms | | 2026-10-08, after the motion polish: frame-rate floor, reused frame buffer. ALLM: not supported (emulator) |
| RK3566 box (X98K Pro, HD USB Camera at 1024x768) | **android-rknn NPU** (float16) | full, auto → lite past 34 ms | 30 offered, ~20.5 analysed while detecting | ~20.5 (nobody in view) | detector 41 ms; tracking lite 25.5 / full 34.4 ms (box, no game) | not measured yet | 2026-10-30. Vrapo! a steady 30 beside it. Landmarks within 0.13–0.53 px of MediaPipe's |
| RK3566 box (X98K Pro, HD USB Camera at 1024x768) | android-mediapipe GPU | full, auto → lite after 10 s | 30 offered, 10–11 analysed | 10–11 | 86 ms with a person (lite), 90–113 ms without (the detector) | ~170 ms | 2026-10-30, a very dark room. The game drawn at 10 fps didn't make it faster; Vrapo! fell to 22 fps beside it |
| RK3576 box | android-mediapipe / rknn | | | | | | |

Read the numbers from **Settings → Kamera** (Recognition Lab) on the device.
