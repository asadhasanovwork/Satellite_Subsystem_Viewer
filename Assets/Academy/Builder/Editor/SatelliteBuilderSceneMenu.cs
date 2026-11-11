using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Academy.Builder.EditorTools
{
    /// <summary>
    /// Makes the Satellite Builder work with no manual steps: when the editor loads (and before entering Play
    /// mode) the SatelliteBuilder scene is created if it is missing and registered in Build Settings.
    /// The scene is built in the background (additively) so the scene you have open is never touched.
    /// The Academy > Builder menu item rebuilds it on demand.
    /// </summary>
    [InitializeOnLoad]
    public static class SatelliteBuilderSceneMenu
    {
        public const string ScenePath = SatelliteBuilderSceneFactory.ScenePath;
        private const string PanelSettingsPath = "Assets/UI/MainPanelSettings.asset";
        private const string MainMenuScenePath = SatelliteBuilderSceneFactory.MainMenuScenePath;

        static SatelliteBuilderSceneMenu()
        {
            EditorApplication.delayCall += AutoSetup;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode)
                    AutoSetup();
            };
        }

        /// <summary>Create the scene if missing and make sure it is in Build Settings. Safe to call any time in edit mode.</summary>
        public static void AutoSetup()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            bool changed = false;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && !File.Exists(ScenePath))
            {
                if (BuildSceneInBackground())
                {
                    Debug.Log("[Academy] Created " + ScenePath + " automatically.");
                    changed = true;
                }
            }

            changed |= AddToBuildSettings(MainMenuScenePath);
            changed |= AddToBuildSettings(ScenePath);
            if (changed)
                AssetDatabase.SaveAssets(); // writes ProjectSettings/EditorBuildSettings.asset to disk
        }

        [MenuItem("Academy/Builder/Rebuild Satellite Builder Scene", false, 100)]
        public static void RebuildScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).path == ScenePath)
                {
                    EditorUtility.DisplayDialog("Satellite Builder", "Close the SatelliteBuilder scene first, then rebuild it.", "OK");
                    return;
                }
            }
            if (BuildSceneInBackground())
            {
                AddToBuildSettings(MainMenuScenePath);
                AddToBuildSettings(ScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log("[Academy] Rebuilt " + ScenePath + ".");
            }
        }

        [MenuItem("Academy/Builder/Open Satellite Builder Scene", false, 101)]
        public static void OpenScene()
        {
            AutoSetup();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        /// <summary>Build the scene additively, save it, close it and restore the previously active scene.</summary>
        private static bool BuildSceneInBackground()
        {
            // Unity allows only one untitled scene; building additively next to one would fail.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                    return false;
            }

            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = default(Scene);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);

                SatelliteBuilderSceneFactory.ApplyRenderSettings();
                SatelliteBuilderSceneFactory.Populate(FindPanelSettings());

                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                {
                    Debug.LogError("[Academy] Could not save " + ScenePath);
                    return false;
                }
                return true;
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded)
                    SceneManager.SetActiveScene(previousActive);
                if (scene.IsValid())
                    EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.Refresh();
            }
        }

        private static PanelSettings FindPanelSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings != null)
                return settings;
            foreach (string guid in AssetDatabase.FindAssets("t:PanelSettings"))
            {
                settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (settings != null)
                    return settings;
            }
            Debug.LogWarning("[Academy] No PanelSettings asset found. Assign one to the UIDocument on 'Satellite Builder UI'.");
            return null;
        }

        private static bool AddToBuildSettings(string path)
        {
            if (!File.Exists(path))
                return false;

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (EditorBuildSettingsScene s in scenes)
            {
                if (s.path != path)
                    continue;
                if (s.enabled)
                    return false;
                s.enabled = true;
                EditorBuildSettings.scenes = scenes.ToArray();
                return true;
            }
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            return true;
        }
    }
}
