"""Yjet!'s songs (2026-11-14): one per planet, each the trip there. Original tunes, Wii-style like the rest of Tundu's
music (see music/README.md), with a steady beat: the stars the player catches are the melody's notes
(tools/space/charts.py reads them from the "Lead" track, channel 1, and the section markers).

Every song has the same shape, which the game follows (the launch from the planet you leave, the set piece of the
chorus, the meteor shower of the bridge, the planet ahead growing in the last chorus, the landing):

    intro 4 bars | verse 8 | chorus 8 | bridge 8 (meteors and comets: step aside, duck) | chorus2 8 | outro 4

The markers in the MIDI file ("section verse", "piece asteroids", "end") are what the charts use: keep them when you
edit a song in a music program, then run tools/space/charts.py (and tools/music/render.py) again.

    python tools/music/compose.py --force space_moon     # rewrite one from this file (your edits to the .mid are lost)
"""

import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from compose import (BAR, CELESTA, CLAP, CONGA_HI, CONGA_LO, CONGA_MUTE, CRASH, EPIANO, FINGER_BASS, FLUTE,  # noqa: E402
                     GLOCK, HAT, HARP, KICK, M, MARIMBA, OCARINA, OPEN_HAT, PAD, PIZZ, RIDE, RIM, SHAKER, SNARE,
                     STEEL_DRUM, TAMB, TRIANGLE, UPRIGHT, VIBES, WHISTLE, XYLOPHONE, BONGO_HI, BONGO_LO, BRUSH_KIT,
                     Song, pitch)

SECTIONS = (("intro", 4), ("verse", 8), ("chorus", 8), ("bridge", 8), ("chorus2", 8), ("outro", 4))


class Trip:
    """A song in Yjet!'s shape: each section its own chords; the markers the charts read."""

    def __init__(self, name, title, bpm, chords, pieces, seed):
        progression = " | ".join(chords[s] for s, _ in SECTIONS)
        self.song = Song(name, title, bpm, progression, seed=seed)
        self.span = {}
        tick = 0
        for section, bars in SECTIONS:
            n = len(chords[section].split("|"))
            assert n == bars, (name, section, n, bars)
            self.span[section] = (tick, tick + bars * BAR)
            tick += bars * BAR
        assert tick == self.song.length, (name, tick, self.song.length)
        for section, _ in SECTIONS:
            self.song.markers.append((self.span[section][0], f"section {section}"))
            if section in pieces:
                self.song.markers.append((self.span[section][0], f"piece {pieces[section]}"))
        self.song.markers.append((tick, "end"))

    def bar(self, section, k=0):
        return self.span[section][0] + k * BAR

    def spans(self, *sections):
        return [self.span[s] for s in sections]

    def tune(self, track, section, text, bar=0, **kw):
        """A melody in a section from its bar `bar` (checked: whole bars of eighths)."""
        notes = M(text)
        eighths = sum(d for _, d in notes)
        assert abs(eighths % 8) < 1e-6, (self.song.name, section, eighths)
        return self.song.melody(track, notes, start=self.bar(section, bar), **kw)


def tracks(s, lead_program, lead_volume=104, double_program=GLOCK, double_volume=56, bass_program=FINGER_BASS,
           brushes=False):
    lead = s.track("Lead", 0, lead_program, lead_volume, 60, 55)          # the stars: tools/space/charts.py reads it
    double = s.track("Sparkle", 1, double_program, double_volume, 80, 70)
    pad = s.track("Pad", 2, PAD, 52, 64, 75)
    pizz = s.track("Pizzicato", 3, PIZZ, 64, 44, 45)
    bass = s.track("Bass", 4, bass_program, 98, 64, 18)
    kit = s.track("Brushes" if brushes else "Drums", 9, BRUSH_KIT if brushes else 0, 84, 64, 30)
    return lead, double, pad, pizz, bass, kit


def outro_chord(s, t, track, notes, velocity=80):
    """The last chord, held from the outro's third bar (the final double star) to the end."""
    for n in notes:
        track.note(t.bar("outro", 2), BAR * 1.9, pitch(n), velocity)


# ---------------------------------------------------------------------------------------------------------- the songs

def space_moon():
    """The Moon, 'Moonbeam Bossa': the first, gentlest trip. A soft bossa: vibraphone over a pad, pizzicato twinkling,
    an upright bass's bossa heartbeat, brushes and a shaker. The chorus sways in 3-3-2."""
    t = Trip("space_moon", "Moonbeam Bossa", 104, {
        "intro": "Fmaj7 | Dm7 | Bbmaj7 | C7sus4 C7",
        "verse": "Fmaj7 | Am7 | Bbmaj7 | C7 | Fmaj7 | Dm7 | Gm7 | C7",
        "chorus": "Bbmaj7 | C7 | Am7 | Dm7 | Gm7 | C7 | Fmaj7 Dm7 | Gm7 C7",
        "bridge": "Dm7 | Bbmaj7 | Dm7 | Bbmaj7 | Gm7 | Am7 | Bbmaj7 | C7sus4 C7",
        "chorus2": "Bbmaj7 | C7 | Am7 | Dm7 | Gm7 | C7 | Fmaj7 Dm7 | Gm7 C7",
        "outro": "Fmaj7 | Bbmaj7 | Fmaj7 | Fmaj7",
    }, {"intro": "launch", "chorus": "asteroids", "bridge": "nebula", "chorus2": "arrival", "outro": "landing"}, seed=201)
    s = t.song
    lead, double, pad, pizz, bass, kit = tracks(s, VIBES, bass_program=UPRIGHT, double_program=CELESTA, brushes=True)
    t.tune(lead, "intro", "F5:4 A5:4 | G5:4 r:2 C5:2", bar=2)
    verse = "C5:2 F5:2 A5:4 | G5:2 E5:2 C5:4 | D5:2 F5:2 A5:2 Bb5:2 | G5:6 r:2 | " \
            "C5:2 F5:2 A5:2 C6:2 | D6:4 A5:4 | Bb5:2 A5:2 G5:2 F5:2 | E5:4 G5:2 C5:2"
    chorus = "D6:3 C6:3 Bb5:2 | G5:3 Bb5:3 C6:2 | E6:3 D6:3 C6:2 | A5:6 r:2 | " \
             "D6:3 F6:3 D6:2 | E6:3 C6:3 Bb5:2 | A5:3 C6:3 D6:2 | Bb5:3 G5:3 C6:2"
    t.tune(lead, "verse", verse, velocity=92, legato=0.9)
    t.tune(lead, "chorus", chorus, velocity=98, legato=0.85)
    t.tune(double, "chorus", chorus, octave_shift=1, velocity=60, legato=0.5)
    t.tune(lead, "bridge", "D5:2 r:2 F5:2 A5:2 | Bb5:4 A5:2 F5:2 | D5:2 r:2 A5:2 D6:2 | D6:4 Bb5:2 F5:2 | "
                           "G5:2 r:2 Bb5:2 D6:2 | C6:4 A5:2 E5:2 | D5:2 F5:2 A5:2 Bb5:2 | C6:4 r:4", velocity=90)
    t.tune(lead, "chorus2", chorus.replace("Bb5:3 G5:3 C6:2", "G5:2 A5:2 Bb5:2 C6:2"), velocity=102, legato=0.85)
    t.tune(double, "chorus2", chorus, octave_shift=1, velocity=66, legato=0.5)
    t.tune(lead, "outro", "C6:2 A5:2 F5:4 | D6:2 Bb5:2 F5:4 | F5:8 | r:8", velocity=96)
    outro_chord(s, t, double, ("A5", "C6", "E6", "F6"), 70)
    for sp in t.spans("intro", "verse", "chorus", "bridge", "chorus2"):
        s.comp(pad, [(0, 4)], low=pitch("E3"), high=pitch("C5"), velocity=48, length=0.98, size=4, span=sp)
    for sp in t.spans("intro", "verse", "chorus2"):
        s.arpeggio(pizz, pitch("F4"), pitch("C6"), step=0.5, velocity=50, pattern=(0, 1, 2, 3, 2, 1, 2, 3), span=sp)
    s.comp(pizz, [(0, 0.5), (1.5, 0.5), (3, 0.5)], low=pitch("A3"), high=pitch("F5"), velocity=58, size=3, span=t.span["chorus"])
    for sp in t.spans("verse", "chorus", "bridge", "chorus2"):
        s.bass(bass, "bossa", low=pitch("E1"), velocity=92, span=sp)
    s.bass(bass, "bossa", low=pitch("E1"), velocity=84, span=(t.bar("outro"), t.bar("outro", 2)))
    bass.note(t.bar("outro", 2), BAR * 1.5, pitch("F1"), 96)
    s.drums(kit, {SHAKER: "o.o.o.o.o.o.o.o.", TRIANGLE: "x..............." + "................" * 3}, span=t.span["intro"])
    s.drums(kit, {KICK: "x.....x.x.......", RIM: "....x.......x...", SHAKER: "o.o.o.o.o.o.o.o."}, span=t.span["verse"])
    groove = {KICK: "x.....x.x.....x.", SNARE: "....x.......x...", HAT: "x.x.x.x.x.x.x.x.",
              CRASH: "x..............." + "................" * 7}
    s.drums(kit, groove, span=t.span["chorus"])
    s.drums(kit, {**groove, TAMB: "....x.......x..."}, span=t.span["chorus2"])
    s.drums(kit, {KICK: "x...x...x...x...", RIM: "..x...x...x...x.", SHAKER: "xoxoxoxoxoxoxoxo",
                  CONGA_HI: "......x.......x.", CONGA_LO: "x.......x.......",
                  CRASH: "x..............." + "................" * 7}, span=t.span["bridge"])
    s.drums(kit, {SHAKER: "o.o.o.o.o.o.o.o.", CRASH: "................" * 2 + "x..............." + "................",
                  TRIANGLE: "................" * 2 + "x..............." + "................"}, span=t.span["outro"])
    return s


def space_candy():
    """The candy planet, 'Candy Comet': bouncy and bright. Xylophone and steel drum over electric piano offbeats, a
    bouncing bass, claps and hats; eighth-note runs like sweets rolling out of a jar."""
    t = Trip("space_candy", "Candy Comet", 112, {
        "intro": "C | Am7 | Dm7 | G7",
        "verse": "C | Em7 | F | G7 | C | Am7 | Dm7 | G7",
        "chorus": "F | G | Em7 | Am7 | Dm7 | G7 | C Am7 | Dm7 G7",
        "bridge": "Am7 | F | Am7 | F | Dm7 | Em7 | F | G7sus4 G7",
        "chorus2": "F | G | Em7 | Am7 | Dm7 | G7 | C Am7 | Dm7 G7",
        "outro": "C | F | C | C",
    }, {"intro": "launch", "chorus": "rings", "bridge": "nebula", "chorus2": "arrival", "outro": "landing"}, seed=202)
    s = t.song
    lead, double, pad, pizz, bass, kit = tracks(s, XYLOPHONE, double_program=STEEL_DRUM, double_volume=62)
    ep = s.track("Electric piano", 5, EPIANO, 60, 44, 35)
    t.tune(lead, "intro", "D6:2 C6:2 A5:4 | B5:2 D6:2 G5:2 r:2", bar=2)
    verse = "E5:1 G5:1 C6:2 G5:1 E5:1 G5:2 | G5:2 B5:1 G5:1 E5:4 | A5:1 C6:1 F6:2 E6:1 C6:1 A5:2 | B5:2 D6:2 G5:4 | " \
            "E5:1 G5:1 C6:2 E6:2 D6:1 C6:1 | A5:2 E6:2 C6:4 | D6:1 C6:1 A5:2 F5:2 A5:2 | G5:4 r:2 G4:1 B4:1"
    chorus = "C6:2 A5:1 C6:1 F6:3 E6:1 | D6:2 B5:2 G5:4 | E6:2 D6:1 B5:1 G5:2 B5:2 | C6:3 B5:1 A5:4 | " \
             "F5:1 A5:1 D6:2 C6:1 A5:1 F5:2 | G5:2 B5:2 D6:2 F6:2 | E6:2 C6:2 A5:2 C6:2 | D6:2 F6:2 E6:2 r:2"
    t.tune(lead, "verse", verse, velocity=96, legato=0.7)
    t.tune(lead, "chorus", chorus, velocity=102, legato=0.7)
    t.tune(double, "chorus", chorus, octave_shift=-1, velocity=62, legato=0.6)
    t.tune(lead, "bridge", "A5:2 r:2 E5:2 r:2 | F5:2 r:2 A5:2 C6:2 | A5:2 r:2 E6:2 r:2 | F6:4 C6:4 | "
                           "D6:2 r:2 A5:2 F5:2 | G5:2 r:2 B5:2 E6:2 | C6:2 A5:2 F5:2 A5:2 | G5:4 r:4", velocity=94, legato=0.7)
    t.tune(lead, "chorus2", chorus.replace("D6:2 F6:2 E6:2 r:2", "D6:2 F6:2 G6:2 r:2"), velocity=106, legato=0.7)
    t.tune(double, "chorus2", chorus, octave_shift=-1, velocity=68, legato=0.6)
    t.tune(lead, "outro", "E6:2 C6:2 G5:4 | A5:2 C6:2 F6:4 | C6:8 | r:8", velocity=100)
    outro_chord(s, t, double, ("E5", "G5", "C6", "E6"), 72)
    for sp in t.spans("intro", "bridge"):
        s.comp(pad, [(0, 4)], low=pitch("E3"), high=pitch("C5"), velocity=50, length=0.98, size=3, span=sp)
    for sp in t.spans("verse", "chorus", "chorus2"):
        s.comp(ep, [(0.5, 0.25), (1.5, 0.25), (2.5, 0.25), (3.5, 0.25)], low=pitch("E3"), high=pitch("C5"), velocity=54,
               size=3, span=sp)
    s.arpeggio(pizz, pitch("C4"), pitch("G5"), step=0.5, velocity=48, span=t.span["intro"])
    for sp in t.spans("verse", "chorus", "bridge", "chorus2"):
        s.bass(bass, "bounce", low=pitch("E1"), velocity=96, span=sp)
    bass.note(t.bar("outro", 2), BAR * 1.5, pitch("C2"), 98)
    s.drums(kit, {HAT: "x.x.x.x.x.x.x.x.", TAMB: "................" * 3 + "....x.x.x.x.x.x."}, span=t.span["intro"])
    s.drums(kit, {KICK: "x.....x.x.......", CLAP: "....x.......x...", HAT: "x.x.x.x.x.x.x.x."}, span=t.span["verse"])
    groove = {KICK: "x.....x.x.....x.", CLAP: "....x.......x...", HAT: "xxxxxxxxxxxxxxxx", TAMB: "..o...o...o...o.",
              CRASH: "x..............." + "................" * 7}
    s.drums(kit, groove, span=t.span["chorus"])
    s.drums(kit, groove, span=t.span["chorus2"])
    s.drums(kit, {KICK: "x...x...x...x...", CLAP: "....x.......x...", OPEN_HAT: "..x...x...x...x.",
                  BONGO_HI: "......x.x.....x.", BONGO_LO: "x.........x.....",
                  CRASH: "x..............." + "................" * 7}, span=t.span["bridge"])
    s.drums(kit, {HAT: "x.x.x.x.x.x.x.x.", CRASH: "................" * 2 + "x..............." + "................"},
            span=t.span["outro"])
    return s


def space_ice():
    """The ice planet, 'Crystal Glide': skating, long gliding notes. Celesta and glockenspiel over harp arpeggios and a
    pad, a walking bass, light drums with a triangle."""
    t = Trip("space_ice", "Crystal Glide", 108, {
        "intro": "Em7 | Cmaj7 | Am7 | D7sus4 D7",
        "verse": "Gmaj7 | Em7 | Cmaj7 | D7 | Gmaj7 | Bm7 | Cmaj7 | D7",
        "chorus": "Cmaj7 | D | Bm7 | Em7 | Am7 | D7 | Gmaj7 Em7 | Am7 D7",
        "bridge": "Em7 | Cmaj7 | Em7 | Cmaj7 | Am7 | Bm7 | Cmaj7 | D7sus4 D7",
        "chorus2": "Cmaj7 | D | Bm7 | Em7 | Am7 | D7 | Gmaj7 Em7 | Am7 D7",
        "outro": "Gmaj7 | Cmaj7 | Gmaj7 | Gmaj7",
    }, {"intro": "launch", "chorus": "ringed", "bridge": "nebula", "chorus2": "arrival", "outro": "landing"}, seed=203)
    s = t.song
    lead, double, pad, pizz, bass, kit = tracks(s, CELESTA, double_program=GLOCK, bass_program=UPRIGHT)
    harp = s.track("Harp", 5, HARP, 66, 76, 70)
    t.tune(lead, "intro", "C6:4 A5:4 | G5:4 r:2 D5:2", bar=2)
    verse = "D5:2 G5:2 B5:4 | E6:4 D6:2 B5:2 | C6:4 G5:4 | A5:6 F#5:2 | " \
            "D5:2 G5:2 B5:2 D6:2 | F#6:4 D6:4 | E6:2 D6:2 C6:2 B5:2 | A5:6 r:2"
    chorus = "E6:4 G6:4 | F#6:4 D6:2 A5:2 | B5:4 D6:2 F#6:2 | E6:6 r:2 | " \
             "C6:2 E6:2 A6:4 | F#6:2 E6:2 D6:2 C6:2 | B5:4 E6:4 | C6:4 D6:2 r:2"
    t.tune(lead, "verse", verse, velocity=94, legato=0.95)
    t.tune(lead, "chorus", chorus, velocity=100, legato=0.92)
    t.tune(double, "chorus", chorus, velocity=52, legato=0.4)
    t.tune(lead, "bridge", "E5:2 r:2 G5:2 B5:2 | C6:4 B5:2 G5:2 | E5:2 r:2 B5:2 E6:2 | E6:4 B5:2 G5:2 | "
                           "A5:2 r:2 C6:2 E6:2 | D6:4 B5:2 F#5:2 | E5:2 G5:2 B5:2 C6:2 | D6:4 r:4", velocity=92)
    t.tune(lead, "chorus2", chorus.replace("C6:4 D6:2 r:2", "C6:2 D6:2 E6:2 F#6:2"), velocity=104, legato=0.92)
    t.tune(double, "chorus2", chorus, velocity=58, legato=0.4)
    t.tune(lead, "outro", "B5:2 G5:2 D5:4 | E6:2 C6:2 G5:4 | G5:8 | r:8", velocity=96)
    outro_chord(s, t, harp, ("G4", "B4", "D5", "F#5", "B5"), 74)
    for sp in t.spans("intro", "verse", "chorus", "bridge", "chorus2"):
        s.comp(pad, [(0, 4)], low=pitch("E3"), high=pitch("C5"), velocity=50, length=0.98, size=4, span=sp)
    for sp in t.spans("intro", "verse", "chorus", "chorus2"):
        s.arpeggio(harp, pitch("G4"), pitch("D6"), step=0.5, velocity=46, pattern=(0, 1, 2, 3, 4, 3, 2, 1), span=sp)
    for sp in t.spans("verse", "chorus", "chorus2"):
        s.bass(bass, "walk", low=pitch("E1"), velocity=90, span=sp)
    s.bass(bass, "pump", low=pitch("E1"), velocity=92, span=t.span["bridge"])
    bass.note(t.bar("outro", 2), BAR * 1.5, pitch("G1"), 96)
    s.drums(kit, {TRIANGLE: "x.......x.......", SHAKER: "o.o.o.o.o.o.o.o."}, span=t.span["intro"])
    s.drums(kit, {KICK: "x.......x.......", RIM: "....x.......x...", RIDE: "x.x.x.x.x.x.x.x."}, span=t.span["verse"])
    groove = {KICK: "x.....x.x.......", SNARE: "....x.......x...", RIDE: "x.x.x.x.x.x.x.x.", TRIANGLE: "x.......x.......",
              CRASH: "x..............." + "................" * 7}
    s.drums(kit, groove, span=t.span["chorus"])
    s.drums(kit, groove, span=t.span["chorus2"])
    s.drums(kit, {KICK: "x...x...x...x...", CLAP: "....x.......x...", SHAKER: "xoxoxoxoxoxoxoxo",
                  CONGA_MUTE: "..x...x...x...x.", CRASH: "x..............." + "................" * 7}, span=t.span["bridge"])
    s.drums(kit, {TRIANGLE: "x.......x.......", CRASH: "................" * 2 + "x..............." + "................"},
            span=t.span["outro"])
    return s


def space_jungle():
    """The jungle planet, 'Jungle Jetpack': a marimba calling and answering over bongos and congas, a whistle in the
    chorus, a bouncing bass; syncopated, the bridge a drum party."""
    t = Trip("space_jungle", "Jungle Jetpack", 116, {
        "intro": "D | Bm7 | G | A7",
        "verse": "D | F#m7 | G | A7 | D | Bm7 | Em7 | A7",
        "chorus": "G | A | F#m7 | Bm7 | Em7 | A7 | D Bm7 | Em7 A7",
        "bridge": "Bm7 | G | Bm7 | G | Em7 | F#m7 | G | A7sus4 A7",
        "chorus2": "G | A | F#m7 | Bm7 | Em7 | A7 | D Bm7 | Em7 A7",
        "outro": "D | G | D | D",
    }, {"intro": "launch", "chorus": "skim", "bridge": "nebula", "chorus2": "arrival", "outro": "landing"}, seed=204)
    s = t.song
    lead, double, pad, pizz, bass, kit = tracks(s, MARIMBA, double_program=WHISTLE, double_volume=64)
    t.tune(lead, "intro", "B5:2 D6:2 G5:4 | A5:2 C#6:2 E6:2 r:2", bar=2)
    verse = "D6:1 r:1 A5:1 r:1 F#5:2 A5:2 | C#6:1 r:1 A5:1 r:1 E5:4 | G5:1 r:1 B5:1 r:1 D6:2 B5:2 | C#6:2 E6:2 A5:4 | " \
            "D6:1 r:1 F#6:1 r:1 A6:2 F#6:2 | D6:4 B5:4 | E6:1 D6:1 B5:2 G5:2 B5:2 | A5:4 r:2 C#6:2"
    chorus = "D6:3 B5:3 G6:2 | E6:3 C#6:3 A5:2 | F#6:3 E6:3 C#6:2 | D6:4 B5:4 | " \
             "G5:1 B5:1 E6:2 D6:1 B5:1 G5:2 | A5:2 C#6:2 E6:2 G6:2 | F#6:2 D6:2 B5:2 D6:2 | E6:2 G6:2 E6:2 r:2"
    t.tune(lead, "verse", verse, velocity=98, legato=0.8)
    t.tune(lead, "chorus", chorus, velocity=104, legato=0.8)
    t.tune(double, "chorus", chorus, octave_shift=-1, velocity=64, legato=0.85)
    t.tune(lead, "bridge", "B5:2 r:2 F#5:2 r:2 | G5:2 r:2 B5:2 D6:2 | B5:2 r:2 F#6:2 r:2 | G6:4 D6:4 | "
                           "E6:2 r:2 B5:2 G5:2 | A5:2 r:2 C#6:2 F#6:2 | D6:2 B5:2 G5:2 B5:2 | A5:4 r:4", velocity=96)
    t.tune(lead, "chorus2", chorus.replace("E6:2 G6:2 E6:2 r:2", "E6:2 G6:2 A6:2 r:2"), velocity=108, legato=0.8)
    t.tune(double, "chorus2", chorus, octave_shift=-1, velocity=70, legato=0.85)
    t.tune(lead, "outro", "F#6:2 D6:2 A5:4 | B5:2 D6:2 G6:4 | D6:8 | r:8", velocity=102)
    outro_chord(s, t, pizz, ("D4", "A4", "D5", "F#5"), 90)
    for sp in t.spans("intro", "verse", "chorus", "bridge", "chorus2"):
        s.comp(pad, [(0, 4)], low=pitch("E3"), high=pitch("C5"), velocity=46, length=0.98, size=3, span=sp)
    for sp in t.spans("verse", "chorus", "chorus2"):
        s.comp(pizz, [(0, 0.25), (1.5, 0.25), (2.5, 0.25)], low=pitch("A3"), high=pitch("F#5"), velocity=60, size=3, span=sp)
    for sp in t.spans("verse", "chorus", "bridge", "chorus2"):
        s.bass(bass, "bounce", low=pitch("E1"), velocity=98, span=sp)
    bass.note(t.bar("outro", 2), BAR * 1.5, pitch("D2"), 100)
    s.drums(kit, {BONGO_HI: "..x...x...x.x...", BONGO_LO: "x.......x.......", SHAKER: "o.o.o.o.o.o.o.o."}, span=t.span["intro"])
    s.drums(kit, {KICK: "x.....x.x.......", CLAP: "....x.......x...", SHAKER: "o.o.o.o.o.o.o.o.",
                  BONGO_HI: "..x.......x.x..."}, span=t.span["verse"])
    groove = {KICK: "x.....x.x.....x.", CLAP: "....x.......x...", HAT: "x.x.x.x.x.x.x.x.", CONGA_HI: "..x...x...x...x.",
              CONGA_LO: "x.......x.......", CRASH: "x..............." + "................" * 7}
    s.drums(kit, groove, span=t.span["chorus"])
    s.drums(kit, {**groove, TAMB: "....x.......x..."}, span=t.span["chorus2"])
    s.drums(kit, {KICK: "x...x...x...x...", CLAP: "....x.......x...", BONGO_HI: "x.x.x.xxx.x.x.xx", BONGO_LO: "..x...x...x...x.",
                  CONGA_MUTE: "x...x...x...x...", CRASH: "x..............." + "................" * 7}, span=t.span["bridge"])
    s.drums(kit, {SHAKER: "o.o.o.o.o.o.o.o.", BONGO_HI: "..x...x.........",
                  CRASH: "................" * 2 + "x..............." + "................"}, span=t.span["outro"])
    return s


def space_star():
    """The star planet, 'Starlight Parade': the galaxy's finale, a march of a fanfare. Flute and glockenspiel over
    ocarina chords, pizzicato and a pad, a bouncing bass, a snare roll and crashes."""
    t = Trip("space_star", "Starlight Parade", 120, {
        "intro": "A | F#m7 | D | E7",
        "verse": "A | C#m7 | D | E7 | A | F#m7 | Bm7 | E7",
        "chorus": "D | E | C#m7 | F#m7 | Bm7 | E7 | A F#m7 | Bm7 E7",
        "bridge": "F#m7 | D | F#m7 | D | Bm7 | C#m7 | D | E7sus4 E7",
        "chorus2": "D | E | C#m7 | F#m7 | Bm7 | E7 | A F#m7 | Bm7 E7",
        "outro": "A | D | A | A",
    }, {"intro": "launch", "chorus": "asteroids", "bridge": "nebula", "chorus2": "arrival", "outro": "landing"}, seed=205)
    s = t.song
    lead, double, pad, pizz, bass, kit = tracks(s, FLUTE, double_program=GLOCK, double_volume=58)
    oca = s.track("Ocarina", 5, OCARINA, 58, 40, 55)
    t.tune(lead, "intro", "F#5:4 A5:4 | B5:4 r:2 E5:2", bar=2)
    verse = "E5:2 A5:2 C#6:2 E6:2 | G#5:4 E5:4 | F#5:2 A5:2 D6:2 F#6:2 | E6:6 r:2 | " \
            "A5:2 C#6:2 E6:2 A6:2 | F#6:4 C#6:4 | D6:2 C#6:2 B5:2 A5:2 | G#5:4 B5:2 E5:2"
    chorus = "A5:2 D6:2 F#6:3 E6:1 | D6:2 B5:2 G#5:4 | C#6:2 E6:2 G#6:3 F#6:1 | E6:2 C#6:2 A5:4 | " \
             "B5:2 D6:2 F#6:2 A6:2 | G#6:3 F#6:1 E6:2 D6:2 | C#6:2 E6:2 F#6:2 C#6:2 | B5:2 D6:2 E6:2 r:2"
    t.tune(lead, "verse", verse, velocity=96, legato=0.88)
    t.tune(lead, "chorus", chorus, velocity=102, legato=0.85)
    t.tune(double, "chorus", chorus, velocity=56, legato=0.45)
    t.tune(lead, "bridge", "F#5:2 r:2 A5:2 C#6:2 | D6:4 C#6:2 A5:2 | F#5:2 r:2 C#6:2 F#6:2 | F#6:4 D6:2 A5:2 | "
                           "B5:2 r:2 D6:2 F#6:2 | E6:4 C#6:2 G#5:2 | F#5:2 A5:2 D6:2 F#6:2 | E6:4 r:4", velocity=96)
    t.tune(lead, "chorus2", chorus.replace("B5:2 D6:2 E6:2 r:2", "B5:2 D6:2 E6:2 G#6:2"), velocity=106, legato=0.85)
    t.tune(double, "chorus2", chorus, velocity=64, legato=0.45)
    t.tune(lead, "outro", "C#6:2 A5:2 E5:4 | F#5:2 A5:2 D6:4 | A5:8 | r:8", velocity=104)
    outro_chord(s, t, oca, ("A4", "C#5", "E5", "A5"), 80)
    for sp in t.spans("verse", "chorus", "chorus2"):
        s.comp(oca, [(0, 0.9), (2, 0.9)], low=pitch("E4"), high=pitch("C#6"), velocity=50, size=3, span=sp)
    for sp in t.spans("intro", "bridge"):
        s.comp(pad, [(0, 4)], low=pitch("E3"), high=pitch("C5"), velocity=52, length=0.98, size=4, span=sp)
    for sp in t.spans("intro", "verse", "chorus2"):
        s.arpeggio(pizz, pitch("A4"), pitch("E6"), step=0.5, velocity=48, span=sp)
    for sp in t.spans("verse", "chorus", "bridge", "chorus2"):
        s.bass(bass, "bounce", low=pitch("E1"), velocity=96, span=sp)
    bass.note(t.bar("outro", 2), BAR * 1.5, pitch("A1"), 100)
    s.drums(kit, {SNARE: "o.o.o.o.o.o.o.o." * 3 + "x.x.x.x.xxxxXXXX", TRIANGLE: "x..............."}, span=t.span["intro"])
    s.drums(kit, {KICK: "x.......x.......", SNARE: "....x.......x...", HAT: "x.x.x.x.x.x.x.x."}, span=t.span["verse"])
    groove = {KICK: "x...x...x...x...", SNARE: "....x.......x.x.", HAT: "x.x.x.x.x.x.x.x.",
              CRASH: "x..............." + "................" * 3}
    s.drums(kit, groove, span=t.span["chorus"])
    s.drums(kit, {**groove, TAMB: "..o...o...o...o."}, span=t.span["chorus2"])
    s.drums(kit, {KICK: "x...x...x...x...", SNARE: "o.o.x.o.o.o.x.o.", OPEN_HAT: "..x...x...x...x.",
                  CRASH: "x..............." + "................" * 7}, span=t.span["bridge"])
    s.drums(kit, {SNARE: "x.x.x.x.x.x.xxxx" + "xxxxxxxxXXXXXXXX" + "................" * 2,
                  CRASH: "................" * 2 + "X..............." + "................"}, span=t.span["outro"])
    return s


# the trip's songs in the galaxy's order (SpaceGalaxy.Destinations: moon, candy, ice, jungle, star)
SONGS = {f.__name__: f for f in (space_moon, space_candy, space_ice, space_jungle, space_star)}
