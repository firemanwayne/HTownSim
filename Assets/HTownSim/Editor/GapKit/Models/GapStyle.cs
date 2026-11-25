#nullable enable

namespace HTownSim.EditorTools.GapKit.Models
{
    /// <summary>The kind of building a lot gets.</summary>
    internal enum GapStyle
    {
        /// <summary>One- or two-storey house with a pitched roof.</summary>
        House,

        /// <summary>Low shop with a storefront band and a flat roof.</summary>
        Shop,

        /// <summary>Mid-rise apartment or townhome with a flat roof.</summary>
        Apartment,

        /// <summary>Tower of ten floors or more, in stone with windows or in glass.</summary>
        Tower,

        /// <summary>A parking structure: plain concrete, no windows, flat roof.</summary>
        Garage,
    }
}
