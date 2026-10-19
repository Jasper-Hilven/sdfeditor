using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace SdfMushrooms.Editor
{
    /// <summary>
    /// Makes the project render with HDRP without a single click: an HDRP asset as the render pipeline, linear colour, and the
    /// mushrooms' diffusion profiles registered globally. When the editor opens it also builds any prefab an import missed and shows
    /// the mushroom scene instead of an empty one.
    /// </summary>
    [InitializeOnLoad]
    public static class HdrpProjectSetup
    {
        public const string PipelineAssetPath = "Assets/Settings/HDRenderPipelineAsset.asset";

        /// <summary>HDRP's limit: 16 slots, one of them taken by the neutral profile.</summary>
        public const int MaxDiffusionProfiles = 15;

        static HdrpProjectSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;
                Ensure();
                MushroomBuilder.BuildStale();
                OpenSceneIfNothingIsOpen();
            };
        }

        public static void Ensure()
        {
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
                PlayerSettings.colorSpace = ColorSpace.Linear;

            var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(PipelineAssetPath);
            if (asset == null)
            {
                MushroomBuilder.EnsureFolder(Path.GetDirectoryName(PipelineAssetPath));
                asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
                AssetDatabase.CreateAsset(asset, PipelineAssetPath);
                AssetDatabase.SaveAssets();
            }
            RaiseSubsurfaceSampleBudget(asset);
            if (GraphicsSettings.defaultRenderPipeline != asset)
                GraphicsSettings.defaultRenderPipeline = asset;
            // Quality levels may override the pipeline; let them all use this one. They also decide whether LOD groups cross-fade (HDRP's
            // own "dithering cross-fade" setting moved there), which the mushrooms' LOD groups rely on to change level without a pop
            var current = QualitySettings.GetQualityLevel();
            for (var level = 0; level < QualitySettings.names.Length; level++)
            {
                QualitySettings.SetQualityLevel(level, false);
                if (QualitySettings.renderPipeline != null && QualitySettings.renderPipeline != asset)
                    QualitySettings.renderPipeline = asset;
                if (!QualitySettings.enableLODCrossFade)
                    QualitySettings.enableLODCrossFade = true;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        /// <summary>
        /// Twice HDRP's default subsurface scattering samples per quality level (40 / 80 / 160): the wide, character-scale scattering of
        /// the mushrooms is grainy with fewer. Low stays cheap for the minimum spec.
        /// </summary>
        private static void RaiseSubsurfaceSampleBudget(HDRenderPipelineAsset asset)
        {
            var serialized = new SerializedObject(asset);
            var values = serialized.FindProperty("m_RenderPipelineSettings.sssSampleBudget.m_Values");
            if (values == null || values.arraySize != 3 || values.GetArrayElementAtIndex(2).intValue >= 160)
                return;
            int[] budget = { 40, 80, 160 };
            for (var level = 0; level < 3; level++)
                values.GetArrayElementAtIndex(level).intValue = budget[level];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        /// <summary>
        /// Makes the diffusion profile list of HDRP's default volume profile, which every scene uses, hold exactly these profiles, so a
        /// prefab dragged into any scene scatters light and profiles no longer used make room. HDRP allows 15.
        /// </summary>
        public static void SetDiffusionProfiles(IEnumerable<DiffusionProfileSettings> profiles)
        {
            if (!GraphicsSettings.TryGetRenderPipelineSettings<HDRPDefaultVolumeProfileSettings>(out var settings) || settings.volumeProfile == null)
            {
                Debug.LogWarning("SDF mushrooms: HDRP has no default volume profile yet, so the diffusion profiles could not be registered");
                return;
            }
            var volumeProfile = settings.volumeProfile;
            if (!volumeProfile.TryGet<DiffusionProfileList>(out var list))
            {
                list = volumeProfile.Add<DiffusionProfileList>(true);
                AssetDatabase.AddObjectToAsset(list, volumeProfile);
            }
            var wanted = profiles.ToArray();
            if (wanted.Length > MaxDiffusionProfiles)
                Debug.LogWarning($"SDF mushrooms: HDRP's diffusion profile list is full; {string.Join(", ", wanted.Skip(MaxDiffusionProfiles).Select(p => p.name))} are left out");
            wanted = wanted.Take(MaxDiffusionProfiles).ToArray();
            var current = list.diffusionProfiles.value ?? new DiffusionProfileSettings[0];
            if (list.active && list.diffusionProfiles.overrideState && current.SequenceEqual(wanted))
                return;
            list.active = true;
            list.diffusionProfiles.overrideState = true;
            list.diffusionProfiles.value = wanted;
            EditorUtility.SetDirty(list);
            EditorUtility.SetDirty(volumeProfile);
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
