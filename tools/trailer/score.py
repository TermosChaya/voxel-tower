#!/usr/bin/env python3
"""
The trailer's score, made here out of sines, saws and noise, nothing recorded and nothing downloaded: a drone and a
lone horn over the valley at dusk; a pulse, plucked strings and a rising choir while the fort goes up; war drums and
low brass as the horde comes; the whole of it at the assault, with a great hit for every blast and fall; a dark choir
over the spells; a heartbeat through the swordsman's eyes; and a last hit and a held chord under the title, dying away.

It is written to the cut: tools/trailer.command passes when each section begins and where the hits fall, so the
music turns where the pictures do.

    python3 score.py out.wav --length 114 --cue 0:intro --cue 8:build ... --hit 52.3 --hit 60.1 ...

Needs numpy and scipy.
"""
import argparse
import math
import wave

import numpy as np
from scipy import signal

SR = 44100
rng = np.random.default_rng(7)

NOTES = {'C': 0, 'C#': 1, 'Db': 1, 'D': 2, 'D#': 3, 'Eb': 3, 'E': 4, 'F': 5, 'F#': 6, 'Gb': 6, 'G': 7, 'G#': 8, 'Ab': 8,
         'A': 9, 'A#': 10, 'Bb': 10, 'B': 11}


def hz(name):
    """The pitch of a note written as D2, Bb3, F#4."""
    i = 2 if len(name) > 2 and name[1] in '#b' else 1
    return 440.0 * 2 ** ((NOTES[name[:i]] + 12 * (int(name[i:]) + 1) - 69) / 12)


# the chords of D minor the sections go through: i, VI, III, VII, iv, V
CHORDS = {
    'Dm': ['D3', 'F3', 'A3', 'D4'], 'Bb': ['Bb2', 'D3', 'F3', 'Bb3'], 'F': ['F2', 'A2', 'C3', 'F3'],
    'C': ['C3', 'E3', 'G3', 'C4'], 'Gm': ['G2', 'Bb2', 'D3', 'G3'], 'A': ['A2', 'C#3', 'E3', 'A3'],
    'Eb': ['Eb3', 'G3', 'Bb3', 'Eb4'],
}
ROOT = {'Dm': 'D2', 'Bb': 'Bb1', 'F': 'F2', 'C': 'C2', 'Gm': 'G1', 'A': 'A1', 'Eb': 'Eb2'}


class Mix:
    """The buses the parts are laid on; each gets its own room, then all are summed."""

    def __init__(self, seconds):
        self.n = int((seconds + 6) * SR)
        self.length = seconds
        self.bus = {}

    def add(self, bus, at, sound, gain=1.0):
        if at < 0:
            sound = sound[:, int(-at * SR):]
            at = 0
        b = self.bus.setdefault(bus, np.zeros((2, self.n), np.float32))
        i = int(at * SR)
        if i >= self.n:
            return
        m = min(sound.shape[1], self.n - i)
        b[:, i:i + m] += sound[:, :m] * gain


# ------------------------------------------------------------------ the instruments

def envelope(n, attack, release, hold=None):
    """Rises over the attack, holds, and dies away over the release at the end of n samples."""
    t = np.arange(n) / SR
    e = np.minimum(1.0, t / max(attack, 1e-3))
    end = n / SR
    e *= np.clip((end - t) / max(release, 1e-3), 0, 1)
    return e.astype(np.float32)


def pan(mono, p):
    """Stereo from mono, p from -1 (left) to 1 (right), the power kept."""
    a = (p + 1) * math.pi / 4
    return np.stack([mono * math.cos(a), mono * math.sin(a)])


def additive(f, n, amps, phase_mod=None, seed=0):
    """Harmonics of f with these amplitudes (a list, or a function of the harmonic's number and frequency)."""
    t = np.arange(n, dtype=np.float64) / SR
    out = np.zeros(n, np.float64)
    r = np.random.default_rng(seed)
    k = 1
    while k * f < 9000:
        a = amps(k, k * f) if callable(amps) else (amps[k - 1] if k <= len(amps) else 0)
        if isinstance(a, np.ndarray) or a > 1e-4:
            ph = 2 * np.pi * k * f * t + r.uniform(0, 2 * np.pi)
            if phase_mod is not None:
                ph = ph + k * phase_mod
            out += a * np.sin(ph)
        k += 1
        if k > 64:
            break
    return out.astype(np.float32)


def saw_pad(f, dur, bright=1800, voices=3, cents=9, attack=1.0, release=1.5, width=.7):
    """A bowed or synthetic pad: detuned saws, darkened."""
    n = int((dur + release) * SR)
    out = np.zeros((2, n), np.float32)
    for v in range(voices):
        d = (v - (voices - 1) / 2) / max(1, (voices - 1) / 2)
        fv = f * 2 ** (d * cents / 1200)
        s = additive(fv, n, lambda k, fk: 1 / k * math.exp(-fk / bright), seed=v + int(f))
        out += pan(s, d * width)
    return out * envelope(n, attack, release) / voices


def choir(notes, dur, vowel='ah', attack=1.4, release=2.0, voices=3):
    """Voices singing a vowel: sung saws shaped by the vowel's formants, a slow vibrato each, spread across."""
    form = {'ah': [(700, 110, 1.0), (1150, 120, .55), (2700, 170, .25)],
            'oh': [(450, 90, 1.0), (800, 100, .6), (2600, 160, .15)],
            'oo': [(330, 80, 1.0), (700, 100, .3), (2400, 160, .08)]}[vowel]
    n = int((dur + release) * SR)
    t = np.arange(n) / SR
    out = np.zeros((2, n), np.float32)
    for j, nm in enumerate(notes):
        f = hz(nm) if isinstance(nm, str) else nm
        for v in range(voices):
            d = (v - (voices - 1) / 2) / max(1, (voices - 1) / 2)
            fv = f * 2 ** (d * 11 / 1200)
            rate = 4.6 + rng.uniform(-.5, .5)
            depth = .0045 * np.minimum(1, t / 1.5)
            pm = (2 * np.pi * fv * depth / (2 * np.pi * rate) * np.sin(2 * np.pi * rate * t + rng.uniform(0, 6))).astype(np.float64)

            def amp(k, fk):
                g = sum(a / (1 + ((fk - F) / bw) ** 2) for F, bw, a in form) + .015
                return g / math.sqrt(k)
            s = additive(fv, n, amp, phase_mod=pm, seed=j * 7 + v)
            out += pan(s, d * .8 + (j - len(notes) / 2) * .1)
    return out * envelope(n, attack, release) / (voices * len(notes)) * 2.2


def horn(f, dur, loud=1.0, attack=.18, release=.6):
    """A brass horn: brighter as it is blown harder, a scoop up into the note, a vibrato as it holds."""
    n = int((dur + release) * SR)
    t = np.arange(n) / SR
    env = envelope(n, attack, release)
    bright = 350 + 2300 * loud * np.minimum(1, t / (attack * 1.5)) * (1 - .25 * np.minimum(1, t / 2))
    scoop = -.04 * np.exp(-t / .06)
    vib = .004 * np.minimum(1, np.maximum(0, t - .4)) * np.sin(2 * np.pi * 5.2 * t)
    pm = np.cumsum(2 * np.pi * f * (scoop + vib)) / SR
    out = np.zeros(n, np.float64)
    for k in range(1, 30):
        if k * f > 7000:
            break
        a = np.exp(-k * f / bright) / k ** .7
        out += a * np.sin(2 * np.pi * k * f * t + k * pm)
    s = (out * env).astype(np.float32)
    return np.stack([s * .9, s * .8]) * .5


def pluck(f, dur=.7, bright=1.0):
    """A plucked string: bright at the pluck, the high harmonics dying first."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    out = np.zeros(n, np.float64)
    for k in range(1, 28):
        if k * f > 8000:
            break
        out += np.exp(-t * (2.5 + k * 1.1 / bright)) / k * np.sin(2 * np.pi * k * f * t + rng.uniform(0, 6))
    out *= np.minimum(1, t / .002)
    return out.astype(np.float32)


def staccato(f, dur=.18):
    """A short bowed note, dug in."""
    n = int((dur + .12) * SR)
    s = additive(f, n, lambda k, fk: 1 / k * math.exp(-fk / 2600), seed=int(f * 3))
    return s * envelope(n, .012, .1)


def noise(n, lo=None, hi=None):
    """White noise, band-limited if asked."""
    x = rng.standard_normal(n).astype(np.float32)
    if lo or hi:
        if lo and hi:
            b, a = signal.butter(2, [lo / (SR / 2), hi / (SR / 2)], 'band')
        elif hi:
            b, a = signal.butter(2, hi / (SR / 2), 'low')
        else:
            b, a = signal.butter(2, lo / (SR / 2), 'high')
        x = signal.lfilter(b, a, x).astype(np.float32)
    return x


def taiko(vel=1.0, pitch=1.0, length=1.3):
    """A great drum: a low skin falling in pitch, the stick's crack on top."""
    n = int(length * SR)
    t = np.arange(n) / SR
    f = (46 + 120 * np.exp(-t / .035)) * pitch
    body = np.sin(np.cumsum(2 * np.pi * f) / SR) * np.exp(-t / .42)
    skin = noise(n, hi=1800) * np.exp(-t / .025) * .9
    thump = noise(n, hi=180) * np.exp(-t / .09) * 1.4
    s = ((body + skin + thump) * vel).astype(np.float32)
    return np.stack([s, s])


def clack(vel=1.0):
    """A rim or a frame drum: a short dry knock."""
    n = int(.15 * SR)
    t = np.arange(n) / SR
    s = (noise(n, 900, 5000) * np.exp(-t / .018) * vel).astype(np.float32)
    return np.stack([s * .9, s])


def boom(size=1.0):
    """A great hit: a sub falling away, a rumble, and a crack."""
    n = int(4.5 * SR)
    t = np.arange(n) / SR
    f = 30 + 55 * np.exp(-t / .12)
    sub = np.sin(np.cumsum(2 * np.pi * f) / SR) * np.exp(-t / 1.1) * 1.4
    rumble = noise(n, hi=220) * np.exp(-t / .9) * 1.6
    crack = noise(n, hi=7000) * np.exp(-t / .03) * .9
    s = ((sub + rumble + crack) * size).astype(np.float32)
    return np.stack([s, s * .97])


def riser(dur, top=6000):
    """Noise and a tone climbing toward a hit."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    k = t / dur
    # the noise opened up from a low hiss to a bright one, in short pieces
    x = rng.standard_normal(n).astype(np.float32)
    out = np.zeros(n, np.float32)
    step = 2048
    for i in range(0, n, step):
        cut = 150 + (top - 150) * (i / n) ** 2
        b, a = signal.butter(2, min(cut, SR / 2 - 100) / (SR / 2), 'low')
        out[i:i + step] = signal.lfilter(b, a, x[i:i + step])
    tone = np.sin(np.cumsum(2 * np.pi * (110 * 2 ** (2 * k))) / SR) * .25
    s = ((out * .9 + tone) * k ** 2.2).astype(np.float32)
    return np.stack([s, np.roll(s, 90)])


def heartbeat(vel=1.0):
    """Lub-dub: two soft low thumps."""
    n = int(.9 * SR)
    t = np.arange(n) / SR
    s = np.zeros(n, np.float32)
    for at, v in ((0, 1.0), (.2, .7)):
        tt = np.maximum(0, t - at)
        on = t >= at
        s += (np.sin(2 * np.pi * (38 + 40 * np.exp(-tt / .03)) * tt) * np.exp(-tt / .12) * v * on).astype(np.float32)
    s *= vel
    return np.stack([s, s])


# ------------------------------------------------------------------ the sections

def intro(m, t0, t1):
    """Dusk over the valley: a low drone, a choir far off, one horn call."""
    d = t1 - t0
    m.add('pad', t0, saw_pad(hz('D1'), d, bright=260, attack=3, release=2.5), .55)
    m.add('pad', t0, saw_pad(hz('A1'), d, bright=300, attack=4, release=2.5), .3)
    m.add('choir', t0 + 1.2, choir(['D3', 'A3', 'D4'], d - 1.2, 'oh', attack=2.5, release=2.5), .55)
    for at, nm, dur in ((3.2, 'D3', 1.1), (4.4, 'A3', .6), (5.1, 'D4', 2.2)):
        if t0 + at < t1:
            m.add('brass', t0 + at, horn(hz(nm), dur, loud=.55), .5)
    m.add('drums', t1 - .05, taiko(.5, .9), .7)


def build(m, t0, t1):
    """The fort going up: a pulse, plucked strings walking the chords, the bass, the strings and the choir coming in."""
    bpm = 96
    beat = 60 / bpm
    bar = 4 * beat
    prog = ['Dm', 'Bb', 'F', 'C']
    d = t1 - t0
    nbars = int(d / bar) + 1
    for b in range(nbars):
        bt = t0 + b * bar
        if bt >= t1:
            break
        ch = prog[(b // 2) % 4]
        k = min(1, (bt - t0) / d)
        # the pulse: the drum on one and three, a knock on the off-beats once it is going
        for i, v in ((0, .55), (2, .4)):
            if bt + i * beat < t1:
                m.add('drums', bt + i * beat, taiko(v * (.6 + .5 * k), 1.05), .55)
        if b >= 2:
            for i in range(8):
                if i % 2 == 1 and bt + i * beat / 2 < t1:
                    m.add('drums', bt + i * beat / 2, clack(.25 + .3 * k), .5)
        # the plucked arpeggio, up and down the chord in eighths
        tones = CHORDS[ch] + [CHORDS[ch][1].replace('3', '4').replace('2', '3')]
        order = [0, 2, 1, 3, 2, 4, 3, 2]
        for i, j in enumerate(order):
            at = bt + i * beat / 2
            if at < t1:
                nm = tones[j % len(tones)]
                m.add('strings', at, pan(pluck(hz(nm) * 2, .9, bright=.6 + k), (i % 2 - .5) * .6), .22 + .12 * k)
        if b % 2 == 0:
            span = min(2 * bar, t1 - bt)
            if bt - t0 > 5:
                m.add('pad', bt, saw_pad(hz(ROOT[ch]), span, bright=420, attack=.3, release=.8), .55)
            if bt - t0 > 9:
                for nm in CHORDS[ch][1:]:
                    m.add('strings', bt, saw_pad(hz(nm), span, bright=1500, attack=.8, release=1.0, voices=2), .16 + .12 * k)
            if bt - t0 > 13.5:
                m.add('choir', bt, choir(CHORDS[ch][:3], span, 'ah', attack=1.0, release=1.4), .55 + .3 * k)
    m.add('fx', t1 - 3.2, riser(3.2), .55)
    m.add('hits', t1 - .02, boom(1.1), .9)


def dread(m, t0, t1):
    """The horde coming: the war horn, low brass held, war drums gathering speed, the choir rising."""
    d = t1 - t0
    m.add('brass', t0 + .6, horn(hz('A2'), 1.2, loud=.9), .9)
    m.add('brass', t0 + 1.9, horn(hz('D3'), 3.0, loud=1.0), .9)
    m.add('pad', t0, saw_pad(hz('D1'), d * .5, bright=380, attack=.2, release=.6), .7)
    m.add('pad', t0 + d * .5, saw_pad(hz('Bb0'), d * .5, bright=380, attack=.2, release=.8), .7)
    m.add('brass', t0 + d * .5, horn(hz('F2'), d * .5 - .3, loud=.7, attack=.5), .6)
    m.add('choir', t0 + 2.5, choir(['D3', 'F3', 'A3'], d - 2.5, 'ah', attack=3, release=1.2), .9)
    # the drums: slow, then faster and harder toward the cut
    at = t0 + .3
    gap = 1.1
    while at < t1 - .1:
        k = (at - t0) / d
        m.add('drums', at, taiko(.55 + .5 * k, .95), .8)
        if k > .5:
            m.add('drums', at + gap / 2, taiko(.3 + .4 * k, 1.15), .6)
        at += gap
        gap = max(.32, gap * .9)
    m.add('fx', t1 - 2.4, riser(2.4), .6)


def battle(m, t0, t1):
    """The assault, and on through the blasts: drums, strings driving, brass on the changes, the choir over all."""
    bpm = 100
    beat = 60 / bpm
    bar = 4 * beat
    prog = ['Dm', 'Bb', 'Gm', 'A']
    pattern = [(0, 1.0), (1.5, .6), (2, .85), (3, .7), (3.5, .6)]
    d = t1 - t0
    b = 0
    while t0 + b * bar < t1:
        bt = t0 + b * bar
        ch = prog[b % 4]
        span = min(bar, t1 - bt)
        k = min(1, (bt - t0) / max(1, d))
        for pos, v in pattern:
            if bt + pos * beat < t1:
                m.add('drums', bt + pos * beat, taiko(v, .95 + .1 * (pos % 2)), .8)
        for i in range(8):
            if bt + i * beat / 2 < t1:
                m.add('drums', bt + i * beat / 2, clack(.35 + .2 * (i % 2 == 0)), .45)
        # the strings: eighths on the root, a figure on the fifth
        root = hz(ROOT[ch]) * 2
        for i, mul in enumerate([1, 1, 1.5, 1, 2, 1, 1.5, 1]):
            if bt + i * beat / 2 < t1:
                m.add('strings', bt + i * beat / 2, pan(staccato(root * mul), (i % 2 - .5) * .5), .32)
        m.add('pad', bt, saw_pad(hz(ROOT[ch]), span, bright=500, attack=.05, release=.4), .55)
        # brass on the change, held
        for nm in CHORDS[ch][:3]:
            m.add('brass', bt, horn(hz(nm), span - .15, loud=.8 + .2 * k, attack=.08), .3)
        m.add('choir', bt, choir(CHORDS[ch][1:], span, 'ah', attack=.4, release=.8), .7)
        b += 1


def magic(m, t0, t1):
    """The spells: the drums fall away to a slow beat, a dark choir, strings trembling, rising toward the falling star."""
    d = t1 - t0
    chords = [['D3', 'A3', 'D4'], ['Eb3', 'Bb3', 'Eb4'], ['D3', 'A3', 'F4'], ['C3', 'G3', 'Eb4'], ['D3', 'A3', 'D4', 'F4']]
    seg = d / len(chords)
    for i, ch in enumerate(chords):
        m.add('choir', t0 + i * seg, choir(ch, seg + .4, 'ah', attack=.8, release=1.2), 1.0 + .15 * i)
        m.add('pad', t0 + i * seg, saw_pad(hz(ch[0]) / 4, seg, bright=300, attack=.5, release=1), .7)
        # strings trembling on the fifth, high
        n = int((seg + .5) * SR)
        tt = np.arange(n) / SR
        trem = (.55 + .45 * np.sin(2 * np.pi * 11 * tt)).astype(np.float32)
        s = saw_pad(hz(ch[1]) * 2, seg, bright=2200, attack=.6, release=.5, voices=2)
        m.add('strings', t0 + i * seg, s[:, :n] * trem[:s.shape[1]], .22)
    at = t0
    while at < t1:
        m.add('drums', at, taiko(.8, .85), .7)
        at += 60 / 100 * 2


def pulse(m, t0, t1):
    """Through his eyes: a heartbeat quickening, strings in sixteenths, the drums coming down hard at the end."""
    d = t1 - t0
    at = t0
    gap = .85
    while at < t1 - .2:
        m.add('drums', at, heartbeat(1.0), 1.0)
        at += gap
        gap = max(.42, gap * .94)
    beat = 60 / 124 / 4
    i = 0
    while t0 + 2.5 + i * beat < t1 - .3:
        at = t0 + 2.5 + i * beat
        k = (at - t0) / d
        nm = ['D3', 'D3', 'A3', 'D3', 'F3', 'D3', 'A3', 'C4'][i % 8]
        m.add('strings', at, pan(staccato(hz(nm), .1), ((i % 2) - .5) * .7), .2 + .15 * k)
        i += 1
    m.add('pad', t0, saw_pad(hz('D1'), d, bright=300, attack=1, release=.5), .6)
    for j, at in enumerate(np.arange(t0 + 2.6, t1 - .4, 60 / 124 * 2)):
        m.add('drums', at, taiko(.5 + .4 * (at - t0) / d, 1.0), .7)
    m.add('fx', t1 - 2.2, riser(2.2), .55)


def title(m, t0, t1, reveal):
    """The name: near silence, then one great hit and the whole choir and brass on D, dying away; a last horn."""
    m.add('choir', t0, choir(['D3', 'A3'], reveal - t0, 'oo', attack=1.5, release=.6), .35)
    m.add('hits', reveal, boom(1.4), 1.0)
    m.add('drums', reveal, taiko(1.2, .8), .9)
    d = t1 - reveal
    m.add('choir', reveal, choir(['D3', 'F3', 'A3', 'D4'], d - 1.5, 'ah', attack=.15, release=3.5), 1.1)
    m.add('pad', reveal, saw_pad(hz('D1'), d - 1.5, bright=500, attack=.05, release=3.5), .8)
    for nm in ('D2', 'A2', 'D3', 'F3'):
        m.add('brass', reveal, horn(hz(nm), 2.6, loud=1.0, attack=.06, release=2.0), .35)
    m.add('brass', reveal + 3.2, horn(hz('A3'), .8, loud=.6), .4)
    m.add('brass', reveal + 4.1, horn(hz('D4'), 2.4, loud=.55, release=1.8), .4)


SECTIONS = {'intro': intro, 'build': build, 'dread': dread, 'battle': battle, 'magic': magic, 'pulse': pulse}


# ------------------------------------------------------------------ the rooms and the master

def room(seconds, decay, seed):
    """A room's answer to a click: noise dying away, a little different in each ear."""
    n = int(seconds * SR)
    t = np.arange(n) / SR
    r = np.random.default_rng(seed)
    ir = np.stack([r.standard_normal(n), r.standard_normal(n)]).astype(np.float32) * np.exp(-t / decay).astype(np.float32)
    b, a = signal.butter(1, 5000 / (SR / 2), 'low')
    ir = signal.lfilter(b, a, ir).astype(np.float32)
    ir[:, :int(.012 * SR)] = 0
    return ir / np.sqrt((ir ** 2).sum(axis=1, keepdims=True))


def master(m, hits):
    """Each bus in its room, the drums ducked for a moment before each great hit, all summed, softly limited."""
    wet = {'drums': .18, 'pad': .45, 'choir': .55, 'brass': .4, 'strings': .35, 'hits': .3, 'fx': .4}
    level = {'drums': 1.0, 'pad': .55, 'choir': .7, 'brass': .7, 'strings': .6, 'hits': 1.1, 'fx': .5}
    ir = room(3.2, .85, 3)
    out = np.zeros((2, m.n), np.float32)
    duck = np.ones(m.n, np.float32)
    for h in hits:
        # a breath before the hit: the drums and strings drop away, then come back
        i0, i1, i2 = int((h - .45) * SR), int((h - .02) * SR), int((h + .6) * SR)
        if 0 <= i0 < m.n:
            duck[i0:min(i1, m.n)] = np.minimum(duck[i0:min(i1, m.n)], np.linspace(1, .15, min(i1, m.n) - i0))
            if i1 < m.n:
                duck[i1:min(i2, m.n)] = np.minimum(duck[i1:min(i2, m.n)], np.linspace(.15, 1, min(i2, m.n) - i1))
    for name, b in m.bus.items():
        x = b.copy()
        if name in ('drums', 'strings'):
            x *= duck
        r = np.stack([signal.fftconvolve(x[c], ir[c])[:m.n] for c in range(2)]).astype(np.float32)
        out += (x * (1 - wet[name]) + r * wet[name] * 1.4) * level[name]
    for h in hits:
        if h < m.length:
            m.add('_', h, boom(1.0), 1.0)
    if '_' in m.bus:
        hb = m.bus['_']
        out += (hb + np.stack([signal.fftconvolve(hb[c], ir[c])[:m.n] for c in range(2)]) * .45) * 1.0
    # nothing below the hearing (the low end kept clear for the game's own blasts), a gentle limit, to a peak just under full
    b, a = signal.butter(2, 34 / (SR / 2), 'high')
    out = signal.lfilter(b, a, out).astype(np.float32)
    out = out[:, :int(m.length * SR)]
    peak = np.abs(out).max() + 1e-9
    out = np.tanh(out / peak * 1.6) / np.tanh(1.6)
    # the last second fades to nothing
    f = int(1.0 * SR)
    out[:, -f:] *= np.linspace(1, 0, f)
    return out * .89


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('out')
    p.add_argument('--length', type=float, required=True, help='seconds of music')
    p.add_argument('--cue', action='append', default=[], help='seconds:section, a section from then to the next cue (intro build dread battle magic pulse title)')
    p.add_argument('--hit', action='append', type=float, default=[], help='seconds of a great hit')
    p.add_argument('--reveal', type=float, help='seconds the title comes up')
    a = p.parse_args()
    cues = sorted((float(c.split(':')[0]), c.split(':')[1]) for c in a.cue)
    m = Mix(a.length)
    for i, (t0, name) in enumerate(cues):
        t1 = cues[i + 1][0] if i + 1 < len(cues) else a.length
        if name == 'title':
            title(m, t0, t1, a.reveal if a.reveal else t0 + 3)
        else:
            SECTIONS[name](m, t0, t1)
        print(f'[score] {name:7} {t0:6.1f} – {t1:6.1f} s')
    out = master(m, a.hit)
    with wave.open(a.out, 'wb') as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((out.T * 32767).astype(np.int16).tobytes())
    print(f'[score] {a.out}: {out.shape[1] / SR:.1f} s, {len(a.hit)} hits')


if __name__ == '__main__':
    main()
