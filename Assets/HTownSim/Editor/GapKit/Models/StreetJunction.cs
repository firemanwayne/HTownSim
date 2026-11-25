#nullable enable
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Models
{
    /// <summary>A place where three or more streets meet.</summary>
    internal sealed class StreetJunction
    {
        /// <summary>The middle of the junction (x, z).</summary>
        internal Vector2 Position { get; set; }

        /// <summary>The streets that arrive here, one entry per direction.</summary>
        internal List<StreetLeg> Legs { get; set; } = new List<StreetLeg>();

        /// <summary>How many legs are major streets.</summary>
        internal int MajorLegs => Legs.Count(leg => leg.IsMajor);

        /// <summary>True when major streets cross or meet here, which gets signals.</summary>
        internal bool Signalised => MajorLegs >= 3;

        /// <summary>True when at least one leg is a major street, which gets crosswalks.</summary>
        internal bool HasMajor => MajorLegs >= 1;

        /// <summary>Half the width of the widest street here: how far the crossing is from the middle.</summary>
        internal float HalfSize => Legs.Max(leg => leg.Width) / 2f;
    }
}
