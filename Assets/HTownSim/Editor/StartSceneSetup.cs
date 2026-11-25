#nullable enable
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HTownSim.EditorTools
{
    /// <summary>
    /// Sets up the start scene: a tiny scene that holds only the start screen (<c>StartingTruckMenu</c>), so the vehicle is picked before the
    /// city loads and the city is loaded once. Creates Assets/Scenes/StartMenu.unity, lists it and the city scene in the build settings (start
    /// scene first), and makes the editor's Play button always begin at the start scene, whichever scene is open. Menu: HTownSim > Start Scene.
    /// </summary>
    [InitializeOnLoad]
    public static class StartSceneSetup
    {
        private const string StartPath = "Assets/Scenes/StartMenu.unity";

        private const string CityPath = "Assets/Scenes/HoustonResponse.unity";

        /// <summary>The editor preference that turns "Play starts at the start scene" off.</summary>
        private const string DisabledKey = "HTownSim.PlayFromStartScene.Disabled";

        private static readonly Color Backdrop = new Color(0.02f, 0.04f, 0.08f, 1f);

        static StartSceneSetup()
        {
            EditorApplication.delayCall += ApplyPlayModeStart;
        }

        [MenuItem("HTownSim/Start Scene/Create And Enable")]
        private static void CreateFromMenu()
        {
            Debug.Log(Create());
        }

        [MenuItem("HTownSim/Start Scene/Play From The Open Scene")]
        private static void DisableFromMenu()
        {
            EditorPrefs.SetBool(DisabledKey, true);

            EditorSceneManager.playModeStartScene = null;

            Debug.Log("Play now starts in the open scene (the start screen then loads the city a second time when another vehicle is picked).");
        }

        /// <summary>Creates the start scene if it is missing, lists both scenes in the build settings and turns the play-mode start on.</summary>
        public static string Create()
        {
            if (EditorApplication.isPlaying)
            {
                return "Stop Play Mode first.";
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(StartPath) == null)
            {
                var open = EditorSceneManager.GetActiveScene().path;

                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };

                var camera = cameraObject.AddComponent<Camera>();

                camera.clearFlags = CameraClearFlags.SolidColor;

                camera.backgroundColor = Backdrop;

                cameraObject.AddComponent<AudioListener>();

                EditorSceneManager.SaveScene(scene, StartPath);

                if (!string.IsNullOrEmpty(open))
                {
                    EditorSceneManager.OpenScene(open, OpenSceneMode.Single);
                }
            }

            EditorBuildSettings.scenes = new[] { StartPath, CityPath }
                .Select(path => new EditorBuildSettingsScene(path, true))
                .ToArray();

            EditorPrefs.SetBool(DisabledKey, false);

            ApplyPlayModeStart();

            return "Start scene ready: Play begins at " + StartPath + " and loads " + CityPath + " once a vehicle is picked.";
        }

        private static void ApplyPlayModeStart()
        {
            if (EditorPrefs.GetBool(DisabledKey, false))
            {
                return;
            }

            var start = AssetDatabase.LoadAssetAtPath<SceneAsset>(StartPath);

            if (start != null)
            {
                EditorSceneManager.playModeStartScene = start;
            }
        }
    }
}
