using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SdfMushrooms.Editor
{
    /// <summary>
    /// Makes the project render with URP without a single click: linear colour, one universal renderer, and a URP asset for each of the
    /// quality levels, best first: <see cref="UltraHigh"/>, <see cref="High"/> (the dev machine), <see cref="Web"/> (integrated GPUs and
    /// the browser) and <see cref="UltraLow"/>, each with LOD cross-fade on, which the models' LOD groups rely on to change level without
    /// a pop. Desktop builds start on High, web builds on Web. When the editor opens it also builds any prefab and level scene an import missed and shows the mushroom scene instead
    /// of an empty one.
    /// <para>
    /// The presets live here, not in the assets: what is set below is put back each time the editor loads, so change them in this file.
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    public static class RenderPipelineSetup
    {
        public const string SettingsFolder = "Assets/Settings";
        public const string RendererPath = SettingsFolder + "/Universal Renderer.asset";
        private const string PostProcessDataPath = "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";

        /// <summary>A quality level and the URP asset it renders with.</summary>
        public sealed class Preset
        {
            public string Name;
            public string AssetPath;
            public float ShadowDistance;
            public int ShadowCascades;
            public int ShadowResolution;
            public SoftShadowQuality SoftShadows;
            /// <summary>1 is off; the camera then smooths edges with FXAA instead (<see cref="SdfMushrooms.QualityPresetCamera"/>).</summary>
            public int Msaa;
            public float RenderScale;
            public bool Hdr;
            public float LodBias;
        }

        /// <summary>
        /// Beyond the dev machine: shadows twice as far and softer, eight samples per pixel on a supersampled target, and finer levels of
        /// detail further out. Still one cascade, as <see cref="High"/>.
        /// </summary>
        public static readonly Preset UltraHigh = new Preset
        {
            Name = "Ultra High", AssetPath = SettingsFolder + "/URP Ultra High.asset",
            ShadowDistance = 120, ShadowCascades = 1, ShadowResolution = 4096, SoftShadows = SoftShadowQuality.High,
            Msaa = 8, RenderScale = 1.25f, Hdr = true, LodBias = 1.5f,
        };

        /// <summary>
        /// The dev machine (M4 Max): shadows far out, four samples per pixel at full resolution. One cascade, like the minimum spec: the
        /// toon look's crisp shadow edges showed every cascade border (a shadow's outline snapping finer or coarser as it crossed one), and
        /// the same detail everywhere reads better than sharper shadows near the eye that change as one walks.
        /// </summary>
        public static readonly Preset High = new Preset
        {
            Name = "High", AssetPath = SettingsFolder + "/URP High.asset",
            ShadowDistance = 60, ShadowCascades = 1, ShadowResolution = 4096, SoftShadows = SoftShadowQuality.Medium,
            Msaa = 4, RenderScale = 1, Hdr = true, LodBias = 1,
        };

        /// <summary>
        /// The minimum spec: integrated GPUs and the web build. Shadows only nearby in one cascade, three quarters of the resolution
        /// with FXAA, no HDR target, and coarser levels of detail sooner.
        /// </summary>
        public static readonly Preset Web = new Preset
        {
            Name = "Integrated GPU / Web", AssetPath = SettingsFolder + "/URP Integrated GPU and Web.asset",
            ShadowDistance = 30, ShadowCascades = 1, ShadowResolution = 1024, SoftShadows = SoftShadowQuality.Low,
            Msaa = 1, RenderScale = 0.75f, Hdr = false, LodBias = 0.7f,
        };

        /// <summary>
        /// Below the minimum spec: shadows only right around the player at a small map, half resolution with FXAA, no HDR target, and the
        /// coarser levels of detail soonest.
        /// </summary>
        public static readonly Preset UltraLow = new Preset
        {
            Name = "Ultra Low", AssetPath = SettingsFolder + "/URP Ultra Low.asset",
            ShadowDistance = 15, ShadowCascades = 1, ShadowResolution = 512, SoftShadows = SoftShadowQuality.Low,
            Msaa = 1, RenderScale = 0.5f, Hdr = false, LodBias = 0.4f,
        };

        /// <summary>The quality levels, best first.</summary>
        public static readonly IReadOnlyList<Preset> Presets = new[] { UltraHigh, High, Web, UltraLow };

        /// <summary>The quality level desktop players start on, <see cref="High"/>.</summary>
        public static int DesktopLevel => IndexOf(High);
        /// <summary>The quality level web players start on, <see cref="Web"/>.</summary>
        public static int WebLevel => IndexOf(Web);

        private static int IndexOf(Preset preset)
        {
            for (var i = 0; i < Presets.Count; i++)
                if (Presets[i] == preset)
                    return i;
            return 0;
        }

        static RenderPipelineSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;
                Ensure();
                MushroomBuilder.BuildStale();
                // After the prefabs, which the levels place
                SdfLevel.Editor.LevelBuilder.BuildStale();
                OpenSceneIfNothingIsOpen();
            };
        }

        public static void Ensure()
        {
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
                PlayerSettings.colorSpace = ColorSpace.Linear;

            var renderer = RendererAsset();
            var assets = Presets.Select(p => PipelineAsset(p, renderer)).ToArray();
            if (GraphicsSettings.defaultRenderPipeline != assets[DesktopLevel])
                GraphicsSettings.defaultRenderPipeline = assets[DesktopLevel];
            EnsureQualityLevels(assets);
        }

        /// <summary>The one renderer every preset draws with: forward, so every light the toon look reads is the main one.</summary>
        private static UniversalRendererData RendererAsset()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer != null)
                return renderer;
            MushroomBuilder.EnsureFolder(SettingsFolder);
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(PostProcessDataPath);
            renderer.renderingMode = RenderingMode.Forward;
            AssetDatabase.CreateAsset(renderer, RendererPath);
            AssetDatabase.SaveAssets();
            return renderer;
        }

        /// <summary>The preset's URP asset, created on first use, with the preset's settings put on it (saved only if they changed).</summary>
        private static UniversalRenderPipelineAsset PipelineAsset(Preset preset, UniversalRendererData renderer)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(preset.AssetPath);
            if (asset == null)
            {
                MushroomBuilder.EnsureFolder(SettingsFolder);
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, preset.AssetPath);
            }

            // Most of these have no public setter
            var serialized = new SerializedObject(asset);
            Set(serialized, "m_SupportsHDR", preset.Hdr);
            Set(serialized, "m_ColorGradingMode", (int)(preset.Hdr ? ColorGradingMode.HighDynamicRange : ColorGradingMode.LowDynamicRange));
            Set(serialized, "m_MSAA", preset.Msaa);
            Set(serialized, "m_RenderScale", preset.RenderScale);
            Set(serialized, "m_EnableLODCrossFade", true);
            Set(serialized, "m_MainLightRenderingMode", (int)LightRenderingMode.PerPixel);
            Set(serialized, "m_MainLightShadowsSupported", true);
            Set(serialized, "m_MainLightShadowmapResolution", preset.ShadowResolution);
            Set(serialized, "m_ShadowDistance", preset.ShadowDistance);
            Set(serialized, "m_ShadowCascadeCount", preset.ShadowCascades);
            Set(serialized, "m_SoftShadowsSupported", true);
            Set(serialized, "m_SoftShadowQuality", (int)preset.SoftShadows);
            Set(serialized, "m_UseSRPBatcher", true);
            if (serialized.ApplyModifiedPropertiesWithoutUndo())
            {
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
            }
            return asset;
        }

        private static void Set(SerializedObject serialized, string name, bool value)
        {
            var property = serialized.FindProperty(name);
            if (property.boolValue != value)
                property.boolValue = value;
        }

        private static void Set(SerializedObject serialized, string name, int value)
        {
            var property = serialized.FindProperty(name);
            if (property.propertyType == SerializedPropertyType.Enum ? property.enumValueFlag != value : property.intValue != value)
                property.intValue = value;
        }

        private static void Set(SerializedObject serialized, string name, float value)
        {
            var property = serialized.FindProperty(name);
            if (!Mathf.Approximately(property.floatValue, value))
                property.floatValue = value;
        }

        /// <summary>
        /// Exactly the presets as quality levels, in order, each with its URP asset, LOD bias and cross-fade; desktop players start on
        /// <see cref="High"/> and web players on <see cref="Web"/>. Quality levels have no API to add or remove them, so this edits the settings file.
        /// </summary>
        private static void EnsureQualityLevels(IReadOnlyList<UniversalRenderPipelineAsset> assets)
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset").FirstOrDefault();
            if (settings == null)
                return;
            var serialized = new SerializedObject(settings);
            var levels = serialized.FindProperty("m_QualitySettings");
            if (levels.arraySize != Presets.Count)
                levels.arraySize = Presets.Count;
            for (var i = 0; i < Presets.Count; i++)
            {
                var level = levels.GetArrayElementAtIndex(i);
                var preset = Presets[i];
                void SetLevel(string name, object value)
                {
                    var property = level.FindPropertyRelative(name);
                    switch (value)
                    {
                        case string text when property.stringValue != text: property.stringValue = text; break;
                        case bool flag when property.boolValue != flag: property.boolValue = flag; break;
                        case int number when property.intValue != number: property.intValue = number; break;
                        case float number when !Mathf.Approximately(property.floatValue, number): property.floatValue = number; break;
                        case Object reference when property.objectReferenceValue != reference: property.objectReferenceValue = reference; break;
                    }
                }
                SetLevel("name", preset.Name);
                SetLevel("customRenderPipeline", assets[i]);
                SetLevel("lodBias", preset.LodBias);
                SetLevel("maximumLODLevel", 0);
                SetLevel("enableLODCrossFade", true);
                // Full-size textures (the levels this replaced lowered them); URP takes anti-aliasing and shadows from its asset
                SetLevel("globalTextureMipmapLimit", 0);
            }

            var platforms = serialized.FindProperty("m_PerPlatformDefaultQuality");
            for (var i = 0; i < platforms.arraySize; i++)
            {
                var platform = platforms.GetArrayElementAtIndex(i);
                var name = platform.FindPropertyRelative("first")?.stringValue;
                var value = platform.FindPropertyRelative("second");
                if (name == null || value == null)
                    continue;
                var level = name is "WebGL" or "Android" or "iPhone" or "tvOS" ? WebLevel : DesktopLevel;
                if (value.intValue != level)
                    value.intValue = level;
            }
            var current = serialized.FindProperty("m_CurrentQuality");
            if (current.intValue >= Presets.Count)
                current.intValue = DesktopLevel;

            if (serialized.ApplyModifiedPropertiesWithoutUndo())
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
        }

        private static void OpenSceneIfNothingIsOpen()
        {
            if (Application.isBatchMode || !File.Exists(MushroomBuilder.ScenePath))
                return;
            var active = EditorSceneManager.GetActiveScene();
            var untitled = string.IsNullOrEmpty(active.path) && !active.isDirty && EditorSceneManager.sceneCount == 1;
            if (untitled)
                EditorSceneManager.OpenScene(MushroomBuilder.ScenePath, OpenSceneMode.Single);
        }
    }
}
