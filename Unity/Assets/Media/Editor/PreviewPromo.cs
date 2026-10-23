using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SdfMedia.Editor
{
    /// <summary>
    /// Tools > SDF Media > Regenerate Promo Videos And Cover Art: the whole promo set in one go. First the cover images, by running the
    /// cover tool's own menu item (CoverCapture, written next to this: the first menu item under Tools/SDF Media whose path has "cover"
    /// in it, found by looking through the editor's menu items so this file does not have to know its exact name; set
    /// <see cref="CoverMenuItem"/> to name it), then, once the three covers <c>Media/Covers/cover-landscape.png</c>, <c>cover-portrait.png</c>
    /// and <c>cover-square.png</c> have all been written anew (or after <see cref="CoverWaitMinutes"/>, with the old ones), the preview
    /// videos (<see cref="PreviewVideoRecorder"/>), whose first half second is the cover. Without a cover menu item the videos are made
    /// with the covers that are there.
    /// </summary>
    [InitializeOnLoad]
    public static class PreviewPromo
    {
        /// <summary>The cover tool's menu path (CoverCapture renders all three covers); empty to look for one by name.</summary>
        public const string CoverMenuItem = "Tools/SDF Media/Render Covers";

        public const int CoverWaitMinutes = 20;

        private const string WaitingKey = "SdfMedia.Promo.Waiting";
        private const string SinceKey = "SdfMedia.Promo.Since";

        private static bool waiting;

        static PreviewPromo()
        {
            waiting = SessionState.GetBool(WaitingKey, false);
            EditorApplication.update += Update;
        }

        [MenuItem("Tools/SDF Media/Regenerate Promo Videos And Cover Art")]
        public static void Regenerate()
        {
            if (PreviewVideoRecorder.Active || waiting)
            {
                Debug.LogWarning("SDF Media: a promo run is already going (Tools > SDF Media > Cancel Preview Recording).");
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                EditorUtility.DisplayDialog("Regenerate Promo", "Stop Play mode and wait for the compile to finish first.", "OK");
                return;
            }
            var path = FindCoverMenu();
            if (path == null)
            {
                Debug.LogWarning("SDF Media: no cover tool menu item found under Tools/SDF Media; making the videos with the covers that are there.");
                PreviewVideoRecorder.Start();
                return;
            }
            SessionState.SetString(SinceKey, DateTime.UtcNow.Ticks.ToString());
            SetWaiting(true);
            Debug.Log($"SDF Media: regenerating the cover art ({path}), then the preview videos.");
            if (!EditorApplication.ExecuteMenuItem(path))
            {
                Debug.LogWarning($"SDF Media: the menu item {path} could not be run; making the videos with the covers that are there.");
                SetWaiting(false);
                PreviewVideoRecorder.Start();
            }
        }

        /// <summary>The recorder ended (written, failed or cancelled): nothing is waiting any more.</summary>
        public static void RecorderFinished(bool ok) => SetWaiting(false);

        private static void SetWaiting(bool value)
        {
            waiting = value;
            SessionState.SetBool(WaitingKey, value);
        }

        /// <summary>The cover tool's menu path: <see cref="CoverMenuItem"/>, else the first plain menu item under Tools/SDF Media with "cover" in its name that is not this file's.</summary>
        private static string FindCoverMenu()
        {
            if (!string.IsNullOrEmpty(CoverMenuItem))
                return CoverMenuItem;
            return TypeCache.GetMethodsWithAttribute<MenuItem>()
                .Where(method => method.DeclaringType != typeof(PreviewPromo) && method.DeclaringType != typeof(PreviewVideoRecorder))
                .SelectMany(method => method.GetCustomAttributes<MenuItem>().Where(item => !item.validate).Select(item => item.menuItem))
                .FirstOrDefault(menu => menu.StartsWith("Tools/SDF Media/", StringComparison.Ordinal) && menu.IndexOf("cover", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // Waits for the cover tool to be done (idle editor, the three covers newer than the start), then starts the videos
        private static void Update()
        {
            if (!waiting || PreviewVideoRecorder.Active)
                return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            long.TryParse(SessionState.GetString(SinceKey, "0"), out var ticks);
            var since = new DateTime(ticks, DateTimeKind.Utc);
            var elapsed = DateTime.UtcNow - since;
            if (elapsed.TotalSeconds < 2)
                return;
            var covers = PreviewVideoRecorder.Shapes.Select(PreviewVideoEncoder.CoverPath).ToList();
            var fresh = covers.All(cover => File.Exists(cover) && File.GetLastWriteTimeUtc(cover) >= since);
            if (!fresh && elapsed.TotalMinutes < CoverWaitMinutes)
                return;
            if (!fresh)
                Debug.LogWarning("SDF Media: the covers were not all rewritten in time; the videos use the ones that are there.");
            SetWaiting(false);
            PreviewVideoRecorder.Start();
        }
    }
}
