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

### Picture filters for the pose AI (2026-11-09)

The user: "I need to understand how the AI model works on the box, and evaluate what others do (edge detection,
filters, post-processing the video) to always get the best results." Explainer with the results:
https://claude.ai/artifact/KGbirikRRZNidbsAnEvzgx. Tested with **`tools/motion/filter_bench.py`**: MediaPipe lite and
full (the box's models; its NPU engine matches MediaPipe within ~0.5 px) on all 2,250 frames of `routine-20261101` (the
user ~3–4 m away, evening, tuner level 3) through 15 picture filters, and on a copy made two stops darker (`dim`: the
camera's measured temporal noise doubled at every brightness, 10 % darker, JPEG 80; its grain is about this camera's own
at the tuner's top level, `sessions/clips/fixed/xga_level5`). Scored against the full model on the unfiltered clip
smoothed both ways in time (the dim copy: the same model on the original); a control (each frame re-saved as JPEG 95)
gives the noise floor. Torso 157 px in the picture; the landmark crop ~590 px, so the model sees the player at ~0.44x
(torso ~69 px, a hand 15–20 px of its 256).

| | Lite: lost % / found after / wrist error (% torso) / 3D forearm off % | Full: the same |
|---|---|---|
| As recorded, no filter | 0 / 0 s / 4.93 / 14.7 | 0.4 / 0 s / 3.98 / 3.6 |
| control (JPEG 95) | 0 / 0 / 4.96 / 14.5 | 0.6 / 0 / 4.11 / 3.4 |
| local contrast (CLAHE 2 / 4) | 0 / 0 / 4.96–5.07 / 11.2–9.9 | 3.9–4.5 (the walk-up) / 0 / 4.31–4.58 / 3.2–3.7 |
| mirrored twin (2 runs a frame) | 0 / 0 / 4.72 / 8.2 | 0 / 0 / 4.14 / 4.0 |
| denoise (blur, bilateral, median, temporal) | 0–0.2 / 0 / 4.92–4.98 / 16.8–18.6 | 0–0.6 / 0 / 3.81–4.03 / 3.3–3.6 |
| grayscale / sharpen / edges drawn over / edges only | 10.9 (first 8 s) / 0.9 / 2.1 / **100** | 11.8 / 2.3 / 5.5 / **100** |
| Dim copy, no filter | **32.8 / 22.2 s** / 6.01 | **34.0 / 22.2 s** / 5.18 |
| brighten (gamma 0.6) | 54.4 / 36.3 s | 53.2 / 36.3 s |
| auto levels / CLAHE 4 on every frame | 18.8 / 5.8 s; 3.4 / 1.2 s, but big wrist misses 21.6 % (none 10.4) | 10.6 / 5.8 s; 9.2 / 1.2 s |
| **CLAHE 4 for the detector only** (`find-clahe4`: only while nobody is followed) | **2.1 / 1.2 s**, wrists on the same frames 5.90 (none 6.01) | **6.0 / 1.2 s**, 5.06 (5.18) |
| auto levels for the detector only | 14.1 / 5.8 s | 32.0 / 5.8 s (lost again on the plain frames) |
| detection threshold 0.3 (`lowdet`) | 39.6 / 22.2 s | 40.8 / 22.2 s |
| sharpen / edges | never found | never found |

**Ghosts**: seven clips of the empty room (`sessions/clips/fixed`, the guitar and cushions on the couch; the `sweep`
clips also have someone sitting at the right edge, not used): frames with a "person" on the couch, lite / full: no
filter 11.7 / 6.7 % (lite 73 % in `xga_level3`: today's models already lock onto the guitar there), threshold 0.3 26.0 /
18.4, auto levels for the detector 49.5 / 35.9, CLAHE 4 for the detector 38.7 / 24.4. The ghost lies on the couch:
nose below the hips. **An upright check** (nose above the shoulders' middle + 0.1 torso, shoulders above the hips)
leaves 0 / 0, 11.1 / 0.3, 0.3 / 0, 2.2 / 1.0 %, and failed **none** of the real player's found frames in any run (the
walk-up close and the dim copy included). **The camera tuner's reading**: its metering area (the middle, x 15–85 %, y
10–90 %) averaged 97 over the routine, the player's own box 50 (below its own VERY_DARK 55), the tabletop 154.

**So**: in usable light no picture filter improves the skeleton (the full model does: 3D forearms 14.7 → 3.6 %);
grayscale, sharpening, edge boosting and edges only hurt the detector; denoising barely matters and makes lite's 3D
forearms worse. In a dark room the weak link is the person detector, and contrast for the detector alone fixes it
without touching tracking, but only safely with the upright check (the check is worth having anyway: a ghost track
keeps the AI awake and, with one player, keeps the detector from looking for the real one).

**Both are in the engine (the same evening, user: "yes, add both")**: `pose_npu.cpp` `LocalContrast` (OpenCV's CLAHE,
8 x 8 tiles, clip 4, on the luma of the 224 x 224 letterboxed picture's area, colour kept) in `DetectorInput`, and
`Upright` checked during a new track's first 15 landmark runs (`Track::frames`; someone already followed is never
dropped); `Frame::rejected` → JNI slot 9 → `NpuPose.rejected` → the stats line's "N ghosts". `TUNDU_NPU_BOOST=0` /
`TUNDU_NPU_UPRIGHT=0` turn them off (pose_npu_test). **On the box** (`pose_npu_test --clip`, the real engine, app
paused; clips in `/data/local/tmp/npu/boost`): the dark copy's first 30 s: found in 848 of 900 frames (old 238), from
1.2 s (old 22.2 s), 53 detector runs (old 663), lite and full alike; the normal clip's first 20 s: 600 of 600 both,
wrists old vs new within 0.7–0.9 % of a torso (median); the seven empty-room clips: ghost frames old 15.6 %, boost
alone 39.4 % (lite) / 25.4 % (full), boost + check **0 %** (56 / 37 ghost tracks rejected). Cost: the detector's input
3.7 → 8.4 ms of CPU per detector run (only while nobody is followed). Contact sheet of the crops through every filter:
`contact_sheet.py` in that session's scratchpad (not kept).

**In the app and a real dark room (2026-11-10 morning, the APK of 09:50, 92.8 MB)**: installed on the box, no errors
(only a SurfaceView buffer message at start), the stats line ends in "N ghosts". The room was dark (curtains closed:
the camera's own picture measured 13 of 255 before the tuner raised it to level 5, mean ~80-110). Live: back in front
after a recording, the app found the user within the first 5-s window and tracked them at 30 poses/s with full (NPU
22-24 ms); 0 ghosts in every line, the empty dark room included. **A raw clip** (`box_clip.py dark-20261110`, 80 s at
level 5, `motion-service-py/sessions/clips`, gitignored; the user in view from ~15 s, standing ~3 m away in a dark
T-shirt, someone else sitting at the left; the camera still low on the table) through `pose_npu_test` old (both off)
against new: lite found the user in 31 → 281 of 2400 frames, full 22 → 421; first found at 74.4 → 60.6 s; held 1-2 s →
9 s (lite) / 13 s (full); 0 ghosts either way. **So the boost helps in a real dark room but isn't enough**: the
grainy level-5 picture still kept the detector from finding a person in plain view for ~45 s. Next to try (not done):
an area-averaged detector input (the 1024 → 224 letterbox now samples ~4 of the ~21 camera pixels behind each detector
pixel bilinearly, so the grain isn't averaged out; a box average would cut it ~4.6x), then a few frames averaged while
nobody is followed; A/B both on this clip (`pose_npu_test --clip`, `/data/local/tmp/npu/boost`).

    motion-service-py/.venv/Scripts/python tools/motion/filter_bench.py dim <clip.mjpg> --out <dir>
    motion-service-py/.venv/Scripts/python tools/motion/filter_bench.py run <clip.mjpg> --out <dir> [--filters a,b] [--jobs 4]
    motion-service-py/.venv/Scripts/python tools/motion/filter_bench.py score --out <dir> [--json results.json]

(60 lite + full runs of 2,250 frames take ~25 min with 4 jobs on the laptop; the dim copy ~4 min.)

### Tracking that listens to the model (2026-11-10)

The user: "my current tracking model is not good enough: it doesn't stay stable, glitches, doesn't always read inputs,
hands don't get tracked correctly; restrictions: bad lighting, the box's processing power; make it production ready."
The models stay MediaPipe's (lite and full on the NPU). What changed is everything the engine does with their answers
(`pose_trust.h`, used by `pose_npu.cpp`), measured first in a lab on the recorded box-camera clips.

**The lab** (`tools/npu/tracklab*.py`, WSL with LiteRT; MediaPipe's original models, which the box's float16 builds
match to ~0.3 px): `tracklab.py` is the box's engine in Python (detector, crop, landmark model, MediaPipe's filters),
`tracklab_trust.py` the new logic, `tracklab_score.py` the judge, `tracklab_view.py` contact sheets of frames with the
skeletons drawn. **The reference ("teacher")**: MediaPipe's *heavy* pose model (five times lite's cost: never for the
box) run six times a frame, on the tracking crop, its mirror image and crops nudged in size and place, the median of
the answers; a joint counts only where the teacher sees it (visibility > 0.5) and agrees with itself (spread < 8 % of a
torso). A frame is **bad** when a joint is more than a quarter of a torso from the teacher. (The heavy model was taken
from the MediaPipe files in The Link's folder; a hand model there was looked at too and dropped: at 3-4 m a hand is
~18 px of this camera's picture and a blur when it moves, nothing for a hand model to read. The Link's hands are good
because its user sits a metre from a 720p camera in a lit room.)

**What the frames show** (`routine-20261101`, the user 3-4 m away in evening light, the camera low on a table that
hides the legs): the models disagree where the camera can't see (an arm behind the body in a side-on punch, the legs
behind the table: lite's hidden legs jump about), on blurred hands, and in the walk up close (the full model reads the
body as seen from behind for up to a second: left and right exchanged). The picture is the limit for hands: a moving
hand is a 15 x 25 px smear.

**What MediaPipe leaves on the table**:

| | Finding (lite, the routine clip) |
|---|---|
| The heatmap as the model's opinion of itself | The landmark model has two heads: a regression (the position) and a heatmap (64 x 64 scores per landmark), which MediaPipe only uses to nudge the position inside a 7 x 7 window. The heatmap's score under a landmark's final position tells a wrong wrist from a right one with AUC **0.98** (elbows 0.96, shoulders 0.99); the model's "visibility" 0.84, and the low-passed visibility the games got **0.72**: "visibility < 0.5" flagged none of the bad wrist frames |
| The visibility's low pass | MediaPipe smooths visibility with alpha 0.1: a limb coming back into view counted **6 frames** late, one going out of view 6 frames late (now 1 and 1) |
| One low pose score ends the track | The pose score dipped under 0.5 for single frames (0.39, 0.44, 0.28 between 0.9s). MediaPipe drops the person at the first, and the detector has to find them again: 19 frames in the evening clip (full), **12 s** and then never in the dark-room clip |
| The detector's input | MediaPipe shrinks the picture to 224 x 224 by bilinear sampling: 4 of the ~21 camera pixels behind each detector pixel, so a dark room's grain goes straight in. And yesterday's local contrast (CLAHE) amplifies real grain: it had won on a *synthetic* dim copy; on the real dark-room clip it found the player in 0.3 % of the frames |
| Other ways to decode | Following the heatmap's peak instead of the regression, lower refinement thresholds, a window moved onto its best cell: no better (bad wrist frames 1.0-1.2 % either way). An anti-aliased or brightened landmark crop: no better |

**What the engine does now** (each part can be switched off for comparing: `TUNDU_NPU_TRUST=0`, `TUNDU_NPU_SWAP=0`,
`TUNDU_NPU_COAST=0`, `TUNDU_NPU_BOOST=0|clahe` in `pose_npu_test`'s environment):

1. **Trust**: every landmark gets a trust 0..1 from the heatmap's best score in the 3 x 3 cells under its final
   position (0 under 0.15, 1 from 0.40; a landmark outside the crop or the picture counts as given). A landmark with
   nothing under it (< 0.20) moves onto the heatmap's best peak when that is clear (>= 0.45) and elsewhere.
2. **Hold**: a sample is pulled toward where the landmark was, riding on the body's own move (the torso joints' shift,
   each weighted by its trust), by `1 - trust` (at least 6 % of the way always gets through, so a hidden limb drifts
   to the model's guess over ~0.5 s). Then MediaPipe's filters as before: a sure sample goes through exactly as
   before (measured lag unchanged: 10 ms against the teacher), a wrist flipped away for a frame or two doesn't, a limb
   the camera can't see rests. World joints the same way.
3. **Visibility** as sent to the games: the model's own, low-passed with alpha 0.5, capped at `0.25 + 0.75 x trust`
   of this frame. Every game's own gate (`HandPresence` 0.5 / 0.3, `SwingDetector` 0.8 / 0.6, `PlacementCheck` 0.3...)
   now skips exactly the unsure samples, in the frame they happen.
4. **Left/right guard**: the shoulders' signed width across the body's axis (+ = facing the camera). Clearly facing
   (>= 0.45 torso, on the last output whose shoulders the model was sure of) and, within 0.25 s, clearly seen from
   behind (<= -0.45): nobody turns that fast (>= 400 degrees/s), so the sides are exchanged back, for at most 30 frames
   in a row. Only that direction: a body read as facing the camera again always stands, so a flip can't be locked in
   (a first version that only compared with the last frame did lock one in for 30 frames once a garbage frame had
   slipped between).
5. **Coasting**: a person followed for at least 15 frames is kept through up to 6 landmark runs in a row with a pose
   score under 0.5 (same crop box, the smoothing as it was; those frames carry no pose). The stats line counts them
   as "dips", apart from "lost".
6. **The detector's eyes** (`DetectorEye`): each picture shrunk by averaging whole blocks of camera pixels (4 x 4 of
   the 4.57 x 4.57 behind a detector pixel at 1024 px), the last four kept; a look takes the newest alone or the
   average of those of the last 0.15 s, by turns (someone walking in is sharpest in one frame, grain goes in the
   average), and brightens it with a gamma toward a mean of 0.45 when darker (at most gamma 0.5). The service's idle
   looks: every 250 ms (was 150), the three frames before each look watched for the average (`MotionService`
   `IDLE_WATCH_MS`, `PoseNpu::Watch`): fewer NPU runs than before (4 a second, not 6.7).

**The person detector, frame by frame** (the share of frames in which it finds the player; `det_sweep` in the lab):

| Detector input | Dark room (real, `dark-20261110`) | Dim copy (synthetic) | Evening (`routine`) | Empty room: frames with a "person" |
|---|---|---|---|---|
| MediaPipe (bilinear) | 0.0 % | 4.0 % | 66.4 % | 0-2 % |
| + local contrast (the box since 2026-11-09) | 0.3 % | 12.8 % | 58.8 % | 2-64 % |
| Area average, one frame | 5.5 % | 22.9 % | **85.7 %** | 0 % |
| Area average, 4 frames | 44 % | **43 %** | 81 % | 0 % |
| Area average, 4 frames, brightened | **63 %** | 39 % | 79 % | 0 % |
| ... 8 frames / frames 150 ms apart / a blur / CLAHE on top | 59 / 21-27 / 2 / 8-20 % | | 65 % (8 frames: a moving player smears) | |

**On the box** (the real NPU engine, `pose_npu_test --clip`, old = this morning's engine, new = this one; scored by
`tracklab_score.py` against the teacher; bad frames a minute per joint, left and right averaged):

| `routine-20261101` (75 s, evening) | Lite old | Lite new | Full old | Full new |
|---|---|---|---|---|
| Wrists more than a quarter torso off | 18.3 | **13.5** | 15.9 | **4.0** |
| Elbows | 8.0 | 4.0 | 14.3 | 3.2 |
| Shoulders | 3.6 | 0.4 | 12.3 | 3.2 |
| Hips | 4.0 | 0.8 | 11.1 | 0.8 |
| Frames without the player | 0 | 0 | 18 | **0** |
| How well the sent visibility tells a bad wrist frame (AUC) | 0.76 | 0.91 | 0.86 | 0.93 |
| A hidden wrist's step per frame, median / 90th percentile (% of a torso) | 4.5 / 18.0 | 3.3 / 13.4 | 5.0 / 23.2 | 4.4 / 15.2 |
| A hidden knee's / ankle's (behind the table) | 2.2 / 8.8, 3.3 / 12.4 | 1.8 / 5.7, 1.8 / 5.8 | 1.4 / 4.5, 2.1 / 5.9 | 1.4 / 4.5, 1.5 / 4.5 |
| Median distance from the teacher, wrists (% of a torso) / lag | 4.6 / 10 ms | 4.6 / 10 ms | 4.4 / 8 ms | 4.4 / 10 ms |
| Tenis!'s swing detector: swings a minute, real / glitch against the other model | 104, 123 / 8 | 101, 120 / 7 | | |

| `dark-20261110` (80 s, the dark room, the player in view from the start) | Lite old | Lite new | Full old | Full new |
|---|---|---|---|---|
| Frames with the player, of 2400 | 281 | **2239** (2279 with the idle looks) | 421 | **2283** |
| First found after | 60.6 s | the first frame | 60.6 s | the first frame |
| Detector runs / dips coasted through / frames with the sides put back | 2119 / - / - | 140 (66) / 21 / 5 | 1979 / - / - | 110 / 8 / 13 |

Ghost tracks started on the seven empty-room clips (the upright check ends them either way): lite 56 → 8 (all in the
1080p clip), full 37 → 1. A detector look: 43.3 → 41-42 ms (the block average is cheaper than the bilinear warp plus
CLAHE); a tracked frame: lite's NPU stage 12-13 ms as before; the pipelined and two-thread orders run the clip like the
serial one (one or two dips more, coasted through). The lab's numbers for the same clip with MediaPipe's own files:
lite wrists 18.7 → 11.9 bad a minute, full 15.1 → 4.4.

**The C++ is checked against the lab on the PC**: `tools/npu/trust_check.sh` (WSL) dumps two stretches of the routine
clip from the lab (the model's raw answers and the lab's results), replays them through `pose_trust.h` compiled with
g++ (`trust_check.cpp`), and compares every number: positions within 0.0007 px, trust, visibility and world joints
equal, the same frames' sides put back, the same landmarks moved onto peaks, both heatmap layouts; and the detector's
input pictures equal to the last bit. Run it after changing either side.

**What this doesn't change**: where the picture has no answer, there is none. A wrist is still a quarter torso off the
teacher ~13 times a minute per hand with lite in a routine full of punches and waves (the model is sure and wrong, or
the teacher is), the median distance (4.6 % of a torso) and the shake of a calm limb are the model's and the picture's
own, and the camera still gives 30 pictures a second with ~30 ms exposures in a dim room. The levers left, by size:
more light and the camera at the TV instead of low on a table (the user's side); the full model in every game (the
NPU's time: an INT8 build trained for it, quantization-aware, would halve it; plain post-training INT8 isn't accurate
enough, see above); the camera's 60 fps modes (Android's camera HAL hides them).

    # the lab (WSL, the RKNN venv):
    python tools/npu/tracklab.py run <clip.mjpg> --model heavy --variant teacher --out <dir>
    python tools/npu/tracklab.py run <clip.mjpg> --model lite --variant stock --out <dir>      # the old engine
    python tools/npu/tracklab.py run <clip.mjpg> --model lite --variant trust --out <dir>      # the box's engine now
    python tools/npu/tracklab_score.py <dir>/<clip>.heavy.teacher.npz <run.npz or the box's .jsonl> ...
    bash tools/npu/trust_check.sh
    # the box: old against new on a clip in /data/local/tmp/npu/boost
    TUNDU_NPU_BOOST=clahe TUNDU_NPU_TRUST=0 TUNDU_NPU_SWAP=0 TUNDU_NPU_COAST=0 ./pose_npu_test <det> <lm> old out --clip <clip.mjpg>
    ./pose_npu_test <det> <lm> new out --clip <clip.mjpg> [--idle]

### The engine live in the app, and the camera picture's cost (2026-11-11)

**Live, first build** (the APK of 19:33, the user's room dark: the camera's own picture 9 of 255, the tuner up to level
5, mean ~97): nobody in view, the detector looked 3.7-3.8 times a second; someone who stood up in the dark was found
within ~10 s and followed with the full model at 30 poses a second for the next minute: 0 camera frames skipped, 0 lost,
0 dips, 0 ghosts (home screen 30.0 fps, NPU 62 % busy, SoC 62-67 °C). Then Vishu! was opened, and the numbers fell
apart: 20-22 camera frames a second reached the AI, 41-45 skipped every 5 s, full gave way to lite ("needs 31 ms").

**Why** (`tools/dev/box-profile.sh -t pool-5-thread-1`, Vishu! with nobody in view, SoC 83 °C, CPU capped at 1.4 GHz):
the camera thread was 76 % busy, 17 frames a second, and spent

| | Share of the camera thread |
|---|---|
| `FrameShare.nativeOffer`: the camera picture for the game | **76 %** (~34 ms a frame) |
| `DetectorEye::Shrink`: the detector's averaged input (new on 2026-11-10) | 18.5 % (~10 ms a call, 14 calls a second) |
| The detector itself (`rknn_run` waits for the NPU) | 2 % |

`nativeOffer` resampled every output pixel on its own (four taps in plain C), even when the game asked for the
camera's own size: a copy done the slowest way. Vishu! asks for 1024 px since its rework of 2026-11-09 (640 before:
~14-20 ms a frame, which Lunapark! and Pikturo's camera mode still paid). The picture is made on the camera thread
before the pose AI gets the frame, so its time is the AI's time.

**What is slow on this CPU** (`pose_npu_test --selftest` on the box: each routine's ARM code against plain C, byte for
byte, and its CPU time, measured with the thread's own clock because a game runs beside it; 1024 x 768 frames):

| | CPU a frame |
|---|---|
| The picture at the camera's own size: the old per-pixel resample | ~34 ms (in the app) |
| ... a copy (16 bytes a step) / in plain C | **2.2 ms** / 4.5-5.3 |
| Exactly half (512 x 384): the average of each 2 x 2, a row at a time / plain C | **1.7-2.0 ms** / 6.5-7.5 |
| 640 x 480: every pixel on its own from two rows at once, plain C / with NEON | 20.2 / 13.9 ms |
| 640 x 480: each camera row blended across once, then two rows blended, NEON / plain C | 8.5 / 24.2 ms |
| The detector's shrink (4 x 4 blocks to 224 x 168): four rows side by side, plain C / NEON | 13.3 / 10.5 ms |
| ... one camera row at a time into a line of sums, plain C / NEON | 5.7-6.1 / **2.4-2.9 ms** |

The arithmetic is not what costs: the A55 cores are in-order with a small cache (32 KB, the rows of a 1024 px picture
are exactly one page apart), and a loop that reads several camera rows side by side stalls on every cache line. Going
through the frame one row at a time halved the plain C; the NEON then does the rest. Rule for any per-pixel pass over a
camera frame here: one stream of memory, in order.

**Which size for the game** (`tools/dev/box-cpu-ab.sh`: PerfLab `video=<width>` per configuration, uncapped, the GPU
pinned at 300 MHz, the NPU loaded like one tracked person, each thread's CPU from /proc; SoC 83-84 °C, CPU 1.4 GHz):

| Lunapark! (asks for 640) | Game fps | Main thread | Camera thread | Unity main / render threads | The app | NPU a pose |
|---|---|---|---|---|---|---|
| 640 x 480, the two-step blend | 35.4 | 20.0 ms | 37 % of a core | 49 / 28 % | 172 % | 17.3 ms |
| 1024 x 768, a copy | 34.4 | 22.7 ms | 22 % | 57 / 32 % | 170 % | 17.9 ms |
| 512 x 384, exactly half | **39.6** | **16.1 ms** | 19 % | 48 / 27 % | **147 %** | 15.6 ms |

| Vishu! (asks for 1024) | Game fps | Main / render thread | Camera thread | The app |
|---|---|---|---|---|
| 1024 x 768, a copy | 15.2-15.5 | 33.6 / 25.1 ms | 21 % | 151 % |
| 640 x 480, the blend | 17.0 | 28.0 / 23.0 ms | 36 % | 164 % |
| 512 x 384 | 18.1 | 23.9 / 19.8 ms | 19 % | 140 % |
| No camera picture (the doll on its backdrop) | 28.1-28.3 | 18 / 18 ms | 14 % | 146 % |

A full-size picture where the game asked for less only moves the work: the camera thread saves 15 % of a core, Unity's
main and render threads pay 13 % (the game copies the frame twice and uploads it), and the game gets slower. Half size
is the cheapest by far, and the pose model's NPU time drops with it (less traffic on the memory bus). So the box hands
over **the camera's own size or exactly half of it, whichever is the smallest that is still at least three quarters of
the width the game asked for** (`SharedSize` in `frame_scale.h`): Vishu!'s 1024 stays the full picture, 640 becomes
512 x 384. The blend is gone from the code. On the PC nothing changes (the JPEG stream at the asked width).

**After** (the APK of 21:01, its `libtundupose.so` the repository's byte for byte):

| | Before | After |
|---|---|---|
| Vishu!, nobody in view: the camera thread | 76 % of a core, 17 frames a second | **16 %**, 33-34 |
| Vishu!, poses a second / camera frames the AI never saw | 20-22 / 41-45 every 5 s (someone followed) | 30-31 / 0 (the NPU loaded like one followed person: nobody stood in front for this one) |
| Lunapark!, uncapped at 300 MHz | 35.4 fps with the 640 px blend (the old resample: not measured) | 39.0 fps, main thread 15.9 ms |
| Home screen, someone sitting at the edge of the picture | not measured (the old engine ended the track at every low score) | 30 poses a second, 0 lost, 1-13 dips coasted through every 5 s, 30.0 fps |

Also fixed on the way: `CameraFeed.Release` destroyed the feed at once, so a scene that asked for it in the same frame
(one camera game loaded straight after another, as PerfLab's `scene=` does) got a feed that died at the end of the
frame: Vishu! with the doll and no picture, and a measurement that looked like a 28 fps Vishu!. The feed now goes on
its next frame if nobody asked again. New in the tools: `pose_npu_test --selftest`, `pose_npu_test --people 2 --clip`
(a recording in the two-player mode; the routine clip: the one person's pose in all 2250 frames, four doubles started
and ended at once), PerfLab `video=`, `tools/dev/box-cpu-ab.sh` + `box_cpu_summary.py`.

**Seen and left open** (each needs a recording or is another piece of work):

- **Vishu!'s frame rate on a hot box**: 22-24 fps with nobody in view and the governor free (main thread 24-27 ms,
  render 19-21), 15 fps uncapped at a pinned 300 MHz with someone followed, against 28 without the camera picture: its
  mirror (the picture's denoise and blur passes, the 3 MB frame copied twice and uploaded) is what costs, not the
  tracking. Half size would give +19 % there (18.1 fps) for a softer picture.
- **Someone half in view makes the detector flicker**: in Lunapark!'s hall, with a person sitting at the edge of the
  picture and moving, the engine found them on the detector's crop and lost them on the next frame's, 32-37 times
  every 5 s for half a minute (a detector look of 41-45 ms each time, the hall at 26.7-28.6 fps), before following
  them steadily. A new track gets no coasting (under 15 frames: it could be a ghost). An idea, not tried: when tracks
  keep ending young, look only four times a second as when nobody is there.
- **Who is the player**: the engine follows the first person the detector ranks highest and, since 2026-11-10, holds
  on to them through low scores. Before, a person the model was unsure of (sitting on the couch at the picture's edge)
  was dropped several times a second, and each time the detector chose again, which gave someone stepping in front of
  the TV their chance. Now such a person can stay "the player" in a one-player game. The other side of the same coin:
  a player who dips for a frame is no longer swapped for a bystander. Choosing the player on purpose (the one standing
  nearest the middle, say) needs recordings with two people in view: none exist.

(Followed up on 2026-11-12: Vishu!'s frame rate in the next section; the detector's back-off in "The detector's back-off
for someone half in view"; who is the player waits for recordings.)

### Vishu!'s frame on a hot box (2026-11-12)

The user: "do this task here" (the follow-up above). Everything below on the X98K Pro at 81-84 °C (the CPU capped at
1.4-1.6 GHz, the GPU at 200-400 MHz whatever was asked: the power allocator overrides a pinned clock that hot).

**Where the frame went** (`tools/dev/box-profile.sh`, the APK of 2026-11-11, Vishu! at 79 °C, 30 fps, GPU 700 MHz):

| Thread | What | Share |
|---|---|---|
| Unity's main thread | IMGUI (`DressGame.OnGUI`: the wardrobe's bubbles, icons, panels, the mirror's frame and its 28 bulbs, labels) | ~30 % |
| | the camera frame copied twice (`tundu_frame_copy` into a kept frame, then `LoadRawTextureData` into the texture) | 18 % |
| | URP's rendering | 17.5 % |
| | the outfit's mesh (`OutfitRenderer.Build`) | 6 % |
| Unity's render thread (Thread-5) | the Mali driver's work per draw call (IMGUI's ~160 draws a frame: one `DynamicVBO` chunk each) | ~50 % |
| | the picture's upload (`glTexSubImage2D`) | 9 % |

**Changes**, each behind a PerfLab switch to compare (`hud=imgui`, `feed=copy`, `mirrorcrop=0`; `mirrorprep=0` and
`hud=off` only to price a part):

1. **The HUD's pictures as one mesh** (`Games/Common/HudBatch.cs`, shader `Games/Common/Resources/TunduHudBatch.shader`):
   `DressGame.OnGUI` runs its HUD code twice on a repaint, the first pass collecting every picture and panel (the
   interface sheet, plus a small shapes texture with the rounded panel and the frame's ring) into one mesh drawn with
   `Graphics.DrawMeshNow` inside OnGUI, the second drawing only the words, the item thumbnails and the cursors with IMGUI
   on top. ~160 IMGUI draws a frame became ~26. Drawn inside OnGUI, the batch writes exactly what IMGUI writes: IMGUI
   samples an sRGB texture decoded and writes the result unconverted (measured on the box: re-encoding the sample to sRGB
   made the bubbles ~5 % brighter; without it the batch matches IMGUI within a level of 255, 228.5/168.2/205.9 against
   228.5/168.3/205.9).
2. **The camera picture uploaded by the render thread** (`frame_share.cpp`, `CameraFeed.ShowNative`): the plugin keeps
   the last frames (a ring; the game says how many, `tundu_frame_keep`), the game picks one (`tundu_frame_pick`, Vishu!'s
   picture waits for its poses) and asks Unity's render thread to upload it straight from the plugin's memory
   (`tundu_frame_request` + `GL.IssuePluginEvent`; `glTexSubImage2D`, Unity's GL state put back). The main thread copies
   nothing; the texture keeps no copy on the CPU (no game reads it there: Pikturo!'s photo and Vishu!'s render on the
   GPU). OpenGL ES only (the box's API); elsewhere the copy path as before. Lunapark! and Pikturo's camera mode get it too.
3. **The denoise only where the mirror shows the picture** (`MirrorLook.NewFrame(..., shown)`, Tundu/MirrorPrep's
   `_Region`): the quad drawn over the shown part (+3 % for the filtering) instead of the camera's whole picture; the rest
   keeps older frames, which the denoise tells from the new one as movement when the framing glides there.
4. **The room's blur and the player map in one small texture** (Tundu/MirrorPrep's Person pass writes the room's
   blurred colour into rgb, through the mirror's own mapping `_PictureST`, and the player into alpha): Tundu/Mirror reads
   two textures a pixel instead of three; and it skips the room's maths where the player covers it or nobody is there,
   and the vignette's roots in the middle of the screen (both give the same picture).
5. **The clothes' room light per vertex** (Tundu/Outfit): a value that is the same everywhere, read once per vertex
   instead of once per pixel.

Tried and **taken back** (no measurable change; they would only have risked the look): the player map at a sixteenth of
the screen instead of an eighth (the sharp area round the player came out a little tighter), the denoise's movement test
with one approximate root per colour instead of two exact ones. At these clocks the GPU's time is in reading the big
picture, not in arithmetic.

**Uncapped, the GPU "pinned" at 300 MHz (it ran at 200-275), the NPU loaded like one followed person, nobody in front:**

| Vishu! (`box-cpu-ab.sh`) | fps | Main / render thread | The app's CPU |
|---|---|---|---|
| Before (all the old paths) | 15.3-16.7 | 31.6-33.8 / 23.8-25.9 ms | 157-164 % of a core |
| + 1 the HUD batch | 16.0 | 27.7 / 11.6 ms | 134 % |
| + 2 the render-thread upload | 16.0 | 25.4 / 20.7 ms | 143 % |
| 1 + 2 | 16.4-16.5 | 21.1 / 8.9 ms | 121 % |
| 1-5 | 18.1-19.8 | 19.8-21.0 / 8.3-9.0 ms | 124-129 % |
| 1-5, the denoise over the whole picture | 18.7 | 21.1 / 8.8 ms | 129 % |
| 1-5 without the HUD (to price it) | 19.2 | 16.1 / 5.6 ms | 116 % |
| 1-5 without the mirror's passes (to price them) | 23.7 | 17.0 / 8.7 ms | 128 % |
| 1-5 with a 512 x 384 picture | 22.2 | 17.3 / 8.1 ms | 126 % |

**As it runs** (the governor free, the 30 fps cap, 82-84 °C; `box-cpu-ab.sh -g 0 -x ''`):

| Vishu! | fps (frames late) | Main / render thread | The app's CPU | GPU clock |
|---|---|---|---|---|
| Before, nobody followed | 22.7-23.8 (42-51 %) | 24-25 / 19-21 ms | 155-159 % | 200-350 MHz |
| After, nobody followed | 27.4-28.1 (11-16 %) | 15-16 / 6-7 ms | 122-128 % | 250-400 MHz |
| Before, someone followed | 22.6 (51 %) | 28.5 / 22.7 ms | 179 % | 225 |
| After, someone followed | 26.6-26.7 (21-23 %) | 19 / 8-9 ms | 135-152 % | 200-300 |

The GPU was ~100 % busy in every one of them: the hot box is GPU-bound in Vishu!, and the CPU's savings give the GPU
more of the power budget too (the allocator held it at 300-400 MHz more often). Right after the final install (83 °C,
the doll on the screen): Vishu! 29.2 fps (5 % late, main 15 ms, render 6 ms), Lunapark!'s hall 30.0.

**The user's call** (a look decision, made on purpose in the rework): Vishu!'s camera picture at half size (512 x 384,
PerfLab `video=512`, `CameraFeed.RequestWidth` in `DressGame.Start`) gives ~+3 fps on the hot box (22.2 against 18.1-19.8
uncapped) for a softer picture. Not changed.

### The detector's back-off for someone half in view (2026-11-12)

`pose_npu.cpp` (`YoungEnd`, `BackingOff`; `TUNDU_NPU_BACKOFF=0` turns it off): after 3 tracks that ended young (lost or
rejected within their first 15 runs) inside 2 s, the detector looks once every 250 ms while nobody is followed, as when
nobody is in view (the frames just before each look still go into its running average), until a track lives past 15
runs. For the flicker seen on 2026-11-11 (a person sitting at the picture's edge found and lost ~7 times a second, a
41-45 ms detector look each time). There is no recording of that; the routine clip cropped so the player is cut by the
picture's edge (`tools/motion/clip_crop.py`, on the box with `pose_npu_test --people 1 --clip`, lite):

| Clip | Back-off | A pose in | Tracks started / ended | Detector runs | Frames backing off |
|---|---|---|---|---|---|
| The routine, the whole picture | off / on | 2250 / 2250 | 1 / 0 | 1 / 1 | 0 / 0 |
| Cropped at x 560 (the player half in view) | off / on | 1892 / 1892 | 7 / 6 | 273 / 273 | 0 / 0 |
| Cropped at x 490 (mostly out) | off | 391 | 39 / 35 (+4 ghosts) | 1778 | 0 |
| | on | 352 | 32 / 31 (+1 ghost) | 1628 | 213 |

Nothing changes where the player is in view (the coasting keeps a half-visible standing player 84 % of the time); with
the player mostly out it engaged for 213 frames, 150 fewer detector runs and 39 fewer frames with a half-person's pose
(who can't play anyway). The cropped clips don't reproduce the live storm; its benefit there needs a recording.
`pose_npu_test --people 1 --clip` (new) prints the frame-by-frame account (`d` detector, `b` backing off).

### Two players after the tracking rework (2026-11-12)

The user, after a Versus match on the box: "the tracking for the 2 player mode in tennis was terrible; [since the tracking
rework] it's not working for 2 people at all, very slow, very unresponsive". The box's log of that match: both players
followed the whole time (people 2/2, 0 lost, 0 dips, ~15 poses a second each), but one player's swing detector had taken
14 moves for glitches and ignored them in ~70 s while 6 of that player's swings got through; the swings that did were
seen ~100 ms after they happened (one player on 2026-11-07: 82-86 ms).

**Two people without two people.** There is no two-person recording, so two windows of the routine clip side by side,
the right one 1125 frames (37.5 s) later, as two people (`tools/npu/two_clip.py`): *wide* (~500 px apart: their crops
don't overlap), *close* (~300 px apart, ~1.5-2 torso lengths: each crop holds part of the other person, as in a real
match), *dark and far* (the `dim` clip, two stops darker with the camera's grain, shrunk to 0.72). Each run on the box with
`pose_npu_test --people 2 --rate 30 --clip` (the app's budget: one landmark run a frame, the two take turns), every person
scored against the heavy-model teacher of the one-person clip on that person's own frames, and Tenis!'s swing detector
(`tools/motion/swing_replay.py`) run on each person's samples (`tools/npu/two_score.py`): strong swings found against the
same detector on the teacher at 30 samples a second, and how much later they're reported. "Old" = the engine with every
2026-11-10 part switched off (`TUNDU_NPU_BOOST=0 TUNDU_NPU_UPRIGHT=0 TUNDU_NPU_TRUST=0 TUNDU_NPU_SWAP=0 TUNDU_NPU_COAST=0
TUNDU_NPU_BACKOFF=0`: the engine of 2026-11-08).

| Run (lite) | Poses a second | Strong swings found (left / right) | Reported later than at 30/s |
|---|---|---|---|
| One person (the routine), new / old | 30 | 76 % / 80 % | +8 / -4 ms |
| Wide, new / old | 15 / 15 | 59, 58 % / 63, 62 % | +60 ms |
| Close, new | 15 / 15 | 54 / 54 % | +61 / +40 ms |
| Close, old | 15 / 15 | 74 / 54 % | +57 / +45 ms |
| Close, new without the trust (`TUNDU_NPU_TRUST=0`) | 15 / 15 | 72 / 62 % | +67 / +43 ms |

What the new engine changed for two players: (1) **tracking: better**. On the close clip one track a person instead of
seven tracks (the old dropped and re-found people), 2.0 s of gaps over 0.25 s against 7.3 s (gaps a two-player match pauses
for); in the dark and far clip the old engine had both people in only 131 frames of 2250 (mostly one person at 29 poses a
second), the new in 1160. (2) **swings: worse**, by the trust's visibility. Its parts one at a time on the close clip
(strong swings, left / right): the hold off 54 / 56 %, the rescue off 54 / 54 %, the visibility cap off 61 / 60 %, the
whole trust off 72 / 62 % (the cap and the quick low pass together): the positions are fine (the wrists as far from the
teacher as before, no lag), but the visibility the games gate on now drops in a blur, and Tenis!'s swing detector skipped
every sample under 0.6 and **dropped a swing under way at the first one** (that is the "A hand's jump ignored" of the log).
At 15 samples a second a swing has two or three fast samples, so one blurred sample lost it. (3) **15 poses a second**
(since 2026-11-05) costs a quarter to a third of the strong swings against one player at 30, and ~50 ms: each sample
of the median of three is twice as old.

**Fixes.** (a) `SwingDetector.MinVisibility` 0.6 → 0.45 and an unsure sample is skipped, also in the middle of a swing
(a swing that stays unsure for a quarter second starts over). Strong swings found, close clip: 54 → 67 / 54 → 60 %; wide
59 → 67 / 58 → 60 %; one person 76 → 80 %. No more glitches through: on the routine judged by the full model on the same
frames (`swing_replay.py agree`) 6 glitch swings against 7 before (lite), 3 against 4 (full); ignoring the visibility
altogether found a few more swings but let 9 glitches through. The simulator (`swing_replay.py sim`) gives the same as
before (its glitches are at visibility 0.8). A blurred sample (0.35) in the middle of a forehand, simulated: every camera
frame, old 1-28 of 40 found, now 40; at 15 a second both lose nearly all (too few samples left to tell the swing from a
glitch's jump). (b) **The focus** (`PoseNpu::SetFocus`, `AndroidMotionService.SetFocus`): while two share the landmark
runs, the player who has to move now gets two runs in three (~20 poses a second, the other ~10, which keeps them in view);
Tenis! gives it to the player the ball is coming to, or who serves (`TennisGame.UpdateFocus`). Close clip, the focused
person (left), with (a): 15/15 67 % +66 ms → focus 2/3 **76 % +33 ms** (focus 3/4 67 % +37 ms; both every frame, twice the
NPU, 70 % -6 ms). With a blurred sample in the middle of a forehand at two frames in three: old 4-35 of 40, now 32-40.
`pose_npu_test --focus-left S` gives the focus to whoever is on the picture's left half. (c) Tenis!'s two-player match
pauses only when a player has been unseen for 0.6 s (`PlayerSlots.Slot.SeenWithin`; it was `Present`, a quarter second:
at ~10 poses a second two dips of the pose score would stop the match).

Not measured: two real people (the composite's seam, the same person twice, no child). The focus works live (the stats
line shows `focus 1 67%` / `focus 2 67%` going back and forth with the ball). The test switches `TUNDU_NPU_HOLD=0`,
`TUNDU_NPU_CAP=0`, `TUNDU_NPU_RESCUE=0` (the trust's parts one at a time) stay in the engine for comparing.

### Two players on a hot box (2026-11-12/13)

The user: "the frame rate and processing of movements is very slow on a hot box; I need actual optimizations on 2
player mode, both graphical and processing". **The setup**: a live versus match on the box (81-84 °C), the laptop playing
two scripted people over Wi-Fi (`fake_sender.py --script tennis_duo --alternate --host 192.168.1.46`), the NPU loaded like
two followed people (PerfLab `people=2`, `npubench=1`, `npurate=30`); A/B of PerfLab settings while the match goes on
(no `scene=`: the scene stays), each thread's CPU from /proc, simpleperf on the main, render and NPU threads.

**Before** (as it runs: GPU 200-300 MHz at 84-100 % load, CPU 1.6 GHz): 24.8-29.6 fps, main thread 20-26 ms, render
thread 17-21 ms; the app 162 % of a core (main 48 %, render 40 %, the camera thread 14 %, CameraX's colour conversion
11 %, the NPU thread 11 %, the GPU driver's thread 12 %). The pose AI kept up (30 runs a second, 0 frames skipped).
**At a pinned 200 MHz, uncapped**, what each layer of the split screen costs (fps):

| Split screen | fps | | Split screen | fps |
|---|---|---|---|---|
| as built (0.75 of the screen) | 19.6 | | without the crowd | 20.9 (+7 %) |
| resolution 0.65 | 23.8 (+22 %) | | without the toys (`hide=skinned`) | 21.8 (+12 %) |
| resolution 0.6 | 26.5 (+36 %) | | without the near end's stands | 20.5 (+5 %) |
| 0.6 without the crowd | 29.9 (+52 %) | | sky, shadows, particles, grass detail, mirror | ±0-2 % |

The GPU pays per pixel and per vertex: at the lower resolution, leaving out the crowd (~670 spectators at 59 vertices,
in ~25 bobbing sections drawn in both halves) gave +13 %. The CPU threads: main 43 % in URP (two cameras), 13-16 % in IMGUI
(the scoreboard alone 9 %: twelve labels a frame, each drawn twice for its shadow, its strings formatted every frame),
motion processing (packets, body tracking) under 3 %; render thread 54 % in the Mali driver (per draw call; its CPU-side
vertex transform for tiling, `gles_vertexp_bb_neon_transform_and_produce_clip_bits`, 14 % alone), 26 % in Unity's draw
code. **Changes**: (1) the split screen at 0.6 of the screen's resolution as the screen's own resolution
(`BoxGraphics.SetSplit` resizes the surface once as a versus match starts and ends; `Settings.SplitScale` 0.6 for
Tenis!, Volejboll! and Gara!: Gara!'s 0.7 was URP's render scale with a copy on top); (2) **the light crowd** in the split
screen (`TennisStadium.LiteCrowd`): the same people as one mesh (the near end's another: 2 renderers instead of ~25
sections), heads a 6-vertex diamond (`TennisArt.Diamond`, 30 vertices a spectator against 59), jumping as one on a cheer; the near end (only the split screen shows it)
has only the light one; (3) the scoreboard's texts without drop shadows on its dark panel, names and games formatted
only when they change; (4) `useGUILayout = false` in Tenis!, Volejboll!, Gara! and the skeleton mirror (none uses
GUILayout, and Unity ran their HUD code a second time every frame for its layout pass). **After**, at a pinned 200 MHz
uncapped: 27.6-27.8 fps (+41 %): the resolution gave most of it (the old build at 0.6: 26.5), the light crowd and the
HUD changes +4-5 %; without any crowd 29.8 (+8 % over the light crowd; over the full one it was +13 %). The 25.4 that
PerfLab's `res=0.75` gave on the new build is not the old resolution: in a split screen `res=` sets the screen's
resolution and the split's own 0.6 stays through URP's render scale (0.8 there) and a copy; compare resolutions with
`scale=1` too. **As it runs** (84 °C, the GPU at 200 MHz and now 47-92 % busy, the CPU throttled further to 1.4 GHz):
28.7-29.2 fps, 5-9 % of frames late, main thread 19-21 ms, render 16-17 ms; native memory 277 → 230 MB. The CPU threads are now the limit. One 10 s window in six
dropped to 22-25 fps in both runs (main 28-34 ms; not the AI, not a match event: unexplained, the baseline had them too).

**Processing**: 40 landmark runs a second (the hitter every frame with a 0.75 focus: its swings ~30 ms sooner) cost
1.3-2.5 fps (27.3-27.9 against 28.7-29.2; main and render threads +3.5 ms each from the memory bus), so the budget stays
30 with the focus at 2/3 (the hitter ~20 poses a second). The NPU thread costs ~4 ms of CPU a pose (reading the outputs,
the trust decoding ~1 ms, the UDP send); a pose leaves ~75 ms after its frame was captured, ~50 ms of that before the AI
starts (the camera's exposure, its JPEG decode in the HAL, CameraX's colour conversion). What's left there: CameraX's
YUV→RGBA conversion (11-12 % of a core; the crops and the detector would read YUV), NEON for the heatmap's peak search.

**Volejboll! and Gara!** got the same split resolution and `useGUILayout = false` (their crowds are Sporte!'s `Fans`,
unchanged). Their split screens, measured on the waiting screen with PerfLab `split=1` (the menu's IMGUI drawn too, the
remembered choices untouched; the NPU loaded like two people; the GPU pinned at 200 MHz, uncapped; 84 °C): Volejboll!
16.6 fps at the old 0.75 against 23.5-23.7 at 0.6 (+42 %); Gara! 16.5-16.7 at its old 0.7 (URP's render scale on a 0.75
screen, a copy on top) against 19.6-21.4 at 0.6 (+25 %; one 10 s window of the new one fell to 16.7). Both stay well
under Tenis!'s split screen (27.7 in a live match): their own costs (Sporte!'s crowds, the sea, the track and its props,
the menu) haven't been looked at layer by layer.

### Volejboll!'s and Gara!'s split screens, and what a chibi costs (2026-11-13)

The user: "yes, do them" (Volejboll!'s and Gara!'s own split-screen costs). **Measured** at the hot box's clocks (the GPU
pinned at 200 MHz, the CPU capped at 1.416 GHz with `scaling_max_freq`, so a cooler box measures like a hot one; the NPU
loaded like two people; uncapped), first layer by layer on the waiting screen (PerfLab `split=1`), then in live versus
matches (PerfLab's new `mode=versus`: the laptop's two scripted people over Wi-Fi, the menu's remembered choice untouched).
*Volejboll!* is GPU-bound: hiding the four chibis (low detail, each drawn twice) +36 %, the sky +10 %, the sand +10 %, the
waiting screen's menu +14 %, the sea +5 %, the net +2 %, the crowd and the particles 0; 0.5 of the screen instead of 0.6
+29 %. *What a chibi costs* (2026-11-06's open question; switches in a live match): lit per vertex +10 %, back faces
culled +10 %, not skinned at all (the pose frozen into a plain mesh) +3 %, hidden +33 %: **its pixels, not its skinning**:
two-sided, every hidden back face is shaded too, and Tundu/Athlete's per-pixel light (the colour table, the sky light,
two `pow`s) is dear at 200 MHz. *Gara!* is CPU-bound in its split screen (main thread 32-36 ms, render 30-35 ms at 1.416
GHz; hiding GPU work changed nothing): the main thread is a quarter IMGUI (the race HUD 13 %, the calls 8 %; laying out
text 11 %: an outlined `SportHud.Text` is the text drawn 5-10 times), 30 % URP's two cameras, 8 % culling, 9 % the game;
the render thread is the Mali driver's work per draw call (draws 35 %, static batches 26 %, per-draw state 23 %,
constant buffers 12 %). Its karts are ~19,700 triangles each (the four ribbed wheels 11,400), its ~60 stars and gift boxes
each their own renderer. Also seen, not changed: URP renders each half of a split screen into an offscreen texture and
copies it to the screen (a camera whose viewport isn't the whole screen: `UniversalRenderer.RequiresIntermediateColorTexture`,
`!cameraData.isDefaultViewport`), a fixed cost of the split screen in every game.

**Changes**: (1) *the chibis* (`Chibi`): the low-detail model (split screens, far teams, the computer's drivers) lit per
vertex (Tundu/Athlete's new `_VERTEX_LIT`: the light worked out at the vertices, the pixels interpolate it); the full
model's closed pieces (head, hair, shoes, most hats, ears, tails: found at runtime from their edges, `IsClosed`) in a
second submesh with back faces culled, like Sporte!'s athletes (the low model keeps one submesh: a second draw call
for ~2 % didn't pay in Gara!); the property block only when it changes (every frame before). (2) *The split screen's
lighter paths* (`BoxGraphics.ApplyLite`, with the split screen): the global keyword `TUNDU_LITE` (Tundu/Sky's clouds from
two octaves of noise, four texture reads a sky pixel instead of eight; Tundu/Water without its drifting foam streaks, two
cosines a pixel instead of nine sines and cosines) and `SportHud.LiteOutlines` (an outlined text as one drop shadow).
(3) *Gara!*: a low-detail kart (`art/kart/kart.py`: the same parts at 40 % of the segments, no tread ribs: 3,044
triangles instead of 19,690; previews `kart_low_*.png`) drawn per camera by size (a `LODGroup`: the full kart from 22 % of
the screen's height, so a player's own kart stays full in their half) and as one combined mesh (`KartModels.LowWhole`: one
draw call instead of six); the stars and gift boxes drawn instanced (`Graphics.RenderMeshInstanced`: one draw call each
per camera); the pterodactyls (skinned) left out of the split screen; the race HUD's texts made only when they change
(the speed ~7 times a second); the karts' property blocks only on a flash. (4) Volejboll!'s scoreboard like Tenis!'s
(texts cached, no shadows on its dark panel). (5) PerfLab: `mode=versus|together|solo` (Tenis!, Volejboll!, Gara!: a
mode for this visit, never saved), `track=0-2` (Gara!), `lite=0|1`, `chibilit=pixel|vertex`, `chibicull=off|all`,
`kart=full|low`, `ptero=1`.

**After**, live versus matches at the hot box's clocks, the new defaults against the old behaviour switched back on
(`chibilit=pixel`, `chibicull=off`, `lite=0`; Gara! also `kart=full`), alternating in one session:

| Split screen (GPU 200 MHz, CPU 1.416 GHz, uncapped) | New | Old switched back on | |
|---|---|---|---|
| Volejboll! | 29.6-29.9 fps | 25.5-25.7 | +16 % |
| Gara! (the coast) | 22.4-25.3 (main 28-32 ms, render 27-31) | 16.5-19.8 (main 31-39, render 31-38) | +29 % |
| Tenis! | 28.3-28.5 | 27.3-27.6 | +3 % |

(Gara!'s instanced stars and its HUD's cached texts can't be switched back: the real gain is a little more.) **As it
runs** on the hot box (83-84 °C: the GPU held at 200 MHz by the heat, the CPU 1.4-1.6 GHz; the 30 fps cap; live versus
matches): Tenis! 29.1-29.5 fps (3-5 % of frames late, main 20 ms, render 16.5), Volejboll! 29.7 (2 % late, main 20-21,
render 17-18), Gara! 24.7-25.7 (33-38 % late, main 27-29, render 26-28): Gara!'s CPU threads are still the limit there.
What's left for it: its HUD as one batch (Vishu!'s `HudBatch`), fewer renderers at range (the road's and ground's chunks,
the scenery's batches), URP's offscreen copy per half.

### Gara!'s split screen, round 2: the HUD in one draw call, the sky, the drivers (2026-11-13 afternoon)

The user: "yes, do Gara!" (its CPU: the HUD batch, fewer draws at range). Live versus races on the coast (PerfLab
`mode=versus track=0`, `fake_sender --script kart_duo --alternate`), uncapped, the NPU loaded like two followed people.
**Two clock conditions, so each change is measured against the limit it acts on**: *CPU-bound* = the CPU capped at the
hot box's 1.416 GHz with the GPU pinned at 600 MHz (it then sits at 15-70 % load), *GPU-bound* = the CPU free (the heat
holds it at ~1.6 GHz) with the GPU pinned at 200 MHz (100 % load). One race at both hot clocks first: CPU 1.416 + GPU 200
23.9 fps, CPU free + GPU 200 ~24.3, CPU 1.416 + GPU 600 25.4: **both limits were within a few percent of each other.**
The windows are noisy (± 2 fps between 22 s windows of the same race), so every A/B alternates configurations in one race.

**Profiles** (`box-profile.sh`, now picking the busiest thread of a name: an idle Java "Thread-5" beside the render
thread had given empty profiles): the main thread ~56 % on the CPU (IMGUI 16 %: labels 10 %, pictures 3 %; URP's two
cameras 34 %: culling 10 %, the render graph 16 %; the game 10 %; `PlayerPrefs.GetFloat` 2 %: `GameAudio`'s volumes read
from Android's preferences every frame), the render thread ~61 %. **The render thread's 57 % "in
`gles_vertexp_bb_neon_transform_and_produce_clip_bits`" is a symbol mix-up**: `libGLES_mali.so` is stripped, so all its
internal code takes the nearest exported name (the profile even shows it calling itself four deep). Its callers are the
real costs: draw calls (static batch ranges, plain draws), framebuffer switches (each split half's offscreen target and
its copy to the screen: `DrawNullGeometry`), constant buffer uploads, IMGUI's dynamic vertex buffers.

**Changes kept** (CPU-bound A/Bs; PerfLab switches bring the old way back):

| Change | Before | After | |
|---|---|---|---|
| The race HUD's pictures as one mesh (`KartHudAtlas`: icons, figures, the wheel, the minimap, dot, block, disc, star in one 1024 x 512 texture; `HudBatch` + a turned quad), `hudbatch=0` | 23.7 fps | 25.5 | render −4 ms, main −2 |
| Its words too (`HudBatch.Text`: the glyphs of IMGUI's font from `Font.RequestCharactersInTexture`, rasterised at the size they take on the screen, laid out like an IMGUI label; `SportHud.Text(HudBatch, ...)` makes the same outline copies), `hudtext=0` | 26.7 | 28.9 | render −2.6 ms, main −2.5 |
| The whole race HUD in one draw call against IMGUI | 25.9 | 28.9 | +12 % |
| The world in fewer meshes (the scenery merged per 120 m square instead of static-batched, the ground's chunks 64 cells, the road's 150 m), `merge=0` (with a scene load) | 27.1 | 27.6 | renderers in a camera's view 85-173 → 18-64; within the noise |

IMGUI draws were ~0.16 ms each of the render thread (each its own state, a dynamic vertex buffer chunk): the HUD was ~25
pictures and ~18 labels a frame in the split screen. The batched words look like IMGUI's (positions, weight, outlines
compared in screenshots); the ★ is drawn as a picture (the box's font may lack the glyph). Also: `GameAudio`'s volumes
read once (2 % of the main thread), URP's volume blending off for cameras without post-processing
(`BoxGraphics.StillVolumes`; the first scene waits a frame for URP's volume manager; `volumes=1`; within the noise),
PerfLab `census=1` (every 10 s, per camera, the renderers in its view by kind: the scenery was the most, 11-97 props).

**Tried and taken back**: the full kart as one skinned mesh (one draw call instead of six; its parts as bones) 28.2 fps
against the six parts' 29.9: skinning costs what the draws saved. The sky's clouds baked into a texture of their plane
(one read a pixel instead of four noise reads; a separate shader: **a skybox draws every pass of its shader**, so a bake
pass in Tundu/Sky covered the frame) and the clouds off (`clouds=0`): neither measurable.

**GPU-bound** (what the GPU at 200 MHz pays, hiding one thing at a time): the drivers +11-20 % (`hide=driver`), karts
and drivers +18 %, **the sky +13-15 % (`sky=0`, three sessions)**, resolution 0.5 instead of 0.6 +9 %, the ground, road
and scenery 3-4 % each, particles and the backdrop nothing measurable. **A driver's cost is fixed, not its triangles**:
`chibi=full` (2.5x the triangles, per-pixel light) changed nothing; held still as a plain mesh (no skinning) +6.5 %.
URP clears a base camera's colour whatever its clear flags (render graph: `clearOnFirstUse` for base cameras), so the
sky's cost is its own pixels: normalize, two `pow`s, the sun's direction normalised per pixel, over ~40 % of each view.
**Tried on the GPU side and taken back**: a cheaper split-screen sky (half precision, `sqrt` for the gradient, the sun's
glow by multiplications, one look at the noise for the clouds) and the computer drivers held still (a plain mesh baked
again once a second, one driver a frame): together 24.3 fps against 24.0 GPU-bound; held still alone +9 % on the GPU but
its re-bakes cost the CPU about as much; and in that last session (84-85 °C) even `sky=0` gained nothing. Neither paid
reliably for the look they cost. The GPU-bound numbers drift with the heat by more than these changes.

**As it runs** at the end (the box at 84-85 °C after hours of measuring: the CPU throttled to 1.1 GHz, the GPU 200-400
MHz; the 30 fps cap; live versus matches): Tenis! 27.9-28.1 fps, Volejboll! 27.7-28.0, Gara! 23.9-25.1 (main 30-31 ms,
render 27-29). Two hours earlier, at 83-84 °C with the CPU at 1.1-1.4 GHz: Tenis! 28.5-29.1, Volejboll! 28.0-29.1, Gara!
24.7-26.6. Gara!'s split screen now meets both limits on a box that hot: its CPU threads at 1.1 GHz and its GPU at 200
MHz. **What's left, as choices for the user** (each costs some look): the split screen at 0.5 of the screen instead of
0.6 (+9 % on the GPU), the computer drivers held still (+6-9 % on the GPU, their poses frozen), the sky without its maths
in the split screen (+13-15 % in three of four sessions), and on the CPU URP's two cameras (a third of the main thread).
A cooler box (a family's, not one measured for hours) runs faster: the same match ran 28-30 fps CPU-bound at 1.416 GHz.
**The final build** (the experiments above taken out), as it runs after a two-hour rest (81.7-83.9 °C, CPU 1.1-1.4 GHz,
GPU 200-400 MHz, the NPU loaded like two followed people): Tenis! 29.1-29.4 fps, Volejboll! 28.9-29.6, **Gara! 25.0-26.8**
(main 26-28 ms, render 23-26). A race just before, without that NPU load, ran 28.9-29.4 with the main thread at 18 ms:
following two people is a large part of a hot box's CPU (four cores and one power budget shared; not A/B'd in one race).

### Lunapark! on the box (2026-11-09 evening)

The first build of the eleventh game (sprites over the camera picture: one sheet, three batches, the words IMGUI;
`BoxGraphics` "Arcade": screen scale 0.8, no post, 30 fps, the pose model auto = full). APK 92.8 MB. The box was quiet
(no `dex2oat`) but warm from the day (78-82 °C).

| Measurement | fps | notes |
|---|---|---|
| the hall (the fair at night, the six booths, the shelf; nobody playing), the GPU at 400 MHz, 31 % busy | **30.0**, 0 % of frames late | main 10.5 ms, render 4.3, GPU 14.2 ms; 2-3 GCs every 10 s (a 9 MB heap: no hitch, worst frame 34 ms) |
| Godit! and Fruta! with the scripted player over Wi-Fi (`fake_sender --script arcade_whack --host <box>`) and someone in the room, 81 °C, the GPU capped at 200 MHz, 100 % busy | **29.7-30.0**, 0-2.4 % late | main 14-16 ms, render 7-8, GPU 19-23 ms; the NPU 51-65 % (the full model following the person at 30 poses/s) |
| Muri! and Kap! played by the person in the room alone (the box's own camera; every round was left after 5-53 s with the remote's Back, the next booth started with → and OK) | **30.0**, 0-0.3 % late | main 12.6-13.9 ms, render 4.7-5.8, GPU 14.5-19 ms; the AI 30 poses/s, 0 camera frames skipped while followed, frames ~48 ms old |
| the hall again with the person still in view, then nobody | 29.8-30.0, 0-1 % late | with the person: main 12-16 ms, GPU 21-25 ms; nobody: main 10.5, GPU 14 ms (the GPU's clock moves with the heat) |

No errors or warnings in the log. The remote's first key after `am start` was swallowed again (the hall's OK did
nothing; the notes on MuMu said the same): the round started when the person raised their hands.

### Zana! on the box (2026-11-16)

The first build of the thirteenth game: a 3D chase over generated lands (the ground in 48 m chunks of 480 triangles,
~220 instanced props of ~1,600 triangles each in view range, the creatures one mesh each, the tall fairy at 27k
triangles with four see-through wings; `BoxGraphics` "Fairy": screen scale 0.8, no post, 30 fps, the lite pose model).
Measured with `tools/dev/box-cpu-ab.sh`, the game playing itself (PerfLab `scene=Fairy`, `autoplay=1`, `land=0`: the
garden's first land and on), the NPU loaded like one followed person (`npubench`). New PerfLab switches read by the game:
`props=0`, `creatures=0`, `bubbles=0`, `glows=0`, `hud=0` (each layer off), `realmpix=1` (the lands lit per pixel, as
built), `propcull=0` (every prop drawn, as built).

| Measurement | fps | notes |
|---|---|---|
| the first build as it runs on a cool box (61 °C) | 29.4, 1.4 % of frames late | the GPU at 700 MHz but 71-90 % busy: at a hot box's 200-300 MHz ~15 fps |
| its layers, uncapped, the GPU pinned at 300 MHz (76-82 °C; the thermal cap still pulled it to 225-275 MHz in the later rows) | as built 15.2; 0.65 of the screen 20.4; no sky 19.4; no props 21.1; no ground and water 19.4; no HUD 18.0; no fairy and wings 16.3; no creatures 15.8; no bubbles, glows or particles 15.0-15.4 | each row at another moment of the flight: the scene's own load moves ±10 % (the HUD's gain didn't repeat later, below) |
| the GPU pinned at 200 MHz (82-83 °C), uncapped | as built 10.4 (three times); the lands lit per vertex 16.3-20.3; the props culled to the camera's view alone 10.2-10.6 | per-pixel Realm on the ground (its texture is white) and the props (flat palette colours) was most of the frame |
| the same, lit per vertex and culled, with each candidate | two-octave clouds (`lite=1`) 19.6-22.4; 0.7 of the screen 21.5-22.7; no HUD 16.1-19.5 | against 16.3-20.3 without |
| final (per vertex, culled, 0.75 of the screen, the two-octave clouds) against as built (`realmpix=1`, `propcull=0`, `lite=0`, `res=0.8`), pinned 200 MHz | **19.7 / 23.1 against 10.6** | about twice as fast |
| final as it runs on the hot box (83-84 °C, the 30 fps cap, the clocks free) | **28.6-28.9** (8-9 % late); in steady flight 29.5-29.7, 1-4 % late, the GPU at 200-300 MHz | as built 27.2 (20 % late) with the GPU pushed to 300-700 MHz: hotter for less |

What changed: Tundu/Realm's `_VERTEX_LIT` (`FairyWorld` turns it on in runtime copies of the ground's and the props'
materials; the water stays per pixel for its ripples): the toon light, the rim and the gloom at the vertices, the pixel
only fogs that colour. The props are culled to the camera's view by a bounding sphere each (instanced drawing culls
nothing itself; it made no measurable difference here, but can only take work away). `BoxGraphics.Settings.LiteSky`:
the two-octave clouds all the time without the split screen's thinner outlines. The 10-second window with the first wave
dipped to 25-26 fps with worst frames of 67-83 ms and the main thread +3-5 ms: each creature species' mesh was put
together on first sight, the pixie's figure when her bubble came; now `GloomViews.Warm` builds them one a frame during
the land's card.

### Zana!'s garden on the box (2026-11-16)

The same day Zana! became the garden defence (CLAUDE.md's Zana! bullet): one fixed stage instead of the flight's
streaming lands, the fairy tree and ~100 instanced props, up to ~45 creatures on the ground and ~18 in the air on a busy
wave, the spells' glows; the same `BoxGraphics` "Fairy" settings (screen scale 0.75, no post, 30 fps, the lite model,
the light sky) and the per-vertex light from the flight's measurement above. Measured with `tools/dev/box-cpu-ab.sh`,
the game playing itself on Hard (PerfLab `scene=Fairy`, `autoplay=1`, `diff=hard`; `wave=<n>` new: the run starts at
that wave, with the transformations a player would have had), the NPU loaded like one followed person, one
configuration a run (`scene=` doesn't reload an open scene).

| Measurement | fps | notes |
|---|---|---|
| as it runs, the hot box (81-82 °C), the clocks free: wave 1 | **30.0**, 0.1 % of frames late | main 11.3 ms, render 6.6 ms, the GPU at 275 MHz |
| the same, wave 12 (~40 on the ground, ~14 in the air, level 3) | **29.6**, 2.7 % late (10 s windows 28.2-30.0, worst frames 50-67 ms) | main 12.9 ms, render 8.1 ms, the GPU at 275 MHz |
| uncapped, the GPU pinned at 200 MHz: wave 12 / wave 15 (a boss) | 22.4 / 23.3 | the flight after its optimisation: 19.7-23.1 at the same clock |

No errors in the log. The first 10 s of a busy wave are the slowest (28.2 fps): the wave's creatures arriving at once.

**The wand build** (the same evening: a raised hand points a wand, shapes on the screen to follow, the fairy flying
after the wand; the game playing itself at a child's pace, zapping and following shapes). Wave 12 on Hard was crowded
now (the slower self-playing wand fell behind: the tree fell within ~11 s, so the run kept restarting at wave 12 with
creatures everywhere), and the creatures were the cost:

| Measurement | fps | notes |
|---|---|---|
| as it runs, a cool box (63-67 °C): wave 1 / wave 12 on Hard, creatures lit per pixel | 30.0 / 29.4 (4.1 % late; one 10 s window 27.8) | main 11.8 / 16.0 ms, the GPU at 650-700 MHz |
| wave 12 uncapped, the GPU pinned at 200 MHz: as built / no HUD / no glows / no creatures drawn | 17.7 / 18.6 / 17.3 / **30.9** | ~50 creatures lit per pixel were ~40 % of the frame (`Tundu/Gloom`: the colour table, three normalizations, the sky light, the rim's and the shine's powers per pixel) |
| the same with `Tundu/Gloom`'s `_VERTEX_LIT` (new; `GloomViews` turns it on in a runtime copy; PerfLab `gloompix=1` = per pixel) | **23.3 / 23.4** against 17.3 per pixel | +35 %; the gesture build at the same clock (fewer creatures alive): 22.4 |
| as it runs, per vertex, the box at 78 °C: wave 12 on Hard | **29.6** (2.9 % late) | the GPU at 475 MHz; no errors; the creatures keep their shading and glowing eyes (box screenshot) |

**The spells-only build** (the same night: the wand only traces shapes, the creatures come in groups, the fairy
wanders and casts by herself; the bolts and darts are a few more glows). The box hot (83 °C), as it runs (the GPU at
225 MHz, the CPU at 1.4 GHz), the NPU loaded like one followed person, the game playing itself on Hard:

| Measurement | fps | notes |
|---|---|---|
| wave 1 | **29.8**, 1.5 % late | main 11.7 ms, render 6.9 ms |
| wave 12 (busy; the self-playing wand a good player now: the waves don't overrun it) | **28.9**, 6.9 % late | main 14.2 ms, render 8.8 ms |
| the final build, Easy, waves 1-2 | **30.0**, 0 % late | main 10 ms, render 5.8 ms; no errors |

### Zana!'s new look on the box (2026-11-17)

The look's overhaul (CLAUDE.md's Zana! bullet): art/fairy's garden kit lit per vertex by the rewritten `Tundu/Realm`
(no textures on the props; the near meadow one detail read a pixel), a sky dome of vertex colours drawn after the garden
at the far plane (the skybox and its noise clouds gone), ~700 instanced things, soft dot shadows, pollen, five additive
sun shafts, the HUD's new frames in the same batch. Measured with `tools/dev/box-gpu-ab.sh -g 200`: the GPU pinned at
200 MHz (the hot box's clock), uncapped (fps=60), the NPU loaded like one followed person, the game playing itself on
Hard (PerfLab `scene=Fairy`, `autoplay=1`, `diff=hard`; `wave=12` after a `scene=Menu`). The new PerfLab switches:
`sky=0`, `ground=0`, `props=0` (all instanced things), `grass=0`, `flowers=0`, `far=0` (far trees, clouds, mountains,
castle), `tree=0`, `rays=0`, `pollen=0`, `shadows=0`, `realm=lite` (every garden material without its rim and full
wind).

The first build ran **20 fps** against the old look's 30 (both wave 1 and 12). What it cost and what was done, round by
round (wave 1, fps; each switch with the rest on):

| Round | all on | without | what changed next |
|---|---|---|---|
| 1 | 20.1 | grass 22.0, sun shafts 20.4, pollen + shadows 20.0 | tiny triangles out (flowers' middles painted into the petals, crowns' buried faces left out: ~25 % fewer triangles) |
| 2 | 22.0 | ground 28.0, props 30.6, flowers 25.2, far 24.0, the light's rim and wind 23.5, creatures + glows 23.2, tree 22.0, sky 21.7, HUD 22.1 | the meadow's texture compressed (ETC2), no anisotropic filtering; fewer flowers, a draw distance for small things; a thinner far forest |
| 3 | 24.2 | ground 29.9, props 44.7, flowers 26.6, far 25.7, grass 26.0, lite 25.4 | the props' haze finished at the vertices (their pixel = one colour), the meadow's texture holding what its pixel needs (~7 operations, was ~20), the lite light (no rim, one wave of wind) for grass, flowers, stones and far things, grass blades in 2 rows |
| 4 | 27.4 | ground 30.7, props 45.7, far 29.2, flowers 29.1, lite 27.3 | the meadow split: the near part with its detail, the hills beyond lit at the vertices only; flowers only within 15 m; fewer far trees, lighter clouds and mountains |

What the box's GPU taught (Mali-G52 2EE MC2 at 200 MHz: ~16 operations a clock): every vertex runs the whole light, so
~250k vertices of grass, flowers and far trees cost more than their pixels; and the meadow covers half the screen, so
each operation in its pixel cost ~0.2 ms. The fairy tree (26k triangles, close and big) and the sky cost nothing
measurable.

**The old look against the new, back to back** (`box_fair`: the same box at 81 °C, the GPU pinned at 200 MHz, uncapped):

| Build | wave 1 | wave 12 (busy) | frames over 34 ms, wave 1 |
|---|---|---|---|
| the old look (2026-11-16) | 30.4 fps | 29.5 | 31-42 % |
| **the new look** | **29.9** | **28.7** | **8-13 %** |

**As it runs** (`box-cpu-ab.sh -g 0` with PerfLab `people=1`, `npubench=1`: the game's own 30 fps cap, the clocks free,
the box at 81-82 °C, the CPU throttled to 1.6 GHz): wave 1 **29.9-30.0 fps** (0-0.7 % of frames late, main 12.5-13.3 ms, render
8.0-8.7 ms), wave 12 **29.2-29.9** (0.3-1.4 % late; one 233 ms hitch as the wave started). The old look there on
2026-11-16 (83 °C): 29.8 (1.5 % late) and 28.9 (6.9 % late). No errors in the log. The box runs this build
(`builds/android/Tundu-2026-11-17-zana-look.apk`; the one before is `Tundu-2026-11-16-before-zana-look.apk`).

**The spirit tree** (the same afternoon: the user found the treehouse "very ugly, and not mystic enough"; the new
`FairyTree`, its own material whose glow fades with the tree's health, spirit lights and an aura in `FairyGame.DrawGlows`).
Back to back with the treehouse build, the GPU pinned at 200 MHz, uncapped, 80-82 °C:

| Build | wave 1 | wave 12 | notes |
|---|---|---|---|
| the treehouse | 29.5-29.7 fps | 28.7-28.8 | two sessions |
| the spirit tree, first (38k triangles) | 28.6 | 27.4 | without the tree 29.9; `spirits=0` + `aura=0` 28.5 (the glows cost nothing) |
| trimmed (29k: cushions over the fairy in full detail, the rest lighter; slimmer wisteria and trunk) | 29.2 | 28.0 | without the tree 30.0 |
| **final (28k: a quarter fewer wisteria clusters)** | **29.5** | **28.1** | |

As it runs (`box-cpu-ab.sh -g 0` with PerfLab `people=1`, `npubench=1`, the box at 81-82 °C): wave 1 **29.9-30.0 fps**
(0-0.3 % late), wave 12 **30.0** (0-0.3 % late); no errors. The box runs `builds/android/Tundu-2026-11-17-spirit-tree.apk`.

### Zana!'s wand over the body: the full pose model (2026-11-17)

The user: hand tracking "not working correctly on the fairies game, especially when the hand goes over the body. maybe
the model needs fixing". `tools/motion/wand_replay.py` replays the wand's input (FairyGame.ReadBody + Wand: the hand tip
0.3 of the forearm past the wrist, round the shoulders' middle in torso lengths, onto a 1920 x 1080 HUD, the cursor's One
Euro) on the box engine's own output for the routine clip (`D:/rknn/bench/box/routine-20261101.<model>-new.jsonl`)
against the heavy teacher through the same filter, on frames with the hand raised and the teacher sure of the wrist:

| run | over the body: median px | > 150 px | > 300 px | beside: median | > 150 px | > 300 px |
|---|---|---|---|---|---|---|
| lite, the game's input | 40 | 3.3 % | 1.1 % | 41 | 3.2 % | 1.0 % |
| **full, the game's input** | **31** | **0.6 %** | **0.2 %** | 37 | 2.5 % | 0.8 % |
| full, median of three | 31 | 0.6 % | 0.0 % | 37 | 3.0 % | 0.9 % |
| full, visibility 0.75 | 31 | 1.2 % | 0.6 % | 38 | 6.0 % | 3.4 % |
| full, a 150 px jump gate | 35 | 11.8 % | 8.2 % | 42 | 13.0 % | 8.0 % |

(over the body = the teacher's wrist between the shoulders, from the hips to 0.7 torso lengths above the shoulders.) The
lever is the model, not the game's filters. Zana! now asks for the full model (`BoxGraphics` "Fairy" `model: "auto"`).
On the box (`box-cpu-ab.sh -g 0 -t 40 -x 'people=1\npubench=1\n'`, the game drawing by itself on Hard, wave 1, 80-82 °C,
alternating; PerfLab `model=full` / `model=lite`: `model=auto` after lite stays on lite):

| run | fps | late | main ms | render ms | NPU part ms | poses/s | skipped |
|---|---|---|---|---|---|---|---|
| full, a | 29.3 | 4.9 % | 16.9 | 11.5 | 24.0 | 30.1 | 0 |
| lite, a | 29.8 | 1.4 % | 15.2 | 9.8 | 12.7 | 30.3 | 0 |
| full, b | 28.6 | 9.1 % | 17.3 | 11.6 | 21.3 | 30.4 | 0 |
| lite, b | 29.1 | 5.7 % | 15.1 | 9.8 | 13.2 | 30.1 | 0 |

About half a frame a second and ~2 ms of the main thread (the memory bus the NPU and the GPU share) for 5x fewer big
wand errors over the body. The box runs this build (2026-11-17 ~17:00; the spells drawn in the air).

### Zana!'s wand jumps (2026-11-17 evening)

The user, after the full model: the wand "jumps a lot during drawing the spell"; "an actual improvement", with The Link
as a reference. **A recording of the user drawing spells** with the box camera (`box_clip.py spells-20261117`: 120 s at
1024x768@30, light level 4, the user ~3-4 m away in a dark jacket, the right hand drawing circles, spirals, a heart, the
bolt, waves, rainbows in front of the body for the first ~36 s, then the room empty), the box's engine on it
(`pose_npu_test --clip`, full and lite: `D:/rknn/bench/spells/box/outspells`), the heavy teacher
(`tracklab.py ... --variant teacher`) and lab variants (`D:/rknn/bench/spells`). Scored with the rewritten
**`tools/motion/wand_replay.py`**: the game's wand pipeline (the hand point, the smooth shoulders and torso unit, Wand's
zone, raise rule and visibility gate, HandInterpolator per game frame at 30 fps, samples 70 ms after capture) against the
teacher's same point smoothed both ways in time; a **jump** is a game frame whose cursor step differs from the
reference's step by more than 40 / 80 / 150 HUD px (of 1920 x 1080), counted per minute of wand time.

**Where the jumps came from**: the pose model's wrist and elbow, not Zana!'s filters. Frames with a wrist more than 12 %
of a torso off the teacher come mostly one at a time (on the routine clip with full: 41 of 53 such runs are one frame);
they sit where the hand is in front of the body, where the forearm points at the TV (the wand's own pose) and in motion
blur, and the model is sure of them (heatmap trust 1.0 at most of them; its visibility tells them better, AUC 0.94). The
wand's point added to it: the wrist pushed 0.3 of the forearm further out (`Body.HandTip`, like every hand cursor) adds a
third of the elbow's error, and the elbow is wrong exactly when the hand draws in front of the body. The biggest jump left
on the spell clip (130 px) is samples skipped for low visibility (0.27-0.44) during a fast stroke, right ones (6-20 px
from the teacher in the picture), then caught up; a lower gate (0.3) helps the spell clip and hurts the routine clip.

**What doesn't help** (each against the same metric): **The Link's hand model** (MediaPipe Holistic's `hand_recrop` +
`hand_landmark_full` from its folder, run Holistic's way in the lab on the box's frames: at 3-4 m in evening light a hand
is a 30-40 px blur and its hand flag stayed ~0 on every hand checked, also slow ones held still; The Link works because
its user sits a metre from a 720p 60 fps camera in a lit room); **the mirrored crop** averaged in (twice the NPU: full
23 → 22 jumps a minute, lite 75 → 70, both still far above full's single run) or on every other frame (worse: the two
views' small offsets zigzag); median of three; a prediction gate that holds a far sample for the next one (its catch-ups
are jumps too); a visibility-weighted Kalman filter (overshoots turns); the hand's average point (wrist, pinky, index,
thumb: noisier than the wrist); a stronger One Euro (only lag).

**What went in** (`Wand`, `FairyGame.ReadBody`): the wand follows **the wrist itself**; the zone and the raise heights moved
by what the tip had added while drawing (median 0.07 torso lengths up, 0.06 inward: `Inward` 0.36, `Bottom` -0.82, `Top`
0.83, `RaiseOn` 0.33, `RaiseOff` 0.15), so the wand lands where it did; the cursor's One Euro estimates speed at 4 Hz (was
2: it opens sooner on a real move, less lag); the cursor runs a camera frame behind the newest sample instead of half
(between real samples, not gliding past the newest one; +17 ms, about what the quicker filter saves):

| jumps a minute > 40 / 80 / 150 px; error median, p90 | spell clip, full (Zana!) | spell clip, lite | routine, full | routine, lite |
|---|---|---|---|---|
| before | 58 / 3.3 / 0; 50, 91 px | 149 / 17 / 1.7; 50, 102 | 280 / 52 / 12.6; 67, 118 | 407 / 107 / 23.6; 76, 140 |
| the wrist (filter and margin as before) | 22 / 1.7 / 0; 45, 81 | 75 / 5 / 0; 44, 90 | 147 / 23 / 7.6; 61, 101 | 250 / 56 / 15.2; 65, 116 |
| **now** (+ filter 4 Hz, a frame behind) | **15 / 1.7 / 0; 43, 76** | 50 / 5 / 0; 42, 84 | **117 / 24 / 3.0; 55, 93** | 203 / 47 / 3.0; 60, 109 |
| the teacher through the same pipeline | 0 / 0 / 0; 26, 42 | | 21 / 4.4 / 0; 33, 49 | |

(The routine clip is punches and waves, far quicker than drawing.) What's left is the model's own noise: the heavy
teacher's wand makes none of these jumps while drawing, and heavy is ~5x lite's NPU time. The other hand cursors: next
section.

    python tools/motion/wand_replay.py D:/rknn/bench/spells/spells-20261117.heavy.teacher.npz \
        D:/rknn/bench/spells/box/outspells/spells-20261117.full.jsonl --variants all

### The other hand cursors (2026-11-17 night)

The user, after the wand: "yes, do them" (the same change for the other games' hand cursors). Who has one: **Pikturo!'s
brush** (`PaintGame.HandHud`, raw samples, both modes), **Lunapark!'s hands** (`ArcadeStage`, raw: the hammer, the blade,
every booth's hits), **`BodyHands`** (the shared cursor: no game reads its camera cursor today, Pikturo! uses it for the
mouse) and **Vishu!** (its own tip on `OutfitTracker`'s fitted pose). The home screen has none (steps and the remote).
All pushed the wrist 0.3 of the forearm on (`Body.HandTip`), toward the hand's middle.

**`tools/motion/hand_replay.py`** mirrors each pipeline on the two clips with the box's engine (full and lite) against
the heavy teacher, scored like `wand_replay.py` (a point against the same point on the teacher; "hand" = the median
distance from the teacher's tip, i.e. from the hand's middle; "floor" = the teacher's own point through the same
pipeline): Pikturo!'s paper (480 px a torso length, the fit and the rope; frames with the hand on the paper), Lunapark!'s
Godit! framing (~304 px a torso length), BodyHands' zone (600 x 415 px a torso length). Vishu!: `DressBench`'s new `Cursor`
test (the real OutfitTracker; the reference = the recording smoothed both ways). The points: the tip, the wrist, the
wrist pushed along the smooth or the low-lag track's forearm, the tip without the elbow's visibility fade or with it
smoothed, a median of three on the forearm, and **the arm made longer**: the raw wrist carried on out from the smooth
shoulder (`s + (w - s)(1 + k)`, k 0.1 / 0.15 / 0.2: no elbow at all, the wrist's noise times 1 + k).

| jumps a minute > 40 px, full model (lite) | Pikturo! spells | Pikturo! routine | Lunapark! spells | Lunapark! routine | BodyHands spells | BodyHands routine |
|---|---|---|---|---|---|---|
| the tip (before) | 14.7 (27.7) | 70.3 (123) | 10.4 (37.0) | 46.6 (68.2) | 14.4 (36.4) | 77.1 (118) |
| the wrist | 4.9 (13.0) | 43.2 (79) | 5.9 (28.1) | 28.2 (42.9) | 8.3 (18.9) | 55.3 (79) |
| along the smooth forearm | 11.4 (21.2) | 56.4 (105) | 8.1 (30.3) | 37.6 (55.9) | 9.8 (28.0) | 70.9 (98) |
| **the arm 0.1 longer (now)** | **9.8 (17.9)** | **55.6 (96)** | **6.7 (30.3)** | **33.1 (51.9)** | **9.1 (25.8)** | **66.8 (97)** |
| the teacher's tip, same pipeline | 1.6 | 21.5 | 1.5 | 8.6 | 2.2 | 20.0 |

(0.6-2.5 minutes of a hand per cell: the spell cells count a handful of jumps each.) The visibility fade and the median
changed nothing measurable; the smooth forearm still lets the elbow's longer errors in (it opens up while the arm
moves). **The wrist jumps least but reaches less and sits off the hand**: from the shoulder (teacher, torso lengths, p99
/ max) the tip reaches 1.27-1.33 / 1.44-1.45, the wrist 1.11-1.16 / 1.25-1.32, the arm 0.1 longer 1.22-1.28 / 1.38-1.45;
Pikturo!'s far pots and brushes are 1.1-1.2 from the nearer shoulder, so the wrist alone would put them at the limit of
reach. Lunapark!'s hands (median px from the hand's middle, routine / spells): the tip 41 / 39, the arm 0.1 longer 42 /
39, the wrist 59 / 41: the hammer stays in the hand. In the painting pose (hands raised in front) the longer arm sits ~4 %
of a torso further from the hand's middle than the tip did (~15 px on the paper; camera mode ~11 px).

**Vishu!** (jumps a minute > 10 / 20 % of a torso, routine full; lite): the tip 19.7 / 6.4 (23.3 / 6.1), the arm 0.1
longer 18.0 / 6.4 (18.4 / 3.8), the wrist 15.9 / 5.8 (14.2 / 3.4); on the spell clip none for any. The fitted pose already
takes the elbow's glitches out, and the longer arm would put the cursor 5-7 % of a torso off the hand: **kept as it was.**

**Lunapark!'s hand filter** works in HUD pixels (`HandInterpolator(1, 4, 2)` on px: its beta acts on px a second, so it
hardly filters; the hands follow the raw samples, interpolated). In torso lengths the same filter took routine 33 → 25
jumps a minute (the longer arm) and spells 6.7 → 8.1, with ~5 px more lag at p90: **not changed** (the booths' speeds,
a slice at 2.2 torso lengths a second, were tuned on these hands).

**What went in**: `Body.HandPoint(track, shoulder, wrist, 0.1)` (`Body.HandTip` removed) in `BodyHands` (`armExtension`
0.1, was `handExtension` 0.3 along the forearm), `ArcadeStage` and `PaintGame.HandHud`; Zana! keeps the wrist (its zone
moved instead; drawing doesn't need the far reach). `fake_sender.py` aims through it (`ARM_LONGER`: `zone_hand`,
`easel_hand`, `arcade_path`; Vishu!'s `dress_hand` keeps 1.15), `paint_sim.py` mirrors it (the scripted painting: its two
strokes as before). The PlayMode tests play the same through it (Godit! 6 moles, Fruta! 28 sliced, Muri! 6 of 6 perfect,
Pikturo!'s 2 strokes); on the box (APK of 2026-11-17 ~22:28) Pikturo! and Lunapark! load with no errors.

    python tools/motion/hand_replay.py D:/rknn/bench/routine-20261101.heavy.teacher.npz \
        D:/rknn/bench/box/routine-20261101.full-new.jsonl --points tip,wrist,stip,arm0.1

### Zana!'s waiting screen (2026-11-17 night)

The box's log while the user had Zana! open: 7 minutes at 21.9-25.5 fps (main 25-29 ms, present wait 13-16 ms, render
21-26 ms), 84 °C, the GPU at 400 MHz 100 % busy, the CPU at 1.416 GHz, someone in view (full model, 30 poses/s), and no
phase change or stroke in the log: the waiting screen. Measured with `box-cpu-ab.sh -g 0 -t 40 -x 'people=1\nnpubench=1\n'`
(as it runs, the NPU loaded like one followed person, 83-84 °C, GPU 200-250 MHz, CPU 1.4-1.6 GHz):

| | fps | late | main ms | render ms |
|---|---|---|---|---|
| waiting screen | 27.6 / 25.9 | 16 / 29 % | 19.3 / 21.9 | 15.0 / 17.2 |
| waiting screen, no HUD (`hud=0`) | 29.4 | 3.5 % | 12.9 | 9.2 |
| a wave (`autoplay=1`, Hard, wave 1) | 28.6 / 27.1 | 9 / 19 % | 15.7 / 18.0 | 10.5 / 12.0 |
| a wave, no HUD | 28.9 | 6.7 % | 13.9 | 9.6 |

The play HUD (in `HudBatch` since the look's overhaul) costs ~2 ms; the waiting screen's 6-9 ms of each thread was its
**menu** (five rows of options, element chips, dress and wings swatches: ~40 IMGUI draws a frame, many of them rounded
boxes) and **the gate's prompt** (`PlayerGate.DrawWaiting`, ~6 labels), IMGUI beside the batched rest. Now `FairyMenu`
draws through a painter (`FairyMenu.IPaint`: the HUD's batch, i.e. Zana!'s sheet's white, panel and capsule and glyphs,
or `FairyMenu.Imgui` as before: PerfLab `menubatch=0`), the two-player cards too, and the prompt comes from
`PlayerGate.Waiting` (its lines, colour and chevrons; `DrawWaiting` draws the same with IMGUI) and goes in as batched lines
(IMGUI only without the camera service and for the chevrons). Same conditions, alternating:

| | fps | late | main ms | render ms |
|---|---|---|---|---|
| **batched** | **28.9 / 28.0** | 8 / 14 % | 17.3 / 18.9 | 9.6 / 11.0 |
| IMGUI (before) | 26.3 / 26.1 | 26 / 26 % | 21.8 / 22.5 | 16.2 / 16.8 |
| no HUD | 29.2 | 4.6 % | 12.9 | 8.9 |

The final build (with the words' fixes below), a cooler box (77 → 82 °C, CPU 1.6-1.8 GHz): batched 28.9 / 28.8 fps (main
17.9 / 18.2 ms, render 10.5 / 10.5), IMGUI 28.0 / 27.4 (21.1 / 21.4, 16.3 / 16.8): the same thread savings, less fps to
win while the CPU still has its clock.

**`HudBatch`'s words now match IMGUI's** (box screenshots, batched against `menubatch=0`): IMGUI writes a label's
colour as linear (unlike its pictures), the batch wrote it as given, so coloured words came out paler (ZJARR's (255,
188, 139) as (255, 223, 195)); and IMGUI lays a line out at the HUD's size before GUI.matrix scales it, its synthesised
bold a little wider than `GetCharacterInfo`'s advances at the screen's size, so the batch's lines were ~10 % narrower
(the glyphs the same height). Now the glyphs' colour is converted and their advances are spread to IMGUI's own width
(`GUIStyle.CalcSize`, remembered per line): widths within 1 px, colours within a level. This also gives Gara!'s and
Zana!'s other batched words IMGUI's look back. Left: the "✦" before an unlock price now draws (IMGUI showed an empty
box); the hint's arrows (↑ ↓ ← →, a fallback font's glyphs) come out smaller than IMGUI's. Gara!'s waiting menu is IMGUI
too (five rows and four move cards): likely the same cost there, not measured.

### Vrapo!'s console polish on the box (2026-11-18)

The rebuilt Vrapo! (CLAUDE.md, "Vrapo!'s console polish") measured with `tools/dev/box-gpu-ab.sh -g 300` (the GPU pinned
at 300 MHz, uncapped, the NPU loaded like one followed person; PerfLab `autoplay=1`: an endless run nobody plays, nothing
stops it), the box at 68-79 °C. The old Vrapo! ran 24.6 fps uncapped at the hot box's 200-270 MHz ("The other games, the
same day" above) and 29.5-29.9 as played.

**The first build: 20.1 fps** (49.8 ms; main 14.3 ms, present wait 35.5: GPU-bound). Each part switched off in turn
(PerfLab, one session):

| off | fps | the part's cost |
|---|---|---|
| nothing | 20.1 | |
| the trees beside the path (`mid=0`) | 25.5 | 10.6 ms |
| the floor (`ground=0`) | 25.9-26.1 | ~11 ms (its pixels: the sky dome took its place) |
| the far band: clumps, cliffs, backdrop, clouds (`far=0`) | 23.3 | 6.9 ms |
| big leaves and bushes (`edge=0`) | 23.1 | 6.7 ms |
| the verge's low plants (`verge=0`) | 22.4 | 5.2 ms |
| all the scenery on the tiles (`props=0`) | 39.1 | 24.2 ms |
| the sky (`sky=0`) | 20.4 | 0.7 ms |
| the soft shadows, the particles (`shadows=0`, `fx=0`) | 20.1, 20.2 | ~0 |

So the scenery's vertices (every one runs the whole light: Tundu/Valley lights at the vertices like Zana!'s garden) and
the near floor's pixels. What was done, measured build after build:

| build | fps | what changed |
|---|---|---|
| 1 | 20.1 | |
| 2 | 23.6 | the kit's vertices welded on import (RunnerArtImport: corners that share everything are one vertex; the counts now equal Blender's), the trees' hand-made far versions from 42 m (`lod=0` → 21.8: 3.5 ms), the floor split: its detail only near the path, the banks and far tiles lit at the vertices (`_GROUND _LITE`) |
| 3 | 27.2 | the plants' far versions (built again at `kit.DETAIL` 0.5: under half the vertices) from 24 m (verge) and 30 m (edge, banks) (`lod=trees` → 25.0: 3.2 ms), the far clumps one a side a tile (two overlapped three times over), far versions and far clumps on the `_LITE` light, instancing with `assumeuniformscaling nolightprobe nolodfade` (no inverse matrix fetched a vertex), the near floor's values passed down 5 → 3 (the grass and the earth lit at the vertices) and its detail only to 6.5 m aside and 40 m ahead (the near floor's detail: 5.4 → 2.9 ms, `detail=0` → 29.5 fps) |
| 4 | 27.1 | the near floor's detail fade worked out from the pixel's depth and uv (a float2 passed down instead of a float4), the floor drawn after the scenery standing on it (queue 2060: its pixels under the plants are rejected first), the sky dome last (queue 2450: it was drawn first, under everything), each kind's instances front to back |

Build 4's changes gained nothing measurable (27.2 → 27.1). Not measured yet: the title screen (a config `scene=Runner` after one with `scene=Menu`: two `scene=` lines in one file keep only the last) and the game as it runs (`box-cpu-ab.sh -g 0 -x 'people=1\nnpubench=1'`).

Lessons: on this GPU what the vertex passes down to the pixel is a cost of its own (the near floor's pixel with five
values passed down cost twice what it did with three); a dome or a floor in the Geometry queue is drawn in whatever order
the distance sort gives (the sky dome, centred on the camera, was first); measure the plants' far versions in a sheet
next to the near ones (`RUNNER_SHEETS=valley_lod_plants,valley_lod_bushes`) before measuring their gain.

### Superboks! on the box (2026-11-19)

Superboks! (game 6, the superhero boxing that replaced Fluturo!) measured with `tools/dev/box-cpu-ab.sh` and PerfLab
`scene=Boxing`, `autoplay=1` (the hero fights by itself: the right defence after each tell, punches while the opponent
is open, the super; nothing saved) and `stage=0-5` (that stage's champion, again and again; it switches without
reloading the scene). Default mode: uncapped (`fps=60`), the GPU pinned at 300 MHz, the NPU loaded like one followed
person (`npubench`, the scene's full model); the box at 77-83 °C, so the heat held the GPU below the pinned clock in
the later windows (the GPU column).

**The first numbers were wrong.** The first round (16-22 fps, the main thread 35-50 ms) ran while Android compiled in
the background after the install; its package manager hung on it and the system watchdog restarted Android at 09:48
box time (`system_server_watchdog`, "Blocked in handler on PackageManager"; `dex2oat32` at 41 % of a core,
`system_server` 74 %). On the quiet box the same stages ran 23-28 fps with the main thread ~13 ms. Check `ps -A | grep
-c dex2oat` and the load average again after every install.

What each layer costs, the clouds stage (the champion Mbreti), the first build:

| config | fps | main ms | render ms | GPU MHz |
|---|---|---|---|---|
| base | 28.6 | 13.2 | 9.5 | 300 |
| `sky=0` | 29.5 | 14.2 | 10.2 | 300 |
| `hide=glow` (the stage's glows) | 26.8 | 12.4 | 9.0 | 300 |
| `hide=hero` (the see-through hero) | 28.2 | 12.2 | 8.6 | 300 |
| `hide=mbreti` (the opponent) | 33.5 | 12.6 | 8.6 | 300 |
| `hide=stage` (the ring and the scenery) | 32.1 | 12.0 | 8.2 | 300 |
| `disable=BoxingGame` (the HUD and the game) | 30.5 | 8.2 | 5.2 | 300 |
| `res=0.6` | 34.2 | 13.0 | 9.2 | 300 |

The opponent is big on the screen and was lit per pixel: `chibilit=vertex` gave +15 % in the gym (22.6 -> 25.9 at 300
MHz) and +12 % on the clouds (27.2 -> 30.5 at 267 MHz). The see-through hero costs nothing measurable (it's the
low-detail chibi in Tundu/AthleteGhost). The stages' geometry is small: 2.3k vertices (gym), 7.1k (roof), 32.7k
(mountain), 32.8k (clouds, after its far balls went to 12 segments), 8.7k (moon), 2.4k (stars), logged as
`[Boxing] Stage N: ...`.

Changes: the boxers' full chibi lit per vertex (`Chibi.VertexLit`, set by `BoxerView`; PerfLab `chibilit=pixel` lights
them per pixel again), the screen at 0.75 of its resolution (`BoxGraphics` "Boxing", was 0.8), balls over 3 m more than
25 m out at 12 segments (were 22), the map's HUD in one `HudBatch` draw.

Per stage, uncapped, before and after (the GPU clock each window ran at):

| stage | before fps (GPU MHz) | after fps (GPU MHz) |
|---|---|---|
| gym | 22.6 (300) | 26.7 (267) |
| roof | 26.0 (300) | 31.5 (233) |
| mountain | 23.5 (300) | 29.6 (267) |
| clouds | 27.2 (267) | 34.1 (200) |
| moon | 26.7 (267) | 31.9 (200) |
| stars | 27.7 (200) | 33.4 (233) |

The map (the waiting screen: the gym, both boxers, a card, four rows, the climb's 18 dots) was IMGUI-bound: ~150 draws,
main 22.5 ms and render 20.5 ms. Batched (its title and words with a ★ stay IMGUI): render 9.5 ms, main 19.7 ms, 20.7
-> 23.3 fps at the same 200 MHz.

As it runs (the 30 fps cap, clocks free, the NPU loaded like one person; a hot box at 82-83 °C, the CPU at 1.67-1.8
GHz):

| screen | fps | frames late | main ms | render ms | GPU MHz |
|---|---|---|---|---|---|
| map | 27.7 | 13.7 % | 18.9 | 8.6 | 267 |
| gym | 28.6 | 9.0 % | 13.5 | 9.5 | 300 |
| mountain | 28.5 | 10.9 % | 13.1 | 9.2 | 267 |
| stars | 28.9 | 7.3 % | 13.4 | 9.6 | 533 |

The AI kept 30 poses a second with no camera frames skipped throughout. Levers left: the fight HUD's IMGUI (~5 ms of
the main thread and ~4 of the render thread with `disable=BoxingGame`), the gym's crowd (193 fans of Sporte!'s
`Tundu/Crowd`), the screen at 0.7.

### Superboks! in first person on the box (2026-11-19 afternoon)

After the user's "I want better perspective and performance" the fight became first person (Kinect Sports boxing's
see-through view): no hero chibi to draw during the fight, the opponent seen whole from half a metre behind the hero's
eyes (56 degrees), the hero's see-through gloves in the view (two small meshes, Tundu/AthleteGhost's two passes), the
fight's HUD in one `HudBatch` draw like the map's (bars, the clock, the super meter, the opponent's stamina, pop-ups).
Same method as above (`box-cpu-ab.sh`, `autoplay=1`, `stage=`; checked quiet first: no `dex2oat`, load under 3.5).

Uncapped, the GPU pinned at 300 MHz (the heat held it at 233-300), the NPU loaded like one followed person, 81-83 °C:

| stage | over the shoulder (morning) fps (GPU MHz) | first person fps (GPU MHz) | main / render ms |
|---|---|---|---|
| gym | 26.7 (267) | 32.6 (300) | 11.1 / 6.2 |
| roof | 31.5 (233) | 33.3 (267) | 11.3 / 6.0 |
| mountain | 29.6 (267) | 31.8 (233) | 11.4 / 6.3 |
| clouds | 34.1 (200) | 32.8 (233) | 11.8 / 6.6 |
| moon | 31.9 (200) | 38.8 (233) | 11.7 / 6.6 |
| stars | 33.4 (233) | 39.3 (233) | 11.4 / 6.3 |

As it runs (the 30 fps cap, clocks free, the NPU loaded like one person; 82-83 °C, the CPU at 1.6-1.7 GHz):

| screen | fps | frames late | main / render ms | GPU MHz |
|---|---|---|---|---|
| map | 29.4 | 3.9 % | 18.9 / 8.3 | 333 |
| gym | 28.8 | 7.7 % | 12.3 / 6.5 | 267 |
| mountain | 28.8 | 7.8 % | 11.6 / 5.9 | 300 |
| stars | 29.8 | 1.3 % | 12.6 / 6.8 | 267 |

The AI kept 30 poses a second with no camera frame skipped. The opponent's AI itself costs nothing measurable (a few
hundred operations a decision, a decision every 0.16-0.42 s). Screenshots from the box: the opponent whole in the
middle, the hero's translucent gloves at the bottom, its stamina under its health bar. Levers left: the gym's crowd (193
fans), the full pose model's memory traffic (the same game with lite: not measured).

### Superboks!'s overhaul on the box (2026-11-19 evening)

The overhaul: Wii Sports Boxing's view from behind and above with the see-through hero, four arenas built as code
(`art/boxing/arena.py`, lit at the vertices by Tundu/Arena), the referee, the HUD from `tools/boxing/art.py`'s sheet in one
`HudBatch` draw, the boxers' portraits. Same method as above (`box-cpu-ab.sh`, PerfLab `scene=Boxing`, `autoplay=1`,
`stage=0-3`; the box checked quiet after each install: no `dex2oat`, load under 3.5).

**The gym's cones of light were most of its frame.** The first build, uncapped at a pinned 300 MHz with the NPU loaded
like one followed person: the map 19.4 fps, the gym's fight 17.1, the roof 46.2, the mountain 48.3, the stars 46.7. In the
gym, hiding the rig's eight cones of light (`hide=beams`) gave 42.6 fps; the crowd (`hide=fans`) 18.3, the set
(`hide=gym_set`) 17.7, the ring 17.0: the cones cost ~35 ms. Each was two rings (at the lamp and at the canvas) faded per
pixel (the edge fade with a `pow`, the near fade: two normalizes and a length), drawn on both sides and added over the
middle of the screen: up to sixteen layers where they cross over the ring. Now (`Tundu/ArenaGlow`): the cones in six
rings, both fades at the vertices (the pixel only adds the colour), the near side only (`Cull Back`: a camera inside a
cone sees none of it, so no wash either), 0.95 m at the canvas instead of 1.15, the strength 0.6 (one side instead of
two); the halos round bulbs and orbs two discs back to back.

Uncapped, the GPU pinned at 300 MHz, the NPU loaded like one followed person (the box warming from 66 to 81 °C):

| screen | first build fps | now fps | main / render ms | first person (afternoon) fps |
|---|---|---|---|---|
| map | 19.4 | 34.2 | 17.2 / 8.3 | |
| gym | 17.1 | 36.6 | 12.5 / 7.8 | 32.6 |
| roof | 46.2 | 44.1 | 12.9 / 8.1 | 33.3 |
| mountain | 48.3 | 46.2 | 12.7 / 7.8 | 31.8 |
| stars | 46.7 | 46.6 | 12.7 / 7.8 | 39.3 |

At 82 °C the heat held the GPU at 200-267 MHz despite the pin: the gym 35.8-36.7 fps, without the cones 39.8 (they cost
~3 ms now), without the crowd 37.8 (~1.5 ms).

As it runs (the 30 fps cap, clocks free, the NPU loaded like one person; 81-82 °C, the CPU at 1.8 GHz):

| screen | fps | frames late | main / render ms | GPU MHz |
|---|---|---|---|---|
| map | 28.9 | 7.5 % | 17.1 / 7.9 | 333 |
| gym | 29.2 | 5.7 % | 12.3 / 7.0 | 500 |
| roof | 30.0 | 0.2 % | 12.2 / 6.9 | 333 |
| mountain | 29.9 | 0.4 % | 12.4 / 6.9 | 233 |
| stars | 30.0 | 0.3 % | 12.3 / 6.8 | 200 |

The AI kept 30 poses a second with no camera frame skipped; no errors. The map's main thread (~17 ms: the skeleton mirror
and the gate's prompt in IMGUI) is what it was in the first-person build (18.9 ms). Box screenshots in `unity/captures`:
`box_boxing_gym_fight.png` (a counter: "WHAM!"), `box_boxing_gym_fight2.png` (the knockdown's side shot),
`box_boxing_map.png`, `box_boxing_intro.png` (the opponent close up over the see-through hero's shoulder),
`box_boxing_intro2.png` (the bell). Levers left: the gym's cones (~3 ms) and crowd (~1.5 ms), the map's IMGUI.

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
