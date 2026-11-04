using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using SdfLevel;
using SdfPlatform;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace SdfCampaign
{
    /// <summary>
    /// Brings in a level's LOD0 meshes after its match started (ticket 60). The web build's level bundles carry stubs where the LOD groups'
    /// finest meshes would be (<see cref="Lod0Meshes"/>); the real ones are in the level's <c>-lod0</c> bundles. <see cref="Prepare"/> (the scene is live,
    /// still covered) puts each LOD1 mesh in the LOD0 slot of the stubs; <see cref="Begin"/> (the HUD is up, so the first <c>gameplayStart</c> has gone)
    /// fetches the bundles in the background, loads their meshes, lets go of the bundles' file data and swaps the meshes in, a few milliseconds a frame:
    /// every MeshFilter in a LOD0 slot, loaded prefabs included (what spawns later is made from them), and the instanced renderer's prototypes.
    /// Nothing happens in the editor and in builds that do not stream, where the real meshes are in the scene.
    /// Ticket 62: the player itself carries stubs for the LOD0 meshes of heads, weapons and pods and for the head pieces; the one
    /// <c>common</c> bundle (<see cref="StreamManifest.CommonKey"/>) holds the real ones, is fetched once after the first match started (before the
    /// level's own), and is swapped in by mesh name: LOD0 slots first show LOD1, a burst before it arrives has no pieces (<see cref="Lod0Meshes.PiecesReady"/>).
    /// </summary>
    public sealed class Lod0Streamer : MonoBehaviour
    {
        /// <summary>The one switch for skipping the LOD0 meshes on a touch device (a phone on its lowest quality would stay on LOD1); off: phones download them.</summary>
        public const bool SkipOnTouch = false;
        /// <summary>The swap works this long a frame.</summary>
        public const float SliceMilliseconds = 4;

        /// <summary>The level whose meshes <see cref="Lod0Meshes.Loaded"/> holds; empty for none.</summary>
        public static string LoadedLevel { get; private set; } = "";
        /// <summary>The meshes swapped in for the level in play (0 until the swap is done); for checks.</summary>
        public static int SwappedMeshes { get; private set; }
        /// <summary>The swap of the level in play is done (or there is nothing to swap).</summary>
        public static bool Done { get; private set; } = true;

        private static Lod0Streamer instance;
        private static int generation;
        private static bool commonRunning;
        private static bool standInsRunning;

        /// <summary>The common bundle's meshes (heads, weapons, pods, head pieces) have been swapped in; true as well where there is no common bundle (editor, desktop).</summary>
        public static bool CommonDone { get; private set; } = true;

        /// <summary>The scene of <paramref name="level"/> is live and covered: LOD1 stands in for the LOD0 stubs.</summary>
        public static void Prepare(string level)
        {
            // The last level's meshes go with its scene (nothing may hold them for UnloadUnusedAssets)
            if (LoadedLevel != level)
            {
                Lod0Meshes.Loaded.Clear();
                LoadedLevel = "";
                SwappedMeshes = 0;
            }
            ContentStreamer.Lod0Pending = false;
            var levelStreams = Enabled(level);
            var commonStreams = CommonEnabled;
            if (!levelStreams && !commonStreams)
                return;
            if (commonStreams && CommonDone && Lod0Meshes.Common.Count == 0)
                CommonDone = false;
            Done = false;
            ContentStreamer.Lod0Pending = levelStreams;
            var changed = StandIns();
            // Prefabs loaded since the common bundle arrived (a level's bundles) carry stubs again
            if (Lod0Meshes.Common.Count > 0)
                SwapStubFilters();
            Invalidate();
            Debug.Log($"Lod0Streamer: {level} shows LOD1 in {changed} LOD0 slots until its LOD0 meshes arrive");
            if (!CommonDone && !standInsRunning)
                Host().StartCoroutine(StandInLoop());
        }

        // LOD1 (or the real mesh, where it has arrived) in every LOD0 slot that holds a stub
        private static int StandIns()
        {
            var changed = 0;
            foreach (var group in Resources.FindObjectsOfTypeAll<LODGroup>())
            {
                if (group == null)
                    continue;
                var lods = group.GetLODs();
                if (lods.Length < 2)
                    continue;
                foreach (var renderer in lods[0].renderers)
                {
                    if (renderer == null || !renderer.TryGetComponent<MeshFilter>(out var filter) || !Lod0Meshes.IsStub(filter.sharedMesh))
                        continue;
                    var key = Lod0Meshes.RealName(filter.sharedMesh.name);
                    var real = Lod0Meshes.TryReal(key, out var loaded) ? loaded : null;
                    var use = real != null ? real : Finer(lods[1], key);
                    if (use != null)
                    {
                        filter.sharedMesh = use;
                        changed++;
                    }
                }
            }
            return changed;
        }

        // Until the common bundle is in, a prefab loaded late may bring stubs in: look again now and then (the prefab assets and what was made from them)
        private static IEnumerator StandInLoop()
        {
            standInsRunning = true;
            while (!CommonDone)
            {
                var until = Time.realtimeSinceStartup + 2;
                while (Time.realtimeSinceStartup < until)
                    yield return null;
                if (!CommonDone)
                    StandIns();
            }
            standInsRunning = false;
        }

        // The real mesh for every MeshFilter that holds a common stub outside the LOD groups (the head pieces)
        private static int SwapStubFilters()
        {
            var swapped = 0;
            foreach (var filter in Resources.FindObjectsOfTypeAll<MeshFilter>())
                if (filter != null && Lod0Meshes.IsStub(filter.sharedMesh) && Lod0Meshes.Common.TryGetValue(Lod0Meshes.RealName(filter.sharedMesh.name), out var real))
                {
                    filter.sharedMesh = real;
                    swapped++;
                }
            return swapped;
        }

        /// <summary>The match of <paramref name="level"/> is running (the HUD is up): fetch the common meshes (once) and the level's LOD0 meshes and swap them in.</summary>
        public static void Begin(string level)
        {
            StreamCheck.Begin();
            if (!Enabled(level) && !(CommonEnabled && !CommonDone) && !MusicPending(level))
            {
                Finish();
                return;
            }
            generation++;
            Done = false;
            Host().StartCoroutine(Sequence(level, generation));
        }

        private static void Finish()
        {
            Done = true;
            ContentStreamer.Lod0Pending = false;
            ContentStreamer.HoldPrefetch = false;
        }

        private static bool Enabled(string level) =>
            ContentStreamer.Enabled && ContentStreamer.Manifest.Has(StreamManifest.Lod0Key(level)) && !(SkipOnTouch && Device.IsTouch);

        private static bool CommonEnabled =>
            ContentStreamer.Enabled && ContentStreamer.Manifest.Has(StreamManifest.CommonKey) && !(SkipOnTouch && Device.IsTouch);

        private static Mesh Finer(LOD lod, string key)
        {
            foreach (var renderer in lod.renderers)
                if (renderer != null && renderer.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null && Lod0Meshes.PartOf(filter.sharedMesh.name) == key)
                    return filter.sharedMesh;
            return null;
        }

        private static void Invalidate()
        {
            var shadow = FindFirstObjectByType<StaticSunShadow>();
            if (shadow != null)
                shadow.Invalidate();
        }

        private static Lod0Streamer Host()
        {
            if (instance == null)
            {
                var go = new GameObject("LOD0 Streamer");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<Lod0Streamer>();
            }
            return instance;
        }

        private static bool Stale(string level, int id) => id != generation || SceneManager.GetActiveScene().name != level;

        private static IEnumerator Sequence(string level, int id)
        {
            if (CommonEnabled && !CommonDone)
            {
                // The common meshes come first: they are on screen in every match
                while (commonRunning)
                    yield return null;
                if (!CommonDone)
                    yield return RunCommon();
            }
            if (Stale(level, id))
                yield break;
            if (Enabled(level))
                yield return Run(level, StreamManifest.Lod0Key(level), id);
            else
                Finish();
            // Last: layers 2 to 4 of the battle music (nobody hears them in the first seconds of a match)
            yield return RunMusic(level);
        }

        private static string loadedMusic = "";

        private static bool MusicPending(string level) =>
            ContentStreamer.Enabled && ContentStreamer.Manifest.Has(StreamManifest.MusicKey(level)) && MusicId(level) != loadedMusic;

        private static string MusicId(string level)
        {
            var names = new List<string>();
            foreach (var bundle in ContentStreamer.Manifest.Bundles(StreamManifest.MusicKey(level)))
                names.Add(bundle.name);
            return string.Join("+", names);
        }

        // The music layers 2 to 4 (ticket 62): fetched after everything else of the match, handed to the arena's sounds and the track in play
        private static IEnumerator RunMusic(string level)
        {
            if (!MusicPending(level))
                yield break;
            var key = StreamManifest.MusicKey(level);
            var id = MusicId(level);
            var started = Time.realtimeSinceStartup;
            ContentStreamer.HoldPrefetch = true;
            var request = ContentStreamer.Fetch(key);
            while (!request.Done)
                yield return null;
            ContentStreamer.HoldPrefetch = false;
            if (request.Failed)
            {
                Debug.LogWarning($"Lod0Streamer: the music layers of {level} failed ({request.Error}); the track plays its first layer only");
                yield break;
            }
            var clips = new List<AudioClip>();
            foreach (var bundle in ContentStreamer.Open(key))
            {
                var load = bundle.LoadAllAssetsAsync<AudioClip>();
                yield return load;
                foreach (var asset in load.allAssets)
                    if (asset is AudioClip clip)
                        clips.Add(clip);
            }
            ContentStreamer.Consume(key);
            SdfAudio.ArenaAudio.Late.Clear();
            foreach (var clip in clips)
                SdfAudio.ArenaAudio.Late[clip.name] = clip;
            loadedMusic = id;
            if (SdfAudio.GameAudio.Current != null)
                SdfAudio.GameAudio.Current.MusicArrived();
            Debug.Log($"Lod0Streamer: {level} music layers fetched ({clips.Count} clips) in {Time.realtimeSinceStartup - started:0.0} s");
        }

        private static IEnumerator RunCommon()
        {
            commonRunning = true;
            var started = Time.realtimeSinceStartup;
            ContentStreamer.HoldPrefetch = true;
            ContentStreamer.Lod0Pending = true;
            var request = ContentStreamer.Fetch(StreamManifest.CommonKey);
            while (!request.Done)
                yield return null;
            if (request.Failed)
            {
                // Heads, weapons and pods stay at LOD1 and bursts go without pieces; the next match tries again
                Debug.LogWarning($"Lod0Streamer: the common bundle failed ({request.Error}); heads, weapons and pieces stay as stand-ins");
                commonRunning = false;
                ContentStreamer.HoldPrefetch = false;
                yield break;
            }
            var fetched = Time.realtimeSinceStartup - started;
            var meshes = new List<Mesh>();
            foreach (var bundle in ContentStreamer.Open(StreamManifest.CommonKey))
            {
                var load = bundle.LoadAllAssetsAsync<Mesh>();
                yield return load;
                foreach (var asset in load.allAssets)
                    if (asset is Mesh mesh)
                        meshes.Add(mesh);
            }
            ContentStreamer.Consume(StreamManifest.CommonKey);
            foreach (var mesh in meshes)
                Lod0Meshes.Common[mesh.name] = mesh;
            // Your own weapon and the heads first (the LOD groups), then the pieces
            var swapped = 0;
            var clock = Stopwatch.StartNew();
            foreach (var group in Resources.FindObjectsOfTypeAll<LODGroup>())
            {
                if (group != null)
                {
                    var lods = group.GetLODs();
                    if (lods.Length > 0)
                        foreach (var renderer in lods[0].renderers)
                            if (renderer != null && renderer.TryGetComponent<MeshFilter>(out var filter) && Lod0Meshes.RealFor(filter.sharedMesh) is { } real)
                            {
                                filter.sharedMesh = real;
                                swapped++;
                            }
                }
                if (clock.Elapsed.TotalMilliseconds > SliceMilliseconds)
                {
                    yield return null;
                    clock.Restart();
                }
            }
            swapped += SwapStubFilters();
            Invalidate();
            CommonDone = true;
            commonRunning = false;
            ContentStreamer.HoldPrefetch = false;
            Debug.Log($"Lod0Streamer: common bundle fetched {meshes.Count} meshes in {fetched:0.0} s, swapped {swapped} in {Time.realtimeSinceStartup - started:0.0} s");
        }

        private static IEnumerator Run(string level, string key, int id)
        {
            var started = Time.realtimeSinceStartup;
            if (LoadedLevel != level)
            {
                // The match's own LOD0 meshes come before the next level's bundles
                ContentStreamer.HoldPrefetch = true;
                StreamRequest request;
                while (true)
                {
                    request = ContentStreamer.Fetch(key);
                    while (!request.Done)
                        yield return null;
                    if (!request.Failed)
                        break;
                    // Not worth a retry loop: the level stays at LOD1, which is playable
                    Debug.LogWarning($"Lod0Streamer: {level} LOD0 bundles failed ({request.Error}); the level stays at LOD1");
                    Finish();
                    yield break;
                }
                ContentStreamer.HoldPrefetch = false;
                var fetched = Time.realtimeSinceStartup - started;
                var meshes = new List<Mesh>();
                foreach (var bundle in ContentStreamer.Open(key))
                {
                    var load = bundle.LoadAllAssetsAsync<Mesh>();
                    yield return load;
                    foreach (var asset in load.allAssets)
                        if (asset is Mesh mesh)
                            meshes.Add(mesh);
                }
                ContentStreamer.Consume(key);
                if (Stale(level, id))
                    yield break;
                Lod0Meshes.Loaded.Clear();
                foreach (var mesh in meshes)
                    Lod0Meshes.Loaded[mesh.name] = mesh;
                LoadedLevel = level;
                Debug.Log($"Lod0Streamer: {level} fetched {meshes.Count} LOD0 meshes in {fetched:0.0} s");
            }

            var swapped = 0;
            var left = 0;
            var clock = Stopwatch.StartNew();
            foreach (var group in Resources.FindObjectsOfTypeAll<LODGroup>())
            {
                if (group != null)
                {
                    var lods = group.GetLODs();
                    if (lods.Length > 0)
                        foreach (var renderer in lods[0].renderers)
                            if (renderer != null && renderer.TryGetComponent<MeshFilter>(out var filter) && Lod0Meshes.RealFor(filter.sharedMesh) is { } real)
                            {
                                filter.sharedMesh = real;
                                swapped++;
                            }
                            else if (renderer != null && renderer.TryGetComponent<MeshFilter>(out var rest) && rest.sharedMesh != null
                                     && (Lod0Meshes.IsStub(rest.sharedMesh) || rest.sharedMesh.name.EndsWith("_LOD1")))
                                left++;
                }
                if (clock.Elapsed.TotalMilliseconds > SliceMilliseconds)
                {
                    yield return null;
                    if (Stale(level, id))
                        yield break;
                    clock.Restart();
                }
            }
            foreach (var scatter in FindObjectsByType<InstancedScatterRenderer>(FindObjectsSortMode.None))
                swapped += scatter.ReplaceLod0Meshes();
            Invalidate();
            SwappedMeshes = swapped;
            Finish();
            Debug.Log($"Lod0Streamer: {level} swapped {swapped} meshes in {Time.realtimeSinceStartup - started:0.0} s ({left} LOD0 slots still on a stub or LOD1)");
        }
    }
}
