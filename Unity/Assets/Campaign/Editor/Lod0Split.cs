using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SdfLevel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SdfCampaign.Editor
{
    /// <summary>
    /// The LOD0 split of the streamed bundles (ticket 60). <see cref="Find"/> lists the level meshes only the finest level of a LOD group uses
    /// (a MeshFilter in a LOD group's LOD0 slot, or a scatter prototype's level 0 part, and no other reference in the scenes, prefabs and assets of the
    /// closure, none from the player); <see cref="Stub"/> then replaces each of those mesh assets, on disk, by a three-vertex stub with the same name plus
    /// <see cref="Lod0Meshes.StubSuffix"/>, the same bounds, the same GUID: the level bundles are built with the stubs, so no prefab or scene changes, and
    /// the real meshes are built into the <c>-lod0</c> bundles before that. <see cref="Dispose"/> puts every original back from a backup in
    /// <see cref="BackupFolder"/>; <see cref="RecoverLeftovers"/> does the same for a build that died with stubs on disk (the meshes are generated, git-ignored,
    /// and not to be re-meshed).
    /// </summary>
    /// <summary>What <see cref="Lod0Split.Find"/> found: the levels' LOD0 meshes (a <c>-lod0</c> bundle each) and the player's own (the common bundle).</summary>
    public sealed class Lod0Plan
    {
        public HashSet<string> level = new HashSet<string>();
        public HashSet<string> common = new HashSet<string>();
        public IEnumerable<string> All => level.Concat(common);
    }

    public sealed class Lod0Split : IDisposable
    {
        public const string BackupFolder = "Library/Lod0Backup";
        private const string Index = BackupFolder + "/index.txt";

        private readonly List<(string path, string backup)> stubbed = new List<(string, string)>();

        /// <summary>Puts back the originals of a build that did not finish; nothing to do when there are no backups.</summary>
        [InitializeOnLoadMethod]
        public static void RecoverLeftovers() => Recover("were left as stubs by a build that did not finish");

        private static void Recover(string why)
        {
            if (!File.Exists(Index))
                return;
            var lines = File.ReadAllLines(Index).Where(l => l.Contains('\t')).Select(l => l.Split('\t')).ToList();
            Debug.Log($"Lod0Split: {lines.Count} mesh assets {why}; restoring them");
            Restore(lines.Select(l => (l[0], l[1])).ToList());
        }

        private static void Restore(List<(string path, string backup)> list)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var (path, backup) in list)
                    File.Copy(backup, path, true);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            foreach (var (path, _) in list)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var bad = list.Where(l => !File.ReadAllBytes(l.path).SequenceEqual(File.ReadAllBytes(l.backup)) || AssetDatabase.LoadAssetAtPath<Mesh>(l.path) is not { } m || Lod0Meshes.IsStub(m)).ToList();
            if (bad.Count > 0)
                throw new Exception($"Lod0Split: {bad.Count} meshes could not be restored (backups kept in {BackupFolder}), first: {bad[0].path}");
            Directory.Delete(BackupFolder, true);
            Debug.Log($"Lod0Split: {list.Count} mesh assets restored");
        }

        public void Dispose()
        {
            if (stubbed.Count == 0 && !File.Exists(Index))
                return;
            stubbed.Clear();
            Recover("are stubs after the level bundles were built");
        }

        /// <summary>Replaces the meshes at <paramref name="paths"/> by stubs (backups first); <see cref="Dispose"/> puts them back.</summary>
        public void Stub(IEnumerable<string> paths)
        {
            RecoverLeftovers();
            Directory.CreateDirectory(BackupFolder);
            var list = paths.ToList();
            foreach (var path in list)
                stubbed.Add((path, Path.Combine(BackupFolder, AssetDatabase.AssetPathToGUID(path) + ".asset")));
            foreach (var (path, backup) in stubbed)
                File.Copy(path, backup, true);
            // The index comes first: from here on a died build is recoverable
            File.WriteAllLines(Index, stubbed.Select(s => s.path + "\t" + s.backup));
            foreach (var (path, _) in stubbed)
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                var stub = new Mesh { name = mesh.name + Lod0Meshes.StubSuffix };
                stub.SetVertices(new Vector3[3]);
                stub.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
                stub.bounds = mesh.bounds;
                EditorUtility.CopySerialized(stub, mesh);
                EditorUtility.SetDirty(mesh);
                Object.DestroyImmediate(stub);
            }
            AssetDatabase.SaveAssets();
            var wrong = stubbed.Count(s => !Lod0Meshes.IsStub(AssetDatabase.LoadAssetAtPath<Mesh>(s.path)) || new FileInfo(s.path).Length > 4096);
            if (wrong > 0)
                throw new Exception($"Lod0Split: {wrong} mesh assets were not turned into stubs");
        }

        /// <summary>
        /// The mesh assets (paths) that are LOD0 slot meshes only: see the class. <paramref name="closure"/> is every asset the scenes need (the bundles'),
        /// <paramref name="player"/> the assets the player carries too.
        /// </summary>
        public static Lod0Plan Find(IEnumerable<string> scenes, IEnumerable<string> closure, HashSet<string> player, ICollection<string> piecePrefabs = null, IDictionary<string, SortedSet<string>> uses = null)
        {
            var assets = closure.ToList();
            var lod0 = new HashSet<string>();
            var other = new HashSet<string>();
            var gore = new HashSet<string>();

            void Classify(Component[] components, GameObject[] roots, bool pieces = false)
            {
                var slot0 = new HashSet<Component>();
                foreach (var root in roots)
                    foreach (var group in root.GetComponentsInChildren<LODGroup>(true))
                    {
                        var lods = group.GetLODs();
                        if (lods.Length < 2)
                            continue;
                        foreach (var renderer in lods[0].renderers)
                            if (renderer != null && renderer.TryGetComponent<MeshFilter>(out var filter))
                                slot0.Add(filter);
                    }
                foreach (var component in components)
                {
                    if (component == null)
                        continue;
                    var so = new SerializedObject(component);
                    var it = so.GetIterator();
                    for (var enter = true; it.Next(enter); enter = true)
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue is not Mesh mesh)
                            continue;
                        var path = AssetDatabase.GetAssetPath(mesh);
                        if (string.IsNullOrEmpty(path))
                            continue;
                        var isLod0 = component is MeshFilter && it.propertyPath == "m_Mesh" && slot0.Contains(component);
                        if (!isLod0 && component is InstancedScatterRenderer)
                        {
                            var m = Regex.Match(it.propertyPath, @"^(prototypes\.Array\.data\[\d+\])\.levels\.Array\.data\[0\]\.parts\.Array\.data\[\d+\]\.mesh$");
                            isLod0 = m.Success && so.FindProperty(m.Groups[1].Value + ".levels.Array.size").intValue >= 2;
                        }
                        // The head pieces' prefabs (ticket 62): a MeshFilter's mesh there is a piece, built into the common bundle
                        if (pieces && !isLod0 && component is MeshFilter && it.propertyPath == "m_Mesh")
                            gore.Add(path);
                        else
                            (isLod0 ? lod0 : other).Add(path);
                    }
                }
            }

            foreach (var scene in scenes)
            {
                var opened = EditorSceneManager.OpenScene(scene, OpenSceneMode.Additive);
                var roots = opened.GetRootGameObjects();
                Classify(roots.SelectMany(r => r.GetComponentsInChildren<Component>(true)).ToArray(), roots);
                EditorSceneManager.CloseScene(opened, true);
            }
            foreach (var path in assets)
            {
                if (path.EndsWith(".prefab"))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null)
                        Classify(prefab.GetComponentsInChildren<Component>(true), new[] { prefab }, piecePrefabs != null && piecePrefabs.Contains(path));
                }
                else if (!IsPlain(path))
                {
                    // Any other asset (a ScriptableObject, a controller, ...) that points at a mesh uses it for something we cannot tell apart
                    foreach (var dependency in AssetDatabase.GetDependencies(path, false))
                        if (dependency != path && dependency.EndsWith(".asset"))
                            other.Add(dependency);
                }
            }

            long Size(IEnumerable<string> paths) => paths.Where(File.Exists).Sum(f => new FileInfo(f).Length);
            Debug.Log($"Lod0Split: {lod0.Count} meshes ({Size(lod0) / 1048576f:0.0} MB of assets) are in a LOD0 slot; also used elsewhere: {lod0.Count(other.Contains)} ({Size(lod0.Where(other.Contains)) / 1048576f:0.0} MB), " +
                      $"in the player: {lod0.Count(player.Contains)} ({Size(lod0.Where(player.Contains)) / 1048576f:0.0} MB), not a mesh .asset: {lod0.Count(p => !p.EndsWith(".asset"))}; head piece meshes: {gore.Count} ({Size(gore) / 1048576f:0.0} MB)");
            bool IsMeshAsset(string p) => p.StartsWith("Assets/") && p.EndsWith(".asset") && AssetDatabase.GetMainAssetTypeAtPath(p) == typeof(Mesh);
            // The level bundles' own (not in the player), and the player's (the common bundle: LOD0 slots of heads, weapons and pods, and the head pieces)
            var eligibleLevel = lod0.Where(p => !other.Contains(p) && !player.Contains(p) && IsMeshAsset(p)).ToList();
            var eligibleCommon = lod0.Where(p => !other.Contains(p) && player.Contains(p) && IsMeshAsset(p))
                .Concat(gore.Where(p => !other.Contains(p) && !lod0.Contains(p) && IsMeshAsset(p))).Distinct().ToList();
            // The runtime finds a real mesh by its name among the ones of the level in play and the common ones: a name two meshes share in one level
            // (or with a common mesh), or one that looks like a LOD1 mesh, stays where it is. The same name in two levels is fine (ground chunks:
            // chunk_0_0_ground in each level's folder; the level's meshes are dropped from the lookup when the next level starts)
            IEnumerable<string> ScenesOf(string p) => common(p) || uses == null || !uses.TryGetValue(p, out var set) ? Enumerable.Empty<string>() : set;
            bool common(string p) => eligibleCommon.Contains(p);
            var dropped = new List<string>();
            foreach (var group in eligibleLevel.Concat(eligibleCommon).GroupBy(p => AssetDatabase.LoadAssetAtPath<Mesh>(p).name))
            {
                var members = group.ToList();
                if (group.Key.EndsWith("_LOD1") || group.Key.EndsWith(Lod0Meshes.StubSuffix))
                    dropped.AddRange(members);
                else if (members.Count > 1)
                    foreach (var p in members)
                        if (members.Any(q => q != p && (common(p) || common(q) || uses == null || ScenesOf(p).Intersect(ScenesOf(q)).Any())))
                            dropped.Add(p);
            }
            if (dropped.Count > 0)
                Debug.LogWarning($"Lod0Split: {dropped.Count} LOD0 meshes stay where they are (name clash), first {dropped[0]}");
            var plan = new Lod0Plan { level = new HashSet<string>(eligibleLevel.Except(dropped)), common = new HashSet<string>(eligibleCommon.Except(dropped)) };
            Debug.Log($"Lod0Split: {plan.level.Count} level LOD0 meshes split off ({Size(plan.level) / 1048576f:0.0} MB of assets), {plan.common.Count} common meshes ({Size(plan.common) / 1048576f:0.0} MB of assets: " +
                      $"{plan.common.Count(gore.Contains)} head pieces); {dropped.Count} stay for their names ({Size(dropped) / 1048576f:0.0} MB)");
            return plan;
        }

        private static bool IsPlain(string path) =>
            path.EndsWith(".mat") || path.EndsWith(".png") || path.EndsWith(".jpg") || path.EndsWith(".tga") || path.EndsWith(".psd") || path.EndsWith(".exr") || path.EndsWith(".wav") ||
            path.EndsWith(".ogg") || path.EndsWith(".mp3") || path.EndsWith(".ttf") || path.EndsWith(".otf") || path.EndsWith(".shader") || path.EndsWith(".shadergraph") ||
            path.EndsWith(".glb") || path.EndsWith(".fbx") || path.EndsWith(".anim") || path.EndsWith(".mask") || path.EndsWith(".bytes") || path.EndsWith(".txt") || path.EndsWith(".json") ||
            path.EndsWith(".mixer") || path.EndsWith(".cubemap") || path.EndsWith(".hdr") || path.EndsWith(".prefab") || path.EndsWith(".unity");
    }
}
