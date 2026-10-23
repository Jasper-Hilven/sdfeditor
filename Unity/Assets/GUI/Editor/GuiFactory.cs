using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SdfGui.Editor
{
    public enum TextStyle
    {
        /// <summary>Lilita One with the ink outline and drop shadow.</summary>
        Title,
        /// <summary>Lilita One without outline, for text on light panels.</summary>
        Label,
        /// <summary>Fredoka without outline, for text on light panels.</summary>
        Body,
        /// <summary>Fredoka with a thin ink outline, for text over the world or dark panels.</summary>
        BodyInk,
    }

    /// <summary>
    /// Builds GUI objects in code for <see cref="GuiPrefabs"/>: rect transforms placed against an anchor of their parent (in the
    /// canvas's 1920 × 1080 units), toon shapes, texts in the GUI's styles, instances of the component prefabs, and saving prefabs.
    /// </summary>
    public sealed class GuiFactory
    {
        public readonly GuiAssets.Kit Kit;

        public GuiFactory(GuiAssets.Kit kit) => Kit = kit;

        public static readonly Vector2 TopLeft = new Vector2(0, 1);
        public static readonly Vector2 Top = new Vector2(0.5f, 1);
        public static readonly Vector2 TopRight = new Vector2(1, 1);
        public static readonly Vector2 Left = new Vector2(0, 0.5f);
        public static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);
        public static readonly Vector2 Right = new Vector2(1, 0.5f);
        public static readonly Vector2 BottomLeft = new Vector2(0, 0);
        public static readonly Vector2 Bottom = new Vector2(0.5f, 0);
        public static readonly Vector2 BottomRight = new Vector2(1, 0);

        public static GameObject Object(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.layer = 5; // UI
            if (parent != null)
                gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        /// <summary>Places a rect at <paramref name="position"/> from an anchor of its parent; the pivot is the anchor unless given.</summary>
        public static RectTransform Place(Component component, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            var rect = (RectTransform)component.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot ?? anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>Makes a rect fill its parent, less a margin on every side.</summary>
        public static RectTransform Stretch(Component component, float margin = 0)
        {
            var rect = (RectTransform)component.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = Centre;
            rect.offsetMin = Vector2.one * margin;
            rect.offsetMax = -Vector2.one * margin;
            return rect;
        }

        public ToonShape Shape(Transform parent, string name, ToonShapeKind kind, Color color, Vector2 anchor, Vector2 position, Vector2 size,
            Action<ToonShape> configure = null)
        {
            var shape = Object(name, parent).AddComponent<ToonShape>();
            shape.material = Kit.shape;
            shape.color = color;
            shape.Kind = kind;
            shape.raycastTarget = false;
            switch (kind)
            {
                case ToonShapeKind.Glow:
                case ToonShapeKind.Vignette:
                    shape.Outline = 0;
                    shape.Shadow = 0;
                    shape.Shading = 0;
                    break;
                case ToonShapeKind.Cross:
                case ToonShapeKind.Check:
                case ToonShapeKind.Ring:
                    shape.Outline = 3;
                    shape.Shadow = 3;
                    shape.Shading = 0;
                    break;
            }
            configure?.Invoke(shape);
            Place(shape, anchor, position, size);
            return shape;
        }

        /// <summary>A panel: a rounded box with a thick outline and a drop shadow.</summary>
        public ToonShape Panel(Transform parent, string name, Color color, Vector2 anchor, Vector2 position, Vector2 size, float radius = 28) =>
            Shape(parent, name, ToonShapeKind.Box, color, anchor, position, size, s =>
            {
                s.CornerRadius = radius;
                s.Outline = 6;
                s.Shadow = 10;
            });

        public TextMeshProUGUI Text(Transform parent, string name, string text, TextStyle style, float size, Color color, Vector2 anchor,
            Vector2 position, Vector2 box, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var label = Object(name, parent).AddComponent<TextMeshProUGUI>();
            var lilita = style == TextStyle.Title || style == TextStyle.Label;
            label.font = lilita ? Kit.lilita : Kit.fredoka;
            label.fontSharedMaterial = style switch
            {
                TextStyle.Title => Kit.lilitaInk,
                TextStyle.BodyInk => Kit.fredokaInk,
                _ => label.font.material,
            };
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            label.richText = true;
            Place(label, anchor, position, box);
            return label;
        }

        /// <summary>Fills a text's letters with a colour fading from <paramref name="top"/> to <paramref name="bottom"/> (the text's colour is then white).</summary>
        public static T Gradient<T>(T label, Color top, Color bottom) where T : TMP_Text
        {
            label.color = Color.white;
            label.enableVertexGradient = true;
            label.colorGradient = new VertexGradient(top, top, bottom, bottom);
            return label;
        }

        /// <summary>
        /// One word of a logo in two layers: a back one with the cream sticker border and the ink extrusion, a front one with the
        /// gradient face and the ink outline, both bent and bounced alike by <see cref="GuiTextWarp"/>. Returns the front text.
        /// </summary>
        public TextMeshProUGUI LogoWord(Transform parent, string name, string text, float size, Color top, Color bottom, Vector2 position, Vector2 box,
            float arch, float archTilt, float bounce)
        {
            TextMeshProUGUI Layer(string layer, Material material, Color colour)
            {
                var label = Text(parent, layer, text, TextStyle.Title, size, colour, Centre, position, box);
                label.fontSharedMaterial = material;
                label.characterSpacing = 4;
                var warp = label.gameObject.AddComponent<GuiTextWarp>();
                warp.arch = arch;
                warp.archTilt = archTilt;
                warp.bounce = bounce;
                return label;
            }
            Layer(name + " Back", Kit.logoBack, GuiPalette.Cream);
            // The letters' thickness: an ink copy straight below
            position += new Vector2(0, -9);
            Layer(name + " Extrusion", Kit.logoFront, GuiPalette.Ink);
            position += new Vector2(0, 9);
            return Gradient(Layer(name, Kit.logoFront, Color.white), top, bottom);
        }

        /// <summary>A text filling its parent, centred.</summary>
        public TextMeshProUGUI FillText(Transform parent, string name, string text, TextStyle style, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var label = Text(parent, name, text, style, size, color, Centre, Vector2.zero, Vector2.zero, alignment);
            Stretch(label);
            return label;
        }

        public Image Icon(Transform parent, string name, string icon, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var image = Object(name, parent).AddComponent<Image>();
            image.sprite = Kit.icons != null ? Kit.icons.Get(icon) : null;
            image.preserveAspect = true;
            image.raycastTarget = false;
            Place(image, anchor, position, size);
            return image;
        }

        /// <summary>An instance of a prefab under <paramref name="parent"/>, placed like any rect.</summary>
        public static T Instance<T>(GameObject prefab, Transform parent, string name, Vector2 anchor, Vector2 position, Vector2? size = null)
            where T : Component
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            var rect = (RectTransform)instance.transform;
            Place(rect, anchor, position, size ?? rect.sizeDelta);
            return instance.GetComponent<T>();
        }

        /// <summary>
        /// Saves a hierarchy as a prefab (replacing the one at the path, keeping its GUID so instances elsewhere follow) and destroys it.
        /// Changes made to the prefab instances inside it in code are recorded first, so they are saved as overrides.
        /// </summary>
        public static GameObject Save(GameObject root, string path)
        {
            GuiAssets.EnsureFolder(Path.GetDirectoryName(path)!.Replace('\\', '/'));
            Record(root);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        public static void Record(GameObject root)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
                if (component != null && PrefabUtility.IsPartOfPrefabInstance(component))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                if (PrefabUtility.IsPartOfPrefabInstance(transform.gameObject))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(transform.gameObject);
        }
    }
}
