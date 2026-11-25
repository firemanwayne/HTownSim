#nullable enable
using UnityEngine;
using UnityEngine.Rendering;

namespace HTownSim.Runtime
{
    /// <summary>
    /// The engine's two front scene lights (wide floodlights on white mounts that follow the cab's brow, under the light
    /// bar ends). The F key switches them: their lens faces (the model's Engine07_SceneLenses) glow white and two wide
    /// spot lights flood the area ahead of the truck. Independent of the emergency lights; off at start and after a
    /// restart.
    /// </summary>
    internal sealed class SceneLights : MonoBehaviour
    {
        /// <summary>Name of the model part holding both scene light lens faces.</summary>
        private const string LensObjectName = "Engine07_SceneLenses";

        /// <summary>Aim: tipped down onto the road and splayed outward, like a flood pattern.</summary>
        private const float AimDownDegrees = 16f;

        private const float AimOutDegrees = 18f;

        private const float SpotAngle = 100f;

        private const float InnerSpotAngle = 60f;

        private const float LightRange = 32f;

        private const float LightIntensity = 70f;

        private const float EmissionStrength = 9f;

        private static readonly Color FloodColour = new(1f, 0.98f, 0.93f);

        private HTownSim.Runtime.Contracts.VehicleStateBase _response = null!;

        private Renderer[] _lenses = System.Array.Empty<Renderer>();

        private Material[] _offMaterials = System.Array.Empty<Material>();

        private Material? _onMaterial;

        private Light[] _lights = System.Array.Empty<Light>();

        private bool _lit;

        /// <summary>True while the scene lights are lit.</summary>
        internal bool Lit => _lit;

        /// <summary>Finds the lens faces and builds the two spot lights, all dark.</summary>
        internal void Initialize(HTownSim.Runtime.Contracts.VehicleStateBase response)
        {
            _response = response;

            var lenses = new System.Collections.Generic.List<Renderer>();

            foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.name == LensObjectName)
                {
                    lenses.Add(renderer);
                }
            }

            _lenses = lenses.ToArray();

            _offMaterials = new Material[_lenses.Length];

            for (var i = 0; i < _lenses.Length; i++)
            {
                _offMaterials[i] = _lenses[i].sharedMaterial;
            }

            _onMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "SceneLensOn", color = FloodColour };

            _onMaterial.EnableKeyword("_EMISSION");

            _onMaterial.SetColor("_EmissionColor", FloodColour * EmissionStrength);

            _lights = new[] { BuildLight(-1f), BuildLight(1f) };
        }

        /// <summary>Adds one flood spot light at a lens, aimed forward, down and outward.</summary>
        private Light BuildLight(float side)
        {
            var lightObject = new GameObject(side < 0f ? "SceneLightLeft" : "SceneLightRight");

            lightObject.transform.SetParent(transform, false);

            // Lens centre from the driven apparatus's profile (right-hand lamp; mirrored for the left).
            var lamp = _response.Profile.SceneLight;

            lightObject.transform.localPosition = new Vector3(side * lamp.x, lamp.y, lamp.z);

            lightObject.transform.localRotation = Quaternion.Euler(AimDownDegrees, side * AimOutDegrees, 0f);

            var spot = lightObject.AddComponent<Light>();

            spot.type = LightType.Spot;

            spot.spotAngle = SpotAngle;

            spot.innerSpotAngle = InnerSpotAngle;

            spot.range = LightRange;

            spot.color = FloodColour;

            spot.intensity = 0f;

            spot.shadows = LightShadows.None;

            spot.renderMode = LightRenderMode.ForcePixel;

            spot.enabled = false;

            return spot;
        }

        /// <summary>Follows the scene light switch.</summary>
        private void LateUpdate()
        {
            if (_response == null)
            {
                return;
            }

            var lit = _response.SceneLightsOn;

            if (lit == _lit)
            {
                return;
            }

            _lit = lit;

            for (var i = 0; i < _lenses.Length; i++)
            {
                _lenses[i].sharedMaterial = lit && _onMaterial != null ? _onMaterial : _offMaterials[i];

                _lenses[i].shadowCastingMode = ShadowCastingMode.Off;
            }

            foreach (var spot in _lights)
            {
                spot.enabled = lit;

                spot.intensity = lit ? LightIntensity : 0f;
            }
        }

        /// <summary>Releases the runtime lens material.</summary>
        private void OnDestroy()
        {
            if (_onMaterial != null)
            {
                Destroy(_onMaterial);
            }
        }
    }
}
