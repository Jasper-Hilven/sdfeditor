using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SdfMushrooms
{
    /// <summary>
    /// Gives the camera the anti-aliasing of the current quality level: none of its own where the level's URP asset samples each pixel
    /// several times (MSAA, the High preset), FXAA where it does not (the integrated GPU / web preset). URP keeps MSAA in the pipeline
    /// asset but FXAA on the camera, so this follows the quality level when it changes.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public sealed class QualityPresetCamera : MonoBehaviour
    {
        private RenderPipelineAsset applied;

        private void OnEnable() => Update();

        private void Update()
        {
            if (QualitySettings.renderPipeline == applied && applied != null)
                return;
            applied = QualitySettings.renderPipeline;
            Apply(GetComponent<Camera>());
        }

        public static void Apply(Camera camera)
        {
            var data = camera.GetUniversalAdditionalCameraData();
            var multisampled = UniversalRenderPipeline.asset != null && UniversalRenderPipeline.asset.msaaSampleCount > 1;
            data.antialiasing = multisampled ? AntialiasingMode.None : AntialiasingMode.FastApproximateAntialiasing;
        }
    }
}
