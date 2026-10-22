using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SDFPipeline.Story;
using SdfLevel.Editor;
using SdfStory.Layout;
using SdfMushrooms;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SdfStory.Editor
{
    /// <summary>
    /// Renders the story intro's comic panels (ticket 31): for each panel of content/story/intro.txt (<see cref="StoryFile"/>) it opens the
    /// stage (Home.unity, Meadow.unity, or an empty stage for the warp), poses the camera, actors, props, screen and lights, and renders
    /// each frame at twice the size, in two passes with the comic look switched on (SdfComic.hlsl: hard bands, a stronger rim, wider
    /// outlines, the room's lamp and monitor as lights): a colour pass and an aux pass that holds the normal, depth and toon band. The
    /// comic post pass (StoryComicPost.shader) draws the ink line, the halftone and the paper over the colour from the aux pass; the result
    /// is downsampled to 1920 × 1080 and written to Assets/Story/Panels/panel-NN.png. <see cref="StoryLettering"/> draws the comic devices,
    /// balloons, captions, SFX and the ink border over each frame's render (31.6). Nothing of it ships: the comic look is off in the
    /// game, and this, the post shader and the screen shader are in an Editor folder.
    /// <para>
    /// Tools > SDF Story > Render Panels, or without a window: <c>Unity -batchmode -quit -projectPath Unity -executeMethod
    /// SdfStory.Editor.StoryCapture.CaptureInBatch [-panel 1[,5,7]] [-panelDir Assets/Story/Panels]</c> (without -nographics, so the GPU renders).
    /// </para>
    /// </summary>
    public static class StoryCapture
    {
        public const string PanelFolder = "Assets/Story/Panels";
        public const string ScreenFolder = "Assets/Story/Screens";
        public const int Width = 1920;
        public const int Height = 1080;
        /// <summary>Each frame is rendered at this many times its size and averaged down.</summary>
        public const int Super = 2;

        private const string PostShaderPath = "Assets/Story/Editor/StoryComicPost.shader";
        private const string ScreenShaderPath = "Assets/Story/Editor/StoryScreen.shader";
        private const string MeadowScenePath = "Assets/Scenes/Meadow.unity";

        /// <summary>The shaders that write the aux pass; anything else is left out of it.</summary>
        private static readonly string[] ToonShaders =
            { "SdfMushrooms/Toon", "SdfMushrooms/Toon Outline", "SdfMushrooms/Toon Far", "SdfMushrooms/Ground", "SdfMushrooms/RoomFace", "SdfStory/Screen" };

        [MenuItem("Tools/SDF Story/Render Panels")]
        public static void RenderFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            RenderPanels(null, PanelFolder);
        }

        [MenuItem("Tools/SDF Story/Render Panel 1")]
        public static void RenderFirstFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            RenderPanels(new[] { 1 }, PanelFolder);
        }

        /// <summary>For batch mode: all panels, or the one of -panel N, into -panelDir (the panel folder by default).</summary>
        public static void CaptureInBatch()
        {
            var args = Environment.GetCommandLineArgs();
            string Argument(string name)
            {
                var index = Array.IndexOf(args, name);
                return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
            }
            // -panel 3, or several as -panel 3,5,7
            var only = Argument("-panel")?.Split(',').Select(int.Parse).ToArray();
            try
            {
                RenderPanels(only, Argument("-panelDir") ?? PanelFolder);
            }
            catch (Exception exception)
            {
                Debug.LogError($"SDF story: could not render the panels: {exception.Message}\n{exception.StackTrace}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Renders the panels of <paramref name="only"/> (every panel when null) into the folder.</summary>
        public static void RenderPanels(IReadOnlyCollection<int> only, string folder)
        {
            RenderPipelineSetup.Ensure();
            MushroomBuilder.BuildStale();
            var file = StoryFile.Load(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "content", "story", StoryFile.FileName)));
            var panels = file.Panels.Where(p => only == null || only.Contains(p.Number)).ToList();
            if (panels.Count == 0)
                throw new InvalidOperationException($"there is no such panel in the story file");
            Directory.CreateDirectory(folder);
            var asyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                foreach (var panel in panels)
                {
                    var path = Path.Combine(folder, $"panel-{panel.Number:00}.png");
                    RenderPanel(panel, path);
                    Debug.Log($"SDF story: panel {panel.Number} rendered to {path}");
                }
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                Shader.SetGlobalVector("_SdfComic", Vector4.zero);
            }
            AssetDatabase.Refresh();
            foreach (var panel in panels)
                ApplyImportSettings($"{PanelFolder}/panel-{panel.Number:00}.png");
        }

        /// <summary>No mipmaps, crunch compressed, a size for the web: the panels are shown as they are, full screen.</summary>
        public static void ApplyImportSettings(string assetPath)
        {
            if (!(AssetImporter.GetAtPath(assetPath) is TextureImporter importer))
                return;
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.crunchedCompression = true;
            importer.compressionQuality = 80;
            importer.SaveAndReimport();
        }

        // ---------------------------------------------------------------------------------------------------------------- one panel

        private static void RenderPanel(StoryPanel panel, string path)
        {
            var scene = OpenStage(panel.Stage);
            var stage = new Stage(scene);
            try
            {
                stage.ForceTopLevelOfDetail();
                foreach (var pose in panel.Actors.Where(a => a.Part == null))
                    stage.PoseActor(pose);
                foreach (var pose in panel.Actors.Where(a => a.Part != null))
                    stage.PosePart(pose);
                stage.SetLights(panel.Lights);
                stage.PublishLights();

                var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                // The paper shows between inset frames
                var paper = new Color32(236, 226, 205, 255);
                image.SetPixels32(Enumerable.Repeat(paper, Width * Height).ToArray());
                var multiple = panel.Frames.Count > 1;
                foreach (var frame in panel.Frames)
                {
                    var rect = FrameRect(frame, multiple);
                    var view = panel.CameraOf(frame);
                    var pixels = RenderFrame(stage, view, rect.width, rect.height);
                    // Devices, balloons, captions, SFX and the border (31.6), over the finished render
                    StoryLettering.Letter(pixels, rect.width, rect.height, frame, panel.Number * 10 + panel.Frames.IndexOf(frame),
                        name => stage.ProjectActor(name, view, rect.width, rect.height));
                    // The image has y up, the frame's rect from the top
                    image.SetPixels(rect.x, Height - rect.y - rect.height, rect.width, rect.height, pixels);
                }
                File.WriteAllBytes(path, image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally
            {
                stage.Dispose();
            }
        }

        private static RectInt FrameRect(StoryFrame frame, bool multiple)
        {
            // Inset frames leave a gutter of the paper round and between them
            var gutter = multiple ? 16 : 0;
            var x = Mathf.RoundToInt(frame.X * Width);
            var y = Mathf.RoundToInt(frame.Y * Height);
            var right = Mathf.RoundToInt((frame.X + frame.W) * Width);
            var bottom = Mathf.RoundToInt((frame.Y + frame.H) * Height);
            var left = x + (frame.X > 0.001f ? gutter / 2 : gutter);
            var top = y + (frame.Y > 0.001f ? gutter / 2 : gutter);
            right -= frame.X + frame.W < 0.999f ? gutter / 2 : gutter;
            bottom -= frame.Y + frame.H < 0.999f ? gutter / 2 : gutter;
            return new RectInt(left, top, right - left, bottom - top);
        }

        private static Scene OpenStage(string stage)
        {
            switch (stage)
            {
                case "home":
                    if (!File.Exists(HomeBuilder.ScenePath))
                        HomeBuilder.Build();
                    return EditorSceneManager.OpenScene(HomeBuilder.ScenePath, OpenSceneMode.Single);
                case "meadow":
                    LevelBuilder.BuildStale();
                    return EditorSceneManager.OpenScene(MeadowScenePath, OpenSceneMode.Single);
                default:
                {
                    // The warp: an empty stage with a camera, a light and no room or level
                    var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    MushroomScene.BuildLighting(scene, null, new MushroomScene.IndoorLighting
                    {
                        lightDirection = new Vector3(0.3f, 0.7f, -1).normalized,
                        lightColor = new Color(1f, 0.95f, 0.85f),
                        ambient = new Color(0.35f, 0.3f, 0.4f),
                    });
                    var camera = MushroomScene.CreateCamera(scene, "Main Camera", 60);
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(0.1f, 0.06f, 0.2f);
                    return scene;
                }
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- rendering

        /// <summary>One frame at Super times its size: the colour and aux passes, the post pass, averaged down to its size.</summary>
        private static Color[] RenderFrame(Stage stage, StoryCamera view, int width, int height)
        {
            var big = new Vector2Int(width * Super, height * Super);
            var postShader = AssetDatabase.LoadAssetAtPath<Shader>(PostShaderPath);
            if (postShader == null)
                throw new InvalidOperationException($"{PostShaderPath} is missing");
            var post = new Material(postShader) { hideFlags = HideFlags.HideAndDontSave };
            var cameraObject = EditorUtility.CreateGameObjectWithHideFlags("Story Camera", HideFlags.HideAndDontSave, typeof(Camera));
            var colour = new RenderTexture(big.x, big.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var aux = new RenderTexture(big.x, big.y, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var processed = new RenderTexture(big.x, big.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var final = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { filterMode = FilterMode.Bilinear };
            colour.filterMode = FilterMode.Bilinear;
            aux.filterMode = FilterMode.Point;
            try
            {
                var camera = cameraObject.GetComponent<Camera>();
                Stage.CopyCamera(stage.SceneCamera, camera);
                camera.transform.SetPositionAndRotation(Stage.ToUnity(view.Pos), Stage.EulerOf(view.Rot));
                camera.fieldOfView = view.Fov;
                camera.aspect = width / (float)height;
                // The room has no ceiling or fourth wall: what is beyond is ink
                if (camera.clearFlags == CameraClearFlags.SolidColor)
                    camera.backgroundColor = new Color(0.1f, 0.065f, 0.09f);
                camera.allowMSAA = false;
                camera.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.None;

                // The colour pass
                Shader.SetGlobalVector("_SdfComic", new Vector4(1, stage.LightCount, 2, 0));
                camera.targetTexture = colour;
                camera.Render();

                // The aux pass: no post-processing, no HDR, nothing that does not write it, the background far away
                var clearFlags = camera.clearFlags;
                var background = camera.backgroundColor;
                var data = camera.GetUniversalAdditionalCameraData();
                camera.allowHDR = false;
                data.renderPostProcessing = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0, 0, 1000, 0);
                Shader.SetGlobalVector("_SdfComic", new Vector4(2, stage.LightCount, 2, 0));
                var hidden = stage.HideNonToon();
                camera.targetTexture = aux;
                camera.Render();
                foreach (var renderer in hidden)
                    renderer.forceRenderingOff = false;
                Shader.SetGlobalVector("_SdfComic", Vector4.zero);

                // The post pass, then down to the frame's size
                post.SetTexture("_Aux", aux);
                post.SetFloat("_Scale", big.y / 1080f);
                post.SetFloat("_Seed", 1.7f);
                Graphics.Blit(colour, processed, post);
                Graphics.Blit(processed, final);
                var active = RenderTexture.active;
                RenderTexture.active = final;
                var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                RenderTexture.active = active;
                var pixels = image.GetPixels();
                Object.DestroyImmediate(image);
                camera.targetTexture = null;
                camera.clearFlags = clearFlags;
                camera.backgroundColor = background;
                return pixels;
            }
            finally
            {
                Shader.SetGlobalVector("_SdfComic", Vector4.zero);
                foreach (var texture in new[] { colour, aux, processed, final })
                {
                    texture.Release();
                    Object.DestroyImmediate(texture);
                }
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(post);
            }
        }

        // ---------------------------------------------------------------------------------------------------------------- the stage

        /// <summary>An opened stage scene, posed for a panel. Its pieces are found by name, the way HomeBuilder names them.</summary>
        private sealed class Stage : IDisposable
        {
            private readonly Scene scene;
            private readonly Dictionary<Transform, (Vector3 position, Quaternion rotation, Vector3 scale)> rest =
                new Dictionary<Transform, (Vector3, Quaternion, Vector3)>();
            private readonly List<Object> made = new List<Object>();
            private readonly Dictionary<string, Transform> madeActors = new Dictionary<string, Transform>();
            private readonly List<Light> storyLights = new List<Light>();
            private Transform actors;

            public Camera SceneCamera { get; }
            public int LightCount => Mathf.Min(storyLights.Count, 4);

            public Stage(Scene scene)
            {
                this.scene = scene;
                SceneCamera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).First();
                var home = scene.GetRootGameObjects().FirstOrDefault(g => g.name == HomeBuilder.RootName);
                actors = home != null ? home.transform.Find(HomeBuilder.ActorsName) : null;
                if (home != null)
                    storyLights.AddRange(home.transform.Find(HomeBuilder.LightsName).GetComponentsInChildren<Light>());
            }

            public void Dispose()
            {
                foreach (var item in made)
                    if (item != null)
                        Object.DestroyImmediate(item);
            }

            // Frames of reference: the pipeline's, x mirrored for Unity

            public static Vector3 ToUnity(StoryVec3 p) => new Vector3(-p.X, p.Y, p.Z);

            /// <summary>(pitch, yaw, roll) as the story file defines it, as Unity's rotation: yaw turns the other way as x is mirrored.</summary>
            public static Quaternion EulerOf(StoryVec3 rot) => Quaternion.Euler(rot.X, -rot.Y, rot.Z);

            public static void CopyCamera(Camera sceneCamera, Camera camera)
            {
                camera.CopyFrom(sceneCamera);
                var sceneData = sceneCamera.GetUniversalAdditionalCameraData();
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = sceneData.renderPostProcessing;
                data.antialiasingQuality = sceneData.antialiasingQuality;
                data.renderShadows = sceneData.renderShadows;
                camera.nearClipPlane = 0.05f;
            }

            /// <summary>A capture shows the finest level of every model, whatever the distance.</summary>
            public void ForceTopLevelOfDetail()
            {
                foreach (var group in Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    group.ForceLOD(0);
            }

            /// <summary>The renderers that are not the toon family's (its shaders write the aux pass), switched off for it.</summary>
            public List<Renderer> HideNonToon()
            {
                var hidden = new List<Renderer>();
                foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (renderer.forceRenderingOff)
                        continue;
                    var materials = renderer.sharedMaterials;
                    if (materials.Length > 0 && materials.All(m => m != null && m.shader != null && ToonShaders.Contains(m.shader.name)))
                        continue;
                    renderer.forceRenderingOff = true;
                    hidden.Add(renderer);
                }
                return hidden;
            }

            // Actors and parts

            private Transform FindActor(string name)
            {
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                        if (transform.name == name && transform.parent != null && (transform.parent.name == HomeBuilder.ActorsName || transform.parent.name == HomeBuilder.RoomName))
                            return transform;
                // Not in the stage: a scout comes with the panel that names it (meadow, warp), made once
                if (madeActors.TryGetValue(name, out var existing))
                    return existing;
                var prefabName = name.StartsWith("Chanterelle Hand", StringComparison.Ordinal)
                    ? (int.Parse(name.Substring(name.Length - 1)) % 2 == 1 ? "chanterelle-scout-hand0" : "chanterelle-scout-hand1")
                    : "amanita-scout";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{MushroomBuilder.PrefabFolder}/{prefabName}.prefab");
                if (prefab == null)
                    throw new InvalidOperationException($"actor '{name}' is not in the stage and there is no prefab {prefabName}");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = name;
                made.Add(instance);
                madeActors[name] = instance.transform;
                return instance.transform;
            }

            private void Remember(Transform transform)
            {
                if (!rest.ContainsKey(transform))
                    rest[transform] = (transform.localPosition, transform.localRotation, transform.localScale);
            }

            public void PoseActor(StoryActorPose pose)
            {
                var transform = FindActor(pose.Actor);
                Remember(transform);
                var parent = transform.parent;
                // Absolute on the stage: world coordinates, whatever the actor hangs under
                var position = pose.Pos.HasValue ? ToUnity(pose.Pos.Value) : transform.position;
                var rotation = pose.Rot.HasValue ? EulerOf(pose.Rot.Value) : transform.rotation;
                transform.SetPositionAndRotation(position, rotation);
                if (pose.Scale.HasValue)
                    transform.localScale = new Vector3(pose.Scale.Value.X, pose.Scale.Value.Y, pose.Scale.Value.Z);
            }

            public void PosePart(StoryActorPose pose)
            {
                var actor = FindActor(pose.Actor);
                var part = FindPart(actor, pose.Part);
                if (part == null)
                    throw new InvalidOperationException($"line {pose.Line}: '{pose.Actor}' has no part '{pose.Part}'");
                Remember(part);
                var (position, rotation, scale) = rest[part];
                // A change on the rest pose, in the actor's own space (x mirrored)
                if (pose.Pos.HasValue)
                    part.localPosition = position + ToUnity(pose.Pos.Value);
                if (pose.Rot.HasValue)
                    part.localRotation = EulerOf(pose.Rot.Value) * rotation;
                if (pose.Scale.HasValue)
                    part.localScale = Vector3.Scale(scale, new Vector3(pose.Scale.Value.X, pose.Scale.Value.Y, pose.Scale.Value.Z));
            }

            private static readonly Regex EyeOrHand = new Regex(@"^(eye|hand)(\.\d+)?$");

            /// <summary>
            /// A part of an actor by the story file's name. A scout's body is the child that is neither an eye nor a hand; eyes and hands are
            /// the uses eye.0 and eye.1, left being the actor's left (the lower x in Unity's space as the actor faces +z); a prop's parts
            /// (seat, screen, crisp_1) are the children named so.
            /// </summary>
            private static Transform FindPart(Transform actor, string part)
            {
                var children = actor.Cast<Transform>().Where(c => !c.name.Contains("_LOD") && c.name != "Outline").ToList();
                if (part == "body")
                    return children.FirstOrDefault(c => !EyeOrHand.IsMatch(c.name));
                var match = Regex.Match(part, @"^(eye|hand)-(left|right)$");
                if (match.Success)
                {
                    var pair = children.Where(c => EyeOrHand.IsMatch(c.name) && c.name.StartsWith(match.Groups[1].Value, StringComparison.Ordinal))
                        .OrderBy(c => c.localPosition.x).ToList();
                    if (pair.Count == 0)
                        return null;
                    return match.Groups[2].Value == "left" ? pair.First() : pair.Last();
                }
                return children.FirstOrDefault(c => c.name == part);
            }

            /// <summary>An actor's tail spot in a frame's pixels (y down), or null when it is behind the camera.</summary>
            public Pt? ProjectActor(string name, StoryCamera view, int width, int height)
            {
                var world = SpeakerPoint(name);
                var cameraToWorld = Matrix4x4.TRS(ToUnity(view.Pos), EulerOf(view.Rot), Vector3.one);
                var worldToCamera = Matrix4x4.Scale(new Vector3(1, 1, -1)) * cameraToWorld.inverse;
                var clip = Matrix4x4.Perspective(view.Fov, width / (float)height, 0.05f, 2000f) * worldToCamera * new Vector4(world.x, world.y, world.z, 1);
                if (clip.w <= 0.01f)
                    return null;
                return new Pt((clip.x / clip.w * 0.5f + 0.5f) * width, (1 - (clip.y / clip.w * 0.5f + 0.5f)) * height);
            }

            /// <summary>Where a balloon's tail points for an actor: a spot on the upper body (the cap), in world space.</summary>
            public Vector3 SpeakerPoint(string name)
            {
                var actor = FindActor(name);
                var body = FindPart(actor, "body") ?? actor;
                var renderers = body.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToList();
                if (renderers.Count == 0)
                    return actor.position;
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                    bounds.Encapsulate(renderer.bounds);
                return new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.72f, bounds.center.z);
            }

            // Lights

            public void SetLights(IEnumerable<StoryLight> lights)
            {
                foreach (var entry in lights)
                {
                    var light = storyLights.FirstOrDefault(l => l.name == entry.Name);
                    if (light == null)
                        continue;
                    if (entry.Intensity.HasValue)
                        light.intensity = entry.Intensity.Value;
                    if (entry.Color.HasValue)
                        light.color = new Color(entry.Color.Value.X, entry.Color.Value.Y, entry.Color.Value.Z);
                }
            }

            /// <summary>The story's lights to the shaders (SdfComic.hlsl), which shade with them in the comic look.</summary>
            public void PublishLights()
            {
                var positions = new Vector4[4];
                var colours = new Vector4[4];
                for (var i = 0; i < LightCount; i++)
                {
                    var light = storyLights[i];
                    positions[i] = new Vector4(light.transform.position.x, light.transform.position.y, light.transform.position.z, 1f / (light.range * light.range));
                    var linear = light.color.linear;
                    colours[i] = new Vector4(linear.r, linear.g, linear.b, light.intensity);
                }
                Shader.SetGlobalVectorArray("_SdfComicLightPos", positions);
                Shader.SetGlobalVectorArray("_SdfComicLightColor", colours);
            }

            // The monitor's screen

            public void SetScreen(StoryScreen screen)
            {
                var monitor = FindActor("monitor");
                var part = FindPart(monitor, "screen");
                if (part == null)
                    return;
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(ScreenShaderPath);
                if (shader == null)
                    throw new InvalidOperationException($"{ScreenShaderPath} is missing");
                var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                made.Add(material);
                var texture = ScreenTexture(screen.Texture);
                made.Add(texture);
                material.SetTexture("_MainTex", texture);
                material.SetFloat("_Glitch", screen.Glitch);
                material.SetFloat("_Bulge", screen.Bulge);
                material.SetFloat("_Seed", 3.3f);

                var filter = part.GetComponent<MeshFilter>();
                var bounds = filter.sharedMesh.bounds;
                // The slab is 12 triangles; a grid across its front face gives the bulge something to push
                var grid = FrontGrid(bounds, 96, 54);
                made.Add(grid);
                filter.sharedMesh = grid;
                var renderer = part.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                material.SetVector("_ScreenRect", new Vector4(bounds.center.x, bounds.center.y, bounds.extents.x, bounds.extents.y));
            }

            private static Mesh FrontGrid(Bounds bounds, int columns, int rows)
            {
                var vertices = new Vector3[(columns + 1) * (rows + 1)];
                for (var y = 0; y <= rows; y++)
                for (var x = 0; x <= columns; x++)
                    vertices[y * (columns + 1) + x] = new Vector3(
                        Mathf.Lerp(bounds.min.x, bounds.max.x, x / (float)columns),
                        Mathf.Lerp(bounds.min.y, bounds.max.y, y / (float)rows), bounds.max.z);
                var triangles = new List<int>();
                for (var y = 0; y < rows; y++)
                for (var x = 0; x < columns; x++)
                {
                    var i = y * (columns + 1) + x;
                    triangles.AddRange(new[] { i, i + columns + 1, i + 1, i + 1, i + columns + 1, i + columns + 2 });
                }
                var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, vertices = vertices };
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                // The slab's bounds, so that the bulge is not culled
                mesh.bounds = new Bounds(bounds.center, bounds.size + new Vector3(0, 0, 0.4f));
                return mesh;
            }

            /// <summary>The picture for a screen: Assets/Story/Screens/&lt;name&gt;.png when there is one (31.7 makes them), else a stand-in.</summary>
            private static Texture2D ScreenTexture(string name)
            {
                var path = $"{ScreenFolder}/{name}.png";
                if (File.Exists(path))
                {
                    var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                    loaded.LoadImage(File.ReadAllBytes(path));
                    return loaded;
                }
                return StandIn(name);
            }

            // A picture for each screen until the real ones exist: sky and meadow, gold, a dark glitch, a blue face
            private static Texture2D StandIn(string name)
            {
                const int w = 512, h = 288;
                var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color[w * h];
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var u = x / (float)w;
                    var v = y / (float)h;
                    Color c;
                    switch (name)
                    {
                        case "screen-victory":
                            c = Color.Lerp(new Color(0.95f, 0.7f, 0.15f), new Color(1f, 0.93f, 0.55f), v);
                            if (Mathf.Abs(u - 0.5f) < 0.3f && Mathf.Abs(v - 0.5f) < 0.1f)
                                c = new Color(0.55f, 0.1f, 0.1f);
                            break;
                        case "screen-glitch":
                            c = Color.Lerp(new Color(0.05f, 0.1f, 0.25f), new Color(0.2f, 0.45f, 0.9f), (Mathf.Sin(v * 60f) + 1) * 0.5f);
                            break;
                        case "screen-prime":
                            c = Color.Lerp(new Color(0.1f, 0.25f, 0.6f), new Color(0.3f, 0.6f, 1f), v);
                            if ((new Vector2(u, v) - new Vector2(0.5f, 0.45f)).magnitude < 0.28f)
                                c = new Color(0.95f, 0.65f, 0.2f);
                            break;
                        default:
                            c = v > 0.38f ? Color.Lerp(new Color(0.55f, 0.8f, 1f), new Color(0.85f, 0.93f, 1f), v) : Color.Lerp(new Color(0.25f, 0.6f, 0.2f), new Color(0.4f, 0.75f, 0.3f), v * 2);
                            if (Mathf.Abs(u - 0.5f) < 0.02f && Mathf.Abs(v - 0.55f) < 0.06f || Mathf.Abs(v - 0.55f) < 0.01f && Mathf.Abs(u - 0.5f) < 0.06f)
                                c = Color.white;
                            break;
                    }
                    // A marked corner, so that a mirrored or upside-down picture shows
                    if (u < 0.12f && v > 0.88f)
                        c = new Color(0.9f, 0.1f, 0.1f);
                    pixels[y * w + x] = c;
                }
                texture.SetPixels(pixels);
                texture.Apply();
                return texture;
            }
        }
    }
}
