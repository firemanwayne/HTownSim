"""Measures the two horns in the reference clip (Reference/horn_ref_0016-0028.wav, video 0:16-0:28) for the synthesizer.

    python horn_analysis.py

The clip has the electric horn first (video ~0:19.6-0:20.9) and then the air horn (blasts from ~0:21.2). For each,
the fundamental is found independently (the 150-300 Hz candidate whose harmonics 1-8 carry the most energy, refined
from its stronger harmonics), then the level of harmonics 1-16 relative to the strongest. Writes horn_profile.json.
The friction siren sweeps through the clip (it sits near 1300 Hz during the electric horn), so the fundamental search is
limited to 150-300 Hz, where the siren never goes. Both horns measure about 210 Hz: they differ in tone, not pitch.
"""
import json
import os
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
SOURCE = os.path.join(HERE, "Reference", "horn_ref_0016-0028.wav")
# Windows in clip seconds (video time = clip + 16 s).
HORNS = {
    "electric": [(3.50, 4.60)],
    "air": [(5.45, 7.15), (8.30, 10.40)],
}
HARMONICS = 16
SIZE = 1 << 19

with wave.open(SOURCE, "rb") as w:
    rate = w.getframerate()
    channels = w.getnchannels()
    data = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float64) / 32768.0
mono = data.reshape(-1, channels).mean(axis=1)
freqs = np.fft.rfftfreq(SIZE, 1 / rate)


def spectrum(t0, t1):
    seg = mono[int(t0 * rate):int(t1 * rate)]
    return np.abs(np.fft.rfft(seg * np.hanning(len(seg)), n=SIZE))


def level_near(spec, target, tolerance):
    band = np.where(np.abs(freqs - target) < tolerance)[0]
    k = band[np.argmax(spec[band])]
    return freqs[k], spec[k]


def fundamental(spec):
    best, best_score = 0.0, -1.0
    for candidate in np.arange(150.0, 300.0, 0.25):
        score = sum(level_near(spec, n * candidate, 3)[1] ** 2 for n in range(1, 9) if n * candidate < 4000)
        # Penalise sub-harmonics: a candidate whose odd multiples are empty is half of the real note.
        if score > best_score:
            best, best_score = candidate, score
    # Refine from harmonics 2-6.
    refined = [level_near(spec, n * best, 6)[0] / n for n in range(2, 7)]
    return float(np.median(refined))


profile = {}
for name, windows in HORNS.items():
    spec = sum(spectrum(*w) for w in windows)
    f0 = fundamental(spec)
    # Check the halving trap: if the odd harmonics are weak, the true note is double.
    odd = np.mean([level_near(spec, n * f0, 4)[1] for n in (1, 3, 5)])
    even = np.mean([level_near(spec, n * f0, 4)[1] for n in (2, 4, 6)])
    if odd < 0.2 * even:
        f0 *= 2
    levels = [level_near(spec, n * f0, 4)[1] for n in range(1, HARMONICS + 1)]
    top = max(levels)
    levels_db = [float(20 * np.log10(level / top)) for level in levels]
    profile[name] = dict(fundamental_hz=f0, harmonic_levels_db=levels_db, windows_video_s=[(16 + a, 16 + b) for a, b in windows])
    print(f"{name} horn: fundamental {f0:.2f} Hz")
    print("   " + "  ".join(f"h{n}:{db:5.1f}" for n, db in enumerate(levels_db, 1)))

json.dump(dict(source=os.path.basename(SOURCE), horns=profile), open(os.path.join(HERE, "horn_profile.json"), "w"), indent=1)
