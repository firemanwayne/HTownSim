#nullable enable
using System;
using System.Collections.Generic;
using HTownSim.Runtime.Incidents.Managers;
using HTownSim.Runtime.Incidents.Models;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HTownSim.Runtime
{
    /// <summary>A unit to show on the map, whoever drives it or wherever it comes from (the local fleet, or a spectator's streamed units): its name, the object to follow and whether it is out on a call.</summary>
    internal sealed class MapUnit
    {
        internal MapUnit(string name, Transform transform, Func<bool> busy)
        {
            Name = name;

            Transform = transform;

            Busy = busy;
        }

        internal string Name { get; }

        internal Transform Transform { get; }

        internal Func<bool> Busy { get; }
    }

    /// <summary>
    /// The computer-driven units on the city map: a small square for each, coloured by kind (fire red, police blue, EMS white, chief
    /// orange). A unit that is out on a call is bright, larger and carries its short name (E16, A16, P2); a parked one is dim with no
    /// label. The squares sit under the player's own marker and the incident marker.
    /// </summary>
    internal sealed class FleetMapMarkers
    {
        /// <summary>Size (HUD units) of the map marker of a parked unit. Raise it for bigger markers; lower it for smaller ones.</summary>
        private const float ParkedSize = 8f;

        /// <summary>Size (HUD units) of the map marker of a unit out on a call. Raise it so responding units stand out more; lower it to keep them small.</summary>
        private const float OutSize = 13f;

        /// <summary>Font size of a responding unit's map label (E16, A16, P2). Raise it for larger labels; lower it for smaller ones.</summary>
        private const float LabelSize = 11f;

        /// <summary>How far (map pixels) a label sits to the right of its marker. Raise it to move the label away; lower it to tuck it in.</summary>
        private const float LabelOffset = 16f;

        /// <summary>Opacity (0 to 1) of a parked unit's marker. Raise it so parked units show more strongly; lower it to fade them into the map.</summary>
        private const float ParkedAlpha = 0.45f;

        private static readonly Color Fire = new Color(1f, 0.22f, 0.18f, 1f);

        private static readonly Color Police = new Color(0.25f, 0.5f, 1f, 1f);

        private static readonly Color Ems = new Color(0.95f, 0.97f, 1f, 1f);

        private static readonly Color Chief = new Color(1f, 0.6f, 0.15f, 1f);

        private readonly Dictionary<string, Marker> _markers = new Dictionary<string, Marker>();

        private readonly RectTransform _parent;

        private readonly Sprite _square;

        private readonly ViewerRole _viewer;

        /// <summary>
        /// Makes the markers inside <paramref name="parent"/>, the rectangle the map's markers are placed in. A marker is made the first
        /// time its unit is seen in <see cref="Update"/>, and only for a unit the <paramref name="viewer"/> may see (see <see cref="ViewerRole"/>).
        /// </summary>
        internal FleetMapMarkers(RectTransform parent, Sprite square, ViewerRole viewer)
        {
            _parent = parent;

            _square = square;

            _viewer = viewer;
        }

        private Marker Create(MapUnit unit)
        {
            var image = new GameObject("Unit " + unit.Name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();

            image.rectTransform.SetParent(_parent, false);

            image.rectTransform.anchorMin = Vector2.zero;

            image.rectTransform.anchorMax = Vector2.zero;

            image.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            image.sprite = _square;

            image.raycastTarget = false;

            var label = new GameObject("Label " + unit.Name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();

            label.rectTransform.SetParent(_parent, false);

            label.rectTransform.anchorMin = Vector2.zero;

            label.rectTransform.anchorMax = Vector2.zero;

            label.rectTransform.pivot = new Vector2(0f, 0.5f);

            label.rectTransform.sizeDelta = new Vector2(60f, 16f);

            label.text = ShortName(unit.Name);

            label.fontSize = LabelSize;

            label.fontStyle = FontStyles.Bold;

            label.alignment = TextAlignmentOptions.MidlineLeft;

            label.textWrappingMode = TextWrappingModes.NoWrap;

            label.raycastTarget = false;

            // Under the player's marker and the incident marker, which were there first.
            label.rectTransform.SetAsFirstSibling();

            image.rectTransform.SetAsFirstSibling();

            return new Marker(unit, image, label, ColourOf(unit.Name), _viewer.CanSeeUnit(unit.Name));
        }

        /// <summary>Moves every marker to its unit (making the ones it has not seen yet); <paramref name="toMap"/> turns a world position into a map position.</summary>
        internal void Update(IReadOnlyList<MapUnit> units, Func<Vector3, Vector2> toMap)
        {
            foreach (var unit in units)
            {
                if (!_markers.TryGetValue(unit.Name, out var marker))
                {
                    marker = Create(unit);

                    _markers[unit.Name] = marker;
                }

                marker.Image.gameObject.SetActive(marker.Visible);

                if (!marker.Visible)
                {
                    marker.Label.gameObject.SetActive(false);

                    continue;
                }

                var out_ = unit.Busy();

                var position = toMap(unit.Transform.position);

                var colour = marker.Colour;

                colour.a = out_ ? 1f : ParkedAlpha;

                marker.Image.color = colour;

                marker.Image.rectTransform.anchoredPosition = position;

                var size = out_ ? OutSize : ParkedSize;

                marker.Image.rectTransform.sizeDelta = new Vector2(size, size);

                marker.Label.gameObject.SetActive(out_);

                marker.Label.color = marker.Colour;

                marker.Label.rectTransform.anchoredPosition = position + new Vector2(LabelOffset, 0f);
            }
        }

        /// <summary>The colour for a unit's kind.</summary>
        private static Color ColourOf(string unitName)
        {
            return LiveDispatchRule.KindOf(unitName) switch
            {
                LiveUnitKind.Police => Police,
                LiveUnitKind.Ems => Ems,
                LiveUnitKind.Chief => Chief,
                _ => Fire,
            };
        }

        /// <summary>A short name for the map: Engine 16 is E16, Ladder 07 L07, Medic 08 M08, Ambulance 16 A16, Supervisor 16 S16, District Chief 08 DC08, HPD Unit 2 P2.</summary>
        internal static string ShortName(string unitName)
        {
            var space = unitName.LastIndexOf(' ');

            if (space < 0)
            {
                return unitName;
            }

            var number = unitName.Substring(space + 1);

            var kind = unitName.Substring(0, space).ToUpperInvariant();

            return kind switch
            {
                "DISTRICT CHIEF" => "DC" + number,
                "HPD UNIT" => "P" + number,
                _ => kind.Substring(0, 1) + number,
            };
        }

        private sealed class Marker
        {
            internal Marker(MapUnit unit, Image image, TextMeshProUGUI label, Color colour, bool visible)
            {
                Unit = unit;

                Image = image;

                Label = label;

                Colour = colour;

                Visible = visible;
            }

            internal MapUnit Unit { get; }

            /// <summary>False for a unit this viewer's role may not see.</summary>
            internal bool Visible { get; }

            internal Image Image { get; }

            internal TextMeshProUGUI Label { get; }

            internal Color Colour { get; }
        }
    }
}
