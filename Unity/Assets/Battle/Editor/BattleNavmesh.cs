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
        /// Bakes the battle scene's navmesh for every agent type; returns how many were baked (0 for a scene with no battle).
        /// <paramref name="levelRoot"/> holds the level's colliders, <paramref name="playMin"/> and <paramref name="playMax"/> are the play
        /// area's corners (x, z in Unity's coordinates), <paramref name="ignored"/> what is no ground (the player, the battle objects).
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
            // The colliders where they are now (the builder just made them)
            Physics.SyncTransforms();

            var low = Mathf.Min(arena.west.spawn.position.y, arena.east.spawn.position.y) - BelowSpawn;
            var high = Mathf.Max(arena.west.spawn.position.y, arena.east.spawn.position.y) + AboveSpawn;
            var baked = 0;
            foreach (var agent in Agents)
            {
                var id = EnsureAgentType(agent);
                var surfaceObject = new GameObject(ObjectPrefix + agent.Name);
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
                var path = $"{meshFolder}/NavMesh {agent.Name}.asset";
                if (AssetDatabase.LoadAssetAtPath<NavMeshData>(path) != null)
                    AssetDatabase.DeleteAsset(path);
                surface.navMeshData.name = surfaceObject.name;
                AssetDatabase.CreateAsset(surface.navMeshData, path);
                baked++;
            }
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

        /// <summary>One level's report: a line per finding.</summary>
        public static string Check(string level)
        {
            var report = new StringBuilder();
            var data = JObject.Parse(File.ReadAllText($"{level}/{SdfLevel.Editor.LevelBuilder.LevelJson}"));
            var name = data.Value<string>("name") ?? Path.GetFileName(level);
            if (!(data["battle"] is JObject battle))
                return $"{name}: no battle\n";
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
                    if (found == 0 || joined < found * 0.8f)
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
