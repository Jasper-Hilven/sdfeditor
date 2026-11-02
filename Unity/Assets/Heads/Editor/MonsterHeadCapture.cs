using System;
using System.IO;
using System.Linq;
using SdfMushrooms.Editor;
using SdfRig;
using SdfRig.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SdfHeads.Editor
{
    /// <summary>
    /// The capture sheet of the monster heads on the soldier (ticket 45), one file per modelled head: <c>heads-&lt;id&gt;.png</c>, red body above
    /// blue, four tiles each: the full body in front and in three-quarter view near (4.6 m, a 30 degree lens, as the soldier's own sheet),
    /// then the same two views at 50 m through the game's lens (60 degrees, 1080 lines, the middle of the frame cut out and scaled up
    /// three times without smoothing, so the pixels a player sees can be counted); and <c>heads-closeup.png</c>, every modelled head close up
    /// from the front, the side and the three-quarter, on the red body. Batch, with the GPU:
    /// <c>tools/unity-batch.sh heads-capture -executeMethod SdfHeads.Editor.MonsterHeadCapture.CaptureInBatch -captureDir &lt;folder&gt;</c>.
    /// </summary>
    public static class MonsterHeadCapture
    {
        /// <summary>The game's vertical field of view and the frame's height in pixels, for the 50 m tiles.</summary>
        const float GameFov = 60;
        const int GameLines = 1080;

        [MenuItem("Sdf/Heads/Capture Heads")]
        public static void CaptureFromMenu() => Capture(Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "HeadCapture"));

        public static void CaptureInBatch()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-captureDir");
            Capture(i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Path.GetTempPath(), "head-capture"));
        }

        public static void Capture(string directory)
        {
            Directory.CreateDirectory(directory);
            var skins = new[] { SoldierModels.RedSkin, SoldierModels.BlueSkin }
                .Select(skin => AssetDatabase.LoadAssetAtPath<GameObject>(SoldierSkins.SkinPrefab(skin))).ToArray();
            var models = AssetDatabase.LoadAssetAtPath<MonsterHeadModels>(MonsterHeadBuilder.AssetPath);
            if (skins.Any(s => s == null) || models == null || models.entries.Length == 0)
            {
                Debug.LogError("MonsterHeadCapture: the soldier skins or the head models are missing; run SoldierSkins and MonsterHeadBuilder first");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            MushroomScene.BuildLighting(scene);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.localScale = new Vector3(300, 0.1f, 300);
            floor.transform.position = new Vector3(0, -0.05f, 0);
            floor.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.45f, 0.55f, 0.4f) };

            var cameraObject = EditorUtility.CreateGameObjectWithHideFlags("Head Camera", HideFlags.HideAndDontSave, typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.72f, 0.82f, 0.92f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200;
            var asyncCompilation = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                var front = new Vector3(0, 0.05f, 1).normalized;
                var quarter = new Vector3(-0.7f, 0.12f, 0.7f).normalized;   // from the soldier's right front (-X is its right)
                var closeViews = new[] { front, new Vector3(-1, 0.05f, 0.02f).normalized, quarter };
                var closeTiles = new System.Collections.Generic.List<Texture2D>();
                foreach (var entry in models.entries)
                {
                    var tiles = new Texture2D[8];
                    for (var s = 0; s < skins.Length; s++)
                    {
                        var soldier = (GameObject)PrefabUtility.InstantiatePrefab(skins[s]);
                        var worn = HeadWear.Attach(soldier.transform, entry.id, entry.prefab, MonsterHeadModels.ShapeOf(entry));
                        if (worn == null)
                            throw new InvalidOperationException($"head {entry.id} did not attach");
                        camera.fieldOfView = 30;
                        var body = new Vector3(0, 0.95f, 0);
                        tiles[s * 4 + 0] = Shoot(camera, body, front, 4.6f, 512, 768);
                        tiles[s * 4 + 1] = Shoot(camera, body, quarter, 4.6f, 512, 768);
                        // At 50 m: the head's height, through the game's lens, the middle of the 1080-line frame, scaled up
                        camera.fieldOfView = GameFov;
                        var far = new Vector3(0, 1.0f, 0);
                        tiles[s * 4 + 2] = FarTile(camera, far, front, 50);
                        tiles[s * 4 + 3] = FarTile(camera, far, quarter, 50);
                        if (s == 0)
                        {
                            camera.fieldOfView = 24;
                            var headAt = new Vector3(0, SoldierBody.HeadTopHeight + entry.boundsCentre.y, 0);
                            foreach (var view in closeViews)
                                closeTiles.Add(Shoot(camera, headAt, view, 2.4f, 480, 480));
                        }
                        Object.DestroyImmediate(soldier);
                    }
                    Save(Atlas(tiles, 4, 2, 512, 768), Path.Combine(directory, $"heads-{entry.id}.png"));
                }
                Save(Atlas(closeTiles.ToArray(), 3, models.entries.Length, 480, 480), Path.Combine(directory, "heads-closeup.png"));
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                Object.DestroyImmediate(cameraObject);
            }
            Debug.Log($"MonsterHeadCapture: written to {directory}");
        }

        /// <summary>
        /// What the player sees of a soldier <paramref name="metres"/> away: the game's frame, 1920 by 1080, with the middle 160 by 240
        /// pixels cut out and scaled up three times without smoothing (480 by 720), then padded to the sheet's 512 by 768.
        /// </summary>
        static Texture2D FarTile(Camera camera, Vector3 target, Vector3 direction, float metres)
        {
            var frame = Shoot(camera, target, direction, metres, GameLines * 16 / 9, GameLines);
            var cut = frame.GetPixels(frame.width / 2 - 80, frame.height / 2 - 120, 160, 240);
            Object.DestroyImmediate(frame);
            var tile = new Texture2D(512, 768, TextureFormat.RGB24, false);
            tile.SetPixels(Enumerable.Repeat(new Color(0.2f, 0.2f, 0.22f), 512 * 768).ToArray());
            for (var y = 0; y < 240; y++)
                for (var x = 0; x < 160; x++)
                    for (var dy = 0; dy < 3; dy++)
                        for (var dx = 0; dx < 3; dx++)
                            tile.SetPixel(16 + x * 3 + dx, 24 + y * 3 + dy, cut[y * 160 + x]);
            tile.Apply();
            return tile;
        }

        static Texture2D Shoot(Camera camera, Vector3 target, Vector3 direction, float distance, int width, int height)
        {
            camera.transform.position = target + direction.normalized * distance;
            camera.transform.LookAt(target);
            const int samples = 1;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = samples };
            camera.targetTexture = rt;
            camera.Render();
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = active;
            camera.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            return image;
        }

        static Texture2D Atlas(Texture2D[] tiles, int columns, int rows, int width, int height)
        {
            var atlas = new Texture2D(columns * width, rows * height, TextureFormat.RGB24, false);
            for (var i = 0; i < tiles.Length; i++)
            {
                atlas.SetPixels(i % columns * width, (rows - 1 - i / columns) * height, width, height, tiles[i].GetPixels());
                Object.DestroyImmediate(tiles[i]);
            }
            atlas.Apply();
            return atlas;
        }

        static void Save(Texture2D image, string path)
        {
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }
    }
}
