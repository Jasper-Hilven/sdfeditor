using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SdfEffects;
using SdfGui;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SdfBattle.Editor
{
    /// <summary>
    /// The gore's play check and capture (tickets 51 and 46): the Meadow in Play mode, a bot killed at each gore level wearing each of the
    /// twelve heads in turn (<see cref="SoldierPuppet.WearHead"/> forces the head), seen from a camera at its side at 0.15 s, 0.5 s, 1.2 s,
    /// 2.4 s and 4.5 s after the kill. Ticket 46's checks: the head's explosion profile is registered and, at Splatty and up, throws its
    /// debris (the count the gore level gives) without allocating, and at Off throws none. Checks, per kill: the head bursts into its own pieces in its own blood colour (none at Off, where it
    /// puffs), the body becomes a ragdoll of ten bodies with no gib, piece or blood on it, it simulates for 3 s at most and then lies
    /// kinematic; then all seven bots are killed in one frame and at most <see cref="GoreRules.MaxCorpses"/> ragdolls simulate. Writes
    /// <c>gore-&lt;id&gt;.png</c> for each head (a row per level: off, splatty, extra splatty, ultra gore; a column per moment),
    /// <c>gore-ragdoll.png</c> (the headless corpse from three sides, lying) and <c>gore-check.txt</c> (the trace) to <c>-captureDir</c>.
    /// Batch, with the GPU and no -quit:
    /// <c>tools/unity-batch.sh gore51 -executeMethod SdfBattle.Editor.GoreCheck.Run -captureDir Media/gore-51</c>. Exit code 1 on a failure.
    /// </summary>
    [InitializeOnLoad]
    public static class GoreCheck
    {
        private const string ActiveKey = "SdfBattle.GoreCheck";
        private const string DirKey = "SdfBattle.GoreCheck.Dir";
        private const string Meadow = "Assets/Scenes/Meadow.unity";
        private static readonly float[] Moments = { 0.15f, 0.5f, 1.2f, 2.4f, 4.5f };
        private static readonly string[] Heads = SdfHeads.MonsterHeads.All.Where(h => h.Built).Select(h => h.Id).ToArray();
        private const int Tile = 400;

        private static readonly List<string> trace = new List<string>();
        private static readonly List<string> errors = new List<string>();
        private static readonly Dictionary<string, Texture2D[]> sheets = new Dictionary<string, Texture2D[]>();
        private static int scenario = -1, phase;
        private static float since, started = -1, killedAt;
        private static Match match;
        private static Player victim;
        private static SoldierRagdoll mine;
        private static int mostSimulating, debrisPeak, drawsPeak, particlePeak;
        private static long allocBefore;
        private static int spawnedBefore;
        private static string head;
        private static int level;
        private static int shot;
        private static int firstBursts;
        private static Camera camera;
        private static string directory;
        private static float frames, frameSeconds;

        static GoreCheck()
        {
            if (!SessionState.GetBool(ActiveKey, false))
                return;
            directory = SessionState.GetString(DirKey, Path.Combine(Path.GetTempPath(), "gore-51"));
            Application.logMessageReceived += (message, stack, type) =>
            {
                if ((type == LogType.Exception || type == LogType.Error) && !stack.Contains("UnityEditor.Search"))
                    errors.Add(message + "\n" + stack);
            };
            EditorApplication.update += Update;
        }

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-captureDir");
            SessionState.SetString(DirKey, Path.GetFullPath(i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Path.GetTempPath(), "gore-51")));
            SessionState.SetBool(ActiveKey, true);
            EditorSceneManager.OpenScene(Meadow);
            EditorApplication.EnterPlaymode();
        }

        private static void Log(string text)
        {
            trace.Add(text);
            Debug.Log("[GoreCheck] " + text);
        }

        private static void Expect(bool ok, string what)
        {
            if (!ok)
                throw new Exception("Expected: " + what);
            trace.Add("ok: " + what);
        }

        // 12 heads x 4 levels, then the ragdoll sheet at splatty, then everyone at once (at ultra gore, each in a different head)
        private static int Scenarios => Heads.Length * 4 + 2;

        private static void Update()
        {
            if (!EditorApplication.isPlaying)
                return;
            var now = Time.realtimeSinceStartup;
            if (started < 0)
                started = since = now;
            try
            {
                Step(now);
            }
            catch (Exception exception)
            {
                errors.Add(exception.ToString());
                Finish();
                return;
            }
            if (now - started > 900)
            {
                errors.Add($"timed out in scenario {scenario}, phase {phase}");
                Finish();
            }
        }

        private static void Step(float now)
        {
            // The level starts paused behind its menu
            if (match != null)
                Time.timeScale = 1;
            if (match == null)
            {
                match = Match.Current;
                if (match == null || match.State == null || now - since < 2)
                    return;
                Time.timeScale = 1;
                if (match.Held)
                    match.Release();
                since = now;
                return;
            }
            // The round counts down first
            if (scenario < 0)
            {
                if (match.Held || !match.State.CanMove || now - since < 4)
                    return;
                scenario = 0;
                phase = 0;
                since = now;
            }
            frames++;
            frameSeconds += Time.unscaledDeltaTime;

            if (scenario >= Scenarios)
            {
                Finish();
                return;
            }
            if (scenario < Heads.Length * 4)
                Single(now, Heads[scenario / 4], scenario % 4);
            else if (scenario == Heads.Length * 4)
                Single(now, "yeti", GoreRules.Splatty, ragdollSheet: true);
            else
                Everyone(now);
        }

        private static Player PickVictim()
        {
            foreach (var player in match.Players)
                if (player.State.IsBot && player.State.Alive && player.soldier != null && player.soldier.Head != null)
                    return player;
            return null;
        }

        private static void Single(float now, string wearing, int gore, bool ragdollSheet = false)
        {
            switch (phase)
            {
                case 0:
                {
                    victim = PickVictim();
                    if (victim == null || now - since < 0.5f)
                        return;
                    victim.State.EndProtection();
                    GuiSettings.Gore = gore;
                    level = gore;
                    head = wearing;
                    victim.soldier.WearHead(wearing);
                    Expect(victim.soldier.Head != null && victim.soldier.Head.Id == wearing, $"the {wearing} is worn");
                    EnsureCamera();
                    // Kill it: a pistol shot from behind its left, so the hit pushes it away from the camera's side
                    var killer = match.Players.First(p => p.State.Team != victim.State.Team);
                    var from = victim.transform.position + victim.transform.right * -2f + Vector3.up * 1.6f;
                    var before = CountObjects("Head of ");
                    spawnedBefore = match.Gore.Monsters.Debris.Spawned;
                    var damage = new Damage(9999, WeaponIds.Pistol, killer.State.Team, killer.State, killer.name, from, headshot: true);
                    var dealt = match.GetComponentInChildren<Combat>().State.Hurt(victim.State, damage);
                    Log($"hurt dealt {dealt}, alive {victim.State.Alive}, health {victim.State.Health}, protected {match.State.CanHurt(victim.State, killer.State.Team)}");
                    killedAt = Time.time;
                    shot = 0;
                    phase = 1;
                    Log($"scenario {scenario}: {wearing} at gore {gore}, killed {victim.name}");
                    var pieces = CountObjects("Head of ") - before;
                    Expect(!victim.soldier.Visible, "the live body is hidden at once (its ragdoll draws)");
                    var ragdolls = match.Gore.Ragdolls.ToList();
                    Expect(ragdolls.Count >= 1, "a ragdoll started in the kill's own frame");
                    var ragdoll = mine = ragdolls.Last();
                    Expect(ragdoll.BodyCount == 10 && ragdoll.ActiveBodies == 10, "ten simulating bodies");
                    Expect(ragdoll.Colliders.All(c => c.gameObject.layer == Corpse.IgnoreRaycastLayer), "its colliders are on the ignore-raycast layer");
                    var renderers = ragdoll.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToList();
                    Expect(renderers.All(r => r is SkinnedMeshRenderer), "nothing but the skinned body: no piece, gib or blood on it");
                    Expect(ragdoll.GetComponentInChildren<SdfHeads.HeadWear>(true) == null, "the ragdoll is headless");
                    Expect(pieces == (gore == GoreRules.Off ? 0 : 1), gore == GoreRules.Off ? "gore off: no head pieces, only a puff" : "the head bursts (a head-pieces corpse appears)");
                    ExplosionChecks(wearing, gore);
                    if (gore != GoreRules.Off)
                    {
                        var corpse = GameObject.FindObjectsByType<Corpse>(FindObjectsSortMode.None).First(c => c.name.StartsWith("Head of "));
                        Expect(corpse.GetComponentsInChildren<Rigidbody>().Length == 7, "seven pieces of the head");
                        Expect(corpse.GetComponentsInChildren<MeshFilter>().All(f => f.sharedMesh != null), "each piece has its mesh");
                    }
                    break;
                }
                case 1:
                {
                    var elapsed = Time.time - killedAt;
                    if (shot < Moments.Length && elapsed >= Moments[shot])
                    {
                        Capture(ragdollSheet ? "gore-ragdoll" : $"gore-{head}", ragdollSheet ? shot : level * Moments.Length + shot, ragdollSheet);
                        shot++;
                    }
                    if (shot >= Moments.Length)
                    {
                        var ragdoll = mine;
                        Log($"timeScale {Time.timeScale}, ragdoll {(ragdoll == null ? "none" : $"simulating {ragdoll.Simulating}, active {ragdoll.ActiveBodies}, active self {ragdoll.gameObject.activeSelf}, age {ragdoll.Age:F2}, id {ragdoll.GetInstanceID()}, of {match.Gore.Ragdolls.Count()}, elapsed {Time.time - killedAt:F2}")}");
                        Expect(ragdoll != null && !ragdoll.Simulating && ragdoll.ActiveBodies == 0, "by 4.5 s the ragdoll lies still (kinematic, no joints)");
                        scenario++;
                        phase = 2;
                        since = now;
                    }
                    break;
                }
                case 2:
                    // Let the next bot be alive and the scene calm for a moment, and its player appear again (the respawn is 5 s)
                    if (now - since > 0.2f)
                        phase = 0;
                    break;
            }
        }

        /// <summary>
        /// Ticket 46: the head's profile is registered (all twelve), and the burst just played threw the debris the gore level gives, none at
        /// Off, with nothing allocated by the explosion itself (the first of the run may warm something up: its bytes are logged).
        /// </summary>
        private static void ExplosionChecks(string id, int gore)
        {
            var profile = HeadBurstProfile.Of(id) as ExplosionProfile;
            Expect(profile != null, $"the {id} has an explosion profile registered");
            if (gore == GoreRules.Off)
            {
                Expect(match.Gore.Monsters.Debris.Spawned == spawnedBefore, "gore off: no debris (the head only puffs)");
                return;
            }
            var expected = SdfHeads.MonsterExplosions.DebrisOf(profile.Explosion, gore);
            Expect(match.Gore.Monsters != null && match.Gore.Monsters.Debris != null && match.Gore.Monsters.Debris.Ready, "the debris models are ready (MonsterDebrisBuilder)");
            var thrown = match.Gore.Monsters.Debris.Spawned - spawnedBefore;
            Expect(thrown == expected && MonsterBurst.LastDebris == expected, $"{id} at gore {gore} threw {thrown} debris pieces, expected {expected}");
            Log($"{id} at gore {gore}: {MonsterBurst.LastDebris} debris, explosion allocated {MonsterBurst.LastAllocatedBytes} bytes, {match.Gore.Monsters.Debris.Live} alive, peak {match.Gore.Monsters.Debris.Peak}");
            if (firstBursts++ > 0)
                Expect(MonsterBurst.LastAllocatedBytes == 0, $"{id} at gore {gore}: the explosion allocated nothing ({MonsterBurst.LastAllocatedBytes} bytes)");
        }

        private static void Everyone(float now)
        {
            if (phase == 2)
                phase = 0;
            switch (phase)
            {
                case 0:
                {
                    GuiSettings.Gore = GoreRules.UltraGore;
                    var targets = match.Players.Where(p => p.State.Alive).ToList();
                    var wear = 0;
                    foreach (var each in targets)
                    {
                        each.State.EndProtection();
                        // The heaviest explosions, one head each: the most debris at once
                        if (each.soldier != null)
                            each.soldier.WearHead(new[] { "santa", "leprechaun", "yeti", "pumpkin", "werewolf", "mummy", "frankenstein", "vampire" }[wear++ % 8]);
                    }
                    Log($"everyone: {targets.Count} killed in one frame at ultra gore");
                    match.Gore.Monsters.Debris.Clear();
                    allocBefore = System.GC.GetTotalMemory(false);
                    particlePeak = 0;
                    foreach (var target in targets)
                    {
                        var killer = match.Players.First(p => p.State.Team != target.State.Team);
                        match.GetComponentInChildren<Combat>().State.Hurt(target.State, new Damage(9999, WeaponIds.Pistol, killer.State.Team, killer.State, killer.name, target.transform.position + Vector3.up * 1.5f + target.transform.right * 2));
                    }
                    killedAt = Time.time;
                    phase = 1;
                    break;
                }
                case 1:
                {
                    mostSimulating = Mathf.Max(mostSimulating, match.Gore.Ragdolls.Count(r => r.Simulating));
                    var debris = match.Gore.Monsters.Debris;
                    debrisPeak = Mathf.Max(debrisPeak, debris.Live);
                    drawsPeak = Mathf.Max(drawsPeak, debris.Draws);
                    if (match.Gore.Particles != null)
                        particlePeak = Mathf.Max(particlePeak, match.Gore.Particles.Count);
                    if (Time.time - killedAt > 3.6f)
                    {
                        Expect(mostSimulating <= GoreRules.MaxCorpses * SdfMushrooms.QualityExtras.GoreAmount && mostSimulating >= 1, $"{mostSimulating} ragdolls simulated at most at once, never more than {GoreRules.MaxCorpses}");
                        Expect(match.Gore.Ragdolls.All(r => !r.Simulating), "all lie still after 3 s");
                        Expect(debrisPeak > 0 && debrisPeak <= match.Gore.Monsters.Debris.capacity * SdfMushrooms.QualityExtras.GoreAmount, $"8 deaths at ultra gore: {debrisPeak} debris alive at most (pool {match.Gore.Monsters.Debris.capacity * SdfMushrooms.QualityExtras.GoreAmount}), {match.Gore.Monsters.Debris.Spawned} thrown");
                        Expect(drawsPeak <= 2 * SdfHeads.DebrisKinds.Count * 3, $"the debris drew in {drawsPeak} instanced calls at most, whatever the pieces");
                        Log($"everyone: debris peak {debrisPeak}, instanced draws peak {drawsPeak}, particles peak {particlePeak}, managed heap {allocBefore} -> {System.GC.GetTotalMemory(false)} bytes");
                        scenario++;
                    }
                    break;
                }
            }
        }

        private static int CountObjects(string prefix) => GameObject.FindObjectsByType<Corpse>(FindObjectsSortMode.None).Count(c => c.name.StartsWith(prefix));

        // ---------------------------------------------------------------- Captures

        private static void EnsureCamera()
        {
            if (camera != null)
                return;
            var go = new GameObject("Gore Check Camera");
            camera = go.AddComponent<Camera>();
            camera.fieldOfView = 40;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 300;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.enabled = false;
        }

        private static void Capture(string sheet, int index, bool ragdoll)
        {
            var count = ragdoll ? Moments.Length : 4 * Moments.Length;
            if (!sheets.TryGetValue(sheet, out var tiles))
                sheets[sheet] = tiles = new Texture2D[count];
            var soldier = victim.soldier;
            var feet = victim.transform.position;
            Vector3 target, from;
            if (ragdoll)
            {
                // The whole corpse from the side the hit came from, then three more sides at the end
                var around = Quaternion.Euler(0, shot * 70, 0);
                var hips = mine != null ? mine.Rigidbodies.First().position : feet;
                if (shot == Moments.Length - 1)
                    Log($"ragdoll hips at {hips}, the player's feet at {feet}, renderers {string.Join(",", mine.GetComponentsInChildren<SkinnedMeshRenderer>().Select(r => r.isVisible + " " + r.bounds.center + " " + r.enabled))}");
                target = hips + Vector3.up * 0.2f;
                feet = hips;
                from = target + around * (victim.transform.right * -1 * 3.2f + Vector3.up * 1.4f);
            }
            else
            {
                target = soldier.Head != null ? soldier.Head.transform.position : feet + Vector3.up * 1.7f;
                target = new Vector3(feet.x, feet.y + 1.5f, feet.z);
                from = ClearView(target, victim.transform, 4.4f);
            }
            camera.transform.position = from;
            camera.transform.LookAt(target);
            var rt = new RenderTexture(Tile, Tile, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            camera.targetTexture = rt;
            camera.Render();
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var image = new Texture2D(Tile, Tile, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Tile, Tile), 0, 0);
            image.Apply();
            RenderTexture.active = active;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            tiles[index] = image;
        }

        /// <summary>A spot about <paramref name="distance"/> metres from the target, to a side, with nothing between: the sides are tried in turn.</summary>
        private static Vector3 ClearView(Vector3 target, Transform of, float distance)
        {
            var sides = new[] { of.right, -of.right, of.forward, -of.forward };
            foreach (var side in sides)
            {
                var spot = target + side * distance + Vector3.up * 0.4f;
                if (!Physics.CheckSphere(spot, 0.3f) && !Physics.Linecast(target, spot, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    return spot;
            }
            return target + Vector3.up * 2f + of.right * distance;
        }

        private static void Write(string name, Texture2D[] tiles, int columns)
        {
            var rows = (tiles.Length + columns - 1) / columns;
            var atlas = new Texture2D(columns * Tile, rows * Tile, TextureFormat.RGB24, false);
            for (var i = 0; i < tiles.Length; i++)
            {
                if (tiles[i] == null)
                    continue;
                var x = (i % columns) * Tile;
                var y = (rows - 1 - i / columns) * Tile;
                atlas.SetPixels(x, y, Tile, Tile, tiles[i].GetPixels());
            }
            atlas.Apply();
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), atlas.EncodeToPNG());
        }

        private static void Finish()
        {
            foreach (var pair in sheets)
                Write(pair.Key, pair.Value, Moments.Length);
            var text = new StringBuilder();
            foreach (var line in trace)
                text.AppendLine(line);
            text.AppendLine($"frames {frames}, mean frame {(frames > 0 ? frameSeconds / frames * 1000 : 0):F1} ms (editor, not the web)");
            foreach (var error in errors)
                text.AppendLine("ERROR " + error);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "gore-check.txt"), text.ToString());
            Debug.Log($"[GoreCheck] {(errors.Count == 0 ? "passed" : "FAILED")}; written to {directory}");
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.update -= Update;
            EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
        }
    }
}
