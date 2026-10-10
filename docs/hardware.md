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
| A | | | | | | |
| B | | | | | | |

**Gate to pick a box:** pose ≥ 25 fps **and** game ≥ 30 fps at the same time, latency < 150 ms,
camera survives reboot and replug.

## Recognition benchmarks (fill in as devices are tested)

| Device | Backend | Model | Camera fps | AI fps | AI time | Capture→game latency | Notes |
|---|---|---|---|---|---|---|---|
| Laptop (Ryzen 7 7840HS, CPU) | py-mediapipe | full | 30 (20 in a dim room) | 30 | 15 ms | ~17 ms to send | 2026-10-05 |
| MuMu Player (emulator, x86 + ARM translation) | android-mediapipe GPU | lite (auto) | 30 | 30 | 12–14 ms | 14 ms (full model, earlier run) | 2026-10-07. Works end to end: body tracked, Fruta playable. First GPU frame takes ~15 s to set up |
| MuMu Player (same) | android-mediapipe GPU | full (auto kept it) | 30 (AE range [30, 30]) | 29.9 | 9.7 ms | | 2026-10-08, after the motion polish: frame-rate floor, reused frame buffer. ALLM: not supported (emulator) |
| RK3566 box | android-mediapipe | | | | | | |
| RK3576 box | android-mediapipe / rknn | | | | | | |

Read the numbers from **Settings → Kamera** (Recognition Lab) on the device.
