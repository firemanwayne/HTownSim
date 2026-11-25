#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Adds LED brake and reverse panels to the rear of the engine. The red panels light steadily while the truck brakes, the
    /// white panels (and a spot light on the road behind) light while it is in reverse. Built at run time on the rear body.
    /// </summary>
    internal sealed class RearLamps : MonoBehaviour
    {
        /// <summary>Where the rear face of the body is, in the engine's local space (metres, negative is behind the centre). Change it if the lamp panels float off the back or sink into it.</summary>
        private const float RearZ = -4.49f;

        /// <summary>How far (metres) each lamp panel sits to the side of the centre line. Raise it to move the panels apart; lower it to bring them together.</summary>
        private const float PanelX = 0.84f;

        /// <summary>The height (metres) of the brake lamp panels. Raise it to move them up the body; lower it to move them down.</summary>
        private const float BrakeY = 0.98f;

        /// <summary>The height (metres) of the reverse lamp panels. Raise it to move them up the body; lower it to move them down.</summary>
        private const float ReverseY = 1.31f;

        /// <summary>The width (metres) of each lamp panel. Raise it for wider panels; lower it for narrower ones.</summary>
        private const float PanelWidth = 0.2f;

        /// <summary>The height (metres) of each lamp panel. Raise it for taller panels; lower it for shorter ones.</summary>
        private const float PanelHeight = 0.2f;

        /// <summary>How many columns of LED dots each lamp panel has. Raise it for a denser panel; lower it for a coarser one.</summary>
        private const int Columns = 4;

        /// <summary>How many rows of LED dots each lamp panel has. Raise it for a denser panel; lower it for a coarser one.</summary>
        private const int Rows = 3;

        /// <summary>The size (metres) of each LED dot. Raise it for bigger dots; lower it for pinpoints.</summary>
        private const float DotSize = 0.034f;

        /// <summary>How bright (HDR emission, above the bloom threshold) a lit LED glows. Raise it for a stronger glow and bloom; lower it for dull lamps.</summary>
        private const float EmissionStrength = 7f;

        /// <summary>The size (metres) of the glow drawn around a lit panel. Raise it for a bigger bloom; lower it for a tight glow.</summary>
        private const float HaloSize = 0.55f;

        /// <summary>Opacity (0 to 1) of the glow around a lit panel. Raise it for a stronger glow; lower it to fade it out.</summary>
        private const float HaloAlpha = 0.5f;

        /// <summary>How bright the white light the reverse lamps cast on the ground is. Raise it for a stronger wash behind the truck; lower it for a weaker one.</summary>
        private const float ReverseLightIntensity = 45f;

        /// <summary>How far (metres) the reverse lamps' light reaches. Raise it to light further behind the truck; lower it to a short pool.</summary>
        private const float ReverseLightRange = 16f;

        private HTownSim.Runtime.Contracts.VehicleStateBase _response = null!;

        private Material _brakeOn = null!;

        private Material _brakeOff = null!;

        private Material _reverseOn = null!;

        private Material _reverseOff = null!;

        private Renderer[] _brakeDots = new Renderer[0];

        private Renderer[] _reverseDots = new Renderer[0];

        private Renderer[] _brakeHalos = new Renderer[0];

        private Renderer[] _reverseHalos = new Renderer[0];

        private Light _reverseLight = null!;

        private bool _brakeLit;

        private bool _reverseLit;

        private Texture2D? _haloTexture;

        /// <summary>Builds the four panels, their halos and the reverse spot light, all dark.</summary>
        internal void Initialize(HTownSim.Runtime.Contracts.VehicleStateBase response)
        {
            _response = response;

            var housing = Lit("RearPanelHousing", new Color(0.04f, 0.04f, 0.045f), false);

            _brakeOff = Lit("BrakeLedOff", new Color(0.32f, 0.03f, 0.04f), false);

            _brakeOn = Lit("BrakeLedOn", new Color(1f, 0.03f, 0.02f), true);

            _reverseOff = Lit("ReverseLedOff", new Color(0.62f, 0.64f, 0.66f), false);

            _reverseOn = Lit("ReverseLedOn", new Color(1f, 0.97f, 0.9f), true);

            _haloTexture = HaloTexture();

            var brakeDots = new System.Collections.Generic.List<Renderer>();

            var reverseDots = new System.Collections.Generic.List<Renderer>();

            var brakeHalos = new System.Collections.Generic.List<Renderer>();

            var reverseHalos = new System.Collections.Generic.List<Renderer>();

            foreach (var side in new[] { -1f, 1f })
            {
                BuildPanel(new Vector3(side * PanelX, BrakeY, RearZ), housing, _brakeOff, brakeDots, brakeHalos, new Color(1f, 0.05f, 0.03f, HaloAlpha));

                BuildPanel(new Vector3(side * PanelX, ReverseY, RearZ), housing, _reverseOff, reverseDots, reverseHalos, new Color(1f, 0.97f, 0.88f, HaloAlpha));
            }

            _brakeDots = brakeDots.ToArray();

            _reverseDots = reverseDots.ToArray();

            _brakeHalos = brakeHalos.ToArray();

            _reverseHalos = reverseHalos.ToArray();

            var lightObject = new GameObject("ReverseLight");

            lightObject.transform.SetParent(transform, false);

            lightObject.transform.localPosition = new Vector3(0f, 1.1f, RearZ - 0.15f);

            lightObject.transform.localRotation = Quaternion.Euler(28f, 180f, 0f);

            _reverseLight = lightObject.AddComponent<Light>();

            _reverseLight.type = LightType.Spot;

            _reverseLight.spotAngle = 110f;

            _reverseLight.range = ReverseLightRange;

            _reverseLight.color = new Color(1f, 0.96f, 0.86f);

            _reverseLight.intensity = 0f;

            _reverseLight.shadows = LightShadows.None;
        }

        /// <summary>Adds one housing, its LED dots and a camera-facing glow.</summary>
        private void BuildPanel(Vector3 centre, Material housing, Material dotMaterial, System.Collections.Generic.List<Renderer> dots, System.Collections.Generic.List<Renderer> halos, Color haloColour)
        {
            var box = Part("RearPanel", centre + new Vector3(0f, 0f, 0.004f), new Vector3(PanelWidth, PanelHeight, 0.02f), housing);

            box.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            for (var column = 0; column < Columns; column++)
            {
                for (var row = 0; row < Rows; row++)
                {
                    var x = (column - (Columns - 1) / 2f) * (PanelWidth - 0.04f) / (Columns - 1);

                    var y = (row - (Rows - 1) / 2f) * (PanelHeight - 0.04f) / (Rows - 1);

                    var dot = Part("LedDot", centre + new Vector3(x, y, -0.006f), new Vector3(DotSize, DotSize, 0.012f), dotMaterial);

                    dot.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                    dots.Add(dot.GetComponent<Renderer>());
                }
            }

            var halo = GameObject.CreatePrimitive(PrimitiveType.Quad);

            Destroy(halo.GetComponent<Collider>());

            halo.name = "RearPanelHalo";

            halo.transform.SetParent(transform, false);

            halo.transform.localPosition = centre + new Vector3(0f, 0f, -0.03f);

            halo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            halo.transform.localScale = Vector3.one * HaloSize;

            var material = new Material(Shader.Find("Sprites/Default")) { mainTexture = _haloTexture, color = haloColour, name = "RearPanelHaloMaterial" };

            var renderer = halo.GetComponent<Renderer>();

            renderer.sharedMaterial = material;

            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            renderer.receiveShadows = false;

            renderer.enabled = false;

            halos.Add(renderer);
        }

        private GameObject Part(string partName, Vector3 localPosition, Vector3 size, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);

            Destroy(part.GetComponent<Collider>());

            part.name = partName;

            part.transform.SetParent(transform, false);

            part.transform.localPosition = localPosition;

            part.transform.localScale = size;

            part.GetComponent<Renderer>().sharedMaterial = material;

            return part;
        }

        private static Material Lit(string materialName, Color colour, bool emissive)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = materialName, color = colour };

            material.SetFloat("_Smoothness", 0.6f);

            if (emissive)
            {
                material.EnableKeyword("_EMISSION");

                material.SetColor("_EmissionColor", colour * EmissionStrength);
            }

            return material;
        }

        private static Texture2D HaloTexture()
        {
            const int Size = 32;

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

            var pixels = new Color32[Size * Size];

            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var u = (x + 0.5f) / Size * 2f - 1f;

                    var v = (y + 0.5f) / Size * 2f - 1f;

                    var falloff = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));

                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(255f * falloff * falloff));
                }
            }

            texture.SetPixels32(pixels);

            texture.Apply();

            return texture;
        }

        /// <summary>Shows the panel state from the driver's inputs.</summary>
        private void LateUpdate()
        {
            if (_response == null)
            {
                return;
            }

            var paused = _response.EngineMuted;

            var brake = !paused && _response.BrakeLightsOn;

            var reverse = !paused && _response.ReverseEngaged;

            if (brake != _brakeLit)
            {
                _brakeLit = brake;

                Show(_brakeDots, _brakeHalos, brake ? _brakeOn : _brakeOff, brake);
            }

            if (reverse != _reverseLit)
            {
                _reverseLit = reverse;

                Show(_reverseDots, _reverseHalos, reverse ? _reverseOn : _reverseOff, reverse);

                _reverseLight.intensity = reverse ? ReverseLightIntensity : 0f;
            }
        }

        private static void Show(Renderer[] dots, Renderer[] halos, Material material, bool lit)
        {
            foreach (var dot in dots)
            {
                dot.sharedMaterial = material;
            }

            foreach (var halo in halos)
            {
                halo.enabled = lit;
            }
        }

        private void OnDestroy()
        {
            if (_haloTexture != null)
            {
                Destroy(_haloTexture);
            }
        }
    }
}
