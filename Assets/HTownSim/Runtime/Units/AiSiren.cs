#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// The siren of a computer-driven unit. It plays the same recorded tones the player's siren control has (wail, yelp, pier from
    /// Resources/SirenAudio), switching between them as a real crew does, and it is heard from where the unit is: farther away it is
    /// quieter and duller (a low-pass filter closes with distance), it carries the pitch shift of a vehicle passing by (Doppler), and it
    /// echoes off the buildings, more when the unit is in a street between buildings than in the open.
    /// </summary>
    internal sealed class AiSiren : MonoBehaviour
    {
        private const string SirenFolder = "SirenAudio/";

        /// <summary>Distance (metres) within which the siren is at full volume. Raise it to keep it loud further out; lower it so it starts to fade sooner.</summary>
        private const float MinimumDistance = 15f;

        /// <summary>Distance (metres) beyond which the siren cannot be heard. Raise it so sirens carry further across the city; lower it to keep them local.</summary>
        private const float MaximumDistance = 320f;

        /// <summary>
        /// How strong the pitch shift of a passing vehicle is (0 for none, 1 for the natural amount). Raise it for a more pronounced rise
        /// and fall as a unit passes; lower it for a steadier tone.
        /// </summary>
        private const float DopplerLevel = 1f;

        /// <summary>The low-pass cutoff (Hz) at full volume distance and at the far limit. Raise the far value for a brighter siren in the distance; lower it for a duller, more muffled one.</summary>
        private const float NearCutoff = 22000f;

        private const float FarCutoff = 1600f;

        /// <summary>
        /// How quickly the siren gets duller with distance: the cutoff follows the distance fraction raised to this power. Below 1 it dulls
        /// quickly at first; above 1 it stays bright until the unit is far away.
        /// </summary>
        private const float DullingPower = 0.6f;

        /// <summary>The echo (slap-back off buildings): delay in milliseconds and how much of each echo is repeated. Raise the delay for a bigger space; raise the decay for a longer tail.</summary>
        private const float EchoDelayMilliseconds = 280f;

        private const float EchoDecay = 0.4f;

        /// <summary>
        /// How much echo is heard (0 to 1 of the wet mix) in the open and in a street between buildings. Raise the open value for echo
        /// everywhere; raise the street value so a unit between buildings rings louder; lower them to dry the sound.
        /// </summary>
        private const float EchoOpenMix = 0.08f;

        private const float EchoStreetMix = 0.42f;

        /// <summary>The extra echo (0 to 1) added at the far limit, so a distant siren is more echo than direct sound. Raise it for a washier distance; lower it for a clearer one.</summary>
        private const float EchoDistanceMix = 0.18f;

        /// <summary>The reverb tail: its decay time in seconds and its level (millibels, higher is louder) in the open and in a street. Raise them for a bigger, wetter space; lower them for a dry one.</summary>
        private const float ReverbDecaySeconds = 1.7f;

        private const float ReverbOpenLevel = -2600f;

        private const float ReverbStreetLevel = -700f;

        /// <summary>The reverb's other settings: the room level (millibels) and the delay before it starts (seconds).</summary>
        private const float ReverbRoom = -900f;

        private const float ReverbDelay = 0.04f;

        /// <summary>
        /// How far (metres) to each side the unit looks for buildings to tell a street between buildings from the open, and how often
        /// (seconds). Raise the distance to count buildings further off as enclosing the street; lower it to count only close walls.
        /// </summary>
        private const float StreetProbeDistance = 18f;

        private const float StreetProbeSeconds = 0.25f;

        /// <summary>How quickly the enclosure value follows what the probes find, per second. Raise it for a quick change of echo when the unit turns a corner; lower it for a slow change.</summary>
        private const float EnclosureFollow = 4f;

        /// <summary>Seconds between a change of tone (a unit alternates wail and yelp), the shortest and the longest. Raise them to hold a tone longer; lower them to switch more often.</summary>
        private const float SwitchMinimumSeconds = 8f;

        private const float SwitchMaximumSeconds = 15f;

        /// <summary>The chance (0 to 1) that a change of tone picks the pier tone instead of wail or yelp. Raise it for more pier; lower it (or 0) for none.</summary>
        private const float PierChance = 0.15f;

        /// <summary>The recorded tones of the unit's kind (see UnitBehavior.SirenTones): the first two alternate, a third is the rarer pier tone.</summary>
        private AudioClip?[] _clips = new AudioClip?[0];

        private AudioSource _source = null!;

        private AudioLowPassFilter _lowPass = null!;

        private AudioEchoFilter _echo = null!;

        private AudioReverbFilter _reverb = null!;

        private AudioListener? _listener;

        private bool _playing;

        private int _tone;

        private float _nextSwitch;

        private float _nextProbe;

        private float _enclosure;

        private float _enclosureTarget;

        private void Awake()
        {
            LoadTones(UnitBehavior.Engine.SirenTones);

            _source = gameObject.AddComponent<AudioSource>();

            _source.playOnAwake = false;

            _source.loop = true;

            _source.volume = UnitBehavior.Engine.AiSirenVolume;

            _source.spatialBlend = 1f;

            _source.rolloffMode = AudioRolloffMode.Logarithmic;

            _source.minDistance = MinimumDistance;

            _source.maxDistance = MaximumDistance;

            _source.dopplerLevel = DopplerLevel;

            // The filters work in the order they are added: dulling first, then the echo, then the reverb tail.
            _lowPass = gameObject.AddComponent<AudioLowPassFilter>();

            _lowPass.cutoffFrequency = NearCutoff;

            _echo = gameObject.AddComponent<AudioEchoFilter>();

            _echo.delay = EchoDelayMilliseconds;

            _echo.decayRatio = EchoDecay;

            _echo.wetMix = EchoOpenMix;

            _echo.dryMix = 1f;

            _reverb = gameObject.AddComponent<AudioReverbFilter>();

            _reverb.reverbPreset = AudioReverbPreset.User;

            _reverb.decayTime = ReverbDecaySeconds;

            _reverb.room = ReverbRoom;

            _reverb.reverbDelay = ReverbDelay;

            _reverb.reverbLevel = ReverbOpenLevel;

            _tone = Random.Range(0, 2);
        }

        /// <summary>Gives the siren the tones and volume of the unit's kind.</summary>
        internal void Configure(UnitBehavior behavior)
        {
            LoadTones(behavior.SirenTones);

            _source.volume = behavior.AiSirenVolume;
        }

        private void LoadTones(string[] tones)
        {
            _clips = new AudioClip?[tones.Length];

            for (var i = 0; i < tones.Length; i++)
            {
                _clips[i] = Resources.Load<AudioClip>(SirenFolder + tones[i]);
            }
        }

        /// <summary>Starts or stops the siren.</summary>
        internal void SetPlaying(bool on)
        {
            if (on == _playing)
            {
                return;
            }

            _playing = on;

            if (!on)
            {
                _source.Stop();

                return;
            }

            _nextSwitch = Time.time + Random.Range(SwitchMinimumSeconds, SwitchMaximumSeconds);

            PlayTone();
        }

        private void Update()
        {
            if (!_playing)
            {
                return;
            }

            if (Time.time >= _nextSwitch)
            {
                _nextSwitch = Time.time + Random.Range(SwitchMinimumSeconds, SwitchMaximumSeconds);

                _tone = _clips.Length > 2 && Random.value < PierChance ? 2 : (_tone == 0 ? 1 : 0);

                PlayTone();
            }

            if (Time.time >= _nextProbe)
            {
                _nextProbe = Time.time + StreetProbeSeconds;

                _enclosureTarget = Enclosure();
            }

            _enclosure = Mathf.MoveTowards(_enclosure, _enclosureTarget, EnclosureFollow * Time.deltaTime);

            ApplyDistance();
        }

        private void PlayTone()
        {
            var clip = _tone < _clips.Length ? _clips[_tone] : null;

            if (clip == null)
            {
                return;
            }

            _source.clip = clip;

            _source.Play();
        }

        /// <summary>Dulls the sound and thickens the echo and reverb with distance and with the walls around the unit.</summary>
        private void ApplyDistance()
        {
            if (_listener == null)
            {
                _listener = FindAnyObjectByType<AudioListener>();
            }

            var far01 = 0f;

            if (_listener != null)
            {
                far01 = Mathf.InverseLerp(MinimumDistance, MaximumDistance, Vector3.Distance(_listener.transform.position, transform.position));
            }

            _lowPass.cutoffFrequency = Mathf.Lerp(NearCutoff, FarCutoff, Mathf.Pow(far01, DullingPower));

            _echo.wetMix = Mathf.Clamp01(Mathf.Lerp(EchoOpenMix, EchoStreetMix, _enclosure) + (far01 * EchoDistanceMix));

            _echo.dryMix = Mathf.Lerp(1f, 0.7f, far01);

            _reverb.reverbLevel = Mathf.Lerp(ReverbOpenLevel, ReverbStreetLevel, _enclosure);
        }

        /// <summary>0 in the open to 1 with buildings close on both sides: the share of the two side probes that hit a solid wall.</summary>
        private float Enclosure()
        {
            var origin = transform.position + (Vector3.up * 1.5f);

            var hits = 0;

            foreach (var side in new[] { transform.right, -transform.right })
            {
                if (Physics.Raycast(origin, side, out var hit, StreetProbeDistance, ~0, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform) && hit.collider.name != "Streets")
                {
                    hits++;
                }
            }

            return hits * 0.5f;
        }
    }
}
