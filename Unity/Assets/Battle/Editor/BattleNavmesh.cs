using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace SdfBattle.Editor
{
    /// <summary>
    /// The navmesh of a battle scene (ticket 23), baked by the level builder on every rebuild, so it stays in step with the level and
    /// nothing of it is made by hand. One NavMeshSurface (AI Navigation package) per agent type under the level's root, Level/Navigation
    /// &lt;agent&gt;, its data saved next to the level's meshes (Assets/Level/Meshes/&lt;level&gt;/NavMesh &lt;agent&gt;.asset, generated,
    /// not in git):
    /// <list type="bullet">
    /// <item>From the level's colliders (the ground chunks, the castles' walls, floors, stairs and wall-walks, the rocks, the trunks; the
    /// castles are drawn instanced, so their colliders are what the scene has of them), not from what is drawn.</item>
    /// <item>Within the play area, from a little below the spawns to <see cref="AboveSpawn"/> over them: the courtyards, the stairs and
    /// the wall-walks (5 m) are in, the towers' caps and the keep's top, which nobody walks, and the tops of the walls round the play area
    /// are out.</item>
    /// <item>The players' capsules and the battle objects (the pods are walked onto, the flag stands touched) are left out of it; but
    /// not the turrets (ticket 24): their colliders go in as obstacles, not walkable, so paths go round them and nothing walks on
    /// their caps. They are static scene objects and respawn where they stood, so the navmesh holds them all the time.</item>
    /// <item><see cref="BotNavigation.ScoutAgent"/>: the players' capsule (r 0.3 m, 1.75 m high, as
    /// <see cref="SdfLevel.FirstPersonController.Configure"/> makes every player's) with 5 cm to spare round it, so paths keep off the
    /// walls' corners; step 0.4 m, slope 45 degrees; the bake makes drop links down ledges up to 6 m (off
    /// the wall-walks into the courtyard), and no other links: a jump up is only where the bots find themselves stuck.
    /// <see cref="BotNavigation.TankAgent"/>: ticket 24's tanks, 0.8 m, ground only.</item>
    /// </list>
    /// The agent types are made (or set back to these numbers) in the project's navigation settings (ProjectSettings/NavMeshAreas.asset)
    /// when missing. Building is single-threaded editor work; at runtime the data is only loaded and queried, which WebGL 2 does.
    /// </summary>
    public static class BattleNavmesh
    {
        public const string ObjectPrefix = "Navigation ";

        /// <summary>Metres over the highest spawn the navmesh reaches: the wall-walks (5 m) and the tower rings, not the caps.</summary>
        public const float AboveSpawn = 7.5f;

        /// <summary>Metres under the lowest spawn it reaches.</summary>
        public const float BelowSpawn = 12;

        /// <summary>An agent type the bake is made for.</summary>
        public readonly struct Agent
        {
            public Agent(string name, float radius, float height, float climb, float slope, float drop)
            {
                Name = name;
                Radius = radius;
                Height = height;
                Climb = climb;
                Slope = slope;
                Drop = drop;
            }

            public string Name { get; }
            public float Radius { get; }
            public float Height { get; }
            public float Climb { get; }
            public float Slope { get; }
            /// <summary>Metres a drop link goes down at most; 0 for none.</summary>
            public float Drop { get; }
        }

        public static readonly Agent[] Agents =
        {
            new Agent(BotNavigation.ScoutAgent, 0.35f, 1.75f, 0.4f, 45, 6),
            new Agent(BotNavigation.TankAgent, 0.45f, 0.8f, 0.15f, 30, 0),
        };

        /// <summary>
        /// The final boss's agent (ticket 33.3): Prime on foot, r 1.8 m, 6.5 m high, step 0.5 m, slope 25 degrees, ground only. Baked
        /// on boss levels only (the ones with <see cref="SdfLevel.PhaseGroup"/>s), so Prime's path round the shelters (their caps are
        /// under his height) and the wreck keeps off what a scout can pass.
        /// </summary>
        public static readonly Agent BossAgent = new Agent(BotNavigation.BossAgent, 1.8f, 6.5f, 0.5f, 25, 0);

        /// <summary>What a surface's object and data are called: Navigation Scout, and on a boss level Navigation Scout Phase 2.</summary>
        public static string SurfaceName(string agent, int phase) => phase == 0 ? ObjectPrefix + agent : $"{ObjectPrefix}{agent} Phase {phase}";

        /// <summary>The asset a surface's data is saved as: NavMesh Scout.asset, on a boss level NavMesh Scout Phase 2.asset.</summary>
        public static string AssetName(string agent, int phase) => phase == 0 ? $"NavMesh {agent}.asset" : $"NavMesh {agent} Phase {phase}.asset";

        /// <summary>
        /// Bakes the battle scene's navmesh for every agent type; returns how many were baked (0 for a scene with no battle).
        /// <paramref name="levelRoot"/> holds the level's colliders, <paramref name="playMin"/> and <paramref name="playMax"/> are the play
        /// area's corners (x, z in Unity's coordinates), <paramref name="ignored"/> what is no ground (the player, the battle objects).
        /// <para>
        /// A boss level (ticket 33.3: its placed models are grouped under <see cref="SdfLevel.PhaseGroup"/>s) is baked once per phase
        /// layout, with the phase's groups shown and the others hidden: three surfaces per agent, Navigation Scout Phase 1 to 3, and the
        /// <see cref="BossAgent"/> as well. The surfaces of phases 2 and 3 are left inactive (the boss's arena switches them with the
        /// phase), and the scene is left in phase 1.
        /// </para>
        /// </summary>
        public static int Build(Transform levelRoot, Vector2 playMin, Vector2 playMax, BattleArena arena, string meshFolder, params GameObject[] ignored)
        {
            if (arena == null || arena.west == null || arena.east == null || arena.west.spawn == null || arena.east.spawn == null)
                return 0;
            foreach (var item in ignored.Where(i => i != null))
            {
                var modifier = item.GetComponent<NavMeshModifier>();
                if (modifier == null)
                    modifier = item.AddComponent<NavMeshModifier>();
                modifier.ignoreFromBuild = true;
                modifier.applyToChildren = true;
            }
            // The turrets (ticket 24) are obstacles: a modifier further down the hierarchy overrides Battle's, so their colliders are
            // collected, marked not walkable
            foreach (var turret in new[] { arena.west, arena.east }.SelectMany(side => side.turrets ?? System.Array.Empty<SdfMachines.MachineRig>()))
                if (turret != null)
                    MarkObstacle(turret.gameObject);
            // The bake takes the colliders of every open scene: another one open beside the level's ends up in its navmesh
            if (levelRoot.gameObject.scene.IsValid() && UnityEngine.SceneManagement.SceneManager.sceneCount > 1)
                Debug.LogWarning($"SDF level: {UnityEngine.SceneManagement.SceneManager.sceneCount} scenes are open while {levelRoot.gameObject.scene.name}'s navmesh is baked; " +
                                 "the colliders of the others are in it too. Close them and rebuild.");
            // The colliders where they are now (the builder just made them)
            Physics.SyncTransforms();

            var low = Mathf.Min(arena.west.spawn.position.y, arena.east.spawn.position.y) - BelowSpawn;
            var high = Mathf.Max(arena.west.spawn.position.y, arena.east.spawn.position.y) + AboveSpawn;
            var baked = 0;
            var boss = levelRoot.GetComponentInChildren<SdfLevel.PhaseGroup>(true) != null;
            var phases = boss ? new[] { 1, 2, 3 } : new[] { 0 };
            var agents = boss ? Agents.Append(BossAgent).ToArray() : Agents;
            foreach (var phase in phases)
            {
                if (phase > 0)
                    SdfLevel.PhaseGroup.ShowPhase(levelRoot, phase);
                foreach (var agent in agents)
                {
                    var id = EnsureAgentType(agent);
                    var surfaceObject = new GameObject(SurfaceName(agent.Name, phase));
                    surfaceObject.transform.SetParent(levelRoot, false);
                    surfaceObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    var surface = surfaceObject.AddComponent<NavMeshSurface>();
                    surface.agentTypeID = id;
                    surface.collectObjects = CollectObjects.Volume;
                    var middle = (playMin + playMax) / 2;
                    var size = playMax - playMin;
                    surface.center = new Vector3(middle.x, (low + high) / 2, middle.y);
                    surface.size = new Vector3(Mathf.Abs(size.x), high - low, Mathf.Abs(size.y));
                    surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
                    surface.layerMask = ~0;
                    surface.defaultArea = 0;
                    var serialized = new SerializedObject(surface);
                    serialized.FindProperty("m_GenerateLinks").boolValue = agent.Drop > 0;
                    serialized.ApplyModifiedPropertiesWithoutUndo();

                    surface.BuildNavMesh();
                    if (surface.navMeshData == null)
                    {
                        Debug.LogWarning($"SDF level: the {agent.Name} navmesh did not bake");
                        continue;
                    }
                    var path = $"{meshFolder}/{AssetName(agent.Name, phase)}";
                    if (AssetDatabase.LoadAssetAtPath<NavMeshData>(path) != null)
                        AssetDatabase.DeleteAsset(path);
                    surface.navMeshData.name = surfaceObject.name;
                    AssetDatabase.CreateAsset(surface.navMeshData, path);
                    baked++;
                    // The phases after the first are switched on by the boss's arena
                    if (phase > 1)
                        surfaceObject.SetActive(false);
                }
            }
            if (boss)
                SdfLevel.PhaseGroup.ShowPhase(levelRoot, 1);
            AssetDatabase.SaveAssets();
            return baked;
        }

        /// <summary>The navmesh area nothing walks: what is marked with it is an obstacle (Unity's built-in area 1).</summary>
        public const int NotWalkable = 1;

        /// <summary>Puts <paramref name="item"/>'s colliders, and its children's, into the bake as obstacles: not walkable.</summary>
        public static void MarkObstacle(GameObject item)
        {
            var modifier = item.GetComponent<NavMeshModifier>();
            if (modifier == null)
                modifier = item.AddComponent<NavMeshModifier>();
            modifier.ignoreFromBuild = false;
            modifier.overrideArea = true;
            modifier.area = NotWalkable;
            modifier.applyToChildren = true;
        }

        /// <summary>
        /// The id of the agent type with the agent's name in the project's navigation settings, made when missing and its numbers set to
        /// the agent's.
        /// </summary>
        public static int EnsureAgentType(Agent agent)
        {
            int? id = null;
            for (var i = 0; i < NavMesh.GetSettingsCount(); i++)
            {
                var settings = NavMesh.GetSettingsByIndex(i);
                if (NavMesh.GetSettingsNameFromID(settings.agentTypeID) == agent.Name)
                {
                    id = settings.agentTypeID;
                    break;
                }
            }
            var made = id == null;
            if (made)
                id = NavMesh.CreateSettings().agentTypeID;

            var project = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("NavMeshProjectSettings"));
            var all = project.FindProperty("m_Settings");
            var names = project.FindProperty("m_SettingNames");
            for (var i = 0; i < all.arraySize; i++)
            {
                var entry = all.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("agentTypeID").intValue != id.Value)
                    continue;
                if (i < names.arraySize)
                    names.GetArrayElementAtIndex(i).stringValue = agent.Name;
                entry.FindPropertyRelative("agentRadius").floatValue = agent.Radius;
                entry.FindPropertyRelative("agentHeight").floatValue = agent.Height;
                entry.FindPropertyRelative("agentClimb").floatValue = agent.Climb;
                entry.FindPropertyRelative("agentSlope").floatValue = agent.Slope;
                entry.FindPropertyRelative("ledgeDropHeight").floatValue = agent.Drop;
                entry.FindPropertyRelative("maxJumpAcrossDistance").floatValue = 0;
                break;
            }
            if (project.ApplyModifiedPropertiesWithoutUndo() || made)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"SDF level: navigation agent type {agent.Name} (id {id}) set: r {agent.Radius} m, {agent.Height} m high, " +
                          $"step {agent.Climb} m, slope {agent.Slope} degrees, drops {agent.Drop} m");
            }
            return id.Value;
        }

        /// <summary>The navmesh surfaces a level scene has, by agent name.</summary>
        public static IEnumerable<NavMeshSurface> SurfacesIn(Transform levelRoot) =>
            levelRoot.GetComponentsInChildren<NavMeshSurface>(true).Where(s => s.name.StartsWith(ObjectPrefix));
    }

    /// <summary>
    /// Checks the baked navmeshes of every battle level without opening their scenes (ticket 23): loads each level's scout navmesh on its
    /// own and asks it whether a bot gets from its spawn to the enemy stand and every pod, how much of the castles' upper floors (the
    /// wall-walks and tower rings, 4 to 6 m over the spawn) is joined to the ground, and how many spots of open ground on a 3 m grid it
    /// cannot reach (rock tops and the like); on a level with machines (ticket 24) also that the scouts' navmesh keeps off each turret,
    /// and that the tank navmesh holds each machine zone and joins the two. Tools > SDF Battle > Check Navmesh; the report goes to the
    /// console.
    /// </summary>
    public static class NavmeshCheck
    {
        [MenuItem("Tools/SDF Battle/Check Navmesh")]
        public static void CheckAll() => Debug.Log(Report());

        /// <summary>For batch mode (<c>-executeMethod SdfBattle.Editor.NavmeshCheck.CheckAllInBatch</c>): the report goes to the log.</summary>
        public static void CheckAllInBatch() => Debug.Log(Report());

        public static string Report()
        {
            var report = new StringBuilder("Navmesh check (ticket 23)\n");
            foreach (var level in SdfLevel.Editor.LevelBuilder.Levels())
                report.Append(Check(level));
            return report.ToString();
        }

        private static Vector3 Position(JToken entry)
        {
            var p = entry["position"].ToObject<float[]>();
            return new Vector3(-p[0], p[1], p[2]);
        }

        /// <summary>
        /// Ticket 24: each side's machine zone on the level's tank navmesh, and a tank's way from the west zone to the east one; returns
        /// the number of problems, with a line per finding in <paramref name="report"/>.
        /// </summary>
        private static int CheckTanks(string name, List<JToken> sides, StringBuilder report)
        {
            var asset = AssetDatabase.LoadAssetAtPath<NavMeshData>($"{SdfLevel.Editor.LevelBuilder.MeshFolder}/{name}/NavMesh {BotNavigation.TankAgent}.asset");
            if (asset == null)
            {
                report.Append("  no tank navmesh (rebuild the scene)\n");
                return 1;
            }
            var filter = new NavMeshQueryFilter { agentTypeID = BotNavigation.AgentTypeOf(BotNavigation.TankAgent) ?? 0, areaMask = NavMesh.AllAreas };
            var instance = NavMesh.AddNavMeshData(asset);
            try
            {
                var problems = 0;
                var zones = new List<Vector3>();
                foreach (var side in sides)
                {
                    var zone = Position(side["machines"]);
                    var radius = side["machines"].Value<float?>("radius") ?? 3;
                    var on = NavMesh.SamplePosition(zone, out var hit, radius, filter);
                    if (on)
                        zones.Add(hit.position);
                    else
                        problems++;
                    report.Append($"  {side.Value<string>("side")} machine zone: {(on ? "on the tank navmesh" : "NOT ON THE TANK NAVMESH")}\n");
                }
                if (zones.Count == 2)
                {
                    var path = new NavMeshPath();
                    var complete = NavMesh.CalculatePath(zones[0], zones[1], filter, path) && path.status == NavMeshPathStatus.PathComplete;
                    var length = 0f;
                    for (var i = 1; i < path.corners.Length; i++)
                        length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                    report.Append($"  a tank from the west machine zone to the east: {(complete ? $"reached, {length:0} m" : "NOT REACHED")}\n");
                    if (!complete)
                        problems++;
                }
                return problems;
            }
            finally
            {
                instance.Remove();
            }
        }

        /// <summary>
        /// Ticket 33.3: a boss level's navmeshes, one set per phase layout (Navigation Scout Phase 1 to 3, and the Tank and Boss agents
        /// the same). From the red spawn, on each phase's scout navmesh: the blue spawn, the blue flag stand (a phase 1 object: reported in
        /// the later phases, where the fallen balcony's ramp is over it, but not a problem there), every pod, the dais where Prime stands
        /// (up the stairs, and in phases 2 and 3 up the ramp), the crash site (phase 3: the wreck's top, where the crown is), and how much
        /// of the open floor on a 3 m grid is reached; the turrets stand off the navmesh; the machine zone is on the tank navmesh. On the
        /// Boss agent's phase 3 navmesh: Prime's way from the wreck to the red flag stand.
        /// </summary>
        private static string CheckBoss(JObject data, JObject battle, JObject boss, string name)
        {
            var report = new StringBuilder($"{name} (boss layout, one navmesh set per phase, from the red spawn):\n");
            var problems = 0;
            var folder = $"{SdfLevel.Editor.LevelBuilder.MeshFolder}/{name}";
            var sides = ((JArray)battle["sides"]).ToList();
            var spawns = sides.Select(s => Position(s["spawn"])).ToList();
            var floor = spawns.Average(p => p.y);
            var path = new NavMeshPath();

            (bool Reached, float Length) Way(NavMeshQueryFilter filter, Vector3 from, Vector3 to, float snap)
            {
                if (!NavMesh.SamplePosition(from, out var start, snap, filter) || !NavMesh.SamplePosition(to, out var end, snap, filter))
                    return (false, -1);
                if (!NavMesh.CalculatePath(start.position, end.position, filter, path))
                    return (false, -1);
                var length = 0f;
                for (var i = 1; i < path.corners.Length; i++)
                    length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                return (path.status == NavMeshPathStatus.PathComplete, length);
            }

            for (var phase = 1; phase <= 3; phase++)
            {
                report.Append($"  phase {phase}:\n");
                var scout = AssetDatabase.LoadAssetAtPath<NavMeshData>($"{folder}/{BattleNavmesh.AssetName(BotNavigation.ScoutAgent, phase)}");
                if (scout == null)
                {
                    report.Append("    no scout navmesh (rebuild the scene)\n");
                    problems++;
                    continue;
                }
                var filter = new NavMeshQueryFilter { agentTypeID = BotNavigation.AgentTypeOf(BotNavigation.ScoutAgent) ?? 0, areaMask = NavMesh.AllAreas };
                var instance = NavMesh.AddNavMeshData(scout);
                try
                {
                    var home = spawns[0];
                    void Target(string what, Vector3 where, bool required = true, float snap = 3)
                    {
                        var (reached, length) = Way(filter, home, where, snap);
                        if (!reached && required)
                            problems++;
                        report.Append($"    {what}: {(reached ? $"reached, {length:0} m" : length < 0 ? "NOT ON THE NAVMESH" : "NOT REACHED")}{(reached || required ? "" : " (not needed in this phase)")}\n");
                    }
                    Target("blue spawn", spawns[1]);
                    foreach (var side in sides)
                    {
                        var sideName = side.Value<string>("side");
                        // The blue flag stand is a phase 1 object; the crown's stand, the wreck's top, is the flag in phase 3
                        Target($"{sideName} flag stand", Position(side["flag"]), required: phase == 1 || sideName == "west");
                        foreach (var pod in (JArray)side["pods"])
                            Target($"{sideName} {pod.Value<string>("pod")} pod", Position(pod));
                    }
                    foreach (var pod in (JArray)battle["neutral"])
                        Target($"neutral {pod.Value<string>("pod")} pod", Position(pod));
                    // The dais: up the box's stairs in every phase, and up the ramp in phases 2 and 3
                    Target("the dais where Prime stands", Position(boss["prime"]), required: phase > 1, snap: 1.5f);
                    // The wreck's top, where the crown is in phase 3: the highest walkable spot over the crash site
                    if (phase == 3)
                    {
                        var crash = Position(boss["crash"]);
                        var top = NavMesh.SamplePosition(crash + Vector3.up * 6, out var topHit, 4, filter) ? topHit.position : crash;
                        Target($"the crown's place on the wreck at ({crash.x:0}, {crash.z:0}), {top.y - floor:0.0} m up", top, snap: 1);
                    }

                    // Open ground nobody reaches, on a 3 m grid of the play area, floor level only
                    var play = data["play"];
                    var min = play["min"].ToObject<float[]>();
                    var max = play["max"].ToObject<float[]>();
                    int ground = 0, unreached = 0;
                    var far = new List<string>();
                    for (var x = -max[0] + 1.5f; x < -min[0]; x += 3)
                    for (var z = min[1] + 1.5f; z < max[1]; z += 3)
                    {
                        if (!NavMesh.SamplePosition(new Vector3(x, floor + 1, z), out var hit, 2, filter) || hit.position.y > floor + 3)
                            continue;
                        ground++;
                        if (NavMesh.CalculatePath(home, hit.position, filter, path) && path.status == NavMeshPathStatus.PathComplete)
                            continue;
                        unreached++;
                        if (far.Count < 12)
                            far.Add($"({hit.position.x:0}, {hit.position.y:0.0}, {hit.position.z:0})");
                    }
                    report.Append($"    open ground: {ground - unreached} of {ground} grid spots reached{(far.Count > 0 ? "; not: " + string.Join(" ", far) : "")}\n");

                    // The turrets stand off the scouts' navmesh
                    foreach (var side in sides)
                        foreach (var turret in (side["turrets"] as JArray) ?? new JArray())
                        {
                            var at = Position(turret);
                            var clear = NavMesh.SamplePosition(at, out var nearest, 4, filter)
                                ? Vector2.Distance(new Vector2(at.x, at.z), new Vector2(nearest.position.x, nearest.position.z))
                                : float.PositiveInfinity;
                            report.Append($"    {side.Value<string>("side")} turret at ({at.x:0}, {at.z:0}): the navmesh {clear:0.0} m from its axis\n");
                            if (clear < 0.6f)
                                problems++;
                        }
                }
                finally
                {
                    instance.Remove();
                }

                // The tanks (phase 2 drops them): the machine zone is on the tank navmesh
                var tank = AssetDatabase.LoadAssetAtPath<NavMeshData>($"{folder}/{BattleNavmesh.AssetName(BotNavigation.TankAgent, phase)}");
                foreach (var side in sides.Where(s => s["machines"] is JObject))
                {
                    var zone = Position(side["machines"]);
                    var onTanks = false;
                    if (tank != null)
                    {
                        var tankInstance = NavMesh.AddNavMeshData(tank);
                        try
                        {
                            onTanks = NavMesh.SamplePosition(zone, out _, side["machines"].Value<float?>("radius") ?? 3,
                                new NavMeshQueryFilter { agentTypeID = BotNavigation.AgentTypeOf(BotNavigation.TankAgent) ?? 0, areaMask = NavMesh.AllAreas });
                        }
                        finally
                        {
                            tankInstance.Remove();
                        }
                    }
                    if (!onTanks)
                        problems++;
                    report.Append($"    {side.Value<string>("side")} machine zone: {(onTanks ? "on the tank navmesh" : "NOT ON THE TANK NAVMESH")}\n");
                }
            }

            // Prime on foot (phase 3): from the wreck to the red flag stand, on the Boss agent's navmesh
            var bossMesh = AssetDatabase.LoadAssetAtPath<NavMeshData>($"{folder}/{BattleNavmesh.AssetName(BotNavigation.BossAgent, 3)}");
            if (bossMesh == null)
            {
                report.Append("  no Boss navmesh for phase 3 (rebuild the scene)\n");
                problems++;
            }
            else
            {
                var bossFilter = new NavMeshQueryFilter { agentTypeID = BotNavigation.AgentTypeOf(BotNavigation.BossAgent) ?? 0, areaMask = NavMesh.AllAreas };
                var instance = NavMesh.AddNavMeshData(bossMesh);
                try
                {
                    // The wreck (ticket 33.5) stands on the crash site, 8 m across: Prime's navmesh starts at its edge, up to 1.8 m (his radius)
                    // and 3.9 m (its half width) from its middle
                    var (reached, length) = Way(bossFilter, Position(boss["crash"]), Position(sides[0]["flag"]), 8);
                    if (!reached)
                        problems++;
                    report.Append($"  Boss agent, phase 3, from the wreck to the red flag stand: {(reached ? $"reached, {length:0} m" : length < 0 ? "NOT ON THE NAVMESH" : "NOT REACHED")}\n");
                }
                finally
                {
                    instance.Remove();
                }
            }
            report.Append($"  {(problems == 0 ? "OK" : problems + " PROBLEMS")}\n");
            return report.ToString();
        }

        /// <summary>One level's report: a line per finding.</summary>
        public static string Check(string level)
        {
            var report = new StringBuilder();
            var data = JObject.Parse(File.ReadAllText($"{level}/{SdfLevel.Editor.LevelBuilder.LevelJson}"));
            var name = data.Value<string>("name") ?? Path.GetFileName(level);
            if (!(data["battle"] is JObject battle))
                return $"{name}: no battle\n";
            // Ticket 33.3: the boss's arena has a navmesh per phase layout
            if (data["boss"] is JObject bossData)
                return CheckBoss(data, battle, bossData, name);
            var asset = AssetDatabase.LoadAssetAtPath<NavMeshData>($"{SdfLevel.Editor.LevelBuilder.MeshFolder}/{name}/NavMesh {BotNavigation.ScoutAgent}.asset");
            if (asset == null)
                return $"{name}: no scout navmesh (rebuild the scene)\n";
            var agent = BotNavigation.AgentTypeOf(BotNavigation.ScoutAgent) ?? 0;
            var filter = new NavMeshQueryFilter { agentTypeID = agent, areaMask = NavMesh.AllAreas };
            var instance = NavMesh.AddNavMeshData(asset);
            var path = new NavMeshPath();
            try
            {
                var sides = ((JArray)battle["sides"]).ToList();
                var spawns = sides.Select(s => Position(s["spawn"])).ToList();
                if (!NavMesh.SamplePosition(spawns[0], out var home, 3, filter))
                    return $"{name}: the west spawn is not on the navmesh\n";

                (bool complete, float length) Way(Vector3 to)
                {
                    if (!NavMesh.SamplePosition(to, out var end, 3, filter))
                        return (false, -1);
                    if (!NavMesh.CalculatePath(home.position, end.position, filter, path))
                        return (false, -1);
                    var corners = path.corners;
                    var length = 0f;
                    for (var i = 1; i < corners.Length; i++)
                        length += Vector3.Distance(corners[i - 1], corners[i]);
                    return (path.status == NavMeshPathStatus.PathComplete, length);
                }

                var problems = 0;
                void Target(string what, Vector3 where)
                {
                    var (complete, length) = Way(where);
                    if (!complete)
                        problems++;
                    report.Append($"  {what}: {(complete ? $"reached, {length:0} m" : length < 0 ? "NOT ON THE NAVMESH" : "NOT REACHED")}\n");
                }
                report.Append($"{name} (from the west spawn):\n");
                foreach (var side in sides)
                {
                    var sideName = side.Value<string>("side");
                    Target($"{sideName} spawn", Position(side["spawn"]));
                    Target($"{sideName} flag stand", Position(side["flag"]));
                    foreach (var pod in (JArray)side["pods"])
                        Target($"{sideName} {pod.Value<string>("pod")} pod", Position(pod));
                }
                foreach (var pod in (JArray)battle["neutral"])
                    Target($"neutral {pod.Value<string>("pod")} pod", Position(pod));

                // The castles' upper floors: navmesh 4 to 6 m over the spawns, within 16 m of each castle's flag stand
                var floor = spawns.Average(p => p.y);
                foreach (var side in sides)
                {
                    var stand = Position(side["flag"]);
                    int found = 0, joined = 0;
                    for (var x = -16f; x <= 16; x += 1.5f)
                    for (var z = -16f; z <= 16; z += 1.5f)
                    {
                        var probe = new Vector3(stand.x + x, floor + 5, stand.z + z);
                        if (!NavMesh.SamplePosition(probe, out var hit, 0.75f, filter) || hit.position.y < floor + 3.5f)
                            continue;
                        found++;
                        if (NavMesh.CalculatePath(home.position, hit.position, filter, path) && path.status == NavMeshPathStatus.PathComplete)
                            joined++;
                    }
                    report.Append($"  {side.Value<string>("side")} castle, upper floors: {joined} of {found} spots joined to the ground\n");
                    // A side with no castle by its stand (the computer has none; the chessboard's card castles stand off at the edge, 70 m
                    // from the stands) has no upper floors to join
                    var hasCastle = ((JArray)data["placements"]).Any(p => p.Value<string>("model")?.Contains("castle") == true &&
                                                                          Vector2.Distance(new Vector2(stand.x, stand.z), new Vector2(Position(p).x, Position(p).z)) < 30);
                    if ((found == 0 && hasCastle) || joined < found * 0.8f)
                        problems++;
                }

                // Open ground nobody reaches, on a 3 m grid of the play area
                var play = data["play"];
                var min = play["min"].ToObject<float[]>();
                var max = play["max"].ToObject<float[]>();
                int ground = 0, unreached = 0;
                var far = new List<string>();
                for (var x = -max[0] + 1.5f; x < -min[0]; x += 3)
                for (var z = min[1] + 1.5f; z < max[1]; z += 3)
                {
                    var probe = new Vector3(x, floor + 1, z);
                    if (!NavMesh.SamplePosition(probe, out var hit, 2, filter) || hit.position.y > floor + 3)
                        continue;
                    ground++;
                    if (NavMesh.CalculatePath(home.position, hit.position, filter, path) && path.status == NavMeshPathStatus.PathComplete)
                        continue;
                    unreached++;
                    if (far.Count < 12)
                        far.Add($"({hit.position.x:0}, {hit.position.y:0.0}, {hit.position.z:0})");
                }
                report.Append($"  open ground: {ground - unreached} of {ground} grid spots reached{(far.Count > 0 ? "; not: " + string.Join(" ", far) : "")}\n");

                // Ticket 24: the turrets stand off the scouts' navmesh (obstacles, paths go round), and each machine zone is on the tanks'
                // navmesh, joined to the other side's across the field
                foreach (var side in sides)
                    foreach (var turret in (side["turrets"] as JArray) ?? new JArray())
                    {
                        var at = Position(turret);
                        var clear = NavMesh.SamplePosition(at, out var nearest, 4, filter)
                            ? Vector2.Distance(new Vector2(at.x, at.z), new Vector2(nearest.position.x, nearest.position.z))
                            : float.PositiveInfinity;
                        report.Append($"  {side.Value<string>("side")} turret at ({at.x:0}, {at.z:0}): the navmesh {clear:0.0} m from its axis\n");
                        if (clear < 0.6f)
                            problems++;
                    }
                // Ticket 28.11: a level with heat has each ring as its Burner area on the scouts' navmesh, and no navmesh a little past the play
                // area's edge or in a sink placed in it (nothing to walk onto, no drop link off the worktop)
                foreach (var ring in (data["heat"]?["rings"] as JArray) ?? new JArray())
                {
                    var at = new Vector3(-ring["at"][0].Value<float>(), floor, ring["at"][1].Value<float>());
                    var group = ring.Value<int>("group");
                    var area = NavMesh.GetAreaFromName(group <= 0 ? "Burner" : $"Burner {group + 1}");
                    var found = NavMesh.SamplePosition(at, out var ringHit, 1.5f, filter);
                    var onRing = found && ringHit.mask == 1 << area;
                    report.Append($"  heat ring {ring.Value<string>("name")} at ({at.x:0.#}, {at.z:0.#}): {(onRing ? "Burner area" : "NOT the Burner area (mask " + (found ? ringHit.mask : -1) + ")")}\n");
                    if (!onRing)
                        problems++;
                }
                if (data["heat"] != null)
                {
                    var half = new Vector2(max[0] - min[0], max[1] - min[1]) / 2;
                    var middle = new Vector2((max[0] + min[0]) / 2, (max[1] + min[1]) / 2);
                    var outside = new[] { new Vector3(middle.x + half.x + 1.5f, floor, middle.y), new Vector3(middle.x - half.x - 1.5f, floor, middle.y),
                        new Vector3(middle.x, floor, middle.y + half.y + 1.5f), new Vector3(middle.x, floor, middle.y - half.y - 1.5f) };
                    var offEdge = outside.Count(point => NavMesh.SamplePosition(point, out _, 0.75f, filter));
                    report.Append($"  past the play area's edge: {offEdge} of {outside.Length} probes on the navmesh\n");
                    problems += offEdge;
                    foreach (var sink in ((JArray)data["placements"]).Where(p => p.Value<string>("model")?.StartsWith("sink") == true))
                    {
                        var at = new Vector3(-sink["position"][0].Value<float>(), floor - 6, sink["position"][2].Value<float>());
                        var inBowl = NavMesh.SamplePosition(at, out var bowlHit, 6, filter) && bowlHit.position.y < floor - 1;
                        report.Append($"  sink at ({at.x:0.#}, {at.z:0.#}): {(inBowl ? "NAVMESH IN THE BOWL at y " + bowlHit.position.y.ToString("0.#") : "no navmesh in the bowl")}\n");
                        if (inBowl)
                            problems++;
                    }
                }
                if (sides.All(side => side["machines"] is JObject))
                    problems += CheckTanks(name, sides, report);

                var triangulation = NavMesh.CalculateTriangulation();
                report.Append($"  {(problems == 0 ? "OK" : problems + " PROBLEMS")}; the loaded navmeshes have {triangulation.indices.Length / 3:N0} triangles\n");
                return report.ToString();
            }
            finally
            {
                instance.Remove();
            }
        }
    }

    /// <summary>Ticket 23's menu: watching the bots play.</summary>
    public static class BotMenu
    {
        private const string SpectateItem = "Tools/SDF Battle/Spectate Bots";

        /// <summary>
        /// On: entering Play mode in a battle scene you are no player; the camera follows the eight bots (Tab: next, F: free camera).
        /// </summary>
        [MenuItem(SpectateItem)]
        private static void ToggleSpectate() => EditorPrefs.SetBool(Match.SpectatePreference, !EditorPrefs.GetBool(Match.SpectatePreference, false));

        [MenuItem(SpectateItem, true)]
        private static bool ToggleSpectateCheck()
        {
            Menu.SetChecked(SpectateItem, EditorPrefs.GetBool(Match.SpectatePreference, false));
            return true;
        }
    }
}
