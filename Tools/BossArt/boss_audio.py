"""Synthesised sound for the Forest boss: one looping battle theme and the effect sounds.

    python Tools/BossArt/boss_audio.py        # writes Assets/!Sounds/Boss/*.wav (+ .meta with stable GUIDs)

Everything is generated here (numpy only): no samples from anywhere else. 44.1 kHz, 16-bit mono.
The theme, "Iron Foreman": 140 BPM, D minor, 32 bars (about 55 s), loops seamlessly (tails wrap to the start).
A forge march: kick + anvil backbeat, a driving low ostinato, heavy brass-like stabs, and a square lead that
enters in the second half of each 16 bars.
"""
import hashlib, os, wave
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, 'Assets', '!Sounds', 'Boss')
SR = 44100
rng = np.random.default_rng(20261128)


def t_axis(seconds): return np.arange(int(SR * seconds)) / SR
def hz(midi): return 440.0 * 2 ** ((midi - 69) / 12)
def env_exp(t, decay): return np.exp(-t / decay)


def band(x, lo=None, hi=None):
    """Zero-phase band limit through the FFT (smooth 2nd-order-like skirts)."""
    spectrum = np.fft.rfft(x); f = np.fft.rfftfreq(len(x), 1 / SR)
    gain = np.ones_like(f)
    if hi: gain *= 1 / np.sqrt(1 + (f / hi) ** 4)
    if lo: gain *= 1 - 1 / np.sqrt(1 + (f / lo) ** 4)
    return np.fft.irfft(spectrum * gain, len(x))


def saw(freq, t, voices=1, detune=0.0):
    out = np.zeros_like(t)
    for v in range(voices):
        f = freq * (1 + detune * (v - (voices - 1) / 2))
        out += 2 * ((f * t + v * .31) % 1) - 1
    return out / voices


def square(freq, t, duty=.5): return np.where((freq * t) % 1 < duty, 1.0, -1.0)


# ------------------------------------------------------------------ drum / metal voices
def kick(strength=1.0):
    t = t_axis(.32)
    f = 46 + 95 * np.exp(-t / .035)
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * env_exp(t, .11)
    click = band(rng.standard_normal(len(t)), 800, 5000) * env_exp(t, .004) * .35
    return (body + click) * strength


def anvil(strength=1.0, pitch=1.0):
    """Hammer on iron: inharmonic partials with a hard attack."""
    t = t_axis(.5)
    out = np.zeros_like(t)
    for f, a, d in ((523, 1.0, .16), (1187, .7, .11), (1931, .55, .08), (2861, .4, .06), (4127, .25, .04)):
        out += a * np.sin(2 * np.pi * f * pitch * t) * env_exp(t, d)
    out += band(rng.standard_normal(len(t)), 2000, 9000) * env_exp(t, .012) * .9
    return out * .5 * strength


def steam_tick(strength=1.0):
    t = t_axis(.07)
    return band(rng.standard_normal(len(t)), 5000, 12000) * env_exp(t, .018) * .35 * strength


def boom(seconds=.9, strength=1.0, low=38):
    t = t_axis(seconds)
    f = low + 60 * np.exp(-t / .06)
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * env_exp(t, seconds * .28)
    rumble = band(rng.standard_normal(len(t)), 30, 180) * env_exp(t, seconds * .35) * .8
    return (body + rumble) * strength


# ------------------------------------------------------------------ the battle theme
BPM = 140
BEAT = 60 / BPM
BARS = 32
LENGTH = int(round(SR * BEAT * 4 * BARS))


def place(buffer, sound, beat, gain=1.0):
    """Adds `sound` at `beat` (0-based, in beats); anything past the end wraps to the start so the loop is seamless."""
    start = int(round(beat * BEAT * SR)) % LENGTH
    end = start + len(sound)
    if end <= LENGTH: buffer[start:end] += sound * gain
    else:
        first = LENGTH - start
        buffer[start:] += sound[:first] * gain; buffer[:end - LENGTH] += sound[first:] * gain


def note(freq, beats, wave_fn, attack=.006, release=.05, **kw):
    seconds = beats * BEAT
    t = t_axis(seconds + release)
    env = np.minimum(1, t / attack) * np.where(t < seconds, 1, np.exp(-(t - seconds) / (release / 4)))
    return wave_fn(freq, t, **kw) * env


# 16-bar progression, one chord per bar (the second 16 bars repeat it with the lead an octave higher)
CHORDS = [  # (root midi for the bass, chord tones for the stabs)
    (38, (62, 65, 69)), (38, (62, 65, 69)), (34, (58, 62, 65)), (36, (60, 64, 67)),      # Dm Dm Bb C
    (38, (62, 65, 69)), (38, (62, 65, 69)), (31, (55, 58, 62)), (33, (57, 61, 64)),      # Dm Dm Gm A
    (38, (62, 65, 69)), (41, (60, 65, 69)), (34, (58, 62, 65)), (36, (60, 64, 67)),      # Dm F  Bb C
    (38, (62, 65, 69)), (34, (58, 62, 65)), (31, (55, 58, 62)), (33, (57, 61, 64)),      # Dm Bb Gm A
]
# lead: (bar 0-7 within the phrase, beat 0-3.5, length in beats, midi)
LEAD = [
    (0, 0, 1, 74), (0, 1, .5, 77), (0, 1.5, .5, 76), (0, 2, 1, 74), (0, 3, 1, 69),
    (1, 0, 1.5, 74), (1, 1.5, .5, 76), (1, 2, 2, 77),
    (2, 0, 1, 77), (2, 1, .5, 79), (2, 1.5, .5, 77), (2, 2, 1, 74), (2, 3, 1, 70),
    (3, 0, 1.5, 76), (3, 1.5, .5, 74), (3, 2, 2, 72),
    (4, 0, .5, 74), (4, .5, .5, 77), (4, 1, .5, 81), (4, 1.5, 1.5, 81), (4, 3, 1, 79),
    (5, 0, 1, 77), (5, 1, 1, 76), (5, 2, 2, 74),
    (6, 0, 1, 79), (6, 1, 1, 82), (6, 2, 1, 81), (6, 3, 1, 79),
    (7, 0, 2, 81), (7, 2, 1, 76), (7, 3, 1, 73),
]


def theme():
    drums = np.zeros(LENGTH); bass = np.zeros(LENGTH); stabs = np.zeros(LENGTH); lead = np.zeros(LENGTH); arp = np.zeros(LENGTH)
    k, k_soft, a, a_high, tick = kick(), kick(.7), anvil(), anvil(.8, 1.26), steam_tick()
    for bar in range(BARS):
        b0 = bar * 4
        root, tones = CHORDS[bar % 16]
        second_half = bar % 16 >= 8
        last_of_phrase = bar % 8 == 7
        # --- drums: four on the floor would be a dance beat; this is a march: 1, the "and" of 2, 3
        place(drums, k, b0); place(drums, k_soft, b0 + 1.5); place(drums, k, b0 + 2)
        if second_half: place(drums, k_soft, b0 + 3.5)
        place(drums, a, b0 + 1); place(drums, a_high if bar % 2 else a, b0 + 3)
        for eighth in range(8):
            place(drums, tick, b0 + eighth * .5, .5 if eighth % 2 == 0 else 1.0)
        if last_of_phrase:                                    # fill: hammers tumbling into the next phrase
            for i, beat in enumerate((2.5, 3, 3.25, 3.5, 3.75)): place(drums, anvil(.6 + .1 * i, 1 + .12 * i), b0 + beat)
        # --- bass ostinato in eighths: root root octave root | root root fifth octave
        for eighth, offset in enumerate((0, 0, 12, 0, 0, 0, 7, 12)):
            place(bass, note(hz(root + offset), .46, saw, voices=2, detune=.006), b0 + eighth * .5)
        # --- stabs: the chord hits on 1 and is pushed again on the "and" of 2
        for beat, length, gain in ((0, 1.2, 1.0), (1.5, .45, .7)) + (((3, .9, .8),) if bar % 4 == 3 else ()):
            for tone in tones:
                place(stabs, note(hz(tone - 12), length, saw, attack=.012, release=.12, voices=3, detune=.009), b0 + beat, gain)
        # --- arpeggio (bars 17-24 only: the bridge before the lead returns)
        if 16 <= bar < 24:
            pattern = (tones[0], tones[1], tones[2], tones[1] + 12, tones[2], tones[1], tones[0] + 12, tones[1])
            for sixteenth in range(16):
                place(arp, note(hz(pattern[sixteenth % 8] + 12), .22, square, release=.03, duty=.25), b0 + sixteenth * .25, .8)
    # --- lead: bars 9-16 as written, bars 25-32 an octave up with the lower octave underneath
    for phrase_start, octave, double in ((8, 0, False), (24, 12, True)):
        for bar, beat, length, midi in LEAD:
            at = (phrase_start + bar) * 4 + beat
            seconds = length * BEAT
            t = t_axis(seconds + .08)
            vibrato = 1 + .006 * np.sin(2 * np.pi * 5.5 * t) * np.minimum(1, t / .18)
            env = np.minimum(1, t / .008) * np.where(t < seconds, 1, np.exp(-(t - seconds) / .02))
            tone = np.where((np.cumsum(hz(midi + octave) * vibrato) / SR) % 1 < .5, 1.0, -1.0) * env
            place(lead, tone, at)
            if double: place(lead, note(hz(midi), length, square, release=.06, duty=.5), at, .55)

    mix = (band(drums, 28, 12000) * .78 + band(bass, 35, 900) * .5 + band(stabs, 90, 3200) * .30
           + band(lead, 180, 5200) * .17 + band(arp, 300, 6000) * .10)
    mix = np.tanh(mix * 1.5) / np.tanh(1.5)          # soft saturation: glues the kit, tames peaks
    return mix / np.max(np.abs(mix)) * .89


# ------------------------------------------------------------------ effect sounds
def fade_tail(x, seconds=.02):
    n = min(len(x), int(SR * seconds)); x = x.copy(); x[-n:] *= np.linspace(1, 0, n); return x


def norm(x, peak=.9):
    x = fade_tail(x / np.max(np.abs(x)) * peak)
    n = int(SR * .002); x[:n] *= np.linspace(0, 1, n)   # 2 ms fade-in: no click at the start
    return x


def sfx_thud(): return norm(band(boom(1.1, 1.0, 34), None, 260), .92)                      # distant footstep


def sfx_slam():                                                                             # fist into the ground
    out = np.zeros(int(SR * 1.0)); b = boom(.9, 1.0, 42); out[:len(b)] += b
    a = anvil(.9, .62); out[:len(a)] += a
    t = t_axis(.5); debris = band(rng.standard_normal(len(t)), 400, 4000) * env_exp(t, .12) * .5
    out[:len(debris)] += debris
    return norm(out)


def sfx_clank():                                                                            # blade on armour: no damage
    a = anvil(1.0, 1.55); return norm(a[:int(SR * .35)])


def sfx_chain():                                                                            # chain running out of the drum
    out = np.zeros(int(SR * .7))
    for i in range(11):
        at = int(SR * (.02 + i * .055 + rng.uniform(-.008, .008)))
        link = anvil(.5, 2.1 + rng.uniform(-.25, .25))[:int(SR * .09)]
        out[at:at + len(link)] += link * (1 - i * .05)
    return norm(out, .8)


def sfx_steam():                                                                            # vent hiss
    t = t_axis(.75); e = np.minimum(1, t / .03) * env_exp(t, .22)
    return norm(band(rng.standard_normal(len(t)), 2200, 11000) * e, .7)


def sfx_whistle():                                                                          # steam whistle: two reeds a fourth apart
    t = t_axis(1.5)
    e = np.minimum(1, t / .09) * np.where(t < 1.15, 1, np.exp(-(t - 1.15) / .09))
    bend = 1 - .05 * np.exp(-t / .12)
    tone = sum(np.sin(2 * np.pi * np.cumsum(f * bend) / SR * h) / h for f in (466, 622) for h in (1, 2, 3))
    hiss = band(rng.standard_normal(len(t)), 1500, 9000) * .5
    return norm((tone * .5 + hiss) * e, .85)


def sfx_charge():                                                                           # furnace winding up
    t = t_axis(1.25); f = 55 + 110 * (t / 1.25) ** 2
    hum = np.sin(2 * np.pi * np.cumsum(f) / SR) + .4 * np.sin(2 * np.pi * np.cumsum(f * 2.01) / SR)
    roar = band(rng.standard_normal(len(t)), 120, 1400) * (t / 1.25)
    return norm((hum * .7 + roar * .6) * np.minimum(1, t / .08) * (.35 + .65 * t / 1.25), .85)


def sfx_explosion():
    out = np.zeros(int(SR * 1.3)); b = boom(1.2, 1.0, 36); out[:len(b)] += b
    t = t_axis(1.0); crack = band(rng.standard_normal(len(t)), 150, 6500) * env_exp(t, .2)
    out[:len(crack)] += crack * .9
    return norm(np.tanh(out * 1.8))


def sfx_slag_hit():                                                                         # molten glob on the ground
    t = t_axis(.42)
    sizzle = band(rng.standard_normal(len(t)), 3000, 11000) * env_exp(t, .11) * .7
    thump = np.sin(2 * np.pi * np.cumsum(70 + 80 * np.exp(-t / .03)) / SR) * env_exp(t, .06)
    return norm(sizzle + thump, .8)


def sfx_sweep():                                                                            # fist dragged along the ground
    t = t_axis(1.1); e = np.sin(np.pi * t / 1.1) ** .7
    scrape = band(rng.standard_normal(len(t)), 250, 2600) * e
    grind = np.sin(2 * np.pi * 61 * t) * e * .5
    return norm(scrape + grind, .8)


def sfx_roar():                                                                             # phase 2: the boiler screams
    t = t_axis(1.6); e = np.minimum(1, t / .05) * np.where(t < 1.2, 1, np.exp(-(t - 1.2) / .12))
    f = 92 * (1 + .04 * np.sin(2 * np.pi * 7 * t))
    growl = saw(1, np.cumsum(f) / SR) + .6 * saw(1, np.cumsum(f * 1.498) / SR)
    return norm(np.tanh((band(growl, 60, 1800) + band(rng.standard_normal(len(t)), 800, 5000) * .5) * e * 2.2), .88)


def sfx_tread():                                                                           # crawler track: clack-CLACK and a low thud
    out = np.zeros(int(SR * .34))
    for at, strength, pitch in ((.0, .7, .8), (.11, 1.0, .66)):
        hit = anvil(strength, pitch)[:int(SR * .16)]; start = int(SR * at); out[start:start + len(hit)] += hit
    thud = boom(.3, .8, 52); out[:len(thud)] += thud[:len(out)] * .6
    return norm(band(out, 45, 5200), .85)


def sfx_engine():                                                                          # the boiler driving the tracks: a short chug (looped by pitch in code)
    t = t_axis(.5)
    chug = sum(np.sin(2 * np.pi * 48 * h * t) / h for h in (1, 2, 3)) * (.55 + .45 * np.sign(np.sin(2 * np.pi * 8 * t)))
    hiss = band(rng.standard_normal(len(t)), 900, 5000) * (.5 + .5 * np.sin(2 * np.pi * 8 * t)) * .35
    return norm(band(chug, 35, 900) + hiss, .8)


SOUNDS = {
    'Boss_BGM': theme, 'Boss_Thud': sfx_thud, 'Boss_Slam': sfx_slam, 'Boss_Clank': sfx_clank, 'Boss_Chain': sfx_chain,
    'Boss_Steam': sfx_steam, 'Boss_Whistle': sfx_whistle, 'Boss_Charge': sfx_charge, 'Boss_Explosion': sfx_explosion,
    'Boss_SlagHit': sfx_slag_hit, 'Boss_Sweep': sfx_sweep, 'Boss_Roar': sfx_roar, 'Boss_Tread': sfx_tread,
}


def stable_guid(name): return hashlib.md5(('BehindTheGear/Boss/' + name).encode()).hexdigest()


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, make in SOUNDS.items():
        data = (np.clip(make(), -1, 1) * 32767).astype('<i2')
        path = os.path.join(OUT, name + '.wav')
        with wave.open(path, 'wb') as f:
            f.setnchannels(1); f.setsampwidth(2); f.setframerate(SR); f.writeframes(data.tobytes())
        meta = path + '.meta'
        if not os.path.exists(meta):   # Unity fills in the importer block on first import; the GUID stays
            with open(meta, 'w', encoding='utf-8', newline='\n') as f: f.write('fileFormatVersion: 2\nguid: ' + stable_guid(name + '.wav') + '\n')
        print(f'{name}: {len(data) / SR:.2f} s')


if __name__ == '__main__':
    main()
