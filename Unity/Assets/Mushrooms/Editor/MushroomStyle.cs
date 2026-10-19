using System.IO;
using UnityEditor;
using UnityEngine;

namespace SdfMushrooms.Editor
{
    /// <summary>
    /// The toon look's settings: Assets/Mushrooms/Mushroom Style.asset; select the asset and change them in the inspector. Turning the
    /// outline on or off rebuilds every prefab; changing another setting only updates the toon materials. Defaults match the toon branch of the IDE's preview (sdf render --style toon).
    /// Colours are picked as usual (sRGB) and handed to the shaders in linear.
    /// </summary>
    public sealed class MushroomStyle : ScriptableObject
    {
        public const string AssetPath = "Assets/Mushrooms/Mushroom Style.asset";

        [Header("Bands")]
        [Tooltip("Light (-1 facing away to 1 facing the sun, cast shadow counts as -1) below which the surface is in the shadow tone")]
        [Range(-1, 1)] public float shadeThreshold = 0f;
        [Tooltip("Light above which the surface is in the lit tone; between the two thresholds it is in the mid tone")]
        [Range(-1, 1)] public float litThreshold = 0.45f;
        [Tooltip("Softness of the band edges, in the same units")]
        [Range(0.001f, 0.3f)] public float bandSoftness = 0.03f;
        [Tooltip("How much the baked ambient occlusion darkens crevices")]
        [Range(0, 1)] public float occlusion = 0.5f;
        [Tooltip("The colour is multiplied by these in the three bands; the mid and lit tones also by the sun's colour")]
        public Color shadowTone = Gamma(0.55f, 0.5f, 0.66f);
        public Color midTone = Gamma(0.82f, 0.8f, 0.85f);
        [ColorUsage(false, true)] public Color litTone = Gamma(1.1f, 1.1f, 1.1f);

        [Header("Highlight and rim")]
        [Range(0, 2)] public float highlight = 0.6f;
        [Tooltip("Lower makes highlights larger")]
        [Range(0.05f, 0.95f)] public float highlightThreshold = 0.6f;
        [Tooltip("Surfaces at least this rough get no highlight")]
        [Range(0.1f, 1)] public float highlightMaxRoughness = 0.75f;
        [Range(0, 1)] public float rim = 0.25f;
        [Range(0, 1)] public float rimWidth = 0.3f;

        [Header("Colour")]
        [Tooltip("How much of each material's colour patches and blotches show; speckles always show")]
        [Range(0, 1)] public float colorVariation = 0.5f;

        [Header("Outline")]
        [Tooltip("Width of the ink line in pixels at 1080p; 0 leaves the outline out")]
        [Range(0, 10)] public float outlineWidth = 4f;
        [Tooltip("The line is the surface's base colour multiplied by this")]
        public Color outlineTone = Gamma(0.18f, 0.13f, 0.14f);

        private static Color Gamma(float r, float g, float b) => new Color(r, g, b).gamma;

        public static MushroomStyle Load()
        {
            var style = AssetDatabase.LoadAssetAtPath<MushroomStyle>(AssetPath);
            if (style != null)
                return style;
            style = CreateInstance<MushroomStyle>();
            MushroomBuilder.EnsureFolder(Path.GetDirectoryName(AssetPath));
            AssetDatabase.CreateAsset(style, AssetPath);
            AssetDatabase.SaveAssets();
            return style;
        }

        /// <summary>Puts the toon settings on a toon surface material.</summary>
        public void ApplyToSurface(Material material)
        {
            material.SetVector("_ToonBands", new Vector4(shadeThreshold, litThreshold, bandSoftness, occlusion));
            material.SetVector("_ToonShadowTone", Linear(shadowTone));
            material.SetVector("_ToonMidTone", Linear(midTone));
            material.SetVector("_ToonLitTone", Linear(litTone));
            material.SetVector("_ToonSpecular", new Vector4(highlight, highlightThreshold, highlightMaxRoughness, 0));
            material.SetVector("_ToonRim", new Vector4(rim, rimWidth, 0, 0));
            material.SetVector("_ToonVariation", new Vector4(colorVariation, 0, 0, 0));
            // The patterns' lines are inked like the outline
            material.SetVector("_ToonInk", Linear(outlineTone));
        }

        /// <summary>Puts the outline settings on the outline material.</summary>
        public void ApplyToOutline(Material material)
        {
            material.SetFloat("_OutlineWidth", outlineWidth);
            material.SetVector("_OutlineTone", Linear(outlineTone));
        }

        private static Vector4 Linear(Color color)
        {
            var linear = color.linear;
            return new Vector4(linear.r, linear.g, linear.b, 0);
        }

        // Inspector edits: the toon materials take the new settings at once; prefabs built with or without the outline when it is now
        // the other way are rebuilt, which BuildStale tells from the stamp on each prefab
        private void OnValidate()
        {
            EditorApplication.delayCall += () =>
            {
                MushroomBuilder.ApplyToonSettings();
                MushroomBuilder.BuildStale();
            };
        }
    }
}
