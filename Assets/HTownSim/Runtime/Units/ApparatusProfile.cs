#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// The size and fit-out of the apparatus being driven: wheels, turning circle, camera distance, where water and the
    /// scene lights come from, and which lens materials flash. HTownSim.Runtime.Contracts.VehicleStateBase, WheelSpin, SceneLights and LensFlasher read
    /// it from the driven object; without one they use <see cref="Engine"/>, the values Engine 07 was built for. A later
    /// apparatus pick swaps the visual and this profile together.
    /// </summary>
    internal sealed class ApparatusProfile : MonoBehaviour
    {
        /// <summary>The unit's radio name, shown in the incident text and the HUD header (e.g. "Ladder 07").</summary>
        [SerializeField] private string _unitName = "Engine 07";

        /// <summary>Rolling radius of the tyres in metres.</summary>
        [SerializeField] private float _wheelRadius = 0.54f;

        /// <summary>Front axle to rear axle (tandem centre) in metres, for the steering angle.</summary>
        [SerializeField] private float _wheelbase = 5.05f;

        /// <summary>Name prefixes of the steering wheels and of the other wheels.</summary>
        [SerializeField] private string[] _frontWheelPrefixes = { "WheelFront" };

        [SerializeField] private string[] _rearWheelPrefixes = { "WheelRear" };

        /// <summary>Yaw in degrees per metre travelled at full lock: 4 gives a ~14 m turning radius.</summary>
        [SerializeField] private float _turnRate = 4f;

        /// <summary>Multiplies the chase camera's distance.</summary>
        [SerializeField] private float _cameraDistanceScale = 1f;

        /// <summary>Where the water stream starts, in the apparatus's local space.</summary>
        [SerializeField] private Vector3 _waterOrigin = new Vector3(0f, 3.15f, 0.98f);

        /// <summary>Right-hand scene light position (mirrored for the left), in local space.</summary>
        [SerializeField] private Vector3 _sceneLight = new Vector3(0.86f, 2.72f, 4.25f);

        /// <summary>Whether the add-on LED brake and reverse panels are built (an apparatus with its own tail lights skips them).</summary>
        [SerializeField] private bool _rearLamps = true;

        /// <summary>Lens material names that flash with the emergency lights (two alternating groups), and that burn steadily with the headlights.</summary>
        [SerializeField] private string[] _flashGroupA = new string[0];

        [SerializeField] private string[] _flashGroupB = new string[0];

        [SerializeField] private string[] _steadyLenses = new string[0];

        /// <summary>The H key blows the synthesized air horn (engine and ladder); off gives the plain electric horn (ambulance).</summary>
        [SerializeField] private bool _airHorn = true;

        /// <summary>Whether E sprays water (engine and ladder); off for the ambulance, which carries none.</summary>
        [SerializeField] private bool _canSprayWater = true;

        /// <summary>Resource name of the lens glow list (position, direction and size of each lens) for a model with its lenses in the mesh.</summary>
        [SerializeField] private string _lensHalos = "";

        /// <summary>An EMS unit hears the EMS high/low alert tone for a new call; fire units hear the station ring and the six beeps.</summary>
        [SerializeField] private bool _emsAlert;

        /// <summary>Engine 07's values, used when the driven object has no profile.</summary>
        internal static readonly ApparatusProfileValues Engine = new ApparatusProfileValues(
            "Engine 07", 0.54f, 5.05f, new[] { "WheelFront" }, new[] { "WheelRear" }, 4f, 1f,
            new Vector3(0f, 3.15f, 0.98f), new Vector3(0.86f, 2.72f, 4.25f), true, new string[0], new string[0], new string[0], true);

        /// <summary>Engine 07's fit-out under another unit name (Engine 08, Engine 01): the same rig with its own number.</summary>
        internal static ApparatusProfileValues EngineAs(string unitName)
        {
            return new ApparatusProfileValues(
                unitName, 0.54f, 5.05f, new[] { "WheelFront" }, new[] { "WheelRear" }, 4f, 1f,
                new Vector3(0f, 3.15f, 0.98f), new Vector3(0.86f, 2.72f, 4.25f), true, new string[0], new string[0], new string[0], true);
        }

        /// <summary>Values supplied at run time by <c>ApparatusLoadout</c>, which win over the serialized fields.</summary>
        private ApparatusProfileValues? _runtimeValues;

        /// <summary>Replaces this profile's values for the rest of the session without touching the saved scene.</summary>
        internal void Use(ApparatusProfileValues values)
        {
            _runtimeValues = values;
        }

        /// <summary>This profile's values.</summary>
        internal ApparatusProfileValues Values => _runtimeValues ?? new ApparatusProfileValues(
            _unitName, _wheelRadius, _wheelbase, _frontWheelPrefixes, _rearWheelPrefixes, _turnRate, _cameraDistanceScale,
            _waterOrigin, _sceneLight, _rearLamps, _flashGroupA, _flashGroupB, _steadyLenses, _airHorn, _canSprayWater, _lensHalos, _emsAlert);

        /// <summary>The profile on a driven object, or Engine 07's values.</summary>
        internal static ApparatusProfileValues For(GameObject apparatus)
        {
            var profile = apparatus.GetComponent<ApparatusProfile>();

            return profile != null ? profile.Values : Engine;
        }
    }
}
