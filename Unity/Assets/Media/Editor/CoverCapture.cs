using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SdfLevel.Editor;
using SdfMushrooms;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SdfMedia.Editor
{
    /// <summary>
    /// How one cover image is framed: its file and size, where the camera stands and looks in the staged scene (<see cref="CoverStage"/>'s
    /// own space, in metres: x to the right, y up, z away from the camera towards the backdrop, the origin on the ground at the middle of
    /// the stage), and where the game's logo goes on top. Edit the table <see cref="CoverCapture.Formats"/> to change a framing.
    /// </summary>
    internal sealed class CoverFormat
    {
        /// <summary>The PNG's name in Media/Covers.</summary>
        public string FileName;
        /// <summary>The finished image's size in pixels (the render is made at <see cref="CoverCapture.Super"/> times this).</summary>
        public int Width, Height;
        /// <summary>The camera's place in the stage's space.</summary>
        public Vector3 CameraPosition;
        /// <summary>
        /// Where the hero's middle (<see cref="CoverStage.HeroCentre"/>) is on the picture: its share from the left and from the top.
        /// The camera is turned to put him there.
        /// </summary>
        public Vector2 Anchor;
        /// <summary>The camera's vertical field of view, degrees.</summary>
        public float FieldOfView;
        /// <summary>The camera tipped about its view axis, degrees: a little slant makes the low shot more dramatic.</summary>
        public float Roll;
        /// <summary>The logo's middle as a share of the image, from its left and from its top.</summary>
        public Vector2 LogoCentre;
        /// <summary>The logo's width as a share of the image's width.</summary>
        public float LogoShare;

        public float Aspect => Width / (float)Height;
    }

    /// <summary>
    /// Renders the three CrazyGames cover images of the game into the repository's Media/Covers folder (next to Unity/):
    /// <c>cover-landscape.png</c> 1920 × 1080, <c>cover-portrait.png</c> 800 × 1200 and <c>cover-square.png</c> 800 × 800. They are
    /// staged pictures, not screenshots: <see cref="CoverStage"/> builds one scene, "mid-air minigun" (the red amanita scout leaping at
    /// the camera with his minigun blazing, two blue chanterelle scouts diving aside and a toon explosion behind them) on the Meadow
    /// level's own ground and sky, each format gets its own camera framing of that same scene so that the three read as one game, and
    /// <see cref="CoverLogo"/> lays the real logo of the title screen (the spotted cap, MINIGUN MUSHROOMS and the Second Place ribbon;
    /// the title and nothing else: CrazyGames allows no other text) over the render. No other text, no buttons, no borders.
    /// <para>
    /// The scene is rendered at the Epic quality level (the preset of <see cref="RenderPipelineSetup.Epic"/>: up to eight samples a pixel, as many as the GPU gives a render texture, LOD0
    /// far out, soft shadows, high-quality bloom) at <see cref="Super"/> times the final size, then averaged down by a bilinear blit
    /// exactly half the size, which is a 2 × 2 box filter: crisp edges, no blur and no pixelation. The logo is rendered the same way, twice
    /// (over black and over white, which gives its transparency, as GuiSnapshot does) and laid over the scene at the final size.
    /// </para>
    /// <para>
    /// Tools > SDF Media > Render Covers (all three), Render Cover Landscape / Portrait / Square (one), Preview Cover Stage and Clear Cover
    /// Stage (builds the stage and leaves it open in the editor, with the Scene view on a format's camera, to look at the poses; the
    /// logo is only in the render). The editor's open scenes are left as they are: the level is opened next to them for the render and
    /// the stage is a temporary scene, both closed afterwards. Where to tweak: the framings in <see cref="Formats"/>, the poses and
    /// positions in <see cref="CoverStage"/> (the block "Composition" at its top), the level and the quality in the constants below.
    /// </para>
    /// </summary>
    public static class CoverCapture
    {
        /// <summary>The level whose ground and sky are the backdrop (Assets/Scenes/&lt;Level&gt;.unity); "fungal" is the other open-air one.</summary>
        public const string Level = "meadow";
        /// <summary>Each cover is rendered at this many times its size and averaged down.</summary>
        public const int Super = 2;
        /// <summary>Where the covers are written, from the repository root.</summary>
        public const string OutputFolder = "Media/Covers";
        /// <summary>The cover is the game's name; the logo's Second Place ribbon is part of that logo. Set false to leave the ribbon out.</summary>
        public const bool ShowSubtitle = true;

        /// <summary>
        /// The three formats of one picture, "mid-air minigun" (see <see cref="CoverStage"/>): the camera is low, a few metres in front of
        /// the red hero (in <see cref="CoverStage"/>'s space he leaps at the origin, the two blue scouts dive aside about 3.5 m behind him and
        /// the explosion is 9 m back), turned up a little and rolled for a dynamic tilt. <see cref="CoverFormat.Anchor"/> puts his middle
        /// right of the middle in the landscape (the logo is upper left), low in the portrait and the square (the logo is on top).
        /// </summary>
        internal static readonly CoverFormat[] Formats =
        {
            new CoverFormat
            {
                FileName = "cover-landscape.png", Width = 1920, Height = 1080,
                CameraPosition = new Vector3(-0.6f, 0.5f, -4.2f), Anchor = new Vector2(0.66f, 0.50f), FieldOfView = 42, Roll = -4,
                LogoCentre = new Vector2(0.265f, 0.27f), LogoShare = 0.47f,
            },
            new CoverFormat
            {
                FileName = "cover-portrait.png", Width = 800, Height = 1200,
                CameraPosition = new Vector3(0, 0.5f, -3.9f), Anchor = new Vector2(0.5f, 0.66f), FieldOfView = 54, Roll = -5,
                LogoCentre = new Vector2(0.5f, 0.145f), LogoShare = 0.92f,
            },
            new CoverFormat
            {
                FileName = "cover-square.png", Width = 800, Height = 800,
                CameraPosition = new Vector3(0, 0.5f, -3.9f), Anchor = new Vector2(0.5f, 0.64f), FieldOfView = 48, Roll = -4,
                LogoCentre = new Vector2(0.5f, 0.165f), LogoShare = 0.78f,
            },
        };

        /// <summary>
        /// The portrait and square covers again at the preview videos' sizes (1080 × 1620 and 1080 × 1080, the same framings), for the
        /// videos' opening frame (PreviewVideoEncoder), so it is not an 800-pixel cover scaled up. The landscape cover is already 1920 × 1080.
        /// </summary>
        internal static readonly CoverFormat[] VideoFormats =
        {
            AtSize(Formats[1], "cover-portrait-video.png", 1080, 1620),
            AtSize(Formats[2], "cover-square-video.png", 1080, 1080),
        };

        private static CoverFormat AtSize(CoverFormat format, string fileName, int width, int height) => new CoverFormat
        {
            FileName = fileName, Width = width, Height = height, CameraPosition = format.CameraPosition, Anchor = format.Anchor,
            FieldOfView = format.FieldOfView, Roll = format.Roll, LogoCentre = format.LogoCentre, LogoShare = format.LogoShare,
        };

        /// <summary>Media/Covers next to the Unity project's folder.</summary>
        public static string OutputPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", OutputFolder));

        private static CoverSession preview;

        [MenuItem("Tools/SDF Media/Render Covers")]
        public static void RenderFromMenu() => Render(Formats.Concat(VideoFormats).ToArray());

        [MenuItem("Tools/SDF Media/Render Cover Landscape")]
        public static void RenderLandscapeFromMenu() => Render(new[] { Formats[0] });

        [MenuItem("Tools/SDF Media/Render Cover Portrait")]
        public static void RenderPortraitFromMenu() => Render(new[] { Formats[1] });

        [MenuItem("Tools/SDF Media/Render Cover Square")]
        public static void RenderSquareFromMenu() => Render(new[] { Formats[2] });

        [MenuItem("Tools/SDF Media/Preview Cover Stage")]
        public static void PreviewFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("SDF media: stop play mode to preview the cover stage");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            ClearPreview();
            Prepare();
            preview = CoverSession.Open(false);
            // The Scene view looks through the landscape camera; Tools > SDF Media > Preview... is not needed again for the other formats:
            // move the Scene view with the camera objects "Cover Camera ..." in the stage (Align View to Selected)
            var sceneView = SceneView.lastActiveSceneView;
            foreach (var format in Formats)
            {
                preview.Stage.PlaceCameraObject(format);
                if (format == Formats[0] && sceneView != null)
                {
                    preview.Stage.CameraPose(format, out var position, out var rotation);
                    sceneView.orthographic = false;
                    sceneView.LookAt(position + rotation * Vector3.forward * 5, rotation, 5, false, true);
                }
            }
            Debug.Log("SDF media: the cover stage is open (temporary scene 'Cover Stage' next to the level). The Scene view shows the landscape framing; select a " +
                      "'Cover Camera ...' object and use GameObject > Align View to Selected for the others. Tools > SDF Media > Clear Cover Stage closes it.");
        }

        [MenuItem("Tools/SDF Media/Clear Cover Stage")]
        public static void ClearFromMenu() => ClearPreview();

        /// <summary>Closes a stage left open by the preview, also one that survived a script reload.</summary>
        public static void ClearPreview()
        {
            if (preview != null)
            {
                preview.Dispose();
                preview = null;
            }
            for (var i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.GetRootGameObjects().Any(g => g.name == CoverStage.RootName))
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void Prepare()
        {
            RenderPipelineSetup.Ensure();
            MushroomBuilder.BuildStale();
            LevelBuilder.BuildStale();
        }

        /// <summary>
        /// Renders every format, the three covers and the two at the videos' sizes, and exits the editor (code 0, or 1 if anything failed).
        /// Without a window: <c>tools/unity-batch.sh covers -executeMethod SdfMedia.Editor.CoverCapture.RenderInBatch [-coverDir &lt;folder&gt;]</c>
        /// (without -nographics, so the GPU renders; the folder is Media/Covers by default). The level's scene and the models are rebuilt
        /// first if they are stale.
        /// </summary>
        public static void RenderInBatch()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-coverDir");
            var folder = index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : null;
            var ok = false;
            try
            {
                ok = Render(Formats.Concat(VideoFormats).ToArray(), folder, true);
            }
            catch (Exception exception)
            {
                Debug.LogError($"SDF media: the cover render failed: {exception}");
            }
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>Renders the formats into <see cref="OutputPath"/> (or another folder); false if it did not.</summary>
        internal static bool Render(IReadOnlyList<CoverFormat> formats, string outputFolder = null, bool batch = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("SDF media: stop play mode to render the covers");
                return false;
            }
            // (batch mode has no dialog to ask, and no scenes of anyone's to save)
            if (!batch && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;
            ClearPreview();
            Prepare();
            var folder = outputFolder ?? OutputPath;
            var succeeded = false;
            Directory.CreateDirectory(folder);
            var asyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            CoverSession session = null;
            try
            {
                session = CoverSession.Open(true);
                foreach (var format in formats)
                {
                    EditorUtility.DisplayProgressBar("SDF media: covers", format.FileName, formats.ToList().IndexOf(format) / (float)formats.Count);
                    var path = Path.Combine(folder, format.FileName);
                    RenderOne(session, format, path);
                    Debug.Log($"SDF media: {format.FileName} ({format.Width} x {format.Height}) rendered to {path}");
                }
                succeeded = true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"SDF media: could not render the covers: {exception.Message}\n{exception.StackTrace}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                session?.Dispose();
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
            }
            if (succeeded)
                Debug.Log($"SDF media: covers written to {folder}");
            return succeeded;
        }

        // ---------------------------------------------------------------------------------------------------------------- one cover

        private static void RenderOne(CoverSession session, CoverFormat format, string path)
        {
            var big = new Vector2Int(format.Width * Super, format.Height * Super);
            var cameraObject = EditorUtility.CreateGameObjectWithHideFlags("Cover Camera", HideFlags.HideAndDontSave, typeof(Camera));
            RenderTexture target = null;
            Color32[] world;
            try
            {
                var camera = cameraObject.GetComponent<Camera>();
                CopyCamera(session.SceneCamera, camera);
                session.Stage.PlaceCamera(camera, format);
                // The logo is drawn by its own camera on the UI layer; the world camera leaves that layer alone
                camera.cullingMask &= ~(1 << CoverLogo.UiLayer);
                camera.allowMSAA = true;

                if (!session.Warm)
                {
                    // Once, small: shaders compiled and the level's shadow map and instances made before the picture that counts
                    var warm = new RenderTexture(256, 144, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    camera.targetTexture = warm;
                    camera.Render();
                    camera.Render();
                    camera.targetTexture = null;
                    warm.Release();
                    Object.DestroyImmediate(warm);
                    session.Warm = true;
                }

                // The quality level's samples, but no more than the GPU gives a render texture of this size: Epic asks for 8, Apple GPUs
                // stop at 4, and a target that was made with 4 while the pipeline asks it for 8 fails the final blit pass ("Attachment 0 was
                // created with 4 samples but 8 samples were requested") and stays black
                var wanted = UniversalRenderPipeline.asset != null ? Mathf.Max(1, UniversalRenderPipeline.asset.msaaSampleCount) : 1;
                var samples = Mathf.Clamp(SystemInfo.GetRenderTextureSupportedMSAASampleCount(
                    new RenderTextureDescriptor(big.x, big.y, RenderTextureFormat.ARGB32, 24)), 1, wanted);
                target = new RenderTexture(big.x, big.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = samples };
                camera.targetTexture = target;
                camera.aspect = format.Aspect;
                camera.Render();
                world = Downscale(target, format.Width, format.Height);
                camera.targetTexture = null;
                // A render that failed leaves the target cleared to black; better an error than a cover with the logo over nothing
                if (!world.Any(p => p.r > 8 || p.g > 8 || p.b > 8))
                    throw new InvalidOperationException($"the 3D render of {format.FileName} is black (see the log for render errors)");
            }
            finally
            {
                if (target != null)
                {
                    target.Release();
                    Object.DestroyImmediate(target);
                }
                Object.DestroyImmediate(cameraObject);
            }

            CoverLogo.Draw(session.StageScene, format, out var overBlack, out var overWhite);
            File.WriteAllBytes(path, Composite(world, overBlack, overWhite, format.Width, format.Height));
        }

        /// <summary>
        /// The camera as the level's own (its settings, post-processing with tone mapping and bloom, and the quality level's anti-aliasing),
        /// as the other snapshot tools copy it.
        /// </summary>
        private static void CopyCamera(Camera sceneCamera, Camera camera)
        {
            camera.CopyFrom(sceneCamera);
            var sceneData = sceneCamera.GetUniversalAdditionalCameraData();
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = sceneData.renderPostProcessing;
            data.antialiasingQuality = sceneData.antialiasingQuality;
            data.renderShadows = sceneData.renderShadows;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = Mathf.Max(sceneCamera.farClipPlane, 600);
            QualityPresetCamera.Apply(camera);
        }

        /// <summary>
        /// A render texture averaged down to the size given (half its size: a bilinear sample between four texels is their mean, so this
        /// is a box filter, taken in linear light) and read back, rows from the bottom.
        /// </summary>
        internal static Color32[] Downscale(RenderTexture source, int width, int height)
        {
            var small = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { filterMode = FilterMode.Bilinear };
            var filter = source.filterMode;
            var active = RenderTexture.active;
            try
            {
                source.filterMode = FilterMode.Bilinear;
                Graphics.Blit(source, small);
                RenderTexture.active = small;
                var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                var pixels = image.GetPixels32();
                Object.DestroyImmediate(image);
                return pixels;
            }
            finally
            {
                RenderTexture.active = active;
                source.filterMode = filter;
                small.Release();
                Object.DestroyImmediate(small);
            }
        }

        /// <summary>
        /// The scene with the logo over it as a PNG: the logo rendered over black is its colour times its coverage, and over white the
        /// same plus what is not covered, so the difference of the two is the transparency and the scene shows through by that much.
        /// </summary>
        private static byte[] Composite(Color32[] world, Color32[] overBlack, Color32[] overWhite, int width, int height)
        {
            var output = new Color32[width * height];
            for (var i = 0; i < output.Length; i++)
            {
                var black = overBlack[i];
                var white = overWhite[i];
                var back = world[i];
                // What the logo lets through, 0 to 255
                var through = Mathf.Clamp(((white.r - black.r) + (white.g - black.g) + (white.b - black.b)) / 3, 0, 255);
                output[i] = new Color32(
                    (byte)Mathf.Min(255, black.r + back.r * through / 255),
                    (byte)Mathf.Min(255, black.g + back.g * through / 255),
                    (byte)Mathf.Min(255, black.b + back.b * through / 255), 255);
            }
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.SetPixels32(output);
            image.Apply();
            var bytes = image.EncodeToPNG();
            Object.DestroyImmediate(image);
            return bytes;
        }
    }

    /// <summary>
    /// The scenes a cover is rendered in, opened and put back: the level next to the editor's open scenes (the active scene gives the
    /// sky and the ambient light), a temporary empty scene that holds the stage and the logo, the Epic quality level, and the other
    /// scenes' renderers and the level's player kept from drawing. Disposing it undoes all of that.
    /// </summary>
    internal sealed class CoverSession : IDisposable
    {
        private Scene backdrop;
        private bool openedBackdrop;
        private Scene previousActive;
        private int previousQuality = -1;
        private readonly List<Renderer> hidden = new List<Renderer>();

        public Scene StageScene { get; private set; }
        public CoverStage Stage { get; private set; }
        /// <summary>The level's player camera, which the cover cameras copy their settings from.</summary>
        public Camera SceneCamera { get; private set; }
        /// <summary>Whether the first small render that warms the shaders is done.</summary>
        public bool Warm;

        /// <param name="forRender">True to hide the other open scenes (a render shows only the stage and the level); the preview keeps them.</param>
        public static CoverSession Open(bool forRender)
        {
            var session = new CoverSession();
            try
            {
                session.Setup(forRender);
            }
            catch
            {
                session.Dispose();
                throw;
            }
            return session;
        }

        private void Setup(bool forRender)
        {
            var path = LevelBuilder.ScenePathOf($"{LevelBuilder.GeneratedFolder}/{CoverCapture.Level}");
            if (!File.Exists(path))
                throw new InvalidOperationException($"there is no {path} (build the level first)");
            previousActive = SceneManager.GetActiveScene();
            backdrop = SceneManager.GetSceneByPath(path);
            openedBackdrop = !backdrop.IsValid() || !backdrop.isLoaded;
            if (openedBackdrop)
                backdrop = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            StageScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            // The sky and the ambient light are the active scene's
            SceneManager.SetActiveScene(backdrop);

            var player = backdrop.GetRootGameObjects().FirstOrDefault(g => g.name == LevelBuilder.PlayerName);
            if (player == null)
                throw new InvalidOperationException($"{path} has no {LevelBuilder.PlayerName} (its camera is the cover camera's model)");
            SceneCamera = player.GetComponentInChildren<Camera>();

            if (forRender)
            {
                for (var i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded || scene == backdrop || scene == StageScene)
                        continue;
                    hidden.AddRange(scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>()).Where(r => !r.forceRenderingOff));
                }
                hidden.AddRange(player.GetComponentsInChildren<Renderer>().Where(r => !r.forceRenderingOff));
                foreach (var renderer in hidden)
                    renderer.forceRenderingOff = true;
            }

            // Epic: the preset is found by name in the project's quality levels
            previousQuality = QualitySettings.GetQualityLevel();
            var epic = Array.IndexOf(QualitySettings.names, RenderPipelineSetup.Epic.Name);
            if (epic < 0)
                Debug.LogWarning($"SDF media: no quality level '{RenderPipelineSetup.Epic.Name}'; the covers use the current one");
            else
                QualitySettings.SetQualityLevel(epic, true);

            Physics.SyncTransforms();
            Stage = new CoverStage(StageScene, player.transform.position.y);
        }

        public void Dispose()
        {
            Stage?.Dispose();
            Stage = null;
            foreach (var renderer in hidden)
                if (renderer != null)
                    renderer.forceRenderingOff = false;
            hidden.Clear();
            if (previousQuality >= 0)
                QualitySettings.SetQualityLevel(previousQuality, true);
            previousQuality = -1;
            if (previousActive.IsValid() && previousActive.isLoaded)
                SceneManager.SetActiveScene(previousActive);
            if (StageScene.IsValid() && StageScene.isLoaded)
                EditorSceneManager.CloseScene(StageScene, true);
            if (openedBackdrop && backdrop.IsValid() && backdrop.isLoaded)
                EditorSceneManager.CloseScene(backdrop, true);
            openedBackdrop = false;
        }
    }
}
