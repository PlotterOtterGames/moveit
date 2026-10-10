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
    # Fruta!
    swish = env(S.noise(int(SR * 0.12), 2000, 9000, seed=7), 0.01, 0.08)
    splat = env(S.noise(int(SR * 0.14), 150, 1300, seed=8), 0.002, 0.11)
    sfx["slice"] = mix(0.35, (0, swish, 0.6), (0.05, splat, 0.7),
                       (0.05, env(sweep(320, 140, 0.08, harmonics=((1, 1),)), 0.002, 0.05), 0.35))
    sfx["splat"] = mix(0.3, (0, splat, 0.8), (0, env(sweep(260, 120, 0.1, harmonics=((1, 1),)), 0.002, 0.06), 0.4))
    sfx["bomb"] = mix(1.2, (0, S._tone_drum(90, 34, 0.35, 1.1), 1.0),
                      (0, env(S.noise(int(SR * 1.1), 30, 450, seed=9), 0.005, 0.8), 0.7))
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
    return sfx


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
