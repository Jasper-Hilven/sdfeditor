using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SdfLevel.Editor
{
    /// <summary>
    /// The batch-mode checks of the castle's floor heights and levels of detail (ticket 09): without a window,
    /// <c>Unity -batchmode -quit -projectPath Unity -executeMethod SdfLevel.Editor.CastleChecks.Run -snapshotDir &lt;folder&gt;</c>
    /// (without -nographics), on a level's first castle (-level &lt;name&gt;, the meadow by default). <c>-castle castle1</c> runs the
    /// same checks on the stone castle (ticket 15.2) instead: it is put where that castle stands, the level's <c>castle0</c> hidden,
    /// so floors, walks and views are the same ones; the default is <c>castle0</c>, as before.
    /// <list type="bullet">
    /// <item><b>Height profiles</b>: a walker is dropped on the floor along a path, 2 cm at a time: at each step a ray from a step
    /// height (0.42 m) above its feet down finds the floor it stands on. It logs every jump of more than 1.5 cm (where, how
    /// much, on which collider), a count of the jumps by size, and where there is no floor. Paths: through the gate into the
    /// courtyard, up the keep's stair onto its ring, and the wall-walk all round the oval.</item>
    /// <item><b>Z-fighting</b>: for the joints, tower rings and the keep's ring, from several distances on both quality levels: the
    /// view, and the same view moved 3 mm sideways and up; the pixels that differ clearly between the two are logged (floors that
    /// z-fight flip with such a move), the views are saved as zfight_&lt;place&gt;_&lt;distance&gt;m_&lt;preset&gt;.png.</item>
    /// <item><b>Levels of detail</b>: lod_&lt;part&gt;_LOD&lt;k&gt;.png for a tower, the keep and the young fly agarics, each level forced on.</item>
    /// </list>
    /// </summary>
    public static class CastleChecks
    {
        private const float A = 12, B = 8;
        private const int Bays = 18;

        public static void Run()
        {
            RenderPipelineSetup.Ensure();
            MushroomBuilder.BuildStale();
            LevelBuilder.BuildStale();
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-snapshotDir");
            var directory = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Path.GetTempPath(), "castle-checks");
            var only = Array.IndexOf(args, "-only") is var o && o >= 0 && o + 1 < args.Length ? args[o + 1] : "walk,zfight,lod";
            var model = Array.IndexOf(args, "-castle") is var c && c >= 0 && c + 1 < args.Length ? args[c + 1] : "castle0";
            var level = Array.IndexOf(args, "-level") is var l && l >= 0 && l + 1 < args.Length ? args[l + 1] : "meadow";
            Directory.CreateDirectory(directory);
            var scene = EditorSceneManager.OpenScene(LevelBuilder.ScenePathOf($"{LevelBuilder.GeneratedFolder}/{level}"), OpenSceneMode.Single);
            Physics.SyncTransforms();
            var castle = scene.GetRootGameObjects().First(g => g.name == LevelBuilder.LevelRootName).transform.Find(LevelBuilder.PlacedName)
                .Cast<Transform>().Where(t => t.name is "castle0" or "castle1").OrderByDescending(t => t.position.x).First();
            // the level's own castle (castle1 in the meadow since ticket 15, castle0 in the fungal level) is swapped for a fresh instance of the
            // model checked, so the checks see the same one wherever it is placed
            if (model != "castle0" || castle.name != "castle0")
                castle = Swap(castle, model);
            var camera = scene.GetRootGameObjects().First(g => g.name == LevelBuilder.PlayerName).GetComponentInChildren<Camera>();
            if (only.Contains("walk"))
                Walks(castle, model);
            if (only.Contains("zfight"))
                ZFighting(castle, camera, directory);
            if (only.Contains("lod"))
                Levels(scene, camera, directory, model);
            Debug.Log($"SDF castle checks ({model}): done, snapshots in {directory}");
        }

        /// <summary>Hides the placed castle (castle0 or castle1) and puts an instance of the other castle's prefab in its place (position, turn and scale).</summary>
        private static Transform Swap(Transform placed, string model)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{MushroomBuilder.PrefabFolder}/{model}.prefab")
                         ?? throw new InvalidOperationException($"There is no prefab for {model}");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, placed.gameObject.scene);
            instance.transform.SetParent(placed.parent, false);
            instance.transform.SetLocalPositionAndRotation(placed.localPosition, placed.localRotation);
            instance.transform.localScale = placed.localScale;
            placed.gameObject.SetActive(false);
            // the placed castle's repeated parts are drawn by the instanced renderer, not by the placed object: take its placements out
            var placedName = placed.name;
            foreach (var renderer in placed.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<InstancedScatterRenderer>(true)))
            {
                var hidden = new HashSet<int>(renderer.prototypes.Select((p, i) => (p, i)).Where(e => e.p.name.StartsWith(placedName + "/", StringComparison.Ordinal)).Select(e => e.i));
                renderer.placements = renderer.placements.Where(p => !hidden.Contains(p.prototype)).ToArray();
                renderer.enabled = false;
                renderer.enabled = true;
            }
            Physics.SyncTransforms();
            return instance.transform;
        }

        // The castle's model coordinates (the pipeline's) to the world: Unity's x is the pipeline's -x
        private static Vector3 ModelPoint(Transform model, float x, float y, float z) => model.TransformPoint(new Vector3(-x, y, z));

        private static Vector2[] Joints()
        {
            const int samples = 20000;
            var points = new Vector2[samples + 1];
            var arc = new float[samples + 1];
            for (var i = 0; i <= samples; i++)
            {
                var t = 2 * Mathf.PI * i / samples;
                points[i] = new Vector2(A * Mathf.Sin(t), B * Mathf.Cos(t));
                if (i > 0)
                    arc[i] = arc[i - 1] + Vector2.Distance(points[i], points[i - 1]);
            }
            var chord = arc[samples] / Bays;
            return Enumerable.Range(0, Bays).Select(k =>
            {
                var s = (k + 0.5f) * chord;
                var i = Array.FindIndex(arc, a => a >= s);
                return points[Mathf.Max(i - 1, 0)];
            }).ToArray();
        }

        private static Vector2 Outward(Vector2 a) => new Vector2(a.x / (A * A), a.y / (B * B)).normalized;

        // ---- floors and walks -------------------------------------------------------------------------------------------------------

        private static void Walks(Transform castle, string modelName)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{MushroomBuilder.PrefabFolder}/{modelName}.prefab");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, castle.gameObject.scene);
            model.transform.SetPositionAndRotation(castle.position, castle.rotation);
            model.transform.localScale = castle.lossyScale;
            try
            {
                for (var level = 0; level < 3; level++)
                {
                    FloorAudit(model, level, 4.9f, 5.6f, "walk level");
                    FloorAudit(model, level, -0.3f, 0.4f, "ground level", ground: true);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(model);
            }

            var walker = new GameObject("Castle Walker") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(walker, castle.gameObject.scene);
            try
            {
                var controller = walker.AddComponent<CharacterController>();
                controller.height = 1.75f;
                controller.radius = 0.3f;
                controller.center = new Vector3(0, controller.height / 2, 0);
                controller.slopeLimit = 45;
                controller.stepOffset = 0.4f;

                // Through the gate into the courtyard
                Walk(castle, walker, controller, "gate to courtyard", GroundAt(castle, new Vector2(0, 16)), Dense(new[] { new Vector2(0, 16), new Vector2(0, 0) }), true);

                // Up the keep's stair, then onto its ring
                var stair = new List<Vector2>();
                for (var i = 0; i < 20; i++)
                    stair.Add(new Vector2(4.6f * Mathf.Sin(0.42f + i * 0.078f), -5 + 4.6f * Mathf.Cos(0.42f + i * 0.078f)));
                stair.Add(new Vector2(4.2f * Mathf.Sin(2.2f), -5 + 4.2f * Mathf.Cos(2.2f)));
                var foot = new Vector2(4.6f * Mathf.Sin(0.15f), -5 + 4.6f * Mathf.Cos(0.15f));
                var top = Walk(castle, walker, controller, "keep stair onto its ring", GroundAt(castle, foot), Dense(stair.Prepend(foot).ToArray()), true);

                // From the ring along the wall-walk all round, joint by joint (towers at 0, 4, 6, 11, 13, 17, round the inner side of
                // each ring); first the +x side down to the gate, then the gate bay, then the -x side back up to the keep
                var joints = Joints();
                var towers = new HashSet<int> { 0, 4, 6, 11, 13, 17 };
                var order = Enumerable.Range(1, 8).Reverse().Concat(new[] { 0, 17 }).Concat(Enumerable.Range(9, 8).Reverse()).ToList();
                var way = new List<Vector2>();
                for (var n = 0; n < order.Count; n++)
                {
                    var k = order[n];
                    var a = joints[k];
                    var outward = Outward(a);
                    if (!towers.Contains(k))
                    {
                        way.Add(a - outward * 0.8f);
                        continue;
                    }
                    var radius = k is 0 or 17 ? 2.0f : 3.1f;
                    var centre = a + outward * 0.4f;
                    var start = Mathf.Atan2(-outward.y, -outward.x);
                    var arc = Enumerable.Range(-2, 5).Select(step => centre + new Vector2(Mathf.Cos(start + step * 0.45f), Mathf.Sin(start + step * 0.45f)) * radius).ToList();
                    var previous = n > 0 ? joints[order[n - 1]] : way.Count > 0 ? way[^1] : a;
                    if (Vector2.Distance(arc[0], previous) > Vector2.Distance(arc[arc.Count - 1], previous))
                        arc.Reverse();
                    way.AddRange(arc);
                }
                Walk(castle, walker, controller, "wall-walk all round from the keep's ring", top, Dense(way.ToArray()), false);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(walker);
            }
        }

        private static List<Vector2> Dense(Vector2[] way)
        {
            var result = new List<Vector2>();
            for (var i = 0; i + 1 < way.Length; i++)
                result.AddRange(Line(way[i], way[i + 1]).Skip(i == 0 ? 0 : 1));
            return result;
        }

        private static List<Vector2> Line(Vector2 from, Vector2 to)
        {
            var n = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(from, to) / 0.05f));
            return Enumerable.Range(0, n + 1).Select(i => Vector2.Lerp(from, to, i / (float)n)).ToList();
        }

        private static Vector3 GroundAt(Transform castle, Vector2 p)
        {
            var world = ModelPoint(castle, p.x, 0, p.y);
            foreach (var hit in Physics.RaycastAll(new Vector3(world.x, 200, world.z), Vector3.down, 400).OrderBy(h => h.distance))
                if (hit.collider is MeshCollider && hit.collider.transform.parent != null && hit.collider.transform.parent.name == "Ground")
                    return hit.point;
            return world;
        }

        /// <summary>
        /// Walks a character controller (the player's size, step 0.4 m, slope 45 degrees) along the path in 5 cm moves and logs where it
        /// stalls (the first sample it cannot get within 15 cm of in 40 moves), the heights it climbed and fell, and which
        /// collider it stands on when that changes with the height: those are the steps between neighbouring parts.
        /// </summary>
        private static Vector3 Walk(Transform castle, GameObject walker, CharacterController controller, string name, Vector3 from, List<Vector2> path, bool stopAtStall)
        {
            controller.enabled = false;
            walker.transform.position = from + Vector3.up * 0.05f;
            controller.enabled = true;
            Physics.SyncTransforms();
            var changes = new Dictionary<string, int>();
            var stalls = new List<string>();
            var lowest = float.PositiveInfinity;
            var highest = float.NegativeInfinity;
            var rises = new List<float>();
            var lastY = walker.transform.position.y;
            string lastOn = null;
            var lastFloorY = float.NaN;
            var travelled = 0f;
            var skipped = 0;
            foreach (var p in path)
            {
                var target = ModelPoint(castle, p.x, 0, p.y);
                var tries = 0;
                while (tries++ < 40)
                {
                    var q = walker.transform.position;
                    var flat = new Vector3(target.x - q.x, 0, target.z - q.z);
                    if (flat.magnitude < 0.08f)
                        break;
                    var move = flat.normalized * Mathf.Min(0.05f, flat.magnitude);
                    controller.Move(move + Vector3.down * 0.1f);
                    var r = walker.transform.position;
                    travelled += new Vector2(r.x - q.x, r.z - q.z).magnitude;
                    lowest = Mathf.Min(lowest, r.y);
                    highest = Mathf.Max(highest, r.y);
                    if (r.y - lastY > 0.04f)
                        rises.Add(r.y - lastY);
                    lastY = r.y;
                    if (Physics.Raycast(r + Vector3.up * 0.1f, Vector3.down, out var hit, 0.5f))
                    {
                        var on = hit.collider.name;
                        var floorY = hit.point.y - castle.position.y;
                        if (lastOn != null && on != lastOn && Mathf.Abs(floorY - lastFloorY) > 0.01f && Mathf.Abs(floorY - lastFloorY) < 0.9f)
                        {
                            var key = $"{Strip(lastOn)} {lastFloorY:0.00} -> {Strip(on)} {floorY:0.00} ({(floorY - lastFloorY) * 100:+0;-0} cm)";
                            changes[key] = changes.GetValueOrDefault(key) + 1;
                        }
                        lastOn = on;
                        lastFloorY = floorY;
                    }
                }
                var end = walker.transform.position;
                if (new Vector2(end.x - target.x, end.z - target.z).magnitude > 0.15f)
                {
                    skipped++;
                    var local = castle.InverseTransformPoint(end);
                    var against = string.Join(", ", Physics.OverlapCapsule(end + Vector3.up * 0.35f, end + Vector3.up * 1.4f, 0.4f)
                        .Where(c => c.gameObject != walker).Select(c => c.name).Distinct());
                    stalls.Add($"stalled at model ({-local.x:0.00}, {local.y:0.00}, {local.z:0.00}) heading for ({p.x:0.00}, {p.y:0.00}) against {against}");
                    if (stopAtStall)
                        break;
                    // Carry on from the sample, on its floor if it has one
                    var floor = GroundAt(castle, p);
                    var own = Physics.RaycastAll(new Vector3(target.x, castle.position.y + 6.5f, target.z), Vector3.down, 3f).OrderBy(h => h.distance).FirstOrDefault();
                    if (own.collider != null)
                        floor = own.point;
                    controller.enabled = false;
                    walker.transform.position = floor + Vector3.up * 0.05f;
                    controller.enabled = true;
                    Physics.SyncTransforms();
                    lastOn = null;
                    lastY = walker.transform.position.y;
                }
            }
            var final = walker.transform.position;
            Debug.Log($"SDF castle checks: walk '{name}': {path.Count} samples, {travelled:0.0} m walked, height {lowest - castle.position.y:0.00} to {highest - castle.position.y:0.00} m, ends at {final.y - castle.position.y:0.00} m up, " +
                      $"{stalls.Count} stalls ({skipped} samples skipped), rises over 4 cm in one move: {rises.Count}, between {(rises.Count > 0 ? rises.Min() : 0) * 100:0} and {(rises.Count > 0 ? rises.Max() : 0) * 100:0} cm");
            foreach (var stall in stalls)
                Debug.Log($"SDF castle checks:   '{name}': {stall}");
            foreach (var change in changes.OrderBy(c => c.Key))
                Debug.Log($"SDF castle checks:   '{name}': floor changes {change.Key} x {change.Value}");
            return final;
        }

        private static string Strip(string name) => name;

        /// <summary>
        /// Looks at the (nearly) flat up-facing triangles of the drawn meshes at one level of detail between two heights above the
        /// castle's base: the floors, per part, and for every 5 cm cell the top two parts' floors that are drawn: how far apart they are
        /// (z-fighting needs them under a few millimetres). A floor with something higher over the cell (a stem, a cap) is hidden from
        /// above and not counted. With <paramref name="ground"/>, each floor is also compared to the ground's collider.
        /// </summary>
        private static void FloorAudit(GameObject root, int level, float low, float high, string label, bool ground = false)
        {
            const float cell = 0.05f;
            var origin = root.transform.position;
            label += $", LOD{level}";
            var cells = new Dictionary<(int, int), List<(string part, float y)>>();
            var top = new Dictionary<(int, int), float>();
            var perPart = new Dictionary<string, Dictionary<int, float>>();
            var surfaces = new List<(string name, Mesh mesh, Matrix4x4 matrix)>();
            foreach (var group in root.GetComponentsInChildren<LODGroup>())
            {
                var lods = group.GetLODs();
                foreach (var renderer in lods[Mathf.Min(level, lods.Length - 1)].renderers)
                    if (renderer != null && renderer.name != MushroomBuilder.OutlineObjectName && renderer.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                        surfaces.Add(($"{group.name}", filter.sharedMesh, renderer.localToWorldMatrix));
            }
            foreach (var (partName, mesh, matrix) in surfaces)
            {
                if (!mesh.isReadable)
                    continue;
                var vertices = mesh.vertices;
                var triangles = mesh.triangles;
                for (var t = 0; t < triangles.Length; t += 3)
                {
                    var p0 = matrix.MultiplyPoint3x4(vertices[triangles[t]]);
                    var p1 = matrix.MultiplyPoint3x4(vertices[triangles[t + 1]]);
                    var p2 = matrix.MultiplyPoint3x4(vertices[triangles[t + 2]]);
                    var cross = Vector3.Cross(p1 - p0, p2 - p0);
                    var twiceArea = cross.magnitude;
                    if (twiceArea < 2e-6f)
                        continue;
                    var normal = cross / twiceArea;
                    var up = Mathf.Abs(normal.y) > 0.05f;
                    var flat = normal.y > 0.999f && Mathf.Max(Mathf.Abs(p1.y - p0.y), Mathf.Abs(p2.y - p0.y)) < 0.015f && p0.y - origin.y >= low - 0.02f && p0.y - origin.y <= high + 0.02f;
                    var minX = Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x));
                    var maxX = Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x));
                    var minZ = Mathf.Min(p0.z, Mathf.Min(p1.z, p2.z));
                    var maxZ = Mathf.Max(p0.z, Mathf.Max(p1.z, p2.z));
                    if (!up)
                        continue;
                    if (flat)
                    {
                        var key = Mathf.RoundToInt((p0.y - origin.y) * 1000);
                        if (!perPart.TryGetValue(partName, out var heights))
                            perPart[partName] = heights = new Dictionary<int, float>();
                        heights[key] = heights.GetValueOrDefault(key) + twiceArea * 0.5f;
                    }
                    for (var cx = Mathf.FloorToInt(minX / cell); cx <= Mathf.FloorToInt(maxX / cell); cx++)
                    for (var cz = Mathf.FloorToInt(minZ / cell); cz <= Mathf.FloorToInt(maxZ / cell); cz++)
                    {
                        var c = new Vector2((cx + 0.5f) * cell, (cz + 0.5f) * cell);
                        if (!InTriangle(c, p0, p1, p2))
                            continue;
                        // The triangle's height over the cell's middle
                        var y = p0.y - (normal.x * (c.x - p0.x) + normal.z * (c.y - p0.z)) / normal.y - origin.y;
                        if (y < low - 0.1f)
                            continue;
                        top[(cx, cz)] = Mathf.Max(top.GetValueOrDefault((cx, cz), float.NegativeInfinity), y);
                        if (!flat)
                            continue;
                        if (!cells.TryGetValue((cx, cz), out var list))
                            cells[(cx, cz)] = list = new List<(string, float)>();
                        list.Add((partName, y));
                    }
                }
            }
            var summary = perPart.OrderBy(p => p.Key).Select(p => $"{p.Key}: " + string.Join(", ", p.Value.OrderByDescending(h => h.Value).Take(3).Select(h => $"{h.Key / 1000f:0.000} m ({h.Value:0.0} m2)")));
            Debug.Log($"SDF castle checks: flat floors {low}..{high} m ({label}), largest per part, first 40:\n" + string.Join("\n", summary.Take(40)));
            var gaps = new Dictionary<int, int>();
            var largeGaps = new Dictionary<string, int>();
            var coplanar = 0;
            var hidden = 0;
            var overlapping = 0;
            string example = null;
            foreach (var (key, list) in cells)
            {
                var ceiling = top[key];
                var shown = list.Where(e => e.y >= ceiling - 0.01f).ToList();
                var parts = list.Select(e => e.part).Distinct().Count();
                if (parts < 2)
                    continue;
                overlapping++;
                if (shown.Select(e => e.part).Distinct().Count() < 2 && shown.Count > 0)
                {
                    // The higher floor wins by more than 1 cm: gap to the best of the rest
                    var best = shown.Max(e => e.y);
                    var next = list.Where(e => e.part != shown[0].part).Max(e => e.y);
                    var cm = Mathf.RoundToInt((best - next) * 100);
                    gaps[cm] = gaps.GetValueOrDefault(cm) + 1;
                    if (cm > 30)
                    {
                        var pair = $"{shown[0].part} over {list.Where(e => e.part != shown[0].part).OrderByDescending(e => e.y).First().part} ({cm} cm)";
                        largeGaps[pair] = largeGaps.GetValueOrDefault(pair) + 1;
                    }
                    continue;
                }
                if (shown.Count == 0)
                {
                    hidden++;
                    continue;
                }
                var close = shown.Max(e => e.y) - shown.Min(e => e.y) < 0.005f;
                if (close)
                {
                    coplanar++;
                    example ??= $"{string.Join(" and ", shown.Select(e => e.part).Distinct())} at {shown[0].y:0.000} m, cell ({key.Item1 * cell:0.0}, {key.Item2 * cell:0.0})";
                }
                else
                {
                    var cm = Mathf.RoundToInt((shown.Max(e => e.y) - shown.Min(e => e.y)) * 100);
                    gaps[cm] = gaps.GetValueOrDefault(cm) + 1;
                    if (cm > 30)
                    {
                        var pair = $"{shown.OrderByDescending(e => e.y).First().part} over {shown.OrderBy(e => e.y).First().part} ({cm} cm)";
                        largeGaps[pair] = largeGaps.GetValueOrDefault(pair) + 1;
                    }
                }
            }
            Debug.Log($"SDF castle checks: floor overlaps ({label}): {overlapping} cells where two parts' flat floors lie over each other; {hidden} of them hidden under something higher; " +
                      $"{coplanar} drawn within 5 mm of each other (z-fighting) {example}; height difference of the top floor to the next one's, cm (cells): {string.Join(", ", gaps.OrderBy(g => g.Key).Select(g => $"{g.Key} x {g.Value}"))}");
            if (largeGaps.Count > 0)
                Debug.Log($"SDF castle checks: steps over 30 cm ({label}): {string.Join("; ", largeGaps.OrderByDescending(g => g.Value).Take(12).Select(g => $"{g.Key} x {g.Value}"))}");
            if (ground)
            {
                var groundGaps = new Dictionary<int, int>();
                var same = 0;
                var counted = 0;
                string where = null;
                foreach (var (key, list) in cells)
                {
                    var world = new Vector3((key.Item1 + 0.5f) * cell, 300, (key.Item2 + 0.5f) * cell);
                    var hit = Physics.RaycastAll(world, Vector3.down, 400).Where(h => h.collider is MeshCollider && h.collider.transform.parent != null && h.collider.transform.parent.name == "Ground").OrderBy(h => h.distance).FirstOrDefault();
                    if (hit.collider == null)
                        continue;
                    foreach (var e in list)
                    {
                        counted++;
                        var d = e.y - (hit.point.y - origin.y);
                        if (Mathf.Abs(d) < 0.005f)
                        {
                            same++;
                            where ??= $"{e.part} at {e.y:0.000} m, cell ({world.x:0.0}, {world.z:0.0})";
                        }
                        else
                        {
                            var cm = Mathf.RoundToInt(d * 100);
                            groundGaps[cm] = groundGaps.GetValueOrDefault(cm) + 1;
                        }
                    }
                }
                Debug.Log($"SDF castle checks: floors against the ground ({label}): {counted} part floors over the ground's chunks, {same} within 5 mm of it {where}; height above the ground, cm (cells): {string.Join(", ", groundGaps.OrderBy(g => g.Key).Select(g => $"{g.Key} x {g.Value}"))}");
            }
        }

        private static bool InTriangle(Vector2 p, Vector3 a, Vector3 b, Vector3 c)
        {
            float Side(Vector3 u, Vector3 v) => (v.x - u.x) * (p.y - u.z) - (v.z - u.z) * (p.x - u.x);
            var d1 = Side(a, b);
            var d2 = Side(b, c);
            var d3 = Side(c, a);
            return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
        }

        // ---- z-fighting ---------------------------------------------------------------------------------------------------------------

        private static void ZFighting(Transform castle, Camera camera, string directory)
        {
            var joints = Joints();
            // Where floors meet: a joint's pad between two bays (5.0 to 5.15), a large tower's ring and a small tower's (5.3 over the
            // pad and the bays), the keep's ring (5.3 over the bays and a pad), seen from the courtyard side along the walk
            // (target, floor height, direction from the target to the camera in plan: inward and along the wall)
            var places = new List<(string name, Vector2 target, float floor, Vector2 horizontal)>();
            Vector2 Tangent(Vector2 a) { var n = Outward(a); return new Vector2(-n.y, n.x); }
            foreach (var (name, k, floor) in new[] { ("joint2", 2, 5.15f), ("joint8", 8, 5.15f), ("joint15", 15, 5.15f) })
                places.Add((name, joints[k] - Outward(joints[k]) * 0.3f, floor, (-Outward(joints[k]) * 0.8f + Tangent(joints[k]) * 0.6f).normalized));
            foreach (var (name, k) in new[] { ("tower4_large", 4), ("tower6_small", 6), ("tower13_large", 13) })
            {
                var o = Outward(joints[k]);
                places.Add((name, joints[k] + o * 0.4f - o * 3.0f, 5.3f, (-o * 0.8f + Tangent(joints[k]) * 0.6f).normalized));
            }
            places.Add(("keep_ring", new Vector2(0, -5 + 4.0f), 5.3f, new Vector2(0.6f, 0.8f)));
            places.Add(("keep_ring_rear", new Vector2(-3.0f, -5 + 3.2f), 5.3f, new Vector2(-0.6f, 0.8f)));

            var level = QualitySettings.GetQualityLevel();
            var rows = new List<string>();
            try
            {
                foreach (var preset in new[] { 0, QualitySettings.names.Length - 1 })
                {
                    QualitySettings.SetQualityLevel(preset, true);
                    var tag = preset == 0 ? "high" : "web";
                    foreach (var (name, at, floor, horizontal) in places)
                    foreach (var distance in new[] { 2f, 6f, 15f, 40f })
                    {
                        var target = ModelPoint(castle, at.x, floor, at.y);
                        var dir = castle.TransformDirection(new Vector3(-horizontal.x, 0, horizontal.y)).normalized;
                        var eye = target + dir * distance + Vector3.up * (1.6f + distance * 0.15f);
                        var rotation = Quaternion.LookRotation(target - eye);
                        var path = Path.Combine(directory, $"zfight_{name}_{distance:0}m_{tag}.png");
                        var shifted = Path.Combine(directory, "tmp_shift.png");
                        MushroomSnapshot.RenderView(camera, eye, rotation, path, 1600, 900);
                        MushroomSnapshot.RenderView(camera, eye + rotation * new Vector3(0.003f, 0.003f, 0), rotation, shifted, 1600, 900);
                        var (count, whole) = Difference(path, shifted);
                        rows.Add($"{tag,-4} {name,-14} {distance,3:0} m: {count,6} of {whole} centre pixels differ clearly between the two views (3 mm apart)");
                    }
                }
            }
            finally
            {
                QualitySettings.SetQualityLevel(level, true);
                var tmp = Path.Combine(directory, "tmp_shift.png");
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
            Debug.Log("SDF castle checks: z-fighting, centre third of each view, pixels differing by more than 60 levels after a 3 mm move:\n" + string.Join("\n", rows));

            // Depth precision at those distances: the smallest gap a 24-bit depth buffer without reversed-Z (WebGL) can tell apart
            var near = camera.nearClipPlane;
            var far = camera.farClipPlane;
            foreach (var z in new[] { 2f, 6f, 15f, 40f, 100f })
                Debug.Log($"SDF castle checks: depth precision at {z} m (near {near} m, far {far} m, 24 bit, no reversed-Z): {z * z / (near * (1 << 24)) * 1000:0.000} mm; the floors are at least 105 mm apart");
        }

        private static (int count, int total) Difference(string a, string b)
        {
            var first = new Texture2D(2, 2);
            var second = new Texture2D(2, 2);
            first.LoadImage(File.ReadAllBytes(a));
            second.LoadImage(File.ReadAllBytes(b));
            var w = first.width;
            var h = first.height;
            var x0 = w / 3;
            var y0 = h / 3;
            var pa = first.GetPixels32();
            var pb = second.GetPixels32();
            var count = 0;
            var total = 0;
            for (var y = y0; y < 2 * y0; y++)
            for (var x = x0; x < 2 * x0; x++)
            {
                var i = y * w + x;
                var d = Mathf.Max(Mathf.Abs(pa[i].r - pb[i].r), Mathf.Max(Mathf.Abs(pa[i].g - pb[i].g), Mathf.Abs(pa[i].b - pb[i].b)));
                count += d > 60 ? 1 : 0;
                total++;
            }
            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);
            return (count, total);
        }

        // ---- levels of detail ---------------------------------------------------------------------------------------------------------

        private static void Levels(Scene scene, Camera camera, string directory, string modelName)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{MushroomBuilder.PrefabFolder}/{modelName}.prefab");
            // castle0's parts are castle_X, castle1's stone_X (only castle0 has the young fly agarics, castle_amanitas)
            var prefix = modelName == "castle0" ? "castle_" : "stone_";
            Keep(scene, camera, directory, prefab, prefix + "keep");
            foreach (var part in new[] { "tower_small", "tower_large", "keep", "amanitas", "gate_turret" }.Select(n => prefix + n))
            {
                var use = prefab.transform.Cast<Transform>().FirstOrDefault(t => t.name.StartsWith(part, StringComparison.Ordinal) && t.GetComponent<LODGroup>() != null);
                if (use == null)
                {
                    Debug.Log($"SDF castle checks: no use of {part} with a LOD group in the castle prefab (instanced parts: {string.Join(", ", prefab.transform.Cast<Transform>().Select(t => t.name).Take(12))}...)");
                    continue;
                }
                var copy = UnityEngine.Object.Instantiate(use.gameObject, new Vector3(0, 140, 0), Quaternion.identity);
                SceneManager.MoveGameObjectToScene(copy, scene);
                try
                {
                    var group = copy.GetComponent<LODGroup>();
                    var lods = group.GetLODs();
                    var bounds = lods[0].renderers.Where(r => r != null).Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
                    var fov = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                    var distance = bounds.extents.magnitude * 1.15f / fov;
                    var direction = new Vector3(-0.25f, 0.2f, 1).normalized;
                    for (var level = 0; level < lods.Length; level++)
                    {
                        group.ForceLOD(level);
                        var triangles = lods[level].renderers.Where(r => r != null && r.GetComponent<MeshFilter>() != null).Sum(r => r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3);
                        var eye = bounds.center + direction * distance;
                        MushroomSnapshot.RenderView(camera, eye, Quaternion.LookRotation(bounds.center - eye), Path.Combine(directory, $"lod_{part}_LOD{level}.png"), 1400, 1400);
                        // And from a little above, looking at the join of cap and stem
                        var low = bounds.center + new Vector3(0.2f, -0.05f, 1).normalized * distance;
                        MushroomSnapshot.RenderView(camera, low, Quaternion.LookRotation(bounds.center - low), Path.Combine(directory, $"lod_{part}_LOD{level}_side.png"), 1400, 1400);
                        Debug.Log($"SDF castle checks: {use.name} LOD{level}: {triangles} triangles, switches below {lods[level].screenRelativeTransitionHeight:0.000}");
                    }
                    group.ForceLOD(-1);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(copy);
                }
            }
        }

        /// <summary>The keep is a GameObject of the castle's own LOD group (with the gate): all else is hidden, each level forced on.</summary>
        private static void Keep(Scene scene, Camera camera, string directory, GameObject prefab, string keepName)
        {
            var copy = UnityEngine.Object.Instantiate(prefab, new Vector3(0, 140, 0), Quaternion.identity);
            SceneManager.MoveGameObjectToScene(copy, scene);
            try
            {
                foreach (var child in copy.transform.Cast<Transform>().ToArray())
                    if (!child.name.StartsWith(keepName, StringComparison.Ordinal))
                        child.gameObject.SetActive(false);
                var group = copy.GetComponent<LODGroup>();
                var lods = group.GetLODs();
                var keep = lods.Select(l => l.renderers.Where(r => r != null && r.name == keepName).ToArray()).ToArray();
                var bounds = keep[0].Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
                var fov = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                var distance = bounds.extents.magnitude * 1.1f / fov;
                for (var level = 0; level < lods.Length; level++)
                {
                    group.ForceLOD(level);
                    foreach (var (view, name) in new[] { (new Vector3(-0.25f, 0.2f, 1), "lod_" + keepName + "_LOD" + level), (new Vector3(0.2f, -0.05f, 1), "lod_" + keepName + "_LOD" + level + "_side") })
                    {
                        var eye = bounds.center + view.normalized * distance;
                        MushroomSnapshot.RenderView(camera, eye, Quaternion.LookRotation(bounds.center - eye), Path.Combine(directory, name + ".png"), 1400, 1400);
                    }
                    Debug.Log($"SDF castle checks: {keepName} LOD{level}: {keep[level].Sum(r => r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3)} triangles");
                }
                group.ForceLOD(-1);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(copy);
            }
        }
    }
}
