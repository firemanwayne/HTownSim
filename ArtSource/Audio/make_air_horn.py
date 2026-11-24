"""Synthesizes the apparatus air horn from the measured reference (horn_profile2.json, made by horn_analysis2.py from
the air horn in video KDFYrhJfku4 at 2:52-2:58).

    python make_air_horn.py

Twin trumpets at the measured pitches: the main one on ~198 Hz (deep: its fundamental is its strongest harmonic, 2nd-4th
within ~3.5 dB) and a weaker second one on ~183 Hz (strong 1st and 4th harmonics) that wanders slightly. Together they
beat at ~15 Hz, the growl of a big air horn. Both pitches are snapped so the 2 s hold loop contains whole cycles of
each (and of the beat and every modulation), so it loops seamlessly. Light saturation for brass. Start: a fast ~80 ms
attack that begins ~1.3% sharp and settles, plus a brief reed flare. Hold: a slow level wobble and a little breath
noise. The game fades it out over 0.3 s on release (AirHorn.cs).

Writes (48 kHz mono 16-bit, one gain for both so they join seamlessly):
  Assets/HTownSim/Resources/SirenAudio/AirHornStart.wav  attack + first part of the hold (play once)
  Assets/HTownSim/Resources/SirenAudio/AirHornLoop.wav   the hold, looped while the horn is held
  ArtSource/Audio/AirHorn_demo.wav                       start + 2 loops + 0.3 s release, to listen to
"""
import json
import os
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "Assets", "HTownSim", "Resources", "SirenAudio")
PROFILE = os.path.join(HERE, "horn_profile2.json")
RATE = 48000
LOOP_SECONDS = 2.0
START_SECONDS = 0.5
ATTACK_SECONDS = 0.08
SHARP_START = 0.013
SETTLE_SECONDS = 0.03
FLARE_RATIO = 3.5
FLARE_SECONDS = 0.07
SECOND_WANDER = (0.5, 0.006)              # (Hz, fraction): slow pitch wander of the second trumpet, whole cycles per loop
WOBBLE = ((3.0, 1.2), (5.0, 0.6))         # (Hz, dB): whole cycles per loop
NOISE_DB = -32.0
SATURATION = 1.2
PEAK_DB = -1.0
RELEASE_SECONDS = 0.3

profile = json.load(open(PROFILE))
notes = []
for key in ("main", "second"):
    measured = profile[key]["fundamental_hz"]
    snapped = round(measured * LOOP_SECONDS) / LOOP_SECONDS
    amplitudes = np.array([10 ** (db / 20) for db in profile[key]["harmonic_levels_db"]])
    notes.append((key, measured, snapped, amplitudes))

total = START_SECONDS + LOOP_SECONDS
n = int(round(total * RATE))
t = np.arange(n) / RATE
loop_start = int(round(START_SECONDS * RATE))
tl = t - START_SECONDS                     # time measured from the loop start
rng = np.random.default_rng(7)
signal = np.zeros(n)

for key, measured, f0, amplitudes in notes:
    # Pitch: both trumpets start sharp and settle; the second also wanders slowly (periodic over the loop).
    settle = 1 + SHARP_START * np.exp(-t / SETTLE_SECONDS)
    settle[t > 0.3] = 1.0
    frequency = f0 * settle
    if key == "second":
        rate_hz, depth = SECOND_WANDER
        frequency = frequency * (1 + depth * np.sin(2 * np.pi * rate_hz * tl))
    phase = 2 * np.pi * np.cumsum(frequency) / RATE
    # Align so that from the loop start the phase is exactly periodic over the loop.
    phase += 2 * np.pi * f0 * tl[loop_start] - phase[loop_start]
    offsets = rng.uniform(0, 2 * np.pi, len(amplitudes))
    for h, (a, p) in enumerate(zip(amplitudes, offsets), 1):
        if h * f0 < RATE / 2 - 500:
            signal += a * np.sin(h * phase + p)

# Level wobble, periodic over the loop.
wobble_db = sum(depth * np.sin(2 * np.pi * rate_hz * tl + i) for i, (rate_hz, depth) in enumerate(WOBBLE))
signal *= 10 ** (wobble_db / 20)

# Breath noise (700-3500 Hz): one loop's worth, tiled, so it is periodic too.
loop_len = n - loop_start
noise = rng.standard_normal(loop_len)
spec = np.fft.rfft(noise)
nf = np.fft.rfftfreq(loop_len, 1 / RATE)
spec[(nf < 700) | (nf > 3500)] = 0
noise = np.fft.irfft(spec, loop_len)
noise /= np.sqrt(np.mean(noise ** 2))
noise_full = np.concatenate([noise[-loop_start:], noise])
signal += noise_full * 10 ** (NOISE_DB / 20) * np.sqrt(np.mean(signal ** 2))

# Brassy saturation, then remove the DC offset it adds (the mean over whole loops, so the loop stays periodic).
signal = np.tanh(SATURATION * signal / np.max(np.abs(signal))) / np.tanh(SATURATION)
signal -= np.mean(signal[loop_start:])

# Attack and the reed flare at the start.
attack = np.clip(t / ATTACK_SECONDS, 0, 1)
signal *= attack * attack * (3 - 2 * attack)
main_f0 = notes[0][2]
flare = np.sin(2 * np.pi * FLARE_RATIO * main_f0 * t) * np.exp(-t / (FLARE_SECONDS / 3)) * 0.35
signal += flare * (t < FLARE_SECONDS * 2)

signal *= 10 ** (PEAK_DB / 20) / np.max(np.abs(signal))
start_clip, loop_clip = signal[:loop_start], signal[loop_start:]


def write(path, samples):
    pcm = np.clip(np.round(samples * 32767), -32768, 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())


os.makedirs(OUT, exist_ok=True)
write(os.path.join(OUT, "AirHornStart.wav"), start_clip)
write(os.path.join(OUT, "AirHornLoop.wav"), loop_clip)
release_len = int(RELEASE_SECONDS * RATE)
release = loop_clip[:release_len] * (1 - np.arange(release_len) / release_len) ** 2
write(os.path.join(HERE, "AirHorn_demo.wav"), np.concatenate([start_clip, loop_clip, loop_clip, release]))

steps = np.abs(np.diff(loop_clip))
for key, measured, f0, _ in notes:
    print(f"{key} trumpet {f0:.2f} Hz (measured {measured:.2f}), {f0 * LOOP_SECONDS:.1f} cycles per loop")
print(f"beat {abs(notes[0][2] - notes[1][2]):.1f} Hz; start {len(start_clip) / RATE:.2f} s, loop {len(loop_clip) / RATE:.2f} s")
print(f"loop seam step {abs(loop_clip[0] - loop_clip[-1]):.4f} vs typical {np.median(steps):.4f} (max {steps.max():.4f}); "
      f"start->loop step {abs(loop_clip[0] - start_clip[-1]):.4f}")
