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
            if (!Enabled(level))
                return;
            Done = false;
            ContentStreamer.Lod0Pending = true;
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
                    var real = Lod0Meshes.Loaded.TryGetValue(key, out var loaded) ? loaded : null;
                    var use = real != null ? real : Finer(lods[1], key);
                    if (use != null)
                    {
                        filter.sharedMesh = use;
                        changed++;
                    }
                }
            }
            Invalidate();
            Debug.Log($"Lod0Streamer: {level} shows LOD1 in {changed} LOD0 slots until its LOD0 meshes arrive");
        }

        /// <summary>The match of <paramref name="level"/> is running (the HUD is up): fetch its LOD0 meshes and swap them in.</summary>
        public static void Begin(string level)
        {
            if (!Enabled(level))
            {
                Finish();
                return;
            }
            generation++;
            Done = false;
            Host().StartCoroutine(Run(level, StreamManifest.Lod0Key(level), generation));
        }

        private static void Finish()
        {
            Done = true;
            ContentStreamer.Lod0Pending = false;
            ContentStreamer.HoldPrefetch = false;
        }

        private static bool Enabled(string level) =>
            ContentStreamer.Enabled && ContentStreamer.Manifest.Has(StreamManifest.Lod0Key(level)) && !(SkipOnTouch && Device.IsTouch);

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
