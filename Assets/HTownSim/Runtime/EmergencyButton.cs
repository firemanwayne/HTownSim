#nullable enable
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HTownSim.Runtime
{
    /// <summary>One backlit key on the <see cref="EmergencyHud"/> controller head.</summary>
    internal sealed class EmergencyButton
    {
        /// <summary>The button's rectangle, used for mouse hit tests.</summary>
        internal RectTransform Rect = null!;

        /// <summary>The key face: frosted and pale when off, dark when active or pressed.</summary>
        internal Image Face = null!;

        /// <summary>The coloured bezel around the face: green when active, red while pressed.</summary>
        internal Image Rim = null!;

        /// <summary>The soft glow around the bezel.</summary>
        internal Outline Glow = null!;

        /// <summary>The small key hint under the label.</summary>
        internal TMP_Text Hint = null!;

        internal TMP_Text Label = null!;

        /// <summary>True while the function this key controls is active.</summary>
        internal Func<bool> IsLit = () => false;

        /// <summary>Runs when the key is pressed by mouse.</summary>
        internal Action OnPress = () => { };

        /// <summary>Runs when a momentary key (the horn) is released; null for latching keys.</summary>
        internal Action? OnRelease;

        /// <summary>True while something other than the mouse or keyboard holds the key down, such as the horn sounding.</summary>
        internal Func<bool> ExtraPressed = () => false;

        /// <summary>The keyboard key that also operates this button; it shows pressed while held.</summary>
        internal UnityEngine.InputSystem.Key Key;

        /// <summary>Current blend from off to active (green), eased each frame.</summary>
        internal float Active;

        /// <summary>Current blend to the pressed (red) look, eased each frame.</summary>
        internal float Pressed;
    }
}
