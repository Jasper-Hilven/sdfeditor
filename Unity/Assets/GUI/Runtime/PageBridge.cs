using System.Runtime.InteropServices;
using SdfPlatform;
using UnityEngine;

namespace SdfGui
{
    /// <summary>
    /// The game's talk with its web page (ticket 57, <c>Assets/Plugins/sdfPage.jslib</c>): on start it adds the page's CSS (no text
    /// selection, no long-press menu, no touch scrolling on the canvas), the AudioContext resume hooks are in the same file, and the page's
    /// safe-area insets come from here. Nothing outside a WebGL build.
    /// </summary>
    public static class PageBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void SdfPageInit();
        [DllImport("__Internal")] private static extern float SdfPageSafeInset(int side);
        [DllImport("__Internal")] private static extern float SdfPageViewport(int axis);
        [DllImport("__Internal")] private static extern void SdfPageResumeAudio();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            try { SdfPageInit(); }
            catch (System.Exception e) { Debug.LogWarning("Page: " + e.Message); }
        }
#endif

        /// <summary>The safe area the page reports (CSS <c>env(safe-area-inset-*)</c>), as fractions of the viewport; none outside a WebGL page.</summary>
        public static SafeInsets PageInsets()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                return SafeAreaRule.FromCss(SdfPageSafeInset(0), SdfPageSafeInset(1), SdfPageSafeInset(2), SdfPageSafeInset(3), SdfPageViewport(0), SdfPageViewport(1));
            }
            catch (System.Exception)
            {
                return SafeInsets.None;
            }
#else
            return SafeInsets.None;
#endif
        }

        /// <summary>Resumes the page's audio now.</summary>
        public static void ResumeAudio()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { SdfPageResumeAudio(); }
            catch (System.Exception) { }
#endif
        }
    }
}
