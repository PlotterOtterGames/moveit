"""Vrapo!'s songs (2026-11-18, the console polish). Original tunes, Wii-style like the rest of Tundu's music (see
music/README.md): marimba and xylophone, steel drums, a whistle, pizzicato, a bouncing bass, congas, bongos and wood
blocks for the jungle valley.

    runner        "Dino Dash"     the run: bright and driving, in sections so a long run doesn't wear it out (F major,
                                  140 BPM, 38 bars, ~65 s): a drum fill in, the marimba's tune, the tune again with the
                                  xylophone and steel drums, the whistle's song over vibes, a breather on log drums and a
                                  glockenspiel, the tune once more with everything
    runner_title  "Dino's Camp"   the title at Dino's camp: an easy bossa, nylon guitar and marimba, the flute answering
                                  (F major, 104 BPM, 16 bars)

    python tools/music/compose.py --force runner runner_title     # rewrite them (your edits to the .mid files are lost)
    python tools/music/render.py runner runner_title
"""

import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from compose import (BAR, BONGO_HI, BONGO_LO, CLAP, CONGA_HI, CONGA_LO, CONGA_MUTE, CRASH, FINGER_BASS, FLUTE, GLOCK,  # noqa: E402
                     HAT, KICK, M, MARIMBA, NYLON, OPEN_HAT, PIZZ, RIM, SHAKER, STEEL_DRUM, TAMB, TRIANGLE, UPRIGHT, VIBES,
                     WHISTLE, WOOD_HI, WOOD_LO, XYLOPHONE, Song, pitch)

A_CHORDS = "Fmaj7 | Gm7 C7 | Am7 Dm7 | Bbmaj7 C7 | Fmaj7 | Dm7 G7 | Gm7 | C7sus4 C7"
A_TUNE = ("C5:1 F5:1 A5:2 G5:1 A5:1 C6:2 | Bb5:1 A5:1 G5:2 E5:1 G5:1 C5:2 | A4:1 C5:1 E5:2 F5:1 E5:1 D5:2 | "
          "D5:1 F5:1 Bb5:2 G5:1 E5:1 C5:2 | C5:1 F5:1 A5:2 G5:1 A5:1 C6:1 D6:1 | F6:2 D6:1 C6:1 B5:2 G5:2 | "
          "Bb5:1 A5:1 G5:1 F5:1 D5:2 F5:2 | G5:3 F5:1 E5:2 r:2")
A_END = ("C5:1 F5:1 A5:2 G5:1 A5:1 C6:2 | Bb5:1 A5:1 G5:2 E5:1 G5:1 C5:2 | A4:1 C5:1 E5:2 F5:1 E5:1 D5:2 | "
         "D5:1 F5:1 Bb5:2 G5:1 E5:1 C5:2 | C5:1 F5:1 A5:2 G5:1 A5:1 C6:1 D6:1 | F6:2 D6:1 C6:1 B5:2 G5:2 | "
         "Bb5:1 A5:1 G5:1 E5:1 C5:2 E5:2 | F5:4 r:1 C5:1 E5:1 G5:1")
B_TUNE = ("D6:3 C6:1 Bb5:2 A5:2 | C6:3 A5:1 E5:2 G5:2 | Bb5:3 A5:1 G5:2 F5:2 | E5:2 G5:2 C6:2 Bb5:2 | "
          "A5:3 F5:1 D5:2 F5:2 | D6:3 C6:1 Bb5:2 D6:2 | C6:2 Bb5:2 A5:2 G5:2 | F5:2 G5:2 A5:2 C6:2")


def runner():
    """'Dino Dash': the run, in sections (intro 2, A 8, A' 8, B 8, breakdown 4, A'' 8 bars)."""
    s = Song("runner", "Dino Dash", 140,
             "F | C7sus4 C7 | " + A_CHORDS + " | " + A_CHORDS + " | "
             "Bbmaj7 | Am7 | Gm7 | C7 | Dm7 | Bbmaj7 | Gm7 | C7sus4 C7 | "
             "Fmaj7 | Bbmaj7 | Fmaj7 | C7sus4 | "
             "Fmaj7 | Gm7 C7 | Am7 Dm7 | Bbmaj7 C7 | Fmaj7 | Dm7 G7 | Gm7 C7 | F C7", seed=181)
    intro, a1, a2, b, c, a3, end = (0, 2 * BAR, 10 * BAR, 18 * BAR, 26 * BAR, 30 * BAR, 38 * BAR)
    s.markers = [(intro, "Intro"), (a1, "A"), (a2, "A again"), (b, "B: the whistle"), (c, "Breakdown"), (a3, "A last")]
    lead = s.track("Marimba", 0, MARIMBA, 112, 56, 40)
    xylo = s.track("Xylophone", 1, XYLOPHONE, 76, 84, 40)
    steel = s.track("Steel drum", 2, STEEL_DRUM, 82, 40, 50)
    whistle = s.track("Whistle", 3, WHISTLE, 92, 70, 55)
    vibes = s.track("Vibraphone", 4, VIBES, 62, 50, 60)
    pizz = s.track("Pizzicato", 5, PIZZ, 74, 36, 30)
    glock = s.track("Glockenspiel", 6, GLOCK, 66, 90, 60)
    bass = s.track("Bass", 7, FINGER_BASS, 104, 64, 15)
    kit = s.track("Drums", 9, 0, 98, 64, 22)

    # the tune: the marimba alone, then with the xylophone an octave up and the steel drum answering
    s.melody(lead, M(A_TUNE), velocity=102, legato=0.82, start=a1)
    s.melody(lead, M(A_TUNE), velocity=104, legato=0.82, start=a2)
    s.melody(xylo, M(A_TUNE), octave_shift=1, velocity=66, legato=0.55, start=a2)
    for bar, notes in ((1, "r:6 F5 A5"), (3, "r:6 C6 A5"), (5, "r:5 D6 C6 A5"), (7, "r:4 C6:2 Bb5:2")):
        s.melody(steel, M(notes), velocity=78, start=a1 + bar * BAR)
        s.melody(steel, M(notes), octave_shift=-1, velocity=74, start=a2 + bar * BAR)
    # the whistle's song over vibes; the marimba keeps a quiet pulse under it
    s.melody(whistle, M(B_TUNE), velocity=96, legato=0.95, start=b)
    s.comp(vibes, [(0, 3.5)], low=pitch("E3"), high=pitch("D5"), velocity=56, length=0.95, size=4, span=(b, c))
    s.comp(lead, [(0.5, 0.25), (1.5, 0.25), (2.5, 0.25), (3.5, 0.25)], low=pitch("A3"), high=pitch("F5"), velocity=52, size=3,
           span=(b, c))
    # the breather: glockenspiel arpeggios over log drums
    s.arpeggio(glock, pitch("C5"), pitch("C7"), step=0.5, velocity=62, pattern=(0, 1, 2, 3, 2, 1, 2, 3), span=(c, a3))
    s.comp(vibes, [(0, 4)], low=pitch("F3"), high=pitch("E5"), velocity=46, length=0.98, size=4, span=(c, a3))
    # the last time round: everything, the steel drum in harmony
    s.melody(lead, M(A_END), velocity=106, legato=0.82, start=a3)
    s.melody(xylo, M(A_END), octave_shift=1, velocity=70, legato=0.55, start=a3)
    s.melody(steel, M(A_END), octave_shift=-1, velocity=58, legato=0.7, start=a3)
    # pizzicato offbeats in the A sections, the bass bouncing all through (walking in the whistle's song)
    for span in ((a1, b), (a3, end)):
        s.comp(pizz, [(0.5, 0.25), (1.5, 0.25), (2.5, 0.25), (3.5, 0.25)], low=pitch("F3"), high=pitch("C5"), velocity=66, size=3,
               span=span)
    s.bass(bass, "bounce", low=pitch("E1"), span=(intro, b))
    s.bass(bass, "walk", low=pitch("E1"), velocity=88, span=(b, c))
    s.bass(bass, "pump", low=pitch("E1"), velocity=76, span=(c, a3))
    s.bass(bass, "bounce", low=pitch("E1"), span=(a3, end))
    # the "go!" whistle into the tune, and into the last time round
    s.melody(whistle, M("r:4 C6:1 D6:1 E6:1 F6:1"), velocity=84, start=a1 - BAR)
    s.melody(whistle, M("r:2 G5:1 A5:1 Bb5:1 C6:1 D6:1 E6:1"), velocity=84, start=a3 - BAR)

    # drums: a fill on log drums in, the groove, lighter for the whistle, log drums and a shaker in the breather
    s.drums(kit, {
        CONGA_HI: "x.x.x.x.x.x.x.x." + "x.xxx.xxx.xxxxxx",
        CONGA_LO: "....x.......x..." + "..x...x...x.x...",
        WOOD_HI:  "................" + "............x.x.",
        CRASH:    "................" + "................",
    }, span=(intro, a1))
    groove = {
        KICK:      "x.....x.x.....x.",
        CLAP:      "....x.......x...",
        SHAKER:    "oxoxoxoxoxoxoxox",
        CONGA_HI:  "...x..x....x..x." + "...x..x....x.x.x",
        CONGA_LO:  "..........x....." + "........x.......",
        WOOD_HI:   "................" * 7 + "............x.x.",
        CRASH:     "x..............." + "................" * 7,
        TAMB:      "................" * 4 + "..o...o...o...o." * 4,
    }
    s.drums(kit, groove, span=(a1, a2))
    groove2 = dict(groove)
    groove2[OPEN_HAT] = "..o...o...o...o."
    s.drums(kit, groove2, span=(a2, b))
    s.drums(kit, {
        KICK:      "x.......x.......",
        RIM:       "....x.......x...",
        SHAKER:    "oxoxoxoxoxoxoxox",
        BONGO_HI:  "..x...x...x..xx.",
        BONGO_LO:  "x.......x.......",
        TRIANGLE:  "x..............." + "................" * 3,
        CRASH:     "x..............." + "................" * 7,
    }, span=(b, c))
    s.drums(kit, {
        CONGA_LO:   "x.....x...x.....",
        CONGA_MUTE: "....x.......x...",
        CONGA_HI:   "........" + "..x.x.x.",
        SHAKER:     "o.o.o.o.o.o.o.o.",
        WOOD_LO:    "................" * 3 + "x.x.x.x.xxxxxxxx",
    }, span=(c, a3))
    s.drums(kit, groove2, span=(a3, end))
    return s


def runner_title():
    """'Dino's Camp': the title. An easy bossa: nylon guitar comping, the marimba's tune, the flute answering, an
    upright bass, a shaker and soft congas."""
    s = Song("runner_title", "Dino's Camp", 104,
             "Fmaj7 | Dm7 | Gm7 | C7 | Am7 | Dm7 | Gm7 | C7sus4 C7 | Bbmaj7 | Am7 | Gm7 | C7 | Fmaj7 | Dm7 | Gm7 C7 | Fmaj7", seed=183)
    lead = s.track("Marimba", 0, MARIMBA, 104, 56, 50)
    flute = s.track("Flute", 1, FLUTE, 80, 74, 60)
    guitar = s.track("Nylon guitar", 2, NYLON, 78, 40, 45)
    bass = s.track("Bass", 3, UPRIGHT, 92, 64, 25)
    kit = s.track("Drums", 9, 0, 76, 64, 35)
    a = M("A5:3 G5:1 F5:2 C5:2 | D5:2 F5:1 A5:1 C6:3 A5:1 | Bb5:3 A5:1 G5:2 D5:2 | E5:4 G5:2 r:2 | "
          "C6:3 B5:1 A5:2 E5:2 | F5:2 A5:1 D6:1 C6:2 A5:2 | G5:2 Bb5:2 D6:2 C6:1 Bb5:1 | A5:3 G5:1 E5:2 C5:2")
    b = M("D6:3 C6:1 Bb5:2 F5:2 | C6:3 A5:1 E5:2 A5:2 | Bb5:2 G5:2 D5:2 G5:2 | E5:2 G5:2 Bb5:2 C6:2 | "
          "A5:3 G5:1 F5:2 A5:2 | D6:2 C6:1 A5:1 F5:2 D5:2 | G5:2 A5:1 Bb5:1 C6:2 E5:2 | F5:6 r:2")
    end = s.melody(lead, a, velocity=96, legato=0.9)
    s.melody(lead, b, velocity=98, legato=0.9, start=end)
    for bar, notes in ((1, "r:6 C6:1 A5:1"), (3, "r:4 G5:1 Bb5:1 C6:2"), (9, "r:6 F6:1 E6:1"), (11, "r:4 E6:1 D6:1 C6:2")):
        s.melody(flute, M(notes), velocity=76, start=bar * BAR)
    s.comp(guitar, [(0, 1.5), (1.5, 0.5), (2.5, 1.0), (3.5, 0.5)], low=pitch("E3"), high=pitch("D5"), velocity=58, size=4)
    s.bass(bass, "bossa", low=pitch("E1"), velocity=84)
    s.drums(kit, {
        SHAKER:     "oxoxoxoxoxoxoxox",
        RIM:        "...x..x....x..x.",
        CONGA_MUTE: "........x.......",
        CONGA_HI:   "..............x.",
        TRIANGLE:   "x..............." + "................" * 7,
    })
    return s


SONGS = {f.__name__: f for f in (runner, runner_title)}
