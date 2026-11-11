using Academy.Prototype;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Academy.Builder
{
    /// <summary>
    /// Builds the Satellite Builder scene content (lights, camera, satellite view, UI) in code, and opens the
    /// builder from anywhere without any manual setup:
    /// 1. the SatelliteBuilder scene if it is in Build Settings,
    /// 2. in the editor, the scene asset directly (works even when it is not in Build Settings),
    /// 3. otherwise a scene generated at runtime with exactly the same content.
    /// The editor auto-setup uses <see cref="Populate"/> too, so the saved scene and the generated one match.
    /// </summary>
    public static class SatelliteBuilderSceneFactory
    {
        public const string SceneName = "SatelliteBuilder";
        public const string ScenePath = "Assets/Academy/Builder/Scenes/SatelliteBuilder.unity";
        public const string MainMenuSceneName = "Test_Demo";
        public const string MainMenuScenePath = "Assets/Test_Demo.unity";

        public static readonly Color BackgroundColor = new Color(0.01f, 0.015f, 0.035f);

        /// <summary>Lighting for the builder. Applies to the active scene, so make the builder scene active first.</summary>
        public static void ApplyRenderSettings()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.17f, 0.19f, 0.23f);
        }

        /// <summary>Create the builder objects in the active scene. Returns the UI component.</summary>
        public static SatelliteBuilderUI Populate(PanelSettings panelSettings)
        {
            var sun = new GameObject("Sun");
            var sunLight = sun.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.intensity = 1.6f;
            sunLight.color = new Color(1f, 0.97f, 0.92f);
            sunLight.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(35f, -40f, 0f);

            var fill = new GameObject("Earthshine Fill");
            var fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.intensity = 0.4f;
            fillLight.color = new Color(0.45f, 0.6f, 1f);
            fillLight.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(-30f, 140f, 0f);

            var satellite = new GameObject("Satellite");
            var view = satellite.AddComponent<BuilderSatelliteView>();

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var cam = cameraObject.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BackgroundColor;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 2000f;
            cam.fieldOfView = 40f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.transform.position = new Vector3(0.55f, 0.33f, -0.77f);
            cameraObject.transform.LookAt(satellite.transform);
            var orbit = cameraObject.AddComponent<PrototypeOrbitCamera>();
            orbit.target = satellite.transform;
            orbit.distance = 1f;

            var uiObject = new GameObject("Satellite Builder UI");
            var document = uiObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            if (panelSettings == null)
                Debug.LogWarning("[Academy] No Panel Settings for the Satellite Builder UI; the UI will not be visible.");
            var ui = uiObject.AddComponent<SatelliteBuilderUI>();
            ui.view = view;
            ui.orbitCamera = orbit;
            return ui;
        }

        /// <summary>Open the builder. <paramref name="panelSettings"/> is used only if the scene has to be generated at runtime.</summary>
        public static void Open(PanelSettings panelSettings)
        {
            if (LoadExisting(SceneName, ScenePath))
                return;

            // Nothing to load: generate the builder scene in code and drop the current scene(s).
            Scene generated = SceneManager.CreateScene(SceneName + " (generated)");
            SceneManager.SetActiveScene(generated);
            ApplyRenderSettings();
            Populate(panelSettings);

            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s != generated && s.isLoaded)
                    SceneManager.UnloadSceneAsync(s);
            }
        }

        /// <summary>Return to the main menu scene.</summary>
        public static bool OpenMainMenu(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
                sceneName = MainMenuSceneName;
            return LoadExisting(sceneName, sceneName == MainMenuSceneName ? MainMenuScenePath : null);
        }

        /// <summary>Load a scene by name from Build Settings or, in the editor, straight from its asset path.</summary>
        public static bool LoadExisting(string sceneName, string assetPath)
        {
            if (Application.CanStreamedLevelBeLoaded(sceneName))
            {
                SceneManager.LoadScene(sceneName);
                return true;
            }
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(assetPath) &&
                UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(assetPath) != null)
            {
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                    assetPath, new LoadSceneParameters(LoadSceneMode.Single));
                return true;
            }
#endif
            return false;
        }
    }
}
