using UnityEngine;
using UnityEditor;

namespace SdfMedia.Editor
{
    /// <summary>
    /// Tools > SDF Media > Regenerate Promo Videos: records the preview videos (<see cref="PreviewVideoRecorder"/>), whose first half
    /// second is the cover. The covers themselves are not made again: since 2026-11-07 the ones in Media/Covers are hand-retouched art
    /// (originals in Media/Covers/retouched) and <see cref="CoverCapture"/> only renders drafts into Media/Covers/Rendered.
    /// </summary>
    public static class PreviewPromo
    {
        [MenuItem("Tools/SDF Media/Regenerate Promo Videos")]
        public static void Regenerate()
        {
            if (PreviewVideoRecorder.Active)
            {
                Debug.LogWarning("SDF Media: a preview recording is already running (Tools > SDF Media > Cancel Preview Recording).");
                return;
            }
            Debug.Log("SDF Media: recording the preview videos; the retouched covers in Media/Covers are kept as they are.");
            PreviewVideoRecorder.Start();
        }

        /// <summary>The recorder ended (written, failed or cancelled).</summary>
        public static void RecorderFinished(bool ok)
        {
        }
    }
}
