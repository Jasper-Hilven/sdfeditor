using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace SdfMushrooms.Editor
{
    /// <summary>
    /// Renders the mushroom scene to PNGs for checking a change: scene.png through the scene's camera and a close-up of each mushroom.
    /// <list type="bullet">
    /// <item>Without a window: <c>Unity -batchmode -projectPath Unity -executeMethod SdfMushrooms.Editor.MushroomSnapshot.Capture
    /// -snapshotDir &lt;folder&gt; -quit</c> (without -nographics, so the GPU renders).</item>
    /// <item>In a running editor, which batch mode cannot open: write the folder's path into Unity/Temp/sdf-mushroom-snapshot.txt;
    /// the editor renders the open mushroom scene within a second and deletes the file. The scene is not changed.</item>
    /// </list>
    /// The renders use a hidden camera of their own, placed like the scene's.
    /// </summary>
    public static class MushroomSnapshot
    {
        public const string RequestPath = "Temp/sdf-mushroom-snapshot.txt";

        public static void Capture()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-snapshotDir");
            var directory = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Path.GetTempPath(), "mushroom-snapshots");
            MushroomBuilder.BuildStale();
            Shoot(EditorSceneManager.OpenScene(MushroomBuilder.ScenePath, OpenSceneMode.Single), directory);
        }

        public static void CaptureOpenScene(string directory)
        {
            var scene = SceneManager.GetSceneByPath(MushroomBuilder.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning($"SDF mushrooms: open {MushroomBuilder.ScenePath} to take snapshots");
                return;
            }
            Shoot(scene, directory);
        }

        [MenuItem("Tools/SDF Mushrooms/Save Snapshots")]
        private static void SaveSnapshotsFromMenu() =>
            CaptureOpenScene(Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "Temp", "Snapshots"));

        private static void Shoot(Scene scene, string directory)
        {
            Directory.CreateDirectory(directory);
            var sceneCamera = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<Camera>()).First(c => c != null);
            var cameraObject = EditorUtility.CreateGameObjectWithHideFlags("Snapshot Camera", HideFlags.HideAndDontSave, typeof(Camera));
            try
            {
                var camera = cameraObject.GetComponent<Camera>();
                camera.CopyFrom(sceneCamera);
                var data = cameraObject.AddComponent<HDAdditionalCameraData>();
                if (sceneCamera.TryGetComponent<HDAdditionalCameraData>(out var sceneData))
                    sceneData.CopyTo(data);
                camera.transform.SetPositionAndRotation(sceneCamera.transform.position, sceneCamera.transform.rotation);
                Render(camera, Path.Combine(directory, "scene.png"), 1920, 1080);

                var root = scene.GetRootGameObjects().First(g => g.name == MushroomScene.MushroomsRootName);
                foreach (Transform mushroom in root.transform)
                {
                    var bounds = mushroom.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
                    // The cap and face, from the front and a little above
                    var target = bounds.center + Vector3.up * bounds.extents.y * 0.35f;
                    var distance = bounds.extents.y * 1.25f / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                    camera.transform.position = target + new Vector3(-0.25f, 0.35f, 1).normalized * distance;
                    camera.transform.LookAt(target);
                    Render(camera, Path.Combine(directory, mushroom.name + ".png"), 1600, 1600);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
            Debug.Log($"SDF mushrooms: snapshots written to {directory}");
        }

        private static void Render(Camera camera, string path, int width, int height)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            camera.targetTexture = target;
            // Many frames: the sky and its ambient light settle over the first ones, temporal anti-aliasing over the rest
            for (var frame = 0; frame < 48; frame++)
                camera.Render();
            var active = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = active;
            camera.targetTexture = null;
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
