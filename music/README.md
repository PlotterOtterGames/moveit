# Tundu's music and sounds

Style for every track: **Wii-era Nintendo feel** (Wii Sports / Mii Channel): bright mallets (marimba, vibraphone,
xylophone, glockenspiel), pizzicato, nylon guitar, flute or whistle, walking or bossa bass, light drums with
shakers, congas and brushes; major-7th chords; short seamless loops. Original tunes only.

## Which file plays where

| File (in `unity/MoveItGames/Assets/Resources/Music/`) | Where |
|---|---|
| `menu.ogg` | Home screen |
| `runner.ogg` | Vrapo!'s run ("Dino Dash", 2026-11-18: 65 s in sections: a log-drum fill, the marimba's tune, the tune with xylophone and steel drums, the whistle's song, a breather, the tune once more; `tools/music/runner_songs.py`) |
| `runner_title.ogg` | Vrapo!'s title at Dino's camp ("Dino's Camp", an easy bossa) |
| `dress.ogg` | Vishu! ("Sparkle Boutique", the dressing room) |
| `runway.ogg` | Vishu!'s fashion show ("Runway Glow", the catwalk) |
| `bowling.ogg` | Bowling |
| `paint.ogg` | Pikturo! (painting) |
| `sword.ogg` | Shpata! ("Little Knights", a toy-soldier march) |
| `fly.ogg` | Fluturo! ("Sky Patrol") |
| `tennis.ogg` | Tenis! ("Center Court", a sunny, bouncy court tune) |
| `space.ogg` | Yjet!'s menu ("Star Hopper") |
| `space_moon.ogg`, `space_candy.ogg`, `space_ice.ogg`, `space_jungle.ogg`, `space_star.ogg` | Yjet!'s flight, one after another ("Moonbeam Bossa", "Candy Comet", "Crystal Glide", "Jungle Jetpack", "Starlight Parade"; each plays once) |
| `fairy.ogg` | Zana!'s realm and menu ("Fairy Glade", celesta and harp) |
| `fairy_fly.ogg`, `fairy_fly2.ogg` | Zana!'s flight: the garden, the forest and the clouds ("Wings of Light"); the lake, the snow and the moon palace ("Sparkle Skies") |
| `fairy_boss.ogg` | Zana!'s guardians ("Shadow Bloom") |
| `jingle_win.ogg` | New record, strike or spare (plays once) |
| `jingle_gameover.ogg` | Crash / end of a round (plays once) |

Sound effects are in `Assets/Resources/Sfx/` (`jump`, `land`, `whoosh`, `slide`, `egg_0`…`egg_7` (eggs in a row
climb a scale), `step` (Vrapo!'s footsteps, played quietly), `egg_gold` (a golden egg), `milestone` (every 500 m), `record` (the old best passed), `crash`, `count`, `go`, `ui_move`, `ui_select`, `ui_back`, `roll`,
`pins`, `ready`; Pikturo!: `paint_pick`, `paint_down`, `paint_brush` (a loop, louder the faster the brush moves),
`paint_stamp`, `paint_fill`, `paint_undo`, `paint_clear`, `paint_photo`; Shpata!: `sword_swish`, `sword_hit`,
`sword_clang` (wrong way, off the shield), `sword_block`, `sword_hurt`, `sword_poof` (a knight beaten), `sword_alert`;
Vishu!: `dress_poof` (something put on), `dress_color` (a new colour), `dress_remove`, `dress_magic`, `dress_surprise`,
`dress_show` (a fashion show starts), `dress_curtain`, `dress_pose` (a pose counted), `dress_star` (a judge's star),
`dress_tick` (the last seconds of dressing time); Tenis!: `tennis_hit` (the racket's pok), `tennis_power` (a power
serve, a smash, a very fast ball), `tennis_bounce`, `tennis_net`, `tennis_swish` (a swing at the air), `tennis_toss`,
`tennis_cheer` (applause when the player wins a point), `tennis_aww`, `tennis_call` (the umpire's chime); Zana!:
`fairy_cast_swipe`, `fairy_cast_vee`, `fairy_cast_peak`, `fairy_cast_circle`, `fairy_cast_bolt`, `fairy_cast_heart` (a
shape cast: each its own little tune), `fairy_fizzle` (a shape not read), `fairy_pop` (a bubble popped), `fairy_purify` (a
creature turns happy), `fairy_chief`, `fairy_chief_saved` (a guardian arrives, is purified), `fairy_hit`, `fairy_shot`,
`fairy_charge`, `fairy_sparkle`, `fairy_ring`, `fairy_heart`, `fairy_ready` (the magic full), `fairy_magic` (the big
magic), `fairy_wave` (a wave cleared), `fairy_rescue` (a pixie freed)).

## Three ways to change the music

**1. Drop in your own file (easiest).** Make a track in any program, export it as **OGG** or WAV, and save it over
the file with the same name (e.g. `runner.ogg`). Unity picks it up; no code changes. Tips: make the end flow into
the start (it loops), avoid MP3 for loops (it adds a gap), keep it around 30–90 s.

**2. Edit my tracks.** The sources are MIDI files in `music/midi/` (`runner.mid`, `menu.mid`, ...). Open one in a
free music program, **MuseScore 4** (sheet music) or **LMMS** (piano roll), or any DAW. Change notes,
instruments (General MIDI programs), tempo, then save as MIDI and render:

    motion-service-py/.venv/Scripts/python tools/music/render.py runner

That writes `Assets/Resources/Music/runner.ogg` with Tundu's built-in Wii-style instruments. `--wav` also writes a
`.wav` next to the `.mid` to listen to. The loop ends at the `loop_end` marker (or the end of the last bar).
Instruments it knows: marimba, vibraphone, xylophone, glockenspiel, music box, celesta, steel drums, pizzicato,
harp, nylon guitar, upright/finger bass, electric piano, piano, flute, whistle, ocarina, soft lead and pad, plus
the General MIDI drum kit on channel 10 (program 40 on the drum channel = brushes).

**3. Compose new ones in code.** `tools/music/compose.py` writes the MIDI files from Python (chords, melodies,
grooves). It never overwrites an existing `.mid` unless you pass `--force <name>`, so your edits are safe.

Sound effects: `tools/music/sfx.py` regenerates `Assets/Resources/Sfx/`. Or drop in your own file with the same name.

## Yjet!'s songs

Yjet! plays its five songs one after another while the astronaut flies (the system's star picks the first). They are in
`tools/music/space_songs.py`; after changing one, render it:

    motion-service-py/.venv/Scripts/python tools/music/render.py space_moon

Or drop in your own `space_moon.ogg` (and the others) in `Assets/Resources/Music`: any length works, each plays once
and the next follows.

## Zana!'s songs

Zana!'s four songs are in `tools/music/fairy_songs.py` (`fairy`, `fairy_fly`, `fairy_fly2`, `fairy_boss`); they loop. After
changing one: `compose.py --force fairy_fly`, then `render.py fairy_fly`.

No extra software is needed: the synthesizer is plain Python + numpy (`tools/music/synth.py`), and `ffmpeg`
encodes OGG (without ffmpeg you get WAV, which works too).
