"""Zana!'s songs (2026-11-15). Original tunes, Wii-style like the rest of Tundu's music (see music/README.md): harp,
celesta, flute and glockenspiel for the fairies' magic, short seamless loops.

    fairy        "Fairy Glade"      the realm's map and menu: a gentle, dreamy stroll (F major)
    fairy_fly    "Wings of Light"   flying (the garden, the forest, the clouds): bright and bouncy (A major)
    fairy_fly2   "Sparkle Skies"    flying (the lake, the snow, the moon palace): a breezy bossa (Bb major)
    fairy_boss   "Shadow Bloom"     a guardian: quick and exciting, a little mysterious, still friendly (A minor)

    python tools/music/compose.py --force fairy_fly     # rewrite one from this file (your edits to the .mid are lost)
    python tools/music/render.py fairy fairy_fly fairy_fly2 fairy_boss
"""

import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from compose import (CELESTA, CLAP, CONGA_HI, CONGA_LO, CONGA_MUTE, CRASH, EPIANO, FINGER_BASS, FLUTE, GLOCK, HARP,  # noqa: E402
                     HAT, KICK, M, NYLON, PAD, PIZZ, RIM, SHAKER, SNARE, TAMB, TRIANGLE, VIBES, WHISTLE, XYLOPHONE, Song,
                     pitch)


def fairy():
    """'Fairy Glade': the realm's menu. Celesta over harp arpeggios and a warm pad, the flute joining in the second half,
    a soft bossa bass, a shaker and a triangle."""
    s = Song("fairy", "Fairy Glade", 96,
             "Fmaj7 | Dm7 | Bbmaj7 | C7sus4 C7 | Fmaj7 | Am7 | Bbmaj7 | Gm7 C7 | Dm7 | Bbmaj7 | Gm7 | C7 | Fmaj7 Am7 |"
             "Bbmaj7 C7 | Fmaj7 | Gm7 C7", seed=121)
    cel = s.track("Celesta", 0, CELESTA, 104, 60, 65)
    flute = s.track("Flute", 1, FLUTE, 82, 74, 60)
    harp = s.track("Harp", 2, HARP, 74, 44, 60)
    pad = s.track("Pad", 3, PAD, 54, 64, 75)
    bass = s.track("Bass", 4, FINGER_BASS, 84, 64, 25)
    kit = s.track("Drums", 9, 0, 70, 64, 35)
    a = M("A5:3 C6:1 A5:2 G5:2 | F5:2 A5:2 D6:3 C6:1 | D6:2 C6:1 Bb5:1 A5:2 F5:2 | G5:4 E5:2 C5:2 | "
          "A5:3 C6:1 F6:2 E6:2 | E6:2 C6:2 A5:3 G5:1 | F5:2 Bb5:2 D6:2 C6:1 Bb5:1 | A5:4 r:2 C5:1 E5:1")
    b = M("F5:2 A5:1 C6:1 F6:2 E6:2 | D6:3 C6:1 Bb5:2 A5:2 | Bb5:2 D6:1 F6:1 E6:2 D6:2 | C6:4 Bb5:2 G5:2 | "
          "A5:2 C6:2 E6:2 C6:2 | D6:2 F6:2 E6:2 G5:2 | F5:3 A5:1 C6:2 A5:2 | G5:4 r:2 C5:1 E5:1")
    end = s.melody(cel, a, velocity=94, legato=0.9)
    s.melody(cel, b, velocity=98, legato=0.9, start=end)
    s.melody(flute, b, octave_shift=-1, velocity=72, legato=0.95, start=end)
    s.arpeggio(harp, pitch("F3"), pitch("C5"), step=0.5, velocity=60, pattern=(0, 1, 2, 3, 4, 3, 2, 1))
    s.comp(pad, [(0, 3.9)], low=pitch("C3"), high=pitch("G4"), velocity=48, length=1.0, size=4)
    s.bass(bass, "bossa", low=pitch("E1"), velocity=80)
    s.drums(kit, {
        KICK:     "x.......x.......",
        SHAKER:   "o.o.o.o.o.o.o.o.",
        TRIANGLE: "x..............." + "................" * 3,
    })
    return s


def fairy_fly():
    """'Wings of Light': flying over the garden, the forest and the clouds. Flute over pizzicato arpeggios and harp, the
    glockenspiel sparkling an octave up, the xylophone answering in the second half, a bouncing bass, claps and a
    shaker."""
    s = Song("fairy_fly", "Wings of Light", 132,
             "A | F#m7 | Dmaj7 | E7 | A | C#m7 | Dmaj7 | Bm7 E7 | D | E | C#m7 F#m7 | Bm7 E7 | A | F#7 | Bm7 E7 | A E7",
             seed=123)
    lead = s.track("Flute", 0, FLUTE, 102, 58, 50)
    glock = s.track("Glockenspiel", 1, GLOCK, 56, 80, 65)
    xylo = s.track("Xylophone", 2, XYLOPHONE, 70, 76, 45)
    pizz = s.track("Pizzicato", 3, PIZZ, 66, 42, 40)
    harp = s.track("Harp", 4, HARP, 58, 86, 55)
    bass = s.track("Bass", 5, FINGER_BASS, 96, 64, 15)
    kit = s.track("Drums", 9, 0, 86, 64, 25)
    a = M("E5:1 A5:1 C#6:2 B5:1 A5:1 E6:2 | C#6:2 A5:1 F#5:1 A5:2 C#6:2 | D6:1 C#6:1 A5:2 F#5:2 A5:2 | B5:3 G#5:1 E5:2 r:2 | "
          "E5:1 A5:1 C#6:2 E6:2 F#6:2 | E6:2 C#6:1 B5:1 G#5:2 E5:2 | F#5:1 A5:1 D6:2 C#6:1 A5:1 F#5:2 | B5:2 D6:2 G#5:2 B5:2")
    b = M("A5:2 D6:1 F#6:1 E6:2 D6:2 | E6:2 B5:1 G#5:1 B5:2 E6:2 | E6:2 C#6:2 C#6:2 A5:2 | D6:2 B5:2 G#5:2 E5:2 | "
          "A5:1 C#6:1 E6:2 A6:2 E6:2 | F#6:2 E6:1 C#6:1 A#5:2 C#6:2 | D6:2 F#6:1 D6:1 B5:2 G#5:2 | A5:4 r:2 E5:1 G#5:1")
    end = s.melody(lead, a, velocity=100, legato=0.8)
    s.melody(lead, b, velocity=104, legato=0.8, start=end)
    s.melody(glock, a, octave_shift=1, velocity=50, legato=0.45)
    s.melody(xylo, b, octave_shift=-1, velocity=64, legato=0.6, start=end)
    s.arpeggio(pizz, pitch("E4"), pitch("C#6"), step=0.5, velocity=56, pattern=(0, 1, 2, 3, 2, 1, 2, 3))
    s.comp(harp, [(0, 1.5), (1.5, 1.0), (2.5, 1.5)], low=pitch("A3"), high=pitch("E5"), velocity=52, size=3)
    s.bass(bass, "bounce", low=pitch("E1"), velocity=92)
    s.drums(kit, {
        KICK:     "x.....x.x.......",
        CLAP:     "....x.......x...",
        HAT:      "..x...x...x...x.",
        SHAKER:   "o.o.o.o.o.o.o.o.",
        TAMB:     "................" * 3 + "..o...o...o.o.o.",
        CRASH:    "x..............." + "................" * 15,
    })
    return s


def fairy_fly2():
    """'Sparkle Skies': flying over the lake, the snow and the moon palace. Celesta and vibes over nylon guitar and a pad,
    a bossa bass, rim clicks, congas and a shaker."""
    s = Song("fairy_fly2", "Sparkle Skies", 124,
             "Bbmaj7 | Gm7 | Ebmaj7 | F7sus4 F7 | Bbmaj7 | Dm7 | Ebmaj7 | Cm7 F7 | Gm7 | Ebmaj7 | Cm7 | F7 | Bbmaj7 Gm7 |"
             "Cm7 F7 | Ebmaj7 | F7sus4 F7", seed=125)
    cel = s.track("Celesta", 0, CELESTA, 104, 60, 60)
    vib = s.track("Vibraphone", 1, VIBES, 80, 82, 60)
    guitar = s.track("Nylon guitar", 2, NYLON, 70, 44, 40)
    pad = s.track("Pad", 3, PAD, 50, 64, 70)
    bass = s.track("Bass", 4, FINGER_BASS, 94, 64, 20)
    kit = s.track("Drums", 9, 0, 82, 64, 30)
    a = M("F5:2 Bb5:1 D6:1 F6:3 D6:1 | D6:2 Bb5:2 G5:2 A5:2 | Bb5:2 G5:1 Eb5:1 G5:2 Bb5:2 | C6:3 Bb5:1 A5:2 F5:2 | "
          "F5:2 Bb5:1 D6:1 F6:2 A6:2 | F6:2 D6:1 C6:1 A5:2 F5:2 | G5:1 Bb5:1 Eb6:2 D6:1 Bb5:1 G5:2 | G5:2 Eb5:2 A5:2 C6:2")
    b = M("Bb5:2 D6:1 F6:1 G6:2 F6:2 | Eb6:2 D6:1 Bb5:1 G5:2 Bb5:2 | C6:2 Eb6:1 G6:1 F6:2 Eb6:2 | D6:2 C6:2 A5:2 F5:2 | "
          "D6:2 F6:2 D6:2 Bb5:2 | C6:2 Eb6:2 A5:2 C6:2 | Bb5:3 G5:1 Eb5:2 G5:2 | F5:4 r:2 F5:1 A5:1")
    end = s.melody(cel, a, velocity=96, legato=0.85)
    s.melody(cel, b, velocity=100, legato=0.85, start=end)
    s.melody(vib, b, octave_shift=-1, velocity=62, legato=0.9, start=end)
    s.comp(guitar, [(0, 0.9), (1.5, 0.4), (2.5, 0.9), (3.5, 0.4)], low=pitch("D3"), high=pitch("D5"), velocity=60, size=4)
    s.comp(pad, [(0, 3.9)], low=pitch("Bb2"), high=pitch("F4"), velocity=44, length=1.0, size=4)
    s.bass(bass, "bossa", low=pitch("E1"), velocity=90)
    s.drums(kit, {
        KICK:       "x.......x.......",
        RIM:        "...x..x....x..x.",
        SHAKER:     "o.o.o.o.o.o.o.o.",
        CONGA_MUTE: "....o.......o...",
        CONGA_HI:   "......o.......o.",
        CONGA_LO:   "...............o",
        TRIANGLE:   "x..............." + "................" * 7,
    })
    return s


def fairy_boss():
    """'Shadow Bloom': a guardian of the kingdom. Xylophone and whistle over pizzicato stabs and electric piano offbeats, a
    pumping bass, a snare backbeat: exciting, a little mysterious, still a children's tune."""
    s = Song("fairy_boss", "Shadow Bloom", 140,
             "Am | F | G | E7 | Am | F | Dm7 | E7 | F | G | Em7 Am | Dm7 E7 | Am | F | G E7 | Am E7", seed=127)
    lead = s.track("Xylophone", 0, XYLOPHONE, 104, 56, 40)
    whistle = s.track("Whistle", 1, WHISTLE, 70, 78, 50)
    pizz = s.track("Pizzicato", 2, PIZZ, 76, 42, 35)
    ep = s.track("Electric piano", 3, EPIANO, 56, 84, 35)
    bass = s.track("Bass", 4, FINGER_BASS, 100, 64, 15)
    kit = s.track("Drums", 9, 0, 92, 64, 20)
    a = M("A5:1 C6:1 E6:2 D6:1 C6:1 B5:2 | C6:2 A5:1 F5:1 A5:2 C6:2 | D6:1 B5:1 G5:2 B5:1 D6:1 G6:2 | G#5:3 B5:1 E6:2 D6:2 | "
          "C6:1 B5:1 A5:2 E6:2 A6:2 | A6:2 G6:1 F6:1 C6:2 A5:2 | D6:2 F6:1 E6:1 D6:2 C6:2 | B5:4 r:2 E5:1 G#5:1")
    b = M("A5:2 C6:1 F6:1 E6:2 C6:2 | B5:2 D6:1 G6:1 F6:2 D6:2 | E6:2 G6:2 E6:2 C6:2 | F6:2 D6:2 G#5:2 B5:2 | "
          "A5:1 E6:1 A6:2 G6:1 E6:1 C6:2 | F6:2 E6:1 C6:1 A5:2 C6:2 | D6:2 B5:2 E6:2 G#5:2 | A5:4 r:2 E5:1 G#5:1")
    end = s.melody(lead, a, velocity=102, legato=0.7)
    s.melody(lead, b, velocity=106, legato=0.7, start=end)
    s.melody(whistle, b, octave_shift=-1, velocity=72, legato=0.85, start=end)
    s.comp(pizz, [(0, 0.3), (0.75, 0.3), (2, 0.3), (2.75, 0.3)], low=pitch("A3"), high=pitch("E5"), velocity=68, size=3)
    s.comp(ep, [(0.5, 0.25), (1.5, 0.25), (2.5, 0.25), (3.5, 0.25)], low=pitch("E3"), high=pitch("C5"), velocity=50, size=3)
    s.bass(bass, "pump", low=pitch("E1"), velocity=96)
    s.drums(kit, {
        KICK:     "x...x...x...x...",
        SNARE:    "....x.......x...",
        HAT:      "x.x.x.x.x.x.x.x.",
        TAMB:     "................" * 3 + "o.o.o.o.o.o.o.o.",
        CRASH:    "x..............." + "................" * 7,
    })
    return s


SONGS = {f.__name__: f for f in (fairy, fairy_fly, fairy_fly2, fairy_boss)}
