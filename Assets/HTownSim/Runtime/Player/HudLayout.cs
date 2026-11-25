#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Arranges the left-hand HUD with layout groups: a stack with the incident panel above the city map on the far left, and
    /// beside it the key list, which only shows while its key is held. A hidden panel is inactive, so the layout closes the gap and the others move up.
    /// </summary>
    internal static class HudLayout
    {
        private const float Left = 28f;
        private const float Top = -28f;
        private const float ContainerWidth = 1040f;
        private const float ContainerHeight = 870f;
        private const float ColumnGap = 22f;
        private const float StackGap = 14f;
        private const float ControlsWidth = 345f;
        private const float ControlsHeight = 522f;
        private const float IncidentWidth = 620f;
        private const float IncidentHeight = 295f;

        /// <summary>The incident panel is drawn at this fraction of its designed size (it took too much of the screen); the stack beneath it follows.</summary>
        private const float IncidentScale = 0.72f;
        private const float MapWidth = 384f;
        private const float MapHeight = 556f;

        /// <summary>Builds the container and stack, moves the HUD parts into them, and returns the incident group so it can be shown or hidden as one.</summary>
        internal static GameObject Build(HudLayoutParts parts)
        {
            var container = NewRect("LeftHud", parts.Canvas);

            container.anchorMin = new Vector2(0f, 1f);

            container.anchorMax = new Vector2(0f, 1f);

            container.pivot = new Vector2(0f, 1f);

            container.anchoredPosition = new Vector2(Left, Top);

            container.sizeDelta = new Vector2(ContainerWidth, ContainerHeight);

            var row = container.gameObject.AddComponent<HorizontalLayoutGroup>();

            Configure(row, ColumnGap);

            // The key list is shown only while its key is held, so it sits outside the row, beside the incident panel and map
            // (which therefore take the far-left spot).
            var controls = parts.Controls.rectTransform;

            controls.SetParent(container, false);

            var ignore = controls.GetComponent<LayoutElement>() ?? controls.gameObject.AddComponent<LayoutElement>();

            ignore.ignoreLayout = true;

            controls.anchorMin = new Vector2(0f, 1f);

            controls.anchorMax = new Vector2(0f, 1f);

            controls.pivot = new Vector2(0f, 1f);

            controls.anchoredPosition = new Vector2((IncidentWidth * IncidentScale) + ColumnGap, 0f);

            controls.sizeDelta = new Vector2(ControlsWidth, ControlsHeight);

            var stackRect = NewRect("HudStack", container);

            var stack = stackRect.gameObject.AddComponent<VerticalLayoutGroup>();

            Configure(stack, StackGap);

            var incident = NewRect("IncidentHud", stackRect);

            var element = incident.gameObject.AddComponent<LayoutElement>();

            element.preferredWidth = IncidentWidth * IncidentScale;

            element.preferredHeight = IncidentHeight * IncidentScale;

            // The parts keep their designed size inside a scaled frame, so the whole panel shrinks together.
            var scaled = NewRect("IncidentScaled", incident);

            scaled.anchorMin = new Vector2(0f, 1f);

            scaled.anchorMax = new Vector2(0f, 1f);

            scaled.pivot = new Vector2(0f, 1f);

            scaled.anchoredPosition = Vector2.zero;

            scaled.sizeDelta = new Vector2(IncidentWidth, IncidentHeight);

            scaled.localScale = Vector3.one * IncidentScale;

            Place(parts.Panel, scaled, new Vector2(0f, 0f));

            Place(parts.Header, scaled, new Vector2(22f, -14f));

            Place(parts.Status.transform, scaled, new Vector2(22f, -56f));

            Place(parts.Telemetry.transform, scaled, new Vector2(22f, -207f));

            Place(parts.HideLabel != null ? parts.HideLabel.transform : null, scaled, new Vector2(IncidentWidth - 110f, -18f));

            if (parts.Map != null)
            {
                Fit((RectTransform)parts.Map, stackRect, MapWidth, MapHeight);
            }

            return incident.gameObject;
        }

        private static void Configure(HorizontalOrVerticalLayoutGroup group, float spacing)
        {
            group.spacing = spacing;

            group.childAlignment = TextAnchor.UpperLeft;

            group.childControlWidth = true;

            group.childControlHeight = true;

            group.childForceExpandWidth = false;

            group.childForceExpandHeight = false;
        }

        /// <summary>Reparents a rectangle into a layout group with a fixed preferred size.</summary>
        private static void Fit(RectTransform rect, Transform parent, float width, float height)
        {
            rect.SetParent(parent, false);

            var element = rect.GetComponent<LayoutElement>();

            if (element == null)
            {
                element = rect.gameObject.AddComponent<LayoutElement>();
            }

            element.minWidth = width;

            element.preferredWidth = width;

            element.preferredHeight = height;

            element.minHeight = height;
        }

        /// <summary>Moves a top-left anchored part inside the incident group at a fixed offset and makes sure it is active.</summary>
        private static void Place(Transform? part, RectTransform parent, Vector2 position)
        {
            if (part is null)
            {
                return;
            }

            part.SetParent(parent, false);

            ((RectTransform)part).anchoredPosition = position;

            part.gameObject.SetActive(true);
        }

        private static RectTransform NewRect(string objectName, Transform parent)
        {
            var rect = new GameObject(objectName, typeof(RectTransform)).GetComponent<RectTransform>();

            rect.SetParent(parent, false);

            return rect;
        }
    }
}
