using System;
using System.IO;
using System.Linq;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SdfRig.Editor
{
    /// <summary>
    /// Renders the soldier's two team prefabs for a look (ticket 43): <c>soldier-sides.png</c> (red above blue, each from the front, the
    /// right, the back and the left) and <c>soldier-size.png</c> (red, blue and the amanita and chanterelle scouts side by side from the
    /// front, one camera, so the heights compare). Batch, with the GPU:
    /// <c>tools/unity-batch.sh soldier-capture -executeMethod SdfRig.Editor.SoldierCapture.CaptureInBatch -captureDir &lt;folder&gt;</c>.
    /// </summary>
    public static class SoldierCapture
    {
        [MenuItem("Sdf/Rig/Capture Soldier")]
        public static void CaptureFromMenu() => Capture(Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "SoldierCapture"));

        public static void CaptureInBatch()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-captureDir");
            Capture(i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Path.GetTempPath(), "soldier-capture"));
        }

        public static void Capture(string directory)
        {
            Directory.CreateDirectory(directory);
            var red = AssetDatabase.LoadAssetAtPath<GameObject>(SoldierSkins.SkinPrefab(SoldierModels.RedSkin));
            var blue = AssetDatabase.LoadAssetAtPath<GameObject>(SoldierSkins.SkinPrefab(SoldierModels.BlueSkin));
            if (red == null || blue == null)
            {
                Debug.LogError("SoldierCapture: the skin prefabs are missing; run SoldierSkins first");
                return;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            MushroomScene.BuildLighting(scene);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.localScale = new Vector3(8, 0.1f, 4);
            floor.transform.position = new Vector3(0, -0.05f, 0);
            floor.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.55f, 0.6f, 0.5f) };

            var cameraObject = EditorUtility.CreateGameObjectWithHideFlags("Soldier Camera", HideFlags.HideAndDontSave, typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.72f, 0.82f, 0.92f);
            camera.fieldOfView = 30;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 50;
            var asyncCompilation = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                // Sides: the soldier faces +Z; cameras at the front (+Z), the right of the soldier (-X in Unity), the back and the left
                var views = new[] { new Vector3(0, 0, 1), new Vector3(-1, 0, 0), new Vector3(0, 0, -1), new Vector3(1, 0, 0) };
                var tiles = new Texture2D[8];
                var slot = 0;
                foreach (var prefab in new[] { red, blue })
                {
                    var soldier = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    foreach (var view in views)
                        tiles[slot++] = Shoot(camera, new Vector3(0, 0.92f, 0), view, 4.4f, 512, 768);
                    UnityEngine.Object.DestroyImmediate(soldier);
                }
                Save(Atlas(tiles, 4, 2, 512, 768), Path.Combine(directory, "soldier-sides.png"));

                // Size: red and blue next to the two scouts, one front view
                var line = new[]
                {
                    (GameObject)PrefabUtility.InstantiatePrefab(red), (GameObject)PrefabUtility.InstantiatePrefab(blue),
                    InstantiateScout("amanita-scout"), InstantiateScout("chanterelle-scout"),
                };
                for (var i = 0; i < line.Length; i++)
                    if (line[i] != null)
                        line[i].transform.position = new Vector3(-2.7f + i * 1.8f, 0, 0);
                var size = Shoot(camera, new Vector3(0, 0.9f, 0), new Vector3(0, 0.05f, 1), 11f, 1600, 600);
                Save(size, Path.Combine(directory, "soldier-size.png"));
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
            Debug.Log($"SoldierCapture: written to {directory}");
        }

        static GameObject InstantiateScout(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Mushrooms/{name}.prefab");
            return prefab == null ? null : (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        }

        static Texture2D Shoot(Camera camera, Vector3 target, Vector3 direction, float distance, int width, int height)
        {
            camera.transform.position = target + direction.normalized * distance;
            camera.transform.LookAt(target);
            // One sample: the pipeline's MSAA count of the quality level does not always match a render target made here
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
            UnityEngine.Object.DestroyImmediate(rt);
            return image;
        }

        static Texture2D Atlas(Texture2D[] tiles, int columns, int rows, int width, int height)
        {
            var atlas = new Texture2D(columns * width, rows * height, TextureFormat.RGB24, false);
            for (var i = 0; i < tiles.Length; i++)
            {
                // The top row is the first tiles: texture rows count from the bottom
                atlas.SetPixels(i % columns * width, (rows - 1 - i / columns) * height, width, height, tiles[i].GetPixels());
                UnityEngine.Object.DestroyImmediate(tiles[i]);
            }
            atlas.Apply();
            return atlas;
        }

        static void Save(Texture2D image, string path)
        {
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
