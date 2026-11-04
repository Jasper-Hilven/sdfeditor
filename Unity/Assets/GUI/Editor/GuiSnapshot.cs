using System;
using System.IO;
using System.Linq;
using SdfMushrooms;
using SdfPlatform;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using TMPro;
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
        // The snapshot's size; -snapshotSize 1728x1080 checks another aspect (a 16:10 laptop, a wide browser window)
        private static int Width = 1920;
        private static int Height = 1080;
        // Only these screens, when -snapshotScreens Victory,Defeat is given
        private static string[] only;

        /// <summary>Batch entry: -snapshotDir folder, and optionally -snapshotSize WxH and -snapshotScreens A,B.</summary>
        public static void Capture()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name)
            {
                var i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            var directory = Arg("-snapshotDir") ?? Path.Combine(Path.GetTempPath(), "gui-snapshots");
            var size = Arg("-snapshotSize")?.Split('x');
            if (size is { Length: 2 } && int.TryParse(size[0], out var w) && int.TryParse(size[1], out var h))
                (Width, Height) = (w, h);
            only = Arg("-snapshotScreens")?.Split(',');
            // -snapshotTouch: as on a touch device (the touch controls over the HUD), by the editor's mobile switch put back after
            var touch = Array.IndexOf(args, "-snapshotTouch") >= 0;
            // -snapshotSafeArea: a pretend notch (6% of the width at each side, 5% at the bottom, 2% at the top, a landscape phone's), the
            // unsafe band tinted red in the picture
            var safe = Array.IndexOf(args, "-snapshotSafeArea") >= 0;
            GuiSafeArea.Simulated = safe ? new SafeInsets(0.06f, 0.05f, 0.06f, 0.02f) : (SafeInsets?)null;
            var before = EditorPrefs.GetBool(SdfPlatform.Device.EditorMobileKey, false);
            try
            {
                if (touch)
                {
                    EditorPrefs.SetBool(SdfPlatform.Device.EditorMobileKey, true);
                    SdfPlatform.Device.Refresh();
                }
                Shoot(EditorSceneManager.OpenScene(GuiScene.ScenePath, OpenSceneMode.Single), directory);
                File.WriteAllText(Path.Combine(directory, "audit.txt"), AuditLog.ToString());
            }
            finally
            {
                GuiSafeArea.Simulated = null;
                if (touch)
                {
                    EditorPrefs.SetBool(SdfPlatform.Device.EditorMobileKey, before);
                    SdfPlatform.Device.Refresh();
                }
            }
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
                var testPanel = flow.testPanel;
                // Shots of chosen screens leave out the developer's test panel, which covers the left of the screen
                flow.testPanel = false;
                try
                {
                    foreach (GuiScreen screen in Enum.GetValues(typeof(GuiScreen)))
                    {
                        if (only != null && Array.IndexOf(only, screen.ToString()) < 0)
                            continue;
                        // The yes/no card shows over a screen: the pause menu, as in a match
                        if (screen == GuiScreen.Confirm)
                        {
                            // Over Settings with the reset question (ticket 63): the longest question the card holds
                            flow.Show(GuiScreen.Pause);
                            flow.Show(GuiScreen.Settings);
                            flow.Ask(GuiFlow.ResetQuestion, null);
                        }
                        else
                            flow.Show(screen);
                        shoot(Path.Combine(directory, $"{screen}.png"));
                        // Settings again scrolled to its last row
                        if (screen == GuiScreen.Settings)
                        {
                            var scroll = flow.GetComponentsInChildren<ScrollRect>(false).FirstOrDefault(s => s.isActiveAndEnabled);
                            if (scroll != null)
                            {
                                scroll.verticalNormalizedPosition = 0;
                                shoot(Path.Combine(directory, "SettingsBottom.png"));
                                scroll.verticalNormalizedPosition = 1;
                            }
                        }
                    }
                    flow.Match.end.Clear();
                }
                finally
                {
                    flow.testPanel = testPanel;
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
            RenderTexture sizing = null;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = uiCamera;
            canvas.planeDistance = 1;
            try
            {
                // The scaler sizes the canvas (to the camera's target, the snapshot's size) when it is enabled; the views draw when their screen turns on
                sizing = new RenderTexture(Width, Height, 24);
                uiCamera.targetTexture = sizing;
                canvas.GetComponent<GuiScaleFloor>()?.Apply();
                var scaler = canvas.GetComponent<CanvasScaler>();
                scaler.enabled = false;
                scaler.enabled = true;
                Canvas.ForceUpdateCanvases();
                foreach (var area in canvas.GetComponentsInChildren<GuiSafeArea>())
                    area.Apply(true);
                foreach (var layout in canvas.GetComponentsInChildren<GuiTouchLayout>(true))
                    layout.Apply();
                Canvas.ForceUpdateCanvases();
                foreach (var view in canvas.GetComponentsInChildren<GuiView>())
                    view.Refresh();
                foreach (var fit in canvas.GetComponentsInChildren<GuiFitInside>())
                    fit.Refit();
                Canvas.ForceUpdateCanvases();
                Audit(canvas, Path.GetFileNameWithoutExtension(path));
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
                uiCamera.targetTexture = null;
                if (sizing != null)
                    Object.DestroyImmediate(sizing);
            }
            if (GuiSafeArea.Simulated is { } unsafeBand)
                Tint(world, unsafeBand);
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.SetPixels(world);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }

        // What the fit check lists per screen (audit.txt next to the pictures): texts under 12 px on the screen, texts wider or taller than
        // their box, and anything drawn past the canvas' edge (full-screen backgrounds, the scrolled rows and the credits' roll aside)
        private static readonly System.Text.StringBuilder AuditLog = new System.Text.StringBuilder();
        private const float MinPixels = 12;

        private static void Audit(Canvas canvas, string shot)
        {
            var canvasRect = (RectTransform)canvas.transform;
            var size = canvasRect.rect.size;
            var toPixels = Height / size.y;
            var name = shot == "SettingsBottom" ? "Settings" : shot;
            var found = new System.Collections.Generic.SortedSet<string>();
            foreach (var root in canvas.GetComponentsInChildren<GuiScreenRoot>(false))
            {
                var wanted = root.screen.ToString() == name || (name == "Confirm" && root.screen == GuiScreen.Settings) ||
                             (name == "Hud" && root.GetComponent<TouchLayer>() != null);
                if (!wanted || root.alwaysOn)
                    continue;
                // What must stay inside: the screen, or its safe area (the root is inset by it)
                var rootCorners = new Vector3[4];
                ((RectTransform)root.transform).GetWorldCorners(rootCorners);
                var lo = (Vector2)canvasRect.InverseTransformPoint(rootCorners[0]);
                var hi = (Vector2)canvasRect.InverseTransformPoint(rootCorners[2]);
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(false))
                {
                    if (string.IsNullOrWhiteSpace(text.text) || text.GetComponentInParent<CanvasGroup>() is { alpha: < 0.05f })
                        continue;
                    var pixels = text.fontSize * (text.transform.lossyScale.y / canvasRect.lossyScale.y) * toPixels;
                    var rect = text.rectTransform.rect;
                    var scale = text.transform.lossyScale.y / canvasRect.lossyScale.y;
                    var label = $"'{text.text.Replace("\n", " ").Trim()}' ({text.gameObject.name})";
                    if (label.Length > 70)
                        label = label.Substring(0, 70) + "...)";
                    if (pixels < MinPixels)
                        found.Add($"small {pixels:0.0}px {label}");
                    if (text.textWrappingMode == TextWrappingModes.NoWrap ? text.renderedWidth > rect.width + 2 : text.renderedHeight > rect.height + 2)
                        found.Add($"overflow {label}: {text.renderedWidth:0}x{text.renderedHeight:0} in {rect.width:0}x{rect.height:0}");
                    if (name != "Credits" && !InScroll(text.transform))
                        Edge(text.rectTransform, canvasRect, lo, hi, "text " + label, found, text.renderedWidth * scale, text.renderedHeight * scale);
                }
                foreach (var graphic in root.GetComponentsInChildren<Graphic>(false))
                {
                    if (graphic is TMP_Text || graphic.color.a < 0.05f || graphic is RawImage || InScroll(graphic.transform))
                        continue;
                    var rect = graphic.rectTransform;
                    var world = new Vector3[4];
                    rect.GetWorldCorners(world);
                    var width = (canvasRect.InverseTransformPoint(world[2]).x - canvasRect.InverseTransformPoint(world[0]).x);
                    if (width >= size.x * 0.95f || graphic.name is "Dim" or "Backdrop" or "Glow" or "Vignette" or "Spore Tint")
                        continue; // backgrounds and effects fill or pass the screen on purpose
                    Edge(rect, canvasRect, lo, hi, "shape " + graphic.name, found);
                }
            }
            foreach (var line in found)
                AuditLog.AppendLine($"{shot}: {line}");
        }

        private static bool InScroll(Transform t) => t.GetComponentInParent<RectMask2D>() != null;

        private static void Edge(RectTransform rect, RectTransform canvasRect, Vector2 lo, Vector2 hi, string what, System.Collections.Generic.ISet<string> found, float width = -1, float height = -1)
        {
            var world = new Vector3[4];
            rect.GetWorldCorners(world);
            var a = (Vector2)canvasRect.InverseTransformPoint(world[0]);
            var b = (Vector2)canvasRect.InverseTransformPoint(world[2]);
            // A text is as wide as it is drawn, round the middle of its box
            if (width >= 0)
            {
                var middle = (a + b) * 0.5f;
                var half = new Vector2(Mathf.Min(width, b.x - a.x), Mathf.Min(height, b.y - a.y)) * 0.5f;
                a = middle - half;
                b = middle + half;
            }
            var slack = 3f;
            if (a.x < lo.x - slack || b.x > hi.x + slack || a.y < lo.y - slack || b.y > hi.y + slack)
                found.Add($"off screen {what}: x {a.x - lo.x:0}..{b.x - lo.x:0} of {hi.x - lo.x:0}, y {a.y - lo.y:0}..{b.y - lo.y:0} of {hi.y - lo.y:0}");
        }

        // The band the safe area leaves out, tinted red (the snapshots' check that nothing sits in it)
        private static void Tint(Color[] pixels, SafeInsets insets)
        {
            for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++)
                {
                    var outside = x < insets.Left * Width || x >= (1 - insets.Right) * Width || y < insets.Bottom * Height || y >= (1 - insets.Top) * Height;
                    if (outside)
                        pixels[y * Width + x] = Color.Lerp(pixels[y * Width + x], Color.red, 0.35f);
                }
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
