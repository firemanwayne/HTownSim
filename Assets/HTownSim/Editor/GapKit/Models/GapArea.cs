#nullable enable

namespace HTownSim.EditorTools.GapKit.Models
{
    /// <summary>One gap region of the city: where its lots come from and what the placeholder pass named things.</summary>
    internal sealed class GapArea
    {
        /// <summary>Creates a description.</summary>
        internal GapArea(string name, string dataPath, string container, string placeholders)
        {
            Name = name;

            DataPath = dataPath;

            Container = container;

            Placeholders = placeholders;
        }

        /// <summary>A short name for logs and the mesh asset.</summary>
        internal string Name { get; }

        /// <summary>The lots file.</summary>
        internal string DataPath { get; }

        /// <summary>The scene container the placeholder pass made (under MidtownReal).</summary>
        internal string Container { get; }

        /// <summary>The group of plain boxes inside the container that the kit replaces.</summary>
        internal string Placeholders { get; }

        /// <summary>The south-west gap: between Midtown and the Station 16 neighborhood.</summary>
        internal static GapArea Southwest { get; } = new GapArea("Southwest", "Assets/HTownSim/Data/Gap_Southwest.txt", "Gap_Placeholders", "PlaceholderBuildings_Gap");

        /// <summary>The south gap: down to the Medical Center.</summary>
        internal static GapArea South { get; } = new GapArea("South", "Assets/HTownSim/Data/Gap_South.txt", "Gap_South", "PlaceholderBuildings_GapS");

        /// <summary>Every gap, in the order they are built.</summary>
        internal static GapArea[] All { get; } = { Southwest, South };
    }
}
