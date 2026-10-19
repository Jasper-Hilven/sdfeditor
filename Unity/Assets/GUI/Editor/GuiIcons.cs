using System.Collections.Generic;
using System.IO;
using System.Linq;
using SdfMushrooms;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SdfGui.Editor
{
    /// <summary>
    /// Renders the GUI's icons from the model prefabs, in the toon look with its ink outline, so they always match the game: the
    /// characters (the players' avatars) from the front and a little above, the weapons from the side. Each is drawn twice, over black
    /// and over white, and the two give its transparency, so no render pipeline setting has to keep alpha. Writes
    /// Assets/GUI/Icons/&lt;model&gt;.png as sprites and the <see cref="GuiIconSet"/> that names them.
    /// </summary>
    public static class GuiIcons
    {
        public const string Folder = GuiAssets.Folder + "/Icons";
        public const string SetPath = Folder + "/Icons.asset";
        private const int Size = 384;

        public static IEnumerable<string> Models => new[] { "amanita-scout", "chanterelle-scout" }.Concat(FakeMatch.Weapons);

        [MenuItem("Tools/SDF GUI/Render Icons")]
        public static void RenderFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            var setup = EditorSceneManager.GetSceneManagerSetup();
            Render();
            if (setup.Length > 0)
                EditorSceneManager.RestoreSceneManagerSetup(setup);
        }

        /// <summary>Renders every icon in a scene of its own (the current scenes are replaced) and updates the icon set.</summary>
        public static GuiIconSet Render()
        {
            GuiAssets.EnsureFolder(Folder);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            MushroomScene.BuildLighting(scene);
            var asyncCompilation = ShaderUtil.allowAsyncCompilation;
            var cameraObject = new GameObject("Icon Camera");
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                var camera = cameraObject.AddComponent<Camera>();
                camera.fieldOfView = 18;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 100;
                camera.clearFlags = CameraClearFlags.SolidColor;
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                foreach (var model in Models)
                    RenderModel(camera, model);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                Object.DestroyImmediate(cameraObject);
            }
            AssetDatabase.Refresh();
            return UpdateSet();
        }

        private static void RenderModel(Camera camera, string model)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{MushroomBuilder.PrefabFolder}/{model}.prefab");
            if (prefab == null)
            {
                Debug.LogWarning($"SDF GUI: no prefab for the icon {model}");
                return;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                // Only the most detailed level of each LOD group
                foreach (var group in instance.GetComponentsInChildren<LODGroup>())
                    group.ForceLOD(0);
                var bounds = Bounds(instance);
                var character = !FakeMatch.Weapons.Contains(model);
                // Characters face +z; a weapon is shown across its longest horizontal side
                var direction = character
                    ? new Vector3(0.25f, 0.2f, 1).normalized
                    : bounds.extents.x >= bounds.extents.z ? new Vector3(0.12f, 0.15f, 1).normalized : new Vector3(1, 0.15f, -0.12f).normalized;
                var radius = bounds.extents.magnitude;
                var distance = radius / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.98f;
                camera.transform.position = bounds.center + direction * distance;
                camera.transform.LookAt(bounds.center);

                var black = Shoot(camera, Color.black);
                var white = Shoot(camera, Color.white);
                var pixels = new Color[black.Length];
                for (var i = 0; i < pixels.Length; i++)
                {
                    var b = black[i];
                    var w = white[i];
                    var alpha = Mathf.Clamp01(1 - ((w.r - b.r) + (w.g - b.g) + (w.b - b.b)) / 3);
                    pixels[i] = alpha > 0.002f ? new Color(b.r / alpha, b.g / alpha, b.b / alpha, alpha) : Color.clear;
                }
                var image = Crop(pixels);
                var path = $"{Folder}/{model}.png";
                File.WriteAllBytes(path, image.EncodeToPNG());
                Object.DestroyImmediate(image);
                AssetDatabase.ImportAsset(path);
                if (AssetImporter.GetAtPath(path) is TextureImporter importer)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.sRGBTexture = true;
                    importer.SaveAndReimport();
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>The icon trimmed to what it shows, a few pixels around it, so it fills the rect it is drawn in.</summary>
        private static Texture2D Crop(Color[] pixels)
        {
            int minX = Size, minY = Size, maxX = -1, maxY = -1;
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                if (pixels[y * Size + x].a < 0.02f)
                    continue;
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
            const int margin = 4;
            if (maxX < 0)
                minX = minY = 0;
            minX = Mathf.Max(0, minX - margin);
            minY = Mathf.Max(0, minY - margin);
            maxX = maxX < 0 ? Size - 1 : Mathf.Min(Size - 1, maxX + margin);
            maxY = maxY < 0 ? Size - 1 : Mathf.Min(Size - 1, maxY + margin);
            var width = maxX - minX + 1;
            var height = maxY - minY + 1;
            var cropped = new Color[width * height];
            for (var y = 0; y < height; y++)
                System.Array.Copy(pixels, (minY + y) * Size + minX, cropped, y * width, width);
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            image.SetPixels(cropped);
            image.Apply();
            return image;
        }

        private static Color[] Shoot(Camera camera, Color background)
        {
            camera.backgroundColor = background;
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            camera.targetTexture = target;
            camera.Render();
            var active = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            image.Apply();
            RenderTexture.active = active;
            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
            var pixels = image.GetPixels();
            Object.DestroyImmediate(image);
            return pixels;
        }

        private static Bounds Bounds(GameObject gameObject)
        {
            var renderers = gameObject.GetComponentsInChildren<Renderer>().Where(r => r.gameObject.name != MushroomBuilder.OutlineObjectName).ToArray();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1))
                bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        /// <summary>The icon set naming every icon in the folder, made when missing.</summary>
        public static GuiIconSet UpdateSet()
        {
            var set = AssetDatabase.LoadAssetAtPath<GuiIconSet>(SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<GuiIconSet>();
                AssetDatabase.CreateAsset(set, SetPath);
            }
            set.icons = Models
                .Select(m => new GuiIconSet.Icon { name = m, sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{Folder}/{m}.png") })
                .Where(i => i.sprite != null)
                .ToArray();
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return set;
        }
    }
}
