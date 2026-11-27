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
        /// <summary>Width in pixels of the generated map texture (when no pre-rendered map is set). Raise it for a sharper map (more memory); lower it for a coarser one.</summary>
        private const int MapTextureWidth = 1024;

        /// <summary>Brightness of the flat ambient light used for the map render only. Raise it for a brighter, flatter map; lower it for a darker map with more contrast.</summary>
        private const float MapAmbientBrightness = 0.8f;

        /// <summary>Pulse rate of the incident marker, in cycles per second. Raise it for a faster pulse; lower it for a slow breathing pulse.</summary>
        private const float PulseHertz = 1.5f;

        /// <summary>Strength of the incident marker pulse as a fraction of its size. Raise it for a bigger pulse; lower it (or 0) for a steady marker.</summary>
        private const float PulseAmplitude = 0.3f;

        /// <summary>Resolution in pixels of the generated marker sprites (arrow, circle, square). Raise it for crisper markers when zoomed; lower it for blockier ones.</summary>
        private const int MarkerTextureSize = 64;

        /// <summary>How far (map pixels) a station label sits to the right of its square; it does not grow with the zoom. Raise it to move labels away from their squares; lower it to tuck them in.</summary>
        private const float StationLabelOffset = 24f;

        private const string StationLabelPrefix = "Label STN";

        private const string StationMarkerPrefix = "Station";

        private const string StationMarkerSuffix = "Marker";

        /// <summary>The live marker of the unit's own station, which is placed every frame and so is not moved here.</summary>
        private const string LiveStationMarker = "StationMarker";

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

        /// <summary>How much larger than its HUD frame the map image is; the frame shows the part around the unit. Raise it to zoom in (see less of the city, more detail); lower it to see more of the city at once.</summary>
        private const float Zoom = 3f;

        /// <summary>Distance in HUD units kept between a pinned incident marker and the viewport edge when the incident is off the map frame. Raise it to keep the marker further in from the edge; lower it to let it sit at the edge.</summary>
        private const float EdgeMargin = 10f;

        private CinemachineBrain? _brain;

        /// <summary>The whole-screen map (N key); made the first time it is opened.</summary>
        private FullScreenMap? _full;

        /// <summary>The spectator's client (found when spectating), which holds the streamed units.</summary>
        private EngineSpectatorClient? _spectator;

        /// <summary>The computer-driven units' markers; made once the fleet exists (it is made after the map).</summary>
        private FleetMapMarkers? _fleetMarkers;

        /// <summary>The masked frame the map image scrolls inside; created on first use.</summary>
        private RectTransform? _viewport;

        /// <summary>The width the map had in the HUD before it became a viewport; the frame is never narrower.</summary>
        private float _frameMinimumWidth;

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

            UpdateFullScreenMap(keyboard);
        }

        /// <summary>N opens the whole-screen map and N or Esc closes it; it only opens while the city is live (not under a menu).</summary>
        private void UpdateFullScreenMap(Keyboard? keyboard)
        {
            var live = IsThirdPersonLive();

            if (keyboard is not null && live && keyboard.nKey.wasPressedThisFrame)
            {
                _full ??= new FullScreenMap(_mapImage.texture, _mapImage.uvRect, _worldMin, _worldSize, _playerMarker, _incidentMarker, _stationMarker);

                _full.Visible = !_full.Visible;
            }
            else if (keyboard is not null && _full is not null && _full.Visible && keyboard.escapeKey.wasPressedThisFrame)
            {
                _full.Visible = false;
            }

            if (_full is null || !_full.Visible)
            {
                return;
            }

            if (!live)
            {
                _full.Visible = false;

                return;
            }

            // A spectator has no unit of its own: the arrow is the free camera.
            var focus = StartingTruckMenu.CitySpectatorRequested && Camera.main != null ? Camera.main.transform : _player;

            _full.Update(focus, _response.IncidentActive, _incident.position, _stationPosition, MapUnits(), Viewer(), _mapRect);
        }

        /// <summary>True while the third-person follow camera is the live camera.</summary>
        private bool IsThirdPersonLive()
        {
            // A spectator flies the main camera itself, with the brain off, and still wants the map.
            if (StartingTruckMenu.CitySpectatorRequested)
            {
                return true;
            }

            return _brain != null && _brain.ActiveVirtualCamera is CinemachineCamera active && active == _thirdPersonCamera;
        }

        /// <summary>
        /// Puts the map image inside a masked viewport the size it had in the HUD and enlarges the image by <see cref="Zoom"/>, so only
        /// the part around the unit shows and the image can scroll under the mask. Done once, when the HUD layout has given the map a size.
        /// </summary>
        private void EnsureViewport()
        {
            if (_viewport != null || _mapRect.rect.width <= 0f)
            {
                return;
            }

            var original = _mapRect;

            var viewport = new GameObject("MapViewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();

            viewport.SetParent(original.parent, false);

            viewport.SetSiblingIndex(original.GetSiblingIndex());

            viewport.anchorMin = original.anchorMin;

            viewport.anchorMax = original.anchorMax;

            viewport.pivot = original.pivot;

            viewport.anchoredPosition = original.anchoredPosition;

            // The frame fills the panel's width (the HUD gives the panel more room than the whole-map image used); the image keeps the world's aspect.
            var frame = new Vector2(Mathf.Max(original.rect.width, ((RectTransform)_panel.transform).rect.width - (2f * original.anchoredPosition.x)), original.rect.height);

            viewport.sizeDelta = frame;

            _frameMinimumWidth = original.rect.width;

            original.SetParent(viewport, false);

            original.anchorMin = Vector2.zero;

            original.anchorMax = Vector2.zero;

            original.pivot = Vector2.zero;

            var imageWidth = original.rect.width * Zoom;

            original.sizeDelta = new Vector2(imageWidth, imageWidth * _worldSize.y / _worldSize.x);

            original.anchoredPosition = Vector2.zero;

            ScaleStationMarks(original);

            _viewport = viewport;
        }

        /// <summary>Moves the fixed station squares and labels (authored for the map at its original size) to where they belong on the enlarged map.</summary>
        private static void ScaleStationMarks(RectTransform map)
        {
            foreach (RectTransform child in map)
            {
                if (child.name.StartsWith(StationLabelPrefix))
                {
                    var square = child.anchoredPosition - new Vector2(StationLabelOffset, 0f);

                    child.anchoredPosition = (square * Zoom) + new Vector2(StationLabelOffset, 0f);
                }
                else if (child.name != LiveStationMarker && child.name.StartsWith(StationMarkerPrefix) && child.name.EndsWith(StationMarkerSuffix))
                {
                    child.anchoredPosition *= Zoom;
                }
            }
        }

        /// <summary>Scrolls the map so the unit is at the centre of the viewport, without showing past the map's edges.</summary>
        private void CenterOnPlayer()
        {
            var view = _viewport!.rect.size;

            var content = _mapRect.rect.size;

            var offset = (view * 0.5f) - ToMap(_player.position);

            offset.x = Mathf.Clamp(offset.x, view.x - content.x, 0f);

            offset.y = Mathf.Clamp(offset.y, view.y - content.y, 0f);

            _mapRect.anchoredPosition = offset;
        }

        /// <summary>
        /// Where the incident marker goes: on the incident, or, when the incident is outside the viewport, pinned to the viewport's edge in
        /// its direction so it can always be found. Returned in map coordinates.
        /// </summary>
        private Vector2 IncidentMarkerPosition()
        {
            var onMap = ToMap(_incident.position);

            var view = _viewport!.rect.size;

            var inView = onMap + _mapRect.anchoredPosition;

            var half = (view * 0.5f) - new Vector2(EdgeMargin, EdgeMargin);

            var fromCentre = inView - (view * 0.5f);

            if (Mathf.Abs(fromCentre.x) <= half.x && Mathf.Abs(fromCentre.y) <= half.y)
            {
                return onMap;
            }

            var scale = Mathf.Min(half.x / Mathf.Max(Mathf.Abs(fromCentre.x), 0.001f), half.y / Mathf.Max(Mathf.Abs(fromCentre.y), 0.001f));

            return (view * 0.5f) + (fromCentre * scale) - _mapRect.anchoredPosition;
        }

        /// <summary>Scrolls the map to the unit, moves the engine arrow and pulses the incident marker while a call is in progress.</summary>
        private void UpdateMarkers()
        {
            EnsureViewport();

            if (_viewport != null)
            {
                // The HUD layout may resize the panel after the viewport exists, so the frame follows the panel's width.
                var width = Mathf.Max(_frameMinimumWidth, ((RectTransform)_panel.transform).rect.width - (2f * _viewport.anchoredPosition.x));

                if (!Mathf.Approximately(_viewport.sizeDelta.x, width))
                {
                    _viewport.sizeDelta = new Vector2(width, _viewport.sizeDelta.y);
                }

                CenterOnPlayer();
            }

            _playerMarker.rectTransform.anchoredPosition = ToMap(_player.position);

            _playerMarker.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -_player.eulerAngles.y);

            _incidentMarker.gameObject.SetActive(_response.IncidentActive);

            _incidentMarker.rectTransform.anchoredPosition = _viewport != null ? IncidentMarkerPosition() : ToMap(_incident.position);

            var pulse = 1f + PulseAmplitude * Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI * PulseHertz);

            _incidentMarker.rectTransform.localScale = Vector3.one * pulse;

            _stationMarker.rectTransform.anchoredPosition = ToMap(_stationPosition);

            var units = MapUnits();

            if (_fleetMarkers == null && units != null && units.Count > 0 && _stationMarker.sprite != null)
            {
                _fleetMarkers = new FleetMapMarkers(_mapRect, _stationMarker.sprite, Viewer());
            }

            if (units != null)
            {
                _fleetMarkers?.Update(units, ToMap);
            }
        }

        /// <summary>The units the map draws: the fleet's, or, for a spectator, every unit the server streams.</summary>
        private IReadOnlyList<MapUnit>? MapUnits()
        {
            if (StartingTruckMenu.CitySpectatorRequested)
            {
                _spectator ??= FindAnyObjectByType<EngineSpectatorClient>();

                return _spectator != null ? _spectator.MapUnits : null;
            }

            var fleet = _response != null ? _response.Fleet : null;

            return fleet?.MapUnits;
        }

        /// <summary>
        /// What the viewer may see of other units: a spectator's role (everything unless it asked for less), or that of the unit being driven
        /// (the police see everything; fire and EMS see only fire and EMS units).
        /// </summary>
        private ViewerRole Viewer()
        {
            if (StartingTruckMenu.CitySpectatorRequested)
            {
                _spectator ??= FindAnyObjectByType<EngineSpectatorClient>();

                return _spectator != null ? _spectator.Role : ViewerRole.Spectator;
            }

            return ViewerRole.ForUnit(ApparatusProfile.For(_player.gameObject).UnitName);
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

            _full?.Destroy();

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
