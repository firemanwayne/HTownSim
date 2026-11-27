#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// The horn of a computer-driven unit, heard from where the unit is: the air horn for an engine or a ladder, the electric horn for the
    /// others (as the unit's profile says, the same as the player's truck of the kind). The unit asks for a number of blasts
    /// (<see cref="Honk"/>); a spectator's copy of the unit just follows the server's horn with <see cref="SetHeld"/>.
    /// </summary>
    internal sealed class AiHorn : MonoBehaviour
    {
        private const string SirenFolder = "SirenAudio/";

        private const string HornResource = "Horn";

        private const string AirHornStartResource = "AirHornStart";

        private const string AirHornLoopResource = "AirHornLoop";

        /// <summary>How long (seconds) each blast of a honk lasts. Raise it for longer blasts; lower it for short toots.</summary>
        private const float BlastSeconds = 0.45f;

        /// <summary>The pause (seconds) between the blasts of a honk. Raise it for a more spaced honk; lower it for a run-together one.</summary>
        private const float GapSeconds = 0.2f;

        /// <summary>Distance (metres) within which the electric horn is at full volume, and beyond which it cannot be heard (the same as the unit's siren).</summary>
        private const float MinimumDistance = 15f;

        private const float MaximumDistance = 320f;

        private AirHorn? _air;

        private AudioSource? _electric;

        private bool _held;

        private int _blastsLeft;

        private bool _blastOn;

        private float _timer;

        /// <summary>True while the horn is sounding (a spectator copies this).</summary>
        internal bool Sounding => _held;

        /// <summary>Gives the unit its kind's horn: the air horn when <paramref name="airHorn"/> and its recordings are present, else the electric horn.</summary>
        internal void Configure(bool airHorn, float volume)
        {
            var start = Resources.Load<AudioClip>(SirenFolder + AirHornStartResource);

            var loop = Resources.Load<AudioClip>(SirenFolder + AirHornLoopResource);

            if (airHorn && start != null && loop != null)
            {
                _air = gameObject.AddComponent<AirHorn>();

                _air.Initialize(start, loop, volume, 0.3f, true);

                return;
            }

            var clip = Resources.Load<AudioClip>(SirenFolder + HornResource);

            if (clip == null)
            {
                return;
            }

            _electric = gameObject.AddComponent<AudioSource>();

            _electric.clip = clip;

            _electric.loop = true;

            _electric.playOnAwake = false;

            _electric.volume = volume;

            _electric.spatialBlend = 1f;

            _electric.rolloffMode = AudioRolloffMode.Logarithmic;

            _electric.minDistance = MinimumDistance;

            _electric.maxDistance = MaximumDistance;
        }

        /// <summary>Sounds the horn in short blasts (one for a vehicle in the way, two for an intersection); ignored while it is already sounding.</summary>
        internal void Honk(int blasts)
        {
            if (_blastsLeft > 0 || _held)
            {
                return;
            }

            _blastsLeft = blasts;

            _blastOn = false;

            _timer = 0f;
        }

        /// <summary>Holds the horn down or lets it go.</summary>
        internal void SetHeld(bool held)
        {
            if (held == _held)
            {
                return;
            }

            _held = held;

            if (held)
            {
                if (_electric != null && !_electric.isPlaying)
                {
                    _electric.Play();
                }
            }
            else
            {
                _air?.Release();

                if (_electric != null)
                {
                    _electric.Stop();
                }
            }
        }

        private void Update()
        {
            if (_held && _air != null)
            {
                _air.Press();
            }

            if (_blastsLeft <= 0)
            {
                return;
            }

            _timer -= Time.deltaTime;

            if (_blastOn)
            {
                if (_timer <= 0f)
                {
                    SetHeld(false);

                    _blastOn = false;

                    _blastsLeft--;

                    _timer = GapSeconds;
                }
            }
            else if (_timer <= 0f)
            {
                SetHeld(true);

                _blastOn = true;

                _timer = BlastSeconds;
            }
        }

        private void OnDisable()
        {
            SetHeld(false);
        }
    }
}
