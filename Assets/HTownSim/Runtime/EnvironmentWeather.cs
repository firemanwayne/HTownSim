#nullable enable
using HTownSim.Runtime.Models;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace HTownSim.Runtime
{
    /// <summary>Blends local weather, sheltered rainfall, wet roads and placeholder environmental audio.</summary>
    internal sealed class EnvironmentWeather : MonoBehaviour
    {
        /// <summary>Scene dependencies and replaceable audio recordings.</summary>
        [SerializeField] private FireResponse _response = null!;
        [SerializeField] private Light _sun = null!;
        [SerializeField] private ParticleSystem _rain = null!;
        [SerializeField] private TMP_Text _label = null!;
        [SerializeField] private AudioClip _cityClip = null!;
        [SerializeField] private AudioClip _windClip = null!;
        [SerializeField] private AudioClip _rainClip = null!;
        [SerializeField] private AudioClip _dieselClip = null!;
        [SerializeField] private Renderer[] _roads = new Renderer[0];
        /// <summary>Independent gain controls, shared by every source in each category.</summary>
        [SerializeField, Range(0, 1)] private float _ambientVolume = 0.55f;
        [SerializeField, Range(0, 1)] private float _engineVolume = 0.6f;
        /// <summary>Transition and rainfall budgets.</summary>
        private const float TransitionSeconds = 5f;
        private const float RainRate = 950f;
        private const float RainRadius = 20f;
        private const float RainHeight = 13f;
        private const float RainSpeed = 18f;
        private const float ShelterHeight = 4f;
        /// <summary>Runtime state owned by this component.</summary>
        private WeatherPreset _preset;
        private float _cloud;
        private float _rainAmount;
        private float _emissionRemainder;
        /// <summary>The inspector default for the ambient volume, used to normalise the gain handed to other sound layers.</summary>
        private const float DefaultAmbientVolume = 0.55f;
        private bool _ambientMuted;
        private bool _sheltered;
        private float _shelterClock;
        private AudioSource _city = null!;
        private AudioSource _wind = null!;
        private AudioSource _rainAudio = null!;
        private AudioSource _diesel = null!;
        private Material? _sky;
        private Material? _originalSky;
        private Color _skyTint;
        private float _sunIntensity;
        private Color _sunColor;
        private bool _fog;
        private float _fogDensity;
        private Color _fogColor;
        private FogMode _fogMode;
        private AmbientMode _ambientMode;
        private Color _ambientSky;
        private Color _ambientEquator;
        private Color _ambientGround;
        private Color[] _roadColors = new Color[0];
        private float[] _roadSmoothness = new float[0];
        private Material[] _roadMaterials = new Material[0];
        private Camera? _camera;
        /// <summary>Current target weather, exposed to menu code and verification.</summary>
        internal WeatherPreset Preset => _preset;
        /// <summary>Whether a roof currently shelters the player's truck.</summary>
        internal bool IsSheltered => _sheltered;

        /// <summary>Master gain for other ambient sound layers: zero when F7 has muted ambience, lower indoors.</summary>
        /// <summary>How overcast the weather is, 0 (clear) to 1, for the sky.</summary>
        internal float CloudAmount => _cloud;

        /// <summary>How hard it is raining, 0 to 1, for the sky.</summary>
        internal float RainAmount => _rainAmount;

        internal float AmbientGain => _ambientMuted ? 0f : _ambientVolume / DefaultAmbientVolume * (_sheltered ? 0.28f : 1f);

        /// <summary>Caches the authored lighting and creates four reusable audio voices.</summary>
        private void Awake()
        {
            _camera = Camera.main;

            _sunIntensity = _sun.intensity;

            _sunColor = _sun.color;

            _fog = RenderSettings.fog;

            _fogDensity = RenderSettings.fogDensity;

            _fogColor = RenderSettings.fogColor;

            _fogMode = RenderSettings.fogMode;

            _ambientMode = RenderSettings.ambientMode;

            _ambientSky = RenderSettings.ambientSkyColor;

            _ambientEquator = RenderSettings.ambientEquatorColor;

            _ambientGround = RenderSettings.ambientGroundColor;

            _originalSky = RenderSettings.skybox;

            if (_originalSky != null)
            {
                _sky = new Material(_originalSky);

                _skyTint = _sky.HasProperty("_SkyTint") ? _sky.GetColor("_SkyTint") : Color.gray;

                RenderSettings.skybox = _sky;
            }

            // A system with emission disabled stops immediately on Play(), so Emit()ted drops would never simulate.
            var rainEmission = _rain.emission;

            rainEmission.enabled = true;

            rainEmission.rateOverTime = 0;

            _city = Source(_cityClip, transform);

            _wind = Source(_windClip, transform);

            _rainAudio = Source(_rainClip, transform);

            _diesel = Source(_dieselClip, _response.transform);

            _diesel.transform.localPosition = new Vector3(0, 1.5f, 2.1f);

            _diesel.spatialBlend = 1;

            _diesel.minDistance = 12;

            _diesel.maxDistance = 65;

            _diesel.rolloffMode = AudioRolloffMode.Linear;

            // Native-backed objects must not be created in field initializers of a serialized MonoBehaviour.
            // One wet-able copy per distinct road material, shared by every renderer that used it (a copy per renderer would give
            // thousands of materials and break batching). Renderers removed by a rebuild are skipped.
            var copies = new System.Collections.Generic.Dictionary<Material, Material>();

            foreach (var road in _roads)
            {
                if (road == null || road.sharedMaterial == null)
                {
                    continue;
                }

                if (!copies.TryGetValue(road.sharedMaterial, out var copy))
                {
                    copy = new Material(road.sharedMaterial);

                    copies[road.sharedMaterial] = copy;
                }

                road.sharedMaterial = copy;
            }

            _roadMaterials = new Material[copies.Count];

            _roadColors = new Color[copies.Count];

            _roadSmoothness = new float[copies.Count];

            var index = 0;

            foreach (var pair in copies)
            {
                _roadMaterials[index] = pair.Value;

                _roadColors[index] = pair.Key.GetColor("_BaseColor");

                _roadSmoothness[index] = pair.Key.GetFloat("_Smoothness");

                index++;
            }

            SetWeather(WeatherPreset.Clear);
        }

        /// <summary>Creates a looping voice with zero initial volume and no Doppler pitch artifacts.</summary>
        private static AudioSource Source(AudioClip clip, Transform parent)
        {
            var source = new GameObject(clip.name + "Source").AddComponent<AudioSource>();

            source.transform.SetParent(parent, false);

            source.clip = clip;

            source.loop = true;

            source.playOnAwake = false;

            source.volume = 0;

            source.dopplerLevel = 0;

            source.Play();

            return source;
        }

        /// <summary>Selects a target preset without abruptly changing the environment.</summary>
        internal void SetWeather(WeatherPreset preset)
        {
            _preset = preset;

            RefreshLabel();
        }

        /// <summary>Updates keyboard controls, weather and audio while preserving the game's pause state.</summary>
        private void Update()
        {
            if (Keyboard.current?.f6Key.wasPressedThisFrame == true)
            {
                SetWeather((WeatherPreset)(((int)_preset + 1) % 3));
            }

            if (Keyboard.current?.f7Key.wasPressedThisFrame == true)
            {
                _ambientMuted = !_ambientMuted;

                RefreshLabel();
            }

            var paused = _response.IsPaused;

            var delta = paused ? 0 : Time.deltaTime;

            _cloud = Mathf.MoveTowards(_cloud, _preset == WeatherPreset.Clear ? 0 : 1, delta / TransitionSeconds);

            _rainAmount = Mathf.MoveTowards(_rainAmount, _preset == WeatherPreset.Rain ? 1 : 0, delta / TransitionSeconds);

            _shelterClock -= delta;

            if (_shelterClock <= 0)
            {
                _sheltered = Physics.Raycast(_response.transform.position + Vector3.up * ShelterHeight, Vector3.up, 100, ~0, QueryTriggerInteraction.Ignore);

                _shelterClock = 0.2f;
            }

            ApplyLighting();

            UpdateAudio(paused);

            EmitRain(delta);
        }

        /// <summary>Blends scene lighting and road appearance without editing shared materials.</summary>
        private void ApplyLighting()
        {
            var darkness = _cloud * 0.48f + _rainAmount * 0.2f;

            _sun.intensity = _sunIntensity * (1 - darkness);

            _sun.color = Color.Lerp(_sunColor, new Color(0.73f, 0.8f, 0.9f), _cloud);

            RenderSettings.ambientMode = AmbientMode.Trilight;

            RenderSettings.ambientSkyColor = Color.Lerp(_ambientSky, new Color(0.33f, 0.38f, 0.44f), _cloud);

            RenderSettings.ambientEquatorColor = Color.Lerp(_ambientEquator, new Color(0.25f, 0.29f, 0.34f), _cloud);

            RenderSettings.ambientGroundColor = Color.Lerp(_ambientGround, new Color(0.14f, 0.16f, 0.19f), _cloud);

            RenderSettings.fog = _fog || _cloud > 0.01f;

            RenderSettings.fogMode = FogMode.ExponentialSquared;

            RenderSettings.fogDensity = Mathf.Lerp(_fogDensity, 0.0065f, _rainAmount);

            RenderSettings.fogColor = Color.Lerp(_fogColor, new Color(0.38f, 0.43f, 0.49f), _cloud);

            if (_sky != null && _sky.HasProperty("_SkyTint"))
            {
                _sky.SetColor("_SkyTint", Color.Lerp(_skyTint, new Color(0.46f, 0.48f, 0.52f), _cloud));
            }

            for (var i = 0; i < _roadMaterials.Length; i++)
            {
                // Shared copies of the road materials: a MaterialPropertyBlock would replace the road shader's other settings.
                _roadMaterials[i].SetColor("_BaseColor", Color.Lerp(_roadColors[i], _roadColors[i] * 0.62f, _rainAmount));

                _roadMaterials[i].SetFloat("_Smoothness", Mathf.Lerp(_roadSmoothness[i], 0.78f, _rainAmount));
            }
        }

        /// <summary>Applies category gain, shelter muffling, pause muting and dispatch priority.</summary>
        private void UpdateAudio(bool paused)
        {
            var priority = _response.DispatchPlaying ? 0.25f : _response.SirenActive ? 0.55f : 1f;

            var ambience = _ambientMuted || paused ? 0 : _ambientVolume * priority;

            var shelter = _sheltered ? 0.28f : 1f;

            var blend = 1 - Mathf.Exp(-Time.unscaledDeltaTime * 5);

            _city.volume = Mathf.Lerp(_city.volume, ambience * 0.4f * shelter, blend);

            _wind.volume = Mathf.Lerp(_wind.volume, ambience * (0.035f + _cloud * 0.1f) * shelter, blend);

            _rainAudio.volume = Mathf.Lerp(_rainAudio.volume, ambience * _rainAmount * 0.55f * shelter, blend);

            _diesel.volume = Mathf.Lerp(_diesel.volume, paused ? 0 : _engineVolume * priority * (0.16f + Mathf.Clamp01(_response.Speed / 18) * 0.2f), blend);

            _diesel.pitch = Mathf.Lerp(_diesel.pitch, 0.85f + Mathf.Clamp01(_response.Speed / 20) * 0.9f, blend);
        }

        /// <summary>Emits a bounded rain field; roofs block each sampled column and stop falling drops.</summary>
        private void EmitRain(float delta)
        {
            if (_response.IsPaused)
            {
                _rain.Pause();

                return;
            }

            if (!_rain.isPlaying)
            {
                _rain.Play();
            }

            if (_rainAmount <= 0.001f)
            {
                return;
            }

            var center = _camera != null ? _camera.transform.position : _response.transform.position;

            _emissionRemainder += RainRate * _rainAmount * delta;

            var count = Mathf.Min(48, Mathf.FloorToInt(_emissionRemainder));

            _emissionRemainder -= Mathf.Floor(_emissionRemainder);

            for (var i = 0; i < count; i++)
            {
                var offset = Random.insideUnitCircle * RainRadius;

                var position = new Vector3(center.x + offset.x, _response.transform.position.y + RainHeight, center.z + offset.y);

                if (Physics.Raycast(position, Vector3.up, 100, ~0, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                var drop = new ParticleSystem.EmitParams { position = position, velocity = new Vector3(0.5f, -RainSpeed, 0.2f), startLifetime = 1.1f, startSize = Random.Range(0.025f, 0.045f) };

                _rain.Emit(drop, 1);
            }
        }

        /// <summary>Shows preset selection and ambience mute state without modifying dispatch text.</summary>
        private void RefreshLabel()
        {
            _label.text = "F6  WEATHER: " + _preset.ToString().ToUpperInvariant() + "    F7  AMBIENCE: " + (_ambientMuted ? "OFF" : "ON");
        }

        /// <summary>Restores authored settings and releases runtime-only assets after Play Mode.</summary>
        private void OnDestroy()
        {
            if (_sun != null)
            {
                _sun.intensity = _sunIntensity;

                _sun.color = _sunColor;
            }

            RenderSettings.skybox = _originalSky;

            RenderSettings.fog = _fog;

            RenderSettings.fogMode = _fogMode;

            RenderSettings.fogDensity = _fogDensity;

            RenderSettings.fogColor = _fogColor;

            RenderSettings.ambientMode = _ambientMode;

            RenderSettings.ambientSkyColor = _ambientSky;

            RenderSettings.ambientEquatorColor = _ambientEquator;

            RenderSettings.ambientGroundColor = _ambientGround;

            foreach (var material in _roadMaterials)
            {
                if (material != null)
                {
                    Destroy(material);
                }
            }

            if (_sky != null)
            {
                Destroy(_sky);
            }

            if (_diesel != null)
            {
                Destroy(_diesel.gameObject);
            }
        }
    }
}
