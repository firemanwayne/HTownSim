#nullable enable
using System;

namespace HTownSim.Runtime
{
    /// <summary>The service a unit belongs to, which decides who may see where it is (see <see cref="ViewerRole"/>).</summary>
    internal enum UnitAgency
    {
        /// <summary>Houston Fire Department (engines, ladders, chiefs).</summary>
        Fire,

        /// <summary>Emergency medical services (ambulances, medic units).</summary>
        Ems,

        /// <summary>Houston Police Department.</summary>
        Police,
    }

    /// <summary>
    /// What a viewer is allowed to know about other units' locations. One rule for the whole game: the city server uses it to decide which
    /// units it streams to a connection (so a location a viewer may not see never leaves the server), and a client's map uses it for the
    /// units it draws. A spectator may see every unit; the police see every unit; fire and EMS see only fire and EMS units. To change who
    /// sees what, change the rows below (or add a role); nothing else needs to know.
    /// </summary>
    internal sealed class ViewerRole
    {
        private static readonly UnitAgency[] Everyone = { UnitAgency.Fire, UnitAgency.Ems, UnitAgency.Police };

        private static readonly UnitAgency[] FireAndEms = { UnitAgency.Fire, UnitAgency.Ems };

        /// <summary>Someone watching the city with no unit of their own: sees every unit.</summary>
        internal static readonly ViewerRole Spectator = new ViewerRole("Spectator", Everyone);

        /// <summary>A fire unit's crew: sees fire and EMS units, not police.</summary>
        internal static readonly ViewerRole Fire = new ViewerRole("Fire", FireAndEms);

        /// <summary>An EMS unit's crew: sees fire and EMS units, not police.</summary>
        internal static readonly ViewerRole Ems = new ViewerRole("Ems", FireAndEms);

        /// <summary>A police unit's officers: see every unit.</summary>
        internal static readonly ViewerRole Police = new ViewerRole("Police", Everyone);

        private static readonly ViewerRole[] All = { Spectator, Fire, Ems, Police };

        private readonly UnitAgency[] _sees;

        private ViewerRole(string name, UnitAgency[] sees)
        {
            Name = name;

            _sees = sees;
        }

        /// <summary>The role's name, as a client announces it.</summary>
        internal string Name { get; }

        /// <summary>True when this role may see where a unit of that service is.</summary>
        internal bool CanSee(UnitAgency agency)
        {
            return Array.IndexOf(_sees, agency) >= 0;
        }

        /// <summary>True when this role may see where the unit with that radio name (Engine 07, Medic 08, HPD Unit 2) is.</summary>
        internal bool CanSeeUnit(string unitName)
        {
            return CanSee(UnitBehavior.For(unitName).Agency);
        }

        /// <summary>True when this role sees nothing that <paramref name="granted"/> does not (a client may ask for less than it is granted, never more).</summary>
        internal bool IsWithin(ViewerRole granted)
        {
            foreach (var agency in _sees)
            {
                if (!granted.CanSee(agency))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The role of the crew of the unit with that radio name.</summary>
        internal static ViewerRole ForUnit(string unitName)
        {
            return UnitBehavior.For(unitName).Agency switch
            {
                UnitAgency.Police => Police,
                UnitAgency.Ems => Ems,
                _ => Fire,
            };
        }

        /// <summary>The role with that name (any case), or null.</summary>
        internal static ViewerRole? Parse(string name)
        {
            foreach (var role in All)
            {
                if (string.Equals(role.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return role;
                }
            }

            return null;
        }
    }
}
