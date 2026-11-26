#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>Builds the short synthetic clips for the truck's turbo whine, air brakes, brake squeal and backing alarm.</summary>
    internal static class TruckSounds
    {
        private const int SampleRate = 44100;

        /// <summary>
        /// A one-second loop of turbo spool: a soft 820 Hz whistle with a quiet octave below, over a band of airy hiss.
        /// No warble and no shrill overtone (the earlier 1.4/2.8 kHz tone with a 7 Hz wobble read as a siren). Every
        /// frequency is a whole number of cycles per second and the noise is looped by crossfade, so it loops cleanly.
        /// </summary>
        internal static AudioClip Turbo()
        {
            var data = new float[SampleRate];

            var random = new System.Random(17);

            var noise = new float[SampleRate];

            var lowPass = 0f;

            var highPass = 0f;

            for (var i = 0; i < noise.Length; i++)
            {
                var white = (float)(random.NextDouble() * 2.0 - 1.0);

                // Band-limit the hiss: smooth it (low-pass), then remove the rumble (high-pass).
                lowPass += (white - lowPass) * 0.35f;

                highPass = lowPass - highPass * 0.6f;

                noise[i] = highPass;
            }

            var fade = SampleRate / 20;

            for (var i = 0; i < data.Length; i++)
            {
                var t = i / (float)SampleRate;

                var hiss = noise[i];

                if (i < fade)
                {
                    // Crossfade the start with the end of the noise so the loop seam is inaudible.
                    var k = i / (float)fade;

                    hiss = noise[i] * k + noise[noise.Length - fade + i] * (1f - k);
                }

                var tone = Mathf.Sin(2f * Mathf.PI * 820f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 410f * t) * 0.18f;

                data[i] = (tone + hiss * 0.22f) * 0.5f;
            }

            return Make("TurboWhine", data);
        }

        /// <summary>A burst of air-brake hiss: filtered noise that starts hard and dies away over about 1.3 seconds.</summary>
        internal static AudioClip AirBrake()
        {
            var data = new float[(int)(SampleRate * 1.3f)];

            var random = new System.Random(11);

            var last = 0f;

            for (var i = 0; i < data.Length; i++)
            {
                var t = i / (float)SampleRate;

                var noise = (float)(random.NextDouble() * 2.0 - 1.0);

                var high = noise - last * 0.55f;

                last = noise;

                var envelope = Mathf.Clamp01(t / 0.015f) * Mathf.Exp(-t * 3.4f);

                data[i] = high * envelope * 0.7f;
            }

            return Make("AirBrake", data);
        }

        /// <summary>
        /// A one-second loop of brake squeal: one 1.3 kHz tone with a soft octave above and a little hiss. The earlier
        /// version stacked 2210 and 2290 Hz (an 80 Hz beat) and 3330 Hz, which read as a shrill, wavering whine.
        /// </summary>
        internal static AudioClip Squeal()
        {
            var data = new float[SampleRate];

            var random = new System.Random(5);

            var smooth = 0f;

            for (var i = 0; i < data.Length; i++)
            {
                var t = i / (float)SampleRate;

                smooth += ((float)(random.NextDouble() * 2.0 - 1.0) - smooth) * 0.2f;

                data[i] = (Mathf.Sin(2f * Mathf.PI * 1300f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 2600f * t) * 0.08f + smooth * 0.15f) * 0.45f;
            }

            return Make("BrakeSqueal", data);
        }

        /// <summary>A one-second backing alarm: a hard 1.1 kHz beep for the first half second, then silence.</summary>
        internal static AudioClip BackingAlarm()
        {
            var data = new float[SampleRate];

            var beep = SampleRate / 2;

            for (var i = 0; i < beep; i++)
            {
                var t = i / (float)SampleRate;

                var square = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 1100f * t));

                var envelope = Mathf.Clamp01(i / 120f) * Mathf.Clamp01((beep - i) / 120f);

                data[i] = (square * 0.35f + Mathf.Sin(2f * Mathf.PI * 1100f * t) * 0.45f) * envelope;
            }

            return Make("BackingAlarm", data);
        }

        private static AudioClip Make(string clipName, float[] data)
        {
            var clip = AudioClip.Create(clipName, data.Length, 1, SampleRate, false);

            clip.SetData(data, 0);

            return clip;
        }
    }
}
