using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SdfBattle;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SdfLevel.Editor
{
    /// <summary>
    /// The data pulses of a level (ticket 30.9, level.json "pulses"), built into the scene: one object, <see cref="RootName"/>, at the
    /// scene's root with the overlay mesh, its material on SdfPulse.shader and the <see cref="DataPulses"/> that plays the rule. The mesh is
    /// a flat strip over each pulse line, as wide as its trace and <see cref="Lift"/> metres over the ground, with UV.x the metres along the
    /// line from its first corner (the shader draws the bands from that and the time), all lines in one mesh: one draw call. A level
    /// without pulses gets nothing. Positions are Unity's, x mirrored as for placements.
    /// </summary>
    public static class LevelPulses
    {
        public const string RootName = "Pulses";
        public const string ShaderPath = "Assets/Level/Shaders/SdfPulse.shader";

        /// <summary>Metres the strips lie over the ground.</summary>
        public const float Lift = 0.02f;

        /// <summary>The longest a corner's miter may be, as a multiple of the half width (a very sharp turn is cut short).</summary>
        private const float MiterLimit = 2;

        /// <summary>Builds the pulses under the scene's root; returns how many lines. Nothing for a level.json without "pulses".</summary>
        public static int Add(Scene scene, JObject data, string name, string meshFolder)
        {
            if (!(data["pulses"] is JObject pulses) || !(pulses["lines"] is JArray entries) || entries.Count == 0)
                return 0;
            var lines = entries.Select(e => new DataPulses.Line
            {
                points = ((JArray)e["points"]).Select(p => new Vector2(-p[0].Value<float>(), p[1].Value<float>())).ToArray(),
                width = e.Value<float>("width"),
            }).ToArray();
            var heights = entries.Select(e => ((JArray)e["y"]).Select(y => y.Value<float>()).ToArray()).ToArray();

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            var rules = new PulseRules
            {
                every = pulses.Value<float>("every"),
                speed = pulses.Value<float>("speed"),
                length = pulses.Value<float>("length"),
                boost = pulses.Value<float>("boost"),
                boostSeconds = pulses.Value<float>("for"),
            };
            var component = root.AddComponent<DataPulses>();
            component.rules = rules;
            component.lines = lines;

            var path = $"{meshFolder}/pulses.asset";
            var mesh = Strips(lines, heights);
            mesh.name = "pulses";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null)
                AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Material(name, rules);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return lines.Length;
        }

        /// <summary>
        /// One strip per line, two vertices at each corner (left and right of the line, mitered so that a jog keeps the trace's width), UV.x
        /// the arc length from the first corner and UV.y 0 on the right, 1 on the left. <paramref name="heights"/> are the ground's y at
        /// each corner; the strips lie <see cref="Lift"/> over it.
        /// </summary>
        public static Mesh Strips(IReadOnlyList<DataPulses.Line> lines, IReadOnlyList<float[]> heights)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (var l = 0; l < lines.Count; l++)
            {
                var points = lines[l].points;
                var half = lines[l].width * 0.5f;
                var first = vertices.Count;
                var arc = 0f;
                for (var i = 0; i < points.Length; i++)
                {
                    if (i > 0)
                        arc += Vector2.Distance(points[i - 1], points[i]);
                    var before = i > 0 ? (points[i] - points[i - 1]).normalized : (points[i + 1] - points[i]).normalized;
                    var after = i + 1 < points.Length ? (points[i + 1] - points[i]).normalized : before;
                    // The side of each leg is its direction turned a quarter; the corner's offset is the mean of the two, lengthened so
                    // that the strip keeps its width across the bend
                    var left = (Quarter(before) + Quarter(after)).normalized;
                    var scale = Mathf.Min(1 / Mathf.Max(Vector2.Dot(left, Quarter(before)), 1e-3f), MiterLimit);
                    var offset = left * (half * scale);
                    var y = heights[l][Mathf.Min(i, heights[l].Length - 1)] + Lift;
                    vertices.Add(new Vector3(points[i].x - offset.x, y, points[i].y - offset.y));
                    vertices.Add(new Vector3(points[i].x + offset.x, y, points[i].y + offset.y));
                    uvs.Add(new Vector2(arc, 0));
                    uvs.Add(new Vector2(arc, 1));
                    if (i > 0)
                    {
                        var a = first + 2 * (i - 1);
                        triangles.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 });
                    }
                }
            }
            var mesh = new Mesh();
            if (vertices.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(Enumerable.Repeat(Vector3.up, vertices.Count).ToList());
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A direction turned a quarter to the left (x, z).</summary>
        private static Vector2 Quarter(Vector2 direction) => new Vector2(-direction.y, direction.x);

        private static Material Material(string name, PulseRules rules)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath) ?? throw new InvalidOperationException($"The shader {ShaderPath} is missing");
            var path = $"{LevelBuilder.MaterialFolder}/{name} Pulses.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                MushroomBuilder.EnsureFolder(LevelBuilder.MaterialFolder);
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }
            // The numbers the rule plays by are the shader's too
            material.SetFloat("_Every", rules.every);
            material.SetFloat("_Speed", rules.speed);
            material.SetFloat("_Length", rules.length);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
