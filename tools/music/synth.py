"""Tundu's little software synthesizer: Wii-style instruments in plain numpy (no samples, no extra packages).

Instruments follow General MIDI program numbers so any MIDI file works:
  mallets (marimba, vibraphone, xylophone, glockenspiel, celesta, steel drums) = struck-bar modal synthesis
  plucked (pizzicato strings, nylon guitar, harp, banjo-ish, upright/finger bass) = Karplus-Strong / additive
  keys (electric piano = FM, piano = additive), winds (flute, recorder, whistle, ocarina), soft lead and pad
  drums (channel 10): kick, snare, brushes, rim, claps, hats, ride, crash, shaker, tambourine, congas, bongos,
  wood blocks, claves, cowbell, triangle
Unknown programs fall back to a soft electric piano, so nothing is ever silent.
"""

import math
from functools import lru_cache

import numpy as np

SR = 32000            # the Wii's own audio rate


def mtof(p):
    return 440.0 * 2 ** ((p - 69) / 12)


def _t(n):
    return np.arange(n) / SR


def _attack(x, ms=2.0):
    k = min(len(x), max(1, int(SR * ms / 1000)))
    x[:k] *= np.linspace(0, 1, k)
    return x


def _fade(x, ms=30.0):
    k = min(len(x), max(1, int(SR * ms / 1000)))
    x[-k:] *= np.linspace(1, 0, k)
    return x


def noise(n, lo=None, hi=None, seed=0):
    """White noise band-limited in the frequency domain (lo..hi Hz), normalised to ±1."""
    x = np.random.default_rng(seed).standard_normal(max(n, 2))
    spec = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / SR)
    mag = np.ones_like(f)
    if lo:
        mag *= 1 / (1 + (lo / np.maximum(f, 1.0)) ** 4)
    if hi:
        mag *= 1 / (1 + (f / hi) ** 4)
    y = np.fft.irfft(spec * mag, len(x))[:n]
    return y / (np.max(np.abs(y)) + 1e-9)


# ---------------------------------------------------------------------------------------------------- mallets

def _bar(f, n, partials, click=0.04, bend_cents=0.0):
    """Struck bar: decaying sine partials (ratio, amplitude, decay seconds) + a soft mallet click."""
    t = _t(n)
    out = np.zeros(n)
    bend = 1 + (2 ** (bend_cents / 1200) - 1) * np.exp(-t / 0.03) if bend_cents else 1.0
    phase_t = np.cumsum(np.full(n, 1 / SR) * bend) if bend_cents else t
    for ratio, amp, decay in partials:
        fr = f * ratio
        if fr > SR * 0.45:
            continue
        out += amp * np.sin(2 * np.pi * fr * phase_t) * np.exp(-t / decay)
    if click:
        k = min(n, int(SR * 0.006))
        out[:k] += click * noise(k, lo=1500, hi=9000, seed=int(f)) * np.linspace(1, 0, k)
    return _attack(out, 1.0)


def marimba(f, dur, vel):
    d = float(np.clip(0.9 * (220 / f) ** 0.5, 0.18, 1.3))
    n = int(SR * min(d * 4.5, 3.0))
    return _bar(f, n, [(1, 1.0, d), (3.93, 0.28 * vel, d * 0.22), (9.7, 0.07 * vel, d * 0.08)], click=0.05 * vel)


def xylophone(f, dur, vel):
    d = float(np.clip(0.35 * (440 / f) ** 0.4, 0.08, 0.5))
    n = int(SR * d * 5)
    return _bar(f, n, [(1, 1.0, d), (3.0, 0.45 * vel, d * 0.35), (6.0, 0.2 * vel, d * 0.15)], click=0.08 * vel)


def vibraphone(f, dur, vel):
    ring = dur + 0.35                       # the pedal lets it ring a little past the note
    n = int(SR * (ring + 0.2))
    t = _t(n)
    x = _bar(f, n, [(1, 1.0, 2.6), (4.0, 0.22 * vel, 0.6), (10.0, 0.05 * vel, 0.18)], click=0.02)
    x *= 1 - 0.22 * (0.5 + 0.5 * np.sin(2 * np.pi * 5.2 * t))      # motor tremolo
    x *= np.clip((ring + 0.2 - t) / 0.2, 0, 1)                     # damper
    return x


def glockenspiel(f, dur, vel):
    n = int(SR * 2.2)
    return _bar(f, n, [(1, 1.0, 1.4), (2.76, 0.45, 0.55), (5.40, 0.22, 0.25), (8.93, 0.1, 0.12)], click=0.03)


def celesta(f, dur, vel):
    n = int(SR * 1.6)
    return _bar(f, n, [(1, 1.0, 0.9), (2.0, 0.25, 0.4), (4.0, 0.08, 0.15)], click=0.01)


def steel_drum(f, dur, vel):
    n = int(SR * 1.4)
    return _bar(f, n, [(1, 1.0, 0.75), (2.0, 0.55, 0.45), (3.0, 0.22, 0.3), (3.92, 0.18, 0.2), (5.1, 0.06, 0.1)],
                click=0.02, bend_cents=35)


# ---------------------------------------------------------------------------------------------------- plucked

def _karplus(f, seconds, decay, bright, seed, pluck_pos=0.18):
    """Karplus-Strong string at 2× rate (better tuning), returned at SR."""
    sr2 = SR * 2
    n = int(sr2 * seconds)
    period = sr2 / f
    L = max(2, int(round(period - 0.5)))
    g = 10 ** (-3 / max(1e-3, decay * f))
    y = np.zeros(n + L + 2)
    exc = np.random.default_rng(seed).uniform(-1, 1, L + 2)
    for _ in range(int(1 + (1 - bright) * 6)):          # darker excitation = rounder tone
        exc = 0.5 * (exc + np.roll(exc, 1))
    shift = max(1, int(L * pluck_pos))
    exc = exc - np.roll(exc, shift)                       # pluck position comb
    y[:L + 2] = exc
    start = L + 2
    while start < len(y):
        end = min(start + L, len(y))
        y[start:end] = g * 0.5 * (y[start - L:end - L] + y[start - L - 1:end - L - 1])
        start = end
    y = y[:n]
    y = 0.5 * (y[0::2] + y[1::2])                          # back to SR (simple averaging decimation)
    return y / (np.max(np.abs(y)) + 1e-9)


def pizzicato(f, dur, vel):
    x = _karplus(f, 0.9, decay=0.45, bright=0.55 + 0.35 * vel, seed=int(f * 7))
    return _fade(_attack(x, 3.0), 120)


def nylon_guitar(f, dur, vel):
    secs = min(2.5, dur + 1.2)
    x = _karplus(f, secs, decay=1.6, bright=0.35 + 0.3 * vel, seed=int(f * 3))
    return _fade(_attack(x, 2.0), 200)


def harp(f, dur, vel):
    x = _karplus(f, 2.2, decay=1.8, bright=0.5, seed=int(f * 5), pluck_pos=0.5)
    return _fade(_attack(x, 2.0), 200)


def upright_bass(f, dur, vel):
    secs = min(2.0, dur + 0.25)
    n = int(SR * secs)
    t = _t(n)
    env = np.exp(-t / 0.9) * np.clip((secs - t) / 0.08, 0, 1)
    x = (np.sin(2 * np.pi * f * t) + 0.45 * np.sin(4 * np.pi * f * t) * np.exp(-t / 0.25)
         + 0.18 * np.sin(6 * np.pi * f * t) * np.exp(-t / 0.12)) * env
    k = int(SR * 0.015)
    x[:k] += 0.25 * vel * noise(k, lo=200, hi=2500, seed=int(f)) * np.linspace(1, 0, k)
    return _attack(x, 6.0)


def finger_bass(f, dur, vel):
    secs = min(1.6, dur + 0.15)
    n = int(SR * secs)
    t = _t(n)
    env = np.exp(-t / 0.7) * np.clip((secs - t) / 0.05, 0, 1)
    x = sum(a * np.sin(2 * np.pi * f * h * t) * np.exp(-t / d)
            for h, a, d in ((1, 1.0, 2.0), (2, 0.6 * vel, 0.35), (3, 0.3 * vel, 0.18), (4, 0.15 * vel, 0.1)))
    return _attack(x * env, 4.0)


# ---------------------------------------------------------------------------------------------------- keys

def electric_piano(f, dur, vel):
    secs = min(3.0, dur + 0.35)
    n = int(SR * secs)
    t = _t(n)
    index = (1.2 + 1.6 * vel) * np.exp(-t / 0.35)
    tone = np.sin(2 * np.pi * f * t + index * np.sin(2 * np.pi * f * t))
    bell = 0.12 * vel * np.sin(2 * np.pi * f * 14 * t) * np.exp(-t / 0.04) if f * 14 < SR * 0.45 else 0
    env = np.exp(-t / (1.6 * (261 / f) ** 0.3)) * np.clip((secs - t) / 0.25, 0, 1)
    return _attack((tone + bell) * env, 2.0)


def piano(f, dur, vel):
    secs = min(3.5, dur + 0.4)
    n = int(SR * secs)
    t = _t(n)
    x = np.zeros(n)
    B = 0.0003
    for h in range(1, 9):
        fh = f * h * math.sqrt(1 + B * h * h)
        if fh > SR * 0.45:
            break
        x += (1 / h ** (1.3 - 0.4 * vel)) * np.sin(2 * np.pi * fh * t) * np.exp(-t / (2.2 / h ** 0.7 * (261 / f) ** 0.3))
    env = np.clip((secs - t) / 0.3, 0, 1)
    k = int(SR * 0.01)
    x[:k] += 0.08 * noise(k, lo=800, hi=6000, seed=int(f)) * np.linspace(1, 0, k)
    return _attack(x * env, 1.5)


# ---------------------------------------------------------------------------------------------------- winds

def _wind(f, dur, vel, harmonics, breath=0.04, vib_depth=0.006, attack=0.04, release=0.09):
    secs = dur + release
    n = int(SR * secs)
    t = _t(n)
    vib = 1 + vib_depth * np.sin(2 * np.pi * 5.2 * t) * np.clip((t - 0.18) / 0.25, 0, 1)
    phase = 2 * np.pi * f * np.cumsum(vib) / SR
    x = sum(a * np.sin(h * phase) for h, a in harmonics)
    if breath:
        x = x + breath * noise(n, lo=800, hi=5000, seed=int(f * 11))
    env = np.clip(t / attack, 0, 1) * np.clip((secs - t) / release, 0, 1)
    return x * env


def flute(f, dur, vel):
    return _wind(f, dur, vel, ((1, 1.0), (2, 0.18), (3, 0.06)), breath=0.05)


def whistle(f, dur, vel):
    return _wind(f, dur, vel, ((1, 1.0), (2, 0.04)), breath=0.02, vib_depth=0.01, attack=0.025)


def ocarina(f, dur, vel):
    return _wind(f, dur, vel, ((1, 1.0), (3, 0.08)), breath=0.03, vib_depth=0.007, attack=0.03)


def soft_lead(f, dur, vel):
    return _wind(f, dur, vel, ((1, 1.0), (2, 0.35), (3, 0.2), (4, 0.1), (5, 0.05)), breath=0.0, vib_depth=0.004,
                 attack=0.015, release=0.06)


def warm_pad(f, dur, vel):
    secs = dur + 0.6
    n = int(SR * secs)
    t = _t(n)
    x = sum(np.sin(2 * np.pi * f * d * t) for d in (0.997, 1.0, 1.004)) / 3
    x += 0.3 * np.sin(4 * np.pi * f * t)
    env = np.clip(t / 0.35, 0, 1) * np.clip((secs - t) / 0.6, 0, 1)
    return x * env


# ---------------------------------------------------------------------------------------------------- drums

def _tone_drum(f0, f1, decay, n_s, slap=0.0, seed=0):
    n = int(SR * n_s)
    t = _t(n)
    freq = f1 + (f0 - f1) * np.exp(-t / 0.02)
    x = np.sin(2 * np.pi * np.cumsum(freq) / SR) * np.exp(-t / decay)
    if slap:
        k = int(SR * 0.01)
        x[:k] += slap * noise(k, lo=1000, hi=7000, seed=seed) * np.linspace(1, 0, k)
    return _attack(x, 0.5)


def _noise_hit(n_s, lo, hi, decay, attack_ms=0.5, seed=0):
    n = int(SR * n_s)
    t = _t(n)
    return _attack(noise(n, lo, hi, seed) * np.exp(-t / decay), attack_ms)


def _metal(n_s, freqs, decay, seed=0, shimmer=0.25):
    """Cymbal: mostly bright noise, plus a light detuned shimmer (sines, not squares: no whistling tone)."""
    n = int(SR * n_s)
    t = _t(n)
    rng = np.random.default_rng(seed)
    tone = sum(np.sin(2 * np.pi * fr * (1 + rng.uniform(-0.03, 0.03)) * t + rng.uniform(0, 6.3))
               * np.exp(-t / (decay * 0.3)) for fr in freqs) / len(freqs)
    x = (1 - shimmer) * noise(n, lo=4500, hi=13000, seed=seed) + shimmer * tone
    return x * np.exp(-t / decay)


def _add(*parts):
    """Sums sounds of different lengths (padded with silence)."""
    out = np.zeros(max(len(p) for p in parts))
    for p in parts:
        out[:len(p)] += p
    return out


def drum(note, vel, brushes=False):
    """General MIDI percussion (channel 10)."""
    if note in (35, 36):
        x = _tone_drum(130, 48, 0.22, 0.5)
        k = int(SR * 0.004)
        x[:k] += 0.3 * noise(k, lo=2000, seed=1)
        return x
    if note in (38, 40):
        if brushes:
            n = int(SR * 0.35)
            t = _t(n)
            return noise(n, lo=1200, hi=8000, seed=note) * (np.clip(t / 0.012, 0, 1) * np.exp(-t / 0.11)) * 0.7
        return _add(0.55 * _tone_drum(210, 180, 0.07, 0.3), 0.6 * _noise_hit(0.3, 1500, 9000, 0.11, seed=note))
    if note == 37:
        return _add(0.6 * _tone_drum(1700, 1600, 0.02, 0.08), 0.4 * _noise_hit(0.06, 2000, 9000, 0.015, seed=37))
    if note == 39:
        n = int(SR * 0.3)
        x = np.zeros(n)
        for i, off in enumerate((0.0, 0.011, 0.022)):
            k = int(off * SR)
            hit = _noise_hit(0.3 - off, 900, 3500, 0.012 if i < 2 else 0.09, seed=39 + i)
            x[k:k + len(hit)] += hit[:n - k]
        return x * 0.8
    if note in (42, 44):
        return 0.45 * _noise_hit(0.08, 7000, 15000, 0.018, seed=42)
    if note == 46:
        return 0.4 * _noise_hit(0.6, 6500, 15000, 0.22, seed=46)
    if note in (49, 57):
        return 0.4 * _metal(2.0, (420, 587, 763, 1190, 1630), 0.8, seed=49, shimmer=0.3)
    if note in (51, 59):
        return 0.22 * _metal(0.9, (2350, 3420, 5170), 0.32, seed=51, shimmer=0.15)
    if note == 54:
        return 0.4 * _noise_hit(0.2, 6000, 14000, 0.06, attack_ms=3, seed=54)
    if note == 56:
        n = int(SR * 0.3)
        t = _t(n)
        return 0.35 * (np.sign(np.sin(2 * np.pi * 545 * t)) + np.sign(np.sin(2 * np.pi * 815 * t))) * np.exp(-t / 0.07)
    if note in (60, 61):
        return _tone_drum(480 if note == 60 else 360, 400 if note == 60 else 300, 0.09, 0.3, slap=0.4, seed=note)
    if note in (62, 63, 64):
        f = {62: 360, 63: 330, 64: 250}[note]
        return _tone_drum(f * 1.1, f, 0.16 if note != 62 else 0.05, 0.5, slap=0.6 if note == 62 else 0.25, seed=note)
    if note in (69, 70, 82):
        n = int(SR * 0.12)
        t = _t(n)
        return 0.45 * noise(n, lo=4500, hi=11000, seed=note) * np.clip(t / 0.012, 0, 1) * np.exp(-t / 0.035)
    if note == 75:
        return 0.6 * _tone_drum(2500, 2500, 0.035, 0.12)
    if note in (76, 77):
        f = 1150 if note == 76 else 800
        n = int(SR * 0.2)
        t = _t(n)
        return 0.7 * (np.sin(2 * np.pi * f * t) + 0.3 * np.sin(2 * np.pi * f * 2.7 * t)) * np.exp(-t / 0.045)
    if note in (80, 81):
        n = int(SR * (0.15 if note == 80 else 1.8))
        t = _t(n)
        d = 0.05 if note == 80 else 0.7
        return 0.25 * sum(np.sin(2 * np.pi * fr * t) for fr in (4200, 6400, 9100)) * np.exp(-t / d)
    return 0.3 * _noise_hit(0.1, 3000, 9000, 0.03, seed=note)     # anything else: a soft tick


# ---------------------------------------------------------------------------------------------------- GM map

def instrument_for(program):
    """General MIDI program (0-based, 0-127) -> (function, level)."""
    table = {
        piano: (0, 1, 2, 3, 6, 7),
        electric_piano: (4, 5, 16, 17, 18, 19, 20, 21, 22, 23),
        celesta: (8,),
        glockenspiel: (9, 10, 14, 112),          # glockenspiel, music box, tubular bells, tinkle bell
        vibraphone: (11,),
        marimba: (12, 108),                      # marimba, kalimba
        xylophone: (13,),
        harp: (15, 46),                          # dulcimer, orchestral harp
        nylon_guitar: (24, 25, 26, 27, 28, 29, 30, 31, 105, 106, 107),
        upright_bass: (32, 35),
        finger_bass: (33, 34, 36, 37, 38, 39),
        pizzicato: (45, 104),
        warm_pad: (40, 41, 42, 43, 44, 47, 48, 49, 50, 51, 52, 53, 54) + tuple(range(88, 96)),
        flute: (72, 73, 74, 75, 77),
        whistle: (76, 78),
        ocarina: (79,),
        soft_lead: tuple(range(80, 88)),
        steel_drum: (113, 114),
    }
    levels = {piano: 0.55, electric_piano: 0.5, celesta: 0.45, glockenspiel: 0.33, vibraphone: 0.5, marimba: 0.65,
              xylophone: 0.5, harp: 0.5, nylon_guitar: 0.5, upright_bass: 0.8, finger_bass: 0.75, pizzicato: 0.55,
              warm_pad: 0.3, flute: 0.45, whistle: 0.4, ocarina: 0.45, soft_lead: 0.3, steel_drum: 0.5}
    for fn, programs in table.items():
        if program in programs:
            return fn, levels[fn]
    return electric_piano, 0.45


# ---------------------------------------------------------------------------------------------------- reverb

@lru_cache(maxsize=4)
def _impulse(seconds=1.6, predelay=0.018, seed=3):
    n = int(SR * seconds)
    t = _t(n)
    irs = []
    for ch in range(2):
        x = noise(n, lo=120, hi=6500, seed=seed + ch) * np.exp(-t * 6.9 / seconds)
        x *= 1 - np.exp(-t / 0.01)
        x = np.concatenate([np.zeros(int(SR * predelay)), x])
        irs.append(x / np.sqrt(np.sum(x ** 2)))
    return tuple(irs)


def reverb(mono, seconds=1.6):
    """Stereo hall from a mono send (FFT convolution)."""
    out = []
    for ir in _impulse(seconds):
        n = len(mono) + len(ir) - 1
        size = 1 << (n - 1).bit_length()
        y = np.fft.irfft(np.fft.rfft(mono, size) * np.fft.rfft(ir, size), size)[:len(mono)]
        out.append(y)
    return np.stack(out)
