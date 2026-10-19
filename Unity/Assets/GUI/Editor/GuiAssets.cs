using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace SdfGui.Editor
{
    /// <summary>
    /// The GUI's shared assets, made when missing: TextMesh Pro's essential resources (its SDF text shaders and settings), the font
    /// assets of Lilita One (headlines, buttons) and Fredoka (body text), both SIL Open Font License (Assets/GUI/Fonts/*-OFL.txt), the
    /// text materials with the ink outline and drop shadow of the toon look, and the one material every <see cref="ToonShape"/> draws
    /// with.
    /// </summary>
    public static class GuiAssets
    {
        public const string Folder = "Assets/GUI";
        public const string FontFolder = Folder + "/Fonts";
        public const string MaterialFolder = Folder + "/Materials";
        public const string ShapeShaderPath = Folder + "/Shaders/ToonShape.shader";
        public const string ShapeMaterialPath = MaterialFolder + "/Toon Shape.mat";
        private const string TmpEssentials = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";
        private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        /// <summary>Every character the GUI writes, put in the font atlases up front; others are added when first drawn.</summary>
        private const string Characters =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~·";

        public sealed class Kit
        {
            public Material shape;
            public TMP_FontAsset lilita;
            public TMP_FontAsset fredoka;
            /// <summary>Lilita One with a thick ink outline and a hard drop shadow: titles, buttons, numbers over the world.</summary>
            public Material lilitaInk;
            /// <summary>Fredoka with a thin ink outline, readable over the world.</summary>
            public Material fredokaInk;
            public GuiIconSet icons;
        }

        public static bool TmpEssentialsImported => File.Exists(TmpSettingsPath);

        /// <summary>Imports TextMesh Pro's essential resources, which the font assets need; returns whether they were there already.</summary>
        public static bool EnsureTmpEssentials()
        {
            if (TmpEssentialsImported)
                return true;
            AssetDatabase.ImportPackage(TmpEssentials, false);
            AssetDatabase.Refresh();
            return false;
        }

        /// <summary>Without a window, run before building the GUI: <c>-executeMethod SdfGui.Editor.GuiAssets.ImportTmpEssentialsInBatch</c>.</summary>
        public static void ImportTmpEssentialsInBatch() => EnsureTmpEssentials();

        public static Kit Load()
        {
            EnsureFolder(MaterialFolder);
            var kit = new Kit
            {
                shape = ShapeMaterial(),
                lilita = FontAsset("LilitaOne-Regular.ttf", "Lilita One SDF"),
                fredoka = FontAsset("Fredoka-SemiBold.ttf", "Fredoka SDF"),
            };
            kit.lilitaInk = TextMaterial(kit.lilita, "Lilita One Ink", 0.28f, true);
            kit.fredokaInk = TextMaterial(kit.fredoka, "Fredoka Ink", 0.2f, false);
            kit.icons = AssetDatabase.LoadAssetAtPath<GuiIconSet>(GuiIcons.SetPath);
            return kit;
        }

        private static Material ShapeMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(ShapeMaterialPath);
            if (material != null)
                return material;
            material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(ShapeShaderPath)) { name = "Toon Shape" };
            material.SetColor("_InkColor", GuiPalette.Ink);
            material.SetColor("_SpotColor", GuiPalette.Cream);
            AssetDatabase.CreateAsset(material, ShapeMaterialPath);
            return material;
        }

        private static TMP_FontAsset FontAsset(string fontFile, string assetName)
        {
            var path = $"{FontFolder}/{assetName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null)
                return existing;
            var font = AssetDatabase.LoadAssetAtPath<Font>($"{FontFolder}/{fontFile}");
            // Big glyphs and padding, so thick outlines and drop shadows stay clean
            var asset = TMP_FontAsset.CreateFontAsset(font, 90, 12, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
            asset.name = assetName;
            asset.TryAddCharacters(Characters, out _);
            AssetDatabase.CreateAsset(asset, path);
            foreach (var texture in asset.atlasTextures)
            {
                texture.name = assetName + " Atlas";
                AssetDatabase.AddObjectToAsset(texture, asset);
            }
            asset.material.name = assetName + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static Material TextMaterial(TMP_FontAsset font, string materialName, float outline, bool dropShadow)
        {
            var path = $"{FontFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;
            material = new Material(font.material) { name = materialName };
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, outline);
            material.SetColor(ShaderUtilities.ID_OutlineColor, GuiPalette.Ink);
            material.SetFloat(ShaderUtilities.ID_FaceDilate, outline * 0.6f);
            if (dropShadow)
            {
                material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                material.SetColor(ShaderUtilities.ID_UnderlayColor, GuiPalette.WithAlpha(GuiPalette.Ink, 0.75f));
                material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.35f);
                material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.7f);
                material.SetFloat(ShaderUtilities.ID_UnderlayDilate, outline * 1.6f);
                material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        public static void EnsureFolder(string folder) => SdfMushrooms.Editor.MushroomBuilder.EnsureFolder(folder);
    }
}
