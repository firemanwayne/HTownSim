#nullable enable
using UnityEngine;

namespace HTownSim.Runtime
{
    /// <summary>
    /// Runs the traffic signals of the gap streets: two phases that take turns, each shown by one renderer whose three submeshes are the
    /// red, amber and green lenses (in that order). Every state change swaps the lens materials of the whole phase, so the cost is two
    /// material swaps per state change, not a renderer per lens. Public because the editor tool that builds the signals adds it.
    /// </summary>
    public sealed class GapSignalCycle : MonoBehaviour
    {
        /// <summary>The cycle length and the moments the phases change, seconds.</summary>
        private const float Cycle = 48f;

        private const float GreenEnd = 20f;

        private const float AmberEnd = 23f;

        private const float SecondStart = 24f;

        private const float SecondGreenEnd = 44f;

        private const float SecondAmberEnd = 47f;

        [SerializeField] private Renderer _phaseA = null!;

        [SerializeField] private Renderer _phaseB = null!;

        /// <summary>Lit and unlit lens materials: red, amber, green.</summary>
        [SerializeField] private Material[] _lit = new Material[0];

        [SerializeField] private Material[] _unlit = new Material[0];

        private int _lastA = -1;

        private int _lastB = -1;

        private void Update()
        {
            var t = Time.time % Cycle;

            Show(_phaseA, StateA(t), ref _lastA);

            Show(_phaseB, StateB(t), ref _lastB);
        }

        /// <summary>The state (0 red, 1 amber, 2 green) of the first phase.</summary>
        private static int StateA(float t)
        {
            return t < GreenEnd ? 2 : t < AmberEnd ? 1 : 0;
        }

        /// <summary>The state of the second phase: red until the first has been red for a second, then green.</summary>
        private static int StateB(float t)
        {
            return t < SecondStart ? 0 : t < SecondGreenEnd ? 2 : t < SecondAmberEnd ? 1 : 0;
        }

        private void Show(Renderer renderer, int state, ref int last)
        {
            if (state == last || renderer == null || _lit.Length < 3 || _unlit.Length < 3)
            {
                return;
            }

            last = state;

            var materials = new Material[3];

            for (var i = 0; i < 3; i++)
            {
                materials[i] = i == state ? _lit[i] : _unlit[i];
            }

            // Lit red is index 0 when state is 0; green is index 2: the submeshes run red, amber, green.
            renderer.sharedMaterials = materials;
        }
    }
}
