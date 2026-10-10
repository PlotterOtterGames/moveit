"""Render music/midi/*.mid to OGG files the game plays (unity/MoveItGames/Assets/Resources/Music/<name>.ogg).

    python tools/music/render.py                 # every .mid in music/midi
    python tools/music/render.py runner menu     # some of them
    python tools/music/render.py --wav           # also keep a .wav next to each .mid (for listening/checking)

Loops: the music loops at the "loop_end" marker (or the end of the last bar). Reverb and ringing notes that run
past the end are wrapped around to the start, so the loop is seamless. Files named jingle_* play once (no wrap).
Needs ffmpeg on the PATH for OGG (falls back to WAV without it).
"""

import argparse
import math
import os
import shutil
import subprocess
import sys
import tempfile
import wave

import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import midi  # noqa: E402
import synth  # noqa: E402
from synth import SR  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MIDI_DIR = os.path.join(ROOT, "music", "midi")
OUT_DIR = os.path.join(ROOT, "unity", "MoveItGames", "Assets", "Resources", "Music")
TAIL = 3.0


def render(path):
    song = midi.read(path)
    name = os.path.splitext(os.path.basename(path))[0]
    looping = not name.startswith("jingle_")
    loop_n = int(round(song.loop_end * SR))
    last_note = max([n.end for n in song.notes] + [0])
    total = (loop_n if looping else int(last_note * SR)) + int(TAIL * SR)
    left, right, send = np.zeros(total), np.zeros(total), np.zeros(total)
    cache = {}
    brushes = song.programs.get(9) == 40
    for note in song.notes:
        ch, vel = note.channel, note.velocity / 127
        if ch == 9:
            key = ("drum", note.pitch, brushes)
            if key not in cache:
                cache[key] = synth.drum(note.pitch, vel, brushes)
            level = 0.8
        else:
            fn, level = synth.instrument_for(song.programs.get(ch, 0))
            dur = max(0.03, note.end - note.start)
            vq = round(vel * 5) / 5
            key = (fn.__name__, note.pitch, round(dur * 20) / 20, vq)
            if key not in cache:
                cache[key] = fn(synth.mtof(note.pitch), round(dur * 20) / 20, max(0.2, vq))
        snd = cache[key]
        gain = level * vel ** 1.3 * (song.volume.get(ch, 100) / 100) ** 2
        angle = song.pan.get(ch, 64) / 127 * math.pi / 2
        start = int(note.start * SR)
        end = min(total, start + len(snd))
        if end <= start:
            continue
        part = snd[:end - start] * gain
        left[start:end] += part * math.cos(angle)
        right[start:end] += part * math.sin(angle)
        send[start:end] += part * (song.reverb.get(ch, 40) / 127)
    wet = synth.reverb(send)
    out = np.stack([left, right]) + 0.55 * wet
    if looping:
        out[:, :total - loop_n] += out[:, loop_n:]     # wrap the tail into the start: seamless loop
        out = out[:, :loop_n]
    else:
        k = int(0.3 * SR)
        out[:, -k:] *= np.linspace(1, 0, k)
    # master: loudness to a common level, then a soft limiter
    rms = np.sqrt(np.mean(out ** 2)) + 1e-9
    out *= 0.16 / rms
    out = np.tanh(out * 0.9) / np.tanh(0.9)
    out *= 0.95 / max(1.0, np.max(np.abs(out)))
    return name, out, song


def write_wav(path, stereo):
    data = (np.clip(stereo.T, -1, 1) * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())


def encode(stereo, out_path, keep_wav=None):
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    ffmpeg = shutil.which("ffmpeg")
    if keep_wav:
        write_wav(keep_wav, stereo)
    if not ffmpeg:
        out_path = os.path.splitext(out_path)[0] + ".wav"
        write_wav(out_path, stereo)
        return out_path
    with tempfile.TemporaryDirectory() as tmp:
        wav = os.path.join(tmp, "x.wav")
        write_wav(wav, stereo)
        subprocess.run([ffmpeg, "-y", "-loglevel", "error", "-i", wav, "-c:a", "libvorbis", "-q:a", "5", out_path],
                       check=True)
    return out_path


def main():
    ap = argparse.ArgumentParser(description="Render Tundu's MIDI music to OGG for the game")
    ap.add_argument("songs", nargs="*", help="names in music/midi (default: all)")
    ap.add_argument("--wav", action="store_true", help="also write music/midi/<name>.wav to listen to")
    args = ap.parse_args()
    names = args.songs or sorted(os.path.splitext(f)[0] for f in os.listdir(MIDI_DIR) if f.lower().endswith(".mid"))
    for name in names:
        name_, stereo, song = render(os.path.join(MIDI_DIR, f"{name}.mid"))
        out = encode(stereo, os.path.join(OUT_DIR, f"{name}.ogg"),
                     keep_wav=os.path.join(MIDI_DIR, f"{name}.wav") if args.wav else None)
        print(f"  {name:16s} {stereo.shape[1] / SR:5.1f} s  {song.bpm:5.1f} BPM  {len(song.notes):4d} notes"
              f"  -> {os.path.relpath(out, ROOT)} ({os.path.getsize(out) // 1024} KB)")


if __name__ == "__main__":
    main()
