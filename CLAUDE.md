# Tundu (formerly "MoveIt"): handoff for the next session

Read this first. It is the state of the project as of **2026-11-07**, written so a new session can continue
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
- **Tenis! (7th game, 2026-10-20/21, user: "a tennis game, like the wii games tennis one"; since round 5 (2026-11-07,
  near the end of this bullet) the toys run to the ball by themselves, the ladder is three difficulties with a single
  match or a tournament, the camera is steeper with a view from above, and the shadows and effects are new; since round
  6 (the same afternoon) one player and two against each other play **singles**, the racket is a bigger, strung one,
  the player's shots go in and backswings toward the ball hold it; since round 7 (the evening, at the end) a swing up is
  a topspin drive and **the computer's first five returns of every point are sure**: what follows is the history up to
  them. 2026-11-03: the body moves
  the player only sideways, the racket follows the hand; **2026-11-05, after playing it on the box**, user: "match the
  perspective of the wii game; the colours are very bright, not stylised; the red circle where to end up is not
  preferable, the system calculates if it is near or not; I didn't mean an exact hit, like in the wii game when it
  comes close enough it counts; the character is too big, the perspective doesn't let you see the ball; it shouldn't
  matter which side you are on; more fluid, and a second character closer to the net, like the wii game, controlled
  exactly like the first one").** **Doubles against two computer players** (`TennisMatch.Players`: the player's back and
  net player, then the computer's; "one up, one back"). **The body moves both of the player's players sideways only**
  (`TennisMatch.BodyToCourt(lateral)`: 0.9 torso lengths = 4.5 m, fed by `TennisGame.Steer` from where they stood at the
  match's start, `BodyGestures.LateralOffset`; `HumanWanted` may go a metre outside the court); the net player stays
  `NetOffset` 1.4 m to the left of the back player (`HumanNetX`): with both on one line it stood between the camera and
  every ball coming to the back player. Forward and back is the game's: the back player runs in for short balls and
  back to `HomeDepth` 12.6, the net player stays at `NetDepth` 4.6. Human toys run up to 12 m/s (the body's steps).
  **Who plays a ball** (`PlanHuman`, every step until 0.45 s before it arrives, `LockBefore`): the net player volleys a
  ball passing within `NetReach` 1.3 m of it, 0.35-2.4 m high, before the bounce (never a serve, not in practice);
  else the back player meets it after the bounce (at its depth if no higher than 1.7 m, else coming down to 1.05 m); a
  volley let through goes to the back player after the bounce. The hitter glides from where the body puts it to the
  spot beside the ball, up to `AssistSide` 1.6 m (the net player `NetReach`); farther, a swing in time is `TooFar`
  (hint "step toward the ball"). **No ring on the court any more.** **A swing hits when the ball is near enough** (Wii):
  any quick move of either hand, either way (`SwingDetector`), seen from when the ball is `EarlyReach` 3.5 m before the
  point where it's met (at least `MinEarly` 0.3 s) to `LateReach` 2 m past it (at least `MinLate` 0.18 s), on the
  camera's capture times. An early swing's hit waits for the ball (its toy swings at it as it arrives); a late one
  meets it at most 0.6 m past the player. When it came aims the ball: early pulls it across, late down the line, the
  ball within `PerfectReach` 0.6 m of its point = perfect (15 % faster); hand speed = power, up = topspin, steeply up
  (> 0.65) = lob, down = slice, met above 2 m = smash. A swing up to 0.6 s before the window is "Herët!" (too early),
  after it "Vonë!" (too late); earlier ones are just the arm moving (no hint, not counted). From the 3rd pair a swing
  at the window's very edge goes wide (`TennisLevel.WideFrom`). The computer's pair: its net player shadows where the
  player's shot crosses the net and volleys, its back player recovers to the middle; they aim into the corners and
  past or over the player's net player (`0.3 + 0.5 AimAway`). **Wii's camera**, measured from the user's clip (solvePnP
  on the court lines, 2.6 px): 6.47 m up at z -24.16, 17 degrees down, vertical field of view 32 degrees, following the
  back player sideways (x 0.75, +-3.2 m) and a little toward the net, never turning. **Wii's grass court**
  (`TennisStadium`): mown stripes in two greens across the court, cream lines, low dark green walls, beige stands, a
  crowd in soft colours, trees and hills in the haze; colours sampled from the clip and taken back by the light's gain
  (`Lit()`, `LightGain` 1.15); sun 1.0, flat ambient (0.47, 0.5, 0.55), fog 45-160 m; the editor's post is gentle
  (contrast 6, no saturation boost: the box has none). **The players are chibis at 0.92 scale** (~1.4 m, as tall as
  Wii's Miis on their court: `TennisPlayerView.Scale`), rackets 1.0x: the player the red-cap kid, the partner a
  cat-eared girl in the same red (`TennisLooks.Partner`), the computer's pairs from the ladder's ten
  (`TennisLooks.Opponents(level)`, `TennisLevel.Name / Partner / TeamName`: Ana & Beni, Era & Dini, Lea & Toni, Mira &
  Leka, Nora & Gent, then paired differently); their faces follow the points, their heads the ball; the scoreboard
  has a chip per player. **Both rackets follow the racket hand** (`TennisPlayerView.FromHand`: 150 degrees per torso
  across, height 1.07 m - 0.66 m per torso below the shoulders, reach 0.63 m; `HandInterpolator` on the raw samples);
  a ball coming to one of them pulls its racket toward its height and distance, and after a hit the racket goes
  through the ball and lets go within `HitMagnet` 0.18 s (`TennisGame.Racket`); an early swing that connects is drawn
  as a stroke timed to the ball, then the racket follows the hand again. The computer's toys keep their animated
  swings (`CourtPlayer.Preparing`, 600 degrees/s outside the hit). The toss leaves the hand at 1 m at 5 m/s (top ~2.3
  m). **Serve**: any quick move of either hand tosses (or it's tossed by itself), a swing while it's up serves, within
  0.12 s of the top = power serve; serves always go in, a missed toss is tossed again. Physics as on 2026-11-03 (no
  air drag, bounces keep half the fall, level 1 rallies 13.5 m/s +1 a level to 19.5, serves 15 +0.8 to 20); real
  scoring (`TennisScore`), the umpire's calls, "Jashtë!" / "Rrjetë!" at the ball, "Përsosur!" / "Lob!" / "Smesh!"
  after the player's hits. **A run is a ladder** (`TennisLevel.For(n)`, every number capped). Balance
  (`TennisMatchTests.Balance_OnTheLadder`: simulated players, camera samples 0.1 s late, swings timed to the ball with
  noise, sideways movement; wins of 10 at levels 1 / 3 / 6 / 9): perfect 10 / 10 / 10 / 10; good (+-0.09 s) 10 / 10 /
  9 / 5; child (-0.04 +- 0.16 s, 10 % no swing, slow) 10 / 7 / 0 / 0; struggling (+-0.25 s, 25 % no swing) 4 / 0 / 0 /
  0; never moves 9 / 6 / 0 / 0; standing still 0; 1.2-4 minutes a match. In 12 level-4 matches the player's net
  player volleyed ~92 balls, the back player hit ~160, the computer's net player ~78. **On the box** (2026-11-05, the
  user playing, 80-82 degrees C, GPU capped at 300-600 MHz): 28.7-28.9 fps with real-time shadows off there
  (`BoxGraphics`; blob shadows under the players, `TennisPlayerView.BuildBlob`) and the stadium on `_TOON_SIMPLE`; with
  shadows 27.5. The user's first rally swings: 7 hits, 5 too early, 1 too far; the hits came -0.78 to +0.28 of the
  window (mostly early). `TennisStadium` builds the stadium in code (~15 draw calls, ~470 spectators that jump on the
  player's points). The skeleton mirror is hidden while playing. Music "Center Court" (`tennis.ogg`) + 9 `tennis_*`
  sounds (a swish on every quick hand move). Keyboard (no camera): <- -> move, space / Enter / OK swings (hold W = lob,
  S = slice) or serves, the drawn hand goes back on the ball's side by itself and sweeps through on the key; H or the
  remote's up (with the camera) = the racket hand. Practice (tests): the computer's back player feeds at 12-20 m/s,
  timed to the scripted swings (`PracticeContact`); a feed that would come too early even at 12 m/s is fed later
  (a ball machine); the net players stay out of it. The scripted player (`fake_sender --script tennis`) swings
  forehands, backhands, a lob (scoop up), a slice (chop down); `--script tennis_move` steps to both sides, toward the
  camera and away (must not move the player). **Not yet**: the user's verdict on the doubles, the camera, the colours
  and the window (does a child's swing land in it? do they want the net player to volley this much?), the user's
  review of the new Albanian (`TennisStrings.HowTo`, `AimHint`, `SwingWhenClose`, `MoveToTheBall`, `TooEarly`,
  `TooLate`, `TheyWon` (plural now), `NextPlayer` ("the next pair")) and the pairs' names, the racket hidden behind the
  toy's big head when the hand is in front of the chest (the camera is behind and above), the frame rate on a cool box.
  **Round 3 (2026-11-06, the user after playing it on the box: "from the moment the ball first is served, its speed
  starts slow, on each hit it gets faster and faster"; "the movement of the arm is too jittery because of the model as
  well. i need it to not jitter, the character to hold the pad correctly, and to read the horizontal swings based on the
  speed of the swing. vertical swings should miss immediately"; "optimize the graphics even more").** *The rally's
  pace*: every ball flies at a time scale (`BallState.Pace`: velocity x pace, gravity x pace squared = the same arc,
  landing spot and bounce heights, only slower or quicker, so every reach and height rule stays valid; `BallPhysics.
  GravityOf`): the serve at `TennisLevel.ServePace` (0.85 for the first pair, +0.025 a pair up to 1; it was 0.6 and
  the user found it sluggish), every hit `PaceStep` 0.05 quicker, up to `TopPace` (1.35, +0.025 a pair up to 1.5);
  `TennisMatch.PaceFor(hits)`, a new point
  starts slow again; `Shot.Speed` is the flown speed (its own x `Shot.Pace`), so the computer's return chance drops as a
  rally speeds up. The swing window is in metres, so a slow ball gives more time (0.63 s at the start). Practice feeds
  time themselves with the same pace. *Swings*, from the user's real ones in the box's log: within ~25 degrees of level,
  arms raised or dropped at ~75, a backswing ~0.3 s before each stroke (it said "Herët!"), the left hand going up taken
  for a swing. Now only sideways swings hit (`TennisMatch.VerticalLimit` 0.77 = 50 degrees from level), the power is the
  hand's sideways speed; a racket-hand swing up or down at a ball in reach misses at once (`SwingResult.Vertical`,
  `MatchEventKind.Vertical`, `PlayerStats.Vertical`, the hint `TennisStrings.SwingSideways`; a volley let through is
  still the back player's); the free hand's up or down is nothing (`SwingDetector.RightHanded`: never reported, and it
  doesn't use up the racket hand's stroke); a swing toward the ball's side more than `BackswingBefore` 0.12 s before it
  arrives is the backswing (`SwingResult.Backswing`: no hit, no hint). The detector re-arms when the hand turns back and
  its cooldown is 0.22 s (0.35 dropped the stroke after a quick backswing). Tilt: topspin > 0.3, lob 0.6-0.77, slice <
  -0.42. *The racket*: the player's toys no longer follow the wrist every frame (it shook with the pose model's noise,
  magnified 150 degrees per torso length, bent the racket in the mitten, and MediaPipe's wrist jumps exactly when the
  hand passes in front of the chest, at the hit). They hold the racket like the computer's (ready now at the hip on the
  racket side, visible from behind; back on the ball's side as it comes) and every swing of the player is drawn as a
  stroke on both of their toys (Wii): the hit timed to the ball, any other swing at once from wherever the racket is
  (`CourtPlayer.SwingQuick`: forehand or backhand by the way the hand went, `Stroke.Scoop` up, overhead down), as quick
  as the hand (`CourtPlayer.SwingPower`: hit 0.14-0.07 s; a hit whose ball is there already at most 0.08 s). The keys
  swing across from the ball's side. Also fixed: an early swing's toy walked off its spot before the ball came (the
  held hit came out "too far"); views kept between matches restart with the new match's clock and swing count. Balance
  (realistic simulated swings: a backswing first, children 20 % the wrong way and 6 % straight up; with the 0.85 start):
  perfect 10 / 10 / 10 / 10, good 10 / 10 / 10 / 1, child 10 / 6 / 0 / 0, struggling 5 / 0 / 0 / 0, never moves 9 / 7 /
  1 / 0, standing still 0; 1.2-3.2 minutes a match. `fake_sender --script tennis`: the lob is a 43-degree scoop, each cycle ends with a swing straight up (must
  miss). *Graphics* (docs/hardware.md "Tenis!'s frame on the box"; `tools/dev/box-tennis-gpu.sh -g 300`: uncapped, the
  GPU pinned so the heat can't move the clock; the hot box sits at 200-250 MHz): the project's `androidBlitType` was
  Always (Unity copied every frame to the screen, on top of URP's copy for the render scale): now Auto (`BoxBuild`);
  the stadium's lawn no longer lies under the court; Tenis! draws 0.75 of the screen's resolution straight into the
  screen's buffer and the TV's display hardware scales it (`BoxGraphics.Settings.ScreenScale`, `Screen.SetResolution`,
  PerfLab `res=` / `boxres=`), the split screen too. Hot box, uncapped: 21.0 -> 28.4 fps; HUD text a touch softer.
  Skinning on the CPU was tried and changed nothing (back to GPU batched); the chibis still cost ~1-3 ms each whatever
  their triangles (not understood: leads in docs/hardware.md). Then the same scaler for every game below the screen's
  resolution (the user: "yes, do the others"): as played on the hot box (82-83 °C), Tenis! 29.7-29.9 fps, Vrapo!
  29.5-29.9, Shpata! 29.2-29.7, Fluturo! 29.7, Vishu! 25-25.5 (now CPU-bound: main and render threads ~21-22 ms),
  Sporte!'s football ~26 (unchanged at that heat, still GPU-bound). **Not yet**: the user's feel of all this on the box,
  the Albanian (`SwingSideways`; `HowTo` and `SwingWhenClose` now say "anash"), Sporte!'s GPU cost (nine athletes, the
  crowd) and Vishu!'s CPU time on a hot box.
  **Round 4 (2026-11-06 evening, the user after round 3 on the box: "the racket bigger and the character smaller; the
  player should reach into the ball like the Wii game (the area where close enough counts), outside it or mistimed the
  ball goes through; the model struggles with hands, they jump, which shows in the rackets: fix it in the model, or else
  read when a jump happened and don't register it; register even small hand moves, like a Wii remote: moving the whole
  arm is tiring").** *The hands*: the jumps are the pose model's own (our NPU engine matches MediaPipe; full glitches as
  often as lite on the routine's identical frames), so the swing detector now tells them from moves itself
  (`SwingDetector`, rewritten; details in its doc and docs/hardware.md "Hand glitches and Tenis!'s swings"): **raw
  samples** (the low-lag filter smeared a one-frame jump into a two-frame "move") through a **median of three** (one
  sample late: a one-frame glitch never gets through), a swing measured from where the hand **trustably** was (an anchor
  0.12 s back), started only when **both of its last two steps** carry it, confirmed while its fastest moment is looked
  for (no extra delay): at least `MinTravel` 0.18 torso lengths, **not all in one step** (`MaxStepShare` 0.7 + 0.05),
  **not straight back** (two samples under 0.45 of the way), unsure samples (visibility < 0.8) wait a sample more,
  under 0.6 skipped; a rejected move's samples lose their trust (`Ignored` counts them, the game logs "A hand's jump
  ignored"). Measured (`tools/motion/swing_replay.py`): on the box's hand-circle recordings 9 of the old detector's 15
  swings were glitches (some straight up: an instant miss), on a still hand with simulated glitches (flips of 0.4-1.2
  torso lengths for 1-3 frames) the old swung at every one, the new at none; a one-frame glitch in the middle of a swing
  keeps it, a two-frame one loses it (~1-2 % of swings). *Small moves*: `SwingSpeed` 3 → **1.6** torso lengths a second;
  a swing under `StrongSpeed` 3 is **`Gentle`**: it hits a ball in reach like any swing (softly), anywhere else it's the
  hand moving (no toy swing, no sound, no "too early", and up or down it is **no instant miss**: only a strong swing up
  or down misses at once). Forearm flicks of 0.25-0.5 torso lengths (12-25 cm) in 0.15-0.25 s: found 38-40 times in 40
  (the old: never), at 15 samples a second (two players) 19-37. On the routine (two models on the same frames as ground
  truth): 123 real swings and 2 glitches against the old 63 and 5. *Sizes*: chibi `Scale` 0.92 → **0.78** (~1.2 m),
  racket `RacketScale` 1 → **1.55** (~1.05 m long, as long as the toy is tall, like a Mii's). *The reach*: every hit's
  swing carries where the ball is met (`CourtPlayer.SwingPoint`, `SwingReaches`: the player's early, on-time and late
  hits, the serve's toss, the computer's returns), and `TennisPlayerView.ReachPose` solves the arm and the racket as two
  bones so **the racket's head is on the ball** at the contact (the mitten below the line to it), the backswing and the
  follow-through turning round the shoulder from there (the head sweeps through the ball); a ball beyond comfortable
  reach makes the toy **lunge** toward it (up to 0.5 m, leaning) or **hop** (up to 0.6 m); the head never sinks into the
  court (`AboveGround`). A ball the toy stands too far from (`ReachTolerance`) gets no reach: it swings short and the
  ball goes by; mistimed swings were already misses. The toss: 0.95 m at 4.4 m/s (top ~1.9 m: the smaller toy reaches it
  with a little jump; was ~2.3 m). Tests: `SwingDetectorTests` 13 (flicks, glitches of a still hand, a glitch mid-swing,
  15 Hz), `TennisMatchTests` +2 (`AGentleFlick_...`, `EveryHit_ReachesIntoTheBall_WhereItLeavesFrom`), PlayMode
  `Practice_SmallFlicks_HitTheFeeds_GlitchesAreIgnored` (`fake_sender --script tennis_flick --glitches 1`: flicks serve
  and return 6+ feeds, the small flick up is no miss, glitches ignored) and the racket test's reaches (the head within
  0-0.05 m of the ball near, far, high, low, backhand and serving; `racket_reach_*.png`). **Not yet**: the user's feel on
  the box (do flicks register reliably with the real camera, does anything else now swing, is 1.55 the right racket and
  0.78 the right toy), small flicks at TV distance with a child (the 0.18 travel is ~9 cm for an adult: a child's is
  less), the lunge's look from the game's camera in a real rally.
  **Round 5 (2026-11-07, user: "the perspective a bit more tilted, more top down view available; the characters move on
  their own and go near to where the ball will end up, depending on the difficulty not all balls need to be reached; 3
  difficulties, a single game and a tournament mode; more styling, shadows, effects, shaders, without breaking the
  performance").** *The toys run to the ball by themselves* (Wii): the body's steps move nothing any more
  (`BodyToCourt`, `SetWanted`, the assist and `LockBefore` are gone; the PlayArea needs only room for a swing, side 0.3).
  As a shot leaves the other side, `PlanHuman` picks the hitter (the net player volleys a ball within `NetReach` if it
  gets there in time, else the back player after the bounce), sets it off after its reaction (`CourtPlayer.Reaction`,
  `RunAt`) at its speed (`CourtPlayer.MaxSpeed` = `TennisLevel.HumanRunSpeed`), and `Contact.Reachable` says whether it
  makes it (`Reaches`: distance / speed + reaction + `RunMargin` 0.15 s); a ball it can't reach goes by, the toy dives,
  a popup says "S'e arriti!" (no blame hint). After its own shot a team covers the court (`Cover`, `SetHomes`: the net
  player waits `NetOffset` beside the back player, together on its player's `NetSide`). Balls out of reach for a player
  swinging well (`TheDifficulty_DecidesWhetherEveryBallCanBeReached`, the final's strength): Easy 0 %, Medium ~2-3 %,
  Hard ~15 %. *Difficulties and formats*: `TennisDifficulty` Easy / Medium / Hard and `TennisFormat` Match / Tournament;
  everything in `TennisLevel` follows one strength (0 gentlest .. 8 strongest, the old ladder's match number - 1;
  `StrengthFor`: Easy 0 / 0.5 / 1, Medium 2.4 / 3.4 / 4.4, Hard 5.2 / 6.6 / 8 per tournament round; a single match plays
  round 1's strength against a random pair); `WideFrom` now 99 / 0.9 / 0.75 / 0.6 by strength (with the toys running
  themselves the timing is all there is to get right, so the strongest pairs punish the window's last 40 %); the
  computer's error rate returning a serve is 0.35 of its usual (a player who only serves won an Easy match otherwise).
  Seven pairs (14 names: Sara, Arbi, Ema, Dion new, with their own looks in `TennisLooks`; the user should review the
  names). `TennisTournament` (engine-free, `TennisTournamentTests`): eight teams drawn at random, quarter-final,
  semi-final, final; the other pairs' matches decided by chance as the rounds go; lose once and you're out (the rest is
  played out so the bracket has a champion). Cups won counted per difficulty (PlayerPrefs `tennis.cups[2].<d>`, solo and
  together apart), shown on the menu. Balance (`Balance_ByDifficulty`, wins of 10, quarter-final / final): perfect
  10/10 everywhere; good 10/10, 10/10, 10/7; child 9/9, 9/6, 1/0; struggling 5/2, 0/0, 0/0; doing nothing 0.
  *The menu* (`TennisMenu`, on the waiting screen; the remote's ↑ ↓ pick a row, ← → change it, all remembered: players,
  game, difficulty, view): replaced the mode bar; ↑ swaps hands only in a match now. Phases: Waiting (menu) → Intro →
  Playing → MatchEnd → Bracket (a tournament between matches: `TennisBracketView`, hands up for the next) → Results
  (hands up = the same again, an arrow = back to the menu; a won tournament shows the 3D cup spinning with confetti, a
  lost one the bracket). `TennisGame.Continue()` / `TennisMatch.Award()` are test hooks (a tournament played through in
  `Menu_ATournament_TheBracket_TheCup`). *The camera* (`TennisGame.CameraViews`): classic 8.2 m up, 12.6 m behind the
  baseline, 22 degrees down, 34 degrees of view (Wii's: 6.47 / 12.3 / 17 / 32); from above 13 m, 14.1 m, 33, 38; the
  split screen's halves 8.4 / 10.6 / 26 / 46 and 12.5 / 11.5 / 36 / 48; eased into over 0.5 s when the menu changes it.
  *Styling* (`TennisFx`, `TennisStadium`): no real-time shadows anywhere (the scene's light too, so the editor matches
  the box); the sun's shadows as soft shapes on the grass: each toy's body stretched away from the sun as tall as it
  stands (hops and lunges move it), its feet's darker contact, its racket head's, the ball's (one dynamic mesh, one
  draw call: `TennisArt.ShadowAtlas`), and the fixed ones (net with its tape line, posts, the umpire on the chair,
  benches, the walls along the near end and the right side and behind the far one: one static mesh); the court a grid
  (`TennisStadium.Grass`, 2400 vertices) with worn grass baked into its colours (behind the baselines' middles, along
  them, the net players' spots, the service T) and darker at the walls' foot; a world-space grass detail in Tundu/Toon
  (`_WORLD_DETAIL`, the one texture sample the palette took anyway; `GrassDetail.asset` made by the scene builder from
  `TennisArt.GrassDetail`); particles (one system each): stars at a hit, grass and a scuff puff at a bounce, grass
  kicked up when a toy sets off or pulls up, confetti for a won match (a shower for the cup); the ball's glow (warmer
  and bigger after a perfect or power shot), its streak widening with its speed; a won tournament's results show the cup
  in 3D (`TennisArt.CupMesh`: a lathe with handles, gold with gloss on the actors' Tundu/Toon) spinning in front of the
  camera with confetti. Also: a swing up or down misses at once only when it's clearly strong (`VerticalMissSpeed` 3.6
  torso lengths/s; the arm coming back down after a small flick up measured 3.1 in the flick test with glitches and
  missed a ball). **On the box** (docs/hardware.md "Tenis! round 5 on the box"; `box-tennis-gpu.sh -g 300`, new configs
  `nofx`, `noshadows`, `nodetail`): the shadows, particles and glow cost nothing measurable (22.6 fps with and without),
  the grass detail ~0.6 ms; the first menu's IMGUI cost ~13 ms a frame (rounded, shadowed panels and outlined text),
  slimmed to ~4.2 ms like the old screen (waiting screen 28.5 fps uncapped at 300 MHz); a live match (the scripted player
  over Wi-Fi, the hot box at 80.6 °C, the 30 fps cap) 29.7-30.0 fps with 0-1.7 % of frames late. The box runs this build
  (2026-11-07 ~13:45). **Not yet**: the user's play test (the self-running toys' speed per difficulty, the camera views,
  the look of the shadows, the tournament's length), the Albanian (`TennisStrings`: HowTo, AimHint, KeyboardHelp,
  OutOfReach "S'e arriti!", the menu rows "Lojtarë / Loja / Vështirësia / Pamja", "NDESHJE / TURNE", "LEHTË / MESATARE /
  VËSHTIRË", "KLASIKE / NGA LART", MenuHint, StartHint, HandHint, RoundName, TournamentOf, SingleOf, ThroughTo, OutIn,
  Champion "Kampion! / Kampionë!", WonTheCup, TournamentWinner, NextMatch, RaiseForNext, Cups, AgainOrMenu) and the new
  names Sara, Arbi, Ema, Dion, two players trying the self-running toys.
  **Round 6 (2026-11-07 afternoon, the user after round 5: "the second character per team is not needed anymore, remove
  and work the game logic against it; the racket bigger, more like a tennis one with strings stylised, not a ping-pong
  flat surface; many cases where my shot goes outside, what is the logic? I want the upwards moves to count as well, at
  least shoot; horizontal swings depending on the speed should provide the best shot; the right hand's shots left to
  right when the ball comes to the left side are not registered that well").** *Singles* (`TennisMatch.Singles`: Solo
  and Versus; Together stays doubles, its second toy is player 2's): one player a side on the singles court
  (`TennisMatch.HalfWidth` 4.115: targets, in and out, the computer's aims scaled by `WidthScale`), each team's second
  `CourtPlayer` (Players 1 and 3) off the court (`Active` false: never placed, moved, drawn, swinging or volleying), no
  volleys, the back player plays every ball after the bounce. Opponents are single players (`TennisLevel.Singles`,
  `Opponent` = one of the fourteen, `OpponentCount`, `OpponentName`; pairs stay for Together: `For(..., singles:
  false)`); a tournament draws seven of the fourteen (`TennisTournament(..., singles)`, `NameOf`); `TennisLooks.Opponent`
  dresses them; the scoreboard has one chip a side; `TheyWon(them, one)` "Fitoi" / "Fituan". *Why shots went out* (the
  user's log: a Hard single match, strength 6.6): `TennisLevel.WideFrom` sent a hit timed in the window's outer share
  wide: the last 10 % on Medium, 25-40 % on Hard, and the user's hits came -0.53..+0.23 of the window (early), at Hard's
  0.6 edge. Now never on Easy, Medium or Hard's quarter-final, beyond 0.85 from strength 6.5 (Hard's later rounds), never
  a lob or a smash; a mistimed hit is weaker instead (`MistimedFrom` 0.4 down to `MistimedPower` 0.55 of its power at
  the edge: slower, shorter); versus `VersusWideFrom` 0.75 (was 0.6). *The best shot from speed*: `Shot.Power` is the
  sideways speed (3 → 10 torso lengths/s), less when mistimed; perfect (15 % faster, the hardest to return) needs a
  sideways swing of at least `PerfectPower` 0.35 (5.5 torso lengths/s) right on time (it was the timing alone). *Up
  counts*: the racket hand up at a ball in reach lobs it (`KindFor`: vertical share > 0.6; `DepthFor(Lob)` 9.8-10.8 m),
  gentle or strong, never perfect; only a strong swing down (>= 3.6) misses at once (`SwingSideways`: "Lëkunde dorën
  anash ose lart, jo poshtë!"). *Backhands*: the camera side sees them (`swing_replay.py`'s mirror of the detector:
  backswing then stroke 40/40 at 30 samples a second, backhand or forehand), the rules lost them: a swing toward the
  ball's side more than 0.12 s early was the backswing (no hit; the user's sense of the ball's side is the court's, the
  toy's is where it stands), and a rising backhand steeper than 50 degrees was a vertical miss. Now a swing toward the
  ball's side in reach holds the ball (`SwingResult.Held`): a stroke after it hits instead; with none it hits once the
  camera has shown `HeldSeen` 0.1 s past the ball's arrival, from where the ball was met (like any swing that reached
  the game late), its timing kept >= -0.6 (never wide); out of reach it's still the `Backswing`. *The computer*:
  without wide shots a good simulated player won Hard's final 80-4 (a diagnostic: the computer failed ~15 % of its
  shots, the self-running toy ~2 %), so strong opponents now attack a weak ball (`Shot.Power` < `WeakShot` 0.35,
  or a lob) with `TennisLevel.Attack` (0.05 + 0.08 k, at most 0.7): flat, `Speed` x 1.15 + 1.5, into the far corner
  (`HalfWidth - 0.35`); they're steadier (`Return` 0.8 + 0.035 k up to 0.98, `Pressure` down to 0.2, `Error` 0.14 - 0.018 k
  down to 0.02), and the player's toys are slower against them (`HumanRunSpeed` 10.5 - 0.5 k, at least 6.8;
  `HumanReaction` 0.06 + 0.022 k, at most 0.22; static fields `TennisLevel.HumanRunSpeedMin` ... for tuning). Balance
  (singles, `Balance_ByDifficulty`, wins of 10, Easy / Medium / Hard quarter-final and final): perfect 10 everywhere; good
  10 everywhere, Hard's final 87-29 in points; child 10/10, 9/4, 1/0; struggling 3/2, 1/0, 0/0; standing still 0. Out
  of reach for a player swinging well (`TheDifficulty_DecidesWhetherEveryBallCanBeReached`): Easy 0 %, Medium 0 %, Hard's
  final 27 % (its long rallies get to the top pace; a good player's ~2 %). *The racket* (`TennisPlayerView.RacketMesh`):
  `RacketScale` 1.55 → 1.85 (~1.2 m, as long as the toy is tall), a tube frame round the oval (36 x 8), an open throat of
  two arms, 4 mains and 5 crosses ~3 cm thick (drawn) in cream with the court showing between them; a far, low-detail
  toy's has its strings on a pale bed (`Low` swaps the mesh: open strings that small would shimmer); the shadow follows
  `RacketHead`. Tests: EditMode 208/210 (2 explicit skipped); new `Singles_OnePlayerASide_OnTheSinglesCourt`,
  `ABackswing_HoldsTheBall_TheStrokeAfterItHitsInstead`, `AMistimedShot_IsWeaker_NotOut`,
  `ASwingDown_MissesAtOnce_UpLobsIt_TheFreeHandsIsNothing`; the doubles volley tests moved to `TennisTwoPlayerTests`
  (`Together_...`); the 7 Tenis! PlayMode tests pass (practice: 21 hits, 6 of them lobs from the scoop and the swing
  straight up; flicks: 14 hits, 10 glitches ignored; screenshots `tennis_coming.png`, `tennis_bracket.png` with single
  names, `tennis_versus.png`, `racket_*.png`); quick rule tests 63. **On the box** (docs/hardware.md "Tenis! round 6 on the box"): Tenis!'s screen uncapped at a pinned 300 MHz
  30.0-30.2 fps (round 5's, four toys: 28.5), the split screen 26.5 (two toys drawn twice; round 4's four at scale 0.7:
  23.2); a live match (the scripted player over Wi-Fi, the 30 fps cap, 71-72 °C) 29.7-30.0 fps, 0-1 % of frames late; a
  gentle swing straight up lobbed the ball there. The box runs this build (2026-11-07 19:10). **Not yet**: the user's play test (does the
  backhand register now, the held backswing's aim, the lobs, the difficulty without wide shots, the racket's look), the
  Albanian (`HowTo` now singular "Lojtari yt vrapon vetë te topi...", `SwingSideways`, `TheyWon` "Fitoi").
  **Round 7 (2026-11-07 evening, the user after round 6: "too many lob shots, be more lenient; at minimum in all game
  modes vs cpu make the bot hit at least 5 times, after that it should depend on the difficulty; it's not fun to make
  1-hit points all the time").** *Lobs* (their log: 13 of 22 hits were lobs; their swings went up at 70-90 degrees): a
  swing up, tilted or straight, is a topspin drive as fast as the hand (`Swing` keeps its vertical share under 0.5
  unless it's a gentle scoop at least `LobFrom` 0.9 steep; `KindFor` lobs only from there; a perfect shot can be any
  but a lob); the keyboard's W sends a gentle scoop. *The computer's sure first returns* (`TennisMatch.SureReturns` 5,
  `SureRally`, `CpuReturnsThisPoint`; solo and together, never practice or versus): in every point its first five
  returns are sure: it gets to the ball (`CourtPlayer.Hurry`: a quicker speed for that ball, at most `HurryLimit` 18
  m/s) and returns it (no return roll, no error) within the player's reach (no attack, no corner, nothing past the net
  player); the player's toy gets to those balls and to the computer's serve (`_sureBall`: hurried, never "too far"),
  and the player's shots don't go wide; after the fifth the difficulty decides as before. The player's own misses still
  end a point. With every point six exchanges or more, a child's misses weigh more: against the computer the window is
  longer at the gentle end (`TennisLevel.WindowScale` 1.5 up to strength 0.5, down to 1 at 5; versus keeps 1, else two
  good simulated players rallied 250 shots) and the computer's return chance starts lower (`Return` 0.72 + 0.045 k, at
  most 0.98). Balance (wins of 10, Easy / Medium / Hard quarter-final and final): perfect 10 everywhere (2.9-3.8 min a
  match); good 10 everywhere, Hard's final 87-30 in points (6.4 min); a child who swings at every ball (new profile
  "careful child") 10/10, 10/9, 5/0; the child who lets one ball in ten go by 6/4, 2/1, 0/0; struggling 0 everywhere
  (13 points won of 93 on Easy); standing still 0. Matches got longer (a good player's 3.3-4 min, were 1.6-2.8).
  New test `TheComputer_ReturnsTheFirstFiveBalls_OfEveryPoint` (every difficulty, solo and together: a point the player
  wins comes after at least five returns; one they lose before that is their own miss, never a ball out of reach or a
  shot out). Tests: EditMode 209/211 (2 explicit skipped), the 7 Tenis! PlayMode tests pass (the versus test now
  checks that only whoever's turn it is serves: player 1 had won their game 4-0 and player 2 served the next, which the
  old check called wrong), quick rule tests 64. Their round-6 log replayed through the new rule: 1 lob of 20 hits
  (was 12). **On the box**: a scripted live match 29.7-30.0 fps, 0-1.7 % of frames late; the box runs this build
  (2026-11-07 20:00). **Not yet**: the user's feel of the sure rallies (five may be too many on Easy for a
  small child, too few on Hard), whether swings up now feel right as drives.
- **Two players (2026-11-05, user: "make a 2 player system; see the box's components and power; optimize to handle 2
  people if possible; evaluate; make tennis the first 2 player game, played like the Wii tennis version").** Details in
  docs/hardware.md "Two players on the X98K Pro". **The evaluation**: the RK3566 has one NPU core; two people = two
  landmark runs in a 33 ms frame. `pose_npu_test --people 2` (box idle, app paused): lite both every frame 28.0 ms of
  NPU thread a frame (max ~35 fps), full 35.5 ms (can't keep 30); in a game each lite run is 15-19 ms (memory bus), so
  both-every-frame doesn't fit beside a 3D game. And the box is already at its heat limit with one player: Tenis! with
  one real player at 83 °C ran 24.5-27.7 fps, GPU throttled to 200-400 MHz at 94-100 %, NPU 40-48 %: **the GPU, not the
  NPU, limits Tenis!**. **Decided: the NPU's landmark runs are a shared budget** (`BoxGraphics.Settings.TwoPlayerRuns`,
  30 a second: the two take turns, each ~15 poses a second, the NPU exactly as busy as with one player); the camera-clock
  interpolation draws them smoothly (~33 ms more lag than 30 Hz). **The engine** (`pose_npu.cpp`, `pose_npu.h`): up to
  `kMaxPeople` 2 tracks (crop box + MediaPipe's filters each, a stable track id), the detector's weighted NMS returns
  every person (not just the best), new people only where nobody is followed (`SamePerson`: hip centres within 0.4 of
  the smaller body's alignment distance; MediaPipe's IoU > 0.5 would merge two people side by side), duplicates dropped
  (the newer track), the detector looks for a missing second person every 0.5 s (a 45 ms frame then), `Schedule` = the
  budget (token bucket, taking turns, one person never waits), `SetPeople(max, rate, twin, keepTrack, bench)`: keep =
  the track that stays when dropping to one, twin = one person's landmarks twice (their cost), bench = fixed crops run
  as if people (cost with nobody in front: PerfLab `npubench`). JNI packs a frame of up to 2 people
  (`pose_npu_jni.cpp`); Java `NpuPose`, `Pose` (now people), `PacketWriter` (`tid`), `PoseEngine.setPeople` (MediaPipe
  fallback: `setNumPoses`), `MotionService.setPeople`. **Protocol** v1.1 `people[].tid` (docs/protocol.md). **SDK**:
  `BodyTracker` matches by `tid` first (a packet may carry only one of two people), then nearest pairs first (globally,
  not body by body), never across two live tracks; `PlayerSlots` (new, engine-free): player 1 = whoever stands on the
  left (re-sorted while waiting, locked in a match, a newcomer takes the free number), each one's calibration, lateral
  offset, hands-up "ready", placement, `TooClose` (< 1.4 torso lengths apart); `AndroidMotionService.SetPeople`
  (`PeopleOverride` for PerfLab), `BoxGraphics.Apply` asks for one person on every scene load. **Tenis! for two**
  (`TennisMode`: Solo, Together, Versus; Wii checked: MiiWiki/GemuBaka): **the remote chooses the game** (since
  2026-11-06; before, two people in view switched it): three cards at the bottom of the waiting and results screens
  (`DrawModeBar`: 1 LOJTAR / KUNDËR / BASHKË, names only, narrow enough to clear the skeleton mirror; the two-player
  screens describe the chosen game at the top), ← → cycles (`ChooseMode`, `NextMode`, PlayerPrefs `tennis.mode`; only
  solo without a camera service); in a two-player mode a card each on their side, both raise their hands (or OK once
  `BothCanPlay`) = start, one alone with raised hands plays solo after `JoinWait` 4 s if nobody else is there
  (`Tracker.SetActive`). **Versus**: singles since 2026-11-07 (Tenis! round 6; before, each player moved both toys of a
  doubles team), the screen split down the middle (player 1's half on the left, where they stand; each half from
  behind its team: `splitFov` 46, `splitHeight` 7.4, `splitBack` 10.6, `splitPitch` 22; a second camera made at runtime;
  the stadium's mirrored near end shown only then: `TennisStadium.ShowNearEnd`), scoreboard across the middle, calls in
  both halves, hints in the player's half; points can end on mistakes: `TennisMatch.VersusWideFrom` (0.6 until round 6,
  now 0.75: two good simulated players' longest rally ~50 shots a match on Easy's toys; 0.6 gave ~26, none ~200), no
  ladder (one match, rematch with hands up). **Together** (the only doubles since round 6): one
  toy each against the ladder, they change places every game except that whoever's turn it is to serve plays from the
  back (player 1 serves first; the net player's reach `TogetherNetReach` 1.7 m: else it got one ball for five of the
  back player's), a call when they change places, its own best (`tennis.best2`). Player 2 is a sky-blue headphones kid
  (`TennisLooks.Player2`, `Partner2`; nobody on the ladder wears sky blue). Each player's own swing detector, hands,
  handedness (J swaps player 2's), hints; a swing hits only a ball coming to that player's toy
  (`TennisMatch.Swing(slot, ...)`), the end of a point waits for the receiving player's camera (`SawUntil(slot, ...)`).
  Two players step 0.75 torso lengths to the court's side (solo 0.9: they share the room). The rules refactor kept solo
  identical (the balance table's numbers unchanged to the decimal). Tests: `TennisTwoPlayerTests` 9 (versus and
  together: own toys, own balls, the camera wait, whole matches between simulated players), `PlayerSlotsTests` 6,
  PlayMode `TwoPlayers_Versus_...` and `TwoPlayers_Together_...` with `fake_sender --script tennis_duo --alternate`
  (two scripted people, one per packet with track ids, like the box): screenshots `tennis_two_waiting.png`,
  `tennis_versus.png`, `tennis_together.png`. PC: `main.py --num-poses 2`. **Measured on the box** (`tools/dev/box-two.sh`
  with PerfLab `npubench=1`: fixed crops run like people, nobody needed; the box at 83 °C): one person 26.8 fps, two
  taking turns **27.2** (no cost), two at 45 runs/s 23.3, both every frame 24.0 (only ~23 poses/s each), **split screen
  21.6** (the four chibis drawn twice are all of it: hidden 27.0; the crowd hidden 24.2; scale 0.65 23.7). Done after:
  the crowd's heads 6 x 4 (was 8 x 5), the split screen at render scale 0.7 on the box (`Settings.SplitScale`,
  `BoxGraphics.SetSplit`): split **23.2 fps**, one view 27.3 (re-measured; since 2026-11-06 the split screen uses
  Tenis!'s screen scale 0.75 instead, not measured with two players yet). The box runs the build of 2026-11-06 21:57 (Tenis! round 4: the glitch-proof swing detector, gentle flicks, the
  smaller toys with bigger rackets reaching into the ball; on the box Tenis!'s waiting screen ran a steady 30 fps, no
  errors; not played yet). PerfLab also has `split=1` (`PerfSwitches`: Tenis!'s split screen
  without two players).
  **Round 2 (2026-11-06, the user after playing with a second person: "lower the quality of the graphics a bit in 2
  player mode and do more general optimizations; make 2 player mode selectable by the remote; when 2 people get close
  to each other then separate, the assignments change (I went from p1 to p2)")**. The swap: a track's next crop slid onto
  the other person, both tracks followed one person, `DropDuplicates` ended the newer and the survivor kept the wrong
  id. Fixed twice: `pose_npu.cpp` `Follow` ends a track whose next crop lands on another live track's person (the older
  keeps it, the detector finds the other again), and `PlayerSlots` makes a number a side of the room during a match
  (bodies the wrong way round by > `SideMargin` 0.3 torso for `SideSwapAfter` 0.25 s change slots, each slot keeping its
  calibration; `SideSwaps` counts). Graphics: a **low-detail chibi** (`art/chibi/build.py -- --low --ao --export` →
  `Chibi_Low.fbx`, `kit.LOW_DETAIL` 0.6 via `kit.lod()`: a dressed chibi 13.4k triangles instead of 33.6k), `Chibi.Low`
  (a second skinned mesh on the same bones, built on first use, bones matched by name), Tenis! draws the far team low
  always and all four low in the split screen (`TennisGame.ApplyDetail`, PerfLab `chibi=full|low`); spectators' hair is
  the top of their head (`PartMesh.Add(..., top, topFrom)`, one ball instead of two), tree crowns 12-segment balls
  (`PartMesh.Ball`). **Not measured on the box yet** (the run was cut short by a restart from the remote's power menu
  during the warm-up; docs/hardware.md "Two players, round 2" has the commands). **Not yet**: two real people trying the
  fixes (crossing, standing close), a low-detail chibi in other games, the user's review of the new Albanian
  (`TennisStrings`: SoloMode "1 LOJTAR", VersusMode "KUNDËR", VersusModeSub, TogetherMode "BASHKË",
  TogetherModeSub, ChooseMode, StandStill, RaiseYourHands, Ready, StepApart, WaitingFor, PlayingAloneIn, ComeBackPlayer,
  HoldStillPlayer, Us, UsVersus, PlayerVersusPlayer, WeWon, Won, SwapPlaces, AtTheBack, Hits, PerfectShots, Rematch).
- **Volejboll! (game 9, 2026-11-07/08, user: "update the volleyball game: take the tennis game changes, align it: the
  difficulty modes, effects, vs and together modes with 2 people; not time based / quick event based: two hands up raise
  the ball for another person, one hand hits it to the other side, the arm's speed is the hit's speed; to defend the
  player must do another movement (not sure what the model reads reliably); on tennis the hits in 2 player mode are
  processed slower, take that into account").** It replaced Sporte!'s volleyball (a ring closing on the ball, any arm
  move at the moment: deleted with its tests; Sporte! keeps the 100 m and football) and is a game of its own like
  Tenis!: its own home tile (nine tiles now), scene `Games/Volley/Volley.unity`, assembly `MoveIt.Games.Volley` (it
  references Tenis!'s for `SwingDetector` and `TennisArt`'s drawn-in-code cup, dot and shadow atlas, and Sporte!'s for
  the beach venue, `Daylight`, `Fans`, `SportsBalls`). Beach volleyball two against two with **the chibis**
  (`VolleyPlayerView`, `VolleyLooks`: tees and shorts; the player's team red, player 2's sky blue, each computer pair its
  own colour) on Sporte!'s beach, **a children's net at 1.5 m** (the chibis are 1.52 m, big heads, 0.34 m arms: the venue
  was rebuilt by `art/sports/venues.py` with `NET_H`; the set is met over the head, the spike at the top of a ~1.1 m
  cartoon jump, `VolleyMatch.SetHeight` / `SpikeHeight`).
  **The moves** (`VolleyMoves`, engine-free, on the camera's raw samples, a median of three per wrist): **both hands
  together low in front = the receive** (the defend move: both wrists 0.3 torso lengths below the shoulders, at most 0.5
  apart, in front of the body; arms hanging are ~0.7 apart), **both hands up = the set** (both wrists 0.3 above the
  shoulders), **one hand's quick move = the hit** (Tenis!'s glitch-proof `SwingDetector`, one per hand, any direction;
  not when the other hand moved along (a pose being made: `StillShare` 0.7), not steeply up (`UpLimit`: the wind-up)).
  A pose counts after 3 samples in a row. Why the receive is "hands together low": it's the real move (Kinect Sports used
  it), shoulders and wrists are what the model gives best, and it's held, not timed; a crouch was the alternative (the
  most reliable joints) but a jump's knee dip reads as one. Measured on simulated arms with the box's glitches (2 a
  second, a hundred times its usual rate): 0.17 false hits a minute at 30 samples a second, 0.42 at 15, a pose for 0.8 /
  1.8 % of the glitches (only when two come back to back; a pose only ever helps), a held bump stays held 90-94 %.
  **The rules** (`VolleyMatch`, engine-free, tested; positions as Tenis!'s: x across, z toward team 1, the net at 0): the
  toys run to the ball by themselves (the difficulty's speed, a dive covers the last 1.2 m); three touches a side, the
  toys taking turns: a free ball, a tip or a serve coming over the net is received **only with the bump** (both hands
  up or one hand at it = "Gabim!", a shank out), the bump sends it to the teammate by the net, **both hands up set it**
  for the first toy (a bump
  there sets it too, less well; one hand pushes it over), the set comes down at the net and **one hand spikes it** as fast
  as the hand went (`SpikeSlow` 9 .. `SpikeFast` 19 m/s; strong and on time = "Smesh i fortë!"; a pose there just pushes
  it over softly). The player's hits always clear the net and land in (into the open court, the swing's side leans it).
  One player plays both of their team's toys; two players together a toy each (player 1's covers the left half: the ball
  to your half is yours to receive, your partner sets it, you hit it); versus each plays their whole team. **Not timed
  like a quick-time event**: a pose counts if seen any time from the previous touch until the ball arrives (+0.15 s), so
  players get ready early; a hit counts from 0.65 s before to 0.25 s after (scaled up to 1.4x on Easy). The best move for
  the touch wins (`Priority`: bump at the receive, set at the second, hit at the third), a pose held on from the last
  touch isn't a new move (it must be let go 0.25 s and taken again: `PoseGap`). **The camera's lag** (the user's tennis
  point): a pose seen before the ball arrives is played at once (no lag at all); the set is floaty (0.4 g) and **its top
  is where the hit meets it**, so a hit the camera reports 0.2-0.3 s late still finds the ball ~0.1 m lower; a ball with
  no move yet waits for the camera (`SawUntil`, the reader's `Settled`), then is played from wherever it is (a "Shpëtim!"
  save from low down). Two players (each seen ~15 times a second): the ball 0.88x as fast, the late windows +0.1 s, the
  wait for the camera longer (`TwoPlayerPace`, `TwoPlayerLate`). Tested at 15 samples a second (sims and the reader).
  **The defence is a choice at the moment of the other side's hit** (2026-11-08. The first fix for the user's "the
  player can keep the hands together before the ball is shot" made the bump count only after their hit; the user: "not
  good: at the moment the ball is shot the player chooses between low hands save or high hands block on the net;
  depending on the type of shot the chosen save is correct or wrong; search alternatives, evaluate, implement, don't
  ask". Researched: Kinect Sports blocks with both arms up at the net when they spike; Switch Sports' block stops a spike
  and sends it back, a lob or an early hit goes over it; Sports Champions' volleyball was criticised for prompting each
  action, which takes the challenge away. The user's idea is the real block/dig choice and was kept, made fair by tells.)
  As a team sets up its hitter at a player's team (`VolleyMatch.Coming`, a `Threat`), the player shows **both hands up =
  block** (the set's pose) or **hands together low = receive**; the pose the camera saw at `Threat.DecideAt` =
  `BlockLead` 0.15 s before the hit (+0.1 with two players) is the choice (`ChoiceAt`: held then, within 0.3 s; the
  computer's hitter hangs at the top of its jump up to `DefenceWait` 0.2 s for the camera's news). The attack decides what
  was right (`Attack`): a **smash** is stopped only by the block (off the blocker's hands straight back down into their
  court: "Bllok!", the player's point, `Touch.Block`, `PointReason.Block`; received low it flies off the arms: "Shumë i
  fortë!", `TooHard`; nothing: it lands), a **tip** (soft, over the block, short) goes over a block ("Mbi bllok!",
  `OverBlock`, their point) and is received low (a tip read right is always reached), a **free ball** (no jump: a push, a
  serve) is just received, any time before it comes. The computer decides at its set (`PlanCpuAttack`): the first
  return of every rally a free ball, then free balls `VolleyLevel.FreeBall` of the time (0.42 the gentlest .. 0.12), else
  tips (`TipShare`: none on Easy, 0.13 Medium's first round .. 0.4) or smashes; its attacking set goes high and comes down
  to the hitter's jump (`SetAbove` 0.75 .. 0.3 m: ~1.4 s from the set to the hit against the gentlest pair, ~1.1 s the
  strongest; a set topping out at the hit gave 0.4-0.7 s, too little for a child). **How it's read** (`TellAt`): the
  hitter's look from the set (a smash: the arm drawn back high, a big jump, a whoosh; a tip: the hand forward, a little
  hop; a free ball: hands up, no jump), on Easy also a red "!" over a smasher (`ShowTell`); on Hard 40-50 % of the tips
  are disguised, looking like smashes until `TellBefore` 0.65-0.5 s before the hit; from Medium the pair reads the player
  (`Adapt` 0.3-0.35: tips up to that much likelier against a player who keeps blocking, `BlockRate`). Over the toy that
  would block, two discs show the choices (BLLOKO / PRIT), the one the camera sees lit; the PRIT icon never shows for a
  smash. Versus: a player's hit at the third touch is a smash, or a tip when gentle (`TipPower` 0.3), and the other
  player chooses as it comes; together: the defender is the player whose half the hitter attacks (their toy blocks).
  **Like Tenis!**: `VolleyMenu` (1 LOJTAR / KUNDËR / BASHKË, NDESHJE / TURNE, LEHTË / MESATARE / VËSHTIRË, KLASIKE / NGA
  LART; remembered under `volley.*`), `VolleyLevel` (strength 0..8 per difficulty and round: dig chance, pressure, attack
  speed, aim into the gaps, the attack mix and its tells (above), errors, serve speed, the player's toys' run speed and
  reaction, the pace, the window scale), `VolleyTournament` (eight pairs, the bracket `VolleyBracketView`, the 3D cup, cups per difficulty
  `volley.cups[2].<d>`), the rally's pace rising each time the ball crosses the net, **the computer's first two returns
  of every rally are sure** (`SureReturns` 2: it digs everything and never errs; each return of the player's takes three
  moves, so two is about Tenis!'s five hits; since 2026-11-08 only the first is a free ball: with two the computer hardly
  ever attacked a good player), Versus with the split screen (each half behind its team), `VolleyScore` (rally scoring, first to 7 with two
  clear, at most 10). Effects (`VolleyFx`, Tenis!'s approach: no real-time shadows): the sun's shadows as one mesh, the
  ball's shadow straight below it (where it comes down) and slanted, stars at touches (warm for spikes), sand splashes,
  sand kicked by runs, landings and dives, confetti, the ball's glow and trail; the crowd cheers points. An icon over the
  toy the ball comes to shows the move it wants (`VolleyArt.MoveIcon`: hands low, hands up, one swinging; "PRIT /
  NGRIJE / GODIT"); the waiting screen teaches four moves (receive, set, hit, block) and the defence's rule. Keys without a
  camera: ↓ receive, ↑ set / block, space / OK hit.
  Music `volley.ogg`, existing sounds (`sport_bump`, `sport_volley`, `sport_thud`, `sport_net`, `sport_whistle`, ...).
  Balance (`Balance_ByDifficulty`: simulated players with the camera's lag, reaction times (0.2 s perfect / 0.3 good /
  0.45 child / 0.55 struggling) and a read of the hitter's look (`VolleySim.ReadSkill` 1 / 0.92 / 0.85 careful child /
  0.75 child / 0.6; a disguised tip switched to only if quick enough), wins of 8 at Easy / Medium / Hard first and last
  round): perfect 8 8 8 8 8 8, good 8 8 8 8 8 7, a careful child 8 8 8 8 7 6, a child who lets 7 % go by 5 5 2 0 0 0,
  struggling 0, standing still 0; **the user's trick, a good player who always defends with the hands low, 1 1 1 0 0 0**;
  one who always blocks 8 8 7 4 1 0 (Easy never tips; on Medium's first round it concedes 32 points where reading
  concedes 7); 1.8-3.3 minutes a match. Balls out of reach for a good player: 0 % (a smash is the block's, a tip read
  right is reached). Tests: `ASmash_IsStoppedOnlyByTheBlock_ATip_OnlyByTheReceive`, `TheBlock_IsWhatThePlayerShowsAsTheHitterHits`
  (hands up until 0.04 s before the moment of the choice block, from 0.02 s after don't, with a 0.17 s camera lag at 15
  samples a second), `TheAttacks_ByDifficulty_EasyNeverTips_HardDisguisesAndReadsThePlayer`,
  `Versus_AStrongHitIsASmash_AGentleOneATip_TheOtherPlayerDefends`. **Not yet**: the user's play test (above all: can
  they read the far hitter's look in time on the TV, the drawn-back arm against the hand forward; is ~1.1-1.4 s from the
  set to the hit enough for a child; is "hands up as the hitter swings" right with the box's camera lag), the Albanian
  (`VolleyStrings`: HowTo, HowBump "Duart bashkë poshtë: prit topin", HowSet, HowHit, HowBlock "Kur hidhen për smesh: dy
  duart lart, blloko", HowDefend, Bump "PRIT", Set "NGRIJE", Hit "GODIT", Block "BLLOKO", Blocked "Bllok!", OverTheBlock
  "Mbi bllok!", TooHard "Shumë i fortë!", TipCall "Fintë!", BlockHint, TipHint, Blocks "Blloqe", ReceiveHint, SetHint,
  HitHint, ServeHint, Save "Shpëtim!", Wrong "Gabim!", FirstTo, TogetherModeSub, KeyboardHelp, ...), the pairs' names (`VolleyLevel.Names`: Drita, Ilir, Besa, Arben, Jona, Luan, Rina, Altin, Dea,
  Erion, Kejsi, Bledi, Ana, Driton), the split screen's frame rate on a hot box. **On the box** (2026-11-08, docs/hardware.md
  "Volejboll! on the box"): the waiting screen 29.6-30.0 fps, a live match with the scripted player over Wi-Fi 29.1-30.0
  fps (0-2.7 % of frames late) at 80-82 °C, the split screen 25.7-27.4 at 82.8 °C with the GPU throttled to 200 MHz (like
  Tenis!'s); no errors. The box runs the build with the defence (block or receive at the hit, 2026-11-08 evening): the
  waiting screen 30.0 fps, a live match 29.5-30.0 (0-1 % late, main 10-13 ms) at 73 °C, no errors.
- **Gara! (game 10, 2026-11-08, user: "a new game: a car game. see how Nex Playground, Wii, Switch and Kinect
  implemented theirs, choose the best gameplay and implement; see how tennis and volleyball were implemented, those are
  the most polished").** Kart racing with an **invisible steering wheel**, the chibis at the wheel. Researched first:
  Nex Playground's Miniacs: Steering Madness (hands up as if gripping a wheel, 1-4 players racing at once on micro
  tracks; community wants more feedback on opponents' positions), Kinect Joy Ride (invisible wheel; boost by pulling the
  hands back and pushing: reviewers found the boost inconsistent and the steering loose, Metacritic 52), Kinect Rush's
  Cars levels (arms as a wheel: "the easiest to get into"), Forza 4's Kinect mode (the game drives the pedals:
  "following a coloured line"), Kinect Star Wars podracing (most precise, tiring), Sonic Free Riders (lean steering and
  laggy jumps: the cautionary tale), Kinect Adventures' River Rush (two players steer one raft: the most fun with two),
  Mario Kart Wii (Wii Wheel tilt; a shake at a ramp = a trick = a boost on landing), Excite Truck (tilt steering; air,
  drift and near-miss stars; land flat = boost), Wii Sports Resort Power Cruising (gates, a boost meter), Mario Kart 8
  Deluxe (smart steering and auto-accelerate for children). **The design** (the best of each, robust for a camera): the
  kart always accelerates; both hands up like on a wheel and turned steer (`KartMoves`: the line between the wrists, the
  left hand higher = right; a 25-degree jump in one sample is held a sample (the box's hand glitches), a light One Euro,
  dead zone 4 degrees, full turn 60; the hands down: the lean steers (shoulders' tilt + torso), "Mbaj timonin me dy
  duar!" after 2.5 s and, after 1.5 s with nobody's hands on the wheel, the kart runs at 90 % (`NoWheelAfter`,
  `NoWheelPace`: else a hands-off kart on Easy, driven perfectly by smart steering, won races); **smart steering** by
  difficulty (`KartLevel.Assist`: Easy 1 = the kart follows the road by itself, the wheel only moves it across, it never
  leaves the road; Medium 0.75-0.6, Hard 0.55-0.3: the player steers the corners); **a jump** (BodyGestures' knee-dip
  rule on the torso) **or the wheel lifted quickly with both hands** (sitting on the couch) = a trick at a ramp (from
  0.6 s before the lip to 0.5 s after, times `WindowScale`; the camera's lag counts: a late jump still tricks while the
  kart flies) and a boost on landing (Mario Kart Wii), a little hop elsewhere, a quick start at "go"; **drift** = the
  wheel held hard (0.6 of a turn for 0.25 s): the drift follows the road by itself (the wheel tightens or widens the
  line from where it began: Mario Kart 8 Deluxe's drift with smart steering), sparks heat in three tiers (0.6/1.3/2.1
  s), letting go boosts; **both hands up** = the gift box's item: rocket (2.5 s at 1.6x, nothing slows it), paint bomb
  (flies to the kart ahead, spins it out, 3 stars lost), shield (stops one bomb/honey), honey (dropped behind, slows the
  next kart); items by place (behind: rockets, ahead: honey and shields; players get more rockets on Easy); boost pads,
  lines of stars (each +0.8 % top speed up to 10, respawn after 12 s). Six karts, 3 laps (~35-40 s a lap), the players
  start at the back. **The rules** (`KartRace`, engine-free, tested): karts in track coordinates (s along, d across,
  heading against the road, speed, height): walls are soft bounces (a new contact every 0.4 s, speed kept by
  `BumpKeep`), kart bumps push apart (a rear-end slows the one behind and ends its drift), the computer drives racing
  lines inside the corners with a lane, wobble and the odd wide corner, aims for pads and boxes (a hash per kart,
  feature and lap), tricks, drift boosts and items (`KartLevel`: pace 0.97 - 0.0068 k, its rubber band only across real
  gaps: from 20 m ahead it eases, from 40 m behind it catches up; it never paints a player on Easy). Boosts are strong
  on purpose (tricks 1.4 s at 1.45x, drift tiers 0.7/1.2/1.7 s, pads 1.2 s at 1.45x, quick start 1.5 s): with weak ones
  skill made no difference. **Tracks** (`KartTracks`, laid out in Python first and checked: closed centripetal
  Catmull-Rom, every corner >= 18 m radius, no part of the road near another): Bregdeti (the coast, 839 m: the sea along
  the long straight, a hairpin, S-bends inland), Fshati (the village, 825 m, up to 12 m of hill, the castle in the
  middle), Lugina e Dinozaurëve (935 m, a double jump on the long straight, the volcano). **Modes** (Volejboll!'s menu +
  a track row, `KartMenu`, remembered under `kart.*`): 1 player, versus (a kart each, the screen split, four computer
  karts), together (River Rush's raft: **one kart, both steer**: the average of the wheels held, either one's jump
  tricks, both within 0.5 s = a double trick, the second player rides on the back); one race or a **cup** (`KartCup`:
  the three tracks, points 10/8/6/4/2/1, standings between races, the podium's 3D cup, cups won per difficulty
  `kart.cups[2].<d>`); Easy/Medium/Hard (strength per cup race like the others); camera behind or high. Balance
  (`Balance_ByDifficulty`, simulated players with the camera's lag who aim for pads, drift and jump, mean place of 6
  over 12 races, Easy/Medium/Hard first and last race): perfect 1.0 1.1 1.3 1.8 1.6 1.9; good 1.4 1.3 1.7 1.6 2.2 1.8;
  child 1.7 2.3 2.2 2.8 2.4 3.1; struggling 2.1 2.7 2.5 3.8 4.1 5.2; standing still 6 everywhere; a race 85-115 s.
  Hard's last race is tight even for perfect (wins ~5 of 12: the computer takes every boost too). **The look**: the kart
  as code in Blender (`art/kart/kart.py`: a soap-bar toy body, side pods, a seat, engine and exhausts, a rear wing,
  chunky ribbed tyres; colour regions like the chibis, baked AO; `Kart.fbx` with KartBody/KartWheel/KartSteering), drawn
  with Tundu/Athlete per racer's paint (`KartLooks`: Tenis!'s cast drives, each their colour; the player red, player 2
  sky blue); the chibi seated at 0.74 scale, its mittens on the wheel's grips turned with the player's own hands
  (`KartView`), leaning into turns, faces by what happens; the scenery as code in Vrapo!'s palette style
  (`art/kart/props.py`: palms, cypress, olive, round and pine trees, bushes, flowers, rocks, white and stone houses, a
  lighthouse, a castle tower and wall, umbrellas, tyre stacks, cones, pennants, the gantry with its countdown lamps,
  gift boxes, grandstands; `KartProps.fbx`, 16 colours appended to `tundu_palette.png`'s spare cells); the dino valley
  uses Vrapo!'s ferns, cycads, rocks, volcano, the dino cheering by the start and circling pterodactyls; the track built
  at runtime (`KartWorld`: the ground grid following the hills, the road ribbon with kerbs, shoulders and red/white
  barriers, the sea plane, ramps, scrolling boost pads, spinning boxes and stars, mountains, Sporte!'s crowd on the
  grandstands, everything static-batched); effects (`KartFx`: soft sun shadows as one mesh, drift sparks by tier, boost
  bursts, dust, exhaust puffs, sparkles, paint splashes, honey puddles, bombs in flight, confetti); the HUD (place, lap,
  stars, the item roulette, the wheel turning with the hands (dim with them down), km/h, the minimap with every kart,
  calls and popups). Music "Tundu Grand Prix" (`kart.ogg`, `music/midi/kart.mid`), 17 `kart_*` sounds (an engine loop
  pitched with speed per player, a drift screech loop). Scene `Games/Kart/Kart.unity`
  (`MoveItSceneBuilder.BuildKartScene`), home tile 10 ("Gara!"; the tiles got a smaller type step for ten),
  `BoxGraphics` "Kart" (Volejboll!'s settings, split 0.7). Keys: ← → steer, space jumps, ↑/Enter the item. **Tests**:
  EditMode 28 (`KartTrackTests` 4, `KartRaceTests` 15 incl. the balance, `KartMovesTests` 6 on simulated bodies: the
  wheel, a glitch held off, the lean, a knee-dip jump (turning the wheel isn't one), the wheel lifted (one hand's glitch
  isn't), hands up once per raise; `KartCupTests` 2), quick runner `tools/logic-tests/quick/run.sh Kart -- <Kart Scripts
  minus the drawing> MoveSDK/Runtime/OneEuroFilter.cs Kart/Tests/Editor/*.cs`; PlayMode `KartGameTests` (port 5023): 3/3
  pass: solo with `--script kart` (a whole race on Easy driven by the scripted wheel: steering -0.66 .. 1.00 (up to 66
  degrees), 32 jumps and 17 raises of both hands seen, which reached the kart as 28 hops, 2 tricks at the ramps, 16
  drifts, 5 items used, 5 stars; 3rd in 113 s; `kart_menu.png`, `kart_countdown.png`, `kart_racing.png`,
  `kart_drift.png`, `kart_air.png`, `kart_item.png`, `kart_results.png`), versus with `--script kart_duo --alternate`
  (one person per packet like the box: each wheel steers its own kart, the split screen, `kart_versus.png`) and a cup of
  one-lap races at 4x speed (the standings after each race, the podium's cup, `kart_standings_1/2.png`, `kart_cup.png`).
  **Not yet**: the user's play test (above all: does the invisible wheel feel responsive with the box's camera, the dead
  zone and full turn, the jump at the ramps with the camera's lag, the drift, the speeds per difficulty, the no-wheel
  slow-down, the together mode), the Albanian (`KartStrings`: everything; the name "Gara!"; the tracks' names, "NISU!",
  "Xhiroja e fundit!", "Salto!", "Mbaj timonin me dy duar!", the items "Raketë / Bombë me bojë / Mburojë / Mjaltë", the
  menu's "GARË / KUPË", "PRAPA / LART"), the karts' and props' look on the TV, the split screen's frame rate (under 30
  while both drive), the hitch as a run starts. **On the box** (2026-11-08 night, docs/hardware.md "Gara! on the box"):
  a live race (the scripted player over Wi-Fi, Easy, the coast) a steady 30 fps (29.9-30.0, 0-0.7 % of frames late, main
  thread 12-16 ms) at 76-77.5 °C with the GPU at 700 MHz, 49-66 % busy; the split screen (versus, two scripted players)
  28.0-29.5 fps, 3-12 % late, at 79-80 °C (main and render threads ~21-25 ms: two cameras and two HUDs; the chibis are
  low-detail there already; Tenis!'s and Volejboll!'s split screens measured 26.5 and 25.7-27.4); a 0.4-0.6 s stall as a
  run starts (the new opponents' chibis are built then); over Wi-Fi every move's age hits `KartGame`'s 0.35 s cap (the
  laptop's clock differs from the box's; with the box's own camera they share one). The box runs this build (2026-11-08
  23:00), its Gara! menu back on 1 LOJTAR.
- **Sporte! (game 8: the 100 metres and football; beach volleyball until 2026-11-07, now Volejboll!; 2026-10-21/22).** History: on 2026-10-21 the user asked
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
- **Chibi characters: the main characters for every game (2026-11-03, user, with a reference sheet of chibi avatar
  parts: "I want the characters to match these main character models I will use in all games, start with tennis").**
  One cute toy body, ~2.3 heads tall (1.52 m at scale 1): a big squarish-round head (superellipsoid) with little ears,
  big black bean eyes with two shines each, a small smile, blush, a small body, mitten hands, big sneakers. Built as
  code in Blender 5.1 **headless** (`art/chibi/`: `kit.py` geometry helpers, regions, rig, weights; `body.py` the body
  and faces; `wardrobe.py` every piece; `chibi.py` build, previews, AO bake, export; `build.py` runs it, ~15 s:
  `blender --background --factory-startup --python art/chibi/build.py -- --ao --preview --export`). **The wardrobe**
  (one piece per slot): hair Spiky, Fluffy, Shaggy, Buns, Short (each also "_Hat" and "_Hood": pressed under a hat or
  the cat hood, `squashed()`, so nothing pokes through), hats Cap, CapBack, CapStar, Bucket, Crown, FrogHat, CatHood,
  ears CatEars, BunnyEars, BearEars, DevilHorns, UnicornHorn, tops Hoodie (hood, pocket, drawstrings), HoodieStar,
  BearHoodie, ZipHoodie, Jacket, TrackJacket, Checkered, Tee, TeeStar, Polo, bottoms Shorts, Pants, Skirt, shoes
  Sneakers, HighTops, accessories Glasses, RoundGlasses, HeartGlasses, Headphones, StarClip, Mask, back AngelWings,
  DevilWings, CatTail, DevilTail, RaccoonTail, BunnyTail (61 pieces). Colours per **region** like Sporte!'s athletes
  (uv1.x = region, per face so borders are crisp; uv1.y = baked AO; Tundu/Athlete's 24-colour table): skin, blush,
  eyes, eye_shine, mouth, tongue, hair(+accent), top / top_accent / top_trim, bottom(+accent), shoes / shoes_accent /
  soles, hat(+accent), ears / ears_inner, acc(+accent), back(+accent). Rig: Hips, Spine, Head, UpperArm/Forearm/Hand,
  Thigh/Shin/Foot per side, and face bones (Eyes, EyesHappy, Mouth, MouthOpen, MouthSad: shown/hidden by scale).
  Previews (`art/chibi/previews/lineup0/1_*.png`, gitignored) show the sheet's four characters (cap kid, cat girl,
  devil girl, headphones kid), the plain one and three more. **Unity**: `Chibi.fbx` in
  `Assets/Games/Common/Characters/Resources/Chibi` (import rules `Editor/ChibiArtImport.cs`, which also makes
  `ChibiMaterial.mat`, Tundu/Athlete two-sided), generated `ChibiData.cs` (regions, pieces, slots), `ChibiLook`
  (pieces + colours, fluent `Paint("top", "#2E6FE8")`, presets `CapKid`, `CatGirl`, `DevilGirl`, `HeadphonesKid`),
  `Chibi` (loads from Resources, merges the look's pieces into one skinned mesh = one draw call, region colours by
  property block; games pose the bones: `AimArm(side, direction)`, `SwingLeg`, `BendLeg` + `LegDrop` (knees, since Volejboll!), `TurnHead`, `HandCenter`, `Shoulder`;
  `SetFace(Smile/Grin/Happy/Sad)`, blinks by itself, `Flash`). Hair strands are 9 rings of 8 since 2026-11-05 (were
  12 of 10): a long hairstyle ~9.8k triangles (was 13.5k), short 4.1k, so a dressed chibi is ~20-27k; four of them in
  Tenis! run on the box (see the Tenis! bullet). **Tenis! is the first game with them**. **Not yet**: the user's look at the kit (style, proportions, which pieces to
  add), the other games (Vishu! could dress a chibi, Sporte!'s athletes, Bowling, Shpata!...), a character creator
  (the player's own look, saved), animation clips (games pose procedurally so far).
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
  hips + 0.3; Tenis! knees + 0.3 + 0.1 above (since 2026-11-07 its toys run by themselves: room for a swing only). Vishu! and Pikturo! set none: the camera picture
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
  userdebug, build X98KPRO_20231007) on the user's Wi-Fi (192.168.1.46 since 2026-11-03; it has also been at
  192.168.1.57 and 192.168.100.103: DHCP). **The camera works** (end of this bullet). A fresh APK (63.9 MB)
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
  view: "Heat and frame rate" below). **Not yet**: a person tracked in a game (speed, feel), the detector's 29.5 MB
  RKNN file (its depth-to-space layers grow into big transposed convolutions; it only runs while nobody is tracked),
  the APK grew 64 → 77 MB. INT8 models: tried 2026-11-01, not accurate enough ("INT8 pose models" below).
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
  700 MHz from ~40 % load, a small passive box. **Not yet**: CameraX delivering YUV to the NPU path (its RGBA
  conversion is ~6 % of a core), a longer idle level (screen saver, camera off), the user's feel of the 30 fps home
  screen and of lite. INT8 pose models were the NPU's lever: not accurate enough (below).
- **INT8 pose models (2026-10-31/11-01, from the heat session: "quantize to INT8, adopt only if as accurate as
  float16"): not accurate enough, float16 stays.** INT8 halves the NPU's time on the RK3566 (the box with no app
  running: lite tracking frame 18.7 → 12.1 ms, NPU 12.3 → 7.4; full 26.4 → 15.6; the detector 32.2 → 18.4, NPU
  28.0 → 14.3; files 3.1 → 1.9, 6.8 → 3.8, 29.5 → 15.0 MB) but loses far too much. Calibration: 200 snapshots from
  the box camera (the family's living room in the evening, people sitting and standing; `D:\rknn\calib`, not in git),
  every 4th held out; `tools/npu/calibration.py` turns pictures into what each model sees (the detector's letterboxed
  frame, the landmark model's tracking crop; also mirrored, other light, the crop moved): 453 detector frames, 157 / 155
  crops. Judged on 25 held-out pictures (20 snapshots, MediaPipe's test picture and 4 copies) with **`compare.py
  --crops`**: every runtime's landmark model on exactly the crop the original model sees. (End to end, on a hard
  picture even float16 lands ~10 px from MediaPipe, because a tiny difference moves the next crop; the clean test
  picture stays at 0.2–0.5 px.) On the box, float16: lite 0.30 px mean / 0.25 cm, full 0.24 px / 0.14 cm. INT8 (w8a8,
  RKNN's "normal" ranges): lite 12.5 px / 10.3 cm, full 13.7 px / 10.9 cm (1.4–2.4 px and ~3 cm on the clean picture,
  5–30 px on the living-room ones). The "mmse" and "kl" ranges are worse (they clip the outputs: world joints 16 and
  135 cm off); 16-bit activations (w8a16) don't exist for the RK3566; Rockchip's automatic hybrid (layers that drift
  stay float16) 11.3 px; float16 for the first third of the network and the output heads 4.8 px / 3.5 cm at 16.7 ms,
  the first two thirds 3.2 px / 1.8 cm at 18.1 ms (float16's time). Every layer alone loses little (Rockchip's
  per-layer analysis): the error builds up over the whole backbone. The INT8 **detector** never missed a person
  float16 found, but found people in 17 of 50 held-out frames where float16 found nobody: often the real guitar
  player (sitting, dim), but also ghosts (a guitar and cushions on the empty couch, which the landmark model then
  "tracked": a ghost keeps the AI at 30 fps, the opposite of saving heat); where both found someone, its first crop
  was ~5 % off in position and size and ~9° in turn. With float16 output heads: 11 of 50, the same crop errors. Heat it
  could have saved, estimated from the full → lite switch (NPU 64 → 34 %, SoC 77 → 75 °C), not measured: ~1 °C with
  someone tracked, ~0.5 °C with nobody in view. Kept: the tool chain (`convert.py --quant --calibration`,
  `calibration.py`, `compare.py --crops`, `pose_npu_test --crop`) and the engine's INT8 path (it writes the NPU's 8-bit
  input codes and dequantizes INT8 outputs itself), which `PoseEngine` never gets a model for. What could work later: a
  model trained for 8 bits (quantization-aware), or a chip with 16-bit activations; check with `compare.py --crops`.
- **Stutter (2026-10-31 evening, cloud session; user: "why does my model stutter so much, but something like the link
  app on steam has perfect tracking?")**. The app is The Link (Masonx, Steam 1285430; see "Stutter, part 2" below).
  Findings: the pose AI itself isn't it (the box runs
  MediaPipe's own models, within 0.5 px of MediaPipe); what differs is everything around it. (1) **The skeleton mirror
  (the corner mirror in the menu and games, Settings → Kamera) drew the newest pose as it arrived**:
  `tools/motion/frame_timing_sim.py` on the box's timing gives a 30 Hz staircase at 60 fps (every frame of a move a
  stall or a double step) and ~9 % stalls/jumps at 30 fps from the camera's and the game's clocks beating (~32 % when
  the AI loses the person twice a second). **Fixed**: `Body.Shown`, the smooth joints interpolated on the camera clock
  by `JointTimeline` (`HandInterpolator`'s timing for all joints: half a camera frame behind, gliding up to 80 ms),
  updated by `MotionSystem` every frame; `MotionDebugOverlay` draws it (< 1 % in the sim). The cursors, Shpata!'s
  ghost, `BodyPuppet` and Vishu!'s clothes were already timed this way: their jerks can only come from the poses
  themselves. (2) **Tracking losses** (the landmark model's presence < 0.5): no pose from that frame, the detector
  (~43 ms) on the next, MediaPipe's smoothing starting over (a jump). Never counted: the stats line now ends in
  `gaps: N camera frames skipped, M lost, worst X ms, frame age Y ms (boot|monotonic clock)` (per 5 s), and the camera
  test screen shows "Kuadro pa AI" / "Trupi u humb" (new Albanian: review). The old "camera fps" counted the
  analyser's calls, so it could never show the AI falling behind; "skipped" can. (3) **Lite is noisier than full**
  (`tools/motion/model_jitter.py`, still frames with camera noise: smoothed, lite moves 0.06-0.27 % of a torso per
  frame vs full 0.01-0.07 % in good light, 0.13-0.46 vs 0.06-0.20 dim; real cameras are far noisier) and the box runs
  lite since the heat work. (4) **30 fps**: the HAL drops the camera's 60/120 fps modes (USB camera section); a PC app
  with a 60 fps webcam gets half the step and half the blur. (5) **Capture clock**: `captureWallTime` only tried the
  boot clock; a camera whose timestamp source is UNKNOWN (likely the external camera HAL's) stamps frames on the
  monotonic one (the two differ once the box has slept), and a miss timed every sample by when its analysis started. Now it picks the clock that gives an age under a second and
  logs which. (6) The camera picture (Vishu!, Pikturo!'s camera mode) shows the newest frame each game frame: 3-8 %
  of the frames repeated or skipped at 30 fps in the sim, ~0.8 % if held half a frame behind (not done: it costs
  picture latency; the user's call). Also: the cloud compile check was red since 308bc2b (stand-ins out of date, Fly's
  `#if UNITY_EDITOR` code made its test assemblies fail): fixed. **Not yet**: the user's look on the box (the
  mirror, the new `gaps:` numbers with someone moving in front, in their usual light; then lite vs full with
  PerfLab `model=`), a recording from the camera test screen (OK) to replay, the HAL's `FpsList` for 640x480@60.
- **Stutter, part 2 (2026-11-01, user: "the camera should be more than enough; there shouldn't be any stutter; The Link
  gets a perfect skeleton with a laptop camera")**; details in docs/hardware.md "The camera, and why the skeleton
  shakes". The branch above was merged into main (the user left the decision to this session). **The camera** is an
  ELP-USBFHD01M (OmniVision OV2710, 1/2.7", 3 µm pixels: better than a laptop webcam's sensor). On the box it gives
  1280x720 at 59 fps and 640x480 at ~90 even at night, but **those modes are 1:1 crops of the sensor's middle**
  (1.5x zoom: a standing adult then needs ~3 m to fit head to toe, not ~2.2 m); 1024x768 (what Android gives us, 30
  fps) keeps the full height. Android drops the 60 fps mode although this box's `FpsList` lists 1280x720 at 60
  (probably the camera's 60.0002 against the bound 60.0). **The shake is mostly the picture, not the camera or our
  code**: `tools/npu/shake.py` (same synthetic camera noise on real box snapshots vs MediaPipe's clean photo, tracked
  like the box) gave lite raw steps of 12 % (wrists) / 4–6 % (shoulders, hips) of a torso per frame on the evening
  snapshots (people sitting with a guitar, gain on) against ~1 % on the photo at the same person size; full −30 %; the
  crop also run mirrored and both answers averaged −20–35 % (two model runs a frame); area-averaged crops no gain.
  **The Link** almost certainly runs MediaPipe Pose (its settings: "pose detection confidence and tracking confidence,
  both default 0.5"; CPU only) with a 720p 60 fps webcam in a bright room, draws the skeleton on the very frame it came
  from, and triggers keys from held poses with a 90 % / 30–50 % on/off gap. **First real test** (the merged build on
  the box, 01:28 the same night: the user standing ~2.5–3 m away, head to feet in the picture, light grey tracksuit
  against white walls, tuner level 4, lite; `motion-service-py/sessions/box-2026-11-01/`, gitignored), scored with
  **`tools/motion/shake_report.py`** (the shake = the packets minus their path smoothed both ways, on frames where
  the player holds still): 91.8 s at a steady 30 Hz, the person never lost, 0 camera frames skipped (the `gaps:`
  counters); still shake RMS head 0.8 %, shoulders 0.55 %, hips 0.9 %, elbows 1.5 %, knees 2.1 %, **wrists 4.5 %,
  ankles 3.6 % of a torso** (~12 / ~10 px on a full-height 1080p mirror), a jump over 3 % in ~20 % of their still
  frames; the laptop's recording (MediaPipe full, the user close to its webcam, torso 506 px of its picture against
  175–216 px here) gives wrists 1.7 %, head 0.3 %, shoulders 0.25 %. Frames are ~47 ms old when the AI starts on them
  (the camera HAL's buffers and conversions), ~70 ms when the pose is out. **Full on the box (second test, 02:02)
  shook just as much** (wrists 4.5 %, head 0.7 %) and didn't keep up while tracking (32–41 ms a frame, 24–27
  poses/s, 14–23 camera frames skipped every 5 s, frames ~65 ms old): lite stays, auto. **Holding still for real**
  (a baseline at 02:35, 1024x768, ~26 s still, brighter: tuner level 3) the skeleton barely moves: wrists 0.43 %, head
  0.26 %, shoulders 0.23 %, ankles 0.79 % of a torso (~0.6–2 px on a full-height 1080p mirror), as steady as the
  laptop; the first tests' "still" frames were pauses inside movement. **What's left is in movement**: wrists and
  ankles land > 20 % of a torso off their path in ~2–3 % of the moving frames (probably motion blur at 30 fps with a
  dim room's long exposures). **1280x1024** (PerfLab `camera=1280x1024`: `CameraSource.request`, `MotionService.
  setCameraSize`, an exact-size `ResolutionFilter` because CameraX prefers 4:3): lite kept 30 fps, frames ~7 ms older,
  the AI ~2–3 ms slower, ankles glitched less while moving (> 10 % off the path in 2.2 % of the frames vs 6.8 %),
  wrists the same: not worth ~10 ms more delay, the default stays 1024x768. **A 10 ms exposure** (manual, root
  `v4l2-ctl`; auto was ~30 ms): darker and noisier, still poses shook more, the player was lost twice a minute, and
  the moving jumps didn't drop: motion blur isn't the main cause, auto stays. The camera's auto exposure hardly
  re-adjusts (it keeps a manual value's brightness unless far off: history-dependent). **A one-frame jump gate for
  hands and feet: not adopted** (`tools/motion/jump_gate.py` on the recordings: true one-frame spikes ~0.6–1.3 a
  minute per wrist or ankle, while the gate held back 6–30 real fast moves a minute; the "off the path while moving"
  numbers were mostly real fast movement). So the pose data is clean (steady still, nothing lost, rare spikes): the
  remaining stutter has to be found on the screen. **Found on the screen** (`adb shell screenrecord` of Settings →
  Kamera while the user moved, `tools/motion/screen_judder.py`): that screen gets `BoxGraphics`' default 60 fps with
  someone in view but its IMGUI needs 18–21 ms of the main thread: 34–45 fps, 53–70 % of the frames late, an uneven
  mix of 1- and 2-refresh frames; in the video the fastest joint stalled for a frame in 13.5 % of the moving frames and
  lurched in 13.1 %. The pose data is even, its drawing isn't. **Fixed**: `BoxGraphics` gives `MotionDebug` a steady
  30 (30.0 fps, 0 % late, main ~13 ms; a new recording: 96 % of the pictures exactly two refreshes apart, before 62 %).
  **The skeleton sometimes not drawn at all** (user, 2026-11-01: "the view of the skeleton doesn't render at all
  sometimes, even when I'm in front"): every scene builds its own motion rig, the menu's lives on (DontDestroyOnLoad)
  and later scenes' rigs remove themselves at the end of their first frame; `RecognitionLab` and `MainMenu` took the
  overlay with `FindAnyObjectByType`, which sometimes returned the doomed duplicate, so the real overlay stayed as the
  menu's settings panel had left it: hidden (Settings → Kamera showed nothing; back home the corner mirror stayed
  gone). Both now take `MotionSystem.Instance`'s overlay, and the menu shows it again on every start. Checked on the
  box: Settings → Kamera drew the full-screen skeleton and the home screen's corner mirror came back every time.
  **Not yet**: the user's look at it; its IMGUI cheap enough for a true 60; the home screen at 60 while the remote is
  used ran ~32 fps with 57 % of the frames late (the same kind of judder while navigating); camera
  sharpness 2 → 0, the tuner setting the exposure itself, a
  skeleton drawn over the camera picture of the same frame (The Link's look), the frame age (~46 ms before the AI
  starts; the HAL keeps 4 buffers: `NumVideoBuffers`). The box runs the build from 13:36 on 2026-11-01 (all of the above, the camera test at 30, the
  size switch with its startup fix).
  **Hands circling in front of the body** (the user's recordings at 13:12 / 13:17 box time; `tools/motion/wrist_jumps.py`,
  docs/hardware.md): the one place where the data isn't clean. Holding the TV remote made the right hand jump far more
  (30 vs the left's 4 in 41 s). Without it both hands jump alike, ~30 a minute each in nonstop circles, **~85 % while
  that hand passes in front of the chest** (2.9 % of those frames, 0.5 % beside the body): with the forearm pointing at
  the TV the hand hides it and the model pulls the wrist toward its elbow for 1-7 frames (the forearm in the picture
  about half its length, ~0.8 in 3D). Every frame was analysed, 30 fps, bright enough: the model's limit on that pose,
  not the camera or our pipeline. A gate for exactly that (`--gate`) removed 1-5 of ~25 jumps a hand and held back real
  jabs: no filter fix. **Next**: the same circles with the full model (PerfLab `model=full`, a test only: ~25 poses/s
  here); if it's much better, the fix is making full keep up at 30 fps (or a stronger chip); else record the camera
  pictures with the poses (a build) and try MediaPipe's models on them on the PC.
- **The Link, observed (2026-11-01 evening, user: "I bought The Link, its performance is very good, analyze its code
  for the pose skeleton; I feel we can do better")**; details in docs/hardware.md "What The Link runs". A Nuitka-compiled
  Python 3.11 program (constants encrypted in the file), observed read-only while running: **MediaPipe Holistic** (legacy
  `mp.solutions`, CPU) with the **full** pose model (heavy/lite not loaded) plus hand and face models, confidences 0.5,
  MediaPipe's default smoothing and **no smoothing of its own**, OpenCV DirectShow MJPG **1280x720 at 60 fps** by default
  (640x360 / 424x240 at 30 as a performance fallback), one loop that reads, runs Holistic, draws on that same frame and
  shows it; a thick stick figure (pose points + neck and spine from the shoulders' and hips' middles, wrists as dots, the
  hand model's 21 points). No secret: stock MediaPipe full on a laptop CPU at the camera's rate, drawing locked to the
  picture. On the box full costs 25.5 ms a tracked frame idle (NPU 20.1; lite 20.4 / 15.0), 32–41 ms in a game; its NPU
  time: early 128/64-px layers ~45 %, the heatmap head ~13 %, three CPU-side output reshapes ~1 ms (lite has them too).
  **Correction**: the camera's 640x480 MJPEG mode is a 2x2-binned 1280x960 window (1.13x zoom, up to 120 fps), not a
  1.5x crop (1280x720 and 800x600 are). **Lite vs full on identical box-camera frames** (a 75 s routine recorded with
  the user, `routine-20261101`): in 2D lite is close to full except in fast punches/waves (wrists > 30 % of a torso
  apart in 5–12 % of those frames, < 1 % while calm); **in 3D full is far steadier** (a forearm > 25 % off its length
  in 4.2 % of the frames against lite's 27.9 %); full was right on bent arms and hands near the face, but swapped left
  and right and lost the person for 0.4 s when the user walked up close with the head out of the picture. New tools:
  `tools/motion/box_clip.py` (records the camera's raw MJPEG + capture times on the box with root `v4l2-ctl` while
  Android's settings are in front, light set like `CameraTuner`), `clip_models.py` (MediaPipe lite / full on the clip →
  recordings in the box recorder's format), `model_errors.py` (only the impossible: one-frame spikes, 3D bones changing
  length, lost frames, two models' disagreement), `clip_video.py` (side-by-side H.264 video, each model's skeleton on its
  own frame), `mjpeg_clip.py` (reads a clip). **Full at 30 fps (the same evening, user: "make full fit")**: the
  output reshapes out of the models (`convert.py`: `drop_output_reshapes`), the heatmap read as float16 (only Refine's
  windows converted), and the landmark crop pipelined with the NPU run (`PoseNpu::Prepare` / `RunJob` behind a lock on
  the crop box; `PoseEngine.submit` + its `pose-npu` thread, which sends each pose; the detector stays synchronous):
  full's tracked frame 27.5 → 23.6 ms serial on the idle box, the NPU's part 19.2 ms with the crop beside it; outputs
  bit-for-bit the same; the pipelined order checked on the recorded clip (`pose_npu_test --clip --order
  serial|pipelined|threads`, built for API 30 to decode the clip with AImageDecoder). Auto keeps full while the NPU's
  part stays under 31 ms (`NPU_SLOW_MS`). Built and installed on the box (APK 2026-11-01 20:26, models in
  `motion-service-py/models`, the previous ones in `D:\rknn\models_before_flat`). **Measured in the app** (the user
  playing Vrapo!, box at 78–82 °C, `tools/dev/box-ai.sh`): with full the AI holds 30 poses/s with 0 frames skipped
  (NPU part 21–29 ms), **but the game drops to 25–29 fps (8–32 % late)** against lite's 29.6–29.9: the NPU and the GPU
  share the memory bus (full's landmark model moves 41.5 MB a frame, lite's 26.3), and the game is back at 30 the moment
  nobody is followed; a 400 MHz NPU clock doesn't help. The auto rule (NPU part > 31 ms → lite) would keep full there.
  **Decided (user: "sure")**: the model per scene in `BoxGraphics.For` (`Settings.Model`): lite in Vrapo!, Shpata!,
  Fluturo!, Tenis!, Sporte!; "auto" (= full, lite only if the NPU can't keep up) in the menu, the camera test, Vishu!,
  Pikturo!, Bowling; applied on every scene load (`AndroidMotionService.SetModel`, ~0.6 s to re-open, then the
  detector), PerfLab without `model=` falls back to the scene's. Built and installed 2026-11-01 20:21; the switch
  checked on the box. **Not yet**: those full scenes' frame rate with someone in front (Pikturo! and Bowling aim at
  60). **The picture** (user: "the camera quality looks very bad"): at equal light (manual 33 ms exposure, white
  balance fixed) 1024x768, 1280x1024 and 1920x1080 are equally sharp and noisy, the JPEGs are high quality: it's the
  evening light (full gain, ~28 ms exposure at most for 30 fps; a fixed 33 ms gives 23 fps) and our brightening
  (`CameraTuner` level 5: gamma 190 clips and drops the mid-tone signal-to-noise 41 → 25; level 3 keeps 40). Raw
  captures in `motion-service-py/sessions/clips/fixed` (gitignored).
- **Git: on GitHub since 2026-10-10** (https://github.com/PlotterOtterGames/moveit, branch main, first commit
  be51fc6, the user asked for it). Commit and push only when the user asks.

## Repository map (`D:\Projects\moveit`)
| Path | What |
|---|---|
| `motion-service-py/` | PC camera service: `main.py` (OpenCV + MediaPipe 1.0.1, `--model auto`, sends protocol v1.1), `video.py` (camera preview stream on request), `fake_sender.py` (scripted test player: `basic`, `bowl`, `slash` (fast swipes, for the hand cursors), `idle`, `dropout`, `jumps`, `runner`, `paint`, `sword`, `fly`, `turns`, `dress`, `runway`, `turn` (Vishu!: the whole body turns and spins in 3D), `tennis` (Tenis!: one racket swing every 3.4 s, serve / forehand / backhand / lob / slice), `tennis_move` (steps to both sides, toward and away from the camera: a segment's `near` = metres toward the camera, the picture has perspective; since 2026-11-07 none may move the players: they run by themselves), `place` (Vrapo!: too close, too near a side, a run drifting to the edge); draws a fake camera picture when asked), `replay.py`, `models/*.task`. Venv in `.venv` (Python 3.12). New script `brawl` (Fluturo!: jabs left and right
without a break for 2.5 min); `tennis_duo` (two people side by side: both calibrate and raise their hands, player 1 steps,
player 2 steps, player 1 flicks up and serves, player 2 swings; `--alternate` = one person per packet with track ids,
like the box sharing its NPU); `volley` (Volejboll!: hands up, then 10 s cycles of the bump (hands together low), the set
(both hands up), a one-arm spike and both hands up again 7.6-8.9 s (the block); the first cycle serves); `volley_duo` (two people, the same moves in 6 s cycles, player 2
3 s later; `--alternate` like the box); `kart` (Gara!: both hands on an invisible wheel in 8 s cycles: turning left and right, a hard right turn held (a drift), both hands up, a jump); `kart_duo` (two people, player 2 half a cycle later; `--alternate`); `tennis_flick` (Tenis! with small forearm flicks only, ~15 cm in 0.22 s: a serve of two
flicks, forehand and backhand flicks, a small flick straight up last in each cycle (nothing: no miss)); `--glitches N`
(any one-player script: the box's pose-model glitches N times a second: a wrist flipped 15-50 cm away for 1-2 frames or
pulled onto its elbow for 1-3, visibility 0.7-0.85) |
| `docs/protocol.md` | **Motion Protocol v1.1**: UDP JSON to 127.0.0.1:5005; 17 COCO joints `k` (+ optional world joints `wk`, `cam`, `model`) |
| `unity/MoveItGames/` | Unity **6000.3.9f1** project (URP), **active platform: Android**. Editor at `D:\Unity\6000.3.9f1` |
| `Assets/MoveSDK/Runtime` | Motion SDK: `CameraFeed` (camera picture for AR modes, on request; on the box raw frames from libtundupose, else JPEG over UDP), `MotionSystem` (UDP receiver, feeds every packet in order), `BodyTracker` (smooth + fast low-lag tracks with body-relative One Euro filters, velocity, world joints, 1.5 s dropout continuity), `BodyGestures` (lanes with look-ahead, jump at take-off incl. from a knee dip, crouch at onset, hands up), `BodyHands` (hand cursors in a body-relative zone), `PlacementCheck` (is the player where the game's `PlayArea` needs them: step back / move left / move right / come closer), `HandInterpolator` (the one cursor engine: camera clock, interpolation or a least-squares fit, glide), `JointTimeline` (the same timing for a whole body: `Body.Shown`, what the skeleton mirror draws), `CursorRope` (string stabilizer), `UnderhandThrowDetector` (bowling, world depth + backswing), `PunchDetector` (punches in any direction, 3D with world joints, tested on simulated arms), `BodyInput` (body + keyboard merged), `PlayerSlots` (two players: who is player 1 / 2, each one's calibration, steps, raised hands, placement; 2026-11-05), `Android/AndroidMotionService.cs` (starts the Java service, camera permission, status, `SetPeople`: up to two people and the NPU's shared budget) |
| `Assets/Plugins/Android/MoveItMotion.androidlib` | **Java camera AI plugin**: CameraX 1.6.2, the pose AI **synchronous** on the CameraX analysis thread: **Rockchip NPU first** (`NpuPose` → `src/main/cpp/pose_npu.cpp` in `jniLibs/arm64-v8a/libtundupose.so`, Rockchip's `librknnrt.so` 2.3.2 beside it), else MediaPipe tasks-vision 1.0.0 in VIDEO mode, GPU→CPU fallback; auto full→lite, `Pose` = one result format, sends protocol packets to localhost, on-demand camera snapshot; nobody in view for 2 s = the AI looks every 150 ms and the thread sleeps between looks; on the NPU frames are read straight from CameraX's buffer; the camera picture for the game goes through `FrameShare` (`src/main/cpp/frame_share.cpp`: raw RGBA in the process, read by `CameraFeed`), the JPEG `VideoStreamer` only as the fallback; `CameraTuner` + `CameraControls` brighten the USB camera in a dim room through its own UVC controls (V4L2 node or root). Models (`.task`, `.rknn`) copied in at build time from `motion-service-py/models` (gitignored there); `libtundupose.so` is committed (rebuild with `tools/npu/build_native.py` after changing the C++) |
| `tools/npu/` | **The NPU pose path as code**: `convert.py` (MediaPipe's models → RKNN float16 per chip, in WSL: the detector's sparse weights densified, the landmark models without the segmentation mask and with the heatmap channels first; `--quant` INT8 with `--calibration`, not accurate enough on the RK3566), `calibration.py` (INT8 calibration pictures: what each model sees, from camera pictures), `blazepose.py` (MediaPipe's pose steps in Python, the reference for the C++), `mp_reference.py` (MediaPipe's own answers, PC venv), `compare.py` (TFLite / RKNN simulator / box dumps vs MediaPipe, px and cm; `--crops`: each model alone on the same crops, against the original model), `shake.py` (how much the landmarks shake on real pictures given camera noise: lite / full, mirrored-crop averaging, area-averaged crops), `build_native.py` (NDK build of `libtundupose.so` and `out/pose_npu_test`, the shell test for the box) |
| `Assets/Plugins/Android/AndroidManifest.xml` | Registers the app as the box's **HOME launcher** (`singleTask`); entry point = Activity; asks the TV for game mode (`preferMinimalPostProcessing`) |
| `Assets/Games/Common` | Shared game code: `GameHud` (IMGUI helpers, temporary), `CommonStrings` (Albanian/English, persisted), `PlayerGate`, `RemoteInput` (TV remote mapping), `AndroidDevice` (volume, Wi-Fi screen), `Brand` (reads `Assets/Resources/brand.json`), `RecognitionLab` (camera test screen), `DevCapture`, `BoxRuntime` (60 fps, no sleep), `BoxGraphics` (render scale, post-processing and target fps per scene on Android players: the box settings measured on the X98K Pro; a 60 fps scene nobody uses for 10 s runs at 30, `BoxGraphics.Idle`: the home screen counts only the remote, Bowling/Pikturo! also a player in view; the camera test a steady 30 since 2026-11-01: at 60 its IMGUI made the skeleton judder; `Settings.ScreenScale`: the scale as the screen's own resolution, scaled up by the TV's display hardware instead of URP's texture + copy: every scene below 1 since 2026-11-06), `PerfLog` (a `[Perf]` line every 10 s in every scene: fps against the target and the share of frames shown late, worst frame, share over 20 / 34 ms, main thread / present wait / render thread ms from frame timing stats (switched on by BoxBuild; GPU ms aren't reliable on the X98K Pro), GC, memory), `PerfLab` (players only: render settings switched on the box from a pushed file: HDR, scale, shadows, shadow distance/resolution, casters off by name (`nocast`), post, bloom/vignette/grade, sky, fps, hide renderers by name/kind, disable scripts by class (a game's HUD), load a scene, start an activity game's event (`activity=sprint|football|volley`), the pose model (`model=`), the camera frame size the AI gets (`camera=1280x1024`); lines tagged in `[Perf]`), `SkyNoise` (the noise texture Tundu/Sky's clouds read), `PartMesh` (primitives merged into one vertex-coloured mesh: Fluturo!, Tenis!), `Activities/` (`ActivityGame`: a game of short activities with medals; `SportHud`: the broadcast-style HUD pieces), `BodyPuppet` + `ToyPoses` (the player's body in metres, keys standing in for it), `GameFx`, `ArtShapes` |
| `Assets/Games/Tennis` | Tenis! (`TennisMode` Solo / Versus = singles, Together = doubles; `CourtPlayer.Controller` = the player slot whose swings each toy plays): rules without the engine (`TennisMatch`: four `CourtPlayer`s, in singles two of them off the court (`Active`), that run to the ball by themselves (`PlanHuman`, `Reaches`, `SetHomes`), who plays each ball (doubles: the net player's volleys, the back player after the bounce), serve, rally, the near-enough swing window, timing → aim, swings up lob, held backswings (`SwingResult.Held`), mistimed = weaker, the computer (its attack on weak balls), practice, `Award` (tests); `TennisScore`; `TennisBall`: `Court` (singles and doubles lines), `BallPhysics` (exact steps, prediction, `Aim`); `TennisLevel` (`TennisDifficulty`, `TennisFormat`, one strength per opponent and round, the fourteen players and seven pairs); `TennisTournament` (eight in a bracket, three rounds); `SwingDetector`), drawing (`TennisGame`: flow (menu, intro, play, bracket, results), swings from the camera, the camera views (`CameraViews`), calls, scoreboard, `Continue` (tests); `TennisMenu` (the waiting screen's choices); `TennisBracketView`; `TennisFx` (sun shadows as one mesh, particles, the ball's glow); `TennisPlayerView`; `TennisLooks` (14 opponents); `TennisStadium` (Wii's grass court: a worn grass grid, the fixed shadows); `TennisArt` (shadow atlas, grass detail, the cup)), `TennisStrings` |
| `Assets/Games/Volley` | Volejboll! (2026-11-07): rules without the engine (`VolleyMatch`: four `VolleyPlayer`s that run to the ball, the three touches, the moves' windows and priorities, the camera's lag (`See`, `SawUntil`), the defence (`Coming`: block or receive as the other side hits, `ChoiceAt`, the block, tips over it), the computer (its attack mix, `PlanCpuAttack`), practice (`PracticeContact`, `PracticeAttack`), `Award` (tests); `VolleyMoves` (the camera's moves: bump, set, hit); `VolleyScore`; `VolleyLevel` (`VolleyMode`, `VolleyDifficulty`, `VolleyFormat`, the seven pairs); `VolleyTournament`), drawing (`VolleyGame`: flow, menu, two players, camera views, HUD, the move icon; `VolleyMenu`; `VolleyBracketView`; `VolleyPlayerView` (the chibis' volleyball moves); `VolleyLooks`; `VolleyFx`; `VolleyArt` (the move icons, the net's mesh)), `VolleyStrings`. Tests: `Tests/Editor` (`VolleySim`: simulated players with the camera's lag who read the hitter; the rules, the moves on simulated arms, the tournament), `Tests/PlayMode` (`VolleyGameTests`, port 5022) |
| `Assets/Games/Kart` | Gara! (2026-11-08): rules without the engine (`KartTrack`: a closed centreline, curvature, hills, features: ramps, pads, gift boxes, stars; `KartTracks`: the three tracks; `KartRace`: six karts in track coordinates, smart steering, drifts, ramps and tricks with the camera's lag, items, bumps, laps, places, the computer and its rubber band, `NobodyDriving`; `KartLevel` (`KartMode`, `KartDifficulty`, `KartFormat`, the ten drivers); `KartCup`; `KartMoves`: the invisible wheel, the lean, jumps and the wheel lifted, hands up), drawing (`KartGame`: flow, menu, two players, chase cameras, HUD with the turning wheel and the minimap, engines; `KartMenu`; `KartWorld` (+ `KartWorldArt`): the track built from code with art/kart's and Vrapo!'s props; `KartView` (+ `KartModels`): a kart and its chibi; `KartFx`; `KartArt`: icons, the wheel, the move figures, pad arrows, the minimap; `KartLooks`; `KartStrings`). `Art/`: `Kart.fbx`, `KartProps.fbx`, `tundu_palette.png` (import rules `Assets/Editor/KartArtImport.cs`). Tests: `Tests/Editor` (`KartSim`: simulated drivers with the camera's lag), `Tests/PlayMode` (`KartGameTests`, port 5023) |
| `art/kart/` | Gara!'s art as code (Blender 5.1, headless): `kart.py` (the kart's body, wheel and steering wheel, colour regions, AO, previews, export), `props.py` (the scenery in Tundu's palette: trees, houses, lighthouse, castle, umbrellas, tyres, the gantry, gift boxes, grandstands), `build.py` (`-- --ao --preview --export`; `-- --props --preview --export`). Previews in `art/kart/previews/` (gitignored like the others) |
| `Assets/Games/Sports` | Sporte! (`SportsGame`, an `ActivityGame`: the menu with the venues): `TrackActivity` + `TrackRules` (`TrackRace`), `FootballActivity` + `FootballRules` (`FootballMatch`), `SportsMotion` (`RunDetector`, `ArmFlick`, `KickDetector`, `DipDetector`), `SportsKit` (`SportsBalls`, `ShotCamera`, `Daylight`), `Athletes/` (`Athlete`, `AthleteLook`, generated `AthleteData`), `Venues/` (`SportsArt`, `Venue`, `Fans`, `GoalNet`), `SportsStrings`. `Art/`: `Athlete.fbx`, `Venues/*.fbx` + seats, `crowd_atlas.png`, `Textures/`; import rules `Assets/Editor/SportsArtImport.cs` |
| `art/sports/` | Sporte!'s art as code (Blender 5.1, headless): `athlete.py` (the athlete's pieces, rig, AO, previews), `athlete_anim.py` (33 clips, IK pose solver, contact sheets), `crowd.py` (the crowd atlas), `build.py` (`--anim --ao --export --crowd --preview --clips --save`), `venue_kit.py` + `venues.py` + `venues_build.py` (the three venues; the beach's net is Volejboll!'s children's net, `NET_H` 1.5 m: rebuild with `venues_build.py -- beach`) |
| `tools/sports/textures.py` | Sporte!'s textures (PIL): ground details, the ad strip with the logo, the net, the big screen |
| `Assets/Games/Runner`, `Bowling` | Vrapo! (runner), Bowling (1–4 players, `BowlingScore` with tests) |
| `Assets/Games/Dress` | Vishu! (dress-up mirror + fashion show, replaced Fruta): rules without the engine (`DressCatalog`, `DressLook`, `OutfitRig`, `PoseTimeline`, `ShowRules`), drawing (`OutfitArt`, `OutfitRenderer`, `DressFx`), `DressGame`, `DressStrings`; `Art/` made by tools/dress; shaders `Games/Common/Shaders/TunduOutfit`, `TunduBodyMask`; import rules `Assets/Editor/DressArtImport.cs` |
| `tools/dress/` | Vishu!'s art as code: `draw.py` (vector toolkit, the body template), `outfits.py` (every item, atlas + manifest), `ui_art.py` (icons, judges, curtains, particles, thumbnails), `preview.py` |
| `Assets/Games/Paint` | Pikturo!: `PaintGame` (modes, toolbars, hands, photos), `PaintCanvas` (premultiplied RenderTexture, wet paint, undo, bucket), `PaintBrush` (tools along smoothed strokes), `PaintPen` (pen rule), `PaintArt` (brush atlas and icons drawn in code), shader `Tundu/Brush`. Pages in `Assets/Resources/Paint/Pages` (alpha-only import: `Editor/PaintPageImport.cs`) |
| `tools/logic-tests/` | .NET 8 + NUnit 3 project that links the engine-free game rules and their EditMode tests from the Unity project and compiles them against `UnityShim/` (cloud sessions: `dotnet test tools/logic-tests`). On the laptop (no .NET SDK): `quick/run.sh [filter] -- <sources>` compiles the given rules + tests with the Unity editor's own runtime and Roslyn and runs them in seconds (Tenis!'s 57 rule tests: see the script's header) |
| `tools/motion/swing_replay.py` | Tenis!'s swing detector, an exact Python mirror of `SwingDetector.cs` (and of the one before 2026-11-06): `recordings` (swings a minute, glitches ignored, `--list`), `agree <a> <b>` (two models' recordings of the same frames: real swings against glitches), `sim` (simulated flicks, swings and the box's glitches). Keep it in step with the C# when changing either |
| `tools/compile-check/` | `check.py`: compiles every asmdef except MoveIt.Editor as its own .NET project (same sources, defines, references) against Unity's UnityEngine reference assemblies (NuGet `UnityEngine.Modules` 2021.3) and `Stubs/` (Input System, test runner), for the Android player and the editor. Names Unity 6 added are listed in `UNITY6_ONLY` |
| `tools/motion/box_clip.py`, `clip_models.py`, `model_errors.py`, `clip_video.py`, `mjpeg_clip.py` | Model A/B on identical pictures: record the box camera's raw MJPEG frames with capture times (root `v4l2-ctl`, the app paused), run MediaPipe lite / full on them into recordings the scorers below read, count only impossible events (one-frame spikes, 3D bones changing length, lost frames, disagreement), and a side-by-side video of each model's skeleton on its own frame |
| `tools/motion/frame_timing_sim.py`, `model_jitter.py`, `shake_report.py`, `jump_gate.py`, `screen_judder.py`, `wrist_jumps.py` | How evenly a joint or the camera picture moves on the TV with the box's timing (newest pose vs the camera-clock timeline; camera, NPU, KEEP_ONLY_LATEST, game frames), how much MediaPipe's lite and full models shake on still frames with camera noise (needs MediaPipe + a photo), how much a real recording's skeleton shakes while the player holds still (per joint, % of the torso; the box's recordings are in `/sdcard/Android/data/al.tundu.games/files/sessions`), a one-frame jump gate replayed on recordings (not adopted), and how evenly the skeleton moves on the TV (an `adb shell screenrecord` video: frame spacing, joints stalling or lurching), and where the wrists jump in a recording (in front of the torso or beside it, how short the forearm looks; `--gate` replays an elbow-collapse gate, not adopted) |
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
| `Assets/Shell/Scripts/MainMenu.cs` | Box home screen (10 game tiles since Gara!, 2026-11-08; a step left/right or ← → moves one tile; back from a game, its tile is selected) + Settings (Wi-Fi, volume, language, camera, version, device; hidden Android settings: 7× OK on "Device") |
| `Assets/Editor/MoveItSceneBuilder.cs` | **Scenes are generated by code**: menu *MoveIt → Build All Scenes* (Menu, Runner, Dress, Bowling, Paint, Sword, Fly, Tennis, Volley, Kart, Sports, MotionDebug). Re-running overwrites them |
| `Assets/Editor/BoxBuild.cs` | *MoveIt → Box → Build Android APK* → `builds/android/<brand name>.apk` (~54 MB). Applies branding, IL2CPP ARM64, min API 28, TV banner and icon, copies models, sets GRADLE_USER_HOME=D:\.gradle in-process, caps Gradle heap at 2 GB |
| `Assets/Resources/brand.json` | **The brand's single source**: name, app id, taglines, palette (primary/secondary/accent/grape/leaf/background/surface/text/textMuted, 4 player colors), fonts. Read by `Brand.cs`, `BoxBuild.cs`, the scripts in `tools/` |
| `tools/branding/` | `logo.py` (the logo as geometry → SVG + anti-aliased PNG), `make_branding.py` (logo files in `brand/logo/`, icon/banner/logo/mark in `Assets/Branding/`, boot animation + boot logo BMP + GIF preview in `out/`), `brand_board.py` (the review page). Run with the motion-service venv |
| `brand/logo/` | Logo masters: on-dark, on-light, white, night, mark, icon (SVG + 2048 px PNG) |
| `art/chibi/` | **The chibi characters as code** (Blender 5.1, headless): `kit.py` (geometry: tubes, rings, slabs, the head's surface; regions; rig; skin weights; `DETAIL` / `lod()`), `body.py` (body, faces), `wardrobe.py` (every piece, `LOOKS` for previews), `chibi.py` (build, AO bake, previews, export), `build.py` (`-- --ao --preview --export`, then the same with `--low` for `Chibi_Low.fbx`: run both after changing a piece) |
| `Assets/Games/Common/Characters` | `Chibi` (the runtime: merged skinned mesh, region colours, posing helpers, faces), `ChibiLook` (pieces + colours, presets), generated `ChibiData.cs`; `Resources/Chibi/Chibi.fbx` + `ChibiMaterial.mat` |
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
- **Unity tests** (via the unityMCP tools, or Unity from the command line when the editor isn't open): 2026-11-08 night
  (Gara!, game 10): EditMode 267/269 (the whole suite in batch mode, ~3 min, 2 explicit diagnostics skipped; Gara!'s 28
  among them: `KartTrackTests` 4, `KartRaceTests` 15 incl. `Balance_ByDifficulty` (~1 min), `KartMovesTests` 6,
  `KartCupTests` 2), PlayMode the 3 `KartGameTests` (port 5023: see the Gara! bullet) and `HomeScreenTests` (ten tiles).
  Quick rule tests: `K=unity/MoveItGames/Assets/Games/Kart; tools/logic-tests/quick/run.sh Kart --
  $K/Scripts/Kart{Track,Tracks,Level,Race,Cup,Moves}.cs unity/MoveItGames/Assets/MoveSDK/Runtime/OneEuroFilter.cs
  $K/Tests/Editor/*.cs` (28 tests, ~40 s; the balance differs a little from Unity's run: the races are chaotic and the
  runtimes' floats differ). The other games' PlayMode tests not rerun (shared code touched: `BoxGraphics`' scene table,
  `MainMenu`'s tiles, the scene builder, spare cells of the shared palette, additions to sfx.py, compose.py and
  fake_sender.py).
  2026-11-08 evening (Volejboll!'s defence: block or receive at the hit): EditMode 239/241 (2 explicit diagnostics skipped; the 31
  volleyball tests among them), PlayMode: the 4 `VolleyGameTests` (practice with `--script volley`: a serve, then by turns
  a computer smash blocked by both hands up (the point, the body serves again) and a free ball received, set and spiked:
  4 blocks and 3 exchanges in 100 s, `volley_block.png` (the BLLOKO / PRIT panel, the "!"), `volley_blocked.png`;
  together and versus with `--script volley_duo --alternate`; the tournament, `volley_menu.png` with the four moves).
  Earlier the same day (the receive rule): Sporte!'s 3 and `HomeScreenTests` passed (not rerun: nothing they use changed).
  Quick rule tests: `V=unity/MoveItGames/Assets/Games/Volley; tools/logic-tests/quick/run.sh Volley --
  unity/MoveItGames/Assets/Games/Tennis/Scripts/SwingDetector.cs $V/Scripts/Volley{Moves,Level,Match,Score,Tournament}.cs
  $V/Tests/Editor/*.cs` (31 tests, ~12 s). Before: PlayMode 22 (`GesturePipelineTests` ×6 using fake_sender, `HomeScreenTests`
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
  `dress_turn_*.png`; `TennisGameTests`, port 5021: --script tennis in practice (doubles: four players), the computer
  timing its feeds to the scripted swings: the body serves, then each swing hits the feed back as it comes near, on
  whichever side (forehands, backhands, a lob, a slice), `tennis_serve.png`, `tennis_coming.png`, `tennis_hit.png`;
  --script tennis_move: sideways steps move the pair far out on both sides, steps toward the camera and away don't move
  them, `tennis_step_right.png`; the computer's racket at 60 fps through every kind of swing never jumps
  (max step per frame < 0.3 m), the player's live racket only as fast as the hand (capped), close-ups `racket_*.png`,
  `racket_live_*.png`), EditMode 156 + 2 explicit
  diagnostics
  (`PlacementCheckTests` 9 (a pinhole camera: right distance, too close, unseen knees, growing at the net, sides, no
  flicker on a border, far = advice, the in-play margin, no face), `BowlingScoreTests` 10, `PaintCanvasTests` 11, `HandCursorTests` 6 (incl. the fit and the rope), `SwordMovesTests` 5, `GiftCodeTests` 3,
  `GameAccessTests` 4, `PunchDetectorTests` 9 (forgiving punches, incl. the MediaPipe-like camera), `FlyRunTests` 32 (the endless run),
  `FlyProgressTests` 8, `FlyRouteTests` 8, `DressRulesTests` 18 (incl. the turn: sheets never fold and cover the silhouette, yaw from world joints and the
  picture, a turned dress keeps its width, hats and glasses stay on a turned face, upright from behind),
  `DressArtTests` 3, `TennisScoreTests` 4, `TennisBallTests` 4, `SwingDetectorTests` 7 (simulated arms through the low-lag
  filter), `TennisMatchTests` 20 (doubles: the body moves both players sideways only, forward and back is the game's, the assist
  glides to near balls only (too far = `TooFar`), a swing hits when the ball is near enough whichever way and side, no
  swing = the ball goes by, far too early / late misses while a little off still hits, an air swing then one on time
  hits, when the swing comes aims the ball, hand speed = power, up / down = spin, lag taken out, the point waits for
  the camera, the net players volley what passes within reach, a volley let through is the back player's, serves, shots
  in, practice feeds, the balance table)). 2026-11-07 evening (Tenis! round 7: swings up = topspin, the computer's sure
  first five returns): 209/211 EditMode, the 7 Tenis! PlayMode tests pass, quick rule tests 64. 2026-11-07 afternoon (Tenis! round 6: singles, swings up lob, held
  backswings, mistimed = weaker, the strung racket): 208/210 EditMode (2 explicit skipped), the 7 Tenis! PlayMode tests
  pass, quick rule tests 63. 2026-11-07 (Tenis! round 5: the toys run by themselves, difficulties,
  tournaments, the camera views, the shadows and effects): 206/208 EditMode (2 explicit diagnostics skipped; new
  `TennisTournamentTests` 4, `ThePlayersToys_RunToTheBall_ByThemselves`, `ShortOrDeep_TheBackPlayerGetsThereInTime`,
  `TheDifficulty_DecidesWhetherEveryBallCanBeReached`, `Balance_ByDifficulty` (the ladder's balance replaced), the
  two-player tests for self-running toys), the 7 Tenis! PlayMode tests pass (new `Menu_ATournament_TheBracket_TheCup`:
  the menu, a whole tournament won through `TennisMatch.Award`, `tennis_menu.png`, `tennis_view_high.png`,
  `tennis_bracket.png`, `tennis_champion.png`; `Steps_DontMoveThePlayers_TheyRunToTheBallThemselves` replaced the
  steering test, `tennis_auto_run.png`; the small-flicks test's practice beat now follows the sideways stroke flicks: synced
  to the serve it could be 0.6 s off and miss every feed); the other games' PlayMode tests not rerun (only Tenis!, the
  shared Tundu/Toon shader's new `_WORLD_DETAIL` option and `PartMesh.Add` with per-vertex colours changed). Quick rule
  tests: `tools/logic-tests/quick/run.sh -- <Tenis! rules + tests, now with TennisTournament.cs>` 61 pass.
  2026-11-06 evening (Tenis! round 4: the glitch-proof detector, gentle flicks,
  the reach): 202/204 EditMode (2 explicit diagnostics skipped; Unity in batch mode), the 6 Tenis! PlayMode tests pass
  (new: `Practice_SmallFlicks_HitTheFeeds_GlitchesAreIgnored`; the racket test now also reaches into balls, its
  per-frame limit scaled with the racket's length); the other games' PlayMode tests not rerun (only Tenis! changed).
  2026-11-06 (Tenis! round 3: pace, sideways swings, strokes): 196/198
  EditMode (2 explicit diagnostics skipped; new: `TheRally_StartsSlow_AndGetsQuickerEveryHit`,
  `ASwingUpOrDown_MissesAtOnce_...`, `ABackswing_OnlyGetsReady_...`, `TheToys_SwingWithThePlayer`,
  `APacedBall_FliesTheSameArc_...`, the detector's free-hand and backswing tests), the 5 Tenis! PlayMode tests pass
  (practice: 12 rally hits, the straight-up swing missed 3 of 3; the racket test drives the player's strokes now: no
  step over 0.31 m a frame); the other games' PlayMode tests not rerun. 2026-11-05 (Tenis! doubles): 172/174 EditMode (2 explicit diagnostics
  skipped), the 3 Tenis! PlayMode tests pass (practice: 20 hits from the scripted swings, none missed); the other
  games' PlayMode tests not rerun. 2026-11-03 (Tenis! racket rework): 170/172 EditMode (2 explicit diagnostics skipped), the 3 Tenis! PlayMode tests pass (practice: 19 rally balls in a row hit back by the scripted hand, none missed; the other games' PlayMode tests not rerun). 2026-10-31 (heat and frame rate on the box): 26/26 PlayMode, 163/165 EditMode (`DressArtTests` now expects the outfit atlas imported as data). 2026-10-22 (Sporte! rebuilt): 26/26 PlayMode, 163/165 EditMode (2 explicit diagnostics skipped). 2026-10-21 (placement check): 22/22 PlayMode,
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
- **The real box (X98K Pro, since 2026-10-30):** on the user's Wi-Fi at `192.168.1.46` (since 2026-11-03; before at
  `192.168.1.57` and `192.168.100.103`: its address changes, ask the user): `adb connect 192.168.1.46:5555`
  (this firmware never asks to authorise the laptop). Install and start: `run-on-mumu.ps1 -Device 192.168.1.46:5555`. **Unity's Android build restarts the adb server**, which drops network
  devices: connect again after every build. Batch build without the editor open (~3 min with a warm Library):
  `Unity.exe -batchmode -projectPath unity/MoveItGames -buildTarget Android -executeMethod
  MoveIt.EditorTools.BoxBuild.BuildApk -quit -logFile <log>`, scenes first with `MoveItSceneBuilder.BuildAll`. Frame
  rate: `adb logcat -s Unity | grep "\[Perf\]"` (`PerfLog`, every 10 s); temperatures and clocks:
  `/sys/class/thermal/thermal_zone0|1/temp` (SoC, GPU), `/sys/class/devfreq/fde60000.gpu/{cur_freq,load}`. From Git
  Bash: `export MSYS_NO_PATHCONV=1` first (it turns `/data/local/tmp` into a Windows path; then give adb.exe local files
  as `D:/...`, not `/d/...`: `adb install -r /d/...apk` fails and the old build stays), screenshots with
  `adb exec-out screencap -p > file.png`. PerfLab's `scene=` doesn't reload the scene that is open: `scene=Menu` first. **Trying render settings on the box** (`PerfLab`): write the file with the
  box's own shell, e.g. `adb shell "printf 'tag=t1\nscene=Runner\nshadows=0\n' >
  /sdcard/Android/data/al.tundu.games/files/perflab.txt"` (`adb push` can't write into the app's folder on Android
  11), then read the `[Perf] Runner [t1]` lines (the second one is steadier); delete the file to go back. Load scenes
  with `scene=`, not with remote keys: key presses get ahead of the game when frames stall and the script ends up in
  the wrong game. Don't switch post-processing effects mid-scene on OpenGL ES (colour grading without bloom ran at
  18 fps in Vrapo!). The camera AI's own numbers: `adb logcat -s MoveItMotion` (a stats line every 5 s: camera and AI
  fps, ms, NPU/GPU, the NPU's detector and landmark times, the frame size, "(idle)" while nobody is in view, then
  `gaps:` camera frames the AI never saw, times it lost the person, the slowest frame, how old frames are when their
  analysis starts and the camera's clock).
  **Measuring tools** (Git Bash, `tools/dev/`): `box-ai.sh [-s scene] [-m model] <seconds>` (the camera AI's stats
  lines, the game's `[Perf]` lines and the heat every ~10 s, together; needs someone in front of the camera),
  `box-scenes.sh Menu Runner Dress ...` (a line per scene: fps, frames
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
  evening photo was too dark for MediaPipe too. **INT8 builds** (2026-11-01, see the status bullet): pictures from the
  box camera (Settings → Kamera, → saves `camera-snapshot.jpg`; or PerfLab `scene=MotionDebug` + `input keyevent 22`
  in a loop) → `calibration.py --tflite <.task contents> --out <dir> <pictures>` → `convert.py --quant normal
  --calibration <dir> --out <dir>` (`<name>.rk3566.int8.rknn`, never into `models/` unless adopted) → `compare.py
  --crops --write-crops <dir>` (the held-out pictures' crops as RGBA) → on the box `pose_npu_test <det> <landmarks>
  <label> out <crops> --crop` → `compare.py --crops --box out --box-labels {model},{model}-int8`.
  `TUNDU_NPU_OUTPUT=float` makes the runtime dequantize INT8 outputs instead of the engine. For clean NPU timings the
  app's camera AI must be off: Tundu pauses it when another activity is in front (`am start -a
  android.settings.SETTINGS`); back with `am start -n al.tundu.games/com.unity3d.player.UnityPlayerActivity` (HOME
  shows a launcher chooser on this box, two home apps: never pick "Always").
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
  tests (153 tests on 2026-10-31, incl. `JointTimelineTests`; `PaintCanvasTests` need the
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
- **Measure the box only when it's quiet** (2026-11-08): after a restart Android recompiles apps in the background
  (`dex2oat`, e.g. Google Play Services: ~60 % of a core, load average 10, ~10 min), and every game then runs 18-27 fps
  with the main thread 2-3x slower: it looked like a regression in the new build. Before measuring: `adb shell "ps -A |
  grep -c dex2oat"` is 0 and `/proc/loadavg` under ~4.5; compare against another game measured the same minute.
- **Measure the box's GPU uncapped and with the clock pinned** (2026-11-06): at a 30 fps cap a cheaper frame only
  shows as an idle GPU, and above ~80 °C the power allocator moves the GPU clock between configurations (and GPU
  cycles per frame aren't comparable across clocks: memory stalls cost fewer cycles at a lower clock).
  `tools/dev/box-tennis-gpu.sh -g 300` (Tenis!) and `tools/dev/box-gpu-ab.sh -g 300` (any scene, "name|PerfLab
  settings" pairs) run PerfLab `fps=60` with the clock pinned; the hot box's real clock is 200-250 MHz, so that's where
  to compare. Full-screen copies are expensive on this GPU: check `androidBlitType` (Auto, not Always) and prefer
  `BoxGraphics.Settings.ScreenScale` to URP's render scale.
- **Tell the pose model's glitches from moves on raw samples** (Tenis! 2026-11-06): the low-lag One Euro track smears a
  one-frame jump into a two-frame "move" that passes any speed rule. A median of three takes one-frame glitches out for
  one sample of delay; a move measured from where the hand trustably was, carried by two steps, not all in one step and
  not straight back takes most of the rest (`SwingDetector`). For ground truth use two models on the very same frames
  (`box_clip.py` + `clip_models.py`, `swing_replay.py agree`): a label from the path's own median calls fast real
  reversals (waves) glitches.
- **Gestures glued to a noisy joint look broken** (Tenis! 2026-11-06): the racket following the pose model's wrist every
  frame shook and bent; the user asked for clean strokes read from the swing (speed, direction) instead. Prefer
  discrete gestures that trigger authored animation over mapping a magnified joint position onto a held object.
- **Programmer art reads as "AI games"** (Sporte! 2026-10-22): primitives for people, flat pale light, random-colour cube crowds and grey HUD boxes were judged not even ready to polish. Make real assets as code instead (Blender headless scripts with previews you look at, baked AO, a rig with authored clips, venues with bevels and textures), one art direction, a broadcast-style HUD. Render previews and look at them before wiring anything into Unity.
- **The Bash tool chokes on long inline scripts with apostrophes** ("unexpected EOF while looking for matching `''"), even in quoted heredocs: write the script to a file in the scratchpad and run that.
- **Inside an `Activity`, `Time` is the activity's own seconds and `Kit` its `ActivityKit`**: write `UnityEngine.Time.timeScale` / `.unscaledTime`, and don't name a type `Kit` (the athletes' outfit enum is `Wear`).
- **IMGUI refuses a `GUI.matrix` scaled by 0** and logs an error, which fails PlayMode tests: clamp pop-in scales.
- **Blender vertex colours for Unity**: store linear values and export the FBX with `colors_type="LINEAR"` (Unity passes them to the shader as they are). Generic rigs driven by Playables + IK need `optimizeGameObjects` off; meshes merged at runtime need `isReadable` and no mesh compression (uv1 holds exact region numbers): `SportsArtImport` does both.
- **A doubles partner moved by the same body stands in front of the ball** (Tenis! 2026-11-05): with the camera high
  behind the back player, a net player on the same line covered every ball coming to the back player (seen in the
  screenshots, not the tests). Offset it sideways (`NetOffset`) and look at the picture, not only at the rules.
- **A physical hit needs a ball the body can reach** (Tenis! 2026-11-03): the abstract timing rules never looked at the
  ball's height, and with no air drag a slow ball arcs high, comes down steep and kicked up to 3.7 m at the baseline.
  Measure where balls reach the player (a perfect simulated racket at every ball, per level) before tuning windows.
  To iterate on engine-free rules without the editor (this laptop has no .NET SDK): Unity's own
  `Editor/Data/NetCoreRuntime/dotnet.exe` + `Editor/Data/DotNetSdkRoslyn/csc.dll` compile them with
  `tools/logic-tests/UnityShim` and `Library/PackageCache/com.unity.ext.nunit@*/net40/unity-custom/nunit.framework.dll`
  (reference only the managed `System*.dll`, `mscorlib`, `netstandard` of `NetCoreRuntime/shared/Microsoft.NETCore.App/6.0.21`)
  into an exe that runs the [Test] methods by reflection: seconds per run instead of minutes.
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
- **IMGUI can cost more than the 3D** (Tenis!, 2026-11-07): a menu of SportHud panels (rounded: every pixel of the panel
  shaded, a draw per layer: shadow, gradient, rim) and outlined text (five or ten draws of the text) cost the box ~13 ms
  a frame, while every new 3D shadow and particle cost nothing measurable. One plain layer per panel, no outlines on dark
  panels, two-draw drop shadows over the scene; check any HUD with PerfLab `disable=MotionDebugOverlay,TennisGame`
  (`box-tennis-gpu.sh nogui`).
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
- **Judge a converted pose model on fixed crops, not through tracking** (2026-11-01): on dim living-room pictures even
  the float16 build ended ~10 px from MediaPipe end to end (a tiny difference moves the next crop, then everything),
  while on the same crops it is 0.3 px off: `compare.py --crops`. RKNN's quantization reads `.npy` calibration inputs
  channels first (3, h, w) whatever the model's layout; `quantized_algorithm` "kl" / "mmse" clip a regression output's
  range (world joints 135 / 16 cm off): look at every output, not only the cosine of the whole.
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
- **Take the motion rig's parts from `MotionSystem.Instance`, never `FindAnyObjectByType`** (2026-11-01): every scene
  builds its own rig (MotionSystem, gestures, input, hands, overlay); the menu's lives on and later ones remove
  themselves at the end of their first frame, so during that frame a search can return the doomed copy (the camera
  test then drew no skeleton at all). `BodyInput.Instance` / `BodyHands.Instance` keep the first instance.
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
