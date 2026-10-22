using System;
using System.IO;
using System.Linq;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SdfStory.Editor
{
    /// <summary>
    /// Builds Assets/Scenes/Home.unity, 42's room, the stage of the story intro's comic panels (ticket 31), from content/scenes/home.txt
    /// (<see cref="HomePlacement"/>) and the meshed prefabs of content/models/home/ (Assets/Mushrooms/&lt;model&gt;.prefab). The scene is
    /// made anew each time and never edited by hand. It is editor-only: it is not added to the build settings' scene list.
    /// <para>
    /// Under Home: Room (the models, one child each, named as their models), Actors (Mushroom 42 on the chair, Mushroom 7 and the four
    /// Chanterelle Hands parked off stage; named as the file names them, which is how the capture finds them) and Lights (the warm Lamp
    /// and the cool Monitor point lights). The scene's Sun is a dim, cool dusk key from the window's side (the toon shaders take only
    /// its direction and hue), the ambient a flat dusk violet, and there is no skybox: the window shows the backdrop card. A Main Camera
    /// stands in the door corner; the capture sets its own.
    /// </para>
    /// Menu Tools > SDF Story > Build Home; in batch mode
    /// <c>Unity -batchmode -quit -projectPath Unity -executeMethod SdfStory.Editor.HomeBuilder.BuildInBatch</c>. x is mirrored and yaw
    /// turned round for Unity's coordinates, as in <c>LevelBuilder</c>.
    /// </summary>
    public static class HomeBuilder
    {
        public const string ScenePath = "Assets/Scenes/Home.unity";
        public const string RootName = "Home";
        public const string RoomName = "Room";
        public const string ActorsName = "Actors";
        public const string LightsName = "Lights";

        /// <summary>content/scenes/home.txt, from the Unity project's folder up.</summary>
        public static string PlacementPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "content", "scenes", "home.txt"));

        [MenuItem("Tools/SDF Story/Build Home")]
        public static void BuildFromMenu() => Build();

        /// <summary>For batch mode: the models' prefabs, then the scene; exits with an error if it could not be built.</summary>
        public static void BuildInBatch()
        {
            RenderPipelineSetup.Ensure();
            MushroomBuilder.BuildStale();
            if (!Build())
                EditorApplication.Exit(1);
        }

        /// <summary>Builds the scene from the placement file; returns whether it could.</summary>
        public static bool Build()
        {
            try
            {
                BuildScene(HomePlacement.Load(PlacementPath));
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"SDF story: could not build {ScenePath}: {exception.Message}\n{exception.StackTrace}");
                return false;
            }
        }

        private static void BuildScene(HomePlacement placement)
        {
            MushroomBuilder.EnsureFolder(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NothingWorthKeepingOpen() ? NewSceneMode.Single : NewSceneMode.Additive);

            // A dim, cool key from the window's side (up and back), so that the toon bands have a direction; the lamp and monitor do the rest
            MushroomScene.BuildLighting(scene, null, new MushroomScene.IndoorLighting
            {
                lightDirection = new Vector3(0.25f, 0.6f, -1).normalized,
                lightColor = new Color(0.62f, 0.6f, 0.95f),
                ambient = placement.Ambient,
            });

            var root = New(RootName, null, scene);
            var room = New(RoomName, root, scene);
            var actors = New(ActorsName, root, scene);
            var missing = 0;
            foreach (var item in placement.Items)
            {
                var prefabPath = $"{MushroomBuilder.PrefabFolder}/{item.Model}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning($"SDF story: home.txt line {item.Line}: there is no prefab {prefabPath} for {item.Model}; mesh content/models (sdf unity) and let the prefabs build");
                    missing++;
                    continue;
                }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = item.Name;
                instance.transform.SetParent((item.Kind == "actor" ? actors : room).transform, false);
                instance.transform.SetLocalPositionAndRotation(ToUnity(item.At), Quaternion.Euler(0, -item.Yaw, 0));
                instance.transform.localScale = Vector3.one * item.Scale;
            }

            var lights = New(LightsName, root, scene);
            foreach (var entry in placement.Lights)
            {
                var lightObject = New(entry.Name, lights, scene);
                lightObject.transform.localPosition = ToUnity(entry.At);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = entry.Color;
                light.intensity = entry.Intensity;
                light.range = entry.Range;
                light.shadows = LightShadows.Soft;
            }

            var camera = MushroomScene.CreateCamera(scene, "Main Camera", placement.CameraFov);
            camera.transform.position = ToUnity(placement.CameraAt);
            camera.transform.rotation = Quaternion.LookRotation(ToUnity(placement.CameraLook) - camera.transform.position);
            // The window is the only sky: no skybox to clear to
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;

            if (missing > 0)
                throw new InvalidOperationException($"{missing} prefabs are missing; mesh content/models first (sdf unity)");
            // Home is an editor-only stage: it is saved, but not added to the build settings' scene list
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"SDF story: built {ScenePath} from content/scenes/home.txt, {placement.Items.Count(i => i.Kind == "model")} models, {placement.Items.Count(i => i.Kind == "actor")} actors, {placement.Lights.Count} lights");
        }

        /// <summary>The pipeline's frame to Unity's: x mirrored.</summary>
        public static Vector3 ToUnity(Vector3 point) => new Vector3(-point.x, point.y, point.z);

        private static GameObject New(string name, GameObject parent, Scene scene)
        {
            var gameObject = new GameObject(name);
            if (parent != null)
                gameObject.transform.SetParent(parent.transform, false);
            else
                SceneManager.MoveGameObjectToScene(gameObject, scene);
            return gameObject;
        }

        private static bool NothingWorthKeepingOpen()
        {
            if (EditorSceneManager.sceneCount != 1)
                return false;
            var active = SceneManager.GetActiveScene();
            return string.IsNullOrEmpty(active.path) && !active.isDirty;
        }
    }
}
