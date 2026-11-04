using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SdfAudio;
using SdfBattle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SdfCampaign.Editor
{
    /// <summary>
    /// Takes the GUI, and the battle music's layers 2 to 4, out of the level scenes for the bundle build (ticket 62). Every level scene holds an instance of the GUI prefab (its
    /// <see cref="Match.gui"/>, for the editor's direct Play), which the campaign destroys at once for Boot's own: but the bundles would carry the
    /// whole GUI (screens, font atlases, icons) a second time. The scene files are backed up in <see cref="BackupFolder"/> (with an index first),
    /// stripped on disk, and <see cref="Dispose"/> puts every file back byte for byte; <see cref="RecoverLeftovers"/> does the same for a build that died.
    /// </summary>
    public sealed class SceneGuiStrip : IDisposable
    {
        public const string BackupFolder = "Library/SceneGuiBackup";
        private const string Index = BackupFolder + "/index.txt";

        [InitializeOnLoadMethod]
        public static void RecoverLeftovers()
        {
            if (!File.Exists(Index))
                return;
            var lines = File.ReadAllLines(Index).Where(l => l.Contains('\t')).Select(l => l.Split('\t')).ToList();
            Debug.Log($"SceneGuiStrip: {lines.Count} scenes were left stripped by a build that did not finish; restoring them");
            Restore(lines.Select(l => (l[0], l[1])).ToList());
        }

        private static void Restore(List<(string path, string backup)> list)
        {
            foreach (var (path, backup) in list)
                File.Copy(backup, path, true);
            foreach (var (path, _) in list)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var bad = list.Where(l => !File.ReadAllBytes(l.path).SequenceEqual(File.ReadAllBytes(l.backup))).ToList();
            if (bad.Count > 0)
                throw new Exception($"SceneGuiStrip: {bad.Count} scenes could not be restored (backups kept in {BackupFolder}), first: {bad[0].path}");
            Directory.Delete(BackupFolder, true);
            Debug.Log($"SceneGuiStrip: {list.Count} scenes restored");
        }

        /// <summary>Per stripped scene, the paths of the music layers 2 to 4 taken out of its <see cref="ArenaAudio"/> (they are built into their own bundle and streamed after the start).</summary>
        public Dictionary<string, List<string>> Music { get; } = new Dictionary<string, List<string>>();

        private readonly List<(string path, string backup)> stripped = new List<(string, string)>();

        /// <summary>Strips the GUI instance out of each scene (backups first); nothing is changed in a scene without one.</summary>
        public void Strip(IEnumerable<string> scenes)
        {
            RecoverLeftovers();
            Directory.CreateDirectory(BackupFolder);
            foreach (var path in scenes)
                stripped.Add((path, Path.Combine(BackupFolder, AssetDatabase.AssetPathToGUID(path) + ".unity")));
            foreach (var (path, backup) in stripped)
                File.Copy(path, backup, true);
            File.WriteAllLines(Index, stripped.Select(s => s.path + "\t" + s.backup));
            var removed = 0;
            foreach (var (path, _) in stripped)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                var changed = false;
                var matches = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Match>(true)).ToList();
                foreach (var match in matches)
                {
                        if (match.gui == null || !match.gui.scene.IsValid() || match.gui.scene != scene)
                        continue;
                    var gui = match.gui;
                    match.gui = null;
                    EditorUtility.SetDirty(match);
                    Object.DestroyImmediate(gui);
                    changed = true;
                    removed++;
                }
                // Ticket 62: layers 2 to 4 of the battle music come after the start (ArenaAudio.Late)
                foreach (var audio in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ArenaAudio>(true)).ToList())
                {
                    var layers = audio.musicLayers;
                    if (layers == null || layers.Length < MusicIntensity.Layers)
                        continue;
                    var clips = new List<string>();
                    for (var i = 1; i < MusicIntensity.Layers; i++)
                        if (layers[i] != null)
                        {
                            clips.Add(AssetDatabase.GetAssetPath(layers[i]));
                            layers[i] = null;
                        }
                    if (clips.Count == 0)
                        continue;
                    Music[path] = clips;
                    EditorUtility.SetDirty(audio);
                    changed = true;
                }
                if (changed)
                    EditorSceneManager.SaveScene(scene);
                EditorSceneManager.CloseScene(scene, true);
            }
            Debug.Log($"SceneGuiStrip: the GUI is out of {removed} of {stripped.Count} level scenes for the bundles, the music layers 2 to 4 out of {Music.Count}");
        }

        public void Dispose()
        {
            if (stripped.Count == 0 && !File.Exists(Index))
                return;
            stripped.Clear();
            RecoverLeftovers();
        }
    }
}
