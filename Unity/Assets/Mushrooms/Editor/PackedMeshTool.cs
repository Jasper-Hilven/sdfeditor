using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SdfMushrooms.Editor
{
    /// <summary>
    /// Rebuilds just some models and reports what their mesh assets hold (ticket 37): without a window, <c>Unity -batchmode -quit
    /// -projectPath Unity -executeMethod SdfMushrooms.Editor.PackedMeshTool.Rebuild -models coffee-bean0,castle0</c> builds the prefabs
    /// and meshes of the models of those names (the file names of their .glb files, without the folder or the .glb), and
    /// <c>... PackedMeshTool.Report -models ...</c> lists their meshes' vertex layouts and sizes. A full rebuild is Tools > SDF
    /// Mushrooms > Rebuild All.
    /// </summary>
    public static class PackedMeshTool
    {
        private static string[] Models()
        {
            var args = Environment.GetCommandLineArgs();
            var at = Array.IndexOf(args, "-models");
            return at >= 0 && at + 1 < args.Length ? args[at + 1].Split(',') : Array.Empty<string>();
        }

        private static string[] GlbsOf(string[] names) =>
            MushroomBuilder.GeneratedModels().Where(glb => names.Contains(Path.GetFileNameWithoutExtension(glb))).ToArray();

        public static void Rebuild()
        {
            MushroomBuilder.FloatMeshes = Environment.GetCommandLineArgs().Contains("-floatMeshes");
            var glbs = GlbsOf(Models());
            Debug.Log($"SDF packed meshes: rebuilding {glbs.Length} models ({string.Join(", ", glbs.Select(Path.GetFileNameWithoutExtension))})");
            var started = DateTime.Now;
            try
            {
                MushroomBuilder.Build(glbs);
            }
            finally
            {
                MushroomBuilder.FloatMeshes = false;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"SDF packed meshes: built in {(DateTime.Now - started).TotalSeconds:0.0} s");
        }

        /// <summary>
        /// Rebuilds the models once as floats and once compact, each time rebuilding a level's scene over them and taking the level's
        /// snapshots: <c>-executeMethod SdfMushrooms.Editor.PackedMeshTool.SnapshotBothWays -models a,b,c -level fungal -snapshotDir
        /// folder</c> writes folder/float and folder/packed. Without a window of its own: needs the GPU, so no -nographics.
        /// </summary>
        public static void SnapshotBothWays()
        {
            var args = Environment.GetCommandLineArgs();
            string Argument(string name, string fallback)
            {
                var at = Array.IndexOf(args, name);
                return at >= 0 && at + 1 < args.Length ? args[at + 1] : fallback;
            }
            var level = Argument("-level", "fungal");
            var directory = Argument("-snapshotDir", Path.Combine(Path.GetTempPath(), "packed-snapshots"));
            var scene = SdfLevel.Editor.LevelBuilder.Levels().First(l => Path.GetFileName(l) == level);
            foreach (var way in new[] { "float", "packed" })
            {
                MushroomBuilder.FloatMeshes = way == "float";
                try
                {
                    MushroomBuilder.Build(GlbsOf(Models()));
                }
                finally
                {
                    MushroomBuilder.FloatMeshes = false;
                }
                AssetDatabase.SaveAssets();
                SdfLevel.Editor.LevelBuilder.Build(scene);
                Directory.CreateDirectory("Temp");
                File.WriteAllLines(SdfLevel.Editor.LevelSnapshot.RequestPath, new[] { Path.Combine(directory, way), level });
                SdfLevel.Editor.LevelSnapshot.CaptureRequested();
                Debug.Log($"SDF packed meshes: {way} snapshots of {level} in {Path.Combine(directory, way)}");
                Report();
            }
        }

        public static void Report() => ReportModels(Models());

        /// <summary>Reports, rebuilds and reports again: what the meshes of the models were, and are.</summary>
        public static void RebuildAndReport()
        {
            Debug.Log("SDF packed meshes: BEFORE");
            Report();
            Rebuild();
            Debug.Log("SDF packed meshes: AFTER");
            Report();
        }

        private static void ReportModels(string[] names)
        {
            foreach (var name in names)
            {
                var glb = GlbsOf(new[] { name }).FirstOrDefault();
                var prefab = glb == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(MushroomBuilder.PrefabPathOf(glb));
                if (prefab == null)
                {
                    Debug.Log($"SDF packed meshes: {name}: no prefab");
                    continue;
                }
                var meshes = prefab.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh)
                    .Concat(prefab.GetComponentsInChildren<MeshCollider>(true).Select(c => c.sharedMesh))
                    .Where(m => m != null).Distinct().ToArray();
                long vertices = 0, vertexBytes = 0, indexBytes = 0, colliderBytes = 0;
                var first = true;
                foreach (var mesh in meshes)
                {
                    var stride = mesh.GetVertexBufferStride(0);
                    var bytes = (long)mesh.vertexCount * stride;
                    var index = (long)mesh.GetIndexCount(0) * (mesh.indexFormat == IndexFormat.UInt16 ? 2 : 4);
                    if (AssetDatabase.GetAssetPath(mesh).EndsWith("_collider.asset"))
                        colliderBytes += bytes + index;
                    else
                    {
                        vertices += mesh.vertexCount;
                        vertexBytes += bytes;
                        indexBytes += index;
                    }
                    if (first && !AssetDatabase.GetAssetPath(mesh).EndsWith("_collider.asset"))
                    {
                        first = false;
                        Debug.Log($"SDF packed meshes: {name} e.g. {mesh.name}: {mesh.vertexCount} vertices, stride {stride}, " +
                                  string.Join(" ", mesh.GetVertexAttributes().Select(a => $"{a.attribute}:{a.format}x{a.dimension}")) +
                                  $", packed {SdfPackedMesh.IsPacked(mesh)}, index {mesh.indexFormat}, bounds {mesh.bounds}");
                    }
                }
                Debug.Log($"SDF packed meshes: {name}: {meshes.Length} meshes, {vertices:N0} vertices, vertex data {vertexBytes / 1e6:0.000} MB " +
                          $"({(vertices > 0 ? vertexBytes / (double)vertices : 0):0.0} bytes a vertex), index data {indexBytes / 1e6:0.000} MB, colliders {colliderBytes / 1e6:0.000} MB, " +
                          $"total {(vertexBytes + indexBytes + colliderBytes) / 1e6:0.000} MB");
            }
        }
    }
}
