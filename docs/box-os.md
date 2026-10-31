# The box's "operating system": hiding Android behind MoveIt

Goal: the family turns on the TV and sees **only MoveIt**: your boot screen, your home screen with the games, and a
small Settings page. Never the Android home screen, app store or system menus.

We don't write an operating system. The box keeps Android underneath (drivers, Wi-Fi, HDMI, USB camera, updates), and
we hide it in layers, from "works today" to "factory firmware":

| Layer | What the user sees | Who does it | When |
|---|---|---|---|
| **1. MoveIt is the home screen** | Box boots into MoveIt; remote Home/Back always land in MoveIt | The app itself (done) | Now |
| **2. Provisioning script** | No Android launcher, no app store, no screensaver; box named MoveIt; our boot animation if the box allows | `tools/provision/provision.ps1`, ~3 min per box | Pilot, 10–30 boxes |
| **3. ODM custom firmware** | MoveIt boot logo from the first second; no Android anywhere; no vendor junk | The Shenzhen box maker, from our files | Batch 1, 100–300 boxes |
| 4. Own firmware (only if needed) | Same as 3 | Us, building Android from Rockchip's SDK | Only if no ODM will do 3 |

## Layer 1: MoveIt is the home screen (done)

- `Assets/Plugins/Android/AndroidManifest.xml` declares the app as a **HOME** launcher (`singleTask`, so pressing Home
  never opens a second copy). Once it's the default, Android starts it at boot and the remote's Home button returns to it.
- The app runs full screen with no Android status or navigation bars, and no Unity splash.
- **Home screen** (`Assets/Shell/Scripts/MainMenu.cs`): brand bar with name, tagline, Wi-Fi and camera status, and a
  clock; three game tiles chosen by stepping or ← →; a **Settings** button (↓).
- **Settings** (remote only; families need nothing more):

  | Item | What it does |
  |---|---|
  | Wi-Fi | Shows connected/not connected. OK opens Android's Wi-Fi screen (the one Android screen we still use); Back returns to MoveIt |
  | Volume | ← → in 10% steps, set directly without Android's volume bar |
  | Language | Shqip / English, remembered |
  | Camera | Status and fps. OK opens the camera test (skeleton view); Back returns |
  | Version | App version (automatic updates come later) |
  | Device | Model and Android version, for support calls |
  | *Android settings* | **Hidden.** Press OK on "Device" 7 times. For technical support only |

- Remote input lives in one file, `Assets/Games/Common/RemoteInput.cs`: D-pad = arrows, OK = Enter / D-pad center /
  gamepad A, Back = Esc. If a box's remote sends something else, fix it there.
- Android calls (volume, opening Wi-Fi settings) are in `Assets/Games/Common/AndroidDevice.cs`, simulated in the editor.

**Build the APK:** Unity → *MoveIt → Box → Build Android APK* (the first run switches the project to Android, the second
builds). Output: `builds/android/MoveIt.apk`.

## Layer 2: provisioning each box (pilot)

```bash
powershell -ExecutionPolicy Bypass -File tools/provision/provision.ps1 -Inspect
```
`-Inspect` lists a new box model's launchers and all apps into `tools/provision/logs/`. Add vendor junk (OTA
updaters, IPTV apps, anything unknown) to `tools/provision/disable-packages.txt`.

```bash
powershell -ExecutionPolicy Bypass -File tools/provision/provision.ps1
```
For each box this installs the APK (with camera permission granted), makes MoveIt the default home, disables the stock
launcher and the listed apps (reversible: `-Undo`), turns off screensaver and sleep, names the box, then reboots
straight into MoveIt. Add `-BootAnimation` to also install our boot animation. That only works if the box's adb allows
`root` + `remount` (many cheap Rockchip boxes do). Use `-Device 192.168.x.x:5555` to do it over the network.

Branding files come from one source, `unity/MoveItGames/Assets/Resources/brand.json` (name, colors, package id):
```bash
motion-service-py/.venv/Scripts/python tools/branding/make_branding.py --size 1920x1080
```
This produces `tools/branding/out/bootanimation.zip`, `logo.bmp` (first boot screen) and `preview.png`, plus the
app icon and TV banner in `Assets/Branding/`.

What can still show Android at this layer: the very first logo before the boot animation (the vendor's logo, in
the bootloader), the Wi-Fi screen when opened from Settings, and Android's volume bar when using the remote's volume
keys. Layer 3 removes these.

## Layer 3: ODM firmware (Batch 1). What to ask the box maker for

Send the ODM `logo.bmp`, `bootanimation.zip`, the release APK and this list. Ask for written confirmation of each item
**before** paying for the batch.

1. **Boot logo**: our `logo.bmp` in the bootloader/resource partition (the first screen at power-on).
2. **Boot animation**: our `bootanimation.zip` in `/system/media` (or `/product/media`, `/oem/media`).
3. **MoveIt preinstalled as a privileged system app** (`/system/priv-app`) and the **only HOME app**. Stock launcher
   removed, not just hidden.
4. **Removed**: Google apps/Play Store, IPTV/streaming apps, vendor app store, **vendor OTA updater**, demo apps.
5. **Clean firmware**: no preinstalled malware (cheap boxes have shipped with BadBox). Ask for the firmware's package
   list and test the network traffic of a sample box before ordering.
6. **Device name** "MoveIt", time zone **Europe/Tirane**, default language our choice, no setup wizard.
7. **USB camera works through Android's camera API** (external camera HAL enabled), and survives reboot and replug,
   **with its fast modes**: the HAL's `FpsList` (`/vendor/etc/external_camera_config.xml`) must let our camera's 640x480
   at 60+ fps through. The X98K Pro's list bounds each size by the first entry at least as tall and drops faster rates,
   so with our camera (one rate per size) 640x480@120 and 1280x720@60 vanished and only 1024x768 and up at 30 fps
   remained (docs/hardware.md "USB camera on the X98K Pro"). Test our camera on their firmware.
8. **adb**: off in shipped units, or locked to our key. Keep a way for us to re-flash or update.
9. **Remote**: our logo printed on it; confirm which key codes OK/Back/Home/Volume send.
10. **Box shell print** and **retail packaging** with our logo; **CE** documents for the box and the camera.
11. A **factory-reset** path that comes back to MoveIt (because MoveIt is in the system image).
12. **TV game mode over HDMI (ALLM)**: Android 11+ with `Display.isMinimalPostProcessingSupported()` = true, so the
    TV switches to its low-latency game mode when our app runs (our manifest asks for it). TVs otherwise add
    50–150 ms of picture processing. Our app logs `TV game mode (ALLM) supported: …` at start.
13. **USB camera holds 30 fps and stays bright in a dim room**. Android's external camera HAL never offers a fixed range
    (only [f/2, f]: `[15, 30]` on the X98K Pro) and passes none of the camera's UVC controls to apps, so either the
    camera's power-on settings are right (from the camera supplier) or the firmware sets them when the camera appears:
    exposure auto priority off (a steady 30 fps), gain and gamma up for dim rooms (on our camera: gain 100, gamma 160,
    saturation 100 took Vishu!'s mirror from mean 31 to 107 of 255, docs/hardware.md "Low light"), or, better, lets
    our app open the camera's `/dev/video*` node (now `media:camera` 0660; ueventd + SELinux for our app only, not every
    app): our `CameraTuner` then adapts the controls to the room by itself, no root. The app logs `Camera fps ranges: …`,
    the range it chose, and `Camera tuning …` / `Camera tuning off: …`.
14. **Signed with the ODM's own release keys, never AOSP test-keys** (`getprop ro.build.tags` = `release-keys`). With
    test-keys anyone can sign "system" apps and updates; the malware boxes (T95, BADBOX) were test-key builds.
15. **Production build, verified boot, locked bootloader, no root**: `ro.build.type` = `user`, `ro.debuggable` = `0`,
    `ro.boot.verifiedbootstate` = `green`, `ro.boot.flash.locked` = `1`, no `su` anywhere.
16. **A/B (seamless) system updates** that fall back to the old system if the new one doesn't boot, and for every
    firmware release the **signed OTA images** (full, and incremental from the previous release) sent to us: we deliver
    them through our own update channel (docs/cloud.md "Firmware"), their OTA updater is removed (item 4).
17. **Device owner**: device-admin supported (`android.software.device_admin` in `pm list features`) and Tundu set as
    device owner in the factory image (`al.tundu.games/al.moveit.cloud.AdminReceiver`), so app and firmware updates
    install silently.
18. **A clock battery (RTC)** if the board has the connector: the date survives unplugging (Game Pass and TLS need it).
19. **The NPU usable by our app**: the pose AI runs on the NPU (the GPU stays the games'), through our own copy of
    Rockchip's runtime (RKNN 2.3.2 in the APK) opening `/dev/dri/renderD129`. On the X98K Pro it works with the
    firmware's 2022 driver 0.7.2 (a warning only), the node is open to apps and SELinux is permissive; in a user build
    with SELinux enforcing, apps must still be allowed to use the NPU node. A recent RKNPU driver (0.9.x) is welcome:
    Rockchip recommends it for runtime 2.x.
20. **Cool enough for hours of play, and clocks that come down when the work does.** The X98K Pro reached 82–83 °C
    after a long session (its first passive trip point is 75 °C) and then throttled the GPU to 200–400 MHz and the CPU
    to 1.4 GHz. Its GPU governor (`simple_ondemand` on `/sys/class/devfreq/fde60000.gpu`) holds the top clock and
    voltage (700 MHz) from about 40 % load: over ~17 minutes of home screen and games it spent 972 s at 700 MHz and
    ~10 s anywhere between 300 and 600 (`trans_stat`), so a half-busy GPU burns full power. The CPU governor
    (`interactive`: `target_loads` 65, `timer_rate` 20 ms, `min_sample_time` 40 ms) keeps all four cores at 1.8 GHz,
    the top clock and voltage, for any game: one thread busy ~13 ms of a 20 ms window is enough, and a 30 fps game's
    main thread is (100 % of the time at 1.8 GHz on the home screen at 30 fps, 2026-10-31). Ask for: governors that
    clock down a half-busy GPU (Linux's default `simple_ondemand` thresholds: up at 90 %) and a CPU that isn't pinned
    to its top step by bursts (`target_loads` ~85-90, or `schedutil`), a heatsink that touches the SoC and vents in
    the shell, and a 1-hour test at our heaviest game staying under 75 °C. The app can't change any of this (it needs
    root, which the shipped firmware must not have, item 15), and Rockchip's power HAL only knows "interactive" and a
    performance boost, so Android's sustained-performance hint would raise the clocks, not cap them.

With item 3 (a system app signed with the firmware's platform key), MoveIt can later have its **own Wi-Fi screen**
and restart/power-off buttons. Ask the ODM whether they can sign our APK with their platform key, or whether they'll
build it into each firmware release. Silent updates don't need the platform key: device owner (item 17, or
`provision.ps1 -DeviceOwner` on pilot boxes) is enough (docs/cloud.md "Updates").

**Signing key:** release APKs must be signed with *our* keystore (not Unity's debug key), and every update must use the
same key, or boxes can't update. Create it once, back it up in two places, and never lose it.

## Layer 4: own firmware (only if no ODM delivers layer 3)

Build Android from Rockchip's SDK for the chosen board: a Linux build machine, roughly 300 GB of free disk and a few
days of work. Same result as layer 3, but we maintain it. Only worth it at larger volumes.

## Optional: hard kiosk lock

If pilot families still find ways out (some remotes have an "Apps" or "Settings" key), turn on Android's **lock task
mode**. This needs a small device-admin component in the app plus `adb shell dpm set-device-owner …` on a freshly reset
box. Not built yet. Add it only if the pilot shows it's needed.

## Phase 1 checklist additions (when the sample boxes arrive)

First box, 2026-10-30: **X98K Pro** (Rockchip RK3566 "BOX DEMO V10" board, 4 GB, Android 11 userdebug, build
X98KPRO_20231007-1607). Its full report is in `tools/provision/logs/` (gitignored); `provision.ps1 -Inspect` now also
runs `inspect-box.sh` (USB camera support, NPU, GPU, display, temperatures, who talks to the network).

- [x] `provision.ps1 -Inspect` on each box; fill in `disable-packages.txt`. X98K Pro: two vendor OTA updaters (ABUpdate
      `com.abupdate.fota_demo_iot` and Rockchip's), a vendor app store and "memory cleaner" (`com.yxt.*`, system rights),
      Aptoide TV, HiTV, CetusPlay, factory test apps; Kodi, Netflix, Prime Video, YouTube and the media apps are a
      product decision (commented out)
- [ ] Remote: does OK arrive as Enter / D-pad center? Back as Esc? Home returns to MoveIt? Any "Apps"/"Settings" key?
      (X98K Pro: D-pad, OK and Back drive Tundu over `adb shell input keyevent`; the real remote is `fdd70030.pwm` IR)
- [x] `-BootAnimation`: does this box allow root + remount? X98K Pro: yes (`ro.debuggable=1`, so `adb root` works);
      the stock animation is `/system/media/bootanimation.zip`
- [ ] After provisioning and reboot: boots straight into MoveIt, no launcher chooser, no screensaver after 30 minutes
- [ ] Settings → Wi-Fi opens and Back returns; volume works; language persists after reboot
- [ ] `adb logcat MoveItMotion:V *:S`: is ALLM supported (box + our TV)? Which camera fps ranges, and does the camera
      stay at ~30 fps with the room lights dimmed? X98K Pro: **no ALLM** (`allmSupported false`, also no game content
      type), so the TV's own Game mode has to be switched on by hand. Only 1920x1080 at 60 Hz offered to apps. With our
      camera (2026-10-30): fps ranges `[[3, 6], [4, 9], [10, 20], [15, 30]]`, so the app keeps the default range, and
      only 1024x768 and up at 30 fps (item 7); its fps in a dimmed room not measured yet (the pose AI held it to 10-11)
- [x] Security properties (items 14-15): `getprop ro.build.tags ro.build.type ro.debuggable ro.boot.verifiedbootstate`;
      watch the box's network traffic on guest Wi-Fi for unknown destinations (BADBOX check). X98K Pro, **out of the
      box**: adb listens on port 5555 on the network with no authorisation (`ro.adb.secure=0`,
      `persist.internet_adb_enable=1`), userdebug build, SELinux permissive, unlocked bootloader (orange), a setuid
      `/system/xbin/su`, a fake Pixel 5 fingerprint, security patch 2021-10-01: anyone on the same Wi-Fi gets a root
      shell. Fine for development, never for families: layer 3 must be a user build with adb off. Network in the first
      25 minutes: Google services, and the stock launcher (system uid) holding several HTTPS connections to a
      Cloudflare-hosted server; provisioning disables that launcher
- [ ] `pm list features` lists `android.software.device_admin`? `provision.ps1 -DeviceOwner` on a reset box, then a
      `beta` update installs without a prompt (`adb logcat TunduCloud:V *:S`). X98K Pro: the feature is there and
      the box has no accounts
- [ ] Unplug the box for a minute: is the date still right after boot (RTC), and how soon does the network fix it?
      (X98K Pro: time zone set to London out of the box, and 6 s behind the laptop with automatic time on)
