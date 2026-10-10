# Tundu's music and sounds

Style for every track: **Wii-era Nintendo feel** (Wii Sports / Mii Channel): bright mallets (marimba, vibraphone,
xylophone, glockenspiel), pizzicato, nylon guitar, flute or whistle, walking or bossa bass, light drums with
shakers, congas and brushes; major-7th chords; short seamless loops. Original tunes only.

## Which file plays where

| File (in `unity/MoveItGames/Assets/Resources/Music/`) | Where |
|---|---|
| `menu.ogg` | Home screen |
| `runner.ogg` | Vrapo! (the dino run) |
| `slash.ogg` | Fruta! |
| `bowling.ogg` | Bowling |
| `paint.ogg` | Pikturo! (painting) |
| `sword.ogg` | Shpata! ("Little Knights", a toy-soldier march) |
| `jingle_win.ogg` | New record, strike or spare (plays once) |
| `jingle_gameover.ogg` | Crash / end of a round (plays once) |

Sound effects are in `Assets/Resources/Sfx/` (`jump`, `land`, `whoosh`, `slide`, `egg_0`…`egg_7` (eggs in a row
climb a scale), `crash`, `count`, `go`, `ui_move`, `ui_select`, `ui_back`, `slice`, `splat`, `bomb`, `roll`,
`pins`, `ready`; Pikturo!: `paint_pick`, `paint_down`, `paint_brush` (a loop, louder the faster the brush moves),
`paint_stamp`, `paint_fill`, `paint_undo`, `paint_clear`, `paint_photo`; Shpata!: `sword_swish`, `sword_hit`,
`sword_clang` (wrong way, off the shield), `sword_block`, `sword_hurt`, `sword_poof` (a knight beaten), `sword_alert`).

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

No extra software is needed: the synthesizer is plain Python + numpy (`tools/music/synth.py`), and `ffmpeg`
encodes OGG (without ffmpeg you get WAV, which works too).
