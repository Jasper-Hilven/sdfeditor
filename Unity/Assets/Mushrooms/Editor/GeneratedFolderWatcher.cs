using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;

namespace SdfMushrooms.Editor
{
    /// <summary>
    /// Unity looks for changed files only when it gets focus. This polls the generated folders twice a second, also while the editor is
    /// in the background, and imports a model or level file as soon as the pipeline has replaced it (the pipeline writes to a hidden
    /// temporary file and renames it, so the file is always complete); the import rebuilds the prefab or the level's scene
    /// (SdfLevel.Editor.LevelBuilder), and the views are repainted to show it.
    /// It also takes snapshot requests (see <see cref="MushroomSnapshot.RequestPath"/>, <see cref="MushroomSnapshot.LodRequestPath"/> and
    /// SdfLevel.Editor.LevelSnapshot.RequestPath).
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

            if (File.Exists(SdfLevel.Editor.LevelSnapshot.RequestPath))
                SdfLevel.Editor.LevelSnapshot.CaptureRequested();

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

        /// <summary>The models' .glb files, and the levels' chunks, region maps and level.json.</summary>
        private static readonly (string Folder, string Pattern)[] Watched =
        {
            (MushroomBuilder.GeneratedFolder, "*.glb"),
            (SdfLevel.Editor.LevelBuilder.GeneratedFolder, "*.glb"),
            (SdfLevel.Editor.LevelBuilder.GeneratedFolder, "*.json"),
            (SdfLevel.Editor.LevelBuilder.GeneratedFolder, "*.png"),
        };

        private static void Snapshot(Dictionary<string, (DateTime, long)> into)
        {
            foreach (var (folder, pattern) in Watched)
                if (Directory.Exists(folder))
                    Snapshot(into, Directory.GetFiles(folder, pattern, SearchOption.AllDirectories));
        }

        private static void Snapshot(Dictionary<string, (DateTime, long)> into, IEnumerable<string> paths)
        {
            foreach (var path in paths)
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
