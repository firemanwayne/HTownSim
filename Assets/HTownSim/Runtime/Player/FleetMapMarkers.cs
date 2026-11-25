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
    /// <summary>
    /// The computer-driven units on the city map: a small square for each, coloured by kind (fire red, police blue, EMS white, chief
    /// orange). A unit that is out on a call is bright, larger and carries its short name (E16, A16, P2); a parked one is dim with no
    /// label. The squares sit under the player's own marker and the incident marker.
    /// </summary>
    internal sealed class FleetMapMarkers
    {
        /// <summary>The marker sizes (HUD units) of a parked and of a responding unit, and the labels' size and offset from their square.</summary>
        private const float ParkedSize = 8f;

        private const float OutSize = 13f;

        private const float LabelSize = 11f;

        private const float LabelOffset = 16f;

        private const float ParkedAlpha = 0.45f;

        private static readonly Color Fire = new Color(1f, 0.22f, 0.18f, 1f);

        private static readonly Color Police = new Color(0.25f, 0.5f, 1f, 1f);

        private static readonly Color Ems = new Color(0.95f, 0.97f, 1f, 1f);

        private static readonly Color Chief = new Color(1f, 0.6f, 0.15f, 1f);

        private readonly List<Marker> _markers = new List<Marker>();

        /// <summary>Makes a marker for each of the fleet's units inside <paramref name="parent"/>, the rectangle the map's markers are placed in.</summary>
        internal FleetMapMarkers(AiFleet fleet, RectTransform parent, Sprite square)
        {
            foreach (var unit in fleet.Units)
            {
                var image = new GameObject("Unit " + unit.UnitName, typeof(RectTransform), typeof(Image)).GetComponent<Image>();

                image.rectTransform.SetParent(parent, false);

                image.rectTransform.anchorMin = Vector2.zero;

                image.rectTransform.anchorMax = Vector2.zero;

                image.rectTransform.pivot = new Vector2(0.5f, 0.5f);

                image.sprite = square;

                image.raycastTarget = false;

                var label = new GameObject("Label " + unit.UnitName, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();

                label.rectTransform.SetParent(parent, false);

                label.rectTransform.anchorMin = Vector2.zero;

                label.rectTransform.anchorMax = Vector2.zero;

                label.rectTransform.pivot = new Vector2(0f, 0.5f);

                label.rectTransform.sizeDelta = new Vector2(60f, 16f);

                label.text = ShortName(unit.UnitName);

                label.fontSize = LabelSize;

                label.fontStyle = FontStyles.Bold;

                label.alignment = TextAlignmentOptions.MidlineLeft;

                label.textWrappingMode = TextWrappingModes.NoWrap;

                label.raycastTarget = false;

                _markers.Add(new Marker(unit, image, label, ColourOf(unit.UnitName)));
            }

            // Under the player's marker and the incident marker, which were there first.
            foreach (var marker in _markers)
            {
                marker.Label.rectTransform.SetAsFirstSibling();

                marker.Image.rectTransform.SetAsFirstSibling();
            }
        }

        /// <summary>Moves every marker to its unit; <paramref name="toMap"/> turns a world position into a map position.</summary>
        internal void Update(Func<Vector3, Vector2> toMap)
        {
            foreach (var marker in _markers)
            {
                var out_ = marker.Unit.Busy;

                var position = toMap(marker.Unit.transform.position);

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
            internal Marker(AiUnit unit, Image image, TextMeshProUGUI label, Color colour)
            {
                Unit = unit;

                Image = image;

                Label = label;

                Colour = colour;
            }

            internal AiUnit Unit { get; }

            internal Image Image { get; }

            internal TextMeshProUGUI Label { get; }

            internal Color Colour { get; }
        }
    }
}
