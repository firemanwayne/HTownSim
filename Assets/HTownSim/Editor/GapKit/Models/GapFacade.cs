#nullable enable

namespace HTownSim.EditorTools.GapKit.Models
{
    /// <summary>A facade texture's tiling, in metres: the material, the size of one tile and the width of one window bay.</summary>
    internal sealed class GapFacade
    {
        /// <summary>Creates a facade description.</summary>
        internal GapFacade(string material, float tileWidth, float tileHeight, float bayWidth)
        {
            Material = material;

            TileWidth = tileWidth;

            TileHeight = tileHeight;

            BayWidth = bayWidth;
        }

        /// <summary>The material asset name.</summary>
        internal string Material { get; }

        /// <summary>Width of one tile.</summary>
        internal float TileWidth { get; }

        /// <summary>Height of one tile.</summary>
        internal float TileHeight { get; }

        /// <summary>Width of one window bay (a whole number of bays fits a tile).</summary>
        internal float BayWidth { get; }

        /// <summary>Residential windows: four bays by four floors.</summary>
        internal static GapFacade Residential { get; } = new GapFacade("FacadeRes", 12f, 12.8f, 3f);

        /// <summary>Shop upper-floor windows: four bays by four floors.</summary>
        internal static GapFacade Shop { get; } = new GapFacade("FacadeShop", 12f, 14.4f, 3f);

        /// <summary>The ground-floor storefront band, 4.5 m tall.</summary>
        internal static GapFacade Storefront { get; } = new GapFacade("Storefront", 12f, 4.5f, 3f);

        /// <summary>Tower windows: four bays by four floors.</summary>
        internal static GapFacade TowerWindows { get; } = new GapFacade("TowerWindows", 8.8f, 14.4f, 2.2f);

        /// <summary>A house front with its door: four bays by two floors.</summary>
        internal static GapFacade HouseFront { get; } = new GapFacade("HouseFront", 12f, 6.4f, 3f);

        /// <summary>A house side with windows only.</summary>
        internal static GapFacade HouseSide { get; } = new GapFacade("HouseSide", 12f, 6.4f, 3f);

        /// <summary>A glass curtain wall, used as the wall itself.</summary>
        internal static GapFacade CurtainWall { get; } = new GapFacade("CurtainWall", 12f, 14.4f, 1.5f);
    }
}
