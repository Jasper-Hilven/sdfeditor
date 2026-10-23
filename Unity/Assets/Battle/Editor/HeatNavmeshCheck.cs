using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using SdfLevel.Editor;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace SdfBattle.Editor
{
    /// <summary>
    /// A check of the way the hob's rings get into the bots' navmesh (ticket 28.10), on a small floor made and thrown away here in the open scene:
    /// with one ring marked as the Burner area by <see cref="LevelHeat.AddMarker"/>, baked for the scouts' agent type. It asks the navmesh
    /// whether the ring's floor is the Burner area and the floor round it is not, that a way across the ring costs no more than the metres while
    /// the ring is cold, and that the way is round the ring (and longer) once <see cref="NavAreaCosts"/> make the ring cost 50 times as much.
    /// Tools > SDF Battle > Check Hob Navmesh; the report goes to the console.
    /// </summary>
    public static class HeatNavmeshCheck
    {
        [MenuItem("Tools/SDF Battle/Check Hob Navmesh")]
        public static void Check() => Debug.Log(Report());

        /// <summary>For batch mode (<c>-executeMethod SdfBattle.Editor.HeatNavmeshCheck.CheckInBatch</c>): the report goes to the log, and a failure exits with 1.</summary>
        public static void CheckInBatch()
        {
            var report = Report(out var problems);
            Debug.Log(report);
            if (problems > 0)
                EditorApplication.Exit(1);
        }

        public static string Report() => Report(out _);

        public static string Report(out int problems)
        {
            var report = new StringBuilder("Hob navmesh check (ticket 28.10)\n");
            problems = 0;
            var scene = SceneManager.GetActiveScene();
            var area = LevelHeat.EnsureArea(HeatHazard.AreaName(0));
            var agent = BattleNavmesh.EnsureAgentType(BattleNavmesh.Agents[0]);
            var root = new GameObject("Hob check");
            // A floor 60 x 40 m with its top at y = 0, and a ring of 5.4 m at (0, 0) across the way from west to east
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(root.transform, false);
            floor.transform.localScale = new Vector3(60, 1, 40);
            floor.transform.localPosition = new Vector3(0, -0.5f, 0);
            var marker = LevelHeat.AddMarker(scene, Vector3.zero, 5.4f, area, root.transform);
            var surface = root.AddComponent<NavMeshSurface>();
            surface.agentTypeID = agent;
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            Physics.SyncTransforms();
            try
            {
                surface.BuildNavMesh();
                var filter = NavAreaCosts.Filter(agent);

                // The floor is the Burner area inside the ring and walkable outside it
                var inside = Mask(new Vector3(1, 0, 1), filter);
                var outside = Mask(new Vector3(8, 0, 1), filter);
                report.AppendLine($"  inside the ring: mask {inside} (Burner is {1 << area}); outside: mask {outside} (Walkable is 1)");
                problems += Expect(report, inside == 1 << area, "the floor inside the ring is the Burner area");
                problems += Expect(report, outside == 1, "the floor outside the ring is plain walkable");
                problems += Expect(report, Mask(new Vector3(5.1f, 0, 0), filter) == 1 << area, "the ring reaches its radius (5.1 m)");
                problems += Expect(report, Mask(new Vector3(5.9f, 0, 0), filter) == 1, "the ring does not reach past its radius (5.9 m)");

                // Cold: straight across; warm: round it
                var from = new Vector3(-12, 0, 0);
                var to = new Vector3(12, 0, 0);
                var cold = Length(from, to, filter, out var coldStraight);
                NavAreaCosts.Set(area, 50);
                var hot = Length(from, to, NavAreaCosts.Filter(agent), out var hotStraight);
                NavAreaCosts.Set(area, 1);
                report.AppendLine($"  way across at cost 1: {cold:0.0} m, through the ring: {coldStraight}; at cost 50: {hot:0.0} m, through the ring: {hotStraight}");
                problems += Expect(report, cold > 0 && Mathf.Abs(cold - 24) < 0.5f && coldStraight, "a cold ring is crossed straight (24 m)");
                problems += Expect(report, hot > 24.5f && hot < 40 && !hotStraight, "a hot ring is gone round (longer than 24 m, shorter than twice)");
                // Standing in the middle of a hot ring: the way out is short
                NavAreaCosts.Set(area, 50);
                var leave = Length(Vector3.zero, new Vector3(9, 0, 0), NavAreaCosts.Filter(agent), out _);
                NavAreaCosts.Set(area, 1);
                report.AppendLine($"  from the ring's middle to 9 m east at cost 50: {leave:0.0} m");
                problems += Expect(report, leave > 0 && leave < 20, "a bot in the middle of a hot ring still finds a way out to a place 9 m off (under 20 m)");
            }
            finally
            {
                NavAreaCosts.Reset();
                LevelHeat.Finish(new[] { marker });
                surface.RemoveData();
                Object.DestroyImmediate(root);
            }
            problems += BuilderCheck(report);
            report.AppendLine(problems == 0 ? "  all fine" : $"  {problems} problem(s)");
            return report.ToString();
        }

        /// <summary>The kitchen's level.json "heat" as the bake writes it (ticket 28.10): what the level builder reads.</summary>
        private const string KitchenHeat = "{\"heat\": {\"cycle\": 45, \"first\": 20, \"warm\": 3, \"hot\": 12, \"cool\": 3, \"damage\": 10, \"cost\": 50, \"cause\": \"the hob\", " +
            "\"groups\": [\"back\", \"front\"], \"rings\": [" +
            "{\"group\": 0, \"name\": \"back\", \"at\": [-10.5, 3], \"radius\": 5.4, \"y\": 0}, {\"group\": 0, \"name\": \"back\", \"at\": [10.5, 3], \"radius\": 5.4, \"y\": 0}, " +
            "{\"group\": 1, \"name\": \"front\", \"at\": [-10.5, -15], \"radius\": 4.2, \"y\": 0}, {\"group\": 1, \"name\": \"front\", \"at\": [10.5, -15], \"radius\": 4.2, \"y\": 0}]}}";

        /// <summary>The level builder's part (<see cref="LevelHeat.Add"/>) on the kitchen's heat and a cube at each ring's centre: the object, the rules, the glow and the markers.</summary>
        private static int BuilderCheck(StringBuilder report)
        {
            var problems = 0;
            var scene = SceneManager.GetActiveScene();
            var burners = new GameObject[4];
            var placed = new System.Collections.Generic.List<SdfLevel.IMMScene.Placement>();
            // The file's x is mirrored in Unity: the ring at file (-10.5, 3) is at Unity (10.5, 3)
            var centres = new[] { new Vector3(10.5f, 0, 3), new Vector3(-10.5f, 0, 3), new Vector3(10.5f, 0, -15), new Vector3(-10.5f, 0, -15) };
            for (var i = 0; i < burners.Length; i++)
            {
                burners[i] = GameObject.CreatePrimitive(PrimitiveType.Cube);
                burners[i].transform.position = centres[i];
                placed.Add(new SdfLevel.IMMScene.Placement { model = i < 2 ? "burner-large" : "burner-small", transform = burners[i].transform });
            }
            var kettle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            placed.Add(new SdfLevel.IMMScene.Placement { model = "kettle0", transform = kettle.transform });
            var markers = LevelHeat.Add(scene, JObject.Parse(KitchenHeat), placed);
            var hazard = Object.FindFirstObjectByType<HeatHazard>();
            try
            {
                problems += Expect(report, markers.Count == 4, "four marker discs for the bake");
                problems += Expect(report, hazard != null && hazard.name == HeatHazard.ObjectName, "a Heat object with the HeatHazard");
                if (hazard != null)
                {
                    problems += Expect(report, hazard.rings.Length == 4 && hazard.rules.groups == 2 && hazard.rules.cause == "the hob" && hazard.rules.damage == 10 && hazard.rules.cost == 50,
                        "the rules and four rings are read");
                    problems += Expect(report, hazard.rings[0].centre == new Vector2(10.5f, 3) && hazard.rings[0].radius == 5.4f, "the first ring is at Unity (10.5, 3), the file's (-10.5, 3)");
                    problems += Expect(report, hazard.glows.All(g => g.renderers.Length == 1), "each ring glows with the one model at its centre, and the kettle with none");
                    problems += Expect(report, hazard.glows[0].renderers[0].gameObject == burners[0], "the model at the ring's centre is the ring's");
                }
                problems += Expect(report, markers.Select(m => m.GetComponent<NavMeshModifier>().area).Distinct().Count() == 2, "two navmesh areas, one per group");
            }
            finally
            {
                LevelHeat.Finish(markers);
                foreach (var burner in burners)
                    Object.DestroyImmediate(burner);
                Object.DestroyImmediate(kettle);
                if (hazard != null)
                    Object.DestroyImmediate(hazard.gameObject);
            }
            return problems;
        }

        private static int Mask(Vector3 at, NavMeshQueryFilter filter) =>
            NavMesh.SamplePosition(at, out var hit, 0.5f, filter) ? hit.mask : 0;

        /// <summary>The length of the way and whether it passes through the ring (a corner or a stretch of it inside 4.8 m of the middle).</summary>
        private static float Length(Vector3 from, Vector3 to, NavMeshQueryFilter filter, out bool throughRing)
        {
            throughRing = false;
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(from, to, filter, path) || path.status != NavMeshPathStatus.PathComplete)
                return 0;
            var length = 0f;
            var corners = path.corners;
            for (var i = 0; i + 1 < corners.Length; i++)
            {
                length += Vector3.Distance(corners[i], corners[i + 1]);
                for (var s = 0; s <= 20; s++)
                    if (Vector3.Lerp(corners[i], corners[i + 1], s / 20f).magnitude < 4.8f)
                        throughRing = true;
            }
            return length;
        }

        private static int Expect(StringBuilder report, bool ok, string what)
        {
            report.AppendLine(ok ? $"  ok: {what}" : $"  FAILED: {what}");
            return ok ? 0 : 1;
        }
    }
}
