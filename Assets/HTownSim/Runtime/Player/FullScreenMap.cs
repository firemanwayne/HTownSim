#nullable enable
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HTownSim.Runtime
{
    /// <summary>
    /// The whole city map over the whole screen (the N key): the same map image as the HUD map, fitted to the screen with the world's
    /// proportions, with the unit's arrow, the call, every station and the computer-driven units drawn on it. It is its own overlay
    /// canvas, made the first time it is opened, and does nothing to the HUD map under it.
    /// </summary>
    internal sealed class FullScreenMap
    {
        /// <summary>Size (canvas units, on a 1920 by 1080 canvas) of the unit's arrow, the call's circle and the player's station square. Raise them to make the markers bigger on the full map; lower them for finer ones.</summary>
        private const float ArrowSize = 30f;

        private const float CallSize = 38f;

        private const float StationSize = 18f;

        /// <summary>The space (canvas units) kept clear of the screen edge around the map, with the extra room at the top for the title. Raise a value to shrink the map and leave more border; lower it to give the map more of the screen.</summary>
        private const float Margin = 30f;

        private const float TitleRoom = 70f;

        private static readonly Color Backdrop = new Color(0.02f, 0.04f, 0.06f, 0.97f);

        private readonly GameObject _root;

        private readonly RectTransform _map;

        private readonly Image _player;

        private readonly Image _call;

        private readonly Image _station;

        private readonly Sprite _square;

        private readonly Vector2 _worldMin;

        private readonly Vector2 _worldSize;

        private FleetMapMarkers? _fleet;

        private bool _stationsCopied;

        /// <summary>Builds the (hidden) overlay. The marker sprites and colours are the HUD map's own.</summary>
        internal FullScreenMap(Texture texture, Rect textureRect, Vector2 worldMin, Vector2 worldSize, Image hudPlayer, Image hudCall, Image hudStation)
        {
            _worldMin = worldMin;

            _worldSize = worldSize;

            _square = hudStation.sprite;

            _root = new GameObject("FullScreenMap", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));

            var canvas = _root.GetComponent<Canvas>();

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            canvas.sortingOrder = 400;

            var scaler = _root.GetComponent<CanvasScaler>();

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            scaler.referenceResolution = new Vector2(1920f, 1080f);

            scaler.matchWidthOrHeight = 0.5f;

            var back = NewChild("Backdrop", _root.transform).gameObject.AddComponent<Image>();

            Stretch(back.rectTransform, Vector2.zero, Vector2.zero);

            back.color = Backdrop;

            back.raycastTarget = false;

            var frame = NewChild("Frame", _root.transform);

            Stretch(frame, new Vector2(Margin, Margin), new Vector2(-Margin, -(Margin + TitleRoom)));

            var mapGo = NewChild("Map", frame);

            _map = mapGo;

            var image = mapGo.gameObject.AddComponent<RawImage>();

            image.texture = texture;

            image.uvRect = textureRect;

            image.raycastTarget = false;

            var fitter = mapGo.gameObject.AddComponent<AspectRatioFitter>();

            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

            fitter.aspectRatio = worldSize.x / worldSize.y;

            _call = NewMarker("Call", hudCall, CallSize);

            _station = NewMarker("Station", hudStation, StationSize);

            _player = NewMarker("You", hudPlayer, ArrowSize);

            _player.rectTransform.SetAsLastSibling();

            var title = NewText("Title", "CITY MAP", 40f, TextAlignmentOptions.TopLeft);

            TopRow(title.rectTransform);

            var hint = NewText("Hint", "N or Esc closes     arrow: you     circle: the call     red: fire     white: EMS     blue: police     orange: chief", 22f, TextAlignmentOptions.TopRight);

            TopRow(hint.rectTransform);

            _root.SetActive(false);
        }

        /// <summary>True while the map is on screen.</summary>
        internal bool Visible
        {
            get => _root.activeSelf;

            set => _root.SetActive(value);
        }

        /// <summary>
        /// Moves the markers. <paramref name="hudMap"/> is the HUD map's image rectangle, whose station squares and labels are copied
        /// across the first time; <paramref name="units"/> are the units to draw (null until they exist), of which a viewer who
        /// <paramref name="seesPolice"/> sees all and any other sees only fire and EMS.
        /// </summary>
        internal void Update(Transform focus, bool callActive, Vector3 callPosition, Vector3 stationPosition, IReadOnlyList<MapUnit>? units, bool seesPolice, RectTransform hudMap)
        {
            if (_map.rect.width <= 0f)
            {
                return;
            }

            CopyStations(hudMap);

            _player.rectTransform.anchoredPosition = ToMap(focus.position);

            _player.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -focus.eulerAngles.y);

            _call.gameObject.SetActive(callActive);

            _call.rectTransform.anchoredPosition = ToMap(callPosition);

            _call.rectTransform.localScale = Vector3.one * (1f + (0.3f * Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI * 1.5f)));

            _station.rectTransform.anchoredPosition = ToMap(stationPosition);

            if (_fleet == null && units != null && units.Count > 0 && _square != null)
            {
                _fleet = new FleetMapMarkers(_map, _square, seesPolice);
            }

            if (units != null)
            {
                _fleet?.Update(units, ToMap);
            }

            _player.rectTransform.SetAsLastSibling();
        }

        /// <summary>Copies the HUD map's authored station squares and labels onto the big map, scaled from the HUD image's size to this one.</summary>
        private void CopyStations(RectTransform hudMap)
        {
            if (_stationsCopied || hudMap.rect.width <= 0f)
            {
                return;
            }

            _stationsCopied = true;

            var scale = _map.rect.width / hudMap.rect.width;

            foreach (RectTransform child in hudMap)
            {
                var isLabel = child.name.StartsWith("Label STN", StringComparison.Ordinal);

                var isStation = child.name != "StationMarker" && child.name.StartsWith("Station", StringComparison.Ordinal) && child.name.EndsWith("Marker", StringComparison.Ordinal);

                if (!isLabel && !isStation)
                {
                    continue;
                }

                var copy = UnityEngine.Object.Instantiate(child, _map, false);

                copy.anchoredPosition = child.anchoredPosition * scale;

                copy.SetAsFirstSibling();
            }
        }

        /// <summary>Converts a world position to a position inside the map rectangle (from its lower-left corner).</summary>
        private Vector2 ToMap(Vector3 world)
        {
            var rect = _map.rect;

            return new Vector2((world.x - _worldMin.x) / _worldSize.x * rect.width, (world.z - _worldMin.y) / _worldSize.y * rect.height);
        }

        /// <summary>Removes the overlay.</summary>
        internal void Destroy()
        {
            UnityEngine.Object.Destroy(_root);
        }

        private Image NewMarker(string name, Image like, float size)
        {
            var image = NewChild(name, _map).gameObject.AddComponent<Image>();

            image.sprite = like.sprite;

            image.color = like.color;

            image.raycastTarget = false;

            image.rectTransform.anchorMin = Vector2.zero;

            image.rectTransform.anchorMax = Vector2.zero;

            image.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            image.rectTransform.sizeDelta = new Vector2(size, size);

            return image;
        }

        private TextMeshProUGUI NewText(string name, string text, float size, TextAlignmentOptions alignment)
        {
            var label = NewChild(name, _root.transform).gameObject.AddComponent<TextMeshProUGUI>();

            label.text = text;

            label.fontSize = size;

            label.fontStyle = FontStyles.Bold;

            label.alignment = alignment;

            label.color = Color.white;

            label.textWrappingMode = TextWrappingModes.NoWrap;

            label.raycastTarget = false;

            return label;
        }

        private static RectTransform NewChild(string name, Transform parent)
        {
            var child = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();

            child.SetParent(parent, false);

            return child;
        }

        /// <summary>Lays a text out along the top of the screen, inside the margins, in the room above the map.</summary>
        private static void TopRow(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 1f);

            rect.anchorMax = new Vector2(1f, 1f);

            rect.pivot = new Vector2(0.5f, 1f);

            rect.anchoredPosition = new Vector2(0f, -Margin * 0.6f);

            rect.sizeDelta = new Vector2(-2f * Margin, TitleRoom);
        }

        private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;

            rect.anchorMax = Vector2.one;

            rect.offsetMin = offsetMin;

            rect.offsetMax = offsetMax;
        }
    }
}
