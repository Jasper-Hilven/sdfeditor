using System.Linq;
using TMPro;
using UnityEngine.TextCore.LowLevel;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using SdfGui;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SdfMedia.Editor
{
    /// <summary>
    /// The cover's title, in the way of the Cogs &amp; Corpses covers: big, one word a line, meeting at the middle, in the teams' colours:
    /// MINIGUN red, MONSTERS blue, with black outlines only (the logo lettering of <see cref="SdfGui.Editor.GuiFactory.LogoWord"/> with
    /// its cream back layer turned to ink, sized to the format). <see cref="CoverCapture"/> blurs and darkens the scene behind it. It sits
    /// on a world-space canvas of the cover's own size (one canvas unit a pixel) far below everything, drawn by an orthographic camera
    /// that sees only the UI layer, at <see cref="CoverCapture.Super"/> times the size, once over black and once over white; the two give
    /// the title's colour and its transparency (the way GuiSnapshot gets a GUI over the world), and <see cref="CoverCapture"/> lays it
    /// over the picture.
    /// </summary>
    internal static class CoverLogo
    {
        /// <summary>The UI layer, which the logo's objects are on and only its camera sees.</summary>
        public const int UiLayer = 5;
        /// <summary>Where the canvas hangs, far from anything else in the scenes.</summary>
        private static readonly Vector3 CanvasPosition = new Vector3(0, -5000, 0);

        /// <summary>The logo of a format at the format's size, over black and over white, rows from the bottom.</summary>
        public static void Draw(Scene scene, CoverFormat format, out Color32[] overBlack, out Color32[] overWhite)
        {
            var canvasObject = new GameObject("Cover Logo Canvas", typeof(RectTransform), typeof(Canvas));
            var cameraObject = EditorUtility.CreateGameObjectWithHideFlags("Cover Logo Camera", HideFlags.HideAndDontSave, typeof(Camera));
            RenderTexture target = null;
            try
            {
                SceneManager.MoveGameObjectToScene(canvasObject, scene);
                canvasObject.layer = UiLayer;
                canvasObject.transform.position = CanvasPosition;
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                // The shape and text shaders read these
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3;
                var canvasRect = (RectTransform)canvasObject.transform;
                canvasRect.sizeDelta = new Vector2(format.Width, format.Height);
                canvasRect.localScale = Vector3.one;

                // The title in the teams' colours: MINIGUN red just above the middle, MONSTERS blue just below it, black outlines only
                var factory = new SdfGui.Editor.GuiFactory(SdfGui.Editor.GuiAssets.Load());
                var title = new GameObject("Cover Title", typeof(RectTransform));
                var titleRect = (RectTransform)title.transform;
                titleRect.SetParent(canvasRect, false);
                titleRect.anchorMin = Vector2.zero;
                titleRect.anchorMax = Vector2.one;
                titleRect.sizeDelta = Vector2.zero;
                float width = format.Width, height = format.Height;
                var x = CoverCapture.Studio ? (CoverStudio.LayoutOf(format).TitleX - 0.5f) * width : 0;
                var size = CoverCapture.TitleSize(format);
                var box = new Vector2(width, size * 1.3f);
                var drop = CoverCapture.TitleOffset(format.Width, format.Height);
                factory.LogoWord(titleRect, "Minigun", "MINIGUN", size, GuiPalette.Hex(0xFF5A48), GuiPalette.Hex(0xC41A1E), new Vector2(x, size * 0.5f - drop), box, 0, 0, size * 0.03f);
                factory.LogoWord(titleRect, "Monsters", "MONSTERS", size, GuiPalette.Hex(0x5EA2FF), GuiPalette.Hex(0x1C4CD2), new Vector2(x, -size * 0.62f - drop), box, 0, 0, size * 0.03f);
                // Black outlines only: the lettering's cream back layer (the logo's light edge) turned to ink
                var ink = new Color(0.05f, 0.03f, 0.04f);
                foreach (var back in titleRect.GetComponentsInChildren<TMP_Text>(true).Where(t => t.name.EndsWith(" Back")))
                {
                    var inked = new Material(back.fontSharedMaterial) { hideFlags = HideFlags.HideAndDontSave };
                    inked.SetColor(ShaderUtilities.ID_OutlineColor, ink);
                    inked.SetColor(ShaderUtilities.ID_UnderlayColor, ink);
                    back.fontSharedMaterial = inked;
                    back.color = ink;
                }
                if (Style != null)
                {
                    Restyle(titleRect, Style);
                    Fit(titleRect, width * 0.86f, -drop);
                }
                foreach (var child in canvasObject.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = UiLayer;

                // The shapes and the lettering build their meshes now
                Canvas.ForceUpdateCanvases();
                foreach (var text in canvasObject.GetComponentsInChildren<TMP_Text>(true))
                    text.ForceMeshUpdate();
                Canvas.ForceUpdateCanvases();

                var camera = cameraObject.GetComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = format.Height / 2f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 50;
                camera.cullingMask = 1 << UiLayer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.transform.SetPositionAndRotation(CanvasPosition + new Vector3(0, 0, -10), Quaternion.identity);
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.renderShadows = false;
                data.antialiasing = AntialiasingMode.None;

                target = new RenderTexture(format.Width * CoverCapture.Super, format.Height * CoverCapture.Super, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                camera.targetTexture = target;
                camera.aspect = format.Aspect;
                camera.backgroundColor = Color.black;
                camera.Render();
                overBlack = CoverCapture.Downscale(target, format.Width, format.Height);
                camera.backgroundColor = Color.white;
                camera.Render();
                overWhite = CoverCapture.Downscale(target, format.Width, format.Height);
                camera.targetTexture = null;
            }
            finally
            {
                if (target != null)
                {
                    target.Release();
                    Object.DestroyImmediate(target);
                }
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(canvasObject);
            }
        }

        // ------------------------------------------------------------------------------------------------------------ Title styles

        /// <summary>A look for the title tried on the covers (2026-11-03, "too playful"): a font, a slant, the outline's weight and the colours.</summary>
        internal sealed class TitleStyle
        {
            public string Id;
            /// <summary>The font file (a cover-only font under Media/Editor/Fonts); null keeps Lilita One.</summary>
            public string FontPath;
            /// <summary>The italic shear, in TMP's units (Lilita's own is 35); 0 for upright.</summary>
            public float Slant;
            public float Spacing;
            /// <summary>How chunky the ink outline and shadow are (1: the logo's own).</summary>
            public float Outline = 1;
            public Color RedTop, RedBottom, BlueTop, BlueBottom;
        }

        private static readonly Color DarkRedTop = GuiPalette.Hex(0xFF6B4A), DarkRedBottom = GuiPalette.Hex(0x8A0E12);
        private static readonly Color SteelTop = GuiPalette.Hex(0x9AD0FF), SteelBottom = GuiPalette.Hex(0x10307E);
        private const string FontFolder = "Assets/Media/Editor/Fonts/";

        public static readonly TitleStyle[] Styles =
        {
            new TitleStyle { Id = "lilita-dark", Slant = 14, Spacing = 0, Outline = 0.7f, RedTop = DarkRedTop, RedBottom = DarkRedBottom, BlueTop = SteelTop, BlueBottom = SteelBottom },
            new TitleStyle { Id = "anton", FontPath = FontFolder + "Anton-Regular.ttf", Slant = 16, Spacing = 2, Outline = 0.6f, RedTop = DarkRedTop, RedBottom = DarkRedBottom, BlueTop = SteelTop, BlueBottom = SteelBottom },
            new TitleStyle { Id = "blackops", FontPath = FontFolder + "BlackOpsOne-Regular.ttf", Slant = 0, Spacing = 0, Outline = 0.6f, RedTop = DarkRedTop, RedBottom = DarkRedBottom, BlueTop = SteelTop, BlueBottom = SteelBottom },
            new TitleStyle { Id = "russo", FontPath = FontFolder + "RussoOne-Regular.ttf", Slant = 16, Spacing = 0, Outline = 0.6f, RedTop = DarkRedTop, RedBottom = DarkRedBottom, BlueTop = SteelTop, BlueBottom = SteelBottom },
        };

        /// <summary>The title's style (Black Ops One, chosen 2026-11-03); null draws the in-game logo (Lilita One) as it is.</summary>
        public static TitleStyle Style = Styles.First(s => s.Id == "blackops");

        /// <summary>
        /// Shrinks the title when its widest word is wider than <paramref name="maxWidth"/> (Black Ops One runs wider than Lilita One and
        /// touched the edges of the square and portrait covers), the two lines drawn together about the title's middle height.
        /// </summary>
        private static void Fit(RectTransform title, float maxWidth, float middle)
        {
            var texts = title.GetComponentsInChildren<TMP_Text>(true);
            foreach (var text in texts)
                text.ForceMeshUpdate();
            var widest = texts.Where(t => t.name == "Minigun" || t.name == "Monsters").Max(t => t.textBounds.size.x);
            if (widest <= maxWidth)
                return;
            var f = maxWidth / widest;
            foreach (var text in texts)
            {
                text.fontSize *= f;
                var rect = text.rectTransform;
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, middle + (rect.anchoredPosition.y - middle) * f);
            }
        }

        /// <summary>The title redrawn in a style: each layer's font swapped (its material kept, on the new atlas), sized to the old cap height, slanted and recoloured.</summary>
        private static void Restyle(RectTransform title, TitleStyle style)
        {
            var texts = title.GetComponentsInChildren<TMP_Text>(true);
            var oldFont = texts[0].font;
            var font = oldFont;
            if (style.FontPath != null)
            {
                var source = AssetDatabase.LoadAssetAtPath<Font>(style.FontPath) ?? throw new System.InvalidOperationException(style.FontPath + " is missing");
                font = TMP_FontAsset.CreateFontAsset(source, 90, 12, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
                font.hideFlags = HideFlags.HideAndDontSave;
                font.TryAddCharacters("MINGUOSTER");
            }
            else
            {
                font = Object.Instantiate(oldFont);
                font.hideFlags = HideFlags.HideAndDontSave;
            }
            font.italicStyle = (byte)Mathf.Max(1, style.Slant);
            float Cap(TMP_FontAsset f) => f.faceInfo.capLine / f.faceInfo.pointSize;
            var scale = Cap(oldFont) / Cap(font);
            foreach (var text in texts)
            {
                var material = new Material(text.fontSharedMaterial) { hideFlags = HideFlags.HideAndDontSave };
                text.font = font;
                material.SetTexture(ShaderUtilities.ID_MainTex, font.atlasTexture);
                material.SetFloat(ShaderUtilities.ID_TextureWidth, font.atlasWidth);
                material.SetFloat(ShaderUtilities.ID_TextureHeight, font.atlasHeight);
                material.SetFloat(ShaderUtilities.ID_GradientScale, font.atlasPadding + 1);
                foreach (var id in new[] { ShaderUtilities.ID_OutlineWidth, ShaderUtilities.ID_FaceDilate, ShaderUtilities.ID_UnderlayDilate })
                    material.SetFloat(id, material.GetFloat(id) * style.Outline);
                text.fontSharedMaterial = material;
                text.fontSize *= scale;
                text.characterSpacing = style.Spacing;
                if (style.Slant > 0)
                    text.fontStyle |= FontStyles.Italic;
                if (text.name == "Minigun")
                    text.colorGradient = new VertexGradient(style.RedTop, style.RedTop, style.RedBottom, style.RedBottom);
                else if (text.name == "Monsters")
                    text.colorGradient = new VertexGradient(style.BlueTop, style.BlueTop, style.BlueBottom, style.BlueBottom);
            }
        }
    }
}
