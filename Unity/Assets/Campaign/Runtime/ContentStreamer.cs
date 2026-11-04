using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace SdfCampaign
{
    /// <summary>A fetch of a key's bundles under way: poll <see cref="Done"/>, then <see cref="Failed"/>.</summary>
    public sealed class StreamRequest
    {
        public bool Done;
        public bool Failed;
        public string Error = "";
        /// <summary>0 to 1 over the bytes of all the bundles asked for.</summary>
        public float Progress;
    }

    /// <summary>
    /// The web build's streamed content (ticket 38): every level (ticket 52) is an AssetBundle in a Bundles folder
    /// next to the player (relative URLs, so the upload is one folder), listed in <see cref="StreamManifest"/>. This downloads them
    /// (UnityWebRequestAssetBundle, cached by Unity's Caching under the content hash), keeps the ones the current scene needs, and fetches the next
    /// level in the background while the player is in a battle. Off (everything false, nothing to do) in the editor and desktop builds and in any
    /// build without the manifest, where the scenes are in the player as before.
    /// </summary>
    public sealed class ContentStreamer : MonoBehaviour
    {
        public const string Folder = "Bundles";
        /// <summary>
        /// A background fetch waits this long after it is asked for. The next level is asked for when the match ends (the end card and,
        /// later, the ad break between levels download it; the next Loading screen covers what is left), so there is nothing to wait for.
        /// </summary>
        public const float PrefetchDelay = 0;
        /// <summary>A download that moves no byte for this long fails (the Loading screen then offers a retry).</summary>
        public const float StallSeconds = 40;

        /// <summary>True while the match in play fetches its own LOD0 bundles (ticket 60): the background fetch of the next level waits.</summary>
        public static bool HoldPrefetch;
        /// <summary>True from a level going live until its LOD0 swap is done: the next level's LOD0 bundles wait, its base bundles do not.</summary>
        public static bool Lod0Pending;

        /// <summary>For checks: stream outside a WebGL player too.</summary>
        public static bool ForceEnabled;
        /// <summary>For checks: the folder URL of the bundles (no trailing slash) instead of the page's.</summary>
        public static string BaseUrl;

        private sealed class State
        {
            public AssetBundle bundle;
            public bool failed;
            public string error = "";
            public float progress;
            public long size;
            /// <summary>The bundle was opened, its assets taken and its file data let go (<see cref="Consume"/>); the assets stay loaded.</summary>
            public bool consumed;
            public bool Have => bundle != null || consumed;
        }

        private static StreamManifest manifest;
        private static ContentStreamer instance;
        private static readonly Dictionary<string, State> states = new Dictionary<string, State>();
        private static readonly List<string> prefetchKeys = new List<string>();
        private static readonly Queue<StreamedBundle> prefetchQueue = new Queue<StreamedBundle>();
        private static bool prefetching;

        public static StreamManifest Manifest
        {
            get
            {
                if (manifest == null)
                {
                    var text = Resources.Load<TextAsset>(StreamManifest.ResourceName);
                    manifest = StreamManifest.Parse(text != null ? text.text : "");
                }
                return manifest;
            }
        }

        /// <summary>Whether this player streams: a WebGL player built with the manifest.</summary>
        public static bool Enabled => (Application.platform == RuntimePlatform.WebGLPlayer || ForceEnabled) && !Manifest.IsEmpty;

        /// <summary>Whether a scene can be loaded, from the player or (once fetched) from its bundle, or can be fetched.</summary>
        public static bool CanLoad(string scene) =>
            Application.CanStreamedLevelBeLoaded(scene) || Enabled && Manifest.Has(StreamManifest.SceneKey(scene));

        /// <summary>Whether a key is streamed and not all in yet.</summary>
        public static bool Needs(string key) => Enabled && Manifest.Has(key) && !IsReady(key);

        public static bool IsReady(string key)
        {
            foreach (var bundle in Manifest.Bundles(key))
                if (!states.TryGetValue(bundle.name, out var state) || !state.Have)
                    return false;
            return true;
        }

        /// <summary>The bundles of a fetched key that are still open (not <see cref="Consume"/>d).</summary>
        public static List<AssetBundle> Open(string key)
        {
            var list = new List<AssetBundle>();
            foreach (var bundle in Manifest.Bundles(key))
                if (states.TryGetValue(bundle.name, out var state) && state.bundle != null)
                    list.Add(state.bundle);
            return list;
        }

        /// <summary>
        /// Lets go of the file data of a key's bundles once their assets are loaded (ticket 60: the LOD0 meshes), keeping the loaded assets; the
        /// bundles count as fetched until <see cref="Release"/>, which frees the assets with the scene's.
        /// </summary>
        public static void Consume(string key)
        {
            foreach (var bundle in Manifest.Bundles(key))
                if (states.TryGetValue(bundle.name, out var state) && state.bundle != null)
                {
                    state.bundle.Unload(false);
                    state.bundle = null;
                    state.consumed = true;
                }
        }

        /// <summary>Fetches everything a key needs; a failed bundle fails the request, and asking again retries it.</summary>
        public static StreamRequest Fetch(string key)
        {
            var request = new StreamRequest();
            if (!Enabled || !Manifest.Has(key))
            {
                request.Progress = 1;
                request.Done = true;
                return request;
            }
            var bundles = Manifest.Bundles(key);
            foreach (var bundle in bundles)
                if (states.TryGetValue(bundle.name, out var state) && state.failed)
                    states.Remove(bundle.name);
            Host().StartCoroutine(Watch(request, bundles));
            return request;
        }

        /// <summary>Fetches keys in the background, one bundle at a time; failures are only logged. GameLoop asks for the next level when a match ends.</summary>
        public static void Prefetch(IEnumerable<string> keys)
        {
            if (!Enabled)
                return;
            // Only the level's own bundles: its LOD0 meshes come after its match starts (Lod0Streamer, ticket 60), as for every level
            var list = new List<string>(keys);
            foreach (var key in list)
            {
                if (!Manifest.Has(key) || IsReady(key))
                    continue;
                if (!prefetchKeys.Contains(key))
                    prefetchKeys.Add(key);
                foreach (var bundle in Manifest.Bundles(key))
                    if (!states.ContainsKey(bundle.name) && !prefetchQueue.Contains(bundle))
                        prefetchQueue.Enqueue(bundle);
            }
            if (!prefetching && prefetchQueue.Count > 0)
                Host().StartCoroutine(PrefetchWorker());
        }

        /// <summary>
        /// Lets go of the bundles the loaded scene (<paramref name="keepKey"/>, empty for one in the player) and the prefetch do not need, with their
        /// assets. Called with the new scene live, so nothing in play uses them.
        /// </summary>
        public static void Release(string keepKey)
        {
            if (!Enabled)
                return;
            var keep = new HashSet<string>();
            var keepLod0 = !string.IsNullOrEmpty(keepKey) && keepKey.StartsWith("scene:") ? StreamManifest.Lod0Key(keepKey.Substring("scene:".Length)) : "";
            prefetchKeys.Remove(keepKey);
            prefetchKeys.Remove(keepLod0);
            var keys = new List<string>(prefetchKeys) { StreamManifest.CommonKey };
            if (!string.IsNullOrEmpty(keepKey))
            {
                keys.Add(keepKey);
                keys.Add(keepLod0);
                if (keepKey.StartsWith("scene:"))
                    keys.Add(StreamManifest.MusicKey(keepKey.Substring("scene:".Length)));
            }
            foreach (var key in keys)
                foreach (var bundle in Manifest.Bundles(key))
                    keep.Add(bundle.name);
            var drop = new List<string>();
            foreach (var pair in states)
                if (pair.Value.Have && !keep.Contains(pair.Key))
                    drop.Add(pair.Key);
            foreach (var name in drop)
            {
                // A consumed bundle has no file data left; its meshes go with the last scene using them (UnloadUnusedAssets)
                if (states[name].bundle != null)
                    states[name].bundle.Unload(true);
                states.Remove(name);
                Debug.Log($"ContentStreamer: released {name}");
            }
        }

        private static ContentStreamer Host()
        {
            if (instance == null)
            {
                var go = new GameObject("Content Streamer");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<ContentStreamer>();
            }
            return instance;
        }

        private static string Url(StreamedBundle bundle)
        {
            var baseUrl = BaseUrl;
            // StreamingAssets, wherever the host serves it: next to index.html on itch.io or localhost, the folder CrazyGames keeps from the
            // upload (its own page loads the game, and it serves no other folder of it)
            if (string.IsNullOrEmpty(baseUrl))
                baseUrl = Application.streamingAssetsPath.TrimEnd('/') + "/" + Folder;
            return baseUrl + "/" + bundle.file;
        }

        private static IEnumerator Watch(StreamRequest request, List<StreamedBundle> bundles)
        {
            long total = 0;
            foreach (var bundle in bundles)
            {
                total += bundle.size;
                if (!states.ContainsKey(bundle.name))
                    Host().StartCoroutine(Download(bundle));
            }
            while (true)
            {
                var done = 0;
                double bytes = 0;
                foreach (var bundle in bundles)
                {
                    if (!states.TryGetValue(bundle.name, out var state))
                        continue;
                    if (state.failed)
                    {
                        request.Failed = true;
                        request.Error = state.error;
                        request.Done = true;
                        yield break;
                    }
                    bytes += bundle.size * (state.Have ? 1 : state.progress);
                    if (state.Have)
                        done++;
                }
                request.Progress = total > 0 ? (float)(bytes / total) : 1;
                if (done == bundles.Count)
                {
                    request.Progress = 1;
                    request.Done = true;
                    yield break;
                }
                yield return null;
            }
        }

        private static IEnumerator PrefetchWorker()
        {
            prefetching = true;
            var until = Time.realtimeSinceStartup + PrefetchDelay;
            while (Time.realtimeSinceStartup < until)
                yield return null;
            // Nothing ahead of the first gameplayStart: CrazyGames counts the initial download up to it, and the first Loading card
            // waits for a click, so the next level would otherwise download while the card is up
            while (SdfPlatform.WebPortal.Active && SdfPlatform.WebPortal.GameplayStarts == 0)
                yield return null;
            while (prefetchQueue.Count > 0)
            {
                while (HoldPrefetch)
                    yield return null;
                var bundle = prefetchQueue.Dequeue();
                while (Lod0Pending && bundle.name.EndsWith("-lod0"))
                    yield return null;
                if (states.ContainsKey(bundle.name))
                {
                    // Taken up by a foreground fetch; wait for it so only one runs at a time
                    while (states.TryGetValue(bundle.name, out var running) && !running.Have && !running.failed)
                        yield return null;
                    continue;
                }
                yield return Download(bundle);
                if (states.TryGetValue(bundle.name, out var state) && state.failed)
                {
                    Debug.LogWarning($"ContentStreamer: prefetch of {bundle.name} failed ({state.error}); it is fetched when needed");
                    states.Remove(bundle.name);
                }
            }
            prefetching = false;
        }

        private static IEnumerator Download(StreamedBundle info)
        {
            var state = new State { size = info.size };
            states[info.name] = state;
            var started = Time.realtimeSinceStartup;
            var url = Url(info);
            Debug.Log($"ContentStreamer: fetching {info.name} ({info.size / 1048576f:0.0} MB) from {url}");
            using (var request = UnityWebRequestAssetBundle.GetAssetBundle(url, Hash128.Parse(info.hash), 0))
            {
                var operation = request.SendWebRequest();
                ulong last = 0;
                var lastMove = Time.realtimeSinceStartup;
                while (!operation.isDone)
                {
                    state.progress = request.downloadProgress;
                    if (request.downloadedBytes != last)
                    {
                        last = request.downloadedBytes;
                        lastMove = Time.realtimeSinceStartup;
                    }
                    else if (Time.realtimeSinceStartup - lastMove > StallSeconds)
                    {
                        request.Abort();
                        break;
                    }
                    yield return null;
                }
                if (request.result != UnityWebRequest.Result.Success)
                {
                    state.failed = true;
                    state.error = string.IsNullOrEmpty(request.error) ? "no answer" : request.error;
                    Debug.LogWarning($"ContentStreamer: {info.name} failed: {state.error}");
                    yield break;
                }
                var bundle = DownloadHandlerAssetBundle.GetContent(request);
                if (bundle == null)
                {
                    state.failed = true;
                    state.error = "not an asset bundle";
                    Debug.LogWarning($"ContentStreamer: {info.name} is not a readable asset bundle");
                    yield break;
                }
                state.progress = 1;
                state.bundle = bundle;
            }
            Debug.Log($"ContentStreamer: {info.name} ready after {Time.realtimeSinceStartup - started:0.0} s");
        }
    }
}
