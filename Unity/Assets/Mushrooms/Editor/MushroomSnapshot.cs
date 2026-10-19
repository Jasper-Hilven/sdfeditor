using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
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
    /// <para>
    /// Levels of detail, on request only (<see cref="LodRequestPath"/>, or Tools > SDF Mushrooms > Save LOD Snapshots): for each model
    /// with a LOD group, &lt;model&gt;_LOD&lt;k&gt;.png shows each level forced on in a close-up; &lt;model&gt;_distances.png shows,
    /// left to right, the model at a distance in the middle of each level's range and just past the cull, as many pixels as it really
    /// covers in a 1080p frame, enlarged without smoothing; &lt;model&gt;_crossfade&lt;k&gt;.png shows the close-up well into the
    /// cross-fade from level k to k+1 without anti-aliasing, so the dithered LOD_FADE_CROSSFADE variants are drawn (and compiled,
    /// synchronously) and their pattern shows. These use a hidden copy of the model whose LOD group is changed at will; the scene's own
    /// instances are only kept from drawing meanwhile. Without a window: as above, with -executeMethod
    /// SdfMushrooms.Editor.MushroomSnapshot.CaptureLevelsOfDetail.
    /// </para>
    /// The renders are drawn at the current quality level, with its anti-aliasing (MSAA samples or FXAA, see
    /// <see cref="QualityPresetCamera"/>).
    /// </summary>
    public static class MushroomSnapshot
    {
        public const string RequestPath = "Temp/sdf-mushroom-snapshot.txt";

        /// <summary>Like <see cref="RequestPath"/>, for the level-of-detail snapshots.</summary>
        public const string LodRequestPath = "Temp/sdf-mushroom-lod-snapshot.txt";

        public static void Capture()
        {
            MushroomBuilder.BuildStale();
            Shoot(EditorSceneManager.OpenScene(MushroomBuilder.ScenePath, OpenSceneMode.Single), DirectoryArgument());
        }

        public static void CaptureLevelsOfDetail()
        {
            MushroomBuilder.BuildStale();
            ShootLevelsOfDetail(EditorSceneManager.OpenScene(MushroomBuilder.ScenePath, OpenSceneMode.Single), DirectoryArgument());
        }

        private static string DirectoryArgument()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-snapshotDir");
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Path.GetTempPath(), "mushroom-snapshots");
        }

        public static void CaptureOpenScene(string directory, bool levelsOfDetail = false)
        {
            var scene = SceneManager.GetSceneByPath(MushroomBuilder.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning($"SDF mushrooms: open {MushroomBuilder.ScenePath} to take snapshots");
                return;
            }
            if (levelsOfDetail)
                ShootLevelsOfDetail(scene, directory);
            else
                Shoot(scene, directory);
        }

        [MenuItem("Tools/SDF Mushrooms/Save Snapshots")]
        private static void SaveSnapshotsFromMenu() =>
            CaptureOpenScene(Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "Temp", "Snapshots"));

        [MenuItem("Tools/SDF Mushrooms/Save LOD Snapshots")]
        private static void SaveLodSnapshotsFromMenu() =>
            CaptureOpenScene(Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "Temp", "Snapshots"), levelsOfDetail: true);

        private static void Shoot(Scene scene, string directory)
        {
            Directory.CreateDirectory(directory);
            var sceneCamera = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<Camera>()).First(c => c != null);
            var cameraObject = EditorUtility.CreateGameObjectWithHideFlags("Snapshot Camera", HideFlags.HideAndDontSave, typeof(Camera));
            var asyncCompilation = ShaderUtil.allowAsyncCompilation;
            try
            {
                // Variants compiled in the background are drawn as placeholders until they are ready; the renders must show the real ones
                ShaderUtil.allowAsyncCompilation = false;
                var camera = cameraObject.GetComponent<Camera>();
                CopyCamera(sceneCamera, camera);
                camera.transform.SetPositionAndRotation(sceneCamera.transform.position, sceneCamera.transform.rotation);
                Render(camera, Path.Combine(directory, "scene.png"), 1920, 1080);

                var root = scene.GetRootGameObjects().First(g => g.name == MushroomScene.MushroomsRootName);
                foreach (var mushroom in MushroomScene.Instances(root))
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
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
            Debug.Log($"SDF mushrooms: snapshots written to {directory}");
        }

        private static void ShootLevelsOfDetail(Scene scene, string directory)
        {
            Directory.CreateDirectory(directory);
            var sceneCamera = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<Camera>()).First(c => c != null);
            var root = scene.GetRootGameObjects().First(g => g.name == MushroomScene.MushroomsRootName);
            var shown = root.GetComponentsInChildren<Renderer>().Where(r => !r.forceRenderingOff).ToArray();
            var cameraObject = EditorUtility.CreateGameObjectWithHideFlags("Snapshot Camera", HideFlags.HideAndDontSave, typeof(Camera));
            var asyncCompilation = ShaderUtil.allowAsyncCompilation;
            GameObject copy = null;
            try
            {
                // Variants compiled in the background are drawn as placeholders until they are ready; these renders must show the real ones
                ShaderUtil.allowAsyncCompilation = false;
                var camera = cameraObject.GetComponent<Camera>();
                CopyCamera(sceneCamera, camera);
                var data = camera.GetUniversalAdditionalCameraData();
                var halfAngle = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                var direction = new Vector3(-0.25f, 0.35f, 1).normalized;

                foreach (var model in MushroomScene.Instances(root))
                {
                    if (!model.TryGetComponent<LODGroup>(out var original))
                        continue;
                    foreach (var renderer in shown)
                        renderer.forceRenderingOff = true;
                    copy = UnityEngine.Object.Instantiate(model.gameObject, model.position, model.rotation);
                    foreach (var transform in copy.GetComponentsInChildren<Transform>(true))
                        transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
                    foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
                        renderer.forceRenderingOff = false;
                    var group = copy.GetComponent<LODGroup>();
                    var lods = group.GetLODs();
                    var center = copy.transform.TransformPoint(group.localReferencePoint);
                    var bounds = lods[0].renderers.Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
                    // Unity's relative height of a LOD group: its size over the height the view spans at its distance, times the LOD bias
                    var size = group.size * copy.transform.lossyScale.y;
                    float DistanceFor(float height) => size * 0.5f * QualitySettings.lodBias / (height * halfAngle);

                    // Each level forced on, in a close-up of the whole model
                    var close = Mathf.Max(bounds.extents.y, bounds.extents.x) * 1.3f / halfAngle;
                    camera.transform.position = bounds.center + direction * close;
                    camera.transform.LookAt(bounds.center);
                    for (var level = 0; level < lods.Length; level++)
                    {
                        group.ForceLOD(level);
                        Render(camera, Path.Combine(directory, $"{model.name}_LOD{level}.png"), 1200, 1200);
                    }
                    group.ForceLOD(-1);

                    // At real distances, the group choosing for itself: halfway (in log) through each level's range, then past the cull
                    float Upper(int level) => level == 0 ? 1 : lods[level - 1].screenRelativeTransitionHeight;
                    var heights = lods.Select((lod, level) => Mathf.Sqrt(lod.screenRelativeTransitionHeight * Upper(level))).ToList();
                    heights.Add(lods[^1].screenRelativeTransitionHeight * 0.8f);
                    var farClip = camera.farClipPlane;
                    var tiles = new List<Texture2D>();
                    foreach (var height in heights)
                    {
                        var distance = DistanceFor(height);
                        camera.farClipPlane = Mathf.Max(farClip, distance * 2);
                        camera.transform.position = center + direction * distance;
                        camera.transform.LookAt(center);
                        tiles.Add(RenderCrop(camera, 1920, 1080, Mathf.Clamp(Mathf.CeilToInt(height * 1080 * 1.6f), 24, 1080), 400));
                    }
                    camera.farClipPlane = farClip;
                    SaveStrip(tiles, Path.Combine(directory, $"{model.name}_distances.png"));

                    // Well into each cross-fade, seen close up: without the animation the fade follows the camera across the whole of the
                    // level's range, and the group's size is scaled so the close-up sits in its middle (in log, a little over a third of the
                    // way along both in relative height and in distance, whichever of them the fade follows). Without anti-aliasing
                    // (neither FXAA nor MSAA samples) the dither pattern itself shows, so each level's share of the pixels can be checked
                    var antialiasing = data.antialiasing;
                    data.antialiasing = AntialiasingMode.None;
                    group.animateCrossFading = false;
                    camera.transform.position = bounds.center + direction * close;
                    camera.transform.LookAt(bounds.center);
                    var closeDistance = Vector3.Distance(camera.transform.position, center);
                    for (var level = 0; level < lods.Length - 1; level++)
                    {
                        lods[level].fadeTransitionWidth = 1;
                        group.SetLODs(lods);
                        var target = Mathf.Sqrt(lods[level].screenRelativeTransitionHeight * Upper(level));
                        group.size *= target / (size * 0.5f * QualitySettings.lodBias / (closeDistance * halfAngle));
                        size = group.size * copy.transform.lossyScale.y;
                        Render(camera, Path.Combine(directory, $"{model.name}_crossfade{level}.png"), 1200, 1200, multisampled: false);
                    }
                    data.antialiasing = antialiasing;

                    UnityEngine.Object.DestroyImmediate(copy);
                    copy = null;
                }
            }
            finally
            {
                if (copy != null)
                    UnityEngine.Object.DestroyImmediate(copy);
                foreach (var renderer in shown)
                    renderer.forceRenderingOff = false;
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
            Debug.Log($"SDF mushrooms: LOD snapshots written to {directory}");
        }

        /// <summary>
        /// Renders one view to a PNG through a hidden copy of a scene's camera (its settings, post-processing and the quality level's
        /// anti-aliasing) at another place, with every shader variant compiled first; for other scenes' snapshots, like the levels'.
        /// </summary>
        public static void RenderView(Camera sceneCamera, Vector3 position, Quaternion rotation, string path, int width, int height)
        {
            var cameraObject = EditorUtility.CreateGameObjectWithHideFlags("Snapshot Camera", HideFlags.HideAndDontSave, typeof(Camera));
            var asyncCompilation = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                var camera = cameraObject.GetComponent<Camera>();
                CopyCamera(sceneCamera, camera);
                camera.transform.SetPositionAndRotation(position, rotation);
                Render(camera, path, width, height);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        /// <summary>Renders a frame and returns the square of <paramref name="crop"/> pixels at its centre, enlarged to <paramref name="tile"/> pixels without smoothing.</summary>
        private static Texture2D RenderCrop(Camera camera, int width, int height, int crop, int tile)
        {
            var target = Target(width, height, true);
            camera.targetTexture = target;
            camera.Render();
            var active = RenderTexture.active;
            RenderTexture.active = target;
            var small = new Texture2D(crop, crop, TextureFormat.RGB24, false);
            small.ReadPixels(new Rect((width - crop) / 2, (height - crop) / 2, crop, crop), 0, 0);
            small.Apply();
            RenderTexture.active = active;
            camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);

            var source = small.GetPixels32();
            var pixels = new Color32[tile * tile];
            for (var y = 0; y < tile; y++)
            for (var x = 0; x < tile; x++)
                pixels[y * tile + x] = source[y * crop / tile * crop + x * crop / tile];
            UnityEngine.Object.DestroyImmediate(small);
            var enlarged = new Texture2D(tile, tile, TextureFormat.RGB24, false);
            enlarged.SetPixels32(pixels);
            return enlarged;
        }

        /// <summary>Saves square tiles side by side, a few dark pixels apart, and destroys them.</summary>
        private static void SaveStrip(IReadOnlyList<Texture2D> tiles, string path)
        {
            const int gap = 6;
            var tile = tiles[0].width;
            var strip = new Texture2D(tiles.Count * (tile + gap) - gap, tile, TextureFormat.RGB24, false);
            strip.SetPixels32(Enumerable.Repeat(new Color32(20, 20, 20, 255), strip.width * strip.height).ToArray());
            for (var i = 0; i < tiles.Count; i++)
            {
                strip.SetPixels32(i * (tile + gap), 0, tile, tile, tiles[i].GetPixels32());
                UnityEngine.Object.DestroyImmediate(tiles[i]);
            }
            File.WriteAllBytes(path, strip.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(strip);
        }

        /// <summary>
        /// The snapshot camera as the scene's: its settings, post-processing (tone mapping, bloom) and the quality level's
        /// anti-aliasing.
        /// </summary>
        private static void CopyCamera(Camera sceneCamera, Camera camera)
        {
            camera.CopyFrom(sceneCamera);
            var sceneData = sceneCamera.GetUniversalAdditionalCameraData();
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = sceneData.renderPostProcessing;
            data.antialiasingQuality = sceneData.antialiasingQuality;
            data.renderShadows = sceneData.renderShadows;
            QualityPresetCamera.Apply(camera);
        }

        /// <summary>A render target with as many samples per pixel as the quality level's MSAA, or one.</summary>
        private static RenderTexture Target(int width, int height, bool multisampled)
        {
            var samples = multisampled && UniversalRenderPipeline.asset != null ? Mathf.Max(1, UniversalRenderPipeline.asset.msaaSampleCount) : 1;
            return new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = samples };
        }

        private static void Render(Camera camera, string path, int width, int height, bool multisampled = true)
        {
            var target = Target(width, height, multisampled);
            camera.targetTexture = target;
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
