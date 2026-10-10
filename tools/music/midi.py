"""Minimal Standard MIDI File (SMF) writer and reader: no packages needed.

Writes format-1 files any music program opens (MuseScore, LMMS, Reaper, FL Studio...). Reads format 0/1 files back
for rendering: tempo changes, notes, program changes, volume/pan/reverb CCs and a "loop_end" marker.
"""

import struct
from dataclasses import dataclass, field

PPQ = 480  # ticks per quarter note


# ---------------------------------------------------------------------------------------------------- writing

@dataclass
class Track:
    name: str
    channel: int = 0
    program: int = 0           # General MIDI program 0-127 (ignored on the drum channel 9 unless kit)
    volume: int = 100          # CC7
    pan: int = 64              # CC10: 0 left .. 127 right
    reverb: int = 40           # CC91 send
    events: list = field(default_factory=list)   # (tick, priority, bytes)

    def note(self, tick, length, pitch, velocity=90):
        tick, length = max(0, int(round(tick))), max(1, int(round(length)))
        self.events.append((tick, 1, bytes([0x90 | self.channel, pitch, max(1, min(127, velocity))])))
        self.events.append((tick + length, 0, bytes([0x80 | self.channel, pitch, 0])))


def _vlq(n):
    out = [n & 0x7F]
    n >>= 7
    while n:
        out.append((n & 0x7F) | 0x80)
        n >>= 7
    return bytes(reversed(out))


def _meta(kind, data):
    return bytes([0xFF, kind]) + _vlq(len(data)) + data


def write(path, tracks, bpm, loop_end_tick=None, numerator=4, title=""):
    chunks = []
    # conductor track: tempo, time signature, loop marker
    conductor = [(0, 0, _meta(0x03, title.encode("utf-8"))),
                 (0, 0, _meta(0x51, struct.pack(">I", int(round(60_000_000 / bpm)))[1:])),
                 (0, 0, _meta(0x58, bytes([numerator, 2, 24, 8])))]
    if loop_end_tick is not None:
        conductor.append((int(loop_end_tick), 0, _meta(0x06, b"loop_end")))
    chunks.append(conductor)
    for t in tracks:
        ev = [(0, -3, _meta(0x03, t.name.encode("utf-8"))),
              (0, -2, bytes([0xC0 | t.channel, t.program])),
              (0, -1, bytes([0xB0 | t.channel, 7, t.volume])),
              (0, -1, bytes([0xB0 | t.channel, 10, t.pan])),
              (0, -1, bytes([0xB0 | t.channel, 91, t.reverb]))]
        chunks.append(ev + t.events)
    with open(path, "wb") as f:
        f.write(b"MThd" + struct.pack(">IHHH", 6, 1, len(chunks), PPQ))
        for events in chunks:
            events = sorted(events, key=lambda e: (e[0], e[1]))
            data, last = bytearray(), 0
            for tick, _, msg in events:
                tick = max(tick, last)
                data += _vlq(tick - last) + msg
                last = tick
            data += _vlq(0) + _meta(0x2F, b"")
            f.write(b"MTrk" + struct.pack(">I", len(data)) + data)


# ---------------------------------------------------------------------------------------------------- reading

@dataclass
class Note:
    start: float      # seconds
    end: float
    channel: int
    pitch: int
    velocity: int


@dataclass
class Song:
    notes: list
    programs: dict    # channel -> program (last seen)
    volume: dict      # channel -> 0..127
    pan: dict
    reverb: dict
    names: dict       # channel -> track name
    bpm: float
    loop_end: float   # seconds
    bar_seconds: float


def read(path):
    with open(path, "rb") as f:
        data = f.read()
    if data[:4] != b"MThd":
        raise ValueError(f"{path} is not a MIDI file")
    _, fmt, ntracks, division = struct.unpack(">IHHH", data[4:14])
    pos = 14
    raw = []        # (tick, kind, payload, track_index)
    track_names = {}
    for ti in range(ntracks):
        assert data[pos:pos + 4] == b"MTrk", "bad track chunk"
        length = struct.unpack(">I", data[pos + 4:pos + 8])[0]
        p, end, tick, status = pos + 8, pos + 8 + length, 0, 0
        while p < end:
            delta, p = _read_vlq(data, p)
            tick += delta
            b = data[p]
            if b == 0xFF:
                kind = data[p + 1]
                n, q = _read_vlq(data, p + 2)
                raw.append((tick, "meta", (kind, data[q:q + n]), ti))
                if kind == 0x03:
                    track_names[ti] = data[q:q + n].decode("utf-8", "replace")
                p = q + n
                continue
            if b in (0xF0, 0xF7):
                n, q = _read_vlq(data, p + 1)
                p = q + n
                continue
            if b & 0x80:
                status, p = b, p + 1
            kind = status & 0xF0
            size = 1 if kind in (0xC0, 0xD0) else 2
            raw.append((tick, "midi", (status, data[p:p + size]), ti))
            p += size
        pos = end

    raw.sort(key=lambda e: e[0])
    # tempo map: tick -> seconds
    changes = {}
    for tick, kind, payload, _ in raw:
        if kind == "meta" and payload[0] == 0x51:
            changes[tick] = int.from_bytes(payload[1], "big")      # the last change at a tick wins
    changes.setdefault(0, 500000)                                   # MIDI default: 120 BPM
    tempos = sorted(changes.items())

    def seconds(tick):
        s, last_tick, us = 0.0, 0, tempos[0][1]
        for t, u in tempos[1:]:
            if t >= tick:
                break
            s += (t - last_tick) * us / 1e6 / division
            last_tick, us = t, u
        return s + (tick - last_tick) * us / 1e6 / division

    programs, volume, pan, reverb, names = {}, {}, {}, {}, {}
    open_notes, notes = {}, []
    loop_end_tick, last_tick, numerator = None, 0, 4
    for tick, kind, payload, ti in raw:
        last_tick = max(last_tick, tick)
        if kind == "meta":
            if payload[0] == 0x06 and payload[1].strip().lower() in (b"loop_end", b"loopend", b"loop"):
                loop_end_tick = tick
            if payload[0] == 0x58:
                numerator = payload[1][0]
            continue
        status, d = payload
        ch, k = status & 0x0F, status & 0xF0
        names.setdefault(ch, track_names.get(ti, f"channel {ch + 1}"))
        if k == 0xC0:
            programs[ch] = d[0]
        elif k == 0xB0:
            {7: volume, 10: pan, 91: reverb}.get(d[0], {})[ch] = d[1]
        elif k == 0x90 and d[1] > 0:
            open_notes.setdefault((ch, d[0]), []).append((tick, d[1]))
        elif k in (0x80, 0x90):
            stack = open_notes.get((ch, d[0]))
            if stack:
                start, vel = stack.pop(0)
                notes.append(Note(seconds(start), seconds(tick), ch, d[0], vel))
    bpm = 60_000_000 / tempos[0][1]
    bar_ticks = division * numerator
    if loop_end_tick is None:   # round the end of the music up to a whole bar
        loop_end_tick = -(-max([0] + [_tick_of(n.end, seconds, last_tick) for n in notes]) // bar_ticks) * bar_ticks
    return Song(sorted(notes, key=lambda n: n.start), programs, volume, pan, reverb, names, bpm,
                seconds(loop_end_tick), seconds(bar_ticks))


def _tick_of(sec, seconds, last_tick):
    lo, hi = 0, max(1, last_tick)
    while seconds(hi) < sec:
        hi *= 2
    while lo < hi:
        mid = (lo + hi) // 2
        if seconds(mid) < sec - 1e-9:
            lo = mid + 1
        else:
            hi = mid
    return lo


def _read_vlq(data, p):
    n = 0
    while True:
        b = data[p]
        p += 1
        n = (n << 7) | (b & 0x7F)
        if not b & 0x80:
            return n, p
