using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SdfCampaign.Editor
{
    /// <summary>
    /// The streamed part of the web build (ticket 38): every level, the Meadow too, as AssetBundles (ticket 52), so the player (Boot and the GUI)
    /// is the initial download and the levels are fetched as the game goes there; the first one behind the Loading screen.
    /// <list type="bullet">
    /// <item>Each level is a scene bundle (<c>fungal</c>); a bundle holds scenes or assets, not both. The assets a scene needs are split by
    /// who uses them: those only that scene uses in <c>fungal-assets</c>, those used by several levels (those every level uses, the soldier, heads, weapons and effects, are the one all-levels shared bundle) in a <c>shared-xxxx</c> bundle per
    /// distinct set of scenes. Every asset is in exactly one bundle (no implicit copies in several), and loading a scene loads the bundles that
    /// hold its assets first.</item>
    /// <item>Assets the player carries as well (used by Boot or in a Resources folder) cannot be shared with a bundle: they
    /// are copied into the bundle that needs them. The log lists the largest.</item>
    /// <item>The bundles are LZ4 chunk-compressed (the only compression WebGL loads), named with their content hash, and written to the
    /// build's <c>StreamingAssets/Bundles</c> folder; the list of them (<see cref="StreamManifest"/>) goes into the player as Resources/StreamedContent.</item>
    /// <item>A <c>link.xml</c> is written for the build so managed and engine code stripping keeps what the bundled scenes' components use.</item>
    /// </list>
    /// Use as <c>using var streamed = new StreamedBundles(BuildTarget.WebGL);</c> around the player build: it moves, writes and, on dispose, puts back.
    /// </summary>
    public sealed class StreamedBundles : IDisposable
    {
        public const string RawFolder = "Library/StreamedBundles";
        public const string CommonBundle = "common-lod0";
        public const string ManifestAsset = "Assets/Campaign/Resources/StreamedContent.json";
        public const string LinkAsset = "Assets/Campaign/link.xml";

        public StreamManifest Manifest { get; private set; } = new StreamManifest();
        /// <summary>Bundle file name to source path (in <see cref="RawFolder"/>) for the copy to the build folder.</summary>
        public Dictionary<string, string> Files { get; } = new Dictionary<string, string>();

        // Ticket 62: the stubs stay on disk through the player build too (the player carries heads, weapons, pods and head pieces as stubs); Dispose puts every original back
        private Lod0Split split;
        // Ticket 62: the level scenes without their GUI copy, while the bundles are planned and built
        private SceneGuiStrip strip;

        public StreamedBundles(BuildTarget target)
        {
            try
            {
                Build(target);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            // The originals first: the manifest and link files below are only deleted
            split?.Dispose();
            split = null;
            strip?.Dispose();
            strip = null;
            if (File.Exists(ManifestAsset))
                AssetDatabase.DeleteAsset(ManifestAsset);
            if (File.Exists(LinkAsset))
                AssetDatabase.DeleteAsset(LinkAsset);
        }

        /// <summary>Copies the bundles to <paramref name="buildFolder"/>/StreamingAssets/Bundles (emptied first).</summary>
        public void CopyTo(string buildFolder)
        {
            var folder = Path.Combine(buildFolder, StreamedContentFolder);
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
            // The bundles were next to index.html before; CrazyGames serves only StreamingAssets of an upload
            var old = Path.Combine(buildFolder, "Bundles");
            if (Directory.Exists(old))
                Directory.Delete(old, true);
            Directory.CreateDirectory(folder);
            foreach (var bundle in Manifest.bundles)
                File.Copy(Files[bundle.file], Path.Combine(folder, bundle.file));
        }

        public const string StreamedContentFolder = "StreamingAssets/" + ContentStreamer.Folder;

        private static bool IsCode(string path) =>
            path.EndsWith(".cs") || path.EndsWith(".dll") || path.EndsWith(".asmdef") || path.EndsWith(".asmref") || path.EndsWith(".so") ||
            path.EndsWith(".jslib") || path.EndsWith(".hlsl") || path.EndsWith(".cginc") || path.EndsWith(".hlsl.meta") || path.EndsWith(".a") || path.Contains("/Editor/") || AssetDatabase.IsValidFolder(path);

        private void Build(BuildTarget target)
        {
            var scenes = LevelBuildScenes.StreamedPaths().ToList();
            var playerScenes = LevelBuildScenes.PlayerPaths().ToList();
            if (scenes.Count == 0)
                throw new Exception("StreamedBundles: no levels to stream");

            // The GUI is in the player (Boot): the bundles of the levels do not carry a second copy (restored below, once they are built)
            strip = new SceneGuiStrip();
            strip.Strip(scenes);

            // What every level needs
            var uses = new Dictionary<string, SortedSet<string>>();
            var assetsByScene = new Dictionary<string, IEnumerable<string>>();
            var scenePaths = new Dictionary<string, string>();
            var realNames = new Dictionary<string, string>();
            var codeAssets = new HashSet<string>();
            foreach (var scene in scenes)
            {
                var real = Path.GetFileNameWithoutExtension(scene);
                var name = real.ToLowerInvariant();
                scenePaths[name] = scene;
                realNames[name] = real;
                var needed = new List<string>();
                foreach (var path in AssetDatabase.GetDependencies(scene, true))
                {
                    if (path == scene || path.EndsWith(".unity"))
                        continue;
                    if (IsCode(path))
                    {
                        if (path.EndsWith(".cs"))
                            codeAssets.Add(path);
                        continue;
                    }
                    needed.Add(path);
                    if (!uses.TryGetValue(path, out var set))
                        uses[path] = set = new SortedSet<string>(StringComparer.Ordinal);
                    set.Add(name);
                }
                assetsByScene[name] = needed;
            }

            // Bundles: the layout is StreamLayout's (tested); a scene bundle holds the scene file
            StreamLayout.Plan(assetsByScene, out var planned, out var plannedEntries, realNames);
            var builds = planned.Select(b => new AssetBundleBuild
            {
                assetBundleName = b.name,
                assetNames = b.isScene ? new[] { scenePaths[b.name] } : b.assets,
            }).ToList();

            WriteLink(scenes, uses.Keys, codeAssets);
            ReportDuplicates(uses, playerScenes);

            // Ticket 60: the LOD0 meshes only the finest level of a LOD group uses are built first, as they are, into the -lod0 bundles;
            // the level bundles are then built with stubs in their place (put back at once, whatever happens)
            Lod0Split.RecoverLeftovers();
            var playerAssets = PlayerAssets(playerScenes);
            var plan = Lod0Split.Find(scenes.Concat(playerScenes), uses.Keys.Concat(playerAssets).Distinct(), playerAssets, PiecePrefabs(), uses);
            StreamLayout.PlanLod0(planned, plannedEntries, plan.level, out var lod0Planned, out var lod0Entries);
            // The player's own LOD0 meshes and the head pieces: one common bundle (ticket 62), listed under its own key
            if (plan.common.Count > 0)
            {
                lod0Planned.Add(new PlannedBundle { name = CommonBundle, assets = plan.common.OrderBy(p => p, StringComparer.Ordinal).ToArray() });
                lod0Entries.Add(new StreamedEntry { key = StreamManifest.CommonKey, bundles = new[] { CommonBundle } });
            }
            // The battle music's layers 2 to 4 (taken out of the scenes above): one bundle per distinct set of clips, listed under each level's music key
            foreach (var group in strip.Music.GroupBy(m => string.Join("|", m.Value)))
            {
                var first = group.First().Value;
                var set = first[0].Contains("orchestra") ? "orchestra" : first[0].Contains("electronic") ? "electronic" : "set" + lod0Planned.Count;
                var name = "music-" + set;
                lod0Planned.Add(new PlannedBundle { name = name, assets = first.ToArray() });
                foreach (var scenePath in group.Select(m => m.Key))
                    lod0Entries.Add(new StreamedEntry { key = StreamManifest.MusicKey(realNames[Path.GetFileNameWithoutExtension(scenePath).ToLowerInvariant()]), bundles = new[] { name } });
            }
            Debug.Log($"StreamedBundles: {plan.level.Count} level LOD0 meshes go to {lod0Planned.Count(b => b.name.EndsWith("-lod0") && b.name != CommonBundle)} -lod0 bundles, {plan.common.Count} common meshes to {CommonBundle}");

            var raw = Path.Combine(RawFolder, target.ToString());
            var rawLod0 = raw + "-lod0";
            foreach (var folder in new[] { raw, rawLod0 })
            {
                Directory.CreateDirectory(folder);
                foreach (var old in Directory.GetFiles(folder))
                    File.Delete(old);
            }
            var lod0Builds = lod0Planned.Select(b => new AssetBundleBuild { assetBundleName = b.name, assetNames = b.assets }).ToList();
            var lod0Hashes = new Dictionary<string, string>();
            if (lod0Builds.Count > 0)
            {
                var builtLod0 = BuildPipeline.BuildAssetBundles(rawLod0, lod0Builds.ToArray(), BuildAssetBundleOptions.ChunkBasedCompression, target);
                if (builtLod0 == null)
                    throw new Exception("StreamedBundles: BuildAssetBundles failed for the LOD0 bundles (see the errors above)");
                // The manifest object is gone after the next build
                foreach (var build in lod0Builds)
                    lod0Hashes[build.assetBundleName] = builtLod0.GetAssetBundleHash(build.assetBundleName).ToString();
            }
            Debug.Log($"StreamedBundles: building {builds.Count} bundles ({uses.Count} assets, {scenes.Count} scenes) for {target}");
            AssetBundleManifest built;
            // The stubs stay until Dispose (after the player build)
            split = new Lod0Split();
            split.Stub(plan.All);
            built = BuildPipeline.BuildAssetBundles(raw, builds.ToArray(), BuildAssetBundleOptions.ChunkBasedCompression, target);
            strip.Dispose();
            strip = null;
            if (built == null)
                throw new Exception("StreamedBundles: BuildAssetBundles failed (see the errors above)");

            // The manifest: files named by content, and what each scene loads (shared first, the scene last)
            var bundles = new List<StreamedBundle>();
            foreach (var build in builds)
            {
                var source = Path.Combine(raw, build.assetBundleName);
                var hash = built.GetAssetBundleHash(build.assetBundleName).ToString();
                var file = $"{build.assetBundleName}-{hash.Substring(0, 12)}.bundle";
                bundles.Add(new StreamedBundle { name = build.assetBundleName, file = file, hash = hash, size = new FileInfo(source).Length });
                Files[file] = source;
            }
            foreach (var build in lod0Builds)
            {
                var source = Path.Combine(rawLod0, build.assetBundleName);
                var hash = lod0Hashes[build.assetBundleName];
                var file = $"{build.assetBundleName}-{hash.Substring(0, 12)}.bundle";
                bundles.Add(new StreamedBundle { name = build.assetBundleName, file = file, hash = hash, size = new FileInfo(source).Length });
                Files[file] = source;
            }
            Manifest = new StreamManifest { bundles = bundles.ToArray(), entries = plannedEntries.Concat(lod0Entries).ToArray() };
            // The key of a scene is its real name ("Fungal"); the Resources manifest carries it
            File.WriteAllText(ManifestAsset, Manifest.ToJson());
            AssetDatabase.ImportAsset(ManifestAsset, ImportAssetOptions.ForceSynchronousImport);
            Report();
        }

        private void Report()
        {
            var text = new StringBuilder("StreamedBundles: bundles\n");
            foreach (var bundle in Manifest.bundles.OrderByDescending(b => b.size))
                text.AppendLine($"  {bundle.size / 1048576f,7:0.0} MB  {bundle.name}");
            text.AppendLine($"  {Manifest.bundles.Sum(b => b.size) / 1048576f,7:0.0} MB  total ({Manifest.bundles.Length} files)");
            foreach (var entry in Manifest.entries.Where(e => e.key.StartsWith("scene:") || e.key.StartsWith("lod0:") || e.key == StreamManifest.CommonKey || e.key.StartsWith("music:")))
                text.AppendLine($"  {entry.key}: {string.Join(", ", entry.bundles)} = {Manifest.Remaining(entry.key, null) / 1048576f:0.0} MB");
            Debug.Log(text.ToString());
        }

        // Assets that are in the player (Boot's closure, everything in Resources) as well as in a bundle: a copy each
        private static HashSet<string> PlayerAssets(List<string> playerScenes)
        {
            var player = new HashSet<string>(AssetDatabase.GetDependencies(playerScenes.ToArray(), true));
            var resources = AssetDatabase.FindAssets("", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).Where(p => p.Contains("/Resources/") && !AssetDatabase.IsValidFolder(p)).ToArray();
            foreach (var path in AssetDatabase.GetDependencies(resources, true))
                player.Add(path);
            return player;
        }

        // The head pieces' prefabs (the registry's), whose meshes the common bundle takes
        private static HashSet<string> PiecePrefabs()
        {
            var set = new HashSet<string>();
            var models = AssetDatabase.LoadAssetAtPath<SdfHeads.MonsterHeadModels>("Assets/Heads/Generated/Resources/MonsterHeadModels.asset");
            if (models != null)
                foreach (var entry in models.entries)
                    foreach (var piece in entry.pieces ?? new GameObject[0])
                        if (piece != null)
                            set.Add(AssetDatabase.GetAssetPath(piece));
            return set;
        }

        private static void ReportDuplicates(Dictionary<string, SortedSet<string>> uses, List<string> playerScenes)
        {
            var player = PlayerAssets(playerScenes);
            var dup = uses.Keys.Where(player.Contains).Select(p => (path: p, size: File.Exists(p) ? new FileInfo(p).Length : 0)).OrderByDescending(d => d.size).ToList();
            var text = new StringBuilder($"StreamedBundles: {dup.Count} assets are in the player and in a bundle too ({dup.Sum(d => d.size) / 1048576f:0.0} MB of source files); largest:\n");
            foreach (var d in dup.Take(25))
                text.AppendLine($"  {d.size / 1048576f,6:0.00} MB  {d.path}");
            Debug.Log(text.ToString());
        }

        // Managed code stripping and engine code stripping only see the player's scenes; the bundled scenes' types are kept by a link.xml
        private static void WriteLink(List<string> scenes, IEnumerable<string> assets, HashSet<string> code)
        {
            var types = new Dictionary<string, SortedSet<string>>(); // assembly to type full names
            var modules = new SortedSet<string>(StringComparer.Ordinal);
            void AddAssembly(Type type)
            {
                var assembly = type.Assembly.GetName().Name;
                if (assembly.StartsWith("UnityEngine.") && assembly.EndsWith("Module"))
                    modules.Add(assembly);
            }
            foreach (var path in code)
            {
                var type = AssetDatabase.LoadAssetAtPath<MonoScript>(path)?.GetClass();
                if (type == null || type.IsGenericType)
                    continue;
                if (!types.TryGetValue(type.Assembly.GetName().Name, out var set))
                    types[type.Assembly.GetName().Name] = set = new SortedSet<string>(StringComparer.Ordinal);
                set.Add(type.FullName.Replace('+', '/'));
                AddAssembly(type);
                for (var b = type.BaseType; b != null; b = b.BaseType)
                    AddAssembly(b);
            }
            foreach (var path in assets)
            {
                var main = AssetDatabase.GetMainAssetTypeAtPath(path);
                if (main != null)
                    AddAssembly(main);
                if (!path.EndsWith(".prefab"))
                    continue;
                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (obj != null)
                        AddAssembly(obj.GetType());
            }
            foreach (var path in scenes)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                foreach (var obj in Resources.FindObjectsOfTypeAll<Component>())
                    if (obj != null && obj.gameObject.scene == scene)
                        AddAssembly(obj.GetType());
                EditorSceneManager.CloseScene(scene, true);
            }
            var xml = new StringBuilder("<linker>\n");
            foreach (var pair in types.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                xml.AppendLine($"  <assembly fullname=\"{pair.Key}\">");
                foreach (var type in pair.Value)
                    xml.AppendLine($"    <type fullname=\"{type}\" preserve=\"all\"/>");
                xml.AppendLine("  </assembly>");
            }
            foreach (var module in modules)
                xml.AppendLine($"  <assembly fullname=\"{module}\" preserve=\"all\"/>");
            xml.AppendLine("</linker>");
            File.WriteAllText(LinkAsset, xml.ToString());
            AssetDatabase.ImportAsset(LinkAsset, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"StreamedBundles: link.xml keeps {types.Sum(p => p.Value.Count)} types of {types.Count} assemblies and {modules.Count} engine modules ({string.Join(", ", modules)})");
        }
    }
}
