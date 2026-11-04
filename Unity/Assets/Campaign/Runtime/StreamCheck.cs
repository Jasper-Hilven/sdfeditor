using System.Collections;
using SdfBattle;
using SdfLevel;
using UnityEngine;

namespace SdfCampaign
{
    /// <summary>
    /// A check for the web build (ticket 62): on a page whose URL contains <c>streamcheck</c> and <c>burst</c>, bots are killed with a headshot at
    /// two moments after the match started, one before the common bundle's meshes have arrived and one after, and what the bursts made is logged
    /// (<c>StreamCheck:</c> lines). It does nothing anywhere else.
    /// </summary>
    public sealed class StreamCheck : MonoBehaviour
    {
        public static bool Requested(string url) =>
            !string.IsNullOrEmpty(url) && url.IndexOf("streamcheck", System.StringComparison.OrdinalIgnoreCase) >= 0 && url.IndexOf("burst", System.StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool started;

        /// <summary>The first match has started (the HUD is up).</summary>
        public static void Begin()
        {
            if (started || !Requested(Application.absoluteURL))
                return;
            started = true;
            var go = new GameObject("Stream Check");
            DontDestroyOnLoad(go);
            go.AddComponent<StreamCheck>().StartCoroutine(go.GetComponent<StreamCheck>().Run());
        }

        private IEnumerator Run()
        {
            yield return Wait(2);
            yield return Kill("before");
            // Until the common meshes are in and swapped (or 120 s)
            var until = Time.realtimeSinceStartup + 120;
            while (!Lod0Streamer.CommonDone && Time.realtimeSinceStartup < until)
                yield return null;
            yield return Wait(3);
            yield return Kill("after");
        }

        private static IEnumerator Wait(float seconds)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
                yield return null;
        }

        private static IEnumerator Kill(string moment)
        {
            var match = Match.Current;
            var combat = Combat.Current;
            if (match == null || combat == null || match.You == null)
            {
                Debug.Log($"StreamCheck: {moment}: no match to burst in");
                yield break;
            }
            Player victim = null;
            foreach (var player in match.Players)
                if (player != match.You && player.State.Alive && player.Team != match.You.Team)
                {
                    victim = player;
                    break;
                }
            if (victim == null)
            {
                Debug.Log($"StreamCheck: {moment}: no bot to kill");
                yield break;
            }
            var before = FindObjectsByType<Corpse>(FindObjectsSortMode.None).Length;
            var common = Lod0Streamer.CommonDone;
            combat.Hurt(victim, Damage.By(match.You.State, "minigun", 10000, victim.transform.position + Vector3.back * 3, true));
            yield return null;
            yield return null;
            var corpses = FindObjectsByType<Corpse>(FindObjectsSortMode.None);
            var pieces = 0;
            var stubs = 0;
            foreach (var corpse in corpses)
                foreach (var filter in corpse.GetComponentsInChildren<MeshFilter>(true))
                {
                    pieces++;
                    if (Lod0Meshes.IsStub(filter.sharedMesh))
                        stubs++;
                }
            Debug.Log($"StreamCheck: {moment}: killed {victim.name}; common meshes arrived: {common}; corpses {before} -> {corpses.Length}, piece meshes {pieces}, on a stub {stubs}");
        }
    }
}
