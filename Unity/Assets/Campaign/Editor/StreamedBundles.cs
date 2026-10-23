using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SdfCampaign.Editor
{
    /// <summary>
    /// The streamed part of the web build (ticket 38): every arena after the first, as AssetBundles, so the player (Boot, the GUI, the menu
    /// backdrop, the intro comic and the first arena) is the initial download and the rest is fetched as the campaign goes there.
    /// <list type="bullet">
    /// <item>Each later scene is a scene bundle (<c>fungal</c>); a bundle holds scenes or assets, not both. The assets a scene needs are split by
    /// who uses them: those only that scene uses in <c>fungal-assets</c>, those used by several later scenes in a <c>shared-xxxx</c> bundle per
    /// distinct set of scenes. Every asset is in exactly one bundle (no implicit copies in several), and loading a scene loads the bundles that
    /// hold its assets first.</item>
    /// <item>The comics' panels (<c>Story/Resources/Comics/*</c>, in the player's Resources otherwise) are a bundle each, <c>comic-beat-fungal</c>;
    /// the folder is moved out of Resources while the player is built, so the player does not carry them too, and moved back after.</item>
    /// <item>Assets the player carries as well (used by Boot or the first arena, or in a Resources folder) cannot be shared with a bundle: they
    /// are copied into the bundle that needs them. The log lists the largest.</item>
    /// <item>The bundles are LZ4 chunk-compressed (the only compression WebGL loads), named with their content hash, and written to the
    /// build's <c>Bundles</c> folder; the list of them (<see cref="StreamManifest"/>) goes into the player as Resources/StreamedContent.</item>
    /// <item>A <c>link.xml</c> is written for the build so managed and engine code stripping keeps what the bundled scenes' components use.</item>
    /// </list>
    /// Use as <c>using var streamed = new StreamedBundles(BuildTarget.WebGL);</c> around the player build: it moves, writes and, on dispose, puts back.
    /// </summary>
    public sealed class StreamedBundles : IDisposable
    {
        public const string RawFolder = "Library/StreamedBundles";
        public const string ManifestAsset = "Assets/Campaign/Resources/StreamedContent.json";
        public const string LinkAsset = "Assets/Campaign/link.xml";
        public const string ComicsSource = "Assets/Story/Resources/Comics";
        public const string ComicsAway = "Assets/Story/StreamedComics";

        public StreamManifest Manifest { get; private set; } = new StreamManifest();
        /// <summary>Bundle file name to source path (in <see cref="RawFolder"/>) for the copy to the build folder.</summary>
        public Dictionary<string, string> Files { get; } = new Dictionary<string, string>();

        public StreamedBundles(BuildTarget target)
        {
            Restore();
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
            if (File.Exists(ManifestAsset))
                AssetDatabase.DeleteAsset(ManifestAsset);
            if (File.Exists(LinkAsset))
                AssetDatabase.DeleteAsset(LinkAsset);
            Restore();
        }

        // Puts the comics back where they belong (also after a build that crashed before it could)
        private static void Restore()
        {
            if (Directory.Exists(ComicsAway) && !Directory.Exists(ComicsSource))
            {
                var error = AssetDatabase.MoveAsset(ComicsAway, ComicsSource);
                if (!string.IsNullOrEmpty(error))
                    throw new Exception("StreamedBundles: cannot put the comics back: " + error);
            }
        }

        /// <summary>Copies the bundles to <paramref name="buildFolder"/>/Bundles (emptied first).</summary>
        public void CopyTo(string buildFolder)
        {
            var folder = Path.Combine(buildFolder, StreamedContentFolder);
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            foreach (var bundle in Manifest.bundles)
                File.Copy(Files[bundle.file], Path.Combine(folder, bundle.file));
        }

        public const string StreamedContentFolder = "Bundles";

        private static bool IsCode(string path) =>
            path.EndsWith(".cs") || path.EndsWith(".dll") || path.EndsWith(".asmdef") || path.EndsWith(".asmref") || path.EndsWith(".so") ||
            path.EndsWith(".jslib") || path.EndsWith(".hlsl") || path.EndsWith(".cginc") || path.EndsWith(".hlsl.meta") || path.EndsWith(".a") || path.Contains("/Editor/") || AssetDatabase.IsValidFolder(path);

        private void Build(BuildTarget target)
        {
            var scenes = CampaignBuildScenes.StreamedPaths().ToList();
            var playerScenes = CampaignBuildScenes.PlayerPaths().ToList();
            if (scenes.Count == 0)
                throw new Exception("StreamedBundles: no later scenes to stream");

            // What every later scene needs
            var uses = new Dictionary<string, SortedSet<string>>();
            var sceneAssets = new Dictionary<string, List<string>>();
            var codeAssets = new HashSet<string>();
            foreach (var scene in scenes)
            {
                var name = Path.GetFileNameWithoutExtension(scene).ToLowerInvariant();
                var all = AssetDatabase.GetDependencies(scene, true);
                sceneAssets[name] = all.ToList();
                foreach (var path in all)
                {
                    if (path == scene || path.EndsWith(".unity"))
                        continue;
                    if (IsCode(path))
                    {
                        if (path.EndsWith(".cs"))
                            codeAssets.Add(path);
                        continue;
                    }
                    if (!uses.TryGetValue(path, out var set))
                        uses[path] = set = new SortedSet<string>(StringComparer.Ordinal);
                    set.Add(name);
                }
            }

            // The comics: out of the player's Resources, a bundle each
            var comicBundles = new Dictionary<string, List<string>>();
            if (Directory.Exists(ComicsSource))
            {
                var error = AssetDatabase.MoveAsset(ComicsSource, ComicsAway);
                if (!string.IsNullOrEmpty(error))
                    throw new Exception("StreamedBundles: cannot move the comics out of Resources: " + error);
                foreach (var folder in Directory.GetDirectories(ComicsAway).OrderBy(f => f, StringComparer.Ordinal))
                {
                    var id = Path.GetFileName(folder);
                    var panels = Directory.GetFiles(folder).Where(f => !f.EndsWith(".meta")).Select(f => f.Replace('\\', '/')).OrderBy(f => f, StringComparer.Ordinal).ToList();
                    if (panels.Count > 0)
                        comicBundles[id] = panels;
                }
            }

            // Bundles: signature (the set of scenes that use an asset) to its assets
            var builds = new List<AssetBundleBuild>();
            var bundleScenes = new Dictionary<string, List<string>>(); // asset bundle name to the scenes (lower case) that load it
            var groups = uses.GroupBy(pair => string.Join("+", pair.Value));
            foreach (var group in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var sceneNames = group.First().Value.ToList();
                var name = sceneNames.Count == 1 ? sceneNames[0] + "-assets" : "shared-" + ShortHash(group.Key);
                builds.Add(new AssetBundleBuild { assetBundleName = name, assetNames = group.Select(p => p.Key).OrderBy(p => p, StringComparer.Ordinal).ToArray() });
                bundleScenes[name] = sceneNames;
            }
            foreach (var scene in scenes)
            {
                var name = Path.GetFileNameWithoutExtension(scene).ToLowerInvariant();
                builds.Add(new AssetBundleBuild { assetBundleName = name, assetNames = new[] { scene } });
            }
            foreach (var pair in comicBundles)
                builds.Add(new AssetBundleBuild { assetBundleName = "comic-" + pair.Key.ToLowerInvariant(), assetNames = pair.Value.ToArray() });

            WriteLink(scenes, uses.Keys, codeAssets);
            ReportDuplicates(uses, playerScenes);

            var raw = Path.Combine(RawFolder, target.ToString());
            Directory.CreateDirectory(raw);
            foreach (var old in Directory.GetFiles(raw))
                File.Delete(old);
            Debug.Log($"StreamedBundles: building {builds.Count} bundles ({uses.Count} assets, {scenes.Count} scenes, {comicBundles.Count} comics) for {target}");
            var built = BuildPipeline.BuildAssetBundles(raw, builds.ToArray(), BuildAssetBundleOptions.ChunkBasedCompression, target);
            if (built == null)
                throw new Exception("StreamedBundles: BuildAssetBundles failed (see the errors above)");

            // The manifest: files named by content, and what each scene and comic loads (shared first, the scene last)
            var bundles = new List<StreamedBundle>();
            foreach (var build in builds)
            {
                var source = Path.Combine(raw, build.assetBundleName);
                var hash = built.GetAssetBundleHash(build.assetBundleName).ToString();
                var comic = build.assetBundleName.StartsWith("comic-") ? comicBundles.Keys.First(id => "comic-" + id.ToLowerInvariant() == build.assetBundleName) : "";
                var file = $"{build.assetBundleName}-{hash.Substring(0, 12)}.bundle";
                bundles.Add(new StreamedBundle { name = build.assetBundleName, file = file, hash = hash, size = new FileInfo(source).Length, comic = comic });
                Files[file] = source;
            }
            var entries = new List<StreamedEntry>();
            var definition = CampaignDefinition.Default();
            foreach (var scene in scenes)
            {
                var name = Path.GetFileNameWithoutExtension(scene).ToLowerInvariant();
                var list = bundleScenes.Where(p => p.Value.Contains(name)).Select(p => p.Key).OrderBy(n => n.StartsWith("shared-") ? 0 : 1).ThenBy(n => n, StringComparer.Ordinal).ToList();
                list.Add(name);
                // The beat that plays over this scene is fetched with it
                foreach (var step in definition.Steps)
                    if (string.Equals(step.scene, Path.GetFileNameWithoutExtension(scene), StringComparison.OrdinalIgnoreCase) && comicBundles.ContainsKey(step.id))
                        list.Add("comic-" + step.id.ToLowerInvariant());
                entries.Add(new StreamedEntry { key = StreamManifest.SceneKey(Path.GetFileNameWithoutExtension(scene)), bundles = list.Distinct().ToArray() });
            }
            foreach (var id in comicBundles.Keys)
                entries.Add(new StreamedEntry { key = StreamManifest.ComicKey(id), bundles = new[] { "comic-" + id.ToLowerInvariant() } });
            Manifest = new StreamManifest { bundles = bundles.ToArray(), entries = entries.ToArray() };
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
            foreach (var entry in Manifest.entries.Where(e => e.key.StartsWith("scene:")))
                text.AppendLine($"  {entry.key}: {string.Join(", ", entry.bundles)} = {Manifest.Remaining(entry.key, null) / 1048576f:0.0} MB");
            Debug.Log(text.ToString());
        }

        // Assets that are in the player (Boot's and the first arena's closure, everything in Resources) as well as in a bundle: a copy each
        private static void ReportDuplicates(Dictionary<string, SortedSet<string>> uses, List<string> playerScenes)
        {
            var player = new HashSet<string>(AssetDatabase.GetDependencies(playerScenes.ToArray(), true));
            var resources = AssetDatabase.FindAssets("", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).Where(p => p.Contains("/Resources/") && !AssetDatabase.IsValidFolder(p)).ToArray();
            foreach (var path in AssetDatabase.GetDependencies(resources, true))
                player.Add(path);
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

        private static string ShortHash(string text)
        {
            using var sha = SHA1.Create();
            return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text)).Take(4).Select(b => b.ToString("x2")));
        }
    }
}
