#nullable enable
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Shows a map of the city on the left side of the HUD while the third-person camera is live.
    /// With <see cref="_staticMap"/> assigned (the pre-rendered Blender map) that image is shown as is; otherwise a
    /// top-down orthographic camera renders the static city into a texture once. Either way the engine, the
    /// incident and the station are live UI markers drawn over it.
    /// </summary>
    internal sealed class CityMap : MonoBehaviour
    {
        /// <summary>Width in pixels of the generated map texture.</summary>
        private const int MapTextureWidth = 1024;

        /// <summary>Brightness of the flat ambient light used for the map render only.</summary>
        private const float MapAmbientBrightness = 0.8f;

        /// <summary>Pulse rate of the incident marker, in cycles per second.</summary>
        private const float PulseHertz = 1.5f;

        /// <summary>Strength of the incident marker pulse as a fraction of its size.</summary>
        private const float PulseAmplitude = 0.3f;

        /// <summary>Resolution of the generated marker sprites.</summary>
        private const int MarkerTextureSize = 64;

        /// <summary>The whole map panel; hidden when the third-person camera is not live.</summary>
        [SerializeField] private GameObject _panel = null!;

        /// <summary>The top-down camera that renders the city.</summary>
        [SerializeField] private Camera _mapCamera = null!;

        /// <summary>Displays the rendered map texture.</summary>
        [SerializeField] private RawImage _mapImage = null!;

        /// <summary>Rect that markers are positioned inside (matches the map image).</summary>
        [SerializeField] private RectTransform _mapRect = null!;

        /// <summary>Arrow showing the engine position and heading.</summary>
        [SerializeField] private Image _playerMarker = null!;

        /// <summary>Pulsing marker over the incident.</summary>
        [SerializeField] private Image _incidentMarker = null!;

        /// <summary>Marker over the station.</summary>
        [SerializeField] private Image _stationMarker = null!;

        /// <summary>The engine.</summary>
        [SerializeField] private Transform _player = null!;

        /// <summary>The incident location.</summary>
        [SerializeField] private Transform _incident = null!;

        /// <summary>World position of the station apron.</summary>
        [SerializeField] private Vector3 _stationPosition;

        /// <summary>Supplies the remaining fire so the incident marker can clear.</summary>
        [SerializeField] private FireResponse _response = null!;

        /// <summary>The follow camera that counts as third-person mode.</summary>
        [SerializeField] private CinemachineCamera _thirdPersonCamera = null!;

        /// <summary>World X and Z of the map's lower-left corner.</summary>
        [SerializeField] private Vector2 _worldMin;

        /// <summary>World X and Z extent covered by the map.</summary>
        [SerializeField] private Vector2 _worldSize;

        /// <summary>
        /// Optional pre-rendered map covering exactly <see cref="_worldMin"/> to <see cref="_worldMin"/> +
        /// <see cref="_worldSize"/> with +Z at the top. When set, the map camera is never used.
        /// </summary>
        [SerializeField] private Texture2D? _staticMap;

        /// <summary>Generated assets released on destroy.</summary>
        private readonly List<Object> _generated = new List<Object>();

        private CinemachineBrain? _brain;

        private RenderTexture? _texture;

        private bool _userVisible = true;

        private bool _rendered;

        private bool _savedFog;

        private AmbientMode _savedAmbientMode;

        private Color _savedAmbientLight;

        /// <summary>Creates the map texture, marker sprites and render hooks.</summary>
        private void Awake()
        {
            _brain = FindAnyObjectByType<CinemachineBrain>();

            _mapImage.color = Color.white;

            _playerMarker.sprite = CreateSprite(IsInsideArrow);

            _incidentMarker.sprite = CreateSprite(IsInsideCircle);

            _stationMarker.sprite = CreateSprite(IsInsideSquare);

            // Unity's overloaded != (not 'is not null'): an unassigned field is a fake-null object in the editor.
            if (_staticMap != null)
            {
                _mapImage.texture = _staticMap;

                _mapCamera.enabled = false;

                _rendered = true;

                return;
            }

            var height = Mathf.RoundToInt(MapTextureWidth * _worldSize.y / _worldSize.x);

            _texture = new RenderTexture(MapTextureWidth, height, 24) { name = "CityMapTexture" };

            _generated.Add(_texture);

            _mapCamera.targetTexture = _texture;

            _mapImage.texture = _texture;

            _mapCamera.enabled = true;

            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;

            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        /// <summary>Handles the toggle key, panel visibility and live markers.</summary>
        private void Update()
        {
            var keyboard = Keyboard.current;

            if (keyboard is not null && keyboard.mKey.wasPressedThisFrame)
            {
                _userVisible = !_userVisible;
            }

            if (_rendered && _mapCamera.enabled)
            {
                _mapCamera.enabled = false;
            }

            _panel.SetActive(_userVisible && IsThirdPersonLive());

            UpdateMarkers();
        }

        /// <summary>True while the third-person follow camera is the live camera.</summary>
        private bool IsThirdPersonLive()
        {
            return _brain != null && _brain.ActiveVirtualCamera is CinemachineCamera active && active == _thirdPersonCamera;
        }

        /// <summary>Moves the engine arrow, pulses the incident marker and clears it once the fire is out.</summary>
        private void UpdateMarkers()
        {
            _playerMarker.rectTransform.anchoredPosition = ToMap(_player.position);

            _playerMarker.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -_player.eulerAngles.y);

            _incidentMarker.gameObject.SetActive(_response.Heat > 0f);

            _incidentMarker.rectTransform.anchoredPosition = ToMap(_incident.position);

            var pulse = 1f + PulseAmplitude * Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI * PulseHertz);

            _incidentMarker.rectTransform.localScale = Vector3.one * pulse;

            _stationMarker.rectTransform.anchoredPosition = ToMap(_stationPosition);
        }

        /// <summary>Converts a world position to a position inside the map rect.</summary>
        private Vector2 ToMap(Vector3 world)
        {
            var rect = _mapRect.rect;

            return new Vector2((world.x - _worldMin.x) / _worldSize.x * rect.width, (world.z - _worldMin.y) / _worldSize.y * rect.height);
        }

        /// <summary>Lights the map render for readability: no fog and a flat ambient fill.</summary>
        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _mapCamera)
            {
                return;
            }

            _savedFog = RenderSettings.fog;

            _savedAmbientMode = RenderSettings.ambientMode;

            _savedAmbientLight = RenderSettings.ambientLight;

            RenderSettings.fog = false;

            RenderSettings.ambientMode = AmbientMode.Flat;

            RenderSettings.ambientLight = Color.white * MapAmbientBrightness;
        }

        /// <summary>Restores the scene lighting after the map render and marks the render complete.</summary>
        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _mapCamera)
            {
                return;
            }

            RenderSettings.fog = _savedFog;

            RenderSettings.ambientMode = _savedAmbientMode;

            RenderSettings.ambientLight = _savedAmbientLight;

            _rendered = true;
        }

        /// <summary>Releases hooks and generated assets.</summary>
        private void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;

            foreach (var asset in _generated)
            {
                Destroy(asset);
            }
        }

        /// <summary>Creates a white sprite from a shape test over the square [-1, 1].</summary>
        private Sprite CreateSprite(System.Func<float, float, bool> inside)
        {
            var texture = new Texture2D(MarkerTextureSize, MarkerTextureSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };

            var pixels = new Color32[MarkerTextureSize * MarkerTextureSize];

            for (var y = 0; y < MarkerTextureSize; y++)
            {
                for (var x = 0; x < MarkerTextureSize; x++)
                {
                    var u = (x + 0.5f) / MarkerTextureSize * 2f - 1f;

                    var v = (y + 0.5f) / MarkerTextureSize * 2f - 1f;

                    pixels[y * MarkerTextureSize + x] = inside(u, v) ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            }

            texture.SetPixels32(pixels);

            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, MarkerTextureSize, MarkerTextureSize), new Vector2(0.5f, 0.5f), MarkerTextureSize);

            _generated.Add(texture);

            _generated.Add(sprite);

            return sprite;
        }

        /// <summary>Arrow pointing up: a triangle with a notch at the base.</summary>
        private static bool IsInsideArrow(float u, float v)
        {
            var halfWidth = (1f - v) * 0.5f;

            var notch = v < -0.55f && Mathf.Abs(u) < (v + 1f) * 0.6f;

            return v >= -1f && v <= 1f && Mathf.Abs(u) <= halfWidth && !notch;
        }

        /// <summary>A filled circle.</summary>
        private static bool IsInsideCircle(float u, float v)
        {
            return u * u + v * v <= 1f;
        }

        /// <summary>A filled square.</summary>
        private static bool IsInsideSquare(float u, float v)
        {
            return Mathf.Abs(u) <= 1f && Mathf.Abs(v) <= 1f;
        }
    }
}
