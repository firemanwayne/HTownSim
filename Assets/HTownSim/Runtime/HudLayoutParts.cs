#nullable enable
using TMPro;
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>The existing HUD objects that <see cref="HudLayout"/> arranges into the left-hand column.</summary>
    internal sealed class HudLayoutParts
    {
        /// <summary>The HUD canvas that owns every part.</summary>
        internal RectTransform Canvas = null!;

        /// <summary>The vertical key list.</summary>
        internal TMP_Text Controls = null!;

        /// <summary>The incident panel background.</summary>
        internal Transform? Panel;

        /// <summary>The incident panel title label.</summary>
        internal Transform? Header;

        /// <summary>The incident status text.</summary>
        internal TMP_Text Status = null!;

        /// <summary>The speed and fire telemetry text.</summary>
        internal TMP_Text Telemetry = null!;

        /// <summary>The "I HIDE" label on the incident panel.</summary>
        internal TMP_Text? HideLabel;

        /// <summary>The city map panel, which shows and hides itself.</summary>
        internal Transform? Map;
    }
}
