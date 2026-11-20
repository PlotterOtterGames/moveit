"""Tundu's sound effects, synthesized in the same Wii-like style as the music (mallets, chimes, soft whooshes).

    python tools/music/sfx.py              # writes unity/MoveItGames/Assets/Resources/Sfx/<name>.ogg
    python tools/music/sfx.py paint_       # only the sounds whose name starts with "paint_"

Replace any file in Resources/Sfx with your own sound of the same name to change it (wav/ogg/mp3).
"""

import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import synth as S  # noqa: E402
from render import encode  # noqa: E402
from synth import SR  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(ROOT, "unity", "MoveItGames", "Assets", "Resources", "Sfx")


def mix(seconds, *parts):
    """parts: (start seconds, sound, gain)."""
    out = np.zeros(int(SR * seconds))
    for start, snd, gain in parts:
        k = int(start * SR)
        n = min(len(snd), len(out) - k)
        if n > 0:
            out[k:k + n] += snd[:n] * gain
    return out


def sweep(f0, f1, seconds, shape=1.0, harmonics=((1, 1.0),), vibrato=0.0):
    n = int(SR * seconds)
    t = np.arange(n) / SR
    k = (t / seconds) ** shape
    f = f0 * (f1 / f0) ** k * (1 + vibrato * np.sin(2 * np.pi * 18 * t))
    ph = 2 * np.pi * np.cumsum(f) / SR
    return sum(a * np.sin(h * ph) for h, a in harmonics)


def env(n_or_x, attack, release):
    x = n_or_x
    n = len(x)
    t = np.arange(n) / SR
    return x * np.clip(t / max(attack, 1e-4), 0, 1) * np.clip((n / SR - t) / max(release, 1e-4), 0, 1)


def note(fn, name_pitch, dur=0.3, vel=0.8):
    return fn(S.mtof(name_pitch), dur, vel)


def seamless(x, crossfade):
    """Makes a loop: the last `crossfade` seconds fade into the start, so it repeats without a click."""
    n = int(SR * crossfade)
    out = x[:-n].copy()
    ramp = np.linspace(0, 1, n)
    out[:n] = x[:n] * ramp + x[-n:] * (1 - ramp)
    return out


C5, D5, E5, F5, G5, A5, B5 = 72, 74, 76, 77, 79, 81, 83
C6 = 84


def make():
    sfx = {}
    sfx["ui_move"] = env(note(S.marimba, C6, vel=0.6)[:int(SR * 0.18)], 0.001, 0.08)
    sfx["ui_select"] = mix(0.6, (0, note(S.marimba, G5), 0.8), (0.07, note(S.marimba, C6), 1.0),
                           (0.07, note(S.glockenspiel, C6 + 12), 0.25))
    sfx["ui_back"] = mix(0.5, (0, note(S.marimba, C6, vel=0.6), 0.7), (0.07, note(S.marimba, G5, vel=0.6), 0.8))
    sfx["count"] = mix(0.45, (0, S.drum(76, 0.9), 0.6), (0, note(S.marimba, C5), 0.9))
    sfx["go"] = mix(1.3, *[(0.0, note(S.marimba, p), 0.6) for p in (C5, E5, G5, C6)],
                    (0.02, note(S.glockenspiel, C6 + 12), 0.4), (0.0, S.drum(81, 1.0), 0.5))
    sfx["ready"] = mix(0.9, (0, note(S.glockenspiel, C6), 0.6), (0.11, note(S.glockenspiel, G5 + 12), 0.7))
    # player
    boing = env(sweep(240, 820, 0.22, shape=0.7, harmonics=((1, 1), (2, 0.25)), vibrato=0.02), 0.005, 0.08)
    sfx["jump"] = mix(0.3, (0, boing, 0.7), (0, note(S.pizzicato, G5, vel=0.6), 0.3))
    thud = S._tone_drum(110, 55, 0.06, 0.2)
    sfx["land"] = mix(0.22, (0, thud, 0.8), (0, env(S.noise(int(SR * 0.15), 150, 1500, seed=3), 0.003, 0.12), 0.3))
    sfx["whoosh"] = env(np.concatenate([S.noise(int(SR * 0.09), 700, 2500, seed=4),
                                        S.noise(int(SR * 0.12), 1500, 6000, seed=5)]), 0.05, 0.12) * 0.6
    sfx["slide"] = mix(0.5, (0, env(S.noise(int(SR * 0.45), 300, 2600, seed=6), 0.04, 0.25), 0.55),
                       (0.02, env(sweep(900, 480, 0.3, harmonics=((1, 1),)), 0.02, 0.1), 0.12))
    for i, p in enumerate((C6, C6 + 2, C6 + 4, C6 + 5, C6 + 7, C6 + 9, C6 + 11, C6 + 12)):
        sfx[f"egg_{i}"] = mix(0.7, (0, note(S.glockenspiel, p), 0.7), (0, note(S.marimba, p - 12, vel=0.7), 0.35))
    tweet = env(sweep(2500, 3300, 0.07, harmonics=((1, 1),)), 0.004, 0.03)
    sfx["crash"] = mix(1.1, (0, S.drum(77, 1.0), 0.7), (0, S._tone_drum(150, 55, 0.12, 0.4), 0.9),
                       (0.02, note(S.marimba, 55, vel=1.0), 0.5),
                       (0.35, tweet, 0.25), (0.47, tweet, 0.22), (0.66, tweet, 0.2), (0.78, tweet, 0.17))
    # Vishu!
    pop = env(sweep(380, 1100, 0.07, shape=0.5, harmonics=((1, 1), (2, 0.2))), 0.002, 0.04)
    shimmer = env(S.noise(int(SR * 0.35), 3000, 11000, seed=60), 0.02, 0.3)
    sfx["dress_poof"] = mix(0.9, (0, pop, 0.6), (0, shimmer, 0.25),
                            *[(0.03 + 0.045 * i, note(S.glockenspiel, p), 0.4) for i, p in enumerate((C6, E5 + 12, G5 + 12, C6 + 12))],
                            (0.0, note(S.marimba, G5, vel=0.6), 0.3))
    sfx["dress_color"] = mix(0.6, (0, env(S.noise(int(SR * 0.2), 4000, 12000, seed=61), 0.01, 0.17), 0.25),
                             (0.0, note(S.celesta, E5 + 12), 0.5), (0.06, note(S.celesta, A5 + 12), 0.55))
    sfx["dress_remove"] = mix(0.5, (0, env(sweep(900, 360, 0.16, shape=0.8, harmonics=((1, 1), (2, 0.2))), 0.003, 0.08), 0.5),
                              (0.02, env(S.noise(int(SR * 0.18), 600, 4000, seed=62), 0.01, 0.15), 0.25))
    sfx["dress_magic"] = mix(1.2, (0, env(S.noise(int(SR * 0.9), 5000, 12000, seed=63), 0.05, 0.7), 0.2),
                             *[(0.05 * i, note(S.celesta, p), 0.35 - 0.02 * i) for i, p in
                               enumerate((C6 + 12, G5 + 12, E5 + 12, C6, G5 + 12, C6 + 12, E5 + 24))])
    roll = env(S.noise(int(SR * 0.45), 2500, 9000, seed=64) * (0.6 + 0.4 * np.sin(2 * np.pi * 22 * np.arange(int(SR * 0.45)) / SR)), 0.05, 0.05)
    sfx["dress_surprise"] = mix(1.3, (0, roll, 0.45),
                                *[(0.42 + 0.06 * i, note(S.marimba, p), 0.45) for i, p in enumerate((C5, E5, G5, C6))],
                                (0.66, note(S.glockenspiel, C6 + 12), 0.45), (0.66, env(S.noise(int(SR * 0.5), 5000, 12000, seed=65), 0.01, 0.45), 0.2))
    sfx["dress_show"] = mix(1.6, *[(0.0, note(S.marimba, p, vel=0.9), 0.4) for p in (F5 - 12, A5 - 12, C5, F5)],
                            *[(0.18 + 0.07 * i, note(S.glockenspiel, p), 0.4) for i, p in enumerate((F5 + 12, A5 + 12, C6 + 12))],
                            (0.0, S.drum(49, 0.9), 0.35))
    n = int(SR * 0.9)
    t = np.arange(n) / SR
    sfx["dress_curtain"] = S.noise(n, 300, 4000, seed=66) * np.sin(np.pi * t / 0.9) ** 1.5 * 0.7
    click = env(S.noise(int(SR * 0.02), 1500, 9000, seed=67), 0.001, 0.015)
    sfx["dress_pose"] = mix(1.2, (0, click, 0.8), (0.06, click, 0.6),
                            *[(0.1 + 0.07 * i, note(S.vibraphone, p), 0.45) for i, p in enumerate((C5 + 12, E5 + 12, G5 + 12))],
                            (0.31, note(S.glockenspiel, C6 + 12), 0.5))
    sfx["dress_star"] = mix(0.7, (0, note(S.glockenspiel, G5 + 12), 0.6), (0, note(S.celesta, G5 + 12), 0.35))
    sfx["dress_tick"] = mix(0.2, (0, S.drum(76, 0.8), 0.7))
    # Bowling
    n = int(SR * 1.6)
    t = np.arange(n) / SR
    sfx["roll"] = env(S.noise(n, 40, 380, seed=10) * (0.7 + 0.3 * np.sin(2 * np.pi * 7 * t)), 0.25, 0.3) * 0.8
    rng = np.random.default_rng(11)
    hits = []
    for k in range(10):
        f = rng.uniform(550, 1500)
        hn = int(SR * 0.12)
        ht = np.arange(hn) / SR
        hit = (np.sin(2 * np.pi * f * ht) + 0.4 * np.sin(2 * np.pi * f * 2.6 * ht)) * np.exp(-ht / 0.035)
        hit[:int(SR * 0.004)] += S.noise(int(SR * 0.004), 1000, 8000, seed=20 + k)
        hits.append((rng.uniform(0, 0.45) ** 1.5, hit, 0.9 * (1 - k / 14)))
    sfx["pins"] = mix(0.8, *hits)
    # Pikturo!
    bloop = env(sweep(420, 980, 0.09, shape=0.6, harmonics=((1, 1), (2, 0.2))), 0.003, 0.05)
    sfx["paint_pick"] = mix(0.4, (0, bloop, 0.6), (0.04, note(S.marimba, E5 + 12, vel=0.7), 0.5))
    sfx["paint_down"] = env(note(S.marimba, G5 + 12, dur=0.12, vel=0.5)[:int(SR * 0.1)], 0.001, 0.05)
    n = int(SR * 2.2)                         # brush on paper: soft filtered noise, slowly breathing, looped
    t = np.arange(n) / SR
    brush = S.noise(n, 500, 3800, seed=30) * (0.75 + 0.25 * np.sin(2 * np.pi * 1.4 * t))
    brush += 0.35 * S.noise(n, 2500, 9000, seed=31) * (0.6 + 0.4 * np.sin(2 * np.pi * 2.3 * t + 1))
    sfx["paint_brush"] = seamless(brush, 0.2) * 0.6
    boing = env(sweep(300, 640, 0.12, shape=0.5, harmonics=((1, 1), (3, 0.15))), 0.003, 0.07)
    sfx["paint_stamp"] = mix(0.3, (0, boing, 0.55), (0, note(S.pizzicato, C6, vel=0.7), 0.5))
    glug = env(sweep(760, 230, 0.32, shape=1.4, harmonics=((1, 1), (2, 0.3)), vibrato=0.06), 0.004, 0.12)
    sfx["paint_fill"] = mix(1.0, (0, glug, 0.6), (0.02, env(S.noise(int(SR * 0.3), 200, 2200, seed=32), 0.01, 0.2), 0.25),
                            *[(0.18 + 0.05 * i, note(S.glockenspiel, p), 0.35) for i, p in enumerate((C6, E5 + 12, G5 + 12))])
    rise = env(S.noise(int(SR * 0.28), 900, 5000, seed=33), 0.24, 0.03)
    sfx["paint_undo"] = mix(0.5, (0, rise, 0.5), (0.18, env(sweep(900, 520, 0.14, harmonics=((1, 1),)), 0.003, 0.08), 0.35))
    sfx["paint_clear"] = mix(1.0, (0, env(S.noise(int(SR * 0.5), 600, 7000, seed=34), 0.15, 0.3), 0.55),
                             *[(0.06 * i, note(S.marimba, p, vel=0.7), 0.4) for i, p in enumerate((C6 + 7, C6 + 4, C6, G5, E5, C5))])
    click = env(S.noise(int(SR * 0.025), 1500, 9000, seed=35), 0.001, 0.02)
    sfx["paint_photo"] = mix(0.9, (0, click, 0.9), (0.09, click, 0.7),
                             (0.15, note(S.glockenspiel, C6 + 12), 0.35), (0.22, note(S.glockenspiel, G5 + 24), 0.3))
    # Shpata!
    sfx["sword_swish"] = env(np.concatenate([S.noise(int(SR * 0.05), 1200, 4000, seed=40),
                                             S.noise(int(SR * 0.11), 2500, 9000, seed=41)]), 0.03, 0.09) * 0.55
    bonk = S._tone_drum(190, 95, 0.07, 0.25)
    card = env(S.noise(int(SR * 0.08), 250, 2200, seed=42), 0.001, 0.06)
    sfx["sword_hit"] = mix(0.45, (0, bonk, 0.9), (0, card, 0.5), (0.01, note(S.marimba, G5, vel=0.8), 0.35),
                           (0.06, note(S.glockenspiel, C6 + 7), 0.25))
    sfx["sword_clang"] = mix(0.7, (0, ring(1150, 0.35, 0.7), 0.7), (0, env(S.noise(int(SR * 0.02), 2000, 9000, seed=43), 0.001, 0.015), 0.4))
    sfx["sword_block"] = mix(1.1, (0, ring(880, 0.6, 1.0), 0.8), (0, ring(1320, 0.4, 0.8), 0.4),
                             (0, env(S.noise(int(SR * 0.03), 2000, 10000, seed=44), 0.001, 0.02), 0.5),
                             *[(0.12 + 0.06 * i, note(S.glockenspiel, p), 0.35) for i, p in enumerate((G5 + 12, C6 + 12, E5 + 24))])
    oof = env(sweep(520, 190, 0.32, shape=0.8, harmonics=((1, 1), (2, 0.3)), vibrato=0.03), 0.004, 0.12)
    sfx["sword_hurt"] = mix(0.9, (0, S._tone_drum(150, 60, 0.1, 0.4), 0.9), (0.02, oof, 0.5),
                            (0.35, tweet, 0.22), (0.47, tweet, 0.2), (0.62, tweet, 0.17))
    pop = env(sweep(300, 900, 0.06, shape=0.5, harmonics=((1, 1),)), 0.002, 0.04)
    sfx["sword_poof"] = mix(1.2, (0, pop, 0.6), (0, env(S.noise(int(SR * 0.25), 800, 7000, seed=45), 0.005, 0.2), 0.4),
                            *[(0.05 + 0.07 * i, note(S.marimba, p), 0.45) for i, p in enumerate((C5 + 12, E5 + 12, G5 + 12, C6 + 12))],
                            (0.33, note(S.glockenspiel, C6 + 12), 0.35))
    sfx["sword_alert"] = mix(0.5, (0, note(S.xylophone, E5 + 12), 0.7), (0.09, note(S.xylophone, A5 + 12), 0.8))
    # Fluturo!
    sfx["fly_punch"] = mix(0.25, (0, env(S.noise(int(SR * 0.11), 1500, 7000, seed=50), 0.015, 0.07), 0.6),
                           (0.05, S._tone_drum(170, 90, 0.04, 0.12), 0.35))
    pow_ = S._tone_drum(210, 70, 0.08, 0.3)
    sfx["fly_smash"] = mix(0.8, (0, pow_, 1.0), (0, ring(640, 0.25, 0.6), 0.45),
                           (0, env(S.noise(int(SR * 0.18), 600, 6000, seed=51), 0.002, 0.15), 0.45),
                           (0.05, note(S.marimba, C6, vel=0.8), 0.3), (0.09, note(S.glockenspiel, G5 + 12), 0.3))
    beep = lambda p: env(sweep(S.mtof(p), S.mtof(p), 0.09, harmonics=((1, 1), (3, 0.3))), 0.004, 0.03)
    sfx["fly_charge"] = mix(0.45, (0, beep(C6 + 7), 0.5), (0.12, beep(C6 + 7), 0.5),
                            (0.24, env(sweep(700, 1600, 0.18, shape=0.6, harmonics=((1, 1), (2, 0.2))), 0.01, 0.06), 0.35))
    sfx["fly_hurt"] = mix(0.9, (0, S._tone_drum(150, 60, 0.1, 0.4), 0.9), (0.02, oof, 0.5),
                          (0.35, tweet, 0.22), (0.47, tweet, 0.2), (0.62, tweet, 0.17))
    n = int(SR * 0.5)
    t = np.arange(n) / SR
    sfx["fly_whoosh"] = S.noise(n, 400, 5000, seed=52) * np.sin(np.pi * t / 0.5) ** 2 * 0.7
    sfx["fly_star"] = mix(0.45, (0, note(S.glockenspiel, C6 + 12), 0.6), (0, note(S.marimba, C6, vel=0.6), 0.35))
    sfx["fly_heart"] = mix(0.9, *[(0.06 * i, note(S.glockenspiel, p), 0.45) for i, p in enumerate((C6, E5 + 12, G5 + 12, C6 + 12))],
                           (0, note(S.marimba, C5, vel=0.7), 0.4))
    sfx["fly_boss"] = mix(1.8, (0, S._tone_drum(70, 40, 0.5, 1.2), 0.9), (0.3, S._tone_drum(66, 38, 0.5, 1.2), 0.9),
                          (0.6, S._tone_drum(58, 32, 0.8, 1.2), 1.0),
                          (0.6, env(sweep(90, 260, 1.0, shape=1.5, harmonics=((1, 1), (2, 0.5), (3, 0.25))), 0.2, 0.4), 0.35),
                          *[(0.6 + 0.02 * i, note(S.xylophone, p, vel=0.7), 0.3) for i, p in enumerate((C5 - 12, 66 - 12, C5))])
    sfx["fly_throw"] = mix(0.7, (0, env(S.noise(int(SR * 0.5), 200, 2500, seed=53), 0.04, 0.35), 0.6),
                           (0, env(sweep(180, 90, 0.4, harmonics=((1, 1), (2, 0.4))), 0.01, 0.25), 0.4))
    sfx["fly_return"] = mix(0.6, (0, ring(1400, 0.2, 0.5), 0.5), (0, env(S.noise(int(SR * 0.3), 1500, 9000, seed=54), 0.01, 0.25), 0.4),
                            (0.03, note(S.glockenspiel, G5 + 24), 0.35))
    sfx["fly_bosshit"] = mix(0.9, (0, S._tone_drum(120, 45, 0.15, 0.6), 1.0), (0, ring(420, 0.4, 0.8), 0.5),
                             (0, env(S.noise(int(SR * 0.3), 300, 5000, seed=55), 0.002, 0.25), 0.5))
    sfx["fly_bossdown"] = mix(2.0, (0, S._tone_drum(80, 30, 0.6, 1.4), 1.0),
                              (0, env(S.noise(int(SR * 1.4), 40, 1800, seed=56), 0.005, 1.1), 0.8),
                              *[(0.35 + 0.08 * i, note(S.marimba, p), 0.5) for i, p in enumerate((C5, E5, G5, C6, E5 + 12, G5 + 12))],
                              (0.9, note(S.glockenspiel, C6 + 12), 0.4))
    # Tenis!
    pok = S._tone_drum(330, 190, 0.035, 0.12)
    strings = env(S.noise(int(SR * 0.04), 1200, 6000, seed=70), 0.001, 0.035)
    sfx["tennis_hit"] = mix(0.35, (0, pok, 0.9), (0, strings, 0.35), (0.005, note(S.marimba, G5, vel=0.6), 0.18))
    whoosh = env(S.noise(int(SR * 0.2), 900, 7000, seed=71), 0.01, 0.17)
    sfx["tennis_power"] = mix(0.8, (0, S._tone_drum(260, 120, 0.06, 0.2), 1.0), (0, strings, 0.5), (0, whoosh, 0.45),
                              (0, ring(1500, 0.12, 0.3), 0.2), (0.04, note(S.glockenspiel, C6 + 12), 0.3),
                              (0.08, note(S.glockenspiel, G5 + 12), 0.25))
    sfx["tennis_bounce"] = mix(0.25, (0, S._tone_drum(170, 95, 0.045, 0.18), 0.9),
                               (0, env(S.noise(int(SR * 0.03), 300, 2500, seed=72), 0.001, 0.025), 0.25))
    n = int(SR * 0.5)
    t = np.arange(n) / SR
    rattle = S.noise(n, 250, 2500, seed=73) * np.exp(-t / 0.15) * (0.6 + 0.4 * np.sin(2 * np.pi * 28 * t))
    sfx["tennis_net"] = mix(0.6, (0, rattle, 0.6), (0, S._tone_drum(140, 80, 0.06, 0.2), 0.6))
    sfx["tennis_swish"] = env(np.concatenate([S.noise(int(SR * 0.05), 700, 3000, seed=74),
                                              S.noise(int(SR * 0.1), 1500, 6000, seed=75)]), 0.03, 0.08) * 0.5
    n = int(SR * 0.3)
    t = np.arange(n) / SR
    sfx["tennis_toss"] = mix(0.4, (0, S.noise(n, 600, 3000, seed=76) * np.sin(np.pi * t / 0.3) ** 2, 0.35),
                             (0.0, note(S.pizzicato, C6, vel=0.5), 0.3))
    # applause: many short claps scattered over a second and a half, a cheer under them, a happy triad
    rng = np.random.default_rng(77)
    claps = []
    for k in range(70):
        c = env(S.noise(int(SR * 0.012), 900, 5000, seed=100 + k), 0.0005, 0.009)
        claps.append((float(rng.uniform(0, 1.5) ** 1.3), c, float(rng.uniform(0.3, 0.8))))
    n = int(SR * 1.6)
    t = np.arange(n) / SR
    cheer = S.noise(n, 500, 3500, seed=78) * np.clip(t / 0.15, 0, 1) * np.exp(-t / 0.7)
    sfx["tennis_cheer"] = mix(1.8, *claps, (0, cheer, 0.35),
                              *[(0.02 + 0.05 * i, note(S.marimba, p, vel=0.7), 0.3) for i, p in enumerate((C5, E5, G5, C6))])
    n = int(SR * 0.9)
    t = np.arange(n) / SR
    aww = S.noise(n, 300, 1800, seed=79) * np.sin(np.pi * t / 0.9) * (1.0 - 0.5 * t)
    sfx["tennis_aww"] = mix(1.0, (0, aww, 0.4), (0.05, note(S.marimba, E5, vel=0.6), 0.35), (0.3, note(S.marimba, C5, vel=0.6), 0.35))
    sfx["tennis_call"] = mix(0.9, (0, note(S.vibraphone, C6), 0.6), (0.16, note(S.vibraphone, G5), 0.6))
    # Sporte! (the bump, the finish and clear jingles, a miss, the referee's whistle)
    sfx["sport_bump"] = mix(0.8, (0, S._tone_drum(130, 55, 0.12, 0.4), 1.0), (0, env(S.noise(int(SR * 0.25), 200, 2500, seed=80), 0.002, 0.2), 0.5),
                          (0.02, oof, 0.35), (0.3, tweet, 0.2), (0.42, tweet, 0.17))
    sfx["sport_finish"] = mix(1.8, *[(0.0 + 0.09 * i, note(S.marimba, p), 0.5) for i, p in enumerate((C5, E5, G5, C6, E5 + 12, G5 + 12))],
                            (0.55, note(S.glockenspiel, C6 + 12), 0.45), (0.55, S.drum(49, 0.9), 0.4),
                            *[(0.55, note(S.vibraphone, p), 0.35) for p in (C5, E5, G5, B5)])
    sfx["sport_clear"] = mix(0.5, (0, note(S.marimba, G5 + 12, vel=0.7), 0.6), (0.05, note(S.glockenspiel, C6 + 12), 0.35))
    sfx["sport_miss"] = mix(0.6, (0, env(sweep(700, 300, 0.4, shape=1.2, harmonics=((1, 1), (2, 0.2))), 0.01, 0.2), 0.4))
    tone = env(sweep(2100, 2100, 0.5, harmonics=((1, 1), (2, 0.3))) * (0.7 + 0.3 * np.sign(np.sin(2 * np.pi * 30 * np.arange(int(SR * 0.5)) / SR))), 0.01, 0.1)
    sfx["sport_whistle"] = mix(0.7, (0, tone, 0.5))
    bang = env(S.noise(int(SR * 0.25), 300, 9000, seed=91), 0.0005, 0.2)
    sfx["sport_gun"] = mix(0.9, (0, bang, 1.0), (0, S._tone_drum(120, 50, 0.08, 0.3), 0.7),
                           (0.05, env(S.noise(int(SR * 0.6), 200, 2000, seed=92), 0.05, 0.5), 0.25))
    sfx["sport_thud"] = mix(0.5, (0, S._tone_drum(110, 50, 0.08, 0.3), 1.0), (0, env(S.noise(int(SR * 0.2), 150, 1800, seed=93), 0.002, 0.18), 0.5))
    sfx["sport_glove"] = mix(0.35, (0, S._tone_drum(240, 140, 0.03, 0.12), 0.9), (0, env(S.noise(int(SR * 0.05), 600, 4000, seed=94), 0.001, 0.04), 0.6))
    sfx["sport_kick"] = mix(0.4, (0, S._tone_drum(150, 80, 0.05, 0.16), 1.0), (0, env(S.noise(int(SR * 0.05), 400, 3500, seed=96), 0.001, 0.04), 0.5))
    n = int(SR * 0.8)
    t = np.arange(n) / SR
    swish = S.noise(n, 400, 4000, seed=97) * np.exp(-t / 0.25) * (0.7 + 0.3 * np.sin(2 * np.pi * 18 * t))
    sfx["sport_net"] = mix(1.0, (0, swish, 0.7), (0, S._tone_drum(130, 70, 0.06, 0.2), 0.5))
    sfx["sport_volley"] = mix(0.35, (0, S._tone_drum(210, 120, 0.04, 0.14), 0.9), (0, env(S.noise(int(SR * 0.04), 800, 5000, seed=98), 0.001, 0.03), 0.5),
                              (0.005, note(S.marimba, C6, vel=0.6), 0.2))
    kart(sfx)
    arcade(sfx)
    space(sfx)
    fairy(sfx)
    boxing(sfx)
    runner(sfx)
    return sfx


def runner(sfx):
    """Vrapo! (the console polish of 2026-11-18): a soft footstep (the game plays it quietly on every stride, pitch
    varied), the golden egg's sparkle, a milestone's little fanfare, the new record's bigger one."""
    pad = S._tone_drum(95, 62, 0.035, 0.12) * 0.8
    scuff = env(S.noise(int(SR * 0.09), 300, 2200, seed=171), 0.002, 0.06) * 0.35
    sfx["step"] = mix(0.14, (0, pad, 0.9), (0.004, scuff, 0.6))
    sparkle = env(S.noise(int(SR * 0.7), 5000, 12000, seed=172), 0.01, 0.6)
    sfx["egg_gold"] = mix(1.3, (0, sparkle, 0.25),
                          *[(0.045 * i, note(S.glockenspiel, p), 0.5 - 0.03 * i) for i, p in
                            enumerate((C6, C6 + 4, C6 + 7, C6 + 12, C6 + 16, C6 + 19))],
                          (0.0, note(S.marimba, C5, vel=0.8), 0.35), (0.0, note(S.vibraphone, C6 + 7, dur=0.9), 0.3))
    sfx["milestone"] = mix(1.2, (0, note(S.marimba, G5), 0.6), (0.1, note(S.marimba, C6), 0.65), (0.2, note(S.marimba, E5 + 12), 0.7),
                           (0.2, note(S.glockenspiel, G5 + 12), 0.4), (0.2, S.drum(81, 0.9), 0.35))
    roll = env(S.noise(int(SR * 0.4), 2500, 9000, seed=173) * (0.6 + 0.4 * np.sin(2 * np.pi * 24 * np.arange(int(SR * 0.4)) / SR)), 0.05, 0.05)
    sfx["record"] = mix(1.9, (0, roll, 0.35),
                        *[(0.38 + 0.08 * i, note(S.marimba, p), 0.55) for i, p in enumerate((C5, E5, G5, C6))],
                        *[(0.38 + 0.08 * i, note(S.xylophone, p + 12), 0.3) for i, p in enumerate((C5, E5, G5))],
                        (0.7, note(S.glockenspiel, C6 + 12, dur=0.8), 0.5), (0.7, S.drum(49, 1.0), 0.35),
                        (0.7, env(S.noise(int(SR * 0.9), 5000, 12000, seed=174), 0.01, 0.8), 0.2))


def kart(sfx):
    """Gara!: the engine and the drift's screech as seamless loops (the game pitches the engine with the speed), boosts,
    the trick, pads, bumps, stars, gift boxes and the item's roulette, the items, laps."""
    # the engine: a soft, buzzy toy two-stroke; whole cycles of every part in the loop's second (seamless)
    n = SR
    t = np.arange(n) / SR
    f0 = 96.0
    phase = 2 * np.pi * f0 * t
    buzz = sum(a * np.sin(h * phase) for h, a in ((1, 1.0), (2, 0.55), (3, 0.38), (4, 0.22), (5, 0.14), (6, 0.08)))
    putter = 0.72 + 0.28 * np.sin(2 * np.pi * 24 * t) ** 2          # the firing: 24 pulses a second
    rumble = 0.25 * np.sin(2 * np.pi * 48 * t)
    air = S.noise(n + int(SR * 0.2), 200, 1800, seed=301)
    air = seamless(air, 0.2)[:n] * 0.18
    sfx["kart_engine"] = (buzz * putter + rumble) * 0.55 + air
    # the drift: a tyre's squeal, wobbling
    n = int(SR * 1.2)
    t = np.arange(n) / SR
    squeal = S.noise(n, 1800, 3800, seed=302) * (0.7 + 0.3 * np.sin(2 * np.pi * 7 * t))
    tone = 0.25 * np.sin(2 * np.pi * (2300 + 60 * np.sin(2 * np.pi * 3 * t)) * t)
    sfx["kart_drift"] = seamless(squeal * 0.6 + tone, 0.2)
    sfx["kart_drift_start"] = mix(0.35, (0, env(sweep(1900, 1500, 0.25, harmonics=((1, 1), (2, 0.2))), 0.005, 0.15), 0.3),
                                  (0, env(S.noise(int(SR * 0.25), 1500, 4000, seed=303), 0.005, 0.2), 0.4))
    # a boost: a whoosh up and a quick rising chime
    whoosh = env(S.noise(int(SR * 0.6), 500, 6000, seed=304) * np.linspace(0.3, 1, int(SR * 0.6)) ** 2, 0.02, 0.25)
    sfx["kart_boost"] = mix(0.8, (0, whoosh, 0.7), (0, env(sweep(300, 900, 0.5, shape=0.7, harmonics=((1, 1), (2, 0.4), (3, 0.2))), 0.01, 0.2), 0.25),
                            *[(0.03 + 0.05 * i, note(S.glockenspiel, p, vel=0.6), 0.25) for i, p in enumerate((G5, B5, D5 + 12))])
    # a trick: a sparkling run up and a happy "whee"
    sfx["kart_trick"] = mix(0.9, *[(0.0 + 0.045 * i, note(S.glockenspiel, p, vel=0.7), 0.3) for i, p in enumerate((C6, E5 + 12, G5 + 12, C6 + 12))],
                            (0, env(sweep(600, 1400, 0.35, shape=0.6, harmonics=((1, 1), (2, 0.3)), vibrato=0.01), 0.02, 0.12), 0.25),
                            (0.05, note(S.marimba, G5), 0.4))
    # a boost pad: a zap
    sfx["kart_pad"] = mix(0.45, (0, env(sweep(320, 1500, 0.22, shape=0.5, harmonics=((1, 1), (3, 0.35), (5, 0.15))), 0.003, 0.1), 0.45),
                          (0.06, note(S.glockenspiel, G5 + 12, vel=0.5), 0.25))
    # a bump: a soft rubbery boing
    n = int(SR * 0.3)
    t = np.arange(n) / SR
    boing = np.sin(2 * np.pi * np.cumsum(140 + 60 * np.exp(-t / 0.05) * np.cos(2 * np.pi * 22 * t)) / SR) * np.exp(-t / 0.09)
    sfx["kart_bump"] = mix(0.35, (0, boing, 0.7), (0, S._tone_drum(170, 80, 0.05, 0.2), 0.6))
    # a star: two bright notes
    sfx["kart_star"] = mix(0.4, (0, note(S.marimba, B5, vel=0.7), 0.5), (0.06, note(S.glockenspiel, E5 + 12, vel=0.7), 0.4))
    # a gift box: a pop and a sparkle
    pop = env(S.noise(int(SR * 0.04), 400, 3000, seed=305), 0.001, 0.03)
    sfx["kart_box"] = mix(0.6, (0, pop, 0.6), (0, S._tone_drum(500, 260, 0.03, 0.1), 0.5),
                          *[(0.03 + 0.03 * i, note(S.glockenspiel, p, vel=0.5), 0.22) for i, p in enumerate((E5 + 12, G5 + 12, B5 + 12))])
    # the roulette: ticks slowing down
    ticks, at = [], 0.0
    for i in range(10):
        ticks.append((at, note(S.xylophone, (C6, E5 + 12, G5 + 12)[i % 3], dur=0.1, vel=0.5), 0.3))
        at += 0.05 + 0.01 * i
    sfx["kart_roll"] = mix(at + 0.2, *ticks)
    sfx["kart_item"] = mix(0.7, (0, note(S.vibraphone, G5), 0.5), (0.1, note(S.vibraphone, D5 + 12), 0.5), (0.1, note(S.glockenspiel, G5 + 12, vel=0.5), 0.2))
    # a rocket: a deep whoosh with a rumble
    n = int(SR * 1.1)
    t = np.arange(n) / SR
    roar = S.noise(n, 80, 2500, seed=306) * np.clip(t / 0.05, 0, 1) * np.exp(-t / 0.5)
    sfx["kart_rocket"] = mix(1.2, (0, roar, 0.8), (0, env(sweep(120, 480, 0.8, shape=0.6, harmonics=((1, 1), (2, 0.5))), 0.02, 0.3), 0.35))
    # a shield: a bubble's bloop and a shimmer
    sfx["kart_shield"] = mix(0.8, (0, env(sweep(300, 900, 0.3, shape=0.4, harmonics=((1, 1), (2, 0.2)), vibrato=0.02), 0.01, 0.15), 0.4),
                             *[(0.12 + 0.04 * i, note(S.celesta, p, vel=0.5), 0.25) for i, p in enumerate((G5, C6, E5 + 12, G5 + 12))])
    # honey: a gloopy blorp
    sfx["kart_honey"] = mix(0.6, (0, env(sweep(420, 140, 0.4, shape=1.4, harmonics=((1, 1), (2, 0.3)), vibrato=0.05), 0.01, 0.2), 0.6),
                            (0.05, env(S.noise(int(SR * 0.2), 150, 900, seed=307), 0.01, 0.15), 0.3))
    # paint: a splat
    sfx["kart_paint"] = mix(0.6, (0, env(S.noise(int(SR * 0.3), 300, 3000, seed=308), 0.002, 0.22), 0.7),
                            (0, S._tone_drum(260, 90, 0.08, 0.3), 0.6), (0.1, env(sweep(700, 300, 0.25), 0.01, 0.15), 0.2))
    # a lap, the last lap
    sfx["kart_lap"] = mix(0.9, (0, note(S.vibraphone, G5), 0.5), (0.12, note(S.vibraphone, D5 + 12), 0.5), (0.12, note(S.glockenspiel, G5 + 12, vel=0.6), 0.3))
    sfx["kart_final"] = mix(1.6, *[(0.0 + 0.11 * i, note(S.marimba, p), 0.5) for i, p in enumerate((G5 - 12, B5 - 12, D5, G5))],
                            *[(0.44, note(S.vibraphone, p), 0.4) for p in (G5, B5, D5 + 12)], (0.44, note(S.glockenspiel, G5 + 12), 0.35),
                            (0.44, S.drum(49, 0.8), 0.35))


def arcade(sfx):
    """Lunapark!: a toy hammer's bonk, a hedgehog's "eek", pops, a juicy slice, the funhouse wall's whoosh, a wing's flap,
    a balloon's boing, the ticket machine."""
    # a bonk: a hollow knock and a squeak (a squeaky toy hammer)
    knock = S._tone_drum(420, 180, 0.05, 0.16)
    squeak = env(sweep(1700, 2600, 0.09, shape=0.6, harmonics=((1, 1), (2, 0.3))), 0.004, 0.05)
    sfx["arcade_bonk"] = mix(0.3, (0, knock, 0.9), (0.005, squeak, 0.35), (0, env(S.noise(int(SR * 0.03), 800, 5000, seed=401), 0.001, 0.02), 0.4))
    # a hedgehog's prickle: a sharp "eek" going down and a buzzy thud
    eek = env(sweep(1900, 700, 0.22, shape=1.3, harmonics=((1, 1), (2, 0.5), (3, 0.3)), vibrato=0.04), 0.004, 0.08)
    sfx["arcade_ouch"] = mix(0.45, (0, eek, 0.55), (0, S._tone_drum(160, 70, 0.08, 0.25), 0.7))
    # a pop: a balloon's crack and a little upward chirp
    crack = env(S.noise(int(SR * 0.05), 300, 9000, seed=402), 0.0005, 0.04)
    chirp = env(sweep(500, 1300, 0.08, shape=0.5, harmonics=((1, 1), (2, 0.2))), 0.002, 0.05)
    sfx["arcade_pop"] = mix(0.3, (0, crack, 0.9), (0.01, chirp, 0.35))
    # a slice: a quick swish up and a wet splat
    swish = env(S.noise(int(SR * 0.12), 1500, 9000, seed=403) * np.linspace(0.3, 1, int(SR * 0.12)), 0.01, 0.04)
    splat = env(S.noise(int(SR * 0.16), 150, 1800, seed=404), 0.002, 0.12)
    sfx["arcade_slice"] = mix(0.35, (0, swish, 0.6), (0.07, splat, 0.7), (0.07, S._tone_drum(260, 110, 0.04, 0.12), 0.35))
    # the wall coming: a low rushing whoosh that swells
    n = int(SR * 1.3)
    t = np.arange(n) / SR
    rush = S.noise(n, 90, 1400, seed=405) * np.sin(np.pi * np.clip(t / 1.3, 0, 1)) ** 1.6
    sfx["arcade_wall"] = mix(1.35, (0, rush, 0.8), (0, env(sweep(70, 140, 1.2, shape=1.2, harmonics=((1, 1), (2, 0.4))), 0.2, 0.3), 0.3))
    # a flap: a soft feathery whoosh
    n = int(SR * 0.16)
    t = np.arange(n) / SR
    sfx["arcade_flap"] = S.noise(n, 400, 3200, seed=406) * np.sin(np.pi * t / 0.16) ** 2 * 0.8
    # a balloon's boing: a rubbery wobble
    n = int(SR * 0.32)
    t = np.arange(n) / SR
    wob = np.sin(2 * np.pi * np.cumsum(260 + 120 * np.exp(-t / 0.08) * np.sin(2 * np.pi * 14 * t)) / SR) * np.exp(-t / 0.12)
    sfx["arcade_boing"] = mix(0.34, (0, wob, 0.7), (0, env(S.noise(int(SR * 0.03), 300, 2000, seed=407), 0.001, 0.02), 0.25))
    # the ticket machine: a ratchet of clicks, then a ding
    clicks = [(0.045 * i, env(S.noise(int(SR * 0.012), 2000, 8000, seed=410 + i), 0.0005, 0.01), 0.5) for i in range(6)]
    sfx["arcade_ticket"] = mix(0.9, *clicks, (0.3, note(S.glockenspiel, C6 + 7), 0.6), (0.3, note(S.vibraphone, G5 + 12, vel=0.6), 0.3))


def space(sfx):
    """Yjet!: the jetpack's soft roar as a seamless loop (the game pitches it with the speed), a star's twinkle, a ring's
    whoosh and chime, a big star, a bump into a rock, a perfect gate, a rescued alien's "wheee", the launch, the landing,
    the low-power beeps, the tow ship's horn."""
    # the jetpack: airy noise over a low hum, whole cycles in the loop's second
    n = SR
    t = np.arange(n) / SR
    air = seamless(S.noise(n + int(SR * 0.2), 120, 2600, seed=501), 0.2)[:n]
    hum = 0.35 * np.sin(2 * np.pi * 70 * t) + 0.15 * np.sin(2 * np.pi * 140 * t)
    flutter = 0.8 + 0.2 * np.sin(2 * np.pi * 12 * t)
    sfx["space_jet"] = air * flutter * 0.7 + hum * 0.4
    # a star: a tiny bright twinkle (the game raises its pitch as they come one after another)
    sfx["space_star"] = mix(0.35, (0, note(S.glockenspiel, C6 + 7, vel=0.6), 0.55), (0.03, note(S.celesta, C6 + 12, vel=0.5), 0.3))
    # a ring: a whoosh through and a rising chime
    n = int(SR * 0.5)
    swoosh = env(S.noise(n, 600, 7000, seed=502) * np.sin(np.pi * np.arange(n) / n) ** 1.5, 0.01, 0.1)
    sfx["space_ring"] = mix(0.9, (0, swoosh, 0.55), *[(0.05 + 0.06 * i, note(S.vibraphone, p, vel=0.7), 0.4) for i, p in enumerate((D5, F5 + 1, A5, D5 + 12))])
    # a big star: a sparkling run and a chord
    sfx["space_bigstar"] = mix(1.2, *[(0.04 * i, note(S.glockenspiel, p, vel=0.6), 0.3) for i, p in enumerate((D5 + 12, F5 + 13, A5 + 12, D5 + 24))],
                               *[(0.2, note(S.celesta, p, vel=0.7), 0.35) for p in (D5, F5 + 1, A5, C6 + 1)])
    # a bump: a soft rocky thud and a squeaky "oof" going down
    oof = env(sweep(900, 380, 0.22, shape=1.2, harmonics=((1, 1), (2, 0.35)), vibrato=0.03), 0.005, 0.1)
    sfx["space_bump"] = mix(0.5, (0, S._tone_drum(110, 55, 0.09, 0.3), 0.9), (0, env(S.noise(int(SR * 0.15), 100, 1600, seed=503), 0.002, 0.12), 0.5), (0.04, oof, 0.35))
    # a perfect gate: a magic "ta-da"
    sfx["space_gate"] = mix(1.3, (0, note(S.vibraphone, A5, vel=0.7), 0.45), (0.1, note(S.vibraphone, D5 + 12, vel=0.8), 0.5),
                            *[(0.1, note(S.celesta, p, vel=0.6), 0.3) for p in (F5 + 13, A5 + 12)],
                            *[(0.18 + 0.035 * i, note(S.glockenspiel, p, vel=0.5), 0.22) for i, p in enumerate((D5 + 24, F5 + 25, A5 + 24, D5 + 36))])
    # an alien rescued: a cute rising "wheee" and a giggle
    whee = env(sweep(500, 1400, 0.35, shape=0.6, harmonics=((1, 1), (2, 0.25), (3, 0.1)), vibrato=0.035), 0.01, 0.1)
    giggle = [(0.4 + 0.08 * i, env(sweep(1300 - 80 * i, 1100 - 80 * i, 0.06, harmonics=((1, 1), (2, 0.2))), 0.004, 0.03), 0.3) for i in range(3)]
    sfx["space_alien"] = mix(0.7, (0, whee, 0.5), *giggle)
    # the launch: a rocket's roar rising
    n = int(SR * 1.3)
    t = np.arange(n) / SR
    roar = S.noise(n, 60, 3000, seed=504) * np.clip(t / 0.08, 0, 1) * np.exp(-t / 0.7)
    sfx["space_launch"] = mix(1.4, (0, roar, 0.8), (0, env(sweep(90, 420, 1.0, shape=0.7, harmonics=((1, 1), (2, 0.5), (3, 0.2))), 0.05, 0.3), 0.35),
                              (0.15, note(S.glockenspiel, D5 + 12, vel=0.5), 0.2))
    # the landing: a soft bump on the pad and a happy two notes
    sfx["space_land"] = mix(1.0, (0, S._tone_drum(150, 70, 0.08, 0.25), 0.7), (0.08, note(S.marimba, A5, vel=0.8), 0.5), (0.22, note(S.marimba, D5 + 12, vel=0.9), 0.55))
    # low power: two gentle beeps going down
    beep = lambda f: env(sweep(f, f, 0.12, harmonics=((1, 1), (3, 0.2))), 0.005, 0.04)
    sfx["space_low"] = mix(0.45, (0, beep(880), 0.4), (0.2, beep(660), 0.4))
    # the tow ship: a friendly "toot-toot"
    toot = lambda f: env(sweep(f, f * 0.98, 0.18, harmonics=((1, 1), (2, 0.6), (3, 0.4), (4, 0.2))), 0.01, 0.06)
    sfx["space_tow"] = mix(0.6, (0, toot(330), 0.45), (0.25, toot(392), 0.45))


def fairy(sfx):
    """Zana!: a cast for each of the six shapes (a swoosh and the shape's own little tune), a bubble's pop, a creature
    turning happy (and a guardian), the fairy bumped, a gloom orb's "pew", a charge's whoosh, sparkles, a flower ring, a
    heart flower, the magic full and the big magic, a wave cleared, a pixie freed, a guardian arriving, a fizzle."""
    def swoosh(seconds, lo, hi, seed, shape=1.5):
        n = int(SR * seconds)
        return env(S.noise(n, lo, hi, seed=seed) * np.sin(np.pi * np.arange(n) / n) ** shape, 0.01, 0.08)

    def run(fn, pitches, step, vel=0.7, gain=0.45, start=0.0):
        return [(start + step * i, note(fn, p, vel=vel), gain) for i, p in enumerate(pitches)]
    # the casts: every one a soft swoosh, then the shape's own figure
    sfx["fairy_cast_swipe"] = mix(0.8, (0, swoosh(0.28, 900, 9000, 601), 0.5), *run(S.glockenspiel, (G5, C6 + 7), 0.07, start=0.08))
    sfx["fairy_cast_vee"] = mix(0.9, (0, swoosh(0.3, 500, 6000, 602), 0.4),
                                *run(S.marimba, (G5 + 12, C6, G5 + 12), 0.08, vel=0.8, gain=0.55, start=0.05))
    n = int(SR * 0.3)
    crackle = env(S.noise(n, 1500, 9000, seed=603) * (np.random.RandomState(603).rand(n) > 0.92), 0.002, 0.15)
    sfx["fairy_cast_peak"] = mix(0.9, (0, swoosh(0.3, 400, 5000, 604), 0.4),
                                 *run(S.xylophone, (C6, G5 + 12, C6 + 12, G5 + 12), 0.06, vel=0.8, gain=0.5, start=0.04),
                                 (0.04, crackle, 0.35))
    bloop = env(sweep(380, 980, 0.16, shape=0.6, harmonics=((1, 1), (2, 0.2))), 0.004, 0.06)
    sfx["fairy_cast_circle"] = mix(1.0, (0, swoosh(0.32, 600, 7000, 605), 0.4), (0.03, bloop, 0.4),
                                   *run(S.vibraphone, (C6, E5 + 12, G5 + 12, C6 + 12), 0.06, vel=0.7, gain=0.45, start=0.06))
    zap = env(S.noise(int(SR * 0.12), 2500, 11000, seed=606), 0.001, 0.08)
    sfx["fairy_cast_bolt"] = mix(0.9, (0, zap, 0.5),
                                 *run(S.glockenspiel, (C6 + 12, G5 + 12, C6 + 19, G5 + 19, C6 + 24), 0.04, vel=0.6, gain=0.35, start=0.02),
                                 (0.18, zap, 0.25))
    sfx["fairy_cast_heart"] = mix(1.2, (0, swoosh(0.35, 400, 5000, 607, 2.0), 0.35),
                                  *run(S.harp, (F5, A5, C6, F5 + 12), 0.07, vel=0.75, gain=0.45, start=0.04),
                                  *[(0.3, note(S.celesta, p, vel=0.6), 0.3) for p in (A5 + 12, C6 + 12)])
    # a bubble pops
    pop = env(sweep(1300, 320, 0.07, shape=0.5, harmonics=((1, 1), (2, 0.3))), 0.001, 0.03)
    sfx["fairy_pop"] = mix(0.3, (0, pop, 0.7), (0, env(S.noise(int(SR * 0.03), 2000, 9000, seed=608), 0.0005, 0.02), 0.35),
                           (0.02, note(S.glockenspiel, C6 + 12, vel=0.4), 0.15))
    # a creature turns happy: a bright "pwing" and a cascade of bells
    pwing = env(sweep(600, 1500, 0.2, shape=0.5, harmonics=((1, 1), (2, 0.3), (3, 0.1))), 0.004, 0.1)
    sfx["fairy_purify"] = mix(1.1, (0, pwing, 0.35),
                              *run(S.glockenspiel, (C6 + 12, G5 + 12, E5 + 12, C6, E5 + 12, G5 + 12, C6 + 12), 0.045, vel=0.55, gain=0.3, start=0.05),
                              (0.05, note(S.celesta, C6 + 4, vel=0.6), 0.3))
    sfx["fairy_chief_saved"] = mix(2.2, (0, pwing, 0.3), *[(0.0, note(S.vibraphone, p, vel=0.8), 0.35) for p in (C5, E5, G5, B5)],
                                   *run(S.glockenspiel, (C6, E5 + 12, G5 + 12, B5 + 12, C6 + 12, E5 + 24, G5 + 24, C6 + 24), 0.06, vel=0.6,
                                        gain=0.3, start=0.12),
                                   *[(0.62, note(S.celesta, p, vel=0.8), 0.35) for p in (C6, E5 + 12, G5 + 12, C6 + 12)])
    # the fairy bumped: a soft thud and an "ow" going down
    ow = env(sweep(1000, 420, 0.24, shape=1.1, harmonics=((1, 1), (2, 0.3)), vibrato=0.03), 0.005, 0.1)
    sfx["fairy_hit"] = mix(0.6, (0, S._tone_drum(140, 60, 0.08, 0.3), 0.8),
                           (0, env(S.noise(int(SR * 0.12), 200, 2500, seed=609), 0.002, 0.1), 0.4), (0.05, ow, 0.4))
    # a gloom orb thrown: a dark, soft "pew"
    sfx["fairy_shot"] = mix(0.45, (0, env(sweep(700, 180, 0.3, shape=0.8, harmonics=((1, 1), (2, 0.4), (3, 0.15))), 0.003, 0.12), 0.5),
                            (0, swoosh(0.25, 150, 1800, 610), 0.4))
    # a charge: a whoosh rising toward the fairy
    n = int(SR * 0.6)
    t = np.arange(n) / SR
    rise = S.noise(n, 200, 4000, seed=611) * (t / 0.6) ** 2 * np.clip((0.6 - t) / 0.05, 0, 1)
    sfx["fairy_charge"] = mix(0.65, (0, rise, 0.7), (0, env(sweep(160, 420, 0.55, shape=1.5, harmonics=((1, 1), (2, 0.5))), 0.05, 0.05), 0.3))
    # a sparkle (the game raises its pitch as they come one after another)
    sfx["fairy_sparkle"] = mix(0.35, (0, note(S.glockenspiel, C6 + 14, vel=0.55), 0.5), (0.03, note(S.celesta, C6 + 21, vel=0.45), 0.25))
    # a flower ring: a whoosh through and a rising chime
    sfx["fairy_ring"] = mix(0.9, (0, swoosh(0.45, 600, 7000, 612), 0.5),
                            *run(S.vibraphone, (E5, G5 + 1, B5, E5 + 12), 0.06, vel=0.7, gain=0.4, start=0.05))
    # a heart flower: two notes up and a harp's sparkle
    sfx["fairy_heart"] = mix(0.9, (0, note(S.marimba, A5, vel=0.8), 0.5), (0.1, note(S.marimba, E5 + 12, vel=0.9), 0.55),
                             *run(S.harp, (A5 + 12, C6 + 13, E5 + 24), 0.04, vel=0.6, gain=0.25, start=0.18))
    # the magic full: a shimmering rising run
    sfx["fairy_ready"] = mix(1.2, *run(S.celesta, (C5, E5, G5, C6, E5 + 12, G5 + 12, C6 + 12), 0.05, vel=0.6, gain=0.35),
                             (0.36, note(S.glockenspiel, C6 + 24, vel=0.6), 0.3))
    # the big magic: a swelling sparkle, a chord and bells all over
    n = int(SR * 1.8)
    t = np.arange(n) / SR
    swell = S.noise(n, 1500, 12000, seed=613) * np.clip(t / 0.5, 0, 1) * np.exp(-np.clip(t - 0.5, 0, None) / 0.5)
    sfx["fairy_magic"] = mix(2.4, (0, swell, 0.35), *[(0.35, note(S.vibraphone, p, vel=0.8), 0.32) for p in (F5, A5, C6, E5 + 12)],
                             *run(S.glockenspiel, (F5 + 12, A5 + 12, C6 + 12, E5 + 24, F5 + 24, A5 + 24, C6 + 24), 0.04, vel=0.55, gain=0.25,
                                  start=0.3),
                             *run(S.harp, (F5, A5, C6, F5 + 12, A5 + 12, C6 + 12), 0.035, vel=0.7, gain=0.3, start=0.32))
    # a wave cleared: a happy "ta-da"
    sfx["fairy_wave"] = mix(1.0, (0, note(S.marimba, G5, vel=0.8), 0.45), (0.1, note(S.marimba, C6, vel=0.9), 0.5),
                            *[(0.2, note(S.vibraphone, p, vel=0.7), 0.3) for p in (E5 + 12, G5 + 12)])
    # a pixie freed: a cute "wheee", a giggle and chimes
    whee = env(sweep(700, 1800, 0.35, shape=0.6, harmonics=((1, 1), (2, 0.25), (3, 0.1)), vibrato=0.04), 0.01, 0.1)
    giggle = [(0.42 + 0.075 * i, env(sweep(1600 - 90 * i, 1350 - 90 * i, 0.055, harmonics=((1, 1), (2, 0.2))), 0.004, 0.03), 0.28)
              for i in range(4)]
    sfx["fairy_rescue"] = mix(1.1, (0, whee, 0.45), *giggle,
                              *run(S.glockenspiel, (C6 + 12, E5 + 24, G5 + 24), 0.05, vel=0.5, gain=0.25, start=0.1))
    # a guardian arrives: a deep, round "bwong" and a mysterious chord (still a toy)
    sfx["fairy_chief"] = mix(1.8, (0, S._tone_drum(90, 45, 0.25, 0.9), 0.7),
                             *[(0.05, note(S.vibraphone, p - 12, vel=0.75), 0.35) for p in (A5, C6, E5 + 12)],
                             (0.4, note(S.vibraphone, G5 + 1 - 12, vel=0.7), 0.3), (0.0, swoosh(1.0, 80, 900, 614, 1.0), 0.3))
    # a fizzle: the spell did not take
    n = int(SR * 0.3)
    sfx["fairy_fizzle"] = mix(0.5, (0, env(S.noise(n, 300, 3000, seed=615) * np.linspace(1, 0, n) ** 2, 0.005, 0.1), 0.45),
                              (0, env(sweep(520, 240, 0.25, shape=0.8, harmonics=((1, 1), (2, 0.25))), 0.005, 0.1), 0.3))


def boxing(sfx):
    """Superboks!: the ring's bell (one ding to start a round, three to end the fight), glove punches (a padded thump and
    a slap; a heavier one for counters and power punches), a punch into a guard (a muffled thud), the swish of a missed or
    dodged punch, the referee's count (a woody knock), a knockdown's crash, the super punch (a rising roar and a boom),
    the power full (a rising chime), a fireball's roar, a lightning crack."""
    sfx["box_bell"] = mix(1.6, (0, ring(1180, 0.9, 1.6), 0.8), (0, ring(1770, 0.5, 1.6), 0.25))
    sfx["box_bells"] = mix(2.0, (0, ring(1180, 0.5, 1.2), 0.7), (0.32, ring(1180, 0.5, 1.2), 0.7), (0.64, ring(1180, 0.9, 1.3), 0.8))
    thump = S._tone_drum(170, 90, 0.04, 0.16)
    slap = env(S.noise(int(SR * 0.06), 800, 6000, seed=701), 0.001, 0.035)
    sfx["box_punch"] = mix(0.35, (0, thump, 0.95), (0, slap, 0.7))
    sfx["box_punch_big"] = mix(0.6, (0, S._tone_drum(120, 55, 0.06, 0.28), 1.0), (0, slap, 0.85),
                               (0, env(S.noise(int(SR * 0.12), 200, 2500, seed=702), 0.001, 0.08), 0.5))
    sfx["box_block"] = mix(0.3, (0, S._tone_drum(230, 160, 0.03, 0.09), 0.8), (0, env(S.noise(int(SR * 0.04), 300, 1800, seed=703), 0.001, 0.03), 0.4))
    n = int(SR * 0.35)
    t = np.arange(n) / SR
    sfx["box_swish"] = mix(0.4, (0, S.noise(n, 700, 5000, seed=704) * np.sin(np.pi * np.clip(t / 0.3, 0, 1)) ** 2, 0.6))
    sfx["box_count"] = mix(0.3, (0, S._tone_drum(520, 380, 0.01, 0.07), 0.8), (0, env(S.noise(int(SR * 0.02), 1500, 6000, seed=705), 0.001, 0.015), 0.4))
    sfx["box_down"] = mix(1.2, (0, S._tone_drum(80, 40, 0.12, 0.5), 1.0), (0.02, env(S.noise(int(SR * 0.4), 100, 1800, seed=706), 0.005, 0.25), 0.6),
                          (0.15, S._tone_drum(110, 60, 0.05, 0.25), 0.5))
    n = int(SR * 1.0)
    t = np.arange(n) / SR
    roar = S.noise(n, 200, 3000, seed=707) * np.clip(t / 0.85, 0, 1) ** 2
    sfx["box_super"] = mix(1.6, (0, roar, 0.7), (0, sweep(220, 880, 0.9, shape=1.4, harmonics=((1, 1), (2, 0.4), (3, 0.2))) * np.clip(np.arange(int(SR * 0.9)) / (SR * 0.9), 0, 1), 0.35),
                           (0.9, S._tone_drum(70, 35, 0.15, 0.6), 1.0), (0.9, env(S.noise(int(SR * 0.5), 100, 3000, seed=708), 0.002, 0.3), 0.7))
    sfx["box_power"] = mix(1.0, *[(0.08 * i, note(S.vibraphone, p, vel=0.75), 0.4) for i, p in enumerate((C5, E5, G5, C6))])
    n = int(SR * 0.9)
    t = np.arange(n) / SR
    sfx["box_fire"] = mix(1.0, (0, S.noise(n, 150, 2200, seed=709) * (0.6 + 0.4 * np.sin(2 * np.pi * 23 * t)) * np.sin(np.pi * t / 0.9), 0.8))
    n = int(SR * 0.5)
    t = np.arange(n) / SR
    crack = S.noise(n, 1500, 9000, seed=710) * np.exp(-t / 0.06) + S.noise(n, 60, 600, seed=711) * np.exp(-t / 0.25) * 0.6
    sfx["box_zap"] = mix(0.6, (0, crack, 0.9))


def ring(f, decay, seconds):
    """A struck metal bar (a toy sword's clang): inharmonic partials that fade at different speeds."""
    n = int(SR * seconds)
    t = np.arange(n) / SR
    out = np.zeros(n)
    for ratio, amp, d in ((1.0, 1.0, decay), (2.76, 0.6, decay * 0.6), (5.4, 0.35, decay * 0.35), (8.93, 0.2, decay * 0.2)):
        out += amp * np.sin(2 * np.pi * f * ratio * t) * np.exp(-t / d)
    return out * np.clip(t / 0.001, 0, 1)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    only = sys.argv[1:]
    for name, x in make().items():
        if only and not any(name.startswith(prefix) for prefix in only):
            continue
        x = x / (np.max(np.abs(x)) + 1e-9) * 0.9
        out = encode(np.stack([x, x]), os.path.join(OUT_DIR, f"{name}.ogg"))
        print(f"  {name:10s} {len(x) / SR:4.2f} s -> {os.path.relpath(out, ROOT)}")


if __name__ == "__main__":
    main()
