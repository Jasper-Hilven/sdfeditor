using System;
using System.IO;
using System.Linq;
using SdfMushrooms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SdfGui.Editor
{
    /// <summary>
    /// Renders every screen of the GUI scene to a PNG, &lt;screen&gt;.png at 1920 × 1080, the way the Game view shows it: the world
    /// through the scene's camera, then the GUI over it. An overlay canvas is not drawn by Camera.Render, so for the shot the GUI's
    /// canvas is put on a camera of its own (without post-processing, like an overlay) and put back after; that camera draws it over
    /// black and over white, which give its transparency, and it is laid over the world here. The scene is not changed. Tools > SDF GUI > Save Snapshots (into Unity/Temp/GUI Snapshots), or without a window: <c>Unity -batchmode
    /// -projectPath Unity -executeMethod SdfGui.Editor.GuiSnapshot.Capture -snapshotDir &lt;folder&gt; -quit</c>.
    /// </summary>
    public static class GuiSnapshot
    {
        private const int Width = 1920;
        private const int Height = 1080;

        public static void Capture()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-snapshotDir");
            var directory = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Path.GetTempPath(), "gui-snapshots");
            Shoot(EditorSceneManager.OpenScene(GuiScene.ScenePath, OpenSceneMode.Single), directory);
        }

        [MenuItem("Tools/SDF GUI/Save Snapshots")]
        private static void CaptureFromMenu()
        {
            var scene = SceneManager.GetSceneByPath(GuiScene.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning($"SDF GUI: open {GuiScene.ScenePath} to take snapshots");
                return;
            }
            Shoot(scene, Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "Temp", "GUI Snapshots"));
        }

        private static void Shoot(Scene scene, string directory)
        {
            Directory.CreateDirectory(directory);
            WithCameras(scene, (flow, shoot) =>
            {
                var shown = flow.screen;
                try
                {
                    foreach (GuiScreen screen in Enum.GetValues(typeof(GuiScreen)))
                    {
                        flow.Show(screen);
                        shoot(Path.Combine(directory, $"{screen}.png"));
                    }
                }
                finally
                {
                    flow.Show(shown);
                }
            });
            Debug.Log($"SDF GUI: snapshots saved to {directory}");
        }

        /// <summary>Renders the GUI scene as it is now (in play mode too) to a PNG.</summary>
        public static void ShootOpen(Scene scene, string path) => WithCameras(scene, (_, shoot) => shoot(path));

        /// <summary>Hands <paramref name="shots"/> the scene's flow and a way to render the scene to a PNG, with the cameras set up for it.</summary>
        private static void WithCameras(Scene scene, Action<GuiFlow, Action<string>> shots)
        {
            var roots = scene.GetRootGameObjects();
            var flow = roots.Select(g => g.GetComponentInChildren<GuiFlow>(true)).First(f => f != null);
            var worldCamera = roots.Select(g => g.GetComponentInChildren<Camera>()).First(c => c != null);
            var canvas = flow.GetComponent<Canvas>();
            var asyncCompilation = ShaderUtil.allowAsyncCompilation;
            var uiObject = EditorUtility.CreateGameObjectWithHideFlags("Snapshot UI Camera", HideFlags.HideAndDontSave, typeof(Camera));
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                var uiCamera = uiObject.GetComponent<Camera>();
                uiCamera.clearFlags = CameraClearFlags.SolidColor;
                uiCamera.cullingMask = 1 << 5;
                uiCamera.orthographic = true;
                uiCamera.nearClipPlane = 0.1f;
                uiCamera.farClipPlane = 10;
                uiCamera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                worldCamera.cullingMask &= ~(1 << 5);
                QualityPresetCamera.Apply(worldCamera);
                shots(flow, path => Render(worldCamera, uiCamera, canvas, path));
            }
            finally
            {
                worldCamera.cullingMask |= 1 << 5;
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                Object.DestroyImmediate(uiObject);
            }
        }

        private static void Render(Camera worldCamera, Camera uiCamera, Canvas canvas, string path)
        {
            var world = Shoot(worldCamera);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = uiCamera;
            canvas.planeDistance = 1;
            try
            {
                // The scaler sizes the canvas when it is enabled; the views draw when their screen turns on
                var scaler = canvas.GetComponent<CanvasScaler>();
                scaler.enabled = false;
                scaler.enabled = true;
                foreach (var view in canvas.GetComponentsInChildren<GuiView>())
                    view.Refresh();
                Canvas.ForceUpdateCanvases();
                uiCamera.backgroundColor = Color.black;
                var overBlack = Shoot(uiCamera);
                uiCamera.backgroundColor = Color.white;
                var overWhite = Shoot(uiCamera);
                for (var i = 0; i < world.Length; i++)
                {
                    var b = overBlack[i];
                    var w = overWhite[i];
                    var alpha = Mathf.Clamp01(1 - ((w.r - b.r) + (w.g - b.g) + (w.b - b.b)) / 3);
                    world[i] = new Color(b.r + world[i].r * (1 - alpha), b.g + world[i].g * (1 - alpha), b.b + world[i].b * (1 - alpha), 1);
                }
            }
            finally
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
            }
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.SetPixels(world);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }

        private static Color[] Shoot(Camera camera)
        {
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var active = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();
                var pixels = image.GetPixels();
                Object.DestroyImmediate(image);
                return pixels;
            }
            finally
            {
                RenderTexture.active = active;
                camera.targetTexture = null;
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }
}
