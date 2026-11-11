using Academy.Builder;
using Academy.Prototype;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Academy.Operations
{
    /// <summary>
    /// Builds the Mission Operations scene in code (no scene asset, no Build Settings needed):
    /// Sun, camera, satellite view, Earth, simulation driver and the ground-station UI.
    /// </summary>
    public static class OperationsSceneFactory
    {
        public const string SceneName = "MissionOperations";

        /// <summary>Open the operations scene. Use <see cref="OperationsSession"/> first to choose what to fly.</summary>
        public static void Open(PanelSettings panelSettings)
        {
            Scene generated = SceneManager.CreateScene(SceneName + " (generated)");
            SceneManager.SetActiveScene(generated);
            Populate(panelSettings);

            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s != generated && s.isLoaded)
                    SceneManager.UnloadSceneAsync(s);
            }
        }

        public static OperationsSimulation Populate(PanelSettings panelSettings)
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.07f, 0.08f, 0.10f);

            var sun = new GameObject("Sun");
            var sunLight = sun.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.intensity = 1.8f;
            sunLight.color = new Color(1f, 0.97f, 0.92f);
            sunLight.shadows = LightShadows.Soft;

            var satellite = new GameObject("Satellite");
            var view = satellite.AddComponent<BuilderSatelliteView>();

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var cam = cameraObject.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.005f, 0.008f, 0.02f);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 5000f;
            cam.fieldOfView = 40f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.transform.position = new Vector3(0.6f, 0.4f, -0.9f);
            cameraObject.transform.LookAt(satellite.transform);
            var orbit = cameraObject.AddComponent<PrototypeOrbitCamera>();
            orbit.target = satellite.transform;
            orbit.distance = 1f;
            orbit.autoRotate = false;

            var earth = new GameObject("Earth").AddComponent<EarthVisual>();

            var driver = new GameObject("Operations").AddComponent<OperationsSimulation>();
            driver.view = view;
            driver.orbitCamera = orbit;
            driver.sunLight = sunLight;
            driver.earth = earth;
            driver.mainCamera = cam;

            var uiObject = new GameObject("Operations UI");
            var document = uiObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            if (panelSettings == null)
                Debug.LogWarning("[Academy] No Panel Settings for the Operations UI; the UI will not be visible.");
            var ui = uiObject.AddComponent<OperationsUI>();
            ui.simulation = driver;
            ui.orbitCamera = orbit;
            return driver;
        }
    }
}
