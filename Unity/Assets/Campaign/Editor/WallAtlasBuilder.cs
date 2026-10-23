using System.Collections.Generic;
using System.IO;
using System.Linq;
using SdfStory;
using UnityEditor;
using UnityEngine;

namespace SdfCampaign.Editor
{
    /// <summary>
    /// Makes the thumbnails the menus' wall of comic panels draws (ticket 35): per comic one atlas of its panels (384 x 216 each, four across,
    /// JPG) in Assets/Campaign/Resources/Wall/&lt;comic&gt;.jpg and the index <c>wall-index.txt</c> ("comic panels" per line), from the shipped panels
    /// (the intro's in Assets/Story/Panels, every other comic's in Assets/Story/Resources/Comics/&lt;comic&gt;). The full panels are 1920 x 1080 and
    /// would weigh tens of megabytes on a wall; the atlases weigh about 100 KB each. Generated from committed art and committed too, so a fresh
    /// clone has the wall; run again after a comic is rendered (<see cref="BootBuilder"/> does).
    /// </summary>
    public static class WallAtlasBuilder
    {
        public const string Folder = "Assets/Campaign/Resources/" + WallAtlas.Folder;

        [MenuItem("Tools/SDF Campaign/Build Wall Thumbnails")]
        public static void BuildFromMenu() => Build();

        public static void BuildInBatch() => Build();

        /// <summary>The panels' files of a comic, in order, as far as the numbers run.</summary>
        public static List<string> PanelFiles(string comic)
        {
            var folder = comic == StoryIntro.Intro ? "Assets/Story/Panels" : $"Assets/Story/Resources/{StoryIntro.ComicResources}/{comic}";
            var files = new List<string>();
            for (var number = 1; number <= StoryIntro.MaxPanels; number++)
            {
                var path = $"{folder}/{StoryIntro.PanelName(number)}.jpg";
                if (!File.Exists(path))
                    break;
                files.Add(path);
            }
            return files;
        }

        /// <summary>Makes the atlases and the index; returns the comics made, with their panel counts.</summary>
        public static List<KeyValuePair<string, int>> Build()
        {
            Directory.CreateDirectory(Folder);
            var made = new List<KeyValuePair<string, int>>();
            foreach (var step in CampaignDefinition.Default().Steps)
            {
                if (step.IsBattle || step.kind == StepKind.Credits)
                    continue;
                var files = PanelFiles(step.id);
                if (files.Count == 0)
                    continue;
                var atlasPath = $"{Folder}/{step.id}.jpg";
                if (!UpToDate(atlasPath, files))
                    Write(atlasPath, files);
                made.Add(new KeyValuePair<string, int>(step.id, files.Count));
            }
            File.WriteAllText($"{Folder}/{WallAtlas.IndexName}.txt", WallAtlas.WriteIndex(made));
            // A comic that has no panels any more leaves no atlas behind
            foreach (var path in Directory.GetFiles(Folder, "*.jpg"))
                if (made.All(m => Path.GetFileNameWithoutExtension(path) != m.Key))
                    AssetDatabase.DeleteAsset(path.Replace('\\', '/'));
            AssetDatabase.Refresh();
            foreach (var pair in made)
                Import($"{Folder}/{pair.Key}.jpg");
            AssetDatabase.SaveAssets();
            Debug.Log("SDF Campaign: wall thumbnails for " + string.Join(", ", made.Select(m => $"{m.Key} ({m.Value})")));
            return made;
        }

        // The atlas is older than one of its panels, or is not there
        private static bool UpToDate(string atlas, List<string> panels)
        {
            if (!File.Exists(atlas))
                return false;
            var made = File.GetLastWriteTimeUtc(atlas);
            return panels.All(p => File.GetLastWriteTimeUtc(p) <= made) && ReadsAs(atlas, panels.Count);
        }

        private static bool ReadsAs(string atlas, int count)
        {
            var texture = new Texture2D(2, 2);
            try
            {
                return texture.LoadImage(File.ReadAllBytes(atlas)) && texture.height == WallAtlas.Rows(count) * WallAtlas.ThumbHeight;
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private static void Write(string atlasPath, List<string> files)
        {
            var rows = WallAtlas.Rows(files.Count);
            var width = WallAtlas.Columns * WallAtlas.ThumbWidth;
            var height = rows * WallAtlas.ThumbHeight;
            var pixels = new Color32[width * height];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(0, 0, 0, 255);
            for (var i = 0; i < files.Count; i++)
            {
                var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                source.LoadImage(File.ReadAllBytes(files[i]));
                var from = source.GetPixels32();
                var sw = source.width;
                var sh = source.height;
                Object.DestroyImmediate(source);
                var column = i % WallAtlas.Columns;
                var row = i / WallAtlas.Columns;
                // Row 0 is the atlas's top, which in the texture is its last row of pixels
                var x0 = column * WallAtlas.ThumbWidth;
                var y0 = (rows - 1 - row) * WallAtlas.ThumbHeight;
                for (var y = 0; y < WallAtlas.ThumbHeight; y++)
                for (var x = 0; x < WallAtlas.ThumbWidth; x++)
                    pixels[(y0 + y) * width + x0 + x] = Average(from, sw, sh, x, y);
            }
            var atlas = new Texture2D(width, height, TextureFormat.RGBA32, false);
            atlas.SetPixels32(pixels);
            File.WriteAllBytes(atlasPath, atlas.EncodeToJPG(85));
            Object.DestroyImmediate(atlas);
        }

        // A box average over the source pixels this thumbnail pixel covers
        private static Color32 Average(Color32[] from, int sw, int sh, int x, int y)
        {
            var x0 = x * sw / WallAtlas.ThumbWidth;
            var x1 = Mathf.Max(x0 + 1, (x + 1) * sw / WallAtlas.ThumbWidth);
            var y0 = y * sh / WallAtlas.ThumbHeight;
            var y1 = Mathf.Max(y0 + 1, (y + 1) * sh / WallAtlas.ThumbHeight);
            int r = 0, g = 0, b = 0, n = 0;
            for (var sy = y0; sy < y1; sy++)
            for (var sx = x0; sx < x1; sx++)
            {
                var c = from[sy * sw + sx];
                r += c.r;
                g += c.g;
                b += c.b;
                n++;
            }
            return new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
        }

        private static void Import(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                return;
            // A thumbnail sheet is shown as it is: no mipmaps, clamped, compressed, at most 2048 across
            if (importer.mipmapEnabled == false && importer.wrapMode == TextureWrapMode.Clamp && importer.textureCompression == TextureImporterCompression.Compressed && importer.maxTextureSize == 2048)
                return;
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
    }
}
