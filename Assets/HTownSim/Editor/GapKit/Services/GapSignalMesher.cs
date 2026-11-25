#nullable enable
using HTownSim.EditorTools.GapKit.Models;
using UnityEngine;

namespace HTownSim.EditorTools.GapKit.Services
{
    /// <summary>
    /// Builds the traffic signals of a junction: for every street arriving at it a mast arm on the near right-hand corner, reaching out over
    /// the approaching lanes, with two signal heads. The poles, arms and head housings go into one mesh; the lenses go into two more, one
    /// for each of the two phases (legs along the junction's first major street are phase A, the others phase B), so that a whole phase
    /// can be switched with one renderer.
    /// </summary>
    internal sealed class GapSignalMesher
    {
        /// <summary>Material names of the structure.</summary>
        private const string Steel = "Steel";

        private const string Housing = "Rubber";

        /// <summary>The lens submeshes' names, in this order (red first): the cycle component relies on it.</summary>
        internal static readonly string[] LensOrder = { "LensRed", "LensAmber", "LensGreen" };

        /// <summary>Mast arm height and the arm's reach over the road, metres.</summary>
        private const float ArmHeight = 7.2f;

        private const float ArmReach = 6.5f;

        /// <summary>How far before the crosswalk the pole stands (past the junction's half size) and how far in from the curb it is.</summary>
        private const float PoleSetback = 4.5f;

        private const float CurbInset = 1.2f;

        /// <summary>Signal head housing size (across, up, along) and a lens's size and lift, metres.</summary>
        private static readonly Vector3 HeadSize = new Vector3(0.5f, 1.4f, 0.5f);

        private const float LensSize = 0.28f;

        private const float LensThickness = 0.1f;

        private const float LensSpacing = 0.45f;

        private readonly GapMeshBuilder _structure;

        private readonly GapMeshBuilder _phaseA;

        private readonly GapMeshBuilder _phaseB;

        private readonly float _groundHeight;

        /// <summary>Builds into the three meshes; <paramref name="groundHeight"/> is the street surface.</summary>
        internal GapSignalMesher(GapMeshBuilder structure, GapMeshBuilder phaseA, GapMeshBuilder phaseB, float groundHeight)
        {
            _structure = structure;

            _phaseA = phaseA;

            _phaseB = phaseB;

            _groundHeight = groundHeight;
        }

        /// <summary>Adds the signals of a junction; returns how many masts it got.</summary>
        internal int Add(StreetJunction junction)
        {
            var reference = junction.Legs.Find(leg => leg.IsMajor)?.Direction ?? junction.Legs[0].Direction;

            foreach (var leg in junction.Legs)
            {
                var phaseA = Mathf.Abs(Vector2.Dot(leg.Direction, reference)) > 0.7f;

                Mast(junction, leg, phaseA ? _phaseA : _phaseB);
            }

            return junction.Legs.Count;
        }

        private void Mast(StreetJunction junction, StreetLeg leg, GapMeshBuilder lenses)
        {
            // Traffic from this leg comes toward the junction, on its own right-hand side.
            var right = new Vector2(-leg.Direction.y, leg.Direction.x);

            var along = junction.Position + (leg.Direction * (junction.HalfSize + PoleSetback));

            var foot = along + (right * ((leg.Width / 2f) - CurbInset));

            var toRoad = -right;

            GapOrientedMesh.Box(_structure, Steel, new Vector3(foot.x, _groundHeight, foot.y), leg.Direction, new Vector3(0.35f, ArmHeight, 0.35f));

            var armMiddle = foot + (toRoad * (ArmReach / 2f));

            GapOrientedMesh.Box(_structure, Steel, new Vector3(armMiddle.x, _groundHeight + ArmHeight - 0.25f, armMiddle.y), toRoad, new Vector3(0.25f, 0.25f, ArmReach));

            foreach (var reach in new[] { 0.55f, 0.85f })
            {
                var head = foot + (toRoad * (ArmReach * reach));

                var headBase = new Vector3(head.x, _groundHeight + ArmHeight - 0.25f - HeadSize.y, head.y);

                GapOrientedMesh.Box(_structure, Housing, headBase, leg.Direction, HeadSize);

                var face = head + (leg.Direction * ((HeadSize.z / 2f) + (LensThickness / 2f)));

                for (var lens = 0; lens < LensOrder.Length; lens++)
                {
                    var y = headBase.y + (HeadSize.y / 2f) + ((1 - lens) * LensSpacing) - (LensSize / 2f);

                    GapOrientedMesh.Box(lenses, LensOrder[lens], new Vector3(face.x, y, face.y), leg.Direction, new Vector3(LensSize, LensSize, LensThickness));
                }
            }
        }
    }
}
