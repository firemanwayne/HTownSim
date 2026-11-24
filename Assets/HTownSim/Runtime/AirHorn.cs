#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// The apparatus horn (the air horn on the engine and ladder, the plain electric horn on the ambulance): a start clip (attack and the first of the blast) that hands off, sample-accurately, to a
    /// seamless hold loop for as long as the horn is held, then fades out over a short release when it is let go, like
    /// the air bleeding off. Pressing again mid-fade blows a fresh blast. Clips: Resources/SirenAudio/AirHornStart and
    /// AirHornLoop, made by ArtSource/Audio/make_air_horn.py. Driven by <see cref="FireResponse"/> (H key, HORN button).
    /// </summary>
    internal sealed class AirHorn : MonoBehaviour
    {
        /// <summary>How long the horn takes to die away after it is released (the air horn bleeds off over 0.3 s; the electric horn stops almost at once).</summary>
        private float _releaseSeconds = 0.3f;

        /// <summary>Scheduling lead so both sources are queued on the audio clock before the blast starts.</summary>
        private const double ScheduleLead = 0.03;

        private AudioSource _start = null!;

        private AudioSource _loop = null!;

        private float _volume;

        private bool _held;

        private float _level = 1f;

        /// <summary>True while any part of the horn is sounding (including the release fade).</summary>
        internal bool Sounding => _start.isPlaying || _loop.isPlaying;

        /// <summary>True while the horn is being held.</summary>
        internal bool Held => _held;

        /// <summary>Builds the two sources (2D, for the player's own truck).</summary>
        internal void Initialize(AudioClip start, AudioClip loop, float volume, float releaseSeconds = 0.3f)
        {
            _volume = volume;

            _releaseSeconds = releaseSeconds;

            _start = NewSource(start, false);

            _loop = NewSource(loop, true);
        }

        private AudioSource NewSource(AudioClip clip, bool loop)
        {
            var source = gameObject.AddComponent<AudioSource>();

            source.clip = clip;

            source.loop = loop;

            source.playOnAwake = false;

            source.spatialBlend = 0f;

            source.volume = _volume;

            return source;
        }

        /// <summary>Blows the horn (call every frame it is held; only the first call starts a blast).</summary>
        internal void Press()
        {
            if (_held)
            {
                return;
            }

            StopNow();

            _held = true;

            _level = 1f;

            _start.volume = _volume;

            _loop.volume = _volume;

            var begin = AudioSettings.dspTime + ScheduleLead;

            _start.PlayScheduled(begin);

            _loop.PlayScheduled(begin + (double)_start.clip.samples / _start.clip.frequency);
        }

        /// <summary>Lets the horn go: it fades out over the release.</summary>
        internal void Release()
        {
            _held = false;
        }

        /// <summary>Silences the horn at once (restart).</summary>
        internal void StopNow()
        {
            _held = false;

            _start.Stop();

            _loop.Stop();

            _level = 1f;
        }

        /// <summary>Runs the release fade.</summary>
        private void Update()
        {
            if (_held || !Sounding)
            {
                return;
            }

            _level -= Time.unscaledDeltaTime / _releaseSeconds;

            if (_level <= 0f)
            {
                StopNow();

                return;
            }

            // Squared for a natural-sounding die-away.
            var volume = _volume * _level * _level;

            _start.volume = volume;

            _loop.volume = volume;
        }
    }
}
