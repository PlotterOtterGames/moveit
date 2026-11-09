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

**Tenis! as doubles (2026-11-05)**: four chibis, Wii's camera (17 degrees down, the court fills the picture), the
camera AI tracking someone, the SoC at 80-82 degrees C with the GPU capped at 300-600 MHz by the power allocator:
27.5-27.7 fps (16-18 % of the frames late) with real-time shadows, 28.8-29.0 (7-8 %) without (PerfLab `shadows=0`,
back to back). Now **shadows off in Tenis! on the box** (`BoxGraphics`), blob shadows under the players
(`TennisPlayerView.BuildBlob`), and the stadium on `_TOON_SIMPLE`: 28.7-28.9 fps with the GPU at 300-400 MHz,
81.7 degrees C. Not measured cool (the box stayed hot while someone played).

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
tracked in a game, with light (speed and feel); INT8 quantization (tried 2026-11-01: twice as fast, not accurate
enough, see "INT8 pose models" below); the detector's file (29.5 MB: MediaPipe's depth-to-space layers become large
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

### The camera, and why the skeleton shakes (2026-11-01)

The user: "the camera should be more than enough to read the pose correctly, there shouldn't be any stutter; The Link
(Steam) gets a perfect skeleton with just a laptop camera."

**The camera** is an ELP-USBFHD01M (USB 32e4:9230, the id OctoPi's issue tracker gives that model): OmniVision's
**OV2710**, 1/2.7", 2 MP, 3.0 µm pixels, 3300–3700 mV/lux·s, signal-to-noise 39–40 dB. Laptop webcams have
1.1–1.4 µm pixels, so the hardware is indeed enough. Beyond the controls above it has sharpness 0..6 (2: sharpening also
sharpens the noise), hue, automatic white balance, and a vendor extension unit; at power-up its exposure priority is on.
Measured on the box with `v4l2-ctl` while Tundu was paused (night, MJPEG): 1280x720 **58.7–59.3 fps**, 640x480 87–93
(asked 120, buffers dropped), 800x600 49, 1024x768 29.8, 1920x1080 29.7, with exposure priority on or off. **The fast
modes are crops**: 1280x720 is the middle 1280 x 720 pixels of the sensor taken 1:1 (things look 1.5x bigger than in
the 1080p picture; OmniVision's own launch note: "cropped 720p 60 fps"), and 800x600 comes from that crop; 1024x768,
what Android hands us, keeps the sensor's full height (4:3, the sides cut). So 60 fps at those sizes costs a third of the
view each way: ~42° vertical instead of ~60°, a standing adult then needs ~3 m to fit head to toe instead of ~2.2 m.
**Except 640x480** (corrected 2026-11-01, measured by matching each mode's picture onto the 1080p one): it is a
1280 x 960 window of the sensor binned 2x2, 1.13x zoom, ~53° vertical, at up to 120 fps (87–93 at night). For the pose
AI 640x480 loses nothing (the person is shrunk into a 256 x 256 crop either way), so it would be the fast mode to use
if Android offered it (it doesn't: same `FpsList` bound).
This box's `FpsList` (Rockchip's format: several rates per size) does list 1280x720 at 60, yet Android drops the mode,
probably because the camera's rate is a hair over 60.0; a bound of 61 would let it through (needs `/vendor`
writable: the ODM, or `adb disable-verity` on this test box).

**How much the pose model shakes on this camera's pictures** (`tools/npu/shake.py`: a still picture made into frames
with the same camera-like noise, sigma 3 levels + JPEG 85, tracked like the box tracks; the raw frame-to-frame
step of the joints before any smoothing, % of the torso, median over pictures). The 20 held-out evening snapshots (people
sitting with a guitar, ~4 m away, the camera tuner's gain on) against MediaPipe's test photo, also shrunk to the same
person size:

| | Wrist | Elbow | Shoulder | Hip | Knee |
|---|---|---|---|---|---|
| Lite, clean photo (full size / same size as the snapshots) | 1.4 / 0.7 | 0.6 / 0.4 | 0.9 / 0.5 | 0.7 / 0.4 | 0.7 / 1.1 |
| **Lite, evening snapshots** | **12.4** | **7.7** | **3.6** | **5.7** | **11.4** |
| Full, evening snapshots | 9.3 | 4.5 | 2.2 | 3.4 | 7.6 |
| Lite, the crop also run mirrored, both averaged | 9.0 | 6.5 | 3.3 | 4.6 | 8.9 |
| Full, mirrored and averaged | 6.6 | 3.1 | 1.6 | 2.8 | 8.2 |

The same model shakes 10–20x more on those pictures than on the clean photo, and it isn't the person's size (the
shrunk photo stays steady): it's the picture (dim and boosted, grey clothes against white walls and a grey couch, a
guitar in front of the body, people sitting). The standing snapshots shook less (shoulders and hips 1–2.6 %). Shrinking
the picture by area averaging before the crop (instead of bilinear samples) didn't help. MediaPipe's smoothing on the
box calms the raw steps several times, but what is left is what the user sees. **The Link** (Masonx, Steam 1285430):
see "What The Link runs" below (observed on the laptop: MediaPipe Holistic with the full pose model, 1280x720 at 60
fps, the skeleton drawn on its own frame); it fires keys from held poses with a gap between "on" and "off" (90 % /
30–50 % similarity): its job hides shake.

**The first real tests** (the same night, 01:28 and 02:02, the merged build: the user standing ~2.5–3 m away, head to
feet in the picture, a light grey tracksuit against white walls, the camera tuner at level 4–5), recordings from
Settings → Kamera (`motion-service-py/sessions/box-2026-11-01/`, gitignored) scored with
`tools/motion/shake_report.py` (each joint's path smoothed both ways in time on the samples' capture times; the shake
is what's left, on the frames where the player holds still):

| Joint (shake RMS, % of torso) | Box, lite | ~px on a full-height 1080p mirror | Still frames jumping > 3 % (lite) | Box, full | Laptop, MediaPipe full (the user close, sitting) |
|---|---|---|---|---|---|
| Head | 0.8 | 2 | 2 % | 0.7 | 0.3 |
| Shoulders | 0.55 | 1.5 | 0.6 % | 0.6 | 0.25 |
| Hips | 0.9 | 2.5 | 2 % | 1.2 | (out of the picture) |
| Elbows | 1.5 | 4 | 6 % | 2.2 | 0.9 |
| Knees | 2.1 | 5.5 | 17 % | 2.2 | (out of the picture) |
| Wrists | **4.5** | **12** | 20 % | **4.5** | 1.7 |
| Ankles | **3.6** | **10** | 20 % | 3.3 | (out of the picture) |

Lite: 91.8 s at a steady 30 Hz, the player never lost, no camera frame skipped (the `gaps:` counters), frames ~47 ms
old when the AI starts on them, the pose out ~70 ms after the capture. **Full shook just as much** and didn't keep up
while tracking: 32–41 ms a frame (26 ms on the idle, cool box), 24–27 poses/s, 14–23 camera frames skipped every 5 s,
frames ~65 ms old: lite stays (auto).

**But those "still" frames were short pauses inside movement** (the player held still only 10–16 % of the time). In a
later baseline (02:35, 1024x768, lite, the tuner at level 3, the player holding still for real ~26 s of 46) the
skeleton barely moved: head 0.26 %, shoulders 0.23 %, elbows 0.50 %, **wrists 0.43 %**, hips 0.36 %, knees 0.53 %,
ankles 0.79 % of the torso, ~0.6–2 px on a full-height 1080p mirror, as steady as the laptop's full model. **What's
left is in movement**: head, shoulders and hips stay on their path, while wrists and ankles land more than 20 % of a
torso off it in ~2–3 % of the moving frames (likely motion blur at 30 fps with the long exposures of a dim room, and the
model on fast limbs). **1280x1024** (PerfLab `camera=1280x1024`, the camera's 5:4 mode; CameraX needed an exact-size
filter, it prefers 4:3): lite kept 30 fps, frames ~7 ms older (~53 ms), the AI ~2–3 ms slower; while moving the ankles
left their path less (4.6 % RMS against 6.1 %, > 10 % in 2.2 % of the frames against 6.8 %), the wrists about the same.
Not a clear enough win for ~10 ms more delay: the default stays 1024x768 (the switch stays for tests). **A 10 ms
exposure** (manual, set as root with `v4l2-ctl`; auto had been ~30 ms in that light) against the auto recording minutes
before in the same light (tuner level 5): darker (mean ~92 against ~155) with the tuner's gain on top, so still poses
shook more (wrists 2.8 % against 1.7 %, ankles 3.8 % against 3.1 %), the player was lost twice in a minute, and the
jumps while moving didn't drop (wrists > 20 % off the path in 3.5 % of the moving frames against 2.3 %, ankles 1.5 %
against 2.5 %): motion blur isn't the main cause, auto stays. **The camera's own auto exposure hardly re-adjusts**:
after a manual value it kept that brightness (155 after 33 ms, 92 after 10 ms; from 5 ms it only rose to ~98), so it
seems to act only outside a wide band, and the picture's brightness depends on its history (the tuner's gain ladder
covers for it; setting the exposure from the tuner too would make it predictable). **A gate against one-frame jumps
of hands and feet** (`tools/motion/jump_gate.py`: a limb sample further than 0.25 torso from its predicted position held
back one frame) was **not adopted**: true one-frame spikes (out and straight back by > 20 % of a torso) are rare, ~0.6–
1.3 a minute per wrist or ankle in most recordings, while the gate held back 6–30 real fast moves a minute per wrist and
left the limbs off their path more often. The "> 20 % off the path while moving" figures above are mostly real fast
movement that the both-ways smoothed reference can't follow, not errors. **So the box's skeleton data is clean**:
steady when still (~1 px on a full-screen mirror), no frames lost at 30 Hz, a rare spike; what the user sees as stutter
has to be measured on the screen itself (how the skeleton is drawn and when), not in the data.

**On the screen** (13:26 the next day, the merged build; `adb shell screenrecord` of the camera test screen while the
user moved, read by `tools/motion/screen_judder.py`): that screen falls under `BoxGraphics`' default, 60 fps while
someone is in view, and can't hold it: its IMGUI (the full-screen skeleton, the latency graph's 150 bars, the event
log, the panel) needs 18–21 ms of the main thread and ~16 ms of the render thread, so it ran **34–45 fps with 53–70 %
of the frames late**, an uneven mix of one- and two-refresh frames (`[Perf] MotionDebug`; recording took it to 20–27).
In the video only 5 % of the new pictures came one refresh after the last, 62 % two and 30 % three refreshes later, and
while moving the fastest joint stood still for a frame in 13.5 % of the frames and lurched at more than twice the
speed around it in 13.1 %. **That's the stutter the user sees there**: the pose data is even, its drawing isn't. The 3D
games hold a steady 30 (0–0.7 % late), the home screen 30 with someone in front. **Fixed** the same day: the camera
test runs a steady 30 on the box (`BoxGraphics`, `MotionDebug`): 30.0 fps, 0 % of the frames late, main thread
~13 ms with someone in view; a new screen recording (its own load included): 29.8 new pictures a second, 96 % of them
exactly two refreshes after the last (before: 62 %, and 30 % three refreshes). Making that screen's IMGUI cheap enough
for a true 60 is the next step if 30 isn't smooth enough.

**Hands circling in front of the body** (the camera test's recorder, `session-20261101-131243` and `-131723`; the user:
"I rotate only my hands, see the jumps in my right hand"): both forearms circling around still elbows in front of the
body, pointing ~70 % of their length toward the TV. The one place where the data isn't clean. With the TV remote in
the right hand, that hand jumped (a step over 15 % of a torso in one frame) 30 times in 41 s, the left 4, the right
mostly on the inner-upper part of its circle (11 % of those frames). Without the remote (`tools/motion/wrist_jumps.py`):
25 and 23 jumps in 49 s, ~30 a minute per hand (many are out-and-back pairs: ~12–15 visible skips a minute), **~85 %
of them while that hand passed in front of the chest** (2.9 % of those frames, 0.5 % beside the body; the circles came
further in this time, each hand in front of the torso in 45–50 % of the frames against 30–33 %, hence the left hand's
4 → 23). In those frames **the model pulls the wrist toward its elbow for 1–7 frames**: at a jump's short end the
forearm in the picture is about half its length (median 0.52 / 0.48) against 1.05 / 0.78 in the frames around it, and
the world joints shorten it too (~0.8), which no real arm does in 33 ms. With the forearm pointing at the camera the
hand hides it, and the model has to guess how far forward the hand is. Not the camera or the pipeline: every frame
analysed (0 skipped, 0 lost), 30 fps, picture mean 123–128 (tuner level 4); the hands touching doesn't matter (fewest
jumps then). A gate for exactly this (`wrist_jumps.py --gate`: a wrist that suddenly lands under 0.6 of its recent
forearm length from its elbow, with a step over 0.12 torso, held back for up to 3 frames; 7 changed nothing) left 20
and 22 of the 25 and 23 jumps (a held wrist jumps when it's let go, and many collapses last several frames) and took
12 of the 20 right-hand jumps out of the laptop's real jabs (`punches.jsonl`): **no filter fix**. Not checked yet:
whether the full model does better on the same movement (PerfLab `model=full`; it manages ~25 poses/s on this box, so
only as a test), and the pictures themselves (the recorder saves the poses only).

### Hand glitches and Tenis!'s swings (2026-11-06)

The user, after playing Tenis! round 3: "the model is struggling with hands, they keep jumping, which shows in the
rackets: fix it in the model, or else read when a jump happened and don't register it; and register small hand moves,
like a Wii remote". **The model**: nothing of ours (the NPU engine gives MediaPipe's answers), and switching models
doesn't help: on the routine's identical frames lite and full both put a wrist more than 0.12 torso lengths off its own
path in 2.1-2.9 % of the frames, and the old swing detector found 70 and 69 swings there with about the same glitches in
them. The glitches seen in the box's recordings (`session-20261101-015347`, `-131243`, `-131723`) are of four kinds:
the wrist pulled onto its elbow for 1-3 frames (the forearm ~0.1-0.4 of its length in the picture), flipped to another
spot and back (once 1.2 torso lengths up by the head, twice in five frames), lost in a blur (visibility 0.6-0.7, the
wrist anywhere), and a one-frame snap that then stays. Most last one or two frames. **So the swing detector reads them
itself** (`SwingDetector`, rewritten; `tools/motion/swing_replay.py` mirrors it exactly, the C# replayed the same
recordings to the same counts):

| What separates a glitch from a move | Glitch-made swings (old detector) | Real ones |
|---|---|---|
| Steps of at least 40 % of the biggest, the same way, in the last six | mostly 1-2 | mostly 3+ |
| How straight (net way / path over those steps) | median 0.52 (out and back) | 0.95 |
| Biggest single step | median 0.47 torso lengths | 0.19 |
| Wrist visibility (10th percentile) | 0.64-0.68 | 0.85-0.88 |

The detector: raw samples relative to the raw shoulders (BodyTracker's low-lag track turns a one-frame jump into a
two-frame move), each through a median of three (one sample late), a swing started only when both of its last two steps
carry it, measured from where the hand trustably was 0.12 s before, confirmed while its fastest moment is looked for: at
least 0.18 torso lengths, not all in one step (0.7 of the way + 0.05), not straight back (under 0.45 of the way for two
samples); a sample under visibility 0.8 makes it wait a sample more, under 0.6 it is skipped; a rejected move's samples
lose their trust, so a flip and its return are no move at all. Start speed 1.6 torso lengths a second (was 3); slower
than 3 is a gentle swing (the game takes it only at a ball in reach).

| | Old detector (3 T/s, low-lag track) | New |
|---|---|---|
| Box, hand circles (`131723`), swings a minute / glitches ignored | 15.6 (9 of 15 glitches) | 86 (12.5 strong) / 10 |
| Box, `015347` (the six glitch swings checked one by one) | 11.5 | 24 (5.2 strong), all six glitches ignored |
| Routine, lite judged by full on the same frames: real / glitch | 63 / 5 | 123 / 2 (60 / 2 strong) |
| Simulated still hand, flips of 0.4-1.2 T for 1-3 frames (80 each) | 80 swings | 0 |
| Simulated forearm flicks 0.25-0.5 T in 0.15-0.25 s (40 each) | 0 found | 38-40 (15 Hz: 19-37) |
| A one-frame / two-frame glitch in the middle of a flick | kept | kept 39-40 / lost |
| Report after the move starts (forearm flick) | 233 ms | 194 ms |

The many new swings on the hand circles are the circles themselves (real moves, mostly gentle: the game ignores gentle
moves away from a ball). End to end (PlayMode, `fake_sender --script tennis_flick --glitches 1`: only small flicks,
glitches once a second): the flicks served and returned 6+ practice feeds in a row, the small flick up was no miss,
the glitches were ignored.

### What The Link runs (observed 2026-11-01)

The user bought The Link (Masonx, Steam 1285430, `D:\SteamLibrary\steamapps\common\The Link`): "its performance is very
good, analyze its code for the pose skeleton; I feel we can do better". It is a Python 3.11 program compiled with Nuitka
(its constants encrypted in the file, so a plain string search finds nothing), so it was observed while running: a
read-only look at its process (which model files are in its memory, which libraries it loaded, the constants of its own
modules as Nuitka unpacked them: names, numbers, texts; never its code). Scripts in the session's scratchpad, not in
the repo. What it does:

| | The Link (the user's laptop) | The box (X98K Pro) |
|---|---|---|
| Pose AI | MediaPipe **Holistic** (the legacy `mp.solutions` API, CPU): the person detector, the **full** pose landmark model (heavy and lite sit in its folder, not loaded), both hands (`hand_recrop` + `hand_landmark_full`) and the face (short-range face detection + face landmarks); a separate Hands instance (`palm_detection_full`) for its hand-mouse mode | the same detector and MediaPipe's steps on the NPU, with the **lite** landmark model |
| Settings | detection and tracking confidence 0.5 (its "geek settings"), smoothing left at MediaPipe's default (on), no segmentation, no face refinement | the same filters (One Euro 0.05 / 80, visibility low-pass, the ROI's velocity filter), re-implemented |
| Its own smoothing | none found: no filter, history or prediction among its camera and pose modules | the mirror draws `Body.Shown` (`JointTimeline`: interpolated on the camera clock, half a frame behind) |
| Camera | OpenCV DirectShow, MJPG, **1280x720 at 60 fps** by default; a performance limit drops to 640x360 or 424x240 at 30 | 1024x768 at 30 (Android's camera HAL hides the faster modes) |
| Loop | one thread: read a frame, Holistic, draw, `cv2.imshow`: the skeleton is drawn on the very frame it came from, at the camera's rate | the pose goes to Unity; the mirror (no picture) is drawn at the game's 30 fps |
| Drawing | a thick stick figure: the pose points plus two midpoints (between the shoulders and between the hips: a neck and a spine), wrists as dots, each hand's 21 points from the hand model; over the camera picture, or a plain background in its privacy mode | the skeleton mirror: 17 joints, thin lines |

So nothing secret: stock MediaPipe at its defaults with the full model, on a laptop CPU, at the camera's rate, drawing
locked to the picture, and a hand model for the hands. The full model on the box's NPU (`pose_npu_test --perf`, idle
and cool): a tracked frame **25.5 ms** (crop 3.4, NPU 20.1, outputs 1.7) against lite's 20.4 (NPU 15.0); in a game
32–41 ms (below). Where full's 20.5 ms of NPU go: the 128 x 128 and 64 x 64 early layers ~45 % (memory-bound), the
heatmap head's upsampling to 48 x 64 x 64 1.9 ms plus ~0.7 ms after it (~13 %; the heatmap only refines the points),
and three output reshapes RKNN runs on the CPU, ~0.35 ms each (also in lite). Making full hold 30 fps in a game would
take pipelining the crop with the previous frame's NPU run, those reshapes moved out of the model, and maybe the
heatmap head; worth it only if full is clearly better on this camera: `tools/motion/box_clip.py` records the camera's
raw frames on the box and `clip_models.py` runs lite and full on exactly those frames for `shake_report.py` and
`wrist_jumps.py`.

**Lite against full on the box camera's own pictures** (the same evening, `routine-20261101`: 75 s at 1024x768@30,
the user ~3–4 m away (torso 153 px of 768) with everything below the knees hidden by the table the camera stands on;
standing, hands circling in front, punches and waves, steps, a walk up close; light level 3; MediaPipe lite and full on
the identical 2250 frames, `tools/motion/model_errors.py`, which counts only what no body does):

| | Lite | Full |
|---|---|---|
| Person lost | 0 frames | 9 (37.8–38.2 s: walked up close, head out of the picture) |
| One-frame spikes (out > 10 % of a torso and straight back), a minute, left / right wrist | 0.8 / 4.8 | 2.4 / 2.4 |
| 3D forearm > 25 % off its own length, left / right | **27.9 / 1.4 %** | **4.2 / 2.8 %** |
| 3D forearm length change from one frame to the next, median | 2.3–2.9 % | 1.9–2.0 % |

Per 5 s: the two models' wrists agree (> 30 % of a torso apart in < 1 % of the frames) while the movement is calm, and
part in 5–12 % of the frames of fast punching and waving (3.2–3.7 torso lengths/s) and 17 % in the close-up. Lite's
forearms are off in 3D in 14–64 % of the frames of every window, full's in 0–2 % except fast punching (~19 %). In the
frames where they differ most, full is right on bent arms and hands near the face or chest, lite in the close-up (full
swapped left and right there, then lost the person). `wrist_jumps.py`'s count (steps over 15 % of a torso) is mostly
real fast movement in a routine like this one (lite 154 / 123 a minute, full 133 / 95), so it only fits the hand-circle
test. **So**: in 2D, what the mirror and the hand cursors use, lite is close to full except in fast moves; in 3D (the
world joints: punches straight at the TV, Bowling's swing, the turn in Vishu!) full is clearly steadier. Side-by-side
video of both skeletons on their own frames: `clip_video.py` (`routine-20261101_lite_vs_full.mp4`, gitignored).

### The full model at 30 fps on the box (2026-11-01)

The user chose full ("make full fit"). Measured with `pose_npu_test` on the idle box, each step checked against the one
before:

| Change | Full, a tracked frame | Lite |
|---|---|---|
| Before (serial: crop, NPU run, outputs) | 27.5 ms (crop 3.4, run 21.8, outputs 1.6) | 18.6 ms |
| The three output reshapes taken out of the models (`convert.py`: `drop_output_reshapes`, the 1 x 1 x n convolution outputs kept channels first) and the heatmap read as float16, only its 7 x 7 refinement windows converted | **23.6 ms** (crop 2.9, run 18.3, outputs 0.6) | 17.6 ms |
| Pipelined: the next frame's crop made on the camera thread while the NPU runs the frame before on its own thread (`PoseEngine.submit`, `PoseNpu::Prepare` / `RunJob`) | the NPU's part 19.2 ms a frame on the user's clip, the crop 4.4 ms beside it; with real threads and the clip's JPEG decoding beside it, 45 frames a second | |

The new engine and models give **bit-for-bit the same** landmarks, world joints and visibility as before on the test
pictures. The pipeline makes each crop from the newest crop box: the frame before's when the NPU finished it in time
(MediaPipe's own chain), else the one before that. Forced to always use the older one, on the user's recorded clip
(`pose_npu_test --clip <clip.mjpg> --order pipelined`, the real NPU engine on all 2250 frames), the wrists differ from
MediaPipe's order by a median 1.1 % of a torso (95th percentile 6 %), the same as the box's engine against MediaPipe on
the PC (1.2 %, 6.3 %), the 3D forearms are as steady (> 25 % off their length in 4.0 / 2.9 % of the frames against
4.4 / 3.0 %), and the person was lost 3 times against 13. `--order threads` runs the app's two threads for real (no
crash, the same numbers). The auto model keeps full while the NPU's part stays under 31 ms a frame (it was 24 ms for
the whole serial frame, for heat).

**In the app** (the same evening, the user playing Vrapo! in front of the camera; the box already hot from the evening,
SoC 78–82 °C, the GPU's thermal throttle at its last step, `thermal-devfreq-0` 4/4; `tools/dev/box-ai.sh`):

| Vrapo!, someone followed | Lite (pipelined) | Full (pipelined, forced) |
|---|---|---|
| The AI | 30 poses/s, 0 frames skipped; NPU part 15–23 ms (NPU 13–21), crop 4–13 ms | 30 poses/s, 0 skipped; NPU part 21–29 ms (NPU 20–27), crop 4–9 ms |
| The game | 29.6–29.9 fps, 1–2.7 % of the frames late | **25.3–28.9 fps, 8–32 % late** |
| NPU load / GPU clock | 38–49 % / mostly 600–700 MHz | 64–70 % / mostly 200–400 MHz at ~100 % load |
| SoC | 78–80.5 °C | 81–81.7 °C |

When the player stepped out of view (the detector every 150 ms) the game was back at 29.9–30.0 fps at once, at the
same 81–82 °C: the cost is mostly live, the NPU and the GPU sharing the memory bus (RKNN's report: full's landmark model
reads and writes 41.5 MB a frame, ~1.2 GB/s at 30 fps; lite's 26.3 MB, ~0.8 GB/s), not slowly built-up heat. The NPU
at 400 MHz instead of 600: full's part 29–35 ms (barely keeping up), the game no better. The camera test with lite:
29.8–29.9 fps. **So full fits the AI's budget but not the GPU's in a heavy 3D game on this box.** The 31 ms rule would
keep full in Vrapo! (its part averaged ~25 ms), so the auto rule needs the game's view (frames late), not the NPU's;
before the test it had switched to lite anyway (the reason scrolled out of the log).

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

### INT8 pose models on the X98K Pro (2026-11-01): not accurate enough

The NPU's work matters for heat (lite instead of full: NPU 64 → 34 %, SoC 77 → 75 °C), and the RK3566's NPU is built
for 8-bit numbers, so the pose models were quantized to INT8 (RKNN-Toolkit2 2.3.2, w8a8: 8-bit weights per channel,
8-bit activations per tensor), to be adopted only if they stayed as accurate as the float16 ones. **They don't: the
box keeps float16.**

**Calibration**: 200 snapshots from the box camera in the evening (the living room, people sitting with a guitar,
standing, walking past; the camera tuner at level 4), one every ~2 s from the camera test screen. Every 4th was held out
for testing; the other 150 (+ one darker evening photo) became what each model really sees (`tools/npu/calibration.py`,
made with `blazepose.py`): the detector's letterboxed 224 x 224 frame of the whole picture and the landmark model's
tracking crop, each also mirrored and with other light, the crops also moved a little (as when the person moves between
frames): 453 detector frames, 157 lite and 155 full crops. The pictures stay on the laptop (`D:\rknn\calib`).

**How it was judged**: 25 held-out pictures with a person (20 snapshots, MediaPipe's test picture and 4 turned or
mirrored copies). End to end, through the tracking, even the float16 build lands ~10 px from MediaPipe on the dim
snapshots (a tiny difference moves the next crop and the uncertain model answers differently), while it stays
0.2–0.5 px on the clean test picture. So each model was run on **exactly the crop the original model sees**
(`compare.py --crops`, `pose_npu_test --crop`) and compared with the original model's answer: picture pixels and cm.

| Lite landmark model | Box NPU per tracked frame | Landmarks (mean / worst picture) | World joints |
|---|---|---|---|
| float16 (today) | 18.7 ms (NPU 12.3) | **0.30 px** / 0.76 | **0.25 cm** |
| INT8, RKNN's "normal" ranges | 12.1 ms (NPU 7.4) | 12.5 px / 29.8 (clean picture 1.4–2.4) | 10.3 cm |
| INT8, "mmse" / "kl" ranges (simulator, end to end, clean picture) | — | 3.4–4.9 / 54–89 px | 16 / 135 cm (output ranges clipped) |
| INT8 with 16-bit activations (w8a16) | — | not available for the RK3566 | |
| Rockchip's automatic hybrid, drifting layers float16 (simulator) | — | 11.3 px | 7.5 cm |
| float16 for the first third + output heads, INT8 the rest | 16.7 ms (NPU 10.6) | 4.8 px / 10.9 | 3.5 cm |
| float16 for the first two thirds + heads | 18.1 ms (NPU 11.9) | 3.2 px / 7.8 | 1.8 cm |
| float16 for the last two thirds + heads (simulator) | — | 11.1 px | 7.8 cm |

The full model: float16 26.4 ms (0.24 px, 0.14 cm), INT8 15.6 ms (13.7 px, 10.9 cm). Every layer alone loses little
(Rockchip's per-layer analysis: cosine > 0.999), but the error builds up over the whole backbone; quantizing any third
of it costs more than the whole budget (float16's 0.3 px). Rows without "simulator" were measured on the box (where
both were run, Rockchip's simulator agreed within ~0.2 px). Timings: the box with no app running, ~50 °C.

**The person detector** (it runs while nobody is followed: every 150 ms on an empty room, and when someone arrives):
float16 32.2 ms (NPU 28.0), INT8 18.4 ms (NPU 14.3), 29.5 → 15 MB. With the same float16 landmark model after it, on
all 205 pictures: the INT8 detector never missed a person the float16 one found, but found people in 17 of the 50
held-out snapshots where float16 found nobody (70 of the 150 calibration ones). Many were the real guitar player,
sitting in the dim light, that float16 (and in most of these frames MediaPipe) misses; some were **ghosts**: a guitar
and cushions on the empty couch, which the landmark model then "followed". A ghost keeps the AI tracking at 30 fps,
the opposite of what INT8 was for. Where both found someone, the INT8 detector's first crop was ~5 % of its size off in
position and size and ~9° in turn (the tracking corrects it from the next frame). With float16 output heads (23 ms):
11 of 50, the same crop errors.

**Heat it could have saved** (estimated from the lite switch, not measured, since nothing passed): INT8 landmarks
would take ~15 % off the NPU's load while someone is followed (~1 °C), the INT8 detector ~10 % while nobody is in view
(~0.5 °C).

**What's kept**: the tool chain (`calibration.py`, `convert.py --quant normal --calibration <dir>` →
`<name>.rk3566.int8.rknn`, `compare.py --crops`, `pose_npu_test --crop`) and INT8 support in the engine (it writes the
NPU's own 8-bit input codes, the landmark crop as `colour - 128`, and dequantizes INT8 outputs itself with their zero
point and scale; `PoseEngine` only ever loads the float16 files). **What could work later**: a pose model trained for
8 bits (quantization-aware training), or a chip whose NPU takes 16-bit activations (RKNN's w8a16 on newer chips); judge
it with `compare.py --crops` on held-out camera pictures first.

### Two players on the X98K Pro (2026-11-05)

**The box** (read over adb the same evening, Tenis! running): Rockchip RK3566 (`rk356x`, kernel 4.19.232, Android 11,
build X98KPRO_20231007), **4 Cortex-A55 cores** at 408 MHz-1.8 GHz (governor `interactive`), **Mali-G52 2EE** at
200-700 MHz (`simple_ondemand`), **one NPU core** (0.8 TOPS, 200-900 MHz, our `userspace` 600 MHz), **4 GB RAM** (3.98 GB;
~2.3 GB available with Tundu running, which held ~700 MB and ~1.5 of the 4 cores), 24 GB data (22 GB free), HDMI 1920 x
1080 at 60 Hz (no ALLM), passive cooling: `soc-thermal` trip points 75 / 85 / 115 °C under the kernel's
`power_allocator`. With one player in Tenis! it sat at **83.3 °C**, the GPU capped at 300-400 MHz (99 % busy) and the
game at 26-28 fps while the player was followed (29.9-30 while nobody was): the box is already at its heat limit with
one player in a 3D game.

**What two people cost the pose AI.** The RK3566 has a single NPU core, so two people means two landmark-model runs in
the same 33 ms camera frame, one after the other (MediaPipe's own multi-pose landmarker works the same way: a crop and
a run per person). Measured with `pose_npu_test --people 2` on a picture of two people (MediaPipe's test photo and its
mirror side by side), the app paused (no game), crops pipelined beside the NPU like the app does:

| Model | Two people, each every frame | Two taking turns (30 runs/s shared) | One person (for comparison) |
|---|---|---|---|
| lite | 28.0 ms of NPU thread a frame: at most ~35 frames/s | 14.4 ms | ~14 ms |
| full | 35.5 ms: at most ~28 frames/s (can't keep 30) | 20.8 ms | ~20 ms |

In a game the GPU shares the memory bus with the NPU and each lite run takes 15-19 ms instead of ~14 (Tenis!, measured
before this work), so **two people every frame doesn't fit beside a 3D game**; it would also keep the NPU ~90 % busy
(heat). Hence the budget: the engine (`pose_npu.cpp`) runs at most N landmark runs a second shared by the people
followed, taking turns; 30 a second = each player at ~15 poses a second with the NPU exactly as busy as with one player.
Each player's samples come ~66 ms apart; the game draws them on the camera clock (interpolated), so rackets and bodies
still move every frame, ~33 ms behind where they'd be at 30 Hz. The person detector (32-43 ms, more than a frame) looks
for a missing second player only every 0.5 s (a 45 ms frame then), never while two are followed.

**In a game, on the box** (`tools/dev/box-two.sh`, the same evening, the box at its heat limit, 82.8-83.9 °C all
along; Tenis!'s waiting screen, which draws the whole stadium and the four toys; PerfLab `npubench=1`: the NPU runs the
landmark model on fixed crops as if one or two people were followed, nobody needed in front of the camera; 100 s each
after 20 s settling, `[Perf]` and the AI's stats lines; `motion-service-py/sessions/two-2026-11-05/`, gitignored):

| Setup | Game fps | Frames late | Poses/s per person | NPU part of a frame | NPU busy | GPU clock |
|---|---|---|---|---|---|---|
| one person | 26.8 | 23 % | 30 | 16.8 ms | 46 % | 330 MHz |
| two, taking turns (`npurate=30`) | **27.2** | 20 % | 15 | 17.6 ms | 46 % | 340 MHz |
| two, 45 runs a second | 23.3 | 50 % | ~18-20 | 20.9 ms | 66 % | 280 MHz |
| two, both every frame | 24.0 | 41 % | 23 (can't keep 30) | 37.8 ms | 75 % | 470 MHz |
| split screen (versus), taking turns | 21.6 | 64 % | 15 | 18.5 ms | 50 % | 380 MHz |
| split screen, both every frame | 20.0 | 73 % | 21 | 40.5 ms | 74 % | 300 MHz |
| one person again (the heat at the end) | 27.3 | 18 % | 30 | 17.4 ms | 47 % | 330 MHz |
| split, taking turns, render scale 0.65 | 23.7 | 48 % | 15 | | 52 % | 383 MHz |
| split, taking turns, the crowd hidden | 24.2 | 42 % | 15 | | 50 % | 417 MHz |
| split, taking turns, the four toys hidden | **27.0** | 20 % | 15 | | 46 % | 400 MHz |

(One real player in Tenis! a few minutes before, the old build: 24.5-27.7 fps, GPU 200-400 MHz at 94-100 %, NPU
40-48 %.) **What it says**: (1) two players taking turns on the NPU cost the game nothing (27.2 against 26.8 fps): the
NPU is as busy as with one. (2) More NPU work slows the game either way: at 45 runs a second the kernel's power
allocator took the GPU down to 280 MHz (the box is at its heat limit, the NPU's watts come out of the GPU's); with both
every frame the GPU even ran faster (470 MHz) but the game was still slower, the NPU's traffic on the memory bus they
share. So **30 shared runs a second is the right budget on the RK3566**
(`BoxGraphics.Settings.TwoPlayerRuns`); a stronger chip (RK3576: 6 TOPS NPU, 2 cores) could give both 30. (3) The
**split screen costs ~20 %** (27.2 → 21.6), and **the four chibis are all of it** (hidden: 27.0, a single view's
speed): ~20-27k triangles each, two-sided, drawn once per half. The crowd is next (+2.6 fps hidden: ~600 spectators
of a box and two 54-vertex spheres, ~65k vertices), then resolution (+2.1 at 0.65). **Done**: the crowd's heads 6 x 4
instead of 8 x 5 (~30 % fewer vertices, every mode), the split screen at render scale 0.7 on the box
(`Settings.SplitScale`, `BoxGraphics.SetSplit`); measured again right after (same heat, 82.8-83.3 °C): **split screen
23.2 fps** (21.6 before, 48 % of the frames late), two taking turns in one view 27.3 (27.2). **Next** (not done): a low-detail chibi for the far team (each half
shows the other team small; fewer hair rings and body segments, built by `art/chibi` like the full one), single-sided
chibi pieces where nothing is seen from inside; then measure again (`box-two.sh split30 two30`).

**Not measured yet**: two real people on the box (the user and someone else): the feel at 15 poses a second each, the
detector finding the second player, two people close together or crossing.

### Two players, round 2 (2026-11-06)

The user played Tenis! with a second person on the box and asked for three things: lighter graphics with two players
plus general optimizations, the two-player game chosen with the remote (not started because two people show up), and a
fix for this: "when 2 people get close to each other then separate, the assignments change (I went from p1 to p2 and
they p2 to p1)".

**The swap.** Each person's next crop comes from their own landmarks. When two people stand close, one track's crop can
slide onto the other person; both tracks then follow the same person, `DropDuplicates` ended the newer one, and the
survivor (carrying the first person's id) stayed on the second person: the ids had changed people, and so had the player
numbers. Two fixes, either enough on its own:
- **The engine** (`pose_npu.cpp`, `Follow`): a track whose next crop lands on a person another live track already
  follows (`SamePerson`, hip centres within 0.4 of the smaller body's alignment distance) ends there, its filters reset;
  the track that was there keeps its person, and the detector (every 0.5 s while fewer than two are followed) finds the
  other one again as a new track.
- **The game** (`PlayerSlots`): during a match a player number is a *side of the room*. Player 1 is the left side; if the
  two bodies are the wrong way round (player 1's right of player 2's) by more than 0.3 torso lengths for 0.25 s of
  camera time, they change slots, and each slot keeps its calibration (where that side's player stood), so a tracker
  mix-up can't move the other player's toys. Standing level never swaps (a margin); a player lost and found again keeps
  their side's calibration. `PlayerSlotsTests`: the sides hold whatever the tracker does, level doesn't swap, found again
  keeps the side.

**The mode with the remote.** The waiting and results screens show three cards at the bottom: 1 LOJTAR (against the
computer), BASHKË (two against the computer), KUNDËR (two against each other); ← → on the remote chooses (remembered).
In a two-player mode both raise their hands (or press OK once both are in view and placed); one person alone with
raised hands plays solo after 4 s if nobody else is there. A second person walking into the picture no longer changes
anything in solo.

**Lighter graphics.**
- A **low-detail chibi**: `art/chibi/build.py -- --low --ao --export` builds every piece at `kit.LOW_DETAIL` 0.6 (fewer
  segments round every tube, ring and ellipsoid, a coarser head and shells, fewer rings along hair strands, rounded
  caps in two steps instead of three) into `Chibi_Low.fbx`: a dressed chibi **13.4k triangles instead of 33.6k** (the
  skirt's 16-wave hem keeps its 48 points: fewer alias it). `Chibi.Low` draws it instead: a second skinned mesh made on
  first use from the same pieces, skinned to the same bones (found by name), so posing, faces and colours carry on.
  Tenis! draws the **far team low always** (0.07-0.08 of the screen's height from Wii's camera: the detail doesn't show)
  and **all four low in the split screen** (`TennisGame.ApplyDetail`; PerfLab `chibi=full|low` forces one).
- **The crowd**: each spectator's hair is now the top of their head (one 6 x 4 ball with two colours,
  `PartMesh.Add(mesh, transform, colour, gloss, top, topFrom)`) instead of a second ball: ~43 % fewer crowd triangles.
- **The trees**: their crowns are 12-segment balls (168 triangles) instead of 22-segment ones (572): 30 trees, 48 with the
  split screen's near end.

**On the box: not measured yet.** The first run (`box-two.sh`: 8 minutes' warm-up, then each configuration with full-
detail chibis right next to the same with low-detail ones, PerfLab `chibi=full`) was cut short: the box was restarted
from its power menu (a long press of the remote's power button, then Restart; `pstore` and the boot reason
`reboot,userrequested` say so) six minutes into the warm-up. Not a crash or heat: Tenis! ran normally until then. The
warm-up itself (split screen, full-detail chibis, two taking turns on the NPU) gave **28.2-29.3 fps** while the box
warmed from 52 to 74 °C with the GPU at 600-700 MHz: a box below its heat limit is far faster than the 23.2 fps
measured at 83 °C the day before, so compare configurations only at the same heat (back to back, as the script does).
To do: `bash tools/dev/box-two.sh -t 480 <out> warmup`, then `-t 120 <out> split30_full split30 one_full one
split30_noskin two30`. (If the script is stopped, delete `perflab.txt` on the box: Tundu reads it again after a
restart.)

### Tenis!'s frame on the box (2026-11-06)

The user asked for more frame rate in Tenis! (one player). **How it was measured**: `tools/dev/box-tennis-gpu.sh`, Tenis!'s
waiting screen (the whole stadium, the four chibis, the HUD and the skeleton mirror), PerfLab switching one thing at a
time, **uncapped** (`fps=60`: a cheaper frame shows as more frames instead of an idle GPU), the pose AI's NPU load of one
tracked person (`npubench`), 60 s each after 15 s settling. A first run with the governor free was spoiled by the heat:
from ~80 °C the power allocator took the GPU from 700 to 333-617 MHz between configurations (and GPU cycles per frame
aren't comparable across clocks either: memory stalls cost fewer cycles at a lower clock). So the second run pinned the
GPU (`-g 300`; the thermal cap held it at 200-250 MHz all along, the box at 82-83 °C: exactly where it sits when the
user plays, GPU 100 % busy). `motion-service-py/sessions/tennis-gpu300-2026-11-06/` (gitignored).

| Change (old build) | fps | |
|---|---|---|
| as built (render scale 0.8) | 21.0-21.1 | (three times: no drift) |
| render scale 0.7 / 0.6 | 23.5 / 26.1 | +12 % / +24 % |
| the four chibis hidden | 25.3 | +20 % |
| the near back player hidden (full detail + racket) | 22.6 | +7 % |
| the far pair hidden (two low-detail chibis, small) | 22.1 | +5 % |
| all four low-detail (a third of the triangles) | 20.9 | **0 %** |
| the stadium mesh hidden (court, stands, trees: the sky instead) | 25.1 | +19 % |
| the crowd hidden | 22.2 | +5 % |
| IMGUI off (`TennisGame` and the mirror disabled: the game frozen too) | 24.0 | +14 % |
| the net hidden | 21.1 | 0 % |

(At 700 MHz, before the throttling, the first run gave the same picture: the chibis ~28 % of the GPU's cycles, render
scale 0.7 -17 %, the crowd -8 %; 38.4 fps uncapped.) **What it says**: (1) a skinned character costs ~5 % of the frame
whatever its size on the screen or its triangle count (the low-detail chibis saved nothing): a fixed cost per
character, the GPU (compute) skinning's dispatch rather than geometry or pixels; (2) at the hot box's clock the frame
is pixel-bound: every 10 % of the render scale is ~12 %; (3) `androidBlitType` was **Always**: Unity drew every frame
into an extra offscreen buffer and copied it to the screen, on top of URP's own copy for the 0.8 render scale (two
full-screen 1080p copies a frame); (4) the stadium's lawn lay under the whole court (every court pixel shaded twice).

**Done, measured the same way on the new build** (`tennis-gpu300-new-2026-11-06/`, the same 200-250 MHz, 83-84 °C):
`androidBlitType` Auto (`BoxBuild`: Unity draws straight into the screen's buffer), the lawn a frame round the court,
and Tenis! drawn at 0.75 of the screen's resolution straight into the screen's buffer, the TV's display hardware
scaling it up (`BoxGraphics.Settings.ScreenScale`, `Screen.SetResolution`; PerfLab `res=`): no URP texture and no copy
at all.

| New build | fps |
|---|---|
| render scale 0.8 (as before, but blit Auto and the lawn fix) | 23.4 (three times) |
| the four chibis hidden | 28.5 (old build 25.3) |
| screen scale 0.8 (the display scales; the same pixels as before) | 27.2-27.3 |
| **screen scale 0.75 (now Tenis!'s setting)** | **28.4** |
| screen scale 0.7 | 29.8 |
| render scale 0.7 (URP texture + copy) | 25.7 |
| the crowd hidden / IMGUI off | 25.1 / 26.3 |

So at the hot box's clock Tenis! went from 21.0 to 28.4 fps uncapped (+35 %), and at the same pixel count the display's
scaler alone is worth +16 % over URP's render scale. On screenshots the 3D scene at 0.75 looks like the old 0.8; the HUD
text is a touch softer (IMGUI now draws at 1440 x 810 too). **Skinning on the CPU** (`meshDeformation` CPU, tried in the
same build) changed nothing on the GPU: the four chibis still cost 7.6 ms a frame (8.1 with GPU skinning), so it went
back to GPU (batched). What a chibi costs is still not understood: ~1 ms each for the small far ones, ~3 ms for the near
ones, not geometry (a third of the triangles: no change), not the skinning mode; perhaps the per-frame skinned vertex
buffer or the two-sided fragment work. Next leads: Arm's GPU profiler (Streamline) on the box; `_Cull` back faces for
the closed pieces (the material property can be switched at runtime to measure); a cheaper Tundu/Athlete for far
characters.

**The other games, the same day** (the user: "yes, do the others"). `tools/dev/box-gpu-ab.sh` (any scene, any PerfLab
settings A/B; `scaler-ab-2026-11-06/`), each game as built against the scaler at its own resolution, uncapped, the hot
box (82-83 °C, the clock 200-270 MHz): Vrapo! 21.2 -> 24.6 fps (0.8), Shpata! 21.5 -> 25.0 (0.8), Fluturo! 22.7 -> 28.6
(0.8), Vishu! 20.4 -> 24.0 (0.7), Sporte!'s football 18.5 -> 18.5 (0.72) with the thermal cap dropping the clock from
250 to 217 MHz in between (+15 % a MHz). Screenshots on the TV: the 3D scenes look the same, the HUDs' small text a
little softer (Vishu!'s labels at 0.7 too: legible), Sporte!'s big screen dark in both (it is). All of them use the
scaler now (`BoxGraphics.For`). **As played** (the new build, its own settings: 30 fps cap, the governor free, one
tracked person's AI load, the box at 82-83 °C): Tenis! 29.7-29.9 fps (1-2 % of the frames late), Vrapo! 29.5-29.9,
Shpata! 29.2-29.7, Fluturo! 29.7, Vishu! 25.0-25.5 (its main and render threads ~21-22 ms: the CPU is its limit now),
Sporte!'s football 25.9-26 (as before at this heat: still GPU-bound with nine athletes and 12k crowd quads); the home
screen back at 1920 x 1080. Before (the same heat): Tenis! 24.5-27.7, Vrapo! 26.7-27.0, Sporte! 25.6-27.7.

### Tenis! round 5 on the box (2026-11-07)

The user asked for "more styling, shadows, effects, shaders, but without breaking the performance". **How it was
measured**: `tools/dev/box-tennis-gpu.sh -g 300` as above (Tenis!'s waiting screen, uncapped, the GPU pinned at 300 MHz,
one tracked person's NPU load; the box at 72-81 °C), new configurations `nofx` (all of the round's shadows, particles and
the ball's glow hidden), `noshadows`, `nodetail` (PerfLab `detail=0`: the stadium without Tundu/Toon's world-space grass
detail; stays off until the app restarts, so it runs last). Logs in `motion-service-py/sessions/tennis-gpu300-2026-11-07*`
(gitignored).

| Configuration | fps | what it says |
|---|---|---|
| first build: as built / `nofx` / `noshadows` | 22.6 / 22.6 / 22.7 | the sun's shadows (one dynamic mesh for the toys, the rackets and the ball, one static mesh for the net, posts, chair, benches, walls), the particles and the glow: **nothing measurable** |
| first build: `nogui` / `nostadium` / `nocrowd` / `noskin` | 32.3 / 28.2 / 24.1 / 26.0 | the new menu's IMGUI cost **~13 ms** a frame (the old screen's ~4.5) |
| HUD slimmed once (one layer per panel, no outline copies on dark panels, two-draw drop shadows over the scene, no pills for options not chosen): as built / `nogui` | 26.6 / 32.7 | ~7 ms |
| slimmed again (a plain panel instead of a rounded one, two arrow characters instead of the animated chevrons, a two-draw title): as built / `nogui` | **28.5** / 32.4 | **~4.2 ms**, as cheap as the old screen |
| as built / `nodetail` | 28.5 / 29.0 | the grass detail ~0.6 ms (2 %) |

**A live match** (the scripted player sent from the laptop over Wi-Fi: `fake_sender --script tennis --host <box>`, the
game's own 30 fps cap, the governor free, 80.6 °C, the GPU at 400 MHz 99 % busy, every effect on): **29.7-30.0 fps,
0-1.7 % of the frames late**, the same as round 4 as played. (The scripted player's swings are mistimed over Wi-Fi, the box's
clock is offset, so it lost 0-2 within a minute; the later stages of that run measured the results screen.) **Lesson**:
on this Mali, IMGUI's rounded panels (every pixel of the panel shaded, one draw per layer: shadow, gradient, rim) and
outlined text (five or ten draws of the text) cost far more than world-space effects; measure HUD changes with `nogui`.

### Tenis! round 6 on the box (2026-11-07)

Singles (one toy a side in Solo and Versus instead of four) and the bigger, strung racket (a tube frame of 36 x 8, an
open throat, 9 strings; ~1k triangles, two of them now instead of four flat ones). The same build measured three ways
(logs in `motion-service-py/sessions/tennis-gpu300-2026-11-07-round6` and `tennis-live-2026-11-07-round6`, gitignored):

| Measurement | fps | before |
|---|---|---|
| `box-tennis-gpu.sh -g 300` `base` (Tenis!'s screen uncapped, GPU pinned at 300 MHz 97 % busy, one person's NPU load, 68-71 °C) | **30.0-30.2** (7-13 % of frames over 34 ms; main 13.6 ms, present wait 19.5, render 9.2) | round 5, four toys: 28.5 |
| the same, new config `split` (PerfLab `split=1`: versus's split screen, two toys drawn twice) | **26.5** (main 22 ms, render 18.5) | round 4, four toys drawn twice at scale 0.7: 23.2 |
| a live match, the game's own settings (30 fps cap, the governor free, the scripted player over Wi-Fi, 71-72 °C) | **29.7-30.0**, 0-1 % of frames late | round 5: 29.7-30.0 at 80.6 °C |

So singles gave back ~2 ms of GPU a frame at a pinned clock (the chibis cost ~1-3 ms each, "Tenis!'s frame on the box"),
and the strung racket costs nothing visible. The box was cooler than in round 5's runs (68-72 against 72-81 °C); at a
pinned clock that shouldn't move the numbers much. A screenshot from the TV (`screen_80s.png`): the strings read as a
lattice at the near toy's size; the far toy's strings lie on a pale bed (open ones that small would shimmer).

Round 7 (the same evening: the computer's sure first five returns, swings up as drives) changes no drawing: the
same scripted live match ran 29.7-30.0 fps, 0-1.7 % of frames late (`tennis-live-2026-11-07-round7`).

### Volejboll! on the box (2026-11-08)

The first build of the ninth game (four chibis on Sporte!'s beach with its crowd and the sea, Tenis!'s settings in
`BoxGraphics`: screen scale 0.75, no real-time shadows, the lite model, the NPU shared by two players). APK 88.4 MB.
Logs in `motion-service-py/sessions/volley-live-2026-11-08` (gitignored).

| Measurement | fps | notes |
|---|---|---|
| the waiting screen (the menu, the moves' cards), the game's 30 fps cap, 81 °C | **29.6-30.0**, 0.3-2.7 % of frames late | main 11.5-13.4 ms, render 7.4-9.1 |
| a live match, the scripted player over Wi-Fi (`fake_sender --script volley --host <box>`), 80-82 °C, the GPU 200-600 MHz at 48-100 % | **29.1-30.0**, 0-2.7 % late | main 10.7-13.1 ms; the first line includes the scene's start |
| the split screen (PerfLab `split=1`: versus, four low-detail chibis drawn twice), 82.8 °C, the GPU held at 200 MHz by the heat | **25.7-27.4**, 16-28 % late | main ~22 ms, render ~19: Tenis!'s split screen was 26.5 uncapped at a pinned 300 MHz |

The scripted player lost the live match 0-7: over Wi-Fi nothing times the computer's balls to its script (the box's
clock is offset too), so its poses and hits mostly came at the wrong moments; 20 touches were played in the minute.

### Volejboll!'s defence on the box (2026-11-08 evening)

The build with the block-or-receive choice (the "!" over a smasher on Easy, the BLLOKO / PRIT panel at the bottom while
an attack is set up, the hitter's tells, the block jump). Same settings as above.

| Measurement | fps | notes |
|---|---|---|
| the waiting screen (four move cards now), 73 °C, the GPU at 700 MHz | **30.0**, 0 % of frames late | main 11.3-11.8 ms, render ~8 |
| a live match, the scripted player over Wi-Fi, 73 °C | **29.5-30.0**, 0-1 % late | main 10.1-12.6 ms; the 1 % lines are the match's start and the rematch (a GC) |

**Measure only on a quiet box.** The first try of this build ran 18-27 fps with the main thread at 25-45 ms, and Tenis!
(30 fps the day before) ran 26-27: the box had been restarted an hour before, and Android was recompiling Google Play
Services in the background (`dex2oat32 ... com.google.android.gms ... --compilation-reason=install-dm`, ~60 % of a core,
`system_server` ~57 %, load average 10). It took ~10 minutes; the load fell from 10 to 4 within two more. Check
`adb shell "ps -A | grep -c dex2oat"` (0) and `cat /proc/loadavg` (< 4.5) before measuring anything.

### Gara! on the box (2026-11-08 night)

The first build of the tenth game (six karts with their chibis on a track built at runtime, the scenery static-batched;
`BoxGraphics` "Kart": Volejboll!'s settings, screen scale 0.75, no real-time shadows, the lite model, the NPU shared by
two players, split 0.7). APK 89.3 MB. The box was quiet (no `dex2oat`, load ~2.7).

| Measurement | fps | notes |
|---|---|---|
| a live race on Easy (the coast), the scripted player over Wi-Fi (`fake_sender --script kart --host <box>`), 76-77.5 °C, the GPU at 700 MHz, 49-66 % busy | **29.9-30.0**, 0-0.7 % of frames late | main 12-16 ms, render 9-13; the first line includes the scene's start (worst frame 634 ms) |
| the split screen (versus, `--script kart_duo --alternate`: two scripted players, one per packet like the box), 79-80 °C, the GPU at 600-700 MHz | **28.0-29.5**, 3-12 % late | main 23-26 ms, render 21-24: two cameras and two HUDs (the chibis are low-detail in the split screen already) |

The scripted player finished 2nd of 6 in 113.5 s (2 tricks, 26 hops, 5 items used). A run's start stalls ~0.4 s (the
next run's line: worst 417 ms, 8 GCs): `StartRun` draws new opponents, and their chibis are built on that frame (the
kept views only match the same looks). Over Wi-Fi every move's age hit `KartGame`'s 0.35 s cap ("seen 350 ms ago"):
the laptop's clock differs from the box's, while the box's own camera stamps frames on its own clock. PerfLab's
`scene=` doesn't reload the scene that is already open: load `scene=Menu` first to get back to a game's waiting screen
(the remote's → there moved the players row to versus; ← put it back).

## Recognition benchmarks (fill in as devices are tested)

| Device | Backend | Model | Camera fps | AI fps | AI time | Capture→game latency | Notes |
|---|---|---|---|---|---|---|---|
| Laptop (Ryzen 7 7840HS, CPU) | py-mediapipe | full | 30 (20 in a dim room) | 30 | 15 ms | ~17 ms to send | 2026-10-05 |
| MuMu Player (emulator, x86 + ARM translation) | android-mediapipe GPU | lite (auto) | 30 | 30 | 12–14 ms | 14 ms (full model, earlier run) | 2026-10-07. Works end to end: body tracked, Fruta playable. First GPU frame takes ~15 s to set up |
| MuMu Player (same) | android-mediapipe GPU | full (auto kept it) | 30 (AE range [30, 30]) | 29.9 | 9.7 ms | | 2026-10-08, after the motion polish: frame-rate floor, reused frame buffer. ALLM: not supported (emulator) |
| RK3566 box (X98K Pro, HD USB Camera at 1024x768) | **android-rknn NPU** (float16) | full, auto → lite past 34 ms | 30 offered, ~20.5 analysed while detecting | ~20.5 (nobody in view) | detector 41 ms; tracking lite 25.5 / full 34.4 ms (box, no game) | not measured yet | 2026-10-30. Vrapo! a steady 30 beside it. Landmarks within 0.13–0.53 px of MediaPipe's. 2026-11-01, the box idle at ~50 °C: lite 18.7, full 26.4, detector 32.2 ms; INT8 would be 12.1 / 15.6 / 18.4 but isn't accurate enough ("INT8 pose models" above) |
| RK3566 box (X98K Pro, HD USB Camera at 1024x768) | android-mediapipe GPU | full, auto → lite after 10 s | 30 offered, 10–11 analysed | 10–11 | 86 ms with a person (lite), 90–113 ms without (the detector) | ~170 ms | 2026-10-30, a very dark room. The game drawn at 10 fps didn't make it faster; Vrapo! fell to 22 fps beside it |
| RK3576 box | android-mediapipe / rknn | | | | | | |

Read the numbers from **Settings → Kamera** (Recognition Lab) on the device.
