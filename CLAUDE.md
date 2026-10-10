# Tundu (formerly "MoveIt"): handoff for the next session

Read this first. It is the state of the project as of **2026-10-10**, written so a new session can continue
without the old conversation.

## The product
**Tundu** ("Tundu!" = "move! shake it!"): a cheap **Android TV box + USB camera** that plugs into any TV and runs our
own **motion games** (Kinect-style, camera only, no depth sensor). Target: **families at home in Albania** (later
Kosovo, North Macedonia, the diaspora), retail **≤ €99**, solo developer, **< €500 before first sales**.
Albanian-language UI is a selling point. **No ads, analytics or IAP SDKs** (kids' camera device, privacy).
Code identifiers still say MoveIt (C# namespaces `MoveIt.*`, Java package `al.moveit.motion`, Unity menu *MoveIt*,
folder `unity/MoveItGames`): internal only, renaming them is optional churn. Product name, app id
(`al.tundu.games`), icon and APK name (`Tundu.apk`) come from `brand.json`.

Plans: `docs/plan.md` (original business and tech plan) and `docs/production-plan.md` (current roadmap P1–P5).
The user writes informal English, wants clear recommendations rather than option lists, and is the developer.

## Current status
- **Phase 0 (PC prototype): done.**
- **P1 "recognition on the box": done** except the hardware-specific parts. The camera AI runs **inside the APK**.
  Tested end to end in the **MuMu Player** emulator with the laptop webcam: GPU, full model, 30 fps, ~10 ms per
  frame, body tracked, Fruta playable by moving.
- **Motion polish (2026-10-08): done**, after the user said it felt "not responsive enough". Measured offline first
  (a simulator mirroring the C# filters and gesture rules on realistic sidesteps, knee-dip jumps and swipes), then:
  body-relative One Euro filters (`OneEuroFilter` takes a scale = torso length; smooth track trails fast moves ~3×
  less), **jumps that start with a knee dip are jumps** (before: 0–8 of 20 realistic jumps detected, the dip was read
  as a crouch and blocked the jump; now 20/20, 45–65 ms after take-off), lane look-ahead (sidestep switches ~60–100 ms
  sooner, never on leans/sway), every packet processed (none dropped when the game runs slower than the camera),
  Runner lane change eases out. Android: camera auto-exposure floor ~30 fps (no 15 fps in dim rooms), reused frame
  Bitmap (no 1.2 MB allocation per frame), `preferMinimalPostProcessing` (TV game mode via HDMI ALLM).
  New test player `fake_sender.py --script jumps` + PlayMode test `RealJumpsWithKneeDip_AreJumpsNotCrouches`.
- **P2 Brand: name and brand kit done, waiting for the user's review.** Name **Tundu** (checked 2026-10-07: TMview
  clear in classes 9/28/41, only two food marks; tundu.al/.com.al/.games/.app had no nameservers; tundu.com taken but
  unused; no Play app). Runner-up was "Kërce". Logo = our own rounded-stroke lettering, the last "u" is a kid cheering
  mid-jump (arms = stems, coral dot = head). Palette Sun #FFC93C / Coral #FF5A4E / Sky / Grape / Leaf on Night
  #141938. Fonts **Fredoka** (titles) + **Nunito** (text), both OFL with ë/ç: not in the repo yet (downloading them
  needs the user's OK). Brand board artifact: https://claude.ai/artifact/67UD2gv3nGtbfDcBLSfr5R. New tagline
  "Luaj me gjithë trupin!" and the board's Albanian sample strings need the user's review. User to do: register
  tundu.al, later file the mark in Albania (DPPI), check Kosovo.
- **Vrapo! is dinosaur-themed (2026-10-08, user's request: "a dinosaur runs").** Made in Blender 5.1 by scripts in
  `art/` (rebuildable): **Dino** (cute chunky T-rex in palette colours, 19-bone rig, clips Idle / Run / Jump / Slide
  (belly slide, fits under the 1.15 m bars) / Hit / Cheer) and the world (Log = jump, VineBridge = duck,
  Boulders = dodge, Egg = coin, Fern/Rock/Cycad scenery, Volcano backdrop). Everything shares one 64×64 palette
  texture = one material. `RunnerPlayer` plays the clips (run speed follows the game, leans into lane changes,
  faces you while waiting and cheers on a new best); `RunnerGame` uses the art prefabs when assigned (gray boxes
  otherwise) and reshuffles roadside scenery as ground tiles loop. HUD "Monedha" → "Vezë". Checked in Play mode
  with `fake_sender.py --script runner` + DevCapture. Not yet: an APK build with the art, a look on the box.
- **Music, sounds and Runner polish (2026-10-08).** The user wants **all music to sound like Wii games** and to
  change it themselves (see `music/README.md`). Original tracks composed as code → editable MIDI (`music/midi/`)
  → rendered by our own numpy synth (`tools/music/`) → OGG in `Assets/Resources/Music` (menu, runner, slash,
  bowling, jingle_win, jingle_gameover) + 24 SFX in `Assets/Resources/Sfx`. `GameAudio` plays them by file name
  (drop-in replaceable), hooked into the menu and all three games. Vrapo! now looks like the famous runners:
  `Tundu/Toon` shader with a curved world (`_TunduCurve`), gradient sky (`Tundu/Sky`), clouds, pterodactyls,
  volcano smoke, dust, egg sparkles with "+10" pop-ups, crash and dizzy stars, speed lines, blob shadow, squash &
  stretch, speed FOV and crash shake (`RunnerFx`, `Tundu/Fx`), plus post-processing (colour grading, vignette,
  quarter-res bloom). Not yet: Fruta/Bowling art and visual polish, HUD restyle (that's P3), checking bloom and
  shadows cost on the real box.
- **Pikturo! (4th game, 2026-10-08, user's request): paint on the TV with the hands.** Both hands are brushes; the
  pen rule (`PaintPen`): raise a hand and hold it still (cursor inside a 55 px circle for 0.35 s; a ring closes) =
  brush down, drop the arm (straight down, full length in the picture, not reaching forward) or leave the picture =
  brush up. (The first rule, "slower than 0.5 screens/s", painted while a slow hand was still arriving: the user saw
  "drawing starts immediately"; fixed 2026-10-08 with evidence from `tools/motion/cursor_sim.py`.) Paint stays "wet" 0.5 s
  and the fast arm-drop at the end of a stroke is wiped, so strokes get no tail. Toolbars pressed by holding a hand
  on a button (0.6 s; clear/camera/photo 1 s): 8 tools left (marker, rainbow, spray, neon, glitter, stamps
  star/heart/flower/dino-footprint/bubble, eraser, bucket), 11 colours right, top: undo (8 steps), size S/M/L,
  symmetry, colouring pages (6, `tools/paint/pages.py`), clear, camera mode, photo (PNG in persistentDataPath/Photos,
  newest 60 kept, box only). **Camera mode** (the user's "transparent canvas"): the camera picture behind the
  painting, framed on the player from the knees to above the head so the hands line up with their image and the
  toolbars are within reach; the framing follows the player while nobody paints (the painting moves with it).
  Inspirations: Kinect Sparkler (light painting in the air; reviewers missed colours and undo), Mario Paint / Kid Pix
  (stamps, fun tools), colouring books (bucket + line art). Remote: ← → colour, ↑ ↓ tool, OK photo. Music `paint.ogg`
  ("Paint Box") + 8 `paint_*` sounds. Not yet: APK build and a look on MuMu/the box, a photo gallery screen, a
  parent setting for photos, higher-resolution camera picture on the box (640×480 is upscaled).
- **2026-10-09: Pikturo is one-handed + switch** (user's idea): the right hand paints (button "Dora" / key H swaps,
  remembered), raising the other hand to the chest (wrist > 0.6 torso above the hips, off below 0.35) = brush down.
  Brush cursor = `HandInterpolator` (between real samples, ~1 frame behind, switch state carried per sample):
  overshoot at stops 8 px instead of 55–68 (see "Hand cursors" below for the later fixes). `IdleFidgeting_TriggersNothing` fails
  intermittently (one false bowling throw, 3 of the last 5 full runs; fake_sender's noise is unseeded): investigate.
- **Hand cursors (2026-10-08 "stuttery and janky", 2026-10-09 still "jarring, skips from point to point" in Fruta
  and Pikturo, while the skeleton mirror looks smooth; the user compares with Nex Playground's fruit game).** The
  mirror draws the heavily smoothed track, small; the cursors use low-lag data magnified ~10× by the hand zone.
  Fixed on 2026-10-09, numbers from `tools/motion/cursor_sim.py` (`report_2026_10_09`):
  (1) `HandPresence`: a wrist sample with visibility < 0.5 used to drop the hand (Fruta's blade vanished and snapped
  back ~220–270 px; Pikturo's stroke ended and restarted: new undo step, gap, and a dip of the free hand could leave
  the switch off). Now found at 0.5, kept down to 0.3 (like the mirror), unsure samples skipped, lost after 0.3 s.
  (2) `HandInterpolator` is the one cursor engine (`HandSmoother` deleted): samples on the camera clock (timing them
  by the receiving game frame made a steady hand's per-frame steps range 0.2–1.7× instead of 0.93–1.08×),
  interpolated `Margin` camera frames behind, gliding on past the newest sample up to `MaxExtrapolation`.
  (3) `BodyHands` feeds it **raw** samples (`Body.Raw`) with one filter (margin 0.5, glide 80 ms, One Euro 1 / 4 / 2):
  smoothing twice (low-lag track + cursor filter) made fast moves uneven. Fruta slashing: stutter 25 → 15 px/frame,
  worst step 103 → 42 px, same lag (~75 ms from capture); rest jitter 2 → 4 px/frame. `Hand.Velocity` and
  `SamplePosition` stay on the low-lag track (slash speed, Pikturo's brush). Pikturo's brush (margin 1, no glide,
  4 / 1 / 1) only gained (1). (4) `Body.HandTip`: the fingertip extension fades in with elbow visibility (it switched
  at 0.3 and moved the cursor ~100 px). (5) Fruta's blade width/brightness eased (switched at the slash speed 30×/s).
  The dip rate is a guess: **the user should record a session** (`main.py --record`, swipes + painting + rest) to
  replay through the sim; then judge on the real camera. Latency (Nex feels fast) is mostly camera fps and inference,
  not filters: 60 fps cameras and the box's GPU/NPU are the levers.
- **Shpata! (5th game, 2026-10-09; reworked 2026-10-10 into a duel, user: "1 hand sword 1 hand shield, player on
  center, attack and counterattack, not fixed directions; loses whichever finishes the health first").** After Wii
  Sports Resort Swordplay. The player is a see-through figure in the middle (`GhostAvatar`: shapes posed from the camera
  joints, x/y from the picture, depth from the world joints, each joint a `HandInterpolator` on the camera clock;
  shader `Tundu/Ghost`, depth material at queue 2999 + colour at 3000 so overlaps tint once; the head fainter). Sword
  along the dominant forearm (turning capped at 1500°/s: a forearm pointing at the TV flipped it = phantom swings),
  a silver round shield on the other hand. Camera centred above and behind (0, 2.55, −1.35): the figure's head covers
  only the knight's legs. Toy knights (`Knight`: primitives, cute face, coloured shield) duel one at a time: **Guard**
  (shield in front, swings clang off, may strike right back), **WindUp** "!" from above / left / right with a ring on
  the spot it lands (the shield over it, or a sword parry, = blocked → **Staggered** 2 s, open: counterattack), else a
  heart lost → **Recover** 0.9 s open; sometimes **Opening** (shield lowered 1.1 s). Player 5 hearts, knight 4 + duel
  HP; each next knight winds up quicker (1.6 → 0.7 s), lowers its guard less, ripostes more; +2 hearts after a win.
  Rules on the screen in `SwordMoves` (swing = tip > 1.6 screen heights/s, swept hit test, `ShieldCovers`,
  `Parries`). Keyboard: arrows swing, space shield, H swaps hands (remote ↑ on the box). Music `sword.ogg` ("Little
  Knights") + 7 `sword_*` sounds. Not yet: the user's review (Albanian in `SwordStrings`, the look), a try with the
  real camera (thresholds from the scripted player only), Blender knights, an APK build.
- **Fluturo! (6th game, 2026-10-10, user: "a flying superhero game ... control while flying and punching"; they didn't
  know the win/lose mechanic, so it was designed in a cloud session).** The hero flies forward over a toy city (seen
  from behind and above); the hero follows the body in its window, no lanes (`FlyRun.Steer`: a step of 0.85 torso
  lengths = the edge, duck = low, a jump lifts it for 0.7 s). Robots fly in, hover in front (their eye reddens), then
  charge at where the hero is: punch them (any fist, any direction, within reach: a gold ring shows what a punch would
  hit) or move away while they charge; a robot that reaches the hero = a heart. Stars and hearts drift past (fly
  through). After each flight (35/39/43 s) the giant robot throws fireballs at the hero: punch them back to hurt it
  (4/5/6 hits), or dodge. **Win** = the third boss beaten (about 3 minutes); **lose** = 5 hearts gone; hearts refill
  after each boss, up to 2 heart pickups per level. Balance from simulated players (`FlyRunTests.Balance`): perfect
  20/20 wins, "child" (0.45 s reactions, 65 % of punches in time, dodges half) 17/20, "struggling" 5/20, standing still
  loses in ~25 s. **Punches**: `MoveSDK/Runtime/PunchDetector` (a bent arm straightening fast: shoulder-wrist distance /
  arm length from <= 0.74 to >= 0.86 within 0.45 s with the fist moving away >= 2.6 torso lengths/s, measured over
  0.15 s; in 3D with world joints so jabs straight at the TV count; or a straight-arm swing toward the TV that stops
  there; never pointing down). Evidence in `PunchDetectorTests` (simulated 3D arms, a perspective camera, noise, the
  BodyTracker filters, adult and child): all 10 punch kinds seen as the fist arrives (-50..+120 ms), no false punch
  from steering, ducking, jumping, arm raises, dropping/raising the fists; a 0.9 s reach toward the TV counts 2 of 24
  times (physically just a slow punch). Without world joints only sideways punches work (both services send them).
  Code: `Games/Fly` (`FlyRun` rules without a scene, `FlyGame`, `Hero`, `RobotView` (robots and the boss), `FlyWorld`
  (city and clouds baked into one mesh per colour), `FlyArt`, `FlyStrings`). Keyboard: arrows fly, space punches. Music
  `fly.ogg` ("Sky Patrol") + 12 `fly_*` sounds. Not yet: everything visual was written in the cloud without Unity
  (compile-checked, not seen): **the user must run Build All Scenes, then play it and the PlayMode test**; the Albanian
  in `FlyStrings`; a try with the real camera; an APK build and the box's frame rate.
- **Camera preview stream (protocol v1.1 addition, docs/protocol.md):** JPEG frames over UDP 127.0.0.1:5006, only
  while a game asks (`CameraFeed.Request()`): PC `video.py` (control port 5007), fake player draws a room + the
  scripted player, Android `VideoStreamer.java` via JNI `setVideo` (encodes on its own thread, never delays the
  pose AI). The Java compiles (checked with javac against the cached dependencies); not yet run on MuMu.
- **Cloud: gift cards, Game Pass, updates (2026-10-10, user: "gift cards that activate game pass for x time, system
  updates of the consoles, push new free and game pass games; don't get hacked, no version mismatches, crashes,
  corruptions, exploits"). Researched and built in a cloud session: design, threat model, setup steps, runbooks and
  sources in `docs/cloud.md`.** Two halves: **updates + game catalog are static manifests signed offline** on the
  laptop (release key, `server/cli/tundu.ts release …`, files on R2 `cdn.tundu.al` named by SHA-256), so a hacked
  server can't push anything; a **small API** (Cloudflare Worker + D1, `server/`, no runtime dependencies,
  `api.tundu.al`) does enrolment, codes → days, check-ins, health events and the admin API. Codes: 16 characters (75
  bits + Luhn mod 32 check, no 0/1/I/O), stored as HMAC(pepper), batches inactive until activated, one-time claim in
  one D1 transaction, retries idempotent by request id, max 10 wrong codes per box per day. Game Pass belongs to the
  box (no accounts): signed token (ECDSA P-256, DER; keys pinned per role in `TrustedKeys.java`) + `PassClock`
  (server time + monotonic clock; the floor never goes back). Box client `Assets/Plugins/Android/MoveItCloud.androidlib`
  (Java, `al.moveit.cloud`): verify, rollout buckets, resumable verified download, PackageInstaller (silent as device
  owner: `provision.ps1 -DeviceOwner`), installs only when no game runs (first minute after the box starts, menu idle
  10 min, night 02-05, or safe mode after 3 starts that never reached the home screen), crash reports from
  ApplicationExitInfo, firmware via `installSystemUpdate` (device owner; exact model + fingerprint). Unity:
  `Games/Common/Cloud` (`TunduCloud.cs` bridge, simulated in the editor: any well-formed code adds 30 days, e.g.
  `TUND-UTES-TKAR-TE2R`; `GameAccess`; `GiftCode`), `Shell/Scripts/GamePassPanel.cs` (code entry with the remote,
  Settings → Game Pass, "GAME PASS" badge on locked tiles, catalog can hide games), BoxBuild: versionCode =
  MAJOR*10000 + MINOR*100 + PATCH, release signing from `TUNDU_KEYSTORE*` environment variables, menu *Build Release
  APK (signed)*. **Every game stays free** until the signed catalog (or `GameAccess.DefaultTier`) says otherwise.
  Not yet: the user's Cloudflare account, keys and keystore (**no production keys exist: `releases/keys.json` is
  empty, so builds trust no manifest or token yet**), a look in Unity (Game Pass screen, the changed
  `HomeScreenTests`; `BoxBuild.cs` can't be compile-checked in the cloud), a real silent update on a box, QR scanning
  with the camera, the user's decisions (which games in Game Pass, prices, card durations; **cards are sold only
  offline, in shops and in person: no payment system, user's decision 2026-10-10**), the accountant (fiscal
  receipts for cards, VAT on vouchers). `npm audit`: undici advisories in local dev tools only; upgrade wrangler ≥
  4.149 and @cloudflare/vitest-plugin ≥ 1.4.0 after 2026-10-22.
- **Hardware sourcing (2026-10-08): `docs/sourcing.md`**: AliExpress links for the samples (checked from Albania,
  prices in lek), Alibaba batch suppliers (boxes with custom firmware/logo, OEM webcams, packaging), the kit contents,
  customs (€22 duty-free, VAT 20%), CE/AKEP, the step-by-step process, unit cost (RK3566 kit €53–82 landed).
  Retail sample prices went up: the recommended sample set is ~€255, not €150–190.
- Then **P3 production UI** (replace IMGUI with **UI Toolkit**, one USS theme from the brand; first-run setup,
  animated home, how-to-play cards, results, sounds; this also fixes washed-out colors on Android), **P4 game polish**
  (Kenney art on `D:\GameAssets_Kenney`: platformer kit, Cube Pets, Input Prompts), **P5 release engineering**
  (release keystore, OTA updates, git, scripted builds).
- **Sample boxes NOT ordered yet** (`docs/sourcing.md` has the links: Tanix TX66 RK3566, H96 Max M9 RK3576,
  Spedal 931 120° webcam).
  Remind the user: delivery takes 2–4 weeks, and real speed numbers plus the Rockchip NPU path need them.
- **Git: on GitHub since 2026-10-10** (https://github.com/PlotterOtterGames/moveit, branch main, first commit
  be51fc6, the user asked for it). Commit and push only when the user asks.

## Repository map (`D:\Projects\moveit`)
| Path | What |
|---|---|
| `motion-service-py/` | PC camera service: `main.py` (OpenCV + MediaPipe 1.0.1, `--model auto`, sends protocol v1.1), `video.py` (camera preview stream on request), `fake_sender.py` (scripted test player: `basic`, `bowl`, `slash`, `idle`, `dropout`, `jumps`, `runner`, `paint`, `sword`, `fly`; draws a fake camera picture when asked), `replay.py`, `models/*.task`. Venv in `.venv` (Python 3.12) |
| `docs/protocol.md` | **Motion Protocol v1.1**: UDP JSON to 127.0.0.1:5005; 17 COCO joints `k` (+ optional world joints `wk`, `cam`, `model`) |
| `unity/MoveItGames/` | Unity **6000.3.9f1** project (URP), **active platform: Android**. Editor at `D:\Unity\6000.3.9f1` |
| `Assets/MoveSDK/Runtime` | Motion SDK: `CameraFeed` (camera picture for AR modes, on request), `MotionSystem` (UDP receiver, feeds every packet in order), `BodyTracker` (smooth + fast low-lag tracks with body-relative One Euro filters, velocity, world joints, 1.5 s dropout continuity), `BodyGestures` (lanes with look-ahead, jump at take-off incl. from a knee dip, crouch at onset, hands up), `BodyHands` (latency-predicted hand cursors), `UnderhandThrowDetector` (bowling, world depth + backswing), `PunchDetector` (punches in any direction, 3D with world joints, tested on simulated arms), `BodyInput` (body + keyboard merged), `Android/AndroidMotionService.cs` (starts the Java service, camera permission, status) |
| `Assets/Plugins/Android/MoveItMotion.androidlib` | **Java camera AI plugin**: CameraX 1.6.2 + MediaPipe tasks-vision 1.0.0, **synchronous VIDEO mode** on the CameraX analysis thread, GPU→CPU fallback, auto full→lite, sends protocol packets to localhost, on-demand camera snapshot. Models copied in at build time (gitignored) |
| `Assets/Plugins/Android/AndroidManifest.xml` | Registers the app as the box's **HOME launcher** (`singleTask`); entry point = Activity; asks the TV for game mode (`preferMinimalPostProcessing`) |
| `Assets/Games/Common` | Shared game code: `GameHud` (IMGUI helpers, temporary), `CommonStrings` (Albanian/English, persisted), `PlayerGate`, `RemoteInput` (TV remote mapping), `AndroidDevice` (volume, Wi-Fi screen), `Brand` (reads `Assets/Resources/brand.json`), `RecognitionLab` (camera test screen), `DevCapture`, `BoxRuntime` (60 fps, no sleep) |
| `Assets/Games/Runner`, `Slash`, `Bowling` | The 3 gray-box games: Vrapo! (runner), Fruta! (slash), Bowling (1–4 players, `BowlingScore` with tests) |
| `Assets/Games/Paint` | Pikturo!: `PaintGame` (modes, toolbars, hands, photos), `PaintCanvas` (premultiplied RenderTexture, wet paint, undo, bucket), `PaintBrush` (tools along smoothed strokes), `PaintPen` (pen rule), `PaintArt` (brush atlas and icons drawn in code), shader `Tundu/Brush`. Pages in `Assets/Resources/Paint/Pages` (alpha-only import: `Editor/PaintPageImport.cs`) |
| `tools/logic-tests/` | .NET 8 + NUnit 3 project that links the engine-free game rules and their EditMode tests from the Unity project and compiles them against `UnityShim/` (cloud sessions: `dotnet test tools/logic-tests`) |
| `tools/compile-check/` | `check.py`: compiles every asmdef except MoveIt.Editor as its own .NET project (same sources, defines, references) against Unity's UnityEngine reference assemblies (NuGet `UnityEngine.Modules` 2021.3) and `Stubs/` (Input System, test runner), for the Android player and the editor. Names Unity 6 added are listed in `UNITY6_ONLY` |
| `tools/motion/cursor_sim.py` | Hand-cursor and Pikturo-pen simulator (mirrors OneEuroFilter, BodyTracker, BodyHands, the game loop; realistic noise and timing): stutter, shake, lag, swipe speed, when the brush goes down. Run it before changing cursor or pen numbers |
| `Assets/Games/Fly` | Fluturo!: `FlyRun` (all rules: flight, robots, stars, hearts, the boss, levels, win/lose; no scene, tested with simulated players), `FlyGame` (body/keys to steering and punches, draws the run, HUD), `Hero` and `RobotView` (built from primitives), `FlyWorld` (scrolling toy city + clouds), `FlyArt` (ring, star mesh, heart texture), `FlyStrings` |
| `Assets/Games/Sword` | Shpata!: `SwordGame` (duel: knight AI, blocks, counterattacks, markers, HUD), `SwordMoves` (swing/hit/shield/parry rules on the screen, tested), `GhostAvatar` (see-through player + sword + shield), `Knight` (toy knight, animated by state), `SwordArt` (shield-spot ring, alert, heart drawn in code). Shader `Games/Common/Shaders/TunduGhost` |
| `tools/paint/pages.py` | Colouring pages as code (OpenCV, figures outlined by morphology): dino, house, fish, butterfly, rocket, cake. `--preview` writes checks with the toolbar areas to `tools/paint/out/` |
| `docs/sourcing.md` | Where to buy every part (links), batch suppliers, Albanian import and CE, the ordering process |
| `docs/cloud.md` | **Gift cards, Game Pass, app/firmware updates, new games**: recommendation, threat model, formats, release steps, setup, runbooks, costs, sources |
| `server/` | Tundu cloud: Cloudflare Worker + D1 (`src/`, `migrations/`, `wrangler.jsonc`), the CLI `node cli/tundu.ts help` (keys, cards, devices, stats, releases, a pretend box `devbox`), tests (`npm run test:all`: typecheck, 49 Worker tests in workerd, 14 CLI tests), shared test vectors `test/vectors` (codes, signed docs, rollout) checked by Java and C# too. Node ≥ 22.18 runs the TypeScript CLI directly |
| `releases/` | Public keys the app pins (`keys.json` → generated `TrustedKeys.java`), signed channel manifests (git history = release log), staged APK/firmware lists. Written only by the CLI |
| `Assets/Plugins/Android/MoveItCloud.androidlib` | **Box cloud client** (Java, no dependencies): `TunduCloud` (Unity API + jobs), `CloudState` (all rules, pure), `SignedDoc`, `PassClock`, `PassToken`, `ChannelManifest`, `GiftCode`, `Downloader`, `CloudApi`, `Installer` (+ receivers), `UpdateJobService`, `DeviceOwner`/`AdminReceiver`, `FirmwareUpdater`, `CrashReports`, generated `TrustedKeys` |
| `Assets/Games/Common/Cloud` | Unity side: `TunduCloud.cs` (JNI bridge, editor simulation), `GameAccess` (catalog rules, tested), `GiftCode` (tested against the server vectors). UI: `Shell/Scripts/GamePassPanel.cs` |
| `tools/cloud-java-tests/` | `run.py`: compiles MoveItCloud against Android API 36 (Robolectric's android-all jar from Maven Central, ~200 MB cached) with warnings as errors and runs its 33 JUnit tests |
| `Assets/Shell/Scripts/MainMenu.cs` | Box home screen (6 game tiles; a step left/right or ← → moves one tile) + Settings (Wi-Fi, volume, language, camera, version, device; hidden Android settings: 7× OK on "Device") |
| `Assets/Editor/MoveItSceneBuilder.cs` | **Scenes are generated by code**: menu *MoveIt → Build All Scenes* (Menu, Runner, Slash, Bowling, Paint, Sword, Fly, MotionDebug). Re-running overwrites them |
| `Assets/Editor/BoxBuild.cs` | *MoveIt → Box → Build Android APK* → `builds/android/<brand name>.apk` (~54 MB). Applies branding, IL2CPP ARM64, min API 28, TV banner and icon, copies models, sets GRADLE_USER_HOME=D:\.gradle in-process, caps Gradle heap at 2 GB |
| `Assets/Resources/brand.json` | **The brand's single source**: name, app id, taglines, palette (primary/secondary/accent/grape/leaf/background/surface/text/textMuted, 4 player colors), fonts. Read by `Brand.cs`, `BoxBuild.cs`, the scripts in `tools/` |
| `tools/branding/` | `logo.py` (the logo as geometry → SVG + anti-aliased PNG), `make_branding.py` (logo files in `brand/logo/`, icon/banner/logo/mark in `Assets/Branding/`, boot animation + boot logo BMP + GIF preview in `out/`), `brand_board.py` (the review page). Run with the motion-service venv |
| `brand/logo/` | Logo masters: on-dark, on-light, white, night, mark, icon (SVG + 2048 px PNG) |
| `art/` | **Blender art as code** (Blender 5.1): `tools/tundu_art.py` (palette + UVs-to-palette-cell, ellipsoid/cone/part helpers, preview renders), `runner/dino.py` (shape), `runner/dino_rig.py` (bones, envelope weights, clips, FBX export), `runner/world.py` (props, one FBX each), `runner/runner.blend` (saved result). Previews render to `runner/previews/` (gitignored) |
| `music/` | `README.md` (how the user changes music), `midi/*.mid` (editable sources; compose.py never overwrites them without `--force`) |
| `tools/music/` | `midi.py` (SMF read/write), `synth.py` (Wii-style instruments, GM program map, drums, reverb), `render.py` (MIDI → seamless-loop OGG via ffmpeg), `compose.py` (the songs as code), `sfx.py` (sound effects) |
| `Assets/Resources/Music`, `Sfx` | Drop-in audio loaded by name by `Games/Common/GameAudio.cs`; import rules in `Assets/Editor/AudioImport.cs` (music streams, SFX mono) |
| `Assets/Games/Common/Shaders/` | `TunduToon` (toon + curved world, used by all runner art and ground), `TunduSky` (gradient skybox), `TunduFx` (particles, blob shadow) |
| `Assets/Games/Runner/Art/` | Exported FBX + `tundu_palette.png` + generated `Dino.controller`. Import rules in `Assets/Editor/RunnerArtImport.cs` (point-filtered palette, no imported materials, clips named/looped) |
| `tools/provision/provision.ps1` | Turns a stock Android box into a MoveIt box over adb (`-Inspect`, `-BootAnimation`, `-Undo`) |
| `tools/dev/run-on-mumu.ps1` | Installs the APK into MuMu (adb 127.0.0.1:16384) with camera permission, starts it, shows logs |
| `docs/box-os.md` | How Android is hidden (layers 1–4) and the checklist to send the box maker (ODM) |

## How to run and verify
- **PC camera service:** `cd motion-service-py && .venv/Scripts/python main.py --preview`. Without a camera:
  `.venv/Scripts/python fake_sender.py --script basic`.
- **Unity tests** (via the unityMCP tools): PlayMode 11 (`GesturePipelineTests` ×6 using fake_sender, `HomeScreenTests`,
  `PaintGameTests` ×3: paper mode, camera mode, all tools + photo; screenshots in `unity/captures/paint_*.png`;
  `SwordGameTests`: the duel with the scripted player: a swing off the guard, shield blocks from all three sides each
  followed by a counterattack that lands, an unblocked blow costs a heart, `sword_*.png`),
  EditMode 29 (`BowlingScoreTests` 10, `PaintCanvasTests` 11, `HandCursorTests` 3, `SwordMovesTests` 5; + since
  2026-10-10, not yet run in Unity: `GiftCodeTests` 3, `GameAccessTests` 4; `HomeScreenTests` now also opens
  Settings → Game Pass). On 2026-10-09
  all passed (2026-10-10: 11/11 PlayMode, 29/29 EditMode; `IdleFidgeting_TriggersNothing` still fails now and then).
  Added in the cloud on 2026-10-10, not yet run in Unity: EditMode `PunchDetectorTests` 5 (+ 2 explicit diagnostics),
  `FlyRunTests` 17 (both pass under dotnet), PlayMode `FlyGameTests` (fake_sender --script fly, port 5014: exactly 6
  punches with the right hands, each smashes a robot, the body steers, the boss comes; screenshots `fly_*.png`).
  `SwordGameTests` uses port 5013. Run them once at the start of
  a session. `PaintGameTests` use ports 5012 (motion) and 5016/5017 (video), so a running `main.py` doesn't interfere.
  Without the editor open: `Unity.exe -projectPath … -runTests -testPlatform EditMode|PlayMode -testResults <xml>`
  (scenes: `-batchmode -executeMethod MoveIt.EditorTools.MoveItSceneBuilder.BuildAll -quit`). EditMode works with
  `-batchmode`; **PlayMode hangs in `-batchmode`** (screenshots wait for end of frame, the home screen test too):
  run it without `-batchmode` (an editor window opens and closes by itself).
- **Motion tuning:** change filter or gesture numbers only with evidence. The PlayMode tests guard the obvious
  cases; for lag numbers, simulate realistic motions (knee-dip jumps, 0.3–0.6 s sidesteps, 5–7 m/s swipes) with
  camera-like noise (0.003 normalized) against the exact C# rules. Real sessions recorded with `main.py --record`
  and replayed with `replay.py` are the next step.
- **Blender art:** start Blender 5.1 (`C:\Program Files\Blender Foundation\Blender 5.1\blender.exe`); its MCP
  extension (lab.blender.org "mcp") auto-starts the bridge on port 9876, then `execute_blender_code` works. Run e.g.
  `importlib.reload(dino); dino.build(); dino_rig.rig(); dino_rig.animate(); dino_rig.export()` and
  `world.build_all(); world.export_all()` (add `D:\Projects\moveit\art\tools` and `art\runner` to `sys.path`), save
  `art/runner/runner.blend`, then in Unity refresh and *MoveIt → Build Runner Scene*. Check props with
  `world.game_view()`: a render from the exact game camera on a 3-lane road (it showed the 6 m boulder lying across
  all lanes; now props are fitted to their lane with `fit()`). The MCP's background mode
  (`*_for_cli`) needs `BLENDER_PATH` in the MCP server config (not set).
- **Android:** build in Unity (*MoveIt → Box → Build Android APK*), then
  `powershell -ExecutionPolicy Bypass -File tools/dev/run-on-mumu.ps1`. Read the motion logs with
  `adb -s 127.0.0.1:16384 logcat MoveItMotion:V *:S` (a stats line every 5 s). adb is at
  `D:\Unity\6000.3.9f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe`.
  Drive the app with `adb shell input keyevent 20/22/66/4` (down/right/enter/back), leaving ~1.5 s between keys.
- **Cloud sessions (claude.ai/code, since 2026-10-10):** a Linux container with a fresh clone, none of the laptop
  (no D:, Unity, Blender, MuMu, camera). `.claude/hooks/session-start.sh` (cloud only) installs .NET 8 and the venv
  (`motion-service-py/.venv/bin/python`, + Pillow), builds the logic tests and runs the compile check. Runs here:
  everything in `tools/` except Blender, `fake_sender.py` (to a UDP listener), **`dotnet test tools/logic-tests`**: the
  game rules that need no engine (`BowlingScore`, `SwordMoves`, `FlyRun`, `PunchDetector`, `HandInterpolator`,
  `HandPresence`, `OneEuroFilter`, `Body`, `Joint`) and their EditMode tests (40 tests; `PaintCanvasTests` need the
  GPU), compiled as C# 9 against `tools/logic-tests/UnityShim` (Vector2/3, Mathf, Time with Unity's exact formulas),
  and **`python3 tools/compile-check/check.py`**: all the game C# (MonoBehaviours, PlayMode tests) against Unity's
  reference assemblies. Also here: **`cd server && npm run test:all`** (Worker + CLI) and
  **`python3 tools/cloud-java-tests/run.py`** (box cloud client). Put engine-free rules in their own class (like `FlyRun`, `SwordMoves`) so they can be tested
  here; adding a file to the logic tests: link it in the csproj, a missing UnityEngine member goes into the shim with
  Unity's behaviour. Not checkable here: the editor scripts (scene builder, BoxBuild), shaders, scenes, how it looks and
  feels, and the Java: say so, the user checks on the laptop. Re-rendered OGGs
  differ by a few bytes (another encoder build) but sound the same: only commit audio that really changed. The
  network blocks `dl.google.com` (Android Maven) and Unity downloads. Work goes to a `claude/...` branch; the user
  pulls, checks in Unity and merges.

## Gotchas (learned the hard way)
- **Cloud secrets that can never be lost**: the Android release keystore and the release key file (boxes can't be
  updated without them), and `CODE_PEPPER` (every card's hash depends on it). Back them up offline (docs/cloud.md).
- In a Bash tool, `pkill -f "wrangler dev"` kills the tool's own shell (the pattern matches its command line): find the
  PIDs with `ps` instead. Cloudflare's docs sites are blocked in the cloud container (WebSearch summaries work).
  Maven Central rate-limits bursts (HTTP 429 with a tiny body); `tools/cloud-java-tests/run.py` retries.
- **unityMCP pins itself to the user's other project "apa-roll" after restarts.** First call
  `set_active_instance "MoveItGames@36707906"`. If calls say "no instances", just retry.
- **Any new unityMCP connection during Play mode triggers a recompile** that breaks game state (domain reload
  disabled in Enter Play Mode settings). For in-game checks: write capture times to
  `unity/MoveItGames/Temp/moveit_capture.txt` (DevCapture → `unity/captures/*.png`), press Play once, make **no MCP
  calls**, wait for `[MotionSystem] Listening` in `%LOCALAPPDATA%\Unity\Editor\Editor.log` **before** starting
  fake_sender, then read `[Gesture]` / `[Runner]` / `[Slash]` / `[Bowling]` lines from the log. Delete the marker after.
- **Long menu items (APK build) time out on MCP but keep running**: follow `[BoxBuild]` lines in Editor.log. If the
  editor seems stuck, enumerate the Unity process's windows; a modal dialog may be blocking. Editor.log keeps old
  `[BoxBuild] APK built` lines (and the MCP retries can run the menu item again): judge by the APK file's timestamp.
- **Don't poll unityMCP while PlayMode tests run.** The bridge can log an error mid-run and Unity fails the running
  test for it. Start the run, then wait for `%USERPROFILE%\AppData\LocalLow\Tundu\Tundu\TestResults.xml` to change
  (the folder follows the product name, it was `MoveIt\MoveIt` before the rebrand). Copy the returned job id exactly.
- `HomeScreenTests` sets the Input System's editor focus rule to "all input goes to the game" and its background
  behaviour to "ignore focus" during the test: by default keys only count while the Game view has focus, and losing
  focus resets the keyboard. Right after a test whose fake_sender.py process had just exited, that swallowed the first
  key (failed 3 runs in a row on 2026-10-08 until fixed).
- `IdleFidgeting_TriggersNothing` saw one false bowling throw once (2026-10-08) while the laptop had 0.3 GB free RAM
  and was paging; it passed on re-run and in every other run. If it repeats without memory pressure, investigate
  `UnderhandThrowDetector` with bursty packets.
- Motion speeds are measured over ≥ 12 ms of camera time (`Body.MinSpeedInterval`): bursts of packets 1 ms apart
  (a stalled sender on a paging laptop) otherwise turned camera noise into fake fast swings (a false bowling throw).
- **Scenes store every serialized tuning value.** After changing a default in `MotionSystem`/`BodyGestures`/game
  scripts, run *MoveIt → Build All Scenes*, or the old value stays in the scenes.
- **After editing Java in the androidlib, call refresh_unity before building**, or Unity compiles the old copy.
  The library needs `buildToolsVersion "36.0.0"`. Use `handleLifecycleEvent`, not `setCurrentState`.
  **Don't switch MediaPipe back to LIVE_STREAM**: it stalled on MuMu.
- **The laptop is short on RAM (16 GB, often < 1 GB free with IntelliJ + MuMu + Unity) and C: is nearly full.**
  Keep everything large on D:. Gradle's cache is at `D:\.gradle`. Suggest closing IntelliJ before builds, and moving
  the Windows page file to D: (the user must do that).
- Unity re-added Ads/Analytics/Purchasing from its template once; they were removed with the Mobile Dependency
  Resolver. **Keep them out.**
- IMGUI colors look washed out on Android (the gray background should be dark). This is fixed by the UI Toolkit
  rewrite in P3; don't hack around it.
- Albanian text: the user is a native speaker and should review new strings.
- **Blender + Unity + IntelliJ don't fit in 16 GB together.** On 2026-10-08 the Unity editor vanished mid-test
  (no crash report, 0.7 GB free): close Blender or IntelliJ before long Unity runs.
- Blender scripting: `Image.pack()` reverted a generated image's pixels (load the palette from its PNG instead);
  bmesh refuses a second face on the same vertices (double-sided leaves need their own vertices); in Unity
  `GetComponent<T>() ?? Add…` doesn't work (fake null). FBX export: `bake_space_transform=True`, forward `-Z`,
  up `Y`, model facing -Y in Blender → faces +Z in Unity at scale 1 (verified with the dino).
