#nullable enable
using System.Collections.Generic;
using System.Linq;
using HTownSim.EditorTools.GapKit.Models;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Services
{
    /// <summary>A grid over the lots' bounds, to ask whether a point lies inside some other lot (which means it is not street).</summary>
    internal sealed class GapParcelIndex
    {
        private const float CellSize = 40f;

        private readonly Dictionary<long, List<GapParcel>> _cells = new Dictionary<long, List<GapParcel>>();

        /// <summary>Indexes the lots.</summary>
        internal GapParcelIndex(IEnumerable<GapParcel> parcels)
        {
            foreach (var parcel in parcels)
            {
                var bounds = parcel.Bounds;

                for (var cx = Cell(bounds[0]); cx <= Cell(bounds[2]); cx++)
                {
                    for (var cz = Cell(bounds[1]); cz <= Cell(bounds[3]); cz++)
                    {
                        var key = Key(cx, cz);

                        if (!_cells.TryGetValue(key, out var list))
                        {
                            list = new List<GapParcel>();

                            _cells[key] = list;
                        }

                        list.Add(parcel);
                    }
                }
            }
        }

        /// <summary>True when the point is inside a lot other than <paramref name="self"/>.</summary>
        internal bool InsideAnother(Vector2 point, GapParcel self)
        {
            if (!_cells.TryGetValue(Key(Cell(point.x), Cell(point.y)), out var list))
            {
                return false;
            }

            foreach (var parcel in list)
            {
                if (parcel != self && Contains(parcel, point))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The lot the point is inside, or null.</summary>
        internal GapParcel? LotAt(Vector2 point)
        {
            foreach (var parcel in Near(point))
            {
                if (Contains(parcel, point))
                {
                    return parcel;
                }
            }

            return null;
        }

        /// <summary>True when the point is inside a lot or within the sidewalk's width of one.</summary>
        internal bool OnLotOrSidewalk(Vector2 point)
        {
            return Near(point).Any(parcel => GapSidewalk.OnLotOrSidewalk(parcel, point));
        }

        /// <summary>The lots whose grid cells are the point's or next to it (the sidewalk reaches into the next cell).</summary>
        private IEnumerable<GapParcel> Near(Vector2 point)
        {
            var cx = Cell(point.x);

            var cz = Cell(point.y);

            for (var x = cx - 1; x <= cx + 1; x++)
            {
                for (var z = cz - 1; z <= cz + 1; z++)
                {
                    if (_cells.TryGetValue(Key(x, z), out var list))
                    {
                        foreach (var parcel in list)
                        {
                            yield return parcel;
                        }
                    }
                }
            }
        }

        private static bool Contains(GapParcel parcel, Vector2 point)
        {
            var bounds = parcel.Bounds;

            if (point.x < bounds[0] || point.x > bounds[2] || point.y < bounds[1] || point.y > bounds[3])
            {
                return false;
            }

            var polygon = parcel.Polygon;

            var inside = false;

            var count = polygon.Length / 2;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                var xi = polygon[i * 2];

                var zi = polygon[(i * 2) + 1];

                var xj = polygon[j * 2];

                var zj = polygon[(j * 2) + 1];

                if (((zi > point.y) != (zj > point.y)) && (point.x < ((xj - xi) * (point.y - zi) / (zj - zi)) + xi))
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static int Cell(float value)
        {
            return Mathf.FloorToInt(value / CellSize);
        }

        private static long Key(int cx, int cz)
        {
            return ((long)cx << 32) ^ (uint)cz;
        }
    }
}
