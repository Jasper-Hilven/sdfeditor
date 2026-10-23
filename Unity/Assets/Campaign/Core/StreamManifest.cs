using System;
using System.Collections.Generic;

namespace SdfCampaign
{
    /// <summary>One AssetBundle of the web build's streamed content (ticket 38): a file next to the player, its content hash (the cache's version) and size.</summary>
    [Serializable]
    public sealed class StreamedBundle
    {
        public string name = "";
        /// <summary>The file in the Bundles folder, with the content hash in its name so a CDN or browser cache never serves a stale one.</summary>
        public string file = "";
        public string hash = "";
        public long size;
        /// <summary>The comic whose panels this bundle holds ("beat-fungal"), or empty.</summary>
        public string comic = "";
    }

    /// <summary>What a key (a scene or a comic) needs loaded, in the order to load it: shared bundles first, the scene bundle last.</summary>
    [Serializable]
    public sealed class StreamedEntry
    {
        public string key = "";
        public string[] bundles = new string[0];
    }

    /// <summary>
    /// The list of streamed bundles and what each scene and comic needs, written by the web build into <c>Resources/StreamedContent</c>
    /// and read by the player (ticket 38). Without it (editor, desktop builds, a build that is not streamed) nothing is streamed.
    /// </summary>
    [Serializable]
    public sealed class StreamManifest
    {
        public const string ResourceName = "StreamedContent";

        public StreamedBundle[] bundles = new StreamedBundle[0];
        public StreamedEntry[] entries = new StreamedEntry[0];

        public static string SceneKey(string scene) => "scene:" + scene;

        public static string ComicKey(string comic) => "comic:" + comic;

        public static StreamManifest Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new StreamManifest();
            var parsed = UnityEngine.JsonUtility.FromJson<StreamManifest>(json);
            return parsed ?? new StreamManifest();
        }

        public string ToJson() => UnityEngine.JsonUtility.ToJson(this, true);

        public bool IsEmpty => bundles.Length == 0;

        public bool Has(string key) => Find(key) != null;

        private StreamedEntry Find(string key)
        {
            foreach (var entry in entries)
                if (entry.key == key)
                    return entry;
            return null;
        }

        public StreamedBundle Bundle(string name)
        {
            foreach (var bundle in bundles)
                if (bundle.name == name)
                    return bundle;
            return null;
        }

        /// <summary>The bundles a key needs, in load order; empty for an unknown key.</summary>
        public List<StreamedBundle> Bundles(string key)
        {
            var list = new List<StreamedBundle>();
            var entry = Find(key);
            if (entry == null)
                return list;
            foreach (var name in entry.bundles)
            {
                var bundle = Bundle(name);
                if (bundle != null)
                    list.Add(bundle);
            }
            return list;
        }

        /// <summary>The bytes of the bundles of <paramref name="key"/> that <paramref name="have"/> does not say are in yet.</summary>
        public long Remaining(string key, Func<string, bool> have)
        {
            long total = 0;
            foreach (var bundle in Bundles(key))
                if (have == null || !have(bundle.name))
                    total += bundle.size;
            return total;
        }
    }

    /// <summary>Which streamed content to fetch ahead while the player is busy in a battle.</summary>
    public static class StreamPlan
    {
        /// <summary>
        /// The keys of the scenes and comics of the steps after <paramref name="currentId"/>, up to and including the next battle (the
        /// arena that follows and the comic that goes before it); after the last battle, the comics of the ending. Only keys the manifest has.
        /// </summary>
        public static List<string> Upcoming(CampaignDefinition definition, string currentId, StreamManifest manifest)
        {
            var keys = new List<string>();
            var from = definition.IndexOf(currentId);
            if (from < 0)
                return keys;
            for (var i = from + 1; i < definition.Steps.Count; i++)
            {
                var step = definition.Steps[i];
                Add(keys, manifest, StreamManifest.SceneKey(step.scene));
                Add(keys, manifest, StreamManifest.ComicKey(step.id));
                if (step.IsBattle)
                    break;
            }
            return keys;
        }

        private static void Add(List<string> keys, StreamManifest manifest, string key)
        {
            if (manifest.Has(key) && !keys.Contains(key))
                keys.Add(key);
        }
    }
}
