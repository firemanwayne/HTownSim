"""Measures the second reference air horn (Reference/airhorn2_ref_0250-0300.wav: video KDFYrhJfku4, 2:50-3:00; the
horn is 2:52-2:58) for make_air_horn.py, writing horn_profile2.json.

    python horn_analysis2.py

A wailing siren sweeps through the clip, so levels come from a per-frame MEDIAN spectrum over the long blast
(2:53.7-2:54.6): steady tones survive, the moving siren line does not. Frame-by-frame tracking shows two tones:
  - the main trumpet, a clean series on ~197 Hz (197, 394, 592, 789, 985, 1182 ...);
  - a weaker second trumpet around ~184 Hz, heard mostly through its 4th and 5th harmonics (~735, ~920 Hz),
    wandering a few percent. The two beat (~13 Hz), the growl of a twin-trumpet air horn.
Each note's fundamental is refined from its stronger harmonics; harmonic 7 of the main note sits where the siren
plateaus (~1380-1440 Hz), so it is interpolated from its neighbours.
"""
import json
import os
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
SOURCE = os.path.join(HERE, "Reference", "airhorn2_ref_0250-0300.wav")
BLAST = (3.72, 4.62)
WIN = 4096
HOP = 512
PAD = 4

with wave.open(SOURCE, "rb") as w:
    rate = w.getframerate()
    ch = w.getnchannels()
    x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float64).reshape(-1, ch).mean(axis=1) / 32768

freqs = np.fft.rfftfreq(WIN * PAD, 1 / rate)
frames = [np.abs(np.fft.rfft(x[s:s + WIN] * np.hanning(WIN), n=WIN * PAD))
          for s in range(int(BLAST[0] * rate), int(BLAST[1] * rate) - WIN, HOP)]
spec = np.median(np.array(frames), axis=0)


def peak(target, tolerance):
    band = np.where(np.abs(freqs - target) < tolerance)[0]
    k = band[np.argmax(spec[band])]
    return freqs[k], spec[k]


def refine(guess, harmonics, tolerance):
    return float(np.median([peak(n * guess, tolerance)[0] / n for n in harmonics]))


main = refine(197.0, (1, 2, 3, 4, 5, 6), 5)
second = refine(184.0, (4, 5), 14)
main_levels = [peak(n * main, 4)[1] for n in range(1, 17)]
second_levels = [peak(n * second, 6)[1] for n in range(1, 9)]
# Scale to the main trumpet's first 12 harmonics; 13-16 (2.6-3.2 kHz) sit under the siren's second harmonic, so they
# are capped at the level of 11-12.
main_levels[6] = float(np.sqrt(main_levels[5] * main_levels[7]))   # siren plateau sits on harmonic 7: interpolate first
top = max(main_levels[:12])
main_db = [float(20 * np.log10(v / top)) for v in main_levels]
second_db = [float(20 * np.log10(v / top)) for v in second_levels]
cap = min(main_db[10], main_db[11])
main_db[12:] = [min(d, cap) for d in main_db[12:]]
floor = float(20 * np.log10(np.median(spec[(freqs > 150) & (freqs < 1500)]) / top))

profile = dict(
    source=os.path.basename(SOURCE),
    blast_video_s=(170 + BLAST[0], 170 + BLAST[1]),
    main=dict(fundamental_hz=main, harmonic_levels_db=main_db),
    second=dict(fundamental_hz=second, harmonic_levels_db=second_db),
    floor_db=floor,
)
json.dump(profile, open(os.path.join(HERE, "horn_profile2.json"), "w"), indent=1)
print(f"main trumpet {main:.1f} Hz:   " + " ".join(f"{d:5.1f}" for d in main_db[:12]))
print(f"second trumpet {second:.1f} Hz: " + " ".join(f"{d:5.1f}" for d in second_db))
print(f"beat {abs(main - second):.1f} Hz, ratio {second / main:.3f}; median floor {floor:.1f} dB")
