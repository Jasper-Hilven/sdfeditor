using System.Collections.Generic;
using System.IO;
using System.Linq;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEngine;

namespace SdfMachines.Editor
{
    /// <summary>
    /// Builds the Prime-copter prefab (ticket 33.5), Assets/Machines/Prefabs/primecopter.prefab, the way <see cref="MachineBuilder"/>
    /// builds a machine: one model per piece (content/models/machines/primecopter-*.sdf, 24's blue helicopter at seven times), each
    /// meshed with its origin at its pivot, each under an empty pivot object placed where the piece hangs from its parent, with the
    /// piece's prefab under that as "Model". From the root down:
    /// <code>
    /// primecopter         PrimeCopterRig
    ///   Body              at the root's origin (on the ground under the middle, the runners' underside), a sphere collider over the puffball
    ///     Model           primecopter-body prefab, with the eyes eye_machine.0 and eye_machine.1
    ///     Rotor           (0, 3.92, 0) in the body: spins about y
    ///     TailRotor       (-0.175, 2.8, -2.15): spins about x (the .sdf's +x side is -x in Unity, as for every mesh)
    ///     Sac0..Sac3      (+-1.25, 1.3, +-1.25): hang from the rim's sockets, their tops
    ///     GripLeft, GripRight   (-+2.1, 0.17, 0.1): the runners' middles, where Prime's hands hold
    ///     Hatch           (0, 2.15, -1.9): under the tail
    ///     Muzzle          (0, 1.4, 1.5): under the nose
    /// </code>
    /// The pivots are those of the files' headers and of SDFPipeline.Tests PrimeCopterTests. The prefab is at its boss size (5 m), not
    /// scaled.
    /// <para>
    /// The prefab is rebuilt when it is missing, older than one of its pieces' prefabs, or built by an older version of this code; that
    /// is checked when the editor loads and whenever <see cref="MushroomBuilder"/> built prefabs. By hand: Tools > SDF Machines >
    /// Rebuild Prime-copter. In a batch:
    /// <c>Unity -batchmode -quit -projectPath Unity -executeMethod SdfMachines.Editor.PrimeCopterBuilder.BuildInBatch</c>.
    /// </para>
    /// </summary>
    public static class PrimeCopterBuilder
    {
        public const string PrefabName = "primecopter";

        /// <summary>Raise when the way the copter is built changes, so it is rebuilt when the editor loads.</summary>
        public const int BuilderVersion = 1;

        /// <summary>The four sacs' places under the rim (Unity's space, in the body).</summary>
        public static readonly Vector3[] SacPivots =
        {
            new Vector3(-1.25f, 1.3f, 1.25f), new Vector3(1.25f, 1.3f, 1.25f), new Vector3(-1.25f, 1.3f, -1.25f), new Vector3(1.25f, 1.3f, -1.25f),
        };

        /// <summary>A piece: its pivot object's name, the model prefab's name, its pivot in the body's space (Unity's).</summary>
        public readonly struct Piece
        {
            public readonly string Name, Model;
            public readonly Vector3 Pivot;

            public Piece(string name, string model, Vector3 pivot)
            {
                Name = name;
                Model = model;
                Pivot = pivot;
            }
        }

        public static readonly Piece Body = new Piece("Body", "primecopter-body", Vector3.zero);

        public static readonly Piece[] Parts =
        {
            new Piece("Rotor", "primecopter-rotor", new Vector3(0, 3.92f, 0)),
            new Piece("TailRotor", "primecopter-tail-rotor", new Vector3(-0.175f, 2.8f, -2.15f)),
            new Piece("Sac0", "primecopter-sac", SacPivots[0]),
            new Piece("Sac1", "primecopter-sac", SacPivots[1]),
            new Piece("Sac2", "primecopter-sac", SacPivots[2]),
            new Piece("Sac3", "primecopter-sac", SacPivots[3]),
        };

        private static string Stamp => $"sdf-primecopter {BuilderVersion}";

        public static string PrefabPath => $"{MachineBuilder.PrefabFolder}/{PrefabName}.prefab";

        public static string ModelPrefabOf(string model) => $"{MushroomBuilder.PrefabFolder}/{model}.prefab";

        private static IEnumerable<string> Models() => Parts.Prepend(Body).Select(p => p.Model).Distinct();

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            MushroomBuilder.Built += _ => EditorApplication.delayCall += BuildStale;
            EditorApplication.delayCall += BuildStale;
        }

        [MenuItem("Tools/SDF Machines/Rebuild Prime-copter")]
        public static void BuildAll() => Build();

        /// <summary>For batch mode: the copter, after the prefabs of models meshed since the last build.</summary>
        public static void BuildInBatch()
        {
            MushroomBuilder.BuildStale();
            Build();
        }

        public static void BuildStale()
        {
            if (IsStale())
                Build();
        }

        private static bool IsStale()
        {
            if (!File.Exists(PrefabPath) || AssetImporter.GetAtPath(PrefabPath)?.userData != Stamp)
                return true;
            var built = File.GetLastWriteTimeUtc(PrefabPath);
            return Models().Select(ModelPrefabOf).Any(source => File.Exists(source) && File.GetLastWriteTimeUtc(source) > built);
        }

        public static void Build()
        {
            var missing = Models().Select(ModelPrefabOf).FirstOrDefault(path => AssetDatabase.LoadAssetAtPath<GameObject>(path) == null);
            if (missing != null)
            {
                Debug.LogWarning($"SDF machines: no prefab at {missing} yet (run sdf mesh on content/models/machines), {PrefabName} not built");
                return;
            }
            try
            {
                Directory.CreateDirectory(MachineBuilder.PrefabFolder);
                BuildOne();
                AssetDatabase.SaveAssets();
                Debug.Log($"SDF machines: built {PrefabPath}");
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"SDF machines: could not build {PrefabName}: {exception.Message}\n{exception.StackTrace}");
            }
        }

        private static Transform AddPivot(Transform parent, Piece piece)
        {
            var pivot = new GameObject(piece.Name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = piece.Pivot;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabOf(piece.Model));
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, pivot);
            model.name = "Model";
            return pivot;
        }

        private static Transform AddMarker(Transform parent, string name, Vector3 position)
        {
            var marker = new GameObject(name).transform;
            marker.SetParent(parent, false);
            marker.localPosition = position;
            return marker;
        }

        private static void BuildOne()
        {
            var root = new GameObject(PrefabName);
            try
            {
                var body = AddPivot(root.transform, Body);
                var parts = Parts.ToDictionary(p => p.Name, p => AddPivot(body, p));

                // Shots that miss a sac stop on the puffball: a sphere over it, as the model has no collider (a convex hull of it
                // would cover the sacs under the rim)
                var sphere = body.gameObject.AddComponent<SphereCollider>();
                sphere.center = new Vector3(0, 2.1f, 0);
                sphere.radius = 1.4f;

                var rig = root.AddComponent<PrimeCopterRig>();
                rig.body = body;
                rig.rotor = parts["Rotor"];
                rig.tailRotor = parts["TailRotor"];
                rig.sacs = new[] { parts["Sac0"], parts["Sac1"], parts["Sac2"], parts["Sac3"] };
                rig.gripLeft = AddMarker(body, "GripLeft", new Vector3(-2.1f, 0.17f, 0.1f));
                rig.gripRight = AddMarker(body, "GripRight", new Vector3(2.1f, 0.17f, 0.1f));
                rig.hatch = AddMarker(body, "Hatch", new Vector3(0, 2.15f, -1.9f));
                rig.muzzle = AddMarker(body, "Muzzle", new Vector3(0, 1.4f, 1.5f));
                // The eyes are the eye part's uses in the body's model, not under its other pieces
                var bodyModel = body.Find("Model");
                rig.eyes = new[] { "eye_machine.0", "eye_machine.1" }.Select(name => bodyModel.Find(name)).Where(eye => eye != null).ToArray();
                if (rig.eyes.Length != 2)
                    Debug.LogWarning($"SDF machines: {PrefabName} has {rig.eyes.Length} eyes under its body, not 2");

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out var saved);
                if (!saved)
                    throw new System.InvalidOperationException($"Could not save {PrefabPath}");
                var importer = AssetImporter.GetAtPath(PrefabPath);
                if (importer != null && importer.userData != Stamp)
                {
                    importer.userData = Stamp;
                    importer.SaveAndReimport();
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
