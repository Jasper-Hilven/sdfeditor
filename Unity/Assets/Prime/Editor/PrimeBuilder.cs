using System.Collections.Generic;
using System.IO;
using System.Linq;
using SdfMushrooms.Editor;
using UnityEditor;
using UnityEngine;

namespace SdfPrime.Editor
{
    /// <summary>
    /// Builds the Chanterelle Prime prefab (ticket 33.1), Assets/Prime/Prefabs/chanterelle-prime.prefab, from the prefabs of his pieces,
    /// the way MachineBuilder builds machines: one model per moving piece, each meshed with its origin at its pivot, each under an empty
    /// pivot object placed where the piece hinges on its parent, with the piece's prefab under that as "Model". From the root down:
    /// <code>
    /// chanterelle-prime   PrimeRig
    ///   Stem              at the root's origin (on the ground, under the middle)
    ///     Model           prime-stem prefab, with the scouts' eyes (eye.0, eye.1) and hands (hand.0, hand.1)
    ///     Cap             (0, 0.9216, 0) in the stem
    ///       Model         prime-cap prefab
    ///       Crown         (0, 0.187, 0) in the cap
    ///         Model       prime-crown prefab (content/models/home/, the story's crown)
    ///       Cape          (0, 0.338, -0.432) in the cap, x negated as the meshes are: no change, they are on the axis
    ///         Model       prime-cape prefab
    ///     Medal           (0, 0.662, 0.112) in the stem
    ///       Model         prime-medal prefab
    /// </code>
    /// The pivots are those of the files' headers and of SDFPipeline.Tests PrimeTests. It is a prefab like any model: not scaled,
    /// 1.41 m tall; the boss scene scales the root by 4.6.
    /// <para>
    /// The prefab is rebuilt when it is missing, older than one of its pieces' prefabs, or built by an older version of this code; that
    /// is checked when the editor loads and whenever <see cref="MushroomBuilder"/> built prefabs. By hand: Tools > SDF Prime > Rebuild
    /// Prime. In a batch:
    /// <c>Unity -batchmode -quit -projectPath Unity -executeMethod SdfPrime.Editor.PrimeBuilder.BuildInBatch</c>.
    /// </para>
    /// </summary>
    public static class PrimeBuilder
    {
        public const string PrefabFolder = "Assets/Prime/Prefabs";
        public const string PrefabName = "chanterelle-prime";

        /// <summary>Raise when the way Prime is built changes, so he is rebuilt when the editor loads.</summary>
        public const int BuilderVersion = 1;

        /// <summary>A moving piece: its pivot object's name, the model prefab's name, its pivot in the parent's space (Unity's), its children.</summary>
        public sealed class Piece
        {
            public readonly string Name, Model;
            public readonly Vector3 Pivot;
            public readonly Piece[] Children;

            public Piece(string name, string model, Vector3 pivot, params Piece[] children)
            {
                Name = name;
                Model = model;
                Pivot = pivot;
                Children = children;
            }

            public IEnumerable<Piece> All() => Children.SelectMany(c => c.All()).Prepend(this);
        }

        public static readonly Piece Root =
            new("Stem", "prime-stem", Vector3.zero,
                new Piece("Cap", "prime-cap", new Vector3(0, 0.9216f, 0),
                    new Piece("Crown", "prime-crown", new Vector3(0, 0.187f, 0)),
                    new Piece("Cape", "prime-cape", new Vector3(0, 0.338f, -0.432f))),
                new Piece("Medal", "prime-medal", new Vector3(0, 0.662f, 0.112f)));

        private static string Stamp => $"sdf-prime {BuilderVersion}";

        public static string PrefabPath => $"{PrefabFolder}/{PrefabName}.prefab";

        public static string ModelPrefabOf(string model) => $"{MushroomBuilder.PrefabFolder}/{model}.prefab";

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            MushroomBuilder.Built += _ => EditorApplication.delayCall += BuildStale;
            EditorApplication.delayCall += BuildStale;
        }

        [MenuItem("Tools/SDF Prime/Rebuild Prime")]
        public static void BuildAll() => Build();

        /// <summary>For batch mode: Prime, after the prefabs of models meshed since the last build.</summary>
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
            return Root.All().Select(p => ModelPrefabOf(p.Model)).Any(source => File.Exists(source) && File.GetLastWriteTimeUtc(source) > built);
        }

        public static void Build()
        {
            var missing = Root.All().Select(p => ModelPrefabOf(p.Model)).FirstOrDefault(path => AssetDatabase.LoadAssetAtPath<GameObject>(path) == null);
            if (missing != null)
            {
                Debug.LogWarning($"SDF prime: no prefab at {missing} yet (run sdf mesh on content/models/characters/prime and home/prime-crown), {PrefabName} not built");
                return;
            }
            try
            {
                Directory.CreateDirectory(PrefabFolder);
                BuildOne();
                AssetDatabase.SaveAssets();
                Debug.Log($"SDF prime: built {PrefabPath}");
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"SDF prime: could not build {PrefabName}: {exception.Message}\n{exception.StackTrace}");
            }
        }

        private static void BuildOne()
        {
            var root = new GameObject(PrefabName);
            try
            {
                var pivots = new Dictionary<string, Transform>();
                void Add(Piece piece, Transform parent)
                {
                    var pivot = new GameObject(piece.Name).transform;
                    pivot.SetParent(parent, false);
                    pivot.localPosition = piece.Pivot;
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabOf(piece.Model));
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, pivot);
                    model.name = "Model";
                    pivots[piece.Name] = pivot;
                    foreach (var child in piece.Children)
                        Add(child, pivot);
                }
                Add(Root, root.transform);

                var rig = root.AddComponent<PrimeRig>();
                rig.stem = pivots["Stem"];
                rig.cap = pivots["Cap"];
                rig.crown = pivots["Crown"];
                rig.cape = pivots["Cape"];
                rig.medal = pivots["Medal"];
                // The eyes and hands are the part uses in the stem's model, not under its child pieces
                var stemModel = pivots["Stem"].Find("Model");
                rig.eyes = new[] { "eye.0", "eye.1" }.Select(name => stemModel.Find(name)).Where(t => t != null).ToArray();
                rig.hands = new[] { "hand.0", "hand.1" }.Select(name => stemModel.Find(name)).Where(t => t != null).ToArray();
                if (rig.eyes.Length != 2 || rig.hands.Length != 2)
                    Debug.LogWarning($"SDF prime: {PrefabName} has {rig.eyes.Length} eyes and {rig.hands.Length} hands under the stem's model, not 2 and 2");

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
