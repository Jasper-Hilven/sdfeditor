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
    /// Makes the project render with HDRP without a single click: an HDRP asset as the render pipeline and linear colour. When the editor opens it also builds any prefab an import missed and shows
    /// the mushroom scene instead of an empty one.
    /// </summary>
    [InitializeOnLoad]
    public static class HdrpProjectSetup
    {
        public const string PipelineAssetPath = "Assets/Settings/HDRenderPipelineAsset.asset";

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
        /// Empties the diffusion profile list of HDRP's default volume profile, which held the realistic look's profiles; the toon look
        /// scatters no light through HDRP.
        /// </summary>
        public static void ClearDiffusionProfiles()
        {
            if (!GraphicsSettings.TryGetRenderPipelineSettings<HDRPDefaultVolumeProfileSettings>(out var settings) || settings.volumeProfile == null)
                return;
            if (!settings.volumeProfile.TryGet<DiffusionProfileList>(out var list) || list.diffusionProfiles.value == null || list.diffusionProfiles.value.Length == 0)
                return;
            list.diffusionProfiles.value = new DiffusionProfileSettings[0];
            EditorUtility.SetDirty(list);
            EditorUtility.SetDirty(settings.volumeProfile);
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
