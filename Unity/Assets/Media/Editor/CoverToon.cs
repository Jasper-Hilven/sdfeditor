using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SdfMedia.Editor
{
    /// <summary>
    /// High toon for the covers only: while a cover renders, every material with the game's toon settings (the toon, far toon, ground and
    /// room shaders' _ToonBands and friends, and the ink outline's width and tone) gets harder ones: sharp band edges, a more saturated (violet)
    /// shadow tone, a brighter lit tone, bigger flat highlights, a stronger rim and a near-black ink outline, only a little
    /// thicker than the game's (a thick one pokes jagged shards out of the pumpkin's carving). The materials'
    /// own values are recorded first and put back afterwards (nothing is saved), so the game's look (MushroomStyle) is untouched.
    /// </summary>
    internal sealed class CoverToon : IDisposable
    {
        // Gamma colours as the style asset writes them, made linear for the shader
        private static Vector4 Linear(float r, float g, float b) { var c = new Color(r, g, b).linear; return new Vector4(c.r, c.g, c.b, 0); }

        private static readonly Dictionary<string, Vector4> Vectors = new Dictionary<string, Vector4>
        {
            // shade threshold, lit threshold, softness (near hard), occlusion
            ["_ToonBands"] = new Vector4(-0.1f, 0.3f, 0.015f, 0.45f),
            ["_ToonShadowTone"] = Linear(0.7f, 0.6f, 0.86f),
            ["_ToonMidTone"] = Linear(0.95f, 0.92f, 0.98f),
            ["_ToonLitTone"] = Linear(1.25f, 1.22f, 1.16f),
            // strength, threshold, max roughness
            ["_ToonSpecular"] = new Vector4(0.75f, 0.58f, 0.8f, 0),
            // strength, width
            ["_ToonRim"] = new Vector4(0.35f, 0.32f, 0, 0),
            ["_OutlineTone"] = Linear(0.07f, 0.05f, 0.06f),
        };
        private static readonly Dictionary<string, float> Floats = new Dictionary<string, float>
        {
            // pixels at 1080p
            ["_OutlineWidth"] = 4.5f,
        };

        private readonly List<(Material Material, string Name, Vector4 Value)> savedVectors = new List<(Material, string, Vector4)>();
        private readonly List<(Material Material, string Name, float Value)> savedFloats = new List<(Material, string, float)>();
        private readonly HashSet<Material> done = new HashSet<Material>();

        /// <summary>The project's toon material assets, before the level's scene is opened (its renderers may copy them).</summary>
        public static CoverToon OnAssets()
        {
            var toon = new CoverToon();
            foreach (var path in AssetDatabase.FindAssets("t:Material").Select(AssetDatabase.GUIDToAssetPath))
                toon.Apply(AssetDatabase.LoadAssetAtPath<Material>(path));
            return toon;
        }

        /// <summary>Also every material on the renderers of the open scenes (copies the level made of the assets when it loaded).</summary>
        public void OnScenes()
        {
            for (var s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                    continue;
                foreach (var root in scene.GetRootGameObjects())
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    Apply(material);
            }
        }

        private void Apply(Material material)
        {
            if (material == null || !done.Add(material))
                return;
            foreach (var pair in Vectors.Where(p => material.HasProperty(p.Key)))
            {
                savedVectors.Add((material, pair.Key, material.GetVector(pair.Key)));
                material.SetVector(pair.Key, pair.Value);
            }
            foreach (var pair in Floats.Where(p => material.HasProperty(p.Key)))
            {
                savedFloats.Add((material, pair.Key, material.GetFloat(pair.Key)));
                material.SetFloat(pair.Key, pair.Value);
            }
        }

        public void Dispose()
        {
            foreach (var (material, name, value) in savedVectors)
                if (material != null)
                    material.SetVector(name, value);
            foreach (var (material, name, value) in savedFloats)
                if (material != null)
                    material.SetFloat(name, value);
            savedVectors.Clear();
            savedFloats.Clear();
            done.Clear();
        }
    }
}
