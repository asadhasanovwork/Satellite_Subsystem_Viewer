using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Academy.Prototype.EditorTools
{
    /// <summary>Menu items: Academy > Prototype.</summary>
    public static class ProceduralSatelliteMenu
    {
        private const string SceneFolder = "Assets/Academy/Prototype/Scenes/";

        [MenuItem("Academy/Prototype/Create Procedural CubeSat Scene (LEO)", false, 0)]
        public static void CreateCubeSatScene()
        {
            CreateScene("ProceduralCubeSat", "Academy/cubesat_3u_prototype.json", "Procedural CubeSat");
        }

        [MenuItem("Academy/Prototype/Create Navigation Satellite Scene (MEO)", false, 1)]
        public static void CreateMeoScene()
        {
            CreateScene("ProceduralNavSat_MEO", "Academy/meo_navsat_prototype.json", "Procedural NavSat");
        }

        [MenuItem("Academy/Prototype/Create Communications Satellite Scene (GEO)", false, 2)]
        public static void CreateGeoScene()
        {
            CreateScene("ProceduralComSat_GEO", "Academy/geo_comsat_prototype.json", "Procedural ComSat");
        }

        private static void CreateScene(string sceneName, string jsonFile, string objectName)
        {
            string scenePath = SceneFolder + sceneName + ".unity";
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Space lighting: dark ambient, warm Sun, faint blue Earthshine fill
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.18f, 0.22f);

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
            fillLight.intensity = 0.35f;
            fillLight.color = new Color(0.45f, 0.6f, 1f);
            fillLight.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(-30f, 140f, 0f);

            var satellite = new GameObject(objectName);
            var builder = satellite.AddComponent<ProceduralSatelliteBuilder>();
            builder.LoadSatellite(jsonFile);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var cam = cameraObject.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.015f, 0.035f);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 2000f;
            cam.fieldOfView = 40f;
            cameraObject.AddComponent<AudioListener>();
            var orbit = cameraObject.AddComponent<PrototypeOrbitCamera>();
            orbit.target = satellite.transform;
            float d = builder.Data != null ? builder.Data.cameraDistance : 1f;
            cameraObject.transform.position = new Vector3(0.55f, 0.33f, -0.77f) * d;
            cameraObject.transform.LookAt(satellite.transform);

            Directory.CreateDirectory(Path.GetDirectoryName(scenePath));
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.Refresh();

            Selection.activeGameObject = satellite;
            SceneView.FrameLastActiveSceneView();
            Debug.Log("[Academy] Created " + scenePath + ". Press Play to watch the deployment.");
        }

        [MenuItem("Academy/Prototype/Rebuild Satellites In Scene", false, 20)]
        public static void RebuildAll()
        {
            foreach (var builder in Object.FindObjectsByType<ProceduralSatelliteBuilder>(FindObjectsSortMode.None))
                builder.Rebuild();
        }
    }
}
