"""Tundu's soundtrack, composed as code and written out as editable MIDI files (music/midi/*.mid).

Wii-style on purpose (see music/README.md): bright mallets, pizzicato, nylon guitar, walking or bossa bass, light
drums with shakers and congas, major-7th / 9th chords, short seamless loops. Every tune here is original.

    python tools/music/compose.py              # writes the MIDI files that don't exist yet
    python tools/music/compose.py --force runner   # rewrites one (your edits to that .mid are lost!)

Once you edit a .mid in a music program, it's yours: render it with tools/music/render.py.
"""

import argparse
import os
import random
import sys

sys.path.insert(0, os.path.dirname(__file__))
import midi  # noqa: E402
from midi import PPQ  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MIDI_DIR = os.path.join(ROOT, "music", "midi")

E8 = PPQ // 2               # one eighth note in ticks
BAR = PPQ * 4

NAMES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}
CHORDS = {
    "": (0, 4, 7), "m": (0, 3, 7), "maj7": (0, 4, 7, 11), "m7": (0, 3, 7, 10), "7": (0, 4, 7, 10),
    "6": (0, 4, 7, 9), "m6": (0, 3, 7, 9), "maj9": (0, 4, 7, 11, 14), "m9": (0, 3, 7, 10, 14), "9": (0, 4, 7, 10, 14),
    "13": (0, 4, 10, 14, 21), "sus4": (0, 5, 7), "7sus4": (0, 5, 7, 10), "dim7": (0, 3, 6, 9),
    "m7b5": (0, 3, 6, 10), "add9": (0, 4, 7, 14), "69": (0, 4, 7, 9, 14),
}

# General MIDI programs (0-based)
MARIMBA, XYLOPHONE, VIBES, GLOCK, STEEL_DRUM = 12, 13, 11, 9, 114
PIZZ, NYLON, UPRIGHT, FINGER_BASS = 45, 24, 32, 33
EPIANO, FLUTE, WHISTLE, PAD = 4, 73, 78, 89
CELESTA = 8
OCARINA, HARP = 79, 46
BRUSH_KIT = 40              # program on the drum channel: brushes instead of sticks (our synth)

# drum notes
KICK, SNARE, RIM, CLAP, HAT, OPEN_HAT, RIDE, CRASH = 36, 38, 37, 39, 42, 46, 51, 49
TAMB, BONGO_HI, BONGO_LO, CONGA_MUTE, CONGA_HI, CONGA_LO = 54, 60, 61, 62, 63, 64
SHAKER, CLAVES, WOOD_HI, WOOD_LO, TRIANGLE = 70, 75, 76, 77, 81


def pitch(name):
    """'C5', 'Bb4', 'F#3' -> MIDI note number (C4 = 60)."""
    n = NAMES[name[0]]
    i = 1
    while i < len(name) and name[i] in "#b":
        n += 1 if name[i] == "#" else -1
        i += 1
    return n + 12 * (int(name[i:]) + 1)


def chord(symbol):
    """'Fmaj7', 'Bbm7', 'C7sus4' -> (root pitch class, intervals)."""
    root = NAMES[symbol[0]]
    i = 1
    while i < len(symbol) and symbol[i] in "#b":
        root += 1 if symbol[i] == "#" else -1
        i += 1
    return root % 12, CHORDS[symbol[i:]]


def bars_of(progression):
    """'Fmaj7 | Gm7 C7 | ...' -> list of (start tick, length ticks, chord symbol)."""
    out = []
    for b, bar in enumerate(p.strip() for p in progression.split("|")):
        names = bar.split()
        for k, name in enumerate(names):
            out.append((b * BAR + k * BAR // len(names), BAR // len(names), name))
    return out


class Song:
    def __init__(self, name, title, bpm, progression, swing=0.0, seed=1):
        self.name, self.title, self.bpm, self.swing = name, title, bpm, swing
        self.chords = bars_of(progression)
        self.length = max(s + l for s, l, _ in self.chords)
        self.tracks = []
        self.rng = random.Random(seed)

    def track(self, name, channel, program, volume=100, pan=64, reverb=40):
        t = midi.Track(name, channel, program, volume, pan, reverb)
        self.tracks.append(t)
        return t

    def at(self, tick, humanize=0):
        """Swing: off-beat eighths are pushed late by `swing` of an eighth."""
        if self.swing and tick % PPQ == E8:
            tick += int(self.swing * E8 * 0.67)
        return tick + (self.rng.randint(-humanize, humanize) if humanize else 0)

    def vel(self, v, spread=6):
        return max(1, min(127, v + self.rng.randint(-spread, spread)))

    # ---------------------------------------------------------------- parts

    def melody(self, track, notes, octave_shift=0, velocity=96, legato=0.92, start=0):
        """notes: [(name or 'r', eighths)]. Returns the end tick."""
        tick = start
        for name, eighths in notes:
            length = int(eighths * E8)
            if name != "r":
                track.note(self.at(tick, 4), length * legato, pitch(name) + 12 * octave_shift,
                           self.vel(velocity + (8 if tick % PPQ == 0 else 0), 5))
            tick += length
        return tick

    def bass(self, track, style, low=pitch("E1"), velocity=92):
        for i, (start, length, name) in enumerate(self.chords):
            root_pc, iv = chord(name)
            root = low + (root_pc - low) % 12
            fifth = root + 7 if root + 7 <= low + 19 else root - 5
            nxt = self.chords[(i + 1) % len(self.chords)][2]
            nxt_root = low + (chord(nxt)[0] - low) % 12
            approach = nxt_root - 1 if nxt_root > root else nxt_root + 1
            beats = length // PPQ
            if style == "bossa":            # root (dotted quarter) + fifth, the bossa nova heartbeat
                pattern = [(0, 1.5, root), (1.5, 0.5, fifth), (2, 1.5, fifth if beats == 4 else root), (3.5, 0.5, root)]
            elif style == "walk":           # quarter-note walking line ending on an approach note
                third = root + iv[1]
                pattern = [(0, 1, root), (1, 1, third), (2, 1, root + 7), (3, 1, approach)]
            elif style == "bounce":         # funky eighths
                pattern = [(0, 0.75, root), (0.75, 0.25, root), (1.5, 0.5, fifth), (2, 0.5, root + 12),
                           (2.5, 0.5, root), (3, 0.5, fifth), (3.5, 0.5, approach)]
            else:                           # "pump": root on each beat
                pattern = [(b, 0.9, root) for b in range(4)]
            for beat, dur, note in pattern:
                if beat >= beats:
                    continue
                track.note(self.at(start + int(beat * PPQ), 3), dur * PPQ * 0.92, note,
                           self.vel(velocity if beat == 0 else velocity - 10))

    def voicing(self, name, low, high, previous, drop_root=True, size=4):
        root_pc, iv = chord(name)
        tones = [(root_pc + i) % 12 for i in iv[(1 if drop_root and len(iv) > 3 else 0):]][:size]
        best, best_cost = None, None
        for base in range(low, low + 12):
            if base % 12 != tones[0]:
                continue
            notes, last = [base], base
            for pc in tones[1:]:
                n = last + ((pc - last) % 12 or 12)
                notes.append(n)
                last = n
            for shift in (-12, 0, 12):
                cand = [n + shift for n in notes]
                if cand[0] < low or cand[-1] > high:
                    continue
                cost = sum(abs(a - b) for a, b in zip(sorted(cand), sorted(previous))) if previous else 0
                if best_cost is None or cost < best_cost:
                    best, best_cost = cand, cost
        return best or [low + 12 + (t - low) % 12 for t in tones]

    def comp(self, track, rhythm, low=pitch("E3"), high=pitch("D5"), velocity=70, length=0.9, size=4):
        """Chord stabs. rhythm: list of (beat offset in the bar, duration in beats)."""
        prev = None
        for start, clen, name in self.chords:
            v = self.voicing(name, low, high, prev, size=size)
            prev = v
            for beat, dur in rhythm:
                tick = start + int(beat * PPQ)
                if tick >= start + clen:
                    continue
                for k, n in enumerate(v):
                    track.note(self.at(tick, 3) + k * 6, dur * PPQ * length, n, self.vel(velocity, 8))

    def arpeggio(self, track, low, high, step=0.5, velocity=70, pattern=(0, 1, 2, 3, 2, 1)):
        for start, clen, name in self.chords:
            v = self.voicing(name, low, high, None, drop_root=False)
            for k in range(int(clen / (step * PPQ))):
                n = v[pattern[k % len(pattern)] % len(v)]
                track.note(self.at(start + int(k * step * PPQ), 2), step * PPQ * 0.9, n, self.vel(velocity, 8))

    def drums(self, track, bars):
        """bars: {note: 'x.o.X...' 16 steps per bar (X loud, x normal, o soft)}, repeated over the whole song."""
        n_bars = self.length // BAR
        for b in range(n_bars):
            for note, steps in bars.items():
                steps = steps.replace(" ", "")
                bar_steps = steps[(b * 16) % len(steps):(b * 16) % len(steps) + 16]
                for s, ch in enumerate(bar_steps):
                    if ch in "xXo":
                        v = {"X": 112, "x": 92, "o": 62}[ch]
                        tick = b * BAR + s * PPQ // 4
                        if self.swing and s % 4 == 2:
                            tick += int(self.swing * E8 * 0.67)
                        track.note(tick + self.rng.randint(-5, 5), PPQ // 4, note, self.vel(v, 7))

    def write(self, folder=MIDI_DIR, loop=True):
        os.makedirs(folder, exist_ok=True)
        path = os.path.join(folder, f"{self.name}.mid")
        midi.write(path, self.tracks, self.bpm, loop_end_tick=self.length if loop else None, title=self.title)
        return path


def M(text):
    """Melody shorthand: 'C5:1 r:1 F5:2' -> [('C5', 1), ('r', 1), ('F5', 2)] (durations in eighths)."""
    out = []
    for tok in text.replace("|", " ").split():         # bar lines are just for reading
        name, _, d = tok.partition(":")
        out.append((name, float(d) if d else 1.0))
    return out


# ---------------------------------------------------------------------------------------------------- songs

def runner():
    """Vrapo! 'Dino Dash': bouncy tropical run, marimba + steel drum, congas and claps."""
    s = Song("runner", "Dino Dash", 132,
             "Fmaj7 | Gm7 C7 | Am7 Dm7 | Gm7 C7 | Fmaj7 | Bbmaj7 | Gm7 | C7sus4 C7 |"
             "Bbmaj7 | C7 | Am7 | Dm7 | Gm7 | C7 | Fmaj7 Dm7 | Gm7 C7", seed=3)
    lead = s.track("Marimba", 0, MARIMBA, 112, 56, 45)
    steel = s.track("Steel drum", 1, STEEL_DRUM, 84, 84, 50)
    pizz = s.track("Pizzicato", 2, PIZZ, 82, 40, 35)
    bass = s.track("Bass", 3, FINGER_BASS, 104, 64, 15)
    whistle = s.track("Whistle", 4, WHISTLE, 70, 76, 55)
    kit = s.track("Drums", 9, 0, 96, 64, 25)
    a = M("r:1 C5 F5 A5:2 G5 F5 E5 | D5 F5:2 E5 D5 C5 Bb4 C5 | A4:2 C5 E5:2 D5 C5:2 | Bb4 D5 G5 F5 E5:2 C5:2 |"
          "r:1 C5 F5 A5:2 C6:2 A5 | Bb5:2 A5 F5 D5:2 F5:2 | G5 F5 E5 D5 Bb4:2 D5:2 | C5:4 r:1 E5 G5 Bb5")
    b = M("A5:2 F5 D5 F5:2 A5:2 | G5:2 E5 C5 E5:2 G5:2 | C6 A5 E5:2 G5 A5 C6:2 | D6:3 C6 A5:2 F5:2 |"
          "Bb5 A5 G5 F5 D5:2 G5:2 | E5 F5 G5 Bb5 A5:2 G5:2 | F5:3 E5 D5:2 A4:2 | Bb4:2 C5 D5 E5:2 r:2")
    end = s.melody(lead, a, velocity=100)
    s.melody(lead, b, velocity=104, start=end)
    s.melody(steel, b, octave_shift=-1, velocity=80, start=8 * BAR)          # steel drum doubles the B tune
    for bar, notes in ((1, "r:6 F5 A5"), (3, "r:6 C6 A5"), (5, "r:6 D6 C6"), (7, "r:4 C6:2 Bb5:2")):
        s.melody(steel, M(notes), velocity=76, start=bar * BAR)              # little answers in the A section
    s.comp(pizz, [(0.5, 0.25), (1.5, 0.25), (2.5, 0.25), (3.5, 0.25)], low=pitch("F3"), high=pitch("C5"),
           velocity=70, size=3)
    s.bass(bass, "bounce", low=pitch("E1"))
    for bar in (7, 15):                                                      # "whee!" whistle into each section
        s.melody(whistle, M("r:4 C6:1 D6:1 E6:1 F6:1"), velocity=80, start=bar * BAR)
    s.drums(kit, {
        KICK:      "x.....x.x.....x.",
        CLAP:      "....x.......x...",
        SHAKER:    "oxoxoxoxoxoxoxox",
        CONGA_HI:  "...x..x....x..x." + "...x..x....x.x.x",
        CONGA_LO:  "..........x....." + "........x.......",
        WOOD_HI:   "................" * 3 + "............x.x.",
        TRIANGLE:  "x..............." + "................" * 7,
        TAMB:      "................" * 8 + "..x...x...x...x." * 8,
    })
    return s


def menu():
    """Home screen 'Tundu Lounge': gentle bossa nova, vibraphone over nylon guitar."""
    s = Song("menu", "Tundu Lounge", 116,
             "Cmaj7 | Am7 | Dm7 | G7 | Cmaj7 | C7 | Fmaj7 | Fm6 |"
             "Em7 | A7 | Dm7 | G7 | Em7 A7 | Dm7 G7 | C6 | G7sus4 G7", swing=0.25, seed=5)
    vib = s.track("Vibraphone", 0, VIBES, 108, 60, 60)
    glock = s.track("Glockenspiel", 1, GLOCK, 52, 80, 70)
    gtr = s.track("Nylon guitar", 2, NYLON, 92, 42, 40)
    bass = s.track("Upright bass", 3, UPRIGHT, 108, 64, 20)
    pad = s.track("Pad", 4, PAD, 46, 64, 80)
    kit = s.track("Brushes", 9, BRUSH_KIT, 84, 64, 35)
    tune = M("E5:3 D5 E5 G5:3 A5:2 G5 E5 C5:4 D5 E5 F5:2 A5:2 C6:2 B5:3 A5 G5:2 F5:2 "
             "E5:3 D5 E5 G5:2 B5 Bb5:2 A5 G5 E5:2 C5:2 A5:3 G5 F5:2 E5:2 Ab5:3 G5 F5:2 D5:2 "
             "G5:2 E5 B4 D5:2 E5:2 C#5:2 E5 G5 A5:4 F5:2 E5 D5 A5:2 F5:2 G5:3 F5 D5:2 B4:2 "
             "E5:2 G5:2 C#5:2 E5:2 D5:2 F5:2 B4:2 D5:2 C5 E5 G5 A5 C6:3 r:1 r:4 G5 F5 D5 B4")
    s.melody(vib, tune, velocity=92, legato=0.98)
    s.melody(glock, tune[len(tune) // 2:], octave_shift=1, velocity=60,
             start=sum(int(d * E8) for _, d in tune[:len(tune) // 2]))       # sparkle in the second half
    s.comp(gtr, [(0, 0.5), (0.75, 0.25), (1.5, 0.5), (2.5, 0.5), (3.25, 0.5)], low=pitch("E3"), high=pitch("A4"),
           velocity=66)
    s.comp(pad, [(0, 4)], low=pitch("G3"), high=pitch("E5"), velocity=50, length=0.98)
    s.bass(bass, "bossa", low=pitch("E1"))
    s.drums(kit, {
        RIM:      "x..x..x...x..x.." + "..x..x..x..x...x",     # bossa clave on the rim
        SNARE:    "....o.......o...",                         # brush taps
        SHAKER:   "o.x.o.x.o.x.o.x.",
        KICK:     "o.......o.......",
        TRIANGLE: "x..............." + "................" * 3,
    })
    return s


def slash():
    """Fruta! 'Fruit Frenzy': quick and playful, xylophone, pizzicato and bongos."""
    s = Song("slash", "Fruit Frenzy", 148,
             "G | Em | C | D7 | G | Em | Am7 D7 | G | C | D | Bm7 | Em | Am7 | D7 | G Em | Am7 D7", seed=7)
    lead = s.track("Xylophone", 0, XYLOPHONE, 108, 58, 40)
    marim = s.track("Marimba", 1, MARIMBA, 84, 80, 40)
    pizz = s.track("Pizzicato", 2, PIZZ, 86, 42, 30)
    bass = s.track("Bass", 3, PIZZ, 100, 64, 15)
    kit = s.track("Drums", 9, 0, 92, 64, 25)
    a = M("D5 G5 B5 G5 A5 B5 D6:2 B5 G5 E5 G5 B5:2 E5:2 C5 E5 G5 C6 B5 A5 G5:2 F#5 A5 C6 A5 F#5:2 D5:2 "
          "D5 G5 B5 D6 E6 D6 B5:2 G5 E5 B4 E5 G5:2 B5:2 C6 B5 A5 E5 F#5 A5 C6 A5 G5:3 D5 G5:2 r:2")
    b = M("E6:2 C6 G5 E5:2 G5:2 F#6:2 D6 A5 F#5:2 A5:2 D6 B5 F#5 B5 D6:2 A5:2 G5 E5 B4 E5 G5 B5 E6:2 "
          "E6 C6 A5 C6 E6:2 D6:2 C6 A5 F#5 D5 F#5 A5 C6:2 B5:2 D6:2 E6:2 B5:2 C6 A5 E5 A5 F#5 A5 D6:2")
    end = s.melody(lead, a, velocity=100, legato=0.6)
    s.melody(lead, b, velocity=104, legato=0.6, start=end)
    s.arpeggio(marim, pitch("G3"), pitch("D5"), step=0.5, velocity=58, pattern=(0, 2, 1, 3))
    s.comp(pizz, [(0.5, 0.25), (1.5, 0.25), (2.5, 0.25), (3.5, 0.25)], low=pitch("G3"), high=pitch("D5"),
           velocity=66, size=3)
    s.bass(bass, "pump", low=pitch("E2"), velocity=88)
    s.drums(kit, {
        KICK:     "x.......x.......",
        CLAP:     "....x.......x...",
        BONGO_HI: "..x...xx..x...x.",
        BONGO_LO: "x.....x.......x.",
        HAT:      "o.o.o.o.o.o.o.o.",
        WOOD_HI:  "................" * 7 + "........x.x.x.x.",
        TAMB:     "................" * 8 + "....x.......x..." * 8,
    })
    return s


def bowling():
    """Bowling 'Strike Lounge': easy swing, flute over electric piano, walking bass, brushes and ride."""
    s = Song("bowling", "Strike Lounge", 104,
             "Bbmaj7 | G7 | Cm7 | F7 | Dm7 | G7 | Cm7 | F7 |"
             "Ebmaj7 | Edim7 | Bbmaj7 | G7 | Cm7 | F7 | Bb6 G7 | Cm7 F7", swing=1.0, seed=9)
    flute = s.track("Flute", 0, FLUTE, 100, 58, 55)
    vib = s.track("Vibraphone", 1, VIBES, 70, 80, 60)
    ep = s.track("Electric piano", 2, EPIANO, 82, 46, 45)
    bass = s.track("Upright bass", 3, UPRIGHT, 108, 64, 20)
    kit = s.track("Brushes", 9, BRUSH_KIT, 84, 64, 35)
    tune = M("D5:2 F5 A5 Bb5:2 A5:2 G5:2 F5 D5 B4:4 C5 Eb5 G5 Bb5 A5:2 G5:2 F5:3 Eb5 D5:2 C5:2 "
             "D5:2 F5 A5 C6:2 A5:2 B5:3 G5 F5:2 D5:2 Eb5:2 G5 Bb5 A5 G5 F5:2 F5:4 r:2 F5 G5 "
             "G5:2 Bb5 D6 C6:2 Bb5:2 Bb5:2 G5 E5 Db5:2 E5:2 F5:3 D5 F5:2 A5:2 Ab5 G5 F5 D5 B4:4 "
             "C5 Eb5 G5 Bb5 D6:2 C6:2 A5:2 F5 Eb5 C5:2 A4:2 Bb4:2 D5:2 G5:2 B4:2 C5 Eb5 G5 Bb5 A5:2 F5:2")
    s.melody(flute, tune, velocity=88, legato=0.95)
    s.melody(vib, tune[len(tune) // 2:], octave_shift=-1, velocity=56, legato=0.95,
             start=sum(int(d * E8) for _, d in tune[:len(tune) // 2]))
    s.comp(ep, [(0, 1.5), (1.5, 0.5), (2.5, 1.0)], low=pitch("F3"), high=pitch("C5"), velocity=62)
    s.bass(bass, "walk", low=pitch("E1"))
    s.drums(kit, {
        RIDE:  "x...x.x.x...x.x.",            # ding, ding-a ding (swung)
        SNARE: "....o.......o...",
        HAT:   "....x.......x...",
        KICK:  "o.......o.......",
    })
    return s


def paint():
    """Pikturo! 'Paint Box': an easy, curious stroll for painting: celesta and vibes over pizzicato, nylon guitar
    arpeggios, walking bass and brushes."""
    s = Song("paint", "Paint Box", 100,
             "Cmaj7 | Am7 | Dm7 | G7 | Em7 | A7 | Dm7 | G7 |"
             "Fmaj7 | Fm6 | Em7 | A7 | Dm7 | G7 | C6 | G7sus4 G7", swing=0.3, seed=21)
    cel = s.track("Celesta", 0, CELESTA, 104, 72, 65)
    vib = s.track("Vibraphone", 1, VIBES, 80, 52, 60)
    pizz = s.track("Pizzicato", 2, PIZZ, 76, 40, 35)
    gtr = s.track("Nylon guitar", 3, NYLON, 66, 86, 40)
    bass = s.track("Upright bass", 4, UPRIGHT, 100, 64, 20)
    kit = s.track("Brushes", 9, BRUSH_KIT, 74, 64, 35)
    tune = M("E5:2 G5 B5 A5:2 G5:2 | E5:3 C5 E5:2 A4:2 | F5:2 A5 C6 B5:2 A5:2 | G5:4 r:2 D5 F5 | "
             "E5:2 G5 B5 D6:2 B5:2 | C#6:3 A5 G5:2 E5:2 | F5:2 E5 D5 A5:2 F5:2 | D5:4 G4:2 B4 D5 | "
             "C5:2 F5 A5 C6:2 E6:2 | D6:3 C6 Ab5:2 F5:2 | G5:2 E5 G5 B5:2 G5:2 | A5:3 G5 E5:2 C#5:2 | "
             "D5:2 F5 A5 C6:2 A5:2 | B5:2 G5 F5 D5:2 B4:2 | C5 E5 G5 A5 C6:3 r:1 | r:4 G5 F5 D5 B4")
    s.melody(cel, tune, velocity=90, legato=0.9)
    half = len(tune) // 2
    s.melody(vib, tune[half:], octave_shift=-1, velocity=58, legato=0.95, start=sum(int(d * E8) for _, d in tune[:half]))
    s.comp(pizz, [(1, 0.25), (3, 0.25)], low=pitch("G3"), high=pitch("D5"), velocity=60, size=3)
    s.arpeggio(gtr, pitch("C3"), pitch("G4"), step=0.5, velocity=50, pattern=(0, 2, 1, 3, 2, 1, 3, 2))
    s.bass(bass, "walk", low=pitch("E1"), velocity=84)
    s.drums(kit, {
        SNARE:    "....o.......o...",
        SHAKER:   "o.o.o.o.o.o.o.o.",
        KICK:     "o.......o.......",
        TRIANGLE: "x..............." + "................" * 7,
    })
    return s


def sword():
    """Shpata! 'Little Knights': a jaunty toy-soldier march, ocarina over pizzicato and harp, snare and tambourine."""
    s = Song("sword", "Little Knights", 126,
             "D | G | A7 | D | Bm | G | Em7 A7 | D | G | A | F#m7 | Bm | Em7 | A7 | D G | Em7 A7", seed=27)
    lead = s.track("Ocarina", 0, OCARINA, 108, 60, 55)
    flute = s.track("Flute", 1, FLUTE, 74, 84, 60)
    harp = s.track("Harp", 2, HARP, 70, 40, 50)
    pizz = s.track("Pizzicato", 3, PIZZ, 84, 50, 30)
    bass = s.track("Upright bass", 4, UPRIGHT, 100, 64, 20)
    kit = s.track("Drums", 9, 0, 92, 64, 30)
    a = M("D5:2 F#5 A5 D6:3 A5 | B5:2 G5 B5 D6:2 B5:2 | A5:2 C#6 E6 A5:2 G5 E5 | F#5:4 D5:2 r:2 | "
          "F#5:2 B5 C#6 D6:2 B5:2 | G5:3 A5 B5:2 G5:2 | E5 G5 B5 E6 C#6:2 A5:2 | D6:4 r:2 A5 B5")
    b = M("D6:2 B5 G5 B5:2 D6:2 | C#6:2 A5 E5 A5:2 C#6:2 | A5:3 F#5 C#6:2 A5:2 | B5:4 F#5:2 B5:2 | "
          "G5:2 E5 G5 B5:2 D6:2 | C#6:2 E6 C#6 A5:2 G5:2 | F#5 A5 D6 F#6 G6:2 D6:2 | E6:2 C#6 A5 E5:2 r:2")
    end = s.melody(lead, a, velocity=100, legato=0.85)
    s.melody(lead, b, velocity=104, legato=0.85, start=end)
    s.melody(flute, b, octave_shift=1, velocity=64, legato=0.8, start=end)      # the flute joins in the second half
    s.arpeggio(harp, pitch("D3"), pitch("A4"), step=0.5, velocity=52, pattern=(0, 1, 2, 3))
    s.comp(pizz, [(1, 0.25), (3, 0.25)], low=pitch("F#3"), high=pitch("D5"), velocity=72, size=3)   # oom-PAH
    s.bass(bass, "pump", low=pitch("E1"), velocity=90)
    s.drums(kit, {
        KICK:     "x.......x.......",
        SNARE:    "x.oox.o.x.oox.ox" + "x.oox.o.x.o.xxxx",     # a toy-soldier march, a roll every other bar
        TAMB:     "....x.......x...",
        CRASH:    "x..............." + "................" * 7,
        TRIANGLE: "................" * 7 + "............x...",
    })
    return s


def jingle_win():
    """New record: a quick rising fanfare that lands on a sparkling chord."""
    s = Song("jingle_win", "Bravo!", 140, "C | Cmaj9", seed=11)
    mar = s.track("Marimba", 0, MARIMBA, 112, 56, 50)
    glock = s.track("Glockenspiel", 1, GLOCK, 70, 80, 70)
    pizz = s.track("Pizzicato", 2, PIZZ, 90, 44, 40)
    kit = s.track("Drums", 9, 0, 90, 64, 40)
    run = M("C5:0.5 E5:0.5 G5:0.5 C6:0.5 E5:0.5 G5:0.5 C6:0.5 E6:0.5 G5:0.5 C6:0.5 E6:0.5 G6:0.5 r:2")
    s.melody(mar, run, velocity=100, legato=0.8)
    for n in ("C5", "E5", "G5", "B5", "D6"):
        mar.note(BAR, BAR * 0.9, pitch(n), 96)
    for n in ("E6", "G6", "C7"):
        glock.note(BAR + 40, BAR * 0.9, pitch(n), 80)
    for n in ("C3", "G3", "E4"):
        pizz.note(BAR, PPQ, pitch(n), 100)
    kit.note(BAR, PPQ, TRIANGLE, 100)
    kit.note(BAR, PPQ, CRASH, 70)
    return s


def jingle_gameover():
    """Crash: a friendly 'aww' going down, then a soft chord: try again!"""
    s = Song("jingle_gameover", "Oops", 100, "Fmaj7 | Cmaj7", seed=13)
    mar = s.track("Marimba", 0, MARIMBA, 108, 60, 50)
    pizz = s.track("Pizzicato", 1, PIZZ, 90, 44, 40)
    vib = s.track("Vibraphone", 2, VIBES, 80, 76, 60)
    s.melody(mar, M("G5:2 F#5:2 F5:2 E5:2"), velocity=96, legato=0.9)
    for n in ("F2", "C3", "A3"):
        pizz.note(0, PPQ, pitch(n), 90)
    for n in ("C3", "G3", "E4"):
        pizz.note(BAR, PPQ, pitch(n), 90)
    for n in ("E4", "G4", "B4", "D5"):
        vib.note(BAR, BAR * 0.8, pitch(n), 70)
    return s


SONGS = {f.__name__: f for f in (menu, runner, slash, bowling, paint, sword, jingle_win, jingle_gameover)}


def main():
    ap = argparse.ArgumentParser(description="Write Tundu's soundtrack as MIDI files")
    ap.add_argument("songs", nargs="*", help=f"which ones (default: all): {', '.join(SONGS)}")
    ap.add_argument("--force", action="store_true", help="overwrite existing .mid files (your edits are lost)")
    args = ap.parse_args()
    for name in args.songs or SONGS:
        path = os.path.join(MIDI_DIR, f"{name}.mid")
        if os.path.exists(path) and not args.force:
            print(f"  keep   {path} (exists; --force to rewrite)")
            continue
        song = SONGS[name]()
        song.write(loop=not name.startswith("jingle_"))
        print(f"  wrote  {path}  ({song.title}, {song.bpm} BPM, {song.length // BAR} bars)")


if __name__ == "__main__":
    main()
