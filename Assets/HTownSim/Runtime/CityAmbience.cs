#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Spatial city sound on top of the weather system's ambient bed: engine and tyre noise on the nearest civilian cars,
    /// car horns, distant sirens and birdsong. All clips are synthesized, so no audio files are needed.
    /// Follows the F7 ambience mute and the same shelter muffling as the bed.
    /// </summary>
    internal sealed class CityAmbience : MonoBehaviour
    {
        private const int Rate = 22050;
        private const int CarVoices = 7;
        private const float CarRefreshSeconds = 0.3f;
        private const float CarMaxDistance = 70f;
        private const float CarMinDistance = 5f;
        private const float IdleVolume = 0.12f;
        private const float MovingVolume = 0.5f;
        private const float FullSpeed = 14f;
        private const float HornMinInterval = 5f;
        private const float HornMaxInterval = 14f;
        private const float HornMinRange = 12f;
        private const float HornMaxRange = 60f;
        private const float SirenMinInterval = 25f;
        private const float SirenMaxInterval = 60f;
        private const float SirenDistance = 220f;
        private const float BirdMinInterval = 3f;
        private const float BirdMaxInterval = 9f;
        private const float BirdRange = 35f;
        private const float ShelterGain = 0.28f;
        private const float SirenPriorityGain = 0.5f;

        private TrafficSystem _traffic = null!;
        private EnvironmentWeather? _weather;
        private FireResponse _response = null!;
        private AudioClip _engineClip = null!;
        private AudioClip _hornClip = null!;
        private AudioClip _sirenClip = null!;
        private AudioClip[] _birdClips = new AudioClip[0];
        private readonly AudioSource[] _carSources = new AudioSource[CarVoices];
        private readonly int[] _carAssigned = new int[CarVoices];
        private readonly List<TrafficSystem.NearCar> _near = new List<TrafficSystem.NearCar>();
        private AudioSource _oneShot = null!;
        private AudioSource _sirenSource = null!;
        private float _nextRefresh;
        private float _nextHorn;
        private float _nextSiren;
        private float _nextBird;
        private float _sirenAge;
        private Vector3 _sirenFrom;
        private Vector3 _sirenTo;

        /// <summary>Wires the sources this component drives.</summary>
        internal void Initialize(TrafficSystem traffic, FireResponse response)
        {
            _traffic = traffic;

            _response = response;

            _weather = FindAnyObjectByType<EnvironmentWeather>();

            _engineClip = MakeEngine();

            _hornClip = MakeHorn();

            _sirenClip = MakeDistantSiren();

            _birdClips = new[] { MakeBird(3100f, 4200f, 0.11f, 1), MakeBird(2500f, 3300f, 0.18f, 2), MakeBird(4200f, 3600f, 0.08f, 3) };

            for (var i = 0; i < CarVoices; i++)
            {
                _carSources[i] = NewSource("CarVoice" + i, _engineClip, true);

                _carAssigned[i] = -1;
            }

            _oneShot = NewSource("OneShot", _hornClip, false);

            _sirenSource = NewSource("DistantSiren", _sirenClip, false);

            _sirenSource.maxDistance = 400f;

            _nextHorn = Random.Range(HornMinInterval, HornMaxInterval);

            _nextSiren = Random.Range(10f, SirenMinInterval);

            _nextBird = Random.Range(1f, BirdMinInterval);
        }

        private AudioSource NewSource(string sourceName, AudioClip clip, bool loop)
        {
            var source = new GameObject(sourceName).AddComponent<AudioSource>();

            source.transform.SetParent(transform, false);

            source.clip = clip;

            source.loop = loop;

            source.playOnAwake = false;

            source.spatialBlend = 1f;

            source.rolloffMode = AudioRolloffMode.Linear;

            source.minDistance = CarMinDistance;

            source.maxDistance = CarMaxDistance;

            source.dopplerLevel = 0.4f;

            source.volume = 0f;

            if (loop)
            {
                source.time = Random.value * clip.length;

                source.Play();
            }

            return source;
        }

        /// <summary>Overall gain: zero when muted or paused, quieter in the shelter and under the siren.</summary>
        private float Gain()
        {
            if (Time.timeScale <= 0f)
            {
                return 0f;
            }

            var gain = _weather != null ? _weather.AmbientGain : 1f;

            return gain * (_response.SirenActive ? SirenPriorityGain : 1f) * (_response.DispatchPlaying ? 0.4f : 1f);
        }

        private void Update()
        {
            var gain = Gain();

            if (Time.time >= _nextRefresh)
            {
                _nextRefresh = Time.time + CarRefreshSeconds;

                _traffic.CollectNearbyCars(_response.transform.position, CarVoices * 2, CarMaxDistance, _near);

                Assign();
            }

            UpdateCarVoices(gain);

            UpdateOneShots(gain);
        }

        /// <summary>Keeps each voice on its car while it stays among the nearest, and gives free voices to newly near cars.</summary>
        private void Assign()
        {
            var taken = new bool[_near.Count];

            for (var v = 0; v < CarVoices; v++)
            {
                var slot = _carAssigned[v] < 0 ? -1 : _near.FindIndex(c => c.Id == _carAssigned[v]);

                if (slot >= 0 && slot < CarVoices)
                {
                    taken[slot] = true;
                }
                else
                {
                    _carAssigned[v] = -1;
                }
            }

            for (var n = 0; n < _near.Count && n < CarVoices; n++)
            {
                if (taken[n])
                {
                    continue;
                }

                for (var v = 0; v < CarVoices; v++)
                {
                    if (_carAssigned[v] >= 0)
                    {
                        continue;
                    }

                    _carAssigned[v] = _near[n].Id;

                    _carSources[v].volume = 0f;

                    break;
                }
            }
        }

        private void UpdateCarVoices(float gain)
        {
            var blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 6f);

            for (var v = 0; v < CarVoices; v++)
            {
                var source = _carSources[v];

                var index = _carAssigned[v] < 0 ? -1 : _near.FindIndex(c => c.Id == _carAssigned[v]);

                if (index < 0)
                {
                    source.volume = Mathf.Lerp(source.volume, 0f, blend);

                    continue;
                }

                var car = _near[index];

                source.transform.position = car.Position;

                var speed01 = Mathf.Clamp01(car.Speed / FullSpeed);

                source.volume = Mathf.Lerp(source.volume, Mathf.Lerp(IdleVolume, MovingVolume, speed01) * gain, blend);

                source.pitch = Mathf.Lerp(source.pitch, 0.8f + speed01 * 0.9f, blend);
            }
        }

        private void UpdateOneShots(float gain)
        {
            if (gain <= 0.01f)
            {
                return;
            }

            if (Time.time >= _nextHorn)
            {
                _nextHorn = Time.time + Random.Range(HornMinInterval, HornMaxInterval);

                Honk(gain);
            }

            if (Time.time >= _nextBird)
            {
                _nextBird = Time.time + Random.Range(BirdMinInterval, BirdMaxInterval);

                Chirp(gain);
            }

            if (Time.time >= _nextSiren)
            {
                _nextSiren = Time.time + Random.Range(SirenMinInterval, SirenMaxInterval);

                StartDistantSiren();
            }

            if (_sirenSource.isPlaying)
            {
                _sirenAge += Time.deltaTime;

                _sirenSource.transform.position = Vector3.Lerp(_sirenFrom, _sirenTo, Mathf.Clamp01(_sirenAge / _sirenClip.length));

                _sirenSource.volume = 0.35f * gain;
            }
        }

        /// <summary>One nearby car (not the one that is stopped for the engine's siren) sounds its horn.</summary>
        private void Honk(float gain)
        {
            var candidates = _near.FindAll(c => Vector3.Distance(c.Position, _response.transform.position) is >= HornMinRange and <= HornMaxRange);

            if (candidates.Count == 0)
            {
                return;
            }

            var car = candidates[Random.Range(0, candidates.Count)];

            var horn = new GameObject("Horn").AddComponent<AudioSource>();

            horn.transform.position = car.Position;

            horn.clip = _hornClip;

            horn.spatialBlend = 1f;

            horn.rolloffMode = AudioRolloffMode.Linear;

            horn.minDistance = 8f;

            horn.maxDistance = 120f;

            horn.pitch = Random.Range(0.85f, 1.2f);

            horn.volume = 0.55f * gain;

            horn.Play();

            Destroy(horn.gameObject, _hornClip.length + 0.2f);
        }

        /// <summary>A short birdsong phrase from a random point in the nearby trees.</summary>
        private void Chirp(float gain)
        {
            var offset = Random.insideUnitCircle * BirdRange;

            var source = new GameObject("Bird").AddComponent<AudioSource>();

            source.transform.position = _response.transform.position + new Vector3(offset.x, 6f, offset.y);

            source.clip = _birdClips[Random.Range(0, _birdClips.Length)];

            source.spatialBlend = 1f;

            source.rolloffMode = AudioRolloffMode.Linear;

            source.minDistance = 4f;

            source.maxDistance = 60f;

            source.volume = 0.4f * gain;

            source.pitch = Random.Range(0.9f, 1.15f);

            source.Play();

            Destroy(source.gameObject, source.clip.length + 0.2f);
        }

        /// <summary>A faint emergency siren passing far off, as there is always one somewhere in Houston.</summary>
        private void StartDistantSiren()
        {
            var angle = Random.value * Mathf.PI * 2f;

            var centre = _response.transform.position;

            _sirenFrom = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * SirenDistance;

            _sirenTo = centre + new Vector3(Mathf.Cos(angle + 0.9f), 0f, Mathf.Sin(angle + 0.9f)) * SirenDistance * 0.8f;

            _sirenAge = 0f;

            _sirenSource.transform.position = _sirenFrom;

            _sirenSource.Play();
        }

        // ---------------------------------------------------------------- synthesized clips

        /// <summary>Seamless engine and tyre bed: a low harmonic stack plus periodic band noise built from sines with integer cycle counts.</summary>
        private static AudioClip MakeEngine()
        {
            const float seconds = 2f;

            var samples = new float[(int)(Rate * seconds)];

            var random = new System.Random(5);

            var phases = new float[48];

            for (var k = 0; k < phases.Length; k++)
            {
                phases[k] = (float)random.NextDouble() * Mathf.PI * 2f;
            }

            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)Rate;

                var tone = Mathf.Sin(2f * Mathf.PI * 46f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 92f * t + 0.6f) * 0.3f + Mathf.Sin(2f * Mathf.PI * 138f * t + 1.1f) * 0.14f;

                var roar = 0f;

                for (var k = 0; k < phases.Length; k++)
                {
                    roar += Mathf.Sin(2f * Mathf.PI * (120f + k * 18f) * t + phases[k]);
                }

                samples[i] = (tone * 0.55f + roar / phases.Length * 0.9f) * 0.8f;
            }

            return Build("CityEngine", samples);
        }

        private static AudioClip MakeHorn()
        {
            var samples = new float[(int)(Rate * 0.55f)];

            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)Rate;

                var envelope = Mathf.Clamp01(t * 40f) * Mathf.Clamp01((0.55f - t) * 25f);

                var a = Mathf.Sin(2f * Mathf.PI * 415f * t) + Mathf.Sin(2f * Mathf.PI * 830f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 1245f * t) * 0.15f;

                var b = Mathf.Sin(2f * Mathf.PI * 523f * t) + Mathf.Sin(2f * Mathf.PI * 1046f * t) * 0.35f;

                samples[i] = (a + b) * 0.3f * envelope;
            }

            return Build("CityHorn", samples);
        }

        private static AudioClip MakeDistantSiren()
        {
            const float seconds = 8f;

            var samples = new float[(int)(Rate * seconds)];

            var phase = 0f;

            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)Rate;

                var sweep = Mathf.PingPong(t / 3.5f, 1f);

                var frequency = Mathf.Lerp(650f, 1450f, sweep);

                phase += 2f * Mathf.PI * frequency / Rate;

                var envelope = Mathf.Clamp01(t * 2f) * Mathf.Clamp01((seconds - t) * 2f);

                samples[i] = (Mathf.Sin(phase) + Mathf.Sin(phase * 2f) * 0.25f) * 0.3f * envelope;
            }

            return Build("CityDistantSiren", samples);
        }

        private static AudioClip MakeBird(float from, float to, float chirp, int seed)
        {
            var count = 2 + seed;

            var gap = chirp * 0.7f;

            var samples = new float[(int)(Rate * (count * (chirp + gap) + 0.05f))];

            for (var n = 0; n < count; n++)
            {
                var start = (int)(Rate * n * (chirp + gap));

                var length = (int)(Rate * chirp);

                var phase = 0f;

                for (var i = 0; i < length && start + i < samples.Length; i++)
                {
                    var u = i / (float)length;

                    var frequency = Mathf.Lerp(from, to, u) * (1f + 0.04f * Mathf.Sin(u * 40f));

                    phase += 2f * Mathf.PI * frequency / Rate;

                    samples[start + i] += Mathf.Sin(phase) * Mathf.Sin(u * Mathf.PI) * 0.25f;
                }
            }

            return Build("CityBird" + seed, samples);
        }

        private static AudioClip Build(string clipName, float[] samples)
        {
            var clip = AudioClip.Create(clipName, samples.Length, 1, Rate, false);

            clip.SetData(samples, 0);

            return clip;
        }

        private void OnDestroy()
        {
            foreach (var clip in new[] { _engineClip, _hornClip, _sirenClip })
            {
                if (clip != null)
                {
                    Destroy(clip);
                }
            }

            foreach (var clip in _birdClips)
            {
                if (clip != null)
                {
                    Destroy(clip);
                }
            }
        }
    }
}
