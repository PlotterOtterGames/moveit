# Tundu (formerly "MoveIt"): handoff for the next session

Read this first. It is the state of the project as of **2026-10-22**, written so a new session can continue
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
  frame, body tracked, Fruta (since replaced by Vishu!) playable by moving.
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
  → rendered by our own numpy synth (`tools/music/`) → OGG in `Assets/Resources/Music` (menu, runner, dress, runway,
  bowling, jingle_win, jingle_gameover) + 24 SFX in `Assets/Resources/Sfx`. `GameAudio` plays them by file name
  (drop-in replaceable), hooked into the menu and all three games. Vrapo! now looks like the famous runners:
  `Tundu/Toon` shader with a curved world (`_TunduCurve`), gradient sky (`Tundu/Sky`), clouds, pterodactyls,
  volcano smoke, dust, egg sparkles with "+10" pop-ups, crash and dizzy stars, speed lines, blob shadow, squash &
  stretch, speed FOV and crash shake (`RunnerFx`, `Tundu/Fx`), plus post-processing (colour grading, vignette,
  quarter-res bloom). Not yet: Bowling art and visual polish, HUD restyle (that's P3), checking bloom and
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
  Brush cursor = `HandInterpolator` (~1 frame behind, switch state carried per sample): overshoot at stops 8 px
  instead of 55–68 (see "Hand cursors" and "Hand tracking round 3" below for the later fixes). `IdleFidgeting_TriggersNothing` fails
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
- **Hand tracking round 3 (2026-10-19, user: "much better in the dress up game than the drawing game; resolution and
  skeleton look better in main.py --preview than in game; reduce the stutter, search how production does it").**
  Findings (`cursor_sim.py report_2026_10_19`, which now draws each cursor where its game draws it, models the
  laptop's real timing from `sessions/punches.jsonl` and MediaPipe's own filter): (a) Pikturo's paper mode maps the
  hand zone to the whole screen, 600 x 415 px per torso vs Vishu!'s mirror 327: the same camera noise is 1.8x / 1.3x
  bigger, so the same cursor filter jitters 2x (2.2 vs 1.2 px/frame at rest); (b) Pikturo's brush smoothed the
  low-lag track again, ~130 ms lag vs Vishu!'s ~90; (c) `main.py --preview` processed only every 2nd frame of the
  laptop's 60 fps camera: `cv2.imshow` + `waitKey(1)` take ~10 ms on Windows (median 11.5 ms measured), the loop
  ~33 ms (recorded gaps 32 / 48 ms); (d) MediaPipe Tasks smooths landmarks itself in VIDEO mode with one pose (One
  Euro, min cutoff 0.05, beta 80, pose_landmarks_detector_graph.cc), with its dt from the timestamps we pass: those
  were the inference start times; (e) the in-game picture is a 640 px JPEG (`CameraFeed.RequestWidth`, one UDP
  datagram, quality stepped down to fit), then cropped to the player (~a third of the picture height for Vishu!) and
  upscaled ~5x; the preview window shows 1280x720 with the skeleton drawn on the very frame it came from (no
  filter, no lag). Changes: the preview draws on its own thread (`PreviewWindow`; the loop can now follow a 60 fps
  camera: sim lag -16..-27 ms for every cursor), MediaPipe gets the capture time on a monotonic clock (PC
  `FrameGrabber`, Android the sensor timestamp; no measurable effect in the sim, but correct), the stats line and
  preview show poses/s next to camera fps. Pikturo's brush: raw samples, `HandInterpolator.FitWindow` 0.25 s (a
  least-squares parabola through the last quarter second, drawn 1 camera frame back, never extrapolated), One Euro
  2 / 4 / 2, then `CursorRope` 6 px (a drawing programs' string stabilizer) on the brush and on the free hand's
  cursor: sim lag 130 -> 110 ms at 30 poses/s. **The user prefers this one** (see round 4).
  `BodyHands.Hand.SamplePosition` is now the raw sample (velocity stays on the low-lag track). The PC service also
  sends the pose model's hand points (`hk`, protocol v1.1, games ignore them). **Not yet**: the APK on MuMu (the Java
  compiles), the in-game picture's resolution (proposed: stream the crop the game shows at the camera's own
  resolution; on the box the camera runs at 640x480).
- **Hand tracking round 4 (2026-10-20): the user's painting recording** (`motion-service-py/sessions/paint.jsonl`,
  gitignored like all recordings: 2.7 min sitting at the laptop, hips out of the picture, evening). Replayed with
  `cursor_sim.py --recording <file>`, which now scores each cursor against the path the samples make when smoothed
  both ways in time (`reference_path`, `score_on_recording`: stutter = excess acceleration px/frame^2, jolts,
  wobble = distance px, lag). Findings: (a) **the webcam ran at 20 fps the whole session** (60 in daylight on
  10-15): its auto exposure lengthens the frames in a dim room; fixed exposures (OpenCV DirectShow 0.25 + -4..-7)
  gave a black picture at the same 20 fps; more light is the fix on the laptop (the box's CameraX code forces >= 30).
  The poses kept up with every frame (the preview thread works). (b) The hand point's noise is 0.5-0.9 % of a torso
  length still, 2.4-3 % moving slowly, 8-15 % fast, and 3-5x more in frames with wrist visibility < 0.9 (the sim
  now models both: `SPEED_NOISE = DIM_ROOM`, `VIS_MODEL`); the old constant noise hid the brush's real problem.
  (c) On the recording the 2026-10-19 brush was quicker but jerkier than the one before (stutter 3.45 vs 2.51, jolts
  52 vs 28): a curve refitted at every sample moves the drawn point whenever a sample arrives. Visibility weights
  and cross-fading the fits didn't help; the rope only added lag and wobble. (d) The palm point (wrist + index +
  pinky, `--palm`) is no steadier than the wrist pushed along the forearm: kept the wrist. (e) A search on the
  recording (`--tune`, `report_2026_10_20`) found per-sample smoothing (`SampleFiltered` in the sim: each raw sample
  One-Euro-smoothed 1.5 / 30 as it arrives, straight lines between, per-frame 4 / 0.5 / 1) best by the score: stutter
  2.44 vs 2.51 (the brush before 10-19), 23 % closer to the path, lag 115 vs 125 ms. It went into both Pikturo hands,
  **the user tried it and said "it felt better before"**, so it was **reverted the same day** to the 10-19 version
  (fit + rope, ~103 ms lag on the recording). Lesson: the score under-weighs lag; for this user responsiveness
  matters more than a little extra smoothness; the user's feel decides. Then researched how people handle MediaPipe
  jitter (options listed to the user, nothing chosen yet). **Not yet**: a recording at TV distance in good light (60
  fps). `main.py --record <path>` is relative to the current folder (the first try landed in
  `motion-service-py/motion-service-py/sessions`).
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
  lengths = the edge, duck = low, a jump lifts it for 0.7 s).
  **Endless since 2026-10-15** (user: "enemies coming 1 by 1 are not a good mechanic; more fun to punch: he who punches
  faster wins; I want an infinite game, how to make it fun?"). Researched first: One Finger Death Punch (enemies stream
  in from both sides, each input a big response, survival mode), Iron Man 3 / Spider-Man Unlimited (endless superhero
  runners with villain encounters, suits), Dragon Ball's beam clash and Mario Party's tug of war (mash-offs), Jetpack
  Joyride / Subway Surfers (3 rolling missions, ranks, cosmetics), runner pacing (warm-up, challenge, rest blocks). The
  run = districts ("Lagjja 1, 2, 3 ...") until the hearts run out. A district: **flight** (60-ish s of street at
  16 m/s rising to 23; stars; 2-3 **drone swarms** in formations: line, vee, column, ring, diagonal; each drone punched
  = a star, the whole swarm = a bonus; a drone that gets through only bumps: combo lost, no heart) → **"Ndal! Të
  rrethuan!"** (the **rush**: groups of fighters charging in **at once from the left, the right, ahead and above**,
  streamed 0.35 s apart, each to its own spot around the hero; any punch hits the most urgent one in reach; one that
  arrives winds up 1.0 s (shorter later) and strikes; the next group comes when the last is nearly down) → **"Grusht më
  grusht!"** (the **clash**: the district's champion, big, red boxing gloves, face to face; a tug-of-war bar: every
  punch of either side pushes it 0.14, **the faster puncher wins**; the champion punches 1.7/s rising 0.25 per district
  to 4.2, in flurries now and then, and tires after 9 s; the bar at the champion = knockout in slow motion; at the hero =
  a heart, the bar back to the middle). Every **third district the boss**: punch its fireballs back (3 + d/3), then the
  final clash (+0.4/s). Each district saved: +1 heart (max 5), faster, bigger rushes, and new robots (armoured from
  district 2, throwers 3, quick yellow darts 4; a card shows each new kind once), and the **time of day** moves on
  (`FlyTheme`: morning, golden, sunset, violet evening, dawn, around again: sky, sun, fog, ambient, city glass).
  `DistrictSettings.For(d)` holds every number (all ramps capped). **Between runs** (`FlyProgress`, saved in
  PlayerPrefs `fly.progress`, engine-free, tested): stars from every run add up and unlock 8 outfits
  (`Hero.Outfits`, suit + cape colours, the last a rainbow cape; thresholds 0/40/100/180/300/450/650/900; the newest is
  worn at once; ← → on the waiting/results screen choose), 3 missions to do in one run (11 kinds: smash N robots/drones,
  win clashes, reach district N, collect stars, combo, win a clash in N s, beat the boss, a whole swarm, punch back
  sparks, a rush without losing a heart; done = bonus stars at once and a card, replaced by a harder one after the run),
  best score + district, "REKORD I RI!" when the run passes the best. No shop, nothing sold. Simulated players
  (`FlyRunTests.Balance`, 2026-10-15): perfect (0.15 s, 4 tries/s, mashes 5/s) district ~21 at the 15-minute cap, good
  (4/s) ~8 in 4.4 min, child (0.45 s, 2.5 tries/s, 65 % land, mashes 3/s) ~5 in 2.8 min, struggling (2.2/s) ~2.6 in
  1.5 min, standing still or never punching loses in district 1. The scripted brawler (`fake_sender --script brawl`,
  jabs ~3.3/s) reaches district 5 with no heart lost; screenshots looked right (rush from both sides, the clash
  over the hero's shoulder with both punch rates under the bar, the themes). Not yet: the user's review of all the new
  Albanian (`FlyStrings`: rush, clash, missions, outfits, enemy cards), a real-camera try, the APK on MuMu.
  **Punches (forgiving since 2026-10-15)**: `MoveSDK/Runtime/PunchDetector`: any quick move of a fist counts, seen as
  it gets going (Kinect games trigger mid-move and lower thresholds until every child's punch counts): the fist moving
  >= 2.2 torso lengths/s relative to its shoulder **in the picture** (world joints jitter like a quick move: with them,
  a still MediaPipe-like hand gave 1.4 false punches/s) **or** the arm straightening >= 1.8/s in 3D (jabs straight at the
  TV, depth squeezed). Not a punch: the arm bending in the picture (>= 1.5/s, or by 0.35 since the move started),
  within 0.7 s of a punch the fist heading back to its shoulder (a forearm folding after a hook), dropping the arms. One
  move, one punch: ready again when the fist slows, or every 0.4 s while it keeps moving (flailing = ~3.5 punches/s).
  Bringing the fists up to the chin and raising the arms may count (they start exactly like a punch from a hanging
  arm). `PunchDetectorTests` (2026-10-15): 503/504 punches clean, 497/504 with the MediaPipe-like camera, mostly before
  the fist arrives; steering, ducking, jumping, swaying: 0 false punches on every camera model. History: 2026-10-12 the
  user's real punches mostly didn't count (the old bent-to-straight rule saw 5/24 jabs with squeezed depth). Real
  evidence still needed: a `main.py --record` session of punches, replayed. The game logs `[Fly] Punch seen: speed,
  arm straightness` for tuning with a real camera. Without world joints a jab straight at the TV can't be seen.
  Code: `Games/Fly` (`FlyRun` rules without a scene, `FlyRoute` streets and turns, `FlyGame`, `Hero`, `RobotView`
  (robots and the boss), `FlyWorld` + `CityBuilder` (the city, see below), `FlyArt`, `FlyStrings`). Keyboard: arrows fly, space punches. Music
  `fly.ogg` ("Sky Patrol") + 12 `fly_*` sounds. **Checked on the laptop 2026-10-12**: scene built, played with
  `fake_sender --script fly` + DevCapture, PlayMode test passes, runs in the APK on MuMu (no errors). Polished then:
  clouds keep out of a 10 m corridor around the flight path (they filled the screen passing the camera), the camera
  sits higher (`FlyGame.CameraHeight/CameraBack/LookHeight/LookAhead`, shared with the scene builder) and the hero's
  model is drawn 0.25 m below its rule point (`Hero.ModelDrop`) so a robot hovering right in front shows above the
  hero's head instead of behind it.
  **Turns and a textured city (2026-10-12, user: "on right go 90 degrees to the road on the right, on left to the road on
  the left; a lot more polished (textures, shading) without breaking performance").** `FlyRoute` (no engine, tested in
  `FlyRouteTests`): a street grid, a crossroads every 80 m, some segments missing (T-junctions, corners; every
  crossroads keeps >= 2 streets). Reaching a crossroads with the hero beyond 0.45 of its window to a side turns 90°
  that way on a 7 m arc; else straight on; where straight on is impossible it turns toward the hero's side (never a
  crash). Arrows beside the hero show the streets that turn off from 46 m before a crossroads, gold = the one it will
  take; "Kthehu majtas ose djathtas!" where the street ends. The city turns around the hero, not the hero: the rules
  (`FlyRun`) stay in the flight frame (`FlyRun.Turned()` drops the stars ahead, robots follow), and the curved world and
  the light stay fixed to the view. `FlyWorld` keeps 5 x 5 blocks around the hero (recycled meshes, 1 block built per
  frame, ~1 ms in the editor). `CityBuilder` builds a block as one mesh with one material (`Tundu/City`, a 16-tile
  texture array from `tools/fly/city_textures.py`, imported by `Editor/FlyArtImport.cs`): houses with apartment /
  classic / balcony / brick / office facades (windows snapped to whole bays), shop and cafe ground floors with awnings,
  cornices, gable roofs, water towers, roof gardens; roads with lane lines, zebra crossings, kerbs, lamps, trees;
  parks with ponds and fountains; tall "town hall" buildings closing missing streets. Glass and water reflect the sky
  (fresnel + sun glint). Hero and robots: merged meshes (`PartMesh`, ~5 draw calls each), `Tundu/Toon` options
  (vertex colours with gloss in alpha, highlight, emission, flash; all off by default for the other games): glossy
  metal, eyes and thrusters glowing through the bloom, a flowing two-sided cape. Effects are particles (sparks,
  glows, scrap and confetti cubes, speed lines), blob shadows on the street (bent like the curved world). Editor, 13
  PlayMode tests running: 59.9 fps, ~105 batches, 50-66k triangles drawn, 120-140k city vertices loaded. A `[Fly] perf`
  log line every 10 s (fps, worst frame, garbage collections, city size; in the editor batches and triangles). Not yet:
  **the APK on MuMu and the box's frame rate** (2026-10-12: no memory for Gradle + MuMu while two VMs ran); the
  Albanian in `FlyStrings` (incl. the new `TurnHowTo`, `TurnHint`); a try with the real camera. The editor shows one
  ~35 ms frame per garbage collection (every ~10 s; incremental GC is on, the player heap is far smaller): check on MuMu.
- **Vishu! (replaced Fruta on 2026-10-16, user: "replace the fruit game with a new game tailored towards girls: dress
  up that follows and sticks to your skeleton; the default view is the camera view of yourself; search the top games of
  this kind first and match them; fully develop it with proper assets and graphics").** Fruta (Slash) is deleted: scene,
  scripts, `slash` song and its sounds (in git history up to 0a6b124). Researched first: Barbie's Kinect "Dream Closet"
  mirror (browse with gestures, outfits cut into parts and scaled onto the skeleton, a photo), the Disney/Zero10 princess
  AR mirror at Macy's (animated magic that follows the body), Dress to Impress (a theme, a timer, the runway with poses,
  1-5 star ratings), Style Savvy (fashion shows judged by a theme), Love Nikki (themes weighted by attributes), Barbie
  Fashion Closet / Magical Fashion (mix and match, princess/fairy/mermaid transformations, then a photo). The game:
  **the mirror** (the camera picture, mirrored, framed on the player like Pikturo's camera mode); 7 categories on the
  left (tops, skirts, hair, headwear, face, wings/capes, in hand), the category's 6 items on the right (+ "nothing"), the
  top row Magic (6 effects: sparkles, hearts, butterflies, bubbles, snow, stars), Surprise (a whole themed look), Show,
  Photo (3-2-1, PNG with the outfit, no buttons), Clear. Bubbles are pressed by holding a hand on them (0.6 s, 1 s for
  show/photo/clear) or with the remote (arrows move to the nearest bubble, OK presses). **Choosing the worn item again
  changes its colour** (each item has 4-9 colourways; dots under it show them). **The fashion show**: a random theme
  (princess, fairy, mermaid, superhero, summer, winter, party) and 60 s to dress ("Gati!" ends early), then the runway
  (curtains, spotlight, music "Runway Glow"): 3 poses from 6 (star, hands on hips, heart above the head, wave, superhero,
  airplane), each counts when held 0.5 s (`PoseCheck`, forgiving, 7 s each, a photo at the first one), then 3 judges
  (Mimi the cat: the theme; Lumi the bunny: colours + a complete look; Dino from Vrapo!: poses + fun) give 2-5 stars each
  with a remark (`ShowJudge`), the stars add up (`DressProgress`, PlayerPrefs `dress.progress`) and unlock 4 **magic
  fabrics** (rainbow 8, glitter 20, gold 36, galaxy 56 stars; shader effects that join every item's colour cycle). The
  look is remembered (`dress.look`). Nobody in front of the camera (or no camera picture yet): a **paper doll** wears the
  outfit (breathing, swaying).
  **How the clothes stick** (`OutfitRig`, engine-free, tested): every piece of art is drawn in one body template (torso
  length T = 1, shoulders +-0.40 at y 0, hips +-0.24 at y 1, eyes +-0.064 at y -0.47; see tools/dress/draw.py) and mapped
  onto the joints by its rig: tops bilinear between the shoulder and hip rows; skirts from the top's waist row (0.8), then
  rigid along the thighs (knees together don't pinch a ball gown) with a swinging hem; the mermaid tail through waist,
  knees and ankles; sleeves along shoulder-elbow-wrist; hats/hair/glasses on a head frame (eye midpoint, roll, a head
  width that grows at once and shrinks slowly, so turning the head doesn't shrink a crown); long hair hangs below a line
  and sways; wings flap around a pivot (faster when the arms move); capes swing; hand items point up along the forearm,
  stand upright, hang and swing (bag, a Verlet pendulum) or float on a string (balloon). Joints the camera can't see are
  estimated from the body's proportions and faded in/out. **Sync**: the clothes are fitted to the joints at the camera
  picture's own capture time (`PoseTimeline` interpolates the raw samples, then a light body-relative One Euro filter):
  the picture and the pose packets travel separately. **Layering without segmentation**: soft body shapes drawn from
  the skeleton into a small mask texture (`Tundu/BodyMask`, R = body, G = arms) hide wings, capes and long hair behind
  the body and tops/skirts behind the arms. The whole outfit is **one mesh, one draw call** (`OutfitRenderer`,
  `Tundu/Outfit`: tint from a grey + tint-mask encoding, the 4 fabrics, a white "poof" flash). Particles in `DressFx`
  (poofs, glints on gems and sequins, the wand's trail, the effects, confetti). The bubbles are anchored to the player's
  shoulders in the current framing (fixed offsets in torso lengths), held still while a hand presses one: anchoring them
  to the *intended* framing missed by ~60 px because the framing only re-centres past a tolerance.
  **Art as code**: `tools/dress/outfits.py` (42 items + the doll, 57 pieces, ~6 min) -> `Art/outfit_atlas.png` (4096,
  ASTC 6x6 on Android) + `outfit_mask.png` (half size, EAC R) + `outfit_atlas.json`; `tools/dress/ui_art.py` -> icons,
  pose figures, theme icons, the 3 judges, curtains, stars, particle sheet `dress_fx.png`, item thumbnails
  (`outfit_thumbs*.png`, coloured by the same shader in the UI). `--preview` writes `tools/dress/out/looks.png` (8 looks
  on the doll) and `items.png`; `--only <item prefixes>` redraws a few items for a quick look (no atlas). Music
  "Sparkle Boutique" (`dress.ogg`) and "Runway Glow" (`runway.ogg`), 10 `dress_*` sounds. Code `Games/Dress`:
  `DressCatalog`, `DressLook` (+ `DressProgress`), `OutfitRig` (+ `OutfitPose`, `OutfitMotion`), `PoseTimeline`,
  `ShowRules` (`PoseCheck`, `ShowJudge`), `OutfitArt`, `OutfitRenderer`, `DressFx`, `DressGame`, `DressStrings`.
  Keyboard: 1-7 categories, arrows + Enter, F photo, R surprise, E magic, S show, X clear, H the other hand holds things.
  Checked 2026-10-16 in the editor (PlayMode, `fake_sender --script dress` / `runway`, screenshots `dress_*.png`): the
  scripted player dresses itself with both hands (ball gown, top, hair, crown, wings, a second colour, magic), the
  clothes follow when it sways, steps and raises its arms, the show runs from the theme to the judges (14 stars, rainbow
  unlocked), every item drawn on the doll. **Not yet**: the user's review of the look and of all the new Albanian
  (`DressStrings`, item names in `DressCatalog`: e.g. "Vishu!", "Sfilatë", "Gati!", "Duart në mes!", the judges'
  remarks); a try with the real camera (head size and the face hole, the hip/knee estimates, pose thresholds come from
  the scripted player and simulated poses only); the APK on MuMu and the box (memory with the 4096 atlas, the frame
  rate, `Graphics.DrawTexture` thumbnails and IMGUI colours on Android); segmentation (MediaPipe's person mask) would
  allow real backgrounds and better layering; two players dressed at once.
  **Polish round (2026-10-19, user: "visual glitches, not good enough art direction; when rotating the clothes get
  glitched since it is 2d").** *Turning* (`Turn` in `OutfitRig.cs`): the body's turn (`OutfitPose.Yaw`) comes from the
  world joints' shoulders and hips (`OutfitPose.WorldYaw`), else from how narrow the shoulders look against their
  facing width plus which way the nose went (narrow shoulders with the nose in the middle = raised arms; under ~25
  degrees reads as 0). The head's turn: the nose against the ears, front or back from the body's turn; its roll stays
  within 90 degrees of the shoulders (from behind the eye line points the other way: crowns were upside down). Rigs
  keep the facing widths (`FrontShoulderHalf`/`FrontHipHalf`), and pieces wrap: tops, skirts, the tail and hats round
  an elliptical section as wide as the art at each height (the `profile` outfits.py writes into the manifest), as two
  sheets, the front and the back (the art mirrored, a little darker), each shown only where it faces the camera (the
  rest collapses onto the silhouette, never a fold). Side on, the sides sit 10 % inside the art's edge (its rim was
  stretched into a gap at 90 degrees), and those pieces' vertex columns follow the section, exactly on the sides and on
  each sheet's horizon (a uniform grid made a sawtooth). Glasses and the hair over the face lie on a sphere (front
  only); wings, capes and the hair behind the head on planes behind the axis, drawn over the clothes when facing away.
  `fake_sender --script turn` (45, 90, -60 degrees, a full spin) -> `dress_turn_*.png`. *Art*: smaller round puff
  sleeves, the fairy top a satin bodice in a cup of soft petals, the petal skirt fewer bigger petals over tulle, hair
  darker at the roots, the doll a cute mannequin (lilac leotard, socks, pink shoes) with her own bob (item `dollhair`,
  shown while no hair is chosen). *Sitting in the picture*: a soft shadow pass (the same mesh, `Tundu/Outfit`
  `_Shadow`: blurred alpha nudged down) and the room's light (a 16x9 copy of the camera picture 4x a second, its last
  mip = the average colour, `_TunduAmbient`: the clothes take a little of its hue and brightness). *Interface*: a
  dressing-room vanity mirror (vignette, a thin glowing frame, warm twinkling bulbs), frosted dark panels with a light
  rim, bubble shadows, a gold glow on the worn item, the category's name on a pill in its colour, label pills, a
  "Vishu!" badge, a toast that pops in. **Not yet**: turning with the real camera (MediaPipe's depth side on, and
  whether it swaps left and right when the back faces the camera; without world joints only <= 90 degrees is
  estimated); the user's look at all of it.
- **Tenis! (7th game, 2026-10-20/21, user: "a tennis game, like the wii games tennis one"; then 2026-10-21: "only 1
  player from each side, and allow the player to move around their side more" → singles, the body moves the player,
  with an assist; the user chose it from three options).** Wii Sports Tennis's rules for a camera: **singles against a
  computer player** (sky-blue shirt, seen from behind). **The body moves the player** (`TennisMatch.BodyToCourt`, fed
  by `TennisGame.Steer`): sideways from where they stood when the match started (`BodyGestures.LateralOffset`,
  re-centred at each match's intro; 0.9 torso lengths = the singles sideline), toward / away from the TV from how big the
  torso looks against then (smoothed 0.25 s, 3 % dead zone; 20 % bigger = at the net, ~10 % smaller = behind the
  baseline). **The assist**: when a ball comes the player glides from where the body puts it to the spot beside the
  ball, up to 1.6 m sideways / 2.5 m forward-back (an ellipse); farther, the ball goes by unless the body steps toward
  it (an on-time swing then says "Afrohu më shumë te topi!"). A **ring on the court** marks that spot: green = in
  reach, coral = move ("Lëviz te rrethi në fushë!"). The touch follows the body until 0.45 s before the ball arrives
  (`LockBefore`): a ball that would bounce behind the player is volleyed as it passes them (or, over their head, up to
  2 m further back where it comes down: a smash), else met after the bounce as it reaches their depth or comes down to
  waist height. A server stands still at the serve spot. **Any quick move of either hand is a racket swing**
  (`SwingDetector`, forgiving like `PunchDetector`: the wrist >= 3 torso lengths/s relative to the shoulders in the
  picture, on the low-lag track; one move, one swing; one racket: both hands share a 0.35 s cooldown; speed and
  direction are the swing's peak a frame or two after it starts (read at the start, 30 fps gave the same backhand 3 or
  8.6), its time the start + `ContactLead` 0.05 s). **The swing's timing aims the ball** (Wii): from 0.4 s early to 0.25 s
  late counts, early pulls it across (to the player's left on a forehand), late the other way, within 0.07 s = perfect
  (15 % faster, a sparkle); swing speed = power (3-10 torso lengths/s), up = topspin (slow and up = lob), down = slice.
  The player's shots go in, except that from the 3rd opponent a swing at the very edge of the window goes wide
  (`TennisLevel.WideFrom`: never for 1-2, the last 10 % for 3-4, the last 25 % from 5; without it a player who moves
  well never lost a point). **Camera lag taken out**: each swing goes to the rules with the moment the camera saw it
  (`TennisGame.ReadBody`: match time - (Unix now - capture time), capped at 0.35 s); an early swing's hit waits for the
  ball, a late one leaves from where the ball was when the racket met it. **Serve**: the ball is tossed by itself (or
  by a first swing, like Wii's flick up); any swing while it's up serves, within 0.12 s of the top = power serve; serves
  always go in, a missed toss is caught and tossed again (no faults; the game waits for the serve). The singles lines
  count (the doubles alleys are drawn but out). Real scoring (`TennisScore`: 15/30/40, deuce, advantage, best of 3
  games, the serve alternating sides and players), the umpire's calls on screen ("15 - zero", "Barazim!", "Avantazh:
  Ti", "Loja: Ti!", "Pikë ndeshjeje!"), "Jashtë!" / "Rrjetë!" at the ball, "Herët!" / "Përsosur!" / "Vonë!" after the
  player's hits. **A run is a ladder** (`TennisLevel.For(n)`: 10 named players (Ana, Beni, Era, ...), then around again,
  every number capped): faster balls, fewer mistakes, more corners away from the player, short balls (`ShortChance`, so
  they step toward the TV), lobs, power serves, hard balls bothering them less (`Pressure`); the computer runs to every
  ball itself and passes or lobs a player at the net. Best run (matches won in a row) saved (`tennis.best`). A **ring
  closes on the ball** at the moment to swing (and on the toss). Balance from simulated players who also move their
  body (`TennisMatchTests.Balance`, 10 matches each, wins at levels 1 / 3 / 6 / 9): perfect 10 / 10 / 10 / 10; good
  (timing -0.05 +- 0.12 s, moves after 0.35 s) 10 / 10 / 10 / 10 (points 80-13 at L6, 82-21 at L9); child (-0.1 +- 0.2 s,
  15 % no swing, slow, moves late and 1 m off) 10 / 5 / 0 / 0; struggling 2 / 0 / 0 / 0; good timing but never moves
  10 / 6 / 0 / 0; standing still (serves only) 0 everywhere; 1.5-4 minutes a match. Look: `TennisStadium` builds
  everything in code (blue hard court with 8 cm lines, the net (a see-through cell texture made in code), ad boards, the
  umpire on his chair, benches, stands on three sides with ~470 low-poly spectators in sections that jump when the
  player wins a point, a striped awning, flags, trees, hills: ~15 draw calls); `TennisPlayerView`: Mii-like toy players
  (big head with a face, 5 hairstyles, headband, racket) in 6 merged meshes, animated in code (ready bounce, running
  legs and lean, forehand / backhand / overhead timed to the contact moment the rules give, a dive, cheer, sulk).
  **The racket (reworked 2026-10-21, user: "it looks sideways and a bit glitchy")**: it was a plank 54 cm wide across a
  straight arm, seen edge-on from behind, and every swing started from a fixed pose (a quick one skipped its backswing:
  the racket jumped up to 0.9 m in a frame). Now it's an oval racket on its own object in the hand, posed by three
  directions (`TennisPlayerView.Pose`: arm, handle, the way the strings face: toward the net when ready and at the hit,
  so the camera sees the strings); **the racket goes back by itself as a ball comes** (`CourtPlayer.Preparing`, 0.6 s
  before the contact, and behind the head during the player's own toss), so a swing only draws the 0.12 s hit and the
  racket meets the ball right as the swing reaches the game; a swing starts from the pose the arm is in, the arm turns at
  most 600 deg/s outside the hit, a swing with too little time gets its backswing (its hit drawn a little after the
  rules'), the hit and follow-through are evenly paced, the body's height and lean ease too (a cheer's hop switched off
  at once made the player pop). `TennisGameTests.Racket_MovesWithoutJumps_StringsFaceTheNet` drives a player through
  every case at 60 fps: the racket's head moves at most 0.26 m a frame (the fast part of a backhand); close-ups
  `racket_*.png`. Camera high behind the baseline (0, 5.4, -22), FOV 46, following the player half
  their x and a third of their depth. The skeleton mirror is hidden while playing (it covered the player stepping
  right), shown while waiting, on the results and while paused. Music "Center Court" (`tennis.ogg`) + 9 `tennis_*`
  sounds. Keyboard: arrows move, space / Enter / OK swings (hold W = lob, S = slice), H or the remote's ↑ (with the
  camera) = the racket hand. Checked 2026-10-21 in the editor: `fake_sender --script tennis` (practice: serves and
  returns feed after feed) and `--script tennis_move` (steps to both sidelines, to the net and behind the baseline);
  screenshots looked right. **Not yet**: the user's review (look, feel, all the Albanian in `TennisStrings` and the
  players' names in `TennisLevel`), a try with the real camera (the depth from the torso's size is the shakiest part: a
  forward lean or a bend makes the torso look smaller; the swing speed, `ContactLead` and the timing windows come from
  simulations; a fast backswing may count as an early swing), room size (a step of ~0.45 m reaches a sideline, ~0.5 m
  toward the TV the net), the music by ear, the APK on MuMu and the box (frame rate with ~65k crowd triangles +
  shadows), two players (MediaPipe runs with one pose).
- **Sporte! (game 8: the 100 metres, football, beach volleyball; 2026-10-21/22).** History: on 2026-10-21 the user asked
  for copies of "the 2 most popular Xbox Kinect games" and got Aventura! (5 Kinect Adventures activities) and Sporte! (8
  Kinect Sports events), all built from code primitives. Their verdict on 2026-10-22: **"very low quality ... they look
  like AI games and not games ready to be polished"**; keep only football, running and volleyball, remove the rest, then
  polish those three. Aventura!, boxing, hurdles, long jump, javelin and discus are gone (code, tests, scenes, music,
  sounds, fake_sender scripts; a copy in `D:\Backups\moveit-removed-2026-10-22`). **What made them look AI-made**:
  stick-figure capsule people, pale hazy light, crowds of random-coloured cubes, untextured flat venues, grey debug-box
  HUDs, static cameras. **The rebuild** (the lesson: real art direction and real assets, even made as code):
  - **Athletes** (`art/sports/athlete.py`, `athlete_anim.py`, `build.py`; Blender 5.1 **headless**:
    `blender --background --factory-startup --python art/sports/build.py -- --anim --ao --export`, ~10 s): one stylised
    toy athlete (~4.8 heads, Wii Sports Resort-like) built from smooth lofted pieces (each garment its own shell with a
    hem), bean eyes, 8 hairstyles, a 25-bone rig, **33 clips** keyed as code (Idle, Ready, Run, Sprint, Walk, Shuffle,
    Cheer, FistPump, Sad, HandsOnHead, Clap, Wave, HighFive, Airplane, Kick/Pass L/R, KeeperReady, Dive L/R, JumpCatch,
    Marks, SetPosition, Dip, Bump, Set, Spike, Serve, Dig, Block; poses solved with IK so planted feet stay planted).
    Colours are not in the model: uv1.x = region (skin, shirt, shorts, hair, eyes ...), uv1.y = baked AO; `Tundu/Athlete`
    colours regions per character (`AthleteLook`: `Wear.Football/Track/Beach`, team colours, gloves, headband). Faces are
    bones (Eyes / EyesShut / Mouth / MouthOpen / MouthSad scaled to show or hide), so clips carry expressions and the game
    blinks. Generated `Scripts/Athletes/AthleteData.cs` (regions, pieces, clip lengths, loops, **contact times**, ground
    speeds). `Athlete` (runtime): merges the chosen pieces into one skinned mesh (one draw call, cached per piece set),
    Playables cross-fades, `PlayThen`, head look-at, two-bone IK hands (`Reach`: the keeper's gloves follow the player's
    hands), `ShowFace`, `Flash`, dithered `Fade`. Previews: `art/sports/previews/` (gitignored like the runner's).
  - **Venues** (`art/sports/venues.py`, `venue_kit.py`, `venues_build.py -- football track beach [--preview]`): geometry
    written in Unity coordinates, vertex colours stored linear, bevelled boxes: a full football stadium (pitch with mown
    stripes and lines, goals, LED boards, four stands with seats, roofs, floodlights, big screen, dugouts), an athletics
    stadium (oval, the 100 m straight, blocks, finish gantry, photo-finish camera, roofed main stand, end stand + screen,
    grass bank with trees), the beach on the Albanian coast (court, net posts, bleachers, palms, umbrellas, lifeguard tower,
    beach bar, sea with foam, mountains). Each writes `Art/Venues/<Venue>.fbx` + `<Venue>_crowd.txt` (seats). `Venue`
    loads one (materials per object name: Ground/Lines/Structure/Seats/Boards/Screen/Water), `Fans` = the crowd: a quad a
    seat drawn by `Tundu/Crowd` from `crowd_atlas.png` (16 people rendered from the athlete model, clapping and cheering;
    `build.py --crowd`), bobbing, jumping when `Cheer()`; `GoalNet` (net mesh that bulges); `SportsBalls` (football,
    volleyball drawn in code, trails); `Daylight` presets (sky gradient + **clouds now in `Tundu/Sky`**, trilight ambient,
    sun, fog); `ShotCamera` (eased shots and cuts). Textures as code: `tools/sports/textures.py` (grass/track/sand detail,
    `ads.png` strip with the logo, net, screen). Shaders `Tundu/Athlete`, `Tundu/Crowd`, `Tundu/Screen` (scrolling LED),
    `Tundu/Net`.
  - **HUD** (`Common/Activities/SportHud.cs`): rounded gradient panels, outlined text, a TV scoreboard, slam-in banners,
    pills, a meter; `ActivityGame`'s points/medals panel, how-to card, countdown, results and cards restyled with it.
  - **The games**: *Futboll* (`FootballMatch`: 5 rounds, attack then defence; attacks vary: a pass then a first-time shot,
    a free kick over a wall of 4 (rounds 2), a penalty (4); the outcome is known when struck (`Predicted`: goal, saved,
    wide, post, wall); a goal goes slow-motion from beside the goal; defending, the player is the keeper behind whom the
    camera films through the net; gloves follow the hands, a stretch toward the shot dives). *100 metra* (`TrackRace`:
    line-up with names, blocks, set, gun, side tracking camera widening with speed, head-on telephoto at the line, a lean
    in the last 8 m (`DipDetector`: the head dropping toward the hips) gains 0.06 s, reaction time, live order, a record
    line painted across the lane at the best's pace, photo finish). *Volejboll* (the player's athlete and a partner in
    yellow vs two in coral, everyone plays their clip timed to the ball's contact, the partner sets, the player spikes,
    points end in a high five and a close-up; sets now 2.45 m so the toy athletes reach them). The menu: the chosen
    sport's venue fills the screen with its athlete in front (all three venues loaded 3 km apart, a flash on the cut),
    three cards. Strings in `SportsStrings` (many new, **the user should review the Albanian**).
  Tests: EditMode `SportsRulesTests` (incl. the lean), PlayMode `SportsGameTests` (screenshots `sports_choose`,
  `sports_<id>`, `sports_football_shot/keeper`, `sports_sprint_running/finish`, `sports_volley_set/point`,
  `sports_results`). **Not yet**: the user's look at it all, a real camera (thresholds from the scripted player), the APK
  and the box's frame rate (12k crowd quads in the stadium, up to ~9 skinned athletes, real-time shadows: measure, then
  thin the crowd or shadows if needed), fonts (Fredoka/Nunito would lift the HUD a lot: downloading them needs the user's
  OK), the sprint's computer runners on the bend (the race is all on the straight), replays.
- **Placement check (2026-10-21, user: "do I need calibration and validation that the player is in the correct place;
  check how similar systems did it").** Researched: nobody makes players calibrate (OpenNI's early "psi pose" was
  dropped for automatic calibration, like ours); everyone checks the player is in the picture with room and says which
  way to move: Kinect's SDK reported per player the picture edges that cut the body (`Body.ClippedEdges`) and games
  paused with "step back / forward" (6 ft solo, 8 ft for two); Nex Playground asks 1.0-4.0 m from the console, 38-152 cm
  high, a clear 1.8 x 1.8 m, has a Tracking Assistant in Help, warns about backlight / others / mirrors and suggests tape
  on the floor; PS Camera's setup waits for the face in a frame; EyeToy blinks its light when too dark. Before, the gate
  only needed both shoulders visible: a child close to the TV calibrated, then their raised hands left the picture and
  "raise both hands" never fired, with no reason given. Now `MoveSDK/Runtime/PlacementCheck` (engine-free, 9
  `PlacementCheckTests` on a pinhole camera: box 60° / laptop 41° vertical, adult and child): each game says what it
  needs in a `PlayArea` (in torso lengths: the lowest joints that must be in the picture, room to step to each side from
  where the round starts, room above the head, how much bigger the body may look), set with `PlayerGate.Use` in its
  Start (`BodyGestures.Area` belongs to the scene that set it). Measured on the smooth track: head = nose + 0.35 T (or
  shoulders + 0.75 T without the face), the lowest pair's lower joint (one seen with visibility >= 0.3 is enough, none =
  out), sides = Side + 0.5 T, all scaled by Grow; 0.02 picture heights margin, a flagged edge clears with 0.1 T more
  (no flicker). Issues: `StepBack` (head or legs cut, or no room on either side), `MoveLeft` / `MoveRight`,
  `ComeCloser` (torso < 0.075 picture heights: a child beyond ~3.5 m with the box camera; advice only, a guess). A
  blocking issue stops calibration and the hands-up start (`BodyGestures`), gives `GateState.Placing` (pauses like
  calibrating) and the waiting / paused screens say what to do with chevrons toward the side to go; the skeleton mirror
  lights the edges like Kinect's samples. During play `BodyGestures.InPlay` checks the body itself (sides at 0.7 T from
  the torso's middle) and `PlayerGate.DrawEdgeWarning` shows a banner + chevrons after 0.25 s at a side / 0.6 s cut
  (a jump takes the head out briefly); the game keeps going, it pauses only when the player is lost, as before.
  Results screens say where to stand instead of "raise both hands to play again" when hands up can't start there
  (`PlayerGate.PlayAgainText`); the mirror shows the start check's edges while such a screen is up
  (`BodyGestures.PlacementShownFrame`), the in-play ones otherwise. Checked in the editor with `--script place`:
  no calibration while too close, hands up too close or at the side starts nothing, the run starts from the middle,
  the warning shows at the edge (no errors; screenshots looked right). Areas:
  menu hips + 0.7; Vrapo! knees + 0.8 + 0.3 above; Bowling knees + 0.8; Fluturo! knees + 0.95 + 0.3 above; Shpata!
  hips + 0.3; Tenis! knees + 1.0 + 0.1 above, grow 0.2 (at the net). Vishu! and Pikturo! set none: the camera picture
  is their own feedback, and Pikturo works sitting. At 2.2 m an adult's torso is ~0.19 of the picture height with the
  box camera; the laptop's 75° camera needs ~3 m (2.2 m = "step back"). `fake_sender --script place` plays it in
  Vrapo!. **Not yet**: the user's review of the Albanian (`CommonStrings.StepBack`, `MoveLeft`, `MoveRight`,
  `ComeCloser`, `RoomToMove`, `SeeFromHeadTo`), a try with the real camera (MediaPipe's out-of-picture joints, the
  0.35 T head, the far limit), the camera setup screen (P3), a light warning (on the box CameraX keeps 30 fps, so the
  picture's brightness would have to come from the Java side), other people in the picture (one pose only).
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
  empty, so builds trust no manifest or token yet**), a real silent update on a box, QR scanning
  with the camera, the user's decisions (which games in Game Pass, prices, card durations; **cards are sold only
  offline, in shops and in person: no payment system, user's decision 2026-10-10**), the accountant (fiscal
  receipts for cards, VAT on vouchers). `npm audit`: undici advisories in local dev tools only; upgrade wrangler ≥
  4.149 and @cloudflare/vitest-plugin ≥ 1.4.0 after 2026-10-22. **Checked on the laptop 2026-10-12**: server tests
  (49 + 14) and the Java tests (33) pass on Windows, Gradle builds the plugin into the APK, on MuMu the client starts,
  fails DNS for api/cdn.tundu.al quietly and a typed code ends in "Nuk ka internet" (`no_identity` = never enrolled);
  the Game Pass screen (16 fixed code places, ↑ ↓ follow the drawn keypad) is covered by `HomeScreenTests`.
- **Hardware sourcing (2026-10-08): `docs/sourcing.md`**: AliExpress links for the samples (checked from Albania,
  prices in lek), Alibaba batch suppliers (boxes with custom firmware/logo, OEM webcams, packaging), the kit contents,
  customs (€22 duty-free, VAT 20%), CE/AKEP, the step-by-step process, unit cost (RK3566 kit €53–82 landed).
  Retail sample prices went up: the recommended sample set is ~€255, not €150–190.
- Then **P3 production UI** (replace IMGUI with **UI Toolkit**, one USS theme from the brand; first-run setup,
  animated home, how-to-play cards, results, sounds; this also fixes washed-out colors on Android), **P4 game polish**
  (Kenney art on `D:\GameAssets_Kenney`: platformer kit, Cube Pets, Input Prompts), **P5 release engineering**
  (release keystore, OTA updates, git, scripted builds).
- **First box: X98K Pro (2026-10-30)**, an RK3566 TV box (4× Cortex-A55 1.8 GHz, Mali-G52 2EE, 4 GB, Android 11
  userdebug, build X98KPRO_20231007) on the user's Wi-Fi (192.168.1.57, from the evening of 2026-10-30
  192.168.100.103). **The camera works** (end of this bullet). A fresh APK (63.9 MB)
  installs and runs on it (Vulkan); the only error is the expected "no camera". Inspected with `provision.ps1 -Inspect`,
  which now runs `tools/provision/inspect-box.sh` (details in docs/box-os.md "Phase 1 checklist" and docs/hardware.md):
  **USB cameras should work** (kernel UVC driver and Android's external camera HAL running; the HAL allows 640x480 and
  1280x720 up to 60 fps; the box declares back/front cameras it doesn't have, so CameraX 1.6.2's check fails, but its
  default retry policy completes init once at least one camera exists: read in its bytecode, not tried), **an NPU**
  (0.8 TOPS; MediaPipe can't reach it and the firmware has no NNAPI driver, so the pose AI now runs on it through
  Rockchip's own runtime, see "NPU pose engine" below; the firmware's driver 0.7.2 and runtime 1.3.0 date from 2022, and
  our bundled runtime 2.3.2 works with that driver, a warning only), **no ALLM**, time zone London.
  **Wide open out of the box**: adb on the network with no authorisation (port 5555), setuid `su` (AOSP: `su 0 cmd`),
  SELinux permissive, unlocked bootloader, a fake Pixel 5 fingerprint, patch 2021-10, vendor OTA updaters
  (ABUpdate/Adups, Rockchip), a vendor app store with system rights; the stock launcher (system uid) keeps HTTPS
  connections to a Cloudflare-hosted server. Not provisioned yet (Tundu installed, not the home screen; the junk is in
  `disable-packages.txt`). **GPU-bound, tuned the same day** (user: "tune the GPU"; full table in docs/hardware.md
  "GPU tuning on the X98K Pro"). The first build gave menu 49 fps, Vrapo!/Shpata!/Fluturo! 23–25, Tenis! 22, Sporte!
  13 (GPU 97–99 % at 700 MHz, CPU light, 0.5 GB, no throttling up to 70 °C). Measured with `PerfLab` (switches pushed
  to the box while a game runs) over four rounds, then: **OpenGL ES 3 first** (Vulkan was slower everywhere, menu 49
  vs 58), **HDR off** in `Mobile_RPAsset`, **`BoxGraphics`** per scene on Android only (3D games: render scale 0.8, no
  post-processing (~13 ms a frame), a steady 30 fps with frame pacing on; menu, Bowling, Pikturo!: scale 1 so URP
  draws straight to the screen, 60 fps; Vishu! scale 1, 30), Sporte!'s clouds from a noise texture (`SkyNoise`, the
  `sin()` noise cost ~14 ms), a lighter SportHud. Then **Sporte!** (user: "fix Sporte!"; measured in its events with
  PerfLab's `activity=`): real-time shadows off there on the box (the cost was every surface sampling the shadow map,
  not the map: shadow distance and casters changed nothing) with blob shadows under the athletes, render scale 0.72,
  `Tundu/Athlete`'s dither as a `_FADE` variant (a `clip` anywhere costs Mali its early depth test), athletes merged
  outermost first with the closed pieces culled (second submesh), `Tundu/Toon`'s `_TOON_SIMPLE` on the venue materials
  (no rim/gloss, sky light per vertex), fewer HUD layers (only the chosen card casts a shadow, no faint rims, fewer
  outline copies). **Result (final check, one build): menu, Bowling, Pikturo! 60; every other game, Sporte!'s menu,
  100 m, football and volleyball included, a steady 30 (0–0.7 % frames late)**, 69 °C, no throttling. The box look
  has no bloom, grading or vignette, and Sporte! no real shadows (flatter; in-shader grading would bring the grading
  back cheaply). **The camera, the same evening** (user: "see if the camera works"): an "HD USB Camera" (USB
  32e4:9230, USB 2.0) plugged in before boot works: Android lists it as external camera 109, CameraX's check fails as
  predicted and its retries finish the init after ~6 s, Tundu opens it, and MediaPipe found the user in a very dark
  room (Settings → Kamera shows the skeleton; → there saves `camera-snapshot.jpg`, exactly what the AI sees). **But the
  pose AI on the GPU is far too slow**: ~10 poses/s (90–113 ms a frame) with nobody in view, full and lite alike (only
  the detector runs), ~11 (86 ms, lite) with a person, ~170 ms from the camera to the game (the laptop ~75); the game
  drawn at 10 fps didn't speed it up (its own GPU work keeps the GPU at ~75 %). **And it costs the games**: Vrapo! fell
  from a steady 30 to 22 fps (46 % of frames late) with the AI running. **Android gets the camera only at 1024x768,
  1280x1024 or 1920x1080, 30 fps**: the camera lists one rate per size (MJPEG 640x480@120, 800x600@60, 1280x720@60,
  the rest @30), and the external camera HAL bounds each size by the first `FpsList` entry at least as tall in
  `/vendor/etc/external_camera_config.xml` (1280x720 gets 1024x768's 30) and drops faster rates
  (`getFrameRateList: failed to get supported frame rates` in logcat), so those sizes vanish. That HAL also only ever
  offers [f/2, f] fps ranges ([15, 30] here), so `CameraSource` keeps the default. The camera's own modes are in
  `/sys/bus/usb/devices/<port>/descriptors` (UVC descriptors). **Low light (2026-10-31)**: the user's evening room
  gave a nearly black picture (Vishu!'s mirror mean 31 of 255) while the camera kept 30 fps; with root,
  `su 0 v4l2-ctl -d /dev/video9 --set-ctrl=gain=100,gamma=160,saturation=100,exposure_auto_priority=0` took it to 107,
  still 30 fps (the camera had gain 0 and exposure priority on). Android's camera API reaches none of these controls
  on this box, so **the camera service sets them itself** (user: "yes, apply them"): `CameraTuner` measures the middle
  of the picture 4x a second and steps a 6-level ladder (gain share 0..1, gamma and saturation x1..1.9 / 1.83 of their
  defaults; level 4 = the evening room's settings) up below mean 90 (2 levels below 55) and down above 170 (210), one
  step per 2 s, exposure priority always off, set again after a camera restart; `CameraControls` reaches the camera's
  V4L2 node directly where the firmware allows the app (`camera_controls.cpp` in `libtundupose.so`), else `su 0
  v4l2-ctl` (this firmware's su lets any app be root), else nothing (logged once). The stats line ends in
  `light <level> (mean <m>, root|direct)`. For families' boxes: the camera supplier's defaults or the ODM opening the
  node to our app (docs/hardware.md "Low light", docs/box-os.md item 13); a Sony STARVIS camera is the hardware fix.
  **Vishu! on the box (2026-10-31, user: "fix Vishu!")**: it ran ~15 fps with the camera picture; the main cause was
  **heat** (after hours of NPU + GPU + camera the SoC sat at 82-83 °C past its 75 °C trip point: GPU 700 → 200-400 MHz,
  CPU 1.8 → 1.4 GHz; then the menu was 38 fps, Vrapo! 25), which also made per-layer tests noisy until the GPU was
  pinned (`su 0 sh -c 'echo userspace > /sys/class/devfreq/fde60000.gpu/governor; echo 200000000 > .../userspace/
  set_freq'`, put back to `simple_ondemand` after). At a pinned 200 MHz: 10.0 fps → **18.6** after: render scale 0.7
  (`BoxGraphics`), Tundu/Outfit without its per-pixel sRGB round trip (atlas and thumbnails imported as data,
  `DressArtImport` v2; `FastSRGBToLinear` out), the mirror's vignette inside the picture's shader (Tundu/Fx
  `_VIGNETTE`, uv1 on the quad) instead of a full-screen IMGUI layer, and the mirror's frame drawn as its 8 edge
  pieces (`FrameEdges`; as one GUI.Box its clear middle was blended over the whole screen: the biggest single cost).
  The main thread (39 ms a frame, 23.5 fps) was fixed the next evening: see "Heat and frame rate" below (the
  "material property-sheet copying" seen then was a symbol mix-up: those libunity symbols were another build's).
  **Next**: a try with a person in good light (tracking
  speed in a game, the lite/full switch), the camera's lost modes (the ODM's HAL config, or our own UVC over USB),
  camera hot-plug (`CameraSource` starts once: a camera plugged in after launch, or unplugged and replugged, is never
  picked up). The laptop can stand in for the camera: `main.py --preview --host <the box's IP>` (MotionSystem listens
  on all interfaces; the box's clock was 6 s behind the laptop's, which turns Tenis!'s lag correction off). Not known
  whether an RK3576 box is on order (`docs/sourcing.md`; delivery takes 2–4 weeks).
- **NPU pose engine (2026-10-30 night, user: "straight to the NPU")**: the pose AI runs on the RK3566's NPU, the GPU
  stays the games'. **Vrapo! back to a steady 30 fps with the camera AI running** (29.9–30.0, 0–0.7 % frames late;
  MediaPipe on the GPU had pulled it to 22) while the AI runs at ~20 poses/s with nobody in view (the person detector,
  ~41 ms a frame). On the box without a game (`pose_npu_test`): a tracked frame (crop + landmark model) **lite 25.5 ms,
  full 34.4 ms**, the detector ~43 ms; with a person in a game: not measured yet (dark room, nobody tracked).
  **Same answers as MediaPipe**: landmarks within 0.13–0.53 px mean of MediaPipe's own on 1000 px pictures (3.5 px
  worst), world joints within 0.1–0.4 cm (`tools/npu/compare.py --box`). How: MediaPipe's own two models (from the
  .task files) converted to RKNN float16 by `tools/npu/convert.py` (Rockchip's RKNN-Toolkit2 2.3.2, Linux only: a venv
  in WSL Ubuntu on `D:\rknn\venv`, TensorFlow 2.14 + LiteRT), run by our C++ engine (`MoveItMotion.androidlib/src/
  main/cpp/pose_npu.cpp`, `libtundupose.so` from `tools/npu/build_native.py`, Rockchip's `librknnrt.so` 2.3.2 next to
  it in `jniLibs`) that does MediaPipe's steps exactly (`tools/npu/blazepose.py` is the Python reference, matching
  MediaPipe to 0.03 px): SSD anchors, weighted NMS, the crop from the detector's two alignment points (rotated, x1.25),
  crops in MediaPipe's pixel convention (no half-pixel shift: that alone was ~2 px off), heatmap refinement, projection,
  world landmarks turned by the crop's rotation, the next crop from landmarks 33/34, and MediaPipe's VIDEO-mode
  smoothing (One Euro 0.05/80 on landmarks scaled by the body, 0.1/40 on world joints, visibility low-pass 0.1, the
  relative velocity filter on the alignment points). Java: `NpuPose` (JNI), `Pose` (one result format for both
  engines), `PoseEngine` tries the NPU first (models in the APK's assets per chip: `pose_detector.rk3566.rknn`,
  `pose_landmarks_<lite|full>.rk3566.rknn`), MediaPipe GPU/CPU otherwise; auto model full → lite past 34 ms while
  tracking; packets say `src: android-rknn-<model>`; the stats line shows the detector and landmark times. **What made
  it fast** (lite tracking 110 → 25.5 ms): the landmark models without MediaPipe's segmentation mask (its 256 x 256
  upsampling was ~55 of ~96 ms on the NPU), the heatmap output channels first (the converter's NHWC transpose ran on the
  CPU, ~10 ms), our crop writing the NPU's own float16 input (RKNN converted uint8 on the CPU, ~7 ms), the crop in
  fixed point on two cores (11 → ~4 ms). The NPU clock (600 MHz, `userspace`; 900 barely helped: memory-bound) and the
  CPU (1.8 GHz) aren't the limit. **Heat**: after ~1.5 h of the camera AI nonstop (nobody in view = the detector every
  frame) plus games, the SoC was at 81–82 °C, past its first passive trip point (75 °C; then 85): GPU 700 → 600 MHz,
  CPU 1.8 → 1.6 GHz; Vrapo! still ~29.9 fps (since 2026-10-31 the detector runs every 150 ms while nobody is in
  view: "Heat and frame rate" below). **Not yet**: a person tracked in a game (speed, feel), INT8 quantization (needs calibration pictures; the NPU is built for 8-bit),
  the detector's 29.5 MB RKNN file (its depth-to-space layers grow into big transposed convolutions; it only runs
  while nobody is tracked), the APK grew 64 → 77 MB.
- **Heat and frame rate (2026-10-31, user: "fully optimize everything, reduce the heat and increase the fps")**.
  Measured first (simpleperf with the build's symbols: `tools/dev/box-profile.sh`; `top -H`; `tools/dev/box-heat.sh`,
  `box-scenes.sh`), details in docs/hardware.md "Heat and frame rate with the camera on". Changes: the camera picture
  for Vishu!/Pikturo! handed over raw inside the process (`FrameShare` → `frame_share.cpp` → `CameraFeed`
  `[DllImport("tundupose")]`; the JPEG decode was 15 ms of Vishu!'s 39 ms frame, the encode ~9 % of the app's CPU);
  the pose AI reads CameraX's buffer in place (no Bitmap copy); nobody in view for 2 s = the AI looks every 150 ms and
  the analysis thread sleeps between looks (CameraX converts no frames); the NPU keeps the full model only under 24 ms
  a frame (`NPU_SLOW_MS`: lite on the RK3566; full at ~29 ms kept the NPU 64 % busy instead of 34 %, and switching to
  lite cooled the SoC 77 → 75 °C within minutes); IL2CPP "faster runtime" (`BoxBuild`: the size-optimised code ran
  value-type generics through a slow shared path, 17 % of the home screen's main thread); 60 fps scenes drop to 30
  when nobody uses them for 10 s (`BoxGraphics.Idle`: the home screen counts only the remote, since with someone in
  view its main thread needed ~17 ms a frame and it ran 43 fps with 39 % of the frames late; Bowling, Pikturo! and
  the camera test also count a player in view); the home screen's background is the camera's clear colour; Vishu!'s
  label widths cached. PerfLab: `model=auto|full|lite` switches the pose model at runtime
  (`MotionService.setModel`). **Results** (someone on the couch in view): home screen 30.0 fps steady (60 with the
  remote), main thread 16.9 → 12.5 ms, Tundu's CPU 190 → 120 % of a core, the box's 287 → 197 %, GPU 700 → 400 MHz,
  NPU 64 → 34 %, SoC 77 → 75 °C and falling; Vishu! 23.5 → 29.9 fps (main thread 39 → 20 ms); every 3D game a
  steady 30 with the AI tracking someone (0–0.7 % of the frames late), Bowling 56.6 and Pikturo! 58.8 of their 60.
  The 3D games keep the GPU at 700 MHz, 60–76 % busy: the SoC went 75 → 80 °C over a round of the games, and 10 min of
  Tenis! sat at 80–81 °C with the kernel's power allocator capping the GPU (300–600 MHz at ~100 % load) and the CPU
  (1.6 GHz): 28.6–30 fps. A heat valve (3D scenes one render-scale step smaller above 79 °C) was tried and
  removed: the next 10 min sat at 80.5–81.6 °C anyway, the GPU capped lower (200–400 MHz), Tenis! 26.9–29.5 fps
  (someone was playing, so more AI work: not a clean test, but no gain). The power allocator holds the SoC near its
  set point by itself and hands freed GPU power to the CPU. **The rest is
  firmware** (docs/box-os.md item 20): the CPU governor holds 1.8 GHz for any game's frame bursts, the GPU governor
  700 MHz from ~40 % load, a small passive box. **Not yet**: INT8 pose models (the NPU is the biggest heat lever left
  in the app), CameraX delivering YUV to the NPU path (its RGBA conversion is ~6 % of a core), a longer idle level
  (screen saver, camera off), the user's feel of the 30 fps home screen and of lite.
- **Git: on GitHub since 2026-10-10** (https://github.com/PlotterOtterGames/moveit, branch main, first commit
  be51fc6, the user asked for it). Commit and push only when the user asks.

## Repository map (`D:\Projects\moveit`)
| Path | What |
|---|---|
| `motion-service-py/` | PC camera service: `main.py` (OpenCV + MediaPipe 1.0.1, `--model auto`, sends protocol v1.1), `video.py` (camera preview stream on request), `fake_sender.py` (scripted test player: `basic`, `bowl`, `slash` (fast swipes, for the hand cursors), `idle`, `dropout`, `jumps`, `runner`, `paint`, `sword`, `fly`, `turns`, `dress`, `runway`, `turn` (Vishu!: the whole body turns and spins in 3D), `tennis` (Tenis!: one racket swing every 3.4 s, serve / forehand / backhand / lob / slice), `tennis_move` (steps to both sides, toward and away from the camera: a segment's `near` = metres toward the camera, the picture has perspective), `place` (Vrapo!: too close, too near a side, a run drifting to the edge); draws a fake camera picture when asked), `replay.py`, `models/*.task`. Venv in `.venv` (Python 3.12). New script `brawl` (Fluturo!: jabs left and right
without a break for 2.5 min) |
| `docs/protocol.md` | **Motion Protocol v1.1**: UDP JSON to 127.0.0.1:5005; 17 COCO joints `k` (+ optional world joints `wk`, `cam`, `model`) |
| `unity/MoveItGames/` | Unity **6000.3.9f1** project (URP), **active platform: Android**. Editor at `D:\Unity\6000.3.9f1` |
| `Assets/MoveSDK/Runtime` | Motion SDK: `CameraFeed` (camera picture for AR modes, on request; on the box raw frames from libtundupose, else JPEG over UDP), `MotionSystem` (UDP receiver, feeds every packet in order), `BodyTracker` (smooth + fast low-lag tracks with body-relative One Euro filters, velocity, world joints, 1.5 s dropout continuity), `BodyGestures` (lanes with look-ahead, jump at take-off incl. from a knee dip, crouch at onset, hands up), `BodyHands` (hand cursors in a body-relative zone), `PlacementCheck` (is the player where the game's `PlayArea` needs them: step back / move left / move right / come closer), `HandInterpolator` (the one cursor engine: camera clock, interpolation or a least-squares fit, glide), `CursorRope` (string stabilizer), `UnderhandThrowDetector` (bowling, world depth + backswing), `PunchDetector` (punches in any direction, 3D with world joints, tested on simulated arms), `BodyInput` (body + keyboard merged), `Android/AndroidMotionService.cs` (starts the Java service, camera permission, status) |
| `Assets/Plugins/Android/MoveItMotion.androidlib` | **Java camera AI plugin**: CameraX 1.6.2, the pose AI **synchronous** on the CameraX analysis thread: **Rockchip NPU first** (`NpuPose` → `src/main/cpp/pose_npu.cpp` in `jniLibs/arm64-v8a/libtundupose.so`, Rockchip's `librknnrt.so` 2.3.2 beside it), else MediaPipe tasks-vision 1.0.0 in VIDEO mode, GPU→CPU fallback; auto full→lite, `Pose` = one result format, sends protocol packets to localhost, on-demand camera snapshot; nobody in view for 2 s = the AI looks every 150 ms and the thread sleeps between looks; on the NPU frames are read straight from CameraX's buffer; the camera picture for the game goes through `FrameShare` (`src/main/cpp/frame_share.cpp`: raw RGBA in the process, read by `CameraFeed`), the JPEG `VideoStreamer` only as the fallback; `CameraTuner` + `CameraControls` brighten the USB camera in a dim room through its own UVC controls (V4L2 node or root). Models (`.task`, `.rknn`) copied in at build time from `motion-service-py/models` (gitignored there); `libtundupose.so` is committed (rebuild with `tools/npu/build_native.py` after changing the C++) |
| `tools/npu/` | **The NPU pose path as code**: `convert.py` (MediaPipe's models → RKNN float16 per chip, in WSL: the detector's sparse weights densified, the landmark models without the segmentation mask and with the heatmap channels first), `blazepose.py` (MediaPipe's pose steps in Python, the reference for the C++), `mp_reference.py` (MediaPipe's own answers, PC venv), `compare.py` (TFLite / RKNN simulator / box dumps vs MediaPipe, px and cm), `build_native.py` (NDK build of `libtundupose.so` and `out/pose_npu_test`, the shell test for the box) |
| `Assets/Plugins/Android/AndroidManifest.xml` | Registers the app as the box's **HOME launcher** (`singleTask`); entry point = Activity; asks the TV for game mode (`preferMinimalPostProcessing`) |
| `Assets/Games/Common` | Shared game code: `GameHud` (IMGUI helpers, temporary), `CommonStrings` (Albanian/English, persisted), `PlayerGate`, `RemoteInput` (TV remote mapping), `AndroidDevice` (volume, Wi-Fi screen), `Brand` (reads `Assets/Resources/brand.json`), `RecognitionLab` (camera test screen), `DevCapture`, `BoxRuntime` (60 fps, no sleep), `BoxGraphics` (render scale, post-processing and target fps per scene on Android players: the box settings measured on the X98K Pro; a 60 fps scene nobody uses for 10 s runs at 30, `BoxGraphics.Idle`: the home screen counts only the remote, Bowling/Pikturo!/the camera test also a player in view), `PerfLog` (a `[Perf]` line every 10 s in every scene: fps against the target and the share of frames shown late, worst frame, share over 20 / 34 ms, main thread / present wait / render thread ms from frame timing stats (switched on by BoxBuild; GPU ms aren't reliable on the X98K Pro), GC, memory), `PerfLab` (players only: render settings switched on the box from a pushed file: HDR, scale, shadows, shadow distance/resolution, casters off by name (`nocast`), post, bloom/vignette/grade, sky, fps, hide renderers by name/kind, disable scripts by class (a game's HUD), load a scene, start an activity game's event (`activity=sprint|football|volley`); lines tagged in `[Perf]`), `SkyNoise` (the noise texture Tundu/Sky's clouds read), `PartMesh` (primitives merged into one vertex-coloured mesh: Fluturo!, Tenis!), `Activities/` (`ActivityGame`: a game of short activities with medals; `SportHud`: the broadcast-style HUD pieces), `BodyPuppet` + `ToyPoses` (the player's body in metres, keys standing in for it), `GameFx`, `ArtShapes` |
| `Assets/Games/Tennis` | Tenis! (singles): rules without the engine (`TennisMatch`: the body → the court (`BodyToCourt`) and the assist, serve, rally, where the ball is met, timing → aim, the computer player, practice; `TennisScore`; `TennisBall`: `Court`, `BallPhysics` (exact steps, prediction, `Aim`); `TennisLevel` (the ladder); `SwingDetector`), drawing (`TennisGame`: flow, swings from the camera, ball, shadow, ring, effects, calls, scoreboard; `TennisPlayerView`; `TennisStadium`; `TennisArt`), `TennisStrings` |
| `Assets/Games/Sports` | Sporte! (`SportsGame`, an `ActivityGame`: the menu with the three venues): `TrackActivity` + `TrackRules` (`TrackRace`), `FootballActivity` + `FootballRules` (`FootballMatch`), `VolleyActivity` + `VolleyRules` (`VolleyMatch`), `SportsMotion` (`RunDetector`, `ArmFlick`, `KickDetector`, `DipDetector`), `SportsKit` (`SportsBalls`, `ShotCamera`, `Daylight`), `Athletes/` (`Athlete`, `AthleteLook`, generated `AthleteData`), `Venues/` (`SportsArt`, `Venue`, `Fans`, `GoalNet`), `SportsStrings`. `Art/`: `Athlete.fbx`, `Venues/*.fbx` + seats, `crowd_atlas.png`, `Textures/`; import rules `Assets/Editor/SportsArtImport.cs` |
| `art/sports/` | Sporte!'s art as code (Blender 5.1, headless): `athlete.py` (the athlete's pieces, rig, AO, previews), `athlete_anim.py` (33 clips, IK pose solver, contact sheets), `crowd.py` (the crowd atlas), `build.py` (`--anim --ao --export --crowd --preview --clips --save`), `venue_kit.py` + `venues.py` + `venues_build.py` (the three venues) |
| `tools/sports/textures.py` | Sporte!'s textures (PIL): ground details, the ad strip with the logo, the net, the big screen |
| `Assets/Games/Runner`, `Bowling` | Vrapo! (runner), Bowling (1–4 players, `BowlingScore` with tests) |
| `Assets/Games/Dress` | Vishu! (dress-up mirror + fashion show, replaced Fruta): rules without the engine (`DressCatalog`, `DressLook`, `OutfitRig`, `PoseTimeline`, `ShowRules`), drawing (`OutfitArt`, `OutfitRenderer`, `DressFx`), `DressGame`, `DressStrings`; `Art/` made by tools/dress; shaders `Games/Common/Shaders/TunduOutfit`, `TunduBodyMask`; import rules `Assets/Editor/DressArtImport.cs` |
| `tools/dress/` | Vishu!'s art as code: `draw.py` (vector toolkit, the body template), `outfits.py` (every item, atlas + manifest), `ui_art.py` (icons, judges, curtains, particles, thumbnails), `preview.py` |
| `Assets/Games/Paint` | Pikturo!: `PaintGame` (modes, toolbars, hands, photos), `PaintCanvas` (premultiplied RenderTexture, wet paint, undo, bucket), `PaintBrush` (tools along smoothed strokes), `PaintPen` (pen rule), `PaintArt` (brush atlas and icons drawn in code), shader `Tundu/Brush`. Pages in `Assets/Resources/Paint/Pages` (alpha-only import: `Editor/PaintPageImport.cs`) |
| `tools/logic-tests/` | .NET 8 + NUnit 3 project that links the engine-free game rules and their EditMode tests from the Unity project and compiles them against `UnityShim/` (cloud sessions: `dotnet test tools/logic-tests`) |
| `tools/compile-check/` | `check.py`: compiles every asmdef except MoveIt.Editor as its own .NET project (same sources, defines, references) against Unity's UnityEngine reference assemblies (NuGet `UnityEngine.Modules` 2021.3) and `Stubs/` (Input System, test runner), for the Android player and the editor. Names Unity 6 added are listed in `UNITY6_ONLY` |
| `tools/motion/cursor_sim.py` | Hand-cursor and Pikturo-pen simulator (mirrors OneEuroFilter, BodyTracker, BodyHands, HandInterpolator, the game loop; MediaPipe's own landmark filter, the laptop's recorded frame timing, noise measured from a real session by speed and visibility, each game's screen mapping): stutter, shake, lag, overshoot, swipe speed, when the brush goes down. `--recording <file> [--palm] [--tune]` scores the cursors on a real session against the path its samples make when smoothed both ways in time; the score under-weighs lag, and **the user's feel outranks both** (2026-10-20: the best-scoring brush felt worse and was reverted). Run it before changing cursor or pen numbers |
| `Assets/Games/Fly` | Fluturo!: `FlyRun` (all rules of the endless run: flight, drone swarms, rushes, clashes, the boss, districts, `DistrictSettings`; no scene, tested with simulated players), `FlyProgress` (stars, outfits, missions, bests between runs; tested), `FlyTheme` (time of day per district), `FlyRoute` (the street grid and the turns; no scene, tested), `FlyGame` (body/keys to steering, turns and punches, draws the run, HUD, perf log), `Hero` and `RobotView` (merged-part meshes, cape cloth), `FlyWorld` (blocks around the hero, the city turned and moved around it, clouds), `CityBuilder` (a block's geometry: houses, streets, parks), `FlyArt` (ring, star, heart, arrow; `PartMesh` moved to Games/Common), `FlyStrings`. `Art/fly_city.png` = the city's texture array |
| `tools/fly/city_textures.py` | The city's 16 tiles as code (PIL, drawn at 4x): facades, shop fronts, roof, road, crossroads, pavement, grass, cobbles, water, leaves. Alpha = material (wall / fixed / glass). `--preview` writes `tools/fly/out/` |
| `Assets/Games/Sword` | Shpata!: `SwordGame` (duel: knight AI, blocks, counterattacks, markers, HUD), `SwordMoves` (swing/hit/shield/parry rules on the screen, tested), `GhostAvatar` (see-through player + sword + shield), `Knight` (toy knight, animated by state), `SwordArt` (shield-spot ring, alert, heart drawn in code). Shader `Games/Common/Shaders/TunduGhost` |
| `tools/paint/pages.py` | Colouring pages as code (OpenCV, figures outlined by morphology): dino, house, fish, butterfly, rocket, cake. `--preview` writes checks with the toolbar areas to `tools/paint/out/` |
| `docs/sourcing.md` | Where to buy every part (links), batch suppliers, Albanian import and CE, the ordering process |
| `docs/cloud.md` | **Gift cards, Game Pass, app/firmware updates, new games**: recommendation, threat model, formats, release steps, setup, runbooks, costs, sources |
| `server/` | Tundu cloud: Cloudflare Worker + D1 (`src/`, `migrations/`, `wrangler.jsonc`), the CLI `node cli/tundu.ts help` (keys, cards, devices, stats, releases, a pretend box `devbox`), tests (`npm run test:all`: typecheck, 49 Worker tests in workerd, 14 CLI tests), shared test vectors `test/vectors` (codes, signed docs, rollout) checked by Java and C# too. Node ≥ 22.18 runs the TypeScript CLI directly |
| `releases/` | Public keys the app pins (`keys.json` → generated `TrustedKeys.java`), signed channel manifests (git history = release log), staged APK/firmware lists. Written only by the CLI |
| `Assets/Plugins/Android/MoveItCloud.androidlib` | **Box cloud client** (Java, no dependencies): `TunduCloud` (Unity API + jobs), `CloudState` (all rules, pure), `SignedDoc`, `PassClock`, `PassToken`, `ChannelManifest`, `GiftCode`, `Downloader`, `CloudApi`, `Installer` (+ receivers), `UpdateJobService`, `DeviceOwner`/`AdminReceiver`, `FirmwareUpdater`, `CrashReports`, generated `TrustedKeys` |
| `Assets/Games/Common/Cloud` | Unity side: `TunduCloud.cs` (JNI bridge, editor simulation), `GameAccess` (catalog rules, tested), `GiftCode` (tested against the server vectors). UI: `Shell/Scripts/GamePassPanel.cs` |
| `tools/cloud-java-tests/` | `run.py`: compiles MoveItCloud against Android API 36 (Robolectric's android-all jar from Maven Central, ~200 MB cached) with warnings as errors and runs its 33 JUnit tests |
| `Assets/Shell/Scripts/MainMenu.cs` | Box home screen (8 game tiles; a step left/right or ← → moves one tile; back from a game, its tile is selected) + Settings (Wi-Fi, volume, language, camera, version, device; hidden Android settings: 7× OK on "Device") |
| `Assets/Editor/MoveItSceneBuilder.cs` | **Scenes are generated by code**: menu *MoveIt → Build All Scenes* (Menu, Runner, Dress, Bowling, Paint, Sword, Fly, Tennis, MotionDebug). Re-running overwrites them |
| `Assets/Editor/BoxBuild.cs` | *MoveIt → Box → Build Android APK* → `builds/android/<brand name>.apk` (~54 MB). Applies branding, IL2CPP ARM64, min API 28, TV banner and icon, copies models, sets GRADLE_USER_HOME=D:\.gradle in-process, caps Gradle heap at 2 GB |
| `Assets/Resources/brand.json` | **The brand's single source**: name, app id, taglines, palette (primary/secondary/accent/grape/leaf/background/surface/text/textMuted, 4 player colors), fonts. Read by `Brand.cs`, `BoxBuild.cs`, the scripts in `tools/` |
| `tools/branding/` | `logo.py` (the logo as geometry → SVG + anti-aliased PNG), `make_branding.py` (logo files in `brand/logo/`, icon/banner/logo/mark in `Assets/Branding/`, boot animation + boot logo BMP + GIF preview in `out/`), `brand_board.py` (the review page). Run with the motion-service venv |
| `brand/logo/` | Logo masters: on-dark, on-light, white, night, mark, icon (SVG + 2048 px PNG) |
| `art/` | **Blender art as code** (Blender 5.1): `tools/tundu_art.py` (palette + UVs-to-palette-cell, ellipsoid/cone/part helpers, preview renders), `runner/dino.py` (shape), `runner/dino_rig.py` (bones, envelope weights, clips, FBX export), `runner/world.py` (props, one FBX each), `runner/runner.blend` (saved result). Previews render to `runner/previews/` (gitignored) |
| `music/` | `README.md` (how the user changes music), `midi/*.mid` (editable sources; compose.py never overwrites them without `--force`) |
| `tools/music/` | `midi.py` (SMF read/write), `synth.py` (Wii-style instruments, GM program map, drums, reverb), `render.py` (MIDI → seamless-loop OGG via ffmpeg), `compose.py` (the songs as code), `sfx.py` (sound effects) |
| `Assets/Resources/Music`, `Sfx` | Drop-in audio loaded by name by `Games/Common/GameAudio.cs`; import rules in `Assets/Editor/AudioImport.cs` (music streams, SFX mono) |
| `Assets/Games/Common/Shaders/` | `TunduToon` (toon + curved world, used by all runner art and ground; options for merged-part characters: vertex colours, gloss, emission, flash; `_TOON_SIMPLE` material keyword for big flat surfaces: no rim or gloss, sky light per vertex: Sporte!'s venues), `TunduCity` (Fluturo!'s city: texture array, vertex-colour walls, reflective glass, fog, curved world), `TunduSky` (gradient skybox; clouds from `SkyNoise`'s texture), `TunduFx` (particles, blob shadow), `TunduAthlete` (Sporte!'s athletes: `_FADE` variant for the dither, `_Cull` per material), `TunduCrowd` (shadow per fan, at the vertices) |
| `Assets/Games/Runner/Art/` | Exported FBX + `tundu_palette.png` + generated `Dino.controller`. Import rules in `Assets/Editor/RunnerArtImport.cs` (point-filtered palette, no imported materials, clips named/looped) |
| `tools/provision/provision.ps1` | Turns a stock Android box into a MoveIt box over adb (`-Inspect`, `-BootAnimation`, `-Undo`). `-Inspect` also runs `inspect-box.sh` on the box: identity, security (adb, SELinux, su), CPU/GPU/NPU, display and ALLM, USB camera support (UVC driver, camera HALs, fps limits), temperatures, input devices, every network connection with the app that owns it. Vendor junk per model in `disable-packages.txt` |
| `tools/dev/run-on-mumu.ps1` | Installs the APK into MuMu (adb 127.0.0.1:16384) with camera permission, starts it, shows logs |
| `docs/box-os.md` | How Android is hidden (layers 1–4) and the checklist to send the box maker (ODM) |

## How to run and verify
- **PC camera service:** `cd motion-service-py && .venv/Scripts/python main.py --preview`. Without a camera:
  `.venv/Scripts/python fake_sender.py --script basic`.
- **Unity tests** (via the unityMCP tools): PlayMode 22 (`GesturePipelineTests` ×6 using fake_sender, `HomeScreenTests`
  (also Settings → Game Pass: keypad ↑ ↓, a code typed on a USB keyboard redeemed by the editor simulation,
  `gamepass*.png`), `PaintGameTests` ×3: paper mode, camera mode, all tools + photo; screenshots in
  `unity/captures/paint_*.png`; `SwordGameTests`: the duel with the scripted player: a swing off the guard, shield
  blocks from all three sides each followed by a counterattack that lands, an unblocked blow costs a heart,
  `sword_*.png`; `FlyGameTests`: fake_sender --script fly, port 5014: exactly 6 punches with the right hands, each
  smashes a robot, the body steers, the boss comes, `fly_*.png`; --script turns: stepping right then left at a
  crossroads the test puts ahead turns right then left, `fly_turn.png`; --script brawl: a whole first district played
  by the body, `fly_rush.png`, `fly_clash.png`; `DressGameTests` ×5, ports 5015 (motion) and 5018/5019 (video):
  --script dress: the hands dress the player, the clothes follow, `dress_mirror.png`, `dress_arms_up.png`; --script
  runway: a whole fashion show, `dress_runway*.png`, `dress_judges.png`, `dress_results.png`; the doll and the magic
  effects, `dress_doll.png`, `dress_magic_*.png`; every item on the doll, `dress_items_0..5.png`; --script turn: the outfit drawn at every angle of a full spin,
  `dress_turn_*.png`; `TennisGameTests`, port 5021: --script tennis in practice, the computer timing its feeds to the
  scripted swings: the body serves, then returns feed after feed (forehands, backhands, lobs, slices) on time,
  `tennis_serve.png`, `tennis_coming.png`, `tennis_hit.png`; --script tennis_move: steps move the player to both
  sidelines, the net and behind the baseline, `tennis_step_right.png`, `tennis_at_net.png`; the racket at 60 fps through
  every kind of swing never jumps (max step per frame < 0.3 m), close-ups `racket_*.png`), EditMode 156 + 2 explicit
  diagnostics
  (`PlacementCheckTests` 9 (a pinhole camera: right distance, too close, unseen knees, growing at the net, sides, no
  flicker on a border, far = advice, the in-play margin, no face), `BowlingScoreTests` 10, `PaintCanvasTests` 11, `HandCursorTests` 6 (incl. the fit and the rope), `SwordMovesTests` 5, `GiftCodeTests` 3,
  `GameAccessTests` 4, `PunchDetectorTests` 9 (forgiving punches, incl. the MediaPipe-like camera), `FlyRunTests` 32 (the endless run),
  `FlyProgressTests` 8, `FlyRouteTests` 8, `DressRulesTests` 18 (incl. the turn: sheets never fold and cover the silhouette, yaw from world joints and the
  picture, a turned dress keeps its width, hats and glasses stay on a turned face, upright from behind),
  `DressArtTests` 3, `TennisScoreTests` 4, `TennisBallTests` 4, `SwingDetectorTests` 7 (simulated arms through the low-lag
  filter), `TennisMatchTests` 15 (the body → the court, the assist reaches near balls only, volleys at the net, timing aims,
  lag taken out, early swings wait, shots in, the balance table)). 2026-10-31 (heat and frame rate on the box): 26/26 PlayMode, 163/165 EditMode (`DressArtTests` now expects the outfit atlas imported as data). 2026-10-22 (Sporte! rebuilt): 26/26 PlayMode, 163/165 EditMode (2 explicit diagnostics skipped). 2026-10-21 (placement check): 22/22 PlayMode,
  156/156 EditMode. 2026-10-21 (Tenis! singles with body movement):
  21/21 PlayMode, 147/147 EditMode; after the racket rework the 3 Tenis! PlayMode and 30 Tenis! EditMode tests pass. 2026-10-20 (Pikturo back at the 10-19 brush): 19/19 PlayMode, 117/117 EditMode (`Fly_PunchesSmashRobots_TheBodySteers` failed once after a 718 ms editor hitch at its start, then passed alone)
  (`IdleFidgeting_TriggersNothing` still fails now and then: one false bowling throw, once in a full run that day, passed alone). `SwordGameTests` uses port 5013. Run them once at the start of
  a session. `PaintGameTests` use ports 5012 (motion) and 5016/5017 (video), so a running `main.py` doesn't interfere.
  Without the editor open: `Unity.exe -projectPath … -runTests -testPlatform EditMode|PlayMode -testResults <xml>`
  (scenes: `-batchmode -executeMethod MoveIt.EditorTools.MoveItSceneBuilder.BuildAll -quit`). EditMode works with
  `-batchmode`; **PlayMode hangs in `-batchmode`** (screenshots wait for end of frame, the home screen test too):
  run it without `-batchmode` (an editor window opens and closes by itself).
- **Motion tuning:** change filter or gesture numbers only with evidence. The PlayMode tests guard the obvious
  cases; for lag numbers, simulate realistic motions (knee-dip jumps, 0.3–0.6 s sidesteps, 5–7 m/s swipes) with
  camera-like noise (0.003 normalized) against the exact C# rules. For cursors, a real session beats the sim:
  record with `main.py --record` (from the repo root: `motion-service-py/.venv/Scripts/python motion-service-py/main.py
  --preview --record motion-service-py/sessions/<name>.jsonl`) and score with `cursor_sim.py --recording`.
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
  Start the emulator without its window: `"D:\Program Files\Netease\MuMuPlayer\nx_main\MuMuManager.exe" control -v 0
  launch` (`shutdown` to stop it: with Unity + IntelliJ open there isn't memory for MuMu and a Gradle build at once).
  MuMu sometimes shows its own ad over Android (close its ×). In MuMu the first key after the app starts is lost
  (probably Android's touch mode; check on the box). Screenshots: `adb shell screencap -p /sdcard/s.png` + `adb pull`
  (PowerShell's `>` corrupts binary output). `adb shell input text CODE` types into the Game Pass screen.
- **The real box (X98K Pro, since 2026-10-30):** on the user's Wi-Fi at `192.168.100.103` (`192.168.1.57` before the
  evening of 2026-10-30; the laptop is `192.168.100.18`): `adb connect 192.168.100.103:5555` (this firmware never asks
  to authorise the laptop). Install and start: `run-on-mumu.ps1 -Device 192.168.100.103:5555`. **Unity's Android build restarts the adb server**, which drops network
  devices: connect again after every build. Batch build without the editor open (~3 min with a warm Library):
  `Unity.exe -batchmode -projectPath unity/MoveItGames -buildTarget Android -executeMethod
  MoveIt.EditorTools.BoxBuild.BuildApk -quit -logFile <log>`, scenes first with `MoveItSceneBuilder.BuildAll`. Frame
  rate: `adb logcat -s Unity | grep "\[Perf\]"` (`PerfLog`, every 10 s); temperatures and clocks:
  `/sys/class/thermal/thermal_zone0|1/temp` (SoC, GPU), `/sys/class/devfreq/fde60000.gpu/{cur_freq,load}`. From Git
  Bash: `export MSYS_NO_PATHCONV=1` first (it turns `/data/local/tmp` into a Windows path), screenshots with
  `adb exec-out screencap -p > file.png`. **Trying render settings on the box** (`PerfLab`): write the file with the
  box's own shell, e.g. `adb shell "printf 'tag=t1\nscene=Runner\nshadows=0\n' >
  /sdcard/Android/data/al.tundu.games/files/perflab.txt"` (`adb push` can't write into the app's folder on Android
  11), then read the `[Perf] Runner [t1]` lines (the second one is steadier); delete the file to go back. Load scenes
  with `scene=`, not with remote keys: key presses get ahead of the game when frames stall and the script ends up in
  the wrong game. Don't switch post-processing effects mid-scene on OpenGL ES (colour grading without bloom ran at
  18 fps in Vrapo!). The camera AI's own numbers: `adb logcat -s MoveItMotion` (a stats line every 5 s: camera and AI
  fps, ms, NPU/GPU, the NPU's detector and landmark times, the frame size, "(idle)" while nobody is in view).
  **Measuring tools** (Git Bash, `tools/dev/`): `box-scenes.sh Menu Runner Dress ...` (a line per scene: fps, frames
  late, main/render thread ms, Tundu's CPU, GPU clock and load, NPU load, temperature), `box-heat.sh <label> <minutes>
  [scene]` (the same every ~20 s, for heat), `box-profile.sh -t UnityMain <name>` (simpleperf with symbols: where a
  thread's time goes, C# methods by name; symbols from the last build in `Library/Bee`, so profile that APK). Unity in
  batch mode (tests too, not only builds) restarts adb mid-measurement. Tundu is a home app: `am force-stop` brings it
  straight back.
- **The NPU pose path** (`tools/npu/`, see the status bullet): converting needs Rockchip's RKNN-Toolkit2 2.3.2 (Linux
  x86-64 only), installed in WSL Ubuntu as a venv on D: (`D:\rknn\venv`, with TensorFlow 2.14 CPU, onnx 1.16.1,
  LiteRT, headless OpenCV; C: is too full for it): `wsl -d Ubuntu -- bash -lc ". /mnt/d/rknn/venv/bin/activate && cd
  /mnt/d/Projects/moveit && python tools/npu/convert.py"` writes `motion-service-py/models/*.rk3566.rknn` (BoxBuild
  copies them into the APK). After changing the C++: `motion-service-py/.venv/Scripts/python tools/npu/build_native.py`
  (Unity's NDK r27c), then rebuild the APK. On the box without the app: push `tools/npu/out/pose_npu_test`,
  `librknnrt.so`, the `.rknn` files and raw RGBA pictures (`<name>.<W>x<H>.rgba`) to `/data/local/tmp/npu`, run
  `LD_LIBRARY_PATH=. ./pose_npu_test pose_detector.rk3566.rknn pose_landmarks_lite.rk3566.rknn lite out <pictures>
  [--loops N] [--frames 2] [--perf]` (timings, the last frame's breakdown, `--perf` RKNN's per-layer table), pull
  `out/*.json` and compare with MediaPipe: `mp_reference.py` (PC venv) then `compare.py --box <dir>` (WSL).
  `TUNDU_NPU_INPUT=uint8` makes the engine use RKNN's uint8 input instead of writing float16 itself. MediaPipe's test
  picture (`pose.jpg` from storage.googleapis.com/mediapipe-assets) and turned copies were the test set; the box's own
  evening photo was too dark for MediaPipe too.
- **Laptop vs cloud tooling:** this laptop has no .NET SDK, so `tools/logic-tests` and `tools/compile-check` (cloud
  stand-ins for Unity) don't run here; Unity runs the same tests. Server tests: `cd server && npm ci && npm run
  test:all` (Node 24 here). Java tests need JDK 21+ (`$env:PATH = "$env:USERPROFILE\.jdks\corretto-22.0.2\bin;" +
  $env:PATH`, then `python tools/cloud-java-tests/run.py`); Unity's JDK 17 is refused with a clear message.
- **Cloud sessions (claude.ai/code, since 2026-10-10):** a Linux container with a fresh clone, none of the laptop
  (no D:, Unity, Blender, MuMu, camera). `.claude/hooks/session-start.sh` (cloud only) installs .NET 8 and the venv
  (`motion-service-py/.venv/bin/python`, + Pillow), builds the logic tests and runs the compile check. Runs here:
  everything in `tools/` except Blender, `fake_sender.py` (to a UDP listener), **`dotnet test tools/logic-tests`**: the
  game rules that need no engine (`BowlingScore`, `SwordMoves`, `FlyRun`, `FlyProgress`, `PunchDetector`, `HandInterpolator`,
  `HandPresence`, `OneEuroFilter`, `Body`, `Joint`, Vishu!'s `DressCatalog`, `DressLook`, `OutfitRig`, `PoseTimeline`,
  `ShowRules`, Tenis!'s `TennisMatch`, `TennisScore`, `TennisBall`, `TennisLevel`, `SwingDetector`) and their EditMode
  tests (84 tests; `PaintCanvasTests` need the
  GPU), compiled as C# 9 against `tools/logic-tests/UnityShim` (Vector2/3, Mathf, Time with Unity's exact formulas, Debug),
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
- **Programmer art reads as "AI games"** (Sporte! 2026-10-22): primitives for people, flat pale light, random-colour cube crowds and grey HUD boxes were judged not even ready to polish. Make real assets as code instead (Blender headless scripts with previews you look at, baked AO, a rig with authored clips, venues with bevels and textures), one art direction, a broadcast-style HUD. Render previews and look at them before wiring anything into Unity.
- **The Bash tool chokes on long inline scripts with apostrophes** ("unexpected EOF while looking for matching `''"), even in quoted heredocs: write the script to a file in the scratchpad and run that.
- **Inside an `Activity`, `Time` is the activity's own seconds and `Kit` its `ActivityKit`**: write `UnityEngine.Time.timeScale` / `.unscaledTime`, and don't name a type `Kit` (the athletes' outfit enum is `Wear`).
- **IMGUI refuses a `GUI.matrix` scaled by 0** and logs an error, which fails PlayMode tests: clamp pop-in scales.
- **Blender vertex colours for Unity**: store linear values and export the FBX with `colors_type="LINEAR"` (Unity passes them to the shader as they are). Generic rigs driven by Playables + IK need `optimizeGameObjects` off; meshes merged at runtime need `isReadable` and no mesh compression (uv1 holds exact region numbers): `SportsArtImport` does both.
- **Procedural swings must start from the pose the arm is in, and turn at a capped speed** (Tenis!'s racket, 2026-10-21:
  starting from a fixed "ready" pose and skipping the backswing when time was short teleported it ~0.9 m in a frame).
  An animation driven by a rule's contact time also needs the wind-up done beforehand (the racket goes back as the ball
  comes), or a swing that reaches the game late through the camera has no time left. Check with a per-frame step test.
- **`PartMesh` vertex colours are stored as given and read as linear** (this project is linear): palette colours come
  out pale (Tenis!'s blue court looked washed out, 2026-10-20). Tenis! sets `PartMesh.Srgb = true`; Fluturo! doesn't
  (its look was tuned that way). Material colours set with `SetColor` are converted already.
- **Motion speeds read at the moment they cross a threshold depend on where the camera frames fall** (Tenis!: the same
  backhand read 3 or 8.6 torso lengths/s at 30 fps). For a strength (power), wait a frame or two for the peak; keep the
  start's capture time for timing.
- **`GUIUtility.RotateAroundPivot` under a scaled `GUI.matrix` turns around the wrong point** (its pivot is taken in
  screen pixels): the skeleton mirror drew only its level bones whenever the screen wasn't 1080 high (the editor's
  Game view; fixed 2026-10-21). Rotate with `GUI.matrix = matrix * Matrix4x4.TRS(a, rotation, one)` (`GameHud.Line`).
- **IMGUI 9-slice boxes smaller than their border draw a square** (2026-10-19: a black square flashed on the dress as
  Vishu!'s toast popped in): `DressGame.Panel` shrinks the corners with the box. Text measured with `CalcSize` renders a
  little wider in a scaled-down Game view: pad pills (`DressGame.TextWidth`).
- **2D clothes on a turning body need the turn, not the picture's widths**: squashing to the shoulders' projected width
  shrank and flipped them. See `Turn` (sections, two sheets, columns along the section) before changing it, and check
  with `fake_sender --script turn`.
- **PowerShell rewrites mangle UTF-8** (2026-10-16): `Get-Content -Raw` + `Set-Content` in Windows PowerShell 5.1 read a
  BOM-less UTF-8 file as ANSI and wrote it back double-encoded ("ë" became "Ã«") with a BOM. Rewrite files with Python
  (`open(..., encoding="utf-8", newline="")`) or the Edit tool. PowerShell also strips quotes from arguments to
  native programs (`python -c "..."` breaks): put scripts in a file.
- **Texture atlases need gaps between cells** (2026-10-16): Vishu!'s pieces packed edge to edge showed thin lines of the
  neighbouring cell along their borders (bilinear filtering + mipmaps). `tools/dress/outfits.py` packs 16 px apart.
- **IMGUI draws in sRGB, and `Graphics.DrawTexture` leaves uv1 undefined**: a custom material drawn in `OnGUI` (Vishu!'s
  coloured thumbnails, Tundu/Outfit with `_LinearOut` 0 and `_UseExtra` 0) must not convert its output to linear
  (colours came out too dark) or read TEXCOORD1 (it held garbage: random rainbow thumbnails). Material colours set
  with `SetColor` are converted to linear in this linear project; `SetVector` passes values as they are.
- **Hand bubbles must be laid out around the player's actual shoulders, not the intended framing**: the camera
  framing only re-centres past a tolerance (2-3 %), which left Vishu!'s bubbles ~60 px from where arms reach.
- **The box's GPU (Mali-G52 2EE) pays for every layer** (2026-10-30): full-screen passes (post-processing ~13 ms, the
  copy of a scaled or HDR image to the screen ~3-4 ms), procedural noise (`sin()` hashes), and IMGUI, which draws
  each element separately: an outlined text is N+2 draws of the text, a panel each of its layers. Sporte!'s menu HUD
  cost ~15-20 ms. Two more from Sporte!: a `clip()`/discard anywhere in a shader, even behind an `if`, costs Mali the
  early depth test for everything drawn with it (make it a variant), and real-time shadows cost in the surfaces that
  sample the map per pixel, not in drawing the map (a shorter shadow distance changed nothing). Check new effects and
  HUDs on the box (`PerfLab`, `[Perf]`), not in the editor.
- **Porting MediaPipe's pose models to Rockchip's NPU** (2026-10-30), each cost an hour: RKNN-Toolkit2 reads TFLite
  through TensorFlow ≤ 2.14, whose interpreter segfaults listing MediaPipe's tensors (hand it LiteRT's `Interpreter`:
  `convert.litert_interpreter`); TF 2.14 pins an old `ml_dtypes` that breaks onnx ≥ 1.17 (onnx 1.16.1); opencv-python
  needs libGL in WSL (use headless); MediaPipe's person detector stores its weights **sparse** (DENSIFY ops), which the
  toolkit's reader can't parse: densify them with LiteRT **without XNNPACK** (with it, the DENSIFY outputs are never
  written and the weights come out garbage), and empty the sparse originals. On the NPU: unused outputs are not free
  (the segmentation mask was over half the landmark model's time), NHWC outputs get a CPU transpose appended
  (append a TRANSPOSE to NCHW in the TFLite: the two cancel), float16 models get their uint8 input converted on the CPU
  (write the native float16 input yourself; on the RK3566 it's NHWC, `RKNN_QUERY_NATIVE_INPUT_ATTR`). Check every
  step against MediaPipe on pictures (`compare.py`): a half-pixel crop convention difference showed up as ~2 px.
- **Stopping a background task doesn't stop a script it started** (2026-10-30): TaskStop ended the tool's shell but
  `bash round3.sh` kept driving the box, so two scripts fought over it (overwritten switches, cleared logs, keys) and
  the numbers were wrong. Stop such scripts by PID (`Get-CimInstance Win32_Process -Filter "name='bash.exe'"`, match
  the command line, `Stop-Process`), and check none is left before measuring again.
- **Profile before guessing, and with the right symbols** (2026-10-31): a profile symbolised with Unity's own
  `libunity.sym.so` from the editor folder (another build than the APK's) named a plausible but wrong culprit
  ("material property-sheet copying") and cost an evening; the real one was the JPEG decode. Use the build's own
  symbols and DWARF call graphs (`tools/dev/box-profile.sh`; frame-pointer chains stop inside libunity). On the A55
  cores the expensive things were plain CPU work: a JPEG decode per frame (15 ms), IMGUI labels (~0.1 ms each, every
  text twice for its shadow), and IL2CPP's "faster (smaller) builds" running generic value-type code through a slow
  shared path (17 % of the home screen's main thread): the box build uses "faster runtime".
- **Engine code stripping vs CreatePrimitive** (found 2026-10-12 on MuMu): the build strips engine classes no script
  names, but `GameObject.CreatePrimitive` adds a collider from native code. Without `Assets/link.xml` (keeps
  Box/Sphere/Capsule/MeshCollider) every primitive logged an error on Android and **Bowling broke** (ball without
  collider: NullReferenceException in `BuildBall`, then every frame). Editor tests can't see this: after installing an
  APK, check `adb logcat Unity:E`. Keep link.xml when adding engine features used only through native code.
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
  fake_sender, then read `[Gesture]` / `[Runner]` / `[Dress]` / `[Bowling]` lines from the log. Delete the marker after.
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
- **fake_sender's arm motions are not rigid arms**: `arm_keys` moves wrist and elbow each in a straight line, so a
  forearm can shrink or stretch, which the forgiving punch rule may read as a straight arm sweeping (a false punch). The
  Fluturo script keeps a boxer's guard (`guard_punch`, fists up all along) and its hook folds back on an arc around the
  elbow (`fold=True`). Arm motions crouch with the body since 2026-10-15 (before, the fists stayed up while the shoulders
  dropped: a "punch"). The simulator in `PunchDetectorTests` uses two-bone IK with fixed bones.
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
