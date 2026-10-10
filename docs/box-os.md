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
7. **USB camera works through Android's camera API** (external camera HAL enabled), and survives reboot and replug.
8. **adb**: off in shipped units, or locked to our key. Keep a way for us to re-flash or update.
9. **Remote**: our logo printed on it; confirm which key codes OK/Back/Home/Volume send.
10. **Box shell print** and **retail packaging** with our logo; **CE** documents for the box and the camera.
11. A **factory-reset** path that comes back to MoveIt (because MoveIt is in the system image).
12. **TV game mode over HDMI (ALLM)**: Android 11+ with `Display.isMinimalPostProcessingSupported()` = true, so the
    TV switches to its low-latency game mode when our app runs (our manifest asks for it). TVs otherwise add
    50–150 ms of picture processing. Our app logs `TV game mode (ALLM) supported: …` at start.
13. **USB camera holds 30 fps in a dim room**: the camera must offer an auto-exposure range starting at 30
    (`[30, 30]` or `[30, 60]`); the app picks it and logs `Camera fps ranges: …` and the range it chose.
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

- [ ] `provision.ps1 -Inspect` on each box; fill in `disable-packages.txt`
- [ ] Remote: does OK arrive as Enter / D-pad center? Back as Esc? Home returns to MoveIt? Any "Apps"/"Settings" key?
- [ ] `-BootAnimation`: does this box allow root + remount?
- [ ] After provisioning and reboot: boots straight into MoveIt, no launcher chooser, no screensaver after 30 minutes
- [ ] Settings → Wi-Fi opens and Back returns; volume works; language persists after reboot
- [ ] `adb logcat MoveItMotion:V *:S`: is ALLM supported (box + our TV)? Which camera fps ranges, and does the camera
      stay at ~30 fps with the room lights dimmed?
- [ ] Security properties (items 14-15): `getprop ro.build.tags ro.build.type ro.debuggable ro.boot.verifiedbootstate`;
      watch the box's network traffic on guest Wi-Fi for unknown destinations (BADBOX check)
- [ ] `pm list features` lists `android.software.device_admin`? `provision.ps1 -DeviceOwner` on a reset box, then a
      `beta` update installs without a prompt (`adb logcat TunduCloud:V *:S`)
- [ ] Unplug the box for a minute: is the date still right after boot (RTC), and how soon does the network fix it?
