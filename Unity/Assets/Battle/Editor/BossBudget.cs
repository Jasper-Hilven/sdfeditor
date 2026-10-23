using System;
using System.Collections.Generic;
using System.Linq;
using SdfLevel;
using UnityEditor;
using UnityEngine;

namespace SdfBattle.Editor
{
    /// <summary>
    /// What the final boss's arena draws from 42's own camera at a moment of the fight (ticket 33.9, the budget of section 11: 500k
    /// triangles and 200 draws at the web preset): every enabled renderer in the camera's frustum, each LOD group at the level it picks
    /// from there, by what it is (the ground, the placed models, Prime, the Prime-copter, the machines, the scouts, the rest), plus the
    /// instanced groups (the crowd, the clutter) as the scatter renderer counted them the last frame, which needs a graphics device
    /// (run <see cref="BossPlayTest"/> without <c>-nographics</c>). A draw is a material of a renderer in view, as in
    /// <c>LevelSnapshot</c>; the GUI's canvases and the screen-space effects are not counted (the HUD is a few draws).
    /// </summary>
    public static class BossBudget
    {
        /// <summary>The lines measured so far, for the play test's end.</summary>
        public static readonly List<string> Lines = new List<string>();

        public static string Measure(string label, Boss boss, Camera camera)
        {
            if (camera == null)
                return null;
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            var position = camera.transform.position;
            var halfAngle = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var totals = new Dictionary<string, (int Draws, int Triangles)>();
            void Add(string kind, int draws, int triangles)
            {
                totals.TryGetValue(kind, out var sum);
                totals[kind] = (sum.Draws + draws, sum.Triangles + triangles);
            }

            // The renderers that belong to a LOD group count at the level it picks; the rest count as they are
            var chosen = new HashSet<Renderer>();
            var inGroups = new HashSet<Renderer>();
            foreach (var group in UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
            {
                var lods = group.GetLODs();
                foreach (var lod in lods)
                    foreach (var renderer in lod.renderers)
                        if (renderer != null)
                            inGroups.Add(renderer);
                var scale = group.transform.lossyScale;
                var size = group.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                var distance = Vector3.Distance(position, group.transform.TransformPoint(group.localReferencePoint));
                var height = size * 0.5f * QualitySettings.lodBias / (Mathf.Max(distance, 0.01f) * halfAngle);
                var level = Array.FindIndex(lods, lod => height >= lod.screenRelativeTransitionHeight);
                if (level < 0)
                    continue;
                foreach (var renderer in lods[level].renderers)
                    if (renderer != null)
                        chosen.Add(renderer);
            }
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                if (inGroups.Contains(renderer) && !chosen.Contains(renderer))
                    continue;
                if (!GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
                    continue;
                Add(KindOf(renderer, boss), Math.Max(1, renderer.sharedMaterials.Length), TrianglesOf(renderer));
                if (TrianglesOf(renderer) == 0)
                    Debug.Log($"SDF boss budget: empty renderer {renderer.GetType().Name} {renderer.transform.parent?.name}/{renderer.name} kind {KindOf(renderer, boss)}");
            }

            var instanced = "";
            foreach (var scatter in UnityEngine.Object.FindObjectsByType<InstancedScatterRenderer>(FindObjectsSortMode.None))
            {
                var frame = scatter.LastFrame;
                if (frame.DrawsPerGroup == null)
                    continue;
                Add("instanced", frame.Draws + frame.OutlineDraws, frame.Triangles + frame.OutlineTriangles);
                instanced = " [" + string.Join(", ", scatter.groups.Select((g, i) => i < frame.DrawsPerGroup.Length
                    ? $"{g.name} {frame.DrawsPerGroup[i]} draws {frame.TrianglesPerGroup[i]:N0} tri" : g.name)) + "]";
            }
            var draws = totals.Values.Sum(v => v.Draws);
            var triangles = totals.Values.Sum(v => v.Triangles);
            var line = $"{label} ({QualitySettings.names[QualitySettings.GetQualityLevel()]}): {triangles:N0} tri, {draws} draws: " +
                       string.Join(", ", totals.OrderByDescending(v => v.Value.Triangles).Select(v => $"{v.Key} {v.Value.Triangles:N0} tri / {v.Value.Draws}")) + instanced;
            Lines.Add(line);
            Debug.Log("SDF boss budget: " + line);
            return line;
        }

        // What a renderer is part of, for the table
        private static string KindOf(Renderer renderer, Boss boss)
        {
            var t = renderer.transform;
            if (boss != null && boss.Body != null && t.IsChildOf(boss.Body.transform)) return "prime";
            if (boss != null && boss.Copter != null && t.IsChildOf(boss.Copter.transform)) return "copter";
            if (renderer.GetComponentInParent<Machine>() != null) return "machines";
            if (renderer.GetComponentInParent<ScoutBody>() != null || renderer.GetComponentInParent<Bot>() != null) return "scouts";
            for (var p = t; p != null; p = p.parent)
            {
                if (p.name == "Ground") return "ground";
                if (p.name == "Placed") return "placed";
                if (p.name == "Level") break;
            }
            if (renderer is ParticleSystemRenderer) return "particles";
            return "other";
        }

        private const string CacheKey = "SdfBattle.BossBudget.Triangles";
        private static Dictionary<string, int> cache;

        // A renderer's place in the scene as a key that edit mode and play mode share: its path and its position to the centimetre
        private static string KeyOf(Renderer renderer)
        {
            var path = renderer.name;
            for (var p = renderer.transform.parent; p != null; p = p.parent)
                path = p.name + "/" + path;
            var at = renderer.transform.position;
            return $"{path}@{Mathf.RoundToInt(at.x * 100)},{Mathf.RoundToInt(at.y * 100)},{Mathf.RoundToInt(at.z * 100)}";
        }

        /// <summary>
        /// Before play mode (the scene open in the editor): remembers every mesh renderer's triangles, because static batching turns the
        /// meshes into combined ones in play mode and a renderer then no longer knows its own share.
        /// </summary>
        public static void Remember()
        {
            var lines = new List<string>();
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var tris = Triangles(renderer);
                if (tris > 0)
                    lines.Add(KeyOf(renderer) + "=" + tris);
            }
            SessionState.SetString(CacheKey, string.Join("\n", lines));
        }

        private static int TrianglesOf(Renderer renderer)
        {
            if (cache == null)
            {
                cache = new Dictionary<string, int>();
                foreach (var line in SessionState.GetString(CacheKey, "").Split('\n'))
                {
                    var cut = line.LastIndexOf('=');
                    if (cut > 0 && int.TryParse(line.Substring(cut + 1), out var tris))
                        cache[line.Substring(0, cut)] = tris;
                }
            }
            if (renderer.isPartOfStaticBatch && cache.TryGetValue(KeyOf(renderer), out var known))
                return known;
            return Triangles(renderer);
        }

        private static int Triangles(Renderer renderer)
        {
            Mesh mesh = null;
            if (renderer is SkinnedMeshRenderer skinned)
                mesh = skinned.sharedMesh;
            else if (renderer.TryGetComponent<MeshFilter>(out var filter))
                mesh = filter.sharedMesh;
            if (mesh == null)
                return 0;
            var count = 0;
            for (var i = 0; i < mesh.subMeshCount; i++)
                count += (int)(mesh.GetIndexCount(i) / 3);
            return count;
        }
    }
}
