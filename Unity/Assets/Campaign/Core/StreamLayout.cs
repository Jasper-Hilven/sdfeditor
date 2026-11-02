using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SdfCampaign
{
    /// <summary>One bundle to build: its name and the assets (or the one scene) it holds.</summary>
    public sealed class PlannedBundle
    {
        public string name = "";
        public string[] assets = new string[0];
        public bool isScene;
    }

    /// <summary>
    /// How the web build splits the levels into bundles (tickets 38, 52), as plain data so it is tested without a build. Every level is a scene
    /// bundle plus the assets only it uses (<c>fungal-assets</c>); an asset several levels use goes in a <c>shared-xxxx</c> bundle per distinct
    /// set of levels (the one every level uses, the soldier, heads, weapons and effects, is the all-levels shared bundle, the "common"
    /// bundle). Every asset is in exactly one bundle. Only Boot is a scene of the player.
    /// </summary>
    public static class StreamLayout
    {
        /// <summary>The one scene the web player carries.</summary>
        public const string PlayerScene = "Boot";

        public static bool IsPlayerScene(string scene) => scene == PlayerScene;

        /// <summary>
        /// The bundles for <paramref name="assetsByScene"/> (scene name, any case, to the assets it needs, scene files and code excluded by the
        /// caller) and the entries (key <c>scene:Name</c>, <paramref name="sceneNames"/> maps the lower-case name to the real one) in load order:
        /// the shared bundles, then the level's own assets, then the scene.
        /// </summary>
        public static void Plan(IDictionary<string, IEnumerable<string>> assetsByScene, out List<PlannedBundle> bundles, out List<StreamedEntry> entries, IDictionary<string, string> sceneNames = null)
        {
            var uses = new Dictionary<string, SortedSet<string>>();
            foreach (var pair in assetsByScene)
                foreach (var path in pair.Value)
                {
                    if (!uses.TryGetValue(path, out var set))
                        uses[path] = set = new SortedSet<string>(StringComparer.Ordinal);
                    set.Add(pair.Key);
                }
            bundles = new List<PlannedBundle>();
            var bundleScenes = new Dictionary<string, List<string>>();
            foreach (var group in uses.GroupBy(p => string.Join("+", p.Value)).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var scenes = group.First().Value.ToList();
                var name = scenes.Count == 1 ? scenes[0] + "-assets" : "shared-" + ShortHash(group.Key);
                bundles.Add(new PlannedBundle { name = name, assets = group.Select(p => p.Key).OrderBy(p => p, StringComparer.Ordinal).ToArray() });
                bundleScenes[name] = scenes;
            }
            foreach (var scene in assetsByScene.Keys)
                bundles.Add(new PlannedBundle { name = scene, assets = new[] { scene }, isScene = true });
            entries = new List<StreamedEntry>();
            foreach (var scene in assetsByScene.Keys)
            {
                var list = bundleScenes.Where(p => p.Value.Contains(scene)).Select(p => p.Key)
                    .OrderBy(n => n.StartsWith("shared-") ? 0 : 1).ThenBy(n => n, StringComparer.Ordinal).ToList();
                list.Add(scene);
                var real = sceneNames != null && sceneNames.TryGetValue(scene, out var r) ? r : scene;
                entries.Add(new StreamedEntry { key = StreamManifest.SceneKey(real), bundles = list.Distinct().ToArray() });
            }
        }

        private static string ShortHash(string text)
        {
            using var sha = SHA1.Create();
            return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text)).Take(4).Select(b => b.ToString("x2")));
        }
    }
}
