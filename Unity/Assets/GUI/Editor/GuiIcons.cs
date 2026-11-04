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
        /// <summary>The ink outline's width in render pixels (of <see cref="Size"/>), and the shading bands the colours are cut into.</summary>
        public const int OutlineWidth = 8;
        public const int Bands = 4;

        public static IEnumerable<string> Models => SdfHeads.MonsterHeads.All.Select(h => h.Id).Concat(MatchView.Weapons);

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
                var character = !MatchView.Weapons.Contains(model);
                // Characters face +z; a weapon is shown across its longest horizontal side
                var direction = character
                    ? new Vector3(0.25f, 0.2f, 1).normalized
                    : bounds.extents.x >= bounds.extents.z ? new Vector3(0.12f, 0.15f, 1).normalized : new Vector3(1, 0.15f, -0.12f).normalized;
                var radius = bounds.extents.magnitude;
                var distance = radius / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.12f; // room for the outline
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
                var image = Fit(Crop(Outline(Toon(pixels))), !character);
                var path = $"{Folder}/{model}.png";
                File.WriteAllBytes(path, image.EncodeToPNG());
                Object.DestroyImmediate(image);
                AssetDatabase.ImportAsset(path);
                Configure(path);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Cel shading: each opaque pixel's brightness is cut into <see cref="Bands"/> flat steps, its hue kept.</summary>
        public static Color[] Toon(Color[] pixels)
        {
            var result = new Color[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                var l = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                if (c.a < 0.02f || l < 0.001f)
                {
                    result[i] = c;
                    continue;
                }
                var scale = Mathf.Clamp(Mathf.Ceil(l * Bands) / Bands / l, 0.7f, 1.3f);
                result[i] = new Color(Mathf.Clamp01(c.r * scale), Mathf.Clamp01(c.g * scale), Mathf.Clamp01(c.b * scale), c.a);
            }
            return result;
        }

        /// <summary>The thick dark ink outline of the toon look: the silhouette grown by <see cref="OutlineWidth"/> pixels in the GUI's ink.</summary>
        public static Color[] Outline(Color[] pixels)
        {
            var ink = GuiPalette.Ink;
            var reach = OutlineWidth + 1;
            var result = new Color[pixels.Length];
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var c = pixels[y * Size + x];
                var best = float.MaxValue;
                if (c.a < 0.999f)
                    for (var dy = -reach; dy <= reach; dy++)
                    for (var dx = -reach; dx <= reach; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= Size || ny >= Size || pixels[ny * Size + nx].a < 0.5f)
                            continue;
                        best = Mathf.Min(best, dx * dx + dy * dy);
                    }
                var coverage = best == float.MaxValue ? 0 : Mathf.Clamp01(OutlineWidth + 0.5f - Mathf.Sqrt(best));
                // The model over its ink
                var a = c.a + coverage * (1 - c.a);
                result[y * Size + x] = a < 0.002f
                    ? Color.clear
                    : new Color((c.r * c.a + ink.r * coverage * (1 - c.a)) / a, (c.g * c.a + ink.g * coverage * (1 - c.a)) / a,
                        (c.b * c.a + ink.b * coverage * (1 - c.a)) / a, a);
            }
            return result;
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

        /// <summary>The longest side an icon is stored at (ticket 61): a head or weapon is drawn at most about 120 px (head) or 200 px
        /// (weapon) wide at 1080p, so these keep it sharp at 1080p and about 1.3x beyond.</summary>
        public const int MaxHead = 192;
        public const int MaxWeapon = 256;

        /// <summary>
        /// The icon at its stored size: the longest side at most <see cref="MaxHead"/> or <see cref="MaxWeapon"/> (area-averaged, in
        /// premultiplied colour so the ink edge keeps no halo), and both sides a multiple of 4 (transparent pixels added) because DXT
        /// and ASTC cannot compress a texture of any other size and Unity then stores it as RGBA32, four times the bytes.
        /// </summary>
        public static Texture2D Fit(Texture2D image, bool weapon)
        {
            var cap = weapon ? MaxWeapon : MaxHead;
            var scale = Mathf.Min(1f, cap / (float)Mathf.Max(image.width, image.height));
            var width = Mathf.Max(4, Mathf.CeilToInt(Mathf.Round(image.width * scale) / 4f) * 4);
            var height = Mathf.Max(4, Mathf.CeilToInt(Mathf.Round(image.height * scale) / 4f) * 4);
            if (width == image.width && height == image.height)
                return image;
            // The scaled picture's size, centred in the padded canvas
            var innerW = Mathf.Min(width, Mathf.Max(1, Mathf.RoundToInt(image.width * scale)));
            var innerH = Mathf.Min(height, Mathf.Max(1, Mathf.RoundToInt(image.height * scale)));
            var offsetX = (width - innerW) / 2;
            var offsetY = (height - innerH) / 2;
            var source = image.GetPixels();
            var result = new Color[width * height];
            for (var y = 0; y < innerH; y++)
            for (var x = 0; x < innerW; x++)
            {
                var x0 = x * (float)image.width / innerW;
                var x1 = (x + 1) * (float)image.width / innerW;
                var y0 = y * (float)image.height / innerH;
                var y1 = (y + 1) * (float)image.height / innerH;
                float r = 0, g = 0, b = 0, a = 0, weight = 0;
                for (var sy = Mathf.FloorToInt(y0); sy < Mathf.Min(image.height, Mathf.CeilToInt(y1)); sy++)
                for (var sx = Mathf.FloorToInt(x0); sx < Mathf.Min(image.width, Mathf.CeilToInt(x1)); sx++)
                {
                    var w = (Mathf.Min(x1, sx + 1) - Mathf.Max(x0, sx)) * (Mathf.Min(y1, sy + 1) - Mathf.Max(y0, sy));
                    var c = source[sy * image.width + sx];
                    r += c.r * c.a * w;
                    g += c.g * c.a * w;
                    b += c.b * c.a * w;
                    a += c.a * w;
                    weight += w;
                }
                result[(y + offsetY) * width + x + offsetX] = a > 0.0001f
                    ? new Color(r / a, g / a, b / a, a / weight)
                    : Color.clear;
            }
            var fitted = new Texture2D(width, height, TextureFormat.RGBA32, false);
            fitted.SetPixels(result);
            fitted.Apply();
            Object.DestroyImmediate(image);
            return fitted;
        }

        /// <summary>Sprite import settings of an icon: no mipmaps, and on the web DXT5 with crunch (WebGL has no other format a
        /// single build can rely on), which keeps the flat toon colours and ink edge clean at this size.</summary>
        public static void Configure(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.maxTextureSize = MaxWeapon;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.crunchedCompression = true;
            importer.compressionQuality = 80;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "WebGL", overridden = true, maxTextureSize = MaxWeapon, format = TextureImporterFormat.DXT5Crunched,
                textureCompression = TextureImporterCompression.Compressed, crunchedCompression = true, compressionQuality = 80,
            });
            importer.SaveAndReimport();
        }

        /// <summary>Fits the PNGs already in the folder to their stored size and applies the import settings (no re-render).</summary>
        [MenuItem("Tools/SDF GUI/Fit Icons")]
        public static void FitExisting()
        {
            foreach (var model in Models)
            {
                var path = $"{Folder}/{model}.png";
                if (!File.Exists(path))
                    continue;
                var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                image.LoadImage(File.ReadAllBytes(path));
                var fitted = Fit(image, MatchView.Weapons.Contains(model));
                if (fitted != image)
                    File.WriteAllBytes(path, fitted.EncodeToPNG());
                Object.DestroyImmediate(fitted);
                AssetDatabase.ImportAsset(path);
                Configure(path);
            }
            AssetDatabase.SaveAssets();
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
            set.avatars = SdfHeads.MonsterHeads.All.Select(h => h.Id).ToArray();
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
