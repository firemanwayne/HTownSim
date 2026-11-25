#nullable enable

namespace HTownSim.Runtime
{
    /// <summary>One card on the start screen: a station or an apparatus.</summary>
    internal readonly struct MenuCard
    {
        /// <summary>Creates a card.</summary>
        internal MenuCard(string title, string description, string footer, bool enabled)
        {
            Title = title;

            Description = description;

            Footer = footer;

            Enabled = enabled;
        }

        /// <summary>The card's name.</summary>
        internal string Title { get; }

        /// <summary>What it is, in a sentence.</summary>
        internal string Description { get; }

        /// <summary>A short marker at the bottom, such as "IN THE BAY NOW"; empty for none.</summary>
        internal string Footer { get; }

        /// <summary>False when the card cannot be chosen (greyed out).</summary>
        internal bool Enabled { get; }
    }
}
