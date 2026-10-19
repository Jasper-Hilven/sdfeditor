using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;

namespace SdfMushrooms.Editor
{
    /// <summary>
    /// Unity looks for changed files only when it gets focus. This polls the generated folder twice a second, also while the editor is in
    /// the background, and imports a model as soon as the pipeline has replaced it (the pipeline writes to a hidden temporary file and
    /// renames it, so the file is always complete); the import rebuilds the prefab, and the views are repainted to show it.
    /// It also takes snapshot requests (see <see cref="MushroomSnapshot.RequestPath"/> and <see cref="MushroomSnapshot.LodRequestPath"/>).
    /// </summary>
    [InitializeOnLoad]
    public static class GeneratedFolderWatcher
    {
        private const double PollSeconds = 0.5;
        private static readonly Dictionary<string, (DateTime Time, long Length)> known = new Dictionary<string, (DateTime, long)>();
        private static double nextPoll;
        private static bool repaintAfterImport;

        static GeneratedFolderWatcher()
        {
            Snapshot(known);
            EditorApplication.update += Poll;
            MushroomBuilder.Built += _ => repaintAfterImport = true;
        }

        private static void Poll()
        {
            if (repaintAfterImport)
            {
                repaintAfterImport = false;
                SceneView.RepaintAll();
                InternalEditorUtility.RepaintAllViews();
                EditorApplication.QueuePlayerLoopUpdate();
            }

            if (EditorApplication.timeSinceStartup < nextPoll)
                return;
            nextPoll = EditorApplication.timeSinceStartup + PollSeconds;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (File.Exists(MushroomSnapshot.RequestPath))
            {
                var directory = File.ReadAllText(MushroomSnapshot.RequestPath).Trim();
                File.Delete(MushroomSnapshot.RequestPath);
                MushroomSnapshot.CaptureOpenScene(directory);
            }
            if (File.Exists(MushroomSnapshot.LodRequestPath))
            {
                var directory = File.ReadAllText(MushroomSnapshot.LodRequestPath).Trim();
                File.Delete(MushroomSnapshot.LodRequestPath);
                MushroomSnapshot.CaptureOpenScene(directory, levelsOfDetail: true);
            }

            var now = new Dictionary<string, (DateTime, long)>();
            Snapshot(now);
            var changed = now.Count != known.Count;
            foreach (var entry in now)
                changed |= !known.TryGetValue(entry.Key, out var before) || before != entry.Value;
            if (!changed)
                return;

            known.Clear();
            foreach (var entry in now)
                known[entry.Key] = entry.Value;
            AssetDatabase.Refresh();
            repaintAfterImport = true;
        }

        private static void Snapshot(Dictionary<string, (DateTime, long)> into)
        {
            if (!Directory.Exists(MushroomBuilder.GeneratedFolder))
                return;
            foreach (var path in Directory.GetFiles(MushroomBuilder.GeneratedFolder, "*.glb"))
            {
                try
                {
                    var info = new FileInfo(path);
                    into[path] = (info.LastWriteTimeUtc, info.Length);
                }
                catch (IOException)
                {
                    // Being replaced right now; the next poll sees the new file
                }
            }
        }
    }
}
