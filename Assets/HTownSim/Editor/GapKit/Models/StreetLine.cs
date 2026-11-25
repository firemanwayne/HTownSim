#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Models
{
    /// <summary>One piece of a street's centre line, in scene metres (x, z).</summary>
    internal sealed class StreetLine
    {
        /// <summary>The City's class for the street: <c>MAJOR</c> or <c>LOCAL</c>.</summary>
        internal string Class { get; set; } = string.Empty;

        /// <summary>The street's name.</summary>
        internal string Name { get; set; } = string.Empty;

        /// <summary>The points along the centre line.</summary>
        internal List<Vector2> Points { get; set; } = new List<Vector2>();

        /// <summary>True for a major street (it gets a double line, crosswalks and signals).</summary>
        internal bool IsMajor => Class == "MAJOR";
    }
}
