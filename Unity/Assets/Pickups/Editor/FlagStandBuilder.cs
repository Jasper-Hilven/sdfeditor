using System.Collections.Generic;
using System.IO;
using System.Linq;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEngine;

namespace SdfPickups.Editor
{
    /// <summary>
    /// Builds the two flag stand prefabs, Assets/Pickups/Flags/flag-stand-west.prefab and flag-stand-east.prefab, from the prefabs of
    /// content/models/pickups/flag-stand.sdf and the side's flag (flag-red.sdf for the west, flag-gold.sdf for the east, the colours of
    /// the amanita and chanterelle scouts). A flag stand prefab reads, from the root down:
    /// <code>
    /// flag-stand-west     FlagStand (side, the Flag child, its home pose)
    ///   Stand             the flag-stand prefab: the 25 cm base with its convex collider, to walk onto, and the socket
    ///   Flag              the flag's pivot, the pole's foot in the bottom of the socket; carried off by a player later
    ///     Model           the flag's prefab (pole up +y, the cloth flying towards +x)
    /// </code>
    /// A stand is rebuilt when it is missing, older than the stand's or its flag's prefab, or built by an older version of this code;
    /// that is checked when the editor loads and whenever <see cref="MushroomBuilder"/> built prefabs. By hand: Tools > SDF Pickups >
    /// Rebuild Flag Stands. In a batch:
    /// <c>Unity -batchmode -quit -projectPath Unity -executeMethod SdfPickups.Editor.FlagStandBuilder.BuildAllInBatch</c> (the level
    /// builder's batch method does it too).
    /// </summary>
    public static class FlagStandBuilder
    {
        public const string FlagFolder = "Assets/Pickups/Flags";
        public const string StandModel = "flag-stand";

        /// <summary>Raise when the way stands are built changes, so every stand is rebuilt when the editor loads.</summary>
        public const int BuilderVersion = 1;

        /// <summary>The stand's height (the top of the socket collar) if its mesh is not there to measure.</summary>
        public const float DefaultStandHeight = 0.3f;

        /// <summary>How far the socket's hole goes down from the top of its collar: the pole's foot stands this far under it.</summary>
        public const float SocketDepth = 0.15f;

        /// <summary>Each side and the model of its flag.</summary>
        public static readonly IReadOnlyList<(BattleSideId side, string flagModel)> Sides = new[]
        {
            (BattleSideId.West, "flag-red"),
            (BattleSideId.East, "flag-gold"),
        };

        private static string Stamp => $"sdf-flag-stand {BuilderVersion}";

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            MushroomBuilder.Built += _ => EditorApplication.delayCall += BuildStale;
            EditorApplication.delayCall += BuildStale;
        }

        public static string ModelPrefabOf(string model) => $"{MushroomBuilder.PrefabFolder}/{model}.prefab";

        public static string StandPrefabOf(BattleSideId side) => $"{FlagFolder}/flag-stand-{FlagStand.SideNameOf(side)}.prefab";

        [MenuItem("Tools/SDF Pickups/Rebuild Flag Stands")]
        public static void BuildAll() => Build(Sides.Select(entry => entry.side).ToArray());

        /// <summary>For batch mode: both stands, after the prefabs of models meshed since the last build (the stand and the flags).</summary>
        public static void BuildAllInBatch()
        {
            MushroomBuilder.BuildStale();
            BuildAll();
        }

        public static void BuildStale() => Build(Sides.Where(entry => IsStale(entry.side, entry.flagModel)).Select(entry => entry.side).ToArray());

        private static bool IsStale(BattleSideId side, string flagModel)
        {
            var path = StandPrefabOf(side);
            if (!File.Exists(path) || AssetImporter.GetAtPath(path)?.userData != Stamp)
                return true;
            var built = File.GetLastWriteTimeUtc(path);
            return new[] { ModelPrefabOf(StandModel), ModelPrefabOf(flagModel) }
                .Any(source => File.Exists(source) && File.GetLastWriteTimeUtc(source) > built);
        }

        public static void Build(IReadOnlyList<BattleSideId> sides)
        {
            if (sides.Count == 0)
                return;
            var stand = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabOf(StandModel));
            if (stand == null)
            {
                Debug.LogWarning($"SDF pickups: no flag stand prefab at {ModelPrefabOf(StandModel)} yet (run sdf unity), no flag stands built");
                return;
            }
            Directory.CreateDirectory(FlagFolder);
            var built = new List<string>();
            foreach (var side in sides)
            {
                var flagModel = Sides.First(entry => entry.side == side).flagModel;
                var flag = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabOf(flagModel));
                if (flag == null)
                {
                    Debug.LogWarning($"SDF pickups: no flag prefab at {ModelPrefabOf(flagModel)} yet (run sdf unity), the {side} stand not built");
                    continue;
                }
                try
                {
                    built.Add(BuildOne(side, stand, flag));
                }
                catch (System.Exception exception)
                {
                    Debug.LogError($"SDF pickups: could not build the {side} flag stand: {exception.Message}\n{exception.StackTrace}");
                }
            }
            AssetDatabase.SaveAssets();
            if (built.Count > 0)
                Debug.Log($"SDF pickups: built {string.Join(", ", built)}");
        }

        private static string BuildOne(BattleSideId side, GameObject standPrefab, GameObject flagPrefab)
        {
            var root = new GameObject($"flag-stand-{FlagStand.SideNameOf(side)}");
            try
            {
                var stand = (GameObject)PrefabUtility.InstantiatePrefab(standPrefab, root.transform);
                stand.name = "Stand";
                var standTop = Measure(stand) is { } standBounds ? standBounds.max.y : DefaultStandHeight;

                // The flag's pivot, the pole's foot in the bottom of the socket, so carrying it moves the pivot alone
                var flag = new GameObject("Flag");
                flag.transform.SetParent(root.transform, false);
                flag.transform.localPosition = new Vector3(0, standTop - SocketDepth, 0);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(flagPrefab, flag.transform);
                model.name = "Model";

                var flagStand = root.AddComponent<FlagStand>();
                flagStand.side = side;
                flagStand.flag = flag.transform;
                flagStand.homePosition = flag.transform.localPosition;
                flagStand.homeRotation = flag.transform.localRotation;

                var path = StandPrefabOf(side);
                PrefabUtility.SaveAsPrefabAsset(root, path, out var saved);
                if (!saved)
                    throw new System.InvalidOperationException($"Could not save {path}");
                var importer = AssetImporter.GetAtPath(path);
                if (importer != null && importer.userData != Stamp)
                {
                    importer.userData = Stamp;
                    importer.SaveAndReimport();
                }
                return path;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>The bounds of the object's meshes (not the ink outline's copies) in its parent's space, or null if it has none.</summary>
        private static Bounds? Measure(GameObject owner)
        {
            Bounds? bounds = null;
            var toParent = owner.transform.parent != null ? owner.transform.parent.worldToLocalMatrix : Matrix4x4.identity;
            foreach (var filter in owner.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || filter.name == MushroomBuilder.OutlineObjectName)
                    continue;
                var local = filter.sharedMesh.bounds;
                var matrix = toParent * filter.transform.localToWorldMatrix;
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = matrix.MultiplyPoint3x4(new Vector3(
                        (corner & 1) == 0 ? local.min.x : local.max.x, (corner & 2) == 0 ? local.min.y : local.max.y, (corner & 4) == 0 ? local.min.z : local.max.z));
                    if (bounds is { } b)
                    {
                        b.Encapsulate(point);
                        bounds = b;
                    }
                    else
                        bounds = new Bounds(point, Vector3.zero);
                }
            }
            return bounds;
        }
    }
}
