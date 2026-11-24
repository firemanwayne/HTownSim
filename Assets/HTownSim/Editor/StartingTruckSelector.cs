#nullable enable
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HTownSim.Editor
{
    /// <summary>Selects and saves the apparatus driven when the scene enters Play mode.</summary>
    internal static class StartingTruckMenu
    {
        /// <summary>Defers intermediate helper saves until the complete truck swap succeeds.</summary>
        internal static bool IsSwitching { get; private set; }

        /// <summary>The editor menu containing all starting apparatus choices.</summary>
        private const string MenuRoot = "HTownSim/Starting Truck/";

        /// <summary>The engine's display name.</summary>
        private const string EngineName = "Engine 07";

        /// <summary>The ladder's display name.</summary>
        private const string LadderName = "Ladder 07";

        /// <summary>The ambulance's display name.</summary>
        private const string AmbulanceName = "Ambulance 07";
        /// <summary>The shared player root, regardless of the fitted apparatus.</summary>
        private const string PlayerName = "Engine07";

        /// <summary>The ladder model used to identify the current apparatus.</summary>
        private const string LadderVisual = "Ladder07Visual";

        /// <summary>The ambulance model used to identify the current apparatus.</summary>
        private const string AmbulanceVisual = "Ambulance07Visual";

        /// <summary>Selects the engine.</summary>
        [MenuItem(MenuRoot + EngineName, false, 10)]
        private static void SelectEngine() => SelectTruck(EngineName);

        /// <summary>Selects the aerial ladder.</summary>
        [MenuItem(MenuRoot + LadderName, false, 11)]
        private static void SelectLadder() => SelectTruck(LadderName);

        /// <summary>Selects the ambulance.</summary>
        [MenuItem(MenuRoot + AmbulanceName, false, 12)]
        private static void SelectAmbulance() => SelectTruck(AmbulanceName);

        /// <summary>Checks the engine menu item and gates scene editing.</summary>
        [MenuItem(MenuRoot + EngineName, true)]
        private static bool ValidateEngine() => ValidateChoice(EngineName);

        /// <summary>Checks the ladder menu item and gates scene editing.</summary>
        [MenuItem(MenuRoot + LadderName, true)]
        private static bool ValidateLadder() => ValidateChoice(LadderName);

        /// <summary>Checks the ambulance menu item and gates scene editing.</summary>
        [MenuItem(MenuRoot + AmbulanceName, true)]
        private static bool ValidateAmbulance() => ValidateChoice(AmbulanceName);

        /// <summary>Finds the player only in the active scene.</summary>
        private static GameObject? FindPlayer()
        {
            var scene = EditorSceneManager.GetActiveScene();

            return scene.IsValid() && scene.isLoaded
                ? scene.GetRootGameObjects().FirstOrDefault(root => root.name == PlayerName)
                : null;
        }

        /// <summary>Reads the fitted model instead of relying on the shared player object's name.</summary>
        private static string CurrentTruck(GameObject player)
        {
            if (player.transform.Find(AmbulanceVisual) != null)
            {
                return AmbulanceName;
            }

            return player.transform.Find(LadderVisual) != null ? LadderName : EngineName;
        }

        /// <summary>Disables switching during Play mode, compilation, or an unsupported scene setup.</summary>
        private static bool ValidateChoice(string truck)
        {
            var player = FindPlayer();

            Menu.SetChecked(MenuRoot + truck, player != null && CurrentTruck(player) == truck);

            return !EditorApplication.isPlayingOrWillChangePlaymode
                && !EditorApplication.isCompiling
                && EditorSceneManager.sceneCount == 1
                && PrefabStageUtility.GetCurrentPrefabStage() == null
                && player != null && player.activeInHierarchy
                && player.GetComponent("FireResponse") != null;
        }

        /// <summary>Runs the required swaps, preserving a scene backup and restoring it if a swap fails.</summary>
        private static void SelectTruck(string truck)
        {
            if (!ValidateChoice(truck))
            {
                return;
            }

            var player = FindPlayer()!;

            if (CurrentTruck(player) == truck)
            {
                return;
            }

            var scene = EditorSceneManager.GetActiveScene();

            if (scene.isDirty || string.IsNullOrEmpty(scene.path))
            {
                if (!EditorUtility.DisplayDialog("Starting Truck", "Save this scene before changing the starting truck?", "Save and Switch", "Cancel"))
                {
                    return;
                }

                if (!EditorSceneManager.SaveScene(scene))
                {
                    return;
                }
            }

            var scenePath = scene.path;

            var backupPath = Path.Combine("Temp", "restore", "StartingTruck", Guid.NewGuid().ToString("N") + ".unity");

            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);

            File.Copy(scenePath, backupPath);

            try
            {
                IsSwitching = true;

                var current = CurrentTruck(player);

                if (current == AmbulanceName)
                {
                    DriveAmbulance07.Revert();

                    DriveLadder07.Refresh();

                    current = LadderName;
                }

                if (truck == EngineName)
                {
                    DriveLadder07.Revert();
                }
                else
                {
                    if (current == EngineName)
                    {
                        DriveLadder07.Apply();
                    }

                    if (truck == AmbulanceName)
                    {
                        DriveAmbulance07.Apply();
                    }
                }

                if (CurrentTruck(FindPlayer()!) != truck)
                {
                    throw new InvalidOperationException("The requested truck could not be fitted.");
                }

                if (!EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()))
                {
                    throw new InvalidOperationException("Could not save the truck selection.");
                }

                SceneView.lastActiveSceneView?.ShowNotification(new GUIContent(truck + " selected. Press Play to drive."));
            }
            catch (Exception exception)
            {
                File.Copy(backupPath, scenePath, true);

                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                Debug.LogException(exception);

                EditorUtility.DisplayDialog("Starting Truck", "The truck could not be changed. Your scene was restored.\n\n" + exception.Message, "OK");
            }
            finally
            {
                IsSwitching = false;
            }
        }
    }
}
