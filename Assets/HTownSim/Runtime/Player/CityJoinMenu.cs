#nullable enable
using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HTownSim.Runtime
{
    /// <summary>Starts a spectator join and carries its timing across the scene load.</summary>
    internal sealed class CityJoinMenu : MonoBehaviour
    {
        private static Stopwatch? _joinTimer;

        private bool _loading;

        private void Start()
        {
            // "-city-autojoin" joins without waiting for the button.
            if (System.Array.Exists(System.Environment.GetCommandLineArgs(), argument => string.Equals(argument, "-city-autojoin", System.StringComparison.OrdinalIgnoreCase)))
            {
                BeginJoin();
            }
        }

        private void Update()
        {
            // Enter or Space joins too, for when the button cannot be clicked.
            var keyboard = UnityEngine.InputSystem.Keyboard.current;

            if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame))
            {
                BeginJoin();
            }
        }

        internal void BeginJoin()
        {
            if (_loading)
            {
                return;
            }

            _loading = true;
            StartTiming();
            StartCoroutine(LoadCity());
        }

        /// <summary>Starts the join clock; the spectator shows how long it took to get the first pose from the city server.</summary>
        internal static void StartTiming()
        {
            _joinTimer = Stopwatch.StartNew();
        }

        private static System.Collections.IEnumerator LoadCity()
        {
            // Allow the loading label to draw before Unity begins loading Houston.
            yield return null;
            yield return SceneManager.LoadSceneAsync(StartingTruckMenu.CityScene);
        }

        internal static double? ElapsedSeconds => _joinTimer?.Elapsed.TotalSeconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetTiming()
        {
            _joinTimer = null;
        }

        private void OnGUI()
        {
            const float width = 320f;
            const float height = 110f;
            var panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUILayout.BeginArea(panel, GUI.skin.box);
            GUILayout.Label("HTownSim");
            if (_loading)
            {
                GUILayout.Label("Loading Houston...");
            }
            else if (GUILayout.Button("Join City (Enter)", GUILayout.Height(38f)))
            {
                BeginJoin();
            }

            GUILayout.EndArea();
        }
    }
}
