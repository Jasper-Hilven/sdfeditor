using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SdfBattle;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SdfLevel.Editor
{
    /// <summary>
    /// The heat of a level (ticket 28.10, level.json "heat"), built into the scene: one object, <see cref="HeatHazard.ObjectName"/>, at the
    /// scene's root with the <see cref="HeatHazard"/> that plays the rule. The models placed at a ring's centre (the kitchen's burners) are
    /// the ones that glow with it; the rings are navmesh areas (<see cref="HeatHazard.AreaName"/>: Burner, Burner 2, ...), which the bake of
    /// the bots' navmesh needs to know: <see cref="Add"/> returns flat marker discs over the rings (a modifier with the area on a thin
    /// collider, the navmesh takes the higher area where they overlap the floor) for the bake, and <see cref="Finish"/> takes them away
    /// again, so no collider of theirs stays in the scene. A level without heat gets nothing. Positions are Unity's, x mirrored as for
    /// placements.
    /// </summary>
    public static class LevelHeat
    {
        /// <summary>Metres a marker disc lies over the floor: inside the same voxel column of the bake, so it joins the floor.</summary>
        public const float Lift = 0.03f;

        /// <summary>Metres from a ring's centre within which a placed model counts as standing there (the burner at it).</summary>
        public const float CentreReach = 0.3f;

        private const int DiscSegments = 48;

        /// <summary>
        /// Builds the heat under the scene's root and returns the marker discs to bake the navmesh with (call <see cref="Finish"/> with
        /// them after the bake); none for a level.json without "heat". <paramref name="placed"/> are the level's placed models.
        /// </summary>
        public static List<GameObject> Add(Scene scene, JObject data, IReadOnlyList<IMMScene.Placement> placed)
        {
            var markers = new List<GameObject>();
            if (!(data["heat"] is JObject heat) || !(heat["rings"] is JArray entries) || entries.Count == 0)
                return markers;
            var rings = entries.Select(e => new HeatRing(new Vector2(-e["at"][0].Value<float>(), e["at"][1].Value<float>()), e.Value<float>("radius"), e.Value<int>("group"))).ToArray();
            var heights = entries.Select(e => e.Value<float>("y")).ToArray();
            var groups = (heat["groups"] as JArray)?.Count ?? rings.Max(r => r.group) + 1;

            var root = new GameObject(HeatHazard.ObjectName);
            SceneManager.MoveGameObjectToScene(root, scene);
            var component = root.AddComponent<HeatHazard>();
            component.rules = new HeatRules
            {
                cycle = heat.Value<float>("cycle"),
                first = heat.Value<float>("first"),
                warm = heat.Value<float>("warm"),
                hot = heat.Value<float>("hot"),
                cool = heat.Value<float>("cool"),
                damage = heat.Value<float>("damage"),
                cost = heat.Value<float>("cost"),
                cause = heat.Value<string>("cause"),
                groups = groups,
            };
            component.rings = rings;
            component.ringHeights = heights;
            component.glows = rings.Select(ring => new HeatHazard.RingGlow { renderers = GlowingRenderers(ring, placed) }).ToArray();
            foreach (var (glow, ring) in component.glows.Zip(rings, (g, r) => (g, r)))
                if (glow.renderers.Length == 0)
                    Debug.LogWarning($"SDF level: no model stands at the heat ring at ({-ring.centre.x}, {ring.centre.y}); it will not glow");

            for (var group = 0; group < groups; group++)
                EnsureArea(HeatHazard.AreaName(group));
            for (var i = 0; i < rings.Length; i++)
                markers.Add(AddMarker(scene, new Vector3(rings[i].centre.x, heights[i], rings[i].centre.y), rings[i].radius, UnityEngine.AI.NavMesh.GetAreaFromName(HeatHazard.AreaName(rings[i].group))));
            return markers;
        }

        /// <summary>Removes the marker discs <see cref="Add"/> made, once the navmesh is baked.</summary>
        public static void Finish(IEnumerable<GameObject> markers)
        {
            foreach (var marker in markers)
            {
                if (marker == null)
                    continue;
                var mesh = marker.GetComponent<MeshCollider>()?.sharedMesh;
                UnityEngine.Object.DestroyImmediate(marker);
                if (mesh != null)
                    UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>
        /// The renderers of the placed models whose origin is within <see cref="CentreReach"/> of the ring's centre. They lose their static
        /// flags: the glow is a property block per renderer, which a batched object cannot take.
        /// </summary>
        private static Renderer[] GlowingRenderers(HeatRing ring, IReadOnlyList<IMMScene.Placement> placed)
        {
            var renderers = new List<Renderer>();
            foreach (var placement in placed)
            {
                if (placement.transform == null)
                    continue;
                var position = placement.transform.position;
                if (Vector2.Distance(new Vector2(position.x, position.z), ring.centre) > CentreReach)
                    continue;
                foreach (var part in placement.transform.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(part.gameObject, 0);
                renderers.AddRange(placement.transform.GetComponentsInChildren<Renderer>(true));
            }
            return renderers.ToArray();
        }

        /// <summary>
        /// A flat disc of <paramref name="radius"/> metres at <paramref name="centre"/> (lifted <see cref="Lift"/>) with a mesh collider and
        /// a navmesh modifier that makes what it covers <paramref name="area"/>. Meant for a bake only.
        /// </summary>
        public static GameObject AddMarker(Scene scene, Vector3 centre, float radius, int area, Transform parent = null)
        {
            var marker = new GameObject("Heat marker");
            if (parent != null)
                marker.transform.SetParent(parent, false);
            else
                SceneManager.MoveGameObjectToScene(marker, scene);
            marker.transform.position = centre + Vector3.up * Lift;
            marker.AddComponent<MeshCollider>().sharedMesh = Disc(radius);
            var modifier = marker.AddComponent<NavMeshModifier>();
            modifier.overrideArea = true;
            modifier.area = area;
            return marker;
        }

        /// <summary>A disc facing up (clockwise seen from above, as Unity's front faces are), a fan of triangles round its middle.</summary>
        public static Mesh Disc(float radius)
        {
            var vertices = new List<Vector3> { Vector3.zero };
            for (var i = 0; i < DiscSegments; i++)
            {
                var angle = i * Mathf.PI * 2 / DiscSegments;
                vertices.Add(new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius));
            }
            var triangles = new List<int>();
            for (var i = 0; i < DiscSegments; i++)
                triangles.AddRange(new[] { 0, 1 + (i + 1) % DiscSegments, 1 + i });
            var mesh = new Mesh { name = "heat marker" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The index of the navmesh area called <paramref name="name"/>, made in the project's navigation settings in the first free slot
        /// after the built-in three when missing (the project ships Burner to Burner 4).
        /// </summary>
        public static int EnsureArea(string name)
        {
            var existing = UnityEngine.AI.NavMesh.GetAreaFromName(name);
            if (existing >= 0)
                return existing;
            var project = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("NavMeshProjectSettings"));
            var areas = project.FindProperty("areas");
            for (var i = 3; i < areas.arraySize; i++)
            {
                var slot = areas.GetArrayElementAtIndex(i).FindPropertyRelative("name");
                if (!string.IsNullOrEmpty(slot.stringValue))
                    continue;
                slot.stringValue = name;
                project.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                Debug.Log($"SDF level: navigation area {name} (index {i}) added to the project");
                return i;
            }
            throw new InvalidOperationException($"The project has no free navigation area for {name}");
        }
    }
}
